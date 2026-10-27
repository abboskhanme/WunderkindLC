using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Tepadagi filial almashtirgich ro'yxati (`Branches:Links`) — joriy filial hostdan, faqat https.</summary>
public class BranchLinksTests
{
    private const string Raw = "Yaypan|https://lc.wunderkindedu.uz; Furqat|https://furqat.wunderkindedu.uz/";

    [Fact]
    public void Joriy_filial_hostdan_aniqlanadi()
    {
        var links = BranchLinks.Parse(Raw, "furqat.wunderkindedu.uz");
        Assert.Equal(
            [new BranchLinks.Link("Yaypan", "https://lc.wunderkindedu.uz", false),
             new BranchLinks.Link("Furqat", "https://furqat.wunderkindedu.uz", true)],
            links);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Yaypan")]
    [InlineData("Yaypan|lc.wunderkindedu.uz")]
    [InlineData("X|javascript:alert(1)")]
    [InlineData("X|http://lc.wunderkindedu.uz")]
    [InlineData("|https://lc.wunderkindedu.uz")]
    public void Buzuq_yoki_https_bolmagan_qiymat_tashlanadi(string? raw) =>
        Assert.Empty(BranchLinks.Parse(raw, "lc.wunderkindedu.uz"));

    [Fact]
    public void Takror_manzil_bir_marta() =>
        Assert.Single(BranchLinks.Parse("A|https://a.uz;B|https://a.uz", "a.uz"));
}
