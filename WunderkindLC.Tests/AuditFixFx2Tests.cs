using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Post-merge revyu tuzatishlari (FX2) — BILLING DVIGATELI:
/// R1-5 aktivlashtirish oyi (hisobi yo'q) to'lov kelganda QISMAN hisob bilan ochiladi (tekin oy yo'q);
/// R1-6 fon accrual'i bitta o'quvchining xatosida to'xtamaydi va qulf ichida qayta tekshiradi;
/// R1-7 kuzatilayotgan o'quvchi balansini qayta o'qishda EF snapshot'i ham yangilanadi (UPDATE yo'qolmaydi).
/// Sanalar <see cref="AppClock.Today"/> ga NISBATAN.
/// </summary>
public class AuditFixFx2Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private const decimal Fee = 300_000m;
    private const decimal Lesson = 25_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");
    private static int DaysIn(string month) => DateTime.DaysInMonth(int.Parse(month[..4]), int.Parse(month[5..7]));

    /// <summary>Kurs (bir dars 25 000) + guruh + FAOL a'zolik (o'quvchi yaratishdagi kabi — hisob qatorisiz).</summary>
    private static (Student S, Group G, StudentGroup M) Setup(AppDbContext ctx, string activatedAt, List<int>? days = null)
    {
        var course = new Subject { Name = "Ingliz tili", Price = Fee, LessonPrice = Lesson };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, CourseId = course.Id, Days = days ?? [0, 1, 2, 3, 4, 5, 6] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = activatedAt, ClassName = g.Name };
        ctx.Students.Add(s);
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = activatedAt, ActivatedAt = activatedAt,
        };
        ctx.StudentGroups.Add(m);
        return (s, g, m);
    }

    private static Task<PaymentIntakeResult> Pay(AppDbContext ctx, Student s, string? groupId, string month, decimal amount = Fee) =>
        PaymentIntake.AddAsync(ctx, new AuditService(ctx, new NoHttpContext()), new MessagingStack().Auto, s,
            new PaymentRequest(amount, month, groupId, Method: "cash", RequestId: Guid.NewGuid().ToString()),
            createdBy: "Kassir", createdById: "kassir-1");

    private static async Task<decimal> Balance(AppDbContext ctx, Student s) =>
        (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance;

    private static Task<MonthlyCharge?> ChargeOf(AppDbContext ctx, Student s, string? groupId, string month) =>
        ctx.MonthlyCharges.AsNoTracking().FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == month);

    // ============================ R1-5 — aktivlashtirish oyi ============================

    [Fact]
    public async Task Yaratilganda_ACTIVE_oquvchi_KEYINGI_oyni_tolasa_aktivlashtirish_oyi_ham_QISMAN_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Import: O'TGAN oyning 25-sanasidan `active`, qisman hisob YOZILMAGAN; oila faqat KEYINGI (joriy) oyni
        // to'laydi. (25-sanadan qolgan 4–7 dars × 25 000 < oylik — ya'ni summa haqiqatan QISMAN.)
        var (s, g, _) = Setup(ctx, $"{M(-1)}-25");
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, g.Id, M(0));

        Assert.Null(res.Error);
        var act = await ChargeOf(ctx, s, g.Id, M(-1));
        Assert.NotNull(act);                                   // birinchi oy TEKIN qolmadi
        Assert.True(act!.Amount > 0 && act.Amount < Fee);      // qisman (25-sanadan qolgan darslar)
        Assert.NotNull(await ChargeOf(ctx, s, g.Id, M(0)));    // to'langan oy ham hisoblandi
    }

    [Fact]
    public async Task Eski_aktivlashtirish_oyiga_orqaga_hisob_YOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // Uch oy oldin aktivlashtirilgan (o'sha oy qatori yo'q — masalan admin o'chirgan): bugungi to'lov uni tiriltirmaydi.
        var (s, g, _) = Setup(ctx, $"{M(-3)}-08");
        await ctx.SaveChangesAsync();

        await Pay(ctx, s, g.Id, M(0));

        Assert.Null(await ChargeOf(ctx, s, g.Id, M(-3)));
    }

    [Fact]
    public async Task Yaratilganda_ACTIVE_oquvchi_aktivlashtirish_oyiga_TOLASA_hisob_ochiladi_TEKIN_oy_yoq()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // `AddMembershipRow`: ActivatedAt = joriy oy boshi (qisman hisob YOZILMAGAN).
        var (s, g, _) = Setup(ctx, $"{M(0)}-01");
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, g.Id, M(0));

        Assert.Null(res.Error);
        var charge = await ChargeOf(ctx, s, g.Id, M(0));
        Assert.NotNull(charge);
        Assert.Equal(Fee, charge!.Amount);          // oyning birinchi darsidan — to'liq oylik
        Assert.Equal(0m, await Balance(ctx, s));    // to'lov shu oyni yopdi, AVANS bo'lib qolmadi
        // Accrual bu oyni QAYTA yozmaydi (aktivlashtirish oyi — ikki marta hisob yo'q).
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
    }

    [Fact]
    public async Task Oy_ORTASIDA_aktivlashtirilgan_oyga_tolov__QISMAN_hisob_ActivatedAt_dan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var month = M(-1);
        var (s, g, _) = Setup(ctx, $"{month}-25");
        await ctx.SaveChangesAsync();

        await Pay(ctx, s, g.Id, month);

        var expected = (DaysIn(month) - 24) * Lesson; // 25-kundan oy oxirigacha har kuni dars
        var charge = await ChargeOf(ctx, s, g.Id, month);
        Assert.NotNull(charge);
        Assert.Equal(expected, charge!.Amount);
        Assert.Equal($"{month}-25", charge.Date);     // `ChargeActivationProrateAsync` qatori bilan bir xil
        // To'liq oylik qisman oyga kiritilgan — ORTIG'I keyingi oyga taqsimlanadi (`PaymentMonthSplit`, 2026-10-23):
        // ilgari u aktivlashtirish oyida avans bo'lib qolar va keyingi oy per-guruh hisobda "to'lanmagan" ko'rinardi.
        var txs = await ctx.FinanceTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(expected, txs.Single(t => t.SplitFromId == null && t.Month == month).Amount);
        Assert.Equal(Fee - expected, txs.Single(t => t.SplitFromId != null && t.Month == M(0)).Amount);
        Assert.Equal(Fee - expected - Fee, await Balance(ctx, s)); // keyingi oy hisobi avans qoidasi bilan ochildi
        // Aktivlashtirish oyi ham, keyingi oy ham qayta yozilmaydi.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, month)).Count);
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Equal(Fee, (await ChargeOf(ctx, s, g.Id, M(0)))!.Amount);
    }

    [Fact]
    public async Task Oyning_OXIRGI_darsidan_keyin_aktivlashtirilgan__hisob_OCHILMAYDI_pul_AVANS()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var month = M(-1);
        var last = new DateOnly(int.Parse(month[..4]), int.Parse(month[5..7]), DaysIn(month));
        var lessonDay = ((int)last.AddDays(-1).DayOfWeek + 6) % 7; // dars faqat oxirgi kundan BIR kun oldin
        var (s, g, _) = Setup(ctx, last.ToString("yyyy-MM-dd"), [lessonDay]);
        await ctx.SaveChangesAsync();

        Assert.False(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, month));
        Assert.Equal(0m, await TuitionService.ActivationChargePreviewAsync(ctx, s, g, month));
        await Pay(ctx, s, g.Id, month);

        Assert.Null(await ChargeOf(ctx, s, g.Id, month));
        Assert.Equal(Fee, await Balance(ctx, s));
    }

    [Fact]
    public void Aktivlashtirish_oyi_azoligi__faqat_FAOL_tirik_va_haqiqiy_sanali()
    {
        var month = "2026-09";
        StudentGroup Sg(string status = "active", bool alive = true, string act = "2026-09-15", string frozen = "") =>
            new() { StudentId = "s", GroupId = "g", Status = status, IsActive = alive, ActivatedAt = act, FrozenAt = frozen };

        Assert.NotNull(TuitionService.ActivationMonthMembership([Sg()], month));
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(status: "trial")], month));
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(status: "frozen")], month));
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(alive: false)], month));
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(act: "2026-08-15")], month)); // boshqa oy
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(act: "2026-09-99")], month)); // buzuq sana
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(act: "2026-09")], month));    // kun yo'q
        Assert.Null(TuitionService.ActivationMonthMembership([Sg(frozen: "2026-09-20")], month));
    }

    [Fact]
    public async Task Kelmagan_darslar_chegirmasi_AKTIVLASHTIRISH_oyida_QISMAN_hisobdan_oshmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var month = M(-1);
        var (s, g, _) = Setup(ctx, $"{month}-25");
        await ctx.SaveChangesAsync();
        var prorate = (DaysIn(month) - 24) * Lesson;

        var q = await LessonDiscountService.QuoteAsync(ctx, s, g, month, 10);
        Assert.Equal(prorate, q.Remaining);   // ilgari to'liq oylik (300 000) dan hisoblanardi
        Assert.Equal(prorate, q.Amount);      // 10 × 25 000 = 250 000 > qisman hisob

        await LessonDiscountService.ApplyAsync(ctx, s, g, month, 10, "kasal edi", "admin-1", "Admin");
        await ctx.SaveChangesAsync();

        var charge = await ChargeOf(ctx, s, g.Id, month);
        Assert.NotNull(charge);
        Assert.Equal(prorate, charge!.Amount);
        Assert.True(charge.Discount <= charge.Amount);
        Assert.Equal(0m, await Balance(ctx, s)); // yechilgan qisman hisob to'liq qoplandi — ortiqcha pul yo'q
    }

    // ============================ R1-7 — kuzatilayotgan balans snapshot'i ============================

    [Fact]
    public async Task Balans_qayta_oqilganda_ESKI_snapshotga_teng_natija_ham_SAQLANADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, _, _) = Setup(ctx, $"{M(-3)}-01");
        s.Balance = 100_000m;
        await ctx.SaveChangesAsync();                 // ctx: s KUZATILMOQDA, snapshot = 100 000

        using (var other = db.NewContext())           // parallel so'rov: vozvrat −50 000
        {
            var o = await other.Students.SingleAsync(x => x.Id == s.Id);
            o.Balance = 50_000m;
            await other.SaveChangesAsync();
        }

        Assert.Equal(50_000m, await StudentBalanceSync.ReloadAsync(ctx, s));
        s.Balance += 50_000m;                         // +50 000 → 100 000 = ESKI snapshot
        await ctx.SaveChangesAsync();

        Assert.Equal(100_000m, await Balance(ctx, s)); // ilgari UPDATE ketmay, bazada 50 000 qolardi
    }

    [Fact]
    public async Task Tolov__oldin_yuklangan_oquvchi_bilan_ham_parallel_ozgarish_YOQOLMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g, _) = Setup(ctx, $"{M(-3)}-01");
        await ctx.SaveChangesAsync();                 // s kuzatilmoqda, balans 0

        // Shu orada boshqa yo'l M(0) hisobini yozdi (−300 000).
        using (var other = db.NewContext())
        {
            other.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = Fee, Date = $"{M(0)}-01" });
            (await other.Students.SingleAsync(x => x.Id == s.Id)).Balance -= Fee;
            await other.SaveChangesAsync();
        }

        await Pay(ctx, s, g.Id, M(0));                // −300 000 + 300 000 = 0 = eski snapshot

        Assert.Equal(0m, await Balance(ctx, s));
        Assert.Single(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
    }

    // ============================ R1-6 — fon accrual chidamliligi ============================

    private sealed class CountingLogger : ILogger
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings++;
        }
    }

    /// <summary>Birinchi saqlashni (qurollantirilgandan keyin) unikal indeks xatosi kabi rad etadi.</summary>
    private sealed class FailFirstSave : SaveChangesInterceptor
    {
        public bool Armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                throw new DbUpdateException("sinov: unikal indeks buzildi");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Birinchi o'quvchi saqlanayotganda IKKINCHISIGA "parallel" hisob yozadi (to'lovdagi
    /// `EnsureChargeAsync` kabi) — ya'ni hisob `already` to'plami o'qilgandan KEYIN paydo bo'ladi.</summary>
    private sealed class ConcurrentChargeOnFirstSave(DbContextOptions<AppDbContext> plain, string groupId, string month)
        : SaveChangesInterceptor
    {
        public bool Armed;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                var saving = eventData.Context!.ChangeTracker.Entries<MonthlyCharge>()
                    .First(e => e.State == EntityState.Added).Entity.StudentId;
                using var other = new AppDbContext(plain);
                var victim = await other.Students.FirstAsync(x => x.Id != saving, cancellationToken);
                other.MonthlyCharges.Add(new MonthlyCharge { StudentId = victim.Id, GroupId = groupId, Month = month, Amount = Fee, Date = $"{month}-01" });
                victim.Balance -= Fee;
                await other.SaveChangesAsync(cancellationToken);
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Ikki faol o'quvchi (M(-3) dan), bitta guruh.</summary>
    private static async Task<(string A, string B, string G)> SeedTwo(AppDbContext ctx)
    {
        var (a, g, _) = Setup(ctx, $"{M(-3)}-01");
        var b = new Student { FullName = "Ikkinchi", EnrollmentDate = $"{M(-3)}-01", ClassName = g.Name };
        ctx.Students.Add(b);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = b.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = $"{M(-3)}-01",
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return (a.Id, b.Id, g.Id);
    }

    [Fact]
    public async Task AccrueMonth_bitta_oquvchi_SAQLANMASA_qolganlari_hisoblanadi_va_Created_qaytadi()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var fail = new FailFirstSave();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).AddInterceptors(fail).Options;
        using var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        var (a, b, _) = await SeedTwo(ctx);
        var log = new CountingLogger();

        fail.Armed = true;
        var (count, total, created) = await TuitionService.AccrueMonth(ctx, M(0), log);

        Assert.Equal(1, count);                       // ilgari istisno butun yugurishni uzardi
        Assert.Equal(Fee, total);
        Assert.Single(created);
        Assert.Equal(1, log.Warnings);
        Assert.DoesNotContain(ctx.ChangeTracker.Entries<MonthlyCharge>(), e => e.State == EntityState.Added);

        // Keyingi yugurish o'tkazib yuborilganini yozadi — dublikatsiz.
        var (again, _, createdAgain) = await TuitionService.AccrueMonth(ctx, M(0), log);
        Assert.Equal(1, again);
        Assert.NotEqual(created[0].StudentId, createdAgain[0].StudentId);
        var charges = await ctx.MonthlyCharges.AsNoTracking().Where(c => c.Month == M(0)).ToListAsync();
        Assert.Equal(2, charges.Count);
        foreach (var id in new[] { a, b })
            Assert.Equal(-Fee, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == id)).Balance);
    }

    [Fact]
    public async Task AccrueMonth_QULF_ICHIDA_qayta_tekshiradi__parallel_yozilgan_hisob_IKKILANMAYDI()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var plain = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        string groupId;
        using (var seed = new AppDbContext(plain))
        {
            seed.Database.EnsureCreated();
            (_, _, groupId) = await SeedTwo(seed);
        }
        var racer = new ConcurrentChargeOnFirstSave(plain, groupId, M(0));
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).AddInterceptors(racer).Options);
        var log = new CountingLogger();

        racer.Armed = true;
        var (count, _, _) = await TuitionService.AccrueMonth(ctx, M(0), log);

        Assert.Equal(1, count);
        Assert.Equal(0, log.Warnings);                // unikal indeksga umuman urilmadi
        var charges = await ctx.MonthlyCharges.AsNoTracking().Where(c => c.Month == M(0)).ToListAsync();
        Assert.Equal(2, charges.Count);
        Assert.Equal(2, charges.Select(c => c.StudentId).Distinct().Count());
        Assert.All(await ctx.Students.AsNoTracking().ToListAsync(), st => Assert.Equal(-Fee, st.Balance));
    }
}
