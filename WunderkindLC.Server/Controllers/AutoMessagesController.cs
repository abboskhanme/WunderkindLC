using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// "Xabarlar → Avto xabarlar" — YAGONA avto-xabar qoidalari CRUD'i. Har qoida
/// <see cref="AutoMessageTriggers"/> katalogidagi bitta hodisaga bog'lanadi va SMS / Push / Telegram
/// kanallarini mustaqil yoqadi. Haqiqiy yuborish: hodisa nuqtalarida <see cref="AutoMessageService"/>
/// (yoki jadvalli hodisalarda tegishli fon-xizmat) shu qoidalarni o'qib yuboradi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("messages.broadcast")]
[Route("api/admin/auto-messages")]
public class AutoMessagesController(AppDbContext db) : ControllerBase
{
    /// <summary>Hodisa katalogi (frontend forma shundan quriladi). category — guruhlash toifasi.</summary>
    [HttpGet("triggers")]
    public ActionResult<IEnumerable<AutoMessageTriggerInfoDto>> Triggers() =>
        AutoMessageTriggers.All.Select(t => new AutoMessageTriggerInfoDto(
            t.Key, t.Label, t.Description, t.Tokens,
            new AutoMessageChannelsDto(t.Sms, t.Push, t.Telegram),
            t.SupportsSchedule, t.SupportsSendScope, t.Audiences, t.DefaultAudience,
            t.DefaultTemplate, t.Category, t.TemplateOptional)).ToList();

    /// <summary>Xabar {token}lari katalogi — shablon tahrirlagichda "token qo'shish" ro'yxati.
    /// group: "student" | "lead" | "common" | "event".</summary>
    [HttpGet("tokens")]
    public ActionResult<IEnumerable<MessageTokenDto>> Tokens() =>
        MessageTokenCatalog.All.Select(t => new MessageTokenDto(t.Token, t.Label, t.Group)).ToList();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AutoMessageRuleDto>>> List()
    {
        var list = await db.AutoMessageRules.OrderBy(r => r.CreatedAt).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<AutoMessageRuleDto>> Create(SaveAutoMessageRuleRequest req)
    {
        var info = AutoMessageTriggers.Get(req.Trigger);
        var error = Validate(req, info);
        if (error is not null) return BadRequest(new { message = error });

        var rule = new AutoMessageRule();
        Apply(rule, req, info!);
        db.AutoMessageRules.Add(rule);
        await db.SaveChangesAsync();
        return ToDto(rule);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AutoMessageRuleDto>> Update(string id, SaveAutoMessageRuleRequest req)
    {
        var rule = await db.AutoMessageRules.FindAsync(id);
        if (rule is null) return NotFound();
        var info = AutoMessageTriggers.Get(req.Trigger);
        var error = Validate(req, info);
        if (error is not null) return BadRequest(new { message = error });

        Apply(rule, req, info!);
        await db.SaveChangesAsync();
        return ToDto(rule);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var rule = await db.AutoMessageRules.FindAsync(id);
        if (rule is null) return NotFound();
        db.AutoMessageRules.Remove(rule);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Keng tarqalgan hodisalar uchun TAYYOR SMS habarlar to'plamini yaratadi (har biri bitta kanal — SMS).
    /// Trigger uchun allaqachon SMS qoidasi bo'lsa — o'tkazib yuboriladi (dublikat qilmaydi). Yaratilgan sonini qaytaradi.</summary>
    [HttpPost("seed-sms")]
    public async Task<ActionResult<object>> SeedStandardSms()
    {
        // Standart SMS to'plami — katalog matnlari (DefaultTemplate) va auditoriyasi bilan.
        string[] standard =
        {
            AutoMessageTriggers.PaymentReceived,
            AutoMessageTriggers.MonthlyCharge,
            AutoMessageTriggers.PaymentDebt,
            AutoMessageTriggers.AttendanceAbsent,
            AutoMessageTriggers.Birthday,
            AutoMessageTriggers.LeadNew,
            AutoMessageTriggers.TrialReminder,
        };

        // Shu triggerlar uchun ALLAQACHON SMS qoidasi bor bo'lsa — takrorlamaymiz.
        var existingSmsTriggers = (await db.AutoMessageRules
                .Where(r => r.SendSms && standard.Contains(r.Trigger))
                .Select(r => r.Trigger).ToListAsync())
            .ToHashSet();

        var created = 0;
        foreach (var key in standard)
        {
            if (existingSmsTriggers.Contains(key)) continue;
            var info = AutoMessageTriggers.Get(key);
            if (info is null) continue;
            db.AutoMessageRules.Add(new AutoMessageRule
            {
                Trigger = key,
                Name = info.Label,
                Enabled = true,
                SendSms = true, SendPush = false, SendTelegram = false,
                SmsProvider = "eskiz",
                Audience = info.DefaultAudience,
                Template = info.DefaultTemplate,
            });
            created++;
        }
        if (created > 0) await db.SaveChangesAsync();
        return new { created };
    }

    /// <summary>Qoidani so'rovdan to'ldiradi — kanallar hodisada MAVJUD bo'lganlari bilan cheklanadi,
    /// audience/scope/schedule maydonlari faqat tegishli hodisada ma'noli.</summary>
    private static void Apply(AutoMessageRule rule, SaveAutoMessageRuleRequest req, AutoMessageTriggers.TriggerInfo info)
    {
        rule.Trigger = req.Trigger;
        rule.Name = (req.Name ?? "").Trim();
        rule.Enabled = req.Enabled;
        // Faqat hodisada mavjud kanallar yoqiladi.
        rule.SendSms = req.SendSms && info.Sms;
        rule.SendPush = req.SendPush && info.Push;
        rule.SendTelegram = req.SendTelegram && info.Telegram;
        rule.Audience = info.Audiences.Contains(req.Audience) ? req.Audience : info.DefaultAudience;
        rule.Template = (req.Template ?? "").Trim();
        rule.OffsetMinutes = req.OffsetMinutes < 0 ? 0 : req.OffsetMinutes;
        rule.SendScope = info.SupportsSendScope
            ? (ReminderSendScopes.IsKnown(req.SendScope) ? req.SendScope : ReminderSendScopes.LessonStart)
            : "";
        rule.ScheduleType = req.ScheduleType == "monthly" ? "monthly" : "daily";
        rule.ScheduleTime = req.ScheduleTime;
        rule.ScheduleDayOfMonth = req.ScheduleDayOfMonth is >= 1 and <= 31 ? req.ScheduleDayOfMonth : 1;
        rule.SmsProvider = req.SmsProvider == "local" ? "local" : "eskiz";
    }

    private static string? Validate(SaveAutoMessageRuleRequest req, AutoMessageTriggers.TriggerInfo? info)
    {
        if (info is null) return "Noma'lum hodisa turi";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Nom kerak";

        // Kamida bitta MAVJUD kanal yoqilgan bo'lsin.
        var anyChannel = (req.SendSms && info.Sms) || (req.SendPush && info.Push) || (req.SendTelegram && info.Telegram);
        if (!anyChannel) return "Kamida bitta kanal (SMS/Push/Telegram) yoqilishi kerak";

        // Shablon: hodisa tokenlar ishlatadigan bo'lsa kerak — LEKIN matn ixtiyoriy hodisalarda (masalan
        // qarzdorlik eslatmasi — bo'sh qoldirilsa tizim matnni o'zi tuzadi) bo'sh bo'lishi mumkin.
        if (info.Tokens.Length > 0 && !info.TemplateOptional && string.IsNullOrWhiteSpace(req.Template))
            return "Xabar matni (shablon) kerak";

        if (info.SupportsSendScope)
        {
            var scope = string.IsNullOrWhiteSpace(req.SendScope) ? ReminderSendScopes.LessonStart : req.SendScope;
            if (!ReminderSendScopes.IsKnown(scope)) return "Yuborish rejimi noto'g'ri";
            if (scope != ReminderSendScopes.LessonStart && !System.TimeSpan.TryParse(req.ScheduleTime, out _))
                return "Yuborish vaqti formati noto'g'ri (HH:mm)";
        }
        if (info.SupportsSchedule)
        {
            if (req.ScheduleType != "daily" && req.ScheduleType != "monthly")
                return "Jadval turi noto'g'ri";
            if (!System.TimeSpan.TryParse(req.ScheduleTime, out _))
                return "Vaqt formati noto'g'ri (HH:mm)";
            if (req.ScheduleType == "monthly" && req.ScheduleDayOfMonth is < 1 or > 31)
                return "Oyning kuni 1-31 oralig'ida bo'lishi kerak";
        }
        return null;
    }

    private static AutoMessageRuleDto ToDto(AutoMessageRule r) =>
        new(r.Id, r.Trigger, r.Name, r.Enabled, r.SendSms, r.SendPush, r.SendTelegram, r.Audience, r.Template,
            r.OffsetMinutes, r.SendScope, r.ScheduleType, r.ScheduleTime, r.ScheduleDayOfMonth,
            r.CreatedAt.ToString("o"), r.SmsProvider);
}
