namespace WunderkindLC.Application.Services.Rating;

/// <summary>One student's TEST average of the rated month in one group (T, 0..100).</summary>
public sealed record TopBottomInput(string StudentId, string FullName, string GroupId, string GroupName, double TestAvg);

/// <summary>A listed student: T and the competition rank by T inside the scope.</summary>
public sealed record TopBottomEntry(string StudentId, string FullName, double TestAvg, int Rank);

/// <summary>One scope's lists. <paramref name="Scored"/> — students with a test in the month (the "/ n" of {orin}).
/// Centre scope: one pseudo-group <c>GroupId = ""</c>, «Markaz».</summary>
public sealed record TopBottomList(
    string GroupId, string GroupName, int Scored, IReadOnlyList<TopBottomEntry> Top, IReadOnlyList<TopBottomEntry> Bottom);

/// <summary>
/// MONTHLY TOP / BOTTOM (CRM upgrade U4d, docs/SPEC-crm-upgrade.md P4.5; owner decision 2026-10-11: by the previous
/// month's TEST average, per group by default, k = min(N, ⌊n/2⌋)). Pure — <c>TopBottomRulesTests</c>.
/// <list type="bullet">
///   <item><b>group</b> scope: every group with ≥ 2 scored students; TOP = the first k by T desc (ties by name),
///   BOTTOM = the last k (lowest T, listed lowest first).</item>
///   <item><b>centre</b> scope: one list of the students' centre T = mean of their group T's, the same k.</item>
///   <item>⌊n/2⌋ keeps small groups from warning almost everyone; with k ≤ n/2 nobody is in both lists.</item>
///   <item>A student TIED with someone in TOP is never put in BOTTOM (same score — one praised, one warned would be
///   unfair); so a group where everyone has the same T has no BOTTOM at all.</item>
///   <item>Students without any test in the month are not considered (they are simply not in the input).</item>
/// </list>
/// </summary>
public static class TopBottomRules
{
    public const string ScopeGroup = "group";
    public const string ScopeCentre = "centre";
    public const string CentreName = "Markaz";

    public const int DefaultCount = 5;
    public const int MinCount = 1;
    public const int MaxCount = 10;

    /// <summary><c>group</c> (default) | <c>centre</c>.</summary>
    public static string NormalizeScope(string? scope) =>
        string.Equals(scope?.Trim(), ScopeCentre, StringComparison.OrdinalIgnoreCase) ? ScopeCentre : ScopeGroup;

    /// <summary>N clamped to 1..10 (an unset/invalid value → 5).</summary>
    public static int NormalizeCount(int? count) =>
        count is >= MinCount and <= MaxCount ? count.Value : DefaultCount;

    /// <summary>k = min(N, ⌊n/2⌋).</summary>
    public static int K(int count, int scored) => Math.Max(0, Math.Min(count, scored / 2));

    /// <summary>T of every (student, group) with a test in the period — from a computed rating.</summary>
    public static IReadOnlyList<TopBottomInput> InputsFrom(RatingResult r) => r.Groups
        .Where(g => g.TestCount > 0 && g.TestAvg is not null)
        .Select(g => new TopBottomInput(
            g.StudentId, r.NameOf(g.StudentId), g.GroupId,
            r.GroupInfo.GetValueOrDefault(g.GroupId)?.Name ?? "", g.TestAvg!.Value))
        .ToList();

    public static IReadOnlyList<TopBottomList> Pick(IEnumerable<TopBottomInput> inputs, int count, string? scope)
    {
        var n = NormalizeCount(count);
        var rows = inputs.ToList();
        if (NormalizeScope(scope) == ScopeCentre)
        {
            var centre = rows
                .GroupBy(r => r.StudentId, StringComparer.Ordinal)
                .Select(g => (Id: g.Key, Name: g.First().FullName, T: RatingFormula.Round1(g.Average(x => x.TestAvg))))
                .ToList();
            var list = One("", CentreName, centre, n);
            return list is null ? [] : [list];
        }
        return rows
            .GroupBy(r => r.GroupId, StringComparer.Ordinal)
            .Select(g => One(
                g.Key, g.First().GroupName,
                g.GroupBy(x => x.StudentId, StringComparer.Ordinal)
                    .Select(s => (Id: s.Key, Name: s.First().FullName, T: s.First().TestAvg)).ToList(),
                n))
            .Where(l => l is not null)
            .Select(l => l!)
            .OrderBy(l => l.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TopBottomList? One(string groupId, string groupName, List<(string Id, string Name, double T)> students, int n)
    {
        if (students.Count < 2) return null;
        var ordered = students
            .OrderByDescending(s => s.T)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        var entries = new List<TopBottomEntry>(ordered.Count);
        double? prev = null;
        var rank = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (prev is null || ordered[i].T != prev.Value) rank = i + 1;
            prev = ordered[i].T;
            entries.Add(new TopBottomEntry(ordered[i].Id, ordered[i].Name, ordered[i].T, rank));
        }
        var k = K(n, entries.Count);
        var top = entries.Take(k).ToList();
        var lowestTop = top.Count > 0 ? top[^1].TestAvg : double.MaxValue;
        var bottom = entries.Skip(entries.Count - k)
            .Where(e => e.TestAvg < lowestTop)
            .Reverse()
            .ToList();
        return new TopBottomList(groupId, groupName, entries.Count, top, bottom);
    }
}
