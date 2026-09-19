using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Moliya → «Tranzaksiya turi» ma'lumotnomasi (edutizim `/finance/payment-type`).
///
/// <para>Ruxsat — sinf darajasida <c>finance.main</c>: GET odatdagidek xodimga ochiq (javobda
/// faqat turlar NOMI, nozik ma'lumot yo'q), yozish esa `finance.main` huquqini talab qiladi.</para>
///
/// <para>⚠️ Tur HISOB-KITOBNI o'zgartirmaydi — u <see cref="TransactionType.BaseCategory"/>
/// orqali mavjud tizim toifasiga bog'lanadi (<see cref="TransactionType"/> izohi).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main")]
[Route("api/admin/transaction-types")]
public class TransactionTypesController(AppDbContext db, AuditService audit) : ControllerBase
{
    public record TypePayload(string Name, string Kind, string BaseCategory);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransactionType>>> GetAll() =>
        await db.TransactionTypes.AsNoTracking()
            .OrderBy(x => x.Kind).ThenBy(x => x.Order).ThenBy(x => x.Name)
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<TransactionType>> Create(TypePayload payload)
    {
        var name = (payload.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Nomini kiriting." });
        if (!TransactionTypeCatalog.IsKind(payload.Kind))
            return BadRequest(new { message = "Bo'lim noto'g'ri." });

        // Tartib — shu bo'limdagi oxirgisidan keyin (ro'yxat tepasiga tasodifan tushib qolmasin).
        var last = await db.TransactionTypes.Where(x => x.Kind == payload.Kind)
            .MaxAsync(x => (int?)x.Order) ?? -1;

        var row = new TransactionType
        {
            Name = name,
            Kind = payload.Kind,
            Direction = TransactionTypeCatalog.DirectionOf(payload.Kind),
            BaseCategory = string.IsNullOrWhiteSpace(payload.BaseCategory) ? "other" : payload.BaseCategory,
            Order = last + 1,
        };
        db.TransactionTypes.Add(row);
        audit.Record("TransactionType", row.Id, "create", $"Tranzaksiya turi qo'shildi: {row.Name}");
        await db.SaveChangesAsync();
        return row;
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TransactionType>> Update(string id, TypePayload payload)
    {
        var row = await db.TransactionTypes.FindAsync(id);
        if (row is null) return NotFound();
        var name = (payload.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "Nomini kiriting." });
        if (!TransactionTypeCatalog.IsKind(payload.Kind))
            return BadRequest(new { message = "Bo'lim noto'g'ri." });

        var before = $"{row.Name} ({row.Kind})";
        row.Name = name;
        row.Kind = payload.Kind;
        row.Direction = TransactionTypeCatalog.DirectionOf(payload.Kind);
        if (!string.IsNullOrWhiteSpace(payload.BaseCategory)) row.BaseCategory = payload.BaseCategory;
        audit.Record("TransactionType", row.Id, "update",
            $"Tranzaksiya turi tahrirlandi: {before} → {row.Name} ({row.Kind})");
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Turni o'chirish.
    ///
    /// <para>⚠️ ISHLATILGAN tur o'chirilmaydi (400, nechta amalda ekani bilan) — aks holda
    /// moliya jadvalidagi eski qatorlar nomsiz qolardi. Kerak bo'lsa avval o'sha amallarning
    /// turi almashtiriladi. Naqsh — `.claude/rules/contacts.md` §3.66 dagi ustun o'chirish.</para>
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var row = await db.TransactionTypes.FindAsync(id);
        if (row is null) return NotFound();
        var used = await db.FinanceTransactions.CountAsync(t => t.TypeId == id);
        if (used > 0)
            return BadRequest(new { message = $"Bu tur {used} ta amalda ishlatilgan — o'chirib bo'lmaydi." });

        db.TransactionTypes.Remove(row);
        audit.Record("TransactionType", id, "delete", $"Tranzaksiya turi o'chirildi: {row.Name}");
        await db.SaveChangesAsync();
        return NoContent();
    }
}
