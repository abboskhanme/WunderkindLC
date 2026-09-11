using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// YUZ BILAN KIRISH — o'quvchi mobil ilovasiga kirishda shaxsni tasdiqlash.
///
/// <para>Ikki qatlam tekshiriladi:</para>
/// <list type="number">
///   <item><b>Sof matematika</b> (<see cref="FaceMatch"/>) — kodlash/dekodlash, kosinus,
///     buzuq ma'lumot, sifat chegaralari, qaror mantig'i;</item>
///   <item><b>Oqim</b> (<see cref="FaceLoginService"/>) — sozlama o'chiq bo'lsa login o'zgarmasligi,
///     ishonchsiz qurilma, etalonni birinchi marta olish, <b>begona odam etalon qo'ya olmasligi</b>
///     (ASOSIY xavfsizlik testi), qurilma ishonchli bo'lgach yuz so'ralmasligi, urinishlar
///     chegarasi va eski selfilarning tozalanishi.</item>
/// </list>
///
/// <para><b>NEGA CONTROLLER TESTLARI YO'Q:</b> <c>WunderkindLC.Tests</c> loyihasi
/// <c>WunderkindLC.Server</c> ga referens QILMAYDI (qarang <c>SensitiveReadPermTests</c>).
/// Shu sabab controller darvozalari (cheklangan token middleware'i, <c>AdminPerm</c>) MANBA
/// MATNIDAN tekshiriladi — kimdir darvozani olib tashlasa test darrov qizaradi.</para>
/// </summary>
public class FaceLoginTests
{
    /* =============================================================================================
     *  Yordamchilar
     * ========================================================================================== */

    /// <summary>Takrorlanadigan "yuz vektori" — <paramref name="seed"/> har xil odam degani.</summary>
    private static float[] Vec(int seed, int dim = 128)
    {
        var rnd = new Random(seed);
        var v = new float[dim];
        for (var i = 0; i < dim; i++) v[i] = (float)(rnd.NextDouble() * 2 - 1);
        return FaceMatch.Normalize(v);
    }

    /// <summary>Berilgan vektorga YAQIN (o'sha odamning boshqa surati) vektor.</summary>
    private static float[] Near(float[] source, double noise, int seed = 7)
    {
        var rnd = new Random(seed);
        var v = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
            v[i] = (float)(source[i] + (rnd.NextDouble() * 2 - 1) * noise);
        return FaceMatch.Normalize(v);
    }

    private static string B64(float[] v) => Convert.ToBase64String(FaceMatch.Encode(v));

    /// <summary>Test uchun seyf — kalit har testda YANGI (global holat/`.env` ga bog'liq emas).</summary>
    internal static FaceVault NewVault() => new(FaceVault.GenerateKey());

    /// <summary>
    /// ⚠️ <c>liveness: false</c> ATAYIN standart: bu faylning testlari SOLISHTIRISH mantig'ini
    /// tekshiradi, tiriklik darvozasini emas (u <c>FaceSecurityTests</c> da). Entity default'i
    /// esa <c>true</c> — prod'da darvoza YOQILGAN holda keladi.
    /// </summary>
    private static (TestDb Db, FaceLoginService Service) NewService(
        bool enabled = true, double threshold = 0.60, string model = "m1", int keep = 5,
        bool liveness = false)
    {
        var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta
        {
            LoginFaceEnabled = enabled,
            LoginFaceThreshold = threshold,
            LoginFaceModelVersion = model,
            LoginFaceKeepChecks = keep,
            LoginFaceRequireLiveness = liveness,
        });
        db.Context.SaveChanges();
        return (db, new FaceLoginService(db.Context, NewVault()));
    }

    private static (AppUser User, Student Student) AddStudent(TestDb db, string? photo = "/uploads/foto.jpg")
    {
        var user = new AppUser { FullName = "Ali Valiyev", Role = Roles.Student, Email = "ali" };
        db.Context.Users.Add(user);
        var student = new Student { FullName = "Ali Valiyev", UserId = user.Id, BirthCertificateUrl = photo };
        db.Context.Students.Add(student);
        db.Context.SaveChanges();
        return (user, student);
    }

    private static FaceLoginService.VerifyRequest Req(
        AppUser user, Student student, float[] selfie, float[]? reference = null,
        string model = "m1", string device = "dev-1", string image = "/uploads/selfi.jpg",
        string quality = "") =>
        new(student, user.Id, image, selfie, reference, quality, model, device, "Pixel", "android", "1.0", "1.2.3.4");

    /* =============================================================================================
     *  1. KODLASH / DEKODLASH
     * ========================================================================================== */

    [Fact]
    public void Encode_Decode_vektorni_aynan_tiklaydi()
    {
        var v = Vec(1);
        var back = FaceMatch.Decode(FaceMatch.Encode(v));
        Assert.Equal(v.Length, back.Length);
        for (var i = 0; i < v.Length; i++) Assert.Equal(v[i], back[i]);
    }

    [Fact]
    public void Decode_buzuq_uzunlikda_bosh_massiv_qaytaradi()
    {
        // 4 ga bo'linmaydigan bayt oqimi — ISTISNO EMAS (klient ma'lumoti ishonchsiz).
        Assert.Empty(FaceMatch.Decode(new byte[] { 1, 2, 3 }));
        Assert.Empty(FaceMatch.Decode(Array.Empty<byte>()));
        Assert.Empty(FaceMatch.Decode(null));
    }

    [Fact]
    public void TryParse_buzuq_base64_ni_xato_matni_bilan_rad_etadi()
    {
        Assert.Null(FaceMatch.TryParse("bu base64 emas!!!", out var e1));
        Assert.False(string.IsNullOrEmpty(e1));

        Assert.Null(FaceMatch.TryParse("", out var e2));
        Assert.False(string.IsNullOrEmpty(e2));

        Assert.NotNull(FaceMatch.TryParse(B64(Vec(3)), out var ok));
        Assert.Null(ok);
    }

    [Fact]
    public void Validate_NaN_Infinity_nol_vektor_va_notogri_olchamni_rad_etadi()
    {
        Assert.NotNull(FaceMatch.Validate(new float[] { 1, 2, 3 }));                  // juda kichik
        Assert.NotNull(FaceMatch.Validate(new float[FaceMatch.MaxDim + 1]));          // juda katta
        Assert.NotNull(FaceMatch.Validate(new float[128]));                           // hammasi nol

        var nan = Vec(4); nan[5] = float.NaN;
        Assert.NotNull(FaceMatch.Validate(nan));

        var inf = Vec(4); inf[9] = float.PositiveInfinity;
        Assert.NotNull(FaceMatch.Validate(inf));

        Assert.Null(FaceMatch.Validate(Vec(4)));
    }

    /* =============================================================================================
     *  2. KOSINUS VA NORMALLASHTIRISH
     * ========================================================================================== */

    [Fact]
    public void Cosine_ozini_ozi_bilan_1_begona_bilan_past()
    {
        var a = Vec(11);
        Assert.Equal(1.0, FaceMatch.Cosine(a, a), 6);
        Assert.True(FaceMatch.Cosine(a, Vec(12)) < 0.5, "Ikki begona vektor yuqori ball berdi");
    }

    [Fact]
    public void Cosine_masshtabga_bogliq_emas_normalizatsiya_ishlaydi()
    {
        var a = Vec(13);
        var scaled = a.Select(x => x * 17f).ToArray();     // klient normallashtirmagan holat
        Assert.Equal(1.0, FaceMatch.Cosine(a, scaled), 6);

        var n = FaceMatch.Normalize(scaled);
        var len = Math.Sqrt(n.Sum(x => (double)x * x));
        Assert.Equal(1.0, len, 5);
    }

    [Fact]
    public void Cosine_uzunliklar_mos_kelmasa_istisno()
    {
        Assert.Throws<ArgumentException>(() => FaceMatch.Cosine(Vec(1, 128), Vec(1, 512)));
    }

    /* =============================================================================================
     *  3. SIFAT CHEGARALARI
     * ========================================================================================== */

    // ⚠️ Qiymatlar XOM birliklarda (ilova o'lchovi): sharpness = Laplas dispersiyasi,
    // brightness = 0..255, yaw/roll = gradus. Ilgari bu yerda 0..1 normallashtirilgan
    // qiymatlar turardi va server chegaralari amalda hech qachon ishlamasdi.
    [Theory]
    [InlineData(0, 500, 128, 0.30, 0, 0, FaceMatch.ReasonNoFace)]
    [InlineData(2, 500, 128, 0.30, 0, 0, FaceMatch.ReasonManyFaces)]
    [InlineData(1, 500, 20, 0.30, 0, 0, FaceMatch.ReasonDark)]
    [InlineData(1, 500, 240, 0.30, 0, 0, FaceMatch.ReasonBright)]
    [InlineData(1, 10, 128, 0.30, 0, 0, FaceMatch.ReasonBlurry)]
    [InlineData(1, 500, 128, 0.05, 0, 0, FaceMatch.ReasonSmallFace)]
    [InlineData(1, 500, 128, 0.30, 45, 0, FaceMatch.ReasonAngle)]
    [InlineData(1, 500, 128, 0.30, 0, 45, FaceMatch.ReasonAngle)]
    public void Reject_har_bir_sifat_muammosi_uchun_ozbekcha_sabab(
        int faces, double sharp, double bright, double ratio, double yaw, double roll,
        string expected)
    {
        var q = new FaceMatch.FaceQuality(faces, sharp, bright, ratio, yaw, roll);
        Assert.Equal(expected, FaceMatch.Reject(q, FaceMatch.DefaultLimits));
    }

    /// <summary>
    /// ⚠️ TARTIB: qorong'i kadrda Laplas dispersiyasi ham tushib ketadi (o'lchov: 533 → 44).
    /// Foydalanuvchiga «xira» emas, «yorug'roq joyda oling» deyilishi kerak — aks holda u
    /// telefonni qimirlatmaslikka urinib, muammoni hech qachon hal qilmasdi.
    /// </summary>
    [Fact]
    public void Reject_qorongi_kadrda_yoruglikni_tiniqlikdan_OLDIN_aytadi()
    {
        var qorongi = new FaceMatch.FaceQuality(1, 44, 20, 0.30, 0, 0);
        Assert.Equal(FaceMatch.ReasonDark, FaceMatch.Reject(qorongi, FaceMatch.DefaultLimits));
    }

    [Fact]
    public void Reject_yaxshi_kadrni_otkazadi()
    {
        var q = new FaceMatch.FaceQuality(1, 531, 128, 0.40, 3, 2);
        Assert.Null(FaceMatch.Reject(q, FaceMatch.DefaultLimits));
        Assert.True(FaceMatch.IsAcceptable(q, FaceMatch.DefaultLimits));
    }

    /// <summary>Standart chegaralar ILOVA birligida (YuNet o'lchovi bo'yicha kalibrlangan).</summary>
    [Fact]
    public void DefaultLimits_ilova_birligida()
    {
        var l = FaceMatch.DefaultLimits;
        Assert.Equal(40, l.MinSharpness);
        Assert.Equal(55, l.MinBrightness);
        Assert.Equal(215, l.MaxBrightness);
        Assert.Equal(0.15, l.MinFaceRatio);
        Assert.Equal(25, l.MaxYaw);
        Assert.Equal(20, l.MaxRoll);
    }

    [Fact]
    public void ParseQuality_buzuq_JSON_da_istisno_tashlamaydi()
    {
        Assert.Equal(FaceMatch.Unknown, FaceMatch.ParseQuality("{buzuq"));
        Assert.Equal(FaceMatch.Unknown, FaceMatch.ParseQuality(""));
        Assert.Equal(FaceMatch.Unknown, FaceMatch.ParseQuality("[1,2,3]"));

        var q = FaceMatch.ParseQuality("""{"faces":1,"sharpness":531,"brightness":128,"faceRatio":0.3,"yaw":-5,"roll":2}""");
        Assert.Equal(1, q.Faces);
        Assert.Equal(531, q.Sharpness, 3);
        Assert.Equal(128, q.Brightness, 3);
        Assert.Equal(-5, q.Yaw, 3);
    }

    /// <summary>Sifat yubormagan klient CHEGARALARDAN o'tadi — sifat qulaylik uchun,
    /// xavfsizlik uchun emas (asosiy qaror baribir vektor solishtiruvida).</summary>
    [Fact]
    public void Unknown_sifat_barcha_chegaralardan_otadi()
    {
        Assert.Null(FaceMatch.Reject(FaceMatch.Unknown, FaceMatch.DefaultLimits));
        // ⚠️ faceRatio 1 EMAS: u tiriklikda "boshlang'ich masofa" bo'lib ishlatiladi va
        // 1 bo'lsa «yaqinlashing» topshirig'i matematik imkonsiz bo'lardi.
        Assert.True(FaceMatch.Unknown.FaceRatio < 1);
    }

    /* =============================================================================================
     *  4. QAROR (Evaluate)
     * ========================================================================================== */

    [Fact]
    public void Evaluate_etalon_bor_va_mos_ruxsat_beradi()
    {
        var etalon = Vec(21);
        var r = FaceMatch.Evaluate(Near(etalon, 0.05), etalon, null, 0.60);
        Assert.True(r.Ok);
        Assert.Equal(FaceMatch.StatusApproved, r.Status);
        Assert.False(r.Enroll);
    }

    [Fact]
    public void Evaluate_etalon_bor_lekin_begona_yuz_rad_etiladi()
    {
        var r = FaceMatch.Evaluate(Vec(22), Vec(21), null, 0.60);
        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonNoMatch, r.Reason);
    }

    [Fact]
    public void Evaluate_etalon_yoq_lekin_profil_rasmiga_mos_etalon_saqlanadi()
    {
        var profil = Vec(23);
        var r = FaceMatch.Evaluate(Near(profil, 0.05), null, profil, 0.60);
        Assert.True(r.Ok);
        Assert.True(r.Enroll);
    }

    [Fact]
    public void Evaluate_hech_narsa_yoq_bolsa_pending()
    {
        var r = FaceMatch.Evaluate(Vec(24), null, null, 0.60);
        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.StatusPending, r.Status);
        Assert.Null(r.Score);
    }

    [Fact]
    public void Evaluate_turli_olchamdagi_vektorlar_Ilovani_yangilang()
    {
        var r = FaceMatch.Evaluate(Vec(25, 128), Vec(25, 512), null, 0.60);
        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonOldApp, r.Reason);
    }

    /* =============================================================================================
     *  5. LOGIN QARORI
     * ========================================================================================== */

    [Fact]
    public async Task Sozlama_ochiq_bolsa_login_ozgarmaydi()
    {
        var (db, svc) = NewService(enabled: false);
        using var _ = db;
        var (user, _) = AddStudent(db);

        var d = await svc.DecideAsync(user, "yangi-qurilma");
        Assert.False(d.Required);
    }

    [Fact]
    public async Task Eski_klient_deviceIdsiz_yuz_soralmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, _) = AddStudent(db);

        Assert.False((await svc.DecideAsync(user, null)).Required);
        Assert.False((await svc.DecideAsync(user, "   ")).Required);
    }

    [Fact]
    public async Task Ishonchsiz_qurilmada_yuz_talab_qilinadi_va_holat_enroll()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, _) = AddStudent(db);

        var d = await svc.DecideAsync(user, "yangi-qurilma");
        Assert.True(d.Required);
        Assert.Equal(FaceLoginService.FaceStatusEnroll, d.Status);
    }

    [Fact]
    public async Task Oqituvchi_va_adminga_tegilmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var teacher = new AppUser { FullName = "O'qituvchi", Role = Roles.Teacher, Email = "t" };
        var admin = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "a" };
        db.Context.Users.AddRange(teacher, admin);
        db.Context.SaveChanges();

        Assert.False((await svc.DecideAsync(teacher, "qurilma")).Required);
        Assert.False((await svc.DecideAsync(admin, "qurilma")).Required);
    }

    /* =============================================================================================
     *  6. ETALON OLISH — VA ASOSIY XAVFSIZLIK TESTI
     * ========================================================================================== */

    [Fact]
    public async Task Etalon_yoq_refVector_mos_kelsa_etalon_saqlanadi_va_ruxsat_beriladi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);

        var profil = Vec(31);                       // profil rasmidan hisoblangan vektor
        var selfie = Near(profil, 0.05);            // o'sha odamning selfisi

        var r = await svc.VerifyAsync(Req(user, student, selfie, profil));

        Assert.True(r.Ok);
        Assert.True(r.Enrolled);
        var saved = await db.Context.StudentFaceProfiles.FirstAsync(p => p.StudentId == student.Id);
        Assert.Equal(FaceLoginService.SourcePhoto, saved.Source);
        Assert.Equal(selfie.Length, saved.Dim);
        Assert.Equal("m1", saved.ModelVersion);
    }

    /// <summary>
    /// ⚠️ ASOSIY XAVFSIZLIK TESTI. Parolni o'g'irlagan/olgan BEGONA odam o'z yuzini etalon qilib
    /// qo'ya olmasligi kerak — uning selfisi markazdagi PROFIL RASMIGA mos kelmaydi.
    /// Bu shart buzilsa butun modul ma'nosini yo'qotadi: birinchi kirgan odam "egasi" bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Begona_odam_refVectorga_mos_kelmasa_etalon_qoya_olmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);

        var profil = Vec(41);       // haqiqiy o'quvchi
        var begona = Vec(42);       // boshqa odam

        var r = await svc.VerifyAsync(Req(user, student, begona, profil));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonNoMatch, r.Reason);
        Assert.False(await db.Context.StudentFaceProfiles.AnyAsync(p => p.StudentId == student.Id));
        Assert.False(await db.Context.TrustedDevices.AnyAsync(d => d.UserId == user.Id));
    }

    [Fact]
    public async Task Etalon_ham_profil_rasmi_ham_yoq_bolsa_pending_va_vektor_saqlanadi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db, photo: null);

        var r = await svc.VerifyAsync(Req(user, student, Vec(51), reference: null));

        Assert.False(r.Ok);
        Assert.Equal(FaceLoginService.StatusPending, r.Status);
        var check = await db.Context.LoginFaceChecks.FirstAsync();
        // Vektor SAQLANADI — admin tasdiqlaganda aynan shu etalon bo'ladi.
        Assert.NotNull(check.Vector);
        Assert.NotEmpty(check.Vector!);
    }

    [Fact]
    public async Task Admin_pendingni_tasdiqlasa_etalon_boladi_va_vektor_urinishdan_ochadi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db, photo: null);
        await svc.VerifyAsync(Req(user, student, Vec(52), reference: null));

        var check = await db.Context.LoginFaceChecks.FirstAsync();
        Assert.Null(await svc.ApproveCheckAsync(check));
        await db.Context.SaveChangesAsync();

        var profile = await db.Context.StudentFaceProfiles.FirstAsync(p => p.StudentId == student.Id);
        Assert.Equal(FaceLoginService.SourceAdmin, profile.Source);
        // Nusxa qoldirilmaydi: vektor endi etalonda.
        Assert.Null((await db.Context.LoginFaceChecks.FirstAsync()).Vector);
    }

    [Fact]
    public async Task Tasdiqlangan_urinishni_qayta_tasdiqlab_bolmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db, photo: null);
        await svc.VerifyAsync(Req(user, student, Vec(53), reference: null));

        var check = await db.Context.LoginFaceChecks.FirstAsync();
        Assert.Null(await svc.ApproveCheckAsync(check));
        Assert.NotNull(await svc.ApproveCheckAsync(check));   // ikkinchi marta — xato matni
    }

    /* =============================================================================================
     *  7. ISHONCHLI QURILMA
     * ========================================================================================== */

    [Fact]
    public async Task Mos_selfidan_keyin_qurilma_ishonchli_va_ikkinchi_loginda_yuz_soralmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);

        var profil = Vec(61);
        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, device: "telefon-1"));
        Assert.True(r.Ok);

        Assert.True(await svc.IsTrustedAsync(user.Id, "telefon-1"));
        Assert.False((await svc.DecideAsync(user, "telefon-1")).Required);

        // Boshqa qurilmada esa yuz baribir so'raladi — endi "verify" holatida (etalon bor).
        var other = await svc.DecideAsync(user, "telefon-2");
        Assert.True(other.Required);
        Assert.Equal(FaceLoginService.FaceStatusVerify, other.Status);
    }

    [Fact]
    public async Task Bekor_qilingan_qurilmada_yuz_qayta_soralaadi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(62);
        await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, device: "telefon-1"));

        var device = await db.Context.TrustedDevices.FirstAsync();
        device.RevokedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await db.Context.SaveChangesAsync();

        Assert.False(await svc.IsTrustedAsync(user.Id, "telefon-1"));
        Assert.True((await svc.DecideAsync(user, "telefon-1")).Required);
    }

    [Fact]
    public async Task Bekor_qilingan_qurilma_qayta_tasdiqlansa_yangi_qator_yaratilmaydi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(63);
        await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, device: "telefon-1"));

        var device = await db.Context.TrustedDevices.FirstAsync();
        device.RevokedAt = "2026-01-01T00:00:00";
        await db.Context.SaveChangesAsync();

        // Etalon bor — endi odatdagi tekshiruv (refVector kerak emas).
        var r = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 99), device: "telefon-1"));
        Assert.True(r.Ok);

        // (UserId, DeviceId) UNIKAL — ikkinchi qator yaratilsa SQLite xato berardi.
        Assert.Equal(1, await db.Context.TrustedDevices.CountAsync());
        Assert.True(await svc.IsTrustedAsync(user.Id, "telefon-1"));
    }

    /* =============================================================================================
     *  8. URINISHLAR CHEGARASI
     * ========================================================================================== */

    [Fact]
    public async Task Urinishlar_chegarasi_soatiga_besh_marta()
    {
        var (db, svc) = NewService(keep: 50);
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(71);

        // 5 ta muvaffaqiyatsiz urinish (begona yuz) — hammasi solishtiruvga yetadi.
        for (var i = 0; i < FaceLoginService.MaxAttemptsPerHour; i++)
        {
            var r = await svc.VerifyAsync(Req(user, student, Vec(200 + i), profil));
            Assert.False(r.Ok);
            Assert.Equal(FaceMatch.ReasonNoMatch, r.Reason);
        }

        // 6-urinish — endi TO'G'RI yuz bilan ham o'tmaydi.
        var blocked = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil));
        Assert.False(blocked.Ok);
        Assert.Equal(FaceMatch.ReasonTooManyAttempts, blocked.Reason);
        Assert.Equal(0, blocked.AttemptsLeft);
    }

    [Fact]
    public async Task Sifat_sababli_rad_etilgan_kadr_chegarani_yemaydi()
    {
        var (db, svc) = NewService(keep: 50);
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(72);
        var qorongi = """{"faces":1,"sharpness":500,"brightness":10,"faceRatio":0.4}""";

        for (var i = 0; i < 8; i++)
        {
            var bad = await svc.VerifyAsync(Req(user, student, Vec(300 + i), profil, quality: qorongi));
            Assert.Equal(FaceMatch.ReasonDark, bad.Reason);
        }

        // Yorug'roq joyda olingan TO'G'RI kadr baribir o'tadi (chegara yeyilmagan).
        var ok = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil));
        Assert.True(ok.Ok);
    }

    /// <summary>
    /// ⚠️ CHEGARANI CHETLAB O'TISH: yaroqsiz kadr yuborib, SOLISHTIRILGAN urinishlarni
    /// jurnaldan surib chiqarish MUMKIN BO'LMASLIGI kerak.
    ///
    /// <para>Sifat sababli rad etilgan urinish soatlik chegarani yemaydi (yuqoridagi test), lekin
    /// bazada JOY EGALLAYDI. Tozalash oddiy "eng eskisini o'chir" bo'lganda hujumchi har safar bitta
    /// qorong'i kadr yuborib, <c>KeepChecks</c> oynasidan haqiqiy urinishni chiqarib yuborardi va
    /// <c>RecentAttemptsAsync</c> hisobi ORQAGA qaytardi — ya'ni yuz solishtirishni CHEKSIZ takrorlash
    /// mumkin edi (tiriklik o'chirilgan bo'lsa nonce ham kerak emas). Endi tozalash avval
    /// CHEGARAGA KIRMAYDIGAN qatorlarni o'chiradi.</para>
    /// </summary>
    [Fact]
    public async Task Yaroqsiz_kadrlar_chegara_hisobini_orqaga_qaytara_olmaydi()
    {
        // KeepChecks chegara bilan bir xil — "oyna to'lgan" holat aynan shu yerda boshlanadi.
        var (db, svc) = NewService(keep: FaceLoginService.MaxAttemptsPerHour);
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(74);
        var qorongi = """{"faces":1,"sharpness":500,"brightness":10,"faceRatio":0.4}""";

        // Chegaraga BIR QADAM qolguncha SOLISHTIRILGAN (begona yuz) urinishlar.
        for (var i = 0; i < FaceLoginService.MaxAttemptsPerHour - 1; i++)
            Assert.Equal(FaceMatch.ReasonNoMatch,
                (await svc.VerifyAsync(Req(user, student, Vec(400 + i), profil))).Reason);
        Assert.Equal(FaceLoginService.MaxAttemptsPerHour - 1, await svc.RecentAttemptsAsync(student.Id));

        // HUJUM: yaroqsiz kadrlar bilan jurnalni "yuvib", hisobni nolga qaytarishga urinish.
        for (var i = 0; i < 10; i++)
            Assert.Equal(FaceMatch.ReasonDark,
                (await svc.VerifyAsync(Req(user, student, Vec(500 + i), profil, quality: qorongi))).Reason);

        // Hisob JOYIDA (nuqsonli tozalashda bu 0 ga tushib ketardi).
        Assert.Equal(FaceLoginService.MaxAttemptsPerHour - 1, await svc.RecentAttemptsAsync(student.Id));

        // Oxirgi solishtirish chegarani yopadi, keyingisi esa TO'G'RI yuz bilan ham o'tmaydi.
        await svc.VerifyAsync(Req(user, student, Vec(600), profil));
        var blocked = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil));
        Assert.False(blocked.Ok);
        Assert.Equal(FaceMatch.ReasonTooManyAttempts, blocked.Reason);
    }

    [Fact]
    public async Task Model_mos_kelmasa_Ilovani_yangilang_va_urinish_yozilmaydi()
    {
        var (db, svc) = NewService(model: "m2");
        using var _ = db;
        var (user, student) = AddStudent(db);

        var r = await svc.VerifyAsync(Req(user, student, Vec(73), Vec(73), model: "m1"));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonOldApp, r.Reason);
        Assert.Equal(0, await db.Context.LoginFaceChecks.CountAsync());
        // Yozuv yo'q → controller endigina saqlagan selfi FAYLINI o'chiradi.
        Assert.False(r.Recorded);
    }

    /// <summary>
    /// ⚠️ <c>ReasonOldApp</c> IKKI joydan keladi va ular <b>ustma-ust tushmaydi</b>: model
    /// versiyasi mos kelmasa yozuv YOZILMAYDI (yuqoridagi test), vektor O'LCHAMI mos kelmasa esa
    /// yoziladi. Controller faylni o'chirish qarorini SABAB MATNI bo'yicha qabul qilganda
    /// ikkinchisida ham o'chirib yuborardi — jurnalda qator qolib, rasmi yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Vektor_olchami_mos_kelmasa_urinish_YOZILADI_va_fayl_saqlanadi()
    {
        var (db, svc) = NewService();
        using var _ = db;
        var (user, student) = AddStudent(db);

        // Etalon 128 o'lchamli, selfi esa 64 — model bir xil ("m1"), ya'ni 2-bosqichdan o'tadi.
        var profil = Vec(75);
        Assert.True((await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil))).Ok);

        var r = await svc.VerifyAsync(Req(user, student, Vec(76, dim: 64), image: "/uploads/face/kichik.jpg"));

        Assert.False(r.Ok);
        Assert.Equal(FaceMatch.ReasonOldApp, r.Reason);
        Assert.True(r.Recorded, "O'lcham mos kelmaganda urinish YOZILADI — fayl o'chirilmasin");
        Assert.True(await db.Context.LoginFaceChecks
            .AnyAsync(c => c.ImageUrl == "/uploads/face/kichik.jpg"));
    }

    /* =============================================================================================
     *  9. TOZALASH (maxfiylik)
     * ========================================================================================== */

    [Fact]
    public async Task Eski_selfilar_KeepChecks_dan_oshganda_tozalanadi()
    {
        var (db, svc) = NewService(keep: 3);
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(81);
        await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, image: "/uploads/a.jpg"));

        // Yana bir nechta urinish (chegaraga tegmasin uchun 4 tadan oshirmaymiz).
        for (var i = 0; i < 4; i++)
            await svc.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 10 + i),
                image: $"/uploads/b{i}.jpg"));

        // Bazada KeepChecks dan ortiq urinish qolmasin.
        var kept = await db.Context.LoginFaceChecks.CountAsync(c => c.StudentId == student.Id);
        Assert.True(kept <= 3, $"Tozalash ishlamadi: {kept} ta urinish qolgan");
    }

    [Fact]
    public async Task Tozalash_pending_urinishga_tegmaydi()
    {
        var (db, svc) = NewService(keep: 1);
        using var _ = db;
        var (user, student) = AddStudent(db, photo: null);

        // pending (etalon ham, profil rasmi ham yo'q)
        await svc.VerifyAsync(Req(user, student, Vec(91), image: "/uploads/pending.jpg"));
        // keyin sifat sababli rad etilgan urinishlar
        var qorongi = """{"faces":1,"brightness":10}""";
        for (var i = 0; i < 3; i++)
            await svc.VerifyAsync(Req(user, student, Vec(92), quality: qorongi, image: $"/uploads/x{i}.jpg"));

        Assert.Equal(1, await db.Context.LoginFaceChecks
            .CountAsync(c => c.Status == FaceLoginService.StatusPending));
    }

    [Fact]
    public async Task Tozalash_ochirilgan_fayl_manzillarini_qaytaradi_etalon_dalilini_esa_yoq()
    {
        var (db, svc) = NewService(keep: 1);
        using var _ = db;
        var (user, student) = AddStudent(db);
        var profil = Vec(95);

        // Birinchi muvaffaqiyatli urinish etalon bo'ladi (SampleUrl = /uploads/etalon.jpg).
        await svc.VerifyAsync(Req(user, student, Near(profil, 0.05), profil, image: "/uploads/etalon.jpg"));
        var r2 = await svc.VerifyAsync(Req(user, student, Near(profil, 0.05, seed: 21), image: "/uploads/keyingi.jpg"));

        // Etalon "dalili" O'CHIRILMAYDI — u yagona saqlanadigan surat.
        Assert.DoesNotContain("/uploads/etalon.jpg", r2.RemovedImages);
    }

    /* =============================================================================================
     *  10. CHEKLANGAN TOKEN DARVOZASI (FaceScopeGate)
     * ========================================================================================== */

    [Theory]
    [InlineData("/api/student/face/status", "GET")]
    [InlineData("/api/student/face/photo", "GET")]
    [InlineData("/api/student/face/challenge", "POST")]
    [InlineData("/api/student/face/verify", "POST")]
    [InlineData("/api/auth/logout", "POST")]
    public void FaceScopeGate_yuz_oqimiga_ruxsat_beradi(string path, string method) =>
        Assert.True(FaceScopeGate.IsAllowed(path, method));

    [Theory]
    [InlineData("/api/student/journal", "GET")]
    [InlineData("/api/student/profile", "GET")]
    [InlineData("/api/admin/students", "GET")]
    [InlineData("/api/student/face/verify", "GET")]      // noto'g'ri metod
    [InlineData("/api/student/face/challenge", "GET")]   // noto'g'ri metod
    [InlineData("/api/student/face", "GET")]
    [InlineData("/uploads/abc.jpg", "GET")]           // yuklangan fayllar ham yopiq
    [InlineData("/hubs/chat", "GET")]                 // SignalR: "/api" bilan boshlanmaydi
    [InlineData("/hubs/live", "POST")]
    public void FaceScopeGate_qolgan_hamma_narsani_bloklaydi(string path, string method) =>
        Assert.False(FaceScopeGate.IsAllowed(path, method));

    [Fact]
    public void FaceScopeGate_SPA_statikasiga_tegmaydi()
    {
        Assert.True(FaceScopeGate.IsAllowed("/", "GET"));
        Assert.True(FaceScopeGate.IsAllowed("/login", "GET"));
        Assert.True(FaceScopeGate.IsAllowed("/assets/index-abc.js", "GET"));
    }

    [Fact]
    public void Cheklangan_token_claimi_ikki_qatlamda_bir_xil()
    {
        // JwtTokenService (Infrastructure) Application'ga referens QILMAYDI — claim nomi
        // takrorlangan. Ular ayri ketsa darvoza jimgina ishlamay qolardi.
        Assert.Equal(FaceScopeGate.ClaimType, JwtTokenService.FaceScopeClaimType);
        Assert.Equal(FaceScopeGate.FaceScope, JwtTokenService.FaceScopeClaimValue);
    }

    /* =============================================================================================
     *  11. DARVOZALAR — manba matnidan (Server loyihasiga referens yo'q)
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

    [Fact]
    public void Admin_face_controlleri_oqishni_ham_darvozalaydi()
    {
        // Javobda selfi manzillari bor — GET xodimga ochiq QOLMASLIGI kerak.
        var src = ServerSource("Controllers", "AdminFaceController.cs");
        Assert.Matches(
            new Regex(@"\[AdminPerm\(\s*""students\.face""[^\]]*\bReadRequiresPerm\s*=\s*true\b[^\]]*\)\]"),
            src);
    }

    [Fact]
    public void Program_cheklangan_token_darvozasini_ornatgan()
    {
        var src = ServerSource("Program.cs");
        Assert.Contains("FaceScopeGate.IsAllowed", src);
        Assert.Contains("faceRequired", src);
    }

    [Fact]
    public void UploadsGuard_cheklangan_tokenni_rad_etadi()
    {
        // `/uploads` darvozasi pipeline'da UseAuthentication'dan OLDIN — FaceScopeGate
        // middleware'i uni ko'rmaydi, shuning uchun tekshiruv o'sha yerda ham bo'lishi SHART.
        var src = ServerSource("UploadsGuard.cs");
        Assert.Contains("FaceScopeClaimType", src);
    }

    /// <summary>
    /// ⚠️ YUZ DARVOZASI <b>HAR IKKALA</b> login yo'lida bo'lishi SHART: parol bilan ham, botdan
    /// olingan bir martalik KOD bilan ham (<c>otp-login</c>).
    ///
    /// <para>Aks holda modul bezakka aylanardi: login/parolini birovga bergan o'quvchi o'sha odamga
    /// bot kodini yuborib qo'ysa yetardi — selfi umuman so'ralmasdi. Bundan tashqari
    /// <c>FaceScopeGate</c> cheklangan token bilan <c>otp-login</c> ga ATAYIN ruxsat beradi (qayta
    /// login uchun), ya'ni selfi ekranidagi foydalanuvchi to'g'ridan-to'g'ri TO'LIQ token olib
    /// ketardi.</para>
    /// </summary>
    [Fact]
    public void OTP_login_ham_yuz_darvozasidan_otadi()
    {
        var src = ServerSource("Controllers", "AuthController.cs");
        var otp = src[src.IndexOf("public async Task<ActionResult<LoginResponse>> OtpLogin", StringComparison.Ordinal)..];

        Assert.Contains("face.DecideAsync", otp);
        Assert.Contains("CreateFaceScopedToken", otp);
        // Ishonchli qurilmada "oxirgi ko'rilgan" ham yangilanadi (parol yo'li bilan bir xil).
        Assert.Contains("face.TouchAsync", otp);
    }

    /// <summary>
    /// ⚠️ Saqlanadigan urinishlar soni SOATLIK CHEGARADAN kam bo'lmasligi kerak: chegara AYNAN
    /// o'sha jurnal qatorlaridan sanaladi (<c>RecentAttemptsAsync</c>). Admin uni 1 ga tushirsa
    /// hisob hech qachon chegaraga yetmasdi va brute-force himoyasi JIMGINA o'chib qolardi.
    /// </summary>
    [Fact]
    public void Sozlamada_KeepChecks_chegaradan_past_tusha_olmaydi()
    {
        var src = ServerSource("Controllers", "AdminFaceController.cs");
        Assert.Matches(
            new Regex(@"LoginFaceKeepChecks\s*=\s*Math\.Clamp\(\s*payload\.KeepChecks,\s*"
                      + @"FaceLoginService\.MaxAttemptsPerHour,\s*100\)"),
            src);
    }

    [Fact]
    public void Audit_bolimi_yuz_turlarini_taniydi()
    {
        Assert.Equal("students", AuditSections.SectionOf(FaceLoginService.AuditEntityProfile));
        Assert.Equal("students", AuditSections.SectionOf(FaceLoginService.AuditEntityDevice));
    }
}
