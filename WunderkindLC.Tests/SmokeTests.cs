using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Test infratuzilmasi to'g'ri ishlashini tekshiruvchi "tutun" testlari — asosiy
/// tekshiruvlar emas, balki TestDb/AppClock kabi yordamchilarning ishlashini tasdiqlaydi.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void AppClock_Now_ToshkentMintaqasida()
    {
        // AppClock.Now doim UTC+5 (Asia/Tashkent) ofsetida bo'lishi kerak — server qayerda
        // (UTC Docker yoki lokal) turishidan qat'i nazar.
        var utcNow = DateTime.UtcNow;
        var tashkentNow = AppClock.Now;

        var diffHours = (tashkentNow - utcNow).TotalHours;

        // Ofset 5 soatga yaqin bo'lishi kerak (test ishlash vaqti sabab bir necha soniya farq bo'lishi mumkin).
        Assert.InRange(diffHours, 4.99, 5.01);
    }

    [Fact]
    public void TestDb_Orqali_EntitySaqlanibOqiladi()
    {
        // TestDb.Sqlite() — asosiy variant (batafsili TestDb.cs izohiga qarang): to'liq
        // AppDbContext modelini (massiv xossali entity'lar bilan birga) qamrab oladi.
        using var db = TestDb.Sqlite();

        var district = new District
        {
            Name = "Chilonzor tumani",
            Order = 1,
        };

        db.Context.Districts.Add(district);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        var loaded = Assert.Single(db.Context.Districts);
        Assert.Equal(district.Id, loaded.Id);
        Assert.Equal("Chilonzor tumani", loaded.Name);
        Assert.Equal(1, loaded.Order);
    }
}
