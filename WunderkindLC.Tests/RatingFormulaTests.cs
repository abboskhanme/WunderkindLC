using WunderkindLC.Application.Services.Rating;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Rating formula (docs/SPEC-crm-upgrade.md P4.3): 60 % test average + 40 % ball normalised to the
/// group's best, competition ranking, centre = mean of group scores.</summary>
public class RatingFormulaTests
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["a"] = "Aliyev Ali", ["b"] = "Botirov Bobur", ["c"] = "Choriyev Chori", ["d"] = "Davronov Dilshod",
    };

    private static RatingInput In(string s, string g, double[] percents, int ball) =>
        new(s, g, percents.Sum(), percents.Length, ball);

    private static GroupScoreRow Row(RatingTable t, string s, string g) =>
        t.Groups.Single(r => r.StudentId == s && r.GroupId == g);

    [Fact]
    public void Weights_60_40_with_ball_normalised_to_the_groups_best()
    {
        var t = RatingFormula.Compute(new[]
        {
            In("a", "g", new[] { 80.0, 100.0 }, 50),   // T = 90, B = 50/50 = 100 → 0.6·90 + 0.4·100 = 94
            In("b", "g", new[] { 70.0 }, 25),          // T = 70, B = 50       → 42 + 20 = 62
        }, 60, Names);

        Assert.Equal((90.0, 100.0, 94.0, 1), (Row(t, "a", "g").TestAvg, Row(t, "a", "g").BallNorm, Row(t, "a", "g").Score, Row(t, "a", "g").Rank));
        Assert.Equal((70.0, 50.0, 62.0, 2), (Row(t, "b", "g").TestAvg, Row(t, "b", "g").BallNorm, Row(t, "b", "g").Score, Row(t, "b", "g").Rank));
    }

    [Fact]
    public void Weight_is_configurable()
    {
        var t = RatingFormula.Compute(new[] { In("a", "g", new[] { 50.0 }, 10), In("b", "g", new[] { 100.0 }, 5) }, 100, Names);
        Assert.Equal(50.0, Row(t, "a", "g").Score);   // ball weight 0 → only T
        var t0 = RatingFormula.Compute(new[] { In("a", "g", new[] { 50.0 }, 10), In("b", "g", new[] { 100.0 }, 5) }, 0, Names);
        Assert.Equal(100.0, Row(t0, "a", "g").Score); // test weight 0 → only B
    }

    [Fact]
    public void Only_tests_or_only_ball_or_nothing()
    {
        var t = RatingFormula.Compute(new[]
        {
            In("a", "g", new[] { 75.0 }, 0),   // ball 0 but the group's best is 20 → B = 0 → 0.6·75 = 45
            In("b", "g", Array.Empty<double>(), 20),   // only B = 100
        }, 60, Names);
        Assert.Equal(45.0, Row(t, "a", "g").Score);
        Assert.Equal(100.0, Row(t, "b", "g").Score);

        // Group max ball 0 → B absent: only T counts; nothing at all → unranked.
        var z = RatingFormula.Compute(new[]
        {
            In("a", "z", new[] { 75.0 }, 0),
            In("b", "z", Array.Empty<double>(), 0),
        }, 60, Names);
        Assert.Equal((75.0, (double?)null), (Row(z, "a", "z").Score, Row(z, "a", "z").BallNorm));
        Assert.Null(Row(z, "b", "z").Score);
        Assert.Null(Row(z, "b", "z").Rank);
        Assert.Null(z.Centre.Single(c => c.StudentId == "b").Rank);
    }

    [Fact]
    public void Competition_ranking_with_ties_and_tie_display_order()
    {
        var t = RatingFormula.Compute(new[]
        {
            In("a", "g", new[] { 80.0 }, 0),
            In("b", "g", new[] { 90.0 }, 0),
            In("c", "g", new[] { 80.0 }, 0),
            In("d", "g", new[] { 70.0 }, 0),
        }, 60, Names);
        var ordered = t.Groups.Where(r => r.GroupId == "g").ToList();
        Assert.Equal(new[] { "b", "a", "c", "d" }, ordered.Select(r => r.StudentId));   // tie: by name
        Assert.Equal(new int?[] { 1, 2, 2, 4 }, ordered.Select(r => r.Rank));
    }

    [Fact]
    public void Ties_are_shown_by_higher_test_average_first()
    {
        // Same score 60: "c" by tests only (T=60), "a" by ball only (T absent) → c first despite the name.
        var t = RatingFormula.Compute(new[]
        {
            In("a", "g", Array.Empty<double>(), 6),
            In("c", "g", new[] { 60.0 }, 6),
            In("b", "g", Array.Empty<double>(), 10),
        }, 0, Names);
        // weight 0 → score = B: a = 60, c = 60, b = 100.
        var ordered = t.Groups.Where(r => r.GroupId == "g").Select(r => r.StudentId).ToList();
        Assert.Equal(new[] { "b", "c", "a" }, ordered);
        Assert.Equal(2, Row(t, "a", "g").Rank);
        Assert.Equal(2, Row(t, "c", "g").Rank);
    }

    [Fact]
    public void Centre_score_is_the_mean_of_group_scores_and_ranks_over_all()
    {
        var t = RatingFormula.Compute(new[]
        {
            In("a", "g1", new[] { 100.0 }, 0),   // 100
            In("a", "g2", new[] { 60.0 }, 0),    // 60 → centre 80
            In("b", "g1", new[] { 90.0 }, 0),    // centre 90
            In("c", "g2", new[] { 80.0 }, 0),    // centre 80
        }, 60, Names);
        var a = t.Centre.Single(c => c.StudentId == "a");
        Assert.Equal((80.0, 2, 80.0, 2), (a.Score!.Value, a.Rank!.Value, a.TestAvg!.Value, a.TestCount));
        Assert.Equal(1, t.Centre.Single(c => c.StudentId == "b").Rank);
        Assert.Equal(2, t.Centre.Single(c => c.StudentId == "c").Rank);   // tie with "a"
        Assert.Equal(new[] { "b", "a", "c" }, t.Centre.Select(c => c.StudentId));
    }

    [Fact]
    public void Each_test_has_equal_weight_in_the_average()
    {
        // 3 tests: 100, 100, 40 → 80 (not a weighted mean by max score).
        var t = RatingFormula.Compute(new[] { In("a", "g", new[] { 100.0, 100.0, 40.0 }, 0) }, 60, Names);
        Assert.Equal(80.0, Row(t, "a", "g").TestAvg);
        Assert.Equal(3, Row(t, "a", "g").TestCount);
    }

    [Fact]
    public void Percent_is_clamped_and_zero_max_is_ignored()
    {
        Assert.Equal(83.3, RatingFormula.Round1(RatingFormula.Percent(25, 30)!.Value));
        Assert.Equal(100, RatingFormula.Percent(35, 30));
        Assert.Equal(0, RatingFormula.Percent(-2, 30));
        Assert.Null(RatingFormula.Percent(5, 0));
    }

    [Fact]
    public void Archived_or_unknown_students_are_excluded_everywhere()
    {
        // "x" is not in the (non-archived) name dictionary: no row, no rank, and it does not set the group's
        // best ball either.
        var t = RatingFormula.Compute(new[]
        {
            In("x", "g", new[] { 100.0 }, 100),
            In("a", "g", new[] { 50.0 }, 10),
        }, 60, Names);
        Assert.DoesNotContain(t.Groups, r => r.StudentId == "x");
        Assert.DoesNotContain(t.Centre, r => r.StudentId == "x");
        Assert.Equal((1, 100.0), (Row(t, "a", "g").Rank!.Value, Row(t, "a", "g").BallNorm!.Value));
    }
}
