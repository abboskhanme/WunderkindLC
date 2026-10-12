using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// BLOCKED CHATS (SPEC P2.4). A chat that blocked the bot is marked on ALL its registrations (<c>BlockedAt</c>) —
/// it disappears from later audience counts («Bloklagan: N») instead of failing every send. As soon as the user
/// talks to the bot again (any message, button or <c>my_chat_member → member</c>) the mark is cleared.
/// </summary>
public static class TelegramBlocks
{
    /// <summary>Marks every registration of <paramref name="chatId"/> blocked. Does NOT save. Returns rows changed.</summary>
    public static async Task<int> MarkBlockedAsync(IAppDbContext db, long chatId, DateTime now, CancellationToken ct = default)
    {
        var regs = await db.TelegramRegistrations.Where(r => r.ChatId == chatId && r.BlockedAt == null).ToListAsync(ct);
        foreach (var r in regs) r.BlockedAt = now;
        return regs.Count;
    }

    /// <summary>Clears the mark (user wrote / pressed / re-joined). One <c>AnyAsync</c>; writes and saves only when
    /// needed. Returns true when something was unblocked.</summary>
    public static async Task<bool> UnblockAsync(IAppDbContext db, long chatId, CancellationToken ct = default)
    {
        if (!await db.TelegramRegistrations.AnyAsync(r => r.ChatId == chatId && r.BlockedAt != null, ct)) return false;
        var regs = await db.TelegramRegistrations.Where(r => r.ChatId == chatId && r.BlockedAt != null).ToListAsync(ct);
        foreach (var r in regs) r.BlockedAt = null;
        await db.SaveChangesAsync(ct);
        return regs.Count > 0;
    }

    /// <summary><c>my_chat_member</c> of a PRIVATE chat: <c>kicked</c> → blocked, <c>member</c> → unblocked
    /// (pure decision; <c>null</c> = nothing to do).</summary>
    public static bool? BlockedByStatus(string? newStatus) => newStatus switch
    {
        "kicked" => true,
        "member" => false,
        _ => null,
    };
}

/// <summary>Result of pressing «✅ Qabul qildim».</summary>
public enum AckOutcome { NotFound, WrongChat, Acknowledged, AlreadyAcknowledged }

/// <summary>
/// «✅ Qabul qildim» BUTTON (callback <c>mack:{deliveryId}</c>, SPEC P2.4). The first press by the chat the message
/// was sent to sets <c>AckAt</c> and removes the button; later presses only repeat the toast. "Ko'rdi / qabul
/// qildi" is ONE status — bots get no read receipts.
/// </summary>
public static class MessageAckService
{
    public const string CallbackPrefix = "mack:";
    public const string ButtonText = "✅ Qabul qildim";
    public const string ToastNotFound = "Xabar topilmadi yoki eskirgan";
    public const string ToastWrongChat = "Bu xabar sizga tegishli emas";
    public const string ToastOk = "✅ Qabul qilindi. Rahmat!";

    /// <summary>Inline keyboard with the button.</summary>
    public static object Keyboard(string deliveryId) => new
    {
        inline_keyboard = new object[][]
        {
            new object[] { new { text = ButtonText, callback_data = CallbackPrefix + deliveryId } },
        },
    };

    public static async Task<AckOutcome> HandleAsync(
        IAppDbContext db, ITelegramSender sender, long chatId, string callbackId, string deliveryId,
        CancellationToken ct = default)
    {
        var d = string.IsNullOrWhiteSpace(deliveryId) ? null
            : await db.MessageDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (d is null)
        {
            await Answer(ToastNotFound);
            return AckOutcome.NotFound;
        }
        if (d.ChatId != chatId)
        {
            await Answer(ToastWrongChat);
            return AckOutcome.WrongChat;
        }
        if (d.AckAt is not null)
        {
            await Answer(ToastOk);
            return AckOutcome.AlreadyAcknowledged;
        }

        d.AckAt = AppClock.Now;
        await db.SaveChangesAsync(ct);
        if (d.TelegramMessageId is long mid)
        {
            try { await sender.EditMessageReplyMarkupAsync(chatId, mid, null, ct); }
            catch (Exception) { /* the ack is stored; a button left on screen is harmless */ }
        }
        await Answer(ToastOk);
        return AckOutcome.Acknowledged;

        async Task Answer(string text)
        {
            if (callbackId.Length == 0) return;
            try { await sender.AnswerCallbackAsync(callbackId, text, ct); }
            catch (Exception) { /* toast only */ }
        }
    }
}
