using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUV JARAYONI — bazaga tegadigan mantiq: onlayn testni topshirish
/// (<see cref="OnlineTestService"/>), test natijalari va o'rinlar
/// (<see cref="TestResultService"/>) hamda jurnalga yozish (<see cref="JournalService"/>).
///
/// <para>Sanalar HAR DOIM <c>AppClock</c>ga NISBATAN quriladi — <c>AppClock</c> statik va testlar
/// istalgan kunda ishlaydi.</para>
///
/// <para><c>[Fact(Skip=...)]</c> bilan belgilangan testlar — TASDIQLANGAN XATOLAR: ular
/// KUTILGAN (to'g'ri) xulqni yozib qo'yadi. Production kodi tuzatilgach Skip olib tashlanadi.</para>
/// </summary>
public class JournalDbTests
{
    /* =========================================================================================
     *  Yordamchilar
     * ========================================================================================= */

    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");
    private static string Stamp(double hours) => AppClock.Now.AddHours(hours).ToString("yyyy-MM-ddTHH:mm");

    private static Group AddGroup(TestDb db, string name = "Ingliz A1", string? startDate = null,
        params int[] days)
    {
        var subject = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(subject);
        var g = new Group
        {
            Name = name,
            CourseId = subject.Id,
            StartDate = startDate,
            Days = days.ToList(),
        };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, Group g, string fullName,
        string? activatedAt = null, bool isActive = true, string status = "active", bool member = true)
    {
        var s = new Student { FullName = fullName };
        db.Context.Students.Add(s);
        if (member)
            db.Context.StudentGroups.Add(new StudentGroup
            {
                StudentId = s.Id,
                GroupId = g.Id,
                IsActive = isActive,
                Status = status,
                JoinedAt = activatedAt ?? "",
                ActivatedAt = activatedAt ?? "",
                RecordedAt = activatedAt ?? "",
            });
        db.Context.SaveChanges();
        return s;
    }

    /// <summary>Onlayn test: vaqt oynasi bugungi kunga nisbatan (<paramref name="fromHours"/> ..
    /// <paramref name="toHours"/> soat) quriladi.</summary>
    private static TestResult AddOnlineTest(TestDb db, Group g, string key = "ABCD",
        double fromHours = -1, double toHours = 1)
    {
        var t = new TestResult
        {
            GroupId = g.Id,
            Name = "Unit 1 test",
            Date = Day(0),
            MaxScore = key.Length,
            Mode = "online",
            PdfUrl = "/uploads/test.pdf",
            PdfName = "test.pdf",
            QuestionCount = key.Length,
            OptionCount = 4,
            AnswerKey = key,
            StartAt = Stamp(fromHours),
            EndAt = Stamp(toHours),
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.Context.TestResults.Add(t);
        db.Context.SaveChanges();
        return t;
    }

    /* =========================================================================================
     *  1) ONLAYN TESTNI TOPSHIRISH — OnlineTestService.SubmitAsync
     * ========================================================================================= */

    [Fact]
    public async Task Submit_JavoblarTekshiriladi_BallSaqlanadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD");

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "AB-D");

        Assert.Null(err);
        Assert.NotNull(res);
        Assert.Equal(3, res!.Score);                 // 3 to'g'ri, 1 javobsiz
        Assert.Equal("AB-D", res.Answers);           // pozitsiya saqlangan
        var row = await db.Context.TestScores.SingleAsync();
        Assert.Equal(OnlineTestService.SourceApp, row.Source);
    }

    [Fact]
    public async Task Submit_IKKINCHImartaRadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD");

        await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");
        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "AAAA");

        Assert.Null(res);
        Assert.Contains("allaqachon", err);
        // Birinchi natija BUZILMAGAN bo'lishi kerak.
        Assert.Equal(4, (await db.Context.TestScores.SingleAsync()).Score);
    }

    [Fact]
    public async Task Submit_BOTorqaliTopshirilgan_IlovadanQaytaTopshirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD");
        db.Context.TestScores.Add(new TestScore
        {
            TestResultId = t.Id, StudentId = st.Id, Score = 2, Answers = "AB--", Source = "bot",
        });
        await db.Context.SaveChangesAsync();

        var (_, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");

        Assert.Contains("allaqachon", err);
    }

    [Fact]
    public async Task Submit_QOLDAkiritilganBall_BLOKLAMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD");
        // Source="" — o'qituvchi qo'lda kiritgan ball, bu "o'quvchi topshirgan" degani EMAS.
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = st.Id, Score = 1 });
        await db.Context.SaveChangesAsync();

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");

        Assert.Null(err);
        Assert.Equal(4, res!.Score);   // qo'lda qo'yilgan ball ustiga yozildi
        Assert.Equal(1, await db.Context.TestScores.CountAsync());
    }

    [Fact]
    public async Task Submit_TestHALIBOSHLANMAGAN_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD", fromHours: 24, toHours: 48);

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");

        Assert.Null(res);
        Assert.Contains("boshlanmagan", err);
        Assert.Empty(db.Context.TestScores);
    }

    [Fact]
    public async Task Submit_TestVAQTITUGAGAN_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD", fromHours: -48, toHours: -24);

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");

        Assert.Null(res);
        Assert.Contains("tugagan", err);
        Assert.Empty(db.Context.TestScores);
    }

    [Fact]
    public async Task Submit_BOSHQAguruhTesti_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var mine = AddGroup(db, "Mening guruhim");
        var other = AddGroup(db, "Begona guruh");
        var st = AddStudent(db, mine, "Ali Aliyev");
        var t = AddOnlineTest(db, other, "ABCD");

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "ABCD");

        Assert.Null(res);
        Assert.Contains("guruhingizga tegishli emas", err);
        Assert.Empty(db.Context.TestScores);
    }

    [Fact]
    public async Task Submit_HAMMASIJAVOBSIZ_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var t = AddOnlineTest(db, g, "ABCD");

        var (res, err) = await OnlineTestService.SubmitAsync(db.Context, st.Id, t.Id, "----");

        Assert.Null(res);
        Assert.Contains("bitta javob", err);
        Assert.Empty(db.Context.TestScores);
    }

    [Fact]
    public async Task Detail_JavobKALITI_faqatTESTTUGAGACHberiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var st = AddStudent(db, g, "Ali Aliyev");
        var ochiq = AddOnlineTest(db, g, "ABCD", fromHours: -1, toHours: 1);
        var yopiq = AddOnlineTest(db, g, "ABCD", fromHours: -48, toHours: -24);

        var d1 = await OnlineTestService.DetailAsync(db.Context, st.Id, ochiq.Id);
        var d2 = await OnlineTestService.DetailAsync(db.Context, st.Id, yopiq.Id);

        Assert.Equal("", d1!.AnswerKey);       // hali ochiq — kalit sir
        Assert.Equal("ABCD", d2!.AnswerKey);   // vaqt tugagan — kalit ochiladi
    }

    /* =========================================================================================
     *  2) O'RINLAR — TestResultService.DetailAsync
     * ========================================================================================= */

    [Fact]
    public async Task Detail_TENGball_BIRXILorin_KeyingisiSAKRAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali");
        var bek = AddStudent(db, g, "Bek");
        var vali = AddStudent(db, g, "Vali");
        AddStudent(db, g, "Zafar");   // ball kiritilmagan
        var t = AddOnlineTest(db, g, "ABCDABCDAB");
        db.Context.TestScores.AddRange(
            new TestScore { TestResultId = t.Id, StudentId = ali.Id, Score = 10 },
            new TestScore { TestResultId = t.Id, StudentId = bek.Id, Score = 10 },
            new TestScore { TestResultId = t.Id, StudentId = vali.Id, Score = 5 });
        await db.Context.SaveChangesAsync();

        var detail = await TestResultService.DetailAsync(db.Context, t.Id);

        // Standart musobaqa reytingi: 1, 1, 3 (2-o'rin "sakrab" o'tiladi).
        Assert.Equal(new[] { "Ali", "Bek", "Vali", "Zafar" },
            detail!.Rows.Select(s => s.FullName).ToArray());
        Assert.Equal(new[] { 1, 1, 3, 0 }, detail.Rows.Select(s => s.Rank).ToArray());
    }

    [Fact]
    public async Task Detail_BallsizlarOXIRIDA_RankNol()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        AddStudent(db, g, "Anvar");                       // ballsiz — ismi alifboda birinchi
        var zafar = AddStudent(db, g, "Zafar");
        var t = AddOnlineTest(db, g, "ABCD");
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = zafar.Id, Score = 1 });
        await db.Context.SaveChangesAsync();

        var detail = await TestResultService.DetailAsync(db.Context, t.Id);

        // Ball kiritilgan o'quvchi (past ball bo'lsa ham) TEPADA, ballsizi oxirida va Rank=0.
        Assert.Equal("Zafar", detail!.Rows[0].FullName);
        Assert.Equal(1, detail.Rows[0].Rank);
        Assert.Equal("Anvar", detail.Rows[1].FullName);
        Assert.Equal(0, detail.Rows[1].Rank);
        Assert.Null(detail.Rows[1].Score);
    }

    [Fact]
    public async Task Detail_MUZLATILGANvaCHIQARILGANazolarKORINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        AddStudent(db, g, "Faol");
        AddStudent(db, g, "Muzlatilgan", status: "frozen");
        AddStudent(db, g, "Chiqarilgan", isActive: false);
        var t = AddOnlineTest(db, g, "ABCD");

        var detail = await TestResultService.DetailAsync(db.Context, t.Id);

        Assert.Equal(new[] { "Faol" }, detail!.Rows.Select(s => s.FullName).ToArray());
    }

    /* =========================================================================================
     *  3) TEST YARATISH/TAHRIRLASH — TestResultService
     * ========================================================================================= */

    private static OnlineTestDto Online(string key = "ABCD", string pdf = "/uploads/t.pdf",
        int? questionCount = null, string start = "", string end = "") =>
        new("online", pdf, "t.pdf", questionCount ?? key.Length, 4, key, start, end);

    [Fact]
    public async Task Create_KALITuzunligiMOSEMAS_XatoVaYOZUVYARATILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        var (dto, err) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 10, "admin", Online("ABC", questionCount: 5));

        Assert.Null(dto);
        Assert.Contains("5 ta harfdan", err);
        Assert.Empty(db.Context.TestResults);
    }

    [Fact]
    public async Task Create_TUGASHboshlanishdanOLDIN_Xato()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        var (dto, err) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 4, "admin",
            Online(start: Stamp(2), end: Stamp(1)));

        Assert.Null(dto);
        Assert.Contains("Tugash vaqti", err);
        Assert.Empty(db.Context.TestResults);
    }

    [Fact]
    public async Task Create_PDFYOQ_Xato()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        var (dto, err) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 4, "admin", Online(pdf: ""));

        Assert.Null(dto);
        Assert.Contains("PDF", err);
        Assert.Empty(db.Context.TestResults);
    }

    [Fact]
    public async Task Create_SavollarSoni_CHEGARADANtashqari_Xato()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        var (_, tooMany) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 4, "admin",
            Online(new string('A', 201), questionCount: 201));
        var (_, zero) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 4, "admin", Online("", questionCount: 0));

        Assert.Contains("1 dan 200 gacha", tooMany);
        Assert.Contains("1 dan 200 gacha", zero);
        Assert.Empty(db.Context.TestResults);
    }

    [Fact]
    public async Task Create_ONLAYNtestda_MaxScoreSAVOLLARSONIgaTENGLASHADI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        // Kiritilgan maxScore (999) e'tiborsiz — onlaynda har savol 1 ball.
        var (dto, err) = await TestResultService.CreateAsync(
            db.Context, g.Id, "Unit 1", Day(0), 999, "admin", Online("ABCDA"));

        Assert.Null(err);
        Assert.Equal(5, dto!.MaxScore);
    }

    [Fact]
    public async Task Create_NomiBoshYokiMaxScoreNol_Xato()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);

        var (_, noName) = await TestResultService.CreateAsync(db.Context, g.Id, "   ", Day(0), 10, "admin");
        var (_, noScore) = await TestResultService.CreateAsync(db.Context, g.Id, "Unit 1", Day(0), 0, "admin");

        Assert.Contains("nomi", noName);
        Assert.Contains("Maksimal ball", noScore);
        Assert.Empty(db.Context.TestResults);
    }

    [Fact]
    public async Task Update_ONLINEnullBerilsa_REJIMOZGARMAYDI()
    {
        // tests.md:56-57 qoidasi: eski/qisqartirilgan forma onlayn testni oflaynga aylantirmasin
        // (PDF, kalit va vaqt oynasi yo'qolib ketardi).
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddOnlineTest(db, g, "ABCD");

        var (ok, err) = await TestResultService.UpdateAsync(db.Context, t.Id, "Yangi nom", Day(1), 99, null);

        Assert.True(ok);
        Assert.Null(err);
        var fresh = await db.Context.TestResults.SingleAsync();
        Assert.Equal("online", fresh.Mode);
        Assert.Equal("ABCD", fresh.AnswerKey);
        Assert.Equal("/uploads/test.pdf", fresh.PdfUrl);
        Assert.Equal(4, fresh.MaxScore);        // MaxScore savollar soniga qaytariladi (99 emas)
    }

    [Fact]
    public async Task Update_ModeOFFLINEberilsa_RejimAlmashadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddOnlineTest(db, g, "ABCD");

        var (ok, _) = await TestResultService.UpdateAsync(db.Context, t.Id, "Oflayn", Day(0), 20,
            new OnlineTestDto("offline", "", "", 0, 4, "", "", ""));

        Assert.True(ok);
        var fresh = await db.Context.TestResults.SingleAsync();
        Assert.Equal("offline", fresh.Mode);
        Assert.Equal(20, fresh.MaxScore);
    }

    [Fact]
    public async Task Update_PDFalmashtirilsa_TelegramKESHIbekorQilinadi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddOnlineTest(db, g, "ABCD");
        t.PdfFileId = "eski-file-id";
        await db.Context.SaveChangesAsync();

        await TestResultService.UpdateAsync(db.Context, t.Id, "Unit 1", Day(0), 4,
            Online(pdf: "/uploads/yangi.pdf"));

        Assert.Equal("", (await db.Context.TestResults.SingleAsync()).PdfFileId);
    }

    /* =========================================================================================
     *  4) JURNALGA YOZISH — JournalService.SetEntryAsync
     * ========================================================================================= */

    private static SetJournalEntryRequest Entry(Group g, Student s, string date, int? grade = 5) =>
        new(g.Id, g.CourseId, 1, s.Id, date, 1, grade, null);

    [Fact]
    public async Task SetEntry_KELASIkunga_Istisno()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(1))));

        Assert.Contains("Kelasi kunlarga", ex.Message);
        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task SetEntry_GURUHboshlanishidanOLDIN_Istisno()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-10));
        var s = AddStudent(db, g, "Ali");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(-20))));

        Assert.Contains("guruh yaratilishidan oldin", ex.Message);
        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task SetEntry_MEMBERSTARTdanOLDIN_Istisno()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Ali", activatedAt: Day(-5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(-10))));

        Assert.Contains("aktivlashtirilgan sanadan oldingi", ex.Message);
        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task SetEntry_BUGUNGIkunga_YozuvVaOTILDIbelgisiPaydoBoladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Ali", activatedAt: Day(-30));

        await JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(0), grade: 4));

        var entry = await db.Context.JournalEntries.SingleAsync();
        Assert.Equal(4, entry.Grade);
        // Baho kiritilishi darsni avtomatik "o'tildi" qiladi (ustun paydo bo'lishi uchun).
        var note = await db.Context.LessonNotes.SingleAsync();
        Assert.True(note.Conducted);
    }

    /* =========================================================================================
     *  5) OMMAVIY DAVOMAT — JournalService.BulkAttendanceAsync
     * ========================================================================================= */

    [Fact]
    public async Task Bulk_KEYINqoshilganOquvchiCHETLABotiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var eski = AddStudent(db, g, "Eski", activatedAt: Day(-30));
        var yangi = AddStudent(db, g, "Yangi", activatedAt: Day(-1));   // dars kunidan KEYIN qo'shilgan

        await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId, Day(-10), 1, new List<string> { eski.Id, yangi.Id }, null));

        var ids = await db.Context.JournalEntries.Select(e => e.StudentId).ToListAsync();
        Assert.Equal(new[] { eski.Id }, ids);
    }

    [Fact]
    public async Task Bulk_HAMMASIKELDI_SababTozalanadi_PresentYoqiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Ali", activatedAt: Day(-30));
        var reason = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.Context.AbsenceReasons.Add(reason);
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id, SubjectId = g.CourseId, Quarter = 1, StudentId = s.Id,
            Date = Day(-1), Period = 1, ReasonId = reason.Id, Grade = 5,
        });
        await db.Context.SaveChangesAsync();

        var used = await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId, Day(-1), 1, new List<string> { s.Id }, null));

        Assert.Null(used);
        var entry = await db.Context.JournalEntries.SingleAsync();
        Assert.Null(entry.ReasonId);      // sabab tozalandi
        Assert.True(entry.Present);
        Assert.Equal(5, entry.Grade);     // BAHO tegilmadi
        var note = await db.Context.LessonNotes.SingleAsync();
        Assert.True(note.Conducted);
        Assert.True(note.AttendanceTaken);
    }

    [Fact]
    public async Task Bulk_HAMMASIKELMADI_SababYOQbolsaAVTOMATIKyaratiladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Ali", activatedAt: Day(-30));

        var used = await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId, Day(-1), 1, new List<string> { s.Id }, null, Absent: true));

        Assert.NotNull(used);
        var reason = await db.Context.AbsenceReasons.SingleAsync();
        Assert.Equal("Sababsiz", reason.Name);
        var entry = await db.Context.JournalEntries.SingleAsync();
        Assert.Equal(reason.Id, entry.ReasonId);
        Assert.False(entry.Present);
    }

    [Fact]
    public async Task Bulk_KELASIkunga_Istisno()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Ali", activatedAt: Day(-30));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
                g.Id, g.CourseId, Day(1), 1, new List<string> { s.Id }, null)));

        Assert.Empty(db.Context.JournalEntries);
        Assert.Empty(db.Context.LessonNotes);
    }

    /* =========================================================================================
     *  6) BAHOLASH MEZONLARI — GradingService
     * ========================================================================================= */

    /// <summary>Guruhga mezon biriktiradi va uning id'sini qaytaradi.</summary>
    private static string AddCriterion(TestDb db, Group? g, string name)
    {
        var c = new GradingCriterion { Name = name, MaxScore = 5 };
        db.Context.GradingCriteria.Add(c);
        if (g is not null)
            db.Context.GroupGradingCriteria.Add(new GroupGradingCriterion { GroupId = g.Id, CriterionId = c.Id });
        db.Context.SaveChanges();
        return c.Id;
    }

    private static void MarkDone(TestDb db, Group g, Student s, string criterionId, int dayOffset = 0)
    {
        db.Context.CriterionGrades.Add(new CriterionGrade
        {
            GroupId = g.Id, StudentId = s.Id, CriterionId = criterionId,
            Date = Day(dayOffset), Done = true,
        });
        db.Context.SaveChanges();
    }

    [Fact]
    public async Task Grading_BajarilganMezonlarSanaladi_OrtachaUlushBoladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        AddCriterion(db, g, "Faollik");
        MarkDone(db, g, s, c1);

        var rows = await GradingService.CalculateStudentTotalsAsync(
            db.Context, g.Id, AppClock.Today.ToString("yyyy-MM"));

        var row = Assert.Single(rows);
        Assert.Equal(2, row.CriteriaCount);
        Assert.Equal(1, row.TotalScore);
        Assert.Equal(0.5, row.AverageScore);
    }

    [Fact]
    public async Task Grading_BirXilMezonIkkiDarsda_FAQATBIRMARTAsanaladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        AddCriterion(db, g, "Faollik");
        MarkDone(db, g, s, c1, dayOffset: 0);
        MarkDone(db, g, s, c1, dayOffset: -1);

        var rows = await GradingService.CalculateStudentTotalsAsync(
            db.Context, g.Id, AppClock.Today.ToString("yyyy-MM"));

        // Distinct(CriterionId) — bir mezon oyiga bir marta hisoblanadi.
        Assert.Equal(1, rows[0].TotalScore);
    }

    [Fact]
    public async Task Grading_AJRATILGANmezonHAMsanaladi_ORTACHA1danOSHADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        var c2 = AddCriterion(db, g, "Faollik");
        var eski = AddCriterion(db, null, "Guruhdan AJRATILGAN mezon");
        MarkDone(db, g, s, c1);
        MarkDone(db, g, s, c2);
        MarkDone(db, g, s, eski);

        var rows = await GradingService.CalculateStudentTotalsAsync(
            db.Context, g.Id, AppClock.Today.ToString("yyyy-MM"));

        Assert.Equal(2, rows[0].CriteriaCount);
        Assert.Equal(3, rows[0].TotalScore);      // 3 > 2
        Assert.Equal(1.5, rows[0].AverageScore);  // 150%
    }

    [Fact(Skip = "XATO (GradingService.cs:45-48,74): ajratilgan mezon ham sanaladi — o'rtacha 1 dan oshadi")]
    public async Task Grading_AJRATILGANmezon_SANALMASLIGIKERAK()
    {
        // XATO (GradingService.cs:45-48,74): `grades` so'rovi faqat GroupId/Done/oy bo'yicha
        // filtrlanadi, `critIds` bo'yicha EMAS. Mezon guruhdan olib tashlansa (yoki umuman
        // boshqa guruhga tegishli bo'lsa) uning eski "bajardi" belgilari sanalishda qoladi —
        // TotalScore CriteriaCount dan oshib ketadi va AverageScore 1 dan katta bo'ladi
        // (UI'da "150%"). To'g'risi: `grades` ham `critIds.Contains(g.CriterionId)` bilan
        // cheklanishi kerak.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var c1 = AddCriterion(db, g, "Uy vazifa");
        var c2 = AddCriterion(db, g, "Faollik");
        var eski = AddCriterion(db, null, "Guruhdan AJRATILGAN mezon");
        MarkDone(db, g, s, c1);
        MarkDone(db, g, s, c2);
        MarkDone(db, g, s, eski);

        var rows = await GradingService.CalculateStudentTotalsAsync(
            db.Context, g.Id, AppClock.Today.ToString("yyyy-MM"));

        Assert.Equal(2, rows[0].TotalScore);
        Assert.Equal(1.0, rows[0].AverageScore);
    }

    /* =========================================================================================
     *  7) TASDIQLANGAN XATOLAR — kutilgan (to'g'ri) xulq, Skip bilan hujjatlashtirilgan
     * ========================================================================================= */

    [Fact]
    public async Task SetScore_NULLberilsa_BUTUNQATOROCHIRILADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var t = AddOnlineTest(db, g, "ABCD");
        await OnlineTestService.SubmitAsync(db.Context, s.Id, t.Id, "ABCD");

        await TestResultService.SetScoreAsync(db.Context, t.Id, s.Id, null);

        Assert.Empty(db.Context.TestScores);   // javoblar, vaqt va Source — hammasi yo'q bo'ldi
    }

    [Fact(Skip = "XATO (TestResultService.cs:295-298): ball tozalanganda ONLAYN topshiriq ham o'chib ketadi")]
    public async Task SetScore_NULL_ONLAYNTOPSHIRIQSAQLANISHIKERAK()
    {
        // XATO (TestResultService.cs:295-298): `score is null` bo'lganda butun TestScore QATORI
        // o'chiriladi. Onlayn testda o'sha qatorda o'quvchining JAVOBLARI, topshirgan VAQTI va
        // Source="app"/"bot" saqlanadi — ya'ni o'qituvchi ballni tozalasa topshiriq izi butunlay
        // yo'qoladi va o'quvchi testni QAYTA topshira oladi (kalit tarqalgan bo'lsa — 100%).
        // To'g'risi: onlayn topshiriqda faqat Score qayta hisoblanishi/bo'shatilishi, javoblar
        // esa joyida qolishi kerak.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var t = AddOnlineTest(db, g, "ABCD");
        await OnlineTestService.SubmitAsync(db.Context, s.Id, t.Id, "ABCD");

        await TestResultService.SetScoreAsync(db.Context, t.Id, s.Id, null);

        var row = await db.Context.TestScores.SingleAsync();
        Assert.Equal("ABCD", row.Answers);
        Assert.Equal(OnlineTestService.SourceApp, row.Source);
        var (_, err) = await OnlineTestService.SubmitAsync(db.Context, s.Id, t.Id, "ABCD");
        Assert.Contains("allaqachon", err);
    }

    [Fact]
    public async Task Update_SavollarSoniKAMAYTIRILSA_ESKIBALLQOLADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var t = AddOnlineTest(db, g, "ABCDABCDAB");   // 10 savol
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = s.Id, Score = 9 });
        await db.Context.SaveChangesAsync();

        await TestResultService.UpdateAsync(db.Context, t.Id, "Unit 1", Day(0), 10, Online("ABCDA"));

        var fresh = await db.Context.TestResults.SingleAsync();
        Assert.Equal(5, fresh.MaxScore);
        Assert.Equal(9, (await db.Context.TestScores.SingleAsync()).Score);   // 9/5 = 180%
    }

    [Fact(Skip = "XATO (TestResultService.cs:160-190): savollar soni kamaytirilsa eski ballar clamp qilinmaydi")]
    public async Task Update_SavollarSoniKAMAYTIRILSA_BALLARCLAMPQILINISHIKERAK()
    {
        // XATO (TestResultService.cs:160-190): ApplyOnline `t.MaxScore = count` deb yangi maksimalni
        // yozadi, lekin ALLAQACHON qo'yilgan TestScore.Score qiymatlariga tegmaydi. Natijada
        // "9/5 = 180%" kabi natijalar chiqadi va o'rtacha ball/reyting buziladi.
        // To'g'risi: MaxScore kamayganda mavjud ballar yangi maksimalga qisilishi kerak
        // (SetScoreAsync ichidagi Math.Clamp bilan bir xil qoida).
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var s = AddStudent(db, g, "Ali");
        var t = AddOnlineTest(db, g, "ABCDABCDAB");
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = s.Id, Score = 9 });
        await db.Context.SaveChangesAsync();

        await TestResultService.UpdateAsync(db.Context, t.Id, "Unit 1", Day(0), 10, Online("ABCDA"));

        Assert.Equal(5, (await db.Context.TestScores.SingleAsync()).Score);
    }

    [Fact]
    public async Task Update_TestSANASIozgarsa_VAQTOYNASIESKIkunda_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddOnlineTest(db, g, "ABCD");
        var eskiKun = Day(0);

        await TestResultService.UpdateAsync(db.Context, t.Id, "Unit 1", Day(7), 4,
            Online(start: eskiKun + "T09:00", end: eskiKun + "T10:00"));

        var fresh = await db.Context.TestResults.SingleAsync();
        Assert.Equal(Day(7), fresh.Date);
        Assert.StartsWith(eskiKun, fresh.StartAt);   // oyna ESKI kunda qolib ketdi
    }

    [Fact(Skip = "XATO (TestResultService.cs:170-175): test sanasi ko'chirilsa StartAt/EndAt eskisicha qoladi")]
    public async Task Update_TestSANASIozgarsa_VAQTOYNASIHAMKOCHISHIKERAK()
    {
        // XATO (TestResultService.cs:170-175): `start`/`end` faqat BO'SH bo'lgandagina test sanasidan
        // olinadi (`t.Date + "T00:00"`). Forma odatda mavjud vaqtlarni o'zgarishsiz qaytaradi, shuning
        // uchun testni boshqa kunga ko'chirganda javob qabul qilish oynasi ESKI kunda qoladi — test
        // yangi kunda "allaqachon tugagan" bo'lib ko'rinadi va hech kim topshira olmaydi.
        // To'g'risi: sana o'zgarganda oynaning KUN qismi ham yangi sanaga ko'chishi kerak
        // (soat qismi saqlangan holda).
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddOnlineTest(db, g, "ABCD");
        var eskiKun = Day(0);

        await TestResultService.UpdateAsync(db.Context, t.Id, "Unit 1", Day(7), 4,
            Online(start: eskiKun + "T09:00", end: eskiKun + "T10:00"));

        var fresh = await db.Context.TestResults.SingleAsync();
        Assert.StartsWith(Day(7), fresh.StartAt);
        Assert.StartsWith(Day(7), fresh.EndAt);
    }

    [Fact]
    public async Task Bulk_BOSHQAguruhOquvchisi_YOZUVYARATILADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var boshqa = AddGroup(db, "Boshqa guruh", startDate: Day(-60));
        var begona = AddStudent(db, boshqa, "Begona", activatedAt: Day(-30));

        await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId, Day(-1), 1, new List<string> { begona.Id }, null));

        Assert.Equal(1, await db.Context.JournalEntries.CountAsync());
    }

    [Fact(Skip = "XATO (JournalService.cs:432-439): BulkAttendanceAsync guruh a'zoligini TEKSHIRMAYDI")]
    public async Task Bulk_BOSHQAguruhOquvchisi_CHETLABOTILISHIKERAK()
    {
        // XATO (JournalService.cs:432-439): `startById` faqat SHU guruhning a'zolaridan quriladi,
        // keyin esa `!startById.TryGetValue(...)` sharti "lug'atda yo'q" ni "cheklov yo'q" deb
        // talqin qiladi. Natijada so'rovda kelgan HAR QANDAY studentId (boshqa guruh o'quvchisi,
        // hatto o'chirilgan id) uchun jurnal yozuvi yaratiladi — o'qituvchi API orqali begona
        // o'quvchiga davomat qo'ya oladi.
        // To'g'risi: shu guruhning FAOL a'zosi bo'lmagan id'lar chetlab o'tilishi kerak.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var boshqa = AddGroup(db, "Boshqa guruh", startDate: Day(-60));
        var begona = AddStudent(db, boshqa, "Begona", activatedAt: Day(-30));

        await JournalService.BulkAttendanceAsync(db.Context, new BulkAttendanceRequest(
            g.Id, g.CourseId, Day(-1), 1, new List<string> { begona.Id }, null));

        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task SetEntry_CHIQARILGANoquvchigaYOZILADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Chiqarilgan", activatedAt: Day(-30), isActive: false);

        await JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(0), grade: 5));

        Assert.Equal(1, await db.Context.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Rating_DAVOMAT_GURUHNINGBUTUNTARIXIboyicha_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-100));
        var yangi = AddStudent(db, g, "Yangi", activatedAt: Day(-2));
        // Guruhda 10 ta o'tilgan dars bor, ulardan faqat 2 tasi o'quvchi qo'shilgandan keyin.
        for (var i = 1; i <= 10; i++)
            db.Context.LessonNotes.Add(new LessonNote
            {
                ClassId = g.Id, SubjectId = g.CourseId, Quarter = 1,
                Date = Day(-i), Period = 1, Conducted = true,
            });
        var sabab = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.Context.AbsenceReasons.Add(sabab);
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id, SubjectId = g.CourseId, Quarter = 1, StudentId = yangi.Id,
            Date = Day(-1), Period = 1, ReasonId = sabab.Id,
        });
        await db.Context.SaveChangesAsync();

        var rows = await RatingService.SchoolAsync(db.Context);

        // 10 dars, 1 kelmagan → 90%; aslida o'quvchi faqat 2 darsga ulgurgan (1/2 = 50%).
        Assert.Equal(90, rows.Single().Attendance);
    }

    [Fact(Skip = "XATO (RatingService.cs:54-69): davomat a'zolik oynasini hisobga olmaydi")]
    public async Task Rating_DAVOMAT_FAQATAZOLIKOYNASIDAsanalishiKerak()
    {
        // XATO (RatingService.cs:54-69): `conducted += cond.Count` GURUHNING BARCHA o'tilgan
        // darslarini sanaydi — o'quvchi guruhga qo'shilishidan OLDINGI va muzlatilgandan
        // KEYINGI darslar ham maxrajga kiradi. Kelmaganlar esa faqat o'quvchining O'Z
        // yozuvlaridan olinadi, shuning uchun yangi qo'shilgan o'quvchining davomati sun'iy
        // ravishda 100% ga yaqinlashadi va reyting adolatsiz bo'ladi.
        // To'g'risi: jurnal qoidasidagi kabi `held = conducted ∩ (memberStart ≤ sana ≤ frozenAt)`
        // (JournalService.MemberStart / StudentAttendanceController bilan bir xil).
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-100));
        var yangi = AddStudent(db, g, "Yangi", activatedAt: Day(-2));
        for (var i = 1; i <= 10; i++)
            db.Context.LessonNotes.Add(new LessonNote
            {
                ClassId = g.Id, SubjectId = g.CourseId, Quarter = 1,
                Date = Day(-i), Period = 1, Conducted = true,
            });
        var sabab = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.Context.AbsenceReasons.Add(sabab);
        db.Context.JournalEntries.Add(new JournalEntry
        {
            ClassId = g.Id, SubjectId = g.CourseId, Quarter = 1, StudentId = yangi.Id,
            Date = Day(-1), Period = 1, ReasonId = sabab.Id,
        });
        await db.Context.SaveChangesAsync();

        var rows = await RatingService.SchoolAsync(db.Context);

        Assert.Equal(50, rows.Single().Attendance);   // 2 dars, 1 kelmagan
    }

    [Fact]
    public async Task Forecast_FAQATTAKRORIYdars_PROGNOZ10xBUZILADI_HOZIRGIXULQ()
    {
        // HOZIRGI xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        using var db = TestDb.Sqlite();
        var (g, _) = AddCurriculum(db, itemCount: 10);
        db.Context.GroupCurriculumLogs.Add(new GroupCurriculumLog
        {
            GroupId = g.Id, ItemId = "", IsRevision = true, Date = Day(-1),
        });
        await db.Context.SaveChangesAsync();

        var dto = await CurriculumForecast.BuildGroupAsync(db.Context, g);

        // pace = 0/1 = 0 → Math.Max(0, 0.1) = 0.1 → 10 band / 0.1 = 100 dars.
        Assert.Equal(10, dto.RemainingItems);
        Assert.Equal(100, dto.EstLessonsLeft);
    }

    [Fact(Skip = "XATO (CurriculumForecast.cs:68-70): faqat takroriy darsda pace 0.1 ga tushib prognoz 10x buziladi")]
    public async Task Forecast_FAQATTAKRORIYdars_PROGNOZREALBOLISHIKERAK()
    {
        // XATO (CurriculumForecast.cs:68-70): `pace = coveredCount / totalLessons` bo'lib,
        // guruhda hali BIRORTA ham yangi band o'tilmagan (faqat takrorlash darsi bo'lgan)
        // holatda pace = 0 chiqadi va `Math.Max(pace, 0.1)` uni 0.1 ga ko'taradi. Natijada
        // qolgan bandlar 10 BARAVAR ko'p darsga cho'ziladi (10 band → 100 dars) va tugash
        // prognozi bir necha yilga siljiydi — admin/o'qituvchi ko'rsatkichi ishonchsiz bo'ladi.
        // To'g'risi: ma'lumot yetarli bo'lmaganda pace = 1.0 (default) qolishi kerak —
        // ya'ni "har darsda bitta band" taxminiga qaytish, 0.1 ga tushmaslik.
        using var db = TestDb.Sqlite();
        var (g, _) = AddCurriculum(db, itemCount: 10);
        db.Context.GroupCurriculumLogs.Add(new GroupCurriculumLog
        {
            GroupId = g.Id, ItemId = "", IsRevision = true, Date = Day(-1),
        });
        await db.Context.SaveChangesAsync();

        var dto = await CurriculumForecast.BuildGroupAsync(db.Context, g);

        Assert.Equal(10, dto.EstLessonsLeft);
    }

    /// <summary>Guruh + kursiga biriktirilgan o'quv dasturi (1 modul → 1 mavzu → 1 dars →
    /// <paramref name="itemCount"/> ta band). Qaytaradi: guruh va band id'lari.</summary>
    private static (Group Group, List<string> ItemIds) AddCurriculum(TestDb db, int itemCount)
    {
        var g = AddGroup(db, days: new[] { 0, 2, 4 });
        var cur = new Curriculum { Name = "A1 dasturi" };
        db.Context.Curricula.Add(cur);
        db.Context.SubjectCurricula.Add(new SubjectCurriculum { SubjectId = g.CourseId, CurriculumId = cur.Id });
        var mod = new CourseModule { CurriculumId = cur.Id, Name = "Modul 1" };
        db.Context.CourseModules.Add(mod);
        var topic = new CourseTopic { CurriculumId = cur.Id, ModuleId = mod.Id, Title = "Mavzu 1" };
        db.Context.CourseTopics.Add(topic);
        var lesson = new CourseLesson { CurriculumId = cur.Id, TopicId = topic.Id, Title = "Dars 1" };
        db.Context.CourseLessons.Add(lesson);
        var ids = new List<string>();
        for (var i = 0; i < itemCount; i++)
        {
            var item = new CourseItem { CurriculumId = cur.Id, LessonId = lesson.Id, Text = $"Band {i + 1}", Order = i };
            db.Context.CourseItems.Add(item);
            ids.Add(item.Id);
        }
        db.Context.SaveChanges();
        return (g, ids);
    }

    [Fact(Skip = "XATO (JournalService.cs:277-281): muzlatilgan/chiqarilgan o'quvchiga yozish ochiq")]
    public async Task SetEntry_CHIQARILGANoquvchiga_TAQIQLANISHIKERAK()
    {
        // XATO (JournalService.cs:277-281): a'zolik `sg.IsActive` sharti bilan qidiriladi. Guruhdan
        // CHIQARILGAN (IsActive=false) yoki MUZLATILGAN (Status="frozen") o'quvchida so'rov null
        // qaytaradi, `memberStart` esa null bo'lib "cheklov yo'q" degan ma'noni beradi — natijada
        // eng qattiq holat eng OCHIQ holatga aylanadi va guruhda ko'rinmaydigan o'quvchiga baho
        // qo'yib bo'ladi (jurnalda ko'rinmaydi, lekin o'rtacha bahoga/reytingga ta'sir qiladi).
        // To'g'risi: faol a'zoligi bo'lmagan o'quvchiga yozish rad etilishi kerak.
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, startDate: Day(-60));
        var s = AddStudent(db, g, "Chiqarilgan", activatedAt: Day(-30), isActive: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JournalService.SetEntryAsync(db.Context, Entry(g, s, Day(0), grade: 5)));

        Assert.Empty(db.Context.JournalEntries);
    }
}
