using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// RBAC and wiring of the HR (Topshiriqlar) controllers at SOURCE level — the test project does not reference
/// <c>WunderkindLC.Server</c> (same approach as <see cref="SensitiveReadPermTests"/>) — plus the permission rule
/// those gates run (<see cref="PermissionRules"/>, the function <c>AdminPermAttribute</c> calls) and the LC
/// adaptations that are pure (Telegram button, EF names).
/// </summary>
public class HrTasksRbacTests
{
    private static string Source(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine([dir!.FullName, .. relative]);
        Assert.True(File.Exists(path), $"Fayl topilmadi: {path}");
        return File.ReadAllText(path);
    }

    private static string Controller(string file) => Source("WunderkindLC.Server", "Controllers", file);

    [Fact]
    public void Panel_hrTasks_kaliti_bilan_oqish_ham_darvozalangan_va_no_store()
    {
        var src = Controller("HrTasksController.cs");
        Assert.Matches(new Regex(@"\[AdminPerm\(\s*""hrTasks""\s*,\s*ReadRequiresPerm\s*=\s*true\s*\)\]"), src);
        Assert.Contains("[Authorize]", src);
        Assert.Contains("[NoStore]", src);
        Assert.Contains("[Route(\"api/admin/hr\")]", src);
        // no method may override the class gate or open it anonymously
        Assert.Single(Regex.Matches(src, @"\[AdminPerm\("));
        Assert.DoesNotContain("AllowAnonymous", src);
        // every write goes through the scoped panel (PanelAsync) — the service decides which rows
        Assert.Contains("service.PanelAsync(Actor, Ct)", src);
    }

    [Fact]
    public void Mening_topshiriqlarim_har_qanday_akkaunt_lekin_HR_qatori_bilan()
    {
        var src = Controller("HrMeController.cs");
        Assert.Contains("[Authorize]", src);
        Assert.Contains("[NoStore]", src);
        Assert.Contains("[Route(\"api/hr/me\")]", src);
        Assert.DoesNotContain("[AdminPerm", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        // the employee comes from the TOKEN, never from the request
        Assert.Contains("service.MeAsync(User.FindFirst(ClaimTypes.NameIdentifier)?.Value", src);
        // /me error codes drop the hr_ prefix like the old Mini App API (proof_required, task_not_owned …)
        Assert.Contains("ex.Code[3..]", src);
    }

    [Fact]
    public void NoStore_har_javobga_yoziladi()
    {
        var src = Controller("NoStoreAttribute.cs");
        Assert.Contains("IAlwaysRunResultFilter", src);
        Assert.Contains("no-store", src);
    }

    [Fact]
    public void Akkaunt_ochirish_HR_tarixini_tekshiradi()
    {
        Assert.Contains("HrTasksService.ReleaseEmployeeAsync(db, id,", Controller("StaffController.cs"));
        var teachers = Controller("TeachersController.cs");
        Assert.Contains("HrTasksService.ReleaseEmployeeAsync(db, teacher.UserId,", teachers);
        // archive = the teacher left → HR row inactive; restore → active again
        Assert.Contains("HrTasksService.SetAccountActiveAsync(db, teacher.UserId, false,", teachers);
        Assert.Contains("HrTasksService.SetAccountActiveAsync(db, teacher.UserId, true,", teachers);
        // the scheduler does not run in import mode
        var program = Source("WunderkindLC.Server", "Program.cs");
        Assert.Matches(new Regex(@"if \(!WunderkindLC\.Application\.Services\.ImportMode\.Enabled\)\s*\n\s*builder\.Services\.AddHostedService<WunderkindLC\.Application\.HrTasks\.HrTasksScheduler>\(\);"), program);
    }

    [Fact]
    public void Ruxsat_qoidasi_kalitsiz_xodim_panelga_kirmaydi()
    {
        // what AdminPermAttribute(ReadRequiresPerm) checks for a staff member
        Assert.False(PermissionRules.HasSection(["staff", "tasks.board"], HrTaskAccess.PermissionKey));
        Assert.False(PermissionRules.HasSection(["hrTasksX"], HrTaskAccess.PermissionKey));
        Assert.True(PermissionRules.HasSection(["hrTasks:edit"], HrTaskAccess.PermissionKey)); // reads open
        Assert.False(PermissionRules.CanWrite(["hrTasks:edit"], HrTaskAccess.PermissionKey, "POST")); // creating needs :create
        Assert.True(PermissionRules.CanWrite([HrTaskAccess.PermissionKey], HrTaskAccess.PermissionKey, "DELETE"));
        // panel role on the employee card follows the same rule
        Assert.Equal("manager", HrTasksService.PanelRole(Roles.Staff, ["hrTasks:create"], HrRoles.Manager));
        Assert.Null(HrTasksService.PanelRole(Roles.Staff, ["staff"], HrRoles.Manager));
        Assert.Null(HrTasksService.PanelRole(Roles.Teacher, [HrTaskAccess.PermissionKey], HrRoles.Manager));
        Assert.Equal("admin", HrTasksService.PanelRole(Roles.Staff, [], HrRoles.Director));
    }

    [Fact]
    public void Katalogda_hrTasks_kaliti_bor()
    {
        var catalog = Source("WunderkindLC.Client", "src", "config", "constants.ts");
        Assert.Contains("{ key: 'hrTasks', label: 'Topshiriqlar' }", catalog);
    }

    // ---------------------------------------------------------------- LC adaptations (pure)

    [Fact]
    public void Telegram_tugmasi_Mini_App_sahifasini_ochadi()
    {
        Assert.Equal(TelegramBotService.PanelUrl + "admin/hr/mening-topshiriqlarim", HrTasksService.MyTasksUrl(Roles.Staff));
        Assert.Equal(TelegramBotService.PanelUrl + "admin/hr/mening-topshiriqlarim", HrTasksService.MyTasksUrl(Roles.Admin));
        Assert.Equal(TelegramBotService.PanelUrl + "teacher/topshiriqlar", HrTasksService.MyTasksUrl(Roles.Teacher));
        Assert.Null(TelegramHrSender.Keyboard(null));
        var button = JsonDocument.Parse(JsonSerializer.Serialize(TelegramHrSender.Keyboard("https://x/admin")))
            .RootElement.GetProperty("inline_keyboard")[0][0];
        Assert.Equal(TelegramHrSender.OpenButtonText, button.GetProperty("text").GetString());
        // a Mini App opens only from a web_app button (a plain url would leave Telegram)
        Assert.Equal("https://x/admin", button.GetProperty("web_app").GetProperty("url").GetString());
    }

    [Fact]
    public void EF_nomlari_LMS_dagidek_snake_case()
    {
        using var db = TestDb.Sqlite();
        var task = db.Context.Model.FindEntityType(typeof(HrTask))!;
        Assert.Equal("hr_tasks", task.GetTableName());
        Assert.Equal("target_amount", task.FindProperty(nameof(HrTask.TargetAmount))!.GetColumnName());
        Assert.Equal("period_start", task.FindProperty(nameof(HrTask.PeriodStart))!.GetColumnName());
        Assert.Equal("pk_hr_tasks", task.FindPrimaryKey()!.GetName());
        Assert.Contains(task.GetForeignKeys(), fk => fk.GetConstraintName() == "fk_hr_tasks_hr_employees_employee_id");
        Assert.Contains(task.GetIndexes(), i => i.GetDatabaseName() == "uq_hr_task_period" && i.IsUnique);
        Assert.Contains(task.GetIndexes(), i => i.GetDatabaseName() == "ix_hr_tasks_template_id");
        var employee = db.Context.Model.FindEntityType(typeof(HrEmployee))!;
        Assert.Equal("hr_role", employee.FindProperty(nameof(HrEmployee.HrRole))!.GetColumnName());
        // LC: NO foreign key to Users — a deleted account leaves a FORMER employee row (hr-tasks.md §4)
        Assert.DoesNotContain(employee.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(AppUser));
        Assert.Contains(employee.GetIndexes(), ix => ix.IsUnique && ix.GetDatabaseName() == "uq_hr_employee_user");
        // the rest of the LC model keeps its PascalCase
        Assert.Equal("FullName", db.Context.Model.FindEntityType(typeof(AppUser))!.FindProperty(nameof(AppUser.FullName))!.GetColumnName());
    }

    [Fact]
    public void Yozish_TOLIQ_kalit_talab_qiladi_va_akkaunt_yaratish_Xodimlar_ruxsati_bilan()
    {
        var src = Controller("HrTasksController.cs");
        // every write goes through Run/RunNoContent, both start with EnsureCanWrite (LMS: writes need the bare key)
        Assert.Equal(2, Regex.Matches(src, @"try\s*\{\s*EnsureCanWrite\(\);").Count);
        Assert.Contains("PermissionRules.HasFullSection(actor.Permissions, HrTaskAccess.PermissionKey)", src);
        // a login-capable account only with the Xodimlar section's full right
        Assert.Contains("AdminPermAttribute.HasFullAccess(User, \"staff\")", src);
    }

    [Fact]
    public void Yuklamalar_uploads_tanigan_turlar_bilan_va_Program_shu_xaritani_ishlatadi()
    {
        Assert.True(HrFileTypes.IsServable(".jpg"));
        Assert.True(HrFileTypes.IsServable(".heic"));
        Assert.True(HrFileTypes.IsServable(".opus"));
        Assert.True(HrFileTypes.IsServable(".bin"));
        Assert.False(HrFileTypes.IsServable(".pages"));
        Assert.Contains("ContentTypeProvider = WunderkindLC.Application.HrTasks.HrFileTypes.Provider",
            Source("WunderkindLC.Server", "Program.cs"));
    }
}
