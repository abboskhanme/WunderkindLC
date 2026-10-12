using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services.Rating;

// JSON contracts of GET api/admin/rating/top-bottom (docs/SPEC-crm-upgrade.md P4.5). camelCase.

public sealed record TopBottomRowDto(string StudentId, string FullName, double TestAvg, int Rank);

/// <summary>Centre scope: one pseudo-group <c>GroupId = ""</c>, «Markaz».</summary>
public sealed record TopBottomGroupDto(
    string GroupId, string GroupName, int Scored, List<TopBottomRowDto> Top, List<TopBottomRowDto> Bottom);

public sealed record TopBottomDto(string Month, string MonthLabel, string Scope, int Count, List<TopBottomGroupDto> Groups);

/// <summary>
/// TOP/BOTTOM of a month — the preview (report tab «TOP/BOTTOM») and the monthly sender
/// (<see cref="MonthlyRatingNotifier"/>) build the lists through THIS method, so what the admin sees is what goes out.
/// T comes from the cached month rating (<see cref="RatingEngine"/>); N and the scope from <c>CenterMeta</c>
/// (<c>RatingTopBottomCount</c>/<c>RatingTopBottomScope</c>).
/// </summary>
public static class TopBottomService
{
    /// <summary>The rated month by default: the previous calendar month of <paramref name="today"/>.</summary>
    public static string PreviousMonth(DateOnly today) =>
        new DateOnly(today.Year, today.Month, 1).AddMonths(-1).ToString("yyyy-MM");

    /// <summary>Configured N and scope (defaults 5 / group when the settings row is missing).</summary>
    public static async Task<(int Count, string Scope)> SettingsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var meta = await db.CenterMeta.AsNoTracking()
            .Select(m => new { m.RatingTopBottomCount, m.RatingTopBottomScope })
            .FirstOrDefaultAsync(ct);
        return (TopBottomRules.NormalizeCount(meta?.RatingTopBottomCount), TopBottomRules.NormalizeScope(meta?.RatingTopBottomScope));
    }

    /// <summary>
    /// Lists of <paramref name="month"/> (<c>yyyy-MM</c>; empty = previous month). <paramref name="scope"/> empty = the
    /// configured scope. Error «Davr noto'g'ri» for a malformed or future month.
    /// </summary>
    public static async Task<(TopBottomDto? Dto, IReadOnlyList<TopBottomList> Lists, string? Error)> BuildAsync(
        IAppDbContext db, RatingLoader load, string? month, string? scope, DateOnly today, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(month) ? PreviousMonth(today) : month.Trim();
        var (period, error) = RatingPeriods.Resolve(RatingPeriods.Month, key, today);
        if (period is null) return (null, [], error);

        var (count, configuredScope) = await SettingsAsync(db, ct);
        var s = string.IsNullOrWhiteSpace(scope) ? configuredScope : TopBottomRules.NormalizeScope(scope);
        var rating = await load(period);
        var lists = TopBottomRules.Pick(TopBottomRules.InputsFrom(rating), count, s);

        static List<TopBottomRowDto> Rows(IEnumerable<TopBottomEntry> e) =>
            e.Select(x => new TopBottomRowDto(x.StudentId, x.FullName, x.TestAvg, x.Rank)).ToList();
        var dto = new TopBottomDto(
            period.Key, period.Label, s, count,
            lists.Select(l => new TopBottomGroupDto(l.GroupId, l.GroupName, l.Scored, Rows(l.Top), Rows(l.Bottom))).ToList());
        return (dto, lists, null);
    }
}
