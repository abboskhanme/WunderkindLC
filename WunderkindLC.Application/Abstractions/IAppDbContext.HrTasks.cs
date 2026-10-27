using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>HR topshiriqlari (docs/SPEC-hr-tasks.md) — the <c>hr_*</c> tables ported from WunderkindLMS.</summary>
public partial interface IAppDbContext
{
    DbSet<HrPosition> HrPositions { get; }
    DbSet<HrEmployee> HrEmployees { get; }
    DbSet<HrEmployeeManager> HrEmployeeManagers { get; }
    DbSet<HrTaskTemplate> HrTaskTemplates { get; }
    DbSet<HrTask> HrTasks { get; }
    DbSet<HrTaskProof> HrTaskProofs { get; }
    DbSet<HrTaskProgress> HrTaskProgress { get; }
    DbSet<HrTaskAttachment> HrTaskAttachments { get; }
    DbSet<HrTaskReminder> HrTaskReminders { get; }
    DbSet<HrSettings> HrSettings { get; }
    DbSet<HrJobRun> HrJobRuns { get; }
    /// <summary>LC: avtomatik sinov darsi topshiriqlari ↔ sinov darslari.</summary>
    DbSet<HrTrialTask> HrTrialTasks { get; }
}
