using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TIRIKLIK TEKSHIRUVI (liveness) — <b>sof funksiyalar</b> (bazaga ham, HTTP'ga ham tegmaydi;
/// testlangan: <c>FaceSecurityTests</c>).
///
/// <para><b>Muammo:</b> model TELEFONDA ishlaydi, ya'ni o'zgartirilgan APK serverga xohlagan
/// vektorni yubora oladi — hatto bosma rasm yoki ekrandagi suratdan olingan vektorni ham.
/// Server kadrni ko'rmaydi, demak "bu haqiqiy odammi" degan savolga O'ZI javob bera olmaydi.</para>
///
/// <para><b>Yechim (server tomonda mumkin bo'lgan yagona narsa):</b> server har urinishdan oldin
/// TASODIFIY harakatlar ketma-ketligini so'raydi (<see cref="Pick"/>) va bir martalik nonce
/// beradi. Ilova o'sha harakatlarni AYNAN shu tartibda o'lchaydi, HAR BIRI uchun o'lchangan
/// QIYMATNI qaytaradi; server esa javob so'ralganiga mos kelishini VA qiymat haqiqiy o'zgarishni
/// ko'rsatishini tekshiradi (<see cref="Check"/>).</para>
///
/// <para>⚠️ <b>HARAKATLAR RO'YXATI DETEKTORGA BOG'LIQ.</b> Ilovada YuNet (OpenCV Zoo) ishlaydi:
/// u yuz to'rtburchagi va <b>5 ta nuqta</b> beradi (ikki ko'z, burun, og'izning ikki cheti).
/// Shuning uchun ro'yxatda faqat SHU nuqtalar va yuz o'lchami bilan HALOL o'lchanadigan
/// harakatlar bor. <c>blink</c> (ko'z qisish) va <c>smile</c> (jilmayish) ATAYIN YO'Q — YuNet
/// ko'z ochiq/yumuqligini ham, jilmayishni ham aniqlay olmaydi, ya'ni ilova ularni tekshira
/// olmasdan "bajarildi" deb yozib yuborardi. Bu — <b>himoya bo'lmagan himoya</b>, eng yomon
/// variant: xavfsizlik borday ko'rinadi, aslida yo'q.</para>
///
/// <para>⚠️ <b>HALOL CHEKLOV — bu REPLAY'ga qarshi KAFOLAT EMAS.</b> Serverda kadrlar yo'q,
/// shuning uchun:</para>
/// <list type="bullet">
///   <item>bosma surat / ekrandagi statik rasm — <b>yopiladi</b> (rasm boshini burmaydi va
///     kameraga yaqinlashmaydi, ilova esa o'zgarishni o'lchay olmaydi);</item>
///   <item>oldindan yozib olingan VIDEO yoki o'zgartirilgan APK "harakat bajarildi, qiymat
///     shuncha" deb YOLG'ON yozishi — <b>yopilmaydi</b>. Buni faqat ilova butunligi
///     (attestation — <see cref="AppAttestation"/>) qisman to'sadi.</item>
/// </list>
/// <para>Ya'ni bu qatlam "arzon hujum"ni (do'stining suratini ko'rsatish) yopadi, "qimmat
/// hujum"ni (APK'ni teskari muhandislik qilish) esa faqat qiyinlashtiradi.</para>
/// </summary>
public static class FaceLiveness
{
    /* =============================================================================================
     *  HARAKATLAR KATALOGI — ilova AYNAN shu kalitlarni tushunishi shart
     * ========================================================================================== */

    /// <summary>Boshni CHAPGA burish — burun ko'zlar o'rtasidan siljiydi (yaw MANFIY).</summary>
    public const string ActionTurnLeft = "turn_left";
    /// <summary>Boshni O'NGGA burish (yaw MUSBAT).</summary>
    public const string ActionTurnRight = "turn_right";
    /// <summary>Telefonga YAQINLASHISH — yuz kadrda kattalashadi (faceRatio OSHADI).</summary>
    public const string ActionMoveCloser = "move_closer";
    /// <summary>ORQAGA surilish — yuz kichrayadi (faceRatio KAMAYADI).</summary>
    public const string ActionMoveBack = "move_back";

    /// <summary>Barcha mumkin bo'lgan harakatlar (server konstantasi — ilova <c>challenge</c>
    /// javobidan oladi, o'zida ro'yxat TUTMAYDI: aks holda ikki joyda ayri ketardi).</summary>
    public static readonly IReadOnlyList<string> All =
        [ActionTurnLeft, ActionTurnRight, ActionMoveCloser, ActionMoveBack];

    /// <summary>Har urinishda so'raladigan harakatlar soni.</summary>
    public const int ActionCount = 2;

    /// <summary>Bir harakat uchun eng qisqa vaqt (ms). Bundan tez "bajarildi" — SOXTA belgisi:
    /// odam boshini burishga ham, telefonni yaqinlashtirishga ham 300 ms dan kam vaqt sarflay
    /// olmaydi (30 fps da bu bor-yo'g'i 9 kadr).</summary>
    public const int MinActionMs = 300;

    /// <summary>Bir harakat uchun eng uzun vaqt (ms). Bundan uzoq — foydalanuvchi chalg'igan yoki
    /// kimdir "to'g'ri kadr"ni izlab o'tirgan; challenge muddati (90 s) baribir tugaydi.</summary>
    public const int MaxActionMs = 20_000;

    /* ---------- O'LCHANGAN QIYMAT chegaralari ----------
     *
     * ⚠️ ENG MUHIM QISM: `ok:true` ga ISHONMAYMIZ. Aynan shu maydonni o'zgartirilgan APK
     * hech narsa qilmasdan `true` qilib yuboradi. Shuning uchun ilova o'lchagan XOM qiymat
     * (`value`) ham talab qilinadi va u haqiqiy o'zgarishni ko'rsatishi kerak. */

    /// <summary>Burilish uchun eng kam burchak (gradus). 12° — odam sezilarli burgani, lekin
    /// kadrdan chiqib ketmagani (sifat chegarasi 25°).</summary>
    public const double MinTurnDegrees = 12;

    /// <summary>«Yaqinlashing» bajarildi deb hisoblanishi uchun yuz kamida shuncha baravar
    /// kattalashishi kerak.</summary>
    public const double CloserFactor = 1.25;

    /// <summary>«Orqaga suriling» bajarildi deb hisoblanishi uchun yuz shuncha baravargacha
    /// kichrayishi kerak.</summary>
    public const double BackFactor = 0.8;

    /// <summary>Nonce amal qilish muddati (soniya) — selfi olishga yetadi, o'g'irlangan nonce
    /// esa uzoq yashamaydi.</summary>
    public const int ChallengeTtlSeconds = 90;

    /// <summary>Foydalanuvchiga ko'rsatiladigan sabab (matnlar yagona joyda — <see cref="FaceMatch"/>).</summary>
    public const string Reason = FaceMatch.ReasonLiveness;

    /* =============================================================================================
     *  TANLASH
     * ========================================================================================== */

    /// <summary>
    /// Tasodifiy <see cref="ActionCount"/> ta harakat, TARTIBI ham tasodifiy, takrorlanmasdan.
    /// <paramref name="rnd"/> tashqaridan beriladi — funksiya sof bo'lib qolsin va test aniq
    /// natijani takrorlay olsin.
    /// </summary>
    public static IReadOnlyList<string> Pick(Random rnd, int count = ActionCount)
    {
        ArgumentNullException.ThrowIfNull(rnd);
        var pool = All.ToList();
        var n = Math.Clamp(count, 1, pool.Count);
        var picked = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            var idx = rnd.Next(pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);      // takrorlanmasin: "turn_left, turn_left" hech narsa isbotlamaydi
        }
        return picked;
    }

    /// <summary>Harakatlar ro'yxatini bazaga yoziladigan JSON'ga o'giradi.</summary>
    public static string Encode(IReadOnlyList<string> actions) => JsonSerializer.Serialize(actions);

    /// <summary>Bazadagi JSON'dan harakatlarni tiklaydi. Buzuq JSON — bo'sh ro'yxat (istisno EMAS).</summary>
    public static IReadOnlyList<string> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list is null ? [] : list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
        catch (JsonException) { return []; }
    }

    /* =============================================================================================
     *  TEKSHIRISH
     * ========================================================================================== */

    /// <summary>Klient yuborgan bitta harakat natijasi.</summary>
    /// <param name="Value">Ilova O'LCHAGAN xom qiymat: burilishlarda YAW (gradus, ±),
    /// masofa harakatlarida <c>faceRatio</c> (0..1). <c>NaN</c> — klient yubormagan.</param>
    public readonly record struct Step(string Action, bool Ok, int Ms, double Value);

    /// <summary>
    /// Klient JSON'ini o'qiydi:
    /// <c>[{"action":"turn_left","ok":true,"ms":1400,"value":-27.5}, ...]</c>.
    /// Buzuq JSON — bo'sh ro'yxat (istisno EMAS: klient ma'lumoti ISHONCHSIZ, u serverga 500
    /// yozdira olmasligi kerak). Bo'sh ro'yxat esa <see cref="Check"/> da baribir RAD etiladi.
    /// </summary>
    public static IReadOnlyList<Step> ParseSteps(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var steps = new List<Step>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) return [];
                var action = el.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
                    ? (a.GetString() ?? "") : "";
                var ok = el.TryGetProperty("ok", out var o) && o.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.Number => o.TryGetInt32(out var n) && n != 0,
                    _ => false,
                };
                steps.Add(new Step(
                    action.Trim().ToLowerInvariant(), ok,
                    (int)Math.Clamp(Num(el, "ms", -1), int.MinValue, int.MaxValue),
                    Num(el, "value", double.NaN)));
            }
            return steps;
        }
        catch (JsonException) { return []; }
    }

    private static double Num(JsonElement el, string name, double fallback) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d)
            ? d : fallback;

    /// <summary>
    /// Klient javobi server so'ragan harakatlarga mos keladimi.
    /// <c>null</c> — o'tdi, aks holda foydalanuvchiga ko'rsatiladigan sabab.
    ///
    /// <para>Tekshiriladi:</para>
    /// <list type="number">
    ///   <item><b>soni</b> va <b>harakatlar TARTIBI bilan aynan mos</b> — tartib ATAYIN muhim,
    ///     aks holda ilova barcha harakatlarni oldindan yozib qo'yib, so'ralganini "tanlab"
    ///     yuboraverardi;</item>
    ///   <item>har biri <c>ok:true</c> va vaqti <see cref="MinActionMs"/>..<see cref="MaxActionMs"/>;</item>
    ///   <item><b>o'lchangan QIYMAT haqiqiy o'zgarishni ko'rsatadi</b> — burilishda burchak
    ///     <see cref="MinTurnDegrees"/> dan katta (to'g'ri ISHORA bilan), masofada esa
    ///     boshlang'ich <paramref name="baselineFaceRatio"/> ga nisbatan
    ///     <see cref="CloserFactor"/> / <see cref="BackFactor"/> chegarasidan o'tgan.</item>
    /// </list>
    /// </summary>
    /// <param name="baselineFaceRatio">Yakuniy selfi sifatidagi <c>faceRatio</c> — "boshlang'ich
    /// masofa" sifatida ishlatiladi (<c>quality.faceRatio</c>).</param>
    public static string? Check(
        IReadOnlyList<string> expected, IReadOnlyList<Step> got, double baselineFaceRatio)
    {
        if (expected is null || expected.Count == 0) return Reason;
        if (got is null || got.Count != expected.Count) return Reason;

        // Boshlang'ich masofa ma'nosiz bo'lsa masofa harakatlarini tekshirib bo'lmaydi.
        var baseline = double.IsNaN(baselineFaceRatio) || double.IsInfinity(baselineFaceRatio)
            ? -1 : baselineFaceRatio;

        for (var i = 0; i < expected.Count; i++)
        {
            var want = (expected[i] ?? "").Trim().ToLowerInvariant();
            var step = got[i];
            if (!string.Equals(want, step.Action, StringComparison.Ordinal)) return Reason;
            if (!step.Ok) return Reason;
            if (step.Ms < MinActionMs || step.Ms > MaxActionMs) return Reason;
            if (double.IsNaN(step.Value)) return Reason;      // qiymat umuman yuborilmagan

            var moved = want switch
            {
                ActionTurnLeft => step.Value <= -MinTurnDegrees,
                ActionTurnRight => step.Value >= MinTurnDegrees,
                ActionMoveCloser => baseline > 0 && step.Value >= baseline * CloserFactor,
                ActionMoveBack => baseline > 0 && step.Value <= baseline * BackFactor,
                // Noma'lum harakat — biz so'ramagan bo'lishimiz kerak edi; baribir rad etamiz.
                _ => false,
            };
            if (!moved) return Reason;
        }
        return null;
    }

    /// <summary>Qulaylik uchun: bazadagi JSON + klient JSON'i bo'yicha tekshiruv.</summary>
    public static string? Check(string? expectedJson, string? clientJson, double baselineFaceRatio) =>
        Check(Decode(expectedJson), ParseSteps(clientJson), baselineFaceRatio);
}
