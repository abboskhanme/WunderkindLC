using System.IO;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>SPEC-backlog-2026-10-17 F5 D1: rahbariyat analitikasi — yagona ta'riflardan yig'iladi, faqat superadmin.</summary>
public class BacklogF5Tests
{
    [Fact]
    public void Oy_puli__oquv_tushumidan_vozvrat_ayriladi__kitob_va_boshqa_kirim_faqat_jami_kirimda()
    {
        var m = ExecutiveDashboard.Money(
        [
            ("income", "tuition", 1_000_000m), ("income", "book", 50_000m), ("expense", "refund", 100_000m),
            ("expense", "rent", 300_000m),
        ]);
        Assert.Equal((900_000m, 1_050_000m, 400_000m, 650_000m), (m.Tuition, m.Income, m.Expense, m.Net));
    }

    [Theory]
    [InlineData("2026-10", -5, "2026-05")]
    [InlineData("2026-01", -1, "2025-12")]
    [InlineData("2026-12", 1, "2027-01")]
    public void Oy_surish(string month, int delta, string expected) =>
        Assert.Equal(expected, ExecutiveDashboard.ShiftMonth(month, delta));

    [Fact]
    public async Task Dashboard__qarz_arxivsiz__lid_konversiyasi__6_oylik_trend()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var month = AppClock.Today.ToString("yyyy-MM");
        db.Students.AddRange(
            new Student { FullName = "Qarzdor", Balance = -200_000m },
            new Student { FullName = "Arxivdagi qarzdor", Balance = -900_000m, IsArchived = true },
            new Student { FullName = "Avans", Balance = 50_000m });
        db.Leads.AddRange(
            new Lead { FullName = "L1", CreatedAt = month + "-02T10:00:00", ConvertedStudentId = "s" },
            new Lead { FullName = "L2", CreatedAt = month + "-03T10:00:00" });
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = month + "-05", Direction = "income", Category = "tuition", Amount = 400_000m,
        });
        await db.SaveChangesAsync();

        var d = await ExecutiveDashboard.BuildAsync(db, month);

        Assert.Equal((200_000m, 1), (d.DebtTotal, d.Debtors));
        Assert.Equal(400_000m, d.ThisMonth.Tuition);
        Assert.Equal(ExecutiveDashboard.TrendMonths, d.Trend.Count);
        var last = d.Trend[^1];
        Assert.Equal((month, 2, 1, 400_000m), (last.Month, last.Leads, last.LeadsConverted, last.Tuition));
    }

    [Fact]
    public void Controller_faqat_superadmin_rolida__ruxsat_kaliti_bilan_ochilmaydi()
    {
        // Test loyihasi Server'ga havola qilmaydi — darvoza manba matnidan (HomeEndpointsRbacTests naqshi).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "ExecutiveAnalyticsController.cs"));
        var header = src[..src.IndexOf("public class ExecutiveAnalyticsController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize(Roles = Roles.SuperAdmin)]", header);
        Assert.DoesNotContain("[AdminPerm(", header);
        Assert.DoesNotContain("AllowAnonymous", src);
    }
}
