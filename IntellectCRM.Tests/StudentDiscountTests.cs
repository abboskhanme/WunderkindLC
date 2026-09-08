using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// CHEGIRMA REGISTRI (<see cref="StudentDiscount"/>) testlari.
///
/// <para>Asosiy INVARIANT tekshiriladi: har o'quvchida ko'pi bilan BITTA <c>active</c> qator
/// bo'ladi va u AYNAN <c>Student.Discount*</c> maydonlariga mos keladi. Registr pul mantig'ini
/// ALMASHTIRMAYDI — shuning uchun <c>InForce</c> ning <c>TuitionService</c> bilan bir xilligi
/// ham alohida qulflangan (`.claude/rules/discounts.md` §1–§2).</para>
///
/// <para>Sanalar MUTLAQ yozilmaydi — <see cref="AppClock"/> ga NISBATAN quriladi, aks holda
/// test kelasi oyda yiqilardi.</para>
/// </summary>
public class StudentDiscountTests
{
    private const string Actor = "Test Admin";

    /// <summary>Joriy oydan <paramref name="delta"/> oy nariga/beriga ("yyyy-MM").</summary>
    private static string M(int delta) => AppClock.Today.AddMonths(delta).ToString("yyyy-MM");

    private static Student AddStudent(AppDbContext ctx, string name = "Test O'quvchi")
    {
        var s = new Student { FullName = name, EnrollmentDate = $"{M(-6)}-01" };
        ctx.Students.Add(s);
        return s;
    }

    private static Group AddGroup(AppDbContext ctx, string name = "A guruh", string teacherId = "")
    {
        var g = new Group { Name = name, MonthlyFee = 500_000m, TeacherId = teacherId };
        ctx.Classes.Add(g);
        return g;
    }

    private static DiscountSpec Spec(
        int pct = 0, decimal amount = 0m, string start = "", string end = "",
        string reason = "", string? groupId = null) =>
        new(pct, amount, start, end, reason, groupId);

    // ==================== ApplyAsync ====================

    [Fact]
    public async Task ApplyAsync_yangi_chegirma_eskisini_REPLACED_qiladi_va_Student_maydonlariga_mos_keladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(10, 0m, reason: "Birinchi"), Actor, "u1");
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, 50_000m, M(0), M(3), "Ikkinchi"), Actor, "u1");
        await db.SaveChangesAsync();

        var rows = await db.StudentDiscounts.Where(d => d.StudentId == s.Id).ToListAsync();
        Assert.Equal(2, rows.Count);

        var old = Assert.Single(rows, r => r.Reason == "Birinchi");
        Assert.Equal(StudentDiscount.StatusReplaced, old.Status);
        Assert.NotEqual("", old.EndedAt);
        Assert.Equal(Actor, old.EndedBy);

        var now = Assert.Single(rows, r => r.Reason == "Ikkinchi");
        Assert.Equal(StudentDiscount.StatusActive, now.Status);
        Assert.Equal("", now.EndedAt);

        // Registr AYNAN Student.Discount* ni aks ettiradi — pul mantig'i o'sha maydonlarni o'qiydi.
        Assert.Equal(20, s.DiscountPct);
        Assert.Equal(50_000m, s.DiscountAmount);
        Assert.Equal("Ikkinchi", s.DiscountNote);
        Assert.Equal(M(0), s.DiscountStartMonth);
        Assert.Equal(M(3), s.DiscountEndMonth);
    }

    [Fact]
    public async Task ApplyAsync_har_qanday_ketma_ketlikda_KO_PI_BILAN_BITTA_active_qator_qoladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        for (var i = 1; i <= 4; i++)
        {
            await StudentDiscountService.ApplyAsync(db, s, Spec(i * 5, reason: $"#{i}"), Actor, null);
            await db.SaveChangesAsync();
        }

        var active = await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync();
        Assert.Single(active);
        Assert.Equal(20, active[0].Pct);
        Assert.Equal(20, s.DiscountPct);
        // Tarix o'chmaydi — hammasi joyida.
        Assert.Equal(4, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
    }

    [Fact]
    public async Task ApplyAsync_NOLINCHI_spec_chegirmani_olib_tashlaydi_va_yangi_qator_OCHMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(15, reason: "Bor edi"), Actor, null);
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(), Actor, null);
        await db.SaveChangesAsync();

        Assert.Null(row);
        Assert.Equal(0, s.DiscountPct);
        Assert.Equal(0m, s.DiscountAmount);
        Assert.Equal("", s.DiscountNote);
        Assert.Equal(1, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
        Assert.Empty(await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_guruh_va_oqituvchi_SNAPSHOT_ini_toldiradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var teacher = new Teacher { FullName = "Ustoz Aliyev" };
        db.Teachers.Add(teacher);
        var g = AddGroup(db, "Ingliz A1", teacher.Id);
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(30, groupId: g.Id), Actor, null);
        await db.SaveChangesAsync();

        Assert.NotNull(row);
        Assert.Equal(g.Id, row!.GroupId);
        Assert.Equal("Ingliz A1", row.GroupName);
        Assert.Equal(teacher.Id, row.TeacherId);
        Assert.Equal("Ustoz Aliyev", row.TeacherName);
        Assert.Equal(g.Id, s.DiscountGroupId);
    }

    // ==================== CancelAsync ====================

    [Fact]
    public async Task CancelAsync_qatorni_OCHIRMAYDI_lekin_oquvchidan_chegirmani_OLADI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(25, 10_000m, reason: "Aka-uka"), Actor, null);
        await db.SaveChangesAsync();

        var ok = await StudentDiscountService.CancelAsync(db, s, row!, "Chegirma muddati tugadi", Actor);
        await db.SaveChangesAsync();

        Assert.True(ok);
        var saved = await db.StudentDiscounts.SingleAsync(d => d.Id == row!.Id);
        Assert.Equal(StudentDiscount.StatusCancelled, saved.Status);
        Assert.Equal("Chegirma muddati tugadi", saved.CancelReason);
        Assert.NotEqual("", saved.EndedAt);
        Assert.Equal(Actor, saved.EndedBy);

        Assert.Equal(0, s.DiscountPct);
        Assert.Equal(0m, s.DiscountAmount);
        Assert.Equal("", s.DiscountStartMonth);
        Assert.Null(s.DiscountGroupId);
    }

    [Fact]
    public async Task CancelAsync_TARIXIY_qatorda_false_qaytaradi_chaqiruvchi_400_beradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var first = await StudentDiscountService.ApplyAsync(db, s, Spec(10, reason: "Eski"), Actor, null);
        await db.SaveChangesAsync();
        await StudentDiscountService.ApplyAsync(db, s, Spec(20, reason: "Yangi"), Actor, null);
        await db.SaveChangesAsync();

        // `first` endi `replaced` — bekor qilib bo'lmaydi.
        Assert.Equal(StudentDiscount.StatusReplaced, first!.Status);
        Assert.False(await StudentDiscountService.CancelAsync(db, s, first, "sabab", Actor));
        // O'quvchining amaldagi chegirmasi TEGILMAYDI.
        Assert.Equal(20, s.DiscountPct);
    }

    // ==================== UpdateAsync ====================

    [Fact]
    public async Task UpdateAsync_TARIXIY_qatorni_tahrirlab_BOLMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var first = await StudentDiscountService.ApplyAsync(db, s, Spec(10, reason: "Eski"), Actor, null);
        await db.SaveChangesAsync();
        await StudentDiscountService.ApplyAsync(db, s, Spec(20, reason: "Yangi"), Actor, null);
        await db.SaveChangesAsync();

        Assert.False(await StudentDiscountService.UpdateAsync(db, s, first!, Spec(99, reason: "Buzuq"), Actor));
        Assert.Equal(10, first!.Pct);          // qator tegilmadi
        Assert.Equal(20, s.DiscountPct);       // o'quvchi ham
    }

    [Fact]
    public async Task UpdateAsync_amaldagi_qatorni_JOYIDA_tahrirlaydi_yangi_qator_OCHMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(10, reason: "Xato yozilgan"), Actor, null);
        await db.SaveChangesAsync();

        Assert.True(await StudentDiscountService.UpdateAsync(
            db, s, row!, Spec(15, 5_000m, M(-1), M(2), "To'g'rilandi"), Actor));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
        Assert.Equal(15, row!.Pct);
        Assert.Equal("To'g'rilandi", row.Reason);
        Assert.Equal(15, s.DiscountPct);
        Assert.Equal(5_000m, s.DiscountAmount);
        Assert.Equal(M(-1), s.DiscountStartMonth);
    }

    // ==================== SyncFromStudentAsync ====================

    [Fact]
    public async Task SyncFromStudentAsync_ESKI_forma_orqali_ozgargan_chegirmani_registrga_koradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(10, reason: "Boshlang'ich"), Actor, null);
        await db.SaveChangesAsync();

        // Eski forma to'g'ridan-to'g'ri Student maydonlarini o'zgartirdi.
        s.DiscountPct = 40;
        s.DiscountNote = "Forma orqali";
        Assert.True(await StudentDiscountService.SyncFromStudentAsync(db, s, Actor, null));
        await db.SaveChangesAsync();

        var active = Assert.Single(await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync());
        Assert.Equal(40, active.Pct);
        Assert.Equal("Forma orqali", active.Reason);
        Assert.Equal(2, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
    }

    [Fact]
    public async Task SyncFromStudentAsync_FARQ_YOQ_bolsa_hech_narsa_qilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(10, 1_000m, M(0), "", "Bir xil"), Actor, null);
        await db.SaveChangesAsync();

        Assert.False(await StudentDiscountService.SyncFromStudentAsync(db, s, Actor, null));
        Assert.Equal(1, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
    }

    // ==================== InForce ====================

    [Fact]
    public void InForce_davr_chegaralari_INKLYUZIV()
    {
        var d = new StudentDiscount
        {
            Status = StudentDiscount.StatusActive,
            StartMonth = "2026-03",
            EndMonth = "2026-05",
        };

        Assert.False(DiscountRules.InForce(d, "2026-02"));
        Assert.True(DiscountRules.InForce(d, "2026-03"));   // boshlanish oyi KIRADI
        Assert.True(DiscountRules.InForce(d, "2026-04"));
        Assert.True(DiscountRules.InForce(d, "2026-05"));   // tugash oyi ham KIRADI
        Assert.False(DiscountRules.InForce(d, "2026-06"));
    }

    [Fact]
    public void InForce_TuitionService_DiscountActiveForMonth_bilan_AYNAN_bir_xil_natija_beradi()
    {
        string[] bounds = ["", "2026-01", "2026-06", "2026-12"];
        string[] months = ["2025-12", "2026-01", "2026-05", "2026-06", "2026-07", "2026-12", "2027-01"];

        foreach (var start in bounds)
        foreach (var end in bounds)
        {
            // Teskari oraliqni ham sinaymiz — ikkala tomon bir xil "hech qachon" berishi kerak.
            var d = new StudentDiscount { Status = StudentDiscount.StatusActive, StartMonth = start, EndMonth = end };
            var s = new Student { DiscountStartMonth = start, DiscountEndMonth = end };
            foreach (var m in months)
                Assert.Equal(TuitionService.DiscountActiveForMonth(s, m), DiscountRules.InForce(d, m));
        }
    }

    [Fact]
    public void InForce_TARIXIY_qator_uchun_har_doim_false()
    {
        foreach (var status in new[] { StudentDiscount.StatusReplaced, StudentDiscount.StatusCancelled })
        {
            var d = new StudentDiscount { Status = status };   // davri cheklovsiz
            Assert.False(DiscountRules.InForce(d, AppClock.Today.ToString("yyyy-MM")));
        }
    }

    [Fact]
    public void StatusLabel_ozbekcha_yorliqlar()
    {
        Assert.Equal("Amalda", DiscountRules.StatusLabel(StudentDiscount.StatusActive));
        Assert.Equal("Almashtirilgan", DiscountRules.StatusLabel(StudentDiscount.StatusReplaced));
        Assert.Equal("Bekor qilingan", DiscountRules.StatusLabel(StudentDiscount.StatusCancelled));
    }

    // ==================== ReapplyCurrentMonthAsync ====================

    [Fact]
    public async Task ReapplyCurrentMonthAsync_LOCKED_qatorga_TEGMAYDI_va_balansni_togri_ozgartiradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var g = AddGroup(db);
        var locked = AddGroup(db, "B guruh");
        var s = AddStudent(db);
        s.Balance = 0m;
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 500_000m, Discount = 0m,
            Date = $"{M(0)}-01",
        });
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = locked.Id, Month = M(0), Amount = 500_000m, Discount = 0m,
            Date = $"{M(0)}-01", Locked = true,
        });
        // O'TGAN oy — tarixiy, tegilmasligi kerak.
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 500_000m, Discount = 0m,
            Date = $"{M(-1)}-01",
        });
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, reason: "20%"), Actor, null);
        var applied = await StudentDiscountService.ReapplyCurrentMonthAsync(db, s);
        await db.SaveChangesAsync();

        Assert.True(applied);
        var charges = await db.MonthlyCharges.Where(c => c.StudentId == s.Id).ToListAsync();
        var open = charges.Single(c => c.Month == M(0) && !c.Locked);
        var lockedCharge = charges.Single(c => c.Locked);
        var past = charges.Single(c => c.Month == M(-1));

        Assert.Equal(100_000m, open.Discount);      // 500 000 ning 20%
        Assert.Equal(500_000m, open.Amount);        // narx TEGILMAYDI
        Assert.Equal(0m, lockedCharge.Discount);    // Locked — tegilmadi
        Assert.Equal(0m, past.Discount);            // o'tgan oy — tarix
        // Chegirma berildi → to'lash kerak bo'lgan summa kamaydi → balans O'SADI.
        Assert.Equal(100_000m, s.Balance);
    }

    [Fact]
    public async Task ReapplyCurrentMonthAsync_chegirma_BEKOR_qilinsa_balansni_qaytaradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var g = AddGroup(db);
        var s = AddStudent(db);
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 500_000m, Discount = 0m,
            Date = $"{M(0)}-01",
        });
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(20), Actor, null);
        await StudentDiscountService.ReapplyCurrentMonthAsync(db, s);
        await db.SaveChangesAsync();
        Assert.Equal(100_000m, s.Balance);

        await StudentDiscountService.CancelAsync(db, s, row!, "Xato berilgan", Actor);
        var applied = await StudentDiscountService.ReapplyCurrentMonthAsync(db, s);
        await db.SaveChangesAsync();

        Assert.True(applied);
        Assert.Equal(0m, (await db.MonthlyCharges.SingleAsync(c => c.StudentId == s.Id)).Discount);
        Assert.Equal(0m, s.Balance);   // qarz asl holiga qaytdi
    }

    // ==================== BuildAsync (profil javobi) ====================

    [Fact]
    public async Task BuildAsync_QOLLANGAN_chegirmani_MonthlyCharge_dan_oladi_registrdan_EMAS()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var teacher = new Teacher { FullName = "Ustoz Valiyev" };
        db.Teachers.Add(teacher);
        var g = AddGroup(db, "Matematika", teacher.Id);
        var s = AddStudent(db);
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-1), Amount = 500_000m, Discount = 50_000m,
            Date = $"{M(-1)}-01",
        });
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(0), Amount = 500_000m, Discount = 100_000m,
            Date = $"{M(0)}-01",
        });
        // Chegirmasiz hisob lentaga TUSHMAYDI.
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = M(-2), Amount = 500_000m, Discount = 0m,
            Date = $"{M(-2)}-01",
        });
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, reason: "Ko'p bolali"), Actor, null);
        await db.SaveChangesAsync();

        var res = await StudentDiscountService.BuildAsync(db, s.Id);

        Assert.Equal(2, res.Applied.Count);
        Assert.Equal(M(0), res.Applied[0].Month);           // eng yangi oy birinchi
        Assert.Equal("Matematika", res.Applied[0].GroupName);
        Assert.Equal("Ustoz Valiyev", res.Applied[0].TeacherName);
        Assert.Equal(150_000m, res.TotalDiscount);
        Assert.Equal(100_000m, res.CurrentMonthDiscount);
        Assert.NotNull(res.Current);
        Assert.Equal("Ko'p bolali", res.Current!.Reason);
        Assert.True(res.Current.InForce);
        Assert.Equal("Amalda", res.Current.StatusLabel);
    }
}
