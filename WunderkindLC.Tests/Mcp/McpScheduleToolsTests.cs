using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP: `schedule_day` (haftalik shablon va aniq sana — ko'chirish, o'rinbosar), `schedule_gaps` (bo'sh oraliqlar va
/// tavsiyalar) — panel bilan bir xil natija, buzuq guruhlar NOMI bilan, filtrlar, sahifalash va ruxsat.
/// </summary>
public class McpScheduleToolsTests
{
    private static List<string?> Groups(JsonElement json) =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("groupId").GetString()).ToList();

    private static List<string?> SkippedNames(JsonElement json) =>
        json.GetProperty("skippedGroups").GetProperty("groups").EnumerateArray()
            .Select(g => g.GetProperty("name").GetString()).ToList();

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" }, new Subject { Id = "c2", Name = "Matematika" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Teacher { Id = "t2", FullName = "Rustam Saidov" },
            new Teacher { Id = "t3", FullName = "Sardor Bo'sh" },
            new Teacher { Id = "t4", FullName = "Anvar Arxiv", IsArchived = true },
            new Room { Id = "r1", Name = "101", IsActive = true },
            new Room { Id = "r2", Name = "102", IsActive = true },
            new Group
            {
                Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1", RoomId = "r1", Days = [0, 2, 4],
                StartTime = "09:00", EndTime = "10:30", StartDate = "2026-09-01", Capacity = 12,
            },
            new Group
            {
                Id = "g2", Name = "English A2", CourseId = "c1", TeacherId = "t1", RoomId = "r1", Days = [0, 2],
                StartTime = "12:00", EndTime = "13:30", StartDate = "2026-09-01",
            },
            new Group
            {
                Id = "g3", Name = "Math B", CourseId = "c2", TeacherId = "t2", RoomId = "r2", Days = [0],
                StartTime = "10:00", EndTime = "11:00", StartDate = "2026-09-01",
            },
            // buzuq: tugash boshlanishdan oldin
            new Group { Id = "g4", Name = "Buzuq vaqt", CourseId = "c2", TeacherId = "t2", RoomId = "r2", Days = [0], StartTime = "13:30", EndTime = "03:00" },
            // buzuq: dars kunlari yo'q
            new Group { Id = "g5", Name = "Kunsiz", CourseId = "c1", TeacherId = "t1", Days = [], StartTime = "09:00", EndTime = "10:00" },
            new Group { Id = "g6", Name = "Arxiv guruh", CourseId = "c1", TeacherId = "t1", Days = [0], StartTime = "08:00", EndTime = "09:00", IsArchived = true },
            // xona FK yo'q — eski matnli nom
            new Group
            {
                Id = "g7", Name = "Zal guruhi", CourseId = "c2", TeacherId = "t2", Room = "Zal", Days = [1],
                StartTime = "15:00", EndTime = "16:00", StartDate = "2026-09-01",
            });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Bobur" },
            new Student { Id = "s3", FullName = "Charos" }, new Student { Id = "s4", FullName = "Davron", IsArchived = true });
        StudentGroup M(string sid, string gid, string status = "active") =>
            new() { StudentId = sid, GroupId = gid, Status = status, JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01" };
        db.StudentGroups.AddRange(M("s1", "g1"), M("s2", "g1", "trial"), M("s3", "g1", "frozen"), M("s4", "g1"), M("s1", "g3"));
        // Math B: 14-sentabr (Du) darsi 15-sentabrga (Se) soat 16:00 ga ko'chirilgan.
        db.LessonReschedules.Add(new LessonReschedule { ClassId = "g3", FromDate = "2026-09-14", ToDate = "2026-09-15", Time = "16:00" });
        // English A1: 14-sentabrda o'rinbosar — Sardor.
        db.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t3", Date = "2026-09-14",
            SelectedDates = ["2026-09-14"], Reason = "Kasal",
        });
        db.SaveChanges();
    }

    // ================================================================== schedule_day — hafta kuni

    [Fact]
    public async Task Hafta_kuni_jadvali_panel_toridagidek_va_buzuq_guruhlar_nomi_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<ScheduleTools>(McpTestHost.SuperAdmin()).ScheduleDay(day: 0));

        Assert.Equal("weekday", json.GetProperty("mode").GetString());
        Assert.Equal("Du", json.GetProperty("dayLabel").GetString());
        // Vaqt bo'yicha; buzuq (g4, g5) va arxivdagi (g6) guruh jadvalda yo'q.
        Assert.Equal(new[] { "g1", "g3", "g2" }, Groups(json));
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        var g1 = json.GetProperty("items")[0];
        Assert.Equal("English A1", g1.GetProperty("groupName").GetString());
        Assert.Equal("Ingliz tili", g1.GetProperty("course").GetProperty("name").GetString());
        Assert.Equal("Dilnoza Karimova", g1.GetProperty("teacher").GetProperty("name").GetString());
        Assert.Equal("101", g1.GetProperty("room").GetProperty("name").GetString());
        Assert.Equal("r1", g1.GetProperty("room").GetProperty("id").GetString());
        Assert.Equal("09:00", g1.GetProperty("startTime").GetString());
        Assert.Equal("10:30", g1.GetProperty("endTime").GetString());
        Assert.Equal(90, g1.GetProperty("minutes").GetInt32());
        // O'rin egallaganlar: faol + sinov; muzlatilgan va arxivdagi o'quvchi emas.
        Assert.Equal(2, g1.GetProperty("members").GetInt32());
        Assert.Equal(12, g1.GetProperty("capacity").GetInt32());
        Assert.Equal(new[] { "Du", "Ch", "Ju" }, g1.GetProperty("dayLabels").EnumerateArray().Select(d => d.GetString()));

        // Panel faqat SONINI ko'rsatadi (ScheduleGridDto.SkippedGroups = 2) — bu yerda nomlari ham.
        Assert.Equal(2, json.GetProperty("skippedGroups").GetProperty("count").GetInt32());
        Assert.Equal(new[] { "Buzuq vaqt", "Kunsiz" }, SkippedNames(json));

        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Hafta_kuni_jadvali_oqituvchi_va_xona_filtrlari_hamda_sahifalash()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<ScheduleTools>(McpTestHost.Staff("schedule.timetable"));

        var byTeacher = McpTestHost.Parse(await tools.ScheduleDay(day: 0, teacherId: "t1"));
        Assert.Equal(new[] { "g1", "g2" }, Groups(byTeacher));
        Assert.Equal(new[] { "Kunsiz" }, SkippedNames(byTeacher));

        var byRoom = McpTestHost.Parse(await tools.ScheduleDay(day: 0, roomId: "r2"));
        Assert.Equal(new[] { "g3" }, Groups(byRoom));
        Assert.Equal(new[] { "Buzuq vaqt" }, SkippedNames(byRoom));

        var tuesday = McpTestHost.Parse(await tools.ScheduleDay(day: 1));
        Assert.Equal(new[] { "g7" }, Groups(tuesday));
        Assert.Equal("Zal", tuesday.GetProperty("items")[0].GetProperty("room").GetProperty("name").GetString());
        Assert.Equal(new[] { "g7" }, Groups(McpTestHost.Parse(await tools.ScheduleDay(day: 1, roomId: "name:Zal"))));

        var paged = McpTestHost.Parse(await tools.ScheduleDay(day: 0, page: 2, pageSize: 2));
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "g2" }, Groups(paged));

        await Assert.ThrowsAsync<McpException>(() => tools.ScheduleDay(day: 7));
        await Assert.ThrowsAsync<McpException>(() => tools.ScheduleDay(day: 0, date: "2026-09-14"));
    }

    // ================================================================== schedule_day — sana

    [Fact]
    public async Task Sana_jadvali_kochirish_va_orinbosarni_hisobga_oladi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<ScheduleTools>(McpTestHost.Admin());

        // 14-sentabr (Du): Math B boshqa kunga ko'chgan; English A1 ni o'rinbosar o'tadi.
        var mon = McpTestHost.Parse(await tools.ScheduleDay(date: "2026-09-14"));
        Assert.Equal("date", mon.GetProperty("mode").GetString());
        Assert.Equal(0, mon.GetProperty("day").GetInt32());
        Assert.Equal(new[] { "g1", "g2" }, Groups(mon));
        var g1 = mon.GetProperty("items")[0];
        Assert.True(g1.GetProperty("substitute").GetBoolean());
        Assert.Equal("t3", g1.GetProperty("teacher").GetProperty("id").GetString());
        Assert.Equal("Sardor Bo'sh", g1.GetProperty("teacher").GetProperty("name").GetString());
        Assert.Equal("Dilnoza Karimova", g1.GetProperty("groupTeacher").GetProperty("name").GetString());
        Assert.Equal(2, g1.GetProperty("members").GetInt32());
        Assert.False(mon.GetProperty("items")[1].GetProperty("substitute").GetBoolean());
        Assert.Equal(2, mon.GetProperty("skippedGroups").GetProperty("count").GetInt32());

        // 15-sentabr (Se): Zal guruhi 15:00, ko'chirilgan Math B — yangi vaqt 16:00–17:00.
        var tue = McpTestHost.Parse(await tools.ScheduleDay(date: "2026-09-15"));
        Assert.Equal(new[] { "g7", "g3" }, Groups(tue));
        var moved = tue.GetProperty("items")[1];
        Assert.True(moved.GetProperty("moved").GetBoolean());
        Assert.Equal("16:00", moved.GetProperty("startTime").GetString());
        Assert.Equal("17:00", moved.GetProperty("endTime").GetString());
        Assert.Equal("102", moved.GetProperty("room").GetProperty("name").GetString());

        // O'qituvchi filtri: shu kuni darsni o'tadigan o'rinbosar.
        Assert.Equal(new[] { "g1" }, Groups(McpTestHost.Parse(await tools.ScheduleDay(date: "2026-09-14", teacherId: "t3"))));
        Assert.Equal(new[] { "g3" }, Groups(McpTestHost.Parse(await tools.ScheduleDay(date: "2026-09-15", roomId: "r2"))));

        // Hech narsa berilmasa — bugungi sana.
        var today = McpTestHost.Parse(await tools.ScheduleDay());
        Assert.Equal(McpToolContext.Iso(McpToolContext.Today), today.GetProperty("date").GetString());

        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Jadval_bolimi_ruxsatisiz_RAD_etiladi_va_hech_narsa_oqilmaydi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<ScheduleTools>(McpTestHost.Staff("classes.list"));

        await Assert.ThrowsAsync<McpAccessDeniedException>(() => tools.ScheduleDay(day: 0));
        await Assert.ThrowsAsync<McpAccessDeniedException>(() => tools.ScheduleGaps());
        Assert.Empty(host.Sql);
        Assert.True(host.State.Denied);
    }

    // ================================================================== schedule_gaps

    [Fact]
    public async Task Xonalardagi_bosh_oraliqlar_tigizlik_tartibida_va_tavsiyalar_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<ScheduleTools>(McpTestHost.SuperAdmin()).ScheduleGaps());

        Assert.Equal("room", json.GetProperty("scope").GetString());
        Assert.Equal(60, json.GetProperty("minMinutes").GetInt32());
        // Faqat 101-xona: Du va Ch kunlari 10:30–12:00 (ikki dars ORASIDA). Du tig'izroq (Math B ketyapti).
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var mon = json.GetProperty("items")[0];
        Assert.Equal("r1", mon.GetProperty("owner").GetProperty("id").GetString());
        Assert.Equal("101", mon.GetProperty("owner").GetProperty("name").GetString());
        Assert.Equal(0, mon.GetProperty("day").GetInt32());
        Assert.Equal("10:30", mon.GetProperty("start").GetString());
        Assert.Equal("12:00", mon.GetProperty("end").GetString());
        Assert.Equal(90, mon.GetProperty("minutes").GetInt32());
        Assert.Equal("English A1", mon.GetProperty("beforeGroup").GetString());
        Assert.Equal("English A2", mon.GetProperty("afterGroup").GetString());
        Assert.Equal(1, mon.GetProperty("peakScore").GetInt32());
        // Tavsiya: yonma-yon darsi bor (t1) tepada, keyin jadvalda darsi yo'q (t3); band (t2) va arxivdagi (t4) yo'q.
        Assert.Equal(new[] { "t1", "t3" },
            mon.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal(2, json.GetProperty("items")[1].GetProperty("day").GetInt32());

        host.AssertReadsOnlyDeclared(typeof(ScheduleTools));
        host.AssertNoSecretColumns();
    }

    [Fact]
    public async Task Bosh_oraliqlar_filtrlari_va_oqituvchi_kesimi()
    {
        using var host = new McpTestHost();
        Seed(host);
        var tools = host.Tool<ScheduleTools>(McpTestHost.Staff("schedule.timetable"));

        Assert.Equal(0, McpTestHost.Parse(await tools.ScheduleGaps(minMinutes: 120)).GetProperty("total").GetInt32());
        Assert.Equal(0, McpTestHost.Parse(await tools.ScheduleGaps(ownerId: "r2")).GetProperty("total").GetInt32());
        var wed = McpTestHost.Parse(await tools.ScheduleGaps(day: 2));
        Assert.Equal(1, wed.GetProperty("total").GetInt32());
        Assert.Equal("Ch", wed.GetProperty("items")[0].GetProperty("dayLabel").GetString());

        var teacher = McpTestHost.Parse(await tools.ScheduleGaps(scope: "teacher"));
        Assert.Equal(2, teacher.GetProperty("total").GetInt32());
        Assert.All(teacher.GetProperty("items").EnumerateArray(),
            g => Assert.Equal("t1", g.GetProperty("owner").GetProperty("id").GetString()));
        // Dushanba 10:30–12:00 da 102-xona band (Math B) — faqat 101 bo'sh.
        Assert.Equal(new[] { "r1" }, teacher.GetProperty("items")[0].GetProperty("suggestions").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()));
        // Chorshanba: ikkalasi bo'sh, kam band (102) tepada.
        Assert.Equal(new[] { "r2", "r1" }, teacher.GetProperty("items")[1].GetProperty("suggestions").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()));

        var paged = McpTestHost.Parse(await tools.ScheduleGaps(page: 2, pageSize: 1));
        Assert.Equal(2, paged.GetProperty("total").GetInt32());
        Assert.Equal(2, paged.GetProperty("items")[0].GetProperty("day").GetInt32());

        await Assert.ThrowsAsync<McpException>(() => tools.ScheduleGaps(scope: "group"));
        await Assert.ThrowsAsync<McpException>(() => tools.ScheduleGaps(day: -1));
    }
}
