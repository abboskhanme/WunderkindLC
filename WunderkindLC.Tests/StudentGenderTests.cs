using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'quvchi jinsi IXTIYORIY (2026-10-09). Asosiy xavf: bo'sh/noma'lum qiymat jimgina "male" ga
/// aylanib, jinsi kiritilmagan qizlar "o'g'il" bo'lib yozilishi.
/// </summary>
public class StudentGenderTests
{
    [Theory]
    [InlineData("male", "male")]
    [InlineData("Erkak", "male")]
    [InlineData("O'g'il", "male")]
    [InlineData("oʻgʻil", "male")]
    [InlineData("o`g`il bola", "male")]
    [InlineData("female", "female")]
    [InlineData("QIZ", "female")]
    [InlineData("ayol", "female")]
    [InlineData("2", "female")]
    [InlineData("Қиз", "female")]
    public void Tanilgan_qiymatlar(string raw, string expected) =>
        Assert.Equal(expected, StudentGender.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noma'lum")]
    public void Bosh_yoki_tanilmagan_qiymat_MALE_bolmaydi(string? raw) =>
        Assert.Equal("", StudentGender.Normalize(raw));

    [Fact]
    public void Eksportda_korsatilmagan_jins_bosh_qoladi()
    {
        Assert.Equal("", StudentGender.ExportLabel(""));
        Assert.Equal("Qiz", StudentGender.ExportLabel("female"));
        Assert.Equal("O'g'il", StudentGender.ExportLabel("male"));
    }
}
