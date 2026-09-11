using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// "BOG'LANISH KERAK" — o'quvchi bilan bog'lanish NAVBATI (follow-up).
///
/// <para>Oqim: o'quvchi profilidagi "⋮" → "Bog'lanish kerak" → SABAB tanlanadi → o'quvchi navbatga
/// tushadi. Operator navbatdan bog'lanadi, natijani va "javobi nima dedi"ni yozadi, so'ng keyingi
/// qadamni tanlaydi: hal bo'ldi / qayta qo'ng'iroq (sana bilan) / bog'lanib bo'lmadi.
/// Har bir amal <see cref="ContactAttempt"/> ga yoziladi — hisobotlar AYNAN shundan hisoblanadi.</para>
///
/// <para>Bosqich/natija kalitlari — <see cref="ContactService"/> (yagona katalog).</para>
///
/// <para>RUXSAT: <c>contacts</c> — o'quvchi ma'lumotidan ALOHIDA. Sabab: navbat bilan ishlaydigan
/// operatorga o'quvchilar bo'limini to'liq ochish shart emas ("Kassa" ruxsati "Moliya"dan alohida
/// bo'lgani bilan bir xil mantiq). Javobda o'quvchi ismi va TELEFONI qaytadi, shuning uchun
/// <c>ReadRequiresPerm = true</c> — o'qish ham darvozalangan.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("contacts", ReadRequiresPerm = true)]
[Route("api/admin/contacts")]
public class ContactsController(
    AppDbContext db, AuditService audit, ContactQueueService queue, IConfiguration config) : ControllerBase
{
    private string Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
    private string ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("sub")?.Value ?? "";
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    /// <summary>Audit tur nomi — <see cref="AuditSections"/> da "Bog'lanish kerak" bo'limiga tushadi.</summary>
    private const string AuditEntity = ContactQueueService.AuditEntity;

    /// <summary>
    /// HISOBOT tomoni (stats · journal · responses · ai-*) FAQAT superadminga ochiq.
    ///
    /// <para>⚠️ <c>[AdminPerm]</c> bu yerda YARAMAYDI: u to'liq huquqli rollarni cheklovsiz
    /// o'tkazib yuboradi (<c>AdminPermAttribute</c>: "to'liq huquqli rollar — cheklovsiz"),
    /// ya'ni oddiy <c>admin</c> ham kirib qolardi. Shuning uchun HARD rol tekshiruvi —
    /// «Aktiv muzlatish» dagi <c>MaySetYearFreeze</c> bilan bir xil naqsh
    /// (<c>.claude/rules/year-freeze.md</c> §3).</para>
    ///
    /// <para>NAVBAT tomoni esa avvalgidek <c>contacts</c> ruxsati bilan ochiq: operator/admin
    /// ishlayveradi, faqat hisobotni ko'ra olmaydi.</para>
    /// </summary>
    private bool MaySeeReports => User.IsInRole(Roles.SuperAdmin);

    /// <summary>Hisobot rad javobi — sabab bilan (jim 404/bo'sh ro'yxat emas).</summary>
    private ObjectResult ReportsForbidden() =>
        StatusCode(403, new { message = "Bog'lanish hisoboti faqat superadmin uchun" });

    /// <summary>Bir amalda navbatga qo'shiladigan eng ko'p o'quvchi ("hammasini tanlash"
    /// bosilsa ham so'rov cheksiz o'smasin).</summary>
    private const int MaxBulk = ContactQueueService.MaxBulk;

    /* =========================================================================================
     *  KATALOG + SANOQLAR
     * ====================================================================================== */

    /// <summary>
    /// Bosqich/natija katalogi va navbat sanoqlari — sahifa BITTA so'rovda ochilsin.
    /// Sanoqlar har doim JORIY holat bo'yicha (davr filtri hisobotga tegishli, navbatga emas).
    /// </summary>
    /// <param name="month">Kunlik reja ko'rsatiladigan OY ("yyyy-MM"). Bo'sh — joriy oy.
    /// Chiplar/sanoqlar oyga BOG'LIQ EMAS — ular har doim joriy holatni bildiradi.</param>
    [HttpGet("meta")]
    public async Task<ActionResult<ContactMetaDto>> Meta([FromQuery] string? month = null)
    {
        var counts = (await db.ContactRequests.AsNoTracking()
                .GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Status, x => x.Count);

        var today = Today;

        // MUDDAT kesimi — bitta guruhli so'rovdan. Qaysi kunga nechta qayta qo'ng'iroq
        // rejalashtirilgani ham shu yerdan chiqadi (alohida so'rov kerak emas).
        var byDay = await db.ContactRequests.AsNoTracking()
            .Where(c => c.Status == ContactStatuses.Callback)
            .GroupBy(c => c.DueDate)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var newCount = counts.GetValueOrDefault(ContactStatuses.New);
        var due = new int[7];   // indeks: 0 todo, 1 overdue, 2 today, 3 tomorrow, 4 week, 5 later, 6 nodate
        foreach (var row in byDay)
        {
            // Guruhni YAGONA qoidadan olamiz (ContactService) — UI va API bir xil sanasin.
            var bucket = ContactService.BucketOf(ContactStatuses.Callback, row.Date, today);
            var idx = bucket switch
            {
                ContactService.Due.Overdue => 1,
                ContactService.Due.Today => 2,
                ContactService.Due.Tomorrow => 3,
                ContactService.Due.Week => 4,
                ContactService.Due.Later => 5,
                _ => 6,
            };
            due[idx] += row.Count;
            if (ContactService.IsTodo(bucket)) due[0] += row.Count;
        }
        // "Bog'lanish kerak" (sanasiz) — ular ham bugungi ishga kiradi.
        due[6] += newCount;
        due[0] += newCount;

        // OYLIK REJA — tanlangan oyning ish BOR kunlari (klient qolgan kunlarni 0 bilan to'ldiradi).
        // Ilgari "bugundan 14 kun" edi; endi kalendar bir oylik bo'lgani uchun chegara ham OY.
        var m = (month ?? "").Trim();
        if (m.Length != 7 || !int.TryParse(m[..4], out _) || !int.TryParse(m[5..7], out _))
            m = today[..7];
        var monthStart = $"{m}-01";
        var monthEnd = $"{m}-31";   // satr solishtiruvi uchun yetarli (kun 31 dan oshmaydi)

        var days = byDay
            .Where(d => d.Date.Length >= 10
                        && string.CompareOrdinal(d.Date, monthStart) >= 0
                        && string.CompareOrdinal(d.Date, monthEnd) <= 0)
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .Select(d => new ContactDayPlanDto(d.Date, d.Count))
            .ToList();

        return new ContactMetaDto(
            ContactService.Statuses.Select(s => new ContactStatusDto(s.Key, s.Label, s.IsOpen, s.Color)).ToList(),
            ContactService.Results.Select(r => new ContactResultDto(r.Key, r.Label, r.Reached)).ToList(),
            ContactService.Statuses.Select(s => new ContactCountDto(s.Key, counts.GetValueOrDefault(s.Key))).ToList(),
            due[1],
            new ContactDueCountsDto(due[0], due[1], due[2], due[3], due[4], due[5], due[6]),
            days);
    }

    /* =========================================================================================
     *  NAVBAT
     * ====================================================================================== */

    /// <summary>
    /// Navbat ro'yxati. <paramref name="status"/> bo'sh — FAQAT ochiqlar (new + callback), chunki
    /// bo'lim ochilganda operatorga kerakli narsa shu. <paramref name="status"/>="all" — hammasi.
    /// </summary>
    /// <param name="overdue">true — faqat muddati o'tgan qayta qo'ng'iroqlar
    /// (ESKI parametr; <paramref name="due"/> berilsa e'tiborga olinmaydi).</param>
    /// <param name="due">MUDDAT guruhi: todo | overdue | today | tomorrow | week | later | nodate
    /// (<c>ContactService.Due</c>). "Bugun kimga qo'ng'iroq qilishim kerak?" savoliga shu javob beradi.</param>
    /// <param name="dueDate">ANIQ kun ("yyyy-MM-dd") — "yaqin kunlar" chizig'idan kun tanlanganda.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContactRequestDto>>> List(
        [FromQuery] string? status, [FromQuery] string? q, [FromQuery] bool overdue = false,
        [FromQuery] string? due = null, [FromQuery] string? dueDate = null,
        [FromQuery] int limit = 200)
    {
        var today = Today;
        var query = db.ContactRequests.AsNoTracking().AsQueryable();

        if (string.IsNullOrEmpty(status))
            query = query.Where(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback);
        else if (status != "all" && ContactService.IsValidStatus(status))
            query = query.Where(c => c.Status == status);

        // ANIQ KUN — "yaqin kunlar" chizig'idan tanlangan sana.
        if (!string.IsNullOrWhiteSpace(dueDate) && DateOnly.TryParse(dueDate, out _))
            query = query.Where(c => c.Status == ContactStatuses.Callback && c.DueDate == dueDate);
        else if (ContactService.IsKnownDue(due))
        {
            // Guruh chegaralarini C#da hisoblab, SQLga oddiy sana solishtiruvi bo'lib tushadi
            // (qoidaning O'ZI `ContactService.BucketOf` da — bu yer faqat tarjima).
            var tomorrow = AppClock.Today.AddDays(1).ToString("yyyy-MM-dd");
            var weekEnd = AppClock.Today.AddDays(7).ToString("yyyy-MM-dd");
            query = due switch
            {
                ContactService.Due.Todo => query.Where(c =>
                    c.Status == ContactStatuses.New
                    || (c.Status == ContactStatuses.Callback
                        && (c.DueDate == "" || string.Compare(c.DueDate, today) <= 0))),
                ContactService.Due.Overdue => query.Where(c =>
                    c.Status == ContactStatuses.Callback && c.DueDate != ""
                    && string.Compare(c.DueDate, today) < 0),
                ContactService.Due.Today => query.Where(c =>
                    c.Status == ContactStatuses.Callback && c.DueDate == today),
                ContactService.Due.Tomorrow => query.Where(c =>
                    c.Status == ContactStatuses.Callback && c.DueDate == tomorrow),
                ContactService.Due.Week => query.Where(c =>
                    c.Status == ContactStatuses.Callback
                    && string.Compare(c.DueDate, tomorrow) > 0
                    && string.Compare(c.DueDate, weekEnd) <= 0),
                ContactService.Due.Later => query.Where(c =>
                    c.Status == ContactStatuses.Callback && string.Compare(c.DueDate, weekEnd) > 0),
                // Sanasiz: "Bog'lanish kerak" + (buzuq) sanasiz qayta qo'ng'iroqlar.
                _ => query.Where(c =>
                    c.Status == ContactStatuses.New
                    || (c.Status == ContactStatuses.Callback && c.DueDate == "")),
            };
        }
        else if (overdue)
            query = query.Where(c => c.Status == ContactStatuses.Callback
                                     && c.DueDate != "" && string.Compare(c.DueDate, today) < 0);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(c => c.StudentName.ToLower().Contains(needle)
                                     || c.ReasonLabel.ToLower().Contains(needle));
        }

        // Tartib: eng shoshilinch yuqorida — muddati o'tganlar, so'ng muddati yaqinlar, so'ng yangilar.
        // `DueDate` bo'sh bo'lgan (new) yozuvlar ostida qolmasin uchun avval holat bo'yicha saralaymiz.
        var items = await query
            .OrderBy(c => c.Status == ContactStatuses.Callback ? 0 : 1)
            .ThenBy(c => c.DueDate)
            .ThenByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();

        var phones = await PhonesAsync(items.Select(i => i.StudentId).Distinct().ToList());
        return items.Select(c => ToDto(c, today, phones)).ToList();
    }

    /// <summary>Bitta talab — TARIXI bilan ("kim qaysi bosqichga oldi, natijasi qanday bo'ldi").</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ContactRequestDto>> Get(string id)
    {
        var c = await db.ContactRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });

        var history = await db.ContactAttempts.AsNoTracking()
            .Where(a => a.RequestId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync();

        var phones = await PhonesAsync(new List<string> { c.StudentId });
        return ToDto(c, Today, phones, history);
    }

    /// <summary>
    /// O'quvchining BARCHA talablari — HAR BIRI TARIXI BILAN.
    ///
    /// <para>Tarix ataylab qo'shiladi: o'quvchi profilining "Aloqa" tabida har bog'lanish urinishi
    /// ("javobi nima dedi") qo'ng'iroqlar tarixi bilan yonma-yon ko'rinadi. Bitta o'quvchi
    /// bo'lgani uchun bu ikkita yengil so'rov — ro'yxat endpointiga tarix qo'shilmaydi.</para>
    /// </summary>
    [HttpGet("student/{studentId}")]
    public async Task<ActionResult<IEnumerable<ContactRequestDto>>> ByStudent(string studentId)
    {
        var items = await db.ContactRequests.AsNoTracking()
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        if (items.Count == 0) return new List<ContactRequestDto>();

        var ids = items.Select(c => c.Id).ToList();
        var history = (await db.ContactAttempts.AsNoTracking()
                .Where(a => ids.Contains(a.RequestId))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .ToListAsync())
            .GroupBy(a => a.RequestId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var phones = await PhonesAsync(new List<string> { studentId });
        return items.Select(c => ToDto(c, Today, phones, history.GetValueOrDefault(c.Id))).ToList();
    }

    /* =========================================================================================
     *  AMALLAR
     * ====================================================================================== */

    /// <summary>
    /// Yangi talab ochish ("Bog'lanish kerak"). Bir o'quvchida bir vaqtda faqat BITTA ochiq talab
    /// bo'ladi — aks holda navbat bir xil odam bilan to'lib ketardi. Ochiq talab bo'lsa 400 va
    /// javobda o'sha talab id'si qaytadi (klient uni ocha oladi).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ContactRequestDto>> Create(CreateContactRequest req)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StudentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var open = await db.ContactRequests
            .FirstOrDefaultAsync(c => c.StudentId == req.StudentId
                                      && (c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback));
        if (open is not null)
            return BadRequest(new
            {
                message = $"Bu o'quvchida allaqachon ochiq talab bor ({ContactService.StatusLabel(open.Status)}) — "
                          + "yangisini ochish o'rniga o'shanga izoh qo'shing.",
                existingId = open.Id,
            });

        var (reasonId, reasonLabel) = await queue.ResolveReasonAsync(req.ReasonId);

        var due = (req.DueDate ?? "").Trim();
        if (due.Length > 0 && !DateOnly.TryParse(due, out _))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        var c = queue.Add(student, reasonId, reasonLabel, (req.Note ?? "").Trim(), due, ActorId, Actor);
        await db.SaveChangesAsync();
        return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));
    }

    /// <summary>
    /// KO'PLAB o'quvchini birdan navbatga qo'shish — "O'quvchilar ro'yxati"da bir nechtasini
    /// belgilab "Bog'lanish kerak" bosilganda.
    ///
    /// <para>DIQQAT: bitta o'quvchida ochiq talab bo'lsa BUTUN amal to'xtamaydi — u chetlab
    /// o'tiladi va javobda soni/ismlari qaytadi. Aks holda 100 ta tanlangan o'quvchidan bittasi
    /// tufayli hech kim navbatga tushmasdi.</para>
    /// </summary>
    [HttpPost("bulk")]
    public async Task<ActionResult<ContactBulkResultDto>> CreateBulk(CreateContactRequestsBulk req)
    {
        var ids = (req.StudentIds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "O'quvchi tanlanmagan" });
        if (ids.Count > MaxBulk)
            return BadRequest(new { message = $"Bir vaqtda ko'pi bilan {MaxBulk} ta o'quvchi tanlanadi" });

        var due = (req.DueDate ?? "").Trim();
        if (due.Length > 0 && !DateOnly.TryParse(due, out _))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        var r = await queue.AddManyAsync(ids, req.ReasonId, req.Note, due, ActorId, Actor);
        return new ContactBulkResultDto(r.Created, r.Skipped, r.SkippedNames, r.NotFound);
    }


    /// <summary>
    /// BOG'LANILDI — urinish natijasi + "javobi nima dedi" + keyingi bosqich.
    /// Modulning asosiy amali; hisobotlardagi barcha sonlar shu yerdan kelib chiqadi.
    /// </summary>
    [HttpPost("{id}/attempt")]
    public async Task<ActionResult<ContactRequestDto>> Attempt(string id, ContactAttemptRequest req)
    {
        var c = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });
        if (!ContactService.IsOpen(c.Status))
            return BadRequest(new { message = "Talab yakunlangan — avval uni qayta oching." });

        if (!ContactService.IsValidResult(req.Result))
            return BadRequest(new { message = "Natija tanlanmagan" });
        if (!ContactService.CanTransitionTo(req.NextStatus))
            return BadRequest(new { message = "Keyingi qadam noto'g'ri (qayta qo'ng'iroq / hal bo'ldi / bog'lanib bo'lmadi)" });

        var due = (req.DueDate ?? "").Trim();
        if (req.NextStatus == ContactStatuses.Callback)
        {
            if (due.Length == 0) return BadRequest(new { message = "Qayta qo'ng'iroq sanasini tanlang" });
            if (!DateOnly.TryParse(due, out _)) return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });
        }
        else due = "";

        var response = (req.Response ?? "").Trim();
        if (response.Length > 2000) response = response[..2000];

        var now = AppClock.Iso();
        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = c.Id,
            StudentId = c.StudentId,
            Type = ContactAttemptTypes.Contact,
            Result = req.Result,
            Response = response,
            NextStatus = req.NextStatus,
            DueDate = due,
            ActorId = ActorId,
            ActorName = Actor,
            CreatedAt = now,
            Date = Today,
        });

        c.AttemptCount++;
        c.Status = req.NextStatus;
        c.DueDate = due;

        // USTUN: karta sudrab tashlangan bo'lsa O'SHA ustun, aks holda yangi holatning TIZIM
        // ustuni (bo'sh StageId aynan shuni bildiradi). Mos kelmagan ustun JIM e'tiborsiz —
        // amalning o'zi (bog'lanish natijasi) ustun tufayli rad etilmasligi kerak.
        c.StageId = "";
        var wantStage = (req.StageId ?? "").Trim();
        if (wantStage.Length > 0)
        {
            var target = await db.ContactStages.FirstOrDefaultAsync(x => x.Id == wantStage);
            if (target is not null && ContactService.StageMatches(target.BaseStatus, c.Status))
                c.StageId = target.Id;
        }

        c.LastResponse = response;
        c.LastActorName = Actor;
        c.LastActionAt = now;
        if (!ContactService.IsOpen(c.Status))
        {
            c.ClosedAt = now;
            c.ClosedBy = Actor;
        }
        else
        {
            // Qayta ochilgan talab yana yopilsa "eski" yopilish izi qolib ketmasin.
            c.ClosedAt = "";
            c.ClosedBy = "";
        }

        audit.Record(AuditEntity, c.Id, "update",
            $"Bog'lanildi: {c.StudentName} — {ContactService.ResultLabel(req.Result)} → "
            + $"{ContactService.StatusLabel(c.Status)}"
            + (due.Length > 0 ? $" ({due})" : "")
            + (response.Length > 0 ? $" — javobi: {response}" : ""),
            studentId: c.StudentId);

        await db.SaveChangesAsync();
        return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));
    }

    /// <summary>Bosqichni o'zgartirmasdan izoh qo'shish (masalan "ota-onasi kelib ketdi").</summary>
    [HttpPost("{id}/note")]
    public async Task<ActionResult<ContactRequestDto>> AddNote(string id, ContactNoteRequest req)
    {
        var c = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "Izoh bo'sh" });
        if (text.Length > 2000) text = text[..2000];

        var now = AppClock.Iso();
        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = c.Id, StudentId = c.StudentId, Type = ContactAttemptTypes.Note,
            Response = text, ActorId = ActorId, ActorName = Actor, CreatedAt = now, Date = Today,
        });
        c.LastActorName = Actor;
        c.LastActionAt = now;

        audit.Record(AuditEntity, c.Id, "update", $"Bog'lanish izohi ({c.StudentName}): {text}",
            studentId: c.StudentId);
        await db.SaveChangesAsync();
        return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));
    }

    /// <summary>Yakunlangan talabni QAYTA ochish — yana navbatga qaytadi.</summary>
    [HttpPost("{id}/reopen")]
    public async Task<ActionResult<ContactRequestDto>> Reopen(string id, ContactReopenRequest req)
    {
        var c = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });
        if (ContactService.IsOpen(c.Status))
            return BadRequest(new { message = "Talab allaqachon ochiq" });

        // Shu o'quvchida boshqa ochiq talab bo'lsa ikkitasi paydo bo'lmasin (Create bilan bir qoida).
        var open = await db.ContactRequests.AnyAsync(x => x.StudentId == c.StudentId && x.Id != c.Id
            && (x.Status == ContactStatuses.New || x.Status == ContactStatuses.Callback));
        if (open)
            return BadRequest(new { message = "Bu o'quvchida boshqa ochiq talab bor — avval uni yakunlang." });

        var now = AppClock.Iso();
        var note = (req.Note ?? "").Trim();
        c.Status = ContactStatuses.New;
        c.DueDate = "";
        // Holat o'zgardi — eski ustun yangi holatga ZID bo'lib qolmasin (tizim ustuniga qaytadi).
        c.StageId = "";
        c.ClosedAt = "";
        c.ClosedBy = "";
        c.LastActorName = Actor;
        c.LastActionAt = now;

        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = c.Id, StudentId = c.StudentId, Type = ContactAttemptTypes.Reopen,
            Response = note, NextStatus = ContactStatuses.New,
            ActorId = ActorId, ActorName = Actor, CreatedAt = now, Date = Today,
        });

        audit.Record(AuditEntity, c.Id, "update",
            $"Bog'lanish talabi qayta ochildi: {c.StudentName}" + (note.Length > 0 ? $" — {note}" : ""),
            studentId: c.StudentId);
        await db.SaveChangesAsync();
        return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));
    }

    /// <summary>Talabni butunlay o'chirish (xato ochilgan bo'lsa) — tarixi bilan.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var c = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });

        await db.ContactAttempts.Where(a => a.RequestId == id).ExecuteDeleteAsync();
        audit.Record(AuditEntity, c.Id, "delete", $"Bog'lanish talabi o'chirildi: {c.StudentName}",
            studentId: c.StudentId);
        db.ContactRequests.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* =========================================================================================
     *  HISOBOTLAR
     * ====================================================================================== */

    /// <summary>
    /// Davr bo'yicha hisobot: kunlik oqim, xodimlar kesimi ("kim qaysi bosqichga oldi"),
    /// sabablar va natijalar kesimi. Sanoqlar <see cref="ContactAttempt"/> dan — ya'ni
    /// "nima bo'ldi" emas, "kim nima qildi" bo'yicha.
    /// </summary>
    /// <param name="from">"yyyy-MM-dd" (bo'sh — oxirgi 30 kun).</param>
    /// <remarks>Hisob-kitobning O'ZI <see cref="ContactReport.BuildAsync"/> da — AYNAN o'sha
    /// funksiyadan kunlik jurnal va AI tahlili ham foydalanadi (raqamlar bir joyda hisoblansin).</remarks>
    [HttpGet("stats")]
    public async Task<ActionResult<ContactStatsDto>> Stats([FromQuery] string? from, [FromQuery] string? to)
    {
        if (!MaySeeReports) return ReportsForbidden();
        if (!TryPeriod(from, to, out var fromDate, out var toDate))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        var m = await ContactReport.BuildAsync(db, fromDate, toDate, Today);
        return new ContactStatsDto(
            m.From, m.To, m.Created, m.Attempts, m.Reached, m.Done, m.Callback, m.Failed,
            m.OpenNow, m.OverdueNow,
            m.Daily, m.ByStaff, m.ByReason, m.ByResult,
            TopWords: m.TopWords, WithResponse: m.WithResponse);
    }

    /// <summary>
    /// KUNLIK JURNAL — "kimga qo'ng'iroq qilindi, qachon, nima dedi, qaysi sabab bilan",
    /// HAR KUN ALOHIDA. Yuqoridagi jadvallar "nechta" ga javob beradi, jurnal esa kunning
    /// o'zini boshdan-oxir ko'rsatadi.
    /// </summary>
    /// <param name="type">Faqat shu turdagi hodisalar: contact | created | note | reopen
    /// (bo'sh — hammasi). Vergul bilan bir nechtasi ham beriladi.</param>
    [HttpGet("journal")]
    public async Task<ActionResult<IEnumerable<ContactJournalDayDto>>> Journal(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? type,
        [FromQuery] int limit = ContactReport.DefaultJournalItems)
    {
        if (!MaySeeReports) return ReportsForbidden();
        if (!TryPeriod(from, to, out var fromDate, out var toDate))
            return BadRequest(new { message = "Sana noto'g'ri (YYYY-MM-DD)" });

        // Noma'lum tur JIM tashlanadi (filtr bo'sh bo'lib qolsa hammasi qaytadi) — klientdagi
        // xato kalit tufayli jurnal butunlay bo'shab qolmasin.
        var types = (type ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => ContactService.AttemptTypes.Any(t => t.Key == k))
            .ToList();

        return await ContactReport.JournalAsync(db, fromDate, toDate, types, limit);
    }

    /* =========================================================================================
     *  AI TAHLIL (Gemini) — sabablar, javob matnlari va natijalar bo'yicha xulosa
     * ====================================================================================== */

    /// <summary>Saqlangan tahlillar — eng yangisi birinchi. Davr berilsa faqat AYNI o'sha davrniki.</summary>
    [HttpGet("ai-analyses")]
    public async Task<ActionResult<IEnumerable<ContactAiRecordDto>>> AiAnalyses(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        if (!MaySeeReports) return ReportsForbidden();
        return await ContactAiAnalysisService.HistoryAsync(db, from, to, ct);
    }

    /// <summary>
    /// Tanlangan davr uchun yangi AI tahlil. Shu davr uchun BUGUN tahlil qilingan bo'lsa Gemini
    /// chaqirilmaydi — mavjudi qaytadi (<c>alreadyToday=true</c>, xato EMAS).
    /// </summary>
    /// <remarks>POST — ya'ni <c>contacts</c> bo'limining "qo'shish" ruxsati talab qilinadi
    /// (<c>AdminPermAttribute</c>): faqat ko'rish ruxsati bor xodim tahlilni O'QIYDI, lekin
    /// yangisini (pulli Gemini chaqiruvini) boshlay olmaydi — voronka tahlilidagi bilan bir xil qoida.</remarks>
    [HttpPost("ai-analysis")]
    public async Task<ActionResult<ContactAiResponseDto>> AiAnalysis(
        ContactAiRequest? req, CancellationToken ct)
    {
        if (!MaySeeReports) return ReportsForbidden();
        return await ContactAiAnalysisService.GenerateAsync(db, config, req?.From, req?.To, ct);
    }

    /// <summary>Davr chegaralari: bo'sh bo'lsa oxirgi 30 kun; teskari berilsa almashtiriladi.</summary>
    private static bool TryPeriod(string? from, string? to, out string fromDate, out string toDate)
    {
        var today = AppClock.Today;
        fromDate = string.IsNullOrWhiteSpace(from)
            ? today.AddDays(-29).ToString("yyyy-MM-dd") : from!.Trim();
        toDate = string.IsNullOrWhiteSpace(to) ? today.ToString("yyyy-MM-dd") : to!.Trim();
        if (!DateOnly.TryParse(fromDate, out var f) || !DateOnly.TryParse(toDate, out var t))
            return false;
        if (t < f) (fromDate, toDate) = (toDate, fromDate);
        return true;
    }

    /// <summary>
    /// JAVOBLAR LENTASI — "javobi nima dedi" matnlarini o'qish uchun. Hisobotdagi sonlar
    /// "nechta" ga javob beradi, bu esa "NIMA deyilgan" ga.
    /// </summary>
    /// <param name="result">Natija kaliti bo'yicha filtr (answered / no_answer / ...).</param>
    /// <param name="q">Javob matni ichidan qidiruv.</param>
    [HttpGet("responses")]
    public async Task<ActionResult<IEnumerable<ContactResponseRowDto>>> Responses(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? result,
        [FromQuery] string? actor, [FromQuery] string? q, [FromQuery] int limit = 200)
    {
        if (!MaySeeReports) return ReportsForbidden();
        var query = db.ContactAttempts.AsNoTracking()
            // Faqat HAQIQIY bog'lanish urinishlari va faqat MATN yozilganlari — bo'sh qatorlar
            // lentani suyultirib, o'qishni qiyinlashtirardi.
            .Where(a => a.Type == ContactAttemptTypes.Contact && a.Response != "");

        if (!string.IsNullOrWhiteSpace(from)) query = query.Where(a => string.Compare(a.Date, from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) query = query.Where(a => string.Compare(a.Date, to) <= 0);
        if (!string.IsNullOrWhiteSpace(result)) query = query.Where(a => a.Result == result);
        if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(a => a.ActorName == actor);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(a => a.Response.ToLower().Contains(needle));
        }

        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();

        // Talab ma'lumoti (o'quvchi ismi, sabab) — bitta so'rovda biriktiriladi (N+1 emas).
        var requestIds = rows.Select(r => r.RequestId).Distinct().ToList();
        var requests = await db.ContactRequests.AsNoTracking()
            .Where(c => requestIds.Contains(c.Id))
            .Select(c => new { c.Id, c.StudentName, c.ReasonLabel })
            .ToDictionaryAsync(c => c.Id);

        return rows.Select(a =>
        {
            var req = requests.GetValueOrDefault(a.RequestId);
            return new ContactResponseRowDto(
                a.Id, a.RequestId, a.StudentId, req?.StudentName ?? "",
                req?.ReasonLabel ?? "", a.Result, ContactService.ResultLabel(a.Result),
                a.NextStatus, ContactService.StatusLabel(a.NextStatus),
                a.Response, a.ActorName, a.CreatedAt);
        }).ToList();
    }

    /* =========================================================================================
     *  Yordamchilar
     * ====================================================================================== */

    /// <summary>O'quvchi id → bog'lanish uchun raqamlar (o'zi + ota-ona), takrorsiz va bo'shsiz.</summary>
    private async Task<Dictionary<string, List<string>>> PhonesAsync(List<string> studentIds)
    {
        if (studentIds.Count == 0) return new Dictionary<string, List<string>>();
        var rows = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
            .ToListAsync();
        return rows.ToDictionary(
            r => r.Id,
            r => new[] { r.Phone, r.ParentPhone, r.FatherPhone, r.MotherPhone }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .Distinct()
                .ToList());
    }

    private static ContactRequestDto ToDto(
        ContactRequest c, string today, Dictionary<string, List<string>> phones,
        List<ContactAttempt>? history = null) =>
        new(
            c.Id, c.StudentId, c.StudentName,
            c.ReasonId, c.ReasonLabel, c.Note,
            c.Status, ContactService.StatusLabel(c.Status), c.DueDate,
            ContactService.IsOverdue(c.Status, c.DueDate, today),
            c.AttemptCount, c.LastResponse, c.LastActorName, c.LastActionAt,
            c.CreatedAt, c.CreatedBy, c.ClosedAt, c.ClosedBy,
            phones.GetValueOrDefault(c.StudentId) ?? new List<string>(),
            history?.Select(a => new ContactAttemptDto(
                a.Id, a.Type, a.Result, ContactService.ResultLabel(a.Result), a.Response,
                a.NextStatus, ContactService.StatusLabel(a.NextStatus), a.DueDate,
                a.ActorName, a.CreatedAt)).ToList(),
            c.StageId);

    /* =========================================================================================
     *  KANBAN USTUNLARI (ContactStage)
     *
     *  ⚠️ Ustun HOLATNI ALMASHTIRMAYDI. Har ustunning BaseStatus'i bor va u to'rtta bazaviy
     *  holatdan biri; hisobotlar, navbat va muddat guruhlari avvalgidek Status bo'yicha
     *  ishlaydi. Foydalanuvchi xohlagancha ustun qo'shadi, hisobot esa o'zgarmaydi.
     * ====================================================================================== */

    /// <summary>Ustunlar ro'yxati (tartibi bilan) + har birida nechta talab borligi.</summary>
    [HttpGet("stages")]
    public async Task<ActionResult<IEnumerable<ContactStageDto>>> Stages()
    {
        var stages = await db.ContactStages.AsNoTracking().OrderBy(x => x.Order).ToListAsync();
        var requests = await db.ContactRequests.AsNoTracking()
            .Select(c => new { c.StageId, c.Status }).ToListAsync();

        // StageId bo'sh (yoki o'chirilgan ustunga ishora qiladigan) talab o'z holatining TIZIM
        // ustuniga sanaladi — ekranda ham aynan o'sha yerda ko'rinadi.
        var known = stages.Select(x => x.Id).ToHashSet();
        var counts = new Dictionary<string, int>();
        foreach (var r in requests)
        {
            var key = !string.IsNullOrEmpty(r.StageId) && known.Contains(r.StageId) ? r.StageId : r.Status;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return stages.Select(x => new ContactStageDto(
            x.Id, x.Title, x.Color, x.Order, x.BaseStatus,
            ContactService.StatusLabel(x.BaseStatus), x.IsSystem, counts.GetValueOrDefault(x.Id))).ToList();
    }

    /// <summary>Yangi ustun (oxiriga qo'shiladi).</summary>
    [HttpPost("stages")]
    public async Task<ActionResult<ContactStageDto>> CreateStage(ContactStageRequest p)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Ustun nomi bo'sh" });
        if (title.Length > 100) title = title[..100];

        // Bazaviy holat berilmasa "qayta qo'ng'iroq" — yangi ustunlar odatda ORALIQ qadam
        // ("SMS yuborildi", "Ota-onasi bilan gaplashildi"), ya'ni talab hali navbatda turadi.
        var baseStatus = (p.BaseStatus ?? "").Trim();
        if (baseStatus.Length == 0) baseStatus = ContactStatuses.Callback;
        if (!ContactService.CanAnchorTo(baseStatus))
            return BadRequest(new { message = "Bazaviy bosqich noto'g'ri" });

        var maxOrder = await db.ContactStages.AnyAsync()
            ? await db.ContactStages.MaxAsync(x => x.Order) : -1;
        var stage = new ContactStage
        {
            Title = title,
            Color = ContactService.SafeColor(p.Color),
            BaseStatus = baseStatus,
            Order = maxOrder + 1,
        };
        db.ContactStages.Add(stage);
        audit.Record(AuditEntity, stage.Id, "create",
            $"Bog'lanish taxtasiga ustun qo'shildi: {stage.Title} ({ContactService.StatusLabel(baseStatus)})");
        await db.SaveChangesAsync();
        return new ContactStageDto(stage.Id, stage.Title, stage.Color, stage.Order,
            stage.BaseStatus, ContactService.StatusLabel(stage.BaseStatus), stage.IsSystem, 0);
    }

    /// <summary>
    /// Ustunni tahrirlash. ⚠️ TIZIM ustunining bazaviy bosqichi O'ZGARTIRILMAYDI (nomi va
    /// rangi esa o'zgaradi) — u har holat uchun "uy" bo'lib qolishi kerak.
    /// </summary>
    [HttpPut("stages/{stageId}")]
    public async Task<ActionResult<ContactStageDto>> UpdateStage(string stageId, ContactStageRequest p)
    {
        var stage = await db.ContactStages.FirstOrDefaultAsync(x => x.Id == stageId);
        if (stage is null) return NotFound(new { message = "Ustun topilmadi" });

        var title = (p.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Ustun nomi bo'sh" });
        if (title.Length > 100) title = title[..100];

        var before = stage.Title;
        stage.Title = title;
        stage.Color = ContactService.SafeColor(p.Color);

        var baseStatus = (p.BaseStatus ?? "").Trim();
        if (baseStatus.Length > 0 && baseStatus != stage.BaseStatus)
        {
            if (stage.IsSystem)
                return BadRequest(new { message = "Tizim ustunining bazaviy bosqichi o'zgartirilmaydi" });
            if (!ContactService.CanAnchorTo(baseStatus))
                return BadRequest(new { message = "Bazaviy bosqich noto'g'ri" });
            // Ustun boshqa holatga ko'chsa, ichidagi talablar unga ZID bo'lib qolardi —
            // ularni o'z holatining tizim ustuniga qaytaramiz (karta yo'qolmaydi).
            var inside = await db.ContactRequests.Where(c => c.StageId == stage.Id).ToListAsync();
            foreach (var c in inside) c.StageId = "";
            stage.BaseStatus = baseStatus;
        }

        audit.Record(AuditEntity, stage.Id, "update",
            $"Bog'lanish taxtasi ustuni tahrirlandi: {before} → {stage.Title}");
        await db.SaveChangesAsync();
        return new ContactStageDto(stage.Id, stage.Title, stage.Color, stage.Order,
            stage.BaseStatus, ContactService.StatusLabel(stage.BaseStatus), stage.IsSystem, 0);
    }

    /// <summary>
    /// Ustunni o'chirish. TIZIM ustuni o'chirilmaydi; ichida talab bor ustun ham o'chirilmaydi
    /// (avval ko'chirilsin) — lidlardagidek "jimgina yetim qoldirish" bu yerda qilinmaydi.
    /// </summary>
    [HttpDelete("stages/{stageId}")]
    public async Task<IActionResult> DeleteStage(string stageId)
    {
        var stage = await db.ContactStages.FirstOrDefaultAsync(x => x.Id == stageId);
        if (stage is null) return NotFound(new { message = "Ustun topilmadi" });
        if (stage.IsSystem)
            return BadRequest(new { message = "Tizim ustunini o'chirib bo'lmaydi" });

        var count = await db.ContactRequests.CountAsync(c => c.StageId == stage.Id);
        if (count > 0)
            return BadRequest(new { message = $"Bu ustunda {count} ta talab bor — avval ularni boshqa ustunga ko'chiring." });

        db.ContactStages.Remove(stage);
        audit.Record(AuditEntity, stage.Id, "delete",
            $"Bog'lanish taxtasi ustuni o'chirildi: {stage.Title}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ustunlar tartibini saqlash (kelgan id'lar tartibida).</summary>
    [HttpPatch("stages/reorder")]
    public async Task<IActionResult> ReorderStages(ContactStageReorderRequest req)
    {
        var stages = await db.ContactStages.ToListAsync();
        for (var i = 0; i < req.Ids.Count; i++)
        {
            var stage = stages.FirstOrDefault(x => x.Id == req.Ids[i]);
            if (stage is not null) stage.Order = i;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Kartani BOSHQA USTUNGA ko'chirish — bosqich (Status) O'ZGARMAGANDA.
    ///
    /// <para>⚠️ Holatni o'zgartiradigan ko'chirish bu yerdan O'TMAYDI: u
    /// <c>POST {id}/attempt</c> orqali, natija va javob bilan yoziladi (§ qoidalar §2).
    /// Shu sabab mos kelmagan ustun 400 bilan rad etiladi va klientga nima qilish kerakligi
    /// aytiladi — jimgina "boshqa narsa" qilib qo'yilmaydi.</para>
    /// </summary>
    [HttpPost("{id}/stage")]
    public async Task<ActionResult<ContactRequestDto>> MoveStage(string id, ContactStageMoveRequest req)
    {
        var c = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Talab topilmadi" });

        var stage = await db.ContactStages.FirstOrDefaultAsync(x => x.Id == req.StageId);
        if (stage is null) return NotFound(new { message = "Ustun topilmadi" });
        if (!ContactService.StageMatches(stage.BaseStatus, c.Status))
            return BadRequest(new
            {
                message = "Bu ustun boshqa bosqichga tegishli — «Bog'lanildi» orqali o'tkazing.",
            });
        if (c.StageId == stage.Id) return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));

        var now = AppClock.Iso();
        c.StageId = stage.Id;
        c.LastActorName = Actor;
        c.LastActionAt = now;

        // Ko'chirish TARIXGA yoziladi (izoh turi) — "kim, qachon" yo'qolmasin. `note` turi
        // ATAYIN: hisobotlardagi "urinish"/"bog'lanildi" sonlari FAQAT `contact` turini
        // sanaydi, ya'ni ustun ko'chirish raqamlarni buzmaydi.
        db.ContactAttempts.Add(new ContactAttempt
        {
            RequestId = c.Id, StudentId = c.StudentId, Type = ContactAttemptTypes.Note,
            Response = $"Ustun: {stage.Title}",
            ActorId = ActorId, ActorName = Actor, CreatedAt = now, Date = Today,
        });

        audit.Record(AuditEntity, c.Id, "update",
            $"Bog'lanish ustuni o'zgardi ({c.StudentName}): {stage.Title}", studentId: c.StudentId);
        await db.SaveChangesAsync();
        return ToDto(c, Today, await PhonesAsync(new List<string> { c.StudentId }));
    }
}
