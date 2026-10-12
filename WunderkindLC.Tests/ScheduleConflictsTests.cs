using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using Slot = WunderkindLC.Application.Services.ScheduleConflicts.Slot;

namespace WunderkindLC.Tests;

/// <summary>
/// Double booking rules (CRM Phase 5, <c>ScheduleConflicts</c>, <c>.claude/rules/schedule.md</c> §8): half-open
/// time, weekdays, date ranges, archived groups, room vs teacher, the "schedule field changed" gate and the
/// superadmin-only override decision.
/// </summary>
public class ScheduleConflictsTests
{
    private static Slot S(
        string id, string room = "r1", string teacher = "t1", int[]? days = null,
        string start = "09:00", string end = "10:00", string? from = null, string? to = null, bool archived = false) =>
        new(id, "G-" + id, room, teacher, days ?? [0, 2], start, end, from, to, archived);

    [Fact]
    public void Yarim_ochiq_tegib_turgan_darslar_toqnashmaydi()
    {
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", start: "10:00", end: "11:00")));
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", start: "08:00", end: "09:00")));
        Assert.NotNull(ScheduleConflicts.Between(S("a"), S("b", start: "09:59", end: "11:00")));
    }

    [Fact]
    public void Umumiy_kun_bolmasa_toqnashmaydi()
    {
        Assert.Null(ScheduleConflicts.Between(S("a", days: [0, 2]), S("b", days: [1, 3])));
        var c = ScheduleConflicts.Between(S("a", days: [4, 0, 2]), S("b", days: [2, 4, 6]))!;
        Assert.Equal([2, 4], c.Days);
        Assert.Equal("Ch, Jum", ScheduleConflicts.FormatDays(c.Days));
    }

    [Fact]
    public void Sana_oraliqlari_kesishmasa_toqnashmaydi()
    {
        // A sentyabr–oktabr, B noyabrdan — bir xil xona/vaqt bo'lsa ham to'qnashuv emas.
        Assert.Null(ScheduleConflicts.Between(
            S("a", from: "2026-09-01", to: "2026-10-31"), S("b", from: "2026-11-01")));
        // Chegara kuni ikkalasiga ham kiradi.
        Assert.NotNull(ScheduleConflicts.Between(
            S("a", from: "2026-09-01", to: "2026-11-01"), S("b", from: "2026-11-01")));
        // Sanasiz guruh — cheksiz (−∞..+∞).
        Assert.NotNull(ScheduleConflicts.Between(S("a"), S("b", from: "2030-01-01", to: "2030-02-01")));
        // Buzuq sana — chegara yo'q deb olinadi.
        Assert.True(ScheduleConflicts.DateRangesOverlap("buzuq", null, "2026-01-01", "2026-01-02"));
    }

    [Fact]
    public void Arxivlangan_guruh_hisobga_olinmaydi()
    {
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", archived: true)));
        Assert.Null(ScheduleConflicts.Between(S("a", archived: true), S("b")));
    }

    [Fact]
    public void Xona_birinchi_keyin_oqituvchi()
    {
        Assert.Equal(ScheduleConflicts.KindRoom, ScheduleConflicts.Between(S("a"), S("b", teacher: "t2"))!.Kind);
        Assert.Equal(ScheduleConflicts.KindTeacher, ScheduleConflicts.Between(S("a"), S("b", room: "r2"))!.Kind);
        Assert.Equal(ScheduleConflicts.KindRoom, ScheduleConflicts.Between(S("a"), S("b"))!.Kind);
        // Na xona, na o'qituvchi umumiy emas — to'qnashuv yo'q; bo'sh xona/o'qituvchi "umumiy" emas.
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", room: "r2", teacher: "t2")));
        Assert.Null(ScheduleConflicts.Between(S("a", room: "", teacher: ""), S("b", room: "", teacher: "")));
    }

    [Fact]
    public void Vaqti_buzuq_guruh_tekshirilmaydi_va_ozi_bilan_toqnashmaydi()
    {
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", start: "", end: "")));
        Assert.Null(ScheduleConflicts.Between(S("a"), S("b", start: "13:30", end: "03:00")));
        Assert.Null(ScheduleConflicts.Between(S("a"), S("a")));
    }

    [Fact]
    public void Barcha_juftliklar_bir_marta_va_kesishgan_oraliq_bilan()
    {
        var pairs = ScheduleConflicts.AllPairs([
            S("a", start: "14:00", end: "15:30"),
            S("b", start: "15:00", end: "16:00"),
            S("c", room: "r9", teacher: "t9"),
            S("d", archived: true),
        ]);
        var p = Assert.Single(pairs);
        Assert.Equal(("a", "b"), (p.GroupAId, p.GroupBId));
        Assert.Equal("15:00–15:30", p.Slot);
    }

    [Fact]
    public void Faqat_jadval_maydoni_ozgarsa_tekshiriladi()
    {
        var before = S("a", from: "2026-09-01");
        Assert.False(ScheduleConflicts.ScheduleChanged(before, before with { GroupName = "Yangi nom" }));
        Assert.False(ScheduleConflicts.ScheduleChanged(before, before with { Days = [2, 0, 0] }));   // tartib/takror
        Assert.False(ScheduleConflicts.ScheduleChanged(before, before with { StartDate = "2026-09-01T00:00:00" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { RoomId = "r2" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { TeacherId = "t2" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { Days = [0] }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { StartTime = "09:30" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { EndTime = "10:30" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { StartDate = "2026-10-01" }));
        Assert.True(ScheduleConflicts.ScheduleChanged(before, before with { EndDate = "2026-12-31" }));
    }

    [Fact]
    public void Qaror_faqat_superadmin_sabab_bilan_baribir_saqlaydi()
    {
        Assert.Equal(ScheduleConflicts.Verdict.Ok, ScheduleConflicts.Decide(0, false, null, false).Verdict);

        // Admin/xodim: force ham YORDAM BERMAYDI — 409, "baribir saqlash" taklif qilinmaydi.
        var admin = ScheduleConflicts.Decide(2, force: true, "kerak", isSuperAdmin: false);
        Assert.Equal(ScheduleConflicts.Verdict.Blocked, admin.Verdict);
        Assert.False(admin.CanForce);
        Assert.Equal(ScheduleConflicts.BlockedMessage, admin.Message);

        // Superadmin, force'siz — 409, lekin canForce.
        var plain = ScheduleConflicts.Decide(1, false, null, true);
        Assert.Equal(ScheduleConflicts.Verdict.Blocked, plain.Verdict);
        Assert.True(plain.CanForce);

        // Superadmin, sababsiz — 409 + sabab so'raladi.
        var noReason = ScheduleConflicts.Decide(1, true, "   ", true);
        Assert.Equal(ScheduleConflicts.Verdict.Blocked, noReason.Verdict);
        Assert.Equal(ScheduleConflicts.ReasonRequiredMessage, noReason.Message);

        // Superadmin + sabab — saqlanadi (va audit matni sababni o'z ichiga oladi).
        var forced = ScheduleConflicts.Decide(1, true, "  Vaqtincha ikki guruh  ", true);
        Assert.Equal(ScheduleConflicts.Verdict.Forced, forced.Verdict);
        Assert.Equal("Vaqtincha ikki guruh", forced.Reason);
        var text = ScheduleConflicts.ForcedAuditText([ScheduleConflicts.Between(S("a"), S("b"))!], forced.Reason);
        Assert.Contains("Jadval to'qnashuvi bilan saqlandi", text);
        Assert.Contains("«G-b» (xona, Du, Ch 09:00–10:00)", text);
        Assert.Contains("sabab: Vaqtincha ikki guruh", text);
    }

    [Fact]
    public void Bir_kunlik_dars_bandligi_xona_va_dars_otadigan_oqituvchi_boyicha()
    {
        var lessons = new[]
        {
            new ScheduleConflicts.DayLesson("g2", "Ikkinchi", "r1", "t2", "14:00", "15:30"),
            new ScheduleConflicts.DayLesson("g3", "Uchinchi", "r3", "t1", "16:00", "17:00"),
            new ScheduleConflicts.DayLesson("g1", "O'zi", "r1", "t1", "14:00", "15:30"),
        };
        Assert.Equal("g2", ScheduleConflicts.LessonClash("g1", "r1", "t1", "15:00", "16:00", lessons)!.GroupId);
        Assert.Equal("g3", ScheduleConflicts.LessonClash("g1", "r9", "t1", "16:30", "17:30", lessons)!.GroupId);
        Assert.Null(ScheduleConflicts.LessonClash("g1", "r1", "t1", "15:30", "16:00", lessons));   // tegib turadi
        var msg = ScheduleConflicts.LessonClashMessage(lessons[0]);
        Assert.Equal("Bu vaqtda xona/o'qituvchi band: Ikkinchi 14:00–15:30", msg);
    }

    // ------------------------------- IO wrapper (RoomConflictService) -------------------------------

    [Fact]
    public async Task Servis_sana_oraligini_hisobga_oladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Rooms.Add(new Room { Id = "r1", Name = "Xona 1" });
        db.Context.Classes.Add(new Group
        {
            Name = "Kuzgi", RoomId = "r1", TeacherId = "t1", Days = [0, 2], StartTime = "09:00", EndTime = "10:00",
            StartDate = "2026-09-01", EndDate = "2026-10-31",
        });
        await db.Context.SaveChangesAsync();
        var svc = new RoomConflictService(db.Context);

        Assert.Empty(await svc.CheckRoomConflictAsync("r1", null, [0], "09:00", "10:00", startDate: "2026-11-01"));
        Assert.Single(await svc.CheckRoomConflictAsync("r1", null, [0], "09:00", "10:00", startDate: "2026-10-15"));
        Assert.Empty(await svc.AllPairsAsync());   // bitta guruh — juftlik yo'q
    }
}
