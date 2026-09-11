using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers.Cti;

/// <summary>
/// CTI (Local Call) MOBIL API — Android agent-ilovasi shartnomasi. Ilova qo'ng'iroq metadata,
/// audio yozuv va hodisalarni shu endpointlarga yuboradi. Login'dan tashqari hammasi
/// <see cref="Roles.CtiAgent"/> token bilan; agent FAQAT O'ZINING yozuvlariga tegadi
/// (tokendagi NameIdentifier=agentId). Shartnomani O'ZGARTIRMANG (ilova unga bog'langan).
/// </summary>
[ApiController]
[Route("api/mobile")]
[Authorize(Roles = Roles.CtiAgent)]
public class CtiMobileController(
    AppDbContext db, JwtTokenService jwt, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Ruxsat etilgan audio kengaytmalar (Guid asosli fayl nomi — path-traversal xavfi yo'q).</summary>
    private static readonly HashSet<string> AllowedAudioExt =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".amr", ".wav", ".ogg", ".aac", ".3gp" };

    private string AgentId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>Yozuvlar papkasi: <c>Cti:RecordingsPath</c> yoki default <c>{ContentRoot}/recordings/cti</c>.
    /// DIQQAT: ATAYIN /uploads OSTIDA EMAS (u ochiq statik servis qilinadi).</summary>
    private string RecordingsDir()
    {
        var configured = config["Cti:RecordingsPath"];
        var dir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "recordings", "cti")
            : configured;
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------- Auth ----------

    /// <summary>Ilova login: login+parol → JWT token, agentId va WebSocket manzili.</summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<CtiLoginResponse>> Login(CtiLoginRequest req)
    {
        var login = req.LoginValue;
        var password = req.PasswordValue;
        if (login.Length == 0 || password.Length == 0)
            return Unauthorized(new { message = "Login yoki parol bo'sh" });

        var agent = await db.CtiAgents.FirstOrDefaultAsync(a => a.Login == login);
        if (agent is null || !agent.IsActive || !PasswordHasher.Verify(password, agent.PasswordHash))
            return Unauthorized(new { message = "Login yoki parol noto'g'ri" });

        var token = jwt.CreateAgentToken(agent.Id, agent.DisplayName);
        var isHttps = Request.IsHttps || Request.Headers["X-Forwarded-Proto"] == "https";
        var wsUrl = $"{(isHttps ? "wss" : "ws")}://{Request.Host}/ws";
        return new CtiLoginResponse(token, agent.Id, wsUrl);
    }

    // ---------- Qo'ng'iroqlar ----------

    /// <summary>Qo'ng'iroq metadatasini qabul qilib yozuv yaratadi (raqam bo'yicha o'quvchini moslaydi).
    /// IDEMPOTENT: ilova javobni o'qiy olmay/tarmoq uzilib QAYTA yuborsa — o'sha qo'ng'iroq
    /// (agent + raqam + yo'nalish + boshlanish vaqti) dublikat YARATILMAYDI, mavjud id qaytadi.</summary>
    [HttpPost("calls")]
    public async Task<ActionResult<CtiCallCreatedResponse>> CreateCall(CtiCallCreateRequest req)
    {
        var direction = req.Direction is "incoming" or "outgoing" or "missed" ? req.Direction : "outgoing";
        var remote = PhoneUtil.Normalize(req.RemoteNumber);
        var remoteNumber = remote.Length > 0 ? remote : (req.RemoteNumber ?? "").Trim();
        var startedAt = ParseDate(req.StartedAt) ?? AppClock.Now;

        // Qayta yuborilgan (retry) qo'ng'iroqni tanib olamiz — kalit: agent+raqam+yo'nalish+boshlanish.
        var existing = await db.CtiCallRecords.FirstOrDefaultAsync(c =>
            c.AgentId == AgentId && c.RemoteNumber == remoteNumber &&
            c.Direction == direction && c.StartedAt == startedAt);
        if (existing is not null)
        {
            // Retry'da to'ldirilgan maydonlarni yangilab qo'yamiz (masalan duration keyin aniqlangan).
            if (req.DurationSec > 0 && existing.DurationSec == 0) existing.DurationSec = req.DurationSec;
            existing.AnsweredAt ??= ParseDate(req.AnsweredAt);
            existing.EndedAt ??= ParseDate(req.EndedAt);
            await db.SaveChangesAsync();
            return new CtiCallCreatedResponse(existing.Id);
        }

        var studentId = await MatchStudentIdAsync(req.RemoteNumber);
        var call = new CtiCallRecord
        {
            AgentId = AgentId,
            Direction = direction,
            RemoteNumber = remoteNumber,
            ContactName = (req.ContactName ?? "").Trim(),
            StudentId = studentId,
            StartedAt = startedAt,
            AnsweredAt = ParseDate(req.AnsweredAt),
            EndedAt = ParseDate(req.EndedAt),
            DurationSec = Math.Max(0, req.DurationSec),
        };
        db.CtiCallRecords.Add(call);
        await db.SaveChangesAsync();
        return new CtiCallCreatedResponse(call.Id);
    }

    /// <summary>Qo'ng'iroq audio yozuvini yuklaydi (multipart, forma nomi moslashuvchan). 50MB limit.</summary>
    [HttpPost("calls/{serverCallId}/audio")]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> UploadAudio(string serverCallId)
    {
        var call = await db.CtiCallRecords.FirstOrDefaultAsync(c => c.Id == serverCallId);
        if (call is null || call.AgentId != AgentId) return NotFound();

        var file = Request.Form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl yuborilmadi" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAudioExt.Contains(ext))
            return BadRequest(new { message = "Audio format qo'llab-quvvatlanmaydi" });

        // Fayl nomi = serverCallId (Guid) + kengaytma — foydalanuvchi nomidan MUSTAQIL (path-traversal yo'q).
        var fileName = serverCallId + ext;
        var fullPath = Path.Combine(RecordingsDir(), fileName);
        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream, HttpContext.RequestAborted);

        call.AudioPath = fileName;
        call.AudioUploaded = true;
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Qo'ng'iroq hodisasini qo'shadi va yozuvni yangilaydi (answered/ended).</summary>
    [HttpPost("calls/{serverCallId}/events")]
    public async Task<IActionResult> AddEvent(string serverCallId, CtiCallEventRequest req)
    {
        var call = await db.CtiCallRecords.FirstOrDefaultAsync(c => c.Id == serverCallId);
        if (call is null || call.AgentId != AgentId) return NotFound();

        var type = req.Type is "ringing" or "answered" or "ended" ? req.Type : "";
        if (type.Length == 0) return BadRequest(new { message = "Noto'g'ri hodisa turi" });
        var at = ParseDate(req.At) ?? AppClock.Now;

        db.CtiCallEvents.Add(new CtiCallEvent { CallId = call.Id, Type = type, At = at });

        if (type == "answered" && call.AnsweredAt is null)
            call.AnsweredAt = at;
        else if (type == "ended")
        {
            call.EndedAt = at;
            if (call.DurationSec == 0 && call.AnsweredAt is { } ans && at > ans)
                call.DurationSec = (int)(at - ans).TotalSeconds;
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    // ---------- Agent holati ----------

    /// <summary>Agent tirikligi — onlayn + oxirgi faollik vaqtini yangilaydi.</summary>
    [HttpPost("agents/heartbeat")]
    public async Task<IActionResult> Heartbeat()
    {
        await db.CtiAgents.Where(a => a.Id == AgentId)
            .ExecuteUpdateAsync(up => up
                .SetProperty(a => a.IsOnline, true)
                .SetProperty(a => a.LastSeenAt, AppClock.Now));
        return Ok();
    }

    /// <summary>FCM qurilma tokenini yangilaydi (oflaynda uyg'otish uchun).</summary>
    [HttpPost("agents/fcm-token")]
    public async Task<IActionResult> UpdateFcmToken(CtiFcmTokenRequest req)
    {
        await db.CtiAgents.Where(a => a.Id == AgentId)
            .ExecuteUpdateAsync(up => up.SetProperty(a => a.FcmToken, (req.Token ?? "").Trim()));
        return Ok();
    }

    // ---------- Yordamchi ----------

    /// <summary>Raqamni o'quvchiga (yoki ota-onaga) moslaydi — TelephonyWebhookController uslubi.</summary>
    private async Task<string?> MatchStudentIdAsync(string? phone)
    {
        var normalized = PhoneUtil.Normalize(phone);
        var key = PhoneUtil.Key(phone);
        if (key.Length < 7) return null;
        return await db.Students
            .Where(s => s.Phone == normalized || s.ParentPhone == normalized ||
                        s.FatherPhone == normalized || s.MotherPhone == normalized)
            .Select(s => s.Id)
            .FirstOrDefaultAsync();
    }

    private static DateTime? ParseDate(string? s) =>
        !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(
            s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;
}
