using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// Global o'quvchi qidiruvi (<c>GET /api/admin/students/search</c>) qoidalari —
/// <see cref="StudentSearch"/> sof funksiyalari va SQLite ustidagi provayder-neytral tarmoq
/// (<c>WhereNameFallback</c>/<c>WherePhone</c>). Npgsql'dagi ILIKE tarmog'i testda ishlamaydi
/// (SQLite) — lekin naqsh yasovchi <c>LikePattern</c> shu yerda qoplangan.
/// </summary>
public class StudentSearchTests
{
    /* ---------- Words ---------- */

    [Fact]
    public void Words_KichikHarf_ApostrofBirxil_MaxWordsgacha()
    {
        Assert.Equal(new[] { "to'lqin", "aliyev" }, StudentSearch.Words("  Toʻlqin   ALIYEV "));
        // 6 ta so'z — faqat birinchi 5 tasi olinadi (har so'z alohida WHERE sharti).
        Assert.Equal(StudentSearch.MaxWords, StudentSearch.Words("a b c d e f").Length);
        Assert.Empty(StudentSearch.Words("   "));
    }

    [Theory]
    [InlineData("To’lqin")]
    [InlineData("To`lqin")]
    [InlineData("Toʼlqin")]
    public void Words_BarchaApostrofVariantlari_BittaKorinishga(string input)
    {
        Assert.Equal(new[] { "to'lqin" }, StudentSearch.Words(input));
    }

    /* ---------- Digits ---------- */

    [Fact]
    public void Digits_FaqatRaqamlarQoladi()
    {
        Assert.Equal("998901234567", StudentSearch.Digits("+998 (90) 123-45-67"));
        Assert.Equal("", StudentSearch.Digits("Aliyev"));
    }

    /* ---------- LikePattern ---------- */

    [Fact]
    public void LikePattern_MaxsusBelgilarEkranlanadi_ApostrofJoker()
    {
        // % _ \ — LIKE maxsus belgilari, \ bilan ekranlanadi (SQL'da ESCAPE '\').
        Assert.Equal(@"%50\%%", StudentSearch.LikePattern("50%"));
        Assert.Equal(@"%a\_b%", StudentSearch.LikePattern("a_b"));
        // Apostrof -> _ (bitta-belgi joker): bazada ' yoki ʻ turganidan qat'i nazar topiladi.
        Assert.Equal("%to_lqin%", StudentSearch.LikePattern("to'lqin"));
    }

    /* ---------- MemberState ---------- */

    [Fact]
    public void MemberState_Ustunlik_ActiveTrialFrozen()
    {
        Assert.Equal("active", StudentSearch.MemberState(new[] { "frozen", "active", "trial" }));
        Assert.Equal("trial", StudentSearch.MemberState(new[] { "frozen", "trial" }));
        Assert.Equal("frozen", StudentSearch.MemberState(new[] { "frozen" }));
        Assert.Equal("", StudentSearch.MemberState(Array.Empty<string?>()));
    }

    /* ---------- ClampLimit ---------- */

    [Fact]
    public void ClampLimit_1dan50gacha()
    {
        Assert.Equal(1, StudentSearch.ClampLimit(0));
        Assert.Equal(12, StudentSearch.ClampLimit(12));
        Assert.Equal(StudentSearch.MaxLimit, StudentSearch.ClampLimit(500));
    }

    /* ---------- SQLite: ism bo'yicha (provayder-neytral tarmoq) ---------- */

    private static Student St(string fullName, string phone = "", string father = "",
        string fatherPhone = "", bool archived = false) => new()
    {
        FullName = fullName,
        Phone = phone,
        FatherFullName = father,
        FatherPhone = fatherPhone,
        IsArchived = archived,
    };

    [Fact]
    public async Task WhereNameFallback_SozTartibiMuhimEmas_ApostrofVariantiTopiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Toʻlqin Akramovich"),   // bazada ʻ (modifier letter) varianti
            St("Karimov Bekzod"),
            St("Toshmatova Lola"));
        await db.Context.SaveChangesAsync();

        // Odam "ism familiya" deb yozadi — bazada esa "Familiya Ism" turadi.
        var words = StudentSearch.Words("to'lqin aliyev");
        var found = await StudentSearch.WhereNameFallback(db.Context.Students.AsNoTracking(), words)
            .Select(s => s.FullName).ToListAsync();

        Assert.Equal(new[] { "Aliyev Toʻlqin Akramovich" }, found);
    }

    [Fact]
    public async Task WhereNameFallback_OtaOnaIsmiBoyichaHamTopadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Sardor", father: "Aliyev Botir"),
            St("Karimov Bekzod", father: "Karimov Olim"));
        await db.Context.SaveChangesAsync();

        var found = await StudentSearch
            .WhereNameFallback(db.Context.Students.AsNoTracking(), StudentSearch.Words("botir"))
            .Select(s => s.FullName).ToListAsync();

        Assert.Equal(new[] { "Aliyev Sardor" }, found);
    }

    /* ---------- SQLite: telefon bo'yicha ---------- */

    [Fact]
    public async Task WherePhone_AjratkichlarsizQismiyMoslik_OtaRaqamiHam()
    {
        using var db = TestDb.Sqlite();
        db.Context.Students.AddRange(
            St("Aliyev Sardor", phone: "+998-90-123-45-67"),
            St("Karimov Bekzod", fatherPhone: "+998 (91) 765-43-21"),
            St("Toshmatova Lola", phone: "+998-93-555-55-55"));
        await db.Context.SaveChangesAsync();

        var q = db.Context.Students.AsNoTracking();

        // O'z raqamining o'rtasidan qismiy moslik.
        Assert.Equal(new[] { "Aliyev Sardor" },
            await StudentSearch.WherePhone(q, "9012345").Select(s => s.FullName).ToListAsync());
        // Ota raqami bo'yicha ham topiladi.
        Assert.Equal(new[] { "Karimov Bekzod" },
            await StudentSearch.WherePhone(q, "917654").Select(s => s.FullName).ToListAsync());
    }

    [Fact]
    public void Normalize_TartiblashUchun_PrefiksAniqlanadi()
    {
        // Controller tartibi: birinchi so'z bilan BOSHLANGAN ism tepaga chiqadi.
        Assert.StartsWith("to'lqin", StudentSearch.Normalize("Toʻlqin Aliyev"));
    }
}
