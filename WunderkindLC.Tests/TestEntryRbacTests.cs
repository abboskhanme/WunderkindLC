using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM U4a — RBAC of the new test endpoints (overview, batch grid save, send results). The test project does not
/// reference the server (see <see cref="RatingRbacTests"/>): the gates are read from the controller source and the
/// decision is checked through <see cref="PermissionRules"/> (what <c>AdminPermAttribute</c> runs for staff).
/// </summary>
public class TestEntryRbacTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo root (WunderkindLC.slnx) not found");
            return dir!.FullName;
        }
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    [Fact]
    public void Admin_endpointlari_testResults_sahifa_kaliti_bilan_darvozalangan()
    {
        var main = Source("TestResultsController.cs");
        Assert.Contains("[AdminPerm(\"classes.testResults\")]", main);
        Assert.Contains("[Route(\"api/admin/test-results\")]", main);

        var grid = Source("TestResultsController.Grid.cs");
        Assert.Contains("[HttpGet(\"overview\")]", grid);
        Assert.Contains("[HttpPut(\"{id}/scores/batch\")]", grid);
        Assert.Contains("[HttpPost(\"{id}/send-results\")]", grid);
        // No method-level override (it would REPLACE the class gate) and no anonymous hole.
        Assert.DoesNotContain("AdminPerm", grid);
        Assert.DoesNotContain("AllowAnonymous", grid);
    }

    [Fact]
    public void Kalitsiz_xodim_403_yozish_amal_boyicha()
    {
        // Without the key: no read of the page, no batch (PUT = edit), no send (POST = create).
        Assert.False(PermissionRules.HasSection(["students.list"], "classes.testResults"));
        Assert.False(PermissionRules.CanWrite(["students.list"], "classes.testResults", "PUT"));
        Assert.False(PermissionRules.CanWrite(["classes.rating"], "classes.testResults", "POST"));
        // Edit-only staff saves the grid but cannot send to parents (send = create).
        Assert.True(PermissionRules.CanWrite(["classes.testResults:edit"], "classes.testResults", "PUT"));
        Assert.False(PermissionRules.CanWrite(["classes.testResults:edit"], "classes.testResults", "POST"));
        Assert.True(PermissionRules.CanWrite(["classes.testResults"], "classes.testResults", "POST"));
        Assert.True(PermissionRules.CanWrite(["classes"], "classes.testResults", "POST"));
    }

    [Fact]
    public void Oqituvchi_endpointlari_faqat_oz_guruhi_OwnsGroup_orqali()
    {
        var src = Source("TeacherPortalController.Tests.cs");
        Assert.DoesNotContain("AllowAnonymous", src);
        var actions = Regex.Matches(src, @"\[Http(Get|Post|Put|Patch|Delete)").Count;
        Assert.Equal(2, actions);
        Assert.Equal(actions, Regex.Matches(src, @"await OwnsGroup\(groupId\)").Count);
        // The portal controller itself is teacher-only.
        Assert.Matches(new Regex(@"\[Authorize\(Roles\s*=\s*""teacher""\)\]"), Source("TeacherPortalController.cs"));
    }
}
