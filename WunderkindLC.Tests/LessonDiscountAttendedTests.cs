using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «CHEGIRMA» KELGAN darslar bo'yicha (2026-10-15, <see cref="LessonDiscountService.ApplyAttendedAsync"/>):
/// o'quvchi faqat kelgan darslari uchun to'laydi, ayirma chegirma. Hisob mexanizmi (qulf, bekor qilish)
/// eski yo'l bilan bir xil — u <c>LessonDiscountTests</c> da.
/// </summary>
public class LessonDiscountAttendedTests
{
    private const decimal Fee = 300_000m;
    private const decimal Lesson = 25_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Har kuni dars (oyda 28–31 ta), bir dars 25 000, oylik 300 000.</summary>
    private static (Student S, Group G) Setup(AppDbContext ctx, decimal chargeAmount = Fee, decimal chargeDiscount = 0m)
    {
        var course = new Subject { Name = "Ingliz tili", Price = Fee, LessonPrice = Lesson };
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
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = chargeAmount, Discount = chargeDiscount, Date = $"{M(0)}-01",
        });
        s.Balance = -(chargeAmount - chargeDiscount);
        return (s, g);
    }

    private static async Task<MonthlyCharge> Charge(AppDbContext ctx, Student s) =>
        await ctx.MonthlyCharges.AsNoTracking().SingleAsync(c => c.StudentId == s.Id && c.Month == M(0));

    // ============================ Sof qoida ============================

    [Fact]
    public void Kelgan_dars_narxi_oquvchining_oz_chegirmasi_ulushi_bilan()
    {
        Assert.Equal(25_000m, LessonDiscountService.AttendedPrice(25_000m, 1m));
        Assert.Equal(22_500m, LessonDiscountService.AttendedPrice(25_000m, 0.9m));
        Assert.Equal(8_333m, LessonDiscountService.AttendedPrice(25_000m, 1m / 3m)); // yaxlit so'm
        Assert.Equal(25_000m, LessonDiscountService.AttendedPrice(25_000m, 1.5m));  // ulush 0..1
    }

    [Fact]
    public void Ayriladi__oy_hisobi_minus_kelgan_darslar_puli__qoldiqdan_oshmaydi_manfiy_bolmaydi()
    {
        Assert.Equal(50_000m, LessonDiscountService.AttendedAmountFor(10, 25_000m, 300_000m, 300_000m));
        Assert.Equal(20_000m, LessonDiscountService.AttendedAmountFor(10, 25_000m, 300_000m, 20_000m));
        Assert.Equal(300_000m, LessonDiscountService.AttendedAmountFor(0, 25_000m, 300_000m, 300_000m));
        Assert.Equal(0m, LessonDiscountService.AttendedAmountFor(12, 25_000m, 300_000m, 300_000m));
        Assert.Equal(0m, LessonDiscountService.AttendedAmountFor(20, 25_000m, 300_000m, 300_000m));
    }

    // ============================ Qo'llash ============================

    [Fact]
    public async Task Kelgan_10_dars__250_000_tolaydi__50_000_ayriladi_qator_QULFLANADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var q = await LessonDiscountService.QuoteAttendedAsync(ctx, s, g, M(0), 10);
        Assert.Equal(250_000m, q.Payable);
        Assert.Equal(300_000m, q.MonthAmount);
        Assert.Equal(50_000m, q.Amount);

        var row = await LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 10, "kasal edi", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        Assert.Equal(10, row.AttendedLessons);
        Assert.Equal(0, row.Lessons);
        Assert.Equal(50_000m, row.Amount);
        var c = await Charge(ctx, s);
        Assert.Equal(50_000m, c.Discount);
        Assert.True(c.Locked);
        Assert.Equal(-250_000m, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance);
    }

    [Fact]
    public async Task Ozining_10_foiz_chegirmasi_bor_oquvchi_kelgan_darsi_uchun_ham_10_foiz_kam_tolaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx, chargeDiscount: 30_000m); // oy hisobi 270 000
        await ctx.SaveChangesAsync();

        var q = await LessonDiscountService.QuoteAttendedAsync(ctx, s, g, M(0), 10);
        Assert.Equal(22_500m, q.AttendedPrice);
        Assert.Equal(225_000m, q.Payable);
        Assert.Equal(45_000m, q.Amount);
    }

    [Fact]
    public async Task Qisman_oyda__kelgan_darslar_qisman_hisobdan_ayriladi_toliq_oydan_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx, chargeAmount: 150_000m); // oyning yarmidan aktivlashgan
        await ctx.SaveChangesAsync();

        var q = await LessonDiscountService.QuoteAttendedAsync(ctx, s, g, M(0), 4);
        Assert.Equal(100_000m, q.Payable);
        Assert.Equal(50_000m, q.Amount);
    }

    [Fact]
    public async Task Hech_kelmagan__0_dars__butun_qoldiq_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var row = await LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 0, "kasal", "u1", "Kassir");
        Assert.Equal(300_000m, row.Amount);
    }

    [Fact]
    public async Task Kelgan_darslar_puli_oydan_kam_bolmasa__chegirma_kerak_emas_xatosi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 12, "kasal", "u1", "Kassir"));
        Assert.Contains("chegirma kerak emas", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(40)]
    public async Task Notogri_son_rad_etiladi(int attended)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), attended, "kasal", "u1", "Kassir"));
    }

    [Fact]
    public async Task Shu_oyda_amaldagi_chegirma_bolsa_IKKINCHISI_rad__bekor_qilingach_mumkin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var first = await LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 10, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 8, "kasal", "u1", "Kassir"));
        Assert.Contains("avval uni bekor qiling", ex.Message);

        await LessonDiscountService.CancelAsync(ctx, s, first.Id, "Kassir");
        await ctx.SaveChangesAsync();
        var second = await LessonDiscountService.ApplyAttendedAsync(ctx, s, g, M(0), 8, "kasal", "u1", "Kassir");
        await ctx.SaveChangesAsync();

        Assert.Equal(100_000m, second.Amount); // 300 000 − 8 × 25 000
        Assert.Equal(100_000m, (await Charge(ctx, s)).Discount);
    }

    // ============================ HTTP — eski sahifa ============================

    /// <summary>⚠️ Eski (keshdagi) sahifa `lessons` = KELMAGAN darslarni yuboradi — uni "kelgan" deb o'qisak deyarli
    /// butun oy ayrilardi. `attended` siz so'rov — 400 va "sahifani yangilang"; HTTP faqat kelgan darslar yo'lini chaqiradi.</summary>
    [Fact]
    public void Eski_sahifa_attended_siz_yuborsa_400__HTTP_faqat_kelgan_darslar_yolini_chaqiradi()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", "LessonDiscountHttp.cs"));
        Assert.Contains("if (attended is null) return new BadRequestObjectResult(new { message = StaleClientMessage });", src);
        Assert.Contains("if (req.Attended is null) return new BadRequestObjectResult(new { message = StaleClientMessage });", src);
        Assert.Contains("LessonDiscountService.QuoteAttendedAsync(", src);
        Assert.Contains("LessonDiscountService.ApplyAttendedAsync(", src);
        Assert.DoesNotContain("LessonDiscountService.ApplyAsync(", src);
        Assert.DoesNotContain("LessonDiscountService.QuoteAsync(", src);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }
}
