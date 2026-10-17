using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// SPEC-backlog-2026-10-17 F3: kassa turi va boshlang'ich qoldiq, chiqim tasdig'i (faqat katta summalar, kiritgan o'zi
/// tasdiqlay olmaydi), xodimlar maoshi reestri (fiks + KPI, avans / yakuniy).
/// </summary>
public class FinanceOpsTests
{
    // ---------- K2: chiqim tasdig'i ----------

    [Theory]
    [InlineData(1_000_000, 1_500_000, "expense", false, true)]    // katta chiqim — tasdiq
    [InlineData(1_000_000, 1_000_000, "expense", false, false)]   // chegaraga TENG — darhol
    [InlineData(1_000_000, 5_000_000, "income", false, false)]    // kirim — hech qachon
    [InlineData(1_000_000, 5_000_000, "expense", true, false)]    // superadminning o'zi — darhol
    [InlineData(0, 5_000_000, "expense", false, false)]           // chegara 0 — o'chiq
    public void Tasdiq_faqat_chegaradan_katta_chiqimga(int threshold, int amount, string dir, bool super, bool expected) =>
        Assert.Equal(expected, ExpenseApproval.NeedsApproval(threshold, amount, dir, super));

    [Fact]
    public void Kiritgan_ozi_tasdiqlay_olmaydi_va_korib_chiqilgani_qayta_tasdiqlanmaydi()
    {
        var r = new ExpenseRequest { CreatedById = "u1", Status = ExpenseApproval.Pending };
        Assert.NotNull(ExpenseApproval.DecisionBlock(r, "u1"));
        Assert.Null(ExpenseApproval.DecisionBlock(r, "boss"));
        r.Status = ExpenseApproval.Approved;
        Assert.NotNull(ExpenseApproval.DecisionBlock(r, "boss"));
    }

    [Fact]
    public void Tasdiqlangan_sorovdan_tranzaksiya_aynan_shu_maydonlar_bilan()
    {
        var r = new ExpenseRequest
        {
            Date = "2026-10-17", Category = "salary", Amount = 3_000_000m, StaffUserId = "u5", Month = "2026-10",
            SalaryPayKind = "advance", CreatedBy = "Kassir", CreatedById = "u1", Method = "cash",
        };
        var tx = ExpenseApproval.ToTransaction(r, "main");
        Assert.Equal(("expense", "salary", 3_000_000m, "u5", "advance", "main", "u1", r.Id),
            (tx.Direction, tx.Category, tx.Amount, tx.StaffUserId, tx.SalaryPayKind, tx.CashboxId, tx.CreatedById, tx.ExpenseRequestId));
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("Advance", true, "advance")]
    [InlineData("final", true, "final")]
    [InlineData("bonus", false, "bonus")]
    public void Maosh_tolovi_turi(string? raw, bool ok, string? kind)
    {
        Assert.Equal(ok, ExpenseApproval.TryPayKind(raw, out var k));
        Assert.Equal(kind, k);
    }

    // ---------- K1: kassa ----------

    [Fact]
    public async Task Boshlangich_qoldiq_sanadan_oldingi_harakatlarni_ozida_saqlaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Cashboxes.Add(new Cashbox { Id = "b1", Name = "Bank", Kind = "bank", OpeningBalance = 10_000_000m, OpeningDate = "2026-10-01" });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { CashboxId = "b1", Direction = "income", Category = "other", Amount = 999m, Date = "2026-09-30" },   // ichida
            new FinanceTransaction { CashboxId = "b1", Direction = "income", Category = "other", Amount = 500_000m, Date = "2026-10-01" },
            new FinanceTransaction { CashboxId = "b1", Direction = "expense", Category = "rent", Amount = 200_000m, Date = "2026-10-05" });
        await ctx.SaveChangesAsync();

        Assert.Equal(10_300_000m, (await CashboxService.BalancesAsync(ctx, null, null))["b1"].Total);
        Assert.Equal(10_300_000m, await CashboxService.TotalAsync(ctx, "b1"));

        var st = await CashboxService.StatementAsync(ctx, "b1", "2026-10-03", "2026-10-31");
        Assert.NotNull(st);
        Assert.Equal((10_500_000m, 0m, 200_000m, 10_300_000m), (st!.Opening, st.Income, st.Expense, st.Closing));
        Assert.Single(st.Rows);

        var early = await CashboxService.StatementAsync(ctx, "b1", "2026-09-01", "2026-10-31");
        Assert.Equal("2026-10-01", early!.From);   // boshlang'ich sanadan oldin — sanadan boshlanadi
        Assert.Equal(10_000_000m, early.Opening);
    }

    [Fact]
    public void Kassa_turi() =>
        Assert.Equal(("cash", "bank", "cash", (string?)null), (
            CashboxService.NormalizeKind(null), CashboxService.NormalizeKind("Bank"),
            CashboxService.NormalizeKind("cash"), CashboxService.NormalizeKind("card")));

    // ---------- K3: maosh reestri ----------

    [Fact]
    public async Task Reestr__xodim_fiks_va_KPI__avans_yakuniy_qoldiq()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.AddRange(
            new AppUser { Id = "s1", FullName = "Kassir", Role = Roles.Staff, Email = "s1" },
            new AppUser { Id = "s2", FullName = "Operator", Role = Roles.Staff, Email = "s2" },
            new AppUser { Id = "a1", FullName = "Admin", Role = Roles.Admin, Email = "a1" });
        ctx.StaffSalaries.AddRange(
            new StaffSalary { UserId = "s1", EffectiveFrom = "2026-09", Amount = 3_000_000m },
            new StaffSalary { UserId = "s1", EffectiveFrom = "2026-11", Amount = 3_500_000m });   // kelajak — oktabrga ta'sir qilmaydi
        ctx.KpiProfiles.Add(new KpiProfile { UserId = "s2", RoleCode = "call_operator", IsActive = true });
        ctx.KpiProfileSalaries.Add(new KpiProfileSalary { UserId = "s2", EffectiveFrom = "2026-09", BaseSalary = 2_000_000m });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Direction = "expense", Category = "salary", StaffUserId = "s1", Month = "2026-10", Amount = 1_000_000m, SalaryPayKind = "advance", Date = "2026-10-15" },
            new FinanceTransaction { Direction = "expense", Category = "salary", StaffUserId = "s1", Month = "2026-10", Amount = 2_000_000m, SalaryPayKind = "final", Date = "2026-11-03" });
        await ctx.SaveChangesAsync();

        var r = await PayrollRegister.BuildAsync(ctx, "2026-10");

        var kassir = r.Staff.Single(x => x.Id == "s1");
        Assert.Equal(("fixed", 3_000_000m, 1_000_000m, 2_000_000m, 0m, "paid"),
            (kassir.Source, kassir.Accrued, kassir.Advance, kassir.Final, kassir.Remaining, kassir.Status));
        var op = r.Staff.Single(x => x.Id == "s2");
        Assert.Equal(("kpi-pending", 2_000_000m, "unpaid"), (op.Source, op.Accrued, op.Status));
        Assert.DoesNotContain(r.Staff, x => x.Id == "a1");   // maoshi yo'q admin ro'yxatni to'ldirmaydi

        // KPI oyi tasdiqlangach — KPI natijasi (oklad + bonus).
        ctx.KpiMonthResults.Add(new KpiMonthResult { UserId = "s2", Month = "2026-10", Status = "confirmed", Salary = 2_600_000m, UserName = "Operator" });
        await ctx.SaveChangesAsync();
        var r2 = await PayrollRegister.BuildAsync(ctx, "2026-10");
        Assert.Equal(("kpi", 2_600_000m), (r2.Staff.Single(x => x.Id == "s2").Source, r2.Staff.Single(x => x.Id == "s2").Accrued));
    }

    [Fact]
    public void Controllerlar_ruxsat_bilan()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        string Src(string f) => File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", f));
        var exp = Src("ExpenseRequestsController.cs");
        Assert.Contains("[AdminPerm(\"finance.main\", \"expenseApproval\", ReadRequiresPerm = true)]\n[Route", exp);
        Assert.Contains("IsSuperAdminOrGranted(User, ExpenseApproval.PermKey)", exp);
        Assert.Contains("User.IsInRole(Roles.SuperAdmin)", exp);   // chegara — faqat superadmin
        Assert.DoesNotContain("AllowAnonymous", exp);
        var pay = Src("PayrollController.cs");
        Assert.Contains("[AdminPerm(\"finance.main\", ReadRequiresPerm = true)]\n[Route", pay);
        Assert.Contains("[HttpGet(\"{id}/statement\")]\n    [AdminPerm(\"finance.main\", ReadRequiresPerm = true)]", Src("CashboxesController.cs"));
    }
}
