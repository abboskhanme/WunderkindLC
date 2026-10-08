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
///
/// <para>🔴 <b>Bitta urinish EMAS — muvaffaqiyatgacha.</b> Ilgari "bugun bajarildi" belgisi
/// Gemini chaqiruvidan OLDIN qo'yilardi va ish faqat 10 daqiqalik oynada boshlanardi: Gemini
/// o'sha paytda yiqilsa (timeout, kvota) yoki server o'sha 10 daqiqada qayta ishga tushayotgan
/// bo'lsa, kunlik tahlil o'sha kuni UMUMAN yaratilmasdi. Endi belgilangan soatdan KEYIN kun
/// oxirigacha (<see cref="RetryMinutes"/> oraliq bilan, ko'pi bilan <see cref="MaxAttemptsPerDay"/>
/// marta) qayta urinadi.</para>
///
/// <para>⚠️ Takror YARATILMAYDI: haqiqat manbai — BAZA (<c>CenterAiAnalyses</c> da bugungi yozuv
/// bormi), xotiradagi belgilar faqat ortiqcha so'rovni kamaytiradi. Restartdan keyin ham bugungi
/// yozuv bo'lsa hech narsa qilinmaydi.</para>
/// </summary>
public class CenterAiSchedulerService(
    IServiceProvider services,
    IConfiguration config,
    ILogger<CenterAiSchedulerService> logger) : BackgroundService
{
    /// <summary>Muvaffaqiyatsiz urinishdan keyin qayta urinish oralig'i (daqiqa).</summary>
    public const int RetryMinutes = 30;

    /// <summary>Bir kunda ko'pi bilan shuncha urinish — kalit noto'g'ri bo'lsa Gemini'ga kun
    /// bo'yi bekorga so'rov ketmasin.</summary>
    public const int MaxAttemptsPerDay = 8;

    private DateOnly _doneDay = DateOnly.MinValue;
    private DateOnly _attemptDay = DateOnly.MinValue;
    private int _attempts;
    private DateTime _nextAttemptAt = DateTime.MinValue;

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

    /// <summary>
    /// Hozir urinish kerakmi — SOF qoida (testlangan).
    /// <para>Belgilangan soatdan KEYIN (kun oxirigacha), bugungi urinishlar chegarasi tugamagan
    /// va qayta urinish vaqti kelgan bo'lsa.</para>
    /// </summary>
    public static bool ShouldAttempt(DateTime now, int hour, int attemptsToday, DateTime nextAttemptAt)
    {
        var target = Math.Clamp(hour, 0, 23) * 60;
        var nowMin = now.Hour * 60 + now.Minute;
        if (nowMin < target) return false;
        if (attemptsToday >= MaxAttemptsPerDay) return false;
        return now >= nextAttemptAt;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = AppClock.Now;
        var today = DateOnly.FromDateTime(now);
        if (_doneDay == today) return; // bugun allaqachon yaratilgan

        if (_attemptDay != today)
        {
            _attemptDay = today;
            _attempts = 0;
            _nextAttemptAt = DateTime.MinValue;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.AiDailyAnalysisEnabled) return;
        if (!GeminiService.IsConfigured(AppSecrets.GeminiApiKey)) return;

        if (!ShouldAttempt(now, meta.AiDailyAnalysisHour, _attempts, _nextAttemptAt)) return;

        // Bugun allaqachon yozuv bo'lsa — qayta yaratmaymiz (baza — haqiqat manbai).
        var todayStr = today.ToString("yyyy-MM-dd");
        if (await db.CenterAiAnalyses.AnyAsync(a => a.Date == todayStr, ct)) { _doneDay = today; return; }

        _attempts++;
        var res = await CenterAiAnalysisService.GenerateAsync(db, config, force: false, ct);
        if (res.Ok)
        {
            _doneDay = today;
            logger.LogInformation("Kunlik markaz AI tahlili yaratildi ({Date})", todayStr);
            return;
        }

        _nextAttemptAt = now.AddMinutes(RetryMinutes);
        logger.LogWarning(
            "Kunlik markaz AI tahlili yaratilmadi ({Attempt}/{Max}-urinish, keyingisi {Next:HH:mm}): {Err}",
            _attempts, MaxAttemptsPerDay, _nextAttemptAt, res.Error);
    }
}
