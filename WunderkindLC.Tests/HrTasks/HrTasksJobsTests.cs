using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;
using static WunderkindLC.Tests.HrTasks.HrTasksKit;

namespace WunderkindLC.Tests.HrTasks;

// ===========================================================================
//  HR topshiriqlari — scheduled work against a database (LC: SQLite TestDb), with a fixed "now" and a fake
//  Telegram sender: generation (idempotency, weekdays, monthly incl. short months, no past-due dates,
//  position targets, inactive employees), the reopen window, reminders, the evening digest (recipients,
//  scope, empty-skip), legacy close, and hr_job_runs idempotency of the tick. Ported 1:1 from
//  WunderkindLMS SchoolLms.Tests/HrTasks/HrTasksJobsTests.cs (each test owns its database here).
// ===========================================================================

public class HrTasksJobsTests
{
    private static HrTaskTemplate Template(string employeeId, string recurrence, short[]? weekdays = null, short[]? days = null,
        TimeOnly? due = null) => new()
    {
        Title = "Shablon " + Guid.NewGuid().ToString("N")[..6], TargetType = HrTargetTypes.Employee, TargetEmployeeId = employeeId,
        Recurrence = recurrence, Weekdays = weekdays ?? [], DaysOfMonth = days ?? [], DueTime = due,
        OverduePolicy = HrOverduePolicies.KeepOpen, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<HrTaskTemplate> SaveAsync(HrTasksKit kit, HrTaskTemplate template)
    {
        kit.Db.HrTaskTemplates.Add(template);
        await kit.Db.SaveChangesAsync();
        return template;
    }

    private static async Task<List<HrTask>> TasksOfAsync(HrTasksKit kit, string templateId)
    {
        await using var db = kit.NewContext();
        return await db.HrTasks.AsNoTracking().Where(t => t.TemplateId == templateId).OrderBy(t => t.DueAt).ToListAsync();
    }

    [Fact]
    public async Task Nusxa_yaratish_idempotent_va_hafta_kuniga_bogliq()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync();
        var weekly = await SaveAsync(kit, Template(e.Employee!.Id, HrRecurrences.Weekly, [1, 3], due: new TimeOnly(18, 0))); // Mon, Wed
        var monday = new DateOnly(2032, 3, 1);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        var now = At(2032, 3, 1, 0, 10);

        var s = kit.Service();
        var first = await s.GenerateAsync(monday, weekly.Id, now);
        Assert.Equal(1, first.Created);
        var again = await s.GenerateAsync(monday, weekly.Id, now);
        Assert.Equal(0, again.Created);
        Assert.Equal(1, again.Skipped);
        Assert.Equal(0, (await s.GenerateAsync(monday.AddDays(1), weekly.Id, At(2032, 3, 2, 0, 10))).Templates); // Tuesday
        Assert.Equal(1, (await s.GenerateAsync(monday.AddDays(2), weekly.Id, At(2032, 3, 3, 0, 10))).Created); // Wednesday

        var tasks = await TasksOfAsync(kit, weekly.Id);
        Assert.Equal(2, tasks.Count);
        var t = tasks[0];
        Assert.Equal(HrTaskSources.Template, t.Source);
        Assert.Equal(monday, t.PeriodStart);
        Assert.Equal(At(2032, 3, 1, 18), t.DueAt);
        Assert.Null(t.CreatedBy);
        Assert.Equal(weekly.Title, t.Title); // snapshot
    }

    [Fact]
    public async Task Oylik_shablon_butun_oy_qisqa_oy_va_otgan_sana()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync();
        var monthly = await SaveAsync(kit, Template(e.Employee!.Id, HrRecurrences.Monthly, days: [15, 31], due: new TimeOnly(18, 0)));
        // April: 15 and 30 (31 → the last day), both at the start of the month
        Assert.Equal(2, (await kit.Service().GenerateAsync(new DateOnly(2032, 4, 1), monthly.Id, At(2032, 4, 1, 0, 10))).Created);
        Assert.Equal(0, (await kit.Service().GenerateAsync(new DateOnly(2032, 4, 2), monthly.Id, At(2032, 4, 2, 0, 10))).Created);
        var april = await TasksOfAsync(kit, monthly.Id);
        Assert.Equal([At(2032, 4, 15, 18), At(2032, 4, 30, 18)], april.Select(t => t.DueAt));
        Assert.All(april, t => Assert.Equal(new DateOnly(2032, 4, 1), t.PeriodStart));

        // a template added mid-month: the 15th already passed → only the 31st (clamped to 31 in May)
        var late = await SaveAsync(kit, Template(e.Employee.Id, HrRecurrences.Monthly, days: [15, 31]));
        Assert.Equal(1, (await kit.Service().GenerateAsync(new DateOnly(2032, 5, 20), late.Id, At(2032, 5, 20, 0, 10))).Created);
        Assert.Equal(At(2032, 5, 31, 23, 59), Assert.Single(await TasksOfAsync(kit, late.Id)).DueAt);
    }

    [Fact]
    public async Task Lavozim_faol_xodimlarga_nofaolga_hech_narsa()
    {
        using var kit = new HrTasksKit();
        var position = new HrPosition { Name = "Lavozim " + Guid.NewGuid().ToString("N")[..8] };
        var active = await kit.PersonAsync();
        var fired = await kit.PersonAsync(active: false);
        kit.Db.HrPositions.Add(position);
        await kit.Db.SaveChangesAsync();
        active.Employee!.PositionId = position.Id;
        fired.Employee!.PositionId = position.Id;
        await kit.Db.SaveChangesAsync();
        var template = Template(active.Employee.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7]);
        template.TargetType = HrTargetTypes.Position;
        template.TargetEmployeeId = null;
        template.TargetPositionId = position.Id;
        await SaveAsync(kit, template);
        var direct = await SaveAsync(kit, Template(fired.Employee.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7]));

        Assert.Equal(1, (await kit.Service().GenerateAsync(new DateOnly(2032, 6, 7), template.Id, At(2032, 6, 7, 0, 10))).Created);
        Assert.Equal(0, (await kit.Service().GenerateAsync(new DateOnly(2032, 6, 7), direct.Id, At(2032, 6, 7, 0, 10))).Created);
        Assert.Equal(active.Employee.Id, Assert.Single(await TasksOfAsync(kit, template.Id)).EmployeeId);
        Assert.Empty(await TasksOfAsync(kit, direct.Id));
    }

    [Fact]
    public async Task Qayta_ochish_faqat_osha_kuni_va_yakundan_oldin()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync();
        var morning = At(2034, 5, 5, 10);
        var task = await kit.TaskAsync(e.Employee!.Id, At(2034, 5, 5, 18));
        var yesterday = await kit.TaskAsync(e.Employee.Id, At(2034, 5, 4, 18), HrTaskStatuses.Done, completedAt: At(2034, 5, 4, 17), comment: "ok");

        var s = kit.Service(morning);
        var me = await s.MeAsync(e.User.Id);
        await s.CompleteAsync(me, task.Id, new HrCompleteInput("Tayyor", []));
        Assert.Equal("open", (await kit.Service(morning).ReopenAsync(me, task.Id)).Status);
        await kit.Service(morning).CompleteAsync(me, task.Id, new HrCompleteInput("Tayyor", []));
        var ex = await Assert.ThrowsAsync<HrTasksException>(() => kit.Service(morning).ReopenAsync(me, yesterday.Id));
        Assert.Contains("reopen_window", ex.Message);

        // today's digest ran → no more reopening
        kit.Db.HrJobRuns.Add(new HrJobRun { JobName = HrJobs.Digest, RunDate = new DateOnly(2034, 5, 5) });
        await kit.Db.SaveChangesAsync();
        var late = await Assert.ThrowsAsync<HrTasksException>(() => kit.Service(morning.AddHours(10)).ReopenAsync(me, task.Id));
        Assert.Equal("hr_task_not_editable", late.Code);
        Assert.Contains("digest_sent", late.Message);
    }

    [Fact]
    public async Task Eslatma_bir_marta_eng_katta_offset_kun_oxiri_otkaziladi()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync(telegram: true);
        var now = At(2038, 2, 3, 15);
        var soon = await kit.TaskAsync(e.Employee!.Id, now.AddMinutes(50), title: "Tez orada");
        await kit.TaskAsync(e.Employee.Id, At(2038, 2, 3, 23, 59), title: "Kun oxiri"); // no due time → skipped

        Assert.Equal(1, await kit.Service().SendRemindersAsync(now));
        Assert.Equal(0, await kit.Service().SendRemindersAsync(now)); // 120 and 60 are claimed
        Assert.Equal(1, await kit.Service().SendRemindersAsync(now.AddMinutes(25))); // the 30-minute one
        Assert.Equal(0, await kit.Service().SendRemindersAsync(now.AddMinutes(60))); // past the deadline

        var texts = kit.Sender.To(e.Chat!.Value);
        Assert.Equal(["⏰ 50 daqiqadan keyin muddati tugaydi:\n• Tez orada", "⏰ 25 daqiqadan keyin muddati tugaydi:\n• Tez orada"], texts);
        await using var db = kit.NewContext();
        Assert.Equal([30, 60, 120], (await db.HrTaskReminders.Where(r => r.TaskId == soon.Id).Select(r => (int)r.OffsetMinutes).ToListAsync()).Order());
    }

    [Fact]
    public async Task Kunlik_yakun_direktorga_hammasi_managerga_shoxi_boshiga_yuborilmaydi()
    {
        using var kit = new HrTasksKit();
        var day = new DateOnly(2036, 11, 12);
        var director = await kit.PersonAsync(Roles.Staff, HrRoles.Director, [HrTaskAccess.PermissionKey], telegram: true, name: "Direktor Yakun");
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], telegram: true, name: "Rahbar Yakun");
        var idle = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], telegram: true, name: "Bekor Rahbar");
        var mine = await kit.PersonAsync(name: "Shoxdagi Xodim");
        var foreign = await kit.PersonAsync(name: "Tashqi Xodim");
        var idleSub = await kit.PersonAsync(name: "Ishsiz Xodim");
        var viewerNoRow = await kit.PersonAsync(Roles.Staff, hrRole: null, permissions: [HrTaskAccess.PermissionKey], telegram: true);
        // LC: a narrower token of the section ("hrTasks:edit") makes a panel user too (PermissionRules.HasSection)
        var narrow = await kit.PersonAsync(Roles.Staff, HrRoles.Director, ["hrTasks:edit"], telegram: true, name: "Tor Ruxsat");
        var noKey = await kit.PersonAsync(Roles.Staff, HrRoles.Director, ["staff"], telegram: true, name: "Kalitsiz");
        await kit.ReportsToAsync(mine.Employee!.Id, manager.Employee!.Id);
        await kit.ReportsToAsync(idleSub.Employee!.Id, idle.Employee!.Id);

        await kit.TaskAsync(mine.Employee.Id, At(2036, 11, 12, 18), HrTaskStatuses.Done, title: "Bajarilgan ish",
            completedAt: At(2036, 11, 12, 17), comment: "tayyor");
        await kit.TaskAsync(mine.Employee.Id, At(2036, 11, 12, 18), title: "Ochiq ish");
        await kit.TaskAsync(foreign.Employee!.Id, At(2036, 11, 12, 18), title: "Tashqi ish");

        await kit.Service().SendDigestAsync(day, At(2036, 11, 12, 19));

        var toDirector = string.Join("\n", kit.Sender.To(director.Chat!.Value));
        Assert.Contains("📊 <b>12.11.2036 — kunlik yakun</b>", toDirector);
        Assert.Contains("Shoxdagi Xodim", toDirector);
        Assert.Contains("Tashqi ish", toDirector);

        var toManager = Assert.Single(kit.Sender.To(manager.Chat!.Value));
        Assert.Contains("✅ Bajarilgan: 1 / 2 (50%)", toManager);
        Assert.Contains("   ✅ Bajarilgan ish (izoh: tayyor)", toManager);
        Assert.Contains("   ⏳ Ochiq ish — muddat 12.11 18:00, ochiq", toManager);
        Assert.DoesNotContain("Tashqi", toManager);

        Assert.Empty(kit.Sender.To(idle.Chat!.Value)); // empty subtree digest → not sent
        Assert.Empty(kit.Sender.To(viewerNoRow.Chat!.Value)); // a panel user without an HR row has no scope
        Assert.NotEmpty(kit.Sender.To(narrow.Chat!.Value));
        Assert.Empty(kit.Sender.To(noKey.Chat!.Value)); // a staff member without the key is not a panel user
        // LC: the digest's button opens the HR panel inside Telegram
        Assert.All(kit.Sender.Messages, m => Assert.EndsWith("/" + HrTasksService.DashboardPath, m.Url));
    }

    [Fact]
    public async Task Eski_close_qatorlari_yopiladi_keep_open_ochiq_qoladi()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync();
        var legacy = await kit.TaskAsync(e.Employee!.Id, At(2033, 1, 10, 18), policy: HrOverduePolicies.Close);
        var modern = await kit.TaskAsync(e.Employee.Id, At(2033, 1, 10, 18));
        Assert.True(await kit.Service().CloseOverdueAsync(At(2033, 1, 10, 23, 50)) >= 1);
        await using var db = kit.NewContext();
        Assert.Equal(HrTaskStatuses.Missed, (await db.HrTasks.SingleAsync(t => t.Id == legacy.Id)).Status);
        Assert.Equal(HrTaskStatuses.Open, (await db.HrTasks.SingleAsync(t => t.Id == modern.Id)).Status);
    }

    [Fact]
    public async Task Rejalashtiruvchi_har_ishni_kuniga_bir_marta_bajaradi()
    {
        using var kit = new HrTasksKit();
        var e = await kit.PersonAsync();
        var template = await SaveAsync(kit, Template(e.Employee!.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7], due: new TimeOnly(21, 0)));
        var scheduler = new HrTasksScheduler(kit.Scopes(), NullLogger<HrTasksScheduler>.Instance);
        var day = new DateOnly(2037, 2, 2); // a Monday — a digest day
        var evening = At(2037, 2, 2, 20);

        var first = await scheduler.TickAsync(evening);
        foreach (var job in new[] { "generated", "morning_sent", "digest_sent" })
            Assert.True(first.ContainsKey(job), job);
        Assert.False(first.ContainsKey("closed")); // 23:50 not reached yet

        var second = await scheduler.TickAsync(evening.AddMinutes(5));
        Assert.DoesNotContain("generated", second.Keys);
        Assert.DoesNotContain("digest_sent", second.Keys);

        var night = await scheduler.TickAsync(At(2037, 2, 2, 23, 55));
        Assert.True(night.ContainsKey("closed"));
        Assert.True(night.ContainsKey("drafts_cleaned"));
        Assert.False((await scheduler.TickAsync(At(2037, 2, 2, 23, 59))).ContainsKey("closed"));

        await using var db = kit.NewContext();
        var runs = await db.HrJobRuns.Where(r => r.RunDate == day).ToListAsync();
        foreach (var job in new[] { HrJobs.Generate, HrJobs.Morning, HrJobs.Digest, HrJobs.CloseOverdue, HrJobs.CleanDrafts })
        {
            var run = Assert.Single(runs, r => r.JobName == job);
            Assert.Equal(HrJobs.StatusOk, run.Status);
            Assert.NotNull(run.FinishedAt);
        }
        // generation ran ONCE: one instance of our template for that day
        Assert.Single(await db.HrTasks.Where(t => t.TemplateId == template.Id && t.PeriodStart == day).ToListAsync());
    }

    [Fact]
    public async Task Ertalabki_royxat_tugmasi_rolga_qarab_sahifa_ochadi()
    {
        // LC addition: the «📋 Topshiriqlarim» button opens the teacher portal for a teacher, the panel otherwise.
        using var kit = new HrTasksKit();
        var teacher = await kit.PersonAsync(Roles.Teacher, telegram: true);
        var staff = await kit.PersonAsync(Roles.Staff, telegram: true);
        await kit.TaskAsync(teacher.Employee!.Id, At(2039, 3, 7, 18), title: "Dars rejasi");
        await kit.TaskAsync(staff.Employee!.Id, At(2039, 3, 7, 18), title: "Hisobot");

        Assert.Equal(2, await kit.Service().SendMorningListsAsync(new DateOnly(2039, 3, 7), At(2039, 3, 7, 7, 30)));
        Assert.EndsWith("/" + HrTasksService.MyTasksTeacherPath, kit.Sender.Messages.Single(m => m.Chat == teacher.Chat).Url);
        Assert.EndsWith("/" + HrTasksService.MyTasksPanelPath, kit.Sender.Messages.Single(m => m.Chat == staff.Chat).Url);
    }
}
