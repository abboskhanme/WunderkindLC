using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Rating;

/// <summary>Group name/course/teacher for display (every group of the centre — small table).</summary>
public sealed record RatingGroupInfo(string Id, string Name, string CourseName, string TeacherName, bool IsArchived);

/// <summary>
/// The computed rating of ONE period (cached as a whole — <see cref="RatingEngine.Cached"/>). Immutable:
/// every endpoint (centre table, group table, student block, cabinet) slices this object, so the numbers are
/// the same everywhere.
/// </summary>
public sealed record RatingResult(
    RatingPeriod Period,
    int TestWeight,
    IReadOnlyList<GroupScoreRow> Groups,
    IReadOnlyList<CentreScoreRow> Centre,
    IReadOnlyDictionary<string, string> StudentNames,
    IReadOnlyDictionary<string, RatingGroupInfo> GroupInfo,
    int HomeworkWeight = 0)
{
    public IReadOnlyDictionary<string, CentreScoreRow> CentreByStudent { get; } =
        Centre.ToDictionary(c => c.StudentId, StringComparer.Ordinal);

    public ILookup<string, GroupScoreRow> RowsByGroup { get; } = Groups.ToLookup(g => g.GroupId, StringComparer.Ordinal);

    public ILookup<string, GroupScoreRow> RowsByStudent { get; } = Groups.ToLookup(g => g.StudentId, StringComparer.Ordinal);

    /// <summary>Ranked students per group (the "/ 18" of «Guruhda: 3-o'rin / 18»).</summary>
    public IReadOnlyDictionary<string, int> RankedInGroup { get; } = Groups
        .Where(g => g.Rank is not null)
        .GroupBy(g => g.GroupId, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    /// <summary>Students with a centre rank (the "/ 430" of «Markazda: 12-o'rin / 430»).</summary>
    public int CentreTotal { get; } = Centre.Count(c => c.Rank is not null);

    public string NameOf(string studentId) => StudentNames.GetValueOrDefault(studentId, "");
}

/// <summary>Loads the rating of a period — cached in production, direct in tests.</summary>
public delegate Task<RatingResult> RatingLoader(RatingPeriod period);

/// <summary>
/// RATING ENGINE (docs/SPEC-crm-upgrade.md P4.3, rules: <c>.claude/rules/rating.md</c>).
///
/// <para><b>SQL aggregates only — the journal is NEVER loaded row by row.</b> Ball comes from
/// <see cref="StudentBallService.ComputeByGroupAsync(IAppDbContext, IReadOnlyCollection{string}?, string, string)"/>
/// (GROUP BY in the database). Test scores are read as a (student, test, score, max) projection joined to the
/// period's tests — small (1–2 tests per group per month), and decimal aggregation is not portable to SQLite.
/// The query count is a fixed number, independent of data volume (<c>RatingEngineTests</c> lock it).</para>
///
/// <para>Test percent = score ÷ max score — today's offline tests as they are; once U4a sets
/// <c>MaxScore = QuestionCount</c> and score = solved count, the same formula gives solved ÷ questions, i.e.
/// nothing here changes.</para>
///
/// <para>No debtor lock: balance is never read (owner decision).</para>
/// </summary>
public static class RatingEngine
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>Every entity <see cref="ComputeAsync"/> reads — a change in any of them recomputes.</summary>
    public static readonly string[] CacheDeps =
    [
        nameof(TestScore), nameof(TestResult), nameof(JournalEntry), nameof(CriterionGrade),
        nameof(StudentBallAdjustment), nameof(StudentGroup), nameof(Student), nameof(Group), nameof(CenterMeta),
        nameof(Subject), nameof(Teacher),
    ];

    /// <summary>Cache key: <c>rating:v2:{kind}:{key}</c> + the clipped end date (the current period grows daily).</summary>
    public static string CacheKey(RatingPeriod p) => $"rating:v2:{p.Kind}:{p.Key}:{p.To}";

    public static RatingLoader Direct(IAppDbContext db) => p => ComputeAsync(db, p);

    public static RatingLoader Cached(DataCache cache) =>
        p => cache.GetOrCreateAsync(CacheKey(p), CacheDeps, CacheTtl, db => ComputeAsync(db, p));

    /// <summary>Configured test weight (0..100); the owner default 60 when the settings row does not exist.</summary>
    public static async Task<int> TestWeightAsync(IAppDbContext db, CancellationToken ct = default) =>
        RatingFormula.ClampWeight(
            await db.CenterMeta.AsNoTracking().Select(m => (int?)m.RatingTestWeight).FirstOrDefaultAsync(ct)
            ?? RatingFormula.DefaultTestWeight);

    /// <summary>Test and homework weights in ONE query (the engine's query budget is fixed — <c>RatingEngineTests</c>);
    /// homework (default 0 — SPEC-backlog A3) is cut so test + homework ≤ 100.</summary>
    public static async Task<(int Test, int Homework)> WeightsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var row = await db.CenterMeta.AsNoTracking()
            .Select(m => new { m.RatingTestWeight, m.RatingHomeworkWeight }).FirstOrDefaultAsync(ct);
        var w = RatingFormula.Weights(row?.RatingTestWeight ?? RatingFormula.DefaultTestWeight, row?.RatingHomeworkWeight ?? 0);
        return (w.Test, w.Homework);
    }

    /// <summary>Homework points of one marked lesson: done (1) 100, partly (3) 50, not done (2) 0; anything else — not marked.</summary>
    public static double? HomeworkPoints(int mark) => mark switch { 1 => 100, 3 => 50, 2 => 0, _ => null };

    /// <summary>Computes the whole rating of <paramref name="p"/> (all groups, the centre).</summary>
    public static async Task<RatingResult> ComputeAsync(IAppDbContext db, RatingPeriod p, CancellationToken ct = default)
    {
        var (weight, homeworkWeight) = await WeightsAsync(db, ct);

        // Scores of the period's tests (one query, joined — no id list). Max ≤ 0 tests are ignored by Percent().
        var scores = await (from s in db.TestScores.AsNoTracking()
                            join t in db.TestResults.AsNoTracking() on s.TestResultId equals t.Id
                            where t.Date.CompareTo(p.From) >= 0 && t.Date.CompareTo(p.To) <= 0
                            select new { s.StudentId, t.GroupId, s.Score, t.MaxScore })
            .ToListAsync(ct);

        // Ball per (student, group) over the date range — GROUP BY in SQL.
        var balls = await StudentBallService.ComputeByGroupAsync(db, null, p.From, p.To);

        var names = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, StringComparer.Ordinal, ct);

        // Homework marks per (student, group) — GROUP BY in SQL; one journal cell counts once (duplicate rows).
        var homework = await db.JournalEntries.AsNoTracking()
            .Where(e => e.Homework > 0 && string.Compare(e.Date, p.From) >= 0 && string.Compare(e.Date, p.To) <= 0)
            .Select(e => new { e.StudentId, e.ClassId, e.SubjectId, e.Date, e.Period, e.Homework })
            .Distinct()
            .GroupBy(x => new { x.StudentId, x.ClassId, x.Homework })
            .Select(g => new { g.Key.StudentId, g.Key.ClassId, g.Key.Homework, N = g.Count() })
            .ToListAsync(ct);

        var inputs = new Dictionary<(string StudentId, string GroupId), (double Sum, int Count, int Ball, double HwSum, int HwCount)>();
        foreach (var s in scores)
        {
            var pct = RatingFormula.Percent(s.Score, s.MaxScore);
            if (pct is null) continue;
            var key = (s.StudentId, s.GroupId);
            var cur = inputs.GetValueOrDefault(key);
            inputs[key] = cur with { Sum = cur.Sum + pct.Value, Count = cur.Count + 1 };
        }
        foreach (var (key, stat) in balls)
        {
            var cur = inputs.GetValueOrDefault(key);
            inputs[key] = cur with { Ball = stat.Ball };
        }
        foreach (var h in homework)
        {
            if (HomeworkPoints(h.Homework) is not { } pts) continue;
            var key = (h.StudentId, h.ClassId);
            var cur = inputs.GetValueOrDefault(key);
            inputs[key] = cur with { HwSum = cur.HwSum + pts * h.N, HwCount = cur.HwCount + h.N };
        }

        var table = RatingFormula.Compute(
            inputs.Select(kv => new RatingInput(
                kv.Key.StudentId, kv.Key.GroupId, kv.Value.Sum, kv.Value.Count, kv.Value.Ball, kv.Value.HwSum, kv.Value.HwCount)),
            weight, names, homeworkWeight);

        var used = table.Groups.Select(g => g.StudentId).ToHashSet(StringComparer.Ordinal);
        var studentNames = names.Where(kv => used.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        return new RatingResult(p, weight, table.Groups, table.Centre, studentNames, await GroupInfoAsync(db, ct), homeworkWeight);
    }

    /// <summary>All groups with course and teacher names (three small queries).</summary>
    private static async Task<Dictionary<string, RatingGroupInfo>> GroupInfoAsync(IAppDbContext db, CancellationToken ct)
    {
        var groups = await db.Classes.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.CourseId, c.TeacherId, c.IsArchived })
            .ToListAsync(ct);
        var courses = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, StringComparer.Ordinal, ct);
        var teachers = await db.Teachers.AsNoTracking().Select(t => new { t.Id, t.FullName })
            .ToDictionaryAsync(t => t.Id, t => t.FullName, StringComparer.Ordinal, ct);
        return groups
            .GroupBy(g => g.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToDictionary(
                g => g.Id,
                g => new RatingGroupInfo(
                    g.Id, g.Name,
                    string.IsNullOrEmpty(g.CourseId) ? "" : courses.GetValueOrDefault(g.CourseId, ""),
                    string.IsNullOrEmpty(g.TeacherId) ? "" : teachers.GetValueOrDefault(g.TeacherId, ""),
                    g.IsArchived),
                StringComparer.Ordinal);
    }

    // =====================================================================================
    //  Slices
    // =====================================================================================

    /// <summary>Centre table: ranked students, optional name search (case-insensitive), paged.</summary>
    public static RatingCentreDto Centre(RatingResult r, string? q, int page, int pageSize)
    {
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var ranked = r.Centre.Where(c => c.Rank is not null && c.Score is not null)
            .Where(c => search is null || r.NameOf(c.StudentId).Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var size = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 200);
        var p = Math.Max(1, page);
        var items = ranked.Skip((p - 1) * size).Take(size).Select(c => new RatingCentreItemDto(
                c.Rank!.Value, c.StudentId, r.NameOf(c.StudentId), c.Score!.Value, c.TestAvg, c.TestCount, c.Ball,
                r.RowsByStudent[c.StudentId]
                    .OrderBy(g => g.Rank is null).ThenBy(g => GroupName(r, g.GroupId), StringComparer.OrdinalIgnoreCase)
                    .Select(g => new RatingCentreGroupDto(g.GroupId, GroupName(r, g.GroupId), g.Rank, g.Score))
                    .ToList(),
                c.HomeworkAvg))
            .ToList();
        return new RatingCentreDto(RatingPeriodDto.Of(r.Period), r.TestWeight, ranked.Count, p, size, items, r.HomeworkWeight);
    }

    /// <summary>
    /// One group's table. Students with data in the period come first (ranked, then unranked); current
    /// members (<paramref name="members"/>: id → name) without any data follow as unranked rows, so the
    /// teacher sees who has no test and no ball yet.
    /// </summary>
    public static RatingGroupDto GroupTable(
        RatingResult r, RatingGroupInfo group, IReadOnlyDictionary<string, string> members)
    {
        var rows = r.RowsByGroup[group.Id].ToList();
        var items = rows.Select(g => new RatingGroupItemDto(
                g.Rank, g.StudentId, r.NameOf(g.StudentId), g.Score, g.TestAvg, g.TestCount, g.Ball, g.BallNorm, g.HomeworkAvg))
            .ToList();
        var have = rows.Select(g => g.StudentId).ToHashSet(StringComparer.Ordinal);
        items.AddRange(members
            .Where(m => !have.Contains(m.Key))
            .OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase)
            .Select(m => new RatingGroupItemDto(null, m.Key, m.Value, null, null, 0, 0, null)));
        return new RatingGroupDto(
            RatingPeriodDto.Of(r.Period), r.TestWeight,
            new RatingGroupHeaderDto(group.Id, group.Name, group.CourseName, group.TeacherName),
            r.RankedInGroup.GetValueOrDefault(group.Id), items, r.HomeworkWeight);
    }

    /// <summary>
    /// The student block: centre place (+ previous period's rank), per-group places, components, 12-month
    /// history, tests of the period and attendance. Null when the student does not exist.
    /// The student's CURRENT memberships (active/trial, non-archived groups) are listed even without data in the period
    /// (rank «—»), so a parent always sees the group.
    /// </summary>
    public static async Task<StudentRatingBlockDto?> StudentBlockAsync(
        IAppDbContext db, RatingLoader load, string studentId, RatingPeriod period, DateOnly today,
        CancellationToken ct = default)
    {
        var exists = await db.Students.AsNoTracking().AnyAsync(s => s.Id == studentId, ct);
        if (!exists) return null;

        var r = await load(period);
        var prevPeriod = RatingPeriods.Previous(period, today);
        var prev = prevPeriod is null ? null : await load(prevPeriod);

        // ---- centre ----
        var me = r.CentreByStudent.GetValueOrDefault(studentId);
        var centre = new RatingCentrePlaceDto(
            me?.Rank, r.CentreTotal, me?.Score, prev?.CentreByStudent.GetValueOrDefault(studentId)?.Rank);

        // ---- groups: rows with data + live memberships without data ----
        var liveGroupIds = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == studentId && sg.IsActive && sg.Status != "frozen")
            .Select(sg => sg.GroupId).Distinct().ToListAsync(ct);
        var myRows = r.RowsByStudent[studentId].ToDictionary(g => g.GroupId, StringComparer.Ordinal);
        var groupIds = myRows.Keys
            .Concat(liveGroupIds.Where(id => r.GroupInfo.TryGetValue(id, out var gi) && !gi.IsArchived))
            .Distinct(StringComparer.Ordinal);
        var groups = groupIds.Select(gid =>
            {
                var info = r.GroupInfo.GetValueOrDefault(gid);
                var row = myRows.GetValueOrDefault(gid);
                return new RatingBlockGroupDto(
                    gid, info?.Name ?? "", info?.CourseName ?? "", row?.Rank, r.RankedInGroup.GetValueOrDefault(gid),
                    row?.Score, row?.TestAvg, row?.TestCount ?? 0, row?.Ball ?? 0, row?.BallNorm, row?.HomeworkAvg);
            })
            .OrderBy(g => g.Rank is null)
            .ThenBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- history (12 months, each month cached on its own) ----
        var history = new List<RatingHistoryPointDto>();
        foreach (var month in RatingPeriods.HistoryMonths(period))
        {
            var mp = RatingPeriods.Resolve(RatingPeriods.Month, month, today).Period;
            if (mp is null) continue;
            var mr = mp.Key == period.Key && period.Kind == RatingPeriods.Month ? r : await load(mp);
            var c = mr.CentreByStudent.GetValueOrDefault(studentId);
            history.Add(new RatingHistoryPointDto(month, c?.Rank, mr.CentreTotal, c?.Score, c?.TestAvg));
        }

        return new StudentRatingBlockDto
        {
            StudentId = studentId,
            Period = RatingPeriodDto.Of(period),
            Centre = centre,
            Groups = groups,
            Components = new RatingComponentsDto(
                me?.TestAvg, me?.TestCount ?? 0, me?.Ball ?? 0, r.TestWeight, me?.HomeworkAvg, r.HomeworkWeight),
            History = history,
            Tests = await TestsAsync(db, studentId, period, r, ct),
            Attendance = await AttendanceAsync(db, studentId, period),
        };
    }

    /// <summary>The student's tests in the period (newest first) with the place inside each test
    /// (competition ranking by score among everyone scored — <c>TestResultService</c>'s rule).</summary>
    private static async Task<List<RatingTestRowDto>> TestsAsync(
        IAppDbContext db, string studentId, RatingPeriod p, RatingResult r, CancellationToken ct)
    {
        var mine = await (from s in db.TestScores.AsNoTracking()
                          join t in db.TestResults.AsNoTracking() on s.TestResultId equals t.Id
                          where s.StudentId == studentId
                                && t.Date.CompareTo(p.From) >= 0 && t.Date.CompareTo(p.To) <= 0
                          select new { t.Id, t.Name, t.Kind, t.Date, t.CreatedAt, t.GroupId, t.MaxScore, s.Score })
            .ToListAsync(ct);
        if (mine.Count == 0) return new List<RatingTestRowDto>();

        var ids = mine.Select(x => x.Id).Distinct().ToList();
        var all = (await db.TestScores.AsNoTracking()
                .Where(s => ids.Contains(s.TestResultId))
                .Select(s => new { s.TestResultId, s.Score })
                .ToListAsync(ct))
            .ToLookup(s => s.TestResultId, s => s.Score, StringComparer.Ordinal);

        return mine
            .OrderByDescending(x => x.Date, StringComparer.Ordinal)
            .ThenByDescending(x => x.CreatedAt, StringComparer.Ordinal)
            .Select(x =>
            {
                var scores = all[x.Id].ToList();
                var pct = RatingFormula.Percent(x.Score, x.MaxScore);
                return new RatingTestRowDto(
                    x.Id, x.Name, x.Kind, x.Date, GroupName(r, x.GroupId), x.Score, x.MaxScore,
                    pct is null ? null : RatingFormula.Round1(pct.Value),
                    scores.Count(s => s > x.Score) + 1, scores.Count);
            })
            .ToList();
    }

    /// <summary>
    /// Attendance of the period — the student app's own numbers (<see cref="StudentJournalBuilder.PeriodAsync"/>:
    /// one student's lessons only, never the whole journal). U3a moves that builder to the single attendance
    /// definition (<c>AttendanceRules</c>); this block follows automatically.
    /// </summary>
    private static async Task<RatingAttendanceDto> AttendanceAsync(IAppDbContext db, string studentId, RatingPeriod p)
    {
        var journal = await StudentJournalBuilder.PeriodAsync(db, studentId, p.From, p.To, null);
        var held = journal?.Summary.Held ?? 0;
        var attended = journal?.Summary.Attended ?? 0;
        return new RatingAttendanceDto(
            held, attended,
            held > 0 ? (int)Math.Round(attended * 100.0 / held, MidpointRounding.AwayFromZero) : null);
    }

    /// <summary>
    /// Cabinet top lists: each of the student's block groups → top 10 + the student's own row when outside;
    /// the centre → the same. Only name, rank and score of other students.
    /// </summary>
    public static StudentRatingSummaryDto Summary(StudentRatingBlockDto block, RatingResult r, int top = 10)
    {
        var id = block.StudentId;

        List<RatingTopRowDto> TopOf<T>(IEnumerable<T> ranked, Func<T, string> studentOf, Func<T, int> rankOf,
            Func<T, double> scoreOf)
        {
            var list = ranked.ToList();
            var rows = list.Take(top).Select(x => new RatingTopRowDto(
                rankOf(x), r.NameOf(studentOf(x)), scoreOf(x), studentOf(x) == id)).ToList();
            if (rows.All(x => !x.IsMe))
            {
                var mine = list.FirstOrDefault(x => studentOf(x) == id);
                if (mine is not null)
                    rows.Add(new RatingTopRowDto(rankOf(mine), r.NameOf(id), scoreOf(mine), true));
            }
            return rows;
        }

        var groupTop = block.Groups
            .Select(g => new RatingGroupTopDto(
                g.GroupId, g.GroupName,
                TopOf(r.RowsByGroup[g.GroupId].Where(x => x.Rank is not null && x.Score is not null),
                    x => x.StudentId, x => x.Rank!.Value, x => x.Score!.Value)))
            .Where(g => g.Rows.Count > 0)
            .ToList();
        var centreTop = TopOf(r.Centre.Where(c => c.Rank is not null && c.Score is not null),
            c => c.StudentId, c => c.Rank!.Value, c => c.Score!.Value);

        return new StudentRatingSummaryDto(block) { GroupTop = groupTop, CentreTop = centreTop };
    }

    private static string GroupName(RatingResult r, string groupId) =>
        r.GroupInfo.GetValueOrDefault(groupId)?.Name ?? "";
}
