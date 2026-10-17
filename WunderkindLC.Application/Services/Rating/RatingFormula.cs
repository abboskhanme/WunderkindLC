namespace WunderkindLC.Application.Services.Rating;

/// <summary>One (student, group) pair of a period: Σ of the student's test percents in that group (each test
/// already clamped to 0..100), how many tests, and the journal BALL in the group over the period.
/// <paramref name="HwSum"/>/<paramref name="HwCount"/> — homework (journal «Uyga vazifa»): Σ of per-lesson points
/// (done 100, partly 50, not done 0) and the number of MARKED lessons (unmarked are skipped) — SPEC-backlog A3.</summary>
public sealed record RatingInput(
    string StudentId, string GroupId, double PercentSum, int TestCount, int Ball, double HwSum = 0, int HwCount = 0);

/// <summary>Group-level result. <paramref name="Score"/>/<paramref name="Rank"/> are null when the student is
/// unranked in this group (neither a test average nor a normalised ball).</summary>
public sealed record GroupScoreRow(
    string StudentId, string GroupId, double? TestAvg, int TestCount, int Ball, double? BallNorm,
    double? Score, int? Rank, double? HomeworkAvg = null);

/// <summary>Centre-level result: mean of the student's group scores; T and ball are the totals over all groups
/// (T = mean over ALL the student's tests, each test equal weight).</summary>
public sealed record CentreScoreRow(
    string StudentId, double? Score, int? Rank, double? TestAvg, int TestCount, int Ball, double? HomeworkAvg = null);

public sealed record RatingTable(IReadOnlyList<GroupScoreRow> Groups, IReadOnlyList<CentreScoreRow> Centre);

/// <summary>
/// The rating formula (docs/SPEC-crm-upgrade.md P4.3; owner decision 2026-10-11). Pure — no database.
/// <list type="bullet">
///   <item><b>T(s,g)</b> = mean test percent of s in g (each test equal weight; missing score = not counted;
///   percent clamped 0..100; tests with max ≤ 0 ignored — <see cref="Percent"/>). Test kind does not matter.</item>
///   <item><b>B(s,g)</b> = Ball ÷ (best Ball in g) × 100; ABSENT when the group's best is 0.</item>
///   <item><b>GroupScore</b> = w·T + (1−w)·B (w = test weight ÷ 100); only T → T; only B → B; none → unranked.
///   One decimal.</item>
///   <item><b>Homework</b> (owner decision 2026-10-17, SPEC-backlog A3): H = mean homework points (done 100, partly 50,
///   not done 0; unmarked skipped), weight wh. With wh &gt; 0 the ball weight is 100 − wt − wh and
///   score = (wh·H + (100 − wh)·base) ÷ 100, base = T and B blended by wt : wb (only one of them → that one). A missing
///   component's weight goes to the others. <b>wh = 0 → exactly the old formula</b> (H never takes part).</item>
///   <item><b>Group rank</b> — competition ranking ("1, 2, 2, 4") by GroupScore among the group's ranked students.</item>
///   <item><b>CentreScore</b> = mean of the student's (rounded) group scores; centre rank — competition ranking.</item>
///   <item>Display order on ties: higher T, then name.</item>
///   <item>Only students present in the name dictionary take part — the engine passes NON-ARCHIVED students only,
///   so an archived student never appears anywhere (and never takes a rank).</item>
/// </list>
/// Normalising the ball to the group's best removes the grading-volume difference between groups (a teacher
/// who grades every lesson vs one who grades weekly).
/// </summary>
public static class RatingFormula
{
    public const int DefaultTestWeight = 60;

    /// <summary>score ÷ max × 100, clamped to 0..100; null when max ≤ 0 (such tests are ignored).</summary>
    public static double? Percent(decimal score, decimal max)
    {
        if (max <= 0) return null;
        var p = (double)(score / max * 100m);
        return Math.Clamp(p, 0, 100);
    }

    public static double Round1(double x) => Math.Round(x, 1, MidpointRounding.AwayFromZero);

    /// <summary>Weight clamped to 0..100.</summary>
    public static int ClampWeight(int w) => Math.Clamp(w, 0, 100);

    /// <summary>Effective (test, homework, ball) weights: each clamped to 0..100, homework cut so the sum stays ≤ 100.</summary>
    public static (int Test, int Homework, int Ball) Weights(int testWeightPercent, int homeworkWeightPercent)
    {
        var wt = ClampWeight(testWeightPercent);
        var wh = Math.Min(ClampWeight(homeworkWeightPercent), 100 - wt);
        return (wt, wh, 100 - wt - wh);
    }

    public static RatingTable Compute(
        IEnumerable<RatingInput> inputs, int testWeightPercent, IReadOnlyDictionary<string, string> names,
        int homeworkWeightPercent = 0)
    {
        var (wt, wh, wb) = Weights(testWeightPercent, homeworkWeightPercent);
        var w = wt / 100.0;
        var rows = inputs.Where(i => names.ContainsKey(i.StudentId)).ToList();
        string NameOf(string id) => names.GetValueOrDefault(id, "");

        var groupRows = new List<GroupScoreRow>(rows.Count);
        foreach (var g in rows.GroupBy(r => r.GroupId, StringComparer.Ordinal))
        {
            // The same (student, group) may arrive twice from a careless caller — merge it.
            var merged = g.GroupBy(r => r.StudentId, StringComparer.Ordinal)
                .Select(x => new RatingInput(x.Key, g.Key, x.Sum(r => r.PercentSum), x.Sum(r => r.TestCount), x.Sum(r => r.Ball),
                    x.Sum(r => r.HwSum), x.Sum(r => r.HwCount)))
                .ToList();
            var best = merged.Count == 0 ? 0 : merged.Max(r => r.Ball);

            var scored = merged.Select(r =>
            {
                double? t = r.TestCount > 0 ? r.PercentSum / r.TestCount : null;
                double? b = best > 0 ? Math.Max(0, r.Ball) * 100.0 / best : null;
                double? hw = r.HwCount > 0 ? r.HwSum / r.HwCount : null;
                double? baseScore = (t, b) switch
                {
                    // wh = 0 — the OLD expression verbatim (no rounding drift in existing ranks).
                    ({ } tv, { } bv) when wh == 0 => w * tv + (1 - w) * bv,
                    ({ } tv, { } bv) => wt + wb == 0 ? (tv + bv) / 2 : (wt * tv + wb * bv) / (wt + wb),
                    ({ } tv, null) => tv,
                    (null, { } bv) => bv,
                    _ => null,
                };
                double? h = wh > 0 ? hw : null;   // weight 0 — homework never takes part
                double? score = h is null ? baseScore
                    : baseScore is null ? h
                    : (wh * h.Value + (100 - wh) * baseScore.Value) / 100.0;
                return new GroupScoreRow(
                    r.StudentId, g.Key,
                    t is null ? null : Round1(t.Value), r.TestCount, r.Ball,
                    b is null ? null : Round1(b.Value),
                    score is null ? null : Round1(score.Value), null,
                    hw is null ? null : Round1(hw.Value));
            }).ToList();

            groupRows.AddRange(RankRows(scored, s => s.Score, s => s.TestAvg, s => NameOf(s.StudentId),
                (row, rank) => row with { Rank = rank }));
        }

        var inputsByStudent = rows.ToLookup(r => r.StudentId, StringComparer.Ordinal);
        var centre = groupRows
            .GroupBy(r => r.StudentId, StringComparer.Ordinal)
            .Select(s =>
            {
                var scores = s.Where(r => r.Score is not null).Select(r => r.Score!.Value).ToList();
                var inputsOf = inputsByStudent[s.Key].ToList();
                var tests = inputsOf.Sum(r => r.TestCount);
                double? t = tests > 0 ? Round1(inputsOf.Sum(r => r.PercentSum) / tests) : null;
                var hwCount = inputsOf.Sum(r => r.HwCount);
                return new CentreScoreRow(
                    s.Key, scores.Count > 0 ? Round1(scores.Average()) : null, null, t, tests,
                    s.Sum(r => Math.Max(0, r.Ball)),
                    hwCount > 0 ? Round1(inputsOf.Sum(r => r.HwSum) / hwCount) : null);
            })
            .ToList();
        var rankedCentre = RankRows(centre, c => c.Score, c => c.TestAvg, c => NameOf(c.StudentId),
            (row, rank) => row with { Rank = rank });

        return new RatingTable(groupRows, rankedCentre);
    }

    /// <summary>
    /// Competition ranking ("1, 2, 2, 4") by score desc; rows without a score stay unranked (rank null) and go
    /// last. Display order on ties: higher T, then name (ordinal, case-insensitive).
    /// </summary>
    public static List<T> RankRows<T>(
        IEnumerable<T> rows, Func<T, double?> score, Func<T, double?> testAvg, Func<T, string> name,
        Func<T, int?, T> withRank)
    {
        var ordered = rows
            .OrderByDescending(r => score(r).HasValue)
            .ThenByDescending(r => score(r) ?? double.MinValue)
            .ThenByDescending(r => testAvg(r) ?? double.MinValue)
            .ThenBy(r => name(r), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<T>(ordered.Count);
        double? prev = null;
        var rank = 0;
        var position = 0;
        foreach (var r in ordered)
        {
            var s = score(r);
            if (s is null) { result.Add(withRank(r, null)); continue; }
            position++;
            if (prev is null || s.Value != prev.Value) rank = position;
            prev = s.Value;
            result.Add(withRank(r, rank));
        }
        return result;
    }
}
