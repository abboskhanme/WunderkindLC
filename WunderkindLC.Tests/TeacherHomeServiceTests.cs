using System.Text.RegularExpressions;
using Group = WunderkindLC.Domain.Group;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QITUVCHI PORTALI — bosh sahifa, dars jadvali va "O'quvchilarim"
/// (<see cref="TeacherHomeService"/>, <c>GET api/teacher/overview | schedule | students</c>).
///
/// <para>Uch narsa qulflanadi: (1) dars rejasi (kunlar, ko'chirish, o'rinbosarlik) — sof funksiya;
/// (2) o'qituvchi FAQAT o'z guruhini ko'radi va to'lov darvozasi yashirgan o'quvchi hech qayerda
/// chiqmaydi; (3) endpointlarning ruxsat darvozasi (manba matnidan — test loyihasi Server'ga
/// havola qilmaydi, <see cref="HomeEndpointsRbacTests"/> bilan bir xil sabab).</para>
/// </summary>
public class TeacherHomeServiceTests
{
    // 2026-09-07 — dushanba. Sof testlar AppClock ga bog'liq emas.
    private static readonly DateOnly Mon = new(2026, 9, 7);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static Group G(string id = "g1", string teacher = "t1", string start = "14:00", string end = "15:30",
        string? startDate = null, params int[] days) => new()
    {
        Id = id, Name = id.ToUpperInvariant(), TeacherId = teacher, CourseId = "c1",
        Days = days.ToList(), StartTime = start, EndTime = end, StartDate = startDate,
    };

    private static TeacherHomeService.PlanSource Own(Group g,
        IReadOnlyList<LessonReschedule>? moves = null, IReadOnlyList<SubstituteTeacherAssignment>? subs = null) =>
        new(g, true, moves ?? new List<LessonReschedule>(), subs ?? new List<SubstituteTeacherAssignment>());

    /* =========================================================================================
     *  1) DARS REJASI — sof funksiya
     * ========================================================================================= */

    [Fact]
    public void Oz_guruhi_hafta_kunlari_boyicha_darslar_chiqadi()
    {
        // Du (0) va Cho (2)
        var plan = TeacherHomeService.PlanLessons("t1", new[] { Own(G(days: new[] { 0, 2 })) }, Mon, Mon.AddDays(6));

        Assert.Equal(new[] { D(Mon), D(Mon.AddDays(2)) }, plan.Select(p => p.Date));
        Assert.All(plan, p =>
        {
            Assert.Equal("14:00", p.StartTime);
            Assert.Equal("15:30", p.EndTime);
            Assert.False(p.IsSubstitute);
            Assert.False(p.Moved);
        });
    }

    /// <summary>Maosh hisobida kunsiz guruh "oyning har kuni" deb olinadi — jadvalda esa bu 30 ta
    /// soxta dars bo'lardi. Jadval kunsiz guruhni UMUMAN chizmaydi.</summary>
    [Fact]
    public void Dars_kunlari_belgilanmagan_guruh_jadvalga_tushmaydi()
        => Assert.Empty(TeacherHomeService.PlanLessons("t1", new[] { Own(G()) }, Mon, Mon.AddDays(30)));

    [Fact]
    public void Guruh_boshlanishidan_oldingi_kunlar_dars_emas()
    {
        var plan = TeacherHomeService.PlanLessons(
            "t1", new[] { Own(G(startDate: D(Mon.AddDays(1)), days: new[] { 0, 2 })) }, Mon, Mon.AddDays(6));
        Assert.Equal(new[] { D(Mon.AddDays(2)) }, plan.Select(p => p.Date));
    }

    /// <summary>Ko'chirilgan dars: asl kun yo'qoladi, yangi kun paydo bo'ladi va yangi vaqtdan
    /// ASL davomiylik bilan davom etadi.</summary>
    [Fact]
    public void Kochirilgan_dars_yangi_kunda_yangi_vaqt_bilan_chiqadi()
    {
        var move = new LessonReschedule { ClassId = "g1", FromDate = D(Mon), ToDate = D(Mon.AddDays(1)), Time = "16:00" };
        var plan = TeacherHomeService.PlanLessons(
            "t1", new[] { Own(G(days: new[] { 0 }), moves: new[] { move }) }, Mon, Mon.AddDays(6));

        var lesson = Assert.Single(plan);
        Assert.Equal(D(Mon.AddDays(1)), lesson.Date);
        Assert.True(lesson.Moved);
        Assert.Equal("16:00", lesson.StartTime);
        Assert.Equal("17:30", lesson.EndTime);
    }

    /// <summary>O'rinbosar FAQAT tayinlovning o'z kunlarida (tanlangan sanalar) dars o'tadi —
    /// guruhning boshqa kunlari uning jadvalida yo'q.</summary>
    [Fact]
    public void Orinbosar_faqat_tanlangan_kunlarda_dars_otadi()
    {
        var g = G("g2", teacher: "t9", days: new[] { 0, 2, 4 });
        var a = new SubstituteTeacherAssignment
        {
            GroupId = "g2", OriginalTeacherId = "t9", SubstituteTeacherId = "t1",
            Date = D(Mon), EndDate = D(Mon.AddDays(6)), SelectedDates = new() { D(Mon.AddDays(2)) },
        };
        var plan = TeacherHomeService.PlanLessons(
            "t1", new[] { new TeacherHomeService.PlanSource(g, false, new List<LessonReschedule>(), new[] { a }) },
            Mon, Mon.AddDays(6));

        var lesson = Assert.Single(plan);
        Assert.Equal(D(Mon.AddDays(2)), lesson.Date);
        Assert.True(lesson.IsSubstitute);
    }

    /// <summary>O'z guruhida boshqa odam o'rinbosar bo'lgan kun jadvaldan YO'QOLMAYDI — dars
    /// belgilangan holda qoladi ("bugun darsim yo'q, nega?" savoli bo'lmasin).</summary>
    [Fact]
    public void Oz_guruhida_orinbosar_bolgan_kun_belgilanadi()
    {
        var a = new SubstituteTeacherAssignment
        {
            GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t5", Date = D(Mon),
        };
        var plan = TeacherHomeService.PlanLessons(
            "t1", new[] { Own(G(days: new[] { 0, 2 }), subs: new[] { a }) }, Mon, Mon.AddDays(6));

        Assert.Equal(2, plan.Count);
        Assert.Equal("t5", plan[0].SubstituteTeacherId);
        Assert.Equal("", plan[1].SubstituteTeacherId);
    }

    [Fact]
    public void Bekor_qilingan_orinbosarlik_hisobga_olinmaydi()
    {
        var a = new SubstituteTeacherAssignment
        {
            GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t5", Date = D(Mon), IsActive = false,
        };
        var plan = TeacherHomeService.PlanLessons(
            "t1", new[] { Own(G(days: new[] { 0 }), subs: new[] { a }) }, Mon, Mon);
        Assert.Equal("", Assert.Single(plan).SubstituteTeacherId);
    }

    [Fact]
    public void Darslar_sana_va_vaqt_boyicha_tartiblanadi()
    {
        var late = G("late", start: "18:00", end: "19:00", days: new[] { 0 });
        var early = G("early", start: "09:00", end: "10:00", days: new[] { 0 });
        var plan = TeacherHomeService.PlanLessons("t1", new[] { Own(late), Own(early) }, Mon, Mon);
        Assert.Equal(new[] { "early", "late" }, plan.Select(p => p.GroupId));
    }

    [Theory]
    [InlineData("14:00", "15:30", null, "14:00", "15:30")]
    [InlineData("14:00", "15:30", "16:00", "16:00", "17:30")]
    [InlineData("14:00", "15:30", "xx", "14:00", "15:30")]   // buzuq yangi vaqt — asl vaqt
    [InlineData("", "", "10:00", "10:00", "")]               // davomiylik noma'lum
    [InlineData("25:00", "", null, "", "")]                  // buzuq asl vaqt — bo'sh, jadval buzilmaydi
    public void Dars_vaqti(string start, string end, string? moved, string expStart, string expEnd)
        => Assert.Equal((expStart, expEnd), TeacherHomeService.LessonTime(start, end, moved));

    [Fact]
    public void Davomat_foizi_kech_kelgan_KELGAN_hisoblanadi()
    {
        StudentJournalBuilder.Cell C(bool conducted, bool present = false, string? reason = null, bool late = false,
            bool blocked = false, bool unknown = false) =>
            new("2026-09-01", 1, conducted, blocked, present, unknown, null, reason, reason, late, 0, 0, null, "", "");

        var cells = new[]
        {
            C(true, present: true),
            C(true, reason: "Kech", late: true),
            C(true, reason: "Kasal"),
            C(true, present: true, blocked: true),   // a'zolikdan tashqarida — sanalmaydi
            C(true, unknown: true),                   // noma'lum — sanalmaydi
            C(false),                                 // o'tilmagan — sanalmaydi
        };
        var (held, attended, absent, late, pct) = TeacherHomeService.Attendance(cells);

        Assert.Equal(3, held);
        Assert.Equal(2, attended);
        Assert.Equal(1, absent);
        Assert.Equal(1, late);
        Assert.Equal(67, pct);
    }

    [Fact]
    public void Dars_otilmagan_oyda_foiz_yoq()
        => Assert.Null(TeacherHomeService.Attendance(Array.Empty<StudentJournalBuilder.Cell>()).Pct);

    /* =========================================================================================
     *  2) BAZA — ko'rinish doirasi va to'lov darvozasi
     * ========================================================================================= */

    private sealed record Seed(Teacher Me, Group Mine, Group Other, Student A, Student B, Student Frozen);

    private static Seed SeedDb(TestDb db, int[]? days = null)
    {
        var course = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(course);
        var me = new Teacher { FullName = "Ali Valiyev", Permissions = TeacherPermissions.All.ToList() };
        var other = new Teacher { FullName = "Boshqa O'qituvchi" };
        db.Context.Teachers.AddRange(me, other);

        var room = new Room { Name = "101-xona" };
        db.Context.Rooms.Add(room);
        var allDays = days ?? new[] { 0, 1, 2, 3, 4, 5, 6 };
        var mine = new Group
        {
            Name = "A1", CourseId = course.Id, TeacherId = me.Id, RoomId = room.Id,
            Days = allDays.ToList(), StartTime = "14:00", EndTime = "15:30",
        };
        var foreign = new Group { Name = "B2", CourseId = course.Id, TeacherId = other.Id, Days = allDays.ToList() };
        var archived = new Group { Name = "Eski", CourseId = course.Id, TeacherId = me.Id, IsArchived = true, Days = allDays.ToList() };
        db.Context.Classes.AddRange(mine, foreign, archived);

        var a = new Student { FullName = "Aziza" };
        var b = new Student { FullName = "Bobur" };
        var f = new Student { FullName = "Farhod" };
        var x = new Student { FullName = "Begona" };
        db.Context.Students.AddRange(a, b, f, x);
        db.Context.StudentGroups.AddRange(
            new StudentGroup { StudentId = a.Id, GroupId = mine.Id, Status = "active" },
            new StudentGroup { StudentId = b.Id, GroupId = mine.Id, Status = "trial" },
            new StudentGroup { StudentId = f.Id, GroupId = mine.Id, Status = "frozen" },
            new StudentGroup { StudentId = x.Id, GroupId = foreign.Id, Status = "active" },
            new StudentGroup { StudentId = a.Id, GroupId = archived.Id, Status = "active" });
        db.Context.SaveChanges();
        return new Seed(me, mine, foreign, a, b, f);
    }

    [Fact]
    public async Task Overview_faqat_oz_faol_guruhini_korsatadi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var dto = await TeacherHomeService.OverviewAsync(db.Context, s.Me.Id, AppClock.Today, canSchedule: true);

        var card = Assert.Single(dto.Groups);                    // begona va arxivlangan guruh yo'q
        Assert.Equal(s.Mine.Id, card.Id);
        Assert.Equal("101-xona", card.Room);                     // xona katalogdan
        Assert.Equal((1, 1, 1), (card.Active, card.Trial, card.Frozen));
        Assert.Equal(2, dto.StudentCount);                       // muzlatilgan kirmaydi
        var lesson = Assert.Single(dto.TodayLessons);            // har kuni dars — bugun ham
        Assert.Equal(2, lesson.StudentCount);
        Assert.False(lesson.Conducted);
    }

    [Fact]
    public async Task Overview_jadval_ruxsatisiz_bugungi_darslarni_bermaydi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var dto = await TeacherHomeService.OverviewAsync(db.Context, s.Me.Id, AppClock.Today, canSchedule: false);

        Assert.False(dto.CanSchedule);
        Assert.Empty(dto.TodayLessons);
        Assert.All(dto.Groups, g => Assert.Equal("", g.NextLessonDate));
        Assert.Single(dto.Groups);                               // guruhlar baribir ko'rinadi
    }

    [Fact]
    public async Task Schedule_otilgan_darsni_belgilaydi_faqat_guruh_kursi_boyicha()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var today = D(AppClock.Today);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = s.Mine.Id, SubjectId = s.Mine.CourseId, Quarter = 1, Date = today, Period = 1, Conducted = true,
        });
        db.Context.SaveChanges();

        var lessons = await TeacherHomeService.ScheduleAsync(db.Context, s.Me.Id, AppClock.Today, AppClock.Today);

        Assert.True(Assert.Single(lessons).Conducted);
    }

    [Fact]
    public async Task Schedule_begona_guruh_darsini_bermaydi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var lessons = await TeacherHomeService.ScheduleAsync(db.Context, s.Me.Id, AppClock.Today, AppClock.Today.AddDays(6));
        Assert.All(lessons, l => Assert.Equal(s.Mine.Id, l.GroupId));
        Assert.Equal(7, lessons.Count);
    }

    [Fact]
    public async Task Students_oz_guruhi_oquvchilari_va_davomati()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var dto = await TeacherHomeService.StudentsAsync(db.Context, s.Me.Id, null);

        Assert.Equal(TuitionService.CurrentMonth(), dto.Month);
        Assert.Equal(new[] { "Aziza", "Bobur", "Farhod" }, dto.Rows.Select(r => r.FullName));
        Assert.DoesNotContain(dto.Rows, r => r.FullName == "Begona");
        Assert.All(dto.Rows, r => Assert.Equal(s.Mine.Id, r.GroupId));   // arxivlangan guruh a'zoligi yo'q
    }

    [Fact]
    public async Task Students_kelajak_yoki_buzuq_oy_joriy_oyga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        var cur = TuitionService.CurrentMonth();

        Assert.Equal(cur, (await TeacherHomeService.StudentsAsync(db.Context, s.Me.Id, "2999-01")).Month);
        Assert.Equal(cur, (await TeacherHomeService.StudentsAsync(db.Context, s.Me.Id, "2026-13")).Month);
        Assert.Equal(cur, (await TeacherHomeService.StudentsAsync(db.Context, s.Me.Id, "abc")).Month);
    }

    /// <summary>To'lov darvozasi (jurnal.md): o'qituvchi jurnalida ko'rinmaydigan o'quvchi ro'yxatda
    /// ham, sanoqlarda ham, bugungi dars sonida ham YO'Q.</summary>
    [Fact]
    public async Task Tolov_darvozasi_yashirgan_oquvchi_hech_qayerda_chiqmaydi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        db.Context.CenterMeta.Add(new CenterMeta { JournalHideUnpaidPrevMonth = true });
        var prevMonth = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        db.Context.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.B.Id, GroupId = s.Mine.Id, Month = prevMonth, Amount = 500_000m, Date = prevMonth + "-01",
        });
        db.Context.SaveChanges();

        var students = await TeacherHomeService.StudentsAsync(db.Context, s.Me.Id, null);
        var overview = await TeacherHomeService.OverviewAsync(db.Context, s.Me.Id, AppClock.Today, canSchedule: true);

        Assert.DoesNotContain(students.Rows, r => r.StudentId == s.B.Id);
        Assert.Equal(0, Assert.Single(overview.Groups).Trial);
        Assert.Equal(1, overview.StudentCount);
        Assert.Equal(1, Assert.Single(overview.TodayLessons).StudentCount);
    }

    /* =========================================================================================
     *  3) RUXSAT darvozasi — manba matnidan
     * ========================================================================================= */

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        return File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", file));
    }

    private static string Body(string src, string signature)
    {
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"Endpoint topilmadi: {signature}");
        var next = src.IndexOf("[Http", i, StringComparison.Ordinal);
        return next > 0 ? src[i..next] : src[i..];
    }

    [Fact]
    public void Portal_faqat_teacher_roli_va_anonim_kirish_yoq()
    {
        var main = Source("TeacherPortalController.cs");
        var home = Source("TeacherPortalController.Home.cs");

        Assert.Matches(new Regex(@"\[Authorize\(Roles\s*=\s*""teacher""\)\]\s*\[Route\(""api/teacher""\)\]\s*public partial class TeacherPortalController"), main);
        Assert.DoesNotContain("AllowAnonymous", home);
        Assert.Contains("public partial class TeacherPortalController", home);
    }

    [Fact]
    public void Jadval_Dars_jadvali_ruxsati_bilan_darvozalangan()
    {
        var body = Body(Source("TeacherPortalController.Home.cs"), "Schedule([FromQuery]");
        Assert.Contains("TeacherPermissions.Schedule", body);
        Assert.Contains("return Forbid()", body);
        Assert.Contains("MaxScheduleDays", body);
    }

    [Fact]
    public void Oquvchilar_Jurnal_ruxsati_bilan_darvozalangan()
    {
        var body = Body(Source("TeacherPortalController.Home.cs"), "Students([FromQuery]");
        Assert.Contains("TeacherPermissions.Journal", body);
        Assert.Contains("return Forbid()", body);
    }

    /// <summary>Hamma endpoint o'qituvchini TOKENDAN oladi — id parametr bilan begona o'qituvchi ochilmasin.</summary>
    [Fact]
    public void Barcha_endpointlar_oqituvchini_tokendan_oladi()
    {
        var home = Source("TeacherPortalController.Home.cs");
        // overview · schedule · students · debt-summary (2026-10-07) — hammasi o'qituvchini TOKENDAN oladi.
        Assert.Equal(4, Regex.Matches(home, @"var t = await Me\(\);").Count);
        Assert.DoesNotContain("teacherId", home);
    }
}
