using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QITUVCHI o'z guruhi o'quvchisini tahrirlashi (<see cref="TeacherStudentEdit"/>) va uning
/// ruxsat darvozasi (`TeacherPortalController.Students.cs`, manba matnidan — test loyihasi
/// Server'ga havola qilmaydi).
/// </summary>
public class TeacherStudentEditTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static TeacherStudentEdit.Input In(string? name = "Aliyev Vali", int? grade = null) =>
        new(name, grade);

    // ============================ Tekshiruv ============================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Ism_bosh_yoki_juda_qisqa_bolsa_rad_etiladi(string? name) =>
        Assert.NotNull(TeacherStudentEdit.Validate(In(name)));

    [Fact]
    public void Ism_juda_uzun_bolsa_rad_etiladi() =>
        Assert.NotNull(TeacherStudentEdit.Validate(In(new string('a', TeacherStudentEdit.MaxNameLength + 1))));

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void Notogri_sinf_rad_etiladi(int grade) =>
        Assert.NotNull(TeacherStudentEdit.Validate(In(grade: grade)));

    [Theory]
    [InlineData(0)]   // tozalash
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(SchoolGrades.Graduated)]
    public void Togri_sinf_qabul_qilinadi(int grade) =>
        Assert.Null(TeacherStudentEdit.Validate(In(grade: grade)));

    // ============================ Telefon — YOPIQ (H1) ============================

    [Fact]
    public void Tahrir_sorovida_TELEFON_maydoni_YOQ()
    {
        // H1 (2026-10-12): o'quvchi/ota-ona/ota/ona raqamlari — Telegram botida ro'yxatdan o'tish uchun moslash
        // maydoni (kirish omili). O'qituvchi ularni o'zgartira olmasin: kiruvchi DTO'da ham, qoida kirishida ham
        // telefon maydoni bo'lmasin.
        foreach (var t in new[] { typeof(WunderkindLC.Application.Dtos.TeacherStudentEditRequest), typeof(TeacherStudentEdit.Input) })
            Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Qollash_TORTTALA_raqamga_TEGMAYDI()
    {
        var s = new Student
        {
            FullName = "Eski", Phone = "+998-90-000-00-00", ParentPhone = "+998-91-111-11-11",
            FatherPhone = "+998-93-333-33-33", MotherPhone = "+998-94-444-44-44",
        };
        TeacherStudentEdit.Apply(s, In("Yangi Ism", grade: 3), Today);

        Assert.Equal("+998-90-000-00-00", s.Phone);
        Assert.Equal("+998-91-111-11-11", s.ParentPhone);
        Assert.Equal("+998-93-333-33-33", s.FatherPhone);
        Assert.Equal("+998-94-444-44-44", s.MotherPhone);
    }

    [Fact]
    public void Controller_telefonni_YOZMAYDI_va_Telegram_ulanishiga_TEGMAYDI()
    {
        var src = Controller("TeacherPortalController.Students.cs");
        var body = src[src.IndexOf("UpdateGroupStudent(", StringComparison.Ordinal)..];
        Assert.DoesNotContain("req.Phone", body);
        Assert.DoesNotMatch(new Regex(@"student\.(Phone|ParentPhone|FatherPhone|MotherPhone)\s*=[^=]"), body);
        Assert.DoesNotContain("TelegramLinks", body);
    }

    // ============================ Qo'llash ============================

    [Fact]
    public void Ism_tozalanadi()
    {
        var s = new Student { FullName = "Eski", Phone = "" };
        TeacherStudentEdit.Apply(s, In("  Aliyev   Vali  "), Today);

        Assert.Equal("Aliyev Vali", s.FullName);
        Assert.Equal("", s.Phone);
    }

    [Fact]
    public void Sinf_null_bolsa_TEGILMAYDI()
    {
        var s = new Student { SchoolGrade = 5, SchoolGradeYear = 2025 };
        TeacherStudentEdit.Apply(s, In(grade: null), Today);

        Assert.Equal(5, s.SchoolGrade);
        Assert.Equal(2025, s.SchoolGradeYear);
    }

    [Fact]
    public void Sinf_0_bolsa_TOZALANADI()
    {
        var s = new Student { SchoolGrade = 5, SchoolGradeYear = 2025 };
        TeacherStudentEdit.Apply(s, In(grade: 0), Today);

        Assert.Null(s.SchoolGrade);
        Assert.Null(s.SchoolGradeYear);
    }

    [Fact]
    public void Sinf_JORIY_oquv_yiliga_boglab_yoziladi()
    {
        // Admin formasi bilan bir xil qoida: keyingi yillarda sinf o'zi oshadi (school-grade.md §2).
        var s = new Student();
        TeacherStudentEdit.Apply(s, In(grade: 7), Today);

        Assert.Equal(7, s.SchoolGrade);
        Assert.Equal(SchoolGrades.AcademicYear(Today), s.SchoolGradeYear);
        Assert.Equal(7, SchoolGrades.Current(s.SchoolGrade, s.SchoolGradeYear, Today));
    }

    [Fact]
    public void Faqat_ikki_maydon_ozgaradi()
    {
        var s = new Student
        {
            FullName = "Eski", Phone = "+998-90-000-00-00", ParentFullName = "Ota", ParentPhone = "+998-91-111-11-11",
            Address = "Qo'qon", ClassName = "A guruh", Balance = -100_000m,
        };
        TeacherStudentEdit.Apply(s, In("Yangi Ism", grade: 3), Today);

        Assert.Equal("+998-90-000-00-00", s.Phone);
        Assert.Equal("Ota", s.ParentFullName);
        Assert.Equal("+998-91-111-11-11", s.ParentPhone);
        Assert.Equal("Qo'qon", s.Address);
        Assert.Equal("A guruh", s.ClassName);
        Assert.Equal(-100_000m, s.Balance);
    }

    // ============================ Ruxsat (manba matnidan) ============================

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

    [Fact]
    public void Ikkala_endpoint_ham_jurnal_ruxsati_va_GURUH_EGASINI_tekshiradi()
    {
        var main = Controller("TeacherPortalController.cs");
        Assert.Contains("[Authorize(Roles = \"teacher\")]", main);

        var src = Controller("TeacherPortalController.Students.cs");
        Assert.DoesNotContain("AllowAnonymous", src);
        foreach (var route in new[] { "[HttpGet(\"groups/{classId}/students\")]", "[HttpPut(\"groups/{classId}/students/{studentId}\")]" })
        {
            var i = src.IndexOf(route, StringComparison.Ordinal);
            Assert.True(i > 0, $"{route} topilmadi");
            var end = src.IndexOf("[Http", i + route.Length, StringComparison.Ordinal);
            var body = end > 0 ? src[i..end] : src[i..];
            Assert.Contains("TeacherPermissions.Journal", body);
            Assert.Contains("OwnsGroupAsync(me.Id, classId)", body);
        }
        // Guruh egasi — o'rinbosar EMAS (OwnedBy), va o'quvchi shu guruhning TIRIK a'zosi.
        Assert.Contains("TeacherGroupAccess.OwnedBy", src);
        Assert.DoesNotContain("CanSubstituteWriteAsync", src);
        Assert.Contains("sg.IsActive", src[src.IndexOf("UpdateGroupStudent", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void Oqituvchi_portalida_azo_QOSHISH_CHIQARISH_yoli_YOQ()
    {
        // Foydalanuvchi talabi (2026-10-05): o'qituvchi guruhga o'quvchi qo'sha olmaydi, chiqara
        // olmaydi. Bunday endpoint qo'shilsa — test qizaradi va qaror qayta ko'rib chiqiladi.
        var dir = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers");
        foreach (var file in Directory.GetFiles(dir, "TeacherPortalController*.cs"))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotMatch(new Regex(@"\[Http(Post|Put|Delete|Patch)\(""[^""]*(members|freeze|activate|transfer|archive)", RegexOptions.IgnoreCase), src);
            Assert.DoesNotContain("db.StudentGroups.Add", src);
            Assert.DoesNotContain("db.StudentGroups.Remove", src);
            Assert.DoesNotContain("db.Students.Add", src);
        }
    }
}
