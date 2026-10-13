using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-13 — owner's list: (1) «Birinchi darsga yozilgan» → aktiv o'quvchi bitta oynada, birinchi oy hisobi ochiq
/// ko'rsatiladi (`TuitionService.ActivationQuoteAsync`); (2) to'lov turlari ichida «Kitob sotuvi» (`BookManualSale`,
/// kassa ham); (3) qo'lda jarima o'qituvchi MAOSHIDAN ushlanadi (`TeacherFine`), kassaga tegmaydi.
/// </summary>
public class EnrollBooksFinesTests
{
    private sealed class NoHttp : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    // ===================== 1) Birinchi oy hisobi =====================

    private static (TestDb Db, Group Group) GroupSeed(decimal lessonPrice = 25_000m)
    {
        var db = TestDb.Sqlite();
        var course = new Subject { Name = "Ingliz tili", LessonPrice = lessonPrice };
        // Du / Chor / Ju — 2026-yil oktabrda 13 dars (1-oktabr — payshanba).
        var g = new Group { Name = "Beginner", CourseId = course.Id, MonthlyFee = 300_000m, Days = new() { 0, 2, 4 } };
        db.Context.Subjects.Add(course);
        db.Context.Classes.Add(g);
        db.Context.SaveChanges();
        return (db, g);
    }

    [Fact]
    public async Task Birinchi_oy_hisobi__qolgan_darslar_x_bir_dars_narxi_va_AYNAN_aktivlashtirish_yozadigan_summa()
    {
        var (db, g) = GroupSeed();
        using var _ = db;

        var q = await TuitionService.ActivationQuoteAsync(db.Context, g, "2026-10-14");
        Assert.NotNull(q);
        Assert.Equal("2026-10", q!.Month);
        Assert.Equal(13, q.LessonsInMonth);
        Assert.Equal(8, q.LessonsLeft);           // 14, 16, 19, 21, 23, 26, 28, 30
        Assert.Equal(25_000m, q.LessonFee);
        Assert.Equal(200_000m, q.Amount);
        Assert.False(q.FullMonth);

        // The quote IS what activation writes (one formula).
        var s = new Student { FullName = "Yangi o'quvchi" };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        await TuitionService.ChargeActivationProrateAsync(db.Context, s, g, "2026-10-14");
        db.Context.SaveChanges();
        var charge = await db.Context.MonthlyCharges.AsNoTracking().SingleAsync(c => c.StudentId == s.Id);
        Assert.Equal(q.Amount, charge.Amount);
    }

    [Fact]
    public async Task Birinchi_darsdan_toliq_oylik__darssiz_qolgan_oy_0__buzuq_sana_null()
    {
        var (db, g) = GroupSeed();
        using var _ = db;

        var full = await TuitionService.ActivationQuoteAsync(db.Context, g, "2026-10-02");
        Assert.True(full!.FullMonth);
        Assert.Equal(300_000m, full.Amount);

        var none = await TuitionService.ActivationQuoteAsync(db.Context, g, "2026-10-31");   // last lesson was the 30th
        Assert.Equal(0m, none!.Amount);
        Assert.Equal(0, none.LessonsLeft);

        Assert.Null(await TuitionService.ActivationQuoteAsync(db.Context, g, "2026-13-99"));
    }

    // ===================== 2) Kitob sotuvi =====================

    [Fact]
    public async Task Kitob_sotuvi__qoldiq_ayiriladi_kassaga_kirim_balansga_tegmaydi_qoldiqdan_kop_bolsa_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = new Book { Title = "Grammar in Use", Price = 50_000m, Stock = 3, IsActive = true };
        var s = new Student { FullName = "Ali Valiyev", Balance = -100_000m };
        ctx.Books.Add(book);
        ctx.Students.Add(s);
        ctx.SaveChanges();
        var audit = new AuditService(ctx, new NoHttp());

        var tooMany = await BookManualSale.CreateAsync(ctx, audit,
            new BookManualSalePayload(book.Id, s.Id, 5, BookSalesService.PayCash), "Kassir", null);
        Assert.Null(tooMany.Order);
        Assert.NotNull(tooMany.Error);
        ctx.ChangeTracker.Clear();
        Assert.Empty(ctx.BookOrders.AsNoTracking());

        var ok = await BookManualSale.CreateAsync(ctx, audit,
            new BookManualSalePayload(book.Id, s.Id, 2, BookSalesService.PayCash), "Kassir", null);
        Assert.Null(ok.Error);
        Assert.Equal(BookSalesService.StatusApproved, ok.Order!.Status);
        Assert.Equal(100_000m, ok.Order.Total);
        Assert.Equal(s.Id, ok.Order.StudentId);
        Assert.Equal(1, (await ctx.Books.AsNoTracking().SingleAsync()).Stock);
        var income = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal("income", income.Direction);
        Assert.Equal("book", income.Category);
        Assert.Equal(100_000m, income.Amount);
        Assert.Equal(-100_000m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);   // tuition balance untouched
    }

    // ===================== 3) Qo'lda jarima =====================

    [Theory]
    [InlineData("2026-10", 50_000, "Kechikdi", null)]
    [InlineData("2026-11", 50_000, "Kechikdi", "Kelajak oyga jarima qo'yilmaydi")]
    [InlineData("2026-1", 50_000, "Kechikdi", "Oy noto'g'ri (yyyy-MM)")]
    [InlineData("2026-10", 0, "Kechikdi", "Jarima summasini kiriting")]
    [InlineData("2026-10", 50_000, "   ", "Jarima sababini yozing")]
    public void Jarima_tekshiruvi(string month, int amount, string reason, string? error) =>
        Assert.Equal(error, TeacherFines.Validate(month, amount, reason, new DateOnly(2026, 10, 13)));

    [Fact]
    public async Task Qolda_jarima_maoshdan_ushlanadi__bekor_qilingani_yoq__qoldiqdan_oshmaydi__sababi_korinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = new Teacher { FullName = "Qat'iy", SalaryMode = "fixed", Salary = 1_000_000m, SalaryStartDate = "2026-01-01" };
        ctx.Teachers.Add(t);
        TeacherFine F(string month, decimal amount, string reason, string status = TeacherFines.Active) => new()
        {
            TeacherId = t.Id, Month = month, Amount = amount, Reason = reason, Status = status, CreatedAt = DateTime.UtcNow,
        };
        ctx.TeacherFines.AddRange(
            F("2026-03", 100_000m, "Darsga kechikdi"),
            F("2026-03", 50_000m, "Jurnal to'ldirilmadi"),
            F("2026-03", 70_000m, "Xato qo'yilgan", TeacherFines.Waived),
            F("2026-04", 5_000_000m, "Katta jarima"));
        ctx.SaveChanges();

        var l = await SalaryLedger.BuildAsync(ctx, t, "2026-03", "2026-04");
        var mar = l.Months.Single(m => m.Month == "2026-03");
        var apr = l.Months.Single(m => m.Month == "2026-04");
        Assert.Equal(850_000m, mar.Expected);
        Assert.Equal(150_000m, mar.ManualFines);
        Assert.Equal(2, mar.ManualFineCount);
        Assert.Equal(new[] { "Darsga kechikdi", "Jurnal to'ldirilmadi" }, mar.ManualFineItems!.Select(i => i.Reason));
        Assert.Equal(0m, apr.Expected);                // capped — salary never negative
        Assert.Equal(1_000_000m, apr.ManualFines);
        Assert.Equal(1_150_000m, l.TotalManualFines);
        Assert.Empty(ctx.FinanceTransactions.AsNoTracking());   // no cash movement
    }

    // ===================== RBAC / wiring (source) =====================

    private static string Server(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, "WunderkindLC.Server", "Controllers", .. parts]));
    }

    [Fact]
    public void Endpointlar_ruxsat_ostida_va_jarima_chiqim_bolib_yozilmaydi()
    {
        var fines = Server("TeacherFinesController.cs");
        Assert.Contains("[AdminPerm(\"finance.main\", ReadRequiresPerm = true)]", fines);
        Assert.DoesNotContain("AllowAnonymous", fines);

        var kassaBooks = Server("KassaController.Books.cs");
        Assert.Contains("[AdminPerm(\"kassa\")]", Server("KassaController.cs"));
        Assert.Contains("BookManualSale.CreateAsync", kassaBooks);
        Assert.Contains("PayCash or BookSalesService.PayCard", kassaBooks);   // no credit from the payment window
        Assert.Contains("BookManualSale.CreateAsync", Server("BooksController.cs"));

        Assert.Contains("[HttpGet(\"{id}/activation-quote\")]", Server("ClassesController.cs"));
        Assert.Contains("if (p.Category == \"penalty\")", Server("FinanceController.cs"));
    }
}
