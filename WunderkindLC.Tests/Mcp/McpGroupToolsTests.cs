using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>MCP: `groups_list`, `group_roster`, `group_journal`, `rooms_list`, `courses_list` — natija, ruxsat va maxfiylik.</summary>
public class McpGroupToolsTests
{
    // ================================================================== umumiy ma'lumot

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili", Price = 300_000, LessonPrice = 25_000 },
            new Subject { Id = "c2", Name = "Matematika", Price = 250_000 },
            new Subject { Id = "c3", Name = "Rus tili", Price = 200_000 },
            new SubjectPriceBand { SubjectId = "c1", GradeFrom = 5, GradeTo = 11, Price = 320_000, LessonPrice = 27_000 },
            new SubjectPriceBand { SubjectId = "c1", GradeFrom = 1, GradeTo = 4, Price = 280_000, LessonPrice = 23_000 },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Sardor Aliyev" },
            new Room { Id = "r1", Name = "101", Capacity = 12, Building = "A", IsActive = true },
            new Room { Id = "r2", Name = "202", Capacity = 10, IsActive = true },
            new Room { Id = "r3", Name = "Eski xona", Capacity = 8, IsActive = false },
            new Group
            {
                Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", RoomId = "r1", Days = [2, 0],
                StartTime = "14:00", EndTime = "15:30", Capacity = 12, MonthlyFee = 300_000, StartDate = "2025-09-01",
            },
            // Eski matnli xona (RoomId yo'q) — "202" nomi bo'yicha xonaga bog'lanadi.
            new Group { Id = "g2", Name = "English B1", CourseId = "c1", TeacherId = "t2", Room = "202", Days = [1, 3], MonthlyFee = 300_000 },
            new Group { Id = "g3", Name = "Matematika 5", CourseId = "c2", TeacherId = "t1", RoomId = "r1", Days = [4] },
            new Group { Id = "gArch", Name = "English Old", CourseId = "c1", TeacherId = "t1", RoomId = "r1", IsArchived = true, ArchivedAt = "2025-06-30" },
            new Group { Id = "gN", Name = "Klub", RoomId = "r3" },
            new Student
            {
                Id = "s1", FullName = "Ali Valiyev", Phone = "+998901112233", ParentPhone = "+998935556677", FatherPhone = "+998935556677",
                BirthCertificateUrl = "/uploads/maxfiy-surat.jpg", ParentPassportUrl = "/uploads/maxfiy-passport.pdf",
            },
            new Student { Id = "s2", FullName = "Bobur Aliyev", Phone = "+998907778899" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Ketgan" },
            new Student { Id = "s5", FullName = "Eldor Arxiv", IsArchived = true },
            new Student { Id = "s6", FullName = "Farida Tugatgan" });

        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = "2025-08-20", ActivatedAt = "2025-08-20" },
            new StudentGroup { StudentId = "s1", GroupId = "g2", Status = "trial", JoinedAt = "2025-10-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "trial", JoinedAt = "2025-09-10" },
            new StudentGroup { StudentId = "s3", GroupId = "g1", Status = "frozen", JoinedAt = "2025-09-01", ActivatedAt = "2025-09-01", FrozenAt = "2025-09-20", YearFreeze = true },
            new StudentGroup { StudentId = "s4", GroupId = "g1", Status = "active", IsActive = false, JoinedAt = "2025-06-01", ActivatedAt = "2025-06-01", LeftAt = "2025-09-05" },
            // Arxivdagi o'quvchining TIRIK a'zoligi (eski ma'lumot) — hozirgi a'zo emas.
            new StudentGroup { StudentId = "s5", GroupId = "g1", Status = "active", JoinedAt = "2025-08-01", ActivatedAt = "2025-08-01" },
            new StudentGroup { StudentId = "s6", GroupId = "gArch", Status = "completed", IsActive = false, JoinedAt = "2025-01-10", ActivatedAt = "2025-01-10", LeftAt = "2025-06-30", FrozenAt = "2025-06-30" },
            // Yopilgan guruhning muzlatilgan a'zosi — "Kurslar analitikasi"da muzlatilgan bo'lib sanaladi.
            new StudentGroup { StudentId = "s2", GroupId = "gArch", Status = "frozen", JoinedAt = "2025-01-10", ActivatedAt = "2025-01-10", FrozenAt = "2025-06-30" },
            new StudentGroup { StudentId = "s3", GroupId = "g3", Status = "active", JoinedAt = "2025-09-01", ActivatedAt = "2025-09-01" });

        // Pul: s1 ning g1 dagi hisob/to'lovlari (+ BOSHQA guruhga teglangan to'lov — g1 balansiga kirmaydi).
        db.MonthlyCharges.AddRange(
            new MonthlyCharge { StudentId = "s1", GroupId = "g1", Month = "2025-09", Amount = 300_000, Discount = 50_000, Date = "2025-09-01" },
            new MonthlyCharge { StudentId = "s1", GroupId = "g1", Month = "2025-10", Amount = 300_000, Date = "2025-10-01" });
        db.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = "s1", GroupId = "g1", Direction = "income", Category = "tuition", Month = "2025-09", Date = "2025-09-05", Amount = 250_000 },
            new FinanceTransaction { StudentId = "s1", GroupId = "g2", Direction = "income", Category = "tuition", Month = "2025-10", Date = "2025-10-02", Amount = 100_000 });
        db.SaveChanges();
    }

    private static List<string?> Ids(JsonElement json, string array = "items", string prop = "id") =>
        json.GetProperty(array).EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    private static JsonElement Item(JsonElement json, string id, string array = "items", string prop = "id") =>
        json.GetProperty(array).EnumerateArray().Single(i => i.GetProperty(prop).GetString() == id);

    // ================================================================== groups_list

    [Fact]
    public async Task GroupsList_kurs_oqituvchi_xona_jadval_va_azolar_soni_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupsList());

        Assert.Equal(4, json.GetProperty("total").GetInt32());   // arxivdagi guruh sukut bo'yicha yo'q
        Assert.Equal(new[] { "g1", "g2", "gN", "g3" }, Ids(json));       // nom bo'yicha: English A1, English B1, Klub, Matematika 5
        var g1 = Item(json, "g1");
        Assert.Equal("Ingliz tili", g1.GetProperty("course").GetString());
        Assert.Equal("Dilnoza Karimova", g1.GetProperty("teacher").GetString());
        Assert.Equal("101", g1.GetProperty("room").GetString());
        Assert.Equal(new[] { "Du", "Ch" }, g1.GetProperty("dayLabels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(300_000, g1.GetProperty("monthlyFee").GetDecimal());
        var members = g1.GetProperty("members");
        Assert.Equal(1, members.GetProperty("active").GetInt32());   // s1 (arxivdagi s5 va chiqqan s4 sanalmaydi)
        Assert.Equal(1, members.GetProperty("trial").GetInt32());    // s2
        Assert.Equal(1, members.GetProperty("frozen").GetInt32());   // s3
        Assert.Equal("202", Item(json, "g2").GetProperty("room").GetString());   // eski matnli xona
        Assert.Equal(1, Item(json, "g2").GetProperty("members").GetProperty("trial").GetInt32());
        var club = Item(json, "gN");
        Assert.Equal(JsonValueKind.Null, club.GetProperty("course").ValueKind);   // kurssiz guruh
        Assert.Equal(0, club.GetProperty("dayLabels").GetArrayLength());
        Assert.Equal(4, host.State.RowCount);
        host.AssertReadsOnlyDeclared(typeof(GroupTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("english", null, null, null, new[] { "g1", "g2" })]
    [InlineData("ENGLISH", null, null, "all", new[] { "g1", "g2", "gArch" })]
    [InlineData(null, "c1", null, null, new[] { "g1", "g2" })]
    [InlineData(null, "c1", null, "all", new[] { "g1", "g2", "gArch" })]
    [InlineData(null, null, "t2", null, new[] { "g2" })]
    [InlineData(null, "c2", "t1", null, new[] { "g3" })]
    [InlineData(null, null, null, "archived", new[] { "gArch" })]
    [InlineData(null, null, null, "all", new[] { "g1", "g2", "gArch", "g3", "gN" })]
    [InlineData("yo'q", null, null, "all", new string[0])]
    public async Task GroupsList_filtrlari(string? search, string? courseId, string? teacherId, string? state, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin())
            .GroupsList(search, courseId, teacherId, state));

        Assert.Equal(expected.OrderBy(x => x), Ids(json).OrderBy(x => x));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GroupsList_sahifalash_va_notogri_holat()
    {
        using var host = new McpTestHost();
        Seed(host);

        var first = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupsList(page: 1, pageSize: 3));
        var second = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupsList(page: 2, pageSize: 3));

        Assert.Equal(new[] { "g1", "g2", "gN" }, Ids(first));
        Assert.Equal(new[] { "g3" }, Ids(second));
        Assert.Equal(4, second.GetProperty("total").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(1, host.State.RowCount);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupsList(state: "hammasi"));
        Assert.Contains("state", ex.Message);
    }

    // ================================================================== group_roster

    [Fact]
    public async Task Roster_hozirgi_azolar_sanoq_va_SHU_GURUH_balansi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster("g1");
        var json = McpTestHost.Parse(raw);

        // Sukut: faqat hozirgilar; arxivdagi o'quvchining tirik a'zoligi (s5) — umuman yo'q.
        Assert.Equal(new[] { "s1", "s2", "s3" }, Ids(json, "members", "studentId"));
        var counts = json.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("active").GetInt32());
        Assert.Equal(1, counts.GetProperty("trial").GetInt32());
        Assert.Equal(1, counts.GetProperty("frozen").GetInt32());
        Assert.Equal(1, counts.GetProperty("left").GetInt32());        // s4 — ro'yxatda yo'q, sanoqda bor
        Assert.Equal(0, counts.GetProperty("completed").GetInt32());
        Assert.Equal("English A1", json.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Ingliz tili", json.GetProperty("group").GetProperty("course").GetString());

        var ali = Item(json, "s1", "members", "studentId");
        Assert.Equal("active", ali.GetProperty("status").GetString());
        Assert.Equal(new[] { "+998935556677" }, ali.GetProperty("parentPhones").EnumerateArray().Select(x => x.GetString()));
        // 250 000 to'langan − (250 000 + 300 000) hisoblangan = −300 000; g2 ga teglangan to'lov kirmaydi.
        Assert.Equal(-300_000m, ali.GetProperty("balance").GetDecimal());
        Assert.Equal(1, ali.GetProperty("debtMonths").GetInt32());     // 2025-10 yopilmagan
        var expected = await GroupBalanceService.ForGroupAsync(host.Rw, "g1", ["s1", "s2", "s3"]);
        Assert.Equal(expected["s1"], ali.GetProperty("balance").GetDecimal());
        Assert.Equal(expected["s2"], Item(json, "s2", "members", "studentId").GetProperty("balance").GetDecimal());

        var charos = Item(json, "s3", "members", "studentId");
        Assert.Equal("frozen", charos.GetProperty("status").GetString());
        Assert.True(charos.GetProperty("yearFreeze").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("note").ValueKind);

        foreach (var secret in new[] { "maxfiy-surat", "maxfiy-passport", "/uploads/" })
            Assert.DoesNotContain(secret, raw);
        Assert.Equal(3, host.State.RowCount);
        host.AssertReadsOnlyDeclared(typeof(GroupTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Roster_chiqqanlar_va_tugatganlar_includeLeft_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var g1 = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster("g1", includeLeft: true));
        Assert.Equal(new[] { "s1", "s2", "s3", "s4" }, Ids(g1, "members", "studentId"));   // faollar tepada, chiqqan oxirida
        var davron = Item(g1, "s4", "members", "studentId");
        Assert.Equal("left", davron.GetProperty("status").GetString());
        Assert.Equal("2025-09-05", davron.GetProperty("leftAt").GetString());

        var old = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster("gArch", includeLeft: true));
        Assert.Equal("completed", Item(old, "s6", "members", "studentId").GetProperty("status").GetString());
        Assert.Equal("frozen", Item(old, "s2", "members", "studentId").GetProperty("status").GetString());
        Assert.Equal(1, old.GetProperty("counts").GetProperty("completed").GetInt32());
        Assert.True(old.GetProperty("group").GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task Roster_moliya_ruxsatisiz_balans_YOQ()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<GroupTools>(McpTestHost.Staff("classes.list")).GroupRoster("g1");
        var json = McpTestHost.Parse(raw);

        var ali = Item(json, "s1", "members", "studentId");
        Assert.Equal(JsonValueKind.Null, ali.GetProperty("balance").ValueKind);
        Assert.Equal(JsonValueKind.Null, ali.GetProperty("debtMonths").ValueKind);
        Assert.Contains("moliya", json.GetProperty("note").GetString());
        Assert.DoesNotContain("300000", raw);
        Assert.DoesNotContain("\"MonthlyCharges\"", string.Join("\n", host.Sql));     // pul jadvallari umuman o'qilmadi
        Assert.DoesNotContain("\"FinanceTransactions\"", string.Join("\n", host.Sql));

        var withFinance = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Staff("classes.list", "finance.main")).GroupRoster("g1"));
        Assert.Equal(-300_000m, Item(withFinance, "s1", "members", "studentId").GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Roster_nom_boyicha_qidirish_aniq_moslik_va_nomzodlar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var exact = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster(name: "english a1"));
        Assert.Equal("g1", exact.GetProperty("group").GetProperty("id").GetString());

        var one = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster(name: "klu"));
        Assert.Equal("gN", one.GetProperty("group").GetProperty("id").GetString());
        Assert.Equal(0, one.GetProperty("members").GetArrayLength());

        var many = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster(name: "english"));
        Assert.Equal(new[] { "g1", "g2", "gArch" }, Ids(many, "candidates"));   // arxivdagi oxirida
        Assert.False(many.TryGetProperty("members", out _));

        var none = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster(name: "yo'q guruh"));
        Assert.Contains("topilmadi", none.GetProperty("message").GetString());
        var noneById = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster("yo'q-id"));
        Assert.Contains("topilmadi", noneById.GetProperty("message").GetString());
        Assert.Equal(0, host.State.RowCount);

        await Assert.ThrowsAsync<McpException>(() => host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster());
    }

    // ================================================================== rooms_list

    [Fact]
    public async Task Rooms_guruhlar_soni_RoomId_va_eski_matnli_xona_boyicha()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Staff("classes.rooms")).RoomsList());

        Assert.Equal(new[] { "r1", "r2" }, Ids(json));                     // nofaol xona sukut bo'yicha yo'q
        Assert.Equal(2, Item(json, "r1").GetProperty("groups").GetInt32());   // g1, g3 (arxivdagi gArch emas)
        Assert.Equal(1, Item(json, "r2").GetProperty("groups").GetInt32());   // g2 — matnli "202"
        Assert.Equal(12, Item(json, "r1").GetProperty("capacity").GetInt32());
        Assert.Equal("A", Item(json, "r1").GetProperty("building").GetString());

        var all = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Staff("classes.list")).RoomsList(includeInactive: true));
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.False(Item(all, "r3").GetProperty("isActive").GetBoolean());
        Assert.Equal(1, Item(all, "r3").GetProperty("groups").GetInt32());   // gN
        host.AssertReadsOnlyDeclared(typeof(GroupTools));
        host.AssertNoSecretColumns();
    }

    // ================================================================== courses_list

    [Fact]
    public async Task Courses_narxlar_guruhlar_va_hozirgi_oquvchilar_kurslar_analitikasi_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Staff("schedule.courses")).CoursesList());

        Assert.Equal(new[] { "c1", "c2", "c3" }, Ids(json));
        var c1 = Item(json, "c1");
        Assert.Equal(300_000, c1.GetProperty("monthlyPrice").GetDecimal());
        Assert.Equal(25_000, c1.GetProperty("lessonPrice").GetDecimal());
        var bands = c1.GetProperty("gradePrices").EnumerateArray().ToList();
        Assert.Equal(new[] { 1, 5 }, bands.Select(b => b.GetProperty("gradeFrom").GetInt32()));
        Assert.Equal(320_000, bands[1].GetProperty("monthlyPrice").GetDecimal());
        Assert.Equal(2, c1.GetProperty("groups").GetInt32());     // g1, g2 (arxivdagi emas)
        Assert.Equal(2, c1.GetProperty("teachers").GetInt32());
        var st = c1.GetProperty("students");
        Assert.Equal(1, st.GetProperty("active").GetInt32());     // s1 (arxivdagi s5 kirmaydi)
        Assert.Equal(2, st.GetProperty("trial").GetInt32());      // s1 (g2), s2
        Assert.Equal(2, st.GetProperty("frozen").GetInt32());     // s3 (g1), s2 (yopilgan gArch)
        Assert.Equal(3, st.GetProperty("total").GetInt32());      // s1, s2, s3 — har biri BIR marta
        var c2 = Item(json, "c2");
        Assert.Equal(1, c2.GetProperty("groups").GetInt32());
        Assert.Equal(1, c2.GetProperty("students").GetProperty("active").GetInt32());
        var c3 = Item(json, "c3");
        Assert.Equal(0, c3.GetProperty("groups").GetInt32());
        Assert.Equal(0, c3.GetProperty("students").GetProperty("total").GetInt32());
        Assert.Equal(0, c3.GetProperty("gradePrices").GetArrayLength());
        host.AssertReadsOnlyDeclared(typeof(GroupTools));
        host.AssertNoSecretColumns();
    }

    // ================================================================== ruxsat

    [Fact]
    public async Task Bolim_ruxsatisiz_HAR_vosita_RAD_etiladi_va_bazaga_sorov_ketmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        GroupTools Tool() => host.Tool<GroupTools>(McpTestHost.Staff("students.list"));

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().GroupsList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().GroupRoster("g1"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().GroupJournal("g1", "2025-09"));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().RoomsList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => Tool().CoursesList());
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    [Fact]
    public async Task Sahifa_kaliti_faqat_ozi_ochadigan_vositani_ochadi()
    {
        using var host = new McpTestHost();
        Seed(host);

        // Xonalar kaliti guruhlar ro'yxatini ochmaydi; guruhlar kaliti kurslar katalogini ochmaydi.
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<GroupTools>(McpTestHost.Staff("classes.rooms")).GroupsList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<GroupTools>(McpTestHost.Staff("classes.list")).CoursesList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<GroupTools>(McpTestHost.Staff("schedule.courses")).GroupRoster("g1"));
        Assert.Empty(host.Sql);

        var ok = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Admin()).GroupsList());
        Assert.Equal(4, ok.GetProperty("total").GetInt32());
    }

    // ================================================================== group_journal

    /// <summary>
    /// 2025-yil sentabr: guruh Du/Ch kunlari (1, 3, 8, 10, 15, 17→18 ko'chirilgan, 22, 24, 29 — 9 dars).
    /// O'tilgan: 1, 3, 8, 15 (davomat olingan) va 10 (davomat OLINMAGAN); 22 — o'tilmagan.
    /// </summary>
    private static void SeedJournal(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Group { Id = "gJ", Name = "Journal A", CourseId = "c1", TeacherId = "t1", Days = [0, 2], StartDate = "2025-09-01" },
            new Group { Id = "gNoCourse", Name = "Kurssiz", Days = [0] },
            new Group { Id = "gNoDays", Name = "Kunsiz", CourseId = "c1" },
            new AbsenceReason { Id = "rAbs", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "rLate", Name = "Kech qoldi", Short = "K", IsLate = true },
            new LessonReschedule { ClassId = "gJ", FromDate = "2025-09-17", ToDate = "2025-09-18" });

        LessonNote Note(string date, bool conducted, bool taken, string topic = "") =>
            new() { ClassId = "gJ", SubjectId = "c1", Date = date, Period = 1, Conducted = conducted, AttendanceTaken = taken, Topic = topic };
        db.LessonNotes.AddRange(
            Note("2025-09-01", true, true, "Present Simple"),
            Note("2025-09-03", true, true),
            Note("2025-09-08", true, true),
            Note("2025-09-10", true, false),
            Note("2025-09-15", true, true),
            Note("2025-09-22", false, true),   // o'tilmagan darsdagi "davomat olindi" — hisobga olinmaydi
            new LessonNote { ClassId = "gNoCourse", SubjectId = "", Date = "2025-09-01", Conducted = true, AttendanceTaken = true });

        db.Students.AddRange(
            new Student { Id = "sA", FullName = "Aziz Karimov" },
            new Student { Id = "sB", FullName = "Bekzod Sobirov" },
            new Student { Id = "sC", FullName = "Davron Ketgan" },
            new Student { Id = "sD", FullName = "Eldor Kechikkan" },
            new Student { Id = "sE", FullName = "Farrux Arxiv", IsArchived = true },
            new Student { Id = "sF", FullName = "Gulnora Muzlagan" });
        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "sA", GroupId = "gJ", Status = "active", JoinedAt = "2025-08-20", ActivatedAt = "2025-08-20", RecordedAt = "2025-08-20" },
            // 9-sentabrda qo'shilgan sinovdagi o'quvchi: 1, 3, 8 — a'zolikdan oldin.
            new StudentGroup { StudentId = "sB", GroupId = "gJ", Status = "trial", JoinedAt = "2025-09-09", RecordedAt = "2025-09-09" },
            // 5-sentabrda guruhdan chiqqan.
            new StudentGroup { StudentId = "sC", GroupId = "gJ", Status = "active", IsActive = false, JoinedAt = "2025-06-01", ActivatedAt = "2025-06-01", LeftAt = "2025-09-05", RecordedAt = "2025-06-01" },
            // Orqaga sanalgan: 1-sentabrdan aktiv, lekin tizimga 9-sentabrda kiritilgan.
            new StudentGroup { StudentId = "sD", GroupId = "gJ", Status = "active", JoinedAt = "2025-09-01", ActivatedAt = "2025-09-01", RecordedAt = "2025-09-09T10:15:00Z" },
            // Arxivdagi o'quvchining tirik a'zoligi (eski ma'lumot).
            new StudentGroup { StudentId = "sE", GroupId = "gJ", Status = "active", JoinedAt = "2025-08-01", ActivatedAt = "2025-08-01" },
            // 4-sentabrda muzlatilgan.
            new StudentGroup { StudentId = "sF", GroupId = "gJ", Status = "frozen", JoinedAt = "2025-08-01", ActivatedAt = "2025-08-01", FrozenAt = "2025-09-04", RecordedAt = "2025-08-01" },
            new StudentGroup { StudentId = "sA", GroupId = "gNoCourse", Status = "active", JoinedAt = "2025-08-20", ActivatedAt = "2025-08-20" },
            new StudentGroup { StudentId = "sA", GroupId = "gNoDays", Status = "active", JoinedAt = "2025-08-20", ActivatedAt = "2025-08-20" });

        JournalEntry Entry(string student, string date, string? reason = null, int? grade = null, bool present = false, string subject = "c1") =>
            new() { ClassId = "gJ", SubjectId = subject, StudentId = student, Date = date, Period = 1, ReasonId = reason, Grade = grade, Present = present };
        db.JournalEntries.AddRange(
            Entry("sA", "2025-09-03", reason: "rAbs"),
            Entry("sA", "2025-09-08", reason: "rLate"),
            Entry("sA", "2025-09-15", grade: 5),
            Entry("sA", "2025-09-01", reason: "rAbs", subject: "c-old"),   // eski kursning yozuvi — hisobga olinmaydi
            Entry("sC", "2025-09-03", present: true),
            Entry("sF", "2025-09-15", present: true));                      // muzlatishdan keyin — hisobga olinmaydi
        db.SaveChanges();
    }

    private static JsonElement Row(JsonElement json, string studentId) => Item(json, studentId, "students", "studentId");

    [Fact]
    public async Task Journal_darslar_va_har_oquvchi_xulosasi_panel_qoidalari_bilan()
    {
        using var host = new McpTestHost();
        SeedJournal(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.Staff("classes.list")).GroupJournal("gJ", "2025-09"));

        // Darslar: ko'chirilgan 17 → 18.
        var dates = json.GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("date").GetString()).ToList();
        Assert.Equal(new[] { "2025-09-01", "2025-09-03", "2025-09-08", "2025-09-10", "2025-09-15", "2025-09-18", "2025-09-22", "2025-09-24", "2025-09-29" }, dates);
        Assert.Equal("Present Simple", json.GetProperty("lessons")[0].GetProperty("topic").GetString());
        var summary = json.GetProperty("summary");
        Assert.Equal(9, summary.GetProperty("planned").GetInt32());
        Assert.Equal(5, summary.GetProperty("conducted").GetInt32());
        Assert.Equal(4, summary.GetProperty("attendanceTaken").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("note").ValueKind);

        // O'TGAN oyda arxivdagi o'quvchi ham o'qigan joyida ko'rinadi (panel kabi).
        Assert.Equal(new[] { "sA", "sB", "sC", "sD", "sE", "sF" }, Ids(json, "students", "studentId"));
        Assert.Equal(6, host.State.RowCount);

        // Aziz: 1 — yozuvsiz, davomat olingan → KELDI; 3 — Sababsiz; 8 — KECH (keldi, kelmadi EMAS);
        // 10 — davomat olinmagan, yozuvsiz → belgilanmagan; 15 — baho → keldi. Eski kurs yozuvi e'tiborsiz.
        var aziz = Row(json, "sA");
        Assert.Equal(5, aziz.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(3, aziz.GetProperty("present").GetInt32());
        Assert.Equal(1, aziz.GetProperty("late").GetInt32());
        Assert.Equal(1, aziz.GetProperty("absent").GetInt32());
        Assert.Equal(1, aziz.GetProperty("unmarked").GetInt32());
        Assert.Equal(0, aziz.GetProperty("unknown").GetInt32());
        var reasons = aziz.GetProperty("absenceReasons").EnumerateArray().ToList();
        Assert.Single(reasons);
        Assert.Equal("Sababsiz", reasons[0].GetProperty("reason").GetString());
        Assert.Equal(1, reasons[0].GetProperty("count").GetInt32());
        Assert.Equal(1, aziz.GetProperty("grades").GetProperty("count").GetInt32());
        Assert.Equal(5, aziz.GetProperty("grades").GetProperty("average").GetDouble());

        // Bekzod: a'zolikdan oldingi 1, 3, 8 hisoblanmaydi → 10 (belgilanmagan) va 15 (keldi).
        var bekzod = Row(json, "sB");
        Assert.Equal("trial", bekzod.GetProperty("status").GetString());
        Assert.Equal(2, bekzod.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(1, bekzod.GetProperty("present").GetInt32());
        Assert.Equal(1, bekzod.GetProperty("unmarked").GetInt32());
        Assert.Equal(0, bekzod.GetProperty("absent").GetInt32());
        Assert.Equal(JsonValueKind.Null, bekzod.GetProperty("grades").GetProperty("average").ValueKind);

        // Davron: 5-sentabrda chiqqan → faqat 1 va 3.
        var davron = Row(json, "sC");
        Assert.Equal("left", davron.GetProperty("status").GetString());
        Assert.Equal(2, davron.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(2, davron.GetProperty("present").GetInt32());

        // Eldor: tizimga 9-sentabrda kiritilgan — 1, 3, 8 dagi belgisiz darslar NOMA'LUM, hisobga kirmaydi.
        var eldor = Row(json, "sD");
        Assert.Equal(2, eldor.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(1, eldor.GetProperty("present").GetInt32());
        Assert.Equal(3, eldor.GetProperty("unknown").GetInt32());

        // Farrux (arxivdagi, RecordedAt yo'q): 1, 3, 8, 15 — keldi; 10 — belgilanmagan.
        var farrux = Row(json, "sE");
        Assert.Equal(5, farrux.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(4, farrux.GetProperty("present").GetInt32());

        // Gulnora: 4-sentabrda muzlatilgan — 15 dagi "keldi" belgisi ham hisoblanmaydi.
        var gulnora = Row(json, "sF");
        Assert.Equal("frozen", gulnora.GetProperty("status").GetString());
        Assert.Equal(2, gulnora.GetProperty("lessonsHeld").GetInt32());
        Assert.Equal(2, gulnora.GetProperty("present").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(GroupTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("2025-10", new[] { "sA", "sB", "sD", "sE" })]         // chiqqan (sC) va sentabrda muzlatilgan (sF) yo'q
    [InlineData("2025-08", new[] { "sA", "sC", "sE", "sF" })]         // sB va sD hali qo'shilmagan
    [InlineData("2025-05", new string[0])]                             // hech kim hali guruhda emas
    public async Task Journal_faqat_shu_oyda_guruhda_bolganlar(string month, string[] expected)
    {
        using var host = new McpTestHost();
        SeedJournal(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("gJ", month));

        Assert.Equal(expected, Ids(json, "students", "studentId"));
    }

    [Fact]
    public async Task Journal_joriy_oyda_arxivdagi_oquvchi_YASHIRILADI()
    {
        using var host = new McpTestHost();
        SeedJournal(host);

        var json = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("gJ"));

        Assert.Equal(McpToolContext.Today.ToString("yyyy-MM"), json.GetProperty("month").GetString());
        Assert.Equal(new[] { "sA", "sB", "sD" }, Ids(json, "students", "studentId"));   // sE — arxivda, sC/sF — sentabrda ketgan
    }

    [Fact]
    public async Task Journal_kurssiz_va_dars_kunisiz_guruh_tushunarli_izoh_bilan()
    {
        using var host = new McpTestHost();
        SeedJournal(host);

        var noCourse = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("gNoCourse", "2025-09"));
        Assert.Equal(5, noCourse.GetProperty("summary").GetProperty("planned").GetInt32());     // sentabrdagi 5 dushanba
        Assert.Equal(0, noCourse.GetProperty("summary").GetProperty("conducted").GetInt32());   // kurssiz — jurnal yo'q
        Assert.Contains("kurs", noCourse.GetProperty("note").GetString());
        Assert.Equal(0, Row(noCourse, "sA").GetProperty("lessonsHeld").GetInt32());

        var noDays = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("gNoDays", "2025-09"));
        Assert.Equal(0, noDays.GetProperty("lessons").GetArrayLength());
        Assert.Contains("dars kunlari", noDays.GetProperty("note").GetString());
        Assert.Equal(new[] { "sA" }, Ids(noDays, "students", "studentId"));   // a'zo baribir ko'rinadi
    }

    [Fact]
    public async Task Journal_notogri_oy_va_topilmagan_guruh()
    {
        using var host = new McpTestHost();
        SeedJournal(host);
        var tool = host.Tool<GroupTools>(McpTestHost.SuperAdmin());

        Assert.Contains("month", (await Assert.ThrowsAsync<McpException>(() => tool.GroupJournal("gJ", "2025-13"))).Message);
        await Assert.ThrowsAsync<McpException>(() => tool.GroupJournal("gJ", "sentabr"));

        var none = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("yo'q", "2025-09"));
        Assert.Contains("topilmadi", none.GetProperty("message").GetString());
        var empty = McpTestHost.Parse(await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("  ", "2025-09"));
        Assert.Contains("topilmadi", empty.GetProperty("message").GetString());
        Assert.Equal(0, host.State.RowCount);
    }

    [Fact]
    public async Task Vositalar_bazada_HECH_NARSANI_ozgartirmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        string Snapshot() => string.Join("|",
            host.Rw.StudentGroups.ToList().OrderBy(m => m.StudentId).ThenBy(m => m.GroupId)
                .Select(m => $"{m.StudentId}:{m.GroupId}:{m.Status}:{m.IsActive}:{m.FrozenAt}:{m.LeftAt}")
                .Concat(host.Rw.MonthlyCharges.ToList().OrderBy(c => c.Month).Select(c => $"{c.StudentId}:{c.Month}:{c.Amount}")));
        host.Rw.ChangeTracker.Clear();   // both snapshots read the database, not the seeded instances
        var before = Snapshot();

        await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupsList(state: "all");
        await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupRoster("g1", includeLeft: true);
        await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).GroupJournal("g1", "2025-09");
        await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).RoomsList(includeInactive: true);
        await host.Tool<GroupTools>(McpTestHost.SuperAdmin()).CoursesList();

        host.Rw.ChangeTracker.Clear();
        Assert.Equal(before, Snapshot());
    }
}
