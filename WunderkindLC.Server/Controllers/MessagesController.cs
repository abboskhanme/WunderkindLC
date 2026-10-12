using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
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

    [HttpPost("broadcast")]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni kerak" });
        var scope = (req.Scope ?? "class").Trim().ToLowerInvariant();

        // Qamrov bo'yicha maqsadli o'quvchilar to'plami.
        var studentsQ = db.Students.AsQueryable();
        var teacherIds = scope == "selected"
            ? (req.TeacherIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList()
            : new List<string>();
        string audience;
        string? groupScope = null;   // "Guruh" qamrovi — token konteksti ({guruh}, dars jadvali) shu guruhdan
        switch (scope)
        {
            case "selected":
                var ids = req.StudentIds ?? new();
                if (ids.Count == 0 && teacherIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
                studentsQ = studentsQ.Where(s => ids.Contains(s.Id));
                audience = $"Tanlangan ({ids.Count + teacherIds.Count})";
                break;
            case "all":
                audience = "Barcha guruhlar";
                break;
            default: // class
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length == 0) return BadRequest(new { message = "Guruh kerak" });
                // A'ZOLIK bo'yicha (eskirgan `ClassName` emas) — guruhlar ro'yxatidagi sanoq bilan bir xil.
                var memberIds = await MessagingAudience.GroupAudienceAsync(db, cn);
                studentsQ = studentsQ.Where(s => memberIds.Contains(s.Id));
                audience = cn;
                groupScope = cn;
                break;
        }

        var students = await studentsQ.ToListAsync();
        if (req.OnlyDebtors)
        {
            students = students.Where(s => s.Balance < 0).ToList();
            audience += " — qarzdorlar";
        }
        // OMMAVIY e'lon: arxivdagi va guruhi YOPILGAN/TUGATILGAN o'quvchilar chiqarib tashlanadi
        // ("Tanlangan" rejimida admin kimni tanlagan bo'lsa — o'shanga yuboriladi).
        if (scope != "selected")
        {
            var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db);
            students = students.Where(s => !s.IsArchived && !closed.Contains(s.Id)).ToList();
        }
        var byId = students.ToDictionary(s => s.Id);
        var sids = students.Select(s => s.Id).ToList();

        // Har bir o'quvchi (ro'yxatdagi chat) uchun matn alohida moslashtiriladi (mail-merge).
        var regs = await db.TelegramRegistrations
            .Where(r => sids.Contains(r.StudentId))
            .ToListAsync();

        // "Tanlab" rejimida tanlangan o'qituvchilarning Telegram registratsiyalari — alohida yuboriladi.
        var teacherRegs = teacherIds.Count > 0
            ? await db.TelegramRegistrations.Where(r => r.TeacherId != null && teacherIds.Contains(r.TeacherId)).ToListAsync()
            : new List<TelegramRegistration>();
        var teachersById = teacherRegs.Count > 0
            ? await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id)
            : new Dictionary<string, Teacher>();

        var centerName = (await db.CenterMeta.FirstOrDefaultAsync())?.Name ?? "";
        var groupByName = await GroupByNameAsync();
        var teacherNames = await TeacherNamesAsync();
        var sent = 0;
        var scopeGroup = groupScope is null ? null : groupByName.GetValueOrDefault(groupScope);
        var scopedText = GroupScoped(text, groupScope);
        foreach (var r in regs)
        {
            if (!byId.TryGetValue(r.StudentId, out var s)) continue;
            var grp = scopeGroup ?? groupByName.GetValueOrDefault(s.ClassName ?? "");
            var message = $"📢 Markaz e'loni\n\n{Personalize(scopedText, s, r, centerName, grp, MessageTokenizer.TeacherNameOf(grp, teacherNames))}";
            if (await telegram.SendMessageAsync(r.ChatId, message)) sent++;
        }
        foreach (var r in teacherRegs)
        {
            if (!teachersById.TryGetValue(r.TeacherId!, out var t)) continue;
            var message = $"📢 Markaz e'loni\n\n{MessageTokenizer.Teacher(text, t, centerName)}";
            if (await telegram.SendMessageAsync(r.ChatId, message)) sent++;
        }

        var user = await db.Users.FindAsync(Uid);
        var bc = new Broadcast
        {
            ClassName = audience,
            Text = text,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = AppClock.Now,
            RecipientCount = regs.Count + teacherRegs.Count,
            SentCount = sent,
        };
        db.Broadcasts.Add(bc);
        await db.SaveChangesAsync();

        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName,
            bc.CreatedAt.ToString("o"), bc.RecipientCount, bc.SentCount);
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
