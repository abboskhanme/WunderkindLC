using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// SINFGA QARAB OYLIK — sof qoida (<see cref="TuitionPricing"/>). Asosiy xavf: oraliq topilmaganda
/// o'quvchi jimgina 0 yoki noto'g'ri narx olishi, va 1-sentabrda sinf oshganda narx o'zgarmasligi.
/// </summary>
public class TuitionPricingTests
{
    private static readonly TuitionPricing.Band Low = new(3, 7, 225_000m, 20_000m);
    private static readonly TuitionPricing.Band Mid = new(8, 9, 275_000m, 0m);
    private static readonly IReadOnlyList<TuitionPricing.Band> Bands = [Low, Mid];
    private const decimal GroupFee = 300_000m;

    // ============================ Tanlash ============================

    [Theory]
    [InlineData(3, 225_000)]   // chegara kiradi
    [InlineData(7, 225_000)]   // chegara kiradi
    [InlineData(8, 275_000)]
    [InlineData(9, 275_000)]
    public void Sinf_oraliqqa_tushsa_oraliq_narxi(int grade, int expected)
    {
        var year = SchoolGrades.AcademicYear(new DateOnly(2026, 10, 1));
        Assert.Equal(expected, TuitionPricing.Resolve(Bands, GroupFee, grade, year, "2026-10").Monthly);
    }

    [Theory]
    [InlineData(null)]   // sinf kiritilmagan — bazadagi ko'pchilik
    [InlineData(2)]      // oraliqdan pastda
    [InlineData(11)]     // oraliqdan yuqorida
    public void Oraliq_topilmasa_GURUH_narxi_va_kurs_bir_dars_narxi(int? grade)
    {
        var (monthly, lesson) = TuitionPricing.Resolve(Bands, GroupFee, grade, 2026, "2026-10");
        Assert.Equal(GroupFee, monthly);
        Assert.Null(lesson); // chaqiruvchi kursning bir dars narxini oladi (avvalgi xatti-harakat)
    }

    [Fact]
    public void Oraliq_yoq_kursda_HECH_NARSA_ozgarmaydi()
    {
        var (monthly, lesson) = TuitionPricing.Resolve([], GroupFee, 5, 2026, "2026-10");
        Assert.Equal(GroupFee, monthly);
        Assert.Null(lesson);
    }

    [Fact]
    public void BEPUL_guruhda_oraliq_QOLLANMAYDI()
    {
        // Bepul guruhda hech qachon hisob yozilmagan; oraliq narxi qo'llansa fon hisobi o'tgan
        // BARCHA oylarga orqaga qarz yozib, SMS eslatmalar yuborardi.
        var (monthly, lesson) = TuitionPricing.Resolve(Bands, 0m, 5, 2026, "2026-10");
        Assert.Equal(0m, monthly);
        Assert.Null(lesson);
    }

    [Fact]
    public void Oraliq_bir_dars_narxi_qaytadi_0_ham_qiymat()
    {
        Assert.Equal(20_000m, TuitionPricing.Resolve(Bands, GroupFee, 5, 2026, "2026-10").Lesson);
        // 0 — "pro-rata" degani, "kursnikini ol" EMAS (null bilan adashmasin).
        Assert.Equal(0m, TuitionPricing.Resolve(Bands, GroupFee, 8, 2026, "2026-10").Lesson);
    }

    [Fact]
    public void Birinchi_sentabrda_sinf_oshadi_va_narx_ozgaradi()
    {
        // 2025/26 o'quv yilida 7-sinf → avgustda 225 000, sentabrdan (8-sinf) 275 000.
        Assert.Equal(225_000m, TuitionPricing.Resolve(Bands, GroupFee, 7, 2025, "2026-08").Monthly);
        Assert.Equal(275_000m, TuitionPricing.Resolve(Bands, GroupFee, 7, 2025, "2026-09").Monthly);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-13")]
    public void Buzuq_oy_sinfsiz_hisoblanadi(string month) =>
        Assert.Equal(GroupFee, TuitionPricing.Resolve(Bands, GroupFee, 5, 2026, month).Monthly);

    // ============================ Tekshiruv ============================

    [Fact]
    public void Togri_oraliqlar_qabul_qilinadi() => Assert.Null(TuitionPricing.Validate(Bands));

    [Fact]
    public void Bosh_royxat_qabul_qilinadi() => Assert.Null(TuitionPricing.Validate([]));

    [Fact]
    public void Ustma_ust_oraliqlar_rad_etiladi() =>
        Assert.NotNull(TuitionPricing.Validate([new(3, 7, 1m, 0m), new(7, 9, 1m, 0m)]));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 13)]
    [InlineData(8, 5)]
    public void Notogri_sinf_chegarasi_rad_etiladi(int from, int to) =>
        Assert.NotNull(TuitionPricing.Validate([new(from, to, 100_000m, 0m)]));

    [Fact]
    public void Narxsiz_yoki_manfiy_dars_narxi_rad_etiladi()
    {
        Assert.NotNull(TuitionPricing.Validate([new(3, 7, 0m, 0m)]));
        Assert.NotNull(TuitionPricing.Validate([new(3, 7, 100_000m, -1m)]));
    }

    [Fact]
    public void Juda_kop_oraliq_rad_etiladi() =>
        Assert.NotNull(TuitionPricing.Validate(
            Enumerable.Range(1, TuitionPricing.MaxBands + 1).Select(i => new TuitionPricing.Band(1, 1, 1m, 0m)).ToList()));

    [Fact]
    public void Yorliq()
    {
        Assert.Equal("3–7-sinf", TuitionPricing.Label(Low));
        Assert.Equal("5-sinf", TuitionPricing.Label(new(5, 5, 1m, 0m)));
        Assert.Equal("Maktabni bitirgan", TuitionPricing.Label(new(12, 12, 1m, 0m)));
    }
}
