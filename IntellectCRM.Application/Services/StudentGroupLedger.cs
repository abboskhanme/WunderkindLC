using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// To'lov oynasi uchun BITTA guruh bo'yicha oylik hisob. Aggregate `MonthlyCharge`dan farqli — bu yerda
/// faqat shu guruhning oylik narxi (aktivlashtirish/muzlatish qisman oylari bilan) va shu guruhga TEGLANGAN
/// to'lovlar olinadi. Maqsad: o'quvchi bir nechta guruhda o'qisa, to'lov kiritishda tanlangan guruh bo'yicha
/// to'g'ri oy va summa ko'rsatish (boshqa guruhlar summasi aralashmasin).
/// </summary>
public static class StudentGroupLedger
{
    /// <summary>Avans uchun joriy oydan keyin ko'rsatiladigan oylar soni (kassir oldindan to'lay olishi uchun).</summary>
    private const int AdvanceMonths = 3;

    public static async Task<GroupLedgerDto> BuildAsync(
        IAppDbContext db, Student student, Group group, StudentGroup membership)
    {
        // Kurs nomi + bir dars yaxlit narxi (LessonPrice) — ikkalasi bitta so'rovda. LessonPrice qisman
        // oylar (aktivlashtirish/muzlatish) previewida TuitionService bilan BIR XIL formula uchun kerak.
        var course = string.IsNullOrEmpty(group.CourseId) ? null
            : await db.Subjects.Where(s => s.Id == group.CourseId)
                .Select(s => new { s.Name, s.LessonPrice }).FirstOrDefaultAsync();
        var courseName = course?.Name ?? group.Name;
        var lessonFee = course?.LessonPrice ?? 0m;

        var months = new List<GroupMonthDto>();
        // Sinov (trial) — to'lov hisoblanmaydi.
        if (membership.Status == "trial")
            return new GroupLedgerDto(group.Id, group.Name, courseName, months);

        // To'lovlar va hisoblar oraliqni hisoblashdan OLDIN o'qiladi — oylar oralig'i pastda AYNAN
        // shular bilan kengaytiriladi (qarang: "TARIXIY OYLAR").
        // Shu guruhga TEGLANGAN tuition to'lovlari — oy bo'yicha.
        var paidByMonth = (await db.FinanceTransactions
                .Where(t => t.StudentId == student.Id && t.GroupId == group.Id
                            && t.Direction == "income" && t.Category == "tuition" && t.Month != null)
                .ToListAsync())
            .GroupBy(t => t.Month!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Mavjud per-guruh hisoblar — HAQIQAT MANBAI (super-admin qo'lda tahrir/Locked shu yerda).
        var chargeByMonth = (await db.MonthlyCharges
                .Where(c => c.StudentId == student.Id && c.GroupId == group.Id)
                .ToListAsync())
            .GroupBy(c => c.Month).ToDictionary(g => g.Key, g => g.First());

        var current = TuitionService.CurrentMonth();
        var startMonth = membership.ActivatedAt.Length >= 7 ? membership.ActivatedAt[..7]
            : membership.JoinedAt.Length >= 7 ? membership.JoinedAt[..7] : current;
        var endMonth = current;
        // MUZLATILGAN yoki guruhdan CHIQARILGAN a'zolikda kelajak oy YO'Q — hisob muzlatish (guruh
        // yopilganda ham shu) yoki chiqish oyida to'xtaydi. Qarz shu sanadan keyin o'smaydi, lekin
        // shu oygacha bo'lgan qarzga to'lov qabul qilinaveradi.
        string? stopMonth = membership.FrozenAt.Length >= 7 ? membership.FrozenAt[..7] : null;
        if (!membership.IsActive && (membership.LeftAt ?? "").Length >= 7)
        {
            var leftMonth = membership.LeftAt![..7];
            if (stopMonth is null || string.CompareOrdinal(leftMonth, stopMonth) < 0) stopMonth = leftMonth;
        }
        if (stopMonth is not null)
        {
            if (string.CompareOrdinal(stopMonth, endMonth) < 0) endMonth = stopMonth;
        }
        else
            // AVANS: faol a'zolik uchun joriy oydan keyingi 3 oyni ham ko'rsatamiz — kassir
            // oldindan to'lay olsin (to'lov qilinsa o'sha oy hisobi EnsureCharge orqali ochiladi).
            for (var i = 0; i < AdvanceMonths; i++) endMonth = TuitionService.NextMonth(endMonth);

        // ── TARIXIY OYLAR: oraliq HAQIQATAN pul biriktirilgan oylar bilan KENGAYTIRILADI ──
        // A'zolik muzlatilib, keyin qayta aktivlashtirilganda `ActivatedAt` YANGI sana bilan
        // ustidan yoziladi (`ClassesController.ActivateCoreAsync`) — eski faol davrning izi
        // qolmaydi. Natijada muzlashdan OLDINGI to'lanmagan oylar bu oraliqdan tushib qolar va
        // kassir ularni to'lov oynasida umuman TANLAY OLMASDI, holbuki `MonthlyCharge` qatori
        // bazada turgani uchun qarz balansda ham, guruh ro'yxatida ham ko'rinib turardi
        // ("2 oy qarz", lekin to'lash mumkin emas).
        //
        // Shuning uchun oraliqqa mavjud HISOB qatorlari va shu guruhga TEGLANGAN to'lovlarning
        // oylari ham kiritiladi. Bu faqat KO'RSATUV oralig'i: hisob-kitob qoidalari
        // (`BillableInMonth`, accrual, muzlatish) tegilmaydi — o'ylab topilgan qarz ham
        // qo'shilmaydi (pastdagi `outside` shartiga qarang).
        var naturalStart = startMonth;
        var naturalEnd = endMonth;
        foreach (var m in chargeByMonth.Keys.Concat(paidByMonth.Keys))
        {
            if (m.Length < 7) continue;
            var mm = m[..7];
            if (string.CompareOrdinal(mm, startMonth) < 0) startMonth = mm;
            if (string.CompareOrdinal(mm, endMonth) > 0) endMonth = mm;
        }

        if (string.CompareOrdinal(startMonth, endMonth) > 0)
            return new GroupLedgerDto(group.Id, group.Name, courseName, months);

        foreach (var month in TuitionService.MonthRange(startMonth, endMonth))
        {
            decimal gross, discount;
            if (chargeByMonth.TryGetValue(month, out var ch))
            {
                // Hisob mavjud — uning summasi/chegirmasi (haqiqat).
                gross = ch.Amount;
                discount = ch.Discount;
            }
            else if (string.CompareOrdinal(month, naturalStart) < 0
                     || string.CompareOrdinal(month, naturalEnd) > 0)
            {
                // Kengaytirilgan (tarixiy/uzoq kelajak) oy va hisob qatori YO'Q — demak bu yerga
                // faqat TO'LOV tufayli tushdik. Hech narsa O'YLAB TOPILMAYDI (guruh oyligi
                // qo'yilsa, mavjud bo'lmagan qarz paydo bo'lardi): oy to'langan bo'lib ko'rinadi.
                gross = 0m;
                discount = 0m;
            }
            else
            {
                // Hisob hali yo'q (kelajak/avans yoki accrue qilinmagan) — guruh narxidan PREVIEW.
                if (membership.ActivatedAt.Length >= 10 && membership.ActivatedAt[..7] == month)
                    gross = ActivationGross(group, lessonFee, membership.ActivatedAt);
                else if (membership.FrozenAt.Length >= 10 && membership.FrozenAt[..7] == month)
                    gross = FreezeGross(group, lessonFee, membership.ActivatedAt, membership.FrozenAt);
                // Guruhdan CHIQARILGAN/TUGATGAN a'zolik — chiqish oyi ham muzlatish kabi QISMAN
                // (chiqish sanasigacha o'qilgan darslar), to'liq oylik EMAS.
                else if (!membership.IsActive && (membership.LeftAt ?? "").Length >= 10
                         && membership.LeftAt![..7] == month)
                    gross = FreezeGross(group, lessonFee, membership.ActivatedAt, membership.LeftAt!);
                else
                    gross = group.MonthlyFee;
                discount = TuitionService.DiscountForMonth(student, gross, month, group.Id);
            }
            var effective = gross - discount;
            if (effective < 0) effective = 0;

            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = effective - paid;
            if (remaining < 0) remaining = 0;
            var status = effective <= 0 || remaining <= 0 ? "paid" : paid > 0 ? "partial" : "unpaid";
            months.Add(new GroupMonthDto(month, effective, paid, remaining, status));
        }
        return new GroupLedgerDto(group.Id, group.Name, courseName, months);
    }

    /// <summary>Aktivlashtirilgan oyning qisman narxi — <see cref="TuitionService.ChargeActivationProrateAsync"/>
    /// bilan AYNAN bir formula: qolgan darslar soni <see cref="TuitionService.ProratedLessonCharge"/> ga beriladi
    /// (12+ dars yoki to'liq oy → to'liq oylik; aks holda kurs `LessonPrice`i × dars, u yo'q bo'lsa pro-rata).</summary>
    private static decimal ActivationGross(Group cls, decimal lessonFee, string dateIso)
    {
        if (cls.MonthlyFee <= 0 || !DateOnly.TryParse(dateIso, out var d)) return cls.MonthlyFee;
        var ms = new DateOnly(d.Year, d.Month, 1);
        var me = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        var total = TuitionService.LessonsInRange(cls.Days, ms, me);
        var rem = TuitionService.LessonsInRange(cls.Days, d, me);
        return TuitionService.ProratedLessonCharge(cls.MonthlyFee, lessonFee, rem, total);
    }

    /// <summary>Muzlatilgan oyning qisman narxi — <see cref="TuitionService.ChargeFreezeProrateAsync"/> bilan
    /// AYNAN bir formula: MUZLATISH SANASI HAM hisobga olinadi (o'sha kuni dars bo'lsa qo'shiladi) va
    /// <see cref="TuitionService.ProratedLessonCharge"/> ishlatiladi.</summary>
    private static decimal FreezeGross(Group cls, decimal lessonFee, string actIso, string fzIso)
    {
        if (cls.MonthlyFee <= 0 || !DateOnly.TryParse(fzIso, out var fz)) return 0m;
        var ms = new DateOnly(fz.Year, fz.Month, 1);
        var me = new DateOnly(fz.Year, fz.Month, DateTime.DaysInMonth(fz.Year, fz.Month));
        var total = TuitionService.LessonsInRange(cls.Days, ms, me);
        var from = ms;
        if (actIso.Length >= 10 && actIso[..7] == fzIso[..7] && DateOnly.TryParse(actIso, out var act) && act > ms)
            from = act;
        var before = fz >= from ? TuitionService.LessonsInRange(cls.Days, from, fz) : 0;
        return TuitionService.ProratedLessonCharge(cls.MonthlyFee, lessonFee, before, total);
    }
}
