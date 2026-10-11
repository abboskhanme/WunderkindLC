using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>MCP: `course_analytics`, `school_grades_breakdown` — panel bilan bir xil sonlar, ruxsat va pul yashirish.</summary>
public class McpAnalyticsToolsTests
{
    // Sanalar BUGUNGA nisbatan: m0 — joriy oy, m1 — o'tgan oy.
    private static readonly DateOnly M0 = new(McpToolContext.Today.Year, McpToolContext.Today.Month, 1);
    private static readonly DateOnly M1 = M0.AddMonths(-1);

    [Theory]
    [InlineData(null, 6)]
    [InlineData(0, 6)]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 6)]
    [InlineData(7, 12)]
    [InlineData(13, 24)]
    [InlineData(1000, 24)]
    public void Oylar_oynasi_qatiy_qiymatlarga_yaxlitlanadi(int? asked, int expected) =>
        Assert.Equal(expected, AnalyticsTools.SnapMonths(asked));
    private static string D(DateOnly month, int day) => month.AddDays(day - 1).ToString("yyyy-MM-dd");
    private static string Mon(DateOnly month) => month.ToString("yyyy-MM");

    private static void SeedCourses(McpTestHost host)
    {
        var db = host.Rw;
        db.Subjects.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili", Price = 300_000 },
            new Subject { Id = "c2", Name = "Matematika", Price = 250_000 },
            new Subject { Id = "c3", Name = "Bo'sh kurs", Price = 100_000 });
        db.Classes.AddRange(
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", MonthlyFee = 300_000 },
            new Group { Id = "g2", Name = "English A2", CourseId = "c1", TeacherId = "t2", MonthlyFee = 320_000 },
            new Group { Id = "g3", Name = "Math 5", CourseId = "c2", TeacherId = "t1", MonthlyFee = 250_000 },
            new Group { Id = "gOld", Name = "English eski", CourseId = "c1", TeacherId = "t3", MonthlyFee = 280_000, IsArchived = true },
            new Group { Id = "gNo", Name = "Kurssiz", CourseId = "", TeacherId = "t1", MonthlyFee = 90_000 });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Bobur" },
            new Student { Id = "s3", FullName = "Charos" }, new Student { Id = "s4", FullName = "Dilshod" },
            new Student { Id = "s5", FullName = "Eldor", IsArchived = true }, new Student { Id = "s6", FullName = "Farrux" },
            new Student { Id = "s7", FullName = "G'ani" });
        db.StudentGroups.AddRange(
            // s1: ikki kursda AKTIV (kesishuv).
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = D(M1, 5), ActivatedAt = D(M1, 6) },
            new StudentGroup { StudentId = "s1", GroupId = "g3", Status = "active", JoinedAt = D(M0, 2), ActivatedAt = D(M0, 3) },
            // s2: sinovda.
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "trial", JoinedAt = D(M0, 3) },
            // s3: guruh almashtirdi (2 kun tanaffus) — KETDI emas; hozir muzlatilgan.
            new StudentGroup { StudentId = "s3", GroupId = "gOld", Status = "active", IsActive = false, JoinedAt = D(M1, 1), LeftAt = D(M1, 20) },
            new StudentGroup { StudentId = "s3", GroupId = "g2", Status = "frozen", JoinedAt = D(M1, 22), ActivatedAt = D(M1, 22), FrozenAt = D(M0, 5) },
            // s4: sertifikat bilan TUGATDI — churn emas.
            new StudentGroup { StudentId = "s4", GroupId = "g1", Status = "completed", IsActive = false, JoinedAt = D(M1, 2), ActivatedAt = D(M1, 2), LeftAt = D(M0, 4) },
            // s5: arxivlangan o'quvchi, a'zoligi eski — joriy sanoqqa kirmaydi, tushumda qoladi.
            new StudentGroup { StudentId = "s5", GroupId = "g3", Status = "active", JoinedAt = D(M1, 10), ActivatedAt = D(M1, 10) },
            // s6: KETDI.
            new StudentGroup { StudentId = "s6", GroupId = "g3", Status = "active", IsActive = false, JoinedAt = D(M1, 3), ActivatedAt = D(M1, 3), LeftAt = D(M0, 6) },
            // s7: kurssiz guruh — analitikaga kirmaydi.
            new StudentGroup { StudentId = "s7", GroupId = "gNo", Status = "active", JoinedAt = D(M1, 1), ActivatedAt = D(M1, 1) });
        db.SaveChanges();
    }

    private static JsonElement Course(JsonElement json, string id) =>
        json.GetProperty("courses").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);

    private static JsonElement Month(JsonElement course, DateOnly month) =>
        course.GetProperty("monthly").EnumerateArray().Single(m => m.GetProperty("month").GetString() == Mon(month));

    // ------------------------------------------------------------------ course_analytics

    [Fact]
    public async Task Kurslar_analitikasi_keldi_ketdi_tugatdi_va_joriy_holat_panel_qoidasi_bilan()
    {
        using var host = new McpTestHost();
        SeedCourses(host);

        var json = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.SuperAdmin()).CourseAnalytics(months: 2));

        // 2 oy so'ralsa 3 oylik oynaga yaxlitlanadi (3/6/12/24 — har xil qiymat butun jadvalni qayta qurardi).
        Assert.Equal(new[] { Mon(M1.AddMonths(-1)), Mon(M1), Mon(M0) }, json.GetProperty("months").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(new[] { "c3", "c1", "c2" },   // nom bo'yicha: Bo'sh kurs, Ingliz tili, Matematika
            json.GetProperty("courses").EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        var c1 = Course(json, "c1");
        Assert.Equal(2, c1.GetProperty("groups").GetInt32());          // arxiv guruh sanalmaydi
        Assert.Equal(2, c1.GetProperty("teachers").GetInt32());
        var cur = c1.GetProperty("current");
        Assert.Equal(1, cur.GetProperty("active").GetInt32());
        Assert.Equal(1, cur.GetProperty("trial").GetInt32());
        Assert.Equal(1, cur.GetProperty("frozen").GetInt32());
        Assert.Equal(3, cur.GetProperty("students").GetInt32());
        Assert.Equal(4, c1.GetProperty("totalEver").GetInt32());
        Assert.Equal(300_000, c1.GetProperty("monthlyRevenue").GetDecimal());

        var c1m1 = Month(c1, M1);
        Assert.Equal(3, c1m1.GetProperty("joined").GetInt32());        // s1, s3 (bir marta), s4
        Assert.Equal(3, c1m1.GetProperty("activated").GetInt32());
        Assert.Equal(0, c1m1.GetProperty("left").GetInt32());          // guruh almashtirish ketish EMAS
        Assert.Equal(3, c1m1.GetProperty("activeAtMonthEnd").GetInt32());
        var c1m0 = Month(c1, M0);
        Assert.Equal(1, c1m0.GetProperty("joined").GetInt32());        // s2
        Assert.Equal(0, c1m0.GetProperty("left").GetInt32());
        Assert.Equal(1, c1m0.GetProperty("completed").GetInt32());     // s4 — tugatdi
        Assert.Equal(1, c1m0.GetProperty("activeAtMonthEnd").GetInt32());

        var c2 = Course(json, "c2");
        Assert.Equal(1, c2.GetProperty("current").GetProperty("active").GetInt32());   // arxivdagi s5 kirmaydi
        Assert.Equal(3, c2.GetProperty("totalEver").GetInt32());
        Assert.Equal(500_000, c2.GetProperty("monthlyRevenue").GetDecimal());          // tushum a'zolik bo'yicha
        Assert.Equal(1, Month(c2, M0).GetProperty("left").GetInt32());                 // s6
        Assert.Equal(1, Month(c2, M0).GetProperty("activated").GetInt32());            // s1
        Assert.Equal(2, Month(c2, M0).GetProperty("activeAtMonthEnd").GetInt32());     // s1, s5

        var c3 = Course(json, "c3");
        Assert.Equal(0, c3.GetProperty("current").GetProperty("students").GetInt32());

        var totals = json.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("activeStudents").GetInt32());
        Assert.Equal(3, totals.GetProperty("groups").GetInt32());                      // kurssiz va arxiv guruh yo'q
        Assert.Equal(800_000, totals.GetProperty("monthlyRevenue").GetDecimal());

        var overlap = json.GetProperty("overlap");
        Assert.Equal(1, overlap.GetProperty("totalStudents").GetInt32());
        Assert.Equal(0, overlap.GetProperty("oneCourse").GetInt32());
        Assert.Equal(1, overlap.GetProperty("multiCourse").GetInt32());
        var pair = overlap.GetProperty("pairs")[0];
        Assert.Equal("c1", pair.GetProperty("a").GetProperty("id").GetString());
        Assert.Equal("Matematika", pair.GetProperty("b").GetProperty("name").GetString());
        Assert.Equal(1, pair.GetProperty("students").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(AnalyticsTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Kurslar_analitikasi_oylar_chegarasi_kurs_filtri_va_kesh()
    {
        using var host = new McpTestHost();
        SeedCourses(host);

        var def = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Admin()).CourseAnalytics());
        Assert.Equal(6, def.GetProperty("months").GetArrayLength());
        Assert.Equal(Mon(M0), def.GetProperty("months")[5].GetString());

        var sqlAfterFirst = host.Sql.Count;
        var again = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Admin()).CourseAnalytics(courseId: "c2"));
        Assert.Equal(sqlAfterFirst, host.Sql.Count);                 // keshdan — bazaga qayta so'rov yo'q
        Assert.Equal("c2", again.GetProperty("courses")[0].GetProperty("id").GetString());
        Assert.Equal(1, again.GetProperty("courses").GetArrayLength());

        var max = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Admin()).CourseAnalytics(months: 1000));
        Assert.Equal(24, max.GetProperty("months").GetArrayLength());

        var missing = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Admin()).CourseAnalytics(courseId: "yoq"));
        Assert.Equal(0, host.State.RowCount);
        Assert.Contains("topilmadi", missing.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Kurslar_analitikasi_moliya_ruxsatisiz_tushum_YOQ()
    {
        using var host = new McpTestHost();
        SeedCourses(host);

        var raw = await host.Tool<AnalyticsTools>(McpTestHost.Staff("schedule.analytics")).CourseAnalytics(months: 2);
        var json = McpTestHost.Parse(raw);
        Assert.Equal(JsonValueKind.Null, Course(json, "c1").GetProperty("monthlyRevenue").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("totals").GetProperty("monthlyRevenue").ValueKind);
        Assert.Equal(JsonValueKind.String, json.GetProperty("note").ValueKind);
        Assert.DoesNotContain("800000", raw);
        Assert.Equal(1, Course(json, "c1").GetProperty("current").GetProperty("active").GetInt32());

        // Kesh foydalanuvchiga bog'liq emas, lekin pul ruxsati HAR chaqiriqda tekshiriladi.
        var fin = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Staff("schedule.analytics", "finance.main"))
            .CourseAnalytics(months: 2));
        Assert.Equal(800_000, fin.GetProperty("totals").GetProperty("monthlyRevenue").GetDecimal());
    }

    [Theory]
    [InlineData("students.list")]
    [InlineData("schedule.courses")]
    [InlineData("finance.main")]
    public async Task Kurslar_analitikasi_ruxsatisiz_RAD(string perm)
    {
        using var host = new McpTestHost();
        SeedCourses(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<AnalyticsTools>(McpTestHost.Staff(perm)).CourseAnalytics());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    // ------------------------------------------------------------------ school_grades_breakdown

    private static void SeedGrades(McpTestHost host)
    {
        var db = host.Rw;
        var y = SchoolGrades.AcademicYear(McpToolContext.Today);
        db.Classes.AddRange(
            new Group { Id = "g1", Name = "A", CourseId = "c1" },
            new Group { Id = "gArx", Name = "Yopilgan", CourseId = "c1", IsArchived = true });
        db.Students.AddRange(
            new Student { Id = "a1", FullName = "A1", SchoolGrade = 7, SchoolGradeYear = y },          // 7
            new Student { Id = "a2", FullName = "A2", SchoolGrade = 7, SchoolGradeYear = y - 1 },      // 8 (o'zi oshdi)
            new Student { Id = "a3", FullName = "A3", SchoolGrade = 11, SchoolGradeYear = y - 1 },     // bitirgan
            new Student { Id = "a4", FullName = "A4" },                                                // kiritilmagan
            new Student { Id = "a5", FullName = "A5", SchoolGrade = 5, SchoolGradeYear = y },          // muzlatilgan
            new Student { Id = "a6", FullName = "A6", SchoolGrade = 3, SchoolGradeYear = y, IsArchived = true },
            new Student { Id = "a7", FullName = "A7", SchoolGrade = 9, SchoolGradeYear = y },          // faqat arxiv guruhda
            new Student { Id = "a8", FullName = "A8", SchoolGrade = 4, SchoolGradeYear = y });         // guruhsiz
        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "a1", GroupId = "g1", Status = "active" },
            new StudentGroup { StudentId = "a2", GroupId = "g1", Status = "active" },
            new StudentGroup { StudentId = "a3", GroupId = "g1", Status = "trial" },
            new StudentGroup { StudentId = "a4", GroupId = "g1", Status = "active" },
            new StudentGroup { StudentId = "a5", GroupId = "g1", Status = "frozen" },
            new StudentGroup { StudentId = "a6", GroupId = "g1", Status = "active" },
            new StudentGroup { StudentId = "a7", GroupId = "gArx", Status = "active" },
            new StudentGroup { StudentId = "a8", GroupId = "g1", Status = "active", IsActive = false });
        db.SaveChanges();
    }

    private static int Count(JsonElement json, int? grade) =>
        json.GetProperty("rows").EnumerateArray()
            .Single(r => grade is null ? r.GetProperty("grade").ValueKind == JsonValueKind.Null
                                       : r.GetProperty("grade").ValueKind == JsonValueKind.Number && r.GetProperty("grade").GetInt32() == grade)
            .GetProperty("count").GetInt32();

    [Fact]
    public async Task Sinflar_kesimi_JORIY_sinf_boyicha_va_sukut_faqat_AKTIVlar()
    {
        using var host = new McpTestHost();
        SeedGrades(host);

        var json = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.Staff("students.list")).SchoolGradesBreakdown());

        Assert.Equal("active", json.GetProperty("scope").GetString());
        Assert.Equal(3, json.GetProperty("total").GetInt32());          // a1, a2, a4
        Assert.Equal(13, json.GetProperty("rows").GetArrayLength());    // 1..11, bitirgan, kiritilmagan — hammasi
        Assert.Equal(1, Count(json, 7));
        Assert.Equal(1, Count(json, 8));                                // xom 7 emas — joriy 8
        Assert.Equal(1, Count(json, null));
        Assert.Equal(0, Count(json, 12));
        Assert.Equal("Maktabni bitirgan", json.GetProperty("rows")[11].GetProperty("label").GetString());

        var scopes = json.GetProperty("scopeCounts").EnumerateArray()
            .ToDictionary(s => s.GetProperty("scope").GetString()!, s => s.GetProperty("count").GetInt32());
        Assert.Equal(3, scopes["active"]);
        Assert.Equal(4, scopes["studying"]);
        Assert.Equal(7, scopes["all"]);                                 // arxivdagi a6 hech qayerda yo'q

        host.AssertReadsOnlyDeclared(typeof(AnalyticsTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("studying", 4, 12, 1)]
    [InlineData("all", 7, 9, 1)]          // a7 — faqat "hammasi"da
    [InlineData("all", 7, 5, 1)]          // muzlatilgan a5 — faqat "hammasi"da
    [InlineData("active", 3, 9, 0)]
    public async Task Sinflar_kesimi_qamrovlari(string scope, int total, int grade, int expected)
    {
        using var host = new McpTestHost();
        SeedGrades(host);

        var json = McpTestHost.Parse(await host.Tool<AnalyticsTools>(McpTestHost.SuperAdmin()).SchoolGradesBreakdown(scope));
        Assert.Equal(total, json.GetProperty("total").GetInt32());
        Assert.Equal(expected, Count(json, grade));
    }

    [Fact]
    public async Task Sinflar_kesimi_ruxsatisiz_RAD_va_notogri_qamrov()
    {
        using var host = new McpTestHost();
        SeedGrades(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<AnalyticsTools>(McpTestHost.Staff("schedule.analytics")).SchoolGradesBreakdown());
        Assert.Empty(host.Sql);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<AnalyticsTools>(McpTestHost.SuperAdmin()).SchoolGradesBreakdown("hammasi"));
        Assert.Contains("scope", ex.Message);
    }
}
