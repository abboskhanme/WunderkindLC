using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Filiallar (branches) — faqat superadmin (tizim egasi). Filial nomi, manzil, GPS joylashuv va
/// radius saqlanadi (kelajakda mobil ilovada xodim/o'quvchi geo-yo'qlamasi uchun).
/// </summary>
[ApiController]
[Authorize(Roles = "superadmin")]
[Route("api/admin/branches")]
public class BranchesController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Tarixdagi tur — "Sozlamalar" bo'limiga tushadi (<c>AuditSections</c>).</summary>
    private const string AuditBranch = "Branch";

    private static object Snapshot(Branch b) => new { b.Name, b.Address, b.Latitude, b.Longitude, b.RadiusMeters };

    /// <summary>Umumiy tekshiruv (yaratish va tahrir uchun bir xil). Ilgari tahrirda nom tekshirilmasdi —
    /// bo'sh nom 500 bilan yiqilardi.</summary>
    private static string? Validate(BranchPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return "Filial nomi kerak";
        if (p.RadiusMeters <= 0) return "Radius musbat bo'lishi kerak (metrda)";
        if (p.Latitude is < -90 or > 90 || p.Longitude is < -180 or > 180) return "Joylashuv koordinatalari noto'g'ri";
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BranchDto>>> GetAll() =>
        (await db.Branches.OrderBy(b => b.Name).ToListAsync()).Select(ToDto).ToList();

    [HttpPost]
    public async Task<ActionResult<BranchDto>> Create(BranchPayload p)
    {
        if (Validate(p) is { } error) return BadRequest(new { message = error });
        var branch = new Branch
        {
            Name = p.Name.Trim(),
            Address = (p.Address ?? "").Trim(),
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            RadiusMeters = p.RadiusMeters,
        };
        db.Branches.Add(branch);
        audit.Record(AuditBranch, branch.Id, "create", $"Filial qo'shildi: {branch.Name}", after: Snapshot(branch));
        await db.SaveChangesAsync();
        return ToDto(branch);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<BranchDto>> Update(string id, BranchPayload p)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();
        if (Validate(p) is { } error) return BadRequest(new { message = error });
        var before = Snapshot(branch);
        branch.Name = p.Name.Trim();
        branch.Address = (p.Address ?? "").Trim();
        branch.Latitude = p.Latitude;
        branch.Longitude = p.Longitude;
        branch.RadiusMeters = p.RadiusMeters;
        audit.Record(AuditBranch, branch.Id, "update", $"Filial tahrirlandi: {branch.Name}",
            before: before, after: Snapshot(branch));
        await db.SaveChangesAsync();
        return ToDto(branch);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();
        audit.Record(AuditBranch, branch.Id, "delete", $"Filial o'chirildi: {branch.Name}", before: Snapshot(branch));
        db.Branches.Remove(branch);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static BranchDto ToDto(Branch b) =>
        new(b.Id, b.Name, b.Address, b.Latitude, b.Longitude, b.RadiusMeters, b.CreatedAt.ToString("o"));
}
