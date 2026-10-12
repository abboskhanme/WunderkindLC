namespace WunderkindLC.Application.Services.Messaging;

/// <summary>Classified result of one Telegram send (SPEC P2.4).</summary>
public enum TgSendResult
{
    Ok,
    /// <summary>The user blocked the bot / account deactivated / chat gone — the chat is marked blocked.</summary>
    Blocked,
    /// <summary>429 — retry after <see cref="TgSendOutcome.RetryAfterSeconds"/>.</summary>
    RateLimited,
    /// <summary>400 «can't parse entities» — resent once as plain text.</summary>
    BadMarkup,
    /// <summary>Anything else (network, timeout, HTTP 200 without <c>ok:true</c>).</summary>
    Failed,
}

/// <summary>Media of a send: a cached Telegram <paramref name="FileId"/> or a local file streamed from disk
/// (<paramref name="OpenStream"/> — never a <c>byte[]</c> of a whole video on the 1 GB server).</summary>
/// <param name="Kind"><c>photo</c> · <c>video</c> · <c>document</c>.</param>
public sealed record TgMedia(string Kind, string? FileId, Func<Stream>? OpenStream, string FileName, string ContentType);

/// <summary>One Telegram send. <paramref name="HtmlText"/> is the text (or the caption when <paramref name="Media"/> is
/// set); <paramref name="ParseHtml"/> false sends it without <c>parse_mode</c> (BadMarkup fallback).</summary>
public sealed record TgSend(long ChatId, string HtmlText, TgMedia? Media = null, object? ReplyMarkup = null, bool ParseHtml = true);

/// <summary>Outcome: <paramref name="MessageId"/> of the sent message, <paramref name="MediaFileId"/> Telegram assigned
/// to an uploaded file, sanitised <paramref name="Description"/> (never the bot token).</summary>
public readonly record struct TgSendOutcome(
    TgSendResult Result, long? MessageId = null, string? MediaFileId = null, int RetryAfterSeconds = 0, string? Description = null);

/// <summary>
/// The outbox's view of the Telegram Bot API (<c>TelegramService</c> implements it; tests use a fake).
/// </summary>
public interface ITelegramSender
{
    bool IsConfigured { get; }
    Task<TgSendOutcome> SendAsync(TgSend msg, CancellationToken ct);
    /// <summary><c>null</c> markup removes the inline keyboard (the «Qabul qildim» button after it was pressed).</summary>
    Task<bool> EditMessageReplyMarkupAsync(long chatId, long messageId, object? replyMarkup, CancellationToken ct);
    Task AnswerCallbackAsync(string callbackId, string? text, CancellationToken ct);
}

/// <summary>Wakes the outbox worker right after rows were saved (instead of waiting for the next poll).</summary>
public interface IOutboxSignal
{
    void Wake();
}
