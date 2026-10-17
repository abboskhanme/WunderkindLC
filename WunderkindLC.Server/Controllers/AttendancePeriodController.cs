using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «O'quvchilar davomati» → «Davr tahlili» (SPEC-backlog-2026-10-17 A1, TZ 2.1): davr (≤ 92 kun) bo'yicha davomat,
/// davomati pasaygan guruhlar, sabablar, sinflar kesimi va davrda 3+ / 5+ dars qoldirganlar.
/// Ruxsat — <c>students.attendance</c>, O'QISH ham (javobda o'quvchi ismlari va ota-ona telefonlari).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.attendance", ReadRequiresPerm = true)]
[Route("api/admin/student-attendance/period")]
public class AttendancePeriodController(DataCache dataCache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttendancePeriodDto>> Get(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? groupId, [FromQuery] string? teacherId,
        CancellationToken ct)
    {
        var today = AppClock.Today;
        var error = AttendancePeriodReport.Validate(from, to, today, out var f, out var t);
        if (error is not null) return BadRequest(new { message = error });

        return await dataCache.GetOrCreateAsync(
            $"attendance:period:{f:yyyy-MM-dd}:{t:yyyy-MM-dd}:{groupId}:{teacherId}:{today:yyyy-MM-dd}",
            new[]
            {
                nameof(JournalEntry), nameof(LessonNote), nameof(StudentGroup), nameof(Student),
                nameof(AbsenceReason), nameof(Group), nameof(Teacher), nameof(Subject),
            },
            TimeSpan.FromMinutes(5),
            db => AttendancePeriodReport.BuildAsync(db, f, t, groupId, teacherId, today, ct));
    }
}
