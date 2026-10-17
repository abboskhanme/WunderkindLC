namespace WunderkindLC.Domain;

// Owner backlog 2026-10-17 (docs/SPEC-backlog-2026-10-17.md F4 L1): a lead that leaves its details through an
// external channel gets a «call this lead» task, assigned round-robin. Model config:
// WunderkindLC.Infrastructure/Data/AppDbContext.CrmLeadTasks.cs. Rules: Application/Services/LeadAutoTask.cs.

public partial class WorkTask
{
    /// <summary>The lead this task is about (<c>null</c> = not a lead task). Written by the lead auto-task; the KPI
    /// check «vazifasiz sdelka = 0» counts it as a link (alongside the older lead-id-in-<see cref="Tags"/> form).</summary>
    public string? LeadId { get; set; }
}

public partial class CenterMeta
{
    /// <summary>LIDGA AVTOMATIK TOPSHIRIQ — default OFF (nothing changes until the owner turns it on and picks staff).</summary>
    public bool LeadAutoTaskEnabled { get; set; }

    /// <summary>Who takes turns (AppUser.Id, in rotation order). Empty while enabled — the task is created unassigned.</summary>
    public List<string> LeadAutoTaskAssigneeIds { get; set; } = new();

    /// <summary>NOT USED (2026-10-17 review S1): the rotation is derived from the latest auto-task's assignee instead of a
    /// stored cursor. Kept only because the column shipped in migration <c>LeadAutoTasks</c>.</summary>
    public int LeadAutoTaskCursor { get; set; }

    /// <summary>Board for the tasks (<c>null</c> or archived/missing = the first active board).</summary>
    public string? LeadAutoTaskBoardId { get; set; }
}
