using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Amal sabablari (muzlatish/o'chirish/sinovga qaytarish/lid/guruh) — markaziy CRUD.
/// Davomat (kelmaganlik) sababi alohida (Settings → absence-reasons).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("settings.reasons")]
[Route("api/admin/action-reasons")]
public class ActionReasonsController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Tarixdagi tur — "Sozlamalar" bo'limiga tushadi (<c>AuditSections</c>). ⚠️ Sabab katalogi
    /// HISOBOTLARNI o'zgartiradi: <c>OutOfControl</c> bayrog'i KPI jarimasi va ketish hisobotida JONLI
    /// o'qiladi (o'tgan oylar ham qayta hisoblanadi) — shuning uchun har yaratish/tahrir/o'chirish yoziladi.
    /// </summary>
    private const string AuditReason = "ActionReason";

    private static object Snapshot(ActionReason r) => new { r.Category, r.Label, r.OutOfControl };

    /// <summary>
    /// Ruxsat etilgan kategoriyalar — <b>TARTIB MUHIM</b>: "Sabablar" sahifasi kartochkalarni
    /// shu tartibda chiqaradi.
    ///
    /// <para>⚠️ Bu ro'yxatga kategoriya qo'shish YETARLI EMAS: `ReasonsPage.tsx` dagi
    /// `CATEGORIES` ga ham sarlavha/ikonka qo'shing. Ilgari aynan shu ikkilanish tufayli
    /// <c>contact</c> va <c>archive_student</c> backendda bor, sahifada esa YO'Q edi —
    /// admin ular uchun sabab qo'sha olmasdi va tanlash ro'yxati doim bo'sh chiqardi.
    /// Endi sahifa `GET categories` ni ham o'qiydi va yorlig'i topilmagan kategoriyani
    /// baribir ko'rsatadi (kalit nomi bilan) — bo'shliq KO'RINADI, jimgina yo'qolmaydi.</para>
    /// </summary>
    private static readonly string[] Categories =
    {
        "freeze", "return_trial", "remove_active", "remove_trial", "remove_frozen", "lead_delete",
        // "lead_lost" — lidni «Yo'qotilgan» ustuniga o'tkazishda MAJBURIY sabab (LeadLoss.ReasonCategory).
        "lead_lost",
        "group_delete",
        // "contact" — "Bog'lanish kerak" talabini ochishdagi sabab (ContactService.ReasonCategory).
        "contact",
        "student_delete", "teacher_delete", "staff_delete", "finance_delete", "archive_student",
    };

    /// <summary>
    /// Kategoriyalar ro'yxati — "Sabablar" sahifasi kartochkalarni shundan quradi, ya'ni
    /// backendga qo'shilgan yangi kategoriya UI'da o'z-o'zidan paydo bo'ladi.
    /// </summary>
    [HttpGet("categories")]
    public ActionResult<IEnumerable<string>> GetCategories() => Categories.ToList();

    /// <summary>
    /// KETISHGA oid kategoriyalar — FAQAT shu yerda "nazoratdan tashqari" bayrog'ining ma'nosi bor
    /// (chiquvchi adminning ketish foizi shu uch amaldan hisoblanadi).
    ///
    /// <para>⚠️ Bu ro'yxat MA'LUMOT QATLAMIGA ta'sir qilmaydi: <c>ActionReason.OutOfControl</c>
    /// HAR kategoriyada bor va shu holicha saqlanadi/qaytariladi. Ro'yxat faqat UI uchun —
    /// checkbox ma'nosiz joyda ko'rinmasin. Aks holda "faqat shu kategoriyalarda saqlanadi"
    /// degan istisno CRUD ichiga kirib, keyin kategoriya qo'shilganda jimgina eskirardi.</para>
    /// </summary>
    private static readonly string[] LeaveCategories = { "archive_student", "remove_active", "freeze" };

    /// <summary>Qaysi kategoriyalarda «nazoratdan tashqari» belgisi ko'rsatiladi (UI uchun).</summary>
    [HttpGet("out-of-control-categories")]
    public ActionResult<IEnumerable<string>> GetLeaveCategories() => LeaveCategories.ToList();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ActionReasonDto>>> GetAll() =>
        await db.ActionReasons
            .OrderBy(r => r.Category).ThenBy(r => r.Order)
            .Select(r => new ActionReasonDto(r.Id, r.Category, r.Label, r.Order, r.OutOfControl))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<ActionReasonDto>> Create(ActionReasonCreate p)
    {
        var category = (p.Category ?? "").Trim();
        if (!Categories.Contains(category)) return BadRequest(new { message = "Noma'lum kategoriya" });
        if (string.IsNullOrWhiteSpace(p.Label)) return BadRequest(new { message = "Sabab nomini kiriting" });
        var order = (await db.ActionReasons.Where(r => r.Category == category).MaxAsync(r => (int?)r.Order) ?? -1) + 1;
        var reason = new ActionReason
        {
            Category = category, Label = p.Label.Trim(), Order = order,
            // "Nazoratdan tashqari" — KPI uchun ma'lumot yig'ish bayrog'i (ketish foizidan
            // chiqariladi). Berilmasa `false`: mavjud xatti-harakat o'zgarmaydi.
            OutOfControl = p.OutOfControl,
        };
        db.ActionReasons.Add(reason);
        audit.Record(AuditReason, reason.Id, "create",
            $"Sabab qo'shildi ({reason.Category}): «{reason.Label}»"
            + (reason.OutOfControl ? " — nazoratdan tashqari" : ""),
            after: Snapshot(reason));
        await db.SaveChangesAsync();
        return new ActionReasonDto(reason.Id, reason.Category, reason.Label, reason.Order, reason.OutOfControl);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, ActionReasonUpdate p)
    {
        var reason = await db.ActionReasons.FindAsync(id);
        if (reason is null) return NotFound();
        if (string.IsNullOrWhiteSpace(p.Label)) return BadRequest(new { message = "Sabab nomini kiriting" });
        var before = Snapshot(reason);
        var oldLabel = reason.Label;
        var oldFlag = reason.OutOfControl;
        reason.Label = p.Label.Trim();
        // ⚠️ null = "tegilmadi": faqat nomni yuboradigan eski chaqiruvchi bayroqni jimgina
        // o'chirib yubormasin (bayroq bir marta qo'yilib, keyin nom tahrirlanganda yo'qolardi).
        if (p.OutOfControl is { } flag) reason.OutOfControl = flag;
        var changes = new List<string>();
        if (oldLabel != reason.Label) changes.Add($"«{oldLabel}» → «{reason.Label}»");
        if (oldFlag != reason.OutOfControl)
            changes.Add(reason.OutOfControl ? "nazoratdan tashqari deb belgilandi" : "nazoratdan tashqari belgisi olindi");
        if (changes.Count > 0)
            audit.Record(AuditReason, reason.Id, "update",
                $"Sabab tahrirlandi ({reason.Category}): «{reason.Label}» — {string.Join("; ", changes)}",
                before: before, after: Snapshot(reason));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var reason = await db.ActionReasons.FindAsync(id);
        if (reason is null) return NotFound();
        audit.Record(AuditReason, reason.Id, "delete",
            $"Sabab o'chirildi ({reason.Category}): «{reason.Label}»"
            + (reason.OutOfControl ? " — nazoratdan tashqari edi (KPI/ketish hisobotida endi hisobga olinmaydi)" : ""),
            before: Snapshot(reason));
        db.ActionReasons.Remove(reason);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
