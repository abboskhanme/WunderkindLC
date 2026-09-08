using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Avtomatik to'lov eslatmasi (fon xizmati). Har oyning 1-sanasida BARCHA qarzdorlarga
/// (balansi manfiy o'quvchilar) batafsil eslatma yuboradi; keyin HAR 2 KUNDA (1, 3, 5, ...)
/// hali ham qarzdor bo'lganlarga TAKROR yuboradi (o'quvchi qarzini to'lasa balansi manfiy
/// bo'lmaydi — eslatma to'xtaydi). Yuborish vaqti: ertalab 09:00 (Toshkent, UTC+5).
///
/// Kanal: Telegram (TelegramRegistration chatlari) VA push (DeviceToken — ota-ona akkaunti).
/// Yuborish mantig'i mavjud helperlarni (TelegramService.SendMessageAsync, FcmService.SendAsync)
/// chaqiradi — yangi yuborish kodi yozilmagan.
///
/// "Oxirgi yuborilgan sana"ni alohida saqlamaymiz: eng sodda yo'l — oyning TOQ kunlarida yubor
/// (kun == 1 yoki (kun - 1) % 2 == 0 ⇒ 1, 3, 5, 7, ...). Bu "1-sanada hamma, keyin har 2 kunda
/// to'lamaganlar" talabini aniq beradi. Sikl kuniga BIR marta ishlaydi (oxirgi ishlagan sana
/// xotirada saqlanadi), 09:00 dan keyin uyg'onganda.
/// </summary>
public class PaymentReminderService(
    IServiceProvider services,
    TelegramService telegram,
    FcmService fcm,
    EskizService eskiz,
    CtiSmsService ctiSms,
    ILogger<PaymentReminderService> logger) : BackgroundService
{
    /// <summary>Eslatma yuboriladigan soat (Toshkent vaqti).</summary>
    private const int SendHour = 9;

    /// <summary>Shu sanada (bugun) kunlik tekshiruv allaqachon ishlaganmi — takror yubormaslik uchun.</summary>
    private DateOnly _lastRun = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = AppClock.Now;
                var today = DateOnly.FromDateTime(now);
                // 09:00 dan o'tgan bo'lsa va bugun hali ishlamagan bo'lsak — kunlik tekshiruv.
                if (now.Hour >= SendHour && _lastRun != today)
                {
                    _lastRun = today;
                    await RunDailyAsync(today, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "To'lov eslatmasi siklida xatolik");
            }

            // Har ~1 soatda uyg'onamiz (09:00 ni o'tkazib yubormaslik uchun).
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Bugungi kun uchun eslatma yuborish kerakmi (toq kunlar: 1, 3, 5, ...) tekshiradi va yuboradi.</summary>
    private async Task RunDailyAsync(DateOnly today, CancellationToken ct)
    {
        // Eslatma faqat toq kunlarda: 1-sana (hamma qarzdor), keyin har 2 kunda (3, 5, 7, ...) — hali qarzdorlar.
        if (today.Day != 1 && (today.Day - 1) % 2 != 0) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        // Kanallar FAQAT admin aniq YOQGAN qarzdorlik qoidalaridan olinadi. DIQQAT: ilgari qoida yo'q bo'lsa
        // push+telegram DEFAULT-ON edi — bu "auto Telegramdan xato bilan yuborish" sababi edi. ENDI: yoqilgan
        // qoida bo'lmasa HECH NARSA yuborilmaydi. Har qoida bitta kanal — bayroqlar birlashtiriladi (union).
        var debtRules = await db.AutoMessageRules
            .Where(r => r.Trigger == AutoMessageTriggers.PaymentDebt && r.Enabled).ToListAsync(ct);
        if (debtRules.Count == 0)
        {
            logger.LogInformation("To'lov eslatmasi: yoqilgan qoida yo'q — yuborilmadi.");
            return;
        }
        var sendSms = debtRules.Any(r => r.SendSms);
        var sendPush = debtRules.Any(r => r.SendPush);
        var sendTelegram = debtRules.Any(r => r.SendTelegram);

        // Qarzdorlar: balansi manfiy, arxivlanmagan o'quvchilar.
        var debtors = await db.Students
            .Where(s => !s.IsArchived && s.Balance < 0)
            .ToListAsync(ct);
        // Guruhi YOPILGAN/TUGATILGAN (tirik a'zoligi qolmagan) o'quvchilarga eslatma yuborilmaydi.
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
            return;
        }

        var debtorIds = debtors.Select(s => s.Id).ToList();

        // Telegram chatlari (har qarzdor o'quvchiga 0+ chat).
        var regsByStudent = (await db.TelegramRegistrations
                .Where(r => debtorIds.Contains(r.StudentId))
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList());

        // Push qurilma tokenlari (ota-ona akkaunti UserId orqali).
        var userIds = debtors.Where(s => s.UserId != null).Select(s => s.UserId!).ToList();
        var tokensByUser = (await db.DeviceTokens
                .Where(d => userIds.Contains(d.UserId))
                .ToListAsync(ct))
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Token).Distinct().ToList());

        // Guruh konteksti (o'quvchining ASOSIY guruhi, ClassName bo'yicha) — {dars_sana}/{dars_vaqti}/
        // {dars_kunlari} va {oqituvchi} tokenlari uchun. Ro'yxat uchun bir marta yuklanadi (N+1 yo'q).
        var groupByName = (await db.Classes.ToListAsync(ct))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
        var teacherNames = await MessageTokenizer.TeacherNamesByIdAsync(db, ct);

        var fcmJson = AppSecrets.FcmServiceAccountJson;
        var centerName = meta?.Name ?? "";
        var telegramReady = sendTelegram && telegram.IsConfigured;
        var pushReady = sendPush && FcmService.IsConfigured(fcmJson);
        var smsProvider = debtRules.FirstOrDefault(r => r.SendSms)?.SmsProvider ?? "eskiz";
        var smsReady = sendSms && AutoMessageSmsSender.IsReady(smsProvider, meta, eskiz);

        // Har kanal uchun mos qoida (bitta kanal modeli — har qoida bitta kanal). Qoidada MATN bo'lsa — o'sha
        // matn ({qarzdorlik} = jami qarz bilan) yuboriladi; bo'sh bo'lsa — tizim batafsil qarz ro'yxatini tuzadi.
        var smsRule = debtRules.FirstOrDefault(r => r.SendSms);
        var pushRule = debtRules.FirstOrDefault(r => r.SendPush);
        var tgRule = debtRules.FirstOrDefault(r => r.SendTelegram);

        int tgSent = 0, pushSent = 0, smsSent = 0, students = 0;
        var deadTokens = new List<string>();
        foreach (var s in debtors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (title, systemBody, total) = await BuildMessageAsync(db, s, ct);
                if (systemBody.Length == 0) continue;
                students++;

                var phone = !string.IsNullOrWhiteSpace(s.ParentPhone) ? s.ParentPhone
                    : !string.IsNullOrWhiteSpace(s.FatherPhone) ? s.FatherPhone
                    : !string.IsNullOrWhiteSpace(s.MotherPhone) ? s.MotherPhone : s.Phone;

                // O'quvchining asosiy guruhi + uning o'qituvchisi — {dars_*} va {oqituvchi} tokenlari uchun.
                var grp = groupByName.GetValueOrDefault(s.ClassName ?? "");
                var grpTeacher = MessageTokenizer.TeacherNameOf(grp, teacherNames);

                // Qoidaning matni bo'lsa — tokenlar bilan render (qarzdorlik = aniq jami), aks holda tizim matni.
                string BodyFor(AutoMessageRule? r)
                {
                    if (r is null || string.IsNullOrWhiteSpace(r.Template)) return systemBody;
                    var withExtra = MessageTokenizer.ApplyExtra(r.Template,
                        new Dictionary<string, string> { ["{qarzdorlik}"] = Money(total) });
                    return MessageTokenizer.Student(withExtra, s, s.ParentFullName, phone, centerName,
                        group: grp, teacherName: grpTeacher);
                }

                // Ilova tarixiga (push/telegram bo'lmasa ham ilovada ko'rinadi). Push kanali yoqilganda.
                if (sendPush) NotificationStore.Add(db, s.UserId, title, BodyFor(pushRule), "payment_debt");

                // Telegram.
                if (telegramReady && regsByStudent.TryGetValue(s.Id, out var chats))
                {
                    var tgText = $"💰 To'lov eslatmasi\n\n{BodyFor(tgRule)}";
                    foreach (var chatId in chats)
                        if (await telegram.SendMessageAsync(chatId, tgText, ct: ct)) tgSent++;
                }

                // Push (ota-ona akkauntining qurilmalariga).
                if (pushReady && s.UserId != null && tokensByUser.TryGetValue(s.UserId, out var toks))
                {
                    var res = await fcm.SendAsync(fcmJson, toks, title, BodyFor(pushRule), ct);
                    pushSent += res.Sent;
                    deadTokens.AddRange(res.InvalidTokens);
                }

                // SMS (ota-ona telefoniga) — SendSms yoqilgan bo'lsa.
                if (smsReady && !string.IsNullOrWhiteSpace(phone))
                {
                    var result = await AutoMessageSmsSender.SendAsync(
                        db, eskiz, ctiSms, smsProvider, phone, s.FullName, BodyFor(smsRule), "Qarzdorlik eslatmasi", ct);
                    if (result.Ok) smsSent++;
                }
            }
            catch (Exception ex)
            {
                // Bitta o'quvchi xatosi butun siklni to'xtatmasin.
                logger.LogWarning(ex, "To'lov eslatmasi: o'quvchi {Id} uchun xatolik", s.Id);
            }
        }

        // O'lik tokenlarni bazadan tozalaymiz (ilova o'chirilgan / web token bekor qilingan).
        if (deadTokens.Count > 0)
            db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => deadTokens.Contains(d.Token)));

        if (students > 0 || deadTokens.Count > 0) await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "To'lov eslatmasi yuborildi: {Students} qarzdor, Telegram {Tg}, push {Push}, SMS {Sms}.",
            students, tgSent, pushSent, smsSent);
    }

    /// <summary>
    /// Bitta o'quvchi uchun batafsil eslatma matnini tuzadi: har faol guruh (kurs) bo'yicha qoldiq qarz
    /// + oxirida "Jami: X so'm". Per-guruh qarz <see cref="StudentGroupLedger"/> orqali (perGroup billing).
    /// Faol/muzlatilgan a'zoliklar hisobga olinadi (trial — to'lov yo'q). A'zoligi yo'q o'quvchida (eski
    /// ClassName modeli) qarz <see cref="StudentLedger"/> umumiy qoldig'idan olinadi.
    /// </summary>
    private static async Task<(string Title, string Body, decimal Total)> BuildMessageAsync(
        IAppDbContext db, Student s, CancellationToken ct)
    {
        var lines = new List<string>();
        decimal total = 0m;

        // Faol/muzlatilgan a'zoliklar (trial — to'lov hisoblanmaydi).
        var memberships = await db.StudentGroups
            .Where(m => m.StudentId == s.Id && m.IsActive && (m.Status == "active" || m.Status == "frozen"))
            .ToListAsync(ct);
        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = (await db.Classes.Where(g => groupIds.Contains(g.Id)).ToListAsync(ct))
            .ToDictionary(g => g.Id);

        // Chegirma kitobi — o'quvchi uchun BIR MARTA (halqa ichida yuklansa har guruh uchun
        // ortiqcha so'rov bo'lardi; qiymat bir xil).
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);

        var hasGroupDebt = false;
        foreach (var m in memberships)
        {
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            var ledger = await StudentGroupLedger.BuildAsync(db, s, g, m, book);
            var owed = ledger.Months.Sum(x => x.Remaining);
            if (owed <= 0) continue;
            hasGroupDebt = true;
            total += owed;
            lines.Add($"• {ledger.CourseName}: {Money(owed)}");
        }

        // A'zoligi (yoki per-guruh qarzi) yo'q — umumiy balans qarzidan foydalanamiz.
        if (!hasGroupDebt)
        {
            total = s.Balance < 0 ? -s.Balance : 0m;
            if (total <= 0) return ("", "", 0m);
            var label = string.IsNullOrEmpty(s.ClassName) ? "Oylik to'lov" : s.ClassName;
            lines.Add($"• {label}: {Money(total)}");
        }

        if (total <= 0) return ("", "", 0m);

        var title = "To'lov eslatmasi";
        var body =
            $"{s.FullName} bo'yicha qarzdorlik:\n" +
            string.Join("\n", lines) +
            $"\n\nJami: {Money(total)}\nIltimos, to'lovni amalga oshiring.";
        return (title, body, total);
    }

    /// <summary>So'm formatlash: 1 700 000 so'm (probel bilan ajratilgan).</summary>
    private static string Money(decimal v)
    {
        var nfi = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };
        return v.ToString("#,0", nfi) + " so'm";
    }
}
