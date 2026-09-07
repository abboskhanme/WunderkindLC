using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// Markaz kameralari (videokuzatuv) — Boshqaruv → Kameralar. CRUD + jonli oqim (HLS, media-shlyuz
/// orqali proksilanadi) + playback/qirqib yuklab olish (yozuvdan MP4). Shlyuz (MediaMTX) ichki
/// tarmoqda; brauzer faqat shu autentifikatsiyalangan endpointlar orqali ko'radi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("cameras")]
[Route("api/admin/cameras")]
public class CamerasController(AppDbContext db, CameraGateway gateway) : ControllerBase
{
    private static CameraDto Dto(Camera c) =>
        new(c.Id, c.Name, c.Location, c.RtspUrl, c.RtspSubUrl, c.RetentionDays, c.IsActive, c.Note,
            c.RecordEnabled);

    /// <summary>Markazdagi YOZUV bosh kaliti. ⚠️ Sozlama yo'q bo'lsa — yozuv O'CHIQ
    /// (fail-closed): "sozlanmagan" holat jimgina 24/7 yozuvni yoqib yubormasin.</summary>
    private async Task<bool> RecordGloballyOnAsync() =>
        (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync())?.CameraRecordEnabled ?? false;

    /// <summary>Kamerani shlyuzga (qayta) yuboradi — yozuv bosh kalit bilan birga hal qilinadi.</summary>
    private async Task SyncAsync(Camera c) =>
        await gateway.EnsureAsync(c, CameraRules.ShouldRecord(await RecordGloballyOnAsync(), c));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CameraDto>>> List() =>
        (await db.Cameras.OrderBy(c => c.Name).ToListAsync()).Select(Dto).ToList();

    [HttpPost]
    public async Task<ActionResult<CameraDto>> Create(SaveCameraRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Nomi shart" });
        if (string.IsNullOrWhiteSpace(req.RtspUrl)) return BadRequest(new { message = "RTSP manzili shart" });
        var c = new Camera
        {
            Name = req.Name.Trim(),
            Location = (req.Location ?? "").Trim(),
            RtspUrl = req.RtspUrl.Trim(),
            RtspSubUrl = (req.RtspSubUrl ?? "").Trim(),
            RetentionDays = CameraRules.ClampRetention(req.RetentionDays),
            IsActive = req.IsActive,
            Note = (req.Note ?? "").Trim(),
            RecordEnabled = req.RecordEnabled,
        };
        db.Cameras.Add(c);
        await db.SaveChangesAsync();
        await SyncAsync(c);
        return Dto(c);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CameraDto>> Update(string id, SaveCameraRequest req)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Nomi shart" });
        if (string.IsNullOrWhiteSpace(req.RtspUrl)) return BadRequest(new { message = "RTSP manzili shart" });
        c.Name = req.Name.Trim();
        c.Location = (req.Location ?? "").Trim();
        c.RtspUrl = req.RtspUrl.Trim();
        c.RtspSubUrl = (req.RtspSubUrl ?? "").Trim();
        c.RetentionDays = CameraRules.ClampRetention(req.RetentionDays);
        c.IsActive = req.IsActive;
        c.Note = (req.Note ?? "").Trim();
        c.RecordEnabled = req.RecordEnabled;
        await db.SaveChangesAsync();
        await SyncAsync(c);
        return Dto(c);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        db.Cameras.Remove(c);
        await db.SaveChangesAsync();
        await gateway.RemoveAsync(id);
        return NoContent();
    }

    /// <summary>Jonli HLS pleylisti — shlyuzda kamerani yoqib, index.m3u8 ni proksilaydi.</summary>
    [HttpGet("{id}/index.m3u8")]
    public async Task<IActionResult> HlsIndex(string id)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        await SyncAsync(c);
        return await ProxyAsync(await gateway.HlsAsync(id, "index.m3u8" + Request.QueryString.Value),
            "application/vnd.apple.mpegurl");
    }

    /// <summary>HLS segmenti (.ts) — proksilanadi. (index.m3u8/clip — alohida literal yo'llar.)</summary>
    [HttpGet("{id}/{file}")]
    public async Task<IActionResult> HlsSegment(string id, string file)
    {
        if (!file.EndsWith(".ts") && !file.EndsWith(".m3u8") && !file.EndsWith(".mp4"))
            return NotFound();
        return await ProxyAsync(await gateway.HlsAsync(id, file + Request.QueryString.Value), null);
    }

    /// <summary>Playback / qirqib olish — yozuvdan MP4. start ("yyyy-MM-ddTHH:mm:ss"), duration soniyada.
    /// download=1 bo'lsa fayl sifatida yuklab beriladi.</summary>
    [HttpGet("{id}/clip")]
    public async Task<IActionResult> Clip(
        string id, [FromQuery] string start, [FromQuery] int duration, [FromQuery] int download = 0)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrEmpty(start) || duration <= 0)
            return BadRequest(new { message = "start va duration kerak" });
        // ⚠️ Yozuv o'chiq bo'lsa shlyuz shunchaki "topilmadi" derdi va foydalanuvchi buni
        // NOSOZLIK deb o'ylardi. Sabab OCHIQ aytiladi (sozlama, xato emas).
        if (!CameraRules.ShouldRecord(await RecordGloballyOnAsync(), c))
            return BadRequest(new
            {
                message = "Bu kamera yozib borilmayapti — yozuv o'chirilgan. "
                        + "Sozlamalar → Kamera integratsiya (yoki kamera kartasi) dan yoqing. "
                        + "Yoqilgandan KEYINGI vaqtlargina yozuvda bo'ladi.",
            });
        if (duration > 3600) duration = 3600; // himoya: maks 1 soat

        var resp = await gateway.PlaybackAsync(id, start, duration);
        if (!resp.IsSuccessStatusCode)
            return StatusCode(502, new { message = "Yozuv topilmadi yoki shlyuz javob bermadi" });

        var stream = await resp.Content.ReadAsStreamAsync();
        if (download == 1)
        {
            var fileName = $"{c.Name}_{start.Replace(":", "-")}_{duration}s.mp4";
            return File(stream, "video/mp4", fileName);
        }
        return File(stream, "video/mp4");
    }

    private async Task<IActionResult> ProxyAsync(HttpResponseMessage resp, string? contentType)
    {
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode);
        var stream = await resp.Content.ReadAsStreamAsync();
        var ct = contentType ?? resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return File(stream, ct);
    }
}
