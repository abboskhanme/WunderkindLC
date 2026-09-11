using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WunderkindLC.Application.Services;
using System.Security.Claims;

namespace WunderkindLC.Application.Hubs;

/// <summary>
/// Guruh chati uchun real-time SignalR hub'i (/hubs/chat). Ulanishda foydalanuvchi
/// a'zo bo'lgan guruhlariga qo'shiladi; yangi xabarlar ChatService orqali shu guruhlarga
/// "message" hodisasi bilan push qilinadi. Xabar YOZISH REST orqali (ruxsat tekshiriladi),
/// bu hub faqat tarqatish (push) uchun.
/// </summary>
[Authorize]
public class ChatHub(ChatService chat) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var uid = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (uid is not null && role is not null)
        {
            foreach (var className in await chat.ClassNamesForUserAsync(uid, role))
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatService.Group(className));
        }
        await base.OnConnectedAsync();
    }
}
