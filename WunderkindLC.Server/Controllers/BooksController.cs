using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KITOBLAR SOTUVI — "O'quv bo'limi → Kitoblar sotuvi" bo'limining API'si:
/// <list type="bullet">
///   <item><b>Ombor</b> — kitob CRUD (nom, narx, muqova, tavsif) + qoldiq kirim/korreksiya
///     (<c>POST /{id}/stock</c>) va butun ombor harakatlari tarixi.</item>
///   <item><b>Buyurtmalar</b> — botdan tushgan buyurtmalarni tasdiqlash/rad etish. Tasdiqlanganda
///     qoldiqdan ayiriladi (<see cref="BookSalesService.ApproveAsync"/>) va mijozga Telegram'da
///     xabar ketadi; rad etilganda sabab bilan xabar ketadi.</item>
///   <item><b>Analitika</b> — davr bo'yicha sotuv (dona/tushum), naqd va karta kesimi, kunlik grafik,
///     kitob kesimidagi top, qoldiq va kirim jami. Barchasi Excel'ga yuklanadi.</item>
///   <item><b>Sozlamalar</b> — botda ko'rinadigan KARTA rekvizitlari (CenterMeta'da).</item>
/// </list>
/// Ruxsat kaliti: <c>books</c> (xodim uchun; admin/superadmin — cheklovsiz).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("books")]
[Route("api/admin/books")]
public class BooksController(AppDbContext db, TelegramService telegram, IWebHostEnvironment env, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>O'quvchi qidiruvida telefon bo'yicha nechta moslik yig'ilgach o'qish to'xtaydi
    /// (ro'yxat baribir 20 ta bilan cheklangan — bu IN(...) ro'yxatining yuqori chegarasi).</summary>
    private const int PhoneScanLimit = 40;

    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    // =============================================================================================
    //  OMBOR — kitoblar CRUD
    // =============================================================================================

    /// <summary>Barcha kitoblar (qoldiq + sotuv statistikasi bilan). <paramref name="activeOnly"/>
    /// berilsa faqat sotuvdagilar.</summary>
    [HttpGet]
    public async Task<ActionResult<List<BookDto>>> GetAll([FromQuery] bool activeOnly = false)
    {
        var query = db.Books.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(b => b.IsActive);
        var books = await query.OrderBy(b => b.Title).ToListAsync();
        return await ToDtosAsync(books);
    }

    /// <summary>Kitoblar ro'yxatiga sotuv/kutilayotgan statistikani biriktiradi (N+1 bo'lmasin —
    /// buyurtmalar BIR marta guruhlab o'qiladi). Sotuv sonlari SOF — qaytarilgan kitoblar
    /// ayirilgan (aks holda "sotilgan" ustuni ombordagi haqiqat bilan mos kelmasdi).</summary>
    private async Task<List<BookDto>> ToDtosAsync(List<Book> books)
    {
        var ids = books.Select(b => b.Id).ToList();
        var stats = await db.BookOrders.AsNoTracking()
            .Where(o => ids.Contains(o.BookId))
            .GroupBy(o => new { o.BookId, o.Status })
            .Select(g => new
            {
                g.Key.BookId,
                g.Key.Status,
                Qty = g.Sum(x => x.Qty - x.ReturnedQty),
                Total = g.Sum(x => x.Total - x.UnitPrice * x.ReturnedQty),
            })
            .ToListAsync();

        return books.Select(b =>
        {
            var sold = stats.FirstOrDefault(s => s.BookId == b.Id && s.Status == BookSalesService.StatusApproved);
            var pending = stats.FirstOrDefault(s => s.BookId == b.Id && s.Status == BookSalesService.StatusPending);
            return new BookDto(
                b.Id, b.Title, b.Author, b.Description, b.CoverUrl, b.Price, b.Stock, b.IsActive,
                sold?.Qty ?? 0, sold?.Total ?? 0m, pending?.Qty ?? 0,
                b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), b.CreatedBy);
        }).ToList();
    }

    /// <summary>Yangi kitob. <c>InitialStock</c> berilsa boshlang'ich qoldiq "initial" kirim sifatida
    /// tarixga ham yoziladi (kitob qachon va qancha miqdorda kirim qilingani hisoboti uchun).</summary>
    [HttpPost]
    public async Task<ActionResult<BookDto>> Create(BookPayload payload)
    {
        var title = (payload.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Kitob nomi kerak" });
        if (payload.Price < 0) return BadRequest(new { message = "Narx manfiy bo'lmaydi" });
        if (payload.InitialStock < 0) return BadRequest(new { message = "Boshlang'ich qoldiq manfiy bo'lmaydi" });

        var book = new Book
        {
            Title = title,
            Author = (payload.Author ?? "").Trim(),
            Description = (payload.Description ?? "").Trim(),
            CoverUrl = (payload.CoverUrl ?? "").Trim(),
            Price = payload.Price,
            Stock = 0,
            IsActive = payload.IsActive,
            CreatedBy = Actor,
        };
        db.Books.Add(book);

        if (payload.InitialStock > 0)
            db.BookStockMoves.Add(BookSalesService.Move(
                book, payload.InitialStock, BookSalesService.ReasonInitial,
                "Kitob qo'shildi (boshlang'ich qoldiq)", Actor));

        audit.Record("Book", book.Id, "create",
            $"Kitob qo'shildi: {book.Title} — {AuditService.Money(book.Price)} so'm" +
            (payload.InitialStock > 0 ? $", boshlang'ich qoldiq {payload.InitialStock} dona" : ""));
        await db.SaveChangesAsync();
        return (await ToDtosAsync(new List<Book> { book }))[0];
    }

    /// <summary>Kitobni tahrirlash (nom/narx/muqova/tavsif/holat). QOLDIQ bu yerda o'zgarmaydi —
    /// u faqat <c>POST /{id}/stock</c> orqali (tarix yoziladigan) yo'l bilan o'zgaradi.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<BookDto>> Update(string id, BookPayload payload)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id);
        if (book is null) return NotFound();
        var title = (payload.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Kitob nomi kerak" });
        if (payload.Price < 0) return BadRequest(new { message = "Narx manfiy bo'lmaydi" });

        var newCover = (payload.CoverUrl ?? "").Trim();
        // Muqova o'zgarsa Telegram keshini bo'shatamiz — aks holda bot eski rasmni yuboraverardi.
        if (newCover != book.CoverUrl) book.CoverFileId = string.Empty;

        book.Title = title;
        book.Author = (payload.Author ?? "").Trim();
        book.Description = (payload.Description ?? "").Trim();
        book.CoverUrl = newCover;
        var oldPrice = book.Price;
        book.Price = payload.Price;
        book.IsActive = payload.IsActive;
        audit.Record("Book", book.Id, "update",
            $"Kitob tahrirlandi: {book.Title}" +
            (oldPrice != payload.Price
                ? $" — narx {AuditService.Money(oldPrice)} → {AuditService.Money(payload.Price)} so'm"
                : ""));
        await db.SaveChangesAsync();
        return (await ToDtosAsync(new List<Book> { book }))[0];
    }

    /// <summary>Kitobni o'chirish. Buyurtma tarixi bor kitob O'CHIRILMAYDI (hisobot buzilmasin) —
    /// uni "sotuvdan olish" (<c>IsActive=false</c>) kerak. Ombor harakatlari tarixi o'chadi.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id);
        if (book is null) return NotFound();

        if (await db.BookOrders.AnyAsync(o => o.BookId == id))
            return BadRequest(new
            {
                message = "Bu kitob bo'yicha buyurtmalar bor — o'chirib bo'lmaydi. "
                          + "Uni sotuvdan olish uchun \"Sotuvda\" belgisini o'chiring.",
            });

        await db.BookStockMoves.Where(m => m.BookId == id).ExecuteDeleteAsync();
        audit.Record("Book", book.Id, "delete", $"Kitob o'chirildi: {book.Title}");
        db.Books.Remove(book);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Omborga KIRIM (yoki qo'lda korreksiya). <c>Qty &gt; 0</c> — kirim ("qoldiq to'ldirildi"),
    /// <c>Qty &lt; 0</c> — ayirish (yo'qolgan/buzilgan kitob). Har amal tarixga yoziladi.
    /// </summary>
    [HttpPost("{id}/stock")]
    public async Task<ActionResult<BookDto>> AddStock(string id, BookStockPayload payload)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id);
        if (book is null) return NotFound();
        if (payload.Qty == 0) return BadRequest(new { message = "Miqdor 0 bo'lmasligi kerak" });
        if (book.Stock + payload.Qty < 0)
            return BadRequest(new { message = $"Qoldiq manfiy bo'lib qoladi (joriy qoldiq {book.Stock})" });

        var reason = payload.Qty > 0 ? BookSalesService.ReasonRestock : BookSalesService.ReasonCorrection;
        db.BookStockMoves.Add(BookSalesService.Move(book, payload.Qty, reason, payload.Note ?? "", Actor));
        audit.Record("Book", book.Id, "update",
            $"Ombor: {book.Title} — {(payload.Qty > 0 ? "+" : "")}{payload.Qty} dona " +
            $"(qoldiq {book.Stock} → {book.Stock + payload.Qty})" +
            (string.IsNullOrWhiteSpace(payload.Note) ? "" : $", izoh: {payload.Note!.Trim()}"));
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // `Book.Stock` — konkurentlik tokeni: kirim yozilayotgan payt boshqa amal (sotuvni
            // tasdiqlash) qoldiqni o'zgartirgan bo'lsa, bu UPDATE 0 qator yangilaydi va bazaga
            // hech narsa tushmaydi. 500 o'rniga tushunarli xabar qaytaramiz (ApproveAsync bilan
            // bir xil uslub) — foydalanuvchi qaytadan bosadi va yangi qoldiq ustiga yoziladi.
            return BadRequest(new { message = "Qoldiq shu payt boshqa amalda o'zgardi — qaytadan urinib ko'ring." });
        }
        return (await ToDtosAsync(new List<Book> { book }))[0];
    }

    /// <summary>Ombor harakatlari tarixi (kirim/sotuv/korreksiya). <paramref name="onlyIn"/> = true —
    /// faqat KIRIM (kitoblar qachon va qancha miqdorda qo'shilgani).</summary>
    [HttpGet("stock-moves")]
    public async Task<ActionResult<List<BookStockMoveDto>>> StockMoves(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? bookId,
        [FromQuery] bool onlyIn = false)
    {
        var moves = await FilteredMovesAsync(from, to, bookId, onlyIn);
        return moves.Select(ToMoveDto).ToList();
    }

    private async Task<List<BookStockMove>> FilteredMovesAsync(
        string? from, string? to, string? bookId, bool onlyIn)
    {
        var query = db.BookStockMoves.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(bookId)) query = query.Where(m => m.BookId == bookId);
        // "Faqat kirim" — kitob QAYERDAN kelgani (nashriyot/boshlang'ich qoldiq). Sotuvdan
        // QAYTARILGAN kitob ham qoldiqni oshiradi, lekin u kirim EMAS — bu ro'yxatga qo'shilsa
        // "qancha kitob olib kelindi" hisoboti shishardi (to'liq tarixda u baribir ko'rinadi).
        if (onlyIn) query = query.Where(m => m.Qty > 0 && m.Reason != BookSalesService.ReasonReturn);
        var (fromDt, toDt) = DateRange(from, to);
        if (fromDt is not null) query = query.Where(m => m.CreatedAt >= fromDt);
        if (toDt is not null) query = query.Where(m => m.CreatedAt < toDt);
        return await query.OrderByDescending(m => m.CreatedAt).Take(2000).ToListAsync();
    }

    private static BookStockMoveDto ToMoveDto(BookStockMove m) => new(
        m.Id, m.BookId, m.BookTitle, m.Qty, m.Reason, m.OrderId, m.Note, m.StockAfter,
        m.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), m.CreatedBy);

    // =============================================================================================
    //  BUYURTMALAR
    // =============================================================================================

    /// <summary>Buyurtmalar ro'yxati. Filtrlar: holat, davr, kitob, to'lov turi, qidiruv (ism/telefon/
    /// buyurtma raqami). Eng yangisi yuqorida.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<List<BookOrderDto>>> Orders(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? bookId, [FromQuery] string? method, [FromQuery] string? q)
    {
        var orders = await FilteredOrdersAsync(status, from, to, bookId, method, q);
        return await ToOrderDtosAsync(orders);
    }

    /// <summary>
    /// KARTA TO'LOVLARI — kartaga o'tkazma bilan to'langan buyurtmalar (mijoz botdan yuborgan
    /// chek rasmi bilan) va shu karta bo'yicha jamlanma.
    ///
    /// <para>Jami summalar <b>butun topilma</b> bo'yicha SQL tomonda hisoblanadi — qaytariladigan
    /// ro'yxat ko'rsatish uchun cheklangan (<see cref="FilteredOrdersAsync"/> 1000 ta), shu sabab
    /// ro'yxatdan qo'shib chiqarilsa jami noto'g'ri bo'lardi.</para>
    ///
    /// <para>Pul MOLIYAGA (FinanceTransaction) YOZILMAYDI — kitob sotuvi ataylab o'quv to'lovi
    /// hisobotlaridan ajratilgan (.claude/rules/books.md §7). "Kartaga hisoblangan" = tasdiqlangan
    /// karta buyurtmalari yig'indisi.</para>
    /// </summary>
    [HttpGet("card-payments")]
    public async Task<ActionResult<BookCardPaymentsDto>> CardPayments(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? bookId, [FromQuery] string? q)
    {
        // Jamlanma — holat filtridan QAT'I NAZAR (foydalanuvchi "kutilmoqda"ni ko'rayotganda ham
        // kartaga jami qancha tushgani ko'rinib tursin).
        var summaryQuery = CardOrdersQuery(from, to, bookId, q);
        // Summalar SOF: qaytarilgan kitobning puli mijozga qaytgan, ya'ni kartada qolmagan.
        var totals = await summaryQuery
            .GroupBy(o => o.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Sum = g.Sum(x => x.Total - x.UnitPrice * x.ReturnedQty),
            })
            .ToListAsync();
        var byStatus = totals.ToDictionary(x => x.Status);
        (int Count, decimal Sum) Of(string s) =>
            byStatus.TryGetValue(s, out var v) ? (v.Count, v.Sum) : (0, 0m);

        var approved = Of(BookSalesService.StatusApproved);
        var pending = Of(BookSalesService.StatusPending);
        var rejected = Of(BookSalesService.StatusRejected);

        var orders = await FilteredOrdersAsync(status, from, to, bookId, BookSalesService.PayCard, q);
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();

        return new BookCardPaymentsDto(
            meta?.BookCardNumber ?? "", meta?.BookCardHolder ?? "",
            approved.Count, approved.Sum,
            pending.Count, pending.Sum,
            rejected.Count,
            await ToOrderDtosAsync(orders));
    }

    /// <summary>Karta to'lovlari jamlanmasi uchun so'rov (holat filtrisiz).</summary>
    private IQueryable<BookOrder> CardOrdersQuery(string? from, string? to, string? bookId, string? q)
    {
        var query = db.BookOrders.AsNoTracking()
            .Where(o => o.PaymentMethod == BookSalesService.PayCard);
        if (!string.IsNullOrWhiteSpace(bookId)) query = query.Where(o => o.BookId == bookId);
        var (fromDt, toDt) = DateRange(from, to);
        if (fromDt is not null) query = query.Where(o => o.CreatedAt >= fromDt);
        if (toDt is not null) query = query.Where(o => o.CreatedAt < toDt);
        return ApplySearch(query, q);
    }

    /// <summary>
    /// Buyurtma QIDIRUVI (ism / telefon / kitob nomi / buyurtma raqami) — barcha ro'yxatlar
    /// (buyurtmalar, karta to'lovlari, nasiya) uchun YAGONA joyda, aks holda bir xil so'rov
    /// bo'limlarda har xil natija berardi.
    /// </summary>
    private static IQueryable<BookOrder> ApplySearch(IQueryable<BookOrder> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return query;
        var term = q.Trim();
        var digits = PhoneUtil.DigitsOnly(term);
        return digits.Length >= 4
            ? query.Where(o => o.Phone.Contains(digits) || o.CustomerName.Contains(term))
            : query.Where(o => o.CustomerName.Contains(term) || o.BookTitle.Contains(term)
                               || o.Number.ToString().Contains(term));
    }

    /// <summary>Xotiradagi ro'yxat uchun AYNI qidiruv (nasiya ro'yxati to'liq yuklab olinadi —
    /// jamlanma qidiruvdan oldin hisoblanishi kerak).</summary>
    private static List<BookOrder> SearchInMemory(List<BookOrder> orders, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return orders;
        var term = q.Trim();
        var digits = PhoneUtil.DigitsOnly(term);
        return digits.Length >= 4
            ? orders.Where(o => o.Phone.Contains(digits)
                                || o.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList()
            : orders.Where(o => o.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || o.BookTitle.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || o.Number.ToString().Contains(term)).ToList();
    }

    /// <summary>Ro'yxat filtridagi maxsus qiymat: HOLAT emas, "qaytarilgan sotuvlar" kesimi
    /// (qaytarish buyurtma holatini o'zgartirmaydi — qarang: <see cref="BookSalesService.ReturnAsync"/>).</summary>
    private const string StatusFilterReturned = "returned";

    private async Task<List<BookOrder>> FilteredOrdersAsync(
        string? status, string? from, string? to, string? bookId, string? method, string? q)
    {
        var query = db.BookOrders.AsNoTracking().AsQueryable();
        if (status == StatusFilterReturned) query = query.Where(o => o.ReturnedQty > 0);
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(bookId)) query = query.Where(o => o.BookId == bookId);
        if (!string.IsNullOrWhiteSpace(method)) query = query.Where(o => o.PaymentMethod == method);
        var (fromDt, toDt) = DateRange(from, to);
        if (fromDt is not null) query = query.Where(o => o.CreatedAt >= fromDt);
        if (toDt is not null) query = query.Where(o => o.CreatedAt < toDt);
        return await ApplySearch(query, q).OrderByDescending(o => o.CreatedAt).Take(1000).ToListAsync();
    }

    /// <summary>Buyurtmalarga o'quvchi ismi va kitobning joriy qoldig'ini biriktiradi (bitta so'rovda).</summary>
    private async Task<List<BookOrderDto>> ToOrderDtosAsync(List<BookOrder> orders)
    {
        var studentIds = orders.Where(o => !string.IsNullOrEmpty(o.StudentId))
            .Select(o => o.StudentId!).Distinct().ToList();
        var names = studentIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.FullName);

        var bookIds = orders.Select(o => o.BookId).Distinct().ToList();
        var stocks = bookIds.Count == 0
            ? new Dictionary<string, int>()
            : await db.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Stock);

        var today = AppClock.Now;
        return orders.Select(o => new BookOrderDto(
            o.Id, o.Number, o.CustomerName, o.Phone, o.StudentId,
            o.StudentId is null ? null : names.GetValueOrDefault(o.StudentId),
            o.BookId, o.BookTitle, o.UnitPrice, o.Qty, o.Total, o.PaymentMethod, o.ReceiptUrl,
            o.Status, o.RejectReason, o.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            o.DecidedAt?.ToString("yyyy-MM-ddTHH:mm:ss"), o.DecidedBy,
            stocks.GetValueOrDefault(o.BookId, 0),
            o.Source, o.CardLast4, o.PaidTime,
            BookSalesService.IsPaid(o),
            o.DueDate?.ToString("yyyy-MM-dd"),
            BookSalesService.IsOverdue(o, today),
            o.PaidAt?.ToString("yyyy-MM-ddTHH:mm:ss"), o.PaidBy, o.SettledMethod,
            o.ReturnedQty, o.ReturnedAt?.ToString("yyyy-MM-ddTHH:mm:ss"), o.ReturnedBy,
            o.ReturnReason, o.RefundedAmount, BookSalesService.NetTotal(o))).ToList();
    }

    /// <summary>Tab belgilari uchun sanoqlar: kutilayotgan buyurtmalar va to'lanmagan nasiyalar
    /// (shundan muddati o'tganlari). Bitta yengil so'rov — har tab uchun alohida chaqiruv bo'lmasin.</summary>
    [HttpGet("orders/pending-count")]
    public async Task<ActionResult<object>> PendingCount()
    {
        // TO'LIQ QAYTARILGAN nasiyada qarz qolmaydi — u navbat belgisida sanalmaydi
        // (aks holda "3 ta qarz" deb ko'rsatib, ro'yxatda 2 tasi chiqardi).
        var unpaid = await db.BookOrders.AsNoTracking()
            .Where(o => o.PaymentMethod == BookSalesService.PayCredit
                        && o.Status == BookSalesService.StatusApproved && o.PaidAt == null
                        && o.ReturnedQty < o.Qty)
            .Select(o => o.DueDate)
            .ToListAsync();
        var today = AppClock.Now.Date;
        return Ok(new
        {
            count = await db.BookOrders.CountAsync(o => o.Status == BookSalesService.StatusPending),
            credits = unpaid.Count,
            overdue = unpaid.Count(d => d is { } due && due.Date < today),
        });
    }

    /// <summary>
    /// Buyurtmani TASDIQLASH: qoldiqdan kitob soni ayiriladi, sotuv analitikaga tushadi va mijozga
    /// botda "Buyurtmangiz tasdiqlandi!" xabari ketadi.
    /// </summary>
    [HttpPost("orders/{id}/approve")]
    public async Task<ActionResult<BookOrderDto>> Approve(string id)
    {
        var order = await db.BookOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        var error = await BookSalesService.ApproveAsync(db, order, Actor);
        if (error is not null) return BadRequest(new { message = error });

        // DIQQAT: ApproveAsync O'ZI SaveChanges qiladi — shuning uchun audit yozuvini
        // alohida saqlaymiz (aks holda u hech qachon bazaga tushmasdi).
        audit.Record("BookOrder", order.Id, "update",
            $"Buyurtma tasdiqlandi: {order.BookTitle} x {order.Qty} dona — " +
            $"{AuditService.Money(order.Total)} so'm ({order.CustomerName})", studentId: order.StudentId);
        await db.SaveChangesAsync();

        // Qo'lda sotilgan buyurtmada Telegram chat yo'q (ChatId=0) — xabar yuborilmaydi.
        if (order.ChatId != 0)
            await telegram.SendMessageAsync(order.ChatId, BookSalesService.CustomerApprovedText(order));
        return (await ToOrderDtosAsync(new List<BookOrder> { order }))[0];
    }

    /// <summary>Buyurtmani RAD etish (sabab bilan). Qoldiq tegilmaydi; mijozga sabab yuboriladi.</summary>
    [HttpPost("orders/{id}/reject")]
    public async Task<ActionResult<BookOrderDto>> Reject(string id, BookRejectPayload payload)
    {
        var order = await db.BookOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        if (string.IsNullOrWhiteSpace(payload.Reason))
            return BadRequest(new { message = "Rad etish sababini kiriting" });

        var error = await BookSalesService.RejectAsync(db, order, payload.Reason, Actor);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record("BookOrder", order.Id, "update",
            $"Buyurtma rad etildi: {order.BookTitle} x {order.Qty} dona ({order.CustomerName}) " +
            $"— sabab: {payload.Reason.Trim()}", studentId: order.StudentId);
        await db.SaveChangesAsync();

        if (order.ChatId != 0)
            await telegram.SendMessageAsync(order.ChatId, BookSalesService.CustomerRejectedText(order));
        return (await ToOrderDtosAsync(new List<BookOrder> { order }))[0];
    }

    /// <summary>
    /// SOTILGAN KITOBNI QAYTARISH (vozvrat) — naqd, karta va nasiya sotuvlarining hammasi uchun.
    /// Ikki narsa birdan to'g'rilanadi: <b>ombor</b> (dona qoldiqqa qaytadi) va <b>pul</b>
    /// (sotuv summasidan qaytarilgan qismi ayiriladi, ya'ni tushum/qarz sof bo'lib qoladi).
    ///
    /// <para>Qisman qaytarish mumkin: 3 dona sotilib, 1 tasi qaytarilsa qolgan 2 tasi sotuvda
    /// qoladi. To'lanmagan NASIYADA pul chiqmaydi — shunchaki qarz kamayadi.</para>
    /// </summary>
    [HttpPost("orders/{id}/return")]
    public async Task<ActionResult<BookOrderDto>> Return(string id, BookReturnPayload payload)
    {
        var order = await db.BookOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // Pul qaytdimi yoki qarz kamaydimi — SERVIS o'zgartirishidan OLDIN aniqlanadi
        // (audit yozuvi va mijozga ketadigan xabar aynan shu farqni aytishi kerak).
        var refund = BookSalesService.RefundFor(order, payload.Qty);

        var error = await BookSalesService.ReturnAsync(
            db, order, payload.Qty, payload.Reason ?? "", Actor);
        if (error is not null) return BadRequest(new { message = error });

        // DIQQAT: ReturnAsync O'ZI SaveChanges qiladi — audit yozuvi alohida saqlanadi
        // (aks holda u bazaga umuman tushmasdi).
        audit.Record("BookOrder", order.Id, "update",
            $"Kitob qaytarildi: {order.BookTitle} x {payload.Qty} dona (buyurtma #{order.Number}" +
            (order.CustomerName.Length > 0 ? $", {order.CustomerName}" : "") + ") — " +
            (refund > 0
                ? $"{AuditService.Money(refund)} so'm qaytarildi"
                : BookSalesService.IsCredit(order)
                    ? $"qarz {AuditService.Money(order.UnitPrice * payload.Qty)} so'mga kamaydi"
                    : "pul qaytarilmadi (olinmagan edi)") +
            (string.IsNullOrWhiteSpace(payload.Reason) ? "" : $", sabab: {payload.Reason!.Trim()}"),
            studentId: order.StudentId);
        await db.SaveChangesAsync();

        // Qo'lda sotilgan buyurtmada Telegram chat yo'q (ChatId=0) — xabar yuborilmaydi.
        if (order.ChatId != 0)
            await telegram.SendMessageAsync(
                order.ChatId, BookSalesService.CustomerReturnedText(order, payload.Qty, refund));
        return (await ToOrderDtosAsync(new List<BookOrder> { order }))[0];
    }

    // =============================================================================================
    //  QO'LDA SOTUV — markazda, joyida (bot orqali emas)
    // =============================================================================================

    /// <summary>
    /// Qo'lda sotuv oynasi uchun O'QUVCHI QIDIRUVI (F.I.Sh yoki telefon, kamida 2 belgi).
    /// <c>KassaController.SearchStudents</c> bilan bir xil mantiq, lekin <c>books</c> ruxsati ostida
    /// va yengilroq (balans hisoblanmaydi — kitob sotuvi o'quvchi balansiga tegmaydi).
    /// </summary>
    [HttpGet("students")]
    public async Task<ActionResult<List<BookStudentDto>>> SearchStudents([FromQuery] string q)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return new List<BookStudentDto>();
        var name = term.ToLower();

        // TELEFON: bazada "+998-90-123-45-67" ko'rinishida saqlanadi, shuning uchun SQL'da
        // to'g'ridan-to'g'ri solishtirib bo'lmaydi — yengil proyeksiya olib xotirada moslashtiramiz.
        //
        // ⚠️ MAMLAKAT KODI: ilgari xom raqamlar solishtirilardi va HAMMA raqam "998" bilan
        // boshlangani uchun "9989" kabi so'rov deyarli har bir o'quvchiga mos kelib, kassirga
        // 80 ta begona odam chiqarardi. Endi ikkala tomon ham MAHALLIY qismga keltiriladi
        // (PhoneUtil.Key = oxirgi 9 raqam) — qarang: BookSalesService.PhoneMatches.
        // Natija ham cheklangan: mos kelganlar `PhoneScanLimit` ga yetganda o'qish TO'XTAYDI,
        // ya'ni butun jadval ro'yxatga yig'ilmaydi va IN(...) ro'yxati shishmaydi.
        //
        // FARQ: `KassaController.SearchStudents` da hali ESKI mantiq (xom raqamlar + Take(80))
        // turibdi — u boshqa bo'lim (balans bilan) va bu ish doirasiga kirmagani uchun ataylab
        // tegilmadi. O'sha yerda ham xuddi shu kamchilik bor.
        var key = PhoneUtil.Key(term);
        var phoneIds = new List<string>();
        if (key.Length >= BookSalesService.MinPhoneDigits)
        {
            await foreach (var r in db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
                .AsAsyncEnumerable())
            {
                if (BookSalesService.PhoneMatches(r.Phone, key)
                    || BookSalesService.PhoneMatches(r.ParentPhone, key)
                    || BookSalesService.PhoneMatches(r.FatherPhone, key)
                    || BookSalesService.PhoneMatches(r.MotherPhone, key))
                    phoneIds.Add(r.Id);
                if (phoneIds.Count >= PhoneScanLimit) break;
            }
        }

        return await db.Students.AsNoTracking()
            .Where(s => s.FullName.ToLower().Contains(name) || phoneIds.Contains(s.Id))
            .OrderBy(s => s.IsArchived).ThenBy(s => s.FullName)
            .Take(20)
            .Select(s => new BookStudentDto(
                s.Id, s.FullName, s.Phone, s.ParentPhone, s.ClassName, s.IsArchived))
            .ToListAsync();
    }

    /// <summary>
    /// MARKAZDA QO'LDA SOTUV: kitob → soni → o'quvchi → naqd/karta. Buyurtma darhol
    /// <b>tasdiqlangan</b> holatda yaratiladi, ya'ni qoldiq shu zahoti ayiriladi va sotuv
    /// analitikaga tushadi. Qoldiq va tarix baribir <see cref="BookSalesService.ApproveAsync"/>
    /// orqali o'zgaradi — ombor mantig'i bitta joyda qoladi (botdagi oqim bilan bir xil).
    /// </summary>
    [HttpPost("orders/manual")]
    public async Task<ActionResult<BookOrderDto>> ManualSale(BookManualSalePayload payload)
    {
        if (payload.Qty <= 0) return BadRequest(new { message = "Sonini kiriting (kamida 1 dona)" });
        if (payload.Qty > 500) return BadRequest(new { message = "Soni juda katta (maksimal 500 dona)" });

        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == payload.BookId);
        // Sotuvdan olingan kitob sotilmaydi. Frontend uni ro'yxatda ko'rsatmaydi, lekin bu
        // endpoint to'g'ridan-to'g'ri chaqirilsa ilgari hech qanday to'siq yo'q edi.
        var bookError = BookSalesService.ManualSaleBookError(book);
        if (book is null || bookError is not null) return BadRequest(new { message = bookError });

        // O'QUVCHI IXTIYORIY. Ilgari majburiy edi va kassir markazda o'qimaydigan odamga
        // (ota-ona, qo'shni maktab o'quvchisi, o'tkinchi) kitob sota olmasdi — buning uchun soxta
        // o'quvchi yaratishga to'g'ri kelardi. `BookOrder.StudentId` allaqachon nullable: bot
        // oqimida mehmon buyurtmasi shunday yoziladi.
        Student? student = null;
        var studentId = (payload.StudentId ?? "").Trim();
        if (studentId.Length > 0)
        {
            student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
            // Id berilgan, lekin topilmadi — bu xato (jim o'tkazib yuborilsa sotuv noto'g'ri
            // odamga teglanmay qolardi va kassir buni sezmasdi).
            if (student is null) return BadRequest(new { message = "O'quvchi topilmadi" });
        }

        // Ism: o'quvchi tanlansa — uning ismi (asl manba), aks holda erkin yozilgan ism.
        // Bo'sh qolishi ham MUMKIN — ro'yxat va cheklarda "Noma'lum" bo'lib ko'rinadi.
        var customerName = student?.FullName ?? (payload.CustomerName ?? "").Trim();
        if (customerName.Length > 120) customerName = customerName[..120];

        var method = payload.PaymentMethod switch
        {
            BookSalesService.PayCard => BookSalesService.PayCard,
            BookSalesService.PayCredit => BookSalesService.PayCredit,
            _ => BookSalesService.PayCash,
        };

        // NASIYADA xaridor MAJBURIY — qarz kimda ekani yozilmasa nasiyaning ma'nosi qolmaydi.
        if (BookSalesService.CreditCustomerError(method, student?.Id, customerName) is { } customerError)
            return BadRequest(new { message = customerError });

        // Karta to'lovida oxirgi 4 raqam va to'lov vaqti — moliya bo'limi bilan BIR XIL
        // normalizatsiya (PaymentFields): to'liq karta raqami hech qachon saqlanmaydi.
        string? last4 = null;
        string? paidTime = null;
        if (method == BookSalesService.PayCard)
        {
            if (!PaymentFields.TryNormalizeCardLast4(payload.CardLast4, out last4))
                return BadRequest(new { message = "Karta raqamining oxirgi 4 raqamini to'liq kiriting" });
            if (last4 is null)
                return BadRequest(new { message = "Karta to'lovida oxirgi 4 raqam majburiy" });
            if (!PaymentFields.TryNormalizeTime(payload.PaidTime, out paidTime))
                return BadRequest(new { message = "To'lov vaqti noto'g'ri (HH:mm)" });
            if (paidTime is null)
                return BadRequest(new { message = "Karta to'lovida to'lov vaqti majburiy" });
        }

        // NASIYA: va'da qilingan sana (ixtiyoriy). Noto'g'ri matn jimgina yutilmaydi — kassir
        // sanani yozdim deb o'ylab, ro'yxatda "muddatsiz" ko'rib qolmasin.
        DateTime? dueDate = null;
        if (method == BookSalesService.PayCredit && !string.IsNullOrWhiteSpace(payload.DueDate))
        {
            if (!DateTime.TryParse(payload.DueDate, out var due))
                return BadRequest(new { message = "To'lov muddati noto'g'ri (YYYY-MM-DD)" });
            dueDate = due.Date;
        }

        var order = new BookOrder
        {
            Number = await BookSalesService.NextOrderNumberAsync(db),
            ChatId = 0,                       // Telegram chat yo'q — xabar yuborilmaydi
            Source = BookSalesService.SourceManual,
            CustomerName = customerName,
            // O'quvchi tanlansa raqam undan olinadi (asl manba); aks holda kassir kiritgani
            // (nasiyada qarzdorni topish uchun kerak, boshqa turlarda ixtiyoriy).
            Phone = student is null
                ? PhoneUtil.Normalize((payload.CustomerPhone ?? "").Trim())
                : string.IsNullOrWhiteSpace(student.Phone) ? student.ParentPhone : student.Phone,
            StudentId = student?.Id,
            DueDate = dueDate,
            BookId = book.Id,
            BookTitle = book.Title,           // SNAPSHOT — keyin narx/nom o'zgarsa hisobot buzilmasin
            UnitPrice = book.Price,
            Qty = payload.Qty,
            Total = book.Price * payload.Qty,
            PaymentMethod = method,
            CardLast4 = last4,
            PaidTime = paidTime,
            Status = BookSalesService.StatusPending,   // ApproveAsync faqat pending'ni qabul qiladi
        };
        db.BookOrders.Add(order);

        // Qoldiq yetmasa — bu yerda 400 qaytadi va SaveChanges CHAQIRILMAYDI, ya'ni buyurtma ham
        // yozilmaydi (yarim holatda "pending" qatori qolib ketmasin).
        var error = await BookSalesService.ApproveAsync(db, order, Actor);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record("BookOrder", order.Id, "create",
            $"Kitob qo'lda sotildi{(method == BookSalesService.PayCredit ? " (NASIYAGA)" : "")}: " +
            $"{order.BookTitle} x {order.Qty} dona — {AuditService.Money(order.Total)} so'm" +
            // Ism bo'sh bo'lishi mumkin (chetdan xaridor) — bo'sh qavs qolib ketmasin.
            (order.CustomerName.Length > 0 ? $" ({order.CustomerName})" : "") +
            (dueDate is { } d ? $", to'lov muddati {d:yyyy-MM-dd}" : ""),
            studentId: order.StudentId);
        await db.SaveChangesAsync();

        return (await ToOrderDtosAsync(new List<BookOrder> { order }))[0];
    }

    // =============================================================================================
    //  NASIYA — kitob berildi, pul keyin olinadi
    // =============================================================================================

    /// <summary>
    /// NASIYA bo'limi: to'lanmagan qarzlar (yoki tanlangan davrda to'langanlari), xaridor
    /// kesimidagi jamlanma va joriy qarz raqamlari.
    ///
    /// <para><b>Davr (from/to) faqat "to'langan" ro'yxatiga va "yig'ilgan pul" raqamiga tegishli</b>
    /// (to'lov sanasi bo'yicha). TO'LANMAGAN qarz esa har doim TO'LIQ ko'rsatiladi — "kimda qarz
    /// bor" savoli sanaga bog'liq emas va davr filtri bilan qarzning bir qismini yashirib qo'yish
    /// operatorni chalg'itardi.</para>
    /// </summary>
    [HttpGet("credits")]
    public async Task<ActionResult<BookCreditsDto>> Credits(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? q)
    {
        var today = AppClock.Now;
        var credits = db.BookOrders.AsNoTracking()
            .Where(o => o.PaymentMethod == BookSalesService.PayCredit
                        && o.Status == BookSalesService.StatusApproved);

        // JORIY QARZ — filtrlardan QAT'I NAZAR (bo'limning asosiy raqami har doim ko'rinib tursin).
        // TO'LIQ QAYTARILGANLAR CHIQARIB TASHLANADI: kitob omborga qaytgan, olinadigan pul ham
        // qolmagan — bunday yozuv qarzdorlar ro'yxatida turishi noto'g'ri bo'lardi.
        var unpaid = await credits.Where(o => o.PaidAt == null && o.ReturnedQty < o.Qty)
            .OrderBy(o => o.DueDate == null).ThenBy(o => o.DueDate).ThenBy(o => o.CreatedAt)
            .ToListAsync();
        var overdue = unpaid.Where(o => BookSalesService.IsOverdue(o, today)).ToList();

        // Davr ichida nasiyadan YIG'ILGAN pul (to'lov sanasi bo'yicha).
        var (fromDt, toDt) = DateRange(from, to);
        var paidQuery = ApplySearch(credits.Where(o => o.PaidAt != null), q);
        if (fromDt is not null) paidQuery = paidQuery.Where(o => o.PaidAt >= fromDt);
        if (toDt is not null) paidQuery = paidQuery.Where(o => o.PaidAt < toDt);
        // SOF summa — qaytarilgan kitobning puli mijozga qaytarilgan (yoki umuman olinmagan).
        var collected = await paidQuery.SumAsync(o => (decimal?)(o.Total - o.UnitPrice * o.ReturnedQty)) ?? 0m;
        var collectedCount = await paidQuery.CountAsync();

        // QARZDORLAR — to'lanmaganlarning TO'LIQ ro'yxatidan (qidiruv bunga ta'sir qilmaydi:
        // "jami kimda qancha qarz bor" surati qidiruv bilan o'zgarmasligi kerak).
        var debtors = unpaid
            .GroupBy(DebtorKey)
            .Select(g => new BookDebtorDto(
                g.Key,
                g.Select(x => x.StudentId).FirstOrDefault(id => !string.IsNullOrEmpty(id)),
                g.Select(x => x.CustomerName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                g.Select(x => x.Phone).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "",
                g.Count(),
                // Qarz SOF: qisman qaytarilgan nasiyada faqat mijozda qolgan kitoblar puli.
                g.Sum(BookSalesService.NetTotal),
                g.Min(x => x.DecidedAt ?? x.CreatedAt).ToString("yyyy-MM-dd"),
                g.Any(x => BookSalesService.IsOverdue(x, today))))
            .OrderByDescending(d => d.HasOverdue).ThenByDescending(d => d.Total)
            .ToList();

        var list = status == "paid"
            ? await paidQuery.OrderByDescending(o => o.PaidAt).Take(1000).ToListAsync()
            : SearchInMemory(unpaid, q);

        return new BookCreditsDto(
            unpaid.Sum(BookSalesService.NetTotal), unpaid.Count,
            overdue.Sum(BookSalesService.NetTotal), overdue.Count,
            collected, collectedCount,
            debtors,
            await ToOrderDtosAsync(list));
    }

    /// <summary>Qarzdorni GURUHLASH kaliti: o'quvchi bo'lsa uning id'si (ismi o'zgarsa ham qarz
    /// bitta odamda qoladi), aks holda ism + mahalliy telefon raqami.</summary>
    private static string DebtorKey(BookOrder o) =>
        string.IsNullOrEmpty(o.StudentId)
            ? $"n:{o.CustomerName.Trim().ToLowerInvariant()}|{PhoneUtil.Key(o.Phone)}"
            : $"s:{o.StudentId}";

    /// <summary>
    /// NASIYA TO'LOVI QABUL QILINDI ("pulini oldim → Tasdiqlash"): summa shu paytdan boshlab
    /// tushumga (to'lovlarga) qo'shiladi. <b>Ombor tegilmaydi</b> — kitob sotuv paytida berilgan.
    /// </summary>
    [HttpPost("orders/{id}/pay")]
    public async Task<ActionResult<BookOrderDto>> PayCredit(string id, BookCreditPayPayload payload)
    {
        var order = await db.BookOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        var method = (payload.Method ?? "").Trim();
        // Karta bo'lsa oxirgi 4 raqam — moliya bo'limi bilan BIR XIL normalizatsiya
        // (to'liq karta raqami hech qachon saqlanmaydi).
        string? last4 = null;
        if (method == BookSalesService.PayCard)
        {
            if (!PaymentFields.TryNormalizeCardLast4(payload.CardLast4, out last4))
                return BadRequest(new { message = "Karta raqamining oxirgi 4 raqamini to'liq kiriting" });
            if (last4 is null)
                return BadRequest(new { message = "Karta to'lovida oxirgi 4 raqam majburiy" });
        }

        var error = await BookSalesService.PayCreditAsync(db, order, method, last4, Actor);
        if (error is not null) return BadRequest(new { message = error });

        // DIQQAT: PayCreditAsync O'ZI SaveChanges qiladi — audit yozuvi alohida saqlanadi
        // (aks holda u bazaga umuman tushmasdi).
        audit.Record("BookOrder", order.Id, "update",
            $"Nasiya to'lovi qabul qilindi: {order.BookTitle} x {order.Qty} dona — " +
            $"{AuditService.Money(order.Total)} so'm ({BookSalesService.PaymentLabel(method)})" +
            (order.CustomerName.Length > 0 ? $", {order.CustomerName}" : ""),
            studentId: order.StudentId);
        await db.SaveChangesAsync();

        return (await ToOrderDtosAsync(new List<BookOrder> { order }))[0];
    }

    // =============================================================================================
    //  ANALITIKA
    // =============================================================================================

    /// <summary>Sotuvlar LENTASIDA ("qaysi kitob qachon sotildi") ko'pi bilan shuncha yozuv
    /// qaytadi — undan ko'pi ro'yxatga sig'maydi va javobni shishirardi. Kunlik/kitob kesimi
    /// aggregatlari esa TO'LIQ (chegarasiz) hisoblanadi.</summary>
    private const int MaxSalesFeed = 400;

    /// <summary>
    /// Davr bo'yicha kitoblar sotuvi analitikasi. FAQAT tasdiqlangan buyurtmalar hisoblanadi
    /// (kutilayotgan/rad etilgan pul emas). Sotuv summasi to'lov turlariga ajratiladi:
    /// naqd · karta · <b>nasiya</b>; nasiyaning qancha qismi allaqachon to'langani, joriy qarz
    /// va davr ichida nasiyadan yig'ilgan pul alohida ko'rsatiladi.
    /// </summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<BookAnalyticsDto>> Analytics([FromQuery] string? from, [FromQuery] string? to)
        => await BuildAnalyticsAsync(from, to);

    private async Task<BookAnalyticsDto> BuildAnalyticsAsync(string? from, string? to)
    {
        var (fromDt, toDt) = DateRange(from, to);
        var today = AppClock.Now;

        var ordersQuery = db.BookOrders.AsNoTracking().AsQueryable();
        if (fromDt is not null) ordersQuery = ordersQuery.Where(o => o.CreatedAt >= fromDt);
        if (toDt is not null) ordersQuery = ordersQuery.Where(o => o.CreatedAt < toDt);
        var orders = await ordersQuery.ToListAsync();

        var approved = orders.Where(o => o.Status == BookSalesService.StatusApproved).ToList();
        var books = await db.Books.AsNoTracking().ToListAsync();
        var stockById = books.ToDictionary(b => b.Id, b => b.Stock);

        // Sotuv sanasi = tasdiqlangan vaqt (qo'lda sotuvda = sotuv payti), bo'lmasa yaratilgan vaqt.
        static DateTime SoldAt(BookOrder o) => o.DecidedAt ?? o.CreatedAt;

        // ⚠️ BARCHA SOTUV RAQAMLARI SOF — qaytarilgan kitoblar ayirilgan
        // (`BookSalesService.NetQty` / `NetTotal`). Qaytarish o'zi SOTILGAN KUNGA yoziladi:
        // "shu kuni nima sotildi" savoliga qaytarilgan kitob bilan javob berish noto'g'ri bo'lardi.
        // Kassadan pul QACHON chiqqani esa alohida raqam (`RefundedInPeriod`, qaytarish sanasi bo'yicha).
        static decimal SumOf(IEnumerable<BookOrder> src, string method) =>
            src.Where(x => x.PaymentMethod == method).Sum(BookSalesService.NetTotal);

        var byDay = approved
            .GroupBy(o => SoldAt(o).ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new BookDaySalesDto(
                g.Key,
                g.Sum(BookSalesService.NetQty),
                SumOf(g, BookSalesService.PayCash),
                SumOf(g, BookSalesService.PayCard),
                SumOf(g, BookSalesService.PayCredit),
                g.Sum(BookSalesService.NetTotal),
                g.Sum(x => x.ReturnedQty),
                g.Sum(BookSalesService.ReturnedAmount)))
            .ToList();

        // HAR KUNI QAYSI KITOB SOTILDI — kun × kitob kesimi (eng yangi kun yuqorida).
        var byDayBook = approved
            .GroupBy(o => new { Date = SoldAt(o).ToString("yyyy-MM-dd"), o.BookId, o.BookTitle })
            .Select(g => new BookDayBookSalesDto(
                g.Key.Date, g.Key.BookId, g.Key.BookTitle,
                g.Sum(BookSalesService.NetQty), g.Sum(BookSalesService.NetTotal), g.Count(),
                g.Sum(x => x.ReturnedQty)))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Qty).ThenBy(x => x.BookTitle)
            .ToList();

        // SOTUVLAR LENTASI — "qaysi kitob QACHON (soati bilan) sotildi", eng yangisi tepada.
        var sales = approved
            .OrderByDescending(SoldAt)
            .Take(MaxSalesFeed)
            // Lentada XOM (sotuv paytidagi) dona/summa turadi — bu hodisalar tarixi; qaytarilgan
            // qismi alohida ustunda ko'rinadi va qatordan "Qaytarish" qilinadi.
            .Select(o => new BookSaleRowDto(
                o.Id, o.Number, SoldAt(o).ToString("yyyy-MM-ddTHH:mm:ss"),
                o.BookId, o.BookTitle, o.Qty, o.Total, o.CustomerName,
                o.PaymentMethod, BookSalesService.IsPaid(o), o.Source, o.ReturnedQty))
            .ToList();

        // NASIYA — davr ichida sotilganlari (sotuv sanasi bo'yicha).
        var creditSold = approved.Where(BookSalesService.IsCredit).ToList();

        // NASIYA — JORIY QARZ: davrga BOG'LIQ EMAS (xuddi ombor qoldig'i kabi). Filtr bilan
        // qarzning bir qismini yashirib qo'yish "hozir qancha qarz bor" savolini buzardi.
        var unpaidCredits = await db.BookOrders.AsNoTracking()
            .Where(o => o.PaymentMethod == BookSalesService.PayCredit
                        && o.Status == BookSalesService.StatusApproved && o.PaidAt == null
                        // To'liq qaytarilganda qarz qolmaydi (Nasiya bo'limi bilan bir xil qoida).
                        && o.ReturnedQty < o.Qty)
            .ToListAsync();
        var overdueCredits = unpaidCredits.Where(o => BookSalesService.IsOverdue(o, today)).ToList();

        // NASIYA — davr ichida YIG'ILGAN pul (sotuv emas, TO'LOV sanasi bo'yicha: nasiya o'tgan
        // oyda sotilib, pul shu oyda kelgan bo'lishi mumkin).
        var collectedQuery = db.BookOrders.AsNoTracking()
            .Where(o => o.PaymentMethod == BookSalesService.PayCredit && o.PaidAt != null);
        if (fromDt is not null) collectedQuery = collectedQuery.Where(o => o.PaidAt >= fromDt);
        if (toDt is not null) collectedQuery = collectedQuery.Where(o => o.PaidAt < toDt);
        var creditCollected =
            await collectedQuery.SumAsync(o => (decimal?)(o.Total - o.UnitPrice * o.ReturnedQty)) ?? 0m;
        var creditCollectedCount = await collectedQuery.CountAsync();

        // QAYTARISH — davr ICHIDA kassadan chiqqan pul (QAYTARISH sanasi bo'yicha: o'tgan oyda
        // sotilgan kitob shu oyda qaytarilishi mumkin, pul esa shu oyda chiqqan).
        var refundQuery = db.BookOrders.AsNoTracking().Where(o => o.ReturnedAt != null);
        if (fromDt is not null) refundQuery = refundQuery.Where(o => o.ReturnedAt >= fromDt);
        if (toDt is not null) refundQuery = refundQuery.Where(o => o.ReturnedAt < toDt);
        var refundedInPeriod = await refundQuery.SumAsync(o => (decimal?)o.RefundedAmount) ?? 0m;
        var refundedCount = await refundQuery.CountAsync();

        var byBook = approved
            .GroupBy(o => new { o.BookId, o.BookTitle })
            .Select(g => new BookSalesByBookDto(
                g.Key.BookId, g.Key.BookTitle,
                g.Sum(BookSalesService.NetQty), g.Sum(BookSalesService.NetTotal),
                stockById.GetValueOrDefault(g.Key.BookId, 0)))
            .OrderByDescending(x => x.Qty)
            .ToList();

        var lowStock = books
            .Where(b => b.IsActive && b.Stock <= 3)
            .OrderBy(b => b.Stock).ThenBy(b => b.Title)
            .Select(b => new BookSalesByBookDto(b.Id, b.Title, 0, 0m, b.Stock))
            .ToList();

        // "Davr ichida kirim" — nashriyotdan olingan/boshlang'ich qoldiq. QAYTARILGAN kitob ham
        // qoldiqni oshiradi, lekin u kirim EMAS (aks holda raqam sotuv qaytgani hisobiga shishardi).
        var movesQuery = db.BookStockMoves.AsNoTracking()
            .Where(m => m.Qty > 0 && m.Reason != BookSalesService.ReasonReturn);
        if (fromDt is not null) movesQuery = movesQuery.Where(m => m.CreatedAt >= fromDt);
        if (toDt is not null) movesQuery = movesQuery.Where(m => m.CreatedAt < toDt);
        var stockIn = await movesQuery.SumAsync(m => (int?)m.Qty) ?? 0;

        return new BookAnalyticsDto(
            From: from ?? "", To: to ?? "",
            OrdersApproved: approved.Count,
            OrdersPending: orders.Count(o => o.Status == BookSalesService.StatusPending),
            OrdersRejected: orders.Count(o => o.Status == BookSalesService.StatusRejected),
            SoldQty: approved.Sum(BookSalesService.NetQty),
            RevenueCash: SumOf(approved, BookSalesService.PayCash),
            RevenueCard: SumOf(approved, BookSalesService.PayCard),
            RevenueTotal: approved.Sum(BookSalesService.NetTotal),
            StockTotal: books.Sum(b => b.Stock),
            StockInQty: stockIn,
            ByDay: byDay,
            ByBook: byBook,
            LowStock: lowStock,
            ByDayBook: byDayBook,
            Sales: sales,
            SalesTruncated: approved.Count > MaxSalesFeed,
            CreditSold: creditSold.Sum(BookSalesService.NetTotal),
            CreditSoldCount: creditSold.Count,
            CreditSoldPaid: creditSold.Where(o => o.PaidAt != null).Sum(BookSalesService.NetTotal),
            CreditOutstanding: unpaidCredits.Sum(BookSalesService.NetTotal),
            CreditOutstandingCount: unpaidCredits.Count,
            CreditOverdue: overdueCredits.Sum(BookSalesService.NetTotal),
            CreditOverdueCount: overdueCredits.Count,
            CreditCollected: creditCollected,
            CreditCollectedCount: creditCollectedCount,
            // Davr SOTUVLARIDAN qaytarilgani (yuqoridagi sof raqamlar shu qadar kamaygan).
            ReturnedQty: approved.Sum(o => o.ReturnedQty),
            ReturnedTotal: approved.Sum(BookSalesService.ReturnedAmount),
            // Davr ICHIDA kassadan qaytarilgan pul (qaytarish sanasi bo'yicha).
            RefundedInPeriod: refundedInPeriod,
            RefundedCount: refundedCount);
    }

    // =============================================================================================
    //  EXCEL EKSPORT
    // =============================================================================================

    /// <summary>Sotuvlar tarixi (buyurtmalar) — .xlsx. Filtrlar ro'yxatdagi bilan bir xil.</summary>
    [HttpGet("orders/export")]
    public async Task<IActionResult> ExportOrders(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? bookId, [FromQuery] string? method, [FromQuery] string? q)
    {
        var dtos = await ToOrderDtosAsync(await FilteredOrdersAsync(status, from, to, bookId, method, q));
        return File(ExcelExport.Build("Kitob sotuvlari", OrderHeaders, OrderRows(dtos)), XlsxMime,
            $"kitob_sotuvlari_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>NASIYA ro'yxati — .xlsx (qarzdorlarni chop etib olib yurish uchun).</summary>
    [HttpGet("credits/export")]
    public async Task<IActionResult> ExportCredits(
        [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? q)
    {
        var result = await Credits(status, from, to, q);
        var data = result.Value;
        if (data is null) return BadRequest();

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Qarzdorlar",
                new[] { "Xaridor", "Telefon", "Nasiyalar", "Qarz (so'm)", "Eng eski sana", "Muddati o'tgan" },
                data.Debtors.Select(d => (IReadOnlyList<string>)new[]
                {
                    string.IsNullOrWhiteSpace(d.Name) ? "Noma'lum" : d.Name,
                    d.Phone, d.Orders.ToString(), AuditService.Money(d.Total),
                    d.OldestDate, d.HasOverdue ? "ha" : "",
                })),
            new ExcelExport.SheetSpec("Nasiyalar", OrderHeaders, OrderRows(data.Orders)),
        });
        return File(bytes, XlsxMime, $"kitob_nasiya_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    private static readonly string[] OrderHeaders =
    [
        "№", "Sana", "Mijoz", "Telefon", "O'quvchi", "Kitob", "Soni",
        "Narx", "Summa", "To'lov turi", "To'lov holati", "Muddat", "To'landi",
        "Holat", "Sabab", "Qaror vaqti", "Qaror qildi",
        // QAYTARISH: "Sof summa" = Summa − qaytarilganlar qiymati (hisobotlarda AYNAN shu).
        "Qaytarilgan (dona)", "Qaytarilgan sana", "Qaytarish sababi", "Qaytarilgan pul", "Sof summa",
    ];

    private static IEnumerable<IReadOnlyList<string>> OrderRows(IEnumerable<BookOrderDto> dtos) =>
        dtos.Select(o => (IReadOnlyList<string>)new[]
        {
            o.Number.ToString(),
            o.CreatedAt.Replace('T', ' '),
            o.CustomerName,
            o.Phone,
            o.StudentName ?? "",
            o.BookTitle,
            o.Qty.ToString(),
            AuditService.Money(o.UnitPrice),
            AuditService.Money(o.Total),
            BookSalesService.PaymentLabel(o.PaymentMethod),
            // Nasiyada eng muhim ustun: pul olindimi yoki hali qarzmi.
            o.PaymentMethod != BookSalesService.PayCredit ? "" : o.IsPaid ? "To'langan" : "Qarz",
            o.DueDate ?? "",
            o.PaidAt?.Replace('T', ' ') ?? "",
            BookSalesService.StatusLabel(o.Status),
            o.RejectReason,
            o.DecidedAt?.Replace('T', ' ') ?? "",
            o.DecidedBy,
            o.ReturnedQty > 0 ? o.ReturnedQty.ToString() : "",
            o.ReturnedAt?.Replace('T', ' ') ?? "",
            o.ReturnReason,
            o.RefundedAmount > 0 ? AuditService.Money(o.RefundedAmount) : "",
            AuditService.Money(o.NetTotal),
        });

    /// <summary>Ombor harakatlari (kirim tarixi) — .xlsx.</summary>
    [HttpGet("stock-moves/export")]
    public async Task<IActionResult> ExportStockMoves(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? bookId,
        [FromQuery] bool onlyIn = false)
    {
        var moves = await FilteredMovesAsync(from, to, bookId, onlyIn);
        var headers = new[] { "Sana", "Kitob", "Miqdor", "Turi", "Izoh", "Qoldiq (keyin)", "Kim" };
        var rows = moves.Select(m => (IReadOnlyList<string>)new[]
        {
            m.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            m.BookTitle,
            (m.Qty > 0 ? "+" : "") + m.Qty,
            ReasonLabel(m.Reason),
            m.Note,
            m.StockAfter.ToString(),
            m.CreatedBy,
        });
        return File(ExcelExport.Build("Ombor harakati", headers, rows), XlsxMime,
            $"kitob_ombor_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>Moliyaviy/analitik hisobot — 3 varaqli .xlsx: umumiy, kunlik, kitob kesimi.</summary>
    [HttpGet("analytics/export")]
    public async Task<IActionResult> ExportAnalytics([FromQuery] string? from, [FromQuery] string? to)
    {
        var a = await BuildAnalyticsAsync(from, to);

        var summary = new List<IReadOnlyList<string>>
        {
            new[] { "Davr", $"{(a.From.Length > 0 ? a.From : "boshidan")} — {(a.To.Length > 0 ? a.To : "hozirgacha")}" },
            new[] { "Tasdiqlangan buyurtmalar", a.OrdersApproved.ToString() },
            new[] { "Kutilayotgan buyurtmalar", a.OrdersPending.ToString() },
            new[] { "Rad etilgan buyurtmalar", a.OrdersRejected.ToString() },
            new[] { "Sotilgan kitob (dona, SOF)", a.SoldQty.ToString() },
            new[] { "Qaytarilgan (dona)", a.ReturnedQty.ToString() },
            new[] { "Qaytarilgan (so'm)", AuditService.Money(a.ReturnedTotal) },
            new[] { "Davr ichida qaytarilgan pul (so'm)", AuditService.Money(a.RefundedInPeriod) },
            new[] { "Sotuv — Naqd (so'm)", AuditService.Money(a.RevenueCash) },
            new[] { "Sotuv — Karta (so'm)", AuditService.Money(a.RevenueCard) },
            new[] { "Sotuv — Nasiya (so'm)", AuditService.Money(a.CreditSold) },
            new[] { "Sotuv — Jami (so'm)", AuditService.Money(a.RevenueTotal) },
            new[] { "Nasiyaga sotuvlar (ta)", a.CreditSoldCount.ToString() },
            new[] { "Shundan to'langan (so'm)", AuditService.Money(a.CreditSoldPaid) },
            new[] { "Davr ichida nasiyadan yig'ildi (so'm)", AuditService.Money(a.CreditCollected) },
            new[] { "JORIY QARZ — jami (so'm)", AuditService.Money(a.CreditOutstanding) },
            new[] { "JORIY QARZ — nasiyalar (ta)", a.CreditOutstandingCount.ToString() },
            new[] { "JORIY QARZ — muddati o'tgan (so'm)", AuditService.Money(a.CreditOverdue) },
            new[] { "Davr ichida kirim (dona)", a.StockInQty.ToString() },
            new[] { "Ombordagi qoldiq (dona)", a.StockTotal.ToString() },
        };

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Umumiy", new[] { "Ko'rsatkich", "Qiymat" }, summary),
            new ExcelExport.SheetSpec("Kunlik",
                new[] { "Sana", "Dona", "Naqd", "Karta", "Nasiya", "Jami", "Qaytarilgan (dona)", "Qaytarilgan (so'm)" },
                a.ByDay.Select(d => (IReadOnlyList<string>)new[]
                {
                    d.Date, d.Qty.ToString(), AuditService.Money(d.Cash),
                    AuditService.Money(d.Card), AuditService.Money(d.Credit),
                    AuditService.Money(d.Total),
                    d.ReturnedQty > 0 ? d.ReturnedQty.ToString() : "",
                    d.ReturnedTotal > 0 ? AuditService.Money(d.ReturnedTotal) : "",
                })),
            // "Har kuni qaysi kitob sotildi" — bo'limning asosiy so'rovi (kun × kitob).
            new ExcelExport.SheetSpec("Kunlik kitoblar",
                new[] { "Sana", "Kitob", "Dona", "Summa", "Sotuvlar", "Qaytarilgan" },
                a.ByDayBook.Select(d => (IReadOnlyList<string>)new[]
                {
                    d.Date, d.BookTitle, d.Qty.ToString(),
                    AuditService.Money(d.Total), d.Orders.ToString(),
                    d.ReturnedQty > 0 ? d.ReturnedQty.ToString() : "",
                })),
            new ExcelExport.SheetSpec("Kitoblar",
                new[] { "Kitob", "Sotilgan (dona)", "Tushum", "Joriy qoldiq" },
                a.ByBook.Select(b => (IReadOnlyList<string>)new[]
                {
                    b.BookTitle, b.Qty.ToString(), AuditService.Money(b.Total), b.Stock.ToString(),
                })),
        });
        return File(bytes, XlsxMime, $"kitob_hisobot_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // =============================================================================================
    //  SOZLAMALAR — botda ko'rinadigan to'lov rekvizitlari
    // =============================================================================================

    /// <summary>Botdagi kitob sotuvi sozlamalari (karta raqami/egasi). Maxfiy emas — mijozga
    /// baribir ko'rsatiladi, shuning uchun bazada (CenterMeta) saqlanadi.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<BookSettingsDto>> GetSettings()
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();
        return new BookSettingsDto(
            meta?.BookSalesEnabled ?? true,
            meta?.BookCardNumber ?? "",
            meta?.BookCardHolder ?? "",
            meta?.BookPaymentNote ?? "");
    }

    [HttpPut("settings")]
    public async Task<ActionResult<BookSettingsDto>> SaveSettings(BookSettingsDto payload)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            meta = new CenterMeta();
            db.CenterMeta.Add(meta);
        }
        meta.BookSalesEnabled = payload.BookSalesEnabled;
        meta.BookCardNumber = (payload.BookCardNumber ?? "").Trim();
        meta.BookCardHolder = (payload.BookCardHolder ?? "").Trim();
        meta.BookPaymentNote = (payload.BookPaymentNote ?? "").Trim();
        audit.Record("Book", "settings", "update",
            "Kitoblar sotuvi sozlamalari o'zgartirildi — botda sotuv: " +
            (meta.BookSalesEnabled ? "YOQILGAN" : "O'CHIRILGAN"));
        await db.SaveChangesAsync();
        return new BookSettingsDto(
            meta.BookSalesEnabled, meta.BookCardNumber, meta.BookCardHolder, meta.BookPaymentNote);
    }

    /// <summary>Kitob muqovasini yuklash — umumiy uploads endpoint'i bilan bir xil, lekin
    /// <c>books</c> ruxsati bilan (kitob bilan ishlaydigan xodimga "uploads" roli kerak bo'lmasin).</summary>
    [HttpPost("cover")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> UploadCover(IFormFile file)
    {
        if (UploadGuard.Validate(file) is { } error) return BadRequest(new { message = error });

        var dir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var stored = UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);
        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }

    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    private static string ReasonLabel(string reason) => reason switch
    {
        BookSalesService.ReasonInitial => "Boshlang'ich qoldiq",
        BookSalesService.ReasonRestock => "Kirim",
        BookSalesService.ReasonSale => "Sotuv",
        BookSalesService.ReasonReturn => "Qaytarish",
        BookSalesService.ReasonCorrection => "Korreksiya",
        _ => reason,
    };

    /// <summary>"YYYY-MM-DD" filtrlarini <see cref="DateTime"/> oraliqqa aylantiradi
    /// (<paramref name="to"/> — SHU KUN ham kirsin uchun +1 kun, yarim ochiq oraliq).</summary>
    private static (DateTime? From, DateTime? To) DateRange(string? from, string? to)
    {
        DateTime? f = DateTime.TryParse(from, out var fd) ? fd.Date : null;
        DateTime? t = DateTime.TryParse(to, out var td) ? td.Date.AddDays(1) : null;
        return (f, t);
    }
}
