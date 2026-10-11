using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Infrastructure.Mcp;

namespace WunderkindLC.Server.Mcp;

/// <summary>
/// Sozlamalar → «AI ulanishlar» (superadmin only): connected AI clients per user, last use, the 24-hour
/// volume flag, the tool-call audit log, revocation, and the per-admin AI access switch. docs/SPEC-mcp.md §6.
/// A user's OWN connections are managed in <see cref="McpMyConnectionsController"/> (same revocation path).
/// </summary>
[ApiController]
[Authorize(Roles = Roles.SuperAdmin)]
[Route("api/admin/mcp")]
public sealed class McpAdminController(AppDbContext db, McpFeature feature, AuditService audit) : ControllerBase
{
    public sealed record McpStatusDto(bool Enabled, string? DisabledReason, string Endpoint, int ToolCount);

    public sealed record McpConnectionDto(
        Guid Id, string ClientId, string ClientName, string UserId, string UserName, string? UserRole,
        bool UserAllowed, DateTime CreatedAt, DateTime? LastUsedAt,
        DateTime? RevokedAt, string? RevokedBy, bool Active, int Calls,
        IReadOnlyList<string> RedirectHosts,
        int Calls24h, long Rows24h, bool VolumeAlert, DateTime? VolumeAlertAt);

    /// <summary>One admin account and its AI access switch (superadmins and staff are not listed: the first
    /// always may connect, the second get the <c>aiAccess</c> permission through their role).</summary>
    public sealed record McpAdminAccessDto(string Id, string FullName, string Phone, bool Enabled, int ActiveConnections);

    public sealed record SetAccessRequest(bool Enabled);

    public sealed record McpAuditRowDto(
        long Id, DateTime At, string UserId, string UserName, string ClientName, string Tool,
        string Arguments, string Outcome, int? RowCount, int DurationMs);

    public sealed record McpAuditPageDto(int Total, int Page, int PageSize, IReadOnlyList<McpAuditRowDto> Rows);

    [HttpGet("status")]
    public McpStatusDto Status() =>
        new(feature.Enabled, feature.Enabled ? null : feature.DisabledReason, McpUrls.Resource(Request),
            McpToolRegistry.ToolTypes.Length);

    [HttpGet("connections")]
    public async Task<IReadOnlyList<McpConnectionDto>> Connections(CancellationToken ct)
    {
        var grants = await (from g in db.McpGrants.AsNoTracking()
                            join c in db.McpClients.AsNoTracking() on g.ClientId equals c.Id
                            orderby g.RevokedAt != null, g.LastUsedAt descending, g.CreatedAt descending
                            select new { g, ClientName = c.Name, c.RedirectUris }).Take(500).ToListAsync(ct);
        var userIds = grants.Select(x => x.g.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var grantIds = grants.Select(x => (Guid?)x.g.Id).ToList();
        var calls = await db.McpAudit.AsNoTracking().Where(a => grantIds.Contains(a.GrantId))
            .GroupBy(a => a.GrantId).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var plainIds = grants.Select(x => x.g.Id).ToList();
        var liveRefresh = await db.McpTokens.AsNoTracking()
            .Where(t => plainIds.Contains(t.GrantId) && t.Kind == McpTokenKind.Refresh
                        && t.ConsumedAt == null && t.ExpiresAt > now)
            .Select(t => t.GrantId).Distinct().ToListAsync(ct);
        var usage = await McpConnections.UsageSinceAsync(db, now - McpLimits.VolumeWindow, plainIds, ct);

        return grants.Select(x =>
        {
            var u = users.FirstOrDefault(u => u.Id == x.g.UserId);
            var allowed = u is not null && McpAccess.IsAllowed(u);
            var day = usage.GetValueOrDefault(x.g.Id) ?? McpUsage.None;
            return new McpConnectionDto(
                x.g.Id, x.g.ClientId, x.ClientName, x.g.UserId, u?.FullName ?? "(o'chirilgan foydalanuvchi)", u?.Role,
                allowed, x.g.CreatedAt, x.g.LastUsedAt, x.g.RevokedAt, x.g.RevokedBy,
                Active: x.g.RevokedAt is null && allowed && liveRefresh.Contains(x.g.Id),
                Calls: calls.FirstOrDefault(c => c.Key == x.g.Id)?.Count ?? 0,
                // Where this client sends keys — the thing to check when a name looks familiar.
                RedirectHosts: [.. x.RedirectUris.Select(McpRedirectRules.DestinationLabel).Distinct()],
                Calls24h: day.Calls, Rows24h: day.Rows,
                // Visible flag: above a threshold right now, or alerted within the last day.
                VolumeAlert: day.Exceeds || !McpConnections.AlertDue(x.g.VolumeAlertAt, now),
                VolumeAlertAt: x.g.VolumeAlertAt);
        }).ToList();
    }

    [HttpGet("audit")]
    public async Task<McpAuditPageDto> Audit(
        [FromQuery] string? userId, [FromQuery] Guid? grantId, [FromQuery] string? outcome,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var q = db.McpAudit.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(a => a.UserId == userId);
        if (grantId is { } g) q = q.Where(a => a.GrantId == g);
        if (!string.IsNullOrWhiteSpace(outcome)) q = q.Where(a => a.Outcome == outcome);
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.Id).Skip((p - 1) * size).Take(size)
            .Select(a => new McpAuditRowDto(a.Id, a.At, a.UserId, a.UserName, a.ClientName, a.Tool,
                a.Arguments, a.Outcome, a.RowCount, a.DurationMs))
            .ToListAsync(ct);
        return new McpAuditPageDto(total, p, size, rows);
    }

    [HttpPost("connections/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        if (!await McpConnections.ExistsAsync(db, id, null, ct)) return NotFound(new { message = "Ulanish topilmadi" });
        var revoked = await McpConnections.RevokeAsync(db, null, id, Me, ct);
        if (revoked.Count > 0)
        {
            var grant = revoked[0];
            var client = await db.McpClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == grant.ClientId, ct);
            var owner = await db.Users.AsNoTracking().Where(u => u.Id == grant.UserId)
                .Select(u => u.FullName).FirstOrDefaultAsync(ct);
            audit.Record("McpGrant", grant.Id.ToString(), "delete",
                $"AI ulanish bekor qilindi: «{client?.Name ?? grant.ClientId}» — {owner ?? "o'chirilgan foydalanuvchi"}");
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>Revoke every active connection of one user (e.g. a laptop was lost).</summary>
    [HttpPost("users/{userId}/revoke-all")]
    public async Task<IActionResult> RevokeAll(string userId, CancellationToken ct)
    {
        var grants = await McpConnections.RevokeAsync(db, userId, null, Me, ct);
        if (grants.Count == 0) return Ok(new { revoked = 0 });
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        audit.Record("McpGrant", userId, "delete",
            $"AI ulanishlar bekor qilindi ({grants.Count} ta) — {owner ?? "o'chirilgan foydalanuvchi"}");
        await db.SaveChangesAsync(ct);
        return Ok(new { revoked = grants.Count });
    }

    // =====================================================================
    //  Per-admin AI access (an admin connects ONLY after a superadmin enabled it)
    // =====================================================================

    /// <summary>Every admin account with its switch and live connection count.</summary>
    [HttpGet("admins")]
    public async Task<IReadOnlyList<McpAdminAccessDto>> Admins(CancellationToken ct)
    {
        var admins = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Admin)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Phone, u.AiAccessEnabled }).ToListAsync(ct);
        var ids = admins.Select(a => a.Id).ToList();
        var bornAfter = DateTime.UtcNow - McpLimits.GrantLifetime;
        var live = await db.McpGrants.AsNoTracking()
            .Where(g => ids.Contains(g.UserId) && g.RevokedAt == null && g.CreatedAt > bornAfter)
            .GroupBy(g => g.UserId).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
        return admins.Select(a => new McpAdminAccessDto(a.Id, a.FullName, a.Phone, a.AiAccessEnabled,
            a.AiAccessEnabled ? live.FirstOrDefault(l => l.Key == a.Id)?.Count ?? 0 : 0)).ToList();
    }

    /// <summary>
    /// Turn AI access on/off for one admin. OFF revokes that admin's connections immediately. Audited
    /// (<c>McpAccess</c> → Sozlamalar). Superadmins and staff are refused (400): the first always may connect,
    /// the second through the <c>aiAccess</c> role permission.
    /// </summary>
    [HttpPut("admins/{userId}/access")]
    public async Task<IActionResult> SetAdminAccess(string userId, SetAccessRequest req, CancellationToken ct)
    {
        var change = await McpConnections.SetAdminAccessAsync(db, userId, req.Enabled, Me, ct);
        if (change is null)
            return BadRequest(new
            {
                message = "Bu yerda faqat ADMINISTRATOR akkauntlari yoqiladi/o'chiriladi. Superadmin har doim ulana oladi, "
                          + "xodimga esa Boshqaruv → Rollar → «AI ulanish (MCP, faqat o'qish)» ruxsati beriladi.",
            });
        if (change.Changed || change.Revoked.Count > 0)
        {
            audit.Record("McpAccess", userId, "update",
                (req.Enabled
                    ? $"AI ulanish ruxsati yoqildi (administrator): {change.User.FullName}"
                    : $"AI ulanish ruxsati o'chirildi (administrator): {change.User.FullName}")
                + (change.Revoked.Count > 0 ? $" — {change.Revoked.Count} ta ulanish bekor qilindi" : ""));
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { enabled = req.Enabled, revoked = change.Revoked.Count });
    }

    private string Me => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "superadmin";
}
