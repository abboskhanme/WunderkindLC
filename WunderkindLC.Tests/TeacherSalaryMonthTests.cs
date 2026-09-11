using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QITUVCHI FOIZLI MAOSHI — TO'LOV QAYSI OYGA HISOBLANADI.
///
/// <para>Qoida: to'lov QAYSI OY UCHUN qilingan bo'lsa (<see cref="FinanceTransaction.Month"/>), shu oy
/// maoshiga kiradi — to'lov SANASI emas. Masalan 3-avgustda IYUL uchun to'lansa, pul o'qituvchining
/// IYUL maoshiga kiradi (u iyulda dars bergan). Bu markazdagi boshqa hamma joy bilan bir xil
/// konvensiya; ilgari <c>SalaryLedger.CollectedPerGroupAsync</c> da SANA ishlatilar edi va o'qituvchi
/// profilida bitta qatorning ikki yarmi turlicha hisoblanardi.</para>
/// </summary>
public class TeacherSalaryMonthTests
{
    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>O'qituvchi + uning 50% foizli guruhi + guruhdagi faol o'quvchi.</summary>
    private static (Teacher Teacher, Group Group, Student Student) Setup(AppDbContext ctx)
    {
        var t = new Teacher { FullName = "Test O'qituvchi", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "A guruh", MonthlyFee = 600_000m, TeacherId = t.Id,
            TeacherSalaryMode = "percent", TeacherSalaryPercent = 50,
            Days = new List<int> { 0, 2, 4 },
        };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Ali Valiyev", EnrollmentDate = $"{M(-6)}-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(-6)}-01", ActivatedAt = $"{M(-6)}-01", RecordedAt = $"{M(-6)}-01",
        });
        return (t, g, s);
    }

    private static void AddTuition(
        AppDbContext ctx, Student s, Group g, string month, string date, decimal amount,
        string direction = "income", string category = "tuition")
    {
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = date, Direction = direction, Category = category, Amount = amount,
            StudentId = s.Id, GroupId = g.Id, Month = month, Method = "cash",
        });
    }

    [Fact]
    public async Task Otgan_oy_uchun_kech_tolangan_pul_OSHA_OY_maoshiga_kiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        // O'quvchi JORIY oyning 3-kunida O'TGAN oy uchun to'ladi.
        AddTuition(ctx, s, g, month: M(-1), date: $"{M(0)}-03", amount: 600_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(0));

        var otgan = dto.Months.Single(x => x.Month == M(-1));
        var joriy = dto.Months.Single(x => x.Month == M(0));

        // 600 000 × 50% = 300 000 — O'TGAN oyga (o'qituvchi o'sha oyda dars bergan).
        Assert.Equal(300_000m, otgan.BaseExpected);
        Assert.Equal(0m, joriy.BaseExpected);
    }

    [Fact]
    public async Task Kelasi_oy_uchun_oldindan_tolangan_pul_HOZIRGI_oyga_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        // Avans: joriy oyning 10-kunida KELASI oy uchun to'lov.
        AddTuition(ctx, s, g, month: M(1), date: $"{M(0)}-10", amount: 600_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(0), M(1));

        Assert.Equal(0m, dto.Months.Single(x => x.Month == M(0)).BaseExpected);
        Assert.Equal(300_000m, dto.Months.Single(x => x.Month == M(1)).BaseExpected);
    }

    [Fact]
    public async Task Oyi_korsatilmagan_ESKI_tolov_sana_boyicha_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        // Eski yozuv: Moliya formasi `Month`ni saqlamagan — orqaga moslik uchun sana ishlatiladi.
        AddTuition(ctx, s, g, month: null!, date: $"{M(-1)}-15", amount: 400_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(0));

        Assert.Equal(200_000m, dto.Months.Single(x => x.Month == M(-1)).BaseExpected);
        Assert.Equal(0m, dto.Months.Single(x => x.Month == M(0)).BaseExpected);
    }

    [Fact]
    public async Task Vozvrat_HAM_osha_oy_bazasidan_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        AddTuition(ctx, s, g, month: M(-1), date: $"{M(-1)}-05", amount: 600_000m);
        // Vozvrat KEYINGI oyda qilingan, lekin O'SHA (iyul) oy uchun — baza o'sha oyda kamayadi.
        AddTuition(ctx, s, g, month: M(-1), date: $"{M(0)}-02", amount: 200_000m,
            direction: "expense", category: "refund");
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(0));

        // (600 000 − 200 000) × 50% = 200 000
        Assert.Equal(200_000m, dto.Months.Single(x => x.Month == M(-1)).BaseExpected);
        Assert.Equal(0m, dto.Months.Single(x => x.Month == M(0)).BaseExpected);
    }

    [Fact]
    public async Task Guruh_kesimidagi_yigilgan_ham_TOLOV_OYI_boyicha_chiqadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        AddTuition(ctx, s, g, month: M(-1), date: $"{M(0)}-03", amount: 600_000m);
        await ctx.SaveChangesAsync();

        // Davr FAQAT o'tgan oy — kech to'langan pul baribir shu davrga tushadi.
        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1));

        var line = Assert.Single(dto.Groups!);
        Assert.Equal(600_000m, line.PeriodCollected);
        Assert.Equal(300_000m, line.PeriodExpected);
    }
}
