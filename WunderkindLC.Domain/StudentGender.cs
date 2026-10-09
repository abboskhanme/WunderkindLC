namespace WunderkindLC.Domain;

/// <summary>
/// O'quvchining JINSI — IXTIYORIY (2026-10-09, foydalanuvchi qarori: "jins kiritish majburiy
/// bo'lmasin"). Saqlanadigan qiymatlar: <see cref="Male"/>, <see cref="Female"/> yoki <c>""</c>
/// (ko'rsatilmagan).
///
/// <para>⚠️ Ilgari bo'sh qiymat jimgina "male" ga aylanardi (forma ham, Excel importi ham, liddan
/// o'quvchi yaratish ham) — ya'ni jinsi kiritilmagan har bir qiz "o'g'il" bo'lib yozilardi. Endi
/// noma'lum qiymat <c>""</c> bo'lib qoladi va ro'yxat/eksportda bo'sh ko'rinadi.</para>
///
/// <para>Mavjud qatorlar TEGILMAGAN (eski "male" qiymatlari o'z joyida).</para>
/// </summary>
public static class StudentGender
{
    public const string Male = "male";
    public const string Female = "female";

    private const string Apostrophes = "'ʻʼ’‘`´";

    /// <summary>
    /// Formadan yoki Excel'dan kelgan qiymat → <c>"male"</c> | <c>"female"</c> | <c>""</c>.
    /// Qabul qilinadi: male/erkak/o'g'il/bola/m/1 va female/ayol/qiz/f/q/2 (registr va apostrof
    /// turlaridan qat'i nazar). Bo'sh yoki tanilmagan — <c>""</c> (taxmin qilinmaydi).
    /// </summary>
    public static string Normalize(string? raw)
    {
        // Apostrof va bo'shliqlarni olib tashlaymiz: "O'g'il", "oʻgʻil", "o`g`il bola" → "ogil"/"ogilbola".
        // ⚠️ `char.IsLetterOrDigit` yetmaydi: «ʻ» (U+02BB) Unicode'da HARF (Lm) hisoblanadi.
        var v = new string((raw ?? "").ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) && !Apostrophes.Contains(c)).ToArray());
        return v switch
        {
            "male" or "erkak" or "ogil" or "ogilbola" or "bola" or "m" or "e" or "1"
                or "ўғил" or "угил" or "эркак" => Male,
            "female" or "ayol" or "qiz" or "qizbola" or "f" or "q" or "2"
                or "қиз" or "киз" or "аёл" => Female,
            _ => "",
        };
    }

    /// <summary>Eksport uchun yorliq: "O'g'il" / "Qiz" / <c>""</c> (ko'rsatilmagan).</summary>
    public static string ExportLabel(string? gender) => gender switch
    {
        Male => "O'g'il",
        Female => "Qiz",
        _ => "",
    };
}
