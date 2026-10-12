using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Review fix (2026-10-12) — the aggregate reports (centre rating + MCP mirror, teacher rating + MCP rating_group,
/// dashboard group attendance) use the ONE attendance definition through <see cref="MembershipAttendance"/>
/// (SQL aggregates). Before: (conducted − absent) ÷ conducted — a single grade made every student of the lesson
/// «attended».
/// </summary>
public class MembershipAttendanceTests
{
    private static void Seed(TestDb db, Action<Infrastructure.Data.AppDbContext> extra)
    {
        var c = db.Context;
        c.Subjects.Add(new Subject { Id = "s1", Name = "Ingliz tili" });
        c.Classes.Add(new Group { Id = "g1", Name = "A1", CourseId = "s1" });
        c.Students.AddRange(
            new Student { Id = "a", FullName = "Aliyev Ali" },
            new Student { Id = "b", FullName = "Botirov Bobur" },
            new Student { Id = "c", FullName = "Choriyev Chori" });
        c.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "sick", Name = "Kasal", Short = "K", IsExcused = true },
            new AbsenceReason { Id = "none", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "late", Name = "Kech", Short = "Kch", IsLate = true });
        extra(c);
        c.SaveChanges();
    }

    private static StudentGroup Member(string student, string joined = "2026-09-01", string recorded = "2026-09-01",
        string status = "active", string frozenAt = "", string activated = "2026-09-01") => new()
    {
        StudentId = student, GroupId = "g1", IsActive = true, Status = status,
        JoinedAt = joined, ActivatedAt = activated, RecordedAt = recorded, FrozenAt = frozenAt,
    };

    private static LessonNote Lesson(string date, bool taken, int period = 1) => new()
    {
        ClassId = "g1", SubjectId = "s1", Date = date, Period = period, Conducted = true, AttendanceTaken = taken,
    };

    private static JournalEntry Entry(string student, string date, string? reason = null, int? grade = null,
        bool present = false, int period = 1) => new()
    {
        StudentId = student, ClassId = "g1", SubjectId = "s1", Date = date, Period = period,
        ReasonId = reason, Grade = grade, Present = present,
    };

    [Fact]
    public async Task One_grade_does_not_make_the_unmarked_students_present()
    {
        using var db = TestDb.Sqlite();
        Seed(db, c =>
        {
            c.StudentGroups.AddRange(Member("a"), Member("b"));
            c.LessonNotes.Add(Lesson("2026-10-05", taken: false));   // conducted by ONE grade
            c.JournalEntries.Add(Entry("a", "2026-10-05", grade: 5));
        });

        var r = await MembershipAttendance.ComputeAsync(db.Context);
        Assert.Equal((1, 1, (int?)100), (r[("a", "g1")].Held, r[("a", "g1")].Attended, r[("a", "g1")].Percent));
        // b is UNMARKED — excluded (old formula: 100 %).
        Assert.Equal((0, (int?)null), (r[("b", "g1")].Held, r[("b", "g1")].Percent));
    }

    [Fact]
    public async Task Taken_lesson__unmarked_is_present__late_is_attended__reasons_split()
    {
        using var db = TestDb.Sqlite();
        Seed(db, c =>
        {
            c.StudentGroups.AddRange(Member("a"), Member("b"), Member("c"));
            c.LessonNotes.AddRange(Lesson("2026-10-05", taken: true), Lesson("2026-10-06", taken: true));
            c.JournalEntries.AddRange(
                Entry("b", "2026-10-05", reason: "late"),
                Entry("b", "2026-10-06", reason: "none"),
                Entry("c", "2026-10-05", reason: "sick"),
                Entry("c", "2026-10-06", reason: "sick", present: true));   // a reason wins over «present»
        });

        var r = await MembershipAttendance.ComputeAsync(db.Context);
        Assert.Equal(new MembershipAttendance.Stat(2, 0, 0, 0), r[("a", "g1")]);   // implicit «keldi» twice
        Assert.Equal(new MembershipAttendance.Stat(0, 1, 0, 1), r[("b", "g1")]);
        Assert.Equal(50, r[("b", "g1")].Percent);
        Assert.Equal(new MembershipAttendance.Stat(0, 0, 2, 0), r[("c", "g1")]);
        Assert.Equal(0, r[("c", "g1")].Percent);
    }

    [Fact]
    public async Task Duplicate_rows_of_one_lesson_cell_count_once_and_the_reason_wins()
    {
        // Bad legacy data: two journal rows for the same (student, lesson) — a grade AND an absence reason.
        using var db = TestDb.Sqlite();
        Seed(db, c =>
        {
            c.StudentGroups.Add(Member("a"));
            c.LessonNotes.AddRange(Lesson("2026-10-05", taken: true), Lesson("2026-10-06", taken: true));
            c.JournalEntries.AddRange(
                Entry("a", "2026-10-05", grade: 3), Entry("a", "2026-10-05", reason: "none"),
                Entry("a", "2026-10-06", grade: 5), Entry("a", "2026-10-06", grade: 4));
        });

        var r = await MembershipAttendance.ComputeAsync(db.Context);
        // 10-05 → absent (reason wins, counted once); 10-06 → present once; nothing implicit is left.
        Assert.Equal(new MembershipAttendance.Stat(1, 0, 0, 1), r[("a", "g1")]);
    }

    [Fact]
    public async Task Implicit_keldi_only_inside_the_membership_window__explicit_marks_always()
    {
        // a recorded on 10-06 (back-dated membership): the 10-05 lesson is before PresentDefaultFrom — its explicit
        // absence counts, an implicit «keldi» would not. b frozen on 10-05: nothing implicit after it.
        using var db = TestDb.Sqlite();
        Seed(db, c =>
        {
            c.StudentGroups.AddRange(
                Member("a", joined: "2026-10-01", recorded: "2026-10-06T09:30:00", activated: "2026-10-01"),
                Member("b", status: "frozen", frozenAt: "2026-10-05"));
            c.LessonNotes.AddRange(
                Lesson("2026-10-04", taken: true), Lesson("2026-10-05", taken: true),
                Lesson("2026-10-06", taken: true), Lesson("2026-10-07", taken: true));
            c.JournalEntries.Add(Entry("a", "2026-10-05", reason: "none"));
        });

        var r = await MembershipAttendance.ComputeAsync(db.Context);
        // a: 10-05 explicit absent (outside the window but explicit) + 10-06, 10-07 implicit; 10-04 not counted.
        Assert.Equal(new MembershipAttendance.Stat(2, 0, 0, 1), r[("a", "g1")]);
        // b: 10-04, 10-05 implicit (inside, up to the freeze day), 10-06/07 after the freeze — not counted.
        Assert.Equal(new MembershipAttendance.Stat(2, 0, 0, 0), r[("b", "g1")]);
    }

    [Fact]
    public async Task Month_filter_and_unbounded_pair()
    {
        using var db = TestDb.Sqlite();
        Seed(db, c =>
        {
            c.StudentGroups.Add(Member("a"));
            c.LessonNotes.AddRange(Lesson("2026-09-30", taken: true), Lesson("2026-10-01", taken: true));
            c.JournalEntries.AddRange(Entry("a", "2026-09-30", reason: "none"), Entry("c", "2026-09-30", grade: 4));
        });

        var oct = await MembershipAttendance.ComputeAsync(db.Context, new[] { "g1" }, "2026-10");
        Assert.Equal(new MembershipAttendance.Stat(1, 0, 0, 0), oct[("a", "g1")]);

        // c has no membership (legacy ClassName fallback) — counted with no window: 09-30 graded, 10-01 implicit.
        var all = await MembershipAttendance.ComputeAsync(db.Context, unboundedPairs: new[] { ("c", "g1") });
        Assert.Equal(new MembershipAttendance.Stat(1, 0, 0, 1), all[("a", "g1")]);
        Assert.Equal(new MembershipAttendance.Stat(2, 0, 0, 0), all[("c", "g1")]);
    }

    [Fact]
    public void Window_is_activation_or_join_and_recorded_until_freeze_or_leave()
    {
        static MembershipAttendance.Member M(string joined = "", string activated = "", string recorded = "",
            string status = "active", string frozen = "", string? left = null) =>
            new("m", "s", "g", status, joined, activated, frozen, left, recorded);

        Assert.Equal(("2026-09-05", (string?)null), MembershipAttendance.ImplicitWindow(M("2026-09-01", "2026-09-05", "2026-09-02")));
        Assert.Equal(("2026-09-10", (string?)null), MembershipAttendance.ImplicitWindow(M("2026-09-01", "", "2026-09-10T08:00:00")));
        Assert.Equal(((string?)null, (string?)null), MembershipAttendance.ImplicitWindow(M()));
        Assert.Equal(("2026-09-01", "2026-10-05"),
            MembershipAttendance.ImplicitWindow(M("2026-09-01", status: "frozen", frozen: "2026-10-05T12:00:00", left: "2026-10-01")));
        Assert.Equal(("2026-09-01", "2026-10-01"), MembershipAttendance.ImplicitWindow(M("2026-09-01", left: "2026-10-01")));
        Assert.True(MembershipAttendance.InWindow("2026-10-05", ("2026-10-05", "2026-10-05")));
        Assert.False(MembershipAttendance.InWindow("2026-10-04", ("2026-10-05", null)));
    }

    [Fact]
    public void Aggregate_reports_no_longer_use_the_old_formula()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(p).ToArray()));

        foreach (var src in new[]
                 {
                     Src("WunderkindLC.Application", "Services", "RatingService.cs"),
                     Src("WunderkindLC.Application", "Services", "StudentBallService.cs"),
                     Src("WunderkindLC.Application", "Mcp", "Tools", "TestTools.cs"),
                     Src("WunderkindLC.Server", "Controllers", "DashboardController.cs"),
                 })
        {
            Assert.Contains("MembershipAttendance.ComputeAsync(", src);
            Assert.DoesNotContain("(conducted - absent)", src);
            Assert.DoesNotContain("opp - abs", src);
        }
        // The dead «journal/conducted» endpoint is gone (no client caller; it showed "o'tildi" as attendance).
        Assert.DoesNotContain("[HttpGet(\"conducted\")]", Src("WunderkindLC.Server", "Controllers", "JournalController.cs"));
    }
}
