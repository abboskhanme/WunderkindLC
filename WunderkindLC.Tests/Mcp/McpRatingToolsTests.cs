using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — CRM upgrade Phase 4 (U4m): `rating_report`, `rating_group_report`, `student_rating`. Raqamlar paneldagi
/// AYNAN o'sha hisobdan (RatingEngine) — testlar + ball, davr bo'yicha; ruxsatlar RatingController bilan bir xil.
/// </summary>
public class McpRatingToolsTests
{
    private static readonly DateOnly MonthStart = new(McpToolContext.Today.Year, McpToolContext.Today.Month, 1);
    private static readonly string Day1 = McpToolContext.Iso(MonthStart);
    private static readonly string Cm = MonthStart.ToString("yyyy-MM");

    private static List<string?> Ids(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("studentId").GetString()).ToList();

    private static void Seed(McpTestHost host, int? testWeight = null)
    {
        var db = host.Rw;
        if (testWeight is not null) db.CenterMeta.Add(new CenterMeta { RatingTestWeight = testWeight.Value });
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1" },
            new Group { Id = "g2", Name = "English B1", CourseId = "c1", TeacherId = "t2" });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Karimov" },
            new Student { Id = "s5", FullName = "Eldor Arxivda", IsArchived = true },
            new Student { Id = "s6", FullName = "Farrux Yangi" });
        StudentGroup M(string sid, string gid, string status = "active") =>
            new() { StudentId = sid, GroupId = gid, Status = status, JoinedAt = Day1, ActivatedAt = Day1 };
        db.StudentGroups.AddRange(M("s1", "g1"), M("s2", "g1"), M("s3", "g1"), M("s5", "g1"), M("s6", "g1", "trial"), M("s4", "g2"));

        db.TestResults.AddRange(
            new TestResult { Id = "T1", GroupId = "g1", Name = "Unit 5", Date = Day1, MaxScore = 10, CreatedAt = Day1 + "T10:00:00", PdfUrl = "/uploads/MAXFIY-KALIT.pdf" },
            new TestResult { Id = "T2", GroupId = "g2", Name = "Unit 2", Date = Day1, MaxScore = 20, CreatedAt = Day1 + "T11:00:00" });
        db.TestScores.AddRange(
            new TestScore { TestResultId = "T1", StudentId = "s1", Score = 8 },
            new TestScore { TestResultId = "T1", StudentId = "s2", Score = 6 },
            new TestScore { TestResultId = "T1", StudentId = "s5", Score = 10 },   // arxivda — o'rin olmaydi
            new TestScore { TestResultId = "T2", StudentId = "s4", Score = 10 });
        JournalEntry G(string sid, string gid, int period, int grade) =>
            new() { StudentId = sid, ClassId = gid, SubjectId = "c1", Date = Day1, Period = period, Grade = grade };
        db.JournalEntries.AddRange(
            G("s1", "g1", 1, 5), G("s1", "g1", 2, 5),   // ball 10 — guruhning eng yaxshisi
            G("s2", "g1", 1, 5),                         // ball 5
            G("s3", "g1", 1, 4), G("s3", "g1", 2, 4),   // ball 8, testi yo'q
            G("s5", "g1", 1, 5), G("s5", "g1", 2, 5), G("s5", "g1", 3, 5));   // arxivda — eng yaxshi balni belgilamaydi
        db.SaveChanges();
    }

    [Fact]
    public async Task Markaz_reytingi_testlar_va_ball_panel_hisobi_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<RatingTools>(McpTestHost.Staff("classes.rating")).RatingReport();
        var json = McpTestHost.Parse(raw);

        Assert.Equal("month", json.GetProperty("period").GetProperty("kind").GetString());
        Assert.Equal(Cm, json.GetProperty("period").GetProperty("key").GetString());
        Assert.Equal(60, json.GetProperty("testWeight").GetInt32());
        // s1: 0.6·80 + 0.4·100 = 88; s3: faqat ball 8/10 → 80; s2: 0.6·60 + 0.4·50 = 56; s4: faqat test (guruhda ball 0) → 50.
        Assert.Equal(new[] { "s1", "s3", "s2", "s4" }, Ids(json));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { 88.0, 80.0, 56.0, 50.0 }, items.Select(i => i.GetProperty("score").GetDouble()));
        Assert.Equal(new[] { 1, 2, 3, 4 }, items.Select(i => i.GetProperty("rank").GetInt32()));
        Assert.Equal(80.0, items[0].GetProperty("testAvg").GetDouble());
        Assert.Equal(10, items[0].GetProperty("ball").GetInt32());
        Assert.Equal("English A1", items[0].GetProperty("groups")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("s5", raw);

        // Panelning o'zi (RatingController.Centre → RatingEngine) bilan AYNAN bir xil.
        var p = RatingPeriods.Resolve("month", null, McpToolContext.Today).Period!;
        var panel = RatingEngine.Centre(await RatingEngine.ComputeAsync(host.Rw, p), null, 1, 50);
        Assert.Equal(panel.Items.Select(i => (i.StudentId, i.Rank, i.Score)),
            items.Select(i => (i.GetProperty("studentId").GetString()!, i.GetProperty("rank").GetInt32(), i.GetProperty("score").GetDouble())));
        Assert.Equal(panel.Total, json.GetProperty("total").GetInt32());

        Assert.DoesNotContain("MAXFIY-KALIT", raw);
        host.AssertReadsOnlyDeclared(typeof(RatingTools));
        host.AssertNoSecretColumns();
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"PdfUrl\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Markaz_reytingi_testlar_ulushi_sozlamadan_oqiladi()
    {
        using var host = new McpTestHost();
        Seed(host, testWeight: 100);

        var json = McpTestHost.Parse(await host.Tool<RatingTools>(McpTestHost.SuperAdmin()).RatingReport());

        Assert.Equal(100, json.GetProperty("testWeight").GetInt32());
        // Faqat testlar: s1 80, s3 (testsiz) — faqat ball 80, s2 60, s4 50.
        var s1 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("studentId").GetString() == "s1");
        Assert.Equal(80.0, s1.GetProperty("score").GetDouble());
    }

    [Fact]
    public async Task Markaz_reytingi_qidiruv_davrlar_va_xatolar()
    {
        using var host = new McpTestHost();
        Seed(host);
        RatingTools Tool() => host.Tool<RatingTools>(McpTestHost.Admin());

        Assert.Equal(new[] { "s2" }, Ids(McpTestHost.Parse(await Tool().RatingReport(search: "bobur"))));
        var quarter = McpTestHost.Parse(await Tool().RatingReport("quarter", Cm));
        Assert.Equal("quarter", quarter.GetProperty("period").GetProperty("kind").GetString());
        Assert.Equal(4, quarter.GetProperty("total").GetInt32());
        var year = McpTestHost.Parse(await Tool().RatingReport("year"));
        Assert.Equal(4, year.GetProperty("total").GetInt32());
        var page2 = McpTestHost.Parse(await Tool().RatingReport(page: 2, pageSize: 3));
        Assert.Equal(new[] { "s4" }, Ids(page2));

        // O'tgan oy — ma'lumot yo'q.
        var prev = MonthStart.AddMonths(-1).ToString("yyyy-MM");
        Assert.Equal(0, McpTestHost.Parse(await Tool().RatingReport("month", prev)).GetProperty("total").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => Tool().RatingReport("week"));
        await Assert.ThrowsAsync<McpException>(() => Tool().RatingReport("month", "2026-13"));
        await Assert.ThrowsAsync<McpException>(() => Tool().RatingReport("month", MonthStart.AddMonths(2).ToString("yyyy-MM")));
    }

    [Fact]
    public async Task Guruh_reytingi_orinlilar_va_malumotsiz_azolar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<RatingTools>(McpTestHost.Staff("classes.list")).RatingGroupReport("g1"));

        Assert.Equal("English A1", json.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Dilnoza Karimova", json.GetProperty("group").GetProperty("teacher").GetString());
        Assert.Equal(3, json.GetProperty("ranked").GetInt32());
        // O'rinlilar, keyin ma'lumoti yo'q hozirgi a'zo (sinovdagi s6). Arxivdagi s5 yo'q.
        Assert.Equal(new[] { "s1", "s3", "s2", "s6" }, Ids(json));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(100.0, items[0].GetProperty("ballNorm").GetDouble());
        Assert.Equal(50.0, items[2].GetProperty("ballNorm").GetDouble());
        Assert.Equal(JsonValueKind.Null, items[3].GetProperty("rank").ValueKind);

        var missing = McpTestHost.Parse(await host.Tool<RatingTools>(McpTestHost.SuperAdmin()).RatingGroupReport("yoq"));
        Assert.Equal("Guruh topilmadi.", missing.GetProperty("message").GetString());
        host.AssertReadsOnlyDeclared(typeof(RatingTools));
    }

    [Fact]
    public async Task Oquvchi_reytingi_bloki()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<RatingTools>(McpTestHost.Staff("students.list")).StudentRating("s1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("Ali Valiyev", json.GetProperty("student").GetProperty("fullName").GetString());
        Assert.Equal(1, json.GetProperty("centre").GetProperty("rank").GetInt32());
        Assert.Equal(4, json.GetProperty("centre").GetProperty("total").GetInt32());
        Assert.Equal(88.0, json.GetProperty("centre").GetProperty("score").GetDouble());
        var g = json.GetProperty("groups")[0];
        Assert.Equal("g1", g.GetProperty("groupId").GetString());
        Assert.Equal(1, g.GetProperty("rank").GetInt32());
        Assert.Equal(3, g.GetProperty("total").GetInt32());
        Assert.Equal(60, json.GetProperty("components").GetProperty("testWeight").GetInt32());
        Assert.Equal(12, json.GetProperty("history").GetArrayLength());
        var test = json.GetProperty("tests")[0];
        Assert.Equal("Unit 5", test.GetProperty("name").GetString());
        Assert.Equal(80.0, test.GetProperty("percent").GetDouble());
        Assert.Equal(2, test.GetProperty("rankInTest").GetInt32());   // arxivdagi s5 (10) testda baribir ball olgan
        Assert.Equal(3, test.GetProperty("takers").GetInt32());
        Assert.True(json.TryGetProperty("attendance", out _));

        var missing = McpTestHost.Parse(await host.Tool<RatingTools>(McpTestHost.SuperAdmin()).StudentRating("yoq"));
        Assert.Equal("O'quvchi topilmadi.", missing.GetProperty("message").GetString());
        host.AssertReadsOnlyDeclared(typeof(RatingTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Reyting_ruxsatlari_RatingController_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<RatingTools>(McpTestHost.Staff("classes.list")).RatingReport());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<RatingTools>(McpTestHost.Staff("students.list")).RatingGroupReport("g1"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<RatingTools>(McpTestHost.Staff("classes.list")).StudentRating("s1"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<RatingTools>(McpTestHost.Staff("classes.testResults")).RatingReport());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);

        // classes.rating — hamma uchala vositani ochadi.
        var rating = McpTestHost.Staff("classes.rating");
        Assert.Equal(4, McpTestHost.Parse(await host.Tool<RatingTools>(rating).RatingReport()).GetProperty("total").GetInt32());
        Assert.Equal(4, McpTestHost.Parse(await host.Tool<RatingTools>(rating).RatingGroupReport("g1")).GetProperty("total").GetInt32());
        Assert.Equal(1, McpTestHost.Parse(await host.Tool<RatingTools>(rating).StudentRating("s1")).GetProperty("centre").GetProperty("rank").GetInt32());
    }

    // ================================================================== rating_top_bottom (U4d)

    private static List<string?> Students(JsonElement list) =>
        list.EnumerateArray().Select(r => r.GetProperty("studentId").GetString()).ToList();

    [Fact]
    public async Task TopBottom_guruh_boyicha_panel_oldindan_korish_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<RatingTools>(McpTestHost.Staff("classes.rating")).RatingTopBottom(month: Cm);
        var json = McpTestHost.Parse(raw);

        // g1: testi bor s1 (80%), s2 (60%) → k = min(5, ⌊2/2⌋) = 1. g2: bitta o'quvchi (s4) — ro'yxat yo'q. Arxivdagi s5 — yo'q.
        Assert.Equal(Cm, json.GetProperty("month").GetString());
        Assert.Equal("group", json.GetProperty("scope").GetString());
        Assert.Equal(5, json.GetProperty("count").GetInt32());
        var g = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal("g1", g.GetProperty("group").GetProperty("id").GetString());
        Assert.Equal(2, g.GetProperty("scored").GetInt32());
        Assert.Equal(new[] { "s1" }, Students(g.GetProperty("top")));
        Assert.Equal(new[] { "s2" }, Students(g.GetProperty("bottom")));
        Assert.Equal(80.0, g.GetProperty("top")[0].GetProperty("testAvg").GetDouble());
        Assert.Equal(2, g.GetProperty("bottom")[0].GetProperty("rank").GetInt32());
        Assert.DoesNotContain("s5", raw);

        // Panelning o'zi (RatingController.TopBottom → TopBottomService.BuildAsync) bilan AYNAN bir xil.
        var (panel, _, _) = await TopBottomService.BuildAsync(
            host.Rw, p => RatingEngine.ComputeAsync(host.Rw, p), Cm, null, McpToolContext.Today);
        Assert.Equal(panel!.Groups.Select(x => x.GroupId),
            json.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("group").GetProperty("id").GetString()));
        Assert.Equal(panel.Groups.SelectMany(x => x.Top.Concat(x.Bottom)).Select(x => (x.StudentId, x.Rank, x.TestAvg)),
            json.GetProperty("items").EnumerateArray()
                .SelectMany(x => x.GetProperty("top").EnumerateArray().Concat(x.GetProperty("bottom").EnumerateArray()))
                .Select(x => (x.GetProperty("studentId").GetString()!, x.GetProperty("rank").GetInt32(), x.GetProperty("testAvg").GetDouble())));

        Assert.DoesNotContain("MAXFIY-KALIT", raw);
        host.AssertReadsOnlyDeclared(typeof(RatingTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task TopBottom_markaz_qamrovi_sozlama_filtrlar_va_xatolar()
    {
        using var host = new McpTestHost();
        Seed(host);
        RatingTools Tool() => host.Tool<RatingTools>(McpTestHost.Admin());

        // Markaz: s1 80, s2 60, s4 50 → k = 1: TOP s1, BOTTOM s4 (eng past).
        var centre = McpTestHost.Parse(await Tool().RatingTopBottom(Cm, "centre"));
        Assert.Equal("centre", centre.GetProperty("scope").GetString());
        var list = Assert.Single(centre.GetProperty("items").EnumerateArray());
        Assert.Equal("", list.GetProperty("group").GetProperty("id").GetString());
        Assert.Equal("Markaz", list.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal(3, list.GetProperty("scored").GetInt32());
        Assert.Equal(new[] { "s1" }, Students(list.GetProperty("top")));
        Assert.Equal(new[] { "s4" }, Students(list.GetProperty("bottom")));

        Assert.Empty(McpTestHost.Parse(await Tool().RatingTopBottom(Cm, groupId: "g2")).GetProperty("items").EnumerateArray());

        // Sukut — O'TGAN oy (navbatdagi yuborish shu oyni oladi); unda test yo'q.
        var prev = McpTestHost.Parse(await Tool().RatingTopBottom());
        Assert.Equal(MonthStart.AddMonths(-1).ToString("yyyy-MM"), prev.GetProperty("month").GetString());
        Assert.Equal(0, prev.GetProperty("total").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => Tool().RatingTopBottom(Cm, "school"));
        await Assert.ThrowsAsync<McpException>(() => Tool().RatingTopBottom("2026-13"));
        await Assert.ThrowsAsync<McpException>(() => Tool().RatingTopBottom(MonthStart.AddMonths(2).ToString("yyyy-MM")));

        // N va qamrov — sozlamadan (CenterMeta, faqat-o'qish ruxsat ro'yxatidagi ustunlar).
        using var configured = new McpTestHost();
        Seed(configured);
        configured.Rw.CenterMeta.Add(new CenterMeta { RatingTopBottomCount = 3, RatingTopBottomScope = "centre" });
        configured.Rw.SaveChanges();
        var json = McpTestHost.Parse(await configured.Tool<RatingTools>(McpTestHost.SuperAdmin()).RatingTopBottom(Cm));
        Assert.Equal("centre", json.GetProperty("scope").GetString());
        Assert.Equal(3, json.GetProperty("count").GetInt32());
        configured.AssertReadsOnlyDeclared(typeof(RatingTools));

        // Ruxsat — `classes.rating` (panel: ReadRequiresPerm).
        using var denied = new McpTestHost();
        Seed(denied);
        foreach (var perm in new[] { "classes.list", "classes.testResults", "students.list" })
            await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
                denied.Tool<RatingTools>(McpTestHost.Staff(perm)).RatingTopBottom(Cm));
        Assert.True(denied.State.Denied);
        Assert.Empty(denied.Sql);
    }
}
