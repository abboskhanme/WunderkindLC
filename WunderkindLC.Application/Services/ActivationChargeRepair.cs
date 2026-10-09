using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// HISOBSIZ QOLGAN «QO'SHILGAN OY» ni TUZATISH (2026-10-09, bir martalik vosita).
///
/// <para><b>Muammo:</b> Excel importida (yoki eski yaratish yo'lida) «active» bilan qo'shilgan a'zolik
/// o'z aktivlashtirish oyiga HISOB YOZMAS edi — o'quvchi balansi 0 bo'lib qolardi, ya'ni o'qiyotgan oyi
/// tekin edi (mijoz: "aktiv qilib qo'shilganda qarzdorlikni hisoblamay, balansni 0 qilib ochyapti").
/// Endi import shu oyni o'zi hisoblaydi (<c>StudentsController.ChargeJoinMonthAsync</c>); bu vosita esa
/// O'SHA xato bilan bazada qolib ketgan a'zoliklarni topadi va AYNAN «Aktivlashtirish» oynasidagi qisman
/// hisobni (<see cref="TuitionService.ChargeActivationProrateAsync"/> — sanadan qolgan darslar, chegirma
/// bilan) yozadi. Yangi hisob-kitob yo'q.</para>
///
/// <para><b>Qaysi a'zolik nomzod</b> (<see cref="IsCandidate"/>, sof): faol (<c>active</c> + tirik),
/// aktivlashtirish oyi — JORIY yoki O'TGAN oy (<c>TuitionService.EnsureRecentActivationChargeAsync</c>
/// bilan bir xil chegara: eski tarixga orqaga qarz yozilmaydi), o'sha oyda muzlatilmagan, shu oyda
/// yopilgan oldingi davri yo'q (qayta aktivlashtirish — murakkab holat, qo'lda tahrirlanadi); va
/// (o'quvchi, guruh, oy) uchun HISOB QATORI UMUMAN YO'Q — 0 so'mlik yoki qo'lda tahrirlangan qator ham
/// "bor" hisoblanadi va tegilmaydi. Arxivdagi o'quvchi/guruh — tushmaydi.</para>
///
/// <para>⚠️ <b>Avval KO'RIB chiqiladi:</b> <see cref="FindAsync"/> hech narsa yozmaydi; yozish —
/// tanlanganlar uchun <see cref="ApplyOneAsync"/>, har o'quvchi o'z QULFI ostida (to'lov bilan bir navbat),
/// balans qulf ichida qayta o'qiladi, nomzodlik qulf ichida QAYTA tekshiriladi. Ota-onaga avto-xabar
/// YUBORILMAYDI — bu tuzatish, yangi oy hisobi emas.</para>
/// </summary>
public static class ActivationChargeRepair
{
    public sealed record Candidate(
        string StudentId, string StudentName, string GroupId, string GroupName,
        string ActivatedAt, string Month, decimal Amount, decimal Balance);

    /// <summary>Tanlov kaliti (klient tanlagan qatorlar shu bilan qaytadi).</summary>
    public static string Key(string studentId, string groupId, string month) => $"{studentId}|{groupId}|{month}";

    /// <summary>A'zolik nomzodmi (hisob qatori borligidan tashqari) — sof funksiya.</summary>
    /// <param name="floorMonth">Eng eski ruxsat etilgan oy (o'tgan oy), "yyyy-MM".</param>
    /// <param name="currentMonth">Joriy oy, "yyyy-MM".</param>
    public static bool IsCandidate(StudentGroup sg, string floorMonth, string currentMonth)
    {
        if (sg.Status != "active" || !sg.IsActive) return false;
        if (sg.ActivatedAt.Length < 10 || !DateOnly.TryParse(sg.ActivatedAt[..10], out _)) return false;
        var month = sg.ActivatedAt[..7];
        if (string.CompareOrdinal(month, floorMonth) < 0 || string.CompareOrdinal(month, currentMonth) > 0) return false;
        if (TuitionService.ActivationMonthMembership(new[] { sg }, month) is null) return false;
        return !MembershipLifecycle.ClosedPeriodEndsInMonth(sg, month);
    }

    /// <summary>Hisobsiz qolgan aktivlashtirish oylari — HECH NARSA YOZMAYDI.</summary>
    public static async Task<List<Candidate>> FindAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var current = today.ToString("yyyy-MM");
        var floor = today.AddMonths(-1).ToString("yyyy-MM");

        var memberships = (await db.StudentGroups.AsNoTracking()
                .Where(sg => sg.Status == "active" && sg.IsActive)
                .ToListAsync(ct))
            .Where(sg => IsCandidate(sg, floor, current))
            .ToList();
        if (memberships.Count == 0) return [];

        var studentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var months = memberships.Select(m => m.ActivatedAt[..7]).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
            .ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, ct);
        var groups = await db.Classes.AsNoTracking()
            .Where(c => groupIds.Contains(c.Id) && !c.IsArchived)
            .ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct);
        var charged = (await db.MonthlyCharges.AsNoTracking()
                .Where(c => studentIds.Contains(c.StudentId) && c.GroupId != null && months.Contains(c.Month))
                .Select(c => new { c.StudentId, c.GroupId, c.Month })
                .ToListAsync(ct))
            .Select(c => Key(c.StudentId, c.GroupId!, c.Month))
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<Candidate>();
        foreach (var sg in memberships)
        {
            var month = sg.ActivatedAt[..7];
            if (charged.Contains(Key(sg.StudentId, sg.GroupId, month))) continue;
            if (!students.TryGetValue(sg.StudentId, out var s) || !groups.TryGetValue(sg.GroupId, out var cls)) continue;
            // Summa — AYNAN yoziladigan qisman hisob (chegirma ayirilgan); 0 — oyning oxirgi darsidan keyin.
            var amount = await TuitionService.ActivationChargePreviewAsync(db, s, cls, month);
            if (amount is not > 0) continue;
            result.Add(new Candidate(s.Id, s.FullName, cls.Id, cls.Name, sg.ActivatedAt[..10], month, amount.Value, s.Balance));
        }
        return result
            .OrderBy(c => c.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.StudentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Bitta nomzodga hisob yozadi. ⚠️ Chaqiruvchi o'quvchi QULFI ichida bo'lishi kerak
    /// (<c>PaymentIdempotency.LockAsync("student:{id}")</c>) va keyin SaveChanges qiladi.
    /// Qaytaradi: yozilgan EFFEKTIV summa (0 — nomzod emas bo'lib chiqdi, hech narsa yozilmadi).
    /// </summary>
    public static async Task<decimal> ApplyOneAsync(IAppDbContext db, AuditService audit, string studentId,
                                                    string groupId, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var sg = await db.StudentGroups
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.GroupId == groupId && x.IsActive, ct);
        if (sg is null || !IsCandidate(sg, today.AddMonths(-1).ToString("yyyy-MM"), today.ToString("yyyy-MM"))) return 0m;
        var month = sg.ActivatedAt[..7];
        if (await db.MonthlyCharges.AnyAsync(c => c.StudentId == studentId && c.GroupId == groupId && c.Month == month, ct))
            return 0m;
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == studentId, ct);
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == groupId, ct);
        if (s is null || s.IsArchived || cls is null || cls.IsArchived) return 0m;

        await StudentBalanceSync.ReloadAsync(db, s);
        var before = s.Balance;
        await TuitionService.ChargeActivationProrateAsync(db, s, cls, sg.ActivatedAt[..10]);
        var charged = before - s.Balance;
        if (charged > 0)
            audit.Record("Membership", $"{cls.Id}:{s.Id}", "update",
                $"Qo'shilgan oy hisobi tuzatildi ({month}, {sg.ActivatedAt[..10]} sanadan qolgan darslar): "
                + $"{AuditService.Money(charged)} so'm — {s.FullName}, {cls.Name}. Aktiv qilib qo'shilganda bu oy "
                + "hisoblanmay qolgan edi",
                studentId: s.Id);
        return charged;
    }
}
