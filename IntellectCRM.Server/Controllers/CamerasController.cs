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
public class CamerasController(AppDbContext db, CameraGateway gateway, NvrArchiveService nvr) : ControllerBase
{
    private static CameraDto Dto(Camera c, CenterMeta? m) =>
        new(c.Id, c.Name, c.Location, c.RtspUrl, c.RtspSubUrl, c.RetentionDays, c.IsActive, c.Note,
            c.RecordEnabled, c.NvrChannel,
            CameraRules.ArchiveSource(NvrArchiveService.IsConfigured(m), m?.CameraRecordEnabled ?? false, c));

    /// <summary>Markaz sozlamalari. ⚠️ Qator yo'q bo'lsa — yozuv ham, NVR ham O'CHIQ
    /// (fail-closed): "sozlanmagan" holat jimgina 24/7 yozuvni yoqib yubormasin.</summary>
    private Task<CenterMeta?> MetaAsync() => db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();

    /// <summary>
    /// Kamerani shlyuzga (qayta) yuboradi.
    /// ⚠️ NVR sozlangan kamerani BIZ yozmaymiz — arxiv NVR'da (takroriy nusxa kerak emas).
    /// </summary>
    private async Task SyncAsync(Camera c, CenterMeta? m) =>
        await gateway.EnsureAsync(c, CameraRules.ShouldRecordWithNvr(
            NvrArchiveService.IsConfigured(m), m?.CameraRecordEnabled ?? false, c));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CameraDto>>> List()
    {
        var m = await MetaAsync();
        return (await db.Cameras.OrderBy(c => c.Name).ToListAsync()).Select(c => Dto(c, m)).ToList();
    }

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
            NvrChannel = req.NvrChannel < 0 ? 0 : req.NvrChannel,
        };
        db.Cameras.Add(c);
        await db.SaveChangesAsync();
        var meta = await MetaAsync();
        await SyncAsync(c, meta);
        return Dto(c, meta);
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
        c.NvrChannel = req.NvrChannel < 0 ? 0 : req.NvrChannel;
        await db.SaveChangesAsync();
        var meta = await MetaAsync();
        await SyncAsync(c, meta);
        return Dto(c, meta);
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
        await SyncAsync(c, await MetaAsync());
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

    /// <summary>
    /// Playback / qirqib olish — arxivdan MP4. start ("yyyy-MM-ddTHH:mm:ss", MARKAZ vaqti),
    /// duration soniyada. download=1 bo'lsa fayl sifatida yuklab beriladi.
    /// </summary>
    /// <remarks>
    /// Arxiv IKKI manbadan kelishi mumkin (<see cref="CameraRules.ArchiveSource"/>):
    /// <b>NVR</b> (afzal — u allaqachon yozib turibdi) yoki bizning shlyuz yozuvimiz.
    /// Klient uchun farqi yo'q — javob ikkalasida ham MP4.
    /// </remarks>
    [HttpGet("{id}/clip")]
    public async Task<IActionResult> Clip(
        string id, [FromQuery] string start, [FromQuery] int duration, [FromQuery] int download = 0)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrEmpty(start) || duration <= 0)
            return BadRequest(new { message = "start va duration kerak" });
        if (duration > NvrArchiveService.MaxClipSeconds) duration = NvrArchiveService.MaxClipSeconds;

        var meta = await MetaAsync();
        var source = CameraRules.ArchiveSource(
            NvrArchiveService.IsConfigured(meta), meta?.CameraRecordEnabled ?? false, c);
        var fileName = $"{c.Name}_{start.Replace(":", "-")}_{duration}s.mp4";

        // ⚠️ Arxiv umuman yo'q bo'lsa sabab OCHIQ aytiladi. Ilgari shlyuz shunchaki
        // "topilmadi" derdi va foydalanuvchi buni NOSOZLIK deb o'ylardi (aslida sozlama).
        if (source == CameraRules.ArchiveNone)
            return BadRequest(new { message = NoArchiveMessage(c, meta) });

        if (source == CameraRules.ArchiveNvr)
        {
            if (!DateTime.TryParse(start, out var startLocal))
                return BadRequest(new { message = "start noto'g'ri formatda (yyyy-MM-ddTHH:mm:ss kutiladi)." });

            var (stream, error) = await nvr.ClipAsync(meta!, c.NvrChannel, startLocal, duration, HttpContext.RequestAborted);
            if (stream is null) return StatusCode(502, new { message = error });
            return download == 1 ? File(stream, "video/mp4", fileName) : File(stream, "video/mp4");
        }

        var resp = await gateway.PlaybackAsync(id, start, duration);
        if (!resp.IsSuccessStatusCode)
            return StatusCode(502, new { message = "Yozuv topilmadi yoki shlyuz javob bermadi" });

        var local = await resp.Content.ReadAsStreamAsync();
        return download == 1 ? File(local, "video/mp4", fileName) : File(local, "video/mp4");
    }

    /// <summary>
    /// NVR arxivida shu oraliqda yozuv bormi — "NVR'ni sinash" va kunni tekshirish uchun.
    /// from/to — MARKAZ vaqti ("yyyy-MM-ddTHH:mm:ss"). to berilmasa from + 1 kun.
    /// </summary>
    /// <remarks>
    /// ⚠️ Xato holatida ham <b>200</b> qaytadi, ichida <c>ok=false</c> va SABAB. Sozlash
    /// aynan shu matn bilan qilinadi (manzil? port? login? kanal?) — 502 bilan qaytarilsa
    /// klientda "server xatosi" bo'lib ko'rinib, sabab yo'qolardi.
    /// </remarks>
    [HttpGet("{id}/nvr-search")]
    public async Task<ActionResult<NvrSearchDto>> NvrSearch(
        string id, [FromQuery] string from, [FromQuery] string? to = null)
    {
        var c = await db.Cameras.FindAsync(id);
        if (c is null) return NotFound();
        var meta = await MetaAsync();

        if (!NvrArchiveService.IsConfigured(meta))
            return new NvrSearchDto(false,
                "NVR sozlanmagan: Sozlamalar → Kamera integratsiya da manzilni kiriting va "
                + ".env ga NVR_USERNAME / NVR_PASSWORD qo'shing.", []);
        if (c.NvrChannel <= 0)
            return new NvrSearchDto(false,
                $"\"{c.Name}\" uchun NVR kanali ko'rsatilmagan (kamera kartasi → Tahrirlash).", []);
        if (!DateTime.TryParse(from, out var fromLocal))
            return new NvrSearchDto(false, "from noto'g'ri formatda (yyyy-MM-ddTHH:mm:ss).", []);
        var toLocal = DateTime.TryParse(to, out var t) ? t : fromLocal.AddDays(1);

        var (segments, error) = await nvr.SearchAsync(meta!, c.NvrChannel, fromLocal, toLocal, HttpContext.RequestAborted);
        return new NvrSearchDto(error is null, error ?? "",
            segments.Select(x => new NvrSegmentDto(x.Start, x.End)).ToList());
    }

    /// <summary>Arxiv yo'qligining ANIQ sababi — foydalanuvchi nimani tuzatishini bilsin.</summary>
    private static string NoArchiveMessage(Camera c, CenterMeta? m)
    {
        if (m is { NvrEnabled: true } && c.NvrChannel <= 0)
            return $"NVR yoqilgan, lekin \"{c.Name}\" uchun kanal raqami ko'rsatilmagan "
                 + "(kamera kartasi → Tahrirlash → «NVR kanali»).";
        if (m is { NvrEnabled: true } && !AppSecrets.NvrCredentialsConfigured)
            return "NVR yoqilgan, lekin .env da NVR_USERNAME / NVR_PASSWORD yo'q.";
        if (!c.IsActive) return $"\"{c.Name}\" faol emas.";
        if (m?.CameraRecordEnabled != true)
            return "Arxiv yo'q: NVR ulanmagan va 24/7 yozuv ham o'chirilgan "
                 + "(Sozlamalar → Kamera integratsiya).";
        return $"\"{c.Name}\" uchun yozuv o'chirilgan (kamera kartasi → Tahrirlash → «Yozib borish»).";
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
