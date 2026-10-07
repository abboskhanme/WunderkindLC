using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KASSADAN KASSAGA KO'CHIRISH (Moliya → «Kassalar»). Qoida — <see cref="CashboxTransferService"/>.
///
/// <para><b>Ruxsat:</b> tarix (<c>GET</c>) — `finance.main` (ReadRequiresPerm, kassa kartalari bilan bir
/// xil). Ko'chirish va bekor qilish — <b>FAQAT superadmin</b>, HARD rol tekshiruvi
/// (<see cref="AdminPermAttribute"/> admin'ni ham o'tkazadi — kassalardagi bilan bir xil sabab).
/// Kassir O'Z kassasidan ham ko'chira olmaydi: ko'chirish = "pul jismonan boshqa kassaga o'tdi" degan
/// da'vo; uni pulni QABUL QILGAN tomon (rahbar) yozadi, aks holda kassir kamomadni qog'ozda boshqa
/// kassaga "o'tkazib" yuborishi mumkin edi. Yangi ruxsat kaliti YO'Q.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main")]
[Route("api/admin/cashboxes/transfers")]
public class CashboxTransfersController(AppDbContext db, AuditService audit) : ControllerBase
{
    public const int MaxLimit = 1000;

    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    /// <summary>Faqat superadmin — 403 SABAB bilan (jim rad etilmaydi).</summary>
    private ObjectResult? DenyUnlessSuperAdmin() =>
        User.IsInRole(Roles.SuperAdmin) ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Kassalar orasida pulni faqat superadmin ko'chiradi" });

    /// <summary>
    /// Ko'chirishlar tarixi (eng yangisi tepada), BEKOR QILINGANLARI ham (ustiga chizilgan holda
    /// ko'rsatish uchun — hisobga baribir kirmaydi). <paramref name="cashboxId"/> — faqat shu kassa
    /// ishtirok etganlari.
    /// </summary>
    [HttpGet]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<CashboxTransferDto>>> List(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? cashboxId, [FromQuery] int limit = 200)
    {
        var q = db.CashboxTransfers.IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(from)) q = q.Where(t => string.Compare(t.Date, from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) q = q.Where(t => string.Compare(t.Date, to) <= 0);
        if (!string.IsNullOrWhiteSpace(cashboxId)) q = q.Where(t => t.FromCashboxId == cashboxId || t.ToCashboxId == cashboxId);
        var rows = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .ToListAsync();
        var names = await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);
        return rows.Select(t => ToDto(t, names)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CashboxTransferDto>> Create(CashboxTransferPayload p)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var res = await CashboxTransferService.CreateAsync(db, audit,
            new CashboxTransferService.Request(p.FromCashboxId, p.ToCashboxId, p.Amount, p.Date, p.Note, p.RequestId),
            UserId, ActorName);
        if (res.Error is not null) return BadRequest(new { message = res.Error });
        return ToDto(res.Transfer!, await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name));
    }

    /// <summary>
    /// Ko'chirishni BEKOR QILISH (marshrut <c>DELETE</c>, `FinanceController` bilan bir xil): qator
    /// o'chmaydi, ikkala kassa qoldig'i tiklanadi. Qabul qilgan kassada pul qolmagan bo'lsa — 400 sabab bilan.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Void(string id, [FromQuery] string? reason = null)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var res = await CashboxTransferService.VoidAsync(db, audit, id, reason, UserId, ActorName);
        if (res.NotFound) return NotFound(new { message = res.Error });
        if (res.Error is not null) return BadRequest(new { message = res.Error });
        return NoContent();
    }

    private static CashboxTransferDto ToDto(CashboxTransfer t, IReadOnlyDictionary<string, string> names) =>
        new(t.Id,
            t.FromCashboxId, names.GetValueOrDefault(t.FromCashboxId, "—"),
            t.ToCashboxId, names.GetValueOrDefault(t.ToCashboxId, "—"),
            t.Amount, t.Date, t.Note, t.CreatedAt, t.CreatedByName,
            t.IsVoided, t.VoidedAt, t.VoidedByName, t.VoidReason);
}
