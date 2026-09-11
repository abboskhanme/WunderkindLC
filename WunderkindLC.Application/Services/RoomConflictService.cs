using WunderkindLC.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Xona/o'qituvchi konfliktini aniqlash servisi.
/// Guruh yaratish/tahrirlashda bir xil xona YOKI bir xil o'qituvchi, kunlar va vaqt
/// to'qnashuvini WARNING sifatida qaytaradi (o'qituvchi bir vaqtda 2 guruhda bo'la olmaydi,
/// xona tanlanmagan yoki har xil bo'lsa ham). REJECT QILINMAYDI — admin "baribir saqlash" qila oladi.
/// </summary>
public class RoomConflictService(IAppDbContext db)
{
    public class ConflictInfo
    {
        public string GroupId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        /// <summary>"Du, Se, Ch" kabi umumiy kunlar.</summary>
        public string SharedDays { get; set; } = string.Empty;
        /// <summary>"09:00–10:00" kabi mavjud guruh vaqt oralig'i.</summary>
        public string ExistingSlot { get; set; } = string.Empty;
        /// <summary>"room" | "teacher" — nima to'qnashganini bildiradi.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Berilgan xona (FK RoomId) va/yoki o'qituvchi, kunlar va vaqt oralig'i uchun konfliktli
    /// guruhlarni qaytaradi (xona bir xil BO'LSA yoki o'qituvchi bir xil BO'LSA — biri kifoya).
    /// roomId/teacherId ikkalasi ham bo'sh yoki days/time bo'sh berilsa — bo'sh ro'yxat.
    /// <paramref name="excludeGroupId"/> — tahrirlash paytida o'z id'sini chiqarib tashlash uchun.
    /// </summary>
    public async Task<List<ConflictInfo>> CheckRoomConflictAsync(
        string? roomId, string? teacherId, List<int> days, string? startTime, string? endTime,
        string? excludeGroupId = null)
    {
        var hasRoom = !string.IsNullOrWhiteSpace(roomId);
        var hasTeacher = !string.IsNullOrWhiteSpace(teacherId);
        if ((!hasRoom && !hasTeacher)
            || days.Count == 0
            || string.IsNullOrWhiteSpace(startTime)
            || string.IsNullOrWhiteSpace(endTime))
            return [];

        var existing = await db.Classes
            .Where(g => ((hasRoom && g.RoomId == roomId) || (hasTeacher && g.TeacherId == teacherId))
                     && !g.IsArchived
                     && g.Id != excludeGroupId
                     && g.Days != null
                     && g.StartTime != null && g.StartTime != ""
                     && g.EndTime != null && g.EndTime != "")
            .ToListAsync();

        var conflicts = new List<ConflictInfo>();
        foreach (var g in existing)
        {
            var sharedDays = (g.Days ?? []).Intersect(days).ToList();
            if (sharedDays.Count > 0 && TimeOverlap(g.StartTime, g.EndTime, startTime, endTime))
            {
                var reason = hasRoom && g.RoomId == roomId ? "room" : "teacher";
                conflicts.Add(new ConflictInfo
                {
                    GroupId = g.Id,
                    GroupName = g.Name,
                    SharedDays = FormatDays(sharedDays),
                    ExistingSlot = $"{g.StartTime}–{g.EndTime}",
                    Reason = reason,
                });
            }
        }
        return conflicts;
    }

    private static bool TimeOverlap(string s1, string e1, string s2, string e2)
    {
        var a1 = TimeToMinutes(s1);
        var b1 = TimeToMinutes(e1);
        var a2 = TimeToMinutes(s2);
        var b2 = TimeToMinutes(e2);
        // Yarim-ochiq oraliq: [a1, b1) va [a2, b2) — b1<=a2 yoki b2<=a1 bo'lsa to'qnashmaydi
        return !(b1 <= a2 || b2 <= a1);
    }

    private static int TimeToMinutes(string hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return 0;
        var parts = hhmm.Split(':');
        if (parts.Length < 2) return 0;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return 0;
        return h * 60 + m;
    }

    private static string FormatDays(List<int> days)
    {
        var dayNames = new[] { "Du", "Se", "Ch", "Pa", "Jum", "Sha", "Yak" };
        return string.Join(", ", days.Order().Select(d => d >= 0 && d < dayNames.Length ? dayNames[d] : d.ToString()));
    }
}
