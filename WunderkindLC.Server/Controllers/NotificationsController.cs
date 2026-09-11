using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Admin/xodim (va istalgan tizimga kirgan foydalanuvchi) BILDIRISHNOMALARI — Topbar dagi qo'ng'iroq
/// ikonkasi shu yerdan o'qiydi. Har foydalanuvchi FAQAT o'z bildirishnomalarini ko'radi
/// (<see cref="UserNotification"/>, tokendagi NameIdentifier bo'yicha). O'qituvchi/o'quvchi portalidagi
/// <c>/teacher/notifications</c>, <c>/student/notifications</c> bilan bir xil shakl.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/notifications")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    /// <summary>Joriy foydalanuvchining oxirgi 100 bildirishnomasi (o'qilmaganlar soni bilan).</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationsResponseDto>> List()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var items = await db.UserNotifications
            .Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();
        var unread = items.Count(n => n.ReadAt == null);
        return new NotificationsResponseDto(unread, items.Select(n =>
            new UserNotificationDto(n.Id, n.Title, n.Body, n.Type, n.CreatedAt.ToString("o"),
                n.ReadAt != null, n.ConfirmedAt != null)).ToList());
    }

    /// <summary>Barcha o'qilmagan bildirishnomalarni o'qilgan deb belgilaydi (qo'ng'iroq ochilganda).</summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return Unauthorized();
        var unread = await db.UserNotifications.Where(n => n.UserId == uid && n.ReadAt == null).ToListAsync();
        foreach (var n in unread) n.ReadAt = AppClock.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
