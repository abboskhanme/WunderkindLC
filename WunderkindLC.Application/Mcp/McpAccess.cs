using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp;

/// <summary>
/// Read-only MCP server for AI clients (docs/SPEC-mcp.md).
///
/// <para>
/// Who may connect: a <see cref="Roles.SuperAdmin"/> always; an <see cref="Roles.Admin"/> only when a
/// superadmin explicitly enabled AI access for that account (<see cref="AppUser.AiAccessEnabled"/>);
/// a <see cref="Roles.Staff"/> member whose permissions carry <see cref="PermissionKey"/>.
/// Nobody else — teachers, students and parents never get a token.
/// </para>
/// </summary>
public static class McpAccess
{
    /// <summary>Permission key (adminPermissions catalog) that lets a staff member connect an AI client.</summary>
    public const string PermissionKey = "aiAccess";

    /// <summary>The only OAuth scope the server issues.</summary>
    public const string Scope = "center.read";

    /// <summary>
    /// Is this account allowed to hold an MCP token right now? Checked at login, consent, code exchange,
    /// every refresh and every <c>/mcp</c> request — so revoking any of the three conditions stops the
    /// connection on its next call.
    /// </summary>
    public static bool IsAllowed(AppUser user) => user.Role switch
    {
        Roles.SuperAdmin => true,
        Roles.Admin => user.AiAccessEnabled,
        Roles.Staff => PermissionRules.HasSection(user.Permissions, PermissionKey),
        _ => false,
    };

    /// <summary>Uzbek reason shown when a panel account may not connect (login page, own-connections block).</summary>
    public static string DeniedReason(AppUser user) => user.Role switch
    {
        Roles.Admin => "Administrator akkauntiga AI ulanishni superadmin alohida yoqadi "
                       + "(Sozlamalar → Umumiy → «AI ulanishlar» → «Administratorlar»).",
        Roles.Staff => "Xodimga AI ulanish rol orqali beriladi: Boshqaruv → Rollar → «AI ulanish (MCP, faqat o'qish)».",
        _ => "Bu imkoniyat faqat rahbariyat uchun.",
    };
}

/// <summary>Opaque secrets: generation, hashing, PKCE.</summary>
public static class McpSecrets
{
    public const string AccessPrefix = "wlc_at_";
    public const string RefreshPrefix = "wlc_rt_";
    public const string CodePrefix = "wlc_ac_";

    public static string New(string prefix) =>
        prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lower-case hex SHA-256 — what the database stores instead of the secret.</summary>
    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>RFC 7636 §4.1: 43–128 chars of [A-Z a-z 0-9 - . _ ~].</summary>
    public static bool IsValidVerifier(string? v) =>
        v is { Length: >= 43 and <= 128 }
        && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>S256 challenge check in constant time.</summary>
    public static bool VerifyPkce(string verifier, string challenge)
    {
        var computed = Encoding.ASCII.GetBytes(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        var expected = Encoding.ASCII.GetBytes(challenge);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>A challenge is base64url(SHA-256) = exactly 43 chars.</summary>
    public static bool IsValidChallenge(string? c) =>
        c is { Length: 43 } && c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}

public static class McpLimits
{
    public const int MaxRedirectUris = 5;
    public const int MaxUriLength = 512;
    public const int MaxClientName = 60;
    public const int MaxState = 1000;

    /// <summary>Global dynamic-registration cap per 24 hours.</summary>
    public const int MaxRegistrationsPerDay = 50;

    /// <summary>Absolute lifetime of a connection — refresh is refused after this.</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromDays(90);

    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Failed OAuth logins per login name before a cool-down.</summary>
    public const int MaxFailuresPerLogin = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    /// <summary>Concurrent /mcp calls: per connection and process-wide.</summary>
    public const int ConcurrentPerGrant = 2;
    public const int ConcurrentGlobal = 6;

    /// <summary>Per-IP request budget on /mcp and /oauth before authentication.</summary>
    public const int PreAuthPerMinute = 300;

    /// <summary>Tool calls per connection per minute.</summary>
    public const int CallsPerMinute = 120;

    /// <summary>Overall time budget of ONE tool call (all its SQL statements together).</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Audit rows older than this are pruned.</summary>
    public const int AuditRetentionDays = 365;

    /// <summary>
    /// A refresh token presented again within this window after it was rotated is treated as a concurrent
    /// refresh by the same client (two requests at the same moment): <c>invalid_grant</c>, connection kept.
    /// Later reuse means the old token is in someone else's hands — the connection is revoked.
    /// </summary>
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    /// <summary>Longest free-text string a tool result carries (longer values end with <see cref="McpText.TruncationMarker"/>).</summary>
    public const int MaxTextLength = 2000;

    /// <summary>Volume watch: tool calls of ONE connection in the last 24 h that raise an alert.</summary>
    public const int VolumeCallsPerDay = 1_000;

    /// <summary>Volume watch: rows returned to ONE connection in the last 24 h that raise an alert.</summary>
    public const int VolumeRowsPerDay = 50_000;

    /// <summary>Window the volume watch sums over, and the minimum gap between two alerts of one connection.</summary>
    public static readonly TimeSpan VolumeWindow = TimeSpan.FromHours(24);

    /// <summary>How often the background volume watch runs.</summary>
    public static readonly TimeSpan VolumeCheckInterval = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Where an OAuth client may send the user back to: EXACT callback URLs of the supported AI apps
/// (OAuth security best practice — the code's safety must not depend on every path of a vendor's
/// domain), plus http on a loopback host (desktop apps; any port and path).
/// </summary>
public sealed class McpRedirectRules
{
    /// <summary>The documented MCP-connector callbacks of Claude and ChatGPT. Override with
    /// <c>Mcp:AllowedRedirectUris</c> / <c>MCP_ALLOWED_REDIRECT_URIS</c> (comma-separated, exact).</summary>
    public static readonly string[] DefaultUris =
    [
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
        "https://chatgpt.com/connector_platform_oauth_redirect",
    ];

    public IReadOnlySet<string> AllowedUris { get; }

    public McpRedirectRules(IEnumerable<string?>? uris = null)
    {
        var set = (uris ?? [])
            .Select(u => (u ?? "").Trim())
            .Where(u => u.Length > 0 && Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == Uri.UriSchemeHttps
                        && string.Equals(u, x.AbsoluteUri, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        AllowedUris = set.Count > 0 ? set : new HashSet<string>(DefaultUris, StringComparer.Ordinal);
    }

    public static bool IsLoopback(Uri uri) => uri.IdnHost is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    /// <summary>
    /// A redirect URI we will ever send a code (or an error) to:
    /// <list type="bullet">
    ///   <item>exact canonical form (raw string == <see cref="Uri.AbsoluteUri"/>) — no case/encoding tricks;</item>
    ///   <item>https: EXACTLY one of <see cref="AllowedUris"/>; http: only on a loopback host;</item>
    ///   <item>no fragment, no user-info, at most <see cref="McpLimits.MaxUriLength"/> characters.</item>
    /// </list>
    /// Custom schemes (cursor://, vscode://) are refused.
    /// </summary>
    public bool IsAllowed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > McpLimits.MaxUriLength) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(value, uri.AbsoluteUri, StringComparison.Ordinal)) return false;
        if (uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) return false;
        if (uri.Scheme == Uri.UriSchemeHttp) return IsLoopback(uri);
        return uri.Scheme == Uri.UriSchemeHttps && AllowedUris.Contains(value);
    }

    /// <summary>
    /// Exact match against a registered URI; for loopback redirects the port may differ
    /// (RFC 8252 §7.3). The result must ALSO pass today's allow-list — a client registered
    /// before a URI was removed gets nothing.
    /// </summary>
    public string? Match(IReadOnlyList<string> registered, string? requested)
    {
        string? match = null;
        if (string.IsNullOrEmpty(requested)) match = registered.Count == 1 ? registered[0] : null;
        else
            foreach (var reg in registered)
            {
                if (string.Equals(reg, requested, StringComparison.Ordinal)) { match = requested; break; }
                if (Uri.TryCreate(reg, UriKind.Absolute, out var r) && Uri.TryCreate(requested, UriKind.Absolute, out var q)
                    && r.Scheme == Uri.UriSchemeHttp && q.Scheme == Uri.UriSchemeHttp && IsLoopback(r)
                    && r.IdnHost == q.IdnHost && r.AbsolutePath == q.AbsolutePath && r.Query == q.Query
                    && q.Fragment.Length == 0)
                { match = requested; break; }
            }
        return match is not null && IsAllowed(match) ? match : null;
    }

    /// <summary>What the consent screen tells the user about where the key goes.</summary>
    public static string DestinationLabel(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var u)
            ? IsLoopback(u) ? "kompyuteringizdagi dastur" : u.IdnHost
            : "?";
}

/// <summary>Display-safe text from untrusted clients.</summary>
public static class McpText
{
    /// <summary>
    /// Strip control/format characters (bidi overrides, zero-width joiners, newlines — the
    /// raw material of name spoofing on the consent screen), collapse spaces, cap length.
    /// </summary>
    public static string Sanitize(string? value, int max)
    {
        var sb = new StringBuilder();
        foreach (var ch in (value ?? "").Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned) continue;
            sb.Append(ch);
        }
        var s = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Appended to a tool-result string cut at <see cref="McpLimits.MaxTextLength"/>.</summary>
    public const string TruncationMarker = "… [matn qisqartirildi]";

    /// <summary>
    /// Every string value of a tool result passes through here (<c>McpToolContext.Serialize</c>). Free text
    /// written by outside parties (lead notes, parent feedback, job applications, contact responses…) reaches
    /// the AI verbatim, so the invisible raw material of prompt injection and display spoofing is removed:
    /// <list type="bullet">
    ///   <item>control characters except <c>\n</c> and <c>\t</c> (<c>\r</c> dropped, U+2028/U+2029 → <c>\n</c>);</item>
    ///   <item>format characters — bidi overrides/isolates, zero-width space/joiners, BOM, Unicode TAG characters
    ///     (U+E0000–E007F, "ASCII smuggling");</item>
    ///   <item>variation selectors supplement (U+E0100–E01EF, payload smuggling), private-use, unassigned code
    ///     points and lone surrogates.</item>
    /// </list>
    /// Letters, digits, punctuation (the Uzbek apostrophes ʻ ’ ' included) and emoji stay as they are. The result
    /// is capped at <paramref name="max"/> characters plus <see cref="TruncationMarker"/> — no field is unbounded.
    /// Pure.
    /// </summary>
    public static string CleanOutput(string? value, int max = McpLimits.MaxTextLength)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        if (IsPlain(value) && value.Length <= max) return value;   // fast path: the vast majority of values

        var sb = new StringBuilder(Math.Min(value.Length, max + 32));
        var span = value.AsSpan();
        var i = 0;
        while (i < span.Length && sb.Length < max)
        {
            if (Rune.DecodeFromUtf16(span[i..], out var rune, out var used) != System.Buffers.OperationStatus.Done)
            {
                i += Math.Max(used, 1);   // lone surrogate / invalid sequence
                continue;
            }
            i += used;
            var v = rune.Value;
            if (v is '\n' or '\t') { sb.Append((char)v); continue; }
            if (v is 0x2028 or 0x2029) { sb.Append('\n'); continue; }
            if (v is >= 0xE0100 and <= 0xE01EF) continue;
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned) continue;
            if (sb.Length + rune.Utf16SequenceLength > max) { i -= used; break; }
            sb.Append(rune.ToString());
        }
        return i < span.Length ? sb.Append(TruncationMarker).ToString() : sb.ToString();
    }

    /// <summary>Printable BMP text with no format/control characters (other than \n and \t) — nothing to clean.</summary>
    private static bool IsPlain(string value)
    {
        foreach (var ch in value)
        {
            if (ch is '\n' or '\t') continue;
            if (ch < 0x20 || char.IsSurrogate(ch)) return false;
            if (ch < 0x7F) continue;
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned) return false;
        }
        return true;
    }
}
