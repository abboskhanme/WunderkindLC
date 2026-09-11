namespace WunderkindLC.Application.Services;

/// <summary>
/// Ommaviy havola uchun "slug" — o'qiladigan, lekin TAXMIN QILIB BO'LMAYDIGAN manzil bo'lagi
/// (`ingliz-tili-3f2a`). Daraja testi ham, lid formasi ham shu yerdan oladi.
/// </summary>
public static class SlugUtil
{
    /// <summary>Lotin harf/raqamlarni saqlab, qolganini "-"ga aylantiradi (sodda slugify).</summary>
    public static string Make(string? s)
    {
        var chars = (s ?? "").Trim().ToLowerInvariant()
            .Select(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }

    /// <summary>
    /// Nomdan NOYOB slug yasaydi: `nom-<4 tasodifiy belgi>`. Tasodifiy quyruq ATAYIN — manzil
    /// o'qiladigan bo'lsa-da, "ingliz-tili" deb taxmin qilib boshqa markazning formasini ochib
    /// bo'lmasin. <paramref name="exists"/> — shu jadvalda slug bandmi (chaqiruvchi beradi).
    /// </summary>
    public static async Task<string> UniqueAsync(string? title, Func<string, Task<bool>> exists, string fallback)
    {
        var baseSlug = Make(title);
        if (baseSlug.Length == 0) baseSlug = fallback;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..4]}";
            if (!await exists(slug)) return slug;
        }
        return Guid.NewGuid().ToString("N")[..10];
    }
}
