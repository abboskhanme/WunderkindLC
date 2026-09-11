using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace WunderkindLC.Server;

/// <summary>
/// DUAL-MODE auth uchun cookie yordamchilari.
///
/// <para>Web SPA endi JWT'ni <c>Authorization: Bearer</c> sarlavhasi O'RNIGA (yoki bilan birga)
/// <b>HttpOnly cookie</b> orqali yubora oladi — XSS bilan token o'g'irlanishini kamaytiradi.
/// Mobil ilovalar esa ilgarigidek Bearer'da ishlaydi (orqaga moslik).</para>
///
/// <para>Naqsh <see cref="UploadsGuard.IssueCookie"/> bilan bir xil, farqi: bu yerdagi
/// <c>at</c> cookie <c>Path=/</c> (butun API uchun), <c>UploadsGuard</c> niki esa
/// <c>Path=/uploads</c> (faqat rasmlar uchun).</para>
///
/// <para><b>CSRF:</b> cookie brauzer tomonidan HAR so'rovga avtomatik qo'shilgani uchun
/// cross-site forge xavfi paydo bo'ladi. Himoya — <b>double-submit</b>: <c>csrf</c> cookie
/// (JS o'qiy oladi) + har UNSAFE so'rovda o'sha qiymat <c>X-CSRF-Token</c> sarlavhasida
/// takrorlanadi (<see cref="CsrfMiddleware"/>). Bearer bilan kelgan so'rovga bu talab
/// qo'yilmaydi — sarlavhani boshqa sayt qo'sha olmaydi.</para>
/// </summary>
public static class AuthCookies
{
    /// <summary>Auth token cookie nomi (butun API — <c>Path=/</c>).</summary>
    public const string AtCookie = "at";

    /// <summary>CSRF token cookie nomi (JS o'qiy oladi — double-submit uchun).</summary>
    public const string CsrfCookie = "csrf";

    /// <summary>Double-submit uchun so'rov sarlavhasi.</summary>
    public const string CsrfHeader = "X-CSRF-Token";

    /// <summary>Refresh token cookie nomi. <c>Path=/api/auth</c> — faqat refresh/logout endpointlariga
    /// yuboriladi (butun API bo'ylab sizib yurmasin), <c>HttpOnly</c> (JS o'qiy olmaydi).</summary>
    public const string RtCookie = "rt";

    /// <summary>Refresh cookie faqat shu yo'l ostiga yuboriladi.</summary>
    public const string RtPath = "/api/auth";

    /// <summary>
    /// Login muvaffaqiyatli bo'lganda: <c>at</c> (auth), <c>csrf</c> va (berilsa) <c>rt</c> (refresh)
    /// cookie'larini qo'yadi. Tokenlar JSON body'da ham qaytishda davom etadi (mobil uchun) — bu QO'SHIMCHA.
    ///
    /// <para><paramref name="refreshToken"/> berilsa: <c>rt</c> cookie qo'yiladi va <c>csrf</c>
    /// REFRESH muddati (<paramref name="refreshExpiresUtc"/>) bilan yashaydi — SPA access tokenni
    /// refresh qilganda ham double-submit uchun csrf saqlanib qolsin (access 60 daqiqada eskiradi,
    /// csrf esa refresh bilan birga 30 kun).</para>
    /// </summary>
    public static void Issue(
        HttpContext ctx, string token, DateTime expiresUtc,
        string? refreshToken = null, DateTime? refreshExpiresUtc = null)
    {
        var https = ctx.Request.IsHttps;
        var expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero);

        // Auth cookie — HttpOnly (JS o'qiy olmaydi, XSS bilan o'g'irlanmasin).
        ctx.Response.Cookies.Append(AtCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = https,
            // Telegram Mini App SPA'ni web.telegram.org IFRAME'ida ochadi — u yerdan kelgan
            // so'rovlar "cross-site" hisoblanadi va Lax cookie YUBORILMAYDI. HTTPS'da None
            // (Secure'ni talab qiladi); dev (http) da Lax (Vite dev-server buzilmasin).
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        });

        // csrf — refresh bo'lsa u bilan birga uzoq yashaydi (aks holda access muddati bilan).
        IssueCsrf(ctx, refreshExpiresUtc ?? expiresUtc);

        if (!string.IsNullOrEmpty(refreshToken))
            IssueRefresh(ctx, refreshToken, refreshExpiresUtc ?? expiresUtc);
    }

    /// <summary>FAQAT <c>rt</c> (refresh) cookie'ni qo'yadi — <c>Path=/api/auth</c>, HttpOnly.</summary>
    public static void IssueRefresh(HttpContext ctx, string refreshToken, DateTime expiresUtc)
    {
        var https = ctx.Request.IsHttps;
        ctx.Response.Cookies.Append(RtCookie, refreshToken, new CookieOptions
        {
            HttpOnly = true,                // JS o'qiy olmaydi (XSS bilan o'g'irlanmasin)
            Secure = https,
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = RtPath,                  // faqat /api/auth/* ga yuboriladi
            Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
            IsEssential = true,
        });
    }

    /// <summary><c>rt</c> cookie'ni o'chiradi (logout).</summary>
    public static void ClearRefresh(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(RtCookie, new CookieOptions { Path = RtPath });

    /// <summary>
    /// FAQAT <c>csrf</c> cookie'ni qo'yadi (yangi tasodifiy qiymat bilan). Login'da
    /// <see cref="Issue"/> chaqiradi; mavjud sessiyalar uchun esa <see cref="CsrfMiddleware"/>
    /// cookie yo'q bo'lsa shu yerdan tiklaydi.
    /// </summary>
    public static void IssueCsrf(HttpContext ctx, DateTime expiresUtc)
    {
        var https = ctx.Request.IsHttps;
        // 32 bayt kriptografik tasodif → base64url (cookie/sarlavhada xavfsiz belgilar).
        var csrf = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        ctx.Response.Cookies.Append(CsrfCookie, csrf, new CookieOptions
        {
            HttpOnly = false,               // JS O'QISHI kerak (double-submit)
            Secure = https,
            SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
            IsEssential = true,
        });
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>
/// CSRF himoyasi (double-submit cookie) — FAQAT cookie bilan autentifikatsiya qilingan
/// UNSAFE so'rovlar uchun.
///
/// <para><b>Qoida:</b> so'rov UNSAFE (POST/PUT/PATCH/DELETE) VA <c>at</c> cookie bor VA
/// <c>Authorization</c> sarlavhasi YO'Q bo'lsa — <c>X-CSRF-Token</c> sarlavhasi <c>csrf</c>
/// cookie qiymatiga TENG bo'lishi shart. Aks holda <c>403</c>.</para>
///
/// <para><b>ISTISNO (tekshirilMAYDI):</b>
/// (a) <c>Authorization</c> sarlavhasi bor — mobil Bearer, cross-site forge qilib bo'lmaydi;
/// (b) SAFE metodlar (GET/HEAD/OPTIONS);
/// (c) <see cref="ExemptPrefixes"/> yo'llari — login (hali auth yo'q), tashqi webhook'lar
/// (Meta/telefoniya/SMS — ular bizning cookie'mizni umuman yubormaydi) va SignalR (<c>/hubs</c>).</para>
///
/// <para><b>Orqaga moslik:</b> hozirgi web va mobil Bearer ishlatadi → <c>Authorization</c>
/// sarlavhasi bor → istisno → hech narsa buzilmaydi. CSRF faqat web cookie rejimiga
/// o'tgandan keyin kuchga kiradi.</para>
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next)
{
    /// <summary>CSRF tekshiruvidan ozod yo'llar (prefiks bo'yicha, oson kengaytiriladi).</summary>
    private static readonly string[] ExemptPrefixes =
    {
        "/api/auth/login",                  // hali autentifikatsiya yo'q
        "/api/auth/otp-login",              // hali autentifikatsiya yo'q
        "/api/auth/refresh",                // access token eskirgan — csrf cookie ham eskirgan bo'lishi mumkin
                                            // (rt cookie o'zi cross-site forge'dan himoyalangan: Path cheklovi + HttpOnly)
        "/api/public/instagram/webhook",    // Meta webhook (tashqi, cookie yubormaydi)
        "/api/public/instagram/leadgen",    // Meta lead webhook
        "/api/telephony/",                  // telefoniya webhook (moizvonki/{secret})
        "/api/sms/callback",                // SMS provayder callback
        "/hubs",                            // SignalR (WebSocket handshake)
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;

        if (IsUnsafe(req.Method))
        {
            var hasBearer = req.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            var atCookie = req.Cookies[AuthCookies.AtCookie];

            // Faqat COOKIE bilan autentifikatsiya qilingan so'rov (Bearer YO'Q, at cookie BOR)
            // va istisno yo'lida bo'lmasa — double-submit tekshiriladi.
            if (!hasBearer && !string.IsNullOrEmpty(atCookie) && !IsExempt(req.Path))
            {
                var headerToken = req.Headers[AuthCookies.CsrfHeader].ToString();
                var cookieToken = req.Cookies[AuthCookies.CsrfCookie];

                if (!TokensMatch(headerToken, cookieToken))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { message = "CSRF token invalid" });
                    return;
                }
            }
        }

        // CSRF cookie KAFOLATI: autentifikatsiyalangan (masalan /auth/me orqali cookie bilan
        // kirgan mavjud sessiya) VA csrf cookie yo'q bo'lsa — yangisini qo'yamiz. Aks holda
        // klient double-submit uchun token topa olmasdi. (`up_at` reissue naqshiga o'xshaydi.)
        if (ctx.User.Identity?.IsAuthenticated == true
            && string.IsNullOrEmpty(req.Cookies[AuthCookies.CsrfCookie]))
            AuthCookies.IssueCsrf(ctx, DateTime.UtcNow.AddHours(12));

        await next(ctx);
    }

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool IsExempt(PathString path)
    {
        foreach (var prefix in ExemptPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Sarlavha va cookie tokenlari mos keladimi (bo'sh bo'lmasin, doimiy vaqtli solishtiruv).</summary>
    private static bool TokensMatch(string? header, string? cookie)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(cookie)) return false;
        var a = Encoding.UTF8.GetBytes(header);
        var b = Encoding.UTF8.GetBytes(cookie);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
