using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>Owner backlog 2026-10-17 (docs/SPEC-backlog-2026-10-17.md F4 L1): lead auto-task — <c>WorkTask.LeadId</c>
/// and the <c>CenterMeta.LeadAutoTask*</c> settings. All additive; the feature is off by default.</summary>
public partial class AppDbContext
{
    partial void ConfigureCrmLeadTasks(ModelBuilder b)
    {
        b.Entity<WorkTask>().Property(x => x.LeadId).HasMaxLength(64);
        b.Entity<WorkTask>().HasIndex(x => x.LeadId);
        b.Entity<CenterMeta>().Property(x => x.LeadAutoTaskBoardId).HasMaxLength(64);
    }
}
