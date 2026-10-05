using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// KASSALAR (<see cref="CashboxService"/>, `docs/SPEC-kassalar.md`). Asosiy xavflar: pul kassir
/// bilmagan kassaga jimgina yozilishi, arxivlangan kassaga to'lov tushishi va qoldiqqa bekor
/// qilingan yozuvning kirib qolishi.
/// </summary>
public class CashboxServiceTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static Cashbox Box(string id, string name, string? responsible = null, bool archived = false, string createdAt = "2026-10-01T10:00:00") =>
        new() { Id = id, Name = name, ResponsibleUserId = responsible, IsArchived = archived, CreatedAt = createdAt };

    // ============================ Standart kassa ============================

    [Fact]
    public void Standart_kassa__oz_kassasi_bolsa_OSHA()
    {
        var boxes = new[] { Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir 1", "u1"), Box("k2", "Kassir 2", "u2") };
        Assert.Equal("k2", CashboxService.PickDefault(boxes, "u2")!.Id);
    }

    [Fact]
    public void Standart_kassa__oz_kassasi_yoq_bolsa_ASOSIY_kassa()
    {
        var boxes = new[] { Box("k1", "Kassir 1", "u1", createdAt: "2026-01-01T00:00:00"), Box(CashboxService.MainId, "Asosiy kassa") };
        Assert.Equal(CashboxService.MainId, CashboxService.PickDefault(boxes, "boshqa")!.Id);
        Assert.Equal(CashboxService.MainId, CashboxService.PickDefault(boxes, null)!.Id);
    }

    [Fact]
    public void Standart_kassa__ARXIVLANGAN_kassa_tanlanmaydi()
    {
        var boxes = new[] { Box("k1", "Kassir 1", "u1", archived: true), Box("k2", "Fond") };
        Assert.Equal("k2", CashboxService.PickDefault(boxes, "u1")!.Id); // asosiy yo'q — birinchi faol
        Assert.Null(CashboxService.PickDefault([Box("k1", "Kassir 1", archived: true)], null));
    }

    // ============================ Nom ============================

    [Theory]
    [InlineData("", "Kassa nomini kiriting")]
    [InlineData("   ", "Kassa nomini kiriting")]
    [InlineData("fond", "allaqachon bor")]       // registrsiz takror
    public void Nom_notogri_bolsa_sabab_bilan_rad_etiladi(string name, string expected)
    {
        var err = CashboxService.ValidateName(name, [Box("k1", "Fond")], null, out _);
        Assert.Contains(expected, err);
    }

    [Fact]
    public void Nom__bosh_joylar_tozalanadi__arxivdagi_bilan_ozi_takror_emas()
    {
        var existing = new[] { Box("k1", "Fond", archived: true), Box("k2", "Click") };
        Assert.Null(CashboxService.ValidateName("  Fond  ", existing, null, out var clean));
        Assert.Equal("Fond", clean);
        Assert.Null(CashboxService.ValidateName("click", existing, "k2", out _)); // o'zini tahrirlash
        Assert.NotNull(CashboxService.ValidateName(new string('x', CashboxService.MaxNameLength + 1), existing, null, out _));
    }

    // ============================ Qaysi kassaga tushadi ============================

    [Fact]
    public async Task Tanlangan_kassa_NOMALUM_yoki_ARXIVLANGAN_bolsa_XATO_jimgina_boshqasiga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Eski", archived: true));
        await ctx.SaveChangesAsync();

        Assert.Equal("Kassa topilmadi", (await CashboxService.ResolveAsync(ctx, "yoq", "u1")).Error);
        var archived = await CashboxService.ResolveAsync(ctx, "k1", "u1");
        Assert.Null(archived.CashboxId);
        Assert.Contains("arxivlangan", archived.Error);
        Assert.Equal(CashboxService.MainId, (await CashboxService.ResolveAsync(ctx, CashboxService.MainId, "u1")).CashboxId);
    }

    [Fact]
    public async Task Kassa_umuman_yoq_bazada_yozuv_kassasiz_qoladi_xato_emas()
    {
        using var db = TestDb.Sqlite();
        var r = await CashboxService.ResolveAsync(db.Context, null, "u1");
        Assert.Null(r.CashboxId);
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Vozvrat_ASL_tolov_kassasidan__u_arxivlangan_bolsa_qaytaruvchining_kassasidan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir 1", "u1"),
            Box("k2", "Kassir 2", "u2"), Box("old", "Eski", archived: true));
        await ctx.SaveChangesAsync();

        // To'lov k1 ga tushgan — vozvratni u2 qilsa ham pul k1 dan chiqadi.
        Assert.Equal("k1", await CashboxService.ForRefundAsync(ctx, "k1", "u2"));
        // Asl kassa arxivlangan / yo'q / kassasiz — qaytaruvchining standart kassasi.
        Assert.Equal("k2", await CashboxService.ForRefundAsync(ctx, "old", "u2"));
        Assert.Equal("k2", await CashboxService.ForRefundAsync(ctx, "yoq", "u2"));
        Assert.Equal(CashboxService.MainId, await CashboxService.ForRefundAsync(ctx, null, "boshqa"));
    }

    [Fact]
    public async Task Tahrirda_kassa__bosh_yoki_osha_bolsa_OZGARMAYDI__arxivlanganga_kochirib_bolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir 1"),
            Box("old", "Eski", archived: true));
        await ctx.SaveChangesAsync();

        // Eski forma kassa yubormaydi — yozuv (hatto arxivlangan kassadagi) o'z kassasida qoladi.
        Assert.Equal(new CashboxService.Resolution(null, null), await CashboxService.ChangeAsync(ctx, null, "old"));
        Assert.Equal(new CashboxService.Resolution(null, null), await CashboxService.ChangeAsync(ctx, "  ", "k1"));
        Assert.Equal(new CashboxService.Resolution(null, null), await CashboxService.ChangeAsync(ctx, "old", "old"));

        Assert.Equal("k1", (await CashboxService.ChangeAsync(ctx, "k1", CashboxService.MainId)).CashboxId);
        Assert.Contains("arxivlangan", (await CashboxService.ChangeAsync(ctx, "old", "k1")).Error);
        Assert.Equal("Kassa topilmadi", (await CashboxService.ChangeAsync(ctx, "yoq", "k1")).Error);
    }

    // ============================ Qoldiq ============================

    [Fact]
    public async Task Qoldiq_kirim_minus_chiqim__BEKOR_qilingan_kirmaydi__davr_alohida()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir"));
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Date = "2026-09-10", Direction = "income", Category = "tuition", Amount = 500_000m, CashboxId = "k1" },
            new FinanceTransaction { Date = "2026-10-02", Direction = "income", Category = "tuition", Amount = 300_000m, CashboxId = "k1" },
            new FinanceTransaction { Date = "2026-10-03", Direction = "expense", Category = "refund", Amount = 100_000m, CashboxId = "k1" },
            new FinanceTransaction { Date = "2026-10-04", Direction = "income", Category = "tuition", Amount = 999_000m, CashboxId = "k1", IsVoided = true },
            new FinanceTransaction { Date = "2026-10-04", Direction = "income", Category = "other", Amount = 50_000m, CashboxId = CashboxService.MainId });
        await ctx.SaveChangesAsync();

        var b = await CashboxService.BalancesAsync(ctx, "2026-10-01", "2026-10-31");

        Assert.Equal(700_000m, b["k1"].Total);
        Assert.Equal(300_000m, b["k1"].PeriodIncome);
        Assert.Equal(100_000m, b["k1"].PeriodExpense);
        Assert.Equal(50_000m, b[CashboxService.MainId].Total);
    }

    [Fact]
    public async Task Asosiy_kassa_bir_marta_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Assert.True(CashboxService.EnsureMain(ctx));
        await ctx.SaveChangesAsync();
        Assert.False(CashboxService.EnsureMain(ctx));
        Assert.Equal(CashboxService.MainName, (await ctx.Cashboxes.SingleAsync()).Name);
    }

    // ============================ To'lov qabul qilish ============================

    private static (Student S, Group G) SeedStudent(AppDbContext ctx)
    {
        var month = AppClock.Today.ToString("yyyy-MM");
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{month}-01" };
        var g = new Group { Name = "A guruh", MonthlyFee = 600_000m, Days = [0, 2, 4] };
        ctx.Students.Add(s);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{month}-01", ActivatedAt = $"{month}-01", RecordedAt = $"{month}-01",
        });
        ctx.Cashboxes.AddRange(Box(CashboxService.MainId, "Asosiy kassa"), Box("k1", "Kassir kassasi", "kassir-1"),
            Box("old", "Eski", archived: true));
        ctx.SaveChanges();
        return (s, g);
    }

    private static Task<PaymentIntakeResult> Pay(AppDbContext ctx, Student s, string groupId, string? userId, string? cashboxId = null) =>
        PaymentIntake.AddAsync(ctx, new AuditService(ctx, new NoHttpContext()), new MessagingStack().Auto, s,
            new PaymentRequest(200_000m, AppClock.Today.ToString("yyyy-MM"), groupId, Method: "cash",
                RequestId: Guid.NewGuid().ToString(), CashboxId: cashboxId),
            createdBy: "Kassir", createdById: userId);

    [Fact]
    public async Task Tolov_kassirning_OZ_kassasiga_mas_uli_yoq_bolsa_ASOSIY_kassaga_tushadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = SeedStudent(ctx);

        var own = await Pay(ctx, s, g.Id, "kassir-1");
        Assert.Equal("k1", (await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Id == own.TxId)).CashboxId);

        await ctx.FinanceTransactions.ExecuteUpdateAsync(x => x.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-1)));
        var other = await Pay(ctx, s, g.Id, "admin-1");
        Assert.Equal(CashboxService.MainId, (await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Id == other.TxId)).CashboxId);
    }

    [Fact]
    public async Task Tolov_TANLANGAN_kassaga_tushadi__arxivlangan_kassa_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = SeedStudent(ctx);

        var chosen = await Pay(ctx, s, g.Id, "kassir-1", CashboxService.MainId);
        Assert.Equal(CashboxService.MainId, (await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Id == chosen.TxId)).CashboxId);

        var balanceBefore = (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance;
        var rejected = await Pay(ctx, s, g.Id, "kassir-1", "old");
        Assert.Contains("arxivlangan", rejected.Error);
        Assert.Equal(1, await ctx.FinanceTransactions.CountAsync());
        Assert.Equal(balanceBefore, (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance);
    }
}
