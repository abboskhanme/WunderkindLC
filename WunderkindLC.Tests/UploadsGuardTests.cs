using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// `/uploads` darvozasining DUAL-MODE auth bilan mosligi.
///
/// <para><b>Tarix (prod xatosi):</b> f69e0a1 commitida web SPA `Authorization: Bearer`
/// sarlavhasidan HttpOnly `at` cookie'ga o'tkazildi, lekin `UploadsGuard` yangilanmagan edi:
/// `IssueCookie` Bearer sarlavhasiz darhol qaytardi (ya'ni web'da `up_at` HECH QACHON
/// qo'yilmasdi), token o'qish esa faqat Bearer va `up_at` ni bilardi. Natijada prodda
/// barcha o'quvchi rasmlari 404 bo'ldi.</para>
///
/// <para>Tests loyihasi Server loyihasiga ATAYIN referens qilmaydi (Server Client `.esproj`
/// bog'lamini tortadi), shuning uchun kafolatlar manba matnidan tekshiriladi —
/// `FaceLoginTests` §11 va `MarketingPublicMediaTests` naqshi.</para>
/// </summary>
public class UploadsGuardTests
{
    /* =============================================================================================
     *  Yordamchilar — manba matni (FaceLoginTests naqshi)
     * ========================================================================================== */

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ServerSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot, "WunderkindLC.Server" }.Concat(parts).ToArray()));

    /// <summary>Manbadan bitta a'zo (metod) matnini kesib oladi: boshlanish belgisidan keyingi
    /// a'zo deklaratsiyasigacha. Belgi topilmasa test aniq xabar bilan yiqiladi.</summary>
    private static string Section(string src, string startMarker, string endMarker)
    {
        var start = src.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Manbada topilmadi: {startMarker}");
        var end = src.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Manbada topilmadi (oxiri): {endMarker}");
        return src[start..end];
    }

    /* =============================================================================================
     *  1. Token O'QISH — uch manba: Bearer → up_at → at
     * ========================================================================================== */

    /// <summary>
    /// Darvoza tokenni `at` auth cookie'sidan HAM o'qishi SHART: web SPA cookie-rejimda
    /// `Authorization` sarlavhasini umuman yubormaydi, `at` esa `Path=/` bilan qo'yilgani
    /// uchun brauzer uni `/uploads` so'roviga o'zi qo'shadi. Bu qator o'chsa — cookie-rejimdagi
    /// foydalanuvchiga hamma rasm 404 (prodda bo'lgan xato qaytadi).
    /// </summary>
    [Fact]
    public void Token_manbalari_Bearer_upat_va_at()
    {
        var src = ServerSource("UploadsGuard.cs");
        var tokens = Section(src, "private static IEnumerable<string> TokensOf", "PublicNamesAsync");

        // Uchchala manba ham bor...
        Assert.Contains("\"Bearer \"", tokens);
        Assert.Contains("UploadAccessRules.CookieName", tokens);     // up_at
        Assert.Contains("AuthCookies.AtCookie", tokens);             // at (dual-mode)

        // ...va ustuvorlik tartibi: Bearer → up_at → at (mobil oqim birinchi, o'zgarmasin).
        var bearerAt = tokens.IndexOf("\"Bearer \"", StringComparison.Ordinal);
        var upAt = tokens.IndexOf("UploadAccessRules.CookieName", StringComparison.Ordinal);
        var atAt = tokens.IndexOf("AuthCookies.AtCookie", StringComparison.Ordinal);
        Assert.True(bearerAt < upAt && upAt < atAt,
            "TokensOf tartibi buzilgan — Bearer → up_at → at bo'lishi kerak");
    }

    /// <summary>
    /// ⚠️ Nomzodlar BIRMA-BIR tekshirilishi SHART (faqat birinchi topilgani emas): refresh'dan
    /// keyin `up_at` bir muddat ESKI tokenda qoladi — eskirgan `up_at` yangi `at` bilan kelgan
    /// so'rovni 404 qilib qo'ymasin. Har bir nomzod AYNAN bitta `IsTokenValid` dan o'tadi,
    /// ya'ni `at` dan kelgan token ham imzo/muddat/face-scope bo'yicha bir xil tekshiriladi.
    /// </summary>
    [Fact]
    public void Ruxsat_tekshiruvi_har_bir_nomzodni_alohida_koradi()
    {
        var src = ServerSource("UploadsGuard.cs");
        var allowed = Section(src, "public async Task<bool> IsAllowedAsync", "public void IssueCookie");

        Assert.Matches(new Regex(@"foreach\s*\(\s*var\s+\w+\s+in\s+TokensOf\("), allowed);
        Assert.Contains("IsTokenValid", allowed);
    }

    /// <summary>
    /// Yuz tasdig'i (scope=face) tekshiruvi token VALIDATSIYASINING o'zida — ya'ni `at`
    /// cookie'dan kelgan cheklangan token ham rad etiladi, manba qaysi bo'lishidan qat'iy nazar.
    /// (FaceLoginTests'dagi tekshiruvning dual-mode davomi.)
    /// </summary>
    [Fact]
    public void Face_scope_tekshiruvi_validatsiyada_qolgan()
    {
        var src = ServerSource("UploadsGuard.cs");
        var valid = Section(src, "private bool IsTokenValid", "TokensOf");
        Assert.Contains("FaceScopeClaimType", valid);
    }

    /* =============================================================================================
     *  2. up_at QO'YISH (IssueCookie) — Bearer'siz ham ishlashi
     * ========================================================================================== */

    /// <summary>
    /// `IssueCookie` Bearer sarlavhasi BO'LMASA `at` cookie'dan token olishi SHART.
    /// Aynan shu joy prod xatosining ildizi edi: `if (!header.StartsWith("Bearer ...")) return;`
    /// — web cookie-rejimga o'tgach `up_at` hech qachon qo'yilmay qoldi.
    /// </summary>
    [Fact]
    public void IssueCookie_Bearersiz_ham_at_cookiedan_ishlaydi()
    {
        var src = ServerSource("UploadsGuard.cs");
        var issue = Section(src, "public void IssueCookie", "ClearCookie");

        // Ikkala manba: Bearer (mobil/eski oqim — OLIB TASHLANMAGAN) va at (web cookie-rejim).
        Assert.Contains("\"Bearer \"", issue);
        Assert.Contains("AuthCookies.AtCookie", issue);

        // Eski xato naqshi qaytmasin: "Bearer bo'lmasa darhol return".
        Assert.DoesNotMatch(
            new Regex(@"if\s*\(\s*!\s*header\.StartsWith\(""Bearer[^)]*\)[^;{]*\)\s*return;"),
            issue);

        // Cookie hamon faqat /uploads ga cheklangan bo'lib qo'yiladi.
        Assert.Contains("Path = \"/uploads\"", issue);
    }

    /* =============================================================================================
     *  3. Nomlar va simlar ayri ketmasin
     * ========================================================================================== */

    /// <summary>
    /// `at` cookie nomi va yo'li: `Path=/` bo'lishi SHART — aks holda brauzer uni `/uploads`
    /// so'roviga yubormay qo'yadi va TokensOf'dagi zaxira manba jimgina ishlamay qoladi.
    /// </summary>
    [Fact]
    public void At_cookie_nomi_va_yoli_uploadsga_yetadi()
    {
        var src = ServerSource("CsrfMiddleware.cs");
        Assert.Contains("public const string AtCookie = \"at\";", src);

        var issue = Section(src, "public static void Issue(", "IssueRefresh");
        Assert.Contains("Path = \"/\"", issue);

        // up_at nomi Application'dagi yagona manbadan (Server ham, testlar ham shuni ishlatadi).
        Assert.Equal("up_at", UploadAccessRules.CookieName);
    }

    /// <summary>
    /// Token'siz so'rov 404 olishi (403 emas — fayl mavjudligini ham tasdiqlamaymiz):
    /// Program.cs'dagi darvoza siми joyida turibdimi.
    /// </summary>
    [Fact]
    public void Tokensiz_sorov_404_oladi_darvoza_simi_joyida()
    {
        var src = ServerSource("Program.cs");
        var gate = Section(src, "uploadsGuard.IsAllowedAsync", "IFileProvider Guarded");
        Assert.Contains("Status404NotFound", gate);
    }
}
