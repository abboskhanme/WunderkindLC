namespace WunderkindLC.Application.Services;

/// <summary>
/// SELFI FAYLLARINING JOYI — yuz tekshiruvidan qolgan suratlar <c>uploads/face/</c> ostida
/// saqlanadi (ilgari <c>uploads/</c> ning O'ZIDA edi).
///
/// <para><b>Nega alohida papka?</b> Uchta sabab, uchalasi ham papka bo'lgandagina mumkin:</para>
/// <list type="number">
///   <item><b>ZAXIRAGA KIRMAYDI.</b> <c>docker-compose</c> dagi kunlik backup butun
///     <c>uploads</c> ni arxivlaydi va arxiv Telegram'ga/off-site'ga ketadi. Bolalarning
///     biometrik suratlari u yerda YOTMASLIGI kerak — <c>tar --exclude='uploads/face'</c>.</item>
///   <item><b>STATIK YO'L BILAN BERILMAYDI.</b> Papka <c>PrivateFolderFileProvider</c> ro'yxatida
///     (<c>uploads/certificates</c> bilan bir xil naqsh) — manzilni bilgan odam ham ololmaydi.
///     Rasm faqat avtorizatsiyalangan admin endpointi orqali: <c>/api/admin/face/checks/{id}/image</c>.</item>
///   <item><b>Ommaviy o'chirish oson</b> — "barcha biometrik suratlarni o'chir" bitta papka.</item>
/// </list>
///
/// <para>⚠️ ESKI YOZUVLAR: modul chiqqandan keyingi bir necha kunda yozilgan selfilar
/// <c>/uploads/&lt;guid&gt;.jpg</c> ko'rinishida bo'lishi mumkin. <see cref="ResolvePath"/> IKKALA
/// ko'rinishni ham tushunadi — eski rasm "yo'qolib qolmasin" (u baribir <c>KeepChecks</c> bo'yicha
/// o'z-o'zidan o'chadi).</para>
/// </summary>
public static class FaceStorage
{
    /// <summary><c>uploads</c> ichidagi papka nomi.</summary>
    public const string FolderName = "face";

    /// <summary>Bazada saqlanadigan manzil prefiksi.</summary>
    public const string UrlPrefix = "/uploads/face/";

    private const string UploadsPrefix = "/uploads/";

    /// <summary>Selfilar papkasining fizik yo'li (mavjud bo'lmasa chaqiruvchi yaratadi).</summary>
    public static string DirectoryPath(string contentRoot) =>
        Path.Combine(contentRoot, "uploads", FolderName);

    /// <summary>Yangi fayl uchun manzil. Nom — tasodifiy GUID (asl fayl nomi HECH QACHON
    /// saqlanmaydi — <c>UploadGuard</c> qoidasi).</summary>
    public static string NewUrl() => $"{UrlPrefix}{Guid.NewGuid():N}.jpg";

    /// <summary>Manzil selfilar papkasiga tegishlimi (eski, tekis manzillar — yo'q).</summary>
    public static bool IsFaceUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith(UrlPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Bazadagi manzildan FIZIK yo'lni hisoblaydi. <c>null</c> — manzil yaroqsiz (yo'l
    /// manipulyatsiyasi, boshqa papka, bo'sh qiymat).
    ///
    /// <para>⚠️ Yo'l manipulyatsiyasidan himoya: manzildan FAQAT fayl nomi olinadi va u
    /// <c>Path.GetFileName</c> bilan aynan bir xil bo'lishi tekshiriladi — ya'ni
    /// <c>../../etc/passwd</c> yoki <c>face/../secret.jpg</c> o'tmaydi.</para>
    /// </summary>
    public static string? ResolvePath(string contentRoot, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(UploadsPrefix, StringComparison.Ordinal))
            return null;

        var rest = url[UploadsPrefix.Length..];
        var inFaceFolder = rest.StartsWith(FolderName + "/", StringComparison.Ordinal);
        if (inFaceFolder) rest = rest[(FolderName.Length + 1)..];

        // Nomda hech qanday yo'l segmenti qolmasligi SHART.
        if (rest.Length == 0 || !string.Equals(rest, Path.GetFileName(rest), StringComparison.Ordinal))
            return null;

        return inFaceFolder
            ? Path.Combine(DirectoryPath(contentRoot), rest)
            : Path.Combine(contentRoot, "uploads", rest);
    }
}
