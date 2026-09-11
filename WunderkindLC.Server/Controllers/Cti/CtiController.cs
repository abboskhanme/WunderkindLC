using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers.Cti;

/// <summary>
/// CTI (Local Call) OPERATOR API — admin panel (React) uchun. Agentlarni boshqaradi, tarixni
/// ko'radi, click-to-call qiladi VA agent telefonining SIM-kartasidan ixtiyoriy SMS yuboradi
/// (ikkalasi ham WebSocket, oflaynda FCM+poll). Ruxsat: <see cref="AdminPermAttribute"/>
/// "calls.local" (Call Center bo'limining "Local Call" sahifasi).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("calls.local")]
[Route("api/cti")]
public class CtiController(
    AppDbContext db, CtiConnectionManager conn, FcmService fcm, CtiSmsService ctiSms,
    IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    // ---------- Ruxsat: har xodim FAQAT O'ZIGA biriktirilgan agent(lar)ni ko'radi/eshitadi;
    // faqat SuperAdmin (va tarixiy sabab bilan Admin) hammasini ko'radi. ----------

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
    /// <summary>Faqat SuperAdmin BARCHA agentlarni ko'radi — foydalanuvchi talabi bo'yicha ATAYIN
    /// faqat shu rol (Admin roli amalda ishlatilmaydi, lekin xavfsizlik uchun bu yerga qo'shilmagan).</summary>
    private bool SeesAll => User.IsInRole(Roles.SuperAdmin);

    /// <summary>Joriy xodimga biriktirilgan agent id'lari (SuperAdmin uchun chaqirilmaydi).</summary>
    private Task<List<string>> MyAgentIdsAsync() =>
        db.CtiAgents.Where(a => a.StaffUserId == Uid).Select(a => a.Id).ToListAsync();

    /// <summary>Berilgan agent joriy xodimga tegishlimi (yoki SuperAdmin)? Ownership tekshiruvi
    /// uchun umumiy yordamchi — Dial/SMS/tahrirlash kabi YOZISH amallarida ishlatiladi.</summary>
    private async Task<bool> OwnsAgentAsync(string agentId)
    {
        if (SeesAll) return true;
        var owner = await db.CtiAgents.Where(a => a.Id == agentId).Select(a => a.StaffUserId).FirstOrDefaultAsync();
        return owner is not null && owner == Uid;
    }

    // ---------- Agentlar ----------

    /// <summary>Agentlar ro'yxati — SuperAdmin BARCHASINI, oddiy xodim FAQAT o'ziga biriktirilganlarini
    /// ko'radi (<c>isOnline</c> JONLI, konnektsiya menejeridan, DB emas).</summary>
    [HttpGet("agents")]
    public async Task<ActionResult<List<CtiAgentDto>>> Agents()
    {
        var q = db.CtiAgents.AsNoTracking();
        if (!SeesAll) q = q.Where(a => a.StaffUserId == Uid);
        var agents = await q.OrderBy(a => a.DisplayName).ToListAsync();

        var staffIds = agents.Where(a => a.StaffUserId != null).Select(a => a.StaffUserId!).Distinct().ToList();
        var staffNames = staffIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Users.Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        return agents.Select(a => new CtiAgentDto(
            a.Id, a.Login, a.DisplayName, a.IsActive,
            conn.IsConnected(a.Id),
            a.LastSeenAt is { } t ? t.ToString("yyyy-MM-ddTHH:mm:ss") : null,
            a.FcmToken.Length > 0,
            a.StaffUserId, a.StaffUserId != null ? staffNames.GetValueOrDefault(a.StaffUserId, "") : "")).ToList();
    }

    /// <summary>Yangi agent yaratadi (login band bo'lmasligi tekshiriladi). SuperAdmin xohlagan xodimga
    /// biriktirishi mumkin (yoki bo'sh qoldirishi — hech kimga ko'rinmaydi, faqat SuperAdmin'ga); oddiy
    /// xodim yaratsa — har doim O'ZIGA biriktiriladi (boshqa xodimga berolmaydi).</summary>
    [HttpPost("agents")]
    public async Task<ActionResult> CreateAgent(CtiAgentCreateRequest req)
    {
        var login = (req.Login ?? "").Trim();
        if (login.Length == 0 || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Login va parol majburiy" });
        if (await db.CtiAgents.AnyAsync(a => a.Login == login))
            return BadRequest(new { message = "Bu login band" });

        var agent = new CtiAgent
        {
            Login = login,
            PasswordHash = PasswordHasher.Hash(req.Password),
            DisplayName = (req.DisplayName ?? "").Trim(),
            StaffUserId = SeesAll ? (string.IsNullOrWhiteSpace(req.StaffUserId) ? null : req.StaffUserId) : Uid,
        };
        db.CtiAgents.Add(agent);
        await db.SaveChangesAsync();
        return Ok(new { id = agent.Id });
    }

    /// <summary>Agentni tahrirlaydi (parol bo'sh/null bo'lsa o'zgarmaydi) — faqat SuperAdmin yoki shu
    /// agentning egasi. Xodimga biriktirishni faqat SuperAdmin o'zgartira oladi.</summary>
    [HttpPut("agents/{id}")]
    public async Task<ActionResult> UpdateAgent(string id, CtiAgentUpdateRequest req)
    {
        var agent = await db.CtiAgents.FirstOrDefaultAsync(a => a.Id == id);
        if (agent is null) return NotFound();
        if (!SeesAll && agent.StaffUserId != Uid) return Forbid();

        agent.DisplayName = (req.DisplayName ?? "").Trim();
        agent.IsActive = req.IsActive;
        if (!string.IsNullOrWhiteSpace(req.Password))
            agent.PasswordHash = PasswordHasher.Hash(req.Password);
        if (SeesAll)
            agent.StaffUserId = string.IsNullOrWhiteSpace(req.StaffUserId) ? null : req.StaffUserId;
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Click-to-call: agent telefoniga <c>dial</c> buyrug'ini yuboradi. WS ulangan bo'lsa darhol;
    /// aks holda FCM data-push bilan uyg'otib, ~6 soniya WS ulanishini kutadi (500ms interval).
    /// </summary>
    [HttpPost("agents/{id}/dial")]
    public async Task<ActionResult<CtiDialResponse>> Dial(string id, CtiDialRequest req)
    {
        var agent = await db.CtiAgents.FirstOrDefaultAsync(a => a.Id == id);
        if (agent is null) return NotFound();
        if (!SeesAll && agent.StaffUserId != Uid) return Forbid();
        var number = NormalizePhone(req.Number ?? "");
        if (number.Length == 0) return BadRequest(new { message = "Raqam bo'sh" });

        var commandId = Guid.NewGuid().ToString();
        var cmd = new CtiCommandLog { AgentId = id, Action = "dial", Payload = number, Status = "pending" };
        db.CtiCommandLogs.Add(cmd);
        await db.SaveChangesAsync();

        object DialMsg() => new { action = "dial", number, commandId };

        // 1) WS ulangan — darhol yuboramiz.
        if (conn.IsConnected(id) && await conn.SendAsync(id, DialMsg()))
        {
            cmd.Status = "sent";
            await db.SaveChangesAsync();
            return new CtiDialResponse(commandId, true);
        }

        // 2) Oflayn — FCM bilan uyg'otamiz, so'ng WS ulanishini poll qilamiz.
        if (agent.FcmToken.Length > 0)
        {
            var meta = await db.CenterMeta.FirstOrDefaultAsync();
            var json = AppSecrets.FcmServiceAccountJson;
            await fcm.SendDataAsync(json, agent.FcmToken, new Dictionary<string, string>
            {
                ["action"] = "dial",
                ["number"] = number,
                ["commandId"] = commandId,
            }, HttpContext.RequestAborted);

            // ~6 soniya: ilova uyg'onib WS ulansa dial yuboramiz.
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(500, HttpContext.RequestAborted);
                if (conn.IsConnected(id) && await conn.SendAsync(id, DialMsg()))
                {
                    cmd.Status = "sent";
                    await db.SaveChangesAsync();
                    return new CtiDialResponse(commandId, true);
                }
            }
        }

        cmd.Status = "failed";
        await db.SaveChangesAsync();
        return new CtiDialResponse(commandId, false);
    }

    /// <summary>
    /// Agent telefonining SIM-kartasidan ixtiyoriy matnli SMS yuborish: <c>send_sms</c> buyrug'i.
    /// Yetkazish oqimi <see cref="Dial"/> bilan bir xil (WS ulangan bo'lsa darhol, aks holda FCM
    /// data-push bilan uyg'otib ~6 soniya kutadi) — <see cref="CtiSmsService"/>ga delegatsiya qilinadi,
    /// shu bilan bu yerdan yuborilgan SMS ham umumiy SMS Tarix (SmsLog, Provider=local)da ko'rinadi.
    /// </summary>
    [HttpPost("agents/{id}/sms")]
    public async Task<ActionResult<CtiSmsResponse>> SendSms(string id, CtiSmsRequest req)
    {
        var agent = await db.CtiAgents.FindAsync(id);
        if (agent is null) return NotFound();
        if (!SeesAll && agent.StaffUserId != Uid) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Number)) return BadRequest(new { message = "Raqam bo'sh" });
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "SMS matni bo'sh" });
        if (text.Length > 1000) return BadRequest(new { message = "SMS matni juda uzun (max 1000 belgi)" });

        var r = await ctiSms.SendSmsAsync(db, id, req.Number, text, ct: HttpContext.RequestAborted);
        return new CtiSmsResponse(r.CommandId, r.Ok);
    }

    // ---------- Qo'ng'iroqlar tarixi ----------

    /// <summary>CTI qo'ng'iroqlar tarixi — filtr (agent/yo'nalish/sana/qidiruv/aniq raqam), sahifalash.</summary>
    [HttpGet("calls")]
    public async Task<ActionResult<CtiCallListDto>> Calls(
        [FromQuery] string? agentId, [FromQuery] string? direction,
        [FromQuery] string? dateFrom, [FromQuery] string? dateTo,
        [FromQuery] string? search, [FromQuery] string? number,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = await FilteredCallsAsync(agentId, direction, dateFrom, dateTo, search, number);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(c => c.StartedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var (agentNames, studentNames) = await LookupNamesAsync(items);
        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        var dtos = items.Select(c => new CtiCallDto(
            c.Id, c.AgentId, agentNames.GetValueOrDefault(c.AgentId, ""),
            c.Direction, c.RemoteNumber, c.ContactName,
            c.StudentId, c.StudentId != null ? studentNames.GetValueOrDefault(c.StudentId, "") : "",
            Iso(c.StartedAt), c.AnsweredAt is { } a ? Iso(a) : null, c.EndedAt is { } e ? Iso(e) : null,
            c.DurationSec, c.AudioUploaded && c.AudioPath.Length > 0, c.Note)).ToList();

        return new CtiCallListDto(total, dtos);
    }

    /// <summary>
    /// Qo'ng'iroqlar RAQAM bo'yicha guruhlangan tarixi — har raqam BITTA qator: jami/o'tkazib
    /// yuborilgan soni + oxirgi qo'ng'iroq ma'lumotlari. Filtrlar oddiy tarix bilan bir xil.
    /// Raqam bosilganda uning barcha qo'ng'iroqlari <c>GET calls?number=...</c> bilan olinadi.
    /// </summary>
    [HttpGet("calls/grouped")]
    public async Task<ActionResult<CtiNumberGroupListDto>> CallsGrouped(
        [FromQuery] string? agentId, [FromQuery] string? direction,
        [FromQuery] string? dateFrom, [FromQuery] string? dateTo,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = await FilteredCallsAsync(agentId, direction, dateFrom, dateTo, search, number: null);

        // 1) Raqam bo'yicha agregatlar (oxirgi qo'ng'iroq vaqti bo'yicha kamayish tartibida sahifalanadi).
        var total = await q.Select(c => c.RemoteNumber).Distinct().CountAsync();
        var groups = await q.GroupBy(c => c.RemoteNumber)
            .Select(g => new
            {
                Number = g.Key,
                Count = g.Count(),
                Missed = g.Count(c => c.Direction == "missed"),
                HasAudio = g.Any(c => c.AudioUploaded && c.AudioPath.Length > 0),
                LastAt = g.Max(c => c.StartedAt),
            })
            .OrderByDescending(x => x.LastAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // 2) Sahifadagi raqamlarning OXIRGI qo'ng'iroq satri (yo'nalish/agent/o'quvchi nomi uchun).
        var numbers = groups.Select(x => x.Number).ToList();
        var lastCalls = await q.Where(c => numbers.Contains(c.RemoteNumber))
            .GroupBy(c => c.RemoteNumber)
            .Select(g => g.OrderByDescending(c => c.StartedAt).First())
            .ToListAsync();
        var lastByNumber = lastCalls.ToDictionary(c => c.RemoteNumber);
        var (agentNames, studentNames) = await LookupNamesAsync(lastCalls);

        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        var items = groups.Select(x =>
        {
            var last = lastByNumber.GetValueOrDefault(x.Number);
            return new CtiNumberGroupDto(
                x.Number,
                last?.ContactName ?? "",
                last?.StudentId,
                last?.StudentId != null ? studentNames.GetValueOrDefault(last.StudentId, "") : "",
                x.Count, x.Missed, x.HasAudio,
                Iso(x.LastAt),
                last?.Direction ?? "outgoing",
                last?.DurationSec ?? 0,
                last != null ? agentNames.GetValueOrDefault(last.AgentId, "") : "");
        }).ToList();

        return new CtiNumberGroupListDto(total, items);
    }

    /// <summary>Bitta CTI qo'ng'irog'ining to'liq tafsiloti (hodisalar bilan).</summary>
    [HttpGet("calls/{id}")]
    public async Task<ActionResult<CtiCallDetailDto>> CallDetail(string id)
    {
        var c = await db.CtiCallRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (!await OwnsAgentAsync(c.AgentId)) return Forbid();

        var agentName = await db.CtiAgents.Where(a => a.Id == c.AgentId)
            .Select(a => a.DisplayName).FirstOrDefaultAsync() ?? "";
        var studentName = c.StudentId != null
            ? await db.Students.Where(s => s.Id == c.StudentId).Select(s => s.FullName).FirstOrDefaultAsync() ?? ""
            : "";
        var events = await db.CtiCallEvents.AsNoTracking()
            .Where(e => e.CallId == id).OrderBy(e => e.At).ToListAsync();

        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        return new CtiCallDetailDto(
            c.Id, c.AgentId, agentName, c.Direction, c.RemoteNumber, c.ContactName,
            c.StudentId, studentName,
            Iso(c.StartedAt), c.AnsweredAt is { } a ? Iso(a) : null, c.EndedAt is { } e ? Iso(e) : null,
            c.DurationSec, c.AudioUploaded && c.AudioPath.Length > 0, c.Note,
            events.Select(ev => new CtiCallEventDto(ev.Type, Iso(ev.At))).ToList(),
            c.Transcript, c.AiAnalysis);
    }

    /// <summary>Qo'ng'iroq audio yozuvini eshittiradi (recordings papkasidan, range qo'llab-quvvatlanadi).
    /// Path-traversal himoyasi: fayl faqat papka ICHIDAN beriladi.</summary>
    [HttpGet("calls/{id}/audio")]
    public async Task<IActionResult> Audio(string id)
    {
        var c = await db.CtiCallRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null || !c.AudioUploaded || c.AudioPath.Length == 0)
            return NotFound(new { message = "Yozuv mavjud emas" });
        if (!await OwnsAgentAsync(c.AgentId)) return Forbid();

        var dir = RecordingsDir();
        var fileName = Path.GetFileName(c.AudioPath); // faqat nom — path-traversal yo'q
        var full = Path.GetFullPath(Path.Combine(dir, fileName));
        // Fayl AYNAN papka ichida ekanini tasdiqlaymiz.
        if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !System.IO.File.Exists(full))
            return NotFound(new { message = "Yozuv fayli topilmadi" });

        return PhysicalFile(full, ContentType(Path.GetExtension(full)), enableRangeProcessing: true);
    }

    /// <summary>Operator izohini yangilaydi.</summary>
    [HttpPut("calls/{id}/note")]
    public async Task<IActionResult> UpdateNote(string id, CtiNoteRequest req)
    {
        var agentId = await db.CtiCallRecords.Where(c => c.Id == id).Select(c => c.AgentId).FirstOrDefaultAsync();
        if (agentId is null) return NotFound();
        if (!await OwnsAgentAsync(agentId)) return Forbid();

        var affected = await db.CtiCallRecords.Where(c => c.Id == id)
            .ExecuteUpdateAsync(up => up.SetProperty(c => c.Note, (req.Note ?? "").Trim()));
        return affected > 0 ? Ok() : NotFound();
    }

    /// <summary>
    /// Local Call yozuvini Azure Speech (diarizatsiya) orqali so'zma-so'z matnga o'giradi — so'zlovchilar
    /// AJRATILGAN ("1-suhbatdosh: ...", "2-suhbatdosh: ..."). Natija Transcript'da saqlanadi (qayta bosilsa
    /// qayta transkript qilinadi). Kalit/region — Sozlamalar (CenterMeta, Speaking bilan bir xil).
    /// </summary>
    [HttpPost("calls/{id}/transcribe")]
    public async Task<ActionResult> Transcribe(string id)
    {
        var call = await db.CtiCallRecords.FindAsync(id);
        if (call is null) return NotFound();
        if (!await OwnsAgentAsync(call.AgentId)) return Forbid();
        if (!call.AudioUploaded || call.AudioPath.Length == 0)
            return BadRequest(new { message = "Bu qo'ng'iroqda yozuv yo'q" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (!AzureTranscribeService.IsConfigured(AppSecrets.AzureSpeechKey, AppSecrets.AzureSpeechRegion))
            return StatusCode(503, new { message = "Azure Speech sozlanmagan (Sozlamalar → AI Check: kalit va region)" });

        var (audio, fileName, error) = ReadLocalAudio(call);
        if (audio is null) return BadRequest(new { message = error ?? "Yozuv faylini olib bo'lmadi" });

        var (ok, text, err) = await AzureTranscribeService.TranscribeAsync(
            audio, fileName, AppSecrets.AzureSpeechKey, AppSecrets.AzureSpeechRegion,
            config["Azure:TranscribeLocales"], HttpContext.RequestAborted);
        if (!ok) return StatusCode(502, new { message = err });

        call.Transcript = text;
        await db.SaveChangesAsync();
        return Ok(new { transcript = text });
    }

    /// <summary>
    /// Transkriptni Gemini AI bilan tahlil qilish: suhbat mazmuni, operator qaysi vaziyatda NIMA DEYISHI
    /// MUMKIN EDI (tavsiya iboralar bilan), umumiy baho. Natija AiAnalysis'da saqlanadi. Avval transkript shart.
    /// </summary>
    [HttpPost("calls/{id}/analyze")]
    public async Task<ActionResult> Analyze(string id)
    {
        var call = await db.CtiCallRecords.FindAsync(id);
        if (call is null) return NotFound();
        if (!await OwnsAgentAsync(call.AgentId)) return Forbid();
        if (call.Transcript.Length == 0)
            return BadRequest(new { message = "Avval transkript qiling — tahlil transkript asosida ishlaydi" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return StatusCode(503, new { message = "Gemini sozlanmagan (Sozlamalar → AI Tahlil: API kalit)" });

        var direction = call.Direction == "incoming" ? "KIRUVCHI (mijoz qo'ng'iroq qildi)" : "CHIQUVCHI (operator qo'ng'iroq qildi)";
        var prompt =
            "Siz o'quv markazi call-markazining sifat nazoratchisisiz. Quyida operator va mijoz " +
            $"o'rtasidagi telefon suhbatining so'zma-so'z transkripti berilgan ({direction}, " +
            $"davomiyligi {call.DurationSec} soniya; so'zlovchilar 1-suhbatdosh/2-suhbatdosh deb ajratilgan). " +
            "Tahlilni O'ZBEK tilida, aniq va amaliy yozing:\n\n" +
            "1. SUHBAT MAZMUNI — 2-3 jumlada nima haqida gaplashildi.\n" +
            "2. YAXSHI JIHATLAR — operator to'g'ri qilgan narsalar.\n" +
            "3. NIMA DEYISH MUMKIN EDI — qaysi vaziyatda (transkriptdan aynan joyini keltirib) " +
            "operator boshqacha/yaxshiroq nima deyishi mumkin edi; har biriga TAYYOR tavsiya ibora yozing.\n" +
            "4. UMUMIY BAHO — 10 ballik baho va bitta asosiy xulosa/tavsiya.\n\n" +
            "TRANSKRIPT:\n" + call.Transcript;

        var model = GeminiService.ResolveModel(config);
        var (ok, text, err) = await GeminiService.GenerateAsync(AppSecrets.GeminiApiKey, model, prompt);
        if (!ok) return StatusCode(502, new { message = err ?? "Gemini javob bermadi" });

        call.AiAnalysis = text.Trim();
        await db.SaveChangesAsync();
        return Ok(new { analysis = call.AiAnalysis });
    }

    /// <summary>Yozuv faylining baytlari (recordings/cti papkasidan; path-traversal himoyasi bilan).</summary>
    private (byte[]? Audio, string FileName, string? Error) ReadLocalAudio(CtiCallRecord call)
    {
        try
        {
            var dir = RecordingsDir();
            var fileName = Path.GetFileName(call.AudioPath); // faqat nom — path-traversal yo'q
            var full = Path.GetFullPath(Path.Combine(dir, fileName));
            if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !System.IO.File.Exists(full))
                return (null, "", "Yozuv fayli topilmadi");
            return (System.IO.File.ReadAllBytes(full), fileName, null);
        }
        catch (Exception ex)
        {
            return (null, "", $"Yozuvni o'qib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>
    /// Berilgan raqamga yuborilgan SMS'lar (Eskiz + Local, ikkalasi ham) — Local Call raqam tarixida
    /// qo'ng'iroqlar bilan BIRGA (bitta vaqt chizig'ida) ko'rsatish uchun. Raqamlar turli formatda
    /// saqlanishi mumkinligi uchun OXIRGI 9 RAQAM bo'yicha moslashtiriladi (<see cref="PhoneUtil.Key"/>).
    /// </summary>
    [HttpGet("sms")]
    public async Task<ActionResult<List<CtiSmsHistoryDto>>> SmsForNumber([FromQuery] string number)
    {
        var key = PhoneUtil.Key(number);
        if (key.Length == 0) return new List<CtiSmsHistoryDto>();

        var logs = await db.SmsLogs.AsNoTracking()
            .Where(l => l.PhoneNumber.EndsWith(key))
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .ToListAsync();

        return logs.Select(l => new CtiSmsHistoryDto(
            l.Id, l.Message, l.Status, l.Provider, l.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"))).ToList();
    }

    // ---------- Yordamchi ----------

    /// <summary>Terilayotgan raqamni xalqaro formatga keltiradi: O'zbekiston uchun <c>+998</c> kodi bilan
    /// (ilova qo'ng'iroq qilishi uchun oldida <c>+</c> shart). 9-xonali lokal raqam → <c>+998XXXXXXXXX</c>;
    /// <c>998</c> bilan boshlansa yoki boshqa formatda — shunchaki <c>+</c> qo'shiladi.</summary>
    private static string NormalizePhone(string raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return "";
        if (digits.StartsWith("998")) return "+" + digits;
        if (digits.Length == 10 && digits.StartsWith("0")) digits = digits[1..];
        if (digits.Length == 9) return "+998" + digits;
        return "+" + digits;
    }

    /// <summary>Tarix so'rovlarining UMUMIY filtri (oddiy va guruhlangan ro'yxat bir xil ishlaydi).
    /// <paramref name="number"/> — aniq raqam (guruh ichini ochish uchun). SuperAdmin bo'lmasa —
    /// FAQAT joriy xodimga biriktirilgan agent(lar)ning qo'ng'iroqlari qaytariladi.</summary>
    private async Task<IQueryable<CtiCallRecord>> FilteredCallsAsync(
        string? agentId, string? direction, string? dateFrom, string? dateTo, string? search, string? number)
    {
        var q = db.CtiCallRecords.AsNoTracking();
        if (!SeesAll)
        {
            var myAgentIds = await MyAgentIdsAsync();
            q = q.Where(c => myAgentIds.Contains(c.AgentId));
        }
        if (!string.IsNullOrWhiteSpace(agentId)) q = q.Where(c => c.AgentId == agentId);
        if (direction is "incoming" or "outgoing" or "missed") q = q.Where(c => c.Direction == direction);
        if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var df))
            q = q.Where(c => c.StartedAt >= df);
        if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var dt))
            q = q.Where(c => c.StartedAt < dt.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.RemoteNumber.Contains(s) || c.ContactName.Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(number))
        {
            var n = number.Trim();
            q = q.Where(c => c.RemoteNumber == n);
        }
        return q;
    }

    private string RecordingsDir()
    {
        var configured = config["Cti:RecordingsPath"];
        var dir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "recordings", "cti")
            : configured;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<(Dictionary<string, string> Agents, Dictionary<string, string> Students)>
        LookupNamesAsync(List<CtiCallRecord> items)
    {
        var agentIds = items.Select(c => c.AgentId).Distinct().ToList();
        var studentIds = items.Where(c => c.StudentId != null).Select(c => c.StudentId!).Distinct().ToList();
        var agents = await db.CtiAgents.Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName);
        var students = await db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);
        return (agents, students);
    }

    private static string ContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" or ".aac" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".amr" => "audio/amr",
        _ => "audio/wav",
    };
}
