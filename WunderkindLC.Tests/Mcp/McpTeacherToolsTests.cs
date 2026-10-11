using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `teachers_list`, `teacher_profile`, `staff_list`, `substitutions_list` — panel bilan bir xil raqamlar,
/// filtrlar, sahifalash, ruxsat, maosh yashirilishi va maxfiylik (login, parol, qurilma id, rasm manzili chiqmaydi).
/// </summary>
public class McpTeacherToolsTests
{
    private static List<string?> Ids(JsonElement json, string prop = "id") =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    private static string D(int offset) => McpToolContext.Iso(McpToolContext.Today.AddDays(offset));

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" },
            new Room { Id = "r1", Name = "101", IsActive = true },
            new Teacher
            {
                Id = "t1", FullName = "Dilnoza Karimova", Phone = "+998901112233", SubjectIds = ["c1"], Category = "Oliy",
                SalaryMode = "fixed", Salary = 3_000_000m, BonusPct = 5m, DeviceUserId = "dev-777",
                PhotoUrl = "/uploads/t1-rasm.jpg", UserId = "user-t1", Permissions = ["perm-maxfiy"],
            },
            new Teacher
            {
                Id = "t2", FullName = "Rustam Saidov", Phone = "+998935554433", SubjectIds = ["c2", "c1"],
                SalaryMode = "percent", SalaryPercent = 40m, IsSupport = true,
            },
            new Teacher { Id = "t3", FullName = "Anvar Arxivda", IsArchived = true, ArchivedAt = "2026-05-01", SubjectIds = ["c2"] },
            new Teacher { Id = "t4", FullName = "Bekzod Bloklangan", IsBlocked = true },
            new Group
            {
                Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", Days = [0, 2, 4],
                StartTime = "14:00", EndTime = "15:30", RoomId = "r1", StartDate = "2026-01-01",
            },
            new Group
            {
                Id = "g2", Name = "English B2", CourseId = "c1", TeacherId = "t1", Days = [1, 3],
                StartTime = "10:00", EndTime = "11:30", Room = "Zal", StartDate = "2026-01-01",
            },
            new Group { Id = "g3", Name = "Math B", CourseId = "c2", TeacherId = "t2", Days = [0], StartDate = "2026-01-01" },
            new Group { Id = "g4", Name = "Eski guruh", CourseId = "c1", TeacherId = "t1", Days = [0], IsArchived = true });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Karimov" },
            new Student { Id = "s5", FullName = "Eldor Arxivda", IsArchived = true });
        StudentGroup M(string sid, string gid, string status = "active", bool open = true) =>
            new() { StudentId = sid, GroupId = gid, Status = status, IsActive = open, JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01" };
        db.StudentGroups.AddRange(
            M("s1", "g1"), M("s2", "g1", "trial"), M("s3", "g1", "frozen"), M("s5", "g1"),
            M("s1", "g2"), M("s4", "g2", open: false),
            M("s4", "g3"),
            M("s2", "g4"));
        db.SaveChanges();
    }

    // ================================================================== teachers_list

    [Fact]
    public async Task Oqituvchilar_royxati_guruh_va_oquvchi_sonlari_hamda_maosh()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).TeachersList();
        var json = McpTestHost.Parse(raw);

        // Sukut — ishlayotganlar: arxivdagi (t3) va bloklangan (t4) kirmaydi.
        Assert.Equal(new[] { "t1", "t2" }, Ids(json));
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var t1 = json.GetProperty("items")[0];
        Assert.Equal("Dilnoza Karimova", t1.GetProperty("fullName").GetString());
        Assert.Equal("+998901112233", t1.GetProperty("phone").GetString());
        Assert.Equal("Oliy", t1.GetProperty("category").GetString());
        // Guruhlar: g1, g2 (arxivdagi g4 emas). O'quvchilar: s1 (ikki guruhda — bir marta), s2, s3;
        // arxivdagi s5 va guruhdan chiqqan s4 sanalmaydi.
        Assert.Equal(2, t1.GetProperty("groups").GetInt32());
        Assert.Equal(3, t1.GetProperty("students").GetInt32());
        Assert.Equal("Ingliz tili", t1.GetProperty("subjects")[0].GetProperty("name").GetString());
        var salary = t1.GetProperty("salary");
        Assert.Equal("fixed", salary.GetProperty("mode").GetString());
        Assert.Equal(3_000_000m, salary.GetProperty("fixed").GetDecimal());
        Assert.Equal(5m, salary.GetProperty("bonusPct").GetDecimal());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("note").ValueKind);

        var t2 = json.GetProperty("items")[1];
        Assert.Equal(1, t2.GetProperty("groups").GetInt32());
        Assert.Equal(1, t2.GetProperty("students").GetInt32());
        Assert.True(t2.GetProperty("isSupport").GetBoolean());
        Assert.Equal(40m, t2.GetProperty("salary").GetProperty("percent").GetDecimal());
        Assert.Equal(new[] { "Matematika", "Ingliz tili" },
            t2.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("name").GetString()));

        Assert.DoesNotContain("dev-777", raw);
        Assert.DoesNotContain("user-t1", raw);
        Assert.DoesNotContain("perm-maxfiy", raw);
        Assert.DoesNotContain("/uploads/", raw);

        host.AssertReadsOnlyDeclared(typeof(TeacherTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Oqituvchilar_royxati_holat_qidiruv_va_kurs_filtrlari()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<TeacherTools>(McpTestHost.Admin());

        Assert.Equal(new[] { "t3" }, Ids(McpTestHost.Parse(await tools.TeachersList(state: "archived"))));
        Assert.Equal(new[] { "t4" }, Ids(McpTestHost.Parse(await tools.TeachersList(state: "blocked"))));
        Assert.Equal(new[] { "t3", "t4", "t1", "t2" }, Ids(McpTestHost.Parse(await tools.TeachersList(state: "all"))));
        Assert.Equal(new[] { "t2" }, Ids(McpTestHost.Parse(await tools.TeachersList(search: "rustam"))));
        Assert.Equal(new[] { "t1" }, Ids(McpTestHost.Parse(await tools.TeachersList(search: "1112233"))));
        Assert.Equal(new[] { "t2" }, Ids(McpTestHost.Parse(await tools.TeachersList(courseId: "c2"))));
        Assert.Equal(new[] { "t1", "t2" }, Ids(McpTestHost.Parse(await tools.TeachersList(courseId: "c1"))));
        Assert.Equal(new[] { "t3", "t2" }, Ids(McpTestHost.Parse(await tools.TeachersList(state: "all", courseId: "c2"))));
        await Assert.ThrowsAsync<McpException>(() => tools.TeachersList(state: "yoq"));
    }

    [Fact]
    public async Task Oqituvchilar_royxati_sahifalanadi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).TeachersList(page: 2, pageSize: 1));

        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(2, json.GetProperty("page").GetInt32());
        Assert.Equal(new[] { "t2" }, Ids(json));
    }

    [Fact]
    public async Task Moliya_ruxsatisiz_maosh_sozlamalari_YASHIRINADI()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<TeacherTools>(McpTestHost.Staff("teachers.list")).TeachersList());
        Assert.All(json.GetProperty("items").EnumerateArray(),
            i => Assert.Equal(JsonValueKind.Null, i.GetProperty("salary").ValueKind));
        Assert.Contains("moliya", json.GetProperty("note").GetString());

        var profile = McpTestHost.Parse(await host.Tool<TeacherTools>(McpTestHost.Staff("teachers.list")).TeacherProfile("t1"));
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("salary").ValueKind);

        var withFinance = McpTestHost.Parse(
            await host.Tool<TeacherTools>(McpTestHost.Staff("teachers.list", "finance.main")).TeachersList());
        Assert.Equal("fixed", withFinance.GetProperty("items")[0].GetProperty("salary").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Oqituvchilar_bolimi_ruxsatisiz_RAD_etiladi_va_hech_narsa_oqilmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<TeacherTools>(McpTestHost.Staff("students.list"));

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => tools.TeachersList());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => tools.TeacherProfile("t1"));
        Assert.Empty(host.Sql);
        Assert.True(host.State.Denied);
    }

    // ================================================================== teacher_profile

    private static void SeedProfileExtras(McpTestHost host)
    {
        var db = host.Rw;
        SubstituteTeacherAssignment A(string id, string gid, string orig, string sub, int offset, bool active = true) => new()
        {
            Id = id, GroupId = gid, OriginalTeacherId = orig, SubstituteTeacherId = sub, Date = D(offset),
            SelectedDates = [D(offset)], Reason = "Kasal", IsActive = active,
        };
        db.SubstituteTeacherAssignments.AddRange(
            A("sa1", "g1", "t1", "t2", 3),
            A("sa2", "g3", "t2", "t1", -5),
            A("sa3", "g1", "t1", "t2", -60),          // oynadan tashqarida
            A("sa4", "g1", "t1", "t2", 1, false));    // bekor qilingan
        db.JournalEntries.AddRange(
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s1", Date = "2026-09-14", Period = 1, Grade = 5 },
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s1", Date = "2026-09-16", Period = 1, Grade = 4 },
            // mezondan yozilgan baho ballga QO'SHILMAYDI (checklar sifatida sanaladi)
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s2", Date = "2026-09-14", Period = 1, Grade = 5, GradeFromCriteria = true });
        db.CriterionGrades.AddRange(
            new CriterionGrade { GroupId = "g1", StudentId = "s2", CriterionId = "k1", Date = "2026-09-14", Done = true },
            new CriterionGrade { GroupId = "g1", StudentId = "s2", CriterionId = "k2", Date = "2026-09-14", Done = true },
            new CriterionGrade { GroupId = "g1", StudentId = "s3", CriterionId = "k1", Date = "2026-09-14", Done = false });
        db.SaveChanges();
    }

    [Fact]
    public async Task Oqituvchi_kartochkasi_guruhlar_orinbosarlik_va_reyting()
    {
        using var host = new McpTestHost();
        Seed(host);
        SeedProfileExtras(host);

        var raw = await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).TeacherProfile("t1");
        var json = McpTestHost.Parse(raw);

        Assert.Equal("Dilnoza Karimova", json.GetProperty("fullName").GetString());
        Assert.Equal("fixed", json.GetProperty("salary").GetProperty("mode").GetString());

        var groups = json.GetProperty("groups");
        Assert.Equal(new[] { "g1", "g2" }, groups.EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        var g1 = groups[0];
        Assert.Equal("Ingliz tili", g1.GetProperty("course").GetString());
        Assert.Equal("101", g1.GetProperty("room").GetString());
        Assert.Equal(new[] { "Du", "Ch", "Ju" }, g1.GetProperty("dayLabels").EnumerateArray().Select(d => d.GetString()));
        var members = g1.GetProperty("members");
        Assert.Equal(1, members.GetProperty("active").GetInt32());   // arxivdagi s5 sanalmaydi
        Assert.Equal(1, members.GetProperty("trial").GetInt32());
        Assert.Equal(1, members.GetProperty("frozen").GetInt32());
        Assert.Equal("Zal", groups[1].GetProperty("room").GetString());

        // ±30 kun, faqat faollari: sa2 (o'rinbosar sifatida), sa1 (asosiy sifatida); sanasi bo'yicha.
        var subs = json.GetProperty("substitutions").GetProperty("items");
        Assert.Equal(new[] { "sa2", "sa1" }, subs.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal("substitute", subs[0].GetProperty("role").GetString());
        Assert.Equal("Math B", subs[0].GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("original", subs[1].GetProperty("role").GetString());
        Assert.Equal("Rustam Saidov", subs[1].GetProperty("substituteTeacher").GetProperty("name").GetString());
        Assert.Equal(D(3), subs[1].GetProperty("datesInRange")[0].GetString());

        // Reyting — o'qituvchi sahifasidagi «Reyting» tabi (StudentBallService.TeacherAsync).
        var rating = json.GetProperty("rating");
        Assert.Equal("umumiy", rating.GetProperty("month").GetString());
        Assert.Equal(2, rating.GetProperty("groupsCount").GetInt32());
        Assert.Equal(3, rating.GetProperty("studentsCount").GetInt32());
        Assert.Equal(4, rating.GetProperty("rowsCount").GetInt32());
        var top = rating.GetProperty("top");
        Assert.Equal("s1", top[0].GetProperty("studentId").GetString());
        Assert.Equal(9, top[0].GetProperty("ball").GetInt32());
        Assert.Equal(1, top[0].GetProperty("rank").GetInt32());
        Assert.Equal("s2", top[1].GetProperty("studentId").GetString());
        Assert.Equal(2, top[1].GetProperty("ball").GetInt32());   // 2 ta bajarilgan mezon, mezon bahosi qo'shilmaydi
        Assert.Equal(2, top[1].GetProperty("rank").GetInt32());

        Assert.DoesNotContain("dev-777", raw);
        Assert.DoesNotContain("user-t1", raw);
        Assert.DoesNotContain("perm-maxfiy", raw);
        Assert.DoesNotContain("/uploads/", raw);

        host.AssertReadsOnlyDeclared(typeof(TeacherTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Oqituvchi_kartochkasi_ism_boyicha_nomzodlar_va_topilmadi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<TeacherTools>(McpTestHost.SuperAdmin());

        var many = McpTestHost.Parse(await tools.TeacherProfile(name: "a"));
        Assert.True(many.GetProperty("candidates").GetArrayLength() > 1);

        var one = McpTestHost.Parse(await tools.TeacherProfile(name: "rustam"));
        Assert.Equal("t2", one.GetProperty("id").GetString());
        Assert.Equal(1, one.GetProperty("rating").GetProperty("rowsCount").GetInt32());   // s4 — Math B da

        var none = McpTestHost.Parse(await tools.TeacherProfile("yoq-id"));
        Assert.Equal("O'qituvchi topilmadi.", none.GetProperty("message").GetString());

        await Assert.ThrowsAsync<McpException>(() => tools.TeacherProfile());
        await Assert.ThrowsAsync<McpException>(() => tools.TeacherProfile("t1", ratingMonth: "2026/09"));
    }

    [Fact]
    public async Task Oqituvchi_reytingi_oy_boyicha()
    {
        using var host = new McpTestHost();
        Seed(host);
        SeedProfileExtras(host);

        var json = McpTestHost.Parse(await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).TeacherProfile("t1", ratingMonth: "2026-08"));
        var rating = json.GetProperty("rating");

        Assert.Equal("2026-08", rating.GetProperty("month").GetString());
        Assert.Equal(0, rating.GetProperty("top")[0].GetProperty("ball").GetInt32());   // avgustda baho yo'q
    }

    // ================================================================== staff_list

    private static void SeedStaff(McpTestHost host)
    {
        var db = host.Rw;
        db.StaffRoleTemplates.Add(new StaffRoleTemplate { Id = "rt1", Code = "cashier", Name = "Kassir" });
        AppUser U(string id, string name, string role, string phone = "", string position = "", string? template = null) => new()
        {
            Id = id, FullName = name, Role = role, Phone = phone, Position = position, RoleTemplateId = template,
            Email = id + "@markaz.uz", PasswordHash = "maxfiy-xesh-" + id, InitialPassword = "boshlangich-" + id,
            Permissions = ["finance.main"],
        };
        db.Users.AddRange(
            U("u3", "Kassir Ali", Roles.Staff, "+998901234567", "Kassir", "rt1"),
            U("u4", "Operator Vali", Roles.Staff, "+998907654321", "Operator"),
            U("u2", "Admin Aka", Roles.Admin, "+998900000002"),
            U("u1", "Boss Opa", Roles.SuperAdmin, "+998900000001"),
            U("u5", "O'qituvchi Akkaunt", Roles.Teacher),
            U("u6", "O'quvchi Akkaunt", Roles.Student));
        db.SaveChanges();
    }

    [Fact]
    public async Task Xodimlar_royxati_panel_akkauntlari_tartib_va_rol_shabloni()
    {
        using var host = new McpTestHost();
        SeedStaff(host);

        var raw = await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).StaffList();
        var json = McpTestHost.Parse(raw);

        // Superadmin, admin, keyin xodimlar (ism bo'yicha); o'qituvchi va o'quvchi akkaunti yo'q.
        Assert.Equal(new[] { "u1", "u2", "u3", "u4" }, Ids(json));
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        var kassir = json.GetProperty("items")[2];
        Assert.Equal("Kassir", kassir.GetProperty("position").GetString());
        Assert.Equal("+998901234567", kassir.GetProperty("phone").GetString());
        Assert.Equal("staff", kassir.GetProperty("role").GetString());
        Assert.Equal("Kassir", kassir.GetProperty("staffRole").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("items")[3].GetProperty("staffRole").ValueKind);

        Assert.DoesNotContain("@markaz.uz", raw);
        Assert.DoesNotContain("maxfiy-xesh", raw);
        Assert.DoesNotContain("boshlangich", raw);
        Assert.DoesNotContain("finance.main", raw);
        // Faqat ruxsat berilgan ustunlar so'ralgan.
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"Email\"") || s.Contains("\"Permissions\""));

        host.AssertReadsOnlyDeclared(typeof(TeacherTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Xodimlar_royxati_rol_va_qidiruv_filtrlari_hamda_sahifalash()
    {
        using var host = new McpTestHost();
        SeedStaff(host);
        var tools = host.Tool<TeacherTools>(McpTestHost.Staff("staff"));

        Assert.Equal(new[] { "u3", "u4" }, Ids(McpTestHost.Parse(await tools.StaffList(role: "staff"))));
        Assert.Equal(new[] { "u1" }, Ids(McpTestHost.Parse(await tools.StaffList(role: "superadmin"))));
        Assert.Equal(new[] { "u4" }, Ids(McpTestHost.Parse(await tools.StaffList(search: "vali"))));
        Assert.Equal(new[] { "u3" }, Ids(McpTestHost.Parse(await tools.StaffList(search: "kassir"))));
        Assert.Equal(new[] { "u4" }, Ids(McpTestHost.Parse(await tools.StaffList(search: "7654321"))));

        var paged = McpTestHost.Parse(await tools.StaffList(page: 2, pageSize: 3));
        Assert.Equal(4, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "u4" }, Ids(paged));

        await Assert.ThrowsAsync<McpException>(() => tools.StaffList(role: "teacher"));
    }

    [Fact]
    public async Task Xodimlar_bolimi_ruxsatisiz_RAD_etiladi()
    {
        using var host = new McpTestHost();
        SeedStaff(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(
            () => host.Tool<TeacherTools>(McpTestHost.Staff("teachers.list")).StaffList());
        Assert.Empty(host.Sql);
    }

    // ================================================================== substitutions_list

    private static void SeedSubs(McpTestHost host)
    {
        var db = host.Rw;
        db.Teachers.Add(new Teacher { Id = "t5", FullName = "Zarina Yordamchi" });
        db.SubstituteTeacherAssignments.AddRange(
            new SubstituteTeacherAssignment
            {
                Id = "a1", GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t2", Date = "2026-09-14",
                EndDate = "2026-09-16", SelectedDates = ["2026-09-14", "2026-09-16"], Reason = "Kasal", CreatedBy = "Admin Aka",
            },
            new SubstituteTeacherAssignment
            {
                Id = "a2", GroupId = "g3", OriginalTeacherId = "t2", SubstituteTeacherId = "t1", Date = "2026-09-21",
                SelectedDates = ["2026-09-21"], Reason = "Ta'til",
            },
            new SubstituteTeacherAssignment
            {
                Id = "a3", GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t2", Date = "2026-09-18",
                SelectedDates = ["2026-09-18"], IsActive = false,
            },
            // eski (oraliqli) yozuv — SelectedDates bo'sh: guruh dars kunlariga yoyiladi
            new SubstituteTeacherAssignment
            {
                Id = "a4", GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t2", Date = "2026-08-03",
                EndDate = "2026-08-07", SelectedDates = [],
            },
            // oraliq KENG, lekin tanlangan sanalar 10..25-sentabrga tushmaydi
            new SubstituteTeacherAssignment
            {
                Id = "a5", GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t5", Date = "2026-09-02",
                EndDate = "2026-09-30", SelectedDates = ["2026-09-02", "2026-09-30"],
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Orinbosarlar_oraliqda_faqat_tanlangan_sanalari_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);
        SeedSubs(host);

        var raw = await host.Tool<TeacherTools>(McpTestHost.SuperAdmin()).SubstitutionsList("2026-09-10", "2026-09-25");
        var json = McpTestHost.Parse(raw);

        // a3 — bekor qilingan, a4 — avgustda, a5 — tanlangan sanalari oraliqqa tushmaydi.
        Assert.Equal(new[] { "a1", "a2" }, Ids(json));
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var a1 = json.GetProperty("items")[0];
        Assert.Equal("English A1", a1.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Dilnoza Karimova", a1.GetProperty("originalTeacher").GetProperty("name").GetString());
        Assert.Equal("Rustam Saidov", a1.GetProperty("substituteTeacher").GetProperty("name").GetString());
        Assert.Equal(new[] { "2026-09-14", "2026-09-16" }, a1.GetProperty("dates").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(2, a1.GetProperty("lessonCount").GetInt32());
        Assert.Equal("Kasal", a1.GetProperty("reason").GetString());
        Assert.True(a1.GetProperty("isActive").GetBoolean());

        host.AssertReadsOnlyDeclared(typeof(TeacherTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Orinbosarlar_filtrlari_holat_va_eski_oraliqli_yozuv()
    {
        using var host = new McpTestHost();
        Seed(host);
        SeedSubs(host);
        var tools = host.Tool<TeacherTools>(McpTestHost.Staff("teachers.substitutions"));

        Assert.Equal(new[] { "a1", "a3", "a2" }, Ids(McpTestHost.Parse(await tools.SubstitutionsList("2026-09-10", "2026-09-25", state: "all"))));
        Assert.Equal(new[] { "a3" }, Ids(McpTestHost.Parse(await tools.SubstitutionsList("2026-09-10", "2026-09-25", state: "cancelled"))));
        Assert.Equal(new[] { "a2" }, Ids(McpTestHost.Parse(await tools.SubstitutionsList("2026-09-10", "2026-09-25", groupId: "g3"))));
        // o'qituvchi — asosiy YOKI o'rinbosar
        Assert.Equal(new[] { "a5" }, Ids(McpTestHost.Parse(await tools.SubstitutionsList("2026-09-01", "2026-09-30", teacherId: "t5"))));
        Assert.Equal(new[] { "a5", "a1", "a2" }, Ids(McpTestHost.Parse(await tools.SubstitutionsList("2026-09-01", "2026-09-30", teacherId: "t1"))));

        // Eski oraliqli yozuv: guruh kunlariga (Du, Ch, Ju) yoyiladi — 3, 5, 7-avgust.
        var aug = McpTestHost.Parse(await tools.SubstitutionsList("2026-08-01", "2026-08-31"));
        Assert.Equal(new[] { "a4" }, Ids(aug));
        var a4 = aug.GetProperty("items")[0];
        Assert.Equal(3, a4.GetProperty("lessonCount").GetInt32());
        Assert.Equal(new[] { "2026-08-03", "2026-08-05", "2026-08-07" },
            a4.GetProperty("datesInRange").EnumerateArray().Select(d => d.GetString()));

        var paged = McpTestHost.Parse(await tools.SubstitutionsList("2026-09-10", "2026-09-25", page: 2, pageSize: 1));
        Assert.Equal(2, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "a2" }, Ids(paged));

        var dflt = McpTestHost.Parse(await tools.SubstitutionsList());
        Assert.Equal(D(-30), dflt.GetProperty("from").GetString());
        Assert.Equal(D(30), dflt.GetProperty("to").GetString());

        await Assert.ThrowsAsync<McpException>(() => tools.SubstitutionsList("2025-01-01", "2026-09-01"));
        await Assert.ThrowsAsync<McpException>(() => tools.SubstitutionsList(state: "yoq"));
    }

    [Fact]
    public async Task Orinbosarlar_ruxsatisiz_RAD_etiladi()
    {
        using var host = new McpTestHost();
        Seed(host);
        SeedSubs(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(
            () => host.Tool<TeacherTools>(McpTestHost.Staff("teachers.list")).SubstitutionsList());
        Assert.Empty(host.Sql);
    }
}
