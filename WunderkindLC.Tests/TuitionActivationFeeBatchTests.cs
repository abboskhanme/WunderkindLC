using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// AKTIVLASHTIRISH TARMOG'I — kursning bir dars narxini (LessonPrice) OLDINDAN uzatish va shu
/// (o'quvchi, oy) uchun ikkita <c>MonthlyCharges</c> so'rovini BITTAGA birlashtirish.
///
/// <para>Nima uchun: ommaviy aktivlashtirishda <c>ChargeActivationProrateAsync</c> har a'zolik uchun
/// bir xil kursning narxini qaytadan so'rar, ustiga aggregate qatorni tozalash va per-guruh qatorni
/// topish ikki alohida aylanish edi. Endi narx bir marta yuklanib
/// (<see cref="TuitionService.LessonFeesForCoursesAsync"/>) halqada <c>lessonFee</c> sifatida
/// uzatiladi, ikkala qator esa bitta so'rovda o'qiladi — muzlatish tarmog'idagi bilan AYNAN bir xil
/// naqsh (<see cref="TuitionLessonFeeBatchTests"/>).</para>
///
/// <para>⚠️ Bu SOF TEZLIK ishi va PUL yo'li. Shuning uchun asosiy testlar — REGRESSIYA QULFI:
/// parametr berilganda va berilmaganda hisob (balans + <c>MonthlyCharge</c> qatori) AYNAN bir xil
/// bo'lishi shart. Sanalar MUTLAQ yozilmaydi — <see cref="AppClock.Today"/> ga nisbatan quriladi.</para>
/// </summary>
public class TuitionActivationFeeBatchTests
{
    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Joriy oyda AYNAN 10 ta dars qoladigan sana (oy oxiridan 9 kun orqaga).
    /// Har kuni dars bo'lgani uchun "qolgan dars" = qolgan kunlar soni. 10 — ham
    /// <see cref="TuitionService.FullMonthLessonThreshold"/> (12) dan kam, ham oydagi jami darsdan
    /// kam, ya'ni LessonPrice formulasi ISHLAYDI va natija oy uzunligiga bog'liq bo'lmaydi.</summary>
    private static string TenLessonsLeftDate()
    {
        var t = AppClock.Today;
        var last = DateTime.DaysInMonth(t.Year, t.Month);
        return new DateOnly(t.Year, t.Month, last - 9).ToString("yyyy-MM-dd");
    }

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

    private static Student AddStudent(AppDbContext ctx)
    {
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-6)}-01", Balance = 0m };
        ctx.Students.Add(s);
        return s;
    }

    private static Subject AddCourse(AppDbContext ctx, decimal lessonPrice)
    {
        var s = new Subject { Name = "Ingliz tili", Price = 600_000m, LessonPrice = lessonPrice };
        ctx.Subjects.Add(s);
        return s;
    }

    // ==================== REGRESSIYA QULFI: lessonFee berilgan / berilmagan ====================

    /// <summary>Bitta AKTIVLASHTIRISH stsenariysini toza bazada bajaradi va natijani qaytaradi.
    /// <paramref name="passFee"/> — narx OLDINDAN uzatilsinmi (ommaviy yo'l) yoki
    /// <c>ChargeActivationProrateAsync</c> uni o'zi so'rasinmi (yakka yo'l).</summary>
    private static async Task<(decimal Balance, decimal Amount, decimal Discount, string Date, int Rows)>
        ActivateAsync(bool passFee, decimal lessonPrice, bool linkCourse = true)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, lessonPrice);
        var g = AddGroup(ctx, linkCourse ? course.Id : "");
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        var activateDate = TenLessonsLeftDate();

        decimal? fee = null;
        if (passFee)
        {
            var fees = await TuitionService.LessonFeesForCoursesAsync(ctx, [g.CourseId]);
            fee = fees.TryGetValue(g.CourseId, out var f) ? f : 0m;
        }

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, activateDate, lessonFee: fee);
        await ctx.SaveChangesAsync();

        var row = await ctx.MonthlyCharges.SingleAsync(c => c.StudentId == s.Id && c.GroupId == g.Id);
        var rows = await ctx.MonthlyCharges.CountAsync();
        return (s.Balance, row.Amount, row.Discount, row.Date, rows);
    }

    [Fact]
    public async Task Narx_UZATILGANDA_va_uzatilmaganda_aktivlashtirish_hisobi_AYNAN_bir_xil()
    {
        var withFee = await ActivateAsync(passFee: true, lessonPrice: 25_000m);
        var without = await ActivateAsync(passFee: false, lessonPrice: 25_000m);

        Assert.Equal(250_000m, withFee.Amount);   // 10 dars × 25 000
        Assert.Equal(-250_000m, withFee.Balance);
        Assert.Equal(without, withFee);
    }

    [Fact]
    public async Task Kurs_narxi_kiritilmagan_boʻlsa_ham_ikki_yoʻl_bir_xil_eski_pro_rata()
    {
        // LessonPrice = 0 → eski pro-rata (oylik × dars ÷ jami). Ikkala yo'l ham 0 oladi.
        var withFee = await ActivateAsync(passFee: true, lessonPrice: 0m);
        var without = await ActivateAsync(passFee: false, lessonPrice: 0m);
        Assert.Equal(without, withFee);
        Assert.True(withFee.Amount > 0m);
    }

    [Fact]
    public async Task Guruhda_KURS_biriktirilmagan_boʻlsa_ham_ikki_yoʻl_bir_xil()
    {
        // CourseId = "" → yakka variant ham, lug'at ham 0 beradi (kalit yo'q) → eski pro-rata.
        var withFee = await ActivateAsync(passFee: true, lessonPrice: 25_000m, linkCourse: false);
        var without = await ActivateAsync(passFee: false, lessonPrice: 25_000m, linkCourse: false);
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

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, TenLessonsLeftDate());
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

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, TenLessonsLeftDate());
        await ctx.SaveChangesAsync();

        Assert.Equal(3, await ctx.MonthlyCharges.CountAsync());   // ikkovi joyida + yangi per-guruh qator
        Assert.NotNull(await ctx.MonthlyCharges.FirstOrDefaultAsync(c => c.GroupId == boshqa.Id));
        Assert.NotNull(await ctx.MonthlyCharges.FirstOrDefaultAsync(c => c.GroupId == null && c.Month == M(-1)));
    }

    /// <summary>⚠️ TARTIB QULFI: `Locked` (qo'lda tahrirlangan) per-guruh qator bo'lsa metod
    /// ERTA qaytadi — LEKIN aggregate qator SHUNDAN OLDIN tozalanishi kerak (avvalgi kodda ham
    /// `PurgeAggregateRowAsync` `existing` dan oldin turardi). Bitta so'rovga birlashtirganda shu
    /// tartib buzilsa, o'quvchida IKKITA hisob qatori qolib, oy summasi ikki baravar ko'rinardi.</summary>
    [Fact]
    public async Task LOCKED_qator_tegilmaydi_lekin_aggregate_qator_baribir_tozalanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, 25_000m);
        var g = AddGroup(ctx, course.Id);
        var s = AddStudent(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = null, Month = M(0), Amount = 600_000m, Date = $"{M(0)}-01",
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 111_000m, Date = $"{M(0)}-01", Locked = true,
        });
        s.Balance = -711_000m;
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, TenLessonsLeftDate());
        await ctx.SaveChangesAsync();

        var row = await ctx.MonthlyCharges.SingleAsync();
        Assert.Equal(g.Id, row.GroupId);
        Assert.Equal(111_000m, row.Amount);          // qo'lda kiritilgan summa TEGILMADI
        Assert.Equal(-111_000m, s.Balance);          // faqat aggregate effektivi (600 000) qaytdi
    }

    /// <summary>SHU OYDA muzlatilgandan keyin QAYTA aktivlashtirish (<c>addSegment: true</c>) —
    /// mavjud qator ustiga segment QO'SHILADI. Birlashtirilgan so'rov bu yo'lni ham avvalgidek
    /// topishi kerak (mavjud qator endi bazadan emas, `monthRows` dan olinadi).</summary>
    [Fact]
    public async Task Qayta_aktivlashtirishda_segment_avvalgidek_USTIGA_qoʻshiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var course = AddCourse(ctx, 25_000m);
        var g = AddGroup(ctx, course.Id);
        var s = AddStudent(ctx);
        // Muzlatishgacha yozilgan segment: 100 000.
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 100_000m, Date = $"{M(0)}-01",
        });
        s.Balance = -100_000m;
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, TenLessonsLeftDate(), addSegment: true);
        await ctx.SaveChangesAsync();

        var row = await ctx.MonthlyCharges.SingleAsync();
        Assert.Equal(350_000m, row.Amount);   // 100 000 + 10 × 25 000, to'liq oylikdan (600 000) oshmaydi
        Assert.Equal(-350_000m, s.Balance);
    }
}
