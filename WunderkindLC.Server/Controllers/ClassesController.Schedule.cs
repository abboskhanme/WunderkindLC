using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Double booking answer of <c>Create</c> / <c>Update</c> / <c>Unarchive</c> (CRM Phase 5,
/// <c>.claude/rules/schedule.md</c> §8). The decision itself is <see cref="ScheduleConflicts.Decide"/>.
/// <para><c>CompleteAndTransfer</c> (superadmin-only) is intentionally NOT checked: the new group takes over
/// the closed group's slot, which is archived in the same transaction.</para>
/// </summary>
public partial class ClassesController
{
    /// <summary>
    /// 409 <c>{ roomConflict: true, message, conflicts: RoomConflictDto[], canForce }</c>. <c>canForce</c> —
    /// the caller is a superadmin (the client then offers «Baribir saqlash» with a required «Sabab»).
    /// </summary>
    private ObjectResult ScheduleConflict(
        ScheduleConflicts.Decision verdict, IReadOnlyList<ScheduleConflicts.Conflict> conflicts) =>
        StatusCode(StatusCodes.Status409Conflict, new
        {
            roomConflict = true,
            message = verdict.Message,
            conflicts = conflicts.Select(c => new RoomConflictDto(
                c.GroupId, c.GroupName, ScheduleConflicts.FormatDays(c.Days), c.ExistingSlot, c.Kind)),
            canForce = verdict.CanForce,
        });
}
