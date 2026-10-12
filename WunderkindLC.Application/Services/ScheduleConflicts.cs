using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DOUBLE BOOKING rules (CRM upgrade Phase 5, docs/SPEC-crm-upgrade.md §P5.1) — pure, tested
/// (<c>ScheduleConflictsTests</c>). <see cref="RoomConflictService"/> is only the IO wrapper around it.
///
/// <para>Two groups CONFLICT when both are non-archived, both have weekdays and a valid time
/// (<see cref="ScheduleRules.MakeSlot"/>), they share a weekday, their half-open time intervals
/// overlap, their date ranges <c>[StartDate or −∞, EndDate or +∞]</c> overlap, and they share the room
/// (<c>RoomId</c>) or the teacher (<c>TeacherId</c>).</para>
///
/// <para>⚠️ Since 2026-10 a conflict is a HARD block (409) for everyone; only a superadmin may save anyway,
/// with a reason (<see cref="Decide"/>), and that is audited. Existing conflicting groups do NOT block
/// unrelated edits: the check runs only when a schedule field changed (<see cref="ScheduleChanged"/>).</para>
/// </summary>
public static class ScheduleConflicts
{
    public const string KindRoom = "room";
    public const string KindTeacher = "teacher";

    /// <summary>409 message (Uzbek, shown to the user).</summary>
    public const string BlockedMessage = "Jadvalda to'qnashuv bor — saqlanmadi";

    /// <summary>Superadmin pressed «Baribir saqlash» without a reason.</summary>
    public const string ReasonRequiredMessage = "Baribir saqlash uchun sababni yozing";

    /// <summary>Longest accepted override reason (audit row stays readable).</summary>
    public const int MaxReasonLength = 500;

    private static readonly string[] DayNames = ["Du", "Se", "Ch", "Pa", "Jum", "Sha", "Yak"];

    /// <summary>The schedule-relevant part of a group.</summary>
    public sealed record Slot(
        string GroupId, string GroupName, string RoomId, string TeacherId,
        IReadOnlyCollection<int> Days, string StartTime, string EndTime,
        string? StartDate, string? EndDate, bool IsArchived);

    /// <summary>One conflicting group. <paramref name="Kind"/> — <c>room</c> when the room is shared
    /// (checked first), otherwise <c>teacher</c>. <paramref name="Days"/> — shared weekdays (0=Monday).
    /// <paramref name="ExistingSlot"/> — the other group's time «09:00–10:00».</summary>
    public sealed record Conflict(string Kind, string GroupId, string GroupName, List<int> Days, string ExistingSlot);

    /// <summary>A conflicting PAIR of existing groups (schedule page banner). <paramref name="Slot"/> — the
    /// overlapping interval «15:00–15:30».</summary>
    public sealed record Pair(
        string Kind, string GroupAId, string GroupAName, string GroupBId, string GroupBName,
        List<int> Days, string Slot);

    public static Slot Of(Group g) => new(
        g.Id, g.Name, g.RoomId ?? "", g.TeacherId ?? "", g.Days ?? [], g.StartTime ?? "", g.EndTime ?? "",
        g.StartDate, g.EndDate, g.IsArchived);

    /// <summary>Normalised "yyyy-MM-dd" (first 10 chars); empty/broken → <c>null</c> (= unbounded side).</summary>
    public static string? DateKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Length > 10) s = s[..10];
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d.ToString("yyyy-MM-dd") : null;
    }

    /// <summary>Do <c>[s1, e1]</c> and <c>[s2, e2]</c> overlap (inclusive days; <c>null</c> = unbounded)?</summary>
    public static bool DateRangesOverlap(string? s1, string? e1, string? s2, string? e2)
    {
        var a1 = DateKey(s1);
        var b1 = DateKey(e1);
        var a2 = DateKey(s2);
        var b2 = DateKey(e2);
        // A starts after B ended, or B starts after A ended → no overlap.
        if (a1 is not null && b2 is not null && string.CompareOrdinal(a1, b2) > 0) return false;
        if (a2 is not null && b1 is not null && string.CompareOrdinal(a2, b1) > 0) return false;
        return true;
    }

    /// <summary>Valid weekday set (0..6, distinct, ascending).</summary>
    public static List<int> CleanDays(IEnumerable<int>? days) =>
        (days ?? []).Where(d => d is >= 0 and <= 6).Distinct().Order().ToList();

    /// <summary>"Du, Ch, Jum" — the same short names the old warning used (<see cref="RoomConflictService"/>).</summary>
    public static string FormatDays(IEnumerable<int> days) =>
        string.Join(", ", CleanDays(days).Select(d => DayNames[d]));

    private static (int Start, int End)? TimeOf(Slot s) =>
        ScheduleRules.MakeSlot(0, s.StartTime, s.EndTime) is { } x ? (x.StartMin, x.EndMin) : null;

    /// <summary>Does <paramref name="candidate"/> conflict with <paramref name="other"/>? <c>null</c> = no.</summary>
    public static Conflict? Between(Slot candidate, Slot other)
    {
        if (candidate.IsArchived || other.IsArchived) return null;
        if (candidate.GroupId.Length > 0 && candidate.GroupId == other.GroupId) return null;

        var sameRoom = candidate.RoomId.Length > 0 && candidate.RoomId == other.RoomId;
        var sameTeacher = candidate.TeacherId.Length > 0 && candidate.TeacherId == other.TeacherId;
        if (!sameRoom && !sameTeacher) return null;

        var shared = CleanDays(candidate.Days).Intersect(CleanDays(other.Days)).ToList();
        if (shared.Count == 0) return null;

        if (TimeOf(candidate) is not { } a || TimeOf(other) is not { } b) return null;
        if (!ScheduleRules.Overlaps(a.Start, a.End, b.Start, b.End)) return null;

        if (!DateRangesOverlap(candidate.StartDate, candidate.EndDate, other.StartDate, other.EndDate)) return null;

        return new Conflict(
            sameRoom ? KindRoom : KindTeacher, other.GroupId, other.GroupName, shared,
            $"{ScheduleRules.FormatTime(b.Start)}–{ScheduleRules.FormatTime(b.End)}");
    }

    /// <summary>Every group <paramref name="candidate"/> conflicts with (one entry per group, by name).</summary>
    public static List<Conflict> Find(Slot candidate, IEnumerable<Slot> others) =>
        others.Select(o => Between(candidate, o)).OfType<Conflict>()
            .OrderBy(c => c.GroupName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Every conflicting pair among <paramref name="groups"/> (each pair once, A before B by name).</summary>
    public static List<Pair> AllPairs(IEnumerable<Slot> groups)
    {
        var list = groups.Where(g => !g.IsArchived && TimeOf(g) is not null && CleanDays(g.Days).Count > 0)
            .OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.GroupId, StringComparer.Ordinal)
            .ToList();
        var result = new List<Pair>();
        for (var i = 0; i < list.Count; i++)
        for (var j = i + 1; j < list.Count; j++)
        {
            var a = list[i];
            var b = list[j];
            if (Between(a, b) is not { } c) continue;
            var ta = TimeOf(a)!.Value;
            var tb = TimeOf(b)!.Value;
            var start = Math.Max(ta.Start, tb.Start);
            var end = Math.Min(ta.End, tb.End);
            result.Add(new Pair(c.Kind, a.GroupId, a.GroupName, b.GroupId, b.GroupName, c.Days,
                $"{ScheduleRules.FormatTime(start)}–{ScheduleRules.FormatTime(end)}"));
        }
        return result;
    }

    /// <summary>
    /// Did any field the conflict check depends on change (room, teacher, weekdays, start/end time, start/end
    /// date)? An edit of an unrelated field (name, note, fee) of an ALREADY conflicting group is not blocked.
    /// </summary>
    public static bool ScheduleChanged(Slot before, Slot after) =>
        before.RoomId != after.RoomId
        || before.TeacherId != after.TeacherId
        || !CleanDays(before.Days).SequenceEqual(CleanDays(after.Days))
        || (before.StartTime ?? "").Trim() != (after.StartTime ?? "").Trim()
        || (before.EndTime ?? "").Trim() != (after.EndTime ?? "").Trim()
        || DateKey(before.StartDate) != DateKey(after.StartDate)
        || DateKey(before.EndDate) != DateKey(after.EndDate);

    /// <summary>Readable list for the audit row: «G1» (xona, Du, Ch 09:00–10:00); «G2» (o'qituvchi, …).</summary>
    public static string Describe(IEnumerable<Conflict> conflicts) =>
        string.Join("; ", conflicts.Select(c =>
            $"«{c.GroupName}» ({(c.Kind == KindRoom ? "xona" : "o'qituvchi")}, {FormatDays(c.Days)} {c.ExistingSlot})"));

    public enum Verdict
    {
        /// <summary>No conflict — save.</summary>
        Ok,
        /// <summary>Conflict, overridden by a superadmin with a reason — save and audit.</summary>
        Forced,
        /// <summary>Conflict — 409, nothing is saved.</summary>
        Blocked,
    }

    /// <param name="CanForce">The caller is a superadmin — the UI offers «Baribir saqlash» + «Sabab».</param>
    public sealed record Decision(Verdict Verdict, string Message, bool CanForce, string Reason);

    /// <summary>
    /// What to do with a save that has <paramref name="conflictCount"/> conflicts. <c>force</c> is honoured
    /// ONLY for a superadmin with a non-empty reason; anybody else gets the 409 (no silent downgrade).
    /// </summary>
    public static Decision Decide(int conflictCount, bool force, string? reason, bool isSuperAdmin)
    {
        var why = (reason ?? "").Trim();
        if (why.Length > MaxReasonLength) why = why[..MaxReasonLength];
        if (conflictCount == 0) return new Decision(Verdict.Ok, "", isSuperAdmin, "");
        if (force && isSuperAdmin && why.Length > 0) return new Decision(Verdict.Forced, "", true, why);
        var message = force && isSuperAdmin ? ReasonRequiredMessage : BlockedMessage;
        return new Decision(Verdict.Blocked, message, isSuperAdmin, "");
    }

    /// <summary>Audit summary of a forced save.</summary>
    public static string ForcedAuditText(IEnumerable<Conflict> conflicts, string reason) =>
        $"Jadval to'qnashuvi bilan saqlandi: {Describe(conflicts)} — sabab: {reason}";

    // ---------------------------------------------------------------------------------------------
    //  ONE LESSON on a DATE (reschedule) — the target time against that day's real lessons
    // ---------------------------------------------------------------------------------------------

    /// <summary>A lesson on the target date: room and the teacher who teaches it THAT day (substitute if any).</summary>
    public sealed record DayLesson(string GroupId, string GroupName, string RoomId, string TeacherId, string Start, string End);

    /// <summary>
    /// First lesson that occupies the room or the teacher during <c>[start, end)</c> — <c>null</c> when free.
    /// Lessons of <paramref name="ownGroupId"/> are ignored.
    /// </summary>
    public static DayLesson? LessonClash(
        string ownGroupId, string roomId, string teacherId, string start, string end, IEnumerable<DayLesson> lessons)
    {
        if (ScheduleRules.MakeSlot(0, start, end) is not { } me) return null;
        foreach (var l in lessons
                     .Where(l => l.GroupId != ownGroupId)
                     .OrderBy(l => ScheduleRules.ParseTime(l.Start) ?? int.MaxValue)
                     .ThenBy(l => l.GroupName, StringComparer.OrdinalIgnoreCase))
        {
            var sameRoom = roomId.Length > 0 && l.RoomId == roomId;
            var sameTeacher = teacherId.Length > 0 && l.TeacherId == teacherId;
            if (!sameRoom && !sameTeacher) continue;
            if (ScheduleRules.MakeSlot(0, l.Start, l.End) is not { } other) continue;
            if (ScheduleRules.Overlaps(me.StartMin, me.EndMin, other.StartMin, other.EndMin)) return l;
        }
        return null;
    }

    /// <summary>The reschedule refusal text: «Bu vaqtda xona/o'qituvchi band: G 14:00–15:30».</summary>
    public static string LessonClashMessage(DayLesson l) =>
        $"Bu vaqtda xona/o'qituvchi band: {l.GroupName} {ScheduleRules.FormatTime(ScheduleRules.ParseTime(l.Start) ?? 0)}–" +
        $"{ScheduleRules.FormatTime(ScheduleRules.ParseTime(l.End) ?? 0)}";
}

/// <summary>
/// A refused reschedule because the target slot is occupied (<see cref="ScheduleConflicts.LessonClash"/>).
/// Subclass of <see cref="InvalidOperationException"/>, so existing callers keep returning 400 with the
/// message; the admin journal endpoint maps it to 409 + <c>canForce</c> for a superadmin.
/// </summary>
public sealed class ScheduleConflictException(string message, ScheduleConflicts.DayLesson clash)
    : InvalidOperationException(message)
{
    public ScheduleConflicts.DayLesson Clash { get; } = clash;
}
