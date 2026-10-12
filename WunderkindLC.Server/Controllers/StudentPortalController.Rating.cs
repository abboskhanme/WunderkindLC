using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>Cabinet «Progress → Reyting» (docs/SPEC-crm-upgrade.md P4.3/P4.4).</summary>
public partial class StudentPortalController
{
    /// <summary>
    /// The student's rating block + group and centre top lists (top 10 + the own row when outside).
    /// Student → own; parent → own child only (<c>TargetAsync</c> ownership check — a foreign <c>studentId</c>
    /// gives 404); admin → <c>?studentId=</c> required.
    /// <para><b>No debtor lock</b> — the balance is never checked (owner decision, 2026-10-11).</para>
    /// </summary>
    [HttpGet("rating/summary")]
    public async Task<ActionResult<StudentRatingSummaryDto>> RatingSummary(
        [FromQuery] string? studentId, [FromQuery] string? period, [FromQuery] string? key)
    {
        if (User.IsInRole("admin") && string.IsNullOrWhiteSpace(studentId)) return NeedStudentId();
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound();

        var today = AppClock.Today;
        var (p, error) = RatingPeriods.Resolve(period, key, today);
        if (p is null) return BadRequest(new { message = error });

        var load = RatingEngine.Cached(dataCache);
        var block = await RatingEngine.StudentBlockAsync(db, load, s.Id, p, today);
        if (block is null) return NotFound();
        return RatingEngine.Summary(block, await load(p));
    }
}
