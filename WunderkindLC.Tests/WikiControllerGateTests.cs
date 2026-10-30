using System.Text.RegularExpressions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// BILIMLAR BAZASI controllerlarining ruxsat darvozalari — MANBA MATNI bo'yicha (test loyihasi
/// <c>WunderkindLC.Server</c> ga havola qilmaydi, <c>OnboardingRbacTests</c> bilan bir xil sabab).
/// Kimdir darvozani olib tashlasa, kalitni kengaytirsa yoki <c>[AllowAnonymous]</c> qo'shsa — qizaradi.
/// </summary>
public class WikiControllerGateTests
{
    private static string SourcePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi topilmadi");
        return Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "Wiki", file);
    }

    private static string Source(string file) => File.ReadAllText(SourcePath(file));

    private static int Count(string src, string literal) => Regex.Matches(src, Regex.Escape(literal)).Count;

    [Fact]
    public void WikiTeamController_OQISH_ham_darvozalangan_va_ochiq_endpoint_yoq()
    {
        var src = Source("WikiTeamController.cs");
        var header = src[..src.IndexOf("public class WikiTeamController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"wiki.team\", ReadRequiresPerm = true)]", header);
        Assert.DoesNotContain("AllowAnonymous", src);
        // Metod darajasidagi [AdminPerm] sinfdagini bekor qilardi (permissions.md §4).
        Assert.Equal(1, Count(src, "[AdminPerm("));
    }

    [Fact]
    public void Umumiy_bolimlar_moduli_butunlay_olib_tashlangan()
    {
        // Egasining qarori (2026-10-25): «Umumiy» (SOP bo'limlari) — hamma yog'i o'chirildi. Qaytib kelmasin.
        Assert.Equal(new[] { "MyNotesController.cs", "WikiTeamController.cs" },
            Directory.GetFiles(Path.GetDirectoryName(SourcePath("MyNotesController.cs"))!, "*.cs").Select(Path.GetFileName).Order());
    }

    [Fact]
    public void MyNotesController_faqat_oz_eslatmalari_egasi_tokendan_tarixga_yozilmaydi()
    {
        var src = Source("MyNotesController.cs");
        Assert.Contains("[Authorize]", src);
        Assert.Contains("[NoStore]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("p.OwnerUserId == UserId && p.OwnerUserId != \"\"", src);
        Assert.Contains("ClaimTypes.NameIdentifier", src);
        Assert.DoesNotContain("audit.Record", src);
        // Har endpoint ruxsatni tekshiradi.
        var endpoints = Regex.Matches(src, @"\[Http(Get|Post|Put|Delete)").Count;
        Assert.Equal(endpoints, Count(src, "if (!Allowed) return NoAccess();"));
    }

    /// <summary>
    /// «Eslatmalarim» HAR BIR xodimga ruxsatsiz (egasining talabi, 2026-10-27) — lekin o'quvchi/ota-onaga EMAS.
    /// Ruxsat kaliti qaytarilsa yangi xodim bo'limni yana ko'rmay qolardi; rol ro'yxatiga `Student` qo'shilsa
    /// kabinet foydalanuvchisi xodimlar modulida yoza olardi.
    /// </summary>
    [Fact]
    public void MyNotesController_barcha_xodimlarga_ruxsatsiz_oquvchiga_yopiq()
    {
        var src = Source("MyNotesController.cs");
        var allowed = src[src.IndexOf("private bool Allowed", StringComparison.Ordinal)..];
        allowed = allowed[..allowed.IndexOf(';')];
        foreach (var role in new[] { "Roles.Staff", "Roles.Admin", "Roles.SuperAdmin", "Roles.PlatformOwner", "Roles.Teacher" })
            Assert.Contains(role, allowed);
        Assert.DoesNotContain("Roles.Student", allowed);
        Assert.DoesNotContain("HasSectionAccess", allowed);
    }

    [Fact]
    public void WikiTeamController_har_YOZISH_faqat_rahbariyatga_har_amal_korinish_qoidasidan_otadi()
    {
        var src = Source("WikiTeamController.cs");
        Assert.Contains("[NoStore]", src);
        // Yozish endpointlari: qo'shish, tahrir, qulf, o'chirish, sozlama — HAR BIRI rahbariyat tekshiruvi bilan.
        var writes = Regex.Matches(src, @"\[Http(Post|Put|Delete)").Count;
        Assert.Equal(5, writes);
        var leaderChecks = Count(src, "if (!viewer.IsLeader) return LeadersOnly();")
                           + Count(src, "if (!(await ViewerAsync()).IsLeader) return LeadersOnly();");
        Assert.Equal(writes + 1, leaderChecks); // + sozlamani O'QISH ham faqat rahbariyatga
        // Har o'qish/yozish ko'rinish qoidasidan o'tadi (o'zi emas, ko'rinadigan xodim).
        Assert.True(Count(src, "Visible(viewer,") >= 7);
        Assert.Contains("v.CanSeeNotesOf(userId)", src);
    }

    [Fact]
    public void MyNotesController_qulflangan_eslatmani_egasi_ham_ozgartira_olmaydi()
    {
        var src = Source("MyNotesController.cs");
        // Yaratish (ota), saqlash, ko'chirish, o'chirish — qulf tekshiruvi bilan.
        Assert.True(Count(src, "return Locked();") >= 4);
    }
}
