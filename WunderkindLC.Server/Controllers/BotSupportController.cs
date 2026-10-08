using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
using System.Security.Claims;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("messages.support")]
[Route("api/admin/messages/support")]
public class BotSupportController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    private string AdminName => User.FindFirst(ClaimTypes.Name)?.Value ?? "";

    [HttpGet("threads")]
    public async Task<ActionResult<List<BotThreadDto>>> Threads()
    {
        var users = await db.BotUsers
            .OrderByDescending(u => u.LastMessageAt ?? u.StartedAt)
            .ToListAsync();

        return users.Select(u => new BotThreadDto(
            u.ChatId, u.Name, u.Username, u.Phone, u.Linked,
            u.StartedAt, u.LastMessageAt, u.LastText, u.AdminUnread)).ToList();
    }

    [HttpGet("threads/{chatId:long}/messages")]
    public async Task<ActionResult<List<BotSupportMessageDto>>> Messages(long chatId)
    {
        var user = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user is null) return NotFound();

        var messages = await db.BotSupportMessages
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (user.AdminUnread > 0)
        {
            user.AdminUnread = 0;
            await db.SaveChangesAsync();
        }

        return messages.Select(m => new BotSupportMessageDto(
            m.Id, m.FromUser, m.Text, m.AdminName, m.CreatedAt)).ToList();
    }

    [HttpPost("threads/{chatId:long}/reply")]
    public async Task<ActionResult<BotSupportMessageDto>> Reply(long chatId, BotSupportReplyRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni kerak" });

        var user = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user is null) return NotFound();
        if (!telegram.IsConfigured)
            return BadRequest(new { message = "Telegram bot sozlanmagan (TELEGRAM_BOT_TOKEN) — javob yuborib bo'lmaydi." });

        // ⚠️ AVVAL yuboriladi, YETIB BORGANDAGINA yoziladi. Ilgari natija e'tiborsiz qolardi: foydalanuvchi
        // botni bloklagan/tarmoq uzilgan bo'lsa ham javob tarixda "yuborildi" bo'lib turardi — admin
        // javob berdim deb o'ylar, ota-ona esa hech narsa olmasdi.
        if (!await telegram.SendMessageAsync(chatId, text))
            return BadRequest(new
            {
                message = "Javob Telegram'ga yetkazilmadi — foydalanuvchi botni bloklagan yoki to'xtatgan bo'lishi, "
                          + "yoki tarmoq xatosi bo'lishi mumkin. Javob saqlanmadi, keyinroq qayta urinib ko'ring.",
            });

        var msg = new BotSupportMessage
        {
            ChatId = chatId,
            FromUser = false,
            Text = text,
            AdminName = AdminName,
            CreatedAt = AppClock.Iso(),
        };
        db.BotSupportMessages.Add(msg);
        await db.SaveChangesAsync();

        return new BotSupportMessageDto(msg.Id, msg.FromUser, msg.Text, msg.AdminName, msg.CreatedAt);
    }

    [HttpGet("unread")]
    public async Task<ActionResult<object>> Unread()
        => Ok(new { count = await db.BotUsers.SumAsync(u => u.AdminUnread) });
}
