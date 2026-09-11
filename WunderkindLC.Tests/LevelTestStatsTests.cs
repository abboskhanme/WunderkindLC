using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// DARAJA TESTLARI umumiy statistikasi (<see cref="LevelTestService.BuildOverallStatsAsync"/>) —
/// "Formalar → Test statistikasi" sahifasining manbai. Rasmiy qoidalar:
/// <c>.claude/rules/lead-forms.md</c>.
///
/// <para>Asosiy qoida lid formalari bilan BIR XIL: foizlar va pul <b>TAKRORSIZ LIDLAR</b> bo'yicha
/// (bir odam testni ikki marta topshirsa ham bitta mijoz), "aktiv" va "to'ladi" ta'rifi esa
/// <see cref="LeadOutcome"/> dan — ya'ni ikki bo'limda bir xil ma'no anglatadi.</para>
/// </summary>
public class LevelTestStatsTests
{
    // ===================== Yordamchilar =====================

    private static LevelTest NewTest(string title = "Ingliz tili — daraja testi", string slug = "ingliz-test") =>
        new() { Title = title, Slug = slug, IsActive = true, CreatedAt = "2026-08-01T09:00:00" };

    /// <summary>Lid + unga bog'langan topshiruv (bazaga qo'shiladi, saqlanmaydi).</summary>
    private static Lead AddSubmission(
        Microsoft.EntityFrameworkCore.DbContext db, LevelTest test, Lead? lead = null,
        string name = "Aliyev Ali", string phone = "+998901234567",
        int percent = 80, string level = "B1", string date = "2026-08-05T10:00:00")
    {
        var target = lead ?? new Lead
        {
            FullName = name, Phone = phone, Source = "Daraja testi", CreatedAt = date,
        };
        if (lead is null) db.Add(target);
        db.Add(new LevelTestSubmission
        {
            TestId = test.Id, FullName = name, Phone = phone, Score = 8, Total = 10,
            Percent = percent, Level = level, CreatedAt = date, LeadId = target.Id,
        });
        return target;
    }

    /// <summary>Lidni o'quvchiga aylantiradi va (ixtiyoriy) to'lov yozadi.</summary>
    private static Student AddStudent(
        Microsoft.EntityFrameworkCore.DbContext db, Lead lead, decimal paid = 0m, string date = "2026-08-06")
    {
        var student = new Student { FullName = lead.FullName };
        db.Add(student);
        lead.ConvertedStudentId = student.Id;
        if (paid > 0)
            db.Add(new FinanceTransaction
            {
                StudentId = student.Id, Direction = "income", Category = "tuition",
                Amount = paid, Date = date,
            });
        return student;
    }

    // ===================== 1) Takrorsiz lidlar =====================

    /// <summary>
    /// Bir odam testni IKKI marta topshirsa: topshiriq 2 ta, lekin lid 1 ta va puli BIR marta
    /// sanaladi — aks holda ko'p topshirilgan test sun'iy ravishda yaxshi/yomon ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Voronka_TAKRORSIZ_lidlar_boyicha_hisoblanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);

        var ali = AddSubmission(db, test);
        AddSubmission(db, test, ali, date: "2026-08-07T10:00:00");     // o'sha odam, ikkinchi marta
        var vali = AddSubmission(db, test, name: "Valiyev Vali", phone: "+998907654321");
        AddStudent(db, ali, paid: 400_000m);
        AddStudent(db, vali);                                          // o'quvchi bo'ldi, lekin to'lamadi
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);

        Assert.Equal(3, stats.Submissions);
        Assert.Equal(2, stats.Leads);          // takrorsiz
        Assert.Equal(2, stats.Converted);
        Assert.Equal(1, stats.Paid);
        Assert.Equal(400_000m, stats.Revenue); // ikki topshiriq bo'lsa ham summa BIR marta

        var row = Assert.Single(stats.ByTest);
        Assert.Equal(3, row.Submissions);
        Assert.Equal(2, row.Leads);
        Assert.Equal(100, row.ConvertRate);    // 2/2
        Assert.Equal(50, row.PayRate);         // 1/2
    }

    // ===================== 2) Testlar kesimi =====================

    [Fact]
    public async Task Har_test_OZ_voronkasini_oladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var a = NewTest("Ingliz tili", "ingliz");
        var b = NewTest("Matematika", "matem");
        db.LevelTests.AddRange(a, b);

        var lead1 = AddSubmission(db, a);
        AddSubmission(db, b, name: "Valiyev Vali", phone: "+998907654321");
        AddStudent(db, lead1, paid: 500_000m);
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        Assert.Equal(2, stats.TestCount);

        var ingliz = stats.ByTest.Single(r => r.Title == "Ingliz tili");
        var matem = stats.ByTest.Single(r => r.Title == "Matematika");
        Assert.Equal(1, ingliz.Paid);
        Assert.Equal(500_000m, ingliz.Revenue);
        Assert.Equal(0, matem.Paid);
        Assert.Equal(0m, matem.Revenue);
    }

    /// <summary>Topshiriq tushmagan test ham ro'yxatda qoladi (bo'sh voronka bilan) — aks holda
    /// "test ishlamayapti" degan xulosa chiqarib bo'lmasdi.</summary>
    [Fact]
    public async Task Topshiriqsiz_test_ham_royxatda_qoladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LevelTests.Add(NewTest());
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        var row = Assert.Single(stats.ByTest);
        Assert.Equal(0, row.Submissions);
        Assert.Equal(0, row.Leads);
        Assert.Equal(0, row.PayRate);
    }

    // ===================== 3) Bosqichlar =====================

    [Fact]
    public async Task Bosqichlar_kesimi_TAKRORSIZ_lidlarni_hozirgi_ustuni_boyicha_guruhlaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);
        var stage = new LeadStage { Title = "Sinov darsi", Color = "amber", Order = 1 };
        db.LeadStages.Add(stage);

        var ali = AddSubmission(db, test);
        AddSubmission(db, test, ali, date: "2026-08-07T10:00:00"); // o'sha lid, ikkinchi topshiriq
        ali.Stage = stage.Id;
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        var row = Assert.Single(stats.ByStage);
        Assert.Equal("Sinov darsi", row.Stage);
        Assert.Equal("amber", row.Color);
        Assert.Equal(1, row.Leads); // ikki topshiriq — bitta lid
    }

    /// <summary>Ustuni O'CHIRILGAN lid bosqichlar kesimiga kirmaydi (kanbanda ham ko'rinmaydi) —
    /// sun'iy "Noma'lum bosqich" yasalmaydi.</summary>
    [Fact]
    public async Task Ochirilgan_ustundagi_lid_bosqichlar_kesimiga_kirmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);
        var stage = new LeadStage { Title = "Yangi", Color = "slate", Order = 0 };
        db.LeadStages.Add(stage);
        var lead = AddSubmission(db, test);
        lead.Stage = stage.Id;
        await db.SaveChangesAsync();

        db.LeadStages.Remove(stage);
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        Assert.Empty(stats.ByStage);
        Assert.Equal("", Assert.Single(stats.Rows).StageTitle);
    }

    // ===================== 4) Qatorlar va kunlik oqim =====================

    [Fact]
    public async Task Qatorlarda_bosqich_va_tolov_korinadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);
        var stage = new LeadStage { Title = "Sinov darsi", Color = "amber", Order = 1 };
        db.LeadStages.Add(stage);
        var lead = AddSubmission(db, test);
        lead.Stage = stage.Id;
        AddStudent(db, lead, paid: 250_000m, date: "2026-08-06");
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        var row = Assert.Single(stats.Rows);

        Assert.Equal("Ingliz tili — daraja testi", row.TestTitle);
        Assert.Equal("Sinov darsi", row.StageTitle);
        Assert.True(row.Paid);
        Assert.Equal(250_000m, row.PaidTotal);
        Assert.Equal("2026-08-06", row.FirstPaidAt);
    }

    /// <summary>Kunlik oqim — har doim 30 ta kun (bo'sh kunlar ham), aks holda grafik uzilardi.</summary>
    [Fact]
    public async Task Kunlik_oqim_har_doim_30_kun()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LevelTests.Add(NewTest());
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        Assert.Equal(LevelTestService.DailyDays, stats.Daily.Count);
        Assert.All(stats.Daily, d => Assert.Equal(0, d.Count));
    }

    /// <summary>
    /// `DistinctByLead` — statistikaning asosiy qoidasi. Bitta test sahifasi ham, umumiy sahifa ham
    /// AYNAN shu funksiyani chaqiradi, ya'ni "aktiv"/"to'ladi" ikki ekranda bir xil chiqadi.
    /// </summary>
    [Fact]
    public void DistinctByLead_har_liddan_BITTA_qator_qoldiradi()
    {
        static LevelTestStatRowDto Row(string subId, string leadId) =>
            new(subId, "Ali", "+998901234567", "B1", 80, "2026-08-05T10:00:00", leadId,
                null, false, "", "", false);

        var result = LevelTestService.DistinctByLead(new[]
        {
            Row("s1", "lead-1"),
            Row("s2", "lead-1"),   // o'sha lid — tashlanadi
            Row("s3", "lead-2"),
            Row("s4", ""),          // lidsiz topshiriq — sanoqqa umuman kirmaydi
        });

        Assert.Equal(2, result.Count);
        Assert.Equal("s1", result[0].SubmissionId);  // birinchisi (eng yangisi) qoladi
        Assert.Equal("s3", result[1].SubmissionId);
    }

    /// <summary>
    /// Qatorlar CHEKLANADI, lekin cheklov JIM QOLMAYDI: `RowsTotal` da jami son qaytadi
    /// (sahifa "N tadan eng yangi M tasi" deb yozadi).
    /// </summary>
    [Fact]
    public async Task Qatorlar_cheklanadi_va_jami_son_alohida_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);
        var extra = 5;
        for (var i = 0; i < LevelTestService.MaxRows + extra; i++)
            AddSubmission(db, test, name: $"Odam {i}", phone: $"+9989000{i:D5}");
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);

        Assert.Equal(LevelTestService.MaxRows + extra, stats.RowsTotal);
        Assert.Equal(LevelTestService.MaxRows, stats.Rows.Count);
        // Sanoqlar CHEKLOVDAN mustaqil — hammasi bo'yicha hisoblanadi.
        Assert.Equal(LevelTestService.MaxRows + extra, stats.Submissions);
        Assert.Equal(LevelTestService.MaxRows + extra, stats.Leads);
    }

    /// <summary>Vozvrat: puli TO'LIQ qaytarilgan odam "to'ladi" deb sanalmaydi (lid formalari
    /// statistikasi bilan bir xil qoida — manbasi yagona: <see cref="LeadOutcome"/>).</summary>
    [Fact]
    public async Task Toliq_qaytarilgan_tolov_sotuvga_kirmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = NewTest();
        db.LevelTests.Add(test);
        var lead = AddSubmission(db, test);
        var student = AddStudent(db, lead, paid: 300_000m);
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "expense", Category = "refund",
            Amount = 300_000m, Date = "2026-08-09",
        });
        await db.SaveChangesAsync();

        var stats = await LevelTestService.BuildOverallStatsAsync(db);
        Assert.Equal(1, stats.Converted);
        Assert.Equal(0, stats.Paid);
        Assert.Equal(0m, stats.Revenue);
    }
}
