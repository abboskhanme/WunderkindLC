using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Support o'qituvchi slotlari uchun umumiy yordamchi. Slot CRUD'i `TeacherPortalController`
/// (/api/teacher/support) ichida; bu yerda faqat admin va o'qituvchi portali bo'lishadigan mantiq.
/// </summary>
public static class SupportService
{
    /// <summary>Berilgan o'quvchi id'lari (null'lar tashlanadi) → FISH xaritasi.</summary>
    public static async Task<Dictionary<string, string>> StudentNamesAsync(
        IAppDbContext db, IEnumerable<string?> ids)
    {
        var list = ids.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Students.Where(s => list.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);
    }

    /// <summary>
    /// Bot "Adminga murojaat" xabarnomasini (ota-ona ismi, TELEFONI, matni) kim oladi: admin/superadmin
    /// va <c>messages.support</c> ruxsati bor XODIM. Ruxsatsiz xodim (kassir, turniket operatori) —
    /// YO'Q: panelda "Support Telegram"ni ocha olmaydigan odamga shaxsiy ma'lumot Telegram orqali
    /// sizib chiqmasin. Ruxsat qoidasi panel darvozasi bilan bir xil (<see cref="PermissionRules.HasSection"/>).
    /// </summary>
    public static bool ReceivesBotSupport(string? role, IEnumerable<string>? permissions) =>
        role == Roles.Admin || role == Roles.SuperAdmin
        || (role == Roles.Staff && PermissionRules.HasSection(permissions, "messages.support"));

    /// <summary>
    /// Slot BOSHLANISH vaqti o'tib ketganmi (markaz vaqti). Buzuq sana/vaqt — o'tgan deb
    /// hisoblanmaydi (eski qatorni bloklab qo'ymaslik uchun; sana tekshiruvi yaratishda bor).
    /// ⚠️ O'tgan slotni bron qilib bo'lmaydi — ilgari o'quvchi kechagi bo'sh vaqtni ham band qila olardi.
    /// </summary>
    public static bool IsPast(SupportSlot slot, DateTime now)
    {
        if (!DateOnly.TryParse(slot.Date, out var d)) return false;
        var today = DateOnly.FromDateTime(now);
        if (d != today) return d < today;
        return TimeSpan.TryParse(slot.StartTime, out var start) && start <= now.TimeOfDay;
    }

    /// <summary>Slot vaqti matni bildirishnoma uchun: "07.10.2026 15:00–15:30".</summary>
    public static string When(SupportSlot slot)
    {
        var date = DateOnly.TryParse(slot.Date, out var d) ? d.ToString("dd.MM.yyyy") : slot.Date;
        var end = string.IsNullOrWhiteSpace(slot.EndTime) ? "" : $"–{slot.EndTime}";
        return $"{date} {slot.StartTime}{end}";
    }

    /// <summary>
    /// Support bron hodisasini IKKINCHI tomonga bildiradi: ilova tarixi (<see cref="NotificationStore"/>)
    /// + push (FCM sozlangan bo'lsa). Ilgari bron, o'quvchining bekor qilishi va o'qituvchining band
    /// slotni o'chirishi hech kimga bildirilmasdi — o'quvchi o'chirilgan darsga kelib turardi.
    /// SaveChanges'ni O'ZI chaqiradi; xato YUTILADI (bildirishnoma asosiy amalni buzmasin).
    /// </summary>
    public static async Task NotifyAsync(
        IAppDbContext db, FcmService? fcm, string? userId, string title, string body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        try
        {
            NotificationStore.Add(db, userId, title, body, "support");
            await db.SaveChangesAsync(ct);

            var json = AppSecrets.FcmServiceAccountJson;
            if (fcm is null || !FcmService.IsConfigured(json)) return;
            var tokens = await db.DeviceTokens.Where(d => d.UserId == userId)
                .Select(d => d.Token).Distinct().ToListAsync(ct);
            if (tokens.Count == 0) return;
            var res = await fcm.SendAsync(json, tokens, title, body, ct);
            if (res.InvalidTokens.Count > 0)
            {
                var dead = res.InvalidTokens.ToList();
                db.DeviceTokens.RemoveRange(db.DeviceTokens.Where(d => dead.Contains(d.Token)));
                await db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            /* bildirishnoma — ikkilamchi; asosiy amal (bron/bekor) allaqachon saqlangan */
        }
    }
}
