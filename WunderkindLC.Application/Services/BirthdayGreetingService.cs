using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Tug'ilgan kun tabrigi (fon xizmati). Tug'ilgan kuni BUGUN bo'lgan (arxivlanmagan, guruhi yopilmagan)
/// o'quvchilarga "Tug'ilgan kun" hodisasining HAR yoqilgan qoidasi bo'yicha tabrik (kabinet + Telegram outbox —
/// <see cref="AutoMessageService"/>). Qoida yo'q bo'lsa — jim o'tadi.
/// <para>⚠️ CRM upgrade (2026-10-12, P2.5): har qoida O'Z soatidan keyin (<c>ScheduleTime</c>, standart 09:00)
/// ishlaydi; xizmat har 10 daqiqada uyg'onadi. Kuniga bir marta — belgi BAZADA (<see cref="JobRunMarker"/>):
/// oluvchi <c>birthday:{ruleId}:{studentId}</c> yuborishdan OLDIN band qilinadi, qoida tugagach
/// <c>birthday:{ruleId}</c>. Outbox dedupe kaliti sana bilan (<c>eventKey = yyyy-MM-dd</c>).</para>
/// </summary>
public class BirthdayGreetingService(
    IServiceProvider services,
    AutoMessageService autoMsg,
    ILogger<BirthdayGreetingService> logger) : BackgroundService
{
    /// <summary>Kalitlar prefiksi: qoida — <c>birthday:{ruleId}</c>, oluvchi — <c>birthday:{ruleId}:{studentId}</c>.</summary>
    public const string JobKey = AutoMessageTriggers.Birthday;

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
                logger.LogError(ex, "Tug'ilgan kun tabrigi siklida xatolik");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Vaqti kelgan va bugun bajarilmagan har qoida — yuboradi va belgilaydi.</summary>
    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        List<AutoMessageRule> rules;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            rules = await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.Birthday).ToListAsync(ct);
        }
        foreach (var rule in rules.Where(r => AutoMessageSchedule.IsDue(r, now)))
        {
            var key = JobRunMarker.For(JobKey, rule.Id);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                if (await JobRunMarker.IsDoneAsync(db, key, today, ct)) continue;
            }
            await RunRuleAsync(rule.Id, today, ct);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                await JobRunMarker.TryClaimAsync(db, key, today, ct);
                await JobRunMarker.PruneAsync(db, JobKey + ":", today.AddDays(-2), ct);
            }
        }
    }

    /// <summary>Bugungi barcha yoqilgan qoidalar (soatidan qat'i nazar) — qo'lda/test yo'li.</summary>
    private async Task RunAsync(DateOnly today, CancellationToken ct)
    {
        List<string> ruleIds;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            ruleIds = await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.Birthday).Select(r => r.Id).ToListAsync(ct);
        }
        foreach (var id in ruleIds) await RunRuleAsync(id, today, ct);
    }

    private async Task RunRuleAsync(string ruleId, DateOnly today, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync(ct);
        // Guruhi YOPILGAN/TUGATILGAN o'quvchiga tabrik yuborilmaydi (u endi markazda o'qimaydi).
        var closedGroupStudents = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        var ruleKey = JobRunMarker.For(JobKey, ruleId);
        var day = today.ToString("yyyy-MM-dd");
        var sent = 0;
        foreach (var s in students)
        {
            if (!IsBirthdayToday(s.BirthDate, today)) continue;
            if (closedGroupStudents.Contains(s.Id)) continue;
            // OLUVCHI BELGISI — yuborishdan OLDIN (restart/ikkinchi nusxa takror tabriklamasin).
            if (!await JobRunMarker.TryClaimAsync(db, JobRunMarker.For(ruleKey, s.Id), today, ct)) continue;
            await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.Birthday, s, ct: ct,
                eventKey: day, sourceId: ruleId, onlyRuleId: ruleId);
            sent++;
        }
        if (sent > 0) logger.LogInformation("Tug'ilgan kun tabrigi (qoida {Rule}): {Count} o'quvchiga yuborildi.", ruleId, sent);
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
