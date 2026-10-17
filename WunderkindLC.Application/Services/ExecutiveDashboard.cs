using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

public record ExecMoneyDto(decimal Tuition, decimal Income, decimal Expense, decimal Net);
public record ExecCashboxDto(string Id, string Name, string Kind, decimal Balance, bool IsArchived);
/// <summary>Bir oy: yangi (birinchi o'quv to'lovi) va ketgan (aktivdan / yangidan) o'quvchilar, kelgan va aylangan lidlar.</summary>
public record ExecMonthDto(string Month, int NewPaying, int LeftActive, int LeftNew, int Leads, int LeadsConverted, decimal Tuition);
public record ExecutiveDashboardDto(
    string Month, ExecMoneyDto ThisMonth, ExecMoneyDto PrevMonth,
    decimal CashTotal, List<ExecCashboxDto> Cashboxes,
    decimal DebtTotal, int Debtors, int ActiveStudents,
    List<ExecMonthDto> Trend);

/// <summary>
/// RAHBARIYAT ANALITIKASI (SPEC-backlog-2026-10-17 F5 D1, TZ 3.4): oylik tushum, kassa qoldiqlari, faol qarz, o'quvchilar
/// oqimi va lid konversiyasi — BITTA ekranda, har ko'rsatkich o'z hisobotiga havola bilan. FAQAT o'qiydi va YANGI
/// formula yozmaydi — har raqam mavjud yagona ta'rifdan:
/// <list type="bullet">
///   <item>tushum/xarajat — <c>FinanceTransaction</c> SANASI bo'yicha (bekor qilinganlar global filtr bilan tushadi;
///     «Kirim chiqim» hisoboti bilan bir xil); o'quv tushumi — <c>income/tuition</c> MINUS vozvrat (<c>refund</c>);</item>
///   <item>kassalar — <see cref="CashboxService.BalancesAsync"/> (boshlang'ich qoldiq + ko'chirishlar bilan);</item>
///   <item>qarz — arxivlanmagan o'quvchilarning umumiy <c>Student.Balance</c> manfiy qismi (bosh sahifa qoidasi —
///     billing.md «PER-GURUH BALANS»: markaz bo'yicha qarzdorlik ATAYIN umumiy balansda);</item>
///   <item>oqim — <see cref="DashboardSummary.FirstPaymentStudentsInMonth"/> (yangi) va
///     <see cref="DashboardSummary.LeftStudentsInMonth"/> (ketdi — course-analytics.md §1 qoidasi);</item>
///   <item>lidlar — shu oyda KELGAN lidlar va ulardan hozir o'quvchiga aylanganlar (<c>LeadAnalytics.InRange</c> naqshi).</item>
/// </list>
/// </summary>
public static class ExecutiveDashboard
{
    public const int TrendMonths = 6;

    public static string ShiftMonth(string month, int delta)
    {
        var d = new DateOnly(int.Parse(month[..4]), int.Parse(month[5..7]), 1).AddMonths(delta);
        return d.ToString("yyyy-MM");
    }

    /// <summary>Oy pul ko'rsatkichlari (sof funksiya): qatorlar — (yo'nalish, toifa, summa).</summary>
    public static ExecMoneyDto Money(IEnumerable<(string Direction, string Category, decimal Amount)> rows)
    {
        decimal tuition = 0, income = 0, expense = 0;
        foreach (var r in rows)
        {
            if (r.Direction == "income")
            {
                income += r.Amount;
                if (r.Category == "tuition") tuition += r.Amount;
            }
            else
            {
                expense += r.Amount;
                if (r.Category == "refund") tuition -= r.Amount;   // vozvrat o'quv tushumidan ayriladi
            }
        }
        return new ExecMoneyDto(tuition, income, expense, income - expense);
    }

    public static async Task<ExecutiveDashboardDto> BuildAsync(IAppDbContext db, string month, CancellationToken ct = default)
    {
        var first = ShiftMonth(month, -(TrendMonths - 1));
        var prev = ShiftMonth(month, -1);

        var tx = await db.FinanceTransactions.AsNoTracking()
            .Where(t => string.Compare(t.Date, first + "-01") >= 0 && string.Compare(t.Date, month + "-31") <= 0)
            .Select(t => new { t.Date, t.Direction, t.Category, t.Amount })
            .ToListAsync(ct);
        ExecMoneyDto MoneyOf(string m) =>
            Money(tx.Where(t => t.Date.StartsWith(m, StringComparison.Ordinal)).Select(t => (t.Direction, t.Category, t.Amount)));

        // Kassalar (butun tarix — qoldiq).
        var balances = await CashboxService.BalancesAsync(db, null, null, ct);
        var boxes = await db.Cashboxes.AsNoTracking()
            .Select(b => new { b.Id, b.Name, b.Kind, b.IsArchived }).ToListAsync(ct);
        var cashboxes = boxes
            .Select(b => new ExecCashboxDto(b.Id, b.Name, b.Kind, balances.GetValueOrDefault(b.Id)?.Total ?? 0m, b.IsArchived))
            .Where(b => !b.IsArchived || b.Balance != 0m)
            .OrderBy(b => b.IsArchived).ThenByDescending(b => b.Balance)
            .ToList();

        // Qarz va faol o'quvchilar.
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.Balance }).ToListAsync(ct);
        var debt = students.Where(s => s.Balance < 0).ToList();
        var activeIds = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.IsActive && sg.Status == "active")
            .Select(sg => sg.StudentId).Distinct().ToListAsync(ct);
        var live = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        // Oqim — bosh sahifa bilan BIR XIL funksiyalar.
        var memberships = await db.StudentGroups.AsNoTracking().ToListAsync(ct);
        var courseByGroup = (await db.Classes.AsNoTracking().Select(g => new { g.Id, g.CourseId }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => g.CourseId ?? "", StringComparer.Ordinal);
        var payments = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && t.Direction == "income" && t.Category == "tuition")
                .Select(t => new { StudentId = t.StudentId!, t.Date }).ToListAsync(ct))
            .Select(t => new DashboardSummary.PaymentRow(t.StudentId, t.Date ?? "")).ToList();

        var leads = await db.Leads.AsNoTracking()
            .Where(l => string.Compare(l.CreatedAt, first) >= 0)
            .Select(l => new { l.CreatedAt, Converted = l.ConvertedStudentId != null }).ToListAsync(ct);

        var trend = new List<ExecMonthDto>();
        for (var i = 0; i < TrendMonths; i++)
        {
            var m = ShiftMonth(first, i);
            var (fromNew, fromActive) = DashboardSummary.LeftStudentsInMonth(memberships, courseByGroup, m);
            var monthLeads = leads.Where(l => (l.CreatedAt ?? "").StartsWith(m, StringComparison.Ordinal)).ToList();
            trend.Add(new ExecMonthDto(m,
                DashboardSummary.FirstPaymentStudentsInMonth(payments, m).Count,
                fromActive.Count, fromNew.Count,
                monthLeads.Count, monthLeads.Count(l => l.Converted),
                MoneyOf(m).Tuition));
        }

        return new ExecutiveDashboardDto(
            month, MoneyOf(month), MoneyOf(prev),
            cashboxes.Sum(b => b.Balance), cashboxes,
            -debt.Sum(s => s.Balance), debt.Count, activeIds.Count(live.Contains),
            trend);
    }
}
