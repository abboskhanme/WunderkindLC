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
/// <see cref="AutoMessageTriggers"/> katalogidagi bitta hodisaga bog'lanadi. Yetkazish — kabinet
/// bildirishnomasi (har doim) + Telegram (<see cref="AutoMessageChannels"/>; SMS/push 2026-10 da olib
/// tashlandi). Haqiqiy yuborish: hodisa nuqtalarida <see cref="AutoMessageService"/> (yoki jadvalli
/// hodisalarda tegishli fon-xizmat) shu qoidalarni o'qib yuboradi.
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
            t.SupportsSchedule, t.SupportsSendScope, t.Audiences, t.DefaultAudience,
            t.DefaultTemplate, t.Category, t.TemplateOptional)).ToList();

    /// <summary>Xabar {token}lari katalogi — shablon tahrirlagichda "token qo'shish" ro'yxati.
    /// group: "student" | "common" | "event".</summary>
    [HttpGet("tokens")]
    public ActionResult<IEnumerable<MessageTokenDto>> Tokens() =>
        MessageTokenCatalog.All.Select(t => new MessageTokenDto(t.Token, t.Label, t.Group)).ToList();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AutoMessageRuleDto>>> List()
    {
        var list = await db.AutoMessageRules.OrderBy(r => r.CreatedAt).ToListAsync();
        // Katalogdan olib tashlangan hodisalarning qoidalari (2026-10: faqat-SMS lid hodisalari) bazada
        // qoladi, lekin hech qayerda ishlamaydi — ro'yxatda ko'rsatilmaydi (UI ularning formasini chiza olmaydi).
        return list.Where(r => AutoMessageTriggers.IsKnown(r.Trigger)).Select(ToDto).ToList();
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

    /// <summary>Keng tarqalgan hodisalar uchun TAYYOR Telegram qoidalari to'plamini yaratadi (katalog matni va
    /// auditoriyasi bilan). Trigger uchun allaqachon Telegram qoidasi bo'lsa (eski SMS qoidasi ham Telegram
    /// sanaladi — <see cref="AutoMessageChannels"/>) — o'tkazib yuboriladi. Yaratilgan sonini qaytaradi.</summary>
    [HttpPost("seed-standard")]
    public async Task<ActionResult<object>> SeedStandard()
    {
        string[] standard =
        {
            AutoMessageTriggers.PaymentReceived,
            AutoMessageTriggers.MonthlyCharge,
            AutoMessageTriggers.PaymentDebt,
            AutoMessageTriggers.AttendanceAbsent,
            AutoMessageTriggers.Birthday,
        };

        var existing = (await db.AutoMessageRules
                .Where(r => (r.SendTelegram || r.SendSms) && standard.Contains(r.Trigger))
                .Select(r => r.Trigger).ToListAsync())
            .ToHashSet();

        var created = 0;
        foreach (var key in standard)
        {
            if (existing.Contains(key)) continue;
            var info = AutoMessageTriggers.Get(key);
            if (info is null) continue;
            db.AutoMessageRules.Add(new AutoMessageRule
            {
                Trigger = key,
                Name = info.Label,
                Enabled = true,
                SendTelegram = true,
                Audience = info.DefaultAudience,
                Template = info.DefaultTemplate,
            });
            created++;
        }
        if (created > 0) await db.SaveChangesAsync();
        return new { created };
    }

    /// <summary>Qoidani so'rovdan to'ldiradi — audience/scope/schedule maydonlari faqat tegishli hodisada ma'noli.
    /// ⚠️ Eski SMS bayrog'i (<c>SendSms</c>) saqlashda TOZALANADI: shundan keyin Telegram'ni faqat
    /// <c>SendTelegram</c> hal qiladi (<see cref="AutoMessageChannels.Telegram"/>).</summary>
    private static void Apply(AutoMessageRule rule, SaveAutoMessageRuleRequest req, AutoMessageTriggers.TriggerInfo info)
    {
        rule.Trigger = req.Trigger;
        rule.Name = (req.Name ?? "").Trim();
        rule.Enabled = req.Enabled;
        rule.SendTelegram = req.SendTelegram;
        rule.SendSms = false;   // legacy (SMS removed 2026-10) — the admin's explicit choice wins from now on
        rule.Audience = info.Audiences.Contains(req.Audience) ? req.Audience : info.DefaultAudience;
        rule.Template = (req.Template ?? "").Trim();
        // Manfiy = darsdan OLDIN (uchrashuv talabi: «darsdan 1–1,5 soat oldin davomat olishni unutmang»).
        // Ilgari 0 ga qirqilardi — faqat dars boshlangach eslatish mumkin edi. Chegara ±3 soat.
        rule.OffsetMinutes = Math.Clamp(req.OffsetMinutes, -180, 180);
        rule.SendScope = info.SupportsSendScope
            ? (ReminderSendScopes.IsKnown(req.SendScope) ? req.SendScope : ReminderSendScopes.LessonStart)
            : "";
        rule.ScheduleType = req.ScheduleType == "monthly" ? "monthly" : "daily";
        rule.ScheduleTime = req.ScheduleTime;
        rule.ScheduleDayOfMonth = req.ScheduleDayOfMonth is >= 1 and <= 31 ? req.ScheduleDayOfMonth : 1;
    }

    private static string? Validate(SaveAutoMessageRuleRequest req, AutoMessageTriggers.TriggerInfo? info)
    {
        if (info is null) return "Noma'lum hodisa turi";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Nom kerak";
        // Kanal tekshiruvi YO'Q: kabinet bildirishnomasi har doim yoziladi, Telegram — ixtiyoriy.

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
        new(r.Id, r.Trigger, r.Name, r.Enabled, AutoMessageChannels.Telegram(r), r.Audience, r.Template,
            r.OffsetMinutes, r.SendScope, r.ScheduleType, r.ScheduleTime, r.ScheduleDayOfMonth,
            r.CreatedAt.ToString("o"));
}
