using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Sinov darsi eslatmasi (fon xizmati). Har kuni ~09:00 (Toshkent) da ERTAGA bo'ladigan
/// (Result=="pending") sinov darslari uchun lidga "Sinov darsi eslatmasi" hodisasiga belgilangan
/// andoza bo'yicha SMS yuboradi. {dars_sana}/{dars_vaqti} sinov darsi vaqtidan, {dars_kunlari}
/// (bo'lsa) guruh kunlaridan to'ladi. Andoza yo'q / Eskiz sozlanmagan bo'lsa — jim o'tadi.
/// Kuniga bir marta ishlaydi (oxirgi ishlagan sana xotirada) — har sinov uchun bir marta (bir kun avval).
/// </summary>
public class TrialReminderService(
    IServiceProvider services,
    AutoMessageService autoMsg,
    ILogger<TrialReminderService> logger) : BackgroundService
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
                logger.LogError(ex, "Sinov darsi eslatma SMS siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunAsync(DateOnly today, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // "Sinov darsi eslatmasi" hodisasiga yoqilgan qoida yo'q bo'lsa — umuman ishlamaymiz (so'rovni tejaymiz).
        var hasRule = await db.AutoMessageRules.AnyAsync(
            r => r.Enabled && r.Trigger == AutoMessageTriggers.TrialReminder, ct);
        if (!hasRule) return;

        // ERTAGA bo'ladigan (pending) sinov darslari — ScheduledAt "yyyy-MM-ddTHH:mm" ertangi sana bilan boshlansa.
        var tomorrow = today.AddDays(1).ToString("yyyy-MM-dd");
        var trials = await db.TrialLessons
            .Where(t => t.Result == "pending" && t.ScheduledAt.StartsWith(tomorrow))
            .ToListAsync(ct);
        if (trials.Count == 0) return;

        var sent = 0;
        foreach (var trial in trials)
        {
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == trial.LeadId, ct);
            if (lead is null) continue;
            // Aylantirilgan (o'quvchiga o'tgan) lidga eslatma yubormaymiz.
            if (lead.ConvertedStudentId is not null) continue;
            var group = string.IsNullOrWhiteSpace(trial.GroupId) ? null
                : await db.Classes.FirstOrDefaultAsync(c => c.Id == trial.GroupId, ct);
            await autoMsg.DispatchLeadAsync(db, AutoMessageTriggers.TrialReminder, lead,
                group: group, trialAt: trial.ScheduledAt, ct: ct);
            sent++;
        }
        if (sent > 0) logger.LogInformation("Sinov darsi eslatma SMS: {Count} lidga yuborildi.", sent);
    }
}
