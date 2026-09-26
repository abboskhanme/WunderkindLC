using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BEKOR QILINGAN TRANZAKSIYA (<see cref="FinanceTransaction.IsVoided"/>) — qator bazada QOLADI, lekin
/// hech bir hisob-kitobga kirmaydi. Buni `AppDbContext` dagi GLOBAL FILTR ta'minlaydi; bu testlar
/// filtr olib tashlansa yoki chetlab o'tilsa qizaradi.
/// </summary>
public class FinanceVoidTests
{
    private static (Teacher T, Group G, Student S) Setup(AppDbContext ctx)
    {
        var t = new Teacher { FullName = "Foizli", SalaryMode = "percent", SalaryPercent = 50 };
        var g = new Group
        {
            Name = "A", MonthlyFee = 600_000m, TeacherId = t.Id,
            TeacherSalaryMode = "percent", TeacherSalaryPercent = 50, Days = new List<int> { 0, 2, 4 },
        };
        var s = new Student { FullName = "Ali", EnrollmentDate = "2026-01-01" };
        ctx.Teachers.Add(t);
        ctx.Classes.Add(g);
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        return (t, g, s);
    }

    private static FinanceTransaction Pay(Student s, Group g, decimal amount, string? receipt = null, bool voided = false) => new()
    {
        Date = "2026-07-05", Direction = "income", Category = "tuition", Amount = amount,
        StudentId = s.Id, GroupId = g.Id, Month = "2026-07", Method = "cash", ReceiptNo = receipt,
        IsVoided = voided, VoidedAt = voided ? "2026-07-06T10:00:00" : null, VoidedBy = voided ? "Admin" : null,
    };

    [Fact]
    public async Task Bekor_qilingan_tolov_BAZADA_qoladi_lekin_oddiy_sorovga_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g, s) = Setup(ctx);
        ctx.FinanceTransactions.AddRange(Pay(s, g, 200_000m), Pay(s, g, 300_000m, voided: true));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.FinanceTransactions.CountAsync());
        Assert.Equal(200_000m, (await ctx.FinanceTransactions.ToListAsync()).Sum(t => t.Amount));
        // O'chirilmagan — ataylab so'ralganda ko'rinadi (Moliya ro'yxati shunday oladi).
        Assert.Equal(2, await ctx.FinanceTransactions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Bekor_qilingan_tolov_OQITUVCHI_MAOSHIGA_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.FinanceTransactions.AddRange(Pay(s, g, 200_000m), Pay(s, g, 300_000m, voided: true));
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, "2026-07", "2026-07");

        Assert.Equal(100_000m, dto.Months.Single().BaseExpected);   // faqat 200 000 × 50%
    }

    [Fact]
    public async Task Bekor_qilingan_tolovning_KVITANSIYA_raqami_qayta_ishlatilishi_mumkin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g, s) = Setup(ctx);
        ctx.FinanceTransactions.Add(Pay(s, g, 300_000m, receipt: "KV100", voided: true));
        await ctx.SaveChangesAsync();

        Assert.Null(await ReceiptGuard.FindDuplicateAsync(ctx, "KV100"));
    }

    [Fact]
    public void Bekor_qilish_endpointi_qatorni_OCHIRMAYDI()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "FinanceController.cs"));

        Assert.DoesNotContain("FinanceTransactions.Remove(", src);
        Assert.DoesNotContain("FinanceTransactions.RemoveRange(", src);
        Assert.Contains("tx.IsVoided = true;", src);
    }

    [Fact]
    public void Global_filtr_AppDbContext_da_turibdi()
    {
        using var db = TestDb.Sqlite();
        var filter = db.Context.Model.FindEntityType(typeof(FinanceTransaction))!.GetQueryFilter();
        Assert.NotNull(filter);
        Assert.Contains("IsVoided", filter!.ToString());
    }
}
