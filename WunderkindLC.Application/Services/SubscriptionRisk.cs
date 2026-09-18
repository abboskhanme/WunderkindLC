using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "JORIY OYDA OBUNASI TUGAYDIGANLAR" (edutizim <c>/students/debt-risk</c>) — puli shu oy
/// darslarini QOPLAMAYDIGAN o'quvchilar.
///
/// <para><b>Ustunlar:</b></para>
/// <list type="bullet">
/// <item><b>Jami darslar narxi</b> — o'quvchining joriy oy o'quv to'lovi (barcha guruhlar):
/// shu oyning <see cref="MonthlyCharge"/> qatorlari (effektiv = <c>Amount − Discount</c>, qisman
/// oylar ham) + hali YOZILMAGAN oylik (accrual fon xizmati kutilmoqda).</item>
/// <item><b>Joriy balans</b> — <see cref="Student.Balance"/>, profil bilan bir xil.</item>
/// <item><b>Kutilayotgan balans</b> — <c>Joriy balans − hali yozilmagan qism</c>, ya'ni oyning
/// BUTUN hisobi yozilgandan keyingi balans.</item>
/// </list>
///
/// <para>⚠️ <b>NEGA "Kutilayotgan = Joriy − Jami" EMAS (edutizimdan farqi):</b> edutizim balansni
/// HAR DARSDA yechadi, bizda esa oylik oy BOSHIDA bitta hisob bilan yoziladi
/// (<see cref="TuitionService.AccrueDue"/> — startupda va har 12 soatda). Ya'ni odatda joriy oy
/// hisobi balansga ALLAQACHON kirgan — uni yana ayirsak o'quvchi ikki marta qarzdor bo'lib
/// chiqardi. Shuning uchun faqat YOZILMAGAN qism ayriladi.</para>
///
/// <para>⚠️ <b>Qoidalar QAYTA YOZILMAGAN:</b> yozilmagan oylik AYNAN <see cref="TuitionService.AccrueMonth"/>
/// sharti bilan topiladi (<c>Status=="active" &amp;&amp; IsActive</c> + <see cref="TuitionService.AccruableMonth"/>
/// + (o'quvchi, guruh, oy) qatori yo'q), chegirma — <see cref="DiscountBook.DiscountFor"/>.
/// Qisman oylar (aktivlashtirish/muzlatish) bu yerda HISOBLANMAYDI — ular amal paytida yoziladi va
/// yozilgan qator sifatida keladi. Guruhsiz eski (ClassName) o'quvchilarning yozilmagan oyligi
/// qo'shilmaydi — faqat yozilgan qatori (ular juda eski yozuvlar, accrual ularni ham darhol yozadi).</para>
///
/// <para>Ro'yxatga tushadi: arxivlanmagan, joriy oyda darsi bor (<c>Jami &gt; 0</c>) va
/// <c>Kutilayotgan &lt; 0</c>. Sinovdagi (to'lovsiz) va shu oy o'qimayotgan qarzdorlar KIRMAYDI —
/// ular "obunasi tugayotgan" emas, oddiy qarzdor (O'quvchilar ro'yxati → Balans filtri).</para>
/// </summary>
public static class SubscriptionRisk
{
    public readonly record struct StudentRow(
        string Id, string FullName, string Phone, string ParentPhone, decimal Balance, string MemberState);

    /// <summary>Joriy oyga tegishli bitta summa. <paramref name="Written"/> — <see cref="MonthlyCharge"/>
    /// qatori (balansga KIRGAN); <c>false</c> — hali yozilmagan (accrual kutilmoqda).</summary>
    public readonly record struct MonthCharge(string StudentId, decimal Effective, bool Written);

    /// <summary>Sof hisob (bazasiz) — <c>SubscriptionRiskTests</c> bilan qoplangan.</summary>
    public static SubscriptionRiskReportDto Compute(
        IReadOnlyList<StudentRow> students, IEnumerable<MonthCharge> charges, string month)
    {
        var total = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var pending = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var c in charges)
        {
            if (c.Effective <= 0) continue;
            total[c.StudentId] = total.GetValueOrDefault(c.StudentId) + c.Effective;
            if (!c.Written) pending[c.StudentId] = pending.GetValueOrDefault(c.StudentId) + c.Effective;
        }

        var items = new List<SubscriptionRiskRowDto>();
        foreach (var s in students)
        {
            var lessons = total.GetValueOrDefault(s.Id);
            if (lessons <= 0) continue;
            var expected = s.Balance - pending.GetValueOrDefault(s.Id);
            if (expected >= 0) continue;
            items.Add(new SubscriptionRiskRowDto(
                s.Id, s.FullName, s.Phone, s.ParentPhone, lessons, s.Balance, expected, s.MemberState));
        }
        // Eng katta kamomad tepada (edutizim tartibi — ID — bizda ma'nosiz).
        items = items.OrderBy(r => r.Expected)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ToList();

        return new SubscriptionRiskReportDto(
            month,
            items.Sum(r => r.LessonsTotal),
            items.Sum(r => r.Balance),
            items.Sum(r => r.Expected),
            items);
    }

    /// <summary>
    /// Hali YOZILMAGAN joriy oy oyligi — <see cref="TuitionService.AccrueMonth"/> ning a'zolik
    /// shoxobchasi bilan AYNAN bir xil shart. <paramref name="written"/> — shu oyda mavjud
    /// (o'quvchi, guruh) juftliklari.
    /// </summary>
    public static IEnumerable<MonthCharge> Pending(
        IEnumerable<StudentGroup> memberships,
        IReadOnlySet<(string StudentId, string? GroupId)> written,
        IReadOnlyDictionary<string, decimal> feesById,
        DiscountBook book,
        string month)
    {
        foreach (var m in memberships)
        {
            if (m.Status != "active" || !m.IsActive) continue;
            if (!TuitionService.AccruableMonth(m, month)) continue;
            if (written.Contains((m.StudentId, (string?)m.GroupId))) continue;
            var fee = feesById.GetValueOrDefault(m.GroupId);
            if (fee <= 0) continue;
            var effective = fee - book.DiscountFor(m.StudentId, fee, month, m.GroupId);
            yield return new MonthCharge(m.StudentId, effective, Written: false);
        }
    }

    /// <summary>Bazadan yuklab, <see cref="Compute"/> ni chaqiradi. Faqat o'qish.</summary>
    /// <param name="month">"yyyy-MM" — odatda <see cref="TuitionService.CurrentMonth"/>.</param>
    public static async Task<SubscriptionRiskReportDto> BuildAsync(
        IAppDbContext db, string month, CancellationToken ct = default)
    {
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone, s.Balance })
            .ToListAsync(ct);
        var liveIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var memberships = (await db.StudentGroups.AsNoTracking().ToListAsync(ct))
            .Where(m => liveIds.Contains(m.StudentId)).ToList();
        var states = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => MembershipLifecycle.MemberState(g), StringComparer.Ordinal);

        var monthRows = await db.MonthlyCharges.AsNoTracking().Where(c => c.Month == month)
            .Select(c => new { c.StudentId, c.GroupId, c.Amount, c.Discount })
            .ToListAsync(ct);
        var written = monthRows.Select(c => (c.StudentId, c.GroupId))
            .ToHashSet();

        var feesById = await db.Classes.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.MonthlyFee, ct);
        var book = await DiscountBook.LoadAsync(db, memberships
            .Where(m => m.Status == "active" && m.IsActive).Select(m => m.StudentId));

        var charges = monthRows
            .Where(c => liveIds.Contains(c.StudentId))
            .Select(c => new MonthCharge(c.StudentId, Math.Max(0m, c.Amount - c.Discount), Written: true))
            .Concat(Pending(memberships, written, feesById, book, month))
            .ToList();

        var rows = students.Select(s => new StudentRow(
                s.Id, s.FullName, s.Phone ?? "", s.ParentPhone ?? "", s.Balance,
                states.GetValueOrDefault(s.Id, "")))
            .ToList();
        return Compute(rows, charges, month);
    }
}
