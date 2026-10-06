using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// GURUHDAN CHIQISH — <see cref="MembershipBilling.LeaveGroupAsync"/> (chiqarish + guruh almashtirishning
/// eski tomoni). Foydalanuvchi talabi (2026-10-06): o'tkazilgan/chiqarilgan o'quvchi eski guruhda
/// «muzlatilgan» bo'lib QOTIB QOLMASIN va ikki guruhda birdan ko'rinmasin; hisob esa chiqish sanasigacha.
/// </summary>
public class MembershipLeaveTests
{
    private const decimal Fee = 600_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private static int DaysIn(int delta)
    {
        var d = AppClock.Today.AddMonths(delta);
        return DateTime.DaysInMonth(d.Year, d.Month);
    }

    /// <summary>Har kuni dars — "dars soni" = kunlar soni (qisman oy oson tekshiriladi).</summary>
    private static (Student S, Group G) Seed(AppDbContext ctx, decimal balance = 0m)
    {
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 } };
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-6)}-01", Balance = balance };
        ctx.Classes.Add(g);
        ctx.Students.Add(s);
        return (s, g);
    }

    private static StudentGroup Member(AppDbContext ctx, Student s, Group g, string status, string activatedAt = "", string frozenAt = "")
    {
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = true,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = activatedAt, FrozenAt = frozenAt, RecordedAt = $"{M(-3)}-01",
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    private static void Charge(AppDbContext ctx, Student s, Group g, string month, decimal amount) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = month, Amount = amount, Date = $"{month}-01" });

    [Fact]
    public async Task Aktiv_azolik_chiqsa__chiqish_oyi_QISMAN__keyingi_oylar_BEKOR__muzlatilgan_bolib_QOLMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -2 * Fee);
        var m = Member(ctx, s, g, "active", activatedAt: $"{M(-3)}-01");
        Charge(ctx, s, g, M(-1), Fee);
        Charge(ctx, s, g, M(0), Fee);   // chiqishdan keyingi oy (oldindan yozilgan)
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.LeaveGroupAsync(ctx, s, g, m, $"{M(-1)}-10");
        await ctx.SaveChangesAsync();

        var saved = await ctx.StudentGroups.AsNoTracking().SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal($"{M(-1)}-10", saved.LeftAt);
        Assert.Equal("active", saved.Status);   // holat o'zgarmaydi — chegara LeftAt
        Assert.Equal("", saved.FrozenAt);        // «muzlatilgan» emas

        Assert.Equal(Fee, res.Restored);
        var row = Assert.Single(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
        Assert.Equal(M(-1), row.Month);
        Assert.Equal(decimal.Round(Fee * 10 / DaysIn(-1), 2), row.Amount);
        Assert.Equal(-row.Amount, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);

        // Chiqqandan keyin pullik emas, keyingi oy hisobi ham yozilmaydi.
        Assert.True(MembershipLifecycle.BillableInMonth(saved, M(-1)));
        Assert.False(MembershipLifecycle.BillableInMonth(saved, M(0)));
        await TuitionService.AccrueMonth(ctx, M(0));
        await ctx.SaveChangesAsync();
        Assert.Single(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Muzlatilgan_va_sinovdagi_azolik_chiqsa_PULGA_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -Fee);
        var frozen = Member(ctx, s, g, "frozen", activatedAt: $"{M(-3)}-01", frozenAt: $"{M(-1)}-15");
        Charge(ctx, s, g, M(-1), Fee);
        var g2 = new Group { Name = "B guruh", MonthlyFee = Fee, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g2);
        var trial = Member(ctx, s, g2, "trial");
        await ctx.SaveChangesAsync();

        Assert.Equal(0m, (await MembershipBilling.LeaveGroupAsync(ctx, s, g, frozen, $"{M(0)}-01")).Restored);
        Assert.Equal(0m, (await MembershipBilling.LeaveGroupAsync(ctx, s, g2, trial, $"{M(0)}-01")).Restored);
        await ctx.SaveChangesAsync();

        Assert.All(await ctx.StudentGroups.AsNoTracking().ToListAsync(), x => Assert.False(x.IsActive));
        Assert.Equal(Fee, (await ctx.MonthlyCharges.AsNoTracking().SingleAsync()).Amount);
        Assert.Equal(-Fee, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
    }

    [Fact]
    public void Otkazilgan_oquvchi_eski_guruhda_KETGAN__holati_faqat_yangi_guruhdan()
    {
        // Eski a'zolik chiqqan, yangisi aktiv — o'quvchi bitta (aktiv) bo'lib sanaladi, eski guruhda
        // «muzlatilgan» qatori yo'q.
        var left = new StudentGroup { Status = "active", IsActive = false, ActivatedAt = $"{M(-3)}-01", LeftAt = $"{M(0)}-06" };
        var live = new StudentGroup { Status = "active", IsActive = true, ActivatedAt = $"{M(0)}-06" };

        var tally = MembershipLifecycle.Tally(new[] { left, live }.Select(x => (x.Status, x.IsActive, x.LeftAt)));
        Assert.Equal(1, tally.Active);
        Assert.Equal(0, tally.Frozen);
        Assert.Equal(1, tally.Left);
        Assert.False(MembershipLifecycle.OccupiesSeat(left));
        Assert.True(MembershipLifecycle.IsActiveOverall(new[] { left, live }));
    }

    // ============================ Tekshiruvdan keyingi tuzatishlar ============================

    [Fact]
    public async Task Sinovdagi_HECH_aktivlashtirilmagan_azolik_muzlatilsa_oylik_YOZILMAYDI()
    {
        // Ilgari bo'sh `ActivatedAt` "oy boshidan faol" deb olinib, oyning 1-kunidan beri darslar yozilardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.SettleFreezeAsync(ctx, s, g, "", $"{M(-1)}-20");
        await ctx.SaveChangesAsync();

        Assert.False(res.Charged);
        Assert.Empty(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
        Assert.Equal(0m, (await ctx.Students.AsNoTracking().SingleAsync()).Balance);
    }

    [Fact]
    public async Task Shu_oyda_chiqib_QAYTA_qoshilib_aktivlashtirilsa_oldingi_segment_SAQLANADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx, balance: -Fee);
        var m = Member(ctx, s, g, "active", activatedAt: $"{M(-3)}-01");
        Charge(ctx, s, g, M(-1), Fee);
        await ctx.SaveChangesAsync();

        // 10-sanada chiqdi (1..10 hisoblandi), 20-sanada qayta qo'shilib aktivlashtirildi.
        await MembershipBilling.LeaveGroupAsync(ctx, s, g, m, $"{M(-1)}-10");
        await ctx.SaveChangesAsync();
        MembershipLifecycle.ClosePeriod(m, $"{M(-1)}-20");                   // AddMember / ActivateCoreAsync
        Assert.True(MembershipLifecycle.ClosedPeriodEndsInMonth(m, M(-1)));
        Assert.False(MembershipLifecycle.ClosedPeriodEndsInMonth(m, M(0)));
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, $"{M(-1)}-20",
            addSegment: MembershipLifecycle.ClosedPeriodEndsInMonth(m, M(-1)));
        await ctx.SaveChangesAsync();

        var days = DaysIn(-1);
        var expected = Math.Min(Fee, decimal.Round(Fee * 10 / days, 2) + decimal.Round(Fee * (days - 19) / days, 2));
        Assert.Equal(expected, (await ctx.MonthlyCharges.AsNoTracking().SingleAsync()).Amount, 0);
    }

    [Fact]
    public async Task Jurnal__chiqqan_oquvchi_faqat_OQIGAN_oylarida__faqat_korish_va_CHIQGAN_belgisi_bilan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);
        var left = Member(ctx, s, g, "active", activatedAt: $"{M(-2)}-01");
        left.JoinedAt = $"{M(-2)}-01";
        left.IsActive = false;
        left.LeftAt = $"{M(-1)}-10";
        await ctx.SaveChangesAsync();

        var before = await JournalService.GroupMonthAsync(ctx, g.Id, M(-1));
        var row = Assert.Single(before!.Students);
        Assert.Equal("frozen", row.Status);          // klientda faqat ko'rish bloki
        Assert.Equal("left", row.Ended);
        Assert.Equal($"{M(-1)}-10", row.FrozenAt);   // oxirgi kuni

        var after = await JournalService.GroupMonthAsync(ctx, g.Id, M(0));
        Assert.Empty(after!.Students);               // chiqqandan keyingi oyda yo'q
    }
}

