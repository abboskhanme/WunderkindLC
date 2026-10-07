using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KASSALAR — pul qaysi kassaga tushishi va kassa qoldig'i (`docs/SPEC-kassalar.md`).
/// Testlar: <c>CashboxServiceTests</c>.
///
/// <para><b>Qaysi kassa</b> (<see cref="ResolveAsync"/>): tanlangan bo'lsa — o'sha (faol bo'lishi
/// shart); aks holda kiritgan xodimning O'Z kassasi; bo'lmasa «Asosiy kassa»; u ham bo'lmasa birinchi
/// faol kassa. Tanlov jimgina boshqa kassaga TUSHIRILMAYDI — noto'g'ri/arxivlangan kassa 400 beradi,
/// aks holda pul kassir bilmagan kassaga yozilib, qoldiqlar yolg'on chiqardi.</para>
///
/// <para><b>Qoldiq</b> saqlanmaydi — har safar tranzaksiyalardan hisoblanadi (kirim − chiqim). Bekor
/// qilingan yozuvlar global filtr bilan o'z-o'zidan tushib qoladi.</para>
/// </summary>
public static class CashboxService
{
    /// <summary>«Asosiy kassa» — migratsiya yaratadi, eski yozuvlar unga ko'chirilgan; arxivlanmaydi.</summary>
    public const string MainId = "main";
    public const string MainName = "Asosiy kassa";
    public const int MaxNameLength = 100;

    public sealed record Resolution(string? CashboxId, string? Error);

    /// <summary>
    /// Kassa qoldig'i. <paramref name="Total"/> — butun tarix: kirim − chiqim + ko'chirib kelingan −
    /// ko'chirib ketilgan. Davr maydonlari ALOHIDA: ko'chirish kirim/chiqim EMAS, shuning uchun
    /// <paramref name="PeriodIncome"/>/<paramref name="PeriodExpense"/> ga qo'shilmaydi.
    /// </summary>
    public sealed record Balance(decimal Total, decimal PeriodIncome, decimal PeriodExpense,
                                 decimal PeriodTransferIn = 0m, decimal PeriodTransferOut = 0m);

    // ============================ Sof qoidalar ============================

    /// <summary>Foydalanuvchining standart kassasi: o'zi mas'ul faol kassa → «Asosiy kassa» → birinchi faol.</summary>
    public static Cashbox? PickDefault(IEnumerable<Cashbox> boxes, string? userId)
    {
        var active = Active(boxes);
        if (!string.IsNullOrEmpty(userId))
        {
            var own = active.FirstOrDefault(b => b.ResponsibleUserId == userId);
            if (own is not null) return own;
        }
        return active.FirstOrDefault(b => b.Id == MainId) ?? active.FirstOrDefault();
    }

    /// <summary>Faol kassalar — yaratilgan tartibda («Asosiy kassa» eng birinchi yaratilgan).</summary>
    public static List<Cashbox> Active(IEnumerable<Cashbox> boxes) =>
        boxes.Where(b => !b.IsArchived)
            .OrderBy(b => b.Id == MainId ? 0 : 1)
            .ThenBy(b => b.CreatedAt, StringComparer.Ordinal)
            .ThenBy(b => b.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>Nom tekshiruvi: bo'sh emas, uzun emas, FAOL kassalar orasida takrorlanmaydi (registrsiz).</summary>
    public static string? ValidateName(string? name, IEnumerable<Cashbox> existing, string? selfId, out string clean)
    {
        clean = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return "Kassa nomini kiriting";
        if (clean.Length > MaxNameLength) return $"Kassa nomi {MaxNameLength} belgidan oshmasin";
        var key = clean;
        if (existing.Any(b => !b.IsArchived && b.Id != selfId && string.Equals(b.Name, key, StringComparison.OrdinalIgnoreCase)))
            return $"«{clean}» nomli kassa allaqachon bor";
        return null;
    }

    // ============================ Baza ============================

    /// <summary>
    /// Yangi yozuv qaysi kassaga tushadi. <paramref name="requestedId"/> berilsa — o'sha (yo'q yoki
    /// arxivlangan bo'lsa <see cref="Resolution.Error"/>); berilmasa — <see cref="PickDefault"/>.
    /// </summary>
    public static async Task<Resolution> ResolveAsync(IAppDbContext db, string? requestedId, string? userId,
                                                      CancellationToken ct = default)
    {
        var boxes = await db.Cashboxes.AsNoTracking().ToListAsync(ct);
        var id = string.IsNullOrWhiteSpace(requestedId) ? null : requestedId.Trim();
        if (id is null) return new Resolution(PickDefault(boxes, userId)?.Id, null);

        var box = boxes.FirstOrDefault(b => b.Id == id);
        if (box is null) return new Resolution(null, "Kassa topilmadi");
        if (box.IsArchived) return new Resolution(null, $"«{box.Name}» kassasi arxivlangan — boshqa kassani tanlang");
        return new Resolution(box.Id, null);
    }

    /// <summary>
    /// VOZVRAT qaysi kassadan chiqadi: pulni QABUL QILGAN kassa (asl to'lovniki) qaytaradi — to'lov va
    /// vozvrat bitta kassada bo'lsa uning qoldig'i izchil qoladi. U kassa arxivlangan/yo'q (yoki asl
    /// to'lov kassasiz) bo'lsa — qaytarayotgan xodimning standart kassasi.
    /// </summary>
    public static async Task<string?> ForRefundAsync(IAppDbContext db, string? originalCashboxId, string? actorId,
                                                     CancellationToken ct = default)
    {
        var original = await ResolveAsync(db, originalCashboxId, actorId, ct);
        return original.Error is null ? original.CashboxId : (await ResolveAsync(db, null, actorId, ct)).CashboxId;
    }

    /// <summary>
    /// TAHRIRDA kassani almashtirish. <paramref name="requestedId"/> bo'sh yoki joriy kassa bilan bir xil —
    /// o'zgarmaydi (<c>(null, null)</c>; eski forma kassa yubormaydi). Aks holda yangi kassa faol bo'lishi
    /// shart (xato — <see cref="Resolution.Error"/>). Qaytaradi: yangi kassa id'si yoki null (o'zgarmaydi).
    /// </summary>
    public static async Task<Resolution> ChangeAsync(IAppDbContext db, string? requestedId, string? currentId,
                                                     CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestedId) || requestedId.Trim() == currentId) return new Resolution(null, null);
        return await ResolveAsync(db, requestedId, null, ct);
    }

    /// <summary>
    /// Har kassaning qoldig'i (butun tarix) va davr ichidagi kirim/chiqimi + ko'chirishlari.
    /// <para>⚠️ KO'CHIRISH (<see cref="CashboxTransfer"/>) faqat SHU YERDA qoldiqqa qo'shiladi — u
    /// moliya tranzaksiyasi emas, ya'ni P&amp;L/hisobotlarga umuman tushmaydi. Bekor qilingan ko'chirish
    /// ham, bekor qilingan tranzaksiya ham global filtr bilan o'z-o'zidan tushib qoladi.</para>
    /// </summary>
    public static async Task<Dictionary<string, Balance>> BalancesAsync(IAppDbContext db, string? from, string? to,
                                                                       CancellationToken ct = default)
    {
        // Xotirada yig'iladi: SQLite (testlar) decimal yig'indini tarjima qila olmaydi; qatorlar yengil.
        var rows = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.CashboxId != null)
            .Select(t => new { t.CashboxId, t.Direction, t.Amount, t.Date })
            .ToListAsync(ct);
        var transfers = await db.CashboxTransfers.AsNoTracking()
            .Select(t => new { t.FromCashboxId, t.ToCashboxId, t.Amount, t.Date })
            .ToListAsync(ct);
        bool InPeriod(string date) =>
            (string.IsNullOrEmpty(from) || string.CompareOrdinal(date, from) >= 0)
            && (string.IsNullOrEmpty(to) || string.CompareOrdinal(date, to) <= 0);

        var byBox = rows.ToLookup(r => r.CashboxId!);
        var inBy = transfers.ToLookup(t => t.ToCashboxId);
        var outBy = transfers.ToLookup(t => t.FromCashboxId);
        var ids = byBox.Select(g => g.Key).Concat(inBy.Select(g => g.Key)).Concat(outBy.Select(g => g.Key)).Distinct();
        return ids.ToDictionary(id => id, id =>
        {
            var own = byBox[id];
            var inT = inBy[id];
            var outT = outBy[id];
            return new Balance(
                own.Sum(r => r.Direction == "income" ? r.Amount : -r.Amount) + inT.Sum(t => t.Amount) - outT.Sum(t => t.Amount),
                own.Where(r => r.Direction == "income" && InPeriod(r.Date)).Sum(r => r.Amount),
                own.Where(r => r.Direction != "income" && InPeriod(r.Date)).Sum(r => r.Amount),
                inT.Where(t => InPeriod(t.Date)).Sum(t => t.Amount),
                outT.Where(t => InPeriod(t.Date)).Sum(t => t.Amount));
        });
    }

    /// <summary>Bitta kassaning HOZIRGI qoldig'i (butun tarix, ko'chirishlar bilan). Kassa yo'q/bo'sh — 0.</summary>
    public static async Task<decimal> TotalAsync(IAppDbContext db, string cashboxId, CancellationToken ct = default)
    {
        var rows = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.CashboxId == cashboxId)
            .Select(t => new { t.Direction, t.Amount })
            .ToListAsync(ct);
        var transfers = await db.CashboxTransfers.AsNoTracking()
            .Where(t => t.FromCashboxId == cashboxId || t.ToCashboxId == cashboxId)
            .Select(t => new { t.ToCashboxId, t.Amount })
            .ToListAsync(ct);
        return rows.Sum(r => r.Direction == "income" ? r.Amount : -r.Amount)
               + transfers.Sum(t => t.ToCashboxId == cashboxId ? t.Amount : -t.Amount);
    }

    /// <summary>«Asosiy kassa» bo'lmasa yaratadi (yangi baza). SaveChanges — chaqiruvchida. true = qo'shildi.</summary>
    public static bool EnsureMain(IAppDbContext db)
    {
        if (db.Cashboxes.Any(b => b.Id == MainId)) return false;
        db.Cashboxes.Add(new Cashbox { Id = MainId, Name = MainName, CreatedAt = AppClock.Iso() });
        return true;
    }
}
