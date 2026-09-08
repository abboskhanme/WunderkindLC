using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// MOLIYA / BILLING mantig'ining BAZA bilan ishlaydigan testlari: oylik hisoblash (accrual),
/// aktivlashtirish qisman to'lovi, hisoblarni bekor qilish, o'quvchi ledgeri va per-guruh balans.
///
/// <para>Har testda izolyatsiyalangan SQLite baza (<see cref="TestDb.Sqlite"/>) ishlatiladi.
/// Sanalar MUTLAQ yozilmaydi — <see cref="AppClock.Today"/> ga NISBATAN quriladi, aks holda test
/// kelasi oyda yiqilardi.</para>
///
/// <para><c>[Fact(Skip=...)]</c> testlar TASDIQLANGAN xatolarni hujjatlashtiradi: ular KUTILGAN
/// (to'g'ri) xulqni yozadi, production kodi hozircha boshqacha ishlaydi.</para>
/// </summary>
public class FinanceDbTests
{
    // ==================== yordamchilar ====================

    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    /// <param name="pct">Chegirma foizi. ⚠️ Endi u <b>REGISTRGA</b> yoziladi (pul manbai o'sha),
    /// <c>Student.Discount*</c> esa faqat KO'ZGU — <c>StudentDiscountService.RefreshMirrorAsync</c>
    /// bilan qo'yiladi. Kutilgan NATIJALAR o'zgarmagan (`.claude/rules/discounts.md` §1).</param>
    /// <param name="discountGroupId">Chegirma QAMROVI: null — «barcha guruhlar».</param>
    private static Student AddStudent(
        AppDbContext ctx, string className = "", bool archived = false,
        int pct = 0, decimal amount = 0m, string? discountGroupId = null)
    {
        var s = new Student
        {
            FullName = "Test O'quvchi",
            ClassName = className,
            EnrollmentDate = $"{M(-6)}-01",
            IsArchived = archived,
        };
        ctx.Students.Add(s);
        if (pct > 0 || amount > 0m)
        {
            ctx.StudentDiscounts.Add(new StudentDiscount
            {
                StudentId = s.Id, StudentName = s.FullName, GroupId = discountGroupId,
                Pct = pct, Amount = amount,
                Status = StudentDiscount.StatusActive, CreatedAt = "2020-01-01T00:00:00",
            });
            // Ko'zgu — registr bilan bir xil bo'lishi kerak (invariant).
            s.DiscountPct = pct;
            s.DiscountAmount = amount;
            s.DiscountGroupId = discountGroupId;
        }
        return s;
    }

    private static Group AddGroup(
        AppDbContext ctx, decimal fee, string name = "A guruh", string courseId = "",
        List<int>? days = null)
    {
        var g = new Group
        {
            Name = name,
            MonthlyFee = fee,
            CourseId = courseId,
            Days = days ?? new List<int> { 0, 1, 2, 3, 4, 5, 6 },
        };
        ctx.Classes.Add(g);
        return g;
    }

    private static StudentGroup AddMembership(
        AppDbContext ctx, Student s, Group g, string status = "active",
        string? activatedAt = null, string frozenAt = "", bool isActive = true)
    {
        var m = new StudentGroup
        {
            StudentId = s.Id,
            GroupId = g.Id,
            Status = status,
            IsActive = isActive,
            JoinedAt = $"{M(-6)}-01",
            ActivatedAt = activatedAt ?? $"{M(-6)}-01",
            FrozenAt = frozenAt,
        };
        ctx.StudentGroups.Add(m);
        return m;
    }

    private static MonthlyCharge AddCharge(
        AppDbContext ctx, Student s, string? groupId, string month,
        decimal amount, decimal discount = 0m, bool locked = false)
    {
        var c = new MonthlyCharge
        {
            StudentId = s.Id, GroupId = groupId, Month = month,
            Amount = amount, Discount = discount, Date = $"{month}-01", Locked = locked,
        };
        ctx.MonthlyCharges.Add(c);
        return c;
    }

    private static FinanceTransaction AddPayment(
        AppDbContext ctx, Student s, string? groupId, string month, decimal amount,
        string direction = "income", string category = "tuition")
    {
        var t = new FinanceTransaction
        {
            Date = $"{month}-10", Direction = direction, Category = category, Amount = amount,
            StudentId = s.Id, GroupId = groupId, Month = month, Method = "cash",
        };
        ctx.FinanceTransactions.Add(t);
        return t;
    }

    // ==================== AccrueMonth ====================

    [Fact]
    public async Task AccrueMonth_aktivlashtirilgan_OYNING_OZI_otkaziladi()
    {
        // Aktivlashtirish oyi QISMAN hisob bilan alohida yoziladi (ChargeActivationProrateAsync),
        // shuning uchun AccrueMonth uni to'liq oylik bilan takrorlamasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, activatedAt: $"{M(-1)}-05");
        await ctx.SaveChangesAsync();

        var aktivOy = await TuitionService.AccrueMonth(ctx, M(-1));
        Assert.Equal(0, aktivOy.Count);
        Assert.Empty(ctx.MonthlyCharges);

        var keyingiOy = await TuitionService.AccrueMonth(ctx, M(0));
        Assert.Equal(1, keyingiOy.Count);
        Assert.Equal(600_000m, keyingiOy.Total);
        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(g.Id, row.GroupId);
        Assert.Equal(600_000m, row.Amount);
        Assert.Equal(-600_000m, s.Balance);
    }

    [Fact]
    public async Task AccrueMonth_muzlatish_oyi_va_undan_keyingi_oylar_hisoblanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, activatedAt: $"{M(-3)}-01", frozenAt: $"{M(-1)}-10");
        await ctx.SaveChangesAsync();

        // Aktiv va muzlatish oralig'idagi oy — to'liq hisoblanadi.
        Assert.Equal(1, (await TuitionService.AccrueMonth(ctx, M(-2))).Count);
        // Muzlatish OYINING O'ZI — qisman hisob alohida yoziladi, to'liq oylik yozilmaydi.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-1))).Count);
        // Muzlatishdan keyingi oylar — umuman yo'q.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);

        Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(-600_000m, s.Balance);
    }

    [Fact]
    public async Task AccrueMonth_IDEMPOTENT_ikkinchi_chaqiriq_nol()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(1, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);

        Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(-600_000m, s.Balance);   // balans IKKI marta kamaymaydi
    }

    [Fact]
    public async Task AccrueMonth_ikki_faol_guruh_ikki_qator()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g1 = AddGroup(ctx, 600_000m, "Ingliz A");
        var g2 = AddGroup(ctx, 200_000m, "Matematika B");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g1, activatedAt: $"{M(-2)}-01");
        AddMembership(ctx, s, g2, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var natija = await TuitionService.AccrueMonth(ctx, M(0));
        Assert.Equal(2, natija.Count);
        Assert.Equal(800_000m, natija.Total);
        Assert.Equal(2, ctx.MonthlyCharges.Count());
        Assert.Equal(-800_000m, s.Balance);
        // Har guruh uchun ALOHIDA qator (per-guruh billing).
        Assert.Equal(new[] { g1.Id, g2.Id }.OrderBy(x => x),
            ctx.MonthlyCharges.Select(c => c.GroupId!).ToList().OrderBy(x => x));
    }

    [Fact]
    public async Task AccrueMonth_IsActive_false_bolsa_qator_yozilmaydi()
    {
        // Guruhdan chiqarilgan a'zolikda Status "active" bo'lib qolishi mumkin — IsActive ham talab qilinadi,
        // aks holda chiqib ketgan o'quvchiga har oy qarz yozilardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, status: "active", activatedAt: $"{M(-2)}-01", isActive: false);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Empty(ctx.MonthlyCharges);
        Assert.Equal(0m, s.Balance);
    }

    [Fact]
    public async Task AccrueMonth_SINOV_azoligiga_hisoblanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, status: "trial", activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Empty(ctx.MonthlyCharges);
    }

    [Fact]
    public async Task AccrueMonth_arxivlangan_oquvchiga_hisoblanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx, archived: true);
        AddMembership(ctx, s, g, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(0))).Count);
        Assert.Empty(ctx.MonthlyCharges);
    }

    [Fact]
    public async Task AccrueMonth_chegirma_qollanadi_balans_effektiv_kamayadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx, pct: 25);          // 25% chegirma
        AddMembership(ctx, s, g, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var natija = await TuitionService.AccrueMonth(ctx, M(0));
        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(600_000m, row.Amount);        // to'liq narx saqlanadi
        Assert.Equal(150_000m, row.Discount);      // chegirma ALOHIDA
        Assert.Equal(450_000m, natija.Total);
        Assert.Equal(-450_000m, s.Balance);        // balansdan faqat effektiv summa yechiladi
    }

    [Fact]
    public async Task AccrueMonth_HAR_FAN_uchun_OZ_chegirmasi__qoshilmaydi()
    {
        // ⚠️ Ikki fan, ikki alohida chegirma. Ular QO'SHILMASLIGI kerak: har hisob qatoriga
        // faqat O'ZINING chegirmasi tushadi (`.claude/rules/discounts.md` §1).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var mat = AddGroup(ctx, 600_000m, "Matematika A");
        var eng = AddGroup(ctx, 400_000m, "Ingliz A1");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, mat, activatedAt: $"{M(-2)}-01");
        AddMembership(ctx, s, eng, activatedAt: $"{M(-2)}-01");
        ctx.StudentDiscounts.AddRange(
            new StudentDiscount
            {
                StudentId = s.Id, GroupId = mat.Id, Pct = 25,
                Status = StudentDiscount.StatusActive, CreatedAt = "2020-01-01T00:00:00",
            },
            new StudentDiscount
            {
                StudentId = s.Id, GroupId = eng.Id, Amount = 100_000m,
                Status = StudentDiscount.StatusActive, CreatedAt = "2020-01-01T00:00:00",
            });
        await ctx.SaveChangesAsync();

        await TuitionService.AccrueMonth(ctx, M(0));

        var rows = await ctx.MonthlyCharges.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(150_000m, rows.Single(r => r.GroupId == mat.Id).Discount);   // 600 000 ning 25%
        Assert.Equal(100_000m, rows.Single(r => r.GroupId == eng.Id).Discount);   // aniq summa
        // Balans: (600 000 − 150 000) + (400 000 − 100 000) = 750 000.
        Assert.Equal(-750_000m, s.Balance);
    }

    [Fact]
    public async Task AccrueMonth_chegirmasi_YOQ_oquvchida_hisob_TOLIQ_narxda()
    {
        // ⚠️ QULF: accrual yo'lida chegirma kitobi yuklanmasa 0 chegirma chiqardi — bu holat esa
        // "chegirmasiz o'quvchi" bilan farq qilmasdi. Shu sabab yuqoridagi testlar bilan JUFT:
        // biri "chegirma bor → qo'llanadi", bu esa "chegirma yo'q → to'liq narx".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        await TuitionService.AccrueMonth(ctx, M(0));

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(600_000m, row.Amount);
        Assert.Equal(0m, row.Discount);
        Assert.Equal(-600_000m, s.Balance);
    }

    // ==================== EnsureChargeAsync (avans) ====================

    [Fact]
    public async Task EnsureCharge_kelajak_oy_hisobini_ochadi_va_IDEMPOTENT()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        Assert.True(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, M(2)));
        await ctx.SaveChangesAsync();
        Assert.False(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, M(2)));
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(M(2), row.Month);
        Assert.Equal(600_000m, row.Amount);
        Assert.Equal(-600_000m, s.Balance);   // ikki marta yechilmaydi
    }

    [Fact]
    public async Task EnsureCharge_narx_nol_bolsa_hisob_ochilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 0m);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        Assert.False(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, M(1)));
        Assert.Empty(ctx.MonthlyCharges);
        Assert.Equal(0m, s.Balance);
    }

    [Fact(Skip = "XATO (TuitionService.EnsureChargeAsync:529-531 va PaymentIntake.cs:43-45): oy formati tekshirilmaydi")]
    public async Task EnsureCharge_toliq_sana_berilsa_ayni_oy_ikki_marta_hisoblanmasligi_kerak()
    {
        // XATO: tekshiruv faqat `month.Length < 7`. Klient/kassir "2026-08-15" (to'liq sana) yuborsa
        // u OY sifatida qabul qilinadi va "2026-08" dan BOSHQA kalit bo'lib qoladi → bir oy uchun
        // IKKITA hisob qatori ochiladi va o'quvchi ikki marta qarzdor bo'ladi.
        // KUTILGAN: format "yyyy-MM" ga keltiriladi (yoki rad etiladi) — ikkinchi chaqiriq yangi
        // qator ochmaydi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        Assert.True(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, M(0)));
        await ctx.SaveChangesAsync();
        Assert.False(await TuitionService.EnsureChargeAsync(ctx, s, g.Id, $"{M(0)}-15"));
        await ctx.SaveChangesAsync();

        Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(-600_000m, s.Balance);
    }

    // ==================== ChargeActivationProrateAsync ====================

    [Fact]
    public async Task ChargeActivationProrate_oy_boshidan_TOLIQ_oylik()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, $"{M(0)}-01");
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(600_000m, row.Amount);       // birinchi darsdan → to'liq oy
        Assert.Equal(-600_000m, s.Balance);
    }

    [Fact]
    public async Task ChargeActivationProrate_bitta_dars_qolganda_LessonPrice_boyicha()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var subj = new Subject { Name = "Ingliz tili", Price = 600_000m, LessonPrice = 20_000m };
        ctx.Subjects.Add(subj);
        var g = AddGroup(ctx, 600_000m, courseId: subj.Id);   // dars kunlari: har kuni
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        // Oyning OXIRGI kuni aktivlashtirildi → 1 ta dars qoldi (12 tadan kam) → 1 × LessonPrice.
        var bugun = AppClock.Today;
        var oyOxiri = new DateOnly(bugun.Year, bugun.Month, DateTime.DaysInMonth(bugun.Year, bugun.Month));
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, oyOxiri.ToString("yyyy-MM-dd"));
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(20_000m, row.Amount);
        Assert.Equal(-20_000m, s.Balance);
    }

    [Fact]
    public async Task ChargeActivationProrate_ikki_marta_bosish_IDEMPOTENT()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        await ctx.SaveChangesAsync();

        var sana = $"{M(0)}-01";
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, sana);
        await ctx.SaveChangesAsync();
        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, sana);   // admin ikki marta bosdi
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(600_000m, row.Amount);
        Assert.Equal(-600_000m, s.Balance);   // qarz IKKI baravar bo'lib ketmaydi
    }

    [Fact]
    public async Task ChargeActivationProrate_LOCKED_qator_tegilmaydi()
    {
        // Super admin qo'lda tahrirlagan (Locked) hisob avtomatik qayta hisobda o'zgarmasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(0), 100_000m, locked: true);
        await ctx.SaveChangesAsync();

        await TuitionService.ChargeActivationProrateAsync(ctx, s, g, $"{M(0)}-01");
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(100_000m, row.Amount);
        Assert.True(row.Locked);
        Assert.Equal(0m, s.Balance);
    }

    // ==================== PurgeChargesAfterMonthAsync ====================

    [Fact]
    public async Task Purge_keyingi_oylar_ochiriladi_balans_QAYTARILADI_Locked_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        AddCharge(ctx, s, g.Id, M(1), 600_000m);
        AddCharge(ctx, s, g.Id, M(2), 600_000m, locked: true);   // qo'lda tahrirlangan — tegilmaydi
        s.Balance = -1_800_000m;
        await ctx.SaveChangesAsync();

        var (restored, months) = await TuitionService.PurgeChargesAfterMonthAsync(ctx, s, g.Id, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(600_000m, restored);                 // faqat M+1 qaytarildi
        Assert.Equal(new[] { M(1) }, months.ToArray());
        Assert.Equal(-1_200_000m, s.Balance);
        Assert.Equal(2, ctx.MonthlyCharges.Count());
        Assert.Contains(ctx.MonthlyCharges, c => c.Month == M(2) && c.Locked);
        Assert.DoesNotContain(ctx.MonthlyCharges, c => c.Month == M(1));
    }

    [Fact]
    public async Task Purge_inclusive_bolsa_oyning_OZI_ham_bekor_qilinadi()
    {
        // A'zolik muzlatish sanasidan KEYIN aktivlashtirilgan bo'lsa — o'sha oyda umuman hisob bo'lmasligi kerak.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        s.Balance = -1_200_000m;
        await ctx.SaveChangesAsync();

        var (restored, months) = await TuitionService.PurgeChargesAfterMonthAsync(ctx, s, g.Id, M(0), inclusive: true);
        await ctx.SaveChangesAsync();

        Assert.Equal(600_000m, restored);
        Assert.Equal(new[] { M(0) }, months.ToArray());
        Assert.Equal(-600_000m, s.Balance);
        Assert.Single(ctx.MonthlyCharges);                // o'tgan oy tarixda qoladi
    }

    [Fact]
    public async Task Purge_chegirmali_qatorda_faqat_EFFEKTIV_summa_qaytariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m);
        var s = AddStudent(ctx);
        AddCharge(ctx, s, g.Id, M(1), 600_000m, discount: 200_000m);
        s.Balance = -400_000m;
        await ctx.SaveChangesAsync();

        var (restored, _) = await TuitionService.PurgeChargesAfterMonthAsync(ctx, s, g.Id, M(0));
        await ctx.SaveChangesAsync();

        Assert.Equal(400_000m, restored);   // to'liq narx emas, chegirmadan keyingi summa
        Assert.Equal(0m, s.Balance);
        Assert.Empty(ctx.MonthlyCharges);
    }

    // ==================== StudentLedger ====================

    [Fact]
    public async Task StudentLedger_100_foiz_chegirma_oyi_PAID_boladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh", pct: 100);
        AddCharge(ctx, s, g.Id, M(0), 600_000m, discount: 600_000m);
        await ctx.SaveChangesAsync();

        var dto = await StudentLedger.BuildAsync(ctx, s);

        var oy = Assert.Single(dto.Months);
        Assert.Equal("paid", oy.Status);      // to'lov qilinmagan bo'lsa ham qarz yo'q
        Assert.Equal(0m, oy.Paid);
        Assert.Equal(0m, oy.Remaining);
        Assert.Equal(600_000m, dto.TotalDiscount);
    }

    [Fact]
    public async Task StudentLedger_qisman_tolov_PARTIAL_toliq_tolov_PAID()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        AddCharge(ctx, s, g.Id, M(-1), 600_000m);
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        AddPayment(ctx, s, g.Id, M(-1), 600_000m);
        AddPayment(ctx, s, g.Id, M(0), 250_000m);
        await ctx.SaveChangesAsync();

        var dto = await StudentLedger.BuildAsync(ctx, s);

        Assert.Equal(2, dto.Months.Count);
        var otgan = dto.Months.Single(m => m.Month == M(-1));
        var joriy = dto.Months.Single(m => m.Month == M(0));
        Assert.Equal("paid", otgan.Status);
        Assert.Equal("partial", joriy.Status);
        Assert.Equal(350_000m, joriy.Remaining);
        Assert.Equal(850_000m, dto.TotalPaid);
    }

    // ==================== GroupBalanceService ====================

    [Fact]
    public async Task GroupBalance_teglanmagan_tolov_NARX_nisbatida_bolinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g1 = AddGroup(ctx, 600_000m, "Ingliz A");
        var g2 = AddGroup(ctx, 200_000m, "Matematika B");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g1, activatedAt: $"{M(-6)}-01");
        AddMembership(ctx, s, g2, activatedAt: $"{M(-6)}-01");
        AddPayment(ctx, s, null, M(0), 800_000m);   // eski (teglanmagan) to'lov
        await ctx.SaveChangesAsync();

        var b1 = await GroupBalanceService.ForGroupAsync(ctx, g1.Id, new[] { s.Id });
        var b2 = await GroupBalanceService.ForGroupAsync(ctx, g2.Id, new[] { s.Id });

        // 800 000 narx nisbatida (600k : 200k) bo'linadi.
        Assert.Equal(600_000m, b1[s.Id]);
        Assert.Equal(200_000m, b2[s.Id]);
    }

    [Fact]
    public async Task GroupBalance_per_guruh_yigindisi_UMUMIY_balansga_teng()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g1 = AddGroup(ctx, 600_000m, "Ingliz A");
        var g2 = AddGroup(ctx, 200_000m, "Matematika B");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g1, activatedAt: $"{M(-2)}-01");
        AddMembership(ctx, s, g2, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        await TuitionService.AccrueMonth(ctx, M(0));      // 600k + 200k hisoblandi
        AddPayment(ctx, s, null, M(0), 400_000m);         // teglanmagan qisman to'lov
        s.Balance += 400_000m;                            // to'lov balansni oshiradi (PaymentIntake bilan bir xil)
        await ctx.SaveChangesAsync();

        var b1 = await GroupBalanceService.ForGroupAsync(ctx, g1.Id, new[] { s.Id });
        var b2 = await GroupBalanceService.ForGroupAsync(ctx, g2.Id, new[] { s.Id });

        Assert.Equal(-300_000m, b1[s.Id]);   // −600 000 + 300 000 (400k × 600/800)
        Assert.Equal(-100_000m, b2[s.Id]);   // −200 000 + 100 000
        Assert.Equal(s.Balance, b1[s.Id] + b2[s.Id]);
    }

    [Fact]
    public async Task GroupBalance_teglangan_tolov_faqat_OZ_guruhiga_tegishli()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g1 = AddGroup(ctx, 600_000m, "Ingliz A");
        var g2 = AddGroup(ctx, 600_000m, "Matematika B");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g1, activatedAt: $"{M(-2)}-01");
        AddMembership(ctx, s, g2, activatedAt: $"{M(-2)}-01");
        AddCharge(ctx, s, g1.Id, M(0), 600_000m);
        AddCharge(ctx, s, g2.Id, M(0), 600_000m);
        AddPayment(ctx, s, g1.Id, M(0), 600_000m);   // faqat BIRINCHI guruhga to'landi
        await ctx.SaveChangesAsync();

        var b1 = await GroupBalanceService.DetailedForGroupAsync(ctx, g1.Id, new[] { s.Id });
        var b2 = await GroupBalanceService.DetailedForGroupAsync(ctx, g2.Id, new[] { s.Id });

        Assert.Equal(0m, b1[s.Id].Balance);            // to'lagan guruhida yashil
        Assert.Equal(0, b1[s.Id].DebtMonths);
        Assert.Equal(-600_000m, b2[s.Id].Balance);     // to'lamagan guruhida qizil
        Assert.Equal(1, b2[s.Id].DebtMonths);
        Assert.Equal(M(0), b2[s.Id].OldestDebtMonth);
    }

    // ==================== TASDIQLANGAN XATOLAR (Skip) ====================

    [Fact(Skip = "XATO (TuitionService.ApplyFeeToCharge:121-133): qisman (prorate) qator to'liq narxga aylanadi")]
    public async Task GuruhNarxiOzgarganda_QISMAN_qator_toliq_narxga_aylanmasligi_kerak()
    {
        // XATO: guruh narxi o'zgarganda ApplyFeeToCharge har qanday (Locked bo'lmagan) qatorga
        // `charge.Amount = newFee` yozadi — aktivlashtirish/muzlatish natijasida QISMAN (masalan
        // 4 dars uchun) hisoblangan qator ham TO'LIQ oylikka aylanadi va o'quvchi o'qimagan
        // darslar uchun qarzdor bo'lib qoladi.
        // KUTILGAN: qisman qator o'z NISBATINI saqlaydi (150 000/600 000 = 25% → 800 000 ning 25%i).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx);
        AddMembership(ctx, s, g, activatedAt: $"{M(0)}-20");
        AddCharge(ctx, s, g.Id, M(0), 150_000m);      // qisman oy (4 dars)
        s.Balance = -150_000m;
        await ctx.SaveChangesAsync();

        await TuitionService.ApplyGroupFeeToCurrentMonthAsync(ctx, g.Id, g.Name, 800_000m);
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.MonthlyCharges);
        Assert.Equal(200_000m, row.Amount);
        Assert.Equal(-200_000m, s.Balance);
    }

    [Fact(Skip = "XATO (StudentLedger.cs:71-77): vozvrat (refund) hisobga olinmaydi")]
    public async Task StudentLedger_VOZVRAT_qilingan_tolov_paid_dan_ayrilishi_kerak()
    {
        // XATO: ledger faqat `income + tuition` yozuvlarini yig'adi; `expense + refund` (pul qaytarish)
        // umuman hisobga olinmaydi. Natijada pulini qaytarib olgan o'quvchining oyi "paid" bo'lib
        // ko'rinadi va qarzi ko'rinmaydi (GroupBalanceService esa vozvratni AYIRADI — ikki ekranda
        // ikki xil raqam).
        // KUTILGAN: to'langan = kirim − vozvrat.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        var tolov = AddPayment(ctx, s, g.Id, M(0), 600_000m);
        var vozvrat = AddPayment(ctx, s, g.Id, M(0), 600_000m, direction: "expense", category: "refund");
        vozvrat.RefundOfId = tolov.Id;
        await ctx.SaveChangesAsync();

        var dto = await StudentLedger.BuildAsync(ctx, s);

        var oy = Assert.Single(dto.Months);
        Assert.Equal(0m, oy.Paid);
        Assert.Equal(600_000m, oy.Remaining);
        Assert.Equal("unpaid", oy.Status);
        Assert.Equal(0m, dto.TotalPaid);
    }

    [Fact(Skip = "XATO (StudentGroupLedger.cs:60-65): vozvrat (refund) hisobga olinmaydi")]
    public async Task StudentGroupLedger_VOZVRAT_qilingan_tolov_paid_dan_ayrilishi_kerak()
    {
        // XATO: guruh ledgeri ham faqat `income + tuition` ni yig'adi (StudentLedger bilan bir xil
        // kamchilik) — vozvratdan keyin to'lov oynasida oy "to'langan" bo'lib turadi.
        // KUTILGAN: shu guruhga to'langan = kirim − vozvrat.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        AddCharge(ctx, s, g.Id, M(0), 600_000m);
        var tolov = AddPayment(ctx, s, g.Id, M(0), 600_000m);
        var vozvrat = AddPayment(ctx, s, g.Id, M(0), 600_000m, direction: "expense", category: "refund");
        vozvrat.RefundOfId = tolov.Id;
        await ctx.SaveChangesAsync();

        var dto = await StudentGroupLedger.BuildAsync(ctx, s, g, m);

        var oy = dto.Months.Single(x => x.Month == M(0));
        Assert.Equal(0m, oy.Paid);
        Assert.Equal(600_000m, oy.Remaining);
    }

    [Fact(Skip = "XATO (PaymentIntake.cs:35-40,151): to'lov sanasi normallashtirilmaydi")]
    public async Task PaymentIntake_sana_ISO_formatga_keltirilishi_kerak()
    {
        // XATO: `paidDate` faqat DateOnly.TryParse bilan TEKSHIRILADI, lekin qayta yozilmaydi —
        // kassir "2026-8-1" kiritsa bazaga AYNAN shu satr tushadi. Keyin barcha joyda sana
        // `Date[..7]` bilan oyga ajratiladi ("2026-8-" → hech qaysi oyga tushmaydi) va sana
        // bo'yicha saralash/oralik filtri buziladi (satrli taqqoslash: "2026-8-1" > "2026-12-31").
        // KUTILGAN: saqlashdan oldin `DateOnly.ToString("yyyy-MM-dd")` ga keltiriladi.
        // (PaymentIntake.AddAsync ni to'g'ridan-to'g'ri chaqirib bo'lmaydi — u AutoMessageService
        // orqali Eskiz/FCM/Telegram/CTI xizmatlarini talab qiladi; shu sabab natija bazada tekshiriladi.)
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(ctx);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = "2026-8-1", Direction = "income", Category = "tuition",
            Amount = 600_000m, StudentId = s.Id, Month = "2026-08", Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var tx = Assert.Single(ctx.FinanceTransactions);
        Assert.Equal("2026-08-01", tx.Date);
    }

    [Fact(Skip = "XATO (SalaryLedger.cs:228): expected==0 va paid>0 bo'lganda holat 'unpaid'")]
    public async Task SalaryLedger_hisoblanmagan_oyga_tolov_qilinsa_holat_unpaid_bolmasligi_kerak()
    {
        // XATO: `status = remaining <= 0 ? (expected <= 0 ? "unpaid" : "paid") : ...` — o'qituvchiga
        // hisoblanmagan (expected = 0) oy uchun avans/qo'shimcha to'lov berilsa, holat "unpaid"
        // (to'lanmagan) bo'lib chiqadi, garchi PUL berilgan bo'lsa ham.
        // KUTILGAN: to'lov bor va qarz yo'q → "paid".
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = new Teacher { FullName = "Test O'qituvchi", SalaryMode = "fixed", Salary = 0m };
        ctx.Teachers.Add(t);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = $"{M(0)}-05", Direction = "expense", Category = "salary",
            Amount = 1_000_000m, TeacherId = t.Id, Month = M(0),
        });
        await ctx.SaveChangesAsync();

        var dto = await SalaryLedger.BuildAsync(ctx, t, M(0), M(0));

        var oy = dto.Months.Single(x => x.Month == M(0));
        Assert.Equal(1_000_000m, oy.Paid);
        Assert.Equal("paid", oy.Status);
    }

    [Fact(Skip = "XATO (RetentionBonusService.cs:838): RestartAsync global bayroqni yoqib boshqa fanlarni ham qo'shadi")]
    public async Task RetentionRestart_faqat_KORSATILGAN_fanga_tegishli_bolishi_kerak()
    {
        // XATO: `RestartAsync` izohida "faqat ko'rsatilgan fan uchun" deyilgan, lekin kod
        // `student.RetentionBonus = true` bilan GLOBAL bayroqni yoqadi — bonus ataylab
        // o'chirilgan o'quvchi bitta fan qayta boshlanishi bilan BARCHA fanlar bo'yicha
        // hisobotga qaytadi.
        // KUTILGAN: global bayroq tegilmaydi, faqat (o'quvchi, fan) track yoziladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = AddStudent(ctx);
        s.RetentionBonus = false;                 // admin bonusni ataylab o'chirgan
        await ctx.SaveChangesAsync();

        var xato = await RetentionBonusService.RestartAsync(ctx, s.Id, "kurs-1", M(0), "admin");
        await ctx.SaveChangesAsync();

        Assert.Null(xato);
        Assert.False(s.RetentionBonus);
        Assert.Single(ctx.RetentionBonusTracks);
    }

    // ==================== StudentGroupLedger — MUZLATIB, QAYTA AKTIVLASHTIRILGAN a'zolik ====================

    [Fact]
    public async Task StudentGroupLedger_qayta_aktivlashtirilgach_ESKI_qarzdor_oy_royxatda_QOLADI()
    {
        // MUAMMO edi: `ActivateCoreAsync` (ClassesController.cs:713) `ActivatedAt` ni YANGI sana bilan
        // ustidan yozadi va `FrozenAt` ni tozalaydi — muzlashdan OLDINGI faol davrning izi qolmaydi.
        // Ledger esa oylar oralig'ini AYNAN `ActivatedAt` dan boshlagani uchun to'lanmagan eski oy
        // to'lov oynasidagi ro'yxatga tushmasdi: qarz balansda ko'rinar, lekin uni to'lab bo'lmasdi.
        // KUTILGAN: hisob qatori (MonthlyCharge) bor har bir oy ro'yxatda bo'ladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        // 4 oy oldin aktiv edi, 3 oy oldin muzlatildi, BUGUN qayta aktivlashtirildi.
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        AddCharge(ctx, s, g.Id, M(-4), 600_000m); // TO'LANMAGAN eski oy
        AddCharge(ctx, s, g.Id, M(-3), 600_000m);
        AddPayment(ctx, s, g.Id, M(-3), 600_000m); // bu oy to'langan
        await ctx.SaveChangesAsync();

        var dto = await StudentGroupLedger.BuildAsync(ctx, s, g, m);

        var eski = dto.Months.Single(x => x.Month == M(-4));
        Assert.Equal(600_000m, eski.Remaining);
        Assert.Equal("unpaid", eski.Status);
        Assert.Equal("paid", dto.Months.Single(x => x.Month == M(-3)).Status);
        Assert.Contains(dto.Months, x => x.Month == M(0)); // joriy oy ham joyida
    }

    [Fact]
    public async Task StudentGroupLedger_hisobi_YOQ_eski_oylar_uchun_QARZ_OYLAB_TOPILMAYDI()
    {
        // Oraliq faqat HAQIQATAN pul biriktirilgan (hisob yoki teglangan to'lov) oylar bilan
        // kengaytiriladi. Oradagi bo'sh oylar (muzlatilgan davr) ro'yxatga TUSHMAYDI, ya'ni
        // mavjud bo'lmagan qarz paydo bo'lmaydi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        AddCharge(ctx, s, g.Id, M(-4), 600_000m);
        await ctx.SaveChangesAsync();

        var dto = await StudentGroupLedger.BuildAsync(ctx, s, g, m);

        // M(-3), M(-2), M(-1) — muzlatilgan davr: hisob ham, to'lov ham yo'q → summa 0.
        foreach (var delta in new[] { -3, -2, -1 })
        {
            var oy = dto.Months.SingleOrDefault(x => x.Month == M(delta));
            if (oy is not null) Assert.Equal(0m, oy.Remaining);
        }
        Assert.Equal(600_000m, dto.Months.Single(x => x.Month == M(-4)).Remaining);
    }

    [Fact]
    public async Task StudentGroupLedger_teglangan_ESKI_tolov_uchun_qarz_YOZILMAYDI()
    {
        // Eski oyda hisob YO'Q, faqat teglangan to'lov bor (masalan hisob qatori o'chirilgan).
        // Oy ro'yxatga tushadi, lekin guruh oyligi O'YLAB TOPILMAYDI — "to'langan" bo'lib turadi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        AddPayment(ctx, s, g.Id, M(-5), 600_000m);
        await ctx.SaveChangesAsync();

        var dto = await StudentGroupLedger.BuildAsync(ctx, s, g, m);

        var oy = dto.Months.Single(x => x.Month == M(-5));
        Assert.Equal(0m, oy.Remaining);
        Assert.Equal(600_000m, oy.Paid);
        Assert.Equal("paid", oy.Status);
    }

    // ============ AccrueMonth — MUZLATIB QAYTA AKTIVLASHTIRILGAN a'zolik (PastPeriods) ============

    [Fact]
    public async Task AccrueMonth_qayta_aktivlashtirilgach_ESKI_davrdagi_TUSHIB_QOLGAN_oy_yoziladi()
    {
        // MUAMMO edi: qayta aktivlashtirishda ActivatedAt yangi sanaga o'tar va eski davrdagi
        // hisobsiz qolgan oy HECH QACHON yozilmasdi (AccrueMonth sharti "oy > ActivatedAt").
        // Endi yopilgan davr (PastPeriods) shu oyni qamrab oladi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        m.PastPeriods = new List<string> { $"{M(-5)}-10|{M(-2)}-15" };
        await ctx.SaveChangesAsync();

        // M(-4), M(-3) — davr ICHIDA, chegaralar emas → yoziladi.
        Assert.Equal(1, (await TuitionService.AccrueMonth(ctx, M(-4))).Count);
        await ctx.SaveChangesAsync();
        Assert.Equal(1, (await TuitionService.AccrueMonth(ctx, M(-3))).Count);
        await ctx.SaveChangesAsync();

        // Chegaralar (aktivlashtirish va muzlatish oylari) QISMAN hisob bilan o'z vaqtida
        // yozilgan — bu yerda takrorlanmaydi.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-5))).Count);
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-2))).Count);
        // Muzlatilgan davr — hisoblanmaydi.
        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-1))).Count);
    }

    [Fact]
    public async Task AccrueMonth_PastPeriods_BOSH_bolsa_bironta_ham_yangi_hisob_YOZILMAYDI()
    {
        // Deploy xavfsizligi: mavjud qatorlarda tarix bo'sh (backfill YO'Q), ya'ni fon xizmati
        // butun tarixni qayta skanerlaganda ham retroaktiv qarz paydo bo'lmaydi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01"); // PastPeriods bo'sh
        await ctx.SaveChangesAsync();

        foreach (var delta in new[] { -5, -4, -3, -2, -1 })
            Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(delta))).Count);
    }

    [Fact]
    public async Task AccrueMonth_guruhdan_CHIQARILGAN_azolikka_retroaktiv_qarz_yozilmaydi()
    {
        // Yopilgan davr bor, lekin a'zolik endi faol emas — chiqib ketgan o'quvchiga orqaga
        // qarab qarz yozilmasligi kerak (AccrueMonth dagi Status/IsActive sharti tegilmagan).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01", isActive: false);
        m.LeftAt = $"{M(0)}-01";
        m.PastPeriods = new List<string> { $"{M(-5)}-10|{M(-2)}-15" };
        await ctx.SaveChangesAsync();

        Assert.Equal(0, (await TuitionService.AccrueMonth(ctx, M(-4))).Count);
    }

    [Fact]
    public async Task AccrueCatchUp_yopilgan_davrdagi_boshliqni_DARHOL_toldiradi()
    {
        // Aktivlashtirish tugmasi bosilganda kutish shart emas: catch-up yopilgan davrlardagi
        // tushib qolgan oylarni ham o'sha zahoti yozadi (fon xizmati 12 soat kutmasin).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        var m = AddMembership(ctx, s, g, activatedAt: $"{M(0)}-01");
        m.PastPeriods = new List<string> { $"{M(-5)}-10|{M(-2)}-15" };
        await ctx.SaveChangesAsync();

        // Bugundan aktivlashtirilgan → oldingi mantiqda catch-up 0 qaytarardi.
        var created = await TuitionService.AccrueCatchUpAsync(ctx, s, g, $"{M(0)}-01", m);
        await ctx.SaveChangesAsync();

        Assert.Equal(2, created); // M(-4) va M(-3)
        var oylar = ctx.MonthlyCharges.Where(c => c.GroupId == g.Id).Select(c => c.Month).OrderBy(x => x).ToList();
        Assert.Equal(new[] { M(-4), M(-3) }, oylar);
    }

    [Fact]
    public async Task AccrueCatchUp_azoliksiz_chaqiriq_ESKI_xattiharakatni_saqlaydi()
    {
        // membership berilmasa (eski chaqiruvlar) — faqat orqaga sanalgan aktivlashtirish oralig'i.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = AddGroup(ctx, 600_000m, "A guruh");
        var s = AddStudent(ctx, className: "A guruh");
        AddMembership(ctx, s, g, activatedAt: $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        var created = await TuitionService.AccrueCatchUpAsync(ctx, s, g, $"{M(-2)}-01");
        await ctx.SaveChangesAsync();

        Assert.Equal(2, created); // M(-1), M(0) — kelajak oy yozilmaydi
        Assert.DoesNotContain(M(1), ctx.MonthlyCharges.Select(c => c.Month).ToList());
    }
}
