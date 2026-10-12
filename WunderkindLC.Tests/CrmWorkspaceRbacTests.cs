using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Reads Server sources — the test project does not reference <c>WunderkindLC.Server</c>
/// (same approach as <see cref="SensitiveReadPermTests"/> / <see cref="HomeEndpointsRbacTests"/>).</summary>
internal static class ServerSource
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

    public static string Read(params string[] relative)
    {
        var path = Path.Combine([RepoRoot, .. relative]);
        Assert.True(File.Exists(path), $"Fayl topilmadi: {path}");
        return File.ReadAllText(path);
    }

    public static string Controller(string file) => Read("WunderkindLC.Server", "Controllers", file);

    /// <summary>Source of one action: from its HTTP attribute to the next HTTP attribute (or end).</summary>
    public static string Action(string src, string httpAttribute)
    {
        var i = src.IndexOf(httpAttribute, StringComparison.Ordinal);
        Assert.True(i >= 0, $"{httpAttribute} topilmadi");
        var next = Regex.Match(src[(i + httpAttribute.Length)..], @"\[Http(Get|Post|Put|Patch|Delete)");
        return next.Success ? src.Substring(i, httpAttribute.Length + next.Index) : src[i..];
    }

    /// <summary>The attributes right before an action's signature (between its HTTP attribute and "public").</summary>
    public static string Attributes(string src, string httpAttribute)
    {
        var i = src.IndexOf(httpAttribute, StringComparison.Ordinal);
        Assert.True(i >= 0, $"{httpAttribute} topilmadi");
        var sig = src.IndexOf("public ", i, StringComparison.Ordinal);
        return src[i..sig];
    }
}

/// <summary>
/// RBAC of every new/changed CRM Phase 5 endpoint (source level) + the permission rule that turns a missing key
/// into 403 for a staff user (<see cref="PermissionRules"/>, the same function <c>AdminPermAttribute</c> calls).
/// </summary>
public class CrmWorkspaceRbacTests
{
    private static readonly string[] StaffWithoutTimetable = ["classes.list", "tasks.dashboard"];

    [Fact]
    public void Jadval_dated_va_conflicts_schedule_timetable_ostida()
    {
        var main = ServerSource.Controller("ScheduleController.cs");
        Assert.Contains("[AdminPerm(\"schedule.timetable\")]", main);
        Assert.Contains("public partial class ScheduleController", main);

        var dated = ServerSource.Controller("ScheduleController.Dated.cs");
        Assert.Contains("[HttpGet(\"dated\")]", dated);
        Assert.Contains("[HttpGet(\"conflicts\")]", dated);
        // Sinf darajasidagi darvozani bekor qiladigan metod atributi yoki anonim kirish YO'Q.
        Assert.DoesNotMatch(new Regex(@"^\s*\[AdminPerm", RegexOptions.Multiline), dated);
        Assert.DoesNotContain("AllowAnonymous", dated);

        // Kalitsiz xodim: GET odatda ochiq (bo'limlararo o'qish), lekin bo'lim/sahifa yo'q — HasSection false.
        Assert.False(PermissionRules.HasSection(StaffWithoutTimetable, "schedule.timetable"));
        Assert.True(PermissionRules.HasSection(["schedule"], "schedule.timetable"));
    }

    [Fact]
    public void Topshiriq_tasdiqlash_va_qaytarish_tasks_board_va_bergan_xodim()
    {
        var src = ServerSource.Controller("WorkTasksController.Approval.cs");
        foreach (var http in new[] { "[HttpPut(\"{id}/approve\")]", "[HttpPut(\"{id}/return\")]" })
        {
            Assert.Contains("[AdminPerm(\"tasks.board\")]", ServerSource.Attributes(src, http));
            var body = ServerSource.Action(src, http);
            Assert.Contains("WorkTaskApproval.IsApprover(t, Uid, IsAdminCaller)", body);
            Assert.Contains("Status403Forbidden", body);
            Assert.Contains("LockKey(id)", body);
        }
        Assert.DoesNotContain("AllowAnonymous", src);
        // Staff with only the dashboard page cannot write tasks → AdminPerm answers 403.
        Assert.False(PermissionRules.CanWrite(["tasks.dashboard"], "tasks.board", "PUT"));
        Assert.True(PermissionRules.CanWrite(["tasks"], "tasks.board", "PUT"));
    }

    [Fact]
    public void Bosh_sahifa_layout_faqat_panel_rollari_va_ozi()
    {
        var src = ServerSource.Controller("HomeLayoutController.cs");
        Assert.Matches(new Regex(@"\[Authorize\(Roles\s*=\s*""superadmin,admin,staff""\)\]"), src);
        Assert.DoesNotMatch(new Regex(@"Roles\s*=\s*""[^""]*(teacher|student|parent)"), src);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Marshrutda foydalanuvchi id'si YO'Q — faqat o'z qatori (Uid claim'dan).
        Assert.Contains("[Route(\"api/admin/home/layout\")]", src);
        Assert.DoesNotContain("{userId}", src);
        Assert.Contains("ClaimTypes.NameIdentifier", src);
    }

    [Fact]
    public void Guruh_toqnashuvi_409_va_faqat_superadmin_baribir_saqlaydi()
    {
        var src = ServerSource.Controller("ClassesController.cs");
        foreach (var http in new[] { "[HttpPost]", "[HttpPut(\"{id}\")]", "[HttpPost(\"{id}/unarchive\")]" })
        {
            var body = ServerSource.Action(src, http);
            Assert.Contains("roomConflict.FindAsync", body);
            Assert.Contains("ScheduleConflicts.Decide(conflicts.Count, force, forceReason, User.IsInRole(Roles.SuperAdmin))", body);
            Assert.Contains("return ScheduleConflict(verdict, conflicts)", body);
            Assert.Contains("ScheduleConflicts.ForcedAuditText", body);
            // Eski "200 + ogohlantirish" qaytmasin.
            Assert.DoesNotContain("Jadvalda vaqt konflikti bor", body);
        }
        // Tahrirda — faqat jadval maydoni o'zgarganda.
        Assert.Contains("ScheduleConflicts.ScheduleChanged(ScheduleConflicts.Of(cls), afterSlot)",
            ServerSource.Action(src, "[HttpPut(\"{id}\")]"));

        var answer = ServerSource.Controller("ClassesController.Schedule.cs");
        Assert.Contains("Status409Conflict", answer);
        Assert.Contains("canForce = verdict.CanForce", answer);
        Assert.Contains("roomConflict = true", answer);
    }

    [Fact]
    public void Dars_kochirishni_faqat_admin_jurnalida_superadmin_majburlaydi()
    {
        var admin = ServerSource.Action(ServerSource.Controller("JournalController.cs"), "[HttpPost(\"reschedule\")]");
        Assert.Contains("User.IsInRole(WunderkindLC.Domain.Roles.SuperAdmin)", admin);
        Assert.Contains("forceConflict: decision.Verdict == ScheduleConflicts.Verdict.Forced", admin);
        Assert.Contains("catch (ScheduleConflictException", admin);
        Assert.Contains("Status409Conflict", admin);

        // O'qituvchi yo'li hech qachon majburlamaydi — so'rovdagi Force e'tiborsiz.
        var teacher = ServerSource.Action(ServerSource.Controller("TeacherPortalController.cs"), "[HttpPost(\"journal/reschedule\")]");
        Assert.DoesNotContain("forceConflict", teacher);
        Assert.DoesNotContain("req.Force", teacher);
    }

    [Fact]
    public void Xona_qavati_tahrirda_yuborilmasa_tegilmaydi()
    {
        var src = ServerSource.Controller("RoomsController.cs");
        Assert.Contains("RoomFloors.Resolve(room.Floor, req.Floor, req.ClearFloor)", src);
        Assert.Equal(2, Regex.Matches(src, @"RoomFloors\.IsValid\(req\.Floor\)").Count);   // create + update
        Assert.Contains("r.Floor", src);   // DTO + snapshot
    }

    [Fact]
    public void Bot_tasdiqlash_tarmogi_va_bajardim_tasdiq_sorovi()
    {
        var bot = ServerSource.Read("WunderkindLC.Application", "Services", "TelegramBotService.cs");
        var done = bot.IndexOf("data.StartsWith(WorkTaskTelegram.CallbackDone", StringComparison.Ordinal);
        var ok = bot.IndexOf("data.StartsWith(WorkTaskTelegram.CallbackApprove", StringComparison.Ordinal);
        Assert.True(done > 0 && ok > done, "wtok: tarmog'i wtdone: dan keyin turishi kerak");
        Assert.Contains("WorkTaskFlow.ApproveFromChatAsync(db, telegram, chatId", bot);
        Assert.Contains("WorkTaskFlow.OnCompletedAsync(db, t, t.AssigneeId, telegram, ct)", bot);
        Assert.Equal("wtok:", WorkTaskTelegram.CallbackApprove);
    }

    [Fact]
    public void Panel_kochirishi_tasdiqlashni_bot_bilan_bir_joydan_chaqiradi()
    {
        var helpers = ServerSource.Controller("WorkTasksController.Helpers.cs");
        Assert.Contains("WorkTaskFlow.OnCompletedAsync(db, t, Uid, telegram)", helpers);
        Assert.Contains("WorkTaskFlow.OnReopened(t)", helpers);
        // Yangi doskalarga «Tekshiruvda» endi ekilmaydi (tasdiqlash bosqichi bor).
        var seed = helpers[helpers.IndexOf("DefaultColumns(string boardId) =>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Tekshiruvda\"", seed[..seed.IndexOf("];", StringComparison.Ordinal)]);
    }
}
