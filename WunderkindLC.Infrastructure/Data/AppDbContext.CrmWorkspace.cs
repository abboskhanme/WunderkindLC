using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>CRM upgrade — Phase 5 model configuration (docs/SPEC-crm-upgrade.md §6).
/// <para>Phase 5 adds COLUMNS only (<c>Domain/Crm/Workspace.cs</c>: <c>Room.Floor</c>,
/// <c>UserSettings.HomeLayout</c>; the Kanban task approval columns were removed with the module, 2026-10-21) — mapped by convention (plain text/int/bool/timestamp like the
/// neighbouring columns), no new tables and no new DbSets. Defaults of the new non-nullable columns
/// (<c>false</c>, <c>0</c>, <c>""</c>) keep every existing row's behaviour unchanged.</para></summary>
public partial class AppDbContext
{
    partial void ConfigureCrmWorkspace(ModelBuilder b)
    {
        // Intentionally empty: convention mapping is enough (see the summary).
    }
}
