using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — o'quvchi profilidagi «Fikr-mulohazalar» bo'limi.
///
/// <para><b>RUXSAT — ATAYIN QATTIQ:</b> faqat <c>admin</c> / <c>superadmin</c> (va platforma
/// egasi). Bu yerda <see cref="AdminPermAttribute"/> ISHLATILMAYDI, chunki u xodimga (staff)
/// GET'ni har doim ochib qo'yadi — bu ma'lumot esa o'qituvchi haqidagi ichki baholash bo'lib,
/// o'qituvchining o'ziga yoki boshqa xodimga ko'rinmasligi kerak. O'quvchi/ota-ona ham
/// yozmaydi — faqat ma'muriyat.</para>
///
/// <para><b>MAXFIYLIK:</b> xom matn HECH QACHON o'qituvchi profilida yoki o'qituvchi ilovasida
/// ko'rsatilmaydi. U faqat shu yerda (o'quvchi profilida) va o'qituvchining AI TAHLILI uchun
/// manba sifatida ishlatiladi — AI umumlashtirgan xulosagina o'qituvchi profilida chiqadi.</para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin + "," + Roles.PlatformOwner)]
[Route("api/admin")]
public class TeacherReviewsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
    private string? ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>O'quvchi profili uchun: har GURUH bo'yicha o'qituvchi + u haqida yozilgan fikrlar.
    /// O'quvchi 2+ guruhda o'qisa — 2+ blok qaytadi (eng yangilari tepada).</summary>
    [HttpGet("students/{studentId}/teacher-reviews")]
    public async Task<ActionResult<IEnumerable<StudentTeacherReviewGroupDto>>> ForStudent(string studentId) =>
        await TeacherReviewService.ForStudentAsync(db, studentId);

    /// <summary>
    /// O'QITUVCHI PROFILIDAGI «Fikrlar» bo'limi: shu o'qituvchi haqida yozilgan BARCHA fikrlar
    /// (eng yangisi tepada, o'quvchi va guruh nomi bilan). Yozuvlar vaqt o'tgani sayin yig'ilib boradi.
    /// <para>Bu ADMIN ko'rinishi. O'qituvchi portali (<c>api/teacher/*</c>) va Flutter ilovasida
    /// bunday endpoint ATAYIN yo'q — o'qituvchi o'zi haqidagi xom fikrlarni ko'rmaydi.</para>
    /// </summary>
    [HttpGet("teachers/{teacherId}/reviews")]
    public async Task<ActionResult<TeacherReviewFeedDto>> ForTeacher(string teacherId) =>
        await TeacherReviewService.ForTeacherAsync(db, teacherId);

    /// <summary>Yangi fikr yozish. Fikr AYNAN guruh o'qituvchisi haqida bo'ladi (server tekshiradi).</summary>
    [HttpPost("students/{studentId}/teacher-reviews")]
    public async Task<ActionResult<TeacherReviewDto>> Add(string studentId, CreateTeacherReviewRequest req)
    {
        var (dto, err) = await TeacherReviewService.AddAsync(
            db, studentId, req.TeacherId, req.GroupId, req.Text, Actor, ActorId);
        if (err != null) return BadRequest(new { message = err });

        // Auditga MATN yozilmaydi — u o'quvchi haqidagi maxfiy mulohaza; faqat fakt qayd etiladi.
        audit.Record("TeacherReview", dto!.Id, "create",
            $"O'qituvchi haqida fikr yozildi (guruh: {req.GroupId})",
            studentId: studentId, teacherId: dto.TeacherId);
        await db.SaveChangesAsync();
        return dto;
    }

    /// <summary>Xato yozilgan fikrni o'chirish.</summary>
    [HttpDelete("teacher-reviews/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var review = await db.TeacherReviews.FindAsync(id);
        if (review is null) return NotFound();
        var studentId = review.StudentId;
        var teacherId = review.TeacherId;

        if (!await TeacherReviewService.DeleteAsync(db, id)) return NotFound();

        audit.Record("TeacherReview", id, "delete", "O'qituvchi haqidagi fikr o'chirildi",
            studentId: studentId, teacherId: teacherId);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
