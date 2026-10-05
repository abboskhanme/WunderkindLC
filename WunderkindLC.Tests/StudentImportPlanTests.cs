using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using static WunderkindLC.Application.Services.StudentImportPlan;

namespace WunderkindLC.Tests;

/// <summary>
/// Excel'dan o'quvchi importi — qator qarorlari (<see cref="StudentImportPlan"/>). Asosiy xavf:
/// bitta o'quvchi ikki guruh ro'yxatida bo'lsa (yoki fayl qayta yuklansa) IKKI marta yaratilishi
/// (2026-09-30 da 22 ta dublikat aynan shunday paydo bo'lgan) va ismdosh bolalarning jimgina
/// birlashib ketishi.
/// </summary>
public class StudentImportPlanTests
{
    private static Row R(int n, string name, string phone = "", string group = "", string grade = "", string fatherPhone = "") =>
        new(n, name, phone, "", fatherPhone, "", "", group, "", "", "", grade);

    private static List<Step> Plan(IEnumerable<Row> rows, IEnumerable<Existing>? existing = null,
        IEnumerable<(string, string, bool)>? memberships = null, params string[] groups) =>
        Build(rows.ToList(), existing ?? [], memberships ?? [], groups.Length == 0 ? ["A guruh", "B guruh"] : groups);

    // ============================ Sinf ustuni ============================

    [Theory]
    [InlineData("7", 7)]
    [InlineData("7-sinf", 7)]
    [InlineData(" 7 sinf ", 7)]
    [InlineData("7 класс", 7)]
    [InlineData("7.0", 7)]     // Excel raqamli katak
    [InlineData("11", 11)]
    [InlineData("12", 12)]
    [InlineData("bitirgan", 12)]
    [InlineData("Maktabni bitirgan", 12)]
    [InlineData("tugatgan", 12)]
    public void Sinf_turli_yozuvlardan_oqiladi(string raw, int expected)
    {
        Assert.True(TryParseGrade(raw, out var g));
        Assert.Equal(expected, g);
    }

    [Fact]
    public void Bosh_sinf_xato_emas_kiritilmagan()
    {
        Assert.True(TryParseGrade("  ", out var g));
        Assert.Null(g);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("yettinchi")]
    [InlineData("-")]
    [InlineData("7a")]          // harfli sinf markazda yo'q — xato yozuv
    [InlineData("8-B")]
    public void Notogri_sinf_rad_etiladi(string raw) => Assert.False(TryParseGrade(raw, out _));

    [Fact]
    public void Notogri_sinf_qator_xatosi_boladi_boshqa_qatorlar_davom_etadi()
    {
        var steps = Plan([R(2, "Aliyev Vali", grade: "13"), R(3, "Karimov Sobir", grade: "8")]);
        Assert.Equal(Kind.Error, steps[0].Kind);
        Assert.Contains("Sinf noto'g'ri", steps[0].Message);
        Assert.Equal(Kind.Create, steps[1].Kind);
        Assert.Equal(8, steps[1].Grade);
    }

    // ============================ Dublikat ============================

    [Fact]
    public void Bazadagi_oquvchi_ISM_va_TELEFON_mos_bolsa_YARATILMAYDI_guruhga_qoshiladi()
    {
        var existing = new Existing("s1", "Aliyev  Vali", false, null, "+998-90-123-45-67");
        var steps = Plan([R(2, "aliyev vali", "901234567", "B guruh")], [existing], [("s1", "A guruh", true)]);

        var s = Assert.Single(steps);
        Assert.Equal(Kind.Join, s.Kind);
        Assert.Equal("s1", s.ExistingId);
    }

    [Fact]
    public void Ota_telefoni_orqali_ham_taniladi()
    {
        var existing = new Existing("s1", "Aliyev Vali", false, null, "", "+998-91-111-11-11");
        var steps = Plan([R(2, "Aliyev Vali", fatherPhone: "911111111", group: "A guruh")], [existing]);
        Assert.Equal(Kind.Join, Assert.Single(steps).Kind);
    }

    [Fact]
    public void Allaqachon_shu_guruhda_bolsa_otkaziladi()
    {
        var existing = new Existing("s1", "Aliyev Vali", false, null, "901234567");
        var steps = Plan([R(2, "Aliyev Vali", "901234567", "A guruh")], [existing], [("s1", "A guruh", true)]);
        var s = Assert.Single(steps);
        Assert.Equal(Kind.Skip, s.Kind);
        Assert.Contains("allaqachon", s.Message);
    }

    [Fact]
    public void Guruhdan_chiqqan_bolsa_JIMGINA_qaytarilmaydi()
    {
        var existing = new Existing("s1", "Aliyev Vali", false, null, "901234567");
        var steps = Plan([R(2, "Aliyev Vali", "901234567", "A guruh")], [existing], [("s1", "A guruh", false)]);
        var s = Assert.Single(steps);
        Assert.Equal(Kind.Skip, s.Kind);
        Assert.Contains("chiqqan", s.Message);
    }

    [Fact]
    public void Faqat_ISM_mos_bolsa_YARATILADI_lekin_OGOHLANTIRILADI()
    {
        // Ismdosh bolalar jimgina birlashib ketmasin.
        var existing = new Existing("s1", "Aliyev Vali", false, null, "901234567");
        var steps = Plan([R(2, "Aliyev Vali", "937777777", "A guruh")], [existing]);
        var s = Assert.Single(steps);
        Assert.Equal(Kind.Create, s.Kind);
        Assert.NotNull(s.Warning);
    }

    [Fact]
    public void Faylning_ozidagi_takror_IKKINCHI_marta_yaratilmaydi_ikkinchi_guruhga_qoshiladi()
    {
        // 2026-09-30 dagi holat: o'quvchi ikki guruh ro'yxatida.
        var steps = Plan([R(2, "Aliyev Vali", "901234567", "A guruh"), R(3, "Aliyev Vali", "901234567", "B guruh")]);
        Assert.Equal(Kind.Create, steps[0].Kind);
        Assert.Equal(Kind.Join, steps[1].Kind);
        Assert.Equal(steps[0].NewIndex, steps[1].NewIndex);
        Assert.Null(steps[1].ExistingId);
    }

    [Fact]
    public void Fayl_QAYTA_yuklansa_hech_kim_ikki_marta_yaratilmaydi()
    {
        var existing = new Existing("s1", "Aliyev Vali", false, null, "901234567");
        var steps = Plan([R(2, "Aliyev Vali", "901234567", "A guruh")], [existing], [("s1", "A guruh", true)]);
        Assert.DoesNotContain(steps, s => s.Kind == Kind.Create);
    }

    [Fact]
    public void Arxivdagi_oquvchi_bilan_mos_kelsa_xato_tiklash_kerak()
    {
        var existing = new Existing("s1", "Aliyev Vali", true, null, "901234567");
        var steps = Plan([R(2, "Aliyev Vali", "901234567", "A guruh")], [existing]);
        var s = Assert.Single(steps);
        Assert.Equal(Kind.Error, s.Kind);
        Assert.Contains("arxiv", s.Message);
    }

    [Fact]
    public void Mavjud_oquvchida_sinf_YOQ_bolsa_toldiriladi_bor_bolsa_TEGILMAYDI()
    {
        var noGrade = new Existing("s1", "Aliyev Vali", false, null, "901234567");
        var withGrade = new Existing("s2", "Karimov Sobir", false, 5, "902222222");
        var steps = Plan(
            [R(2, "Aliyev Vali", "901234567", "A guruh", "7"), R(3, "Karimov Sobir", "902222222", "A guruh", "9")],
            [noGrade, withGrade]);
        Assert.True(steps[0].FillGrade);
        Assert.False(steps[1].FillGrade);
    }

    // ============================ Boshqa ============================

    [Fact]
    public void Topilmagan_guruh_va_bosh_FISH_xato_bosh_qator_otkaziladi()
    {
        var steps = Plan([R(2, "Aliyev Vali", group: "Yo'q guruh"), R(3, "", "901234567"), R(4, "")]);
        Assert.Equal(Kind.Error, steps[0].Kind);
        Assert.Contains("Guruh topilmadi", steps[0].Message);
        Assert.Equal(Kind.Error, steps[1].Kind);
        Assert.Equal(Kind.Skip, steps[2].Kind);
    }

    [Fact]
    public void Ism_ortiqcha_boshliqsiz_saqlanadi() =>
        Assert.Equal("Aliyev Vali", CleanName("  Aliyev    Vali "));

    [Fact]
    public void Ism_kaliti_apostrof_va_bosh_joydan_qati_nazar()
    {
        Assert.Equal(NameKey("Ulug'bekov  Shohjahon"), NameKey("ulugʻbekov shohjahon"));
        Assert.Equal(NameKey("Ulug`bekov Shohjahon"), NameKey("ULUG’BEKOV SHOHJAHON "));
    }
}
