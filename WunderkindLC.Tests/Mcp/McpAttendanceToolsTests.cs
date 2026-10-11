using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `attendance_daily`, `attendance_not_taken`, `student_attendance`, `absence_reasons` —
/// panel bilan bir xil raqamlar, filtrlar, sahifalash, ruxsat va maxfiylik.
/// </summary>
public class McpAttendanceToolsTests
{
    private const string Day = "2026-09-14";   // dushanba

    private static List<string?> Ids(JsonElement json, string prop) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    private static void Reasons(AppDbContext db) =>
        db.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "rS", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "rK", Name = "Kasal", Short = "K" },
            new AbsenceReason { Id = "rL", Name = "Kech keldi", Short = "Kech", IsLate = true });

    // ================================================================== attendance_daily

    private static void SeedDaily(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" }, new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Room { Id = "r1", Name = "101", IsActive = true },
            new Group
            {
                Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", StartTime = "14:00", EndTime = "15:30",
                RoomId = "r1", Room = "eski yorliq", Days = [0, 2, 4], StartDate = "2026-09-01",
            },
            new Group
            {
                Id = "g2", Name = "Math B", CourseId = "c2", TeacherId = "t2", StartTime = "09:00", EndTime = "10:30",
                Room = "202", Days = [0], StartDate = "2026-09-01",
            });
        Reasons(db);
        db.Students.AddRange(
            new Student
            {
                Id = "s1", FullName = "Ali Valiyev", Phone = "+998901112233", ParentFullName = "Vali Valiyev",
                ParentPhone = "+998935556677", BirthCertificateUrl = "/uploads/maxfiy-surat.jpg",
                ParentPassportUrl = "/uploads/maxfiy-passport.pdf",
            },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Muzlagan" },
            new Student { Id = "s5", FullName = "Eldor Arxivda", IsArchived = true },
            new Student { Id = "s6", FullName = "Farrux Keyinroq" },
            new Student { Id = "s7", FullName = "Gulnoza Chiqqan" },
            new Student { Id = "s8", FullName = "Hamid Karimov", MotherPhone = "+998971234567" });
        StudentGroup M(string sid, string gid, string status = "active", string from = "2026-09-01") =>
            new() { StudentId = sid, GroupId = gid, Status = status, JoinedAt = from, ActivatedAt = from, RecordedAt = from };
        var frozen = M("s4", "g1", "frozen");
        frozen.FrozenAt = "2026-09-10";
        var left = M("s7", "g1");
        left.IsActive = false;
        left.LeftAt = "2026-09-12";
        db.StudentGroups.AddRange(
            M("s1", "g1"), M("s2", "g1"), M("s3", "g1"), frozen, M("s5", "g1"), M("s6", "g1", from: "2026-09-20"), left,
            M("s3", "g2"), M("s8", "g2"));
        db.LessonNotes.AddRange(
            new LessonNote { ClassId = "g1", SubjectId = "c1", Date = Day, Period = 1, Conducted = true, AttendanceTaken = true },
            new LessonNote { ClassId = "g2", SubjectId = "c2", Date = Day, Period = 1, Conducted = true, AttendanceTaken = true },
            new LessonNote { ClassId = "g1", SubjectId = "c1", Date = "2026-09-15", Period = 1, Conducted = true, AttendanceTaken = true });
        db.JournalEntries.AddRange(
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s1", Date = Day, Period = 1, ReasonId = "rS" },
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s2", Date = Day, Period = 1, ReasonId = "rL" },
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s3", Date = Day, Period = 1, Present = true },
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s5", Date = Day, Period = 1, ReasonId = "rK" },
            new JournalEntry { ClassId = "g2", SubjectId = "c2", StudentId = "s8", Date = Day, Period = 1, ReasonId = "rK" },
            new JournalEntry { ClassId = "g1", SubjectId = "c1", StudentId = "s1", Date = "2026-09-15", Period = 1, ReasonId = "rS" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Kunlik_davomat_panel_bilan_bir_xil_jamlanma_va_royxat()
    {
        using var host = new McpTestHost();
        SeedDaily(host);

        var raw = await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).AttendanceDaily(Day);
        var json = McpTestHost.Parse(raw);
        var sum = json.GetProperty("summary");

        Assert.Equal(Day, json.GetProperty("date").GetString());
        Assert.Equal(2, sum.GetProperty("conductedGroups").GetInt32());
        // Kutilganlar: s1, s2, s3 (ikki guruhda — bir marta), s8. Muzlatilgan (s4), arxivdagi (s5),
        // keyin qo'shilgan (s6) va chiqib ketgan (s7) sanalmaydi.
        Assert.Equal(4, sum.GetProperty("markedStudents").GetInt32());
        Assert.Equal(2, sum.GetProperty("absent").GetInt32());
        Assert.Equal(2, sum.GetProperty("absentStudents").GetInt32());
        Assert.Equal(1, sum.GetProperty("late").GetInt32());
        Assert.Equal(2, sum.GetProperty("present").GetInt32());     // kech kelgan KELGAN
        Assert.Equal(1, sum.GetProperty("unexcused").GetInt32());

        // Avval kelmaganlar (vaqt bo'yicha), keyin kechikkanlar; arxivdagi s5 ro'yxatda yo'q.
        Assert.Equal(new[] { "s8", "s1", "s2" }, Ids(json, "studentId"));
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items");
        var hamid = items[0];
        Assert.Equal("Math B", hamid.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Matematika", hamid.GetProperty("group").GetProperty("course").GetString());
        Assert.Equal("Rustam Saidov", hamid.GetProperty("group").GetProperty("teacher").GetString());
        Assert.Equal("202", hamid.GetProperty("lesson").GetProperty("room").GetString());
        Assert.Equal("+998971234567", hamid.GetProperty("parent").GetProperty("motherPhone").GetString());
        var ali = items[1];
        Assert.Equal("Sababsiz", ali.GetProperty("reason").GetProperty("name").GetString());
        Assert.False(ali.GetProperty("reason").GetProperty("isLate").GetBoolean());
        Assert.Equal("+998901112233", ali.GetProperty("phone").GetString());
        Assert.Equal("+998935556677", ali.GetProperty("parent").GetProperty("phone").GetString());
        Assert.Equal("101", ali.GetProperty("lesson").GetProperty("room").GetString());   // xona RoomId bo'yicha
        Assert.Equal("14:00", ali.GetProperty("lesson").GetProperty("startTime").GetString());
        Assert.True(items[2].GetProperty("reason").GetProperty("isLate").GetBoolean());
        Assert.DoesNotContain("/uploads/", raw);

        host.AssertReadsOnlyDeclared(typeof(AttendanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Kunlik_davomat_guruh_filtri_jamlanmani_ham_toraytiradi()
    {
        using var host = new McpTestHost();
        SeedDaily(host);

        var json = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).AttendanceDaily(Day, "g1"));
        var sum = json.GetProperty("summary");

        Assert.Equal("group", json.GetProperty("scope").GetString());
        Assert.Equal(1, sum.GetProperty("conductedGroups").GetInt32());
        Assert.Equal(3, sum.GetProperty("markedStudents").GetInt32());
        Assert.Equal(1, sum.GetProperty("absent").GetInt32());
        Assert.Equal(1, sum.GetProperty("late").GetInt32());
        Assert.Equal(2, sum.GetProperty("present").GetInt32());
        Assert.Equal(new[] { "s1", "s2" }, Ids(json, "studentId"));
    }

    [Fact]
    public async Task Kunlik_davomat_faqat_kelmaganlar_va_sahifalash()
    {
        using var host = new McpTestHost();
        SeedDaily(host);

        var absentOnly = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .AttendanceDaily(Day, onlyAbsent: true));
        Assert.Equal(new[] { "s8", "s1" }, Ids(absentOnly, "studentId"));
        Assert.Equal(1, absentOnly.GetProperty("summary").GetProperty("late").GetInt32());   // jamlanma o'zgarmaydi

        var page2 = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .AttendanceDaily(Day, page: 2, pageSize: 1));
        Assert.Equal(new[] { "s1" }, Ids(page2, "studentId"));
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Equal(1, host.State.RowCount);

        // Boshqa kun: faqat g1 o'tilgan, s1 kelmagan.
        var next = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.Staff("students.attendance"))
            .AttendanceDaily("2026-09-15"));
        Assert.Equal(1, next.GetProperty("summary").GetProperty("conductedGroups").GetInt32());
        Assert.Equal(3, next.GetProperty("summary").GetProperty("markedStudents").GetInt32());
        Assert.Equal(2, next.GetProperty("summary").GetProperty("present").GetInt32());
        Assert.Equal(new[] { "s1" }, Ids(next, "studentId"));
    }

    [Fact]
    public async Task Kunlik_davomat_notogri_sana_tushunarli_xato()
    {
        using var host = new McpTestHost();
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).AttendanceDaily("14.09.2026"));
        Assert.Contains("date", ex.Message);
    }

    // ================================================================== attendance_not_taken

    private static void SeedLessons(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Teacher { Id = "t3", FullName = "Sardor Robotov" },
            new Room { Id = "r1", Name = "101", IsActive = true });
        Group G(string id, string name, string teacher, List<int> days, string start, string end, string from = "2026-09-01") =>
            new() { Id = id, Name = name, CourseId = "c1", TeacherId = teacher, Days = days, StartTime = start, EndTime = end, StartDate = from };
        var g1 = G("g1", "English A1", "t1", [0, 2, 4], "14:00", "15:30");
        g1.RoomId = "r1";
        var g2 = G("g2", "Math B", "t1", [0], "16:00", "17:30");
        g2.Room = "202";
        var old = G("g-old", "Yopilgan", "t1", [0], "08:00", "09:00");
        old.IsArchived = true;
        db.Classes.AddRange(
            g1, g2,
            G("g3", "English B1", "t1", [0], "10:00", "11:30"),
            G("g4", "Robototexnika", "t3", [1], "15:00", "16:00"),                    // seshanba — 14-ga ko'chirilgan
            G("g5", "Kech boshlangan", "t3", [0], "12:00", "13:00", "2026-10-01"),    // 14-sentabrda hali boshlanmagan
            old);
        db.LessonReschedules.Add(new LessonReschedule { ClassId = "g4", FromDate = "2026-09-15", ToDate = Day, Time = "18:00" });
        db.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = "g3", OriginalTeacherId = "t1", SubstituteTeacherId = "t2", Date = Day, SelectedDates = [Day],
            IsActive = true, Reason = "Kasal",
        });
        db.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "c1", Date = Day, Period = 1, Conducted = true, AttendanceTaken = true });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Bobur" }, new Student { Id = "s3", FullName = "Charos" });
        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g2", Status = "active", JoinedAt = "2026-09-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g2", Status = "trial", JoinedAt = "2026-09-01" },
            new StudentGroup { StudentId = "s3", GroupId = "g2", Status = "active", IsActive = false, JoinedAt = "2026-01-01", LeftAt = "2026-06-01" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Davomat_olinmagan_darslar_kochirish_orinbosar_va_kechikish_bilan()
    {
        using var host = new McpTestHost();
        SeedLessons(host);

        var json = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.Staff("classes.notAttended"))
            .AttendanceNotTaken(Day));
        var sum = json.GetProperty("summary");

        // Darslar: g1 (davomat olingan), g2, g3, g4 (ko'chirilgan). Arxiv va hali boshlanmagan guruh yo'q.
        Assert.Equal(4, sum.GetProperty("lessons").GetInt32());
        Assert.Equal(1, sum.GetProperty("attendanceDone").GetInt32());
        Assert.Equal(3, sum.GetProperty("notTaken").GetInt32());
        Assert.Equal(3, sum.GetProperty("overdue").GetInt32());
        Assert.Equal(new[] { "g3", "g2", "g4" }, Ids(json, "groupId"));

        var items = json.GetProperty("items");
        var g3 = items[0];
        Assert.Equal("t2", g3.GetProperty("teacher").GetProperty("id").GetString());
        Assert.Equal("Rustam Saidov", g3.GetProperty("teacher").GetProperty("name").GetString());
        Assert.True(g3.GetProperty("substitute").GetBoolean());
        Assert.Equal("Dilnoza Karimova", g3.GetProperty("groupTeacher").GetProperty("name").GetString());
        Assert.Equal("ended", g3.GetProperty("phase").GetString());
        Assert.True(g3.GetProperty("overdue").GetBoolean());
        Assert.True(g3.GetProperty("minutesOverdue").GetInt32() > 0);

        var g2 = items[1];
        Assert.Equal("202", g2.GetProperty("room").GetString());
        Assert.Equal(2, g2.GetProperty("studentsCount").GetInt32());   // chiqib ketgan a'zo sanalmaydi
        Assert.False(g2.GetProperty("substitute").GetBoolean());

        var g4 = items[2];
        Assert.True(g4.GetProperty("moved").GetBoolean());
        Assert.Equal("18:00", g4.GetProperty("startTime").GetString());
        Assert.Equal("19:00", g4.GetProperty("endTime").GetString());   // asl davomiylik saqlanadi
        Assert.Equal("Sardor Robotov", g4.GetProperty("teacher").GetProperty("name").GetString());

        host.AssertReadsOnlyDeclared(typeof(AttendanceTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("t2", null, new[] { "g3" }, 1)]                 // o'sha kuni o'rinbosar o'tadi
    [InlineData("t1", null, new[] { "g3", "g2" }, 3)]           // guruh egasi: g1 (olingan), g2, g3
    [InlineData(null, "g4", new[] { "g4" }, 1)]
    public async Task Davomat_olinmagan_oqituvchi_va_guruh_filtrlari(
        string? teacherId, string? groupId, string[] expected, int lessons)
    {
        using var host = new McpTestHost();
        SeedLessons(host);

        var json = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .AttendanceNotTaken(Day, teacherId: teacherId, groupId: groupId));

        Assert.Equal(expected, Ids(json, "groupId"));
        Assert.Equal(lessons, json.GetProperty("summary").GetProperty("lessons").GetInt32());
    }

    [Fact]
    public async Task Davomat_olinmagan_kelgusi_kun_va_faqat_kechikkanlar_va_sahifalash()
    {
        using var host = new McpTestHost();
        SeedLessons(host);

        // Kelgusi dushanba: hamma dars hali boshlanmagan — kechikkan yo'q, o'rinbosar yo'q, g5 endi boshlangan.
        var future = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).AttendanceNotTaken("2030-01-07"));
        Assert.Equal(4, future.GetProperty("summary").GetProperty("notTaken").GetInt32());
        Assert.Equal(0, future.GetProperty("summary").GetProperty("overdue").GetInt32());
        Assert.Equal(new[] { "g3", "g5", "g1", "g2" }, Ids(future, "groupId"));
        Assert.All(future.GetProperty("items").EnumerateArray(), i =>
        {
            Assert.Equal("upcoming", i.GetProperty("phase").GetString());
            Assert.Equal(JsonValueKind.Null, i.GetProperty("minutesOverdue").ValueKind);
            Assert.False(i.GetProperty("substitute").GetBoolean());
        });

        var overdueOnly = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .AttendanceNotTaken("2030-01-07", onlyOverdue: true));
        Assert.Equal(0, overdueOnly.GetProperty("total").GetInt32());

        var page2 = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .AttendanceNotTaken(Day, page: 2, pageSize: 2));
        Assert.Equal(new[] { "g4" }, Ids(page2, "groupId"));
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
    }

    // ================================================================== student_attendance

    private static void SeedStudent(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", Days = [0, 2, 4], StartDate = "2026-09-01" },
            new Group { Id = "g2", Name = "Math B", CourseId = "c2", TeacherId = "t1", Days = [1], StartDate = "2026-09-01" });
        Reasons(db);
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev", BirthCertificateUrl = "/uploads/maxfiy-surat.jpg" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" });
        db.StudentGroups.AddRange(
            // Orqaga sanalgan a'zolik: 1-sentabrdan aktiv, lekin tizimga 10-sentabrda kiritilgan.
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01", RecordedAt = "2026-09-10" },
            new StudentGroup { StudentId = "s1", GroupId = "g2", Status = "active", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01", RecordedAt = "2026-09-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01", RecordedAt = "2026-09-01" });
        LessonNote N(string gid, string sub, string date, bool taken = true, bool conducted = true) =>
            new() { ClassId = gid, SubjectId = sub, Date = date, Period = 1, Conducted = conducted, AttendanceTaken = taken };
        db.LessonNotes.AddRange(
            N("g1", "c1", "2026-09-07"), N("g1", "c1", "2026-09-09"), N("g1", "c1", "2026-09-11"),
            N("g1", "c1", "2026-09-14"), N("g1", "c1", "2026-09-16", taken: false), N("g1", "c1", "2026-09-18"),
            N("g1", "c1", "2026-09-21", conducted: false),
            N("g2", "c2", "2026-09-15"));
        JournalEntry E(string sid, string gid, string sub, string date, string? reason = null, int? grade = null) =>
            new() { StudentId = sid, ClassId = gid, SubjectId = sub, Date = date, Period = 1, ReasonId = reason, Grade = grade };
        db.JournalEntries.AddRange(
            E("s1", "g1", "c1", "2026-09-09", "rS"),
            E("s1", "g1", "c1", "2026-09-14", "rL"),
            E("s1", "g1", "c1", "2026-09-18", grade: 5),
            E("s1", "g2", "c2", "2026-09-15", "rK"),
            E("s2", "g1", "c1", "2026-09-11", "rS"));   // boshqa o'quvchi — aralashmasin
        db.SaveChanges();
    }

    [Fact]
    public async Task Oquvchi_davomati_har_dars_va_jami_oquvchi_ilovasi_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        SeedStudent(host);

        var raw = await host.Tool<AttendanceTools>(McpTestHost.Staff("students.attendance"))
            .StudentAttendance("s1", "2026-09-01", "2026-09-30");
        var json = McpTestHost.Parse(raw);
        var totals = json.GetProperty("totals");

        Assert.Equal("Ali Valiyev", json.GetProperty("student").GetProperty("fullName").GetString());
        Assert.Equal(6, totals.GetProperty("conducted").GetInt32());   // 7 dars, 7-sentabr (kiritilishidan oldin) — jamiga kirmaydi
        Assert.Equal(2, totals.GetProperty("attended").GetInt32());    // 11 (standart keldi) + 18 (baho)
        Assert.Equal(2, totals.GetProperty("absent").GetInt32());
        Assert.Equal(1, totals.GetProperty("late").GetInt32());
        Assert.Equal(33, totals.GetProperty("percent").GetInt32());
        Assert.Equal(7, totals.GetProperty("lessonsListed").GetInt32());
        Assert.Equal(1, totals.GetProperty("excludedFromTotals").GetInt32());

        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "2026-09-18", "2026-09-16", "2026-09-15", "2026-09-14", "2026-09-11", "2026-09-09", "2026-09-07" },
            items.Select(i => i.GetProperty("date").GetString()));
        Assert.Equal(
            new[] { "present", "unmarked", "absent", "late", "present", "absent", "unmarked" },
            items.Select(i => i.GetProperty("status").GetString()));
        Assert.Equal(5, items[0].GetProperty("grade").GetInt32());
        Assert.Equal("Math B", items[2].GetProperty("group").GetProperty("name").GetString());
        Assert.Equal("Kasal", items[2].GetProperty("reason").GetProperty("name").GetString());
        Assert.True(items[3].GetProperty("reason").GetProperty("isLate").GetBoolean());
        Assert.False(items[1].GetProperty("beforeRecorded").GetBoolean());
        Assert.True(items[6].GetProperty("beforeRecorded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("reason").ValueKind);

        var byCourse = json.GetProperty("byCourse").EnumerateArray().ToList();
        Assert.Equal(5, byCourse.Single(c => c.GetProperty("course").GetString() == "Ingliz tili").GetProperty("conducted").GetInt32());
        Assert.Equal(2, json.GetProperty("groups").GetArrayLength());
        Assert.DoesNotContain("/uploads/", raw);

        host.AssertReadsOnlyDeclared(typeof(AttendanceTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Oquvchi_davomati_guruh_va_sana_filtrlari_hamda_sahifalash()
    {
        using var host = new McpTestHost();
        SeedStudent(host);

        var g1 = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.Staff("students.list"))
            .StudentAttendance("s1", "2026-09-01", "2026-09-30", "g1"));
        Assert.Equal(5, g1.GetProperty("totals").GetProperty("conducted").GetInt32());
        Assert.Equal(1, g1.GetProperty("totals").GetProperty("absent").GetInt32());
        Assert.Equal(40, g1.GetProperty("totals").GetProperty("percent").GetInt32());
        Assert.Equal(6, g1.GetProperty("total").GetInt32());

        var week = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .StudentAttendance("s1", "2026-09-10", "2026-09-14"));
        Assert.Equal(2, week.GetProperty("totals").GetProperty("conducted").GetInt32());
        Assert.Equal(1, week.GetProperty("totals").GetProperty("late").GetInt32());
        Assert.Equal(50, week.GetProperty("totals").GetProperty("percent").GetInt32());

        var page2 = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin())
            .StudentAttendance("s1", "2026-09-01", "2026-09-30", page: 2, pageSize: 2));
        Assert.Equal(new[] { "2026-09-15", "2026-09-14" }, Ids(page2, "date"));
        Assert.Equal(2, host.State.RowCount);

        // Sukut: oxirgi 30 kun.
        var dflt = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).StudentAttendance("s1"));
        Assert.Equal(McpToolContext.Iso(McpToolContext.Today.AddDays(-29)), dflt.GetProperty("from").GetString());
        Assert.Equal(McpToolContext.Iso(McpToolContext.Today), dflt.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Oquvchi_davomati_topilmasa_xabar_va_notogri_oraliq_xato()
    {
        using var host = new McpTestHost();
        SeedStudent(host);

        var missing = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).StudentAttendance("yoq"));
        Assert.Equal("O'quvchi topilmadi.", missing.GetProperty("message").GetString());

        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).StudentAttendance("s1", "2026-01-01", "2027-06-01"));
        await Assert.ThrowsAsync<McpException>(() =>
            host.Tool<AttendanceTools>(McpTestHost.SuperAdmin()).StudentAttendance("s1", "2026-09-30", "2026-09-01"));
    }

    // ================================================================== absence_reasons

    [Fact]
    public async Task Sabablar_katalogi_avval_kelmaslik_keyin_kech()
    {
        using var host = new McpTestHost();
        Reasons(host.Rw);
        host.Rw.SaveChanges();

        var json = McpTestHost.Parse(await host.Tool<AttendanceTools>(McpTestHost.Staff("students.attendance")).AbsenceReasons());

        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "rK", "rS", "rL" }, Ids(json, "id"));
        var late = json.GetProperty("items")[2];
        Assert.Equal("Kech", late.GetProperty("short").GetString());
        Assert.True(late.GetProperty("isLate").GetBoolean());
        host.AssertReadsOnlyDeclared(typeof(AttendanceTools));
    }

    // ================================================================== ruxsat

    [Theory]
    [InlineData("classes.list")]
    [InlineData("students.list")]
    [InlineData("aiAccess")]
    public async Task Davomat_bolimi_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        SeedDaily(host);
        var user = McpTestHost.Staff(perm);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<AttendanceTools>(user).AttendanceDaily(Day));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<AttendanceTools>(user).AbsenceReasons());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => host.Tool<AttendanceTools>(user).AttendanceNotTaken(Day));
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);   // rad etilganda bazaga umuman so'rov ketmaydi
    }

    [Fact]
    public async Task Davomat_olinmagan_monitori_faqat_oz_ruxsati_bilan()
    {
        using var host = new McpTestHost();
        SeedLessons(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<AttendanceTools>(McpTestHost.Staff("students.attendance")).AttendanceNotTaken(Day));
        Assert.Empty(host.Sql);
    }

    [Theory]
    [InlineData("classes.list")]
    [InlineData("classes.notAttended")]
    [InlineData("students.turnstile")]
    public async Task Oquvchi_davomati_ruxsatisiz_RAD_etiladi(string perm)
    {
        using var host = new McpTestHost();
        SeedStudent(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<AttendanceTools>(McpTestHost.Staff(perm)).StudentAttendance("s1"));
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }
}
