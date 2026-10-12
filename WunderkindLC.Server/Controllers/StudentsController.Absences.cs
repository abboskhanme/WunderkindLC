using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quvchi profili → «Qoldirilgan darslar» (CRM Phase 3, U3d — SPEC P3.5). Ruxsat — sinf darajasidagi
/// <c>[AdminPerm("students.list")]</c> (profil bilan bir xil).
/// </summary>
public partial class StudentsController
{
    /// <summary>
    /// <c>GET api/admin/students/{id}/absences?from=&amp;to=</c> — davr (standart oxirgi 180 kun, ko'pi bilan 366)
    /// bo'yicha sanoqlar, sabablar kesimi va qoldirilgan/kech qolingan darslar ro'yxati (yangisi tepada).
    /// </summary>
    [HttpGet("{id}/absences")]
    public async Task<ActionResult<StudentAbsencesDto>> Absences(string id, [FromQuery] string? from, [FromQuery] string? to)
    {
        if (!await db.Students.AsNoTracking().AnyAsync(s => s.Id == id))
            return NotFound(new { message = "O'quvchi topilmadi" });
        var (f, t, error) = StudentAbsences.NormalizeRange(from, to, AppClock.Today);
        if (error is not null) return BadRequest(new { message = error });
        return await StudentAbsences.BuildAsync(db, id, f, t);
    }
}
