using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Telegram <b>Mini App</b> (<c>window.Telegram.WebApp.initData</c>) imzosini tekshiradi.
///
/// <para>Mini App ichidagi sahifa oddiy statik HTML — u yerda "login" yo'q. Telegram sahifaga
/// imzolangan <c>initData</c> satrini beradi; server uni BOT TOKENI bilan qayta hisoblab
/// haqiqiyligini tasdiqlaydi. Rasmiy algoritm:</para>
/// <code>
/// secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)
/// hash       = HMAC_SHA256(key: secret_key,  data: data_check_string)
/// </code>
/// <para><c>data_check_string</c> — <c>hash</c>dan tashqari barcha maydonlar <c>key=value</c>
/// ko'rinishida, kalit bo'yicha alifbo tartibida, <c>\n</c> bilan ulangan holda.</para>
///
/// <para>Qo'shimcha himoya: <c>auth_date</c> eskirgan bo'lsa (default 24 soat) rad etiladi —
/// bir marta o'g'irlangan satr abadiy ishlamasin.</para>
/// </summary>
public static class TelegramInitData
{
    /// <param name="ChatId">Telegram foydalanuvchi (= shaxsiy chat) id'si.</param>
    public record User(long ChatId, string Username, string FirstName, string LastName);

    /// <summary>Imzoni tekshiradi. Yaroqli bo'lsa foydalanuvchi ma'lumoti, aks holda <c>null</c>.</summary>
    /// <param name="initData">Telegram bergan xom so'rov satri (URL-encoded).</param>
    /// <param name="botToken">Mini App QAYSI botga tegishli bo'lsa — o'sha botning tokeni.</param>
    /// <param name="maxAge">Ruxsat etilgan eng katta "yosh" (default 24 soat).</param>
    public static User? Validate(string? initData, string botToken, TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(initData) || string.IsNullOrWhiteSpace(botToken)) return null;

        try
        {
            // Xom satrni o'zimiz ajratamiz (tayyor query-parser emas): har juftlik alohida kerak va
            // dekodlash `encodeURIComponent`ning teskarisi bo'lishi shart — `Uri.UnescapeDataString`
            // (`+` belgisini bo'sh joyga AYLANTIRMAYDI, aks holda imzo mos kelmay qolardi).
            var pairs = new List<(string Key, string Value)>();
            string? hash = null;
            foreach (var part in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part[..eq];
                var value = Uri.UnescapeDataString(part[(eq + 1)..]);
                if (key == "hash") { hash = value; continue; }
                if (key == "signature") continue; // Telegram'ning yangi (Ed25519) imzosi — HMAC hisobiga kirmaydi
                pairs.Add((key, value));
            }
            if (string.IsNullOrEmpty(hash) || pairs.Count == 0) return null;

            var dataCheckString = string.Join('\n',
                pairs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));

            var secretKey = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
            var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
            var expected = Convert.ToHexString(computed).ToLowerInvariant();

            // Vaqt bo'yicha oshkor bo'lmaydigan taqqoslash.
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(hash.ToLowerInvariant())))
                return null;

            // auth_date — eskirmaganini tekshiramiz.
            var authRaw = pairs.FirstOrDefault(p => p.Key == "auth_date").Value;
            if (long.TryParse(authRaw, out var authUnix))
            {
                var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authUnix);
                if (age > (maxAge ?? TimeSpan.FromHours(24)) || age < TimeSpan.FromMinutes(-5)) return null;
            }

            var userJson = pairs.FirstOrDefault(p => p.Key == "user").Value;
            if (string.IsNullOrWhiteSpace(userJson)) return null;

            using var doc = JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return null;

            string Str(string prop) =>
                root.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

            return new User(idEl.GetInt64(), Str("username"), Str("first_name"), Str("last_name"));
        }
        catch
        {
            return null;
        }
    }
}
