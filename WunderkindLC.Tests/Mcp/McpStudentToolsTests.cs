using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>MCP: `center_overview`, `students_search`, `student_profile` — natija, ruxsat va maxfiylik.</summary>
public class McpStudentToolsTests
{
    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        var course = new Subject { Id = "c1", Name = "Ingliz tili" };
        var teacher = new Teacher { Id = "t1", FullName = "Dilnoza Karimova" };
        var room = new Room { Id = "r1", Name = "101", IsActive = true };
        var g1 = new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1" };
        var g2 = new Group { Id = "g2", Name = "English B1", CourseId = "c1", TeacherId = "t1" };
        var old = new Group { Id = "g-old", Name = "Yopilgan", CourseId = "c1", TeacherId = "t1", IsArchived = true };
        db.AddRange(course, teacher, room, g1, g2, old);

        db.Students.AddRange(
            new Student
            {
                Id = "s1", FullName = "Ali Valiyev", Phone = "+998901112233", ParentPhone = "+998935556677",
                ParentFullName = "Vali Valiyev", Balance = -250_000, SchoolGrade = 7, SchoolGradeYear = 2026,
                BirthCertificateUrl = "/uploads/maxfiy-surat.jpg", ParentPassportUrl = "/uploads/maxfiy-passport.pdf",
                UserId = "u-s1",
            },
            new Student { Id = "s2", FullName = "Bobur Aliyev", Phone = "+998907778899", Balance = 100_000 },
            new Student { Id = "s3", FullName = "Charos Tosheva", Balance = 0 },
            new Student { Id = "s4", FullName = "Davron Guruhsiz" },
            new Student { Id = "s5", FullName = "Eldor Ketgan", IsArchived = true, ArchiveReason = "Ko'chib ketdi", Balance = -90_000 });

        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-03" },
            new StudentGroup { StudentId = "s1", GroupId = "g2", Status = "trial", JoinedAt = "2026-10-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "trial", JoinedAt = "2026-10-05" },
            new StudentGroup { StudentId = "s3", GroupId = "g2", Status = "frozen", JoinedAt = "2026-08-01", FrozenAt = "2026-09-20", YearFreeze = true },
            // Arxiv guruhdagi tirik a'zolik va yopilgan a'zolik "hozirgi" hisoblanmaydi.
            new StudentGroup { StudentId = "s4", GroupId = "g-old", Status = "active", JoinedAt = "2025-01-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g2", Status = "active", IsActive = false, JoinedAt = "2026-01-01", LeftAt = "2026-06-01" });

        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = "s1", ChatId = 555000111, ParentName = "Vali", Phone = "+998935556677" });
        db.Users.Add(new AppUser { Id = "u-s1", FullName = "Ali Valiyev", Role = Roles.Student, Email = "ali.valiyev", PasswordHash = "XESH", InitialPassword = "ochiq-parol" });
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ center_overview

    [Fact]
    public async Task Overview_oquvchini_BIR_marta_sanaydi_va_holatni_ustunlik_boyicha_oladi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OverviewTools>(McpTestHost.SuperAdmin()).CenterOverview());
        var st = json.GetProperty("students");

        Assert.Equal(1, st.GetProperty("active").GetInt32());        // s1 (ikki guruhda — bir marta)
        Assert.Equal(1, st.GetProperty("trial").GetInt32());         // s2
        Assert.Equal(1, st.GetProperty("frozen").GetInt32());        // s3
        Assert.Equal(1, st.GetProperty("withoutGroup").GetInt32());  // s4 (faqat arxiv guruhda)
        Assert.Equal(4, st.GetProperty("total").GetInt32());
        Assert.Equal(1, st.GetProperty("archived").GetInt32());
        Assert.Equal(2, json.GetProperty("groups").GetInt32());
        Assert.Equal(1, json.GetProperty("teachers").GetInt32());
        Assert.Equal(1, json.GetProperty("money").GetProperty("debtors").GetInt32());       // arxivdagi qarzdor kirmaydi
        Assert.Equal(250_000, json.GetProperty("money").GetProperty("totalDebt").GetDecimal());
        host.AssertReadsOnlyDeclared(typeof(OverviewTools));
    }

    [Fact]
    public async Task Overview_moliya_ruxsatisiz_qarzni_KORSATMAYDI()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OverviewTools>(McpTestHost.Staff("aiAccess")).CenterOverview();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("money").ValueKind);
        Assert.DoesNotContain("250000", raw);
        Assert.Equal(4, json.GetProperty("students").GetProperty("total").GetInt32());
    }

    // ------------------------------------------------------------------ students_search

    [Fact]
    public async Task Search_royxat_guruhlar_holat_va_telegram_belgisi_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch());
        var items = json.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(4, json.GetProperty("total").GetInt32());   // arxivdagi sukut bo'yicha yo'q
        var ali = items.Single(i => i.GetProperty("id").GetString() == "s1");
        Assert.Equal("active", ali.GetProperty("memberState").GetString());
        Assert.Equal(2, ali.GetProperty("groups").GetArrayLength());
        Assert.True(ali.GetProperty("telegramLinked").GetBoolean());
        Assert.Equal(-250_000, ali.GetProperty("balance").GetDecimal());
        Assert.Equal("yearFrozen", items.Single(i => i.GetProperty("id").GetString() == "s3").GetProperty("memberState").GetString());
        var davron = items.Single(i => i.GetProperty("id").GetString() == "s4");
        Assert.Equal("none", davron.GetProperty("memberState").GetString());
        Assert.False(davron.GetProperty("telegramLinked").GetBoolean());
        host.AssertReadsOnlyDeclared(typeof(StudentTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("vali", null, null, null, null, new[] { "s1" })]                       // ism
    [InlineData("935556677", null, null, null, null, new[] { "s1" })]                  // ota-ona telefoni
    [InlineData(null, "archived", null, null, null, new[] { "s5" })]
    [InlineData(null, "all", null, null, null, new[] { "s1", "s2", "s3", "s4", "s5" })]
    [InlineData(null, null, "trial", null, null, new[] { "s2" })]                      // s1 da ham sinov bor, lekin u AKTIV
    [InlineData(null, null, "frozen", null, null, new[] { "s3" })]
    [InlineData(null, null, "none", null, null, new[] { "s4" })]
    [InlineData(null, null, null, "g2", null, new[] { "s1", "s3" })]                   // yopilgan a'zolik (s2) kirmaydi
    [InlineData(null, null, null, null, "linked", new[] { "s1" })]
    [InlineData(null, null, null, null, "unlinked", new[] { "s2", "s3", "s4" })]
    public async Task Search_filtrlari(
        string? search, string? state, string? memberState, string? groupId, string? telegram, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin())
            .StudentsSearch(search, state, memberState, groupId, telegram: telegram));
        var ids = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).OrderBy(x => x);

        Assert.Equal(expected, ids);
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_sahifalash_va_chegara()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tool = host.Tool<StudentTools>(McpTestHost.SuperAdmin());

        var first = McpTestHost.Parse(await tool.StudentsSearch(page: 1, pageSize: 3));
        var second = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch(page: 2, pageSize: 3));

        Assert.Equal(3, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(4, first.GetProperty("total").GetInt32());
        Assert.Equal(1, host.State.RowCount);
        Assert.Equal(McpToolContext.MaxPageSize, McpToolContext.PageSize(100_000));
        Assert.Equal(1, McpToolContext.PageSize(0));
    }

    [Fact]
    public async Task Search_moliya_ruxsatisiz_balans_YOQ_va_qarzdor_filtri_RAD()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<StudentTools>(McpTestHost.Staff("students.list")).StudentsSearch();
        Assert.DoesNotContain("250000", raw);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            McpTestHost.Parse(raw).GetProperty("items")[0].GetProperty("balance").ValueKind);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<StudentTools>(McpTestHost.Staff("students.list")).StudentsSearch(debtorsOnly: true));
        Assert.True(host.State.Denied);

        var debtors = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.Staff("students.list", "finance.main"))
            .StudentsSearch(debtorsOnly: true));
        Assert.Equal("s1", debtors.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("aiAccess")]
    [InlineData("students.turnstile")]
    [InlineData("finance.main")]
    public async Task Oquvchilar_bolimi_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<StudentTools>(McpTestHost.Staff(perm)).StudentsSearch());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<StudentTools>(McpTestHost.Staff(perm)).StudentProfile("s1"));
        Assert.Empty(host.Sql);   // rad etilganda bazaga umuman so'rov ketmaydi
    }

    [Fact]
    public async Task Search_notogri_parametr_tushunarli_xato_beradi()
    {
        using var host = new McpTestHost();
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch(state: "hammasi"));
        Assert.Contains("state", ex.Message);
    }

    // ------------------------------------------------------------------ student_profile

    [Fact]
    public async Task Profile_guruhlar_ota_ona_va_telegram_bilan_HUJJATLARSIZ()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile("s1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("Ali Valiyev", json.GetProperty("fullName").GetString());
        Assert.Equal("active", json.GetProperty("memberState").GetString());
        Assert.Equal(2, json.GetProperty("groups").GetArrayLength());
        Assert.Equal("Ingliz tili", json.GetProperty("groups")[0].GetProperty("course").GetString());
        Assert.Equal("Dilnoza Karimova", json.GetProperty("groups")[0].GetProperty("teacher").GetString());
        Assert.True(json.GetProperty("telegram").GetProperty("linked").GetBoolean());
        Assert.Equal(-250_000, json.GetProperty("balance").GetDecimal());

        // Hujjat skanlari, login/parol va Telegram chat id'si HECH QACHON chiqmaydi.
        foreach (var secret in new[] { "maxfiy-surat", "maxfiy-passport", "ochiq-parol", "XESH", "ali.valiyev", "555000111", "u-s1" })
            Assert.DoesNotContain(secret, raw);
        host.AssertReadsOnlyDeclared(typeof(StudentTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Profile_ism_boyicha_bir_nechta_mos_kelsa_nomzodlar_qaytadi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var many = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile(name: "ali"));
        Assert.Equal(2, many.GetProperty("candidates").GetArrayLength());   // Ali Valiyev, Bobur Aliyev

        var one = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile(name: "charos"));
        Assert.Equal("s3", one.GetProperty("id").GetString());

        var none = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile("yo'q-id"));
        Assert.Contains("topilmadi", none.GetProperty("message").GetString());
        Assert.Equal(0, host.State.RowCount);

        await Assert.ThrowsAsync<McpException>(() => host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile());
    }

    [Fact]
    public async Task Vositalar_bazada_HECH_NARSANI_ozgartirmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        string Snapshot() => string.Join("|",
            host.Rw.Students.OrderBy(s => s.Id).Select(s => s.Id + ":" + s.FullName + ":" + s.Balance + ":" + s.IsArchived)
                .AsEnumerable().Concat(host.Rw.StudentGroups.OrderBy(m => m.StudentId).ThenBy(m => m.GroupId)
                    .Select(m => m.StudentId + m.GroupId + m.Status + m.IsActive).AsEnumerable()));
        var before = Snapshot();

        await host.Tool<OverviewTools>(McpTestHost.SuperAdmin()).CenterOverview();
        await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch();
        await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentProfile("s1");

        host.Rw.ChangeTracker.Clear();
        Assert.Equal(before, Snapshot());
    }
}
