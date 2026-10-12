using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>Qizil ro'yxat qatori (SPEC P3.5).</summary>
public record RedListContactDto(string RequestId, string Status);

public record RedListRowDto(
    string StudentId, string StudentName, string GroupId, string GroupName, string TeacherName,
    int Streak, int Level, List<string> Dates, string LastDate, RedListContactDto? Contact);

/// <summary>
/// «O'quvchilar davomati» → «Qizil ro'yxat» (CRM Phase 3, U3d): ketma-ket SABABSIZ dars qoldirganlar
/// (<see cref="AbsenceStreakLoader"/> — avtomatik «Bog'lanish kerak» va KPI bilan bitta ta'rif), 3+ va 5+ darajalar.
/// Ruxsat — <c>students.attendance</c>, O'QISH ham (javobda o'quvchi ismlari).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.attendance", ReadRequiresPerm = true)]
[Route("api/admin/student-attendance/red-list")]
public class AttendanceRedListController(DataCache dataCache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RedListRowDto>>> Get(
        [FromQuery] int? min, [FromQuery] string? groupId, [FromQuery] string? teacherId)
    {
        var today = AppClock.Today;
        var all = await dataCache.GetOrCreateAsync(
            $"attendance:red-list:{today:yyyy-MM-dd}",
            new[]
            {
                nameof(JournalEntry), nameof(LessonNote), nameof(StudentGroup), nameof(Student),
                nameof(AbsenceReason), nameof(ContactRequest), nameof(Group), nameof(Teacher),
                nameof(AbsenceStreakAlert),
            },
            TimeSpan.FromMinutes(5),
            db => BuildAsync(db, today));

        var threshold = Math.Max(1, min ?? AbsenceStreaks.Level3);
        return all
            .Where(r => r.Streak >= threshold)
            .Where(r => string.IsNullOrWhiteSpace(groupId) || r.GroupId == groupId)
            .Where(r => string.IsNullOrWhiteSpace(teacherId) || r.TeacherId == teacherId)
            .Select(r => r.Dto)
            .ToList();
    }

    private sealed record Item(RedListRowDto Dto, string GroupId, string TeacherId, int Streak);

    private static async Task<List<Item>> BuildAsync(IAppDbContext db, DateOnly today)
    {
        var rows = await AbsenceStreakLoader.ComputeAsync(db, today);
        if (rows.Count == 0) return [];

        var teacherIds = rows.Select(r => r.TeacherId).Where(x => x.Length > 0).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName }).ToDictionaryAsync(t => t.Id, t => t.FullName);

        // «Bog'lanish kerak» holati: avval o'quvchining OCHIQ talabi, bo'lmasa shu seriya uchun yaratilgani.
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var open = (await db.ContactRequests.AsNoTracking()
                .Where(c => studentIds.Contains(c.StudentId)
                            && (c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback))
                .Select(c => new { c.Id, c.StudentId, c.Status })
                .ToListAsync())
            .GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.First());
        var alerts = await db.AbsenceStreakAlerts.AsNoTracking()
            .Where(a => studentIds.Contains(a.StudentId) && a.ContactRequestId != null)
            .Select(a => new { a.StudentId, a.GroupId, a.StreakStart, a.ContactRequestId })
            .ToListAsync();
        var linkedIds = alerts.Select(a => a.ContactRequestId!).Distinct().ToList();
        var linked = await db.ContactRequests.AsNoTracking().Where(c => linkedIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Status }).ToDictionaryAsync(c => c.Id, c => c.Status);

        return rows
            .OrderByDescending(r => r.Streak.Count)
            .ThenByDescending(r => r.Streak.LastDate, StringComparer.Ordinal)
            .ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                RedListContactDto? contact = null;
                if (open.TryGetValue(r.StudentId, out var o)) contact = new RedListContactDto(o.Id, o.Status);
                else
                {
                    var a = alerts.FirstOrDefault(x => x.StudentId == r.StudentId && x.GroupId == r.GroupId
                                                       && x.StreakStart == r.Streak.StartDate);
                    if (a?.ContactRequestId is not null && linked.TryGetValue(a.ContactRequestId, out var st))
                        contact = new RedListContactDto(a.ContactRequestId, st);
                }
                return new Item(
                    new RedListRowDto(r.StudentId, r.StudentName, r.GroupId, r.GroupName,
                        teachers.GetValueOrDefault(r.TeacherId, ""), r.Streak.Count, r.Level,
                        r.Streak.Dates.ToList(), r.Streak.LastDate, contact),
                    r.GroupId, r.TeacherId, r.Streak.Count);
            })
            .ToList();
    }
}
