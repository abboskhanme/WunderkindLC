using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using System.Net;
using System.Text;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KITOB SOTUVI — Telegram bot oqimi. Mijoz telefon raqamini yuborgach klaviaturada
/// «📚 Kitob sotib olish» tugmasi paydo bo'ladi. Oqim:
/// <list type="number">
///   <item>katalog (faol kitoblar, narx + qoldiq) → kitob tanlanadi;</item>
///   <item>soni tanlanadi (1–5 tugma yoki qo'lda yozib);</item>
///   <item>to'lov turi: <b>💵 Naqd</b> (kitobni olayotganda kassaga) yoki <b>💳 Karta orqali</b>;</item>
///   <item>karta tanlansa — admin belgilagan karta raqami/egasi ko'rsatiladi va <b>chek rasmi</b>
///     (skrinshot yoki PDF) so'raladi; rasm serverga ko'chiriladi (<c>/uploads/...</c>);</item>
///   <item>buyurtma <c>pending</c> holatda yoziladi, adminlarga Telegram xabarnomasi ketadi.</item>
/// </list>
/// Qoldiq buyurtma tushganda TEGILMAYDI — faqat admin tasdiqlaganda ayiriladi
/// (<see cref="BookSalesService"/>). Avtomatik to'lov tizimi (Click/Payme) ISHLATILMAYDI.
///
/// <para><see cref="OnlineTestBotService"/> bilan bir xil tuzilma: <c>Handles(data)</c> orqali
/// <c>TelegramBotService</c> callback'larni shu servisga yo'naltiradi, vaqtinchalik holat
/// <see cref="BookBotSession"/> da (chat bo'yicha bitta).</para>
/// </summary>
public class BookShopBotService(
    TelegramService telegram, IHostEnvironment env, ILogger<BookShopBotService> logger)
{
    /// <summary>Reply-klaviaturadagi tugma matni.</summary>
    public const string BooksButtonText = "📚 Kitob sotib olish";

    // ---------- callback_data prefikslari (Telegram cheklovi: 64 bayt) ----------
    /// <summary>Kitobni ochish: <c>kb:{bookId}</c></summary>
    public const string CbBook = "kb:";
    /// <summary>Soni tanlandi: <c>kq:{son}</c></summary>
    public const string CbQty = "kq:";
    /// <summary>"Boshqa son" — keyingi matn son deb qabul qilinadi.</summary>
    public const string CbQtyOther = "kqm";
    /// <summary>To'lov turi: naqd / karta.</summary>
    public const string CbPayCash = "kpc";
    public const string CbPayCard = "kpk";
    /// <summary>Naqd buyurtmani yakuniy tasdiqlash.</summary>
    public const string CbConfirm = "kconf";
    /// <summary>«🧾 Chekni yuborish» — mijozga chekni qanday yuborishni aniq tushuntiradi.
    /// Sessiya bosqichini O'ZGARTIRMAYDI: karta tanlangan zahoti chek allaqachon qabul qilinadi,
    /// bu tugma faqat yo'l-yo'riq (mijoz uzun matnni o'qimay tushunib qolmasin).</summary>
    public const string CbSendReceipt = "krcp";
    /// <summary>Katalogga qaytish / bekor qilish.</summary>
    public const string CbList = "klist";
    public const string CbCancel = "kcan";

    /// <summary>Katalogda ko'rsatiladigan maksimal kitob (bitta xabarga sig'ishi uchun).</summary>
    private const int CatalogLimit = 30;
    /// <summary>Bitta buyurtmada maksimal kitob soni (xato kirituvdan himoya).</summary>
    private const int MaxQty = 50;

    /// <summary>Berilgan callback shu servisga tegishlimi (TelegramBotService shunga qarab yo'naltiradi).</summary>
    public static bool Handles(string data) =>
        data.StartsWith(CbBook, StringComparison.Ordinal)
        || data.StartsWith(CbQty, StringComparison.Ordinal)
        || data is CbQtyOther or CbPayCash or CbPayCard or CbConfirm or CbSendReceipt or CbList or CbCancel;

    // ==================================================================================
    //  1) KATALOG
    // ==================================================================================

    /// <summary>«📚 Kitob sotib olish» — sotuvdagi kitoblar ro'yxati (narx + qoldiq).</summary>
    public async Task ShowCatalogAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is not null && !meta.BookSalesEnabled)
        {
            await telegram.SendMessageAsync(chatId,
                "ℹ️ Kitoblar sotuvi hozircha o'chirilgan. Administratorga murojaat qiling.", null, ct);
            return;
        }

        var books = await db.Books.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Title)
            .Take(CatalogLimit)
            .ToListAsync(ct);

        if (books.Count == 0)
        {
            await telegram.SendMessageAsync(chatId,
                "📭 Hozircha sotuvda kitob yo'q.\n\nKitoblar qo'shilganda shu tugma orqali buyurtma berish mumkin bo'ladi.",
                null, ct);
            return;
        }

        var text = new StringBuilder("📚 <b>Sotuvdagi kitoblar</b>\n\n");
        var rows = new List<object[]>();
        foreach (var b in books)
        {
            var stockTag = b.Stock > 0 ? $"omborda {b.Stock} dona" : "tugagan";
            text.Append($"📕 <b>{Esc(b.Title)}</b>\n");
            if (!string.IsNullOrWhiteSpace(b.Author)) text.Append($"     ✍️ {Esc(b.Author)}\n");
            text.Append($"     💰 {AuditService.Money(b.Price)} so'm · {stockTag}\n\n");
            if (b.Stock > 0)
                rows.Add(new object[] { new { text = $"🛒 {Short(b.Title, 30)}", callback_data = CbBook + b.Id } });
        }

        if (rows.Count == 0)
        {
            await telegram.SendMessageAsync(chatId,
                text + "😔 Barcha kitoblar hozircha tugagan. Yangi kelganda xabar beramiz.",
                null, ct, "HTML");
            return;
        }

        text.Append("Kerakli kitobni tanlang 👇");
        await telegram.SendMessageAsync(chatId, text.ToString(),
            new { inline_keyboard = rows.ToArray() }, ct, "HTML");
    }

    // ==================================================================================
    //  2) KITOB TANLANDI → SONI
    // ==================================================================================

    /// <summary>Kitob tanlandi: muqova + tavsif yuboriladi va soni so'raladi.</summary>
    public async Task OpenBookAsync(IAppDbContext db, long chatId, string data, CancellationToken ct)
    {
        var bookId = data[CbBook.Length..];
        var book = await db.Books.FirstOrDefaultAsync(x => x.Id == bookId, ct);
        if (book is null || !book.IsActive)
        {
            await telegram.SendMessageAsync(chatId, "Kitob topilmadi yoki sotuvdan olingan.", null, ct);
            return;
        }
        if (book.Stock <= 0)
        {
            await telegram.SendMessageAsync(chatId,
                $"😔 «{book.Title}» hozirda omborda tugagan. Keyinroq urinib ko'ring.", null, ct);
            return;
        }

        // Sessiyani (qayta) ochamiz — bitta chatda bitta faol savdo sessiyasi.
        var old = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (old is not null) db.BookBotSessions.Remove(old);
        db.BookBotSessions.Add(new BookBotSession
        {
            ChatId = chatId, Step = "qty", BookId = book.Id, Qty = 1,
            PaymentMethod = string.Empty, UpdatedAt = AppClock.Iso(),
        });
        await db.SaveChangesAsync(ct);

        var caption = new StringBuilder($"📕 <b>{Esc(book.Title)}</b>\n");
        if (!string.IsNullOrWhiteSpace(book.Author)) caption.Append($"✍️ {Esc(book.Author)}\n");
        caption.Append($"💰 {AuditService.Money(book.Price)} so'm\n");
        caption.Append($"📦 Omborda: {book.Stock} dona\n");
        if (!string.IsNullOrWhiteSpace(book.Description))
            caption.Append($"\n{Esc(Short(book.Description, 500))}\n");
        caption.Append("\n🔢 Nechta kitob kerak?");

        var keyboard = QtyKeyboard(book.Stock);

        // Muqova bo'lsa — rasm bilan (file_id keshlanadi, ikkinchi marta qayta yuklanmaydi).
        if (await SendCoverAsync(db, book, chatId, caption.ToString(), keyboard, ct)) return;
        await telegram.SendMessageAsync(chatId, caption.ToString(), keyboard, ct, "HTML");
    }

    /// <summary>Muqova rasmini yuboradi (keshlangan <c>file_id</c> bo'lsa qayta yuklamasdan).
    /// Muqova yo'q yoki yuborilmasa false — chaqiruvchi oddiy matn yuboradi.</summary>
    private async Task<bool> SendCoverAsync(
        IAppDbContext db, Book book, long chatId, string caption, object keyboard, CancellationToken ct)
    {
        try
        {
            if (book.CoverFileId.Length == 0 && book.CoverUrl.Length == 0) return false;

            string? newId;
            if (book.CoverFileId.Length > 0)
            {
                newId = await telegram.SendPhotoReturningIdAsync(
                    chatId, book.CoverFileId, null, "cover.jpg", caption, keyboard, ct);
            }
            else
            {
                var rel = book.CoverUrl.TrimStart('/');           // "uploads/xxx.jpg"
                var abs = Path.Combine(env.ContentRootPath, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs)) return false;
                var bytes = await File.ReadAllBytesAsync(abs, ct);
                newId = await telegram.SendPhotoReturningIdAsync(
                    chatId, null, bytes, Path.GetFileName(abs), caption, keyboard, ct);
            }

            if (newId is null) return false;
            if (newId.Length > 0 && newId != book.CoverFileId)
            {
                book.CoverFileId = newId;
                await db.SaveChangesAsync(ct);
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kitob muqovasini yuborishda xato: {Book}", book.Id);
            return false;
        }
    }

    /// <summary>Soni tugmasi bosildi (<c>kq:{son}</c>) — to'lov turini so'raymiz.</summary>
    public async Task SetQtyAsync(IAppDbContext db, long chatId, string data, CancellationToken ct)
    {
        var raw = data[CbQty.Length..];
        if (!int.TryParse(raw, out var qty)) return;
        await ApplyQtyAsync(db, chatId, qty, ct);
    }

    /// <summary>"Boshqa son" tugmasi — keyingi matn xabari son deb qabul qilinadi.</summary>
    public async Task AskQtyAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var session = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (session is null) { await ExpiredAsync(chatId, ct); return; }
        session.Step = "qty";
        session.UpdatedAt = AppClock.Iso();
        await db.SaveChangesAsync(ct);
        await telegram.SendMessageAsync(chatId,
            "🔢 Nechta kitob kerakligini raqam bilan yozib yuboring (masalan: 7).", null, ct);
    }

    /// <summary>Sonni qo'llaydi va to'lov turini so'raydi. Qoldiqdan oshsa — ogohlantiradi.</summary>
    private async Task ApplyQtyAsync(IAppDbContext db, long chatId, int qty, CancellationToken ct)
    {
        var session = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (session is null) { await ExpiredAsync(chatId, ct); return; }

        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == session.BookId, ct);
        if (book is null)
        {
            db.BookBotSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            await telegram.SendMessageAsync(chatId, "Kitob topilmadi. Katalogni qaytadan oching.", null, ct);
            return;
        }

        if (qty < 1 || qty > MaxQty)
        {
            await telegram.SendMessageAsync(chatId,
                $"🔢 Son 1 dan {MaxQty} gacha bo'lishi kerak. Qaytadan kiriting.", null, ct);
            return;
        }
        if (qty > book.Stock)
        {
            await telegram.SendMessageAsync(chatId,
                $"📦 Omborda faqat {book.Stock} dona bor. Kamroq son kiriting.", QtyKeyboard(book.Stock), ct);
            return;
        }

        session.Qty = qty;
        session.Step = "pay";
        session.UpdatedAt = AppClock.Iso();
        await db.SaveChangesAsync(ct);

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var cardAvailable = !string.IsNullOrWhiteSpace(meta?.BookCardNumber);
        var total = book.Price * qty;

        var rows = new List<object[]>();
        if (cardAvailable)
            rows.Add(new object[] { new { text = "💳 Karta orqali", callback_data = CbPayCard } });
        rows.Add(new object[] { new { text = "💵 Naqd pulda", callback_data = CbPayCash } });
        rows.Add(new object[] { new { text = "🔄 Boshqa kitob", callback_data = CbList },
                                new { text = "❌ Bekor qilish", callback_data = CbCancel } });

        await telegram.SendMessageAsync(chatId,
            $"🧾 <b>Buyurtma</b>\n📕 {Esc(book.Title)}\n🔢 {qty} dona × {AuditService.Money(book.Price)} so'm\n"
            + $"💰 <b>Jami: {AuditService.Money(total)} so'm</b>\n\nTo'lov turini tanlang 👇",
            new { inline_keyboard = rows.ToArray() }, ct, "HTML");
    }

    /// <summary>Matn xabari savdo sessiyasiga tegishlimi (soni kutilyaptimi) — TelegramBotService
    /// oddiy matn oqimidan OLDIN shuni sinaydi. Qabul qilinsa true.</summary>
    public async Task<bool> HandleTextAsync(IAppDbContext db, long chatId, string text, CancellationToken ct)
    {
        var session = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (session is null) return false;

        if (session.Step == "qty")
        {
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length is 0 or > 4) return false;   // songa o'xshamaydi — oddiy oqim davom etsin
            if (!int.TryParse(digits, out var qty)) return false;
            await ApplyQtyAsync(db, chatId, qty, ct);
            return true;
        }

        if (session.Step == "receipt")
        {
            await telegram.SendMessageAsync(chatId,
                "🧾 To'lov chekining RASMINI (skrinshot) yoki PDF faylini yuboring — matn emas.\n"
                + "Bekor qilish uchun /start bosing.", null, ct);
            return true;
        }

        return false;
    }

    // ==================================================================================
    //  3) TO'LOV TURI
    // ==================================================================================

    /// <summary>Naqd to'lov tanlandi — yakuniy tasdiqlash so'raladi (to'lov kitobni olayotganda).</summary>
    public async Task ChooseCashAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, book) = await SessionWithBookAsync(db, chatId, ct);
        if (session is null || book is null) { await ExpiredAsync(chatId, ct); return; }

        session.PaymentMethod = BookSalesService.PayCash;
        session.Step = "confirm";
        session.UpdatedAt = AppClock.Iso();
        await db.SaveChangesAsync(ct);

        await telegram.SendMessageAsync(chatId,
            $"💵 <b>Naqd to'lov</b>\n\n📕 {Esc(book.Title)}\n🔢 {session.Qty} dona\n"
            + $"💰 Jami: <b>{AuditService.Money(book.Price * session.Qty)} so'm</b>\n\n"
            + "To'lovni kitobni olib ketayotganda markaz kassasiga topshirasiz.\n"
            + "Buyurtmani yuboraymi?",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✅ Ha, buyurtma beraman", callback_data = CbConfirm } },
                    new object[] { new { text = "❌ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");
    }

    /// <summary>Karta orqali to'lov tanlandi — rekvizitlar ko'rsatiladi va chek so'raladi.</summary>
    public async Task ChooseCardAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, book) = await SessionWithBookAsync(db, chatId, ct);
        if (session is null || book is null) { await ExpiredAsync(chatId, ct); return; }

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(meta?.BookCardNumber))
        {
            await telegram.SendMessageAsync(chatId,
                "💳 Karta orqali to'lov hozircha sozlanmagan. «💵 Naqd pulda» variantini tanlang yoki "
                + "administratorga murojaat qiling.", null, ct);
            return;
        }

        session.PaymentMethod = BookSalesService.PayCard;
        session.Step = "receipt";
        session.UpdatedAt = AppClock.Iso();
        await db.SaveChangesAsync(ct);

        var total = book.Price * session.Qty;
        var lines = new StringBuilder();
        lines.Append("💳 <b>Karta orqali to'lov</b>\n\n");
        lines.Append($"📕 {Esc(book.Title)} — {session.Qty} dona\n");
        lines.Append($"💰 To'lash kerak: <b>{AuditService.Money(total)} so'm</b>\n\n");
        lines.Append($"💳 Karta raqami:\n<code>{Esc(meta.BookCardNumber)}</code>\n");
        if (!string.IsNullOrWhiteSpace(meta.BookCardHolder))
            lines.Append($"👤 Karta egasi: <b>{Esc(meta.BookCardHolder)}</b>\n");
        if (!string.IsNullOrWhiteSpace(meta.BookPaymentNote))
            lines.Append($"\nℹ️ {Esc(meta.BookPaymentNote)}\n");
        lines.Append("\n🧾 To'lovni amalga oshirgach, quyidagi tugmani bosing va "
                     + "<b>chek rasmini (skrinshot) yoki PDF faylini</b> shu yerga yuboring.");

        await telegram.SendMessageAsync(chatId, lines.ToString(),
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "🧾 Chekni yuborish", callback_data = CbSendReceipt } },
                    new object[] { new { text = "❌ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");
    }

    /// <summary>«🧾 Chekni yuborish» bosildi — mijozga chekni qanday yuborishni aniq ko'rsatamiz.
    /// Bosqich allaqachon <c>receipt</c> (karta tanlanganda o'rnatilgan), shuning uchun tugmani
    /// bosmasdan yuborilgan chek ham QABUL QILINADI — tugma faqat yo'riqnoma.</summary>
    public async Task PromptReceiptAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, book) = await SessionWithBookAsync(db, chatId, ct);
        if (session is null || book is null) { await ExpiredAsync(chatId, ct); return; }
        if (session.Step != "receipt")
        {
            await telegram.SendMessageAsync(chatId,
                "Bu buyurtmada chek talab qilinmaydi.", null, ct);
            return;
        }

        var total = book.Price * session.Qty;
        await telegram.SendMessageAsync(chatId,
            "🧾 <b>Chekni yuborish</b>\n\n"
            + $"📕 {Esc(book.Title)} — {session.Qty} dona\n"
            + $"💰 To'lov summasi: <b>{AuditService.Money(total)} so'm</b>\n\n"
            + "📸 Endi <b>chek rasmini</b> (skrinshot) yoki <b>PDF faylini</b> shu chatga yuboring.\n"
            + "Telegramdagi 📎 tugmasi orqali galereyadan rasm tanlang yoki suratga oling.\n\n"
            + "Chek kelgach buyurtma administratorga yuboriladi — u tekshirib tasdiqlaydi.",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "❌ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");
    }

    // ==================================================================================
    //  4) CHEK (rasm/PDF) VA BUYURTMANI YAKUNLASH
    // ==================================================================================

    /// <summary>Chat chekni kutyaptimi (karta to'lovi tanlangan) — TelegramBotService rasm/hujjat
    /// kelganda shuni tekshiradi.</summary>
    public async Task<bool> AwaitingReceiptAsync(IAppDbContext db, long chatId, CancellationToken ct) =>
        await db.BookBotSessions.AnyAsync(s => s.ChatId == chatId && s.Step == "receipt", ct);

    /// <summary>
    /// Mijoz yuborgan chek faylini (Telegram <paramref name="fileId"/>) serverga ko'chiradi va
    /// buyurtmani yaratadi. <paramref name="fileName"/> — hujjat bo'lsa asl nomi (kengaytma uchun).
    /// </summary>
    public async Task HandleReceiptAsync(
        IAppDbContext db, long chatId, string fileId, string? fileName, CancellationToken ct)
    {
        var (session, book) = await SessionWithBookAsync(db, chatId, ct);
        if (session is null || book is null) { await ExpiredAsync(chatId, ct); return; }
        if (session.Step != "receipt") return;

        var url = await SaveTelegramFileAsync(fileId, fileName, ct);
        if (url is null)
        {
            await telegram.SendMessageAsync(chatId,
                "⚠️ Faylni yuklab olishda xato bo'ldi. Iltimos, chekni qaytadan yuboring.", null, ct);
            return;
        }

        await CreateOrderAsync(db, session, book, chatId, url, ct);
    }

    /// <summary>Naqd buyurtmani yakuniy tasdiqlash (chek talab qilinmaydi).</summary>
    public async Task ConfirmCashAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, book) = await SessionWithBookAsync(db, chatId, ct);
        if (session is null || book is null) { await ExpiredAsync(chatId, ct); return; }
        if (session.PaymentMethod != BookSalesService.PayCash) return;
        await CreateOrderAsync(db, session, book, chatId, string.Empty, ct);
    }

    /// <summary>Buyurtmani <c>pending</c> holatda yozadi, sessiyani yopadi, mijozga tasdiq va
    /// adminlarga xabarnoma yuboradi. Qoldiq bu yerda TEGILMAYDI (admin tasdiqlaganda ayiriladi).</summary>
    private async Task CreateOrderAsync(
        IAppDbContext db, BookBotSession session, Book book, long chatId, string receiptUrl, CancellationToken ct)
    {
        // Qoldiq buyurtma berish vaqtida ham tekshiriladi (boshqa mijoz oldin sotib olgan bo'lishi mumkin).
        if (book.Stock < session.Qty)
        {
            db.BookBotSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            await telegram.SendMessageAsync(chatId,
                $"😔 Afsus, «{book.Title}» omborda {book.Stock} dona qoldi — buyurtma bekor qilindi. "
                + "Katalogdan qaytadan tanlang.", null, ct);
            return;
        }

        var botUser = await db.BotUsers.AsNoTracking().FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
        var phone = botUser?.Phone ?? "";
        // Telefon markazdagi o'quvchiga mos kelsa — buyurtmani shu o'quvchiga teglaymiz (hisobotda ko'rinadi).
        string? studentId = null;
        if (phone.Length >= 7)
        {
            var key = PhoneUtil.Key(phone);
            studentId = (await db.Students.AsNoTracking()
                    .Select(s => new { s.Id, s.Phone, s.ParentPhone }).ToListAsync(ct))
                .FirstOrDefault(s => PhoneUtil.Key(s.ParentPhone) == key || PhoneUtil.Key(s.Phone) == key)?.Id;
        }

        var order = new BookOrder
        {
            Number = await BookSalesService.NextOrderNumberAsync(db, ct),
            ChatId = chatId,
            CustomerName = botUser?.Name ?? "",
            Phone = phone,
            StudentId = studentId,
            BookId = book.Id,
            BookTitle = book.Title,
            UnitPrice = book.Price,
            Qty = session.Qty,
            Total = book.Price * session.Qty,
            PaymentMethod = session.PaymentMethod.Length > 0 ? session.PaymentMethod : BookSalesService.PayCash,
            ReceiptUrl = receiptUrl,
            Status = BookSalesService.StatusPending,
        };
        db.BookOrders.Add(order);
        db.BookBotSessions.Remove(session);
        await db.SaveChangesAsync(ct);

        var payLine = order.PaymentMethod == BookSalesService.PayCard
            ? "🧾 Chekingiz administratorga yuborildi."
            : "💵 To'lovni kitobni olayotganda kassaga topshirasiz.";
        await telegram.SendMessageAsync(chatId,
            $"✅ <b>Buyurtma qabul qilindi!</b>\n\n#️⃣ Buyurtma: <b>#{order.Number}</b>\n"
            + $"📕 {Esc(order.BookTitle)}\n🔢 {order.Qty} dona\n"
            + $"💰 Jami: <b>{AuditService.Money(order.Total)} so'm</b>\n"
            + $"💳 To'lov: {BookSalesService.PaymentLabel(order.PaymentMethod)}\n\n"
            + payLine + "\n⏳ Administrator tekshirgach javob yuboriladi.",
            null, ct, "HTML");

        await BookSalesService.NotifyAdminsAsync(db, telegram, order, ct);
    }

    /// <summary>Savdo sessiyasini bekor qiladi.</summary>
    public async Task CancelAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var session = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (session is not null)
        {
            db.BookBotSessions.Remove(session);
            await db.SaveChangesAsync(ct);
        }
        await telegram.SendMessageAsync(chatId,
            "❌ Buyurtma bekor qilindi. Yangi buyurtma uchun «" + BooksButtonText + "» tugmasini bosing.",
            null, ct);
    }

    // ==================================================================================
    //  Yordamchilar
    // ==================================================================================

    private async Task<(BookBotSession? Session, Book? Book)> SessionWithBookAsync(
        IAppDbContext db, long chatId, CancellationToken ct)
    {
        var session = await db.BookBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (session is null) return (null, null);
        var book = await db.Books.FirstOrDefaultAsync(x => x.Id == session.BookId, ct);
        return (session, book);
    }

    private Task ExpiredAsync(long chatId, CancellationToken ct) =>
        telegram.SendMessageAsync(chatId,
            "⌛️ Buyurtma sessiyasi topilmadi. «" + BooksButtonText + "» tugmasi orqali qaytadan boshlang.",
            null, ct);

    /// <summary>Telegram'dagi faylni <c>uploads/</c> ga ko'chiradi va `/uploads/...` URL'ini qaytaradi.
    /// Kengaytma allowlist'dan o'tmasa yoki yuklab bo'lmasa — null.</summary>
    private async Task<string?> SaveTelegramFileAsync(string fileId, string? fileName, CancellationToken ct)
    {
        try
        {
            var path = await telegram.GetFilePathAsync(fileId, ct);
            if (path is null) return null;

            // Kengaytma: avval Telegram bergan yo'ldan, bo'lmasa hujjat nomidan; ruxsat etilmasa rad.
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(fileName)) ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            ext = ext.ToLowerInvariant();
            if (!UploadGuard.AllowedExtensions.Contains(ext)) return null;

            var bytes = await telegram.DownloadFileAsync(path, ct);
            if (bytes is null || bytes.Length == 0 || bytes.Length > UploadGuard.MaxBytes) return null;

            var dir = Path.Combine(env.ContentRootPath, "uploads");
            Directory.CreateDirectory(dir);
            var stored = $"{Guid.NewGuid():N}{ext}";
            await File.WriteAllBytesAsync(Path.Combine(dir, stored), bytes, ct);
            return $"/uploads/{stored}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kitob cheki faylini saqlashda xato: chat {Chat}", fileId);
            return null;
        }
    }

    /// <summary>Soni tugmalari (1..5, qoldiqdan oshmaydi) + "boshqa son" + bekor.</summary>
    private static object QtyKeyboard(int stock)
    {
        var max = Math.Min(5, Math.Max(1, stock));
        var qtyRow = Enumerable.Range(1, max)
            .Select(n => (object)new { text = n.ToString(), callback_data = CbQty + n })
            .ToArray();
        return new
        {
            inline_keyboard = new object[][]
            {
                qtyRow,
                new object[] { new { text = "✏️ Boshqa son", callback_data = CbQtyOther } },
                new object[] { new { text = "🔄 Boshqa kitob", callback_data = CbList },
                               new { text = "❌ Bekor qilish", callback_data = CbCancel } },
            },
        };
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Short(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";
}
