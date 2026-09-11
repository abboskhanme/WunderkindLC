using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// `/uploads` DARVOZASINING QOIDALARI (<see cref="UploadAccessRules"/>).
///
/// <para>Bu yerdagi xato ikki tomonga og'ishi mumkin: maxfiy fayl ochilib ketadi, yoki login
/// sahifasining logotipi yo'qoladi (foydalanuvchi tizimga kira olmay qoladi degani emas, lekin
/// ko'rinishi buziladi). Shuning uchun ikkala yo'nalish ham testlanadi.</para>
/// </summary>
public class UploadAccessRulesTests
{
    // ---------------------------------------------------------------- FileNameOf

    [Theory]
    [InlineData("/uploads/abc123.png", "abc123.png")]
    [InlineData("abc123.png", "abc123.png")]
    [InlineData("/uploads/certificates/cert-1.pdf", "cert-1.pdf")]
    public void FileNameOf_FaqatFaylNominiOladi(string kirish, string kutilgan)
    {
        Assert.Equal(kutilgan, UploadAccessRules.FileNameOf(kirish));
    }

    [Fact]
    public void FileNameOf_SOROVQATORINItashlaydi()
    {
        // Brauzer keshni yangilash uchun "?v=2" qo'shishi mumkin — nom o'zgarmasligi kerak.
        Assert.Equal("logo.png", UploadAccessRules.FileNameOf("/uploads/logo.png?v=2"));
        Assert.Equal("logo.png", UploadAccessRules.FileNameOf("/uploads/logo.png#top"));
    }

    [Fact]
    public void FileNameOf_BOSHqiymatlarda_BOSHqaytaradi()
    {
        Assert.Equal("", UploadAccessRules.FileNameOf(null));
        Assert.Equal("", UploadAccessRules.FileNameOf("   "));
        Assert.Equal("", UploadAccessRules.FileNameOf("/uploads/"));
    }

    // ---------------------------------------------------------------- IsPublicFile

    [Fact]
    public void LOGOTIP_ochiqQoladi()
    {
        // Logotip login sahifasida va PWA manifestida kerak — foydalanuvchi hali kirmagan.
        var ochiq = UploadAccessRules.PublicNamesFrom(["/uploads/logo-abc.png"]);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/logo-abc.png", ochiq));
    }

    [Fact]
    public void BOSHQAfayl_OCHIQEMAS()
    {
        var ochiq = UploadAccessRules.PublicNamesFrom(["/uploads/logo-abc.png"]);

        // O'quvchi surati, passport skani, shartnoma — hech biri ro'yxatda emas.
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/surat-xyz.jpg", ochiq));
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/passport-1.pdf", ochiq));
    }

    [Fact]
    public void OchiqRoyxatBOSHbolsa_HECHNARSAochilmaydi()
    {
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/logo-abc.png", []));
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/logo-abc.png", null));
    }

    [Fact]
    public void OchiqFayl_PAPKAdanQATIYNAZARnomBoyichaTopiladi()
    {
        // Bazada manzil "/uploads/x.png", so'rov esa boshqacha yozilgan bo'lishi mumkin —
        // solishtirish faqat FAYL NOMI bo'yicha (papka tekis).
        var ochiq = UploadAccessRules.PublicNamesFrom(["/uploads/logo-abc.png"]);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/logo-abc.PNG", ochiq));
    }

    // ------------------------------------------------- LANDING (ommaviy sahifa rasmlari)
    //
    // Landing login'SIZ ko'riladi (`GET /api/public/landing-data`). Admin o'qituvchi suratini yoki
    // sertifikat rasmini yuklaganda manzil `/uploads/<guid>.png` bo'ladi — u yopiq bo'lsa admin
    // (login qilgan) rasmni ko'radi, mehmon esa 404 va SINUQ rasm oladi.
    //
    // Guard bazadan FAQAT `IsActive` yozuvlarning manzillarini yig'adi — quyidagi testlar aynan
    // shu yig'ilgan ro'yxat ustidagi qarorni tekshiradi.

    /// <summary>Guard'dagi so'rovning modeli: faqat FAOL yozuvlarning manzillari ro'yxatga tushadi.</summary>
    private static HashSet<string> OchiqRoyxat(
        string logo,
        (string Url, bool IsActive)[] oqituvchilar,
        (string Url, bool IsActive)[] sertifikatlar,
        (string Url, bool IsActive)[]? fikrlar = null)
    {
        var urls = new List<string?> { logo };   // LOGOTIP birinchi (chegara qirqsa ham qolsin)
        urls.AddRange(oqituvchilar.Where(t => t.IsActive).Select(t => (string?)t.Url));
        urls.AddRange(sertifikatlar.Where(c => c.IsActive).Select(c => (string?)c.Url));
        urls.AddRange((fikrlar ?? []).Where(f => f.IsActive).Select(f => (string?)f.Url));
        return UploadAccessRules.PublicNamesFrom(urls);
    }

    [Fact]
    public void FAOL_oqituvchiRasmi_OCHIQ()
    {
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [("/uploads/teacher-1.jpg", true)], []);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/teacher-1.jpg", ochiq));
    }

    [Fact]
    public void FAOL_sertifikatRasmi_OCHIQ()
    {
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [], [("/uploads/cert-1.png", true)]);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/cert-1.png", ochiq));
    }

    [Fact]
    public void FAOLEMAS_oqituvchiVaSertifikatRasmi_YOPIQ()
    {
        // Admin saytdan olib tashlagan bo'lsa — fayl ham darhol yopilishi kerak.
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [("/uploads/teacher-eski.jpg", false)],
            [("/uploads/cert-eski.png", false)]);

        Assert.False(UploadAccessRules.IsPublicFile("/uploads/teacher-eski.jpg", ochiq));
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/cert-eski.png", ochiq));
    }

    [Fact]
    public void FAOL_fikrAvatari_OCHIQ()
    {
        // "Fikrlar" (testimonials) bo'limi landing'da login'siz ko'rinadi — avatar yopiq bo'lsa
        // mehmon sinuq rasm ko'rardi (CMS avatarni xuddi shu `/uploads/<guid>.png` ga yuklaydi).
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [], [], [("/uploads/fikr-1.png", true)]);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/fikr-1.png", ochiq));
    }

    [Fact]
    public void FAOLEMAS_fikrAvatari_YOPIQ()
    {
        // Admin fikrni saytdan olib tashlasa (IsActive=false) — avatari ham darhol yopiladi.
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [], [], [("/uploads/fikr-eski.png", false)]);

        Assert.False(UploadAccessRules.IsPublicFile("/uploads/fikr-eski.png", ochiq));
    }

    [Fact]
    public void LANDING_ochilgach_LOGOTIP_hamon_OCHIQ()
    {
        // Regressiya: landing rasmlari (o'qituvchi + sertifikat + FIKR avatari) qo'shilishi
        // logotipni siqib chiqarmasligi kerak — u login sahifasida kerak.
        var ochiq = OchiqRoyxat("/uploads/logo-abc.png",
            [("/uploads/teacher-1.jpg", true)], [("/uploads/cert-1.png", true)],
            [("/uploads/fikr-1.png", true)]);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/logo-abc.png", ochiq));
    }

    [Fact]
    public void LANDING_ochilgach_BOSHQAyuklanganFayl_YOPIQ_qoladi()
    {
        // Regressiya: passport skani / shartnoma / o'quvchi surati baribir ro'yxatda emas.
        var ochiq = OchiqRoyxat("/uploads/logo.png",
            [("/uploads/teacher-1.jpg", true)], [("/uploads/cert-1.png", true)],
            [("/uploads/fikr-1.png", true)]);

        Assert.False(UploadAccessRules.IsPublicFile("/uploads/passport-skan.pdf", ochiq));
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/shartnoma-9.docx", ochiq));
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/face/selfi-1.jpg", ochiq));
    }

    // ---------------------------------------------------------------- CHEGARA

    [Fact]
    public void CHEGARAdanOSHGANI_qirqiladi_vaSANALADI()
    {
        // Cheklov JIMGINA emas: nechtasi kirmagani qaytadi (guard uni logga yozadi).
        var urls = Enumerable.Range(0, 5).Select(i => (string?)$"/uploads/cert-{i}.png");

        var ochiq = UploadAccessRules.PublicNamesFrom(urls, max: 3, out var skipped);

        Assert.Equal(3, ochiq.Count);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void CHEGARAdaLOGOTIPbirinchi_bolgani_uchun_QOLADI()
    {
        // Guard logotipni BIRINCHI qo'shadi — chegara faqat landing rasmlarini qirqadi.
        var urls = new List<string?> { "/uploads/logo.png" };
        urls.AddRange(Enumerable.Range(0, 5).Select(i => (string?)$"/uploads/cert-{i}.png"));

        var ochiq = UploadAccessRules.PublicNamesFrom(urls, max: 2, out var skipped);

        Assert.True(UploadAccessRules.IsPublicFile("/uploads/logo.png", ochiq));
        Assert.Equal(4, skipped);
    }

    [Fact]
    public void CHEGARA_TAKRORnomniQAYTAsanamaydi()
    {
        // Bir xil rasm ikki yozuvda ishlatilgan bo'lsa u to'plamda yangi joy egallamaydi.
        var urls = new List<string?> { "/uploads/a.png", "/uploads/a.png", "/uploads/b.png" };

        var ochiq = UploadAccessRules.PublicNamesFrom(urls, max: 2, out var skipped);

        Assert.Equal(2, ochiq.Count);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void PublicNamesFrom_BOSHvaNULLqiymatlarniTASHLAYDI()
    {
        // Logotip qo'yilmagan markazda ro'yxat BO'SH bo'lishi kerak — aks holda bo'sh nom
        // hamma narsani ochib yuborishi mumkin edi.
        var ochiq = UploadAccessRules.PublicNamesFrom([null, "", "   ", "/uploads/logo.png"]);

        Assert.Single(ochiq);
        Assert.Contains("logo.png", ochiq);
        Assert.False(UploadAccessRules.IsPublicFile("/uploads/", ochiq));
    }
}
