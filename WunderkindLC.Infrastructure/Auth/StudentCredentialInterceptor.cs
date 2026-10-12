using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Auth;

/// <summary>
/// STUDENT PASSWORD CAPTURE — "no call site can forget it" (docs/SPEC-crm-upgrade.md §7 P6.1,
/// <c>.claude/rules/auth-sessions.md</c> §6).
///
/// <para>Every password write goes through <c>AccountExtensions</c> (<c>SetInitialPassword</c>,
/// <c>SetOwnPassword</c>, <c>BlockLogin</c>; <c>AccountFactory.CreateAccountFor</c> calls the first), which only
/// sets <see cref="AppUser.PendingPassword"/>. Right before the same <c>SaveChanges</c> writes the hash, this
/// interceptor turns that pending change into the <see cref="StudentCredential"/> row — same transaction, so the
/// stored password can never disagree with the hash:</para>
/// <list type="bullet">
/// <item><c>set</c> + key configured → upsert the encrypted row;</item>
/// <item><c>clear</c> (blocked login), or <c>set</c> WITHOUT a key → delete the row (never plain text, never stale);</item>
/// <item>a deleted student account → its row is deleted too;</item>
/// <item>non-student accounts (teacher, staff, admin, parent) → nothing is stored.</item>
/// </list>
/// The pending value is cleared afterwards. Registered as a DI <c>IInterceptor</c> from
/// <c>CrmAccountsSetup.AddCrmAccounts</c> (SPEC §0.2 step 5). Singleton, stateless.
/// </summary>
public sealed class StudentCredentialInterceptor(CredentialVault vault) : SaveChangesInterceptor
{
    // ⚠️ Runs on EVERY SaveChanges of the app, so automatic change detection is switched off while it works:
    // the pending value is not a column (no detection needed to find it), Remove() marks Deleted immediately,
    // and SaveChanges itself detects changes right after the interceptors. Otherwise each save of a large unit of
    // work (billing accrual, import) would scan every tracked entity two extra times.

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } ctx)
        {
            var auto = ctx.ChangeTracker.AutoDetectChangesEnabled;
            ctx.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                var work = Collect(ctx);
                if (work.Count > 0)
                {
                    var set = ctx.Set<StudentCredential>();
                    var missing = MissingFromLocal(set, work.Where(w => !w.Value.IsNew).Select(w => w.Key));
                    var loaded = missing.Count == 0 ? new List<StudentCredential>() : set.Where(c => missing.Contains(c.UserId)).ToList();
                    Apply(set, work, loaded);
                }
            }
            finally { ctx.ChangeTracker.AutoDetectChangesEnabled = auto; }
        }
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } ctx)
        {
            var auto = ctx.ChangeTracker.AutoDetectChangesEnabled;
            ctx.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                var work = Collect(ctx);
                if (work.Count > 0)
                {
                    var set = ctx.Set<StudentCredential>();
                    var missing = MissingFromLocal(set, work.Where(w => !w.Value.IsNew).Select(w => w.Key));
                    var loaded = missing.Count == 0
                        ? new List<StudentCredential>()
                        : await set.Where(c => missing.Contains(c.UserId)).ToListAsync(cancellationToken);
                    Apply(set, work, loaded);
                }
            }
            finally { ctx.ChangeTracker.AutoDetectChangesEnabled = auto; }
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>One planned vault change: upsert (with the plain password and source) or delete.</summary>
    private sealed record Work(StudentCredentials.VaultAction Action, string? Plain, string Source, bool IsNew = false);

    /// <summary>
    /// Reads every tracked account with a pending change (and every deleted student account), decides through
    /// <see cref="StudentCredentials.Decide"/> and clears the pending value. Keyed by user id — the LAST change of
    /// an account in one save wins (create + admin-set in the same request stores the admin-set password).
    /// </summary>
    private Dictionary<string, Work> Collect(DbContext ctx)
    {
        var work = new Dictionary<string, Work>(StringComparer.Ordinal);
        foreach (var entry in ctx.ChangeTracker.Entries<AppUser>())
        {
            var user = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                if (user.Role == Roles.Student)
                    work[user.Id] = new Work(StudentCredentials.VaultAction.Delete, null, "");
                user.PendingPassword = null;
                continue;
            }
            var pending = user.PendingPassword;
            if (pending is null || entry.State == EntityState.Detached) continue;
            user.PendingPassword = null;

            var action = StudentCredentials.Decide(pending, user.Role, vault.Configured);
            if (action != StudentCredentials.VaultAction.None)
                work[user.Id] = new Work(action, pending.Plain, pending.Source, entry.State == EntityState.Added);
        }
        return work;
    }

    /// <summary>Ids whose row is not already tracked (those are looked up in the database).</summary>
    private static List<string> MissingFromLocal(DbSet<StudentCredential> set, IEnumerable<string> ids)
    {
        var tracked = set.Local.Select(c => c.UserId).ToHashSet(StringComparer.Ordinal);
        return ids.Where(id => !tracked.Contains(id)).ToList();
    }

    private void Apply(DbSet<StudentCredential> set, Dictionary<string, Work> work, List<StudentCredential> loaded)
    {
        var rows = set.Local.Concat(loaded)
            .GroupBy(c => c.UserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var now = AppClock.Now;

        foreach (var (userId, w) in work)
        {
            rows.TryGetValue(userId, out var row);
            if (w.Action == StudentCredentials.VaultAction.Delete)
            {
                if (row is not null) set.Remove(row);
                continue;
            }

            var cipher = vault.Protect(w.Plain!);
            if (row is null)
            {
                set.Add(new StudentCredential { UserId = userId, PasswordCipher = cipher, Source = w.Source, UpdatedAt = now });
                continue;
            }
            row.PasswordCipher = cipher;
            row.Source = w.Source;
            row.UpdatedAt = now;
            // A row removed earlier in the same unit of work (e.g. purge, then re-create) is revived.
            EntityEntry<StudentCredential> entry = set.Entry(row);
            if (entry.State == EntityState.Deleted) entry.State = EntityState.Modified;
        }
    }
}
