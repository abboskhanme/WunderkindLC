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
/// Who may connect: <see cref="Roles.SuperAdmin"/>, <see cref="Roles.Admin"/>, and a
/// <see cref="Roles.Staff"/> member whose permissions carry <see cref="PermissionKey"/>.
/// Nobody else — teachers, students and parents never get a token.
/// </para>
/// </summary>
public static class McpAccess
{
    /// <summary>Permission key (adminPermissions catalog) that lets a staff member connect an AI client.</summary>
    public const string PermissionKey = "aiAccess";

    /// <summary>The only OAuth scope the server issues.</summary>
    public const string Scope = "center.read";

    /// <summary>Is this account allowed to hold an MCP token right now?</summary>
    public static bool IsAllowed(AppUser user) =>
        user.Role is Roles.SuperAdmin or Roles.Admin
        || (user.Role == Roles.Staff && PermissionRules.HasSection(user.Permissions, PermissionKey));
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
}
