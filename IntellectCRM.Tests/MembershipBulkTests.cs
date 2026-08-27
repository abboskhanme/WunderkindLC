using IntellectCRM.Application.Services;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// Ommaviy muzlatish/aktivlashtirishga QAYSI a'zolik tushadi.
///
/// <para><c>ClassesController.BulkApplyAsync</c> AYNAN shu funksiyani chaqiradi — qoida
/// takrorlanmaydi.</para>
/// </summary>
public class MembershipBulkTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("trial", true)]
    // Allaqachon muzlatilgan — qayta muzlatilsa qisman to'lov IKKI marta yozilardi.
    [InlineData("frozen", false)]
    [InlineData("completed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Muzlatish_FaqatFaolVaSinov(string? status, bool expected) =>
        Assert.Equal(expected, MembershipBulk.IsEligible(status, freeze: true));

    [Theory]
    [InlineData("trial", true)]
    [InlineData("frozen", true)]
    // Allaqachon faol — qayta aktivlashtirish ActivatedAt ni surib, hisoblangan oyni buzardi.
    [InlineData("active", false)]
    public void Aktivlashtirish_FaolBolmaganlar(string status, bool expected) =>
        Assert.Equal(expected, MembershipBulk.IsEligible(status, freeze: false));

    /// <summary>Chegara "bor" ekani qulflanadi — olib tashlansa ommaviy amal so'rovni cho'zib yuborardi.</summary>
    [Fact]
    public void Chegara_Belgilangan() => Assert.Equal(500, MembershipBulk.MaxTargets);
}
