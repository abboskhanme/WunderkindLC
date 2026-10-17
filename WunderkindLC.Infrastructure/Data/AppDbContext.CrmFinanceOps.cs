using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>Owner backlog 2026-10-17 (docs/SPEC-backlog-2026-10-17.md F3): cashbox kind/opening balance, expense
/// approval (<see cref="ExpenseRequest"/>) and staff payroll (<see cref="StaffSalary"/>, <c>FinanceTransaction.StaffUserId</c>
/// / <c>SalaryPayKind</c>). New non-nullable columns default to the old behaviour (<c>cash</c>, 0, threshold 0 = off).</summary>
public partial class AppDbContext
{
    public DbSet<ExpenseRequest> ExpenseRequests => Set<ExpenseRequest>();
    public DbSet<StaffSalary> StaffSalaries => Set<StaffSalary>();

    partial void ConfigureCrmFinanceOps(ModelBuilder b)
    {
        b.Entity<Cashbox>().Property(x => x.Kind).HasMaxLength(8).HasDefaultValue("cash");
        b.Entity<Cashbox>().Property(x => x.OpeningBalance).HasPrecision(18, 2);
        b.Entity<Cashbox>().Property(x => x.OpeningDate).HasMaxLength(10);

        b.Entity<CenterMeta>().Property(x => x.ExpenseApprovalThreshold).HasPrecision(18, 2);

        b.Entity<FinanceTransaction>().Property(x => x.StaffUserId).HasMaxLength(64);
        b.Entity<FinanceTransaction>().Property(x => x.SalaryPayKind).HasMaxLength(16);
        b.Entity<FinanceTransaction>().Property(x => x.ExpenseRequestId).HasMaxLength(64);
        b.Entity<FinanceTransaction>().HasIndex(x => new { x.StaffUserId, x.Month });
        // Bitta tasdiq so'rovi — ko'pi bilan BITTA tranzaksiya (jarayon qulfidan tashqari baza himoyasi, F3 revyu).
        b.Entity<FinanceTransaction>().HasIndex(x => x.ExpenseRequestId).IsUnique()
            .HasFilter("\"ExpenseRequestId\" IS NOT NULL");

        b.Entity<ExpenseRequest>(e =>
        {
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.SalaryPayKind).HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        b.Entity<StaffSalary>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.EffectiveFrom).HasMaxLength(7);
            e.HasIndex(x => new { x.UserId, x.EffectiveFrom }).IsUnique();
        });
    }
}
