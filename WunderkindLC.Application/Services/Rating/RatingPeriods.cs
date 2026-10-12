using System.Globalization;

namespace WunderkindLC.Application.Services.Rating;

/// <summary>A resolved rating period: <paramref name="From"/>..<paramref name="To"/> are inclusive ISO dates,
/// <paramref name="To"/> already clipped to today.</summary>
public sealed record RatingPeriod(string Kind, string Key, string Label, string From, string To);

/// <summary>
/// Rating periods (docs/SPEC-crm-upgrade.md P4.3, owner decision 2026-10-11: "juda maydalashib ketmasin"):
/// <list type="bullet">
///   <item><c>month</c> — key <c>yyyy-MM</c> (default: current month);</item>
///   <item><c>quarter</c> («3 oy») — key = the END month <c>yyyy-MM</c>; covers end−2 … end (a ROLLING three
///   calendar months — "3 months" and "quarter" were merged into this one);</item>
///   <item><c>year</c> («O'quv yili») — key = start year <c>yyyy</c>; 1 September … 31 August.</item>
/// </list>
/// Ranges are clipped to today; a key in the future is an error («Davr noto'g'ri»). Pure — <c>today</c> is a
/// parameter (tests), never <c>AppClock</c> inside.
/// </summary>
public static class RatingPeriods
{
    public const string Month = "month";
    public const string Quarter = "quarter";
    public const string Year = "year";

    public const string InvalidMessage = "Davr noto'g'ri";

    /// <summary>Uzbek (Latin) month names — the client's <c>lib/teacherPortal.ts</c> spelling.</summary>
    public static readonly string[] MonthNames =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    /// <summary>Earliest accepted year — guards against nonsense keys ("0001-01") building huge ranges.</summary>
    public const int MinYear = 2000;

    /// <summary>
    /// Resolves (<paramref name="kind"/>, <paramref name="key"/>) into a period. Empty kind = <c>month</c>,
    /// empty key = the kind's current period. Returns an error (Uzbek) for an unknown kind, a malformed key
    /// or a period that starts after today.
    /// </summary>
    public static (RatingPeriod? Period, string? Error) Resolve(string? kind, string? key, DateOnly today)
    {
        var k = string.IsNullOrWhiteSpace(kind) ? Month : kind.Trim().ToLowerInvariant();
        var raw = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        switch (k)
        {
            case Month:
            {
                var m = raw is null ? new DateOnly(today.Year, today.Month, 1) : ParseMonth(raw);
                if (m is null || m.Value > today) return (null, InvalidMessage);
                var from = m.Value;
                var to = Min(from.AddMonths(1).AddDays(-1), today);
                return (new RatingPeriod(Month, MonthKey(from), MonthLabel(from), Iso(from), Iso(to)), null);
            }
            case Quarter:
            {
                var end = raw is null ? new DateOnly(today.Year, today.Month, 1) : ParseMonth(raw);
                if (end is null || end.Value > today) return (null, InvalidMessage);
                var from = end.Value.AddMonths(-2);
                var to = Min(end.Value.AddMonths(1).AddDays(-1), today);
                return (new RatingPeriod(Quarter, MonthKey(end.Value), QuarterLabel(from, end.Value), Iso(from), Iso(to)), null);
            }
            case Year:
            {
                int start;
                if (raw is null) start = CurrentAcademicYear(today);
                else if (raw.Length != 4 || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out start)
                         || start < MinYear)
                    return (null, InvalidMessage);
                var from = new DateOnly(start, 9, 1);
                if (from > today) return (null, InvalidMessage);
                var to = Min(new DateOnly(start + 1, 8, 31), today);
                return (new RatingPeriod(Year, start.ToString(CultureInfo.InvariantCulture), YearLabel(start), Iso(from), Iso(to)), null);
            }
            default:
                return (null, InvalidMessage);
        }
    }

    /// <summary>The academic year (start year) <paramref name="today"/> belongs to: September or later → this
    /// year, otherwise the previous one.</summary>
    public static int CurrentAcademicYear(DateOnly today) => today.Month >= 9 ? today.Year : today.Year - 1;

    /// <summary>
    /// The previous period of the SAME kind (for the ▲/▼ arrow): month → previous month; «3 oy» → the three
    /// months right BEFORE this window (non-overlapping, e.g. Aug–Oct → May–Jul); year → previous academic
    /// year. Null when it would start before <see cref="MinYear"/>.
    /// </summary>
    public static RatingPeriod? Previous(RatingPeriod p, DateOnly today)
    {
        string? key = p.Kind switch
        {
            Month => ShiftMonthKey(p.Key, -1),
            Quarter => ShiftMonthKey(p.Key, -3),
            Year => int.TryParse(p.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                ? (y - 1).ToString(CultureInfo.InvariantCulture)
                : null,
            _ => null,
        };
        if (key is null) return null;
        return Resolve(p.Kind, key, today).Period;
    }

    /// <summary>The <paramref name="count"/> calendar months ending with the month of <paramref name="p"/>'s
    /// END date (oldest first) — the 12-month history chart.</summary>
    public static List<string> HistoryMonths(RatingPeriod p, int count = 12)
    {
        var end = ParseMonth(p.To[..7]) ?? throw new ArgumentException("Bad period end", nameof(p));
        var result = new List<string>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            var m = end.AddMonths(-i);
            if (m.Year >= MinYear) result.Add(MonthKey(m));
        }
        return result;
    }

    /// <summary>"2026-10" → "Oktabr 2026".</summary>
    public static string MonthLabel(DateOnly m) => $"{MonthNames[m.Month - 1]} {m.Year}";

    /// <summary>«Avgust – Oktabr 2026»; across a year boundary «Noyabr 2025 – Yanvar 2026».</summary>
    public static string QuarterLabel(DateOnly from, DateOnly end) =>
        from.Year == end.Year
            ? $"{MonthNames[from.Month - 1]} – {MonthNames[end.Month - 1]} {end.Year}"
            : $"{MonthLabel(from)} – {MonthLabel(end)}";

    /// <summary>2026 → «2026/27 o'quv yili».</summary>
    public static string YearLabel(int start) => $"{start}/{(start + 1) % 100:00} o'quv yili";

    /// <summary>Strict "yyyy-MM" parser (month 1..12, year ≥ <see cref="MinYear"/>); returns the 1st day.</summary>
    public static DateOnly? ParseMonth(string s)
    {
        if (s.Length != 7 || s[4] != '-') return null;
        if (!int.TryParse(s.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return null;
        if (!int.TryParse(s.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mo)) return null;
        if (y < MinYear || y > 9998 || mo is < 1 or > 12) return null;
        return new DateOnly(y, mo, 1);
    }

    public static string MonthKey(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string? ShiftMonthKey(string key, int months)
    {
        var m = ParseMonth(key);
        if (m is null) return null;
        var shifted = m.Value.AddMonths(months);
        return shifted.Year < MinYear ? null : MonthKey(shifted);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
}
