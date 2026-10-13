using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «📱 Panel» Mini App → links the Telegram chat to the logged-in account (no phone sharing in the bot,
/// 2026-10-12). Any authenticated role, but only ever the CALLER's own account(s) — see
/// <see cref="TelegramMiniAppLink"/>. Rules: <c>.claude/rules/messaging.md</c> §0.5 «Ro'yxatdan o'tish».
/// </summary>
[ApiController]
[Route("api/telegram")]
[Authorize]
public class TelegramLinkController(
    IAppDbContext db, AuditService audit, TelegramService telegram, AutoMessageService autoMsg,
    ILogger<TelegramLinkController> logger) : ControllerBase
{
    public record LinkRequest(string? InitData);

    [HttpPost("link")]
    public async Task<IActionResult> Link([FromBody] LinkRequest req, CancellationToken ct)
    {
        if (!telegram.IsConfigured)
            return BadRequest(new { message = "Telegram bot sozlanmagan" });
        // The Mini App must belong to the MAIN bot: its token signs initData, and the chat id is only
        // deliverable through that bot.
        var tg = TelegramInitData.Validate(req.InitData, telegram.BotToken);
        if (tg is null)
            return BadRequest(new { message = "Telegram ma'lumoti tasdiqlanmadi — Panelni bot ichidan qayta oching" });

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var senderName = string.Join(" ", new[] { tg.FirstName, tg.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var o = await TelegramMiniAppLink.LinkAsync(db, uid, role, tg.ChatId, senderName, ct);
        await TelegramLinkFlow.CompleteAsync(db, audit, telegram, autoMsg, logger, o, tg.ChatId, replyWhenNothingNew: false, ct);

        return Ok(new
        {
            linked = o.Students.Count + o.Teachers.Count + (o.Staff is null ? 0 : 1),
            created = o.NewStudentLinks.Count + o.NewTeachers.Count + (o.NewStaffLink ? 1 : 0),
        });
    }

    /// <summary>Does this account already receive bot messages? Drives the «Bot xabarlarini yoqish» banner of a Mini App
    /// opened from the reply keyboard (no initData there — see <see cref="TelegramLinkTokens"/>).</summary>
    [HttpGet("link-status")]
    public async Task<IActionResult> LinkStatus(CancellationToken ct)
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        return Ok(new { configured = telegram.IsConfigured, linked = await TelegramMiniAppLink.IsLinkedAsync(db, uid, role, ct) });
    }

    /// <summary>One-time <c>sendData</c> payload bound to the CALLER's account; the bot redeems it for the chat it arrives
    /// in (<c>web_app_data</c> service message — only the user's own Mini App can post it into their chat).</summary>
    [HttpPost("link-token")]
    public IActionResult LinkToken([FromServices] TelegramLinkTokens tokens)
    {
        if (!telegram.IsConfigured) return BadRequest(new { message = "Telegram bot sozlanmagan" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uid)) return Unauthorized();
        try
        {
            return Ok(new { data = tokens.Issue(uid, User.FindFirst(ClaimTypes.Role)?.Value ?? "") });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
