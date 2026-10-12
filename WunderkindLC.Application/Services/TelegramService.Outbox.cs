using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Outbox sending (CRM upgrade P2.4): one classified send — text or media (photo / video / document) with an
/// optional HTML caption and inline keyboard. Used ONLY by <see cref="MessageDeliveryWorker"/>; interactive bot
/// replies keep the older helpers.
/// </summary>
public partial class TelegramService : ITelegramSender
{
    /// <summary>Named client for media uploads: the default <c>"telegram"</c> client's 10 s timeout is too short for a
    /// 50 MB video (registered with 120 s in <c>CrmMessagingSetup</c>).</summary>
    public const string MediaClientName = "telegram-media";

    private HttpClient MediaClient() => httpFactory.CreateClient(MediaClientName);

    public async Task<TgSendOutcome> SendAsync(TgSend msg, CancellationToken ct)
    {
        if (!IsConfigured) return new TgSendOutcome(TgSendResult.Failed, Description: "Telegram bot sozlanmagan");
        try
        {
            HttpResponseMessage resp;
            if (msg.Media is null)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["chat_id"] = msg.ChatId, ["text"] = msg.HtmlText, ["link_preview_options"] = NoLinkPreview,
                };
                if (msg.ParseHtml) payload["parse_mode"] = "HTML";
                if (msg.ReplyMarkup is not null) payload["reply_markup"] = msg.ReplyMarkup;
                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                resp = await Client().PostAsync($"{ApiBase}/sendMessage", content, ct);
            }
            else
            {
                var (method, field) = msg.Media.Kind switch
                {
                    "photo" => ("sendPhoto", "photo"),
                    "video" => ("sendVideo", "video"),
                    _ => ("sendDocument", "document"),
                };
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(msg.ChatId.ToString()), "chat_id");
                if (!string.IsNullOrEmpty(msg.HtmlText))
                {
                    form.Add(new StringContent(msg.HtmlText, Encoding.UTF8), "caption");
                    if (msg.ParseHtml) form.Add(new StringContent("HTML"), "parse_mode");
                }
                if (msg.ReplyMarkup is not null)
                    form.Add(new StringContent(JsonSerializer.Serialize(msg.ReplyMarkup), Encoding.UTF8), "reply_markup");
                if (msg.Media.Kind == "video") form.Add(new StringContent("true"), "supports_streaming");

                if (!string.IsNullOrWhiteSpace(msg.Media.FileId))
                {
                    form.Add(new StringContent(msg.Media.FileId), field);
                }
                else if (msg.Media.OpenStream is not null)
                {
                    // STREAMED from disk — the whole file is never held in memory (1 GB server, 50 MB videos).
                    var file = new StreamContent(msg.Media.OpenStream());
                    file.Headers.ContentType = MediaTypeHeaderValue.TryParse(msg.Media.ContentType, out var mt)
                        ? mt : new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(file, field, string.IsNullOrWhiteSpace(msg.Media.FileName) ? "file" : msg.Media.FileName);
                }
                else return new TgSendOutcome(TgSendResult.Failed, Description: "Fayl topilmadi");

                resp = await MediaClient().PostAsync($"{ApiBase}/{method}", form, ct);
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            var parsed = ParseBody(body);
            if (resp.IsSuccessStatusCode && parsed.Ok is true)
            {
                var (messageId, fileId) = ParseSent(body, msg.Media?.Kind);
                return new TgSendOutcome(TgSendResult.Ok, messageId, fileId);
            }

            var result = ClassifySendError((int)resp.StatusCode, parsed.Description);
            // ⚠️ Log: chat id and status only — never the text, never the token.
            logger.LogWarning("Telegram send rad etdi: {Result} chat={ChatId} status={Status} retryAfter={RetryAfter} ({Description})",
                result, msg.ChatId, (int)resp.StatusCode, parsed.RetryAfterSeconds, parsed.Description);
            return new TgSendOutcome(result, RetryAfterSeconds: parsed.RetryAfterSeconds,
                Description: SanitizeDescription(parsed.Description ?? $"HTTP {(int)resp.StatusCode}"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Telegram send xatosi: chat={ChatId} ({Type})", msg.ChatId, ex.GetType().Name);
            return new TgSendOutcome(TgSendResult.Failed, Description: SanitizeDescription(ex.Message));
        }
    }

    Task<bool> ITelegramSender.EditMessageReplyMarkupAsync(long chatId, long messageId, object? replyMarkup, CancellationToken ct) =>
        EditMessageReplyMarkupAsync(chatId, messageId, replyMarkup, ct);

    Task ITelegramSender.AnswerCallbackAsync(string callbackId, string? text, CancellationToken ct) =>
        AnswerCallbackAsync(callbackId, text, ct);

    /// <summary>
    /// Telegram send error → <see cref="TgSendResult"/> (pure, case-insensitive, <c>TelegramSendErrorTests</c>).
    /// 403 / «bot was blocked by the user» / «user is deactivated» / «bot can't initiate conversation» /
    /// «chat not found» → Blocked; 429 / «Too Many Requests» → RateLimited; 400 «can't parse entities» → BadMarkup;
    /// everything else → Failed.
    /// </summary>
    public static TgSendResult ClassifySendError(int statusCode, string? description)
    {
        var d = description ?? "";
        if (statusCode == 403
            || Has(d, "bot was blocked by the user")
            || Has(d, "user is deactivated")
            || Has(d, "bot can't initiate conversation")
            || Has(d, "chat not found"))
            return TgSendResult.Blocked;
        if (statusCode == 429 || Has(d, "Too Many Requests")) return TgSendResult.RateLimited;
        if (statusCode == 400 && Has(d, "can't parse entities")) return TgSendResult.BadMarkup;
        return TgSendResult.Failed;

        static bool Has(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Error text that may be stored/shown: never the bot token, at most 300 characters.</summary>
    private string SanitizeDescription(string? text)
    {
        var t = text ?? "";
        var token = BotToken;
        if (!string.IsNullOrEmpty(token)) t = t.Replace(token, "***", StringComparison.Ordinal);
        return t.Length > 300 ? t[..300] : t;
    }

    /// <summary>message_id of the sent message + Telegram file_id of an uploaded media (largest photo size).</summary>
    private static (long? MessageId, string? FileId) ParseSent(string body, string? mediaKind)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.Object)
                return (null, null);
            long? messageId = r.TryGetProperty("message_id", out var mid) && mid.ValueKind == JsonValueKind.Number
                ? mid.GetInt64() : null;
            string? fileId = null;
            if (mediaKind == "photo" && r.TryGetProperty("photo", out var photos)
                && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0)
            {
                var last = photos[photos.GetArrayLength() - 1];
                if (last.TryGetProperty("file_id", out var f)) fileId = f.GetString();
            }
            else if (mediaKind is not null)
            {
                // A video may come back as "video", "animation" (no sound) or "document" — the first one present.
                foreach (var key in new[] { "video", "animation", "document" })
                    if (r.TryGetProperty(key, out var m) && m.ValueKind == JsonValueKind.Object
                        && m.TryGetProperty("file_id", out var f))
                    {
                        fileId = f.GetString();
                        break;
                    }
            }
            return (messageId, fileId);
        }
        catch (JsonException) { return (null, null); }
    }
}
