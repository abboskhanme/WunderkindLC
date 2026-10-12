using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>One message to put into the outbox (<see cref="TelegramOutbox"/>).</summary>
/// <param name="Source"><c>campaign</c> · <c>auto</c>.</param>
/// <param name="Trigger">Campaign kind or auto trigger key.</param>
/// <param name="DedupeKey">Natural event key — the same key is never queued twice. <c>null</c> = the row id.</param>
public sealed record OutboxItem(
    string Source, string Trigger, long ChatId, string Text,
    string? StudentId = null, string? TeacherId = null, string? RegistrationId = null,
    string Relation = "", string RecipientName = "",
    string? BroadcastId = null, string? SourceId = null, string? AttachmentId = null,
    bool AckRequired = false, string? DedupeKey = null, string? CreatedById = null);

/// <summary>Delivery counters of the rows of one source record (U3b: absent-notice item, test, achievement…).</summary>
public sealed record DeliverySummary(int Total, int Sent, int Failed, int Blocked, int Pending, int Acked);

/// <summary>
/// THE TELEGRAM OUTBOX (SPEC P2.4) — the ONLY way a parent/student-facing Telegram message is sent. Callers add rows
/// inside their own <c>SaveChanges</c> (this class never saves) and then call <see cref="IOutboxSignal.Wake"/>;
/// <see cref="MessageDeliveryWorker"/> sends them (throttled, retried, logged per recipient).
/// </summary>
public static class TelegramOutbox
{
    public const string SourceCampaign = "campaign";
    public const string SourceAuto = "auto";

    /// <summary>Campaign rows expire after 12 h, auto messages after 6 h (a stale «darsga kelmadi» is worse than none).</summary>
    public static readonly TimeSpan CampaignTtl = TimeSpan.FromHours(12);
    public static readonly TimeSpan AutoTtl = TimeSpan.FromHours(6);

    public const int MaxKeyLength = 200;

    /// <summary>Unique-index-safe key: keys longer than 200 chars become <c>"h:" + sha256hex(key)</c>.</summary>
    public static string NormalizeKey(string key)
    {
        if (key.Length <= MaxKeyLength) return key;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "h:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Builds the row (pure — campaigns add thousands of rows whose keys are unique by construction).</summary>
    public static MessageDelivery Build(OutboxItem item, DateTime now)
    {
        var row = new MessageDelivery
        {
            BroadcastId = item.BroadcastId,
            Source = item.Source,
            Trigger = item.Trigger,
            SourceId = item.SourceId,
            StudentId = item.StudentId,
            TeacherId = item.TeacherId,
            RegistrationId = item.RegistrationId,
            ChatId = item.ChatId,
            Relation = item.Relation ?? "",
            RecipientName = Cut(item.RecipientName ?? "", 200),
            Text = item.Text ?? "",
            AttachmentId = item.AttachmentId,
            AckRequired = item.AckRequired,
            Status = "pending",
            CreatedAt = now,
            NextAttemptAt = now,
            ExpiresAt = now + (item.Source == SourceCampaign ? CampaignTtl : AutoTtl),
            CreatedById = item.CreatedById,
        };
        row.DedupeKey = NormalizeKey(string.IsNullOrEmpty(item.DedupeKey) ? row.Id : item.DedupeKey);
        return row;
    }

    /// <summary>
    /// Adds a row unless one with the same dedupe key is already tracked or stored (the unique index is the last
    /// line of defence — <see cref="SaveAsync"/>). Returns the new row, or <c>null</c> when it was a duplicate.
    /// Does NOT save.
    /// </summary>
    public static async Task<MessageDelivery?> EnqueueAsync(IAppDbContext db, OutboxItem item, CancellationToken ct = default)
    {
        var row = Build(item, AppClock.Now);
        if (db.MessageDeliveries.Local.Any(d => d.DedupeKey == row.DedupeKey)) return null;
        if (await db.MessageDeliveries.AnyAsync(d => d.DedupeKey == row.DedupeKey, ct)) return null;
        db.MessageDeliveries.Add(row);
        return row;
    }

    /// <summary>
    /// <c>SaveChanges</c> that survives a dedupe race: on a unique-key violation the duplicate outbox rows are
    /// DETACHED (never a global <c>ChangeTracker.Clear()</c> — the caller's other changes stay) and the save is
    /// retried once (<c>LeadNotifier</c> pattern).
    /// </summary>
    public static async Task SaveAsync(IAppDbContext db, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (db is DbContext ctx)
        {
            var added = ctx.ChangeTracker.Entries<MessageDelivery>()
                .Where(e => e.State == EntityState.Added).ToList();
            if (added.Count == 0) throw;
            var keys = added.Select(e => e.Entity.DedupeKey).ToList();
            var taken = (await db.MessageDeliveries.AsNoTracking()
                    .Where(d => keys.Contains(d.DedupeKey)).Select(d => d.DedupeKey).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var dupes = added.Where(e => taken.Contains(e.Entity.DedupeKey)).ToList();
            if (dupes.Count == 0) throw;
            foreach (var e in dupes) e.State = EntityState.Detached;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Delivery counters per source record (one grouped query) — for status columns such as the absent-notice
    /// control page (Phase 3 U3b) and the test «Yuborilgan» line (Phase 4).</summary>
    public static async Task<Dictionary<string, DeliverySummary>> SummaryBySourceAsync(
        IAppDbContext db, IReadOnlyCollection<string> sourceIds, CancellationToken ct = default)
    {
        if (sourceIds.Count == 0) return new Dictionary<string, DeliverySummary>();
        var ids = sourceIds.Distinct().ToList();
        var rows = await db.MessageDeliveries.AsNoTracking()
            .Where(d => d.SourceId != null && ids.Contains(d.SourceId))
            .Select(d => new { d.SourceId, d.Status, Acked = d.AckAt != null })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SourceId!)
            .ToDictionary(g => g.Key, g => new DeliverySummary(
                g.Count(),
                g.Count(r => r.Status == "sent"),
                g.Count(r => DeliveryStatus.IsFailure(r.Status)),
                g.Count(r => r.Status == "blocked"),
                g.Count(r => r.Status is "pending" or "sending"),
                g.Count(r => r.Acked)));
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Delivery status vocabulary (one place — UI chips, reports and summaries agree).</summary>
public static class DeliveryStatus
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string Unknown = "unknown";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    /// <summary>Not delivered for a reason that is not "blocked": failed, unknown (interrupted), expired.</summary>
    public static bool IsFailure(string status) => status is Failed or Unknown or Expired;

    /// <summary>Waiting in the queue.</summary>
    public static bool IsOpen(string status) => status is Pending or Sending;

    /// <summary>Uzbek label (CRM report chips).</summary>
    public static string Label(string status) => status switch
    {
        Pending or Sending => "Navbatda",
        Sent => "Yuborildi",
        Blocked => "Bloklagan",
        Cancelled => "Bekor qilindi",
        Expired => "Muddati o'tdi",
        Unknown => "Noma'lum",
        _ => "Xato",
    };
}
