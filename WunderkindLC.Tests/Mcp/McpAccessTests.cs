using System.Text.RegularExpressions;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP'ga KIM ulana oladi va OAuth qoidalari (PKCE, qaytish manzili, ruxsat kalitlari) —
/// docs/SPEC-mcp.md §2, §4.
/// </summary>
public class McpAccessTests
{
    private static AppUser U(string role, params string[] perms) => new() { Role = role, Permissions = [.. perms] };

    [Fact]
    public void Superadmin_va_admin_har_doim_ulana_oladi()
    {
        Assert.True(McpAccess.IsAllowed(U(Roles.SuperAdmin)));
        Assert.True(McpAccess.IsAllowed(U(Roles.Admin)));
    }

    [Fact]
    public void Xodim_faqat_aiAccess_ruxsati_bilan()
    {
        Assert.False(McpAccess.IsAllowed(U(Roles.Staff)));
        Assert.False(McpAccess.IsAllowed(U(Roles.Staff, "students", "finance", "staff")));
        Assert.True(McpAccess.IsAllowed(U(Roles.Staff, "aiAccess")));
        Assert.True(McpAccess.IsAllowed(U(Roles.Staff, "students.list", "aiAccess:create")));
        // Nom bo'yicha adashish yo'q.
        Assert.False(McpAccess.IsAllowed(U(Roles.Staff, "aiAccessX", "ai")));
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Student)]
    [InlineData("ctiagent")]   // old CTI agent role (removed 2026-10) — still never allowed
    [InlineData("parent")]
    public void Oqituvchi_oquvchi_ota_ona_HECH_QACHON(string role) =>
        Assert.False(McpAccess.IsAllowed(U(role, "aiAccess")));

    // ------------------------------------------------------------------ ruxsat kalitlari katalogi

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

    private static HashSet<string> Catalog()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Client", "src", "config", "constants.ts"));
        var start = src.IndexOf("export const adminPermissions", StringComparison.Ordinal);
        var end = src.IndexOf("export function permPagesOf", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "constants.ts da `adminPermissions` topilmadi");
        return Regex.Matches(src[start..end], @"key:\s*'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet();
    }

    [Fact]
    public void aiAccess_kaliti_ruxsatlar_katalogida_bor() =>
        Assert.Contains(McpAccess.PermissionKey, Catalog());

    [Fact]
    public void HAR_bolim_kaliti_ruxsatlar_katalogida_bor()
    {
        var catalog = Catalog();
        var missing = McpAreas.All.SelectMany(a => a.Keys.Select(k => (a.Title, Key: k)))
            .Where(x => !catalog.Contains(x.Key)).Select(x => $"{x.Title}: \"{x.Key}\"").ToList();
        Assert.True(missing.Count == 0,
            "McpAreas dagi bu kalitlar `adminPermissions` katalogida YO'Q — xodimga bu bo'lim hech qachon "
            + "ochilmaydi:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Nozik_bolimlar_SAHIFA_kaliti_bilan_darvozalangan()
    {
        // Bo'lim kaliti ("students") uning ISTALGAN sahifasi bilan ham o'tadi (yuqoriga o'qish merosi) —
        // o'quvchi ma'lumoti turniket operatoriga, pul esa bonus hisoboti ko'ruvchisiga ochilib ketardi.
        Assert.Equal(["students.list"], McpAreas.Students.Keys);
        Assert.Equal(["finance.main"], McpAreas.Finance.Keys);
        Assert.Equal(["teachers.list"], McpAreas.Teachers.Keys);
        Assert.Equal(["leads.list"], McpAreas.Leads.Keys);
        Assert.True(McpAreas.ContactReport.SuperAdminOnly);
    }

    [Fact]
    public void Bolim_ruxsati_panel_qoidasi_bilan_tekshiriladi()
    {
        using var host = new McpTestHost();

        Assert.True(host.Context(McpTestHost.SuperAdmin()).CanReadArea(McpAreas.Finance));
        Assert.True(host.Context(McpTestHost.Admin()).CanReadArea(McpAreas.Finance));
        Assert.False(host.Context(McpTestHost.Admin()).CanReadArea(McpAreas.ContactReport));   // hard rol
        Assert.True(host.Context(McpTestHost.SuperAdmin()).CanReadArea(McpAreas.ContactReport));

        // Xodim: sahifa kaliti yoki butun bo'lim (pastga meros).
        Assert.True(host.Context(McpTestHost.Staff("students.list")).CanReadArea(McpAreas.Students));
        Assert.True(host.Context(McpTestHost.Staff("students")).CanReadArea(McpAreas.Students));
        Assert.True(host.Context(McpTestHost.Staff("students.list:edit")).CanReadArea(McpAreas.Students));
        // Boshqa sahifa ochmaydi.
        Assert.False(host.Context(McpTestHost.Staff("students.turnstile")).CanReadArea(McpAreas.Students));
        Assert.False(host.Context(McpTestHost.Staff("finance.bonus")).CanReadArea(McpAreas.Finance));
        Assert.False(host.Context(McpTestHost.Staff("students.list")).CanReadFinance);
        // Umumiy ko'rsatkichlar — ulana olgan har kimga.
        Assert.True(host.Context(McpTestHost.Staff("aiAccess")).CanReadArea(McpAreas.Any));
        // O'qituvchi tokeni (hech qachon berilmaydi) baribir hech narsa o'qimaydi.
        Assert.False(host.Context(McpTestHost.Teacher()).CanReadArea(McpAreas.Students));
    }

    [Fact]
    public void Ruxsatsiz_bolim_RAD_etiladi_va_auditga_denied_bolib_tushadi()
    {
        using var host = new McpTestHost();
        var ctx = host.Context(McpTestHost.Staff("aiAccess"));

        var ex = Assert.Throws<McpAccessDeniedException>(() => ctx.Require(McpAreas.Finance));
        Assert.Contains("Moliya", ex.Message);
        Assert.True(host.State.Denied);
    }

    // ------------------------------------------------------------------ OAuth: PKCE va tokenlar

    [Fact]
    public void PKCE_S256_RFC7636_namunasi()
    {
        // RFC 7636, Appendix B.
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.True(McpSecrets.IsValidVerifier(verifier));
        Assert.True(McpSecrets.IsValidChallenge(challenge));
        Assert.True(McpSecrets.VerifyPkce(verifier, challenge));
        Assert.False(McpSecrets.VerifyPkce(verifier + "x", challenge));
        Assert.False(McpSecrets.IsValidVerifier("qisqa"));
        Assert.False(McpSecrets.IsValidChallenge("plain-challenge"));
    }

    [Fact]
    public void Tokenlar_tasodifiy_va_bazada_faqat_XESHI_saqlanadi()
    {
        var a = McpSecrets.New(McpSecrets.AccessPrefix);
        var b = McpSecrets.New(McpSecrets.AccessPrefix);

        Assert.NotEqual(a, b);
        Assert.StartsWith("wlc_at_", a);
        Assert.True(a.Length >= 43 + 7);
        var hash = McpSecrets.Hash(a);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain(a, hash);
        Assert.Equal(hash, McpSecrets.Hash(a));
    }

    // ------------------------------------------------------------------ OAuth: qaytish manzili

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://claude.com/api/mcp/auth_callback", true)]
    [InlineData("https://chatgpt.com/connector_platform_oauth_redirect", true)]
    [InlineData("http://localhost:33418/callback", true)]
    [InlineData("http://127.0.0.1:8123/cb", true)]
    [InlineData("https://chatgpt.com/cb", false)]                            // to'g'ri host, lekin AYNAN callback emas
    [InlineData("https://claude.ai/api/mcp/other", false)]
    [InlineData("https://claude.ai/api/mcp/auth_callback?x=1", false)]
    [InlineData("https://evil.example.com/cb", false)]
    [InlineData("https://claude.ai.evil.com/api/mcp/auth_callback", false)]
    [InlineData("http://claude.ai/api/mcp/auth_callback", false)]           // https emas
    [InlineData("https://claude.ai:8443/api/mcp/auth_callback", false)]     // nostandart port
    [InlineData("https://user@claude.ai/api/mcp/auth_callback", false)]     // user-info
    [InlineData("https://claude.ai/api/mcp/auth_callback#f", false)]        // fragment
    [InlineData("https://CLAUDE.ai/api/mcp/auth_callback", false)]          // kanonik emas
    [InlineData("cursor://anysphere.cursor-retrieval/oauth", false)]        // maxsus sxema
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Qaytish_manzili_faqat_AI_ilovalarning_AYNAN_callback_manzili(string uri, bool allowed) =>
        Assert.Equal(allowed, new McpRedirectRules().IsAllowed(uri));

    [Fact]
    public void Qaytish_manzili_royxatdan_otganiga_AYNAN_mos_kelishi_shart()
    {
        var rules = new McpRedirectRules();
        string[] registered = ["https://claude.ai/api/mcp/auth_callback", "http://localhost:1000/cb"];

        Assert.Equal("https://claude.ai/api/mcp/auth_callback", rules.Match(registered, "https://claude.ai/api/mcp/auth_callback"));
        Assert.Null(rules.Match(registered, "https://claude.ai/api/mcp/other"));
        Assert.Null(rules.Match(registered, "https://chatgpt.com/connector_platform_oauth_redirect"));   // ro'yxatda yo'q
        // Loopback: port farq qilishi mumkin (RFC 8252 §7.3), yo'l — yo'q.
        Assert.Equal("http://localhost:2000/cb", rules.Match(registered, "http://localhost:2000/cb"));
        Assert.Null(rules.Match(registered, "http://localhost:2000/boshqa"));
        // Bitta manzil ro'yxatdan o'tgan bo'lsa — bo'sh so'rov o'shani oladi; ikkita bo'lsa — yo'q.
        Assert.Null(rules.Match(registered, null));
        Assert.Equal("https://claude.ai/api/mcp/auth_callback", rules.Match(["https://claude.ai/api/mcp/auth_callback"], null));
    }

    [Fact]
    public void Sozlamadagi_royxat_standartni_ALMASHTIRADI_va_yaroqsiz_qiymat_tashlanadi()
    {
        var rules = new McpRedirectRules(["https://chatgpt.com/connector_platform_oauth_redirect", "http://evil.example.com/x", " "]);
        Assert.Equal(["https://chatgpt.com/connector_platform_oauth_redirect"], rules.AllowedUris);
        Assert.Null(rules.Match(["https://claude.ai/api/mcp/auth_callback"], "https://claude.ai/api/mcp/auth_callback"));
    }

    [Theory]
    [InlineData("Claude", "Claude")]
    [InlineData("  ko'p   bo'shliq  ", "ko'p bo'shliq")]
    [InlineData("Wunder‮kind​ rasmiy", "Wunderkind rasmiy")]   // bidi + zero-width
    [InlineData("qator\nuzilishi", "qator uzilishi")]
    public void Mijoz_nomi_tozalanadi(string raw, string expected) =>
        Assert.Equal(expected, McpText.Sanitize(raw, 60));

    [Fact]
    public void Mijoz_nomi_uzunligi_cheklanadi() =>
        Assert.Equal(60, McpText.Sanitize(new string('a', 500), 60).Length);
}

/// <summary>Parol almashtirilganda/tiklanganda AI ulanishlari ham uziladi (auth-sessions.md §2).</summary>
public class McpSessionRevocationTests
{
    [Fact]
    public async Task Parol_almashtirilsa_AI_ulanishlari_ham_BEKOR_qilinadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.McpClients.Add(new McpClient { Id = "mcp_c1", Name = "Claude", RedirectUris = ["https://claude.ai/cb"] });
        db.Context.McpGrants.AddRange(
            new McpGrant { ClientId = "mcp_c1", UserId = "u1", Scope = McpAccess.Scope },
            new McpGrant { ClientId = "mcp_c1", UserId = "u2", Scope = McpAccess.Scope });
        db.Context.SaveChanges();

        // Joriy veb-sessiya saqlansa ham (o'z parolini almashtirish) — AI ulanishi uziladi.
        await WunderkindLC.Infrastructure.Auth.RefreshTokenService.RevokeAllForUserAsync(db.Context, "u1", Guid.NewGuid());
        db.Context.SaveChanges();

        var grants = db.Context.McpGrants.ToList();
        Assert.NotNull(grants.Single(g => g.UserId == "u1").RevokedAt);
        Assert.Equal("system:password-change", grants.Single(g => g.UserId == "u1").RevokedBy);
        Assert.Null(grants.Single(g => g.UserId == "u2").RevokedAt);
    }
}

/// <summary>
/// Server darvozalari (test loyihasi Server'ga bog'lanmagan — manba matni o'qiladi, PermissionCatalogTests naqshi):
/// `/mcp` faqat MCP tokeni bilan, boshqaruv API faqat superadmin, OAuth faqat O'Z yo'llarida anonim.
/// </summary>
public class McpServerGateTests
{
    private static string Server(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "Repo ildizi topilmadi");
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "WunderkindLC.Server" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Mcp_endpointi_MCP_token_siyosati_va_chastota_chegarasi_bilan()
    {
        var setup = Server("Mcp", "McpSetup.cs");
        Assert.Contains("app.MapMcp(\"/mcp\")", setup);
        Assert.Contains(".RequireAuthorization(Policy)", setup);
        Assert.Contains(".RequireRateLimiting(RateLimitPolicy)", setup);
        Assert.Contains(".RequireClaim(McpClaims.GrantId)", setup);
        // Faqat-o'qish ulanishiga qaytish yo'li YO'Q: vositalar konteksti ReadOnlyDatabase dan.
        Assert.Contains("ReadOnlyDatabase.ForNpgsql(McpReadOnlyRole.BuildConnectionString", setup);
        Assert.DoesNotContain("GetRequiredService<AppDbContext>().Users", setup);
    }

    [Fact]
    public void AI_ulanishlar_boshqaruvi_FAQAT_superadmin()
    {
        var admin = Server("Mcp", "McpAdminController.cs");
        Assert.Contains("[Authorize(Roles = Roles.SuperAdmin)]", admin);
        Assert.Contains("[Route(\"api/admin/mcp\")]", admin);
        Assert.DoesNotContain("AllowAnonymous", admin);
        Assert.DoesNotContain("AdminPerm", admin);   // admin ham, ruxsatli xodim ham kirmaydi
    }

    [Fact]
    public void OAuth_anonim_yollari_faqat_oauth_va_well_known()
    {
        var oauth = Server("Mcp", "McpOAuthController.cs");
        var routes = System.Text.RegularExpressions.Regex.Matches(oauth, "\\[Http(?:Get|Post)\\(\"([^\"]+)\"\\)\\]")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, r => Assert.True(r.StartsWith("/oauth/") || r.StartsWith("/.well-known/oauth-"), r));
        // Login sahifasi: skriptsiz CSP, freym taqiqlangan.
        Assert.Contains("default-src 'none'", oauth);
        Assert.Contains("frame-ancestors 'none'", oauth);
        // PKCE faqat S256.
        Assert.Contains("code_challenge_methods_supported\"] = new[] { \"S256\" }", oauth);
    }
}
