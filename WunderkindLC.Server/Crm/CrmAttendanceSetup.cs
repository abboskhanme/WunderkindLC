namespace WunderkindLC.Server.Crm;

/// <summary>Phase 3 of the CRM upgrade (docs/SPEC-crm-upgrade.md): DI registrations and one-time
/// startup work (idempotent, guarded by a SeedMarker key) for this phase only.</summary>
public static class CrmAttendanceSetup
{
    public static IServiceCollection AddCrmAttendance(this IServiceCollection services) => services;

    public static Task RunStartupAsync(IServiceProvider services) => Task.CompletedTask;
}
