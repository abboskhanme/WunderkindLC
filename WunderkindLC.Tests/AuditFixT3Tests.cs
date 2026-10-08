using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-07 audit tuzatishlari — TO'LOVLAR, OYLIK HISOB, CHEGIRMALAR, KASSALAR:
/// vozvrat tahriri balansni buzmasligi (C7), kelmagan darslar chegirmasi qulflagan oyning muzlatishda
/// qisqarishi (FIN-1), sinovdagi o'quvchi to'lovi (FIN-2), avans bekor qilinganda kelgusi oy hisobi (FIN-4),
/// hisob faqat accrual yozadigan oyga (FIN-5), kelgusi oylarni qayta narxlash (FIN-6), bo'lingan to'lov
/// bog'lanishi (FIN-7), tushum rejasida vozvrat (FIN-9), oy formati va fon accrual'ining balans poygasi.
/// Sanalar <see cref="AppClock.Today"/> ga NISBATAN.
/// </summary>
public class AuditFixT3Tests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private const decimal Fee = 300_000m;
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <summary>Kurs (bir dars 25 000) + har kuni darsli guruh + aktiv o'quvchi (M(-3) dan).</summary>
    private static (Student S, Group G) Setup(AppDbContext ctx, string status = "active", string? activatedAt = null)
    {
        var course = new Subject { Name = "Ingliz tili", Price = Fee, LessonPrice = 25_000m };
        ctx.Subjects.Add(course);
        var g = new Group { Name = "A guruh", MonthlyFee = Fee, CourseId = course.Id, Days = [0, 1, 2, 3, 4, 5, 6] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{M(-3)}-01", ClassName = g.Name };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = status, IsActive = true,
            JoinedAt = $"{M(-3)}-01", ActivatedAt = status == "trial" ? "" : activatedAt ?? $"{M(-3)}-01",
        });
        return (s, g);
    }

    private static Task<PaymentIntakeResult> Pay(AppDbContext ctx, Student s, string? groupId, string month, decimal amount = Fee) =>
        PaymentIntake.AddAsync(ctx, new AuditService(ctx, new NoHttpContext()), new MessagingStack().Auto, s,
            new PaymentRequest(amount, month, groupId, Method: "cash", RequestId: Guid.NewGuid().ToString()),
            createdBy: "Kassir", createdById: "kassir-1");

    private static async Task<decimal> Balance(AppDbContext ctx, Student s) =>
        (await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.Id)).Balance;

    private static Task<MonthlyCharge?> ChargeOf(AppDbContext ctx, Student s, string? groupId, string month) =>
        ctx.MonthlyCharges.AsNoTracking().FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == month);

    // ============================ Oy formati (FIN low b) ============================

    [Theory]
    [InlineData("2026-10", true)]
    [InlineData("2026-01", true)]
    [InlineData("2026-1", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-00", false)]
    [InlineData("2026-10-15", false)]
    [InlineData("abcd-ef", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Oy_AYNAN_yyyy_MM_bolishi_shart(string? month, bool ok) =>
        Assert.Equal(ok, PaymentFields.IsMonth(month));

    [Fact]
    public async Task Tolov_TOLIQ_SANA_bilan_oy_sifatida_RAD_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, g.Id, $"{M(0)}-15");

        Assert.NotNull(res.Error);
        Assert.Empty(await ctx.FinanceTransactions.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
        Assert.Equal(0m, await Balance(ctx, s));
    }

    // ============================ C7 — umumiy tahrir ============================

    private static FinanceTransaction RefundRow() => new()
    {
        Date = $"{M(0)}-05", Direction = "expense", Category = "refund", Amount = 200_000m,
        StudentId = "s1", GroupId = "g1", Month = M(0), RefundOfId = "pay-1", Comment = "kasal", CashboxId = "main",
    };

    private static FinanceTransactionPayload Payload(FinanceTransaction t, decimal? amount = null, string? category = null,
                                                     string? date = null, string? typeId = null, string? studentId = null,
                                                     string? teacherId = null, string? note = null) =>
        new(date ?? t.Date, t.Direction, category ?? t.Category, amount ?? t.Amount, note ?? t.Note,
            studentId, teacherId, TypeId: typeId);

    [Fact]
    public void Vozvrat_tahriri_OQUVCHI_va_SUMMANI_saqlaydi__balans_tasiri_ozgarmaydi()
    {
        // Forma vozvratda o'quvchini yubormas edi — server StudentId'ni bo'shatib, qaytarilgan pulni
        // o'quvchi balansiga QAYTARIB berardi; turi esa forma zaxirasi bilan almashardi.
        var tx = RefundRow();
        var effectBefore = FinanceTxEdit.StudentBalanceEffect(tx);
        var p = Payload(tx, date: $"{M(0)}-06", typeId: "arenda-turi", note: "tuzatildi");

        Assert.Null(FinanceTxEdit.EditError(tx, p, AppClock.Today.AddMonths(1).ToString("yyyy-MM-dd")));
        var changes = FinanceTxEdit.ApplyRefundSafe(tx, p);

        Assert.Equal("s1", tx.StudentId);
        Assert.Equal(200_000m, tx.Amount);
        Assert.Equal("refund", tx.Category);
        Assert.Null(tx.TypeId);
        Assert.Equal("g1", tx.GroupId);
        Assert.Equal($"{M(0)}-06", tx.Date);
        Assert.Equal("tuzatildi", tx.Note);
        Assert.Equal(effectBefore, FinanceTxEdit.StudentBalanceEffect(tx));
        Assert.Contains(changes, c => c.StartsWith("sana"));
    }

    [Fact]
    public void Vozvrat_summasi_yoki_turi_umumiy_tahrirda_RAD_etiladi()
    {
        var tx = RefundRow();
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        Assert.Equal(FinanceTxEdit.RefundLockedMessage, FinanceTxEdit.EditError(tx, Payload(tx, amount: 150_000m), today));
        Assert.Equal(FinanceTxEdit.RefundLockedMessage, FinanceTxEdit.EditError(tx, Payload(tx, category: "rent"), today));
        Assert.NotNull(FinanceTxEdit.EditError(tx, Payload(tx, date: AppClock.Today.AddDays(3).ToString("yyyy-MM-dd")), today));
    }

    [Fact]
    public void Oquvchi_tolovi_umumiy_tahrirdan_OTMAYDI__oddiy_qatorni_vozvratga_aylantirib_bolmaydi()
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var pay = new FinanceTransaction { Date = today, Direction = "income", Category = "tuition", Amount = Fee, StudentId = "s1" };
        Assert.Equal(FinanceTxEdit.StudentPaymentMessage, FinanceTxEdit.EditError(pay, Payload(pay, studentId: "s1"), today));

        var rent = new FinanceTransaction { Date = today, Direction = "expense", Category = "rent", Amount = 1_000_000m };
        Assert.NotNull(FinanceTxEdit.EditError(rent, Payload(rent, category: "refund"), today));
    }

    [Fact]
    public void Oddiy_tahrir_forma_yubormagan_TUR_va_OQITUVCHINI_saqlaydi()
    {
        // DTO ilgari TypeId qaytarmas edi — har tahrir turni forma zaxirasi bilan almashtirardi.
        var salary = new FinanceTransaction
        {
            Date = $"{M(0)}-05", Direction = "expense", Category = "salary", Amount = 2_000_000m,
            TeacherId = "t1", TypeId = "maosh-turi", Month = M(-1),
        };
        FinanceTxEdit.ApplyGeneric(salary, Payload(salary, amount: 2_100_000m));
        Assert.Equal("t1", salary.TeacherId);
        Assert.Equal("maosh-turi", salary.TypeId);
        Assert.Equal(M(-1), salary.Month);
        Assert.Equal(2_100_000m, salary.Amount);

        // Toifa almashsa eski bog'lanishlar ergashmaydi.
        FinanceTxEdit.ApplyGeneric(salary, Payload(salary, category: "rent"));
        Assert.Null(salary.TeacherId);
        Assert.Null(salary.TypeId);
    }

    // ============================ FIN-1 — kelmagan darslar chegirmasi + muzlatish ============================

    [Fact]
    public async Task Chegirma_qulflagan_oy_MUZLATISHDA_qisqaradi_chegirma_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = Fee, Date = $"{M(0)}-01" });
        s.Balance = -Fee;
        await ctx.SaveChangesAsync();
        await LessonDiscountService.ApplyAsync(ctx, s, g, M(0), 2, "kasal", "u1", "Kassir"); // −50 000, qulf
        await ctx.SaveChangesAsync();

        // 10-kungacha (10 dars × 25 000) o'qidi — ilgari qulfli qator TO'LIQ oylikda qolardi.
        await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(-3)}-01", $"{M(0)}-10");
        await ctx.SaveChangesAsync();

        var c = await ChargeOf(ctx, s, g.Id, M(0));
        Assert.Equal(250_000m, c!.Amount);
        Assert.Equal(50_000m, c.Discount);   // kelmagan darslar chegirmasi qayta qo'shildi
        Assert.True(c.Locked);
        Assert.Equal(-200_000m, await Balance(ctx, s));
    }

    [Fact]
    public async Task QOLDA_tahrirlangan_qulfli_oy_muzlatishda_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 280_000m, Date = $"{M(0)}-01", Locked = true });
        s.Balance = -280_000m;
        await ctx.SaveChangesAsync();

        await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(-3)}-01", $"{M(0)}-10");
        await ctx.SaveChangesAsync();

        Assert.Equal(280_000m, (await ChargeOf(ctx, s, g.Id, M(0)))!.Amount);
        Assert.Equal(-280_000m, await Balance(ctx, s));
    }

    [Fact]
    public async Task Chegirmali_KELGUSI_oy_hisobi_muzlatishda_BEKOR_qilinadi_chegirma_yopiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        // Kelgusi oy: hisob avans kabi ochiladi (−300 000), chegirma −50 000 → balans −250 000.
        await LessonDiscountService.ApplyAsync(ctx, s, g, M(1), 2, "safar", "u1", "Kassir");
        await ctx.SaveChangesAsync();
        Assert.Equal(-250_000m, await Balance(ctx, s));

        await MembershipBilling.SettleFreezeAsync(ctx, s, g, $"{M(-3)}-01", $"{M(0)}-10");
        await ctx.SaveChangesAsync();

        Assert.Null(await ChargeOf(ctx, s, g.Id, M(1)));
        Assert.NotNull((await ctx.LessonDiscounts.AsNoTracking().SingleAsync()).CancelledAt);
        Assert.Equal(0m, await LessonDiscountService.ActiveAmountAsync(ctx, s.Id, g.Id, M(1)));
        // Kelgusi oy (−250 000) to'liq qaytdi; qolgani — muzlatish oyining o'qilgan 10 darsi (−250 000).
        Assert.Equal(250_000m, (await ChargeOf(ctx, s, g.Id, M(0)))!.Amount);
        Assert.Equal(-250_000m, await Balance(ctx, s));
    }

    [Fact]
    public void Qulf_sababi__chegirmadan_OLDIN_qulfli_bolsa_qolda_tahrir()
    {
        var a = new LessonDiscount { Amount = 25_000m, CreatedAt = "2026-10-01T10:00:00", WasLocked = false };
        var b = new LessonDiscount { Amount = 50_000m, CreatedAt = "2026-10-02T10:00:00", WasLocked = true };
        Assert.Equal(75_000m, LessonDiscountService.LockExtra([a, b]));
        Assert.Null(LessonDiscountService.LockExtra([new LessonDiscount { Amount = 1, CreatedAt = "1", WasLocked = true }]));
        Assert.Null(LessonDiscountService.LockExtra([new LessonDiscount { Amount = 1, CreatedAt = "1", CancelledAt = "x" }]));
        Assert.Null(LessonDiscountService.LockExtra([]));
    }

    // ============================ FIN-2 / FIN-5 — sinov va hisob sharti ============================

    [Fact]
    public async Task SINOVDAGI_oquvchi_tolovi_guruhga_AVANS_bolib_teglanadi_hisob_OCHILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx, status: "trial");
        await ctx.SaveChangesAsync();

        var res = await Pay(ctx, s, null, M(0));

        Assert.Null(res.Error);
        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(g.Id, tx.GroupId);
        Assert.Equal(M(0), tx.Month);
        Assert.Empty(await ctx.MonthlyCharges.AsNoTracking().ToListAsync());
        Assert.Equal(Fee, await Balance(ctx, s));
    }

    [Fact]
    public void Guruh_tanlash__sinov_ham_sanaladi__eski_guruhga_JIMGINA_tushmaydi()
    {
        var mixed = new List<(string, string, bool)> { ("old", "active", false), ("trial", "trial", true) };
        Assert.NotNull(PaymentIntake.ResolveGroup(mixed, null).Error);             // noaniq — tanlash shart
        Assert.Equal("trial", PaymentIntake.ResolveGroup(mixed, "trial").GroupId); // yuborilgani bajariladi
        Assert.Equal("old", PaymentIntake.ResolveGroup(mixed, "old").GroupId);
        Assert.NotNull(PaymentIntake.ResolveGroup(mixed, "begona").Error);

        // Tugagan (faol bo'lmagan) sinov nomzod emas.
        var endedTrial = new List<(string, string, bool)> { ("a", "active", true), ("t", "trial", false) };
        Assert.Equal("a", PaymentIntake.ResolveGroup(endedTrial, null).GroupId);
        // A'zoligi yo'q eski o'quvchi — guruhsiz, yuborilgan guruh e'tiborsiz.
        Assert.Equal((null, null), PaymentIntake.ResolveGroup([], "x"));
    }

    [Fact]
    public async Task EnsureCharge_sinov_va_aktivlashtirish_OYIGA_toliq_hisob_ochmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (trialStudent, g) = Setup(ctx, status: "trial");
        var late = new Student { FullName = "Kech", EnrollmentDate = $"{M(0)}-01" };
        ctx.Students.Add(late);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = late.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{M(0)}-01", ActivatedAt = $"{M(0)}-28",
        });
        await ctx.SaveChangesAsync();

        Assert.False(await TuitionService.EnsureChargeAsync(ctx, trialStudent, g.Id, M(0)));
        // Aktivlashtirish oyi — TO'LIQ emas, faqat QISMAN hisob (28-kundan). audit R1-5: ilgari umuman ochilmasdi va
        // shu oyga to'lov avans bo'lib keyingi oyni yopardi (bir oy tekin) — `AuditFixFx2Tests`.
        Assert.True(await TuitionService.EnsureChargeAsync(ctx, late, g.Id, M(0)));
        var partial = ctx.MonthlyCharges.Local.Single(c => c.StudentId == late.Id && c.Month == M(0));
        Assert.Equal((DateTime.DaysInMonth(AppClock.Today.Year, AppClock.Today.Month) - 27) * 25_000m, partial.Amount);
        Assert.True(partial.Amount < Fee);
        Assert.True(await TuitionService.EnsureChargeAsync(ctx, late, g.Id, M(1)));    // keyingi oy — odatdagi avans
    }

    // ============================ FIN-4 — avans bekor qilinsa ============================

    [Fact]
    public async Task Avans_BEKOR_qilinsa_kelgusi_oy_hisobi_ham_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var res = await Pay(ctx, s, g.Id, M(1));
        Assert.NotNull(await ChargeOf(ctx, s, g.Id, M(1)));
        Assert.Equal(0m, await Balance(ctx, s));

        // FinanceController.Delete bilan bir xil: bekor qilish + balans + yetim avans hisobi.
        var tx = await ctx.FinanceTransactions.SingleAsync(t => t.Id == res.TxId);
        tx.IsVoided = true;
        s.Balance -= tx.Amount;
        Assert.True(await TuitionService.DropOrphanAdvanceChargeAsync(ctx, s, g.Id, M(1), ignoreTxIds: [tx.Id]));
        await ctx.SaveChangesAsync();

        Assert.Null(await ChargeOf(ctx, s, g.Id, M(1)));
        Assert.Equal(0m, await Balance(ctx, s)); // yolg'on qarz yo'q
    }

    [Fact]
    public async Task Avans_hisobi_BOSHQA_tolov_qolsa_va_JORIY_oyda_TEGILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        await ctx.SaveChangesAsync();
        var first = await Pay(ctx, s, g.Id, M(1), 100_000m);
        await ctx.FinanceTransactions.ExecuteUpdateAsync(x => x.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-1)));
        await Pay(ctx, s, g.Id, M(1), 200_000m);

        Assert.False(await TuitionService.DropOrphanAdvanceChargeAsync(ctx, s, g.Id, M(1), ignoreTxIds: [first.TxId!]));
        Assert.False(await TuitionService.DropOrphanAdvanceChargeAsync(ctx, s, g.Id, M(0), ignoreTxIds: [first.TxId!]));
        Assert.NotNull(await ChargeOf(ctx, s, g.Id, M(1)));
    }

    // ============================ FIN-6 / ADM-7 — kelgusi oylar narxi ============================

    [Fact]
    public async Task Guruh_narxi_ozgarsa_KELGUSI_oldindan_tolangan_oy_ham_qayta_hisoblanadi_va_TARIXGA_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        foreach (var m in new[] { M(-1), M(0), M(1) })
            ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = m, Amount = Fee, Date = $"{m}-01" });
        s.Balance = -3 * Fee;
        await ctx.SaveChangesAsync();
        var audit = new AuditService(ctx, new NoHttpContext());

        var applied = await TuitionService.ApplyGroupFeeToCurrentMonthAsync(ctx, g.Id, g.Name, 400_000m, oldFee: Fee, audit: audit);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, applied);                                            // o'quvchilar soni
        Assert.Equal(Fee, (await ChargeOf(ctx, s, g.Id, M(-1)))!.Amount);    // o'tgan oy — tarix
        Assert.Equal(400_000m, (await ChargeOf(ctx, s, g.Id, M(0)))!.Amount);
        Assert.Equal(400_000m, (await ChargeOf(ctx, s, g.Id, M(1)))!.Amount);
        Assert.Equal(-Fee - 800_000m, await Balance(ctx, s));
        var row = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.StudentId == s.Id);
        Assert.Equal(AuditService.EntityStudentDiscount, row.EntityType);
        Assert.Contains(M(1), row.Summary);
    }

    [Fact]
    public async Task Chegirma_berilsa_KELGUSI_avans_oyiga_ham_qollanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = M(1), Amount = Fee, Date = $"{M(1)}-01" });
        s.Balance = -Fee;
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, StudentName = s.FullName, Pct = 20, Status = StudentDiscount.StatusActive,
            CreatedAt = "2026-01-01T00:00:00",
        });
        await ctx.SaveChangesAsync();

        Assert.False(await StudentDiscountService.ReapplyCurrentMonthAsync(ctx, s)); // joriy oy hisobi yo'q
        Assert.Equal(1, await StudentDiscountService.ReapplyFutureMonthsAsync(ctx, s));
        await ctx.SaveChangesAsync();

        Assert.Equal(60_000m, (await ChargeOf(ctx, s, g.Id, M(1)))!.Discount);
        Assert.Equal(-240_000m, await Balance(ctx, s));
    }

    // ============================ FIN-7 — bo'lingan to'lov ============================

    [Fact]
    public async Task Guruh_almashtirishda_BOLINGAN_qator_asl_tolovga_BOGLANADI_va_maydonlarini_oladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, a) = Setup(ctx);
        var b = new Group { Name = "B guruh", MonthlyFee = Fee, Days = [0, 2, 4] };
        ctx.Classes.Add(b);
        ctx.Cashboxes.Add(new Cashbox { Id = "k1", Name = "Kassa 1" });
        ctx.MonthlyCharges.Add(new MonthlyCharge { StudentId = s.Id, GroupId = a.Id, Month = M(0), Amount = 100_000m, Date = $"{M(0)}-01" });
        var pay = new FinanceTransaction
        {
            Date = $"{M(0)}-02", Direction = "income", Category = "tuition", Amount = Fee, StudentId = s.Id,
            GroupId = a.Id, Month = M(0), Method = "card", CardLast4 = "1234", PaidTime = "10:15", ReceiptNo = "KV77",
            CreatedBy = "Kassir", CreatedById = "kassir-1", CashboxId = "k1",
        };
        ctx.FinanceTransactions.Add(pay);
        await ctx.SaveChangesAsync();

        var moved = await TuitionService.CarryGroupAdvanceAsync(ctx, s, a, b, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(200_000m, moved);
        var child = await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Id != pay.Id);
        Assert.Equal(pay.Id, child.SplitFromId);
        Assert.Equal(b.Id, child.GroupId);
        Assert.Equal(200_000m, child.Amount);
        Assert.Equal("kassir-1", child.CreatedById);
        Assert.Equal("KV77", child.ReceiptNo);
        Assert.Equal("1234", child.CardLast4);
        Assert.Equal("card", child.Method);
        Assert.Equal("k1", child.CashboxId);
        Assert.Equal(100_000m, (await ctx.FinanceTransactions.AsNoTracking().SingleAsync(t => t.Id == pay.Id)).Amount);

        // Oila: istalgan a'zosidan butun oila topiladi (bekor qilish birga).
        var tracked = await ctx.FinanceTransactions.SingleAsync(t => t.Id == child.Id);
        Assert.Equal(2, (await PaymentSplits.FamilyAsync(ctx, tracked)).Count);
        // Kvitansiya: asl to'lovni tahrirlashda o'z oilasi dublikat emas; yangi to'lov uchun — band.
        Assert.Null(await ReceiptGuard.FindDuplicateAsync(ctx, "KV77", excludeTxId: pay.Id));
        Assert.NotNull(await ReceiptGuard.FindDuplicateAsync(ctx, "KV77"));
    }

    // ============================ FIN-9 — tushum rejasida vozvrat ============================

    [Fact]
    public async Task Tushum_rejasi__Shu_oyda_tolagan_summa_VOZVRATNI_ayiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Setup(ctx);
        var pay = new FinanceTransaction
        {
            Date = $"{M(0)}-03", Direction = "income", Category = "tuition", Amount = Fee, StudentId = s.Id, GroupId = g.Id, Month = M(0),
        };
        ctx.FinanceTransactions.Add(pay);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-04", Direction = "expense", Category = "refund", Amount = 100_000m, StudentId = s.Id,
            GroupId = g.Id, Month = M(0), RefundOfId = pay.Id,
        });
        s.Balance = 200_000m;
        await ctx.SaveChangesAsync();

        var plan = await IncomePlan.BuildAsync(ctx, M(0), "reja");

        Assert.Equal(200_000m, plan.Rows.Single(r => r.Label == IncomePlan.LabelPaid).Amount);
    }

    // ============================ FIN low (a) — fon accrual balans poygasi ============================

    [Fact]
    public async Task AccrueMonth_ESKI_nusxadagi_balans_bilan_parallel_tolov_tasirini_YOQOTMAYDI()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var ctxA = new AppDbContext(options);
        ctxA.Database.EnsureCreated();
        var (seeded, g) = Setup(ctxA);
        await ctxA.SaveChangesAsync();
        ctxA.ChangeTracker.Clear();
        _ = await ctxA.Students.FirstAsync(x => x.Id == seeded.Id); // fon xizmati: eski nusxa (balans 0)

        // Shu orada boshqa so'rov to'lov kiritdi (+100 000).
        using (var ctxB = new AppDbContext(options))
        {
            var sB = await ctxB.Students.FirstAsync(x => x.Id == seeded.Id);
            sB.Balance += 100_000m;
            await ctxB.SaveChangesAsync();
        }

        var (count, _, _) = await TuitionService.AccrueMonth(ctxA, M(0));

        Assert.Equal(1, count);
        using var check = new AppDbContext(options);
        Assert.Equal(100_000m - Fee, (await check.Students.AsNoTracking().SingleAsync(x => x.Id == seeded.Id)).Balance);
        Assert.Equal(g.Id, (await check.MonthlyCharges.AsNoTracking().SingleAsync()).GroupId);
    }
}
