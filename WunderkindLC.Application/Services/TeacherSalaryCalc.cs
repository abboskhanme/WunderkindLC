using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchi oylik maoshini DARS JADVALI + DAVOMATdan hisoblaydi.
/// Reja (nominal) oylik = haftalik darslar × <see cref="WeeksPerMonth"/> × toifaning bir soat narxi.
/// Davomatga moslangan oylik = shu oyda o'qituvchi KELMAGAN (absent) kunlardagi darslar chegirilgan.
/// Haftalik darslar — har guruhning asosiy jadval shabloni (faqat mavjud, arxivlanmagan guruhlar).
/// </summary>
public static class TeacherSalaryCalc
{
    /// <summary>Oyiga o'rtacha hafta soni (haftalik darslarni oylikka aylantirish uchun).</summary>
    public const int WeeksPerMonth = 4;

    /// <summary>
    /// Har o'qituvchining hafta kuni kesimida darslar soni: teacherId → int[6]
    /// (indeks 0=Dushanba ... 5=Shanba; <see cref="ScheduleLesson.Day"/> bilan bir xil).
    /// Har guruh uchun faqat bitta asosiy (eng ko'p darsli) shablon, faqat mavjud guruhlar.
    /// </summary>
    public static Task<Dictionary<string, int[]>> LessonsByWeekdayAsync(IAppDbContext db) =>
        // Dars jadvali olib tashlandi — maosh QO'LDA kiritiladi (Teacher.Salary). Avtomatik "haftalik
        // darslar" hisobi yo'q, shuning uchun bo'sh qaytaradi (lesson-asoslangan barcha hisoblar 0 bo'ladi).
        Task.FromResult(new Dictionary<string, int[]>());

    /// <summary>Har o'qituvchining haftalik darslar soni (teacherId → son).</summary>
    public static async Task<Dictionary<string, int>> WeeklyLessonsAsync(IAppDbContext db) =>
        (await LessonsByWeekdayAsync(db)).ToDictionary(kv => kv.Key, kv => kv.Value.Sum());

    /// <summary>Toifa kaliti bo'yicha bir soat dars narxi (CenterMeta'dan).</summary>
    public static decimal RateFor(CenterMeta? meta, string? category)
    {
        if (meta is null) return 0m;
        return category switch
        {
            "oliy" => meta.SalaryRateOliy,
            "1" => meta.SalaryRate1,
            "2" => meta.SalaryRate2,
            "mutaxasis" => meta.SalaryRateMutaxasis,
            _ => 0m,
        };
    }

    /// <summary>Reja (nominal) oylik = haftalik darslar × 4 × toifa narxi (davomatsiz).</summary>
    public static decimal Monthly(int weeklyLessons, string? category, CenterMeta? meta) =>
        weeklyLessons * WeeksPerMonth * RateFor(meta, category);

    /// <summary>Kelmagan (absent) kunlardagi darslar soni.</summary>
    public static int MissedLessons(int[] byWeekday, IEnumerable<string> absentDates)
    {
        var missed = 0;
        foreach (var d in absentDates)
            if (DateOnly.TryParse(d, out var date))
            {
                var wd = ((int)date.DayOfWeek + 6) % 7; // Dushanba=0 ... Shanba=5, Yakshanba=6
                if (wd < 6) missed += byWeekday[wd];
            }
        return missed;
    }

    /// <summary>Ustama (foiz) qo'shilgan summa: salary × (1 + bonus%/100).</summary>
    public static decimal WithBonus(decimal salary, decimal bonusPct) => salary * (1 + bonusPct / 100m);

    /// <summary>O'qituvchining maosh boshlanish sanasi ("YYYY-MM-DD") — yangi maydon, bo'lmasa eski oydan.</summary>
    public static string? StartDateOf(Teacher t) =>
        !string.IsNullOrEmpty(t.SalaryStartDate) ? t.SalaryStartDate
        : !string.IsNullOrEmpty(t.SalaryStartMonth) ? $"{t.SalaryStartMonth}-01"
        : null;

    /// <summary>[from..to] oralig'idagi darslar soni — yakshanbasiz.</summary>
    public static int LessonsInRange(int[] byWeekday, DateOnly from, DateOnly to)
    {
        var total = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var wd = ((int)d.DayOfWeek + 6) % 7; // Dushanba=0..Shanba=5, Yakshanba=6
            if (wd >= 6) continue; // yakshanba
            total += byWeekday[wd];
        }
        return total;
    }

    /// <summary>
    /// Bir oydagi REJA darslar soni — o'sha oyda hafta kunlari necha marta kelishiga qarab.
    /// Ishga kirgan oy — kelgan kunidan; kirgan oydan oldin 0.
    /// </summary>
    public static int PlannedLessonsForMonth(int[] byWeekday, string month, string? startDate)
    {
        if (month.Length < 7) return 0;
        var y = int.Parse(month[..4]);
        var mo = int.Parse(month.Substring(5, 2));
        var monthEnd = new DateOnly(y, mo, DateTime.DaysInMonth(y, mo));
        var from = new DateOnly(y, mo, 1);
        if (startDate is { Length: >= 7 })
        {
            var startMonth = startDate[..7];
            if (string.CompareOrdinal(month, startMonth) < 0) return 0; // hali ishga kirmagan
            if (month == startMonth && startDate.Length >= 10 && DateOnly.TryParse(startDate, out var sd))
                from = sd; // qisman: kelgan kunidan oy oxirigacha
        }
        return LessonsInRange(byWeekday, from, monthEnd);
    }

    /// <summary>
    /// Bitta oy uchun yakuniy maosh (so'm): reja + qisman birinchi oy + davomat + ustama bilan.
    /// </summary>
    public static decimal MonthlyForMonth(
        int[] byWeekday, string? category, CenterMeta? meta, string month, string? startDate,
        IEnumerable<string> absentDatesInMonth, decimal bonusPct)
    {
        var planned = PlannedLessonsForMonth(byWeekday, month, startDate);
        // Ishga kirgan oyda — faqat kelgan kunidan keyingi yo'qliklar hisobga olinadi.
        var absences = startDate is { Length: >= 10 } && startDate[..7] == month
            ? absentDatesInMonth.Where(d => string.CompareOrdinal(d, startDate) >= 0)
            : absentDatesInMonth;
        var net = Math.Max(0, planned - MissedLessons(byWeekday, absences));
        return WithBonus(net * RateFor(meta, category), bonusPct);
    }

    /// <summary>Bitta o'qituvchining REJA (nominal) oyligi — davomat hisobga olinmaydi.</summary>
    public static async Task<decimal> MonthlyAsync(IAppDbContext db, Teacher t)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        var weekly = (await WeeklyLessonsAsync(db)).GetValueOrDefault(t.Id);
        return Monthly(weekly, t.Category, meta);
    }
}
