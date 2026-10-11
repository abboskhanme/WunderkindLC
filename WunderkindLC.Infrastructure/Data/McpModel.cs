using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// Read-only MCP server: OAuth clients, grants, one-time codes, hashed tokens and the
/// tool-call audit (docs/SPEC-mcp.md, <c>AddMcpReadOnly</c> migration).
/// Secrets are never stored in the clear — codes and tokens are SHA-256 hashes.
/// </summary>
internal static class McpModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<McpClient>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<McpGrant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(200);
            e.Property(x => x.Scope).HasMaxLength(100);
            e.Property(x => x.Resource).HasMaxLength(500);
            e.Property(x => x.RevokedBy).HasMaxLength(200);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ClientId);
            e.HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpAuthCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(200);
            e.Property(x => x.RedirectUri).HasMaxLength(2000);
            e.Property(x => x.CodeChallenge).HasMaxLength(128);
            e.Property(x => x.Resource).HasMaxLength(500);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.GrantId);
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<McpGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<McpAuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(200);
            e.Property(x => x.UserName).HasMaxLength(200);
            e.Property(x => x.ClientId).HasMaxLength(64);
            e.Property(x => x.ClientName).HasMaxLength(200);
            e.Property(x => x.Tool).HasMaxLength(100);
            e.Property(x => x.Arguments).HasMaxLength(500);
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasIndex(x => x.GrantId);
        });
    }
}
