using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// O'quvchi to'lov tarixi (ledger): oylar bo'yicha hisoblangan/chegirma/to'langan holat va to'lovlar ro'yxati.
/// Admin moliya bo'limi ham, o'quvchi (oila) ilovasi ham shu yagona mantiqdan foydalanadi.
///
/// MODEL: MonthlyCharge.Amount — to'liq oylik (guruh narxi), MonthlyCharge.Discount — chegirma.
/// Haqiqiy to'lash kerak bo'lgan summa har oy uchun: effective = Amount − Discount.
/// "Paid" — sof naqd to'lovlar (FinanceTransaction[income, tuition]), chegirma ta'sir qilmaydi.
/// </summary>
public static class StudentLedger
{
    /// <summary>Oy bo'yicha aggregate hisob (barcha guruhlar yig'indisi) — to'lov allokatsiyasi uchun.</summary>
    private record ChargeRow(string Month, decimal Amount, decimal Discount, string? GroupId = null);

    public static async Task<StudentLedgerDto> BuildAsync(IAppDbContext db, Student student)
    {
        // JORIY EFFEKTIV OYLIK ("keyingi oy nima hisoblanadi") — o'quvchining BARCHA AKTIV guruh
        // a'zoliklari narxlari YIG'INDISI, chegirma HAR GURUH uchun alohida ayrilgan
        // (`TuitionService.AccrueMonth` bilan AYNAN bir xil qoida: sinov va muzlatilgan
        // a'zoliklarga oylik hisoblanmaydi).
        //
        // ⚠️ Ilgari bu yerda faqat `Student.ClassName` guruhining narxi olinardi. U BIRINCHI
        // qo'shilgan guruhda qotib qoladi, ya'ni: ikki kursda o'qiyotgan o'quvchi "oylik 500 000"
        // ko'rardi (jadvalda esa 900 000 hisoblangan), eski guruhida muzlatilgan o'quvchiga esa
        // hamon o'sha guruh narxi ko'rsatilardi. ⚠️ FAQAT KO'RSATISH — hisob/balans bu yerda
        // O'ZGARMAYDI (`StudentLedgerDto.MonthlyFee` boshqa hech qayerda ishlatilmaydi).
        var classNameGroup = await db.Classes.FirstOrDefaultAsync(c => c.Name == student.ClassName);
        var discounts = (await DiscountBook.LoadForStudentAsync(db, student.Id)).For(student.Id);
        var currentMonth = TuitionService.CurrentMonth();
        var liveMemberships = await StudentMembershipView.LiveMembershipsAsync(db, student.Id);
        var billableGroupIds = liveMemberships
            .Where(m => m.Status == "active").Select(m => m.GroupId).Distinct().ToList();
        var billableGroups = billableGroupIds.Count == 0
            ? new List<Group>()
            : await db.Classes.Where(c => billableGroupIds.Contains(c.Id)).ToListAsync();
        decimal fee;
        if (billableGroups.Count > 0)
        {
            fee = billableGroups.Sum(g =>
                g.MonthlyFee - TuitionService.DiscountForMonth(discounts, g.MonthlyFee, currentMonth, g.Id));
        }
        else
        {
            // ORQAGA MOSLIK: aktiv a'zolik yo'q (yoki umuman a'zolik yo'q) — eski ClassName narxi.
            var rawFee = classNameGroup?.MonthlyFee ?? 0m;
            fee = rawFee - TuitionService.DiscountForMonth(
                discounts, rawFee, currentMonth, classNameGroup?.Id);
        }

        // Per-guruh hisoblar — bir oyda bir nechta (har guruh) bo'lishi mumkin; oy bo'yicha aggregate qilamiz.
        var chargeRows = await db.MonthlyCharges.Where(c => c.StudentId == student.Id).ToListAsync();
        var charges = chargeRows
            .GroupBy(c => c.Month)
            .Select(g => new ChargeRow(g.Key, g.Sum(c => c.Amount), g.Sum(c => c.Discount), g.First().GroupId))
            .OrderBy(c => c.Month).ToList();

        // Kurs breakdown uchun: guruh id → kurs nomi (perGroup hisob qatorlaridan quriladi).
        var chargeGroupIds = chargeRows.Where(c => c.GroupId != null).Select(c => c.GroupId!).Distinct().ToList();
        var groupsById = (await db.Classes.Where(c => chargeGroupIds.Contains(c.Id)).ToListAsync())
            .ToDictionary(c => c.Id);
        var nameGroup = await db.Classes.FirstOrDefaultAsync(c => c.Name == student.ClassName);
        var courseIds = groupsById.Values.Select(g => g.CourseId)
            .Concat(nameGroup is null ? Array.Empty<string>() : new[] { nameGroup.CourseId })
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var courseNames = (await db.Subjects.Where(s => courseIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id, s => s.Name);

        List<MonthCourseDto> CoursesForMonth(string month)
        {
            // Shu oyning per-guruh hisob qatorlari — har biri guruh (kurs) nomi + summasi.
            return chargeRows.Where(c => c.Month == month).Select(c =>
            {
                // GURUH nomi — asosiy ko'rsatkich (o'quvchi profilida "Guruh — Kurs" bo'lib chiqadi),
                // kurs nomi esa yonida. Kurs biriktirilmagan bo'lsa faqat guruh nomi qoladi.
                if (c.GroupId is null)
                {
                    var gName = nameGroup?.Name ?? student.ClassName;
                    var cName = nameGroup is null || string.IsNullOrEmpty(nameGroup.CourseId)
                        ? "" : courseNames.GetValueOrDefault(nameGroup.CourseId, "");
                    return new MonthCourseDto(
                        string.IsNullOrEmpty(cName) ? (string.IsNullOrEmpty(gName) ? "—" : gName) : cName,
                        c.Amount, null, string.IsNullOrEmpty(gName) ? null : gName);
                }
                if (!groupsById.TryGetValue(c.GroupId, out var g))
                    return new MonthCourseDto("—", c.Amount, c.GroupId);
                var course = string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, "");
                return new MonthCourseDto(
                    string.IsNullOrEmpty(course) ? g.Name : course, c.Amount, c.GroupId, g.Name);
            }).ToList();
        }
        var payments = await db.FinanceTransactions
            .Where(t => t.StudentId == student.Id && t.Direction == "income" && t.Category == "tuition")
            .OrderByDescending(t => t.Date).ToListAsync();

        // VOZVRATLAR (`expense + refund`) — ALOHIDA ro'yxat.
        // ⚠️ Ilgari ular umuman o'qilmasdi: `FinanceController.Refund` pulni `Student.Balance` dan
        // AYIRADI, lekin ledger faqat kirimni sanagani uchun oy hamon "to'langan" (yashil) bo'lib
        // ko'rinardi — profilning tepasida manfiy balans, ostida esa yashil oy turardi. Vozvrat
        // qatorlari to'lovlar RO'YXATIDA ko'rsatilmaydi (u "amalga oshirilgan to'lovlar" ro'yxati,
        // Moliya → "Vozvratlar" tabi alohida), lekin JAMI va oylarga taqsimlashda AYRILADI —
        // `GroupBalanceService`/`SalaryLedger` bilan bitta konvensiya.
        var refunds = await db.FinanceTransactions
            .Where(t => t.StudentId == student.Id && t.Direction == "expense" && t.Category == "refund")
            .Select(t => new { t.Month, t.Amount }).ToListAsync();

        var totalCharged = charges.Sum(c => c.Amount);          // to'liq narx
        var totalDiscount = charges.Sum(c => c.Discount);       // jami chegirma
        var totalPaidActual = payments.Sum(p => p.Amount) - refunds.Sum(r => r.Amount); // sof naqd

        // To'lovni oylarga taqsimlash (allokatsiya):
        //   1) Aniq oyga biriktirilgan to'lov o'sha oyning EFFEKTIV summasidan oshmagan holda yoziladi;
        //   2) qolgan pul (oysiz to'lovlar + ortgani) eng eski qarzdan boshlab (FIFO) taqsimlanadi.
        // Effektiv summa = Amount − Discount; status shunga qarab.
        var paidByMonth = payments
            .Where(p => !string.IsNullOrEmpty(p.Month))
            .GroupBy(p => p.Month!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        foreach (var r in refunds.Where(r => !string.IsNullOrEmpty(r.Month)))
            paidByMonth[r.Month!] = paidByMonth.GetValueOrDefault(r.Month!, 0m) - r.Amount;
        var pool = payments.Where(p => string.IsNullOrEmpty(p.Month)).Sum(p => p.Amount)
                   - refunds.Where(r => string.IsNullOrEmpty(r.Month)).Sum(r => r.Amount);

        // ── OYGA TEGLANGAN, LEKIN HISOB QATORI YO'Q TO'LOV — HOVUZGA ──
        // ⚠️ Halqa faqat HISOB qatorlari bo'yicha aylanadi, ya'ni `paidByMonth` dagi kalitning
        // mos hisob oyi bo'lmasa o'sha pul HECH QAYERDA ishlatilmasdi: tepada "jami to'langan"
        // ko'rinardi, oylar ro'yxatida esa hech bir qator uni ko'rsatmasdi va eski oylar hamon
        // "to'lanmagan" bo'lib turardi. Bunday yetim teg ikki yo'l bilan paydo bo'ladi:
        //   (1) `TuitionService.EnsureChargeAsync` narx 0 bo'lsa hisob YARATMAYDI, `PaymentIntake`
        //       esa oyni baribir yozadi;
        //   (2) ORQAGA sanalgan muzlatish `PurgeChargesAfterMonthAsync` bilan hisobni o'chiradi,
        //       to'lovning oy tegi esa joyida qoladi (guruh almashtirishdan farqli — u yerda
        //       `CarryGroupAdvanceAsync` pulni qayta teglaydi).
        // Yechim: yetim teglangan pul UMUMIY HOVUZGA qo'shiladi va FIFO bilan eng eski qarzdan
        // yopiladi — ya'ni pul KO'RINADI va ISHLAYDI. Jami raqamlar o'zgarmaydi.
        var chargeMonths = charges.Select(c => c.Month).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in paidByMonth)
            if (!chargeMonths.Contains(kv.Key)) pool += kv.Value;

        var alloc = new decimal[charges.Count];
        for (var i = 0; i < charges.Count; i++)
        {
            var effective = charges[i].Amount - charges[i].Discount;
            if (effective < 0) effective = 0;
            if (!paidByMonth.TryGetValue(charges[i].Month, out var explicitPaid)) continue;
            // Vozvrat tufayli oy neti MANFIY bo'lishi mumkin — "to'langan" manfiy chizilmasin
            // (farq hovuzga o'tadi, ya'ni jami pul yo'qolmaydi).
            var applied = Math.Max(0m, Math.Min(explicitPaid, effective));
            alloc[i] = applied;
            pool += explicitPaid - applied; // oy summasidan ortgani umumiy hovuzga qo'shiladi
        }
        for (var i = 0; i < charges.Count && pool > 0; i++)
        {
            var effective = charges[i].Amount - charges[i].Discount;
            if (effective < 0) effective = 0;
            var remaining = effective - alloc[i];
            if (remaining <= 0) continue;
            var extra = Math.Min(pool, remaining);
            alloc[i] += extra;
            pool -= extra;
        }

        var months = new List<MonthLedgerDto>();
        for (var i = 0; i < charges.Count; i++)
        {
            var c = charges[i];
            var effective = c.Amount - c.Discount;
            if (effective < 0) effective = 0;
            var paid = alloc[i];
            var remaining = effective - paid;
            if (remaining < 0) remaining = 0;
            string status;
            if (effective == 0) status = "paid";          // 100% chegirma — qarz yo'q
            else if (remaining <= 0) status = "paid";
            else if (paid > 0) status = "partial";
            else status = "unpaid";
            months.Add(new MonthLedgerDto(c.Month, c.Amount, c.Discount, paid, remaining, status, CoursesForMonth(c.Month), c.GroupId));
        }

        // To'lov QAYSI guruhga qilingani — guruh nomi + o'sha guruh o'qituvchisi (to'lov tarixida ko'rsatiladi).
        var payGroupIds = payments.Where(t => t.GroupId != null).Select(t => t.GroupId!).Distinct().ToList();
        var payGroups = await db.Classes.Where(c => payGroupIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.TeacherId, c.CourseId }).AsNoTracking().ToListAsync();
        var payTeacherIds = payGroups.Select(g => g.TeacherId).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        var payTeacherNames = (await db.Teachers.Where(t => payTeacherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FullName }).AsNoTracking().ToListAsync())
            .ToDictionary(t => t.Id, t => t.FullName);
        // To'lov guruhining KURSI (profil to'lov ro'yxatida "Guruh — Kurs" bo'lib chiqadi).
        var payCourseIds = payGroups.Select(g => g.CourseId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        var payCourseNames = (await db.Subjects.Where(s => payCourseIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name }).AsNoTracking().ToListAsync())
            .ToDictionary(s => s.Id, s => s.Name);
        var payGroupInfo = payGroups.ToDictionary(
            g => g.Id,
            g => (Name: g.Name,
                  Teacher: string.IsNullOrEmpty(g.TeacherId) ? null : payTeacherNames.GetValueOrDefault(g.TeacherId),
                  Course: string.IsNullOrEmpty(g.CourseId) ? null : payCourseNames.GetValueOrDefault(g.CourseId)));

        var paymentDtos = payments.Select(t =>
        {
            string? gName = null, tName = null, cName = null;
            if (t.GroupId != null && payGroupInfo.TryGetValue(t.GroupId, out var gi))
            {
                gName = gi.Name;
                tName = gi.Teacher;
                cName = gi.Course;
            }
            // NAQD bo'lsa — qog'oz kvitansiya raqami, KARTA bo'lsa — oxirgi 4 raqam + to'lov vaqti.
            // Ikkalasi ham to'lov oynasida kiritiladi; bu yerda faqat uzatiladi (tozalash/normalizatsiya
            // `PaymentFields` da, yozish paytida bo'lgan).
            return new PaymentDto(t.Date, t.Amount, t.Note, t.Month, t.Comment, t.Method, gName, tName, cName,
                t.ReceiptNo, t.PaidTime, t.CardLast4);
        }).ToList();

        // Sarlavhadagi guruh nomi ham TIRIK a'zoliklardan (`ClassName` faqat zaxira) — aks holda
        // "A guruh · oylik 900 000" kabi ZID sarlavha chiqardi (nomi bir guruhniki, summa esa
        // ikkala guruhniki).
        var groupLabel = string.Join(", ", await StudentMembershipView.DisplayGroupNamesAsync(db, student));

        return new StudentLedgerDto(
            Map(student, groupLabel), student.Balance, fee,
            totalCharged, totalDiscount, totalPaidActual,
            months, paymentDtos);
    }

    private static StudentDto Map(Student s, string? groupLabel = null) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone,
        string.IsNullOrEmpty(groupLabel) ? s.ClassName : groupLabel,
        s.EnrollmentDate, s.Balance,
        s.DiscountPct, s.DiscountAmount, s.DiscountNote);
}
