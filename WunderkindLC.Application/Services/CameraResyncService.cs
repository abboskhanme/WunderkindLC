using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Kameralarni media-shlyuzga (MediaMTX) DAVRIY QAYTA YUBORUVCHI fon xizmati.
///
/// <para><b>Nega kerak:</b> kamera yo'llari (paths) MediaMTX'ning faqat XOTIRASIDA turadi.
/// Shlyuz konteyneri qayta ishga tushsa (yangilanish, server reboot, OOM) hamma yo'l yo'qoladi —
/// 24/7 YOZUV JIMGINA TO'XTAYDI va kimdir kamerani jonli ochmaguncha qayta tiklanmaydi
/// (jonli ochilganda <c>CamerasController.HlsIndex</c> qayta yuboradi, yozuv esa hech kim
/// qaramasa ham kerak). Ilova ham startupda hech narsa yubormasdi — env'dan kelgan
/// <c>Camera:RecordEnabled</c> o'zgarishi esa bazani o'zgartirib, shlyuzni eski holatda qoldirardi.</para>
///
/// <para>Startupdan biroz keyin (ilova va shlyuz ko'tarilsin) va keyin har
/// <see cref="Interval"/> da BARCHA kameralar AYNAN <c>CamerasController.SyncAsync</c> qoidasi bilan
/// (<see cref="CameraRules.ShouldRecordWithNvr"/>) qayta yuboriladi. <c>add</c>→<c>patch</c>
/// idempotent — mavjud yo'l shunchaki yangilanadi, ishlab turgan oqim uzilmaydi.</para>
///
/// <para>Yozilmayotgan kameralarning ESKI segmentlari ham shu yerda saqlash muddati bo'yicha
/// tozalanadi (<see cref="CameraGateway.PruneAsync"/>) — MediaMTX buni faqat yozilayotgan yo'lda o'zi qiladi.</para>
///
/// <para>Holat bazada (kameralar + <see cref="CenterMeta"/>) — xotiradagi hech narsaga tayanmaydi,
/// ya'ni restart ham, takroriy sikl ham zararsiz.</para>
/// </summary>
public class CameraResyncService(IServiceProvider services, ILogger<CameraResyncService> logger) : BackgroundService
{
    /// <summary>Startupdan keyin birinchi sinxrongacha kutish (shlyuz konteyneri ham ko'tarilsin).</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>Davriy qayta yuborish oralig'i.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SyncAllAsync(stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "[kamera] shlyuzga qayta yuborishda xato"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Bitta sikl: hamma kamerani qayta yuboradi. Natija: (yuborildi, xato).</summary>
    public async Task<(int Synced, int Failed)> SyncAllAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var gateway = scope.ServiceProvider.GetRequiredService<CameraGateway>();

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var cams = await db.Cameras.AsNoTracking().ToListAsync(ct);
        if (cams.Count == 0) return (0, 0);

        var nvrOn = NvrArchiveService.IsConfigured(meta);
        var recordOn = meta?.CameraRecordEnabled ?? false;       // fail-closed (cameras.md §2)
        int synced = 0, failed = 0, pruned = 0;
        foreach (var c in cams)
        {
            ct.ThrowIfCancellationRequested();
            var record = CameraRules.ShouldRecordWithNvr(nvrOn, recordOn, c);
            var error = await gateway.EnsureAsync(c, record);
            if (error is null) synced++;
            else
            {
                failed++;
                logger.LogWarning("[kamera] \"{Name}\" shlyuzga yuborilmadi: {Error}", c.Name, error);
            }
            if (!record) pruned += await gateway.PruneAsync(c.Id, c.RetentionDays);
        }
        if (failed > 0 || pruned > 0)
            logger.LogInformation("[kamera] qayta yuborildi: {Synced} ta, xato {Failed} ta, eski segment o'chirildi {Pruned} ta",
                synced, failed, pruned);
        return (synced, failed);
    }
}
