using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QITUVCHI BOTI uchun Telegram Bot API mijozi — ALOHIDA token bilan
/// (<see cref="AppSecrets.TeacherBotToken"/>, <c>.env: TEACHER_BOT_TOKEN</c>).
///
/// <para>Nega alohida servis: <see cref="TelegramService"/> markazning ASOSIY botiga (ota-onalar,
/// e'lonlar, onlayn test, kitob sotuvi) bog'langan. O'qituvchi boti mustaqil BotFather boti —
/// uning long polling'i ham mustaqil bo'lishi kerak. <see cref="CareerTelegramService"/> bilan bir
/// xil naqsh; metodlar to'plami ATAYIN kichik: botning butun ishi — bitta tugma bilan Mini App.</para>
/// </summary>
public class TeacherTelegramService(IHttpClientFactory httpFactory, ILogger<TeacherTelegramService> logger)
{
    private volatile string _username = "";

    public string BotToken => AppSecrets.TeacherBotToken;
    /// <summary>Bot username'i (<c>getMe</c> orqali startupda aniqlanadi).</summary>
    public string BotUsername => _username;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    private HttpClient Client() => httpFactory.CreateClient("telegram");

    /// <summary>Long polling uchun mijoz — uzoqroq timeout bilan (sababi
    /// <see cref="CareerTelegramService"/> dagi bilan bir xil: "telegram" mijozining qisqa
    /// timeout'i <c>getUpdates</c> ni har safar uzib qo'yardi).</summary>
    private HttpClient PollingClient(int timeoutSec)
    {
        var client = Client();
        client.Timeout = TimeSpan.FromSeconds(Math.Max(timeoutSec, 0) + 15);
        return client;
    }

    private string ApiBase => $"https://api.telegram.org/bot{BotToken}";

    /// <summary>Matn yuboradi (ixtiyoriy reply_markup / parse_mode bilan). Muvaffaqiyat — true.</summary>
    public async Task<bool> SendMessageAsync(
        long chatId, string text, object? replyMarkup = null, CancellationToken ct = default,
        string? parseMode = null)
    {
        if (!IsConfigured) return false;
        try
        {
            var payload = new Dictionary<string, object?> { ["chat_id"] = chatId, ["text"] = text };
            if (replyMarkup is not null) payload["reply_markup"] = replyMarkup;
            if (parseMode is not null) payload["parse_mode"] = parseMode;
            var resp = await PostAsync("sendMessage", payload, ct);
            return resp;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "O'qituvchi boti: sendMessage xatosi");
            return false;
        }
    }

    /// <summary>Inline tugma bosilganidagi "soatchani" to'xtatadi.</summary>
    public async Task AnswerCallbackAsync(string callbackId, CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        try { await PostAsync("answerCallbackQuery", new { callback_query_id = callbackId }, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "O'qituvchi boti: answerCallbackQuery xatosi"); }
    }

    /// <summary>Long polling — yangi update'lar (null = xato/sozlanmagan).</summary>
    public async Task<JsonElement?> GetUpdatesAsync(long offset, int timeoutSec, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            var url = $"{ApiBase}/getUpdates?offset={offset}&timeout={timeoutSec}"
                      + "&allowed_updates=[\"message\",\"callback_query\"]";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await PollingClient(timeoutSec).SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            return result.Clone();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // ⚠️ Istisno matnini emas, faqat turini yozamiz: HttpClient xatosi so'rov manzilini
            // (ichida TOKEN bor) o'z ichiga olishi mumkin.
            logger.LogWarning("O'qituvchi boti: getUpdates xatosi ({Type})", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Bot username'ini aniqlaydi (<c>getMe</c>) va xotiraga oladi.</summary>
    public async Task<string> RefreshUsernameAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return "";
        try
        {
            var resp = await Client().GetAsync($"{ApiBase}/getMe", ct);
            if (!resp.IsSuccessStatusCode) return _username;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("result", out var r) &&
                r.TryGetProperty("username", out var u))
                _username = u.GetString() ?? "";
            return _username;
        }
        catch (Exception ex)
        {
            logger.LogWarning("O'qituvchi boti: getMe xatosi ({Type})", ex.GetType().Name);
            return _username;
        }
    }

    /// <summary>
    /// Kiritish maydoni CHAPIDAGI (ko'k) menyu tugmasini OLIB TASHLAYDI: menyu tugmasi standart
    /// holatga qaytariladi va buyruqlar ro'yxati tozalanadi (buyruqsiz standart tugma chizilmaydi).
    /// Foydalanuvchi talabi: Mini App faqat PASTDAGI bitta tugmadan ochilsin.
    /// Startupda chaqiriladi — BotFather'da keyin qo'lda o'rnatilgan tugma ham qaytib tozalanadi.
    /// </summary>
    public async Task RemoveMenuButtonAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return;
        try
        {
            await PostAsync("setChatMenuButton", MenuButtonPayload, ct);
            await PostAsync("deleteMyCommands", new { }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("O'qituvchi boti: menyu tugmasini tozalab bo'lmadi ({Type})", ex.GetType().Name);
        }
    }

    /// <summary><c>setChatMenuButton</c> tanasi — standart (web_app EMAS) tugma.</summary>
    public static readonly object MenuButtonPayload = new { menu_button = new { type = "default" } };

    private async Task<bool> PostAsync(string method, object payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await Client().PostAsync($"{ApiBase}/{method}", content, ct);
        return resp.IsSuccessStatusCode;
    }
}
