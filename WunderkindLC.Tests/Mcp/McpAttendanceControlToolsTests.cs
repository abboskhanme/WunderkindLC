using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — CRM upgrade Phase 3 (U3m): `attendance_red_list`, `attendance_discipline`, `teacher_attendance_fines`.
/// Panel bilan bir xil ta'rif (AbsenceStreakLoader, AttendanceDiscipline.Decide, AttendanceFineQueries), ruxsat
/// (pul — faqat moliya ruxsati bilan) va bazaga faqat e'lon qilingan jadvallar.
/// </summary>
public class McpAttendanceControlToolsTests
{
    private static string D(int offset) => McpToolContext.Iso(McpToolContext.Today.AddDays(offset));

    private static List<string?> Ids(JsonElement json, string prop) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    // ================================================================== attendance_red_list

    private static void SeedStreaks(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", StartDate = D(-30), Days = [0, 2, 4] },
            new Group { Id = "g2", Name = "English B1", CourseId = "c1", TeacherId = "t2", StartDate = D(-30), Days = [1, 3] });
        db.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "rS", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "rK", Name = "Kasal", Short = "K", IsExcused = true },
            new AbsenceReason { Id = "rL", Name = "Kech", Short = "Kech", IsLate = true });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Arxivda", IsArchived = true },
            new Student { Id = "s4", FullName = "Davron Kechikkan" },
            new Student { Id = "s5", FullName = "Eldor Uzoq" });
        StudentGroup M(string sid, string gid) =>
            new() { StudentId = sid, GroupId = gid, Status = "active", JoinedAt = D(-30), ActivatedAt = D(-30), RecordedAt = D(-30) };
        db.StudentGroups.AddRange(M("s1", "g1"), M("s2", "g1"), M("s3", "g1"), M("s4", "g1"), M("s5", "g2"));

        var dates = new[] { D(-10), D(-8), D(-6), D(-4) };
        foreach (var d in dates)
            db.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "c1", Date = d, Period = 1, Conducted = true, AttendanceTaken = true, AttendanceTakenAt = DateTime.Parse(d + "T14:05:00") });
        // g2 — eski seriya (20 kundan oldin): "yangi" emas.
        foreach (var d in new[] { D(-25), D(-23), D(-21) })
            db.LessonNotes.Add(new LessonNote { ClassId = "g2", SubjectId = "c1", Date = d, Period = 1, Conducted = true, AttendanceTaken = true, AttendanceTakenAt = DateTime.Parse(d + "T14:05:00") });

        JournalEntry E(string sid, string gid, string date, string reason) =>
            new() { StudentId = sid, ClassId = gid, SubjectId = "c1", Date = date, Period = 1, ReasonId = reason };
        db.JournalEntries.AddRange(
            // s1: keldi, keyin 3 ta sababsiz → seriya 3.
            E("s1", "g1", dates[1], "rS"), E("s1", "g1", dates[2], "rS"), E("s1", "g1", dates[3], "rS"),
            // s2: S, K(sababli — to'xtatadi), S, S → seriya 2.
            E("s2", "g1", dates[0], "rS"), E("s2", "g1", dates[1], "rK"), E("s2", "g1", dates[2], "rS"), E("s2", "g1", dates[3], "rS"),
            // s3 arxivda — hech qachon.
            E("s3", "g1", dates[1], "rS"), E("s3", "g1", dates[2], "rS"), E("s3", "g1", dates[3], "rS"),
            // s4: S, S, Kech (KELGAN — to'xtatadi) → 0.
            E("s4", "g1", dates[1], "rS"), E("s4", "g1", dates[2], "rS"), E("s4", "g1", dates[3], "rL"),
            // s5: eski 3 lik seriya.
            E("s5", "g2", D(-25), "rS"), E("s5", "g2", D(-23), "rS"), E("s5", "g2", D(-21), "rS"));
        db.ContactRequests.Add(new ContactRequest { Id = "cr1", StudentId = "s1", StudentName = "Ali Valiyev", Status = ContactStatuses.New, CreatedAt = D(-1) + "T10:00:00" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Qizil_royxat_panel_taarifi_bilan()
    {
        using var host = new McpTestHost();
        SeedStreaks(host);

        var raw = await host.Tool<AttendanceControlTools>(McpTestHost.Staff("students.attendance")).AttendanceRedList();
        var json = McpTestHost.Parse(raw);

        // s1 (3, yangi) va s5 (3, eski). s2 — 2 (sababli yo'qlik to'xtatdi), s4 — kech keldi, s3 — arxivda.
        Assert.Equal(new[] { "s1", "s5" }, Ids(json, "studentId"));
        var ali = json.GetProperty("items")[0];
        Assert.Equal(3, ali.GetProperty("streak").GetInt32());
        Assert.Equal(3, ali.GetProperty("level").GetInt32());
        Assert.Equal(new[] { D(-8), D(-6), D(-4) }, ali.GetProperty("dates").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(D(-4), ali.GetProperty("lastDate").GetString());
        Assert.True(ali.GetProperty("fresh").GetBoolean());
        Assert.Equal("Dilnoza Karimova", ali.GetProperty("teacher").GetProperty("name").GetString());
        Assert.Equal("cr1", ali.GetProperty("contact").GetProperty("requestId").GetString());
        Assert.Equal("new", ali.GetProperty("contact").GetProperty("status").GetString());
        var eldor = json.GetProperty("items")[1];
        Assert.False(eldor.GetProperty("fresh").GetBoolean());
        Assert.Equal(JsonValueKind.Null, eldor.GetProperty("contact").ValueKind);
        Assert.Equal(2, json.GetProperty("summary").GetProperty("students").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(AttendanceControlTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Qizil_royxat_filtrlari_va_ruxsat()
    {
        using var host = new McpTestHost();
        SeedStreaks(host);
        AttendanceControlTools Tool() => host.Tool<AttendanceControlTools>(McpTestHost.SuperAdmin());

        Assert.Equal(new[] { "s1", "s5", "s2" }, Ids(McpTestHost.Parse(await Tool().AttendanceRedList(min: 2)), "studentId"));
        Assert.Equal(new[] { "s5" }, Ids(McpTestHost.Parse(await Tool().AttendanceRedList(groupId: "g2")), "studentId"));
        Assert.Equal(new[] { "s1" }, Ids(McpTestHost.Parse(await Tool().AttendanceRedList(teacherId: "t1")), "studentId"));
        Assert.Empty(Ids(McpTestHost.Parse(await Tool().AttendanceRedList(min: 5)), "studentId"));

        using var denied = new McpTestHost();
        SeedStreaks(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            denied.Tool<AttendanceControlTools>(McpTestHost.Staff("students.list")).AttendanceRedList());
        Assert.Empty(denied.Sql);
    }

    // ================================================================== attendance_discipline

    private const string Day = "2026-09-14";   // dushanba, o'tgan kun — natija vaqtga bog'liq emas

    private static void SeedDiscipline(McpTestHost host)
    {
        var db = host.Rw;
        db.CenterMeta.Add(new CenterMeta
        {
            AttendanceDisciplineEnabled = true, AttendanceDeadlineMinutes = 15, AttendanceFineEnabled = true, AttendanceFineAmount = 20_000,
        });
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Teacher { Id = "t3", FullName = "Sardor O'rinbosar" });
        Group G(string id, string name, string teacher, string start, string end, string? course = "c1") =>
            new() { Id = id, Name = name, CourseId = course ?? "", TeacherId = teacher, Days = [0], StartTime = start, EndTime = end, StartDate = "2026-09-01" };
        db.Classes.AddRange(
            G("gA", "A — o'z vaqtida", "t1", "14:00", "15:00"),
            G("gB", "B — kech", "t1", "15:00", "16:00"),
            G("gC", "C — olinmagan", "t2", "16:00", "17:00"),
            G("gD", "D — kurssiz", "t1", "17:00", "18:00", course: null),
            G("gE", "E — vaqtsiz", "t1", "", ""),
            G("gF", "F — o'quvchisiz", "t1", "10:00", "11:00"));
        db.Students.AddRange(new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Bobur" }, new Student { Id = "s3", FullName = "Charos" });
        StudentGroup M(string sid, string gid) => new() { StudentId = sid, GroupId = gid, Status = "active", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01" };
        db.StudentGroups.AddRange(M("s1", "gA"), M("s2", "gB"), M("s3", "gC"), M("s1", "gD"), M("s1", "gE"));
        db.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = "gC", OriginalTeacherId = "t2", SubstituteTeacherId = "t3", Date = Day, SelectedDates = [Day], IsActive = true, Reason = "Kasal",
        });
        db.LessonNotes.AddRange(
            new LessonNote { ClassId = "gA", SubjectId = "c1", Date = Day, Period = 1, Conducted = true, AttendanceTakenAt = new DateTime(2026, 9, 14, 14, 10, 0) },
            new LessonNote { ClassId = "gB", SubjectId = "c1", Date = Day, Period = 1, Conducted = true, AttendanceTakenAt = new DateTime(2026, 9, 14, 15, 40, 0) },
            // gC — faqat baho (davomat belgisi yo'q).
            new LessonNote { ClassId = "gC", SubjectId = "c1", Date = Day, Period = 1, Conducted = true });
        db.TeacherAttendanceFines.AddRange(
            new TeacherAttendanceFine { Id = "f1", TeacherId = "t1", GroupId = "gB", LessonDate = Day, Month = "2026-09", LessonStart = "15:00", Deadline = new DateTime(2026, 9, 14, 15, 15, 0), Amount = 20_000, Status = "active", AttendanceTakenAt = new DateTime(2026, 9, 14, 15, 40, 0), CreatedAt = new DateTime(2026, 9, 14, 15, 16, 0) },
            new TeacherAttendanceFine { Id = "f2", TeacherId = "t3", GroupId = "gC", LessonDate = Day, Month = "2026-09", LessonStart = "16:00", Deadline = new DateTime(2026, 9, 14, 16, 15, 0), Amount = 20_000, Status = "waived", WaivedByName = "Nodira Menejer", WaivedAt = new DateTime(2026, 9, 15, 9, 0, 0), WaiveReason = "Bayram", CreatedAt = new DateTime(2026, 9, 14, 16, 16, 0) },
            new TeacherAttendanceFine { Id = "f3", TeacherId = "t1", GroupId = "gA", LessonDate = "2026-08-31", Month = "2026-08", LessonStart = "14:00", Deadline = new DateTime(2026, 8, 31, 14, 15, 0), Amount = 15_000, Status = "active", CreatedAt = new DateTime(2026, 8, 31, 14, 16, 0) });
        db.SaveChanges();
    }

    [Fact]
    public async Task Intizom_oz_vaqtida_kech_va_olinmagan_darslar()
    {
        using var host = new McpTestHost();
        SeedDiscipline(host);

        var raw = await host.Tool<AttendanceControlTools>(McpTestHost.SuperAdmin()).AttendanceDisciplineReport(Day);
        var json = McpTestHost.Parse(raw);

        var sum = json.GetProperty("summary");
        Assert.Equal(3, sum.GetProperty("lessons").GetInt32());   // gD (kurssiz), gE (vaqtsiz), gF (o'quvchisiz) kirmaydi
        Assert.Equal(1, sum.GetProperty("onTime").GetInt32());
        Assert.Equal(1, sum.GetProperty("late").GetInt32());
        Assert.Equal(1, sum.GetProperty("notTaken").GetInt32());

        // Sukut — muammolilar, vaqt bo'yicha.
        Assert.Equal(new[] { "gB", "gC" }, json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("group").GetProperty("id").GetString()));
        var gB = json.GetProperty("items")[0];
        Assert.Equal("late", gB.GetProperty("status").GetString());
        Assert.Equal(25, gB.GetProperty("minutesLate").GetInt32());
        Assert.Equal("2026-09-14T15:15:00", gB.GetProperty("deadline").GetString());
        Assert.Equal("2026-09-14T15:40:00", gB.GetProperty("attendanceTakenAt").GetString());
        Assert.Equal(20_000, gB.GetProperty("fine").GetProperty("amount").GetDecimal());
        var gC = json.GetProperty("items")[1];
        Assert.Equal("not_taken", gC.GetProperty("status").GetString());
        Assert.Equal("t3", gC.GetProperty("teacher").GetProperty("id").GetString());   // o'sha kuni o'rinbosar o'tgan
        Assert.True(gC.GetProperty("substitute").GetBoolean());
        Assert.Equal("waived", gC.GetProperty("fine").GetProperty("status").GetString());

        var byTeacher = json.GetProperty("byTeacher").EnumerateArray().ToDictionary(x => x.GetProperty("teacherId").GetString()!);
        Assert.Equal(2, byTeacher["t1"].GetProperty("lessons").GetInt32());
        Assert.Equal(1, byTeacher["t1"].GetProperty("late").GetInt32());
        Assert.Equal(1, byTeacher["t1"].GetProperty("fines").GetInt32());
        Assert.Equal(1, byTeacher["t3"].GetProperty("notTaken").GetInt32());
        Assert.True(json.GetProperty("settings").GetProperty("disciplineEnabled").GetBoolean());
        Assert.Equal(20_000, json.GetProperty("settings").GetProperty("fineAmount").GetDecimal());

        host.AssertReadsOnlyDeclared(typeof(AttendanceControlTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Intizom_filtrlari_moliyasiz_summasiz_va_ruxsat()
    {
        using var host = new McpTestHost();
        SeedDiscipline(host);

        var raw = await host.Tool<AttendanceControlTools>(McpTestHost.Staff("classes.notAttended"))
            .AttendanceDisciplineReport(Day, status: "all");
        var json = McpTestHost.Parse(raw);
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.All(json.GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("fine").ValueKind));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("settings").GetProperty("fineAmount").ValueKind);
        Assert.DoesNotContain("20000", raw);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("byTeacher")[0].GetProperty("fines").ValueKind);

        AttendanceControlTools Tool() => host.Tool<AttendanceControlTools>(McpTestHost.SuperAdmin());
        var onTime = McpTestHost.Parse(await Tool().AttendanceDisciplineReport(Day, status: "on_time"));
        Assert.Equal(new[] { "gA" }, onTime.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("group").GetProperty("id").GetString()));
        // Guruh egasi bo'yicha ham (gC egasi t2, o'sha kuni o'rinbosar o'tgan).
        var owner = McpTestHost.Parse(await Tool().AttendanceDisciplineReport(Day, teacherId: "t2", status: "all"));
        Assert.Equal(1, owner.GetProperty("total").GetInt32());
        var byGroup = McpTestHost.Parse(await Tool().AttendanceDisciplineReport(Day, groupId: "gB", status: "all"));
        Assert.Equal(1, byGroup.GetProperty("total").GetInt32());
        // Dars yo'q kun (seshanba).
        Assert.Equal(0, McpTestHost.Parse(await Tool().AttendanceDisciplineReport("2026-09-15")).GetProperty("summary").GetProperty("lessons").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => Tool().AttendanceDisciplineReport("2026-08-01", "2026-09-14"));
        await Assert.ThrowsAsync<McpException>(() => Tool().AttendanceDisciplineReport(Day, status: "ok"));
        await Assert.ThrowsAsync<McpException>(() => Tool().AttendanceDisciplineReport(Day, "2026-09-01"));

        using var denied = new McpTestHost();
        SeedDiscipline(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            denied.Tool<AttendanceControlTools>(McpTestHost.Staff("students.attendance")).AttendanceDisciplineReport(Day));
        Assert.Empty(denied.Sql);
    }

    [Fact]
    public async Task Intizom_bir_necha_kun_oraligi()
    {
        using var host = new McpTestHost();
        SeedDiscipline(host);

        // 14 va 21-sentabr (ikki dushanba): 21-da hech kim davomat olmagan.
        var json = McpTestHost.Parse(await host.Tool<AttendanceControlTools>(McpTestHost.SuperAdmin())
            .AttendanceDisciplineReport("2026-09-14", "2026-09-21", status: "all"));

        Assert.Equal(6, json.GetProperty("summary").GetProperty("lessons").GetInt32());
        Assert.Equal(4, json.GetProperty("summary").GetProperty("notTaken").GetInt32());
        Assert.Equal("2026-09-21", json.GetProperty("items")[0].GetProperty("date").GetString());   // yangisi tepada
        // 21-da o'rinbosar yo'q — gC ni egasi o'tadi.
        Assert.Contains(json.GetProperty("items").EnumerateArray(), i =>
            i.GetProperty("date").GetString() == "2026-09-21" && i.GetProperty("group").GetProperty("id").GetString() == "gC"
            && i.GetProperty("teacher").GetProperty("id").GetString() == "t2");
    }

    // ================================================================== teacher_attendance_fines

    [Fact]
    public async Task Davomat_jarimalari_oy_boyicha_va_jamlanma()
    {
        using var host = new McpTestHost();
        SeedDiscipline(host);

        var json = McpTestHost.Parse(await host.Tool<AttendanceControlTools>(McpTestHost.Staff("finance.main"))
            .TeacherAttendanceFines("2026-09"));

        Assert.Equal(2, json.GetProperty("total").GetInt32());   // f3 — avgust
        Assert.Equal(20_000, json.GetProperty("summary").GetProperty("totalActive").GetDecimal());
        Assert.Equal(1, json.GetProperty("summary").GetProperty("countActive").GetInt32());
        var f2 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "f2");
        Assert.Equal("waived", f2.GetProperty("status").GetString());
        Assert.Equal("Bayram", f2.GetProperty("waived").GetProperty("reason").GetString());
        Assert.Equal("Sardor O'rinbosar", f2.GetProperty("teacher").GetProperty("name").GetString());
        Assert.Equal("C — olinmagan", f2.GetProperty("group").GetProperty("name").GetString());
        var t1 = json.GetProperty("byTeacher").EnumerateArray().Single(x => x.GetProperty("teacherId").GetString() == "t1");
        Assert.Equal(20_000, t1.GetProperty("activeAmount").GetDecimal());

        host.AssertReadsOnlyDeclared(typeof(AttendanceControlTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Davomat_jarimalari_filtrlari_va_faqat_moliya_ruxsati_bilan()
    {
        using var host = new McpTestHost();
        SeedDiscipline(host);
        AttendanceControlTools Tool() => host.Tool<AttendanceControlTools>(McpTestHost.Admin());

        Assert.Equal(new[] { "f2" }, Ids(McpTestHost.Parse(await Tool().TeacherAttendanceFines("2026-09", status: "waived")), "id"));
        Assert.Equal(new[] { "f1" }, Ids(McpTestHost.Parse(await Tool().TeacherAttendanceFines("2026-09", teacherId: "t1")), "id"));
        Assert.Equal(new[] { "f3" }, Ids(McpTestHost.Parse(await Tool().TeacherAttendanceFines("2026-08")), "id"));
        await Assert.ThrowsAsync<McpException>(() => Tool().TeacherAttendanceFines("2026-09", status: "paid"));
        await Assert.ThrowsAsync<McpException>(() => Tool().TeacherAttendanceFines("2026/09"));

        using var denied = new McpTestHost();
        SeedDiscipline(denied);
        foreach (var perm in new[] { "classes.notAttended", "teachers.list", "kassa" })
            await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
                denied.Tool<AttendanceControlTools>(McpTestHost.Staff(perm)).TeacherAttendanceFines("2026-09"));
        Assert.Empty(denied.Sql);
    }

    // ================================================================== attendance_notices (U3b)

    private const string NoticeDay = "2026-10-05";
    private const long ChatMother = 987_654_321;
    private const long ChatFather = 555_000_111;

    private static DateTime At(string date, string time) => DateTime.Parse(date + "T" + time + ":00");

    /// <summary>
    /// g1 (14:00–15:30, t1): n1 Ali — «kelmadi» rejalashtirilgan; n2 Bobur — «kelmadi» (Kasal) yuborildi, ikki chat
    /// (biri o'qib «tanishdim» bosgan, biri bloklagan); n3 Charos — «keldi» yuborildi. Davomat 14:05 da olingan.
    /// g2 (16:00–17:00, t2): davomat 16:40 da (KECH) — n4 Davron tasdiq kutmoqda; n5 Eldor menejer to'xtatgan.
    /// n6 — boshqa kun.
    /// </summary>
    private static void SeedNotices(McpTestHost host)
    {
        var db = host.Rw;
        db.CenterMeta.Add(new CenterMeta { AbsentNoticeApproval = "late_only", AbsentNoticeDelayMinutes = 5, AttendanceDeadlineMinutes = 15 });
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", Days = [0], StartTime = "14:00", EndTime = "15:30" },
            new Group { Id = "g2", Name = "English B1", CourseId = "c1", TeacherId = "t2", Days = [0], StartTime = "16:00", EndTime = "17:00" });
        db.AbsenceReasons.AddRange(
            new AbsenceReason { Id = "rS", Name = "Sababsiz", Short = "S" },
            new AbsenceReason { Id = "rK", Name = "Kasal", Short = "K", IsExcused = true });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Tosheva" },
            new Student { Id = "s4", FullName = "Davron Karimov" },
            new Student { Id = "s5", FullName = "Eldor Saidov" });
        db.LessonNotes.AddRange(
            new LessonNote { ClassId = "g1", SubjectId = "c1", Date = NoticeDay, Period = 1, Conducted = true, AttendanceTakenAt = At(NoticeDay, "14:05") },
            new LessonNote { ClassId = "g2", SubjectId = "c1", Date = NoticeDay, Period = 1, Conducted = true, AttendanceTakenAt = At(NoticeDay, "16:40") });

        AttendanceNotifyItem N(string id, string kind, string gid, string sid, string status, string? reason, bool late,
            string start, string end, string sendAfter, string date = NoticeDay) => new()
        {
            Id = id, Kind = kind, ClassId = gid, SubjectId = "c1", Date = date, Period = 1, StudentId = sid, Status = status,
            ReasonId = reason, TakenLate = late, LessonStart = start, LessonEnd = end, SendAfter = At(date, sendAfter),
            CreatedAt = At(date, start), UpdatedAt = At(date, start),
        };
        var held = N("n5", "absent", "g2", "s5", "held", "rS", true, "16:00", "17:00", "17:05");
        held.DecidedById = "u-admin";
        held.DecidedByName = "Menejer Nodira";
        held.DecidedAt = At(NoticeDay, "16:50");
        held.DecisionNote = "Ota-onasi oldindan aytgan";
        db.AttendanceNotifyItems.AddRange(
            N("n1", "absent", "g1", "s1", "pending", "rS", false, "14:00", "15:30", "15:35"),
            N("n2", "absent", "g1", "s2", "done", "rK", false, "14:00", "15:30", "15:35"),
            N("n3", "present", "g1", "s3", "done", null, false, "14:00", "15:30", "14:05"),
            N("n4", "absent", "g2", "s4", "awaiting", "rS", true, "16:00", "17:00", "17:05"),
            held,
            N("n6", "absent", "g1", "s1", "expired", "rS", false, "14:00", "15:30", "15:35", date: "2026-10-04"));
        MessageDelivery Del(string id, long chat, string status, DateTime? ack) => new()
        {
            Id = id, Source = "auto", Trigger = "attendance_absent", SourceId = "n2", StudentId = "s2", ChatId = chat,
            Relation = "mother", RecipientName = "Bobur Aliyev", Text = "Farzandingiz darsga kelmadi", Status = status,
            CreatedAt = At(NoticeDay, "15:35"), AckAt = ack, AckRequired = true, DedupeKey = "n2:" + id,
        };
        db.MessageDeliveries.AddRange(
            Del("d1", ChatMother, "sent", At(NoticeDay, "15:40")),
            Del("d2", ChatFather, "blocked", null));
        db.SaveChanges();
    }

    [Fact]
    public async Task Davomat_xabarlari_panel_navbati_bilan_bir_xil_va_chat_id_siz()
    {
        using var host = new McpTestHost();
        SeedNotices(host);

        var raw = await host.Tool<AttendanceControlTools>(McpTestHost.Staff("students.notices")).AttendanceNotices(NoticeDay);
        var json = McpTestHost.Parse(raw);

        // Panelning o'zi (AttendanceNoticesController.List → AbsentNoticeQueries.ListAsync) bilan AYNAN bir xil.
        var panel = await WunderkindLC.Application.Services.AbsentNoticeQueries.ListAsync(host.Rw, NoticeDay, null, null, null);
        Assert.Equal(panel.Lessons.SelectMany(l => l.Items).Select(i => (string?)i.Id), Ids(json, "id"));
        Assert.Equal(new[] { "n1", "n2", "n3", "n4", "n5" }, Ids(json, "id"));   // dars vaqti, «kelmadi» oldin, ism
        var counts = json.GetProperty("counts");
        Assert.Equal(panel.Counts.Pending, counts.GetProperty("pending").GetInt32());
        Assert.Equal(1, counts.GetProperty("pending").GetInt32());
        Assert.Equal(1, counts.GetProperty("awaiting").GetInt32());
        Assert.Equal(1, counts.GetProperty("held").GetInt32());
        Assert.Equal(2, counts.GetProperty("sent").GetInt32());
        Assert.Equal(0, counts.GetProperty("expired").GetInt32());   // n6 — boshqa kun

        var items = json.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);
        Assert.Equal("Kelmadi", items["n1"].GetProperty("kindLabel").GetString());
        Assert.Equal("Rejalashtirilgan", items["n1"].GetProperty("statusLabel").GetString());
        Assert.Equal(NoticeDay + "T15:35:00", items["n1"].GetProperty("sendAfter").GetString());
        Assert.False(items["n1"].GetProperty("reason").GetProperty("isExcused").GetBoolean());
        Assert.Equal("Kasal", items["n2"].GetProperty("reason").GetProperty("name").GetString());
        Assert.True(items["n2"].GetProperty("reason").GetProperty("isExcused").GetBoolean());
        var delivery = items["n2"].GetProperty("delivery");
        Assert.Equal(1, delivery.GetProperty("sent").GetInt32());
        Assert.Equal(1, delivery.GetProperty("blocked").GetInt32());
        Assert.Equal(1, delivery.GetProperty("acked").GetInt32());
        Assert.Equal(JsonValueKind.Null, items["n1"].GetProperty("delivery").ValueKind);
        Assert.Equal("Keldi", items["n3"].GetProperty("kindLabel").GetString());
        Assert.Equal(JsonValueKind.Null, items["n3"].GetProperty("reason").ValueKind);
        Assert.Equal("Tasdiq kutmoqda", items["n4"].GetProperty("statusLabel").GetString());
        Assert.Equal("Menejer Nodira", items["n5"].GetProperty("decision").GetProperty("by").GetString());
        Assert.Equal("Ota-onasi oldindan aytgan", items["n5"].GetProperty("decision").GetProperty("note").GetString());

        // Darslar: davomat birinchi belgisi va kech olinganmi (g2 — 16:40, muddat 16:15).
        var lessons = json.GetProperty("lessons").EnumerateArray().ToList();
        Assert.Equal(new[] { "g1", "g2" }, lessons.Select(l => l.GetProperty("group").GetProperty("id").GetString()));
        Assert.Equal(NoticeDay + "T14:05:00", lessons[0].GetProperty("attendanceTakenAt").GetString());
        Assert.False(lessons[0].GetProperty("takenLate").GetBoolean());
        Assert.True(lessons[1].GetProperty("takenLate").GetBoolean());
        Assert.Equal("Dilnoza Karimova", lessons[0].GetProperty("teacher").GetString());
        Assert.Equal(3, lessons[0].GetProperty("notices").GetInt32());

        var settings = json.GetProperty("settings");
        Assert.Equal("late_only", settings.GetProperty("approval").GetString());
        Assert.Equal(5, settings.GetProperty("delayMinutes").GetInt32());

        // Chat id hech qayerda yo'q — na natijada, na SQL'da.
        Assert.DoesNotContain(ChatMother.ToString(), raw);
        Assert.DoesNotContain(ChatFather.ToString(), raw);
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"ChatId\"", StringComparison.Ordinal));
        host.AssertReadsOnlyDeclared(typeof(AttendanceControlTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Davomat_xabarlari_filtrlari_sahifalash_va_ruxsat()
    {
        using var host = new McpTestHost();
        SeedNotices(host);
        AttendanceControlTools Tool() => host.Tool<AttendanceControlTools>(McpTestHost.Admin());

        // Holat filtri — sanoqlar filtrdan OLDIN (chiplar o'z sonini saqlaydi).
        var awaiting = McpTestHost.Parse(await Tool().AttendanceNotices(NoticeDay, status: "awaiting"));
        Assert.Equal(new[] { "n4" }, Ids(awaiting, "id"));
        Assert.Equal(2, awaiting.GetProperty("counts").GetProperty("sent").GetInt32());
        Assert.Equal(new[] { "n1", "n2", "n3" }, Ids(McpTestHost.Parse(await Tool().AttendanceNotices(NoticeDay, groupId: "g1")), "id"));
        Assert.Equal(new[] { "n4", "n5" }, Ids(McpTestHost.Parse(await Tool().AttendanceNotices(NoticeDay, teacherId: "t2")), "id"));
        Assert.Equal(new[] { "n6" }, Ids(McpTestHost.Parse(await Tool().AttendanceNotices("2026-10-04")), "id"));
        var paged = McpTestHost.Parse(await Tool().AttendanceNotices(NoticeDay, page: 2, pageSize: 2));
        Assert.Equal(5, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "n3", "n4" }, Ids(paged, "id"));
        // Sukut — bugun.
        Assert.Equal(McpToolContext.Iso(McpToolContext.Today),
            McpTestHost.Parse(await Tool().AttendanceNotices()).GetProperty("date").GetString());

        await Assert.ThrowsAsync<McpException>(() => Tool().AttendanceNotices(NoticeDay, status: "sending"));
        await Assert.ThrowsAsync<McpException>(() => Tool().AttendanceNotices("05.10.2026"));

        // Ruxsat — `students.notices` (panel: ReadRequiresPerm); davomat yoki o'quvchilar ruxsati YETMAYDI.
        using var denied = new McpTestHost();
        SeedNotices(denied);
        foreach (var perm in new[] { "students.attendance", "students.list", "classes.notAttended" })
            await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
                denied.Tool<AttendanceControlTools>(McpTestHost.Staff(perm)).AttendanceNotices(NoticeDay));
        Assert.True(denied.State.Denied);
        Assert.Empty(denied.Sql);
    }
}
