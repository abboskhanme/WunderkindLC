using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CHEGIRMA REGISTRI (<see cref="StudentDiscount"/>) testlari.
///
/// <para>Asosiy INVARIANT tekshiriladi: har <c>(o'quvchi, guruh)</c> QAMROVIDA ko'pi bilan BITTA
/// <c>active</c> qator bo'ladi — o'quvchida esa nechta fan bo'lsa shuncha chegirma bo'lishi
/// mumkin. Registr endi PUL MANBASI, <c>Student.Discount*</c> esa KO'ZGU
/// (`.claude/rules/discounts.md` §1–§4).</para>
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

    private static Group AddGroup(
        AppDbContext ctx, string name = "A guruh", string teacherId = "", string courseId = "")
    {
        var g = new Group { Name = name, MonthlyFee = 500_000m, TeacherId = teacherId, CourseId = courseId };
        ctx.Classes.Add(g);
        return g;
    }

    /// <summary>FAN (kurs) — chegirma qatoridagi <c>CourseName</c> snapshot'i shundan olinadi.</summary>
    private static Subject AddCourse(AppDbContext ctx, string name)
    {
        var c = new Subject { Name = name };
        ctx.Subjects.Add(c);
        return c;
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
        var course = AddCourse(db, "Ingliz tili");
        var g = AddGroup(db, "Ingliz A1", teacher.Id, course.Id);
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var row = await StudentDiscountService.ApplyAsync(db, s, Spec(30, groupId: g.Id), Actor, null);
        await db.SaveChangesAsync();

        Assert.NotNull(row);
        Assert.Equal(g.Id, row!.GroupId);
        Assert.Equal("Ingliz A1", row.GroupName);
        Assert.Equal(course.Id, row.CourseId);
        Assert.Equal("Ingliz tili", row.CourseName);   // foydalanuvchi FANNI ko'radi
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

    // ==================== HAR FAN UCHUN ALOHIDA CHEGIRMA ====================

    [Fact]
    public async Task ApplyAsync_HAR_FAN_uchun_ALOHIDA_qator__biri_ikkinchisini_YOPMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id, reason: "Mat"), Actor, null);
        await db.SaveChangesAsync();
        await StudentDiscountService.ApplyAsync(db, s, Spec(15, groupId: eng.Id, reason: "Ing"), Actor, null);
        await db.SaveChangesAsync();

        var active = await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Equal(20, Assert.Single(active, a => a.GroupId == mat.Id).Pct);
        Assert.Equal(15, Assert.Single(active, a => a.GroupId == eng.Id).Pct);
    }

    [Fact]
    public async Task ApplyAsync_AYNAN_SHU_qamrovdagi_eskisini_yopadi_boshqasiga_TEGMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id), Actor, null);
        await StudentDiscountService.ApplyAsync(db, s, Spec(15, groupId: eng.Id), Actor, null);
        await db.SaveChangesAsync();

        // Matematikaga YANGI chegirma — faqat matematikaning eski qatori yopiladi.
        await StudentDiscountService.ApplyAsync(db, s, Spec(50, groupId: mat.Id), Actor, null);
        await db.SaveChangesAsync();

        var active = await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Equal(50, Assert.Single(active, a => a.GroupId == mat.Id).Pct);
        Assert.Equal(15, Assert.Single(active, a => a.GroupId == eng.Id).Pct);   // TEGILMADI
        Assert.Equal(3, await db.StudentDiscounts.CountAsync(d => d.StudentId == s.Id));
    }

    [Fact]
    public async Task CancelAsync_FAQAT_shu_fanning_chegirmasini_yopadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        var matRow = await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id), Actor, null);
        await StudentDiscountService.ApplyAsync(db, s, Spec(15, groupId: eng.Id), Actor, null);
        await db.SaveChangesAsync();

        Assert.True(await StudentDiscountService.CancelAsync(db, s, matRow!, "Kerak emas", Actor));
        await db.SaveChangesAsync();

        var active = await db.StudentDiscounts
            .Where(d => d.StudentId == s.Id && d.Status == StudentDiscount.StatusActive).ToListAsync();
        Assert.Equal(eng.Id, Assert.Single(active).GroupId);
    }

    [Fact]
    public async Task ActiveInScopeAsync_BAND_qamrovni_topadi__409_shu_bilan_qaytariladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id), Actor, null);
        await db.SaveChangesAsync();

        Assert.NotNull(await StudentDiscountService.ActiveInScopeAsync(db, s.Id, mat.Id));
        Assert.Null(await StudentDiscountService.ActiveInScopeAsync(db, s.Id, eng.Id));
        Assert.Null(await StudentDiscountService.ActiveInScopeAsync(db, s.Id, null));   // «barcha guruhlar» bo'sh
    }

    // ==================== KO'ZGU (Student.Discount*) ====================

    [Fact]
    public async Task RefreshMirror_ASOSIY_qator_BARCHA_GURUHLAR_niki__u_bolmasa_eng_YANGISI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        // Faqat fan chegirmasi — ko'zguda o'sha turadi.
        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id, reason: "Mat"), Actor, null);
        await db.SaveChangesAsync();
        Assert.Equal(20, s.DiscountPct);
        Assert.Equal(mat.Id, s.DiscountGroupId);

        // «Barcha guruhlar» qo'shildi — ASOSIY endi O'SHA (guruh id'i yo'q).
        await StudentDiscountService.ApplyAsync(db, s, Spec(30, reason: "Hammasi"), Actor, null);
        await db.SaveChangesAsync();
        Assert.Equal(30, s.DiscountPct);
        Assert.Equal("Hammasi", s.DiscountNote);
        Assert.Null(s.DiscountGroupId);
    }

    [Fact]
    public async Task RefreshMirror_ESKI_MALUMOT__bitta_qator_bolsa_AYNAN_eski_qiymatlarni_beradi()
    {
        // ⚠️ ORQAGA MOSLIK: migratsiyadan keyin har o'quvchida BITTA qator bor —
        // ko'zgu o'sha qatorni AYNAN aks ettirishi kerak (hech narsa o'zgarmasin).
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var g = AddGroup(db);
        var s = AddStudent(db);
        db.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, StudentName = s.FullName, GroupId = g.Id,
            Pct = 35, Amount = 12_000m, StartMonth = M(-2), EndMonth = M(4),
            Reason = "Eski yozuv", Status = StudentDiscount.StatusActive,
            CreatedAt = "2026-01-01T00:00:00",
        });
        await db.SaveChangesAsync();

        await StudentDiscountService.RefreshMirrorAsync(db, s);

        Assert.Equal(35, s.DiscountPct);
        Assert.Equal(12_000m, s.DiscountAmount);
        Assert.Equal("Eski yozuv", s.DiscountNote);
        Assert.Equal(M(-2), s.DiscountStartMonth);
        Assert.Equal(M(4), s.DiscountEndMonth);
        Assert.Equal(g.Id, s.DiscountGroupId);
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
            foreach (var m in months)
                Assert.Equal(TuitionService.DiscountActiveForMonth(start, end, m), DiscountRules.InForce(d, m));
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

    // ==================== DiscountRules.Resolve (PUL QOIDASI) ====================

    private static StudentDiscount Row(
        int pct, string? groupId, string createdAt = "2026-01-01T00:00:00",
        string start = "", string end = "", string status = StudentDiscount.StatusActive) => new()
    {
        StudentId = "s1", Pct = pct, GroupId = groupId, CreatedAt = createdAt,
        StartMonth = start, EndMonth = end, Status = status,
    };

    [Fact]
    public void Resolve_ENG_ANIQ_moslik_GOLIB__shu_guruh_barcha_guruhlardan_ustun()
    {
        var rows = new List<StudentDiscount> { Row(10, null), Row(50, "g1") };

        Assert.Equal(50, DiscountRules.Resolve(rows, M(0), "g1")!.Pct);   // aynan shu fan
        Assert.Equal(10, DiscountRules.Resolve(rows, M(0), "g2")!.Pct);   // boshqa fan → umumiy
        Assert.Equal(10, DiscountRules.Resolve(rows, M(0), null)!.Pct);   // guruhsiz hisob → umumiy
    }

    [Fact]
    public void Resolve_chegirmalar_HECH_QACHON_QOSHILMAYDI()
    {
        // 40% (umumiy) + 60% (fan) qo'shilsa 100% bo'lib, oylik NOLGA tushib ketardi.
        // Faqat BITTASI qo'llanadi — eng aniqi.
        var rows = new List<StudentDiscount> { Row(40, null), Row(60, "g1") };
        var win = DiscountRules.Resolve(rows, M(0), "g1");

        Assert.Equal(60, win!.Pct);
        Assert.Equal(240_000m, TuitionService.DiscountForMonth(rows, 400_000m, M(0), "g1"));  // 60%, 100% EMAS
        Assert.Equal(160_000m, TuitionService.DiscountForMonth(rows, 400_000m, M(0), "g9"));  // 40%
    }

    [Fact]
    public void Resolve_bir_qamrovda_bir_nechta_qator_bolsa_ENG_YANGISI()
    {
        // Buzuq/qo'lda tuzatilgan ma'lumot — natija ro'yxat TARTIBIGA bog'liq bo'lmasligi kerak.
        var rows = new List<StudentDiscount>
        {
            Row(10, "g1", "2026-01-01T00:00:00"),
            Row(70, "g1", "2026-05-01T00:00:00"),
            Row(30, "g1", "2026-03-01T00:00:00"),
        };
        Assert.Equal(70, DiscountRules.Resolve(rows, M(0), "g1")!.Pct);
        rows.Reverse();
        Assert.Equal(70, DiscountRules.Resolve(rows, M(0), "g1")!.Pct);
    }

    [Fact]
    public void Resolve_davri_tugagan_FAN_chegirmasi_ornini_UMUMIYSIGA_beradi()
    {
        var rows = new List<StudentDiscount>
        {
            Row(10, null),
            Row(50, "g1", start: M(-3), end: M(-1)),   // davri o'tgan
        };
        Assert.Equal(10, DiscountRules.Resolve(rows, M(0), "g1")!.Pct);
        Assert.Equal(50, DiscountRules.Resolve(rows, M(-2), "g1")!.Pct);
    }

    [Fact]
    public void Resolve_TARIXIY_qatorlar_va_bosh_royxat_null()
    {
        Assert.Null(DiscountRules.Resolve([], M(0), "g1"));
        Assert.Null(DiscountRules.Resolve(null, M(0), "g1"));
        Assert.Null(DiscountRules.Resolve(
            [Row(50, "g1", status: StudentDiscount.StatusReplaced),
             Row(50, null, status: StudentDiscount.StatusCancelled)],
            M(0), "g1"));
    }

    [Fact]
    public void ActiveLabel_FAN_nomi_bilan_qisqa_yorliq()
    {
        var rows = new List<StudentDiscount>
        {
            new() { Pct = 20, GroupId = "g1", CourseName = "Matematika", Status = StudentDiscount.StatusActive },
            new() { Amount = 50_000m, GroupId = "g2", CourseName = "Ingliz tili", Status = StudentDiscount.StatusActive },
        };
        // Tartib: «barcha guruhlar» birinchi, keyin FAN nomi bo'yicha alifbo (natija barqaror
        // bo'lsin — qatorlar tartibi bazadan har xil kelishi mumkin).
        Assert.Equal("Ingliz tili 50 000 so'm, Matematika 20%", DiscountRules.ActiveLabel(rows));
        // «Barcha guruhlar» — nomi yo'q qator, ro'yxat boshida.
        Assert.Equal("Barcha guruhlar 15%", DiscountRules.ActiveLabel([Row(15, null)]));
    }

    // ==================== KITOB (DiscountBook) ====================

    [Fact]
    public async Task DiscountBook_har_FAN_uchun_OZ_chegirmasini_beradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id), Actor, null);
        await StudentDiscountService.ApplyAsync(db, s, Spec(0, 100_000m, groupId: eng.Id), Actor, null);
        await db.SaveChangesAsync();

        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
        Assert.Equal(100_000m, book.DiscountFor(s.Id, 500_000m, M(0), mat.Id));   // 20%
        Assert.Equal(100_000m, book.DiscountFor(s.Id, 500_000m, M(0), eng.Id));   // aniq summa
        Assert.Equal(0m, book.DiscountFor(s.Id, 500_000m, M(0), "boshqa"));       // chegirmasiz fan
        Assert.Equal(0m, book.DiscountFor("begona", 500_000m, M(0), mat.Id));     // boshqa o'quvchi
    }

    [Fact]
    public async Task DiscountBook_HALI_SAQLANMAGAN_qatorni_ham_koradi()
    {
        // ⚠️ Chegirma berilgandan keyin, SaveChanges'dan OLDIN joriy oy qayta hisoblanadi —
        // yangi qator ko'rinmasa "chegirma berdim, oylik o'zgarmadi" holati chiqardi.
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var g = AddGroup(db);
        var s = AddStudent(db);
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: g.Id), Actor, null);
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);   // SaveChanges QILINMAGAN

        Assert.Equal(100_000m, book.DiscountFor(s.Id, 500_000m, M(0), g.Id));
    }

    [Fact]
    public async Task ReapplyCurrentMonthAsync_HAR_GURUH_hisobiga_OZ_chegirmasini_qoyadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var mat = AddGroup(db, "Matematika A");
        var eng = AddGroup(db, "Ingliz A1");
        var s = AddStudent(db);
        s.Balance = 0m;
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = mat.Id, Month = M(0), Amount = 500_000m, Date = $"{M(0)}-01",
        });
        db.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = eng.Id, Month = M(0), Amount = 500_000m, Date = $"{M(0)}-01",
        });
        await db.SaveChangesAsync();

        await StudentDiscountService.ApplyAsync(db, s, Spec(20, groupId: mat.Id), Actor, null);
        await StudentDiscountService.ApplyAsync(db, s, Spec(10, groupId: eng.Id), Actor, null);
        Assert.True(await StudentDiscountService.ReapplyCurrentMonthAsync(db, s));
        await db.SaveChangesAsync();

        var charges = await db.MonthlyCharges.Where(c => c.StudentId == s.Id).ToListAsync();
        Assert.Equal(100_000m, charges.Single(c => c.GroupId == mat.Id).Discount);
        Assert.Equal(50_000m, charges.Single(c => c.GroupId == eng.Id).Discount);
        Assert.Equal(150_000m, s.Balance);
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
        var current = Assert.Single(res.Active);
        Assert.Equal("Ko'p bolali", current.Reason);
        Assert.True(current.InForce);
        Assert.Equal("Amalda", current.StatusLabel);
    }
}
