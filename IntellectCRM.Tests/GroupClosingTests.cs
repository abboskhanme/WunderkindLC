using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// GURUHNI YOPISH / TUGATISH (sertifikat bilan) hisob-kitobi.
///
/// <para>Talab: guruh yopilganda yoki sertifikat bilan tugatilganda o'quvchi shu oy uchun
/// "to'lagan" (hisobsiz) bo'lib qolmasligi kerak — belgilangan SANAGACHA o'qigan darslari uchun
/// oylik AYNAN ESKI GURUHGA yozilishi, sanadan keyingi oylar esa bekor qilinishi shart. Yangi
/// guruh esa o'z sanasidan aktivlashtiriladi.</para>
///
/// <para>Hisobning yagona manbai — <see cref="MembershipBilling.SettleFreezeAsync"/>: "Muzlatish",
/// "Guruh almashtirish", "Guruhni yopish" va "Guruhni tugatish" AYNAN shuni chaqiradi.</para>
///
/// <para>Sanalar MUTLAQ yozilmaydi — <see cref="AppClock.Today"/> ga NISBATAN quriladi.</para>
/// </summary>
public class GroupClosingTests
{
    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Shu (nisbiy) oydagi kunlar soni — har kuni dars bo'lgan guruhda "jami dars".</summary>
    private static int DaysIn(int delta)
    {
        var d = AppClock.Today.AddMonths(delta);
        return DateTime.DaysInMonth(d.Year, d.Month);
    }

    private static Student AddStudent(AppDbContext ctx, decimal balance = 0m)
    {
        var s = new Student
        {
            FullName = "Test O'quvchi",
            EnrollmentDate = $"{M(-6)}-01",
            Balance = balance,
        };
        ctx.Students.Add(s);
        return s;
    }

    /// <summary>Har kuni dars bo'ladigan guruh — "dars soni" = kunlar soni (hisob oson tekshiriladi).</summary>
    private static Group AddGroup(AppDbContext ctx, decimal fee = 600_000m, string name = "A guruh")
    {
        var g = new Group
        {
            Name = name,
            MonthlyFee = fee,
            Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Classes.Add(g);
        return g;
    }

    private static StudentGroup AddMembership(
        AppDbContext ctx, Student s, Group g, string activatedAt, string status = "active")
    {
        var m = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = true,
            JoinedAt = activatedAt, ActivatedAt = status == "trial" ? "" : activatedAt,
            RecordedAt = activatedAt,
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    private static void AddCharge(AppDbContext ctx, Student s, string groupId, string month, decimal amount)
        => ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = groupId, Month = month, Amount = amount, Date = $"{month}-01",
        });

    // ==================== MembershipBilling.SettleFreezeAsync ====================

    [Fact]
    public async Task Yopish_eski_guruhga_QISMAN_oylik_yozadi_va_keyingi_oylarni_bekor_qiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx, balance: -1_200_000m);   // ikkita to'liq oylik hisoblangan
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(-3)}-01", $"{M(-1)}-10");
        await ctx.SaveChangesAsync();

        Assert.True(res.Charged);
        Assert.Equal(600_000m, res.Restored);
        Assert.Equal(new[] { M(0) }, res.PurgedMonths);

        // Yopish oyi QISMAN (10 kun), keyingi oy umuman yo'q — "hisob yo'q = to'langan" holati emas.
        var row = Assert.Single(ctx.MonthlyCharges.ToList());
        Assert.Equal(M(-1), row.Month);
        Assert.Equal(g.Id, row.GroupId);
        Assert.Equal(decimal.Round(600_000m * 10 / DaysIn(-1), 2), row.Amount);
    }

    [Fact]
    public async Task Yopish_sanasi_aktivlashtirishdan_OLDIN_bolsa_hisob_umuman_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx, balance: -600_000m);
        // O'quvchi keyingi oydan aktivlashtirilgan, guruh esa o'tgan oyda yopilyapti — o'qimagan.
        AddMembership(ctx, s, g, $"{M(0)}-01");
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(0)}-01", $"{M(-1)}-20");
        await ctx.SaveChangesAsync();

        Assert.False(res.Charged);
        Assert.Equal(600_000m, res.Restored);
        Assert.Empty(ctx.MonthlyCharges.ToList());
        Assert.Equal(0m, s.Balance);
    }

    [Fact]
    public async Task Yopish_QOLDA_tahrirlangan_Locked_hisobga_tegmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, $"{M(-3)}-01");
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 123_000m,
            Date = $"{M(0)}-01", Locked = true,
        });
        await ctx.SaveChangesAsync();

        var res = await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(-3)}-01", $"{M(-1)}-10");
        await ctx.SaveChangesAsync();

        Assert.Empty(res.PurgedMonths);
        var locked = ctx.MonthlyCharges.Single(c => c.Month == M(0));
        Assert.Equal(123_000m, locked.Amount);
    }

    // ==================== "Tugatish (sertifikat bilan)" oqimi ====================

    [Fact]
    public async Task Tugatishda_eski_guruh_qarzi_qoladi_yangi_guruh_oz_sanasidan_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var eski = AddGroup(ctx, 600_000m, "Beginner");
        var yangi = AddGroup(ctx, 800_000m, "Elementary");
        var s = AddStudent(ctx, balance: -600_000m);
        var m = AddMembership(ctx, s, eski, $"{M(-3)}-01");
        AddCharge(ctx, s, eski.Id, M(-1), 600_000m);
        await ctx.SaveChangesAsync();

        var closeDate = $"{M(-1)}-10";
        var activateDate = $"{M(-1)}-11";

        // 1) Eski guruh — hisob yopiladi, a'zolik "tugatgan" bo'ladi (controller bilan bir xil tartib).
        await MembershipBilling.SettleFreezeAsync(ctx, s, eski, m.ActivatedAt, closeDate);
        m.FrozenAt = closeDate;
        m.Status = "completed";
        m.IsActive = false;
        m.LeftAt = closeDate;
        await ctx.SaveChangesAsync();

        // 2) Yangi guruh — o'z sanasidan aktivlashtiriladi.
        var yangiM = new StudentGroup
        {
            StudentId = s.Id, GroupId = yangi.Id, Status = "active", IsActive = true,
            JoinedAt = activateDate, ActivatedAt = activateDate,
            RecordedAt = AppClock.Today.ToString("yyyy-MM-dd"),
        };
        ctx.StudentGroups.Add(yangiM);
        await TuitionService.ChargeActivationProrateAsync(ctx, s, yangi, activateDate);
        await TuitionService.AccrueCatchUpAsync(ctx, s, yangi, activateDate);
        await ctx.SaveChangesAsync();

        // ESKI guruhda o'qilgan 10 kun uchun qarz AYNAN eski guruhga yozilgan.
        var eskiRow = Assert.Single(ctx.MonthlyCharges.Where(c => c.GroupId == eski.Id).ToList());
        Assert.Equal(M(-1), eskiRow.Month);
        Assert.Equal(decimal.Round(600_000m * 10 / DaysIn(-1), 2), eskiRow.Amount);

        // YANGI guruh o'z sanasidan hisoblanadi (eski guruh narxi bilan aralashmaydi). 11-sanadan
        // oy oxirigacha 18+ dars qolgani uchun aktivlashtirish oyi TO'LIQ oylik
        // (TuitionService.FullMonthLessonThreshold = 12), oradagi oy ham to'liq.
        var yangiOylar = ctx.MonthlyCharges.Where(c => c.GroupId == yangi.Id)
            .OrderBy(c => c.Month).ToList();
        Assert.Equal(new[] { M(-1), M(0) }, yangiOylar.Select(c => c.Month).ToArray());
        Assert.All(yangiOylar, r => Assert.Equal(800_000m, r.Amount));
    }

    // ==================== "Tugatish" — KIM yangi guruhga ko'chadi ====================

    /// <summary>Sof qoida uchun a'zolik yasovchi (bazasiz).</summary>
    private static StudentGroup Member(string studentId, string status, bool isActive = true) =>
        new() { StudentId = studentId, GroupId = "g1", Status = status, IsActive = isActive };

    [Fact]
    public void Kochiriladiganlar_FAQAT_aktiv_azolar()
    {
        var ids = GroupCompletionRules.TransferableStudentIds(new[]
        {
            Member("s1", "active"),
            Member("s2", "trial"),      // sinovda — ko'chirilmaydi
            Member("s3", "frozen"),     // muzlatilgan — ko'chirilmaydi
            Member("s4", "active"),
        });

        Assert.Equal(new[] { "s1", "s4" }, ids);
    }

    [Fact]
    public void Kochiriladiganlar_guruhdan_CHIQQAN_azoni_olmaydi()
    {
        // IsActive=false — a'zolik allaqachon yakunlangan (chiqarilgan/tugatilgan).
        var ids = GroupCompletionRules.TransferableStudentIds(new[]
        {
            Member("s1", "active", isActive: false),
            Member("s2", "active"),
        });

        Assert.Equal(new[] { "s2" }, ids);
    }

    [Fact]
    public void Kochiriladiganlar_faqat_sinovdagilar_bolsa_BOSH_qaytadi()
    {
        var ids = GroupCompletionRules.TransferableStudentIds(new[]
        {
            Member("s1", "trial"),
            Member("s2", "frozen"),
        });

        Assert.Empty(ids);
    }

    // ==================== StudentGroupLedger — chiqish oyi ====================

    [Fact]
    public async Task Ledger_tugatilgan_azolikda_chiqish_oyi_TOLIQ_oylik_bolib_kormaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx);
        var m = AddMembership(ctx, s, g, $"{M(-3)}-01");
        // Hisob qatori YO'Q (masalan qisman summa 0 chiqqan) — ledger PREVIEW ko'rsatadi.
        m.Status = "completed";
        m.IsActive = false;
        m.LeftAt = $"{M(0)}-05";
        await ctx.SaveChangesAsync();

        var dto = await StudentGroupLedger.BuildAsync(ctx, s, g, m);

        var oxirgi = dto.Months.Last();
        Assert.Equal(M(0), oxirgi.Month);
        // Chiqish oyi 5 kunlik QISMAN summa — to'liq oylik EMAS.
        Assert.Equal(decimal.Round(600_000m * 5 / DaysIn(0), 2), oxirgi.Fee);
        Assert.NotEqual(600_000m, oxirgi.Fee);
    }

    // ==================== ARXIV / YOPILGAN GURUHGA TO'LOV ====================

    /// <summary>HTTP kontekstsiz <see cref="AuditService"/> uchun (testda so'rov yo'q).</summary>
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static async Task<PaymentIntakeResult> PayAsync(
        AppDbContext ctx, Student s, string groupId, string month, decimal amount)
    {
        var audit = new AuditService(ctx, new NoHttpContext());
        var stack = new MessagingStack();
        return await PaymentIntake.AddAsync(
            ctx, audit, stack.Auto, s,
            new PaymentRequest(amount, month, groupId, Method: "cash"),
            createdBy: "Test kassir");
    }

    [Fact]
    public async Task Arxiv_guruhga_tolov_QABUL_qilinadi_boshqa_faol_guruhi_bolsa_ham()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var eski = AddGroup(ctx, 600_000m, "Beginner (yopilgan)");
        var yangi = AddGroup(ctx, 800_000m, "Elementary");
        var s = AddStudent(ctx);

        // Eski guruh sertifikat bilan tugatilgan — a'zolik "completed" + IsActive=false.
        var eskiM = AddMembership(ctx, s, eski, $"{M(-3)}-01");
        eskiM.Status = "completed";
        eskiM.IsActive = false;
        eskiM.LeftAt = $"{M(-1)}-10";
        eskiM.FrozenAt = $"{M(-1)}-10";
        AddCharge(ctx, s, eski.Id, M(-1), 200_000m);   // yopish sanasigacha qolgan qarz
        // Yangi guruhda faol.
        AddMembership(ctx, s, yangi, $"{M(-1)}-11");
        await ctx.SaveChangesAsync();

        var res = await PayAsync(ctx, s, eski.Id, M(-1), 200_000m);
        await ctx.SaveChangesAsync();

        Assert.Null(res.Error);
        var tx = Assert.Single(ctx.FinanceTransactions.Where(t => t.Category == "tuition").ToList());
        // Pul AYNAN eski (arxiv) guruhga teglanadi — faol guruhga "sirg'alib" ketmaydi.
        Assert.Equal(eski.Id, tx.GroupId);
        Assert.Equal(M(-1), tx.Month);
    }

    [Fact]
    public async Task Faqat_yopilgan_azoligi_bor_oquvchida_tolov_osha_guruhga_teglanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var eski = AddGroup(ctx, 600_000m, "Beginner (yopilgan)");
        var s = AddStudent(ctx);
        var m = AddMembership(ctx, s, eski, $"{M(-3)}-01");
        m.Status = "completed";
        m.IsActive = false;
        m.LeftAt = $"{M(-1)}-10";
        m.FrozenAt = $"{M(-1)}-10";
        AddCharge(ctx, s, eski.Id, M(-1), 200_000m);
        await ctx.SaveChangesAsync();

        var res = await PayAsync(ctx, s, eski.Id, M(-1), 200_000m);
        await ctx.SaveChangesAsync();

        Assert.Null(res.Error);
        var tx = Assert.Single(ctx.FinanceTransactions.Where(t => t.Category == "tuition").ToList());
        Assert.Equal(eski.Id, tx.GroupId);   // teglanmagan (null) bo'lib qolmaydi
    }

    // ==================== GURUHNI ARXIVLASH — O'QUVCHI ARXIVLANMAYDI ====================

    /// <summary>Repo ildizi (<c>IntellectCRM.slnx</c> yonidagi papka).</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IntellectCRM.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (IntellectCRM.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    /// <summary><c>ClassesController.Archive</c> METODINING tanasi (keyingi metodgacha).
    /// Test loyihasi <c>IntellectCRM.Server</c> ga bog'lanmagan (csproj'da ProjectReference yo'q),
    /// shuning uchun endpoint MANBA matni bo'yicha qulflanadi.</summary>
    private static string ArchiveMethodSource()
    {
        var src = File.ReadAllText(Path.Combine(
            RepoRoot, "IntellectCRM.Server", "Controllers", "ClassesController.cs"));
        var start = src.IndexOf("public async Task<IActionResult> Archive(string id)", StringComparison.Ordinal);
        Assert.True(start > 0, "ClassesController.Archive topilmadi");
        // Chegara — metodning O'Z yopuvchi qavsi (qavslarni sanaymiz): keyingi metodning IZOHI
        // tanaga tushsa, u yerdagi «ArchivedWithClass» so'zi tekshiruvni yolg'on yiqitardi.
        var open = src.IndexOf('{', start);
        Assert.True(open > start, "Archive tanasi topilmadi");
        var depth = 0;
        var end = open;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) { end = i + 1; break; }
        }
        Assert.True(end > open, "Archive tanasining oxiri topilmadi");
        return src[start..end];
    }

    /// <summary>
    /// ⚠️ Guruh arxivlanganda O'QUVCHI <b>arxivga tushmaydi</b> — u shunchaki «Aktiv emas»
    /// bo'lib ro'yxatda qoladi. Ilgari <c>Archive</c> o'quvchilarni <c>IsArchived=true</c>
    /// qilar va ular ro'yxatdan butunlay yo'qolib, faqat «Arxiv» tabida qolardi.
    /// </summary>
    [Fact]
    public void Archive_endpointi_OQUVCHINI_arxivlamaydi()
    {
        var body = ArchiveMethodSource();

        // O'quvchi arxiv maydonlariga TEGILMAYDI (bular faqat Unarchive/StudentsController ishi).
        Assert.DoesNotContain("ArchivedWithClass", body);
        Assert.DoesNotContain("ArchiveReason", body);
        Assert.DoesNotContain("db.Students", body);
        // Javob shakli saqlangan, lekin arxivlangan o'quvchilar soni HAR DOIM 0.
        Assert.Contains("archivedStudents = 0", body);
    }

    /// <summary>
    /// A'zolikni muzlatish yadrosi «Guruhni yopish» bilan AYNAN BITTA —
    /// <c>CloseMembersAsync</c>. Nusxa ko'chirilsa ikkisi jimgina ayri ketardi
    /// (masalan qisman to'lov bir yo'lda yozilib, ikkinchisida yo'q bo'lardi).
    /// </summary>
    [Fact]
    public void Archive_azoliklarni_YOPISH_bilan_BITTA_yadroda_muzlatadi()
    {
        var body = ArchiveMethodSource();
        Assert.Contains("CloseMembersAsync(cls", body);
        Assert.Contains("frozenMembers", body);
    }

    /// <summary>
    /// ⚠️ A'ZOLIKNI MUZLATISH SHART — faqat "o'quvchini arxivlamaslik" YETMAYDI:
    /// <c>AccrueMonth</c> guruhning <c>IsArchived</c> bayrog'iga QARAMAYDI, ya'ni a'zolik
    /// <c>active</c> bo'lib qolsa o'quvchiga har oy YOLG'ON QARZ yozilib, ota-onalarga to'lov
    /// eslatmasi SMS'i ketaverardi (`.claude/rules/membership-periods.md` §6).
    ///
    /// <para>Bu yerda arxivlash NATIJASI qulflanadi: o'quvchi arxivda EMAS, a'zolik
    /// <c>frozen</c>, jamlama holat "aktiv" EMAS va keyingi oyga hisob YOZILMAYDI.</para>
    /// </summary>
    [Fact]
    public async Task Guruh_ARXIVLANGANDA_oquvchi_ARXIVLANMAYDI_azolik_MUZLATILADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx);
        var s = AddStudent(ctx, balance: -600_000m);
        var m = AddMembership(ctx, s, g, $"{M(-3)}-01");
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        await ctx.SaveChangesAsync();

        // `Archive` → `CloseMembersAsync` yadrosi: guruh arxivga, a'zolik muzlatiladi.
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        g.IsArchived = true;
        g.ArchivedAt = today;
        var activatedAt = m.ActivatedAt;
        m.Status = "frozen";
        m.FrozenAt = today;
        await MembershipBilling.SettleFreezeAsync(ctx, s, g, activatedAt, today, membership: m);
        await ctx.SaveChangesAsync();

        // 1) O'quvchi ARXIVDA EMAS — ro'yxatdan yo'qolmaydi.
        Assert.False(s.IsArchived);
        Assert.False(s.ArchivedWithClass);
        Assert.Null(s.ArchivedAt);

        // 2) A'zolik MUZLATILGAN — o'quvchi o'z-o'zidan "Aktiv emas" bo'lib qoladi.
        Assert.Equal("frozen", m.Status);
        Assert.True(m.IsActive);   // ⚠️ to'lov qabul qilish uchun a'zolik TIRIK qoladi
        Assert.Equal("frozen", MembershipLifecycle.MemberState(new[] { m }));
        Assert.NotEqual("active", MembershipLifecycle.MemberState(new[] { m }));
        Assert.False(MembershipLifecycle.IsActiveOverall(new[] { m }));

        // 3) Keyingi oyga YOLG'ON QARZ yozilmaydi.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(1))).Count);
        Assert.DoesNotContain(M(1), ctx.MonthlyCharges.Select(c => c.Month).ToList());
    }

    /// <summary>Boshqa guruhda o'qiyotgan o'quvchi guruh arxivlangandan keyin ham AKTIV
    /// qoladi — jamlama holat BARCHA tirik a'zoliklar ustidan olinadi.</summary>
    [Fact]
    public async Task Guruh_ARXIVLANGANDA_boshqa_guruhdagi_azolik_AKTIV_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var arxiv = AddGroup(ctx, name: "A guruh");
        var boshqa = AddGroup(ctx, name: "B guruh");
        var s = AddStudent(ctx);
        var mA = AddMembership(ctx, s, arxiv, $"{M(-3)}-01");
        var mB = AddMembership(ctx, s, boshqa, $"{M(-3)}-01");
        await ctx.SaveChangesAsync();

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        arxiv.IsArchived = true;
        mA.Status = "frozen";
        mA.FrozenAt = today;
        await ctx.SaveChangesAsync();

        Assert.False(s.IsArchived);
        Assert.Equal("active", MembershipLifecycle.MemberState(new[] { mA, mB }));
        Assert.True(MembershipLifecycle.IsActiveOverall(new[] { mA, mB }));
    }
}
