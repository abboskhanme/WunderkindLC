using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Eskiz.uz SMS yetkazib berish holati webhook'i (callback_url). ANONIM — Eskiz serveri chaqiradi.
/// request_id bo'yicha <see cref="SmsLog"/> topib, Status'ni yangilaydi (DELIVRD/UNDELIV/...).
/// Eskiz JSON yoki form-data yuborishi mumkin — ikkalasi ham qo'llab-quvvatlanadi.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/sms")]
public class SmsCallbackController(AppDbContext db, ILogger<SmsCallbackController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    [HttpPost("callback")]
    public async Task<IActionResult> Callback()
    {
        string? requestId = null, status = null;
        try
        {
            if (Request.HasFormContentType)
            {
                requestId = Request.Form["request_id"].ToString();
                status = Request.Form["status"].ToString();
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    var d = JsonSerializer.Deserialize<EskizCallbackDto>(body, Opts);
                    requestId = d?.request_id;
                    status = d?.status;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eskiz callback o'qishda xatolik");
        }

        // Eskiz qayta urinishlarda doim 200 kutadi — yozuv topilmasa ham OK qaytaramiz.
        if (string.IsNullOrWhiteSpace(requestId)) return Ok(new { ok = true });

        var log = await db.SmsLogs.FirstOrDefaultAsync(l => l.RequestId == requestId);
        if (log is not null)
        {
            if (!string.IsNullOrWhiteSpace(status)) log.Status = status!;
            log.UpdatedAt = AppClock.Now;
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
