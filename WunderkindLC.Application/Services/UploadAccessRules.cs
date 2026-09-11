namespace WunderkindLC.Application.Services;

/// <summary>
/// `/uploads` DARVOZASINING SOF QOIDALARI — HTTP/token kontekstisiz, shuning uchun testlanadi.
///
/// <para>Darvozaning o'zi <c>UploadsGuard</c> (Server) da: u tokenni tekshiradi va cookie qo'yadi.
/// Bu yerda esa faqat "qaysi manzil qamrovga kiradi" va "qaysi fayl OCHIQ qoladi" qarorlari —
/// aynan shu joyda xato qilinsa maxfiy fayl ochilib ketadi yoki login sahifasining logotipi
/// yo'qoladi, shuning uchun ular alohida testlanadi.</para>
/// </summary>
public static class UploadAccessRules
{
    /// <summary>Brauzerga qo'yiladigan cookie nomi (faqat <c>/uploads</c> yo'liga tegishli).</summary>
    public const string CookieName = "up_at";

    /// <summary>
    /// Manzildan FAYL NOMINI ajratadi (papkasiz). Solishtirish faqat nom bo'yicha bo'ladi, chunki
    /// `/uploads` — tekis papka va bazada manzil `/uploads/&lt;guid&gt;.png` ko'rinishida saqlanadi.
    /// </summary>
    public static string FileNameOf(string? urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return "";
        var s = urlOrPath.Trim();
        // So'rov qatori bo'lsa tashlanadi ("/uploads/a.png?v=2").
        var q = s.IndexOfAny(['?', '#']);
        if (q >= 0) s = s[..q];
        var slash = s.LastIndexOf('/');
        return slash >= 0 ? s[(slash + 1)..] : s;
    }

    /// <summary>
    /// Ochiq fayllar ro'yxatining amaliy CHEGARASI.
    ///
    /// <para>Ro'yxat butunlay xotirada turadi va HAR bir <c>/uploads</c> so'rovida ko'riladi, ya'ni
    /// u cheksiz o'sa olmaydi. Landing sertifikatlari yillar davomida yig'ilib borishi mumkin —
    /// shuning uchun yuqori, lekin ANIQ chegara qo'yilgan. Chegaradan oshgani JIMGINA tashlanmaydi:
    /// <c>PublicNamesFrom</c> nechta nom kirmaganini qaytaradi va chaqiruvchi buni logga yozadi.</para>
    /// </summary>
    public const int MaxPublicNames = 2000;

    /// <summary>
    /// Shu fayl LOGIN'SIZ berilishi kerakmi.
    ///
    /// <para>Ikki holat bor va ikkalasining ham printsipi BITTA: "ochiq" deb faqat markaz O'ZI
    /// ommaviy ko'rsatayotgan fayl hisoblanadi.</para>
    ///
    /// <list type="number">
    /// <item>Markazning LOGOTIPI (<c>CenterMeta.LogoUrl</c>, <c>CareerAbout.LogoUrl</c>) — login
    /// sahifasida, PWA manifestida va ochiq vakansiya sahifasida kerak.</item>
    /// <item>LANDING sahifasining rasmlari — o'qituvchi surati (<c>LandingTeacher.PhotoUrl</c>),
    /// natija/sertifikat rasmi (<c>LandingCertificate.ImageUrl</c>) va fikr avatari
    /// (<c>LandingTestimonial.AvatarUrl</c>), FAQAT <c>IsActive</c> bo'lganlari. Landing
    /// login'siz ko'riladi (<c>GET /api/public/landing-data</c>), ya'ni bu rasmlarni yopiq
    /// qoldirish mehmonga SINUQ rasm ko'rsatish degani edi.</item>
    /// </list>
    ///
    /// <para>⚠️ <b>Nega faqat <c>IsActive</c>:</b> admin sertifikatni saytdan olib tashlasa, fayl
    /// ham O'SHA ZAHOTI (kesh muddatidan keyin) yopilishi kerak — "bir marta ommaviy bo'lgan fayl
    /// abadiy ommaviy" qoidasi bu yerda ishlamaydi.</para>
    /// </summary>
    public static bool IsPublicFile(string? path, IReadOnlyCollection<string>? publicFileNames)
    {
        if (publicFileNames is null || publicFileNames.Count == 0) return false;
        var name = FileNameOf(path);
        if (name.Length == 0) return false;
        foreach (var p in publicFileNames)
            if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Bazadagi manzillar ro'yxatidan ochiq fayl nomlari to'plamini yasaydi (bo'sh qiymatlar tashlanadi).
    /// Chegara — <see cref="MaxPublicNames"/>.
    /// </summary>
    public static HashSet<string> PublicNamesFrom(IEnumerable<string?> urls) =>
        PublicNamesFrom(urls, MaxPublicNames, out _);

    /// <summary>
    /// Bir xil, lekin chegara va "nechtasi kirmadi" (<paramref name="skipped"/>) bilan.
    ///
    /// <para>⚠️ <b>TARTIB MUHIM:</b> chegaraga yetilganda ro'yxatning OXIRI qirqiladi, shuning uchun
    /// chaqiruvchi eng muhim manzillarni (LOGOTIP) BIRINCHI qo'shishi kerak — login sahifasi
    /// landing sertifikatlari tufayli buzilib qolmasin.</para>
    /// </summary>
    public static HashSet<string> PublicNamesFrom(IEnumerable<string?> urls, int max, out int skipped)
    {
        skipped = 0;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in urls)
        {
            var name = FileNameOf(u);
            if (name.Length == 0) continue;
            // Takror nom yangi joy egallamaydi — u chegaradan keyin ham qabul qilinaveradi.
            if (set.Count >= max && !set.Contains(name)) { skipped++; continue; }
            set.Add(name);
        }
        return set;
    }
}
