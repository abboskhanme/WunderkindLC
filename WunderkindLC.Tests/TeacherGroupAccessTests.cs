using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// GURUH O'QITUVCHIGA KO'RINADIMI (<see cref="TeacherGroupAccess"/>).
///
/// <para>Qoida ikki joyda ishlaydi: ro'yxat so'rovida (SQL — <c>VisibleQuery</c>) va guruh ichiga
/// kirishda (xotirada — <c>Visible</c>/<c>OwnedBy</c>). Ular AYRILIB KETSA foydalanuvchi buni
/// "guruh ro'yxatda yo'q, lekin eski havola bilan ochiladi" (yoki teskarisi) ko'rinishida ko'rardi,
/// shuning uchun bu yerda ikkalasi bir xil holatlar to'plamida solishtiriladi.</para>
/// </summary>
public class TeacherGroupAccessTests
{
    private static Group Make(bool archived = false, bool blocked = false, string status = "active") =>
        new() { Id = "g1", Name = "A1", TeacherId = "t1", Status = status, IsArchived = archived, IsBlocked = blocked };

    /// <summary>Barcha kombinatsiyalar: (arxiv × blok × holat).</summary>
    public static TheoryData<bool, bool, string> AllCases()
    {
        var data = new TheoryData<bool, bool, string>();
        foreach (var archived in new[] { false, true })
            foreach (var blocked in new[] { false, true })
                foreach (var status in new[] { "active", "full", "archived" })
                    data.Add(archived, blocked, status);
        return data;
    }

    [Fact]
    public void Faol_guruh_koinadi()
    {
        var g = Make();
        Assert.Null(TeacherGroupAccess.HiddenReason(g));
        Assert.True(TeacherGroupAccess.Visible(g));
        Assert.True(TeacherGroupAccess.OwnedBy(g, "t1"));
    }

    [Fact]
    public void Arxivlangan_guruh_yashiriladi()
        => Assert.Equal(TeacherGroupAccess.ReasonArchived,
            TeacherGroupAccess.HiddenReason(Make(archived: true)));

    /// <summary>"Tugatish (sertifikat bilan)" va "Guruhni yopish" ikkalasi ham
    /// <c>Status="archived"</c> qo'yadi — bayroq qo'lda tozalansa ham guruh chiqib ketmasin.</summary>
    [Fact]
    public void Holati_archived_bolgan_guruh_bayroqsiz_ham_yashiriladi()
        => Assert.Equal(TeacherGroupAccess.ReasonArchived,
            TeacherGroupAccess.HiddenReason(Make(status: "archived")));

    [Fact]
    public void Vaqtincha_bloklangan_guruh_yashiriladi()
        => Assert.Equal(TeacherGroupAccess.ReasonBlocked,
            TeacherGroupAccess.HiddenReason(Make(blocked: true)));

    /// <summary>Ikkalasi bo'lsa BLOK ustun — admin ataylab qilgan amal tushuntirishga muhimroq.</summary>
    [Fact]
    public void Blok_arxivdan_ustun()
        => Assert.Equal(TeacherGroupAccess.ReasonBlocked,
            TeacherGroupAccess.HiddenReason(Make(archived: true, blocked: true)));

    [Fact]
    public void Begona_guruh_ochilmaydi()
    {
        var g = Make();
        Assert.False(TeacherGroupAccess.OwnedBy(g, "boshqa-oqituvchi"));
        Assert.False(TeacherGroupAccess.OwnedBy(g, ""));
    }

    /// <summary>O'qituvchisi biriktirilmagan guruh (TeacherId="") hech kimniki emas —
    /// bo'sh id bilan tasodifan mos kelib qolmasin.</summary>
    [Fact]
    public void Oqituvchisiz_guruh_hech_kimniki_emas()
    {
        var g = Make();
        g.TeacherId = "";
        Assert.False(TeacherGroupAccess.OwnedBy(g, ""));
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Royxat_sorovi_va_xotiradagi_qoida_bir_xil(bool archived, bool blocked, string status)
    {
        var g = Make(archived, blocked, status);
        var inSql = TeacherGroupAccess.VisibleQuery.Compile()(g);
        Assert.Equal(TeacherGroupAccess.Visible(g), inSql);
    }
}
