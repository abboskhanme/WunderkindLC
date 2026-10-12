using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// DATED schedule and the double-booking report (CRM Phase 5, <c>.claude/rules/schedule.md</c> §8). Same
/// class-level gate as the rest of the controller — <c>[AdminPerm("schedule.timetable")]</c>; read-only.
/// </summary>
public partial class ScheduleController
{
    /// <summary>Every entity the dated factory reads — the cache refreshes as soon as one of them changes.</summary>
    private static readonly string[] DatedDeps =
    [
        nameof(Group), nameof(Teacher), nameof(Subject), nameof(Room), nameof(StudentGroup), nameof(Student),
        nameof(LessonNote), nameof(LessonReschedule), nameof(SubstituteTeacherAssignment),
    ];

    private DataCache Cache => HttpContext.RequestServices.GetRequiredService<DataCache>();

    private Task<DatedScheduleDto> LoadDatedAsync(DateOnly from, DateOnly to) =>
        Cache.GetOrCreateAsync(
            $"schedule:dated:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}", DatedDeps, TimeSpan.FromMinutes(5),
            async db => DatedSchedule.Build(await DatedSchedule.LoadAsync(db, from, to), from, to));

    /// <summary>
    /// Real lessons of <c>from..to</c> (both included, at most 7 days; empty = today): rooms with FLOOR,
    /// teachers, lessons (moved ones on their new date/time, substitutes named, members, attendance taken).
    /// </summary>
    [HttpGet("dated")]
    public async Task<ActionResult<DatedScheduleDto>> Dated([FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var (f, t, error) = DatedSchedule.ParseRange(from, to, AppClock.Today);
        if (error is not null) return BadRequest(new { message = error });
        return await LoadDatedAsync(f, t);
    }

    /// <summary>
    /// Existing double bookings (two active groups in the same room or with the same teacher at the same time):
    /// the schedule page shows «Jadvalda {n} ta to'qnashuv bor». Saving such a group is refused only when a
    /// schedule field changes (<see cref="ScheduleConflicts.ScheduleChanged"/>).
    /// </summary>
    [HttpGet("conflicts")]
    public async Task<ActionResult<List<ScheduleConflictPairDto>>> Conflicts()
    {
        var svc = HttpContext.RequestServices.GetRequiredService<RoomConflictService>();
        return (await svc.AllPairsAsync())
            .Select(p => new ScheduleConflictPairDto(
                p.Kind,
                new ScheduleConflictGroupDto(p.GroupAId, p.GroupAName),
                new ScheduleConflictGroupDto(p.GroupBId, p.GroupBName),
                p.Days, p.Slot))
            .ToList();
    }
}
