using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «CHEGIRMA» — kelmagan darslar uchun oylikdan ayirish (<see cref="LessonDiscountService"/>).
/// Asosiy xavflar: to'langan oyga chegirma "avans" bo'lib ketishi, keyingi qayta hisob chegirmani
/// jimgina o'chirishi va bekor qilishda balans/qulfning noto'g'ri tiklanishi.
/// </summary>
public class LessonDiscountTests
{
    private const decimal Fee = 300_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private static (Student S, Group G) Setup(AppDbContext ctx, decimal lessonPrice = 25_000m, bool withCharge = true)
    {
        var course = new Subject { Name = "Ingliz tili", Price = Fee, LessonPrice = lessonPrice };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, CourseId = course.Id, Days = [0, 1, 2, 3, 4, 5, 6] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-3)}-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = $"{M(-3)}-01",
        });
        if (withCharge)
        {
            ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = Fee, Date = $"{M(0)}-01" });
            s.Balance = -Fee;
        }
        return (s, g);
    }

    private static async Task<MonthlyCharge> Charge(AppDbContext ctx, Student s) =>
        await ctx.MonthlyCharges.AsNoTracking().SingleAsync(c => c.StudentId == s.Id && c.Month == M(0));

    private static async Task<decimal> Balance(AppDbContext ctx, Student s) =>
        (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance;

    // ============================ Sof qoida ============================

    [Fact]
    public void Bir_dars_narxi_belgilangan_bolsa_osha_aks_holda_oylik_bolingan_darslar()
    {
        Assert.Equal(25_000m, LessonDiscountService.LessonPrice(300_000m, 25_000m, 12));
        Assert.Equal(25_000m, LessonDiscountService.LessonPrice(300_000m, 0m, 12));
        Assert.Equal(23_077m, LessonDiscountService.LessonPrice(300_000m, 0m, 13)); // yaxlit so'm
        Assert.Equal(0m, LessonDiscountService.LessonPrice(300_000m, 0m, 0));
    }

    [Fact]
    public void Summa_tolanmagan_qoldiqdan_oshmaydi()
    {
        Assert.Equal(50_000m, LessonDiscountService.AmountFor(2, 25_000m, 300_000m));
        Assert.Equal(20_000m, LessonDiscountService.AmountFor(2, 25_000m, 20_000m));
        Assert.Equal(0m, LessonDiscountService.AmountFor(2, 25_000m, 0m));
    }

    // ============================ Qo'llash ============================

    [Fact]
    public async Task Qollanganda_hisob_chegirmasi_oshadi_qator_QULFLANADI_balans_yengillashadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var row = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal edi", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        Assert.Equal(50_000m, row.Amount);
        var c = await Charge(ctx, s);
        Assert.Equal(50_000m, c.Discount);
        Assert.True(c.Locked);
        Assert.Equal(-250_000m, await Balance(ctx, s));
        Assert.Equal("kasal edi", (await ctx.LessonDiscounts.AsNoTracking().SingleAsync()).Reason);
    }

    [Fact]
    public async Task Qisman_tolangan_oyda_faqat_QOLDIQ_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-02", Direction = "income", Category = "tuition", Amount = 280_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var row = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal", "u1", "Kassir");
        Assert.Equal(20_000m, row.Amount);
    }

    [Fact]
    public async Task Tolangan_oyga_QOLLANMAYDI_avansga_aylanib_ketmasin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-02", Direction = "income", Category = "tuition", Amount = Fee,
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal", "u1", "Kassir"));
        Assert.Contains("to'lanmagan summa yo'q", ex.Message);
    }

    [Theory]
    [InlineData(0, "kasal")]
    [InlineData(40, "kasal")]
    [InlineData(2, "  ")]
    public async Task Notogri_kiritma_rad_etiladi(int lessons, string reason)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAsync(ctx, s, g, M(0), lessons, reason, "u1", "Kassir"));
        Assert.Empty(await ctx.LessonDiscounts.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Oydagi_darslardan_kop_dars_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        g.Days = [0]; // haftada 1 dars → oyda 4–5 ta
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 10, "kasal", "u1", "Kassir"));
        Assert.Contains("ta dars bor", ex.Message);
    }

    [Fact]
    public async Task Hisobi_yoq_oyda_hisob_yaratiladi_va_chegirma_qollanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx, withCharge: false);
        await ctx.SaveChangesAsync();

        await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 3, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        var c = await Charge(ctx, s);
        Assert.Equal(Fee, c.Amount);
        Assert.Equal(75_000m, c.Discount);
        Assert.Equal(-225_000m, await Balance(ctx, s));
    }

    [Fact]
    public async Task Sinf_oraligi_bor_kursda_ORALIQ_bir_dars_narxi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.SubjectPriceBands.Add(new SubjectPriceBand
        {
            SubjectId = g.CourseId!, GradeFrom = 5, GradeTo = 7, Price = 250_000m, LessonPrice = 20_000m,
        });
        s.SchoolGrade = 6;
        s.SchoolGradeYear = SchoolGrades.AcademicYear(AppClock.Today);
        await ctx.SaveChangesAsync();

        var q = await LessonDiscountService.QuoteAsync(ctx, s, g, M(0), 2);
        Assert.Equal(20_000m, q.LessonPrice);
        Assert.Equal(40_000m, q.Amount);
    }

    // ============================ Himoya va bekor qilish ============================

    [Fact]
    public async Task Keyingi_qayta_hisob_chegirmani_OCHIRMAYDI()
    {
        // Narxni joriy oyga qo'llash `Discount` ni qayta hisoblaydi — qulflangan qatorga tegmasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        await TuitionService.ApplyGroupFeeToCurrentMonthAsync(ctx, g.Id, g.Name, 320_000m, oldFee: Fee);
        await ctx.SaveChangesAsync();

        var c = await Charge(ctx, s);
        Assert.Equal(Fee, c.Amount);
        Assert.Equal(50_000m, c.Discount);
    }

    [Fact]
    public async Task Bekor_qilish_hammasini_QAYTARADI_ikkinchi_marta_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var row = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        await LessonDiscountService.CancelAsync(ctx, s, row.Id, "Admin");
        await ctx.SaveChangesAsync();

        var c = await Charge(ctx, s);
        Assert.Equal(0m, c.Discount);
        Assert.False(c.Locked); // chegirmadan oldin qulfsiz edi
        Assert.Equal(-Fee, await Balance(ctx, s));
        Assert.NotNull((await ctx.LessonDiscounts.AsNoTracking().SingleAsync()).CancelledAt); // iz qoladi

        await Assert.ThrowsAsync<InvalidOperationException>(() => LessonDiscountService.CancelAsync(ctx, s, row.Id, "Admin"));
    }

    [Fact]
    public async Task Bir_oyda_ikki_chegirma__birinchisi_bekor_qilinsa_QULF_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var first = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 1, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();
        var second = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 1, "safar", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        await LessonDiscountService.CancelAsync(ctx, s, first.Id, "Admin");
        await ctx.SaveChangesAsync();
        Assert.True((await Charge(ctx, s)).Locked);
        Assert.Equal(25_000m, (await Charge(ctx, s)).Discount);

        await LessonDiscountService.CancelAsync(ctx, s, second.Id, "Admin");
        await ctx.SaveChangesAsync();
        Assert.False((await Charge(ctx, s)).Locked);
        Assert.Equal(0m, (await Charge(ctx, s)).Discount);
    }

    [Fact]
    public async Task Avval_qolda_tahrirlangan_oy_bekor_qilingandan_keyin_ham_QULFLI_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var c0 = await ctx.MonthlyCharges.SingleAsync(c => c.StudentId == s.Id);
        c0.Locked = true;
        await ctx.SaveChangesAsync();

        var row = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 1, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();
        await LessonDiscountService.CancelAsync(ctx, s, row.Id, "Admin");
        await ctx.SaveChangesAsync();

        Assert.True((await Charge(ctx, s)).Locked);
    }

    [Fact]
    public async Task Qolda_oylik_tahririda_AMALDAGI_chegirmalar_qayta_hisobga_QOSHILADI()
    {
        // `EditCharge` registr chegirmasini YANGI summadan qayta hisoblaydi; kelmagan darslar
        // chegirmasi registrda yo'q — `ActiveAmountAsync` qo'shilmasa u jimgina o'chib ketardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var cancelled = await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 1, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();
        await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "safar", "u1", "Kassir");
        await ctx.SaveChangesAsync();
        await LessonDiscountService.CancelAsync(ctx, s, cancelled.Id, "Admin");
        await ctx.SaveChangesAsync();

        Assert.Equal(50_000m, await LessonDiscountService.ActiveAmountAsync(ctx, s.Id, g.Id, M(0)));
        Assert.Equal(0m, await LessonDiscountService.ActiveAmountAsync(ctx, s.Id, g.Id, M(-1)));
        Assert.Equal(0m, await LessonDiscountService.ActiveAmountAsync(ctx, s.Id, null, M(0)));

        // Registr chegirmasi 10% (yangi summa 250 000 dan) + darslar chegirmasi.
        var c = await Charge(ctx, s);
        var spec = 25_000m + await LessonDiscountService.ActiveAmountAsync(ctx, s.Id, g.Id, M(0));
        var plan = TuitionService.PlanChargeEdit(c.Amount, c.Discount, 250_000m, spec);
        Assert.Equal(75_000m, plan.NewDiscount);
    }

    [Fact]
    public async Task Oquvchi_butunlay_ochirilsa_chegirmalari_ham_ochadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 1, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        await StudentPurge.PurgeAsync(ctx, s.Id);
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.LessonDiscounts.AsNoTracking().ToListAsync());
    }
}
