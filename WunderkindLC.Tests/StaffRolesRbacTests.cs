using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// XODIM ROLLARI — RUXSAT darvozasi (<c>StaffController</c>). Rol berish = huquq berish, shuning uchun
/// rol CRUD'i, xodimga rol biriktirish, individual ruxsat, parol tiklash/almashtirish — hammasi
/// <c>HasFullAccess(User, "staff")</c> ortida bo'lishi SHART (`.claude/rules/staff-roles.md` §2).
///
/// <para><b>NEGA MANBA MATNI:</b> test loyihasi <c>WunderkindLC.Server</c> ga havola QILMAYDI
/// (<see cref="HomeEndpointsRbacTests"/> bilan bir xil sabab). Kimdir tekshiruvni olib tashlasa —
/// test darrov qizaradi.</para>
/// </summary>
public class StaffRolesRbacTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi topilmadi");
        return File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "StaffController.cs"));
    }

    /// <summary>Metod TANASI (e'londan keyingi birinchi <c>{</c> dan boshlab ~N belgi).</summary>
    private static string Body(string src, string signature, int take = 900)
    {
        var i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"Metod topilmadi: {signature}");
        var open = src.IndexOf('{', i);
        return src.Substring(open, Math.Min(take, src.Length - open));
    }

    [Fact]
    public void Sinf_darajasida_staff_ruxsati_va_anonim_kirish_yoq()
    {
        var src = Source();
        Assert.Contains("[AdminPerm(\"staff\")]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
    }

    [Fact]
    public void CanEditRoles_TOLIQ_ruxsat()
    {
        Assert.Matches(new Regex(@"CanEditRoles\s*=>\s*AdminPermAttribute\.HasFullAccess\(User,\s*""staff""\)"), Source());
    }

    [Theory]
    [InlineData("CreateRole(SaveStaffRoleRequest req)")]
    [InlineData("UpdateRole(string id, SaveStaffRoleRequest req)")]
    [InlineData("DeleteRole(string id)")]
    public void Rol_CRUD_birinchi_qatorda_darvoza(string signature)
    {
        var body = Body(Source(), signature, 120);
        Assert.Contains("if (!CanEditRoles) return Forbid();", body);
    }

    [Theory]
    [InlineData("SetPermissions(string id, SetStaffPermissionsRequest req)")]
    [InlineData("ResetPassword(string id)")]
    [InlineData("Credentials(string id)")]
    public void Ruxsat_berish_va_parol_TOLIQ_ruxsat_ortida(string signature)
    {
        var body = Body(Source(), signature, 700);
        Assert.Contains("AdminPermAttribute.HasFullAccess(User, \"staff\")) return Forbid();", body);
    }

    [Fact]
    public void Create_rol_bilan_TOLIQ_ruxsat_ortida()
    {
        var body = Body(Source(), "Create(CreateStaffWithTemplateRequest req)", 4000);
        var at = body.IndexOf("if (!string.IsNullOrWhiteSpace(req.RoleTemplateId))", StringComparison.Ordinal);
        Assert.True(at >= 0, "Rol bilan yaratish tarmog'i topilmadi");
        var roleBranch = body.Substring(at, Math.Min(900, body.Length - at));
        Assert.Contains("HasFullAccess(User, \"staff\")) return Forbid();", roleBranch);
    }

    [Fact]
    public void Update_rol_ALMASHTIRISH_va_PAROL_TOLIQ_ruxsat_ortida()
    {
        var body = Body(Source(), "Update(string id, CreateStaffWithTemplateRequest req)", 3000);
        // Parol almashtirish — akkauntga kirish.
        Assert.Matches(new Regex(@"NewPassword\)\s*&&\s*!AdminPermAttribute\.HasFullAccess\(User,\s*""staff""\)\)\s*return Forbid\(\);"), body);
        // Rol o'zgarsa — to'liq ruxsat.
        Assert.Contains("if (!fullAccess) return Forbid();", body);
        Assert.Contains("var fullAccess = AdminPermAttribute.HasFullAccess(User, \"staff\");", body);
    }

    [Theory]
    [InlineData(new[] { "staff:create", "staff:edit", "staff:delete" }, false)]
    [InlineData(new[] { "staff:view" }, false)]
    [InlineData(new[] { "staff.xyz" }, false)]
    [InlineData(new[] { "staff" }, true)]
    public void Barcha_amallar_ALOHIDA_berilsa_ham_TOLIQ_ruxsat_EMAS(string[] perms, bool expected) =>
        Assert.Equal(expected, PermissionRules.HasFullSection(perms, "staff"));
}
