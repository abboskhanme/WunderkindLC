using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// CRM upgrade U4d — monthly TOP/BOTTOM preview (docs/SPEC-crm-upgrade.md P4.5): the same lists the
/// <see cref="MonthlyRatingNotifier"/> sends on the rule day. Report page tab «TOP/BOTTOM».
/// </summary>
public partial class RatingController
{
    /// <summary>
    /// <c>?month=yyyy-MM</c> (empty = the previous month — the one the next sending uses) and <c>?scope=group|centre</c>
    /// (empty = the configured scope). 400 «Davr noto'g'ri» for a malformed or future month.
    /// </summary>
    [HttpGet("top-bottom")]
    [AdminPerm("classes.rating", ReadRequiresPerm = true)]
    public async Task<ActionResult<TopBottomDto>> TopBottom([FromQuery] string? month, [FromQuery] string? scope)
    {
        var (dto, _, error) = await TopBottomService.BuildAsync(
            db, RatingEngine.Cached(cache), month, scope, AppClock.Today, HttpContext.RequestAborted);
        return dto is null ? BadRequest(new { message = error }) : dto;
    }
}
