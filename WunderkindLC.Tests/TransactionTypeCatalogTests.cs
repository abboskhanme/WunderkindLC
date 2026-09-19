using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// TRANZAKSIYA TURLARI katalogi (<see cref="TransactionTypeCatalog"/>).
///
/// <para>Eng muhim qulf — har turning <c>BaseCategory</c> si MAVJUD tizim toifasi bo'lishi:
/// butun moliya mantig'i (maosh foizi, «Jarima» sahifasi, P&amp;L) aynan shu kodga qaraydi,
/// ya'ni noto'g'ri yozilgan kod jimgina "hech qayerga tushmaydigan" pul yaratardi.</para>
/// </summary>
public class TransactionTypeCatalogTests
{
    /// <summary>Klientdagi ro'yxatlar bilan bir xil (config/constants.ts).</summary>
    private static readonly HashSet<string> KnownCategories =
    [
        // kirim
        "tuition", "donation", "rent_in", "other",
        // chiqim
        "salary", "utilities", "supplies", "rent", "repair", "penalty",
    ];

    [Theory]
    [InlineData("kirim", "income")]
    [InlineData("vaucher", "income")]
    [InlineData("chiqim", "expense")]
    [InlineData("jarima", "expense")]
    public void Bolim_pul_yonalishini_belgilaydi(string kind, string direction)
        => Assert.Equal(direction, TransactionTypeCatalog.DirectionOf(kind));

    [Fact]
    public void Notanish_bolim_qabul_qilinmaydi()
    {
        Assert.False(TransactionTypeCatalog.IsKind("boshqa"));
        Assert.False(TransactionTypeCatalog.IsKind(null));
        Assert.True(TransactionTypeCatalog.IsKind("jarima"));
    }

    [Fact]
    public void Seed_turlarining_hammasi_MAVJUD_tizim_toifasiga_boglangan()
    {
        foreach (var (kind, name, baseCategory) in TransactionTypeCatalog.Seed)
            Assert.True(KnownCategories.Contains(baseCategory),
                $"{kind}/{name}: noma'lum toifa '{baseCategory}'");
    }

    [Fact]
    public void Seed_qatorlarining_yonalishi_bolimidan_kelib_chiqadi()
    {
        foreach (var row in TransactionTypeCatalog.SeedRows())
            Assert.Equal(TransactionTypeCatalog.DirectionOf(row.Kind), row.Direction);
    }

    [Fact]
    public void Seed_tartibi_HAR_BOLIM_ichida_noldan_boshlanadi()
    {
        foreach (var group in TransactionTypeCatalog.SeedRows().GroupBy(x => x.Kind))
            Assert.Equal(Enumerable.Range(0, group.Count()), group.Select(x => x.Order));
    }

    [Fact]
    public void Bir_bolimda_bir_xil_nom_takrorlanmaydi()
    {
        foreach (var group in TransactionTypeCatalog.Seed.GroupBy(x => x.Kind))
            Assert.Equal(group.Count(), group.Select(x => x.Name).Distinct().Count());
    }

    [Fact]
    public void Jarima_turi_penalty_toifasida_Jarima_sahifasi_shunga_qaraydi()
    {
        var jarima = TransactionTypeCatalog.Seed.Where(x => x.Kind == "jarima").ToList();
        Assert.NotEmpty(jarima);
        Assert.All(jarima, x => Assert.Equal("penalty", x.BaseCategory));
    }
}
