using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Rating;

/// <summary>Bitta kesim qatori (guruh yoki maktab sinfi). O'rtachalar 0..100, 1 kasr; ma'lumot bo'lmasa <c>null</c>.
/// <paramref name="Students"/> — shu davrda ballga ega (reytingga kirgan) o'quvchilar.</summary>
public sealed record RatingSectionRowDto(
    string Key, string Label, string? Course, string? Teacher, int Students,
    double? AvgScore, double? AvgTest, double? AvgBall, double? AvgHomework, int? AttendancePct, int LessonsHeld);

public sealed record RatingSectionsDto(
    RatingPeriodDto Period, List<RatingSectionRowDto> Groups, List<RatingSectionRowDto> Grades, int HomeworkWeight);

/// <summary>
/// REYTING «GURUHLAR KESIMI» (SPEC-backlog-2026-10-17 F4 A2, TZ 2.2): har guruh va har maktab sinfi bo'yicha o'rtacha
/// reyting bali, test %, ball %, uy vazifasi va davomat. Raqamlar KESHLANGAN davr reytingidan (<see cref="RatingResult"/> —
/// markaz jadvali, guruh jadvali va o'quvchi bloki bilan BIR xil) va yagona davomat ta'rifidan
/// (<see cref="MembershipAttendance"/>, davr kunlari bilan) olinadi — ikkinchi formula YOZILMAYDI.
///
/// <para><b>Guruh qatori</b> — o'sha guruhdagi (o'quvchi, guruh) bali bor qatorlar o'rtachasi. <b>Sinf qatori</b> — o'quvchining
/// MARKAZ bali (bir o'quvchi bir marta), sinf — joriy maktab sinfi (<c>SchoolGrades.Current</c>, «Sinflar kesimi» hisoboti
/// bilan bir xil); sinfi kiritilmaganlar «Sinf ko'rsatilmagan» qatorida. Arxivdagi o'quvchi reytingda yo'q (rating.md §1).</para>
/// </summary>
public static class RatingSections
{
    public const string NoGradeKey = "none";

    private static double? Avg(IEnumerable<double?> xs)
    {
        var v = xs.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : Math.Round(v.Average(), 1);
    }

    public static string GradeLabel(int? grade) =>
        grade is null ? "Sinf ko'rsatilmagan"
        : grade >= SchoolGrades.Graduated ? "Maktabni bitirgan"
        : $"{grade}-sinf";

    /// <summary>Sof yig'ish: reyting natijasi + (o'quvchi, guruh) davomati + o'quvchi → joriy sinf.</summary>
    public static (List<RatingSectionRowDto> Groups, List<RatingSectionRowDto> Grades) Build(
        RatingResult r,
        IReadOnlyDictionary<(string StudentId, string GroupId), MembershipAttendance.Stat> attendance,
        IReadOnlyDictionary<string, int?> gradeOf)
    {
        var attByGroup = attendance.GroupBy(kv => kv.Key.GroupId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Aggregate(MembershipAttendance.Stat.Empty, (a, kv) => a.Plus(kv.Value)),
                StringComparer.Ordinal);
        var attByStudent = attendance.GroupBy(kv => kv.Key.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Aggregate(MembershipAttendance.Stat.Empty, (a, kv) => a.Plus(kv.Value)),
                StringComparer.Ordinal);

        var groups = r.RowsByGroup
            .Select(g =>
            {
                var info = r.GroupInfo.GetValueOrDefault(g.Key);
                var rows = g.Where(x => x.Score is not null).ToList();
                var att = attByGroup.GetValueOrDefault(g.Key) ?? MembershipAttendance.Stat.Empty;
                return new RatingSectionRowDto(
                    g.Key, info?.Name ?? g.Key, info?.CourseName, info?.TeacherName, rows.Count,
                    Avg(rows.Select(x => x.Score)), Avg(rows.Select(x => x.TestAvg)), Avg(rows.Select(x => x.BallNorm)),
                    Avg(rows.Select(x => x.HomeworkAvg)), att.Percent, att.Held);
            })
            .Where(x => x.Students > 0)
            .OrderByDescending(x => x.AvgScore ?? -1).ThenBy(x => x.Label, StringComparer.CurrentCulture)
            .ToList();

        var grades = r.Centre.Where(c => c.Score is not null)
            .GroupBy(c => gradeOf.GetValueOrDefault(c.StudentId))
            .Select(g =>
            {
                var att = g.Select(c => attByStudent.GetValueOrDefault(c.StudentId) ?? MembershipAttendance.Stat.Empty)
                    .Aggregate(MembershipAttendance.Stat.Empty, (a, s) => a.Plus(s));
                return new RatingSectionRowDto(
                    g.Key?.ToString() ?? NoGradeKey, GradeLabel(g.Key), null, null, g.Count(),
                    Avg(g.Select(c => c.Score)), Avg(g.Select(c => c.TestAvg)),
                    // Markazda ball % yo'q (guruh eng yaxshisiga nisbatan) — sinf qatorida o'quvchining guruh ball %lari o'rtachasi.
                    Avg(g.SelectMany(c => r.RowsByStudent[c.StudentId]).Select(x => x.BallNorm)),
                    Avg(g.Select(c => c.HomeworkAvg)), att.Percent, att.Held);
            })
            .OrderBy(x => x.Key == NoGradeKey ? 99 : int.Parse(x.Key))
            .ToList();
        return (groups, grades);
    }

    public static async Task<RatingSectionsDto> BuildAsync(IAppDbContext db, RatingLoader load, RatingPeriod p, CancellationToken ct = default)
    {
        var r = await load(p);
        var attendance = await MembershipAttendance.ComputeAsync(db, null, fromDay: p.From, toDay: p.To, ct: ct);
        var ids = r.Centre.Select(c => c.StudentId).ToList();
        var today = AppClock.Today;
        var gradeOf = (await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.SchoolGrade, s.SchoolGradeYear }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => SchoolGrades.Current(s.SchoolGrade, s.SchoolGradeYear, today), StringComparer.Ordinal);
        var (groups, grades) = Build(r, attendance, gradeOf);
        return new RatingSectionsDto(new RatingPeriodDto(p.Kind, p.Key, p.Label, p.From, p.To), groups, grades, r.HomeworkWeight);
    }
}
