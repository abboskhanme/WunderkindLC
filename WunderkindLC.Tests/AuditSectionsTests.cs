using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'ZGARISHLAR TARIXINI BO'LIMLARGA AJRATISH qoidasi (<see cref="AuditSections"/>).
///
/// <para>Bu xarita "Sozlamalar → O'zgarishlar tarixi" sahifasining butun mantig'i: chiplar,
/// sanoq va <c>?section=</c> filtri shundan. Xato xarita = yozuv ko'rinmay qolishi.</para>
/// </summary>
public class AuditSectionsTests
{
    [Theory]
    // Nomi ALDAMCHI turlar — xarita nom bo'yicha emas, MAZMUN bo'yicha tuzilgan.
    [InlineData("StudentDiscount", "students")]   // chegirma + arxiv + login bloklash + qo'lda hisob
    [InlineData("ClassFee", "classes")]           // guruh oyligi + guruhni arxivlash
    [InlineData("TeacherSalary", "teachers")]     // maosh + o'qituvchi yozuvining o'zi
    // To'g'ridan-to'g'ri turlar.
    [InlineData("Student", "students")]
    [InlineData("ContactRequest", "contacts")]
    [InlineData("Group", "classes")]
    [InlineData("Membership", "classes")]
    [InlineData("FinanceTransaction", "finance")]
    [InlineData("Lead", "leads")]
    [InlineData("Course", "schedule")]
    [InlineData("Book", "books")]
    [InlineData("Contract", "contracts")]
    [InlineData("Vacancy", "vacancies")]
    [InlineData("Staff", "staff")]
    [InlineData("CenterMeta", "settings")]
    public void SectionOf_turni_togri_bolimga_soladi(string entityType, string expected)
        => Assert.Equal(expected, AuditSections.SectionOf(entityType));

    [Theory]
    [InlineData("AllaqachonYoq")]
    [InlineData("")]
    [InlineData(null)]
    public void SectionOf_notanish_tur_BOSHQA_bolimiga_tushadi(string? entityType)
        => Assert.Equal(AuditSections.Other, AuditSections.SectionOf(entityType));

    [Fact]
    public void Har_bir_bolim_All_royxatida_bor()
    {
        // Xaritada bor har bir tur UI ro'yxatidagi bo'limga tushishi shart — aks holda yozuv
        // hech qaysi chipda ko'rinmay qolardi.
        foreach (var type in AuditSections.KnownEntityTypes)
        {
            var section = AuditSections.SectionOf(type);
            Assert.True(
                AuditSections.All.Any(s => s.Key == section),
                $"'{type}' → '{section}' bo'limi AuditSections.All da yo'q");
        }
    }

    [Fact]
    public void EntityTypesOf_bolimning_barcha_turlarini_qaytaradi()
    {
        var classes = AuditSections.EntityTypesOf("classes");
        Assert.Contains("Group", classes);
        Assert.Contains("Membership", classes);
        Assert.Contains("ClassFee", classes);
        Assert.DoesNotContain("Student", classes);
    }

    [Fact]
    public void EntityTypesOf_notanish_va_BOSHQA_uchun_bosh_qaytaradi()
    {
        // Bo'sh ro'yxat = "filtr qo'ymang". "Boshqa" esa ro'yxat bilan emas, xaritada YO'Q
        // turlar sifatida (KnownEntityTypes teskarisi) filtrlanadi — AuditController shunday qiladi.
        Assert.Empty(AuditSections.EntityTypesOf("yoq-bunday-bolim"));
        Assert.Empty(AuditSections.EntityTypesOf(""));
        Assert.Empty(AuditSections.EntityTypesOf(AuditSections.Other));
    }

    [Fact]
    public void IsKnownSection_faqat_royxatdagini_tan_oladi()
    {
        Assert.True(AuditSections.IsKnownSection("finance"));
        Assert.True(AuditSections.IsKnownSection(AuditSections.Other));
        Assert.False(AuditSections.IsKnownSection("moliya"));
        Assert.False(AuditSections.IsKnownSection(null));
    }

    [Fact]
    public void Bolim_kalitlari_takrorlanmaydi()
        => Assert.Equal(AuditSections.All.Count, AuditSections.All.Select(s => s.Key).Distinct().Count());
}
