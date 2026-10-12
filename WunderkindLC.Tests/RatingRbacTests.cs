using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// RATING endpoints — RBAC (docs/SPEC-crm-upgrade.md P4.3/P4.7). The test project does not reference the
/// server (see <see cref="HomeEndpointsRbacTests"/>), so the gates are read from the controller source and the
/// decision itself is checked through <see cref="PermissionRules"/> — the rule <c>AdminPermAttribute</c> runs
/// for a staff user (admin/superadmin always pass; other roles get 403).
/// </summary>
public class RatingRbacTests
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

    /// <summary>The attribute block right above <c>[Http…("route")]</c> … up to the method signature.</summary>
    private static string Gate(string src, string httpAttr)
    {
        var i = src.IndexOf(httpAttr, StringComparison.Ordinal);
        Assert.True(i > 0, $"{httpAttr} not found");
        var sig = src.IndexOf("public async Task", i, StringComparison.Ordinal);
        return src[i..sig];
    }

    [Fact]
    public void Every_rating_endpoint_is_permission_gated_including_reads()
    {
        var src = Source("RatingController.cs");
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("[Route(\"api/admin/rating\")]", src);

        Assert.Contains("[AdminPerm(\"classes.rating\", ReadRequiresPerm = true)]", Gate(src, "[HttpGet(\"centre\")]"));
        Assert.Contains("[AdminPerm(\"classes.rating\", \"classes.list\", ReadRequiresPerm = true)]", Gate(src, "[HttpGet(\"groups/{groupId}\")]"));
        Assert.Contains("[AdminPerm(\"students.list\", \"classes.rating\", ReadRequiresPerm = true)]", Gate(src, "[HttpGet(\"students/{studentId}\")]"));
        Assert.Contains("[AdminPerm(\"classes.rating\", ReadRequiresPerm = true)]", Gate(src, "[HttpGet(\"settings\")]"));
        Assert.Contains("[AdminPerm(\"classes.rating\", ReadRequiresPerm = true)]", Gate(src, "[HttpPut(\"settings\")]"));

        // Every action carries its own gate (no ungated method sneaks in).
        var actions = Regex.Matches(src, @"\[Http(Get|Post|Put|Patch|Delete)").Count;
        var gates = Regex.Matches(src, @"\[AdminPerm\(").Count;
        Assert.Equal(actions, gates);
    }

    [Fact]
    public void Staff_without_the_key_is_refused_with_the_key_or_section_allowed()
    {
        // 403 for a staff user without the key (reads are gated: ReadRequiresPerm).
        Assert.False(PermissionRules.HasSection(["students.list"], "classes.rating"));
        Assert.False(PermissionRules.HasSection(["classes.testResults"], "classes.rating"));
        Assert.False(PermissionRules.HasSection(Array.Empty<string>(), "classes.rating"));
        // Allowed: the page key itself or the whole «Guruhlar» section (downward inheritance).
        Assert.True(PermissionRules.HasSection(["classes.rating"], "classes.rating"));
        Assert.True(PermissionRules.HasSection(["classes"], "classes.rating"));

        // Settings PUT needs a write grant: view-only staff cannot change the weight.
        Assert.True(PermissionRules.CanWrite(["classes.rating"], "classes.rating", "PUT"));
        Assert.True(PermissionRules.CanWrite(["classes.rating:edit"], "classes.rating", "PUT"));
        Assert.False(PermissionRules.CanWrite(["classes.rating:create"], "classes.rating", "PUT"));
        Assert.False(PermissionRules.CanWrite(["classes.list"], "classes.rating", "PUT"));
    }

    [Fact]
    public void Cabinet_summary_is_only_for_the_own_child_and_never_checks_the_balance()
    {
        var portal = Source("StudentPortalController.cs");
        Assert.Matches(new Regex(@"\[Authorize\(Roles\s*=\s*""student,parent,admin""\)\]"), portal);

        var src = Source("StudentPortalController.Rating.cs");
        Assert.Contains("[HttpGet(\"rating/summary\")]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        // TargetAsync = own student / own child (parent phone match) / admin with ?studentId=.
        Assert.Contains("await TargetAsync(studentId)", src);
        Assert.Contains("NeedStudentId()", src);
        // No debtor lock (owner decision): the balance is never read.
        Assert.DoesNotContain("Balance", src);
    }
}
