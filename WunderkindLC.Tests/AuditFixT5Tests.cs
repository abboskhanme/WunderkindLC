using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 AUDITI — JURNAL / DAVOMAT / BAHOLASH / TESTLAR tuzatishlari (T5):
/// <list type="bullet">
///   <item>C8 — "kech" ota-onaga "darsga kelmadi" bo'lib ketmaydi, sabab almashsa qayta xabar yo'q,
///     ommaviy davomat faqat YANGI kelmaganlarga xabar beradi;</item>
///   <item>LRN-3 — jurnalga faqat guruh a'zosiga (o'qituvchi — faqat faol, muzlatilmagan a'zoga);</item>
///   <item>LRN-1 — mezon sinxroni qo'lda qo'yilgan bahoga tegmaydi, ball bir checkni ikki marta sanamaydi;</item>
///   <item>LRN-2 — dars ko'chirish jurnal siyosati/maosh muhlatiga bo'ysunadi, mezonlar ham ko'chadi, tarixga yoziladi;</item>
///   <item>LRN-4 — baholash taxtasi jurnal qoidalariga bo'ysunadi;</item>
///   <item>LRN-5 — davomat eslatmasi kunning HAQIQIY darslariga va o'rinbosarga, belgi bazada;</item>
///   <item>LRN-6 — onlayn test kaliti o'zgarsa javoblar qayta baholanadi; o'rin qoidasi bitta.</item>
/// </list>
/// Sanalar <c>AppClock</c>ga NISBATAN quriladi.
/// </summary>
public class AuditFixT5Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");
    private static int WeekdayIdx(int offset) => ((int)AppClock.Today.AddDays(offset).DayOfWeek + 6) % 7;
    private static readonly IReadOnlySet<string> NoLate = new HashSet<string>();

    private static Group AddGroup(TestDb db, string? startDate = null, IEnumerable<int>? days = null, string name = "Ingliz A1")
    {
        var subject = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(subject);
        var g = new Group
        {
            Name = name, CourseId = subject.Id, StartDate = startDate ?? Day(-90),
            Days = (days ?? Enumerable.Range(0, 7)).ToList(), StartTime = "14:00", EndTime = "15:30",
            TeacherId = "t-1",
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, Group g, string name, string status = "active",
        bool isActive = true, string? frozenAt = null, bool archived = false, string? activatedAt = null)
    {
        var s = new Student { FullName = name, IsArchived = archived };
        db.Context.Students.Add(s);
        db.Context.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = isActive, Status = status,
            JoinedAt = activatedAt ?? Day(-60), ActivatedAt = activatedAt ?? Day(-60),
            RecordedAt = activatedAt ?? Day(-60), FrozenAt = frozenAt ?? "",
        });
        db.Context.SaveChanges();
        return s;
    }

    private static Student AddOutsider(TestDb db, string name = "Begona")
    {
        var s = new Student { FullName = name };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    private static AbsenceReason Reason(TestDb db, string name, bool late = false)
    {
        var r = new AbsenceReason { Name = name, Short = name[..1], IsLate = late };
        db.Context.AbsenceReasons.Add(r);
        db.Context.SaveChanges();
        return r;
    }

    private static SetJournalEntryRequest Cell(Group g, Student s, string date, int? grade = null,
        string? reasonId = null, int homework = 0) =>
        new(g.Id, g.CourseId!, 1, s.Id, date, 1, grade, reasonId, homework);

    /* =========================================================================================
     *  C8 — "KELMADI" XABARI QACHON KETADI
     * ========================================================================================= */

    [Fact]
    public void IsNewAbsence_KECH_hechqachon_kelmadi_emas()
    {
        var late = new HashSet<string> { "kech" };
        Assert.False(AttendanceNotify.IsNewAbsence(null, "kech", late));
        Assert.False(AttendanceNotify.IsNewAbsence("sababsiz", "kech", late));
    }

    [Fact]
    public void IsNewAbsence_faqat_KELMADI_emas_holatdan_otishda()
    {
        var late = new HashSet<string> { "kech" };
        Assert.True(AttendanceNotify.IsNewAbsence(null, "sababsiz", late));     // belgisiz → kelmadi
        Assert.True(AttendanceNotify.IsNewAbsence("kech", "sababsiz", late));   // kech → kelmadi (haqiqiy o'zgarish)
        Assert.False(AttendanceNotify.IsNewAbsence("sababsiz", "kasal", late)); // sabab almashdi — yangi hodisa EMAS
        Assert.False(AttendanceNotify.IsNewAbsence("kasal", "kasal", NoLate));
        Assert.False(AttendanceNotify.IsNewAbsence("kasal", null, NoLate));     // tozalandi
    }

    [Fact]
    public async Task SetEntry_KECH_sababi_yangi_kelmadi_QAYTARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var kech = Reason(db, "Kech keldi", late: true);

        var dispatch = await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), reasonId: kech.Id));

        Assert.False(dispatch);
        Assert.Equal(kech.Id, (await db.Context.JournalEntries.SingleAsync()).ReasonId);
    }

    [Fact]
    public async Task SetEntry_sabab_ALMASHSA_qayta_xabar_yoq_birinchi_belgida_bor()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var sababsiz = Reason(db, "Sababsiz");
        var kasal = Reason(db, "Kasal");

        Assert.True(await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), reasonId: sababsiz.Id)));
        Assert.False(await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), reasonId: kasal.Id)));
    }

    [Fact]
    public async Task Bulk_KELMADI_faqat_YANGI_kelmaganlarni_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var allaqachon = AddStudent(db, g, "Allaqachon kelmagan");
        var kelgan = AddStudent(db, g, "Kelgan edi");
        var begona = AddOutsider(db);
        var sababsiz = Reason(db, "Sababsiz");
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, StudentId = allaqachon.Id,
            Date = Day(0), Period = 1, ReasonId = sababsiz.Id,
        });
        await db.Context.SaveChangesAsync();

        var r = await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId!, Day(0), 1, new List<string> { allaqachon.Id, kelgan.Id, begona.Id }, sababsiz.Id, Absent: true));

        Assert.Equal(new[] { kelgan.Id }, r.NewlyAbsentIds);                 // faqat yangi kelmagan
        Assert.Equal(new[] { begona.Id }, r.SkippedIds);                      // a'zo emas — chetlab o'tildi
        Assert.DoesNotContain(await db.Context.JournalEntries.Select(e => e.StudentId).ToListAsync(), id => id == begona.Id);
    }

    [Fact]
    public async Task Bulk_KECH_bilan_hech_kim_YANGI_kelmagan_emas()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var kech = Reason(db, "Kech keldi", late: true);

        var r = await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId!, Day(0), 1, new List<string> { s.Id }, kech.Id, Absent: true));

        Assert.Empty(r.NewlyAbsentIds);
        Assert.Equal(new[] { s.Id }, r.WrittenIds);
    }

    /* =========================================================================================
     *  LRN-3 — KIMGA YOZISH MUMKIN (a'zolik darvozasi)
     * ========================================================================================= */

    [Fact]
    public void MemberWriteError_qoidalari()
    {
        var active = new StudentGroup { IsActive = true, Status = "active", ActivatedAt = Day(-30) };
        var frozen = new StudentGroup { IsActive = true, Status = "frozen", ActivatedAt = Day(-30), FrozenAt = Day(-10) };
        var left = new StudentGroup { IsActive = false, Status = "active", ActivatedAt = Day(-30), LeftAt = Day(-5) };
        var leftNoDate = new StudentGroup { IsActive = false, Status = "active", ActivatedAt = Day(-30) };

        Assert.Equal(JournalService.NotMemberMessage, JournalService.MemberWriteError(null, false, Day(0), teacher: false));
        Assert.Equal(JournalService.BeforeMemberStartMessage, JournalService.MemberWriteError(active, false, Day(-40), teacher: true));
        Assert.Null(JournalService.MemberWriteError(active, false, Day(0), teacher: true));
        // O'qituvchi: muzlatilgan / chiqqan / arxivlangan — yo'q.
        Assert.Equal(JournalService.NotActiveMemberMessage, JournalService.MemberWriteError(frozen, false, Day(-20), teacher: true));
        Assert.Equal(JournalService.NotActiveMemberMessage, JournalService.MemberWriteError(left, false, Day(-20), teacher: true));
        Assert.Equal(JournalService.ArchivedStudentMessage, JournalService.MemberWriteError(active, true, Day(0), teacher: true));
        // Admin: guruhda BO'LGAN kunni tuzata oladi, undan keyingisini — yo'q.
        Assert.Null(JournalService.MemberWriteError(frozen, false, Day(-10), teacher: false));
        Assert.Equal(JournalService.OutsideMembershipMessage, JournalService.MemberWriteError(frozen, false, Day(-9), teacher: false));
        Assert.Null(JournalService.MemberWriteError(left, false, Day(-6), teacher: false));
        Assert.Equal(JournalService.OutsideMembershipMessage, JournalService.MemberWriteError(left, false, Day(0), teacher: false));
        Assert.Equal(JournalService.OutsideMembershipMessage, JournalService.MemberWriteError(leftNoDate, false, Day(-6), teacher: false));
    }

    [Fact]
    public async Task SetEntry_OQITUVCHI_begona_va_muzlatilgan_oquvchiga_yoza_olmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var muzlagan = AddStudent(db, g, "Muzlagan", status: "frozen", frozenAt: Day(-3));
        var begona = AddOutsider(db);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Cell(g, begona, Day(0), grade: 5), teacher: true));
        Assert.Equal(JournalService.NotMemberMessage, ex1.Message);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Cell(g, muzlagan, Day(-5), grade: 5), teacher: true));
        Assert.Equal(JournalService.NotActiveMemberMessage, ex2.Message);
        Assert.Empty(db.Context.JournalEntries);

        // Admin esa muzlatilgan o'quvchining muzlatishdan OLDINGI darsini tuzata oladi.
        await JournalService.SetEntryAsync(db.Context, Cell(g, muzlagan, Day(-5), grade: 5));
        Assert.Equal(1, await db.Context.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Bulk_OQITUVCHI_muzlatilgan_va_arxivlanganni_CHETLAB_otadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var faol = AddStudent(db, g, "Faol");
        var muzlagan = AddStudent(db, g, "Muzlagan", status: "frozen", frozenAt: Day(-3));
        var arxiv = AddStudent(db, g, "Arxivda", archived: true);

        var r = await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId!, Day(0), 1, new List<string> { faol.Id, muzlagan.Id, arxiv.Id }, null, Absent: true),
            teacher: true);

        Assert.Equal(new[] { faol.Id }, r.WrittenIds);
        Assert.Equal(new[] { faol.Id }, r.NewlyAbsentIds);
        Assert.Equal(2, r.SkippedIds.Count);
    }

    /* =========================================================================================
     *  C1 — arxivlangan (a'zoligi hali tirik) o'quvchi JORIY oy jurnalida yo'q
     * ========================================================================================= */

    [Fact]
    public async Task GroupMonth_ARXIVLANGAN_tirik_azo_joriy_oyda_YOQ_otgan_oyda_BOR()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: AppClock.Today.AddMonths(-2).ToString("yyyy-MM-01"));
        var faol = AddStudent(db, g, "Faol", activatedAt: AppClock.Today.AddMonths(-2).ToString("yyyy-MM-01"));
        var arxiv = AddStudent(db, g, "Arxivda", archived: true, activatedAt: AppClock.Today.AddMonths(-2).ToString("yyyy-MM-01"));

        var cur = await JournalService.GroupMonthAsync(db.Context, g.Id, null);
        Assert.Equal(new[] { faol.Id }, cur!.Students.Select(s => s.StudentId));

        var prevMonth = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        var prev = await JournalService.GroupMonthAsync(db.Context, g.Id, prevMonth);
        Assert.Contains(prev!.Students, s => s.StudentId == arxiv.Id);
    }

    /* =========================================================================================
     *  LRN-1 — MEZON SINXRONI va BALL
     * ========================================================================================= */

    private static string AddCriterion(TestDb db, Group g, string name)
    {
        var c = new GradingCriterion { Name = name, MaxScore = 5 };
        db.Context.GradingCriteria.Add(c);
        db.Context.GroupGradingCriteria.Add(new GroupGradingCriterion { GroupId = g.Id, CriterionId = c.Id });
        db.Context.SaveChanges();
        return c.Id;
    }

    private static SetCriterionGradeRequest Tick(Group g, Student s, string criterionId, bool done = true, int day = 0) =>
        new(g.Id, s.Id, criterionId, Day(day), done);

    [Fact]
    public async Task Mezon_belgilansa_QOLDA_qoyilgan_bahoga_TEGMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), grade: 5));

        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1), isAdmin: true);
        var entry = await db.Context.JournalEntries.SingleAsync();
        Assert.Equal(5, entry.Grade);              // ilgari 1 ga aylanardi
        Assert.False(entry.GradeFromCriteria);

        // Ommaviy "olib tashlash" ham qo'lda bahoni o'chirmaydi.
        await GradingService.BulkGradeAsync(db.Context, g, c1, Day(0), false, isAdmin: true);
        Assert.Equal(5, (await db.Context.JournalEntries.SingleAsync()).Grade);
    }

    [Fact]
    public async Task Mezon_sinxroni_FAQAT_oz_bahosini_yozadi_va_tozalaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        var c2 = AddCriterion(db, g, "Faollik");

        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1), isAdmin: true);
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c2), isAdmin: true);
        var entry = await db.Context.JournalEntries.SingleAsync();
        Assert.Equal(2, entry.Grade);
        Assert.True(entry.GradeFromCriteria);
        Assert.True((await db.Context.LessonNotes.SingleAsync()).Conducted);

        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1, done: false), isAdmin: true);
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c2, done: false), isAdmin: true);
        Assert.Empty(db.Context.JournalEntries);   // bo'm-bo'sh qolgan sinxron yozuvi olib tashlandi
    }

    [Fact]
    public async Task Katakda_faqat_UY_VAZIFA_tahrirlansa_mezon_bahosi_egasi_OZGARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1), isAdmin: true);

        await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), grade: 1, homework: 1));
        Assert.True((await db.Context.JournalEntries.SingleAsync()).GradeFromCriteria);

        await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(0), grade: 4, homework: 1));
        Assert.False((await db.Context.JournalEntries.SingleAsync()).GradeFromCriteria); // qo'lda o'zgartirildi
    }

    [Fact]
    public async Task Ball_bir_checkni_IKKI_marta_sanamaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        var c2 = AddCriterion(db, g, "Faollik");
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1), isAdmin: true);
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c2), isAdmin: true);  // jurnalga 2 (mezondan)
        await JournalService.SetEntryAsync(db.Context, Cell(g, s, Day(-1), grade: 5));     // qo'lda 5

        var b = (await StudentBallService.ComputeByGroupAsync(db.Context))[(s.Id, g.Id)];

        Assert.Equal(5, b.JournalTotal);
        Assert.Equal(2, b.CriteriaDone);
        Assert.Equal(7, b.Ball);   // ilgari 5 + 2 + 2 = 9
    }

    [Fact]
    public async Task Mezon_ajratilganda_qayta_sinxron_faqat_oz_bahosini_kamaytiradi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        var c2 = AddCriterion(db, g, "Faollik");
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1), isAdmin: true);
        await GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c2), isAdmin: true);

        db.Context.GroupGradingCriteria.RemoveRange(db.Context.GroupGradingCriteria.Where(x => x.CriterionId == c2));
        await db.Context.SaveChangesAsync();
        await GradingService.ResyncAsync(db.Context, new[] { (g.Id, s.Id, Day(0)) });
        await db.Context.SaveChangesAsync();

        Assert.Equal(1, (await db.Context.JournalEntries.SingleAsync()).Grade);
    }

    /* =========================================================================================
     *  LRN-4 — BAHOLASH TAXTASI jurnal qoidalariga bo'ysunadi
     * ========================================================================================= */

    [Fact]
    public async Task Baholash_KELAJAK_sana_va_YOPIQ_oyna_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        db.Context.CenterMeta.Add(new CenterMeta { JournalEditMode = JournalPolicy.ModeWindow, JournalRetroDays = 3 });
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1, day: 1), isAdmin: false));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GradingService.UpsertGradeAsync(db.Context, Tick(g, s, c1, day: -10), isAdmin: false));
        Assert.Contains("Jurnal sozlamasi", ex.Message);
        Assert.Empty(db.Context.CriterionGrades);
        Assert.Empty(db.Context.LessonNotes);     // "o'tildi" (maosh) ham yozilmadi
    }

    [Fact]
    public async Task Baholash_MUZLATILGAN_oquvchiga_mezon_qoyilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var muzlagan = AddStudent(db, g, "Muzlagan", status: "frozen", frozenAt: Day(-3));
        var c1 = AddCriterion(db, g, "Uy vazifa");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GradingService.UpsertGradeAsync(db.Context, Tick(g, muzlagan, c1, day: -5), isAdmin: true));
        Assert.Empty(db.Context.CriterionGrades);
    }

    /* =========================================================================================
     *  LRN-2 — DARSNI KO'CHIRISH
     * ========================================================================================= */

    /// <summary>Guruh — faqat BUGUNGI hafta kunida dars (bugun, −7, +7 kunlar dars kuni).</summary>
    private static Group WeeklyGroup(TestDb db) => AddGroup(db, days: new[] { WeekdayIdx(0) });

    private static AuditService Audit(TestDb db) => new(db.Context, new NoHttpContext());

    [Fact]
    public async Task Kochirish_OQITUVCHI_otgan_darsni_KELAJAKKA_kochira_olmaydi_admin_oladi()
    {
        using var db = TestDb.Sqlite();
        var g = WeeklyGroup(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(-7), Day(1), null, "o'qituvchi", isAdmin: false));
        Assert.Contains("kelajakka", ex.Message);
        Assert.Empty(db.Context.LessonReschedules);

        await JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(-7), Day(1), null, "admin", isAdmin: true, Audit(db));
        Assert.Single(db.Context.LessonReschedules);
    }

    [Fact]
    public async Task Kochirish_OQITUVCHI_maosh_muhlati_otgan_darsni_kochira_olmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = WeeklyGroup(db);
        db.Context.CenterMeta.Add(new CenterMeta { SalaryRequireJournal = true, SalaryGraceDays = 2 });
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(-7), Day(-6), null, "o'qituvchi", isAdmin: false));
        Assert.Contains("muhlati", ex.Message);
    }

    [Fact]
    public async Task Kochirish_JURNAL_OYNASI_yopiq_kunni_kochirib_bolmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = WeeklyGroup(db);
        db.Context.CenterMeta.Add(new CenterMeta { JournalEditMode = JournalPolicy.ModeWindow, JournalRetroDays = 3 });
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(-7), Day(-6), null, "o'qituvchi", isAdmin: false));
        Assert.Contains("Jurnal sozlamasi", ex.Message);
    }

    [Fact]
    public async Task Kochirish_ORINBOSAR_qamragan_kun_hech_kimga_kochirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = WeeklyGroup(db);
        db.Context.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = "t-1", SubstituteTeacherId = "t-2",
            Date = Day(7), SelectedDates = new List<string> { Day(7) }, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(7), Day(8), null, "admin", isAdmin: true));
        Assert.Contains("o'rinbosar", ex.Message);
    }

    [Fact]
    public async Task Kochirish_MEZON_belgilari_ham_kochadi_va_TARIXGA_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var g = WeeklyGroup(db);
        var s = AddStudent(db, g, "Ali");
        db.Context.CriterionGrades.Add(new CriterionGrade
        {
            GroupId = g.Id, StudentId = s.Id, CriterionId = "c-1", Date = Day(-7), Done = true,
        });
        await db.Context.SaveChangesAsync();

        await JournalService.RescheduleLessonAsync(db.Context, g.Id, Day(-7), Day(-6), "16:00", "admin", isAdmin: true, Audit(db));

        Assert.Equal(Day(-6), (await db.Context.CriterionGrades.SingleAsync()).Date);
        var log = await db.Context.AuditLogs.SingleAsync();
        Assert.Equal("Group", log.EntityType);
        Assert.Equal(g.Id, log.EntityId);
        Assert.Contains("ko'chirildi", log.Summary);

        // Bekor qilish — mezon ham asl kunga qaytadi.
        var rec = await db.Context.LessonReschedules.SingleAsync();
        await JournalService.CancelRescheduleAsync(db.Context, rec.Id, isAdmin: true, Audit(db));
        Assert.Equal(Day(-7), (await db.Context.CriterionGrades.SingleAsync()).Date);
    }

    /* =========================================================================================
     *  LRN-5 — DAVOMAT ESLATMASI
     * ========================================================================================= */

    [Fact]
    public async Task Eslatma_kunning_HAQIQIY_darslari_va_ORINBOSARGA()
    {
        using var db = TestDb.Sqlite();
        var bugun = WeekdayIdx(0);
        var boshqaKun = (bugun + 1) % 7;
        var oddiy = AddGroup(db, days: new[] { bugun }, name: "Oddiy");
        var bloklangan = AddGroup(db, days: new[] { bugun }, name: "Bloklangan");
        bloklangan.IsBlocked = true;
        var kochirilgan = AddGroup(db, days: new[] { boshqaKun }, name: "Ko'chirilgan");
        var kochirilganAsl = AddGroup(db, days: new[] { bugun }, name: "Bugundan ketgan");
        db.Context.LessonReschedules.Add(new LessonReschedule { ClassId = kochirilgan.Id, FromDate = Day(1), ToDate = Day(0), Time = "17:00" });
        db.Context.LessonReschedules.Add(new LessonReschedule { ClassId = kochirilganAsl.Id, FromDate = Day(0), ToDate = Day(1) });
        db.Context.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = oddiy.Id, OriginalTeacherId = "t-1", SubstituteTeacherId = "t-sub",
            Date = Day(0), SelectedDates = new List<string> { Day(0) }, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var lessons = await LessonAttendanceReminderService.LessonsTodayAsync(db.Context, AppClock.Today);

        Assert.Equal(new[] { "Ko'chirilgan", "Oddiy" }, lessons.Select(l => l.Group.Name).OrderBy(n => n, StringComparer.Ordinal));
        var moved = lessons.Single(l => l.Group.Id == kochirilgan.Id);
        Assert.Equal("17:00", moved.StartTime);
        Assert.Equal("t-sub", LessonAttendanceReminderService.RecipientTeacherId(lessons.Single(l => l.Group.Id == oddiy.Id)));
        Assert.Equal("t-1", LessonAttendanceReminderService.RecipientTeacherId(moved));
    }

    [Fact]
    public async Task Eslatma_belgisi_BAZADA_qayta_yuklashdan_keyin_ham_takrorlanmaydi()
    {
        using var db = TestDb.Sqlite();
        var key = $"r1:g1:{Day(0)}";

        Assert.True(await LessonAttendanceReminderService.TryMarkAsync(db.Context, key, Day(0), default));
        // "Server qayta yuklandi" — xotirada hech narsa yo'q, faqat baza.
        Assert.False(await LessonAttendanceReminderService.TryMarkAsync(db.Context, key, Day(0), default));
        Assert.Equal(1, await db.Context.LessonReminderMarks.CountAsync());
    }

    /* =========================================================================================
     *  LRN-6 — TEST: maksimal ball kamaysa mavjud ballar QISILADI
     *  (onlayn qism — kalit o'zgarsa qayta baholash, tashqi ishtirokchilar — 2026-10 da onlayn test
     *  rejimi bilan birga olib tashlandi; testlar faqat oflayn)
     * ========================================================================================= */

    [Fact]
    public async Task Test_MAKSIMAL_ball_kamaysa_mavjud_ballar_QISILADI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var t = new TestResult { GroupId = g.Id, Name = "Unit 1", Date = Day(0), MaxScore = 10 };
        db.Context.TestResults.Add(t);
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = s.Id, Score = 9 });
        await db.Context.SaveChangesAsync();

        var r = await TestResultService.UpdateDetailedAsync(db.Context, t.Id, "Unit 1", Day(0), 5);

        Assert.True(r.Ok);
        Assert.Equal(1, r.Regraded);
        Assert.Equal(5, (await db.Context.TestScores.SingleAsync()).Score);
    }

    [Fact]
    public void Test_qolda_ball_faqat_QISILADI()
    {
        Assert.Equal(2, TestResultService.ClampScore(9, 2));
        Assert.Equal(1, TestResultService.ClampScore(1, 2));
        Assert.Equal(0, TestResultService.ClampScore(-3, 2));
    }

    /* =========================================================================================
     *  Kunlik davomat sahifasi — faqat shu kuni KUTILGAN a'zolar
     * ========================================================================================= */

    [Fact]
    public void ExpectedOn_muzlatilgan_va_hali_qoshilmagan_KUTILMAYDI()
    {
        var active = new StudentGroup { IsActive = true, Status = "active", ActivatedAt = Day(-10) };
        var frozen = new StudentGroup { IsActive = true, Status = "frozen", ActivatedAt = Day(-30), FrozenAt = Day(-5) };
        var frozenNoDate = new StudentGroup { IsActive = true, Status = "frozen", ActivatedAt = Day(-30) };
        var left = new StudentGroup { IsActive = false, Status = "active", ActivatedAt = Day(-30), LeftAt = Day(-2) };

        Assert.True(StudentJournalBuilder.ExpectedOn(active, Day(0)));
        Assert.False(StudentJournalBuilder.ExpectedOn(active, Day(-11)));    // hali qo'shilmagan
        Assert.False(StudentJournalBuilder.ExpectedOn(frozen, Day(0)));      // muzlatilgan
        Assert.True(StudentJournalBuilder.ExpectedOn(frozen, Day(-5)));      // muzlatish kuni hali hisoblanadi
        Assert.False(StudentJournalBuilder.ExpectedOn(frozenNoDate, Day(0)));
        Assert.True(StudentJournalBuilder.ExpectedOn(left, Day(-3)));        // o'tgan kunda u guruhda edi
        Assert.False(StudentJournalBuilder.ExpectedOn(left, Day(0)));
    }
}
