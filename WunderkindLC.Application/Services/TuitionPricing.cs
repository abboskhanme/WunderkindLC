using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// SINFGA QARAB OYLIK — o'quvchi shu guruhda shu oy uchun QANCHA to'laydi. Qoida YAGONA joyda
/// (<c>TuitionPricingTests</c>), SPEC: <c>docs/SPEC-kurs-sinf-narxi.md</c>.
///
/// <code>
/// o'quvchining SHU OYDAGI sinfi (oyning 1-kuniga, SchoolGrades.Current)
///   → guruh kursining oralig'iga tushsa : oraliq Price (+ oraliq LessonPrice)
///   → aks holda                         : guruh MonthlyFee (+ kursning LessonPrice)
/// </code>
///
/// <para>⚠️ "Aks holda" ATAYIN keng: sinf kiritilmagan (bazadagi ko'pchilik), oraliqdan tashqarida
/// yoki kurssiz guruh — hammasi avvalgidek guruh narxini to'laydi. Ya'ni oraliq qo'shilmagan
/// kursda HECH NARSA o'zgarmaydi.</para>
///
/// <para>⚠️ Yangi hisob-kitob joyi yozsangiz `Group.MonthlyFee` ni TO'G'RIDAN-TO'G'RI o'qimang —
/// o'quvchi qarziga tegishli bo'lsa <see cref="PricingBook.Resolve"/> ni chaqiring. Guruh
/// darajasidagi og'irliklar (maosh/tushum taqsimoti) guruh narxida qoladi — SPEC'dagi ro'yxat.</para>
/// </summary>
public static class TuitionPricing
{
    public const int MaxBands = 12;

    public readonly record struct Band(int GradeFrom, int GradeTo, decimal Price, decimal LessonPrice);

    /// <summary>"yyyy-MM" oyidagi sinf (oyning 1-kuniga) — sentabr hisobi yangi (oshgan) sinf bilan.</summary>
    public static int? GradeInMonth(int? schoolGrade, int? schoolGradeYear, string month)
    {
        if (month.Length < 7 || !int.TryParse(month[..4], out var y) || !int.TryParse(month[5..7], out var m)
            || m is < 1 or > 12)
            return null;
        return SchoolGrades.Current(schoolGrade, schoolGradeYear, new DateOnly(y, m, 1));
    }

    /// <summary>Sinf tushadigan oraliq (chegaralar kiradi). Sinf yo'q yoki mos oraliq yo'q — <c>null</c>.</summary>
    public static Band? Match(IReadOnlyList<Band> bands, int? grade)
    {
        if (grade is not int g) return null;
        foreach (var b in bands)
            if (g >= b.GradeFrom && g <= b.GradeTo) return b;
        return null;
    }

    /// <summary>
    /// Oylik va bir dars narxi. <c>Lesson</c> = <c>null</c> — "kursning bir dars narxini ishlating"
    /// (avvalgi xatti-harakat, chaqiruvchi o'zi oladi); qiymat — oraliqniki (0 = pro-rata).
    /// </summary>
    /// <remarks>⚠️ Guruh narxi 0 (BEPUL guruh) bo'lsa oraliqlar QO'LLANMAYDI. Bunday guruhda hech qachon
    /// hisob yozilmagan; oraliq narxi qo'llansa fon hisobi (`AccrueDue`, butun tarixni skanerlaydi)
    /// aktivlashtirishdan beri BARCHA oylarga orqaga qarz yozib, ota-onalarga SMS yuborardi.
    /// Kurs formasi ham asosiy narxi 0 kursga oraliq qo'shishni rad etadi.</remarks>
    public static (decimal Monthly, decimal? Lesson) Resolve(
        IReadOnlyList<Band> bands, decimal groupFee, int? schoolGrade, int? schoolGradeYear, string month)
    {
        if (bands.Count == 0 || groupFee <= 0) return (groupFee, null);
        var band = Match(bands, GradeInMonth(schoolGrade, schoolGradeYear, month));
        return band is { } b ? (b.Price, b.LessonPrice) : (groupFee, null);
    }

    /// <summary>Kurs formasidan kelgan oraliqlar to'g'rimi. Xato matni (o'zbekcha) yoki <c>null</c>.</summary>
    public static string? Validate(IReadOnlyList<Band> bands)
    {
        if (bands.Count > MaxBands) return $"Ko'pi bilan {MaxBands} ta oraliq";
        foreach (var b in bands)
        {
            if (!SchoolGrades.IsValid(b.GradeFrom) || !SchoolGrades.IsValid(b.GradeTo))
                return "Sinf 1 dan 11 gacha yoki «Maktabni bitirgan» bo'lishi kerak";
            if (b.GradeFrom > b.GradeTo) return $"Oraliq noto'g'ri: {b.GradeFrom}–{b.GradeTo}";
            if (b.Price <= 0) return $"{Label(b)} uchun oylik narxni kiriting";
            if (b.LessonPrice < 0) return $"{Label(b)}: bir dars narxi manfiy bo'lmasin";
        }
        var sorted = bands.OrderBy(b => b.GradeFrom).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i].GradeFrom <= sorted[i - 1].GradeTo)
                return $"Oraliqlar ustma-ust tushmoqda: {Label(sorted[i - 1])} va {Label(sorted[i])}";
        return null;
    }

    /// <summary>"5–6-sinf" / "7-sinf" / "Maktabni bitirgan" — xabar va audit matni uchun.</summary>
    public static string Label(Band b)
    {
        static string G(int g) => g == SchoolGrades.Graduated ? "bitirgan" : g.ToString();
        if (b.GradeFrom == b.GradeTo)
            return b.GradeFrom == SchoolGrades.Graduated ? "Maktabni bitirgan" : $"{b.GradeFrom}-sinf";
        return $"{G(b.GradeFrom)}–{G(b.GradeTo)}-sinf";
    }
}

/// <summary>
/// Kurslarning sinf oraliqlari — bitta so'rov bilan yuklab, xotirada narx tanlaydi
/// (<see cref="TuitionPricing.Resolve"/>). Ommaviy hisobda (yuzlab a'zolik) N+1 bo'lmasin.
/// </summary>
public sealed class PricingBook
{
    private readonly Dictionary<string, List<TuitionPricing.Band>> _byCourse;

    private PricingBook(Dictionary<string, List<TuitionPricing.Band>> byCourse) => _byCourse = byCourse;

    public static PricingBook Empty { get; } = new(new Dictionary<string, List<TuitionPricing.Band>>(StringComparer.Ordinal));

    public static PricingBook From(IEnumerable<SubjectPriceBand> rows) =>
        new(rows
            .GroupBy(r => r.SubjectId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.GradeFrom)
                      .Select(r => new TuitionPricing.Band(r.GradeFrom, r.GradeTo, r.Price, r.LessonPrice))
                      .ToList(),
                StringComparer.Ordinal));

    /// <summary>Barcha kurslar oraliqlari (jadval kichik: kurs × bir necha qator).</summary>
    public static async Task<PricingBook> LoadAsync(IAppDbContext db, CancellationToken ct = default) =>
        From(await db.SubjectPriceBands.AsNoTracking().ToListAsync(ct));

    /// <summary>Bitta kursniki (yakka amal — aktivlashtirish, muzlatish, to'lov oynasi).</summary>
    public static async Task<PricingBook> LoadForCourseAsync(IAppDbContext db, string? courseId, CancellationToken ct = default) =>
        string.IsNullOrEmpty(courseId)
            ? Empty
            : From(await db.SubjectPriceBands.AsNoTracking().Where(b => b.SubjectId == courseId).ToListAsync(ct));

    public IReadOnlyList<TuitionPricing.Band> BandsOf(string? courseId) =>
        !string.IsNullOrEmpty(courseId) && _byCourse.TryGetValue(courseId, out var list)
            ? list
            : Array.Empty<TuitionPricing.Band>();

    /// <summary>Shu o'quvchining shu guruhdagi shu oy narxi (oylik + bir dars).</summary>
    public (decimal Monthly, decimal? Lesson) Resolve(Group group, Student student, string month) =>
        TuitionPricing.Resolve(BandsOf(group.CourseId), group.MonthlyFee, student.SchoolGrade, student.SchoolGradeYear, month);

    /// <summary>Guruh narxi O'RNIGA boshqa asosiy narx bilan (guruh narxi o'zgartirilayotgan paytda).</summary>
    public (decimal Monthly, decimal? Lesson) Resolve(string? courseId, decimal groupFee, Student student, string month) =>
        TuitionPricing.Resolve(BandsOf(courseId), groupFee, student.SchoolGrade, student.SchoolGradeYear, month);

    public decimal MonthlyFee(Group group, Student student, string month) => Resolve(group, student, month).Monthly;
}
