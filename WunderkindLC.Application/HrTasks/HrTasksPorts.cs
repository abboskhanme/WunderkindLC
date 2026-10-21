using WunderkindLC.Application.Services;

namespace WunderkindLC.Application.HrTasks;

// ===========================================================================
//  The two outside-world ports of HR topshiriqlari: Telegram and file storage.
//  Behind interfaces so tests replace them (tests NEVER touch the network).
// ===========================================================================

/// <summary>
/// Telegram delivery for HR messages — our single bot (<see cref="TelegramService"/>).
/// Never throws: a Telegram failure must not fail the request that caused it.
/// </summary>
public interface IHrTelegramSender
{
    /// <summary>false → nothing is sent at all (bot not configured).</summary>
    bool IsConfigured { get; }
    string? BotUsername { get; }

    /// <summary>
    /// <paramref name="openUrl"/> — an LC addition: the message carries one «📋 Topshiriqlarim» button that opens
    /// that page of the panel inside Telegram (Mini App, <see cref="TelegramBotService.PanelUrl"/>). null = no button.
    /// </summary>
    Task<bool> SendHtmlAsync(long chatId, string html, string? openUrl = null, CancellationToken ct = default);
    Task<bool> SendVoiceAsync(long chatId, byte[] bytes, string fileName, string contentType, string caption, CancellationToken ct = default);
}

/// <summary>The production sender: <see cref="TelegramService"/> with <c>parse_mode=HTML</c>.</summary>
public sealed class TelegramHrSender(TelegramService telegram) : IHrTelegramSender
{
    /// <summary>Button text of <see cref="IHrTelegramSender.SendHtmlAsync"/>'s <c>openUrl</c>.</summary>
    public const string OpenButtonText = "📋 Topshiriqlarim";

    public bool IsConfigured => telegram.IsConfigured;
    public string? BotUsername => string.IsNullOrWhiteSpace(telegram.BotUsername) ? null : telegram.BotUsername;

    public Task<bool> SendHtmlAsync(long chatId, string html, string? openUrl = null, CancellationToken ct = default) =>
        telegram.SendMessageAsync(chatId, html, Keyboard(openUrl), ct, "HTML");

    public Task<bool> SendVoiceAsync(long chatId, byte[] bytes, string fileName, string contentType, string caption, CancellationToken ct = default) =>
        telegram.SendVoiceAsync(chatId, bytes, fileName, contentType, caption, ct);

    /// <summary>One inline <c>web_app</c> button (a Mini App opens only from such a button — not a plain link).</summary>
    public static object? Keyboard(string? openUrl) => string.IsNullOrWhiteSpace(openUrl)
        ? null
        : new { inline_keyboard = new[] { new object[] { new { text = OpenButtonText, web_app = new { url = openUrl } } } } };
}

/// <summary>
/// Proof / attachment files under our <c>uploads/</c> (served at <c>/uploads/...</c> — login required,
/// <c>UploadsGuard</c>). Names are unguessable GUIDs (<see cref="UploadGuard.SafeName"/> style); the user's file
/// name is metadata only.
/// </summary>
public interface IHrFileStore
{
    /// <summary>Saves and returns the path relative to <c>uploads/</c>: <c>hr/{folder}/yyyy/MM/&lt;guid&gt;.ext</c>.</summary>
    Task<string> SaveAsync(string folder, byte[] content, string extension, DateTimeOffset now, CancellationToken ct = default);
    string UrlFor(string relativePath);
    Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default);
    bool Delete(string relativePath);
}

public sealed class LocalHrFileStore(IWebHostEnvironment env) : IHrFileStore
{
    private string Root => Path.Combine(env.ContentRootPath, "uploads");

    public async Task<string> SaveAsync(string folder, byte[] content, string extension, DateTimeOffset now, CancellationToken ct = default)
    {
        var ext = SafeExtension(extension);
        var relative = $"hr/{folder}/{now.UtcDateTime:yyyy}/{now.UtcDateTime:MM}/{Guid.NewGuid():N}{ext}";
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content, ct);
        return relative;
    }

    public string UrlFor(string relativePath) => "/uploads/" + relativePath.TrimStart('/');

    public async Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        if (!IsOurs(relativePath)) return null;
        var full = Full(relativePath);
        return File.Exists(full) ? await File.ReadAllBytesAsync(full, ct) : null;
    }

    public bool Delete(string relativePath)
    {
        if (!IsOurs(relativePath)) return false;
        var full = Full(relativePath);
        if (!File.Exists(full)) return false;
        File.Delete(full);
        return true;
    }

    private string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Only paths this store generated — never a traversal out of <c>uploads/hr</c>.</summary>
    private static bool IsOurs(string relative) =>
        relative.StartsWith("hr/", StringComparison.Ordinal) && !relative.Contains("..", StringComparison.Ordinal);

    /// <summary>Lower-case <c>.ext</c> of letters/digits only; anything else becomes <c>.bin</c>.</summary>
    public static string SafeExtension(string? extension)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return ext.Length is >= 2 and <= 10 && ext[1..].All(char.IsAsciiLetterOrDigit) ? ext : ".bin";
    }
}
