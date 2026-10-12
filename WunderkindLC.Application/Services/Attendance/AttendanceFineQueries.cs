using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>One attendance fine row for the UI (Moliya → Jarima → «Davomat jarimalari», teacher portal).</summary>
public record AttendanceFineDto(
    string Id, string TeacherId, string TeacherName, string GroupId, string GroupName,
    string LessonDate, string LessonStart, string Deadline, decimal Amount, string Status,
    string? AttendanceTakenAt, string? WaivedByName, string? WaivedAt, string? WaiveReason);

/// <summary>List + totals (active fines only).</summary>
public record AttendanceFineListDto(List<AttendanceFineDto> Rows, decimal TotalActive, int CountActive);

/// <summary>Waive request (single or a whole day).</summary>
public record AttendanceFineWaiveRequest(string? Reason);
public record AttendanceFineWaiveDayRequest(string? Date, string? Reason);

/// <summary>Read side of attendance fines — shared by the admin controller and the teacher portal.</summary>
public static class AttendanceFineQueries
{
    public const int MaxRows = 1000;

    /// <summary>
    /// Fines filtered by month ("yyyy-MM"), teacher and status (<c>active</c> / <c>waived</c>, empty = all), newest
    /// lesson first. Totals count ACTIVE fines of the filtered set.
    /// </summary>
    public static async Task<AttendanceFineListDto> ListAsync(
        IAppDbContext db, string? month, string? teacherId, string? status, CancellationToken ct = default)
    {
        var q = db.TeacherAttendanceFines.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(month)) q = q.Where(f => f.Month == month);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(f => f.TeacherId == teacherId);
        if (status is AttendanceDiscipline.FineActive or AttendanceDiscipline.FineWaived) q = q.Where(f => f.Status == status);

        var rows = await q.OrderByDescending(f => f.LessonDate).ThenBy(f => f.LessonStart)
            .Take(MaxRows).ToListAsync(ct);
        var teacherIds = rows.Select(r => r.TeacherId).Distinct().ToList();
        var groupIds = rows.Select(r => r.GroupId).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName }).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var groups = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name }).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var dtos = rows.Select(f => ToDto(f, teachers.GetValueOrDefault(f.TeacherId, ""), groups.GetValueOrDefault(f.GroupId, "")))
            .ToList();
        var active = rows.Where(f => f.Status == AttendanceDiscipline.FineActive).ToList();
        return new AttendanceFineListDto(dtos, active.Sum(f => f.Amount), active.Count);
    }

    public static AttendanceFineDto ToDto(TeacherAttendanceFine f, string teacherName, string groupName) => new(
        f.Id, f.TeacherId, teacherName, f.GroupId, groupName,
        f.LessonDate, f.LessonStart, f.Deadline.ToString("yyyy-MM-ddTHH:mm:ss"), f.Amount, f.Status,
        f.AttendanceTakenAt?.ToString("yyyy-MM-ddTHH:mm:ss"), f.WaivedByName,
        f.WaivedAt?.ToString("yyyy-MM-ddTHH:mm:ss"), f.WaiveReason);
}
