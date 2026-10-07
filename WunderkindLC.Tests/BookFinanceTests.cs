using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// KITOB SOTUVI → KASSA (<see cref="BookFinance"/>, books.md §7). Asosiy xavflar: kitob puli o'quv
/// to'loviga (tuition — balans, o'qituvchi foizi, KPI) aralashib ketishi, nasiyada pul olinmasdan
/// "kirim" yozilishi va kassaga kirmagan eski sotuv qaytarilganda kassa qoldig'i yolg'on kamayishi.
/// </summary>
public class BookFinanceTests
{
    private static Cashbox Box(string id, string name, string? responsible = null) =>
        new() { Id = id, Name = name, ResponsibleUserId = responsible, CreatedAt = "2026-10-01T10:00:00" };

    private static async Task<(AppDbContext Ctx, Book Book)> SeedAsync(TestDb db)
    {
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kitob kassasi", "kassir-1"));
        ctx.TransactionTypes.AddRange(TransactionTypeCatalog.SeedRows());
        var book = new Book { Title = "Alifbo", Author = "Muallif", Price = 30_000m, Stock = 10, IsActive = true };
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        return (ctx, book);
    }

    private static async Task<BookOrder> OrderAsync(AppDbContext ctx, Book book, int qty, string method,
                                                    string? studentId = null)
    {
        var order = new BookOrder
        {
            Number = 7, ChatId = 0, CustomerName = "Ali Valiyev", BookId = book.Id, BookTitle = book.Title,
            UnitPrice = book.Price, Qty = qty, Total = book.Price * qty, PaymentMethod = method,
            Source = BookSalesService.SourceManual, StudentId = studentId,
        };
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Naqd_sotuv_kitob_KIRIMI_bolib_kassirning_kassasiga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var student = new Student { FullName = "Xaridor O'quvchi" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        var order = await OrderAsync(ctx, book, 2, BookSalesService.PayCash, student.Id);

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir", "kassir-1"));

        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(("income", FinanceCategories.Book, 60_000m), (tx.Direction, tx.Category, tx.Amount));
        Assert.Equal(order.Id, tx.BookOrderId);
        Assert.Equal("k1", tx.CashboxId);                    // kassirning O'Z kassasi
        Assert.Equal(BookSalesService.PayCash, tx.Method);
        Assert.Equal("kassir-1", tx.CreatedById);
        Assert.Contains("Kitob sotuvi #7", tx.Note);
        // Ko'rinish uchun markaz turi — kitob toifasidagi tur.
        var type = await ctx.TransactionTypes.AsNoTracking().SingleAsync(t => t.Id == tx.TypeId);
        Assert.Equal(FinanceCategories.Book, type.BaseCategory);
        // ⚠️ O'quv to'lovi EMAS: o'quvchiga bog'lanmaydi, balansi o'zgarmaydi.
        Assert.Null(tx.StudentId);
        Assert.Equal(0m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
        Assert.Equal(60_000m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Kassasiz_xodim_sotsa_ASOSIY_kassaga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var order = await OrderAsync(ctx, book, 1, BookSalesService.PayCard);

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin", "admin-1"));

        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(CashboxService.MainId, tx.CashboxId);
        Assert.Equal(BookSalesService.PayCard, tx.Method);
    }

    [Fact]
    public async Task Nasiya__tasdiqlanganda_KIRIM_YOQ__tolanganda_SOF_summa_kirim_boladi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var order = await OrderAsync(ctx, book, 3, BookSalesService.PayCredit);

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir", "kassir-1"));
        Assert.Empty(await ctx.FinanceTransactions.ToListAsync());      // pul hali olinmagan

        // To'lashdan oldin 1 dona qaytarildi — qarz kamaydi, kassadan hech narsa chiqmaydi.
        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir", "kassir-1"));
        Assert.Empty(await ctx.FinanceTransactions.ToListAsync());

        Assert.Null(await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "Kassir", "kassir-1"));
        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(60_000m, tx.Amount);                                // 3 × 30 000 − 30 000
        Assert.Equal("k1", tx.CashboxId);
    }

    [Fact]
    public async Task Qaytarishda_pul_SOTUV_kassasidan_chiqadi__kitob_chiqimi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var order = await OrderAsync(ctx, book, 2, BookSalesService.PayCash);
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir", "kassir-1"));

        // Boshqa xodim (kassasi yo'q) qaytarsa ham pul sotuv tushgan kassadan chiqadi.
        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "Yaroqsiz", "Admin", "admin-1"));

        var refund = await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Direction == "expense");
        Assert.Equal((FinanceCategories.Book, 30_000m, "k1"), (refund.Category, refund.Amount, refund.CashboxId));
        Assert.Equal(order.Id, refund.BookOrderId);
        Assert.Equal(30_000m, await CashboxService.TotalAsync(ctx, "k1"));
    }

    [Fact]
    public async Task Integratsiyadan_OLDINGI_sotuv_qaytarilsa_kassadan_pul_AYRILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var order = await OrderAsync(ctx, book, 2, BookSalesService.PayCash);
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir", "kassir-1"));
        // Eski sotuvni taqlid qilamiz: moliyada kirimi yo'q.
        await ctx.FinanceTransactions.IgnoreQueryFilters().ExecuteDeleteAsync();

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir", "kassir-1"));

        Assert.Empty(await ctx.FinanceTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(30_000m, order.RefundedAmount);    // kitob bo'limida pul qaytarildi deb turibdi
    }

    [Fact]
    public async Task Kitob_puli_O_QUV_TOLOVI_hisob_kitobiga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book) = await SeedAsync(db);
        var order = await OrderAsync(ctx, book, 1, BookSalesService.PayCash);
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir", "kassir-1"));

        // Maosh foizi, o'quvchi balansi, KPI, kurs moliyasi — hammasi shu shartga qaraydi.
        Assert.False(await ctx.FinanceTransactions.AnyAsync(t =>
            (t.Direction == "income" && t.Category == "tuition") || (t.Direction == "expense" && t.Category == "refund")));
        // P&L esa kitob kirimini O'Z toifasida ko'radi.
        var rows = await ctx.FinanceTransactions.AsNoTracking().ToListAsync();
        var year = FinanceYearReport.Build(rows.Select(t => new FinanceYearReport.Row(t.Date, t.Direction, t.Category, t.Amount)),
            AppClock.Today.Year);
        Assert.Equal(FinanceCategories.Book, Assert.Single(year.Income).Category);
    }

    // ============================ Moliyada tahrirlanmaydi (manba matni) ============================

    [Fact]
    public void Moliyada_kitob_yozuvi_tahrirlanmaydi_va_bekor_qilinmaydi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "FinanceController.cs"));

        foreach (var marker in new[] { "[HttpPut(\"transactions/{id}\")]", "[HttpDelete(\"transactions/{id}\")]" })
        {
            var i = src.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(i > 0, marker);
            var body = src[i..];
            body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
            Assert.Matches(new Regex(@"tx\.BookOrderId is not null\) return BadRequest\(new \{ message = BookLinkedMessage \}\)"), body);
        }
    }
}
