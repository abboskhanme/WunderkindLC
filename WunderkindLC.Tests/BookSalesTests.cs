using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// KITOBLAR SOTUVI — <see cref="BookSalesService"/> (ombor/buyurtma mantig'ining YAGONA joyi) va
/// <see cref="BookShopBotService"/> ning sof (bog'liqliksiz) qismlari.
///
/// <para>Kutilayotgan xulqning rasmiy manbai — <c>.claude/rules/books.md</c>:
/// <list type="bullet">
///   <item>qoldiq FAQAT admin tasdiqlaganda ayiriladi (buyurtma tushganda TEGILMAYDI);</item>
///   <item>ikkinchi marta tasdiqlash/rad etish — o'zgartirmasdan xato matni;</item>
///   <item>qoldiq yetmasa tasdiqlash rad etiladi;</item>
///   <item>kitob nomi/narxi buyurtmada SNAPSHOT;</item>
///   <item>bitta chatda bitta faol bot sessiyasi (ChatId UNIKAL).</item>
/// </list></para>
/// </summary>
public class BookSalesTests
{
    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    private static Book NewBook(string title = "Alifbo", decimal price = 25000, int stock = 10) => new()
    {
        Title = title, Author = "Muallif", Price = price, Stock = stock, IsActive = true,
    };

    private static BookOrder NewOrder(Book book, int qty = 2, string status = BookSalesService.StatusPending) => new()
    {
        Number = 1,
        ChatId = 100,
        CustomerName = "Ali Valiyev",
        Phone = "998901234567",
        BookId = book.Id,
        BookTitle = book.Title,
        UnitPrice = book.Price,
        Qty = qty,
        Total = book.Price * qty,
        PaymentMethod = BookSalesService.PayCash,
        Status = status,
    };

    /// <summary>
    /// "IKKI KASSIR" — bitta SQLite bazasi ustida IKKITA mustaqil <see cref="AppDbContext"/>.
    /// Har birining o'z ChangeTracker'i bor, ya'ni ikki alohida HTTP so'rovi (ikki kassir)
    /// bir vaqtda ishlayotgan holat aynan takrorlanadi. <see cref="TestDb"/> bitta kontekst
    /// beradi — poyga (race) ssenariysi uchun shu yordamchi kerak.
    /// </summary>
    private sealed class IkkiKassir : IDisposable
    {
        private readonly SqliteConnection _connection;

        /// <summary>Birinchi kassirning konteksti (baza shu orqali yaratiladi).</summary>
        public AppDbContext A { get; }

        /// <summary>Ikkinchi kassirning konteksti — AYNI bazaga, lekin o'z kuzatuvi bilan.</summary>
        public AppDbContext B { get; }

        public IkkiKassir()
        {
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            A = new AppDbContext(options);
            A.Database.EnsureCreated();
            B = new AppDbContext(options);
        }

        public void Dispose()
        {
            A.Dispose();
            B.Dispose();
            _connection.Dispose();
        }
    }

    // =============================================================================================
    //  1) SOF MANTIQ — yorliqlar
    // =============================================================================================

    [Fact]
    public void PaymentLabel_NaqdVaKarta()
    {
        Assert.Equal("Karta", BookSalesService.PaymentLabel(BookSalesService.PayCard));
        Assert.Equal("Naqd", BookSalesService.PaymentLabel(BookSalesService.PayCash));
    }

    [Fact]
    public void PaymentLabel_Nasiya()
    {
        Assert.Equal("Nasiya", BookSalesService.PaymentLabel(BookSalesService.PayCredit));
    }

    [Fact]
    public void PaymentLabel_NomalumQiymat_XomSatrniOzgartirmaydi()
    {
        // null → bo'sh satr (xabar matnida "💳 To'lov: " bo'sh chiqadi, "null" emas).
        Assert.Equal("", BookSalesService.PaymentLabel(null));
        Assert.Equal("click", BookSalesService.PaymentLabel("click"));
    }

    [Fact]
    public void StatusLabel_UchtaHolat()
    {
        Assert.Equal("Tasdiqlangan", BookSalesService.StatusLabel(BookSalesService.StatusApproved));
        Assert.Equal("Rad etilgan", BookSalesService.StatusLabel(BookSalesService.StatusRejected));
        Assert.Equal("Kutilmoqda", BookSalesService.StatusLabel(BookSalesService.StatusPending));
    }

    [Fact]
    public void StatusLabel_NomalumHolat_KutilmoqdaDeydi()
    {
        Assert.Equal("Kutilmoqda", BookSalesService.StatusLabel(null));
        Assert.Equal("Kutilmoqda", BookSalesService.StatusLabel("qandaydir"));
    }

    [Fact]
    public void Konstantalar_QoidadagiXomSatrlarGaMos()
    {
        // books.md §2: bu satrlar bazada saqlanadi va frontend (bookLabels.ts) ham AYNAN shularni
        // kutadi — o'zgartirish = eski yozuvlar "noma'lum" bo'lib qolishi.
        Assert.Equal("pending", BookSalesService.StatusPending);
        Assert.Equal("approved", BookSalesService.StatusApproved);
        Assert.Equal("rejected", BookSalesService.StatusRejected);
        Assert.Equal("initial", BookSalesService.ReasonInitial);
        Assert.Equal("restock", BookSalesService.ReasonRestock);
        Assert.Equal("sale", BookSalesService.ReasonSale);
        Assert.Equal("correction", BookSalesService.ReasonCorrection);
        Assert.Equal("cash", BookSalesService.PayCash);
        Assert.Equal("card", BookSalesService.PayCard);
        Assert.Equal("credit", BookSalesService.PayCredit);
    }

    // =============================================================================================
    //  2) SOF MANTIQ — Move (ombor harakati)
    // =============================================================================================

    [Fact]
    public void Move_Kirim_QoldiqniOshiradi_VaStockAfterYozadi()
    {
        var book = NewBook(stock: 10);

        var move = BookSalesService.Move(book, 5, BookSalesService.ReasonRestock, "Nashriyotdan", "Admin");

        Assert.Equal(15, book.Stock);
        Assert.Equal(5, move.Qty);
        Assert.Equal(15, move.StockAfter);
        Assert.Equal(BookSalesService.ReasonRestock, move.Reason);
        Assert.Equal(book.Id, move.BookId);
        Assert.Equal(book.Title, move.BookTitle);   // kitob o'chirilsa ham tarix o'qiladi
        Assert.Null(move.OrderId);
    }

    [Fact]
    public void Move_Chiqim_QoldiqniKamaytiradi()
    {
        var book = NewBook(stock: 10);

        var move = BookSalesService.Move(book, -3, BookSalesService.ReasonSale, "", "Admin", "ord-1");

        Assert.Equal(7, book.Stock);
        Assert.Equal(-3, move.Qty);
        Assert.Equal(7, move.StockAfter);
        Assert.Equal("ord-1", move.OrderId);
    }

    [Fact]
    public void Move_KetmaKet_StockAfterZanjiriUzilmaydi()
    {
        var book = NewBook(stock: 0);

        var m1 = BookSalesService.Move(book, 20, BookSalesService.ReasonInitial, "", "Admin");
        var m2 = BookSalesService.Move(book, -3, BookSalesService.ReasonSale, "", "Admin");
        var m3 = BookSalesService.Move(book, 5, BookSalesService.ReasonRestock, "", "Admin");
        var m4 = BookSalesService.Move(book, -1, BookSalesService.ReasonCorrection, "", "Admin");

        Assert.Equal(new[] { 20, 17, 22, 21 }, new[] { m1.StockAfter, m2.StockAfter, m3.StockAfter, m4.StockAfter });
        Assert.Equal(21, book.Stock);
        // Har bir StockAfter = oldingi StockAfter + Qty (hisobot ustuni shu zanjirga tayanadi).
        Assert.Equal(m1.StockAfter + m2.Qty, m2.StockAfter);
        Assert.Equal(m2.StockAfter + m3.Qty, m3.StockAfter);
        Assert.Equal(m3.StockAfter + m4.Qty, m4.StockAfter);
    }

    [Fact]
    public void Move_NullNoteVaCreatedBy_BoshSatrgaAylanadi()
    {
        var book = NewBook();

        var move = BookSalesService.Move(book, 1, BookSalesService.ReasonRestock, null!, null!);

        Assert.Equal("", move.Note);
        Assert.Equal("", move.CreatedBy);
    }

    [Fact]
    public void Move_NolMiqdor_QoldiqniOzgartirmaydi_LekinTarixgaYoziladi()
    {
        // Move o'zi 0 ni rad etmaydi — darvoza chaqiruvchida (BooksController: "Miqdor 0 bo'lmasligi kerak").
        var book = NewBook(stock: 7);

        var move = BookSalesService.Move(book, 0, BookSalesService.ReasonCorrection, "", "Admin");

        Assert.Equal(7, book.Stock);
        Assert.Equal(7, move.StockAfter);
    }

    [Fact]
    public void Move_ManfiyQoldiqqaYolQoyadi_DarvozaChaqiruvchida()
    {
        // HOZIRGI xulq: Move past darajali — manfiy qoldiqni to'smaydi. Himoya BooksController
        // (`AddStock`: "Qoldiq manfiy bo'lib qoladi") va ApproveAsync ichida. Agar kelajakda
        // Move'ning O'ZIGA guard qo'shilsa — shu test o'zgaradi.
        var book = NewBook(stock: 2);

        var move = BookSalesService.Move(book, -5, BookSalesService.ReasonCorrection, "", "Admin");

        Assert.Equal(-3, book.Stock);
        Assert.Equal(-3, move.StockAfter);
    }

    // =============================================================================================
    //  3) MIJOZ/ADMIN MATNLARI
    // =============================================================================================

    [Fact]
    public void CustomerApprovedText_Naqd_KassaQatoriBor()
    {
        var o = NewOrder(NewBook(price: 25000), qty: 2);
        o.PaymentMethod = BookSalesService.PayCash;

        var text = BookSalesService.CustomerApprovedText(o);

        Assert.Contains("tasdiqlandi", text);
        Assert.Contains("Alifbo", text);
        Assert.Contains("2 dona", text);
        Assert.Contains("50 000", text);          // AuditService.Money — bo'sh joy ajratgichi
        Assert.Contains("Naqd", text);
        Assert.Contains("kassasiga topshirasiz", text);
    }

    [Fact]
    public void CustomerApprovedText_Karta_KassaQatoriYoq()
    {
        var o = NewOrder(NewBook());
        o.PaymentMethod = BookSalesService.PayCard;

        var text = BookSalesService.CustomerApprovedText(o);

        Assert.Contains("Karta", text);
        Assert.DoesNotContain("kassasiga topshirasiz", text);
    }

    [Fact]
    public void CustomerRejectedText_SababBoshBolsa_StandartMatn()
    {
        var o = NewOrder(NewBook());
        o.RejectReason = "   ";

        var text = BookSalesService.CustomerRejectedText(o);

        Assert.Contains("rad etildi", text);
        Assert.Contains("Sabab ko'rsatilmagan.", text);
    }

    [Fact]
    public void CustomerRejectedText_SababBorBolsa_AynanShuMatn()
    {
        var o = NewOrder(NewBook());
        o.RejectReason = "Chek o'qilmadi";

        Assert.Contains("Chek o'qilmadi", BookSalesService.CustomerRejectedText(o));
    }

    [Fact]
    public void AdminNewOrderText_Karta_ChekHolatiKorsatiladi()
    {
        var o = NewOrder(NewBook());
        o.PaymentMethod = BookSalesService.PayCard;

        o.ReceiptUrl = "";
        Assert.Contains("Chek: yuborilmagan", BookSalesService.AdminNewOrderText(o));

        o.ReceiptUrl = "/uploads/abc.jpg";
        Assert.Contains("Chek: yuborildi", BookSalesService.AdminNewOrderText(o));
    }

    [Fact]
    public void AdminNewOrderText_Naqd_ChekQatoriUmumanYoq()
    {
        var o = NewOrder(NewBook());
        o.PaymentMethod = BookSalesService.PayCash;

        Assert.DoesNotContain("Chek", BookSalesService.AdminNewOrderText(o));
    }

    [Fact]
    public void AdminNewOrderText_IsmBoshBolsa_Nomalum_TelefonYoqBolsaQatorTushibQoladi()
    {
        var o = NewOrder(NewBook());
        o.CustomerName = "  ";
        o.Phone = "";

        var text = BookSalesService.AdminNewOrderText(o);

        Assert.Contains("Noma'lum", text);
        Assert.DoesNotContain("📞", text);
        Assert.Contains("#1", text);
    }

    // =============================================================================================
    //  4) BOT — callback prefikslari (Handles)
    // =============================================================================================

    [Fact]
    public void Handles_OzPrefikslariniQabulQiladi()
    {
        Assert.True(BookShopBotService.Handles(BookShopBotService.CbBook + "abc"));
        Assert.True(BookShopBotService.Handles(BookShopBotService.CbQty + "3"));
        foreach (var d in new[]
                 {
                     BookShopBotService.CbQtyOther, BookShopBotService.CbPayCash, BookShopBotService.CbPayCard,
                     BookShopBotService.CbConfirm, BookShopBotService.CbSendReceipt, BookShopBotService.CbList,
                     BookShopBotService.CbCancel,
                 })
            Assert.True(BookShopBotService.Handles(d), $"Handles({d}) false qaytardi");
    }

    [Fact]
    public void Handles_BegonaCallbacklarniQabulQilmaydi()
    {
        foreach (var d in new[] { "", "start", "check_sub_menu", "k", "kq", "kqty:3", "kbook" })
            Assert.False(BookShopBotService.Handles(d), $"Handles({d}) true qaytardi");
    }

    [Fact]
    public void Handles_OnlineTestPrefikslariBilanToqnashmaydi()
    {
        // TelegramBotService avval OnlineTestBotService.Handles ni sinaydi — ikkisi kesishsa
        // kitob tugmalari onlayn testga ketib qolardi.
        foreach (var d in new[]
                 {
                     OnlineTestBotService.CbOpen + "1", OnlineTestBotService.CbAnswer + "1:A",
                     OnlineTestBotService.CbGoto + "2", OnlineTestBotService.CbModeButtons,
                     OnlineTestBotService.CbModeText, OnlineTestBotService.CbFinish,
                     OnlineTestBotService.CbConfirm, OnlineTestBotService.CbEdit,
                     OnlineTestBotService.CbCancel, OnlineTestBotService.CbList,
                 })
            Assert.False(BookShopBotService.Handles(d), $"Kitob servisi onlayn test callback'ini oldi: {d}");
    }

    [Fact]
    public void CallbackPrefikslari_Noyob_Va64BaytdanQisqa()
    {
        var all = new[]
        {
            BookShopBotService.CbBook, BookShopBotService.CbQty, BookShopBotService.CbQtyOther,
            BookShopBotService.CbPayCash, BookShopBotService.CbPayCard, BookShopBotService.CbConfirm,
            BookShopBotService.CbSendReceipt, BookShopBotService.CbList, BookShopBotService.CbCancel,
        };

        Assert.Equal(all.Length, all.Distinct().Count());
        // Telegram cheklovi: callback_data ≤ 64 bayt (prefiks + Guid("N") = 32 → hali ham sig'adi).
        foreach (var p in all) Assert.True(p.Length + 36 <= 64, $"Prefiks juda uzun: {p}");
    }

    // =============================================================================================
    //  5) BAZA — buyurtma raqami
    // =============================================================================================

    [Fact]
    public async Task NextOrderNumber_BoshBazada_Bir()
    {
        // Raqam navbati jarayon bo'yicha umumiy (static) — har test o'z bazasi bilan ishlagani
        // uchun oldingi testdan qolgan belgi natijani buzmasin.
        BookSalesService.ResetOrderNumberSequence();
        using var db = TestDb.Sqlite();

        Assert.Equal(1, await BookSalesService.NextOrderNumberAsync(db.Context));
    }

    [Fact]
    public async Task NextOrderNumber_EngKattaRaqamdanKeyingisi()
    {
        BookSalesService.ResetOrderNumberSequence();
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook();
        ctx.Books.Add(book);
        foreach (var n in new[] { 1, 2, 7 })
        {
            var o = NewOrder(book);
            o.Number = n;
            ctx.BookOrders.Add(o);
        }
        await ctx.SaveChangesAsync();

        Assert.Equal(8, await BookSalesService.NextOrderNumberAsync(ctx));
    }

    [Fact]
    public async Task NextOrderNumber_SaqlanmasdanIkkiMartaOlinsa_TAKRORLANMAYDI()
    {
        // POYGA: raqam olingandan keyin buyurtma bazaga yozilgunicha bir necha `await` bor.
        // Ilgari ikkala kassir ham MAX(Number)+1 = #1 ni olib, IKKALA buyurtma #1 bo'lardi.
        BookSalesService.ResetOrderNumberSequence();
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var birinchi = await BookSalesService.NextOrderNumberAsync(ctx);
        var ikkinchi = await BookSalesService.NextOrderNumberAsync(ctx);   // hali HECH BIRI saqlanmadi

        Assert.Equal(1, birinchi);
        Assert.Equal(2, ikkinchi);
    }

    [Fact]
    public async Task NextOrderNumber_XotiradagiBelgi_BazadagiEngKATTAdanOrtaOlmaydi()
    {
        // Belgi bazadan ORQADA qolsa (masalan ilova qayta ishga tushdi) — baza yutadi.
        BookSalesService.ResetOrderNumberSequence();
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook();
        ctx.Books.Add(book);
        var saqlangan = NewOrder(book);
        saqlangan.Number = 40;
        ctx.BookOrders.Add(saqlangan);
        await ctx.SaveChangesAsync();

        Assert.Equal(41, await BookSalesService.NextOrderNumberAsync(ctx));
        Assert.Equal(42, await BookSalesService.NextOrderNumberAsync(ctx));
    }

    // =============================================================================================
    //  6) BAZA — TASDIQLASH (qoldiq faqat shu yerda ayiriladi)
    // =============================================================================================

    [Fact]
    public async Task Approve_QoldiqniAyiradi_HarakatYozadi_HolatniYangilaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10, price: 25000);
        var order = NewOrder(book, qty: 3);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.ApproveAsync(ctx, order, "Superadmin");

        Assert.Null(err);
        Assert.Equal(7, book.Stock);
        Assert.Equal(BookSalesService.StatusApproved, order.Status);
        Assert.Equal("Superadmin", order.DecidedBy);
        Assert.NotNull(order.DecidedAt);

        var move = await ctx.BookStockMoves.SingleAsync();
        Assert.Equal(-3, move.Qty);
        Assert.Equal(7, move.StockAfter);
        Assert.Equal(BookSalesService.ReasonSale, move.Reason);
        Assert.Equal(order.Id, move.OrderId);
        Assert.Contains($"#{order.Number}", move.Note);
    }

    [Fact]
    public async Task Approve_QoldiqAynanYetarli_Nolgacha()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 4);
        var order = NewOrder(book, qty: 4);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin"));
        Assert.Equal(0, book.Stock);
    }

    [Fact]
    public async Task Approve_IkkinchiMarta_QoldiqQaytaAyrilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 3);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin"));
        var err = await BookSalesService.ApproveAsync(ctx, order, "Admin");

        Assert.NotNull(err);
        Assert.Contains("allaqachon hal qilingan", err);
        Assert.Contains("Tasdiqlangan", err);
        Assert.Equal(7, book.Stock);                                  // ikki marta ayirilmadi
        Assert.Equal(1, await ctx.BookStockMoves.CountAsync());       // ikkinchi harakat yozilmadi
    }

    [Fact]
    public async Task Approve_QoldiqYetmasa_HechNarsaOzgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 2);
        var order = NewOrder(book, qty: 5);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.ApproveAsync(ctx, order, "Admin");

        Assert.NotNull(err);
        Assert.Contains("qoldiq 2", err);
        Assert.Contains("buyurtma 5", err);
        Assert.Equal(2, book.Stock);
        Assert.Equal(BookSalesService.StatusPending, order.Status);
        Assert.Null(order.DecidedAt);
        Assert.Empty(await ctx.BookStockMoves.ToListAsync());
    }

    [Fact]
    public async Task Approve_KitobOchirilgan_XatoQaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var order = NewOrder(NewBook());
        order.BookId = "yoq-kitob";
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.ApproveAsync(ctx, order, "Admin");

        Assert.NotNull(err);
        Assert.Contains("Kitob topilmadi", err);
        Assert.Equal(BookSalesService.StatusPending, order.Status);
    }

    [Fact]
    public async Task Approve_RadEtilganBuyurtmani_TasdiqlabBolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 2);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.RejectAsync(ctx, order, "Chek yo'q", "Admin"));
        var err = await BookSalesService.ApproveAsync(ctx, order, "Admin");

        Assert.NotNull(err);
        Assert.Contains("Rad etilgan", err);
        Assert.Equal(10, book.Stock);                          // rad etilganda qoldiq TEGILMAGAN
        Assert.Empty(await ctx.BookStockMoves.ToListAsync());
    }

    // =============================================================================================
    //  7) BAZA — RAD ETISH
    // =============================================================================================

    [Fact]
    public async Task Reject_QoldiqTegilmaydi_SababTrimlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 6);
        var order = NewOrder(book, qty: 2);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.RejectAsync(ctx, order, "  Chek soxta  ", "Admin");

        Assert.Null(err);
        Assert.Equal(BookSalesService.StatusRejected, order.Status);
        Assert.Equal("Chek soxta", order.RejectReason);
        Assert.Equal("Admin", order.DecidedBy);
        Assert.NotNull(order.DecidedAt);
        Assert.Equal(6, book.Stock);
        Assert.Empty(await ctx.BookStockMoves.ToListAsync());
    }

    [Fact]
    public async Task Reject_NullSabab_BoshSatr()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var order = NewOrder(NewBook());
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.RejectAsync(ctx, order, null!, "Admin"));
        Assert.Equal("", order.RejectReason);
    }

    [Fact]
    public async Task Reject_TasdiqlanganBuyurtmani_RadEtibBolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 2);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin"));
        var err = await BookSalesService.RejectAsync(ctx, order, "Fikrim o'zgardi", "Admin");

        Assert.NotNull(err);
        Assert.Contains("Tasdiqlangan", err);
        Assert.Equal(BookSalesService.StatusApproved, order.Status);
        Assert.Equal(8, book.Stock);   // BEKOR QILISH YO'Q: tasdiqlangan buyurtmada qoldiq qaytmaydi
    }

    /// <summary>
    /// Ilgari bu yerda SKIP qilingan test turardi: "tasdiqlangan sotuvni bekor qilib, qoldiqni
    /// omborga qaytarish mantig'i UMUMAN yo'q". Endi u bor — lekin `RejectAsync` orqali EMAS
    /// (rad etish hali BERILMAGAN buyurtma uchun), balki <see cref="BookSalesService.ReturnAsync"/>
    /// bilan: qaytarish qisman ham bo'ladi, shuning uchun holat emas, DONA hisoblanadi.
    /// </summary>
    [Fact]
    public async Task Approve_KeyinQaytarish_QoldiqOmborgaQaytadI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 3);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin"));
        Assert.Equal(7, book.Stock);

        // Qaytarish qoldiqni tiklaydi va teskari harakat yozadi (sotuv + qaytarish = 2 ta).
        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 3, "Xato tasdiqlandi", "Admin"));
        Assert.Equal(10, book.Stock);
        Assert.Equal(2, await ctx.BookStockMoves.CountAsync());
        Assert.True(BookSalesService.IsFullyReturned(order));
        Assert.Equal(0, BookSalesService.NetTotal(order));
    }

    // =============================================================================================
    //  8) BAZA — SNAPSHOT va sessiya cheklovi
    // =============================================================================================

    [Fact]
    public async Task BuyurtmaNarxi_SNAPSHOT_KitobNarxiOzgarsaHamOzgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(price: 25000, stock: 10);
        var order = NewOrder(book, qty: 2);      // UnitPrice=25000, Total=50000
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        book.Price = 40000;
        book.Title = "Alifbo (2-nashr)";
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin"));

        var saved = await ctx.BookOrders.AsNoTracking().SingleAsync();
        Assert.Equal(25000m, saved.UnitPrice);
        Assert.Equal(50000m, saved.Total);
        Assert.Equal("Alifbo", saved.BookTitle);
    }

    [Fact]
    public async Task BotSessiyasi_BittaChatdaBittaSessiya_UnikalIndeks()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.BookBotSessions.Add(new BookBotSession { ChatId = 777, Step = "qty", BookId = "b1" });
        await ctx.SaveChangesAsync();

        ctx.BookBotSessions.Add(new BookBotSession { ChatId = 777, Step = "pay", BookId = "b2" });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task BotSessiyasi_BoshqaChat_MustaqilYashaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.BookBotSessions.Add(new BookBotSession { ChatId = 1, Step = "qty", BookId = "b1" });
        ctx.BookBotSessions.Add(new BookBotSession { ChatId = 2, Step = "receipt", BookId = "b2" });
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await ctx.BookBotSessions.CountAsync());
    }

    [Fact]
    public async Task StockMove_KitobOchirilsaHam_TarixdagiNomSaqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(title: "Fizika 9", stock: 5);
        ctx.Books.Add(book);
        ctx.BookStockMoves.Add(BookSalesService.Move(book, 5, BookSalesService.ReasonInitial, "", "Admin"));
        await ctx.SaveChangesAsync();

        ctx.Books.Remove(book);
        await ctx.SaveChangesAsync();

        var move = await ctx.BookStockMoves.AsNoTracking().SingleAsync();
        Assert.Equal("Fizika 9", move.BookTitle);   // hisobot buzilmaydi
    }

    // =============================================================================================
    //  9) HUJJATLASHTIRILGAN XATOLAR (Skip) — kutilgan to'g'ri xulq
    // =============================================================================================

    [Fact(Skip = "XATO (BookShopBotService.cs:276 HandleTextAsync + BookBotSession.UpdatedAt): bot "
                 + "savdo sessiyasining MUDDATI yo'q. UpdatedAt yoziladi, lekin hech qayerda O'QILMAYDI "
                 + "va eski sessiyalarni tozalaydigan xizmat yo'q. Natija: bir oy oldin kitob ochib "
                 + "tashlab ketgan chatda step='qty' abadiy qoladi va mijozning ISTALGAN 1-4 raqamli "
                 + "matni (masalan '2' — boshqa menyuga javob) kitob soni sifatida yutiladi. "
                 + "Tuzatish: HandleTextAsync/SessionWithBookAsync sessiya yoshini tekshirsin "
                 + "(masalan 30 daqiqa) yoki fon xizmati eskisini o'chirsin.")]
    public async Task BotSessiyasi_EskirganSessiya_MatnniYutmasligiKerak_KUTILGAN()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var eski = AppClock.Now.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss");
        ctx.BookBotSessions.Add(new BookBotSession
        {
            ChatId = 55, Step = "qty", BookId = "b1", UpdatedAt = eski,
        });
        await ctx.SaveChangesAsync();

        // KUTILGAN: 30 kunlik sessiya "eskirgan" hisoblanadi va bazadan yo'qoladi/inobatga olinmaydi.
        var session = await ctx.BookBotSessions.FirstAsync(s => s.ChatId == 55);
        var yosh = AppClock.Now - DateTime.Parse(session.UpdatedAt);
        Assert.True(yosh > TimeSpan.FromMinutes(30));
        Assert.Empty(await ctx.BookBotSessions.Where(s => s.ChatId == 55).ToListAsync());
    }

    // =============================================================================================
    //  QO'LDA SOTUV (markazda, bot orqali emas) — BooksController.ManualSale oqimi
    // =============================================================================================

    /// <summary>Qo'lda sotuvda controller nima qilishini takrorlaydi: SAQLANMAGAN pending buyurtma
    /// qo'shiladi va darhol <c>ApproveAsync</c> chaqiriladi (bitta SaveChanges).</summary>
    private static BookOrder NewManualOrder(Book book, int qty = 1) => new()
    {
        Number = 1,
        ChatId = 0,                                    // Telegram chat yo'q
        Source = BookSalesService.SourceManual,
        CustomerName = "Ali Valiyev",
        Phone = "998901234567",
        StudentId = "st-1",
        BookId = book.Id,
        BookTitle = book.Title,
        UnitPrice = book.Price,
        Qty = qty,
        Total = book.Price * qty,
        PaymentMethod = BookSalesService.PayCard,
        CardLast4 = "1234",
        PaidTime = "14:30",
        Status = BookSalesService.StatusPending,
    };

    [Fact]
    public void SourceLabel_QoldaVaBot()
    {
        Assert.Equal("Qo'lda", BookSalesService.SourceLabel(BookSalesService.SourceManual));
        Assert.Equal("Bot", BookSalesService.SourceLabel(BookSalesService.SourceBot));
        Assert.Equal("Bot", BookSalesService.SourceLabel(""));      // eski qatorlar
    }

    [Fact]
    public async Task QoldaSotuv_BuyurtmaVaQoldiqBittaSaveChangesdaYoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10, price: 25000);
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();

        var order = NewManualOrder(book, qty: 3);
        ctx.BookOrders.Add(order);                     // hali SAQLANMAGAN
        var err = await BookSalesService.ApproveAsync(ctx, order, "Kassir");

        Assert.Null(err);
        Assert.Equal(7, book.Stock);
        Assert.Equal(BookSalesService.StatusApproved, order.Status);
        Assert.Equal("Kassir", order.DecidedBy);

        // Buyurtma ham, ombor harakati ham bazaga tushdi.
        var saved = await ctx.BookOrders.SingleAsync();
        Assert.Equal(BookSalesService.SourceManual, saved.Source);
        Assert.Equal(0, saved.ChatId);                 // botga xabar yuborilmaydi
        Assert.Equal("1234", saved.CardLast4);
        Assert.Equal("14:30", saved.PaidTime);
        Assert.Equal("st-1", saved.StudentId);
        var move = await ctx.BookStockMoves.SingleAsync();
        Assert.Equal(-3, move.Qty);
        Assert.Equal(BookSalesService.ReasonSale, move.Reason);
    }

    [Fact]
    public async Task QoldaSotuv_OQUVCHISIZ_ham_sotiladi()
    {
        // Markazda o'qimaydigan xaridor (ota-ona, o'tkinchi): `StudentId` yo'q va ism ham bo'sh
        // qolishi mumkin. Ilgari qo'lda sotuvda o'quvchini tanlash MAJBURIY edi va kassir bunday
        // sotuv uchun soxta o'quvchi yaratishga majbur bo'lardi. Ombor mantig'i o'zgarmaydi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 4, price: 30000);
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();

        var order = NewManualOrder(book, qty: 2);
        order.StudentId = null;
        order.CustomerName = "";
        order.Phone = "";
        ctx.BookOrders.Add(order);

        var err = await BookSalesService.ApproveAsync(ctx, order, "Kassir");

        Assert.Null(err);
        Assert.Equal(2, book.Stock);
        var saved = await ctx.BookOrders.SingleAsync();
        Assert.Null(saved.StudentId);
        Assert.Equal("", saved.CustomerName);
        Assert.Equal(BookSalesService.StatusApproved, saved.Status);
        // Sotuv analitikaga odatdagidek tushadi (ombor harakati yozilgan).
        var move = await ctx.BookStockMoves.SingleAsync();
        Assert.Equal(-2, move.Qty);
    }

    [Fact]
    public async Task QoldaSotuv_QoldiqYetmasa_BuyurtmaUMUMANYOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 2);
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();

        var order = NewManualOrder(book, qty: 5);
        ctx.BookOrders.Add(order);
        var err = await BookSalesService.ApproveAsync(ctx, order, "Kassir");

        Assert.NotNull(err);
        Assert.Contains("qoldiq 2", err);
        // ApproveAsync SaveChanges chaqirmagani uchun controller 400 qaytaradi va yarim holatdagi
        // "pending" qatori bazada QOLMAYDI — qo'lda sotuvning asosiy kafolati.
        Assert.Empty(await ctx.BookOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.BookStockMoves.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void QoldaSotuv_SotuvdanOlinganKitob_SOTILMAYDI()
    {
        // Frontend sotuvdan olingan kitobni ro'yxatda ko'rsatmaydi, lekin `POST /orders/manual`
        // to'g'ridan-to'g'ri chaqirilsa ilgari hech qanday to'siq yo'q edi.
        var book = NewBook();
        Assert.Null(BookSalesService.ManualSaleBookError(book));

        book.IsActive = false;
        var err = BookSalesService.ManualSaleBookError(book);
        Assert.NotNull(err);
        Assert.Contains("sotuvdan olingan", err);

        Assert.Equal("Kitob topilmadi", BookSalesService.ManualSaleBookError(null));
    }

    // =============================================================================================
    //  NASIYA — kitob berildi, pul keyin olinadi
    //
    //  Qoida (.claude/rules/books.md §2.4): nasiya sotuv ham odatdagidek TASDIQLANADI va
    //  qoldiqdan ayiriladi (kitob mijozning qo'lida), lekin PUL olinmaguncha `PaidAt` bo'sh
    //  turadi va summa tushumga emas QARZGA sanaladi.
    // =============================================================================================

    /// <summary>Nasiyaga sotilgan (tasdiqlangan, lekin hali to'lanmagan) buyurtma.</summary>
    private static BookOrder NewCreditOrder(Book book, int qty = 1, DateTime? due = null)
    {
        var o = NewManualOrder(book, qty);
        o.PaymentMethod = BookSalesService.PayCredit;
        o.CardLast4 = null;
        o.PaidTime = null;
        o.DueDate = due;
        return o;
    }

    [Fact]
    public void IsPaid_NaqdVaKarta_TasdiqlanganiToLanganiDeganI()
    {
        // ESKI qatorlarda `PaidAt` bo'sh (migratsiyagacha yozilgan) — ular baribir to'langan.
        var o = NewOrder(NewBook(), status: BookSalesService.StatusApproved);
        o.PaidAt = null;
        Assert.True(BookSalesService.IsPaid(o));

        o.Status = BookSalesService.StatusPending;
        Assert.False(BookSalesService.IsPaid(o));    // hali tasdiqlanmagan = pul olinmagan
    }

    [Fact]
    public void IsPaid_Nasiya_FaqatPaidAtToLganda()
    {
        var o = NewCreditOrder(NewBook());
        o.Status = BookSalesService.StatusApproved;

        Assert.True(BookSalesService.IsCredit(o));
        Assert.False(BookSalesService.IsPaid(o));   // tasdiqlangan, lekin pul olinmagan

        o.PaidAt = AppClock.Now;
        Assert.True(BookSalesService.IsPaid(o));
    }

    [Fact]
    public void EffectiveMethod_NasiyaYopilganUsulniQaytaradi()
    {
        var o = NewCreditOrder(NewBook());
        Assert.Equal(BookSalesService.PayCredit, BookSalesService.EffectiveMethod(o));

        o.SettledMethod = BookSalesService.PayCash;
        Assert.Equal(BookSalesService.PayCash, BookSalesService.EffectiveMethod(o));

        var naqd = NewOrder(NewBook());
        Assert.Equal(BookSalesService.PayCash, BookSalesService.EffectiveMethod(naqd));
    }

    [Fact]
    public void IsOverdue_MuddatSizNasiya_HECHQACHONkechikkanEMAS()
    {
        // Kassir muddat qo'ymagan bo'lsa uni "kechikkan" deb ayblash noto'g'ri bo'lardi.
        var o = NewCreditOrder(NewBook(), due: null);
        o.Status = BookSalesService.StatusApproved;

        Assert.False(BookSalesService.IsOverdue(o, new DateTime(2030, 1, 1)));
    }

    [Fact]
    public void IsOverdue_MuddatOtganVaToLANMAGAN()
    {
        var bugun = new DateTime(2026, 8, 7);
        var o = NewCreditOrder(NewBook(), due: bugun.AddDays(-1));
        o.Status = BookSalesService.StatusApproved;

        Assert.True(BookSalesService.IsOverdue(o, bugun));

        // AYNAN bugungi muddat hali o'tmagan.
        o.DueDate = bugun;
        Assert.False(BookSalesService.IsOverdue(o, bugun));

        // To'langan nasiya — muddat o'tgan bo'lsa ham qarz emas.
        o.DueDate = bugun.AddDays(-5);
        o.PaidAt = AppClock.Now;
        Assert.False(BookSalesService.IsOverdue(o, bugun));
    }

    [Fact]
    public void IsOverdue_NaqdSotuv_NASIYAEMAS()
    {
        var o = NewOrder(NewBook(), status: BookSalesService.StatusApproved);
        o.DueDate = new DateTime(2020, 1, 1);   // ma'nosiz, lekin bo'lib qolsa ham
        Assert.False(BookSalesService.IsOverdue(o, new DateTime(2026, 8, 7)));
    }

    [Fact]
    public void CreditCustomerError_NasiyadaXaridorMAJBURIY()
    {
        // Naqd/kartada xaridor ixtiyoriy (chetdan kelgan odamga ham sotiladi).
        Assert.Null(BookSalesService.CreditCustomerError(BookSalesService.PayCash, null, null));
        Assert.Null(BookSalesService.CreditCustomerError(BookSalesService.PayCard, null, "  "));

        // Nasiyada — o'quvchi YOKI ism bo'lishi shart.
        var err = BookSalesService.CreditCustomerError(BookSalesService.PayCredit, null, "   ");
        Assert.NotNull(err);
        Assert.Contains("xaridor", err);

        Assert.Null(BookSalesService.CreditCustomerError(BookSalesService.PayCredit, "st-1", null));
        Assert.Null(BookSalesService.CreditCustomerError(BookSalesService.PayCredit, null, "Ali Valiyev"));
    }

    [Fact]
    public async Task Approve_Naqd_PaidAtniQOYADI_NasiyaDaQOYMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var naqd = NewOrder(book, qty: 1);
        var nasiya = NewCreditOrder(book, qty: 1);
        nasiya.Number = 2;
        nasiya.Status = BookSalesService.StatusPending;
        ctx.Books.Add(book);
        ctx.BookOrders.AddRange(naqd, nasiya);
        await ctx.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(ctx, naqd, "Kassir"));
        Assert.Null(await BookSalesService.ApproveAsync(ctx, nasiya, "Kassir"));

        Assert.Equal(naqd.DecidedAt, naqd.PaidAt);   // pul tasdiqlash paytida olindi
        Assert.Null(nasiya.PaidAt);                  // nasiya — qarz bo'lib qoldi
        // IKKALASIDA ham qoldiq ayirilgan: kitob mijozning qo'lida.
        Assert.Equal(8, book.Stock);
        Assert.Equal(2, await ctx.BookStockMoves.CountAsync());
    }

    [Fact]
    public async Task PayCredit_ToLovniQabulQiladi_OMBORGATEGMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5, price: 30000);
        var order = NewCreditOrder(book, qty: 2);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));
        Assert.Equal(3, book.Stock);
        var harakatlar = await ctx.BookStockMoves.CountAsync();

        var err = await BookSalesService.PayCreditAsync(
            ctx, order, BookSalesService.PayCard, "1234", "Kassir-2");

        Assert.Null(err);
        Assert.NotNull(order.PaidAt);
        Assert.Equal("Kassir-2", order.PaidBy);
        Assert.Equal(BookSalesService.PayCard, order.SettledMethod);
        Assert.Equal("1234", order.CardLast4);
        Assert.True(BookSalesService.IsPaid(order));
        // OMBOR TEGILMAYDI — kitob sotuv paytida berilgan.
        Assert.Equal(3, book.Stock);
        Assert.Equal(harakatlar, await ctx.BookStockMoves.CountAsync());
    }

    [Fact]
    public async Task PayCredit_Naqd_EskiKartaRaqaminiTOZALAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5);
        var order = NewCreditOrder(book);
        order.CardLast4 = "9999";              // qandaydir eski qiymat qolib ketgan bo'lsa
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));

        Assert.Null(await BookSalesService.PayCreditAsync(
            ctx, order, BookSalesService.PayCash, null, "Kassir"));

        Assert.Null(order.CardLast4);          // naqd to'lovda karta raqami turmasin
        Assert.Equal(BookSalesService.PayCash, order.SettledMethod);
    }

    [Fact]
    public async Task PayCredit_IKKINCHImarta_QabulQILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5);
        var order = NewCreditOrder(book);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));

        Assert.Null(await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K"));
        var birinchiPaytI = order.PaidAt;

        var err = await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K2");

        Assert.NotNull(err);
        Assert.Contains("allaqachon to'langan", err);
        Assert.Equal(birinchiPaytI, order.PaidAt);   // ikkinchi tasdiq vaqtni surib yubormadi
        Assert.Equal("K", order.PaidBy);
    }

    [Fact]
    public async Task PayCredit_NasiyaBOLMAGANbuyurtma_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5);
        var order = NewOrder(book);                 // naqd
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));

        var err = await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K");

        Assert.NotNull(err);
        Assert.Contains("nasiyaga sotilmagan", err);
    }

    [Fact]
    public async Task PayCredit_NOTOGRIusul_RadEtiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5);
        var order = NewCreditOrder(book);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));

        var err = await BookSalesService.PayCreditAsync(ctx, order, "click", null, "K");

        Assert.NotNull(err);
        Assert.Contains("naqd yoki karta", err);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public async Task PayCredit_TasdiqlanmaganBuyurtma_QabulQILINMAYDI()
    {
        // Nasiya "pending" holatda qolib ketgan bo'lsa (kitob hali berilmagan) — pulni
        // qabul qilib bo'lmaydi, avval sotuvning o'zi tasdiqlanishi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 5);
        var order = NewCreditOrder(book);
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K");

        Assert.NotNull(err);
        Assert.Contains("holati mos emas", err);
        Assert.Null(order.PaidAt);
    }

    // =============================================================================================
    //  QOLDIQ POYGASI (race) — `Book.Stock` konkurentlik tokeni
    // =============================================================================================

    [Fact]
    public async Task Qoldiq_KonkurentlikTokeni_EskirganQiymatUstigaYOZTIRMAYDI()
    {
        using var kassirlar = new IkkiKassir();
        var book = NewBook(stock: 1);
        kassirlar.A.Books.Add(book);
        await kassirlar.A.SaveChangesAsync();

        // Ikkinchi kassir kitobni O'Z kontekstida o'qidi — Stock=1 ni ko'rib turibdi.
        var eskirgan = await kassirlar.B.Books.SingleAsync(x => x.Id == book.Id);
        Assert.Equal(1, eskirgan.Stock);

        // Birinchi kassir sotdi — bazada qoldiq 0 bo'ldi.
        book.Stock = 0;
        await kassirlar.A.SaveChangesAsync();

        // Ikkinchi kassir eskirgan Stock=1 ustiga yozmoqchi: EF `WHERE Id=@id AND Stock=1` yozadi,
        // 0 qator yangilanadi → istisno. Token bo'lmaganda bu jimgina "0" bo'lib o'tib ketardi.
        eskirgan.Stock = 0;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => kassirlar.B.SaveChangesAsync());
    }

    [Fact]
    public async Task Approve_IkkiKassirBirVaqtda_OXIRGISI_TushunarliXATOoladi()
    {
        using var kassirlar = new IkkiKassir();
        var book = NewBook(stock: 1);                 // omborda BITTA dona
        var birinchi = NewOrder(book, qty: 1);
        var ikkinchi = NewOrder(book, qty: 1);
        ikkinchi.Number = 2;
        kassirlar.A.Books.Add(book);
        kassirlar.A.BookOrders.Add(birinchi);
        kassirlar.A.BookOrders.Add(ikkinchi);
        await kassirlar.A.SaveChangesAsync();

        // Ikkinchi kassir o'z so'rovida buyurtmani va kitobni oldin o'qib qo'ydi (qoldiq 1 ko'rinadi).
        var ikkinchiB = await kassirlar.B.BookOrders.SingleAsync(o => o.Id == ikkinchi.Id);
        var bookB = await kassirlar.B.Books.SingleAsync(x => x.Id == book.Id);
        Assert.Equal(1, bookB.Stock);

        // Birinchi kassir ulgurdi — yagona dona sotildi.
        Assert.Null(await BookSalesService.ApproveAsync(kassirlar.A, birinchi, "Kassir-1"));
        Assert.Equal(0, book.Stock);

        // Ikkinchi kassir tasdiqlaydi: xotirasidagi tekshiruv (1 >= 1) o'tadi, lekin YOZUV rad etiladi.
        var err = await BookSalesService.ApproveAsync(kassirlar.B, ikkinchiB, "Kassir-2");

        Assert.NotNull(err);
        Assert.Contains("boshqa amalda o'zgardi", err);

        // BAZA: faqat BITTA sotuv yozilgan, qoldiq manfiyga tushmagan, ikkinchi buyurtma pending.
        Assert.Equal(1, await kassirlar.A.BookStockMoves.AsNoTracking().CountAsync());
        Assert.Equal(0, (await kassirlar.A.Books.AsNoTracking().SingleAsync(x => x.Id == book.Id)).Stock);
        var bazada = await kassirlar.A.BookOrders.AsNoTracking().SingleAsync(o => o.Id == ikkinchi.Id);
        Assert.Equal(BookSalesService.StatusPending, bazada.Status);

        // XOTIRA: ikkinchi kassirning kontekstida ham "sotilgan" ko'rinish qolmadi.
        Assert.Equal(1, bookB.Stock);
        Assert.Equal(BookSalesService.StatusPending, ikkinchiB.Status);
        Assert.Null(ikkinchiB.DecidedAt);
    }

    [Fact]
    public async Task Approve_BoshqaKitobningQoldigiOzgargani_TASDIQLASHNIBUZMAYDI()
    {
        // Token faqat AYNI kitobga tegishli — parallel sotuvlar bir-birini bekorga to'smasin.
        using var kassirlar = new IkkiKassir();
        var alifbo = NewBook("Alifbo", stock: 5);
        var fizika = NewBook("Fizika 9", stock: 5);
        var order = NewOrder(fizika, qty: 2);
        kassirlar.A.Books.Add(alifbo);
        kassirlar.A.Books.Add(fizika);
        kassirlar.A.BookOrders.Add(order);
        await kassirlar.A.SaveChangesAsync();

        var fizikaB = await kassirlar.B.Books.SingleAsync(x => x.Id == fizika.Id);
        var orderB = await kassirlar.B.BookOrders.SingleAsync(o => o.Id == order.Id);

        // Birinchi kassir BOSHQA kitobning qoldig'ini o'zgartirdi.
        alifbo.Stock = 1;
        await kassirlar.A.SaveChangesAsync();

        Assert.Null(await BookSalesService.ApproveAsync(kassirlar.B, orderB, "Kassir-2"));
        Assert.Equal(3, fizikaB.Stock);
    }

    // =============================================================================================
    //  O'QUVCHI QIDIRUVI — telefon mosligi (qo'lda sotuv oynasi)
    // =============================================================================================

    [Fact]
    public void PhoneMatches_MamlakatKODI_BEGONAoquvchilarniTortmaydi()
    {
        // XATO edi: bazada hamma raqam "+998..." bo'lgani uchun xom raqamlar ustidagi Contains
        // "9989" so'roviga deyarli BUTUN ro'yxatni qaytarardi.
        const string saqlangan = "+998-90-123-45-67";

        Assert.False(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("9989")));
        Assert.False(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("998")));
        Assert.False(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("7777")));
    }

    [Fact]
    public void PhoneMatches_MahalliyRaqamBolagiga_MosKeladi()
    {
        const string saqlangan = "+998-90-123-45-67";

        Assert.True(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("901234567")));
        Assert.True(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("+998 90 123 45 67")));
        Assert.True(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("9012")));   // boshi
        Assert.True(BookSalesService.PhoneMatches(saqlangan, PhoneUtil.Key("4567")));   // oxirgi 4 raqam
    }

    [Fact]
    public void PhoneMatches_JudaQisqaYokiBoshRaqam_MosKELMAYDI()
    {
        Assert.False(BookSalesService.PhoneMatches("+998-90-123-45-67", PhoneUtil.Key("901")));
        Assert.False(BookSalesService.PhoneMatches(null, PhoneUtil.Key("9012")));
        Assert.False(BookSalesService.PhoneMatches("", PhoneUtil.Key("9012")));
        Assert.False(BookSalesService.PhoneMatches("+998-90-123-45-67", PhoneUtil.Key("")));
    }

    [Fact]
    public async Task OquvchiQidiruvi_9989_BUTUNROYXATNIqaytarmaydi()
    {
        // Controller mantig'ining takrori: telefon bo'yicha nomzodlarni ajratish.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.AddRange(
            new Student { FullName = "Ali Valiyev", Phone = PhoneUtil.Normalize("901234567") },
            new Student { FullName = "Vali Aliyev", Phone = PhoneUtil.Normalize("935556677") },
            new Student { FullName = "Zuhra Karimova", ParentPhone = PhoneUtil.Normalize("939989900") });
        await ctx.SaveChangesAsync();

        var rows = await ctx.Students.AsNoTracking()
            .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone })
            .ToListAsync();

        List<string> Topilgan(string soz)
        {
            var key = PhoneUtil.Key(soz);
            return rows
                .Where(r => BookSalesService.PhoneMatches(r.Phone, key)
                            || BookSalesService.PhoneMatches(r.ParentPhone, key))
                .Select(r => r.FullName)
                .ToList();
        }

        // "9989" — faqat MAHALLIY raqami shu bo'lakni o'z ichiga olgan o'quvchi (mamlakat kodi emas).
        Assert.Equal(new[] { "Zuhra Karimova" }, Topilgan("9989"));
        Assert.Equal(new[] { "Ali Valiyev" }, Topilgan("901234567"));
        Assert.Equal(new[] { "Vali Aliyev" }, Topilgan("5566"));
        Assert.Empty(Topilgan("998"));   // mamlakat kodi — 4 raqamdan qisqa, umuman qidirilmaydi
    }

    // =============================================================================================
    //  QAYTARISH (vozvrat) — `BookSalesService.ReturnAsync` va sof yordamchilar
    //
    //  Kutilayotgan xulq (.claude/rules/books.md, "Qaytarish"):
    //    • qaytarilgan dona OMBORGA qaytadi ("return" harakati bilan);
    //    • sotuv summasidan qaytarilgan qismi AYIRILADI (NetQty/NetTotal — hisobotlarning manbai);
    //    • buyurtma HOLATI o'zgarmaydi (qaytarish qisman ham bo'ladi);
    //    • pul faqat OLINGAN bo'lsa qaytariladi — to'lanmagan nasiyada qarz kamayadi, xolos;
    //    • sotilganidan ko'p qaytarib bo'lmaydi; tasdiqlanmagan sotuvni umuman qaytarib bo'lmaydi.
    // =============================================================================================

    /// <summary>Tasdiqlangan (naqd) sotuv — qaytarish uchun tayyor buyurtma yasaydi.</summary>
    private static async Task<(AppDbContext Ctx, Book Book, BookOrder Order)> SotilganAsync(
        AppDbContext ctx, int stock = 10, int qty = 3, decimal price = 25000,
        string method = BookSalesService.PayCash)
    {
        var book = NewBook(price: price, stock: stock);
        var order = NewOrder(book, qty: qty);
        order.PaymentMethod = method;
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Kassir"));
        return (ctx, book, order);
    }

    [Fact]
    public void NetQty_NetTotal_QaytarilganiAyiriladi()
    {
        var book = NewBook(price: 20000);
        var order = NewOrder(book, qty: 3);   // 60 000

        Assert.Equal(3, BookSalesService.NetQty(order));
        Assert.Equal(60000, BookSalesService.NetTotal(order));
        Assert.False(BookSalesService.IsFullyReturned(order));

        order.ReturnedQty = 1;
        Assert.Equal(2, BookSalesService.NetQty(order));
        Assert.Equal(40000, BookSalesService.NetTotal(order));
        Assert.Equal(20000, BookSalesService.ReturnedAmount(order));
        Assert.False(BookSalesService.IsFullyReturned(order));

        order.ReturnedQty = 3;
        Assert.Equal(0, BookSalesService.NetQty(order));
        Assert.Equal(0, BookSalesService.NetTotal(order));
        Assert.True(BookSalesService.IsFullyReturned(order));
    }

    [Fact]
    public async Task Qaytarish_QoldiqniOSHIRADI_HarakatYozadi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await SotilganAsync(db.Context, stock: 10, qty: 3);
        Assert.Equal(7, book.Stock);

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 3, "Kitob yaroqsiz", "Kassir"));

        Assert.Equal(10, book.Stock);
        Assert.Equal(3, order.ReturnedQty);
        Assert.NotNull(order.ReturnedAt);
        Assert.Equal("Kassir", order.ReturnedBy);
        Assert.Equal("Kitob yaroqsiz", order.ReturnReason);
        // Naqd sotuvda pul olingan edi — demak mijozga qaytariladi.
        Assert.Equal(order.Total, order.RefundedAmount);
        // HOLAT o'zgarmaydi: qaytarish "rad etish" emas.
        Assert.Equal(BookSalesService.StatusApproved, order.Status);

        var move = await ctx.BookStockMoves.OrderByDescending(m => m.CreatedAt).FirstAsync();
        Assert.Equal(BookSalesService.ReasonReturn, move.Reason);
        Assert.Equal(3, move.Qty);
        Assert.Equal(10, move.StockAfter);
        Assert.Equal(order.Id, move.OrderId);
    }

    [Fact]
    public async Task Qaytarish_QISMAN_QolganiSotuvdaQoladi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await SotilganAsync(db.Context, stock: 10, qty: 3, price: 20000);

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir"));

        Assert.Equal(8, book.Stock);                                  // 10 − 3 + 1
        Assert.Equal(2, BookSalesService.NetQty(order));
        Assert.Equal(40000, BookSalesService.NetTotal(order));        // 60 000 − 20 000
        Assert.Equal(20000, order.RefundedAmount);
        Assert.False(BookSalesService.IsFullyReturned(order));

        // Ikkinchi qism ham qaytarilishi mumkin — sonlar QO'SHILIB boradi.
        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 2, "", "Kassir"));
        Assert.Equal(10, book.Stock);
        Assert.Equal(3, order.ReturnedQty);
        Assert.Equal(60000, order.RefundedAmount);
        Assert.True(BookSalesService.IsFullyReturned(order));
    }

    [Fact]
    public async Task Qaytarish_SOTILGANIDANKOP_QABULQILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await SotilganAsync(db.Context, stock: 10, qty: 2);

        var err = await BookSalesService.ReturnAsync(ctx, order, 3, "", "Kassir");

        Assert.NotNull(err);
        Assert.Contains("2 dona", err);
        Assert.Equal(8, book.Stock);          // ombor TEGILMAGAN
        Assert.Equal(0, order.ReturnedQty);
        Assert.Equal(1, await ctx.BookStockMoves.CountAsync());   // faqat sotuv harakati
    }

    [Fact]
    public async Task Qaytarish_IKKINCHImarta_QOLGANIDANKOP_bolmaydi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, _, order) = await SotilganAsync(db.Context, qty: 2);

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 2, "", "Kassir"));
        var err = await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir");

        Assert.NotNull(err);
        Assert.Contains("to'liq qaytarilgan", err);
        Assert.Equal(2, order.ReturnedQty);
    }

    [Fact]
    public void Qaytarish_NOLYOKIMANFIY_son_QABULQILINMAYDI()
    {
        var order = NewOrder(NewBook(), qty: 2, status: BookSalesService.StatusApproved);

        Assert.Contains("kamida 1 dona", BookSalesService.ReturnError(order, 0));
        Assert.Contains("kamida 1 dona", BookSalesService.ReturnError(order, -1));
        Assert.Null(BookSalesService.ReturnError(order, 2));
    }

    [Fact]
    public async Task Qaytarish_TASDIQLANMAGANsotuv_QABULQILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 2);   // pending — kitob hali berilmagan
        ctx.Books.Add(book);
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir");

        Assert.NotNull(err);
        Assert.Contains("Rad etish", err);
        Assert.Equal(10, book.Stock);         // kutilayotgan buyurtma qoldiqqa tegmagan edi
        Assert.Equal(0, order.ReturnedQty);
    }

    [Fact]
    public async Task Qaytarish_TOLANMAGANNASIYA_PULQAYTARILMAYDI_QARZKAMAYADI()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await SotilganAsync(
            db.Context, stock: 10, qty: 2, price: 30000, method: BookSalesService.PayCredit);
        Assert.False(BookSalesService.IsPaid(order));   // nasiya: pul hali olinmagan

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir"));

        Assert.Equal(9, book.Stock);
        // PUL CHIQMAYDI — qarz kamayadi (30 000 qolgan).
        Assert.Equal(0, order.RefundedAmount);
        Assert.Equal(30000, BookSalesService.NetTotal(order));
    }

    [Fact]
    public async Task Qaytarish_TOLANGANNASIYA_PULQAYTARILADI()
    {
        using var db = TestDb.Sqlite();
        var (ctx, _, order) = await SotilganAsync(
            db.Context, qty: 2, price: 30000, method: BookSalesService.PayCredit);
        Assert.Null(await BookSalesService.PayCreditAsync(
            ctx, order, BookSalesService.PayCash, null, "Kassir"));

        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir"));

        Assert.Equal(30000, order.RefundedAmount);   // pul olingan edi — qaytariladi
        Assert.Equal(30000, BookSalesService.NetTotal(order));
    }

    [Fact]
    public async Task ToLIQQAYTARILGANnasiya_TOLOVQABULQILINMAYDI()
    {
        using var db = TestDb.Sqlite();
        var (ctx, _, order) = await SotilganAsync(
            db.Context, qty: 2, method: BookSalesService.PayCredit);
        Assert.Null(await BookSalesService.ReturnAsync(ctx, order, 2, "", "Kassir"));

        var err = await BookSalesService.PayCreditAsync(
            ctx, order, BookSalesService.PayCash, null, "Kassir");

        Assert.NotNull(err);
        Assert.Contains("to'liq qaytarilgan", err);
        Assert.Null(order.PaidAt);
    }

    /// <summary>
    /// QOLDIQ POYGASI qaytarishda ham: ikki kassir bir vaqtda qaytarsa, ikkinchisi eskirgan
    /// qoldiq ustiga yozmaydi — tushunarli xato oladi va HECH NARSA o'zgarmaydi
    /// (`Book.Stock` konkurentlik tokeni; `ApproveAsync` bilan bir xil naqsh).
    /// </summary>
    [Fact]
    public async Task Qaytarish_IkkiKassirBirVaqtda_OXIRGISI_TushunarliXATOoladi()
    {
        using var kassir = new IkkiKassir();
        var book = NewBook(stock: 10);
        var order = NewOrder(book, qty: 2);
        kassir.A.Books.Add(book);
        kassir.A.BookOrders.Add(order);
        await kassir.A.SaveChangesAsync();
        Assert.Null(await BookSalesService.ApproveAsync(kassir.A, order, "Kassir A"));

        // Ikkinchi kassir buyurtmani VA kitobni o'z so'rovida OLDINDAN o'qib qo'ydi (qoldiq: 8) —
        // aynan shu "eskirgan" nusxa poygani yuzaga keltiradi.
        var b = await kassir.B.BookOrders.SingleAsync(o => o.Id == order.Id);
        var bookB = await kassir.B.Books.SingleAsync(x => x.Id == book.Id);
        Assert.Equal(8, bookB.Stock);

        // Birinchi kassir ulgurdi — bazada qoldiq 9 bo'ldi.
        Assert.Null(await BookSalesService.ReturnAsync(kassir.A, order, 1, "", "Kassir A"));
        var err = await BookSalesService.ReturnAsync(kassir.B, b, 1, "", "Kassir B");

        Assert.NotNull(err);
        Assert.Contains("qaytadan urinib", err);
        // XOTIRA: ikkinchi kassirning kontekstida "qaytarilgan" ko'rinish qolmadi.
        Assert.Equal(0, b.ReturnedQty);
        Assert.Null(b.ReturnedAt);
        Assert.Equal(8, bookB.Stock);
        // BAZA: faqat BITTA qaytarish yozilgan (sotuv + bitta qaytarish = 2 ta harakat).
        var saqlangan = await kassir.A.Books.AsNoTracking().FirstAsync(x => x.Id == book.Id);
        Assert.Equal(9, saqlangan.Stock);
        Assert.Equal(2, await kassir.A.BookStockMoves.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Qaytarish_KitobOchirilgan_XatoQaytaradi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await SotilganAsync(db.Context, qty: 1);
        ctx.Books.Remove(book);
        await ctx.SaveChangesAsync();

        var err = await BookSalesService.ReturnAsync(ctx, order, 1, "", "Kassir");

        Assert.NotNull(err);
        Assert.Contains("Kitob topilmadi", err);
        Assert.Equal(0, order.ReturnedQty);
    }
}
