using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// TASK APPROVAL endpoints (CRM Phase 5, <c>.claude/rules/topshiriqlar.md</c> §8). Write = <c>tasks.board</c>
/// (same page key as every other task write) PLUS a hard gate: only the task's creator or an admin/superadmin
/// (<see cref="WorkTaskApproval.IsApprover"/>) — anybody else gets 403 with the reason. Logic lives in
/// <see cref="WorkTaskFlow"/> (the bot's «✅ Tasdiqlash» uses the same methods), under the task lock.
/// </summary>
public partial class WorkTasksController
{
    private bool IsAdminCaller => User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin);

    /// <summary>Approve a done task that waits for approval → «Tasdiqlangan»; the assignee is notified.</summary>
    [HttpPut("{id}/approve")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskDto>> Approve(string id)
    {
        using var gate = await PaymentIdempotency.LockAsync(WorkTaskFlow.LockKey(id));
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        if (!WorkTaskApproval.IsApprover(t, Uid, IsAdminCaller))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = WorkTaskApproval.NotApproverMessage });

        var error = await WorkTaskFlow.ApproveAsync(db, t, Uid, telegram);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        return (await MapAsync([t])).First();
    }

    /// <summary>Return a done task to work with a reason (1..500): first open column, the assignee is notified.</summary>
    [HttpPut("{id}/return")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskDto>> Return(string id, WorkTaskReturnInput input)
    {
        using var gate = await PaymentIdempotency.LockAsync(WorkTaskFlow.LockKey(id));
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        if (!WorkTaskApproval.IsApprover(t, Uid, IsAdminCaller))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = WorkTaskApproval.NotApproverMessage });

        var error = await WorkTaskFlow.ReturnAsync(db, t, Uid, input.Reason, telegram);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        return (await MapAsync([t])).First();
    }
}
