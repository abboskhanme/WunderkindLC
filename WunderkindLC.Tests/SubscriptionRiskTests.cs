using Regex = System.Text.RegularExpressions.Regex;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// "JORIY OYDA OBUNASI TUGAYDIGANLAR" (<see cref="SubscriptionRisk"/>). Asosiy xavflar:
/// (1) oy boshida ALLAQACHON yozilgan oylikni yana ayirib, o'quvchini ikki marta qarzdor qilish;
/// (2) hali yozilmagan oylikni chegirmasiz hisoblash (yolg'on "tugaydi");
/// (3) sinovdagi / shu oy o'qimayotgan qarzdorni "obunasi tugayotgan" deb ko'rsatish.
/// </summary>
public class SubscriptionRiskTests
{
    private const string Month = "2026-09";

    private static SubscriptionRisk.StudentRow S(string id, decimal balance, string state = "active") =>
        new(id, "O'quvchi " + id, "+998-90-000-00-0" + id.Length, "", balance, state);

    // ============================ sof hisob (Compute) ============================

    [Fact]
    public void Yozilgan_oylik_QAYTA_ayrilmaydi__kutilayotgan_joriy_balansga_teng()
    {
        // Oylik oy boshida yozilgan: balans allaqachon -100 000 (500 000 hisob, 400 000 to'lov).
        var rows = new[] { S("a", -100_000m) };
        var charges = new[] { new SubscriptionRisk.MonthCharge("a", 500_000m, Written: true) };

        var r = SubscriptionRisk.Compute(rows, charges, Month);

        var item = Assert.Single(r.Items);
        Assert.Equal(500_000m, item.LessonsTotal);
        Assert.Equal(-100_000m, item.Balance);
        Assert.Equal(-100_000m, item.Expected); // -600 000 EMAS
    }

    [Fact]
    public void Yozilmagan_oylik_kutilayotgan_balansdan_ayriladi()
    {
        // Accrual hali ishlamagan: avans 300 000, oylik 500 000 → oy oxirida -200 000.
        var rows = new[] { S("a", 300_000m) };
        var charges = new[] { new SubscriptionRisk.MonthCharge("a", 500_000m, Written: false) };

        var item = Assert.Single(SubscriptionRisk.Compute(rows, charges, Month).Items);
        Assert.Equal(300_000m, item.Balance);
        Assert.Equal(-200_000m, item.Expected);
    }

    [Fact]
    public void Puli_yetadigan_va_shu_oy_darsi_yoq_oquvchilar_KIRMAYDI()
    {
        var rows = new[]
        {
            S("covered", 0m),                 // to'liq to'lagan
            S("advance", 200_000m),           // avans bor, yozilmagan qism 150 000
            S("oldDebt", -900_000m, "frozen"),// eski qarz, lekin shu oy darsi yo'q (muzlatilgan)
            S("trial", 0m, "trial"),          // sinov — hisob yo'q
        };
        var charges = new[]
        {
            new SubscriptionRisk.MonthCharge("covered", 500_000m, true),
            new SubscriptionRisk.MonthCharge("advance", 150_000m, false),
        };

        Assert.Empty(SubscriptionRisk.Compute(rows, charges, Month).Items);
    }

    [Fact]
    public void Jamlar_faqat_royxatdagilar_boyicha_va_kamomad_kattasi_tepada()
    {
        var rows = new[] { S("a", -50_000m), S("b", -400_000m), S("ok", 0m) };
        var charges = new[]
        {
            new SubscriptionRisk.MonthCharge("a", 300_000m, true),
            new SubscriptionRisk.MonthCharge("b", 200_000m, true),
            new SubscriptionRisk.MonthCharge("b", 100_000m, true), // ikkinchi guruh
            new SubscriptionRisk.MonthCharge("ok", 500_000m, true),
        };

        var r = SubscriptionRisk.Compute(rows, charges, Month);

        Assert.Equal(new[] { "b", "a" }, r.Items.Select(i => i.StudentId));
        Assert.Equal(600_000m, r.TotalLessons);   // 300 000 + 300 000 ("ok" kirmaydi)
        Assert.Equal(-450_000m, r.TotalBalance);
        Assert.Equal(-450_000m, r.TotalExpected);
        Assert.Equal(Month, r.Month);
    }

    [Fact]
    public void Nol_effektiv_hisob_100_foiz_chegirma_dars_narxi_hisoblanmaydi()
    {
        var rows = new[] { S("a", -10m) };
        var charges = new[] { new SubscriptionRisk.MonthCharge("a", 0m, true) };
        Assert.Empty(SubscriptionRisk.Compute(rows, charges, Month).Items);
    }

    // ============================ yozilmagan oylik (Pending) ============================

    private static StudentGroup M(string student, string group, string status, string activated,
        string frozen = "", bool isActive = true) => new()
        {
            StudentId = student, GroupId = group, Status = status,
            ActivatedAt = activated, FrozenAt = frozen, IsActive = isActive,
        };

    [Fact]
    public void Pending_AccrueMonth_shartini_takrorlaydi()
    {
        var memberships = new[]
        {
            M("s1", "g", "active", "2026-05-10"),            // ✓ to'liq oy yozilishi kerak
            M("s2", "g", "active", "2026-09-03"),            // ✗ aktivlashtirish OYI — qisman hisob amal paytida
            M("s3", "g", "trial", ""),                       // ✗ sinov
            M("s4", "g", "active", "2026-05-10", isActive: false), // ✗ guruhdan chiqqan
            M("s5", "g", "active", "2026-05-10"),            // ✗ allaqachon yozilgan
            M("s6", "g", "frozen", "2026-05-10", "2026-08-20"), // ✗ muzlatilgan
        };
        var written = new HashSet<(string, string?)> { ("s5", "g") };
        var fees = new Dictionary<string, decimal> { ["g"] = 500_000m };

        var pending = SubscriptionRisk.Pending(memberships, written, fees, DiscountBook.Empty, Month).ToList();

        var only = Assert.Single(pending);
        Assert.Equal("s1", only.StudentId);
        Assert.Equal(500_000m, only.Effective);
        Assert.False(only.Written);
    }

    // ============================ baza bilan (BuildAsync) ============================

    [Fact]
    public async Task Build_yozilmagan_oylikka_REGISTR_chegirmasi_qollanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "A", MonthlyFee = 500_000m };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Chegirmali", Balance = 100_000m };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(M(s.Id, g.Id, "active", "2026-05-10"));
        // 20% chegirma — yozilmagan oylik 400 000 bo'lishi kerak (500 000 EMAS).
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, StudentName = s.FullName, GroupId = g.Id, Pct = 20,
            Status = StudentDiscount.StatusActive, CreatedAt = "2020-01-01T00:00:00",
        });
        await ctx.SaveChangesAsync();

        var r = await SubscriptionRisk.BuildAsync(ctx, Month);

        var item = Assert.Single(r.Items);
        Assert.Equal(400_000m, item.LessonsTotal);
        Assert.Equal(-300_000m, item.Expected);
        Assert.Equal("active", item.MemberState);
    }

    [Fact]
    public async Task Build_yozilgan_hisob_va_arxivlangan_oquvchi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "A", MonthlyFee = 500_000m };
        ctx.Classes.Add(g);
        var debtor = new Student { FullName = "Qarzdor", Balance = -120_000m };
        var archived = new Student { FullName = "Arxivda", Balance = -500_000m, IsArchived = true };
        ctx.Students.AddRange(debtor, archived);
        ctx.StudentGroups.Add(M(debtor.Id, g.Id, "active", "2026-05-10"));
        ctx.StudentGroups.Add(M(archived.Id, g.Id, "active", "2026-05-10"));
        // Oy boshida yozilgan hisob (qisman chegirma bilan): effektiv = 450 000.
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = debtor.Id, GroupId = g.Id, Month = Month, Amount = 500_000m, Discount = 50_000m,
            Date = Month + "-01",
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = archived.Id, GroupId = g.Id, Month = Month, Amount = 500_000m, Date = Month + "-01",
        });
        await ctx.SaveChangesAsync();

        var r = await SubscriptionRisk.BuildAsync(ctx, Month);

        var item = Assert.Single(r.Items); // arxivlangan KIRMAYDI
        Assert.Equal(debtor.Id, item.StudentId);
        Assert.Equal(450_000m, item.LessonsTotal);
        Assert.Equal(-120_000m, item.Expected); // yozilgan — qayta ayrilmaydi
    }
}

/// <summary>
/// O'quvchilar ro'yxati HOLAT filtri va qo'shimcha ustunlari (<see cref="StudentListView"/>).
/// </summary>
public class StudentListViewTests
{
    private static Student WithStates(string memberState, params string[] statuses) => new()
    {
        FullName = "X",
        MemberState = memberState,
        GroupStates = statuses.Select(st => new StudentGroupState { GroupId = st, Name = st, Status = st }).ToList(),
    };

    [Fact]
    public void Yangi_royxat_tirik_SINOV_azoligi_borlarni_oladi__boshqa_kursda_aktiv_bolsa_ham()
    {
        // Bosh sahifa "Yangi o'quvchilar" kartochkasi (`DashboardSummary`) bilan bir xil ta'rif.
        Assert.True(StudentListView.MatchesState(WithStates("trial", "trial"), "trial"));
        Assert.True(StudentListView.MatchesState(WithStates("active", "active", "trial"), "trial"));
        Assert.False(StudentListView.MatchesState(WithStates("frozen", "frozen"), "trial"));
        Assert.False(StudentListView.MatchesState(WithStates(""), "trial"));
    }

    [Fact]
    public void Aktiv_royxat_MemberState_boyicha()
    {
        Assert.True(StudentListView.MatchesState(WithStates("active", "frozen", "active"), "active"));
        Assert.False(StudentListView.MatchesState(WithStates("trial", "trial"), "active"));
        Assert.False(StudentListView.MatchesState(WithStates("yearFrozen", "frozen"), "active"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("trial", true)]
    [InlineData("active", true)]
    [InlineData("frozen", false)]
    [InlineData("TRIAL", false)]
    public void Faqat_malum_holatlar_qabul_qilinadi(string? state, bool known)
    {
        Assert.Equal(known, StudentListView.IsKnownState(state));
        // Filtrsiz — hamma mos.
        if (string.IsNullOrEmpty(state)) Assert.True(StudentListView.MatchesState(WithStates(""), state));
    }

    [Fact]
    public void Moderator_avval_yopgan_keyin_ishlagan_xodim()
    {
        var names = new Dictionary<string, string> { ["closer"] = "Yopgan", ["worker"] = "Ishlagan" };
        Assert.Equal("Yopgan", StudentListView.PickModerator("closer", "worker", names));
        Assert.Equal("Ishlagan", StudentListView.PickModerator(null, "worker", names));
        Assert.Equal("Ishlagan", StudentListView.PickModerator("deleted-user", "worker", names));
        Assert.Equal("", StudentListView.PickModerator(null, null, names));
    }

    [Fact]
    public async Task Qoshimcha_ustunlar_mavjud_yozuvlardan_toldiriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var operatorUser = new AppUser { FullName = "Operator", Role = "staff", Email = "op@test.local" };
        var studentUser = new AppUser { FullName = "O'quvchi", Role = "student", Email = "st@test.local", FirstLoginAt = "2026-09-02T10:00:00" };
        ctx.Users.AddRange(operatorUser, studentUser);
        var s = new Student { FullName = "O'quvchi", UserId = studentUser.Id };
        var bare = new Student { FullName = "Lidsiz" };
        ctx.Students.AddRange(s, bare);
        ctx.Leads.Add(new Lead
        {
            FullName = "O'quvchi", Source = "instagram", ConvertedStudentId = s.Id,
            AssigneeUserId = operatorUser.Id, CreatedAt = "2026-08-01T09:00:00",
        });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "tuition", Date = "2026-09-05", Amount = 1 },
            new FinanceTransaction { StudentId = s.Id, Direction = "income", Category = "tuition", Date = "2026-08-05", Amount = 1 },
            // Vozvrat / boshqa kategoriya — "to'lov sanasi" emas.
            new FinanceTransaction { StudentId = s.Id, Direction = "expense", Category = "tuition", Date = "2026-09-10", Amount = 1 });
        ctx.Contracts.AddRange(
            new Contract { Target = "parent", RecipientKey = s.Id, Number = 7 },
            new Contract { Target = "parent", RecipientKey = s.Id, Number = 12 });
        await ctx.SaveChangesAsync();

        var list = new[] { s, bare };
        await StudentListView.EnrichExtrasAsync(ctx, list);

        Assert.Equal("instagram", s.LeadSource);
        Assert.Equal("Operator", s.Moderator);
        Assert.Equal("2026-09-05", s.LastPaymentDate);
        Assert.Equal("2026-09-02T10:00:00", s.AppFirstLoginAt);
        Assert.Equal(12, s.ContractNumber);

        Assert.Equal("", bare.LeadSource);
        Assert.Equal("", bare.Moderator);
        Assert.Equal("", bare.LastPaymentDate);
        Assert.Null(bare.ContractNumber);
    }
}

/// <summary>
/// O'quvchilar ro'yxatlari endpointlarining RUXSAT darvozasi. Test loyihasi Server'ga havola
/// qilmaydi — darvoza manba matnidan tekshiriladi (<see cref="HomeEndpointsRbacTests"/> naqshi).
/// </summary>
public class StudentListsRbacTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "StudentsController.cs");
        Assert.True(File.Exists(path), $"Controller fayli topilmadi: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Metod e'lonidan OLDINGI atributlar bloki (oldingi metod tanasi tugagan joydan).</summary>
    private static string AttributesOf(string src, string method)
    {
        var i = src.IndexOf($"> {method}(", StringComparison.Ordinal);
        Assert.True(i > 0, $"{method} topilmadi");
        var start = src.LastIndexOf("    }", i, StringComparison.Ordinal);
        return src[start..i];
    }

    [Fact]
    public void Sinf_darajasida_oquvchilar_royxati_ruxsati()
    {
        var src = Source();
        var header = src[..src.IndexOf("public class StudentsController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"students.list\")]", header);
    }

    [Theory]
    [InlineData("SubscriptionRiskReport", "[HttpGet(\"subscription-risk\")]")]
    [InlineData("GetAll", "[HttpGet]")]
    public void Royxat_endpointlari_sinf_darvozasini_bekor_qilmaydi(string method, string route)
    {
        var attrs = AttributesOf(Source(), method);
        Assert.Contains(route, attrs);
        // Metod darajasidagi [AdminPerm] sinfdagisini BEKOR qiladi (permissions.md §4) —
        // bu yerda boshqa kalit qo'yilmagan bo'lishi kerak; anonim kirish ham yo'q.
        Assert.DoesNotMatch(new Regex(@"\[AdminPerm\("), attrs);
        Assert.DoesNotContain("AllowAnonymous", attrs);
    }
}
