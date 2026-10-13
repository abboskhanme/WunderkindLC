using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Moliya → «Jarima» → o'qituvchiga QO'LDA jarima (2026-10-13). Jarima tanlangan oy MAOSHIDAN ushlanadi
/// (`SalaryLedger`), kassaga tegmaydi; o'chirilmaydi — bekor qilinadi (sabab bilan) va qayta tiklanadi.
/// Ruxsat — davomat jarimalari bilan bir xil (`finance.main`, o'qish ham ruxsat bilan: javobda o'qituvchilar
/// summalari). Qoidalar — <see cref="TeacherFines"/>.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", ReadRequiresPerm = true)]
[Route("api/admin/teacher-fines")]
public class TeacherFinesController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private string? ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<List<TeacherFineDto>> List([FromQuery] string? month, [FromQuery] string? teacherId)
    {
        var q = db.TeacherFines.AsNoTracking().AsQueryable();
        if (PaymentFields.IsMonth(month)) q = q.Where(f => f.Month == month);
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(f => f.TeacherId == teacherId);
        var rows = await q.OrderByDescending(f => f.CreatedAt).Take(1000).ToListAsync();
        var ids = rows.Select(r => r.TeacherId).Distinct().ToList();
        var names = await db.Teachers.AsNoTracking().Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName);
        return rows.Select(f => Dto(f, names.GetValueOrDefault(f.TeacherId, ""))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<TeacherFineDto>> Create(TeacherFineRequest req)
    {
        var error = TeacherFines.Validate(req.Month, req.Amount, req.Reason, AppClock.Today);
        if (error is not null) return BadRequest(new { message = error });
        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Id == req.TeacherId);
        if (teacher is null) return BadRequest(new { message = "O'qituvchi topilmadi" });

        var fine = new TeacherFine
        {
            TeacherId = teacher.Id, Month = req.Month, Amount = decimal.Round(req.Amount, 2), Reason = req.Reason.Trim(),
            Status = TeacherFines.Active, CreatedAt = DateTime.UtcNow, CreatedById = ActorId, CreatedByName = Actor,
        };
        db.TeacherFines.Add(fine);
        audit.Record("TeacherFine", $"{teacher.Id}:{fine.Id}", "create",
            $"Jarima qo'yildi: {teacher.FullName} — {AuditService.Money(fine.Amount)} so'm, {fine.Month} maoshidan; sabab: {fine.Reason}",
            teacherId: teacher.Id);
        await db.SaveChangesAsync();
        return Dto(fine, teacher.FullName);
    }

    [HttpPost("{id}/waive")]
    public async Task<ActionResult<TeacherFineDto>> Waive(string id, TeacherFineWaiveRequest req)
    {
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is 0 or > 300) return BadRequest(new { message = "Bekor qilish sababini yozing (300 belgigacha)" });
        var fine = await db.TeacherFines.FirstOrDefaultAsync(f => f.Id == id);
        if (fine is null) return NotFound(new { message = "Jarima topilmadi" });
        if (fine.Status == TeacherFines.Waived) return BadRequest(new { message = "Jarima allaqachon bekor qilingan" });
        fine.Status = TeacherFines.Waived;
        fine.WaivedAt = DateTime.UtcNow;
        fine.WaivedById = ActorId;
        fine.WaivedByName = Actor;
        fine.WaiveReason = reason;
        var name = await TeacherNameAsync(fine.TeacherId);
        audit.Record("TeacherFine", $"{fine.TeacherId}:{fine.Id}", "update",
            $"Jarima bekor qilindi: {name} — {AuditService.Money(fine.Amount)} so'm ({fine.Month}); sabab: {reason}",
            teacherId: fine.TeacherId);
        await db.SaveChangesAsync();
        return Dto(fine, name);
    }

    [HttpPost("{id}/restore")]
    public async Task<ActionResult<TeacherFineDto>> Restore(string id)
    {
        var fine = await db.TeacherFines.FirstOrDefaultAsync(f => f.Id == id);
        if (fine is null) return NotFound(new { message = "Jarima topilmadi" });
        if (fine.Status == TeacherFines.Active) return BadRequest(new { message = "Jarima allaqachon amalda" });
        fine.Status = TeacherFines.Active;
        fine.WaivedAt = null;
        fine.WaivedById = null;
        fine.WaivedByName = null;
        fine.WaiveReason = null;
        var name = await TeacherNameAsync(fine.TeacherId);
        audit.Record("TeacherFine", $"{fine.TeacherId}:{fine.Id}", "update",
            $"Jarima tiklandi: {name} — {AuditService.Money(fine.Amount)} so'm ({fine.Month})", teacherId: fine.TeacherId);
        await db.SaveChangesAsync();
        return Dto(fine, name);
    }

    private async Task<string> TeacherNameAsync(string teacherId) =>
        await db.Teachers.AsNoTracking().Where(t => t.Id == teacherId).Select(t => t.FullName).FirstOrDefaultAsync() ?? "";

    private static TeacherFineDto Dto(TeacherFine f, string teacherName) =>
        new(f.Id, f.TeacherId, teacherName, f.Month, f.Amount, f.Reason, f.Status, f.CreatedAt, f.CreatedByName,
            f.WaivedAt, f.WaivedByName, f.WaiveReason);
}
