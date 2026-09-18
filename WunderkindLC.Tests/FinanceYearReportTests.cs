using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// YILLIK MOLIYA JADVALI (P&L va Pul oqimi sahifalari) — `FinanceYearReport` sof funksiyasi.
/// Testlar aynan jadvalda ko'rinadigan narsani qulflaydi: oy ustunlari, jamlar, boshlang'ich
/// balansning oydan oyga o'tishi va buzuq sanalarning TASHLANISHI.
/// </summary>
public class FinanceYearReportTests
{
    private static FinanceYearReport.Row In(string date, string cat, decimal amount) => new(date, "income", cat, amount);
    private static FinanceYearReport.Row Out(string date, string cat, decimal amount) => new(date, "expense", cat, amount);

    [Fact]
    public void Summalar_OY_ustunlariga_tushadi()
    {
        var r = FinanceYearReport.Build(new[]
        {
            In("2026-01-10", "tuition", 100),
            In("2026-01-20", "tuition", 50),
            In("2026-03-01", "other", 30),
            Out("2026-01-15", "rent", 40),
        }, 2026);

        Assert.Equal(150, r.Income.Single(l => l.Category == "tuition").Months[0]);
        Assert.Equal(30, r.Income.Single(l => l.Category == "other").Months[2]);
        Assert.Equal(40, r.Expense.Single(l => l.Category == "rent").Months[0]);
        Assert.Equal(150, r.IncomeTotal[0]);
        Assert.Equal(110, r.Net[0]);        // 150 kirim − 40 chiqim
        Assert.Equal(30, r.Net[2]);
    }

    [Fact]
    public void Boshlangich_balans_oydan_oyga_OTADI_va_otgan_yildan_boshlanadi()
    {
        var r = FinanceYearReport.Build(new[] { In("2026-01-05", "tuition", 200) }, 2026, carryOver: 500);

        Assert.Equal(500, r.Opening[0]);
        Assert.Equal(700, r.Opening[1]); // 500 + yanvar sofi (200)
        Assert.Equal(700, r.Opening[11]);
    }

    [Fact]
    public void BOSHQA_yil_va_buzuq_sana_HISOBGA_kirmaydi()
    {
        var r = FinanceYearReport.Build(new[]
        {
            In("2025-12-31", "tuition", 999),
            In("", "tuition", 999),
            In("2026-13-01", "tuition", 999),
            In("2026-02-02", "tuition", 10),
        }, 2026);

        Assert.Equal(10, r.IncomeTotal.Sum());
        Assert.Single(r.Income);
    }

    [Fact]
    public void Kategoriyasiz_qator_other_ga_tushadi_va_tartib_KATTAsidan()
    {
        var r = FinanceYearReport.Build(new[]
        {
            In("2026-01-01", "", 5),
            In("2026-01-01", "tuition", 100),
        }, 2026);

        Assert.Equal("tuition", r.Income[0].Category); // eng kattasi tepada
        Assert.Equal("other", r.Income[1].Category);
        Assert.Equal(5, r.Income[1].Total);
    }
}
