using WunderkindLC.Application.Abstractions;
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
            // U3d: consecutive unexcused absences → alerts + automatic «Bog'lanish kerak» (every 30 min, 08–22).
            services.AddHostedService<AbsenceStreakService>();
        }
        return services;
    }

    public static async Task RunStartupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CrmAttendance");
        try
        {
            // U3d: the contact reason «Ketma-ket darsga kelmadi» + CenterMeta.AbsenceStreakReasonId (once).
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await AbsenceStreakService.SeedReasonAsync(db))
                logger.LogInformation("CRM attendance: streak contact reason seeded");
        }
        catch (Exception ex)
        {
            // A failed seed must not stop the app — streak requests then go without a catalogue reason.
            logger.LogError(ex, "CRM attendance startup seed failed");
        }
    }
}
