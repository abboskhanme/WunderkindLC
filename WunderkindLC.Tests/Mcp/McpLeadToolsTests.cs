using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>MCP: `leads_list`, `lead_detail`, `leads_funnel`, `lead_stages` — natija, filtrlar, ruxsat va maxfiylik.</summary>
public class McpLeadToolsTests
{
    /// <summary>Bugungi kundan siljigan sana (testlar sanaga bog'lanmasin).</summary>
    private static string D(int days) => McpToolContext.Iso(McpToolContext.Today.AddDays(days));

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.LeadStages.AddRange(
            new LeadStage { Id = "st1", Title = "Yangi", Color = "blue", Order = 0 },
            new LeadStage { Id = "st2", Title = "Qo'ng'iroq qilindi", Color = "amber", Order = 1 },
            new LeadStage { Id = "st3", Title = "Sinovga yozildi", Color = "emerald", Order = 2 });
        db.LeadSources.AddRange(
            new LeadSource { Id = "src1", Name = "Instagram", Order = 0 },
            new LeadSource { Id = "src2", Name = "Telegram", Order = 1 });
        db.Subjects.AddRange(new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" });
        db.Teachers.Add(new Teacher { Id = "t1", FullName = "Dilnoza Karimova" });
        db.Classes.AddRange(
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1" },
            new Group { Id = "g2", Name = "Matematika 5", CourseId = "c2", TeacherId = "t1" });
        db.Users.AddRange(
            new AppUser { Id = "u-op1", FullName = "Olim Operator", Role = Roles.Staff, Email = "olim.login", PasswordHash = "XESH-1" },
            new AppUser
            {
                Id = "u-op2", FullName = "Nodira Menejer", Role = Roles.Staff, Email = "nodira.login",
                PasswordHash = "XESH-2", InitialPassword = "ochiq-parol",
            });
        db.Students.Add(new Student { Id = "s-conv", FullName = "Bobur Sinov" });

        db.Leads.AddRange(
            new Lead
            {
                Id = "L1", FullName = "Ali Yangi", Phone = "+998 90 111-22-33", FatherFullName = "Vali Yangi",
                FatherPhone = "+998935556677", Source = "Instagram", InterestSubject = "Ingliz tili", Stage = "st1",
                AssigneeUserId = "u-op1", CreatedAt = D(-2) + "T10:00:00", RepeatCount = 1, LastRepeatAt = D(-1) + "T09:00:00",
                AnswersJson = "[{\"question\":\"Qaysi vaqt qulay?\",\"answers\":[\"Kechqurun\"]}]", Note = "Onasi qo'ng'iroq qiladi",
            },
            // Manba ESKI yozuvdagidek ID bilan saqlangan; kurs bo'sh — sinov guruhining kursi olinadi.
            new Lead
            {
                Id = "L2", FullName = "Bobur Sinov", Phone = "+998901234567", Source = "src2", InterestSubject = "",
                Stage = "st3", AssigneeUserId = "u-op2", CreatedAt = D(-5) + "T09:00:00", ConvertedStudentId = "s-conv",
                ClosedByUserId = "u-op2", ClosedAt = D(-1),
            },
            new Lead
            {
                Id = "L3", FullName = "Charos Yo'qotilgan", Phone = "+998907770011", Source = "Instagram",
                InterestSubject = "ingliz tili", Stage = "st2", CreatedAt = D(-10) + "T12:00:00", RepeatCount = 2,
                LostAt = D(-1) + "T11:00:00", LostReason = "Narx qimmat", LostComment = "Boshqa markazga ketdi",
                LostByName = "Olim Operator",
            },
            // Bosqichi o'chirilgan (kanbanda birinchi ustunda), manbasiz, 60 kun oldin kelgan.
            new Lead { Id = "L4", FullName = "Davron Eski", Phone = "+998901110000", Source = "", Stage = "o'chirilgan", CreatedAt = D(-60) + "T08:00:00" },
            new Lead
            {
                Id = "L5", FullName = "Eldor Ketgan", Phone = "+998909990000", Source = "Telegram", InterestSubject = "Matematika",
                Stage = "st1", CreatedAt = D(-3) + "T08:00:00", LostAt = D(0) + "T08:00:00", LostReason = "Vaqt to'g'ri kelmadi",
            });

        db.LeadEvents.Add(new LeadEvent { LeadId = "L1", Type = "created", Text = "Lid qo'shildi", ActorName = "Olim Operator", ActorUserId = "u-op1", ToStage = "st1", CreatedAt = D(-2) + "T10:00:00" });
        for (var i = 0; i < 105; i++)
            db.LeadEvents.Add(new LeadEvent { LeadId = "L1", Type = "note", Text = $"Izoh {i}", ActorName = "Olim Operator", ActorUserId = "u-op1", CreatedAt = $"{D(-1)}T10:{i / 60:00}:{i % 60:00}" });
        db.LeadEvents.AddRange(
            new LeadEvent { LeadId = "L2", Type = "created", Text = "Lid qo'shildi", ActorName = "Nodira Menejer", ActorUserId = "u-op2", ToStage = "st1", CreatedAt = D(-5) + "T09:00:00" },
            new LeadEvent { LeadId = "L2", Type = "stage", Text = "Bosqich: Sinovga yozildi", ActorName = "Nodira Menejer", ActorUserId = "u-op2", FromStage = "st1", ToStage = "st3", CreatedAt = D(-4) + "T09:00:00" },
            new LeadEvent { LeadId = "L2", Type = "convert", Text = "O'quvchiga aylantirildi", ActorName = "Nodira Menejer", ActorUserId = "u-op2", CreatedAt = D(-1) + "T12:00:00" },
            new LeadEvent { LeadId = "L3", Type = "created", Text = "Formadan keldi", ActorName = "Lid formasi", ToStage = "st1", CreatedAt = D(-10) + "T12:00:00" },
            new LeadEvent { LeadId = "L3", Type = "stage", Text = "Bosqich: Qo'ng'iroq qilindi", ActorName = "Olim Operator", ActorUserId = "u-op1", FromStage = "st1", ToStage = "st2", CreatedAt = D(-9) + "T12:00:00" },
            new LeadEvent { LeadId = "L3", Type = "lost", Text = "Yo'qotildi: Narx qimmat", ActorName = "Olim Operator", ActorUserId = "u-op1", FromStage = "st2", CreatedAt = D(-1) + "T11:00:00" },
            new LeadEvent { LeadId = "L5", Type = "created", Text = "Daraja testi orqali keldi", ActorName = "Daraja testi", ToStage = "st1", CreatedAt = D(-3) + "T08:00:00" });

        db.TrialLessons.AddRange(
            new TrialLesson { Id = "T1", LeadId = "L1", GroupId = "g1", ScheduledAt = D(1) + "T15:00", Result = "pending", CreatedAt = D(-2) },
            new TrialLesson { Id = "T2", LeadId = "L2", GroupId = "g2", ScheduledAt = D(-1) + "T15:00", Result = "stayed", AttendedAt = D(-1), CreatedAt = D(-4) },
            new TrialLesson { Id = "T3", LeadId = "L3", GroupId = "g1", ScheduledAt = D(-8) + "T15:00", Result = "no_show", CreatedAt = D(-9) });

        db.LevelTests.Add(new LevelTest { Id = "lt1", Title = "Ingliz tili daraja testi", CourseId = "c1", Slug = "maxfiy-slug" });
        db.LevelTestSubmissions.Add(new LevelTestSubmission
        {
            TestId = "lt1", LeadId = "L1", FullName = "Ali Yangi", Phone = "+998901112233", Score = 15, Total = 20,
            Percent = 75, Level = "B1", CreatedAt = D(-2) + "T09:00:00", SurveyJson = "[{\"q\":\"maxfiy-sorovnoma\",\"a\":[\"x\"]}]",
        });

        db.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = "s-conv", Direction = "income", Category = "tuition", Amount = 500_000, Date = D(-1) },
            new FinanceTransaction { StudentId = "s-conv", Direction = "expense", Category = "refund", Amount = 100_000, Date = D(0) });
        db.SaveChanges();
    }

    private static List<string?> Ids(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();

    private static JsonElement Item(JsonElement json, string id) =>
        json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

    // ------------------------------------------------------------------ leads_list

    [Fact]
    public async Task List_sukut_boyicha_faqat_OCHIQ_lidlar_eng_yangisi_tepada()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsList();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(2, json.GetProperty("total").GetInt32());              // L2 aylantirilgan, L3/L5 yo'qotilgan
        Assert.Equal(["L1", "L4"], Ids(json));
        var ali = Item(json, "L1");
        Assert.Equal("open", ali.GetProperty("state").GetString());
        Assert.Equal("Yangi", ali.GetProperty("stage").GetProperty("title").GetString());
        Assert.Equal("Instagram", ali.GetProperty("source").GetString());
        Assert.Equal("Ingliz tili", ali.GetProperty("interestCourse").GetString());
        Assert.Equal("Olim Operator", ali.GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal(1, ali.GetProperty("repeatCount").GetInt32());
        var trial = ali.GetProperty("nextTrialLesson");
        Assert.Equal(D(1) + "T15:00", trial.GetProperty("at").GetString());
        Assert.Equal("English A1", trial.GetProperty("groupName").GetString());
        Assert.False(trial.GetProperty("isPast").GetBoolean());

        // O'chirilgan bosqichdagi lid — kanbandagidek birinchi ustunda; manbasi yo'q.
        var davron = Item(json, "L4");
        Assert.Equal("st1", davron.GetProperty("stage").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, davron.GetProperty("source").ValueKind);
        Assert.Equal(JsonValueKind.Null, davron.GetProperty("assignee").ValueKind);

        foreach (var secret in new[] { "XESH", "olim.login", "ochiq-parol" })
            Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(LeadTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task List_aylantirilgan_va_yoqotilgan_qatorlari()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.Staff("leads.list")).LeadsList(state: "all"));

        var bobur = Item(json, "L2");
        Assert.Equal("converted", bobur.GetProperty("state").GetString());
        Assert.Equal("s-conv", bobur.GetProperty("convertedStudentId").GetString());
        Assert.Equal("Telegram", bobur.GetProperty("source").GetString());           // eski yozuv: manba ID → nomi
        Assert.Equal("Matematika", bobur.GetProperty("interestCourse").GetString()); // kurs bo'sh → sinov guruhining kursi
        Assert.Equal(JsonValueKind.Null, bobur.GetProperty("nextTrialLesson").ValueKind);
        Assert.Equal("Nodira Menejer", bobur.GetProperty("assignee").GetProperty("name").GetString());

        var charos = Item(json, "L3");
        Assert.Equal("lost", charos.GetProperty("state").GetString());
        Assert.Equal("Narx qimmat", charos.GetProperty("lost").GetProperty("reason").GetString());
        Assert.Equal("Qo'ng'iroq qilindi", charos.GetProperty("stage").GetProperty("title").GetString()); // bosqich saqlanadi
        Assert.Equal(JsonValueKind.Null, charos.GetProperty("convertedStudentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, Item(json, "L5").GetProperty("nextTrialLesson").ValueKind);
    }

    [Theory]
    [InlineData("ali", "all", null, null, null, null, null, null, new[] { "L1" })]
    [InlineData("901112233", "all", null, null, null, null, null, null, new[] { "L1" })]          // formatli telefon
    [InlineData("+998 93 555 66 77", "all", null, null, null, null, null, null, new[] { "L1" })]  // ota telefoni
    [InlineData(null, "lost", null, null, null, null, null, null, new[] { "L3", "L5" })]
    [InlineData(null, "converted", null, null, null, null, null, null, new[] { "L2" })]
    [InlineData(null, "all", null, null, null, null, null, null, new[] { "L1", "L2", "L3", "L4", "L5" })]
    [InlineData(null, "all", "st1", null, null, null, null, null, new[] { "L1", "L4", "L5" })]    // o'chirilgan bosqich → birinchi
    [InlineData(null, "all", "st2", null, null, null, null, null, new[] { "L3" })]
    [InlineData(null, null, "st1", null, null, null, null, null, new[] { "L1", "L4" })]
    [InlineData(null, "all", null, "Telegram", null, null, null, null, new[] { "L2", "L5" })]      // ID bilan saqlangan ham
    [InlineData(null, "all", null, "instagram", null, null, null, null, new[] { "L1", "L3" })]
    [InlineData(null, "all", null, "unknown", null, null, null, null, new[] { "L4" })]
    [InlineData(null, "all", null, null, "u-op1", null, null, null, new[] { "L1" })]
    [InlineData(null, "all", null, null, "none", null, null, null, new[] { "L3", "L4", "L5" })]
    [InlineData(null, "all", null, null, null, "Matematika", null, null, new[] { "L2", "L5" })]   // L2 — sinov guruhi orqali
    [InlineData(null, "all", null, null, null, "Ingliz tili", null, null, new[] { "L1", "L3" })]
    [InlineData(null, "all", null, null, null, null, -7, null, new[] { "L1", "L2", "L5" })]
    [InlineData(null, "all", null, null, null, null, null, -4, new[] { "L2", "L3", "L4" })]
    [InlineData(null, "all", null, null, null, null, -7, -3, new[] { "L2", "L5" })]
    public async Task List_filtrlari(
        string? search, string? state, string? stageId, string? source, string? assigneeId, string? course,
        int? fromDays, int? toDays, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsList(
            search, state, stageId, source, assigneeId, course,
            fromDays is { } f ? D(f) : null, toDays is { } t ? D(t) : null));

        Assert.Equal(expected, Ids(json).OrderBy(x => x));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task List_sahifalash()
    {
        using var host = new McpTestHost();
        Seed(host);

        var first = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsList(state: "all", page: 1, pageSize: 2));
        var third = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsList(state: "all", page: 3, pageSize: 2));

        Assert.Equal(["L1", "L5"], Ids(first));
        Assert.Equal(["L4"], Ids(third));
        Assert.Equal(5, third.GetProperty("total").GetInt32());
        Assert.Equal(1, host.State.RowCount);
    }

    [Fact]
    public async Task List_notogri_parametr_tushunarli_xato_beradi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tool = host.Tool<LeadTools>(McpTestHost.SuperAdmin());

        Assert.Contains("state", (await Assert.ThrowsAsync<McpException>(() => tool.LeadsList(state: "hammasi"))).Message);
        Assert.Contains("stageId", (await Assert.ThrowsAsync<McpException>(() => tool.LeadsList(stageId: "yoq"))).Message);
        await Assert.ThrowsAsync<McpException>(() => tool.LeadsList(createdFrom: "2026/01/01"));
        await Assert.ThrowsAsync<McpException>(() => tool.LeadsList(createdFrom: D(0), createdTo: D(-1)));
    }

    [Theory]
    [InlineData("aiAccess")]
    [InlineData("students.list")]
    [InlineData("leads.stats")]
    [InlineData("leads.forms")]
    public async Task Lidlar_bolimi_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<LeadTools>(McpTestHost.Staff(perm)).LeadsList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<LeadTools>(McpTestHost.Staff(perm)).LeadDetail("L1"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<LeadTools>(McpTestHost.Staff(perm)).LeadStages());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);   // rad etilganda bazaga umuman so'rov ketmaydi
    }

    // ------------------------------------------------------------------ lead_detail

    [Fact]
    public async Task Detail_tarix_sinov_darsi_daraja_testi_va_javoblar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<LeadTools>(McpTestHost.Staff("leads.list")).LeadDetail("L1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("Ali Yangi", json.GetProperty("lead").GetProperty("fullName").GetString());
        Assert.Equal("+998935556677", json.GetProperty("contact").GetProperty("father").GetProperty("phone").GetString());
        Assert.Equal("Onasi qo'ng'iroq qiladi", json.GetProperty("note").GetString());

        // Tarix: eng yangisi tepada, 100 ta bilan cheklangan, jami soni ochiq.
        Assert.Equal(106, json.GetProperty("eventsTotal").GetInt32());
        Assert.Equal(LeadTools.MaxEvents, json.GetProperty("events").GetArrayLength());
        Assert.Equal("Izoh 104", json.GetProperty("events")[0].GetProperty("text").GetString());
        Assert.Equal("Olim Operator", json.GetProperty("events")[0].GetProperty("actor").GetString());

        var trial = json.GetProperty("trials").EnumerateArray().Single();
        Assert.Equal("English A1", trial.GetProperty("groupName").GetString());
        Assert.Equal("Dilnoza Karimova", trial.GetProperty("teacher").GetString());
        Assert.Equal("Ingliz tili", trial.GetProperty("course").GetString());
        Assert.Equal("pending", trial.GetProperty("result").GetString());
        Assert.False(trial.GetProperty("attended").GetBoolean());

        var test = json.GetProperty("levelTests").EnumerateArray().Single();
        Assert.Equal("Ingliz tili daraja testi", test.GetProperty("test").GetString());
        Assert.Equal(15, test.GetProperty("score").GetInt32());
        Assert.Equal(75, test.GetProperty("percent").GetInt32());
        Assert.Equal("B1", json.GetProperty("level").GetString());   // lidda daraja yo'q → oxirgi test natijasi

        var answer = json.GetProperty("entryFormAnswers").EnumerateArray().Single();
        Assert.Equal("Qaysi vaqt qulay?", answer.GetProperty("question").GetString());
        Assert.Equal("Kechqurun", answer.GetProperty("answers")[0].GetString());

        // Login/parol, test havolasi (slug) va so'rovnoma matni chiqmaydi.
        foreach (var secret in new[] { "XESH", "olim.login", "ochiq-parol", "maxfiy-slug", "maxfiy-sorovnoma" })
            Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(LeadTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Detail_aylantirilgan_va_yoqotilgan_lid()
    {
        using var host = new McpTestHost();
        Seed(host);

        var bobur = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail("L2"));
        Assert.Equal("converted", bobur.GetProperty("lead").GetProperty("state").GetString());
        Assert.Equal("Nodira Menejer", bobur.GetProperty("closed").GetProperty("by").GetString());
        Assert.True(bobur.GetProperty("trials")[0].GetProperty("attended").GetBoolean());   // "stayed" — sinovga KELGAN
        Assert.Equal(JsonValueKind.Null, bobur.GetProperty("lostDetail").ValueKind);

        var charos = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail("L3"));
        var lost = charos.GetProperty("lostDetail");
        Assert.Equal("Narx qimmat", lost.GetProperty("reason").GetString());
        Assert.Equal("Boshqa markazga ketdi", lost.GetProperty("comment").GetString());
        Assert.Equal("Olim Operator", lost.GetProperty("byName").GetString());
        Assert.Equal(JsonValueKind.Null, charos.GetProperty("level").ValueKind);
    }

    [Fact]
    public async Task Detail_telefon_yoki_ism_boyicha_topadi_kopi_bolsa_nomzodlar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var byPhone = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail(phone: "90 123 45 67"));
        Assert.Equal("L2", byPhone.GetProperty("lead").GetProperty("id").GetString());

        var byName = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail(name: "yangi"));
        Assert.Equal("L1", byName.GetProperty("lead").GetProperty("id").GetString());

        var many = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail(name: "an"));
        Assert.Equal(3, many.GetProperty("candidates").GetArrayLength());   // Ali Yangi, Charos Yo'qotilgan, Eldor Ketgan

        var none = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail("yoq-id"));
        Assert.Contains("topilmadi", none.GetProperty("message").GetString());
        Assert.Equal(0, host.State.RowCount);

        await Assert.ThrowsAsync<McpException>(() => host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail());
        await Assert.ThrowsAsync<McpException>(() => host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail(phone: "1234"));
    }

    // ------------------------------------------------------------------ leads_funnel

    [Fact]
    public async Task Funnel_davrdagi_lidlar_voronka_manba_sinov_yoqotish_va_pul()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<LeadTools>(McpTestHost.Staff("leads.stats", "finance.main")).LeadsFunnel();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(D(-29), json.GetProperty("from").GetString());
        Assert.Equal(4, json.GetProperty("total").GetInt32());              // L4 (60 kun oldin) kirmaydi
        Assert.Equal(1, json.GetProperty("converted").GetInt32());
        Assert.Equal(25, json.GetProperty("conversionRate").GetInt32());
        Assert.Equal(1, json.GetProperty("paid").GetInt32());
        Assert.Equal(400_000, json.GetProperty("revenue").GetDecimal());   // to'lov − vozvrat
        Assert.Equal(25, json.GetProperty("payRate").GetInt32());

        var stages = json.GetProperty("stages").EnumerateArray().ToList();
        Assert.Equal(["st1", "st2", "st3"], stages.Select(s => s.GetProperty("stageId").GetString()));
        Assert.Equal([4, 2, 1], stages.Select(s => s.GetProperty("reached").GetInt32()));
        Assert.Equal([2, 1, 1], stages.Select(s => s.GetProperty("currentCount").GetInt32()));
        Assert.Equal([1, 1, 0], stages.Select(s => s.GetProperty("currentLost").GetInt32()));   // yo'qotilgan o'z bosqichida
        Assert.Equal(24.0, stages[0].GetProperty("avgHoursInStage").GetDouble());
        Assert.Equal(2, stages[0].GetProperty("avgHoursSamples").GetInt32());
        Assert.Equal(JsonValueKind.Null, stages[1].GetProperty("avgHoursInStage").ValueKind);
        Assert.Equal(0, json.GetProperty("leadsInDeletedStage").GetInt32());

        // Manbalar: ID bilan saqlangan eski yozuv ("src2") nomi bo'yicha birlashadi.
        var sources = json.GetProperty("sources").EnumerateArray()
            .ToDictionary(s => s.GetProperty("label").GetString()!, s => s.GetProperty("count").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["Instagram"] = 2, ["Telegram"] = 2 }, sources);

        var conv = json.GetProperty("conversion");
        Assert.Equal(3, conv.GetProperty("trialScheduled").GetInt32());
        Assert.Equal(1, conv.GetProperty("trialAttended").GetInt32());
        Assert.Equal(75, conv.GetProperty("trialScheduledPct").GetInt32());
        Assert.Equal(1, conv.GetProperty("converted").GetInt32());

        var lost = json.GetProperty("lost");
        Assert.Equal(2, lost.GetProperty("count").GetInt32());
        Assert.Equal(50, lost.GetProperty("rate").GetDouble());
        Assert.Equal(["Narx qimmat", "Vaqt to'g'ri kelmadi"],
            lost.GetProperty("byReason").EnumerateArray().Select(r => r.GetProperty("reason").GetString()).OrderBy(x => x));

        var courses = json.GetProperty("courses").EnumerateArray().ToList();
        Assert.Equal("Ingliz tili", courses[0].GetProperty("course").GetString());   // "ingliz tili" ham shu kursga
        Assert.Equal(2, courses[0].GetProperty("leads").GetInt32());
        Assert.Contains(courses, c => c.GetProperty("course").GetString() == "Ko'rsatilmagan" && c.GetProperty("converted").GetInt32() == 1);

        var managers = json.GetProperty("managers").EnumerateArray().ToList();
        Assert.Equal("Nodira Menejer", managers[0].GetProperty("name").GetString());   // tushum bo'yicha birinchi
        Assert.Equal(1, managers[0].GetProperty("won").GetInt32());
        Assert.Equal(400_000, managers[0].GetProperty("revenue").GetDecimal());
        var olim = managers.Single(m => m.GetProperty("name").GetString() == "Olim Operator");
        Assert.Equal(1, olim.GetProperty("moves").GetInt32());
        Assert.Equal(2, olim.GetProperty("leads").GetInt32());

        foreach (var secret in new[] { "XESH", "olim.login", "ochiq-parol" })
            Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(LeadTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Funnel_moliya_ruxsatisiz_pul_YOQ_va_tolovlar_OQILMAYDI()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<LeadTools>(McpTestHost.Staff("leads.stats")).LeadsFunnel();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(JsonValueKind.Null, json.GetProperty("paid").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("revenue").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("managers")[0].GetProperty("revenue").ValueKind);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("note").GetString()));
        Assert.DoesNotContain("400000", raw);
        Assert.Equal(1, json.GetProperty("converted").GetInt32());   // pulsiz ko'rsatkichlar joyida

        var finance = host.Rw.Model.FindEntityType(typeof(FinanceTransaction))!.GetTableName()!;
        Assert.DoesNotContain(finance, host.TablesRead());
    }

    [Fact]
    public async Task Funnel_davr_tanlash_va_chegaralar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var wide = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsFunnel(D(-60), D(0)));
        Assert.Equal(5, wide.GetProperty("total").GetInt32());
        Assert.Equal(1, wide.GetProperty("leadsInDeletedStage").GetInt32());   // L4

        var narrow = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsFunnel(D(-3), D(-2)));
        Assert.Equal(2, narrow.GetProperty("total").GetInt32());                // L1, L5 (chegara kunlari kiradi)

        await Assert.ThrowsAsync<McpException>(() => host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsFunnel(D(0), D(-1)));
        await Assert.ThrowsAsync<McpException>(() => host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsFunnel(D(-400), D(0)));
    }

    [Theory]
    [InlineData("leads.list")]
    [InlineData("aiAccess")]
    [InlineData("finance.main")]
    public async Task Funnel_CRM_statistika_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<LeadTools>(McpTestHost.Staff(perm)).LeadsFunnel());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    // ------------------------------------------------------------------ lead_stages

    [Fact]
    public async Task Stages_tartib_ustun_sanoqlari_manbalar_va_kurslar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<LeadTools>(McpTestHost.Staff("leads.list")).LeadStages());
        var stages = json.GetProperty("stages").EnumerateArray().ToList();

        Assert.Equal(["Yangi", "Qo'ng'iroq qilindi", "Sinovga yozildi"], stages.Select(s => s.GetProperty("title").GetString()));
        Assert.True(stages[0].GetProperty("isFirst").GetBoolean());
        Assert.False(stages[1].GetProperty("isFirst").GetBoolean());
        // Kanban: yo'qotilmagan lidlar (aylantirilgani ham); o'chirilgan bosqichdagi L4 — birinchi ustunda.
        Assert.Equal([2, 0, 1], stages.Select(s => s.GetProperty("cards").GetInt32()));
        Assert.Equal([2, 0, 0], stages.Select(s => s.GetProperty("openLeads").GetInt32()));
        Assert.Equal(2, json.GetProperty("lostColumn").GetProperty("cards").GetInt32());

        Assert.Equal(["Instagram", "Telegram"],
            json.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["Ingliz tili", "Matematika"],
            json.GetProperty("courses").EnumerateArray().Select(s => s.GetString()));
        host.AssertReadsOnlyDeclared(typeof(LeadTools));
        host.AssertNoSecretColumns();
    }

    // ------------------------------------------------------------------ faqat o'qish

    [Fact]
    public async Task Lid_vositalari_bazada_HECH_NARSANI_ozgartirmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        string Snapshot() => string.Join("|",
            host.Rw.Leads.OrderBy(l => l.Id)
                .Select(l => l.Id + ":" + l.Stage + ":" + l.LostAt + ":" + l.ConvertedStudentId + ":" + l.RepeatCount)
                .AsEnumerable()
                .Append("events=" + host.Rw.LeadEvents.Count())
                .Append("trials=" + string.Join(",", host.Rw.TrialLessons.OrderBy(x => x.Id).Select(x => x.Id + x.Result))));
        var before = Snapshot();

        await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsList(state: "all");
        await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadDetail("L1");
        await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadsFunnel(D(-60), D(0));
        await host.Tool<LeadTools>(McpTestHost.SuperAdmin()).LeadStages();

        host.Rw.ChangeTracker.Clear();
        Assert.Equal(before, Snapshot());
    }
}
