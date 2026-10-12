using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Crm;

/// <summary>Phase 3 of the CRM upgrade (docs/SPEC-crm-upgrade.md): DI registrations and one-time
/// startup work (idempotent, guarded by a SeedMarker key) for this phase only.</summary>
public static class CrmAttendanceSetup
{
    public static IServiceCollection AddCrmAttendance(this IServiceCollection services)
    {
        // Background jobs — like the other reminder services, not in data-import mode.
        if (!ImportMode.Enabled)
        {
            // U3c: teacher reminders during the lesson, admin escalation, attendance fines (every minute).
            services.AddHostedService<AttendanceDisciplineService>();
        }
        return services;
    }

    public static Task RunStartupAsync(IServiceProvider services) => Task.CompletedTask;
}
