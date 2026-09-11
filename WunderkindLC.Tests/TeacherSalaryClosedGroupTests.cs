using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// GURUH YOPILGANDAN KEYIN O'QITUVCHI MAOSHI.
///
/// <para>Hayotiy holat: o'tgan oyda o'qituvchining faol guruhi bor edi; guruh yopildi, barcha
/// o'quvchilar muzlatildi, yangi oyda guruh arxivga o'tkazildi. Shundan keyin o'qituvchiga oylik
/// "yozilmay" qolardi — uch xil sabab bilan:</para>
/// <list type="number">
///   <item><b>O'tgan oy jadvaldan tushib qolardi.</b> Standart davr
///     <see cref="TuitionService.AcademicYearStartMonthAsync"/> dan boshlanadi, u esa ARXIVLANMAGAN
///     o'quvchilarning eng erta kelgan oyi — guruh bilan birga o'quvchilar arxivlangach JORIY oyga
///     sakrardi.</item>
///   <item><b>Jurnal ushlanmasi butun oylikni yeb qo'yardi.</b> Rejadagi darslar guruh hafta
///     kunlaridan chiqariladi va chegara faqat <see cref="Group.EndDate"/> bo'yicha olinardi —
///     "Arxivlash" yo'lida esa u qo'yilmaydi, ya'ni arxivdan keyingi "darslar" belgilanmagan
///     hisoblanardi.</item>
///   <item><b>Foizli maoshda pul hali yig'ilmagan bo'lsa 0 chiqardi</b> va nima uchun ishlangani
///     umuman ko'rinmasdi.</item>
/// </list>
/// </summary>
public class TeacherSalaryClosedGroupTests
{
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>50% foizli o'qituvchi + guruh + o'quvchi. Guruh 6 oy oldin boshlangan.</summary>
    private static (Teacher Teacher, Group Group, Student Student) Setup(
        AppDbContext ctx, bool studentArchived = false)
    {
        var t = new Teacher { FullName = "Yopilgan guruh o'qituvchisi", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "Yopilgan guruh", MonthlyFee = 600_000m, TeacherId = t.Id,
            TeacherSalaryMode = "percent", TeacherSalaryPercent = 50,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },   // har kuni dars — sana testda muhim emas
            StartDate = $"{M(-6)}-01",
        };
        ctx.Classes.Add(g);
        var s = new Student
        {
            FullName = "Muzlatilgan o'quvchi", EnrollmentDate = $"{M(-6)}-01",
            IsArchived = studentArchived,
        };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "frozen", IsActive = true,
            JoinedAt = $"{M(-6)}-01", ActivatedAt = $"{M(-6)}-01", RecordedAt = $"{M(-6)}-01",
            FrozenAt = $"{M(0)}-01",
        });
        return (t, g, s);
    }

    /// <summary>
    /// Guruh yopilib, o'quvchilari arxivlangan bo'lsa ham STANDART davr (from/to berilmagan)
    /// o'tgan oyni QAMRAB oladi — "o'tgan oy uchun qancha hisoblangan" savoli javobsiz qolmasin.
    /// </summary>
    [Fact]
    public async Task Guruh_yopilgach_OTGAN_OY_jadvaldan_tushib_qolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, _) = Setup(ctx, studentArchived: true);   // o'quvchi arxivda → akademik yil = joriy oy
        g.IsArchived = true;
        g.Status = "archived";
        g.ArchivedAt = $"{M(0)}-01";
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, null, null);

        Assert.Contains(dto.Months, x => x.Month == M(-1));
        Assert.Contains(dto.Months, x => x.Month == M(0));
    }

    /// <summary>
    /// ARXIVLANGAN guruhda dars REJALASHTIRILMAYDI: arxiv sanasidan keyingi kunlar "o'tkazib
    /// yuborilgan dars" bo'lib maoshdan ushlanmaydi (<c>EndDate</c> qo'yilmagan bo'lsa ham).
    /// </summary>
    [Fact]
    public async Task Arxivlangan_guruh_uchun_jurnal_ushlanmasi_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        // Maosh jurnalga bog'langan, muhlat yo'q.
        ctx.CenterMeta.Add(new CenterMeta { SalaryRequireJournal = true, SalaryGraceDays = 0 });

        // Guruh o'tgan oyning 1-kunida ARXIVGA olingan; EndDate ATAYIN bo'sh ("Arxivlash" yo'li).
        g.IsArchived = true;
        g.Status = "archived";
        g.ArchivedAt = $"{M(-1)}-01";

        // O'sha yagona kun jurnalda "o'tildi" deb belgilangan.
        ctx.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = "", Quarter = 1, Date = $"{M(-1)}-01", Conducted = true,
        });
        // Pul ham kelgan — maosh bazasi bor.
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(-1)}-05", Direction = "income", Category = "tuition", Amount = 600_000m,
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1));
        var otgan = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(1, otgan.PlannedLessons);      // faqat arxivgacha bo'lgan kun
        Assert.Equal(0, otgan.MissedLessons);
        Assert.Equal(0m, otgan.Deduction);
        Assert.Equal(300_000m, otgan.Expected);     // 600 000 × 50%
    }

    /// <summary>
    /// FOIZLI maoshda pul hali yig'ilmagan bo'lsa — <c>Expected</c> 0, lekin
    /// <c>Charged</c>/<c>PotentialExpected</c> o'qituvchi o'sha oyda nima ishlaganini ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Pul_yigilmagan_oyda_HISOBLANGAN_boyicha_maosh_korinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        // O'tgan oy uchun hisob yozilgan, lekin to'lov yo'q (guruh yopilgan, o'quvchi to'lamagan).
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 600_000m, Discount = 0m,
            Date = $"{M(-1)}-01",
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1));
        var otgan = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(0m, otgan.Expected);                  // yig'ilgan pul yo'q
        Assert.Equal(600_000m, otgan.Charged);             // o'quvchiga hisoblangan
        Assert.Equal(300_000m, otgan.PotentialExpected);   // hammasi to'lansa — 50%
    }

    /// <summary>Chegirma HISOBLANGANdan ayriladi — potentsial maosh ham shunga qarab kamayadi.</summary>
    [Fact]
    public async Task Chegirma_hisoblangan_bazadan_ayriladi()
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

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(-1), M(-1));
        var otgan = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(400_000m, otgan.Charged);
        Assert.Equal(200_000m, otgan.PotentialExpected);
    }

    /// <summary>
    /// Pul to'liq kelgan bo'lsa potentsial va haqiqiy raqam TENG — UI'da ortiqcha qator chiqmasin
    /// (u faqat <c>PotentialExpected &gt; Expected</c> bo'lganda ko'rsatiladi).
    /// </summary>
    [Fact]
    public async Task Hammasi_tolangan_oyda_potentsial_haqiqiyga_teng()
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
        var otgan = dto.Months.Single(x => x.Month == M(-1));

        Assert.Equal(300_000m, otgan.Expected);
        Assert.Equal(300_000m, otgan.PotentialExpected);
    }
}
