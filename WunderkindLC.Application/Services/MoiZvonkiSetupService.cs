using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// MoiZvonki webhook AVTO-OBUNASI — start'da bir marta: bizning webhook URL
/// (https://{App:Host}/api/telephony/moizvonki/{secret}) provayderga ro'yxatdan o'tkaziladi.
/// Qayta chaqirish xavfsiz — provayder eski URL'ni yangisiga almashtiradi. App:Host bo'sh
/// bo'lsa (dev) o'tkazib yuboriladi; qo'lda variant — POST api/admin/calls/telephony/subscribe.
/// </summary>
public class MoiZvonkiSetupService(
    MoiZvonkiService moizvonki, IConfiguration config, ILogger<MoiZvonkiSetupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!moizvonki.IsConfigured) return;
        var host = config["App:Host"] ?? "";
        if (host.Length == 0)
        {
            logger.LogInformation("MoiZvonki: App:Host yo'q (dev?) — webhook avto-obuna o'tkazib yuborildi");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } // app to'liq ko'tarilsin
        catch (TaskCanceledException) { return; }

        var url = $"https://{host}/api/telephony/moizvonki/{moizvonki.WebhookSecret}";
        var (ok, body) = await moizvonki.SubscribeWebhooksAsync(url, stoppingToken);
        if (ok)
            logger.LogInformation("MoiZvonki webhook obunasi yangilandi: {Url}", url);
        else
            logger.LogWarning("MoiZvonki webhook obunasi MUVAFFAQIYATSIZ: {Body} — " +
                "qo'lda: POST api/admin/calls/telephony/subscribe (javobni ko'rish uchun)", body);
    }
}
