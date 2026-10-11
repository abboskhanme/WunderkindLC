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
/// «Mening AI ulanishlarim» — a panel user manages their OWN AI connections (Akkaunt sozlamalari): list them
/// (app, created, last used, expires, calls) and revoke one or all. Every query is filtered by the caller's
/// NameIdentifier: a grant id of someone else answers 404 (never revealed, never touched). Revocation is the
/// SAME path the superadmin page uses (<see cref="McpConnections.RevokeAsync"/>): the token handler and every
/// refresh check the grant, so access and refresh tokens die at once. Each revoke is audited
/// (<c>McpGrant</c> → Sozlamalar). docs/SPEC-mcp.md §6.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin + "," + Roles.Staff)]
[Route("api/mcp/my")]
public sealed class McpMyConnectionsController(AppDbContext db, McpFeature feature, AuditService audit) : ControllerBase
{
    /// <param name="Allowed">May this account connect an AI app right now (<see cref="McpAccess.IsAllowed"/>).</param>
    /// <param name="Reason">Why not (Uzbek), when <paramref name="Allowed"/> is false.</param>
    /// <param name="Enabled">Is the MCP server switched on in this deployment.</param>
    /// <param name="Endpoint">The URL to paste into the AI app (only when allowed and enabled).</param>
    public sealed record MyMcpDto(
        bool Allowed, string? Reason, bool Enabled, string? Endpoint, IReadOnlyList<McpOwnConnectionDto> Connections);

    private string Me => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    [HttpGet]
    public async Task<ActionResult<MyMcpDto>> Get(CancellationToken ct)
    {
        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == Me, ct);
        if (me is null) return Unauthorized();
        var allowed = McpAccess.IsAllowed(me);
        var connections = await McpConnections.ListOwnAsync(db, me.Id, ct);
        return new MyMcpDto(allowed, allowed ? null : McpAccess.DeniedReason(me), feature.Enabled,
            allowed && feature.Enabled ? McpUrls.Resource(Request) : null, connections);
    }

    [HttpPost("connections/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Me) || !await McpConnections.ExistsAsync(db, id, Me, ct))
            return NotFound(new { message = "Ulanish topilmadi" });
        var revoked = await McpConnections.RevokeAsync(db, Me, id, Me, ct);
        if (revoked.Count > 0)
        {
            var grant = revoked[0];
            var client = await db.McpClients.AsNoTracking().Where(c => c.Id == grant.ClientId)
                .Select(c => c.Name).FirstOrDefaultAsync(ct);
            audit.Record("McpGrant", grant.Id.ToString(), "delete",
                $"AI ulanish egasi tomonidan bekor qilindi: «{client ?? grant.ClientId}» — {User.FindFirst(ClaimTypes.Name)?.Value}");
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Me)) return Unauthorized();
        var revoked = await McpConnections.RevokeAsync(db, Me, null, Me, ct);
        if (revoked.Count > 0)
        {
            audit.Record("McpGrant", Me, "delete",
                $"AI ulanishlar egasi tomonidan bekor qilindi ({revoked.Count} ta) — {User.FindFirst(ClaimTypes.Name)?.Value}");
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { revoked = revoked.Count });
    }
}
