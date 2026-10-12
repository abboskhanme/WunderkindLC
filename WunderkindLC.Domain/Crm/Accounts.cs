using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace WunderkindLC.Domain;

/*
 * CRM upgrade — Phase 6 «Accounts» (docs/SPEC-crm-upgrade.md §7, unit U6a): permanently visible
 * STUDENT passwords. Rule file: .claude/rules/auth-sessions.md §6.
 */

/// <summary>Where a stored student password came from (<see cref="StudentCredential.Source"/>).</summary>
public static class PasswordSources
{
    /// <summary>Account created (student form, Excel import, lead conversion, credentials auto-create).</summary>
    public const string Create = "create";
    /// <summary>A «reset password» endpoint or the group «Paroli noma'lumlarga yangi parol».</summary>
    public const string Reset = "reset";
    /// <summary>An admin typed a new password (student edit form, restore from archive with a password).</summary>
    public const string AdminSet = "admin_set";
    /// <summary>The student changed it in the cabinet.</summary>
    public const string SelfChange = "self_change";
    /// <summary>One-time startup copy of a still-unused <c>AppUser.InitialPassword</c>.</summary>
    public const string Backfill = "backfill";
    /// <summary>Not stored in the vault — shown from <c>AppUser.InitialPassword</c> (reveal only).</summary>
    public const string Initial = "initial";
}

/// <summary>
/// A password change waiting for the next <c>SaveChanges</c> of the same context. Set ONLY by
/// <c>AccountExtensions</c> (<c>SetInitialPassword</c>, <c>SetOwnPassword</c>, <c>BlockLogin</c>) and consumed
/// by <c>StudentCredentialInterceptor</c>, which stores/deletes the vault row in the same transaction.
/// </summary>
/// <param name="Kind"><see cref="KindSet"/> or <see cref="KindClear"/>.</param>
/// <param name="Plain">The new plain password (<see cref="KindSet"/> only).</param>
/// <param name="Source">One of <see cref="PasswordSources"/>.</param>
public sealed record PendingPasswordChange(string Kind, string? Plain, string Source)
{
    public const string KindSet = "set";
    public const string KindClear = "clear";

    public static PendingPasswordChange Set(string plain, string source) => new(KindSet, plain, source);
    public static PendingPasswordChange Clear() => new(KindClear, null, "");

    /// <summary>⚠️ Never prints the password — a record's generated ToString would (logs, debugger, exceptions).</summary>
    public override string ToString() => $"PendingPasswordChange {{ Kind = {Kind}, Source = {Source} }}";
}

public partial class AppUser
{
    /// <summary>
    /// Password change for <c>StudentCredentialInterceptor</c> (not a column; never serialised — archive
    /// snapshots, API responses and the backup must not carry the plain password).
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public PendingPasswordChange? PendingPassword { get; set; }
}

/// <summary>
/// The current password of a STUDENT account, AES-256-GCM encrypted (<c>CredentialVault</c>, key only in
/// <c>.env</c> → <c>CREDENTIAL_VAULT_KEY</c>). Table <c>StudentCredentials</c>, one row per account.
///
/// <para>⚠️ Never in the JSON backup (<c>BackupService.ExcludedTables</c>), never in any MCP <c>Reads</c>
/// (the column name contains "Password" — the read-only role never gets it), never in logs or audit.
/// Parents' (role <c>parent</c>) passwords are NOT stored (SPEC §10 #10).</para>
/// </summary>
public class StudentCredential
{
    /// <summary>The student's <see cref="AppUser.Id"/> (primary key).</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Vault blob: <c>[1 byte version=1][12 nonce][16 tag][ciphertext UTF-8]</c>.
    /// ⚠️ Keep "Password" in the name — the MCP read-only role and <c>SecretScrub</c> close it by name.</summary>
    public byte[] PasswordCipher { get; set; } = Array.Empty<byte>();
    /// <summary><see cref="PasswordSources"/>: create · reset · admin_set · self_change · backfill.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>When the password was stored (centre time, <c>AppClock.Now</c>).</summary>
    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}
