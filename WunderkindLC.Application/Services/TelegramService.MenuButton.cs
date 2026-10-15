using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

public partial class TelegramService
{
    /// <summary>
    /// Botning chat MENYU tugmasini (kiritish maydoni yonidagi) «📱 Panel» Mini App'ga bog'laydi — barcha shaxsiy
    /// chatlar uchun standart. Bir marta, startupda (<c>TelegramBotService</c>).
    /// <para>⚠️ NEGA (2026-10-15): pastki KLAVIATURA tugmasidan ochilgan Mini App Telegram'dan initData OLMAYDI, ya'ni
    /// login'dan keyin chat o'zi ulanmasdi va ota-onadan «🔔 Bot xabarlarini yoqish» bosish talab qilinardi. Menyu
    /// tugmasidan ochilgan Mini App initData oladi — chat login bilan AVTOMATIK ulanadi
    /// (core.telegram.org/bots/webapps).</para>
    /// Xato jim (log) — bot ishlashiga ta'sir qilmaydi.
    /// </summary>
    public async Task SetMenuButtonAsync(string miniAppUrl, string text, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(miniAppUrl)) return;
        try
        {
            var payload = new { menu_button = new { type = "web_app", text, web_app = new { url = miniAppUrl } } };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Client().PostAsync($"{ApiBase}/setChatMenuButton", content, ct);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Telegram setChatMenuButton rad etdi: status={Status}", (int)resp.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram setChatMenuButton xatosi");
        }
    }
}
