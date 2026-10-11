using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Mcp;

/// <summary>
/// Sozlamalar → «AI ulanishlar» (superadmin only): connected AI clients per user, last use,
/// the tool-call audit log, and revocation. docs/SPEC-mcp.md §6.
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
        IReadOnlyList<string> RedirectHosts);

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

        return grants.Select(x =>
        {
            var u = users.FirstOrDefault(u => u.Id == x.g.UserId);
            var allowed = u is not null && McpAccess.IsAllowed(u);
            return new McpConnectionDto(
                x.g.Id, x.g.ClientId, x.ClientName, x.g.UserId, u?.FullName ?? "(o'chirilgan foydalanuvchi)", u?.Role,
                allowed, x.g.CreatedAt, x.g.LastUsedAt, x.g.RevokedAt, x.g.RevokedBy,
                Active: x.g.RevokedAt is null && allowed && liveRefresh.Contains(x.g.Id),
                Calls: calls.FirstOrDefault(c => c.Key == x.g.Id)?.Count ?? 0,
                // Where this client sends keys — the thing to check when a name looks familiar.
                RedirectHosts: [.. x.RedirectUris.Select(McpRedirectRules.DestinationLabel).Distinct()]);
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
        var grant = await db.McpGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grant is null) return NotFound(new { message = "Ulanish topilmadi" });
        if (grant.RevokedAt is null)
        {
            grant.RevokedAt = DateTime.UtcNow;
            grant.RevokedBy = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "superadmin";
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
        var by = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "superadmin";
        var now = DateTime.UtcNow;
        var grants = await db.McpGrants.Where(g => g.UserId == userId && g.RevokedAt == null).ToListAsync(ct);
        if (grants.Count == 0) return Ok(new { revoked = 0 });
        foreach (var g in grants)
        {
            g.RevokedAt = now;
            g.RevokedBy = by;
        }
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        audit.Record("McpGrant", userId, "delete",
            $"AI ulanishlar bekor qilindi ({grants.Count} ta) — {owner ?? "o'chirilgan foydalanuvchi"}");
        await db.SaveChangesAsync(ct);
        return Ok(new { revoked = grants.Count });
    }
}
