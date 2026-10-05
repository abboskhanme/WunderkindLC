using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// SINFGA QARAB OYLIK — haqiqiy hisob yo'llari (SQLite): oylik hisob, qisman oy, catch-up, avans
/// hisobi, to'lov oynasi ko'rinishi va joriy oyga qo'llash. Hammasi BITTA qoidadan
/// (<see cref="TuitionPricing"/>) foydalanishini qulflaydi: ko'rinish va haqiqiy hisob ayrilmasin.
///
/// <para>Kurs: 3–7-sinf 225 000 (bir dars 20 000), 8–9-sinf 275 000; guruh (asosiy) narxi 300 000.</para>
/// </summary>
public class SubjectPriceBandBillingTests
{
    private const decimal GroupFee = 300_000m;
    private const decimal LowFee = 225_000m;
    private const decimal MidFee = 275_000m;

    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");
    private static int Year => SchoolGrades.AcademicYear(AppClock.Today);

    private static (Subject Course, Group Group) AddCourseWithBands(AppDbContext ctx)
    {
        var course = new Subject { Name = "Ingliz tili", Price = GroupFee, LessonPrice = 30_000m };
        ctx.Subjects.Add(course);
        ctx.SubjectPriceBands.AddRange(
            new SubjectPriceBand { SubjectId = course.Id, GradeFrom = 3, GradeTo = 7, Price = LowFee, LessonPrice = 20_000m },
            new SubjectPriceBand { SubjectId = course.Id, GradeFrom = 8, GradeTo = 9, Price = MidFee, LessonPrice = 0m });
        var g = new Group
        {
            Name = "A guruh", MonthlyFee = GroupFee, CourseId = course.Id,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Classes.Add(g);
        return (course, g);
    }

    /// <summary><paramref name="grade"/> — JORIY o'quv yilidagi sinf (null — kiritilmagan).</summary>
    private static Student AddStudent(AppDbContext ctx, int? grade)
    {
        var s = new Student
        {
            FullName = $"O'quvchi {grade?.ToString() ?? "sinfsiz"}", EnrollmentDate = $"{M(-6)}-01",
            SchoolGrade = grade, SchoolGradeYear = grade is null ? null : Year,
        };
        ctx.Students.Add(s);
        return s;
    }

    private static StudentGroup AddMembership(AppDbContext ctx, Student s, Group g, string activatedAt) =>
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = activatedAt, ActivatedAt = activatedAt,
        }).Entity;

    private static async Task<decimal> ChargeOf(AppDbContext ctx, Student s, string month) =>
        (await ctx.MonthlyCharges.AsNoTracking().SingleAsync(c => c.StudentId == s.Id && c.Month == month)).Amount;

    // ============================ Oylik hisob ============================

    [Fact]
    public async Task AccrueMonth_har_oquvchiga_SINFIGA_qarab_narx_yozadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var low = AddStudent(ctx, 5);
        var mid = AddStudent(ctx, 8);
        var none = AddStudent(ctx, null);   // sinf yo'q → guruh narxi
        var high = AddStudent(ctx, 11);     // oraliqdan tashqarida → guruh narxi
        foreach (var s in new[] { low, mid, none, high }) AddMembership(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var r = await TuitionService.AccrueMonth(ctx, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(4, r.Count);
        Assert.Equal(LowFee, await ChargeOf(ctx, low, M(0)));
        Assert.Equal(MidFee, await ChargeOf(ctx, mid, M(0)));
        Assert.Equal(GroupFee, await ChargeOf(ctx, none, M(0)));
        Assert.Equal(GroupFee, await ChargeOf(ctx, high, M(0)));
        // Balans hisob bilan bir xil kamaygan.
        Assert.Equal(-LowFee, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == low.Id)).Balance);
        Assert.Equal(-MidFee, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == mid.Id)).Balance);
    }

    [Fact]
    public async Task Chegirma_ORALIQ_narxidan_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 8);
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, StudentName = s.FullName, Pct = 10,
            Status = StudentDiscount.StatusActive, CreatedAt = "2020-01-01T00:00:00",
        });
        AddMembership(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        await TuitionService.AccrueMonth(ctx, M(0));
        await ctx.SaveChangesAsync();

        var c = await ctx.MonthlyCharges.AsNoTracking().SingleAsync(x => x.StudentId == s.Id);
        Assert.Equal(MidFee, c.Amount);
        Assert.Equal(MidFee * 0.10m, c.Discount);
    }

    [Fact]
    public async Task Catch_up_oylari_ham_oraliq_narxida()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 5);
        var m = AddMembership(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var created = await TuitionService.AccrueCatchUpAsync(ctx, s, g, m.ActivatedAt, m);
        await ctx.SaveChangesAsync();

        Assert.Equal(2, created); // M(-1) va M(0)
        Assert.Equal(LowFee, await ChargeOf(ctx, s, M(-1)));
        Assert.Equal(LowFee, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task Avans_hisobi_EnsureCharge_oraliq_narxida()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 8);
        AddMembership(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        Assert.True(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, M(1)));
        await ctx.SaveChangesAsync();
        Assert.Equal(MidFee, await ChargeOf(ctx, s, M(1)));
    }

    // ============================ Qisman oy ============================

    [Fact]
    public async Task Aktivlashtirish_oy_boshidan_TOLIQ_oraliq_narxi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 5);
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, $"{M(0)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(LowFee, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task Aktivlashtirish_oy_oxirida_ORALIQ_bir_dars_narxi()
    {
        // Guruh har kuni dars qiladi; oyning oxirgi kuni aktivlashtirildi → 1 dars × 20 000
        // (kursning 30 000 lik bir dars narxi EMAS — oraliqniki).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 5);
        await ctx.SaveChangesAsync();

        var today = AppClock.Today;
        var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, last.ToString("yyyy-MM-dd"));
        await ctx.SaveChangesAsync();

        Assert.Equal(20_000m, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task Muzlatish_oy_oxirida_TOLIQ_oraliq_narxidan_oshmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 8);
        await ctx.SaveChangesAsync();

        var today = AppClock.Today;
        var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        await TuitionService.ChargeFreezeProrateAsync(ctx, s, g, $"{M(-3)}-01", last.ToString("yyyy-MM-dd"));
        await ctx.SaveChangesAsync();

        // Butun oy o'qildi → to'liq oraliq narxi (guruhning 300 000 i EMAS).
        Assert.Equal(MidFee, await ChargeOf(ctx, s, M(0)));
    }

    // ============================ To'lov oynasi va joriy oyga qo'llash ============================

    [Fact]
    public async Task Tolov_oynasidagi_kelajak_oy_haqiqiy_hisob_bilan_BIR_XIL()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 8);
        var m = AddMembership(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var ledger = await StudentGroupLedger.BuildAsync(ctx, s, g, m);
        var next = ledger.Months.Single(x => x.Month == M(1));
        Assert.Equal(MidFee, next.Fee);
    }

    [Fact]
    public async Task Joriy_oyga_qollash_har_oquvchini_oz_oraligiga_moslaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var low = AddStudent(ctx, 5);
        var none = AddStudent(ctx, null);
        foreach (var s in new[] { low, none })
        {
            AddMembership(ctx, s, g, $"{M(-2)}-01");
            // Eski (oraliqlardan oldingi) hisob — guruh narxida.
            ctx.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = GroupFee, Date = $"{M(0)}-01",
            });
            s.Balance = -GroupFee;
        }
        await ctx.SaveChangesAsync();

        var applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(ctx, g.Id, g.Name, GroupFee);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, applied); // faqat sinfi bor o'quvchi o'zgardi
        Assert.Equal(LowFee, await ChargeOf(ctx, low, M(0)));
        Assert.Equal(-LowFee, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == low.Id)).Balance);
        Assert.Equal(GroupFee, await ChargeOf(ctx, none, M(0)));
    }

    [Fact]
    public async Task Joriy_oyga_qollash_QISMAN_hisobni_va_narxi_ozgarmaganni_TEGMAYDI()
    {
        // Kurs oraliqlari o'zgardi (5-sinf: 225 000 → 240 000), guruh narxi o'zgarmadi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var full = AddStudent(ctx, 5);      // to'liq oy hisobi — yangi narxga o'tadi
        var partial = AddStudent(ctx, 5);   // oy o'rtasida aktivlashgan — qisman hisob, TEGILMAYDI
        var none = AddStudent(ctx, null);   // sinfsiz — narxi o'zgarmadi, TEGILMAYDI
        foreach (var (s, amount) in new[] { (full, LowFee), (partial, 60_000m), (none, GroupFee) })
        {
            AddMembership(ctx, s, g, $"{M(-2)}-01");
            ctx.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = amount, Date = $"{M(0)}-01",
            });
            s.Balance = -amount;
        }
        await ctx.SaveChangesAsync();

        var oldBands = await ctx.SubjectPriceBands.AsNoTracking().ToListAsync();
        var newBands = oldBands.Select(b => new SubjectPriceBand
        {
            SubjectId = b.SubjectId, GradeFrom = b.GradeFrom, GradeTo = b.GradeTo,
            Price = b.GradeFrom == 3 ? 240_000m : b.Price, LessonPrice = b.LessonPrice,
        }).ToList();

        var applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
            ctx, g.Id, g.Name, GroupFee, PricingBook.From(newBands), courseId: g.CourseId,
            oldFee: GroupFee, oldPricing: PricingBook.From(oldBands));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, applied);
        Assert.Equal(240_000m, await ChargeOf(ctx, full, M(0)));
        Assert.Equal(60_000m, await ChargeOf(ctx, partial, M(0)));
        Assert.Equal(GroupFee, await ChargeOf(ctx, none, M(0)));
        Assert.Equal(-60_000m, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == partial.Id)).Balance);
    }

    [Fact]
    public async Task Joriy_oyga_qollash_UZATILGAN_yangi_kursni_ishlatadi_bazadagi_eskisini_emas()
    {
        // Guruh kursi shu so'rovda almashtirilmoqda (bazada hali eski — oraliqsiz kurs).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (bandCourse, _) = AddCourseWithBands(ctx);
        var plain = new Subject { Name = "Oddiy kurs", Price = GroupFee };
        ctx.Subjects.Add(plain);
        var g = new Group { Name = "B guruh", MonthlyFee = GroupFee, CourseId = plain.Id, Days = [0, 2, 4] };
        ctx.Classes.Add(g);
        var s = AddStudent(ctx, 8);
        AddMembership(ctx, s, g, $"{M(-2)}-01");
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = GroupFee, Date = $"{M(0)}-01" });
        s.Balance = -GroupFee;
        await ctx.SaveChangesAsync();

        var applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
            ctx, g.Id, g.Name, GroupFee, await PricingBook.LoadAsync(ctx),
            courseId: bandCourse.Id, oldFee: GroupFee, oldCourseId: plain.Id);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, applied);
        Assert.Equal(MidFee, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task BEPUL_guruhga_oraliq_bilan_ham_hisob_YOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        g.MonthlyFee = 0m;
        var s = AddStudent(ctx, 5);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-1))).Count);
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Empty(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Kurs_ochirilsa_oraliqlari_ham_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = new Subject { Name = "Bo'sh kurs", Price = 100_000m };
        ctx.Subjects.Add(course);
        ctx.SubjectPriceBands.Add(new SubjectPriceBand { SubjectId = course.Id, GradeFrom = 1, GradeTo = 4, Price = 90_000m });
        await ctx.SaveChangesAsync();

        ctx.Subjects.Remove(course);
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.SubjectPriceBands.AsNoTracking().ToListAsync());
    }

    // ============================ Sinf o'zgarsa — DARHOL qayta hisob ============================

    private sealed class NoHttpContext : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get => null; set { } }
    }

    private static MonthlyCharge AddCharge(AppDbContext ctx, Student s, Group g, string month, decimal amount, bool locked = false)
    {
        var c = new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = month, Amount = amount, Date = $"{month}-01", Locked = locked };
        ctx.MonthlyCharges.Add(c);
        s.Balance -= amount;
        return c;
    }

    [Fact]
    public async Task Sinf_kiritilsa_JORIY_va_KEYINGI_oylik_darhol_sinf_narxiga_otadi__otgan_oy_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, null);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g, M(-1), GroupFee);
        AddCharge(ctx, s, g, M(0), GroupFee);
        AddCharge(ctx, s, g, M(1), GroupFee);   // oldindan to'langan oy
        await ctx.SaveChangesAsync();

        s.SchoolGrade = 5;
        s.SchoolGradeYear = Year;
        var changes = await TuitionService.ApplyGradeChangeAsync(ctx, s, null, null);
        await ctx.SaveChangesAsync();

        Assert.Equal(2, changes.Count);
        Assert.Equal(GroupFee, await ChargeOf(ctx, s, M(-1)));
        Assert.Equal(LowFee, await ChargeOf(ctx, s, M(0)));
        Assert.Equal(LowFee, await ChargeOf(ctx, s, M(1)));
        Assert.Equal(-(GroupFee + 2 * LowFee), (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance);
    }

    [Fact]
    public async Task Sinf_oraliqdan_tashqariga_chiqsa_ASOSIY_narxga_qaytadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 5);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g, M(0), LowFee);
        await ctx.SaveChangesAsync();

        var (oldGrade, oldYear) = (s.SchoolGrade, s.SchoolGradeYear);
        s.SchoolGrade = 11;
        await TuitionService.ApplyGradeChangeAsync(ctx, s, oldGrade, oldYear);
        await ctx.SaveChangesAsync();

        Assert.Equal(GroupFee, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task Sinf_narxi_YOQ_kursda_sinf_ahamiyatsiz__oylik_OZGARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var plain = new Subject { Name = "Matematika", Price = GroupFee };
        ctx.Subjects.Add(plain);
        var g = new Group { Name = "M guruh", MonthlyFee = GroupFee, CourseId = plain.Id, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g);
        var s = AddStudent(ctx, null);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g, M(0), GroupFee);
        await ctx.SaveChangesAsync();

        s.SchoolGrade = 5;
        s.SchoolGradeYear = Year;
        Assert.Empty(await TuitionService.ApplyGradeChangeAsync(ctx, s, null, null));
        await ctx.SaveChangesAsync();
        Assert.Equal(GroupFee, await ChargeOf(ctx, s, M(0)));
    }

    [Fact]
    public async Task Qisman_oy_va_qolda_tahrirlangan_oy_sinf_ozgarganda_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var partial = AddStudent(ctx, null);
        var locked = AddStudent(ctx, null);
        AddMembership(ctx, partial, g, $"{M(0)}-15");
        AddMembership(ctx, locked, g, $"{M(-3)}-01");
        AddCharge(ctx, partial, g, M(0), 150_000m);                 // oy o'rtasida aktivlashtirilgan
        AddCharge(ctx, locked, g, M(0), GroupFee, locked: true);    // qo'lda tahrirlangan / «Chegirma»
        await ctx.SaveChangesAsync();

        foreach (var st in new[] { partial, locked })
        {
            st.SchoolGrade = 5;
            st.SchoolGradeYear = Year;
            Assert.Empty(await TuitionService.ApplyGradeChangeAsync(ctx, st, null, null));
        }
        await ctx.SaveChangesAsync();
        Assert.Equal(150_000m, await ChargeOf(ctx, partial, M(0)));
        Assert.Equal(GroupFee, await ChargeOf(ctx, locked, M(0)));
    }

    [Fact]
    public async Task GradeRepricing_har_ozgargan_oyni_TARIXGA_yozadi__ozgarmasa_yozmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (_, g) = AddCourseWithBands(ctx);
        var s = AddStudent(ctx, 7);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g, M(0), LowFee);
        await ctx.SaveChangesAsync();
        var audit = new AuditService(ctx, new NoHttpContext());

        // Formadagi joriy sinf qayta yuborildi (o'zgarmadi) — hech narsa yozilmaydi.
        Assert.Equal(0, await GradeRepricing.ApplyAsync(ctx, audit, s, s.SchoolGrade, s.SchoolGradeYear));

        var (oldGrade, oldYear) = (s.SchoolGrade, s.SchoolGradeYear);
        s.SchoolGrade = 8;
        Assert.Equal(1, await GradeRepricing.ApplyAsync(ctx, audit, s, oldGrade, oldYear));
        await ctx.SaveChangesAsync();

        Assert.Equal(MidFee, await ChargeOf(ctx, s, M(0)));
        var log = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.StudentId == s.Id);
        Assert.Equal(AuditService.EntityStudentDiscount, log.EntityType);
        Assert.Contains("7-sinf → 8-sinf", log.Summary);
    }

    [Fact]
    public async Task Kurs_sinf_narxlari_ozgarsa_asosiy_narx_ESKICHA_qolganda_ham_ORALIQDAGILAR_otadi()
    {
        // SubjectsController: asosiy narx «Yo'q» (keyingi oydan), sinf narxlari esa joriy oyga DARHOL —
        // servis eski asosiy narx bilan, YANGI oraliqlar bilan chaqiriladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = new Subject { Name = "Ingliz tili", Price = GroupFee };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "A guruh", MonthlyFee = GroupFee, CourseId = course.Id, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g);
        var inBand = AddStudent(ctx, 5);
        var outBand = AddStudent(ctx, 10);
        foreach (var st in new[] { inBand, outBand })
        {
            AddMembership(ctx, st, g, $"{M(-3)}-01");
            AddCharge(ctx, st, g, M(0), GroupFee);
        }
        await ctx.SaveChangesAsync();

        var newRows = new[] { new SubjectPriceBand { SubjectId = course.Id, GradeFrom = 3, GradeTo = 7, Price = LowFee } };
        g.MonthlyFee = 350_000m; // asosiy narx ham o'zgardi, lekin joriy oyga qo'llanmaydi
        var applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
            ctx, g.Id, g.Name, GroupFee, PricingBook.From(newRows), courseId: course.Id,
            oldFee: GroupFee, oldPricing: PricingBook.Empty);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, applied);
        Assert.Equal(LowFee, await ChargeOf(ctx, inBand, M(0)));
        Assert.Equal(GroupFee, await ChargeOf(ctx, outBand, M(0)));
    }
}
