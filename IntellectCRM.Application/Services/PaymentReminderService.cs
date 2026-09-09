using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Avtomatik to'lov eslatmasi (fon xizmati). Har oyning 1-sanasida BARCHA qarzdorlarga
/// (balansi manfiy o'quvchilar) eslatma yuboradi; keyin HAR 2 KUNDA (1, 3, 5, ...)
/// hali ham qarzdor bo'lganlarga TAKROR yuboradi (o'quvchi qarzini to'lasa balansi manfiy
/// bo'lmaydi — eslatma to'xtaydi). Yuborish vaqti: ertalab 09:00 (Toshkent, UTC+5).
///
/// ⚠️ XABAR HAR FAN (kurs/guruh) UCHUN ALOHIDA ketadi: o'quvchi ikki fanda o'qisa ota-onaga
/// ikkita alohida eslatma boradi (bitta "jami" xabar EMAS). Batafsil sabab va narx kelishuvi —
/// <see cref="BuildMessagesAsync"/> izohida.
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

        // Guruh konteksti — FAQAT zaxira yo'l uchun (a'zoligi yo'q, eski `ClassName` modelidagi
        // o'quvchi): {dars_sana}/{dars_vaqti}/{dars_kunlari} va {oqituvchi} tokenlari shundan
        // to'ladi. A'zoligi bor o'quvchida token konteksti endi HAR XABARNING O'Z GURUHIDAN
        // olinadi (pastga qarang) — ilgari hammasi shu eskirgan yorliqdan olinardi.
        // Ro'yxat uchun bir marta yuklanadi (N+1 yo'q).
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
        // matn ({qarzdorlik} = SHU FANNING qarzi bilan) yuboriladi; bo'sh bo'lsa — tizim matni.
        var smsRule = debtRules.FirstOrDefault(r => r.SendSms);
        var pushRule = debtRules.FirstOrDefault(r => r.SendPush);
        var tgRule = debtRules.FirstOrDefault(r => r.SendTelegram);

        int tgSent = 0, pushSent = 0, smsSent = 0, students = 0, messages = 0;
        var deadTokens = new List<string>();
        foreach (var s in debtors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // HAR FAN uchun alohida xabar (§ BuildMessagesAsync izohi).
                var items = await BuildMessagesAsync(db, s, groupByName, ct);
                if (items.Count == 0) continue;
                students++;

                var phone = !string.IsNullOrWhiteSpace(s.ParentPhone) ? s.ParentPhone
                    : !string.IsNullOrWhiteSpace(s.FatherPhone) ? s.FatherPhone
                    : !string.IsNullOrWhiteSpace(s.MotherPhone) ? s.MotherPhone : s.Phone;

                foreach (var item in items)
                {
                    messages++;
                    // Token konteksti — SHU xabarning guruhi: {guruh}/{oqituvchi}/{dars_*} aynan shu
                    // fandan, {qarzdorlik} esa aynan shu fanning qarzi (jami EMAS).
                    var grpTeacher = MessageTokenizer.TeacherNameOf(item.Group, teacherNames);
                    var extra = new Dictionary<string, string>
                    {
                        ["{qarzdorlik}"] = Money(item.Amount),
                        ["{kurs}"] = item.Label,
                        // {guruh} ATAYIN ustidan yoziladi: MessageTokenizer.Student uni eskirgan
                        // `Student.ClassName` dan to'ldiradi (u faqat BIRINCHI guruhda yoziladi va
                        // keyin yangilanmaydi — `StudentMembershipView` izohi).
                        ["{guruh}"] = item.Group?.Name ?? item.Label,
                    };

                    // Qoidaning matni bo'lsa — tokenlar bilan render, aks holda tizim matni.
                    string BodyFor(AutoMessageRule? r)
                    {
                        if (r is null || string.IsNullOrWhiteSpace(r.Template)) return item.Body;
                        var withExtra = MessageTokenizer.ApplyExtra(r.Template, extra);
                        return MessageTokenizer.Student(withExtra, s, s.ParentFullName, phone, centerName,
                            group: item.Group, teacherName: grpTeacher);
                    }

                    // Ilova tarixiga (push/telegram bo'lmasa ham ilovada ko'rinadi). Push kanali yoqilganda.
                    if (sendPush) NotificationStore.Add(db, s.UserId, item.Title, BodyFor(pushRule), "payment_debt");

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
                        var res = await fcm.SendAsync(fcmJson, toks, item.Title, BodyFor(pushRule), ct);
                        pushSent += res.Sent;
                        deadTokens.AddRange(res.InvalidTokens);
                    }

                    // SMS (ota-ona telefoniga) — SendSms yoqilgan bo'lsa.
                    // ⚠️ Ikki fanda o'qiydigan o'quvchining ota-onasiga SHU sikl ichida IKKI SMS ketadi.
                    // Yuborish yo'lida takrorni "yutadigan" dedupe YO'Q (SmsLog faqat jurnal), ustiga
                    // matnlar fan nomi bilan HAR XIL — ya'ni ikkalasi ham chiqadi. Bu ONGLI qaror.
                    if (smsReady && !string.IsNullOrWhiteSpace(phone))
                    {
                        var result = await AutoMessageSmsSender.SendAsync(
                            db, eskiz, ctiSms, smsProvider, phone, s.FullName, BodyFor(smsRule),
                            "Qarzdorlik eslatmasi", ct);
                        if (result.Ok) smsSent++;
                    }
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

        // ⚠️ "Qarzdor" va "xabar" soni endi BOSHQA-BOSHQA: har fan uchun alohida xabar ketadi
        // (2 fanda o'qiydigan 1 qarzdor = 2 xabar). Log shuni ochiq yozadi, aks holda
        // "3 qarzdor, SMS 5" chalkash ko'rinardi.
        logger.LogInformation(
            "To'lov eslatmasi yuborildi: {Students} qarzdor, {Messages} xabar (har FAN uchun alohida), "
            + "Telegram {Tg}, push {Push}, SMS {Sms}.",
            students, messages, tgSent, pushSent, smsSent);
    }

    /// <summary>Bitta yuboriladigan xabar: QAYSI fan (guruh) uchun, qancha qarz va tayyor matn.</summary>
    /// <param name="Group">Token konteksti — {guruh}, {oqituvchi} va {dars_*} AYNAN shu guruhdan
    /// olinadi. Zaxira (a'zoliksiz) yo'lda <c>null</c> bo'lishi mumkin.</param>
    /// <param name="Label">Kurs (fan) nomi — matnda va {kurs} tokenida.</param>
    /// <param name="Amount">SHU fan bo'yicha qarz (jami EMAS) — {qarzdorlik} tokeni shundan.</param>
    private sealed record DebtMessage(Group? Group, string Label, decimal Amount, string Title, string Body);

    /// <summary>
    /// Bitta o'quvchi uchun eslatma xabarlari — HAR FAN (guruh) uchun ALOHIDA element.
    ///
    /// <para>⚠️ Ilgari o'quvchiga BITTA xabar ketardi: ichida har kurs alohida qator, oxirida
    /// "Jami". Endi har fan O'Z xabari bilan boradi — ikki fanda o'qiyotgan o'quvchi ikkita
    /// alohida eslatma oladi. Bu markaz egasining ANIQ talabi va u NARXGA tegadi (N fan = N SMS,
    /// N push, N telegram) — ONGLI kelishuv, xato emas. Shuning uchun matn ham qisqartirilgan.</para>
    ///
    /// <para>Per-guruh qarz <see cref="StudentGroupLedger"/> orqali (perGroup billing).
    /// Faol/muzlatilgan a'zoliklar hisobga olinadi (trial — to'lov yo'q), qarzi YO'Q fan uchun
    /// xabar umuman tuzilmaydi. A'zoligi yo'q o'quvchida (eski <c>ClassName</c> modeli) avvalgidek
    /// BITTA xabar qaytadi — qarz <see cref="Student.Balance"/> qoldig'idan.</para>
    /// </summary>
    private static async Task<List<DebtMessage>> BuildMessagesAsync(
        IAppDbContext db, Student s, IReadOnlyDictionary<string, Group> groupByName, CancellationToken ct)
    {
        var items = new List<DebtMessage>();

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

        // Bitta guruhda bir nechta a'zolik qatori bo'lsa — BITTA xabar (asosiysi bo'yicha):
        // aks holda o'quvchi bir xil fan uchun ikki marta eslatma olardi.
        foreach (var group in memberships.GroupBy(m => m.GroupId))
        {
            var m = MembershipLifecycle.PrimaryMembership(group) ?? group.First();
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            var ledger = await StudentGroupLedger.BuildAsync(db, s, g, m, book);
            var owed = ledger.Months.Sum(x => x.Remaining);
            if (owed <= 0) continue;
            items.Add(Compose(s, g, ledger.CourseName, owed));
        }

        if (items.Count > 0) return items;

        // ZAXIRA YO'L: a'zoligi (yoki per-guruh qarzi) yo'q — umumiy balans qarzidan BITTA xabar.
        // Bu yerda fan kesimi UMUMAN ma'lum emas, shuning uchun bo'linmaydi (eski xulq saqlanadi).
        var total = s.Balance < 0 ? -s.Balance : 0m;
        if (total <= 0) return items;
        var label = s.ClassName ?? "";
        items.Add(Compose(s, groupByName.GetValueOrDefault(label), label, total));
        return items;
    }

    /// <summary>Bitta fan uchun xabar matni. Matn ATAYIN qisqa: endi har fan alohida SMS bo'lib
    /// ketadi, ya'ni har ortiqcha belgi qarzdorlar soniga KO'PAYTIRILADI (SMS narxi segment
    /// bo'yicha). "Jami" qatori YO'Q — u fanlarni aralashtirib, xabarni yolg'on qilardi.</summary>
    private static DebtMessage Compose(Student s, Group? group, string label, decimal amount)
    {
        var head = string.IsNullOrWhiteSpace(label)
            ? $"{s.FullName} bo'yicha qarzdorlik: {Money(amount)}."
            : $"{s.FullName} — {label} bo'yicha qarzdorlik: {Money(amount)}.";
        return new DebtMessage(group, label, amount, "To'lov eslatmasi",
            $"{head}\nIltimos, to'lovni amalga oshiring.");
    }

    /// <summary>So'm formatlash: 1 700 000 so'm (probel bilan ajratilgan).</summary>
    private static string Money(decimal v)
    {
        var nfi = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };
        return v.ToString("#,0", nfi) + " so'm";
    }
}
