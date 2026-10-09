using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// AKTIV A'ZOLIKNING AKTIVLASHTIRISH SANASINI O'ZGARTIRISH (2026-10-09) — va pulni shunga moslash.
///
/// <para><b>Nega kerak</b> (mijoz): o'quvchi oyning 8-sanasidan qo'shilib, qisman (−180 000) hisoblangan;
/// u to'liq oylikni (225 000) to'laganda balans +45 000 bo'lib qolardi. Sanasi xato kiritilgan (aslida 1-sanadan
/// o'qiyapti) yoki kech kiritilgan o'quvchining hisobini to'g'rilashning ANIQ yo'li yo'q edi — faqat
/// superadmin «Tranzaksiyalar tarixi» ichida oyni qo'lda tahrirlay olardi.</para>
///
/// <para><b>Nima qiladi</b> (yangi hisob-kitob YO'Q — mavjud yagona manbalar):</para>
/// <list type="bullet">
/// <item>yangi sananing oyi — qisman hisob <see cref="TuitionService.ChargeActivationProrateAsync"/> (qolgan darslar,
/// oyning birinchi darsidan — to'liq oylik); shu sanadan keyin dars qolmasa — o'sha oy hisobi olib tashlanadi;</item>
/// <item>sana OLDINGI oyga surilsa — eski aktivlashtirish oyi endi TO'LIQ oy (<see cref="TuitionService.RechargeFullMonthAsync"/>),
/// oraliq oylar esa darhol yoziladi (<see cref="TuitionService.AccrueCatchUpAsync"/>, orqaga sanalgan aktivlashtirishdagi kabi).</item>
/// </list>
///
/// <para>⚠️ <b>Cheklovlar</b> (<see cref="BlockReason"/>, sof): faqat <c>active</c> a'zolik; kelajak sana yo'q;
/// KEYINGI oyga surish yo'q (u holda o'tgan oy hisoblarini bekor qilish kerak bo'lardi — buning uchun
/// «Muzlatish»/«Guruhdan chiqarish» bor); 12 oydan ortiq orqaga yo'q (yil xato yozilsa bir yillik qarz
/// tushmasin); yopilgan oldingi faol davrga (<see cref="StudentGroup.PastPeriods"/>) tegib bo'lmaydi.
/// Qo'lda tahrirlangan (<c>Locked</c>) oy TEGILMAYDI va natijada alohida aytiladi.</para>
///
/// <para>SaveChanges QILMAYDI. Chaqiruvchi o'quvchi QULFI ichida bo'ladi (to'lov bilan bir navbat).</para>
/// </summary>
public static class ActivationDateChange
{
    /// <summary>Ko'pi bilan shuncha oy orqaga surish mumkin.</summary>
    public const int MaxMonthsBack = 12;

    public sealed record MonthChange(string Month, decimal? Before, decimal? After);

    /// <param name="Months">Hisobi o'zgargan oylar (effektiv summa: hisob − chegirma; <c>null</c> — hisob yo'q).</param>
    /// <param name="LockedMonths">Qo'lda tahrirlangani uchun TEGILMAGAN oylar.</param>
    public sealed record Result(string OldDate, string NewDate, decimal BalanceBefore, decimal BalanceAfter,
        List<MonthChange> Months, List<string> LockedMonths);

    /// <summary>Nega o'zgartirib bo'lmaydi (<c>null</c> — mumkin). Sof funksiya.</summary>
    public static string? BlockReason(StudentGroup sg, string? newDate, DateOnly today)
    {
        if (sg.Status != "active" || !sg.IsActive || sg.FrozenAt.Length > 0)
            return "Faqat AKTIV a'zolikning aktivlashtirish sanasi o'zgartiriladi";
        if (sg.ActivatedAt.Length < 10 || !DateOnly.TryParse(sg.ActivatedAt[..10], CultureInfo.InvariantCulture, out var old))
            return "A'zolikda aktivlashtirish sanasi yo'q — «Aktivlashtirish» oynasidan foydalaning";
        if (string.IsNullOrWhiteSpace(newDate)
            || !DateOnly.TryParseExact(newDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return "Sana noto'g'ri (yyyy-MM-dd)";
        if (d > today) return "Sana kelajakda bo'lishi mumkin emas";
        if (d == old) return "Sana o'zgarmadi";
        if (MonthIndex(d) > MonthIndex(old))
            return "Aktivlashtirishni keyingi oyga surib bo'lmaydi — o'tgan oy hisobini bekor qilish kerak bo'lardi. "
                   + "Shu oy ichida surish mumkin; aks holda «Muzlatish» yoki «Guruhdan chiqarish»dan foydalaning";
        if (MonthIndex(today) - MonthIndex(d) > MaxMonthsBack)
            return $"{MaxMonthsBack} oydan ortiq orqaga surib bo'lmaydi";
        var newMonth = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        foreach (var p in sg.PastPeriods)
        {
            if (!MembershipLifecycle.TryParsePeriod(p, out _, out var to) || to.Length < 7) continue;
            if (string.CompareOrdinal(to[..7], newMonth) >= 0)
                return $"Bu a'zolikda {to} gacha oldingi faol davr bor — sanani unga tegadigan qilib surib bo'lmaydi. "
                       + "Kerak bo'lsa oylik hisobni «Balansni tuzatish» orqali o'zgartiring";
        }
        return null;
    }

    private static int MonthIndex(DateOnly d) => d.Year * 12 + d.Month;

    /// <summary>
    /// Sanani o'zgartiradi va hisoblarni moslaydi. <see cref="BlockReason"/> OLDIN tekshirilgan bo'lishi kerak
    /// (bu yerda qayta tekshiriladi — xato bo'lsa <see cref="InvalidOperationException"/>).
    /// </summary>
    public static async Task<Result> ApplyAsync(IAppDbContext db, Student s, Group cls, StudentGroup sg, string newDate)
    {
        var reason = BlockReason(sg, newDate, AppClock.Today);
        if (reason is not null) throw new InvalidOperationException(reason);
        newDate = newDate.Trim();
        var oldDate = sg.ActivatedAt[..10];
        var (oldMonth, newMonth) = (oldDate[..7], newDate[..7]);

        // Shu (o'quvchi, guruh) ning BARCHA hisoblari kuzatuvga olinadi — keyin nima o'zgarganini solishtirish uchun
        // (`Local` o'chirilganlarni ko'rsatmaydi, qo'shilganlarni ko'rsatadi).
        var rows = await db.MonthlyCharges.Where(c => c.StudentId == s.Id && c.GroupId == cls.Id).ToListAsync();
        var before = rows.GroupBy(r => r.Month).ToDictionary(g => g.Key, g => g.Sum(Effective), StringComparer.Ordinal);
        var balanceBefore = s.Balance;
        var locked = new List<string>();

        sg.ActivatedAt = newDate;
        // Qo'shilgan sana aktivlashtirishdan KEYIN bo'lmasin (sana xato kiritilgan holat).
        if (sg.JoinedAt.Length >= 10 && string.CompareOrdinal(newDate, sg.JoinedAt[..10]) < 0) sg.JoinedAt = newDate;

        // 1) Yangi aktivlashtirish oyi — qisman hisob (qo'lda tahrirlangan bo'lsa tegilmaydi).
        if (!await TuitionService.RechargeActivationMonthAsync(db, s, cls, newDate)) locked.Add(newMonth);

        // 2) Oldingi oyga surildi — eski aktivlashtirish oyi endi TO'LIQ oy, oraliq oylar darhol yoziladi.
        if (string.CompareOrdinal(newMonth, oldMonth) < 0)
        {
            if (!await TuitionService.RechargeFullMonthAsync(db, s, cls, oldMonth)) locked.Add(oldMonth);
            await TuitionService.AccrueCatchUpAsync(db, s, cls, newDate, sg);
        }

        var after = db.MonthlyCharges.Local
            .Where(c => c.StudentId == s.Id && c.GroupId == cls.Id)
            .GroupBy(r => r.Month).ToDictionary(g => g.Key, g => g.Sum(Effective), StringComparer.Ordinal);
        var months = before.Keys.Union(after.Keys)
            .Select(m => new MonthChange(m,
                before.TryGetValue(m, out var b) ? b : null,
                after.TryGetValue(m, out var a) ? a : null))
            .Where(c => c.Before != c.After)
            .OrderBy(c => c.Month, StringComparer.Ordinal)
            .ToList();
        return new Result(oldDate, newDate, balanceBefore, s.Balance, months, locked);
    }

    private static decimal Effective(MonthlyCharge c) => Math.Max(0m, c.Amount - c.Discount);
}
