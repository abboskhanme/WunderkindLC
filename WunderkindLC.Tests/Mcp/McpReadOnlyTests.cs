using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — QAT'IY FAQAT O'QISH (foydalanuvchi talabi, 2026-10-11): "yozishga, o'chirishga,
/// qo'shishga hech qanday vosita bo'lmasin". Bu testlar qoidani uch qatlamda qulflaydi:
/// (1) faqat-o'qish konteksti yozishni rad etadi; (2) har vosita `ReadOnly` deb e'lon qilingan va
/// faqat <see cref="McpToolContext"/> ga bog'liq; (3) baza roli faqat ruxsat ro'yxatidagi
/// jadvallarni SELECT qila oladi, maxfiy jadval/ustunlar esa hech qachon berilmaydi.
/// </summary>
public class McpReadOnlyTests
{
    // ------------------------------------------------------------------ 1. kontekst qulflari

    [Fact]
    public void Faqat_oqish_kontekstida_SaveChanges_RAD_etiladi()
    {
        using var host = new McpTestHost();
        using var ro = host.OpenReadOnly();
        ro.Students.Add(new Student { FullName = "Yozilmasligi kerak" });

        var ex = Assert.Throws<InvalidOperationException>(() => ro.SaveChanges());
        Assert.Equal(ReadOnlyDatabase.RefuseWrites.Message, ex.Message);
        Assert.Equal(0, host.Rw.Students.Count());
    }

    [Fact]
    public async Task Faqat_oqish_kontekstida_ExecuteDelete_va_ExecuteUpdate_RAD_etiladi()
    {
        using var host = new McpTestHost();
        host.Rw.Students.Add(new Student { FullName = "Ali Valiyev" });
        host.Rw.SaveChanges();
        using var ro = host.OpenReadOnly();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ro.Students.ExecuteDeleteAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ro.Students.ExecuteUpdateAsync(s => s.SetProperty(x => x.FullName, "O'zgartirildi")));

        host.Rw.ChangeTracker.Clear();
        Assert.Equal("Ali Valiyev", host.Rw.Students.Single().FullName);
    }

    [Fact]
    public async Task Faqat_oqish_kontekstida_xom_SQL_bilan_yozish_RAD_etiladi()
    {
        using var host = new McpTestHost();
        using var ro = host.OpenReadOnly();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ro.Database.ExecuteSqlRawAsync("DELETE FROM \"Students\""));
    }

    [Fact]
    public void Faqat_oqish_konteksti_ozgarishlarni_KUZATMAYDI()
    {
        using var host = new McpTestHost();
        host.Rw.Students.Add(new Student { FullName = "Ali Valiyev" });
        host.Rw.SaveChanges();
        using var ro = host.OpenReadOnly();

        var loaded = ro.Students.First();
        loaded.FullName = "O'zgartirildi";

        Assert.Empty(ro.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("SELECT 1", true)]
    [InlineData("  select * from \"Students\"", true)]
    [InlineData("-- teg\nSELECT 1", true)]
    [InlineData("/* teg */ SELECT 1", true)]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x", true)]
    [InlineData("(SELECT 1) UNION (SELECT 2)", true)]
    [InlineData("INSERT INTO \"Students\" VALUES (1)", false)]
    [InlineData("UPDATE \"Students\" SET \"FullName\" = 'x'", false)]
    [InlineData("DELETE FROM \"Students\"", false)]
    [InlineData("DROP TABLE \"Students\"", false)]
    [InlineData("TRUNCATE \"Students\"", false)]
    [InlineData("WITH gone AS (DELETE FROM \"Students\" RETURNING 1) SELECT count(*) FROM gone", false)]
    [InlineData("SELECT 1; DELETE FROM \"Students\"", false)]
    [InlineData("-- SELECT\nDELETE FROM \"Students\"", false)]
    [InlineData("", false)]
    public void Faqat_SELECT_buyruqlari_otadi(string sql, bool expected) =>
        Assert.Equal(expected, ReadOnlyDatabase.SelectOnly.IsSelect(sql));

    // ------------------------------------------------------------------ 2. vositalar

    public static TheoryData<Type> ToolTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var t in McpToolRegistry.ToolTypes) data.Add(t);
        return data;
    }

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerToolAttribute Attr)> AllTools() =>
        from type in McpToolRegistry.ToolTypes
        from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        let attr = method.GetCustomAttribute<McpServerToolAttribute>()
        where attr is not null
        select (type, method, attr);

    [Fact]
    public void Kamida_bitta_vosita_bor_va_nomlari_takrorlanmaydi()
    {
        var tools = AllTools().ToList();
        Assert.NotEmpty(tools);
        var dup = tools.GroupBy(t => t.Attr.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Takror vosita nomlari: " + string.Join(", ", dup));
    }

    [Fact]
    public void HAR_vosita_FAQAT_OQISH_deb_elon_qilingan()
    {
        foreach (var (type, method, attr) in AllTools())
        {
            var who = $"{type.Name}.{method.Name}";
            Assert.True(attr.ReadOnly, $"{who}: ReadOnly = true bo'lishi SHART");
            Assert.False(attr.Destructive, $"{who}: Destructive = false bo'lishi SHART");
            Assert.True(attr.Idempotent, $"{who}: Idempotent = true bo'lishi SHART");
            Assert.False(attr.OpenWorld, $"{who}: OpenWorld = false bo'lishi SHART (tashqi tizimga chiqmaydi)");
            Assert.Matches("^[a-z][a-z0-9_]{2,60}$", attr.Name ?? "");
            Assert.False(string.IsNullOrWhiteSpace(attr.Title), $"{who}: Title bo'sh");
            Assert.False(string.IsNullOrWhiteSpace(method.GetCustomAttribute<DescriptionAttribute>()?.Description),
                $"{who}: [Description] bo'sh — AI vositani qachon chaqirishni bilmaydi");
        }
    }

    [Fact]
    public void Vosita_nomi_yozish_amalini_bildirmaydi()
    {
        var verbs = new[]
        {
            "create", "add", "insert", "update", "edit", "set", "delete", "remove", "send", "post", "write",
            "save", "archive", "restore", "pay", "assign", "move", "import", "upload", "revoke", "reset", "mark",
        };
        foreach (var (_, _, attr) in AllTools())
        {
            var parts = attr.Name!.Split('_');
            var bad = parts.Where(verbs.Contains).ToList();
            Assert.True(bad.Count == 0, $"`{attr.Name}`: nomida yozish fe'li bor ({string.Join(", ", bad)})");
        }
    }

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void Vosita_sinfi_FAQAT_McpToolContext_ga_bogliq(Type type)
    {
        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());
        var ctors = type.GetConstructors();
        Assert.Single(ctors);
        var parameters = ctors[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(McpToolContext), parameters[0].ParameterType);

        // DI'dan yoziladigan kontekst yoki servis ola olmasin: boshqa maydon/xossa yo'q.
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType != typeof(McpToolContext)).ToList();
        Assert.True(fields.Count == 0,
            $"{type.Name}: ortiqcha maydonlar — {string.Join(", ", fields.Select(f => f.FieldType.Name))}");
    }

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void Har_ochiq_metod_vosita_va_string_qaytaradi(Type type)
    {
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.True(m.GetCustomAttribute<McpServerToolAttribute>() is not null,
                $"{type.Name}.{m.Name}: ochiq metod [McpServerTool] bilan belgilanmagan");
            Assert.True(m.ReturnType == typeof(Task<string>) || m.ReturnType == typeof(string),
                $"{type.Name}.{m.Name}: natija t.Json(...) (string) bo'lishi kerak — maxfiy maydonlar shu yerda tozalanadi");
        }
    }

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

    private static readonly string ToolsDir = Path.Combine("WunderkindLC.Application", "Mcp", "Tools");

    [Fact]
    public void Vosita_manba_kodida_yozish_chaqiruvi_YOQ()
    {
        var forbidden = new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", ".Entry(", ".Attach(", "ChangeTracker",
            "HttpClient", "TelegramService", "File.Write", "File.Delete", "AuditService", "IServiceProvider",
        };
        // Staging a change on a DbSet (`db.Students.Add(…)`, `t.Db.Leads.Remove(…)`). Building a local
        // List with `.Add(` is fine; the runtime guard refuses SaveChanges anyway.
        var dbSetMutation = new Regex(@"\b(?:db|Db)\.[A-Z][A-Za-z]*\.(?:Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(");
        var files = Directory.GetFiles(Path.Combine(RepoRoot, ToolsDir), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var file in files)
        {
            // Kommentlar tekshirilmaydi (izohda "SaveChanges" so'zi bo'lishi mumkin).
            var code = Regex.Replace(File.ReadAllText(file), @"//.*$", "", RegexOptions.Multiline);
            hits.AddRange(forbidden.Where(code.Contains).Select(f => $"{Path.GetFileName(file)}: `{f}`"));
            hits.AddRange(dbSetMutation.Matches(code).Select(m => $"{Path.GetFileName(file)}: `{m.Value}`"));
        }
        Assert.True(hits.Count == 0, "MCP vositalarida taqiqlangan chaqiruv:\n  " + string.Join("\n  ", hits));
    }

    [Fact]
    public void Tools_papkasidagi_HAR_sinf_royxatga_olingan()
    {
        var registered = McpToolRegistry.ToolTypes.ToHashSet();
        var inAssembly = typeof(McpToolRegistry).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();
        var missing = inAssembly.Where(t => !registered.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0,
            "Bu vosita sinflari McpToolRegistry.ToolTypes da YO'Q (AI ularni ko'rmaydi): " + string.Join(", ", missing));
    }

    // ------------------------------------------------------------------ 3. baza roli

    [Fact]
    public void Hech_bir_vosita_TAQIQLANGAN_jadvalni_oqimaydi()
    {
        var bad = McpToolRegistry.ReadEntities.Intersect(McpReadOnlyGrants.Forbidden).Select(t => t.Name).ToList();
        Assert.True(bad.Count == 0, "Vosita `Reads` ro'yxatida taqiqlangan entity: " + string.Join(", ", bad));
    }

    [Fact]
    public void Rol_buyruqlari_faqat_SELECT_beradi_va_maxfiy_ustunlarni_ochmaydi()
    {
        using var host = new McpTestHost();
        var sql = McpReadOnlyRole.BuildStatements(host.Rw.Model, "wunderkindlc", "Abcdefgh12345678");
        var grants = sql.Where(s => s.StartsWith("GRANT", StringComparison.Ordinal)).ToList();

        // Jadvalga beriladigan yagona huquq — SELECT.
        foreach (var g in grants.Where(g => g.Contains(" ON public.", StringComparison.Ordinal)))
            Assert.StartsWith("GRANT SELECT", g);
        Assert.DoesNotContain(grants, g => Regex.IsMatch(g, @"\b(INSERT|UPDATE|DELETE|TRUNCATE|ALL)\b"));

        Assert.Contains(grants, g => g == "GRANT SELECT ON public.\"Students\" TO wlc_ro");

        var all = string.Join("\n", sql);
        foreach (var secret in new[]
                 {
                     "PasswordHash", "InitialPassword", "\"Email\"", "\"Permissions\"", "RefreshTokens", "LoginOtpCodes",
                     "McpTokens", "McpAuthCodes", "DeviceTokens", "StudentFaceProfiles", "TrustedDevices", "\"Cameras\"",
                 })
            Assert.DoesNotContain(secret, all);

        // Avval hammasi olib tashlanadi, keyin ro'yxat beriladi (eski ruxsat qolib ketmasin).
        var revoke = sql.ToList().FindIndex(s => s.StartsWith("REVOKE ALL ON ALL TABLES", StringComparison.Ordinal));
        var firstGrant = sql.ToList().FindIndex(s => s.StartsWith("GRANT SELECT", StringComparison.Ordinal));
        Assert.True(revoke >= 0 && revoke < firstGrant);
    }

    [Fact]
    public void Users_jadvali_butunlay_emas_USTUNLAR_boyicha_beriladi()
    {
        Assert.True(McpReadOnlyGrants.RestrictedColumns.ContainsKey(typeof(AppUser)));
        var allowed = McpReadOnlyGrants.RestrictedColumns[typeof(AppUser)];
        foreach (var secret in new[]
                 {
                     nameof(AppUser.PasswordHash), nameof(AppUser.InitialPassword), nameof(AppUser.Email),
                     nameof(AppUser.Permissions), nameof(AppUser.FailedLoginCount), nameof(AppUser.LockoutUntil),
                 })
            Assert.DoesNotContain(secret, allowed);
        Assert.DoesNotContain(typeof(AppUser), McpReadOnlyGrants.Tables);

        // McpUserRow (vositalar foydalanadigan yagona proyeksiya) faqat ruxsat etilgan ustunlardan.
        foreach (var p in typeof(McpUserRow).GetProperties())
            Assert.Contains(p.Name, allowed);
    }

    [Theory]
    [InlineData("Abcdefgh12345678", true)]
    [InlineData("a-b_c-d_e-f_g-h_1", true)]
    [InlineData("qisqa", false)]
    [InlineData("Abcdefgh1234567'", false)]
    [InlineData("Abcdefgh12345678; DROP ROLE x", false)]
    [InlineData("Abcdefgh 12345678", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rol_paroli_faqat_xavfsiz_belgilardan(string? password, bool ok)
    {
        Assert.Equal(ok, McpReadOnlyRole.IsValidPassword(password));
        if (!ok)
        {
            using var host = new McpTestHost();
            Assert.Throws<ArgumentException>(() =>
                McpReadOnlyRole.BuildStatements(host.Rw.Model, "db", password ?? ""));
        }
    }

    [Fact]
    public void Faqat_oqish_ulanishi_qattiqlashtirilgan()
    {
        var conn = McpReadOnlyRole.BuildConnectionString(
            "Host=postgres;Database=wunderkindlc;Username=wunderkindlc;Password=owner-secret;Maximum Pool Size=30",
            "Abcdefgh12345678");
        var b = new Npgsql.NpgsqlConnectionStringBuilder(conn);

        Assert.Equal(McpReadOnlyRole.RoleName, b.Username);
        Assert.Equal("Abcdefgh12345678", b.Password);
        Assert.DoesNotContain("owner-secret", conn);
        Assert.True(b.MaxPoolSize <= 8);
        Assert.False(b.IncludeErrorDetail);
        Assert.Contains("default_transaction_read_only=on", b.Options);
    }

    [Fact]
    public async Task Foydalanuvchi_ismlari_SQLda_id_boyicha_filtrlanadi_va_maxfiy_ustun_tanlanmaydi()
    {
        using var host = new McpTestHost();
        host.Rw.Users.AddRange(
            new AppUser { Id = "a", FullName = "Admin A", Role = Roles.Admin, Email = "login-a", PasswordHash = "H" },
            new AppUser { Id = "b", FullName = "Xodim B", Role = Roles.Staff, Email = "login-b", PasswordHash = "H" },
            new AppUser { Id = "s", FullName = "O'quvchi S", Role = Roles.Student, Email = "login-s", PasswordHash = "H" });
        host.Rw.SaveChanges();
        var ctx = host.Context(McpTestHost.SuperAdmin());

        var names = await ctx.UserNamesAsync(["a", null, "", "b", "a", "yo'q"]);

        Assert.Equal(new Dictionary<string, string> { ["a"] = "Admin A", ["b"] = "Xodim B" }, names);
        var sql = Assert.Single(host.Sql);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);   // butun jadval yuklanmaydi
        host.AssertNoSecretColumns();
        Assert.DoesNotContain("\"Email\"", sql);
        Assert.Empty(await ctx.UserNamesAsync([]));
    }

    // ------------------------------------------------------------------ 4. chiqishni tozalash

    [Theory]
    [InlineData("passwordHash")]
    [InlineData("InitialPassword")]
    [InlineData("email")]
    [InlineData("login")]
    [InlineData("lastLoginAt")]
    [InlineData("accessToken")]
    [InlineData("fcm_token")]
    [InlineData("parentPassportUrl")]
    [InlineData("birthCertificateUrl")]
    [InlineData("rtspUrl")]
    [InlineData("chatId")]
    [InlineData("apiKey")]
    public void Maxfiy_nomli_maydon_natijadan_OLIB_tashlanadi(string name)
    {
        Assert.True(McpToolContext.IsSecretName(name));
        var json = McpToolContext.Serialize(new Dictionary<string, object>
        {
            [name] = "maxfiy-qiymat",
            ["fullName"] = "Ali",
            ["nested"] = new Dictionary<string, object> { [name] = "maxfiy-qiymat", ["phone"] = "901234567" },
            ["list"] = new[] { new Dictionary<string, object> { [name] = "maxfiy-qiymat" } },
        });

        Assert.DoesNotContain("maxfiy-qiymat", json);
        Assert.Contains("Ali", json);
        Assert.Contains("901234567", json);
    }

    [Theory]
    [InlineData("fullName")]
    [InlineData("phone")]
    [InlineData("balance")]
    [InlineData("groupId")]
    [InlineData("telegramLinked")]
    public void Oddiy_maydon_saqlanadi(string name) => Assert.False(McpToolContext.IsSecretName(name));
}
