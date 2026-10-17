using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Bitta (o'quvchi, guruh, oy) katagi: hisob va SHU guruh+oyga teglangan to'lov.
/// </summary>
/// <param name="GroupId">Guruh id'si; eski guruhsiz hisobda <see cref="DebtLedger.NoGroup"/>.</param>
/// <param name="Paid">Teglangan to'lovlar − vozvratlar (manfiy bo'lishi mumkin — pastda 0 ga qisiladi).</param>
/// <param name="HasCharge">Hisob qatori bormi (faqat to'lovdan paydo bo'lgan katakda — yo'q).</param>
public sealed record LedgerCell(
    string StudentId, string GroupId, string Month,
    decimal Gross, decimal Discount, decimal Paid, bool HasCharge,
    string? ChargeId = null, string ChargeDate = "")
{
    /// <summary>Chegirmadan keyingi summa (manfiy bo'lmaydi).</summary>
    public decimal Net => Math.Max(0m, Gross - Discount);

    /// <summary>To'langanning shu oy hisobini yopgan qismi (sofdan oshmaydi).</summary>
    public decimal Applied => Math.Min(Math.Max(0m, Paid), Net);

    public decimal Remaining => Net - Applied;

    /// <summary><c>paid</c> · <c>partial</c> · <c>unpaid</c> — <see cref="StudentGroupLedger"/> bilan bir xil.</summary>
    public string Status => Net <= 0 || Remaining <= 0 ? "paid" : Paid > 0 ? "partial" : "unpaid";
}

/// <summary>
/// QARZ HISOBINING YAGONA ASOSI — markaz bo'yicha moliya hisobotlari (qarzdorlik, tariflar, hisob-fakturalar)
/// uchun (o'quvchi, guruh, oy) kataklari.
///
/// <para><b>QOIDA — kassadagi to'lov oynasi (<see cref="StudentGroupLedger"/>) qoidasi:</b>
/// har oy MUSTAQIL; to'lov <c>FinanceTransaction.Month</c> + <c>GroupId</c> bo'yicha o'z katagiga tushadi,
/// vozvrat (<c>expense + refund</c>) ayriladi; keyingi oyga to'langan avans o'tgan oy qarzini YOPMAYDI.</para>
///
/// <para>⚠️ To'lov oynasidan FARQLARI (ataylab, hujjatlangan): (1) oyna SINOVDAGI a'zolik uchun oy ko'rsatmaydi —
/// bu yerda sinovga qaytarilgan a'zolikning qolgan hisobi ham qarz (qatorda «Sinov» belgisi); (2) hisobi YO'Q oyga
/// tushgan to'lov (sinovdagi avans, guruhga teglanmagan eski to'lov) hech bir qarzni yopmaydi — u
/// <see cref="Unmatched"/> bilan alohida aytiladi.</para>
///
/// <para>⚠️ Markaz bo'yicha <c>Student.Balance</c> (umumiy balans) bilan farq qilishi MUMKIN: o'quvchi kelgusi
/// oyni oldindan to'lab, eski oyni to'lamagan bo'lsa — balans musbat, bu yerda esa eski oy qarz. Bu ATAYIN
/// (<c>docs/ASSUMPTIONS.md</c>, 2026-10-17).</para>
///
/// <para>Faqat hisob yozilgan oylar qarz bo'ladi — hisobi hali yozilmagan oy (accrual kutayotgan) hech narsa
/// o'ylab topmaydi (<see cref="StudentGroupLedger"/> previewidan farqi shu: hisobot HAQIQIY yozuvlardan).</para>
///
/// <para>Hisob-kitob XOTIRADA: SQLite decimal yig'indisini qo'llamaydi (<c>rating.md</c> §3), ma'lumot esa
/// oyiga ~1 000 qator.</para>
/// </summary>
public static class DebtLedger
{
    /// <summary>Eski guruhsiz (<c>MonthlyCharge.GroupId == null</c>) hisoblar kaliti.</summary>
    public const string NoGroup = "";

    /// <summary>To'lov muddati kuni sozlanmagan bo'lsa (<see cref="JournalPolicy"/> bilan bir xil).</summary>
    public const int DefaultDueDay = 10;

    /// <summary>Bir so'rovda ko'pi bilan nechta oy (cheksiz davr butun tarixni yuklamasin).</summary>
    public const int MaxMonths = 24;

    public sealed record ChargeRow(string Id, string StudentId, string? GroupId, string Month, decimal Amount, decimal Discount, string Date);

    /// <param name="Signed">Kirim musbat, vozvrat manfiy.</param>
    public sealed record PaymentRow(string StudentId, string? GroupId, string Month, decimal Signed);

    /// <summary>Kataklarni yig'adi. Bir katakka tushgan bir nechta hisob qatori (eski dublikat) qo'shiladi.</summary>
    public static List<LedgerCell> Build(IEnumerable<ChargeRow> charges, IEnumerable<PaymentRow> payments)
    {
        var cells = new Dictionary<(string, string, string), LedgerCell>();
        foreach (var c in charges)
        {
            var key = (c.StudentId, c.GroupId ?? NoGroup, c.Month);
            cells[key] = cells.TryGetValue(key, out var cur)
                ? cur with { Gross = cur.Gross + c.Amount, Discount = cur.Discount + c.Discount }
                : new LedgerCell(c.StudentId, c.GroupId ?? NoGroup, c.Month, c.Amount, c.Discount, 0m, true, c.Id, c.Date);
        }
        foreach (var p in payments)
        {
            var key = (p.StudentId, p.GroupId ?? NoGroup, p.Month);
            cells[key] = cells.TryGetValue(key, out var cur)
                ? cur with { Paid = cur.Paid + p.Signed }
                : new LedgerCell(p.StudentId, p.GroupId ?? NoGroup, p.Month, 0m, 0m, p.Signed, false);
        }
        return cells.Values.ToList();
    }

    /// <summary>Oyning to'lov muddati — shu oyning <paramref name="dueDay"/>-kuni (oy uzunligiga qisiladi).</summary>
    public static DateOnly DueDate(string month, int dueDay)
    {
        if (month.Length < 7 || !int.TryParse(month[..4], out var y) || !int.TryParse(month[5..7], out var m)
            || m is < 1 or > 12)
            return DateOnly.MaxValue;
        var day = Math.Clamp(dueDay, 1, DateTime.DaysInMonth(y, m));
        return new DateOnly(y, m, day);
    }

    /// <summary>Muddatdan o'tgan kunlar (manfiy — muddat hali kelmagan).</summary>
    public static int AgeDays(string month, int dueDay, DateOnly today)
    {
        var due = DueDate(month, dueDay);
        return due == DateOnly.MaxValue ? 0 : today.DayNumber - due.DayNumber;
    }

    /// <summary>Qarz yoshi guruhi (TZ 1.1: 7 / 30 / 60+ kun).</summary>
    /// <remarks>Muddat kunining O'ZI hali kechikish emas (faktura ham shunday — <c>OverdueDays &gt; 0</c>).</remarks>
    public static string Bucket(int ageDays) => ageDays switch
    {
        <= 0 => "notdue",
        < 7 => "0-6",
        < 30 => "7-29",
        < 60 => "30-59",
        _ => "60+",
    };

    public static readonly IReadOnlyList<(string Key, string Label)> Buckets =
    [
        ("notdue", "Muddati kelmagan"),
        ("0-6", "1–6 kun"),
        ("7-29", "7–29 kun"),
        ("30-59", "30–59 kun"),
        ("60+", "60+ kun"),
    ];

    /// <summary>Hisobi yo'q kataklarga tushgan to'lovlar (soni va summasi).</summary>
    public static UnmatchedPaymentsDto Unmatched(IEnumerable<LedgerCell> cells)
    {
        var list = cells.Where(c => !c.HasCharge && c.Paid > 0).ToList();
        return new UnmatchedPaymentsDto(list.Count, list.Sum(c => c.Paid));
    }

    /// <summary>Kataklar yig'indisi. <c>Debtors</c> — qarzi bor DISTINCT o'quvchilar.</summary>
    public static DebtTotalsDto Totals(IEnumerable<LedgerCell> cells)
    {
        decimal gross = 0, discount = 0, net = 0, applied = 0, debt = 0;
        var debtors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cells)
        {
            gross += c.Gross;
            discount += c.Discount;
            net += c.Net;
            applied += c.Applied;
            debt += c.Remaining;
            if (c.Remaining > 0) debtors.Add(c.StudentId);
        }
        return new DebtTotalsDto(gross, discount, net, applied, debt, Pct(applied, net), debtors.Count);
    }

    /// <summary>Yig'ilish foizi (1 kasr). Sof 0 bo'lsa — 100 (yig'adigan narsa yo'q).</summary>
    public static decimal Pct(decimal part, decimal whole) =>
        whole <= 0 ? 100m : decimal.Round(part / whole * 100m, 1);

    /// <summary>"yyyy-MM" oralig'ini tekshiradi va qirqadi (buzuq → standart, teskari → almashtiriladi).</summary>
    public static (string From, string To) ClampRange(string? from, string? to, string defaultFrom, string defaultTo)
    {
        var f = PaymentFields.IsMonth(from) ? from! : defaultFrom;
        var t = PaymentFields.IsMonth(to) ? to! : defaultTo;
        if (string.CompareOrdinal(f, t) > 0) (f, t) = (t, f);
        var months = TuitionService.MonthRange(f, t).ToList();
        if (months.Count > MaxMonths) f = months[^MaxMonths];
        return (f, t);
    }

    /// <summary>Davr kataklarini yuklaydi: hisoblar + teglangan to'lovlar (bekor qilinganlar global filtr bilan tushadi).</summary>
    public static async Task<List<LedgerCell>> LoadAsync(
        IAppDbContext db, string fromMonth, string toMonth, CancellationToken ct = default)
    {
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => string.Compare(c.Month, fromMonth) >= 0 && string.Compare(c.Month, toMonth) <= 0)
            .Select(c => new ChargeRow(c.Id, c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount, c.Date))
            .ToListAsync(ct);
        var payments = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId != null && t.Month != null
                        && string.Compare(t.Month, fromMonth) >= 0 && string.Compare(t.Month, toMonth) <= 0
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund")))
            .Select(t => new { t.StudentId, t.GroupId, t.Month, t.Direction, t.Amount })
            .ToListAsync(ct);
        return Build(charges, payments.Select(p => new PaymentRow(
            p.StudentId!, p.GroupId, p.Month!, p.Direction == "expense" ? -p.Amount : p.Amount)));
    }

    /// <summary>To'lov muddati kuni (<c>CenterMeta.JournalUnpaidCutoffDay</c>, 1..28; bo'lmasa 10).</summary>
    public static async Task<int> DueDayAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var d = await db.CenterMeta.AsNoTracking().Select(m => (int?)m.JournalUnpaidCutoffDay).FirstOrDefaultAsync(ct);
        return d is >= 1 and <= 28 ? d.Value : DefaultDueDay;
    }
}
