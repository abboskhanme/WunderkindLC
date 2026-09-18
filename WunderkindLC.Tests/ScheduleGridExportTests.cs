using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BOSH SAHIFA jadval to'ri (<see cref="ScheduleGridExport"/>, <see cref="ScheduleService.GetGridAsync"/>) —
/// kun/vaqt filtri, ustunlar, kunlar yorlig'i va Excel eksporti.
/// </summary>
public class ScheduleGridExportTests
{
    private static ScheduleGridGroupDto G(
        string id, List<int> days, string start, string end,
        string room = "r1", string roomName = "1-xona", string teacher = "t1",
        string course = "eng", string status = "active") =>
        new(id, "Guruh " + id, course, "Ingliz tili", teacher, "O'qituvchi " + teacher, room, roomName,
            days, start, end, 0, 5, 10, Status: status);

    private static ScheduleGridDto Grid(params ScheduleGridGroupDto[] groups) => new(
        groups.ToList(),
        [new ScheduleOwnerDto("r1", "1-xona", 1), new ScheduleOwnerDto("r2", "2-xona", 0)],
        [new ScheduleOwnerDto("t1", "O'qituvchi t1", 1), new ScheduleOwnerDto("t2", "O'qituvchi t2", 0)],
        0);

    [Theory]
    [InlineData(new[] { 0, 2, 4 }, "Toq kunlar")]
    [InlineData(new[] { 4, 0, 2 }, "Toq kunlar")]
    [InlineData(new[] { 1, 3, 5 }, "Juft kunlar")]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5 }, "Har kuni")]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6 }, "Har kuni")]
    [InlineData(new[] { 0, 5, 6 }, "Du, Sha, Yak")]
    [InlineData(new int[0], "")]
    public void DaysLabel_edutizim_yozuvida(int[] days, string expected) =>
        Assert.Equal(expected, ScheduleGridExport.DaysLabel(days));

    [Fact]
    public void PlannedLessons_sanalar_orasidagi_dars_kunlari()
    {
        // 2026-09-14 — Dushanba. Du/Chor/Ju ikki hafta = 6 dars.
        Assert.Equal(6, ScheduleGridExport.PlannedLessons([0, 2, 4], "2026-09-14", "2026-09-27"));
        Assert.Equal(0, ScheduleGridExport.PlannedLessons([0, 2, 4], "", "2026-09-27"));
        Assert.Equal(0, ScheduleGridExport.PlannedLessons([0, 2, 4], "2026-09-27", "2026-09-14"));
        Assert.Equal(0, ScheduleGridExport.PlannedLessons([0, 2, 4], "2026-01-01", "2099-12-31"));
        Assert.Equal(0, ScheduleGridExport.PlannedLessons([], "2026-09-14", "2026-09-27"));
    }

    [Fact]
    public void MakeFilter_notogri_qiymatlar_standartga_tushadi()
    {
        var f = ScheduleGridExport.MakeFilter(9, "xyz", "25:00", "abc", " ", null, "", todayDay: 4);
        Assert.Equal(4, f.Day);
        Assert.False(f.ByTeacher);
        Assert.Equal(ScheduleGridExport.DefaultFromMin, f.FromMin);
        Assert.Equal(ScheduleGridExport.DefaultToMin, f.ToMin);
        Assert.Null(f.CourseId);
        Assert.Null(f.RoomId);

        // Teskari oraliq — standart.
        var r = ScheduleGridExport.MakeFilter(1, "teacher", "20:00", "09:00", null, null, null, 0);
        Assert.True(r.ByTeacher);
        Assert.Equal((ScheduleGridExport.DefaultFromMin, ScheduleGridExport.DefaultToMin), (r.FromMin, r.ToMin));
    }

    [Fact]
    public void LessonsOfDay_kun_vaqt_va_filtrlar()
    {
        var groups = new[]
        {
            G("a", [4], "09:00", "10:00"),
            G("b", [4], "07:00", "08:00"),                   // 08:00 dan oldin TUGAYDI — ko'rinmaydi
            G("c", [3], "09:00", "10:00"),                   // boshqa kun
            G("d", [4], "07:30", "09:00", course: "math"),   // oraliqqa tegadi
            G("e", [4], "12:00", "13:00", status: "blocked"),
        };
        var f = new ScheduleGridExport.Filter(4, false, 8 * 60, 22 * 60);
        Assert.Equal(new[] { "d", "a", "e" }, ScheduleGridExport.LessonsOfDay(groups, f).Select(g => g.GroupId));

        Assert.Equal(new[] { "d" }, ScheduleGridExport.LessonsOfDay(groups, f with { CourseId = "math" }).Select(g => g.GroupId));
        Assert.Equal(new[] { "e" }, ScheduleGridExport.LessonsOfDay(groups, f with { Status = "blocked" }).Select(g => g.GroupId));
        Assert.Equal(new[] { "a" }, ScheduleGridExport.LessonsOfDay(groups, f with { GroupId = "a" }).Select(g => g.GroupId));
    }

    [Fact]
    public void Range_oraliqdan_chiqqan_dars_uchun_kengayadi_va_yarim_soatga_yaxlitlanadi()
    {
        var f = new ScheduleGridExport.Filter(4, false, 8 * 60, 22 * 60);
        Assert.Equal((8 * 60, 22 * 60), ScheduleGridExport.Range([], f));
        Assert.Equal((7 * 60, 22 * 60 + 30),
            ScheduleGridExport.Range([G("a", [4], "07:15", "09:00"), G("b", [4], "21:00", "22:10")], f));
    }

    [Fact]
    public void Columns_bosh_xonalar_ham_va_xonasiz_dars_uchun_ustun()
    {
        var lessons = new[] { G("a", [4], "09:00", "10:00"), G("b", [4], "11:00", "12:00", room: "", roomName: "") };
        var f = new ScheduleGridExport.Filter(4, false, 8 * 60, 22 * 60);
        var cols = ScheduleGridExport.Columns(Grid(lessons), lessons, f);
        Assert.Equal(new[] { "r1", "r2", "" }, cols.Select(c => c.Key));

        var byTeacher = ScheduleGridExport.Columns(Grid(lessons), lessons, f with { ByTeacher = true });
        Assert.Equal(new[] { "t1", "t2" }, byTeacher.Select(c => c.Key));

        var oneRoom = ScheduleGridExport.Columns(Grid(lessons), lessons, f with { RoomId = "r2" });
        Assert.Equal(new[] { "r2" }, oneRoom.Select(c => c.Key));
    }

    [Fact]
    public void Sheets_tor_va_royxat()
    {
        var grid = Grid(G("a", [0, 2, 4], "09:00", "10:00"));
        var sheets = ScheduleGridExport.Sheets(grid, new ScheduleGridExport.Filter(4, false, 8 * 60, 22 * 60));

        var table = sheets[0];
        Assert.Equal("Jadval", table.Name);
        Assert.Equal(new[] { "Vaqt", "1-xona", "2-xona" }, table.Headers);
        var rows = table.Rows.ToList();
        Assert.Equal(28, rows.Count);                                   // 08:00–22:00, yarim soatdan
        Assert.Equal("09:00 - 09:30", rows[2][0]);
        Assert.StartsWith("09:00 - 10:00 · Ingliz tili · Guruh a", rows[2][1]);
        Assert.Equal("↳ Ingliz tili", rows[3][1]);                      // davomi
        Assert.Equal("", rows[4][1]);
        Assert.Equal("", rows[2][2]);                                   // bo'sh xona

        var list = sheets[1].Rows.Single();
        Assert.Equal("Toq kunlar", list[5]);
        Assert.Equal("5/10", list[6]);
    }

    [Fact]
    public void NaturalComparer_raqamlarni_son_sifatida_solishtiradi()
    {
        var names = new[] { "1-bino 10-xona", "1-bino 2-xona", "2-bino 1-xona", "1-bino 1-xona" };
        Assert.Equal(
            new[] { "1-bino 1-xona", "1-bino 2-xona", "1-bino 10-xona", "2-bino 1-xona" },
            names.OrderBy(n => n, ScheduleGridExport.NaturalComparer));
    }

    // ============================ ScheduleService.GetGridAsync ============================

    [Fact]
    public async Task GetGridAsync_guruh_kunlari_azolar_darslar_va_bosh_xonalar()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var busy = new Room { Name = "1-bino 2-xona" };
        var empty = new Room { Name = "1-bino 10-xona" };
        var off = new Room { Name = "Yopilgan xona", IsActive = false };
        ctx.Rooms.AddRange(busy, empty, off);
        var teacher = new Teacher { FullName = "Aliyev" };
        var idle = new Teacher { FullName = "Bo'sh o'qituvchi" };
        ctx.Teachers.AddRange(teacher, idle);

        var g = new Group
        {
            Name = "A", TeacherId = teacher.Id, RoomId = busy.Id, Days = [0, 2, 4],
            StartTime = "09:00", EndTime = "10:30", Capacity = 12,
            StartDate = "2026-09-14", EndDate = "2026-09-27",
        };
        var broken = new Group { Name = "Buzuq", Days = [1], StartTime = "13:30", EndTime = "03:00" };
        var inOffRoom = new Group { Name = "B", RoomId = off.Id, Days = [1], StartTime = "10:00", EndTime = "11:00" };
        ctx.Classes.AddRange(g, broken, inOffRoom);

        var s1 = new Student { FullName = "S1" };
        var s2 = new Student { FullName = "S2" };
        var s3 = new Student { FullName = "S3" };
        ctx.Students.AddRange(s1, s2, s3);
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = s1.Id, GroupId = g.Id, Status = "active", IsActive = true },
            new StudentGroup { StudentId = s2.Id, GroupId = g.Id, Status = "trial", IsActive = true },
            // Muzlatilgan o'rin band qilmaydi.
            new StudentGroup { StudentId = s3.Id, GroupId = g.Id, Status = "frozen", IsActive = true });
        ctx.LessonNotes.AddRange(
            new LessonNote { ClassId = g.Id, Date = "2026-09-14", Period = 1, Conducted = true },
            new LessonNote { ClassId = g.Id, Date = "2026-09-16", Period = 1, Conducted = true },
            new LessonNote { ClassId = g.Id, Date = "2026-09-18", Period = 1, Conducted = false });
        await ctx.SaveChangesAsync();

        var grid = await new ScheduleService(ctx).GetGridAsync();

        Assert.Equal(1, grid.SkippedGroups);
        var a = grid.Groups.Single(x => x.GroupId == g.Id);
        Assert.Equal(new[] { 0, 2, 4 }, a.Days);
        Assert.Equal(("09:00", "10:30", 90), (a.Start, a.End, a.Minutes));
        Assert.Equal((2, 12), (a.Members, a.Capacity));
        Assert.Equal((2, 6), (a.LessonsDone, a.LessonsTotal));
        Assert.Equal("active", a.Status);

        // Faol xonalar (darssizi ham) + darsi bor yopilgan xona; tartib — tabiiy.
        Assert.Equal(new[] { "1-bino 2-xona", "1-bino 10-xona", "Yopilgan xona" }, grid.Rooms.Select(r => r.Name));
        // Darsi yo'q o'qituvchi ham ro'yxatda.
        Assert.Contains(grid.Teachers, t => t.Id == idle.Id && t.GroupCount == 0);
    }
}
