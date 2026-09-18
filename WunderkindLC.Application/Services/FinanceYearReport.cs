namespace WunderkindLC.Application.Services;

/// <summary>
/// YILLIK MOLIYA JADVALI — edutizimdagi ikkita sahifa AYNAN shu ma'lumotdan quriladi:
/// «Moliya hisobotlari (P&amp;L)» va «Pul oqimi hisoboti» (`docs/edutizim/INVENTORY.md` §7).
/// Ikkalasi ham "kategoriya × oy" jadvali, farqi faqat QATORLAR tartibi va jamlarda.
///
/// <para>Sof funksiya: tranzaksiya qatorlari beriladi, natija qaytadi (bazaga tegmaydi) —
/// shuning uchun testlari oddiy (<c>FinanceYearReportTests</c>).</para>
///
/// <para>⚠️ Bu YANGI hisob-kitob EMAS: summalar <c>FinanceTransaction</c> qatorlarining oddiy
/// yig'indisi (oylik hisob, chegirma, maosh qoidalariga TEGMAYDI — `.claude/rules/billing.md`).
/// Ya'ni jadvaldagi son "Moliya → Amallar" dagi qatorlar yig'indisi bilan bir xil chiqadi.</para>
/// </summary>
public static class FinanceYearReport
{
    /// <summary>Bitta tranzaksiya (hisobga kerak bo'lgan qismi).</summary>
    /// <param name="Date">"yyyy-MM-dd" (buzuq/bo'sh sana — qator TASHLANADI).</param>
    /// <param name="Direction">"income" | "expense".</param>
    public readonly record struct Row(string Date, string Direction, string Category, decimal Amount);

    /// <summary>Bitta qator: kategoriya va uning 12 oylik summasi (yanvar → dekabr).</summary>
    public sealed record Line(string Category, decimal[] Months)
    {
        public decimal Total => Months.Sum();
    }

    /// <summary>
    /// Yillik natija. <paramref name="Opening"/> — har oyning BOSHLANG'ICH balansi
    /// (shu yil boshigacha bo'lgan sof + shu yil ichida oldingi oylarning sofi).
    /// </summary>
    public sealed record Result(
        int Year,
        decimal[] Opening,
        decimal[] IncomeTotal,
        decimal[] ExpenseTotal,
        decimal[] Net,
        List<Line> Income,
        List<Line> Expense);

    private static decimal[] Empty() => new decimal[12];

    /// <summary>Sana yilga mos kelsa oy indeksini (0–11) qaytaradi, aks holda -1.</summary>
    public static int MonthIndex(string date, int year)
    {
        // "yyyy-MM-dd" — qo'lda kesamiz: `DateOnly.TryParse` madaniyatga bog'liq bo'lib qolmasin
        // va bazadagi buzuq qatorlar (bo'sh sana) jimgina 1-yanvarga tushmasin.
        if (date.Length < 7) return -1;
        if (!int.TryParse(date.AsSpan(0, 4), out var y) || y != year) return -1;
        if (!int.TryParse(date.AsSpan(5, 2), out var m) || m < 1 || m > 12) return -1;
        return m - 1;
    }

    /// <summary>
    /// Yillik jadvalni quradi. <paramref name="carryOver"/> — shu yil boshiga qadar to'plangan
    /// sof (kirim − chiqim); "Boshlang'ich balans" qatori shundan boshlanadi.
    /// </summary>
    public static Result Build(IEnumerable<Row> rows, int year, decimal carryOver = 0)
    {
        var income = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        var expense = new Dictionary<string, decimal[]>(StringComparer.Ordinal);
        var incomeTotal = Empty();
        var expenseTotal = Empty();

        foreach (var r in rows)
        {
            var i = MonthIndex(r.Date ?? "", year);
            if (i < 0) continue;
            var expenseRow = string.Equals(r.Direction, "expense", StringComparison.Ordinal);
            var map = expenseRow ? expense : income;
            var cat = string.IsNullOrWhiteSpace(r.Category) ? "other" : r.Category;
            if (!map.TryGetValue(cat, out var months)) map[cat] = months = Empty();
            months[i] += r.Amount;
            (expenseRow ? expenseTotal : incomeTotal)[i] += r.Amount;
        }

        var net = Empty();
        var opening = Empty();
        var running = carryOver;
        for (var i = 0; i < 12; i++)
        {
            opening[i] = running;
            net[i] = incomeTotal[i] - expenseTotal[i];
            running += net[i];
        }

        // Tartib: yil bo'yicha KATTAsidan kichigiga — jadvalda eng muhim qator tepada bo'lsin.
        static List<Line> Sorted(Dictionary<string, decimal[]> map) =>
            map.Select(kv => new Line(kv.Key, kv.Value))
               .OrderByDescending(l => l.Total)
               .ThenBy(l => l.Category, StringComparer.Ordinal)
               .ToList();

        return new Result(year, opening, incomeTotal, expenseTotal, net, Sorted(income), Sorted(expense));
    }
}
