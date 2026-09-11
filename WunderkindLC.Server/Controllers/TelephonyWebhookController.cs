using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Hubs;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MoiZvonki webhook qabul qiluvchi — provayder qo'ng'iroq hodisalarini (call.start=1,
/// call.answer=2, call.finish=4, sms=32) shu endpointga POST qiladi. Autentifikatsiya —
/// URL'dagi maxfiy segment (JWT emas, chunki so'rov tashqi serverdan keladi).
/// Korrelyatsiya: event_pbx_call_id → Call.AsteriskUniqueId (provayder qo'ng'iroq id'si
/// sifatida qayta ishlatiladi). CRM'dan boshlangan chiquvchi qo'ng'iroq birinchi hodisada
/// raqam+vaqt bo'yicha kutayotgan yozuvga bog'lanadi; qolganlari (kiruvchi yoki telefondan
/// to'g'ridan-to'g'ri terilgan) YANGI yozuv sifatida jurnalga tushadi.
/// DIQQAT: chiquvchida provayder call.answer'ni call.start'dan keyin darhol yuboradi
/// (haqiqiy javob vaqti noma'lum) — aniq holat/davomiylik call.finish'da keladi.
/// Telefonda internet bo'lmasa faqat call.finish kelishi mumkin — bu ham qo'llanadi.
/// </summary>
[ApiController]
[Route("api/telephony")]
public class TelephonyWebhookController(
    AppDbContext db, MoiZvonkiService moizvonki, IHubContext<LiveHub> hub,
    ILogger<TelephonyWebhookController> logger) : ControllerBase
{
    [HttpPost("moizvonki/{secret}")]
    [AllowAnonymous]
    public async Task<IActionResult> MoiZvonki(string secret, [FromBody] JsonElement body)
    {
        // Sir mos kelmasa — 404 (endpoint mavjudligini ham oshkor qilmaymiz).
        if (!moizvonki.IsConfigured || secret != moizvonki.WebhookSecret) return NotFound();

        if (!body.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
            return Ok(); // notanish format — provayder qayta urinmasin deb 200

        var type = GetInt(ev, "event_type");
        if (type is not (1 or 2 or 4)) return Ok(); // SMS (32) va boshqalar — hozircha jurnalga yozilmaydi

        var pbxId = GetStr(ev, "event_pbx_call_id");
        if (pbxId.Length == 0) return Ok();
        var direction = GetInt(ev, "direction"); // 0=kiruvchi, 1=chiquvchi
        var clientNumber = GetStr(ev, "client_number");

        var call = await FindOrCreateCallAsync(pbxId, direction, clientNumber);

        // Terminal holatga yetgan yozuvga takror yetkazilgan hodisa — e'tiborsiz (idempotent).
        if (call.Status is "completed" or "no_answer" or "busy" or "failed") return Ok();

        switch (type)
        {
            case 1: // call.start
                if (call.Status == "originating") call.Status = "ringing";
                break;
            case 2: // call.answer (chiquvchida start'dan keyin darhol keladi — taxminiy)
                if (call.AnsweredAt is null)
                {
                    call.Status = "answered";
                    call.AnsweredAt = AppClock.Now;
                }
                break;
            case 4: // call.finish — yakuniy haqiqat (answered/duration/recording shu yerda)
                var answered = GetBool(ev, "answered");
                var duration = Math.Max(0, GetInt(ev, "duration"));
                call.EndedAt = AppClock.Now;
                if (answered)
                {
                    call.Status = "completed";
                    call.DurationSeconds = duration;
                    // Chiquvchida call.answer taxminiy edi — aniq davomiylikdan qayta hisoblaymiz.
                    call.AnsweredAt = call.EndedAt.Value.AddSeconds(-duration);
                }
                else
                {
                    call.Status = "no_answer";
                    call.DurationSeconds = 0;
                }
                var rec = GetStr(ev, "recording");
                if (rec.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    call.RecordingFile = rec; // to'liq URL — CallsController.Recording proxy qiladi
                // db_call_id — calls.list sinxronizatsiyasi bilan takrorlanmaslik uchun.
                var dbId = GetStr(ev, "db_call_id");
                if (dbId.Length > 0 && call.ProviderDbId.Length == 0) call.ProviderDbId = dbId;
                break;
        }

        await db.SaveChangesAsync();

        await hub.Clients.Group(LiveHub.Group("calls")).SendAsync("callUpdated", new
        {
            id = call.Id,
            status = call.Status,
            phoneNumber = call.PhoneNumber,
            studentId = call.StudentId,
            answeredAt = call.AnsweredAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            endedAt = call.EndedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            durationSeconds = call.DurationSeconds,
            hasRecording = call.RecordingFile.Length > 0,
        });

        return Ok();
    }

    /// <summary>
    /// Hodisani Call yozuviga bog'lash: (1) pbxId bo'yicha; (2) chiquvchida — CRM'dan
    /// boshlangan KUTAYOTGAN yozuv (raqam mos + oxirgi 15 daq + hali provayder id'siz);
    /// (3) topilmasa yangi yozuv (kiruvchi yoki telefondan qo'lda terilgan chiquvchi).
    /// </summary>
    private async Task<Call> FindOrCreateCallAsync(string pbxId, int direction, string clientNumber)
    {
        var existing = await db.Calls.FirstOrDefaultAsync(c => c.AsteriskUniqueId == pbxId);
        if (existing is not null) return existing;

        var normalized = PhoneUtil.Normalize(clientNumber);
        var key = PhoneUtil.Key(clientNumber);

        if (direction == 1 && key.Length >= 7)
        {
            var cutoff = AppClock.Now.AddMinutes(-15);
            var pending = await db.Calls
                .Where(c => c.Direction == "outbound" && c.AsteriskUniqueId == ""
                            && c.EndedAt == null && c.StartedAt >= cutoff)
                .OrderByDescending(c => c.StartedAt)
                .Take(10).ToListAsync();
            var match = pending.FirstOrDefault(c => PhoneUtil.Key(c.PhoneNumber) == key);
            if (match is not null)
            {
                match.AsteriskUniqueId = pbxId;
                return match;
            }
        }

        // Kiruvchi (yoki CRM'siz qilingan chiquvchi) — raqamdan o'quvchini topamiz.
        var student = key.Length >= 7
            ? await db.Students.FirstOrDefaultAsync(s =>
                s.Phone == normalized || s.ParentPhone == normalized ||
                s.FatherPhone == normalized || s.MotherPhone == normalized)
            : null;

        var call = new Call
        {
            StudentId = student?.Id,
            PhoneNumber = normalized.Length > 0 ? normalized : clientNumber,
            Direction = direction == 0 ? "inbound" : "outbound",
            Status = "ringing",
            AsteriskUniqueId = pbxId,
        };
        db.Calls.Add(call);
        logger.LogInformation("Telefoniya: yangi {Dir} qo'ng'iroq jurnalga tushdi ({Phone})",
            call.Direction, call.PhoneNumber);
        return call;
    }

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            }
            : "";

    private static int GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n) ? n : 0,
            JsonValueKind.String => int.TryParse(v.GetString(), out var s) ? s : 0,
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
