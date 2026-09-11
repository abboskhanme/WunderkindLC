using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// YUZ BILAN KIRISH — UCHTA ZAIFLIKNI YOPADIGAN QATLAMLAR.
///
/// <list type="number">
///   <item><b>Soxta vektor</b> (o'zgartirilgan APK) → bir martalik NONCE + ilova butunligi
///     (<see cref="AppAttestation"/>);</item>
///   <item><b>Tiriklik</b> → server so'ragan TASODIFIY harakatlar va ularning O'LCHANGAN
///     qiymatlari (<see cref="FaceLiveness"/>);</item>
///   <item><b>Zaxira nusxadagi biometrika</b> → vektorlar bazada SHIFRLANGAN
///     (<see cref="FaceVault"/>) va selfilar <c>uploads/face/</c> da (zaxiradan chiqarilgan,
///     statik yo'l bilan berilmaydi).</item>
/// </list>
///
/// <para><b>NEGA CONTROLLER TESTLARI YO'Q:</b> <c>WunderkindLC.Tests</c> loyihasi
/// <c>WunderkindLC.Server</c> ga referens QILMAYDI. Shu sabab controller/pipeline darvozalari
/// MANBA MATNIDAN tekshiriladi (<c>FaceLoginTests</c> dagi bilan bir xil usul).</para>
/// </summary>
public class FaceSecurityTests
{
    /* =============================================================================================
     *  Yordamchilar
     * ========================================================================================== */

    private static float[] Vec(int seed, int dim = 128)
    {
        var rnd = new Random(seed);
        var v = new float[dim];
        for (var i = 0; i < dim; i++) v[i] = (float)(rnd.NextDouble() * 2 - 1);
        return FaceMatch.Normalize(v);
    }

    private static float[] Near(float[] source, double noise, int seed = 7)
    {
        var rnd = new Random(seed);
        var v = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
            v[i] = (float)(source[i] + (rnd.NextDouble() * 2 - 1) * noise);
        return FaceMatch.Normalize(v);
    }

    /// <summary>Yaxshi kadr sifati (XOM birliklarda) — sifat darvozasi yo'lni to'smasin.</summary>
    private const string GoodQuality =
        """{"faces":1,"sharpness":531,"brightness":128,"faceRatio":0.32,"yaw":0,"roll":0}""";

    private const double BaselineRatio = 0.32;

    private static (TestDb Db, FaceLoginService Service, FaceVault Vault) NewService(
        bool liveness = true, bool attestation = false, FaceVault? vault = null)
    {
        var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta
        {
            LoginFaceEnabled = true,
            LoginFaceThreshold = 0.60,
            LoginFaceModelVersion = "m1",
            LoginFaceKeepChecks = 20,
            LoginFaceRequireLiveness = liveness,
            LoginFaceRequireAttestation = attestation,
        });
        db.Context.SaveChanges();
        var v = vault ?? new FaceVault(FaceVault.GenerateKey());
        return (db, new FaceLoginService(db.Context, v), v);
    }

    // ⚠️ `login` PARAMETR: AppUser.Email unikal — bitta testda ikkita o'quvchi kerak bo'lganda
    // (masalan "begona nonce") bir xil login SQLite'da xato berardi.
    private static (AppUser User, Student Student) AddStudent(
        TestDb db, string? photo = "/uploads/foto.jpg", string login = "ali")
    {
        var user = new AppUser { FullName = "Ali Valiyev", Role = Roles.Student, Email = login };
        db.Context.Users.Add(user);
        var student = new Student { FullName = "Ali Valiyev", UserId = user.Id, BirthCertificateUrl = photo };
        db.Context.Students.Add(student);
        db.Context.SaveChanges();
        return (user, student);
    }

    private static FaceLoginService.VerifyRequest Req(
        AppUser user, Student student, float[] selfie, float[]? reference = null,
        string nonce = "", string liveness = "",
        AppAttestation.Verdict attest = AppAttestation.Verdict.NotConfigured) =>
        new(student, user.Id, "/uploads/face/selfi.jpg", selfie, reference, GoodQuality, "m1",
            "dev-1", "Pixel", "android", "1.0", "1.2.3.4",
            Nonce: nonce, LivenessJson: liveness, AttestVerdict: attest);

    /// <summary>So'ralgan harakatlarga TO'G'RI javob yasaydi (o'lchangan qiymatlar bilan).</summary>
    private static string GoodLiveness(IReadOnlyList<string> actions, double baseline = BaselineRatio)
    {
        var parts = actions.Select(a => a switch
        {
            FaceLiveness.ActionTurnLeft => $$"""{"action":"turn_left","ok":true,"ms":1400,"value":-27.5}""",
            FaceLiveness.ActionTurnRight => $$"""{"action":"turn_right","ok":true,"ms":1300,"value":24.0}""",
            FaceLiveness.ActionMoveCloser =>
                $$"""{"action":"move_closer","ok":true,"ms":900,"value":{{(baseline * 1.6).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}}}""",
            _ =>
                $$"""{"action":"move_back","ok":true,"ms":950,"value":{{(baseline * 0.5).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}}}""",
        });
        return "[" + string.Join(",", parts) + "]";
    }

    /* =============================================================================================
     *  1. SEYF (FaceVault) — vektorlar bazada shifrlangan
     * ========================================================================================== */

    [Fact]
    public void Vault_shifrlash_ochish_aylanishi_vektorni_aynan_tiklaydi()
    {
        var vault = new FaceVault(FaceVault.GenerateKey());
        var v = Vec(1);

        var blob = vault.Protect(v);
        var back = vault.Unprotect(blob);

        Assert.NotNull(back);
        Assert.Equal(v.Length, back!.Length);
        for (var i = 0; i < v.Length; i++) Assert.Equal(v[i], back[i]);
    }

    /// <summary>Shifrlangan blob OCHIQ vektorga o'xshamasligi kerak (aks holda shifr yo'q demakdir).</summary>
    [Fact]
    public void Vault_blob_ochiq_vektor_baytlaridan_farq_qiladi()
    {
        var vault = new FaceVault(FaceVault.GenerateKey());
        var v = Vec(2);

        var blob = vault.Protect(v);
        var plain = FaceMatch.Encode(v);

        Assert.NotEqual(plain.Length, blob.Length);          // versiya + nonce + teg qo'shiladi
        Assert.False(blob.AsSpan().StartsWith(plain));
        Assert.Equal(FaceVault.FormatVersion, blob[0]);
        // Har safar yangi nonce — bir xil vektor ikki xil blob beradi (naqsh ko'rinmasin).
        Assert.NotEqual(Convert.ToBase64String(blob), Convert.ToBase64String(vault.Protect(v)));
    }

    [Fact]
    public void Vault_buzuq_blob_null_qaytaradi_istisno_EMAS()
    {
        var vault = new FaceVault(FaceVault.GenerateKey());

        Assert.Null(vault.Unprotect(null));
        Assert.Null(vault.Unprotect([]));
        Assert.Null(vault.Unprotect([1, 2, 3]));                       // juda qisqa
        Assert.Null(vault.Unprotect(new byte[64]));                    // versiya bayti 0
        // Shifrmatn buzilgan — GCM tegi mos kelmaydi.
        var blob = vault.Protect(Vec(3));
        blob[^1] ^= 0xFF;
        Assert.Null(vault.Unprotect(blob));
        // ESKI (shifrlanmagan) qator ham "ochilmadi" bo'lib qaytadi, istisno tashlamaydi.
        Assert.Null(vault.Unprotect(FaceMatch.Encode(Vec(4))));
    }

    [Fact]
    public void Vault_boshqa_kalit_bilan_ochilmaydi()
    {
        var a = new FaceVault(FaceVault.GenerateKey());
        var b = new FaceVault(FaceVault.GenerateKey());

        Assert.Null(b.Unprotect(a.Protect(Vec(5))));
    }

    [Fact]
    public void Vault_kalit_yoq_yoki_notogri_uzunlikda_sozlanmagan_boladi()
    {
        Assert.False(new FaceVault(null).Configured);
        Assert.False(new FaceVault("").Configured);
        Assert.False(new FaceVault("bu base64 emas!!!").Configured);
        Assert.False(new FaceVault(Convert.ToBase64String(new byte[16])).Configured);   // 16 bayt
        Assert.True(new FaceVault(FaceVault.GenerateKey()).Configured);

        Assert.False(FaceVault.IsValidKey(""));
        Assert.True(FaceVault.IsValidKey(FaceVault.GenerateKey()));
    }

    /// <summary>⚠️ Kalit yo'q → MODUL O'CHIQ. "Jimgina ochiq saqlash" yo'li BO'LMASLIGI kerak.</summary>
    [Fact]
    public async Task Kalit_sozlanmagan_bolsa_modul_ochiq_va_login_ozgarmaydi()
    {
        var (db, svc, _) = NewService(vault: new FaceVault(null));
        using var _d = db;
        var (user, _) = AddStudent(db);

        var settings = await svc.SettingsAsync();
        Assert.False(settings.Enabled);          // amalda ishlamaydi
        Assert.True(settings.EnabledSetting);    // ...lekin sozlama YOQILGAN (UI shuni ko'rsatadi)
        Assert.False(settings.VaultReady);

        // Yangi qurilmada ham selfi so'ralmaydi — o'quvchilar qulflanib qolmasin.
        Assert.False((await svc.DecideAsync(user, "yangi-qurilma")).Required);
    }

    /// <summary>Kalit ALMASHSA eski etalon ochilmaydi → "etalon yo'q" (istisno emas):
    /// o'quvchi profil rasmi orqali QAYTA ro'yxatdan o'tadi.</summary>
    [Fact]
    public async Task Kalit_almashsa_eski_etalon_ochilmaydi_va_qayta_royxat_boladi()
    {
        var db = TestDb.Sqlite();
        using var _d = db;
        db.Context.CenterMeta.Add(new CenterMeta
        {
            LoginFaceEnabled = true, LoginFaceThreshold = 0.60,
            LoginFaceModelVersion = "m1", LoginFaceKeepChecks = 20,
            LoginFaceRequireLiveness = false,
        });
        db.Context.SaveChanges();
        var (user, student) = AddStudent(db);

        var profil = Vec(11);
        var eski = new FaceLoginService(db.Context, new FaceVault(FaceVault.GenerateKey()));
        Assert.True((await eski.VerifyAsync(Req(user, student, Near(profil, 0.05), profil))).Ok);
        Assert.True(await db.Context.StudentFaceProfiles.AnyAsync(p => p.StudentId == student.Id));

        // Kalit almashdi (yangi server / .env yangilangan).
        var yangi = new FaceLoginService(db.Context, new FaceVault(FaceVault.GenerateKey()));

        // Etalon O'QILMAYDI: refVector'siz solishtiradigan narsa qolmaydi → pending (istisno EMAS).
        var r = await yangi.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 21)));
        Assert.Equal(FaceLoginService.StatusPending, r.Status);

        // Profil rasmi bilan esa qayta ro'yxatdan o'tadi va etalon YANGI kalit bilan yoziladi.
        var again = await yangi.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 22), profil));
        Assert.True(again.Ok);
        Assert.True(again.Enrolled);
    }

    /// <summary>Bazadagi etalon va urinish vektorlari HAR IKKALASI ham shifrlangan bo'lishi shart.</summary>
    [Fact]
    public async Task Bazadagi_vektorlar_shifrlangan_holda_yotadi()
    {
        var (db, svc, vault) = NewService(liveness: false);
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(31);
        var selfie = Near(profil, 0.05);

        Assert.True((await svc.VerifyAsync(Req(user, student, selfie, profil))).Ok);

        var saved = await db.Context.StudentFaceProfiles.FirstAsync(p => p.StudentId == student.Id);
        Assert.Equal(FaceVault.FormatVersion, saved.Vector[0]);
        Assert.NotEqual(FaceMatch.Encode(selfie).Length, saved.Vector.Length);
        Assert.NotNull(vault.Unprotect(saved.Vector));

        // `pending` urinish vektori ham shifrlangan (rasmi yo'q o'quvchi).
        var (user2, student2) = AddStudent(db, photo: null, login: "vali");
        await svc.VerifyAsync(Req(user2, student2, Vec(32)));
        var check = await db.Context.LoginFaceChecks.FirstAsync(c => c.StudentId == student2.Id);
        Assert.NotNull(check.Vector);
        Assert.Equal(FaceVault.FormatVersion, check.Vector![0]);
        Assert.NotNull(vault.Unprotect(check.Vector));
    }

    /* =============================================================================================
     *  2. NONCE (bir martalik chaqiruv)
     * ========================================================================================== */

    [Fact]
    public async Task Challenge_ikkita_tasodifiy_harakat_va_muddat_beradi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);

        Assert.True(ch.Ok);
        Assert.NotEmpty(ch.Nonce);
        Assert.Equal(FaceLiveness.ActionCount, ch.Actions.Count);
        Assert.Equal(ch.Actions.Count, ch.Actions.Distinct().Count());   // takrorlanmaydi
        Assert.All(ch.Actions, a => Assert.Contains(a, FaceLiveness.All));
        Assert.True(string.CompareOrdinal(ch.ExpiresAt, AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss")) > 0);
    }

    [Fact]
    public async Task Nonce_bir_marta_ishlatiladi_ikkinchisida_rad()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(41);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);
        var ok = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil,
            ch.Nonce, GoodLiveness(ch.Actions)));
        Assert.True(ok.Ok);

        // AYNAN o'sha nonce bilan takroriy so'rov (replay).
        var again = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 21), profil,
            ch.Nonce, GoodLiveness(ch.Actions)));
        Assert.False(again.Ok);
        Assert.Equal(FaceMatch.ReasonNoChallenge, again.Reason);
    }

    /// <summary>
    /// PARALLEL ikki so'rov AYNI nonce bilan — faqat BITTASI o'tishi kerak.
    ///
    /// <para>⚠️ Ketma-ket takrorni (yuqoridagi test) <c>UsedAt</c> ni o'qish yetarli to'sadi, PARALLEL
    /// so'rovni esa YO'Q: nonce'ni "ishlatilgan" deb belgilash bilan saqlash oralig'ida <c>await</c>
    /// bor, ya'ni ikkala so'rov ham "ishlatilmagan" ni ko'rib o'tib ketardi. Bu bir marta yozib
    /// olingan tiriklik sessiyasini IKKI marta ishlatish yo'li edi. Himoya —
    /// <c>FaceChallenge.UsedAt</c> konkurentlik tokeni (<c>AppDbContext</c>).</para>
    ///
    /// <para>Ikki kontekst = ikki so'rov (`TestDb.NewContext`): birinchisi nonce'ni bandlab
    /// saqlaydi, ikkinchisi esa uni HALI BO'SH deb o'qib olgan edi.</para>
    /// </summary>
    [Fact]
    public async Task Parallel_ikki_sorov_bitta_nonce_bilan_faqat_bittasi_otadi()
    {
        var (db, svc, vault) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(46);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);

        // Ikkinchi "so'rov" — mustaqil kontekst, AYNI baza.
        using var other = db.NewContext();
        var svcB = new FaceLoginService(other, vault);

        // ⚠️ POYGANI MODELLASHTIRISH: B chaqiruvni A dan OLDIN o'qib oldi (`UsedAt` hali bo'sh).
        // EF tracker uni eslab qoladi va B keyinroq so'rov yuborganda AYNAN shu (eskirgan)
        // nusxani qaytaradi — haqiqiy parallel so'rovda ham xuddi shunday bo'ladi.
        await other.FaceChallenges.FirstAsync(c => c.Nonce == ch.Nonce);

        // A yakunlanadi va nonce'ni bandlaydi.
        var rA = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil,
            ch.Nonce, GoodLiveness(ch.Actions)));
        Assert.True(rA.Ok);

        // B "nonce bo'sh" deb o'ylab davom etadi — uni FAQAT konkurentlik tokeni to'sadi.
        var rB = await svcB.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 31), profil,
            ch.Nonce, GoodLiveness(ch.Actions)));
        Assert.False(rB.Ok);
        Assert.Equal(FaceMatch.ReasonNoChallenge, rB.Reason);
    }

    /// <summary>Konkurentlik himoyasi model metadatasida turibdi — kimdir uni olib tashlasa
    /// yuqoridagi poyga JIMGINA qaytadi (SQL o'zgarmaydi, test esa qizaradi).</summary>
    [Fact]
    public void Nonce_UsedAt_konkurentlik_tokeni()
    {
        using var db = TestDb.Sqlite();
        var prop = db.Context.Model
            .FindEntityType(typeof(FaceChallenge))!
            .FindProperty(nameof(FaceChallenge.UsedAt))!;
        Assert.True(prop.IsConcurrencyToken,
            "FaceChallenge.UsedAt konkurentlik tokeni bo'lishi SHART — aks holda parallel ikki "
            + "verify bitta nonce bilan o'tib ketadi (AppDbContext izohiga qarang).");
    }

    [Fact]
    public async Task Muddati_otgan_nonce_rad_etiladi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);
        var row = await db.Context.FaceChallenges.FirstAsync(c => c.Nonce == ch.Nonce);
        row.ExpiresAt = AppClock.Now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        await db.Context.SaveChangesAsync();

        var r = await svc.VerifyAsync(Req(user, student, Vec(42), Vec(42),
            ch.Nonce, GoodLiveness(ch.Actions)));
        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonNoChallenge, r.Reason);
    }

    [Fact]
    public async Task Begona_foydalanuvchining_noncei_qabul_qilinmaydi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (ali, aliStudent) = AddStudent(db);
        var vali = new AppUser { FullName = "Vali", Role = Roles.Student, Email = "vali" };
        db.Context.Users.Add(vali);
        var valiStudent = new Student { FullName = "Vali", UserId = vali.Id, BirthCertificateUrl = "/uploads/v.jpg" };
        db.Context.Students.Add(valiStudent);
        await db.Context.SaveChangesAsync();

        // Nonce VALI uchun berilgan, so'rov esa ALI nomidan keladi.
        var ch = await svc.IssueChallengeAsync(vali.Id, valiStudent.Id);
        var r = await svc.VerifyAsync(Req(ali, aliStudent, Vec(43), Vec(43),
            ch.Nonce, GoodLiveness(ch.Actions)));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonNoChallenge, r.Reason);
    }

    [Fact]
    public async Task Nonce_umuman_yuborilmasa_sozlamaga_qarab_hal_qilinadi()
    {
        // (a) MAJBURIY (default) — rad.
        var (db1, svc1, _) = NewService(liveness: true);
        using (db1)
        {
            var (user, student) = AddStudent(db1);
            var r = await svc1.VerifyAsync(Req(user, student, Vec(44), Vec(44)));
            Assert.False(r.Ok);
            Assert.Equal(FaceMatch.ReasonNoChallenge, r.Reason);
        }

        // (b) O'CHIRILGAN — eski oqim ishlayveradi.
        var (db2, svc2, _) = NewService(liveness: false);
        using (db2)
        {
            var (user, student) = AddStudent(db2);
            var profil = Vec(45);
            Assert.True((await svc2.VerifyAsync(Req(user, student, Near(profil, 0.05), profil))).Ok);
        }
    }

    /// <summary>Chaqiruvlar SOATIGA cheklangan — nonce olish "bepul qayta urinish" bo'lib
    /// qolmasin (tiriklikdan o'tmagan urinish asosiy chegarani yemaydi).</summary>
    [Fact]
    public async Task Challenge_soatiga_cheklangan()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);

        for (var i = 0; i < FaceLoginService.MaxChallengesPerHour; i++)
            Assert.True((await svc.IssueChallengeAsync(user.Id, student.Id)).Ok);

        var blocked = await svc.IssueChallengeAsync(user.Id, student.Id);
        Assert.False(blocked.Ok);
        Assert.Equal(FaceMatch.ReasonTooManyAttempts, blocked.Reason);
    }

    /// <summary>
    /// ⚠️ Chegara MUDDATI O'TGAN chaqiruvlarni ham sanaydi. Ilgari tozalash "muddati o'tgan"
    /// (90 s) qatorlarni o'chirar edi va hisob har safar nolga qaytardi — ya'ni chegara
    /// JIMGINA ishlamasdi. Endi faqat BIR SOATDAN eski qatorlar o'chadi.
    /// </summary>
    [Fact]
    public async Task Challenge_chegarasi_muddati_otgan_chaqiruvlarni_ham_sanaydi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);

        for (var i = 0; i < FaceLoginService.MaxChallengesPerHour; i++)
            await svc.IssueChallengeAsync(user.Id, student.Id);

        // Hammasining muddati o'tdi (90 soniya), lekin ular SOAT ichida berilgan.
        foreach (var row in await db.Context.FaceChallenges.ToListAsync())
            row.ExpiresAt = AppClock.Now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        await db.Context.SaveChangesAsync();

        Assert.False((await svc.IssueChallengeAsync(user.Id, student.Id)).Ok);
        Assert.Equal(FaceLoginService.MaxChallengesPerHour, await db.Context.FaceChallenges.CountAsync());
    }

    /* =============================================================================================
     *  3. TIRIKLIK (harakatlar + o'lchangan qiymat)
     * ========================================================================================== */

    [Fact]
    public void Liveness_katalogida_blink_va_smile_YOQ()
    {
        // YuNet 5 nuqta beradi — ko'z qisish/jilmayishni O'LCHAY OLMAYDI, ya'ni ilova ularni
        // faqat "bajarildi" deb yozib yuborardi (himoya bo'lmagan himoya).
        Assert.DoesNotContain("blink", FaceLiveness.All);
        Assert.DoesNotContain("smile", FaceLiveness.All);
        Assert.Equal(
            new[] { "turn_left", "turn_right", "move_closer", "move_back" },
            FaceLiveness.All.ToArray());
    }

    [Fact]
    public void Liveness_togri_ketma_ketlik_otadi()
    {
        var actions = new[] { FaceLiveness.ActionTurnLeft, FaceLiveness.ActionMoveCloser };
        Assert.Null(FaceLiveness.Check(
            FaceLiveness.Encode(actions), GoodLiveness(actions), BaselineRatio));
    }

    [Fact]
    public void Liveness_tartib_notogri_bolsa_rad()
    {
        var asked = new[] { FaceLiveness.ActionTurnLeft, FaceLiveness.ActionTurnRight };
        var answered = new[] { FaceLiveness.ActionTurnRight, FaceLiveness.ActionTurnLeft };
        Assert.Equal(FaceMatch.ReasonLiveness, FaceLiveness.Check(
            FaceLiveness.Encode(asked), GoodLiveness(answered), BaselineRatio));
    }

    [Fact]
    public void Liveness_bitta_harakat_yetishmasa_rad()
    {
        var asked = new[] { FaceLiveness.ActionTurnLeft, FaceLiveness.ActionMoveBack };
        var answered = new[] { FaceLiveness.ActionTurnLeft };
        Assert.Equal(FaceMatch.ReasonLiveness, FaceLiveness.Check(
            FaceLiveness.Encode(asked), GoodLiveness(answered), BaselineRatio));

        // Umuman javob bermaslik ham rad.
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode(asked), "", BaselineRatio));
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode(asked), "{buzuq", BaselineRatio));
    }

    [Fact]
    public void Liveness_bir_zumda_bajarildi_desa_rad()
    {
        var asked = new[] { FaceLiveness.ActionTurnLeft };
        var tez = """[{"action":"turn_left","ok":true,"ms":50,"value":-30}]""";
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode(asked), tez, BaselineRatio));

        var juda_uzoq = """[{"action":"turn_left","ok":true,"ms":60000,"value":-30}]""";
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode(asked), juda_uzoq, BaselineRatio));
    }

    /// <summary>
    /// ⚠️ ASOSIY: <c>ok:true</c> ga ISHONILMAYDI. O'zgartirilgan APK hech narsa qilmasdan
    /// "bajarildi" deb yozadi — o'lchangan QIYMAT haqiqiy o'zgarishni ko'rsatishi shart.
    /// </summary>
    [Theory]
    // burilish — burchak yetarli emas yoki ISHORASI teskari
    [InlineData("turn_left", 1400, -3.0)]
    [InlineData("turn_left", 1400, 27.0)]
    [InlineData("turn_right", 1400, 4.0)]
    [InlineData("turn_right", 1400, -27.0)]
    // masofa — o'zgarish yetarli emas (baseline 0.32)
    [InlineData("move_closer", 900, 0.33)]
    [InlineData("move_back", 900, 0.31)]
    public void Liveness_okTrue_bolsa_ham_olchangan_qiymat_yolgon_bolsa_rad(
        string action, int ms, double value)
    {
        var json = $$"""[{"action":"{{action}}","ok":true,"ms":{{ms}},"value":{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]""";
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode([action]), json, BaselineRatio));
    }

    [Fact]
    public void Liveness_qiymat_umuman_yuborilmasa_rad()
    {
        var json = """[{"action":"turn_left","ok":true,"ms":1400}]""";
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode([FaceLiveness.ActionTurnLeft]), json, BaselineRatio));
    }

    [Fact]
    public void Liveness_ok_false_bolsa_rad()
    {
        var json = """[{"action":"turn_left","ok":false,"ms":1400,"value":-30}]""";
        Assert.Equal(FaceMatch.ReasonLiveness,
            FaceLiveness.Check(FaceLiveness.Encode([FaceLiveness.ActionTurnLeft]), json, BaselineRatio));
    }

    /// <summary>Oqimning O'ZIDA: to'g'ri javob o'tadi, noto'g'risi «Tiriklik tekshiruvidan o'tmadi».</summary>
    [Fact]
    public async Task Verify_tiriklikdan_otmasa_rad_etiladi_va_urinish_yoziladi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(51);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);
        var yolgon = "[" + string.Join(",", ch.Actions.Select(a =>
            $$"""{"action":"{{a}}","ok":true,"ms":40,"value":0}""")) + "]";

        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, ch.Nonce, yolgon));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonLiveness, r.Reason);
        Assert.False(await db.Context.StudentFaceProfiles.AnyAsync(p => p.StudentId == student.Id));
        Assert.False(await db.Context.TrustedDevices.AnyAsync(d => d.UserId == user.Id));
        // Admin "nega kira olmayapti" ni ko'rsin.
        Assert.Equal(1, await db.Context.LoginFaceChecks.CountAsync(c => c.Reason == FaceMatch.ReasonLiveness));
        // ...lekin SOLISHTIRISH bo'lmagani uchun soatlik chegara YEYILMAYDI.
        Assert.Equal(0, await svc.RecentAttemptsAsync(student.Id));
    }

    [Fact]
    public async Task Verify_togri_tiriklik_bilan_otadi()
    {
        var (db, svc, _) = NewService();
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(52);

        var ch = await svc.IssueChallengeAsync(user.Id, student.Id);
        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil,
            ch.Nonce, GoodLiveness(ch.Actions)));

        Assert.True(r.Ok);
        Assert.True(r.Enrolled);
        Assert.True(await svc.IsTrustedAsync(user.Id, "dev-1"));
    }

    /* ---------------------------------------------------------------------------------------------
     *  MODEL ALMASHISHI — `status` va `verify` BIR XIL javob berishi shart
     * ------------------------------------------------------------------------------------------ */

    /// <summary>⚠️ Bu qoida ikki joyda AYRI hisoblanardi va shu sabab oqim jimgina buzilardi:
    /// <c>GET /student/face/status</c> etalonni «qatori bormi» deb sanardi, <c>VerifyAsync</c> esa
    /// model versiyasini ham tekshirardi. Endi ikkalasi <c>TemplateUsable</c> ni chaqiradi.</summary>
    [Fact]
    public void TemplateUsable_model_almashsa_eski_etalon_yaroqsiz()
    {
        Assert.True(FaceLoginService.TemplateUsable("m1", "m1"));
        Assert.True(FaceLoginService.TemplateUsable("M1", "m1"));   // registr muhim emas
        Assert.False(FaceLoginService.TemplateUsable("m1", "m2"));
        Assert.False(FaceLoginService.TemplateUsable(null, "m2"));
        // Markaz modeli belgilanmagan — tekshiruv ATAYIN o'chirilgan.
        Assert.True(FaceLoginService.TemplateUsable("m1", ""));
        Assert.True(FaceLoginService.TemplateUsable("m1", null));
    }

    /// <summary>Model almashgach o'quvchi ADMIN TASDIG'INI KUTMASDAN, profil rasmidan olingan
    /// yangi etalon bilan kirishi kerak — aks holda modelni yangilash «hamma o'quvchini qo'lda
    /// tasdiqlash» degani bo'lardi.</summary>
    [Fact]
    public async Task Model_almashgach_profil_rasmidan_qayta_royxatdan_otadi()
    {
        var (db, svc, _) = NewService(liveness: false);
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(77);

        // 1. Eski model ("m1") bilan ro'yxatdan o'tdi.
        Assert.True((await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil))).Enrolled);

        // 2. Markaz modelni almashtirdi.
        (await db.Context.CenterMeta.FirstAsync()).LoginFaceModelVersion = "m2";
        await db.Context.SaveChangesAsync();

        // 3. Eski etalon endi YAROQSIZ — `status` shuni ko'rib "etalon yo'q" deydi va ilova
        //    profil rasmidan `refVector` yuboradi.
        var saved = await db.Context.StudentFaceProfiles.FirstAsync(p => p.StudentId == student.Id);
        Assert.False(FaceLoginService.TemplateUsable(saved.ModelVersion, "m2"));

        var r = await svc.VerifyAsync(new FaceLoginService.VerifyRequest(
            student, user.Id, "/uploads/face/selfi2.jpg", Near(profil, 0.05), profil, GoodQuality,
            "m2", "dev-1", "Pixel", "android", "1.0", "1.2.3.4"));

        Assert.True(r.Ok);
        Assert.True(r.Enrolled);
        Assert.Equal("m2", (await db.Context.StudentFaceProfiles
            .FirstAsync(p => p.StudentId == student.Id)).ModelVersion);
    }

    /// <summary>⚠️ `refVector` YUBORILMASA (ilova "etalon bor" deb o'ylagan holat) — o'quvchi
    /// `pending` ga tushadi. AYNAN shu eski nosozlikning oqibati edi; test uni yodda tutadi.</summary>
    [Fact]
    public async Task Model_almashgach_refVector_kelmasa_pending_boladi()
    {
        var (db, svc, _) = NewService(liveness: false);
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(78);

        Assert.True((await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil))).Enrolled);
        (await db.Context.CenterMeta.FirstAsync()).LoginFaceModelVersion = "m2";
        await db.Context.SaveChangesAsync();

        var r = await svc.VerifyAsync(new FaceLoginService.VerifyRequest(
            student, user.Id, "/uploads/face/selfi2.jpg", Near(profil, 0.05), null, GoodQuality,
            "m2", "dev-1", "Pixel", "android", "1.0", "1.2.3.4"));

        Assert.False(r.Ok);
        Assert.Equal(FaceLoginService.StatusPending, r.Status);
    }

    /* =============================================================================================
     *  4. ILOVA HAQIQIYLIGI (attestation)
     * ========================================================================================== */

    [Theory]
    [InlineData(AppAttestation.Verdict.Ok)]
    [InlineData(AppAttestation.Verdict.Failed)]
    [InlineData(AppAttestation.Verdict.Unavailable)]
    [InlineData(AppAttestation.Verdict.NotConfigured)]
    public void Attestation_sozlama_ochiq_bolsa_hech_narsani_tosmaydi(AppAttestation.Verdict v) =>
        Assert.Null(AppAttestation.Gate(v, required: false));

    [Fact]
    public void Attestation_majburiy_bolsa_faqat_ok_otadi()
    {
        Assert.Null(AppAttestation.Gate(AppAttestation.Verdict.Ok, required: true));
        Assert.Equal(FaceMatch.ReasonAttestation,
            AppAttestation.Gate(AppAttestation.Verdict.Failed, required: true));
        Assert.Equal(FaceMatch.ReasonAttestationMissing,
            AppAttestation.Gate(AppAttestation.Verdict.NotConfigured, required: true));
    }

    /// <summary>⚠️ FAIL-CLOSED: tashqi xizmat javob bermasa (timeout/5xx) va tekshiruv MAJBURIY
    /// bo'lsa — kirish RAD etiladi. "Tekshira olmadik" ≠ "o'tkazamiz".</summary>
    [Fact]
    public void Attestation_xizmat_javob_bermasa_fail_closed()
    {
        Assert.Equal(FaceMatch.ReasonAttestationUnavailable,
            AppAttestation.Gate(AppAttestation.Verdict.Unavailable, required: true));
        // Sozlama o'chiq bo'lsa esa fail-open (hech kim qulflanib qolmasin).
        Assert.Null(AppAttestation.Gate(AppAttestation.Verdict.Unavailable, required: false));
    }

    [Fact]
    public async Task Verify_attestation_majburiy_bolsa_notConfigured_rad_etiladi()
    {
        var (db, svc, _) = NewService(liveness: false, attestation: true);
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(61);

        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil,
            attest: AppAttestation.Verdict.NotConfigured));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonAttestationMissing, r.Reason);
    }

    [Fact]
    public async Task Verify_attestation_ixtiyoriy_bolsa_notConfigured_otadi_va_jurnalga_yoziladi()
    {
        var (db, svc, _) = NewService(liveness: false, attestation: false);
        using var _d = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(62);

        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil,
            attest: AppAttestation.Verdict.NotConfigured));

        Assert.True(r.Ok);
        var check = await db.Context.LoginFaceChecks.FirstAsync(c => c.StudentId == student.Id);
        Assert.Equal("notConfigured", check.Attested);
    }

    /// <summary>Google javobini baholash — tarmoqqa tegmasdan (sof funksiya).</summary>
    [Fact]
    public void Attestation_Judge_google_javobini_togri_baholaydi()
    {
        const string pkg = "uz.wunderkindlc.student";
        // Google javobining soddalashtirilgan nusxasi (faqat biz o'qiydigan maydonlar).
        static string Body(string package, string app, string device, string nonce) =>
            "{\"tokenPayloadExternal\":{"
            + "\"requestDetails\":{\"requestPackageName\":\"" + package + "\",\"nonce\":\"" + nonce + "\"},"
            + "\"appIntegrity\":{\"appRecognitionVerdict\":\"" + app + "\",\"packageName\":\"" + package + "\"},"
            + "\"deviceIntegrity\":{\"deviceRecognitionVerdict\":[\"" + device + "\"]}}}";

        Assert.Equal(AppAttestation.Verdict.Ok,
            AppAttestation.Judge(Body(pkg, "PLAY_RECOGNIZED", "MEETS_DEVICE_INTEGRITY", "N1"), pkg, "N1").Verdict);

        // O'zgartirilgan APK.
        Assert.Equal(AppAttestation.Verdict.Failed,
            AppAttestation.Judge(Body(pkg, "UNRECOGNIZED_VERSION", "MEETS_DEVICE_INTEGRITY", "N1"), pkg, "N1").Verdict);
        // Root/emulyator.
        Assert.Equal(AppAttestation.Verdict.Failed,
            AppAttestation.Judge(Body(pkg, "PLAY_RECOGNIZED", "MEETS_BASIC_INTEGRITY", "N1"), pkg, "N1").Verdict);
        // Eski token qayta ishlatilmoqda (nonce mos emas).
        Assert.Equal(AppAttestation.Verdict.Failed,
            AppAttestation.Judge(Body(pkg, "PLAY_RECOGNIZED", "MEETS_DEVICE_INTEGRITY", "ESKI"), pkg, "N1").Verdict);
        // Boshqa paket (token boshqa ilovaniki).
        Assert.Equal(AppAttestation.Verdict.Failed,
            AppAttestation.Judge(Body("boshqa.paket", "PLAY_RECOGNIZED", "MEETS_DEVICE_INTEGRITY", "N1"), pkg, "N1").Verdict);

        // Tushunarsiz javob — bu MIJOZ aybi emas, shuning uchun `unavailable`.
        Assert.Equal(AppAttestation.Verdict.Unavailable, AppAttestation.Judge("{buzuq", pkg, "N1").Verdict);
        Assert.Equal(AppAttestation.Verdict.Unavailable, AppAttestation.Judge("{}", pkg, "N1").Verdict);
        Assert.Equal(AppAttestation.Verdict.Unavailable, AppAttestation.Judge("", pkg, "N1").Verdict);
    }

    /* =============================================================================================
     *  5. SELFI FAYLLARI — uploads/face/ (zaxiradan tashqarida, statik yo'ldan berilmaydi)
     * ========================================================================================== */

    [Fact]
    public void FaceStorage_yangi_selfi_face_papkasiga_yoziladi()
    {
        var url = FaceStorage.NewUrl();
        Assert.StartsWith("/uploads/face/", url);
        Assert.True(FaceStorage.IsFaceUrl(url));
        Assert.False(FaceStorage.IsFaceUrl("/uploads/eski.jpg"));
    }

    [Fact]
    public void FaceStorage_yol_manipulyatsiyasini_rad_etadi()
    {
        const string root = "/app";
        Assert.Null(FaceStorage.ResolvePath(root, null));
        Assert.Null(FaceStorage.ResolvePath(root, ""));
        Assert.Null(FaceStorage.ResolvePath(root, "/etc/passwd"));
        Assert.Null(FaceStorage.ResolvePath(root, "/uploads/../secret.jpg"));
        Assert.Null(FaceStorage.ResolvePath(root, "/uploads/face/../../secret.jpg"));
        Assert.Null(FaceStorage.ResolvePath(root, "/uploads/certificates/a.pdf"));

        // Yangi va ESKI manzillar — ikkalasi ham tushuniladi (eski selfilar yo'qolmasin).
        Assert.NotNull(FaceStorage.ResolvePath(root, "/uploads/face/abc.jpg"));
        Assert.NotNull(FaceStorage.ResolvePath(root, "/uploads/abc.jpg"));
        Assert.Contains(FaceStorage.FolderName, FaceStorage.ResolvePath(root, "/uploads/face/abc.jpg")!);
    }

    /// <summary>Selfi papkasi statik yo'l bilan BERILMASLIGI kerak — `Program.cs` da
    /// <c>PrivateFolderFileProvider</c> ro'yxatiga qo'shilganini manba matnidan tekshiramiz
    /// (Tests loyihasi Server'ga referens qilmaydi).</summary>
    [Fact]
    public void Program_face_papkasini_statik_yoldan_yopgan()
    {
        var src = ServerSource("Program.cs");
        Assert.Contains("FaceStorage.FolderName", src);
        Assert.Contains("PrivateFolderFileProvider", src);
        // Kalit yo'q bo'lsa startupda OGOHLANTIRISH.
        Assert.Contains("FaceVectorKeyConfigured", src);
    }

    /// <summary>Admin javobida `/uploads/...` manzili emas, AVTORIZATSIYALANGAN endpoint bo'lishi kerak.</summary>
    [Fact]
    public void Admin_javobida_selfi_manzili_emas_API_yoli_qaytadi()
    {
        var src = ServerSource("Controllers", "AdminFaceController.cs");
        Assert.Matches(new Regex(@"HttpGet\(""checks/\{id\}/image""\)"), src);
        Assert.Contains("/api/admin/face/checks/", src);
        // O'qish darvozasi joyida qolgan (javobda biometrik surat endpointlari bor).
        Assert.Matches(
            new Regex(@"\[AdminPerm\(\s*""students\.face""[^\]]*\bReadRequiresPerm\s*=\s*true\b[^\]]*\)\]"), src);
    }

    /// <summary>Selfi `uploads` ning O'ZIGA emas, `uploads/face/` ga yozilishi kerak.</summary>
    [Fact]
    public void Student_controlleri_selfini_face_papkasiga_yozadi()
    {
        var src = ServerSource("Controllers", "StudentFaceController.cs");
        Assert.Contains("FaceStorage.NewUrl()", src);
        Assert.Contains("FaceStorage.ResolvePath", src);
        // Chaqiruv endpointi bor.
        Assert.Contains(@"HttpPost(""challenge"")", src);
    }

    /// <summary>Zaxira nusxa selfilarni OLMASLIGI kerak (docker-compose `backup` xizmati).</summary>
    [Fact]
    public void Backup_arxivi_face_papkasini_istisno_qiladi()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot, "docker-compose.yml"));
        Assert.Contains("--exclude", compose);
        Assert.Contains("uploads/face", compose);
    }

    /// <summary>`.env.example` yangi kalitlarni eslatib tursin (aks holda deployda unutiladi).</summary>
    [Fact]
    public void EnvExample_yangi_kalitlarni_hujjatlashtirgan()
    {
        var env = File.ReadAllText(Path.Combine(RepoRoot, ".env.example"));
        Assert.Contains(AppSecrets.EnvKeys.FaceVectorKey, env);
        Assert.Contains(AppSecrets.EnvKeys.PlayIntegritySaJson, env);
        Assert.Contains(AppSecrets.EnvKeys.PlayIntegrityPackage, env);
    }

    /* =============================================================================================
     *  Manba matni yordamchilari
     * ========================================================================================== */

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ServerSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot, "WunderkindLC.Server" }.Concat(parts).ToArray()));
}
