using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Lid manbalari ma'lumotnomasi ("O'quv bo'limi → Sabablar" sahifasida boshqariladi).
/// Lid formasi va Lidlar filtri shu ro'yxatdan tanlaydi. O'qish barcha xodimlarga ochiq
/// (AdminPerm GET'ni bloklamaydi), yozish — "settings" ruxsati bilan.
/// Manba lidga NOMI bilan yoziladi (<see cref="Lead.Source"/>), shuning uchun nomni o'zgartirish
/// eski lidlarni ko'chirmaydi — bu holda eski lidlarning manbasi ham yangilanadi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings.reasons")]
[Route("api/admin/lead-sources")]
public class LeadSourcesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadSourceDto>>> GetAll() =>
        await db.LeadSources.AsNoTracking()
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new LeadSourceDto(s.Id, s.Name, s.Order))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<LeadSourceDto>> Create(LeadSourceInput p)
    {
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Manba nomini kiriting" });
        if (await db.LeadSources.AnyAsync(s => s.Name == name))
            return BadRequest(new { message = "Bunday manba allaqachon mavjud" });

        var order = (await db.LeadSources.MaxAsync(s => (int?)s.Order) ?? -1) + 1;
        var src = new LeadSource { Name = name, Order = order };
        db.LeadSources.Add(src);
        await db.SaveChangesAsync();
        return new LeadSourceDto(src.Id, src.Name, src.Order);
    }

    /// <summary>Nomni o'zgartirish — shu manbaga ega LIDLAR ham yangi nomga ko'chiriladi.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, LeadSourceInput p)
    {
        var src = await db.LeadSources.FindAsync(id);
        if (src is null) return NotFound();
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Manba nomini kiriting" });
        if (await db.LeadSources.AnyAsync(s => s.Name == name && s.Id != id))
            return BadRequest(new { message = "Bunday manba allaqachon mavjud" });

        var old = src.Name;
        src.Name = name;
        if (old != name)
        {
            var leads = await db.Leads.Where(l => l.Source == old).ToListAsync();
            foreach (var l in leads) l.Source = name;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Manbani ro'yxatdan o'chiradi. Eski lidlardagi matn saqlanadi (tarix buzilmasin).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var src = await db.LeadSources.FindAsync(id);
        if (src is null) return NotFound();
        db.LeadSources.Remove(src);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
