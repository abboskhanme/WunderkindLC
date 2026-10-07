using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// VAKANSIYALAR bo'limining ikki sahifasi — RUXSAT darvozalari:
/// <list type="bullet">
///   <item><c>vacancies.list</c> — vakansiyalar va arizalar (<c>CareerController</c>, javobda CV manzili);</item>
///   <item><c>vacancies.onboarding</c> — moslashuv (<c>OnboardingController</c>, javobda telefon va izohlar).</item>
/// </list>
/// <para><b>NEGA MANBA MATNI:</b> test loyihasi <c>WunderkindLC.Server</c> ga havola QILMAYDI
/// (<see cref="SensitiveReadPermTests"/> bilan bir xil sabab). Kimdir atributni olib tashlasa,
/// kalitni bo'limga kengaytirsa yoki <c>[AllowAnonymous]</c> qo'shsa — test darrov qizaradi.</para>
/// </summary>
public class OnboardingRbacTests
{
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

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string Controller(string file) => Source("WunderkindLC.Server", "Controllers", file);

    private static string ClassHeader(string source, string className)
    {
        var i = source.IndexOf($"public class {className}", StringComparison.Ordinal);
        Assert.True(i > 0, $"{className} topilmadi");
        return source[..i];
    }

    /* ---------------------------------------------------------------- OnboardingController */

    [Fact]
    public void Onboarding_sinf_darajasida_sahifa_kaliti_va_OQISH_ham_darvozalangan()
    {
        var src = Controller("OnboardingController.cs");
        var header = ClassHeader(src, "OnboardingController");

        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"vacancies.onboarding\", ReadRequiresPerm = true)]", header);
        Assert.Contains("[Route(\"api/admin/onboarding\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
    }

    [Fact]
    public void Onboarding_metodlarda_sinf_darvozasini_BEKOR_qiladigan_atribut_yoq()
    {
        // Metod darajasidagi [AdminPerm] sinfdagini bekor qiladi (permissions.md §4) — bu yerda
        // hamma metod bitta sahifa kaliti bilan ishlaydi, shuning uchun BITTA atribut bo'lishi shart.
        var src = Controller("OnboardingController.cs");
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
    }

    [Theory]
    [InlineData("[HttpPost]")]
    [InlineData("[HttpPut(\"{id}\")]")]
    [InlineData("[HttpPut(\"{id}/tasks/{taskId}\")]")]
    [InlineData("[HttpPut(\"{id}/cancel\")]")]
    [InlineData("[HttpPut(\"{id}/reopen\")]")]
    [InlineData("[HttpDelete(\"{id}\")]")]
    [InlineData("[HttpPost(\"steps\")]")]
    [InlineData("[HttpPut(\"steps/{stepId}\")]")]
    [InlineData("[HttpDelete(\"steps/{stepId}\")]")]
    [InlineData("[HttpPut(\"steps/reorder\")]")]
    [InlineData("[HttpGet(\"people\")]")]
    public void Onboarding_endpointlari_mavjud(string route) =>
        Assert.Contains(route, Controller("OnboardingController.cs"));

    /* ---------------------------------------------------------------- CareerController */

    [Fact]
    public void Career_SAHIFA_kaliti_bilan_darvozalangan_bolim_emas()
    {
        // `vacancies` (bo'lim) bo'lsa, faqat «Moslashuv» berilgan xodim yuqoriga o'qish merosi
        // bilan nomzod CV'larini ham o'qir edi.
        var header = ClassHeader(Controller("CareerController.cs"), "CareerController");
        Assert.Contains("[AdminPerm(\"vacancies.list\", ReadRequiresPerm = true)]", header);
        Assert.DoesNotContain("[AdminPerm(\"vacancies\",", header);
    }

    [Fact]
    public void Career_ON_OFF_tugmasi_PUT_TAHRIR_amali_va_tarixga_yoziladi()
    {
        var src = Controller("CareerController.cs");
        var i = src.IndexOf("[HttpPut(\"vacancies/{id}/active\")]", StringComparison.Ordinal);
        Assert.True(i > 0, "ON/OFF endpointi topilmadi");
        var sig = src.IndexOf("public async Task", i, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminPerm", src[i..sig]);
        Assert.DoesNotContain("AllowAnonymous", src[i..sig]);
        var body = src[sig..src.IndexOf("[HttpPost(\"vacancies/{id}/archive\")]", sig, StringComparison.Ordinal)];
        Assert.Contains("audit.Record(\"Vacancy\"", body);
    }

    [Fact]
    public void Career_hired_bolganda_moslashuv_AVTOMATIK_va_auditga_yoziladi()
    {
        var src = Controller("CareerController.cs");
        var i = src.IndexOf("[HttpPost(\"applications/{id}/status\")]", StringComparison.Ordinal);
        var body = src[i..src.IndexOf("[HttpPut(\"applications/{id}/note\")]", i, StringComparison.Ordinal)];
        Assert.Contains("CareerService.CanMoveTo(", body);
        Assert.Contains("changed && a.Status == CareerService.StatusHired", body);
        Assert.Contains("OnboardingService.StartForApplicationAsync(", body);
        Assert.Contains("audit.Record(OnboardingService.AuditEntity", body);
        // Audit SaveChanges'dan OLDIN (audit.md §1).
        Assert.True(body.IndexOf("audit.Record(OnboardingService.AuditEntity", StringComparison.Ordinal)
                    < body.LastIndexOf("SaveChangesAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void MiniApp_faqat_YOQILGAN_vakansiyalarni_korsatadi_va_qabul_qiladi()
    {
        var src = Controller("PublicCareerController.cs");
        Assert.Contains("v.Status == CareerService.VacancyActive", src);
        Assert.Contains("vacancy.Status != CareerService.VacancyActive", src);
    }

    [Fact]
    public void MiniApp_yol_xaritasi_ESKI_bosqichni_alohida_qadam_qilib_chizmaydi()
    {
        // Bosqichlar serverdan keladi (bootstrap) — JS ularni qattiq yozmaydi, faqat eski
        // (legacy) bosqichni yo'l-xaritadan chiqaradi.
        var js = Source("WunderkindLC.Server", "wwwroot", "vakansiya.js");
        Assert.Contains("isLegacy", js);
        Assert.DoesNotContain("'interview'", js);
        Assert.DoesNotContain("'director'", js);
    }

    /* ---------------------------------------------------------------- ruxsat qoidasi */

    [Fact]
    public void Faqat_moslashuv_berilgan_xodim_CV_larni_OQIY_OLMAYDI()
    {
        string[] perms = ["vacancies.onboarding"];
        Assert.True(PermissionRules.HasSection(perms, "vacancies.onboarding"));
        Assert.False(PermissionRules.HasSection(perms, "vacancies.list"));
        Assert.False(PermissionRules.CanWrite(perms, "vacancies.list", "PUT"));
    }

    [Fact]
    public void Faqat_vakansiyalar_berilgan_xodim_moslashuvni_OCHA_OLMAYDI()
    {
        string[] perms = ["vacancies.list"];
        Assert.False(PermissionRules.HasSection(perms, "vacancies.onboarding"));
        Assert.False(PermissionRules.CanWrite(perms, "vacancies.onboarding", "POST"));
    }

    [Theory]
    [InlineData("vacancies")]
    [InlineData("vacancies:edit")]
    public void Eski_BOLIM_ruxsati_ikkala_sahifani_ham_ochadi(string perm)
    {
        // Mavjud xodimlarning `vacancies` ruxsati o'zgarmaydi — pastga meros (permissions.md §2).
        string[] perms = [perm];
        Assert.True(PermissionRules.HasSection(perms, "vacancies.list"));
        Assert.True(PermissionRules.HasSection(perms, "vacancies.onboarding"));
        Assert.True(PermissionRules.CanWrite(perms, "vacancies.list", "PUT"));
        Assert.True(PermissionRules.CanWrite(perms, "vacancies.onboarding", "PUT"));
    }

    [Fact]
    public void Moslashuv_KO_RISH_yetarli_emas_yozish_uchun()
    {
        string[] perms = ["vacancies.onboarding:edit"];
        Assert.True(PermissionRules.CanWrite(perms, "vacancies.onboarding", "PUT"));   // belgilash
        Assert.False(PermissionRules.CanWrite(perms, "vacancies.onboarding", "POST")); // yangi boshlash
        Assert.False(PermissionRules.CanWrite(perms, "vacancies.onboarding", "DELETE"));
    }
}
