using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KITOB SOTUVI → KASSA (`.claude/rules/books.md` §7). Pul HAQIQATAN olingan yoki qaytarilgan paytda
/// moliya tranzaksiyasi yoziladi, toifasi <see cref="FinanceCategories.Book"/>:
///
/// <list type="bullet">
/// <item><b>Naqd/karta sotuv tasdiqlandi</b> — kirim (sotuv summasi);</item>
/// <item><b>Nasiya to'landi</b> — kirim (o'sha paytdagi SOF summa: oldin qaytarilgani ayirilgan);</item>
/// <item><b>Qaytarishda pul berildi</b> — chiqim, sotuv kirimi tushgan KASSADAN.</item>
/// </list>
///
/// <para>⚠️ Toifa <c>tuition</c> EMAS — o'quv to'lovi hisobotlari, o'quvchi balansi, o'qituvchi
/// foizi va KPI faqat <c>tuition</c>/<c>refund</c> ga qaraydi, ya'ni kitob puli ularga tushmaydi
/// (books.md'dagi asl xavotir aynan shu edi). <c>StudentId</c> ATAYIN yozilmaydi: o'quvchi to'lov
/// tarixi va o'quvchini o'chirish (purge) kitob pulini ko'rmasin; xaridor — izohda.</para>
///
/// <para>⚠️ Integratsiyadan OLDINGI sotuvlar moliyaga yozilmagan va BACKFILL qilinmaydi. Shuning uchun
/// qaytarishda pul faqat shu buyurtmaning moliya KIRIMI bor bo'lsa chiqim qilinadi — aks holda eski
/// sotuvning qaytarilishi kassadan "kirmagan pulni" ayirib, qoldiqni yolg'on kamaytirardi.</para>
///
/// <para>Hech biri SaveChanges qilmaydi — <see cref="BookSalesService"/> o'z (yagona) SaveChanges'ida
/// saqlaydi; poyga bo'lsa qo'shilgan yozuvni o'zi qaytarib oladi.</para>
/// </summary>
public static class BookFinance
{
    /// <summary>Moliyadagi izoh: "Kitob sotuvi #12: Kitob nomi × 2 — Xaridor".</summary>
    public static string SaleNote(BookOrder o) =>
        $"Kitob sotuvi #{o.Number}: {o.BookTitle} × {BookSalesService.NetQty(o)}"
        + (string.IsNullOrWhiteSpace(o.CustomerName) ? "" : $" — {o.CustomerName.Trim()}");

    /// <summary>Kitob toifasidagi KIRIM turi (jadvalda markazning o'z nomi chiqsin). Yo'q bo'lsa null.</summary>
    private static Task<string?> BookTypeIdAsync(IAppDbContext db, CancellationToken ct) =>
        db.TransactionTypes.AsNoTracking()
            .Where(t => t.Direction == "income" && t.BaseCategory == FinanceCategories.Book)
            .OrderBy(t => t.Order).Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// SOTUV PULI olindi — kirim (kontekstga qo'shiladi, saqlanmaydi). Pul olinmagan (nasiya) yoki
    /// summa 0 bo'lsa — null. Kassa: kiritgan xodimning o'z kassasi → «Asosiy kassa» (`CashboxService`).
    /// </summary>
    public static async Task<FinanceTransaction?> AddSaleIncomeAsync(
        IAppDbContext db, BookOrder order, string? actorId, string? actorName, CancellationToken ct = default)
    {
        if (!BookSalesService.IsPaid(order)) return null;
        var amount = BookSalesService.NetTotal(order);
        if (amount <= 0) return null;
        // Bir buyurtmaga BITTA kirim: bunday qator Moliya'da tahrirlanmaydi/bekor qilinmaydi, ya'ni
        // takrori (poyga yoki qayta chaqiruv) kassada abadiy qolardi.
        if (db.FinanceTransactions.Local.Any(t => t.BookOrderId == order.Id && t.Direction == "income")
            || await db.FinanceTransactions.AnyAsync(t => t.BookOrderId == order.Id && t.Direction == "income", ct))
            return null;

        var tx = new FinanceTransaction
        {
            Date = AppClock.Today.ToString("yyyy-MM-dd"),
            Direction = "income",
            Category = FinanceCategories.Book,
            TypeId = await BookTypeIdAsync(db, ct),
            Amount = amount,
            Note = SaleNote(order),
            Method = BookSalesService.EffectiveMethod(order),
            CardLast4 = order.CardLast4,
            CreatedBy = actorName,
            CreatedById = actorId,
            CashboxId = (await CashboxService.ResolveAsync(db, null, actorId, ct)).CashboxId,
            BookOrderId = order.Id,
        };
        db.FinanceTransactions.Add(tx);
        return tx;
    }

    /// <summary>
    /// QAYTARISHDA pul berildi — chiqim, sotuv kirimi tushgan kassadan (u arxivlangan bo'lsa qaytaruvchining
    /// standart kassasi). Shu buyurtmaning moliya kirimi yo'q (eski sotuv) yoki <paramref name="refund"/> 0 — null.
    /// </summary>
    public static async Task<FinanceTransaction?> AddRefundExpenseAsync(
        IAppDbContext db, BookOrder order, decimal refund, int qty, string? actorId, string? actorName,
        CancellationToken ct = default)
    {
        if (refund <= 0) return null;
        var income = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.BookOrderId == order.Id && t.Direction == "income")
            .OrderBy(t => t.CreatedAt)
            .Select(t => new { t.CashboxId, t.Method })
            .FirstOrDefaultAsync(ct);
        if (income is null) return null;

        var tx = new FinanceTransaction
        {
            Date = AppClock.Today.ToString("yyyy-MM-dd"),
            Direction = "expense",
            Category = FinanceCategories.Book,
            Amount = refund,
            Note = $"Kitob qaytarildi #{order.Number}: {order.BookTitle} × {qty}"
                   + (string.IsNullOrWhiteSpace(order.CustomerName) ? "" : $" — {order.CustomerName.Trim()}"),
            // Pul qaysi shaklda olingan bo'lsa shunday qaytadi ("Naqd jami" to'g'ri chiqsin).
            Method = income.Method,
            CreatedBy = actorName,
            CreatedById = actorId,
            CashboxId = await CashboxService.ForRefundAsync(db, income.CashboxId, actorId, ct),
            BookOrderId = order.Id,
        };
        db.FinanceTransactions.Add(tx);
        return tx;
    }
}
