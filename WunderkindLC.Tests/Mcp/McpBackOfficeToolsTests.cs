using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `contracts_list`, `books_stock`, `book_orders`, `feedback_list`, `vacancies_list`, `job_applications`,
/// `onboarding_list` — natija, filtrlar, ruxsat, pul yashirish va fayl manzillari/Telegram ma'lumotlari CHIQMASLIGI.
/// </summary>
public class McpBackOfficeToolsTests
{
    private static List<string?> Ids(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();

    private static JsonElement Item(JsonElement json, string id) =>
        json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

    private static void SeedStudents(McpTestHost host)
    {
        host.Rw.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" });
    }

    // ================================================================== contracts_list

    private static void SeedContracts(McpTestHost host)
    {
        SeedStudents(host);
        host.Rw.Contracts.AddRange(
            new Contract
            {
                Id = "k1", Number = 1, Target = "parent", RecipientKey = "s1", RecipientName = "Ali Valiyev",
                TemplateName = "O'quv shartnomasi", SentAt = new DateTime(2026, 9, 10, 10, 0, 0), Status = "downloaded",
                DocxUrl = "/uploads/maxfiy.docx", PdfUrl = "/uploads/maxfiy.pdf", FileName = "Shartnoma № 1",
            },
            new Contract
            {
                Id = "k2", Number = 2, Target = "parent", RecipientKey = "s2", RecipientName = "Bobur Aliyev",
                TemplateName = "O'quv shartnomasi", SentAt = new DateTime(2026, 10, 1, 9, 0, 0), Status = "sent", Delivered = true,
            },
            new Contract
            {
                Id = "k3", Number = 3, Target = "staff", RecipientKey = "t1", RecipientName = "Dilnoza Karimova",
                TemplateName = "Mehnat shartnomasi", SentAt = new DateTime(2026, 10, 5, 12, 0, 0), Status = "sent", Visible = false,
            },
            new Contract
            {
                Id = "k4", Number = 4, Target = "parent", RecipientKey = "p:998901112233", RecipientName = "Eski Ota",
                TemplateName = "O'quv shartnomasi", SentAt = new DateTime(2025, 12, 1, 8, 0, 0), Status = "downloaded",
            });
        host.Rw.SaveChanges();
    }

    [Fact]
    public async Task Shartnomalar_royxati_eng_yangisi_birinchi_va_fayl_manzilisiz()
    {
        using var host = new McpTestHost();
        SeedContracts(host);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.Staff("contracts")).ContractsList();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "k4", "k3", "k2", "k1" }, Ids(json));
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        var k1 = Item(json, "k1");
        Assert.Equal("s1", k1.GetProperty("studentId").GetString());
        Assert.Equal("download", k1.GetProperty("channel").GetString());
        Assert.Equal(JsonValueKind.Null, k1.GetProperty("telegramDelivered").ValueKind);
        Assert.True(k1.GetProperty("pdfAttached").GetBoolean());
        Assert.Equal("2026-09-10T10:00:00", k1.GetProperty("date").GetString());
        var k2 = Item(json, "k2");
        Assert.Equal("telegram", k2.GetProperty("channel").GetString());
        Assert.True(k2.GetProperty("telegramDelivered").GetBoolean());
        var k3 = Item(json, "k3");
        Assert.Equal("t1", k3.GetProperty("teacherId").GetString());
        Assert.Equal(JsonValueKind.Null, k3.GetProperty("studentId").ValueKind);
        Assert.False(k3.GetProperty("visibleInApp").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Item(json, "k4").GetProperty("studentId").ValueKind);   // eski telefon kaliti
        Assert.DoesNotContain("/uploads", raw);

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("staff", null, null, null, null, null, new[] { "k3" })]
    [InlineData(null, "s1", null, null, null, null, new[] { "k1" })]
    [InlineData(null, null, "t1", null, null, null, new[] { "k3" })]
    [InlineData(null, null, null, "2026-10-01", "2026-10-31", null, new[] { "k3", "k2" })]
    [InlineData(null, null, null, null, null, "mehnat", new[] { "k3" })]
    [InlineData(null, null, null, null, null, "2", new[] { "k2" })]
    [InlineData("parent", null, null, null, null, "bobur", new[] { "k2" })]
    public async Task Shartnomalar_filtrlari(
        string? target, string? studentId, string? teacherId, string? from, string? to, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        SeedContracts(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin())
            .ContractsList(target, studentId, teacherId, from, to, search));
        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Shartnomalar_sahifalash_ruxsat_va_oraliq_chegarasi()
    {
        using var host = new McpTestHost();
        SeedContracts(host);

        var second = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Admin()).ContractsList(page: 2, pageSize: 3));
        Assert.Equal(new[] { "k1" }, Ids(second));
        Assert.Equal(4, second.GetProperty("total").GetInt32());

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Admin()).ContractsList(from: "2025-01-01", to: "2026-10-01"));
        Assert.Contains("366", ex.Message);
        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Admin()).ContractsList(target: "ota"));
    }

    // ================================================================== books

    private static void SeedBooks(McpTestHost host)
    {
        SeedStudents(host);
        var db = host.Rw;
        db.Books.AddRange(
            new Book { Id = "b1", Title = "Ingliz tili 1", Author = "Murphy", Price = 80_000, Stock = 10, CoverUrl = "/uploads/muqova.jpg", CoverFileId = "TGFILE123" },
            new Book { Id = "b2", Title = "Matematika 5", Author = "Alimov", Price = 50_000, Stock = 0, IsActive = false });
        db.BookOrders.AddRange(
            // Qo'lda naqd sotuv, 3 dan 1 tasi qaytarilgan.
            new BookOrder
            {
                Id = "o1", Number = 1, BookId = "b1", BookTitle = "Ingliz tili 1", UnitPrice = 80_000, Qty = 3, Total = 240_000,
                ReturnedQty = 1, RefundedAmount = 80_000, ReturnedAt = new DateTime(2026, 10, 3, 9, 0, 0), ReturnReason = "Ortiqcha",
                Status = "approved", PaymentMethod = "cash", Source = "manual", CustomerName = "Vali Valiyev",
                Phone = "998935556677", StudentId = "s1", CreatedAt = new DateTime(2026, 10, 2, 10, 0, 0),
            },
            // Botdan naqd — tasdiqlangan, lekin olib ketilmagan (to'lanmagan).
            new BookOrder
            {
                Id = "o2", Number = 2, BookId = "b1", BookTitle = "Ingliz tili 1", UnitPrice = 80_000, Qty = 1, Total = 80_000,
                Status = "approved", PaymentMethod = "cash", Source = "bot", PayAtPickup = true, ChatId = 555000111,
                CustomerName = "Mehmon", Phone = "998901234567", CreatedAt = new DateTime(2026, 10, 5, 11, 0, 0),
            },
            // Karta — chek bilan, tasdiq kutilmoqda.
            new BookOrder
            {
                Id = "o3", Number = 3, BookId = "b1", BookTitle = "Ingliz tili 1", UnitPrice = 80_000, Qty = 2, Total = 160_000,
                Status = "pending", PaymentMethod = "card", ReceiptUrl = "/uploads/chek.jpg", ChatId = 555000222,
                CustomerName = "Karim", Phone = "998977777777", CreatedAt = new DateTime(2026, 10, 6, 12, 0, 0),
            },
            // Nasiya — muddati o'tgan.
            new BookOrder
            {
                Id = "o4", Number = 4, BookId = "b2", BookTitle = "Matematika 5", UnitPrice = 50_000, Qty = 1, Total = 50_000,
                Status = "approved", PaymentMethod = "credit", Source = "manual", DueDate = AppClock.Now.Date.AddDays(-10),
                CustomerName = "Nasiyachi", Phone = "998911111111", CreatedAt = new DateTime(2026, 8, 20, 9, 0, 0),
            },
            new BookOrder
            {
                Id = "o5", Number = 5, BookId = "b2", BookTitle = "Matematika 5", UnitPrice = 50_000, Qty = 1, Total = 50_000,
                Status = "rejected", RejectReason = "Chek noto'g'ri", PaymentMethod = "card",
                CustomerName = "Rad", Phone = "998922222222", CreatedAt = new DateTime(2026, 10, 7, 8, 0, 0),
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Kitoblar_ombori_SOF_sotuv_va_qoldiq()
    {
        using var host = new McpTestHost();
        SeedBooks(host);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).BooksStock();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "b1", "b2" }, Ids(json));
        Assert.Equal(10, json.GetProperty("totalStock").GetInt32());
        var b1 = Item(json, "b1");
        Assert.Equal(10, b1.GetProperty("stock").GetInt32());
        Assert.Equal(80_000, b1.GetProperty("price").GetDecimal());
        Assert.Equal(3, b1.GetProperty("soldQty").GetInt32());            // o1 (3-1) + o2
        Assert.Equal(240_000, b1.GetProperty("soldAmount").GetDecimal());  // 320 000 − qaytarilgan 80 000
        Assert.Equal(2, b1.GetProperty("pendingQty").GetInt32());
        var b2 = Item(json, "b2");
        Assert.False(b2.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, b2.GetProperty("soldQty").GetInt32());            // rad etilgan sanalmaydi
        Assert.DoesNotContain("/uploads", raw);
        Assert.DoesNotContain("TGFILE123", raw);

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();

        Assert.Equal(new[] { "b1" }, Ids(McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).BooksStock(activeOnly: true))));
        Assert.Equal(new[] { "b2" }, Ids(McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).BooksStock(search: "alimov"))));
    }

    [Fact]
    public async Task Kitob_buyurtmalari_tolov_olib_ketish_va_nasiya_holati()
    {
        using var host = new McpTestHost();
        SeedBooks(host);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).BookOrders();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "o5", "o3", "o2", "o1", "o4" }, Ids(json));
        var o1 = Item(json, "o1");
        Assert.Equal("Ali Valiyev", o1.GetProperty("studentName").GetString());
        Assert.Equal(2, o1.GetProperty("netQty").GetInt32());
        Assert.Equal(240_000, o1.GetProperty("amount").GetDecimal());
        Assert.Equal(160_000, o1.GetProperty("netAmount").GetDecimal());
        Assert.True(o1.GetProperty("paid").GetBoolean());
        Assert.False(o1.GetProperty("awaitingPickup").GetBoolean());
        var o2 = Item(json, "o2");
        Assert.True(o2.GetProperty("awaitingPickup").GetBoolean());
        Assert.False(o2.GetProperty("paid").GetBoolean());
        Assert.True(Item(json, "o3").GetProperty("receiptAttached").GetBoolean());
        var o4 = Item(json, "o4");
        Assert.True(o4.GetProperty("overdue").GetBoolean());
        Assert.False(o4.GetProperty("paid").GetBoolean());
        Assert.Equal("Chek noto'g'ri", Item(json, "o5").GetProperty("rejectReason").GetString());
        Assert.DoesNotContain("/uploads", raw);
        Assert.DoesNotContain("555000111", raw);

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("awaiting", null, null, null, null, null, new[] { "o2" })]
    [InlineData("returned", null, null, null, null, null, new[] { "o1" })]
    [InlineData("pending", null, null, null, null, null, new[] { "o3" })]
    [InlineData("approved", null, null, null, null, null, new[] { "o2", "o1", "o4" })]
    [InlineData(null, "b2", null, null, null, null, new[] { "o5", "o4" })]
    [InlineData(null, null, "credit", null, null, null, new[] { "o4" })]
    [InlineData(null, null, null, "2026-10-05", "2026-10-06", null, new[] { "o3", "o2" })]
    [InlineData(null, null, null, null, null, "93555", new[] { "o1" })]          // telefon
    [InlineData(null, null, null, null, null, "nasiya", new[] { "o4" })]         // xaridor
    [InlineData(null, null, null, null, null, "matem", new[] { "o5", "o4" })]    // kitob nomi
    public async Task Kitob_buyurtmalari_filtrlari(
        string? status, string? bookId, string? method, string? from, string? to, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        SeedBooks(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin())
            .BookOrders(status, bookId, method, from, to, search));
        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Kitoblar_moliya_ruxsatisiz_summalar_YOQ_va_ruxsatsiz_RAD()
    {
        using var host = new McpTestHost();
        SeedBooks(host);

        var stock = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Staff("books")).BooksStock());
        Assert.Equal(JsonValueKind.Null, Item(stock, "b1").GetProperty("soldAmount").ValueKind);
        Assert.Equal(3, Item(stock, "b1").GetProperty("soldQty").GetInt32());
        Assert.Equal(JsonValueKind.String, stock.GetProperty("note").ValueKind);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.Staff("books")).BookOrders();
        var orders = McpTestHost.Parse(raw);
        Assert.Equal(JsonValueKind.Null, Item(orders, "o1").GetProperty("amount").ValueKind);
        Assert.Equal(JsonValueKind.Null, Item(orders, "o1").GetProperty("netAmount").ValueKind);
        Assert.DoesNotContain("240000", raw);

        var sqlBefore = host.Sql.Count;
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("contracts")).BooksStock());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("contracts")).BookOrders());
        Assert.Equal(sqlBefore, host.Sql.Count);
    }

    // ================================================================== feedback_list

    private static void SeedFeedback(McpTestHost host)
    {
        SeedStudents(host);
        var db = host.Rw;
        db.Feedbacks.AddRange(
            new Feedback
            {
                Id = "f1", StudentId = "s1", ParentName = "Vali", SenderRole = "parent", SenderName = "Vali Valiyev",
                Type = "suggestion", Text = new string('a', 600), ImageUrl = "/uploads/rasm.jpg",
                CreatedAt = new DateTime(2026, 10, 1, 9, 0, 0), Status = "new",
            },
            new Feedback
            {
                Id = "f2", StudentId = "", SenderRole = "teacher", SenderName = "Dilnoza", TeacherId = "t1",
                Type = "complaint", Text = "Xona sovuq", CreatedAt = new DateTime(2026, 10, 3, 9, 0, 0), Status = "resolved",
            },
            new Feedback
            {
                Id = "f3", StudentId = "s2", ParentName = "Bobur ota", SenderRole = "", SenderName = "",
                Type = "complaint", Text = "Dars kech boshlanadi", CreatedAt = new DateTime(2026, 9, 15, 9, 0, 0), Status = "resolved",
            });
        db.AuditLogs.AddRange(
            new AuditLog { EntityType = "Feedback", EntityId = "f2", Action = "update", Timestamp = "2026-10-04T09:00:00", ActorName = "Admin Bek", Summary = "Shikoyat hal qilindi deb belgilandi (Dilnoza)" },
            new AuditLog { EntityType = "Feedback", EntityId = "f3", Action = "update", Timestamp = "2026-09-16T10:00:00", ActorName = "Operator", Summary = "Shikoyat hal qilindi deb belgilandi (Bobur ota)" },
            new AuditLog { EntityType = "Feedback", EntityId = "f3", Action = "update", Timestamp = "2026-09-17T10:00:00", ActorName = "Operator", Summary = "Shikoyat qayta ochildi (Bobur ota)" },
            new AuditLog { EntityType = "Feedback", EntityId = "f3", Action = "update", Timestamp = "2026-09-18T11:00:00", ActorName = "Rahbar", Summary = "Shikoyat hal qilindi deb belgilandi (Bobur ota)" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Taklif_shikoyatlar_kim_va_qachon_hal_qilgani_bilan()
    {
        using var host = new McpTestHost();
        SeedFeedback(host);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).FeedbackList();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "f2", "f1", "f3" }, Ids(json));
        Assert.Equal(1, json.GetProperty("open").GetInt32());
        var f1 = Item(json, "f1");
        Assert.Equal(501, f1.GetProperty("text").GetString()!.Length);          // 500 + "…"
        Assert.Equal("Vali Valiyev", f1.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("Ali Valiyev", f1.GetProperty("student").GetProperty("name").GetString());
        Assert.True(f1.GetProperty("imageAttached").GetBoolean());
        Assert.False(f1.GetProperty("resolved").GetBoolean());
        var f2 = Item(json, "f2");
        Assert.Equal("teacher", f2.GetProperty("author").GetProperty("role").GetString());
        Assert.Equal("t1", f2.GetProperty("author").GetProperty("teacherId").GetString());
        Assert.Equal(JsonValueKind.Null, f2.GetProperty("student").ValueKind);
        Assert.Equal("Admin Bek", f2.GetProperty("resolvedBy").GetString());
        var f3 = Item(json, "f3");
        Assert.Equal("parent", f3.GetProperty("author").GetProperty("role").GetString());
        Assert.Equal("Bobur ota", f3.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("Rahbar", f3.GetProperty("resolvedBy").GetString());          // oxirgi "hal qilindi"
        Assert.Equal("2026-09-18T11:00:00", f3.GetProperty("resolvedAt").GetString());
        Assert.DoesNotContain("/uploads", raw);

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("complaint", null, null, null, new[] { "f2", "f3" })]
    [InlineData("suggestion", null, null, null, new[] { "f1" })]
    [InlineData(null, false, null, null, new[] { "f1" })]
    [InlineData(null, true, null, null, new[] { "f2", "f3" })]
    [InlineData(null, null, "2026-10-01", "2026-10-31", new[] { "f2", "f1" })]
    public async Task Taklif_shikoyatlar_filtrlari(string? kind, bool? resolved, string? from, string? to, string[] expected)
    {
        using var host = new McpTestHost();
        SeedFeedback(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Admin()).FeedbackList(kind, resolved, from, to));
        Assert.Equal(expected, Ids(json));
    }

    [Fact]
    public async Task Taklif_shikoyatlar_tarix_ruxsatisiz_kim_hal_qilgani_YOQ_va_ruxsatsiz_RAD()
    {
        using var host = new McpTestHost();
        SeedFeedback(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Staff("feedback")).FeedbackList());
        Assert.Equal(JsonValueKind.Null, Item(json, "f2").GetProperty("resolvedBy").ValueKind);
        Assert.True(Item(json, "f2").GetProperty("resolved").GetBoolean());
        Assert.Equal(JsonValueKind.String, json.GetProperty("note").ValueKind);
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"AuditLogs\"", StringComparison.Ordinal));

        var withAudit = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Staff("feedback", "audit")).FeedbackList());
        Assert.Equal("Admin Bek", Item(withAudit, "f2").GetProperty("resolvedBy").GetString());

        var sqlBefore = host.Sql.Count;
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("students.list")).FeedbackList());
        Assert.Equal(sqlBefore, host.Sql.Count);
    }

    // ================================================================== vacancies / applications

    private static void SeedCareer(McpTestHost host)
    {
        var db = host.Rw;
        db.Vacancies.AddRange(
            new Vacancy { Id = "v1", Title = "Ingliz tili o'qituvchisi", Status = "active", Order = 1, CreatedAt = "2026-09-01T10:00:00", SalaryFrom = 5_000_000, SalaryTo = 8_000_000 },
            new Vacancy { Id = "v2", Title = "Administrator", Status = "paused", Order = 2, CreatedAt = "2026-09-05T10:00:00" },
            new Vacancy { Id = "v3", Title = "Eski lavozim", Status = "archived", Order = 0, CreatedAt = "2026-01-01T10:00:00", ArchivedAt = "2026-03-01T10:00:00" });
        db.JobApplications.AddRange(
            new JobApplication
            {
                Id = "a1", Number = 1, VacancyId = "v1", VacancyTitle = "Ingliz tili o'qituvchisi", FullName = "Aziz Karimov",
                Phone = "+998901111111", Status = "new", CreatedAt = "2026-09-10T10:00:00", CvUrl = "/uploads/cv-maxfiy.pdf",
                ChatId = 777000111, TgUsername = "aziz_tg", AdminNote = "Kuchli nomzod", Experience = new string('x', 700),
            },
            new JobApplication
            {
                Id = "a2", Number = 2, VacancyId = "v1", VacancyTitle = "Ingliz tili o'qituvchisi", FullName = "Bekzod Tursunov",
                Phone = "+998902222222", Status = "interview", StatusNote = "Suhbat 12-oktabr 10:00",
                StatusChangedAt = "2026-10-02T09:00:00", StatusChangedBy = "HR", CreatedAt = "2026-09-20T10:00:00",
            },
            new JobApplication { Id = "a3", Number = 3, VacancyId = "v1", VacancyTitle = "Ingliz tili o'qituvchisi", FullName = "Dildora", Phone = "+998903333333", Status = "rejected", CreatedAt = "2026-10-05T10:00:00" },
            new JobApplication { Id = "a4", Number = 4, VacancyId = "v2", VacancyTitle = "Administrator", FullName = "Eldor", Phone = "+998904444444", Status = "hired", CreatedAt = "2026-10-06T10:00:00" },
            new JobApplication { Id = "a5", Number = 5, VacancyId = "v3", VacancyTitle = "Eski lavozim", FullName = "Farida", Phone = "+998905555555", Status = "review", CreatedAt = "2026-02-01T10:00:00" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Vakansiyalar_bosqichlar_boyicha_arizalar_soni_bilan()
    {
        using var host = new McpTestHost();
        SeedCareer(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Staff("vacancies.list")).VacanciesList());

        Assert.Equal(new[] { "v1", "v2" }, Ids(json));                       // sukut: joriylar (active + paused)
        var apps = Item(json, "v1").GetProperty("applications");
        Assert.Equal(3, apps.GetProperty("total").GetInt32());
        Assert.Equal(1, apps.GetProperty("newCount").GetInt32());
        Assert.Equal(new[] { "new", "interview", "rejected" },
            apps.GetProperty("byStage").EnumerateArray().Select(s => s.GetProperty("stage").GetString()));
        Assert.Equal(5_000_000, Item(json, "v1").GetProperty("salary").GetProperty("from").GetDecimal());
        Assert.Equal("hired", Item(json, "v2").GetProperty("applications").GetProperty("byStage")[0].GetProperty("stage").GetString());
        Assert.DoesNotContain(json.GetProperty("stages").EnumerateArray(), s => s.GetProperty("key").GetString() == "review");

        var all = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).VacanciesList("all"));
        Assert.Equal(new[] { "v3", "v1", "v2" }, Ids(all));
        var archived = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).VacanciesList("archived"));
        Assert.Equal("review", Item(archived, "v3").GetProperty("applications").GetProperty("byStage")[0].GetProperty("stage").GetString());
        await Assert.ThrowsAsync<McpException>(() => host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).VacanciesList("ochiq"));

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Arizalar_royxati_CV_va_Telegram_malumotisiz()
    {
        using var host = new McpTestHost();
        SeedCareer(host);

        var raw = await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).JobApplications();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "a4", "a3", "a2", "a1", "a5" }, Ids(json));
        var a1 = Item(json, "a1");
        Assert.True(a1.GetProperty("cvAttached").GetBoolean());
        Assert.Equal("Kuchli nomzod", a1.GetProperty("internalNote").GetString());
        Assert.Equal(501, a1.GetProperty("experience").GetString()!.Length);
        Assert.Equal("CV qabul qilindi", a1.GetProperty("stageLabel").GetString());
        var a2 = Item(json, "a2");
        Assert.Equal("Suhbat 12-oktabr 10:00", a2.GetProperty("noteToCandidate").GetString());
        Assert.Equal("HR", a2.GetProperty("stageChangedBy").GetString());
        Assert.DoesNotContain("/uploads", raw);
        Assert.DoesNotContain("777000111", raw);
        Assert.DoesNotContain("aziz_tg", raw);

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("v1", null, null, null, null, new[] { "a3", "a2", "a1" })]
    [InlineData(null, "interview", null, null, null, new[] { "a2" })]
    [InlineData(null, "review", null, null, null, new[] { "a5" })]
    [InlineData(null, null, "2026-10-01", "2026-10-31", null, new[] { "a4", "a3" })]
    [InlineData(null, null, null, null, "bekzod", new[] { "a2" })]
    [InlineData(null, null, null, null, "4444", new[] { "a4" })]
    [InlineData(null, null, null, null, "administrator", new[] { "a4" })]
    public async Task Arizalar_filtrlari(string? vacancyId, string? stage, string? from, string? to, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        SeedCareer(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Admin())
            .JobApplications(vacancyId, stage, from, to, search));
        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Vakansiyalar_va_moslashuv_ruxsatlari_ALOHIDA()
    {
        using var host = new McpTestHost();
        SeedCareer(host);

        // Faqat moslashuv berilgan xodim CV/arizalarni ko'rmaydi — va teskarisi (onboarding.md §7).
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("vacancies.onboarding")).JobApplications());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("vacancies.onboarding")).VacanciesList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("vacancies.list")).OnboardingList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.Staff("books")).ContractsList());
        Assert.Empty(host.Sql);

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).JobApplications(stage: "suhbat"));
    }

    // ================================================================== onboarding_list

    private static string Day(int offset) => McpToolContext.Iso(McpToolContext.Today.AddDays(offset));

    private static void SeedOnboarding(McpTestHost host)
    {
        var db = host.Rw;
        db.EmployeeOnboardings.AddRange(
            new EmployeeOnboarding { Id = "e1", FullName = "Aziza Rahimova", Phone = "+998905555555", Position = "Ingliz tili o'qituvchisi", StartDate = Day(-10), Status = "active", Source = "hired", ApplicationNumber = 7, MentorName = "Dilnoza" },
            new EmployeeOnboarding { Id = "e2", FullName = "Botir Saidov", Phone = "+998906666666", Position = "Administrator", StartDate = Day(-3), Status = "active", Source = "manual" },
            new EmployeeOnboarding { Id = "e3", FullName = "Gulnora", StartDate = Day(-40), Status = "completed", Source = "manual", CompletedAt = Day(-12) },
            new EmployeeOnboarding { Id = "e4", FullName = "Hasan", StartDate = Day(-20), Status = "cancelled", Source = "manual", CancelledAt = Day(-15) });
        db.EmployeeOnboardingTasks.AddRange(
            new EmployeeOnboardingTask { Id = "e1t1", OnboardingId = "e1", Title = "Hujjatlar", DayOffset = 0, Order = 1, DoneAt = Day(-10) + "T10:00:00", DoneBy = "HR" },
            new EmployeeOnboardingTask { Id = "e1t2", OnboardingId = "e1", Title = "Shartnoma", DayOffset = 2, Order = 2 },
            new EmployeeOnboardingTask { Id = "e1t3", OnboardingId = "e1", Title = "Telegram kanallar", DayOffset = 5, Order = 3, Note = "Guruh havolasi yuborildi" },
            new EmployeeOnboardingTask { Id = "e1t4", OnboardingId = "e1", Title = "Birinchi dars", DayOffset = 14, Order = 4 },
            new EmployeeOnboardingTask { Id = "e2t1", OnboardingId = "e2", Title = "Hujjatlar", DayOffset = 0, Order = 1, DoneAt = Day(-3) + "T10:00:00" },
            new EmployeeOnboardingTask { Id = "e2t2", OnboardingId = "e2", Title = "Metodika", DayOffset = 7, Order = 2 },
            new EmployeeOnboardingTask { Id = "e3t1", OnboardingId = "e3", Title = "Hujjatlar", DayOffset = 0, Order = 1, DoneAt = Day(-40) + "T10:00:00" },
            new EmployeeOnboardingTask { Id = "e3t2", OnboardingId = "e3", Title = "Yakuniy suhbat", DayOffset = 30, Order = 2, DoneAt = Day(-12) + "T10:00:00" },
            new EmployeeOnboardingTask { Id = "e4t1", OnboardingId = "e4", Title = "Hujjatlar", DayOffset = 1, Order = 1 });
        db.SaveChanges();
    }

    [Fact]
    public async Task Moslashuv_royxati_foiz_kun_kechikish_va_jamlanma()
    {
        using var host = new McpTestHost();
        SeedOnboarding(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Staff("vacancies.onboarding")).OnboardingList());

        Assert.Equal(new[] { "e1", "e2", "e4", "e3" }, Ids(json));      // faollar tepada, eng ko'p kechikkan birinchi
        var s = json.GetProperty("summary");
        Assert.Equal(2, s.GetProperty("active").GetInt32());
        Assert.Equal(1, s.GetProperty("completed").GetInt32());
        Assert.Equal(1, s.GetProperty("cancelled").GetInt32());
        Assert.Equal(1, s.GetProperty("withOverdue").GetInt32());
        Assert.Equal(2, s.GetProperty("overdueTasks").GetInt32());

        var e1 = Item(json, "e1");
        Assert.Equal(25, e1.GetProperty("progressPct").GetInt32());
        Assert.Equal(11, e1.GetProperty("dayNumber").GetInt32());
        Assert.Equal(2, e1.GetProperty("overdueTasks").GetInt32());
        Assert.Equal("Shartnoma", e1.GetProperty("nextTask").GetProperty("title").GetString());
        Assert.Equal(Day(-8), e1.GetProperty("nextTask").GetProperty("dueDate").GetString());
        Assert.Equal("Dilnoza", e1.GetProperty("mentorName").GetString());
        Assert.Equal(7, e1.GetProperty("applicationNumber").GetInt32());
        Assert.Equal(50, Item(json, "e2").GetProperty("progressPct").GetInt32());
        Assert.Equal(0, Item(json, "e4").GetProperty("overdueTasks").GetInt32());   // to'xtatilganda kechikish yo'q
        Assert.Equal(100, Item(json, "e3").GetProperty("progressPct").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("overdue", null, new[] { "e1" })]
    [InlineData("completed", null, new[] { "e3" })]
    [InlineData("active", null, new[] { "e1", "e2" })]
    [InlineData(null, "dilnoza", new[] { "e1" })]      // mentor
    [InlineData(null, "admin", new[] { "e2" })]        // lavozim
    public async Task Moslashuv_filtrlari(string? status, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        SeedOnboarding(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.Admin()).OnboardingList(status, search));
        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Moslashuv_cheklisti_id_boyicha_va_sahifalash()
    {
        using var host = new McpTestHost();
        SeedOnboarding(host);

        var json = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).OnboardingList(onboardingId: "e1"));
        Assert.Equal("Aziza Rahimova", json.GetProperty("onboarding").GetProperty("fullName").GetString());
        var list = json.GetProperty("checklist").EnumerateArray().ToList();
        Assert.Equal(4, list.Count);
        Assert.True(list[0].GetProperty("done").GetBoolean());
        Assert.Equal("HR", list[0].GetProperty("doneBy").GetString());
        Assert.True(list[1].GetProperty("overdue").GetBoolean());
        Assert.Equal("Guruh havolasi yuborildi", list[2].GetProperty("note").GetString());
        Assert.False(list[3].GetProperty("overdue").GetBoolean());

        var missing = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).OnboardingList(onboardingId: "yoq"));
        Assert.Contains("topilmadi", missing.GetProperty("message").GetString());

        var page2 = McpTestHost.Parse(await host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).OnboardingList(page: 2, pageSize: 2));
        Assert.Equal(new[] { "e4", "e3" }, Ids(page2));
        Assert.Equal(4, page2.GetProperty("total").GetInt32());

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<BackOfficeTools>(McpTestHost.SuperAdmin()).OnboardingList(status: "kechikkan"));
        host.AssertReadsOnlyDeclared(typeof(BackOfficeTools));
    }
}
