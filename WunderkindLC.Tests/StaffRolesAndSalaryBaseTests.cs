using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// (1) FOIZLI MAOSH BAZASI — <see cref="CenterMeta.SalaryChargedBaseFrom"/> dan boshlab HISOBLANGAN
/// to'liq oylik (chegirmasiz), undan oldin — yig'ilgan pul. (2) XODIM ROLLARI — jonli bog'lanish.
/// Sanalar MUTLAQ (2026-yil) — qoida kuchga kirish oyiga bog'liq, joriy sanaga emas.
/// </summary>
public class StaffRolesAndSalaryBaseTests
{
    private const string Before = "2026-08";
    private const string Cutover = "2026-09";
    private const string After = "2026-10";

    /// <summary>50% foizli o'qituvchi + guruh (600 000) + o'quvchi + markaz sozlamasi.</summary>
    private static (Teacher T, Group G, Student S) Setup(AppDbContext ctx, string chargedFrom, string groupMode = "percent")
    {
        ctx.CenterMeta.Add(new CenterMeta { SalaryChargedBaseFrom = chargedFrom });
        var t = new Teacher { FullName = "Foizli O'qituvchi", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "A guruh", MonthlyFee = 600_000m, TeacherId = t.Id,
            TeacherSalaryMode = groupMode, TeacherSalaryPercent = groupMode == "percent" ? 50 : 0,
            Days = new List<int> { 0, 2, 4 },
        };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Ali Valiyev", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        return (t, g, s);
    }

    private static void Charge(AppDbContext ctx, Student s, Group g, string month, decimal amount, decimal discount = 0m) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = month, Amount = amount, Discount = discount, Date = $"{month}-01",
        });

    private static void Pay(AppDbContext ctx, Student s, Group g, string month, decimal amount) =>
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{month}-05", Direction = "income", Category = "tuition", Amount = amount,
            StudentId = s.Id, GroupId = g.Id, Month = month, Method = "cash",
        });

    [Fact]
    public async Task Kuchga_kirgandan_keyin_TOLANMAGAN_oy_uchun_ham_foiz_hisoblangan_oylikdan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Cutover, 600_000m);        // pul KELMAGAN
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, Cutover, Cutover);

        Assert.Equal(300_000m, dto.Months.Single(m => m.Month == Cutover).BaseExpected);
    }

    [Fact]
    public async Task Chegirma_ayrilmaydi_foiz_TOLIQ_oylikdan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, After, 600_000m, discount: 200_000m);
        Pay(ctx, s, g, After, 400_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, After, After);

        // 600 000 × 50% — chegirmani markaz ko'taradi.
        Assert.Equal(300_000m, dto.Months.Single(m => m.Month == After).BaseExpected);
    }

    [Fact]
    public async Task Kuchga_kirishdan_OLDINGI_oy_ozgarmaydi_yigilgan_puldan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Before, 600_000m);
        Pay(ctx, s, g, Before, 200_000m);             // faqat 200 000 kelgan
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, Before, Before);

        Assert.Equal(100_000m, dto.Months.Single(m => m.Month == Before).BaseExpected);
    }

    [Fact]
    public async Task Sozlama_BOSH_bolsa_eski_qoida_yigilgan_pul()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, chargedFrom: "");
        Charge(ctx, s, g, After, 600_000m);
        Pay(ctx, s, g, After, 200_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, After, After);

        Assert.Equal(100_000m, dto.Months.Single(m => m.Month == After).BaseExpected);
    }

    [Fact]
    public async Task Guruh_UMUMIY_rejimda_oqituvchi_foiziga_ergashadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover, groupMode: "");
        t.SalaryPercent = 40;
        Charge(ctx, s, g, Cutover, 500_000m);
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, Cutover, Cutover);

        Assert.Equal(200_000m, dto.Months.Single(m => m.Month == Cutover).BaseExpected);
    }

    [Theory]
    [InlineData("2026-08", "2026-09", false)]
    [InlineData("2026-09", "2026-09", true)]
    [InlineData("2026-12", "2026-09", true)]
    [InlineData("2026-12", "", false)]
    [InlineData("2026-12", null, false)]
    public void UsesChargedBase_chegarasi(string month, string? from, bool expected) =>
        Assert.Equal(expected, SalaryLedger.UsesChargedBase(month, from));

    // ==================== ROLLAR ====================

    [Fact]
    public async Task Rol_ruxsatlari_ozgarsa_FAQAT_shu_roldagi_xodimlarga_tarqaladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var kassir = new StaffRoleTemplate { Code = "cashier", Name = "Kassir", DefaultPermissions = ["kassa"] };
        var boshqa = new StaffRoleTemplate { Code = "other", Name = "Boshqa", DefaultPermissions = ["leads"] };
        ctx.StaffRoleTemplates.AddRange(kassir, boshqa);
        var a = new AppUser { FullName = "A", Role = Roles.Staff, Email = "a" };
        var b = new AppUser { FullName = "B", Role = Roles.Staff, Email = "b" };
        var individual = new AppUser { FullName = "C", Role = Roles.Staff, Email = "c", Permissions = ["kassa", "audit"] };
        StaffRoles.Assign(a, kassir);
        StaffRoles.Assign(b, boshqa);
        ctx.Users.AddRange(a, b, individual);
        await ctx.SaveChangesAsync();

        kassir.DefaultPermissions = StaffRoles.Normalize(["finance", "kassa"]);
        var synced = await StaffRoles.SyncMembersAsync(ctx, kassir);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, synced);
        Assert.Equal(new[] { "finance", "kassa" }, a.Permissions);
        Assert.Equal(new[] { "leads" }, b.Permissions);                // boshqa rol tegilmadi
        Assert.Equal(new[] { "kassa", "audit" }, individual.Permissions); // individual tegilmadi
    }

    [Fact]
    public async Task Superadmin_qilingan_akkauntga_rol_ruxsati_YOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var role = new StaffRoleTemplate { Code = "x", Name = "X", DefaultPermissions = ["leads"] };
        ctx.StaffRoleTemplates.Add(role);
        var boss = new AppUser { FullName = "Boss", Role = Roles.SuperAdmin, Email = "boss", RoleTemplateId = role.Id };
        ctx.Users.Add(boss);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await StaffRoles.SyncMembersAsync(ctx, role));
        Assert.Empty(boss.Permissions);
    }

    [Fact]
    public void Normalize_bosh_va_dublikatlarni_tashlaydi()
    {
        Assert.Equal(new[] { "finance", "leads" },
            StaffRoles.Normalize(new[] { " leads ", "", null, "finance", "leads" }));
        Assert.Empty(StaffRoles.Normalize(null));
    }

    [Theory]
    [InlineData("Kassir", "kassir")]
    [InlineData("Katta kassir 2", "katta_kassir_2")]
    [InlineData("Qo'ng'iroq operatori", "qo_ng_iroq_operatori")]
    public void CodeFrom_barqaror_kod(string name, string expected) =>
        Assert.Equal(expected, StaffRoles.CodeFrom(name));

    [Fact]
    public void CodeFrom_lotin_harfi_yoq_nomda_tasodifiy_kod() =>
        Assert.StartsWith("role_", StaffRoles.CodeFrom("Касса"));
}
