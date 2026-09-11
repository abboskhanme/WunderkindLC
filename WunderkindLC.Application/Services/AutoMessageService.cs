using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YAGONA avto-xabar dispatcheri (singleton). Hodisa (trigger) yuz berganda shu hodisaga yoqilgan
/// <see cref="AutoMessageRule"/> qoidalarni topadi va har qoidada YOQILGAN kanallar (SMS / Push / Telegram)
/// bo'yicha shablonni render qilib yuboradi. Eski <c>AutoSmsService</c> (faqat SMS) + 3 ta eslatma
/// fon-xizmati (faqat push/telegram) o'rniga bitta markaz.
///
/// HECH QACHON tashqariga exception chiqarmaydi — asosiy oqim (to'lov/lid/davomat) buzilmaydi
/// (kanal xatosi loglanadi, davom etadi).
/// </summary>
public class AutoMessageService(
    EskizService eskiz,
    FcmService fcm,
    TelegramService telegram,
    CtiSmsService ctiSms,
    ILogger<AutoMessageService> logger)
{
    /// <summary>O'quvchi (ota-ona) uchun avto-xabar — SMS + Push + Telegram (qoidada yoqilganiga qarab).
    /// <paramref name="group"/> — hodisa QAYSI guruhga tegishli (masalan to'lov shu guruh uchun qilindi).
    /// Berilmasa o'quvchining asosiy (ClassName) guruhi olinadi. Guruh dars jadvali tokenlariga
    /// ({dars_kunlari}...) va "o'qituvchiga yuborish" auditoriyasiga ta'sir qiladi.</summary>
    public async Task DispatchStudentAsync(
        IAppDbContext db, string trigger, Student s,
        Dictionary<string, string>? extraTokens = null, Group? group = null, CancellationToken ct = default)
    {
        try
        {
            var rules = await RulesAsync(db, trigger, ct);
            if (rules.Count == 0) return;

            var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
            var centerName = meta?.Name ?? "";
            var fcmJson = AppSecrets.FcmServiceAccountJson;
            // Hodisa guruhi: chaqiruvchi bergan bo'lsa (to'lov qaysi guruhga tushgani) — o'sha;
            // aks holda o'quvchining asosiy guruhi (ClassName bo'yicha) — dars jadvali tokenlari uchun.
            var ctxGroup = group ?? (string.IsNullOrWhiteSpace(s.ClassName) ? null
                : await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName, ct));
            // {oqituvchi} tokeni — shu guruhning o'qituvchisi F.I.Sh (guruh/o'qituvchi bo'lmasa bo'sh).
            var groupTeacherName = await MessageTokenizer.GroupTeacherNameAsync(db, ctxGroup, s, ct);

            var deadTokens = new List<string>();
            var dirty = false;

            foreach (var rule in rules)
            {
                try
                {
                    var audienceStudent = rule.Audience == "students";
                    var audienceTeachers = rule.Audience == "teachers";

                    // O'qituvchi auditoriyasi: MATN o'quvchi haqida (Student tokenizer — o'zgarmaydi), lekin
                    // YETKAZISH guruh o'qituvchisiga bo'ladi. Guruh/o'qituvchi bo'lmasa — bu qoidani o'tkazib yubor.
                    Teacher? teacher = null;
                    if (audienceTeachers)
                    {
                        if (string.IsNullOrEmpty(ctxGroup?.TeacherId)) continue;
                        teacher = await db.Teachers.FindAsync(new object?[] { ctxGroup.TeacherId }, ct);
                        if (teacher is null) continue;
                    }

                    // Telefon: render ({telefon} tokeni) + o'quvchi/ota-ona auditoriyasi SMS manzili.
                    // O'quvchi auditoriyasi — o'z raqami birinchi; aks holda ota-ona birinchi.
                    var phone = audienceStudent
                        ? Coalesce(s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone)
                        : Coalesce(s.ParentPhone, s.FatherPhone, s.MotherPhone, s.Phone);

                    var withExtra = MessageTokenizer.ApplyExtra(rule.Template, extraTokens);
                    var msg = MessageTokenizer.Student(withExtra, s, s.ParentFullName, phone, centerName,
                        extra: null, group: ctxGroup, teacherName: groupTeacherName);
                    var title = Title(rule, trigger);
                    if (string.IsNullOrWhiteSpace(msg)) continue;

                    // Yetkazish manzili auditoriya bo'yicha (matn bir xil — faqat manzil o'zgaradi).
                    var smsPhone     = audienceTeachers ? teacher!.Phone    : phone;
                    var smsRecipient = audienceTeachers ? teacher!.FullName : s.FullName;
                    var pushUserId   = audienceTeachers ? teacher!.UserId   : s.UserId;

                    // SMS.
                    if (rule.SendSms && !string.IsNullOrWhiteSpace(smsPhone) && AutoMessageSmsSender.IsReady(rule.SmsProvider, meta, eskiz))
                        dirty |= await SendSmsAsync(db, trigger, rule.SmsProvider, smsPhone!, smsRecipient, rule.Template, msg, ct);

                    // Push (ilova akkaunti) + ichki bildirishnoma tarixi.
                    if (rule.SendPush && !string.IsNullOrWhiteSpace(pushUserId))
                    {
                        NotificationStore.Add(db, pushUserId!, title, msg, trigger);
                        dirty = true;
                        if (FcmService.IsConfigured(fcmJson))
                        {
                            var tokens = await db.DeviceTokens.Where(d => d.UserId == pushUserId)
                                .Select(d => d.Token).Distinct().ToListAsync(ct);
                            if (tokens.Count > 0)
                            {
                                var res = await fcm.SendAsync(fcmJson, tokens, title, msg, ct);
                                deadTokens.AddRange(res.InvalidTokens);
                            }
                        }
                    }

                    // Telegram (o'qituvchi auditoriyasida — o'qituvchi chatlari; aks holda o'quvchi chatlari).
                    if (rule.SendTelegram && telegram.IsConfigured)
                    {
                        var teacherId = teacher?.Id;
                        var chats = audienceTeachers
                            ? await db.TelegramRegistrations.Where(r => r.TeacherId == teacherId)
                                .Select(r => r.ChatId).Distinct().ToListAsync(ct)
                            : await db.TelegramRegistrations.Where(r => r.StudentId == s.Id)
                                .Select(r => r.ChatId).Distinct().ToListAsync(ct);
                        foreach (var chatId in chats)
                            await telegram.SendMessageAsync(chatId, $"🔔 {title}\n\n{msg}", ct: ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Avto-xabar ({Trigger}) o'quvchi {Id} qoida {Rule} — xatolik", trigger, s.Id, rule.Id);
                }
            }

            if (deadTokens.Count > 0)
            {
                db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => deadTokens.Contains(d.Token)));
                dirty = true;
            }
            if (dirty) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar ({Trigger}) o'quvchi {Id} — umumiy xatolik", trigger, s.Id);
        }
    }

    /// <summary>Lid uchun avto-xabar — FAQAT SMS (lidda ilova/telegram yo'q).</summary>
    public async Task DispatchLeadAsync(
        IAppDbContext db, string trigger, Lead lead,
        Dictionary<string, string>? extraTokens = null,
        Group? group = null, string? trialAt = null, CancellationToken ct = default)
    {
        try
        {
            var rules = await RulesAsync(db, trigger, ct);
            if (rules.Count == 0) return;

            var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
            var centerName = meta?.Name ?? "";
            var phone = Coalesce(lead.Phone, lead.FatherPhone, lead.MotherPhone);
            if (string.IsNullOrWhiteSpace(phone)) return;
            // {oqituvchi} — sinov darsi guruhining o'qituvchisi (guruh berilmasa bo'sh).
            var teacherName = await MessageTokenizer.GroupTeacherNameAsync(db, group, ct: ct);

            var dirty = false;
            foreach (var rule in rules)
            {
                if (!rule.SendSms) continue;
                if (!AutoMessageSmsSender.IsReady(rule.SmsProvider, meta, eskiz)) continue;
                try
                {
                    var withExtra = MessageTokenizer.ApplyExtra(rule.Template, extraTokens);
                    var msg = MessageTokenizer.Lead(withExtra, lead, phone, centerName, extra: null, group: group,
                        trialAt: trialAt, teacherName: teacherName);
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    dirty |= await SendSmsAsync(db, trigger, rule.SmsProvider, phone!, lead.FullName, rule.Template, msg, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Avto-xabar ({Trigger}) lid {Id} qoida {Rule} — xatolik", trigger, lead.Id, rule.Id);
                }
            }
            if (dirty) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar ({Trigger}) lid {Id} — umumiy xatolik", trigger, lead.Id);
        }
    }

    // (DispatchTeacherAsync olib tashlandi — hech qayerda chaqirilmasdi. O'qituvchi auditoriyasiga
    // yetkazish DispatchStudentAsync ichida (Audience=="teachers") allaqachon qo'llab-quvvatlanadi.)

    /// <summary>O'quvchi darsga kelmaganda (attendance_absent) ota-onaga xabar. {sana}=dars sanasi (dd.MM.yyyy),
    /// {guruh}=guruh nomi, {sabab}=davomat sababi. Jurnal (SetEntry) yoki ommaviy davomat muvaffaqiyatidan keyin chaqiriladi.</summary>
    /// <param name="group">Dars QAYSI guruhda bo'lgani — {oqituvchi} va {dars_*} tokenlari shundan
    /// (berilmasa o'quvchining asosiy guruhi olinadi).</param>
    public Task DispatchAttendanceAbsentAsync(
        IAppDbContext db, Student s, string groupName, string reasonName, string dateIso,
        Group? group = null, CancellationToken ct = default)
    {
        var sana = dateIso.Length >= 10 ? $"{dateIso[8..10]}.{dateIso[5..7]}.{dateIso[..4]}" : dateIso;
        return DispatchStudentAsync(db, AutoMessageTriggers.AttendanceAbsent, s, new Dictionary<string, string>
        {
            ["{sana}"] = sana,
            ["{guruh}"] = groupName,
            ["{sabab}"] = reasonName,
        }, group: group, ct: ct);
    }

    /// <summary>Yangi yaratilgan oylik hisoblar (monthly_charge) uchun ota-onaga xabar. (o'quvchi, oy)
    /// bo'yicha yig'iladi — bitta o'quvchiga bir oyda BITTA xabar (guruhlar summasi). {oy}=oy nomi,
    /// {summa}=shu oy hisobi, {qarzdorlik}=joriy balans (dispatcher Student tokenizeridan). Idempotent:
    /// faqat YANGI yozilgan hisoblar keladi (AccrueMonth mavjudlarni tashlab ketadi).</summary>
    public async Task DispatchMonthlyChargesAsync(
        IAppDbContext db, IReadOnlyCollection<(string StudentId, string Month, decimal Amount)> charges,
        CancellationToken ct = default)
    {
        try
        {
            if (charges.Count == 0) return;
            var hasRule = await db.AutoMessageRules.AnyAsync(
                r => r.Enabled && r.Trigger == AutoMessageTriggers.MonthlyCharge, ct);
            if (!hasRule) return;

            foreach (var g in charges.GroupBy(c => (c.StudentId, c.Month)))
            {
                var s = await db.Students.FindAsync(new object?[] { g.Key.StudentId }, ct);
                if (s is null || s.IsArchived) continue;
                var summa = g.Sum(x => x.Amount);
                var monthName = g.Key.Month.Length >= 7 && int.TryParse(g.Key.Month.Substring(5, 2), out var mm)
                    ? MessageTokenizer.MonthNameUz(mm) : "";
                await DispatchStudentAsync(db, AutoMessageTriggers.MonthlyCharge, s, new Dictionary<string, string>
                {
                    ["{summa}"] = MessageTokenizer.MoneyPlain(summa),
                    ["{oy}"] = monthName,
                }, ct: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar (monthly_charge) — umumiy xatolik");
        }
    }

    /// <summary>Lidga BIR MARTALIK daraja-test havolasini SMS qilib yuboradi (test_link hodisasi, {link}).
    /// Interaktiv amal (admin "Test yuborish") — natijani (yuborildimi + status + RequestId) qaytaradi,
    /// invite holatini saqlash uchun. Rule yo'q/o'chirilgan / Eskiz sozlanmagan / raqam yo'q ⇒ Ok=false.</summary>
    public async Task<(bool Ok, string Status, string RequestId)> SendLeadTestLinkAsync(
        IAppDbContext db, Lead lead, string link, CancellationToken ct = default)
    {
        try
        {
            var rule = (await RulesAsync(db, AutoMessageTriggers.TestLink, ct)).FirstOrDefault(r => r.SendSms);
            if (rule is null) return (false, "Avto-xabar qoidasi yo'q (test_link, SMS)", "");
            var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
            if (!AutoMessageSmsSender.IsReady(rule.SmsProvider, meta, eskiz))
                return (false, rule.SmsProvider == "local" ? "Local SMS yoqilmagan" : "Eskiz sozlanmagan", "");
            var phone = Coalesce(lead.Phone, lead.FatherPhone, lead.MotherPhone);
            if (string.IsNullOrWhiteSpace(phone)) return (false, "Lidda raqam yo'q", "");

            var withExtra = MessageTokenizer.ApplyExtra(rule.Template, new Dictionary<string, string> { ["{link}"] = link });
            var msg = MessageTokenizer.Lead(withExtra, lead, phone, meta?.Name ?? "");
            var result = await AutoMessageSmsSender.SendAsync(
                db, eskiz, ctiSms, rule.SmsProvider, phone!, lead.FullName, msg,
                "Daraja testi havolasi", ct, batchMessage: rule.Template);
            await db.SaveChangesAsync(ct);
            return (result.Ok, result.Status, result.RequestId);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, "");
        }
    }

    // ---------- Yordamchilar ----------

    private static Task<List<AutoMessageRule>> RulesAsync(IAppDbContext db, string trigger, CancellationToken ct) =>
        db.AutoMessageRules.Where(r => r.Enabled && r.Trigger == trigger).ToListAsync(ct);

    private static string Title(AutoMessageRule rule, string trigger) =>
        !string.IsNullOrWhiteSpace(rule.Name) ? rule.Name
        : (AutoMessageTriggers.Get(trigger)?.Label ?? "Xabar");

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Bitta raqamga SMS yuboradi (provider bo'yicha — eskiz|local) va jurnalga (SmsLog + SmsBatch)
    /// yozadi. SaveChanges CHAQIRUVCHIDA (dirty flag orqali). Har doim true qaytaradi — yozuv muvaffaqiyat/
    /// muvaffaqiyatsizlikdan qat'i nazar amalga oshadi (natija SmsLog.Status'da ko'rinadi).</summary>
    private async Task<bool> SendSmsAsync(
        IAppDbContext db, string trigger, string provider, string phone, string recipientName,
        string templateText, string message, CancellationToken ct)
    {
        var label = AutoMessageTriggers.Get(trigger)?.Label ?? "Avto";
        await AutoMessageSmsSender.SendAsync(
            db, eskiz, ctiSms, provider, phone, recipientName, message, label, ct, batchMessage: templateText);
        return true;
    }
}
