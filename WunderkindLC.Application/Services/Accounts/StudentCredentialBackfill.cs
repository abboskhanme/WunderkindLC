using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Accounts;

/// <summary>
/// One-time startup copy of still-unused initial passwords into the vault (docs/SPEC-crm-upgrade.md §7 P6.1):
/// every student account with <c>InitialPassword != null</c> and no <see cref="StudentCredential"/> row gets a
/// <see cref="PasswordSources.Backfill"/> row — so those passwords stay visible after the student's first login.
///
/// <para>Guarded by the <see cref="MarkerKey"/> <see cref="SeedMarker"/> (FinanceTypeSeed pattern). ⚠️ Runs ONLY
/// with a configured vault; without the key nothing is written and NO marker is set — it is retried on the next
/// start. Students who already logged in before the vault existed cannot be recovered (only the hash is stored):
/// they stay «Parol saqlanmagan» until an admin regenerates the password.</para>
/// </summary>
public static class StudentCredentialBackfill
{
    public const string MarkerKey = "student-credential-backfill-2026-10";

    /// <summary>Ran = false: the marker exists or the vault is not configured. Stored = rows added.</summary>
    public static async Task<(bool Ran, int Stored)> RunOnceAsync(
        IAppDbContext db, CredentialVault vault, CancellationToken ct = default)
    {
        if (!vault.Configured) return (false, 0);
        if (await db.SeedMarkers.AnyAsync(m => m.Key == MarkerKey, ct)) return (false, 0);

        var candidates = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Student && u.InitialPassword != null && u.InitialPassword != "")
            .Select(u => new { u.Id, u.Role, u.InitialPassword })
            .ToListAsync(ct);
        var existing = (await db.StudentCredentials.AsNoTracking().Select(c => c.UserId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var now = AppClock.Now;
        var stored = 0;
        foreach (var u in candidates)
        {
            if (!StudentCredentials.NeedsBackfill(u.Role, u.InitialPassword, existing.Contains(u.Id))) continue;
            db.StudentCredentials.Add(new StudentCredential
            {
                UserId = u.Id,
                PasswordCipher = vault.Protect(u.InitialPassword!),
                Source = PasswordSources.Backfill,
                UpdatedAt = now,
            });
            stored++;
        }
        db.SeedMarkers.Add(new SeedMarker { Key = MarkerKey, AppliedAt = AppClock.Iso() });
        await db.SaveChangesAsync(ct);
        return (true, stored);
    }
}
