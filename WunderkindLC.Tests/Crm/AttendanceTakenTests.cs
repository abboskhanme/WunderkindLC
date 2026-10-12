using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// U3a — "attendance taken" (SPEC P3.1): <see cref="LessonNote.AttendanceTakenAt"/> = FIRST present/absent mark,
/// <see cref="LessonNote.AttendanceTaken"/> = COMPLETE (bulk, or every expected student marked).
/// </summary>
public class AttendanceTakenTests
{
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    private sealed record Seed(Group Group, Student A, Student B, AbsenceReason Absent);

    private static Seed SeedDb(TestDb db)
    {
        var subject = new Subject { Name = "Ingliz tili" };
        var teacher = new Teacher { FullName = "O'qituvchi" };
        var g = new Group
        {
            Name = "A1", CourseId = subject.Id, TeacherId = teacher.Id,
            StartDate = AppClock.Today.AddMonths(-3).ToString("yyyy-MM-dd"),
            Days = [0, 1, 2, 3, 4, 5, 6],
        };
        var a = new Student { FullName = "Ali" };
        var b = new Student { FullName = "Bobur" };
        var reason = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.Context.AddRange(subject, teacher, g, a, b, reason);
        var since = AppClock.Today.AddMonths(-2).ToString("yyyy-MM-dd");
        foreach (var s in new[] { a, b })
            db.Context.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
                JoinedAt = since, ActivatedAt = since, RecordedAt = since,
            });
        db.Context.SaveChanges();
        return new Seed(g, a, b, reason);
    }

    private static SetJournalEntryRequest Req(Seed s, Student st, int? grade = null, string? reasonId = null, bool present = false) =>
        new(s.Group.Id, s.Group.CourseId!, 1, st.Id, Today, 1, grade, reasonId, Present: present);

    private static Task<LessonNote> NoteAsync(TestDb db, Seed s) =>
        db.Context.LessonNotes.AsNoTracking().SingleAsync(n => n.ClassId == s.Group.Id && n.Date == Today);

    [Fact]
    public async Task First_single_mark_stamps_AttendanceTakenAt_once()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, present: true), actorId: "user-1");
        var first = (await NoteAsync(db, s)).AttendanceTakenAt;
        Assert.NotNull(first);
        Assert.Equal("user-1", (await NoteAsync(db, s)).AttendanceTakenById);

        await Task.Delay(20);
        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, reasonId: s.Absent.Id), actorId: "user-2");
        var note = await NoteAsync(db, s);
        Assert.Equal(first, note.AttendanceTakenAt);          // never moves
        Assert.Equal("user-1", note.AttendanceTakenById);
    }

    [Fact]
    public async Task Grade_alone_does_not_stamp_attendance()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, grade: 5));
        var note = await NoteAsync(db, s);
        Assert.True(note.Conducted);
        Assert.Null(note.AttendanceTakenAt);
        Assert.False(note.AttendanceTaken);
    }

    [Fact]
    public async Task Marking_every_expected_student_completes_the_lesson()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, present: true));
        Assert.False((await NoteAsync(db, s)).AttendanceTaken);

        // A grade counts for completeness (the student was in the lesson) — only not for the FIRST mark.
        await JournalService.SetEntryAsync(db.Context, Req(s, s.B, grade: 4));
        Assert.True((await NoteAsync(db, s)).AttendanceTaken);
    }

    [Fact]
    public async Task Payment_hidden_student_is_not_expected_on_the_teacher_path()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.CenterMeta.Add(new CenterMeta { JournalHideUnpaidPrevMonth = true });
        var prevMonth = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        db.Context.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.B.Id, GroupId = s.Group.Id, Month = prevMonth, Amount = 500_000m, Date = prevMonth + "-01",
        });
        db.Context.SaveChanges();

        // Teacher marks the only VISIBLE student → complete.
        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, present: true), teacher: true);
        Assert.True((await NoteAsync(db, s)).AttendanceTaken);
    }

    [Fact]
    public async Task Payment_hidden_student_still_counts_on_the_admin_path()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.CenterMeta.Add(new CenterMeta { JournalHideUnpaidPrevMonth = true });
        var prevMonth = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        db.Context.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.B.Id, GroupId = s.Group.Id, Month = prevMonth, Amount = 500_000m, Date = prevMonth + "-01",
        });
        db.Context.SaveChanges();

        await JournalService.SetEntryAsync(db.Context, Req(s, s.A, present: true));
        Assert.False((await NoteAsync(db, s)).AttendanceTaken);
    }

    [Fact]
    public async Task Bulk_attendance_sets_both_stamps()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        await JournalService.BulkAttendanceAsync(db.Context,
            new BulkAttendanceRequest(s.Group.Id, s.Group.CourseId!, Today, 1, [s.A.Id, s.B.Id], null),
            actorId: "admin-1");
        var note = await NoteAsync(db, s);
        Assert.True(note.AttendanceTaken);
        Assert.NotNull(note.AttendanceTakenAt);
        Assert.Equal("admin-1", note.AttendanceTakenById);
        Assert.NotNull(await AttendanceTaking.FirstMarkAsync(db.Context, s.Group.Id, Today));
    }

    [Fact]
    public async Task First_mark_lookup_is_subject_and_period_agnostic()
    {
        using var db = TestDb.Sqlite();
        db.Context.LessonNotes.AddRange(
            new LessonNote { ClassId = "g1", SubjectId = "x", Date = Today, Period = 2, Conducted = true, AttendanceTakenAt = AppClock.Now },
            new LessonNote { ClassId = "g2", SubjectId = "y", Date = Today, Period = 1, Conducted = true });
        db.Context.SaveChanges();

        var map = await AttendanceTaking.FirstMarksAsync(db.Context, Today, ["g1", "g2"]);
        Assert.True(map.ContainsKey("g1"));
        Assert.False(map.ContainsKey("g2"));
    }

    /* =========================================================================================
     *  Journal summaries — late = attended, unmarked excluded (StudentJournalBuilder)
     * ========================================================================================= */

    [Fact]
    public async Task Profile_journal_counts_late_as_attended_and_skips_unmarked_lessons()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var late = new AbsenceReason { Name = "Kech keldi", Short = "KK", IsLate = true };
        db.Context.AbsenceReasons.Add(late);
        var month = AppClock.Today.ToString("yyyy-MM");
        string D(int d) => $"{month}-{d:00}";
        // day 1: attendance taken, A late; day 2: attendance taken, A no mark (= present);
        // day 3: conducted by a grade of B only, A no mark (= UNMARKED); day 4: A absent.
        db.Context.LessonNotes.AddRange(
            new LessonNote { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = D(1), Period = 1, Conducted = true, AttendanceTaken = true },
            new LessonNote { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = D(2), Period = 1, Conducted = true, AttendanceTaken = true },
            new LessonNote { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = D(3), Period = 1, Conducted = true },
            new LessonNote { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, Date = D(4), Period = 1, Conducted = true });
        db.Context.JournalEntries.AddRange(
            new JournalEntry { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, StudentId = s.A.Id, Date = D(1), Period = 1, ReasonId = late.Id },
            new JournalEntry { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, StudentId = s.B.Id, Date = D(3), Period = 1, Grade = 5 },
            new JournalEntry { ClassId = s.Group.Id, SubjectId = s.Group.CourseId!, Quarter = 1, StudentId = s.A.Id, Date = D(4), Period = 1, ReasonId = s.Absent.Id });
        db.Context.SaveChanges();

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.A.Id, D(1), D(4), null);
        Assert.NotNull(dto);
        Assert.Equal(3, dto!.Summary.Held);         // day 3 unmarked → excluded
        Assert.Equal(2, dto.Summary.Attended);      // late + default present
        Assert.Equal(1, dto.Summary.Late);
        Assert.Equal(1, dto.Summary.Absent);
        Assert.Equal(67, dto.Summary.AttendancePct);
    }

    /* =========================================================================================
     *  Settings rules (SPEC P3.2)
     * ========================================================================================= */

    private static AttendanceSettingsDto Ok() =>
        new(true, 15, [5, 10], true, true, 20_000m, 1, "late_only");

    [Fact]
    public void Settings_valid_defaults_pass()
    {
        Assert.Null(AttendanceSettingsRules.Validate(Ok()));
        Assert.Null(AttendanceSettingsRules.Validate(Ok() with { ReminderMinutes = [], FineEnabled = false, FineAmount = 0 }));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void Settings_deadline_out_of_range(int deadline) =>
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { DeadlineMinutes = deadline, ReminderMinutes = [] }));

    [Fact]
    public void Settings_reminders_must_be_ascending_within_deadline_and_at_most_three()
    {
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { ReminderMinutes = [10, 5] }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { ReminderMinutes = [5, 16] }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { ReminderMinutes = [1, 2, 3, 4] }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { ReminderMinutes = [0] }));
    }

    [Fact]
    public void Settings_fine_must_be_positive_when_enabled_and_notice_fields_bounded()
    {
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { FineAmount = 0 }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { FineAmount = 10_000_001m }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { AbsentNoticeDelayMinutes = 181 }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Ok() with { AbsentNoticeApproval = "maybe" }));
    }

    [Fact]
    public void Settings_reminder_csv_round_trip()
    {
        Assert.Equal([5, 10], AttendanceSettingsRules.ParseReminders("5, 10"));
        Assert.Empty(AttendanceSettingsRules.ParseReminders(""));
        Assert.Equal("5,10", AttendanceSettingsRules.FormatReminders([5, 10]));
        var defaults = AttendanceSettingsRules.FromMeta(null);
        Assert.Equal(15, defaults.DeadlineMinutes);
        Assert.Equal("late_only", defaults.AbsentNoticeApproval);
    }
}
