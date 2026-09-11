using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'rinbosar o'qituvchilarni boshqarish API (Admin portal).
///
/// <para>⚠️ <b>O'QISH ham darvozalangan</b> (<c>ReadRequiresPerm = true</c>): javobda
/// <c>EstimatedSalary</c> va <c>PerLessonFee</c> — ya'ni o'qituvchining MAOSH raqamlari qaytadi.
/// <c>AdminPerm</c> odatda GET'ni har qanday xodimga ochadi (bo'limlararo o'qish uchun), bu yerda
/// esa u kassir/qabulchi ham o'qituvchilar maoshini ko'rishini anglatardi. Bir xil sabab bilan
/// darvozalangan boshqa joylar: <c>ContractsController</c>, <c>CareerController</c>
/// (`.claude/rules/uploads-security.md`).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers.substitutions", ReadRequiresPerm = true)]
[Route("api/admin/substitute-teachers")]
public class SubstituteTeachersController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "Admin";
    private string? ActorUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// O'rinbosar o'qituvchi tayinlovlari ro'yxati.
    ///
    /// <para>⚠️ CHEGARA: bir so'rovda ko'pi bilan
    /// <see cref="SubstituteTeacherService.MaxRows"/> qator (audit <c>MaxLimit</c> bilan bir xil
    /// g'oya) — ilgari <c>ToListAsync()</c> xom holda edi va bir necha yildan keyin butun jadval
    /// xotiraga yig'ilardi. <b>Chegara foydalanuvchidan YASHIRILMAYDI:</b> javob sarlavhalarida
    /// <c>X-Total-Count</c> (jami topilgan) va <c>X-Returned-Count</c> (shu javobda nechta)
    /// qaytadi — UI "jami N, bu yerda M" deb yoza oladi. Javob TANASI ilgarigidek MASSIV
    /// (mavjud klientlar buzilmasin).</para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAssignments(
        [FromQuery] string? groupId,
        [FromQuery] string? teacherId,
        [FromQuery] string? date,
        [FromQuery] bool? isActive,
        [FromQuery] int? limit)
    {
        var (items, total) = await SubstituteTeacherService.GetAssignmentsPageAsync(
            db, groupId, teacherId, date, isActive, limit ?? SubstituteTeacherService.MaxRows);

        Response.Headers["X-Total-Count"] = total.ToString();
        Response.Headers["X-Returned-Count"] = items.Count.ToString();
        return Ok(items);
    }

    /// <summary>
    /// JONLI HISOB (admin modali): tanlangan sanalar uchun nechta dars, bitta dars narxi,
    /// o'rinbosarga to'lanadigan va asosiy o'qituvchidan ushlanadigan summa.
    ///
    /// <para>⚠️ Frontend pulni O'ZI hisoblaMAYDI — ilgari hisoblardi va saqlangandan keyin
    /// chiqadigan raqamdan farq qilardi (modalda bir son, ro'yxatda boshqa son). Tekshiruv ham
    /// AYNAN yaratishdagi tekshiruv (<c>ValidateAsync</c>), ya'ni "modal ruxsat berdi, server rad
    /// etdi" holati bo'lmaydi.</para>
    ///
    /// <para><c>Warning</c> — ogohlantirish (guruhda faol o'quvchi yo'q, bu oyda pul yig'ilmagan
    /// va h.k.) yoki <c>null</c>. U tayinlashga TO'SIQ EMAS, faqat izoh.</para>
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<SubstituteFeePreviewDto>> Preview(
        [FromQuery] string? groupId,
        [FromQuery] string? substituteTeacherId,
        [FromQuery] List<string>? dates,
        [FromQuery] string? date,
        [FromQuery] string? endDate)
    {
        var req = new CreateSubstituteAssignmentRequest(
            groupId ?? "", substituteTeacherId ?? "", dates, date, endDate);
        var (error, preview) = await SubstituteTeacherService.PreviewAsync(db, req);
        if (error is not null) return BadRequest(new { message = error });
        return Ok(preview);
    }

    /// <summary>
    /// Guruhning oydagi dars kunlarini olish (modal uchun).
    /// </summary>
    [HttpGet("group-lesson-dates")]
    public async Task<IActionResult> GetGroupLessonDates(
        [FromQuery] string groupId,
        [FromQuery] string month)
    {
        var result = await SubstituteTeacherService.GetGroupLessonDatesAsync(db, groupId, month);
        return Ok(result);
    }

    /// <summary>
    /// ID bo'yicha tayinlovni olish.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var item = await SubstituteTeacherService.GetByIdAsync(db, id);
        if (item is null) return NotFound();
        return Ok(item);
    }

    /// <summary>
    /// Yangi o'rinbosar o'qituvchi tayinlash.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubstituteAssignmentRequest req)
    {
        // Audit yozuvi SERVIS ICHIDA, tayinlov bilan BITTA SaveChanges'da saqlanadi
        // (`.claude/rules/audit.md` §1). Ilgari u shu yerda, servis allaqachon saqlab bo'lgandan
        // KEYIN chaqirilar va bazaga UMUMAN tushmasdi.
        var (ok, message, entity) = await SubstituteTeacherService.CreateAssignmentAsync(
            db, req, ActorName, ActorUserId, audit);
        if (!ok)
            return BadRequest(new { message });

        var created = await SubstituteTeacherService.GetByIdAsync(db, entity!.Id);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, created);
    }

    /// <summary>
    /// O'rinbosar o'qituvchi tayinlovini bekor qilish.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id)
    {
        // Audit — servis ichida (yuqoridagi bilan bir xil sabab). `action` = "delete":
        // "cancel" ruxsat etilgan qiymatlar ro'yxatida YO'Q.
        var (ok, message) = await SubstituteTeacherService.CancelAssignmentAsync(db, id, audit);
        if (!ok)
            return BadRequest(new { message });

        return Ok(new { message });
    }
}
