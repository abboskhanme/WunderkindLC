using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KASSADAN KASSAGA KO'CHIRISH (`.claude/rules/billing.md` «KASSADAN KASSAGA KO'CHIRISH»).
/// Testlar: <c>CashboxTransferTests</c>.
///
/// <para><b>Nima:</b> pul bir kassadan boshqasiga o'tadi — ikkala kassaning qoldig'i o'zgaradi, lekin
/// bu KIRIM ham, CHIQIM ham EMAS. Shuning uchun alohida jadvalda (<see cref="CashboxTransfer"/>) —
/// moliya hisobotlari, P&amp;L, maosh bazasi uni umuman ko'rmaydi.</para>
///
/// <para><b>Minusga tushmaydi:</b> ko'chirish chiqayotgan kassada YETARLI qoldiq bo'lsagina yoziladi
/// (aks holda qog'ozda yo'q pul "ko'chirilardi"). Bekor qilish ham shu qoida bilan: qabul qilgan kassa
/// pulni allaqachon ishlatib bo'lgan bo'lsa, bekor qilish uni minusga tushirmaydi — rad etiladi.
/// ⚠️ Oddiy chiqim (xarajat) uchun bu qoida YO'Q — avvalgidek, kassa chiqim bilan minusga tushishi
/// mumkin (u ALOHIDA qaror, bu yerda tegilmadi).</para>
///
/// <para><b>Poyga:</b> tekshiruv + yozish bitta jarayon qulfi ostida (<see cref="LockScope"/>), ya'ni
/// bir kassadan bir zumda ikki ko'chirish ikkalasi ham eski qoldiqni ko'rib o'tib ketmaydi.</para>
/// </summary>
public static class CashboxTransferService
{
    public const int MaxNoteLength = 500;
    /// <summary>Barcha ko'chirish amallari uchun bitta qulf (amal kam, navbat zarar qilmaydi).</summary>
    public const string LockScope = "cashbox-transfer";

    public sealed record Request(string? FromCashboxId, string? ToCashboxId, decimal Amount,
                                 string? Date, string? Note, string? RequestId = null);

    public sealed record Result(CashboxTransfer? Transfer, string? Error, bool NotFound = false);

    // ============================ Sof qoidalar ============================

    /// <summary>
    /// Kiritilgan qiymatlar tekshiruvi (qoldiqdan tashqari). <c>null</c> — to'g'ri; aks holda sabab matni.
    /// <paramref name="cleanDate"/> — "yyyy-MM-dd" (bo'sh bo'lsa <paramref name="today"/>).
    /// </summary>
    public static string? Validate(Cashbox? from, Cashbox? to, decimal amount, string? date, string today,
                                   out string cleanDate)
    {
        cleanDate = today;
        if (from is null) return "Qaysi kassadan ko'chirilishini tanlang";
        if (to is null) return "Qaysi kassaga ko'chirilishini tanlang";
        if (from.Id == to.Id) return "Kassa o'ziga ko'chirilmaydi — boshqa kassani tanlang";
        // Arxivlangan kassa yangi pul QABUL QILMAYDI (kassalar qoidasi). Undan CHIQARISH esa mumkin —
        // aynan shu yo'l bilan arxivdagi qoldiq faol kassaga olinadi.
        if (to.IsArchived) return $"«{to.Name}» kassasi arxivlangan — unga pul ko'chirib bo'lmaydi";
        if (amount <= 0) return "Summa musbat bo'lishi kerak";

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", out var d)) return "Sana noto'g'ri";
            cleanDate = d.ToString("yyyy-MM-dd");
        }
        if (string.CompareOrdinal(cleanDate, today) > 0) return "Kelajak sanaga ko'chirib bo'lmaydi";
        return null;
    }

    /// <summary>MINUSGA TUSHMASLIK qoidasi: kassada <paramref name="amount"/> dan kam pul bo'lsa — sabab matni.</summary>
    public static string? OverdraftError(string boxName, decimal balance, decimal amount) =>
        amount > balance
            ? $"«{boxName}» kassasida yetarli mablag' yo'q: qoldiq {AuditService.Money(balance)} so'm, "
              + $"ko'chirish {AuditService.Money(amount)} so'm"
            : null;

    // ============================ Baza ============================

    /// <summary>
    /// Ko'chirishni yozadi (qulf ostida): tekshiruv → qoldiq → yozuv + audit → SaveChanges.
    /// Bir oyna kaliti (<see cref="Request.RequestId"/>) bilan qayta yuborilgan so'rov — ikkinchi
    /// ko'chirish EMAS, avvalgisi qaytadi.
    /// </summary>
    public static async Task<Result> CreateAsync(IAppDbContext db, AuditService audit, Request req,
                                                 string? actorId, string actorName, CancellationToken ct = default)
    {
        using var gate = await PaymentIdempotency.LockAsync(LockScope);
        var reqKey = PaymentIdempotency.Key(LockScope, req.RequestId);
        var knownId = PaymentIdempotency.Find(reqKey, DateTime.UtcNow);
        if (knownId is not null
            && await db.CashboxTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == knownId, ct) is { } known)
            return new Result(known, null);

        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync(ct);
        var from = boxes.FirstOrDefault(b => b.Id == (req.FromCashboxId ?? "").Trim());
        var to = boxes.FirstOrDefault(b => b.Id == (req.ToCashboxId ?? "").Trim());
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (Validate(from, to, req.Amount, req.Date, today, out var date) is { } error)
            return new Result(null, error);

        var balance = await CashboxService.TotalAsync(db, from!.Id, ct);
        if (OverdraftError(from.Name, balance, req.Amount) is { } overdraft)
            return new Result(null, overdraft);

        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > MaxNoteLength }) note = note[..MaxNoteLength];
        var row = new CashboxTransfer
        {
            FromCashboxId = from.Id,
            ToCashboxId = to!.Id,
            Amount = req.Amount,
            Date = date,
            Note = note,
            CreatedAt = AppClock.Iso(),
            CreatedById = actorId,
            CreatedByName = actorName,
        };
        db.CashboxTransfers.Add(row);
        audit.Record(AuditService.EntityCashboxTransfer, row.Id, "create",
            $"Kassadan kassaga ko'chirildi: «{from.Name}» → «{to.Name}» — {AuditService.Money(row.Amount)} so'm"
                + (date != today ? $" ({date})" : "")
                + (note is null ? "" : $" — {note}"),
            after: Snapshot(row));
        await db.SaveChangesAsync(ct);
        PaymentIdempotency.Remember(reqKey, row.Id, DateTime.UtcNow);
        return new Result(row, null);
    }

    /// <summary>
    /// Ko'chirishni BEKOR QILISH ("o'chirish"): qator qoladi (tarix), ikkala qoldiq tiklanadi.
    /// Qabul qilgan kassada pul qolmagan bo'lsa — rad etiladi (minusga tushmaydi).
    /// </summary>
    public static async Task<Result> VoidAsync(IAppDbContext db, AuditService audit, string id, string? reason,
                                               string? actorId, string actorName, CancellationToken ct = default)
    {
        using var gate = await PaymentIdempotency.LockAsync(LockScope);
        var row = await db.CashboxTransfers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return new Result(null, "Ko'chirish topilmadi yoki allaqachon bekor qilingan", NotFound: true);

        var names = await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        string Name(string boxId) => names.GetValueOrDefault(boxId, boxId);

        var received = await CashboxService.TotalAsync(db, row.ToCashboxId, ct);
        if (row.Amount > received)
            return new Result(null,
                $"Bekor qilib bo'lmaydi: «{Name(row.ToCashboxId)}» kassasida {AuditService.Money(received)} so'm qolgan, "
                + $"ko'chirish {AuditService.Money(row.Amount)} so'm — pul allaqachon ishlatilgan. "
                + "Kerak bo'lsa avval kassaga pul qo'ying yoki teskari ko'chirish qiling.");

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is { Length: > MaxNoteLength }) cleanReason = cleanReason[..MaxNoteLength];
        audit.Record(AuditService.EntityCashboxTransfer, row.Id, "delete",
            $"Ko'chirish bekor qilindi: «{Name(row.FromCashboxId)}» → «{Name(row.ToCashboxId)}» — "
                + $"{AuditService.Money(row.Amount)} so'm ({row.Date})"
                + (cleanReason is null ? "" : $" — sabab: {cleanReason}"),
            before: Snapshot(row));
        row.IsVoided = true;
        row.VoidedAt = AppClock.Iso();
        row.VoidedById = actorId;
        row.VoidedByName = actorName;
        row.VoidReason = cleanReason;
        await db.SaveChangesAsync(ct);
        return new Result(row, null);
    }

    /// <summary>Audit Before/After uchun.</summary>
    public static object Snapshot(CashboxTransfer t) => new
    {
        t.FromCashboxId,
        t.ToCashboxId,
        t.Amount,
        t.Date,
        t.Note,
    };
}
