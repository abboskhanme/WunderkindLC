using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «YO'QOTILGAN LIDLAR» — belgilash, tiklash va hisobot (`.claude/rules/crm-leads.md`
/// «YO'QOTILGAN LIDLAR»). Marshrut `LeadsController` bilan bir xil prefiksda
/// (<c>api/admin/leads</c>), lekin ALOHIDA faylda — qoida `LeadLoss`/`LeadLossReport` da,
/// bu yerda faqat HTTP.
///
/// <para><b>Ruxsat:</b> belgilash/tiklash — lidlar ro'yxati sahifasi (<c>leads.list</c>,
/// PATCH = "edit" amali: lidni tahrirlash bilan bir xil daraja). Hisobot — CRM statistikasi
/// sahifasi kaliti (<c>leads.stats</c>) va <c>ReadRequiresPerm</c>: GET odatda har qanday
/// xodimga ochiq, bu yerda esa sotuv bo'limining natijalari va menejerlar kesimi bor.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.list")]
[Route("api/admin/leads")]
public class LeadLossController(
    AppDbContext db, AuditService audit, TelegramService telegram,
    ILogger<LeadLossController> logger) : ControllerBase
{
    /// <summary>Lidni YO'QOTILGAN deb belgilash. Sabab MAJBURIY (kategoriya <c>lead_lost</c>).</summary>
    [HttpPatch("{id}/lost")]
    public async Task<ActionResult<LeadLossStateDto>> MarkLost(string id, MarkLeadLostRequest req)
    {
        var res = await LeadLoss.MarkLostAsync(db, audit, id, req.ReasonId, req.Comment, Actor());
        if (res.Status == 404) return NotFound(new { message = res.Error });
        if (res.Lead is null) return BadRequest(new { message = res.Error });
        // Kartada "❌ Yo'qotildi" qatori paydo bo'ladi (kartasi yo'q eski lidga hech narsa ketmaydi).
        await LeadNotifier.SyncCardAsync(db, telegram, res.Lead.Id, logger: logger);
        return await LeadLoss.StateAsync(db, res.Lead);
    }

    /// <summary>Yo'qotilgan lidni tiklash (ixtiyoriy — boshqa bosqichga).</summary>
    [HttpPatch("{id}/restore")]
    public async Task<ActionResult<LeadLossStateDto>> Restore(string id, RestoreLeadRequest? req)
    {
        var res = await LeadLoss.RestoreAsync(db, audit, id, req?.Stage, Actor());
        if (res.Status == 404) return NotFound(new { message = res.Error });
        if (res.Lead is null) return BadRequest(new { message = res.Error });
        await LeadNotifier.SyncCardAsync(db, telegram, res.Lead.Id, logger: logger);
        return await LeadLoss.StateAsync(db, res.Lead);
    }

    /// <summary>
    /// «Yo'qotilgan lidlar» hisoboti. <c>from</c>/<c>to</c> — "yyyy-MM-dd", lid KELGAN sana
    /// bo'yicha (ikkalasi ixtiyoriy, chegara kunlari kiradi).
    /// </summary>
    [HttpGet("lost-report")]
    [AdminPerm("leads.stats", ReadRequiresPerm = true)]
    public async Task<ActionResult<LeadLossReportDto>> Report(
        [FromQuery] string? from = null, [FromQuery] string? to = null) =>
        await LeadLossReport.BuildAsync(db, from, to);

    private LeadLoss.Actor Actor()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        var name = User.Identity?.Name
                   ?? User.FindFirst("name")?.Value
                   ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                   ?? "Admin";
        return new LeadLoss.Actor(string.IsNullOrWhiteSpace(id) ? null : id, name);
    }
}
