using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Hubs;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Guruh chatining umumiy mantig'i (a'zolik, xabar olish/yuborish). Admin web va
/// o'qituvchi/o'quvchi portal controllerlari shu xizmatdan foydalanadi. Xabar saqlangach
/// SignalR orqali shu guruhga (real-time) push qilinadi.
/// </summary>
public class ChatService(IAppDbContext db, IHubContext<ChatHub> hub)
{
    /// <summary>Guruh nomidan SignalR guruh nomi.</summary>
    public static string Group(string className) => $"class:{className}";

    /// <summary>
    /// Barcha xodimlar (o'qituvchilar + adminlar) uchun umumiy guruh chati kanali kaliti.
    /// Guruh nomi bo'la olmaydigan zahiraviy qiymat — ChatMessage.ClassName ustunida saqlanadi.
    /// </summary>
    public const string StaffChannel = "__xodimlar__";

    /// <summary>"since" so'rov parametrini (ISO sana) DateTime'ga aylantiradi (xato/bo'sh → null).</summary>
    public static DateTime? ParseSince(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    /// <summary>
    /// Foydalanuvchi a'zo bo'lgan chat kanallari (nomlari). admin/superadmin = barcha guruhlar + xodimlar;
    /// o'qituvchi = guruh rahbarligi + dars beradigan guruhlar + xodimlar; o'quvchi = O'Z GURUHLARI (a'zolik bo'yicha, bittadan ko'p bo'lishi mumkin).
    /// "Xodimlar" (<see cref="StaffChannel"/>) — barcha o'qituvchi va adminlar uchun umumiy kanal.
    /// </summary>
    public async Task<List<string>> ClassNamesForUserAsync(string userId, string role)
    {
        switch (role)
        {
            case "admin":
            case "superadmin":
                {
                    var names = await db.Classes.Where(c => !c.IsArchived)
                        .OrderBy(c => c.Grade).ThenBy(c => c.Name)
                        .Select(c => c.Name).ToListAsync();
                    names.Add(StaffChannel);
                    return names;
                }

            case "student":
                {
                    // O'quvchi — O'ZI A'ZO bo'lgan BARCHA guruhlar (bir necha guruhda o'qish odatiy hol).
                    // ⚠️ Ilgari faqat `Student.ClassName` (BIRINCHI qo'shilgan guruh) qaytarilardi:
                    // o'quvchi eski guruhida muzlatilib yangisida o'qiyotgan bo'lsa, YANGI guruh
                    // chatiga umuman kira olmasdi (`CanAccessAsync` shu ro'yxatga qaraydi).
                    // `ClassName` — faqat a'zolik umuman bo'lmagan (juda eski) yozuvlar uchun zaxira.
                    var s = await db.Students.FirstOrDefaultAsync(x => x.UserId == userId);
                    if (s is null) return new List<string>();
                    var names = await StudentMembershipView.DisplayGroupNamesAsync(db, s);
                    return names;
                }

            case "teacher":
                {
                    var t = await db.Teachers.FirstOrDefaultAsync(x => x.UserId == userId);
                    if (t is null) return new();
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(t.HomeroomClass)) names.Add(t.HomeroomClass);

                    // Dars beradigan guruhlar — guruhga biriktirilgan o'qituvchi (Group.TeacherId).
                    // Ko'rinish qoidasi guruhlar ro'yxati bilan BITTA manbadan (TeacherGroupAccess):
                    // arxivlangan/tugatilgan va vaqtincha bloklangan guruh chati ham ochilmaydi.
                    var taughtNames = await db.Classes.Where(c => c.TeacherId == t.Id)
                        .Where(TeacherGroupAccess.VisibleQuery)
                        .Select(c => c.Name).ToListAsync();
                    foreach (var n in taughtNames) names.Add(n);

                    var list = names.ToList();
                    list.Add(StaffChannel); // har bir o'qituvchi — xodim
                    return list;
                }

            default:
                return new();
        }
    }

    /// <summary>Foydalanuvchi shu guruh chatiga kira oladimi.</summary>
    public async Task<bool> CanAccessAsync(string userId, string role, string className)
    {
        if (role == "admin") return true;
        var names = await ClassNamesForUserAsync(userId, role);
        return names.Contains(className);
    }

    /// <summary>Admin panelidagi chat "Xabarlar" bo'limiga tegishli — ruxsat tokeni shu nom bilan.</summary>
    public const string MessagesSection = "messages";

    /// <summary>
    /// ADMIN paneli (web) chatining darvozasi — SOF funksiya, shuning uchun testlanadi.
    ///
    /// <para>Sabab: <c>AdminPermAttribute</c> xodim (staff) uchun BARCHA GET so'rovlarini ruxsat
    /// tekshirmasdan o'tkazadi (bo'limlararo o'qish buzilmasin deb). Guruh chati esa "bo'limlararo
    /// o'qish" emas — bu shaxsiy yozishmalar va <see cref="StaffChannel"/> (xodimlar kanali).
    /// Darvozasiz qolsa, tor ruxsatli xodim (masalan faqat "books") istalgan guruh chatini va
    /// xodimlar kanalini o'qiy olardi.</para>
    ///
    /// <para>Qoida: <b>admin/superadmin</b> — cheklovsiz (barcha kanallar, mavjud xatti-harakat
    /// o'zgarmaydi); <b>staff</b> — faqat "messages" bo'lim ruxsati (yalang <c>"messages"</c> yoki
    /// <c>"messages:amal"</c>) berilgan bo'lsa; boshqa rollar — yo'q (ular o'z portallaridan,
    /// a'zolik tekshiruvi bilan kiradi: <see cref="CanAccessAsync"/>).</para>
    ///
    /// <para>Aynan shu qoida frontend'dagi <c>RequirePerm perm="messages"</c> bilan bir xil —
    /// ya'ni UI'da "Xabarlar" sahifasini ko'ra oladigan xodim serverdan ham hamma narsani oladi,
    /// ko'ra olmaydigani esa endi serverdan ham ololmaydi.</para>
    /// </summary>
    public static bool CanUseAdminChat(string? role, IEnumerable<string>? permissions) =>
        role == Roles.Admin || role == Roles.SuperAdmin ||
        (role == Roles.Staff && PermissionRules.HasSection(permissions, MessagesSection));

    /// <summary>
    /// Guruh chatidagi xabarlar. since=null bo'lsa — eng so'nggi 200 ta (vaqt bo'yicha o'sish
    /// tartibida); since berilsa — shu vaqtdan keyingilar (yangilanish uchun).
    /// </summary>
    public async Task<List<ChatMessageDto>> GetMessagesAsync(string className, DateTime? since)
    {
        if (since is null)
        {
            var recent = await db.ChatMessages
                .Where(m => m.ClassName == className)
                .OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync();
            recent.Reverse();
            return recent.Select(ToDto).ToList();
        }

        var after = await db.ChatMessages
            .Where(m => m.ClassName == className && m.CreatedAt > since)
            .OrderBy(m => m.CreatedAt).ToListAsync();
        return after.Select(ToDto).ToList();
    }

    /// <summary>
    /// Guruh chatiga xabar yozadi (jo'natuvchi nomi/roli akkauntdan olinadi), saqlaydi va
    /// SignalR orqali shu guruhga push qiladi. Bo'sh matn yuborilmaydi.
    /// </summary>
    public async Task<ChatMessageDto?> PostAsync(string className, string userId, string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0) return null;

        var user = await db.Users.FindAsync(userId);
        var msg = new ChatMessage
        {
            ClassName = className,
            SenderUserId = userId,
            SenderName = user?.FullName ?? "Foydalanuvchi",
            SenderRole = user?.Role ?? "",
            Text = text,
            CreatedAt = AppClock.Now,
        };
        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync();

        var dto = ToDto(msg);
        await hub.Clients.Group(Group(className)).SendAsync("message", dto);
        return dto;
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id, m.ClassName, m.SenderUserId, m.SenderName, m.SenderRole, m.Text, m.CreatedAt.ToString("o"));
}
