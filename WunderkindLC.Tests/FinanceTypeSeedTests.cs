using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// MAJBURIY KIRIM/CHIQIM TURLARI (<see cref="FinanceTypeSeed"/>): kitob, mock, boshqa kirim; ijara,
/// kommunal, internet, kantselyariya. Asosiy xavflar: admin o'chirgan tur har restartda QAYTA
/// tug'ilishi, eski (edutizim) turlar yonida nomdosh dublikat paydo bo'lishi va admin ataylab
/// boshqa toifaga bog'lagan turning jimgina qayta bog'lanishi.
/// </summary>
public class FinanceTypeSeedTests
{
    private static TransactionType T(string kind, string name, string category, int order = 0) => new()
    {
        Kind = kind, Name = name, BaseCategory = category, Order = order,
        Direction = TransactionTypeCatalog.DirectionOf(kind),
    };

    /// <summary>Prod'dagi holat: edutizimdan kelgan ESKI toifalar bilan seed (2026-10 gacha).</summary>
    private static List<TransactionType> LegacyCatalog() =>
    [
        T("kirim", "O'quvchi to'ladi", "tuition", 0),
        T("kirim", "Kitob uchun", "other", 1),
        T("kirim", "Mock to'lov", "other", 2),
        T("kirim", "Fond", "other", 3),
        T("chiqim", "Hodimga oylik", "salary", 0),
        T("chiqim", "Internet va telefon", "utilities", 1),
        T("chiqim", "Kommunal to'lovlar", "utilities", 2),
        T("chiqim", "Arenda", "rent", 3),
        T("chiqim", "Kanstovar", "supplies", 4),
        T("chiqim", "Boshqa", "other", 5),
    ];

    [Fact]
    public void Yangi_baza_seedi_allaqachon_TOLIQ__reja_bosh()
    {
        var plan = FinanceTypeSeed.PlanFor(TransactionTypeCatalog.SeedRows());
        Assert.True(plan.IsEmpty, string.Join(", ", plan.Add.Select(a => a.Name)));
    }

    [Fact]
    public void Yangi_baza_seedida_kitob_mock_internet_kantselyariya_OZ_toifasida()
    {
        var seed = TransactionTypeCatalog.SeedRows();
        Assert.Equal(FinanceCategories.Book, seed.Single(t => t.Name == "Kitob uchun").BaseCategory);
        Assert.Equal(FinanceCategories.Mock, seed.Single(t => t.Name == "Mock to'lov").BaseCategory);
        Assert.Equal(FinanceCategories.Internet, seed.Single(t => t.Name == "Internet va telefon").BaseCategory);
        Assert.Equal(FinanceCategories.Stationery, seed.Single(t => t.Name == "Kanstovar").BaseCategory);
        Assert.Contains(seed, t => t is { Kind: "kirim", Name: "Boshqa kirim", BaseCategory: "other" });
    }

    [Fact]
    public void Eski_katalogda__eski_turlar_QAYTA_BOGLANADI__dublikat_yaratilmaydi()
    {
        var existing = LegacyCatalog();
        var plan = FinanceTypeSeed.PlanFor(existing);

        var repointed = plan.Repoint.ToDictionary(r => r.Row.Name, r => r.NewCategory);
        Assert.Equal(4, repointed.Count);
        Assert.Equal(FinanceCategories.Book, repointed["Kitob uchun"]);
        Assert.Equal(FinanceCategories.Mock, repointed["Mock to'lov"]);
        Assert.Equal(FinanceCategories.Internet, repointed["Internet va telefon"]);
        Assert.Equal(FinanceCategories.Stationery, repointed["Kanstovar"]);

        // Faqat «Boshqa kirim» yo'q edi («Fond» ham other, lekin u boshqa ma'nodagi tur).
        var added = Assert.Single(plan.Add);
        Assert.Equal(("kirim", "Boshqa kirim", "other", "income"), (added.Kind, added.Name, added.BaseCategory, added.Direction));
        Assert.Equal(4, added.Order);   // bo'lim oxiriga
    }

    [Fact]
    public void Bosh_katalogda__sakkizta_tur_qoshiladi()
    {
        var plan = FinanceTypeSeed.PlanFor([]);
        Assert.Empty(plan.Repoint);
        Assert.Equal(FinanceTypeSeed.RequiredTypes.Length, plan.Add.Count);
        Assert.Equal(
            ["O'qish to'lovi", "Kitob/qo'llanma sotuvi", "Mock imtihon to'lovi", "Boshqa kirim"],
            plan.Add.Where(a => a.Kind == "kirim").Select(a => a.Name).ToArray());
        Assert.Equal(
            ["Ijara", "Kommunal to'lovlar", "Internet", "Kantselyariya"],
            plan.Add.Where(a => a.Kind == "chiqim").Select(a => a.Name).ToArray());
        Assert.All(plan.Add, a => Assert.Equal(TransactionTypeCatalog.DirectionOf(a.Kind), a.Direction));
        Assert.Equal([0, 1, 2, 3], plan.Add.Where(a => a.Kind == "kirim").Select(a => a.Order).ToArray());
    }

    [Fact]
    public void Admin_BOSHQA_toifaga_boglagan_nomdosh_tur_TEGILMAYDI_va_dublikat_qoshilmaydi()
    {
        // Admin «Kitob uchun» ni ataylab "tuition" ga bog'lagan (eski toifa "other" emas).
        var existing = new List<TransactionType> { T("kirim", "Kitob uchun", "tuition") };
        var plan = FinanceTypeSeed.PlanFor(existing);

        Assert.DoesNotContain(plan.Repoint, r => r.Row.Name == "Kitob uchun");
        Assert.DoesNotContain(plan.Add, a => a.BaseCategory == FinanceCategories.Book);
    }

    [Fact]
    public void Nom_solishtirish_apostrof_va_registrga_qaramaydi()
    {
        Assert.Equal(FinanceTypeSeed.NormName("O'quvchi to'ladi"), FinanceTypeSeed.NormName("  o‘quvchi   toʻladi "));
        // «O‘quvchi to‘ladi» (boshqa apostrof) mavjud bo'lsa — tuition talabi bajarilgan.
        var plan = FinanceTypeSeed.PlanFor([T("kirim", "KITOB UCHUN", "other"), T("kirim", "O‘quvchi to‘ladi", "tuition")]);
        Assert.Contains(plan.Repoint, r => r.Row.Name == "KITOB UCHUN" && r.NewCategory == FinanceCategories.Book);
        Assert.DoesNotContain(plan.Add, a => a.BaseCategory == FinanceCategories.Tuition);
    }

    [Fact]
    public async Task Seed_BIR_MARTA_ishlaydi__admin_ochirgan_tur_QAYTA_TUGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.TransactionTypes.AddRange(LegacyCatalog());
        await ctx.SaveChangesAsync();

        var first = await FinanceTypeSeed.EnsureOnceAsync(ctx);
        Assert.Equal((true, 1, 4), first);
        Assert.Equal(FinanceCategories.Internet,
            (await ctx.TransactionTypes.AsNoTracking().SingleAsync(t => t.Name == "Internet va telefon")).BaseCategory);
        Assert.True(await ctx.SeedMarkers.AnyAsync(m => m.Key == FinanceTypeSeed.MarkerKey));

        // Admin «Mock to'lov» turini o'chirdi — keyingi restartda u QAYTMAYDI.
        ctx.TransactionTypes.Remove(await ctx.TransactionTypes.SingleAsync(t => t.Name == "Mock to'lov"));
        await ctx.SaveChangesAsync();
        var count = await ctx.TransactionTypes.CountAsync();

        var second = await FinanceTypeSeed.EnsureOnceAsync(ctx);
        Assert.Equal((false, 0, 0), second);
        Assert.Equal(count, await ctx.TransactionTypes.CountAsync());
        Assert.False(await ctx.TransactionTypes.AnyAsync(t => t.BaseCategory == FinanceCategories.Mock));
    }

    [Fact]
    public async Task Seed_mavjud_TRANZAKSIYALARNI_qayta_yozmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var types = LegacyCatalog();
        ctx.TransactionTypes.AddRange(types);
        var internet = types.Single(t => t.Name == "Internet va telefon");
        ctx.FinanceTransactions.Add(new FinanceTransaction
            { Date = "2026-09-10", Direction = "expense", Category = "utilities", TypeId = internet.Id, Amount = 150_000m });
        await ctx.SaveChangesAsync();

        await FinanceTypeSeed.EnsureOnceAsync(ctx);

        // Eski qator avvalgi toifasida qoladi (backfill YO'Q) — faqat turning o'zi yangi toifaga bog'landi.
        Assert.Equal("utilities", (await ctx.FinanceTransactions.AsNoTracking().SingleAsync()).Category);
    }
}
