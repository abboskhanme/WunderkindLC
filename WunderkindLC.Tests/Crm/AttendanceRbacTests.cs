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

    /* ---------- U3c: attendance fines — `finance.main`, reading gated too ---------- */

    [Fact]
    public void Attendance_fines_read_and_write_require_finance_main()
    {
        Assert.True(Gated(Controller("AttendanceFinesController.cs"), "finance.main", readRequiresPerm: true));
        // A staff user without the key (or with a sibling page of finance) gets 403 on GET and PUT.
        Assert.False(PermissionRules.HasSection(["students.list"], "finance.main"));
        Assert.False(PermissionRules.HasSection(["finance.bonus"], "finance.main"));
        Assert.False(PermissionRules.CanWrite(["classes"], "finance.main", "PUT"));
        Assert.True(PermissionRules.HasSection(["finance"], "finance.main"));
        Assert.True(PermissionRules.CanWrite(["finance.main"], "finance.main", "PUT"));
    }

    /* ---------- U3d: red list — `students.attendance` (reading gated); profile absences — `students.list` ---------- */

    [Fact]
    public void Red_list_requires_students_attendance_even_for_reading()
    {
        Assert.True(Gated(Controller("AttendanceRedListController.cs"), "students.attendance", readRequiresPerm: true));
        Assert.False(PermissionRules.HasSection(["students.list"], "students.attendance"));
        Assert.False(PermissionRules.HasSection(["finance.main"], "students.attendance"));
        Assert.True(PermissionRules.HasSection(["students"], "students.attendance"));
    }

    [Fact]
    public void Profile_absences_live_in_the_students_list_controller()
    {
        Assert.True(Gated(Controller("StudentsController.cs"), "students.list", readRequiresPerm: false));
        var src = Controller("StudentsController.Absences.cs");
        Assert.Contains("public partial class StudentsController", src);
        Assert.DoesNotContain("[AllowAnonymous]", src);
        Assert.DoesNotContain("[AdminPerm(", src);   // no method-level override that would widen the class gate
    }

    [Fact]
    public void Teacher_sees_only_own_fines_behind_the_salary_permission()
    {
        var portal = Controller("TeacherPortalController.cs");
        Assert.Contains("[Authorize(Roles = \"teacher\")]", portal);
        var fines = Controller("TeacherPortalController.Fines.cs");
        Assert.Contains("TeacherPermissions.Salary", fines);
        // Own rows only: the teacher id comes from the token (Me()), never from the query string.
        Assert.Contains("ListAsync(db, month, t.Id", fines);
        Assert.DoesNotContain("[FromQuery] string? teacherId", fines);
    }
}
