using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// HOME WIDGETS — per-user layout of the admin home page (CRM Phase 5, <c>.claude/rules/home-widgets.md</c>):
/// which of the 12 cards and which blocks are shown, and in which order. Stored server-side in
/// <c>UserSettings.HomeLayout</c> (follows the user to another browser). Pure rules, tested
/// (<c>HomeLayoutRulesTests</c>); the client keeps the same catalog in <c>lib/homeWidgets.ts</c>.
///
/// <para>⚠️ The block catalog lists the blocks later phases add too (<c>absentNotices</c>, <c>redList</c>):
/// a layout saved today keeps their place, and the client simply skips a block it does not render yet.
/// Adding a block = one entry here + one in <c>homeWidgets.ts</c> + its component.</para>
/// </summary>
public static class HomeLayoutRules
{
    public const int Version = 1;
    public const int MaxJsonLength = 4000;

    public const string BadVersionMessage = "Bosh sahifa sozlamasi versiyasi noto'g'ri";
    public const string TooLongMessage = "Bosh sahifa sozlamasi juda katta";

    /// <summary>The 12 home cards, in the default (edutizim) order — <c>HOME_CARDS</c> on the client.</summary>
    public static readonly IReadOnlyList<string> CardKeys =
    [
        "orders", "firstLesson", "newStudents", "activeStudents", "ordersLeft", "newLeft",
        "activeLeft", "debtors", "groups", "firstPayments", "frozen", "archived",
    ];

    /// <summary>Blocks in default order with their default visibility (today's page = cashboxes + schedule).</summary>
    public static readonly IReadOnlyList<(string Key, bool DefaultVisible)> BlockKeys =
    [
        ("cashboxes", true),
        ("schedule", true),
        ("lessonsMonitor", false),
        ("myTasks", false),
        ("absentNotices", false),
        ("redList", false),
        ("contactsToday", false),
        ("aiAnalysis", false),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The default layout (identical to the page before widgets existed).</summary>
    public static HomeLayoutDto Default() => new(
        Version, true,
        CardKeys.Select(k => new HomeLayoutItemDto(k, true)).ToList(),
        BlockKeys.Select(b => new HomeLayoutItemDto(b.Key, b.DefaultVisible)).ToList());

    /// <summary>
    /// Cleans a layout sent by the client: version must be 1, unknown and duplicate keys are dropped, missing
    /// known keys are APPENDED with their default visibility, the JSON must fit in 4 000 chars.
    /// </summary>
    public static (HomeLayoutDto? Layout, string? Error) Normalize(HomeLayoutDto? input)
    {
        if (input is null || input.Version != Version) return (null, BadVersionMessage);
        // The size limit applies to what was SENT (a 10 000-key payload is refused, not silently trimmed).
        if (Serialize(input).Length > MaxJsonLength) return (null, TooLongMessage);

        var cards = Merge(input.Cards, CardKeys.Select(k => (k, true)).ToList());
        var blocks = Merge(input.Blocks, BlockKeys);
        return (new HomeLayoutDto(Version, input.CardsVisible, cards, blocks), null);
    }

    private static List<HomeLayoutItemDto> Merge(
        IEnumerable<HomeLayoutItemDto>? items, IReadOnlyList<(string Key, bool DefaultVisible)> catalog)
    {
        var known = catalog.ToDictionary(c => c.Key, c => c.DefaultVisible, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<HomeLayoutItemDto>();
        foreach (var it in items ?? [])
        {
            if (it?.Key is not { } key || !known.ContainsKey(key) || !seen.Add(key)) continue;
            result.Add(new HomeLayoutItemDto(key, it.Visible));
        }
        foreach (var (key, visible) in catalog)
            if (seen.Add(key)) result.Add(new HomeLayoutItemDto(key, visible));
        return result;
    }

    public static string Serialize(HomeLayoutDto layout) => JsonSerializer.Serialize(layout, Json);

    /// <summary>Stored JSON → layout (normalised). Empty or broken JSON → <c>null</c> ("never customised").</summary>
    public static HomeLayoutDto? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return Normalize(JsonSerializer.Deserialize<HomeLayoutDto>(json, Json)).Layout;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------- storage (caller's own row only) -------------------------------

    public static async Task<HomeLayoutDto?> GetAsync(IAppDbContext db, string userId, CancellationToken ct = default) =>
        Parse(await db.UserSettings.Where(s => s.UserId == userId)
            .Select(s => s.HomeLayout).FirstOrDefaultAsync(ct));

    /// <summary>Normalises and saves; returns the stored layout or the refusal text.</summary>
    public static async Task<(HomeLayoutDto? Layout, string? Error)> SaveAsync(
        IAppDbContext db, string userId, HomeLayoutDto? input, CancellationToken ct = default)
    {
        var (layout, error) = Normalize(input);
        if (layout is null) return (null, error);
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null)
        {
            row = new UserSettings { UserId = userId };
            db.UserSettings.Add(row);
        }
        row.HomeLayout = Serialize(layout);
        row.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        return (layout, null);
    }

    /// <summary>«Standartga qaytarish»: forgets the stored layout (the row itself stays — other settings).</summary>
    public static async Task ResetAsync(IAppDbContext db, string userId, CancellationToken ct = default)
    {
        var row = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null || row.HomeLayout.Length == 0) return;
        row.HomeLayout = "";
        row.UpdatedAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
    }
}
