using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Back office: contracts, book sales, suggestions/complaints, vacancies and applications, onboarding.
/// No file URLs (contract PDF/DOCX, book covers and receipts, CVs, feedback photos), no Telegram ids.
/// </summary>
[McpServerToolType]
public sealed class BackOfficeTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Contract), typeof(Book), typeof(BookOrder), typeof(Student), typeof(Feedback), typeof(AuditLog),
        typeof(Vacancy), typeof(JobApplication), typeof(EmployeeOnboarding), typeof(EmployeeOnboardingTask),
    ];

    private const int MaxText = 500;

    private const string MoneyNote = "Summalar ko'rsatilmadi: moliya ruxsati yo'q.";

    // =============================================================================================
    //  contracts_list
    // =============================================================================================

    [McpServerTool(Name = "contracts_list", Title = "Shartnomalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tuzilgan shartnomalar tarixi (eng yangisi birinchi): raqami, sanasi, kimga (o'quvchi / xodim — id va ism), "
        + "andoza nomi, qanday berilgani (yuklab olingan yoki Telegram orqali yuborilgan va yetkazildimi), tayyor PDF "
        + "biriktirilganmi va ilovada ko'rinadimi. Filtrlar: oluvchi turi, o'quvchi yoki xodim id, sana oralig'i, "
        + "qidiruv (oluvchi/andoza nomi yoki raqam). Fayl manzillari berilmaydi. Sahifalangan. "
        + "Contracts issued, newest first, without file links.")]
    public async Task<string> ContractsList(
        [Description("Oluvchi turi: parent (o'quvchi/ota-ona) | staff (xodim)")] string? target = null,
        [Description("O'quvchi id — shu o'quvchiga tuzilgan shartnomalar")] string? studentId = null,
        [Description("O'qituvchi/xodim id — shu xodimga tuzilgan shartnomalar")] string? teacherId = null,
        [Description("Sana oralig'i boshi (YYYY-MM-DD)")] string? from = null,
        [Description("Sana oralig'i oxiri (YYYY-MM-DD)")] string? to = null,
        [Description("Oluvchi yoki andoza nomi bo'lagi, yoki shartnoma raqami")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Contracts);
        var range = OptionalRange(from, to);
        var db = t.Db;

        var q = db.Contracts.AsQueryable();
        switch ((target ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case "parent": q = q.Where(c => c.Target == "parent"); break;
            case "staff": q = q.Where(c => c.Target == "staff"); break;
            default: throw new McpException("«target»: parent | staff.");
        }
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var sid = studentId.Trim();
            q = q.Where(c => c.Target == "parent" && c.RecipientKey == sid);
        }
        if (!string.IsNullOrWhiteSpace(teacherId))
        {
            var tid = teacherId.Trim();
            q = q.Where(c => c.Target == "staff" && c.RecipientKey == tid);
        }
        if (range is { } r)
        {
            var f = r.From.ToDateTime(TimeOnly.MinValue);
            var tt = r.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(c => c.SentAt >= f && c.SentAt < tt);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = int.TryParse(term.TrimStart('№', '#', ' '), out var number)
                ? q.Where(c => c.Number == number || c.RecipientName.ToLower().Contains(term) || c.TemplateName.ToLower().Contains(term))
                : q.Where(c => c.RecipientName.ToLower().Contains(term) || c.TemplateName.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(c => c.Number).ThenBy(c => c.Id).Skip((p - 1) * size).Take(size)
            .Select(c => new
            {
                c.Id, c.Number, c.FileName, c.Target, c.RecipientKey, c.RecipientName, c.TemplateName,
                c.SentAt, c.Status, c.Delivered, c.Visible, HasPdf = c.PdfUrl != "",
            }).ToListAsync(ct);

        var items = rows.Select(c => new
        {
            c.Id, c.Number,
            title = string.IsNullOrWhiteSpace(c.FileName) ? $"Shartnoma № {c.Number}" : c.FileName,
            target = c.Target,
            // Old parent contracts were keyed by the parent's phone ("p:…"/"n:…") — not a student id.
            studentId = c.Target == "parent" && !IsLegacyParentKey(c.RecipientKey) ? c.RecipientKey : null,
            teacherId = c.Target == "staff" ? c.RecipientKey : null,
            recipientName = c.RecipientName,
            templateName = c.TemplateName,
            date = c.SentAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            channel = c.Status == "downloaded" ? "download" : "telegram",
            telegramDelivered = c.Status == "downloaded" ? (bool?)null : c.Delivered,
            c.Status,
            pdfAttached = c.HasPdf,
            visibleInApp = c.Visible,
        }).ToList();

        return t.Json(new { total, page = p, pageSize = size, items }, items.Count);
    }

    private static bool IsLegacyParentKey(string key) =>
        key.StartsWith("p:", StringComparison.Ordinal) || key.StartsWith("n:", StringComparison.Ordinal);

    // =============================================================================================
    //  books_stock / book_orders
    // =============================================================================================

    [McpServerTool(Name = "books_stock", Title = "Kitoblar ombori",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sotuvdagi kitoblar: nomi, muallifi, narxi, ombordagi qoldiq, sotuvdami (faol), sotilgan dona (SOF — "
        + "qaytarilgani ayirilgan), tasdiq kutayotgan dona. Sotuv summasi faqat moliya ruxsati bo'lsa. "
        + "Filtrlar: faqat faollar, nom/muallif bo'yicha qidiruv. Sahifalangan. "
        + "Books with price, stock and net sold quantity.")]
    public async Task<string> BooksStock(
        [Description("true — faqat sotuvdagi (faol) kitoblar")] bool? activeOnly = null,
        [Description("Kitob nomi yoki muallif bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Books);
        var finance = t.CanReadFinance;
        var db = t.Db;

        var q = db.Books.AsQueryable();
        if (activeOnly == true) q = q.Where(b => b.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(b => b.Title.ToLower().Contains(term) || b.Author.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var totalStock = await q.SumAsync(b => b.Stock, ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var books = await q.OrderBy(b => b.Title).ThenBy(b => b.Id).Skip((p - 1) * size).Take(size)
            .Select(b => new { b.Id, b.Title, b.Author, b.Price, b.Stock, b.IsActive, b.CreatedAt })
            .ToListAsync(ct);

        // Sales per book in ONE grouped query (the panel's BooksController.ToDtosAsync): NET of returns.
        var ids = books.Select(b => b.Id).ToList();
        var stats = await db.BookOrders
            .Where(o => ids.Contains(o.BookId)
                        && (o.Status == BookSalesService.StatusApproved || o.Status == BookSalesService.StatusPending))
            .GroupBy(o => new { o.BookId, o.Status })
            .Select(g => new { g.Key.BookId, g.Key.Status, Qty = g.Sum(x => x.Qty - x.ReturnedQty) })
            .ToListAsync(ct);
        // Money only with finance access. Summed on the client: decimal aggregates do not translate on every
        // provider; the rows are 3 numbers per approved sale of the books on this page.
        var amountByBook = new Dictionary<string, decimal>();
        if (finance)
            foreach (var o in await db.BookOrders
                         .Where(o => ids.Contains(o.BookId) && o.Status == BookSalesService.StatusApproved)
                         .Select(o => new { o.BookId, o.Total, o.UnitPrice, o.ReturnedQty }).ToListAsync(ct))
                amountByBook[o.BookId] = amountByBook.GetValueOrDefault(o.BookId) + o.Total - o.UnitPrice * o.ReturnedQty;

        var items = books.Select(b =>
        {
            var sold = stats.FirstOrDefault(s => s.BookId == b.Id && s.Status == BookSalesService.StatusApproved);
            var pending = stats.FirstOrDefault(s => s.BookId == b.Id && s.Status == BookSalesService.StatusPending);
            return new
            {
                b.Id, b.Title, b.Author, price = b.Price, stock = b.Stock, isActive = b.IsActive,
                soldQty = sold?.Qty ?? 0,
                soldAmount = finance ? amountByBook.GetValueOrDefault(b.Id) : (decimal?)null,
                pendingQty = pending?.Qty ?? 0,
                createdAt = b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            };
        }).ToList();

        return t.Json(new
        {
            total, totalStock, page = p, pageSize = size,
            note = finance ? null : MoneyNote,
            items,
        }, items.Count);
    }

    [McpServerTool(Name = "book_orders", Title = "Kitob buyurtmalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kitob buyurtmalari va sotuvlari (eng yangisi birinchi): raqami, sanasi, xaridor ismi va telefoni, "
        + "o'quvchi bo'lsa — uning ismi, kitob, dona (qaytarilgani bilan), to'lov turi (naqd / karta / nasiya), "
        + "holati, to'langanmi, olib ketilishi kutilyaptimi, nasiya muddati va kechikkanmi. Summalar faqat moliya ruxsati bo'lsa. "
        + "Filtrlar: holat (pending / approved / rejected / returned — qaytarilganlar / awaiting — olib ketish va to'lov "
        + "kutilmoqda), kitob, to'lov turi, sana oralig'i, qidiruv (xaridor ismi, telefon, kitob nomi, raqam). "
        + "Chek rasmlari va Telegram ma'lumotlari berilmaydi. Sahifalangan. "
        + "Book orders with status, payment and pickup state.")]
    public async Task<string> BookOrders(
        [Description("pending | approved | rejected | returned | awaiting")] string? status = null,
        [Description("Kitob id (books_stock natijasidan)")] string? bookId = null,
        [Description("To'lov turi: cash | card | credit")] string? method = null,
        [Description("Sana oralig'i boshi (YYYY-MM-DD)")] string? from = null,
        [Description("Sana oralig'i oxiri (YYYY-MM-DD)")] string? to = null,
        [Description("Xaridor ismi, telefon (kamida 4 raqam), kitob nomi yoki buyurtma raqami")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Books);
        var finance = t.CanReadFinance;
        var range = OptionalRange(from, to);
        var db = t.Db;

        // Same filters as the panel's BooksController.FilteredOrdersAsync / ApplySearch.
        var q = db.BookOrders.AsQueryable();
        switch ((status ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case "returned": q = q.Where(o => o.ReturnedQty > 0); break;
            case "awaiting":
                q = q.Where(o => o.PayAtPickup && o.Status == BookSalesService.StatusApproved && o.PaidAt == null);
                break;
            case BookSalesService.StatusPending: q = q.Where(o => o.Status == BookSalesService.StatusPending); break;
            case BookSalesService.StatusApproved: q = q.Where(o => o.Status == BookSalesService.StatusApproved); break;
            case BookSalesService.StatusRejected: q = q.Where(o => o.Status == BookSalesService.StatusRejected); break;
            default: throw new McpException("«status»: pending | approved | rejected | returned | awaiting.");
        }
        if (!string.IsNullOrWhiteSpace(bookId))
        {
            var bid = bookId.Trim();
            q = q.Where(o => o.BookId == bid);
        }
        switch ((method ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case BookSalesService.PayCash: q = q.Where(o => o.PaymentMethod == BookSalesService.PayCash); break;
            case BookSalesService.PayCard: q = q.Where(o => o.PaymentMethod == BookSalesService.PayCard); break;
            case BookSalesService.PayCredit: q = q.Where(o => o.PaymentMethod == BookSalesService.PayCredit); break;
            default: throw new McpException("«method»: cash | card | credit.");
        }
        if (range is { } r)
        {
            var f = r.From.ToDateTime(TimeOnly.MinValue);
            var tt = r.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(o => o.CreatedAt >= f && o.CreatedAt < tt);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = PhoneUtil.DigitsOnly(term);
            q = digits.Length >= 4
                ? q.Where(o => o.Phone.Contains(digits) || o.CustomerName.ToLower().Contains(term))
                : q.Where(o => o.CustomerName.ToLower().Contains(term) || o.BookTitle.ToLower().Contains(term)
                               || o.Number.ToString().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        // Explicit projection: no ChatId, no receipt URL (only whether one is attached).
        var page1 = await q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).ThenBy(o => o.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(o => new
            {
                o.Id, o.Number, o.CustomerName, o.Phone, o.StudentId, o.BookId, o.BookTitle, o.UnitPrice, o.Qty,
                o.Total, o.PaymentMethod, o.Status, o.RejectReason, o.CreatedAt, o.DecidedAt, o.DecidedBy, o.Source,
                o.DueDate, o.PaidAt, o.SettledMethod, o.PayAtPickup, o.ReturnedQty, o.ReturnedAt, o.ReturnReason,
                o.RefundedAmount, HasReceipt = o.ReceiptUrl != "",
            }).ToListAsync(ct);
        var hasReceipt = page1.Where(o => o.HasReceipt).Select(o => o.Id).ToHashSet();
        // The pure helpers (IsPaid / NetTotal / IsOverdue) take the entity — rebuilt in memory from the projection.
        var rows = page1.Select(o => new BookOrder
        {
            Id = o.Id, Number = o.Number, CustomerName = o.CustomerName, Phone = o.Phone, StudentId = o.StudentId,
            BookId = o.BookId, BookTitle = o.BookTitle, UnitPrice = o.UnitPrice, Qty = o.Qty, Total = o.Total,
            PaymentMethod = o.PaymentMethod, Status = o.Status, RejectReason = o.RejectReason ?? "",
            CreatedAt = o.CreatedAt, DecidedAt = o.DecidedAt, DecidedBy = o.DecidedBy ?? "", Source = o.Source,
            DueDate = o.DueDate, PaidAt = o.PaidAt, SettledMethod = o.SettledMethod, PayAtPickup = o.PayAtPickup,
            ReturnedQty = o.ReturnedQty, ReturnedAt = o.ReturnedAt, ReturnReason = o.ReturnReason ?? "",
            RefundedAmount = o.RefundedAmount,
        }).ToList();

        var studentIds = rows.Where(o => !string.IsNullOrEmpty(o.StudentId)).Select(o => o.StudentId!).Distinct().ToList();
        var names = studentIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Students.Where(s => studentIds.Contains(s.Id))
                .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var now = AppClock.Now;
        var items = rows.Select(o => new
        {
            o.Id, o.Number,
            date = o.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            customerName = o.CustomerName, phone = o.Phone,
            studentId = string.IsNullOrEmpty(o.StudentId) ? null : o.StudentId,
            studentName = o.StudentId is null ? null : names.GetValueOrDefault(o.StudentId),
            book = new { id = o.BookId, title = o.BookTitle },
            unitPrice = o.UnitPrice,
            quantity = o.Qty, returnedQty = o.ReturnedQty, netQty = BookSalesService.NetQty(o),
            amount = finance ? o.Total : (decimal?)null,
            netAmount = finance ? BookSalesService.NetTotal(o) : (decimal?)null,
            refundedAmount = finance ? o.RefundedAmount : (decimal?)null,
            paymentMethod = o.PaymentMethod,
            settledMethod = o.SettledMethod,
            source = o.Source,
            status = o.Status,
            rejectReason = o.RejectReason.Length == 0 ? null : o.RejectReason,
            decidedAt = o.DecidedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            decidedBy = o.DecidedBy.Length == 0 ? null : o.DecidedBy,
            paid = BookSalesService.IsPaid(o),
            paidAt = o.PaidAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            // Approved bot cash order that the customer has not collected (and paid for) yet — the panel's «awaiting».
            awaitingPickup = o.PayAtPickup && o.Status == BookSalesService.StatusApproved && o.PaidAt is null,
            dueDate = o.DueDate?.ToString("yyyy-MM-dd"),
            overdue = BookSalesService.IsOverdue(o, now),
            returnedAt = o.ReturnedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            returnReason = o.ReturnReason.Length == 0 ? null : o.ReturnReason,
            receiptAttached = hasReceipt.Contains(o.Id),
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            note = finance ? null : MoneyNote,
            items,
        }, items.Count);
    }

    // =============================================================================================
    //  feedback_list
    // =============================================================================================

    [McpServerTool(Name = "feedback_list", Title = "Taklif va shikoyatlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ota-onalar va o'qituvchilar ilovadan yuborgan taklif va shikoyatlar (eng yangisi birinchi): sanasi, turi, "
        + "matni (500 belgigacha), yuboruvchi (ismi, roli), qaysi o'quvchi nomidan, hal qilinganmi. Kim va qachon hal qilgani "
        + "«O'zgarishlar tarixi» ruxsati bo'lsa ko'rsatiladi. Filtrlar: tur (suggestion / complaint), hal qilinganmi, "
        + "sana oralig'i. Biriktirilgan rasm manzili berilmaydi. Sahifalangan. "
        + "Suggestions and complaints from parents and teachers.")]
    public async Task<string> FeedbackList(
        [Description("Tur: suggestion (taklif) | complaint (shikoyat)")] string? kind = null,
        [Description("true — faqat hal qilinganlar, false — faqat ochiqlar")] bool? resolved = null,
        [Description("Sana oralig'i boshi (YYYY-MM-DD)")] string? from = null,
        [Description("Sana oralig'i oxiri (YYYY-MM-DD)")] string? to = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Feedback);
        var canAudit = t.CanReadArea(McpAreas.Audit);
        var range = OptionalRange(from, to);
        var db = t.Db;

        var q = db.Feedbacks.AsQueryable();
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case "suggestion": q = q.Where(f => f.Type == "suggestion"); break;
            case "complaint": q = q.Where(f => f.Type == "complaint"); break;
            default: throw new McpException("«kind»: suggestion | complaint.");
        }
        if (resolved is { } res) q = res ? q.Where(f => f.Status == "resolved") : q.Where(f => f.Status != "resolved");
        if (range is { } r)
        {
            var f0 = r.From.ToDateTime(TimeOnly.MinValue);
            var tt = r.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(f => f.CreatedAt >= f0 && f.CreatedAt < tt);
        }

        var total = await q.CountAsync(ct);
        var open = await q.CountAsync(f => f.Status != "resolved", ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id).Skip((p - 1) * size).Take(size)
            .Select(f => new
            {
                f.Id, f.StudentId, f.ParentName, f.Type, f.Text, f.SenderRole, f.SenderName, f.TeacherId,
                f.CreatedAt, f.Status, HasImage = f.ImageUrl != null && f.ImageUrl != "",
            }).ToListAsync(ct);

        var studentIds = rows.Select(f => f.StudentId).Where(id => id != "").Distinct().ToList();
        var students = studentIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Students.Where(s => studentIds.Contains(s.Id))
                .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        // Who / when resolved lives only in the change history (the panel writes it there) — gated like the history.
        var resolvedBy = new Dictionary<string, (string At, string? By)>();
        var resolvedIds = rows.Where(f => f.Status == "resolved").Select(f => f.Id).ToList();
        if (canAudit && resolvedIds.Count > 0)
        {
            var logs = await db.AuditLogs
                .Where(a => a.EntityType == "Feedback" && a.Action == "update" && resolvedIds.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Timestamp, a.ActorName, a.Summary })
                .ToListAsync(ct);
            foreach (var g in logs.Where(a => a.Summary.Contains("hal qilindi")).GroupBy(a => a.EntityId))
            {
                var last = g.OrderByDescending(a => a.Timestamp, StringComparer.Ordinal).First();
                resolvedBy[g.Key] = (last.Timestamp, last.ActorName);
            }
        }

        var items = rows.Select(f =>
        {
            var role = string.IsNullOrEmpty(f.SenderRole) ? "parent" : f.SenderRole;
            var isResolved = f.Status == "resolved";
            var who = resolvedBy.TryGetValue(f.Id, out var rb) ? rb : default;
            return new
            {
                f.Id,
                date = f.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                kind = f.Type,
                text = Truncate(f.Text),
                author = new
                {
                    name = !string.IsNullOrWhiteSpace(f.SenderName) ? f.SenderName : f.ParentName,
                    role,
                    teacherId = string.IsNullOrEmpty(f.TeacherId) ? null : f.TeacherId,
                },
                student = f.StudentId == "" ? null : new { id = f.StudentId, name = students.GetValueOrDefault(f.StudentId) },
                resolved = isResolved,
                resolvedAt = isResolved && canAudit ? who.At : null,
                resolvedBy = isResolved && canAudit ? who.By : null,
                imageAttached = f.HasImage,
            };
        }).ToList();

        return t.Json(new
        {
            total, open, page = p, pageSize = size,
            note = canAudit ? null : "Kim va qachon hal qilgani ko'rsatilmadi: «O'zgarishlar tarixi» ruxsati yo'q.",
            items,
        }, items.Count);
    }

    // =============================================================================================
    //  vacancies_list / job_applications
    // =============================================================================================

    [McpServerTool(Name = "vacancies_list", Title = "Vakansiyalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bo'sh ish o'rinlari (vakansiyalar): lavozim, bo'lim, ish turi, joyi, e'lon qilingan maosh oralig'i, holati "
        + "(active — yoqilgan, paused — vaqtincha o'chirilgan, archived), muddati va har bosqichdagi arizalar soni "
        + "(CV qabul qilindi → Test → Demo dars → HR suhbati → Rahbar suhbati → Ishga qabul qilindi / Rad etildi). "
        + "Sukut bo'yicha joriylari (active + paused). Sahifalangan. "
        + "Vacancies with application counts per hiring stage.")]
    public async Task<string> VacanciesList(
        [Description("current (sukut — active + paused) | active | paused | archived | all; vergul bilan bir nechta")] string? status = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Vacancies);
        var statuses = VacancyStatuses(status);
        var db = t.Db;

        var q = db.Vacancies.AsQueryable();
        if (statuses is not null) q = q.Where(v => statuses.Contains(v.Status));

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var vacancies = await q.OrderBy(v => v.Order).ThenByDescending(v => v.CreatedAt).ThenBy(v => v.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(v => new
            {
                v.Id, v.Title, v.Department, v.EmploymentType, v.Location, v.SalaryFrom, v.SalaryTo, v.SalaryNote,
                v.Status, v.Deadline, v.Order, v.CreatedAt, v.ArchivedAt,
            }).ToListAsync(ct);

        // Application counts in ONE grouped query (the panel's CareerController.Vacancies).
        var ids = vacancies.Select(v => v.Id).ToList();
        var counts = await db.JobApplications.Where(a => ids.Contains(a.VacancyId))
            .GroupBy(a => new { a.VacancyId, a.Status })
            .Select(g => new { g.Key.VacancyId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var items = vacancies.Select(v =>
        {
            var mine = counts.Where(c => c.VacancyId == v.Id).ToList();
            return new
            {
                v.Id, v.Title, v.Department, v.EmploymentType, v.Location,
                salary = new { from = v.SalaryFrom, to = v.SalaryTo, note = v.SalaryNote },
                v.Status, v.Deadline,
                createdAt = v.CreatedAt,
                archivedAt = v.ArchivedAt.Length == 0 ? null : v.ArchivedAt,
                applications = new
                {
                    total = mine.Sum(c => c.Count),
                    newCount = mine.Where(c => c.Status == CareerService.StatusNew).Sum(c => c.Count),
                    byStage = ByStage(mine.Select(c => (c.Status, c.Count))),
                },
            };
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            stages = CareerService.Stages.Where(s => !s.IsLegacy).OrderBy(s => s.Order)
                .Select(s => new { key = s.Key, label = s.Label, order = s.Order, isFinal = s.IsFinal }),
            items,
        }, items.Count);
    }

    [McpServerTool(Name = "job_applications", Title = "Ishga arizalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Nomzodlarning ishga arizalari (eng yangisi birinchi): ism, telefon, vakansiya, joriy bosqich, ariza sanasi, "
        + "bosqich qachon va kim tomonidan o'zgartirilgani, nomzodga ko'rinadigan izoh va ichki izoh, tajriba va "
        + "motivatsiya (500 belgigacha), CV biriktirilganmi. Filtrlar: vakansiya, bosqich, sana oralig'i, qidiruv "
        + "(ism, telefon, vakansiya nomi). CV fayli va Telegram ma'lumotlari berilmaydi. Sahifalangan. "
        + "Job applications with hiring stage and notes, without CV files.")]
    public async Task<string> JobApplications(
        [Description("Vakansiya id (vacancies_list natijasidan)")] string? vacancyId = null,
        [Description("Bosqich: new | test | trial | interview | director | hired | rejected | review (eski)")] string? stage = null,
        [Description("Ariza sanasi oralig'i boshi (YYYY-MM-DD)")] string? from = null,
        [Description("Ariza sanasi oralig'i oxiri (YYYY-MM-DD)")] string? to = null,
        [Description("Ism, telefon yoki vakansiya nomi bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Vacancies);
        var range = OptionalRange(from, to);
        if (!string.IsNullOrWhiteSpace(stage) && !CareerService.IsValidStatus(stage.Trim()))
            throw new McpException("«stage»: " + string.Join(" | ", CareerService.Stages.Select(s => s.Key)) + ".");
        var db = t.Db;

        // Same filters as the panel's CareerController.Applications.
        var q = db.JobApplications.AsQueryable();
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var st = stage.Trim();
            q = q.Where(a => a.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(vacancyId))
        {
            var vid = vacancyId.Trim();
            q = q.Where(a => a.VacancyId == vid);
        }
        if (range is { } r)
        {
            var f = McpToolContext.Iso(r.From);
            var tt = McpToolContext.Iso(r.To.AddDays(1));
            q = q.Where(a => string.Compare(a.CreatedAt, f) >= 0 && string.Compare(a.CreatedAt, tt) < 0);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(a => a.FullName.ToLower().Contains(term) || a.Phone.Contains(term)
                             || a.VacancyTitle.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Number).ThenBy(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(a => new
            {
                a.Id, a.Number, a.VacancyId, a.VacancyTitle, a.FullName, a.Phone, a.Experience, a.Motivation,
                HasCv = a.CvUrl != "",
                a.Status, a.StatusNote, a.StatusChangedAt, a.StatusChangedBy, a.AdminNote, a.CreatedAt,
            }).ToListAsync(ct);

        var items = rows.Select(a =>
        {
            var s = CareerService.StageOf(a.Status);
            return new
            {
                a.Id, a.Number,
                vacancy = new { id = a.VacancyId, title = a.VacancyTitle },
                fullName = a.FullName, phone = a.Phone,
                stage = a.Status, stageLabel = s.Key == a.Status ? s.Label : a.Status,
                createdAt = a.CreatedAt,
                stageChangedAt = a.StatusChangedAt, stageChangedBy = a.StatusChangedBy,
                noteToCandidate = a.StatusNote.Length == 0 ? null : a.StatusNote,
                internalNote = a.AdminNote.Length == 0 ? null : a.AdminNote,
                experience = Truncate(a.Experience),
                motivation = Truncate(a.Motivation),
                cvAttached = a.HasCv,
            };
        }).ToList();

        return t.Json(new { total, page = p, pageSize = size, items }, items.Count);
    }

    private static List<string>? VacancyStatuses(string? status)
    {
        var raw = (status ?? "current").Trim().ToLowerInvariant();
        if (raw == "all") return null;
        if (raw == "current") return [CareerService.VacancyActive, CareerService.VacancyPaused];
        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        if (list.Count == 0 || !list.All(CareerService.IsValidVacancyStatus))
            throw new McpException("«status»: current | active | paused | archived | all.");
        return list;
    }

    private static List<object> ByStage(IEnumerable<(string Status, int Count)> counts)
    {
        var list = counts.ToList();
        return CareerService.Stages.OrderBy(s => s.Order).ThenBy(s => s.IsLegacy)
            .Select(s => new { stage = s.Key, label = s.Label, count = list.Where(c => c.Status == s.Key).Sum(c => c.Count) })
            .Where(x => x.count > 0)
            .Cast<object>()
            .Concat(list.Where(c => !CareerService.IsValidStatus(c.Status)).GroupBy(c => c.Status)
                .Select(g => (object)new { stage = g.Key, label = g.Key, count = g.Sum(c => c.Count) }))
            .ToList();
    }

    // =============================================================================================
    //  onboarding_list
    // =============================================================================================

    [McpServerTool(Name = "onboarding_list", Title = "Yangi xodimlar moslashuvi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Yangi xodimlarning 30 kunlik moslashuvi (adaptatsiya cheklisti): ism, telefon, lavozim, boshlanish sanasi, "
        + "holati (active / completed / cancelled), bajarilish foizi, nechanchi kun, kechikkan qadamlar soni, navbatdagi "
        + "qadam va muddati, mentor. Faollar tepada, eng ko'p kechikkani birinchi; yuqorida jamlanma. Filtrlar: holat "
        + "(overdue — faol va kechikkan qadami bor), qidiruv (ism, lavozim, mentor, telefon). onboardingId berilsa — "
        + "o'sha xodimning to'liq cheklisti (har qadam: muddati, bajarildimi, kim/qachon, izoh, kechikkanmi). "
        + "Employee 30-day onboarding progress, or one checklist by id.")]
    public async Task<string> OnboardingList(
        [Description("active | completed | cancelled | overdue")] string? status = null,
        [Description("Ism, lavozim, mentor yoki telefon bo'lagi")] string? search = null,
        [Description("Bitta moslashuv id — cheklisti bilan")] string? onboardingId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Onboarding);
        var st = (status ?? "").Trim().ToLowerInvariant();
        if (st is not ("" or OnboardingService.StatusActive or OnboardingService.StatusCompleted
            or OnboardingService.StatusCancelled or "overdue"))
            throw new McpException("«status»: active | completed | cancelled | overdue.");
        var db = t.Db;
        var today = McpToolContext.Today;

        if (!string.IsNullOrWhiteSpace(onboardingId))
        {
            var id = onboardingId.Trim();
            var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return t.Json(new { message = "Moslashuv topilmadi." }, 0);
            var tasks = await db.EmployeeOnboardingTasks.Where(x => x.OnboardingId == id).ToListAsync(ct);
            var d = OnboardingService.ToDto(o, tasks, today, withTasks: true);
            return t.Json(new
            {
                onboarding = Row(d),
                note = d.Note.Length == 0 ? null : d.Note,
                createdAt = d.CreatedAt, createdBy = d.CreatedBy,
                completedAt = d.CompletedAt.Length == 0 ? null : d.CompletedAt,
                cancelledAt = d.CancelledAt.Length == 0 ? null : d.CancelledAt,
                checklist = (d.Tasks ?? new List<Dtos.OnboardingTaskDto>()).Select(x => new
                {
                    x.Id, x.Title, x.Description, dayOffset = x.DayOffset, dueDate = x.DueDate,
                    done = x.Done,
                    doneAt = x.DoneAt.Length == 0 ? null : x.DoneAt,
                    doneBy = x.DoneBy.Length == 0 ? null : x.DoneBy,
                    note = x.Note.Length == 0 ? null : x.Note,
                    overdue = x.Overdue,
                }),
            }, 1);
        }

        // The panel's OnboardingController.LoadAllAsync: one row per hire (a small table) + their tasks,
        // the summary from the same DTOs (OnboardingService.ToDto / Summarize).
        var list = await db.EmployeeOnboardings.ToListAsync(ct);
        var ids = list.Select(x => x.Id).ToList();
        var byOnboarding = (await db.EmployeeOnboardingTasks.Where(x => ids.Contains(x.OnboardingId)).ToListAsync(ct))
            .ToLookup(x => x.OnboardingId);
        var all = list.Select(x => OnboardingService.ToDto(x, byOnboarding[x.Id], today, withTasks: false)).ToList();
        var summary = OnboardingService.Summarize(all);

        IEnumerable<Dtos.EmployeeOnboardingDto> filtered = st switch
        {
            "" => all,
            "overdue" => all.Where(i => i.Status == OnboardingService.StatusActive && i.OverdueCount > 0),
            _ => all.Where(i => i.Status == st),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(i =>
                i.FullName.ToLowerInvariant().Contains(term) || i.Position.ToLowerInvariant().Contains(term)
                || i.MentorName.ToLowerInvariant().Contains(term) || i.Phone.Contains(term));
        }
        var ordered = filtered
            .OrderBy(i => i.Status == OnboardingService.StatusActive ? 0 : 1)
            .ThenByDescending(i => i.OverdueCount)
            .ThenByDescending(i => i.StartDate, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = ordered.Skip((p - 1) * size).Take(size).Select(Row).ToList();

        return t.Json(new
        {
            summary = new
            {
                active = summary.Active, completed = summary.Completed, cancelled = summary.Cancelled,
                withOverdue = summary.WithOverdue, overdueTasks = summary.OverdueTasks,
            },
            total = ordered.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    private static object Row(Dtos.EmployeeOnboardingDto d) => new
    {
        d.Id, d.FullName, d.Phone, d.Position,
        startDate = d.StartDate, status = d.Status, source = d.Source,
        applicationNumber = d.ApplicationNumber == 0 ? (int?)null : d.ApplicationNumber,
        mentorName = d.MentorName.Length == 0 ? null : d.MentorName,
        progressPct = d.ProgressPct, dayNumber = d.DayNumber,
        tasksTotal = d.Total, tasksDone = d.DoneCount, overdueTasks = d.OverdueCount,
        nextTask = d.NextTitle.Length == 0 ? null : new { title = d.NextTitle, dueDate = d.NextDueDate },
    };

    // =============================================================================================
    //  helpers
    // =============================================================================================

    /// <summary>No dates — no filter; otherwise a validated range (≤ 366 days).</summary>
    private static (DateOnly From, DateOnly To)? OptionalRange(string? from, string? to) =>
        string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to)
            ? null
            : McpToolContext.Range(from, to, McpToolContext.MaxRangeDays);

    private static string? Truncate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var v = s.Trim();
        return v.Length <= MaxText ? v : v[..MaxText] + "…";
    }
}
