using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YAGONA avto-xabar dispatcheri (singleton). Hodisa (trigger) yuz berganda shu hodisaga yoqilgan
/// <see cref="AutoMessageRule"/> qoidalarni topadi va har qoida bo'yicha shablonni render qilib
/// yetkazadi: kabinet bildirishnomasi (har doim) + Telegram (<see cref="AutoMessageChannels"/>).
/// <para>2026-10: SMS, push va Local SMS kanallari olib tashlandi — Telegram yagona tashqi kanal.</para>
///
/// HECH QACHON tashqariga exception chiqarmaydi — asosiy oqim (to'lov/davomat) buzilmaydi
/// (kanal xatosi loglanadi, davom etadi).
///
/// <para>⚠️ CRM upgrade (2026-10-12, P2.5): Telegram endi TO'G'RIDAN-TO'G'RI yuborilmaydi — har xabar
/// <see cref="TelegramOutbox"/> qatori (oluvchi bo'yicha jurnal, «Qabul qildim» tugmasi, bloklagan chatlar,
/// restartga chidamli, tezlik chegarasi) va <see cref="MessageDeliveryWorker"/> yuboradi. Matn Telegram HTML
/// (<see cref="MessageRender"/> — qiymatlar escape qilinadi); kabinet qo'ng'iroqchasiga ODDIY matn yoziladi.</para>
/// </summary>
public class AutoMessageService(
    TelegramService telegram,
    ILogger<AutoMessageService> logger,
    IOutboxSignal? outbox = null)
{
    /// <summary>A rendered auto message longer than this (visible chars) is sent as plain text cut at 4 000.</summary>
    private const int MaxVisible = TelegramHtml.MaxText;

    /// <summary>O'quvchi (ota-ona) uchun avto-xabar — kabinet bildirishnomasi + Telegram (qoidaga qarab).
    /// <paramref name="group"/> — hodisa QAYSI guruhga tegishli (masalan to'lov shu guruh uchun qilindi).
    /// Berilmasa o'quvchining asosiy (ClassName) guruhi olinadi. Guruh dars jadvali tokenlariga
    /// ({dars_kunlari}...) va "o'qituvchiga yuborish" auditoriyasiga ta'sir qiladi.
    /// <para>⚠️ ARXIVDAGI o'quvchiga faqat <see cref="AutoMessageTriggers.ReachesArchived"/> hodisalari
    /// (to'lov kvitansiyasi) ketadi — davomat/baho/qo'shilish xabarlari YUBORILMAYDI (arxiv = o'quvchi
    /// ketdi; eski arxivlarda a'zolik tirik qolgan, jurnal ularni hali ko'rsatishi mumkin).</para></summary>
    /// <param name="eventKey">Natural key of the EVENT (e.g. attendance notice item id, test score, month): the
    /// outbox never queues the same <c>{trigger}:{rule}:{student}:{chat}:{eventKey}</c> twice. <c>null</c> = no dedupe.</param>
    /// <param name="sourceId">Originating record stored on the delivery rows (status lookups by source).</param>
    /// <param name="onlyRuleId">Restrict to ONE rule (scheduled triggers run rule by rule: birthday, rating_*).</param>
    /// <param name="attachmentId">Media that overrides the rule's own attachment (achievement photo).</param>
    /// <returns>Number of Telegram messages queued.</returns>
    public async Task<int> DispatchStudentAsync(
        IAppDbContext db, string trigger, Student s,
        Dictionary<string, string>? extraTokens = null, Group? group = null, CancellationToken ct = default,
        string? eventKey = null, string? sourceId = null, string? onlyRuleId = null, string? attachmentId = null)
    {
        var queued = 0;
        try
        {
            if (s.IsArchived && !AutoMessageTriggers.ReachesArchived(trigger)) return 0;
            var rules = await RulesAsync(db, trigger, ct);
            if (onlyRuleId is not null) rules = rules.Where(r => r.Id == onlyRuleId).ToList();
            if (rules.Count == 0) return 0;

            var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
            var centerName = meta?.Name ?? "";
            // Hodisa guruhi: chaqiruvchi bergan bo'lsa (to'lov qaysi guruhga tushgani) — o'sha;
            // aks holda o'quvchining asosiy guruhi (ClassName bo'yicha) — dars jadvali tokenlari uchun.
            var ctxGroup = group ?? (string.IsNullOrWhiteSpace(s.ClassName) ? null
                : await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName, ct));
            // {oqituvchi} tokeni — shu guruhning o'qituvchisi F.I.Sh (guruh/o'qituvchi bo'lmasa bo'sh).
            var groupTeacherName = await MessageTokenizer.GroupTeacherNameAsync(db, ctxGroup, s, ct);

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

                    // {telefon} tokeni auditoriyaga qarab (MessagingAudience.StudentPhone): «students» — o'quvchining
                    // o'z raqami birinchi, aks holda ota-ona; Telegram'da — o'sha chatning raqami.
                    var title = Title(rule, trigger);
                    // Kabinet (qo'ng'iroqcha) — ODDIY matn (HTML belgilari ko'rinmasin).
                    var (cabinetHtml, _) = Render(rule.Template, s, null, ctxGroup, groupTeacherName, extraTokens, centerName, rule.Audience);
                    var cabinetText = TelegramHtml.ToPlain(cabinetHtml);
                    if (string.IsNullOrWhiteSpace(cabinetText)) continue;

                    // Kabinet bildirishnomasi (o'quvchi/o'qituvchi web kabineti, qo'ng'iroqcha) — HAR DOIM
                    // (ilgari faqat push yoqilgan qoidada yozilardi; push 2026-10 da olib tashlandi).
                    var cabinetUserId = audienceTeachers ? teacher!.UserId : s.UserId;
                    if (!string.IsNullOrWhiteSpace(cabinetUserId))
                    {
                        NotificationStore.Add(db, cabinetUserId!, title, cabinetText, trigger);
                        dirty = true;
                    }

                    // Telegram — OUTBOX orqali (o'qituvchi auditoriyasida — o'qituvchi chatlari; aks holda
                    // o'quvchining chatlari: «parents» — ota-ona chatlari, «students» — o'quvchining o'z chati).
                    if (!AutoMessageChannels.Telegram(rule)) continue;
                    var regs = audienceTeachers
                        ? await db.TelegramRegistrations.AsNoTracking()
                            .Where(r => r.TeacherId == teacher!.Id && r.BlockedAt == null).ToListAsync(ct)
                        : (await db.TelegramRegistrations.AsNoTracking()
                            .Where(r => r.StudentId == s.Id && r.BlockedAt == null).ToListAsync(ct))
                            .Where(r => audienceStudent
                                ? r.Relation == TelegramRelations.StudentSelf
                                : TelegramRelations.IsParent(r.Relation))
                            .ToList();
                    var header = HeaderFor(rule, trigger);
                    foreach (var reg in regs.GroupBy(r => r.ChatId).Select(g => g.First()))
                    {
                        var (html, errors) = Render(rule.Template, s, audienceTeachers ? null : reg, ctxGroup,
                            groupTeacherName, extraTokens, centerName, rule.Audience);
                        var text = header + html;
                        if (errors.Count > 0 || TelegramHtml.VisibleLength(text) > MaxVisible)
                        {
                            if (TelegramHtml.VisibleLength(text) > MaxVisible)
                                logger.LogWarning("Avto-xabar juda uzun — oddiy matn qilib qirqildi: qoida {Rule}, o'quvchi {Id}", rule.Id, s.Id);
                            text = TelegramHtml.PlainCut(text, 4000);
                        }
                        var row = await TelegramOutbox.EnqueueAsync(db, new OutboxItem(
                            TelegramOutbox.SourceAuto, trigger, reg.ChatId, text,
                            StudentId: s.Id, TeacherId: audienceTeachers ? teacher!.Id : null, RegistrationId: reg.Id,
                            Relation: audienceTeachers ? "" : TelegramRelations.Effective(reg.Relation),
                            RecipientName: audienceTeachers ? teacher!.FullName : s.FullName,
                            SourceId: sourceId ?? rule.Id,
                            AttachmentId: attachmentId ?? rule.AttachmentId,
                            AckRequired: rule.AckButton,
                            DedupeKey: eventKey is null ? null : $"{trigger}:{rule.Id}:{s.Id}:{reg.ChatId}:{eventKey}"), ct);
                        if (row is null) continue;
                        queued++;
                        dirty = true;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Avto-xabar ({Trigger}) o'quvchi {Id} qoida {Rule} — xatolik", trigger, s.Id, rule.Id);
                }
            }

            if (dirty) await TelegramOutbox.SaveAsync(db, ct);
            if (queued > 0) outbox?.Wake();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar ({Trigger}) o'quvchi {Id} — umumiy xatolik", trigger, s.Id);
            return 0;
        }
        return queued;
    }

    /// <summary>
    /// «01» — thank-you to the chat that has JUST connected to the bot (<c>bot_connected</c>, SPEC P2.4): ONE message
    /// per enabled rule to THAT chat only (not to the student's other chats), rendered for the student with
    /// {qarindosh} = the chat's relation. Dedupe <c>bot_connected:{rule}:{student}:{chat}</c> — never twice.
    /// Archived students get nothing. Saves and wakes the outbox. Returns messages queued.
    /// </summary>
    public async Task<int> DispatchBotConnectedAsync(
        IAppDbContext db, Student s, TelegramRegistration reg, CancellationToken ct = default)
    {
        try
        {
            if (s.IsArchived) return 0;
            var rules = await RulesAsync(db, AutoMessageTriggers.BotConnected, ct);
            if (rules.Count == 0) return 0;
            var centerName = (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.Name ?? "";
            var queued = 0;
            foreach (var rule in rules)
            {
                var (html, errors) = MessageRender.RenderStudent(rule.Template, s, reg, null, null, null, centerName);
                var text = errors.Count > 0 ? TelegramHtml.PlainCut(html, 4000) : html;
                if (TelegramHtml.VisibleLength(text) == 0) continue;
                var row = await TelegramOutbox.EnqueueAsync(db, new OutboxItem(
                    TelegramOutbox.SourceAuto, AutoMessageTriggers.BotConnected, reg.ChatId, text,
                    StudentId: s.Id, RegistrationId: reg.Id, Relation: TelegramRelations.Effective(reg.Relation),
                    RecipientName: s.FullName, SourceId: rule.Id, AttachmentId: rule.AttachmentId,
                    AckRequired: rule.AckButton,
                    DedupeKey: $"{AutoMessageTriggers.BotConnected}:{rule.Id}:{s.Id}:{reg.ChatId}"), ct);
                if (row is not null) queued++;
            }
            if (queued > 0)
            {
                await TelegramOutbox.SaveAsync(db, ct);
                outbox?.Wake();
            }
            return queued;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar (bot_connected) o'quvchi {Id} — xatolik", s.Id);
            return 0;
        }
    }

    /// <summary>Renders a rule template (Telegram HTML). A template with markup errors (legacy text with a stray
    /// tag) is rendered as escaped plain text — an auto message must still go out.</summary>
    private static (string Html, IReadOnlyList<string> Errors) Render(
        string template, Student s, TelegramRegistration? reg, Group? group, string? teacherName,
        IReadOnlyDictionary<string, string>? extra, string centerName, string audience)
    {
        var r = MessageRender.RenderStudent(template, s, reg, group, teacherName, extra, centerName, audience);
        if (r.Errors.Count == 0) return r;
        var plain = MessageRender.RenderStudent(TelegramHtml.Escape(template), s, reg, group, teacherName, extra, centerName, audience);
        return (plain.Html, Array.Empty<string>());
    }

    /// <summary>Telegram header: debt reminders «💰 To'lov eslatmasi», other triggers «🔔 {rule name}».</summary>
    private static string HeaderFor(AutoMessageRule rule, string trigger) =>
        trigger == AutoMessageTriggers.PaymentDebt ? MessageRender.HeaderDebt : MessageRender.HeaderAuto(Title(rule, trigger));

    // (DispatchLeadAsync olib tashlandi 2026-10: lid hodisalari — yangi lid, sinov darsi eslatmasi,
    // daraja testi havolasi va natijasi — faqat lid telefoniga SMS edi; lidning Telegram manzili yo'q.)

    // (DispatchTeacherAsync olib tashlandi — hech qayerda chaqirilmasdi. O'qituvchi auditoriyasiga
    // yetkazish DispatchStudentAsync ichida (Audience=="teachers") allaqachon qo'llab-quvvatlanadi.)

    /// <summary>O'quvchi darsga kelmaganda (attendance_absent) ota-onaga xabar. {sana}=dars sanasi (dd.MM.yyyy),
    /// {guruh}=guruh nomi, {sabab}=davomat sababi. Jurnal (SetEntry) yoki ommaviy davomat muvaffaqiyatidan keyin chaqiriladi.</summary>
    /// <param name="group">Dars QAYSI guruhda bo'lgani — {oqituvchi} va {dars_*} tokenlari shundan
    /// (berilmasa o'quvchining asosiy guruhi olinadi).</param>
    /// <param name="eventKey">Outbox dedupe key of the notice (Phase 3: the absent-notice queue item id).</param>
    /// <param name="sourceId">Originating record (Phase 3: the queue item id — the control page reads delivery status by it).</param>
    public Task<int> DispatchAttendanceAbsentAsync(
        IAppDbContext db, Student s, string groupName, string reasonName, string dateIso,
        Group? group = null, CancellationToken ct = default, string? eventKey = null, string? sourceId = null)
    {
        var sana = dateIso.Length >= 10 ? $"{dateIso[8..10]}.{dateIso[5..7]}.{dateIso[..4]}" : dateIso;
        return DispatchStudentAsync(db, AutoMessageTriggers.AttendanceAbsent, s, new Dictionary<string, string>
        {
            ["{sana}"] = sana,
            ["{guruh}"] = groupName,
            ["{sabab}"] = reasonName,
        }, group: group, ct: ct, eventKey: eventKey, sourceId: sourceId);
    }

    /// <summary>
    /// O'quvchi darsga KELGANDA (attendance_present) ota-onaga xabar — faqat SHU so'rovda yangi «keldi»
    /// bo'lganlarga: <paramref name="wasPresent"/> — saqlashdan OLDIN allaqachon «keldi» bo'lganlar
    /// (<see cref="JournalService.PresentStudentIdsAsync"/>). Qayta saqlash yoki qayta «hammasi keldi»
    /// takror xabar yubormaydi. Qoida yoqilmagan bo'lsa hech narsa o'qilmaydi. Xato yutiladi —
    /// davomat javobini bloklamaydi (`attendance_absent` bilan bir xil).
    /// </summary>
    public async Task DispatchNewlyPresentAsync(
        IAppDbContext db, string classId, string subjectId, string dateIso, int period,
        IReadOnlyCollection<string> studentIds, IReadOnlySet<string> wasPresent, CancellationToken ct = default)
    {
        try
        {
            if (studentIds.Count == 0) return;
            if (!await db.AutoMessageRules.AnyAsync(r => r.Enabled && r.Trigger == AutoMessageTriggers.AttendancePresent, ct))
                return;
            var now = await JournalService.PresentStudentIdsAsync(db, classId, subjectId, dateIso, period, studentIds);
            var fresh = now.Where(id => !wasPresent.Contains(id)).ToList();
            if (fresh.Count == 0) return;
            var cls = await db.Classes.FindAsync(new object[] { classId }, ct);
            var sana = dateIso.Length >= 10 ? $"{dateIso[8..10]}.{dateIso[5..7]}.{dateIso[..4]}" : dateIso;
            foreach (var sid in fresh)
            {
                var s = await db.Students.FindAsync(new object[] { sid }, ct);
                if (s is null) continue;
                await DispatchStudentAsync(db, AutoMessageTriggers.AttendancePresent, s, new Dictionary<string, string>
                {
                    ["{sana}"] = sana,
                    ["{guruh}"] = cls?.Name ?? "",
                }, group: cls, ct: ct);
            }
        }
        catch (Exception ex)
        {
            // Avto-xabar davomatni to'xtatmaydi (Telegram nosozligi jurnal xatosi emas) — lekin JIM
            // emas: ilgari xato umuman yozilmas, "keldi" xabari nega ketmagani hech qayerda ko'rinmasdi.
            logger.LogWarning(ex, "Avto-xabar (attendance_present) guruh {ClassId} {Date} — xatolik", classId, dateIso);
        }
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

    // ---------- Yordamchilar ----------

    private static Task<List<AutoMessageRule>> RulesAsync(IAppDbContext db, string trigger, CancellationToken ct) =>
        db.AutoMessageRules.Where(r => r.Enabled && r.Trigger == trigger).ToListAsync(ct);

    private static string Title(AutoMessageRule rule, string trigger) =>
        !string.IsNullOrWhiteSpace(rule.Name) ? rule.Name
        : (AutoMessageTriggers.Get(trigger)?.Label ?? "Xabar");

    /// <summary>The Telegram bot the outbox sends through (kept for DI compatibility of the constructor).</summary>
    public bool TelegramConfigured => telegram.IsConfigured;
}
