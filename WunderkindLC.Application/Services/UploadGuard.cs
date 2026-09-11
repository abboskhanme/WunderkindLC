namespace WunderkindLC.Application.Services;

/// <summary>
/// Yuklanadigan fayllarni xavfsizlik nuqtai nazaridan tekshiradi: hajm va kengaytma (allowlist).
/// Faqat ruxsat etilgan rasm/hujjat turlari qabul qilinadi — <c>.svg</c>/<c>.html</c> kabi
/// brauzerda kod ishga tushira oladigan turlar RAD etiladi (saqlangan XSS oldini olish).
/// </summary>
public static class UploadGuard
{
    public const long MaxBytes = 20_000_000;

    /// <summary>Ruxsat etilgan kengaytmalar (kichik harf, nuqta bilan).</summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt",
        ".mp4", ".webm", ".mov", ".m4a", ".mp3", ".ogg",
    };

    /// <summary>
    /// O'QUVCHI SURATI uchun ruxsat etilgan kengaytmalar — umumiy ro'yxatdan TORROQ.
    ///
    /// <para>Sabab: surat nafaqat ekranda ko'rsatiladi, balki Word sertifikatiga ham qo'yiladi
    /// (<see cref="DocxTemplate.ApplyPhoto"/>). U yerda faqat shu turlar ishonchli ishlaydi:</para>
    /// <list type="bullet">
    ///   <item><c>.heic</c> — Word/LibreOffice uni umuman chiza olmaydi va `@rasm` belgisi
    ///   sertifikatdan JIMGINA yo'qolardi (foydalanuvchi sababini bilmasdi);</item>
    ///   <item><c>.webp</c> — brauzerda ko'rinadi, lekin sertifikatda chiqmaydi;</item>
    ///   <item><c>.gif</c>/<c>.bmp</c>/<c>.tiff</c> — o'lchamini fayl sarlavhasidan o'qiy olmaymiz,
    ///   shuning uchun nisbati saqlanmay CHO'ZILIB ketardi.</item>
    /// </list>
    /// Ilova va admin panelidagi surat oynasi baribir JPEG chiqaradi, ya'ni bu odatiy oqimni
    /// cheklamaydi — faqat "qo'lda boshqa fayl tanlash" yo'lini yopadi.
    /// </summary>
    public static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
    };

    /// <summary>O'quvchi surati sifatida shu manzilni qabul qilsa bo'ladimi (kengaytma bo'yicha).</summary>
    public static bool IsPhotoUrl(string url) =>
        PhotoExtensions.Contains(Path.GetExtension(url));

    /// <summary>
    /// Faylni tekshiradi. Hammasi joyida bo'lsa <c>null</c>, aks holda xato xabarini qaytaradi.
    /// </summary>
    public static string? Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0) return "Fayl bo'sh";
        if (file.Length > MaxBytes) return "Fayl 20 MB dan katta";
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return "Ruxsat etilmagan fayl turi";
        return null;
    }

    /// <summary>Saqlash uchun xavfsiz, takrorlanmas fayl nomi (foydalanuvchi nomidan faqat kengaytma olinadi).</summary>
    public static string SafeName(IFormFile file) =>
        $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
}
