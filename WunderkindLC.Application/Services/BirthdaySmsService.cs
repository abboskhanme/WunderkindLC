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
/// <para>⚠️ Kuniga bir marta — belgi BAZADA (<see cref="JobRunMarker"/>): har o'quvchi yuborishdan
/// OLDIN band qilinadi (<c>birthday:{studentId}</c>), ish tugagach <c>birthday</c> belgisi qo'yiladi.
/// Ilgari belgi xotirada edi va har deploy (09:00 dan keyin) tabrik SMS'ini qayta yuborardi.</para>
/// </summary>
public class BirthdaySmsService(
    IServiceProvider services,
    AutoMessageService autoMsg,
    ILogger<BirthdaySmsService> logger) : BackgroundService
{
    private const int SendHour = 9;

    /// <summary>Ish darajasidagi belgi kaliti (oluvchi kaliti — <c>birthday:{studentId}</c>).</summary>
    public const string JobKey = AutoMessageTriggers.Birthday;

    /// <summary>Shu jarayonda bugun tekshirildimi — faqat TEZLIK uchun (haqiqiy belgi bazada).</summary>
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
                logger.LogError(ex, "Tug'ilgan kun SMS siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>09:00 dan keyin, bugungi ish bazada bajarilmagan bo'lsa — yuboradi va belgilaydi.</summary>
    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        if (now.Hour < SendHour || _checked == today) return;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await JobRunMarker.IsDoneAsync(db, JobKey, today, ct)) { _checked = today; return; }
        }
        await RunAsync(today, ct);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await JobRunMarker.TryClaimAsync(db, JobKey, today, ct);
            await JobRunMarker.PruneAsync(db, JobKey + ":", today.AddDays(-2), ct);
        }
        _checked = today;
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
            // OLUVCHI BELGISI — yuborishdan OLDIN (restart/ikkinchi nusxa takror tabriklamasin).
            if (!await JobRunMarker.TryClaimAsync(db, JobRunMarker.For(JobKey, s.Id), today, ct)) continue;
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
