using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Kpi;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// AUDIT T4 — o'qituvchi maoshi, o'rinbosarlar, KPI va o'qituvchi hisobotlari bo'yicha tuzatishlar.
/// <list type="bullet">
///   <item><b>C3</b> — o'qituvchi almashsa guruhning o'tgan oylari eski o'qituvchida qoladi (tarix bo'yicha,
///     oy ichida darslar nisbatida); ikki varaqa yig'indisi bitta o'qituvchidagiga TENG;</item>
///   <item><b>C4</b> — qat'iy summa guruh yopilganda / o'qituvchi arxivlanganda to'xtaydi;</item>
///   <item><b>SAL-1</b> — o'rinbosar darslari guruhning amaldagi dars kunlari bilan kesishtiriladi;</item>
///   <item><b>SAL-2/SAL-4</b> — arxivda kelajakdagi tayinlovlar bekor qilinadi, bekor qilishda o'tmish oynasi;</item>
///   <item><b>SAL-3/SAL-5</b> — KPI ketganlar (tugatish/sinov kirmaydi, arxiv sababi) va ketgan xodimning oyi.</item>
/// </list>
/// </summary>
public class AuditFixT4Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string Controller(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    /* =========================================================================================
     *  C3 — GURUH EGALIGI TARIX BO'YICHA
     * ====================================================================================== */

    private static List<GroupTeacherAssignment> History(string groupId, string a, string b, string changeDay) =>
    [
        new() { GroupId = groupId, TeacherId = a, FromDate = "2026-01-01", ToDate = changeDay },
        new() { GroupId = groupId, TeacherId = b, FromDate = changeDay, ToDate = null },
    ];

    [Fact]
    public void TeacherOnDate__almashuv_kuni_YANGI__oldin_ESKI__birinchi_qatordan_oldin_BIRINCHI_oqituvchi()
    {
        var h = History("g", "A", "B", "2026-06-15");
        Assert.Equal("A", GroupTeacherHistory.TeacherOnDate(h, "2026-06-14", "B"));
        Assert.Equal("B", GroupTeacherHistory.TeacherOnDate(h, "2026-06-15", "B"));   // almashuv kuni — yangisi
        Assert.Equal("B", GroupTeacherHistory.TeacherOnDate(h, "2026-09-01", "B"));
        Assert.Equal("A", GroupTeacherHistory.TeacherOnDate(h, "2025-12-01", "B"));   // tarixdan oldin — birinchisi
        // Tarix umuman yo'q — hozirgi o'qituvchi.
        Assert.Equal("B", GroupTeacherHistory.TeacherOnDate(null, "2026-06-01", "B"));
        // O'qituvchi olib tashlangan oraliq — hech kim.
        var removed = new List<GroupTeacherAssignment>
        {
            new() { GroupId = "g", TeacherId = "A", FromDate = "2026-01-01", ToDate = "2026-03-01" },
        };
        Assert.Null(GroupTeacherHistory.TeacherOnDate(removed, "2026-04-01", ""));
    }

    [Fact]
    public void SharesInMonth__oy_ichida_almashuv_darslar_nisbatida_bolinadi()
    {
        var h = History("g", "A", "B", "2026-06-15");
        // Iyun-2026 dushanbalari: 1, 8 (A) | 15, 22, 29 (B).
        var dates = new List<string> { "2026-06-01", "2026-06-08", "2026-06-15", "2026-06-22", "2026-06-29" };
        var shares = GroupTeacherHistory.SharesInMonth(h, dates, "2026-06", "B");
        Assert.Equal(0.4m, shares["A"]);
        Assert.Equal(0.6m, shares["B"]);
        Assert.Equal(1m, shares.Values.Sum());

        var noHistory = GroupTeacherHistory.SharesInMonth(null, dates, "2026-06", "B");
        Assert.Equal(1m, Assert.Single(noHistory).Value);
    }

    /// <summary>Dushanba darsli, 50% foizli guruh; A 15-iyungacha, keyin B. May va iyun uchun 1 000 000 to'langan.</summary>
    private static (Teacher A, Teacher B, Group G, Student S) SetupTeacherChange(AppDbContext ctx, bool withHistory = true)
    {
        var a = new Teacher { FullName = "Eski o'qituvchi", SalaryMode = "percent", SalaryPercent = 50 };
        var b = new Teacher { FullName = "Yangi o'qituvchi", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.AddRange(a, b);
        var g = new Group
        {
            Name = "Almashgan guruh", MonthlyFee = 1_000_000m, TeacherId = withHistory ? b.Id : a.Id,
            TeacherSalaryMode = "percent", TeacherSalaryPercent = 50,
            Days = new List<int> { 0 }, StartDate = "2026-01-01",
        };
        ctx.Classes.Add(g);
        if (withHistory) ctx.GroupTeacherAssignments.AddRange(History(g.Id, a.Id, b.Id, "2026-06-15"));
        var s = new Student { FullName = "To'lovchi o'quvchi", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        foreach (var m in new[] { "2026-05", "2026-06" })
            ctx.FinanceTransactions.Add(new FinanceTransaction
            {
                Date = $"{m}-05", Direction = "income", Category = "tuition", Amount = 1_000_000m,
                StudentId = s.Id, GroupId = g.Id, Month = m, Method = "cash",
            });
        return (a, b, g, s);
    }

    [Fact]
    public async Task C3__oqituvchi_almashsa_otgan_oy_ESKI_oqituvchida_qoladi__oy_ichida_darslar_nisbatida_va_yigindi_TENG()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, b, _, _) = SetupTeacherChange(ctx);
        await ctx.SaveChangesAsync();

        var la = await SalaryLedger.BuildAsync(ctx, a, "2026-05", "2026-06");
        var lb = await SalaryLedger.BuildAsync(ctx, b, "2026-05", "2026-06");

        // May — butunlay A (guruh hozir B niki bo'lsa ham — ilgari bu oy B ga ko'chib ketardi).
        Assert.Equal(500_000m, la.Months.Single(m => m.Month == "2026-05").Expected);
        Assert.Equal(0m, lb.Months.Single(m => m.Month == "2026-05").Expected);
        // Iyun — 5 darsdan 2 tasi A, 3 tasi B.
        Assert.Equal(200_000m, la.Months.Single(m => m.Month == "2026-06").Expected);
        Assert.Equal(300_000m, lb.Months.Single(m => m.Month == "2026-06").Expected);
        // Nol yig'indi: ikki varaqa bitta o'qituvchidagi summaga teng.
        Assert.Equal(1_000_000m, la.TotalExpected + lb.TotalExpected);
        // Eski o'qituvchi varaqasida tarixiy guruh qatori ko'rinadi.
        Assert.Contains(la.Groups!, l => l.PeriodExpected == 700_000m);
    }

    [Fact]
    public async Task C3__tarixsiz_guruh_hozirgi_oqituvchiga_toliq_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, _, _, _) = SetupTeacherChange(ctx, withHistory: false);
        await ctx.SaveChangesAsync();

        var la = await SalaryLedger.BuildAsync(ctx, a, "2026-05", "2026-06");
        Assert.Equal(1_000_000m, la.TotalExpected);   // 2 × 1 000 000 × 50%
    }

    [Fact]
    public async Task C3__orinbosarlik_ushlanmasi_QAMRALGAN_SANA_egasidan__saqlangan_OriginalTeacherIddan_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, b, g, _) = SetupTeacherChange(ctx);
        var sub = new Teacher { FullName = "O'rinbosar", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.Add(sub);
        // Tayinlov A egalik qilgan paytda yaratilgan (OriginalTeacherId = A), lekin 22-iyun — B ning kuni.
        ctx.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = a.Id, SubstituteTeacherId = sub.Id,
            Date = "2026-06-22", EndDate = "2026-06-22", SelectedDates = new List<string> { "2026-06-22" },
            IsActive = true, CreatedBy = "Admin",
        });
        await ctx.SaveChangesAsync();

        var juneA = (await SalaryLedger.BuildAsync(ctx, a, "2026-06", "2026-06")).Months.Single();
        var juneB = (await SalaryLedger.BuildAsync(ctx, b, "2026-06", "2026-06")).Months.Single();
        var juneS = (await SalaryLedger.BuildAsync(ctx, sub, "2026-06", "2026-06")).Months.Single();

        // Bitta dars narxi = 500 000 / 5 = 100 000.
        Assert.Equal(0m, juneA.SubstituteDeduction);
        Assert.Equal(100_000m, juneB.SubstituteDeduction);
        Assert.Equal(100_000m, juneS.SubstituteFee);
        Assert.Equal(200_000m, juneA.Expected);
        Assert.Equal(200_000m, juneB.Expected);
        // Markaz uchun neytral: uchalasi birga guruhning iyundagi hovuzi.
        Assert.Equal(500_000m, juneA.Expected + juneB.Expected + juneS.Expected);
    }

    [Fact]
    public async Task C3__jurnal_jarimasi_faqat_oqituvchining_OZ_sanalari_uchun()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, b, g, _) = SetupTeacherChange(ctx);
        ctx.CenterMeta.Add(new CenterMeta { SalaryRequireJournal = true, SalaryGraceDays = 0 });
        // A ning ikkala darsi (1, 8) va B ning bittasi (15) belgilangan.
        foreach (var d in new[] { "2026-06-01", "2026-06-08", "2026-06-15" })
            ctx.LessonNotes.Add(new LessonNote { ClassId = g.Id, SubjectId = "", Quarter = 1, Date = d, Conducted = true });
        await ctx.SaveChangesAsync();

        var juneA = (await SalaryLedger.BuildAsync(ctx, a, "2026-06", "2026-06")).Months.Single();
        var juneB = (await SalaryLedger.BuildAsync(ctx, b, "2026-06", "2026-06")).Months.Single();

        Assert.Equal(2, juneA.PlannedLessons);
        Assert.Equal(0, juneA.MissedLessons);
        Assert.Equal(200_000m, juneA.Expected);
        Assert.Equal(3, juneB.PlannedLessons);
        Assert.Equal(2, juneB.MissedLessons);                // 22 va 29 — B ning o'zi belgilamagan
        Assert.Equal(100_000m, juneB.Expected);              // 300 000 − 300 000 × 2/3
    }

    [Fact]
    public async Task C3__tarixsiz_guruhda_oqituvchi_almashsa_eski_oqituvchi_davri_YOZIB_qolinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = new Teacher { FullName = "A" };
        var b = new Teacher { FullName = "B" };
        ctx.Teachers.AddRange(a, b);
        var g = new Group { Name = "Tarixsiz", TeacherId = a.Id, StartDate = "2026-02-01", Days = new List<int> { 0 } };
        ctx.Classes.Add(g);
        await ctx.SaveChangesAsync();

        g.TeacherId = b.Id;   // ClassesController.Update dagi kabi — saqlashdan OLDIN
        await GroupTeacherHistory.AssignAsync(ctx, g.Id, b.Id, "Admin");
        await ctx.SaveChangesAsync();

        var rows = await ctx.GroupTeacherAssignments.Where(x => x.GroupId == g.Id).OrderBy(x => x.FromDate).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(a.Id, rows[0].TeacherId);
        Assert.Equal("2026-02-01", rows[0].FromDate);
        Assert.NotNull(rows[0].ToDate);
        Assert.Equal(b.Id, rows[1].TeacherId);
        Assert.Null(rows[1].ToDate);
    }

    /* =========================================================================================
     *  C4 — QAT'IY SUMMA TO'XTAYDI
     * ====================================================================================== */

    [Fact]
    public void MonthFraction__ikkala_chekka_oy_qisman()
    {
        Assert.Equal(1m, SalaryLedger.MonthFraction("2026-01", "2026-01-01", null));
        Assert.Equal(15m / 31m, SalaryLedger.MonthFraction("2026-03", null, "2026-03-15"));
        Assert.Equal(0m, SalaryLedger.MonthFraction("2026-04", null, "2026-03-15"));
        Assert.Equal(21m / 30m, SalaryLedger.MonthFraction("2026-04", "2026-04-10", null));
        Assert.Equal(0m, SalaryLedger.MonthFraction("2026-03", "2026-04-10", null));
        Assert.Equal(6m / 30m, SalaryLedger.MonthFraction("2026-04", "2026-04-10", "2026-04-15"));
    }

    [Fact]
    public async Task C4__guruh_qatiy_summasi_guruh_yopilgach_toxtaydi__keyin_ochilgan_guruh_oldingi_oylarga_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = new Teacher { FullName = "Qat'iy guruhli", SalaryMode = "fixed", Salary = 0m };
        ctx.Teachers.Add(t);
        ctx.Classes.Add(new Group
        {
            Name = "Yopilgan", TeacherId = t.Id, TeacherSalaryMode = "fixed", TeacherSalaryFixed = 3_100_000m,
            Days = new List<int> { 0, 2, 4 }, StartDate = "2026-01-01",
            IsArchived = true, Status = "archived", ArchivedAt = "2026-03-10",
        });
        ctx.Classes.Add(new Group
        {
            Name = "Yangi", TeacherId = t.Id, TeacherSalaryMode = "fixed", TeacherSalaryFixed = 3_000_000m,
            Days = new List<int> { 0, 2, 4 }, StartDate = "2026-04-10",
        });
        await ctx.SaveChangesAsync();

        var l = await SalaryLedger.BuildAsync(ctx, t, "2026-01", "2026-05");
        decimal M(string m) => l.Months.Single(x => x.Month == m).Expected;

        Assert.Equal(3_100_000m, M("2026-01"));
        Assert.Equal(3_100_000m, M("2026-02"));
        Assert.Equal(1_000_000m, M("2026-03"));             // 10/31 — yopilgan oy QISMAN, yangi guruh hali yo'q
        Assert.Equal(2_100_000m, M("2026-04"));             // faqat yangi guruh: 21/30
        Assert.Equal(3_000_000m, M("2026-05"));             // yopilgan guruh uchun HECH NARSA
    }

    [Fact]
    public async Task C4_SAL2__arxivlangan_oqituvchining_qatiy_oyligi_arxiv_kunida_toxtaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = new Teacher
        {
            FullName = "Ishdan ketgan", SalaryMode = "fixed", Salary = 3_100_000m, SalaryStartDate = "2026-01-01",
            IsArchived = true, ArchivedAt = "2026-03-10",
        };
        ctx.Teachers.Add(t);
        await ctx.SaveChangesAsync();

        var l = await SalaryLedger.BuildAsync(ctx, t, "2026-01", "2026-05");
        decimal M(string m) => l.Months.Single(x => x.Month == m).Expected;
        Assert.Equal(3_100_000m, M("2026-02"));
        Assert.Equal(1_000_000m, M("2026-03"));
        Assert.Equal(0m, M("2026-04"));
        Assert.Equal(0m, M("2026-05"));
        Assert.Equal(7_200_000m, l.TotalExpected);
    }

    /* =========================================================================================
     *  SAL-1 — O'RINBOSAR DARSLARI AMALDAGI DARS KUNLARI BILAN KESISHTIRILADI
     * ====================================================================================== */

    [Fact]
    public void SAL1__guruh_yopilgandan_keyingi_va_kochirilgan_sanalar_sanalmaydi()
    {
        // Dush/Chor/Juma; avgust-2026: 3, 5, 7, 10, 12, 14 ... Guruh 7-avgustda yopilgan.
        var g = new Group { Name = "G", Days = new List<int> { 0, 2, 4 }, ArchivedAt = "2026-08-07", IsArchived = true };
        var a = new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = "o", SubstituteTeacherId = "s", IsActive = true,
            Date = "2026-08-03", EndDate = "2026-08-14",
            SelectedDates = new List<string> { "2026-08-03", "2026-08-05", "2026-08-07", "2026-08-10", "2026-08-12", "2026-08-14" },
        };
        // Maxraj (oyning dars soni) allaqachon qirqilgan — surat ham qirqilishi SHART.
        Assert.Equal(3, SubstituteTeacherService.ScheduledLessonsInMonth(g, "2026-08"));
        Assert.Equal(3, SubstituteTeacherService.LessonsInMonth(a, g, "2026-08"));
        Assert.Equal(new[] { "2026-08-03", "2026-08-05", "2026-08-07" }, SubstituteTeacherService.EffectiveDates(a, g));

        // Ko'chirilgan dars: 5-avgust → 6-avgust. Eski sana uchun haq YO'Q — o'rniga darsning YANGI
        // sanasi sanaladi (audit R1-2: o'sha dars, boshqa kunda; ilgari u tushib qolib 2 chiqardi).
        var moves = new List<JournalService.LessonMove> { new("2026-08-05", "2026-08-06") };
        Assert.Equal(3, SubstituteTeacherService.LessonsInMonth(a, g, "2026-08", moves));
        Assert.Equal(new[] { "2026-08-03", "2026-08-06", "2026-08-07" }, SubstituteTeacherService.EffectiveDates(a, g, moves));

        // Guruhsiz chaqiruv (kirish huquqi) — saqlangan sanalar o'zgarmaydi.
        Assert.Equal(6, SubstituteTeacherService.EffectiveDates(a).Count);
    }

    /* =========================================================================================
     *  SAL-4 — BEKOR QILISHDA O'TMISH OYNASI · SAL-2 — ARXIVDA KELAJAKDAGI KUNLAR
     * ====================================================================================== */

    private static async Task<(Group G, Teacher Orig, Teacher Sub)> SubSetupAsync(AppDbContext ctx)
    {
        var orig = new Teacher { FullName = "Asosiy" };
        var sub = new Teacher { FullName = "O'rinbosar" };
        ctx.Teachers.AddRange(orig, sub);
        var g = new Group { Name = "IELTS", TeacherId = orig.Id, Days = new List<int> { 0, 2, 4 } };
        ctx.Classes.Add(g);
        await ctx.SaveChangesAsync();
        return (g, orig, sub);
    }

    private static SubstituteTeacherAssignment Assignment(Group g, Teacher orig, Teacher sub, params string[] dates) => new()
    {
        GroupId = g.Id, OriginalTeacherId = orig.Id, SubstituteTeacherId = sub.Id, IsActive = true,
        Date = dates[0], EndDate = dates[^1], SelectedDates = dates.ToList(), CreatedBy = "Admin",
    };

    [Fact]
    public async Task SAL4__bekor_qilish_14_kundan_eski_kunlarni_OZGARTIRMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var audit = new AuditService(ctx, new NoHttpContext());
        var (g, orig, sub) = await SubSetupAsync(ctx);
        var today = new DateOnly(2026, 10, 7);   // chegara: 2026-09-23

        var old = Assignment(g, orig, sub, "2026-08-17", "2026-08-19");
        var mixed = Assignment(g, orig, sub, "2026-09-14", "2026-09-23", "2026-10-09");
        var fresh = Assignment(g, orig, sub, "2026-10-05", "2026-10-07");
        ctx.SubstituteTeacherAssignments.AddRange(old, mixed, fresh);
        await ctx.SaveChangesAsync();

        var (okOld, msgOld) = await SubstituteTeacherService.CancelAssignmentAsync(ctx, old.Id, audit, today);
        Assert.False(okOld);
        Assert.Contains("14 kundan eski", msgOld);
        Assert.True((await ctx.SubstituteTeacherAssignments.FindAsync(old.Id))!.IsActive);

        var (okMixed, _) = await SubstituteTeacherService.CancelAssignmentAsync(ctx, mixed.Id, audit, today);
        Assert.True(okMixed);
        var m = await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(x => x.Id == mixed.Id);
        Assert.True(m.IsActive);                                           // eski kun o'z kuchida
        Assert.Equal(new[] { "2026-09-14" }, m.SelectedDates!.ToArray());
        Assert.Equal("2026-09-14", m.EndDate);

        var (okFresh, _) = await SubstituteTeacherService.CancelAssignmentAsync(ctx, fresh.Id, audit, today);
        Assert.True(okFresh);
        Assert.False((await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(x => x.Id == fresh.Id)).IsActive);

        var logs = await ctx.AuditLogs.Where(x => x.EntityType == SubstituteTeacherService.AuditEntityType).ToListAsync();
        Assert.Contains(logs, l => l.Action == "update" && l.Summary.Contains("o'z kuchida qoldi"));
        Assert.Contains(logs, l => l.Action == "delete");
    }

    [Fact]
    public async Task SAL2__oqituvchi_arxivlanganda_kelajakdagi_orinbosarlik_kunlari_bekor_qilinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var audit = new AuditService(ctx, new NoHttpContext());
        var (g, orig, sub) = await SubSetupAsync(ctx);
        var today = new DateOnly(2026, 10, 7);

        var spanning = Assignment(g, orig, sub, "2026-10-05", "2026-10-07", "2026-10-09");   // 7 — bugun
        var future = Assignment(g, orig, sub, "2026-10-12");
        var past = Assignment(g, orig, sub, "2026-09-28");
        ctx.SubstituteTeacherAssignments.AddRange(spanning, future, past);
        await ctx.SaveChangesAsync();

        var changed = await SubstituteTeacherService.CancelFutureForTeacherAsync(ctx, sub.Id, audit, "o'qituvchi arxivlandi", today);
        await ctx.SaveChangesAsync();

        Assert.Equal(2, changed);
        var s = await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(x => x.Id == spanning.Id);
        Assert.True(s.IsActive);
        Assert.Equal(new[] { "2026-10-05" }, s.SelectedDates!.ToArray());
        Assert.False((await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(x => x.Id == future.Id)).IsActive);
        Assert.True((await ctx.SubstituteTeacherAssignments.AsNoTracking().SingleAsync(x => x.Id == past.Id)).IsActive);
        Assert.Equal(2, await ctx.AuditLogs.CountAsync(x => x.EntityType == SubstituteTeacherService.AuditEntityType));
    }

    [Fact]
    public void SAL2__arxivlash_faol_guruh_darvozasidan_otadi_va_tayinlovlarni_bekor_qiladi()
    {
        var src = Controller("TeachersController.cs");
        var start = src.IndexOf("public async Task<IActionResult> Archive(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = src.Substring(start, src.IndexOf("private async Task<IActionResult?> ActiveGroupsGuardAsync", start, StringComparison.Ordinal) - start);
        Assert.Contains("ActiveGroupsGuardAsync(id, \"arxivlab\")", body);
        Assert.Contains("CancelFutureForTeacherAsync", body);
        Assert.Contains("unpaidSalary", body);
        // Delete ham AYNAN shu darvozadan.
        Assert.Contains("ActiveGroupsGuardAsync(id, \"o'chirib\")", src);
        // Qaytarishda parolsiz akkaunt haqida bayroq.
        Assert.Contains("loginBlocked", src);
    }

    [Fact]
    public void Low__per_guruh_foiz_100_dan_oshsa_400()
    {
        var src = Controller("TeachersController.cs");
        var start = src.IndexOf("public async Task<IActionResult> UpdateGroupSalaries(", StringComparison.Ordinal);
        var body = src[start..];
        Assert.Contains("i.Percent is < 0 or > 100", body);
        Assert.Contains("BadRequest", body);
    }

    [Fact]
    public void Low__kun_davomatini_tozalash_turniket_va_arxiv_oqituvchiga_tegmaydi()
    {
        var src = Controller("TeacherAttendanceController.cs");
        var start = src.IndexOf("public async Task<IActionResult> SetDay(", StringComparison.Ordinal);
        var body = src[start..];
        Assert.Contains("a.Source != \"turnstile\"", body);
        Assert.Contains("activeIds.Contains(a.TeacherId)", body);
    }

    /* =========================================================================================
     *  SAL-3 — KPI KETGANLAR · SAL-5 — KETGAN XODIMNING OYI
     * ====================================================================================== */

    private static readonly DateOnly PrevFirst = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1).AddMonths(-1);
    private static string Prev(int day) => PrevFirst.AddDays(day - 1).ToString("yyyy-MM-dd");
    private static string PrevMonth => PrevFirst.ToString("yyyy-MM");

    private static void SeedRetentionRules(AppDbContext ctx) =>
        ctx.KpiRuleSets.Add(new KpiRuleSet
        {
            RoleCode = KpiConst.Retention, EffectiveFrom = "2020-01",
            Json = KpiMetricsService.SerializeRules(KpiRuleSeed.Retention()),
        });

    private static Student AddLeaver(AppDbContext ctx, Group g, string name, string status, bool activated, string leftAt)
    {
        var s = new Student { FullName = name };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = false,
            JoinedAt = PrevFirst.AddMonths(-2).ToString("yyyy-MM-dd"),
            ActivatedAt = activated ? PrevFirst.AddMonths(-2).ToString("yyyy-MM-dd") : "",
            RecordedAt = PrevFirst.AddMonths(-2).ToString("yyyy-MM-dd"),
            LeftAt = leftAt,
        });
        return s;
    }

    [Fact]
    public async Task SAL3__KPI_ketganlar__tugatish_va_sinov_kirmaydi__arxiv_sababi_jarima_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedRetentionRules(ctx);
        ctx.KpiProfiles.Add(new KpiProfile { UserId = "ret-1", RoleCode = KpiConst.Retention, StartMonth = "2020-01" });
        var oocReason = new ActionReason { Category = "archive_student", Label = "Ko'chib ketdi", OutOfControl = true };
        var ctlReason = new ActionReason { Category = "remove", Label = "Narx qimmat", OutOfControl = false };
        ctx.ActionReasons.AddRange(oocReason, ctlReason);
        var g = new Group { Name = "KPI guruh", Days = new List<int> { 0 }, StartDate = "2020-01-01" };
        ctx.Classes.Add(g);

        // 1) Sababli (nazorat ostidagi) ketish.
        var s1 = AddLeaver(ctx, g, "Sababli", "active", activated: true, leftAt: Prev(10));
        ctx.MembershipReasonLogs.Add(new MembershipReasonLog
        {
            StudentId = s1.Id, GroupId = g.Id, Action = MembershipReasons.ActionRemove, Date = Prev(10),
            ReasonId = ctlReason.Id, ReasonLabel = ctlReason.Label, CreatedAt = Prev(10) + "T10:00:00",
        });
        // 2) Sertifikat bilan TUGATGAN — ketish emas, "kursi tugagan".
        AddLeaver(ctx, g, "Tugatgan", "completed", activated: true, leftAt: Prev(12));
        // 3) Faqat SINOVda bo'lgan — ushlab qolish churn'iga kirmaydi.
        AddLeaver(ctx, g, "Sinovdagi", "trial", activated: false, leftAt: Prev(14));
        // 4) ARXIVLANGAN (o'sha kuni) — sababi arxiv sababi, nazoratdan tashqari → jarima yo'q.
        var s4 = AddLeaver(ctx, g, "Arxivlangan", "active", activated: true, leftAt: Prev(16));
        s4.IsArchived = true;
        s4.ArchivedAt = Prev(16) + "T15:30:00";
        s4.ArchiveReason = oocReason.Label;
        // 5) Sababsiz ketish — "noma'lum" (jarima).
        AddLeaver(ctx, g, "Sababsiz", "active", activated: true, leftAt: Prev(18));
        await ctx.SaveChangesAsync();

        var dto = await KpiMetricsService.MonthAsync(ctx, "ret-1", PrevMonth);
        double Input(string key) => dto.Inputs.Single(i => i.Key == key).Value;

        Assert.Equal(2, Input("leftControlled"));       // 1 (sababli) + 5 (sababsiz)
        Assert.Equal(1, Input("unknownReasonLeft"));    // faqat 5
        Assert.Equal(1, Input("courseFinished"));       // faqat 2 (tugatgan)
    }

    [Fact]
    public void SAL3__arxiv_shu_kuni_ketish_bilan_bir_kunda_bolsa_arxiv_sababi_olinadi()
    {
        var r = MembershipReasons.Resolve("2026-09-10", null, [], [],
            new MembershipReasons.ArchiveInfo(true, "2026-09-10T18:00:00", "Ko'chib ketdi"));
        Assert.Equal("Ko'chib ketdi", r.Label);
        Assert.Equal("archive", r.Source);
    }

    [Fact]
    public async Task SAL5__KPI_yopish_jadvali_ketgan_xodimning_otgan_oyini_yoqotmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        SeedRetentionRules(ctx);
        // Hozir faol.
        ctx.KpiProfiles.Add(new KpiProfile { UserId = "faol", RoleCode = KpiConst.Retention, IsActive = true });
        // Faolsizlantirilgan, lekin o'sha oy boshida faol bo'lgan (oy boshi snapshoti bor).
        ctx.KpiProfiles.Add(new KpiProfile { UserId = "ketgan", RoleCode = KpiConst.Retention, IsActive = false });
        ctx.KpiMonthSnapshots.Add(new KpiMonthSnapshot { Month = PrevMonth, RoleCode = KpiConst.Retention, UserId = "ketgan", Json = "{}" });
        // Faolsizlantirilgan va o'sha oyda yo'q edi — chiqmasligi kerak.
        ctx.KpiProfiles.Add(new KpiProfile { UserId = "eski", RoleCode = KpiConst.Retention, IsActive = false });
        // Profili o'chirilgan, lekin o'sha oy natijasi saqlangan.
        ctx.KpiMonthResults.Add(new KpiMonthResult
        {
            UserId = "ochirilgan", UserName = "O'chirilgan xodim", Month = PrevMonth, RoleCode = KpiConst.Retention,
            Status = KpiConst.MonthConfirmed, Salary = 4_500_000m, InputsJson = "[]", CoefsJson = "{}",
        });
        await ctx.SaveChangesAsync();

        var rows = await KpiMetricsService.MonthAllAsync(ctx, PrevMonth);
        var ids = rows.Select(r => r.UserId).ToHashSet();
        Assert.Contains("faol", ids);
        Assert.Contains("ketgan", ids);
        Assert.Contains("ochirilgan", ids);
        Assert.DoesNotContain("eski", ids);
        Assert.Equal(4_500_000m, rows.Single(r => r.UserId == "ochirilgan").Salary);
    }

    /* =========================================================================================
     *  QO'SHIMCHA (lead) — ARXIVDA TURGAN DAVR · O'QITUVCHI ILOVASIDAGI O'RINBOSARLIKLAR
     * ====================================================================================== */

    [Fact]
    public void InactivePeriod__arxiv_va_qaytarilgan_kun_ishlangan__bir_kunda_qaytarilsa_yozuv_yoq()
    {
        Assert.Equal("2026-02-11|2026-03-31", SalaryLedger.InactivePeriodFor("2026-02-10", new DateOnly(2026, 4, 1)));
        Assert.Null(SalaryLedger.InactivePeriodFor("2026-02-10", new DateOnly(2026, 2, 10)));
        Assert.Null(SalaryLedger.InactivePeriodFor("2026-02-10", new DateOnly(2026, 2, 11)));
        Assert.Null(SalaryLedger.InactivePeriodFor(null, new DateOnly(2026, 2, 11)));

        var spans = SalaryLedger.InactiveSpans(new Teacher { InactivePeriods = ["2026-02-11|2026-03-31", "buzuq"] });
        Assert.Single(spans);
        Assert.Equal(10m / 28m, SalaryLedger.MonthFraction("2026-02", null, null, spans));
        Assert.Equal(0m, SalaryLedger.MonthFraction("2026-03", null, null, spans));
        Assert.Equal(1m, SalaryLedger.MonthFraction("2026-04", null, null, spans));
    }

    [Fact]
    public async Task Arxivdan_qaytarilgan_oqituvchining_arxivdagi_oylari_HISOBLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // 10-fevralda arxivlangan, 1-aprelda qaytarilgan (ArchivedAt tozalangan).
        var t = new Teacher
        {
            FullName = "Qaytgan", SalaryMode = "fixed", Salary = 3_100_000m, SalaryStartDate = "2026-01-01",
            InactivePeriods = ["2026-02-11|2026-03-31"],
        };
        ctx.Teachers.Add(t);
        await ctx.SaveChangesAsync();

        var l = await SalaryLedger.BuildAsync(ctx, t, "2026-01", "2026-04");
        decimal M(string m) => l.Months.Single(x => x.Month == m).Expected;
        Assert.Equal(3_100_000m, M("2026-01"));
        Assert.Equal(1_107_142.86m, M("2026-02"));   // 10/28
        Assert.Equal(0m, M("2026-03"));
        Assert.Equal(3_100_000m, M("2026-04"));
    }

    [Fact]
    public async Task Arxivdagi_kunlar_FOIZLI_guruh_ulushiga_ham_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, _, _, _) = SetupTeacherChange(ctx, withHistory: false);   // A — yagona ega, iyunda 5 dars
        a.InactivePeriods = ["2026-06-09|2026-06-30"];                    // 15, 22, 29 — arxivda
        await ctx.SaveChangesAsync();

        var june = (await SalaryLedger.BuildAsync(ctx, a, "2026-06", "2026-06")).Months.Single();
        Assert.Equal(200_000m, june.Expected);   // 500 000 × 2/5

        // HOZIR arxivda (8-iyunda arxivlangan) — arxiv kuni ishlangan, keyingi darslar yo'q.
        a.InactivePeriods = [];
        a.IsArchived = true;
        a.ArchivedAt = "2026-06-08";
        await ctx.SaveChangesAsync();
        var june2 = (await SalaryLedger.BuildAsync(ctx, a, "2026-06", "2026-06")).Months.Single();
        Assert.Equal(200_000m, june2.Expected);
    }

    [Fact]
    public async Task Oqituvchi_ilovasi__orinbosarliklar_QAMRALGAN_SANA_egasiga_korinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (a, b, g, _) = SetupTeacherChange(ctx);
        var sub = new Teacher { FullName = "O'rinbosar", SalaryMode = "percent", SalaryPercent = 50 };
        ctx.Teachers.Add(sub);
        ctx.SubstituteTeacherAssignments.Add(new SubstituteTeacherAssignment
        {
            GroupId = g.Id, OriginalTeacherId = a.Id, SubstituteTeacherId = sub.Id,
            Date = "2026-06-22", EndDate = "2026-06-22", SelectedDates = new List<string> { "2026-06-22" },
            IsActive = true, CreatedBy = "Admin",
        });
        await ctx.SaveChangesAsync();

        var forB = await SubstituteTeacherService.ForTeacherAsync(ctx, b);
        var row = Assert.Single(forB);
        Assert.Equal(b.Id, row.OriginalTeacherId);
        Assert.Equal(b.FullName, row.OriginalTeacherName);
        Assert.Empty(await SubstituteTeacherService.ForTeacherAsync(ctx, a));   // saqlangan "asosiy", lekin 22-iyun B niki
        Assert.Single(await SubstituteTeacherService.ForTeacherAsync(ctx, sub));

        var src = Controller("TeacherPortalController.cs");
        var start = src.IndexOf("public async Task<IActionResult> MySubstitutions()", StringComparison.Ordinal);
        Assert.Contains("SubstituteTeacherService.ForTeacherAsync(db, t, activeOnly: true)", src.Substring(start, 1500));
    }

    [Fact]
    public void Arxivdan_qaytarish_davrni_InactivePeriods_ga_yozadi()
    {
        var src = Controller("TeachersController.cs");
        var start = src.IndexOf("public async Task<IActionResult> Restore(", StringComparison.Ordinal);
        var body = src.Substring(start, 1800);
        Assert.Contains("InactivePeriodFor(teacher.ArchivedAt, AppClock.Today)", body);
        Assert.True(body.IndexOf("InactivePeriods", StringComparison.Ordinal)
                    < body.IndexOf("teacher.ArchivedAt = null", StringComparison.Ordinal),
            "Davr ArchivedAt tozalanishidan OLDIN yozilishi shart");
    }
}
