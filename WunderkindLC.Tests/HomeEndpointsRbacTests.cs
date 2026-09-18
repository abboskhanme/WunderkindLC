using System.Text.RegularExpressions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BOSH SAHIFA endpointlarining RUXSAT darvozasi: <c>GET api/admin/dashboard/summary</c>,
/// <c>GET api/admin/schedule/grid</c> va <c>GET api/admin/schedule/export</c>.
///
/// <para><b>NEGA MANBA MATNI:</b> test loyihasi <c>WunderkindLC.Server</c> ga havola QILMAYDI
/// (<see cref="SensitiveReadPermTests"/> bilan bir xil sabab) — darvoza controller faylidan
/// o'qib tekshiriladi. Kimdir atributni olib tashlasa yoki <c>[AllowAnonymous]</c> qo'shsa,
/// test darrov qizaradi.</para>
/// </summary>
public class HomeEndpointsRbacTests
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

    private static string Source(string file)
    {
        var path = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file);
        Assert.True(File.Exists(path), $"Controller fayli topilmadi: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Sinf e'lonidan OLDINGI atributlar (sinf darajasidagi darvoza).</summary>
    private static string ClassHeader(string source, string className)
    {
        var i = source.IndexOf($"public class {className}", StringComparison.Ordinal);
        Assert.True(i > 0, $"{className} topilmadi");
        return source[..i];
    }

    [Fact]
    public void Dashboard_summary_faqat_admin_superadmin_xodimga()
    {
        var src = Source("DashboardController.cs");
        var header = ClassHeader(src, "DashboardController");

        Assert.Matches(new Regex(@"\[Authorize\(Roles\s*=\s*""admin,superadmin,staff""\)\]"), header);
        Assert.Contains("[HttpGet(\"summary\")]", src);
        // O'qituvchi/o'quvchi/ota-ona roli yo'q, anonim kirish yo'q.
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.DoesNotMatch(new Regex(@"Roles\s*=\s*""[^""]*(teacher|student|parent)"), src);
    }

    [Fact]
    public void Jadval_tori_va_eksporti_dars_jadvali_ruxsati_ostida()
    {
        var src = Source("ScheduleController.cs");
        var header = ClassHeader(src, "ScheduleController");

        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"schedule.timetable\")]", header);
        Assert.Contains("[HttpGet(\"grid\")]", src);
        Assert.Contains("[HttpGet(\"export\")]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Metod darajasidagi [AdminPerm] sinfdagisini BEKOR qiladi (permissions.md §4) —
        // bu yerda torroq/kengroq kalit qo'yilmagan bo'lishi kerak.
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
        // Jadvalni O'ZGARTIRUVCHI endpoint yo'q (schedule.md §5).
        Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Patch|Delete)"), src);
    }
}
