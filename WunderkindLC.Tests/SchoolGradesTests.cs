using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHINING MAKTAB SINFI. Asosiy talab: "nechanchi sinflar o'qiyapti" statistikasi har yili
/// qo'lda tuzatilmasdan TO'G'RI qolsin — ya'ni sinf 1-sentabrda o'zi oshadi.
/// </summary>
public class SchoolGradesTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Theory]
    [InlineData("2026-09-01", 2026)] // yangi o'quv yili shu kuni boshlanadi
    [InlineData("2026-12-31", 2026)]
    [InlineData("2027-05-25", 2026)] // bahor — hali o'tgan sentyabrdagi yil
    [InlineData("2027-08-31", 2026)] // yozgi ta'til — yil hali almashmagan
    [InlineData("2027-09-01", 2027)]
    public void AcademicYear_sentabrda_almashadi(string date, int expected) =>
        Assert.Equal(expected, SchoolGrades.AcademicYear(D(date)));

    [Fact]
    public void Kiritilgan_yilning_ozida_sinf_ozgarmaydi() =>
        Assert.Equal(7, SchoolGrades.Current(7, 2026, D("2027-05-10")));

    [Fact]
    public void Keyingi_sentabrda_sinf_BIRGA_oshadi()
    {
        Assert.Equal(7, SchoolGrades.Current(7, 2026, D("2027-08-31")));
        Assert.Equal(8, SchoolGrades.Current(7, 2026, D("2027-09-01")));
        Assert.Equal(10, SchoolGrades.Current(7, 2026, D("2029-10-01")));
    }

    [Fact]
    public void Onbirinchi_sinfdan_keyin_BITIRGAN_va_undan_osmaydi()
    {
        Assert.Equal(SchoolGrades.Graduated, SchoolGrades.Current(11, 2026, D("2027-09-01")));
        Assert.Equal(SchoolGrades.Graduated, SchoolGrades.Current(10, 2026, D("2035-09-01")));
        Assert.Equal(SchoolGrades.Graduated, SchoolGrades.Current(SchoolGrades.Graduated, 2020, D("2030-01-01")));
    }

    [Fact]
    public void Kiritilmagan_yoki_buzuq_sinf_NULL()
    {
        Assert.Null(SchoolGrades.Current(null, 2026, D("2026-10-01")));
        Assert.Null(SchoolGrades.Current(0, 2026, D("2026-10-01")));
        Assert.Null(SchoolGrades.Current(13, 2026, D("2026-10-01")));
        Assert.Null(SchoolGrades.Current(-1, 2026, D("2026-10-01")));
    }

    [Fact]
    public void Yili_yoq_qator_ozgarishsiz_qoladi() =>
        Assert.Equal(5, SchoolGrades.Current(5, null, D("2030-10-01")));

    [Fact]
    public void Kelajak_yili_yozilgan_buzuq_qator_0_sinf_bermaydi() =>
        Assert.Equal(SchoolGrades.Min, SchoolGrades.Current(2, 2030, D("2026-10-01")));

    [Theory]
    [InlineData(1, true)]
    [InlineData(11, true)]
    [InlineData(12, true)] // bitirgan
    [InlineData(0, false)]
    [InlineData(13, false)]
    public void IsValid_1_dan_bitirgangacha(int grade, bool ok) =>
        Assert.Equal(ok, SchoolGrades.IsValid(grade));

    [Fact]
    public void Entity_SchoolGradeNow_bugungi_sinfni_beradi()
    {
        var year = SchoolGrades.AcademicYear(AppClock.Today);
        var s = new Student { SchoolGrade = 5, SchoolGradeYear = year - 2 };
        Assert.Equal(7, s.SchoolGradeNow);
        Assert.Null(new Student().SchoolGradeNow);
    }
}
