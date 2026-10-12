using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// What the bot's conversation HISTORY may keep (M4, 2026-10-12). Every bot reply is logged into
/// <see cref="BotSupportMessage.Text"/> and <see cref="BotUser.LastText"/> (admin panel «Support Telegram», the nightly
/// JSON backup that goes to a Telegram chat). The login message (login + the still unused initial password) and the
/// one-time login code are CREDENTIALS — they must never be stored in plain text: the history keeps a placeholder.
///
/// <para>Matching is CONSERVATIVE — only the exact templates the bot sends (current and historical, see
/// <c>TelegramBotService.BuildLoginText</c> / <c>HandleOtpRequestAsync</c>), by their fixed prefix, so a normal
/// support message is never touched. <see cref="LastText"/> is cut at 140 characters — the prefixes are short enough
/// to match the cut text as well.</para>
/// </summary>
public static class BotHistoryText
{
    /// <summary>Stored instead of the login/password message.</summary>
    public const string LoginPlaceholder = "[kirish ma'lumotlari yuborildi]";

    /// <summary>Stored instead of the one-time login code message.</summary>
    public const string OtpPlaceholder = "[bir martalik kirish kodi yuborildi]";

    /// <summary>Fixed prefixes of the login message (every version the bot has sent).</summary>
    private static readonly string[] LoginPrefixes =
    [
        "🔑 Kirish ma'lumotlari:\n",   // login + password (current and the pre-HTML version)
        "🔑 Ilovaga kirish:\n",        // the oldest «Login: …\nParol: …» version
        "🔑 Login: ",                  // the password was already used — login only
    ];

    /// <summary>The fixed middle part of the one-time code message («🔑 {label} uchun bir martalik kirish kodi:\n…»).</summary>
    private const string OtpMarker = " uchun bir martalik kirish kodi:";

    /// <summary>The text to keep in the history: the placeholder for a credential message, otherwise the text itself.</summary>
    public static string ForHistory(string? text)
    {
        var t = text ?? "";
        if (!t.StartsWith("🔑 ", StringComparison.Ordinal)) return t;
        if (t.Contains(OtpMarker, StringComparison.Ordinal)) return OtpPlaceholder;
        foreach (var p in LoginPrefixes)
            if (t.StartsWith(p, StringComparison.Ordinal)) return LoginPlaceholder;
        return t;
    }

    /// <summary>True when <paramref name="text"/> is a credential message (would be replaced).</summary>
    public static bool IsSecret(string? text) => !string.Equals(ForHistory(text), text ?? "", StringComparison.Ordinal);

    /// <summary>
    /// The 140-character preview kept in <see cref="BotUser.LastText"/> (the same cut the bot always used) —
    /// already scrubbed.
    /// </summary>
    public static string Preview(string? text)
    {
        var t = ForHistory(text);
        return t.Length > 140 ? t[..140] : t;
    }

    /// <summary>SeedMarker of the one-time scrub of rows written before this rule.</summary>
    public const string ScrubMarkerKey = "bot-history-credentials-scrub-2026-10";

    /// <summary>
    /// ONE-TIME startup scrub (SeedMarker <see cref="ScrubMarkerKey"/>, idempotent): replaces every stored bot reply
    /// and <c>LastText</c> preview that is a credential message (<see cref="ForHistory"/>) with its placeholder.
    /// Candidates are narrowed in SQL by the common «🔑 » prefix (works on PostgreSQL and SQLite), the exact rule runs in
    /// memory. Returns the number of changed rows (messages + previews), <c>null</c> when it already ran.
    /// </summary>
    public static async Task<int?> ScrubStoredAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == ScrubMarkerKey, ct)) return null;
        var changed = 0;

        var messages = await db.BotSupportMessages
            .Where(m => !m.FromUser && m.Text.StartsWith("🔑 "))
            .ToListAsync(ct);
        foreach (var m in messages)
        {
            var safe = ForHistory(m.Text);
            if (safe == m.Text) continue;
            m.Text = safe;
            changed++;
        }

        var users = await db.BotUsers.Where(u => u.LastText.StartsWith("🔑 ")).ToListAsync(ct);
        foreach (var u in users)
        {
            var safe = ForHistory(u.LastText);
            if (safe == u.LastText) continue;
            u.LastText = safe;
            changed++;
        }

        db.SeedMarkers.Add(new SeedMarker { Key = ScrubMarkerKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return changed;
    }
}
