using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Kunlik avtomatik backup (fon xizmati) — har kuni CenterMeta'dagi <c>BackupScheduleHour:Minute</c>
/// (Toshkent) vaqtida markaz ma'lumotlarini JSON qilib Telegram orqali adminga yuboradi
/// (<see cref="BackupService.SendAsync"/>). Sozlama (vaqt, chat, yoqilgan) Sozlamalar → Telegram bot →
/// Backup'dan. Alohida docker konteyner/pg_dump/curl KERAK EMAS.
/// </summary>
public class BackupSchedulerService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<BackupSchedulerService> logger) : BackgroundService
{
    private DateOnly _lastRun = DateOnly.MinValue;

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
                logger.LogError(ex, "Backup jadval siklida xatolik");
            }
            // Daqiqa aniqligi uchun har ~1 daqiqada tekshiramiz.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = AppClock.Now;
        var today = DateOnly.FromDateTime(now);
        if (_lastRun == today) return; // bugun allaqachon yuborilgan

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.TelegramBackupEnabled) return;

        var hour = Math.Clamp(meta.BackupScheduleHour, 0, 23);
        var minute = Math.Clamp(meta.BackupScheduleMinute, 0, 59);
        var target = hour * 60 + minute;
        var nowMin = now.Hour * 60 + now.Minute;
        // Belgilangan vaqtga yetganda (10-daqiqali oyna ichida), kuniga bir marta.
        if (nowMin < target || nowMin >= target + 10) return;
        // `_lastRun` faqat XOTIRADA — oyna ichida restart bo'lsa zaxira IKKINCHI marta ketardi. Shuning
        // uchun bazadagi oxirgi yuborilgan vaqt ham tekshiriladi: bugun belgilangan vaqtdan KEYIN yuborilgan
        // bo'lsa — takrorlanmaydi. (Ertalab qo'lda "Hozir yuborish" bosilgani kechki jadvalni BEKOR QILMAYDI.)
        if (AlreadySentToday(meta.TelegramBackupLastSentAt, today, target))
        {
            _lastRun = today;
            return;
        }

        _lastRun = today;
        var (ok, msg) = await BackupService.SendAsync(db, telegram, logger, ct);
        if (ok) logger.LogInformation("Avtomatik backup yuborildi: {Msg}", msg);
        else logger.LogWarning("Avtomatik backup yuborilmadi: {Msg}", msg);
    }

    /// <summary>Bugungi jadvaldagi zaxira allaqachon yuborilganmi: oxirgi yuborish BUGUN va belgilangan
    /// daqiqadan (<paramref name="targetMinute"/>) keyin bo'lgan (sof funksiya, testlangan).</summary>
    public static bool AlreadySentToday(DateTime? lastSentAt, DateOnly today, int targetMinute) =>
        lastSentAt is { } last
        && DateOnly.FromDateTime(last) == today
        && last.Hour * 60 + last.Minute >= targetMinute;
}
