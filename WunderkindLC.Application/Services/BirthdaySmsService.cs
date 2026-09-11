using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Tug'ilgan kun avto-SMS (fon xizmati). Har kuni ~09:00 (Toshkent) da tug'ilgan kuni BUGUN
/// bo'lgan (arxivlanmagan) o'quvchilarga "Tug'ilgan kun" hodisasiga belgilangan andoza bo'yicha
/// tabrik SMS yuboradi. Andoza yo'q / Eskiz sozlanmagan bo'lsa — jim o'tadi.
/// Kuniga bir marta ishlaydi (oxirgi ishlagan sana xotirada).
/// </summary>
public class BirthdaySmsService(
    IServiceProvider services,
    AutoMessageService autoMsg,
    ILogger<BirthdaySmsService> logger) : BackgroundService
{
    private const int SendHour = 9;
    private DateOnly _lastRun = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = AppClock.Now;
                var today = DateOnly.FromDateTime(now);
                if (now.Hour >= SendHour && _lastRun != today)
                {
                    _lastRun = today;
                    await RunAsync(today, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tug'ilgan kun SMS siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunAsync(DateOnly today, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // "Tug'ilgan kun" hodisasiga yoqilgan qoida yo'q bo'lsa — umuman ishlamaymiz (so'rovni tejaymiz).
        var hasRule = await db.AutoMessageRules.AnyAsync(
            r => r.Enabled && r.Trigger == AutoMessageTriggers.Birthday, ct);
        if (!hasRule) return;

        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync(ct);
        // Guruhi YOPILGAN/TUGATILGAN o'quvchiga tabrik yuborilmaydi (u endi markazda o'qimaydi).
        var closedGroupStudents = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        var sent = 0;
        foreach (var s in students)
        {
            if (!IsBirthdayToday(s.BirthDate, today)) continue;
            if (closedGroupStudents.Contains(s.Id)) continue;
            await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.Birthday, s, ct: ct);
            sent++;
        }
        if (sent > 0) logger.LogInformation("Tug'ilgan kun SMS: {Count} o'quvchiga yuborildi.", sent);
    }

    /// <summary>BirthDate (turli formatlar) bugungi oy+kunga to'g'ri keladimi.</summary>
    private static bool IsBirthdayToday(string? birthDate, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(birthDate)) return false;
        if (DateTime.TryParse(birthDate, out var d))
            return d.Month == today.Month && d.Day == today.Day;
        return false;
    }
}
