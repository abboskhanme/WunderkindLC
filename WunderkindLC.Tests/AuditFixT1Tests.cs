using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 audit tuzatishlari — GURUHLAR va A'ZOLIKLAR (T1): guruhni o'chirish (pul himoyasi +
/// o'quv ma'lumotini tozalash), guruh hayotida o'rinbosarlar, guruh almashganda chegirma, guruh
/// nomi noyobligi va qayta nomlash, arxivdagi o'quvchi/guruh qoidalari, «Tugatish» qoidalari.
///
/// <para>Test loyihasi Server'ga bog'lanmagan — controller oqimi manba matni bo'yicha qulflanadi
/// (<see cref="GroupClosingTests"/> naqshi), mantiqning o'zi esa Application yordamchilarida.</para>
/// </summary>
public class AuditFixT1Tests
{
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");
    private static string D(int days) => AppClock.Today.AddDays(days).ToString("yyyy-MM-dd");

    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    private static Group AddGroup(AppDbContext ctx, string name = "A guruh", decimal fee = 600_000m,
        string courseId = "", string teacherId = "", bool archived = false)
    {
        var g = new Group
        {
            Name = name, MonthlyFee = fee, CourseId = courseId, TeacherId = teacherId, IsArchived = archived,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Classes.Add(g);
        return g;
    }

    private static Student AddStudent(AppDbContext ctx, string name = "Test O'quvchi", bool archived = false,
        string className = "")
    {
        var s = new Student { FullName = name, EnrollmentDate = $"{M(-6)}-01", IsArchived = archived, ClassName = className };
        ctx.Students.Add(s);
        return s;
    }

    private static Teacher AddTeacher(AppDbContext ctx, string name)
    {
        var t = new Teacher { FullName = name };
        ctx.Teachers.Add(t);
        return t;
    }

    private static StudentGroup AddMember(AppDbContext ctx, Student s, Group g, string status = "active",
        bool isActive = true)
    {
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = isActive,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = status == "trial" ? "" : $"{M(-3)}-01",
            FrozenAt = status == "frozen" ? $"{M(-1)}-10" : "", RecordedAt = $"{M(-3)}-01",
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    private static void AddCharge(AppDbContext ctx, Student s, string? groupId, string month, decimal amount) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = groupId, Month = month, Amount = amount, Date = $"{month}-01",
        });

    private static FinanceTransaction AddPayment(AppDbContext ctx, Student s, string? groupId, string month,
        decimal amount, string direction = "income", string category = "tuition")
    {
        var t = new FinanceTransaction
        {
            Date = $"{month}-10", Direction = direction, Category = category, Amount = amount,
            StudentId = s.Id, GroupId = groupId, Month = month, Method = "cash",
        };
        ctx.FinanceTransactions.Add(t);
        return t;
    }

    // =====================================================================================
    //  C2 — GURUHNI O'CHIRISH: pul himoyasi
    // =====================================================================================

    [Fact]
    public async Task Ochirish_guruhga_TEGLANGAN_tolov_bolsa_RAD_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        AddPayment(ctx, s, g.Id, M(-1), 600_000m);
        await ctx.SaveChangesAsync();

        Assert.Equal(GroupDeletion.HasPaymentsMessage, await GroupDeletion.MoneyBlockReasonAsync(ctx, g.Id));
    }

    [Fact]
    public async Task Ochirish_VOZVRAT_ham_pul_tarixi__rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        AddPayment(ctx, s, g.Id, M(-1), 100_000m, direction: "expense", category: "refund");
        await ctx.SaveChangesAsync();

        Assert.NotNull(await GroupDeletion.MoneyBlockReasonAsync(ctx, g.Id));
    }

    [Fact]
    public async Task Ochirish_faqat_TOLANMAGAN_hisob_bolsa_ruxsat__qarz_balansdan_olinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        s.Balance = -600_000m;
        await ctx.SaveChangesAsync();

        Assert.Null(await GroupDeletion.MoneyBlockReasonAsync(ctx, g.Id));
        var credit = await MembershipBilling.CreditAndDropGroupChargesAsync(ctx, g.Id);
        await ctx.SaveChangesAsync();
        Assert.Equal(600_000m, credit.Credited);
        Assert.Equal(0m, s.Balance);     // soxta qarz qolmadi, soxta AVANS ham paydo bo'lmadi
    }

    [Fact]
    public async Task Ochirish_BEKOR_qilingan_tolov_tosiq_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        var t = AddPayment(ctx, s, g.Id, M(-1), 600_000m);
        t.IsVoided = true;
        await ctx.SaveChangesAsync();

        Assert.Null(await GroupDeletion.MoneyBlockReasonAsync(ctx, g.Id));
    }

    [Fact]
    public async Task Ochirish_ESKI_teglanmagan_tolov_shu_guruh_hisobini_yopgan_bolsa_ham_rad_etiladi()
    {
        // Eski (GroupId = null) to'lov per-guruh balans qoidasi bo'yicha shu guruhga tushadi —
        // hisobni balansga qaytarish yana soxta avans yasardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        AddMember(ctx, s, g, "active");
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        AddPayment(ctx, s, null, M(-1), 600_000m);
        await ctx.SaveChangesAsync();

        Assert.Equal(GroupDeletion.HasPaymentsMessage, await GroupDeletion.MoneyBlockReasonAsync(ctx, g.Id));
    }

    // =====================================================================================
    //  LRN-8 — GURUHNI O'CHIRISH: o'quv/yordamchi ma'lumot ham o'chadi
    // =====================================================================================

    [Fact]
    public async Task Ochirishda_baholar_ball_testlar_sertifikatlar_dastur_chat_TOZALANADI__boshqa_guruh_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var g = AddGroup(ctx, "O'chadigan", teacherId: t.Id);
        var other = AddGroup(ctx, "Qoladigan", teacherId: t.Id);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        foreach (var gid in new[] { g.Id, other.Id })
        {
            ctx.CriterionGrades.Add(new CriterionGrade { GroupId = gid, StudentId = s.Id, CriterionId = "c1", Date = D(-3), Done = true });
            ctx.StudentBallAdjustments.Add(new StudentBallAdjustment { GroupId = gid, StudentId = s.Id, Delta = 5, Reason = "x" });
            ctx.GroupGradingCriteria.Add(new GroupGradingCriterion { GroupId = gid, CriterionId = "c1" });
            ctx.LessonReschedules.Add(new LessonReschedule { ClassId = gid, FromDate = D(-5), ToDate = D(-4) });
            ctx.GroupCurriculumLogs.Add(new GroupCurriculumLog { GroupId = gid, ItemId = "i1", Date = D(-2) });
            ctx.GroupAiAnalyses.Add(new GroupAiAnalysis { GroupId = gid, Date = D(-1) });
            ctx.MembershipReasonLogs.Add(new MembershipReasonLog { GroupId = gid, StudentId = s.Id, Action = "remove", Date = D(-1), ReasonLabel = "x" });
            ctx.GroupTeacherAssignments.Add(new GroupTeacherAssignment { GroupId = gid, TeacherId = t.Id, FromDate = D(-30) });
        }
        var test = new TestResult { GroupId = g.Id, Name = "Unit 1", Date = D(-3), MaxScore = 10, PdfUrl = "/uploads/q1.pdf" };
        var keptTest = new TestResult { GroupId = other.Id, Name = "Unit 1", Date = D(-3), MaxScore = 10 };
        ctx.TestResults.AddRange(test, keptTest);
        await ctx.SaveChangesAsync();
        ctx.TestScores.Add(new TestScore { TestResultId = test.Id, StudentId = s.Id, Score = 7 });
        ctx.TestScores.Add(new TestScore { TestResultId = keptTest.Id, StudentId = s.Id, Score = 8 });
        ctx.TestCertificates.Add(new TestCertificate
        {
            TestResultId = test.Id, StudentId = s.Id, DocxUrl = "/uploads/certificates/a.docx", PdfUrl = "/uploads/certificates/a.pdf",
        });
        ctx.ExternalTestScores.Add(new ExternalTestScore { TestResultId = test.Id, ChatId = 5, FullName = "Tashqi" });
        ctx.TestBotSessions.Add(new TestBotSession { ChatId = 77, TestResultId = test.Id, StudentId = s.Id });
        ctx.ChatMessages.Add(new ChatMessage { ClassName = g.Name, Text = "salom" });
        ctx.ChatMessages.Add(new ChatMessage { ClassName = other.Name, Text = "salom" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var group = await ctx.Classes.FirstAsync(x => x.Id == g.Id);
        var res = await GroupDeletion.PurgeLearningDataAsync(ctx, group, "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Tests);
        Assert.Contains("/uploads/certificates/a.docx", res.FileUrls);
        Assert.Contains("/uploads/certificates/a.pdf", res.FileUrls);
        Assert.Contains("/uploads/q1.pdf", res.FileUrls);

        Assert.Empty(await ctx.CriterionGrades.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.StudentBallAdjustments.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.GroupGradingCriteria.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.LessonReschedules.Where(x => x.ClassId == g.Id).ToListAsync());
        Assert.Empty(await ctx.GroupCurriculumLogs.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.GroupAiAnalyses.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.MembershipReasonLogs.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.GroupTeacherAssignments.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.TestResults.Where(x => x.GroupId == g.Id).ToListAsync());
        Assert.Empty(await ctx.TestCertificates.ToListAsync());
        Assert.Empty(await ctx.ExternalTestScores.ToListAsync());
        Assert.Empty(await ctx.TestBotSessions.ToListAsync());
        Assert.Single(await ctx.TestScores.ToListAsync());               // boshqa guruh testi bali qoldi
        Assert.Empty(await ctx.ChatMessages.Where(m => m.ClassName == g.Name).ToListAsync());

        // Boshqa guruhning hammasi joyida.
        Assert.Single(await ctx.CriterionGrades.ToListAsync());
        Assert.Single(await ctx.StudentBallAdjustments.ToListAsync());
        Assert.Single(await ctx.LessonReschedules.ToListAsync());
        Assert.Single(await ctx.TestResults.ToListAsync());
        Assert.Single(await ctx.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task Ochirishda_shu_guruhga_qaratilgan_AMALDAGI_chegirma_bekor_qilinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount { StudentId = s.Id, GroupId = g.Id, Pct = 20, CreatedAt = AppClock.Iso() });
        await ctx.SaveChangesAsync();

        var res = await GroupDeletion.PurgeLearningDataAsync(ctx, g, "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.DiscountsCancelled);
        var d = await ctx.StudentDiscounts.AsNoTracking().SingleAsync();
        Assert.Equal(StudentDiscount.StatusCancelled, d.Status);
        Assert.Equal(0, (await ctx.Students.AsNoTracking().SingleAsync()).DiscountPct);   // ko'zgu yangilandi
    }

    [Fact]
    public async Task Ochirishda_nomdosh_BOSHQA_guruh_bolsa_chat_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, "Eski nom");
        AddGroup(ctx, "Eski nom");   // prod'dagi eski dublikat
        ctx.ChatMessages.Add(new ChatMessage { ClassName = "Eski nom", Text = "salom" });
        await ctx.SaveChangesAsync();

        await GroupDeletion.PurgeLearningDataAsync(ctx, g, "Admin");
        await ctx.SaveChangesAsync();

        Assert.Single(await ctx.ChatMessages.ToListAsync());
    }

    // =====================================================================================
    //  SAL-1 / C3 — O'RINBOSARLAR GURUH HAYOTIDA
    // =====================================================================================

    [Fact]
    public void SplitDates_yopish_kuni_birinchi_qismda__almashuv_kuni_ikkinchi_qismda()
    {
        var dates = new[] { "2026-10-03", "2026-10-01", "2026-10-05", "2026-10-03" };
        var (b1, a1) = SubstituteGroupLifecycle.SplitDates(dates, "2026-10-03", keepCutoff: true);
        Assert.Equal(new[] { "2026-10-01", "2026-10-03" }, b1);
        Assert.Equal(new[] { "2026-10-05" }, a1);
        var (b2, a2) = SubstituteGroupLifecycle.SplitDates(dates, "2026-10-03", keepCutoff: false);
        Assert.Equal(new[] { "2026-10-01" }, b2);
        Assert.Equal(new[] { "2026-10-03", "2026-10-05" }, a2);
    }

    [Fact]
    public async Task Guruh_yopilganda_yopish_kunidan_KEYINGI_orinbosar_sanalari_olib_tashlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var owner = AddTeacher(ctx, "Asosiy");
        var sub = AddTeacher(ctx, "O'rinbosar");
        var g = AddGroup(ctx, teacherId: owner.Id);
        var mixed = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = owner.Id, SubstituteTeacherId = sub.Id,
            SelectedDates = new List<string> { D(-2), D(0), D(2), D(4) }, Date = D(-2), EndDate = D(4),
        };
        var future = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = owner.Id, SubstituteTeacherId = sub.Id,
            SelectedDates = new List<string> { D(5), D(6) }, Date = D(5), EndDate = D(6),
        };
        ctx.SubstituteTeacherAssignments.AddRange(mixed, future);
        await ctx.SaveChangesAsync();

        var res = await SubstituteGroupLifecycle.TrimAfterAsync(ctx, Audit(ctx), g, D(0), "guruh yopildi");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Trimmed);
        Assert.Equal(1, res.Cancelled);
        var m = await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(a => a.Id == mixed.Id);
        Assert.Equal(new[] { D(-2), D(0) }, m.SelectedDates);   // yopish kuni — oxirgi dars, qoladi
        Assert.Equal(D(-2), m.Date);
        Assert.Equal(D(0), m.EndDate);
        Assert.True(m.IsActive);
        Assert.False((await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(a => a.Id == future.Id)).IsActive);

        var logs = await ctx.AuditLogs.AsNoTracking().Where(a => a.EntityType == "substitute_teacher").ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.StartsWith(g.Id + ":", l.EntityId));   // guruh "Tarix" tabida ko'rinadi
        Assert.All(logs, l => Assert.Equal(sub.Id, l.TeacherId));
    }

    [Fact]
    public async Task Oqituvchi_almashganda__yangi_ega_ozi_orinbosar_bolsa_kelajak_sanalari_olib_tashlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var oldOwner = AddTeacher(ctx, "Eski");
        var newOwner = AddTeacher(ctx, "Yangi");
        var g = AddGroup(ctx, teacherId: oldOwner.Id);
        var a = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = oldOwner.Id, SubstituteTeacherId = newOwner.Id,
            SelectedDates = new List<string> { D(-1), D(0), D(3) }, Date = D(-1), EndDate = D(3),
        };
        ctx.SubstituteTeacherAssignments.Add(a);
        await ctx.SaveChangesAsync();

        var res = await SubstituteGroupLifecycle.OnTeacherChangedAsync(ctx, Audit(ctx), g, newOwner.Id, D(0), "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Trimmed);
        var saved = await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(new[] { D(-1) }, saved.SelectedDates);   // o'zi o'qitadigan kunlarga qo'sh haq yo'q
        Assert.Equal(oldOwner.Id, saved.OriginalTeacherId);
    }

    [Fact]
    public async Task Oqituvchi_almashganda__aralash_tayinlov_IKKIGA_bolinadi__kelajak_yangi_egaga()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var oldOwner = AddTeacher(ctx, "Eski");
        var newOwner = AddTeacher(ctx, "Yangi");
        var sub = AddTeacher(ctx, "O'rinbosar");
        var g = AddGroup(ctx, teacherId: oldOwner.Id);
        var mixed = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = oldOwner.Id, SubstituteTeacherId = sub.Id,
            SelectedDates = new List<string> { D(-2), D(1) }, Date = D(-2), EndDate = D(1),
        };
        var future = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = oldOwner.Id, SubstituteTeacherId = sub.Id,
            SelectedDates = new List<string> { D(4) }, Date = D(4), EndDate = D(4),
        };
        ctx.SubstituteTeacherAssignments.AddRange(mixed, future);
        await ctx.SaveChangesAsync();

        var res = await SubstituteGroupLifecycle.OnTeacherChangedAsync(ctx, Audit(ctx), g, newOwner.Id, D(0), "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Split);
        Assert.Equal(1, res.Reassigned);
        var rows = await ctx.SubstituteTeacherAssignments.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        var pastPart = rows.Single(r => r.Id == mixed.Id);
        Assert.Equal(new[] { D(-2) }, pastPart.SelectedDates);
        Assert.Equal(oldOwner.Id, pastPart.OriginalTeacherId);     // o'tmishdagi ushlanma eski egadan
        var futurePart = rows.Single(r => r.Id != mixed.Id && r.Id != future.Id);
        Assert.Equal(new[] { D(1) }, futurePart.SelectedDates);
        Assert.Equal(newOwner.Id, futurePart.OriginalTeacherId);
        Assert.True(futurePart.IsActive);
        Assert.Equal(newOwner.Id, rows.Single(r => r.Id == future.Id).OriginalTeacherId);
    }

    [Fact]
    public async Task Guruh_ochirilganda_orinbosar_tayinlovlari_ochiriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var owner = AddTeacher(ctx, "Asosiy");
        var sub = AddTeacher(ctx, "O'rinbosar");
        var g = AddGroup(ctx, teacherId: owner.Id);
        ctx.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = owner.Id, SubstituteTeacherId = sub.Id,
            SelectedDates = new List<string> { D(1) }, Date = D(1), EndDate = D(1),
        });
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await SubstituteGroupLifecycle.RemoveAllAsync(ctx, Audit(ctx), g));
        await ctx.SaveChangesAsync();
        Assert.Empty(await ctx.SubstituteTeacherAssignments.ToListAsync());
    }

    // =====================================================================================
    //  FIN-3 — GURUH ALMASHGANDA CHEGIRMA KO'CHADI (bir xil fan)
    // =====================================================================================

    [Fact]
    public async Task Chegirma_bir_xil_fanda_yangi_guruhga_kochadi_va_yangi_guruh_hisobiga_qollanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        var from = AddGroup(ctx, "Ingliz A", courseId: "eng", teacherId: t.Id);
        var to = AddGroup(ctx, "Ingliz B", courseId: "eng", teacherId: t.Id);
        var s = AddStudent(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, GroupId = from.Id, GroupName = from.Name, CourseId = "eng", CourseName = "Ingliz tili",
            Pct = 20, CreatedAt = AppClock.Iso(),
        });
        await ctx.SaveChangesAsync();

        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, from, to, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, res.Moved);
        // audit R1-3: qator JOYIDA ko'chmaydi — IKKIGA bo'linadi (eski qamrov o'tkazish oyi bilan tugaydi).
        var rows = await ctx.StudentDiscounts.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        var d = rows.Single(x => x.GroupId == to.Id);
        Assert.Equal(to.Name, d.GroupName);
        Assert.Equal(StudentDiscount.StatusActive, d.Status);
        Assert.Equal(M(0), d.StartMonth);
        Assert.Equal(M(0), rows.Single(x => x.GroupId == from.Id).EndMonth);
        var book = await DiscountBook.LoadForStudentAsync(ctx, s.Id);
        Assert.Equal(120_000m, book.DiscountFor(s.Id, 600_000m, M(0), to.Id));   // yangi guruh hisobida 20%
        Assert.Equal(120_000m, book.DiscountFor(s.Id, 600_000m, M(0), from.Id)); // o'tkazish oyi — eski guruh qismi ham
        Assert.Equal(0m, book.DiscountFor(s.Id, 600_000m, M(1), from.Id));       // keyingi oydan eski guruhda yo'q
        Assert.Equal(120_000m, book.DiscountFor(s.Id, 600_000m, M(1), to.Id));
        Assert.Contains(await ctx.AuditLogs.AsNoTracking().ToListAsync(),
            a => a.EntityType == AuditService.EntityStudentDiscount && a.Summary.Contains("ko'chirildi"));
    }

    [Fact]
    public async Task Chegirma_BOSHQA_fanga_kochmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var from = AddGroup(ctx, "Matematika", courseId: "math");
        var to = AddGroup(ctx, "Ingliz", courseId: "eng");
        var s = AddStudent(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount { StudentId = s.Id, GroupId = from.Id, Pct = 20, CreatedAt = AppClock.Iso() });
        await ctx.SaveChangesAsync();

        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, from, to, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(0, res.Moved);
        Assert.Equal(1, res.SkippedOtherSubject);
        Assert.Equal(from.Id, (await ctx.StudentDiscounts.AsNoTracking().SingleAsync()).GroupId);
    }

    [Fact]
    public async Task Chegirma_yangi_guruhda_allaqachon_chegirma_bolsa_kochmaydi__tugagan_chegirma_ham()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var from = AddGroup(ctx, "A", courseId: "eng");
        var to = AddGroup(ctx, "B", courseId: "eng");
        var s = AddStudent(ctx);
        ctx.StudentDiscounts.Add(new StudentDiscount { StudentId = s.Id, GroupId = from.Id, Pct = 20, CreatedAt = AppClock.Iso() });
        ctx.StudentDiscounts.Add(new StudentDiscount { StudentId = s.Id, GroupId = to.Id, Pct = 10, CreatedAt = AppClock.Iso() });
        await ctx.SaveChangesAsync();

        var res = await DiscountTransfer.MoveGroupScopedAsync(ctx, Audit(ctx), s, from, to, M(0));
        Assert.Equal(0, res.Moved);
        Assert.Equal(1, res.SkippedConflict);

        // Tugash oyi o'tgan chegirma "kuchda" emas — ko'chirilmaydi.
        var ended = new StudentDiscount { EndMonth = M(-1), Status = StudentDiscount.StatusActive };
        Assert.False(DiscountTransfer.StillRelevant(ended, M(0)));
        Assert.True(DiscountTransfer.StillRelevant(new StudentDiscount { EndMonth = M(0) }, M(0)));
    }

    // =====================================================================================
    //  MSG-5 — GURUH NOMI NOYOB, QAYTA NOMLASHDA CHAT/YORLIQ KO'CHADI
    // =====================================================================================

    [Fact]
    public void Nom_registrsiz_va_boshliqsiz_solishtiriladi()
    {
        var groups = new[] { ("g1", "Ingliz  A1 "), ("g2", "Matematika") };
        Assert.True(GroupNames.IsTaken(groups, "ingliz a1", null));
        Assert.True(GroupNames.IsTaken(groups, "  INGLIZ A1", "g2"));
        Assert.False(GroupNames.IsTaken(groups, "Ingliz A1", "g1"));   // o'zi bilan to'qnashmaydi
        Assert.False(GroupNames.IsTaken(groups, "Ingliz A2", null));
        Assert.False(GroupNames.IsTaken(groups, "   ", null));
        Assert.Equal("ingliz a1", GroupNames.Normalize("  Ingliz   A1 "));
    }

    [Fact]
    public async Task Nom_ARXIVDAGI_guruh_bilan_ham_tekshiriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        AddGroup(ctx, "Yopilgan guruh", archived: true);
        await ctx.SaveChangesAsync();

        Assert.True(await GroupNames.IsTakenAsync(ctx, "yopilgan GURUH", null));
        Assert.False(await GroupNames.IsTakenAsync(ctx, "Yangi guruh", null));
    }

    [Fact]
    public async Task Qayta_nomlashda_chat_yorliq_va_rahbarlik_yangi_nomga_kochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = AddTeacher(ctx, "Ustoz");
        t.HomeroomClass = "Eski";
        var g = AddGroup(ctx, "Eski", teacherId: t.Id);
        var s = AddStudent(ctx, className: "Eski");
        ctx.ChatMessages.Add(new ChatMessage { ClassName = "Eski", Text = "salom" });
        await ctx.SaveChangesAsync();

        g.Name = "Yangi";
        var res = await GroupNames.MigrateRenameAsync(ctx, g, "Eski", "Yangi");
        await ctx.SaveChangesAsync();

        Assert.Equal(new GroupNames.RenameResult(1, 1, 1), res);
        Assert.Equal("Yangi", (await ctx.ChatMessages.AsNoTracking().SingleAsync()).ClassName);
        Assert.Equal("Yangi", (await ctx.Students.AsNoTracking().SingleAsync()).ClassName);
        Assert.Equal("Yangi", (await ctx.Teachers.AsNoTracking().SingleAsync()).HomeroomClass);
    }

    [Fact]
    public async Task Qayta_nomlashda_ESKI_nomdosh_guruh_bolsa_faqat_shu_guruh_azolari_yorligi_kochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, "Eski");
        var twin = AddGroup(ctx, "Eski");
        var mine = AddStudent(ctx, "Meniki", className: "Eski");
        var theirs = AddStudent(ctx, "Boshqaniki", className: "Eski");
        AddMember(ctx, mine, g);
        AddMember(ctx, theirs, twin);
        ctx.ChatMessages.Add(new ChatMessage { ClassName = "Eski", Text = "salom" });
        await ctx.SaveChangesAsync();

        var res = await GroupNames.MigrateRenameAsync(ctx, g, "Eski", "Yangi");
        await ctx.SaveChangesAsync();

        Assert.Equal(0, res.ChatMessages);   // chatni nom bo'yicha ajratib bo'lmaydi — o'sha guruhda qoladi
        Assert.Equal("Yangi", (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == mine.Id)).ClassName);
        Assert.Equal("Eski", (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == theirs.Id)).ClassName);
    }

    // =====================================================================================
    //  C1 / MEM-1 — ARXIVDAGI o'quvchi va ARXIVDAGI guruh
    // =====================================================================================

    [Fact]
    public async Task Orin_ARXIVDAGI_oquvchi_va_muzlatilgan_azolik_egallamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        AddMember(ctx, AddStudent(ctx, "Faol"), g, "active");
        AddMember(ctx, AddStudent(ctx, "Sinov"), g, "trial");
        AddMember(ctx, AddStudent(ctx, "Muzlatilgan"), g, "frozen");
        AddMember(ctx, AddStudent(ctx, "Arxivdagi", archived: true), g, "active");
        AddMember(ctx, AddStudent(ctx, "Chiqqan"), g, "active", isActive: false);
        await ctx.SaveChangesAsync();

        var n = await MembershipLifecycle.SeatHolders(ctx.StudentGroups.Where(x => x.GroupId == g.Id), ctx.Students).CountAsync();
        Assert.Equal(2, n);
        // «Guruh to'ldirish» (Fill) dagi guruhlangan ko'rinish ham SQL'ga tarjima qilinadi va bir xil son beradi.
        var byGroup = await MembershipLifecycle.SeatHolders(ctx.StudentGroups, ctx.Students)
            .GroupBy(sg => sg.GroupId).Select(x => new { x.Key, N = x.Count() }).ToListAsync();
        Assert.Equal(2, Assert.Single(byGroup).N);
    }

    [Theory]
    [InlineData(true, false, MembershipBulk.GroupArchivedReason)]
    [InlineData(true, true, MembershipBulk.GroupArchivedReason)]
    [InlineData(false, true, MembershipBulk.StudentArchivedReason)]
    [InlineData(false, false, null)]
    public void Aktivlashtirish_arxivdagi_guruh_va_oquvchida_taqiqlangan(bool groupArchived, bool studentArchived, string? expected) =>
        Assert.Equal(expected, MembershipBulk.ActivationBlockReason(groupArchived, studentArchived));

    // =====================================================================================
    //  MEM-2 — «Tugatish»: faqat AKTIVLAR kursni tugatadi
    // =====================================================================================

    [Fact]
    public void Tugatishda_faqat_AKTIV_va_arxivda_bolmagan_azo_kursni_tugatadi()
    {
        var members = new[]
        {
            new StudentGroup { StudentId = "a", Status = "active", IsActive = true },
            new StudentGroup { StudentId = "t", Status = "trial", IsActive = true },
            new StudentGroup { StudentId = "f", Status = "frozen", IsActive = true },
            new StudentGroup { StudentId = "x", Status = "active", IsActive = true },
        };
        var archived = new HashSet<string> { "x" };
        Assert.Equal(new[] { "a" }, GroupCompletionRules.TransferableStudentIds(members, archived));
        Assert.True(GroupCompletionRules.Completes(members[0], archived));
        Assert.False(GroupCompletionRules.Completes(members[1], archived));
        Assert.False(GroupCompletionRules.Completes(members[2], archived));
        Assert.False(GroupCompletionRules.Completes(members[3], archived));
    }

    [Fact]
    public async Task Tugatishda_sinov_va_muzlatilgan_azo_ODDIY_CHIQADI__completed_bolmaydi__pulga_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        s.Balance = -600_000m;
        var frozen = AddMember(ctx, s, g, "frozen");
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        await ctx.SaveChangesAsync();

        var close = D(0);
        await MembershipBilling.LeaveGroupAsync(ctx, s, g, frozen, close);
        MembershipReasons.Record(ctx, s.Id, g.Id, MembershipReasons.ActionClose, close, null, MembershipReasons.CompletedNotFinishedLabel);
        await ctx.SaveChangesAsync();

        var saved = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        Assert.Equal("frozen", saved.Status);   // "completed" EMAS — bitiruvchi emas
        Assert.False(saved.IsActive);
        Assert.Equal(close, saved.LeftAt);
        Assert.Equal(-600_000m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
        // «Ketish sabablari» uchun tizim sababi bor (Sabab ko'rsatilmagan emas).
        var r = MembershipReasons.Resolve(close, saved.FrozenAt, await ctx.MembershipReasonLogs.ToListAsync(),
            Array.Empty<MembershipReasons.AuditRow>(), new MembershipReasons.ArchiveInfo(false, null, null));
        Assert.Equal(MembershipReasons.CompletedNotFinishedLabel, r.Label);
    }

    [Fact]
    public async Task LeaveGroupAsync_allaqachon_chiqqan_azolikni_QAYTA_chiqarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        var left = AddMember(ctx, s, g, "active", isActive: false);
        left.LeftAt = $"{M(-2)}-15";
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.LeaveGroupAsync(ctx, s, g, left, D(0));
        Assert.Equal(0m, res.Restored);
        Assert.Equal($"{M(-2)}-15", left.LeftAt);   // chiqish sanasi ustidan yozilmadi
    }

    // =====================================================================================
    //  MEM-4 — ARXIVDAN CHIQARISHDA tugash sanasi
    // =====================================================================================

    [Theory]
    [InlineData("2026-09-30", "2026-09-30", null)]        // «Guruhni yopish» qo'ygan sana
    [InlineData("2026-06-01", "2026-09-30", null)]        // allaqachon o'tib ketgan sana
    [InlineData("2026-12-31", "2026-09-30", "2026-12-31")] // rejalashtirilgan kelajak sana qoladi
    [InlineData(null, "2026-09-30", null)]
    [InlineData("", "2026-09-30", null)]
    [InlineData("2026-09-30", null, "2026-09-30")]
    public void Arxivdan_chiqarishda_yopish_qoygan_tugash_sanasi_tozalanadi(string? end, string? archivedAt, string? expected) =>
        Assert.Equal(expected, GroupLifecycleRules.EndDateOnUnarchive(end, archivedAt));

    // =====================================================================================
    //  Guruh ARXIVDAN CHIQARILGANDA eski (ArchivedWithClass) o'quvchilar — orqaga hisob YO'Q
    // =====================================================================================

    [Theory]
    [InlineData("2026-05-10", "2026-05-12", "2026-01-01", "2026-05-10")]   // o'quvchi arxivlangan kun
    [InlineData(null, "2026-05-12", "2026-01-01", "2026-05-12")]           // bo'lmasa guruh arxivlangan kun
    [InlineData(null, null, "2026-01-01", "2026-10-08")]                   // bo'lmasa bugun
    [InlineData("2026-05-10", null, "2026-06-01", "2026-06-01")]           // aktivlashtirishdan oldin emas
    public void Qaytarilgan_azolik_muzlatish_sanasi(string? studentAt, string? groupAt, string activatedAt, string expected) =>
        Assert.Equal(expected, GroupUnarchive.FreezeDate(studentAt, groupAt, activatedAt, "2026-10-08"));

    [Fact]
    public async Task Guruh_arxivdan_chiqarilganda_eski_oquvchi_azoligi_MUZLATILGAN_qaytadi__arxiv_oylari_hisoblanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var archivedAt = $"{M(-4)}-10";
        var g = AddGroup(ctx, archived: true);
        g.ArchivedAt = archivedAt;
        var other = AddGroup(ctx, "Boshqa", archived: true);
        Student Legacy(string name)
        {
            var s = AddStudent(ctx, name, archived: true);
            s.ArchivedWithClass = true;
            s.ArchivedAt = archivedAt;
            var m = AddMember(ctx, s, g, "active");
            m.ActivatedAt = $"{M(-8)}-01";
            return s;
        }
        var fixedOne = Legacy("Tuzatilgan");
        var control = Legacy("Nazorat");
        // Tuzatilganning BOSHQA (arxiv) guruhdagi tirik aktiv a'zoligi ham muzlatiladi.
        var otherMembership = AddMember(ctx, fixedOne, other, "active");
        otherMembership.ActivatedAt = $"{M(-8)}-01";
        await ctx.SaveChangesAsync();

        var frozen = await GroupUnarchive.FreezeRevivedMembershipsAsync(ctx, Audit(ctx), g, archivedAt, new[] { fixedOne });
        Assert.Equal(2, frozen);
        // Controller'dagi kabi: o'quvchi (va nazorat ham) arxivdan qaytariladi, guruh ochiladi.
        foreach (var s in new[] { fixedOne, control }) { s.IsArchived = false; s.ArchivedAt = null; s.ArchivedWithClass = false; }
        g.IsArchived = false;
        await ctx.SaveChangesAsync();

        var m = await ctx.StudentGroups.AsNoTracking().SingleAsync(x => x.StudentId == fixedOne.Id && x.GroupId == g.Id);
        Assert.Equal("frozen", m.Status);
        Assert.Equal(archivedAt, m.FrozenAt);
        Assert.True(m.IsActive);

        // Fon accrual arxivda turgan oylarga TUZATILGAN o'quvchiga hech narsa yozmaydi ...
        foreach (var month in new[] { M(-3), M(-2), M(-1) })
            await TuitionService.AccrueMonth(ctx, month);
        await ctx.SaveChangesAsync();
        var charges = await ctx.MonthlyCharges.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(charges, c => c.StudentId == fixedOne.Id);
        // ... nazorat (tuzatilmagan, eski xatti-harakat) esa har oy uchun qarz olardi — himoya aynan shu.
        Assert.Equal(3, charges.Count(c => c.StudentId == control.Id));
    }

    // =====================================================================================
    //  Controller oqimi — MANBA MATNI bo'yicha qulf (Server testga bog'lanmagan)
    // =====================================================================================

    private static string ControllerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        return File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "ClassesController.cs"));
    }

    /// <summary>Metod tanasi (o'z yopuvchi qavsigacha).</summary>
    private static string Body(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"{signature} topilmadi");
        var open = src.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src[start..(i + 1)];
        }
        throw new InvalidOperationException("tana oxiri topilmadi");
    }

    [Fact]
    public void Tugatish_BITTA_tranzaksiyada__yigma_audit_SAQLASHDAN_OLDIN__faqat_superadmin()
    {
        var body = Body(ControllerSource(), "public async Task<ActionResult<CompleteAndTransferResultDto>> CompleteAndTransfer(");
        Assert.Contains("MayCompleteGroups", body);
        // Qo'lda ochilgan tranzaksiya FAQAT execution strategy ichida (`EnableRetryOnFailure`).
        var strategy = body.IndexOf("CreateExecutionStrategy", StringComparison.Ordinal);
        var trx = body.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        Assert.True(strategy > 0 && trx > strategy, "Tranzaksiya execution strategy ichida ochilishi shart");
        Assert.Contains("db.ChangeTracker.Clear()", body);   // qayta urinishda holat bazadan qayta o'qiladi
        var summary = body.IndexOf("\"complete-and-transfer\"", StringComparison.Ordinal);
        var lastSave = body.LastIndexOf("SaveChangesAsync", StringComparison.Ordinal);
        var commit = body.IndexOf("CommitAsync", StringComparison.Ordinal);
        Assert.True(summary > 0 && summary < lastSave && lastSave < commit,
            "Yig'ma audit oxirgi SaveChanges'dan OLDIN, commit esa eng oxirida bo'lishi shart");
        // Har a'zo uchun alohida Membership qatori.
        Assert.Contains("audit.Record(\"Membership\", $\"{id}:{m.StudentId}\"", body);
        // Sinov/muzlatilganlar oddiy chiqish yo'li bilan; yangi guruhga o'qituvchi tarixi; chegirma.
        Assert.Contains("MembershipBilling.LeaveGroupAsync", body);
        Assert.Contains("GroupTeacherHistory.AssignAsync(db, newGroup.Id", body);
        Assert.Contains("DiscountTransfer.MoveGroupScopedAsync", body);
        Assert.Contains("ReleasePrimaryGroupNameAsync", body);
        Assert.Contains("GroupNames.IsTakenAsync", body);
        Assert.Contains("SubstituteGroupLifecycle.TrimAfterAsync", body);
    }

    /// <summary>
    /// `Program.cs` da `EnableRetryOnFailure` yoqilgan: EF qo'lda ochilgan tranzaksiyani FAQAT
    /// `CreateExecutionStrategy().ExecuteAsync(...)` ichida qabul qiladi, aks holda birinchi so'rovda
    /// InvalidOperationException (500). SQLite testlari buni USHLAMAYDI (u yerda strategiya yo'q) —
    /// shuning uchun qoida manba matni bo'yicha qulflanadi: tranzaksiya ochadigan HAR fayl strategiyani ham ishlatsin.
    /// </summary>
    [Fact]
    public void Qolda_tranzaksiya_faqat_execution_strategy_ichida()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var offenders = new[] { "WunderkindLC.Server", "WunderkindLC.Application", "WunderkindLC.Infrastructure" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(dir!.FullName, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var t = File.ReadAllText(f);
                return t.Contains("BeginTransactionAsync(") && !t.Contains("CreateExecutionStrategy(");
            })
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(offenders.Count == 0, "Strategiyasiz qo'lda tranzaksiya: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("public async Task<ActionResult<CloseGroupResultDto>> Close(")]
    [InlineData("public async Task<IActionResult> Archive(string id)")]
    [InlineData("public async Task<IActionResult> Unarchive(string id)")]
    public void Yopish_arxivlash_arxivdan_chiqarish_ochirish_ruxsatini_talab_qiladi(string signature) =>
        Assert.Contains("if (!MayCloseGroups) return CloseForbidden();", Body(ControllerSource(), signature));

    [Fact]
    public void Ochirish_pul_himoyasi_va_oquv_malumotini_tozalaydi()
    {
        var body = Body(ControllerSource(), "public async Task<IActionResult> Delete(string id");
        Assert.Contains("GroupDeletion.MoneyBlockReasonAsync", body);
        Assert.Contains("GroupDeletion.PurgeLearningDataAsync", body);
        Assert.Contains("SubstituteGroupLifecycle.RemoveAllAsync", body);
        // Atomiklik — BITTA SaveChanges (qo'lda tranzaksiya va darhol yuboriladigan Execute* YO'Q):
        // `EnableRetryOnFailure` strategiyasiz qo'lda tranzaksiyani rad etadi (ilgari har safar 500).
        Assert.DoesNotContain("BeginTransactionAsync", body);
        Assert.DoesNotContain("ExecuteDeleteAsync", body);
        Assert.DoesNotContain("ExecuteUpdateAsync", body);
        // Pul himoyasi tranzaksiya va o'chirishlardan OLDIN.
        Assert.True(body.IndexOf("MoneyBlockReasonAsync", StringComparison.Ordinal)
                    < body.IndexOf("CreditAndDropGroupChargesAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Guruh_almashtirish__kelajak_sana_rad__catch_up__chegirma__arxiv_guruh_rad()
    {
        var body = Body(ControllerSource(), "public async Task<IActionResult> TransferMember(");
        Assert.Contains("fz > AppClock.Today", body);
        Assert.Contains("TuitionService.AccrueCatchUpAsync", body);
        Assert.Contains("DiscountTransfer.MoveGroupScopedAsync", body);
        Assert.Contains("toGroup.IsArchived", body);
        Assert.Contains("MembershipLifecycle", body);
        Assert.Contains("SeatHolders", body);
    }

    [Fact]
    public void Arxiv_guruhga_qoshish_va_aktivlashtirish_rad_etiladi()
    {
        var src = ControllerSource();
        Assert.Contains("cls.IsArchived", Body(src, "public async Task<IActionResult> AddMember("));
        Assert.Contains("ActivationBlockReason", Body(src, "public async Task<IActionResult> ActivateMember("));
        Assert.Contains("ActivationBlockReason", Body(src, "private async Task<ActionResult<BulkMembershipResultDto>> BulkApplyAsync("));
    }

    [Fact]
    public void Qayta_nomlash_va_oqituvchi_almashuvi_Update_da_qollanadi()
    {
        var body = Body(ControllerSource(), "public async Task<ActionResult<Group>> Update(");
        Assert.Contains("GroupNames.MigrateRenameAsync", body);
        Assert.Contains("GroupNames.IsTakenAsync", body);
        Assert.Contains("SubstituteGroupLifecycle.OnTeacherChangedAsync", body);
    }

    [Fact]
    public void Arxivdan_chiqarish_eski_oquvchilar_azoligini_arxiv_maydonlari_tozalanishidan_OLDIN_muzlatadi()
    {
        var body = Body(ControllerSource(), "public async Task<IActionResult> Unarchive(string id)");
        var freeze = body.IndexOf("GroupUnarchive.FreezeRevivedMembershipsAsync", StringComparison.Ordinal);
        // ("cls.ArchivedAt = null" guruhniki — o'quvchi maydoni sababi bo'yicha qidiriladi.)
        var clear = body.IndexOf("s.ArchiveReason = null", StringComparison.Ordinal);
        Assert.True(freeze > 0 && freeze < clear, "Muzlatish o'quvchi arxiv sanasi tozalanishidan OLDIN bo'lishi shart");
    }

    [Fact]
    public void Uzaytirishni_bekor_qilish_endpointi_audit_bilan()
    {
        var body = Body(ControllerSource(), "public async Task<IActionResult> DeleteExtension(");
        Assert.Contains("StudentExtensions.Remove", body);
        Assert.Contains("audit.Record(\"StudentExtension\"", body);
        Assert.Contains("\"delete\"", body);
    }
}
