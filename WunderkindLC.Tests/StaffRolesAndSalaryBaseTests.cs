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

    [Fact]
    public async Task Hisoblangan_bazada_HOZIR_CHIQADIGAN_oylik_tushgan_puldan__farq_qarzdorlar_tolasa()
    {
        // Foydalanuvchi qarori (2026-10-07): rasmiy oylik hisoblangandan, o'qituvchiga esa
        // "hozir tushgan puldan" ham ko'rsatiladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Cutover, 600_000m);
        Pay(ctx, s, g, Cutover, 200_000m);       // 600 000 dan faqat 200 000 tushgan
        await ctx.SaveChangesAsync();

        var m = (await SalaryLedger.BuildAsync(ctx, t, Cutover, Cutover)).Months.Single(x => x.Month == Cutover);

        Assert.True(m.ChargedBase);
        Assert.Equal(300_000m, m.Expected);
        Assert.Equal(100_000m, m.CollectedExpected);
    }

    [Fact]
    public async Task HOZIR_CHIQADIGAN__markaz_qoplagan_chegirma_qarzdorlar_tolasa_qismiga_KIRMAYDI()
    {
        // TZ 1.6 (2026-10-17): chegirmani markaz qoplaydi — u hech qachon "qarzdordan" kelmaydi. Ilgari 200 000 lik
        // chegirmaning ulushi ham «Qarzdorlar to'lasa yana +X» bo'lib ko'rinardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Cutover, 600_000m, discount: 200_000m);   // sof 400 000
        Pay(ctx, s, g, Cutover, 200_000m);                          // qarz 200 000
        await ctx.SaveChangesAsync();

        var m = (await SalaryLedger.BuildAsync(ctx, t, Cutover, Cutover)).Months.Single(x => x.Month == Cutover);

        Assert.Equal(300_000m, m.Expected);            // to'liq summadan — chegirma maoshni kamaytirmaydi
        Assert.Equal(200_000m, m.CollectedExpected);   // (200 000 tushgan + 200 000 chegirma) × 50%
        Assert.Equal(100_000m, m.Expected - m.CollectedExpected);   // = qarz 200 000 × 50%
    }

    [Fact]
    public async Task Yigilgan_bazali_oyda_HOZIR_CHIQADIGAN_rasmiy_oylikning_ozi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Before, 600_000m);
        Pay(ctx, s, g, Before, 200_000m);
        await ctx.SaveChangesAsync();

        var m = (await SalaryLedger.BuildAsync(ctx, t, Before, Before)).Months.Single(x => x.Month == Before);

        Assert.False(m.ChargedBase);
        Assert.Equal(m.Expected, m.CollectedExpected);
    }

    [Fact]
    public async Task Oqituvchi_kabineti__qarzdorlar_soni_va_JAMI_qarzi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        var paid = new Student { FullName = "To'lagan O'quvchi", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(paid);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = paid.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        Charge(ctx, s, g, After, 600_000m);
        Charge(ctx, paid, g, After, 600_000m);
        Pay(ctx, paid, g, After, 600_000m);
        await ctx.SaveChangesAsync();

        var debt = await TeacherHomeService.DebtSummaryAsync(ctx, new[] { g.Id });

        Assert.Equal(1, debt.DebtorCount);
        Assert.Equal(600_000m, debt.DebtTotal);
    }

    [Fact]
    public async Task HOZIR_CHIQADIGAN__otgan_oyga_teglangan_ortiqcha_pul_joriy_oyni_YOPADI__balans_bo_yicha()
    {
        // 2026-10-26 shikoyati: oktabr puli sentabrga teglansa (balans 0) u «Qarzdorlar to'lasa yana +X» ga tushardi,
        // qarzdorlar sanog'ida esa o'quvchi yo'q edi. Endi to'lanmagan qism BALANS bo'yicha (FIFO).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Cutover, 600_000m);
        Charge(ctx, s, g, After, 600_000m);
        Pay(ctx, s, g, Cutover, 1_200_000m);   // ikki oy puli — hammasi sentabrga teglangan
        await ctx.SaveChangesAsync();

        var m = (await SalaryLedger.BuildAsync(ctx, t, Cutover, After)).Months.Single(x => x.Month == After);

        Assert.Equal(300_000m, m.Expected);
        Assert.Equal(m.Expected, m.CollectedExpected);   // "qarzdorlar to'lasa yana" — 0
        Assert.Equal(0, m.UnpaidStudents);
    }

    [Fact]
    public async Task HOZIR_CHIQADIGAN__qarz_ENG_YANGI_oyda_qoladi_va_qarzdor_sanaladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        Charge(ctx, s, g, Cutover, 600_000m);
        Charge(ctx, s, g, After, 600_000m);
        Pay(ctx, s, g, After, 600_000m);       // oktabrga teglangan, lekin sentabr ham to'lanmagan edi
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, Cutover, After);
        var sep = dto.Months.Single(x => x.Month == Cutover);
        var oct = dto.Months.Single(x => x.Month == After);

        Assert.Equal(sep.Expected, sep.CollectedExpected);      // pul eng eski oyni yopadi
        Assert.Equal(0m, oct.CollectedExpected);                // qarz oktabrda
        Assert.Equal(1, oct.UnpaidStudents);
        Assert.Equal(0, oct.UnpaidStudentsLeft);
    }

    [Fact]
    public async Task HOZIR_CHIQADIGAN__guruhdan_CHIQQAN_oquvchining_tolanmagan_oyi_ham_sanaladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx, Cutover);
        var left = new Student { FullName = "Chiqqan O'quvchi", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(left);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = left.Id, GroupId = g.Id, Status = "active", IsActive = false, LeftAt = $"{After}-10",
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        Charge(ctx, s, g, After, 600_000m);
        Pay(ctx, s, g, After, 600_000m);
        Charge(ctx, left, g, After, 200_000m);   // qisman oy, to'lanmagan
        await ctx.SaveChangesAsync();

        var m = (await SalaryLedger.BuildAsync(ctx, t, After, After)).Months.Single(x => x.Month == After);

        Assert.Equal(400_000m, m.Expected);                     // (600 000 + 200 000) × 50%
        Assert.Equal(100_000m, m.Expected - m.CollectedExpected);   // = 200 000 × 50%
        Assert.Equal(1, m.UnpaidStudents);
        Assert.Equal(1, m.UnpaidStudentsLeft);
    }

    [Fact]
    public void FifoUnpaid__qarz_eng_yangi_oylarga__oy_hisobidan_oshmaydi()
    {
        var owed = new[] { ("2026-09", 600_000m), ("2026-10", 600_000m), ("2026-08", 600_000m) };

        var r = GroupBalanceService.FifoUnpaid(-900_000m, owed);

        Assert.Equal(600_000m, r["2026-10"]);
        Assert.Equal(300_000m, r["2026-09"]);
        Assert.False(r.ContainsKey("2026-08"));
        Assert.Empty(GroupBalanceService.FifoUnpaid(0m, owed));
        Assert.Empty(GroupBalanceService.FifoUnpaid(50_000m, owed));
    }
}
