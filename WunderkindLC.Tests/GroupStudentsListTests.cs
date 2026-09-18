using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// "Guruh → Guruh o'quvchilari" ro'yxatining filtr/sahifalash qoidasi (<see cref="GroupStudentsList"/>).
/// <c>ClassesController.Memberships</c> AYNAN shu funksiyani chaqiradi — qoida takrorlanmaydi.
/// </summary>
public class GroupStudentsListTests
{
    private static GroupStudentsList.Row R(
        string student, string name, string group = "g1", string status = "active",
        string teacher = "t1", bool archived = false, string joined = "2026-09-01", bool isActive = true,
        bool yearFreeze = false) =>
        new($"m-{student}-{group}", student, name, group, $"Guruh {group}", archived,
            teacher, $"O'qituvchi {teacher}", status, yearFreeze, joined, isActive);

    private static readonly GroupStudentsList.Row[] Sample =
    [
        R("s1", "Ali Valiyev", status: "active", joined: "2026-09-10"),
        R("s2", "Bobur Karimov", status: "trial", joined: "2026-09-12"),
        R("s3", "Dilnoza Aliyeva", status: "frozen", joined: "2026-08-01"),
        R("s4", "Eldor Sobirov", status: "frozen", yearFreeze: true, joined: "2026-06-01"),
        R("s5", "Farruh Nazarov", status: "completed", isActive: false, joined: "2026-01-01"),
        R("s6", "G'ayrat Umarov", group: "g2", teacher: "t2", archived: true, status: "active", joined: "2026-05-05"),
        // Bir o'quvchi ikki guruhda — ikkala a'zolik ALOHIDA qator.
        R("s1", "Ali Valiyev", group: "g2", teacher: "t2", status: "trial", joined: "2026-09-15"),
    ];

    [Fact]
    public void Standart_holatda_muzlatilgan_va_chiqqanlar_yoq()
    {
        var res = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query());
        Assert.Equal(4, res.Total);
        Assert.DoesNotContain(res.Items, r => r.Status == "frozen");
        Assert.DoesNotContain(res.Items, r => !r.IsActive);
    }

    [Fact]
    public void Muzlatilgan_kaliti_faqat_muzlatilganlarni_beradi_aktiv_muzlatish_ham()
    {
        var res = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query(Frozen: true));
        Assert.Equal(["s3", "s4"], res.Items.Select(r => r.StudentId).Order().ToArray());
    }

    [Fact]
    public void Bir_oquvchi_ikki_guruhda_ikki_qator()
    {
        var res = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query(Search: "ali val"));
        Assert.Equal(2, res.Items.Count(r => r.StudentId == "s1"));
    }

    [Fact]
    public void Oqituvchi_va_guruh_holati_filtri()
    {
        var byTeacher = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query(TeacherId: "t2"));
        Assert.All(byTeacher.Items, r => Assert.Equal("t2", r.TeacherId));
        Assert.Equal(2, byTeacher.Total);

        var archived = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query(GroupState: "archived"));
        Assert.Equal(["s6"], archived.Items.Select(r => r.StudentId).ToArray());

        var active = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query(GroupState: "active"));
        Assert.DoesNotContain(active.Items, r => r.GroupArchived);
    }

    [Fact]
    public void Sana_oraligi_chegaralarni_oz_ichiga_oladi()
    {
        var res = GroupStudentsList.Apply(Sample,
            new GroupStudentsList.Query(From: "2026-09-10", To: "2026-09-12"));
        Assert.Equal(["s1", "s2"], res.Items.Select(r => r.StudentId).Order().ToArray());
    }

    [Fact]
    public void Eng_yangi_qoshilgan_tepada()
    {
        var res = GroupStudentsList.Apply(Sample, new GroupStudentsList.Query());
        Assert.Equal("2026-09-15", res.Items[0].JoinedAt);
    }

    [Fact]
    public void Sahifalash_va_notogri_qiymatlar_chegaralanadi()
    {
        var many = Enumerable.Range(1, 120).Select(i => R($"s{i:000}", $"O'quvchi {i:000}")).ToList();

        var p2 = GroupStudentsList.Apply(many, new GroupStudentsList.Query(Page: 2, PageSize: 50));
        Assert.Equal(120, p2.Total);
        Assert.Equal(2, p2.Page);
        Assert.Equal(50, p2.Items.Count);

        // Oxirgi sahifadan narisi — oxirgi sahifaga tushadi (bo'sh ekran chiqmasin).
        var tooFar = GroupStudentsList.Apply(many, new GroupStudentsList.Query(Page: 99, PageSize: 50));
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(20, tooFar.Items.Count);

        // Hajm 0/manfiy/juda katta — chegaralanadi.
        Assert.Equal(1, GroupStudentsList.Apply(many, new GroupStudentsList.Query(PageSize: 0)).PageSize);
        Assert.Equal(GroupStudentsList.MaxPageSize,
            GroupStudentsList.Apply(many, new GroupStudentsList.Query(PageSize: 100_000)).PageSize);
    }

    [Fact]
    public void Bosh_royxat_bir_sahifa()
    {
        var res = GroupStudentsList.Apply([], new GroupStudentsList.Query(Page: 5));
        Assert.Equal(0, res.Total);
        Assert.Equal(1, res.Page);
        Assert.Empty(res.Items);
    }
}

/// <summary>
/// "Guruh o'quvchilari" va guruh sahifasi "O'quvchilar" tabi endpointlarining RUXSAT darvozasi.
/// Test loyihasi Server'ga havola qilmaydi — darvoza manba matnidan tekshiriladi
/// (<see cref="SensitiveReadPermTests"/>, <see cref="HomeEndpointsRbacTests"/> bilan bir xil naqsh).
/// </summary>
public class GroupStudentsRbacTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
        var path = Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "ClassesController.cs");
        Assert.True(File.Exists(path), $"Controller fayli topilmadi: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Metod e'lonidan OLDINGI atributlar bloki (oldingi metod tanasi tugagan joydan).</summary>
    private static string AttributesOf(string src, string method)
    {
        var i = src.IndexOf($"> {method}(", StringComparison.Ordinal);
        Assert.True(i > 0, $"{method} topilmadi");
        var start = src.LastIndexOf("    }", i, StringComparison.Ordinal);
        return src[start..i];
    }

    [Fact]
    public void Sinf_darajasida_guruhlar_royxati_ruxsati()
    {
        var src = Source();
        var header = src[..src.IndexOf("public class ClassesController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"classes.list\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
    }

    [Theory]
    [InlineData("Memberships", "[HttpGet(\"memberships\")]")]
    [InlineData("Roster", "[HttpGet(\"{id}/roster\")]")]
    public void Yangi_endpointlar_sinf_darvozasini_bekor_qilmaydi(string method, string route)
    {
        var attrs = AttributesOf(Source(), method);
        Assert.Contains(route, attrs);
        // Metod darajasidagi [AdminPerm] sinfdagisini BEKOR qiladi (permissions.md §4) —
        // bu yerda boshqa kalit qo'yilmagan bo'lishi kerak; anonim kirish ham yo'q.
        Assert.DoesNotMatch(new Regex(@"\[AdminPerm\("), attrs);
        Assert.DoesNotContain("AllowAnonymous", attrs);
    }

    /// <summary>Roster javobidagi NOZIK maydonlar javobning o'zida darvozalanadi.</summary>
    [Fact]
    public void Roster_narx_va_izohni_ruxsatga_qarab_beradi()
    {
        var src = Source();
        var start = src.IndexOf("> Roster(", StringComparison.Ordinal);
        var end = src.IndexOf("[HttpGet(\"memberships\")]", start, StringComparison.Ordinal);
        var body = src[start..end];
        Assert.Contains("CanSeeFinance()", body);
        Assert.Contains("HasSectionAccess(User, \"students.list\")", body);
    }
}
