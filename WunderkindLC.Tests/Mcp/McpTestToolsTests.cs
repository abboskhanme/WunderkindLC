using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `tests_list`, `test_scores`, `student_tests`, `rating_group`, `rating_center` — panel bilan bir xil
/// raqamlar (TestResultService / StudentBallService / RatingService), filtrlar, sahifalash, ruxsat, maxfiylik.
/// Onlayn test qismi (bot PDF, kalit, kod, tashqi ishtirokchilar) chiqmasligi va o'qilmasligi ham tekshiriladi.
/// </summary>
public class McpTestToolsTests
{
    private static List<string?> Ids(JsonElement json, string prop) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    // ================================================================== ma'lumot

    /// <summary>
    /// g1 «English A1» (faol): s1, s2 faol, s3 sinov, s8 faol, s4 muzlatilgan, s6 chiqib ketgan.
    /// g2 «Math B»: s3, s7 faol. g3 «Old group» — arxivlangan. s5 — arxivlangan o'quvchi.
    /// </summary>
    private static void SeedBase(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" }, new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", Days = [0, 2, 4], StartDate = "2026-09-01" },
            new Group { Id = "g2", Name = "Math B", CourseId = "c2", TeacherId = "t2", Days = [0], StartDate = "2026-09-01" },
            new Group { Id = "g3", Name = "Old group", CourseId = "c1", TeacherId = "t1", IsArchived = true });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev", BirthCertificateUrl = "/uploads/maxfiy-surat.jpg" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Muzlagan" },
            new Student { Id = "s5", FullName = "Eldor Arxivda", IsArchived = true },
            new Student { Id = "s6", FullName = "Farrux Chiqqan" },
            new Student { Id = "s7", FullName = "Gulnoza Karimova" },
            new Student { Id = "s8", FullName = "Hamid Karimov" });
        StudentGroup M(string sid, string gid, string status = "active", string joined = "2026-09-01") =>
            new() { StudentId = sid, GroupId = gid, Status = status, JoinedAt = joined, ActivatedAt = joined, RecordedAt = joined };
        var frozen = M("s4", "g1", "frozen");
        frozen.FrozenAt = "2026-09-15";
        var left = M("s6", "g1");
        left.IsActive = false;
        left.LeftAt = "2026-09-20";
        var archived = M("s5", "g1");
        db.StudentGroups.AddRange(
            M("s1", "g1"), M("s2", "g1"), M("s3", "g1", "trial", "2026-09-05"), M("s8", "g1"), frozen, left, archived,
            M("s3", "g2"), M("s7", "g2"));
        db.SaveChanges();
    }

    /// <summary>T1..T4 testlari; T2 da onlayn maydonlar (chiqmasligi kerak), T1 da tashqi ishtirokchi.</summary>
    private static void SeedTests(McpTestHost host)
    {
        var db = host.Rw;
        db.TestResults.AddRange(
            new TestResult { Id = "T1", GroupId = "g1", Name = "Unit 1 test", Date = "2026-09-10", MaxScore = 20, CreatedAt = "2026-09-10T10:00:00" },
            new TestResult
            {
                Id = "T2", GroupId = "g1", Name = "Unit 2 test", Date = "2026-10-02", MaxScore = 10, CreatedAt = "2026-10-02T09:00:00",
                Mode = "online", PdfUrl = "/uploads/0123456789abcdef0123456789abcdef.pdf", AnswerKey = "DCBAD", Code = "QWERTY",
            },
            new TestResult { Id = "T3", GroupId = "g2", Name = "Algebra nazorat", Date = "2026-09-20", MaxScore = 50, CreatedAt = "2026-09-20T11:00:00" },
            new TestResult { Id = "T4", GroupId = "g3", Name = "Final exam", Date = "2026-06-15", MaxScore = 100, CreatedAt = "2026-06-15T08:00:00" });
        db.TestScores.AddRange(
            new TestScore { TestResultId = "T1", StudentId = "s1", Score = 18 },
            new TestScore { TestResultId = "T1", StudentId = "s2", Score = 18 },
            new TestScore { TestResultId = "T1", StudentId = "s3", Score = 15 },
            new TestScore { TestResultId = "T1", StudentId = "s6", Score = 12 },    // chiqib ketgan — "mehmon" qator
            new TestScore { TestResultId = "T2", StudentId = "s1", Score = 7, Answers = "DCBAA", Source = "bot", SubmittedAt = "2026-10-02T10:00:00" },
            new TestScore { TestResultId = "T2", StudentId = "s3", Score = 9 },
            new TestScore { TestResultId = "T3", StudentId = "s3", Score = 40 },
            new TestScore { TestResultId = "T3", StudentId = "s7", Score = 45 },
            new TestScore { TestResultId = "T4", StudentId = "s1", Score = 88 });
        db.ExternalTestScores.Add(new ExternalTestScore
        {
            TestResultId = "T1", ChatId = 987654321, FullName = "Tashqi Ishtirokchi", Phone = "+998900000000", Score = 20,
        });
        db.SaveChanges();
    }

    private static McpTestHost TestsHost()
    {
        var host = new McpTestHost();
        SeedBase(host);
        SeedTests(host);
        return host;
    }

    private static void AssertNoOnlineTestData(string raw)
    {
        Assert.DoesNotContain("/uploads/", raw);
        Assert.DoesNotContain("QWERTY", raw);
        Assert.DoesNotContain("DCBAD", raw);
        Assert.DoesNotContain("DCBAA", raw);
        Assert.DoesNotContain("Tashqi Ishtirokchi", raw);
        Assert.DoesNotContain("987654321", raw);
    }

    // ================================================================== tests_list

    [Fact]
    public async Task Testlar_royxati_yangisidan_eskisiga_ortacha_ball_va_foiz_bilan()
    {
        using var host = TestsHost();

        var raw = await host.Tool<TestTools>(McpTestHost.SuperAdmin()).TestsList();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(4, json.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "T2", "T3", "T1", "T4" }, Ids(json, "id"));

        var t1 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "T1");
        Assert.Equal("Unit 1 test", t1.GetProperty("name").GetString());
        Assert.Equal("2026-09-10", t1.GetProperty("date").GetString());
        Assert.Equal(20m, t1.GetProperty("maxScore").GetDecimal());
        Assert.Equal("English A1", t1.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Ingliz tili", t1.GetProperty("course").GetString());
        Assert.Equal("Dilnoza Karimova", t1.GetProperty("teacher").GetProperty("name").GetString());
        // Faol (muzlatilmagan) a'zolar: s1, s2, s3 (sinov), s5 (arxivdagi o'quvchi, lekin a'zolik tirik), s8.
        Assert.Equal(5, t1.GetProperty("groupStudents").GetInt32());
        Assert.Equal(4, t1.GetProperty("scored").GetInt32());                 // tashqi ishtirokchi sanalmaydi
        Assert.Equal(15.8m, t1.GetProperty("averageScore").GetDecimal());     // 63 / 4 = 15.75
        Assert.Equal(78.8m, t1.GetProperty("averagePercent").GetDecimal());   // 15.75 / 20 × 100

        var t4 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "T4");
        Assert.True(t4.GetProperty("group").GetProperty("isArchived").GetBoolean());
        Assert.Equal(88m, t4.GetProperty("averagePercent").GetDecimal());

        AssertNoOnlineTestData(raw);
        host.AssertReadsOnlyDeclared(typeof(TestTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Testlar_royxati_filtrlar_va_sahifalash()
    {
        using var host = TestsHost();
        var tool = () => host.Tool<TestTools>(McpTestHost.Staff("classes.testResults"));

        var byGroup = McpTestHost.Parse(await tool().TestsList(groupId: "g1"));
        Assert.Equal(new[] { "T2", "T1" }, Ids(byGroup, "id"));

        var byMonth = McpTestHost.Parse(await tool().TestsList(month: "2026-09"));
        Assert.Equal(new[] { "T3", "T1" }, Ids(byMonth, "id"));
        Assert.Equal("2026-09", byMonth.GetProperty("month").GetString());

        var byRange = McpTestHost.Parse(await tool().TestsList(from: "2026-09-15", to: "2026-10-31"));
        Assert.Equal(new[] { "T2", "T3" }, Ids(byRange, "id"));

        var bySearch = McpTestHost.Parse(await tool().TestsList(search: "UNIT"));
        Assert.Equal(new[] { "T2", "T1" }, Ids(bySearch, "id"));

        var paged = McpTestHost.Parse(await tool().TestsList(page: 2, pageSize: 1));
        Assert.Equal(4, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "T3" }, Ids(paged, "id"));

        await Assert.ThrowsAsync<McpException>(() => tool().TestsList(month: "2026-09", from: "2026-09-01"));
        await Assert.ThrowsAsync<McpException>(() => tool().TestsList(month: "2026/09"));
        await Assert.ThrowsAsync<McpException>(() => tool().TestsList(from: "2025-01-01", to: "2026-10-01"));
    }

    [Fact]
    public async Task Testlar_royxati_ruxsatsiz_xodimga_YOPIQ_va_hech_narsa_soralmaydi()
    {
        using var host = TestsHost();

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<TestTools>(McpTestHost.Staff("students.list")).TestsList());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<TestTools>(McpTestHost.Staff("classes.list")).TestScores("T1"));
        Assert.Empty(host.Sql);
    }

    // ================================================================== test_scores

    [Fact]
    public async Task Test_ballari_musobaqa_orni_bilan_va_jamlanma()
    {
        using var host = TestsHost();

        var raw = await host.Tool<TestTools>(McpTestHost.Admin()).TestScores("T1");
        var json = McpTestHost.Parse(raw);
        var test = json.GetProperty("test");
        Assert.Equal("Unit 1 test", test.GetProperty("name").GetString());
        Assert.Equal("English A1", test.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Dilnoza Karimova", test.GetProperty("teacher").GetProperty("name").GetString());

        // Teng ball (18, 18) — bir xil o'rin, keyingisi sakraydi (3); chiqib ketgan s6 — mehmon qator;
        // muzlatilgan s4 kirmaydi; ball kiritilmagan s5, s8 oxirida (o'rni yo'q).
        Assert.Equal(new[] { "s1", "s2", "s3", "s6", "s5", "s8" }, Ids(json, "studentId"));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new int?[] { 1, 1, 3, 4, null, null },
            items.Select(i => i.GetProperty("rank").ValueKind == JsonValueKind.Null ? (int?)null : i.GetProperty("rank").GetInt32()));
        Assert.Equal(90m, items[0].GetProperty("percent").GetDecimal());
        Assert.Equal(75m, items[2].GetProperty("percent").GetDecimal());
        Assert.False(items[3].GetProperty("member").GetBoolean());
        Assert.True(items[0].GetProperty("member").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[5].GetProperty("score").ValueKind);

        var sum = json.GetProperty("summary");
        Assert.Equal(6, sum.GetProperty("students").GetInt32());
        Assert.Equal(4, sum.GetProperty("entered").GetInt32());
        Assert.Equal(2, sum.GetProperty("notEntered").GetInt32());
        Assert.Equal(15.8m, sum.GetProperty("averageScore").GetDecimal());
        Assert.Equal(18m, sum.GetProperty("maxScore").GetDecimal());
        Assert.Equal(12m, sum.GetProperty("minScore").GetDecimal());
        Assert.Equal(78.8m, sum.GetProperty("averagePercent").GetDecimal());

        AssertNoOnlineTestData(raw);
        Assert.DoesNotContain("answers", raw, StringComparison.OrdinalIgnoreCase);
        host.AssertReadsOnlyDeclared(typeof(TestTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Test_ballari_paneldagi_markazdagilar_royxati_bilan_BIR_XIL()
    {
        using var host = TestsHost();

        var json = McpTestHost.Parse(await host.Tool<TestTools>(McpTestHost.SuperAdmin()).TestScores("T1"));
        var panel = await TestResultService.DetailAsync(host.Rw, "T1");

        Assert.NotNull(panel);
        Assert.Equal(panel!.Rows.Select(r => r.StudentId), Ids(json, "studentId"));
        Assert.Equal(panel.Rows.Select(r => r.Rank == 0 ? (int?)null : r.Rank),
            json.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("rank").ValueKind == JsonValueKind.Null ? (int?)null : i.GetProperty("rank").GetInt32()));
    }

    [Fact]
    public async Task Test_ballari_sahifalash_va_topilmagan_test()
    {
        using var host = TestsHost();
        var tool = () => host.Tool<TestTools>(McpTestHost.Staff("classes.testResults"));

        var paged = McpTestHost.Parse(await tool().TestScores("T1", page: 2, pageSize: 2));
        Assert.Equal(6, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "s3", "s6" }, Ids(paged, "studentId"));

        var missing = McpTestHost.Parse(await tool().TestScores("yoq"));
        Assert.Equal("Test topilmadi.", missing.GetProperty("message").GetString());

        await Assert.ThrowsAsync<McpException>(() => tool().TestScores(" "));
    }

    // ================================================================== student_tests

    [Fact]
    public async Task Oquvchi_test_tarixi_ball_foiz_va_orin_bilan()
    {
        using var host = TestsHost();

        var raw = await host.Tool<TestTools>(McpTestHost.Staff("classes.testResults")).StudentTests("s1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("Ali Valiyev", json.GetProperty("student").GetProperty("fullName").GetString());
        Assert.Equal(new[] { "T2", "T1", "T4" }, Ids(json, "testId"));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(7m, items[0].GetProperty("score").GetDecimal());
        Assert.Equal(10m, items[0].GetProperty("maxScore").GetDecimal());
        Assert.Equal(70m, items[0].GetProperty("percent").GetDecimal());
        Assert.Equal(2, items[0].GetProperty("rank").GetInt32());             // s3 = 9
        Assert.Equal(2, items[0].GetProperty("scoredCount").GetInt32());
        Assert.Equal(1, items[1].GetProperty("rank").GetInt32());             // 18 = 18 → ikkalasi 1-o'rin
        Assert.Equal(4, items[1].GetProperty("scoredCount").GetInt32());
        Assert.Equal("Old group", items[2].GetProperty("group").GetProperty("name").GetString());
        Assert.Equal(3, json.GetProperty("summary").GetProperty("tests").GetInt32());
        Assert.Equal(82.7m, json.GetProperty("summary").GetProperty("averagePercent").GetDecimal());  // (70+90+88)/3

        AssertNoOnlineTestData(raw);
        host.AssertReadsOnlyDeclared(typeof(TestTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Oquvchi_test_tarixi_sana_filtri_ruxsat_va_topilmagan()
    {
        using var host = TestsHost();

        // O'quvchilar ro'yxati ruxsati ham yetadi.
        var sep = McpTestHost.Parse(await host.Tool<TestTools>(McpTestHost.Staff("students.list"))
            .StudentTests("s1", from: "2026-09-01", to: "2026-09-30"));
        Assert.Equal(new[] { "T1" }, Ids(sep, "testId"));
        Assert.Equal("2026-09-01", sep.GetProperty("from").GetString());

        var paged = McpTestHost.Parse(await host.Tool<TestTools>(McpTestHost.Admin()).StudentTests("s1", page: 2, pageSize: 2));
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "T4" }, Ids(paged, "testId"));

        var none = McpTestHost.Parse(await host.Tool<TestTools>(McpTestHost.Admin()).StudentTests("yoq"));
        Assert.Equal("O'quvchi topilmadi.", none.GetProperty("message").GetString());

        var before = host.Sql.Count;
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<TestTools>(McpTestHost.Staff("classes.list")).StudentTests("s1"));
        Assert.Equal(before, host.Sql.Count);
    }

    // ================================================================== reyting ma'lumoti

    /// <summary>
    /// g1: 3 ta o'tilgan dars (09-07, 09-09, 10-05) + 1 o'tilmagan; mezonlar k1, k2 biriktirilgan; s2 ga +4 tuzatish.
    /// g2: 2 ta o'tilgan dars (09-08, 09-10).
    /// </summary>
    private static void SeedRating(McpTestHost host)
    {
        var db = host.Rw;
        db.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "rS", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "rL", Name = "Kech keldi", Short = "Kech", IsLate = true });
        LessonNote N(string gid, string sub, string date, bool conducted = true) =>
            new() { ClassId = gid, SubjectId = sub, Date = date, Period = 1, Conducted = conducted, AttendanceTaken = conducted };
        db.LessonNotes.AddRange(
            N("g1", "c1", "2026-09-07"), N("g1", "c1", "2026-09-09"), N("g1", "c1", "2026-10-05"),
            N("g1", "c1", "2026-10-07", conducted: false),
            N("g2", "c2", "2026-09-08"), N("g2", "c2", "2026-09-10"));
        JournalEntry E(string gid, string sub, string sid, string date, int? grade = null, string? reason = null, bool fromCriteria = false) =>
            new() { ClassId = gid, SubjectId = sub, StudentId = sid, Date = date, Period = 1, Grade = grade, ReasonId = reason, GradeFromCriteria = fromCriteria };
        db.JournalEntries.AddRange(
            E("g1", "c1", "s1", "2026-09-07", 5), E("g1", "c1", "s1", "2026-09-09", 4),
            E("g1", "c1", "s1", "2026-10-05", 3, fromCriteria: true),          // mezondan — ballga QO'SHILMAYDI
            E("g1", "c1", "s2", "2026-09-07", reason: "rS"),                   // kelmadi
            E("g1", "c1", "s2", "2026-09-09", 5),
            E("g1", "c1", "s2", "2026-10-05", reason: "rL"),                   // kech — kelmaslik EMAS
            E("g1", "c1", "s2", "2026-10-07", reason: "rS"),                   // o'tilmagan dars — sanalmaydi
            E("g1", "c1", "s3", "2026-10-05", 5),
            E("g1", "c1", "s4", "2026-09-07", 5),
            E("g2", "c2", "s3", "2026-09-08", 4),
            E("g2", "c2", "s7", "2026-09-08", 5),
            E("g2", "c2", "s7", "2026-09-10", reason: "rS"));
        db.GradingCriteria.AddRange(
            new GradingCriterion { Id = "k1", Name = "Uy vazifasi", Order = 1 },
            new GradingCriterion { Id = "k2", Name = "Faollik", Order = 2 });
        db.GroupGradingCriteria.AddRange(
            new GroupGradingCriterion { GroupId = "g1", CriterionId = "k1", Order = 1 },
            new GroupGradingCriterion { GroupId = "g1", CriterionId = "k2", Order = 2 });
        CriterionGrade K(string sid, string crit, string date, bool done = true) =>
            new() { GroupId = "g1", StudentId = sid, CriterionId = crit, Date = date, Done = done };
        db.CriterionGrades.AddRange(
            K("s1", "k1", "2026-10-05"), K("s1", "k2", "2026-10-05"), K("s1", "k1", "2026-09-09"),
            K("s2", "k1", "2026-09-07", done: false),
            K("s3", "k2", "2026-10-05"));
        db.StudentBallAdjustments.Add(new StudentBallAdjustment
        {
            StudentId = "s2", GroupId = "g1", Delta = 4, Reason = "Olimpiada", CreatedAt = "2026-09-20T10:00:00", CreatedBy = "Admin",
        });
        db.SaveChanges();
    }

    private static McpTestHost RatingHost()
    {
        var host = new McpTestHost();
        SeedBase(host);
        SeedRating(host);
        return host;
    }

    // ================================================================== rating_group

    [Fact]
    public async Task Guruh_reytingi_yigilgan_ball_va_tarkibi_butun_vaqt()
    {
        using var host = RatingHost();

        var raw = await host.Tool<TestTools>(McpTestHost.Staff("classes.list")).RatingGroup("g1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("all-time", json.GetProperty("period").GetString());
        Assert.Equal("English A1", json.GetProperty("group").GetProperty("name").GetString());
        // Muzlatilgan s4, chiqib ketgan s6 va arxivdagi s5 kirmaydi.
        Assert.Equal(new[] { "s1", "s2", "s3", "s8" }, Ids(json, "studentId"));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, items.Select(i => i.GetProperty("rank").GetInt32()));

        var ali = items[0];
        Assert.Equal(12, ali.GetProperty("ball").GetInt32());              // 5 + 4 (mezondan 3 emas) + 3 mezon
        Assert.Equal(9, ali.GetProperty("journalTotal").GetInt32());
        Assert.Equal(2, ali.GetProperty("gradeCount").GetInt32());
        Assert.Equal(4.5, ali.GetProperty("averageGrade").GetDouble());
        Assert.Equal(3, ali.GetProperty("criteriaDone").GetInt32());
        var crit = ali.GetProperty("criteria").EnumerateArray().ToList();
        Assert.Equal(new[] { "Uy vazifasi", "Faollik" }, crit.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(new[] { 2, 1 }, crit.Select(c => c.GetProperty("done").GetInt32()));
        Assert.Equal(100, ali.GetProperty("attendance").GetProperty("percent").GetDouble());

        var bobur = items[1];
        Assert.Equal(9, bobur.GetProperty("ball").GetInt32());             // 5 + 4 tuzatish
        Assert.Equal(4, bobur.GetProperty("adjustment").GetInt32());
        Assert.Equal(0, bobur.GetProperty("criteriaDone").GetInt32());     // Done=false sanalmaydi
        Assert.Equal(3, bobur.GetProperty("attendance").GetProperty("lessonsConducted").GetInt32());
        Assert.Equal(1, bobur.GetProperty("attendance").GetProperty("absent").GetInt32());   // kech va o'tilmagan dars — yo'q
        Assert.Equal(67, bobur.GetProperty("attendance").GetProperty("percent").GetDouble());

        Assert.Equal("trial", items[2].GetProperty("membershipStatus").GetString());
        Assert.Equal(6, items[2].GetProperty("ball").GetInt32());
        Assert.Equal(0, items[3].GetProperty("ball").GetInt32());

        var sum = json.GetProperty("summary");
        Assert.Equal(4, sum.GetProperty("students").GetInt32());
        Assert.Equal(6.8, sum.GetProperty("averageBall").GetDouble());      // (12+9+6+0)/4 = 6.75
        Assert.Equal(12, sum.GetProperty("topBall").GetInt32());

        // Server formulasi bilan bir xil (o'qituvchi reytingi ham shu funksiyadan).
        var panel = await StudentBallService.ComputeByGroupAsync(host.Rw, new List<string> { "g1" });
        Assert.Equal(panel[("s1", "g1")].Ball, ali.GetProperty("ball").GetInt32());
        Assert.Equal(panel[("s2", "g1")].Ball, bobur.GetProperty("ball").GetInt32());

        Assert.DoesNotContain("/uploads/", raw);
        host.AssertReadsOnlyDeclared(typeof(TestTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Guruh_reytingi_oy_kesimi_va_muzlatilganlar()
    {
        using var host = RatingHost();
        var tool = () => host.Tool<TestTools>(McpTestHost.Admin());

        var oct = McpTestHost.Parse(await tool().RatingGroup("g1", month: "2026-10"));
        Assert.Equal("2026-10", oct.GetProperty("month").GetString());
        Assert.Equal(new[] { "s3", "s1", "s2", "s8" }, Ids(oct, "studentId"));
        var items = oct.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { 6, 2, 0, 0 }, items.Select(i => i.GetProperty("ball").GetInt32()));
        Assert.Equal(0, items[1].GetProperty("journalTotal").GetInt32());  // oktyabrdagi baho mezondan
        Assert.Equal(0, items[2].GetProperty("adjustment").GetInt32());    // tuzatish sentabrda yozilgan
        Assert.Equal(1, items[2].GetProperty("attendance").GetProperty("lessonsConducted").GetInt32());
        Assert.Equal(100, items[2].GetProperty("attendance").GetProperty("percent").GetDouble());

        var withFrozen = McpTestHost.Parse(await tool().RatingGroup("g1", includeFrozen: true));
        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s8" }, Ids(withFrozen, "studentId"));
        var davron = withFrozen.GetProperty("items")[3];
        Assert.Equal("frozen", davron.GetProperty("membershipStatus").GetString());
        Assert.Equal(5, davron.GetProperty("ball").GetInt32());

        var paged = McpTestHost.Parse(await tool().RatingGroup("g1", page: 2, pageSize: 3));
        Assert.Equal(4, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "s8" }, Ids(paged, "studentId"));
        Assert.Equal(4, paged.GetProperty("items")[0].GetProperty("rank").GetInt32());

        var missing = McpTestHost.Parse(await tool().RatingGroup("yoq"));
        Assert.Equal("Guruh topilmadi.", missing.GetProperty("message").GetString());
        await Assert.ThrowsAsync<McpException>(() => tool().RatingGroup("g1", month: "oktyabr"));
    }

    [Fact]
    public async Task Guruh_reytingi_ruxsatsiz_xodimga_YOPIQ()
    {
        using var host = RatingHost();

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<TestTools>(McpTestHost.Staff("classes.testResults")).RatingGroup("g1"));
        Assert.Empty(host.Sql);
    }

    // ================================================================== rating_center

    [Fact]
    public async Task Markaz_reytingi_guruhlar_boyicha_ortacha_ball_bilan()
    {
        using var host = RatingHost();

        var raw = await host.Tool<TestTools>(McpTestHost.Staff("students.list")).RatingCenter();
        var json = McpTestHost.Parse(raw);

        Assert.Equal("avgBall", json.GetProperty("sortBy").GetString());
        // Arxivdagi s5 va guruhsiz (chiqib ketgan) s6 kirmaydi. 5 ballda teng: o'rtacha baho, keyin ism.
        Assert.Equal(new[] { "s1", "s2", "s4", "s7", "s3", "s8" }, Ids(json, "studentId"));
        Assert.Equal(6, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, items.Select(i => i.GetProperty("rank").GetInt32()));

        var ali = items[0];
        Assert.Equal(12, ali.GetProperty("ball").GetInt32());
        Assert.Equal(4.0, ali.GetProperty("averageGrade").GetDouble());   // 5, 4, 3 — o'rtacha bahoda HAMMA baho
        Assert.Equal(100, ali.GetProperty("attendancePercent").GetDouble());

        var charos = items[4];
        Assert.Equal(10, charos.GetProperty("ball").GetInt32());          // g1: 6, g2: 4
        Assert.Equal(5.0, charos.GetProperty("avgBall").GetDouble());
        Assert.Equal(2, charos.GetProperty("groupCount").GetInt32());
        Assert.Equal(4.5, charos.GetProperty("averageGrade").GetDouble());
        Assert.Equal("Math B", charos.GetProperty("mainGroup").GetString());   // faol a'zolik sinovdan oldin
        var groups = charos.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(new[] { "g2", "g1" }, groups.Select(g => g.GetProperty("id").GetString()));
        Assert.Equal(new[] { 4, 6 }, groups.Select(g => g.GetProperty("ball").GetInt32()));

        Assert.Equal(50, items[3].GetProperty("attendancePercent").GetDouble());   // s7: 2 darsdan 1 tasida yo'q
        Assert.Equal(67, items[1].GetProperty("attendancePercent").GetDouble());   // s2

        Assert.DoesNotContain("/uploads/", raw);
        host.AssertReadsOnlyDeclared(typeof(TestTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Markaz_reytingi_RatingService_bilan_BIR_XIL_raqamlar()
    {
        using var host = RatingHost();

        var json = McpTestHost.Parse(await host.Tool<TestTools>(McpTestHost.SuperAdmin()).RatingCenter(pageSize: 200));
        var panel = await RatingService.SchoolAsync(host.Rw);

        var mine = json.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("studentId").GetString()!);
        Assert.Equal(panel.Select(r => r.Student.Id).OrderBy(x => x), mine.Keys.OrderBy(x => x));
        foreach (var r in panel)
        {
            var m = mine[r.Student.Id];
            Assert.Equal(r.Ball, m.GetProperty("ball").GetInt32());
            Assert.Equal(r.AvgBall, m.GetProperty("avgBall").GetDouble());
            Assert.Equal(r.GroupCount, m.GetProperty("groupCount").GetInt32());
            Assert.Equal(r.Average, m.GetProperty("averageGrade").GetDouble());
            Assert.Equal(r.Attendance, m.GetProperty("attendancePercent").ValueKind == JsonValueKind.Null
                ? null : m.GetProperty("attendancePercent").GetDouble());
            Assert.Equal(r.ClassName, m.GetProperty("mainGroup").GetString());
        }
    }

    [Fact]
    public async Task Markaz_reytingi_jami_ball_saralash_sahifalash_va_kesh()
    {
        using var host = RatingHost();
        var tool = () => host.Tool<TestTools>(McpTestHost.Admin());

        var byBall = McpTestHost.Parse(await tool().RatingCenter(sortBy: "ball"));
        Assert.Equal(new[] { "s1", "s3", "s2", "s4", "s7", "s8" }, Ids(byBall, "studentId"));

        // Natija keshlangan (5 daqiqa): keyingi chaqiruv bazaga murojaat qilmaydi.
        var before = host.Sql.Count;
        var paged = McpTestHost.Parse(await tool().RatingCenter(page: 2, pageSize: 2));
        Assert.Equal(before, host.Sql.Count);
        Assert.Equal(new[] { "s4", "s7" }, Ids(paged, "studentId"));
        Assert.Equal(new[] { 3, 4 }, paged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("rank").GetInt32()));

        await Assert.ThrowsAsync<McpException>(() => tool().RatingCenter(sortBy: "average"));
    }

    [Fact]
    public async Task Markaz_reytingi_ruxsatsiz_xodimga_YOPIQ()
    {
        using var host = RatingHost();

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<TestTools>(McpTestHost.Staff("classes.list")).RatingCenter());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    [Fact]
    public void Foiz_bir_kasrgacha_va_maksimal_ball_yoq_bolsa_null()
    {
        Assert.Equal(78.8m, TestTools.Percent(15.75m, 20));
        Assert.Equal(33.3m, TestTools.Percent(1, 3));
        Assert.Null(TestTools.Percent(null, 20));
        Assert.Null(TestTools.Percent(5, 0));
    }
}
