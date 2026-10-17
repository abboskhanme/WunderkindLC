using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// MOLIYA HISOBOTLARI (SPEC-backlog-2026-10-17 F1): «Qarzdorlik tahlili», «Guruhlar va tariflar»,
/// «Hisob-fakturalar». Asos — <see cref="DebtLedger"/>: kassadagi to'lov oynasi qoidasi (har oy mustaqil,
/// avans o'tgan oy qarzini yopmaydi, vozvrat ayriladi).
/// </summary>
public class FinanceReportsTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);   // muddat 10-sana → oktabr 15 kun, sentabr 45 kun

    private static void Seed(AppDbContext ctx)
    {
        ctx.Teachers.Add(new Teacher { Id = "t1", FullName = "Gulasal" });
        ctx.Subjects.Add(new Subject { Id = "c1", Name = "Ingliz tili" });
        ctx.Classes.Add(new Group { Id = "g1", Name = "Elementary", TeacherId = "t1", CourseId = "c1", MonthlyFee = 1_590_000m, Days = [0, 2, 4] });
    }

    private static void Student(AppDbContext ctx, string id, string name, bool archived = false, string activatedAt = "2026-08-01")
    {
        ctx.Students.Add(new Student { Id = id, FullName = name, ParentPhone = "+998901112233", IsArchived = archived });
        ctx.StudentGroups.Add(new StudentGroup { StudentId = id, GroupId = "g1", Status = "active", IsActive = true, ActivatedAt = activatedAt, JoinedAt = "2026-08-01" });
    }

    private static void Charge(AppDbContext ctx, string sid, string month, decimal amount, decimal discount = 0m) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = sid, GroupId = "g1", Month = month, Amount = amount, Discount = discount, Date = month + "-01" });

    private static void Pay(AppDbContext ctx, string sid, string month, decimal amount, string direction = "income", string category = "tuition") =>
        ctx.FinanceTransactions.Add(new FinanceTransaction { StudentId = sid, GroupId = "g1", Month = month, Amount = amount, Direction = direction, Category = category, Date = "2026-10-05" });

    [Fact]
    public async Task Qarzdorlik__kelgusi_oy_avansi_otgan_oy_qarzini_YOPMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "Ali");
        Charge(ctx, "s1", "2026-10", 1_590_000m);
        Charge(ctx, "s1", "2026-11", 1_590_000m);
        Pay(ctx, "s1", "2026-11", 1_590_000m);   // noyabr oldindan to'langan, oktabr — yo'q
        await ctx.SaveChangesAsync();

        var r = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-10", "2026-10"), Today);

        var row = Assert.Single(r.Rows);
        Assert.Equal(1_590_000m, row.Debt);
        Assert.Equal("2026-10", row.OldestMonth);
        Assert.Equal(15, row.AgeDays);
        Assert.Equal("7-29", row.Bucket);
        Assert.Equal("+998901112233", row.Phone);
        Assert.Equal("Ingliz tili", row.CourseName);
        Assert.Equal("Gulasal", row.TeacherName);
        Assert.Equal(1, r.Total.Debtors);
        Assert.Equal(0m, r.Total.CollectionPct);
    }

    [Fact]
    public async Task Qarzdorlik__kelgusi_oy_qarz_hisobotiga_KIRMAYDI_va_vozvrat_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "Ali");
        Charge(ctx, "s1", "2026-10", 1_000_000m);
        Pay(ctx, "s1", "2026-10", 1_000_000m);
        Pay(ctx, "s1", "2026-10", 400_000m, "expense", "refund");   // 400 ming qaytarildi → 400 ming qarz
        Charge(ctx, "s1", "2026-11", 1_000_000m);                    // kelgusi oy — qarz emas
        await ctx.SaveChangesAsync();

        var r = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-10", "2026-12"), Today);

        Assert.Equal(["2026-10"], r.Months);
        var row = Assert.Single(r.Rows);
        Assert.Equal(400_000m, row.Debt);
        Assert.Equal(60m, r.Total.CollectionPct);
        Assert.Equal(600_000m, r.Total.Paid);
    }

    [Fact]
    public async Task Qarzdorlik__arxivdagilar_standart_KIRMAYDI_belgi_bilan_qoshiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "Faol");
        Student(ctx, "s2", "Ketgan", archived: true);
        Charge(ctx, "s1", "2026-10", 1_000_000m);
        Charge(ctx, "s2", "2026-09", 800_000m);
        await ctx.SaveChangesAsync();

        var off = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-09", "2026-10"), Today);
        Assert.Equal(["s1"], off.Rows.Select(r => r.StudentId));
        Assert.Equal(1_000_000m, off.Total.Debt);
        Assert.Equal(800_000m, off.Archived.Debt);   // belgi o'chiq bo'lsa ham ma'lumot uchun

        var on = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-09", "2026-10", IncludeArchived: true), Today);
        Assert.Equal(2, on.Rows.Count);
        Assert.Equal(1_800_000m, on.Total.Debt);
        Assert.True(on.Rows.Single(r => r.StudentId == "s2").IsArchived);
    }

    [Fact]
    public async Task Qarzdorlik__yosh_filtri_va_guruhlar()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "Eski qarz");
        Student(ctx, "s2", "Yangi qarz");
        Charge(ctx, "s1", "2026-09", 1_000_000m);   // 45 kun
        Charge(ctx, "s1", "2026-10", 1_000_000m);
        Charge(ctx, "s2", "2026-10", 1_000_000m, discount: 200_000m);   // 15 kun, sof 800 ming
        Pay(ctx, "s2", "2026-10", 300_000m);
        await ctx.SaveChangesAsync();

        var all = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-09", "2026-10"), Today);
        Assert.Equal(1, all.Buckets.Single(b => b.Key == "30-59").Rows);
        Assert.Equal(1, all.Buckets.Single(b => b.Key == "7-29").Rows);
        Assert.Equal(2_000_000m, all.Rows.Single(r => r.StudentId == "s1").Debt);
        Assert.Equal(500_000m, all.Rows.Single(r => r.StudentId == "s2").Debt);
        Assert.Equal(200_000m, all.Total.Discount);

        var old = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-09", "2026-10", MinAge: 30), Today);
        Assert.Equal(["s1"], old.Rows.Select(r => r.StudentId));
        // Yosh guruhlari filtrdan OLDIN — kartochkalar to'liq manzarani ko'rsatadi.
        Assert.Equal(1, old.Buckets.Single(b => b.Key == "7-29").Rows);
    }

    [Fact]
    public async Task Hisobi_yoq_oyga_tushgan_tolov_jimgina_yoqolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "Ali");
        Charge(ctx, "s1", "2026-10", 1_000_000m);
        Pay(ctx, "s1", "2026-09", 300_000m);   // sentabrda hisob yo'q (masalan sinovdagi avans)
        Student(ctx, "s2", "Vali");
        Charge(ctx, "s2", "2026-09", 20_000m);  // importda sentabr hisobi kichik, to'lov esa katta
        Pay(ctx, "s2", "2026-09", 220_000m);
        await ctx.SaveChangesAsync();

        var r = await DebtReport.BuildAsync(ctx, new DebtReport.Query("2026-09", "2026-10"), Today);
        Assert.Equal(1_000_000m, r.Total.Debt);
        Assert.Equal((2, 500_000m), (r.Unmatched.Count, r.Unmatched.Amount));   // 300 000 hisobsiz + 200 000 ortiqcha

        var inv = await InvoiceReport.BuildAsync(ctx, new InvoiceReport.Query("2026-09", "2026-10"), Today);
        Assert.Equal(500_000m, inv.Unmatched.Amount);
    }

    [Fact]
    public void Qarz_yoshi_guruhlari_va_muddat()
    {
        Assert.Equal(new DateOnly(2026, 2, 10), DebtLedger.DueDate("2026-02", 10));
        Assert.Equal(new DateOnly(2026, 2, 28), DebtLedger.DueDate("2026-02", 31));
        Assert.Equal("notdue", DebtLedger.Bucket(-3));
        Assert.Equal("notdue", DebtLedger.Bucket(0));   // muddat kunining o'zi — hali kechikish emas
        Assert.Equal("0-6", DebtLedger.Bucket(1));
        Assert.Equal("7-29", DebtLedger.Bucket(7));
        Assert.Equal("30-59", DebtLedger.Bucket(30));
        Assert.Equal("60+", DebtLedger.Bucket(60));
        Assert.Equal(100m, DebtLedger.Pct(0m, 0m));
    }

    [Fact]
    public async Task Tariflar__narx_taqsimoti_qisman_oy_va_sust_intizom()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        Student(ctx, "s1", "A");
        Student(ctx, "s2", "B");
        Student(ctx, "s3", "C", activatedAt: "2026-10-15");   // oy o'rtasida — qisman
        Student(ctx, "s4", "D");
        Charge(ctx, "s1", "2026-10", 1_590_000m);
        Charge(ctx, "s2", "2026-10", 1_590_000m);
        Charge(ctx, "s3", "2026-10", 900_000m);
        Charge(ctx, "s4", "2026-10", 1_490_000m);   // qo'lda tuzatilgan shaxsiy narx
        Pay(ctx, "s1", "2026-10", 1_590_000m);
        await ctx.SaveChangesAsync();

        var r = await TariffReport.BuildAsync(ctx, "2026-10", null, Today);

        var g = Assert.Single(r.Groups);
        Assert.Equal(4, g.Students);
        var full = g.Prices.Single(p => p.Price == 1_590_000m);
        Assert.Equal(3, full.Students);
        Assert.Equal(1, full.Partial);
        Assert.Equal(1, g.Prices.Single(p => p.Price == 1_490_000m).Students);
        Assert.Equal(5_570_000m, g.Planned);
        Assert.Equal(1_590_000m, g.Collected);
        Assert.Equal(3_980_000m, g.Debt);
        Assert.True(g.Weak);
        Assert.Equal("Gulasal", g.TeacherName);
        var t = Assert.Single(r.Teachers);
        Assert.Equal(1, t.WeakGroups);
    }

    [Fact]
    public async Task Fakturalar__holat_muddat_va_jamlar_BUTUN_filtrdan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Seed(ctx);
        for (var i = 1; i <= 5; i++) Student(ctx, $"s{i}", $"O'quvchi {i}");
        foreach (var i in Enumerable.Range(1, 5)) Charge(ctx, $"s{i}", "2026-10", 1_000_000m);
        Pay(ctx, "s1", "2026-10", 1_000_000m);
        Pay(ctx, "s2", "2026-10", 250_000m);
        await ctx.SaveChangesAsync();

        var all = await InvoiceReport.BuildAsync(ctx, new InvoiceReport.Query("2026-10", "2026-10", PageSize: 2), Today);
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(1, all.PaidCount);
        Assert.Equal(1, all.PartialCount);
        Assert.Equal(3, all.UnpaidCount);
        Assert.Equal(4, all.OverdueCount);
        Assert.Equal(3_750_000m, all.Totals.Debt);   // sahifa emas, butun filtr
        Assert.Equal("2026-10-10", all.Items[0].DueDate);

        var overdue = await InvoiceReport.BuildAsync(ctx, new InvoiceReport.Query("2026-10", "2026-10", Status: "unpaid", Overdue: true), Today);
        Assert.Equal(3, overdue.TotalCount);
        Assert.All(overdue.Items, i => Assert.Equal(15, i.OverdueDays));

        var paid = await InvoiceReport.BuildAsync(ctx, new InvoiceReport.Query("2026-10", "2026-10", Status: "paid"), Today);
        var p = Assert.Single(paid.Items);
        Assert.Equal(0, p.OverdueDays);
        Assert.Equal(1_000_000m, p.Paid);
    }

    [Fact]
    public void Controller__har_hisobot_oz_kaliti_bilan_va_oqish_ham_darvozalangan()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "FinanceReportsController.cs"));
        Assert.Contains("[AdminPerm(\"finance.debts\", ReadRequiresPerm = true)]\n[Route", src);
        Assert.Contains("[HttpGet(\"debts\")]\n    [AdminPerm(\"finance.debts\", ReadRequiresPerm = true)]", src);
        Assert.Contains("[HttpGet(\"tariffs\")]\n    [AdminPerm(\"finance.tariffs\", ReadRequiresPerm = true)]", src);
        Assert.Contains("[HttpGet(\"invoices\")]\n    [AdminPerm(\"finance.invoices\", ReadRequiresPerm = true)]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.DoesNotContain("HttpPost", src);
    }
}
