using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

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
/// Kanal: kabinet bildirishnomasi (har doim) + Telegram (TelegramRegistration chatlari) —
/// <see cref="AutoMessageChannels"/>. SMS/push 2026-10 da olib tashlandi.
///
/// "Qaysi kunlari" — oyning TOQ kunlarida yubor (kun == 1 yoki (kun - 1) % 2 == 0 ⇒ 1, 3, 5, 7, ...).
/// Bu "1-sanada hamma, keyin har 2 kunda to'lamaganlar" talabini aniq beradi.
///
/// <para>⚠️ <b>"BUGUN ISHLADIMI" — BAZADA</b> (<see cref="JobRunMarker"/>). Ilgari bu belgi
/// xotirada edi: har deploy/restart (09:00 dan keyin) BUTUN qarzdorlar ro'yxatiga eslatma QAYTA
/// ketardi. Endi ikki daraja: har o'quvchi yuborishdan OLDIN band qilinadi
/// (<c>payment_debt:{studentId}</c> — ish yarmida yiqilsa ham takror yo'q, qolganlarga yetadi) va
/// ish to'liq tugagach <c>payment_debt</c> belgisi qo'yiladi.</para>
///
/// <para>⚠️ <b>AUDITORIYA</b> qoidaning o'zidan (<see cref="AutoMessageRule.Audience"/>), har qoida
/// alohida: <c>parents</c> — ota-ona raqami birinchi; <c>students</c> — o'quvchining o'z raqami
/// birinchi; <c>teachers</c> — SHU FAN guruhining o'qituvchisiga (kabineti/Telegrami).
/// Ilgari auditoriya umuman o'qilmasdi — hamma qoida ota-onaga ketardi.</para>
/// </summary>
public class PaymentReminderService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<PaymentReminderService> logger) : BackgroundService
{
    /// <summary>Eslatma yuboriladigan soat (Toshkent vaqti).</summary>
    private const int SendHour = 9;

    /// <summary>Ish darajasidagi belgi kaliti (oluvchi kaliti — <c>payment_debt:{studentId}</c>).</summary>
    public const string JobKey = AutoMessageTriggers.PaymentDebt;

    /// <summary>Shu jarayonda bugun allaqachon tekshirildimi — har soatda bazaga bekor so'rov
    /// yubormaslik uchun. ⚠️ Faqat TEZLIK uchun: haqiqiy "bajarildi" belgisi bazada.</summary>
    private DateOnly _checked = DateOnly.MinValue;

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

            // Har ~1 soatda uyg'onamiz (09:00 ni o'tkazib yubormaslik uchun).
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Bitta uyg'onish: 09:00 dan o'tgan va bugungi ish bazada BAJARILMAGAN bo'lsa — kunlik ish.
    /// Ish yarmida yiqilsa belgi qo'yilmaydi: keyingi uyg'onishda qayta boshlanadi, allaqachon
    /// olganlar esa oluvchi belgisi tufayli TAKROR olmaydi.
    /// </summary>
    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        if (now.Hour < SendHour || _checked == today) return;

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await JobRunMarker.IsDoneAsync(db, JobKey, today, ct)) { _checked = today; return; }
        }

        // Ish yarmida tutilgan xato bo'lsa (belgisi qo'yilmagan qarzdorlar qolgan) — ish belgisi QO'YILMAYDI.
        if (!await RunDailyAsync(today, ct)) return;

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await JobRunMarker.TryClaimAsync(db, JobKey, today, ct);
            // Oluvchi belgilari faqat O'Z sanasida ma'noli — eskilari tozalanadi.
            await JobRunMarker.PruneAsync(db, JobKey + ":", today.AddDays(-2), ct);
        }
        _checked = today;
    }

    /// <summary>Bugungi kun uchun eslatma yuborish kerakmi (toq kunlar: 1, 3, 5, ...) tekshiradi va yuboradi.
    /// <para>Natija — kunlik ish TO'LIQ bajarildimi. <c>false</c>: kamida bitta qarzdor oluvchi belgisi
    /// QO'YILMASDAN (ya'ni hali yuborilmay) xatoga uchradi — ish belgisi qo'yilmaydi va keyingi uyg'onishda
    /// faqat shu qolganlar qayta uriniladi (belgisi borlar takror olmaydi).</para></summary>
    private async Task<bool> RunDailyAsync(DateOnly today, CancellationToken ct)
    {
        // Eslatma faqat toq kunlarda: 1-sana (hamma qarzdor), keyin har 2 kunda (3, 5, 7, ...) — hali qarzdorlar.
        if (today.Day != 1 && (today.Day - 1) % 2 != 0) return true;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        // Yetkazish FAQAT admin aniq YOQGAN qarzdorlik qoidalaridan olinadi. DIQQAT: ilgari qoida yo'q bo'lsa
        // push+telegram DEFAULT-ON edi — bu "auto Telegramdan xato bilan yuborish" sababi edi. ENDI: yoqilgan
        // qoida bo'lmasa HECH NARSA yuborilmaydi. HAR QOIDA alohida yetkaziladi — o'z kanallari va o'z
        // AUDITORIYASI bilan (DispatchStudentAsync bilan bir xil model).
        var debtRules = await db.AutoMessageRules
            .Where(r => r.Trigger == AutoMessageTriggers.PaymentDebt && r.Enabled).ToListAsync(ct);
        if (debtRules.Count == 0)
        {
            logger.LogInformation("To'lov eslatmasi: yoqilgan qoida yo'q — yuborilmadi.");
            return true;
        }

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
            return true;
        }

        var debtorIds = debtors.Select(s => s.Id).ToList();

        // Telegram chatlari (har qarzdor o'quvchiga 0+ chat).
        var regsByStudent = (await db.TelegramRegistrations
                .Where(r => debtorIds.Contains(r.StudentId))
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList());

        // "O'qituvchilarga" qoidasi bo'lsa — o'qituvchilar va ularning Telegram chatlari (bir marta).
        var anyTeacherRule = debtRules.Any(r => r.Audience == "teachers");
        var teachersById = anyTeacherRule
            ? await db.Teachers.AsNoTracking().Where(t => !t.IsArchived).ToDictionaryAsync(t => t.Id, ct)
            : new Dictionary<string, Teacher>();
        var chatsByTeacher = anyTeacherRule
            ? (await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => r.TeacherId != null && r.TeacherId != "").ToListAsync(ct))
                .GroupBy(r => r.TeacherId!)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList())
            : new Dictionary<string, List<long>>();

        // Guruh konteksti — FAQAT zaxira yo'l uchun (a'zoligi yo'q, eski `ClassName` modelidagi
        // o'quvchi): {dars_sana}/{dars_vaqti}/{dars_kunlari} va {oqituvchi} tokenlari shundan
        // to'ladi. A'zoligi bor o'quvchida token konteksti endi HAR XABARNING O'Z GURUHIDAN
        // olinadi (pastga qarang) — ilgari hammasi shu eskirgan yorliqdan olinardi.
        // Ro'yxat uchun bir marta yuklanadi (N+1 yo'q).
        var groupByName = (await db.Classes.ToListAsync(ct))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
        var teacherNames = await MessageTokenizer.TeacherNamesByIdAsync(db, ct);

        var centerName = meta?.Name ?? "";

        int tgSent = 0, cabinet = 0, students = 0, messages = 0, alreadySent = 0, noGroupDebt = 0;
        var unclaimedFailures = 0;
        foreach (var s in debtors)
        {
            ct.ThrowIfCancellationRequested();
            var claimed = false;
            try
            {
                // HAR FAN uchun alohida xabar (§ BuildMessagesAsync izohi).
                var items = await BuildMessagesAsync(db, s, groupByName, ct);
                if (items.Count == 0) { noGroupDebt++; continue; }

                // ⚠️ OLUVCHI BELGISI — yuborishdan OLDIN, atomik. Restart/ikkinchi nusxa shu
                // o'quvchiga bugun TAKROR yubormaydi (ilgari har deploy hammaga qayta yuborardi).
                if (!await JobRunMarker.TryClaimAsync(db, JobRunMarker.For(JobKey, s.Id), today, ct))
                {
                    alreadySent++;
                    continue;
                }
                claimed = true;
                students++;

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

                    foreach (var rule in debtRules)
                    {
                        // AUDITORIYA — qoidaning o'zidan (ilgari e'tiborsiz qolardi).
                        var toTeacher = rule.Audience == "teachers";
                        Teacher? teacher = null;
                        if (toTeacher)
                        {
                            // Fan guruhining o'qituvchisi yo'q (zaxira yo'l / o'qituvchisiz guruh) — bu
                            // qoida shu xabar uchun o'tkazib yuboriladi.
                            if (string.IsNullOrEmpty(item.Group?.TeacherId)
                                || !teachersById.TryGetValue(item.Group.TeacherId, out teacher))
                                continue;
                        }
                        var phone = MessagingAudience.StudentPhone(s, rule.Audience);

                        // Qoidaning matni bo'lsa — tokenlar bilan render, aks holda tizim matni.
                        var body = string.IsNullOrWhiteSpace(rule.Template)
                            ? item.Body
                            : MessageTokenizer.Student(MessageTokenizer.ApplyExtra(rule.Template, extra),
                                s, s.ParentFullName, phone, centerName, group: item.Group, teacherName: grpTeacher);

                        // Kabinet bildirishnomasi (web kabinet qo'ng'iroqchasi) — HAR DOIM (AutoMessageChannels).
                        var cabinetUserId = toTeacher ? teacher!.UserId : s.UserId;
                        if (!string.IsNullOrWhiteSpace(cabinetUserId))
                        {
                            NotificationStore.Add(db, cabinetUserId, item.Title, body, "payment_debt");
                            cabinet++;
                        }

                        // Telegram.
                        if (AutoMessageChannels.Telegram(rule) && telegram.IsConfigured)
                        {
                            var chats = toTeacher
                                ? chatsByTeacher.GetValueOrDefault(teacher!.Id)
                                : regsByStudent.GetValueOrDefault(s.Id);
                            if (chats is not null)
                            {
                                var tgText = $"💰 To'lov eslatmasi\n\n{body}";
                                foreach (var chatId in chats)
                                    if (await telegram.SendMessageAsync(chatId, tgText, ct: ct)) tgSent++;
                            }
                        }

                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Bitta o'quvchi xatosi butun siklni to'xtatmasin. Belgi QO'YILMAGAN bo'lsa (xabar hali
                // ketmagan) — kun "bajarildi" deb yopilmaydi, keyingi uyg'onishda shu o'quvchi qayta uriniladi.
                if (!claimed) unclaimedFailures++;
                logger.LogWarning(ex, "To'lov eslatmasi: o'quvchi {Id} uchun xatolik", s.Id);
            }

            // HAR O'QUVCHIDAN KEYIN saqlanadi: kabinet tarixi yiqilishda yo'qolmasin,
            // keyingi o'quvchining belgisi esa toza kontekstda band qilinsin (JobRunMarker sharti).
            try { await db.SaveChangesAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "To'lov eslatmasi: o'quvchi {Id} yozuvi saqlanmadi", s.Id); }
        }

        if (alreadySent > 0)
            logger.LogInformation("To'lov eslatmasi: {Count} qarzdor bugun ALLAQACHON olgan — takror yuborilmadi.", alreadySent);
        if (noGroupDebt > 0)
            // Ro'yxat olinganidan keyin qarzi yopilgan (yoki guruh ledger'lari va balans qarzni
            // ko'rsatmagan) qarzdorlar — xabar yo'q. Diagnostika uchun soni yoziladi.
            logger.LogInformation("To'lov eslatmasi: {Count} qarzdorda qarz topilmadi — yuborilmadi.", noGroupDebt);

        // ⚠️ "Qarzdor" va "xabar" soni endi BOSHQA-BOSHQA: har fan uchun alohida xabar ketadi
        // (2 fanda o'qiydigan 1 qarzdor = 2 xabar). Log shuni ochiq yozadi, aks holda
        // "3 qarzdor, 5 xabar" chalkash ko'rinardi.
        logger.LogInformation(
            "To'lov eslatmasi yuborildi: {Students} qarzdor, {Messages} xabar (har FAN uchun alohida), "
            + "Telegram {Tg}, kabinet {Cabinet}.",
            students, messages, tgSent, cabinet);
        if (unclaimedFailures > 0)
            logger.LogWarning("To'lov eslatmasi: {Count} qarzdorga xato tufayli yuborilmadi — keyingi uyg'onishda qayta uriniladi.",
                unclaimedFailures);
        return unclaimedFailures == 0;
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
    /// alohida eslatma oladi. Bu markaz egasining ANIQ talabi (N fan = N xabar) — ONGLI kelishuv,
    /// xato emas. Shuning uchun matn ham qisqartirilgan.</para>
    ///
    /// <para>Per-guruh qarz <see cref="StudentGroupLedger"/> orqali (perGroup billing). BARCHA
    /// to'lovli a'zoliklar hisobga olinadi — TUGAGAN/CHIQQANLARI ham (trial — to'lov yo'q):
    /// guruh almashtirgan o'quvchining ESKI guruhdagi qoldiq qarzi o'sha fan nomi bilan keladi.
    /// Qarzi YO'Q fan uchun xabar umuman tuzilmaydi.</para>
    ///
    /// <para>⚠️ ZAXIRA YO'L (bitta xabar, qarz <see cref="Student.Balance"/> dan, yorliq
    /// <c>ClassName</c>) — FAQAT a'zoligi UMUMAN YO'Q (eski model) o'quvchiga. Ilgari a'zoligi bor,
    /// lekin faol guruhlarida qarzi chiqmagan o'quvchiga ham ishlardi va eski guruhning qarzini
    /// boshqa (yangi) guruh nomi bilan yozib yuborardi.</para>
    /// </summary>
    private static async Task<List<DebtMessage>> BuildMessagesAsync(
        IAppDbContext db, Student s, IReadOnlyDictionary<string, Group> groupByName, CancellationToken ct)
    {
        var items = new List<DebtMessage>();

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(m => m.StudentId == s.Id)
            .ToListAsync(ct);

        if (memberships.Count == 0)
        {
            // ZAXIRA YO'L: a'zoligi yo'q (eski ClassName modeli) — umumiy balans qarzidan BITTA xabar.
            // Bu yerda fan kesimi UMUMAN ma'lum emas, shuning uchun bo'linmaydi (eski xulq saqlanadi).
            var total = s.Balance < 0 ? -s.Balance : 0m;
            if (total <= 0) return items;
            var label = s.ClassName ?? "";
            items.Add(Compose(s, groupByName.GetValueOrDefault(label), label, total));
            return items;
        }

        // To'lovli a'zoliklar (faol, muzlatilgan, CHIQQAN/TUGATGAN) — sinov (trial) to'lov hisoblamaydi.
        var billable = memberships.Where(m => m.Status != "trial").ToList();
        var groupIds = billable.Select(m => m.GroupId).Distinct().ToList();
        var groups = (await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync(ct))
            .ToDictionary(g => g.Id);

        // Chegirma kitobi — o'quvchi uchun BIR MARTA (halqa ichida yuklansa har guruh uchun
        // ortiqcha so'rov bo'lardi; qiymat bir xil).
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);

        // Bitta guruhda bir nechta a'zolik qatori bo'lsa — BITTA xabar: tirigi (asosiysi), u yo'q
        // bo'lsa ENG OXIRGI tugagani. Aks holda o'quvchi bir xil fan uchun ikki marta eslatma olardi.
        foreach (var group in billable.GroupBy(m => m.GroupId))
        {
            var m = MembershipLifecycle.PrimaryMembership(group)
                    ?? group.OrderByDescending(x => x.LeftAt ?? "", StringComparer.Ordinal)
                        .ThenByDescending(x => x.JoinedAt ?? "", StringComparer.Ordinal)
                        .First();
            if (!groups.TryGetValue(m.GroupId, out var g)) continue;
            var ledger = await StudentGroupLedger.BuildAsync(db, s, g, m, book);
            // ⚠️ FAQAT joriy oygacha (2026-10-11). Ledger faol a'zolikka keyingi 3 oyni ham AVANS uchun
            // ko'rsatadi (to'liq oylik "qolgan" bilan) — ilgari ular ham qo'shilib, ota-onaga haqiqiy
            // qarzdan 3 oylik ko'p summa ketardi, qarzi yo'q fan bo'yicha esa "qarz" xabari tuzilardi.
            var current = TuitionService.CurrentMonth();
            var owed = ledger.Months.Where(x => string.CompareOrdinal(x.Month, current) <= 0).Sum(x => x.Remaining);
            if (owed <= 0) continue;
            items.Add(Compose(s, g, ledger.CourseName, owed));
        }

        // GURUHGA BOG'LANMAGAN QARZ: a'zolik bor, lekin hech bir guruh ledger'ida qarz chiqmadi, balans
        // esa manfiy (eski `GroupId`siz hisoblar). Kurs NOMISIZ bitta umumiy eslatma: noto'g'ri guruh
        // nomi (`ClassName`) bilan yozish yolg'on bo'lardi, jim tashlab ketish esa qarzni unuttirardi.
        if (items.Count == 0 && s.Balance < 0)
            items.Add(Compose(s, null, "", -s.Balance));

        return items;
    }

    /// <summary>Bitta fan uchun xabar matni. Matn ATAYIN qisqa: har fan alohida xabar bo'lib
    /// ketadi. "Jami" qatori YO'Q — u fanlarni aralashtirib, xabarni yolg'on qilardi.</summary>
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
