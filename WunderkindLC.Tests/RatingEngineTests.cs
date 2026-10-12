using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Tests.Mcp;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// RATING ENGINE on SQLite (docs/SPEC-crm-upgrade.md P4.3/P4.7): engine = formula on seeded data, the student
/// block and cabinet slices, the date-range ball overload, a FIXED query count that never reads the journal
/// row by row, and the re-implemented legacy <see cref="RatingService.SchoolAsync"/> = old algorithm = MCP.
/// </summary>
public class RatingEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static RatingPeriod Period(string kind, string key) => RatingPeriods.Resolve(kind, key, Today).Period!;

    // ------------------------------------------------------------------ seed

    /// <summary>Records every SQL command (to count queries and look for whole-journal reads).</summary>
    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r)
        { Commands.Add(c.CommandText); return r; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Commands.Add(c.CommandText); return ValueTask.FromResult(r); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r)
        { Commands.Add(c.CommandText); return r; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default)
        { Commands.Add(c.CommandText); return ValueTask.FromResult(r); }
    }

    private sealed class Db : IDisposable
    {
        private readonly SqliteConnection _conn = new("Filename=:memory:");
        public Capture Capture { get; } = new();
        public AppDbContext Context { get; }

        public Db()
        {
            _conn.Open();
            Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_conn).AddInterceptors(Capture).Options);
            Context.Database.EnsureCreated();
        }

        public void Dispose() { Context.Dispose(); _conn.Dispose(); }
    }

    /// <summary>
    /// g1 (Ingliz tili, Karimova): a, b members (+ d with no data); g2 (Matematika): c.
    /// October 2026:
    ///   tests in g1: T1 max 30 → a 30 (100 %), b 15 (50 %), z 30 (archived); T2 max 10 → a 8 (80 %);
    ///   T3 max 0 → ignored. a T = 90, b T = 50.
    ///   ball g1: a = 5 + 4 (manual) + 1 adjustment = 10; b = 5 + 1 criteria (the criteria-made grade 1 is NOT
    ///   counted) = 6 → best 10: a B = 100, b B = 60.
    ///   a = 0.6·90 + 0.4·100 = 94; b = 0.6·50 + 0.4·60 = 54. g2: c ball 3 → only B = 100.
    ///   Centre: c 100 (1), a 94 (2), b 54 (3).
    /// September 2026: T0 max 20 → a 10 (50 %), b 20 (100 %); ball a = 5 + 2 adjustment = 7, b = 0 →
    ///   a = 0.6·50 + 0.4·100 = 70 (1), b = 0.6·100 + 0 = 60 (2).
    /// </summary>
    private static void Seed(AppDbContext db)
    {
        db.Subjects.AddRange(new Subject { Id = "s1", Name = "Ingliz tili" }, new Subject { Id = "s2", Name = "Matematika" });
        db.Teachers.Add(new Teacher { Id = "t1", FullName = "Karimova Dilnoza" });
        db.Classes.AddRange(
            new Group { Id = "g1", Name = "English A1", CourseId = "s1", TeacherId = "t1" },
            new Group { Id = "g2", Name = "Math 5", CourseId = "s2" });
        db.Students.AddRange(
            new Student { Id = "a", FullName = "Aliyev Ali" },
            new Student { Id = "b", FullName = "Botirov Bobur" },
            new Student { Id = "c", FullName = "Choriyev Chori" },
            new Student { Id = "d", FullName = "Davronov Dilshod" },
            new Student { Id = "z", FullName = "Zokirov Zafar", IsArchived = true });
        foreach (var (s, g) in new[] { ("a", "g1"), ("b", "g1"), ("d", "g1"), ("c", "g2"), ("z", "g1") })
            db.StudentGroups.Add(new StudentGroup
            {
                StudentId = s, GroupId = g, IsActive = true, Status = "active",
                JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01", RecordedAt = "2026-09-01",
            });

        db.TestResults.AddRange(
            new TestResult { Id = "T0", GroupId = "g1", Name = "Unit 1", Date = "2026-09-20", MaxScore = 20, CreatedAt = "2026-09-20T10:00:00" },
            new TestResult { Id = "T1", GroupId = "g1", Name = "Unit 2", Date = "2026-10-03", MaxScore = 30, CreatedAt = "2026-10-03T10:00:00" },
            new TestResult { Id = "T2", GroupId = "g1", Name = "Unit 3", Date = "2026-10-08", MaxScore = 10, CreatedAt = "2026-10-08T10:00:00" },
            new TestResult { Id = "T3", GroupId = "g1", Name = "Bo'sh", Date = "2026-10-09", MaxScore = 0, CreatedAt = "2026-10-09T10:00:00" });
        db.TestScores.AddRange(
            new TestScore { TestResultId = "T0", StudentId = "a", Score = 10 },
            new TestScore { TestResultId = "T0", StudentId = "b", Score = 20 },
            new TestScore { TestResultId = "T1", StudentId = "a", Score = 30 },
            new TestScore { TestResultId = "T1", StudentId = "b", Score = 15 },
            new TestScore { TestResultId = "T1", StudentId = "z", Score = 30 },
            new TestScore { TestResultId = "T2", StudentId = "a", Score = 8 },
            new TestScore { TestResultId = "T3", StudentId = "b", Score = 5 });

        void Grade(string s, string g, string date, int grade, bool fromCriteria = false, int period = 1) =>
            db.JournalEntries.Add(new JournalEntry
            {
                StudentId = s, ClassId = g, SubjectId = g == "g1" ? "s1" : "s2", Date = date, Period = period,
                Grade = grade, GradeFromCriteria = fromCriteria,
            });
        Grade("a", "g1", "2026-10-05", 5);
        Grade("a", "g1", "2026-10-07", 4);
        Grade("b", "g1", "2026-10-05", 5);
        Grade("b", "g1", "2026-10-07", 1, fromCriteria: true);
        Grade("c", "g2", "2026-10-06", 3);
        Grade("a", "g1", "2026-09-22", 5);
        db.CriterionGrades.AddRange(
            new CriterionGrade { GroupId = "g1", StudentId = "b", CriterionId = "k", Date = "2026-10-07", Done = true },
            new CriterionGrade { GroupId = "g1", StudentId = "a", CriterionId = "k", Date = "2026-10-07", Done = false });
        db.StudentBallAdjustments.AddRange(
            new StudentBallAdjustment { StudentId = "a", GroupId = "g1", Delta = 1, Reason = "x", CreatedAt = "2026-10-05T10:00:00" },
            new StudentBallAdjustment { StudentId = "a", GroupId = "g1", Delta = 2, Reason = "x", CreatedAt = "2026-09-10T10:00:00" });
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ engine = formula

    [Fact]
    public async Task Engine_computes_the_month_by_the_formula()
    {
        using var db = new Db();
        Seed(db.Context);

        var r = await RatingEngine.ComputeAsync(db.Context, Period("month", "2026-10"));

        Assert.Equal(60, r.TestWeight);   // no CenterMeta row → owner default
        var a = r.RowsByGroup["g1"].Single(x => x.StudentId == "a");
        var b = r.RowsByGroup["g1"].Single(x => x.StudentId == "b");
        Assert.Equal((90.0, 2, 10, 100.0, 94.0, 1), (a.TestAvg!.Value, a.TestCount, a.Ball, a.BallNorm!.Value, a.Score!.Value, a.Rank!.Value));
        Assert.Equal((50.0, 1, 6, 60.0, 54.0, 2), (b.TestAvg!.Value, b.TestCount, b.Ball, b.BallNorm!.Value, b.Score!.Value, b.Rank!.Value));
        Assert.DoesNotContain(r.Groups, x => x.StudentId == "z");   // archived
        Assert.DoesNotContain(r.Groups, x => x.StudentId == "d");   // no data in the period

        Assert.Equal(new[] { "c", "a", "b" }, r.Centre.Select(x => x.StudentId));
        Assert.Equal(new int?[] { 1, 2, 3 }, r.Centre.Select(x => x.Rank));
        Assert.Equal(3, r.CentreTotal);
        Assert.Equal(2, r.RankedInGroup["g1"]);
    }

    [Fact]
    public async Task Configured_weight_is_used()
    {
        using var db = new Db();
        Seed(db.Context);
        db.Context.CenterMeta.Add(new CenterMeta { RatingTestWeight = 100 });
        db.Context.SaveChanges();

        var r = await RatingEngine.ComputeAsync(db.Context, Period("month", "2026-10"));
        Assert.Equal(100, r.TestWeight);
        Assert.Equal(90.0, r.RowsByGroup["g1"].Single(x => x.StudentId == "a").Score);
    }

    [Fact]
    public async Task Quarter_and_year_merge_months()
    {
        using var db = new Db();
        Seed(db.Context);

        var q = await RatingEngine.ComputeAsync(db.Context, Period("quarter", "2026-10"));
        var a = q.RowsByGroup["g1"].Single(x => x.StudentId == "a");
        // Tests 50, 100, 80 → 76.7; ball 5+2 (Sep) + 10 (Oct) = 17.
        Assert.Equal((76.7, 3, 17), (a.TestAvg!.Value, a.TestCount, a.Ball));

        var y = await RatingEngine.ComputeAsync(db.Context, Period("year", "2026"));
        Assert.Equal(3, y.RowsByGroup["g1"].Single(x => x.StudentId == "a").TestCount);
    }

    // ------------------------------------------------------------------ ball date range

    [Fact]
    public async Task Ball_date_range_overload_matches_month_and_bounds_adjustments_by_day()
    {
        using var db = new Db();
        Seed(db.Context);
        db.Context.StudentBallAdjustments.AddRange(
            new StudentBallAdjustment { StudentId = "b", GroupId = "g1", Delta = 3, Reason = "x", CreatedAt = "2026-10-31T23:59:59.500" },
            new StudentBallAdjustment { StudentId = "b", GroupId = "g1", Delta = 7, Reason = "x", CreatedAt = "2026-11-01T00:00:00" });
        db.Context.SaveChanges();

        var byMonth = await StudentBallService.ComputeByGroupAsync(db.Context, null, "2026-10");
        var byRange = await StudentBallService.ComputeByGroupAsync(db.Context, null, "2026-10-01", "2026-10-31");
        Assert.Equal(byMonth.OrderBy(k => k.Key).Select(k => (k.Key, k.Value.Ball)),
            byRange.OrderBy(k => k.Key).Select(k => (k.Key, k.Value.Ball)));
        Assert.Equal(9, byRange[("b", "g1")].Ball);   // 5 + 1 criteria + 3 (last second of Oct); Nov 1 excluded

        var oneDay = await StudentBallService.ComputeByGroupAsync(db.Context, new[] { "g1" }, "2026-10-05", "2026-10-05");
        Assert.Equal(6, oneDay[("a", "g1")].Ball);    // grade 5 + adjustment 1 of that day
        Assert.False(oneDay.ContainsKey(("c", "g2")));
    }

    // ------------------------------------------------------------------ never the whole journal

    [Fact]
    public async Task Query_count_is_fixed_and_the_journal_is_only_aggregated()
    {
        using var db = new Db();
        Seed(db.Context);
        var p = Period("month", "2026-10");

        db.Capture.Commands.Clear();
        await RatingEngine.ComputeAsync(db.Context, p);
        var before = db.Capture.Commands.ToList();

        // 10× more journal rows, scores and tests — the number of queries must not move.
        for (var i = 0; i < 60; i++)
        {
            var sid = "x" + i;
            db.Context.Students.Add(new Student { Id = sid, FullName = "Student " + i });
            for (var d = 1; d <= 9; d++)
                db.Context.JournalEntries.Add(new JournalEntry
                {
                    StudentId = sid, ClassId = "g1", SubjectId = "s1", Date = $"2026-10-0{d}", Period = 1, Grade = d % 5 + 1,
                });
            db.Context.TestScores.Add(new TestScore { TestResultId = "T1", StudentId = sid, Score = i % 30 });
        }
        db.Context.SaveChanges();

        db.Capture.Commands.Clear();
        var r = await RatingEngine.ComputeAsync(db.Context, p);
        var after = db.Capture.Commands.ToList();

        Assert.Equal(before.Count, after.Count);
        Assert.True(after.Count <= 10, $"Too many queries: {after.Count}");
        Assert.Equal(62, r.RankedInGroup["g1"]);
        // Every statement that touches the journal is an aggregate (GROUP BY) — rows never leave the database.
        foreach (var sql in after.Where(s => s.Contains("\"JournalEntries\"", StringComparison.Ordinal)))
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ student block + cabinet

    [Fact]
    public async Task Student_block_has_places_components_history_tests_and_attendance()
    {
        using var db = new Db();
        Seed(db.Context);
        var p = Period("month", "2026-10");

        var block = await RatingEngine.StudentBlockAsync(db.Context, RatingEngine.Direct(db.Context), "a", p, Today);

        Assert.NotNull(block);
        Assert.Equal(("month", "2026-10", "Oktabr 2026"), (block!.Period.Kind, block.Period.Key, block.Period.Label));
        Assert.Equal((2, 3, 94.0, 1), (block.Centre.Rank!.Value, block.Centre.Total, block.Centre.Score!.Value, block.Centre.PrevRank!.Value));
        var g = Assert.Single(block.Groups);
        Assert.Equal(("g1", "English A1", "Ingliz tili", 1, 2), (g.GroupId, g.GroupName, g.CourseName, g.Rank!.Value, g.Total));
        Assert.Equal((90.0, 2, 10, 60), (block.Components.TestAvg!.Value, block.Components.TestCount, block.Components.Ball, block.Components.TestWeight));

        Assert.Equal(12, block.History.Count);
        Assert.Equal(("2026-10", 2, 3), (block.History[^1].Month, block.History[^1].CentreRank!.Value, block.History[^1].CentreTotal));
        Assert.Equal(("2026-09", 1), (block.History[^2].Month, block.History[^2].CentreRank!.Value));
        Assert.Null(block.History[0].CentreRank);

        // Newest first; the place inside a test counts everyone scored (archived "z" too — the test's own ranking).
        Assert.Equal(new[] { "T2", "T1" }, block.Tests.Select(t => t.TestId));
        var t1 = block.Tests[1];
        Assert.Equal((30m, 30m, 100.0, 1, 3), (t1.Solved, t1.Questions, t1.Percent!.Value, t1.RankInTest, t1.Takers));
        Assert.Equal("English A1", t1.GroupName);

        Assert.Equal((0, 0, (int?)null), (block.Attendance.Held, block.Attendance.Attended, block.Attendance.Percent));
    }

    [Fact]
    public async Task Student_without_data_still_sees_the_current_group_unranked()
    {
        using var db = new Db();
        Seed(db.Context);

        var block = await RatingEngine.StudentBlockAsync(db.Context, RatingEngine.Direct(db.Context), "d", Period("month", "2026-10"), Today);
        var g = Assert.Single(block!.Groups);
        Assert.Equal(("g1", (int?)null, 2), (g.GroupId, g.Rank, g.Total));
        Assert.Null(block.Centre.Rank);
        Assert.Equal(3, block.Centre.Total);

        Assert.Null(await RatingEngine.StudentBlockAsync(db.Context, RatingEngine.Direct(db.Context), "nope", Period("month", "2026-10"), Today));
    }

    [Fact]
    public async Task Cabinet_summary_has_top_lists_with_the_own_row()
    {
        using var db = new Db();
        Seed(db.Context);
        var p = Period("month", "2026-10");
        var load = RatingEngine.Direct(db.Context);
        var block = await RatingEngine.StudentBlockAsync(db.Context, load, "b", p, Today);

        var summary = RatingEngine.Summary(block!, await load(p), top: 1);

        var group = Assert.Single(summary.GroupTop);
        Assert.Equal(new[] { ("Aliyev Ali", 1, false), ("Botirov Bobur", 2, true) },
            group.Rows.Select(r => (r.FullName, r.Rank, r.IsMe)));   // top 1 + own row
        Assert.Equal(new[] { ("Choriyev Chori", 1, false), ("Botirov Bobur", 3, true) },
            summary.CentreTop.Select(r => (r.FullName, r.Rank, r.IsMe)));
        Assert.Equal(3, summary.Centre.Rank);   // block fields are carried over
    }

    [Fact]
    public async Task Centre_and_group_tables()
    {
        using var db = new Db();
        Seed(db.Context);
        var r = await RatingEngine.ComputeAsync(db.Context, Period("month", "2026-10"));

        var centre = RatingEngine.Centre(r, null, 1, 2);
        Assert.Equal((3, 2), (centre.Total, centre.Items.Count));
        Assert.Equal("Choriyev Chori", centre.Items[0].FullName);
        Assert.Equal(new[] { ("g1", (int?)1) }, centre.Items[1].Groups.Select(x => (x.Id, x.Rank)));

        var search = RatingEngine.Centre(r, "BOTIR", 1, 50);
        Assert.Equal(("b", 3), (search.Items.Single().StudentId, search.Items.Single().Rank));

        var table = RatingEngine.GroupTable(r, r.GroupInfo["g1"],
            new Dictionary<string, string> { ["a"] = "Aliyev Ali", ["b"] = "Botirov Bobur", ["d"] = "Davronov Dilshod" });
        Assert.Equal(("English A1", "Ingliz tili", "Karimova Dilnoza", 2),
            (table.Group.Name, table.Group.CourseName, table.Group.TeacherName, table.Ranked));
        Assert.Equal(new[] { ("a", (int?)1), ("b", (int?)2), ("d", (int?)null) }, table.Items.Select(i => (i.StudentId, i.Rank)));
    }

    // ------------------------------------------------------------------ legacy SchoolAsync

    /// <summary>
    /// The legacy <c>RatingService.SchoolAsync</c> as it was before the SQL rewrite (it loaded the whole journal into
    /// memory) — kept as the in-memory REFERENCE the SQL implementation must match. Its attendance is the ONE definition
    /// (review fix 2026-10-12): every conducted lesson classified one by one with <see cref="AttendanceRules.Classify(bool,bool,bool,bool,JournalEntry?,AbsenceReason?)"/>,
    /// the implicit «keldi» only when attendance was taken AND the day is inside the membership window
    /// (<see cref="MembershipAttendance.ImplicitWindow"/>). Before, it was (conducted − absent) ÷ conducted.
    /// </summary>
    private static async Task<List<StudentRatingRowDto>> LegacySchoolAsync(AppDbContext db)
    {
        var balls = StudentBallService.AggregateByStudent(await StudentBallService.ComputeByGroupAsync(db));
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();
        var classes = await db.Classes.ToListAsync();
        var classById = classes.ToDictionary(c => c.Id);
        var reasons = (await db.AbsenceReasons.ToListAsync()).ToDictionary(r => r.Id);
        var live = await db.StudentGroups.Where(m => m.IsActive).ToListAsync();
        var groupsByStudent = live
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(m => MembershipLifecycle.StateRank(m.Status))
                .ThenByDescending(m => m.JoinedAt ?? string.Empty, StringComparer.Ordinal)
                .Select(m => m.GroupId).Distinct().ToList());
        var memberOf = live
            .GroupBy(m => (m.StudentId, m.GroupId))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(m => MembershipLifecycle.StateRank(m.Status))
                .ThenByDescending(m => m.JoinedAt, StringComparer.Ordinal).First());
        var entriesByClass = (await db.JournalEntries.ToListAsync())
            .GroupBy(e => e.ClassId).ToDictionary(g => g.Key, g => g.ToList());
        var lessonsByClass = (await db.LessonNotes.Where(n => n.Conducted).ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.GroupBy(n => (n.SubjectId, n.Date, n.Period))
                .Select(x => (Key: x.Key, Taken: x.Any(n => n.AttendanceTaken))).ToList());

        var result = new List<StudentRatingRowDto>();
        foreach (var st in students)
        {
            var groupIds = groupsByStudent.TryGetValue(st.Id, out var gs) && gs.Count > 0
                ? gs
                : classes.Where(c => !string.IsNullOrEmpty(st.ClassName) && c.Name == st.ClassName).Select(c => c.Id).ToList();
            if (groupIds.Count == 0) continue;
            var grades = new List<double>();
            var marks = new List<LessonMark>();
            foreach (var gid in groupIds)
            {
                var mine = entriesByClass.TryGetValue(gid, out var ents)
                    ? ents.Where(e => e.StudentId == st.Id).ToList() : new List<JournalEntry>();
                grades.AddRange(mine.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value));
                if (!lessonsByClass.TryGetValue(gid, out var lessons)) continue;
                var window = memberOf.TryGetValue((st.Id, gid), out var m)
                    ? MembershipAttendance.ImplicitWindow(new MembershipAttendance.Member(
                        m.Id, m.StudentId, m.GroupId, m.Status, m.JoinedAt, m.ActivatedAt, m.FrozenAt, m.LeftAt, m.RecordedAt))
                    : ((string?)null, (string?)null);   // legacy ClassName fallback — no membership, no window
                foreach (var (key, taken) in lessons)
                {
                    // One cell: a reason row wins over «keldi»/grade rows of the same lesson (Classify's order).
                    var cell = mine.Where(x => (x.SubjectId, x.Date, x.Period) == key).ToList();
                    var e = cell.FirstOrDefault(x => x.ReasonId != null) ?? cell.FirstOrDefault();
                    var reason = e?.ReasonId is null ? null : reasons.GetValueOrDefault(e.ReasonId) ?? new AbsenceReason();
                    marks.Add(AttendanceRules.Classify(true, taken && MembershipAttendance.InWindow(key.Date, window),
                        false, false, e, reason));
                }
            }
            var average = grades.Count > 0 ? Math.Round(grades.Average(), 1) : 0;
            double? attendance = AttendanceRules.Tally(marks).Percent;
            var firstCls = classById.TryGetValue(groupIds[0], out var c0) ? c0 : null;
            var b = balls.GetValueOrDefault(st.Id);
            result.Add(new StudentRatingRowDto(
                new StudentDto(st.Id, st.FullName, st.BirthDate, st.Address, st.Gender, st.ParentFullName, st.ParentPhone,
                    st.ClassName, st.EnrollmentDate, st.Balance, LoginBlocked: st.LoginBlocked),
                firstCls?.Name ?? st.ClassName, firstCls?.Grade ?? 0, average, attendance, b?.Ball ?? 0, groupIds,
                AvgBall: b?.AvgBall ?? 0, GroupCount: b?.GroupCount ?? 0,
                GroupBalls: b is null ? new Dictionary<string, int>() : new Dictionary<string, int>(b.GroupBalls)));
        }
        return result;
    }

    /// <summary>Attendance, late reasons, frozen/trial memberships, a ClassName-only student, criteria grades.</summary>
    private static void SeedLegacy(AppDbContext db)
    {
        Seed(db);
        db.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "sick", Name = "Kasal", Short = "K" },
            new AbsenceReason { Id = "late", Name = "Kech", Short = "Kch", IsLate = true });
        db.Classes.Add(new Group { Id = "g3", Name = "Old group", CourseId = "s1", Grade = 7 });
        db.Students.Add(new Student { Id = "e", FullName = "Ergashev Erkin", ClassName = "Old group" });
        // b is frozen in g2 on 10-05 — the taken lesson of 10-06 is outside its window (no implicit «keldi»).
        db.StudentGroups.Add(new StudentGroup
        {
            StudentId = "b", GroupId = "g2", IsActive = true, Status = "frozen", JoinedAt = "2026-10-01", FrozenAt = "2026-10-05",
        });
        db.StudentGroups.Add(new StudentGroup { StudentId = "c", GroupId = "g1", IsActive = true, Status = "trial", JoinedAt = "2026-10-02" });
        // Attendance TAKEN on 10-05 (g1), 10-06 (g2, g3) — implicit «keldi» there; 10-07 only conducted (one grade is
        // not attendance) — unmarked students are NOT counted.
        foreach (var (g, s, d, per, taken) in new[] { ("g1", "s1", "2026-10-05", 1, true), ("g1", "s1", "2026-10-07", 1, false),
                     ("g1", "s1", "2026-10-07", 1, false), ("g2", "s2", "2026-10-06", 1, true), ("g3", "s1", "2026-10-06", 2, true) })
            db.LessonNotes.Add(new LessonNote { ClassId = g, SubjectId = s, Date = d, Period = per, Conducted = true, AttendanceTaken = taken });
        db.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "s1", Date = "2026-10-09", Period = 1, Conducted = false });
        db.JournalEntries.AddRange(
            new JournalEntry { StudentId = "a", ClassId = "g1", SubjectId = "s1", Date = "2026-10-07", Period = 2, ReasonId = "sick" },
            new JournalEntry { StudentId = "b", ClassId = "g1", SubjectId = "s1", Date = "2026-10-09", Period = 1, ReasonId = "sick" },  // not conducted
            new JournalEntry { StudentId = "b", ClassId = "g1", SubjectId = "s1", Date = "2026-10-05", Period = 2, ReasonId = "late" },
            new JournalEntry { StudentId = "c", ClassId = "g2", SubjectId = "s2", Date = "2026-10-06", Period = 1, ReasonId = "sick" },
            new JournalEntry { StudentId = "e", ClassId = "g3", SubjectId = "s1", Date = "2026-10-06", Period = 2, Grade = 4 });
        db.SaveChanges();
    }

    [Fact]
    public async Task SchoolAsync_matches_the_in_memory_reference_and_MCP()
    {
        using var host = new McpTestHost();
        SeedLegacy(host.Rw);

        var now = await RatingService.SchoolAsync(host.Rw);
        var legacy = await LegacySchoolAsync(host.Rw);
        var mcp = await host.Tool<TestTools>(McpTestHost.SuperAdmin()).ComputeCenterAsync(CancellationToken.None);

        Assert.NotEmpty(now);
        Assert.Equal(legacy.Select(Key), now.Select(Key));
        foreach (var (n, l) in now.Zip(legacy))
        {
            Assert.Equal(l.GroupIds, n.GroupIds);
            Assert.Equivalent(l.GroupBalls, n.GroupBalls, strict: true);
            Assert.Equal((l.Student.Id, l.Student.FullName, l.Student.ClassName, l.Student.Balance, l.Student.LoginBlocked),
                (n.Student.Id, n.Student.FullName, n.Student.ClassName, n.Student.Balance, n.Student.LoginBlocked));
        }

        var byId = mcp.ToDictionary(r => r.StudentId);
        Assert.Equal(now.Select(r => r.Student.Id).OrderBy(x => x), byId.Keys.OrderBy(x => x));
        foreach (var r in now)
        {
            var m = byId[r.Student.Id];
            Assert.Equal((r.Ball, r.AvgBall, r.GroupCount, r.Average, r.Attendance, r.ClassName),
                (m.Ball, m.AvgBall, m.GroupCount, m.Average, m.Attendance, m.MainGroup));
        }

        static object Key(StudentRatingRowDto r) =>
            (r.Student.Id, r.ClassName, r.Grade, r.Average, r.Attendance, r.Ball, r.AvgBall, r.GroupCount);
    }
}
