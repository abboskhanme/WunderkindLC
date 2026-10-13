using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// QO'LDA KITOB SOTUVI (markazda, joyida) — `books.md` §2.1. Ilgari butun mantiq `BooksController.ManualSale` ichida
/// edi; 2026-10-13 dan u kassa portalidan ham chaqiriladi (o'quvchi to'lov oynasidagi «Kitob sotuvi» turi), shuning
/// uchun BITTA joyga ko'chirildi — qoidalar (qoldiq, nasiya, karta maydonlari, ombor, kassaga kirim `BookFinance`, audit)
/// ikki joyda ayri ketmasin. Muvaffaqiyatda SAQLAYDI; xatoda hech narsa yozilmaydi va foydalanuvchi matni qaytadi.
/// </summary>
public static class BookManualSale
{
    public static async Task<(BookOrder? Order, string? Error)> CreateAsync(
        IAppDbContext db, AuditService audit, BookManualSalePayload payload, string actor, string? actorId,
        CancellationToken ct = default)
    {
        if (payload.Qty <= 0) return (null, "Sonini kiriting (kamida 1 dona)");
        if (payload.Qty > 500) return (null, "Soni juda katta (maksimal 500 dona)");

        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == payload.BookId, ct);
        // Sotuvdan olingan kitob sotilmaydi. Frontend uni ro'yxatda ko'rsatmaydi, lekin bu
        // endpoint to'g'ridan-to'g'ri chaqirilsa ilgari hech qanday to'siq yo'q edi.
        var bookError = BookSalesService.ManualSaleBookError(book);
        if (book is null || bookError is not null) return (null, bookError ?? "Kitob topilmadi");

        // O'QUVCHI IXTIYORIY. Ilgari majburiy edi va kassir markazda o'qimaydigan odamga
        // (ota-ona, qo'shni maktab o'quvchisi, o'tkinchi) kitob sota olmasdi — buning uchun soxta
        // o'quvchi yaratishga to'g'ri kelardi. `BookOrder.StudentId` allaqachon nullable: bot
        // oqimida mehmon buyurtmasi shunday yoziladi.
        Student? student = null;
        var studentId = (payload.StudentId ?? "").Trim();
        if (studentId.Length > 0)
        {
            student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
            // Id berilgan, lekin topilmadi — bu xato (jim o'tkazib yuborilsa sotuv noto'g'ri
            // odamga teglanmay qolardi va kassir buni sezmasdi).
            if (student is null) return (null, "O'quvchi topilmadi");
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
            return (null, customerError);

        // Karta to'lovida oxirgi 4 raqam va to'lov vaqti — moliya bo'limi bilan BIR XIL
        // normalizatsiya (PaymentFields): to'liq karta raqami hech qachon saqlanmaydi.
        string? last4 = null;
        string? paidTime = null;
        if (method == BookSalesService.PayCard)
        {
            if (!PaymentFields.TryNormalizeCardLast4(payload.CardLast4, out last4))
                return (null, "Karta raqamining oxirgi 4 raqamini to'liq kiriting");
            if (last4 is null)
                return (null, "Karta to'lovida oxirgi 4 raqam majburiy");
            if (!PaymentFields.TryNormalizeTime(payload.PaidTime, out paidTime))
                return (null, "To'lov vaqti noto'g'ri (HH:mm)");
            if (paidTime is null)
                return (null, "Karta to'lovida to'lov vaqti majburiy");
        }

        // NASIYA: va'da qilingan sana (ixtiyoriy). Noto'g'ri matn jimgina yutilmaydi — kassir
        // sanani yozdim deb o'ylab, ro'yxatda "muddatsiz" ko'rib qolmasin.
        DateTime? dueDate = null;
        if (method == BookSalesService.PayCredit && !string.IsNullOrWhiteSpace(payload.DueDate))
        {
            if (!DateTime.TryParse(payload.DueDate, out var due))
                return (null, "To'lov muddati noto'g'ri (YYYY-MM-DD)");
            dueDate = due.Date;
        }

        var order = new BookOrder
        {
            Number = await BookSalesService.NextOrderNumberAsync(db, ct),
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
        var error = await BookSalesService.ApproveAsync(db, order, actor, actorId, ct);
        if (error is not null) return (null, error);

        audit.Record("BookOrder", order.Id, "create",
            $"Kitob qo'lda sotildi{(method == BookSalesService.PayCredit ? " (NASIYAGA)" : "")}: " +
            $"{order.BookTitle} x {order.Qty} dona — {AuditService.Money(order.Total)} so'm" +
            // Ism bo'sh bo'lishi mumkin (chetdan xaridor) — bo'sh qavs qolib ketmasin.
            (order.CustomerName.Length > 0 ? $" ({order.CustomerName})" : "") +
            (dueDate is { } d ? $", to'lov muddati {d:yyyy-MM-dd}" : ""),
            studentId: order.StudentId);
        await db.SaveChangesAsync(ct);
        return (order, null);
        }
}
