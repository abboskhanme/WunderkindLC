using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-23 mijoz shikoyati ("qarzdorlar to'lasa yana" noto'g'ri): to'lov oynasi eng eski qarzdor oyni tanlab
/// qo'yardi, kassir 225 000 kiritib oyni qoldirsa butun to'lov o'sha oyga teglanardi.
/// (1) <see cref="PaymentMonthSplit"/> — qabulda ortig'i keyingi oylarga; (2) <see cref="PaymentMonthRepair"/> —
/// bazada qolganlarni superadmin tuzatadi. Sanalar <see cref="AppClock.Today"/> ga NISBATAN.
/// </summary>
public class PaymentMonthSplitTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private const decimal Fee = 300_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private static (Student S, Group G) Setup(AppDbContext ctx)
    {
        var course = new Subject { Name = "Ingliz tili", Price = Fee, LessonPrice = 25_000m };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, CourseId = course.Id, Days = [0, 1, 2, 3, 4, 5, 6] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-3)}-01", ClassName = g.Name };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = $"{M(-3)}-01",
        });
        return (s, g);
    }

    private static Task<PaymentIntakeResult> Pay(AppDbContext ctx, Student s, string groupId, string month, decimal amount,
                                                 string? requestId = null, string? receiptNo = "KV777") =>
        PaymentIntake.AddAsync(ctx, new AuditService(ctx, new NoHttpContext()), new MessagingStack().Auto, s,
            new PaymentRequest(amount, month, groupId, Method: "cash", ReceiptNo: receiptNo,
                RequestId: requestId ?? Guid.NewGuid().ToString()),
            createdBy: "Kassir", createdById: "kassir-1");

    // ============================ Sof taqsimot ============================

    private static readonly List<(string, decimal)> Rows =
        [("2026-09", 20_000m), ("2026-10", 225_000m), ("2026-11", 225_000m)];

    [Fact]
    public void Qoldiqqa_SIGSA_bitta_qism_tanlangan_oyda() =>
        Assert.Equal([new PaymentMonthSplit.Part("2026-09", 20_000m)], PaymentMonthSplit.Allocate(Rows, "2026-09", 20_000m));

    [Fact]
    public void Qoldiqdan_OSHSA_ortigi_KEYINGI_oyga() =>
        Assert.Equal(
            [new PaymentMonthSplit.Part("2026-09", 20_000m), new PaymentMonthSplit.Part("2026-10", 205_000m)],
            PaymentMonthSplit.Allocate(Rows, "2026-09", 225_000m));

    [Fact]
    public void Hammasidan_ortgani_OXIRGI_qatorda_qoladi() =>
        Assert.Equal(
            [
                new PaymentMonthSplit.Part("2026-09", 20_000m), new PaymentMonthSplit.Part("2026-10", 225_000m),
                new PaymentMonthSplit.Part("2026-11", 255_000m),
            ],
            PaymentMonthSplit.Allocate(Rows, "2026-09", 500_000m));

    [Fact]
    public void Oradagi_TOLANGAN_oy_otkazib_yuboriladi() =>
        Assert.Equal(
            [new PaymentMonthSplit.Part("2026-09", 20_000m), new PaymentMonthSplit.Part("2026-11", 100_000m)],
            PaymentMonthSplit.Allocate([("2026-09", 20_000m), ("2026-10", 0m), ("2026-11", 225_000m)], "2026-09", 120_000m));

    [Fact]
    public void TOLANGAN_oy_ATAYIN_tanlansa_yoki_royxatda_yoq_bolsa_bolinmaydi()
    {
        Assert.Equal([new PaymentMonthSplit.Part("2026-09", 225_000m)],
            PaymentMonthSplit.Allocate([("2026-09", 0m), ("2026-10", 225_000m)], "2026-09", 225_000m));
        Assert.Equal([new PaymentMonthSplit.Part("2027-05", 225_000m)], PaymentMonthSplit.Allocate(Rows, "2027-05", 225_000m));
        Assert.Equal([new PaymentMonthSplit.Part("2026-10", 1m)], PaymentMonthSplit.Allocate([], "2026-10", 1m));
    }

    [Fact]
    public void Orqaga_ketmaydi__tanlangan_oydan_oldingi_qarz_tegilmaydi() =>
        Assert.Equal(
            [new PaymentMonthSplit.Part("2026-10", 225_000m), new PaymentMonthSplit.Part("2026-11", 25_000m)],
            PaymentMonthSplit.Allocate(Rows, "2026-10", 250_000m));

    // ============================ Qabul (PaymentIntake) ============================

    [Fact]
    public async Task Tolov_qoldiqdan_OSHSA_BOLINGAN_OILA_yoziladi__ildiz_tanlangan_oyda()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, g.Id, M(-1), 400_000m);

        Assert.Null(res.Error);
        Assert.Equal([new PaymentMonthSplit.Part(M(-1), Fee), new PaymentMonthSplit.Part(M(0), 100_000m)], res.Parts);
        var txs = await ctx.FinanceTransactions.AsNoTracking().ToListAsync();
        var root = txs.Single(t => t.SplitFromId == null);
        var child = txs.Single(t => t.SplitFromId != null);
        Assert.Equal(res.TxId, root.Id);
        Assert.Equal((M(-1), Fee), (root.Month, root.Amount));
        Assert.Equal((M(0), 100_000m, root.Id), (child.Month, child.Amount, child.SplitFromId));
        // Jismonan bitta to'lov — kvitansiya, kassir, kassa, usul bir xil.
        Assert.Equal((root.ReceiptNo, root.CreatedById, root.CashboxId, root.Method, root.GroupId),
            (child.ReceiptNo, child.CreatedById, child.CashboxId, child.Method, child.GroupId));

        // Per-guruh hisob (to'lov oynasi) — o'tgan oy to'langan, joriy oyda qolgan 200 000.
        var sg = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        var ledger = await StudentGroupLedger.BuildAsync(ctx, await ctx.Students.AsNoTracking().SingleAsync(), g, sg);
        Assert.Equal("paid", ledger.Months.Single(m => m.Month == M(-1)).Status);
        Assert.Equal(200_000m, ledger.Months.Single(m => m.Month == M(0)).Remaining);
        // Balans: ikki oy hisobi (avans qoidasi bilan ochilgan) − to'lov.
        Assert.Equal(400_000m - 2 * Fee, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
        Assert.Contains("oylarga taqsimlandi", (await ctx.AuditLogs.AsNoTracking().SingleAsync()).Summary);
    }

    [Fact]
    public async Task Qoldiqqa_SIGADIGAN_tolov_avvalgidek_BITTA_qator()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, g.Id, M(-1), Fee);

        Assert.Null(res.Error);
        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal((M(-1), Fee, (string?)null), (tx.Month, tx.Amount, tx.SplitFromId));
    }

    [Fact]
    public async Task Taqsimlangan_tolov_6_soniyada_QAYTA_kelsa_dublikat_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        // Kvitansiyasiz (band kvitansiya 409 ni avvalroq qaytaradi) va boshqa oyna kaliti — oila summasi bo'yicha.
        var first = await Pay(ctx, s, g.Id, M(-1), 400_000m, requestId: "oyna-1", receiptNo: null);
        var second = await Pay(ctx, s, g.Id, M(-1), 400_000m, requestId: "oyna-2", receiptNo: null);

        Assert.True(second.Idempotent);
        Assert.Equal(first.TxId, second.TxId);
        Assert.Equal(2, await ctx.FinanceTransactions.CountAsync());
    }

    // ============================ Tuzatish rejasi (sof) ============================

    private static PaymentMonthRepair.Payment P(string id, string month, decimal amount, bool refund = false) =>
        new(id, month, amount, $"{month}-15", DateTime.UtcNow, refund);

    private static Dictionary<string, decimal> D(params (string M, decimal V)[] xs) => xs.ToDictionary(x => x.M, x => x.V);

    [Fact]
    public void Reja__hisobi_0_oyga_yozilgan_tolov_TOLIQ_keyingi_oyga() =>
        Assert.Equal([new PaymentMonthRepair.Move("t1", "2026-09", "2026-10", 225_000m)],
            PaymentMonthRepair.Plan(D(("2026-09", 0m), ("2026-10", 225_000m)), [P("t1", "2026-09", 225_000m)], D(),
                "2026-09", "2026-10").Moves);

    [Fact]
    public void Reja__faqat_ORTIGI_kochadi() =>
        Assert.Equal([new PaymentMonthRepair.Move("t1", "2026-09", "2026-10", 205_000m)],
            PaymentMonthRepair.Plan(D(("2026-09", 20_000m), ("2026-10", 225_000m)), [P("t1", "2026-09", 225_000m)], D(),
                "2026-09", "2026-10").Moves);

    [Fact]
    public void Reja__bir_tolov_bir_nechta_KAM_tolangan_oyga_eskisidan_boshlab() =>
        Assert.Equal(
            [
                new PaymentMonthRepair.Move("t1", "2026-08", "2026-09", 225_000m),
                new PaymentMonthRepair.Move("t1", "2026-08", "2026-10", 100_000m),
            ],
            PaymentMonthRepair.Plan(D(("2026-08", 0m), ("2026-09", 225_000m), ("2026-10", 225_000m)),
                [P("t1", "2026-08", 325_000m)], D(), "2026-08", "2026-10").Moves);

    [Fact]
    public void Reja__ORQAGA_KELAJAKKA_va_OYNADAN_tashqariga_kochmaydi()
    {
        // Orqaga: oktabrga ortiqcha, sentabr kam — tegilmaydi.
        Assert.Empty(PaymentMonthRepair.Plan(D(("2026-09", 225_000m), ("2026-10", 0m)), [P("t1", "2026-10", 225_000m)], D(),
            "2026-09", "2026-10").Moves);
        // Kelajak: noyabr joriy oydan keyin.
        Assert.Empty(PaymentMonthRepair.Plan(D(("2026-10", 0m), ("2026-11", 225_000m)), [P("t1", "2026-10", 225_000m)], D(),
            "2026-09", "2026-10").Moves);
        // Oynadan eski oy (maoshi yig'ilgan pul bazasida berilgan) — tegilmaydi.
        Assert.Empty(PaymentMonthRepair.Plan(D(("2026-08", 0m), ("2026-09", 225_000m)), [P("t1", "2026-08", 225_000m)], D(),
            "2026-09", "2026-10").Moves);
    }

    [Fact]
    public void Reja__VOZVRATI_bor_tolov_kochirilmaydi_izoh_bilan()
    {
        var plan = PaymentMonthRepair.Plan(D(("2026-09", 0m), ("2026-10", 225_000m)),
            [P("t1", "2026-09", 225_000m, refund: true)], D(), "2026-09", "2026-10");
        Assert.Empty(plan.Moves);
        Assert.Single(plan.Notes);
    }

    [Fact]
    public void Reja__VOZVRAT_ortiqchani_kamaytiradi() =>
        // Sentabrga 225 000 to'langan, 100 000 qaytarilgan (vozvrat boshqa to'lovdan) → ortiqcha 125 000.
        Assert.Equal([new PaymentMonthRepair.Move("t1", "2026-09", "2026-10", 125_000m)],
            PaymentMonthRepair.Plan(D(("2026-09", 0m), ("2026-10", 225_000m)), [P("t1", "2026-09", 225_000m)],
                D(("2026-09", 100_000m)), "2026-09", "2026-10").Moves);

    [Theory]
    [InlineData("2026-10", "2026-09", "2026-09")]
    [InlineData("2026-10", null, "2026-09")]
    [InlineData("2026-12", "2026-09", "2026-10")]
    [InlineData("2026-10", "2026-01", "2026-08")]
    [InlineData("2027-01", "", "2026-12")]
    public void Oyna_chegarasi(string current, string? chargedFrom, string expected) =>
        Assert.Equal(expected, PaymentMonthRepair.FloorMonth(current, chargedFrom));

    // ============================ Tuzatish (bazada) ============================

    private static async Task<(Student S, Group G, FinanceTransaction Tx)> SeedMisTagged(AppDbContext ctx, decimal lastMonthCharge)
    {
        var (s, g) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = lastMonthCharge, Locked = true });
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = Fee });
        var tx = new FinanceTransaction
        {
            Date = $"{M(0)}-12", Direction = "income", Category = "tuition", Amount = Fee, StudentId = s.Id,
            GroupId = g.Id, Month = M(-1), Note = "O'quvchi to'lovi", ReceiptNo = "KV1", CreatedById = "kassir-1",
            Method = "cash", CashboxId = "main",
        };
        ctx.FinanceTransactions.Add(tx);
        s.Balance = Fee - lastMonthCharge - Fee;
        await ctx.SaveChangesAsync();
        return (s, g, tx);
    }

    [Fact]
    public async Task Tuzatish__hisobi_0_oydagi_tolov_TEGI_almashadi__pul_va_balans_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, tx) = await SeedMisTagged(ctx, 0m);

        var found = await PaymentMonthRepair.FindAsync(ctx);
        var c = Assert.Single(found);
        Assert.Equal(PaymentMonthRepair.Key(s.Id, g.Id), c.Key);
        Assert.Equal([new PaymentMonthRepair.MoveView(M(-1), M(0), Fee)], c.Moves);

        var moved = await PaymentMonthRepair.ApplyOneAsync(ctx, new AuditService(ctx, new NoHttpContext()), s.Id, g.Id);
        await ctx.SaveChangesAsync();

        Assert.Equal(Fee, moved);
        var after = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal((tx.Id, M(0), Fee), (after.Id, after.Month, after.Amount));
        Assert.Equal(0m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
        Assert.Contains("To'lov oyi tuzatildi", (await ctx.AuditLogs.AsNoTracking().SingleAsync()).Summary);
        Assert.Empty(await PaymentMonthRepair.FindAsync(ctx));
    }

    [Fact]
    public async Task Tuzatish__qisman_ortiqcha_BOLINADI__oila_bilan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, tx) = await SeedMisTagged(ctx, 20_000m);

        await PaymentMonthRepair.ApplyOneAsync(ctx, new AuditService(ctx, new NoHttpContext()), s.Id, g.Id);
        await ctx.SaveChangesAsync();

        var txs = await ctx.FinanceTransactions.AsNoTracking().ToListAsync();
        var root = txs.Single(t => t.Id == tx.Id);
        var child = txs.Single(t => t.Id != tx.Id);
        Assert.Equal((M(-1), 20_000m), (root.Month, root.Amount));
        Assert.Equal((M(0), Fee - 20_000m, tx.Id, "KV1", "main"),
            (child.Month, child.Amount, child.SplitFromId, child.ReceiptNo, child.CashboxId));
        Assert.Equal(-20_000m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);

        var ledger = await StudentGroupLedger.BuildAsync(ctx, await ctx.Students.AsNoTracking().SingleAsync(), g,
            await ctx.StudentGroups.AsNoTracking().SingleAsync());
        Assert.Equal("paid", ledger.Months.Single(m => m.Month == M(-1)).Status);
        Assert.Equal(20_000m, ledger.Months.Single(m => m.Month == M(0)).Remaining);
    }

    // ============================ Ruxsat ============================

    [Fact]
    public void Tuzatish_endpointlari_FAQAT_superadmin()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "BillingRepairController.cs"));
        var header = src[..src.IndexOf("public class BillingRepairController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize(Roles = Roles.SuperAdmin)]", header);
        Assert.DoesNotContain("[AdminPerm(", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("[HttpGet(\"payment-months\")]", src);
        Assert.Contains("[HttpPost(\"payment-months\")]", src);
    }
}
