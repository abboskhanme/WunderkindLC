using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// CRM upgrade U4a (docs/SPEC-crm-upgrade.md P4.2) — centre overview, Excel-like grid batch save and
/// «Ota-onalarga yuborish». Gate: the CLASS-level page key <c>classes.testResults</c> (GET — reading,
/// PUT batch — edit, POST send — create); no method-level gate here on purpose (it would replace the class one). Logic in <see cref="TestResultService"/> / <see cref="TestScoreMessages"/>
/// (shared with the teacher portal).
/// </summary>
public partial class TestResultsController
{
    /// <summary>The primary constructor is shared with the main file — extra services are resolved here.</summary>
    private AuditService Audit => HttpContext.RequestServices.GetRequiredService<AuditService>();

    /// <summary>Centre overview: every test of the month (<c>?month=yyyy-MM</c>, empty = all), optional kind /
    /// course / teacher filters.</summary>
    [HttpGet("overview")]
    public async Task<List<TestOverviewRowDto>> Overview(
        [FromQuery] string? month = null, [FromQuery] string? kind = null,
        [FromQuery] string? courseId = null, [FromQuery] string? teacherId = null) =>
        await TestResultService.OverviewAsync(db, month, kind, courseId, teacherId);

    /// <summary>
    /// Grid batch save: valid rows saved, invalid rows NOT saved and NOT clamped (returned in <c>errors</c>).
    /// </summary>
    [HttpPut("{id}/scores/batch")]
    public async Task<ActionResult<TestScoreBatchResult>> SaveScoresBatch(string id, TestScoreBatchRequest req)
    {
        var (result, err) = await TestResultService.SaveScoresBatchAsync(db, id, req.Rows);
        if (err == "Test topilmadi") return NotFound(new { message = err });
        return err != null ? BadRequest(new { message = err }) : result!;
    }

    /// <summary>
    /// «Ota-onalarga yuborish» — queues each scored student's result through the Telegram outbox (<c>test_score</c>
    /// rules). Re-sends only changed scores unless <c>resend = true</c>.
    /// </summary>
    [HttpPost("{id}/send-results")]
    public async Task<ActionResult<SendTestResultsResultDto>> SendResults(string id, SendTestResultsRequest? req)
    {
        var sp = HttpContext.RequestServices;
        var (result, err) = await TestScoreMessages.SendAsync(
            db, sp.GetRequiredService<AutoMessageService>(), RatingEngine.Cached(sp.GetRequiredService<DataCache>()),
            id, req?.Resend == true, User.FindFirst(ClaimTypes.NameIdentifier)?.Value, Audit, AppClock.Today,
            HttpContext.RequestAborted);
        if (err == "Test topilmadi") return NotFound(new { message = err });
        return err != null ? BadRequest(new { message = err }) : result!;
    }
}
