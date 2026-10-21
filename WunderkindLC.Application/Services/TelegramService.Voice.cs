namespace WunderkindLC.Application.Services;

public partial class TelegramService
{
    /// <summary>
    /// Ovozli xabar (sendVoice, multipart) — HR topshiriqlarida rahbarning ovozli izohi xodimga shu bilan
    /// boradi (docs/SPEC-hr-tasks.md). Telegram OGG/Opus'ni "voice" sifatida ko'rsatadi, boshqa formatni
    /// rad etishi mumkin — u holda false (chaqiruvchi so'rovni buzmaydi). Token yo'q bo'lsa — yubormaydi.
    /// </summary>
    public async Task<bool> SendVoiceAsync(
        long chatId, byte[] bytes, string fileName, string contentType, string? caption = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");
            var fileContent = new ByteArrayContent(bytes);
            if (!string.IsNullOrWhiteSpace(contentType))
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "voice", fileName);
            var resp = await Client().PostAsync($"{ApiBase}/sendVoice", form, ct);
            if (resp.IsSuccessStatusCode) return true;
            logger.LogWarning("Telegram sendVoice rad etdi: chat={ChatId} status={Status}", chatId, (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram sendVoice xatosi: chat={ChatId}", chatId);
            return false;
        }
    }
}
