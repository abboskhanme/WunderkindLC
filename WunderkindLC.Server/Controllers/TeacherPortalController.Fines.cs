using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QITUVCHI — o'z davomat jarimalari (CRM Phase 3, U3c): faqat O'QISH, amal yo'q. O'qituvchi TOKENDAN
/// aniqlanadi (<c>Me()</c>) — begona o'qituvchining jarimasi hech qanday parametr bilan ochilmaydi.
/// Maosh ma'lumoti bo'lgani uchun maosh sahifasi bilan bir xil darvoza: <see cref="TeacherPermissions.Salary"/>.
/// </summary>
public partial class TeacherPortalController
{
    [HttpGet("attendance-fines")]
    public async Task<ActionResult<AttendanceFineListDto>> AttendanceFines([FromQuery] string? month)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Salary)) return Forbid();
        if (!string.IsNullOrWhiteSpace(month) && !PaymentFields.IsMonth(month))
            return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
        return await AttendanceFineQueries.ListAsync(db, month, t.Id, status: null);
    }
}
