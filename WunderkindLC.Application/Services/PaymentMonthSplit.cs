namespace WunderkindLC.Application.Services;

/// <summary>
/// TO'LOVNI OYLARGA TAQSIMLASH (2026-10-23, mijoz shikoyati: "qarzdorlar to'lasa yana" noto'g'ri). Sof qoida
/// (testlangan: <c>PaymentMonthSplitTests</c>), klientdagi nusxasi — <c>lib/paymentSplit.ts</c>.
///
/// <para><b>Muammo:</b> to'lov oynasi eng eski QARZDOR oyni oldindan tanlaydi (masalan sentabr — 1 darslik
/// 20 000 qoldiq). Kassir summani 225 000 ga o'zgartirib, oyni qoldirsa butun to'lov SENTABRGA teglanardi:
/// per-guruh hisobda (to'lov oynasi, guruh ro'yxatidagi "1 oy qarz", to'lov eslatmasi) oktabr to'lanmagan,
/// o'qituvchi maoshidagi «Hozir chiqadigan» esa oktabr pulini ko'rmay, uni «Qarzdorlar to'lasa yana» ga
/// qo'shardi — holbuki o'quvchining umumiy balansi 0.</para>
///
/// <para><b>Qoida:</b> qatorlar — AYNAN to'lov oynasi ko'rsatadigan per-guruh hisob
/// (<see cref="StudentGroupLedger"/>, oylar o'sish tartibida). Tanlangan oydan boshlab har oy o'z QOLDIG'ICHA
/// oladi; oxirgi qatordan ortgani o'sha oxirgi qatorda qoladi (avans — avvalgidek).</para>
/// <list type="bullet">
///   <item>Summa tanlangan oy qoldig'iga sig'sa — BITTA qism, xatti-harakat o'zgarmaydi.</item>
///   <item>Tanlangan oy ro'yxatda yo'q yoki uning qoldig'i 0 (to'langan oy ATAYIN tanlangan) — BITTA qism,
///         tanlangan oyda (kassirning ongli tanlovi; bo'linish faqat qoldiq ORTIB ketganda).</item>
///   <item>Orqaga (tanlangan oydan oldingi qarzga) hech narsa ketmaydi — kassir oyni o'zi tanlagan.</item>
/// </list>
/// </summary>
public static class PaymentMonthSplit
{
    public readonly record struct Part(string Month, decimal Amount);

    /// <param name="rows">Per-guruh hisob qatorlari (oy, qoldiq) — o'sish tartibida.</param>
    /// <param name="selectedMonth">Kassir tanlagan oy ("yyyy-MM").</param>
    /// <param name="amount">To'lov summasi (musbat).</param>
    public static List<Part> Allocate(IReadOnlyList<(string Month, decimal Remaining)> rows, string selectedMonth, decimal amount)
    {
        var single = new List<Part> { new(selectedMonth, amount) };
        var start = -1;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Month == selectedMonth) { start = i; break; }
        if (start < 0 || rows[start].Remaining <= 0m || amount <= rows[start].Remaining) return single;

        var parts = new List<Part>();
        var left = amount;
        for (var i = start; i < rows.Count && left > 0m; i++)
        {
            // Oxirgi qator qolgan hammasini oladi (avans) — pul hech qachon "taqsimlanmay" qolmaydi.
            var take = i == rows.Count - 1 ? left : Math.Min(Math.Max(0m, rows[i].Remaining), left);
            if (take <= 0m) continue;
            parts.Add(new Part(rows[i].Month, take));
            left -= take;
        }
        return parts.Count == 0 ? single : parts;
    }

    /// <summary>Audit/izoh uchun qisqa matn: «2026-09: 20 000, 2026-10: 205 000».</summary>
    public static string Describe(IEnumerable<Part> parts) =>
        string.Join(", ", parts.Select(p => $"{p.Month}: {AuditService.Money(p.Amount)}"));
}
