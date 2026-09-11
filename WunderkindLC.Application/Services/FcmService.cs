using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Firebase Cloud Messaging (FCM HTTP v1) orqali ilovaga push yuboradi. Hisob ma'lumotlari —
/// Firebase service account JSON (.env: FCM_SERVICE_ACCOUNT_JSON — <see cref="AppSecrets"/>;
/// bazada saqlanmaydi va UI'dan kiritilmaydi). Bitta Firebase loyiha ikkala app (parent/teacher) uchun ishlaydi — token o'zi
/// to'g'ri ilovaga yetadi. OAuth access token xotirada keshlanadi (~1 soat).
/// Tashqi paket talab qilmaydi (BCL: RSA + HttpClient).
/// </summary>
public class FcmService(IHttpClientFactory httpFactory, ILogger<FcmService> logger)
{
    public readonly record struct Creds(string ClientEmail, string PrivateKey, string ProjectId);

    private readonly object _lock = new();
    private string _cachedEmail = "";
    private string _cachedToken = "";
    private DateTime _cachedExpiry = DateTime.MinValue;

    /// <summary>JSON to'g'ri service account ekanini tekshiradi.</summary>
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

    /// <summary>Push yuborish natijasi: yuborilganlar soni + BOSHQA yaroqsiz (o'lik) tokenlar ro'yxati.
    /// <see cref="InvalidTokens"/> — FCM "ro'yxatda yo'q" (404/UNREGISTERED) deb qaytargan tokenlar; ular
    /// ilova o'chirilgan yoki web token bekor qilingan qurilmalar — bazadan o'chirib tashlash kerak.</summary>
    public readonly record struct SendResult(int Sent, IReadOnlyList<string> InvalidTokens);

    /// <summary>Berilgan tokenlarga push (title/body) yuboradi. Yuborilganlar soni va yaroqsiz
    /// tokenlar ro'yxatini qaytaradi (chaqiruvchi ularni bazadan o'chirishi mumkin).</summary>
    public async Task<SendResult> SendAsync(
        string serviceAccountJson, IReadOnlyCollection<string> tokens, string title, string body,
        CancellationToken ct = default)
    {
        if (!TryParse(serviceAccountJson, out var c) || tokens.Count == 0)
            return new SendResult(0, []);

        string accessToken;
        try { accessToken = await GetAccessTokenAsync(c, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "FCM access token olishda xato"); return new SendResult(0, []); }

        var client = httpFactory.CreateClient();
        var url = $"https://fcm.googleapis.com/v1/projects/{c.ProjectId}/messages:send";
        var sent = 0;
        var invalid = new List<string>();
        foreach (var token in tokens)
        {
            try
            {
                var payload = new { message = new { token, notification = new { title, body } } };
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) { sent++; continue; }

                // Xatoni KO'RINADIGAN qilamiz: FCM aniq sababni qaytaradi (token yaroqsiz,
                // loyiha mos kelmaydi (SenderId mismatch), ruxsat yo'q va h.k.).
                var err = await resp.Content.ReadAsStringAsync(ct);
                // 404 (NOT_FOUND) yoki UNREGISTERED — token o'lik (ilova o'chirilgan / web token bekor).
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    err.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase))
                {
                    invalid.Add(token);
                    logger.LogInformation("FCM: o'lik token o'chirishga belgilandi ({Status})", (int)resp.StatusCode);
                }
                else
                {
                    logger.LogWarning("FCM push rad etildi ({Status}): {Body}", (int)resp.StatusCode, err);
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "FCM push yuborishda xato"); }
        }
        return new SendResult(sent, invalid);
    }

    /// <summary>
    /// Bitta tokenga DATA-message (notification YO'Q) yuboradi — ilovani fonda uyg'otish uchun
    /// (masalan CTI agent-ilovaga <c>dial</c> buyrug'ini yetkazish). <c>android.priority = "high"</c>
    /// — Doze rejimida ham darhol yetadi. Muvaffaqiyat/xato — <see cref="SendAsync"/> uslubida loglanadi.
    /// </summary>
    public async Task<bool> SendDataAsync(
        string serviceAccountJson, string token, IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default)
    {
        if (!TryParse(serviceAccountJson, out var c) || string.IsNullOrWhiteSpace(token))
            return false;

        string accessToken;
        try { accessToken = await GetAccessTokenAsync(c, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "FCM access token olishda xato"); return false; }

        var client = httpFactory.CreateClient();
        var url = $"https://fcm.googleapis.com/v1/projects/{c.ProjectId}/messages:send";
        try
        {
            var payload = new { message = new { token, data, android = new { priority = "high" } } };
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true;
            var err = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("FCM data-push rad etildi ({Status}): {Body}", (int)resp.StatusCode, err);
            return false;
        }
        catch (Exception ex) { logger.LogWarning(ex, "FCM data-push yuborishda xato"); return false; }
    }

    private async Task<string> GetAccessTokenAsync(Creds c, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_cachedEmail == c.ClientEmail && DateTime.UtcNow < _cachedExpiry)
                return _cachedToken;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = c.ClientEmail,
            scope = "https://www.googleapis.com/auth/firebase.messaging",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600,
        }));
        var unsigned = $"{header}.{claims}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(c.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var jwt = $"{unsigned}.{B64Url(signature)}";

        var client = httpFactory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt,
        });
        var resp = await client.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString() ?? "";
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        lock (_lock)
        {
            _cachedEmail = c.ClientEmail;
            _cachedToken = token;
            _cachedExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }
        return token;
    }

    private static string B64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
