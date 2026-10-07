using System.Text;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Moliya TIZIM toifalari (<see cref="FinanceTransaction.Category"/>) — hisob-kitob va hisobotlar
/// AYNAN shu kodlarga qaraydi (`.claude/rules/billing.md` «TRANZAKSIYA TURLARI»). Klientdagi
/// ro'yxat — `config/constants.ts` (`incomeCategories`/`expenseCategories`), ikkalasi bir xil bo'lsin.
/// </summary>
public static class FinanceCategories
{
    public const string Tuition = "tuition";
    /// <summary>Kitob/qo'llanma sotuvi (kirim) va kitob qaytarishdagi pul (chiqim) — `BookFinance`.</summary>
    public const string Book = "book";
    /// <summary>Mock imtihon to'lovi (kirim, qo'lda kiritiladi — alohida modul yo'q).</summary>
    public const string Mock = "mock";
    public const string Other = "other";
    public const string Rent = "rent";
    public const string Utilities = "utilities";
    /// <summary>Jihoz/materiallar.</summary>
    public const string Supplies = "supplies";
    /// <summary>Internet — «Kommunal»dan ALOHIDA (yig'ilishdagi talab: filial xarajatlari alohida).</summary>
    public const string Internet = "internet";
    /// <summary>Kantselyariya — «Jihoz/materiallar» (<c>supplies</c>) dan ALOHIDA.</summary>
    public const string Stationery = "stationery";
}

/// <summary>
/// KIRIM/CHIQIM TURLARI — yig'ilish talabi bo'yicha majburiy turlar (bir martalik seed).
/// Testlar: <c>FinanceTypeSeedTests</c>.
///
/// <list type="bullet">
/// <item>Kirim: O'qish to'lovi · Kitob/qo'llanma sotuvi · Mock imtihon to'lovi · Boshqa kirim.</item>
/// <item>Chiqim (filial xarajatlari): Ijara · Kommunal to'lovlar · Internet · Kantselyariya.</item>
/// </list>
///
/// <para><b>Nega BIR MARTALIK</b> (<see cref="SeedMarker"/>, kalit <see cref="MarkerKey"/>):
/// tranzaksiya turlari — foydalanuvchi boshqaradigan katalog. "Yo'q bo'lsa qo'shiladi" naqshi admin
/// ATAYIN o'chirgan turni har restartda qayta tug'dirardi; "faqat bo'sh jadvalda" esa mavjud bazaga
/// (prod) yangi turlarni umuman olib kirmasdi. Belgi bilan seed mavjud bazada BIR marta ishlaydi,
/// keyin admin qarori (o'chirish, qayta nomlash) hurmat qilinadi.</para>
///
/// <para><b>Eski turlar QAYTA BOG'LANADI</b> (yangi tur qo'shilmaydi): edutizimdan kelgan «Kitob uchun»,
/// «Mock to'lov» (<c>other</c> edi), «Internet va telefon» (<c>utilities</c> edi), «Kanstovar»
/// (<c>supplies</c> edi) yangi toifaga o'tkaziladi — aks holda katalogda «Internet va telefon» (Kommunal)
/// va «Internet» (Internet) yonma-yon turib qolar, ishlatilgani esa o'chirilmasdi. Faqat ESKI (legacy)
/// toifadagi qator ko'chiriladi — admin boshqa toifaga ataylab bog'lagan tur tegilmaydi.</para>
///
/// <para>⚠️ Mavjud TRANZAKSIYALAR qayta yozilmaydi (backfill YO'Q): turning toifasi faqat YANGI
/// yozuvlarga ta'sir qiladi, eski qatorlar hisobotda avvalgi toifasida qoladi (audit.md «eski
/// qatorlar qayta yozilmaydi» bilan bir xil printsip).</para>
/// </summary>
public static class FinanceTypeSeed
{
    /// <summary>Seed belgisi — yozilgan bo'lsa seed qayta ishlamaydi.</summary>
    public const string MarkerKey = "finance-types-2026-10";

    /// <param name="Kind">Bo'lim (kirim/chiqim).</param>
    /// <param name="Name">Yangi qo'shilganda turning nomi.</param>
    /// <param name="Category">Tizim toifasi.</param>
    /// <param name="Aliases">Shu ma'nodagi mavjud tur nomlari (eski/edutizim nomlari).</param>
    /// <param name="LegacyCategory">Eski tur shu toifada bo'lsa — yangi toifaga QAYTA BOG'LANADI.</param>
    /// <param name="AnyOfCategory">true — bo'limda shu toifadagi ISTALGAN tur yetarli; false — nomi ham mos
    /// bo'lishi shart (<c>other</c> umumiy savat: «Fond» ham other, lekin u «Boshqa kirim» emas).</param>
    public sealed record Required(string Kind, string Name, string Category, string[] Aliases,
                                  string? LegacyCategory, bool AnyOfCategory);

    public static readonly Required[] RequiredTypes =
    [
        // — KIRIM —
        new("kirim", "O'qish to'lovi", FinanceCategories.Tuition, ["O'quvchi to'ladi", "O'quvchi to'lovi"], null, true),
        new("kirim", "Kitob/qo'llanma sotuvi", FinanceCategories.Book, ["Kitob uchun", "Kitob sotuvi", "Kitob"], FinanceCategories.Other, true),
        new("kirim", "Mock imtihon to'lovi", FinanceCategories.Mock, ["Mock to'lov", "Mock"], FinanceCategories.Other, true),
        new("kirim", "Boshqa kirim", FinanceCategories.Other, ["Boshqa"], null, false),
        // — CHIQIM (filial xarajatlari) —
        new("chiqim", "Ijara", FinanceCategories.Rent, ["Arenda"], null, true),
        new("chiqim", "Kommunal to'lovlar", FinanceCategories.Utilities, ["Kommunal"], null, true),
        new("chiqim", "Internet", FinanceCategories.Internet, ["Internet va telefon", "Internet va aloqa"], FinanceCategories.Utilities, true),
        new("chiqim", "Kantselyariya", FinanceCategories.Stationery, ["Kanstovar", "Kanselyariya", "Kanstovarlar"], FinanceCategories.Supplies, true),
    ];

    /// <summary>Reja: qo'shiladigan yangi turlar va toifasi almashadigan mavjud turlar.</summary>
    public sealed record Plan(List<TransactionType> Add, List<(TransactionType Row, string NewCategory)> Repoint)
    {
        public bool IsEmpty => Add.Count == 0 && Repoint.Count == 0;
    }

    /// <summary>Nom solishtirish: registr, apostrof turlari (' ʻ ’ `) va ortiqcha bo'shliqlar hisobga olinmaydi.</summary>
    public static string NormName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Trim().ToLowerInvariant())
            sb.Append(ch is 'ʻ' or 'ʼ' or '’' or '‘' or '`' or '´' ? '\'' : ch);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool NameMatches(string? name, Required r)
    {
        var n = NormName(name);
        return n == NormName(r.Name) || r.Aliases.Any(a => NormName(a) == n);
    }

    /// <summary>
    /// SOF reja (bazaga tegmaydi). Har majburiy tur uchun: bor bo'lsa — hech narsa; eski nomli tur ESKI
    /// toifada tursa — qayta bog'lanadi; xuddi shu nomli tur BOSHQA toifada tursa — tegilmaydi (admin
    /// qarori, nomdosh tur yaratilmaydi); aks holda yangisi qo'shiladi (bo'lim oxiriga).
    /// </summary>
    public static Plan PlanFor(IReadOnlyCollection<TransactionType> existing)
    {
        var add = new List<TransactionType>();
        var repoint = new List<(TransactionType Row, string NewCategory)>();
        var nextOrder = existing.GroupBy(t => t.Kind).ToDictionary(g => g.Key, g => g.Max(t => t.Order) + 1);

        foreach (var r in RequiredTypes)
        {
            var inKind = existing.Where(t => t.Kind == r.Kind).ToList();
            bool Effective(TransactionType t, string category) =>
                (repoint.FirstOrDefault(x => ReferenceEquals(x.Row, t)).NewCategory ?? t.BaseCategory) == category;

            var satisfied = inKind.Any(t => Effective(t, r.Category) && (r.AnyOfCategory || NameMatches(t.Name, r)));
            if (satisfied) continue;

            var legacy = r.LegacyCategory is null ? null
                : inKind.FirstOrDefault(t => Effective(t, r.LegacyCategory) && NameMatches(t.Name, r));
            if (legacy is not null)
            {
                repoint.Add((legacy, r.Category));
                continue;
            }
            if (inKind.Any(t => NameMatches(t.Name, r))) continue;

            nextOrder.TryGetValue(r.Kind, out var order);
            nextOrder[r.Kind] = order + 1;
            add.Add(new TransactionType
            {
                Name = r.Name,
                Kind = r.Kind,
                Direction = TransactionTypeCatalog.DirectionOf(r.Kind),
                BaseCategory = r.Category,
                Order = order,
            });
        }
        return new Plan(add, repoint);
    }

    /// <summary>
    /// Bir martalik seed: belgi bo'lmasa reja qo'llanadi va belgi yoziladi (bitta SaveChanges).
    /// Qaytaradi: <c>Ran=false</c> — belgi allaqachon bor (hech narsa qilinmadi).
    /// </summary>
    public static async Task<(bool Ran, int Added, int Repointed)> EnsureOnceAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == MarkerKey, ct)) return (false, 0, 0);

        var existing = await db.TransactionTypes.ToListAsync(ct);
        var plan = PlanFor(existing);
        foreach (var (row, category) in plan.Repoint) row.BaseCategory = category;
        db.TransactionTypes.AddRange(plan.Add);
        db.SeedMarkers.Add(new SeedMarker { Key = MarkerKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return (true, plan.Add.Count, plan.Repoint.Count);
    }
}
