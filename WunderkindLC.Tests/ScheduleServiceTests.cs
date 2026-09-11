using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// DARS JADVALI servisi — foydalanuvchining asosiy talabi:
/// "xonada 4-soatda dars bor, 5-soat bo'sh, 6-soatda yana dars — o'sha bo'sh oraliqqa
/// tushadigan BOSHQA o'qituvchini TAVSIYA qilsin".
/// </summary>
public class ScheduleServiceTests
{
    private static Room AddRoom(AppDbContext db, string name)
    {
        var r = new Room { Name = name };
        db.Rooms.Add(r);
        return r;
    }

    private static Teacher AddTeacher(AppDbContext db, string name)
    {
        var t = new Teacher { FullName = name };
        db.Teachers.Add(t);
        return t;
    }

    private static Group AddGroup(
        AppDbContext db, string name, Teacher? teacher, Room? room,
        List<int> days, string start, string end, bool archived = false)
    {
        var g = new Group
        {
            Name = name,
            TeacherId = teacher?.Id ?? "",
            RoomId = room?.Id,
            Days = days,
            StartTime = start,
            EndTime = end,
            IsArchived = archived,
        };
        db.Classes.Add(g);
        return g;
    }

    /// <summary>Talabdagi holat: xonada 09:00–10:30 va 12:00–13:30 dars, orasi bo'sh.</summary>
    private static (TestDb Db, ScheduleService Svc, Room Room, Teacher Busy, Teacher Free, Teacher Idle) Setup()
    {
        var db = TestDb.Sqlite();
        var ctx = db.Context;

        var room = AddRoom(ctx, "101-xona");
        var other = AddRoom(ctx, "102-xona");

        var busy = AddTeacher(ctx, "Aliyev");   // teshik paytida BOSHQA xonada darsi bor
        var free = AddTeacher(ctx, "Valiyev");  // teshikning oldingi darsini o'zi o'qiydi
        var idle = AddTeacher(ctx, "G'aniyev"); // jadvalda umuman darsi yo'q

        // Dushanba (0): 101-xonada ikki dars, orada 10:30–12:00 bo'sh.
        AddGroup(ctx, "A guruh", free, room, [0], "09:00", "10:30");
        AddGroup(ctx, "B guruh", free, room, [0], "12:00", "13:30");
        // Aliyev aynan o'sha paytda boshqa xonada band.
        AddGroup(ctx, "C guruh", busy, other, [0], "11:00", "12:00");

        ctx.SaveChanges();
        return (db, new ScheduleService(ctx), room, busy, free, idle);
    }

    [Fact]
    public async Task Teshik_IkkiDarsOrasida_Topiladi()
    {
        var (db, svc, room, _, _, _) = Setup();
        using var _d = db;

        var gaps = await svc.GetGapsAsync("room", room.Id, null);

        var gap = Assert.Single(gaps);
        Assert.Equal("room", gap.Scope);
        Assert.Equal(0, gap.Day);
        Assert.Equal("10:30", gap.Start);
        Assert.Equal("12:00", gap.End);
        Assert.Equal(90, gap.Minutes);
        // Teshikning ikki tomonidagi darslar ko'rsatiladi — "qayerda" savoli javobsiz qolmasin.
        Assert.Equal("A guruh", gap.BeforeGroup);
        Assert.Equal("B guruh", gap.AfterGroup);
    }

    [Fact]
    public async Task Tavsiyada_BANDoqituvchi_YOQ()
    {
        var (db, svc, room, busy, _, _) = Setup();
        using var _d = db;

        var gap = Assert.Single(await svc.GetGapsAsync("room", room.Id, null));

        // Aliyev 11:00–12:00 da boshqa xonada dars o'tyapti — u tavsiya etilmasligi SHART.
        Assert.DoesNotContain(gap.Suggestions, s => s.Id == busy.Id);
    }

    [Fact]
    public async Task Tavsiyada_YonmaYonDarsiBorlar_TEPADA()
    {
        var (db, svc, room, _, free, idle) = Setup();
        using var _d = db;

        var gap = Assert.Single(await svc.GetGapsAsync("room", room.Id, null));

        // Valiyev teshikning ikkala tomonidagi darsni o'zi o'qiydi — u markazda baribir turibdi,
        // ya'ni teshikni to'ldirsa kuni uzluksiz bo'ladi. Shuning uchun BIRINCHI.
        Assert.Equal(free.Id, gap.Suggestions[0].Id);
        Assert.Contains(gap.Suggestions, s => s.Id == idle.Id);
        Assert.True(
            gap.Suggestions.FindIndex(s => s.Id == free.Id)
            < gap.Suggestions.FindIndex(s => s.Id == idle.Id));
    }

    [Fact]
    public async Task QisqaOraliq_Chegaradan_Otmaydi()
    {
        var (db, svc, room, _, _, _) = Setup();
        using var _d = db;

        // 90 daqiqalik teshik: 60 va 90 chegarada ko'rinadi, 120 da — yo'q.
        Assert.Single(await svc.GetGapsAsync("room", room.Id, 90));
        Assert.Empty(await svc.GetGapsAsync("room", room.Id, 120));
    }

    [Fact]
    public async Task OQITUVCHI_kesimida_BoshXonaTavsiyaEtiladi()
    {
        var (db, svc, _, _, free, _) = Setup();
        using var _d = db;

        // Valiyevning O'ZIDA ham 10:30–12:00 teshik bor (ikkala darsi ham uniki).
        var gap = Assert.Single(await svc.GetGapsAsync("teacher", free.Id, null));
        Assert.Equal("teacher", gap.Scope);
        Assert.Equal("10:30", gap.Start);
        // O'qituvchi teshigida tavsiya — BO'SH XONA (o'qituvchi allaqachon ma'lum).
        Assert.All(gap.Suggestions, s => Assert.Equal("room", s.Kind));
        Assert.NotEmpty(gap.Suggestions);
    }

    [Fact]
    public async Task ARXIV_guruh_jadvalga_KIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = AddRoom(ctx, "101");
        var t = AddTeacher(ctx, "O'qituvchi");
        AddGroup(ctx, "A", t, room, [0], "09:00", "10:30");
        AddGroup(ctx, "B", t, room, [0], "12:00", "13:30");
        // Arxivdagi guruh teshikni "to'ldirib" turgandek ko'rinmasligi kerak.
        AddGroup(ctx, "Arxiv", t, room, [0], "10:30", "12:00", archived: true);
        ctx.SaveChanges();

        var gaps = await new ScheduleService(ctx).GetGapsAsync("room", room.Id, null);
        Assert.Single(gaps);
    }

    [Fact]
    public async Task BUZUQ_vaqt_yiqitmaydi_va_SANALADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = AddRoom(ctx, "101");
        var t = AddTeacher(ctx, "O'qituvchi");
        AddGroup(ctx, "Yaxshi", t, room, [0], "09:00", "10:30");
        // Bazadagi HAQIQIY buzuq qatorlar.
        AddGroup(ctx, "Teskari", t, room, [0], "13:30", "03:00");
        AddGroup(ctx, "Vaqtsiz", t, room, [0], "21:29", "");
        AddGroup(ctx, "Kunsiz", t, room, [], "09:00", "10:30");
        ctx.SaveChanges();

        var board = await new ScheduleService(ctx).GetBoardAsync();

        // Faqat to'g'ri qator jadvalga tushadi...
        Assert.Single(board.Lessons);
        // ...qolgani JIMGINA yo'qolmaydi — soni qaytadi (ekranda ogohlantirish chiqadi).
        Assert.Equal(3, board.SkippedGroups);
    }

    [Fact]
    public async Task Board_TIGIZLIKni_hisoblaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = AddRoom(ctx, "101");
        var other = AddRoom(ctx, "102");
        var t1 = AddTeacher(ctx, "A");
        var t2 = AddTeacher(ctx, "B");
        AddGroup(ctx, "G1", t1, room, [0], "09:00", "10:00");
        AddGroup(ctx, "G2", t2, other, [0], "09:00", "10:00");
        ctx.SaveChanges();

        var board = await new ScheduleService(ctx).GetBoardAsync();

        var bucket = Assert.Single(board.Peak, p => p.Day == 0 && p.Bucket == "09:00");
        Assert.Equal(2, bucket.Count);   // ikkala guruh birga ketyapti
        Assert.Equal(2, board.Rooms.Count);
        Assert.Equal(2, board.Teachers.Count);
    }

    [Fact]
    public async Task BitalikDarsda_teshik_YOQ()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var room = AddRoom(ctx, "101");
        var t = AddTeacher(ctx, "O'qituvchi");
        // Kun boshidagi va oxiridagi bo'shliq "teshik" emas.
        AddGroup(ctx, "Yolg'iz", t, room, [0], "09:00", "10:30");
        ctx.SaveChanges();

        Assert.Empty(await new ScheduleService(ctx).GetGapsAsync("room", room.Id, null));
    }
}
