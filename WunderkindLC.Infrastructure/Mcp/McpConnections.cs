using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Infrastructure.Mcp;

/// <summary>One connection as its owner sees it (Sozlamalar → «Mening AI ulanishlarim»).</summary>
public sealed record McpOwnConnectionDto(
    Guid Id, string ClientName, IReadOnlyList<string> RedirectHosts, DateTime CreatedAt, DateTime? LastUsedAt,
    DateTime? ExpiresAt, DateTime? RevokedAt, bool Active, int Calls);

/// <summary>Calls and rows of one connection over the volume window.</summary>
public sealed record McpUsage(int Calls, long Rows)
{
    public static readonly McpUsage None = new(0, 0);

    /// <summary>Above either volume threshold (<see cref="McpLimits.VolumeCallsPerDay"/> / <see cref="McpLimits.VolumeRowsPerDay"/>).</summary>
    public bool Exceeds => McpConnections.ExceedsVolume(Calls, Rows);
}

/// <summary>A connection the volume watch just flagged (claimed for today's alert).</summary>
public sealed record McpVolumeAlert(Guid GrantId, string UserId, string UserName, string ClientName, int Calls, long Rows);

/// <summary>
/// Connection (grant) management shared by the superadmin page (<c>McpAdminController</c>) and the user's own
/// block (<c>McpMyConnectionsController</c>): the SAME revocation path for both, the per-admin enablement switch
/// and the volume watch. Writes only <c>McpGrants</c> and the <c>Users.AiAccessEnabled</c> flag; the caller adds
/// the audit row and saves (except the volume claim, which is an atomic UPDATE of its own).
/// </summary>
public static class McpConnections
{
    /// <summary>
    /// Revoke connections: every live one of <paramref name="userId"/>, or just <paramref name="grantId"/> —
    /// and then only if it belongs to <paramref name="userId"/> when an owner is given (a user can never touch
    /// someone else's connection). Tokens die with the grant: every token lookup and every refresh checks
    /// <see cref="McpGrant.RevokedAt"/>. Tracked changes — the caller audits and saves.
    /// </summary>
    /// <param name="userId">Owner filter; <c>null</c> only for the superadmin's single revoke.</param>
    /// <returns>The grants revoked by this call (already revoked ones are not repeated).</returns>
    public static async Task<List<McpGrant>> RevokeAsync(
        AppDbContext db, string? userId, Guid? grantId, string by, CancellationToken ct = default)
    {
        if (userId is null && grantId is null) throw new ArgumentException("owner or grant required");
        var q = db.McpGrants.Where(g => g.RevokedAt == null);
        if (userId is not null) q = q.Where(g => g.UserId == userId);
        if (grantId is { } id) q = q.Where(g => g.Id == id);
        var grants = await q.ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var g in grants)
        {
            g.RevokedAt = now;
            g.RevokedBy = by;
        }
        return grants;
    }

    /// <summary>Does this connection exist and (when <paramref name="userId"/> is given) belong to that user?</summary>
    public static Task<bool> ExistsAsync(AppDbContext db, Guid grantId, string? userId, CancellationToken ct = default) =>
        db.McpGrants.AnyAsync(g => g.Id == grantId && (userId == null || g.UserId == userId), ct);

    /// <summary>The caller's own connections (newest activity first), never anyone else's.</summary>
    public static async Task<List<McpOwnConnectionDto>> ListOwnAsync(AppDbContext db, string userId, CancellationToken ct = default)
    {
        var rows = await (from g in db.McpGrants.AsNoTracking()
                          join c in db.McpClients.AsNoTracking() on g.ClientId equals c.Id
                          where g.UserId == userId
                          orderby g.RevokedAt != null, g.LastUsedAt descending, g.CreatedAt descending
                          select new { g, ClientName = c.Name, c.RedirectUris }).Take(100).ToListAsync(ct);
        var ids = rows.Select(r => r.g.Id).ToList();
        var nullableIds = ids.Select(id => (Guid?)id).ToList();
        var calls = await db.McpAudit.AsNoTracking().Where(a => nullableIds.Contains(a.GrantId))
            .GroupBy(a => a.GrantId).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var refresh = await db.McpTokens.AsNoTracking()
            .Where(t => ids.Contains(t.GrantId) && t.Kind == McpTokenKind.Refresh && t.ConsumedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.GrantId).Select(x => new { x.Key, Until = x.Max(t => t.ExpiresAt) }).ToListAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var allowed = user is not null && McpAccess.IsAllowed(user);

        return rows.Select(r =>
        {
            var lifetimeEnd = r.g.CreatedAt + McpLimits.GrantLifetime;
            var until = refresh.FirstOrDefault(x => x.Key == r.g.Id)?.Until;
            DateTime? expires = until is { } u ? (u < lifetimeEnd ? u : lifetimeEnd) : null;
            var active = r.g.RevokedAt is null && allowed && expires is { } e && e > now;
            return new McpOwnConnectionDto(
                r.g.Id, r.ClientName, [.. r.RedirectUris.Select(McpRedirectRules.DestinationLabel).Distinct()],
                r.g.CreatedAt, r.g.LastUsedAt, r.g.RevokedAt is null ? expires : null, r.g.RevokedAt, active,
                calls.FirstOrDefault(c => c.Key == r.g.Id)?.Count ?? 0);
        }).ToList();
    }

    // =====================================================================
    //  Per-admin enablement (I7)
    // =====================================================================

    /// <summary>Result of switching one admin's AI access.</summary>
    public sealed record AccessChange(AppUser User, bool Changed, List<McpGrant> Revoked);

    /// <summary>
    /// Turn AI access on or off for one <c>admin</c> account. Turning it OFF revokes that user's connections at
    /// once (the token handler would refuse them on the next call anyway — revoking also kills refresh and makes
    /// the list honest). Turning it ON from off also revokes leftovers (connections made before the switch existed,
    /// dormant while access was off): enabling never brings an old connection back to life — the admin connects
    /// afresh, and superadmins get the new-connection alert. <c>null</c> when the account does not exist or is not
    /// an admin. Caller audits and saves.
    /// </summary>
    public static async Task<AccessChange?> SetAdminAccessAsync(
        AppDbContext db, string userId, bool enabled, string by, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Role != Roles.Admin) return null;
        var changed = user.AiAccessEnabled != enabled;
        user.AiAccessEnabled = enabled;
        var revoked = !enabled || changed ? await RevokeAsync(db, userId, null, by, ct) : [];
        return new AccessChange(user, changed, revoked);
    }

    // =====================================================================
    //  Volume watch (I7)
    // =====================================================================

    /// <summary>Above either daily volume threshold? Pure.</summary>
    public static bool ExceedsVolume(int calls, long rows) =>
        calls > McpLimits.VolumeCallsPerDay || rows > McpLimits.VolumeRowsPerDay;

    /// <summary>May this connection be alerted again (at most once per <see cref="McpLimits.VolumeWindow"/>)? Pure.</summary>
    public static bool AlertDue(DateTime? lastAlertAt, DateTime now) =>
        lastAlertAt is null || now - lastAlertAt.Value >= McpLimits.VolumeWindow;

    /// <summary>Calls and rows per connection since <paramref name="since"/> (only connections with activity).</summary>
    public static async Task<Dictionary<Guid, McpUsage>> UsageSinceAsync(
        AppDbContext db, DateTime since, IReadOnlyCollection<Guid>? grantIds = null, CancellationToken ct = default)
    {
        var q = db.McpAudit.AsNoTracking().Where(a => a.At >= since && a.GrantId != null);
        if (grantIds is not null)
        {
            var ids = grantIds.Select(id => (Guid?)id).ToList();
            q = q.Where(a => ids.Contains(a.GrantId));
        }
        var rows = await q.GroupBy(a => a.GrantId)
            .Select(x => new { x.Key, Calls = x.Count(), Rows = x.Sum(a => (long)(a.RowCount ?? 0)) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key!.Value, r => new McpUsage(r.Calls, r.Rows));
    }

    /// <summary>
    /// Connections whose last-24-h volume exceeds a threshold and that were not alerted in the last 24 h. Each is
    /// CLAIMED with a conditional UPDATE of <see cref="McpGrant.VolumeAlertAt"/> before it is returned, so two
    /// runs (or two instances) never alert the same connection twice in a day — and a restart does not re-alert.
    /// </summary>
    public static async Task<List<McpVolumeAlert>> ClaimVolumeAlertsAsync(
        AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var usage = await UsageSinceAsync(db, now - McpLimits.VolumeWindow, null, ct);
        var over = usage.Where(u => u.Value.Exceeds).Select(u => u.Key).ToList();
        if (over.Count == 0) return [];

        var grants = await (from g in db.McpGrants.AsNoTracking()
                            join c in db.McpClients.AsNoTracking() on g.ClientId equals c.Id
                            where over.Contains(g.Id)
                            select new { g.Id, g.UserId, g.VolumeAlertAt, ClientName = c.Name }).ToListAsync(ct);
        var userIds = grants.Select(g => g.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var claimed = new List<McpVolumeAlert>();
        foreach (var g in grants)
        {
            if (!AlertDue(g.VolumeAlertAt, now)) continue;
            var id = g.Id;
            var previous = g.VolumeAlertAt;
            var won = await db.McpGrants.Where(x => x.Id == id && x.VolumeAlertAt == previous)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.VolumeAlertAt, now), ct);
            if (won != 1) continue;
            var u = usage[id];
            claimed.Add(new McpVolumeAlert(id, g.UserId, names.GetValueOrDefault(g.UserId) ?? "(o'chirilgan foydalanuvchi)",
                g.ClientName, u.Calls, u.Rows));
        }
        return claimed;
    }
}
