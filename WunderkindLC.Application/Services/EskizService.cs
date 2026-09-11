using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Eskiz.uz SMS shlyuzi (https://notify.eskiz.uz).
///
/// <para><b>Login/parol — faqat .env dan</b> (<c>ESKIZ_EMAIL</c> / <c>ESKIZ_PASSWORD</c>,
/// <see cref="AppSecrets"/>): bazada saqlanmaydi, UI'dan kiritilmaydi. Jo'natuvchi nomi (sender)
/// maxfiy emas — u CenterMeta'da qoladi va admin UI'dan o'zgartiradi.</para>
///
/// <para>Bearer token XOTIRADA keshlanadi (~29 kun; xizmat singleton) — ilgari CenterMeta'da
/// saqlanardi. Restartdan keyin bir marta qayta login qilinadi, 401 qaytsa token yangilanib bitta
/// qayta urinish bo'ladi. IHttpClientFactory ishlatiladi.</para>
/// </summary>
public class EskizService(
    IConfiguration config, IHttpClientFactory httpFactory, ILogger<EskizService> logger)
{
    // Keshlangan Bearer token (xotirada — bazaga yozilmaydi). Singleton bo'lgani uchun butun ilova
    // uchun bitta; parallel SMS yuborishlarda poyga bo'lmasin deb qulf bilan yangilanadi.
    // volatile: qulf FAQAT yangilashda olinadi, o'qish (Cached) esa qulfsiz — ayni paytdagi
    // qiymatni ko'rishi uchun.
    private volatile string _token = "";
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static string Email => AppSecrets.EskizEmail;
    private static string Password => AppSecrets.EskizPassword;

    /// <summary>Login/parol .env'da berilganmi (parametr eski imzoni saqlash uchun — e'tiborsiz).</summary>
    public bool IsConfigured(CenterMeta? m = null) => AppSecrets.EskizConfigured;

    /// <summary>
    /// Jo'natuvchi nomi (sender). Tartib: CenterMeta → `.env` (`Eskiz:From`) → "4546".
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>To'ldirilmagan NAMUNA qiymat sender sifatida ishlatilmaydi</b> — u Eskiz tomonidan
    /// tasdiqlanmagan va SMS'ni rad ettirishi mumkin. Prodda `EskizFrom` da aynan
    /// "tasdiqlangan_nikname" (o'rniga ism yozilishi kerak bo'lgan namuna matn) turgani
    /// topilgan: markaz nomi ko'rinmasdi va buni hech narsa ko'rsatmasdi. Endi bunday qiymat
    /// "kiritilmagan" deb hisoblanadi va "4546" ga tushadi — SMS baribir ketaveradi.
    /// Tasdiqlangan nomlar ro'yxati <see cref="GetNicknamesAsync"/> bilan olinadi va
    /// Sozlamalar sahifasida ko'rsatiladi.
    /// </remarks>
    public string SenderOf(CenterMeta? m) =>
        Usable(m?.EskizFrom) ?? Usable(config["Eskiz:From"]) ?? "4546";

    /// <summary>Namuna/bo'sh bo'lmagan haqiqiy qiymat, aks holda null.</summary>
    private static string? Usable(string? from) =>
        !string.IsNullOrWhiteSpace(from) && !IsPlaceholderSender(from) ? from.Trim() : null;

    /// <summary>Hujjat/namuna matni sender sifatida yozib qo'yilganmi.</summary>
    /// <remarks>Ro'yxat ATAYIN qisqa va ANIQ: haqiqiy nomni tasodifan rad etmasin.</remarks>
    public static bool IsPlaceholderSender(string? from)
    {
        var v = (from ?? "").Trim().ToLowerInvariant();
        return v is "tasdiqlangan_nikname" or "tasdiqlangan_nickname" or "nickname" or "nikname";
    }

    /// <summary>Ko'rsatish uchun amaldagi email (.env dan).</summary>
    public string DisplayEmail(CenterMeta? m = null) => Email;

    private string BaseUrl => (config["Eskiz:BaseUrl"] ?? "https://notify.eskiz.uz").TrimEnd('/');
    private HttpClient Client() => httpFactory.CreateClient("eskiz");

    /// <summary>Telefonni Eskiz formatiga keltiradi: faqat raqam, 998 prefiks bilan (998901234567).</summary>
    public static string NormalizePhone(string? phone)
    {
        var d = PhoneUtil.DigitsOnly(phone);
        if (d.Length == 9) d = "998" + d;                       // 901234567 -> 998901234567
        else if (d.Length > 9 && !d.StartsWith("998")) d = "998" + d[^9..];
        return d;
    }

    /// <summary>Keshlangan (yaroqli) tokenni qaytaradi; yo'q/eskirgan yoki <paramref name="forceRefresh"/>
    /// bo'lsa qayta login qiladi. Kesh XOTIRADA (bazaga yozilmaydi). (token, error).
    /// <paramref name="db"/> endi kerak emas — imzo chaqiruvchilar uchun saqlangan.</summary>
    public async Task<(string? Token, string? Error)> GetTokenAsync(
        IAppDbContext? db = null, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!AppSecrets.EskizConfigured)
            return (null, $"Eskiz login/parol sozlanmagan (.env: {AppSecrets.EnvKeys.EskizEmail} / {AppSecrets.EnvKeys.EskizPassword}).");

        if (!forceRefresh && Cached() is { } cached) return (cached, null);

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Qulfni kutayotganda boshqa so'rov yangilagan bo'lishi mumkin.
            if (!forceRefresh && Cached() is { } fresh) return (fresh, null);

            using var form = new MultipartFormDataContent
            {
                { new StringContent(Email), "email" },
                { new StringContent(Password), "password" },
            };
            var resp = await Client().PostAsync($"{BaseUrl}/api/auth/login", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (null, $"Eskiz login xato ({(int)resp.StatusCode}). .env dagi login/parolni tekshiring.");
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("token", out var tok) || tok.GetString() is not { Length: > 0 } token)
                return (null, "Eskiz token qaytmadi.");

            _token = token;
            _tokenExpiresAt = DateTime.UtcNow.AddDays(29);
            return (token, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eskiz login xatosi");
            return (null, "Eskiz bilan bog'lanishda xatolik.");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Xotiradagi token — hali kamida bir kun yaroqli bo'lsa. Aks holda null.</summary>
    private string? Cached() =>
        !string.IsNullOrEmpty(_token) && _tokenExpiresAt > DateTime.UtcNow.AddDays(1) ? _token : null;

    public record SmsResult(bool Ok, string RequestId, string Status, string? Error);

    /// <summary>Bitta raqamga SMS yuboradi (token + 401 retry bilan). RequestId/status qaytaradi.</summary>
    public async Task<SmsResult> SendSmsAsync(
        IAppDbContext db, string phone, string message, string? callbackUrl = null, CancellationToken ct = default)
    {
        if (!AppSecrets.EskizConfigured)
            return new SmsResult(false, "", "error", "Eskiz sozlanmagan (.env: ESKIZ_EMAIL / ESKIZ_PASSWORD).");
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        var from = SenderOf(meta);
        var mobile = NormalizePhone(phone);
        if (mobile.Length < 12) return new SmsResult(false, "", "error", "Telefon raqami noto'g'ri.");

        var (token, err) = await GetTokenAsync(db, false, ct);
        if (token is null) return new SmsResult(false, "", "error", err);

        var (result, unauthorized) = await TrySendAsync(token, mobile, message, from, callbackUrl, ct);
        if (unauthorized)
        {
            // Token eskirgan/yaroqsiz — yangilab bir marta qayta urinamiz.
            (token, err) = await GetTokenAsync(db, true, ct);
            if (token is null) return new SmsResult(false, "", "error", err);
            (result, _) = await TrySendAsync(token, mobile, message, from, callbackUrl, ct);
        }
        return result;
    }

    private async Task<(SmsResult Result, bool Unauthorized)> TrySendAsync(
        string token, string mobile, string message, string from, string? callbackUrl, CancellationToken ct)
    {
        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(mobile), "mobile_phone" },
                { new StringContent(message), "message" },
                { new StringContent(from), "from" },
            };
            if (!string.IsNullOrWhiteSpace(callbackUrl))
                form.Add(new StringContent(callbackUrl), "callback_url");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/message/sms/send")
            { Content = form };
            req.Headers.Add("Authorization", $"Bearer {token}");
            var resp = await Client().SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (new SmsResult(false, "", "error", "Token yaroqsiz."), true);

            if (!resp.IsSuccessStatusCode)
            {
                // ⚠️ Ilgari HTTP xato javoblari (400/402 va h.k.) LOGGA UMUMAN tushmasdi — faqat
                // SmsLogs.Status ga. Natijada "nega ketmadi" savoliga log'dan javob yo'q edi.
                // Javob tanasi ham yoziladi (unda Eskiz'ning ANIQ sababi bo'ladi: balans,
                // moderatsiyadan o'tmagan matn, tasdiqlanmagan sender...). Telefon RAQAMI
                // yozilmaydi — log'da shaxsiy ma'lumot saqlamaymiz.
                logger.LogWarning("Eskiz SMS rad etdi: HTTP {Status}, javob: {Body}",
                    (int)resp.StatusCode, body.Length > 500 ? body[..500] : body);
                var msg = TryGetMessage(body) ?? $"Eskiz xato ({(int)resp.StatusCode}).";
                return (new SmsResult(false, "", "error", msg), false);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? idEl.ToString()) : "";
            var status = root.TryGetProperty("status", out var stEl) ? (stEl.GetString() ?? "") : "waiting";
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(status))
                return (new SmsResult(false, "", "error", TryGetMessage(body) ?? "Noma'lum javob."), false);
            return (new SmsResult(true, id, string.IsNullOrEmpty(status) ? "waiting" : status, null), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Chaqiruvchi bekor qildi (masalan foydalanuvchi sahifani yopdi) — bu XATO emas.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // ⚠️ TIMEOUT alohida ajratiladi: `TaskCanceledException` ni umumiy "Yuborishda
            // xatolik" deb ko'rsatish operatorni chalg'itardi — u Eskiz RAD ETDI deb o'ylab,
            // matnni yoki raqamni qayta-qayta o'zgartirib ko'rardi. Aslida javob KELMAGAN.
            logger.LogWarning(ex, "Eskiz javob bermadi (timeout) — so'rov bekor qilindi");
            return (new SmsResult(false, "", "error",
                "Eskiz javob bermadi (vaqt tugadi). Bu rad etish EMAS — biroz kutib qayta urining."), false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eskiz SMS yuborish xatosi");
            return (new SmsResult(false, "", "error", "Yuborishda xatolik."), false);
        }
    }

    /// <summary>
    /// Eskiz kabinetida TASDIQLANGAN jo'natuvchi nomlari (nickname). Bo'sh ro'yxat = hech biri
    /// tasdiqlanmagan (SMS "4546" dan ketadi, markaz nomi ko'rinmaydi).
    /// </summary>
    /// <remarks>
    /// ⚠️ Sozlamalar sahifasi shu ro'yxatni ko'rsatadi. Ilgari "Jo'natuvchi nomi" ERKIN MATN
    /// maydoni edi: admin istalgan narsani yozib qo'yardi (prodda "tasdiqlangan_nikname" namuna
    /// matni turgan) va tasdiqlangani bor-yo'qligini bilishning UMUMAN yo'li yo'q edi.
    /// </remarks>
    public async Task<List<string>> GetNicknamesAsync(IAppDbContext? db = null, CancellationToken ct = default)
    {
        var (token, _) = await GetTokenAsync(db, false, ct);
        if (token is null) return [];
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/nick/me");
            req.Headers.Add("Authorization", $"Bearer {token}");
            var resp = await Client().SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            // Javob ikki ko'rinishda bo'lishi mumkin: to'g'ridan-to'g'ri massiv yoki {data:[...]}.
            var arr = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d
                : default;
            if (arr.ValueKind != JsonValueKind.Array) return [];

            var names = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.String ? item.GetString()
                    : item.TryGetProperty("nickname", out var n) ? n.GetString()
                    : item.TryGetProperty("name", out var n2) ? n2.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name!.Trim());
            }
            return names;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eskiz nickname ro'yxati olinmadi");
            return [];
        }
    }

    /// <summary>Hisobdagi qoldiq (balans). Xato bo'lsa null.</summary>
    public async Task<decimal?> GetBalanceAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var (token, _) = await GetTokenAsync(db, false, ct);
        if (token is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/user/get-limit");
            req.Headers.Add("Authorization", $"Bearer {token}");
            var resp = await Client().SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("balance", out var bal))
                return bal.ValueKind == JsonValueKind.Number ? bal.GetDecimal()
                    : decimal.TryParse(bal.GetString(), out var b) ? b : null;
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eskiz balans xatosi");
            return null;
        }
    }

    private static string? TryGetMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return m.ValueKind == JsonValueKind.String ? m.GetString() : m.ToString();
        }
        catch { /* ignore */ }
        return null;
    }
}
