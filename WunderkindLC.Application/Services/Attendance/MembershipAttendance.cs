using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Attendance of every (student, group) for the AGGREGATE reports — the centre rating (<c>RatingService.SchoolAsync</c>
/// and its MCP mirror), the teacher rating (<c>StudentBallService.TeacherAsync</c>, MCP <c>rating_group</c>) and the
/// dashboard group attendance — under the ONE attendance definition (<see cref="AttendanceRules.Classify(bool,bool,bool,bool,bool,int?,bool,AbsenceReason?)"/>,
/// <c>.claude/rules/journal.md</c> «YAGONA DAVOMAT TA'RIFI»), computed with SQL aggregates only (the journal is never
/// loaded into memory). Before 2026-10-12 these reports used (conducted − absent) ÷ conducted: an unmarked student of a
/// conducted lesson counted as PRESENT, i.e. one grade made the whole group «attended».
///
/// <para>Per conducted lesson of the group, a student is:</para>
/// <list type="bullet">
///   <item>a reason → <b>Late</b> (attended) / <b>AbsentExcused</b> / <b>AbsentUnexcused</b> (unknown reason — unexcused);</item>
///   <item>no reason + «keldi» / grade / mastery → <b>Present</b>;</item>
///   <item>no mark, attendance TAKEN (complete) and the date inside the membership's implicit window
///     (<see cref="ImplicitWindow"/>) → <b>Present</b>;</item>
///   <item>otherwise → Unmarked / Blocked — EXCLUDED from the percentage.</item>
/// </list>
/// <para>Explicit marks are counted on every conducted lesson of the group (the journal only accepts them inside the
/// membership — <c>JournalService.MemberWriteError</c>); the implicit «keldi» only inside the window
/// [activation (or join) ∨ recorded day .. frozen / left day]. A student WITHOUT a live membership (legacy
/// <c>ClassName</c> fallback of the centre rating) is passed in <c>unboundedPairs</c> — no window.</para>
/// </summary>
public static class MembershipAttendance
{
    /// <summary>One (student, group) result. <see cref="Percent"/> = round(attended × 100 / held), null when nothing held.</summary>
    public sealed record Stat(int Present, int Late, int AbsentExcused, int AbsentUnexcused)
    {
        public static readonly Stat Empty = new(0, 0, 0, 0);
        public int Held => Present + Late + AbsentExcused + AbsentUnexcused;
        public int Attended => Present + Late;
        public int Absent => AbsentExcused + AbsentUnexcused;
        public int? Percent => Held == 0 ? null : AttendanceRules.PercentOf(Attended, Held);
        public Stat Plus(Stat o) =>
            new(Present + o.Present, Late + o.Late, AbsentExcused + o.AbsentExcused, AbsentUnexcused + o.AbsentUnexcused);
    }

    /// <summary>The membership fields the window needs (a projection of <see cref="StudentGroup"/>).</summary>
    public sealed record Member(
        string Id, string StudentId, string GroupId, string Status,
        string JoinedAt, string ActivatedAt, string FrozenAt, string? LeftAt, string RecordedAt);

    private static string? Day(string? iso) => iso is { Length: >= 10 } ? iso[..10] : null;

    /// <summary>
    /// Inclusive day window ("yyyy-MM-dd", <c>null</c> = open) in which an UNMARKED student of a lesson whose attendance
    /// was taken counts as present: from max(activation day — or join day when never activated, recorded day
    /// = <c>PresentDefaultFrom</c>) to the freeze day (frozen) or the leave day. The SQL in <see cref="ComputeAsync"/>
    /// applies EXACTLY the same bounds (<c>MembershipAttendanceTests</c> lock both).
    /// </summary>
    public static (string? From, string? To) ImplicitWindow(Member m)
    {
        var start = Day(m.ActivatedAt) ?? Day(m.JoinedAt);
        var recorded = Day(m.RecordedAt);
        var from = start is null ? recorded
            : recorded is null ? start
            : string.CompareOrdinal(start, recorded) >= 0 ? start : recorded;
        var to = m.Status == "frozen" ? Day(m.FrozenAt) : Day(m.LeftAt);
        return (from, to);
    }

    /// <summary>Is <paramref name="date"/> inside the window (inclusive).</summary>
    public static bool InWindow(string date, (string? From, string? To) w) =>
        (w.From is null || string.CompareOrdinal(date, w.From) >= 0)
        && (w.To is null || string.CompareOrdinal(date, w.To) <= 0);

    /// <summary>
    /// Per (student, group) stats for every LIVE membership (<c>IsActive</c>) of <paramref name="groupIds"/> (null = the
    /// whole centre) plus the <paramref name="unboundedPairs"/>. Two live memberships of the same pair (bad legacy data)
    /// count once — the one an active/newer state wins (<c>RatingService</c> order). <paramref name="month"/> ("yyyy-MM")
    /// narrows lessons and marks to that month.
    /// </summary>
    public static async Task<Dictionary<(string StudentId, string GroupId), Stat>> ComputeAsync(
        IAppDbContext db, IReadOnlyCollection<string>? groupIds = null, string? month = null,
        IReadOnlyCollection<(string StudentId, string GroupId)>? unboundedPairs = null, CancellationToken ct = default)
    {
        var gids = groupIds?.Distinct().ToList();
        var reasons = await db.AbsenceReasons.AsNoTracking()
            .Select(r => new { r.Id, r.IsLate, r.IsExcused }).ToListAsync(ct);
        var reasonById = reasons.ToDictionary(r => r.Id, StringComparer.Ordinal);

        // ---- live memberships (one per pair) ----
        var mq = db.StudentGroups.AsNoTracking().Where(sg => sg.IsActive);
        if (gids is not null) mq = mq.Where(sg => gids.Contains(sg.GroupId));
        var members = (await mq.Select(sg => new Member(
                    sg.Id, sg.StudentId, sg.GroupId, sg.Status, sg.JoinedAt, sg.ActivatedAt, sg.FrozenAt, sg.LeftAt,
                    sg.RecordedAt))
                .ToListAsync(ct))
            .GroupBy(m => (m.StudentId, m.GroupId))
            .Select(g => g.OrderBy(m => MembershipLifecycle.StateRank(m.Status))
                .ThenByDescending(m => m.JoinedAt, StringComparer.Ordinal)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .First())
            .ToList();

        var pairs = members.Select(m => (m.StudentId, m.GroupId)).ToHashSet();
        var extra = (unboundedPairs ?? Array.Empty<(string, string)>())
            .Where(p => !pairs.Contains(p)).Distinct().ToList();
        var result = new Dictionary<(string StudentId, string GroupId), Stat>();
        if (members.Count == 0 && extra.Count == 0) return result;

        var conducted = db.LessonNotes.Where(n => n.Conducted);
        var taken = db.LessonNotes.Where(n => n.Conducted && n.AttendanceTaken);
        var allGroups = members.Select(m => m.GroupId).Concat(extra.Select(p => p.GroupId)).Distinct().ToList();

        // ---- 1) explicit marks on a conducted lesson — counted per LESSON CELL (a duplicate journal row of the same
        //         cell counts once; a reason WINS over «keldi»/grade on the same cell — Classify's order) ----
        var entries = db.JournalEntries.AsNoTracking()
            .Where(e => conducted.Any(n => n.ClassId == e.ClassId && n.SubjectId == e.SubjectId
                                           && n.Date == e.Date && n.Period == e.Period));
        if (gids is not null) entries = entries.Where(e => allGroups.Contains(e.ClassId));
        if (month is not null) entries = entries.Where(e => e.Date.StartsWith(month));
        var reasonCells = await entries.Where(e => e.ReasonId != null)
            .Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period, e.ReasonId }).Distinct()
            .GroupBy(x => new { x.StudentId, x.ClassId, x.ReasonId })
            .Select(g => new { g.Key.StudentId, g.Key.ClassId, g.Key.ReasonId, N = g.Count() })
            .ToListAsync(ct);
        var presentCells = await entries
            .Where(e => e.ReasonId == null && (e.Present || e.Grade != null || e.Mastery != null)
                        && !db.JournalEntries.Any(o => o.StudentId == e.StudentId && o.ClassId == e.ClassId
                                                       && o.SubjectId == e.SubjectId && o.Date == e.Date
                                                       && o.Period == e.Period && o.ReasonId != null))
            .Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period }).Distinct()
            .GroupBy(x => new { x.StudentId, x.ClassId })
            .Select(g => new { g.Key.StudentId, g.Key.ClassId, N = g.Count() })
            .ToListAsync(ct);
        var explicitBy = new Dictionary<(string, string), Stat>();
        void AddExplicit((string, string) key, Stat add) =>
            explicitBy[key] = explicitBy.GetValueOrDefault(key, Stat.Empty).Plus(add);
        foreach (var r in reasonCells)
            AddExplicit((r.StudentId, r.ClassId),
                reasonById.TryGetValue(r.ReasonId!, out var reason) && reason.IsLate ? new Stat(0, r.N, 0, 0)
                : reasonById.TryGetValue(r.ReasonId!, out var reason2) && reason2.IsExcused ? new Stat(0, 0, r.N, 0)
                : new Stat(0, 0, 0, r.N));   // unknown reason — unexcused (as the old counts treated every ReasonId)
        foreach (var r in presentCells) AddExplicit((r.StudentId, r.ClassId), new Stat(r.N, 0, 0, 0));

        // ---- 2) attendance-TAKEN lessons per (group, day) — the base of the implicit «keldi» ----
        var tq = taken.AsNoTracking().Where(n => allGroups.Contains(n.ClassId));
        if (month is not null) tq = tq.Where(n => n.Date.StartsWith(month));
        var takenDays = (await tq.Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period }).Distinct()
                .GroupBy(x => new { x.ClassId, x.Date })
                .Select(g => new { g.Key.ClassId, g.Key.Date, N = g.Count() })
                .ToListAsync(ct))
            .ToLookup(x => x.ClassId, StringComparer.Ordinal);

        // ---- 3) classified entries on a TAKEN lesson inside each membership's window (they are not implicit) ----
        var classifiedByMember = new Dictionary<string, int>(StringComparer.Ordinal);
        if (members.Count > 0)
        {
            var cq =
                from sg in mq
                join e in db.JournalEntries.AsNoTracking()
                    on new { sg.StudentId, ClassId = sg.GroupId } equals new { e.StudentId, e.ClassId }
                where (e.ReasonId != null || e.Present || e.Grade != null || e.Mastery != null)
                      && taken.Any(n => n.ClassId == e.ClassId && n.SubjectId == e.SubjectId
                                        && n.Date == e.Date && n.Period == e.Period)
                      // window FROM — ImplicitWindow: activation day (or join day), and the recorded day
                      && ((sg.ActivatedAt.Length >= 10 && string.Compare(e.Date, sg.ActivatedAt.Substring(0, 10)) >= 0)
                          || (sg.ActivatedAt.Length < 10
                              && (sg.JoinedAt.Length < 10 || string.Compare(e.Date, sg.JoinedAt.Substring(0, 10)) >= 0)))
                      && (sg.RecordedAt.Length < 10 || string.Compare(e.Date, sg.RecordedAt.Substring(0, 10)) >= 0)
                      // window TO — the freeze day (frozen) or the leave day
                      && ((sg.Status == "frozen"
                           && (sg.FrozenAt.Length < 10 || string.Compare(e.Date, sg.FrozenAt.Substring(0, 10)) <= 0))
                          || (sg.Status != "frozen"
                              && (sg.LeftAt == null || sg.LeftAt!.Length < 10
                                  || string.Compare(e.Date, sg.LeftAt!.Substring(0, 10)) <= 0)))
                select new { sg.Id, e.SubjectId, e.Date, e.Period };
            if (month is not null) cq = cq.Where(x => x.Date.StartsWith(month));
            // Distinct CELLS (a duplicate journal row of one lesson must not make the implicit count negative-ish).
            foreach (var r in await cq.Distinct().GroupBy(x => x.Id).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct))
                classifiedByMember[r.Id] = r.N;
        }

        // ---- 3b) the same without a window for the unbounded pairs ----
        var classifiedExtra = new Dictionary<(string, string), int>();
        if (extra.Count > 0)
        {
            var sids = extra.Select(p => p.StudentId).Distinct().ToList();
            var xq = db.JournalEntries.AsNoTracking()
                .Where(e => sids.Contains(e.StudentId)
                            && (e.ReasonId != null || e.Present || e.Grade != null || e.Mastery != null)
                            && taken.Any(n => n.ClassId == e.ClassId && n.SubjectId == e.SubjectId
                                              && n.Date == e.Date && n.Period == e.Period));
            if (month is not null) xq = xq.Where(e => e.Date.StartsWith(month));
            foreach (var r in await xq.Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period }).Distinct()
                         .GroupBy(x => new { x.StudentId, x.ClassId })
                         .Select(g => new { g.Key.StudentId, g.Key.ClassId, N = g.Count() }).ToListAsync(ct))
                classifiedExtra[(r.StudentId, r.ClassId)] = r.N;
        }

        foreach (var m in members)
        {
            var w = ImplicitWindow(m);
            var takenInWindow = takenDays[m.GroupId].Where(d => InWindow(d.Date, w)).Sum(d => d.N);
            var implicitPresent = Math.Max(0, takenInWindow - classifiedByMember.GetValueOrDefault(m.Id));
            var e = explicitBy.GetValueOrDefault((m.StudentId, m.GroupId), Stat.Empty);
            result[(m.StudentId, m.GroupId)] = e.Plus(new Stat(implicitPresent, 0, 0, 0));
        }
        foreach (var p in extra)
        {
            var takenAll = takenDays[p.GroupId].Sum(d => d.N);
            var implicitPresent = Math.Max(0, takenAll - classifiedExtra.GetValueOrDefault(p));
            var e = explicitBy.GetValueOrDefault(p, Stat.Empty);
            result[p] = e.Plus(new Stat(implicitPresent, 0, 0, 0));
        }
        return result;
    }
}
