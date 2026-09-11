using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHI ILOVASI — «Umumiy statistika» (sana oralig'i bo'yicha jurnal) va uning yadrosi
/// <see cref="StudentJournalBuilder"/>.
///
/// <para>Yadro admin jurnal modali bilan BIR XIL (<c>GET /api/admin/student-attendance/journal</c>
/// endi shu servisga o'tkazilgan), shuning uchun bu yerda ikkala chiqish ham tekshiriladi:
/// yangi oraliq endpointi (<see cref="StudentJournalBuilder.PeriodAsync"/>) va eski admin javobi
/// (<see cref="StudentJournalBuilder.GroupMonthAsync"/>) — refaktordan keyin xatti-harakat
/// o'zgarmaganiga ishonch uchun.</para>
///
/// <para>Sanalar HAR DOIM <c>AppClock</c>ning JORIY OYiga nisbatan quriladi — admin javobidagi
/// oylar ro'yxati joriy oyda tugaydi, testlar esa istalgan kunda ishlashi kerak.</para>
/// </summary>
public class StudentPortalJournalTests
{
    /* =========================================================================================
     *  Yordamchilar
     * ========================================================================================= */

    private static string Month => AppClock.Today.ToString("yyyy-MM");

    /// <summary>Joriy oyning N-kuni ("yyyy-MM-dd").</summary>
    private static string D(int day) => $"{Month}-{day:00}";

    /// <summary>Guruh: HAFTANING BARCHA kunlari dars (0..6) — shunda oyning har kuni dars sanasi
    /// bo'ladi va test kalendarga (bugun qaysi hafta kuni ekaniga) bog'liq bo'lmaydi.</summary>
    private static Group AddGroup(TestDb db, string name, string subjectName, string startDay = "01")
    {
        var subject = new Subject { Name = subjectName };
        db.Context.Subjects.Add(subject);
        var teacher = new Teacher { FullName = $"{subjectName} o'qituvchisi" };
        db.Context.Teachers.Add(teacher);
        var g = new Group
        {
            Name = name,
            CourseId = subject.Id,
            TeacherId = teacher.Id,
            StartDate = $"{Month}-{startDay}",
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, string fullName = "Ali Valiyev")
    {
        var s = new Student { FullName = fullName };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    private static StudentGroup AddMember(
        TestDb db, Student s, Group g,
        string? activatedAt = null, string? recordedAt = null,
        string status = "active", string? frozenAt = null, string? leftAt = null)
    {
        var m = new StudentGroup
        {
            StudentId = s.Id,
            GroupId = g.Id,
            IsActive = leftAt is null,
            Status = status,
            JoinedAt = activatedAt ?? D(1),
            ActivatedAt = activatedAt ?? D(1),
            RecordedAt = recordedAt ?? activatedAt ?? D(1),
            FrozenAt = frozenAt ?? "",
            LeftAt = leftAt,
        };
        db.Context.StudentGroups.Add(m);
        db.Context.SaveChanges();
        return m;
    }

    /// <summary>O'tilgan dars (ustun). <paramref name="attendanceTaken"/> — RASSMIY davomat olindi.</summary>
    private static void AddLesson(
        TestDb db, Group g, string date, bool attendanceTaken = true,
        string topic = "", string? homework = null)
    {
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, Date = date, Period = 1,
            Topic = topic, Homework = homework, Conducted = true, AttendanceTaken = attendanceTaken,
        });
        db.Context.SaveChanges();
    }

    private static void AddEntry(
        TestDb db, Group g, Student s, string date,
        int? grade = null, string? reasonId = null, bool present = false,
        int homework = 0, int behavior = 0)
    {
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, StudentId = s.Id,
            Date = date, Period = 1, Grade = grade, ReasonId = reasonId, Present = present,
            Homework = homework, Behavior = behavior,
        });
        db.Context.SaveChanges();
    }

    private static AbsenceReason AddReason(TestDb db, string name, bool isLate)
    {
        var r = new AbsenceReason { Name = name, Short = name[..1], IsLate = isLate };
        db.Context.AbsenceReasons.Add(r);
        db.Context.SaveChanges();
        return r;
    }

    /* =========================================================================================
     *  1) Sana oralig'i filtri
     * ========================================================================================= */

    /// <summary>Oraliqdan tashqaridagi dars javobga TUSHMAYDI (hafta tanlangan — oyning qolgani emas).</summary>
    [Fact]
    public async Task Oraliqdan_tashqaridagi_dars_qaytmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g);
        AddLesson(db, g, D(3));
        AddLesson(db, g, D(12));

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(5), null);

        Assert.NotNull(dto);
        Assert.Single(dto!.Lessons);
        Assert.Equal(D(3), dto.Lessons[0].Date);
        Assert.Equal(1, dto.Summary.Held);
    }

    /* =========================================================================================
     *  2) A'zolik chegaralari (memberStart / memberEnd)
     * ========================================================================================= */

    /// <summary>
    /// O'quvchi 10-kuni qo'shilib, 20-kuni MUZLATILGAN: 5-kundagi (qo'shilishidan oldin) va
    /// 25-kundagi (muzlatilgandan keyin) darslar unga TEGISHLI EMAS — javobda ham, jamlanmada ham yo'q.
    /// </summary>
    [Fact]
    public async Task Azolikdan_oldingi_va_muzlatilgandan_keyingi_darslar_chiqmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g, activatedAt: D(10), status: "frozen", frozenAt: D(20));
        AddLesson(db, g, D(5));
        AddLesson(db, g, D(15));
        AddLesson(db, g, D(25));

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Single(dto!.Lessons);
        Assert.Equal(D(15), dto.Lessons[0].Date);
        Assert.Equal(1, dto.Summary.Held);
        Assert.Equal(1, dto.Summary.Attended);
        Assert.Equal(100, dto.Summary.AttendancePct);
    }

    /* =========================================================================================
     *  3) RecordedAt — orqaga sanalgan a'zolikdagi NOMA'LUM darslar
     * ========================================================================================= */

    /// <summary>
    /// A'zolik 1-kundan sanalgan, lekin tizimga 10-kuni kiritilgan (<c>RecordedAt</c>). 5-kundagi
    /// darsda bu o'quvchida yozuv YO'Q — u "keldi" ham, "kelmadi" ham emas, ya'ni JAMLANMAGA
    /// KIRMAYDI (aks holda davomat foizi asossiz tushardi). 15-kundagi dars esa odatdagidek sanaladi.
    /// Dars ro'yxatida esa 5-kun KO'RINADI (dars bo'lgan) — faqat foizga ta'sir qilmaydi.
    /// </summary>
    [Fact]
    public async Task RecordedAtdan_oldingi_yozuvsiz_dars_jamlanmaga_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g, activatedAt: D(1), recordedAt: D(10));
        AddLesson(db, g, D(5));
        AddLesson(db, g, D(15));

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Lessons.Count);                       // ikkala dars ham ro'yxatda
        Assert.False(dto.Lessons.Single(l => l.Date == D(5)).Present);
        Assert.Equal(1, dto.Summary.Held);                         // jamlanmada faqat 15-kun
        Assert.Equal(1, dto.Summary.Attended);
        Assert.Equal(100, dto.Summary.AttendancePct);
    }

    /* =========================================================================================
     *  4) Ikki guruh — birlashtirilgan va filtrlangan ko'rinish
     * ========================================================================================= */

    /// <summary>
    /// <c>groupId</c> berilmasa — ikkala guruh darslari birga (har biri o'z guruhi/fani bilan);
    /// berilsa — faqat o'sha guruh. Guruh TANLOVI (<c>Groups</c>) esa har ikki holatda ham to'liq.
    /// </summary>
    [Fact]
    public async Task Ikki_guruhli_oquvchi_groupId_bilan_va_bilmagan_holda()
    {
        using var db = TestDb.Sqlite();
        var g1 = AddGroup(db, "Ingliz A1", "Ingliz tili");
        var g2 = AddGroup(db, "Matematika 1", "Matematika");
        var s = AddStudent(db);
        AddMember(db, s, g1);
        AddMember(db, s, g2);
        AddLesson(db, g1, D(3));
        AddLesson(db, g2, D(3));
        AddLesson(db, g2, D(4));

        var all = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);
        Assert.NotNull(all);
        Assert.Equal(3, all!.Lessons.Count);
        Assert.Equal(2, all.Groups.Count);
        Assert.Equal(2, all.Subjects.Count);
        Assert.Contains(all.Lessons, l => l.GroupName == "Ingliz A1" && l.SubjectName == "Ingliz tili");
        Assert.Contains(all.Lessons, l => l.GroupName == "Matematika 1" && l.SubjectName == "Matematika");

        var only = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), g2.Id);
        Assert.NotNull(only);
        Assert.Equal(2, only!.Lessons.Count);
        Assert.All(only.Lessons, l => Assert.Equal(g2.Id, l.GroupId));
        Assert.Equal(2, only.Groups.Count);                        // tanlov ro'yxati baribir to'liq
        Assert.Equal(g2.Id, only.GroupId);
    }

    /// <summary>
    /// BEGONA guruh id'si berilsa — bo'sh natija (boshqa o'quvchining jurnali hech qachon ochilmaydi).
    /// </summary>
    [Fact]
    public async Task Azo_bolmagan_guruh_soralsa_bosh_qaytadi()
    {
        using var db = TestDb.Sqlite();
        var mine = AddGroup(db, "Ingliz A1", "Ingliz tili");
        var other = AddGroup(db, "Begona guruh", "Fizika");
        var s = AddStudent(db);
        AddMember(db, s, mine);
        AddLesson(db, mine, D(3));
        AddLesson(db, other, D(3));

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), other.Id);

        Assert.NotNull(dto);
        Assert.Empty(dto!.Lessons);
        Assert.Equal(0, dto.Summary.Held);
    }

    /* =========================================================================================
     *  5) Hisob: avgGrade / attendancePct / uy vazifasi / xulq
     * ========================================================================================= */

    /// <summary>
    /// 4 ta dars: baho 5, baho 4, sababli kelmadi, yozuvsiz (standart "keldi").
    /// → held 4, attended 3 (ikki baho + standart keldi), foiz 75, o'rtacha 4.5, 2 ta baho, 1 kelmadi.
    /// </summary>
    [Fact]
    public async Task Ortacha_baho_va_davomat_foizi_togri_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g);
        var kasal = AddReason(db, "Kasal", isLate: false);
        foreach (var day in new[] { 3, 4, 5, 6 }) AddLesson(db, g, D(day));
        AddEntry(db, g, s, D(3), grade: 5, homework: 1, behavior: 1);
        AddEntry(db, g, s, D(4), grade: 4, homework: 2);
        AddEntry(db, g, s, D(5), reasonId: kasal.Id);
        // D(6) — yozuv yo'q: davomat olingan va RecordedAt oshib ketgan → standart "keldi".

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Equal(4, dto!.Summary.Held);
        Assert.Equal(3, dto.Summary.Attended);
        Assert.Equal(75, dto.Summary.AttendancePct);
        Assert.Equal(1, dto.Summary.Absent);
        Assert.Equal(0, dto.Summary.Late);
        Assert.Equal(2, dto.Summary.GradesCount);
        Assert.Equal(4.5, dto.Summary.AvgGrade);
        Assert.Equal(1, dto.Summary.HomeworkDone);
        Assert.Equal(1, dto.Summary.HomeworkMissed);
        Assert.Equal(1, dto.Summary.BehaviorGood);
        Assert.Equal(0, dto.Summary.BehaviorBad);

        // Fan kesimi — bitta kurs, jamlanma bilan bir xil raqamlar.
        var subj = Assert.Single(dto.Subjects);
        Assert.Equal("Ingliz tili", subj.SubjectName);
        Assert.Equal(4, subj.Held);
        Assert.Equal(3, subj.Attended);
        Assert.Equal(4.5, subj.AvgGrade);
    }

    /// <summary>
    /// KECHIKKAN o'quvchi darsda QATNASHGAN hisoblanadi (davomat foizini tushirmaydi),
    /// lekin alohida <c>Late</c> sanog'ida ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Kechikish_kelmaganga_qoshilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g);
        var kech = AddReason(db, "Kech keldi", isLate: true);
        AddLesson(db, g, D(3));
        AddEntry(db, g, s, D(3), reasonId: kech.Id);

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Summary.Late);
        Assert.Equal(0, dto.Summary.Absent);
        Assert.True(dto.Lessons[0].IsLate);
        Assert.Equal("Kech keldi", dto.Lessons[0].ReasonName);
    }

    /// <summary>Mavzu va uyga vazifa MATNI darsdan (<see cref="LessonNote"/>) keladi.</summary>
    [Fact]
    public async Task Mavzu_va_uyga_vazifa_matni_qaytadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g);
        AddLesson(db, g, D(3), topic: "Present Simple", homework: "Unit 3, 5-mashq");

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Equal("Present Simple", dto!.Lessons[0].Topic);
        Assert.Equal("Unit 3, 5-mashq", dto.Lessons[0].HomeworkText);
    }

    /// <summary>O'tilmagan (rejadagi) dars javobga umuman kirmaydi.</summary>
    [Fact]
    public async Task Otilmagan_dars_qaytmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "A1", "Ingliz tili");
        var s = AddStudent(db);
        AddMember(db, s, g);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, Date = D(3), Period = 1,
            Conducted = false, AttendanceTaken = false,
        });
        db.Context.SaveChanges();

        var dto = await StudentJournalBuilder.PeriodAsync(db.Context, s.Id, D(1), D(28), null);

        Assert.NotNull(dto);
        Assert.Empty(dto!.Lessons);
    }

    /* =========================================================================================
     *  6) Oraliqni normallashtirish (from/to)
     * ========================================================================================= */

    /// <summary>Bo'sh oraliq — JORIY OY (1-kundan oy oxirigacha).</summary>
    [Fact]
    public void Bosh_oraliq_joriy_oyga_tenglashadi()
    {
        var (from, to, error) = StudentJournalBuilder.NormalizeRange(null, null);

        Assert.Null(error);
        Assert.Equal(D(1), from);
        Assert.Equal($"{Month}-{DateTime.DaysInMonth(AppClock.Today.Year, AppClock.Today.Month):00}", to);
    }

    /// <summary>Juda uzun oraliq rad etiladi (jimgina qirqilmaydi — mijoz sababni biladi).</summary>
    [Fact]
    public void Juda_uzun_oraliq_rad_etiladi()
    {
        var from = AppClock.Today.AddDays(-500).ToString("yyyy-MM-dd");
        var to = AppClock.Today.ToString("yyyy-MM-dd");

        var (_, _, error) = StudentJournalBuilder.NormalizeRange(from, to);

        Assert.NotNull(error);
    }

    /// <summary>Teskari oraliq (from &gt; to) ham xato.</summary>
    [Fact]
    public void Teskari_oraliq_rad_etiladi()
    {
        var (_, _, error) = StudentJournalBuilder.NormalizeRange(D(10), D(5));
        Assert.NotNull(error);
    }

    /// <summary>Noto'g'ri sana matni — xato (jimgina joriy oyga tushib ketmaydi).</summary>
    [Fact]
    public void Notogri_sana_rad_etiladi()
    {
        var (_, _, error) = StudentJournalBuilder.NormalizeRange("kecha", null);
        Assert.NotNull(error);
    }

    /* =========================================================================================
     *  7) REFAKTOR NAZORATI — admin javobi (StudentAttendanceController.Journal) o'zgarmadi
     * ========================================================================================= */

    /// <summary>
    /// Admin o'quvchi profilidagi jurnal modali: kataklar OYNING HAR dars kunidan quriladi
    /// (bloklanganlari ham), jamlanma esa faqat "tirik" kataklardan. Bu test refaktordan keyin
    /// javob shakli va raqamlari o'zgarmaganini qo'riqlaydi.
    /// </summary>
    [Fact]
    public async Task Admin_jurnal_modali_ozgarmadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, "Ingliz A1", "Ingliz tili");
        var s = AddStudent(db, "Ali Valiyev");
        AddMember(db, s, g, activatedAt: D(10), recordedAt: D(10));
        var kasal = AddReason(db, "Kasal", isLate: false);
        AddLesson(db, g, D(5));                       // a'zolikdan oldin → blocked
        AddLesson(db, g, D(11));
        AddLesson(db, g, D(12));
        AddEntry(db, g, s, D(11), grade: 5);
        AddEntry(db, g, s, D(12), reasonId: kasal.Id);

        var dto = await StudentJournalBuilder.GroupMonthAsync(db.Context, s.Id, g.Id, Month);

        Assert.NotNull(dto);
        Assert.Equal(s.Id, dto!.StudentId);
        Assert.Equal("Ali Valiyev", dto.FullName);
        Assert.Equal(g.Id, dto.GroupId);
        Assert.Equal(Month, dto.Month);
        Assert.Contains(Month, dto.Months);
        var groupOption = Assert.Single(dto.Groups);
        Assert.Equal("Ingliz tili", groupOption.CourseName);

        // Kataklar — oyning HAMMA kuni (guruh har kuni dars qiladi).
        Assert.Equal(DateTime.DaysInMonth(AppClock.Today.Year, AppClock.Today.Month), dto.Cells.Count);
        Assert.True(dto.Cells.Single(c => c.Date == D(5)).Blocked);
        Assert.False(dto.Cells.Single(c => c.Date == D(11)).Blocked);

        // Jamlanma: 2 ta o'tilgan dars (5-kun bloklangan), 1 keldi (baho), 1 kelmadi, o'rtacha 5.
        Assert.Equal(2, dto.Conducted);
        Assert.Equal(1, dto.Attended);
        Assert.Equal(1, dto.Absent);
        Assert.Equal(0, dto.Late);
        Assert.Equal(5, dto.AvgGrade);
    }

    /// <summary>
    /// Admin modali: guruh berilmasa — birinchi FAOL guruh tanlanadi; o'quvchi guruhsiz bo'lsa
    /// bo'sh (lekin 404 emas) javob qaytadi — eski xatti-harakat.
    /// </summary>
    [Fact]
    public async Task Admin_jurnal_modali_guruhsiz_oquvchi()
    {
        using var db = TestDb.Sqlite();
        var s = AddStudent(db);

        var dto = await StudentJournalBuilder.GroupMonthAsync(db.Context, s.Id, null, null);

        Assert.NotNull(dto);
        Assert.Empty(dto!.Groups);
        Assert.Equal("", dto.GroupId);
        Assert.Empty(dto.Cells);
        Assert.Equal(0, dto.Conducted);
    }

    /// <summary>Mavjud bo'lmagan o'quvchi — ikkala metod ham <c>null</c> (controller 404 qiladi).</summary>
    [Fact]
    public async Task Notanish_oquvchi_null_qaytaradi()
    {
        using var db = TestDb.Sqlite();

        Assert.Null(await StudentJournalBuilder.GroupMonthAsync(db.Context, "yoq", null, null));
        Assert.Null(await StudentJournalBuilder.PeriodAsync(db.Context, "yoq", D(1), D(28), null));
    }
}
