using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using System.Security.Claims;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Admin "Xabarlar" bo'limi: guruh ota-onalariga (va o'qituvchilarga) Telegram bot orqali e'lon
/// yuborish, oluvchilar ro'yxati va qo'lda yoziladigan tayyor matnlar (shablonlar).
/// <para>2026-10: SMS (Eskiz / Local SMS), push va guruh chati olib tashlandi — Telegram yagona
/// tashqi kanal (`.claude/rules/messaging.md`).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("messages.broadcast")]
[Route("api/admin/messages")]
public class MessagesController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    // ---------- Guruhlar ro'yxati (e'lon auditoriyasini tanlash uchun) ----------

    /// <summary>E'lon "Guruh" auditoriyasi uchun guruhlar: o'quvchilar soni va Telegram'ga ulangan
    /// chatlar soni. (Ilgari guruh chati darvozasi ostida edi — chat 2026-10 da olib tashlandi.)</summary>
    [HttpGet("classes")]
    public async Task<ActionResult<IEnumerable<MessageClassDto>>> Classes()
    {
        var classes = await db.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
        var regs = await db.TelegramRegistrations.Select(r => new { r.StudentId, r.ChatId }).ToListAsync();

        // GURUH → O'QUVCHILAR: e'lon YUBORISHDA ishlatiladigan AYNAN o'sha qoida
        // (`MessagingAudience.GroupAudienceByNameAsync`: tirik a'zolik, arxivdagilarsiz, a'zoligi
        // umuman yo'q eski yozuvlarda — `ClassName` zaxirasi). Ya'ni ko'rsatilgan son = oluvchilar.
        // ⚠️ Ilgari sanoq a'zolikdan, yuborish esa eskirgan `ClassName` dan olinardi — ikkinchi
        // guruhdagi o'quvchi sanalar, lekin xabar olmasdi.
        var audienceByName = await MessagingAudience.GroupAudienceByNameAsync(db);
        var chatsByStudent = regs.GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).ToList());

        var studentCountByClass = audienceByName.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        // Har guruh bo'yicha alohida (distinct) Telegram chatlar soni — e'lon oluvchilar.
        // (O'quvchi ikki guruhda bo'lsa uning chati IKKALASIDA ham sanaladi — u ikkalasidan ham
        // e'lon oladi.)
        var parentChatsByClass = audienceByName.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.SelectMany(sid => chatsByStudent.GetValueOrDefault(sid) ?? new List<long>())
                .Distinct().Count());

        return classes.Select(c => new MessageClassDto(
            c.Name, c.Grade,
            studentCountByClass.GetValueOrDefault(c.Name, 0),
            parentChatsByClass.GetValueOrDefault(c.Name, 0))).ToList();
    }

    // ---------- E'lon (Telegram) ----------

    [HttpGet("broadcasts")]
    public async Task<ActionResult<IEnumerable<BroadcastDto>>> Broadcasts([FromQuery] string? className)
    {
        var q = db.Broadcasts.AsQueryable();
        if (!string.IsNullOrEmpty(className)) q = q.Where(b => b.ClassName == className);
        var list = await q.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
        return list.Select(b => new BroadcastDto(
            b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
            b.RecipientCount, b.SentCount)).ToList();
    }

    /// <summary>
    /// LEGACY adapter (CRM upgrade P2.7): the old "scope" request is mapped to an <see cref="AudienceSpec"/> and sent
    /// through the Telegram OUTBOX (<see cref="CampaignService"/>) — no direct send any more. Returns the old
    /// <see cref="BroadcastDto"/> (<c>SentCount</c> = 0: delivery is asynchronous; the report is under
    /// <c>api/admin/messaging/campaigns/{id}</c>). The new client does not call it.
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni kerak" });
        var scope = (req.Scope ?? "class").Trim().ToLowerInvariant();
        var all = WunderkindLC.Application.Services.Messaging.TelegramRelations.All.ToList();
        var statuses = new List<string> { "active", "trial", "frozen" };
        var specs = new List<AudienceSpec>();
        switch (scope)
        {
            case "selected":
                var ids = (req.StudentIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                var teacherIds = (req.TeacherIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if (ids.Count == 0 && teacherIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
                if (ids.Count > 0) specs.Add(new AudienceSpec { Kind = "selected", StudentIds = ids, Relations = all, OnlyDebtors = req.OnlyDebtors });
                if (teacherIds.Count > 0) specs.Add(new AudienceSpec { Kind = "teachers", TeacherIds = teacherIds });
                break;
            case "all":
                specs.Add(new AudienceSpec { Kind = "active", MemberStatuses = statuses, Relations = all, OnlyDebtors = req.OnlyDebtors });
                break;
            default: // class
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length == 0) return BadRequest(new { message = "Guruh kerak" });
                var groupIds = await db.Classes.Where(c => c.Name == cn).Select(c => c.Id).ToListAsync();
                if (groupIds.Count == 0) return BadRequest(new { message = "Guruh topilmadi" });
                specs.Add(new AudienceSpec { Kind = "active", GroupIds = groupIds, MemberStatuses = statuses, Relations = all, OnlyDebtors = req.OnlyDebtors });
                break;
        }

        var services = HttpContext.RequestServices;
        var audit = services.GetRequiredService<AuditService>();
        var actorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var actorName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Administrator";
        CampaignCreatedDto? last = null;
        var total = 0;
        foreach (var spec in specs)
        {
            var (created, error) = await CampaignService.CreateAsync(db, audit,
                new CreateCampaignRequest(CampaignService.KindBroadcast, spec, text, null, false),
                actorId, actorName, telegram.IsConfigured);
            if (error is not null) return BadRequest(new { message = error });
            last = created;
            total += created!.Messages;
        }
        services.GetRequiredService<IOutboxSignal>().Wake();
        var bc = await db.Broadcasts.AsNoTracking().FirstAsync(b => b.Id == last!.Id);
        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName, bc.CreatedAt.ToString("o"), total, 0);
    }

    // Matn o'rinbosarlari markazlashgan: <see cref="MessageTokenizer"/> (barcha kanal/auditoriyalar shu yerda).

    /// <summary>O'quvchilarning asosiy guruhi (ClassName → Group) — dars jadvali tokenlari uchun.
    /// Nomi takrorlansa birinchisi olinadi (ToDictionary istisnosiz).</summary>
    private async Task<Dictionary<string, Group>> GroupByNameAsync() =>
        (await db.Classes.ToListAsync()).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());

    /// <summary>O'qituvchi id → F.I.Sh ({oqituvchi} tokeni uchun) — ro'yxatga bir marta yuklanadi.</summary>
    private async Task<Dictionary<string, string>> TeacherNamesAsync() =>
        await MessageTokenizer.TeacherNamesByIdAsync(db);

    /// <summary>"Guruh" qamrovida {guruh} tokeni TANLANGAN guruh nomi bilan to'ladi (o'quvchining
    /// eskirgan <c>ClassName</c>i bilan emas — u ikkinchi guruhda o'qishi mumkin).</summary>
    private static string GroupScoped(string text, string? groupName) =>
        string.IsNullOrEmpty(groupName) ? text
            : MessageTokenizer.ApplyExtra(text, new Dictionary<string, string> { ["{guruh}"] = groupName });

    /// <summary>E'lon matnini shu o'quvchi/ota-ona ma'lumotiga moslab almashtiradi.</summary>
    private static string Personalize(string template, Student s, TelegramRegistration reg, string centerName,
        Group? group = null, string? teacherName = null) =>
        MessageTokenizer.Student(template, s, reg.ParentName, reg.Phone, centerName, null, group, teacherName);

    // ---------- Telegram ro'yxati (ota-onalar) ----------

    [HttpGet("telegram/registrations")]
    public async Task<ActionResult<IEnumerable<TelegramParentDto>>> Registrations([FromQuery] string? className)
    {
        var studentsQ = db.Students.AsQueryable();
        if (!string.IsNullOrEmpty(className))
        {
            // Guruh a'zoligi bo'yicha — e'lon oluvchilari bilan bir xil ro'yxat (eskirgan ClassName emas).
            var memberIds = await MessagingAudience.GroupAudienceAsync(db, className);
            studentsQ = studentsQ.Where(s => memberIds.Contains(s.Id));
        }
        var students = await studentsQ.ToListAsync();
        var byId = students.ToDictionary(s => s.Id);
        var ids = students.Select(s => s.Id).ToList();

        var regs = await db.TelegramRegistrations
            .Where(r => ids.Contains(r.StudentId))
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

        return regs.Select(r =>
        {
            byId.TryGetValue(r.StudentId, out var s);
            return new TelegramParentDto(
                r.StudentId, s?.FullName ?? "", s?.ClassName ?? "", s?.Balance ?? 0m,
                r.ParentName, r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"));
        }).ToList();
    }

    /// <summary>"Tanlab" e'lon uchun Telegramda ro'yxatdan o'tgan o'qituvchilar (xodim ro'yxati).</summary>
    [HttpGet("telegram/registrations/teachers")]
    public async Task<ActionResult<IEnumerable<TelegramTeacherDto>>> TeacherRegistrations()
    {
        var regs = await db.TelegramRegistrations
            .Where(r => r.TeacherId != null)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        var teacherIds = regs.Select(r => r.TeacherId!).Distinct().ToList();
        var teachers = await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        return regs.Select(r => new TelegramTeacherDto(
            r.TeacherId!, teachers.GetValueOrDefault(r.TeacherId!)?.FullName ?? "",
            r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"))).ToList();
    }

    /// <summary>Telegram bot holati (sozlanganmi, bot foydalanuvchi nomi) — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("telegram/status")]
    public ActionResult<object> TelegramStatus() =>
        Ok(new { configured = telegram.IsConfigured, botUsername = telegram.BotUsername ?? "" });

    // ---------- "Tanlab" e'lon uchun oluvchilar ----------

    /// <summary>
    /// "Tanlab" e'lon uchun o'quvchilar — barcha arxivlanmagan o'quvchilar (ism bo'yicha) va har biriga
    /// ULANGAN Telegram chatlari soni (0 bo'lsa — e'lon unga yetib bormaydi, UI buni ko'rsatadi).
    /// </summary>
    [HttpGet("recipients")]
    public async Task<ActionResult<IEnumerable<MessageRecipientDto>>> Recipients()
    {
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.ClassName })
            .ToListAsync();
        var chats = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.StudentId != null && r.StudentId != "")
                .Select(r => new { r.StudentId, r.ChatId }).ToListAsync())
            .GroupBy(r => r.StudentId!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().Count());
        return students.Select(s => new MessageRecipientDto(
            s.Id, s.FullName, s.ClassName ?? "", chats.GetValueOrDefault(s.Id, 0))).ToList();
    }

    /// <summary>"Tanlab" e'lon uchun o'qituvchilar — barcha arxivlanmagan o'qituvchilar va ulangan
    /// Telegram chatlari soni.</summary>
    [HttpGet("recipients/teachers")]
    public async Task<ActionResult<IEnumerable<MessageTeacherRecipientDto>>> TeacherRecipients()
    {
        var teachers = await db.Teachers.AsNoTracking().Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.FullName })
            .ToListAsync();
        var chats = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.TeacherId != null && r.TeacherId != "")
                .Select(r => new { r.TeacherId, r.ChatId }).ToListAsync())
            .GroupBy(r => r.TeacherId!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().Count());
        return teachers.Select(t => new MessageTeacherRecipientDto(
            t.Id, t.FullName, chats.GetValueOrDefault(t.Id, 0))).ToList();
    }

    /// <summary>
    /// Barcha tayyor matnlar (birlashgan): qo'lda yoziladigan andozalar (db.SmsTemplates — jadval nomi
    /// tarixiy) + avto-xabar qoidalari shablonlari (db.AutoMessageRules, Template bo'sh bo'lmaganlari).
    /// </summary>
    [HttpGet("templates/all")]
    public async Task<ActionResult<IEnumerable<UnifiedTemplateDto>>> AllTemplates()
    {
        var sms = await db.SmsTemplates.AsNoTracking().OrderBy(t => t.Order).ThenBy(t => t.Name)
            .Select(t => new UnifiedTemplateDto("manual", t.Name, t.Text)).ToListAsync();
        // Katalogdan olib tashlangan (faqat-SMS lid) hodisalarning eski qoidalari tayyor matn bo'lib
        // chiqmaydi — ularning {link}/{natija} kabi tokenlarini e'lon to'ldira olmaydi.
        var autos = (await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Template != "")
                .OrderBy(r => r.Name)
                .Select(r => new { r.Trigger, r.Name, r.Template }).ToListAsync())
            .Where(r => AutoMessageTriggers.IsKnown(r.Trigger))
            .Select(r => new UnifiedTemplateDto("auto", r.Name, r.Template)).ToList();
        return sms.Concat(autos).ToList();
    }

    // ---------- Qo'lda yuboriladigan tayyor matnlar (andozalar) ----------

    [HttpGet("templates")]
    public async Task<ActionResult<IEnumerable<SmsTemplateDto>>> Templates()
    {
        var list = await db.SmsTemplates.OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync();
        return list.Select(t => new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order)).ToList();
    }

    // Avto-hodisalar YAGONA "Avto xabarlar" (AutoMessageRule) moduliga ko'chdi — andozalar faqat
    // QO'LDA yuborish uchun.

    [HttpPost("templates")]
    public async Task<ActionResult<SmsTemplateDto>> CreateTemplate(SaveSmsTemplateRequest req)
    {
        var name = (req.Name ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (name.Length == 0 || text.Length == 0) return BadRequest(new { message = "Nom va matn kerak" });
        var order = (await db.SmsTemplates.MaxAsync(t => (int?)t.Order) ?? 0) + 1;
        var t = new SmsTemplate { Name = name, Text = text, Order = order };
        db.SmsTemplates.Add(t);
        await db.SaveChangesAsync();
        return new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order);
    }

    [HttpPut("templates/{id}")]
    public async Task<ActionResult<SmsTemplateDto>> UpdateTemplate(string id, SaveSmsTemplateRequest req)
    {
        var t = await db.SmsTemplates.FindAsync(id);
        if (t is null) return NotFound();
        var name = (req.Name ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (name.Length == 0 || text.Length == 0) return BadRequest(new { message = "Nom va matn kerak" });
        t.Name = name; t.Text = text;
        await db.SaveChangesAsync();
        return new SmsTemplateDto(t.Id, t.Name, t.Text, t.Order);
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        var t = await db.SmsTemplates.FindAsync(id);
        if (t is null) return NotFound();
        db.SmsTemplates.Remove(t);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // (Lidga SMS — sms/lead, sms/lead-bulk — 2026-10 da olib tashlandi: lidning Telegram manzili yo'q.)

}
