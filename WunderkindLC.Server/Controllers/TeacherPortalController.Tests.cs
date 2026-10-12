using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// CRM upgrade U4a (docs/SPEC-crm-upgrade.md P4.2) — the teacher's grid batch save and «Ota-onalarga yuborish» for
/// HIS OWN groups. Every action goes through <c>OwnsGroup</c> (the <c>journal</c> permission AND group owner;
/// substitutes are refused — tests.md). Logic shared with the admin side (<see cref="TestResultService"/>,
/// <see cref="TestScoreMessages"/>).
/// </summary>
public partial class TeacherPortalController
{
    /// <summary>Grid batch save (own group): valid rows saved, invalid rows NOT saved and NOT clamped.</summary>
    [HttpPut("test-results/{id}/scores/batch")]
    public async Task<ActionResult<TestScoreBatchResult>> TeacherTestScoresBatch(string id, TestScoreBatchRequest req)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var (result, err) = await TestResultService.SaveScoresBatchAsync(db, id, req.Rows);
        return err != null ? BadRequest(new { message = err }) : result!;
    }

    /// <summary>«Ota-onalarga yuborish» (own group) — through the Telegram outbox, only changed scores unless
    /// <c>resend = true</c>.</summary>
    [HttpPost("test-results/{id}/send-results")]
    public async Task<ActionResult<SendTestResultsResultDto>> TeacherTestSendResults(string id, SendTestResultsRequest? req)
    {
        var groupId = await TestResultService.GroupIdOfAsync(db, id);
        if (groupId is null) return NotFound();
        if (!await OwnsGroup(groupId)) return Forbid();
        var cache = HttpContext.RequestServices.GetRequiredService<DataCache>();
        var (result, err) = await TestScoreMessages.SendAsync(
            db, autoMsg, RatingEngine.Cached(cache), id, req?.Resend == true,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value, audit, AppClock.Today, HttpContext.RequestAborted);
        return err != null ? BadRequest(new { message = err }) : result!;
    }
}
