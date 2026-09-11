using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// PER-GURUH balans (qarz/avans) — guruh kontekstidagi ro'yxatlar uchun.
/// <para>Muammo: <see cref="Student.Balance"/> — o'quvchining BARCHA guruhlari bo'yicha UMUMIY balans.
/// O'quvchi 2+ guruhda o'qib, faqat bittasiga to'lasa, umumiy balans manfiy bo'lib qoladi va HAR IKKALA
/// o'qituvchi uni "qarzdor" (qizil) ko'rardi. Bu servis har guruh uchun ALOHIDA hisoblaydi: to'lagan
/// guruhida yashil, to'lamaganida qizil.</para>
/// <para>Formula (belgi <see cref="Student.Balance"/> bilan bir xil — manfiy = qarz):
/// <c>balans_g = (shu guruhga to'langan − vozvrat) − (shu guruh uchun hisoblangan)</c>, bunda
/// hisob = <see cref="MonthlyCharge"/> qatorlari (Amount − Discount) <c>GroupId == g</c>,
/// to'lov = <see cref="FinanceTransaction"/> (income+tuition, minus expense+refund) <c>GroupId == g</c>.</para>
/// <para>TEGLANMAGAN (GroupId=null — per-guruh billingdan OLDINGI eski yozuvlar) to'lov VA hisob guruhlar
/// oylik narxi (MonthlyFee) nisbatida taqsimlanadi — <see cref="SalaryLedger"/>dagi foizli maosh bazasi
/// bilan AYNAN bir xil konvensiya (bir xil pul ikki joyda turlicha taqsimlanmasin). Ikkala tomon ham BIR XIL
/// qoida bilan taqsimlangani muhim: eski (teglanmagan hisob + teglanmagan to'lov) juftligi bir-birini
/// to'liq so'ndiradi — to'lagan o'quvchi eski oylar tufayli qizil bo'lib qolmaydi.</para>
/// <para>Shu oyda BILLABLE a'zoligi bo'lmagan davr yozuvlari (masalan o'quvchi hali hech bir guruhda
/// bo'lmaganda yozilgan hisob) hech bir guruhga taqsimlanmaydi — ular umumiy balansda qoladi.</para>
/// </summary>
public static class GroupBalanceService
{
    /// <summary>Qarzdor deb hisoblash uchun eng kichik summa (so'm) — taqsimlashdan qoladigan
    /// tiyin-darajadagi farqlar "qarz oyi" bo'lib sanalmasin.</summary>
    private const decimal DebtEpsilon = 0.5m;

    /// <summary>Bitta guruh bo'yicha: studentId → shu guruhdagi balans (manfiy = qarz).
    /// Ro'yxatdagi HAR o'quvchi uchun kalit qaytadi (hisob/to'lovi bo'lmasa 0).</summary>
    public static async Task<Dictionary<string, decimal>> ForGroupAsync(
        IAppDbContext db, string groupId, IEnumerable<string> studentIds)
    {
        var detailed = await DetailedForGroupAsync(db, groupId, studentIds);
        return detailed.ToDictionary(kv => kv.Key, kv => kv.Value.Balance);
    }

    /// <summary>Balans + QARZDOR OYLAR SONI (shu guruh bo'yicha). Balans hisobi
    /// <see cref="ForGroupAsync"/> bilan AYNAN bir xil — bu yerda qo'shimcha ravishda hisob/to'lov
    /// OYMA-OY ham yig'iladi va to'liq yopilmagan oylar sanaladi (<see cref="StudentGroupLedger"/>
    /// dagi "unpaid/partial" qoidasi bilan bir xil konvensiya: oy uchun hisoblangan − o'sha oyga
    /// to'langan &gt; 0 bo'lsa — o'sha oy QARZ).</summary>
    public static async Task<Dictionary<string, GroupBalanceInfo>> DetailedForGroupAsync(
        IAppDbContext db, string groupId, IEnumerable<string> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var result = ids.ToDictionary(id => id, _ => 0m);
        if (ids.Count == 0 || string.IsNullOrEmpty(groupId))
            return result.ToDictionary(kv => kv.Key, kv => new GroupBalanceInfo(kv.Value, 0, "", false));

        // OYMA-OY tomon (faqat qarzdor oylarni sanash uchun; balans yuqoridagi `result`da).
        var owedByMonth = new Dictionary<(string StudentId, string Month), decimal>();
        var paidByMonth = new Dictionary<(string StudentId, string Month), decimal>();
        static void Add(Dictionary<(string, string), decimal> map, string sid, string month, decimal amount)
            => map[(sid, month)] = map.GetValueOrDefault((sid, month), 0m) + amount;

        // Teglanmagan (GroupId=null) hisob/to'lovlar: (o'quvchi, oy) → net summa. Oxirida narx
        // nisbatida taqsimlanadi (hisob manfiy, to'lov musbat — ikkalasi bir xil qoida bilan).
        var untagged = new Dictionary<(string StudentId, string Month), decimal>();
        // O'sha teglanmagan summalarning hisob va to'lov tomonlari ALOHIDA — oyma-oy qarzni
        // aniqlash uchun (net yig'indidan hisob/to'lovni ajratib bo'lmaydi).
        var untaggedOwed = new Dictionary<(string, string), decimal>();
        var untaggedPaid = new Dictionary<(string, string), decimal>();
        void AddUntagged(string studentId, string month, decimal amount) =>
            untagged[(studentId, month)] = untagged.GetValueOrDefault((studentId, month), 0m) + amount;

        // (1) Hisoblangan summa (chegirmadan keyin) — qarz tomoni. Shu guruhniki to'g'ridan-to'g'ri,
        //     teglanmagani (eski aggregate qator) taqsimlash uchun yig'iladi.
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => ids.Contains(c.StudentId) && (c.GroupId == groupId || c.GroupId == null))
            .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount })
            .ToListAsync();
        foreach (var c in charges)
        {
            if (!result.ContainsKey(c.StudentId)) continue;
            var effective = Math.Max(0m, c.Amount - c.Discount);
            if (effective == 0m) continue;
            if (c.GroupId == groupId)
            {
                result[c.StudentId] -= effective;
                if (c.Month.Length >= 7) Add(owedByMonth, c.StudentId, c.Month[..7], effective);
            }
            else if (c.Month.Length >= 7)
            {
                AddUntagged(c.StudentId, c.Month[..7], -effective);
                Add(untaggedOwed, c.StudentId, c.Month[..7], effective);
            }
        }

        // (2) To'lovlar (kirim tuition) va VOZVRATLAR (chiqim refund — manfiy).
        var movements = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId != null && ids.Contains(t.StudentId)
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund")))
            .Select(t => new { StudentId = t.StudentId!, t.GroupId, t.Month, t.Date, t.Amount, t.Direction })
            .ToListAsync();

        foreach (var m in movements)
        {
            var amount = m.Direction == "expense" ? -m.Amount : m.Amount;
            if (amount == 0m || !result.ContainsKey(m.StudentId)) continue;
            // To'lov qaysi OYGA tegishli (Month tegi bo'lsa u, aks holda to'lov sanasi).
            var month = m.Month is { Length: >= 7 } tagged ? tagged[..7]
                : m.Date.Length >= 7 ? m.Date[..7] : "";
            if (m.GroupId == groupId)
            {
                result[m.StudentId] += amount;
                if (month.Length > 0) Add(paidByMonth, m.StudentId, month, amount);
                continue;
            }
            if (!string.IsNullOrEmpty(m.GroupId)) continue; // boshqa guruhga teglangan — bizga tegishli emas
            if (month.Length == 0) continue;
            AddUntagged(m.StudentId, month, amount);
            Add(untaggedPaid, m.StudentId, month, amount);
        }

        // (3) Teglanmagan hisob/to'lovlarning shu guruhga tegishli ULUSHINI qo'shamiz (narx nisbatida).
        if (untagged.Count > 0 || untaggedOwed.Count > 0 || untaggedPaid.Count > 0)
        {
            var memberships = await db.StudentGroups.AsNoTracking()
                .Where(sg => ids.Contains(sg.StudentId)).ToListAsync();
            var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
            var feeByGroup = await db.Classes.AsNoTracking()
                .Where(c => groupIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.MonthlyFee);
            var byStudent = memberships.GroupBy(m => m.StudentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Shu guruhga tegadigan ulush (fee/denom) — (o'quvchi, oy) bo'yicha bir marta hisoblanadi.
            // Koeffitsient EMAS, ikkala son qaytariladi: `amount * fee / denom` tartibi eski hisob
            // bilan bit-ma-bit bir xil qolsin (balans o'zgarmasligi shart).
            var shareOf = new Dictionary<(string, string), (decimal Fee, decimal Denom)>();
            (decimal Fee, decimal Denom) Share(string sid, string month)
            {
                if (shareOf.TryGetValue((sid, month), out var cached)) return cached;
                (decimal Fee, decimal Denom) share = (0m, 0m);
                if (byStudent.TryGetValue(sid, out var membs))
                {
                    var billable = membs.Where(m => BillableInMonth(m, month)).ToList();
                    var denom = billable.Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
                    var fee = billable.Any(m => m.GroupId == groupId)
                        ? feeByGroup.GetValueOrDefault(groupId, 0m) : 0m;
                    // denom<=0 → shu oyda billable guruh yo'q; fee<=0 → bu guruh shu oyda billable emas.
                    if (denom > 0 && fee > 0) share = (fee, denom);
                }
                shareOf[(sid, month)] = share;
                return share;
            }

            foreach (var ((sid, month), amount) in untagged)
            {
                var (fee, denom) = Share(sid, month);
                if (denom <= 0) continue;
                result[sid] += amount * fee / denom;
            }
            // Oyma-oy tomonga ham AYNAN shu ulush bilan (qarzdor oylarni sanash uchun).
            foreach (var ((sid, month), amount) in untaggedOwed)
            {
                var (fee, denom) = Share(sid, month);
                if (denom > 0) Add(owedByMonth, sid, month, amount * fee / denom);
            }
            foreach (var ((sid, month), amount) in untaggedPaid)
            {
                var (fee, denom) = Share(sid, month);
                if (denom > 0) Add(paidByMonth, sid, month, amount * fee / denom);
            }
        }

        // (4) QARZDOR OYLAR: hisoblangan bor va o'sha oyga to'langani yetmagan oylar soni.
        //     Har oy MUSTAQIL — kelasi oyga to'langan avans o'tgan oy qarzini yopmaydi
        //     (StudentGroupLedger'dagi oy holati bilan bir xil qoida).
        var debtMonths = ids.ToDictionary(id => id, _ => 0);
        // Shu SIKLDAYOQ (qo'shimcha so'rovsiz): eng eski qarz oyi va joriy oyda qarz bor-yo'qligi —
        // jurnalning to'lov "darvozasi" (JournalPolicy.PaymentGate) shu ikkisiga tayanadi.
        var oldestDebt = ids.ToDictionary(id => id, _ => "");
        var debtThisMonth = ids.ToDictionary(id => id, _ => false);
        var currentMonth = TuitionService.CurrentMonth();
        foreach (var ((sid, month), owed) in owedByMonth)
        {
            if (owed <= 0 || !debtMonths.ContainsKey(sid)) continue;
            var paid = paidByMonth.GetValueOrDefault((sid, month), 0m);
            if (owed - paid <= DebtEpsilon) continue;
            debtMonths[sid] += 1;
            if (oldestDebt[sid].Length == 0 || string.CompareOrdinal(month, oldestDebt[sid]) < 0)
                oldestDebt[sid] = month;
            if (month == currentMonth) debtThisMonth[sid] = true;
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => new GroupBalanceInfo(
                decimal.Round(kv.Value, 2),
                debtMonths.GetValueOrDefault(kv.Key, 0),
                oldestDebt.GetValueOrDefault(kv.Key, ""),
                debtThisMonth.GetValueOrDefault(kv.Key, false)));
    }

    /// <summary>Shu guruh bo'yicha balans va to'liq yopilmagan (qarzdor) OYLAR soni.
    /// <para><c>Balance</c> — manfiy = qarz (belgi <see cref="Student.Balance"/> bilan bir xil).
    /// <c>DebtMonths</c> — nechta OY uchun qarz bor (2+ bo'lsa UI'da alohida rang bilan ajratiladi).
    /// Diqqat: balans manfiy bo'lmasa ham DebtMonths &gt; 0 bo'lishi mumkin (masalan avans keyingi
    /// oyga teglangan, o'tgan oy esa yopilmagan) — ular BOSHQA-BOSHQA ko'rsatkich.</para>
    /// <para><c>OldestDebtMonth</c> — qarzi yopilmagan ENG ESKI oy ("yyyy-MM"), qarz yo'q bo'lsa "".
    /// <c>DebtThisMonth</c> — JORIY oyning o'zida qarz bormi. Ikkalasi ham <c>DebtMonths</c> bilan
    /// BIR XIL siklda (qo'shimcha so'rovsiz) hisoblanadi va jurnalning to'lov "darvozasi" uchun kerak
    /// (<see cref="JournalPolicy.PaymentGate"/>).</para></summary>
    public readonly record struct GroupBalanceInfo(
        decimal Balance, int DebtMonths, string OldestDebtMonth = "", bool DebtThisMonth = false);

    /// <summary>A'zolik shu oyda hisob-kitobga kiradimi — <b>YAGONA TA'RIF</b>ga delegat qiladi.
    /// <para>⚠️ Ilgari bu yerda mantiq QAYTA YOZILGAN edi (belgi-ma-belgi bir xil nusxa).
    /// <c>MembershipLifecycle</c> yopilgan faol davrlarni (<c>StudentGroup.PastPeriods</c>) hisobga
    /// oladigan bo'lgach, nusxa jimgina eski xatti-harakatda qolib, bu hisobot maosh va bonusdan
    /// AJRALIB ketardi. Nusxa QAYTA TIKLANMASIN.</para></summary>
    private static bool BillableInMonth(StudentGroup m, string month) =>
        MembershipLifecycle.BillableInMonth(m, month);
}
