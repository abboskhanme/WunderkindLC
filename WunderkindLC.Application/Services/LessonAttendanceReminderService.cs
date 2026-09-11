using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchiga "davomat kiriting" eslatmasi (fon xizmati). Qoida <see cref="WunderkindLC.Domain.AutoMessageRule.SendScope"/>
/// bo'yicha 3 rejimda ishlaydi:
/// <list type="bullet">
/// <item>"lesson_start" (default) — har faol guruh uchun, dars boshlanish vaqtidan (<see cref="Group.StartTime"/>)
/// qoida bo'yicha belgilangan daqiqa (odatda 5) o'tgach, agar shu kunga hali davomat kiritilmagan bo'lsa
/// (<see cref="LessonNote.Conducted"/>), guruh o'qituvchisiga yuboriladi.</item>
/// <item>"not_filled" — kunlik <see cref="WunderkindLC.Domain.AutoMessageRule.ScheduleTime"/>da: bugun darsi bo'lib (boshlangan)
/// davomatini HALI kiritmagan o'qituvchilarga, har to'ldirilmagan guruh uchun alohida.</item>
/// <item>"all" — kunlik <see cref="WunderkindLC.Domain.AutoMessageRule.ScheduleTime"/>da: BARCHA faol o'qituvchilarga
/// (davomatni to'ldirganlarga ham).</item>
/// </list>
/// Kanal: push (FCM) + Telegram + ichki bildirishnoma. Andoza/vaqt "Sozlamalar → Eslatmalar"da boshqariladi;
/// bir nechta qoida bo'lishi mumkin. Dars vaqti aniq daqiqa talab qilgani uchun sikl HAR 1 DAQIQADA uyg'onadi.
/// Bir marta yuborilishi uchun xotirada (ruleId, groupId/daily, sana) kaliti saqlanadi — kun almashganda tozalanadi.
/// </summary>
public class LessonAttendanceReminderService(
    IServiceProvider services,
    TelegramService telegram,
    FcmService fcm,
    EskizService eskiz,
    CtiSmsService ctiSms,
    ILogger<LessonAttendanceReminderService> logger) : BackgroundService
{
    private readonly HashSet<string> _processed = new();
    private DateOnly _processedDate = DateOnly.MinValue;

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
                logger.LogError(ex, "Davomat eslatmasi siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var today = AppClock.Today;
        if (_processedDate != today)
        {
            _processedDate = today;
            _processed.Clear();
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var rules = await db.AutoMessageRules
            .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.LessonAttendance)
            .ToListAsync(ct);
        if (rules.Count == 0) return;

        var weekday = ((int)today.DayOfWeek + 6) % 7;
        // Kunlik rejimlar uchun StartTime bo'sh guruhlar ham kiradi (vaqtini bilib bo'lmasa ham eslatiladi);
        // "lesson_start" rejimi baribir StartTime'siz guruhni o'tkazib yuboradi.
        var groups = await db.Classes
            .Where(g => !g.IsArchived && g.TeacherId != "")
            .ToListAsync(ct);
        groups = groups.Where(g => g.Days.Contains(weekday)).ToList();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var fcmJson = AppSecrets.FcmServiceAccountJson;
        var pushReady = FcmService.IsConfigured(fcmJson);
        var todayStr = today.ToString("yyyy-MM-dd");
        var now = AppClock.Now;
        var deadTokens = new List<string>();

        foreach (var rule in rules)
        {
            if (rule.SendScope is ReminderSendScopes.All or ReminderSendScopes.NotFilled)
            {
                // Kunlik rejim: belgilangan vaqtda bir marta.
                if (!TimeSpan.TryParse(rule.ScheduleTime, out var at)) continue;
                var passed = now.TimeOfDay - at;
                // 10 daqiqalik oyna — servis biroz kechiksa ham eslatma o'tkazib yuborilmasin.
                if (passed < TimeSpan.Zero || passed > TimeSpan.FromMinutes(10)) continue;
                if (!_processed.Add($"{rule.Id}:daily:{todayStr}")) continue;

                try
                {
                    await SendDailyAsync(db, rule, groups, todayStr, now, meta?.Name ?? "",
                        fcmJson, pushReady, meta, deadTokens, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Davomat eslatmasi (kunlik): qoida {Id} uchun xatolik", rule.Id);
                }
                continue;
            }

            // "lesson_start" (default): har guruh darsi boshlangach +N daqiqada.
            foreach (var g in groups)
            {
                if (!TimeSpan.TryParse(g.StartTime, out var start)) continue;
                var target = start.Add(TimeSpan.FromMinutes(rule.OffsetMinutes));
                var elapsed = now.TimeOfDay - target;
                // 10 daqiqalik oyna — servis biroz kechiksa ham eslatma o'tkazib yuborilmasin.
                if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMinutes(10)) continue;

                var key = $"{rule.Id}:{g.Id}:{todayStr}";
                if (!_processed.Add(key)) continue;

                try
                {
                    await SendAsync(db, rule, g, todayStr, meta?.Name ?? "", fcmJson, pushReady, meta, deadTokens, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Davomat eslatmasi: guruh {Id} uchun xatolik", g.Id);
                }
            }
        }

        if (deadTokens.Count > 0)
            db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => deadTokens.Contains(d.Token)));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Kunlik rejim: "all" — bugun darsi bor-yo'qligidan va to'ldirganidan qat'i nazar BARCHA faol
    /// o'qituvchilarga bittadan; "not_filled" — bugun darsi bo'lib (boshlangan) davomati hali
    /// kiritilmagan har bir guruh uchun o'z o'qituvchisiga.
    /// </summary>
    private async Task SendDailyAsync(
        IAppDbContext db, AutoMessageRule rule, List<Group> todayGroups, string todayStr, DateTime now,
        string centerName, string fcmJson, bool pushReady, CenterMeta? meta, List<string> deadTokens, CancellationToken ct)
    {
        if (rule.SendScope == ReminderSendScopes.All)
        {
            var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync(ct);
            var template = string.IsNullOrWhiteSpace(rule.Template)
                ? "Assalomu alaykum, {fish}! Iltimos, bugungi darslaringiz davomatini jurnalga kiritishni unutmang."
                : rule.Template;
            var title = string.IsNullOrWhiteSpace(rule.Name) ? "Davomat eslatmasi" : rule.Name;
            foreach (var teacher in teachers)
            {
                var body = MessageTokenizer.Teacher(template, teacher, centerName);
                await DeliverAsync(db, rule, teacher, title, body, fcmJson, pushReady, meta, deadTokens, ct);
            }
            return;
        }

        // "not_filled": faqat boshlangan (yoki vaqti kiritilmagan) darslar — hali bo'lmagan darsni so'ramaymiz.
        foreach (var g in todayGroups)
        {
            if (TimeSpan.TryParse(g.StartTime, out var start) && start > now.TimeOfDay) continue;
            await SendAsync(db, rule, g, todayStr, centerName, fcmJson, pushReady, meta, deadTokens, ct);
        }
    }

    private async Task SendAsync(
        IAppDbContext db, AutoMessageRule rule, Group g, string todayStr, string centerName,
        string fcmJson, bool pushReady, CenterMeta? meta, List<string> deadTokens, CancellationToken ct)
    {
        // Davomat allaqachon kiritilgan bo'lsa — jim o'tkaziladi.
        var conducted = await db.LessonNotes.AnyAsync(n =>
            n.ClassId == g.Id && n.SubjectId == g.CourseId && n.Quarter == 1 &&
            n.Date == todayStr && n.Conducted, ct);
        if (conducted) return;

        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Id == g.TeacherId && !t.IsArchived, ct);
        if (teacher is null) return;

        var courseName = string.IsNullOrEmpty(g.CourseId) ? "" : (await db.Subjects.FindAsync(g.CourseId))?.Name ?? "";
        var template = string.IsNullOrWhiteSpace(rule.Template)
            ? "Assalomu alaykum, {fish}! {guruh} guruhida ({kurs}) dars boshlandi ({dars_vaqti}). Iltimos, davomatni jurnalga kiriting."
            : rule.Template;
        var body = MessageTokenizer.Teacher(template, teacher, centerName,
            extra: new Dictionary<string, string> { ["{kurs}"] = courseName }, group: g);
        var title = string.IsNullOrWhiteSpace(rule.Name) ? "Davomat eslatmasi" : rule.Name;

        await DeliverAsync(db, rule, teacher, title, body, fcmJson, pushReady, meta, deadTokens, ct);
    }

    /// <summary>Bitta o'qituvchiga qoidada YOQILGAN kanallar orqali yetkazish: push (FCM) + Telegram + SMS.</summary>
    private async Task DeliverAsync(
        IAppDbContext db, AutoMessageRule rule, Teacher teacher, string title, string body,
        string fcmJson, bool pushReady, CenterMeta? meta, List<string> deadTokens, CancellationToken ct)
    {
        if (rule.SendPush && teacher.UserId is not null)
        {
            NotificationStore.Add(db, teacher.UserId, title, body, "lesson_attendance");
            if (pushReady)
            {
                var tokens = await db.DeviceTokens.Where(d => d.UserId == teacher.UserId)
                    .Select(d => d.Token).Distinct().ToListAsync(ct);
                if (tokens.Count > 0)
                {
                    var res = await fcm.SendAsync(fcmJson, tokens, title, body, ct);
                    deadTokens.AddRange(res.InvalidTokens);
                }
            }
        }

        if (rule.SendTelegram && telegram.IsConfigured)
        {
            var chatIds = await db.TelegramRegistrations
                .Where(r => r.TeacherId == teacher.Id)
                .Select(r => r.ChatId).Distinct().ToListAsync(ct);
            foreach (var chatId in chatIds)
                await telegram.SendMessageAsync(chatId, $"🔔 {title}\n\n{body}", ct: ct);
        }

        if (rule.SendSms && !string.IsNullOrWhiteSpace(teacher.Phone) && AutoMessageSmsSender.IsReady(rule.SmsProvider, meta, eskiz))
        {
            await AutoMessageSmsSender.SendAsync(
                db, eskiz, ctiSms, rule.SmsProvider, teacher.Phone, teacher.FullName, body, "Davomat eslatmasi", ct);
        }
    }
}
