using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// HOME WIDGETS — the caller's OWN home page layout (CRM Phase 5, <c>.claude/rules/home-widgets.md</c>).
/// Panel roles only (<c>superadmin,admin,staff</c> — same gate as the home page summary); there is no user id
/// in the route, so nobody can read or change another user's layout. What each card/block SHOWS stays gated by
/// its own endpoint — a layout entry never opens data.
/// <para>Not audited: a personal view preference, not centre data (like the theme/language settings).</para>
/// </summary>
[ApiController]
[Authorize(Roles = "superadmin,admin,staff")]
[Route("api/admin/home/layout")]
public class HomeLayoutController(AppDbContext db) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary><c>{ layout: null }</c> — never customised (the client shows the defaults).</summary>
    [HttpGet]
    public async Task<ActionResult<HomeLayoutResponseDto>> Get()
    {
        if (Uid.Length == 0) return Unauthorized();
        return new HomeLayoutResponseDto(await HomeLayoutRules.GetAsync(db, Uid));
    }

    /// <summary>Saves the layout (normalised: unknown/duplicate keys dropped, missing ones appended).</summary>
    [HttpPut]
    public async Task<ActionResult<HomeLayoutDto>> Save(HomeLayoutDto layout)
    {
        if (Uid.Length == 0) return Unauthorized();
        var (saved, error) = await HomeLayoutRules.SaveAsync(db, Uid, layout);
        return saved is null ? BadRequest(new { message = error }) : saved;
    }

    /// <summary>«Standartga qaytarish».</summary>
    [HttpDelete]
    public async Task<IActionResult> Reset()
    {
        if (Uid.Length == 0) return Unauthorized();
        await HomeLayoutRules.ResetAsync(db, Uid);
        return NoContent();
    }
}
