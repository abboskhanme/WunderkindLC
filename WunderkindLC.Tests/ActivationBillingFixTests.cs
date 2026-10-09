using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-09 mijoz shikoyatlari (billing):
/// (1) aktiv qilib qo'shilgan o'quvchining qo'shilgan oyi hisoblanmay balans 0 qolardi — bazadagilarni
///     tuzatish vositasi <see cref="ActivationChargeRepair"/>;
/// (3) oy o'rtasidan (8-sanadan) qisman hisoblangan o'quvchi to'liq oylik to'lasa ortiqcha plyus qolardi —
///     aktivlashtirish sanasini o'zgartirish <see cref="ActivationDateChange"/>.
/// Sanalar <see cref="AppClock.Today"/> ga NISBATAN (oy o'tgan sari test buzilmasin).
/// </summary>
public class ActivationBillingFixTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private const decimal Fee = 300_000m;
    private const decimal Lesson = 25_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Har kuni dars (25-sanadan qolgan 4–7 dars × 25 000 — QISMAN; 1-sanadan — to'liq oylik).</summary>
    private static (Student S, Group G, StudentGroup M) Setup(AppDbContext ctx, string activatedAt, List<int>? days = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..6]; // kurs/guruh nomlari noyob bo'lsin (bir testda bir nechta)
        var course = new Subject { Name = "Koreys tili " + tag, Price = Fee, LessonPrice = Lesson };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "K guruh " + tag, MonthlyFee = Fee, CourseId = course.Id, Days = days ?? [0, 1, 2, 3, 4, 5, 6] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Rasulov Ramzbek", EnrollmentDate = activatedAt, ClassName = g.Name };
        ctx.Students.Add(s);
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = activatedAt, ActivatedAt = activatedAt,
        };
        ctx.StudentGroups.Add(m);
        return (s, g, m);
    }

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    private static async Task<decimal> Balance(AppDbContext ctx, Student s) =>
        (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance;

    private static Task<MonthlyCharge?> ChargeOf(AppDbContext ctx, Student s, Group g, string month) =>
        ctx.MonthlyCharges.AsNoTracking().FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == g.Id && c.Month == month);

    // ============================ (1) Qo'shilgan oy hisobini tuzatish ============================

    [Fact]
    public async Task Tuzatish__hisobsiz_qolgan_qoshilgan_oy_TOPILADI_yoziladi_va_ikkinchi_marta_TOPILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Import xatosi: joriy oy 1-sanasidan `active`, hisob qatori YO'Q, balans 0.
        var (s, g, _) = Setup(ctx, $"{M(0)}-01");
        await ctx.SaveChangesAsync();

        var found = await ActivationChargeRepair.FindAsync(ctx);
        var c = Assert.Single(found);
        Assert.Equal(Fee, c.Amount);           // oyning birinchi darsidan — to'liq oylik
        Assert.Equal(0m, c.Balance);
        Assert.Equal(0m, await Balance(ctx, s)); // FindAsync hech narsa yozmaydi

        var charged = await ActivationChargeRepair.ApplyOneAsync(ctx, Audit(ctx), s.Id, g.Id);
        await ctx.SaveChangesAsync();

        Assert.Equal(Fee, charged);
        Assert.Equal(-Fee, await Balance(ctx, s));
        Assert.Equal(Fee, (await ChargeOf(ctx, s, g, M(0)))!.Amount);
        Assert.Empty(await ActivationChargeRepair.FindAsync(ctx));
        // Ikkinchi bosish — hech narsa (idempotent).
        Assert.Equal(0m, await ActivationChargeRepair.ApplyOneAsync(ctx, Audit(ctx), s.Id, g.Id));
    }

    [Fact]
    public async Task Tuzatish__QISMAN_summa_otgan_oy_25_sanadan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, _) = Setup(ctx, $"{M(-1)}-25");
        await ctx.SaveChangesAsync();

        var c = Assert.Single(await ActivationChargeRepair.FindAsync(ctx));
        Assert.True(c.Amount > 0 && c.Amount < Fee);
        Assert.Equal(c.Amount, await ActivationChargeRepair.ApplyOneAsync(ctx, Audit(ctx), s.Id, g.Id));
    }

    [Fact]
    public async Task Tuzatish__hisobi_BOR_sinovdagi_eski_va_arxivdagi_TOPILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Hisob qatori bor (0 so'mlik qo'lda tahrir ham "bor") — tegilmaydi.
        var (s1, g1, _) = Setup(ctx, $"{M(0)}-01");
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s1.Id, GroupId = g1.Id, Month = M(0), Amount = 0, Locked = true });
        // Sinovdagi.
        var (_, _, m2) = Setup(ctx, $"{M(0)}-01");
        m2.Status = "trial";
        m2.ActivatedAt = "";
        // Uch oy oldin aktivlashtirilgan — eski tarixga orqaga qarz yozilmaydi.
        Setup(ctx, $"{M(-3)}-01");
        // Arxivdagi o'quvchi.
        var (s4, _, _) = Setup(ctx, $"{M(0)}-01");
        s4.IsArchived = true;
        await ctx.SaveChangesAsync();

        Assert.Empty(await ActivationChargeRepair.FindAsync(ctx));
    }

    [Fact]
    public async Task Bir_sorovda_IKKI_guruhda_aktivlashtirish__eski_guruhsiz_hisob_BIR_marta_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Eski (ClassName) o'quvchi: joriy oyga guruhsiz (aggregate) hisob bor, balans −Fee.
        var (s, gA, mA) = Setup(ctx, $"{M(0)}-01");
        var (_, gB, mB) = Setup(ctx, $"{M(0)}-01");
        mB.StudentId = s.Id;
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = null, Month = M(0), Amount = Fee });
        s.Balance = -Fee;
        await ctx.SaveChangesAsync();

        // Ikkala aktivlashtirish SAQLASHSIZ ketma-ket (import/ommaviy amal) — o'chirilgan aggregate qator
        // ikkinchi so'rovda ham qaytadi, lekin ikkinchi marta balansga QO'SHILMASLIGI kerak.
        await TuitionService.ChargeActivationProrateAsync(ctx, s, gA, mA.ActivatedAt);
        await TuitionService.ChargeActivationProrateAsync(ctx, s, gB, mB.ActivatedAt);
        await ctx.SaveChangesAsync();

        Assert.Equal(-2 * Fee, await Balance(ctx, s));
        Assert.Null(await ctx.MonthlyCharges.AsNoTracking().FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == null));
    }

    // ============================ (3) Aktivlashtirish sanasini o'zgartirish ============================

    [Fact]
    public void Sana_qoidalari__kelajak_keyingi_oy_ozgarmagan_va_aktiv_emas_RAD_etiladi()
    {
        var today = new DateOnly(2026, 10, 9);
        var sg = new StudentGroup { Status = "active", IsActive = true, ActivatedAt = "2026-10-08" };

        Assert.Null(ActivationDateChange.BlockReason(sg, "2026-10-01", today));
        Assert.Null(ActivationDateChange.BlockReason(sg, "2026-09-15", today));      // oldingi oyga — mumkin
        Assert.NotNull(ActivationDateChange.BlockReason(sg, "2026-10-08", today));   // o'zgarmadi
        Assert.NotNull(ActivationDateChange.BlockReason(sg, "2026-10-10", today));   // kelajak
        Assert.NotNull(ActivationDateChange.BlockReason(sg, "2026-13-01", today));   // buzuq
        Assert.NotNull(ActivationDateChange.BlockReason(sg, "2025-09-01", today));   // 12 oydan ortiq
        var later = new StudentGroup { Status = "active", IsActive = true, ActivatedAt = "2026-09-08" };
        Assert.NotNull(ActivationDateChange.BlockReason(later, "2026-10-01", today)); // keyingi oyga — yo'q
        var trial = new StudentGroup { Status = "trial", IsActive = true, ActivatedAt = "" };
        Assert.NotNull(ActivationDateChange.BlockReason(trial, "2026-10-01", today));
        // Yopilgan oldingi davrga tegadigan sana — yo'q.
        var past = new StudentGroup
        {
            Status = "active", IsActive = true, ActivatedAt = "2026-10-08", PastPeriods = ["2026-08-01|2026-09-20"],
        };
        Assert.NotNull(ActivationDateChange.BlockReason(past, "2026-09-25", today));
        Assert.Null(ActivationDateChange.BlockReason(past, "2026-10-01", today));
    }

    [Fact]
    public async Task Sana_SHU_OY_ichida_orqaga_surilsa_qisman_hisob_TOLIQ_oylikka_aylanadi__ortiqcha_plyus_qolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // O'tgan oyning 25-sanasidan aktivlashtirilgan — qisman hisob.
        var (s, g, m) = Setup(ctx, $"{M(-1)}-25");
        await ctx.SaveChangesAsync();
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, m.ActivatedAt);
        await ctx.SaveChangesAsync();
        var partial = (await ChargeOf(ctx, s, g, M(-1)))!.Amount;
        Assert.True(partial < Fee);

        var r = await ActivationDateChange.ApplyAsync(ctx, s, g, m, $"{M(-1)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(Fee, (await ChargeOf(ctx, s, g, M(-1)))!.Amount);
        Assert.Equal(-Fee, await Balance(ctx, s));
        var change = Assert.Single(r.Months);
        Assert.Equal((partial, Fee), (change.Before!.Value, change.After!.Value));
        Assert.Equal($"{M(-1)}-01", (await ctx.StudentGroups.AsNoTracking().SingleAsync()).ActivatedAt);
        Assert.Empty(r.LockedMonths);
    }

    [Fact]
    public async Task Sana_OLDINGI_oyga_surilsa_eski_oy_TOLIQ_oraliq_oylar_darhol_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, m) = Setup(ctx, $"{M(-1)}-25");
        await ctx.SaveChangesAsync();
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, m.ActivatedAt);
        await ctx.SaveChangesAsync();

        await ActivationDateChange.ApplyAsync(ctx, s, g, m, $"{M(-2)}-25");
        await ctx.SaveChangesAsync();

        var newPartial = (await ChargeOf(ctx, s, g, M(-2)))!.Amount;
        Assert.True(newPartial > 0 && newPartial < Fee);              // yangi aktivlashtirish oyi — qisman
        Assert.Equal(Fee, (await ChargeOf(ctx, s, g, M(-1)))!.Amount); // eski aktivlashtirish oyi — endi to'liq
        Assert.Equal(Fee, (await ChargeOf(ctx, s, g, M(0)))!.Amount);  // oraliq/joriy oy — catch-up
        Assert.Equal(-(newPartial + Fee + Fee), await Balance(ctx, s));
    }

    [Fact]
    public async Task Sana_KEYINROQ_surilsa_shu_oy_ichida_hisob_kamayadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, m) = Setup(ctx, $"{M(-1)}-01");
        await ctx.SaveChangesAsync();
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, m.ActivatedAt);
        await ctx.SaveChangesAsync();
        Assert.Equal(-Fee, await Balance(ctx, s));

        await ActivationDateChange.ApplyAsync(ctx, s, g, m, $"{M(-1)}-25");
        await ctx.SaveChangesAsync();

        var partial = (await ChargeOf(ctx, s, g, M(-1)))!.Amount;
        Assert.True(partial < Fee);
        Assert.Equal(-partial, await Balance(ctx, s));
    }

    [Fact]
    public async Task Qolda_tahrirlangan_oy_TEGILMAYDI_va_natijada_aytiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, m) = Setup(ctx, $"{M(-1)}-25");
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 111_000m, Locked = true });
        s.Balance = -111_000m;
        await ctx.SaveChangesAsync();

        var r = await ActivationDateChange.ApplyAsync(ctx, s, g, m, $"{M(-1)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(111_000m, (await ChargeOf(ctx, s, g, M(-1)))!.Amount);
        Assert.Equal(-111_000m, await Balance(ctx, s));
        Assert.Equal(new[] { M(-1) }, r.LockedMonths);
    }

    [Fact]
    public async Task Sanadan_keyin_oyda_DARS_qolmasa_oy_hisobi_olib_tashlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Darssiz guruh (jadval kiritilmagan) — qisman summa 0: qulfsiz eski hisob qolmasin.
        var (s, g, m) = Setup(ctx, $"{M(-1)}-01", days: []);
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = Fee });
        s.Balance = -Fee;
        await ctx.SaveChangesAsync();

        await ActivationDateChange.ApplyAsync(ctx, s, g, m, $"{M(-1)}-20");
        await ctx.SaveChangesAsync();

        Assert.Null(await ChargeOf(ctx, s, g, M(-1)));
        Assert.Equal(0m, await Balance(ctx, s));
    }
}
