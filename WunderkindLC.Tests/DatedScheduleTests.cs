using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «Kun» / «Hafta» dated schedule (CRM Phase 5, <c>DatedSchedule</c>): real lessons of real dates — moved lessons on
/// their NEW date/time, substitutes named, group lifetime respected, rooms grouped by floor; plus the room floor rules.
/// </summary>
public class DatedScheduleTests
{
    private static readonly DateOnly D0 = new(2026, 10, 12);   // Monday
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<WunderkindLC.Application.Dtos.DatedScheduleDto> BuildAsync(TestDb db, DateOnly from, DateOnly to) =>
        DatedSchedule.Build(await DatedSchedule.LoadAsync(db.Context, from, to), from, to);

    private static (Room R1, Room R2, Room R3) Rooms(TestDb db)
    {
        var r1 = new Room { Name = "10-xona", Floor = 2 };
        var r2 = new Room { Name = "2-xona", Floor = 1 };
        var r3 = new Room { Name = "Zal" };
        db.Context.Rooms.AddRange(r1, r2, r3);
        db.Context.Teachers.AddRange(
            new Teacher { Id = "t1", FullName = "Ali" },
            new Teacher { Id = "t2", FullName = "Vali" },
            new Teacher { Id = "t3", FullName = "Arxivdagi", IsArchived = true });
        db.Context.SaveChanges();
        return (r1, r2, r3);
    }

    [Fact]
    public async Task Kochirilgan_dars_yangi_sana_va_vaqtida_asl_kunida_yoq()
    {
        using var db = TestDb.Sqlite();
        var (r1, _, _) = Rooms(db);
        var g = new Group
        {
            Name = "Ingliz A1", RoomId = r1.Id, TeacherId = "t1", Days = [0], StartTime = "14:00", EndTime = "15:30",
            StartDate = "2026-09-01",
        };
        db.Context.Classes.Add(g);
        db.Context.LessonReschedules.Add(new LessonReschedule
        {
            ClassId = g.Id, FromDate = Iso(D0), ToDate = Iso(D0.AddDays(2)), Time = "16:00",
        });
        await db.Context.SaveChangesAsync();

        var week = await BuildAsync(db, D0, D0.AddDays(6));
        var l = Assert.Single(week.Lessons);
        Assert.Equal(Iso(D0.AddDays(2)), l.Date);
        Assert.Equal(("16:00", "17:30"), (l.Start, l.End));
        Assert.True(l.Moved);
        Assert.Equal(2, l.Floor);
        Assert.Equal("Ali", l.TeacherName);
    }

    [Fact]
    public async Task Orinbosar_nomi_va_guruh_umri()
    {
        using var db = TestDb.Sqlite();
        var (_, r2, _) = Rooms(db);
        var g = new Group { Name = "Mat", RoomId = r2.Id, TeacherId = "t1", Days = [0, 2], StartTime = "09:00", EndTime = "10:00" };
        var later = new Group { Name = "Kech boshlanadi", TeacherId = "t2", Days = [0], StartTime = "11:00", EndTime = "12:00", StartDate = Iso(D0.AddDays(1)) };
        var ended = new Group { Name = "Tugagan", TeacherId = "t2", Days = [2], StartTime = "11:00", EndTime = "12:00", EndDate = Iso(D0.AddDays(1)) };
        var broken = new Group { Name = "Buzuq", TeacherId = "t2", Days = [0], StartTime = "13:30", EndTime = "03:00" };
        db.Context.Classes.AddRange(g, later, ended, broken);
        db.Context.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = "t1", SubstituteTeacherId = "t2",
            Date = Iso(D0), SelectedDates = [Iso(D0)], IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var day = await BuildAsync(db, D0, D0);
        var l = Assert.Single(day.Lessons);   // "Kech boshlanadi" hali boshlanmagan, "Buzuq" — vaqti noto'g'ri
        Assert.Equal(("t2", "Vali"), (l.SubstituteTeacherId, l.SubstituteTeacherName));
        Assert.Equal(1, day.SkippedGroups);

        var wed = await BuildAsync(db, D0.AddDays(2), D0.AddDays(2));
        Assert.Equal(["Mat"], wed.Lessons.Select(x => x.GroupName));   // "Tugagan" EndDate dan keyin yo'q
        Assert.Equal("", wed.Lessons[0].SubstituteTeacherId);
    }

    [Fact]
    public async Task Xonalar_qavat_boyicha_va_arxivdagi_oqituvchi_faqat_darsi_bolsa()
    {
        using var db = TestDb.Sqlite();
        Rooms(db);
        db.Context.Rooms.Add(new Room { Name = "Yopilgan", Floor = 1, IsActive = false });
        await db.Context.SaveChangesAsync();

        var day = await BuildAsync(db, D0, D0);
        Assert.Equal(["2-xona", "10-xona", "Zal"], day.Rooms.Select(r => r.Name));
        Assert.Equal([1, 2, (int?)null], day.Rooms.Select(r => r.Floor));
        Assert.Equal(["Ali", "Vali"], day.Teachers.Select(t => t.Name));
    }

    /// <summary>
    /// «Davomat olindi» — YAGONA ta'rif (U3a, journal.md): guruhning shu kundagi istalgan LessonNote ida BIRINCHI davomat
    /// belgisi (<c>AttendanceTakenAt</c>) — dars monitoringi, eslatma va jarima bilan bir xil. Faqat baho qo'yilgan
    /// («o'tildi», <c>Conducted</c>) dars davomat olingan EMAS; belgi boshqa fan/dars raqamidagi yozuvda bo'lsa ham sanaladi.
    /// </summary>
    [Fact]
    public async Task Davomat_olindi_birinchi_belgi_boyicha_faqat_baho_emas()
    {
        using var db = TestDb.Sqlite();
        Rooms(db);
        Group G(string name) => new() { Name = name, TeacherId = "t1", Days = [0], StartTime = "14:00", EndTime = "15:00", CourseId = "c1" };
        var marked = G("Belgilangan");
        var gradeOnly = G("Faqat baho");
        var otherPeriod = G("Boshqa dars raqami");
        var nothing = G("Hech narsa");
        db.Context.Classes.AddRange(marked, gradeOnly, otherPeriod, nothing);
        var day = Iso(D0);
        db.Context.LessonNotes.AddRange(
            new LessonNote { ClassId = marked.Id, SubjectId = "c1", Date = day, Period = 1, Conducted = true, AttendanceTakenAt = D0.ToDateTime(new TimeOnly(14, 5)) },
            new LessonNote { ClassId = gradeOnly.Id, SubjectId = "c1", Date = day, Period = 1, Conducted = true },
            new LessonNote { ClassId = otherPeriod.Id, SubjectId = "c2", Date = day, Period = 3, AttendanceTakenAt = D0.ToDateTime(new TimeOnly(14, 20)) },
            // Boshqa kundagi belgi bu kunga ta'sir qilmaydi.
            new LessonNote { ClassId = nothing.Id, SubjectId = "c1", Date = Iso(D0.AddDays(-7)), Period = 1, Conducted = true, AttendanceTakenAt = D0.AddDays(-7).ToDateTime(new TimeOnly(14, 1)) });
        await db.Context.SaveChangesAsync();

        var taken = (await BuildAsync(db, D0, D0)).Lessons.ToDictionary(l => l.GroupName, l => l.AttendanceTaken);
        Assert.True(taken["Belgilangan"]);
        Assert.False(taken["Faqat baho"]);
        Assert.True(taken["Boshqa dars raqami"]);
        Assert.False(taken["Hech narsa"]);
    }

    [Fact]
    public void Oraliq_tekshiruvi()
    {
        var today = D0;
        Assert.Equal((today, today, (string?)null), DatedSchedule.ParseRange(null, null, today));
        Assert.Null(DatedSchedule.ParseRange("2026-10-12", "2026-10-18", today).Error);
        Assert.Equal(DatedSchedule.RangeTooLongMessage, DatedSchedule.ParseRange("2026-10-12", "2026-10-19", today).Error);
        Assert.Equal(DatedSchedule.BadRangeMessage, DatedSchedule.ParseRange("2026-10-12", "2026-10-11", today).Error);
        Assert.Equal(DatedSchedule.BadRangeMessage, DatedSchedule.ParseRange("12.10.2026", null, today).Error);
    }

    [Fact]
    public async Task Kunlik_eksport_haqiqiy_darslardan()
    {
        using var db = TestDb.Sqlite();
        var (r1, _, _) = Rooms(db);
        var g = new Group { Name = "Ingliz", RoomId = r1.Id, TeacherId = "t1", Days = [0], StartTime = "14:00", EndTime = "15:30" };
        db.Context.Classes.Add(g);
        await db.Context.SaveChangesAsync();

        var grid = DatedSchedule.ToGrid(await BuildAsync(db, D0, D0), 0);
        var row = Assert.Single(grid.Groups);
        Assert.Equal([0], row.Days);
        Assert.Equal(90, row.Minutes);
        Assert.NotEmpty(ScheduleGridExport.Sheets(grid, ScheduleGridExport.MakeFilter(0, "room", null, null, null, null, null, 0)));
    }

    // ------------------------------- Room floor -------------------------------

    [Fact]
    public void Qavat_tahrirda_yuborilmasa_saqlanadi_ClearFloor_tozalaydi()
    {
        Assert.Equal(3, RoomFloors.Resolve(3, null, clear: false));
        Assert.Equal(4, RoomFloors.Resolve(3, 4, clear: false));
        Assert.Null(RoomFloors.Resolve(3, null, clear: true));
        Assert.Null(RoomFloors.Resolve(3, 7, clear: true));
        Assert.True(RoomFloors.IsValid(null));
        Assert.True(RoomFloors.IsValid(-2));
        Assert.True(RoomFloors.IsValid(50));
        Assert.False(RoomFloors.IsValid(-3));
        Assert.False(RoomFloors.IsValid(51));
        Assert.Equal("2-qavat", RoomFloors.Label(2));
        Assert.Equal("Qavat ko'rsatilmagan", RoomFloors.Label(null));
    }
}
