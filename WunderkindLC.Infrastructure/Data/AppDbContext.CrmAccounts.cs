using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>CRM upgrade Phase 6 «Accounts» (docs/SPEC-crm-upgrade.md §7): the student password vault table.</summary>
public partial class AppDbContext
{
    public DbSet<StudentCredential> StudentCredentials => Set<StudentCredential>();

    partial void ConfigureCrmAccounts(ModelBuilder b)
    {
        b.Entity<StudentCredential>(e =>
        {
            e.ToTable("StudentCredentials");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasMaxLength(36);
            e.Property(x => x.PasswordCipher).IsRequired();
            e.Property(x => x.Source).HasMaxLength(16);
        });
    }
}
