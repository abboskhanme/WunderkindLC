using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Moliyaviy hisobot KURS va GURUH kesimida: qaysi kurs ko'p daromad keltiradi, qaysi kurs
/// o'quvchilari to'lovni to'liq amalga oshirdi, qaysi guruh (o'qituvchi) to'lov yig'ishda faolroq.
///
/// DAVR = OYLAR bo'yicha: to'lov QAYSI OY UCHUN qilingani (FinanceTransaction.Month) hisobga olinadi,
/// pul qachon kassaga tushgani EMAS. Sabab: "Hisoblangan" (MonthlyCharge) ham oy bo'yicha — ikkalasi
/// bir asosda bo'lmasa, kelasi oy uchun oldindan (avans) to'langan pul joriy davrni shishirib, yig'ilish
/// foizini 100% dan oshirib yuborardi va keyingi oyda o'sha o'quvchi "to'lamagan" bo'lib ko'rinardi.
/// O'quvchining "To'lov tarixi" (StudentGroupLedger) allaqachon shu asosda ishlaydi. Eski, oyi
/// belgilanmagan yozuvlar uchun sana oralig'iga tushiladi. (Moliya → Umumiy va To'lovlar jadvali —
/// aksincha, HAQIQIY pul oqimi: ular sana bo'yicha qoladi.)
///
/// "Yig'ilgan" (collected) = shu davr OYLARI uchun kelgan tuition to'lovlari, guruhga tegishli qilib:
///   • teglangan to'lov (FinanceTransaction.GroupId bor) → 100% o'sha guruhga;
///   • teglanmagan to'lov → o'quvchining SHU OYDAGI billable guruhlari oylik narxi (MonthlyFee)
///     nisbatida taqsimlanadi (maosh hisobidagi bilan bir xil mantiq — SalaryLedger).
/// "Hisoblangan" (billed) = MonthlyCharge (Amount − Discount), per-guruh (GroupId) yozuvlaridan.
/// "To'liq to'lagan" = o'quvchining shu guruh uchun yig'ilgani ≥ hisoblangani (billed > 0 bo'lganlar ichida).
/// </summary>
public static class CourseFinanceReport
{
    public static async Task<CourseFinanceReportDto> BuildAsync(
        IAppDbContext db, string? from, string? to)
    {
        var fromDate = string.IsNullOrEmpty(from)
            ? $"{await TuitionService.AcademicYearStartMonthAsync(db)}-01"
            : from;
        var toDate = string.IsNullOrEmpty(to) ? $"{AppClock.Today:yyyy-MM-dd}" : to;
        var fromMonth = fromDate.Length >= 7 ? fromDate[..7] : fromDate;
        var toMonth = toDate.Length >= 7 ? toDate[..7] : toDate;
        var monthsSet = TuitionService.MonthRange(fromMonth, toMonth).ToHashSet();

        // ---- Guruhlar / kurslar / o'qituvchilar ----
        var groups = await db.Classes.ToListAsync();
        var feeByGroup = groups.ToDictionary(g => g.Id, g => g.MonthlyFee);

        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s);
        var teacherNames = await db.Teachers.ToDictionaryAsync(t => t.Id, t => t.FullName);

        // ---- A'zoliklar (taqsimlash maxraji + o'quvchi sanog'i uchun) ----
        var memberships = await db.StudentGroups.ToListAsync();
        var membsByStudent = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Guruhdagi faol o'quvchilar soni (arxivlanmagan a'zolik).
        var activeStudentsByGroup = memberships
            .Where(m => m.IsActive)
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).Distinct().Count());

        // ---- HISOBLANGAN (billed): per-guruh MonthlyCharge ----
        var charges = await db.MonthlyCharges
            .Where(c => c.GroupId != null)
            .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount })
            .ToListAsync();
        var billedByGroup = new Dictionary<string, decimal>();
        var billedBySg = new Dictionary<(string, string), decimal>();
        foreach (var c in charges)
        {
            if (!monthsSet.Contains(c.Month) || c.GroupId is null) continue;
            var net = c.Amount - c.Discount;
            if (net <= 0) continue;
            billedByGroup[c.GroupId] = billedByGroup.GetValueOrDefault(c.GroupId) + net;
            var key = (c.StudentId, c.GroupId);
            billedBySg[key] = billedBySg.GetValueOrDefault(key) + net;
        }

        // ---- YIG'ILGAN (collected): tuition to'lovlarini guruhga tegishli qilish. VOZVRAT (expense+refund)
        // MANFIY qo'shiladi — "yig'ilgan" net (to'langan − vozvrat) bo'ladi (o'qituvchi maoshi bilan izchil). ----
        var payments = await LoadPaymentsAsync(db, fromDate, toDate, monthsSet.ToList());

        var collectedByGroup = new Dictionary<string, decimal>();
        var cashByGroup = new Dictionary<string, decimal>();
        var collectedBySg = new Dictionary<(string, string), decimal>();
        var untaggedByStudentMonth = new Dictionary<(string Sid, string Month), (decimal Total, decimal Cash)>();

        void AddCollected(string groupId, string studentId, decimal amount, decimal cash)
        {
            collectedByGroup[groupId] = collectedByGroup.GetValueOrDefault(groupId) + amount;
            cashByGroup[groupId] = cashByGroup.GetValueOrDefault(groupId) + cash;
            var key = (studentId, groupId);
            collectedBySg[key] = collectedBySg.GetValueOrDefault(key) + amount;
        }

        foreach (var p in payments)
        {
            if (p.Amount == 0m) continue;   // vozvrat manfiy — o'tkazib yubormaymiz
            var month = p.Month;
            var cash = p.IsCash ? p.Amount : 0m;
            if (!string.IsNullOrEmpty(p.GroupId))
                AddCollected(p.GroupId, p.StudentId, p.Amount, cash);
            else
            {
                var key = (p.StudentId, month);
                var cur = untaggedByStudentMonth.GetValueOrDefault(key);
                untaggedByStudentMonth[key] = (cur.Total + p.Amount, cur.Cash + cash);
            }
        }

        // Teglanmagan to'lovni narx nisbatida billable guruhlarga taqsimlash (vozvratdan keyin manfiy ham bo'lishi mumkin).
        foreach (var ((sid, month), (amount, cashAmount)) in untaggedByStudentMonth)
        {
            if (amount == 0m && cashAmount == 0m) continue;
            if (!membsByStudent.TryGetValue(sid, out var membs)) continue;
            var active = membs.Where(m => BillableInMonth(m, month)).ToList();
            var denom = active.Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
            if (denom <= 0) continue;
            foreach (var m in active)
            {
                var fee = feeByGroup.GetValueOrDefault(m.GroupId, 0m);
                if (fee <= 0) continue;
                AddCollected(m.GroupId, sid, amount * fee / denom, cashAmount * fee / denom);
            }
        }

        // ---- GURUH qatorlari ----
        var groupRows = new List<GroupFinanceRowDto>();
        foreach (var g in groups)
        {
            var billed = decimal.Round(billedByGroup.GetValueOrDefault(g.Id), 2);
            var collected = decimal.Round(collectedByGroup.GetValueOrDefault(g.Id), 2);
            var cash = decimal.Round(cashByGroup.GetValueOrDefault(g.Id), 2);
            var students = activeStudentsByGroup.GetValueOrDefault(g.Id, 0);
            // Bu davrda hech qanday faollik bo'lmagan bo'sh guruhni chiqarib tashlaymiz.
            if (billed <= 0 && collected <= 0 && students == 0) continue;

            // To'liq to'lagan / billable o'quvchilar (shu guruh uchun).
            var sgStudents = billedBySg.Keys.Where(k => k.Item2 == g.Id).Select(k => k.Item1).ToHashSet();
            var billable = 0;
            var fullyPaid = 0;
            foreach (var sid in sgStudents)
            {
                var b = billedBySg.GetValueOrDefault((sid, g.Id));
                if (b <= 0) continue;
                billable++;
                var c = collectedBySg.GetValueOrDefault((sid, g.Id));
                if (c + 0.5m >= b) fullyPaid++;
            }

            var courseName = subjects.TryGetValue(g.CourseId, out var subj) ? subj.Name : "—";
            var teacherName = teacherNames.GetValueOrDefault(g.TeacherId, "—");
            var pct = billed > 0 ? decimal.Round(collected / billed * 100m, 1) : 0m;

            groupRows.Add(new GroupFinanceRowDto(
                g.Id, g.Name, courseName, g.TeacherId, teacherName,
                students, billed, collected, cash, pct, fullyPaid, billable));
        }

        // ---- KURS qatorlari (guruhlarni CourseId bo'yicha jamlash) ----
        var courseRows = new List<CourseFinanceRowDto>();
        var byCourse = groups.GroupBy(g => g.CourseId);
        foreach (var grp in byCourse)
        {
            var courseId = grp.Key;
            if (string.IsNullOrEmpty(courseId)) continue;
            var gids = grp.Select(g => g.Id).ToHashSet();

            var billed = decimal.Round(gids.Sum(id => billedByGroup.GetValueOrDefault(id)), 2);
            var collected = decimal.Round(gids.Sum(id => collectedByGroup.GetValueOrDefault(id)), 2);
            if (billed <= 0 && collected <= 0) continue;

            // Kurs o'quvchilari (shu kurs guruhlaridagi faol a'zolar, takrorlanmas).
            var studentCount = memberships
                .Where(m => m.IsActive && gids.Contains(m.GroupId))
                .Select(m => m.StudentId).Distinct().Count();

            // To'liq to'lagan / billable — shu kurs guruhlari bo'yicha (har (o'quvchi,guruh) bir marta).
            var billable = 0;
            var fullyPaid = 0;
            foreach (var key in billedBySg.Keys.Where(k => gids.Contains(k.Item2)))
            {
                var b = billedBySg.GetValueOrDefault(key);
                if (b <= 0) continue;
                billable++;
                var c = collectedBySg.GetValueOrDefault(key);
                if (c + 0.5m >= b) fullyPaid++;
            }

            var name = subjects.TryGetValue(courseId, out var subj) ? subj.Name : "—";
            var price = subj?.Price ?? 0m;
            var pct = billed > 0 ? decimal.Round(collected / billed * 100m, 1) : 0m;
            var paidPct = billable > 0 ? decimal.Round((decimal)fullyPaid / billable * 100m, 1) : 0m;

            courseRows.Add(new CourseFinanceRowDto(
                courseId, name, price, grp.Count(), studentCount,
                billed, collected, pct, fullyPaid, billable, paidPct));
        }

        var totalBilled = decimal.Round(billedByGroup.Values.Sum(), 2);
        var totalCollected = decimal.Round(collectedByGroup.Values.Sum(), 2);
        var totalCash = decimal.Round(cashByGroup.Values.Sum(), 2);
        var collectionPct = totalBilled > 0 ? decimal.Round(totalCollected / totalBilled * 100m, 1) : 0m;

        return new CourseFinanceReportDto(
            fromDate, toDate, totalBilled, totalCollected, totalCash, collectionPct,
            courseRows.OrderByDescending(c => c.Collected).ToList(),
            groupRows.OrderByDescending(g => g.Collected).ToList());
    }

    /// <summary>
    /// BITTA guruh ichidagi to'lov holati: har bir o'quvchi uchun davr bo'yicha hisoblangan/yig'ilgan
    /// va to'liq to'laganmi (kim to'ladi / kim to'lamadi).
    /// </summary>
    public static async Task<GroupPaymentsReportDto> BuildGroupPaymentsAsync(
        IAppDbContext db, string groupId, string? from, string? to)
    {
        var group = await db.Classes.FirstOrDefaultAsync(g => g.Id == groupId);
        return group is null
            ? await BuildPaymentsAsync(db, groupId, "—", new List<string>(), from, to)
            : await BuildPaymentsAsync(db, groupId, group.Name, new List<string> { groupId }, from, to);
    }

    /// <summary>
    /// O'QITUVCHINING BARCHA guruhlari bo'yicha YAGONA to'lov holati — ko'rinishi bitta guruhnikidek
    /// (Moliya → Guruhlar → o'qituvchi tanlanganda "Barchasini ko'rish"). Bir nechta guruhda o'qiydigan
    /// o'quvchi BITTA qatorda jamlanadi: hisoblangan/yig'ilgan shu o'qituvchi guruhlari bo'yicha qo'shiladi.
    /// </summary>
    public static async Task<GroupPaymentsReportDto> BuildTeacherPaymentsAsync(
        IAppDbContext db, string teacherId, string? from, string? to)
    {
        var teacherName = await db.Teachers.Where(t => t.Id == teacherId)
            .Select(t => t.FullName).FirstOrDefaultAsync() ?? "—";
        var groupIds = await db.Classes.Where(c => c.TeacherId == teacherId).Select(c => c.Id).ToListAsync();
        return await BuildPaymentsAsync(db, teacherId, teacherName, groupIds, from, to);
    }

    /// <summary>
    /// Bir yoki bir nechta guruh bo'yicha per-o'quvchi to'lov holati (yagona mantiq — guruh ham,
    /// o'qituvchi ham shu yerdan chiqadi). Yig'ilgan logikasi <see cref="BuildAsync"/> bilan bir xil:
    /// teglangan to'lov 100% o'z guruhiga, teglanmagan to'lov esa o'quvchining shu oydagi billable
    /// guruhlari narxi nisbatida taqsimlanadi — bu yerda MAQSADLI guruhlarga tushgan ulush olinadi.
    /// </summary>
    private static async Task<GroupPaymentsReportDto> BuildPaymentsAsync(
        IAppDbContext db, string reportId, string reportName, List<string> groupIds, string? from, string? to)
    {
        var fromDate = string.IsNullOrEmpty(from)
            ? $"{await TuitionService.AcademicYearStartMonthAsync(db)}-01"
            : from;
        var toDate = string.IsNullOrEmpty(to) ? $"{AppClock.Today:yyyy-MM-dd}" : to;
        var fromMonth = fromDate.Length >= 7 ? fromDate[..7] : fromDate;
        var toMonth = toDate.Length >= 7 ? toDate[..7] : toDate;
        var monthsSet = TuitionService.MonthRange(fromMonth, toMonth).ToHashSet();

        if (groupIds.Count == 0)
            return new GroupPaymentsReportDto(reportId, reportName, fromDate, toDate, 0, 0, 0, 0, 0, new());

        var targetIds = groupIds.ToHashSet();
        // Narxlar — teglanmagan to'lovni maqsadli guruh(lar) ulushiga taqsimlash uchun (BARCHA guruh kerak:
        // maxraj o'quvchining hamma billable guruhlaridan yig'iladi).
        var feeByGroup = await db.Classes.ToDictionaryAsync(g => g.Id, g => g.MonthlyFee);

        var memberships = await db.StudentGroups.ToListAsync();
        var membsByStudent = memberships.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        // HISOBLANGAN (billed) — maqsadli guruh(lar) MonthlyCharge'laridan, davr ichida.
        var charges = await db.MonthlyCharges
            .Where(c => c.GroupId != null && groupIds.Contains(c.GroupId))
            .Select(c => new { c.StudentId, c.Month, c.Amount, c.Discount })
            .ToListAsync();
        var billedByStudent = new Dictionary<string, decimal>();
        foreach (var c in charges)
        {
            if (!monthsSet.Contains(c.Month)) continue;
            var net = c.Amount - c.Discount;
            if (net <= 0) continue;
            billedByStudent[c.StudentId] = billedByStudent.GetValueOrDefault(c.StudentId) + net;
        }

        // YIG'ILGAN (collected) — maqsadli guruh(lar)ga tegishli ulush. VOZVRAT manfiy (net).
        var payments = await LoadPaymentsAsync(db, fromDate, toDate, monthsSet.ToList());
        var collectedByStudent = new Dictionary<string, decimal>();
        foreach (var p in payments)
        {
            if (p.Amount == 0m) continue;   // vozvrat manfiy
            if (!string.IsNullOrEmpty(p.GroupId))
            {
                if (targetIds.Contains(p.GroupId))
                    collectedByStudent[p.StudentId] = collectedByStudent.GetValueOrDefault(p.StudentId) + p.Amount;
                continue;
            }
            // Teglanmagan — narx nisbatida maqsadli guruh(lar)ga tushadigan ulushni olamiz.
            if (!membsByStudent.TryGetValue(p.StudentId, out var membs)) continue;
            var active = membs.Where(m => BillableInMonth(m, p.Month)).ToList();
            var denom = active.Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
            if (denom <= 0) continue;
            var share = active.Where(m => targetIds.Contains(m.GroupId))
                .Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
            if (share <= 0) continue;
            collectedByStudent[p.StudentId] =
                collectedByStudent.GetValueOrDefault(p.StudentId) + p.Amount * share / denom;
        }

        // Ko'rsatiladigan o'quvchilar: maqsadli guruh(lar) faol a'zolari + hisoblangani/yig'ilgani borlar.
        var sids = new HashSet<string>(memberships
            .Where(m => targetIds.Contains(m.GroupId) && m.IsActive).Select(m => m.StudentId));
        foreach (var k in billedByStudent.Keys) sids.Add(k);
        foreach (var k in collectedByStudent.Keys) sids.Add(k);

        var nameById = (await db.Students.Where(s => sids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName }).ToListAsync())
            .ToDictionary(s => s.Id, s => s.FullName);
        // Ko'p guruhli o'quvchida ENG "kuchli" holat ko'rsatiladi: aktiv → (sinov) → muzlatilgan.
        var statusByStudent = memberships
            .Where(m => targetIds.Contains(m.GroupId))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => (
                g.FirstOrDefault(m => m.Status == "active")
                ?? g.FirstOrDefault(m => m.IsActive)
                ?? g.First()).Status);

        var rows = sids.Select(sid =>
        {
            var billed = decimal.Round(billedByStudent.GetValueOrDefault(sid), 2);
            var collected = decimal.Round(collectedByStudent.GetValueOrDefault(sid), 2);
            var debt = decimal.Round(Math.Max(0m, billed - collected), 2);
            var fullyPaid = billed > 0 && collected + 0.5m >= billed;
            return new GroupPaymentRowDto(
                sid, nameById.GetValueOrDefault(sid, "—"), statusByStudent.GetValueOrDefault(sid, ""),
                billed, collected, debt, fullyPaid, collected > 0);
        })
        .OrderByDescending(r => r.Debt)
        .ThenByDescending(r => r.Billed)
        .ThenBy(r => r.FullName)
        .ToList();

        return new GroupPaymentsReportDto(
            reportId, reportName, fromDate, toDate,
            decimal.Round(billedByStudent.Values.Sum(), 2),
            decimal.Round(collectedByStudent.Values.Sum(), 2),
            rows.Count(r => r.Billed > 0 && r.FullyPaid),
            rows.Count(r => r.Billed > 0 && !r.FullyPaid),
            rows.Count, rows);
    }

    /// <summary>Davr OYLARIGA tegishli tuition to'lovlari + vozvratlar (vozvrat MANFIY — "yig'ilgan" net
    /// bo'ladi). Tanlash to'lovning <see cref="FinanceTransaction.Month"/> (qaysi oy uchun to'langani)
    /// bo'yicha — sana bo'yicha EMAS, aks holda avans joriy davrni shishirardi; oyi belgilanmagan eski
    /// yozuvlar uchungina sana oralig'i ishlatiladi.
    /// IsCash — to'lov usuli naqdmi ("Naqd jami" kartasi uchun); vozvrat yozuvida Method odatda bo'sh
    /// (eski yozuvlar) — shuning uchun ASL to'lovnikidan olinadi, aks holda naqd qaytarilgan pul
    /// naqd jamidan ayrilmay qolardi.</summary>
    private static async Task<List<PaymentRow>> LoadPaymentsAsync(
        IAppDbContext db, string fromDate, string toDate, List<string> months)
    {
        var raw = await db.FinanceTransactions
            .Where(t => t.StudentId != null
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund"))
                        && ((t.Month != null && months.Contains(t.Month))
                            || (t.Month == null
                                && string.Compare(t.Date, fromDate) >= 0
                                && string.Compare(t.Date, toDate) <= 0)))
            .Select(t => new { StudentId = t.StudentId!, t.GroupId, t.Date, t.Month, t.Amount, t.Direction, t.Method, t.RefundOfId })
            .ToListAsync();

        var originIds = raw.Where(r => r.Method is null && r.RefundOfId != null)
            .Select(r => r.RefundOfId!).Distinct().ToList();
        var originMethod = originIds.Count == 0
            ? new Dictionary<string, string?>()
            : await db.FinanceTransactions.Where(t => originIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Method })
                .ToDictionaryAsync(x => x.Id, x => x.Method);

        return raw.Select(r =>
        {
            var method = r.Method ?? (r.RefundOfId is null ? null : originMethod.GetValueOrDefault(r.RefundOfId));
            var month = string.IsNullOrEmpty(r.Month)
                ? (r.Date.Length >= 7 ? r.Date[..7] : r.Date)
                : r.Month;
            return new PaymentRow(
                r.StudentId, r.GroupId, month,
                r.Direction == "expense" ? -r.Amount : r.Amount,
                method == "cash");
        }).ToList();
    }

    /// <summary>Hisobot uchun normallashtirilgan to'lov qatori: Month — to'lov QAYSI OY uchun ekani
    /// (oyi yo'q eski yozuvda sanadan olinadi), Amount — vozvrat bo'lsa manfiy.</summary>
    private sealed record PaymentRow(string StudentId, string? GroupId, string Month, decimal Amount, bool IsCash);

    /// <summary>A'zolik shu oyda hisob-kitobga kiradimi — <b>YAGONA TA'RIF</b>ga delegat qiladi.
    /// <para>⚠️ Ilgari bu yerda mantiq QAYTA YOZILGAN edi (belgi-ma-belgi bir xil nusxa).
    /// <c>MembershipLifecycle</c> yopilgan faol davrlarni (<c>StudentGroup.PastPeriods</c>) hisobga
    /// oladigan bo'lgach, nusxa jimgina eski xatti-harakatda qolib, bu hisobot maosh va bonusdan
    /// AJRALIB ketardi. Nusxa QAYTA TIKLANMASIN.</para></summary>
    private static bool BillableInMonth(StudentGroup m, string month) =>
        MembershipLifecycle.BillableInMonth(m, month);
}
