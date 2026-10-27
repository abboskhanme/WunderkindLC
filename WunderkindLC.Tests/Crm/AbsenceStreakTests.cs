using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// U3d — consecutive unexcused absences (SPEC P3.5): the pure streak rule, the loader, the background run that
/// creates «Bog'lanish kerak» requests (one open request per student), and the profile «Qoldirilgan darslar» builder.
/// </summary>
public class AbsenceStreakTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private const LessonMark U = LessonMark.AbsentUnexcused;
    private const LessonMark E = LessonMark.AbsentExcused;
    private const LessonMark P = LessonMark.Present;
    private const LessonMark L = LessonMark.Late;
    private const LessonMark N = LessonMark.Unmarked;

    private static AbsenceStreaks.Streak S(params LessonMark[] marks) =>
        AbsenceStreaks.Compute(marks.Select((m, i) => ($"2026-09-{i + 1:00}", m)).ToList());

    /* =========================================================================================
     *  1) Pure rule
     * ========================================================================================= */

    [Fact]
    public void Three_unexcused_is_level_3()
    {
        var s = S(P, U, U, U);
        Assert.Equal(3, s.Count);
        Assert.Equal("2026-09-02", s.StartDate);
        Assert.Equal(["2026-09-02", "2026-09-03", "2026-09-04"], s.Dates);
        Assert.Equal(3, AbsenceStreaks.LevelOf(s.Count));
    }

    [Fact]
    public void Excused_absence_breaks_the_streak()
    {
        Assert.Equal(2, S(U, E, U, U).Count);
        Assert.Equal(0, AbsenceStreaks.LevelOf(2));
    }

    [Fact]
    public void Unmarked_lessons_are_skipped()
    {
        Assert.Equal(3, S(U, N, U, U).Count);
        Assert.Equal(3, S(U, U, U, N).Count);
    }

    [Fact]
    public void Present_or_late_breaks_and_level_5()
    {
        Assert.Equal(1, S(U, U, P, U).Count);
        Assert.Equal(0, S(U, U, L).Count);
        Assert.Equal(5, AbsenceStreaks.LevelOf(S(U, U, U, U, U, U).Count));
    }

    [Fact]
    public void Note_text_lists_dates()
    {
        Assert.Equal("A1: ketma-ket 3 ta dars sababsiz qoldirildi (02.09, 03.09, 04.09)",
            AbsenceStreaks.NoteText("A1", S(P, U, U, U)));
    }

    /* =========================================================================================
     *  2) Loader + background run
     * ========================================================================================= */

    private static DateOnly Today => AppClock.Today;
    private static string D(int daysAgo) => Today.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    private sealed record Seed(Student Student, Group G1, Group G2, AbsenceReason Unexcused, AbsenceReason Excused);

    private static Seed SeedDb(TestDb db)
    {
        var c = db.Context;
        var subject = new Subject { Name = "Ingliz tili" };
        var teacher = new Teacher { FullName = "O'qituvchi" };
        Group G(string name) => new()
        {
            Name = name, CourseId = subject.Id, TeacherId = teacher.Id,
            StartDate = Today.AddDays(-90).ToString("yyyy-MM-dd"), Days = [0, 1, 2, 3, 4, 5, 6],
        };
        var g1 = G("A1");
        var g2 = G("B2");
        var st = new Student { FullName = "Ali Valiyev" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        var excused = new AbsenceReason { Name = "Kasal", Short = "K", IsExcused = true };
        c.AddRange(subject, teacher, g1, g2, st, unexcused, excused);
        foreach (var g in new[] { g1, g2 })
            c.StudentGroups.Add(new StudentGroup
            {
                StudentId = st.Id, GroupId = g.Id, IsActive = true, Status = "active",
                JoinedAt = D(80), ActivatedAt = D(80), RecordedAt = D(80),
            });
        c.SaveChanges();
        return new Seed(st, g1, g2, unexcused, excused);
    }

    /// <summary>Conducted lesson with attendance taken; optional mark of the student.</summary>
    private static void Lesson(TestDb db, Seed s, Group g, string date, AbsenceReason? reason = null, bool present = false)
    {
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, Date = date, Period = 1,
            Conducted = true, AttendanceTaken = true, AttendanceTakenAt = DateTime.Parse(date + "T10:00:00"),
        });
        if (reason is not null || present)
            db.Context.JournalEntries.Add(new JournalEntry
            {
                ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, StudentId = s.Student.Id, Date = date, Period = 1,
                ReasonId = reason?.Id, Present = present,
            });
        db.Context.SaveChanges();
    }

    private static Task<AbsenceStreakService.RunResult> RunAsync(TestDb db) =>
        AbsenceStreakService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()), Today);

    [Fact]
    public async Task Loader_keeps_groups_separate_and_attendance_taken_no_mark_is_present()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        Lesson(db, s, s.G1, D(5), s.Unexcused);
        Lesson(db, s, s.G1, D(4), s.Unexcused);
        Lesson(db, s, s.G2, D(3), s.Unexcused);
        Lesson(db, s, s.G1, D(2));                     // taken + no mark = present → breaks G1? (it is the latest)
        Lesson(db, s, s.G2, D(1), s.Unexcused);

        var rows = await AbsenceStreakLoader.ComputeAsync(db.Context, Today);
        Assert.DoesNotContain(rows, r => r.GroupId == s.G1.Id);    // latest G1 lesson = present
        Assert.Equal(2, Assert.Single(rows, r => r.GroupId == s.G2.Id).Streak.Count);
    }

    [Fact]
    public async Task Three_absences_create_exactly_one_request_and_reruns_are_idempotent()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.ActionReasons.Add(new ActionReason { Category = "contact", Label = "Boshqa" });
        db.Context.SaveChanges();
        await AbsenceStreakService.SeedReasonAsync(db.Context);
        Lesson(db, s, s.G1, D(6), s.Unexcused);
        Lesson(db, s, s.G1, D(4), s.Unexcused);
        Lesson(db, s, s.G1, D(2), s.Unexcused);

        var r = await RunAsync(db);
        Assert.Equal(new AbsenceStreakService.RunResult(1, 1, 0), r);
        var req = Assert.Single(db.Context.ContactRequests.AsNoTracking().ToList());
        Assert.Equal(AbsenceStreakService.ReasonLabel, req.ReasonLabel);
        Assert.NotEqual("", req.ReasonId);
        Assert.Contains("A1: ketma-ket 3 ta dars", req.Note);
        Assert.Equal(req.Id, db.Context.AbsenceStreakAlerts.AsNoTracking().Single().ContactRequestId);

        Assert.Equal(new AbsenceStreakService.RunResult(0, 0, 0), await RunAsync(db));
        Assert.Single(db.Context.ContactRequests.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Fifth_absence_adds_a_note_to_the_open_request()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        foreach (var d in new[] { 10, 8, 6 }) Lesson(db, s, s.G1, D(d), s.Unexcused);
        await RunAsync(db);
        foreach (var d in new[] { 4, 2 }) Lesson(db, s, s.G1, D(d), s.Unexcused);

        var r = await RunAsync(db);
        Assert.Equal(new AbsenceStreakService.RunResult(1, 0, 1), r);
        Assert.Single(db.Context.ContactRequests.AsNoTracking().ToList());
        var note = Assert.Single(db.Context.ContactAttempts.AsNoTracking().Where(a => a.Type == ContactAttemptTypes.Note).ToList());
        Assert.Contains("ketma-ket 5 ta dars", note.Response);
    }

    [Fact]
    public async Task Closed_request_then_level_5_opens_a_new_request()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        foreach (var d in new[] { 10, 8, 6 }) Lesson(db, s, s.G1, D(d), s.Unexcused);
        await RunAsync(db);
        var first = db.Context.ContactRequests.Single();
        first.Status = ContactStatuses.Done;
        db.Context.SaveChanges();
        foreach (var d in new[] { 4, 2 }) Lesson(db, s, s.G1, D(d), s.Unexcused);

        var r = await RunAsync(db);
        Assert.Equal(1, r.Requests);
        Assert.Equal(2, db.Context.ContactRequests.AsNoTracking().Count());
    }

    [Fact]
    public async Task Stale_streak_creates_the_alert_but_no_request()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        foreach (var d in new[] { 30, 25, 20 }) Lesson(db, s, s.G1, D(d), s.Unexcused);

        var r = await RunAsync(db);
        Assert.Equal(new AbsenceStreakService.RunResult(1, 0, 0), r);
        Assert.Empty(db.Context.ContactRequests.AsNoTracking().ToList());
        Assert.Null(db.Context.AbsenceStreakAlerts.AsNoTracking().Single().ContactRequestId);
    }

    [Fact]
    public async Task Kpi_streak_norm_counts_only_fresh_streaks_like_the_contact_rule()
    {
        // Review fix (2026-10-12): KPI «Ketma-ket 3 dars kelmaganlar» uses the same 14-day freshness as the automatic
        // «Bog'lanish kerak» — a student who stopped coming a month ago (still a member) does not keep the norm red.
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        foreach (var d in new[] { 30, 25, 20 }) Lesson(db, s, s.G1, D(d), s.Unexcused);
        var today = Today.ToString("yyyy-MM-dd");

        var stale = await Application.Services.Kpi.KpiMetricsService.AbsenceStreaksAsync(db.Context, today, default);
        Assert.Empty(stale.Three);

        foreach (var d in new[] { 5, 3, 1 }) Lesson(db, s, s.G2, D(d), s.Unexcused);
        var fresh = await Application.Services.Kpi.KpiMetricsService.AbsenceStreaksAsync(db.Context, today, default);
        Assert.Equal(s.Student.Id, Assert.Single(fresh.Three).Id);
    }

    [Fact]
    public async Task Two_groups_at_level_3_in_one_run_mean_one_action()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        foreach (var d in new[] { 6, 4, 2 })
        {
            Lesson(db, s, s.G1, D(d), s.Unexcused);
            Lesson(db, s, s.G2, D(d), s.Unexcused);
        }
        var r = await RunAsync(db);
        Assert.Equal(new AbsenceStreakService.RunResult(2, 1, 0), r);
        Assert.Single(db.Context.ContactRequests.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Excused_absence_in_between_means_no_alert()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        Lesson(db, s, s.G1, D(8), s.Unexcused);
        Lesson(db, s, s.G1, D(6), s.Unexcused);
        Lesson(db, s, s.G1, D(4), s.Excused);
        Lesson(db, s, s.G1, D(2), s.Unexcused);

        Assert.Equal(new AbsenceStreakService.RunResult(0, 0, 0), await RunAsync(db));
    }

    // ---- 2026-10-27: «Sozlamalar → Servis» — necha dars (standart 3), yoqish/o'chirish (egasining talabi) ----

    private static void ServiceSettings(TestDb db, bool enabled, int lessons)
    {
        var meta = db.Context.CenterMeta.FirstOrDefault();
        if (meta is null)
        {
            meta = new CenterMeta();
            db.Context.CenterMeta.Add(meta);
        }
        meta.ContactAbsenceAutoEnabled = enabled;
        meta.ContactAbsenceLessons = lessons;
        db.Context.SaveChanges();
    }

    [Fact]
    public void Contact_levels_follow_the_setting_and_never_repeat_a_lower_one()
    {
        Assert.Equal([3, 5], AbsenceStreaks.ContactLevels(3));
        Assert.Equal([1, 5], AbsenceStreaks.ContactLevels(1));
        Assert.Equal([7], AbsenceStreaks.ContactLevels(7));
        Assert.Equal([3, 5], AbsenceStreaks.ContactLevels(0));   // buzuq qiymat — standart 3
        Assert.Empty(AbsenceStreaks.ContactLevelsToRaise(1, 0, 2));
        Assert.Equal([2], AbsenceStreaks.ContactLevelsToRaise(4, 0, 2));
        Assert.Equal([2, 5], AbsenceStreaks.ContactLevelsToRaise(6, 0, 2));
        Assert.Equal([5], AbsenceStreaks.ContactLevelsToRaise(5, 2, 2));
        // chegara 3 dan 2 ga PASAYTIRILDI: 3-darajada ishlangan seriya 2 bilan qayta ochilmaydi, 5 da hamon izoh/talab
        Assert.Empty(AbsenceStreaks.ContactLevelsToRaise(4, 3, 2));
        Assert.Equal([5], AbsenceStreaks.ContactLevelsToRaise(5, 3, 2));
        // qizil ro'yxat / KPI darajasi sozlamaga bog'liq emas
        Assert.Equal(0, AbsenceStreaks.LevelOf(2));
    }

    [Fact]
    public async Task Default_is_three_lessons_two_are_not_enough()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        Lesson(db, s, s.G1, D(4), s.Unexcused);
        Lesson(db, s, s.G1, D(2), s.Unexcused);
        Assert.Equal(new AbsenceStreakService.RunResult(0, 0, 0), await RunAsync(db));
    }

    [Fact]
    public async Task Disabled_setting_creates_nothing()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        ServiceSettings(db, enabled: false, lessons: 3);
        foreach (var d in new[] { 6, 4, 2 }) Lesson(db, s, s.G1, D(d), s.Unexcused);
        Assert.Equal(new AbsenceStreakService.RunResult(0, 0, 0), await RunAsync(db));
        Assert.Empty(db.Context.ContactRequests.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Setting_two_lessons_opens_a_request_with_the_reason_and_note()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        await AbsenceStreakService.SeedReasonAsync(db.Context);
        ServiceSettings(db, enabled: true, lessons: 2);
        Lesson(db, s, s.G1, D(4), s.Unexcused);
        Lesson(db, s, s.G1, D(2), s.Unexcused);

        Assert.Equal(new AbsenceStreakService.RunResult(1, 1, 0), await RunAsync(db));
        var req = Assert.Single(db.Context.ContactRequests.AsNoTracking().ToList());
        Assert.Equal(AbsenceStreakService.ReasonLabel, req.ReasonLabel);
        Assert.Equal(s.Student.FullName, req.StudentName);
        Assert.Contains("A1: ketma-ket 2 ta dars", req.Note);
        Assert.Equal(2, db.Context.AbsenceStreakAlerts.AsNoTracking().Single().Level);
    }

    [Fact]
    public async Task Lowering_the_setting_does_not_reopen_a_streak_raised_at_3()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        ServiceSettings(db, enabled: true, lessons: 2);
        foreach (var d in new[] { 6, 4, 2 }) Lesson(db, s, s.G1, D(d), s.Unexcused);
        // deploydan oldingi qator: shu seriya 3-darajada allaqachon ishlangan
        db.Context.AbsenceStreakAlerts.Add(new AbsenceStreakAlert
        {
            StudentId = s.Student.Id, GroupId = s.G1.Id, StreakStart = D(6), Level = 3, CreatedAt = AppClock.Now,
        });
        db.Context.SaveChanges();

        Assert.Equal(new AbsenceStreakService.RunResult(0, 0, 0), await RunAsync(db));
        Assert.Empty(db.Context.ContactRequests.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Seed_reason_is_created_once_and_reused()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await AbsenceStreakService.SeedReasonAsync(db.Context));
        Assert.False(await AbsenceStreakService.SeedReasonAsync(db.Context));
        var reason = Assert.Single(db.Context.ActionReasons.AsNoTracking().Where(r => r.Category == "contact").ToList());
        Assert.Equal(reason.Id, db.Context.CenterMeta.AsNoTracking().Single().AbsenceStreakReasonId);
    }

    /* =========================================================================================
     *  3) Profile «Qoldirilgan darslar»
     * ========================================================================================= */

    [Fact]
    public async Task Profile_absences_list_every_missed_date_and_counts_match()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var late = new AbsenceReason { Name = "Kech keldi", Short = "KK", IsLate = true };
        db.Context.AbsenceReasons.Add(late);
        db.Context.SaveChanges();
        Lesson(db, s, s.G1, D(6), s.Unexcused);
        Lesson(db, s, s.G2, D(5), s.Excused);
        Lesson(db, s, s.G1, D(4), late);
        Lesson(db, s, s.G1, D(3));                       // taken + no mark = present
        Lesson(db, s, s.G2, D(2), present: true);

        var (from, to, error) = StudentAbsences.NormalizeRange(null, null, Today);
        Assert.Null(error);
        var dto = await StudentAbsences.BuildAsync(db.Context, s.Student.Id, from, to);

        Assert.Equal(5, dto.Counts.Held);
        Assert.Equal(3, dto.Counts.Attended);
        Assert.Equal(1, dto.Counts.Late);
        Assert.Equal(1, dto.Counts.AbsentExcused);
        Assert.Equal(1, dto.Counts.AbsentUnexcused);
        Assert.Equal(60, dto.Counts.Percent);
        Assert.Equal([D(4), D(5), D(6)], dto.Items.Select(i => i.Date));          // newest first
        Assert.Equal(["late", "absent_excused", "absent_unexcused"], dto.Items.Select(i => i.Kind));
        Assert.Equal(3, dto.ByReason.Sum(r => r.Count));
        Assert.True(dto.ByReason.Single(r => r.Name == "Kasal").IsExcused);
    }

    [Fact]
    public void Profile_range_defaults_and_limits()
    {
        var (from, to, _) = StudentAbsences.NormalizeRange(null, null, new DateOnly(2026, 10, 12));
        Assert.Equal("2026-04-16", from);
        Assert.Equal("2026-10-12", to);
        Assert.NotNull(StudentAbsences.NormalizeRange("2025-01-01", "2026-10-12", new DateOnly(2026, 10, 12)).Error);
        Assert.NotNull(StudentAbsences.NormalizeRange("2026-10-12", "2026-10-01", new DateOnly(2026, 10, 12)).Error);
    }
}
