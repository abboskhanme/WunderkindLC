using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Kunlik markaz AI tahlili (fon xizmati) — har kuni CenterMeta'dagi <c>AiDailyAnalysisHour</c>
/// (Toshkent, default 8:00) da markazning bir kun oldingi/joriy oy ma'lumotlari asosida AI tahlil
/// yaratadi va saqlaydi (<see cref="CenterAiAnalysisService.GenerateAsync"/>). Tahlil yoqilgan
/// (<c>AiDailyAnalysisEnabled</c>) va Gemini kaliti sozlangan bo'lsagina ishlaydi.
/// </summary>
public class CenterAiSchedulerService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<CenterAiSchedulerService> logger) : BackgroundService
{
    private DateOnly _lastRun = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Kunlik AI tahlil siklida xatolik"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = AppClock.Now;
        var today = DateOnly.FromDateTime(now);
        if (_lastRun == today) return; // bugun allaqachon bajarilgan

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.AiDailyAnalysisEnabled) return;
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey)) return;

        var hour = Math.Clamp(meta.AiDailyAnalysisHour, 0, 23);
        var target = hour * 60; // soat:00
        var nowMin = now.Hour * 60 + now.Minute;
        // Belgilangan soatga yetganda (10-daqiqali oyna ichida), kuniga bir marta.
        if (nowMin < target || nowMin >= target + 10) return;

        // Bugun allaqachon yozuv bo'lsa — qayta yaratmaymiz.
        var todayStr = today.ToString("yyyy-MM-dd");
        if (await db.CenterAiAnalyses.AnyAsync(a => a.Date == todayStr, ct)) { _lastRun = today; return; }

        _lastRun = today;
        var res = await CenterAiAnalysisService.GenerateAsync(db, config, force: false, ct);
        if (res.Ok) logger.LogInformation("Kunlik markaz AI tahlili yaratildi ({Date})", todayStr);
        else logger.LogWarning("Kunlik markaz AI tahlili yaratilmadi: {Err}", res.Error);
    }
}
