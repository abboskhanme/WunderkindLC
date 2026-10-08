using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Lidlar kanbani USTUNLARI (bosqichlar).
///
/// <para>⚠️ Har o'zgarish AUDITGA yoziladi (tur <c>LeadStage</c> → "Lidlar" bo'limi): bosqich nomi va
/// tartibi voronka analitikasi, CRM statistikasi va Meta CAPI ("sifatli lid" bosqich nomidan
/// aniqlanadi) natijasini o'zgartiradi — kim qachon o'zgartirgani ko'rinmasa hisobot nega
/// boshqacha chiqqanini topib bo'lmasdi (ADM-6).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.list")]
[Route("api/admin/lead-stages")]
public class LeadStagesController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Audit turi — <see cref="AuditSections"/> da "Lidlar" bo'limi.</summary>
    public const string AuditEntity = "LeadStage";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadStage>>> GetAll() =>
        await db.LeadStages.OrderBy(s => s.Order).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<LeadStage>> Create(StagePayload p)
    {
        var maxOrder = await db.LeadStages.AnyAsync() ? await db.LeadStages.MaxAsync(s => s.Order) : -1;
        var stage = new LeadStage { Title = p.Title, Color = p.Color, Order = maxOrder + 1 };
        db.LeadStages.Add(stage);
        audit.Record(AuditEntity, stage.Id, "create", $"Lidlar ustuni qo'shildi: «{stage.Title}»",
            after: Snapshot(stage));
        await db.SaveChangesAsync();
        return stage;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LeadStage>> Update(string id, StagePayload p)
    {
        var stage = await db.LeadStages.FindAsync(id);
        if (stage is null) return NotFound();
        var before = Snapshot(stage);
        var oldTitle = stage.Title;
        stage.Title = p.Title;
        stage.Color = p.Color;
        audit.Record(AuditEntity, stage.Id, "update",
            oldTitle == stage.Title
                ? $"Lidlar ustuni tahrirlandi: «{stage.Title}»"
                : $"Lidlar ustuni qayta nomlandi: «{oldTitle}» → «{stage.Title}»",
            before: before, after: Snapshot(stage));
        await db.SaveChangesAsync();
        return stage;
    }

    /// <summary>
    /// Ustunni o'chiradi. ⚠️ Ichida (yo'qotilmagan) lid bo'lsa — 400 soni bilan: klient ham shunday
    /// tekshiradi, lekin server ham himoya qiladi (API orqali to'g'ridan-to'g'ri chaqirilsa lidlar
    /// "yetim" bo'lib, keyingi restartgacha kanbanda noto'g'ri ustunda turardi). Yo'qotilgan lidlar
    /// SANALMAYDI — ular faqat «Yo'qotilgan» ustunida ko'rinadi (`crm-leads.md` §6).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var stage = await db.LeadStages.FindAsync(id);
        if (stage is null) return NotFound();
        var inStage = await db.Leads.CountAsync(l => l.Stage == id && l.LostAt == null);
        if (inStage > 0)
            return BadRequest(new { message = $"Bu ustunda {inStage} ta lid bor — avval ularni boshqa ustunga ko'chiring." });
        db.LeadStages.Remove(stage);
        audit.Record(AuditEntity, stage.Id, "delete", $"Lidlar ustuni o'chirildi: «{stage.Title}»",
            before: Snapshot(stage));
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ustunlar tartibini saqlash (kelgan id'lar tartibida).</summary>
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(ReorderRequest req)
    {
        var stages = await db.LeadStages.ToListAsync();
        var changed = false;
        for (var i = 0; i < req.Ids.Count; i++)
        {
            var stage = stages.FirstOrDefault(s => s.Id == req.Ids[i]);
            if (stage is null || stage.Order == i) continue;
            stage.Order = i;
            changed = true;
        }
        // Tartib o'zgarmagan bo'lsa tarixga qator TUSHMAYDI (ikki marta bosilgan tugma).
        if (changed)
            audit.Record(AuditEntity, "reorder", "update",
                "Lidlar ustunlari tartibi o'zgartirildi: " + string.Join(" → ",
                    stages.OrderBy(s => s.Order).Select(s => s.Title)));
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Audit surati (maxfiy qiymat yo'q).</summary>
    private static object Snapshot(LeadStage s) => new { s.Title, s.Color, s.Order };
}
