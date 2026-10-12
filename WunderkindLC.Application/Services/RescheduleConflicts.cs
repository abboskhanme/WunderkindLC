using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Moving ONE lesson to another date/time (<see cref="JournalService.RescheduleLessonAsync"/>): is the room or
/// the teacher already busy then? (CRM Phase 5, <c>.claude/rules/schedule.md</c> §8.)
///
/// <para>The target is compared with that day's REAL lessons — <see cref="AttendanceWatch.LessonsOn"/> (group
/// weekdays + moves + group lifetime + substitutes), i.e. the same source as the dated schedule and the
/// lesson monitor. A lesson's teacher is the person who teaches it that day: the substitute when there is one,
/// otherwise the owner. The decision is <see cref="ScheduleConflicts.LessonClash"/> (pure, tested).</para>
/// </summary>
public static class RescheduleConflicts
{
    /// <summary>
    /// First lesson that occupies <paramref name="cls"/>'s room or its teacher on <paramref name="date"/> during
    /// the moved lesson (start = <paramref name="time"/> or the group start, same duration). <c>null</c> = free
    /// (also when the group has no valid time — nothing to compare).
    /// </summary>
    public static async Task<ScheduleConflicts.DayLesson?> FindAsync(
        IAppDbContext db, Group cls, string date, string? time, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day)) return null;
        var (start, end) = TeacherHomeService.LessonTime(cls.StartTime, cls.EndTime, time);
        if (ScheduleRules.MakeSlot(0, start, end) is null) return null;

        // Our teacher THAT day: a substitute covering the date (if any) teaches instead of the owner.
        var ownAssignments = await db.SubstituteTeacherAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.GroupId == cls.Id).ToListAsync(ct);
        var teacherId = ownAssignments.FirstOrDefault(a => SubstituteTeacherService.CoversDate(a, date))
            ?.SubstituteTeacherId ?? cls.TeacherId ?? "";

        var lessons = await LessonsOnAsync(db, day, cls.Id, ct);
        return ScheduleConflicts.LessonClash(cls.Id, cls.RoomId ?? "", teacherId, start, end, lessons);
    }

    /// <summary>Every lesson of the other non-archived groups on <paramref name="day"/>.</summary>
    public static async Task<List<ScheduleConflicts.DayLesson>> LessonsOnAsync(
        IAppDbContext db, DateOnly day, string excludeGroupId, CancellationToken ct = default)
    {
        var d = day.ToString("yyyy-MM-dd");
        var groups = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived && g.Id != excludeGroupId).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules.AsNoTracking()
                .Where(r => ids.Contains(r.ClassId) && (r.FromDate == d || r.ToDate == d))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => a.IsActive && ids.Contains(a.GroupId)
                            && string.Compare(a.Date, d) <= 0
                            && string.Compare(a.EndDate ?? a.Date, d) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);

        return AttendanceWatch.LessonsOn(groups, moves, assignments, day)
            .Select(l => new ScheduleConflicts.DayLesson(
                l.Group.Id, l.Group.Name, l.Group.RoomId ?? "",
                l.SubstituteTeacherId.Length > 0 ? l.SubstituteTeacherId : l.Group.TeacherId ?? "",
                l.StartTime, l.EndTime))
            .ToList();
    }
}
