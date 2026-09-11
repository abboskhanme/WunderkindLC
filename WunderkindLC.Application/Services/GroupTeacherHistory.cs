using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUHNING O'QITUVCHI TARIXI — <see cref="GroupTeacherAssignment"/> yozish va o'qishning
/// YAGONA joyi.
///
/// <para><b>Muammo:</b> <see cref="Group.TeacherId"/> faqat hozirgi o'qituvchini saqlaydi;
/// almashganda eski qiymat yo'qoladi. Shu sabab "2026-09 da bu guruhni kim o'qitgan?" degan
/// savolga javob yo'q edi — o'quvchini ushlab turish bonusini o'qituvchilar orasida oylar
/// nisbatida bo'lish uchun esa aynan shu kerak.</para>
///
/// <para><b>Invariant:</b> bir guruhda bir vaqtda ko'pi bilan BITTA ochiq (<c>ToDate == null</c>)
/// qator bo'ladi. <see cref="AssignAsync"/> buni ta'minlaydi: eski ochiq qator(lar)ni yopib,
/// yangisini ochadi. Boshqa hech qayerda <c>GroupTeacherAssignments.Add</c> qilinmasin.</para>
///
/// <para><b>Sanalar orqaga sanalmaydi</b> — har doim <c>AppClock.Today</c>
/// (<see cref="StudentGroup.RecordedAt"/> bilan bir xil tamoyil).</para>
/// </summary>
public static class GroupTeacherHistory
{
    /// <summary>Backfill (migratsiya) yaratgan qatorlarning <c>CreatedBy</c> qiymati.</summary>
    public const string BackfillActor = "migratsiya";

    /// <summary>
    /// Guruhga o'qituvchi biriktirilganini qayd qiladi. Guruh yaratilganda va o'qituvchi
    /// ALMASHGANDA chaqiriladi. Hozirgi ochiq qator allaqachon shu o'qituvchida bo'lsa —
    /// hech narsa qilmaydi (takroriy saqlash tarixni ifloslantirmasin).
    ///
    /// <para><c>SaveChangesAsync</c> chaqirilmaydi — chaqiruvchi o'z tranzaksiyasida saqlaydi.</para>
    /// </summary>
    /// <returns>Yangi qator ochilgan bo'lsa <c>true</c>.</returns>
    public static async Task<bool> AssignAsync(
        IAppDbContext db, string groupId, string? teacherId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return false;

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var open = await db.GroupTeacherAssignments
            .Where(a => a.GroupId == groupId && a.ToDate == null)
            .ToListAsync(ct);

        // Allaqachon shu o'qituvchida ochiq qator bor — o'zgarish yo'q.
        if (!string.IsNullOrWhiteSpace(teacherId) && open.Count == 1 && open[0].TeacherId == teacherId)
            return false;

        // Eski ochiq qator(lar)ni bugungi sana bilan yopamiz. Bir kunda qayta biriktirilsa
        // FromDate == ToDate bo'ladi — bu "shu kuni almashdi" degani, TeacherAt oxirgisini oladi.
        foreach (var a in open) a.ToDate = today;

        // O'qituvchi olib tashlangan bo'lsa (bo'sh) — yangi qator ochilmaydi, faqat eskisi yopiladi.
        if (string.IsNullOrWhiteSpace(teacherId)) return false;

        db.GroupTeacherAssignments.Add(new GroupTeacherAssignment
        {
            GroupId = groupId,
            TeacherId = teacherId!,
            FromDate = today,
            ToDate = null,
            CreatedBy = actor,
        });
        return true;
    }

    /// <summary>
    /// Berilgan guruhlar uchun butun tarixni yuklaydi (guruh id → qatorlar, <c>FromDate</c> bo'yicha
    /// tartiblangan). Ommaviy hisob-kitob uchun — o'quvchi/oy bo'yicha aylanishda N+1 so'rov bo'lmasin.
    /// </summary>
    public static async Task<Dictionary<string, List<GroupTeacherAssignment>>> LoadAsync(
        IAppDbContext db, IEnumerable<string> groupIds, CancellationToken ct = default)
    {
        var ids = groupIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (ids.Count == 0) return [];

        var rows = await db.GroupTeacherAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.GroupId))
            .ToListAsync(ct);

        return rows
            .GroupBy(a => a.GroupId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(a => a.FromDate, StringComparer.Ordinal)
                      .ThenBy(a => a.ToDate ?? "￿", StringComparer.Ordinal)
                      .ToList());
    }

    /// <summary>
    /// Guruhning FALON OYDA kim o'qitganini aniqlaydi (<paramref name="month"/> — "YYYY-MM").
    /// Oy ichida almashuv bo'lgan bo'lsa — o'sha oyni QAMRAB OLGAN oxirgi biriktirish olinadi
    /// (oy ichida kunma-kun bo'lish ortiqcha aniqlik; bonus taqsimoti oy darajasida ishlaydi).
    ///
    /// <para>Tarix topilmasa <c>null</c> qaytadi — chaqiruvchi <see cref="Group.TeacherId"/> ga
    /// fallback qiladi (backfilldan oldingi davr uchun).</para>
    /// </summary>
    public static string? TeacherAtMonth(List<GroupTeacherAssignment>? history, string month)
    {
        if (history is null || history.Count == 0 || month.Length < 7) return null;

        var monthStart = month[..7] + "-01";
        var monthEnd = month[..7] + "-31";   // ISO satrlar leksikografik solishtiriladi — "-31" oy oxiri o'rnida yetarli

        GroupTeacherAssignment? best = null;
        foreach (var a in history)
        {
            // Biriktirish oy bilan kesishadimi: FromDate <= monthEnd && (ToDate == null || ToDate >= monthStart)
            if (string.CompareOrdinal(a.FromDate, monthEnd) > 0) continue;
            if (a.ToDate is not null && string.CompareOrdinal(a.ToDate, monthStart) < 0) continue;
            best = a;   // ro'yxat FromDate bo'yicha tartiblangan → oxirgi moslik qoladi
        }
        return best?.TeacherId;
    }
}
