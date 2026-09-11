using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YUZ VEKTORLARI USTIDAGI MATEMATIKA — <b>sof funksiyalar</b> (bazaga ham, HTTP'ga ham tegmaydi;
/// testlangan: <c>FaceLoginTests</c>).
///
/// <para>⚠️ <b>Model bu yerda ISHLAMAYDI.</b> Yuz modeli TELEFONDA ishlaydi (server 1 GB RAM —
/// `FACE-DETEKT-PLAN.md` §2/§6), serverga faqat tayyor vektor keladi. Shuning uchun bu yerda
/// ML kutubxonasi yo'q: kodlash/dekodlash, L2-normallashtirish va kosinus — hammasi qo'lda.</para>
///
/// <para><b>Klient yuborgan ma'lumot ISHONCHSIZ deb qaraladi:</b> buzuq base64, NaN/Infinity,
/// nol vektor, o'lchamlar mos kelmasligi — hammasi ISTISNO emas, tushunarli XATO MATNI bilan
/// qaytadi (controller 400 qiladi). Aks holda soxta klient serverga 500 yozdirib turardi.</para>
/// </summary>
public static class FaceMatch
{
    /* =============================================================================================
     *  Chegaralar
     * ========================================================================================== */

    /// <summary>Qabul qilinadigan eng kichik/katta vektor o'lchami. 512 (ArcFace) va 128
    /// (FaceNet) — amaldagi ikki variant; chegaralar shularni qamrab, absurd qiymatlarni
    /// (1 yoki 1 000 000) kesib tashlaydi.</summary>
    public const int MinDim = 32;
    public const int MaxDim = 2048;

    /* =============================================================================================
     *  RAD SABABLARI — o'zbekcha matnlar YAGONA joyda (backend, ilova va admin ro'yxati bir xil
     *  matnni ko'rsatishi uchun; komponentlarda xom satr yozilmasin).
     * ========================================================================================== */

    public const string ReasonBlurry = "Rasm xira — qimirlatmasdan qayta oling";
    public const string ReasonDark = "Yorug'roq joyda oling";
    public const string ReasonBright = "Juda yorqin — yorug'lik to'g'ridan tushmasin";
    public const string ReasonNoFace = "Yuz topilmadi";
    public const string ReasonManyFaces = "Kadrda bir nechta odam";
    public const string ReasonSmallFace = "Telefonni yaqinroq tuting";
    public const string ReasonAngle = "Yuzni kameraga to'g'ri qarating";
    public const string ReasonNoMatch = "Yuz mos kelmadi";
    /// <summary>Ilovadagi model serverdagi <c>LoginFaceModelVersion</c> bilan mos emas — turli
    /// modellarning vektorlarini solishtirish MA'NOSIZ (tasodifiy natija berardi).</summary>
    public const string ReasonOldApp = "Ilovani yangilang";
    /// <summary>Na etalon, na profil rasmi bor — avtomatik qaror qabul qilib bo'lmaydi.</summary>
    public const string ReasonPending = "Rasmingiz tekshiruvga yuborildi — administrator tasdiqlagach kirasiz";
    public const string ReasonTooManyAttempts = "Urinishlar soni oshdi — bir soatdan keyin qayta urinib ko'ring";

    /// <summary>Tiriklik (liveness) tekshiruvi: server so'ragan harakatlar bajarilmadi yoki
    /// javob mos kelmadi (<see cref="FaceLiveness"/>).</summary>
    public const string ReasonLiveness = "Tiriklik tekshiruvidan o'tmadi";
    /// <summary>Bir martalik nonce yo'q/eskirgan/ishlatilgan — ilova avval
    /// <c>POST /api/student/face/challenge</c> ni chaqirishi kerak.</summary>
    public const string ReasonNoChallenge = "Tekshiruv muddati tugadi — qaytadan urinib ko'ring";
    /// <summary>Play Integrity ilovani/qurilmani RAD ETDI — o'zgartirilgan APK belgisi.</summary>
    public const string ReasonAttestation = "Ilova haqiqiyligi tasdiqlanmadi — ilovani Play Market'dan o'rnating";
    /// <summary>Attestation MAJBURIY, lekin token umuman kelmagan/sozlanmagan.</summary>
    public const string ReasonAttestationMissing = "Ilova haqiqiyligini tekshirib bo'lmadi — ilovani yangilang";
    /// <summary>Attestation MAJBURIY, lekin tashqi xizmatga yetib borilmadi (fail-closed).</summary>
    public const string ReasonAttestationUnavailable = "Tekshiruv xizmati vaqtincha ishlamayapti — birozdan keyin urinib ko'ring";

    /* =============================================================================================
     *  KODLASH / DEKODLASH (float32 little-endian)
     * ========================================================================================== */

    /// <summary>Vektorni baytlarga o'giradi (float32 LE) — bazada `byte[]` sifatida saqlanadi.</summary>
    public static byte[] Encode(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        var bytes = new byte[vector.Length * 4];
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), vector[i]);
        return bytes;
    }

    /// <summary>Baytlardan vektorni tiklaydi (float32 LE). Uzunlik 4 ga bo'linmasa — bo'sh massiv
    /// (chaqiruvchi <see cref="Validate"/> orqali tekshiradi; bu yerda istisno tashlanmaydi).</summary>
    public static float[] Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length % 4 != 0) return Array.Empty<float>();
        var v = new float[bytes.Length / 4];
        for (var i = 0; i < v.Length; i++)
            v[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return v;
    }

    /// <summary>
    /// Klientdan kelgan base64 (float32 LE) ni vektorga o'giradi va TEKSHIRADI.
    /// Muvaffaqiyatda <c>error == null</c>; aks holda vektor <c>null</c> va sabab matni beriladi.
    /// </summary>
    public static float[]? TryParse(string? base64, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(base64)) { error = "Vektor berilmagan"; return null; }

        byte[] raw;
        try { raw = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { error = "Vektor formati noto'g'ri (base64 emas)"; return null; }

        var v = Decode(raw);
        error = Validate(v);
        return error is null ? v : null;
    }

    /// <summary>
    /// Vektor yaroqlimi. <c>null</c> — yaroqli, aks holda sabab matni.
    /// Tekshiriladi: o'lcham chegarasi, NaN/Infinity va NOL vektor (uzunligi 0 — kosinus 0/0
    /// bo'lib ketardi va har qanday yuzga "mos" chiqishi mumkin edi).
    /// </summary>
    public static string? Validate(float[]? v)
    {
        if (v is null || v.Length == 0) return "Vektor bo'sh";
        if (v.Length < MinDim || v.Length > MaxDim) return $"Vektor o'lchami noto'g'ri ({v.Length})";
        double sum = 0;
        foreach (var x in v)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) return "Vektorda yaroqsiz son bor";
            sum += (double)x * x;
        }
        if (sum <= 1e-12) return "Vektor bo'sh (barcha qiymatlar nol)";
        return null;
    }

    /// <summary>L2-normallashtirish (uzunligi 1 bo'ladi). Klient normallashtirmagan bo'lsa ham
    /// kosinus to'g'ri chiqsin uchun HAR DOIM qo'llanadi. Nol vektor o'zgarishsiz qaytadi.</summary>
    public static float[] Normalize(float[] v)
    {
        ArgumentNullException.ThrowIfNull(v);
        double sum = 0;
        foreach (var x in v) sum += (double)x * x;
        if (sum <= 1e-12) return v;
        var norm = Math.Sqrt(sum);
        var r = new float[v.Length];
        for (var i = 0; i < v.Length; i++) r[i] = (float)(v[i] / norm);
        return r;
    }

    /// <summary>
    /// Kosinus o'xshashligi (-1..1; yuz vektorlarida amalda 0..1).
    /// Uzunliklar mos kelmasa — <see cref="ArgumentException"/> (bu KOD xatosi: turli modellarning
    /// vektorlari solishtirilmoqda; klient xatosi emas — u yuqorida `modelVersion` bilan ushlanadi).
    /// </summary>
    public static double Cosine(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length)
            throw new ArgumentException($"Vektor uzunliklari mos emas: {a.Length} va {b.Length}");
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na <= 1e-12 || nb <= 1e-12) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /* =============================================================================================
     *  SIFAT (klient hisoblab yuboradi — server FAQAT chegaralarni qo'llaydi)
     * ========================================================================================== */

    /// <summary>
    /// Klient o'lchagan kadr sifati — <b>XOM (normallashtirilmagan) birliklarda</b>.
    ///
    /// <para>⚠️ Ilgari bu yerda 0..1 ga normallashtirilgan qiymatlar kutilardi, ilova esa xom
    /// qiymat yuborardi (masalan yorug'lik 128, chegara esa 0.92) — ya'ni server chegaralari
    /// AMALDA hech qachon ishlamasdi. Birliklar ILOVA tomoniga moslashtirildi (u haqiqiy
    /// fotolarda kalibrlangan), teskarisiga emas.</para>
    /// </summary>
    /// <param name="Faces">Kadrda topilgan yuzlar soni.</param>
    /// <param name="Sharpness">Aniqlik — <b>Laplas dispersiyasi</b> (xom son; yaxshi fotoda ~500).</param>
    /// <param name="Brightness">O'rtacha yorug'lik — <b>0..255</b> (normal ~128).</param>
    /// <param name="FaceRatio">Yuz kadrning qancha qismini egallagan (0..1).</param>
    /// <param name="Yaw">Chapga/o'ngga burilish (GRADUS, ±).</param>
    /// <param name="Roll">Yon egilish (GRADUS, ±).</param>
    public readonly record struct FaceQuality(
        int Faces, double Sharpness, double Brightness, double FaceRatio,
        double Yaw, double Roll);

    /// <summary>Qabul chegaralari. Ilova AYNAN shu qiymatlarni `GET /face/status` dan oladi —
    /// chegaralar ikki joyda ayri ketmasin (telefonda "yaxshi", serverda "yomon" bo'lib qolmasin).</summary>
    public readonly record struct QualityLimits(
        double MinSharpness, double MinBrightness, double MaxBrightness,
        double MinFaceRatio, double MaxYaw, double MaxRoll);

    /// <summary>
    /// Standart chegaralar — ilovadagi detektor (YuNet) o'lchoviga qarab HAQIQIY fotolarda
    /// kalibrlangan. Juda qattiq emas: rad etilgan har bir kadr foydalanuvchi uchun "kira
    /// olmayapman" degani.
    /// </summary>
    public static readonly QualityLimits DefaultLimits = new(
        MinSharpness: 40,      // Laplas dispersiyasi (yaxshi foto ~500, qorong'i/xira ~40 gacha tushadi)
        MinBrightness: 55,     // 0..255
        MaxBrightness: 215,    // 0..255
        MinFaceRatio: 0.15,
        MaxYaw: 25,            // gradus
        MaxRoll: 20);          // gradus

    /// <summary>
    /// Kadr qabul qilinadimi. <c>null</c> — qabul qilinadi, aks holda foydalanuvchiga
    /// ko'rsatiladigan sabab.
    ///
    /// <para>⚠️ <b>Tekshiruv TARTIBI o'lchovga asoslangan.</b> Avval "yuz bormi" (eng tushunarli
    /// xato), keyin <b>YORUG'LIK</b>, faqat undan keyin TINIQLIK, oxirida o'lcham/burchak.</para>
    ///
    /// <para>Yorug'lik tiniqlikdan OLDIN turishi shart: bitta va o'sha rasm qorong'ilashtirilganda
    /// Laplas dispersiyasi <b>533 → 44</b> ga tushadi. Ya'ni qorong'i kadr "xira" bo'lib
    /// ko'rinadi va foydalanuvchiga «Rasm xira — qimirlatmasdan qayta oling» degan NOTO'G'RI
    /// maslahat berilardi, holbuki qilish kerak bo'lgan ish — yorug'roq joyga o'tish.</para>
    /// </summary>
    public static string? Reject(FaceQuality q, QualityLimits limits)
    {
        if (q.Faces <= 0) return ReasonNoFace;
        if (q.Faces > 1) return ReasonManyFaces;
        if (q.Brightness < limits.MinBrightness) return ReasonDark;
        if (q.Brightness > limits.MaxBrightness) return ReasonBright;
        if (q.Sharpness < limits.MinSharpness) return ReasonBlurry;
        if (q.FaceRatio < limits.MinFaceRatio) return ReasonSmallFace;
        if (Math.Abs(q.Yaw) > limits.MaxYaw || Math.Abs(q.Roll) > limits.MaxRoll) return ReasonAngle;
        return null;
    }

    /// <summary>Qulaylik uchun: standart chegaralar bilan.</summary>
    public static bool IsAcceptable(FaceQuality q, QualityLimits limits) => Reject(q, limits) is null;

    /// <summary>
    /// Klient yuborgan sifat JSON'ini o'qiydi. Buzuq/bo'sh JSON — ISTISNO EMAS: barcha
    /// ko'rsatkichlar "yaxshi" deb qabul qilinadi (<see cref="Unknown"/>), chunki sifat
    /// tekshiruvi QULAYLIK uchun, xavfsizlik uchun emas — asosiy qaror baribir vektor
    /// solishtiruvida chiqadi.
    /// </summary>
    public static FaceQuality ParseQuality(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Unknown;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unknown;
            return new FaceQuality(
                Faces: (int)Num(root, "faces", Unknown.Faces),
                Sharpness: Num(root, "sharpness", Unknown.Sharpness),
                Brightness: Num(root, "brightness", Unknown.Brightness),
                FaceRatio: Num(root, "faceRatio", Unknown.FaceRatio),
                Yaw: Num(root, "yaw", 0),
                Roll: Num(root, "roll", 0));
        }
        catch (JsonException) { return Unknown; }
    }

    /// <summary>
    /// Sifat noma'lum (klient yubormagan/buzuq) — barcha chegaralardan o'tadigan, ammo
    /// <b>HAQIQATGA YAQIN</b> qiymatlar.
    ///
    /// <para>⚠️ <c>FaceRatio</c> ATAYIN 1 EMAS, 0.3: bu qiymat tiriklik tekshiruvida
    /// "boshlang'ich masofa" sifatida ham ishlatiladi (<c>FaceLiveness</c>). 1 bo'lsa
    /// «yaqinlashing» topshirig'ini bajarish MATEMATIK jihatdan imkonsiz bo'lardi
    /// (faceRatio 1 dan oshmaydi), ya'ni sifat yubormagan klient jimgina qulflanib qolardi.</para>
    ///
    /// <para>⚠️ <c>eyesOpen</c> UMUMAN YO'Q: ilovadagi detektor (YuNet) 5 ta nuqta beradi
    /// (ikki ko'z, burun, og'iz cheti) va ko'z ochiq/yumuqligini o'lchay OLMAYDI. Uni talab
    /// qilish "himoya bo'lmagan himoya" bo'lardi — klient har doim <c>true</c> yozib yuborardi.</para>
    /// </summary>
    public static readonly FaceQuality Unknown = new(
        Faces: 1, Sharpness: 1000, Brightness: 128, FaceRatio: 0.3, Yaw: 0, Roll: 0);

    private static double Num(JsonElement root, string name, double fallback)
    {
        if (!root.TryGetProperty(name, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d)
                ? d : fallback,
            JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var s) ? s : fallback,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => fallback,
        };
    }

    /* =============================================================================================
     *  QAROR — "kim kirdi" mantig'ining YADROSI (sof funksiya, bazaga bog'liq emas)
     * ========================================================================================== */

    /// <summary>Qaror natijasi.</summary>
    /// <param name="Ok">Kirishga ruxsat berildimi.</param>
    /// <param name="Status">approved | rejected | pending.</param>
    /// <param name="Reason">O'zbekcha sabab (muvaffaqiyatda bo'sh).</param>
    /// <param name="Score">Kosinus (solishtirish bo'lmagan holatda null).</param>
    /// <param name="Enroll">Shu selfi ETALON qilib saqlansinmi (birinchi marta ro'yxatdan o'tish).</param>
    public readonly record struct MatchOutcome(
        bool Ok, string Status, string Reason, double? Score, bool Enroll);

    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusPending = "pending";

    /// <summary>
    /// SELFINI kim bilan solishtirish kerakligini hal qiladi va natijani qaytaradi.
    ///
    /// <list type="number">
    ///   <item><b>Etalon bor</b> — selfi etalon bilan solishtiriladi. Bu odatiy yo'l.</item>
    ///   <item><b>Etalon yo'q, lekin profil rasmi vektori (<paramref name="refVector"/>) bor</b> —
    ///     selfi PROFIL RASMI bilan solishtiriladi va mos kelsa ETALON qilib saqlanadi.
    ///     ⚠️ Bu modulning ASOSIY xavfsizlik nuqtasi: parolni o'g'irlagan begona odam O'Z yuzini
    ///     etalon qilib qo'ya olmaydi — uning yuzi markazdagi profil rasmiga mos kelmaydi.</item>
    ///   <item><b>Ikkalasi ham yo'q</b> (o'quvchining rasmi umuman yuklanmagan) — avtomatik qaror
    ///     qabul qilinmaydi: <c>pending</c>, admin ko'rib tasdiqlaydi.</item>
    /// </list>
    /// </summary>
    public static MatchOutcome Evaluate(
        float[] selfie, float[]? enrolled, float[]? refVector, double threshold)
    {
        ArgumentNullException.ThrowIfNull(selfie);

        var target = enrolled is { Length: > 0 } ? enrolled : null;
        var enroll = false;
        if (target is null && refVector is { Length: > 0 })
        {
            target = refVector;
            enroll = true;
        }

        // Solishtiradigan hech narsa yo'q — begona odamni kiritib yuborishdan ko'ra admin ko'rib
        // chiqqani xavfsizroq.
        if (target is null)
            return new MatchOutcome(false, StatusPending, ReasonPending, null, false);

        // Turli o'lchamdagi vektorlar — solishtirib bo'lmaydi (odatda ilova versiyasi eskirgan).
        if (target.Length != selfie.Length)
            return new MatchOutcome(false, StatusRejected, ReasonOldApp, null, false);

        var score = Cosine(Normalize(selfie), Normalize(target));
        return score >= threshold
            ? new MatchOutcome(true, StatusApproved, "", score, enroll)
            : new MatchOutcome(false, StatusRejected, ReasonNoMatch, score, false);
    }
}
