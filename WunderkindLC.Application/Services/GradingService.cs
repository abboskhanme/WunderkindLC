using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Baholash (GradingCriterion, CriterionGrade) mantig'i — mezonlar bo'yicha o'quvchilarni
/// har darsga belgilash (o'qituvchi/admin) va ularning yig'ilgan statistikasi.
/// </summary>
public static class GradingService
{
    /// <summary>
    /// Guruhning bitta oyida o'quvchi-level agregatsiya: nechta mezon biriktirilgan,
    /// har o'quvchi nechta mezonni nechta darsda bajargan, ummumiy/o'rtacha.
    /// </summary>
    /// <param name="db">Database konteksti.</param>
    /// <param name="groupId">Guruh ID.</param>
    /// <param name="month">Oy ("yyyy-MM"), bo'lmasa joriy oy.</param>
    /// <returns>O'quvchi bo'yicha: Id, FullName, CriteriaCount, TotalScore, AverageScore.</returns>
    public static async Task<List<StudentGradingTotalDto>> CalculateStudentTotalsAsync(
        IAppDbContext db, string groupId, string? month)
    {
        var cur = TuitionService.CurrentMonth();
        var resolved = !string.IsNullOrEmpty(month) ? month : cur;

        // Shu guruhga biriktirilgan mezonlar.
        var critIds = await db.GroupGradingCriteria
            .Where(g => g.GroupId == groupId)
            .Select(g => g.CriterionId)
            .ToListAsync();

        if (critIds.Count == 0)
            return new List<StudentGradingTotalDto>();

        // Mezonlarni olaylik (MaxScore har mezon uchun).
        var criteria = await db.GradingCriteria
            .Where(c => critIds.Contains(c.Id))
            .Select(c => new { c.Id, c.MaxScore })
            .ToListAsync();

        var critDict = criteria.ToDictionary(c => c.Id, c => c.MaxScore);

        // Shu oyda shu guruhdagi o'quvchilarning barcha "bajardi" belgilari.
        var grades = await db.CriterionGrades
            .Where(g => g.GroupId == groupId && g.Done && g.Date.StartsWith(resolved))
            .Select(g => new { g.StudentId, g.CriterionId })
            .ToListAsync();

        // Har o'quvchi uchun: shu oy nechta mezon check bajargan.
        var doneByStudent = grades
            .GroupBy(g => g.StudentId)
            .ToDictionary(
                grp => grp.Key,
                grp => grp.Select(g => g.CriterionId).Distinct().Count());

        // O'quvchilar (faol a'zolar).
        var memberIds = await db.StudentGroups
            .Where(sg => sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.StudentId)
            .ToListAsync();

        var students = await db.Students
            .Where(s => memberIds.Contains(s.Id) && !s.IsArchived)
            .OrderBy(s => s.FullName)
            .ToListAsync();

        var result = new List<StudentGradingTotalDto>();

        foreach (var st in students)
        {
            var doneCount = doneByStudent.TryGetValue(st.Id, out var cnt) ? cnt : 0;
            var criteriaCount = critIds.Count;
            var avgScore = criteriaCount > 0 ? (double)doneCount / criteriaCount : 0;

            result.Add(new StudentGradingTotalDto(
                st.Id,
                st.FullName,
                criteriaCount,
                doneCount,
                Math.Round(avgScore, 2)));
        }

        return result;
    }
}
