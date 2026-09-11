using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Tuman + maktab boshqaruvi (Sozlamalar). Admin tuman yaratadi, har tuman ichida maktablarni
/// qo'shib chiqadi. O'quvchi ma'lumotini kiritishda admin tuman → maktabni tanlaydi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings.districts")]
[Route("api/admin")]
public class DistrictsController(AppDbContext db) : ControllerBase
{
    /// <summary>Barcha tumanlar — har biri ichida maktablari bilan (tartiblangan).</summary>
    [HttpGet("districts")]
    public async Task<ActionResult<IEnumerable<DistrictDto>>> GetAll()
    {
        var districts = await db.Districts.OrderBy(d => d.Order).ThenBy(d => d.Name).ToListAsync();
        var schools = await db.Schools.OrderBy(s => s.Order).ThenBy(s => s.Name).ToListAsync();
        var byDistrict = schools.GroupBy(s => s.DistrictId)
            .ToDictionary(g => g.Key, g => g.ToList());
        return districts.Select(d => new DistrictDto(
            d.Id, d.Name, d.Order,
            (byDistrict.GetValueOrDefault(d.Id) ?? new List<School>())
                .Select(s => new SchoolDto(s.Id, s.DistrictId, s.Name, s.Order)).ToList()
        )).ToList();
    }

    [HttpPost("districts")]
    public async Task<ActionResult<DistrictDto>> CreateDistrict(DistrictCreate p)
    {
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Tuman nomini kiriting" });
        var order = (await db.Districts.MaxAsync(d => (int?)d.Order) ?? -1) + 1;
        var district = new District { Name = name, Order = order };
        db.Districts.Add(district);
        await db.SaveChangesAsync();
        return new DistrictDto(district.Id, district.Name, district.Order, new List<SchoolDto>());
    }

    [HttpPut("districts/{id}")]
    public async Task<IActionResult> UpdateDistrict(string id, DistrictUpdate p)
    {
        var district = await db.Districts.FindAsync(id);
        if (district is null) return NotFound();
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Tuman nomini kiriting" });
        district.Name = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("districts/{id}")]
    public async Task<IActionResult> DeleteDistrict(string id)
    {
        var district = await db.Districts.FindAsync(id);
        if (district is null) return NotFound();
        // Tuman o'chirilsa ichidagi maktablar ham o'chiriladi.
        await db.Schools.Where(s => s.DistrictId == id).ExecuteDeleteAsync();
        db.Districts.Remove(district);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("districts/{districtId}/schools")]
    public async Task<ActionResult<SchoolDto>> CreateSchool(string districtId, SchoolCreate p)
    {
        var district = await db.Districts.FindAsync(districtId);
        if (district is null) return NotFound(new { message = "Tuman topilmadi" });
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Maktab raqami/nomini kiriting" });
        var order = (await db.Schools.Where(s => s.DistrictId == districtId).MaxAsync(s => (int?)s.Order) ?? -1) + 1;
        var school = new School { DistrictId = districtId, Name = name, Order = order };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        return new SchoolDto(school.Id, school.DistrictId, school.Name, school.Order);
    }

    [HttpPut("schools/{id}")]
    public async Task<IActionResult> UpdateSchool(string id, SchoolUpdate p)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null) return NotFound();
        var name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Maktab raqami/nomini kiriting" });
        school.Name = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("schools/{id}")]
    public async Task<IActionResult> DeleteSchool(string id)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null) return NotFound();
        db.Schools.Remove(school);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
