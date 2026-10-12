using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Server.Crm;

/// <summary>Phase 2 of the CRM upgrade (docs/SPEC-crm-upgrade.md §3 — Telegram messaging core): DI registrations and
/// one-time startup work (idempotent, guarded by a SeedMarker key) for this phase only.</summary>
public static class CrmMessagingSetup
{
    public static IServiceCollection AddCrmMessaging(this IServiceCollection services)
    {
        // ⚠️ Media uploads (up to 50 MB video) need a longer timeout than the default "telegram" client's 10 s.
        services.AddHttpClient(TelegramService.MediaClientName, c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddSingleton<ITelegramSender>(sp => sp.GetRequiredService<TelegramService>());
        // Private attachment storage: {ContentRoot}/uploads/messaging (never served statically — Program.cs).
        services.AddSingleton(sp => new MessageAttachmentStore(
            Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "uploads")));
        // The outbox worker: one singleton = the hosted service AND the "wake" signal callers use after saving.
        services.AddSingleton<MessageDeliveryWorker>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<MessageDeliveryWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<MessageDeliveryWorker>());
        return services;
    }

    public static async Task RunStartupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CrmMessaging");
        try
        {
            var n = await MessagingDataFixes.BackfillRelationsAsync(db);
            if (n is not null) log.LogInformation("[crm-messaging] Telegram ulanishlari qarindoshligi to'ldirildi: {Count} ta", n);
        }
        catch (Exception ex) { log.LogWarning(ex, "[crm-messaging] qarindoshlik backfill bajarilmadi (migratsiya qo'llanmaganmi?)"); }
        try
        {
            var disabled = await MessagingDataFixes.TelegramOnlyRulesAsync(db);
            if (disabled is { Count: > 0 })
                log.LogWarning("[crm-messaging] Tashqi kanali yo'q (faqat push) avto-xabar qoidalari O'CHIRILDI: {Names}",
                    string.Join(", ", disabled));
        }
        catch (Exception ex) { log.LogWarning(ex, "[crm-messaging] avto-xabar qoidalari tuzatilmadi"); }
    }
}
