using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
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
/// </summary>
public class AutoMessageService(
    TelegramService telegram,
    ILogger<AutoMessageService> logger)
{
    /// <summary>O'quvchi (ota-ona) uchun avto-xabar — kabinet bildirishnomasi + Telegram (qoidaga qarab).
    /// <paramref name="group"/> — hodisa QAYSI guruhga tegishli (masalan to'lov shu guruh uchun qilindi).
    /// Berilmasa o'quvchining asosiy (ClassName) guruhi olinadi. Guruh dars jadvali tokenlariga
    /// ({dars_kunlari}...) va "o'qituvchiga yuborish" auditoriyasiga ta'sir qiladi.
    /// <para>⚠️ ARXIVDAGI o'quvchiga faqat <see cref="AutoMessageTriggers.ReachesArchived"/> hodisalari
    /// (to'lov kvitansiyasi) ketadi — davomat/baho/qo'shilish xabarlari YUBORILMAYDI (arxiv = o'quvchi
    /// ketdi; eski arxivlarda a'zolik tirik qolgan, jurnal ularni hali ko'rsatishi mumkin).</para></summary>
    public async Task DispatchStudentAsync(
        IAppDbContext db, string trigger, Student s,
        Dictionary<string, string>? extraTokens = null, Group? group = null, CancellationToken ct = default)
    {
        try
        {
            if (s.IsArchived && !AutoMessageTriggers.ReachesArchived(trigger)) return;
            var rules = await RulesAsync(db, trigger, ct);
            if (rules.Count == 0) return;

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

                    // Telefon — faqat {telefon} tokeni uchun. O'quvchi auditoriyasi — o'z raqami birinchi;
                    // aks holda ota-ona birinchi.
                    var phone = audienceStudent
                        ? Coalesce(s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone)
                        : Coalesce(s.ParentPhone, s.FatherPhone, s.MotherPhone, s.Phone);

                    var withExtra = MessageTokenizer.ApplyExtra(rule.Template, extraTokens);
                    var msg = MessageTokenizer.Student(withExtra, s, s.ParentFullName, phone, centerName,
                        extra: null, group: ctxGroup, teacherName: groupTeacherName);
                    var title = Title(rule, trigger);
                    if (string.IsNullOrWhiteSpace(msg)) continue;

                    // Kabinet bildirishnomasi (o'quvchi/o'qituvchi web kabineti, qo'ng'iroqcha) — HAR DOIM
                    // (ilgari faqat push yoqilgan qoidada yozilardi; push 2026-10 da olib tashlandi).
                    var cabinetUserId = audienceTeachers ? teacher!.UserId : s.UserId;
                    if (!string.IsNullOrWhiteSpace(cabinetUserId))
                    {
                        NotificationStore.Add(db, cabinetUserId!, title, msg, trigger);
                        dirty = true;
                    }

                    // Telegram (o'qituvchi auditoriyasida — o'qituvchi chatlari; aks holda o'quvchi chatlari).
                    if (AutoMessageChannels.Telegram(rule) && telegram.IsConfigured)
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

            if (dirty) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Avto-xabar ({Trigger}) o'quvchi {Id} — umumiy xatolik", trigger, s.Id);
        }
    }

    // (DispatchLeadAsync olib tashlandi 2026-10: lid hodisalari — yangi lid, sinov darsi eslatmasi,
    // daraja testi havolasi va natijasi — faqat lid telefoniga SMS edi; lidning Telegram manzili yo'q.)

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

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
