using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "Erkin eslatma" (fon xizmati) — admin Sozlamalar → Eslatmalarda o'zi yozgan matn, tanlagan
/// auditoriya (o'qituvchilar yoki o'quvchilar/ota-onalar) va jadval (har kuni yoki oyning muayyan
/// kunida, belgilangan soatda) bo'yicha kabinet bildirishnomasi + Telegram orqali avtomatik yuboriladigan qoida
/// (<see cref="WunderkindLC.Domain.AutoMessageRule"/>, Trigger==<see cref="AutoMessageTriggers.CustomSchedule"/>).
///
/// Jadval aniq daqiqa talab qilgani uchun sikl HAR 1 DAQIQADA uyg'onadi (10 daqiqalik oyna).
///
/// <para>⚠️ "Bugun yuborildi" belgisi BAZADA (<see cref="JobRunMarker"/>) — ilgari xotiradagi
/// <c>_sent</c> to'plami edi va oyna ichida deploy/restart bo'lsa qoida HAMMAGA qayta yuborilardi.
/// Endi: har oluvchi yuborishdan OLDIN band qilinadi (<c>custom_schedule:{ruleId}:{s|t}:{id}</c>),
/// qoida to'liq tugagach <c>custom_schedule:{ruleId}</c> belgisi qo'yiladi. Yarmida yiqilsa —
/// oyna ichidagi keyingi daqiqada qolganlarga davom etadi, olganlarga takror ketmaydi.</para>
///
/// <para>⚠️ Oylik qoida 29–31-sanaga qo'yilgan bo'lsa, qisqa oyda OYNING OXIRGI KUNI yuboriladi
/// (<see cref="IsScheduledDay"/>) — ilgari fevral/aprel kabi oylarda umuman o'tkazib yuborilardi.</para>
///
/// <para>Auditoriya: <c>students</c> / <c>parents</c> — o'quvchi kabineti + o'quvchiga ulangan Telegram
/// chatlari ({telefon} tokeni auditoriyaga qarab o'quvchi yoki ota-ona raqami); <c>teachers</c> —
/// o'qituvchilarga. SMS/push 2026-10 da olib tashlandi (<see cref="AutoMessageChannels"/>).</para>
/// </summary>
public class CustomReminderService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<CustomReminderService> logger) : BackgroundService
{
    /// <summary>Belgi kaliti prefiksi: qoida — <c>custom_schedule:{ruleId}</c>, oluvchi —
    /// <c>custom_schedule:{ruleId}:{s|t}:{id}</c>.</summary>
    public const string JobKey = AutoMessageTriggers.CustomSchedule;

    /// <summary>Qoida shu kuni yuborilishi kerakmi. Oylik qoida 29–31-sanaga qo'yilgan bo'lsa
    /// qisqa oyda OYNING OXIRGI KUNI yuboriladi (o'tkazib yuborilmaydi).</summary>
    public static bool IsScheduledDay(AutoMessageRule rule, DateOnly today)
    {
        if (rule.ScheduleType != "monthly") return true;
        var dim = DateTime.DaysInMonth(today.Year, today.Month);
        var day = Math.Clamp(rule.ScheduleDayOfMonth, 1, 31);
        return today.Day == Math.Min(day, dim);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erkin eslatma siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var today = AppClock.Today;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var rules = await db.AutoMessageRules
            .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.CustomSchedule)
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        var now = AppClock.Now;
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var centerName = meta?.Name ?? "";

        foreach (var rule in rules)
        {
            if (!IsScheduledDay(rule, today)) continue;
            if (!TimeSpan.TryParse(rule.ScheduleTime, out var target)) continue;

            var elapsed = now.TimeOfDay - target;
            // 10 daqiqalik oyna — servis biroz kechiksa ham eslatma o'tkazib yuborilmasin.
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(10)) continue;

            // Qoida bugun TO'LIQ yuborilganmi (bazadagi belgi — restartdan keyin ham saqlanadi).
            var ruleKey = JobRunMarker.For(JobKey, rule.Id);
            if (await JobRunMarker.IsDoneAsync(db, ruleKey, today, ct)) continue;

            try
            {
                await SendAsync(db, rule, today, centerName, ct);
                await JobRunMarker.TryClaimAsync(db, ruleKey, today, ct);
                await JobRunMarker.PruneAsync(db, ruleKey + ":", today.AddDays(-2), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erkin eslatma: qoida {Id} uchun xatolik", rule.Id);
            }
        }
    }

    private async Task SendAsync(
        IAppDbContext db, AutoMessageRule rule, DateOnly today, string centerName, CancellationToken ct)
    {
        var ruleKey = JobRunMarker.For(JobKey, rule.Id);
        var title = string.IsNullOrWhiteSpace(rule.Name) ? "Eslatma" : rule.Name;

        if (rule.Audience == "teachers")
        {
            // Akkauntsiz o'qituvchi ham Telegram orqali olishi mumkin (ulanish TeacherId bo'yicha).
            var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct);
            foreach (var t in teachers)
            {
                // OLUVCHI BELGISI — yuborishdan OLDIN (restartdan keyin takror ketmasin).
                if (!await JobRunMarker.TryClaimAsync(db, $"{ruleKey}:t:{t.Id}", today, ct)) continue;
                var body = MessageTokenizer.Teacher(rule.Template, t, centerName);
                await DeliverAsync(db, rule, t.UserId, t.Id, isTeacher: true, title, body, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            var students = await db.Students.Where(s => !s.IsArchived).ToListAsync(ct);
            // Guruhi YOPILGAN/TUGATILGAN o'quvchilar eslatma olmaydi (endi o'qimaydi).
            var closedGroupStudents = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
            students = students.Where(s => !closedGroupStudents.Contains(s.Id)).ToList();
            // Guruh konteksti (asosiy guruh, ClassName bo'yicha) — {dars_sana}/{dars_vaqti}/{dars_kunlari}
            // va {oqituvchi} tokenlari uchun; ro'yxat uchun bir marta yuklanadi.
            var groupByName = (await db.Classes.ToListAsync(ct))
                .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
            var teacherNames = await MessageTokenizer.TeacherNamesByIdAsync(db, ct);
            foreach (var s in students)
            {
                if (!await JobRunMarker.TryClaimAsync(db, $"{ruleKey}:s:{s.Id}", today, ct)) continue;
                // {telefon} tokeni: "students" — o'quvchining O'Z raqami birinchi, "parents" — ota-ona raqami.
                var phone = MessagingAudience.StudentPhone(s, rule.Audience);
                var grp = groupByName.GetValueOrDefault(s.ClassName ?? "");
                var body = MessageTokenizer.Student(rule.Template, s, s.ParentFullName, phone, centerName,
                    group: grp, teacherName: MessageTokenizer.TeacherNameOf(grp, teacherNames));
                await DeliverAsync(db, rule, s.UserId, s.Id, isTeacher: false, title, body, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Bitta oluvchiga yetkazish: kabinet bildirishnomasi (HAR DOIM, akkaunti bo'lsa) + Telegram
    /// (<see cref="AutoMessageChannels.Telegram"/>).</summary>
    private async Task DeliverAsync(
        IAppDbContext db, AutoMessageRule rule, string? userId, string entityId, bool isTeacher,
        string title, string body, CancellationToken ct)
    {
        if (userId is not null)
            NotificationStore.Add(db, userId, title, body, "custom_schedule");

        if (AutoMessageChannels.Telegram(rule) && telegram.IsConfigured)
        {
            var chatIds = isTeacher
                ? await db.TelegramRegistrations.Where(r => r.TeacherId == entityId).Select(r => r.ChatId).Distinct().ToListAsync(ct)
                : await db.TelegramRegistrations.Where(r => r.StudentId == entityId).Select(r => r.ChatId).Distinct().ToListAsync(ct);
            foreach (var chatId in chatIds)
                await telegram.SendMessageAsync(chatId, $"🔔 {title}\n\n{body}", ct: ct);
        }
    }
}
