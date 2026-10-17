using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «O'quvchilar davomati» → «Davr tahlili» (SPEC-backlog-2026-10-17 A1): sanoqlar YAGONA davomat ta'rifidan
/// (<see cref="MembershipAttendance"/>), oldingi teng davr bilan solishtirish, sabablar, sinflar, 3+ qoldirganlar.
/// </summary>
public class AttendancePeriodReportTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static TestDb Seed()
    {
        var db = TestDb.Sqlite();
        var c = db.Context;
        c.Subjects.Add(new Subject { Id = "s1", Name = "Ingliz tili" });
        c.Teachers.Add(new Teacher { Id = "t1", FullName = "Gulasal" });
        c.Classes.Add(new Group { Id = "g1", Name = "A1", CourseId = "s1", TeacherId = "t1" });
        c.Students.AddRange(
            new Student { Id = "a", FullName = "Aliyev Ali", ParentPhone = "+998901112233", SchoolGrade = 7, SchoolGradeYear = 2026 },
            new Student { Id = "b", FullName = "Botirov Bobur" },
            new Student { Id = "x", FullName = "Arxivdagi", IsArchived = true });
        c.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "sick", Name = "Kasal", Short = "K", IsExcused = true },
            new AbsenceReason { Id = "none", Name = "Sababsiz", Short = "S" });
        foreach (var sid in new[] { "a", "b", "x" })
            c.StudentGroups.Add(new StudentGroup
            {
                StudentId = sid, GroupId = "g1", IsActive = true, Status = "active",
                JoinedAt = "2026-08-01", ActivatedAt = "2026-08-01", RecordedAt = "2026-08-01",
            });
        // Oldingi davr (2026-09-21..30): ikki dars, hamma keldi (davomat olingan, belgisiz = keldi).
        foreach (var d in new[] { "2026-09-22", "2026-09-24" })
            c.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "s1", Date = d, Period = 1, Conducted = true, AttendanceTaken = true });
        // Joriy davr (2026-10-01..10): to'rt dars; Ali uchtasiga sababsiz kelmadi, bittasiga kasal.
        foreach (var d in new[] { "2026-10-01", "2026-10-03", "2026-10-06", "2026-10-08" })
            c.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "s1", Date = d, Period = 1, Conducted = true, AttendanceTaken = true });
        foreach (var d in new[] { "2026-10-01", "2026-10-03", "2026-10-06" })
            c.JournalEntries.Add(new JournalEntry { StudentId = "a", ClassId = "g1", SubjectId = "s1", Date = d, Period = 1, ReasonId = "none" });
        c.JournalEntries.Add(new JournalEntry { StudentId = "a", ClassId = "g1", SubjectId = "s1", Date = "2026-10-08", Period = 1, ReasonId = "sick" });
        c.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Davr__guruh_pasaygani_sabablar_sinflar_va_xavf_royxati()
    {
        using var db = Seed();
        var r = await AttendancePeriodReport.BuildAsync(db.Context, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), null, null, Today);

        Assert.Equal("2026-09-21", r.PrevFrom);
        Assert.Equal("2026-09-30", r.PrevTo);
        Assert.Equal(10, r.Days);

        // Arxivdagi o'quvchi sanalmaydi: 2 o'quvchi × 4 dars.
        Assert.Equal(8, r.Total.Held);
        Assert.Equal(3, r.Total.Unexcused);
        Assert.Equal(1, r.Total.Excused);
        Assert.Equal(50, r.Total.Percent);
        Assert.Equal(100, r.PreviousTotal.Percent);

        var g = Assert.Single(r.Groups);
        Assert.Equal(-50, g.DeltaPp);
        Assert.True(g.Dropped);
        Assert.Equal("Gulasal", g.TeacherName);
        Assert.Equal(2, g.Students);

        Assert.Equal(3, r.Reasons.Single(x => x.ReasonId == "none").Count);
        Assert.True(r.Reasons.Single(x => x.ReasonId == "sick").IsExcused);

        Assert.Equal("7-sinf", r.Grades.Single(x => x.Grade == 7).Label);
        Assert.Equal(0, r.Grades.Single(x => x.Grade == 7).Counts.Percent);
        Assert.Equal(100, r.Grades.Single(x => x.Grade is null).Counts.Percent);

        var risk = Assert.Single(r.AtRisk);
        Assert.Equal("a", risk.StudentId);
        Assert.Equal(4, risk.Absent);
        Assert.Equal(3, risk.Unexcused);
        Assert.Equal("+998901112233", risk.Phone);
    }

    [Fact]
    public async Task Ochituvchi_filtri_boshqa_ochituvchida_bosh()
    {
        using var db = Seed();
        var r = await AttendancePeriodReport.BuildAsync(db.Context, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), null, "boshqa", Today);
        Assert.Empty(r.Groups);
        Assert.Equal(0, r.Total.Held);
        Assert.Null(r.Total.Percent);
    }

    [Theory]
    [InlineData("2026-07-01", "2026-10-10", true)]    // 102 kun
    [InlineData("2026-10-10", "2026-10-01", true)]    // teskari
    [InlineData("2026-07-11", "2026-10-10", false)]   // 92 kun — chegarada
    [InlineData(null, null, false)]                   // standart — oxirgi 30 kun
    public void Davr_tekshiruvi(string? from, string? to, bool error)
    {
        var e = AttendancePeriodReport.Validate(from, to, Today, out var f, out var t);
        Assert.Equal(error, e is not null);
        if (from is null) Assert.Equal(29, t.DayNumber - f.DayNumber);
    }

    [Fact]
    public void Pasayish_chegarasi() =>
        Assert.Equal((true, false, false), (
            AttendancePeriodReport.IsDrop(AttendancePeriodReport.Delta(80, 90)),
            AttendancePeriodReport.IsDrop(AttendancePeriodReport.Delta(81, 90)),
            AttendancePeriodReport.IsDrop(AttendancePeriodReport.Delta(80, null))));
}
