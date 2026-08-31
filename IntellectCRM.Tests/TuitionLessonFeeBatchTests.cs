using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// KURSNING BIR DARS NARXI (LessonPrice) — OMMAVIY yuklash va uni muzlatish hisobiga UZATISH.
///
/// <para>Nima uchun: ommaviy muzlatishda <c>ChargeFreezeProrateAsync</c> har a'zolik uchun bir xil
/// kursning narxini qaytadan so'rardi (500 ta bir xil so'rov). Endi narx bir marta yuklanib
/// (<see cref="TuitionService.LessonFeesForCoursesAsync"/>) halqada <c>lessonFee</c> sifatida
/// uzatiladi.</para>
///
/// <para>⚠️ Bu SOF TEZLIK ishi. Shuning uchun asosiy testlar — REGRESSIYA QULFI: parametr
/// berilganda va berilmaganda hisob (balans + <c>MonthlyCharge</c> qatori) AYNAN bir xil bo'lishi
/// shart. Sanalar MUTLAQ yozilmaydi — <see cref="AppClock.Today"/> ga nisbatan quriladi.</para>
/// </summary>
public class TuitionLessonFeeBatchTests
{
    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Har kuni dars bo'ladigan guruh — "jami dars" = oydagi kunlar soni.</summary>
    private static Group AddGroup(AppDbContext ctx, string courseId, decimal fee = 600_000m)
    {
        var g = new Group
        {
            Name = "A guruh",
            MonthlyFee = fee,
            CourseId = courseId,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Classes.Add(g);
        return g;
    }

    private static Student AddStudent(AppDbContext ctx) =>
        AddTo(ctx, new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-6)}-01", Balance = 0m });

    private static Student AddTo(AppDbContext ctx, Student s) { ctx.Students.Add(s); return s; }

    private static Subject AddCourse(AppDbContext ctx, decimal lessonPrice) =>
        AddTo(ctx, new Subject { Name = "Ingliz tili", Price = 600_000m, LessonPrice = lessonPrice });

    private static Subject AddTo(AppDbContext ctx, Subject s) { ctx.Subjects.Add(s); return s; }

    // ==================== LessonFeesForCoursesAsync — semantika ====================

    [Fact]
    public async Task Ommaviy_variant_BITTA_soʻrovda_barcha_kurs_narxini_qaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = AddCourse(ctx, 25_000m);
        var b = AddCourse(ctx, 40_000m);
        await ctx.SaveChangesAsync();

        var fees = await TuitionService.LessonFeesForCoursesAsync(ctx, [a.Id, b.Id, a.Id]);

        Assert.Equal(25_000m, fees[a.Id]);
        Assert.Equal(40_000m, fees[b.Id]);
        Assert.Equal(2, fees.Count);   // takroriy kalit ikki marta so'ralmaydi
    }

    [Fact]
    public async Task Boʻsh_va_TOPILMAGAN_kurs_lugʻatga_KIRMAYDI_yaʼni_chaqiruvchi_0_oladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = AddCourse(ctx, 25_000m);
        await ctx.SaveChangesAsync();

        var fees = await TuitionService.LessonFeesForCoursesAsync(ctx, ["", null, "yoq-bunday-kurs", a.Id]);

        // Yakka variant (`LessonFeeForCourseAsync`) bu ikkala holatda ham 0 qaytaradi — lug'atda
        // kalit umuman bo'lmagani uchun chaqiruvchi ham aynan 0 oladi.
        Assert.False(fees.ContainsKey(""));
        Assert.False(fees.ContainsKey("yoq-bunday-kurs"));
        Assert.Equal(25_000m, fees[a.Id]);
    }

    [Fact]
    public async Task Kurslar_boʻsh_boʻlsa_soʻrov_umuman_qilinmaydi()
    {
        using var db = TestDb.Sqlite();
        var fees = await TuitionService.LessonFeesForCoursesAsync(db.Context, []);
        Assert.Empty(fees);
    }

    // ==================== REGRESSIYA QULFI: lessonFee berilgan / berilmagan ====================

    /// <summary>Bitta muzlatish stsenariysini toza bazada bajaradi va natijani qaytaradi.
    /// <paramref name="passFee"/> — narx OLDINDAN uzatilsinmi (ommaviy yo'l) yoki
    /// <c>ChargeFreezeProrateAsync</c> uni o'zi so'rasinmi (yakka yo'l).</summary>
    private static async Task<(decimal Balance, decimal Amount, decimal Discount, string Date, int Rows)>
        FreezeAsync(bool passFee, decimal lessonPrice, bool linkCourse = true)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, lessonPrice);
        var g = AddGroup(ctx, linkCourse ? course.Id : "");
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        var activatedAt = $"{M(0)}-01";
        var freezeDate = $"{M(0)}-10";   // 10 ta dars — 12 tadan kam, ya'ni LessonPrice ISHLAYDI

        decimal? fee = null;
        if (passFee)
        {
            var fees = await TuitionService.LessonFeesForCoursesAsync(ctx, [g.CourseId]);
            fee = fees.TryGetValue(g.CourseId, out var f) ? f : 0m;
        }

        await TuitionService.ChargeFreezeProrateAsync(ctx, s, g, activatedAt, freezeDate, fee);
        await ctx.SaveChangesAsync();

        var row = await ctx.MonthlyCharges.SingleAsync(c => c.StudentId == s.Id && c.GroupId == g.Id);
        var rows = await ctx.MonthlyCharges.CountAsync();
        return (s.Balance, row.Amount, row.Discount, row.Date, rows);
    }

    [Fact]
    public async Task Narx_UZATILGANDA_va_uzatilmaganda_hisob_AYNAN_bir_xil()
    {
        var withFee = await FreezeAsync(passFee: true, lessonPrice: 25_000m);
        var without = await FreezeAsync(passFee: false, lessonPrice: 25_000m);

        Assert.Equal(250_000m, withFee.Amount);   // 10 dars × 25 000
        Assert.Equal(without, withFee);
    }

    [Fact]
    public async Task Kurs_narxi_kiritilmagan_boʻlsa_ham_ikki_yoʻl_bir_xil_eski_pro_rata()
    {
        // LessonPrice = 0 → eski pro-rata (oylik × dars ÷ jami). Ikkala yo'l ham 0 oladi.
        var withFee = await FreezeAsync(passFee: true, lessonPrice: 0m);
        var without = await FreezeAsync(passFee: false, lessonPrice: 0m);
        Assert.Equal(without, withFee);
        Assert.True(withFee.Amount > 0m);
    }

    [Fact]
    public async Task Guruhda_KURS_biriktirilmagan_boʻlsa_ham_ikki_yoʻl_bir_xil()
    {
        // CourseId = "" → yakka variant ham, lug'at ham 0 beradi (kalit yo'q).
        var withFee = await FreezeAsync(passFee: true, lessonPrice: 25_000m, linkCourse: false);
        var without = await FreezeAsync(passFee: false, lessonPrice: 25_000m, linkCourse: false);
        Assert.Equal(without, withFee);
    }

    // ==================== BIRLASHTIRILGAN MonthlyCharges soʻrovi ====================

    [Fact]
    public async Task Eski_AGGREGATE_qator_avvalgidek_tozalanadi_va_balansga_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, 25_000m);
        var g = AddGroup(ctx, course.Id);
        var s = AddStudent(ctx);
        // Guruhsiz (eski ClassName) davrdan qolgan aggregate qator: 600 000 hisoblangan, 100 000 chegirma.
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = null, Month = M(0), Amount = 600_000m, Discount = 100_000m,
            Date = $"{M(0)}-01",
        });
        s.Balance = -500_000m;   // yaratilganda balansdan effektiv summa yechilgan edi
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeFreezeProrateAsync(ctx, s, g, $"{M(0)}-01", $"{M(0)}-10");
        await ctx.SaveChangesAsync();

        // Aggregate qator o'chdi, effektivi (600 000 − 100 000) balansga qaytdi, so'ng per-guruh
        // qisman hisob (10 × 25 000) yechildi: −500 000 + 500 000 − 250 000.
        Assert.Equal(-250_000m, s.Balance);
        var row = await ctx.MonthlyCharges.SingleAsync();
        Assert.Equal(g.Id, row.GroupId);
        Assert.Equal(250_000m, row.Amount);
    }

    [Fact]
    public async Task Boshqa_GURUH_va_boshqa_OY_qatorlariga_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, 25_000m);
        var g = AddGroup(ctx, course.Id);
        var boshqa = AddGroup(ctx, course.Id);
        var s = AddStudent(ctx);
        // Birlashtirilgan so'rov (GroupId == null || GroupId == cls.Id) begona qatorni tortmasligi kerak.
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = boshqa.Id, Month = M(0), Amount = 600_000m, Date = $"{M(0)}-01",
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = null, Month = M(-1), Amount = 600_000m, Date = $"{M(-1)}-01",
        });
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeFreezeProrateAsync(ctx, s, g, $"{M(0)}-01", $"{M(0)}-10");
        await ctx.SaveChangesAsync();

        Assert.Equal(3, await ctx.MonthlyCharges.CountAsync());   // ikkovi joyida + yangi per-guruh qator
        Assert.NotNull(await ctx.MonthlyCharges.FirstOrDefaultAsync(c => c.GroupId == boshqa.Id));
        Assert.NotNull(await ctx.MonthlyCharges.FirstOrDefaultAsync(c => c.GroupId == null && c.Month == M(-1)));
    }
}
