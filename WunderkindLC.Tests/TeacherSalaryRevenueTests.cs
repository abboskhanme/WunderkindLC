using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QITUVCHI KARTOCHKASIDAGI OY TAFSILOTI — TUSHUM raqamlari.
///
/// <para>Admin tanlangan oy uchun beshta raqamni ko'radi: "aslida qancha tushum bo'lishi kerak edi",
/// "qancha tushum bo'ldi", "qancha oylik hisoblandi", "qanchasi berildi", "qanchasi qoldi".
/// Dastlabki uchtasidan ikkitasi — <see cref="Application.Dtos.MonthSalaryDto.TuitionCharged"/> va
/// <see cref="Application.Dtos.MonthSalaryDto.TuitionCollected"/>.</para>
///
/// <para>Ikki nozik joy tekshiriladi:</para>
/// <list type="number">
///   <item>tushum QAT'IY maoshli o'qituvchida ham hisoblanadi (maosh rejimiga bog'liq emas) —
///     ilgari tuition bazasi faqat foizli maosh uchun o'qilardi;</item>
///   <item><c>withRevenue</c> berilmasa raqamlar 0 bo'lib qoladi — Moliya → "O'qituvchilar"
///     hisoboti bu metodni HAR BIR o'qituvchi uchun chaqiradi va u yerda ortiqcha so'rov
///     qilinmasligi kerak.</item>
/// </list>
/// </summary>
public class TeacherSalaryRevenueTests
{
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>QAT'IY maoshli o'qituvchi (foiz YO'Q) + guruh + o'quvchi.</summary>
    private static (Teacher Teacher, Group Group, Student Student) Setup(AppDbContext ctx)
    {
        var t = new Teacher
        {
            FullName = "Qat'iy maoshli o'qituvchi", SalaryMode = "fixed", Salary = 2_000_000m,
            SalaryStartDate = $"{M(-6)}-01",
        };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "Guruh", MonthlyFee = 600_000m, TeacherId = t.Id,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
            StartDate = $"{M(-6)}-01",
        };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "O'quvchi", EnrollmentDate = $"{M(-6)}-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(-6)}-01", ActivatedAt = $"{M(-6)}-01", RecordedAt = $"{M(-6)}-01",
        });
        return (t, g, s);
    }

    /// <summary>
    /// Hisob 600 000, to'langani 250 000 → "bo'lishi kerak" va "bo'ldi" ayni shu raqamlar,
    /// farqi esa shu oy bo'yicha yig'ilmagan qarz. Maosh QAT'IY bo'lgani uchun tushum unga
    /// ta'sir qilmaydi — oylik o'z summasida qoladi.
    /// </summary>
    [Fact]
    public async Task Qatiy_maoshda_ham_oylik_TUSHUM_korsatiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 600_000m,
            Date = $"{M(-1)}-01",
        });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-10", Direction = "income", Category = "tuition", Amount = 250_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1), withRevenue: true);
        var oy = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(600_000m, oy.TuitionCharged);      // aslida qancha bo'lishi kerak edi
        Assert.Equal(250_000m, oy.TuitionCollected);    // qancha tushdi
        Assert.Equal(2_000_000m, oy.Expected);          // qat'iy oylik — tushumga bog'liq emas
    }

    /// <summary>
    /// Chegirma "bo'lishi kerak" raqamidan AYRILADI: markaz o'zi kechgan pulni "yig'ilmagan qarz"
    /// deb ko'rsatish noto'g'ri bo'lardi (kassir bor-yo'g'i 400 000 ni undirishi kerak).
    /// </summary>
    [Fact]
    public async Task Chegirma_tushum_rejasidan_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 600_000m, Discount = 200_000m,
            Date = $"{M(-1)}-01",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1), withRevenue: true);
        var oy = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(400_000m, oy.TuitionCharged);
        Assert.Equal(0m, oy.TuitionCollected);
    }

    /// <summary>
    /// TO'LOV QAYSI OYGA — <c>Month</c> tegi, sana EMAS: o'tgan oy uchun bu oyda to'langan pul
    /// O'TGAN oy tushumiga tushadi (butun tizimdagi konvensiya bilan bir xil).
    /// </summary>
    [Fact]
    public async Task Kech_tolangan_pul_OZ_oyining_tushumiga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-03", Direction = "income", Category = "tuition", Amount = 600_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(0), withRevenue: true);

        Assert.Equal(600_000m, dto.Months.Single(x => x.Month == M(-1)).TuitionCollected);
        Assert.Equal(0m, dto.Months.Single(x => x.Month == M(0)).TuitionCollected);
    }

    /// <summary>
    /// VOZVRAT tushumdan ayriladi — qaytarilgan pul "kelgan" hisoblanmaydi.
    /// </summary>
    [Fact]
    public async Task Vozvrat_tushumdan_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-05", Direction = "income", Category = "tuition", Amount = 600_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-20", Direction = "expense", Category = "refund", Amount = 100_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1), withRevenue: true);

        Assert.Equal(500_000m, dto.Months.Single(x => x.Month == M(-1)).TuitionCollected);
    }

    /// <summary>
    /// <c>withRevenue</c> BERILMASA qat'iy maoshli o'qituvchida tuition umuman o'qilmaydi
    /// (Moliya hisoboti va o'qituvchi ilovasi shu yo'ldan keladi) — raqamlar 0.
    /// </summary>
    [Fact]
    public async Task withRevenue_berilmasa_tushum_oqilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 600_000m,
            Date = $"{M(-1)}-01",
        });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-10", Direction = "income", Category = "tuition", Amount = 600_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1));
        var oy = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(0m, oy.TuitionCharged);
        Assert.Equal(0m, oy.TuitionCollected);
        Assert.Equal(2_000_000m, oy.Expected);   // maosh hisobi O'ZGARMAYDI
    }

    /// <summary>
    /// FOIZLI maoshda tushum raqamlari maosh bazasi bilan MOS keladi (barcha guruhlar foizli):
    /// "tushum bo'ldi" × foiz = "oylik hisoblandi" — admin raqamlarni bir-biriga solishtira oladi.
    /// </summary>
    [Fact]
    public async Task Foizli_maoshda_tushum_va_maosh_bazasi_mos_keladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        t.SalaryMode = "percent";
        t.SalaryPercent = 40m;
        g.TeacherSalaryMode = "percent";
        g.TeacherSalaryPercent = 40m;
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-10", Direction = "income", Category = "tuition", Amount = 500_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1), withRevenue: true);
        var oy = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(500_000m, oy.TuitionCollected);
        Assert.Equal(oy.Collected, oy.TuitionCollected);   // hamma guruh foizli → bazalar teng
        Assert.Equal(200_000m, oy.Expected);               // 500 000 × 40%
    }
}
