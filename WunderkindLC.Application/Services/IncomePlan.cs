using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "TUSHUM REJASI" (edutizim <c>/analytics/income-plan</c>) — joriy oyda markaz QANCHA pul
/// kutayotgani: reja, o'tgan oydan ko'chgan qarz/avans, shu oyda to'langan va qolgan kutilayotgan.
///
/// <para><b>Qatorlar</b> (edutizimdagi tartib):</para>
/// <list type="number">
/// <item><b>Reja</b> — shu oyning o'quv to'lovi (yozilgan <see cref="Domain.MonthlyCharge"/> qatorlari
/// + hali yozilmagan oylik, chegirma ayrilgan). Manba <see cref="SubscriptionRisk"/> bilan AYNAN
/// BITTA — ikki sahifada ikki xil son chiqmasin.</item>
/// <item><b>Eski oydan qarzdor bo'lib o'tganlar</b> — oy BOSHIDAGI manfiy balanslar (manfiy son).</item>
/// <item><b>Eski oydan to'lab o'tganlar</b> — oy boshidagi musbat balanslar (avans).</item>
/// <item><b>Shu oyda to'langan</b> — shu oy uchun kelgan o'quv to'lovlari.</item>
/// <item><b>Qolgan kutilayotgan tushum</b> = reja + qarz − avans − to'langan.</item>
/// </list>
///
/// <para>⚠️ <b>Oy boshidagi balans QAYTA hisoblanmaydi</b>: u joriy balansdan tiklanadi —
/// <c>balans − shu oyda to'langan + shu oyda YOZILGAN hisob</c>. Yozilmagan (accrual kutayotgan)
/// oylik balansga hali tegmagan, shuning uchun qo'shilmaydi (<c>SubscriptionRisk</c> dagi bilan
/// bir xil mulohaza).</para>
/// </summary>
public static class IncomePlan
{
    /// <summary>Shu oy uchun kelgan o'quv to'lovi (vozvrat — manfiy summa bilan).</summary>
    public readonly record struct Payment(string StudentId, decimal Amount);

    /// <summary>Jadvaldagi bitta qator. <paramref name="Students"/> yo'q = "o'quvchi soni" bo'sh.</summary>
    public sealed record Row(string Label, int? Students, decimal Amount);

    public sealed record Result(string Month, List<Row> Rows);

    public const string LabelDebt = "Eski oydan qarzdor bo'lib o'tgan o'quvchilar summasi";
    public const string LabelCredit = "Eski oydan o'quvchilar to'lab o'tgan summa";
    public const string LabelPaid = "Shu oyda to'lagan summa";
    public const string LabelRemaining = "Qolgan kutilayotgan tushum";

    /// <summary>Sof hisob (bazasiz) — <c>IncomePlanTests</c> bilan qoplangan.</summary>
    public static Result Compute(
        IReadOnlyList<(string Id, decimal Balance)> students,
        IEnumerable<SubscriptionRisk.MonthCharge> charges,
        IEnumerable<Payment> payments,
        string month,
        string planLabel)
    {
        var planByStudent = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var writtenByStudent = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var c in charges)
        {
            if (c.Effective <= 0) continue;
            planByStudent[c.StudentId] = planByStudent.GetValueOrDefault(c.StudentId) + c.Effective;
            if (c.Written) writtenByStudent[c.StudentId] = writtenByStudent.GetValueOrDefault(c.StudentId) + c.Effective;
        }

        var paidByStudent = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var p in payments)
            paidByStudent[p.StudentId] = paidByStudent.GetValueOrDefault(p.StudentId) + p.Amount;

        decimal plan = 0, debt = 0, credit = 0, paid = 0;
        int planCount = 0, debtCount = 0, creditCount = 0, paidCount = 0;

        foreach (var (id, balance) in students)
        {
            var monthPlan = planByStudent.GetValueOrDefault(id);
            if (monthPlan > 0)
            {
                plan += monthPlan;
                planCount++;
            }

            var paidNow = paidByStudent.GetValueOrDefault(id);
            if (paidNow != 0)
            {
                paid += paidNow;
                paidCount++;
            }

            // Oy BOSHIDAGI balans (yozilgan hisob balansga kirgan, to'lov ham).
            var opening = balance - paidNow + writtenByStudent.GetValueOrDefault(id);
            if (opening < 0)
            {
                debt += opening;
                debtCount++;
            }
            else if (opening > 0)
            {
                credit += opening;
                creditCount++;
            }
        }

        var remaining = plan + (-debt) - credit - paid;

        return new Result(month, new List<Row>
        {
            new(planLabel, planCount, plan),
            new(LabelDebt, debtCount, debt),
            new(LabelCredit, creditCount, credit),
            new(LabelPaid, paidCount, paid),
            new(LabelRemaining, null, remaining),
        });
    }

    /// <summary>Bazadan yuklab hisoblaydi (faqat o'qish). <paramref name="month"/> — "yyyy-MM".</summary>
    public static async Task<Result> BuildAsync(
        IAppDbContext db, string month, string planLabel, CancellationToken ct = default)
    {
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.Balance }).ToListAsync(ct);
        var liveIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var memberships = (await db.StudentGroups.AsNoTracking().ToListAsync(ct))
            .Where(m => liveIds.Contains(m.StudentId)).ToList();

        var monthRows = await db.MonthlyCharges.AsNoTracking().Where(c => c.Month == month)
            .Select(c => new { c.StudentId, c.GroupId, c.Amount, c.Discount }).ToListAsync(ct);
        var written = monthRows.Select(c => (c.StudentId, c.GroupId)).ToHashSet();

        var feesById = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.MonthlyFee, ct);
        var book = await DiscountBook.LoadAsync(db, memberships
            .Where(m => m.Status == "active" && m.IsActive).Select(m => m.StudentId));

        var charges = monthRows
            .Where(c => liveIds.Contains(c.StudentId))
            .Select(c => new SubscriptionRisk.MonthCharge(c.StudentId, Math.Max(0m, c.Amount - c.Discount), Written: true))
            .Concat(SubscriptionRisk.Pending(memberships, written, feesById, book, month))
            .ToList();

        // To'lov QAYSI OYGA — `Month`, sana EMAS (`.claude/rules/billing.md`). Eski (Month'siz)
        // yozuvlarda sana bo'yicha olinadi, aks holda ular umuman tushib qolardi.
        var from = month + "-01";
        var to = month + "-31";
        var payments = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && t.Category == "tuition"
                            && (t.Month == month
                                || ((t.Month == null || t.Month == "")
                                    && t.Date.CompareTo(from) >= 0 && t.Date.CompareTo(to) <= 0)))
                .Select(t => new { t.StudentId, t.Direction, t.Amount })
                .ToListAsync(ct))
            .Where(t => liveIds.Contains(t.StudentId!))
            // Vozvrat (chiqim) — to'langan summadan AYIRILADI.
            .Select(t => new Payment(t.StudentId!, t.Direction == "expense" ? -t.Amount : t.Amount))
            .ToList();

        return Compute(
            students.Select(s => (s.Id, s.Balance)).ToList(), charges, payments, month, planLabel);
    }
}
