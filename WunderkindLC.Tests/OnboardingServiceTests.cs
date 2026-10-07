using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// MOSLASHUV (onboarding — 30 kunlik adaptatsiya): katalog seed'i, ariza "Ishga qabul qilindi"
/// bo'lganda AVTOMATIK boshlash (idempotentlik), katalogdan NUSXA olish va sof qoidalar
/// (muddat, kechikish, foiz, holat). Qoidalar: <c>.claude/rules/onboarding.md</c>.
/// </summary>
public class OnboardingServiceTests
{
    private static readonly DateOnly Bugun = new(2026, 10, 6);

    private static JobApplication Ariza(string id = "app-1", int number = 12) => new()
    {
        Id = id,
        Number = number,
        VacancyId = "vac-1",
        VacancyTitle = "Ingliz tili o'qituvchisi",
        FullName = "Valiyev Ali",
        Phone = "+998901234567",
        Status = CareerService.StatusHired,
    };

    /* =========================================================================================
     *  STANDART KATALOG
     * ========================================================================================= */

    [Fact]
    public void DefaultSteps_11ta_Muddati30KunIchida()
    {
        Assert.Equal(11, OnboardingService.DefaultSteps.Count);
        Assert.All(OnboardingService.DefaultSteps, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.True(OnboardingService.IsValidDayOffset(s.DayOffset), $"{s.Title}: {s.DayOffset}");
        });
        Assert.Equal(11, OnboardingService.DefaultSteps.Select(s => s.Title).Distinct().Count());
    }

    [Theory]
    [InlineData("Hujjatlarni topshirish")]
    [InlineData("Mehnat shartnomasini imzolash")]
    [InlineData("Ichki tartib-qoidalar bilan tanishish")]
    [InlineData("Telegram kanal va guruhlarga ulanish")]
    [InlineData("30-kunlik yakuniy suhbat")]
    public void DefaultSteps_YigilishdagiQadamlarBor(string title)
    {
        Assert.Contains(OnboardingService.DefaultSteps, s => s.Title == title);
    }

    [Fact]
    public void DefaultSteps_YakuniySuhbat_30KunOxirida()
    {
        Assert.Equal(OnboardingService.PeriodDays, OnboardingService.DefaultSteps.Max(s => s.DayOffset));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(-1, false)]
    [InlineData(31, false)]
    public void IsValidDayOffset_0dan30gacha(int d, bool kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.IsValidDayOffset(d));

    [Fact]
    public async Task EnsureCatalogAsync_BoshBazada11ta_IkkinchiMartaHechNarsa()
    {
        using var db = TestDb.Sqlite();

        Assert.Equal(11, await OnboardingService.EnsureCatalogAsync(db.Context));
        Assert.Equal(0, await OnboardingService.EnsureCatalogAsync(db.Context));
        Assert.Equal(11, await db.Context.OnboardingSteps.CountAsync());
    }

    [Fact]
    public async Task EnsureCatalogAsync_AdminOchirganQadam_QaytaTugilmaydi()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);

        var bir = await db.Context.OnboardingSteps.FirstAsync();
        db.Context.OnboardingSteps.Remove(bir);
        await db.Context.SaveChangesAsync();

        Assert.Equal(0, await OnboardingService.EnsureCatalogAsync(db.Context));
        Assert.Equal(10, await db.Context.OnboardingSteps.CountAsync());
    }

    /* =========================================================================================
     *  AVTOMATIK BOSHLASH — idempotent
     * ========================================================================================= */

    [Fact]
    public async Task StartForApplication_Yaratadi_ArizaMaydonlariVaBugungiSana()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);

        var (o, created) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        await db.Context.SaveChangesAsync();

        Assert.True(created);
        Assert.Equal("Valiyev Ali", o.FullName);
        Assert.Equal("+998901234567", o.Phone);
        Assert.Equal("Ingliz tili o'qituvchisi", o.Position);
        Assert.Equal("app-1", o.JobApplicationId);
        Assert.Equal(12, o.ApplicationNumber);
        Assert.Equal("2026-10-06", o.StartDate);
        Assert.Equal(OnboardingService.StatusActive, o.Status);
        Assert.Equal(OnboardingService.SourceHired, o.Source);
        Assert.Equal(11, await db.Context.EmployeeOnboardingTasks.CountAsync(t => t.OnboardingId == o.Id));
    }

    [Fact]
    public async Task StartForApplication_IkkiMarta_BittaMoslashuv()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);

        var (first, c1) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        await db.Context.SaveChangesAsync();
        var (second, c2) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun.AddDays(5));
        await db.Context.SaveChangesAsync();

        Assert.True(c1);
        Assert.False(c2);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("2026-10-06", second.StartDate); // qayta "hired" boshlanish sanasini SURMAYDI
        Assert.Equal(1, await db.Context.EmployeeOnboardings.CountAsync());
        Assert.Equal(11, await db.Context.EmployeeOnboardingTasks.CountAsync());
    }

    [Fact]
    public async Task StartForApplication_SaqlanmaganHolatda_IkkinchiChaqiruvHamTopadi()
    {
        // Bitta so'rov ichida (SaveChanges'dan oldin) ikki marta chaqirilsa ham dublikat yo'q.
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);

        await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        var (_, c2) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        await db.Context.SaveChangesAsync();

        Assert.False(c2);
        Assert.Equal(1, await db.Context.EmployeeOnboardings.CountAsync());
    }

    [Fact]
    public async Task StartForApplication_YangiKontekstda_HamIdempotent()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);
        await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        await db.Context.SaveChangesAsync();

        using var ctx2 = db.NewContext();
        var (_, created) = await OnboardingService.StartForApplicationAsync(ctx2, Ariza(), "HR", Bugun);
        await ctx2.SaveChangesAsync();

        Assert.False(created);
        Assert.Equal(1, await ctx2.EmployeeOnboardings.CountAsync());
    }

    [Fact]
    public async Task StartForApplication_ToxtatilganBolsaHam_YangisiOchilmaydi()
    {
        using var db = TestDb.Sqlite();
        var (o, _) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        o.Status = OnboardingService.StatusCancelled;
        await db.Context.SaveChangesAsync();

        var (again, created) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);

        Assert.False(created);
        Assert.Equal(OnboardingService.StatusCancelled, again.Status);
    }

    [Fact]
    public async Task StartForApplication_BoshqaAriza_AlohidaMoslashuv()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.StartForApplicationAsync(db.Context, Ariza("app-1"), "HR", Bugun);
        await OnboardingService.StartForApplicationAsync(db.Context, Ariza("app-2", 13), "HR", Bugun);
        await db.Context.SaveChangesAsync();

        Assert.Equal(2, await db.Context.EmployeeOnboardings.CountAsync());
    }

    /* =========================================================================================
     *  KATALOGDAN NUSXA
     * ========================================================================================= */

    [Fact]
    public async Task AddWithTasks_KatalogBosh_StandartQadamlarBirTranzaksiyada()
    {
        // Katalog umuman bo'sh — moslashuv cheklistsiz qolmasin: standart qadamlar ham yoziladi,
        // lekin SaveChanges CHAQIRUVCHIDA (audit bilan bitta tranzaksiya).
        using var db = TestDb.Sqlite();
        var o = new EmployeeOnboarding { FullName = "Test", StartDate = "2026-10-01" };

        var tasks = await OnboardingService.AddWithTasksAsync(db.Context, o);

        Assert.Equal(11, tasks.Count);
        Assert.Equal(0, await db.Context.OnboardingSteps.CountAsync()); // hali saqlanmagan
        await db.Context.SaveChangesAsync();
        Assert.Equal(11, await db.Context.OnboardingSteps.CountAsync());
        Assert.Equal(11, await db.Context.EmployeeOnboardingTasks.CountAsync());
        Assert.Equal(Enumerable.Range(0, 11), tasks.Select(t => t.Order));
    }

    [Fact]
    public async Task AddWithTasks_OchirilganKatalogQadami_NusxaQilinmaydi()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);
        var step = await db.Context.OnboardingSteps.OrderBy(s => s.Order).FirstAsync();
        step.IsActive = false;
        await db.Context.SaveChangesAsync();

        var tasks = await OnboardingService.AddWithTasksAsync(
            db.Context, new EmployeeOnboarding { FullName = "Test", StartDate = "2026-10-01" });

        Assert.Equal(10, tasks.Count);
        Assert.DoesNotContain(tasks, t => t.StepId == step.Id);
    }

    [Fact]
    public async Task Nusxa_KatalogKeyinTahrirlansa_EskiMoslashuvOzgarmaydi()
    {
        using var db = TestDb.Sqlite();
        await OnboardingService.EnsureCatalogAsync(db.Context);
        var (o, _) = await OnboardingService.StartForApplicationAsync(db.Context, Ariza(), "HR", Bugun);
        await db.Context.SaveChangesAsync();

        var step = await db.Context.OnboardingSteps.OrderBy(s => s.Order).FirstAsync();
        var asl = step.Title;
        step.Title = "Yangi nom";
        step.DayOffset = 25;
        await db.Context.SaveChangesAsync();

        var task = await db.Context.EmployeeOnboardingTasks.SingleAsync(t => t.OnboardingId == o.Id && t.StepId == step.Id);
        Assert.Equal(asl, task.Title);
        Assert.NotEqual(25, task.DayOffset);
    }

    /* =========================================================================================
     *  SOF QOIDALAR
     * ========================================================================================= */

    [Theory]
    [InlineData("2026-10-01", 0, "2026-10-01")]
    [InlineData("2026-10-01", 30, "2026-10-31")]
    [InlineData("2026-12-20", 14, "2027-01-03")]
    [InlineData("", 3, "")]
    [InlineData("2026-13-40", 3, "")]
    [InlineData("01.10.2026", 3, "")]
    public void DueDate_BoshlanishPlusKun(string start, int offset, string kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.DueDate(start, offset));

    [Theory]
    [InlineData("2026-10-01", 4, false, true)]   // muddat 10-05, bugun 10-06 → kechikkan
    [InlineData("2026-10-01", 5, false, false)]  // muddat BUGUN → hali kechikmagan
    [InlineData("2026-10-01", 10, false, false)] // kelajak
    [InlineData("2026-10-01", 1, true, false)]   // bajarilgan — kechikmaydi
    [InlineData("", 1, false, false)]            // buzuq sana — yolg'on qizil bo'lmasin
    [InlineData("2026-09-01", 30, false, true)]  // 30-kunlik suhbat o'tib ketgan
    public void IsOverdue_MuddatOtganVaBajarilmagan(string start, int offset, bool done, bool kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.IsOverdue(start, offset, done, Bugun));

    [Theory]
    [InlineData(0, 11, 0)]
    [InlineData(1, 11, 9)]
    [InlineData(10, 11, 90)]  // 90.9 → 90: 100% faqat hammasi bajarilganda
    [InlineData(11, 11, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(5, 4, 100)]   // buzuq qiymat chegaralanadi
    public void ProgressPct_PastgaYaxlitlanadi(int done, int total, int kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.ProgressPct(done, total));

    [Theory]
    [InlineData("2026-10-06", 1)]
    [InlineData("2026-10-01", 6)]
    [InlineData("2026-09-06", 31)]
    [InlineData("2026-10-10", 0)] // hali boshlanmagan
    [InlineData("", 0)]
    public void DayNumber_BoshlanishKuni1(string start, int kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.DayNumber(start, Bugun));

    [Theory]
    [InlineData("active", 11, 11, "completed")]
    [InlineData("active", 10, 11, "active")]
    [InlineData("completed", 10, 11, "active")]   // belgi olindi — yana faol
    [InlineData("cancelled", 11, 11, "cancelled")] // to'xtatilgan holat o'zgarmaydi
    [InlineData("active", 0, 0, "active")]         // qadamsiz moslashuv o'z-o'zidan yakunlanmaydi
    public void StatusAfterTasks_AvtomatikYakunlash(string status, int done, int total, string kutilgan) =>
        Assert.Equal(kutilgan, OnboardingService.StatusAfterTasks(status, done, total));

    private static EmployeeOnboardingTask Qd(int offset, bool done, string title = "Q") => new()
    {
        Title = title, DayOffset = offset, Order = offset, DoneAt = done ? "2026-10-02T10:00:00" : "",
    };

    [Fact]
    public void ToDto_KechikkanlarSoni_VaNavbatdagiQadam()
    {
        var o = new EmployeeOnboarding { Id = "o1", StartDate = "2026-10-01", Status = OnboardingService.StatusActive };
        var tasks = new[] { Qd(1, true, "A"), Qd(2, false, "B"), Qd(3, false, "C"), Qd(20, false, "D") };

        var dto = OnboardingService.ToDto(o, tasks, Bugun, withTasks: true);

        Assert.Equal(4, dto.Total);
        Assert.Equal(1, dto.DoneCount);
        Assert.Equal(25, dto.ProgressPct);
        Assert.Equal(2, dto.OverdueCount); // B (10-03) va C (10-04)
        Assert.Equal("B", dto.NextTitle);
        Assert.Equal("2026-10-03", dto.NextDueDate);
        Assert.Equal(6, dto.DayNumber);
        Assert.NotNull(dto.Tasks);
        Assert.True(dto.Tasks!.Single(t => t.Title == "B").Overdue);
        Assert.False(dto.Tasks!.Single(t => t.Title == "D").Overdue);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("completed")]
    public void ToDto_FaolBolmaganMoslashuvda_KechikishYoq(string status)
    {
        var o = new EmployeeOnboarding { StartDate = "2026-09-01", Status = status };

        var dto = OnboardingService.ToDto(o, [Qd(1, false)], Bugun, withTasks: false);

        Assert.Equal(0, dto.OverdueCount);
        Assert.Null(dto.Tasks);
    }

    [Fact]
    public void Summarize_HolatlarVaKechikkanlar()
    {
        var faolKech = OnboardingService.ToDto(
            new EmployeeOnboarding { StartDate = "2026-09-01", Status = "active" },
            [Qd(1, false), Qd(2, false)], Bugun, false);
        var faol = OnboardingService.ToDto(
            new EmployeeOnboarding { StartDate = "2026-10-06", Status = "active" }, [Qd(1, false)], Bugun, false);
        var yakun = OnboardingService.ToDto(
            new EmployeeOnboarding { StartDate = "2026-09-01", Status = "completed" }, [Qd(1, true)], Bugun, false);
        var toxt = OnboardingService.ToDto(
            new EmployeeOnboarding { StartDate = "2026-09-01", Status = "cancelled" }, [Qd(1, false)], Bugun, false);

        var s = OnboardingService.Summarize([faolKech, faol, yakun, toxt]);

        Assert.Equal(2, s.Active);
        Assert.Equal(1, s.Completed);
        Assert.Equal(1, s.Cancelled);
        Assert.Equal(1, s.WithOverdue);
        Assert.Equal(2, s.OverdueTasks);
    }
}
