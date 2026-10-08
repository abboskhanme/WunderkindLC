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

        // ⚠️ TARIXSIZ GURUH (backfilldan keyin yaratilgan, masalan «Tugatish — sertifikat bilan»
        // yangi guruhi) o'qituvchisi almashsa — ESKI o'qituvchining davri yo'qolmasin. Ilgari bu
        // yerda faqat YANGI o'qituvchiga bugundan qator ochilardi va undan oldingi barcha oylar
        // (maosh, hisobotlar) "birinchi qator egasi"ga — ya'ni YANGI o'qituvchiga — yozilib qolardi.
        // Bazadagi (hali saqlanmagan o'zgarishsiz) qiymat AsNoTracking so'rov bilan o'qiladi:
        // chaqiruvchi `Group.TeacherId` ni allaqachon yangisiga almashtirgan bo'ladi.
        if (open.Count == 0 && !await db.GroupTeacherAssignments.AnyAsync(a => a.GroupId == groupId, ct))
        {
            var stored = await db.Classes.AsNoTracking()
                .Where(c => c.Id == groupId)
                .Select(c => new { c.TeacherId, c.StartDate })
                .FirstOrDefaultAsync(ct);
            if (stored is not null && !string.IsNullOrWhiteSpace(stored.TeacherId) && stored.TeacherId != teacherId)
            {
                var from = stored.StartDate is { Length: >= 10 } sd ? sd[..10] : null;
                from ??= await db.StudentGroups.AsNoTracking()
                    .Where(sg => sg.GroupId == groupId && sg.JoinedAt != "")
                    .Select(sg => sg.JoinedAt).OrderBy(j => j).FirstOrDefaultAsync(ct);
                if (from is not { Length: >= 10 } || string.CompareOrdinal(from[..10], today) > 0) from = today;
                db.GroupTeacherAssignments.Add(new GroupTeacherAssignment
                {
                    GroupId = groupId,
                    TeacherId = stored.TeacherId,
                    FromDate = from[..10],
                    ToDate = today,
                    CreatedBy = actor,
                });
            }
        }

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

    /* =========================================================================================
     *  DARS SANASI BO'YICHA EGALIK — maosh va hisobotlar "o'tgan oyni KIM o'qitgan" savoliga
     *  hozirgi Group.TeacherId bilan emas, TARIX bilan javob beradi (audit C3).
     * ====================================================================================== */

    /// <summary>
    /// Guruhning SHU SANADAGI asosiy o'qituvchisi (<paramref name="date"/> — "yyyy-MM-dd").
    /// <list type="bullet">
    /// <item>Tarix umuman yo'q — <paramref name="fallbackTeacherId"/> (<see cref="Group.TeacherId"/>);</item>
    /// <item>sanani qamragan biriktirish — o'sha (almashuv kuni ikkalasi ham qamraydi → YANGISI,
    ///   <see cref="TeacherAtMonth"/> bilan bir xil tamoyil);</item>
    /// <item>sana BIRINCHI qatordan OLDIN — birinchi qator egasi (backfill taxmini: "hozirgi o'qituvchi
    ///   guruh boshidan o'qitgan"; yangi guruhda qator yaratilgan KUNDAN boshlanadi, guruh esa
    ///   o'tmishdagi <c>StartDate</c> bilan ochilgan bo'lishi mumkin);</item>
    /// <item>o'qituvchi olib tashlangan oraliq (qatorlar yopiq, yangisi yo'q) — <c>null</c>: hech kim.</item>
    /// </list>
    /// </summary>
    public static string? TeacherOnDate(List<GroupTeacherAssignment>? history, string? date, string? fallbackTeacherId)
    {
        var fallback = string.IsNullOrWhiteSpace(fallbackTeacherId) ? null : fallbackTeacherId;
        if (history is null || history.Count == 0) return fallback;
        if (date is not { Length: >= 10 }) return fallback;
        var d = date[..10];

        var ordered = Ordered(history);
        GroupTeacherAssignment? best = null;
        foreach (var a in ordered)
        {
            if (a.FromDate is { Length: >= 10 } && string.CompareOrdinal(a.FromDate[..10], d) > 0) continue;
            if (a.ToDate is { Length: >= 10 } && string.CompareOrdinal(a.ToDate[..10], d) < 0) continue;
            best = a;
        }
        if (best is not null) return string.IsNullOrWhiteSpace(best.TeacherId) ? null : best.TeacherId;

        var first = ordered[0];
        if (first.FromDate is { Length: >= 10 } && string.CompareOrdinal(d, first.FromDate[..10]) < 0)
            return string.IsNullOrWhiteSpace(first.TeacherId) ? null : first.TeacherId;
        return null;
    }

    /// <summary>
    /// (guruh, oy) → har o'qituvchining shu oydagi ULUSHI (0..1): guruhning shu oydagi REJADAGI dars
    /// sanalaridan (<paramref name="lessonDates"/> — ko'chirishlar va guruh umri bilan,
    /// <see cref="SubstituteTeacherService.LessonDatesInMonth"/>) qanchasi kimga tegishli.
    ///
    /// <para>⚠️ NEGA KUN BO'YICHA: oy o'rtasida o'qituvchi almashsa guruhning oylik maosh ulushi ikkala
    /// o'qituvchi orasida o'tgan darslari NISBATIDA bo'linadi — ikkala varaqaning yig'indisi bitta
    /// o'qituvchi bo'lgandagi summaga TENG (markaz ortiqcha ham, kam ham to'lamaydi).</para>
    ///
    /// <para>Tarix yo'q — hammasi <paramref name="fallbackTeacherId"/> ga (1.0). Oyda dars sanasi yo'q
    /// (guruh umridan tashqari) — oyni qamragan oxirgi biriktirish (<see cref="TeacherAtMonth"/>).
    /// Egasiz sanalar (o'qituvchi olib tashlangan) hech kimga yozilmaydi.</para>
    /// </summary>
    public static Dictionary<string, decimal> SharesInMonth(
        List<GroupTeacherAssignment>? history, IReadOnlyCollection<string> lessonDates, string month,
        string? fallbackTeacherId)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (history is null || history.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(fallbackTeacherId)) result[fallbackTeacherId] = 1m;
            return result;
        }

        if (lessonDates.Count == 0)
        {
            var ordered = Ordered(history);
            var owner = TeacherAtMonth(ordered, month);
            if (owner is null && month.Length >= 7 && ordered[0].FromDate is { Length: >= 7 } f
                && string.CompareOrdinal(month[..7], f[..7]) < 0)
                owner = ordered[0].TeacherId;
            if (!string.IsNullOrWhiteSpace(owner)) result[owner] = 1m;
            return result;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in lessonDates)
        {
            var t = TeacherOnDate(history, d, fallbackTeacherId: null);
            if (t is null) continue;
            counts[t] = counts.GetValueOrDefault(t) + 1;
        }
        foreach (var (t, n) in counts)
            result[t] = (decimal)n / lessonDates.Count;
        return result;
    }

    /// <summary>Tarix qatorlari <c>FromDate</c> bo'yicha tartiblangan (LoadAsync allaqachon tartiblaydi;
    /// testda qo'lda yasalgan ro'yxat uchun himoya).</summary>
    private static List<GroupTeacherAssignment> Ordered(List<GroupTeacherAssignment> history)
    {
        for (var i = 1; i < history.Count; i++)
            if (string.CompareOrdinal(history[i - 1].FromDate, history[i].FromDate) > 0)
                return history.OrderBy(a => a.FromDate, StringComparer.Ordinal)
                    .ThenBy(a => a.ToDate ?? "￿", StringComparer.Ordinal).ToList();
        return history;
    }
}
