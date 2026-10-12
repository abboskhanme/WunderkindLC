using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// CRM Phase 3, U3b (SPEC P3.3 / P3.7 «AbsentNoticeTests», acceptance P3.9 #1–#4): «kelmadi» goes at lesson end +
/// delay, managers can hold / approve / cancel it, late-taken attendance waits for approval and expires unsent,
/// a correction to «keldi» cancels it, past days never message parents, «keldi» stays immediate.
/// Time is passed explicitly (the queue, the worker and the rules take <c>now</c>), so the tests do not depend on the
/// wall clock: lessons are TODAY 14:00–15:30.
/// </summary>
public class AbsentNoticeTests
{
    private static readonly string Today = AppClock.Today.ToString("yyyy-MM-dd");
    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");
    private static DateTime At(int h, int m) => AppClock.Today.ToDateTime(new TimeOnly(h, m));
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    /* ===================================== pure rules ===================================== */

    [Fact]
    public void SendAfter_is_lesson_end_plus_delay_never_before_the_mark()
    {
        // 14:00–15:30, delay 1, marked 14:10 → 15:31 (acceptance #1).
        Assert.Equal(At(15, 31), AbsentNoticeRules.SendAfter(Today, "15:30", "14:00", At(14, 10), 1));
        // Marked after the end → right away (not in the past).
        Assert.Equal(At(16, 0), AbsentNoticeRules.SendAfter(Today, "15:30", "14:00", At(16, 0), 1));
        // Unknown end → mark + delay; broken end (before start) is unknown too.
        Assert.Equal(At(14, 15), AbsentNoticeRules.SendAfter(Today, "", "14:00", At(14, 10), 5));
        Assert.Equal(At(14, 15), AbsentNoticeRules.SendAfter(Today, "03:00", "13:30", At(14, 10), 5));
        // Delay 0 → exactly at the end.
        Assert.Equal(At(15, 30), AbsentNoticeRules.SendAfter(Today, "15:30", "14:00", At(14, 10), 0));
    }

    [Fact]
    public void TakenLate_is_first_mark_after_start_plus_deadline()
    {
        Assert.False(AbsentNoticeRules.TakenLate(Today, "14:00", At(14, 12), 15));
        Assert.False(AbsentNoticeRules.TakenLate(Today, "14:00", At(14, 15), 15));   // exactly at the deadline — on time
        Assert.True(AbsentNoticeRules.TakenLate(Today, "14:00", At(14, 40), 15));
        Assert.False(AbsentNoticeRules.TakenLate(Today, "", At(23, 0), 15));        // no start — never late
        Assert.False(AbsentNoticeRules.TakenLate(Today, "14:00", null, 15));
    }

    [Theory]
    [InlineData("absent", "auto", false, "pending")]
    [InlineData("absent", "auto", true, "pending")]
    [InlineData("absent", "manual", false, "awaiting")]
    [InlineData("absent", "late_only", false, "pending")]
    [InlineData("absent", "late_only", true, "awaiting")]
    [InlineData("absent", "???", true, "awaiting")]      // unknown mode behaves like the default
    [InlineData("present", "manual", true, "pending")]   // «keldi» never needs approval
    public void Initial_status_by_approval_mode(string kind, string mode, bool late, string expected) =>
        Assert.Equal(expected, AbsentNoticeRules.InitialStatus(kind, mode, late));

    [Fact]
    public void Transitions_table()
    {
        var all = new[] { "pending", "awaiting", "held", "approved", "sending", "done", "failed", "unknown", "expired", "cancelled" };
        string[] Allowed(string action) => all.Where(s => AbsentNoticeRules.CanApply(s, action)).ToArray();
        Assert.Equal(new[] { "pending", "awaiting", "approved" }, Allowed("hold"));
        Assert.Equal(new[] { "pending", "awaiting", "held" }, Allowed("approve"));
        Assert.Equal(new[] { "pending", "awaiting", "held", "approved" }, Allowed("cancel"));
        Assert.Empty(Allowed("send"));
        Assert.Equal("held", AbsentNoticeRules.TargetStatus("hold"));
        Assert.Equal("approved", AbsentNoticeRules.TargetStatus("approve"));
        Assert.Equal("cancelled", AbsentNoticeRules.TargetStatus("cancel"));
    }

    [Fact]
    public void Only_pending_and_approved_are_sent_and_open_ones_expire_after_six_hours()
    {
        var sendAfter = At(15, 31);
        Assert.False(AbsentNoticeRules.Sendable("pending", sendAfter, At(15, 30)));
        Assert.True(AbsentNoticeRules.Sendable("pending", sendAfter, At(15, 31)));
        Assert.True(AbsentNoticeRules.Sendable("approved", sendAfter, At(16, 0)));
        Assert.False(AbsentNoticeRules.Sendable("awaiting", sendAfter, At(16, 0)));
        Assert.False(AbsentNoticeRules.Sendable("held", sendAfter, At(16, 0)));
        Assert.False(AbsentNoticeRules.IsExpired("held", sendAfter, At(21, 31)));
        Assert.True(AbsentNoticeRules.IsExpired("held", sendAfter, At(21, 32)));
        Assert.False(AbsentNoticeRules.IsExpired("done", sendAfter, At(23, 0)));
    }

    [Fact]
    public void Texts_and_keys()
    {
        Assert.Equal("https://crm.example.uz", AbsentNoticeRules.BaseUrl("crm.example.uz"));
        Assert.Equal("https://crm.example.uz", AbsentNoticeRules.BaseUrl("https://crm.example.uz/"));
        Assert.Equal("", AbsentNoticeRules.BaseUrl(" "));

        var text = AbsentNoticeRules.AlertText("Ingliz A1", "2026-10-12", 3, true, new DateTime(2026, 10, 12, 14, 40, 0), "https://crm.example.uz");
        Assert.Equal("🕓 Ingliz A1 (12.10.2026) — 3 ta «kelmadi» xabari tasdiq kutmoqda: davomat kech olingan (14:40). "
                     + "Tekshiring: https://crm.example.uz/admin/nazorat/davomat-xabarlari?date=2026-10-12", text);
        Assert.DoesNotContain("Tekshiring", AbsentNoticeRules.AlertText("A", "2026-10-12", 1, true, null, ""));

        Assert.Equal("«Kelmadi» xabari to'xtatildi: Ali, Ingliz A1, 12.10.2026 — izoh: kasal emas",
            AbsentNoticeRules.AuditSummary("hold", "absent", "Ali", "Ingliz A1", "2026-10-12", "kasal emas"));
        Assert.Equal("«Kelmadi» xabari bekor qilindi: Ali, Ingliz A1, 12.10.2026",
            AbsentNoticeRules.AuditSummary("cancel", "absent", "Ali", "Ingliz A1", "2026-10-12", null));

        Assert.Equal("sent", AbsentNoticeRules.CountKey("done"));
        Assert.Equal("pending", AbsentNoticeRules.CountKey("sending"));
        Assert.Equal("failed", AbsentNoticeRules.CountKey("unknown"));
        Assert.Equal("notice-await:g1:2026-10-12", AbsentNoticeRules.AlertKey("g1", "2026-10-12"));
        Assert.Null(AbsentNoticeRules.CleanNote("  ").Note);
        Assert.NotNull(AbsentNoticeRules.CleanNote(new string('x', 301)).Error);
    }

    /* ===================================== fixtures ===================================== */

    private static Group AddGroup(TestDb db, string name = "Ingliz A1")
    {
        var subject = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(subject);
        var g = new Group
        {
            Name = name, CourseId = subject.Id, StartDate = Day(-90),
            Days = Enumerable.Range(0, 7).ToList(), StartTime = "14:00", EndTime = "15:30", TeacherId = "t-1",
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, Group g, string name, string userId)
    {
        var s = new Student { FullName = name, UserId = userId, ParentPhone = "901112233" };
        db.Context.Students.Add(s);
        db.Context.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
            JoinedAt = Day(-60), ActivatedAt = Day(-60), RecordedAt = Day(-60),
        });
        db.Context.SaveChanges();
        return s;
    }

    private static AbsenceReason Reason(TestDb db, string name = "Sababsiz", bool late = false)
    {
        var r = new AbsenceReason { Name = name, Short = name[..1], IsLate = late };
        db.Context.AbsenceReasons.Add(r);
        db.Context.SaveChanges();
        return r;
    }

    private static void AddRule(TestDb db, string trigger, string template = "{fish}: {sabab}")
    {
        db.Context.AutoMessageRules.Add(new AutoMessageRule { Trigger = trigger, Enabled = true, Audience = "parents", Template = template });
        db.Context.SaveChanges();
    }

    private static void Settings(TestDb db, string approval = "late_only", int delay = 1)
    {
        db.Context.CenterMeta.Add(new CenterMeta { AbsentNoticeApproval = approval, AbsentNoticeDelayMinutes = delay });
        db.Context.SaveChanges();
    }

    private static AttendanceNotifyWorker Worker(IAppDbContext ctx) =>
        new(new SingleServiceProvider(ctx), new MessagingStack().Auto, NullLogger<AttendanceNotifyWorker>.Instance);

    /// <summary>Bulk attendance exactly like the controllers (queue rows in beforeSave), at a chosen time.</summary>
    private static Task<JournalService.BulkAttendanceResult> Bulk(TestDb db, BulkAttendanceRequest req, DateTime now) =>
        JournalService.BulkAttendanceAsync(db.Context, req,
            beforeSave: r => AttendanceNotify.EnqueueBulkAsync(db.Context, req, r, None, now: now));

    /// <summary>One cell exactly like the controllers: save → reconcile → enqueue → save.</summary>
    private static async Task Cell(TestDb db, Group g, Student s, string date, DateTime now, string? reasonId, bool present = false)
    {
        var req = new SetJournalEntryRequest(g.Id, g.CourseId!, 1, s.Id, date, 1, null, reasonId, Present: present);
        var wasPresent = present
            ? await JournalService.PresentStudentIdsAsync(db.Context, g.Id, g.CourseId!, date, 1, [s.Id])
            : new HashSet<string>();
        var newAbsence = await JournalService.SetEntryAsync(db.Context, req);
        await AttendanceNotify.ReconcileAsync(db.Context, now, g.Id, date, [s.Id]);
        if (await AttendanceNotify.EnqueueSingleAsync(db.Context, req, newAbsence, wasPresent, now: now) > 0)
            await db.Context.SaveChangesAsync();
    }

    /// <summary>Marks attendance of the group's day as first taken at <paramref name="at"/> (the single path reads it from the DB).</summary>
    private static void FirstMark(TestDb db, Group g, DateTime at)
    {
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, Date = Today, Period = 1, Conducted = true,
            AttendanceTakenAt = at,
        });
        db.Context.SaveChanges();
    }

    private static List<string> Notified(TestDb db) =>
        db.Context.UserNotifications.AsNoTracking().Where(n => n.Type != AttendanceNotify.AlertNotificationType)
            .Select(n => n.UserId).OrderBy(x => x).ToList();

    private static AttendanceNotifyItem Item(TestDb db, Student s) =>
        db.Context.AttendanceNotifyItems.AsNoTracking().Single(i => i.StudentId == s.Id);

    /* ===================================== queue + worker ===================================== */

    [Fact]
    public async Task On_time_absence_goes_at_lesson_end_plus_delay_not_before()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);

        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], sababsiz.Id, Absent: true), At(14, 10));

        var item = Item(db, ali);
        Assert.Equal("pending", item.Status);
        Assert.Equal(At(15, 31), item.SendAfter);
        Assert.Equal("14:00", item.LessonStart);
        Assert.Equal("15:30", item.LessonEnd);
        Assert.False(item.TakenLate);

        var worker = Worker(db.Context);
        Assert.Equal(0, await worker.ProcessDueAsync(At(15, 30), CancellationToken.None));
        Assert.Empty(Notified(db));
        Assert.Equal(1, await worker.ProcessDueAsync(At(15, 31), CancellationToken.None));
        Assert.Equal(new[] { "u-ali" }, Notified(db));
        Assert.Equal("done", Item(db, ali).Status);
    }

    [Fact]
    public async Task Late_attendance_waits_for_approval_alerts_managers_once_and_expires_unsent()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);   // late_only, deadline 15
        var super = new AppUser { FullName = "Boss", Role = Roles.SuperAdmin, Email = "s@x" };
        var manager = new AppUser { FullName = "Menejer", Role = Roles.Staff, Email = "m@x", Permissions = ["students.notices"] };
        var cashier = new AppUser { FullName = "Kassir", Role = Roles.Staff, Email = "k@x", Permissions = ["finance.main"] };
        db.Context.Users.AddRange(super, manager, cashier);
        db.Context.SaveChanges();

        // First mark of the day at 14:40 (deadline 14:15) → «Tasdiq kutmoqda» (acceptance #3).
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id, vali.Id], sababsiz.Id, Absent: true), At(14, 40));
        Assert.All(db.Context.AttendanceNotifyItems.AsNoTracking().ToList(), i =>
        {
            Assert.Equal("awaiting", i.Status);
            Assert.True(i.TakenLate);
        });

        var worker = Worker(db.Context);
        await worker.ProcessDueAsync(At(14, 41), CancellationToken.None);
        await worker.ProcessDueAsync(At(14, 50), CancellationToken.None);   // second cycle — no second alert
        var alerts = db.Context.UserNotifications.AsNoTracking()
            .Where(n => n.Type == AttendanceNotify.AlertNotificationType).ToList();
        Assert.Equal(new[] { manager.Id, super.Id }.OrderBy(x => x), alerts.Select(a => a.UserId).OrderBy(x => x));
        Assert.Contains("2 ta «kelmadi» xabari tasdiq kutmoqda: davomat kech olingan", alerts[0].Body);

        // Nobody acts: never sent, «Muddati o'tdi» after SendAfter (15:31) + 6 h (acceptance #2).
        await worker.ProcessDueAsync(At(16, 0), CancellationToken.None);
        Assert.Empty(Notified(db));
        await worker.ProcessDueAsync(At(21, 32), CancellationToken.None);
        Assert.All(db.Context.AttendanceNotifyItems.AsNoTracking().ToList(), i => Assert.Equal("expired", i.Status));
        Assert.Empty(Notified(db));
    }

    [Fact]
    public async Task Alert_goes_to_managers_telegram_chats_only()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db, approval: "manual");
        var admin = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "a@x" };
        var cashier = new AppUser { FullName = "Kassir", Role = Roles.Staff, Email = "k@x", Permissions = ["finance.main"] };
        db.Context.Users.AddRange(admin, cashier);
        db.Context.TelegramRegistrations.AddRange(
            new TelegramRegistration { UserId = admin.Id, ChatId = 111 },
            new TelegramRegistration { UserId = cashier.Id, ChatId = 222 });
        db.Context.SaveChanges();

        // manual mode: awaiting even on time.
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], sababsiz.Id, Absent: true), At(14, 5));
        Assert.Equal("awaiting", Item(db, ali).Status);

        var sent = new List<(long Chat, string Text)>();
        Task Send(long chat, string text, CancellationToken _) { sent.Add((chat, text)); return Task.CompletedTask; }
        Assert.Equal(1, await AttendanceNotify.AlertAwaitingAsync(db.Context, Send, "https://crm.example.uz", At(14, 6)));
        Assert.Equal(0, await AttendanceNotify.AlertAwaitingAsync(db.Context, Send, "https://crm.example.uz", At(14, 7)));
        Assert.Equal(new long[] { 111 }, sent.Select(s => s.Chat).ToArray());
        Assert.Contains("har bir xabarni menejer tasdiqlaydi", sent[0].Text);
        Assert.Contains($"/admin/nazorat/davomat-xabarlari?date={Today}", sent[0].Text);
    }

    [Fact]
    public async Task Held_notice_is_never_sent_until_approved()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], sababsiz.Id, Absent: true), At(14, 10));

        // Manager «To'xtatish» before 15:31 (the controller applies exactly this transition).
        var item = db.Context.AttendanceNotifyItems.Single();
        Assert.True(AbsentNoticeRules.CanApply(item.Status, AbsentNoticeRules.ActionHold));
        item.Status = AbsentNoticeRules.TargetStatus(AbsentNoticeRules.ActionHold)!;
        db.Context.SaveChanges();

        var worker = Worker(db.Context);
        await worker.ProcessDueAsync(At(15, 40), CancellationToken.None);
        Assert.Empty(Notified(db));
        Assert.Equal("held", Item(db, ali).Status);

        // «Tasdiqlash» after SendAfter → goes on the next cycle.
        item = db.Context.AttendanceNotifyItems.Single();
        Assert.True(AbsentNoticeRules.CanApply(item.Status, AbsentNoticeRules.ActionApprove));
        item.Status = AbsentNoticeRules.TargetStatus(AbsentNoticeRules.ActionApprove)!;
        db.Context.SaveChanges();
        await worker.ProcessDueAsync(At(15, 45), CancellationToken.None);
        Assert.Equal(new[] { "u-ali" }, Notified(db));
        Assert.False(AbsentNoticeRules.CanApply(Item(db, ali).Status, AbsentNoticeRules.ActionCancel));   // sent → 409
    }

    [Fact]
    public async Task Teacher_fixing_absent_to_present_cancels_the_unsent_notice()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);
        FirstMark(db, g, At(14, 5));

        await Cell(db, g, ali, Today, At(14, 10), sababsiz.Id);
        Assert.Equal("pending", Item(db, ali).Status);
        Assert.Equal(At(15, 31), Item(db, ali).SendAfter);
        Assert.Empty(Notified(db));   // nothing in the request any more

        await Cell(db, g, ali, Today, At(14, 20), null, present: true);   // acceptance #4
        var item = Item(db, ali);
        Assert.Equal("cancelled", item.Status);
        Assert.Equal(AbsentNoticeRules.CancelChanged, item.CancelReason);

        await Worker(db.Context).ProcessDueAsync(At(15, 31), CancellationToken.None);
        Assert.Empty(Notified(db));
    }

    [Fact]
    public async Task Changed_to_late_is_cancelled_by_the_worker_sweep_and_reason_change_keeps_one_notice()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        var sababsiz = Reason(db);
        var kasal = Reason(db, "Kasal");
        var kech = Reason(db, "Kech", late: true);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);
        FirstMark(db, g, At(14, 5));

        await Cell(db, g, ali, Today, At(14, 10), sababsiz.Id);
        await Cell(db, g, vali, Today, At(14, 10), sababsiz.Id);
        // Ali: «Sababsiz» → «Kasal» — not a new event (still one notice), the message carries the CURRENT reason.
        await Cell(db, g, ali, Today, At(14, 12), kasal.Id);
        Assert.Single(db.Context.AttendanceNotifyItems.AsNoTracking().Where(i => i.StudentId == ali.Id));
        // Vali: changed to «Kech» directly in the journal (any path) — the worker sweep cancels it.
        var entry = db.Context.JournalEntries.Single(e => e.StudentId == vali.Id);
        entry.ReasonId = kech.Id;
        db.Context.SaveChanges();

        await Worker(db.Context).ProcessDueAsync(At(15, 31), CancellationToken.None);
        Assert.Equal("cancelled", Item(db, vali).Status);
        Assert.Equal("done", Item(db, ali).Status);
        var body = db.Context.UserNotifications.AsNoTracking().Single(n => n.UserId == "u-ali").Body;
        Assert.Contains("Kasal", body);
    }

    [Fact]
    public async Task Past_day_corrections_never_message_parents()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);

        await Cell(db, g, ali, Day(-1), AppClock.Now, sababsiz.Id);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Day(-1), 1, [vali.Id], sababsiz.Id, Absent: true), AppClock.Now);

        Assert.Empty(db.Context.AttendanceNotifyItems.AsNoTracking());
        Assert.Equal(2, db.Context.JournalEntries.Count());   // the journal itself is saved
    }

    [Fact]
    public async Task Present_notice_is_immediate_and_never_waits()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var vali = AddStudent(db, g, "Vali", "u-vali");
        AddRule(db, AutoMessageTriggers.AttendancePresent, "{fish} darsga keldi");
        Settings(db, approval: "manual");

        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], null), At(14, 40));
        await Cell(db, g, vali, Today, At(14, 41), null, present: true);

        var items = db.Context.AttendanceNotifyItems.AsNoTracking().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(AttendanceNotify.KindPresent, i.Kind);
            Assert.Equal("pending", i.Status);
            Assert.False(i.TakenLate);
        });
        Assert.Equal(At(14, 40), items.Single(i => i.StudentId == ali.Id).SendAfter);

        await Worker(db.Context).ProcessDueAsync(At(14, 41), CancellationToken.None);
        Assert.Equal(new[] { "u-ali", "u-vali" }, Notified(db));
    }

    [Fact]
    public async Task Archived_student_is_cancelled_not_sent()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], sababsiz.Id, Absent: true), At(14, 10));
        ali.IsArchived = true;
        db.Context.SaveChanges();

        await Worker(db.Context).ProcessDueAsync(At(15, 31), CancellationToken.None);
        Assert.Equal("cancelled", Item(db, ali).Status);
        Assert.Equal(AbsentNoticeRules.CancelArchived, Item(db, ali).CancelReason);
        Assert.Empty(Notified(db));
    }

    [Fact]
    public async Task Moved_lesson_uses_its_new_time()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali", "u-ali");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db, delay: 5);
        // Yesterday's lesson moved to today 18:00 (90 minutes → ends 19:30); today's regular one moved to tomorrow.
        db.Context.LessonReschedules.AddRange(
            new LessonReschedule { ClassId = g.Id, FromDate = Day(-1), ToDate = Today, Time = "18:00" },
            new LessonReschedule { ClassId = g.Id, FromDate = Today, ToDate = Day(1) });
        db.Context.SaveChanges();

        var ctx = await AttendanceNotify.LessonContextAsync(db.Context, g.Id, Today);
        Assert.Equal("18:00", ctx.Start);
        Assert.Equal("19:30", ctx.End);
        await Bulk(db, new BulkAttendanceRequest(g.Id, g.CourseId!, Today, 1, [ali.Id], sababsiz.Id, Absent: true), At(18, 5));
        Assert.Equal(At(19, 35), Item(db, ali).SendAfter);
    }

    [Fact]
    public async Task Control_page_list_groups_per_lesson_with_counts_before_the_status_filter()
    {
        using var db = TestDb.Sqlite();
        var a = AddGroup(db, "A guruh");
        var b = AddGroup(db, "B guruh");
        var ali = AddStudent(db, a, "Ali", "u-ali");
        var vali = AddStudent(db, a, "Vali", "u-vali");
        var soli = AddStudent(db, b, "Soli", "u-soli");
        var sababsiz = Reason(db);
        AddRule(db, AutoMessageTriggers.AttendanceAbsent);
        Settings(db);
        await Bulk(db, new BulkAttendanceRequest(a.Id, a.CourseId!, Today, 1, [ali.Id, vali.Id], sababsiz.Id, Absent: true), At(14, 10));
        await Bulk(db, new BulkAttendanceRequest(b.Id, b.CourseId!, Today, 1, [soli.Id], sababsiz.Id, Absent: true), At(14, 50));

        var all = await AbsentNoticeQueries.ListAsync(db.Context, Today, null, null, null);
        Assert.Equal(2, all.Counts.Pending);
        Assert.Equal(1, all.Counts.Awaiting);
        Assert.Equal(2, all.Lessons.Count);
        var lessonA = all.Lessons.Single(l => l.GroupId == a.Id);
        Assert.Equal(new[] { "Ali", "Vali" }, lessonA.Items.Select(i => i.StudentName));
        Assert.Equal("Sababsiz", lessonA.Items[0].ReasonName);
        Assert.Null(lessonA.Items[0].Delivery);
        Assert.False(lessonA.TakenLate);
        Assert.True(all.Lessons.Single(l => l.GroupId == b.Id).TakenLate);

        var awaiting = await AbsentNoticeQueries.ListAsync(db.Context, Today, "awaiting", null, null);
        Assert.Equal(2, awaiting.Counts.Pending);   // counters ignore the status filter
        Assert.Equal(new[] { "Soli" }, awaiting.Lessons.SelectMany(l => l.Items).Select(i => i.StudentName));

        var summary = await AbsentNoticeQueries.SummaryAsync(db.Context, Today);
        Assert.Equal(3, summary.Counts.Pending + summary.Counts.Awaiting);
    }

    [Fact]
    public void Notice_audit_type_is_mapped_to_groups()
    {
        Assert.Equal("classes", AuditSections.SectionOf(AbsentNoticeAudit.EntityType));
        Assert.Equal("g1:i1", AbsentNoticeAudit.EntityId("g1", "i1"));
    }
}
