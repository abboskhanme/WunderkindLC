using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Call Center — MoiZvonki (bulutli telefoniya) orqali chiquvchi qo'ng'iroq va qo'ng'iroqlar jurnali.
/// Oqim: POST originate → MoiZvonkiService (qo'ng'iroq operator telefonidan chiqadi) → Call yozuvi
/// "originating". Holat yangilanishi (ringing/answered/completed) — provayder webhook'i
/// (TelephonyWebhookController) + SignalR orqali keladi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("calls.cloud")]
[Route("api/admin/calls")]
public class CallsController(
    AppDbContext db, MoiZvonkiService moizvonki,
    MoiZvonkiCallSyncService callSync,
    IConfiguration config, IHttpClientFactory httpFactory) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>Faol telefoniya provayderi: MoiZvonki (sozlangan bo'lsa), aks holda modul o'chiq.</summary>
    private string Provider => moizvonki.IsConfigured ? "moizvonki" : "";

    /// <summary>Frontend uchun modul holati: telefoniya sozlanganmi + qaysi provayder.</summary>
    [HttpGet("config")]
    public ActionResult<object> Config() => Ok(new
    {
        configured = Provider.Length > 0,
        provider = Provider,
    });

    /// <summary>
    /// MoiZvonki webhook obunasini qo'lda ishga tushirish (diagnostika): bizning webhook URL
    /// provayderga ro'yxatdan o'tkaziladi, provayderning XOM javobi qaytariladi (sxema mos
    /// kelmasa shu yerda ko'rinadi). URL App:Host'dan quriladi. "Call Center" ruxsati (qo'shish
    /// amali) kerak; superadmin/admin har doim, xodim faqat shu ruxsat berilgan bo'lsa.
    /// </summary>
    [HttpPost("telephony/subscribe")]
    public async Task<ActionResult> SubscribeTelephonyWebhooks()
    {
        if (!moizvonki.IsConfigured)
            return StatusCode(503, new { message = "MoiZvonki sozlanmagan (MoiZvonki__Enabled/Domain/UserName/ApiKey)" });
        var host = config["App:Host"] ?? "";
        if (host.Length == 0)
            return BadRequest(new { message = "App:Host (APP_HOST env) sozlanmagan — webhook URL qurib bo'lmaydi" });
        var url = $"https://{host}/api/telephony/moizvonki/{moizvonki.WebhookSecret}";
        var (ok, body) = await moizvonki.SubscribeWebhooksAsync(url);
        return Ok(new { ok, url, providerResponse = body });
    }

    /// <summary>
    /// Chiquvchi qo'ng'iroq. Body: studentId YOKI phoneNumber (dialpad). Qo'ng'iroq operator
    /// telefonidan chiqadi (MoiZvonki). Qo'lda terilgan raqam o'quvchiga tegishli bo'lsa
    /// (telefonlari bo'yicha moslash) — tarix o'sha o'quvchiga bog'lanadi.
    /// </summary>
    [HttpPost("originate")]
    public async Task<ActionResult> Originate(OriginateCallRequest req)
    {
        if (Provider.Length == 0)
            return StatusCode(503, new { message = "Telefoniya sozlanmagan — MoiZvonki (MoiZvonki__Enabled/Domain/UserName/ApiKey) bering" });

        // 1) Raqam va (iloji bo'lsa) o'quvchini aniqlash.
        string phone;
        Student? student = null;

        if (!string.IsNullOrWhiteSpace(req.StudentId))
        {
            student = await db.Students.FindAsync(req.StudentId);
            if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
            var candidate = new[] { student.Phone, student.ParentPhone, student.FatherPhone, student.MotherPhone }
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (candidate is null)
                return BadRequest(new { message = "O'quvchida telefon raqam kiritilmagan" });
            phone = PhoneUtil.Normalize(candidate);
        }
        else
        {
            var (valid, normalized, error) = PhoneUtil.Validate(req.PhoneNumber);
            if (!valid) return BadRequest(new { message = error ?? "Telefon raqam noto'g'ri" });
            phone = normalized;
            // Qo'lda terilgan raqam bazadagi o'quvchiga tegishlimi? (tarix bog'lash uchun)
            student = await db.Students.FirstOrDefaultAsync(s =>
                s.Phone == phone || s.ParentPhone == phone || s.FatherPhone == phone || s.MotherPhone == phone);
        }

        // 2) Jurnal yozuvi + qo'ng'iroq buyrug'i (qo'ng'iroq akkaunt telefonidan chiqadi).
        var call = new Call
        {
            StudentId = student?.Id,
            OperatorUserId = Uid.Length > 0 ? Uid : null,
            PhoneNumber = phone,
            Direction = "outbound",
            Status = "originating",
        };
        db.Calls.Add(call);
        await db.SaveChangesAsync();

        var (ok, message) = await moizvonki.MakeCallAsync(new string(phone.Where(char.IsDigit).ToArray()));
        if (!ok)
        {
            call.Status = "failed";
            call.EndedAt = AppClock.Now;
            call.Note = message;
            await db.SaveChangesAsync();
            return StatusCode(502, new { message, callId = call.Id });
        }

        return Ok(new { callId = call.Id, status = call.Status, phoneNumber = phone, studentId = student?.Id });
    }

    /// <summary>
    /// Qo'ng'iroqlar tarixini provayderdan QO'LDA sinxronlash (calls.list) — "Yozuvlar tarixi"
    /// tabidagi "Yangilash" tugmasi. Davriy avto-sinxron baribir 5 daqiqada yuradi; bu darhol ko'rish uchun.
    /// </summary>
    [HttpPost("telephony/sync")]
    public async Task<ActionResult> SyncTelephonyHistory()
    {
        if (!moizvonki.IsConfigured)
            return StatusCode(503, new { message = "MoiZvonki sozlanmagan" });
        var (added, updated) = await callSync.SyncOnceAsync(HttpContext.RequestAborted);
        return Ok(new { added, updated });
    }

    /// <summary>Umumiy filtrlar: sana oralig'i (yyyy-MM-dd), yo'nalish, holat, aniq raqam.</summary>
    private static IQueryable<Call> ApplyFilters(
        IQueryable<Call> q, string? phone, string? dateFrom, string? dateTo, string? direction, string? status)
    {
        if (!string.IsNullOrWhiteSpace(phone))
            q = q.Where(c => c.PhoneNumber == phone);
        if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var df))
            q = q.Where(c => c.StartedAt >= df);
        if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var dt))
            q = q.Where(c => c.StartedAt < dt.AddDays(1)); // kun oxirigacha inklyuziv
        if (direction is "inbound" or "outbound")
            q = q.Where(c => c.Direction == direction);
        if (status == "answered")
            q = q.Where(c => c.AnsweredAt != null);
        else if (status == "missed")
            q = q.Where(c => c.AnsweredAt == null && c.EndedAt != null);
        return q;
    }

    /// <summary>Ism/raqam qidiruvi (raqam contains, ism — Students orqali).</summary>
    private async Task<IQueryable<Call>> ApplySearchAsync(IQueryable<Call> q, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return q;
        var s = search.Trim().ToLower();
        var sids = await db.Students
            .Where(x => x.FullName.ToLower().Contains(s))
            .Select(x => x.Id).ToListAsync();
        return q.Where(c => c.PhoneNumber.Contains(s) || (c.StudentId != null && sids.Contains(c.StudentId)));
    }

    /// <summary>Barcha qo'ng'iroqlar (eng oxirgisi tepada). <paramref name="phone"/> berilsa —
    /// faqat shu raqam bilan suhbatlar (guruh qatori ichini ochish uchun).</summary>
    [HttpGet]
    public async Task<ActionResult<CallListDto>> List(
        [FromQuery] string? search, [FromQuery] string? phone,
        [FromQuery] string? dateFrom, [FromQuery] string? dateTo,
        [FromQuery] string? direction, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = ApplyFilters(db.Calls.AsNoTracking(), phone, dateFrom, dateTo, direction, status);
        q = await ApplySearchAsync(q, search);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(c => c.StartedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new CallListDto(total, await ToDtosAsync(items));
    }

    /// <summary>
    /// RAQAM BO'YICHA guruhlangan tarix: har raqam bitta qator (nechta qo'ng'iroq, oxirgisi
    /// qachon/holati, jami gaplashuv). Qator ochilganda <see cref="List"/> phone= bilan chaqiriladi.
    /// </summary>
    [HttpGet("by-number")]
    public async Task<ActionResult<CallGroupListDto>> ByNumber(
        [FromQuery] string? search,
        [FromQuery] string? dateFrom, [FromQuery] string? dateTo,
        [FromQuery] string? direction, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = ApplyFilters(db.Calls.AsNoTracking(), null, dateFrom, dateTo, direction, status);
        q = await ApplySearchAsync(q, search);

        var grouped = q.GroupBy(c => c.PhoneNumber).Select(g => new
        {
            Phone = g.Key,
            Count = g.Count(),
            Answered = g.Count(c => c.AnsweredAt != null),
            TotalDuration = g.Sum(c => c.DurationSeconds),
            LastAt = g.Max(c => c.StartedAt),
        });

        var total = await grouped.CountAsync();
        var pageRows = await grouped.OrderByDescending(x => x.LastAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var phones = pageRows.Select(x => x.Phone).ToList();

        // Sahifadagi raqamlarning qo'ng'iroqlari — oxirgi holat + o'quvchi nomi uchun
        // (bitta so'rov, keyin xotirada eng so'nggisi olinadi).
        var meta = await db.Calls.AsNoTracking()
            .Where(c => phones.Contains(c.PhoneNumber))
            .Select(c => new { c.PhoneNumber, c.StartedAt, c.Status, c.Direction, c.StudentId })
            .ToListAsync();
        var lastByPhone = meta.GroupBy(m => m.PhoneNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.StartedAt).First());
        var studentByPhone = meta.Where(m => m.StudentId != null)
            .GroupBy(m => m.PhoneNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.StartedAt).First(m => m.StudentId != null).StudentId!);
        var studentIds = studentByPhone.Values.Distinct().ToList();
        var studentNames = await db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);

        var items = pageRows.Select(x =>
        {
            var last = lastByPhone.GetValueOrDefault(x.Phone);
            var sid = studentByPhone.GetValueOrDefault(x.Phone);
            return new CallGroupDto(
                x.Phone, sid,
                sid != null ? studentNames.GetValueOrDefault(sid, "") : "",
                x.Count, x.Answered, x.TotalDuration,
                x.LastAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                last?.Status ?? "", last?.Direction ?? "");
        }).ToList();

        return new CallGroupListDto(total, items);
    }

    /// <summary>Bitta qo'ng'iroqning TO'LIQ tafsiloti — detal oynasi (transkript + AI tahlil bilan).</summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<CallDetailDto>> Detail(string id)
    {
        var c = await db.Calls.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        var studentName = c.StudentId != null
            ? await db.Students.Where(s => s.Id == c.StudentId).Select(s => s.FullName).FirstOrDefaultAsync() ?? ""
            : "";
        var operatorName = c.OperatorUserId != null
            ? await db.Users.Where(u => u.Id == c.OperatorUserId).Select(u => u.FullName).FirstOrDefaultAsync() ?? ""
            : "";
        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        return new CallDetailDto(
            c.Id, c.StudentId, studentName, c.PhoneNumber, c.Direction, c.Status,
            Iso(c.StartedAt), c.AnsweredAt is { } a ? Iso(a) : null, c.EndedAt is { } e ? Iso(e) : null,
            c.DurationSeconds, operatorName, c.RecordingFile.Length > 0, c.Note,
            c.Transcript, c.AiAnalysis);
    }

    /// <summary>
    /// Suhbat yozuvini Azure (Fast Transcription) orqali SO'ZMA-SO'Z matnga o'girish —
    /// hech qanday moslashtirish/senzurasiz. Natija Call.Transcript'da saqlanadi (qayta
    /// bosilsa qayta transkript qilinadi). Kalit/region — Sozlamalar (CenterMeta, Speaking bilan bir xil).
    /// </summary>
    [HttpPost("{id}/transcribe")]
    public async Task<ActionResult> Transcribe(string id)
    {
        var call = await db.Calls.FindAsync(id);
        if (call is null) return NotFound();
        if (call.RecordingFile.Length == 0)
            return BadRequest(new { message = "Bu qo'ng'iroqda yozuv yo'q" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (!AzureTranscribeService.IsConfigured(AppSecrets.AzureSpeechKey, AppSecrets.AzureSpeechRegion))
            return StatusCode(503, new { message = "Azure Speech sozlanmagan (Sozlamalar → AI Check: kalit va region)" });

        var (audio, fileName, error) = await GetRecordingBytesAsync(call);
        if (audio is null)
            return BadRequest(new { message = error ?? "Yozuv faylini olib bo'lmadi" });

        var (ok, text, err) = await AzureTranscribeService.TranscribeAsync(
            audio, fileName, AppSecrets.AzureSpeechKey, AppSecrets.AzureSpeechRegion,
            config["Azure:TranscribeLocales"], HttpContext.RequestAborted);
        if (!ok) return StatusCode(502, new { message = err });

        call.Transcript = text;
        await db.SaveChangesAsync();
        return Ok(new { transcript = text });
    }

    /// <summary>
    /// Transkriptni Gemini AI bilan tahlil qilish: suhbat mazmuni, operator qaysi vaziyatda
    /// NIMA DEYISHI MUMKIN EDI (aniq tavsiya iboralar bilan), umumiy baho. Natija
    /// Call.AiAnalysis'da saqlanadi. Avval transkript bo'lishi shart.
    /// </summary>
    [HttpPost("{id}/analyze")]
    public async Task<ActionResult> Analyze(string id)
    {
        var call = await db.Calls.FindAsync(id);
        if (call is null) return NotFound();
        if (call.Transcript.Length == 0)
            return BadRequest(new { message = "Avval transkript qiling — tahlil transkript asosida ishlaydi" });

        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey))
            return StatusCode(503, new { message = "Gemini sozlanmagan (Sozlamalar → AI Tahlil: API kalit)" });

        var direction = call.Direction == "inbound" ? "KIRUVCHI (mijoz qo'ng'iroq qildi)" : "CHIQUVCHI (operator qo'ng'iroq qildi)";
        var prompt =
            "Siz o'quv markazi call-markazining sifat nazoratchisisiz. Quyida operator va mijoz " +
            $"o'rtasidagi telefon suhbatining so'zma-so'z transkripti berilgan ({direction}, " +
            $"davomiyligi {call.DurationSeconds} soniya). Tahlilni O'ZBEK tilida, aniq va amaliy yozing:\n\n" +
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

    /// <summary>Yozuv faylining to'liq baytlari: provayder (MoiZvonki) URL'idan oqim qilib olinadi.</summary>
    private async Task<(byte[]? Audio, string FileName, string? Error)> GetRecordingBytesAsync(Call call)
    {
        if (!call.RecordingFile.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, "", "Yozuv mavjud emas");

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(120);
            var resp = await http.GetAsync(call.RecordingFile);
            if (!resp.IsSuccessStatusCode)
                return (null, "", $"Provayderdan yozuvni olib bo'lmadi (HTTP {(int)resp.StatusCode})");
            var ctType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (ctType.StartsWith("text/") || ctType.Contains("html"))
                return (null, "", "Provayder yozuv o'rniga sahifa qaytardi");
            var name = Path.GetFileName(new Uri(call.RecordingFile).AbsolutePath);
            if (string.IsNullOrWhiteSpace(name) || !name.Contains('.')) name = "recording.mp3";
            return (await resp.Content.ReadAsByteArrayAsync(), name, null);
        }
        catch (Exception ex)
        {
            return (null, "", $"Yozuvni yuklab bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>Bitta o'quvchining qo'ng'iroqlar tarixi (detalli oynadagi tab).</summary>
    [HttpGet("student/{studentId}")]
    public async Task<ActionResult<List<CallDto>>> ByStudent(string studentId)
    {
        var items = await db.Calls.AsNoTracking()
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.StartedAt)
            .Take(200).ToListAsync();
        return await ToDtosAsync(items);
    }

    /// <summary>
    /// Suhbat yozuvini eshittirish. DIQQAT: yozuvlar ATAYIN /uploads (ochiq statik) ostida EMAS —
    /// faqat shu autentifikatsiyalangan endpoint orqali beriladi. Yozuv provayder (MoiZvonki)
    /// URL'idan proxy qilinadi (havola brauzerga oshkor bo'lmaydi).
    /// </summary>
    [HttpGet("{id}/recording")]
    public async Task<IActionResult> Recording(string id)
    {
        var call = await db.Calls.FindAsync(id);
        if (call is null) return NotFound();

        // MoiZvonki: yozuv to'liq URL bo'lib keladi — provayderdan oqim qilib uzatamiz
        // (proxy: havola brauzerga oshkor bo'lmaydi, auth bizning tomonda qoladi).
        if (call.RecordingFile.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var resp = await http.GetAsync(call.RecordingFile, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
                return NotFound(new { message = $"Yozuvni provayderdan olib bo'lmadi (HTTP {(int)resp.StatusCode})" });
            var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
            // Provayder audio o'rniga sahifa/xato qaytarsa (himoyalangan havola) — aniq xabar,
            // pleyerga HTML berib "jim ishlamaslik" holatiga tushmaymiz.
            if (mediaType.StartsWith("text/") || mediaType.Contains("html"))
                return NotFound(new { message = "Provayder yozuv o'rniga sahifa qaytardi — havola himoyalangan bo'lishi mumkin" });
            return File(await resp.Content.ReadAsStreamAsync(), mediaType);
        }

        return NotFound(new { message = "Yozuv mavjud emas" });
    }

    private async Task<List<CallDto>> ToDtosAsync(List<Call> items)
    {
        var studentIds = items.Where(c => c.StudentId != null).Select(c => c.StudentId!).Distinct().ToList();
        var operatorIds = items.Where(c => c.OperatorUserId != null).Select(c => c.OperatorUserId!).Distinct().ToList();
        var studentNames = await db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);
        var operatorNames = await db.Users.Where(u => operatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        return items.Select(c => new CallDto(
            c.Id, c.StudentId,
            c.StudentId != null ? studentNames.GetValueOrDefault(c.StudentId, "") : "",
            c.PhoneNumber, c.Direction, c.Status,
            Iso(c.StartedAt),
            c.AnsweredAt is { } a ? Iso(a) : null,
            c.EndedAt is { } e ? Iso(e) : null,
            c.DurationSeconds,
            c.OperatorUserId != null ? operatorNames.GetValueOrDefault(c.OperatorUserId, "") : "",
            c.RecordingFile.Length > 0,
            c.Note)).ToList();
    }
}
