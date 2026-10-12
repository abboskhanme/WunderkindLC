using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Avtomatik to'lov eslatmasi (fon xizmati, trigger <c>payment_debt</c>).
///
/// <para>⚠️ <b>JADVAL — HAR QOIDANING O'ZIDAN</b> (CRM upgrade 2026-10-12, P2.5): har yoqilgan qoida oyning
/// O'Z kunida (<c>ScheduleDayOfMonth</c>, 31 → qisqa oyda oxirgi kun) va O'Z soatida (<c>ScheduleTime</c>)
/// ishlaydi (<see cref="AutoMessageSchedule.IsDue"/>). Ko'proq kun kerak bo'lsa — ko'proq qoida. Eski "1-sanada
/// hamma, keyin har 2 kunda (toq kunlar) 09:00" jadvali OLIB TASHLANDI. Xizmat har 10 daqiqada uyg'onadi.</para>
///
/// <para>⚠️ XABAR HAR FAN (kurs/guruh) UCHUN ALOHIDA ketadi (<see cref="DebtMessages"/>): o'quvchi ikki fanda o'qisa
/// ota-onaga ikkita alohida eslatma boradi.</para>
///
/// <para>Kanal: kabinet bildirishnomasi (har doim) + Telegram — <see cref="TelegramOutbox"/> orqali (oluvchi bo'yicha
/// jurnal, «Qabul qildim» tugmasi, bloklagan chatlar). Dedupe kaliti
/// <c>payment_debt:{ruleId}:{studentId}:{groupId|none}:{chatId}:{yyyy-MM-dd}</c>.</para>
///
/// <para>⚠️ <b>"BUGUN ISHLADIMI" — BAZADA</b> (<see cref="JobRunMarker"/>): har o'quvchi yuborishdan OLDIN band
/// qilinadi (<c>payment_debt:{ruleId}:{studentId}</c>), qoida to'liq tugagach <c>payment_debt:{ruleId}</c> belgisi
/// qo'yiladi — restart takrorlamaydi, ish yarmida yiqilsa qolganlarga keyingi uyg'onishda yetadi.</para>
///
/// <para>⚠️ <b>AUDITORIYA</b> qoidaning o'zidan: <c>parents</c> — ota-ona chatlari; <c>students</c> — o'quvchining
/// o'z chati; <c>teachers</c> — SHU FAN guruhining o'qituvchisi.</para>
/// </summary>
public class PaymentReminderService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<PaymentReminderService> logger,
    IOutboxSignal? outbox = null) : BackgroundService
{
    /// <summary>Kalitlar prefiksi: qoida — <c>payment_debt:{ruleId}</c>, oluvchi — <c>payment_debt:{ruleId}:{studentId}</c>.</summary>
    public const string JobKey = AutoMessageTriggers.PaymentDebt;

    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(AppClock.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "To'lov eslatmasi siklida xatolik");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Bitta uyg'onish: vaqti kelgan (<see cref="AutoMessageSchedule.IsDue"/>) va bugun BAJARILMAGAN har qoida
    /// ishlaydi. Qoida yarmida yiqilsa belgisi qo'yilmaydi — keyingi uyg'onishda qolganlarga davom etadi.
    /// </summary>
    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        List<AutoMessageRule> rules;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            rules = await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Trigger == AutoMessageTriggers.PaymentDebt && r.Enabled).ToListAsync(ct);
        }

        foreach (var rule in rules.Where(r => AutoMessageSchedule.IsDue(r, now)))
        {
            var key = JobRunMarker.For(JobKey, rule.Id);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                if (await JobRunMarker.IsDoneAsync(db, key, today, ct)) continue;
            }
            if (!await RunRuleAsync(rule.Id, today, ct)) continue;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                await JobRunMarker.TryClaimAsync(db, key, today, ct);
                // Oluvchi belgilari faqat O'Z sanasida ma'noli — eskilari tozalanadi.
                await JobRunMarker.PruneAsync(db, JobKey + ":", today.AddDays(-2), ct);
            }
        }
    }

    /// <summary>Bugungi KUNGA belgilangan barcha yoqilgan qoidalarni (soatidan qat'i nazar) ishga tushiradi —
    /// qo'lda/test yo'li. Natija — hammasi TO'LIQ bajarildimi.</summary>
    private async Task<bool> RunDailyAsync(DateOnly today, CancellationToken ct)
    {
        List<AutoMessageRule> rules;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            rules = await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Trigger == AutoMessageTriggers.PaymentDebt && r.Enabled).ToListAsync(ct);
        }
        if (rules.Count == 0)
        {
            logger.LogInformation("To'lov eslatmasi: yoqilgan qoida yo'q — yuborilmadi.");
            return true;
        }
        var ok = true;
        foreach (var rule in rules)
        {
            var monthly = new AutoMessageRule { ScheduleType = "monthly", ScheduleDayOfMonth = rule.ScheduleDayOfMonth };
            if (!CustomReminderService.IsScheduledDay(monthly, today)) continue;
            ok &= await RunRuleAsync(rule.Id, today, ct);
        }
        return ok;
    }

    /// <summary>
    /// BITTA qoida bo'yicha bugungi eslatmalar. Natija — kunlik ish TO'LIQ bajarildimi: <c>false</c> — kamida bitta
    /// qarzdor oluvchi belgisi QO'YILMASDAN xatoga uchradi (keyingi uyg'onishda faqat shu qolganlar uriniladi).
    /// </summary>
    private async Task<bool> RunRuleAsync(string ruleId, DateOnly today, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var rule = await db.AutoMessageRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.Enabled && r.Trigger == AutoMessageTriggers.PaymentDebt, ct);
        if (rule is null) return true;
        var ruleKey = JobRunMarker.For(JobKey, rule.Id);
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var centerName = meta?.Name ?? "";

        // Qarzdorlar: balansi manfiy, arxivlanmagan o'quvchilar; guruhi YOPILGAN/TUGATILGANlar chiqariladi.
        var debtors = await db.Students.Where(s => !s.IsArchived && s.Balance < 0).ToListAsync(ct);
        var closedGroupStudents = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        var skipped = debtors.Count(s => closedGroupStudents.Contains(s.Id));
        if (skipped > 0)
        {
            debtors = debtors.Where(s => !closedGroupStudents.Contains(s.Id)).ToList();
            logger.LogInformation("To'lov eslatmasi: {Count} qarzdor o'tkazib yuborildi (guruhi yopilgan/tugatilgan).", skipped);
        }
        if (debtors.Count == 0)
        {
            logger.LogInformation("To'lov eslatmasi: qarzdor yo'q.");
            return true;
        }

        var debtorIds = debtors.Select(s => s.Id).ToHashSet();
        var toTeacher = rule.Audience == "teachers";
        var toStudent = rule.Audience == "students";
        // Telegram chatlari (bloklaganlar va auditoriyaga mos kelmaganlar chiqariladi).
        var regsByStudent = toTeacher ? new Dictionary<string, List<TelegramRegistration>>()
            : (await db.TelegramRegistrations.AsNoTracking().Where(r => r.StudentId != "" && r.BlockedAt == null).ToListAsync(ct))
                .Where(r => debtorIds.Contains(r.StudentId)
                            && (toStudent ? r.Relation == TelegramRelations.StudentSelf : TelegramRelations.IsParent(r.Relation)))
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => r.ChatId).Select(x => x.First()).ToList());
        var teachersById = toTeacher
            ? await db.Teachers.AsNoTracking().Where(t => !t.IsArchived).ToDictionaryAsync(t => t.Id, ct)
            : new Dictionary<string, Teacher>();
        var regsByTeacher = toTeacher
            ? (await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => r.TeacherId != null && r.TeacherId != "" && r.BlockedAt == null).ToListAsync(ct))
                .GroupBy(r => r.TeacherId!)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => r.ChatId).Select(x => x.First()).ToList())
            : new Dictionary<string, List<TelegramRegistration>>();

        var groupByName = (await db.Classes.ToListAsync(ct)).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
        var teacherNames = await MessageTokenizer.TeacherNamesByIdAsync(db, ct);
        var day = today.ToString("yyyy-MM-dd");

        int queued = 0, cabinet = 0, students = 0, messages = 0, alreadySent = 0, noGroupDebt = 0;
        var unclaimedFailures = 0;
        foreach (var s in debtors)
        {
            ct.ThrowIfCancellationRequested();
            var claimed = false;
            try
            {
                var items = await DebtMessages.BuildForStudentAsync(db, s, groupByName, ct);
                if (items.Count == 0) { noGroupDebt++; continue; }

                // ⚠️ OLUVCHI BELGISI — yuborishdan OLDIN, atomik (restart/ikkinchi nusxa takrorlamasin).
                if (!await JobRunMarker.TryClaimAsync(db, JobRunMarker.For(ruleKey, s.Id), today, ct))
                {
                    alreadySent++;
                    continue;
                }
                claimed = true;
                students++;

                foreach (var item in items)
                {
                    messages++;
                    Teacher? teacher = null;
                    if (toTeacher && (string.IsNullOrEmpty(item.Group?.TeacherId)
                                      || !teachersById.TryGetValue(item.Group.TeacherId, out teacher)))
                        continue;   // fan guruhining o'qituvchisi yo'q — shu xabar uchun o'tkazib yuboriladi

                    var grpTeacher = MessageTokenizer.TeacherNameOf(item.Group, teacherNames);
                    var extra = DebtMessages.Extra(item);
                    string Body(TelegramRegistration? reg) => string.IsNullOrWhiteSpace(rule.Template)
                        ? TelegramHtml.Escape(item.Body)
                        : RenderSafe(rule.Template, s, reg, item.Group, grpTeacher, extra, centerName, rule.Audience);

                    // Kabinet bildirishnomasi — HAR DOIM, oddiy matn.
                    var cabinetUserId = toTeacher ? teacher!.UserId : s.UserId;
                    if (!string.IsNullOrWhiteSpace(cabinetUserId))
                    {
                        NotificationStore.Add(db, cabinetUserId, item.Title, TelegramHtml.ToPlain(Body(null)), "payment_debt");
                        cabinet++;
                    }

                    if (!AutoMessageChannels.Telegram(rule)) continue;
                    var chats = toTeacher ? regsByTeacher.GetValueOrDefault(teacher!.Id) : regsByStudent.GetValueOrDefault(s.Id);
                    if (chats is null) continue;
                    foreach (var reg in chats)
                    {
                        var text = MessageRender.HeaderDebt + Body(toTeacher ? null : reg);
                        if (TelegramHtml.VisibleLength(text) > TelegramHtml.MaxText) text = TelegramHtml.PlainCut(text, 4000);
                        var row = await TelegramOutbox.EnqueueAsync(db, new OutboxItem(
                            TelegramOutbox.SourceAuto, AutoMessageTriggers.PaymentDebt, reg.ChatId, text,
                            StudentId: s.Id, TeacherId: teacher?.Id, RegistrationId: reg.Id,
                            Relation: toTeacher ? "" : TelegramRelations.Effective(reg.Relation),
                            RecipientName: toTeacher ? teacher!.FullName : s.FullName,
                            SourceId: rule.Id, AttachmentId: rule.AttachmentId, AckRequired: rule.AckButton,
                            DedupeKey: $"{JobKey}:{rule.Id}:{s.Id}:{(string.IsNullOrEmpty(item.Group?.Id) ? "none" : item.Group!.Id)}:{reg.ChatId}:{day}"), ct);
                        if (row is not null) queued++;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (!claimed) unclaimedFailures++;
                logger.LogWarning(ex, "To'lov eslatmasi: o'quvchi {Id} uchun xatolik", s.Id);
            }

            // HAR O'QUVCHIDAN KEYIN saqlanadi (JobRunMarker sharti: band qilish toza kontekstda).
            try { await TelegramOutbox.SaveAsync(db, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "To'lov eslatmasi: o'quvchi {Id} yozuvi saqlanmadi", s.Id); }
        }
        if (queued > 0) outbox?.Wake();

        if (alreadySent > 0)
            logger.LogInformation("To'lov eslatmasi: {Count} qarzdor bugun ALLAQACHON olgan — takror yuborilmadi.", alreadySent);
        if (noGroupDebt > 0)
            logger.LogInformation("To'lov eslatmasi: {Count} qarzdorda qarz topilmadi — yuborilmadi.", noGroupDebt);
        logger.LogInformation(
            "To'lov eslatmasi (qoida {Rule}): {Students} qarzdor, {Messages} xabar (har FAN uchun alohida), "
            + "Telegram navbatga {Tg}, kabinet {Cabinet}. Bot sozlangan: {Configured}.",
            rule.Id, students, messages, queued, cabinet, telegram.IsConfigured);
        if (unclaimedFailures > 0)
            logger.LogWarning("To'lov eslatmasi: {Count} qarzdorga xato tufayli yuborilmadi — keyingi uyg'onishda qayta uriniladi.",
                unclaimedFailures);
        return unclaimedFailures == 0;
    }

    /// <summary>Qoida matni (Telegram HTML); belgilash xatosi bo'lgan eski matn — escape qilingan oddiy matn.</summary>
    private static string RenderSafe(
        string template, Student s, TelegramRegistration? reg, Group? group, string? teacherName,
        IReadOnlyDictionary<string, string> extra, string centerName, string audience)
    {
        var r = MessageRender.RenderStudent(template, s, reg, group, teacherName, extra, centerName, audience);
        return r.Errors.Count == 0 ? r.Html
            : MessageRender.RenderStudent(TelegramHtml.Escape(template), s, reg, group, teacherName, extra, centerName, audience).Html;
    }
}
