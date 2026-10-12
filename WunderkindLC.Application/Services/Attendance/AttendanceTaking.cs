using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "Was attendance TAKEN?" — two different questions, deliberately kept apart (SPEC P3.1):
/// <list type="bullet">
///   <item><b>first mark</b> — <see cref="LessonNote.AttendanceTakenAt"/>: the first «keldi» or absence/late
///     reason of the lesson (single cell or bulk). Teacher reminders, fines, the monitor and absent notices read
///     THIS, per (group, date), subject/period agnostic — a teacher marking cell by cell is never fined.</item>
///   <item><b>complete</b> — <see cref="LessonNote.AttendanceTaken"/>: bulk attendance, or every expected
///     student marked. Only this switches on the "no mark = present" default (<see cref="AttendanceRules"/>).</item>
/// </list>
/// A grade alone is neither.
/// </summary>
public static class AttendanceTaking
{
    /// <summary>The cell carries an ATTENDANCE mark (present or a reason) — a grade alone does not.</summary>
    public static bool IsAttendanceMark(bool present, string? reasonId) => present || reasonId is not null;

    /// <summary>Stamps the FIRST attendance mark once — later marks never move it.</summary>
    public static void StampFirstMark(LessonNote note, DateTime now, string? actorId)
    {
        if (note.AttendanceTakenAt is not null) return;
        note.AttendanceTakenAt = now;
        note.AttendanceTakenById = string.IsNullOrWhiteSpace(actorId) ? null : actorId;
    }

    /// <summary>
    /// Is the lesson COMPLETE: every student EXPECTED on that date (<see cref="StudentJournalBuilder.ExpectedOn"/>,
    /// not archived) has «keldi», a reason or a grade in this lesson. On the TEACHER path, students hidden by the
    /// payment gate (<see cref="JournalPolicy.PaymentHiddenStudentsAsync"/>) are not expected — the teacher cannot
    /// see them (bulk attendance skips them too). A lesson with nobody expected is never "complete".
    /// </summary>
    public static async Task<bool> LessonCompleteAsync(
        IAppDbContext db, string classId, string subjectId, int quarter, string date, int period, bool teacher,
        CancellationToken ct = default)
    {
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.GroupId == classId).ToListAsync(ct);
        var candidateIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        if (candidateIds.Count == 0) return false;
        var archived = (await db.Students.AsNoTracking()
                .Where(s => candidateIds.Contains(s.Id) && s.IsArchived).Select(s => s.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var expected = memberships
            .Where(m => !archived.Contains(m.StudentId) && StudentJournalBuilder.ExpectedOn(m, date))
            .Select(m => m.StudentId)
            .ToHashSet(StringComparer.Ordinal);
        if (teacher && expected.Count > 0)
            expected.ExceptWith(await JournalPolicy.PaymentHiddenStudentsAsync(db, classId, expected));
        if (expected.Count == 0) return false;

        var ids = expected.ToList();
        var marked = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter
                            && e.Date == date && e.Period == period && ids.Contains(e.StudentId)
                            && (e.Present || e.ReasonId != null || e.Grade != null))
                .Select(e => e.StudentId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        return expected.IsSubsetOf(marked);
    }

    /// <summary>
    /// First attendance mark per group on <paramref name="date"/> — ANY lesson note of (group, date) with
    /// <see cref="LessonNote.AttendanceTakenAt"/> (earliest wins). Groups without one are absent from the map.
    /// </summary>
    public static async Task<Dictionary<string, DateTime>> FirstMarksAsync(
        IAppDbContext db, string date, IReadOnlyCollection<string> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return new(StringComparer.Ordinal);
        var ids = groupIds.ToList();
        var rows = await db.LessonNotes.AsNoTracking()
            .Where(n => n.Date == date && ids.Contains(n.ClassId) && n.AttendanceTakenAt != null)
            .Select(n => new { n.ClassId, n.AttendanceTakenAt })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.ClassId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(r => r.AttendanceTakenAt!.Value), StringComparer.Ordinal);
    }

    /// <summary>First attendance mark of one group's lesson day, or <c>null</c>.</summary>
    public static async Task<DateTime?> FirstMarkAsync(
        IAppDbContext db, string groupId, string date, CancellationToken ct = default)
    {
        var map = await FirstMarksAsync(db, date, new[] { groupId }, ct);
        return map.TryGetValue(groupId, out var at) ? at : null;
    }
}
