using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: moliya vositalari — `finance_summary`, `finance_transactions`, `finance_debtors`, `student_ledger`,
/// `cashboxes`, `teacher_salaries`, `discounts_report`. Raqamlar panel bilan bir xil mantiqdan, ruxsat va
/// maxfiylik tekshiriladi. Oylar JORIY sanaga nisbatan (M — joriy oy, P — o'tgan oy).
/// </summary>
public class McpFinanceToolsTests
{
    private static readonly string M = AppClock.Today.ToString("yyyy-MM");
    private static readonly string P = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Subject { Id = "c2", Name = "Matematika" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova", SalaryMode = "percent", SalaryPercent = 40 },
            new Teacher { Id = "t2", FullName = "Bekzod Aliyev", SalaryMode = "fixed", Salary = 3_000_000 },
            new Teacher { Id = "t3", FullName = "Eski O'qituvchi", IsArchived = true },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", MonthlyFee = 400_000 },
            new Group { Id = "g2", Name = "Math B1", CourseId = "c2", TeacherId = "t2", MonthlyFee = 300_000 });

        db.Students.AddRange(
            new Student
            {
                Id = "s1", FullName = "Ali Valiyev", Phone = "+998901112233", ParentPhone = "+998935556677",
                Balance = -500_000, BirthCertificateUrl = "/uploads/maxfiy-surat.jpg",
            },
            new Student { Id = "s2", FullName = "Bobur Aliyev", Balance = -300_000 },
            new Student { Id = "s3", FullName = "Charos Tosheva", Balance = -100_000 },
            new Student { Id = "s4", FullName = "Eldor Ketgan", Balance = -900_000, IsArchived = true },
            new Student { Id = "s5", FullName = "Farrux Avansli", Balance = 50_000 },
            new Student { Id = "s6", FullName = "Gulnora Saidova", Balance = -200_000 },
            new Student { Id = "s7", FullName = "Hulkar Nurmatova", Balance = 0 });

        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = $"{P}-01", ActivatedAt = $"{P}-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g2", Status = "active", JoinedAt = $"{M}-01", ActivatedAt = $"{M}-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "trial", JoinedAt = $"{M}-01" },
            new StudentGroup
            {
                StudentId = "s4", GroupId = "g1", Status = "active", IsActive = false,
                JoinedAt = $"{P}-01", ActivatedAt = $"{P}-01", LeftAt = $"{P}-28",
            },
            new StudentGroup
            {
                StudentId = "s6", GroupId = "g2", Status = "active", IsActive = false,
                JoinedAt = $"{P}-01", ActivatedAt = $"{P}-01", LeftAt = $"{M}-05",
            },
            new StudentGroup { StudentId = "s7", GroupId = "g1", Status = "active", JoinedAt = $"{M}-01", ActivatedAt = $"{M}-01" });

        db.MonthlyCharges.AddRange(
            new MonthlyCharge { StudentId = "s1", GroupId = "g1", Month = P, Amount = 400_000, Locked = true },
            new MonthlyCharge { StudentId = "s1", GroupId = "g1", Month = M, Amount = 400_000 },
            new MonthlyCharge { StudentId = "s2", GroupId = "g2", Month = M, Amount = 300_000 },
            new MonthlyCharge { StudentId = "s4", GroupId = "g1", Month = P, Amount = 900_000 },
            new MonthlyCharge { StudentId = "s6", GroupId = "g2", Month = P, Amount = 300_000 },
            new MonthlyCharge { StudentId = "s6", GroupId = "g2", Month = M, Amount = 300_000, Discount = 100_000 },
            new MonthlyCharge { StudentId = "s7", GroupId = "g1", Month = M, Amount = 400_000, Discount = 400_000 });

        db.StudentDiscounts.AddRange(
            new StudentDiscount
            {
                StudentId = "s6", CourseId = "c2", CourseName = "Matematika", Amount = 100_000, Reason = "Ijtimoiy",
                StartMonth = M, CreatedAt = $"{M}-01T10:00:00",
            },
            new StudentDiscount
            {
                StudentId = "s7", GroupId = "g1", GroupName = "English A1", Pct = 100, Reason = "Xodim farzandi",
                StartMonth = M, CreatedAt = $"{M}-01T11:00:00",
            },
            new StudentDiscount { StudentId = "s1", Pct = 10, Reason = "Eski", Status = StudentDiscount.StatusCancelled },
            new StudentDiscount { StudentId = "s4", Pct = 20, Reason = "Ijtimoiy" });

        db.Cashboxes.AddRange(
            new Cashbox { Id = "main", Name = "Asosiy kassa" },
            new Cashbox { Id = "k2", Name = "Filial kassa", ResponsibleUserId = "u-kassir", ResponsibleName = "Kassir Olim", CreatedAt = "2026-01-01" },
            new Cashbox { Id = "k3", Name = "Eski kassa", IsArchived = true, CreatedAt = "2025-01-01" });
        db.CashboxTransfers.AddRange(
            new CashboxTransfer { FromCashboxId = "main", ToCashboxId = "k2", Amount = 100_000, Date = $"{M}-08" },
            new CashboxTransfer { FromCashboxId = "k3", ToCashboxId = "main", Amount = 30_000, Date = $"{P}-15", IsVoided = true });

        db.TransactionTypes.AddRange(
            new TransactionType { Id = "tt1", Name = "O'quvchi to'ladi", Kind = "kirim", Direction = "income", BaseCategory = "tuition" },
            new TransactionType { Id = "tt2", Name = "Arenda", Kind = "chiqim", Direction = "expense", BaseCategory = "rent" });

        db.FinanceTransactions.AddRange(
            Tx("tx1", $"{M}-02", "income", "tuition", 300_000, "cash", "main", student: "s1", group: "g1", month: P,
                type: "tt1", note: "O'tgan oy uchun", receipt: "KV000123"),
            Tx("tx2", $"{M}-03", "income", "book", 50_000, "card", "k2", note: "Kitob sotuvi"),
            Tx("tx3", $"{M}-03", "income", "other", 20_000, "bank", "main", note: "Homiy"),
            Tx("tx4", $"{M}-04", "income", "tuition", 100_000, "card", "k2", student: "s5"),
            Tx("tx5", $"{M}-05", "expense", "refund", 50_000, "cash", "k2", student: "s5", refundOf: "tx4"),
            Tx("tx6", $"{M}-05", "expense", "salary", 1_000_000, "cash", "main", teacher: "t1", month: P),
            Tx("tx7", $"{M}-06", "expense", "salary", 500_000, "cash", "main", teacher: "t2"),
            Tx("tx8", $"{M}-06", "expense", "rent", 2_000_000, "bank", "main", type: "tt2"),
            Tx("tx9", $"{M}-02", "income", "tuition", 999_000, "cash", "main", student: "s2", group: "g2", month: M,
                voided: true),
            Tx("tx10", $"{P}-10", "income", "other", 700_000, "cash", "main", note: "Fond"),
            Tx("tx11", $"{M}-07", "expense", "salary", 800_000, "cash", "main", teacher: "t1", month: M),
            Tx("tx13", $"{P}-01", "income", "other", 30_000, "cash", "k3"),
            Tx("tx14", $"{P}-20", "expense", "salary", 200_000, "cash", "main", teacher: "t3", month: P),
            Tx("tx15", $"{P}-05", "income", "tuition", 300_000, "cash", "main", student: "s6", group: "g2", month: P));

        db.Users.Add(new AppUser { Id = "u-kassir", FullName = "Kassir Olim", Role = Roles.Staff, Email = "kassir.login", PasswordHash = "XESH" });
        db.SaveChanges();
    }

    private static FinanceTransaction Tx(
        string id, string date, string direction, string category, decimal amount, string? method, string? cashbox,
        string? student = null, string? group = null, string? teacher = null, string? month = null, string? type = null,
        string? note = null, string? receipt = null, string? refundOf = null, bool voided = false) => new()
    {
        Id = id, Date = date, Direction = direction, Category = category, Amount = amount, Method = method,
        CashboxId = cashbox, StudentId = student, GroupId = group, TeacherId = teacher, Month = month, TypeId = type,
        Note = note, ReceiptNo = receipt, RefundOfId = refundOf, CreatedBy = "Kassir Olim", CreatedById = "u-kassir",
        IsVoided = voided, VoidedAt = voided ? $"{M}-02T12:00:00" : null, VoidedBy = voided ? "Admin" : null,
        VoidReason = voided ? "Xato kiritilgan" : null,
    };

    private static List<string?> Ids(JsonElement json, string prop = "id", string list = "items") =>
        json.GetProperty(list).EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    private static JsonElement ById(JsonElement json, string id, string prop = "id", string list = "items") =>
        json.GetProperty(list).EnumerateArray().Single(i => i.GetProperty(prop).GetString() == id);

    private static string LastDay(string month)
    {
        var d = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd");
        return d.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
    }

    // ------------------------------------------------------------------ finance_summary

    [Fact]
    public async Task Summary_joriy_oy_kirim_chiqim_toifa_usul_va_qarz_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceSummary());

        Assert.Equal($"{M}-01", json.GetProperty("from").GetString());
        Assert.Equal(470_000, json.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(4_350_000, json.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(-3_880_000, json.GetProperty("net").GetDecimal());
        Assert.Equal(400_000, json.GetProperty("tuitionIncome").GetDecimal());
        Assert.Equal(70_000, json.GetProperty("otherIncome").GetDecimal());
        Assert.Equal(9, json.GetProperty("transactionCount").GetInt32());   // bekor qilingan (tx9) va o'tgan oy kirmaydi

        var inc = json.GetProperty("incomeByCategory");
        Assert.Equal("tuition", inc[0].GetProperty("category").GetString());
        Assert.Equal("O'quvchi to'lovi", inc[0].GetProperty("label").GetString());
        Assert.Equal(400_000, inc[0].GetProperty("amount").GetDecimal());
        var exp = json.GetProperty("expenseByCategory").EnumerateArray().ToList();
        Assert.Equal("salary", exp[0].GetProperty("category").GetString());
        Assert.Equal(2_300_000, exp[0].GetProperty("amount").GetDecimal());
        Assert.Equal("Vozvrat", exp.Single(e => e.GetProperty("category").GetString() == "refund").GetProperty("label").GetString());

        var methods = json.GetProperty("byMethod").EnumerateArray().ToList();
        var cash = methods.Single(m => m.GetProperty("method").GetString() == "cash");
        Assert.Equal(300_000, cash.GetProperty("income").GetDecimal());
        Assert.Equal(2_350_000, cash.GetProperty("expense").GetDecimal());
        Assert.Equal(150_000, methods.Single(m => m.GetProperty("method").GetString() == "card").GetProperty("income").GetDecimal());
        Assert.Equal("Bank orqali", methods.Single(m => m.GetProperty("method").GetString() == "bank").GetProperty("label").GetString());

        var types = json.GetProperty("byType").EnumerateArray().ToList();
        Assert.Equal(2_000_000, types.Single(x => x.GetProperty("name").GetString() == "Arenda").GetProperty("amount").GetDecimal());
        Assert.Equal(300_000, types.Single(x => x.GetProperty("name").GetString() == "O'quvchi to'ladi").GetProperty("amount").GetDecimal());

        Assert.Equal(1, json.GetProperty("voided").GetProperty("count").GetInt32());
        Assert.Equal(999_000, json.GetProperty("voided").GetProperty("amount").GetDecimal());
        var rec = json.GetProperty("receivables");
        Assert.Equal(2_000_000, rec.GetProperty("studentDebt").GetDecimal());   // arxivdagi s4 ham
        Assert.Equal(900_000, rec.GetProperty("archivedDebt").GetDecimal());
        Assert.Equal(50_000, rec.GetProperty("studentAdvance").GetDecimal());

        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Summary_tanlangan_davr_va_notogri_davr()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.Staff("finance.main"))
            .FinanceSummary($"{P}-01", LastDay(P)));
        Assert.Equal(1_030_000, json.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(200_000, json.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(P, json.GetProperty("byMonth")[0].GetProperty("month").GetString());

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceSummary($"{M}-10", $"{M}-01"));
        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceSummary("2024-01-01", "2025-06-01"));
    }

    // ------------------------------------------------------------------ finance_transactions

    [Fact]
    public async Task Jurnal_eng_yangisi_tepada_nomlar_va_vozvrat_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceTransactions();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(9, json.GetProperty("total").GetInt32());
        Assert.Equal("tx11", json.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.DoesNotContain("tx9", Ids(json));
        Assert.Equal(470_000, json.GetProperty("totals").GetProperty("income").GetDecimal());
        Assert.Equal(4_350_000, json.GetProperty("totals").GetProperty("expense").GetDecimal());

        var tx1 = ById(json, "tx1");
        Assert.Equal("O'quvchi to'lovi", tx1.GetProperty("categoryLabel").GetString());
        Assert.Equal("O'quvchi to'ladi", tx1.GetProperty("typeName").GetString());
        Assert.Equal("Ali Valiyev", tx1.GetProperty("student").GetProperty("name").GetString());
        Assert.Equal("English A1", tx1.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Asosiy kassa", tx1.GetProperty("cashbox").GetProperty("name").GetString());
        Assert.Equal("Kassir Olim", tx1.GetProperty("createdBy").GetString());
        Assert.Equal("KV000123", tx1.GetProperty("receiptNo").GetString());
        Assert.False(tx1.GetProperty("voided").GetBoolean());
        Assert.Equal(50_000, ById(json, "tx4").GetProperty("refunded").GetDecimal());
        Assert.Equal("Vozvrat", ById(json, "tx5").GetProperty("categoryLabel").GetString());
        Assert.Equal("Dilnoza Karimova", ById(json, "tx6").GetProperty("teacher").GetProperty("name").GetString());

        // Akkaunt id'si (CreatedById) va login chiqmaydi.
        Assert.DoesNotContain("u-kassir", raw);
        Assert.DoesNotContain("kassir.login", raw);
        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Jurnal_bekor_qilinganlar_soralsa_belgi_bilan_lekin_jamiga_kirmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceTransactions(includeVoided: true));

        Assert.Equal(10, json.GetProperty("total").GetInt32());
        Assert.Equal(1, json.GetProperty("totals").GetProperty("voidedCount").GetInt32());
        Assert.Equal(470_000, json.GetProperty("totals").GetProperty("income").GetDecimal());
        var tx9 = ById(json, "tx9");
        Assert.True(tx9.GetProperty("voided").GetBoolean());
        Assert.Equal("Xato kiritilgan", tx9.GetProperty("voidInfo").GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("direction", "expense", new[] { "tx11", "tx5", "tx6", "tx7", "tx8" })]
    [InlineData("category", "salary", new[] { "tx11", "tx6", "tx7" })]
    [InlineData("typeId", "tt2", new[] { "tx8" })]
    [InlineData("studentId", "s5", new[] { "tx4", "tx5" })]
    [InlineData("teacherId", "t1", new[] { "tx11", "tx6" })]
    [InlineData("groupId", "g1", new[] { "tx1" })]
    [InlineData("cashboxId", "k2", new[] { "tx2", "tx4", "tx5" })]
    [InlineData("method", "bank", new[] { "tx3", "tx8" })]
    [InlineData("search", "kitob", new[] { "tx2" })]
    [InlineData("search", "kv0001", new[] { "tx1" })]
    public async Task Jurnal_filtrlari(string filter, string value, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);
        var tool = host.Tool<FinanceTools>(McpTestHost.SuperAdmin());

        var raw = filter switch
        {
            "direction" => await tool.FinanceTransactions(direction: value),
            "category" => await tool.FinanceTransactions(category: value),
            "typeId" => await tool.FinanceTransactions(typeId: value),
            "studentId" => await tool.FinanceTransactions(studentId: value),
            "teacherId" => await tool.FinanceTransactions(teacherId: value),
            "groupId" => await tool.FinanceTransactions(groupId: value),
            "cashboxId" => await tool.FinanceTransactions(cashboxId: value),
            "method" => await tool.FinanceTransactions(method: value),
            "search" => await tool.FinanceTransactions(search: value),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };
        var json = McpTestHost.Parse(raw);

        Assert.Equal(expected.OrderBy(x => x), Ids(json).OrderBy(x => x));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Jurnal_sahifalash_davr_va_notogri_parametr()
    {
        using var host = new McpTestHost();
        Seed(host);

        var third = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceTransactions(page: 3, pageSize: 4));
        Assert.Equal(1, third.GetProperty("items").GetArrayLength());
        Assert.Equal(9, third.GetProperty("total").GetInt32());
        Assert.Equal(1, host.State.RowCount);

        var prev = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceTransactions($"{P}-01", LastDay(P)));
        Assert.Equal(new[] { "tx10", "tx13", "tx14", "tx15" }, Ids(prev).OrderBy(x => x));

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceTransactions(direction: "kirim"));
        Assert.Contains("direction", ex.Message);
        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceTransactions(method: "naqd"));
    }

    // ------------------------------------------------------------------ finance_debtors

    [Fact]
    public async Task Qarzdorlar_eng_katta_qarz_tepada_fan_boyicha_qatorlar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceDebtors();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "s1", "s2", "s6", "s3" }, Ids(json, "studentId"));   // arxivdagi s4 sukut bo'yicha yo'q
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        Assert.Equal(1_100_000, json.GetProperty("totalDebt").GetDecimal());

        var ali = ById(json, "s1", "studentId");
        Assert.Equal(500_000, ali.GetProperty("debt").GetDecimal());
        Assert.Equal("+998935556677", ali.GetProperty("parent").GetProperty("phone").GetString());
        var line = Assert.Single(ali.GetProperty("groups").EnumerateArray());
        Assert.Equal("g1", line.GetProperty("groupId").GetString());
        Assert.Equal("Ingliz tili", line.GetProperty("course").GetString());
        Assert.Equal("Dilnoza Karimova", line.GetProperty("teacher").GetString());
        Assert.Equal(500_000, line.GetProperty("debt").GetDecimal());
        // Faqat muddati kelgan oylar: o'tgan oyning qoldig'i + joriy oy. Avans (kelgusi) oylar qarz EMAS.
        var months = line.GetProperty("unpaidMonths").EnumerateArray().ToList();
        Assert.Equal(new[] { P, M }, months.Select(m => m.GetProperty("month").GetString()));
        Assert.Equal(100_000, months[0].GetProperty("remaining").GetDecimal());
        Assert.Equal(400_000, months[1].GetProperty("remaining").GetDecimal());

        // Sinovdagi g1 a'zoligi qarz qatori bermaydi.
        var bobur = ById(json, "s2", "studentId");
        Assert.Equal("g2", Assert.Single(bobur.GetProperty("groups").EnumerateArray()).GetProperty("groupId").GetString());

        // Guruhdan chiqqan a'zolikning qarzi ham o'sha fan nomi bilan keladi.
        var gulnora = Assert.Single(ById(json, "s6", "studentId").GetProperty("groups").EnumerateArray());
        Assert.Equal("left", gulnora.GetProperty("membershipStatus").GetString());
        Assert.Equal(200_000, gulnora.GetProperty("debt").GetDecimal());

        // A'zoligi yo'q (eski) o'quvchi — guruhga bog'lanmagan qarz.
        var charos = ById(json, "s3", "studentId");
        Assert.Equal(0, charos.GetProperty("groups").GetArrayLength());
        Assert.Equal(100_000, charos.GetProperty("ungroupedDebt").GetDecimal());

        Assert.DoesNotContain("maxfiy-surat", raw);
        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("g1", null, null, false, new[] { "s1" })]
    [InlineData("g1", null, null, true, new[] { "s4", "s1" })]
    [InlineData(null, "c2", null, false, new[] { "s2", "s6" })]
    [InlineData(null, null, 250_000, false, new[] { "s1", "s2" })]
    [InlineData(null, null, null, true, new[] { "s4", "s1", "s2", "s6", "s3" })]
    public async Task Qarzdorlar_filtrlari(string? groupId, string? courseId, int? minDebt, bool archived, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceDebtors(groupId, courseId, minDebt, archived));

        Assert.Equal(expected, Ids(json, "studentId"));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Qarzdorlar_sahifalash_va_arxivdagi_qarz_qatori()
    {
        using var host = new McpTestHost();
        Seed(host);

        var second = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceDebtors(page: 2, pageSize: 3));
        Assert.Equal(new[] { "s3" }, Ids(second, "studentId"));
        Assert.Equal(4, second.GetProperty("total").GetInt32());

        var all = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .FinanceDebtors(includeArchived: true));
        var eldor = ById(all, "s4", "studentId");
        Assert.True(eldor.GetProperty("isArchived").GetBoolean());
        Assert.Equal(900_000, Assert.Single(eldor.GetProperty("groups").EnumerateArray()).GetProperty("debt").GetDecimal());

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).FinanceDebtors(minDebt: -1));
    }

    // ------------------------------------------------------------------ student_ledger

    [Fact]
    public async Task Ledger_oyma_oy_hisob_qulf_belgisi_tolovlar_va_balans()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).StudentLedgerTool("s1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal(-500_000, json.GetProperty("balance").GetDecimal());
        Assert.Equal(500_000, json.GetProperty("debt").GetDecimal());
        Assert.Equal(400_000, json.GetProperty("currentMonthlyFee").GetDecimal());
        Assert.Equal(800_000, json.GetProperty("totals").GetProperty("charged").GetDecimal());
        Assert.Equal(300_000, json.GetProperty("totals").GetProperty("paid").GetDecimal());

        var months = json.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(new[] { P, M }, months.Select(m => m.GetProperty("month").GetString()));
        Assert.Equal(300_000, months[0].GetProperty("paid").GetDecimal());
        Assert.Equal(100_000, months[0].GetProperty("remaining").GetDecimal());
        Assert.Equal("partial", months[0].GetProperty("status").GetString());
        var pGroup = Assert.Single(months[0].GetProperty("groups").EnumerateArray());
        Assert.True(pGroup.GetProperty("locked").GetBoolean());
        Assert.Equal("English A1", pGroup.GetProperty("groupName").GetString());
        Assert.Equal("Ingliz tili", pGroup.GetProperty("course").GetString());
        Assert.False(months[1].GetProperty("groups")[0].GetProperty("locked").GetBoolean());
        Assert.Equal("unpaid", months[1].GetProperty("status").GetString());

        var byGroup = Assert.Single(json.GetProperty("byGroup").EnumerateArray());
        Assert.Equal(500_000, byGroup.GetProperty("debt").GetDecimal());
        Assert.Equal(2, byGroup.GetProperty("months").GetArrayLength());

        var pay = Assert.Single(json.GetProperty("payments").EnumerateArray());
        Assert.Equal(300_000, pay.GetProperty("amount").GetDecimal());
        Assert.Equal("English A1", pay.GetProperty("group").GetString());
        Assert.Equal("Dilnoza Karimova", pay.GetProperty("teacher").GetString());
        Assert.Equal("KV000123", pay.GetProperty("receiptNo").GetString());

        Assert.DoesNotContain("maxfiy-surat", raw);
        Assert.DoesNotContain("/uploads", raw);
        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Ledger_bekor_qilingan_tolov_belgi_bilan_vozvrat_va_avans()
    {
        using var host = new McpTestHost();
        Seed(host);

        var bobur = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).StudentLedgerTool("s2"));
        var voided = Assert.Single(bobur.GetProperty("payments").EnumerateArray());
        Assert.True(voided.GetProperty("voided").GetBoolean());
        Assert.Equal("Xato kiritilgan", voided.GetProperty("voidInfo").GetProperty("reason").GetString());
        Assert.Equal(0, bobur.GetProperty("totals").GetProperty("paid").GetDecimal());   // bekor qilingani jamiga kirmaydi

        var farrux = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).StudentLedgerTool("s5"));
        Assert.Equal(50_000, farrux.GetProperty("advance").GetDecimal());
        Assert.Equal(50_000, farrux.GetProperty("totals").GetProperty("paid").GetDecimal());   // 100 000 − 50 000 vozvrat
        var refund = Assert.Single(farrux.GetProperty("refunds").EnumerateArray());
        Assert.Equal("tx4", refund.GetProperty("refundOfId").GetString());

        var none = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).StudentLedgerTool("yo'q"));
        Assert.Contains("topilmadi", none.GetProperty("message").GetString());
        Assert.Equal(0, host.State.RowCount);
        await Assert.ThrowsAsync<McpException>(() => host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).StudentLedgerTool(" "));
    }

    // ------------------------------------------------------------------ cashboxes

    [Fact]
    public async Task Kassalar_qoldiq_davr_va_kochirishlar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.Staff("finance.main")).Cashboxes());

        Assert.Equal(new[] { "main", "k2", "k3" }, Ids(json));
        var main = ById(json, "main");
        Assert.True(main.GetProperty("isMain").GetBoolean());
        Assert.Equal(-3_280_000, main.GetProperty("balance").GetDecimal());   // bekor qilingan tx9 va ko'chirish kirmaydi
        Assert.Equal(320_000, main.GetProperty("periodIncome").GetDecimal());
        Assert.Equal(4_300_000, main.GetProperty("periodExpense").GetDecimal());
        Assert.Equal(100_000, main.GetProperty("periodTransferOut").GetDecimal());
        var k2 = ById(json, "k2");
        Assert.Equal("Kassir Olim", k2.GetProperty("responsibleName").GetString());
        Assert.Equal(200_000, k2.GetProperty("balance").GetDecimal());
        Assert.Equal(100_000, k2.GetProperty("periodTransferIn").GetDecimal());
        Assert.Equal(30_000, ById(json, "k3").GetProperty("balance").GetDecimal());   // bekor qilingan ko'chirish kirmaydi
        Assert.Equal(-3_050_000, json.GetProperty("total").GetDecimal());
        Assert.Equal(-3_080_000, json.GetProperty("activeTotal").GetDecimal());

        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Kassalar_kassa_ruxsati_bilan_ROYXAT_lekin_qoldiqsiz()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<FinanceTools>(McpTestHost.Staff("kassa")).Cashboxes();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(3, json.GetProperty("items").GetArrayLength());
        Assert.All(json.GetProperty("items").EnumerateArray(),
            i => Assert.Equal(JsonValueKind.Null, i.GetProperty("balance").ValueKind));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("total").ValueKind);
        Assert.Contains("moliya", json.GetProperty("note").GetString());
        Assert.DoesNotContain("3280000", raw);
        Assert.DoesNotContain("FinanceTransactions", string.Join("\n", host.Sql));   // qoldiq umuman hisoblanmadi
    }

    // ------------------------------------------------------------------ teacher_salaries

    [Fact]
    public async Task Maosh_qaysi_oy_uchun_berilgani_boyicha_sanaladi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var prev = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).TeacherSalaries(P));
        // Arxivdagi o'qituvchi shu oy uchun maosh olgan — ro'yxatda.
        Assert.Equal(new[] { "t2", "t1", "t3" }, Ids(prev, "teacherId"));
        Assert.Equal(1_000_000, ById(prev, "t1", "teacherId").GetProperty("paid").GetDecimal());   // o'tgan oy maoshi joriy oyda berilgan
        Assert.Equal(200_000, ById(prev, "t3", "teacherId").GetProperty("paid").GetDecimal());
        Assert.Equal(0, ById(prev, "t2", "teacherId").GetProperty("paid").GetDecimal());
        Assert.Equal(1_200_000, prev.GetProperty("totalPaid").GetDecimal());

        var cur = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).TeacherSalaries());
        Assert.Equal(new[] { "t2", "t1" }, Ids(cur, "teacherId"));
        var bekzod = ById(cur, "t2", "teacherId");
        Assert.Equal(500_000, bekzod.GetProperty("paid").GetDecimal());   // oyi yo'q — sana bo'yicha
        Assert.Equal(3_000_000, bekzod.GetProperty("fixedSalary").GetDecimal());
        var dilnoza = ById(cur, "t1", "teacherId");
        Assert.Equal(800_000, dilnoza.GetProperty("paid").GetDecimal());
        Assert.Equal(40, dilnoza.GetProperty("salaryPercent").GetDecimal());
        // Hisoblangan maosh, ushlanma va qoldiq — panel bilan AYNAN bir funksiyadan (SalaryLedger).
        var cm = AppClock.Today.ToString("yyyy-MM");
        foreach (var id in new[] { "t1", "t2" })
        {
            var teacher = host.Rw.Teachers.Single(x => x.Id == id);
            var month = (await SalaryLedger.BuildAsync(host.Rw, teacher, cm, cm)).Months.Single(x => x.Month == cm);
            var row = ById(cur, id, "teacherId");
            Assert.Equal(month.Expected, row.GetProperty("accrued").GetDecimal());
            Assert.Equal(month.Deduction, row.GetProperty("journalDeduction").GetDecimal());
            Assert.Equal(month.Paid, row.GetProperty("paid").GetDecimal());
            Assert.Equal(month.Remaining, row.GetProperty("remaining").GetDecimal());
        }
        Assert.Equal(3_000_000, bekzod.GetProperty("accrued").GetDecimal());   // qat'iy oylik, jurnalga bog'lanmagan

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).TeacherSalaries("2026-13"));
        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    // ------------------------------------------------------------------ discounts_report

    [Fact]
    public async Task Chegirmalar_hisoboti_oylik_hisoblardan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.Staff("finance.discounts"))
            .DiscountsReport(P, M));

        var s = json.GetProperty("summary");
        Assert.Equal(3_000_000, s.GetProperty("periodCharged").GetDecimal());
        Assert.Equal(500_000, s.GetProperty("periodDiscount").GetDecimal());
        Assert.Equal(16.7m, s.GetProperty("sharePct").GetDecimal());
        Assert.Equal(2, s.GetProperty("activeCount").GetInt32());        // bekor qilingan va arxivdagi o'quvchiniki kirmaydi
        Assert.Equal(6, s.GetProperty("totalStudents").GetInt32());
        Assert.Equal(33.3m, s.GetProperty("studentSharePct").GetDecimal());
        Assert.Equal(500_000, s.GetProperty("currentMonthDiscount").GetDecimal());

        var months = json.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(new[] { P, M }, months.Select(m => m.GetProperty("month").GetString()));
        Assert.Equal(1_600_000, months[0].GetProperty("charged").GetDecimal());
        Assert.Equal(0, months[0].GetProperty("discount").GetDecimal());
        Assert.Equal(2, months[1].GetProperty("students").GetInt32());

        var byGroup = json.GetProperty("byGroup");
        Assert.Equal("English A1", byGroup[0].GetProperty("groupName").GetString());
        Assert.Equal(2_100_000, byGroup[0].GetProperty("charged").GetDecimal());
        Assert.Equal(400_000, byGroup[0].GetProperty("discount").GetDecimal());
        Assert.Equal("Dilnoza Karimova", json.GetProperty("byTeacher")[0].GetProperty("teacherName").GetString());

        var students = json.GetProperty("byStudent");
        Assert.Equal(2, students.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "s7", "s6" }, Ids(students, "studentId"));
        var hulkar = ById(students, "s7", "studentId");
        Assert.Equal("Hulkar Nurmatova", hulkar.GetProperty("studentName").GetString());
        Assert.Equal(1, hulkar.GetProperty("activeCount").GetInt32());
        Assert.False(string.IsNullOrEmpty(hulkar.GetProperty("activeLabel").GetString()));
        // Panel bilan bir xil: o'quvchi kesimida faqat CHEGIRMALI oylar (P oyidagi chegirmasiz hisob kirmaydi).
        var gulnora = ById(students, "s6", "studentId");
        Assert.Equal(300_000, gulnora.GetProperty("charged").GetDecimal());
        Assert.Equal(1, gulnora.GetProperty("months").GetInt32());

        var reasons = json.GetProperty("byReason").EnumerateArray().ToList();
        Assert.Equal(new[] { "Xodim farzandi", "Ijtimoiy" }, reasons.Select(r => r.GetProperty("reason").GetString()));
        Assert.Equal(400_000, reasons[0].GetProperty("discount").GetDecimal());
        Assert.Equal(100_000, reasons[1].GetProperty("discount").GetDecimal());

        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Chegirmalar_davr_sahifalash_va_chegara()
    {
        using var host = new McpTestHost();
        Seed(host);

        var cur = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).DiscountsReport(M, M));
        Assert.Equal(1_400_000, cur.GetProperty("summary").GetProperty("periodCharged").GetDecimal());
        Assert.Single(cur.GetProperty("months").EnumerateArray());

        var paged = McpTestHost.Parse(await host.Tool<FinanceTools>(McpTestHost.SuperAdmin())
            .DiscountsReport(P, M, page: 2, pageSize: 1));
        Assert.Equal(new[] { "s6" }, Ids(paged.GetProperty("byStudent"), "studentId"));

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).DiscountsReport("2020-01", M));
        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<FinanceTools>(McpTestHost.SuperAdmin()).DiscountsReport("2026/01", M));
    }

    // ------------------------------------------------------------------ ruxsat

    [Theory]
    [InlineData("aiAccess")]
    [InlineData("students.list")]
    [InlineData("kassa")]
    [InlineData("finance.discounts")]
    public async Task Moliya_ruxsatisiz_pul_vositalari_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);
        FinanceTools Tool() => host.Tool<FinanceTools>(McpTestHost.Staff(perm));

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().FinanceSummary());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().FinanceTransactions());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().FinanceDebtors());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().StudentLedgerTool("s1"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().TeacherSalaries());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);   // rad etilganda bazaga umuman so'rov ketmaydi
    }

    [Theory]
    [InlineData("aiAccess")]
    [InlineData("students.list")]
    [InlineData("teachers.list")]
    public async Task Kassa_ruxsatisiz_kassalar_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<FinanceTools>(McpTestHost.Staff(perm)).Cashboxes());
        Assert.Empty(host.Sql);
    }

    [Theory]
    [InlineData("aiAccess")]
    [InlineData("finance.main")]
    [InlineData("kassa")]
    public async Task Chegirmalar_hisoboti_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<FinanceTools>(McpTestHost.Staff(perm)).DiscountsReport());
        Assert.Empty(host.Sql);
    }

    [Fact]
    public async Task Moliya_vositalari_bazada_HECH_NARSANI_ozgartirmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        string Snapshot() => string.Join("|",
            host.Rw.Students.OrderBy(s => s.Id).Select(s => s.Id + ":" + s.Balance).AsEnumerable()
                .Concat(host.Rw.MonthlyCharges.OrderBy(c => c.StudentId).ThenBy(c => c.Month)
                    .Select(c => c.StudentId + c.Month + c.Amount + c.Discount + c.Locked).AsEnumerable())
                .Concat(new[] { host.Rw.FinanceTransactions.Count().ToString(), host.Rw.Cashboxes.Count().ToString() }));
        var before = Snapshot();
        var tool = host.Tool<FinanceTools>(McpTestHost.SuperAdmin());

        await tool.FinanceSummary();
        await tool.FinanceTransactions(includeVoided: true);
        await tool.FinanceDebtors(includeArchived: true);
        await tool.StudentLedgerTool("s1");
        await tool.StudentLedgerTool("s6");
        await tool.Cashboxes();
        await tool.TeacherSalaries(P);
        await tool.DiscountsReport(P, M);

        host.Rw.ChangeTracker.Clear();
        Assert.Equal(before, Snapshot());
        host.AssertReadsOnlyDeclared(typeof(FinanceTools));
        host.AssertNoSecretColumns();
    }
}
