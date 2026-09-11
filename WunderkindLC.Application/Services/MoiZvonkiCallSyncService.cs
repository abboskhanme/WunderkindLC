using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Hubs;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// MoiZvonki qo'ng'iroqlar TARIXI sinxronizatsiyasi (calls.list) — "Yozuvlar tarixi"
/// webhook'larga bog'liq bo'lib qolmasin: eski qo'ng'iroqlar (integratsiyagacha bo'lganlar ham),
/// webhook yetib kelmagan/obuna ishlamagan holatlar — hammasi davriy ravishda tortib olinadi.
///
/// Ish tartibi (hujjat tavsiyasi bo'yicha from_id bilan ketma-ket):
///  • from_id = bazadagi eng katta ProviderDbId + 1 (birinchi ishga tushishda 0 — TO'LIQ tarix);
///  • supervised=1 — barcha xodimlarning qo'ng'iroqlari (api_key administrator bo'lsa);
///  • sahifalab (max_results=100) yuriladi, har siklda ko'pi bilan 10 sahifa (keyingisida davom etadi);
///  • dublikat himoyasi: db_call_id (ProviderDbId) bo'yicha; webhook allaqachon yozgan bo'lsa
///    (pbx orqali) — raqam+vaqt oynasi bilan moslab, yetishmagan maydonlar TO'LDIRILADI.
/// Har 5 daqiqada uyg'onadi. Qo'lda: POST api/admin/calls/telephony/sync (SyncOnceAsync).
/// </summary>
public class MoiZvonkiCallSyncService(
    IServiceProvider services,
    MoiZvonkiService moizvonki,
    IHubContext<LiveHub> hub,
    ILogger<MoiZvonkiCallSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!moizvonki.Enabled) return;

        // App ko'tarilib bo'lsin.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (added, updated) = await SyncOnceAsync(stoppingToken);
                if (added + updated > 0)
                    logger.LogInformation("MoiZvonki tarix sinxron: {Added} yangi, {Updated} yangilandi", added, updated);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MoiZvonki tarix sinxronida xato");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Bir sinxron sikli — controller (qo'lda "Yangilash") ham shu metodni chaqiradi.</summary>
    public async Task<(int Added, int Updated)> SyncOnceAsync(CancellationToken ct = default)
    {
        if (!moizvonki.IsConfigured) return (0, 0);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // Davom etish nuqtasi — bazadagi eng katta db_call_id (raqamli bo'lmasa 0 dan).
        var maxDbId = (await db.Calls.Where(c => c.ProviderDbId != "")
                .Select(c => c.ProviderDbId).ToListAsync(ct))
            .Select(s => long.TryParse(s, out var n) ? n : 0L)
            .DefaultIfEmpty(0L).Max();

        var added = 0;
        var updated = 0;
        var fromId = maxDbId + 1;
        long offset = 0;

        for (var page = 0; page < 10; page++)
        {
            var extra = new Dictionary<string, object?>
            {
                ["from_id"] = fromId,
                ["max_results"] = 100,
                ["supervised"] = 1,
            };
            if (offset > 0) extra["from_offset"] = offset;

            var (ok, body) = await moizvonki.CallApiAsync("calls.list", extra, ct);
            if (!ok)
            {
                logger.LogWarning("MoiZvonki calls.list xato: {Body}", body);
                break;
            }

            JsonElement root;
            try { root = JsonDocument.Parse(body).RootElement; }
            catch { logger.LogWarning("MoiZvonki calls.list javobi JSON emas"); break; }

            var items = FindItemsArray(root);
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) break;

            foreach (var item in items.EnumerateArray())
            {
                var r = await UpsertAsync(db, item, ct);
                if (r == UpsertResult.Added) added++;
                else if (r == UpsertResult.Updated) updated++;
            }
            await db.SaveChangesAsync(ct);

            // Keyingi sahifa: results_next_offset bo'lsa shu bilan, aks holda to'xtaymiz.
            var next = GetLong(root, "results_next_offset");
            if (next <= 0 || next == offset) break;
            offset = next;
        }

        if (added > 0)
            await hub.Clients.Group(LiveHub.Group("calls")).SendAsync("historySynced", new { added }, ct);
        return (added, updated);
    }

    private enum UpsertResult { Skipped, Added, Updated }

    private static async Task<UpsertResult> UpsertAsync(IAppDbContext db, JsonElement item, CancellationToken ct)
    {
        var dbId = GetStr(item, "db_call_id");
        if (dbId.Length == 0) return UpsertResult.Skipped;

        var existing = await db.Calls.FirstOrDefaultAsync(c => c.ProviderDbId == dbId, ct);
        var direction = GetLong(item, "direction") == 0 ? "inbound" : "outbound";
        var clientNumber = GetStr(item, "client_number");
        var answered = GetBool(item, "answered");
        var duration = (int)Math.Max(0, GetLong(item, "duration"));
        var recording = GetStr(item, "recording");
        var startedAt = FromUnix(GetLong(item, "start_time"));
        var endedAt = FromUnix(GetLong(item, "end_time"));

        if (existing is null)
        {
            // Webhook (pbx) orqali allaqachon yozilgan bo'lishi mumkin — raqam+vaqt bilan moslaymiz
            // (dublikat qator ochilmasin): ±10 daqiqa oynasi, db_call_id hali bo'sh.
            var key = PhoneUtil.Key(clientNumber);
            if (key.Length >= 7 && startedAt is { } st)
            {
                var lo = st.AddMinutes(-10);
                var hi = st.AddMinutes(10);
                var candidates = await db.Calls
                    .Where(c => c.ProviderDbId == "" && c.Direction == direction
                                && c.StartedAt >= lo && c.StartedAt <= hi)
                    .ToListAsync(ct);
                existing = candidates.FirstOrDefault(c => PhoneUtil.Key(c.PhoneNumber) == key);
            }
        }

        if (existing is not null)
        {
            // Yetishmagan maydonlarni to'ldiramiz (webhook yozgani ustuvor, bo'shlarigina).
            var changed = false;
            if (existing.ProviderDbId.Length == 0) { existing.ProviderDbId = dbId; changed = true; }
            if (existing.RecordingFile.Length == 0 && recording.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            { existing.RecordingFile = recording; changed = true; }
            if (existing.EndedAt is null && endedAt is not null)
            {
                existing.EndedAt = endedAt;
                existing.Status = answered ? "completed" : "no_answer";
                existing.DurationSeconds = answered ? duration : 0;
                if (answered && existing.AnsweredAt is null) existing.AnsweredAt = endedAt.Value.AddSeconds(-duration);
                changed = true;
            }
            return changed ? UpsertResult.Updated : UpsertResult.Skipped;
        }

        // Butunlay yangi (integratsiyagacha bo'lgan yoki webhook yetmagan) qo'ng'iroq.
        var normalized = PhoneUtil.Normalize(clientNumber);
        var studentId = PhoneUtil.Key(clientNumber).Length >= 7
            ? await db.Students
                .Where(s => s.Phone == normalized || s.ParentPhone == normalized
                            || s.FatherPhone == normalized || s.MotherPhone == normalized)
                .Select(s => (string?)s.Id).FirstOrDefaultAsync(ct)
            : null;

        db.Calls.Add(new Call
        {
            StudentId = studentId,
            PhoneNumber = normalized.Length > 0 ? normalized : clientNumber,
            Direction = direction,
            Status = answered ? "completed" : "no_answer",
            StartedAt = startedAt ?? AppClock.Now,
            AnsweredAt = answered && endedAt is not null ? endedAt.Value.AddSeconds(-duration) : null,
            EndedAt = endedAt,
            DurationSeconds = answered ? duration : 0,
            RecordingFile = recording.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? recording : "",
            ProviderDbId = dbId,
            Note = GetStr(item, "user_account") is { Length: > 0 } ua ? $"MoiZvonki operator: {ua}" : "",
        });
        return UpsertResult.Added;
    }

    /// <summary>Javobdan qo'ng'iroqlar massivini topadi ("results" yoki birinchi massiv-maydon).</summary>
    private static JsonElement FindItemsArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return default;
        if (root.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Array) return r;
        foreach (var p in root.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Array)
                return p.Value;
        return default;
    }

    /// <summary>Unix soniya → markaz mintaqasi (UTC+5) vaqti. 0/manfiy — null.</summary>
    private static DateTime? FromUnix(long seconds) =>
        seconds > 0 ? AppClock.ToLocal(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime) : null;

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            }
            : "";

    private static long GetLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var s) ? s : 0,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => v.GetString() is "1" or "true" or "True",
            _ => false,
        };
    }
}
