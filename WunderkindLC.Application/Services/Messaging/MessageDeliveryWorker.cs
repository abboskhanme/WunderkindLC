using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// OUTBOX WORKER (SPEC P2.4, <c>MessageDeliveryWorkerTests</c>) — sends <see cref="MessageDelivery"/> rows.
///
/// <list type="bullet">
///   <item><b>Restart safe:</b> at startup every <c>sending</c> row becomes <c>unknown</c> and is NOT resent (it may
///     have gone out); <c>pending</c> rows simply continue (<c>AttendanceNotifyWorker</c> pattern).</item>
///   <item><b>Claim</b> <c>pending → sending</c> through the <c>Status</c> concurrency token — a row is sent once.</item>
///   <item><b>Throttle:</b> ≤ 20 messages/second globally, ≥ 1.1 s between two messages to the same chat (a row for a
///     chat that is "too soon" is postponed, not sent).</item>
///   <item><b>Outcomes:</b> Ok → sent; Blocked → blocked + every registration of the chat marked; 429 → back to pending
///     after <c>retry_after</c> (and the worker pauses); BadMarkup → one immediate plain-text resend; Failed → retried
///     after 1 and 5 minutes, then failed. Not sent before <c>ExpiresAt</c> → expired.</item>
///   <item><b>Multi-part:</b> text longer than a caption with an attachment → media without caption, then the text;
///     the button and <c>TelegramMessageId</c> belong to the LAST message. For a pending row a non-null
///     <c>TelegramMessageId</c> means "the media part already went out" — a retry sends only the text.</item>
///   <item><b>Daily maintenance:</b> rows older than 180 days are deleted (batches of 5 000); local attachment files
///     older than 60 days that already have a Telegram <c>file_id</c> and are no longer referenced are deleted.</item>
/// </list>
/// </summary>
public class MessageDeliveryWorker(
    IServiceProvider services, ITelegramSender sender, MessageAttachmentStore store,
    ILogger<MessageDeliveryWorker> logger) : BackgroundService, IOutboxSignal
{
    public const int BatchSize = 200;
    public const int MaxAttempts = 3;
    public const int RetentionDays = 180;
    public const int AttachmentKeepDays = 60;
    public const int PruneBatch = 5000;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ChatSpacing = TimeSpan.FromMilliseconds(1100);
    public const int GlobalPerSecond = 20;

    /// <summary>Clock (tests inject a fixed/advancing one).</summary>
    internal Func<DateTime> Now { get; set; } = () => AppClock.Now;
    /// <summary>Delay (tests inject a no-op).</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Dictionary<long, DateTime> _lastByChat = new();
    private readonly Queue<DateTime> _recentSends = new();
    private DateTime _pausedUntil = DateTime.MinValue;
    private DateOnly _maintainedOn = DateOnly.MinValue;

    public void Wake()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RecoverAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Telegram outbox: tiklashda xatolik"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (sender.IsConfigured) await ProcessBatchAsync(stoppingToken);
                if (DateOnly.FromDateTime(Now()) != _maintainedOn) await MaintainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Telegram outbox: siklda xatolik — keyinroq davom etadi"); }

            try { await _signal.WaitAsync(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Startup: interrupted <c>sending</c> rows → <c>unknown</c> (never resent), then maintenance.</summary>
    internal async Task RecoverAsync(CancellationToken ct)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var stuck = await db.MessageDeliveries.Where(d => d.Status == DeliveryStatus.Sending).ToListAsync(ct);
            foreach (var d in stuck)
            {
                d.Status = DeliveryStatus.Unknown;
                d.Error = "Yuborish paytida server qayta ishga tushdi";
            }
            if (stuck.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Telegram outbox: {Count} ta xabar yuborish paytida uzilgan — qayta yuborilmaydi.", stuck.Count);
                await FinishCampaignsAsync(db, stuck.Select(d => d.BroadcastId), ct);
            }
        }
        await MaintainAsync(ct);
    }

    /// <summary>One pass over due <c>pending</c> rows (up to <see cref="BatchSize"/>). Returns rows finalised.</summary>
    internal async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var now = Now();
        if (now < _pausedUntil) return 0;

        List<string> ids;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            ids = await db.MessageDeliveries.AsNoTracking()
                .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
                .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.CreatedAt)
                .Select(d => d.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        var done = 0;
        var campaigns = new HashSet<string>();
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            if (Now() < _pausedUntil) break;
            var (finalised, broadcastId) = await ProcessOneAsync(id, ct);
            if (finalised) done++;
            if (broadcastId is not null) campaigns.Add(broadcastId);
        }

        if (campaigns.Count > 0)
        {
            using var scope = services.CreateScope();
            await FinishCampaignsAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), campaigns, ct);
        }
        return done;
    }

    private async Task<(bool Finalised, string? BroadcastId)> ProcessOneAsync(string id, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.MessageDeliveries.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null || row.Status != DeliveryStatus.Pending) return (false, null);

        var now = Now();
        if (now > row.ExpiresAt)
        {
            row.Status = DeliveryStatus.Expired;
            row.Error = "Muddati o'tdi — yuborilmadi";
            return (await TrySaveAsync(db, row.Id), row.BroadcastId);
        }

        // Per-chat spacing: postpone (not claimed — another pass sends it).
        if (_lastByChat.TryGetValue(row.ChatId, out var last) && now < last + ChatSpacing)
        {
            row.NextAttemptAt = last + ChatSpacing;
            await TrySaveAsync(db, row.Id);
            return (false, null);
        }

        // CLAIM (concurrency token).
        row.Status = DeliveryStatus.Sending;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return (false, null); }

        var attachment = row.AttachmentId is null ? null
            : await db.MessageAttachments.FirstOrDefaultAsync(a => a.Id == row.AttachmentId, CancellationToken.None);

        var outcome = await SendRowAsync(db, row, attachment);
        Finalise(row, attachment, outcome);
        if (outcome.Result == TgSendResult.Blocked)
            await TelegramBlocks.MarkBlockedAsync(db, row.ChatId, Now(), CancellationToken.None);

        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            // The row stays `sending` → becomes `unknown` at the next startup (never resent).
            logger.LogWarning(ex, "Telegram outbox: xabar {Id} holati saqlanmadi", row.Id);
        }
        return (true, row.BroadcastId);
    }

    /// <summary>Sends the row (one or two Telegram messages). Throttle bookkeeping happens here.</summary>
    private async Task<TgSendOutcome> SendRowAsync(IAppDbContext db, MessageDelivery row, MessageAttachment? attachment)
    {
        var keyboard = row.AckRequired ? MessageAckService.Keyboard(row.Id) : null;
        var text = row.Text ?? "";

        if (row.AttachmentId is not null && attachment is null)
            return new TgSendOutcome(TgSendResult.Failed, Description: "Fayl topilmadi");

        if (attachment is null)
            return await SendWithFallbackAsync(new TgSend(row.ChatId, text, null, keyboard));

        TgMedia media;
        if (attachment.TelegramFileId.Length > 0)
            media = new TgMedia(attachment.Kind, attachment.TelegramFileId, null, attachment.FileName, attachment.ContentType);
        else if (store.Exists(attachment.FileUrl))
            media = new TgMedia(attachment.Kind, null, () => store.OpenRead(attachment.FileUrl), attachment.FileName, attachment.ContentType);
        else
            return new TgSendOutcome(TgSendResult.Failed, Description: "Fayl topilmadi");

        var visible = TelegramHtml.VisibleLength(text);
        var mode = TelegramHtml.DeliveryMode(true, visible);
        if (mode == "caption" || visible == 0)
        {
            var o = await SendWithFallbackAsync(new TgSend(row.ChatId, text, media, keyboard));
            if (o.Result == TgSendResult.Ok && o.MediaFileId is { Length: > 0 } fid && attachment.TelegramFileId.Length == 0)
                attachment.TelegramFileId = fid;
            return o;
        }

        // SEPARATE: media first (no caption, no button) — unless a previous attempt already sent it.
        if (row.TelegramMessageId is null)
        {
            var first = await SendWithFallbackAsync(new TgSend(row.ChatId, "", media, null));
            if (first.Result != TgSendResult.Ok) return first;
            if (first.MediaFileId is { Length: > 0 } fid && attachment.TelegramFileId.Length == 0)
                attachment.TelegramFileId = fid;
            row.TelegramMessageId = first.MessageId ?? 0;
            await TrySaveAsync(db, row.Id);
            await ThrottleAsync(row.ChatId, ChatSpacing);
        }
        return await SendWithFallbackAsync(new TgSend(row.ChatId, text, null, keyboard));
    }

    /// <summary>Send + throttle; BadMarkup → one immediate resend as plain text without parse mode.</summary>
    private async Task<TgSendOutcome> SendWithFallbackAsync(TgSend msg)
    {
        await ThrottleAsync(msg.ChatId, TimeSpan.Zero);
        var outcome = await sender.SendAsync(msg, CancellationToken.None);
        Record(msg.ChatId);
        if (outcome.Result != TgSendResult.BadMarkup) return outcome;

        logger.LogWarning("Telegram outbox: HTML rad etildi (chat {ChatId}) — oddiy matn bilan qayta yuborilmoqda", msg.ChatId);
        await ThrottleAsync(msg.ChatId, ChatSpacing);
        var plain = msg with { HtmlText = TelegramHtml.ToPlain(msg.HtmlText), ParseHtml = false };
        var second = await sender.SendAsync(plain, CancellationToken.None);
        Record(msg.ChatId);
        return second;
    }

    private void Finalise(MessageDelivery row, MessageAttachment? attachment, TgSendOutcome o)
    {
        var now = Now();
        switch (o.Result)
        {
            case TgSendResult.Ok:
                row.Status = DeliveryStatus.Sent;
                row.SentAt = now;
                row.TelegramMessageId = o.MessageId ?? row.TelegramMessageId;
                row.Error = "";
                break;
            case TgSendResult.Blocked:
                row.Status = DeliveryStatus.Blocked;
                row.Error = Cut(o.Description ?? "Foydalanuvchi botni bloklagan");
                break;
            case TgSendResult.RateLimited:
                var wait = TimeSpan.FromSeconds(Math.Clamp(o.RetryAfterSeconds, 1, 300));
                row.Status = DeliveryStatus.Pending;
                row.NextAttemptAt = now + wait;
                row.Error = Cut(o.Description ?? "Too Many Requests");
                // The whole worker pauses (capped) — the next request would only deepen the limit.
                _pausedUntil = now + (wait > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : wait);
                break;
            default: // Failed / BadMarkup after the plain retry
                row.Attempts++;
                row.Error = Cut(o.Description ?? "Yuborilmadi");
                if (row.Attempts < MaxAttempts)
                {
                    row.Status = DeliveryStatus.Pending;
                    row.NextAttemptAt = now + (row.Attempts == 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5));
                }
                else row.Status = DeliveryStatus.Failed;
                break;
        }
    }

    /// <summary>Waits for the global 20/s gate and the per-chat spacing (<paramref name="extra"/> = minimum gap).</summary>
    private async Task ThrottleAsync(long chatId, TimeSpan extra)
    {
        var now = Now();
        var wait = TimeSpan.Zero;
        while (_recentSends.Count > 0 && _recentSends.Peek() <= now - TimeSpan.FromSeconds(1)) _recentSends.Dequeue();
        if (_recentSends.Count >= GlobalPerSecond)
            wait = _recentSends.Peek() + TimeSpan.FromSeconds(1) - now;
        if (extra > TimeSpan.Zero && _lastByChat.TryGetValue(chatId, out var last) && last + extra - now > wait)
            wait = last + extra - now;
        if (wait > TimeSpan.Zero) await Delay(wait, CancellationToken.None);
    }

    private void Record(long chatId)
    {
        var now = Now();
        _lastByChat[chatId] = now;
        _recentSends.Enqueue(now);
        if (_lastByChat.Count > 10_000)
            foreach (var k in _lastByChat.Where(kv => kv.Value < now - TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList())
                _lastByChat.Remove(k);
    }

    /// <summary>Campaigns without <c>pending/sending</c> rows → <c>done</c> (others → <c>sending</c>).</summary>
    internal async Task FinishCampaignsAsync(IAppDbContext db, IEnumerable<string?> broadcastIds, CancellationToken ct)
    {
        var ids = broadcastIds.Where(x => x is not null).Select(x => x!).Distinct().ToList();
        if (ids.Count == 0) return;
        var open = (await db.MessageDeliveries.AsNoTracking()
                .Where(d => d.BroadcastId != null && ids.Contains(d.BroadcastId)
                            && (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Sending))
                .Select(d => d.BroadcastId!).Distinct().ToListAsync(ct))
            .ToHashSet();
        var headers = await db.Broadcasts.Where(b => ids.Contains(b.Id) && b.Status != "done" && b.Status != "cancelled")
            .ToListAsync(ct);
        foreach (var b in headers)
        {
            if (!open.Contains(b.Id))
            {
                b.Status = "done";
                b.FinishedAt = Now();
            }
            else if (b.Status == "queued") b.Status = "sending";
        }
        if (headers.Count > 0)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* another pass did it */ }
        }
    }

    /// <summary>Daily: delivery log retention (180 days) and local attachment cleanup (60 days, uploaded, unused).</summary>
    internal async Task MaintainAsync(CancellationToken ct)
    {
        var now = Now();
        _maintainedOn = DateOnly.FromDateTime(now);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // Rows that can no longer go out (e.g. the bot token was missing for days) — expired, so retention applies.
        var stale = await db.MessageDeliveries
            .Where(d => d.Status == DeliveryStatus.Pending && d.ExpiresAt < now)
            .OrderBy(d => d.ExpiresAt).Take(PruneBatch).ToListAsync(ct);
        foreach (var d in stale)
        {
            d.Status = DeliveryStatus.Expired;
            d.Error = "Muddati o'tdi — yuborilmadi";
        }
        if (stale.Count > 0)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) when (db is DbContext ctx)
            {
                // A row was claimed meanwhile — leave all of them for the next day (nothing half-written stays tracked).
                foreach (var d in stale) ctx.Entry(d).State = EntityState.Detached;
            }
            await FinishCampaignsAsync(db, stale.Select(d => d.BroadcastId), ct);
        }

        var cutoff = now.AddDays(-RetentionDays);
        for (var round = 0; round < 20; round++)
        {
            var old = await db.MessageDeliveries
                .Where(d => d.CreatedAt < cutoff && d.Status != DeliveryStatus.Pending && d.Status != DeliveryStatus.Sending)
                .OrderBy(d => d.CreatedAt).Take(PruneBatch).ToListAsync(ct);
            if (old.Count == 0) break;
            db.MessageDeliveries.RemoveRange(old);
            await db.SaveChangesAsync(ct);
            if (old.Count < PruneBatch) break;
        }

        var fileCutoff = now.AddDays(-AttachmentKeepDays);
        var candidates = await db.MessageAttachments
            .Where(a => a.CreatedAt < fileCutoff && a.TelegramFileId != "" && a.FileDeletedAt == null)
            .Take(500).ToListAsync(ct);
        if (candidates.Count == 0) return;
        var ids = candidates.Select(a => a.Id).ToList();
        var inQueue = (await db.MessageDeliveries.AsNoTracking()
                .Where(d => d.AttachmentId != null && ids.Contains(d.AttachmentId)
                            && (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Sending))
                .Select(d => d.AttachmentId!).Distinct().ToListAsync(ct))
            .ToHashSet();
        var byRule = (await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Enabled && r.AttachmentId != null && ids.Contains(r.AttachmentId))
                .Select(r => r.AttachmentId!).ToListAsync(ct))
            .ToHashSet();
        var byAchievement = (await db.StudentAchievements.AsNoTracking()
                .Where(s => s.AttachmentId != null && ids.Contains(s.AttachmentId))
                .Select(s => s.AttachmentId!).ToListAsync(ct))
            .ToHashSet();
        var deleted = 0;
        foreach (var a in candidates)
        {
            if (inQueue.Contains(a.Id) || byRule.Contains(a.Id) || byAchievement.Contains(a.Id)) continue;
            try { store.Delete(a.FileUrl); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram outbox: biriktirma fayli {Id} o'chirilmadi", a.Id);
                continue;
            }
            a.FileDeletedAt = now;
            deleted++;
        }
        if (deleted > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>Saves a non-claim change (expiry / postponement); a concurrent claim wins silently.</summary>
    private async Task<bool> TrySaveAsync(IAppDbContext db, string id)
    {
        try { await db.SaveChangesAsync(CancellationToken.None); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram outbox: xabar {Id} saqlanmadi", id);
            return false;
        }
    }

    private static string Cut(string s) => s.Length <= 300 ? s : s[..300];
}
