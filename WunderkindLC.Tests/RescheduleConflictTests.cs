using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Moving a lesson into an occupied room/teacher slot is refused with the conflicting group named (CRM Phase 5,
/// <c>RescheduleConflicts</c> → <c>JournalService.RescheduleLessonAsync</c>). The comparison uses that day's REAL
/// lessons (moves, substitutes) — <c>AttendanceWatch.LessonsOn</c>.
/// </summary>
public class RescheduleConflictTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");
    private static int Wd(int offset) => ((int)AppClock.Today.AddDays(offset).DayOfWeek + 6) % 7;

    private static Group Add(TestDb db, string name, int dayOffset, string room, string teacher,
        string start = "14:00", string end = "15:30")
    {
        // Group.RoomId — haqiqiy FK (RoomsArchiveTests.SeedRefs).
        if (db.Context.Rooms.Find(room) is null) db.Context.Rooms.Add(new Room { Id = room, Name = "Xona " + room });
        var g = new Group
        {
            Name = name, RoomId = room, TeacherId = teacher, Days = [Wd(dayOffset)],
            StartTime = start, EndTime = end, StartDate = Day(-60),
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    [Fact]
    public async Task Band_xonaga_kochirish_rad_etiladi_va_guruh_nomi_aytiladi()
    {
        using var db = TestDb.Sqlite();
        var g1 = Add(db, "Birinchi", 7, "r1", "t1");
        Add(db, "Ikkinchi", 8, "r1", "t2");

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), null, "admin", isAdmin: true));
        Assert.Equal("Bu vaqtda xona/o'qituvchi band: Ikkinchi 14:00–15:30", ex.Message);
        Assert.Empty(db.Context.LessonReschedules);

        // Boshqa vaqtga (davomiylik saqlanadi: 16:00–17:30) — bo'sh.
        await JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), "16:00", "admin", isAdmin: true);
        Assert.Single(db.Context.LessonReschedules);
    }

    [Fact]
    public async Task Band_oqituvchiga_kochirish_rad_etiladi_oqituvchi_yolida_ham()
    {
        using var db = TestDb.Sqlite();
        var g1 = Add(db, "Birinchi", 7, "r1", "t1");
        Add(db, "Boshqa xonada", 8, "r2", "t1", "14:30", "16:00");

        // O'qituvchi yo'li (isAdmin: false) ham xuddi shunday — InvalidOperationException (400) bo'lib chiqadi.
        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), null, "t", isAdmin: false));
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Contains("Boshqa xonada 14:30–16:00", ex.Message);
    }

    [Fact]
    public async Task Orinbosar_dars_otadigan_kun_uning_bandligi_hisoblanadi_egasi_esa_bosh()
    {
        using var db = TestDb.Sqlite();
        var g1 = Add(db, "Birinchi", 7, "r1", "t1");
        // t9 ning guruhi Day(8) da — darsni t1 O'RINBOSAR bo'lib o'tadi → t1 band.
        var sub = Add(db, "O'rinbosarli", 8, "r5", "t9");
        // t1 ning O'Z guruhi Day(8) da — lekin o'sha kuni t7 o'rinbosar → t1 shu dars uchun BO'SH.
        var own = Add(db, "Egasi bo'sh", 8, "r6", "t1", "16:00", "17:00");
        db.Context.SubstituteTeacherAssignments.AddRange(
            new SubstituteTeacherAssignment
            {
                GroupId = sub.Id, OriginalTeacherId = "t9", SubstituteTeacherId = "t1",
                Date = Day(8), SelectedDates = [Day(8)], IsActive = true,
            },
            new SubstituteTeacherAssignment
            {
                GroupId = own.Id, OriginalTeacherId = "t1", SubstituteTeacherId = "t7",
                Date = Day(8), SelectedDates = [Day(8)], IsActive = true,
            });
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), null, "admin", isAdmin: true));
        Assert.Contains("O'rinbosarli", ex.Message);

        await JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), "16:00", "admin", isAdmin: true);
        Assert.Single(db.Context.LessonReschedules);
    }

    [Fact]
    public async Task Boshqa_kunga_kochirilgan_dars_joyni_boshatadi()
    {
        using var db = TestDb.Sqlite();
        var g1 = Add(db, "Birinchi", 7, "r1", "t1");
        var g2 = Add(db, "Ikkinchi", 8, "r1", "t2");
        db.Context.LessonReschedules.Add(new LessonReschedule { ClassId = g2.Id, FromDate = Day(8), ToDate = Day(9) });
        await db.Context.SaveChangesAsync();

        await JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), null, "admin", isAdmin: true);
        Assert.Equal(2, await db.Context.LessonReschedules.CountAsync());
    }

    [Fact]
    public async Task Majburiy_kochirish_saqlanadi_va_tarixga_sabab_bilan_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var g1 = Add(db, "Birinchi", 7, "r1", "t1");
        Add(db, "Ikkinchi", 8, "r1", "t2");
        var audit = new AuditService(db.Context, new NoHttpContext());

        await JournalService.RescheduleLessonAsync(db.Context, g1.Id, Day(7), Day(8), null, "admin", isAdmin: true,
            audit, forceConflict: true, forceReason: "Imtihon haftasi");

        Assert.Single(db.Context.LessonReschedules);
        var row = await db.Context.AuditLogs.SingleAsync(a => a.Summary.Contains("to'qnashuvi"));
        Assert.Contains("«Ikkinchi»", row.Summary);
        Assert.Contains("sabab: Imtihon haftasi", row.Summary);
    }
}
