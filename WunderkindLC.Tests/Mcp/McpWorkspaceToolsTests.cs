using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — CRM upgrade Phase 5 (U5m): `schedule_week` (sanalar bo'yicha jadval, qavatlar), `schedule_conflicts`,
/// `schedule_day`/`rooms_list` dagi qavat va `tasks_list` dagi tasdiq holati. Panelning o'z funksiyalari
/// (DatedSchedule, RoomConflictService, WorkTaskApproval) bilan bir xil natija.
/// </summary>
public class McpWorkspaceToolsTests
{
    private const string Mon = "2026-09-14";   // dushanba

    private static void SeedSchedule(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Teacher { Id = "t3", FullName = "Sardor O'rinbosar" },
            new Room { Id = "r1", Name = "101", Floor = 1, Capacity = 12, IsActive = true },
            new Room { Id = "r2", Name = "201", Floor = 2, Capacity = 10, IsActive = true },
            new Room { Id = "r3", Name = "Zal", Capacity = 30, IsActive = true });   // qavat ko'rsatilmagan
        Group G(string id, string name, string teacher, string room, List<int> days, string start, string end) =>
            new() { Id = id, Name = name, CourseId = "c1", TeacherId = teacher, RoomId = room, Days = days, StartTime = start, EndTime = end, StartDate = "2026-09-01", Capacity = 12 };
        var archived = G("gX", "Yopilgan", "t1", "r1", [0], "14:30", "15:30");
        archived.IsArchived = true;
        db.Classes.AddRange(
            G("g1", "English A1", "t1", "r1", [0, 2], "14:00", "15:00"),
            G("g2", "English B1", "t2", "r1", [0], "14:30", "15:30"),   // xona to'qnashuvi (g1 bilan, dushanba)
            G("g3", "English C1", "t1", "r2", [2], "14:30", "15:30"),   // o'qituvchi to'qnashuvi (g1 bilan, chorshanba)
            G("g4", "English D1", "t2", "r1", [0], "15:00", "16:00"),   // g1 bilan TEGIB turadi — to'qnashuv emas; g2 bilan esa xona
            G("g5", "Kids", "t3", "r3", [1], "10:00", "11:00"),
            archived);
        db.LessonReschedules.Add(new LessonReschedule { ClassId = "g5", FromDate = "2026-09-15", ToDate = "2026-09-17", Time = "16:00" });
        db.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t3", Date = Mon, SelectedDates = [Mon], IsActive = true, Reason = "Kasal",
        });
        db.Students.AddRange(new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Bobur" });
        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "trial", JoinedAt = "2026-09-01" });
        db.LessonNotes.Add(new LessonNote { ClassId = "g1", SubjectId = "c1", Date = Mon, Period = 1, Conducted = true });
        db.SaveChanges();
    }

    private static List<string?> Groups(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("group").GetProperty("id").GetString()).ToList();

    // ================================================================== schedule_week

    [Fact]
    public async Task Hafta_jadvali_panel_bilan_bir_xil_qavatlar_kochirish_va_orinbosar()
    {
        using var host = new McpTestHost();
        SeedSchedule(host);

        var raw = await host.Tool<ScheduleTools>(McpTestHost.Staff("schedule.timetable")).ScheduleWeek(Mon, "2026-09-20");
        var json = McpTestHost.Parse(raw);

        // Dushanba: g1, g2, g4; chorshanba: g1, g3; payshanba: g5 (seshanbadan ko'chirilgan). Arxivdagi gX yo'q.
        var panel = DatedSchedule.Build(await DatedSchedule.LoadAsync(host.Rw, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20)),
            new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20));
        Assert.Equal(panel.Lessons.Count, json.GetProperty("total").GetInt32());
        Assert.Equal(6, json.GetProperty("total").GetInt32());
        Assert.Equal(panel.Lessons.Select(l => l.GroupId), Groups(json));

        var items = json.GetProperty("items").EnumerateArray().ToList();
        var g1Mon = items.First(i => i.GetProperty("group").GetProperty("id").GetString() == "g1");
        Assert.Equal(Mon, g1Mon.GetProperty("date").GetString());
        Assert.Equal("t3", g1Mon.GetProperty("substitute").GetProperty("id").GetString());
        Assert.Equal(1, g1Mon.GetProperty("room").GetProperty("floor").GetInt32());
        Assert.Equal("1-qavat", g1Mon.GetProperty("room").GetProperty("floorLabel").GetString());
        Assert.Equal(2, g1Mon.GetProperty("members").GetInt32());
        Assert.True(g1Mon.GetProperty("conducted").GetBoolean());
        var g5 = items.Single(i => i.GetProperty("group").GetProperty("id").GetString() == "g5");
        Assert.Equal("2026-09-17", g5.GetProperty("date").GetString());
        Assert.True(g5.GetProperty("moved").GetBoolean());
        Assert.Equal("16:00", g5.GetProperty("startTime").GetString());
        Assert.Equal(JsonValueKind.Null, g5.GetProperty("room").GetProperty("floor").ValueKind);

        // Qavatlar o'sish tartibida, ko'rsatilmagani oxirida.
        Assert.Equal(new[] { "1-qavat", "2-qavat", "Qavat ko'rsatilmagan" },
            json.GetProperty("floors").EnumerateArray().Select(f => f.GetProperty("label").GetString()));

        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Hafta_jadvali_filtrlari_va_chegaralar()
    {
        using var host = new McpTestHost();
        SeedSchedule(host);
        ScheduleTools Tool() => host.Tool<ScheduleTools>(McpTestHost.SuperAdmin());

        Assert.Equal(new[] { "g1", "g2", "g4" }, Groups(McpTestHost.Parse(await Tool().ScheduleWeek(Mon))));   // bitta kun
        Assert.Equal(new[] { "g3" }, Groups(McpTestHost.Parse(await Tool().ScheduleWeek(Mon, "2026-09-20", floor: 2))));
        // O'qituvchi: o'z guruhlari YOKI o'sha kuni o'rinbosarligi.
        Assert.Equal(new[] { "g1", "g5" }, Groups(McpTestHost.Parse(await Tool().ScheduleWeek(Mon, "2026-09-20", teacherId: "t3"))));
        Assert.Equal(new[] { "g2", "g4" }, Groups(McpTestHost.Parse(await Tool().ScheduleWeek(Mon, "2026-09-20", teacherId: "t2"))));
        Assert.Equal(new[] { "g1", "g1" }, Groups(McpTestHost.Parse(await Tool().ScheduleWeek(Mon, "2026-09-20", groupId: "g1"))));
        Assert.Equal(4, McpTestHost.Parse(await Tool().ScheduleWeek(Mon, "2026-09-20", roomId: "r1")).GetProperty("total").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => Tool().ScheduleWeek(Mon, "2026-09-21"));   // 8 kun
        await Assert.ThrowsAsync<McpException>(() => Tool().ScheduleWeek("2026-09-20", Mon));
        await Assert.ThrowsAsync<McpException>(() => Tool().ScheduleWeek("14.09.2026"));

        using var denied = new McpTestHost();
        SeedSchedule(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => denied.Tool<ScheduleTools>(McpTestHost.Staff("classes.list")).ScheduleWeek());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => denied.Tool<ScheduleTools>(McpTestHost.Staff("classes.list")).ScheduleConflictPairs());
        Assert.Empty(denied.Sql);
    }

    [Fact]
    public async Task Kunlik_jadval_va_xonalar_royxatida_qavat()
    {
        using var host = new McpTestHost();
        SeedSchedule(host);

        var day = McpTestHost.Parse(await host.Tool<ScheduleTools>(McpTestHost.SuperAdmin()).ScheduleDay(date: Mon));
        var g2 = day.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("groupId").GetString() == "g2");
        Assert.Equal(1, g2.GetProperty("room").GetProperty("floor").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));

        using var roomsHost = new McpTestHost();   // SQL jurnali vosita bo'yicha alohida tekshiriladi
        SeedSchedule(roomsHost);
        var rooms = McpTestHost.Parse(await roomsHost.Tool<GroupTools>(McpTestHost.Staff("classes.rooms")).RoomsList());
        var byId = rooms.GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!);
        Assert.Equal(2, byId["r2"].GetProperty("floor").GetInt32());
        Assert.Equal(JsonValueKind.Null, byId["r3"].GetProperty("floor").ValueKind);
        Assert.Equal("Qavat ko'rsatilmagan", byId["r3"].GetProperty("floorLabel").GetString());
        roomsHost.AssertReadsOnlyDeclared(typeof(GroupTools));
    }

    // ================================================================== schedule_conflicts

    [Fact]
    public async Task Jadval_toqnashuvlari_panel_bilan_bir_xil()
    {
        using var host = new McpTestHost();
        SeedSchedule(host);

        var json = McpTestHost.Parse(await host.Tool<ScheduleTools>(McpTestHost.Staff("schedule.timetable")).ScheduleConflictPairs());

        var panel = await new RoomConflictService(host.Rw).AllPairsAsync();
        Assert.Equal(panel.Count, json.GetProperty("total").GetInt32());
        var pairs = json.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("kind").GetString(), string.Join("+", new[]
            {
                i.GetProperty("groupA").GetProperty("id").GetString(), i.GetProperty("groupB").GetProperty("id").GetString(),
            }.OrderBy(x => x)))).ToList();
        Assert.Contains(("room", "g1+g2"), pairs);
        Assert.Contains(("teacher", "g1+g3"), pairs);
        Assert.Contains(("room", "g2+g4"), pairs);
        Assert.DoesNotContain(pairs, p => p.Item2 == "g1+g4");               // 15:00 da tegib turadi — to'qnashuv emas
        Assert.DoesNotContain(pairs, p => p.Item2.Contains("gX"));          // arxivdagi guruh
        Assert.Equal(json.GetProperty("byKind").GetProperty("room").GetInt32() + json.GetProperty("byKind").GetProperty("teacher").GetInt32(),
            json.GetProperty("total").GetInt32());
        var first = json.GetProperty("items")[0];
        Assert.True(first.GetProperty("dayLabels").GetArrayLength() > 0);

        ScheduleTools Tool() => host.Tool<ScheduleTools>(McpTestHost.SuperAdmin());
        Assert.All(McpTestHost.Parse(await Tool().ScheduleConflictPairs("teacher")).GetProperty("items").EnumerateArray(),
            i => Assert.Equal("teacher", i.GetProperty("kind").GetString()));
        Assert.Equal(1, McpTestHost.Parse(await Tool().ScheduleConflictPairs(groupId: "g3")).GetProperty("total").GetInt32());
        await Assert.ThrowsAsync<McpException>(() => Tool().ScheduleConflictPairs("both"));
        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));
    }

    // ================================================================== tasks_list — tasdiq holati

    private static void SeedTasks(McpTestHost host)
    {
        var db = host.Rw;
        db.Users.AddRange(
            new AppUser { Id = "u1", FullName = "Olim Operator", Role = Roles.Staff, Email = "olim", PasswordHash = "H" },
            new AppUser { Id = "u2", FullName = "Nodira Menejer", Role = Roles.Admin, Email = "nodira", PasswordHash = "H" });
        db.WorkTaskBoards.Add(new WorkTaskBoard { Id = "b1", Title = "Umumiy" });
        db.WorkTaskColumns.AddRange(
            new WorkTaskColumn { Id = "c1", BoardId = "b1", Title = "Rejada", Order = 0 },
            new WorkTaskColumn { Id = "c2", BoardId = "b1", Title = "Bajarildi", Order = 1, IsDone = true });
        var now = AppClock.Now;
        db.WorkTasks.AddRange(
            new WorkTask { Id = "w1", BoardId = "b1", ColumnId = "c2", Title = "Hisobot", AssigneeId = "u1", CreatedById = "u2", ApprovalRequired = true, CompletedAt = now.AddHours(-3), CreatedAt = now.AddDays(-2) },
            new WorkTask { Id = "w2", BoardId = "b1", ColumnId = "c2", Title = "Majlis", AssigneeId = "u1", CreatedById = "u2", ApprovalRequired = true, ApprovedAt = now.AddHours(-1), ApprovedById = "u2", CompletedAt = now.AddHours(-2), CreatedAt = now.AddDays(-2) },
            new WorkTask { Id = "w3", BoardId = "b1", ColumnId = "c2", Title = "O'zimniki", AssigneeId = "u2", CreatedById = "u2", CompletedAt = now.AddHours(-1), CreatedAt = now.AddDays(-2) },
            new WorkTask { Id = "w4", BoardId = "b1", ColumnId = "c1", Title = "Qaytarilgan", AssigneeId = "u1", CreatedById = "u2", ApprovalRequired = true, ReturnCount = 1, LastReturnedAt = now.AddHours(-5), CreatedAt = now.AddDays(-3) },
            new WorkTask { Id = "w5", BoardId = "b1", ColumnId = "c1", Title = "Boshqasiniki", AssigneeId = "u2", CreatedById = "u1", CreatedAt = now.AddDays(-1) });
        db.SaveChanges();
    }

    private static List<string?> TaskIds(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();

    [Fact]
    public async Task Topshiriqlar_tasdiq_holati_bilan()
    {
        using var host = new McpTestHost();
        SeedTasks(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("tasks.board")).TasksList(state: "all");
        var json = McpTestHost.Parse(raw);
        var byId = json.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);

        Assert.Equal("awaiting_approval", byId["w1"].GetProperty("state").GetString());
        Assert.Equal("Tasdiq kutmoqda", byId["w1"].GetProperty("stateLabel").GetString());
        Assert.Equal("approved", byId["w2"].GetProperty("state").GetString());
        Assert.Equal("Nodira Menejer", byId["w2"].GetProperty("approval").GetProperty("approvedBy").GetString());
        Assert.Equal("done", byId["w3"].GetProperty("state").GetString());
        Assert.False(byId["w3"].GetProperty("approval").GetProperty("required").GetBoolean());
        Assert.Equal("open", byId["w4"].GetProperty("state").GetString());
        Assert.Equal(1, byId["w4"].GetProperty("approval").GetProperty("returnCount").GetInt32());
        Assert.Equal(JsonValueKind.String, byId["w4"].GetProperty("approval").GetProperty("lastReturnedAt").ValueKind);

        Assert.DoesNotContain("nodira\"", raw);   // login (Email) chiqmaydi
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        host.AssertNoSecretColumns();
    }

    [Theory]
    [InlineData("awaiting", null, new[] { "w1" })]
    [InlineData("approved", null, new[] { "w2" })]
    [InlineData("done", null, new[] { "w1", "w2", "w3" })]
    [InlineData("open", null, new[] { "w4", "w5" })]
    [InlineData("all", "u1", new[] { "w5" })]
    [InlineData("awaiting", "u2", new[] { "w1" })]
    public async Task Topshiriqlar_tasdiq_va_bergan_xodim_filtrlari(string state, string? createdById, string[] expected)
    {
        using var host = new McpTestHost();
        SeedTasks(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin())
            .TasksList(state: state, createdById: createdById));

        Assert.Equal(expected.OrderBy(x => x), TaskIds(json).OrderBy(x => x));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }
}
