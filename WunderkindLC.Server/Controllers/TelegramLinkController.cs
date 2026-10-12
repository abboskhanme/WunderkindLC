using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // One audit row per NEW link (never the chat id).
        foreach (var reg in o.NewStudentLinks)
        {
            var s = o.Students.First(x => x.Id == reg.StudentId);
            audit.Record(AuditService.EntityStudent, s.Id, "update",
                $"Telegram ulandi (Panel orqali kirishda): {s.FullName}", studentId: s.Id);
        }
        foreach (var t in o.NewTeachers)
            audit.Record("TeacherSalary", t.Id, "update",
                $"Telegram ulandi (Panel orqali kirishda): {t.FullName}", teacherId: t.Id);
        if (o.NewStaffLink && o.Staff is not null)
            audit.Record("Staff", o.Staff.Id, "update", $"Telegram ulandi (Panel orqali kirishda): {o.Staff.FullName}");

        await db.SaveChangesAsync(ct);

        if (o.AnyNew)
        {
            // Bot confirmation + refreshed keyboard (Panel · one-time code · …); a failure must not fail the link.
            try
            {
                var books = (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.BookSalesEnabled ?? true;
                await telegram.SendMessageAsync(tg.ChatId, TelegramMiniAppLink.ConfirmationText(o),
                    TelegramBotService.RegisteredKeyboard(books, panel: true), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Telegram ulanish tasdig'i yuborilmadi");
            }
            // «01» thank-you (outbox, deduped) — same as the old phone registration.
            foreach (var reg in o.NewStudentLinks)
            {
                var s = o.Students.First(x => x.Id == reg.StudentId);
                await autoMsg.DispatchBotConnectedAsync(db, s, reg, ct);
            }
        }

        return Ok(new
        {
            linked = o.Students.Count + o.Teachers.Count + (o.Staff is null ? 0 : 1),
            created = o.NewStudentLinks.Count + o.NewTeachers.Count + (o.NewStaffLink ? 1 : 0),
        });
    }
}
