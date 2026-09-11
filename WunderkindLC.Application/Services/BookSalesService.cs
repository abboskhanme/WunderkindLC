using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KITOBLAR SOTUVI — ombor va buyurtma mantig'ining YAGONA joyi. Admin panel
/// (<c>BooksController</c>) ham, Telegram bot (<see cref="BookShopBotService"/>) ham shu yerdan
/// foydalanadi, shunda "qoldiq qanday o'zgaradi" qoidasi ikki joyda ikki xil bo'lib ketmaydi.
///
/// <para><b>Qoldiq (ostatka) qoidasi:</b> <see cref="Book.Stock"/> — joriy qoldiq, va uning HAR
/// bir o'zgarishi <see cref="BookStockMove"/> ga yoziladi (musbat = kirim, manfiy = chiqim).
/// Buyurtma tushganda qoldiq TEGILMAYDI — faqat admin <b>tasdiqlaganda</b> ayiriladi (spetsifikatsiya:
/// "admin tasdiqlasa keyin o'quvchiga sotiladi ... sotilganidan keyin ostatkadan ayiradi").</para>
/// </summary>
public static class BookSalesService
{
    // Buyurtma holatlari
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    // Ombor harakati sabablari
    public const string ReasonInitial = "initial";
    public const string ReasonRestock = "restock";
    public const string ReasonSale = "sale";
    public const string ReasonCorrection = "correction";

    /// <summary>
    /// QAYTARISH (vozvrat) — mijoz kitobni qaytarib berdi, dona omborga qaytdi.
    /// <b>Kirim (<see cref="ReasonRestock"/>) EMAS</b>: "davr ichida kirim" hisoboti nashriyotdan
    /// olingan kitoblarni ko'rsatadi, qaytarilgan sotuv esa u yerga qo'shilsa raqam shishardi.
    /// </summary>
    public const string ReasonReturn = "return";

    // To'lov turlari (avtomatik to'lov tizimi YO'Q — faqat naqd yoki karta raqamiga o'tkazma)
    public const string PayCash = "cash";
    public const string PayCard = "card";

    /// <summary>
    /// NASIYA — kitob berildi, pul keyin olinadi. Buyurtma odatdagidek tasdiqlanadi (qoldiqdan
    /// ayiriladi), lekin <see cref="BookOrder.PaidAt"/> bo'sh turadi va summa "qarz" bo'lib
    /// sanaladi; kassir pulni olgach <see cref="PayCreditAsync"/> uni to'lovlarga qo'shadi.
    /// FAQAT markazda qo'lda sotuvda (botda YO'Q — noma'lum Telegram mijoziga qarz berilmaydi).
    /// </summary>
    public const string PayCredit = "credit";

    // Buyurtma manbai: mijoz botdan bergan yoki markazda qo'lda sotilgan
    public const string SourceBot = "bot";
    public const string SourceManual = "manual";

    public static string PaymentLabel(string? method) => method switch
    {
        PayCard => "Karta",
        PayCash => "Naqd",
        PayCredit => "Nasiya",
        _ => method ?? "",
    };

    public static string SourceLabel(string? source) =>
        source == SourceManual ? "Qo'lda" : "Bot";

    public static string StatusLabel(string? status) => status switch
    {
        StatusApproved => "Tasdiqlangan",
        StatusRejected => "Rad etilgan",
        _ => "Kutilmoqda",
    };

    /// <summary>
    /// Ombor qoldig'ini o'zgartiradi va tarixga (<see cref="BookStockMove"/>) yozadi.
    /// <paramref name="qty"/> musbat = kirim, manfiy = chiqim. <b>SaveChanges chaqirilmaydi</b> —
    /// chaqiruvchi o'z tranzaksiyasida saqlaydi (buyurtma tasdiqlash bilan bitta SaveChanges'da ketsin).
    /// </summary>
    public static BookStockMove Move(
        Book book, int qty, string reason, string note, string createdBy, string? orderId = null)
    {
        book.Stock += qty;
        return new BookStockMove
        {
            BookId = book.Id,
            BookTitle = book.Title,
            Qty = qty,
            Reason = reason,
            OrderId = orderId,
            Note = note ?? string.Empty,
            StockAfter = book.Stock,
            CreatedBy = createdBy ?? string.Empty,
        };
    }

    /// <summary>
    /// BUYURTMA RAQAMI NAVBATI — bir vaqtda faqat BITTA chaqiruv raqam oladi.
    /// Sabab: raqam bazadagi eng kattasidan keyingisi bo'lib beriladi, lekin raqam olingandan
    /// keyin buyurtma bazaga YOZILGUNCHA bir necha <c>await</c> bor (o'quvchi qidiruvi, chek
    /// saqlash va h.k.). Ikki kassir (yoki bot bilan kassir) bir vaqtda sotsa, ikkalasi ham bir
    /// xil "eng katta"ni ko'rib, IKKALA buyurtma ham <c>#57</c> bo'lib qolardi.
    /// Ilova BITTA nusxada ishlagani uchun jarayon ichidagi navbat yetarli
    /// (<c>TestCertificateService.NumberGate</c> bilan bir xil yondashuv).
    /// </summary>
    private static readonly SemaphoreSlim NumberGate = new(1, 1);

    /// <summary>
    /// Shu jarayonda oxirgi BERILGAN raqam — hali bazaga yozilmagan bo'lishi mumkin.
    /// Faqat qulfning o'zi yetmaydi: qulf ostida berilgan raqam bazada darhol paydo bo'lmaydi,
    /// shuning uchun keyingi chaqiruv <c>MAX(Number)</c> dan yana o'shani ko'rardi. Belgi shu
    /// "berildi, lekin hali saqlanmadi" oralig'ini qoplaydi.
    /// </summary>
    private static int _lastIssuedNumber;

    /// <summary>Keyingi ko'rsatiladigan buyurtma raqami (#1, #2 ...). Qarang: <see cref="NumberGate"/>.</summary>
    public static async Task<int> NextOrderNumberAsync(IAppDbContext db, CancellationToken ct = default)
    {
        await NumberGate.WaitAsync(ct);
        try
        {
            var max = await db.BookOrders.MaxAsync(o => (int?)o.Number, ct) ?? 0;
            // Raqam olingach buyurtma yozilmasligi mumkin (masalan qoldiq yetmadi) — u holda shu
            // raqam "kuyadi" va ro'yxatda bo'shliq qoladi. Bu takrorlanishdan ko'ra zararsizroq.
            var next = Math.Max(max, _lastIssuedNumber) + 1;
            _lastIssuedNumber = next;
            return next;
        }
        finally
        {
            NumberGate.Release();
        }
    }

    /// <summary>
    /// FAQAT TESTLAR uchun: raqam navbatining xotiradagi belgisini nolga qaytaradi. Har test o'z
    /// bazasi bilan ishlagani uchun, oldingi testdan qolgan belgi natijani buzmasin.
    /// </summary>
    public static void ResetOrderNumberSequence() => _lastIssuedNumber = 0;

    /// <summary>Telefon bo'yicha qidiruvda talab qilinadigan eng kam raqam soni.</summary>
    public const int MinPhoneDigits = 4;

    /// <summary>
    /// TELEFON QIDIRUVI mosligi (qo'lda sotuvda o'quvchi tanlash uchun).
    ///
    /// <para>Bazada raqam <c>+998-90-123-45-67</c> ko'rinishida saqlanadi, ya'ni HAMMA raqam
    /// <c>998</c> bilan boshlanadi. Shu sabab xom raqamlar ustida oddiy <c>Contains</c> qilinsa,
    /// "9989" kabi so'rov deyarli har bir o'quvchiga mos kelib, kassirga tasodifiy begona
    /// odamlar ro'yxatini chiqarardi.</para>
    ///
    /// <para>Yechim: ikkala tomon ham MAHALLIY qismga keltiriladi (mamlakat kodisiz oxirgi 9
    /// raqam — <see cref="PhoneUtil.Key"/>), keyin solishtiriladi. Shunda "9989" faqat mahalliy
    /// raqami aynan shu bo'lakni o'z ichiga olganlarga mos keladi.</para>
    /// </summary>
    /// <param name="stored">Bazadagi raqam (istalgan formatda).</param>
    /// <param name="needleKey">Qidiruv raqami — allaqachon <see cref="PhoneUtil.Key"/> dan o'tgan.</param>
    public static bool PhoneMatches(string? stored, string needleKey) =>
        needleKey.Length >= MinPhoneDigits && PhoneUtil.Key(stored).Contains(needleKey);

    /// <summary>
    /// QO'LDA SOTUV uchun kitob darvozasi: sotuvdan olingan (<c>IsActive=false</c>) kitobni
    /// sotib bo'lmaydi. Frontend'da bunday kitob ro'yxatda ko'rinmaydi, lekin API to'g'ridan-to'g'ri
    /// chaqirilsa tekshiruv YO'Q edi — sotuvdan olingan kitob baribir sotilardi.
    /// </summary>
    /// <returns><c>null</c> — sotsa bo'ladi; aks holda foydalanuvchiga ko'rsatiladigan xato matni.</returns>
    public static string? ManualSaleBookError(Book? book) =>
        book is null ? "Kitob topilmadi"
        : !book.IsActive ? "Kitob sotuvdan olingan — avval \"Sotuvda\" belgisini yoqing"
        : null;

    // =============================================================================================
    //  NASIYA (credit) — kitob berildi, pul keyin olinadi
    // =============================================================================================

    /// <summary>Buyurtma nasiyaga sotilganmi.</summary>
    public static bool IsCredit(BookOrder o) => o.PaymentMethod == PayCredit;

    /// <summary>
    /// PUL OLINGANMI. Naqd/karta sotuvda pul tasdiqlash paytida olinadi, ya'ni tasdiqlangan
    /// buyurtma = to'langan. Nasiyada esa pul KEYIN olinadi — <see cref="BookOrder.PaidAt"/>
    /// to'lgunicha bu <c>false</c>, ya'ni summa tushumga emas, QARZGA sanaladi.
    ///
    /// <para>⚠️ ATAYIN <c>PaidAt != null</c> emas: eski (nasiya moduli qo'shilishidan oldingi)
    /// qatorlarda <c>PaidAt</c> bo'sh, lekin ular naqd/karta bo'lgani uchun to'langan hisoblanadi
    /// — migratsiyadagi to'ldirish bajarilmagan bazada ham hisobot to'g'ri chiqsin.</para>
    /// </summary>
    public static bool IsPaid(BookOrder o) =>
        o.Status == StatusApproved && (!IsCredit(o) || o.PaidAt != null);

    /// <summary>
    /// Pul QAYSI ko'rinishda olingani: naqd/kartada — o'sha turning o'zi, nasiyada — u qanday
    /// yopilgani (<see cref="BookOrder.SettledMethod"/>). Hali to'lanmagan nasiyada
    /// <see cref="PayCredit"/> qaytadi.
    /// </summary>
    public static string EffectiveMethod(BookOrder o) =>
        IsCredit(o) ? (string.IsNullOrEmpty(o.SettledMethod) ? PayCredit : o.SettledMethod!) : o.PaymentMethod;

    /// <summary>
    /// MUDDATI O'TGAN nasiya: to'lanmagan va va'da qilingan sana <paramref name="today"/> dan
    /// oldin. Sana belgilanmagan nasiya hech qachon "muddati o'tgan" bo'lmaydi (kassir muddat
    /// qo'ymagan bo'lsa uni kechikkan deb ayblash noto'g'ri bo'lardi).
    /// <paramref name="today"/> PARAMETR — funksiya sof qolsin uchun ichkarida AppClock o'qilmaydi.
    /// </summary>
    public static bool IsOverdue(BookOrder o, DateTime today) =>
        IsCredit(o) && o.Status == StatusApproved && o.PaidAt is null
        && o.DueDate is { } due && due.Date < today.Date;

    /// <summary>
    /// NASIYADA XARIDOR MAJBURIY: qarzni kimdan olish kerakligi yozilmasa nasiya ma'nosini
    /// yo'qotadi. Naqd/kartada esa xaridor ixtiyoriy bo'lib qoladi (chetdan kelgan odam).
    /// </summary>
    /// <returns><c>null</c> — sotsa bo'ladi; aks holda xato matni.</returns>
    public static string? CreditCustomerError(string? method, string? studentId, string? customerName) =>
        method == PayCredit
        && string.IsNullOrWhiteSpace(studentId)
        && string.IsNullOrWhiteSpace(customerName)
            ? "Nasiyada xaridor ko'rsatilishi shart — o'quvchini F.I.Sh. bo'yicha qidirib tanlang "
              + "yoki xaridor ismini yozing."
            : null;

    /// <summary>
    /// NASIYA TO'LOVINI QABUL QILISH ("pulini oldim → Tasdiqlash"): buyurtma to'langan deb
    /// belgilanadi va summa shu paytdan boshlab tushumga (to'lovlarga) qo'shiladi.
    /// <b>Ombor TEGILMAYDI</b> — kitob allaqachon sotuv paytida berilgan va qoldiqdan ayirilgan.
    /// </summary>
    /// <param name="method">Pul qanday olindi: <see cref="PayCash"/> yoki <see cref="PayCard"/>.</param>
    /// <param name="cardLast4">Karta bo'lsa — oxirgi 4 raqam (to'liq raqam SAQLANMAYDI).</param>
    /// <returns><c>null</c> — muvaffaqiyat; aks holda foydalanuvchiga ko'rsatiladigan xato matni.</returns>
    public static async Task<string?> PayCreditAsync(
        IAppDbContext db, BookOrder order, string method, string? cardLast4, string paidBy,
        CancellationToken ct = default)
    {
        if (!IsCredit(order)) return "Bu buyurtma nasiyaga sotilmagan — to'lov allaqachon olingan.";
        if (order.Status != StatusApproved)
            return $"Buyurtma holati mos emas ({StatusLabel(order.Status)}).";
        if (order.PaidAt is not null) return "Bu nasiya allaqachon to'langan deb belgilangan.";
        // TO'LIQ QAYTARILGAN nasiyada olinadigan pul qolmaydi (qarz 0 ga tushgan) — "To'landi"
        // bosilsa summa tushumga qo'shilib, qaytarilgan kitob sotilgandek ko'rinardi.
        if (IsFullyReturned(order))
            return "Bu nasiya to'liq qaytarilgan — qarz qolmagan, to'lov qabul qilinmaydi.";
        if (method != PayCash && method != PayCard)
            return "To'lov turini tanlang: naqd yoki karta.";

        order.PaidAt = AppClock.Now;
        order.PaidBy = paidBy ?? string.Empty;
        order.SettledMethod = method;
        // Karta bo'lsa oxirgi 4 raqam yoziladi; naqdda eski qiymat qolib ketmasin.
        order.CardLast4 = method == PayCard ? cardLast4 : null;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Buyurtmani TASDIQLAYDI: qoldiqdan kitob soni ayiriladi, harakat tarixi yoziladi, holat
    /// <c>approved</c> bo'ladi. Qoldiq yetmasa yoki buyurtma allaqachon hal qilingan bo'lsa —
    /// o'zgartirmasdan xato matnini qaytaradi (chaqiruvchi 400 qiladi va mijozga xabar yubormaydi).
    /// </summary>
    /// <returns><c>null</c> — muvaffaqiyat; aks holda foydalanuvchiga ko'rsatiladigan xato matni.</returns>
    public static async Task<string?> ApproveAsync(
        IAppDbContext db, BookOrder order, string decidedBy, CancellationToken ct = default)
    {
        if (order.Status != StatusPending)
            return $"Buyurtma allaqachon hal qilingan ({StatusLabel(order.Status)}).";

        var book = await db.Books.FirstOrDefaultAsync(x => x.Id == order.BookId, ct);
        if (book is null) return "Kitob topilmadi (o'chirilgan bo'lishi mumkin).";
        if (book.Stock < order.Qty)
            return $"Omborda yetarli emas: qoldiq {book.Stock} dona, buyurtma {order.Qty} dona.";

        var move = Move(
            book, -order.Qty, ReasonSale,
            $"Buyurtma #{order.Number} — {order.CustomerName}".Trim(), decidedBy, order.Id);
        db.BookStockMoves.Add(move);

        order.Status = StatusApproved;
        order.RejectReason = string.Empty;
        order.DecidedAt = AppClock.Now;
        order.DecidedBy = decidedBy ?? string.Empty;
        // Naqd/kartada pul shu paytda olinadi. NASIYADA esa yo'q — `PaidAt` bo'sh qoladi va
        // buyurtma "qarz" bo'lib sanaladi (qarang: PayCreditAsync).
        if (!IsCredit(order)) order.PaidAt = order.DecidedAt;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // QOLDIQ POYGASI: yuqoridagi tekshiruv bilan SaveChanges orasida BOSHQA amal
            // (ikkinchi kassir, bot buyurtmasini tasdiqlash, ombor korreksiyasi) shu kitobning
            // qoldig'ini o'zgartirgan. `Book.Stock` konkurentlik tokeni bo'lgani uchun (qarang:
            // AppDbContext, "Kitoblar sotuvi") EF ning UPDATE'i 0 qator yangilagan — ya'ni bazaga
            // HECH NARSA yozilmagan. Xotiradagi o'zgarishlarni ham qaytaramiz: chaqiruvchi
            // "ayrilgan" qoldiqni yoki "tasdiqlangan" buyurtmani ko'rib qolmasin.
            book.Stock += order.Qty;
            db.BookStockMoves.Remove(move);
            order.Status = StatusPending;
            order.DecidedAt = null;
            order.DecidedBy = string.Empty;
            order.PaidAt = null;
            return "Qoldiq shu payt boshqa amalda o'zgardi — qaytadan urinib ko'ring.";
        }
        return null;
    }

    /// <summary>Buyurtmani RAD etadi (sabab bilan). Qoldiq tegilmaydi.</summary>
    /// <returns><c>null</c> — muvaffaqiyat; aks holda xato matni.</returns>
    public static async Task<string?> RejectAsync(
        IAppDbContext db, BookOrder order, string reason, string decidedBy, CancellationToken ct = default)
    {
        if (order.Status != StatusPending)
            return $"Buyurtma allaqachon hal qilingan ({StatusLabel(order.Status)}).";

        order.Status = StatusRejected;
        order.RejectReason = (reason ?? string.Empty).Trim();
        order.DecidedAt = AppClock.Now;
        order.DecidedBy = decidedBy ?? string.Empty;
        await db.SaveChangesAsync(ct);
        return null;
    }

    // =============================================================================================
    //  QAYTARISH (vozvrat) — mijoz kitobni qaytarib berdi
    //
    //  Ikki narsa bir vaqtda to'g'rilanadi:
    //    1) OMBOR — qaytarilgan dona qoldiqqa qo'shiladi (`BookStockMove`, Reason="return");
    //    2) PUL   — sotuv summasidan qaytarilgan qismi AYIRILADI, ya'ni barcha hisobotlar
    //               (tushum, kunlik grafik, kitob kesimi, qarz) SOF qiymat bilan ishlaydi.
    //
    //  Buyurtma HOLATI o'zgarmaydi ("approved" bo'lib qoladi): qaytarish QISMAN ham bo'ladi
    //  (3 dona sotilib, 1 tasi qaytarilishi mumkin) va "rad etilgan" holat buni ifodalay olmaydi.
    //  Rad etish (`RejectAsync`) esa hali BERILMAGAN buyurtma uchun — u ombordan hech narsa
    //  ayirmagan, shuning uchun qaytariladigan narsa ham yo'q.
    // =============================================================================================

    /// <summary>Shu buyurtmadan qaytarilgan kitoblarning SOTUV summasi (dona × sotuv narxi).</summary>
    public static decimal ReturnedAmount(BookOrder o) => o.UnitPrice * o.ReturnedQty;

    /// <summary>SOF (haqiqatan mijozda qolgan) dona = sotilgan − qaytarilgan.</summary>
    public static int NetQty(BookOrder o) => o.Qty - o.ReturnedQty;

    /// <summary>SOF summa = sotuv summasi − qaytarilganlarning qiymati. Hisobotlarda
    /// (tushum, qarz, kitob kesimi) <b>har doim shu</b> ishlatiladi, xom <c>Total</c> emas.</summary>
    public static decimal NetTotal(BookOrder o) => o.Total - ReturnedAmount(o);

    /// <summary>Butun buyurtma qaytarilganmi (mijozda hech narsa qolmagan).</summary>
    public static bool IsFullyReturned(BookOrder o) => o.Qty > 0 && o.ReturnedQty >= o.Qty;

    /// <summary>Yana qancha dona qaytarish mumkin.</summary>
    public static int ReturnableQty(BookOrder o) => Math.Max(0, NetQty(o));

    /// <summary>
    /// Shu qaytarishda mijozga QAYTARILADIGAN pul. Pul faqat ALLAQACHON OLINGAN bo'lsa qaytariladi:
    /// to'lanmagan nasiyada kassadan hech narsa chiqmaydi — qarz kamayadi, xolos.
    /// (Chaqiruvchi audit yozuvi va mijozga ketadigan xabar uchun ham shu funksiyadan foydalanadi —
    /// qoida ikki joyda ikki xil bo'lib ketmasin.)
    /// </summary>
    public static decimal RefundFor(BookOrder o, int qty) => IsPaid(o) ? o.UnitPrice * qty : 0m;

    /// <summary>
    /// QAYTARISH DARVOZASI (sof funksiya — testlangan). <c>null</c> bo'lsa qaytarsa bo'ladi,
    /// aks holda foydalanuvchiga ko'rsatiladigan xato matni.
    /// </summary>
    /// <param name="qty">Hozir qaytarilayotgan dona.</param>
    public static string? ReturnError(BookOrder order, int qty)
    {
        // Faqat TASDIQLANGAN sotuvda kitob mijozga berilgan va ombordan ayirilgan. Kutilayotgan
        // buyurtma "Rad etish" bilan yopiladi (u qoldiqqa umuman tegmagan).
        if (order.Status != StatusApproved)
            return $"Faqat tasdiqlangan sotuvni qaytarish mumkin (hozirgi holat: {StatusLabel(order.Status)}). "
                   + "Kutilayotgan buyurtmani «Rad etish» bilan yoping.";
        if (qty <= 0) return "Qaytariladigan sonni kiriting (kamida 1 dona).";

        var left = ReturnableQty(order);
        if (left == 0)
            return $"Bu sotuv allaqachon to'liq qaytarilgan ({order.ReturnedQty} dona).";
        if (qty > left)
            return $"Bu sotuvdan ko'pi bilan {left} dona qaytarish mumkin "
                   + $"(sotilgan {order.Qty}, allaqachon qaytarilgan {order.ReturnedQty}).";
        return null;
    }

    /// <summary>
    /// SOTILGAN KITOBNI QAYTARISH: dona omborga qaytadi va sotuv summasidan o'sha qismi ayiriladi.
    ///
    /// <para><b>Pul:</b> faqat ALLAQACHON OLINGAN bo'lsa qaytariladi. To'lanmagan nasiyada pul
    /// umuman olinmagan — kitob qaytsa shunchaki QARZ kamayadi, kassadan hech narsa chiqmaydi.</para>
    ///
    /// <para>Qisman qaytarish qo'llab-quvvatlanadi va bir necha marta bo'lishi mumkin —
    /// <see cref="BookOrder.ReturnedQty"/> qo'shilib boradi, <see cref="BookOrder.Qty"/> dan oshmaydi.</para>
    /// </summary>
    /// <returns><c>null</c> — muvaffaqiyat; aks holda foydalanuvchiga ko'rsatiladigan xato matni.</returns>
    public static async Task<string?> ReturnAsync(
        IAppDbContext db, BookOrder order, int qty, string reason, string returnedBy,
        CancellationToken ct = default)
    {
        if (ReturnError(order, qty) is { } error) return error;

        // Kitob KUZATILGAN holda yuklanadi (`AsNoTracking` SIZ) — `Book.Stock` konkurentlik
        // tokeni bo'lgani uchun EF asl qiymatni bilishi shart.
        var book = await db.Books.FirstOrDefaultAsync(x => x.Id == order.BookId, ct);
        if (book is null) return "Kitob topilmadi (o'chirilgan bo'lishi mumkin).";

        // Pul mijozdan OLINGANMI — o'zgartirishdan OLDIN aniqlanadi.
        var refund = RefundFor(order, qty);

        var move = Move(
            book, qty, ReasonReturn,
            $"Buyurtma #{order.Number} — qaytarildi{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}")}",
            returnedBy, order.Id);
        db.BookStockMoves.Add(move);

        // Poygada tiklash uchun ESKI qiymatlar (bu buyurtma ilgari ham qisman qaytarilgan
        // bo'lishi mumkin — u holda "bo'sh"ga emas, aynan oldingi holatga qaytariladi).
        var prevAt = order.ReturnedAt;
        var prevBy = order.ReturnedBy;
        var prevReason = order.ReturnReason;

        order.ReturnedQty += qty;
        order.ReturnedAt = AppClock.Now;
        order.ReturnedBy = returnedBy ?? string.Empty;
        order.ReturnReason = (reason ?? string.Empty).Trim();
        order.RefundedAmount += refund;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // QOLDIQ POYGASI (`ApproveAsync` bilan bir xil): oraga boshqa amal tushgan, UPDATE 0
            // qator yangilagan va bazaga HECH NARSA yozilmagan. Xotiradagi o'zgarishlarni ham
            // qaytaramiz — chaqiruvchi "qaytarilgan" buyurtmani ko'rib qolmasin.
            book.Stock -= qty;
            db.BookStockMoves.Remove(move);
            order.ReturnedQty -= qty;
            order.RefundedAmount -= refund;
            order.ReturnedAt = prevAt;
            order.ReturnedBy = prevBy;
            order.ReturnReason = prevReason;
            return "Qoldiq shu payt boshqa amalda o'zgardi — qaytadan urinib ko'ring.";
        }
        return null;
    }

    // ---------------------------------------------------------------------------------
    //  Mijozga (botga) yuboriladigan xabar matnlari — controller ham, bot ham shu yerdan oladi
    // ---------------------------------------------------------------------------------

    public static string CustomerApprovedText(BookOrder o)
    {
        var lines = new List<string>
        {
            "✅ Buyurtmangiz tasdiqlandi!",
            "",
            $"📕 {o.BookTitle}",
            $"🔢 Soni: {o.Qty} dona",
            $"💰 Summa: {AuditService.Money(o.Total)} so'm",
            $"💳 To'lov: {PaymentLabel(o.PaymentMethod)}",
        };
        if (o.PaymentMethod == PayCash)
            lines.Add("\n💵 To'lovni kitobni olib ketayotganda markaz kassasiga topshirasiz.");
        lines.Add("\n📍 Kitobni markazdan olib ketishingiz mumkin. Savol bo'lsa administratorga yozing.");
        return string.Join("\n", lines);
    }

    public static string CustomerRejectedText(BookOrder o)
    {
        var reason = string.IsNullOrWhiteSpace(o.RejectReason) ? "Sabab ko'rsatilmagan." : o.RejectReason;
        return string.Join("\n", new[]
        {
            "❌ Buyurtmangiz rad etildi.",
            "",
            $"📕 {o.BookTitle} — {o.Qty} dona",
            $"💬 Sabab: {reason}",
            "",
            "Savolingiz bo'lsa «✍️ Adminga murojaat» tugmasi orqali yozing.",
        });
    }

    /// <summary>
    /// Mijozga (botga) — kitob QAYTARIB olindi. <paramref name="qty"/> — shu safar qaytarilgani,
    /// <paramref name="refund"/> — mijozga qaytarilgan pul (to'lanmagan nasiyada 0 bo'ladi,
    /// u yerda pul emas, QARZ kamayadi — matn ham shuni aytadi).
    /// </summary>
    public static string CustomerReturnedText(BookOrder o, int qty, decimal refund)
    {
        var lines = new List<string>
        {
            "↩️ Kitob qaytarib olindi.",
            "",
            $"📕 {o.BookTitle}",
            $"🔢 Qaytarildi: {qty} dona",
        };
        if (refund > 0) lines.Add($"💵 Qaytarilgan summa: {AuditService.Money(refund)} so'm");
        else if (IsCredit(o)) lines.Add($"💳 Qarzingiz {AuditService.Money(o.UnitPrice * qty)} so'mga kamaydi.");
        if (!string.IsNullOrWhiteSpace(o.ReturnReason)) lines.Add($"💬 Sabab: {o.ReturnReason}");
        if (NetQty(o) > 0) lines.Add($"\n📦 Sizda shu buyurtmadan {NetQty(o)} dona qoldi.");
        lines.Add("\nSavolingiz bo'lsa «✍️ Adminga murojaat» tugmasi orqali yozing.");
        return string.Join("\n", lines);
    }

    /// <summary>Adminlarga (Telegram) yuboriladigan "yangi buyurtma" xabari.</summary>
    public static string AdminNewOrderText(BookOrder o)
    {
        var lines = new List<string>
        {
            "📚 Yangi kitob buyurtmasi!",
            $"#️⃣ Buyurtma: #{o.Number}",
            $"👤 {(string.IsNullOrWhiteSpace(o.CustomerName) ? "Noma'lum" : o.CustomerName)}",
        };
        if (!string.IsNullOrWhiteSpace(o.Phone)) lines.Add($"📞 {o.Phone}");
        lines.Add($"📕 {o.BookTitle}");
        lines.Add($"🔢 Soni: {o.Qty} dona");
        lines.Add($"💰 Summa: {AuditService.Money(o.Total)} so'm");
        lines.Add($"💳 To'lov turi: {PaymentLabel(o.PaymentMethod)}");
        if (o.PaymentMethod == PayCard)
            lines.Add(string.IsNullOrWhiteSpace(o.ReceiptUrl) ? "🧾 Chek: yuborilmagan" : "🧾 Chek: yuborildi");
        lines.Add("");
        lines.Add("Tasdiqlash/rad etish: Admin panel → O'quv bo'limi → Kitoblar sotuvi.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Yangi buyurtma haqida ADMIN/SUPERADMIN'larga Telegram xabarnomasi (botga bog'langan
    /// <see cref="TelegramRegistration"/> orqali). Xato xabarnomani buyurtmani buzmasligi kerak —
    /// jim yutiladi (<see cref="LeadNotifier"/> bilan bir xil siyosat).
    /// </summary>
    public static async Task NotifyAdminsAsync(
        IAppDbContext db, TelegramService telegram, BookOrder order, CancellationToken ct = default)
    {
        try
        {
            if (!telegram.IsConfigured) return;
            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct);
            if (regs.Count == 0) return;

            var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
            var adminIds = (await db.Users
                    .Where(u => userIds.Contains(u.Id) && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
                    .Select(u => u.Id).ToListAsync(ct))
                .ToHashSet();
            if (adminIds.Count == 0) return;

            var text = AdminNewOrderText(order);
            var sent = new HashSet<long>();
            foreach (var r in regs)
            {
                if (r.UserId is null || !adminIds.Contains(r.UserId)) continue;
                if (!sent.Add(r.ChatId)) continue;
                await telegram.SendMessageAsync(r.ChatId, text, ct: ct);
            }
        }
        catch
        {
            // Xabarnoma buyurtmani buzmasligi kerak.
        }
    }
}
