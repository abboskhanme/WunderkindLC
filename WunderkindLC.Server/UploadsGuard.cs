using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace WunderkindLC.Server;

/// <summary>
/// `/uploads` DARVOZASI — yuklangan fayllar endi login talab qiladi.
///
/// <para><b>Muammo:</b> `/uploads` statik papka autentifikatsiyasiz berilardi. Manzil tasodifiy
/// GUID bo'lsa ham, uni bir marta olgan odam faylni <b>abadiy</b> ola olardi — tizimdan
/// chiqarilsa ham, ishdan bo'shatilsa ham.</para>
///
/// <para><b>Nega cookie?</b> Loyihada JWT Bearer ishlatiladi (cookie yo'q edi), brauzer esa
/// <c>&lt;img src="/uploads/..."&gt;</c> ga <c>Authorization</c> sarlavhasini YUBORMAYDI. Shuning
/// uchun login qilgan foydalanuvchiga <c>Path=/uploads</c> ga cheklangan cookie qo'yiladi —
/// brauzer uni rasm so'rovlarida o'zi yuboradi va frontend kodiga tegish kerak bo'lmaydi.
/// Bu loyihadagi mavjud yondashuv bilan bir xil: <c>/ws</c> va SignalR ham sarlavha yubora
/// olmagani uchun tokenni boshqa yo'l bilan oladi.</para>
///
/// <para><b>Cookie qanday paydo bo'ladi?</b> Alohida "login" qadami YO'Q: SPA har qanday
/// avtorizatsiyalangan API so'rovi qilganda cookie o'z-o'zidan qo'yiladi
/// (<see cref="IssueCookie"/>). Ya'ni mavjud sessiyalar ham qayta login qilmasdan ishlayveradi.</para>
///
/// <para><b>DUAL-MODE (f69e0a1 dan keyin):</b> web SPA endi JWT'ni <c>Authorization</c> sarlavhasi
/// o'rniga HttpOnly <c>at</c> cookie'da yuboradi (<see cref="AuthCookies"/>), mobil esa
/// ilgarigidek Bearer'da. Shuning uchun darvoza tokenni UCH manbadan qabul qiladi
/// (<see cref="TokensOf"/>): Bearer sarlavha → <c>up_at</c> cookie → <c>at</c> cookie, va
/// <see cref="IssueCookie"/> ham ikkala rejimda ishlaydi. Busiz cookie-rejimdagi foydalanuvchiga
/// barcha rasmlar 404 bo'lardi (aynan shu xato prodda kuzatilgan edi).</para>
///
/// <para><b>Nima OCHIQ qoladi:</b> markaz LOGOTIPI (login sahifasi, PWA manifesti, ochiq vakansiya
/// sahifasi) VA landing sahifasining ommaviy rasmlari — faol o'qituvchi surati, faol
/// sertifikat/natija rasmi hamda faol FIKR (testimonial) avatari. Landing login'siz ko'riladi,
/// ya'ni bu rasmlar yopiq bo'lsa mehmon sinuq rasm ko'rardi.
/// Printsip o'zgarmaydi: "ochiq" deb faqat markaz O'ZI ommaviy ko'rsatayotgan
/// fayl hisoblanadi (<c>IsActive=false</c> yozuvning rasmi darhol yopiladi). Ro'yxat bazadan
/// olinadi va keshlanadi.</para>
///
/// <para><b>Favqulodda o'chirish:</b> <c>Uploads:RequireAuth=false</c> — kodni qayta yig'masdan
/// eski xatti-harakatga qaytaradi. Rad etilgan har so'rov logga yoziladi.</para>
/// </summary>
public sealed class UploadsGuard(
    JwtOptions jwt,
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<UploadsGuard> logger)
{
    /// <summary>Darvoza yoqilganmi (standart — HA).</summary>
    public bool Enabled { get; } = config.GetValue("Uploads:RequireAuth", true);

    /// <summary>
    /// Ochiq fayllar ro'yxati shuncha vaqtda bir yangilanadi.
    ///
    /// <para>Logotip kamdan-kam o'zgaradi, landing sertifikatlari esa tez-tez qo'shiladi — shuning
    /// uchun TTL aynan shu ikkinchisiga qarab tanlangan: admin yangi sertifikat qo'shsa, u saytda
    /// ko'pi bilan 1 daqiqada ochiladi (va olib tashlansa — ko'pi bilan 1 daqiqada yopiladi).
    /// So'rov arzon: ikkita indekssiz skan emas, faqat bitta ustunli <c>Select</c>, va u
    /// trafikdan QAT'IY NAZAR daqiqada bir marta ketadi.</para>
    /// </summary>
    private static readonly TimeSpan PublicCacheTtl = TimeSpan.FromMinutes(1);

    private volatile IReadOnlyCollection<string> _publicNames = [];
    private DateTime _publicLoadedUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _publicLock = new(1, 1);

    /// <summary>
    /// Tekshirilgan tokenlar keshi: token → amal qilish muddati (UTC).
    /// Bitta sahifada o'nlab rasm bo'lishi mumkin — har biri uchun imzoni qaytadan tekshirmaymiz.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _tokenCache = new();

    private TokenValidationParameters Parameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
    };

    /// <summary>Token haqiqiymi (imzo/muddat/issuer/audience). Muddati — keshga yoziladi.</summary>
    private bool IsTokenValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        if (_tokenCache.TryGetValue(token, out var until))
        {
            if (until > DateTime.UtcNow) return true;
            _tokenCache.TryRemove(token, out _);   // muddati o'tgan — qaytadan tekshiriladi
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, Parameters(), out var validated);
            var expires = validated.ValidTo;
            if (expires <= DateTime.UtcNow) return false;

            // YUZ TASDIG'I KUTILAYOTGAN sessiya (scope=face) — `/uploads` ga KIRITILMAYDI.
            // Bu darvoza pipeline'da `UseAuthentication` dan OLDIN turadi, ya'ni `FaceScopeGate`
            // middleware'i bu so'rovni umuman ko'rmaydi. Busiz cheklangan token bilan
            // sarlavha orqali istalgan yuklangan faylni (passport skani, shartnoma) olib
            // bo'lardi — cheklangan token esa faqat SELFI yuborish uchun berilgan.
            if (validated is JwtSecurityToken jwtToken
                && jwtToken.Claims.Any(c => c.Type == JwtTokenService.FaceScopeClaimType
                                            && c.Value == JwtTokenService.FaceScopeClaimValue))
                return false;
            // Kesh cheksiz o'smasin — vaqti-vaqti bilan eskirganlarini tozalaymiz.
            if (_tokenCache.Count > 500)
                foreach (var (k, v) in _tokenCache)
                    if (v <= DateTime.UtcNow) _tokenCache.TryRemove(k, out _);
            _tokenCache[token] = expires;
            return true;
        }
        catch
        {
            return false;   // imzo/muddat/format — sabab muhim emas, natija bir xil
        }
    }

    /// <summary>
    /// So'rovdagi token NOMZODLARI, ustuvorlik tartibida:
    ///
    /// <para>1. <c>Authorization: Bearer</c> — mobil (Flutter) ilovalar va Bearer rejimidagi
    /// so'rovlar; 2. <c>up_at</c> cookie (<c>Path=/uploads</c>) — brauzer <c>&lt;img&gt;</c> ga
    /// sarlavha yubora olmagani uchun qo'yiladigan maxsus cookie; 3. <c>at</c> auth cookie
    /// (<see cref="AuthCookies.AtCookie"/>, <c>Path=/</c>).</para>
    ///
    /// <para><b>Nega <c>at</c> ham kerak (DUAL-MODE):</b> web SPA endi JWT'ni <c>Authorization</c>
    /// sarlavhasi O'RNIGA HttpOnly <c>at</c> cookie orqali yuboradi (<see cref="AuthCookies"/>).
    /// U <c>Path=/</c> bilan qo'yilgani uchun brauzer uni <c>/uploads</c> so'rovlariga O'ZI
    /// qo'shib yuboradi — o'qimaslik esa cookie-rejimdagi foydalanuvchiga rasmlarni 404 qilardi
    /// (aynan shu xato prodda bo'lgan edi: Bearer yo'q → <c>up_at</c> hech qachon qo'yilmasdi).</para>
    ///
    /// <para>⚠️ NOMZODLAR BIRMA-BIR tekshiriladi (faqat birinchi topilgani EMAS): access token
    /// 60 daqiqada eskiradi va refresh'dan keyin <c>at</c> yangilanadi, <c>up_at</c> esa keyingi
    /// API so'rovigacha ESKI qiymatda qolishi mumkin — eskirgan <c>up_at</c> tufayli yangi
    /// <c>at</c> bilan kelgan so'rov rad etilib qolmasin.</para>
    /// </summary>
    private static IEnumerable<string> TokensOf(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearer = header["Bearer ".Length..].Trim();
            if (bearer.Length > 0) yield return bearer;
        }

        var upAt = ctx.Request.Cookies[UploadAccessRules.CookieName];
        if (!string.IsNullOrEmpty(upAt)) yield return upAt;

        var at = ctx.Request.Cookies[AuthCookies.AtCookie];
        if (!string.IsNullOrEmpty(at)) yield return at;
    }

    /// <summary>
    /// Ochiq fayllar (logotip + landing sahifasining FAOL rasmlari) ro'yxati — keshdan, kerak
    /// bo'lsa bazadan yangilanadi.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> PublicNamesAsync()
    {
        if (DateTime.UtcNow - _publicLoadedUtc < PublicCacheTtl) return _publicNames;
        await _publicLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _publicLoadedUtc < PublicCacheTtl) return _publicNames;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var urls = new List<string?>();

            // TARTIB MUHIM: LOGOTIP birinchi. Ro'yxat chegarasiga (MaxPublicNames) yetilganda
            // qirqiladigan qism landing rasmlari bo'lsin — login sahifasi hech qachon buzilmasin.
            urls.AddRange(await db.CenterMeta.AsNoTracking().Select(m => m.LogoUrl).ToListAsync());
            urls.AddRange(await db.CareerAbout.AsNoTracking().Select(a => a.LogoUrl).ToListAsync());

            // LANDING — ommaviy (login'siz) sahifa: `GET /api/public/landing-data` faqat
            // `IsActive` yozuvlarni qaytaradi, ya'ni bu yerdagi filtr AYNAN o'sha filtr.
            // `Take(cap)` — nazoratsiz o'sgan jadval butunlay xotiraga yig'ilib qolmasin
            // (+1: chegaradan oshgani BILINSIN va logga tushsin).
            const int cap = UploadAccessRules.MaxPublicNames + 1;
            urls.AddRange(await db.LandingTeachers.AsNoTracking()
                .Where(t => t.IsActive && !string.IsNullOrEmpty(t.PhotoUrl))
                .Select(t => t.PhotoUrl).Take(cap).ToListAsync());
            urls.AddRange(await db.LandingCertificates.AsNoTracking()
                .Where(c => c.IsActive && !string.IsNullOrEmpty(c.ImageUrl))
                .Select(c => c.ImageUrl).Take(cap).ToListAsync());
            // FIKRLAR (testimonials) avatari — landing'dagi "Ota-onalar fikri" bo'limida
            // ko'rsatiladi. Busiz mehmon avatarlar o'rnida SINUQ rasm ko'rardi: CMS avatarni
            // xuddi shu `/uploads/<guid>.png` ga yuklaydi.
            urls.AddRange(await db.LandingTestimonials.AsNoTracking()
                .Where(t => t.IsActive && !string.IsNullOrEmpty(t.AvatarUrl))
                .Select(t => t.AvatarUrl).Take(cap).ToListAsync());

            _publicNames = UploadAccessRules.PublicNamesFrom(
                urls, UploadAccessRules.MaxPublicNames, out var skipped);
            // Cheklov JIMGINA qirqilmaydi — aks holda "nega bu sertifikat saytda ko'rinmayapti"
            // savoliga javob topib bo'lmasdi.
            if (skipped > 0)
                logger.LogWarning(
                    "Ochiq fayllar ro'yxati chegaraga yetdi ({Max} ta) — {Skipped} ta landing rasmi "
                    + "ro'yxatga KIRMADI va mehmonga ko'rinmaydi (faol yozuvlar sonini kamaytiring).",
                    UploadAccessRules.MaxPublicNames, skipped);

            _publicLoadedUtc = DateTime.UtcNow;
            return _publicNames;
        }
        catch (Exception ex)
        {
            // Baza vaqtincha yetib bo'lmasa — eski ro'yxat bilan davom etamiz (logotip yo'qolmasin).
            logger.LogWarning(ex, "Ochiq fayllar ro'yxatini yangilab bo'lmadi — eski ro'yxat ishlatiladi.");
            return _publicNames;
        }
        finally
        {
            _publicLock.Release();
        }
    }

    /// <summary>
    /// So'rovga RUXSAT bormi. <c>false</c> bo'lsa chaqiruvchi 404 qaytaradi (403 emas —
    /// faylning mavjudligini ham tasdiqlamaymiz).
    /// </summary>
    public async Task<bool> IsAllowedAsync(HttpContext ctx)
    {
        if (!Enabled) return true;
        // Manbalardan BITTASI haqiqiy bo'lsa yetadi — har biri bir xil qat'iy tekshiruvdan
        // o'tadi (imzo/muddat/issuer/audience + yuz tasdig'i scope'i), qayerdan kelgani farqsiz:
        // `up_at` va `at` ichida AYNAN bir xil formatdagi JWT yotadi.
        foreach (var token in TokensOf(ctx))
            if (IsTokenValid(token)) return true;
        return UploadAccessRules.IsPublicFile(ctx.Request.Path.Value, await PublicNamesAsync());
    }

    /// <summary>
    /// Avtorizatsiyalangan so'rovdan keyin <c>/uploads</c> uchun cookie qo'yadi (kerak bo'lsa).
    /// Alohida "login" qadami yo'q — SPA'ning har qanday API so'rovi cookie'ni tiklaydi.
    /// </summary>
    public void IssueCookie(HttpContext ctx)
    {
        if (!Enabled) return;
        // DUAL-MODE manba: mobil/Bearer rejimida token `Authorization` sarlavhasida, web SPA'da
        // esa HttpOnly `at` cookie'da (AuthCookies) — sarlavha umuman bo'lmasligi mumkin.
        // Faqat sarlavhaga qarash `up_at` cookie'sining web'da HECH QACHON qo'yilmasligiga olib
        // kelardi. `at` (Path=/) o'zi ham /uploads ga boradi, lekin `up_at` baribir qo'yiladi —
        // rasm ko'rsatish bitta manbaga qaram bo'lib qolmasin (ikkalasi ham qo'llab-quvvatlanadi,
        // hech biri olib tashlanmaydi).
        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : ctx.Request.Cookies[AuthCookies.AtCookie];
        if (string.IsNullOrEmpty(token)) return;
        // Allaqachon shu token turgan bo'lsa — qayta qo'yish shart emas.
        if (ctx.Request.Cookies[UploadAccessRules.CookieName] == token) return;
        if (!IsTokenValid(token)) return;
        _tokenCache.TryGetValue(token, out var expires);

        var https = ctx.Request.IsHttps;
        ctx.Response.Cookies.Append(UploadAccessRules.CookieName, token, new CookieOptions
        {
            HttpOnly = true,                 // JS o'qiy olmaydi (XSS bilan o'g'irlanmasin)
            Secure = https,
            // Telegram Mini App SPA'ni web.telegram.org ichida IFRAME'da ochadi — u yerdan
            // kelgan rasm so'rovlari "cross-site" hisoblanadi va Lax cookie YUBORILMAYDI.
            // Shuning uchun HTTPS'da None (u Secure'ni talab qiladi); dev (http) da Lax.
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/uploads",               // API so'rovlariga umuman yuborilmaydi
            Expires = expires == default ? null : new DateTimeOffset(expires, TimeSpan.Zero),
            IsEssential = true,
        });
    }

    /// <summary>Chiqishda (logout) cookie'ni o'chirish uchun.</summary>
    public static void ClearCookie(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(UploadAccessRules.CookieName, new CookieOptions { Path = "/uploads" });

    /// <summary>Rad etilgan so'rovni logga yozadi (kutilmagan mijoz shu yerdan ko'rinadi).</summary>
    public void LogDenied(HttpContext ctx) =>
        logger.LogWarning("/uploads rad etildi: {Path} (UA: {UserAgent})",
            ctx.Request.Path.Value, ctx.Request.Headers.UserAgent.ToString());
}
