using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BOSH SAHIFA kartochkalari (<see cref="DashboardSummary"/>) — "Dars jadvali" sahifasi tepasidagi
/// 12 ta raqam. Asosiy xavf: "ketdi" ni noto'g'ri sanash (guruh almashtirish yoki sertifikat bilan
/// tugatish churn bo'lib ko'rinmasin) va o'quvchi holatini BITTA a'zolikdan olish.
/// </summary>
public class DashboardSummaryTests
{
    private const string Month = "2026-09";
    private const string Today = "2026-09-18";

    private static StudentGroup M(
        string student, string group, string status, string joined,
        string activated = "", string? left = null, string frozen = "",
        bool isActive = true, bool yearFreeze = false, List<string>? past = null) => new()
        {
            StudentId = student, GroupId = group, Status = status, JoinedAt = joined,
            ActivatedAt = activated, LeftAt = left, FrozenAt = frozen,
            IsActive = isActive && left is null, YearFreeze = yearFreeze, PastPeriods = past ?? [],
        };

    private static readonly Dictionary<string, string> Courses = new()
    {
        ["gA"] = "eng", ["gB"] = "eng", ["gM"] = "math", ["gNoCourse"] = "",
    };

    // ============================ Lidlar ============================

    [Fact]
    public void Buyurtmalar_faqat_aylantirilmagan_lidlar()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("l1", false),
            new DashboardSummary.LeadRow("l2", true),
            new DashboardSummary.LeadRow("l3", false),
        };
        Assert.Equal(2, DashboardSummary.OpenLeads(leads));
    }

    [Fact]
    public void Birinchi_darsga_keladiganlar_faqat_bugun_yoki_keyingi_belgilanmagan_sinov()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("today", false),
            new DashboardSummary.LeadRow("future", false),
            new DashboardSummary.LeadRow("past", false),
            new DashboardSummary.LeadRow("came", false),
            new DashboardSummary.LeadRow("converted", true),
        };
        var trials = new[]
        {
            new DashboardSummary.TrialRow("today", "pending", "2026-09-18T09:00"),
            new DashboardSummary.TrialRow("future", "pending", "2026-09-25T15:00"),
            // Bir lidda ikkita sinov — BITTA sanaladi.
            new DashboardSummary.TrialRow("future", "pending", "2026-09-26T15:00"),
            // O'tib ketgan, belgilanmagan — "keladigan" emas.
            new DashboardSummary.TrialRow("past", "pending", "2026-09-10T09:00"),
            // Natijasi belgilangan.
            new DashboardSummary.TrialRow("came", "came", "2026-09-20T09:00"),
            // Aylantirilgan lid ochiq buyurtma emas.
            new DashboardSummary.TrialRow("converted", "pending", "2026-09-20T09:00"),
            // O'chirilgan lidning yetim sinovi.
            new DashboardSummary.TrialRow("deleted", "pending", "2026-09-20T09:00"),
        };
        Assert.Equal(2, DashboardSummary.FirstLessonLeads(leads, trials, Today));
    }

    // ============================ Ketganlar ============================

    [Fact]
    public void Sinovda_turib_shu_oyda_chiqarilgan_yangi_oquvchidan_ketgan()
    {
        var ms = new[] { M("s1", "gA", "trial", "2026-09-01", left: "2026-09-10") };
        Assert.Equal((1, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Aktivlashgan_holda_shu_oyda_chiqarilgan_aktivdan_ketgan()
    {
        var ms = new[] { M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10") };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Guruh_almashtirish_bir_kurs_ichida_KETISH_EMAS()
    {
        var ms = new[]
        {
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-05"),
            M("s1", "gB", "active", "2026-09-06", activated: "2026-09-06"),
        };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Sertifikat_bilan_tugatish_churn_EMAS()
    {
        var ms = new[] { M("s1", "gA", "completed", "2026-05-01", activated: "2026-05-03", left: "2026-09-10") };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Otgan_oyda_ketgan_bu_oyga_TUSHMAYDI()
    {
        var ms = new[] { M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-08-30") };
        Assert.Equal((0, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Sinovga_qaytarilgan_lekin_ilgari_aktiv_bolgan_aktivdan_ketgan_hisoblanadi()
    {
        // ReturnToTrial ActivatedAt ni tozalaydi, lekin yopilgan davr PastPeriods da qoladi.
        var ms = new[]
        {
            M("s1", "gA", "trial", "2026-05-01", left: "2026-09-12", past: ["2026-05-03|2026-08-01"]),
        };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Ikki_kursdan_ketgan_oquvchi_BIR_marta_sanaladi()
    {
        var ms = new[]
        {
            M("s1", "gA", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-10"),
            M("s1", "gM", "active", "2026-05-01", activated: "2026-05-03", left: "2026-09-11"),
        };
        Assert.Equal((0, 1), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    [Fact]
    public void Kurssiz_guruhdan_ketish_ham_korinadi()
    {
        var ms = new[] { M("s1", "gNoCourse", "trial", "2026-09-01", left: "2026-09-10") };
        Assert.Equal((1, 0), DashboardSummary.LeftInMonth(ms, Courses, Month));
    }

    // ============================ Holatlar ============================

    [Fact]
    public void Muzlatilgan_faqat_BOSHQA_faol_azoligi_yoqlar()
    {
        var ms = new[]
        {
            // s1: eski guruhida muzlatilgan, yangisida o'qiyapti — AKTIV, muzlatilgan emas.
            M("s1", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-06-01"),
            M("s1", "gM", "active", "2026-06-02", activated: "2026-06-02"),
            // s2: faqat muzlatilgan.
            M("s2", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-06-01"),
            // s3: «aktiv muzlatish» — baribir muzlatilgan.
            M("s3", "gA", "frozen", "2026-01-01", activated: "2026-01-02", frozen: "2026-08-25", yearFreeze: true),
        };
        Assert.Equal(2, DashboardSummary.FrozenStudents(ms));
    }

    [Fact]
    public void Birinchi_tolov_shu_oyda_bolganlar()
    {
        var pays = new[]
        {
            new DashboardSummary.PaymentRow("new", "2026-09-03"),
            new DashboardSummary.PaymentRow("new", "2026-09-20"),
            new DashboardSummary.PaymentRow("old", "2026-08-15"),
            new DashboardSummary.PaymentRow("old", "2026-09-15"),
            new DashboardSummary.PaymentRow("broken", ""),
        };
        Assert.Equal(1, DashboardSummary.FirstPaymentsInMonth(pays, Month));
    }

    [Fact]
    public void Compute_arxivdagilar_joriy_sanoqlarga_kirmaydi_va_Arxivlar_da_sanaladi()
    {
        var students = new[]
        {
            new DashboardSummary.StudentRow("a", false, -100),
            new DashboardSummary.StudentRow("t", false, 0),
            new DashboardSummary.StudentRow("x", true, -500),
        };
        var ms = new[]
        {
            M("a", "gA", "active", "2026-05-01", activated: "2026-05-01"),
            M("a", "gM", "trial", "2026-09-01"),
            M("t", "gA", "trial", "2026-09-01"),
            M("x", "gA", "active", "2026-05-01", activated: "2026-05-01"),
        };
        var groups = new[]
        {
            new DashboardSummary.GroupRow("gA", "eng", false, "active"),
            new DashboardSummary.GroupRow("gM", "math", false, "full"),
            new DashboardSummary.GroupRow("gOld", "eng", true, "archived"),
        };

        var dto = DashboardSummary.Compute(
            leads: [], trials: [], deletedLeadAts: ["2026-09-02T10:00:00", "2026-08-30T10:00:00"],
            students, ms, groups, payments: [], Month, Today);

        Assert.Equal(1, dto.ActiveStudents);   // "x" arxivda
        Assert.Equal(2, dto.NewStudents);      // "a" (boshqa kursda sinov) + "t"
        Assert.Equal(1, dto.Debtors);          // arxivdagi qarzdor sanalmaydi
        Assert.Equal(1, dto.Archived);
        Assert.Equal(2, dto.Groups);           // arxivlangan guruh sanalmaydi
        Assert.Equal(1, dto.OrdersLeft);       // faqat shu oyda o'chirilgan lid
        Assert.Equal(Month, dto.Month);
    }

    // ============================ Baza bilan (EF tarjimasi) ============================

    [Fact]
    public async Task BuildAsync_bazadan_toliq_hisoblaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var eng = new Subject { Name = "Ingliz tili" };
        ctx.Subjects.Add(eng);
        var gA = new Group { Name = "A", CourseId = eng.Id };
        var gB = new Group { Name = "B", CourseId = eng.Id, IsArchived = true, Status = "archived" };
        ctx.Classes.AddRange(gA, gB);

        var active = new Student { FullName = "Aktiv", Balance = -50_000 };
        var leaver = new Student { FullName = "Ketgan" };
        var archived = new Student { FullName = "Arxiv", IsArchived = true };
        ctx.Students.AddRange(active, leaver, archived);

        ctx.StudentGroups.AddRange(
            M(active.Id, gA.Id, "active", "2026-05-01", activated: "2026-05-01"),
            M(leaver.Id, gB.Id, "active", "2026-03-01", activated: "2026-03-01", left: "2026-09-04"));

        var open = new Lead { FullName = "Ochiq" };
        var converted = new Lead { FullName = "Aylangan", ConvertedStudentId = active.Id };
        ctx.Leads.AddRange(open, converted);
        ctx.TrialLessons.Add(new TrialLesson
        {
            LeadId = open.Id, GroupId = gA.Id, ScheduledAt = "2026-09-19T10:00", Result = "pending",
        });
        ctx.ArchivedRecords.AddRange(
            new ArchivedRecord { Type = "lead", EntityId = "x1", DeletedAt = "2026-09-01T12:00:00" },
            new ArchivedRecord { Type = "student", EntityId = "x2", DeletedAt = "2026-09-01T12:00:00" });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = active.Id, Direction = "income", Category = "tuition", Amount = 100, Date = "2026-09-02" },
            // Vozvrat va boshqa kategoriya "birinchi to'lov" emas.
            new FinanceTransaction { StudentId = leaver.Id, Direction = "expense", Category = "refund", Amount = 10, Date = "2026-09-02" },
            new FinanceTransaction { StudentId = leaver.Id, Direction = "income", Category = "other", Amount = 10, Date = "2026-09-02" });
        await ctx.SaveChangesAsync();

        var dto = await DashboardSummary.BuildAsync(ctx, new DateOnly(2026, 9, 18));

        Assert.Equal(1, dto.Orders);
        Assert.Equal(1, dto.FirstLesson);
        Assert.Equal(0, dto.NewStudents);
        Assert.Equal(1, dto.ActiveStudents);
        Assert.Equal(1, dto.OrdersLeft);
        Assert.Equal(0, dto.NewLeft);
        Assert.Equal(1, dto.ActiveLeft);       // arxivlangan guruhdan ketgan tarix ham ko'rinadi
        Assert.Equal(1, dto.Debtors);
        Assert.Equal(1, dto.Groups);
        Assert.Equal(1, dto.FirstPayments);
        Assert.Equal(0, dto.Frozen);
        Assert.Equal(1, dto.Archived);
    }
}
