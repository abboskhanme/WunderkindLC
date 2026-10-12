using Microsoft.EntityFrameworkCore.Diagnostics;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Crm;

/// <summary>Phase 6 of the CRM upgrade (docs/SPEC-crm-upgrade.md): DI registrations and one-time
/// startup work (idempotent, guarded by a SeedMarker key) for this phase only.</summary>
public static class CrmAccountsSetup
{
    /// <summary>Shown in the startup log, the profile card and the group «Login/parollar» window.</summary>
    public const string VaultOffMessage = "Parollarni saqlash o'chiq: .env da CREDENTIAL_VAULT_KEY yo'q";

    public static IServiceCollection AddCrmAccounts(this IServiceCollection services)
    {
        // U6a — student password vault (key only in .env, fail-closed) + the SaveChanges interceptor that
        // stores every student password set/reset/self-change in the same transaction (SPEC §0.2 step 5).
        services.AddSingleton(_ => CredentialVault.FromSecrets());
        services.AddSingleton<IInterceptor, StudentCredentialInterceptor>();
        return services;
    }

    public static async Task RunStartupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var vault = sp.GetRequiredService<CredentialVault>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CrmAccounts");
        if (!vault.Configured)
        {
            // Fail-closed: nothing is stored (never plain text). The backfill retries on the next start.
            logger.LogWarning("{Message} — o'quvchi parollari saqlanmaydi (faqat hali ishlatilmagan dastlabki "
                + "parol ko'rinadi). Kalit: openssl rand -base64 32", VaultOffMessage);
            return;
        }
        try
        {
            var (ran, stored) = await StudentCredentialBackfill.RunOnceAsync(sp.GetRequiredService<AppDbContext>(), vault);
            if (ran) logger.LogInformation("Student password vault backfill: {Count} account(s) stored", stored);
        }
        catch (Exception ex)
        {
            // Never block startup over the backfill; the marker is not written, so it retries next start.
            logger.LogError(ex, "Student password vault backfill failed");
        }
    }
}
