using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Admin "Ilova → Support" bo'limi: support o'qituvchilar ro'yxati + har birining bo'sh vaqt
/// slotlari, bronlari va o'tilgan darslari (qaysi o'quvchiga, qachon, qaysi mavzu).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app.support")]
[Route("api/admin/support")]
public class SupportController(AppDbContext db) : ControllerBase
{
    /// <summary>Barcha support o'qituvchilar + slot statistikasi (bo'sh / bron / o'tilgan).</summary>
    [HttpGet("teachers")]
    public async Task<ActionResult<IEnumerable<SupportTeacherDto>>> Teachers()
    {
        var supports = await db.Teachers.Where(t => t.IsSupport && !t.IsArchived)
            .OrderBy(t => t.FullName).ToListAsync();
        var ids = supports.Select(t => t.Id).ToList();
        var slots = await db.SupportSlots.Where(s => ids.Contains(s.TeacherId)).ToListAsync();
        var byTeacher = slots.GroupBy(s => s.TeacherId).ToDictionary(g => g.Key, g => g.ToList());

        return supports.Select(t =>
        {
            var ts = byTeacher.GetValueOrDefault(t.Id) ?? new();
            return new SupportTeacherDto(t.Id, t.FullName, t.Phone, t.PhotoUrl,
                ts.Count(s => s.Status == "open"),
                ts.Count(s => s.Status == "booked"),
                ts.Count(s => s.Status == "done"));
        }).ToList();
    }

    /// <summary>Bitta support tafsiloti — barcha slot/darslari (eng yangi birinchi).</summary>
    [HttpGet("teachers/{id}")]
    public async Task<ActionResult<SupportTeacherDetailDto>> Teacher(string id)
    {
        var t = await db.Teachers.FindAsync(id);
        if (t is null || !t.IsSupport) return NotFound();

        var slots = await db.SupportSlots.Where(s => s.TeacherId == id)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.StartTime).ToListAsync();
        var names = await SupportService.StudentNamesAsync(db, slots.Select(s => s.StudentId));

        var dtos = slots.Select(s => new SupportSlotDto(
            s.Id, s.TeacherId, s.Date, s.StartTime, s.EndTime, s.Status,
            s.StudentId, s.StudentId != null ? names.GetValueOrDefault(s.StudentId, "") : "",
            s.Topic, s.Notes, s.BookedAt)).ToList();

        return new SupportTeacherDetailDto(t.Id, t.FullName, t.Phone, t.PhotoUrl, dtos);
    }
}
