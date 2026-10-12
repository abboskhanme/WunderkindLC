using WunderkindLC.Application.Services.Rating;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Rating periods (docs/SPEC-crm-upgrade.md P4.3): month, rolling 3 months, academic year.</summary>
public class RatingPeriodsTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static RatingPeriod Ok(string? kind, string? key, DateOnly? today = null)
    {
        var (p, error) = RatingPeriods.Resolve(kind, key, today ?? Today);
        Assert.Null(error);
        return p!;
    }

    [Fact]
    public void Month_default_is_current_month_clipped_to_today()
    {
        var p = Ok(null, null);
        Assert.Equal(("month", "2026-10", "Oktabr 2026", "2026-10-01", "2026-10-12"), (p.Kind, p.Key, p.Label, p.From, p.To));
    }

    [Fact]
    public void Past_month_covers_the_whole_month()
    {
        var p = Ok("month", "2026-02");
        Assert.Equal(("2026-02-01", "2026-02-28", "Fevral 2026"), (p.From, p.To, p.Label));
        Assert.Equal("2024-02-29", Ok("month", "2024-02").To);   // leap year
    }

    [Fact]
    public void Quarter_is_rolling_three_months_ending_with_the_key()
    {
        var p = Ok("quarter", "2026-10");
        Assert.Equal(("quarter", "2026-10", "2026-08-01", "2026-10-12", "Avgust – Oktabr 2026"),
            (p.Kind, p.Key, p.From, p.To, p.Label));

        var cross = Ok("quarter", "2026-01");
        Assert.Equal(("2025-11-01", "2026-01-31", "Noyabr 2025 – Yanvar 2026"), (cross.From, cross.To, cross.Label));
        Assert.Equal("2026-10", Ok("quarter", null).Key);   // default: ends with the current month
    }

    [Fact]
    public void Academic_year_runs_september_to_august_with_boundary()
    {
        var y = Ok("year", "2025");
        Assert.Equal(("2025-09-01", "2026-08-31", "2025/26 o'quv yili"), (y.From, y.To, y.Label));

        // Default year: 31 August still belongs to the previous academic year, 1 September starts a new one.
        Assert.Equal("2025", Ok("year", null, new DateOnly(2026, 8, 31)).Key);
        Assert.Equal("2026", Ok("year", null, new DateOnly(2026, 9, 1)).Key);

        var current = Ok("year", "2026");
        Assert.Equal(("2026-09-01", "2026-10-12", "2026/27 o'quv yili"), (current.From, current.To, current.Label));
    }

    [Theory]
    [InlineData("month", "2026-11")]      // future
    [InlineData("quarter", "2027-01")]    // future
    [InlineData("year", "2027")]          // starts after today
    [InlineData("month", "2026-13")]
    [InlineData("month", "26-10")]
    [InlineData("month", "1999-12")]      // before MinYear
    [InlineData("year", "20x6")]
    [InlineData("week", "2026-10")]       // unknown kind
    public void Invalid_or_future_keys_are_rejected(string kind, string key)
    {
        var (p, error) = RatingPeriods.Resolve(kind, key, Today);
        Assert.Null(p);
        Assert.Equal(RatingPeriods.InvalidMessage, error);
    }

    [Fact]
    public void Previous_period_of_the_same_kind()
    {
        Assert.Equal("2026-09", RatingPeriods.Previous(Ok("month", "2026-10"), Today)!.Key);
        Assert.Equal("2025-12", RatingPeriods.Previous(Ok("month", "2026-01"), Today)!.Key);
        // «3 oy» — the three months right BEFORE the window (non-overlapping).
        var q = RatingPeriods.Previous(Ok("quarter", "2026-10"), Today)!;
        Assert.Equal(("2026-07", "2026-05-01", "2026-07-31"), (q.Key, q.From, q.To));
        Assert.Equal("2025", RatingPeriods.Previous(Ok("year", "2026"), Today)!.Key);
    }

    [Fact]
    public void History_is_twelve_months_oldest_first_ending_with_the_period_end()
    {
        var months = RatingPeriods.HistoryMonths(Ok("month", "2026-10"));
        Assert.Equal(12, months.Count);
        Assert.Equal("2025-11", months[0]);
        Assert.Equal("2026-10", months[^1]);

        // A past academic year ends in August.
        Assert.Equal("2026-08", RatingPeriods.HistoryMonths(Ok("year", "2025"))[^1]);
    }
}
