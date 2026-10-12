using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// DAVOMAT JARIMALARI (CRM Phase 3, U3c — SPEC P3.4): Moliya → Jarima → «Davomat jarimalari».
/// Jarimani fon xizmati yozadi (<see cref="AttendanceDisciplineService"/>), bu yerda — ro'yxat, bekor qilish
/// (sabab bilan), tiklash va bir kunning hammasini bekor qilish (bayram — loyihada bayram kalendari yo'q).
/// <para>Ruxsat — <c>finance.main</c>, O'QISH ham (<c>ReadRequiresPerm</c>): javobda o'qituvchilarning maosh
/// ushlanmalari bor. Har amal tarixga yoziladi (<c>TeacherAttendanceFine</c> → «O'qituvchilar»).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", ReadRequiresPerm = true)]
[Route("api/admin/attendance-fines")]
public class AttendanceFinesController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttendanceFineListDto>> List(
        [FromQuery] string? month, [FromQuery] string? teacherId, [FromQuery] string? status)
    {
        if (!string.IsNullOrWhiteSpace(month) && !PaymentFields.IsMonth(month))
            return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
        return await AttendanceFineQueries.ListAsync(db, month, teacherId, status);
    }

    /// <summary>Bitta jarimani bekor qilish (sabab 1..300 belgi). Faqat faol jarima.</summary>
    [HttpPut("{id}/waive")]
    public async Task<ActionResult<AttendanceFineDto>> Waive(string id, AttendanceFineWaiveRequest req)
    {
        if (AttendanceDiscipline.WaiveReasonError(req.Reason) is { } error) return BadRequest(new { message = error });
        var f = await db.TeacherAttendanceFines.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound(new { message = "Jarima topilmadi" });
        if (f.Status != AttendanceDiscipline.FineActive)
            return Conflict(new { message = "Jarima allaqachon bekor qilingan" });

        var (teacherName, groupName) = await NamesAsync(f.TeacherId, f.GroupId);
        Apply(f, req.Reason!.Trim());
        audit.Record(AttendanceFineAudit.EntityType, AttendanceFineAudit.EntityId(f.GroupId, f.Id), "update",
            $"Davomat jarimasi bekor qilindi: {teacherName}, {groupName}, {AttendanceDiscipline.Dmy(f.LessonDate)} "
            + $"({AuditService.Money(f.Amount)} so'm) — sabab: {f.WaiveReason}",
            teacherId: f.TeacherId);
        await db.SaveChangesAsync();
        return AttendanceFineQueries.ToDto(f, teacherName, groupName);
    }

    /// <summary>Bekor qilingan jarimani qaytarish (yana maoshdan ushlanadi).</summary>
    [HttpPut("{id}/restore")]
    public async Task<ActionResult<AttendanceFineDto>> Restore(string id)
    {
        var f = await db.TeacherAttendanceFines.FirstOrDefaultAsync(x => x.Id == id);
        if (f is null) return NotFound(new { message = "Jarima topilmadi" });
        if (f.Status != AttendanceDiscipline.FineWaived)
            return Conflict(new { message = "Jarima bekor qilinmagan" });

        var (teacherName, groupName) = await NamesAsync(f.TeacherId, f.GroupId);
        f.Status = AttendanceDiscipline.FineActive;
        f.WaivedAt = null;
        f.WaivedById = null;
        f.WaivedByName = null;
        f.WaiveReason = null;
        audit.Record(AttendanceFineAudit.EntityType, AttendanceFineAudit.EntityId(f.GroupId, f.Id), "update",
            $"Davomat jarimasi tiklandi: {teacherName}, {groupName}, {AttendanceDiscipline.Dmy(f.LessonDate)} "
            + $"({AuditService.Money(f.Amount)} so'm)",
            teacherId: f.TeacherId);
        await db.SaveChangesAsync();
        return AttendanceFineQueries.ToDto(f, teacherName, groupName);
    }

    /// <summary>Bir KUNNING barcha faol jarimalarini bekor qilish (bayram, server uzilishi) — har biriga tarix qatori.</summary>
    [HttpPut("waive-day")]
    public async Task<ActionResult<object>> WaiveDay(AttendanceFineWaiveDayRequest req)
    {
        if (!DateOnly.TryParseExact(req.Date, "yyyy-MM-dd", out _))
            return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        if (AttendanceDiscipline.WaiveReasonError(req.Reason) is { } error) return BadRequest(new { message = error });

        var reason = req.Reason!.Trim();
        var rows = await db.TeacherAttendanceFines
            .Where(f => f.LessonDate == req.Date && f.Status == AttendanceDiscipline.FineActive)
            .ToListAsync();
        foreach (var f in rows)
        {
            var (teacherName, groupName) = await NamesAsync(f.TeacherId, f.GroupId);
            Apply(f, reason);
            audit.Record(AttendanceFineAudit.EntityType, AttendanceFineAudit.EntityId(f.GroupId, f.Id), "update",
                $"Davomat jarimasi kun bo'yicha bekor qilindi ({AttendanceDiscipline.Dmy(f.LessonDate)}): {teacherName}, "
                + $"{groupName} ({AuditService.Money(f.Amount)} so'm) — sabab: {reason}",
                teacherId: f.TeacherId);
        }
        await db.SaveChangesAsync();
        return Ok(new { waived = rows.Count });
    }

    private void Apply(TeacherAttendanceFine f, string reason)
    {
        f.Status = AttendanceDiscipline.FineWaived;
        f.WaivedAt = AppClock.Now;
        f.WaivedById = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        f.WaivedByName = User.FindFirst(ClaimTypes.Name)?.Value;
        f.WaiveReason = reason;
    }

    private async Task<(string Teacher, string Group)> NamesAsync(string teacherId, string groupId) =>
        ((await db.Teachers.AsNoTracking().Where(t => t.Id == teacherId).Select(t => t.FullName).FirstOrDefaultAsync()) ?? "",
         (await db.Classes.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync()) ?? "");
}
