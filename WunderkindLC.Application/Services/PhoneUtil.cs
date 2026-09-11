namespace WunderkindLC.Application.Services;

/// <summary>Telefon raqamlarini solishtirish uchun yordamchi (turli format: +998, bo'sh joy, qavs).</summary>
public static class PhoneUtil
{
    /// <summary>Faqat raqamlarni qoldiradi.</summary>
    public static string DigitsOnly(string? s) =>
        new(string.IsNullOrEmpty(s) ? Array.Empty<char>() : s.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Solishtirish kaliti — oxirgi 9 raqam (mamlakat kodisiz mahalliy raqam).
    /// "+998 90 123 45 67", "998901234567", "901234567" → barchasi "901234567".
    /// </summary>
    public static string Key(string? s)
    {
        var d = DigitsOnly(s);
        return d.Length <= 9 ? d : d[^9..];
    }

    /// <summary>
    /// Telefon raqamni standart formatga +998-XX-XXX-XX-XX ko'rinishiga olib keladi.
    /// - Bo'sh yoki null bo'lsa — bo'sh string qaytaradi
    /// - Faqat raqamlarni saqlaydi (bo'sh joy, qavs, chiziqlar olib tashlaydi)
    /// - 998 prefiksi yo'q bo'lsa qo'shadi
    /// - Format noto'g'ri bo'lsa (12 ta raqam yo'q) — asl kiritilgan qiymatni qaytaradi
    ///
    /// Misollar:
    /// "901234567" → "+998-90-123-45-67"
    /// "+998 90 123 45 67" → "+998-90-123-45-67"
    /// "998901234567" → "+998-90-123-45-67"
    /// "90123" → "90123" (format xato — o'rniga shuning o'zi)
    /// </summary>
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";

        var original = phone;
        var digits = DigitsOnly(phone);

        // 998 prefiksi yo'q bo'lsa qo'shadi
        if (!digits.StartsWith("998"))
            digits = "998" + digits;

        // Format tekshiruvi — 12 ta raqam bo'lishi kerak (998 + 9 ta mahalliy raqam)
        if (digits.Length != 12)
            return original.Trim();

        // +998-XX-XXX-XX-XX formatiga keltiriladi
        return "+" + digits[0..3] + "-" + digits[3..5] + "-" + digits[5..8] + "-" + digits[8..10] + "-" + digits[10..12];
    }

    /// <summary>
    /// Telefon raqamni tekshiradi. Qaytaradi (valid: bool, normalized: string, errorMessage: string?).
    /// <list type="bullet">
    ///   <item>Valid: oxirgi 9 ta raqam kamida 7 ta belgidan iborat bo'lsa</item>
    ///   <item>Aks holda — xato matni (bo'sh yoki juda qisqa)</item>
    /// </list>
    /// </summary>
    public static (bool Valid, string Normalized, string? Error) Validate(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return (false, "", "Telefon raqami bo'sh");

        var key = Key(phone);
        if (key.Length < 7)
            return (false, phone.Trim(), "Telefon raqami juda qisqa (kamida 7 ta raqam)");

        var normalized = Normalize(phone);
        if (normalized == phone.Trim() && !phone.Contains("998"))
        {
            // Faqat oldingi 7 ta raqam = format emas aytishga teng
            // Biz qayta normalizatsiya qilmadik, demak natija xato format
            return (false, phone.Trim(), "Noto'g'ri telefon raqami formati");
        }

        return (true, normalized, null);
    }
}
