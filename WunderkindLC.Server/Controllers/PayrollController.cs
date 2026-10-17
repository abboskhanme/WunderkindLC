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

public record PayrollPersonDto(string Id, string FullName, string Role, string Position);

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

    /// <summary>Oylik belgilash mumkin bo'lgan panel akkauntlari (xodim/admin/superadmin) — reestrda hali qatori yo'q
    /// admin/superadmin oyligini ham kiritish uchun (reestr ularni faqat oylik yoki to'lov bo'lsa ko'rsatadi).</summary>
    [HttpGet("people")]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<PayrollPersonDto>>> People(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin)
            .OrderBy(u => u.FullName)
            .Select(u => new PayrollPersonDto(u.Id, u.FullName, u.Role, u.Position))
            .ToListAsync(ct);

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
            .Select(u => new { u.Id, u.FullName, u.Role }).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { message = "Xodim topilmadi" });
        // 2026-10-17 (F3 revyu): superadmin bo'lmagan xodim O'Z oyligini, superadmin oyligini va O'TGAN oylarni
        // o'zgartira olmaydi — aks holda "hisoblangan" summani o'zi oshirib, katta to'lovni tasdiqqa olib chiqardi.
        if (!User.IsInRole(Roles.SuperAdmin))
        {
            var me = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (user.Id == me)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "O'zingizning oyligingizni superadmin belgilaydi" });
            if (user.Role == Roles.SuperAdmin)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Superadmin oyligini faqat superadmin belgilaydi" });
            if (string.CompareOrdinal(from, TuitionService.CurrentMonth()) < 0)
                return BadRequest(new { message = "O'tgan oylar uchun oylikni faqat superadmin o'zgartiradi" });
        }

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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Ikki marta bosilgan "Saqlash": (UserId, EffectiveFrom) unikal — ikkinchisi shu yerda, 500 emas.
            return Conflict(new { message = "Bu oy uchun oylik hozirgina saqlandi — sahifani yangilang" });
        }
        return Ok(new { ok = true });
    }
}
