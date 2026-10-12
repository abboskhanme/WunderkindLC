using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>Telegram delivery of one notice (outbox rows with <c>SourceId = item.Id</c>).</summary>
public record AbsentNoticeDeliveryDto(int Sent, int Failed, int Blocked, int Acked);

public record AbsentNoticeItemDto(
    string Id, string Kind, string StudentId, string StudentName, string ReasonName, bool IsExcused,
    string Status, DateTime SendAfter, string? DecidedByName, DateTime? DecidedAt, string? Note,
    string? CancelReason, int Period, AbsentNoticeDeliveryDto? Delivery);

public record AbsentNoticeLessonDto(
    string GroupId, string GroupName, string CourseName, string TeacherName, string LessonStart, string LessonEnd,
    DateTime? AttendanceTakenAt, bool TakenLate, List<AbsentNoticeItemDto> Items);

/// <summary>Chip counters (keys of <see cref="AbsentNoticeRules.CountKey"/>).</summary>
public record AbsentNoticeCountsDto(
    int Pending, int Awaiting, int Held, int Approved, int Sent, int Failed, int Cancelled, int Expired);

public record AbsentNoticeListDto(string Date, AbsentNoticeCountsDto Counts, List<AbsentNoticeLessonDto> Lessons);

public record AbsentNoticeSummaryDto(string Date, AbsentNoticeCountsDto Counts);

public record AbsentNoticeActionRequest(string? Note);

public record AbsentNoticeBulkRequest(List<string>? Ids, string? Action, string? Note);

public record AbsentNoticeBulkResult(int Changed, int Skipped);

/// <summary>
/// «Davomat xabarlari» control page reads (SPEC P3.3) — one day, grouped per lesson (group), counters per status
/// and Telegram delivery per notice. Read-only; the controller orchestrates writes.
/// </summary>
public static class AbsentNoticeQueries
{
    public static AbsentNoticeCountsDto Counts(IEnumerable<string> statuses)
    {
        var c = statuses.GroupBy(AbsentNoticeRules.CountKey).ToDictionary(g => g.Key, g => g.Count());
        int N(string k) => c.GetValueOrDefault(k);
        return new AbsentNoticeCountsDto(
            N("pending"), N("awaiting"), N("held"), N("approved"), N("sent"), N("failed"), N("cancelled"), N("expired"));
    }

    /// <summary>Counters of one day (home widget). Archived groups/students are not filtered: a notice is a notice.</summary>
    public static async Task<AbsentNoticeSummaryDto> SummaryAsync(IAppDbContext db, string date, CancellationToken ct = default)
    {
        var statuses = await db.AttendanceNotifyItems.AsNoTracking()
            .Where(i => i.Date == date).Select(i => i.Status).ToListAsync(ct);
        return new AbsentNoticeSummaryDto(date, Counts(statuses));
    }

    /// <summary>
    /// The day's queue. <paramref name="status"/> — a counter key (pending, awaiting, held, approved, sent, failed,
    /// cancelled, expired); counters are computed BEFORE the status filter (chips keep their numbers).
    /// <paramref name="teacherId"/> — the person teaching that day (substitute when assigned) or the group owner.
    /// </summary>
    public static async Task<AbsentNoticeListDto> ListAsync(
        IAppDbContext db, string date, string? status, string? groupId, string? teacherId, CancellationToken ct = default)
    {
        var q = db.AttendanceNotifyItems.AsNoTracking().Where(i => i.Date == date);
        if (!string.IsNullOrWhiteSpace(groupId)) q = q.Where(i => i.ClassId == groupId);
        var items = await q.ToListAsync(ct);

        var classIds = items.Select(i => i.ClassId).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking().Where(g => classIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId }).ToListAsync(ct);
        var groupById = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var substitutes = (await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => a.IsActive && classIds.Contains(a.GroupId)
                            && string.Compare(a.Date, date) <= 0 && string.Compare(a.EndDate ?? a.Date, date) >= 0)
                .ToListAsync(ct))
            .Where(a => SubstituteTeacherService.CoversDate(a, date))
            .GroupBy(a => a.GroupId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().SubstituteTeacherId, StringComparer.Ordinal);
        string TeacherOf(string classId) =>
            substitutes.TryGetValue(classId, out var sub) && !string.IsNullOrEmpty(sub)
                ? sub
                : groupById.TryGetValue(classId, out var g) ? g.TeacherId ?? "" : "";

        if (!string.IsNullOrWhiteSpace(teacherId))
            items = items.Where(i => TeacherOf(i.ClassId) == teacherId
                                     || (groupById.TryGetValue(i.ClassId, out var g) && g.TeacherId == teacherId))
                .ToList();

        var counts = Counts(items.Select(i => i.Status));
        if (!string.IsNullOrWhiteSpace(status))
            items = items.Where(i => AbsentNoticeRules.CountKey(i.Status) == status).ToList();
        if (items.Count == 0) return new AbsentNoticeListDto(date, counts, []);

        var names = await NamesAsync(db, items, ct);
        var teacherIds = items.Select(i => TeacherOf(i.ClassId)).Where(id => id.Length > 0).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName }).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var courseIds = groups.Select(g => g.CourseId ?? "").Where(id => id.Length > 0).Distinct().ToList();
        var courses = await db.Subjects.AsNoTracking().Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var marks = await AttendanceTaking.FirstMarksAsync(db, date, items.Select(i => i.ClassId).Distinct().ToList(), ct);
        var settings = AttendanceSettingsRules.FromMeta(await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct));

        var lessons = items
            .GroupBy(i => i.ClassId, StringComparer.Ordinal)
            .Select(g =>
            {
                groupById.TryGetValue(g.Key, out var grp);
                var first = g.OrderBy(i => i.CreatedAt).First();
                DateTime? takenAt = marks.TryGetValue(g.Key, out var t) ? t : null;
                var tid = TeacherOf(g.Key);
                // «Kelmadi» qatorlari bo'lsa — ularning saqlangan bayrog'i (holat AYNAN shu bilan hal bo'lgan);
                // faqat «keldi» bo'lsa — joriy muddat bo'yicha hisoblanadi.
                var absentRows = g.Where(i => i.Kind == AttendanceNotify.KindAbsent).ToList();
                var takenLate = absentRows.Count > 0
                    ? absentRows.Any(i => i.TakenLate)
                    : AbsentNoticeRules.TakenLate(date, first.LessonStart, takenAt, settings.DeadlineMinutes);
                return new AbsentNoticeLessonDto(
                    g.Key, grp?.Name ?? "", courses.GetValueOrDefault(grp?.CourseId ?? "", ""),
                    teachers.GetValueOrDefault(tid, ""), first.LessonStart, first.LessonEnd, takenAt,
                    takenLate,
                    g.OrderBy(i => i.Kind == AttendanceNotify.KindPresent)
                        .ThenBy(i => names.Students.GetValueOrDefault(i.StudentId, ""), StringComparer.CurrentCulture)
                        .Select(i => ToDto(i, names))
                        .ToList());
            })
            .OrderBy(l => ScheduleRules.ParseTime(l.LessonStart) ?? int.MaxValue)
            .ThenBy(l => l.GroupName, StringComparer.CurrentCulture)
            .ToList();
        return new AbsentNoticeListDto(date, counts, lessons);
    }

    /// <summary>DTO of a single (just changed) notice.</summary>
    public static async Task<AbsentNoticeItemDto> ItemAsync(IAppDbContext db, AttendanceNotifyItem item, CancellationToken ct = default) =>
        ToDto(item, await NamesAsync(db, [item], ct));

    internal sealed record Names(
        Dictionary<string, string> Students,
        Dictionary<string, (string Name, bool IsExcused)> Reasons,
        Dictionary<string, DeliverySummary> Delivery);

    private static async Task<Names> NamesAsync(IAppDbContext db, IReadOnlyCollection<AttendanceNotifyItem> items, CancellationToken ct)
    {
        var sids = items.Select(i => i.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => sids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var rids = items.Where(i => i.ReasonId != null).Select(i => i.ReasonId!).Distinct().ToList();
        var reasons = (await db.AbsenceReasons.AsNoTracking().Where(r => rids.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, r.IsExcused }).ToListAsync(ct))
            .ToDictionary(r => r.Id, r => (r.Name, r.IsExcused), StringComparer.Ordinal);
        var delivery = await TelegramOutbox.SummaryBySourceAsync(db, items.Select(i => i.Id).ToList(), ct);
        return new Names(students, reasons, delivery);
    }

    private static AbsentNoticeItemDto ToDto(AttendanceNotifyItem i, Names n)
    {
        var reason = i.ReasonId is not null && n.Reasons.TryGetValue(i.ReasonId, out var r) ? r : ("", false);
        AbsentNoticeDeliveryDto? delivery = n.Delivery.TryGetValue(i.Id, out var d)
            ? new AbsentNoticeDeliveryDto(d.Sent, d.Failed, d.Blocked, d.Acked)
            : null;
        return new AbsentNoticeItemDto(
            i.Id, i.Kind, i.StudentId, n.Students.GetValueOrDefault(i.StudentId, ""), reason.Item1, reason.Item2,
            i.Status, i.SendAfter, i.DecidedByName, i.DecidedAt, i.DecisionNote, i.CancelReason, i.Period, delivery);
    }
}
