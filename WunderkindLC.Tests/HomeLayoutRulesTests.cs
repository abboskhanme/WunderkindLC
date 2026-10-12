using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Home widgets layout (CRM Phase 5, <c>HomeLayoutRules</c>): defaults, normalisation, storage per user.</summary>
public class HomeLayoutRulesTests
{
    private static HomeLayoutItemDto I(string key, bool visible = true) => new(key, visible);

    [Fact]
    public void Standart_bugungi_sahifa_bilan_bir_xil()
    {
        var d = HomeLayoutRules.Default();
        Assert.Equal(1, d.Version);
        Assert.True(d.CardsVisible);
        Assert.Equal(12, d.Cards.Count);
        Assert.All(d.Cards, c => Assert.True(c.Visible));
        Assert.Equal("orders", d.Cards[0].Key);
        Assert.Equal(["cashboxes", "schedule"], d.Blocks.Where(b => b.Visible).Select(b => b.Key));
        Assert.Equal(8, d.Blocks.Count);
    }

    [Fact]
    public void Normallashtirish_nomalum_takror_va_yetishmaganlar()
    {
        var input = new HomeLayoutDto(1, false,
            [I("debtors", false), I("hack"), I("debtors"), I("orders")],
            [I("schedule", false), I("myTasks"), I("zzz")]);
        var (layout, error) = HomeLayoutRules.Normalize(input);
        Assert.Null(error);
        Assert.False(layout!.CardsVisible);
        Assert.Equal(["debtors", "orders"], layout.Cards.Take(2).Select(c => c.Key));
        Assert.False(layout.Cards[0].Visible);                       // birinchi uchragani saqlanadi
        Assert.Equal(12, layout.Cards.Count);                        // qolganlari oxiriga, ko'rinadigan
        Assert.DoesNotContain(layout.Cards, c => c.Key == "hack");
        Assert.Equal(["schedule", "myTasks", "cashboxes"], layout.Blocks.Take(3).Select(b => b.Key));
        Assert.True(layout.Blocks.Single(b => b.Key == "cashboxes").Visible);       // standart ko'rinish
        Assert.False(layout.Blocks.Single(b => b.Key == "redList").Visible);
    }

    [Fact]
    public void Versiya_va_hajm()
    {
        Assert.Equal(HomeLayoutRules.BadVersionMessage, HomeLayoutRules.Normalize(new HomeLayoutDto(2, true, [], [])).Error);
        Assert.Equal(HomeLayoutRules.BadVersionMessage, HomeLayoutRules.Normalize(null).Error);
        var huge = new HomeLayoutDto(1, true, Enumerable.Range(0, 300).Select(i => I("key" + i)).ToList(), []);
        Assert.Equal(HomeLayoutRules.TooLongMessage, HomeLayoutRules.Normalize(huge).Error);
        Assert.Null(HomeLayoutRules.Parse("{buzuq"));
        Assert.Null(HomeLayoutRules.Parse(""));
    }

    [Fact]
    public async Task Saqlash_oqish_va_standartga_qaytarish_foydalanuvchi_boyicha()
    {
        using var db = TestDb.Sqlite();
        Assert.Null(await HomeLayoutRules.GetAsync(db.Context, "u1"));

        var (saved, error) = await HomeLayoutRules.SaveAsync(db.Context, "u1",
            new HomeLayoutDto(1, true, [I("groups")], [I("myTasks")]));
        Assert.Null(error);
        Assert.Equal("groups", saved!.Cards[0].Key);

        var back = await HomeLayoutRules.GetAsync(db.Context, "u1");
        Assert.Equal("myTasks", back!.Blocks[0].Key);
        Assert.Null(await HomeLayoutRules.GetAsync(db.Context, "u2"));   // boshqa foydalanuvchiga tegmaydi

        var (_, bad) = await HomeLayoutRules.SaveAsync(db.Context, "u1", new HomeLayoutDto(9, true, [], []));
        Assert.NotNull(bad);
        Assert.Equal("groups", (await HomeLayoutRules.GetAsync(db.Context, "u1"))!.Cards[0].Key);   // eski qoldi

        await HomeLayoutRules.ResetAsync(db.Context, "u1");
        Assert.Null(await HomeLayoutRules.GetAsync(db.Context, "u1"));
    }
}
