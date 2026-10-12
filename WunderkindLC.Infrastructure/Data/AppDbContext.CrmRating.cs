using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>Phase 4 (tests &amp; rating) model configuration — docs/SPEC-crm-upgrade.md §5 P4.1.
/// No new tables in Phase 4: rating settings on <see cref="CenterMeta"/> (U4b) and the test kind /
/// results-sent stamp on <see cref="TestResult"/> (U4a).
/// <para>⚠️ No <c>HasDefaultValue</c> on purpose: with a DB default EF treats the CLR default (0) as "not set"
/// and an INSERT of weight 0 ("ball only" — a valid value) would silently become the DB default. The existing
/// row gets the owner's defaults from the data SQL instead (<c>docs/migrations/Phase4.sql</c>).</para></summary>
public partial class AppDbContext
{
    partial void ConfigureCrmRating(ModelBuilder b)
    {
        b.Entity<CenterMeta>().Property(m => m.RatingTopBottomScope).HasMaxLength(8);

        // U4a — test kind + "results sent" stamp (data SQL: docs/migrations/Phase4a.sql).
        b.Entity<TestResult>().Property(t => t.Kind).HasMaxLength(16);
        b.Entity<TestResult>().Property(t => t.ResultsSentById).HasMaxLength(64);
    }
}
