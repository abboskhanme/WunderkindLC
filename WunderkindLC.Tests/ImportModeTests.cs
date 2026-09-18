using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// IMPORT REJIMI (`ImportMode`) — boshqa tizimdan ko'chirish paytida avto-hisob/eslatma/SMS
/// o'chirilishi kerak. Test aynan KALITNI qulflaydi: qiymatlar qanday o'qilishi va
/// standart holat (o'chiq) — chunki noto'g'ri yoqilgan rejim oylik hisobni JIMGINA to'xtatadi.
/// </summary>
public class ImportModeTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    public void Yoqiladigan_qiymatlar(string raw)
    {
        ImportMode.Configure(Config(("Import:Mode", raw)));
        Assert.True(ImportMode.Enabled);
        ImportMode.SetForTests(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("boshqa")]
    public void Ochiq_qoladigan_qiymatlar(string raw)
    {
        ImportMode.Configure(Config(("Import:Mode", raw)));
        Assert.False(ImportMode.Enabled);
    }

    [Fact]
    public void Kalit_UMUMAN_berilmasa_OCHIQ()
    {
        ImportMode.Configure(Config());
        Assert.False(ImportMode.Enabled);
    }
}
