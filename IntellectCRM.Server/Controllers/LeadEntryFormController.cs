using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// «LID KIRITISH FORMASI» — menejer <c>/admin/leads</c> da YANGI LID kiritganda ko'radigan
/// maydonlar sozlamasi. Boshqariladi "O'quv bo'limi → Formalar → Lid kiritish formasi"
/// sahifasidan (<c>/admin/forms/lid-kiritish</c>).
///
/// <para>⚠️ Bu OMMAVIY forma EMAS (<see cref="LeadFormsController"/> — u tashqi mijoz uchun,
/// o'z havolasi va manbasi bilan). Bu yerda markaz O'Z XODIMIGA qanday ma'lumot kiritishni
/// majburiy qilishini belgilaydi.</para>
///
/// <para><b>RUXSAT — o'qish ATAYIN ochiq.</b> Sinfda <c>ReadRequiresPerm</c> YO'Q, ya'ni GET
/// odatdagidek har qanday xodimga ochiq. Sabab: sozlamani AYNAN lidlar sahifasi o'qiydi va u
/// yerda xodimda ko'pincha faqat <c>leads.list</c> bo'ladi — GET'ni <c>leads.forms</c> bilan
/// yopsak, lid kiritish oynasi o'sha xodimda umuman ochilmasdi. Javobda shaxsiy ma'lumot yo'q
/// (faqat maydon nomlari va holatlari), shuning uchun buni yopishning ma'nosi ham yo'q.
/// YOZISH esa avvalgidek <c>leads.forms:edit</c> talab qiladi.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.forms")]
[Route("api/admin/lead-entry-form")]
public class LeadEntryFormController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Joriy sozlama (standart maydonlar holati + qo'shimcha savollar).</summary>
    [HttpGet]
    public async Task<ActionResult<LeadEntryFormDto>> Get() =>
        LeadEntryFormService.BuildDto(await LeadEntryFormService.LoadAsync(db));

    /// <summary>Sozlamani saqlash — bandlar TO'LIQ almashtiriladi.</summary>
    [HttpPut]
    public async Task<ActionResult<LeadEntryFormDto>> Save(LeadEntryFormPayload p)
    {
        await LeadEntryFormService.SaveAsync(db, p);

        var rows = await LeadEntryFormService.LoadAsync(db);
        var dto = LeadEntryFormService.BuildDto(rows);
        var required = dto.Standard.Count(s => s.State == LeadEntryRules.StateRequired)
                       + dto.Fields.Count(f => f.Required);
        // Tarix "Lidlar" bo'limida ko'rinadi (`AuditSections` → LeadForm → leads).
        audit.Record("LeadForm", "entry-form", "update",
            $"Lid kiritish formasi o'zgartirildi — {dto.Fields.Count} ta qo'shimcha savol, "
            + $"{required} ta majburiy maydon");
        await db.SaveChangesAsync();
        return dto;
    }

    /// <summary>Maydon turlari ma'lumotnomasi (qo'shimcha savol muharriri uchun).</summary>
    [HttpGet("field-kinds")]
    public ActionResult<IEnumerable<string>> FieldKinds() => Ok(LeadFormService.Kinds);
}
