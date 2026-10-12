using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// One-time startup data fixes of Phase 2 (SPEC P2.2 «Startup data fixes»), each guarded by a <see cref="SeedMarker"/>
/// key and idempotent (<c>FinanceTypeSeed</c> pattern).
/// </summary>
public static class MessagingDataFixes
{
    public const string RelationBackfillKey = "tg-relation-backfill-2026-10";
    public const string TelegramOnlyKey = "autorule-telegram-only-2026-10";

    /// <summary>Relation of every existing student registration (<see cref="TelegramRegistrar.BackfillRelationsAsync"/>).</summary>
    public static async Task<int?> BackfillRelationsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == RelationBackfillKey, ct)) return null;
        var n = await TelegramRegistrar.BackfillRelationsAsync(db, ct);
        db.SeedMarkers.Add(new SeedMarker { Key = RelationBackfillKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>
    /// Telegram is the only channel: a rule that delivered over NO external channel (push-only — neither Telegram
    /// nor a legacy SMS flag, which Phase 1 delivers over Telegram) is DISABLED (its names are returned for the log),
    /// then <c>SendTelegram = true</c> on every rule — from now on <c>Enabled</c> alone decides.
    /// </summary>
    public static async Task<List<string>?> TelegramOnlyRulesAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == TelegramOnlyKey, ct)) return null;
        var rules = await db.AutoMessageRules.ToListAsync(ct);
        var disabled = new List<string>();
        foreach (var r in rules)
        {
            if (r.Enabled && !AutoMessageChannels.Telegram(r))
            {
                r.Enabled = false;
                disabled.Add(string.IsNullOrWhiteSpace(r.Name) ? r.Trigger : r.Name);
            }
            r.SendTelegram = true;
        }
        db.SeedMarkers.Add(new SeedMarker { Key = TelegramOnlyKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return disabled;
    }
}
