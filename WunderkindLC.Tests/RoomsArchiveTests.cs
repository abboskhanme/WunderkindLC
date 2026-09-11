using System.Text.Json;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// XONALAR va ARXIV testlari: <see cref="RoomConflictService"/> (jadval to'qnashuvi),
/// <see cref="RoomUtilizationService"/> (bandlik metrikalari) va <see cref="ArchiveService"/>
/// (o'chirishdan oldingi JSON surat).
/// </summary>
public class RoomsArchiveTests
{
    // ===================== 1) Xona / o'qituvchi to'qnashuvi =====================

    private const string RoomA = "room-a";
    private const string RoomB = "room-b";
    private const string TeacherA = "teacher-a";
    private const string TeacherB = "teacher-b";

    /// <summary>Guruhning <c>RoomId</c> ustuni <see cref="Room"/> ga HAQIQIY tashqi kalit —
    /// shu sababli har testda xonalar (va qulaylik uchun o'qituvchilar) oldindan yaratiladi.</summary>
    private static void SeedRefs(WunderkindLC.Infrastructure.Data.AppDbContext ctx)
    {
        ctx.Rooms.AddRange(
            new Room { Id = RoomA, Name = "Xona A" },
            new Room { Id = RoomB, Name = "Xona B" });
        ctx.Teachers.AddRange(
            new Teacher { Id = TeacherA, FullName = "O'qituvchi A" },
            new Teacher { Id = TeacherB, FullName = "O'qituvchi B" });
    }

    private static Group Existing(
        string name = "Mavjud guruh", string? roomId = RoomA, string teacherId = TeacherA,
        string start = "09:00", string end = "10:00", bool archived = false, params int[] days) => new()
    {
        Name = name,
        RoomId = roomId,
        TeacherId = teacherId,
        StartTime = start,
        EndTime = end,
        IsArchived = archived,
        Days = days.Length > 0 ? days.ToList() : new List<int> { 0, 2 },   // Du, Ch
    };

    [Fact]
    public async Task Toqnashuv_xona_va_oqituvchi_berilmasa_bosh_royxat()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing());
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(null, null, new List<int> { 0 }, "09:00", "10:00"));
        Assert.Empty(await svc.CheckRoomConflictAsync("", "  ", new List<int> { 0 }, "09:00", "10:00"));
    }

    [Fact]
    public async Task Toqnashuv_kunlar_yoki_vaqt_bosh_bolsa_tekshirilmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing());
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int>(), "09:00", "10:00"));
        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "", "10:00"));
        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "09:00", null));
    }

    [Fact]
    public async Task Toqnashuv_bir_xil_xona_va_vaqtda_topiladi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(days: new[] { 0, 2 }));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        var conflicts = await svc.CheckRoomConflictAsync(RoomA, TeacherB, new List<int> { 0 }, "09:30", "10:30");

        var c = Assert.Single(conflicts);
        Assert.Equal("room", c.Reason);
        Assert.Equal("Mavjud guruh", c.GroupName);
        Assert.Equal("Du", c.SharedDays);
        Assert.Equal("09:00–10:00", c.ExistingSlot);
    }

    [Fact]
    public async Task Toqnashuv_tugash_vaqti_boshlanish_vaqtiga_teng_bolsa_yoq()
    {
        // Yarim-ochiq oraliq [start, end): 09:00–10:00 va 10:00–11:00 TO'QNASHMAYDI.
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing());
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "10:00", "11:00"));
        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "08:00", "09:00"));
    }

    [Fact]
    public async Task Toqnashuv_bir_daqiqalik_kesishuvda_ham_topiladi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing());
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Single(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "09:59", "11:00"));
        Assert.Single(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "08:00", "09:01"));
    }

    [Fact]
    public async Task Toqnashuv_yangi_dars_mavjudini_toliq_qamrasa_ham_topiladi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing());
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Single(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "08:00", "12:00"));
    }

    [Fact]
    public async Task Toqnashuv_boshqa_kunlarda_yoq()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(days: new[] { 0, 2 }));   // Du, Ch
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 1, 3 }, "09:00", "10:00"));
    }

    [Fact]
    public async Task Toqnashuv_arxivlangan_guruh_hisobga_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(archived: true));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "09:00", "10:00"));
    }

    [Fact]
    public async Task Toqnashuv_tahrirlanayotgan_guruhning_ozi_hisobga_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        var g = Existing();
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(
            RoomA, null, new List<int> { 0 }, "09:00", "10:00", excludeGroupId: g.Id));
    }

    [Fact]
    public async Task Toqnashuv_oqituvchi_boyicha_boshqa_xonada_ham_topiladi()
    {
        // O'qituvchi bir vaqtda ikki guruhda bo'la olmaydi — xona boshqa bo'lsa ham.
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(roomId: RoomA, teacherId: TeacherA));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        var c = Assert.Single(await svc.CheckRoomConflictAsync(RoomB, TeacherA, new List<int> { 0 }, "09:00", "10:00"));
        Assert.Equal("teacher", c.Reason);
    }

    [Fact]
    public async Task Toqnashuv_vaqti_kiritilmagan_guruhni_tekshirmaydi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(start: "", end: ""));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync(RoomA, TeacherA, new List<int> { 0 }, "09:00", "10:00"));
    }

    [Fact]
    public async Task Toqnashuv_umumiy_kunlar_qisqartma_bilan_tartiblab_yoziladi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.Add(Existing(days: new[] { 4, 0, 2 }));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        var c = Assert.Single(await svc.CheckRoomConflictAsync(
            RoomA, null, new List<int> { 2, 4, 0, 6 }, "09:00", "10:00"));
        Assert.Equal("Du, Ch, Jum", c.SharedDays);
    }

    [Fact]
    public async Task Toqnashuv_bir_nechta_guruh_bilan_bolsa_hammasi_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        SeedRefs(db.Context);
        db.Context.Classes.AddRange(
            Existing("Birinchi"),
            Existing("Ikkinchi", start: "09:30", end: "10:30"));
        db.Context.SaveChanges();
        var svc = new RoomConflictService(db.Context);

        var conflicts = await svc.CheckRoomConflictAsync(RoomA, null, new List<int> { 0 }, "09:00", "11:00");
        Assert.Equal(2, conflicts.Count);
    }

    // ===================== 2) Xona bandligi (utilization) =====================

    private static Room NewRoom(string name = "Xona 1", int capacity = 10) =>
        new() { Name = name, Capacity = capacity, IsActive = true };

    private static void AddMembers(WunderkindLC.Infrastructure.Data.AppDbContext ctx, Group g, int count,
        string status = "active", bool active = true)
    {
        for (var i = 0; i < count; i++)
        {
            var s = new Student { FullName = $"O'quvchi {Guid.NewGuid():N}" };
            ctx.Students.Add(s);
            ctx.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id, GroupId = g.Id, IsActive = active, Status = status,
            });
        }
    }

    [Fact]
    public async Task Bandlik_guruhsiz_xona_bosh_deb_belgilanadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Rooms.Add(NewRoom());
        db.Context.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(db.Context).GetRoomUtilizationAsync());
        Assert.Equal("Bo'sh", m.EfficiencyStatus);
        Assert.Equal(0, m.TotalSlots);
        Assert.Equal(0, m.ActiveGroupCount);
        Assert.Equal(0, m.EfficiencyScore);
    }

    [Fact]
    public async Task Bandlik_faol_bolmagan_xona_royxatga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var room = NewRoom();
        room.IsActive = false;
        db.Context.Rooms.Add(room);
        db.Context.SaveChanges();

        Assert.Empty(await new RoomUtilizationService(db.Context).GetRoomUtilizationAsync());
    }

    [Fact]
    public async Task Bandlik_slotlar_va_foiz_togri_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom(capacity: 10);
        var g = new Group { Name = "G1", RoomId = room.Id, StartTime = "09:00", EndTime = "11:00", Days = new List<int> { 0, 2 } };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 6);
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());

        Assert.Equal(1, m.ActiveGroupCount);
        Assert.Equal(10, m.TotalSlots);
        Assert.Equal(6, m.CurrentStudents);
        Assert.Equal(4, m.Gap);
        Assert.Equal(60.0, m.OccupancyPercent);
        Assert.Equal(4.0, m.WeeklyActiveHours);        // 2 kun × 2 soat
        Assert.Equal("Optimal", m.EfficiencyStatus);
        Assert.Equal(new[] { "G1" }, m.GroupNames);
    }

    [Fact]
    public async Task Bandlik_muzlatilgan_va_chiqarilgan_azolar_sanalmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom(capacity: 10);
        var g = new Group { Name = "G1", RoomId = room.Id, StartTime = "09:00", EndTime = "10:00", Days = new List<int> { 0 } };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 3);                                  // faol
        AddMembers(ctx, g, 2, status: "frozen");                // muzlatilgan — sanalmaydi
        AddMembers(ctx, g, 4, status: "active", active: false); // a'zolik yopilgan — sanalmaydi
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal(3, m.CurrentStudents);
    }

    [Fact]
    public async Task Bandlik_arxiv_guruh_hisobga_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom();
        var g = new Group { Name = "Arxiv", RoomId = room.Id, IsArchived = true, StartTime = "09:00", EndTime = "10:00" };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 5);
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal(0, m.ActiveGroupCount);
        Assert.Equal("Bo'sh", m.EfficiencyStatus);
    }

    [Fact]
    public async Task Bandlik_matnli_xona_nomi_orqali_ham_boglanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom("Xona 7");
        var g = new Group { Name = "Eski guruh", Room = "xona 7", StartTime = "09:00", EndTime = "10:00", Days = new List<int> { 0 } };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 2);
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal(1, m.ActiveGroupCount);   // registr farqisiz moslashadi
        Assert.Equal(2, m.CurrentStudents);
    }

    [Fact]
    public async Task Bandlik_sigimdan_oshsa_tolib_toshgan_deb_belgilanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom(capacity: 3);
        var g = new Group { Name = "G1", RoomId = room.Id, StartTime = "09:00", EndTime = "10:00", Days = new List<int> { 0 } };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 5);
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal("To'lib toshgan", m.EfficiencyStatus);
        Assert.Equal(0, m.Gap);
    }

    [Fact]
    public async Task Bandlik_guruhi_bor_lekin_oquvchisi_yoq_xona_kam_tolgan_deb_belgilanadi()
    {
        // HOZIRGI XULQ (xatoni qayd etuvchi yashil test): occupancy < 30 sharti "Bo'sh" shartidan
        // OLDIN tekshiriladi ⇒ o'quvchisi umuman yo'q xona ham "Kam to'lgan" bo'lib chiqadi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom();
        ctx.Rooms.Add(room);
        ctx.Classes.Add(new Group
        {
            Name = "Bo'sh guruh", RoomId = room.Id, StartTime = "09:00", EndTime = "10:00",
            Days = new List<int> { 0 },
        });
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal(0, m.CurrentStudents);
        Assert.Equal("Kam to'lgan", m.EfficiencyStatus);
    }

    [Fact(Skip = "XATO (RoomUtilizationService.cs:130-133): status shartlar tartibi noto'g'ri — " +
                 "`occupancyPct < 30` sharti `currentStudents == 0` dan oldin turgani uchun \"Bo'sh\" " +
                 "tarmog'iga hech qachon yetib borilmaydi. Guruhi bor, lekin bironta o'quvchisi yo'q xona " +
                 "\"Kam to'lgan\" deb ko'rsatiladi. Tuzatish: `currentStudents == 0` tekshiruvini birinchi qo'yish.")]
    public async Task Bandlik_oquvchisi_yoq_xona_bosh_deb_belgilanishi_kerak()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom();
        ctx.Rooms.Add(room);
        ctx.Classes.Add(new Group
        {
            Name = "Bo'sh guruh", RoomId = room.Id, StartTime = "09:00", EndTime = "10:00",
            Days = new List<int> { 0 },
        });
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal("Bo'sh", m.EfficiencyStatus);
    }

    [Fact]
    public async Task Bandlik_royxati_samaradorlik_boyicha_kamayib_tartiblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var busy = NewRoom("Band xona", capacity: 10);
        var idle = NewRoom("Bo'sh xona", capacity: 10);
        var g = new Group { Name = "G1", RoomId = busy.Id, StartTime = "09:00", EndTime = "13:00", Days = new List<int> { 0, 1, 2, 3, 4 } };
        ctx.Rooms.AddRange(busy, idle);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 8);
        ctx.SaveChanges();

        var list = await new RoomUtilizationService(ctx).GetRoomUtilizationAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("Band xona", list[0].RoomName);
        Assert.True(list[0].EfficiencyScore > list[1].EfficiencyScore);
    }

    [Fact]
    public async Task Bandlik_haftalik_foiz_100_dan_oshmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom(capacity: 10);
        // 7 kun × 14 soat = 98 soat > 6×14 = 84 soat sig'imi.
        var g = new Group
        {
            Name = "Tinimsiz", RoomId = room.Id, StartTime = "08:00", EndTime = "22:00",
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 5);
        ctx.SaveChanges();

        var m = Assert.Single(await new RoomUtilizationService(ctx).GetRoomUtilizationAsync());
        Assert.Equal(100.0, m.WeeklyUtilizationPercent);
        Assert.InRange(m.EfficiencyScore, 0, 100);
    }

    [Fact]
    public async Task Xona_tafsiloti_topilmagan_xonada_null_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var svc = new RoomUtilizationService(db.Context);
        Assert.Null(await svc.GetRoomDetailMetricAsync("yoq"));
        Assert.Null(await svc.GetRoomCapacityAsync("yoq"));
    }

    [Fact]
    public async Task Xona_tafsiloti_guruh_va_oquvchi_sonini_beradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom(capacity: 10);
        var teacher = new Teacher { FullName = "Karimov Karim" };
        var subject = new Subject { Name = "IELTS" };
        var g = new Group
        {
            Name = "G1", RoomId = room.Id, TeacherId = teacher.Id, CourseId = subject.Id,
            StartTime = "09:00", EndTime = "10:30", Days = new List<int> { 0, 2 },
        };
        ctx.Rooms.Add(room);
        ctx.Teachers.Add(teacher);
        ctx.Subjects.Add(subject);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 4);
        ctx.SaveChanges();

        var d = await new RoomUtilizationService(ctx).GetRoomDetailMetricAsync(room.Id);

        Assert.NotNull(d);
        Assert.Equal(1, d!.GroupCount);
        Assert.Equal(10, d.TotalSlots);
        Assert.Equal(4, d.ActualStudents);
        Assert.Equal(40.0, d.OccupancyPercent);
        Assert.Equal(d.OccupancyPercent, d.UtilizationPercent);
        Assert.Equal(6, d.Gap);
        var gd = Assert.Single(d.Groups);
        Assert.Equal("IELTS", gd.CourseName);
        Assert.Equal("Karimov Karim", gd.TeacherName);
        Assert.Equal("Du-Ch", gd.Days);
        Assert.Equal("09:00-10:30", gd.TimeSlot);
    }

    [Fact]
    public async Task Xona_sigimi_metrikasi_bosh_xonada_Empty_holatini_beradi()
    {
        using var db = TestDb.Sqlite();
        var room = NewRoom();
        db.Context.Rooms.Add(room);
        db.Context.SaveChanges();

        var c = await new RoomUtilizationService(db.Context).GetRoomCapacityAsync(room.Id);

        Assert.NotNull(c);
        Assert.Equal("Empty", c!.Status);
        Assert.Equal(0, c.TotalSlots);
        Assert.Empty(c.Groups);
    }

    [Fact]
    public async Task Xona_tafsiloti_matnli_nom_registri_farq_qilsa_guruhni_kormaydi()
    {
        // HOZIRGI XULQ (xatoni qayd etuvchi yashil test): dashboard matnli xona nomini REGISTR
        // FARQISIZ moslaydi (roomByName — OrdinalIgnoreCase), tafsilot/sig'im so'rovlari esa
        // `c.Room == room.Name` (SQL `=`, registrga sezgir) ⇒ bir xil guruh ikki joyda TURLICHA sanaladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom("Xona 7");
        var g = new Group
        {
            Name = "Eski guruh", Room = "xona 7",   // kichik harf bilan yozilgan
            StartTime = "09:00", EndTime = "10:00", Days = new List<int> { 0 },
        };
        ctx.Rooms.Add(room);
        ctx.Classes.Add(g);
        AddMembers(ctx, g, 3);
        ctx.SaveChanges();

        var svc = new RoomUtilizationService(ctx);
        var dashboard = Assert.Single(await svc.GetRoomUtilizationAsync());
        var detail = await svc.GetRoomDetailMetricAsync(room.Id);
        var capacity = await svc.GetRoomCapacityAsync(room.Id);

        Assert.Equal(1, dashboard.ActiveGroupCount);   // dashboard ko'radi
        Assert.Equal(0, detail!.GroupCount);           // tafsilot KO'RMAYDI
        Assert.Equal(0, capacity!.GroupCount);         // sig'im metrikasi ham KO'RMAYDI
    }

    [Fact(Skip = "XATO (RoomUtilizationService.cs:169-170 va 224-225): tafsilot/sig'im so'rovlarida " +
                 "matnli xona nomi `c.Room == room.Name` bilan (SQL `=`, REGISTRGA SEZGIR) solishtiriladi, " +
                 "dashboard esa OrdinalIgnoreCase lug'at ishlatadi. Xona nomi boshqa registrda yozilgan " +
                 "eski guruh dashboardda sanaladi, xona kartasi/modalida esa g'oyib bo'ladi (guruh soni 0, " +
                 "bandlik 0%). Tuzatish: uchala joyda YAGONA moslash (masalan dashboarddagi kabi " +
                 "registrsiz lug'at orqali xona id'sini oldindan aniqlash).")]
    public async Task Xona_tafsiloti_matnli_nom_registri_farq_qilsa_ham_guruhni_hisoblashi_kerak()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = NewRoom("Xona 7");
        ctx.Rooms.Add(room);
        ctx.Classes.Add(new Group
        {
            Name = "Eski guruh", Room = "xona 7",
            StartTime = "09:00", EndTime = "10:00", Days = new List<int> { 0 },
        });
        ctx.SaveChanges();

        var detail = await new RoomUtilizationService(ctx).GetRoomDetailMetricAsync(room.Id);
        Assert.Equal(1, detail!.GroupCount);
    }

    // ===================== 3) Arxiv (o'chirishdan oldingi surat) =====================

    private static Lead SampleLead() => new()
    {
        FullName = "Yangi Lid",
        Phone = "901234567",
        Source = "Instagram",
        InterestSubject = "IELTS",
        CreatedAt = AppClock.Iso(),
    };

    [Fact]
    public void Arxiv_surati_asosiy_maydonlar_bilan_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SampleLead();

        ArchiveService.Snapshot(ctx, "lead", lead.Id, lead.FullName, lead.Phone, lead, "Qiziqmadi", "Admin Aliyev");
        ctx.SaveChanges();

        var rec = Assert.Single(ctx.ArchivedRecords);
        Assert.Equal("lead", rec.Type);
        Assert.Equal(lead.Id, rec.EntityId);
        Assert.Equal("Yangi Lid", rec.Title);
        Assert.Equal("901234567", rec.Subtitle);
        Assert.Equal("Qiziqmadi", rec.Reason);
        Assert.Equal("Admin Aliyev", rec.ActorName);
    }

    [Fact]
    public void Arxiv_surati_SaveChangesni_ozi_chaqirmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        ArchiveService.Snapshot(ctx, "lead", "id-1", "T", "S", SampleLead(), null, "Admin");

        // Chaqiruvchi SaveChanges qilmaguncha bazada yozuv yo'q.
        Assert.Equal(0, ctx.ArchivedRecords.Count());
        ctx.SaveChanges();
        Assert.Equal(1, ctx.ArchivedRecords.Count());
    }

    [Fact]
    public void Arxiv_JSON_surati_asl_entityga_qaytib_deserializatsiya_bolinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var lead = SampleLead();

        ArchiveService.Snapshot(ctx, "lead", lead.Id, lead.FullName, lead.Phone, lead, null, "Admin");
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var rec = Assert.Single(ctx.ArchivedRecords);
        var restored = JsonSerializer.Deserialize<Lead>(rec.Json)!;

        Assert.Equal(lead.Id, restored.Id);
        Assert.Equal(lead.FullName, restored.FullName);
        Assert.Equal(lead.Phone, restored.Phone);
        Assert.Equal(lead.Source, restored.Source);
        Assert.Equal(lead.InterestSubject, restored.InterestSubject);
        Assert.Equal(lead.CreatedAt, restored.CreatedAt);
    }

    [Fact]
    public void Arxiv_massiv_xossali_entityni_ham_toliq_saqlaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var group = new Group
        {
            Name = "IELTS-1", MonthlyFee = 500000m, Days = new List<int> { 0, 2, 4 },
            StartTime = "09:00", EndTime = "10:30",
        };

        ArchiveService.Snapshot(ctx, "group", group.Id, group.Name, "", group, null, "Admin");
        ctx.SaveChanges();

        var restored = JsonSerializer.Deserialize<Group>(Assert.Single(ctx.ArchivedRecords).Json)!;
        Assert.Equal(new[] { 0, 2, 4 }, restored.Days);
        Assert.Equal(500000m, restored.MonthlyFee);
        Assert.Equal("09:00", restored.StartTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Arxiv_bosh_sabab_null_sifatida_saqlanadi(string? reason)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        ArchiveService.Snapshot(ctx, "lead", "id-1", "T", "S", SampleLead(), reason, "Admin");
        ctx.SaveChanges();

        Assert.Null(Assert.Single(ctx.ArchivedRecords).Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Arxiv_ochirgan_shaxs_korsatilmasa_Admin_yoziladi(string? actor)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        ArchiveService.Snapshot(ctx, "lead", "id-1", "T", "S", SampleLead(), null, actor!);
        ctx.SaveChanges();

        Assert.Equal("Admin", Assert.Single(ctx.ArchivedRecords).ActorName);
    }

    [Fact]
    public void Arxiv_sarlavha_va_ostsarlavha_null_bolsa_bosh_satr()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        ArchiveService.Snapshot(ctx, "lead", "id-1", null!, null!, SampleLead(), null, "Admin");
        ctx.SaveChanges();

        var rec = Assert.Single(ctx.ArchivedRecords);
        Assert.Equal("", rec.Title);
        Assert.Equal("", rec.Subtitle);
    }

    [Fact]
    public void Arxiv_ochirilgan_vaqti_Toshkent_ISO_formatida()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        ArchiveService.Snapshot(ctx, "lead", "id-1", "T", "S", SampleLead(), null, "Admin");
        ctx.SaveChanges();

        var deletedAt = Assert.Single(ctx.ArchivedRecords).DeletedAt;
        Assert.Equal(19, deletedAt.Length);                 // "yyyy-MM-ddTHH:mm:ss"
        Assert.Equal('T', deletedAt[10]);
        Assert.StartsWith(AppClock.Today.ToString("yyyy-MM-dd"), deletedAt);
        Assert.True(DateTime.TryParse(deletedAt, out _));
    }

    [Fact]
    public void Arxiv_bir_nechta_surat_mustaqil_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = SampleLead();
        var b = SampleLead();
        b.FullName = "Ikkinchi Lid";

        ArchiveService.Snapshot(ctx, "lead", a.Id, a.FullName, a.Phone, a, null, "Admin");
        ArchiveService.Snapshot(ctx, "lead", b.Id, b.FullName, b.Phone, b, "Dublikat", "Operator");
        ctx.SaveChanges();

        Assert.Equal(2, ctx.ArchivedRecords.Count());
        Assert.Equal(2, ctx.ArchivedRecords.Select(r => r.EntityId).Distinct().Count());
        Assert.Equal("Dublikat", ctx.ArchivedRecords.Single(r => r.EntityId == b.Id).Reason);
    }
}
