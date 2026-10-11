using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Infrastructure.Mcp;

/// <summary>Access + refresh pair handed to the AI client (the secrets themselves — the database keeps hashes).</summary>
public sealed record McpIssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn, string Scope);

/// <summary>Outcome of a refresh-token exchange: tokens, or an OAuth error (and whether the grant was revoked).</summary>
public sealed record McpRefreshResult(McpIssuedTokens? Tokens, string? Error, string? Description, bool Revoked = false)
{
    public static McpRefreshResult Fail(string error, string description, bool revoked = false) =>
        new(null, error, description, revoked);
}

/// <summary>
/// Token issuing, rotation and grant revocation for the OAuth flow (docs/SPEC-mcp.md §4). Runs on the
/// READ/WRITE app context and writes only the <c>Mcp*</c> tables. Lives in Infrastructure (not Server) so
/// the rotation rules — atomic single use, the concurrent-refresh grace window, reuse detection — are
/// exercised by the test project against a real database.
/// </summary>
public static class McpTokens
{
    /// <summary>New access + refresh pair under <paramref name="grant"/>. Caller saves.</summary>
    public static McpIssuedTokens Issue(AppDbContext db, McpGrant grant)
    {
        var now = DateTime.UtcNow;
        var access = McpSecrets.New(McpSecrets.AccessPrefix);
        var refresh = McpSecrets.New(McpSecrets.RefreshPrefix);
        db.McpTokens.Add(new McpToken
        {
            GrantId = grant.Id, Kind = McpTokenKind.Access, TokenHash = McpSecrets.Hash(access),
            CreatedAt = now, ExpiresAt = now + McpLimits.AccessLifetime,
        });
        db.McpTokens.Add(new McpToken
        {
            GrantId = grant.Id, Kind = McpTokenKind.Refresh, TokenHash = McpSecrets.Hash(refresh),
            CreatedAt = now, ExpiresAt = now + McpLimits.RefreshLifetime,
        });
        return new McpIssuedTokens(access, refresh, (int)McpLimits.AccessLifetime.TotalSeconds, grant.Scope);
    }

    /// <summary>
    /// Revoke a grant (and so every token under it — each token lookup joins its grant) and save.
    /// Idempotent: an already revoked grant keeps its first revocation.
    /// </summary>
    public static async Task RevokeGrantAsync(AppDbContext db, Guid grantId, string by, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await db.McpGrants.Where(g => g.Id == grantId && g.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, now).SetProperty(g => g.RevokedBy, by), ct);
    }

    /// <summary>
    /// Is this second presentation of an already rotated refresh token a concurrent refresh by the same client
    /// (inside <see cref="McpLimits.RefreshReuseGrace"/>) rather than a stolen token being replayed? Pure.
    /// </summary>
    public static bool WithinReuseGrace(DateTime consumedAt, DateTime now) =>
        now - consumedAt <= McpLimits.RefreshReuseGrace;

    /// <summary>
    /// <c>grant_type=refresh_token</c>: validate, rotate atomically and issue a new pair.
    /// <list type="bullet">
    ///   <item>Rotation is a conditional UPDATE (<c>ConsumedAt IS NULL</c>): of two exchanges of the same token
    ///     exactly one succeeds — there are never two live successors.</item>
    ///   <item>A token presented again within <see cref="McpLimits.RefreshReuseGrace"/> of its rotation gets
    ///     <c>invalid_grant</c> WITHOUT revoking: a client that legitimately refreshed twice at the same moment
    ///     keeps its connection (the first request's new pair is valid).</item>
    ///   <item>Later reuse = the old token is in someone else's hands → the whole connection is revoked.</item>
    /// </list>
    /// </summary>
    public static async Task<McpRefreshResult> RotateAsync(
        AppDbContext db, string? refreshToken, string? clientId, CancellationToken ct = default)
    {
        var refresh = refreshToken ?? "";
        if (!refresh.StartsWith(McpSecrets.RefreshPrefix, StringComparison.Ordinal) || refresh.Length > 200)
            return McpRefreshResult.Fail("invalid_grant", "unknown refresh_token");
        var hash = McpSecrets.Hash(refresh);
        var tok = await db.McpTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Kind == McpTokenKind.Refresh, ct);
        if (tok is null) return McpRefreshResult.Fail("invalid_grant", "unknown refresh_token");
        var grant = await db.McpGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == tok.GrantId, ct);
        if (grant is null || grant.RevokedAt is not null) return McpRefreshResult.Fail("invalid_grant", "grant revoked");
        if (!string.Equals(grant.ClientId, clientId, StringComparison.Ordinal))
            return McpRefreshResult.Fail("invalid_grant", "client mismatch");

        if (tok.ConsumedAt is { } consumed)
            return await ReusedAsync(db, grant.Id, consumed, ct);
        var now = DateTime.UtcNow;
        if (tok.ExpiresAt <= now) return McpRefreshResult.Fail("invalid_grant", "refresh_token expired");
        if (grant.CreatedAt + McpLimits.GrantLifetime <= now)
            return McpRefreshResult.Fail("invalid_grant", "connection older than 90 days — sign in again");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == grant.UserId, ct);
        if (user is null || !McpAccess.IsAllowed(user)) return McpRefreshResult.Fail("invalid_grant", "user no longer allowed");

        // Atomic rotation: a racing second use of the same refresh token loses here.
        var tokId = tok.Id;
        var won = await db.McpTokens.Where(t => t.Id == tokId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct);
        if (won != 1)
        {
            // Lost the race: the winner set ConsumedAt a moment ago — normally inside the grace window.
            var winnerAt = await db.McpTokens.AsNoTracking().Where(t => t.Id == tokId)
                .Select(t => t.ConsumedAt).FirstOrDefaultAsync(ct);
            return await ReusedAsync(db, grant.Id, winnerAt ?? DateTime.MinValue, ct);
        }
        var issued = Issue(db, grant);
        await db.SaveChangesAsync(ct);
        return new McpRefreshResult(issued, null, null);
    }

    private static async Task<McpRefreshResult> ReusedAsync(AppDbContext db, Guid grantId, DateTime consumedAt, CancellationToken ct)
    {
        if (WithinReuseGrace(consumedAt, DateTime.UtcNow))
            return McpRefreshResult.Fail("invalid_grant", "refresh_token already used (concurrent refresh) — use the newest token");
        // Rotation reuse = the old refresh token is in someone else's hands.
        await RevokeGrantAsync(db, grantId, "system:refresh-reuse", ct);
        return McpRefreshResult.Fail("invalid_grant", "refresh_token already used", revoked: true);
    }
}
