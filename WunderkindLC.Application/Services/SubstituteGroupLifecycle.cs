using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH HAYOTI O'ZGARGANDA O'RINBOSAR TAYINLOVLARI — guruh yopilganda/arxivlanganda/o'chirilganda
/// va asosiy o'qituvchi almashganda (<c>.claude/rules/substitute-teachers.md</c> §9).
///
/// <para><b>NEGA:</b> tayinlov sanalari (<see cref="SubstituteTeacherAssignment.SelectedDates"/>)
/// O'ZIDA saqlanadi va guruh hayotidan mustaqil sanaladi. Guruh 6 ta sanadan 3 tasi o'tganda
/// yopilsa, o'rinbosarga hamon 6 dars yozilar, maxraj esa (yopilgan oyning haqiqiy darslari)
/// qirqilgani uchun bitta dars narxi 2 barobar chiqib, asosiy o'qituvchining ulushi 0 gacha
/// tushardi. O'qituvchi almashganda esa <see cref="SubstituteTeacherAssignment.OriginalTeacherId"/>
/// eski o'qituvchida qolib, ushlanma HECH KIMDAN ayrilmasdi; o'rinbosarning o'zi yangi egasi
/// bo'lsa — bir darsga ikki marta haq olardi.</para>
///
/// <para>Ma'lumot modeli <see cref="SubstituteTeacherService"/> niki: sanalar ISO "yyyy-MM-dd".
/// Qirqish/bo'lish SAQLANGAN XOM sanalar ustida (<see cref="SubstituteTeacherService.SummaryDates"/>) —
/// pul uchun filtrlangan <see cref="SubstituteTeacherService.EffectiveDates"/> EMAS: aks holda ko'chirilgan
/// kunda haqiqatan o'tilgan dars yoki guruh umridan tashqariga tushgan sana yozib qo'yilgan ro'yxatdan
/// ABADIY o'chib ketardi (audit R1-2). Eski (sanalarsiz) qator guruh dars kunlariga yoyiladi.
/// Qisqartirilgan qatorda <c>Date</c>/<c>EndDate</c> (SQL indeks oralig'i) qolgan sanalarning
/// birinchi/oxirgisiga moslanadi.</para>
///
/// <para>Audit — <c>substitute_teacher</c>, <c>EntityId = "{groupId}:{assignmentId}"</c>, teacherId =
/// O'RINBOSAR (§6). <c>SaveChanges</c> QILINMAYDI — chaqiruvchi saqlaydi.</para>
/// </summary>
public static class SubstituteGroupLifecycle
{
    /// <summary>Nima o'zgardi (audit/javob matni uchun).</summary>
    public readonly record struct Result(int Trimmed, int Cancelled, int Reassigned, int Split)
    {
        public int Total => Trimmed + Cancelled + Reassigned + Split;
    }

    /// <summary>Sanalarni <paramref name="cutoff"/> bo'yicha ikkiga bo'ladi (sof funksiya).
    /// <paramref name="keepCutoff"/> = true — <c>cutoff</c> kunining O'ZI birinchi qismda qoladi
    /// (yopish kuni — oxirgi dars), false — ikkinchi qismga o'tadi (o'qituvchi almashgan kun —
    /// o'sha kundan yangi o'qituvchi dars o'tadi).</summary>
    public static (List<string> Before, List<string> After) SplitDates(
        IEnumerable<string> dates, string cutoff, bool keepCutoff)
    {
        var before = new List<string>();
        var after = new List<string>();
        foreach (var d in dates.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                     .Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            var cmp = string.CompareOrdinal(d, cutoff);
            if (cmp < 0 || (cmp == 0 && keepCutoff)) before.Add(d);
            else after.Add(d);
        }
        return (before, after);
    }

    /// <summary>
    /// GURUH YOPILDI / ARXIVLANDI / TUGATILDI — <paramref name="lastLessonDate"/> dan KEYINGI
    /// o'rinbosar sanalari olib tashlanadi; sanasi qolmagan tayinlov BEKOR qilinadi.
    ///
    /// <para>⚠️ Yopish kunining O'ZI dars (<see cref="SalaryLedger.LessonEnd"/> chegarasi KIRADI —
    /// muzlatish/yopish sanasi hisobda ham qatnashgan kun), shuning uchun o'sha kungi o'rinbosar
    /// darsi QOLADI va unga haq yoziladi.</para>
    ///
    /// <para>⚠️ Guruhning <c>EndDate</c>/<c>ArchivedAt</c> maydonlari o'zgartirilishidan OLDIN
    /// chaqirilsin: eski (sanalarsiz) qatorning sanalari guruh umri bo'yicha qirqib hisoblanadi.</para>
    /// </summary>
    public static async Task<Result> TrimAfterAsync(
        IAppDbContext db, AuditService? audit, Group group, string lastLessonDate, string why,
        CancellationToken ct = default)
    {
        var cutoff = Day(lastLessonDate);
        if (cutoff.Length < 10) return default;

        var rows = await db.SubstituteTeacherAssignments
            .Where(a => a.GroupId == group.Id && a.IsActive && string.Compare(a.EndDate ?? a.Date, cutoff) > 0)
            .ToListAsync(ct);
        if (rows.Count == 0) return default;

        var names = await TeacherNamesAsync(db, rows.Select(r => r.SubstituteTeacherId), ct);
        var moves = await MovesAsync(db, group, ct);
        int trimmed = 0, cancelled = 0;
        foreach (var a in rows)
        {
            var (keep, drop) = SplitDates(SubstituteTeacherService.SummaryDates(a, group, moves), cutoff, keepCutoff: true);
            if (drop.Count == 0) continue;
            var sub = names.GetValueOrDefault(a.SubstituteTeacherId, "Noma'lum o'qituvchi");
            if (keep.Count == 0)
            {
                a.IsActive = false;
                cancelled++;
                audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "delete",
                    $"\"{group.Name}\" guruhidagi o'rinbosar o'qituvchi {sub} tayinlovi bekor qilindi " +
                    $"({SubstituteTeacherService.FormatDates(drop)} kunlari) — {why}",
                    teacherId: a.SubstituteTeacherId);
            }
            else
            {
                SetDates(a, keep);
                trimmed++;
                audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "update",
                    $"\"{group.Name}\" guruhidagi o'rinbosar o'qituvchi {sub} tayinlovidan " +
                    $"{SubstituteTeacherService.FormatDates(drop)} kunlari olib tashlandi — {why}; " +
                    $"qoldi: {SubstituteTeacherService.FormatDates(keep)}",
                    teacherId: a.SubstituteTeacherId);
            }
        }
        return new Result(trimmed, cancelled, 0, 0);
    }

    /// <summary>
    /// GURUH O'CHIRILDI — guruhning BARCHA tayinlovlari (bekor qilinganlari ham) o'chiriladi: guruhsiz
    /// ularni hisoblab ham, ko'rsatib ham bo'lmaydi. Faol tayinlovlar uchun audit yoziladi.
    /// </summary>
    public static async Task<int> RemoveAllAsync(
        IAppDbContext db, AuditService? audit, Group group, CancellationToken ct = default)
    {
        var rows = await db.SubstituteTeacherAssignments.Where(a => a.GroupId == group.Id).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var names = await TeacherNamesAsync(db, rows.Select(r => r.SubstituteTeacherId), ct);
        var moves = await MovesAsync(db, group, ct);
        foreach (var a in rows.Where(r => r.IsActive))
            audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "delete",
                $"\"{group.Name}\" guruhi o'chirildi — o'rinbosar o'qituvchi " +
                $"{names.GetValueOrDefault(a.SubstituteTeacherId, "Noma'lum o'qituvchi")} tayinlovi o'chirildi " +
                $"({SubstituteTeacherService.FormatDates(SubstituteTeacherService.SummaryDates(a, group, moves))} kunlari)",
                teacherId: a.SubstituteTeacherId);
        db.SubstituteTeacherAssignments.RemoveRange(rows);
        return rows.Count;
    }

    /// <summary>
    /// GURUHNING ASOSIY O'QITUVCHISI ALMASHDI — <paramref name="fromDate"/> (bugun) va undan keyingi
    /// o'rinbosar sanalari uchun:
    /// <list type="bullet">
    ///   <item>o'rinbosar YANGI EGANING O'ZI bo'lsa — o'sha sanalar olib tashlanadi (u baribir o'z
    ///     guruhida dars o'tadi; aks holda bir darsga ham maosh, ham o'rinbosarlik haqi olardi);</item>
    ///   <item>aks holda <c>OriginalTeacherId</c> = yangi ega (ushlanma darsni o'tkazib yuborgan
    ///     HAQIQIY egadan ayrilsin). Tayinlovda o'tmish sanalari ham bo'lsa u IKKIGA bo'linadi:
    ///     o'tmish eski egada qoladi (o'sha kunlar uning darsi edi), kelajak — yangi qatorga.</item>
    /// </list>
    /// ⚠️ Guruhning yangi maydonlari qo'llanishidan OLDIN chaqirilsin (eski sanalarsiz qator ESKI
    /// jadval bo'yicha yoyiladi).
    /// </summary>
    public static async Task<Result> OnTeacherChangedAsync(
        IAppDbContext db, AuditService? audit, Group group, string newOwnerId, string fromDate, string actor,
        CancellationToken ct = default)
    {
        var cutoff = Day(fromDate);
        if (cutoff.Length < 10 || string.IsNullOrWhiteSpace(newOwnerId)) return default;

        var rows = await db.SubstituteTeacherAssignments
            .Where(a => a.GroupId == group.Id && a.IsActive && string.Compare(a.EndDate ?? a.Date, cutoff) >= 0)
            .ToListAsync(ct);
        if (rows.Count == 0) return default;

        var names = await TeacherNamesAsync(db, rows.Select(r => r.SubstituteTeacherId).Append(newOwnerId), ct);
        var owner = names.GetValueOrDefault(newOwnerId, "Noma'lum o'qituvchi");
        var moves = await MovesAsync(db, group, ct);
        int trimmed = 0, cancelled = 0, reassigned = 0, split = 0;
        foreach (var a in rows)
        {
            var (past, future) = SplitDates(SubstituteTeacherService.SummaryDates(a, group, moves), cutoff, keepCutoff: false);
            if (future.Count == 0) continue;
            var sub = names.GetValueOrDefault(a.SubstituteTeacherId, "Noma'lum o'qituvchi");

            if (string.Equals(a.SubstituteTeacherId, newOwnerId, StringComparison.Ordinal))
            {
                if (past.Count == 0)
                {
                    a.IsActive = false;
                    cancelled++;
                    audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "delete",
                        $"\"{group.Name}\" guruhida o'rinbosar {sub} tayinlovi bekor qilindi " +
                        $"({SubstituteTeacherService.FormatDates(future)} kunlari) — u endi guruhning asosiy o'qituvchisi",
                        teacherId: a.SubstituteTeacherId);
                }
                else
                {
                    SetDates(a, past);
                    trimmed++;
                    audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "update",
                        $"\"{group.Name}\" guruhida o'rinbosar {sub} tayinlovidan {SubstituteTeacherService.FormatDates(future)} " +
                        $"kunlari olib tashlandi — u endi guruhning asosiy o'qituvchisi",
                        teacherId: a.SubstituteTeacherId);
                }
                continue;
            }

            if (string.Equals(a.OriginalTeacherId, newOwnerId, StringComparison.Ordinal)) continue;

            if (past.Count == 0)
            {
                a.OriginalTeacherId = newOwnerId;
                reassigned++;
                audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "update",
                    $"\"{group.Name}\" guruhida o'rinbosar {sub} tayinlovi ({SubstituteTeacherService.FormatDates(future)} kunlari) " +
                    $"yangi asosiy o'qituvchi {owner} nomiga o'tkazildi (ushlanma undan)",
                    teacherId: a.SubstituteTeacherId);
                continue;
            }

            // Aralash: o'tmish eski egada qoladi, kelajak — yangi egaga yangi qatorda.
            SetDates(a, past);
            var next = new SubstituteTeacherAssignment
            {
                GroupId = a.GroupId,
                OriginalTeacherId = newOwnerId,
                SubstituteTeacherId = a.SubstituteTeacherId,
                Reason = a.Reason,
                CreatedBy = actor,
                CreatedById = a.CreatedById,
                CreatedAt = AppClock.Now,
                IsActive = true,
            };
            SetDates(next, future);
            db.SubstituteTeacherAssignments.Add(next);
            split++;
            audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(a), "update",
                $"\"{group.Name}\" guruhida o'qituvchi almashdi — o'rinbosar {sub} tayinlovi bo'lindi: " +
                $"{SubstituteTeacherService.FormatDates(past)} eski o'qituvchida qoldi",
                teacherId: a.SubstituteTeacherId);
            audit?.Record(SubstituteTeacherService.AuditEntityType, SubstituteTeacherService.AuditEntityId(next), "create",
                $"\"{group.Name}\" guruhida o'qituvchi almashdi — o'rinbosar {sub} {SubstituteTeacherService.FormatDates(future)} " +
                $"kunlari yangi asosiy o'qituvchi {owner} o'rniga dars o'tadi",
                teacherId: a.SubstituteTeacherId);
        }
        return new Result(trimmed, cancelled, reassigned, split);
    }

    private static void SetDates(SubstituteTeacherAssignment a, List<string> dates)
    {
        a.SelectedDates = dates;
        a.Date = dates[0];
        a.EndDate = dates[^1];
    }

    /// <summary>Guruhning dars ko'chirishlari — faqat eski (sanalarsiz, oraliqli) tayinlovni yoyish uchun.</summary>
    private static async Task<IReadOnlyList<JournalService.LessonMove>?> MovesAsync(
        IAppDbContext db, Group group, CancellationToken ct) =>
        (await SubstituteTeacherService.MovesByGroupAsync(db, new[] { group.Id }, ct)).GetValueOrDefault(group.Id);

    private static async Task<Dictionary<string, string>> TeacherNamesAsync(
        IAppDbContext db, IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Teachers.AsNoTracking().Where(t => list.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
    }

    private static string Day(string? iso) => iso is { Length: >= 10 } ? iso[..10] : iso ?? "";
}
