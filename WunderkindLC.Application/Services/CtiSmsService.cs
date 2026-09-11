using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Local SMS — CTI (Local Call) agent telefonining SIM-kartasidan ixtiyoriy matnli SMS yuborish.
/// Eskiz'ga muqobil "provider" sifatida MessagesController, AutoMessageService va Local Call
/// operator paneli (<c>CtiController</c>) tomonidan ishlatiladi — bittasi yetkazish (WS/FCM+poll,
/// <see cref="CtiCommandLog"/>) va SmsLog yozuvini markazlashtiradi, shu bilan Tarix bitta joyda qoladi.
/// </summary>
public class CtiSmsService(CtiConnectionManager conn, FcmService fcm, ILogger<CtiSmsService> log)
{
    public record LocalSmsResult(bool Ok, string CommandId, string Status, string? Error);

    /// <summary>
    /// Agent shu muddatdan beri ko'rinmagan bo'lsa — uni "o'lik" deb hisoblaymiz va FCM bilan
    /// uyg'otishga URINMAYMIZ.
    /// </summary>
    /// <remarks>
    /// <para>⚠️ Nega kerak (2026-09-07 da topilgan): ikkala CTI agent ham <b>3 haftadan beri</b>
    /// oflayn edi, lekin `SmsProvider='local'` bo'lgan avto-qoidalar har kuni ishlayverardi.
    /// Har urinish: bitta bekor FCM push + <b>6 sekund</b> kutish (12 × 500 ms poll) + "yetkazilmadi"
    /// yozuvi. Kuniga 20+ marta. Log'da esa HECH NARSA yo'q edi — faqat `SmsLogs.Status`.</para>
    ///
    /// <para>⚠️ Chegara ATAYIN uzoq (7 kun): FCM bilan uyg'otish yo'li aynan "ilova fonda"
    /// holati uchun. Bir necha soat ko'rinmagan agent normal holat — uni o'tkazib yuborsak
    /// ISHLAYOTGAN sozlamani buzardik. 7 kun esa "token eskirgan, ilova o'chirilgan".</para>
    /// </remarks>
    private static readonly TimeSpan StaleAgentAfter = TimeSpan.FromDays(7);

    /// <summary>
    /// Ketma-ket yuborishlar orasidagi minimal masofani ta'minlaydi (CenterMeta.LocalSmsDelaySeconds
    /// sozlangan bo'lsa) — barcha chaqiruvchilar (broadcast, lidlarga ommaviy SMS, avto-xabar
    /// trigger'lari, eslatma HostedService'lari) shu YAGONA joydan o'tgani uchun alohida-alohida
    /// har bir bulk-loop'da bloklamaslikning hojati yo'q. Statik (instance DI lifetime'idan qat'i
    /// nazar to'g'ri ishlashi uchun) — jarayon darajasida bitta hisoblagich.
    /// </summary>
    private static readonly SemaphoreSlim PaceLock = new(1, 1);
    private static DateTime _lastSentUtc = DateTime.MinValue;

    private static async Task PaceAsync(int delaySeconds, CancellationToken ct)
    {
        if (delaySeconds <= 0) return;
        await PaceLock.WaitAsync(ct);
        try
        {
            var wait = TimeSpan.FromSeconds(delaySeconds) - (DateTime.UtcNow - _lastSentUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastSentUtc = DateTime.UtcNow;
        }
        finally
        {
            PaceLock.Release();
        }
    }

    /// <summary>
    /// SMS yuboradi va natijani <see cref="SmsLog"/>ga yozadi (Provider="local"). <paramref name="agentId"/>
    /// berilmasa — CenterMeta.LocalSmsDefaultAgentId ishlatiladi (avtomatik/fon xabarlar shu yo'l bilan).
    /// <paramref name="batchId"/> chaqiruvchining O'ZI bir SmsBatch yaratadigan hollarda beriladi
    /// (masalan MessagesController/AutoMessageService — bir nechta oluvchi bitta partiya ostida).
    /// Berilmasa (masalan Local Call sahifasidan ixtiyoriy raqamga ad-hoc yuborish) — bu metod O'ZI
    /// bittalik SmsBatch yaratadi, shu bilan har qanday yuborish umumiy SMS Tarixida ko'rinadi.
    /// </summary>
    public async Task<LocalSmsResult> SendSmsAsync(
        IAppDbContext db, string? agentId, string phone, string message,
        string recipientName = "", string? batchId = null, CancellationToken ct = default)
    {
        var ownsBatch = batchId is null;
        batchId ??= Guid.NewGuid().ToString();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var resolvedAgentId = !string.IsNullOrWhiteSpace(agentId) ? agentId : meta?.LocalSmsDefaultAgentId;
        if (string.IsNullOrWhiteSpace(resolvedAgentId))
            return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, false,
                "", "yetkazilmadi", "Standart Local SMS agent tanlanmagan (Sozlamalar → Xabar kanallari → SMS).", null, ct);

        var agent = await db.CtiAgents.FirstOrDefaultAsync(a => a.Id == resolvedAgentId, ct);
        if (agent is null)
            return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, false,
                "", "yetkazilmadi", "Local SMS agent topilmadi.", null, ct);

        var number = NormalizePhone(phone);
        if (number.Length == 0)
            return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, false,
                "", "yetkazilmadi", "Telefon raqami noto'g'ri.", null, ct);

        // Massaviy yuborishda ikkita SMS orasida kutish (sozlangan bo'lsa) — haqiqiy dispatch'dan
        // oldin, shunda noto'g'ri so'rovlar (yuqoridagi validatsiya) kutish vaqtini yemaydi.
        await PaceAsync(meta?.LocalSmsDelaySeconds ?? 0, ct);

        var commandId = Guid.NewGuid().ToString();
        var cmd = new CtiCommandLog
        {
            AgentId = resolvedAgentId, Action = "send_sms", Payload = $"{number}: {message}", Status = "pending",
        };
        db.CtiCommandLogs.Add(cmd);
        await db.SaveChangesAsync(ct);

        object SmsMsg() => new { action = "send_sms", to = number, text = message, commandId };

        // 1) WS ulangan — darhol yuboramiz.
        if (conn.IsConnected(resolvedAgentId) && await conn.SendAsync(resolvedAgentId, SmsMsg()))
        {
            cmd.Status = "sent";
            await db.SaveChangesAsync(ct);
            return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, true,
                commandId, "yuborildi", null, resolvedAgentId, ct);
        }

        // 2) Oflayn — FCM bilan uyg'otamiz, so'ng WS ulanishini poll qilamiz.
        //    ⚠️ Lekin agent UZOQ vaqtdan beri ko'rinmagan bo'lsa urinmaymiz: FCM tokeni
        //    deyarli aniq eskirgan, natija esa har safar 6 sekund bekor kutish bo'lardi.
        //    ⚠️ `LastSeenAt == null` (hech qachon ko'rinmagan) stale HISOBLANMAYDI: bu yangi
        //    o'rnatilgan agentning birinchi uyg'otilishi bo'lishi mumkin. Stale = "ko'rgan edik,
        //    lekin ANCHA oldin".
        var lastSeen = agent.LastSeenAt;
        if (lastSeen is not null && AppClock.Now - lastSeen.Value > StaleAgentAfter)
        {
            cmd.Status = "failed";
            await db.SaveChangesAsync(ct);
            // ⚠️ LOGGA yoziladi: ilgari bu holat butunlay jim edi va "SMS nega ketmayapti"
            // savoliga faqat SmsLogs jadvalidan javob topish mumkin edi.
            log.LogWarning(
                "Local SMS yuborilmadi: agent \"{Agent}\" {Days} kundan beri ko'rinmagan "
                + "(oxirgi: {LastSeen}). Telefondagi agent ilovasini qayta ulang.",
                agent.DisplayName,
                (int)(AppClock.Now - lastSeen.Value).TotalDays,
                lastSeen.Value.ToString("yyyy-MM-dd HH:mm"));
            return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, false,
                commandId, "yetkazilmadi",
                $"Agent \"{agent.DisplayName}\" uzoq vaqtdan beri oflayn — telefondagi ilovani qayta ulang.",
                resolvedAgentId, ct);
        }

        if (agent.FcmToken.Length > 0)
        {
            var json = AppSecrets.FcmServiceAccountJson;
            await fcm.SendDataAsync(json, agent.FcmToken, new Dictionary<string, string>
            {
                ["action"] = "send_sms",
                ["commandId"] = commandId,
            }, ct);

            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(500, ct);
                if (conn.IsConnected(resolvedAgentId) && await conn.SendAsync(resolvedAgentId, SmsMsg()))
                {
                    cmd.Status = "sent";
                    await db.SaveChangesAsync(ct);
                    return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, true,
                        commandId, "yuborildi", null, resolvedAgentId, ct);
                }
            }
        }

        cmd.Status = "failed";
        await db.SaveChangesAsync(ct);
        return await FinishAsync(db, phone, message, recipientName, batchId, ownsBatch, false,
            commandId, "yetkazilmadi", "Agent oflayn — yetkazilmadi.", resolvedAgentId, ct);
    }

    /// <summary>SmsLog (har doim) + agar <paramref name="ownsBatch"/> bo'lsa bittalik SmsBatch yozadi.</summary>
    private static async Task<LocalSmsResult> FinishAsync(
        IAppDbContext db, string phone, string message, string recipientName, string batchId,
        bool ownsBatch, bool ok, string commandId, string status, string? error, string? agentId,
        CancellationToken ct)
    {
        db.SmsLogs.Add(new SmsLog
        {
            BatchId = batchId, PhoneNumber = phone, RecipientName = recipientName, Message = message,
            RequestId = commandId, Status = status, Provider = "local", AgentId = agentId,
        });
        if (ownsBatch)
        {
            db.SmsBatches.Add(new SmsBatch
            {
                Id = batchId,
                Audience = recipientName.Length > 0 ? recipientName : phone,
                Message = message, SenderUserId = "", SenderName = "Local Call", CreatedAt = AppClock.Now,
                RecipientCount = 1, SentCount = ok ? 1 : 0, Provider = "local",
            });
        }
        await db.SaveChangesAsync(ct);
        return new LocalSmsResult(ok, commandId, status, error);
    }

    /// <summary>Terilayotgan/SMS oluvchi raqamni xalqaro formatga keltiradi (dial bilan bir xil qoida).</summary>
    private static string NormalizePhone(string raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return "";
        if (digits.StartsWith("998")) return "+" + digits;
        if (digits.Length == 10 && digits.StartsWith("0")) digits = digits[1..];
        if (digits.Length == 9) return "+998" + digits;
        return "+" + digits;
    }
}
