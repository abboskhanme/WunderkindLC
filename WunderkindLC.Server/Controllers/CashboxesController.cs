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
/// KASSALAR (Moliya → «Kassalar», `docs/SPEC-kassalar.md`). Qoida — <see cref="CashboxService"/>.
///
/// <para><b>Ruxsat:</b> tanlov ro'yxati (<c>GET options</c>) — to'lov oynasi uchun, admin panelidagi
/// har kimga (kassir ham, `kassa` ruxsati bilan). Qoldiqlar — `finance.main` (ReadRequiresPerm).
/// Yaratish/tahrirlash/arxivlash — <b>FAQAT superadmin</b>: HARD rol tekshiruvi, chunki
/// <see cref="AdminPermAttribute"/> admin'ni ham cheklovsiz o'tkazadi («Aktiv muzlatish» bilan bir xil
/// sabab, `year-freeze.md` §3). Yangi ruxsat kaliti YO'Q.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main")]
[Route("api/admin/cashboxes")]
public class CashboxesController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    /// <summary>Faqat superadmin — 403 SABAB bilan (jim rad etilmaydi).</summary>
    private ObjectResult? DenyUnlessSuperAdmin() =>
        User.IsInRole(Roles.SuperAdmin) ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Kassani faqat superadmin yaratadi va tahrirlaydi" });

    /// <summary>To'lov oynasidagi «Kassa» tanlovi: faol kassalar + shu foydalanuvchining standart kassasi.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<CashboxOptionsDto>> Options()
    {
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        var uid = UserId;
        return new CashboxOptionsDto(
            CashboxService.Active(boxes)
                .Select(b => new CashboxOptionDto(b.Id, b.Name, b.ResponsibleName, uid is not null && b.ResponsibleUserId == uid))
                .ToList(),
            CashboxService.PickDefault(boxes, uid)?.Id);
    }

    /// <summary>Kassalar kartalari: qoldiq (butun tarix) + davr ichidagi kirim/chiqim. Faollar avval.</summary>
    [HttpGet]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<CashboxDto>>> List([FromQuery] string? from, [FromQuery] string? to)
    {
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        var balances = await CashboxService.BalancesAsync(db, from, to);
        return CashboxService.Active(boxes)
            .Concat(boxes.Where(b => b.IsArchived).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            .Select(b => ToDto(b, balances.GetValueOrDefault(b.Id)))
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CashboxDto>> Create(CashboxPayload p)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(p.Name, boxes, null, out var name) is { } nameError)
            return BadRequest(new { message = nameError });
        var (respId, respName, respError) = await ResponsibleAsync(p.ResponsibleUserId);
        if (respError is not null) return BadRequest(new { message = respError });

        var box = new Cashbox
        {
            Name = name, ResponsibleUserId = respId, ResponsibleName = respName,
            CreatedAt = AppClock.Iso(), CreatedByName = ActorName,
        };
        db.Cashboxes.Add(box);
        audit.Record(AuditService.EntityCashbox, box.Id, "create",
            $"Kassa yaratildi: «{name}»" + (respName is null ? "" : $" — mas'ul: {respName}"));
        await db.SaveChangesAsync();
        return ToDto(box, null);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CashboxDto>> Update(string id, CashboxPayload p)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(p.Name, boxes, id, out var name) is { } nameError)
            return BadRequest(new { message = nameError });
        var (respId, respName, respError) = await ResponsibleAsync(p.ResponsibleUserId);
        if (respError is not null) return BadRequest(new { message = respError });

        var changes = new List<string>();
        if (box.Name != name) changes.Add($"nomi «{box.Name}» → «{name}»");
        if (box.ResponsibleUserId != respId) changes.Add($"mas'ul {box.ResponsibleName ?? "—"} → {respName ?? "—"}");
        box.Name = name;
        box.ResponsibleUserId = respId;
        box.ResponsibleName = respName;
        if (changes.Count > 0)
            audit.Record(AuditService.EntityCashbox, box.Id, "update", $"Kassa tahrirlandi: «{name}» — " + string.Join(", ", changes));
        await db.SaveChangesAsync();
        return ToDto(box, (await CashboxService.BalancesAsync(db, null, null)).GetValueOrDefault(box.Id));
    }

    /// <summary>Arxivlash: yangi pul qabul qilmaydi, qoldig'i va tarixi saqlanadi. «Asosiy kassa» — yo'q.</summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        if (box.Id == CashboxService.MainId)
            return BadRequest(new { message = "«Asosiy kassa» arxivlanmaydi — mas'uli yo'q to'lovlar shu kassaga tushadi" });
        if (box.IsArchived) return Ok(new { ok = true });

        var balance = (await CashboxService.BalancesAsync(db, null, null)).GetValueOrDefault(box.Id)?.Total ?? 0m;
        box.IsArchived = true;
        audit.Record(AuditService.EntityCashbox, box.Id, "update",
            $"Kassa arxivlandi: «{box.Name}»" + (balance != 0 ? $" (qoldig'i {AuditService.Money(balance)} so'm)" : ""));
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        if (DenyUnlessSuperAdmin() is { } deny) return deny;
        var box = await db.Cashboxes.FindAsync(id);
        if (box is null) return NotFound(new { message = "Kassa topilmadi" });
        if (!box.IsArchived) return Ok(new { ok = true });
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync();
        if (CashboxService.ValidateName(box.Name, boxes, box.Id, out _) is { } nameError)
            return BadRequest(new { message = nameError + " — avval birini qayta nomlang" });

        box.IsArchived = false;
        audit.Record(AuditService.EntityCashbox, box.Id, "update", $"Kassa arxivdan tiklandi: «{box.Name}»");
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Mas'ul — admin paneli akkaunti (xodim/admin/superadmin); bo'sh = mas'ulsiz.</summary>
    private async Task<(string? Id, string? Name, string? Error)> ResponsibleAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return (null, null, null);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Trim()
                        && (u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
            .Select(u => new { u.Id, u.FullName })
            .FirstOrDefaultAsync();
        return user is null ? (null, null, "Mas'ul xodim topilmadi") : (user.Id, user.FullName, null);
    }

    private static CashboxDto ToDto(Cashbox b, CashboxService.Balance? balance) =>
        new(b.Id, b.Name, b.ResponsibleUserId, b.ResponsibleName, b.Id == CashboxService.MainId, b.IsArchived,
            balance?.Total ?? 0m, balance?.PeriodIncome ?? 0m, balance?.PeriodExpense ?? 0m, b.CreatedAt,
            balance?.PeriodTransferIn ?? 0m, balance?.PeriodTransferOut ?? 0m);
}
