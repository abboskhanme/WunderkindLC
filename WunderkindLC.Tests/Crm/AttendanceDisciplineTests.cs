using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// U3c — teacher reminders, admin escalation and attendance fines (SPEC P3.4): the pure decision
/// (<see cref="AttendanceDiscipline.Decide"/>) and the background run with a fixed clock and a fake Telegram.
/// </summary>
public class AttendanceDisciplineTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static readonly AttendanceSettingsDto Settings = new(true, 15, [5, 10], true, true, 20_000m, 1, "late_only");

    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");
    private static string Yesterday => AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd");

    /// <summary>Today at HH:mm (centre time).</summary>
    private static DateTime At(int h, int m, int dayOffset = 0) =>
        AppClock.Today.AddDays(dayOffset).ToDateTime(new TimeOnly(h, m));

    /* =========================================================================================
     *  1) Pure decision
     * ========================================================================================= */

    [Fact]
    public void Reminders_at_plus_5_and_plus_10_only_while_not_taken()
    {
        Assert.Equal([5], AttendanceDiscipline.Decide(Today, "14:00", At(14, 5), null, Settings, true).DueReminders);
        Assert.Equal([5], AttendanceDiscipline.Decide(Today, "14:00", At(14, 9), null, Settings, true).DueReminders);
        Assert.Equal([5, 10], AttendanceDiscipline.Decide(Today, "14:00", At(14, 12), null, Settings, true).DueReminders);
        Assert.Empty(AttendanceDiscipline.Decide(Today, "14:00", At(14, 4), null, Settings, true).DueReminders);
        // Taken → no reminders.
        Assert.Empty(AttendanceDiscipline.Decide(Today, "14:00", At(14, 12), At(14, 3), Settings, true).DueReminders);
        // Window: a stale reminder (25 min late) is not sent.
        Assert.Empty(AttendanceDiscipline.Decide(Today, "14:00", At(14, 30), null, Settings, true).DueReminders);
    }

    [Fact]
    public void Attendance_at_plus_12_is_on_time_at_plus_16_is_fined()
    {
        var onTime = AttendanceDiscipline.Decide(Today, "14:00", At(14, 20), At(14, 12), Settings, true);
        Assert.False(onTime.FineDue);
        Assert.False(onTime.EscalateDue);

        var late = AttendanceDiscipline.Decide(Today, "14:00", At(14, 20), At(14, 16), Settings, true);
        Assert.True(late.FineDue);
        Assert.True(late.EscalateDue);

        var notYet = AttendanceDiscipline.Decide(Today, "14:00", At(14, 14), null, Settings, true);
        Assert.False(notYet.FineDue);
    }

    [Fact]
    public void Lesson_without_start_time_is_skipped_and_yesterday_never_escalates()
    {
        var none = AttendanceDiscipline.Decide(Today, "", At(18, 0), null, Settings, true);
        Assert.Null(none.Start);
        Assert.False(none.FineDue);

        var y = AttendanceDiscipline.Decide(Yesterday, "14:00", At(9, 0), null, Settings, isToday: false);
        Assert.True(y.FineDue);
        Assert.False(y.EscalateDue);
        Assert.Empty(y.DueReminders);
    }

    [Fact]
    public void Fine_disabled_or_zero_amount_is_never_due()
    {
        Assert.False(AttendanceDiscipline.Decide(Today, "14:00", At(15, 0), null, Settings with { FineEnabled = false }, true).FineDue);
        Assert.False(AttendanceDiscipline.Decide(Today, "14:00", At(15, 0), null, Settings with { FineAmount = 0 }, true).FineDue);
    }

    [Fact]
    public void Message_texts_match_the_spec()
    {
        var deadline = At(14, 15);
        Assert.Equal("⏰ A1 (Ingliz tili) — dars 14:00 da boshlandi. Davomatni kiriting.\n14:15 gacha olinmasa 20 000 so'm jarima yoziladi.",
            AttendanceDiscipline.ReminderText("A1", "Ingliz tili", "14:00", deadline, true, 20_000m));
        Assert.StartsWith("❗️ ", AttendanceDiscipline.FineText(Today, "A1", deadline, 20_000m));
        Assert.Equal("⚠️ Davomat olinmadi: A1 — Ali Valiyev, dars 14:00. Jarima: 20 000 so'm.",
            AttendanceDiscipline.EscalationText("A1", "Ali Valiyev", "14:00", 20_000m));
    }

    /* =========================================================================================
     *  2) Background run
     * ========================================================================================= */

    private sealed record Seed(Group Group, Teacher Teacher, AppUser TeacherUser, AppUser Admin, Teacher Substitute);

    private static Seed SeedDb(TestDb db, bool enabled = true, bool yesterdayTaken = true)
    {
        var c = db.Context;
        c.CenterMeta.Add(new CenterMeta
        {
            AttendanceDisciplineEnabled = enabled, AttendanceDeadlineMinutes = 15, AttendanceReminderMinutes = "5,10",
            AttendanceEscalateEnabled = true, AttendanceFineEnabled = true, AttendanceFineAmount = 20_000m,
        });
        var tu = new AppUser { FullName = "Ali Valiyev", Role = Roles.Teacher, Email = "t@x" };
        var admin = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "a@x" };
        var staffNo = new AppUser { FullName = "Kassir", Role = Roles.Staff, Email = "k@x", Permissions = ["finance.main"] };
        var teacher = new Teacher { FullName = "Ali Valiyev", UserId = tu.Id };
        var sub = new Teacher { FullName = "Vali O'rinbosar" };
        var subject = new Subject { Name = "Ingliz tili" };
        var g = new Group
        {
            Name = "A1", CourseId = subject.Id, TeacherId = teacher.Id, StartTime = "14:00", EndTime = "15:30",
            StartDate = AppClock.Today.AddMonths(-2).ToString("yyyy-MM-dd"), Days = [0, 1, 2, 3, 4, 5, 6],
        };
        var st = new Student { FullName = "O'quvchi" };
        c.AddRange(tu, admin, staffNo, teacher, sub, subject, g, st);
        var since = AppClock.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        c.StudentGroups.Add(new StudentGroup
        {
            StudentId = st.Id, GroupId = g.Id, IsActive = true, Status = "active",
            JoinedAt = since, ActivatedAt = since, RecordedAt = since,
        });
        c.TelegramRegistrations.AddRange(
            new TelegramRegistration { TeacherId = teacher.Id, ChatId = 111 },
            new TelegramRegistration { UserId = admin.Id, ChatId = 222 },
            new TelegramRegistration { UserId = staffNo.Id, ChatId = 333 });
        if (yesterdayTaken)
            c.LessonNotes.Add(new LessonNote
            {
                ClassId = g.Id, SubjectId = subject.Id, Quarter = 1, Date = Yesterday, Period = 1,
                Conducted = true, AttendanceTaken = true, AttendanceTakenAt = At(14, 3, dayOffset: -1),
            });
        c.SaveChanges();
        return new Seed(g, teacher, tu, admin, sub);
    }

    private static async Task<(AttendanceDisciplineService.RunResult Result, List<(long Chat, string Text)> Sent)> RunAsync(
        TestDb db, DateTime now)
    {
        var sent = new List<(long, string)>();
        var r = await AttendanceDisciplineService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()),
            (chat, text, _) => { sent.Add((chat, text)); return Task.CompletedTask; }, now);
        return (r, sent);
    }

    [Fact]
    public async Task Disabled_does_nothing()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db, enabled: false);
        var (r, sent) = await RunAsync(db, At(14, 20));
        Assert.Equal(new AttendanceDisciplineService.RunResult(0, 0, 0), r);
        Assert.Empty(sent);
        Assert.Empty(db.Context.TeacherAttendanceFines);
    }

    [Fact]
    public async Task Reminder_goes_once_to_the_teacher_bell_and_telegram()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var (r1, sent1) = await RunAsync(db, At(14, 5));
        Assert.Equal(1, r1.Reminders);
        Assert.Single(sent1, x => x.Chat == 111 && x.Text.StartsWith("⏰ A1"));
        Assert.Single(db.Context.UserNotifications.AsNoTracking().Where(n => n.UserId == s.TeacherUser.Id).ToList());

        // Restart / next tick in the same window: no duplicate.
        var (r2, sent2) = await RunAsync(db, At(14, 6));
        Assert.Equal(0, r2.Reminders);
        Assert.Empty(sent2);
    }

    [Fact]
    public async Task Deadline_without_attendance_fines_teacher_and_escalates_once()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var (r, sent) = await RunAsync(db, At(14, 16));
        Assert.Equal(1, r.Fines);
        Assert.Equal(1, r.Escalations);
        var fine = Assert.Single(db.Context.TeacherAttendanceFines.AsNoTracking().ToList());
        Assert.Equal(s.Teacher.Id, fine.TeacherId);
        Assert.Equal(20_000m, fine.Amount);
        Assert.Equal(Today[..7], fine.Month);
        Assert.Equal(AttendanceDiscipline.FineActive, fine.Status);
        Assert.Contains(sent, x => x.Chat == 111 && x.Text.StartsWith("❗️"));
        Assert.Contains(sent, x => x.Chat == 222 && x.Text.StartsWith("⚠️ Davomat olinmadi"));
        Assert.DoesNotContain(sent, x => x.Chat == 333);   // staff without classes.notAttended
        Assert.Single(db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityType == AttendanceFineAudit.EntityType).ToList());

        var (r2, sent2) = await RunAsync(db, At(14, 30));
        Assert.Equal(new AttendanceDisciplineService.RunResult(0, 0, 0), r2);
        Assert.Empty(sent2);
        Assert.Single(db.Context.TeacherAttendanceFines.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Attendance_taken_at_plus_12_means_no_fine_and_no_escalation()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = Today, Period = 1,
            Conducted = true, AttendanceTakenAt = At(14, 12),
        });
        db.Context.SaveChanges();

        var (r, sent) = await RunAsync(db, At(14, 20));
        Assert.Equal(new AttendanceDisciplineService.RunResult(0, 0, 0), r);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Late_attendance_still_fined_and_time_copied()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = Today, Period = 1,
            Conducted = true, AttendanceTakenAt = At(14, 40),
        });
        db.Context.SaveChanges();

        var (r, _) = await RunAsync(db, At(14, 45));
        Assert.Equal(1, r.Fines);
        Assert.Equal(At(14, 40), db.Context.TeacherAttendanceFines.AsNoTracking().Single().AttendanceTakenAt);
    }

    [Fact]
    public async Task Substitute_teaching_that_day_is_fined()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = s.Group.Id, OriginalTeacherId = s.Teacher.Id, SubstituteTeacherId = s.Substitute.Id,
            Date = Today, EndDate = Today, SelectedDates = [Today], IsActive = true,
        });
        db.Context.SaveChanges();

        await RunAsync(db, At(14, 16));
        Assert.Equal(s.Substitute.Id, db.Context.TeacherAttendanceFines.AsNoTracking().Single().TeacherId);
    }

    [Fact]
    public async Task Yesterday_missed_fine_is_written_after_downtime_without_messages()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db, yesterdayTaken: false);

        // 09:00 today — today's lesson has not started; yesterday's lesson was never taken.
        var (r, sent) = await RunAsync(db, At(9, 0));
        Assert.Equal(1, r.Fines);
        Assert.Equal(0, r.Escalations);
        Assert.Empty(sent);
        Assert.Equal(Yesterday, db.Context.TeacherAttendanceFines.AsNoTracking().Single().LessonDate);
    }

    [Fact]
    public async Task Group_without_expected_students_gets_nothing()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db);
        db.Context.StudentGroups.RemoveRange(db.Context.StudentGroups);
        db.Context.SaveChanges();

        var (r, sent) = await RunAsync(db, At(14, 16));
        Assert.Equal(new AttendanceDisciplineService.RunResult(0, 0, 0), r);
        Assert.Empty(sent);
    }

    /* =========================================================================================
     *  2b) NOT RETROACTIVE — AttendanceDisciplineSince / AttendanceFineSince (review fix 2026-10-12)
     * ========================================================================================= */

    [Fact]
    public void Lesson_with_deadline_before_discipline_since_is_skipped_entirely()
    {
        // Discipline switched on at 14:30 — the 14:00 lesson (deadline 14:15) gets nothing.
        var d = AttendanceDiscipline.Decide(Today, "14:00", At(14, 31), null, Settings, true, disciplineSince: At(14, 30));
        Assert.Null(d.Start);
        Assert.False(d.FineDue);
        Assert.False(d.EscalateDue);
        Assert.Empty(d.DueReminders);

        // Switched on at 14:10 — the deadline (14:15) is after it: the lesson is handled normally.
        var later = AttendanceDiscipline.Decide(Today, "14:00", At(14, 16), null, Settings, true, disciplineSince: At(14, 10));
        Assert.True(later.FineDue);
        Assert.True(later.EscalateDue);
    }

    [Fact]
    public void Fine_since_blocks_only_the_fine_not_the_escalation()
    {
        var d = AttendanceDiscipline.Decide(Today, "14:00", At(14, 20), null, Settings, true,
            disciplineSince: At(8, 0), fineSince: At(14, 18));
        Assert.True(d.Late);
        Assert.True(d.EscalateDue);
        Assert.False(d.FineDue);

        // Yesterday's lesson, fine switched on this morning — never fined.
        Assert.False(AttendanceDiscipline.Decide(Yesterday, "14:00", At(9, 0), null, Settings, false, fineSince: At(8, 0)).FineDue);
    }

    [Fact]
    public void Since_is_stamped_on_false_to_true_cleared_on_off_kept_while_on()
    {
        var now = At(10, 0);
        var earlier = At(8, 0, dayOffset: -3);
        Assert.Equal(now, AttendanceSettingsRules.Since(wasOn: false, isOn: true, current: earlier, now));
        Assert.Null(AttendanceSettingsRules.Since(wasOn: true, isOn: false, current: earlier, now));
        Assert.Equal(earlier, AttendanceSettingsRules.Since(wasOn: true, isOn: true, current: earlier, now));
        // A legacy row that is on but has no moment gets one on the next save (never "unbounded" again).
        Assert.Equal(now, AttendanceSettingsRules.Since(wasOn: true, isOn: true, current: null, now));

        var meta = new CenterMeta { AttendanceDisciplineEnabled = true, AttendanceDisciplineSince = earlier };
        AttendanceSettingsRules.Apply(meta, Settings, now);
        Assert.Equal(earlier, meta.AttendanceDisciplineSince);   // discipline stayed on
        Assert.Equal(now, meta.AttendanceFineSince);             // fine just switched on
    }

    [Fact]
    public async Task Enabling_today_fires_no_burst_for_earlier_lessons()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db, yesterdayTaken: false);
        var meta = db.Context.CenterMeta.Single();
        meta.AttendanceDisciplineSince = At(14, 30);   // switched on after today's 14:15 deadline
        meta.AttendanceFineSince = At(14, 30);
        db.Context.SaveChanges();

        // Yesterday's lesson was never taken and today's deadline already passed — still no fine/escalation/reminder.
        var (r, sent) = await RunAsync(db, At(14, 31));
        Assert.Equal(new AttendanceDisciplineService.RunResult(0, 0, 0), r);
        Assert.Empty(sent);
        Assert.Empty(db.Context.TeacherAttendanceFines.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Fine_switched_on_later_still_escalates_but_does_not_fine()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db, yesterdayTaken: false);
        var meta = db.Context.CenterMeta.Single();
        meta.AttendanceDisciplineSince = At(8, 0, dayOffset: -7);
        meta.AttendanceFineSince = At(14, 16);   // fine switched on after today's deadline
        db.Context.SaveChanges();

        var (r, sent) = await RunAsync(db, At(14, 17));
        Assert.Equal(0, r.Fines);
        Assert.Equal(1, r.Escalations);
        Assert.Contains(sent, x => x.Chat == 222 && x.Text.StartsWith("⚠️ Davomat olinmadi"));
        Assert.Empty(db.Context.TeacherAttendanceFines.AsNoTracking().ToList());
    }

    /// <summary>Any save that carries a NEW fine fails with a non-race DbUpdateException (e.g. disk full).</summary>
    private sealed class FailFineInsert : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TeacherAttendanceFine>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated: disk full");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Non_race_fine_insert_failure_is_logged_not_swallowed()
    {
        using var db = TestDb.Sqlite(new FailFineInsert());
        SeedDb(db);
        var log = new CapturingLogger<AttendanceDisciplineService>();

        var r = await AttendanceDisciplineService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()),
            null, At(14, 16), default, log);

        Assert.Equal(0, r.Fines);
        Assert.Contains(log.Warnings, w => w.StartsWith("Davomat jarimasi yozilmadi"));
        Assert.Empty(db.Context.TeacherAttendanceFines.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Existing_fine_is_not_rewritten_and_not_logged()
    {
        // The fine of the day already exists (a previous tick or a parallel instance) — nothing new, no warning.
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var log = new CapturingLogger<AttendanceDisciplineService>();
        var (r1, _) = await RunAsync(db, At(14, 16));
        Assert.Equal(1, r1.Fines);

        var r2 = await AttendanceDisciplineService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()),
            null, At(14, 17), default, log);
        Assert.Equal(0, r2.Fines);
        Assert.Empty(log.Warnings);
        Assert.Equal(s.Teacher.Id, db.Context.TeacherAttendanceFines.AsNoTracking().Single().TeacherId);
    }

    /* =========================================================================================
     *  3) Salary
     * ========================================================================================= */

    private static (TestDb Db, Teacher Teacher) SalarySeed()
    {
        var db = TestDb.Sqlite();
        var t = new Teacher { FullName = "Qat'iy", SalaryMode = "fixed", Salary = 1_000_000m, SalaryStartDate = "2026-01-01" };
        db.Context.Teachers.Add(t);
        db.Context.SaveChanges();
        return (db, t);
    }

    private static TeacherAttendanceFine Fine(Teacher t, string date, decimal amount, string status = "active") => new()
    {
        TeacherId = t.Id, GroupId = Guid.NewGuid().ToString(), LessonDate = date, Month = date[..7], LessonStart = "14:00",
        Deadline = DateTime.Parse(date + "T14:15:00"), Amount = amount, Status = status, CreatedAt = DateTime.Parse(date + "T14:16:00"),
    };

    [Fact]
    public async Task Active_fines_reduce_expected_waived_do_not_month_by_lesson_date()
    {
        var (db, t) = SalarySeed();
        using var _ = db;
        db.Context.TeacherAttendanceFines.AddRange(
            Fine(t, "2026-03-10", 20_000m),
            Fine(t, "2026-03-11", 20_000m),
            Fine(t, "2026-03-12", 50_000m, AttendanceDiscipline.FineWaived),
            Fine(t, "2026-04-01", 30_000m));
        db.Context.SaveChanges();

        var l = await SalaryLedger.BuildAsync(db.Context, t, "2026-03", "2026-04");
        var mar = l.Months.Single(m => m.Month == "2026-03");
        var apr = l.Months.Single(m => m.Month == "2026-04");
        Assert.Equal(960_000m, mar.Expected);
        Assert.Equal(40_000m, mar.AttendanceFines);
        Assert.Equal(2, mar.AttendanceFineCount);
        Assert.Equal(970_000m, apr.Expected);
        Assert.Equal(70_000m, l.TotalAttendanceFines);
    }

    [Fact]
    public async Task Fines_are_capped_at_the_month_base()
    {
        var (db, t) = SalarySeed();
        using var _ = db;
        db.Context.TeacherAttendanceFines.Add(Fine(t, "2026-03-10", 5_000_000m));
        db.Context.SaveChanges();

        var mar = (await SalaryLedger.BuildAsync(db.Context, t, "2026-03", "2026-03")).Months.Single();
        Assert.Equal(0m, mar.Expected);
        Assert.Equal(1_000_000m, mar.AttendanceFines);
    }
}
