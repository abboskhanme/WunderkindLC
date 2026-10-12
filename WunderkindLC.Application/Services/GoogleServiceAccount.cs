using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Google service account JSON parser (client_email + private_key + project_id).
///
/// <para>Used by FACE LOGIN app attestation (<see cref="AppAttestation"/>, Google Play Integrity).
/// The key comes from <c>.env</c> (<c>PLAY_INTEGRITY_SA_JSON</c>, falling back to
/// <c>FCM_SERVICE_ACCOUNT_JSON</c> — see <see cref="AppSecrets"/>).</para>
///
/// <para>History: this parser used to live in <c>FcmService</c> (Firebase push). Push was removed
/// in 2026-10 (no installable app is used); only the parser is still needed.</para>
/// </summary>
public static class GoogleServiceAccount
{
    public readonly record struct Creds(string ClientEmail, string PrivateKey, string ProjectId);

    /// <summary>True when the JSON is a usable service account (all three fields present).</summary>
    public static bool IsConfigured(string? json) => TryParse(json, out _);

    public static bool TryParse(string? json, out Creds creds)
    {
        creds = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var email = root.TryGetProperty("client_email", out var e) ? e.GetString() ?? "" : "";
            var key = root.TryGetProperty("private_key", out var k) ? k.GetString() ?? "" : "";
            var project = root.TryGetProperty("project_id", out var p) ? p.GetString() ?? "" : "";
            if (email.Length == 0 || key.Length == 0 || project.Length == 0) return false;
            creds = new Creds(email, key, project);
            return true;
        }
        catch { return false; }
    }
}
