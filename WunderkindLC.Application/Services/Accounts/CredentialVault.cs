using System.Security.Cryptography;
using System.Text;

namespace WunderkindLC.Application.Services.Accounts;

/// <summary>
/// STUDENT PASSWORD VAULT — encrypts student account passwords for the database (AES-256-GCM), so that
/// administrators can always see a student's current login and password (owner decision, 2026-10-11;
/// docs/SPEC-crm-upgrade.md §7 P6.1). A clone of <see cref="FaceVault"/>.
///
/// <para><b>Key — ONLY in <c>.env</c>:</b> <c>CREDENTIAL_VAULT_KEY</c> (base64, 32 bytes) →
/// <see cref="AppSecrets.CredentialVaultKey"/>. Never in the database or the UI, so a database dump alone
/// does not reveal any password. DataProtection keys (the <c>dpkeys</c> volume) are NOT used on purpose:
/// that volume is outside the backups.</para>
///
/// <para>⚠️ <b>Fail-closed:</b> without a valid key <see cref="Configured"/> is false and NOTHING is stored —
/// there is no "store it in plain text" fallback. <see cref="Unprotect"/> never throws: a blob encrypted
/// with another key, a corrupt blob or an unknown version gives <c>null</c> ("not stored").</para>
///
/// <para><b>Blob format:</b> <c>[1 byte version = 1][12 byte nonce][16 byte GCM tag][ciphertext, UTF-8]</c>.</para>
/// </summary>
public sealed class CredentialVault
{
    /// <summary>AES-256 — the key must be exactly 32 bytes.</summary>
    public const int KeyBytes = 32;

    /// <summary>Current blob format version.</summary>
    public const byte FormatVersion = 1;

    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 1 + NonceBytes + TagBytes;

    private readonly byte[]? _key;

    /// <summary>Key from a base64 string. Empty/corrupt/wrong length is NOT an exception — the vault is
    /// simply "not configured".</summary>
    public CredentialVault(string? base64Key)
    {
        _key = TryDecodeKey(base64Key);
    }

    /// <summary>With <c>CREDENTIAL_VAULT_KEY</c> from <c>.env</c> (DI creates it this way).</summary>
    public static CredentialVault FromSecrets() => new(AppSecrets.CredentialVaultKey);

    /// <summary>Key present and valid. False → passwords are not stored.</summary>
    public bool Configured => _key is not null;

    /// <summary>A fresh key (for the deploy docs / an admin hint).</summary>
    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>Is the base64 string a valid 32-byte key.</summary>
    public static bool IsValidKey(string? base64Key) => TryDecodeKey(base64Key) is not null;

    private static byte[]? TryDecodeKey(string? base64Key)
    {
        var s = (base64Key ?? "").Trim();
        if (s.Length == 0) return null;
        try
        {
            var raw = Convert.FromBase64String(s);
            return raw.Length == KeyBytes ? raw : null;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// Encrypts a password. ⚠️ Throws <see cref="InvalidOperationException"/> when the key is missing — a
    /// CODE error: callers check <see cref="Configured"/> first (the interceptor stores nothing then).
    /// </summary>
    public byte[] Protect(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        if (_key is null)
            throw new InvalidOperationException(
                "CREDENTIAL_VAULT_KEY sozlanmagan — parolni shifrlab bo'lmaydi (saqlash o'chiq bo'lishi kerak edi).");

        var data = Encoding.UTF8.GetBytes(plain);
        var blob = new byte[HeaderBytes + data.Length];
        blob[0] = FormatVersion;
        var nonce = blob.AsSpan(1, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var gcm = new AesGcm(_key, TagBytes);
        gcm.Encrypt(nonce, data, blob.AsSpan(HeaderBytes, data.Length), blob.AsSpan(1 + NonceBytes, TagBytes));
        CryptographicOperations.ZeroMemory(data);
        return blob;
    }

    /// <summary>
    /// Decrypts a blob. <b>Never throws</b> — no key, a blob from another key, a corrupt or too short blob or an
    /// unknown version → <c>null</c> (shown as "not stored"; the admin can regenerate the password).
    /// </summary>
    public string? Unprotect(byte[]? blob)
    {
        if (_key is null || blob is null) return null;
        if (blob.Length < HeaderBytes) return null;
        if (blob[0] != FormatVersion) return null;

        var cipherLen = blob.Length - HeaderBytes;
        var plain = new byte[cipherLen];
        try
        {
            using var gcm = new AesGcm(_key, TagBytes);
            gcm.Decrypt(
                blob.AsSpan(1, NonceBytes),
                blob.AsSpan(HeaderBytes, cipherLen),
                blob.AsSpan(1 + NonceBytes, TagBytes),
                plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }   // tag mismatch = other key or corrupt blob
        catch (ArgumentException) { return null; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
