namespace WunderkindLC.Server.Crm;

/// <summary>Phase 4 of the CRM upgrade (docs/SPEC-crm-upgrade.md): DI registrations and one-time
/// startup work (idempotent, guarded by a SeedMarker key) for this phase only.</summary>
public static class CrmRatingSetup
{
    public static IServiceCollection AddCrmRating(this IServiceCollection services)
    {
        // U4d — monthly TOP/BOTTOM messages (rating_top / rating_bottom rules, once per rated month in the DB).
        // Like every scheduled sender, off during a data import (ImportMode).
        if (!WunderkindLC.Application.Services.ImportMode.Enabled)
            services.AddHostedService<WunderkindLC.Application.Services.Rating.MonthlyRatingNotifier>();
        return services;
    }

    public static Task RunStartupAsync(IServiceProvider services) => Task.CompletedTask;
}
