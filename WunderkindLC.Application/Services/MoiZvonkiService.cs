using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// MoiZvonki (moizvonki.ru) REST mijozi — Call Center'ning bulutli telefoniya transporti.
/// Operator telefonidagi MoiZvonki ilovasi (SIM bilan) qo'ng'iroqni bajaradi; CRM esa REST
/// orqali buyuradi (calls.make_call) va webhook orqali holat/yozuvlarni qabul qiladi
/// (<see cref="Controllers"/> dagi TelephonyWebhookController).
///
/// Sozlash (env, masalan <c>MoiZvonki__Domain</c>):
///   "MoiZvonki": { "Enabled", "Domain" ("kompaniya" yoki to'liq "kompaniya.moizvonki.ru"),
///     "UserName" (kabinet email), "ApiKey" (kabinet → Sozlamalar → Integratsiya),
///     "WebhookSecret" (ixtiyoriy — bo'sh bo'lsa ApiKey'dan avtomatik hosil qilinadi) }
///
/// API: POST https://{domain}.moizvonki.ru/api/v1, tana: {user_name, api_key, action, ...}.
/// DIQQAT: api_key FOYDALANUVCHIGA tegishli — calls.make_call o'sha foydalanuvchi telefonidan
/// teradi. V1: bitta operator akkaunti (ko'p operator — kelajakda per-user kalitlar).
/// </summary>
public class MoiZvonkiService(IConfiguration config, IHttpClientFactory httpFactory, ILogger<MoiZvonkiService> logger)
{
    public bool Enabled => config.GetValue<bool>("MoiZvonki:Enabled");
    private string Domain => config["MoiZvonki:Domain"] ?? "";
    private string UserName => config["MoiZvonki:UserName"] ?? "";
    private string ApiKey => config["MoiZvonki:ApiKey"] ?? "";

    public bool IsConfigured =>
        Enabled && Domain.Length > 0 && UserName.Length > 0 && ApiKey.Length > 0;

    /// <summary>API bazaviy manzili — Domain subdomain ("kompaniya"), to'liq host
    /// ("kompaniya.moizvonki.ru") yoki to'liq URL ("http(s)://...", test/proxy uchun).</summary>
    private string ApiUrl =>
        Domain.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? $"{Domain.TrimEnd('/')}/api/v1"
            : $"https://{(Domain.Contains('.') ? Domain : Domain + ".moizvonki.ru")}/api/v1";

    /// <summary>
    /// Webhook URL'idagi sir — tashqi POST'lar faqat shu yo'l orqali qabul qilinadi.
    /// Berilmasa ApiKey'dan deterministik hosil qilinadi (sozlashni soddalashtirish uchun).
    /// </summary>
    public string WebhookSecret
    {
        get
        {
            var s = config["MoiZvonki:WebhookSecret"] ?? "";
            if (s.Length > 0) return s;
            if (ApiKey.Length == 0) return "";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes("izvonki:" + ApiKey));
            return Convert.ToHexString(hash)[..24].ToLowerInvariant();
        }
    }

    /// <summary>Umumiy REST chaqiruv: action + qo'shimcha maydonlar. Javob (ok, body-matn).</summary>
    public async Task<(bool Ok, string Body)> CallApiAsync(
        string action, Dictionary<string, object?>? extra = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return (false, "MoiZvonki sozlanmagan (MoiZvonki:Enabled/Domain/UserName/ApiKey)");

        var payload = new Dictionary<string, object?>
        {
            ["user_name"] = UserName,
            ["api_key"] = ApiKey,
            ["action"] = action,
        };
        if (extra is not null)
            foreach (var (k, v) in extra) payload[k] = v;

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            var resp = await http.PostAsync(ApiUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("MoiZvonki {Action} xato: HTTP {Code} {Body}", action, (int)resp.StatusCode, body);
                return (false, body.Length > 0 ? body : $"HTTP {(int)resp.StatusCode}");
            }
            return (true, body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MoiZvonki {Action} ulanish xatosi", action);
            return (false, $"MoiZvonki bilan ulanish xatosi: {ex.Message}");
        }
    }

    /// <summary>Chiquvchi qo'ng'iroq — operator (UserName akkaunti) telefonidan raqamga teriladi.</summary>
    public Task<(bool Ok, string Body)> MakeCallAsync(string toDigits, CancellationToken ct = default) =>
        CallApiAsync("calls.make_call", new Dictionary<string, object?> { ["to"] = toDigits }, ct);

    /// <summary>
    /// Webhook obunasi — call.start/answer/finish hodisalari bizning URL'ga yuborilsin.
    /// Sxemaga moslik uchun bir nechta keng tarqalgan shakl BIRGA yuboriladi (ortiqcha
    /// maydonlar odatda e'tiborsiz qoladi); provayder rad etsa javob matni logda ko'rinadi.
    /// </summary>
    public Task<(bool Ok, string Body)> SubscribeWebhooksAsync(string url, CancellationToken ct = default) =>
        CallApiAsync("webhook.subscribe", new Dictionary<string, object?>
        {
            ["url"] = url,
            ["events"] = new[] { "call.start", "call.answer", "call.finish" },
            ["call.start"] = url,
            ["call.answer"] = url,
            ["call.finish"] = url,
        }, ct);
}
