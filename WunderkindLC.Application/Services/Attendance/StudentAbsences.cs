using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

public record StudentAbsenceCountsDto(
    int Held, int Attended, int Present, int Late, int AbsentExcused, int AbsentUnexcused, int Unmarked, int? Percent);

public record StudentAbsenceReasonDto(string ReasonId, string Name, bool IsExcused, bool IsLate, int Count);

/// <param name="Kind"><c>late</c> · <c>absent_excused</c> · <c>absent_unexcused</c>.</param>
public record StudentAbsenceItemDto(
    string Date, string GroupId, string GroupName, string CourseName, string ReasonName, string Kind);

public record StudentAbsencesDto(
    string From, string To, StudentAbsenceCountsDto Counts,
    List<StudentAbsenceReasonDto> ByReason, List<StudentAbsenceItemDto> Items);

/// <summary>
/// O'quvchi profili → «Baholar va davomat» → «Qoldirilgan darslar» (CRM Phase 3, U3d — SPEC P3.5): tanlangan
/// davrdagi har qoldirilgan/kech qolingan dars (sana, guruh, sabab) va sanoqlar. Kataklar
/// <see cref="StudentJournalBuilder.PeriodCellsAsync"/> dan, belgi — <see cref="AttendanceRules"/>; ya'ni sanoqlar
/// ro'yxat bilan va o'quvchi ilovasi bilan AYNAN bir xil.
/// </summary>
public static class StudentAbsences
{
    public const int DefaultDays = 180;
    public const int MaxDays = 366;

    /// <summary>Davrni tekshiradi: bo'sh — oxirgi <see cref="DefaultDays"/> kun; ko'pi bilan <see cref="MaxDays"/>.</summary>
    public static (string From, string To, string? Error) NormalizeRange(string? from, string? to, DateOnly today)
    {
        DateOnly? f = DateOnly.TryParse(from, out var fv) ? fv : null;
        DateOnly? t = DateOnly.TryParse(to, out var tv) ? tv : null;
        if (!string.IsNullOrWhiteSpace(from) && f is null) return ("", "", "«from» sanasi noto'g'ri");
        if (!string.IsNullOrWhiteSpace(to) && t is null) return ("", "", "«to» sanasi noto'g'ri");
        t ??= today;
        f ??= t.Value.AddDays(-(DefaultDays - 1));
        if (f > t) return ("", "", "«from» sanasi «to» dan katta bo'lishi mumkin emas");
        if (t.Value.DayNumber - f.Value.DayNumber + 1 > MaxDays)
            return ("", "", $"Oraliq juda uzun — ko'pi bilan {MaxDays} kun");
        return (f.Value.ToString("yyyy-MM-dd"), t.Value.ToString("yyyy-MM-dd"), null);
    }

    /// <summary>Sof yig'uvchi (testlanadi).</summary>
    public static StudentAbsencesDto Build(
        string from, string to, IEnumerable<(Group Group, string CourseName, StudentJournalBuilder.Cell Cell)> cells,
        IReadOnlyDictionary<string, AbsenceReason> reasons)
    {
        var list = cells.ToList();
        var t = AttendanceRules.Tally(list.Select(x => x.Cell.Mark));
        var counts = new StudentAbsenceCountsDto(
            t.Held, t.Attended, t.Present, t.Late, t.AbsentExcused, t.AbsentUnexcused, t.Unmarked, t.Percent);

        var missed = list
            .Where(x => x.Cell.Mark is LessonMark.Late or LessonMark.AbsentExcused or LessonMark.AbsentUnexcused)
            .ToList();
        var items = missed
            .OrderByDescending(x => x.Cell.Date, StringComparer.Ordinal)
            .ThenBy(x => x.Group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new StudentAbsenceItemDto(
                x.Cell.Date, x.Group.Id, x.Group.Name, x.CourseName, x.Cell.ReasonName ?? "",
                AttendanceRules.Key(x.Cell.Mark)))
            .ToList();
        var byReason = missed
            .Where(x => x.Cell.ReasonId is not null)
            .GroupBy(x => x.Cell.ReasonId!, StringComparer.Ordinal)
            .Select(g =>
            {
                var r = reasons.GetValueOrDefault(g.Key);
                return new StudentAbsenceReasonDto(g.Key, r?.Name ?? g.First().Cell.ReasonName ?? "",
                    r?.IsExcused ?? false, r?.IsLate ?? false, g.Count());
            })
            .OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new StudentAbsencesDto(from, to, counts, byReason, items);
    }

    public static async Task<StudentAbsencesDto> BuildAsync(IAppDbContext db, string studentId, string from, string to)
    {
        var cells = await StudentJournalBuilder.PeriodCellsAsync(db, studentId, from, to);
        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id);
        return Build(from, to, cells, reasons);
    }
}
