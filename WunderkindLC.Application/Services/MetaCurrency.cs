using System.Globalization;
using System.Text;

namespace WunderkindLC.Application.Services;

/// <summary>
/// META PUL BIRLIGI — "minor unit" (tiyin/sent) va "major unit" (so'm/dollar) o'rtasidagi
/// YAGONA ko'prik. Sof funksiyalar: HTTP ham, DB ham yo'q (<c>MetaInsightsParserTests</c>
/// bilan qoplangan).
///
/// <para><b>⚠️ META'DA ASSIMETRIYA BOR — eng ko'p uchraydigan xato shu yerda:</b></para>
/// <list type="table">
///   <item><term>Byudjet (<c>daily_budget</c>, <c>lifetime_budget</c>)</term>
///         <description>butun son, <b>MINOR</b> unit: <c>5000</c> = 50.00 USD</description></item>
///   <item><term>Insights <c>spend</c></term>
///         <description><b>MATN</b>, <b>MAJOR</b> unit: <c>"312.45"</c> = 312.45 USD</description></item>
/// </list>
/// Bazada hamma narsa MINOR bo'lib saqlanadi (<c>long</c>) — kasrli <c>decimal</c> ustunlar
/// yig'indida yaxlitlash xatosi to'plardi va valyuta almashsa qayta hisoblab bo'lmasdi.
///
/// <para><b>🔴 <c>currency_offset</c> MAYDONI META'DA YO'Q.</b> Ad Account tugunida bunday
/// maydon umuman qaytmaydi (u eskirgan <c>Currency</c> tugunida edi) va uni so'rasangiz Graph
/// BUTUN so'rovni <c>code 100</c> bilan rad etadi — ya'ni statistika UMUMAN kelmay qo'yadi.
/// Shuning uchun offset BIZNING tomonda: <c>GET /act_{id}?fields=currency</c> faqat ISO kodini
/// beradi, qolganini shu sinf hal qiladi.</para>
/// </summary>
public static class MetaCurrency
{
    /// <summary>Noma'lum valyuta uchun XAVFSIZ default. Dunyodagi valyutalarning aksariyati
    /// ikki xonali kasrga ega, ya'ni yangi kod chiqib qolsa ham xato "100 barobar" emas,
    /// "0 barobar" bo'lmaydi.</summary>
    public const int DefaultOffset = 2;

    /// <summary>Ruxsat etilgan eng katta offset — <see cref="Factor"/> jadvalining chegarasi.
    /// Bundan kattasi <c>long</c> ni tez to'ldirib qo'yardi (1 mlrd so'm × 10^9 = overflow).</summary>
    public const int MaxOffset = 6;

    /// <summary>
    /// Meta'ning "zero-decimal" valyutalari — minor unit = major unit (kasr qismi YO'Q).
    /// Bu ro'yxatdagi valyutada <c>spend "312"</c> → <c>312</c> minor, <c>31200</c> EMAS.
    ///
    /// <para>⚠️ UZS bu ro'yxatda YO'Q: so'mning rasmiy kasr birligi (tiyin) bor va Meta uni
    /// ikki xonali deb hisoblaydi, ya'ni UZS uchun offset <b>2</b>.</para>
    /// </summary>
    private static readonly HashSet<string> Zero = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "ISK", "PYG", "UGX", "RWF", "VUV",
        "XAF", "XOF", "XPF", "KMF", "DJF", "GNF", "BIF", "MGA",
    };

    /// <summary>10^offset — <c>Math.Pow</c> (double) o'rniga JADVAL: <c>double</c> aylanishi
    /// katta summalarda bir tiyinlik farq berardi va u hisobotda "1 so'm yetishmayapti" bo'lib
    /// chiqardi.</summary>
    private static readonly decimal[] Factors = { 1m, 10m, 100m, 1000m, 10_000m, 100_000m, 1_000_000m };

    /// <summary>Valyuta ISO kodidan kasr xonalari soni. Bo'sh yoki noma'lum kod →
    /// <see cref="DefaultOffset"/>.</summary>
    public static int OffsetOf(string? code) =>
        string.IsNullOrWhiteSpace(code) ? DefaultOffset
        : Zero.Contains(code.Trim()) ? 0
        : DefaultOffset;

    /// <summary>Offsetni ruxsat etilgan oraliqqa qisadi (bazadagi buzuq qiymat hisobni
    /// buzmasin).</summary>
    public static int Clamp(int offset) => offset < 0 ? 0 : offset > MaxOffset ? MaxOffset : offset;

    private static decimal Factor(int offset) => Factors[Clamp(offset)];

    /// <summary>
    /// Insights <c>spend</c> (MATN, MAJOR) → MINOR unit.
    /// <c>"312.45"</c> + offset 2 → <c>31245</c>.
    ///
    /// <para>⚠️ Har doim <see cref="CultureInfo.InvariantCulture"/>: server madaniyati
    /// <c>ru-RU</c> bo'lsa <c>"312.45"</c> nuqtasi guruh ajratgichi deb o'qilib, natija
    /// <b>31245.00</b> emas, <b>3124500</b> chiqardi.</para>
    ///
    /// <para>Buzuq/bo'sh qiymat → <c>0</c>, istisno OTILMAYDI: bitta xato qator butun kunlik
    /// sinxronizatsiyani yiqitmasligi kerak.</para>
    /// </summary>
    public static long ParseSpendToMinor(string? spend, int offset)
    {
        if (string.IsNullOrWhiteSpace(spend)) return 0;

        var normalized = NormalizeNumeric(spend);
        if (normalized.Length == 0) return 0;

        if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var major))
            return 0;

        return ToMinor(major, offset);
    }

    /// <summary>MAJOR (<c>decimal</c>) → MINOR. Yarmi yuqoriga (<see cref="MidpointRounding.AwayFromZero"/>)
    /// — bank yaxlitlashi ("juftga") kassaga tushunarsiz ko'rinardi.</summary>
    public static long ToMinor(decimal major, int offset)
    {
        try
        {
            var minor = Math.Round(major * Factor(offset), MidpointRounding.AwayFromZero);
            if (minor > long.MaxValue) return long.MaxValue;
            if (minor < long.MinValue) return long.MinValue;
            return (long)minor;
        }
        catch (OverflowException)
        {
            // Aql bovar qilmaydigan qiymat (masalan Meta'dan buzuq matn) — 0 bo'lgani yaxshi,
            // chunki u hisobotda darhol ko'zga tashlanadi, "long.MaxValue" esa grafikni buzardi.
            return 0;
        }
    }

    /// <summary>MINOR → MAJOR (<c>decimal</c>) — faqat hisob-kitob uchun. Ekranga
    /// <see cref="FormatMinor"/> chiqariladi.</summary>
    public static decimal ToMajor(long minor, int offset) => minor / Factor(offset);

    /// <summary>
    /// MINOR → Meta yuboradigan MATN ko'rinishi (<c>31245</c> + offset 2 → <c>"312.45"</c>).
    /// <see cref="ParseSpendToMinor"/> ning teskarisi: test round-trip shu ikkisi bilan
    /// tekshiriladi. Ajratgich HAR DOIM nuqta (invariant) — bu MASHINA formati.
    /// </summary>
    public static string ToMajorString(long minor, int offset)
    {
        var o = Clamp(offset);
        return ToMajor(minor, o).ToString("F" + o.ToString(CultureInfo.InvariantCulture),
                                          CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// MINOR → ODAM o'qiydigan matn: <c>120000000</c> + offset 2 + "UZS" → <c>"1 200 000 UZS"</c>.
    ///
    /// <para>⚠️ Kasr qismi NOL bo'lsa umuman chizilmaydi. Sabab: so'mda tiyin ishlatilmaydi va
    /// "1 200 000.00 so'm" hisobotni shovqin bilan to'ldirardi; kasr HAQIQATAN bo'lganda
    /// (dollar sarfi "312.45") u baribir ko'rinadi.</para>
    ///
    /// <para>Guruh ajratgichi — oddiy probel (madaniyatga bog'liq emas): server
    /// <c>en-US</c> da ishlaganda vergul chiqarib, o'zbekcha interfeysga begona ko'rinardi.</para>
    /// </summary>
    public static string FormatMinor(long minor, int offset, string? currency = null)
    {
        var o = Clamp(offset);
        var factor = (long)Factor(o);

        var negative = minor < 0;
        // ⚠️ long.MinValue uchun Math.Abs istisno otadi — shuning uchun qo'lda.
        var abs = negative ? (minor == long.MinValue ? long.MaxValue : -minor) : minor;

        var whole = abs / factor;
        var frac = abs % factor;

        var sb = new StringBuilder();
        if (negative) sb.Append('-');
        sb.Append(whole.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " "));
        if (frac != 0 && o > 0)
            sb.Append('.').Append(frac.ToString(new string('0', o), CultureInfo.InvariantCulture));

        var code = (currency ?? "").Trim();
        if (code.Length > 0) sb.Append(' ').Append(code.ToUpperInvariant());

        return sb.ToString();
    }

    /// <summary>
    /// Raqamli matnni invariant ko'rinishga keltiradi.
    ///
    /// <para>Meta HAR DOIM invariant format yuboradi (<c>"312.45"</c>), lekin bu funksiya qo'lda
    /// kiritilgan/ko'chirilgan qiymatga ham chidashi kerak, chunki bitta qatorning 100 barobar
    /// xato o'qilishi butun hisobotni buzadi:</para>
    /// <list type="bullet">
    ///   <item>probel va uzilmas probel (NBSP) — guruh ajratgichi sifatida — OLIB TASHLANADI;</item>
    ///   <item>vergul — agar nuqta YO'Q bo'lsa, BITTA vergul bo'lsa va undan keyin 1–2 raqam
    ///         qolsa — KASR ajratgichi ("312,45"), aks holda guruh ajratgichi ("1,234").</item>
    /// </list>
    /// </summary>
    internal static string NormalizeNumeric(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch) || ch == '\'' || ch == '’') continue;   // guruh ajratgichlari
            sb.Append(ch);
        }

        var v = sb.ToString();
        if (v.Length == 0) return "";

        if (!v.Contains('.'))
        {
            var last = v.LastIndexOf(',');
            var tail = v.Length - last - 1;
            if (last >= 0 && v.IndexOf(',') == last && (tail == 1 || tail == 2))
                v = v.Remove(last, 1).Insert(last, ".");
        }

        return v.Replace(",", "");
    }
}
