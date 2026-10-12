using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Accounts;

/// <summary>
/// Pure rules of the student password vault (docs/SPEC-crm-upgrade.md §7 P6.1, tests:
/// <c>StudentCredentialsTests</c>). The interceptor, the startup backfill, the profile card, the group
/// «Login/parollar» window and every export decide through these functions — one rule, many callers.
/// </summary>
public static class StudentCredentials
{
    /// <summary>Text of an unknown password in Excel exports.</summary>
    public const string UnknownExportText = "(saqlanmagan)";

    /// <summary>What the interceptor does with the vault row of one account.</summary>
    public enum VaultAction
    {
        /// <summary>Leave the row as it is (not a student, or nothing pending).</summary>
        None,
        /// <summary>Insert or overwrite the row with the new encrypted password.</summary>
        Upsert,
        /// <summary>Delete the row (login blocked, or a new password could not be stored — the old one is stale).</summary>
        Delete,
    }

    /// <summary>
    /// The interceptor's decision for one pending change.
    /// <list type="bullet">
    /// <item>not a <c>student</c> account (teacher, staff, admin, PARENT — SPEC §10 #10) → <see cref="VaultAction.None"/>;</item>
    /// <item><c>clear</c> (login blocked) → <see cref="VaultAction.Delete"/>;</item>
    /// <item><c>set</c> + vault configured → <see cref="VaultAction.Upsert"/>;</item>
    /// <item>⚠️ <c>set</c> WITHOUT a key → <see cref="VaultAction.Delete"/>: the old stored password is no longer
    ///   valid; leaving it would show a WRONG password once the key comes back. The new one stays visible from
    ///   <c>InitialPassword</c> until the student's first login.</item>
    /// </list>
    /// </summary>
    public static VaultAction Decide(PendingPasswordChange? pending, string? role, bool vaultConfigured)
    {
        if (pending is null || !string.Equals(role, Roles.Student, StringComparison.Ordinal)) return VaultAction.None;
        if (pending.Kind == PendingPasswordChange.KindClear) return VaultAction.Delete;
        if (pending.Kind != PendingPasswordChange.KindSet || pending.Plain is null) return VaultAction.None;
        return vaultConfigured ? VaultAction.Upsert : VaultAction.Delete;
    }

    /// <summary>A revealed credential: the password (null = unknown), whether it is known, its source and when it was stored.</summary>
    public sealed record Revealed(string? Password, bool Known, string Source, DateTime? UpdatedAt);

    /// <summary>
    /// What an administrator sees for one account.
    /// <list type="number">
    /// <item>The vault row decrypts → that password (<c>known</c>, its source, when).</item>
    /// <item>Otherwise a still-unused <c>InitialPassword</c> → that one (source <c>initial</c>).</item>
    /// <item>Otherwise → unknown (an account created before the vault whose student already logged in, a
    ///   blocked login, or a vault key that changed) — «Parol saqlanmagan»; the admin regenerates it.</item>
    /// </list>
    /// ⚠️ <c>InitialPassword</c> is set together with the hash and cleared on every own change and login, so when
    /// it is present it IS the current password; a decrypted row that differs from it is stale (it can only
    /// happen after a key outage) and loses.
    /// </summary>
    public static Revealed Reveal(StudentCredential? row, AppUser? user, CredentialVault vault)
    {
        if (user is null) return new Revealed(null, false, "", null);
        var initial = string.IsNullOrEmpty(user.InitialPassword) ? null : user.InitialPassword;
        var stored = row is null ? null : vault.Unprotect(row.PasswordCipher);
        if (stored is not null && (initial is null || initial == stored))
            return new Revealed(stored, true, row!.Source, row.UpdatedAt);
        if (initial is not null)
            return new Revealed(initial, true, PasswordSources.Initial, null);
        return new Revealed(null, false, "", null);
    }

    /// <summary>Password cell of an Excel export: the password, or <see cref="UnknownExportText"/>
    /// (no account → empty — the login cell is empty too).</summary>
    public static string ExportCell(Revealed r, bool hasAccount) =>
        !hasAccount ? "" : r.Known ? r.Password ?? "" : UnknownExportText;

    /// <summary>Startup backfill: a student account with a still-unused initial password and no vault row.</summary>
    public static bool NeedsBackfill(string? role, string? initialPassword, bool hasRow) =>
        !hasRow && string.Equals(role, Roles.Student, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(initialPassword);
}
