using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Arxiv — o'chirilgan Lid/O'quvchi/O'qituvchi/Xodim/Guruh/Moliya yozuvlarining JSON
/// suratlari (<see cref="ArchivedRecord"/>). Ro'yxat, tiklash (asl entity'ni qaytarib
/// qo'shadi) va butunlay o'chirish. "Sozlamalar" ruxsati talab qilinadi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings.archive")]
[Route("api/admin/archive")]
public class ArchiveController(AppDbContext db) : ControllerBase
{
    /// <summary>Arxiv ro'yxati (ixtiyoriy <paramref name="type"/> bo'yicha), DeletedAt kamayuvchi.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ArchivedRecordDto>>> List([FromQuery] string? type = null)
    {
        var q = db.ArchivedRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(r => r.Type == type);
        return await q
            .OrderByDescending(r => r.DeletedAt)
            .Select(r => new ArchivedRecordDto(
                r.Id, r.Type, r.EntityId, r.Title, r.Subtitle, r.Reason, r.DeletedAt, r.ActorName))
            .ToListAsync();
    }

    /// <summary>Tur bo'yicha arxiv yozuvlari soni (lead/student/teacher/staff/group/finance).</summary>
    [HttpGet("counts")]
    public async Task<ActionResult<Dictionary<string, int>>> Counts()
    {
        var counts = await db.ArchivedRecords
            .GroupBy(r => r.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        var result = new Dictionary<string, int>
        {
            ["lead"] = 0, ["student"] = 0, ["teacher"] = 0,
            ["staff"] = 0, ["group"] = 0, ["finance"] = 0,
        };
        foreach (var c in counts) result[c.Type] = c.Count;
        return result;
    }

    /// <summary>Arxiv yozuvini TIKLASH — asl entity'ni qayta qo'shadi va arxiv yozuvini o'chiradi.</summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var rec = await db.ArchivedRecords.FindAsync(id);
        if (rec is null) return NotFound();

        const string existsMessage = "Bu yozuv allaqachon mavjud — tiklab bo'lmaydi";
        try
        {
            var json = rec.Json;
            switch (rec.Type)
            {
                case "lead":
                {
                    var lead = JsonSerializer.Deserialize<Lead>(json)!;
                    if (await db.Leads.AnyAsync(x => x.Id == lead.Id))
                        return BadRequest(new { message = existsMessage });
                    db.Leads.Add(lead);
                    break;
                }
                case "student":
                {
                    var student = JsonSerializer.Deserialize<Student>(json)!;
                    if (await db.Students.AnyAsync(x => x.Id == student.Id))
                        return BadRequest(new { message = existsMessage });
                    db.Students.Add(student);
                    break;
                }
                case "teacher":
                {
                    var teacher = JsonSerializer.Deserialize<Teacher>(json)!;
                    if (await db.Teachers.AnyAsync(x => x.Id == teacher.Id))
                        return BadRequest(new { message = existsMessage });
                    db.Teachers.Add(teacher);
                    break;
                }
                case "staff":
                {
                    var user = JsonSerializer.Deserialize<AppUser>(json)!;
                    if (await db.Users.AnyAsync(x => x.Id == user.Id))
                        return BadRequest(new { message = existsMessage });
                    // Login (Email) band bo'lsa — tiklab bo'lmaydi (boshqa user shu login bilan turibdi).
                    if (await db.Users.AnyAsync(u => u.Email == user.Email && u.Id != user.Id))
                        return BadRequest(new { message = "Bu login band — tiklab bo'lmaydi" });
                    // Xavfsizlik: eski ruxsatlarni jimgina tiklamaymiz — admin qayta beradi.
                    user.Permissions = new();
                    db.Users.Add(user);
                    break;
                }
                case "group":
                {
                    var group = JsonSerializer.Deserialize<Group>(json)!;
                    if (await db.Classes.AnyAsync(x => x.Id == group.Id))
                        return BadRequest(new { message = existsMessage });
                    db.Classes.Add(group);
                    break;
                }
                case "finance":
                {
                    var tx = JsonSerializer.Deserialize<FinanceTransaction>(json)!;
                    if (await db.FinanceTransactions.AnyAsync(x => x.Id == tx.Id))
                        return BadRequest(new { message = existsMessage });
                    db.FinanceTransactions.Add(tx);
                    // O'chirishda balans QAYTARILGAN edi (FinanceController.Delete) — tiklashda QAYTA qo'llaymiz.
                    if (tx.Direction == "income" && tx.Category == "tuition" && !string.IsNullOrEmpty(tx.StudentId))
                    {
                        var student = await db.Students.FindAsync(tx.StudentId);
                        if (student is not null) student.Balance += tx.Amount;
                    }
                    break;
                }
                default:
                    return BadRequest(new { message = $"Noma'lum arxiv turi: {rec.Type}" });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Tiklab bo'lmadi: " + ex.Message });
        }

        db.ArchivedRecords.Remove(rec);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Arxiv yozuvini BUTUNLAY o'chirish (faqat surat o'chadi, boshqa narsa emas).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Purge(string id)
    {
        var rec = await db.ArchivedRecords.FindAsync(id);
        if (rec is null) return NotFound();
        db.ArchivedRecords.Remove(rec);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
