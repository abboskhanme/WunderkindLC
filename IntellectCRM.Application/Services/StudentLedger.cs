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
        var classNameGroup = await db.Classes.FirstOrDefaultAsync(c => c.Name == student.ClassName);
        var rawFee = classNameGroup?.MonthlyFee ?? 0m;
        // Joriy effektiv oylik (yangi oy uchun nima hisoblanadi) — chegirma ayirilgan.
        // Joriy effektiv oylik — chegirma faqat amal qilish davrida (DiscountStartMonth..EndMonth) qo'llanadi.
        // Guruh konteksti — asosiy (ClassName) guruh: chegirma boshqa guruhga biriktirilgan bo'lsa 0.
        // Chegirma REGISTRDAN (`.claude/rules/discounts.md`): bitta o'quvchi — bitta yengil so'rov.
        var discounts = (await DiscountBook.LoadForStudentAsync(db, student.Id)).For(student.Id);
        var fee = rawFee - TuitionService.DiscountForMonth(
            discounts, rawFee, TuitionService.CurrentMonth(), classNameGroup?.Id);

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

        var totalCharged = charges.Sum(c => c.Amount);          // to'liq narx
        var totalDiscount = charges.Sum(c => c.Discount);       // jami chegirma
        var totalPaidActual = payments.Sum(p => p.Amount);      // haqiqiy naqd

        // To'lovni oylarga taqsimlash (allokatsiya):
        //   1) Aniq oyga biriktirilgan to'lov o'sha oyning EFFEKTIV summasidan oshmagan holda yoziladi;
        //   2) qolgan pul (oysiz to'lovlar + ortgani) eng eski qarzdan boshlab (FIFO) taqsimlanadi.
        // Effektiv summa = Amount − Discount; status shunga qarab.
        var paidByMonth = payments
            .Where(p => !string.IsNullOrEmpty(p.Month))
            .GroupBy(p => p.Month!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var pool = payments.Where(p => string.IsNullOrEmpty(p.Month)).Sum(p => p.Amount);

        var alloc = new decimal[charges.Count];
        for (var i = 0; i < charges.Count; i++)
        {
            var effective = charges[i].Amount - charges[i].Discount;
            if (effective < 0) effective = 0;
            if (!paidByMonth.TryGetValue(charges[i].Month, out var explicitPaid)) continue;
            var applied = Math.Min(explicitPaid, effective);
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

        return new StudentLedgerDto(
            Map(student), student.Balance, fee,
            totalCharged, totalDiscount, totalPaidActual,
            months, paymentDtos);
    }

    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance,
        s.DiscountPct, s.DiscountAmount, s.DiscountNote);
}
