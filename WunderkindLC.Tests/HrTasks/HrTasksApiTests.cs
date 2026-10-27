using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using Xunit;
using static WunderkindLC.Tests.HrTasks.HrTasksKit;

namespace WunderkindLC.Tests.HrTasks;

// ===========================================================================
//  HR topshiriqlari — the WunderkindLMS HTTP suite (SchoolLms.Tests/HrTasks/HrTasksApiTests.cs) one layer
//  down: LC tests do not host the server, so every assertion runs against HrTasksService (the controllers
//  are `Run(service…)` wrappers; their gates are checked at source level in HrTasksRbacTests). Covered:
//  manager scope (404 outside the subtree), director-only writes, cycle rejection, employee creation
//  (staff user + one-time password), the task lifecycle of "Mening topshiriqlarim" (proof required, KPI
//  auto-done, reopen), upload limits, batch limits, templates, immediate Telegram messages, invite.
// ===========================================================================

public class HrTasksApiTests
{
    private static async Task<HrTasksException> FailsAsync(Func<Task> call) => await Assert.ThrowsAsync<HrTasksException>(call);

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static HrUpload Upload(byte[] bytes, string name, string type) => new(bytes, name, type);

    // =====================================================================
    //  1. Who reaches the panel / "Mening topshiriqlarim"
    // =====================================================================

    [Fact]
    public async Task Me_hr_qatorisiz_yoki_nofaol_403_hr_not_employee()
    {
        using var kit = new HrTasksKit();
        foreach (var role in new[] { Roles.Admin, Roles.Teacher, Roles.Staff })
        {
            var noRow = await kit.PersonAsync(role, hrRole: null, permissions: [HrTaskAccess.PermissionKey]);
            var ex = await FailsAsync(() => kit.Service().MeAsync(noRow.User.Id));
            Assert.Equal(403, ex.StatusCode);
            Assert.Equal("hr_not_employee", ex.Code);
        }
        // a fired employee (inactive row) is not an employee either
        var fired = await kit.PersonAsync(Roles.Staff, active: false);
        Assert.Equal("hr_not_employee", (await FailsAsync(() => kit.Service().MeAsync(fired.User.Id))).Code);
    }

    [Fact]
    public async Task Admin_superadmin_va_direktor_hammasini_oqiydi()
    {
        using var kit = new HrTasksKit();
        var director = await kit.PersonAsync(Roles.Staff, HrRoles.Director, [HrTaskAccess.PermissionKey]);
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var super = await kit.PersonAsync(Roles.SuperAdmin, hrRole: null);
        foreach (var user in new[] { admin.User, super.User, director.User })
        {
            var s = kit.Service();
            var panel = await s.PanelAsync(Actor(user));
            Assert.True(panel.IsDirector);
            Assert.False(panel.Scope.IsLimited);
            await s.UserCandidatesAsync("Xodim");
            await s.PositionsAsync(null, null, new HrPaging());
            await s.EmployeesAsync(panel, new HrEmployeeFilter(), new HrPaging(WithTotal: true));
            await s.TreeAsync(panel, new HrEmployeeFilter());
            await s.TemplatesAsync(panel, new HrTemplateFilter(), new HrPaging());
            await s.TasksAsync(panel, new HrTaskFilter(Statuses: [HrTaskStatuses.Open, HrTaskStatuses.Returned], Sort: "-created_at"), new HrPaging());
            await s.SummaryAsync(panel.Scope, null, null, null);
            var report = await s.EmployeeReportAsync(panel.Scope, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
            Assert.NotEmpty(HrTasksService.ReportCsv(report));
            Assert.NotEmpty(ExcelExport.Build("Hisobot", HrTasksService.ReportHeaders, HrTasksService.ReportRows(report)));
            await s.SettingsAsync(panel);
            await s.DashboardAsync(panel);
        }
        var access = kit.Service().Access(await kit.PanelAsync(director.User));
        Assert.True(access.IsDirector);
        Assert.Equal("all", access.Scope);
        Assert.True(access.CanManage);
        // LC: a narrower token reads but does not "manage"
        var editor = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, ["hrTasks:edit"]);
        Assert.False(kit.Service().Access(await kit.PanelAsync(editor.User)).CanManage);
    }

    [Fact]
    public async Task Sozlamalar_va_lavozim_yozish_faqat_direktorga()
    {
        using var kit = new HrTasksKit();
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey]);
        var managerPanel = await kit.PanelAsync(manager.User);
        Assert.Equal(403, (await FailsAsync(() => kit.Service().SettingsAsync(managerPanel))).StatusCode);
        var post = await FailsAsync(() => kit.Service().CreatePositionAsync(managerPanel, new HrPositionCreateInput("Manager lavozimi")));
        Assert.Equal(403, post.StatusCode);
        Assert.Equal("forbidden", post.Code);
        await kit.Service().PositionsAsync(null, null, new HrPaging()); // reads are open

        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var adminPanel = await kit.PanelAsync(admin.User);
        var name = "Buxgalter " + Guid.NewGuid().ToString("N")[..6];
        var position = await kit.Service().CreatePositionAsync(adminPanel, new HrPositionCreateInput($"  {name}  "));
        Assert.Equal(name, position.Name);
        Assert.Equal(409, (await FailsAsync(() => kit.Service().CreatePositionAsync(adminPanel, new HrPositionCreateInput(name)))).StatusCode);

        // settings round-trip
        var before = await kit.Service().SettingsAsync(adminPanel);
        Assert.Equal("19:00", before.DigestTime);
        var saved = await kit.Service().UpdateSettingsAsync(adminPanel, new HrSettingsInput(
            "Asia/Tashkent", "19:30", [1, 2, 3, 4, 5], true, "07:30", "00:10", "23:50", true, [30, 120], false));
        Assert.Equal("19:30", saved.DigestTime);
        Assert.Equal([120, 30], saved.ReminderOffsets);
        var bad = await FailsAsync(() => kit.Service().UpdateSettingsAsync(adminPanel, new HrSettingsInput(
            null, "19:00", null, null, "07:30", "00:10", "23:50", null, [1], null)));
        Assert.Equal(422, bad.StatusCode);
    }

    // =====================================================================
    //  2. Scope — a manager sees self + the whole subtree; outside = 404
    // =====================================================================

    [Fact]
    public async Task Manager_faqat_oz_shoxini_koradi_tashqarisi_404()
    {
        using var kit = new HrTasksKit();
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], name: "Manager Shox");
        var sub = await kit.PersonAsync(Roles.Staff, name: "Bo'ysunuvchi Bir");
        var subSub = await kit.PersonAsync(Roles.Teacher, name: "Ichki Ikki");
        var outsider = await kit.PersonAsync(Roles.Staff, name: "Begona Uch");
        await kit.ReportsToAsync(sub.Employee!.Id, manager.Employee!.Id);
        await kit.ReportsToAsync(subSub.Employee!.Id, sub.Employee.Id);

        var due = DateTimeOffset.UtcNow.AddDays(2);
        var inside = await kit.TaskAsync(subSub.Employee.Id, due);
        var outside = await kit.TaskAsync(outsider.Employee!.Id, due);

        var panel = await kit.PanelAsync(manager.User);
        var list = await kit.Service().EmployeesAsync(panel, new HrEmployeeFilter(), new HrPaging(200));
        var ids = list.Items.Select(e => e.Id).ToList();
        Assert.Contains(manager.Employee.Id, ids);
        Assert.Contains(sub.Employee.Id, ids);
        Assert.Contains(subSub.Employee.Id, ids);
        Assert.DoesNotContain(outsider.Employee.Id, ids);

        await kit.Service().EmployeeAsync(panel, subSub.Employee.Id);
        await kit.Service().TaskAsync(panel, inside.Id);
        foreach (var call in new Func<Task>[]
                 {
                     () => kit.Service().EmployeeAsync(panel, outsider.Employee.Id),
                     () => kit.Service().TaskAsync(panel, outside.Id),
                     () => kit.Service().UpdateEmployeeAsync(panel, outsider.Employee.Id, Json(new { note = "x" })),
                     () => kit.Service().DeactivateEmployeeAsync(panel, outsider.Employee.Id),
                     () => kit.Service().CancelTaskAsync(panel, outside.Id, null),
                     () => kit.Service().CreateTaskAsync(panel, new HrTaskCreateInput(outsider.Employee.Id, "Begonaga", null, due.ToString("o"), null, null, null, null)),
                 })
            Assert.Equal(404, (await FailsAsync(call)).StatusCode);

        var tasks = await kit.Service().TasksAsync(panel, new HrTaskFilter(), new HrPaging(200));
        var taskIds = tasks.Items.Select(t => t.Id).ToList();
        Assert.Contains(inside.Id, taskIds);
        Assert.DoesNotContain(outside.Id, taskIds);

        // the whole subtree in the tree view, the manager's own node as the root
        var tree = await kit.Service().TreeAsync(panel, new HrEmployeeFilter());
        var root = Assert.Single(tree.Roots);
        Assert.Equal(manager.Employee.Id, root.Id);
        Assert.Equal(subSub.Employee.Id, root.Children[0].Children[0].Id);

        // a manager may create an employee — it lands under the manager; cannot grant a scope role
        // the factory adds the account to kit.Db — the service saves on that same context (one request)
        var created = await kit.Service(db: kit.Db).CreateEmployeeAsync(panel,
            HrPeopleSyncTests.Input(fullName: "Yangi Xodim", phone: NextPhone()), NewStaffUserFor(kit));
        Assert.Equal([manager.Employee.Id], created.ManagerIds);
        await kit.Service().EmployeeAsync(await kit.PanelAsync(manager.User), created.Id);
        var escalate = await FailsAsync(() => kit.Service().UpdateEmployeeAsync(panel, sub.Employee.Id, Json(new { hrRole = HrRoles.Director })));
        Assert.Equal(403, escalate.StatusCode);
        Assert.Equal("hr_panel_access_forbidden", escalate.Code);

        // a manager without an HR row sees nobody (fail-closed), still no error
        var rowless = await kit.PersonAsync(Roles.Staff, hrRole: null, permissions: [HrTaskAccess.PermissionKey]);
        Assert.Empty((await kit.Service().EmployeesAsync(await kit.PanelAsync(rowless.User), new HrEmployeeFilter(), new HrPaging())).Items);
    }

    [Fact]
    public async Task Royxat_view_oz_topshiriqlarimni_xodimlarnikidan_ajratadi()
    {
        using var kit = new HrTasksKit();
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], name: "Rahbar View");
        var sub = await kit.PersonAsync(Roles.Staff, name: "Xodim View");
        await kit.ReportsToAsync(sub.Employee!.Id, manager.Employee!.Id);

        var due = DateTimeOffset.UtcNow.AddDays(2);
        var mine = await kit.TaskAsync(manager.Employee.Id, due, title: "Menga berilgan");
        var selfAdded = await kit.TaskAsync(sub.Employee.Id, due, source: HrTaskSources.Self, title: "Xodim o'zi qo'shgan");
        var panel = await kit.PanelAsync(manager.User);
        var given = await kit.Service().CreateTaskAsync(panel, new HrTaskCreateInput(sub.Employee.Id, "Rahbar bergan", null, due.ToString("o"), null, null, null, null));

        async Task<List<string>> IdsAsync(string? view) =>
            (await kit.Service().TasksAsync(panel, new HrTaskFilter(View: view), new HrPaging(200))).Items.Select(t => t.Id).ToList();

        // no view — everything in scope, as before
        var all = await IdsAsync(null);
        Assert.Contains(mine.Id, all);
        Assert.Contains(selfAdded.Id, all);
        Assert.Contains(given.Id, all);

        var staff = await IdsAsync(HrTaskViews.Staff);
        Assert.DoesNotContain(mine.Id, staff);
        Assert.Contains(selfAdded.Id, staff);
        Assert.Contains(given.Id, staff);

        Assert.Equal([given.Id], await IdsAsync(HrTaskViews.Given));
    }

    [Fact]
    public async Task Rahbarlikda_halqa_409()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var a = await kit.PersonAsync();
        var b = await kit.PersonAsync();
        var c = await kit.PersonAsync();
        await kit.ReportsToAsync(b.Employee!.Id, a.Employee!.Id); // b → a
        await kit.ReportsToAsync(c.Employee!.Id, b.Employee.Id);  // c → b

        var cycle = await FailsAsync(() => kit.Service().SetManagersAsync(panel, a.Employee.Id, [c.Employee.Id]));
        Assert.Equal(409, cycle.StatusCode);
        Assert.Equal("hr_manager_cycle", cycle.Code);
        Assert.Equal("hr_manager_cycle", (await FailsAsync(() => kit.Service().SetManagersAsync(panel, a.Employee.Id, [a.Employee.Id]))).Code);
        var unknown = await FailsAsync(() => kit.Service().SetManagersAsync(panel, a.Employee.Id, [Guid.NewGuid().ToString()]));
        Assert.Equal(404, unknown.StatusCode);
        Assert.Equal("hr_manager_not_found", unknown.Code);

        // a valid replacement: c now reports to a directly (the list is replaced)
        var ok = await kit.Service().SetManagersAsync(panel, c.Employee.Id, [a.Employee.Id]);
        Assert.Equal([a.Employee.Id], ok.ManagerIds);
    }

    // =====================================================================
    //  3. Employees: a staff user created from name + phone
    // =====================================================================

    /// <summary>What HrTasksController.NewStaffUser does (minus the audit row) — the LC account factory.</summary>
    private static HrNewUserFactory NewStaffUserFor(HrTasksKit kit) => (fullName, login, displayPhone) =>
    {
        var user = AccountFactory.CreateAccountFor(kit.Db, Roles.Staff, fullName, out var password);
        user.Email = login;
        user.Phone = PhoneUtil.Normalize(displayPhone);
        return (user, password);
    };

    [Fact]
    public async Task Ism_va_telefon_bilan_staff_foydalanuvchi_yaratiladi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        // the factory adds the user to kit.Db — the service must run on the same context (one request)
        var s = kit.Service(db: kit.Db);
        var panel = await s.PanelAsync(Actor(admin.User));
        var phone = NextPhone();
        var body = await s.CreateEmployeeAsync(panel,
            HrPeopleSyncTests.Input(fullName: "Hodimov  Hodim", phone: phone[4..], branch: "school", hrRole: "manager"), NewStaffUserFor(kit));
        Assert.Equal("Hodimov Hodim", body.FullName);
        Assert.Equal(phone, body.Phone);
        Assert.Equal(Roles.Staff, body.Role);
        Assert.Equal(HrRoles.Manager, body.HrRole);
        Assert.Equal(phone.TrimStart('+'), body.Credentials!.Login);
        Assert.Equal(8, body.Credentials.Password.Length);
        Assert.Null(body.PanelRole); // no hrTasks key — the permission is granted elsewhere
        Assert.Null(body.TelegramUserId);

        // the new account logs in with login = phone digits
        await using (var read = kit.NewContext())
        {
            var user = await read.Users.SingleAsync(u => u.Email == phone.TrimStart('+'));
            Assert.True(PasswordHasher.Verify(body.Credentials.Password, user.PasswordHash));
            Assert.Empty(user.Permissions);
        }

        // the same phone again → 409; an existing user → no credentials
        Assert.Equal(409, (await FailsAsync(() => s.CreateEmployeeAsync(panel,
            HrPeopleSyncTests.Input(fullName: "Boshqa", phone: phone), NewStaffUserFor(kit)))).StatusCode);
        var teacher = await kit.PersonAsync(Roles.Teacher, hrRole: null, name: "Tanlangan O'qituvchi");
        var picked = await s.CreateEmployeeAsync(panel, HrPeopleSyncTests.Input(userId: teacher.User.Id, phone: NextPhone()), HrPeopleSyncTests.NoNewUser);
        Assert.Equal("Tanlangan O'qituvchi", picked.FullName);
        Assert.Equal(Roles.Teacher, picked.Role);
        Assert.Null(picked.Credentials);
        Assert.Equal(409, (await FailsAsync(() => s.CreateEmployeeAsync(panel,
            HrPeopleSyncTests.Input(userId: teacher.User.Id, phone: NextPhone()), HrPeopleSyncTests.NoNewUser))).StatusCode);

        // candidates list marks HR rows
        var candidates = await kit.Service().UserCandidatesAsync("Tanlangan");
        Assert.Equal(picked.Id, candidates.Single(u => u.Id == teacher.User.Id).EmployeeId);
    }

    [Fact]
    public async Task Ishdan_boshatish_ochiq_topshiriqlarni_bekor_qiladi_akkauntga_tegmaydi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync();
        var open = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1));
        var done = await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(-1), HrTaskStatuses.Done,
            completedAt: DateTimeOffset.UtcNow.AddDays(-1), comment: "ok");

        Assert.False((await kit.Service().DeactivateEmployeeAsync(panel, e.Employee.Id)).IsActive);
        await using (var read = kit.NewContext())
        {
            Assert.Equal(HrTaskStatuses.Cancelled, (await read.HrTasks.SingleAsync(t => t.Id == open.Id)).Status);
            Assert.Equal(HrTaskStatuses.Done, (await read.HrTasks.SingleAsync(t => t.Id == done.Id)).Status);
            Assert.True(await read.Users.AnyAsync(u => u.Id == e.User.Id)); // the account stays
        }
        // a fired employee gets no new task
        var task = await FailsAsync(() => kit.Service().CreateTaskAsync(panel,
            new HrTaskCreateInput(e.Employee.Id, "Yangi", null, DateTimeOffset.UtcNow.AddDays(1).ToString("o"), null, null, null, null)));
        Assert.Equal(409, task.StatusCode);
        Assert.Equal("hr_employee_inactive", task.Code);

        // LC: deleting the account still goes through — the row stays as a FORMER employee with its history
        await using var db = kit.NewContext();
        var release = await HrTasksService.ReleaseEmployeeAsync(db, e.User.Id, DateTimeOffset.UtcNow);
        Assert.True(release.KeptHistory);
        Assert.Equal(0, release.CancelledTasks); // already cancelled by the deactivation
    }

    // =====================================================================
    //  4. Lifecycle through "Mening topshiriqlarim" (/api/hr/me)
    // =====================================================================

    [Fact]
    public async Task Hayot_sikli_isbot_talabi_qaytarish_qayta_ochish()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync(Roles.Teacher, name: "Sikl Xodimi");
        var due = DateTimeOffset.UtcNow.AddHours(5);
        var task = await kit.Service().CreateTaskAsync(panel, new HrTaskCreateInput(e.Employee!.Id, "Hisobot tayyorlash", "Oylik", due.ToString("o"), null, null, null, null));
        var id = task.Id;
        Assert.Equal("director", task.Source);
        Assert.True(task.NeedsAcceptance);
        Assert.Equal("keep_open", task.OverduePolicy);

        // it is in its day's list, not overdue
        var meRow = await kit.Service().MeAsync(e.User.Id);
        Assert.Equal(e.Employee.Id, (await kit.Service().ProfileAsync(meRow)).EmployeeId);
        var day = await kit.Service().DayAsync(meRow, HrValues.LocalDate(due, Tz));
        var item = day.Items.Single(t => t.Id == id);
        Assert.False(item.IsOverdue);
        Assert.Null(item.CurrentAmount); // check task

        // "ko'rdim"
        Assert.False((await kit.Service().AcceptAsync(meRow, id)).NeedsAcceptance);

        // no proof, no comment → 409 proof_required (the /me controller drops the hr_ prefix)
        var noProof = await FailsAsync(() => kit.Service().CompleteAsync(meRow, id, new HrCompleteInput("  ", [])));
        Assert.Equal(409, noProof.StatusCode);
        Assert.Equal("hr_proof_required", noProof.Code);
        Assert.Equal("done", (await kit.Service().CompleteAsync(meRow, id, new HrCompleteInput("Tayyor", null))).Status);

        // reopen — same local day, the digest has not run → open again
        Assert.Equal("open", (await kit.Service().ReopenAsync(meRow, id)).Status);
        await kit.Service().CompleteAsync(meRow, id, new HrCompleteInput("Yana tayyor", null));

        // the director returns it; the employee completes again
        Assert.Equal("returned", (await kit.Service().ReturnTaskAsync(panel, id, "Rasm yo'q")).Status);
        Assert.Equal(422, (await FailsAsync(() => kit.Service().ReturnTaskAsync(panel, id, ""))).StatusCode);
        var again = await kit.Service().CompleteAsync(meRow, id, new HrCompleteInput("Rasm qo'shdim", null));
        Assert.Equal("done", again.Status);
        Assert.Equal("Rasm yo'q", again.DirectorComment);

        // a done task cannot be cancelled
        var cancel = await FailsAsync(() => kit.Service().CancelTaskAsync(panel, id, null));
        Assert.Equal(409, cancel.StatusCode);
        Assert.Equal("hr_task_not_editable", cancel.Code);

        // detail (panel) — flat task + proofs/progress/attachments
        var detail = await kit.Service().TaskAsync(panel, id);
        var json = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var key in new[] { "id", "employeeId", "periodStart", "dueAt", "isLate", "proofs", "progress", "attachments",
                     "employeeName", "proofCount", "acceptedAt", "needsAcceptance", "currentAmount" })
            Assert.True(json.TryGetProperty(key, out _), key);
        Assert.Equal("Sikl Xodimi", detail.EmployeeName);

        // another employee's task → 403 task_not_owned
        var stranger = await kit.PersonAsync();
        var strangerRow = await kit.Service().MeAsync(stranger.User.Id);
        var foreign = await FailsAsync(() => kit.Service().CompleteAsync(strangerRow, id, new HrCompleteInput("meniki", null)));
        Assert.Equal(403, foreign.StatusCode);
        Assert.Equal("hr_task_not_owned", foreign.Code);
    }

    [Fact]
    public async Task KPI_qadami_maqsadga_yetganda_avtomatik_bajariladi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var e = await kit.PersonAsync();
        // a fixed clock: "Natijam" counts tasks due up to the end of TODAY in Tashkent
        var now = At(2040, 6, 10, 9);
        var created = await kit.Service(now).CreateTaskAsync(await kit.PanelAsync(admin.User),
            new HrTaskCreateInput(e.Employee!.Id, "Leadlar", null, now.AddMinutes(30).ToString("o"), "numeric", 10, "lead", null));
        var me = await kit.Service().MeAsync(e.User.Id);
        var step = await kit.Service(now).AddProgressAsync(me, created.Id, new HrProgressInput(4.50m, "ertalab"));
        Assert.Equal("open", step.Status);
        Assert.Equal(4.5m, step.CurrentAmount);
        var zero = await FailsAsync(() => kit.Service(now).AddProgressAsync(me, created.Id, new HrProgressInput(0, null)));
        Assert.Equal(422, zero.StatusCode);
        Assert.Equal("hr_invalid_amount", zero.Code);
        var last = await kit.Service(now).AddProgressAsync(me, created.Id, new HrProgressInput(5.5m, null));
        Assert.Equal("done", last.Status);
        Assert.Equal(10m, last.CurrentAmount);

        Assert.Equal(2, (await kit.Service().TaskAsync(await kit.PanelAsync(admin.User), created.Id)).Progress.Count);
        var summary = await kit.Service(now).MySummaryAsync(me);
        Assert.Contains(summary.Kpi, k => k.TaskId == created.Id);
    }

    [Fact]
    public async Task Isbot_yuklash_chegaralari()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var e = await kit.PersonAsync();
        var task = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1));
        var me = await kit.Service().MeAsync(e.User.Id);

        var html = await FailsAsync(() => kit.Service().UploadProofAsync(me, task.Id, Upload([1, 2, 3], "x.html", "text/html")));
        Assert.Equal(422, html.StatusCode);
        Assert.Equal("hr_unsupported_proof_type", html.Code);
        Assert.Equal("hr_unsupported_proof_type",
            (await FailsAsync(() => kit.Service().UploadProofAsync(me, task.Id, Upload([1, 2, 3], "rasm.svg", "image/png")))).Code);
        var big = await FailsAsync(() => kit.Service().UploadProofAsync(me, task.Id, Upload(new byte[HrLimits.MaxProofBytes + 1], "big.pdf", "application/pdf")));
        Assert.Equal(422, big.StatusCode);
        Assert.Equal("hr_proof_too_large", big.Code);

        // audio (a voice note on completion) and any document are fine
        var voice = await kit.Service().UploadProofAsync(me, task.Id, Upload([1, 2, 3], "ovoz.webm", "audio/webm"));
        Assert.StartsWith("/uploads/hr/proofs/", voice.Url);
        foreach (var name in new[] { "a.zip", "b.xlsx", "c.jpg", "d.pdf" })
            await kit.Service().UploadProofAsync(me, task.Id, Upload([9], name, "application/octet-stream"));
        var sixth = await FailsAsync(() => kit.Service().UploadProofAsync(me, task.Id, Upload([9], "f.png", "image/png")));
        Assert.Equal(409, sixth.StatusCode);
        Assert.Equal("hr_proof_limit_exceeded", sixth.Code);

        // a file alone is enough proof
        var done = await kit.Service().CompleteAsync(me, task.Id, new HrCompleteInput(null, []));
        Assert.Equal("done", done.Status);
        Assert.Equal(5, done.Proofs.Count);
        var detail = await kit.Service().TaskAsync(await kit.PanelAsync(admin.User), task.Id);
        var voiceProof = detail.Proofs.Single(p => p.OriginalName == "ovoz.webm");
        Assert.Equal("file", voiceProof.Kind);
        Assert.Equal("audio/webm", voiceProof.ContentType);
        Assert.Equal(5, detail.ProofCount);

        // someone else's task
        var stranger = await kit.PersonAsync();
        var strangerRow = await kit.Service().MeAsync(stranger.User.Id);
        Assert.Equal(403, (await FailsAsync(() => kit.Service().UploadProofAsync(strangerRow, task.Id, Upload([1], "a.png", "image/png")))).StatusCode);
    }

    // =====================================================================
    //  5. Batch limits + partial success, templates
    // =====================================================================

    [Fact]
    public async Task Batch_chegaralari_va_qisman_muvaffaqiyat()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var due = DateTimeOffset.UtcNow.AddDays(2).ToString("o");
        List<HrTaskBatchItemInput> Items(int n) => [.. Enumerable.Range(1, n).Select(i => new HrTaskBatchItemInput($"Band {i}", due, null, null, null, null, null))];
        List<string> Ids(int n) => [.. Enumerable.Range(0, n).Select(_ => Guid.NewGuid().ToString())];

        foreach (var (employees, items) in new[] { (101, 1), (1, 21), (30, 17) })
        {
            var tooBig = await FailsAsync(() => kit.Service().CreateTaskBatchAsync(panel, new HrTaskBatchInput(Ids(employees), Items(items))));
            Assert.Equal(422, tooBig.StatusCode);
            Assert.Equal("hr_batch_too_large", tooBig.Code);
            var rules = Enumerable.Range(1, items)
                .Select(i => new HrTemplateBatchItemInput($"Qoida {i}", null, "daily", [1], null, null, null, null, null, null, null)).ToList();
            Assert.Equal("hr_batch_too_large",
                (await FailsAsync(() => kit.Service().CreateTemplateBatchAsync(panel, new HrTemplateBatchInput(Ids(employees), rules)))).Code);
        }

        var a = await kit.PersonAsync();
        var b = await kit.PersonAsync();
        var fired = await kit.PersonAsync(active: false);
        var unknown = Guid.NewGuid().ToString();
        var result = await kit.Service().CreateTaskBatchAsync(panel,
            new HrTaskBatchInput([a.Employee!.Id, b.Employee!.Id, fired.Employee!.Id, unknown], Items(3)));
        Assert.Equal(6, result.Created);
        Assert.Equal(6, result.TaskIds!.Count);
        var skipped = result.Skipped.ToDictionary(s => s.EmployeeId, s => s.Reason);
        Assert.Equal("inactive", skipped[fired.Employee.Id]);
        Assert.Equal("not_found", skipped[unknown]);
    }

    [Fact]
    public async Task Shablon_yaratish_bugungi_nusxa_va_ochirishda_tarix_qoladi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync();
        HrTemplateCreateInput Rule(string title, List<int>? weekdays, string? dueTime = null, string? kind = null) =>
            new(title, null, "employee", e.Employee!.Id, null, "daily", weekdays, null, dueTime, kind, null, null, null);

        var invalid = await FailsAsync(() => kit.Service().CreateTemplateAsync(panel, Rule("Kunlik", [])));
        Assert.Equal(422, invalid.StatusCode);
        Assert.Equal("hr_invalid_recurrence", invalid.Code);
        Assert.Equal("hr_invalid_amount",
            (await FailsAsync(() => kit.Service().CreateTemplateAsync(panel, Rule("KPI", [1, 2, 3, 4, 5, 6, 7], kind: "numeric")))).Code);

        var template = await kit.Service().CreateTemplateAsync(panel, Rule("Har kuni hisobot", [7, 1, 2, 3, 4, 5, 6, 1], "23:58"));
        Assert.Equal("23:58", template.DueTime);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], template.Weekdays);
        Assert.Equal("keep_open", template.OverduePolicy);

        // today's instance was generated at once (unless 23:58 local already passed — then none)
        var generated = (await kit.Service().TasksAsync(panel, new HrTaskFilter(EmployeeId: e.Employee!.Id), new HrPaging(50)))
            .Items.Where(t => t.TemplateId == template.Id).ToList();
        Assert.True(generated.Count <= 1);
        Assert.All(generated, t => Assert.Equal("template", t.Source));

        Assert.False((await kit.Service().ToggleTemplateAsync(panel, template.Id, null)).IsActive);
        var patched = await kit.Service().UpdateTemplateAsync(panel, template.Id, Json(new { recurrence = "monthly", daysOfMonth = new[] { 31 } }));
        Assert.Empty(patched.Weekdays);
        Assert.Equal([31], patched.DaysOfMonth);

        // a history task of the template survives its deletion with templateId = null
        var history = await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(-3), HrTaskStatuses.Done,
            source: HrTaskSources.Template, completedAt: DateTimeOffset.UtcNow.AddDays(-3), comment: "ok");
        history.TemplateId = template.Id;
        await kit.Db.SaveChangesAsync();
        await kit.Service().DeleteTemplateAsync(panel, template.Id);
        await using (var read = kit.NewContext())
            Assert.Null((await read.HrTasks.SingleAsync(t => t.Id == history.Id)).TemplateId);
        Assert.Equal(404, (await FailsAsync(() => kit.Service().DeleteTemplateAsync(panel, template.Id))).StatusCode);
    }

    // =====================================================================
    //  6. Telegram — immediate messages (fake sender, never the network)
    // =====================================================================

    [Fact]
    public async Task Yangi_va_qaytarilgan_topshiriq_xodimga_darhol_boradi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync(telegram: true);
        var chat = e.Chat!.Value;

        var created = await kit.Service().CreateTaskAsync(panel,
            new HrTaskCreateInput(e.Employee!.Id, "Shartnoma <tayyorlash>", "Ota-ona & o'quvchi bilan", "2031-03-10T18:00:00", null, null, null, null));
        var message = Assert.Single(kit.Sender.To(chat));
        // LC (2026-10-27): the FULL task — title, description, due, who gave it — and «✅ Qabul qildim» under it
        Assert.StartsWith("🆕 <b>Sizga yangi topshiriq</b>\n\n<b>Shartnoma &lt;tayyorlash&gt;</b>\n\nOta-ona &amp; o'quvchi bilan\n", message);
        Assert.Contains("⏰ Muddat: 10.03.2031 18:00", message);
        Assert.Contains("👤 Berdi: " + admin.User.FullName, message);
        Assert.Equal((chat, created.Id), Assert.Single(kit.Sender.TaskButtons));
        // LC: the message opens the staff member's own tasks in the panel
        Assert.Equal(TelegramBotService.PanelUrl + HrTasksService.MyTasksPanelPath, kit.Sender.Messages.Single().Url);

        var row = await kit.Db.HrTasks.SingleAsync(x => x.Id == created.Id);
        row.Status = HrTaskStatuses.Done;
        row.CompletedAt = DateTimeOffset.UtcNow;
        await kit.Db.SaveChangesAsync();
        await kit.Service().ReturnTaskAsync(panel, created.Id, "Imzo yo'q");
        Assert.Equal(2, kit.Sender.To(chat).Count);
        Assert.StartsWith("↩️ <b>Topshiriq qaytarildi</b>", kit.Sender.To(chat)[1]);

        // LC: a SMALL batch (≤ MaxFullTaskMessages) → each task in full, each with its own «Qabul qildim»
        await kit.Service().CreateTaskBatchAsync(panel, new HrTaskBatchInput([e.Employee.Id],
        [
            new HrTaskBatchItemInput("Bir", "2031-03-11T10:00:00", null, null, null, null, null),
            new HrTaskBatchItemInput("Ikki", "2031-03-11T10:00:00", null, null, null, null, null),
        ]));
        Assert.Equal(4, kit.Sender.To(chat).Count);
        Assert.Contains("<b>Bir</b>", kit.Sender.To(chat)[2]);
        Assert.Contains("<b>Ikki</b>", kit.Sender.To(chat)[3]);
        Assert.Equal(3, kit.Sender.TaskButtons.Count);

        // a LARGE batch → ONE summary message per employee (LMS)
        await kit.Service().CreateTaskBatchAsync(panel, new HrTaskBatchInput([e.Employee.Id],
        [
            new HrTaskBatchItemInput("Bir", "2031-03-11T10:00:00", null, null, null, null, null),
            new HrTaskBatchItemInput("Ikki", "2031-03-11T10:00:00", null, null, null, null, null),
            new HrTaskBatchItemInput("Uch", "2031-03-11T10:00:00", null, null, null, null, null),
            new HrTaskBatchItemInput("To'rt", "2031-03-11T10:00:00", null, null, null, null, null),
        ]));
        Assert.Equal(5, kit.Sender.To(chat).Count);
        Assert.StartsWith("🆕 <b>Sizga 4 ta yangi topshiriq berildi</b>\n\n1. Bir\n2. Ikki\n3. Uch\n4. To'rt", kit.Sender.To(chat)[4]);

        // an unlinked employee — nothing is sent, the request still succeeds
        var unlinked = await kit.PersonAsync();
        var before = kit.Sender.Messages.Count;
        await kit.Service().CreateTaskAsync(panel, new HrTaskCreateInput(unlinked.Employee!.Id, "Jim", null, "2031-03-10T18:00:00", null, null, null, null));
        Assert.Equal(before, kit.Sender.Messages.Count);
    }

    [Fact]
    public async Task Oqituvchi_kartasi_orqali_ulangan_chatga_ham_boradi()
    {
        // LC: a teacher's chat is linked to the teacher CARD (TelegramRegistration.TeacherId), not the account.
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var e = await kit.PersonAsync(Roles.Teacher, name: "Kartali O'qituvchi");
        var card = new Teacher { FullName = e.User.FullName, UserId = e.User.Id };
        kit.Db.Teachers.Add(card);
        kit.Db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = card.Id, ChatId = 5_550_001, ParentName = "T" });
        // a blocked chat is skipped
        kit.Db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = card.Id, ChatId = 5_550_002, ParentName = "T", BlockedAt = DateTime.Now });
        await kit.Db.SaveChangesAsync();

        await kit.Service().CreateTaskAsync(await kit.PanelAsync(admin.User),
            new HrTaskCreateInput(e.Employee!.Id, "Dars rejasi", null, "2031-03-10T18:00:00", null, null, null, null));
        var sent = Assert.Single(kit.Sender.Messages);
        Assert.Equal(5_550_001, sent.Chat);
        Assert.Equal(TelegramBotService.PanelUrl + HrTasksService.MyTasksTeacherPath, sent.Url);

        // the card shows the linked chat; "linked" filter finds it
        var panel = await kit.PanelAsync(admin.User);
        Assert.Equal(5_550_001, (await kit.Service().EmployeeAsync(panel, e.Employee.Id)).TelegramUserId);
        var linked = await kit.Service().EmployeesAsync(panel, new HrEmployeeFilter(Linked: true), new HrPaging(200));
        Assert.Contains(linked.Items, x => x.Id == e.Employee.Id);
        var notLinked = await kit.Service().EmployeesAsync(panel, new HrEmployeeFilter(Linked: false), new HrPaging(200));
        Assert.DoesNotContain(notLinked.Items, x => x.Id == e.Employee.Id);
    }

    [Fact]
    public async Task Taklif_bot_havolasi_ulanganida_409()
    {
        // LC: no admin-issued code — the chat links itself on Mini App login; the invite gives the bot link.
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var e = await kit.PersonAsync();
        var invite = await kit.Service().InviteAsync(panel, e.Employee!.Id);
        Assert.Equal("", invite.InviteCode);
        Assert.Equal("https://t.me/wk_test_bot", invite.InviteUrl);
        Assert.Equal(e.Employee.Id, invite.EmployeeId);

        var linked = await kit.PersonAsync(telegram: true);
        var again = await FailsAsync(() => kit.Service().InviteAsync(panel, linked.Employee!.Id));
        Assert.Equal(409, again.StatusCode);
        Assert.Equal("user_already_linked", again.Code);
        // the card shows the linked Telegram id
        Assert.Equal(linked.Chat, (await kit.Service().EmployeeAsync(panel, linked.Employee!.Id)).TelegramUserId);
    }

    [Fact]
    public async Task Mening_topshiriqlarim_sahifa_shaklida()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey]);
        var mine = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1), title: "Meniki");
        var other = await kit.PersonAsync();
        await kit.TaskAsync(other.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1), title: "Begona");
        var me = await kit.Service().MeAsync(e.User.Id);
        var page = await kit.Service().MyTasksAsync(me, new HrTaskFilter(EmployeeId: other.Employee.Id), new HrPaging(20, null, true));
        Assert.Equal(mine.Id, Assert.Single(page.Items).Id); // employeeId in the query is ignored
        Assert.Equal(1, page.Total);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);

        var self = await kit.Service().CreateSelfTaskAsync(me, new HrSelfTaskInput("O'zimga", null, "2031-01-05T18:00:00+05:00"));
        Assert.Equal("self", self.Source);
        Assert.False(self.NeedsAcceptance);
    }

    [Fact]
    public async Task Xodim_faolligi_bajarilgan_HR_topshiriqlarini_sanaydi()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync(Roles.Staff);
        await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(-1), HrTaskStatuses.Done, completedAt: DateTimeOffset.UtcNow.AddHours(-3), comment: "ok");
        await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(-90), HrTaskStatuses.Done, completedAt: DateTimeOffset.UtcNow.AddDays(-60), comment: "eski");
        await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(1)); // open — not counted

        var rows = await StaffActivity.BuildAsync(kit.NewContext(), 30);
        Assert.Equal(1, rows.Single(r => r.Kind == "staff" && r.Id == e.User.Id).TasksClosed);
    }
}
