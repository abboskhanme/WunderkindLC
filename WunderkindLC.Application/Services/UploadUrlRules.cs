using System.Text.RegularExpressions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// BAZADAGI FAYL MANZILI → DISKDAGI FAYL: qaysi manzilga ishonish mumkin (sof qoidalar, testlangan).
///
/// <para><b>Muammo:</b> ko'p maydonlar (<c>TestResult.PdfUrl</c>, <c>JobApplication.CvUrl</c>,
/// <c>Book.CoverUrl</c>) klientdan keladi. Server ularni keyin FAYL sifatida o'qiydi (botga
/// yuboradi) yoki O'CHIRADI. Manzil tekshirilmasa: o'qituvchi <c>PdfUrl = "/../.env"</c> qo'yib
/// botdan serverning maxfiy faylini olardi; nomzod <c>CvUrl</c> ga BEGONA PDF (shartnoma, boshqa
/// nomzod CV'si) manzilini yozib, ariza o'chirilganda o'sha faylni diskdan o'chirtirardi.</para>
///
/// <para><b>Ikki qatlam (ikkalasi ham SHART):</b></para>
/// <list type="number">
///   <item><b>Yozishda</b> — manzil SERVER O'ZI yaratgan nom naqshiga mos bo'lishi kerak
///   (<see cref="IsServerPdf"/>, <see cref="IsCareerCv"/>). Aks holda 400.</item>
///   <item><b>O'qish/o'chirishda</b> — yo'l <see cref="ResolveFlat"/> bilan hal qilinadi: faqat
///   <c>uploads/</c> ning O'ZIDAGI fayl (pastki papka, <c>..</c>, absolut yo'l — rad). Bazada
///   eskidan (tekshiruvdan oldin) qolgan yomon qiymat ham shu yerda to'xtaydi.</item>
/// </list>
/// </summary>
public static class UploadUrlRules
{
    /// <summary>Bazadagi manzil prefiksi.</summary>
    public const string Prefix = "/uploads/";

    /// <summary>Nomzod CV fayli nomining prefiksi (<c>PublicCareerController.UploadCv</c>). CV'ni
    /// boshqa yuklashlardan (shartnoma, test PDF, skan) AJRATADI — ariza faqat shu endpoint
    /// yaratgan faylga ishora qila oladi.</summary>
    public const string CvPrefix = "cv-";

    // `UploadGuard.SafeName`: "{Guid:N}{kengaytma kichik harfda}" — 32 ta kichik hex.
    // ⚠️ `\z` (`$` EMAS): `$` oxirgi "\n" dan oldin ham mos keladi — "...pdf\n" o'tib ketardi.
    private static readonly Regex ServerPdf =
        new(@"^/uploads/[0-9a-f]{32}\.pdf\z", RegexOptions.CultureInvariant);

    private static readonly Regex CareerCv =
        new(@"^/uploads/cv-[0-9a-f]{32}\.pdf\z", RegexOptions.CultureInvariant);

    // `TestCertificateService.SaveCertFileAsync`: "/uploads/certificates/cert-{Guid:N}.docx|.pdf".
    private static readonly Regex TestCertificate =
        new(@"^/uploads/certificates/cert-[0-9a-f]{32}\.(docx|pdf)\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Umumiy yuklash endpointi (<c>POST /api/admin/uploads</c>, o'qituvchining
    /// <c>test-results/uploads</c>) saqlagan PDF manzilimi — <c>/uploads/&lt;32 hex&gt;.pdf</c>.
    /// Onlayn test savollari faqat shunday bo'lishi mumkin.
    /// </summary>
    public static bool IsServerPdf(string? url) => url is not null && ServerPdf.IsMatch(url);

    // Umumiy yuklash (`UploadGuard.SafeName`) — kengaytma alohida ro'yxat bilan tekshiriladi.
    private static readonly Regex ServerUpload =
        new(@"^/uploads/[0-9a-f]{32}(\.[a-z0-9]{2,5})\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Umumiy yuklash (<c>UploadGuard.SafeName</c>) yoki server o'zi yozgan hujjat
    /// (<c>ContractService.SaveUploadAsync</c>) manzilimi — <c>/uploads/&lt;32 hex&gt;.&lt;kengaytma&gt;</c>,
    /// kengaytma <see cref="UploadGuard.AllowedExtensions"/> dan (kichik harf). Tekis papka.
    /// </summary>
    public static bool IsServerUpload(string? url)
    {
        if (url is null) return false;
        var m = ServerUpload.Match(url);
        return m.Success && UploadGuard.AllowedExtensions.Contains(m.Groups[1].Value);
    }

    /// <summary><see cref="IsServerUpload"/> + aynan shu kengaytma (nuqta bilan, masalan ".docx").</summary>
    public static bool IsServerUpload(string? url, string extension) =>
        IsServerUpload(url) && url!.EndsWith(extension, StringComparison.Ordinal);

    /// <summary>Karyera Mini App'i yuklagan CV manzilimi — <c>/uploads/cv-&lt;32 hex&gt;.pdf</c>.</summary>
    public static bool IsCareerCv(string? url) => url is not null && CareerCv.IsMatch(url);

    /// <summary>Test sertifikati fayli (server yaratgan) manzilimi.</summary>
    public static bool IsTestCertificateFile(string? url) => url is not null && TestCertificate.IsMatch(url);

    /// <summary>Yangi CV fayl nomi (papkasiz).</summary>
    public static string NewCvFileName() => $"{CvPrefix}{Guid.NewGuid():N}.pdf";

    /// <summary>
    /// <c>/uploads/&lt;nom&gt;</c> → <c>{contentRoot}/uploads/&lt;nom&gt;</c>. Faqat <c>uploads/</c>
    /// ning O'ZIDAGI fayl: pastki papka (<c>face/</c>, <c>certificates/</c>), <c>..</c>, teskari
    /// chiziq, absolut yo'l — <c>null</c>. Natija <c>Path.GetFullPath</c> bilan qayta tekshiriladi
    /// (papka ichida ekani). Fayl mavjudligi TEKSHIRILMAYDI.
    /// </summary>
    public static string? ResolveFlat(string contentRoot, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var name = url[Prefix.Length..];
        if (name.Length == 0 || name == "." || name == ".." || name.Contains('/') || name.Contains('\\')
            || name.Contains(':') || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
            return null;

        var root = Path.GetFullPath(Path.Combine(contentRoot, "uploads"));
        var full = Path.GetFullPath(Path.Combine(root, name));
        var dir = Path.GetDirectoryName(full);
        return string.Equals(dir, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? full
            : null;
    }
}
