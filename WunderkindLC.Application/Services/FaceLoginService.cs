using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YUZ BILAN KIRISH — oqimning YAGONA joyi (login qarori, tekshirish, ishonchli qurilma, tozalash).
/// <see cref="FaceMatch"/> matematikani beradi, bu servis esa baza bilan ishlaydi. Controllerlar
/// (auth, o'quvchi, admin) faqat shu servisni chaqiradi, aks holda "qachon selfi so'raladi" qoidasi
/// uch joyda uch xil bo'lib ketardi.
///
/// <para><b>Oqim:</b></para>
/// <code>
/// login (deviceId bilan) → qurilma ishonchli EMAS va sozlama YOQILGAN
///     → CHEKLANGAN token (scope=face) + faceRequired:true
///     → POST /api/student/face/challenge   → { nonce, actions:["blink","turn_left"], expiresAt }
///     → ilova harakatlarni O'LCHAYDI, selfi oladi, TELEFONDA vektor hisoblaydi
///     → POST /api/student/face/verify (nonce + liveness + ixtiyoriy integrityToken bilan)
///         nonce/liveness/attestation darvozasi   → o'tmasa: rejected
///         etalon bor      → cosine(selfi, etalon)     >= chegara ? ruxsat : rad
///         etalon yo'q     → cosine(selfi, profil rasmi) >= chegara ? etalon SAQLANADI + ruxsat : rad
///         ikkalasi yo'q   → pending (admin tasdiqlaydi)
///     → ruxsat: qurilma ISHONCHLI + TO'LIQ token
/// </code>
///
/// <para>⚠️ <b>MAXFIYLIK:</b> selfi fayllari cheksiz to'planmaydi —
/// <see cref="CleanupAsync"/> <c>CenterMeta.LoginFaceKeepChecks</c> dan oshganini (yozuvi ham,
/// vektori ham) o'chiradi va fayl manzillarini qaytaradi, chaqiruvchi esa fayllarni diskdan
/// o'chiradi. Auditga selfi MANZILI hech qachon yozilmaydi.</para>
///
/// <para>⚠️ <b>VEKTORLAR SHIFRLANGAN</b> (<see cref="FaceVault"/>, kalit faqat `.env` da).
/// Kalit sozlanmagan bo'lsa <see cref="SettingsAsync"/> modulni O'CHIQ deb qaytaradi — ochiq
/// matnda saqlashga tushib qolish yo'li YO'Q.</para>
/// </summary>
public class FaceLoginService(IAppDbContext db, FaceVault vault)
{
    /* =============================================================================================
     *  Konstantalar
     * ========================================================================================== */

    /// <summary>Urinish holatlari — <see cref="LoginFaceCheck.Status"/>.</summary>
    public const string StatusApproved = FaceMatch.StatusApproved;
    public const string StatusRejected = FaceMatch.StatusRejected;
    public const string StatusPending = FaceMatch.StatusPending;

    /// <summary>Etalon manbai — <see cref="StudentFaceProfile.Source"/>.</summary>
    public const string SourcePhoto = "photo";
    public const string SourceAdmin = "admin";

    /// <summary>Login javobidagi <c>faceStatus</c>: birinchi marta (etalon yo'q) yoki odatiy tekshiruv.</summary>
    public const string FaceStatusEnroll = "enroll";
    public const string FaceStatusVerify = "verify";

    /// <summary>Bir o'quvchi uchun SOATIGA ruxsat etilgan tekshiruv urinishlari.
    /// ⚠️ Faqat SOLISHTIRISHGACHA yetgan urinishlar sanaladi (qarang <see cref="RecentAttemptsAsync"/>).</summary>
    public const int MaxAttemptsPerHour = 5;

    /// <summary>
    /// Bir akkaunt uchun SOATIGA beriladigan tiriklik chaqiruvlari (nonce).
    ///
    /// <para>⚠️ Nega alohida chegara? Tiriklik/attestation darvozasidan o'tmagan urinish
    /// <see cref="MaxAttemptsPerHour"/> ni YEMAYDI (solishtirish umuman bo'lmagan — sifat sababli
    /// rad etilgan kadr bilan bir xil siyosat). Aks holda hujumchi cheksiz urinardi: har safar
    /// yangi nonce olib, harakatlarni taxmin qilib ko'raverardi. Shu sabab chegara chaqiruv
    /// BERISH bosqichida turadi — 15 ta yetadi (odatiy foydalanuvchiga 1-2 tasi kerak).</para>
    /// </summary>
    public const int MaxChallengesPerHour = 15;

    /// <summary>Audit turlari — <see cref="AuditSections"/> da "O'quvchilar" bo'limiga xaritalangan.</summary>
    public const string AuditEntityProfile = "StudentFaceProfile";
    public const string AuditEntityDevice = "TrustedDevice";

    private static string NowIso() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    /* =============================================================================================
     *  Sozlamalar
     * ========================================================================================== */

    /// <summary>Markaz sozlamalari (CenterMeta). Yozuv umuman bo'lmasa — standart qiymatlar
    /// (modul O'CHIQ) qaytadi, ya'ni sozlanmagan bazada kirish odatdagidek ishlaydi.</summary>
    /// <param name="Enabled">Modul HAQIQATAN ishlayaptimi — sozlama YOQILGAN <b>va</b> shifrlash
    /// kaliti bor. Butun oqim (login qarori, verify) SHUNGA qaraydi.</param>
    /// <param name="EnabledSetting">Bazadagi XOM sozlama (admin panelidagi tugma holati).
    /// ⚠️ <paramref name="Enabled"/> dan farq qilishi mumkin: kalit yo'q bo'lsa tugma "yoqilgan"
    /// bo'lib turadi-yu, modul ishlamaydi — UI aynan shu farqni ko'rsatib turishi kerak.</param>
    /// <param name="RequireLiveness">Bir martalik nonce + harakatlar MAJBURIYmi (default TRUE).</param>
    /// <param name="RequireAttestation">Play Integrity MAJBURIYmi (default FALSE).</param>
    /// <param name="VaultReady">`FACE_VECTOR_KEY` sozlanganmi (admin sahifasida sabab ko'rsatish uchun).</param>
    public readonly record struct FaceSettings(
        bool Enabled, double Threshold, string ModelVersion, int KeepChecks,
        bool RequireLiveness, bool RequireAttestation, bool VaultReady, bool EnabledSetting);

    /// <summary>Chegara sozlanmagan/buzuq bo'lsa ishlatiladigan xavfsiz qiymat.</summary>
    public const double DefaultThreshold = 0.60;

    public async Task<FaceSettings> SettingsAsync(CancellationToken ct = default)
    {
        // ⚠️ KALIT YO'Q — MODUL YO'Q. Yuz vektori biometrik ma'lumot va u bazaga faqat
        // SHIFRLANGAN holda yoziladi (`FaceVault`). Kalit sozlanmagan bo'lsa "vaqtincha ochiq
        // saqlaymiz" degan variant YO'Q: modul o'chiq bo'ladi va kirish odatdagidek (parol
        // bilan) ishlayveradi. Startupda ogohlantirish logi yoziladi (Program.cs).
        var vaultReady = vault.Configured;

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null)
            return new FaceSettings(false, DefaultThreshold, "", 5, true, false, vaultReady, false);

        // ⚠️ Chegara "yo'q" bo'lsa (0, manfiy yoki NaN — masalan qo'lda SQL bilan tuzatilgan
        // baza) uni QISIB EMAS, STANDART qiymatga qaytaramiz. Qisib qo'yilsa 0 → 0.05 bo'lardi,
        // ya'ni har qanday yuz "mos" chiqib, modul jimgina ISHLAMAY qo'yardi.
        var raw = meta.LoginFaceThreshold;
        var threshold = double.IsNaN(raw) || raw <= 0 ? DefaultThreshold : Math.Min(raw, 0.99);
        var keep = meta.LoginFaceKeepChecks <= 0 ? 5 : Math.Min(meta.LoginFaceKeepChecks, 100);
        return new FaceSettings(
            meta.LoginFaceEnabled && vaultReady, threshold, meta.LoginFaceModelVersion ?? "", keep,
            meta.LoginFaceRequireLiveness, meta.LoginFaceRequireAttestation, vaultReady,
            meta.LoginFaceEnabled);
    }

    /* =============================================================================================
     *  LOGIN QARORI — "shu qurilmada selfi so'ralsinmi"
     * ========================================================================================== */

    /// <summary>Login javobiga qo'shiladigan qaror.</summary>
    /// <param name="Required">Yuz tasdig'i talab qilinadimi (true bo'lsa token CHEKLANGAN beriladi).</param>
    /// <param name="Status">enroll (etalon yo'q — birinchi marta) | verify.</param>
    public readonly record struct LoginDecision(bool Required, string Status);

    /// <summary>
    /// O'QUVCHI logini uchun qaror.
    ///
    /// <para>⚠️ <b>ESKI KLIENTLAR</b> (<paramref name="deviceId"/> yubormaydigan ilova versiyalari)
    /// uchun xatti-harakat O'ZGARMAYDI — yuz so'ralmaydi. Sabab: qurilmani ajrata olmasak "yangi
    /// qurilma" tushunchasi yo'q, va har kirishda selfi so'rab, yangilanmagan ilovadagi barcha
    /// o'quvchini tizimdan chiqarib qo'yardik. Ilova yangilangach deviceId keladi va darvoza
    /// o'zi ishlay boshlaydi.</para>
    /// </summary>
    public async Task<LoginDecision> DecideAsync(
        AppUser user, string? deviceId, CancellationToken ct = default)
    {
        if (user.Role != Roles.Student) return new LoginDecision(false, "");

        var id = (deviceId ?? "").Trim();
        if (id.Length == 0) return new LoginDecision(false, "");

        var settings = await SettingsAsync(ct);
        if (!settings.Enabled) return new LoginDecision(false, "");

        var studentId = await db.Students.AsNoTracking()
            .Where(s => s.UserId == user.Id).Select(s => s.Id).FirstOrDefaultAsync(ct);
        // O'quvchi yozuvi topilmasa (akkaunt bor, profil yo'q) — kirishni bloklamaymiz: modul
        // shaxsni tasdiqlaydi, akkauntni butunlay yopish uchun boshqa vositalar bor.
        if (string.IsNullOrEmpty(studentId)) return new LoginDecision(false, "");

        if (await IsTrustedAsync(user.Id, id, ct)) return new LoginDecision(false, "");

        var enrolled = await db.StudentFaceProfiles.AsNoTracking().AnyAsync(p => p.StudentId == studentId, ct);
        return new LoginDecision(true, enrolled ? FaceStatusVerify : FaceStatusEnroll);
    }

    /* =============================================================================================
     *  ISHONCHLI QURILMALAR
     * ========================================================================================== */

    /// <summary>Qurilma ishonchlimi (bekor qilinmagan yozuv bormi).</summary>
    public async Task<bool> IsTrustedAsync(string userId, string deviceId, CancellationToken ct = default) =>
        await db.TrustedDevices.AsNoTracking()
            .AnyAsync(d => d.UserId == userId && d.DeviceId == deviceId
                           && (d.RevokedAt == null || d.RevokedAt == ""), ct);

    /// <summary>
    /// Qurilmani ISHONCHLI deb belgilaydi (yoki mavjudini yangilaydi/tiklaydi).
    /// <c>SaveChanges</c> QILMAYDI — chaqiruvchining tranzaksiyasida saqlanadi.
    /// </summary>
    public async Task TrustAsync(
        string userId, string deviceId, string deviceName, string platform, CancellationToken ct = default)
    {
        var id = (deviceId ?? "").Trim();
        if (id.Length == 0) return;

        var now = NowIso();
        var existing = await db.TrustedDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == id, ct);
        if (existing is null)
        {
            db.TrustedDevices.Add(new TrustedDevice
            {
                UserId = userId,
                DeviceId = id,
                DeviceName = deviceName ?? "",
                Platform = platform ?? "",
                CreatedAt = now,
                LastSeenAt = now,
            });
            return;
        }

        // Bekor qilingan qurilma yuz bilan qayta tasdiqlansa — tiklanadi (yangi qator yaratilmaydi:
        // (UserId, DeviceId) unikal, aks holda saqlash xatosi chiqardi).
        existing.RevokedAt = null;
        existing.LastSeenAt = now;
        if (!string.IsNullOrWhiteSpace(deviceName)) existing.DeviceName = deviceName;
        if (!string.IsNullOrWhiteSpace(platform)) existing.Platform = platform;
    }

    /// <summary>Ishonchli qurilmadan kirilganda "oxirgi ko'rilgan" vaqtini yangilaydi
    /// (admin ro'yxatida "bu telefon hali ishlatilyaptimi" ko'rinsin). SaveChanges QILMAYDI.</summary>
    public async Task TouchAsync(string userId, string? deviceId, CancellationToken ct = default)
    {
        var id = (deviceId ?? "").Trim();
        if (id.Length == 0) return;
        var d = await db.TrustedDevices.FirstOrDefaultAsync(x => x.UserId == userId && x.DeviceId == id, ct);
        if (d is not null) d.LastSeenAt = NowIso();
    }

    /* =============================================================================================
     *  URINISHLAR CHEGARASI
     * ========================================================================================== */

    /// <summary>
    /// Oxirgi bir soatdagi urinishlar soni.
    ///
    /// <para>⚠️ FAQAT SOLISHTIRISHGACHA yetgan urinishlar sanaladi (<c>Score</c> hisoblangan yoki
    /// <c>pending</c>). Sifat sababli rad etilgan kadr (qorong'i xona, xira rasm) chegarani
    /// yemaydi — aks holda yomon yorug'likdagi foydalanuvchi 5 ta noto'g'ri kadrdan keyin bir
    /// soatga tizimdan chiqib qolardi, holbuki hech qanday "urinish" bo'lmagan edi.</para>
    /// </summary>
    public async Task<int> RecentAttemptsAsync(string studentId, CancellationToken ct = default)
    {
        var since = AppClock.Now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        return await db.LoginFaceChecks.AsNoTracking()
            .CountAsync(c => c.StudentId == studentId
                             && string.Compare(c.CreatedAt, since) >= 0
                             && (c.Score != null || c.Status == StatusPending), ct);
    }

    /// <summary>
    /// Urinish SOATLIK CHEGARAGA kiradimi (<see cref="RecentAttemptsAsync"/> dagi shart bilan
    /// AYNAN bir xil bo'lishi SHART).
    ///
    /// <para>⚠️ Nega alohida funksiya? Chegarani <b>tozalash</b> ham bilishi kerak: hisoblanadigan
    /// qatorni o'chirib yuborish hisobni orqaga qaytaradi (qarang <see cref="CleanupAsync"/>).</para>
    /// </summary>
    private static bool CountsTowardLimit(LoginFaceCheck c) =>
        c.Score is not null || c.Status == StatusPending;

    /* =============================================================================================
     *  TIRIKLIK CHAQIRUVI (challenge) — bir martalik nonce + tasodifiy harakatlar
     * ========================================================================================== */

    /// <summary>Chaqiruv natijasi. <paramref name="Ok"/> false bo'lsa <paramref name="Reason"/>
    /// foydalanuvchiga ko'rsatiladi (chegara tugagan).</summary>
    public readonly record struct ChallengeResult(
        bool Ok, string Reason, string Nonce, IReadOnlyList<string> Actions, string ExpiresAt);

    /// <summary>
    /// Yangi chaqiruv beradi: TASODIFIY nonce + TASODIFIY harakatlar (tartibi ham tasodifiy).
    /// <c>SaveChanges</c> O'ZI chaqiriladi — nonce bazada bo'lmasa <c>verify</c> uni topa olmaydi.
    ///
    /// <para>Jadval cheksiz o'smasligi uchun shu yerda eski qatorlar ham tozalanadi.</para>
    ///
    /// <para>⚠️ Tozalash chegarasi <b>BIR SOAT</b>, "muddati o'tgan" (90 s) EMAS. Sabab:
    /// soatlik chegara AYNAN shu qatorlarni sanaydi — 90 soniyalik tozalash hisobni har safar
    /// nolga qaytarib, <see cref="MaxChallengesPerHour"/> ni JIMGINA ishlamaydigan qilib
    /// qo'yardi (hujumchi cheksiz chaqiruv olaverardi).</para>
    /// </summary>
    public async Task<ChallengeResult> IssueChallengeAsync(
        string userId, string studentId, CancellationToken ct = default)
    {
        var since = AppClock.Now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        var issued = await db.FaceChallenges.AsNoTracking()
            .CountAsync(c => c.UserId == userId && string.Compare(c.CreatedAt, since) >= 0, ct);
        if (issued >= MaxChallengesPerHour)
            return new ChallengeResult(false, FaceMatch.ReasonTooManyAttempts, "", [], "");

        // Bir soatdan eski qatorlar — na chegaraga, na tekshiruvga kerak (muddati allaqachon
        // o'tgan, ya'ni ular bilan kirib bo'lmaydi).
        var nowIso = NowIso();
        var stale = await db.FaceChallenges
            .Where(c => c.UserId == userId && string.Compare(c.CreatedAt, since) < 0)
            .ToListAsync(ct);
        if (stale.Count > 0) db.FaceChallenges.RemoveRange(stale);

        var actions = FaceLiveness.Pick(Random.Shared);
        var challenge = new FaceChallenge
        {
            UserId = userId,
            StudentId = studentId,
            Nonce = NewNonce(),
            ActionsJson = FaceLiveness.Encode(actions),
            CreatedAt = nowIso,
            ExpiresAt = AppClock.Now.AddSeconds(FaceLiveness.ChallengeTtlSeconds)
                .ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.FaceChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        return new ChallengeResult(true, "", challenge.Nonce, actions, challenge.ExpiresAt);
    }

    /// <summary>Tasodifiy 32 bayt → base64url (URL/JSON'da xavfsiz, taxmin qilib bo'lmaydi).</summary>
    private static string NewNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Nonce'ni topadi, EGALIGINI/muddatini tekshiradi va ISHLATILGAN deb belgilaydi.
    /// <c>SaveChanges</c> QILMAYDI — chaqiruvchi (VerifyAsync) o'z tranzaksiyasida saqlaydi.
    ///
    /// <para>⚠️ Nonce urinish MUVAFFAQIYATSIZ bo'lsa ham ishlatilgan bo'lib qoladi: aks holda
    /// hujumchi bitta nonce bilan harakatlarni taxmin qilib cheksiz urinardi.</para>
    /// </summary>
    private async Task<(FaceChallenge? Challenge, string? Error)> ConsumeChallengeAsync(
        string userId, string? nonce, CancellationToken ct)
    {
        var n = (nonce ?? "").Trim();
        if (n.Length == 0) return (null, FaceMatch.ReasonNoChallenge);

        var challenge = await db.FaceChallenges.FirstOrDefaultAsync(c => c.Nonce == n, ct);
        // Begona foydalanuvchining nonce'i — "topilmadi" bilan BIR XIL javob (kimning nonce'i
        // ekanini oshkor qilmaymiz).
        if (challenge is null || challenge.UserId != userId) return (null, FaceMatch.ReasonNoChallenge);
        if (!string.IsNullOrEmpty(challenge.UsedAt)) return (null, FaceMatch.ReasonNoChallenge);
        if (string.Compare(challenge.ExpiresAt, NowIso(), StringComparison.Ordinal) < 0)
            return (null, FaceMatch.ReasonNoChallenge);

        challenge.UsedAt = NowIso();
        return (challenge, null);
    }

    /* =============================================================================================
     *  TEKSHIRISH (verify)
     * ========================================================================================== */

    /// <summary>Klientdan kelgan tekshiruv so'rovi (fayl allaqachon saqlangan — <paramref name="ImageUrl"/>).</summary>
    /// <param name="Nonce">`challenge` dan olingan bir martalik nonce (bo'sh bo'lishi mumkin —
    /// sozlama <c>RequireLiveness=false</c> bo'lgandagina o'tadi).</param>
    /// <param name="LivenessJson">Harakatlar natijasi: <c>[{"action":"blink","ok":true,"ms":900}]</c>.</param>
    /// <param name="AttestVerdict">Controller hisoblagan ilova haqiqiyligi xulosasi.</param>
    /// <param name="AttestReason">Xulosaning qisqa sababi (jurnalga yoziladi).</param>
    public sealed record VerifyRequest(
        Student Student,
        string UserId,
        string ImageUrl,
        float[] Selfie,
        float[]? RefVector,
        string QualityJson,
        string ModelVersion,
        string DeviceId,
        string DeviceName,
        string Platform,
        string AppVersion,
        string Ip,
        string Nonce = "",
        string LivenessJson = "",
        AppAttestation.Verdict AttestVerdict = AppAttestation.Verdict.NotConfigured,
        string AttestReason = "");

    /// <summary>Tekshiruv natijasi.</summary>
    /// <param name="AttemptsLeft">Shu soatda qolgan urinishlar (0 — chegara tugadi).</param>
    /// <param name="RemovedImages">Tozalashda o'chirilgan eski selfi manzillari — chaqiruvchi
    /// FAYLLARNI diskdan o'chiradi.</param>
    /// <param name="Recorded">Shu urinish uchun <c>LoginFaceCheck</c> qatori YOZILDIMI.
    /// <c>false</c> bo'lsa chaqiruvchi endigina saqlagan selfi faylini O'CHIRADI — unga
    /// ishora qiladigan yozuv yo'q, ya'ni u diskda "egasiz" qolib ketardi.
    ///
    /// <para>⚠️ Bu bayroq ATAYIN bor: ilgari controller <b>sabab matni</b> bo'yicha taxmin
    /// qilardi (<c>ReasonTooManyAttempts</c> yoki <c>ReasonOldApp</c> bo'lsa fayl o'chiriladi).
    /// <c>ReasonOldApp</c> esa IKKI joydan keladi — model versiyasi mos kelmasa (yozuv YO'Q) va
    /// vektor o'lchami mos kelmasa (<c>FaceMatch.Evaluate</c>, yozuv BOR). Ikkinchisida fayl
    /// o'chib ketar, admin esa urinish ro'yxatida buzuq rasm ko'rardi.</para></param>
    public readonly record struct VerifyResult(
        bool Ok, string Status, string Reason, double? Score, int AttemptsLeft,
        bool Enrolled, IReadOnlyList<string> RemovedImages, bool Recorded);

    /// <summary>
    /// Saqlangan etalon HOZIR ishlatishga yaroqlimi — ya'ni uni yaratgan model markaz kutayotgan
    /// model bilan bir xilmi. Turli modellarning vektorlarini solishtirish tasodifiy natija beradi.
    ///
    /// <para>⚠️ <b>BU YAGONA MANBA — ikkita joyda AYRIM hisoblamang.</b> Ilgari
    /// <c>GET /student/face/status</c> etalonni shunchaki "qatori bormi" deb sanardi
    /// (<c>AnyAsync(p =&gt; p.StudentId == me.Id)</c>), <see cref="VerifyAsync"/> esa model
    /// versiyasini ham tekshirardi. Model almashganda ikkalasi AYRI javob berardi va oqim
    /// jimgina buzilardi: <c>status</c> «etalon bor» deydi → ilova profil rasmidan
    /// <c>refVector</c> YUBORMAYDI → <c>verify</c> da esa etalon yaroqsiz, <c>refVector</c> ham
    /// yo'q → har bir o'quvchi <c>pending</c> ga tushib, admin tasdig'ini kutib qolardi.
    /// Holbuki profil rasmi joyida turgan va hammasi avtomatik hal bo'lishi kerak edi.</para>
    ///
    /// <para>Markaz modeli belgilanmagan bo'lsa (bo'sh satr) tekshirilmaydi — bu ATAYIN
    /// "o'chirilgan" holat.</para>
    /// </summary>
    public static bool TemplateUsable(string? templateModelVersion, string? centerModelVersion) =>
        string.IsNullOrEmpty(centerModelVersion)
        || string.Equals(templateModelVersion, centerModelVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Selfini tekshiradi, urinishni yozadi va (muvaffaqiyatda) qurilmani ishonchli qiladi.
    /// SaveChanges O'ZI chaqiriladi — oqim bitta tranzaksiyada tugasin.
    /// </summary>
    public async Task<VerifyResult> VerifyAsync(VerifyRequest req, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var studentId = req.Student.Id;
        var quality = FaceMatch.ParseQuality(req.QualityJson);

        // 1. Chegara — solishtirishdan OLDIN (rad etilgan urinish ham selfi saqlab bo'lgan, lekin
        //    hech bo'lmasa vektor solishtiruvi qilinmaydi va yangi yozuv qo'shilmaydi).
        var used = await RecentAttemptsAsync(studentId, ct);
        if (used >= MaxAttemptsPerHour)
            return new VerifyResult(false, StatusRejected, FaceMatch.ReasonTooManyAttempts,
                null, 0, false, Array.Empty<string>(), Recorded: false);

        // 2. Model mos kelmasa — solishtirish MA'NOSIZ (turli modellar vektorlari taqqoslanmaydi).
        //    Yozuv qo'shilmaydi: bu shaxs urinishi emas, ilova versiyasi muammosi.
        if (!string.IsNullOrEmpty(settings.ModelVersion)
            && !string.Equals(settings.ModelVersion, req.ModelVersion, StringComparison.OrdinalIgnoreCase))
            return new VerifyResult(false, StatusRejected, FaceMatch.ReasonOldApp,
                null, MaxAttemptsPerHour - used, false, Array.Empty<string>(), Recorded: false);

        // 2.5. TIRIKLIK (nonce + harakatlar) va ILOVA HAQIQIYLIGI.
        //
        //  ⚠️ TARTIB: bu darvoza SIFAT tekshiruvidan ham OLDIN turadi. Sabab — u kadr haqida emas,
        //  SO'ROVNING O'ZI haqida ("bu haqiqiy odam va haqiqiy ilovami"). Sifat esa qulaylik
        //  uchun; soxta so'rovga "yorug'roq joyda oling" deb maslahat berishning ma'nosi yo'q.
        //
        //  ⚠️ Bu urinishlar chegarasini YEMAYDI (Score = null) — solishtirish umuman bo'lmagan.
        //  Cheksiz urinishdan `MaxChallengesPerHour` himoya qiladi (nonce BERISH bosqichida).
        var gateReason = await AuthenticityGateAsync(req, settings, quality.FaceRatio, ct);
        if (gateReason is not null)
        {
            AddCheck(req, StatusRejected, gateReason, null, settings.ModelVersion, storeVector: false);
            var cleanedG = await CleanupAsync(studentId, settings.KeepChecks - 1, ct);
            if (!await TrySaveAsync(ct)) return NonceRace(used);
            return new VerifyResult(false, StatusRejected, gateReason,
                null, MaxAttemptsPerHour - used, false, cleanedG, Recorded: true);
        }

        // 3. Kadr sifati — foydalanuvchiga aniq maslahat beradi. Urinish YOZILADI (admin "nega
        //    kira olmayapti" ni ko'rsin), lekin chegarani yemaydi (Score = null).
        var qualityReason = FaceMatch.Reject(quality, FaceMatch.DefaultLimits);
        if (qualityReason is not null)
        {
            AddCheck(req, StatusRejected, qualityReason, null, settings.ModelVersion, storeVector: false);
            // KeepChecks - 1: yangi qo'shilgan urinish hali BAZADA yo'q (SaveChanges qilinmagan),
            // ya'ni u so'rov natijasiga tushmaydi — o'rnini oldindan bo'shatib qo'yamiz.
            var cleanedQ = await CleanupAsync(studentId, settings.KeepChecks - 1, ct);
            if (!await TrySaveAsync(ct)) return NonceRace(used);
            return new VerifyResult(false, StatusRejected, qualityReason,
                null, MaxAttemptsPerHour - used, false, cleanedQ, Recorded: true);
        }

        // 4. Etalon (bo'lsa) va qaror.
        var profile = await db.StudentFaceProfiles.FirstOrDefaultAsync(p => p.StudentId == studentId, ct);
        // Model almashgan bo'lsa eski etalon YAROQSIZ — u bilan solishtirish tasodifiy natija
        // berardi. Bunday holatda etalon yo'q deb qaraladi va profil rasmi orqali qayta olinadi.
        // ⚠️ `Unprotect` null qaytarsa (kalit almashgan / blob buzuq) — bu ISTISNO EMAS: etalon
        // "yo'q" deb qaraladi va o'quvchi profil rasmi orqali qayta ro'yxatdan o'tadi.
        var enrolledVector = profile is not null && TemplateUsable(profile.ModelVersion, settings.ModelVersion)
            ? vault.Unprotect(profile.Vector)
            : null;

        var outcome = FaceMatch.Evaluate(req.Selfie, enrolledVector, req.RefVector, settings.Threshold);

        // 5. Yozuv. Vektor FAQAT kerak bo'lganda saqlanadi: `pending` urinishni admin tasdiqlaganda
        //    aynan shu vektor etalon bo'ladi. Rad etilgan urinishda vektor saqlanmaydi (biometrik
        //    ma'lumot keraksiz to'planmasin).
        AddCheck(req, outcome.Status, outcome.Reason, outcome.Score, settings.ModelVersion,
            storeVector: outcome.Status == StatusPending);

        var enrolledNow = false;
        if (outcome.Ok)
        {
            if (outcome.Enroll)
            {
                UpsertProfile(profile, studentId, req.Selfie, settings.ModelVersion, SourcePhoto, req.ImageUrl);
                enrolledNow = true;
            }
            await TrustAsync(req.UserId, req.DeviceId, req.DeviceName, req.Platform, ct);
        }

        var cleaned = await CleanupAsync(studentId, settings.KeepChecks - 1, ct);
        if (!await TrySaveAsync(ct)) return NonceRace(used);

        var left = outcome.Score is null && outcome.Status != StatusPending
            ? MaxAttemptsPerHour - used
            : MaxAttemptsPerHour - used - 1;
        return new VerifyResult(outcome.Ok, outcome.Status, outcome.Reason, outcome.Score,
            Math.Max(0, left), enrolledNow, cleaned, Recorded: true);
    }

    /// <summary>
    /// Saqlashga urinadi. <c>false</c> — <b>bir martalik nonce ayni shu paytda BOSHQA so'rovda
    /// ishlatilgan</b>.
    ///
    /// <para>⚠️ Nega kerak? <see cref="ConsumeChallengeAsync"/> nonce'ni "ishlatilgan" deb
    /// belgilaydi, lekin saqlash keyinroq bo'ladi — orada <c>await</c> bor. Ya'ni AYNI bir nonce
    /// bilan yuborilgan IKKI parallel <c>verify</c> so'rovi ikkalasi ham "ishlatilmagan" ni ko'rib
    /// o'tib ketardi va "nonce BIR MARTA ishlatiladi" kafolati buzilardi (bir marta yozib olingan
    /// tiriklik sessiyasini ikki marta ishlatish mumkin bo'lardi).</para>
    ///
    /// <para>Yechim <c>Book.Stock</c> dagi bilan bir xil: <c>FaceChallenge.UsedAt</c> —
    /// KONKURENTLIK TOKENI (<c>AppDbContext</c>), ya'ni EF <c>… WHERE UsedAt IS NULL</c> yozadi va
    /// ikkinchi so'rov 0 qator yangilab, istisno oladi. <b>Migratsiya kerak emas</b> — bu faqat
    /// model metadatasi.</para>
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>Nonce poygasida yutqazgan so'rov uchun javob: yozuv SAQLANMADI (tranzaksiya
    /// qaytdi), demak selfi fayli ham keraksiz — <c>Recorded: false</c>.</summary>
    private static VerifyResult NonceRace(int used) => new(
        false, StatusRejected, FaceMatch.ReasonNoChallenge, null,
        Math.Max(0, MaxAttemptsPerHour - used), false, Array.Empty<string>(), Recorded: false);

    /// <summary>
    /// HAQIQIYLIK DARVOZASI: bir martalik nonce + tiriklik harakatlari + ilova attestation'i.
    /// <c>null</c> — o'tdi, aks holda foydalanuvchiga ko'rsatiladigan sabab.
    ///
    /// <para><b>Orqaga moslik:</b> <paramref name="settings"/> da <c>RequireLiveness=false</c>
    /// bo'lsa VA klient nonce yubormagan bo'lsa — darvoza o'tkazadi (modul liveness'siz ham
    /// ishlayveradi). Lekin nonce YUBORILGAN bo'lsa u HAR DOIM tekshiriladi: yaroqsiz nonce
    /// bilan kelgan so'rovni "e'tiborsiz qoldirish" soxta klient uchun bepul yo'l bo'lardi.</para>
    /// </summary>
    /// <param name="baselineFaceRatio">Yakuniy kadrdagi <c>faceRatio</c> — «yaqinlashing /
    /// orqaga suriling» topshiriqlari uchun boshlang'ich masofa.</param>
    private async Task<string?> AuthenticityGateAsync(
        VerifyRequest req, FaceSettings settings, double baselineFaceRatio, CancellationToken ct)
    {
        // --- ILOVA HAQIQIYLIGI (sozlama o'chiq bo'lsa faqat jurnalga yoziladi) ---
        if (AppAttestation.Gate(req.AttestVerdict, settings.RequireAttestation) is { } attestReason)
            return attestReason;

        // --- TIRIKLIK ---
        var nonce = (req.Nonce ?? "").Trim();
        if (nonce.Length == 0)
            return settings.RequireLiveness ? FaceMatch.ReasonNoChallenge : null;

        var (challenge, error) = await ConsumeChallengeAsync(req.UserId, nonce, ct);
        if (error is not null) return error;

        return FaceLiveness.Check(challenge!.ActionsJson, req.LivenessJson, baselineFaceRatio);
    }

    private LoginFaceCheck AddCheck(
        VerifyRequest req, string status, string reason, double? score, string modelVersion, bool storeVector)
    {
        var check = new LoginFaceCheck
        {
            StudentId = req.Student.Id,
            UserId = req.UserId,
            CreatedAt = NowIso(),
            DeviceId = req.DeviceId,
            DeviceName = req.DeviceName,
            Platform = req.Platform,
            AppVersion = req.AppVersion,
            Ip = req.Ip,
            ImageUrl = req.ImageUrl,
            Score = score,
            ModelVersion = string.IsNullOrEmpty(modelVersion) ? req.ModelVersion : modelVersion,
            Status = status,
            Reason = reason,
            Quality = req.QualityJson ?? "",
            // Vektor SHIFRLANGAN holda yoziladi (etalon bilan bir xil format).
            Vector = storeVector ? vault.Protect(req.Selfie) : null,
            Dim = storeVector ? req.Selfie.Length : 0,
            // Attestation natijasi sozlama o'chiq bo'lsa ham YOZILADI — admin "qancha urinish
            // o'zgartirilgan ilovadan keladi" ni ko'rib, majburiy qilishga qaror qila olsin.
            Attested = AppAttestation.Code(req.AttestVerdict),
            AttestReason = req.AttestReason ?? "",
        };
        db.LoginFaceChecks.Add(check);
        return check;
    }

    /// <summary>Etalonni yaratadi yoki almashtiradi. SaveChanges QILMAYDI.</summary>
    private void UpsertProfile(
        StudentFaceProfile? existing, string studentId, float[] vector,
        string modelVersion, string source, string sampleUrl)
    {
        var now = NowIso();
        if (existing is null)
        {
            db.StudentFaceProfiles.Add(new StudentFaceProfile
            {
                StudentId = studentId,
                Vector = vault.Protect(vector),
                Dim = vector.Length,
                ModelVersion = modelVersion,
                Source = source,
                SampleUrl = sampleUrl,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return;
        }
        existing.Vector = vault.Protect(vector);
        existing.Dim = vector.Length;
        existing.ModelVersion = modelVersion;
        existing.Source = source;
        existing.SampleUrl = sampleUrl;
        existing.UpdatedAt = now;
    }

    /* =============================================================================================
     *  ADMIN AMALLARI
     * ========================================================================================== */

    /// <summary>
    /// <c>pending</c> urinishni tasdiqlaydi — o'sha selfi ETALON bo'ladi.
    /// <c>null</c> qaytsa muvaffaqiyat, aks holda foydalanuvchiga ko'rsatiladigan xato matni
    /// (<see cref="BookSalesService"/> bilan bir xil uslub). SaveChanges QILMAYDI —
    /// chaqiruvchi audit yozuvi bilan birga saqlaydi.
    /// </summary>
    public async Task<string?> ApproveCheckAsync(LoginFaceCheck check, CancellationToken ct = default)
    {
        if (check.Status != StatusPending) return "Faqat kutilayotgan urinishni tasdiqlash mumkin";
        // ⚠️ `null` = shifr ochilmadi (kalit almashgan/yo'qolgan). Bu holda etalon yasab
        // bo'lmaydi — adminga tushunarli matn qaytadi, istisno TASHLANMAYDI.
        var vector = vault.Unprotect(check.Vector);
        if (vector is null) return "Urinish vektorini ochib bo'lmadi (FACE_VECTOR_KEY o'zgargan) — o'quvchi qayta ro'yxatdan o'tsin";
        if (FaceMatch.Validate(vector) is { } err) return $"Urinish vektori yaroqsiz: {err}";

        var profile = await db.StudentFaceProfiles
            .FirstOrDefaultAsync(p => p.StudentId == check.StudentId, ct);
        UpsertProfile(profile, check.StudentId, vector, check.ModelVersion, SourceAdmin, check.ImageUrl);

        check.Status = StatusApproved;
        check.Reason = "Administrator tasdiqladi";
        // Vektor endi etalonda — urinish yozuvida saqlab turishning hojati yo'q (biometrik
        // ma'lumot nusxalari kamaysin).
        check.Vector = null;
        check.Dim = 0;
        return null;
    }

    /// <summary>Kutilayotgan urinishni rad etadi. SaveChanges QILMAYDI.</summary>
    public string? RejectCheck(LoginFaceCheck check, string? note)
    {
        if (check.Status != StatusPending) return "Faqat kutilayotgan urinishni rad etish mumkin";
        check.Status = StatusRejected;
        check.Reason = string.IsNullOrWhiteSpace(note) ? "Administrator rad etdi" : note.Trim();
        check.Vector = null;
        check.Dim = 0;
        return null;
    }

    /* =============================================================================================
     *  TOZALASH (maxfiylik)
     * ========================================================================================== */

    /// <summary>
    /// O'quvchi bo'yicha BAZADAGI eng so'nggi <paramref name="keep"/> ta urinishdan boshqasini
    /// o'chiradi va o'chirilgan selfi MANZILLARINI qaytaradi (fayllarni chaqiruvchi o'chiradi).
    /// SaveChanges QILMAYDI.
    ///
    /// <para>⚠️ <c>pending</c> urinishlar HECH QACHON o'chirilmaydi va chegaraga ham kirmaydi —
    /// ular admin qaroriga muhtoj; tozalash ularni yeb qo'ysa o'quvchi hech qachon kira olmasdi.</para>
    /// <para>⚠️ Etalonning "dalili" (<c>StudentFaceProfile.SampleUrl</c>) fayli o'chirilmaydi.</para>
    ///
    /// <para>⚠️ <b>O'CHIRISH NAVBATI — avval CHEGARAGA KIRMAYDIGAN urinishlar</b>
    /// (<see cref="CountsTowardLimit"/>), keyin qolganlari; har ikkalasida eng eskisidan boshlab.
    /// Bu ATAYIN shunday va u <b>xavfsizlik</b> qoidasi, tartib masalasi emas:</para>
    /// <para>Ilgari tozalash oddiy "eng eskisini o'chir" edi. Sifat/tiriklik sababli rad etilgan
    /// urinish (<c>Score = null</c>) esa soatlik chegarani <b>YEMAYDI</b>, lekin bazada JOY
    /// EGALLAYDI — ya'ni hujumchi ataylab yaroqsiz kadr yuborib, <c>KeepChecks</c> oynasidan
    /// SOLISHTIRILGAN urinishlarni surib chiqarardi va <see cref="MaxAttemptsPerHour"/> hisobi
    /// nolga qaytardi. Tiriklik o'chirilgan bo'lsa (nonce shart emas) bu <b>cheksiz</b> yuz
    /// solishtirish degani edi. Endi bunday qatorlar birinchi bo'lib o'chadi, ya'ni chegarani
    /// tashkil qiladigan qatorlar joyida qoladi.</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> CleanupAsync(
        string studentId, int keep, CancellationToken ct = default)
    {
        if (keep < 0) keep = 0;

        var all = await db.LoginFaceChecks
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
        if (all.Count <= keep) return Array.Empty<string>();

        // `pending` umuman qatnashmaydi (na chegarada, na o'chirishda).
        var candidates = all.Where(c => c.Status != StatusPending).ToList();
        var excess = candidates.Count - keep;
        if (excess <= 0) return Array.Empty<string>();

        var sampleUrl = await db.StudentFaceProfiles.AsNoTracking()
            .Where(p => p.StudentId == studentId).Select(p => p.SampleUrl).FirstOrDefaultAsync(ct);

        // `candidates` yangisidan eskisiga tartiblangan → indeks KATTA = ESKI.
        var doomed = candidates
            .Select((Check, Index) => (Check, Index))
            .OrderBy(x => CountsTowardLimit(x.Check) ? 1 : 0)   // avval chegaraga kirmaydiganlar
            .ThenByDescending(x => x.Index)                     // har guruhda eng eskisidan
            .Take(excess)
            .Select(x => x.Check)
            .ToList();

        var removed = new List<string>();
        foreach (var c in doomed)
        {
            db.LoginFaceChecks.Remove(c);
            if (!string.IsNullOrEmpty(c.ImageUrl) && c.ImageUrl != sampleUrl) removed.Add(c.ImageUrl);
        }
        return removed;
    }
}
