using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Foydalanuvchiga yuborilgan bildirishnomani ilova TARIXIGA yozadi (push yetib bormasa ham —
/// ilovadagi "Bildirishnomalar" ro'yxatida ko'rinadi). Push yuborilgan har joyda chaqiriladi.
/// </summary>
public static class NotificationStore
{
    /// <summary>Bitta foydalanuvchi uchun bildirishnoma qo'shadi. SaveChanges'ni CHAQIRUVCHI bajaradi.
    /// <paramref name="pushMessageId"/> — admin e'loni bo'lsa manba broadcast id'si (tasdiqlarni bog'lash uchun).</summary>
    public static void Add(IAppDbContext db, string? userId, string title, string body,
        string type = "general", string pushMessageId = "")
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        db.UserNotifications.Add(new UserNotification
        {
            UserId = userId,
            Title = title ?? "",
            Body = body ?? "",
            Type = type,
            PushMessageId = pushMessageId ?? "",
        });
    }
}
