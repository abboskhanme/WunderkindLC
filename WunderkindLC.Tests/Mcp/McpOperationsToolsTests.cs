using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `contact_queue`, `contact_report`, `tasks_list`, `messages_history`, `auto_message_rules`, `audit_log` —
/// natija, filtrlar, sahifalash, ruxsat va maxfiylik.
/// </summary>
public class McpOperationsToolsTests
{
    /// <summary>Bugungi kundan siljigan sana (testlar sanaga bog'lanmasin).</summary>
    private static string D(int days) => McpToolContext.Iso(McpToolContext.Today.AddDays(days));

    private static DateTime At(int days, int hour) => McpToolContext.Today.AddDays(days).ToDateTime(new TimeOnly(hour, 0));

    private static List<string?> Ids(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();

    private static JsonElement Item(JsonElement json, string id) =>
        json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;

        db.Users.AddRange(
            new AppUser { Id = "u1", FullName = "Olim Operator", Role = Roles.Staff, Email = "olim.login", PasswordHash = "XESH-1" },
            new AppUser { Id = "u2", FullName = "Nodira Menejer", Role = Roles.Admin, Email = "nodira.login", PasswordHash = "XESH-2", InitialPassword = "ochiq-parol" });

        // ---------------------------------------------------------------- Bog'lanish kerak
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev", Phone = "+998901112233", ParentPhone = "+998935556677", FatherPhone = "+998901112233" },
            new Student { Id = "s2", FullName = "Bobur Arxivda", Phone = "+998907778899", IsArchived = true },
            new Student { Id = "s3", FullName = "Charos Tosheva", MotherPhone = "+998911234567" },
            new Student { Id = "s4", FullName = "Dilshod Karimov" },
            new Student { Id = "s5", FullName = "Eldor Nazarov" },
            new Student { Id = "s6", FullName = "Farrux Olimov" });
        db.ContactStages.AddRange(
            new ContactStage { Id = ContactStatuses.New, Title = "Bog'lanish kerak", BaseStatus = ContactStatuses.New, IsSystem = true, Order = 0 },
            new ContactStage { Id = ContactStatuses.Callback, Title = "Qayta qo'ng'iroq", BaseStatus = ContactStatuses.Callback, IsSystem = true, Order = 1 },
            new ContactStage { Id = ContactStatuses.Done, Title = "Hal bo'ldi", BaseStatus = ContactStatuses.Done, IsSystem = true, Order = 2 },
            new ContactStage { Id = ContactStatuses.Failed, Title = "Bog'lanib bo'lmadi", BaseStatus = ContactStatuses.Failed, IsSystem = true, Order = 3 },
            new ContactStage { Id = "st-sms", Title = "SMS yuborildi", BaseStatus = ContactStatuses.Callback, Order = 4 });
        db.ContactRequests.AddRange(
            new ContactRequest { Id = "r1", StudentId = "s1", StudentName = "Ali Valiyev", ReasonId = "rs1", ReasonLabel = "Qarzdorlik", Status = ContactStatuses.New, CreatedAt = D(-1) + "T10:00:00", CreatedBy = "Dilnoza O'qituvchi" },
            new ContactRequest
            {
                Id = "r2", StudentId = "s2", StudentName = "Bobur Arxivda", ReasonId = "rs2", ReasonLabel = "Davomat", Status = ContactStatuses.Callback,
                StageId = "st-sms", DueDate = D(-2), AttemptCount = 1, LastResponse = "ko'tarmadi", LastActorName = "Olim Operator",
                LastActionAt = D(-3) + "T12:00:00", CreatedAt = D(-5) + "T09:00:00", CreatedBy = "Olim Operator",
            },
            new ContactRequest { Id = "r3", StudentId = "s3", StudentName = "Charos Tosheva", ReasonId = "rs1", ReasonLabel = "Qarzdorlik", Status = ContactStatuses.Callback, DueDate = D(0), CreatedAt = D(-2) + "T09:00:00" },
            new ContactRequest { Id = "r4", StudentId = "s4", StudentName = "Dilshod Karimov", ReasonLabel = "Davomat", ReasonId = "rs2", Status = ContactStatuses.Callback, DueDate = D(1), CreatedAt = D(-6) + "T09:00:00" },
            new ContactRequest { Id = "r5", StudentId = "s5", StudentName = "Eldor Nazarov", Status = ContactStatuses.Callback, DueDate = D(5), CreatedAt = D(-6) + "T08:00:00" },
            new ContactRequest { Id = "r8", StudentId = "s6", StudentName = "Farrux Olimov", Status = ContactStatuses.Callback, DueDate = D(20), CreatedAt = D(-6) + "T07:00:00" },
            new ContactRequest { Id = "r6", StudentId = "s3", StudentName = "Charos Tosheva", Status = ContactStatuses.Done, CreatedAt = D(-3) + "T09:00:00", ClosedAt = D(-1) + "T12:00:00", ClosedBy = "Nodira Menejer" },
            new ContactRequest { Id = "r7", StudentId = "s4", StudentName = "Dilshod Karimov", Status = ContactStatuses.Failed, CreatedAt = D(-4) + "T09:00:00", ClosedAt = D(0) + "T09:00:00", ClosedBy = "Olim Operator" });
        db.ContactAttempts.AddRange(
            new ContactAttempt { RequestId = "r1", StudentId = "s1", Type = ContactAttemptTypes.Created, ActorName = "Olim Operator", Date = D(-1), CreatedAt = D(-1) + "T10:00:00" },
            new ContactAttempt { RequestId = "r2", StudentId = "s2", Type = ContactAttemptTypes.Contact, Result = "no_answer", NextStatus = ContactStatuses.Callback, Response = "telefonni ko'tarmadi", ActorName = "Olim Operator", Date = D(-1), CreatedAt = D(-1) + "T11:00:00" },
            new ContactAttempt { RequestId = "r3", StudentId = "s3", Type = ContactAttemptTypes.Contact, Result = "answered", NextStatus = ContactStatuses.Callback, Response = "To'lovni ertaga qiladi", ActorName = "Nodira Menejer", Date = D(0), CreatedAt = D(0) + "T09:30:00" },
            new ContactAttempt { RequestId = "r6", StudentId = "s3", Type = ContactAttemptTypes.Contact, Result = "answered", NextStatus = ContactStatuses.Done, Response = "To'lovni qildi", ActorName = "Nodira Menejer", Date = D(-1), CreatedAt = D(-1) + "T12:00:00" },
            new ContactAttempt { RequestId = "r7", StudentId = "s4", Type = ContactAttemptTypes.Contact, Result = "wrong_number", NextStatus = ContactStatuses.Failed, ActorName = "Olim Operator", Date = D(0), CreatedAt = D(0) + "T09:00:00" },
            new ContactAttempt { RequestId = "r7", StudentId = "s4", Type = ContactAttemptTypes.Contact, Result = "answered", NextStatus = ContactStatuses.Callback, Response = "eski", ActorName = "Olim Operator", Date = D(-30), CreatedAt = D(-30) + "T09:00:00" });

        // ---------------------------------------------------------------- Topshiriqlar
        db.WorkTaskBoards.AddRange(
            new WorkTaskBoard { Id = "b1", Title = "Umumiy", Order = 0 },
            new WorkTaskBoard { Id = "b2", Title = "Marketing", Order = 1, IsArchived = true });
        db.WorkTaskColumns.AddRange(
            new WorkTaskColumn { Id = "c1", BoardId = "b1", Title = "Yangi", Order = 0, IsSystem = true },
            new WorkTaskColumn { Id = "c2", BoardId = "b1", Title = "Bajarildi", Order = 1, IsDone = true, IsSystem = true },
            new WorkTaskColumn { Id = "c3", BoardId = "b2", Title = "Yangi", Order = 0 },
            new WorkTaskColumn { Id = "c4", BoardId = "b2", Title = "Tayyor", Order = 1, IsDone = true });
        db.WorkTasks.AddRange(
            new WorkTask { Id = "t1", BoardId = "b1", ColumnId = "c1", Title = "Hisobotni tayyorlash", Description = "Oylik hisobot", AssigneeId = "u1", CreatedById = "u2", Priority = 2, DueDate = D(-2), CreatedAt = At(-5, 9) },
            new WorkTask { Id = "t2", BoardId = "b1", ColumnId = "c1", Title = "Ota-onalar majlisi", AssigneeId = "u2", CreatedById = "u2", Priority = 3, DueDate = D(0), DueTime = "18:00", CreatedAt = At(-4, 9) },
            new WorkTask { Id = "t3", BoardId = "b1", ColumnId = "c1", Title = "Xonani tozalash", AssigneeId = "", CreatedById = "u1", Priority = 0, CreatedAt = At(-3, 9) },
            new WorkTask { Id = "t4", BoardId = "b1", ColumnId = "c2", Title = "Kassa hisobi", AssigneeId = "u1", CreatedById = "u2", Priority = 1, DueDate = D(-5), CreatedAt = At(-6, 9), CompletedAt = At(-4, 15), CompletedById = "u1" },
            new WorkTask { Id = "t5", BoardId = "b2", ColumnId = "c3", Title = "Instagram post", AssigneeId = "u1", CreatedById = "u2", DueDate = D(1), CreatedAt = At(-2, 9) },
            new WorkTask { Id = "t6", BoardId = "b1", ColumnId = "c1", Title = "Eski topshiriq", AssigneeId = "u1", CreatedById = "u2", DueDate = D(3), IsArchived = true, CreatedAt = At(-9, 9) });
        db.WorkTaskItems.AddRange(
            new WorkTaskItem { TaskId = "t1", Title = "Ma'lumot yig'ish", Done = true, Order = 0 },
            new WorkTaskItem { TaskId = "t1", Title = "Jadval", Order = 1 },
            new WorkTaskItem { TaskId = "t1", Title = "Yuborish", Order = 2 });

        // ---------------------------------------------------------------- E'lonlar va avto-xabarlar
        db.Broadcasts.AddRange(
            new Broadcast { Id = "bc1", ClassName = "English A1", Text = "Ertaga dars bo'lmaydi", SenderUserId = "u2", SenderName = "Nodira Menejer", CreatedAt = At(-3, 10), RecipientCount = 10, SentCount = 9 },
            new Broadcast { Id = "bc2", ClassName = "Barcha guruhlar — qarzdorlar", Text = new string('x', 600), SenderUserId = "u2", SenderName = "Nodira Menejer", CreatedAt = At(-1, 10), RecipientCount = 50, SentCount = 48 },
            new Broadcast { Id = "bc3", ClassName = "Tanlangan (3)", Text = "Eski e'lon", SenderUserId = "u1", SenderName = "Olim Operator", CreatedAt = At(-40, 10), RecipientCount = 3, SentCount = 3 });
        db.AutoMessageRules.AddRange(
            new AutoMessageRule
            {
                Id = "am1", Trigger = "payment_debt", Name = "Qarz eslatmasi", Enabled = true, SendTelegram = true, SendSms = true,
                SmsProvider = "maxfiy-provayder", Audience = "parents", Template = "Hurmatli {fish}, qarzdorlik: {qarzdorlik}", CreatedAt = At(-30, 9),
            },
            new AutoMessageRule { Id = "am2", Trigger = "attendance_absent", Name = "Kelmadi", Enabled = false, Template = "{ism} darsga kelmadi", CreatedAt = At(-20, 9) },
            new AutoMessageRule
            {
                Id = "am3", Trigger = "custom_schedule", Name = "Oylik eslatma", Enabled = true, SendTelegram = true, Audience = "students",
                Template = "Eslatma {markaz}", ScheduleType = "monthly", ScheduleTime = "10:30", ScheduleDayOfMonth = 5, CreatedAt = At(-10, 9),
            });

        // ---------------------------------------------------------------- Audit
        db.AuditLogs.AddRange(
            new AuditLog { Id = "a1", EntityType = "Student", EntityId = "s1", Action = "update", Timestamp = D(-1) + "T10:00:00", ActorName = "Olim Operator", Summary = "O'quvchi ma'lumoti o'zgardi: Ali Valiyev", StudentId = "s1", Before = "{\"Phone\":\"maxfiy-oldin\"}", After = "{\"Phone\":\"maxfiy-keyin\"}" },
            new AuditLog { Id = "a2", EntityType = "FinanceTransaction", EntityId = "tx1", Action = "create", Timestamp = D(0) + "T09:00:00", ActorName = "Nodira Menejer", Summary = "To'lov qabul qilindi: 500 000 so'm", StudentId = "s1" },
            new AuditLog { Id = "a3", EntityType = "Membership", EntityId = "g1:s1", Action = "create", Timestamp = D(-2) + "T11:00:00", ActorName = "Olim Operator", Summary = "Guruhga qo'shildi: English A1", StudentId = "s1" },
            new AuditLog { Id = "a4", EntityType = "TeacherSalary", EntityId = "t1", Action = "update", Timestamp = D(-3) + "T08:00:00", ActorName = "Nodira Menejer", Summary = "Maosh to'landi", TeacherId = "t1" },
            new AuditLog { Id = "a5", EntityType = "YangiTur", EntityId = "x1", Action = "delete", Timestamp = D(-10) + "T08:00:00", ActorName = "Nodira Menejer", Summary = "Noma'lum yozuv o'chirildi" },
            new AuditLog { Id = "a6", EntityType = "Lead", EntityId = "L1", Action = "delete", Timestamp = D(-40) + "T08:00:00", ActorName = "Olim Operator", Summary = "Lid o'chirildi" });

        db.SaveChanges();
    }

    // ================================================================== contact_queue

    [Fact]
    public async Task Navbat_sukut_boyicha_OCHIQLAR_taxta_tartibida_va_sanoqlar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("contacts")).ContactQueue();
        var json = McpTestHost.Parse(raw);

        // Qayta qo'ng'iroqlar muddati bo'yicha (kechikkan tepada), keyin sanasiz yangi talab.
        Assert.Equal(new[] { "r2", "r3", "r4", "r5", "r8", "r1" }, Ids(json));
        Assert.Equal(6, json.GetProperty("total").GetInt32());

        var sum = json.GetProperty("summary");
        Assert.Equal(6, sum.GetProperty("openNow").GetInt32());
        Assert.Equal(1, sum.GetProperty("overdueNow").GetInt32());
        Assert.Equal(3, sum.GetProperty("todo").GetInt32());            // kechikkan + bugungi + sanasiz
        Assert.Equal(1, sum.GetProperty("due").GetProperty("tomorrow").GetInt32());
        Assert.Equal(1, sum.GetProperty("due").GetProperty("week").GetInt32());
        Assert.Equal(1, sum.GetProperty("due").GetProperty("later").GetInt32());
        Assert.Equal(1, sum.GetProperty("due").GetProperty("nodate").GetInt32());
        Assert.Equal(1, sum.GetProperty("byStatus").EnumerateArray().Single(s => s.GetProperty("status").GetString() == "done").GetProperty("count").GetInt32());

        var r2 = Item(json, "r2");
        Assert.True(r2.GetProperty("student").GetProperty("isArchived").GetBoolean());
        Assert.Equal("SMS yuborildi", r2.GetProperty("stage").GetString());   // o'z ustuni
        Assert.Equal("overdue", r2.GetProperty("dueBucket").GetString());
        Assert.True(r2.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("ko'tarmadi", r2.GetProperty("lastResponse").GetString());
        Assert.Equal("Olim Operator", r2.GetProperty("lastActor").GetString());

        var r1 = Item(json, "r1");
        Assert.Equal("Bog'lanish kerak", r1.GetProperty("stage").GetString());   // StageId bo'sh → tizim ustuni
        Assert.Equal("nodate", r1.GetProperty("dueBucket").GetString());
        Assert.Equal(2, r1.GetProperty("student").GetProperty("phones").GetArrayLength());   // takrorsiz
        Assert.Equal("Qarzdorlik", r1.GetProperty("reason").GetProperty("label").GetString());
        Assert.Equal("Dilnoza O'qituvchi", r1.GetProperty("createdBy").GetString());
        Assert.Equal("today", Item(json, "r3").GetProperty("dueBucket").GetString());

        foreach (var secret in new[] { "olim.login", "XESH", "ochiq-parol" }) Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("all", null, null, null, new[] { "r2", "r3", "r4", "r5", "r8", "r1", "r7", "r6" })]   // yopilganlar oxirida, yangisi tepada
    [InlineData("done", null, null, null, new[] { "r6" })]
    [InlineData("failed", null, null, null, new[] { "r7" })]
    [InlineData("new", null, null, null, new[] { "r1" })]
    [InlineData(null, "todo", null, null, new[] { "r2", "r3", "r1" })]
    [InlineData(null, "overdue", null, null, new[] { "r2" })]
    [InlineData(null, "today", null, null, new[] { "r3" })]
    [InlineData(null, "tomorrow", null, null, new[] { "r4" })]
    [InlineData(null, "week", null, null, new[] { "r5" })]
    [InlineData(null, "later", null, null, new[] { "r8" })]
    [InlineData(null, "nodate", null, null, new[] { "r1" })]
    [InlineData("done", "todo", null, null, new string[0])]                                        // yakuniy talab muddat guruhida yo'q
    [InlineData(null, null, "rs1", null, new[] { "r3", "r1" })]
    [InlineData(null, null, "davom", null, new[] { "r2", "r4" })]
    [InlineData(null, null, null, "charos", new[] { "r3" })]
    [InlineData("all", null, null, "charos", new[] { "r3", "r6" })]
    public async Task Navbat_filtrlari(string? status, string? due, string? reason, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin()).ContactQueue(status, due, reason, search));

        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Navbat_sahifalash_va_notogri_parametr()
    {
        using var host = new McpTestHost();
        Seed(host);

        var first = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactQueue(page: 1, pageSize: 4));
        var second = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactQueue(page: 2, pageSize: 4));
        Assert.Equal(new[] { "r2", "r3", "r4", "r5" }, Ids(first));
        Assert.Equal(new[] { "r8", "r1" }, Ids(second));
        Assert.Equal(6, second.GetProperty("total").GetInt32());
        Assert.Equal(2, host.State.RowCount);

        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactQueue(status: "hammasi"));
        var ex = await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactQueue(due: "kecha"));
        Assert.Contains("due", ex.Message);
    }

    [Theory]
    [InlineData("students.list")]
    [InlineData("aiAccess")]
    [InlineData("tasks.board")]
    public async Task Navbat_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<OperationsTools>(McpTestHost.Staff(perm)).ContactQueue());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    // ================================================================== contact_report

    [Fact]
    public async Task Hisobot_davr_boyicha_sonlar_panel_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactReportTool());

        Assert.Equal(D(-6), json.GetProperty("from").GetString());
        Assert.Equal(D(0), json.GetProperty("to").GetString());
        var tot = json.GetProperty("totals");
        Assert.Equal(1, tot.GetProperty("created").GetInt32());
        Assert.Equal(4, tot.GetProperty("attempts").GetInt32());      // 30 kun oldingisi davrga kirmaydi
        Assert.Equal(2, tot.GetProperty("reached").GetInt32());       // ko'tarmagan va raqam ishlamagan — "bog'lanildi" emas
        Assert.Equal(1, tot.GetProperty("done").GetInt32());
        Assert.Equal(2, tot.GetProperty("callback").GetInt32());
        Assert.Equal(1, tot.GetProperty("failed").GetInt32());
        Assert.Equal(3, tot.GetProperty("withResponse").GetInt32());
        Assert.Equal(6, json.GetProperty("current").GetProperty("openNow").GetInt32());
        Assert.Equal(1, json.GetProperty("current").GetProperty("overdueNow").GetInt32());
        Assert.Equal(7, json.GetProperty("daily").GetArrayLength());

        var staff = json.GetProperty("byStaff").EnumerateArray().ToList();
        Assert.Equal(2, staff.Single(s => s.GetProperty("actorName").GetString() == "Olim Operator").GetProperty("attempts").GetInt32());
        Assert.Equal(2, staff.Single(s => s.GetProperty("actorName").GetString() == "Nodira Menejer").GetProperty("reached").GetInt32());
        Assert.Equal(2, json.GetProperty("byResult").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "answered").GetProperty("count").GetInt32());
        // Sabablar — talab OCHILGAN sana bo'yicha: r1 (kecha) va r3 (2 kun oldin) — ikkalasi Qarzdorlik.
        Assert.Equal(2, json.GetProperty("byReason").EnumerateArray().Single(r => r.GetProperty("reason").GetString() == "Qarzdorlik").GetProperty("created").GetInt32());
        Assert.Equal(2, json.GetProperty("topWords")[0].GetProperty("count").GetInt32());   // "to'lovni" — ikki javobda
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Hisobot_tanlangan_davr_va_chegara()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactReportTool(D(-30), D(-30)));
        Assert.Equal(1, json.GetProperty("totals").GetProperty("attempts").GetInt32());
        Assert.Equal(1, json.GetProperty("daily").GetArrayLength());

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).ContactReportTool(D(-400), D(0)));
    }

    [Theory]
    [InlineData(false)]   // admin — panelda ham hisobot tabi yo'q (qattiq rol tekshiruvi)
    [InlineData(true)]    // `contacts` ruxsatli xodim
    public async Task Hisobot_FAQAT_superadmin_uchun(bool staff)
    {
        using var host = new McpTestHost();
        Seed(host);
        var user = staff ? McpTestHost.Staff("contacts") : McpTestHost.Admin();

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<OperationsTools>(user).ContactReportTool());
        Assert.Empty(host.Sql);
    }

    // ================================================================== tasks_list

    [Fact]
    public async Task Topshiriqlar_sukut_boyicha_OCHIQLAR_arxivsiz_muddat_tartibida()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("tasks.dashboard")).TasksList();
        var json = McpTestHost.Parse(raw);

        // t4 bajarilgan, t5 arxivlangan doskada, t6 arxivlangan — kirmaydi; muddatsiz (t3) oxirida.
        Assert.Equal(new[] { "t1", "t2", "t3" }, Ids(json));
        Assert.Equal(3, json.GetProperty("total").GetInt32());

        var t1 = Item(json, "t1");
        Assert.Equal("Umumiy", t1.GetProperty("board").GetProperty("title").GetString());
        Assert.Equal("Yangi", t1.GetProperty("column").GetProperty("title").GetString());
        Assert.False(t1.GetProperty("column").GetProperty("isDone").GetBoolean());
        Assert.Equal("Olim Operator", t1.GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal("Nodira Menejer", t1.GetProperty("createdBy").GetString());
        Assert.Equal("Yuqori", t1.GetProperty("priorityLabel").GetString());
        Assert.True(t1.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(1, t1.GetProperty("checklist").GetProperty("done").GetInt32());
        Assert.Equal(3, t1.GetProperty("checklist").GetProperty("total").GetInt32());
        Assert.False(Item(json, "t2").GetProperty("isOverdue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Item(json, "t3").GetProperty("assignee").ValueKind);

        foreach (var secret in new[] { "olim.login", "nodira.login", "XESH", "ochiq-parol" }) Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Topshiriqlar_bajarilgani_yopiluvchi_ustundan_va_bajarilgan_vaqti_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin()).TasksList(state: "done"));
        var t4 = Item(json, "t4");

        Assert.Equal(new[] { "t4" }, Ids(json));
        Assert.True(t4.GetProperty("column").GetProperty("isDone").GetBoolean());
        Assert.False(t4.GetProperty("isOverdue").GetBoolean());      // muddati o'tgan, lekin bajarilgan
        Assert.StartsWith(D(-4), t4.GetProperty("completedAt").GetString());
    }

    [Theory]
    [InlineData(null, null, "overdue", null, null, null, null, new[] { "t1" })]
    [InlineData(null, null, "today", null, null, null, null, new[] { "t2" })]
    [InlineData(null, null, "all", null, null, null, null, new[] { "t4", "t1", "t2", "t3" })]
    [InlineData("b2", null, null, null, null, null, null, new[] { "t5" })]                    // arxiv doska ANIQ tanlansa ko'rinadi
    [InlineData(null, "u1", null, null, null, null, null, new[] { "t1" })]
    [InlineData(null, "none", null, null, null, null, null, new[] { "t3" })]
    [InlineData(null, null, null, "-3", "0", null, null, new[] { "t1", "t2" })]
    [InlineData(null, null, null, null, null, "hisobot", null, new[] { "t1" })]
    [InlineData(null, null, "all", null, null, null, true, new[] { "t4", "t1", "t2", "t5", "t6", "t3" })]
    public async Task Topshiriqlar_filtrlari(
        string? boardId, string? assigneeId, string? state, string? dueFromOffset, string? dueToOffset,
        string? search, bool? includeArchived, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);
        string? Off(string? o) => o is null ? null : D(int.Parse(o));

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Staff("tasks.board"))
            .TasksList(boardId, assigneeId, state, Off(dueFromOffset), Off(dueToOffset), search, includeArchived));

        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Topshiriqlar_sahifalash_va_ruxsat()
    {
        using var host = new McpTestHost();
        Seed(host);

        var p2 = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).TasksList(page: 2, pageSize: 2));
        Assert.Equal(new[] { "t3" }, Ids(p2));
        Assert.Equal(3, p2.GetProperty("total").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).TasksList(state: "yopiq"));
        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).TasksList(dueFrom: "01.10.2026"));

        using var denied = new McpTestHost();
        Seed(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => denied.Tool<OperationsTools>(McpTestHost.Staff("contacts")).TasksList());
        Assert.Empty(denied.Sql);
    }

    // ================================================================== messages_history

    [Fact]
    public async Task Elonlar_tarixi_eng_yangisi_tepada_matn_qirqilgan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Staff("messages.broadcast")).MessagesHistory());

        Assert.Equal(new[] { "bc2", "bc1", "bc3" }, Ids(json));
        var bc2 = Item(json, "bc2");
        Assert.Equal(OperationsTools.MaxBroadcastText, bc2.GetProperty("text").GetString()!.Length);
        Assert.True(bc2.GetProperty("truncated").GetBoolean());
        Assert.Equal(50, bc2.GetProperty("recipients").GetInt32());
        Assert.Equal(48, bc2.GetProperty("sent").GetInt32());
        Assert.Equal("Barcha guruhlar — qarzdorlar", bc2.GetProperty("audience").GetString());
        var bc1 = Item(json, "bc1");
        Assert.False(bc1.GetProperty("truncated").GetBoolean());
        Assert.Equal("Nodira Menejer", bc1.GetProperty("sender").GetString());
        Assert.StartsWith(D(-3), bc1.GetProperty("sentAt").GetString());
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData(-5, null, null, new[] { "bc2", "bc1" })]
    [InlineData(null, -2, null, new[] { "bc1", "bc3" })]
    [InlineData(-3, -3, null, new[] { "bc1" })]                // "gacha" kuni ham kiradi
    [InlineData(null, null, "qarzdor", new[] { "bc2" })]        // auditoriya bo'yicha
    [InlineData(null, null, "dars", new[] { "bc1" })]           // matn bo'yicha
    [InlineData(null, null, "olim", new[] { "bc3" })]           // yuboruvchi bo'yicha
    public async Task Elonlar_filtrlari(int? fromOffset, int? toOffset, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin()).MessagesHistory(
            fromOffset is null ? null : D(fromOffset.Value), toOffset is null ? null : D(toOffset.Value), search));

        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Elonlar_sahifalash_va_ruxsat()
    {
        using var host = new McpTestHost();
        Seed(host);

        var p2 = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).MessagesHistory(page: 2, pageSize: 2));
        Assert.Equal(new[] { "bc3" }, Ids(p2));
        Assert.Equal(3, p2.GetProperty("total").GetInt32());
        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).MessagesHistory(D(0), D(-1)));

        using var denied = new McpTestHost();
        Seed(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => denied.Tool<OperationsTools>(McpTestHost.Staff("contacts")).MessagesHistory());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => denied.Tool<OperationsTools>(McpTestHost.Staff("contacts")).AutoMessageRules());
        Assert.Empty(denied.Sql);
    }

    // ================================================================== auto_message_rules

    [Fact]
    public async Task Avto_xabar_qoidalari_SMS_sozlamasisiz_va_qoidasiz_hodisalar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("messages.broadcast")).AutoMessageRules();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "am1", "am2", "am3" }, Ids(json));
        var am1 = Item(json, "am1");
        Assert.Equal("payment_debt", am1.GetProperty("trigger").GetProperty("key").GetString());
        Assert.Equal("Qarzdorlik eslatmasi", am1.GetProperty("trigger").GetProperty("label").GetString());
        Assert.Equal("Moliya", am1.GetProperty("trigger").GetProperty("category").GetString());
        Assert.True(am1.GetProperty("sendTelegram").GetBoolean());
        Assert.Equal("parents", am1.GetProperty("audience").GetString());
        Assert.Contains("{qarzdorlik}", am1.GetProperty("template").GetString());
        var am3 = Item(json, "am3").GetProperty("schedule");
        Assert.True(am3.GetProperty("usesSchedule").GetBoolean());
        Assert.Equal("monthly", am3.GetProperty("type").GetString());
        Assert.Equal(5, am3.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal("10:30", am3.GetProperty("time").GetString());
        // 15 hodisa − 3 qoida (2026-10: lid hodisalari olib tashlandi; 2026-10-12 CRM upgrade P2.5: +5 Telegram hodisasi)
        Assert.Equal(WunderkindLC.Application.Services.AutoMessageTriggers.All.Length - 3,
            json.GetProperty("triggersWithoutRule").GetArrayLength());
        Assert.Equal(12, json.GetProperty("triggersWithoutRule").GetArrayLength());

        // SMS/push kanallari va SMS provayderi chiqmaydi (o'chirilayotgan modullar).
        Assert.DoesNotContain("maxfiy-provayder", raw);
        Assert.DoesNotContain("sendSms", raw);
        Assert.DoesNotContain("sendPush", raw);
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"SmsProvider\"", StringComparison.Ordinal));
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Avto_xabar_filtrlari()
    {
        using var host = new McpTestHost();
        Seed(host);
        OperationsTools Tool() => host.Tool<OperationsTools>(McpTestHost.Admin());

        Assert.Equal(new[] { "am2" }, Ids(McpTestHost.Parse(await Tool().AutoMessageRules(enabled: false))));
        Assert.Equal(new[] { "am1" }, Ids(McpTestHost.Parse(await Tool().AutoMessageRules(trigger: "payment_debt"))));
        var finance = McpTestHost.Parse(await Tool().AutoMessageRules(category: "Moliya"));
        Assert.Equal(new[] { "am1" }, Ids(finance));
        Assert.All(finance.GetProperty("triggersWithoutRule").EnumerateArray(),
            x => Assert.Equal("Moliya", x.GetProperty("category").GetString()));
        await Assert.ThrowsAsync<McpException>(() => Tool().AutoMessageRules(trigger: "sms_yubor"));
    }

    // ================================================================== audit_log

    [Fact]
    public async Task Tarix_eng_yangisi_tepada_bolim_bilan_va_SNAPSHOTsiz()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).AuditLogTool();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "a2", "a1", "a3", "a4", "a5", "a6" }, Ids(json));
        Assert.Equal(6, json.GetProperty("total").GetInt32());
        var a3 = Item(json, "a3");
        Assert.Equal("classes", a3.GetProperty("section").GetString());           // Membership → Guruhlar
        Assert.Equal("Guruhlar", a3.GetProperty("sectionLabel").GetString());
        Assert.Equal("g1:s1", a3.GetProperty("entityId").GetString());
        Assert.Equal("other", Item(json, "a5").GetProperty("section").GetString());   // xaritada yo'q tur
        Assert.Equal("To'lov qabul qilindi: 500 000 so'm", Item(json, "a2").GetProperty("summary").GetString());

        // Eski/yangi qiymat snapshotlari umuman o'qilmaydi.
        Assert.DoesNotContain("maxfiy-oldin", raw);
        Assert.DoesNotContain("maxfiy-keyin", raw);
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"Before\"", StringComparison.Ordinal) || s.Contains("\"After\"", StringComparison.Ordinal));
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("students", null, null, null, null, null, null, null, new[] { "a1" })]
    [InlineData("classes", null, null, null, null, null, null, null, new[] { "a3" })]
    [InlineData("finance", null, null, null, null, null, null, null, new[] { "a2" })]
    [InlineData("other", null, null, null, null, null, null, null, new[] { "a5" })]
    [InlineData(null, "delete", null, null, null, null, null, null, new[] { "a5", "a6" })]
    [InlineData(null, null, "olim", null, null, null, null, null, new[] { "a1", "a3", "a6" })]
    [InlineData(null, null, null, -3, -1, null, null, null, new[] { "a1", "a3", "a4" })]
    [InlineData(null, null, null, null, null, "maosh", null, null, new[] { "a4" })]
    [InlineData(null, null, null, null, null, null, "s1", null, new[] { "a2", "a1", "a3" })]
    [InlineData(null, null, null, null, null, null, null, "t1", new[] { "a4" })]
    public async Task Tarix_filtrlari(
        string? section, string? action, string? actor, int? fromOffset, int? toOffset, string? search,
        string? studentId, string? teacherId, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin()).AuditLogTool(
            section, action, actor, fromOffset is null ? null : D(fromOffset.Value), toOffset is null ? null : D(toOffset.Value),
            search, studentId, teacherId));

        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Tarix_sahifalash_va_notogri_parametr()
    {
        using var host = new McpTestHost();
        Seed(host);

        var p3 = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).AuditLogTool(page: 3, pageSize: 2));
        Assert.Equal(new[] { "a5", "a6" }, Ids(p3));
        Assert.Equal(6, p3.GetProperty("total").GetInt32());

        var ex = await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).AuditLogTool(section: "moliya"));
        Assert.Contains("finance", ex.Message);
        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.SuperAdmin()).AuditLogTool(action: "pay"));
    }

    [Fact]
    public async Task Tarix_audit_ruxsati_BUTUN_tarixni_ochadi_panel_bilan_bir_xil()
    {
        // audit.md §3: `audit` = BUTUN tarix (to'lov summalari ham) — ATAYIN; AI panel ko'rsatganidan ko'p ham, kam ham ko'rmaydi.
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Staff("audit")).AuditLogTool());

        Assert.Equal(6, json.GetProperty("total").GetInt32());
        Assert.Equal("To'lov qabul qilindi: 500 000 so'm", Item(json, "a2").GetProperty("summary").GetString());
        Assert.Equal("O'quvchi ma'lumoti o'zgardi: Ali Valiyev", Item(json, "a1").GetProperty("summary").GetString());
    }

    [Theory]
    [InlineData("finance.main")]
    [InlineData("students.list")]
    [InlineData("contacts")]
    public async Task Tarix_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<OperationsTools>(McpTestHost.Staff(perm)).AuditLogTool());
        Assert.Empty(host.Sql);
    }
}
