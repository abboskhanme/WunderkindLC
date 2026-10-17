using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <param name="Amount">Oylik summa (0 — oylik to'xtatildi).</param>
/// <param name="FromMonth">Shu oydan boshlab ("yyyy-MM"); bo'sh — joriy oy.</param>
public record StaffSalaryPayload(decimal Amount, string? FromMonth, string? Note);

/// <summary>
/// XODIMLAR MAOSHI REESTRI (SPEC-backlog-2026-10-17 F3 K3; hisob — <see cref="PayrollRegister"/>). O'qish va xodim oyligini
/// kiritish — `finance.main` (O'QISH ham: maosh summalari). To'lov — odatdagi maosh chiqimi
/// (<c>POST finance/transactions</c>, <c>staffUserId</c> / <c>salaryPayKind</c>; chegaradan katta bo'lsa — tasdiq).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", ReadRequiresPerm = true)]
[Route("api/admin/finance/payroll")]
public class PayrollController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<PayrollDto>> Get([FromQuery] string? month, CancellationToken ct)
    {
        var m = PaymentFields.IsMonth(month) ? month! : TuitionService.CurrentMonth();
        return await PayrollRegister.BuildAsync(db, m, ct);
    }

    /// <summary>Xodimning qat'iy oyligi — shu oydan boshlab (versiya; o'tgan oylar o'zgarmaydi). Bir oyga bitta versiya.</summary>
    [HttpPut("staff/{userId}/salary")]
    [AdminPerm("finance.main")]
    public async Task<IActionResult> SetStaffSalary(string userId, StaffSalaryPayload p, CancellationToken ct)
    {
        if (p.Amount < 0) return BadRequest(new { message = "Oylik manfiy bo'lmasin" });
        var from = string.IsNullOrWhiteSpace(p.FromMonth) ? TuitionService.CurrentMonth() : p.FromMonth.Trim();
        if (!PaymentFields.IsMonth(from)) return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && (u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
            .Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { message = "Xodim topilmadi" });

        var row = await db.StaffSalaries.FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == from, ct);
        var old = row?.Amount;
        if (row is null)
        {
            row = new StaffSalary { UserId = userId, EffectiveFrom = from };
            db.StaffSalaries.Add(row);
        }
        row.Amount = decimal.Round(p.Amount, 0);
        row.Note = string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim();
        row.CreatedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        row.CreatedAt = AppClock.Iso();
        audit.Record("Staff", userId, "update",
            $"Xodim oyligi: {user.FullName} — {from} dan {AuditService.Money(row.Amount)} so'm"
            + (old is null ? "" : $" (avval {AuditService.Money(old.Value)})"));
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
