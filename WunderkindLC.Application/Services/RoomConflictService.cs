using WunderkindLC.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Xona/o'qituvchi to'qnashuvi — IO qatlami. QOIDANING O'ZI <see cref="ScheduleConflicts"/> da (sof,
/// testlangan): bir xil xona YOKI bir xil o'qituvchi, umumiy kun, ustma-ust vaqt (yarim-ochiq) va
/// ustma-ust sana oralig'i (<c>StartDate..EndDate</c>).
///
/// <para>⚠️ 2026-10 dan to'qnashuv QAT'IY TAQIQ (409) — faqat superadmin sabab bilan "baribir saqlash"
/// qila oladi (<see cref="ScheduleConflicts.Decide"/>). Ilgari bu yerda faqat OGOHLANTIRISH edi.</para>
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
    /// Berilgan xona (FK RoomId) va/yoki o'qituvchi, kunlar, vaqt va sana oralig'i uchun to'qnashadigan
    /// guruhlar (har guruh bitta qator; xona bir xil bo'lsa "room", aks holda "teacher").
    /// roomId/teacherId ikkalasi ham bo'sh yoki kunlar/vaqt bo'sh berilsa — bo'sh ro'yxat.
    /// <paramref name="excludeGroupId"/> — tahrirlash paytida o'z id'sini chiqarib tashlash uchun.
    /// <paramref name="startDate"/>/<paramref name="endDate"/> — guruh umri (bo'sh = chegarasiz).
    /// </summary>
    public async Task<List<ConflictInfo>> CheckRoomConflictAsync(
        string? roomId, string? teacherId, List<int> days, string? startTime, string? endTime,
        string? excludeGroupId = null, string? startDate = null, string? endDate = null)
    {
        var conflicts = await FindAsync(new ScheduleConflicts.Slot(
            excludeGroupId ?? "", "", roomId?.Trim() ?? "", teacherId?.Trim() ?? "",
            days, startTime ?? "", endTime ?? "", startDate, endDate, IsArchived: false));
        return conflicts.Select(c => new ConflictInfo
        {
            GroupId = c.GroupId,
            GroupName = c.GroupName,
            SharedDays = ScheduleConflicts.FormatDays(c.Days),
            ExistingSlot = c.ExistingSlot,
            Reason = c.Kind,
        }).ToList();
    }

    /// <summary>
    /// <paramref name="candidate"/> bilan to'qnashadigan ARXIVLANMAGAN guruhlar
    /// (<see cref="ScheduleConflicts.Find"/>). Faqat xonasi yoki o'qituvchisi mos guruhlar yuklanadi;
    /// <c>Days</c> (<c>List&lt;int&gt;</c>) xotirada tekshiriladi (SQLite/PostgreSQL bir xil).
    /// </summary>
    public async Task<List<ScheduleConflicts.Conflict>> FindAsync(ScheduleConflicts.Slot candidate)
    {
        var roomId = candidate.RoomId;
        var teacherId = candidate.TeacherId;
        var hasRoom = roomId.Length > 0;
        var hasTeacher = teacherId.Length > 0;
        if ((!hasRoom && !hasTeacher) || candidate.IsArchived) return [];
        if (ScheduleConflicts.CleanDays(candidate.Days).Count == 0
            || ScheduleRules.MakeSlot(0, candidate.StartTime, candidate.EndTime) is null)
            return [];

        var excludeId = candidate.GroupId;
        var existing = await db.Classes.AsNoTracking()
            .Where(g => !g.IsArchived
                        && g.Id != excludeId
                        && ((hasRoom && g.RoomId == roomId) || (hasTeacher && g.TeacherId == teacherId)))
            .ToListAsync();

        return ScheduleConflicts.Find(candidate, existing.Select(ScheduleConflicts.Of));
    }

    /// <summary>Barcha to'qnashayotgan JUFTLIKLAR (jadval sahifasidagi ogohlantirish banneri uchun).</summary>
    public async Task<List<ScheduleConflicts.Pair>> AllPairsAsync()
    {
        var groups = await db.Classes.AsNoTracking().Where(g => !g.IsArchived).ToListAsync();
        return ScheduleConflicts.AllPairs(groups.Select(ScheduleConflicts.Of));
    }
}
