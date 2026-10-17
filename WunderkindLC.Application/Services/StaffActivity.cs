using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>Xodim/o'qituvchi faolligi (Xodimlar ro'yxati, SPEC-backlog F4 S1). <paramref name="Kind"/>: staff | teacher,
/// <paramref name="Id"/> — ro'yxatdagi id (xodimda AppUser.Id, o'qituvchida Teacher.Id). Vaqtlar — markaz vaqti, ISO.
/// <paramref name="LastSeenAt"/> — oxirgi kirish YOKI sessiya yangilanishi (token yangilanishi ~ har faol soat), kechrog'i.</summary>
public record StaffActivityDto(
    string Kind, string Id, string? LastLoginAt, string? LastSeenAt,
    int AuditActions, int TasksClosed, int LeadsHandled, int AttendanceLessons);

/// <summary>
/// XODIMLAR FAOLLIGI (TZ 3.1: "xodimlar profiliga oxirgi kirgan vaqti, faolligi statistikasi"). Faqat O'QIYDI.
///
/// <para>Manbalar (davr — oxirgi <c>days</c> kun): «O'zgarishlar tarixi» qatorlari (<c>AuditLog.ActorId</c>), yopilgan
/// topshiriqlar (<c>WorkTask.CompletedById</c>), ishlangan lidlar (<c>LeadEvent.ActorUserId</c>, TAKRORSIZ lidlar) va
/// o'qituvchida davomati olingan darslar (<c>LessonNote.AttendanceTakenAt</c>, o'tgan guruh egasi bo'yicha emas —
/// davomatni olgan odam). <b>Oxirgi ko'rinish</b> — oxirgi login va oxirgi token yangilanishining kechrog'i: sessiya
/// uzoq yashaydi, faqat login vaqti haftalab eskirib ko'rinardi.</para>
/// </summary>
public static class StaffActivity
{
    public const int DefaultDays = 30;
    public const int MaxDays = 365;

    /// <summary>Ikki markaz vaqtining kechrog'i ("yyyy-MM-ddTHH:mm:ss" — ordinal solishtiriladi).</summary>
    public static string? Later(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? (string.IsNullOrEmpty(b) ? null : b)
        : string.IsNullOrEmpty(b) ? a
        : string.CompareOrdinal(a, b) >= 0 ? a : b;

    public static async Task<List<StaffActivityDto>> BuildAsync(IAppDbContext db, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var sinceLocal = AppClock.Now.AddDays(-days);
        var since = sinceLocal.ToString("yyyy-MM-ddTHH:mm:ss");

        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Teacher)
            .Select(u => new { u.Id, u.Role, u.LastLoginAt })
            .ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => t.UserId != null && t.UserId != "")
            .Select(t => new { t.Id, t.UserId })
            .ToListAsync(ct);

        // Token yangilanishlari — UTC; faqat eng so'nggisi kerak (SQL'da GROUP BY).
        var lastRefresh = (await db.RefreshTokens.AsNoTracking()
                .Where(r => userIds.Contains(r.UserId))
                .GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, Last = g.Max(r => r.CreatedAt) })
                .ToListAsync(ct))
            .ToDictionary(x => x.UserId, x => AppClock.ToLocal(x.Last).ToString("yyyy-MM-ddTHH:mm:ss"));

        var audits = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.ActorId != null && string.Compare(a.Timestamp, since) >= 0)
                .GroupBy(a => a.ActorId!)
                .Select(g => new { g.Key, N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
        var tasks = (await db.WorkTasks.AsNoTracking()
                .Where(t => t.CompletedAt != null && t.CompletedAt >= sinceLocal && t.CompletedById != "")
                .GroupBy(t => t.CompletedById)
                .Select(g => new { g.Key, N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
        var leads = (await db.LeadEvents.AsNoTracking()
                .Where(e => e.ActorUserId != null && string.Compare(e.CreatedAt, since) >= 0)
                .Select(e => new { e.ActorUserId, e.LeadId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ActorUserId!)
            .ToDictionary(g => g.Key, g => g.Count());
        var lessons = (await db.LessonNotes.AsNoTracking()
                .Where(n => n.AttendanceTakenAt != null && n.AttendanceTakenAt >= sinceLocal && n.AttendanceTakenById != null)
                .Select(n => new { n.AttendanceTakenById, n.ClassId, n.Date })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.AttendanceTakenById!)
            .ToDictionary(g => g.Key, g => g.Count());

        StaffActivityDto Row(string kind, string id, string userId, string? lastLogin) => new(
            kind, id, lastLogin, Later(lastLogin, lastRefresh.GetValueOrDefault(userId)),
            audits.GetValueOrDefault(userId), tasks.GetValueOrDefault(userId), leads.GetValueOrDefault(userId),
            lessons.GetValueOrDefault(userId));

        var byUser = users.ToDictionary(u => u.Id);
        var result = users.Where(u => u.Role != Roles.Teacher)
            .Select(u => Row("staff", u.Id, u.Id, u.LastLoginAt))
            .ToList();
        foreach (var t in teachers)
            if (byUser.TryGetValue(t.UserId!, out var u))
                result.Add(Row("teacher", t.Id, u.Id, u.LastLoginAt));
        return result;
    }
}
