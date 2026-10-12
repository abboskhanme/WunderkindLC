using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — CRM upgrade Phase 6 (U6m, SPEC P6.3): o'quvchi parollari seyfi (<see cref="StudentCredential"/>,
/// jadval <c>StudentCredentials</c>) AI ulanishiga HECH QACHON ochilmaydi. Uch qatlam: (1) taqiqlangan ro'yxatda va
/// hech bir vosita `Reads` ida yo'q; (2) vositalar manba kodida umuman tilga olinmaydi; (3) PostgreSQL'da faqat-o'qish
/// roli jadvaldan BIRORTA ham ustunni o'qiy olmaydi — qo'lda bitta ustun berilsa ham o'z-o'zini tekshirish MCP'ni o'chiradi.
/// </summary>
public class McpCredentialVaultTests
{
    [Fact]
    public void Parol_seyfi_TAQIQLANGAN_va_ruxsat_royxatida_YOQ()
    {
        Assert.Contains(typeof(StudentCredential), McpReadOnlyGrants.Forbidden);
        Assert.DoesNotContain(typeof(StudentCredential), McpToolRegistry.ReadEntities);
        Assert.True(McpReadOnlyGrants.IsSensitiveColumn(nameof(StudentCredential.PasswordCipher)));

        using var host = new McpTestHost();
        Assert.DoesNotContain(McpReadOnlyRole.Grants(host.Rw.Model), g => g.Entity == typeof(StudentCredential));
        var sql = McpReadOnlyRole.BuildStatements(host.Rw.Model, "wunderkindlc", "Abcdefgh12345678");
        Assert.DoesNotContain(sql, s => s.Contains("StudentCredentials", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, s => s.Contains("PasswordCipher", StringComparison.Ordinal));
    }

    [Fact]
    public void Vositalar_manba_kodida_parol_seyfi_tilga_OLINMAYDI()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "WunderkindLC.Application", "Mcp"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("McpToolRegistry.cs", StringComparison.Ordinal))   // the Forbidden list itself
            .ToList();
        Assert.NotEmpty(files);
        var hits = files
            .SelectMany(f => new[] { "StudentCredential", "CredentialVault", "PasswordCipher", "InitialPassword" }
                .Where(word => File.ReadAllText(f).Contains(word, StringComparison.Ordinal))
                .Select(word => $"{Path.GetFileName(f)}: {word}"))
            .ToList();
        Assert.True(hits.Count == 0, "MCP kodida o'quvchi paroliga yo'l: " + string.Join(", ", hits));
    }

    [Fact]
    public async Task PostgreSQL_da_faqat_oqish_roli_parol_seyfini_OQIY_OLMAYDI()
    {
        if (!McpTestHost.IsPostgres) return;   // SQLite: rol yo'q
        using var host = new McpTestHost();
        host.Rw.Users.Add(new AppUser { Id = "u-s1", FullName = "Ali Valiyev", Role = Roles.Student, Email = "ali", PasswordHash = "H" });
        host.Rw.StudentCredentials.Add(new StudentCredential { UserId = "u-s1", PasswordCipher = [1, 2, 3, 4], Source = PasswordSources.Create });
        host.Rw.SaveChanges();

        // Xom SQL ham (EF modelini chetlab), EF so'rovi ham — `permission denied` (42501).
        await using (var conn = new Npgsql.NpgsqlConnection(host.PgReadOnlyConnection))
        {
            await conn.OpenAsync();
            foreach (var column in new[] { "\"UserId\"", "\"Source\"", "\"PasswordCipher\"", "count(*)" })
            {
                await using var cmd = new Npgsql.NpgsqlCommand($"SELECT {column} FROM \"StudentCredentials\"", conn);
                var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteScalarAsync());
                Assert.Equal("42501", ex.SqlState);
            }
        }
        using var ro = host.OpenReadOnly();
        await Assert.ThrowsAnyAsync<Exception>(() => ro.StudentCredentials.Select(c => c.UserId).ToListAsync());

        // Kimdir qo'lda BITTA "zararsiz" ustunni ochsa ham — o'z-o'zini tekshirish MCP'ni o'chiradi (fail-closed).
        Assert.Null(McpReadOnlyRole.SelfTest(host.PgReadOnlyConnection!, host.Rw.Model));
        host.Rw.Database.ExecuteSqlRaw("GRANT SELECT (\"UserId\") ON \"StudentCredentials\" TO " + McpReadOnlyRole.RoleName);
        Assert.Contains("forbidden table StudentCredential", McpReadOnlyRole.SelfTest(host.PgReadOnlyConnection!, host.Rw.Model));
    }
}
