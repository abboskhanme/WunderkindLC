using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// KASSADAN KASSAGA KO'CHIRISH (<see cref="CashboxTransferService"/>, billing.md). Asosiy xavflar:
/// ko'chirish KIRIM/CHIQIM bo'lib hisobotlarga (P&amp;L, pul oqimi, maosh bazasi) oqib ketishi, kassa
/// qog'ozda MINUSGA tushishi va bekor qilishda qoldiq tiklanmay qolishi.
/// </summary>
public class CashboxTransferTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static readonly string Today = AppClock.Today.ToString("yyyy-MM-dd");

    private static Cashbox Box(string id, string name, bool archived = false) =>
        new() { Id = id, Name = name, IsArchived = archived, CreatedAt = "2026-10-01T10:00:00" };

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    /// <summary>Asosiy kassa (1 000 000 kirim) + kassir kassasi (300 000 kirim, 50 000 chiqim) + arxivdagi kassa (20 000).</summary>
    private static AppDbContext Seed(TestDb db)
    {
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir kassasi"),
            Box("old", "Eski kassa", archived: true));
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Date = "2026-10-01", Direction = "income", Category = "tuition", Amount = 1_000_000m, CashboxId = CashboxService.MainId },
            new FinanceTransaction { Date = "2026-10-01", Direction = "income", Category = "tuition", Amount = 300_000m, CashboxId = "k1" },
            new FinanceTransaction { Date = "2026-10-02", Direction = "expense", Category = "supplies", Amount = 50_000m, CashboxId = "k1" },
            new FinanceTransaction { Date = "2026-09-01", Direction = "income", Category = "other", Amount = 20_000m, CashboxId = "old" });
        ctx.SaveChanges();
        return ctx;
    }

    private static Task<CashboxTransferService.Result> Transfer(AppDbContext ctx, string from, string to, decimal amount,
        string? date = null, string? requestId = null) =>
        CashboxTransferService.CreateAsync(ctx, Audit(ctx),
            new CashboxTransferService.Request(from, to, amount, date, "Kun yakuni", requestId), "sa-1", "Super Admin");

    // ============================ Sof qoidalar ============================

    [Fact]
    public void Tekshiruv__ozi_ozi_ga_arxivdagi_kassaga_nol_summa_kelajak_sana_RAD()
    {
        var a = Box("a", "A");
        var b = Box("b", "B");
        Assert.Contains("o'ziga", CashboxTransferService.Validate(a, a, 10, null, Today, out _));
        Assert.Contains("arxivlangan", CashboxTransferService.Validate(a, Box("x", "X", archived: true), 10, null, Today, out _));
        Assert.Contains("musbat", CashboxTransferService.Validate(a, b, 0, null, Today, out _));
        Assert.Contains("musbat", CashboxTransferService.Validate(a, b, -5, null, Today, out _));
        Assert.Equal("Sana noto'g'ri", CashboxTransferService.Validate(a, b, 10, "06.10.2026", Today, out _));
        Assert.Contains("Kelajak", CashboxTransferService.Validate(a, b, 10, "2999-01-01", Today, out _));
        Assert.Contains("kassadan", CashboxTransferService.Validate(null, b, 10, null, Today, out _));
        Assert.Contains("kassaga", CashboxTransferService.Validate(a, null, 10, null, Today, out _));
    }

    [Fact]
    public void Tekshiruv__ARXIVDAGI_kassadan_chiqarish_MUMKIN__sana_bosh_bolsa_bugun()
    {
        Assert.Null(CashboxTransferService.Validate(Box("x", "X", archived: true), Box("b", "B"), 10, null, Today, out var d));
        Assert.Equal(Today, d);
        Assert.Null(CashboxTransferService.Validate(Box("a", "A"), Box("b", "B"), 10, "2026-01-15", Today, out var past));
        Assert.Equal("2026-01-15", past);
    }

    [Fact]
    public void Minus_qoidasi__qoldiqdan_kop_bolsa_sabab_bilan__teng_bolsa_mumkin()
    {
        var err = CashboxTransferService.OverdraftError("Kassir kassasi", 250_000m, 300_000m);
        Assert.Contains("yetarli mablag' yo'q", err);
        Assert.Contains("250 000", err);
        Assert.Null(CashboxTransferService.OverdraftError("Kassir kassasi", 250_000m, 250_000m));
    }

    // ============================ Baza ============================

    [Fact]
    public async Task Kochirish_ikkala_qoldiqni_ozgartiradi__jami_ozgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);

        var res = await Transfer(ctx, "k1", CashboxService.MainId, 200_000m);

        Assert.Null(res.Error);
        var b = await CashboxService.BalancesAsync(ctx, "2026-10-01", Today);
        Assert.Equal(50_000m, b["k1"].Total);                       // 300 000 − 50 000 − 200 000
        Assert.Equal(1_200_000m, b[CashboxService.MainId].Total);
        Assert.Equal(200_000m, b["k1"].PeriodTransferOut);
        Assert.Equal(200_000m, b[CashboxService.MainId].PeriodTransferIn);
        // Ko'chirish kirim/chiqim EMAS — davr kirim/chiqimi o'zgarmadi.
        Assert.Equal(300_000m, b["k1"].PeriodIncome);
        Assert.Equal(50_000m, b["k1"].PeriodExpense);
        Assert.Equal(1_000_000m, b[CashboxService.MainId].PeriodIncome);
        Assert.Equal(1_270_000m, b.Values.Sum(x => x.Total));       // pul yo'qolmadi, paydo ham bo'lmadi
        Assert.Equal(50_000m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Kochirish_MOLIYA_TRANZAKSIYASI_EMAS__PnL_va_hisobotlarga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);
        var rowsBefore = await ctx.FinanceTransactions.AsNoTracking().ToListAsync();
        var yearBefore = FinanceYearReport.Build(
            rowsBefore.Select(t => new FinanceYearReport.Row(t.Date, t.Direction, t.Category, t.Amount)), 2026);

        Assert.Null((await Transfer(ctx, CashboxService.MainId, "k1", 400_000m)).Error);

        // Moliya jadvaliga HECH NARSA yozilmadi — P&L, pul oqimi, «Kirim chiqim», kassir hisoboti,
        // maosh bazasi va KPI shu jadvaldan o'qiydi.
        var rowsAfter = await ctx.FinanceTransactions.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(rowsBefore.Count, rowsAfter.Count);
        var yearAfter = FinanceYearReport.Build(
            rowsAfter.Select(t => new FinanceYearReport.Row(t.Date, t.Direction, t.Category, t.Amount)), 2026);
        Assert.Equal(yearBefore.IncomeTotal, yearAfter.IncomeTotal);
        Assert.Equal(yearBefore.ExpenseTotal, yearAfter.ExpenseTotal);
        Assert.Single(await ctx.CashboxTransfers.ToListAsync());
    }

    [Fact]
    public async Task Qoldiqdan_kop_kochirib_bolmaydi__hech_narsa_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);

        var res = await Transfer(ctx, "k1", CashboxService.MainId, 250_001m);   // qoldiq 250 000

        Assert.Contains("yetarli mablag' yo'q", res.Error);
        Assert.Empty(await ctx.CashboxTransfers.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await ctx.AuditLogs.ToListAsync());
        Assert.Equal(250_000m, await CashboxService.TotalAsync(ctx, "k1"));
        // Aynan qoldiqcha — mumkin (kassa 0 ga tushadi, minusga emas).
        Assert.Null((await Transfer(ctx, "k1", CashboxService.MainId, 250_000m)).Error);
        Assert.Equal(0m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Arxivdagi_kassaga_kochirib_bolmaydi__undan_chiqarish_mumkin()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);

        Assert.Contains("arxivlangan", (await Transfer(ctx, "k1", "old", 10_000m)).Error);
        Assert.Null((await Transfer(ctx, "old", CashboxService.MainId, 20_000m)).Error);
        Assert.Equal(0m, await CashboxService.TotalAsync(ctx, "old"));
    }

    [Fact]
    public async Task Bekor_qilish_qoldiqlarni_TIKLAYDI__qator_tarixda_qoladi__auditga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);
        var t = (await Transfer(ctx, "k1", CashboxService.MainId, 100_000m)).Transfer!;

        var res = await CashboxTransferService.VoidAsync(ctx, Audit(ctx), t.Id, "Xato kassa", "sa-1", "Super Admin");

        Assert.Null(res.Error);
        Assert.Equal(250_000m, await CashboxService.TotalAsync(ctx, "k1"));
        Assert.Equal(1_000_000m, await CashboxService.TotalAsync(ctx, CashboxService.MainId));
        // Global filtr — hisobdan chiqdi, lekin qator o'chmagan (tarix).
        Assert.Empty(await ctx.CashboxTransfers.ToListAsync());
        var row = await ctx.CashboxTransfers.IgnoreQueryFilters().SingleAsync();
        Assert.True(row.IsVoided);
        Assert.Equal("Xato kassa", row.VoidReason);
        Assert.Equal("Super Admin", row.VoidedByName);

        var audits = await ctx.AuditLogs.Where(a => a.EntityType == AuditService.EntityCashboxTransfer).ToListAsync();
        Assert.Equal(["create", "delete"], audits.Select(a => a.Action).OrderBy(x => x).ToArray());
        Assert.Equal("finance", AuditSections.SectionOf(AuditService.EntityCashboxTransfer));

        // Ikkinchi marta — topilmaydi (qoldiq IKKI marta tiklanmaydi).
        var again = await CashboxTransferService.VoidAsync(ctx, Audit(ctx), t.Id, null, "sa-1", "Super Admin");
        Assert.True(again.NotFound);
        Assert.Equal(250_000m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Bekor_qilish__qabul_qilgan_kassa_pulni_ishlatib_bolgan_bolsa_RAD__minusga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);
        var t = (await Transfer(ctx, CashboxService.MainId, "k1", 100_000m)).Transfer!;
        // k1: 250 000 + 100 000 = 350 000 → 300 000 xarajat qilindi → 50 000 qoldi.
        ctx.FinanceTransactions.Add(new FinanceTransaction
            { Date = Today, Direction = "expense", Category = "rent", Amount = 300_000m, CashboxId = "k1" });
        await ctx.SaveChangesAsync();

        var res = await CashboxTransferService.VoidAsync(ctx, Audit(ctx), t.Id, null, "sa-1", "Super Admin");

        Assert.Contains("allaqachon ishlatilgan", res.Error);
        Assert.False((await ctx.CashboxTransfers.IgnoreQueryFilters().SingleAsync()).IsVoided);
        Assert.Equal(50_000m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Bir_oyna_kaliti_bilan_qayta_yuborilgan_sorov_IKKINCHI_kochirish_yozmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);
        var key = Guid.NewGuid().ToString();

        var first = await Transfer(ctx, CashboxService.MainId, "k1", 10_000m, requestId: key);
        var second = await Transfer(ctx, CashboxService.MainId, "k1", 10_000m, requestId: key);

        Assert.Equal(first.Transfer!.Id, second.Transfer!.Id);
        Assert.Single(await ctx.CashboxTransfers.ToListAsync());
    }

    [Fact]
    public async Task Orqaga_sanali_kochirish_sana_boyicha_davrga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = Seed(db);
        Assert.Null((await Transfer(ctx, CashboxService.MainId, "k1", 5_000m, date: "2026-10-01")).Error);

        var sept = await CashboxService.BalancesAsync(ctx, "2026-09-01", "2026-09-30");
        Assert.Equal(0m, sept["k1"].PeriodTransferIn);
        var oct1 = await CashboxService.BalancesAsync(ctx, "2026-10-01", "2026-10-01");
        Assert.Equal(5_000m, oct1["k1"].PeriodTransferIn);
    }

    // ============================ RBAC (manba matni) ============================

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    [Fact]
    public void Kochirish__yozish_FAQAT_superadmin__tarix_moliya_ruxsati_bilan()
    {
        var src = Source("CashboxTransfersController.cs");
        var header = src[..src.IndexOf("public class CashboxTransfersController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"finance.main\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Tarix — o'qish ham ruxsat bilan (kassa kartalari bilan bir xil).
        Assert.Contains("[AdminPerm(\"finance.main\", ReadRequiresPerm = true)]", src);

        // Har yozish amali HARD rol tekshiruvi bilan boshlanadi (admin ham, kassir ham o'tmaydi).
        Assert.Contains("User.IsInRole(Roles.SuperAdmin)", src);
        var writes = Regex.Matches(src, @"\[Http(Post|Put|Patch|Delete)[^\]]*\]");
        Assert.Equal(2, writes.Count);
        foreach (Match m in writes)
        {
            var body = src[m.Index..];
            body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
            Assert.Contains("DenyUnlessSuperAdmin()", body);
        }
    }
}
