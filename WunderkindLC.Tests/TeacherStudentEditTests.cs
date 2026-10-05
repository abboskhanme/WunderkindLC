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

    private static TeacherStudentEdit.Input In(string? name = "Aliyev Vali", int? grade = null, string? phone = null) =>
        new(name, grade, phone);

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

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    public void Notogri_telefon_rad_etiladi(string phone) =>
        Assert.NotNull(TeacherStudentEdit.Validate(In(phone: phone)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("901234567")]
    [InlineData("+998 90 123 45 67")]
    public void Bosh_yoki_togri_telefon_qabul_qilinadi(string? phone) =>
        Assert.Null(TeacherStudentEdit.Validate(In(phone: phone)));

    [Fact]
    public void Ozgarmagan_eski_formatdagi_telefon_ismni_saqlashga_TOSIQ_EMAS()
    {
        // Xorijiy/eski raqam bor o'quvchining faqat ismini tuzatish mumkin bo'lishi kerak.
        const string foreign = "+7-916-123-45-67";
        Assert.NotNull(TeacherStudentEdit.Validate(In(phone: foreign)));
        Assert.Null(TeacherStudentEdit.Validate(In(phone: foreign), currentPhone: foreign));

        var s = new Student { FullName = "Eski", Phone = foreign };
        TeacherStudentEdit.Apply(s, In("Yangi Ism", phone: foreign), Today);
        Assert.Equal(foreign, s.Phone);
    }

    // ============================ Qo'llash ============================

    [Fact]
    public void Ism_tozalanadi_telefon_normallashadi()
    {
        var s = new Student { FullName = "Eski", Phone = "" };
        TeacherStudentEdit.Apply(s, In("  Aliyev   Vali  ", phone: "90 123 45 67"), Today);

        Assert.Equal("Aliyev Vali", s.FullName);
        Assert.Equal("+998-90-123-45-67", s.Phone);
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
    public void Faqat_uch_maydon_ozgaradi()
    {
        var s = new Student
        {
            FullName = "Eski", Phone = "+998-90-000-00-00", ParentFullName = "Ota", ParentPhone = "+998-91-111-11-11",
            Address = "Qo'qon", ClassName = "A guruh", Balance = -100_000m,
        };
        TeacherStudentEdit.Apply(s, In("Yangi Ism", grade: 3, phone: "901234567"), Today);

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
