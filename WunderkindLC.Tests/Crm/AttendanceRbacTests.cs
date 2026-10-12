using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// RBAC of the Phase 3 endpoints (SPEC P3.7). The test project does not reference the Server, so — like
/// <c>SensitiveReadPermTests</c> — the gate is read from the controller source and the permission semantics
/// (a staff user WITHOUT the key gets 403) are checked with the same <see cref="PermissionRules"/> the
/// <c>AdminPermAttribute</c> calls.
/// </summary>
public class AttendanceRbacTests
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

    private static string Controller(string file)
    {
        var path = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file);
        Assert.True(File.Exists(path), $"Controller not found: {path}");
        return File.ReadAllText(path);
    }

    private static bool Gated(string source, string key, bool readRequiresPerm) =>
        Regex.IsMatch(source,
            @"\[AdminPerm\(\s*""" + Regex.Escape(key) + @"""" +
            (readRequiresPerm ? @"[^\]]*\bReadRequiresPerm\s*=\s*true\b" : "") + @"[^\]]*\)\]");

    /* ---------- U3a: attendance settings — `classes` (journal policy gate) ---------- */

    [Fact]
    public void Attendance_settings_are_gated_by_classes()
    {
        Assert.True(Gated(Controller("AttendanceSettingsController.cs"), "classes", readRequiresPerm: false));
    }

    [Fact]
    public void Staff_without_classes_cannot_save_attendance_settings()
    {
        // 403: no key at all, or an unrelated section.
        Assert.False(PermissionRules.CanWrite(["students.list"], "classes", "PUT"));
        Assert.False(PermissionRules.CanWrite([], "classes", "PUT"));
        // A page key of the section reads upward but never writes the section (permissions.md §2).
        Assert.False(PermissionRules.CanWrite(["classes.list"], "classes", "PUT"));
        Assert.True(PermissionRules.CanWrite(["classes"], "classes", "PUT"));
        Assert.True(PermissionRules.CanWrite(["classes:edit"], "classes", "PUT"));
    }
}
