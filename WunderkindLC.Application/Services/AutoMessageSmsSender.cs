using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Avto-xabarlarda (AutoMessageService va 3 ta eslatma HostedService'i) SMS'ni AutoMessageRule.SmsProvider
/// ("eskiz"|"local") bo'yicha yuboradigan umumiy yordamchi — bu naqsh har birida takrorlangani uchun
/// bitta joyga jamlangan.
/// </summary>
public static class AutoMessageSmsSender
{
    /// <summary>Yuborish natijasi: muvaffaqiyatmi, holat matni, so'rov/buyruq identifikatori
    /// (eskiz — Eskiz RequestId callback uchun; local — CtiCommandLog.Id, informatsion).</summary>
    public record SendResult(bool Ok, string Status, string RequestId);

    /// <summary>provider="local" uchun CenterMeta.LocalSmsEnabled, "eskiz" uchun eskiz.IsConfigured(meta).</summary>
    public static bool IsReady(string provider, CenterMeta? meta, EskizService eskiz) =>
        provider == "local" ? meta?.LocalSmsEnabled == true : eskiz.IsConfigured(meta);

    /// <summary>
    /// SMS yuboradi va SmsBatch yozadi (audiens yorlig'i bilan). provider="local" bo'lsa SmsLog
    /// <see cref="CtiSmsService"/> ichida allaqachon yoziladi (qayta yozilmaydi); "eskiz" bo'lsa shu yerda
    /// yoziladi. <paramref name="batchMessage"/> berilmasa SmsBatch.Message ham <paramref name="message"/>
    /// (shaxsiylashtirilgan matn) bo'ladi — berilsa (masalan xom shablon) o'sha ko'rsatiladi.
    /// DIQQAT: bu metod DB'ga entity qo'shadi lekin SaveChanges CHAQIRMAYDI — chaqiruvchi javobgar
    /// (natija muvaffaqiyatsiz bo'lsa ham yozuv saqlanishi kerak — shuning uchun natija emas, "yozildi"
    /// holatini alohida kuzating).
    /// </summary>
    public static async Task<SendResult> SendAsync(
        IAppDbContext db, EskizService eskiz, CtiSmsService ctiSms, string provider,
        string phone, string recipientName, string message, string audienceLabel,
        CancellationToken ct, string? batchMessage = null)
    {
        var batchId = Guid.NewGuid().ToString();
        bool ok; string status; string requestId;
        if (provider == "local")
        {
            var lr = await ctiSms.SendSmsAsync(db, null, phone, message, recipientName, batchId, ct);
            ok = lr.Ok; status = lr.Status; requestId = lr.CommandId;
        }
        else
        {
            var r = await eskiz.SendSmsAsync(db, phone, message, callbackUrl: null, ct);
            ok = r.Ok; status = r.Ok ? r.Status : (r.Error ?? "error"); requestId = r.RequestId;
            db.SmsLogs.Add(new SmsLog
            {
                BatchId = batchId, PhoneNumber = EskizService.NormalizePhone(phone), RecipientName = recipientName,
                Message = message, RequestId = requestId, Status = status,
            });
        }
        db.SmsBatches.Add(new SmsBatch
        {
            Id = batchId, Audience = $"Avto ({audienceLabel}): {recipientName}", Message = batchMessage ?? message,
            SenderUserId = "", SenderName = "Avto xabar", CreatedAt = AppClock.Now,
            RecipientCount = 1, SentCount = ok ? 1 : 0, Provider = provider,
        });
        return new SendResult(ok, status, requestId);
    }
}
