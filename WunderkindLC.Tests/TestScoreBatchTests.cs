using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM U4a — result-only test entry (docs/SPEC-crm-upgrade.md P4.2/P4.7): kind + «Savollar soni», the grid batch save
/// that REJECTS invalid values (never clamps), percent / rank, month / kind filters, overview and audit.
/// </summary>
public class TestScoreBatchTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static AuditService Audit(TestDb db) => new(db.Context, new NoHttpContext());

    private static string Day(int offset) => AppClock.Today.AddDays(offset).ToString("yyyy-MM-dd");

    private static Group AddGroup(TestDb db, string name = "Ingliz A1", string teacherId = "")
    {
        var subject = new Subject { Name = "Ingliz tili" };
        db.Context.Subjects.Add(subject);
        var g = new Group { Name = name, CourseId = subject.Id, TeacherId = teacherId };
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return g;
    }

    private static Student AddStudent(TestDb db, Group? g, string fullName, string status = "active", bool isActive = true)
    {
        var s = new Student { FullName = fullName };
        db.Context.Students.Add(s);
        if (g is not null)
            db.Context.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, IsActive = isActive, Status = status });
        db.Context.SaveChanges();
        return s;
    }

    private static TestResult AddTest(TestDb db, Group g, int questions = 30, decimal? maxScore = null,
        string kind = TestEntryRules.Interim, string? date = null)
    {
        var t = new TestResult
        {
            GroupId = g.Id, Name = "Unit 5", Date = date ?? Day(0), Kind = kind,
            QuestionCount = questions, MaxScore = maxScore ?? questions,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.Context.TestResults.Add(t);
        db.Context.SaveChanges();
        return t;
    }

    // ===================== pure rules =====================

    [Fact]
    public void Yechgan_0_dan_savollar_sonigacha_BUTUN_son_tashqarisi_RAD_qisilmaydi()
    {
        Assert.Null(TestEntryRules.ValidateSolved(null, 30));   // null clears
        Assert.Null(TestEntryRules.ValidateSolved(0, 30));
        Assert.Null(TestEntryRules.ValidateSolved(30, 30));
        Assert.Equal("Natija 0 dan 30 gacha butun son bo'lsin", TestEntryRules.ValidateSolved(31, 30));
        Assert.Equal("Natija 0 dan 30 gacha butun son bo'lsin", TestEntryRules.ValidateSolved(-1, 30));
        Assert.Equal("Natija 0 dan 30 gacha butun son bo'lsin", TestEntryRules.ValidateSolved(12.5m, 30));
        // Old test (no «Savollar soni»): the max score is the range, still whole numbers only.
        Assert.Equal(37, TestEntryRules.MaxSolved(0, 37.5m));
        Assert.Equal(30, TestEntryRules.MaxSolved(30, 30m));
    }

    [Fact]
    public void Yangi_test_turi_va_savollar_soni_SHART()
    {
        Assert.Equal(TestEntryRules.NameRequired, TestEntryRules.ValidateNew(" ", "monthly", 30));
        Assert.Equal(TestEntryRules.KindRequired, TestEntryRules.ValidateNew("Unit 5", null, 30));
        Assert.Equal(TestEntryRules.KindRequired, TestEntryRules.ValidateNew("Unit 5", "weekly", 30));
        Assert.Equal(TestEntryRules.QuestionsRange, TestEntryRules.ValidateNew("Unit 5", "monthly", null));
        Assert.Equal(TestEntryRules.QuestionsRange, TestEntryRules.ValidateNew("Unit 5", "monthly", 0));
        Assert.Equal(TestEntryRules.QuestionsRange, TestEntryRules.ValidateNew("Unit 5", "monthly", 201));
        Assert.Null(TestEntryRules.ValidateNew("Unit 5", " Interim ", 30));
        Assert.Equal("Oylik", TestEntryRules.KindLabel("monthly"));
        Assert.Equal("Oraliq", TestEntryRules.KindLabel("interim"));
        Assert.Equal("", TestEntryRules.KindLabel(""));
    }

    [Fact]
    public void Foiz_bir_kasr_va_orin_musobaqa_usulida()
    {
        Assert.Equal(83.3, TestEntryRules.Percent(25, 30));
        Assert.Equal(100, TestEntryRules.Percent(30, 30));
        Assert.Null(TestEntryRules.Percent(5, 0));
        var ranks = TestEntryRules.Ranks([("a", 30m), ("b", 25m), ("c", 25m), ("d", 20m)]);
        Assert.Equal(new[] { 1, 2, 2, 4 }, new[] { ranks["a"], ranks["b"], ranks["c"], ranks["d"] });
        Assert.Equal("2026-10", TestEntryRules.NormalizeMonth("2026-10"));
        Assert.Null(TestEntryRules.NormalizeMonth("2026-13"));
        Assert.Equal(("2026-02-01", "2026-02-28"), TestEntryRules.MonthRange("2026-02"));
    }

    [Fact]
    public void Savollar_sonini_kamaytirish_natija_katta_bolsa_RAD()
    {
        Assert.Null(TestEntryRules.ValidateQuestionLowering(25, [25m, 20m]));
        Assert.Equal("2 ta o'quvchining natijasi yangi savollar sonidan katta",
            TestEntryRules.ValidateQuestionLowering(20, [25m, 21m, 20m]));
    }

    // ===================== batch save =====================

    [Fact]
    public async Task Batch_togri_qatorlar_saqlanadi_notogrisi_SAQLANMAYDI_va_QISILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali");
        var bek = AddStudent(db, g, "Bek");
        var vali = AddStudent(db, g, "Vali");
        var zafar = AddStudent(db, g, "Zafar");
        var stranger = AddStudent(db, null, "Begona");
        var frozen = AddStudent(db, g, "Muzlagan", status: "frozen");
        var t = AddTest(db, g, questions: 30);
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = vali.Id, Score = 10 });
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = zafar.Id, Score = 18 });
        db.Context.SaveChanges();

        var (res, err) = await TestResultService.SaveScoresBatchAsync(db.Context, t.Id,
        [
            new(ali.Id, 25),
            new(bek.Id, 31),             // out of range — red, not saved, NOT clamped to 30
            new(vali.Id, 12.5m),         // fractional — rejected, the old 10 stays
            new(zafar.Id, null),         // clears
            new(stranger.Id, 5),         // not a member
            new(frozen.Id, 5),           // frozen member — not a grid row
        ]);

        Assert.Null(err);
        Assert.NotNull(res);
        Assert.Equal(new[] { bek.Id, vali.Id, stranger.Id, frozen.Id }.OrderBy(x => x),
            res!.Errors.Select(e => e.StudentId).OrderBy(x => x));
        Assert.Equal("Natija 0 dan 30 gacha butun son bo'lsin", res.Errors.First(e => e.StudentId == bek.Id).Message);
        Assert.Equal(TestEntryRules.NotMember, res.Errors.First(e => e.StudentId == stranger.Id).Message);

        var saved = db.Context.TestScores.AsNoTracking().Where(s => s.TestResultId == t.Id)
            .ToDictionary(s => s.StudentId, s => s.Score);
        Assert.Equal(25, saved[ali.Id]);
        Assert.Equal(10, saved[vali.Id]);
        Assert.False(saved.ContainsKey(bek.Id));
        Assert.False(saved.ContainsKey(zafar.Id));

        var ali1 = res.Detail.Rows.Single(r => r.StudentId == ali.Id);
        Assert.Equal(83.3, ali1.Percent);
        Assert.Equal(1, ali1.Rank);
        Assert.Equal(33.3, res.Detail.Rows.Single(r => r.StudentId == vali.Id).Percent);
        Assert.Equal(2, res.Detail.Rows.Single(r => r.StudentId == vali.Id).Rank);
        Assert.Equal(30, res.Detail.QuestionCount);
    }

    [Fact]
    public async Task Batch_guruhdan_chiqqanning_eski_natijasini_tuzatsa_boladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var left = AddStudent(db, g, "Chiqqan", isActive: false);
        var t = AddTest(db, g);
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = left.Id, Score = 20 });
        db.Context.SaveChanges();

        var (res, _) = await TestResultService.SaveScoresBatchAsync(db.Context, t.Id, [new(left.Id, 22)]);

        Assert.Empty(res!.Errors);
        Assert.Equal(22, db.Context.TestScores.AsNoTracking().Single().Score);
        Assert.False(res.Detail.Rows.Single().Member);
    }

    [Fact]
    public async Task Batch_eski_testda_0_dan_maks_ballgacha_butun_son()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali");
        var bek = AddStudent(db, g, "Bek");
        var t = AddTest(db, g, questions: 0, maxScore: 100, kind: "");

        var (res, _) = await TestResultService.SaveScoresBatchAsync(db.Context, t.Id,
            [new(ali.Id, 100), new(bek.Id, 50.5m)]);

        Assert.Equal(bek.Id, Assert.Single(res!.Errors).StudentId);
        Assert.Equal("Natija 0 dan 100 gacha butun son bo'lsin", res.Errors[0].Message);
        Assert.Equal(100, db.Context.TestScores.AsNoTracking().Single().Score);
    }

    [Fact]
    public async Task Batch_topilmagan_test_va_juda_kop_qator()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var t = AddTest(db, g);
        Assert.Equal("Test topilmadi", (await TestResultService.SaveScoresBatchAsync(db.Context, "x", [])).Error);
        var many = Enumerable.Range(0, 501).Select(i => new TestScoreBatchRow("s" + i, 1)).ToList();
        Assert.Equal(TestEntryRules.TooManyRows, (await TestResultService.SaveScoresBatchAsync(db.Context, t.Id, many)).Error);
    }

    // ===================== create / update / delete =====================

    [Fact]
    public async Task Yaratish_turi_va_savollar_soni_bilan_MaxScore_teng_tarixga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        AddStudent(db, g, "Ali");

        var (none, noKind) = await TestResultService.CreateEntryAsync(db.Context,
            new CreateTestResultRequest(g.Id, "Unit 5", Day(0), 0), "admin", Audit(db));
        Assert.Null(none);
        Assert.Equal(TestEntryRules.KindRequired, noKind);
        var (_, noQuestions) = await TestResultService.CreateEntryAsync(db.Context,
            new CreateTestResultRequest(g.Id, "Unit 5", Day(0), 0, Kind: "monthly"), "admin", Audit(db));
        Assert.Equal(TestEntryRules.QuestionsRange, noQuestions);
        var (_, badDate) = await TestResultService.CreateEntryAsync(db.Context,
            new CreateTestResultRequest(g.Id, "Unit 5", "12.10.2026", 0, Kind: "monthly", QuestionCount: 30), "admin", Audit(db));
        Assert.Equal(TestEntryRules.DateInvalid, badDate);
        Assert.Empty(db.Context.TestResults);

        var (dto, err) = await TestResultService.CreateEntryAsync(db.Context,
            new CreateTestResultRequest(g.Id, " Unit 5 ", Day(0), 999, Kind: "interim", QuestionCount: 30), "admin", Audit(db));

        Assert.Null(err);
        Assert.Equal("interim", dto!.Kind);
        Assert.Equal(30, dto.QuestionCount);
        Assert.Equal(30, dto.MaxScore);   // the client's maxScore is ignored
        Assert.Equal(1, dto.StudentCount);
        var log = Assert.Single(db.Context.AuditLogs.AsNoTracking());
        Assert.Equal(TestResultService.AuditEntity, log.EntityType);
        Assert.Equal($"{g.Id}:{dto.Id}", log.EntityId);
        Assert.Equal("create", log.Action);
        Assert.Equal("classes", AuditSections.SectionOf(log.EntityType));
        Assert.Contains("Unit 5 (Oraliq", log.Summary);
    }

    [Fact]
    public async Task Tahrir_savollar_sonini_natijadan_past_qilish_RAD_teng_yoki_yuqori_mumkin()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali");
        var t = AddTest(db, g, questions: 30);
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = ali.Id, Score = 25 });
        db.Context.SaveChanges();

        var r = await TestResultService.UpdateEntryAsync(db.Context, t.Id,
            new UpdateTestResultRequest("Unit 5", Day(0), 0, Kind: "monthly", QuestionCount: 20), Audit(db));
        Assert.False(r.Ok);
        Assert.Equal("1 ta o'quvchining natijasi yangi savollar sonidan katta", r.Error);
        Assert.Equal(25, db.Context.TestScores.AsNoTracking().Single().Score);   // never clamped

        r = await TestResultService.UpdateEntryAsync(db.Context, t.Id,
            new UpdateTestResultRequest("Unit 5b", Day(0), 0, Kind: "monthly", QuestionCount: 25), Audit(db));
        Assert.True(r.Ok);
        var saved = db.Context.TestResults.AsNoTracking().Single();
        Assert.Equal((25, 25m, "monthly", "Unit 5b"), (saved.QuestionCount, saved.MaxScore, saved.Kind, saved.Name));

        // Not sent → the count is kept; the max score is ignored for a question-based test.
        r = await TestResultService.UpdateEntryAsync(db.Context, t.Id,
            new UpdateTestResultRequest("Unit 5b", Day(0), 7), Audit(db));
        Assert.True(r.Ok);
        Assert.Equal(25, db.Context.TestResults.AsNoTracking().Single().MaxScore);
        Assert.Equal(2, db.Context.AuditLogs.Count(l => l.EntityType == TestResultService.AuditEntity && l.Action == "update"));
    }

    [Fact]
    public async Task Ochirish_tarixga_natijalar_soni_bilan()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db);
        var ali = AddStudent(db, g, "Ali");
        var t = AddTest(db, g);
        db.Context.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = ali.Id, Score = 5 });
        db.Context.SaveChanges();

        Assert.True(await TestResultService.DeleteEntryAsync(db.Context, t.Id, Audit(db)));
        Assert.Empty(db.Context.TestResults);
        var log = Assert.Single(db.Context.AuditLogs.AsNoTracking());
        Assert.Equal("delete", log.Action);
        Assert.Contains("1 ta natija bilan", log.Summary);
    }

    // ===================== filters / overview =====================

    [Fact]
    public async Task Oy_va_tur_filtri_va_markaz_kesimi()
    {
        using var db = TestDb.Sqlite();
        var g = AddGroup(db, teacherId: "t-1");
        var ali = AddStudent(db, g, "Ali");
        var now = AddTest(db, g, kind: TestEntryRules.Monthly, date: "2026-09-15");
        AddTest(db, g, kind: TestEntryRules.Interim, date: "2026-09-30");
        AddTest(db, g, kind: TestEntryRules.Monthly, date: "2026-08-31");
        db.Context.TestScores.Add(new TestScore { TestResultId = now.Id, StudentId = ali.Id, Score = 15 });
        db.Context.SaveChanges();

        Assert.Equal(2, (await TestResultService.ListForGroupAsync(db.Context, g.Id, "2026-09")).Count);
        var monthly = await TestResultService.ListForGroupAsync(db.Context, g.Id, "2026-09", "monthly");
        var one = Assert.Single(monthly);
        Assert.Equal(50, one.AvgPercent);
        Assert.Equal(3, (await TestResultService.ListForGroupAsync(db.Context, g.Id, "bad", "bad")).Count);

        var groups = await TestResultService.GroupsOverviewAsync(db.Context, "2026-09");
        Assert.Equal(2, groups.Single().MonthTestCount);
        Assert.Equal(50, groups.Single().MonthAvgPercent);
        Assert.Equal(3, groups.Single().TestCount);

        var overview = await TestResultService.OverviewAsync(db.Context, "2026-09", null, null, "t-1");
        Assert.Equal(new[] { "2026-09-30", "2026-09-15" }, overview.Select(o => o.Date));
        Assert.Equal((1, 1), (overview[1].Scored, overview[1].Members));
        Assert.Empty(await TestResultService.OverviewAsync(db.Context, "2026-09", null, null, "t-2"));
    }
}
