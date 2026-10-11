namespace WunderkindLC.Domain;

// Read-only MCP server for AI clients (docs/SPEC-mcp.md, .claude/rules/mcp.md).
// Secrets are never stored in the clear: authorization codes and tokens are SHA-256 hashes.
// All timestamps are UTC (DateTime with Kind=Utc) — the RefreshToken pattern.

/// <summary>
/// An OAuth client registered through dynamic client registration (RFC 7591).
/// Public clients only — no client secret is issued; PKCE (S256) protects the code.
/// </summary>
public class McpClient
{
    /// <summary>Opaque public identifier, e.g. <c>mcp_…</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Exact redirect URIs the client registered (https, or http on a loopback host).</summary>
    public List<string> RedirectUris { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One "connection": a user authorised a client. Every token hangs off a grant, so
/// revoking the grant kills its access and refresh tokens at once.
/// </summary>
public class McpGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ClientId { get; set; } = string.Empty;

    /// <summary><see cref="AppUser.Id"/> — no FK on purpose: a deleted user simply stops authenticating.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    /// <summary>RFC 8707 resource the tokens are bound to (our <c>/mcp</c> URL).</summary>
    public string Resource { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>Who revoked (<see cref="AppUser.Id"/>), or <c>system:…</c> for replay detection.</summary>
    public string? RevokedBy { get; set; }
}

/// <summary>One-time authorization code. Only its SHA-256 hash is stored.</summary>
public class McpAuthCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CodeHash { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>PKCE S256 challenge (base64url of SHA-256 of the verifier).</summary>
    public string CodeChallenge { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    /// <summary>Grant created when the code was exchanged — revoked if the code is replayed.</summary>
    public Guid? GrantId { get; set; }
}

/// <summary>Access or refresh token. Only its SHA-256 hash is stored.</summary>
public class McpToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GrantId { get; set; }

    /// <summary><see cref="McpTokenKind"/>.</summary>
    public string Kind { get; set; } = McpTokenKind.Access;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Refresh token already exchanged (rotation). Reuse revokes the grant.</summary>
    public DateTime? ConsumedAt { get; set; }
}

public static class McpTokenKind
{
    public const string Access = "access";
    public const string Refresh = "refresh";
}

/// <summary>
/// Audit row: one MCP tool call. User and client names are snapshots, so a later rename
/// or deletion keeps the log readable.
/// </summary>
public class McpAuditEntry
{
    public long Id { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public Guid? GrantId { get; set; }

    public string Tool { get; set; } = string.Empty;

    /// <summary>Short, truncated argument summary (never secrets — tools take none).</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary><c>ok</c> | <c>denied</c> | <c>error</c>.</summary>
    public string Outcome { get; set; } = "ok";

    public int? RowCount { get; set; }

    public int DurationMs { get; set; }

    /// <summary>Client IP (after the trusted proxy), for incident review.</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent of the calling AI client (truncated).</summary>
    public string? UserAgent { get; set; }
}
