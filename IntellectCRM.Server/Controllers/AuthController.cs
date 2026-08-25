using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Auth;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;

namespace IntellectCRM.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db, JwtTokenService jwt, ILogger<AuthController> logger,
    FaceLoginService face, RefreshTokenService refreshTokens) : ControllerBase
{
    /// <summary>Akkaunt bloklanishidan oldingi ketma-ket noto'g'ri urinishlar chegarasi.</summary>
    private const int MaxFailedAttempts = 5;
    /// <summary>Chegaraga yetganda akkaunt necha daqiqaga bloklanadi.</summary>
    private const int LockoutMinutes = 3;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);

        // Akkaunt vaqtincha bloklangan bo'lsa — parol to'g'ri bo'lsa ham kiritmaymiz (brute-force himoyasi).
        if (user is not null && IsLockedOut(user, out var remaining))
        {
            logger.LogWarning("Bloklangan akkauntga login urinishi: login={Login}, IP={IP}", req.Email, ip);
            return Unauthorized(new { message = $"Akkaunt vaqtincha bloklangan. {remaining} soniyadan so'ng qayta urinib ko'ring." });
        }

        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        {
            // Noto'g'ri parol — hisoblagichni oshiramiz; chegaraga yetsa akkauntni bloklaymiz.
            if (user is not null)
            {
                user.FailedLoginCount++;
                string? lockMessage = null;
                if (user.FailedLoginCount >= MaxFailedAttempts)
                {
                    user.LockoutUntil = AppClock.Now.AddMinutes(LockoutMinutes).ToString("yyyy-MM-ddTHH:mm:ss");
                    user.FailedLoginCount = 0; // blokdan keyin yangi hisob boshlanadi
                    lockMessage = $"{MaxFailedAttempts} marta noto'g'ri urinish — akkaunt {LockoutMinutes} daqiqaga bloklandi.";
                }
                await db.SaveChangesAsync();
                logger.LogWarning("Muvaffaqiyatsiz login urinishi: login={Login}, IP={IP}, urinishlar={Count}",
                    req.Email, ip, user.FailedLoginCount);
                if (lockMessage is not null) return Unauthorized(new { message = lockMessage });
            }
            else
            {
                // Noma'lum login — akkaunt yo'q, hisoblab bo'lmaydi (faqat kuzatuv).
                logger.LogWarning("Muvaffaqiyatsiz login urinishi (akkaunt yo'q): login={Login}, IP={IP}", req.Email, ip);
            }
            return Unauthorized(new { message = "Login yoki parol noto'g'ri" });
        }

        // Arxivlangan o'qituvchi/o'quvchi qayta kira olmasin (token revocation bilan bir xil mantiq).
        if (await IsBlockedAsync(user))
            return Unauthorized(new { message = "Akkaunt arxivlangan yoki to'xtatilgan" });

        // Admin o'quvchi login'ini vaqtincha cheklagan bo'lsa — farqli xabar bilan rad etamiz.
        if (user.Role == Roles.Student && await db.Students.AnyAsync(s => s.UserId == user.Id && s.LoginBlocked))
            return Unauthorized(new { message = "Sizning hisobingiz hali aktiv emas. Administratorga murojaat qiling." });

        // O'qituvchi "vaqtincha aktiv emas" qilingan bo'lsa — arxivdan FARQLI xabar (paroli joyida,
        // admin qaytarsa o'sha parol bilan kiraveradi).
        if (user.Role == Roles.Teacher && await db.Teachers.AnyAsync(t => t.UserId == user.Id && t.IsBlocked))
            return Unauthorized(new { message = TeacherBlockedMessage });

        // Muvaffaqiyatli login — noto'g'ri urinish hisoblagichi va (agar bo'lsa) blokni tozalaymiz.
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;

        // Login kuzatuvi: birinchi marta kirayotgan bo'lsa FirstLoginAt ham, har safar LastLoginAt yoziladi.
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        // Foydalanuvchi parolni ishlatdi — dastlabki ochiq parol endi superadmin'ga ko'rsatilmaydi.
        user.InitialPassword = null;

        // YUZ BILAN KIRISH (faqat O'QUVCHI roli). Qurilma ishonchli emas va sozlama yoqilgan bo'lsa —
        // TO'LIQ token o'rniga CHEKLANGAN (scope=face) token beriladi: u bilan faqat selfi yuborish
        // mumkin (`FaceScopeGate`). Ishonchli qurilmada esa hech narsa o'zgarmaydi.
        // ⚠️ Qaror `FaceLoginService` da — o'qituvchi/admin/ota-onaga TEGMAYDI va `deviceId`
        // yubormaydigan ESKI klientlar uchun ham xatti-harakat o'zgarmaydi.
        var faceDecision = await face.DecideAsync(user, req.DeviceId, HttpContext.RequestAborted);
        if (faceDecision.Required)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Yuz tasdig'i talab qilindi: userId={UserId}, IP={IP}", user.Id, ip);
            var faceToken = jwt.CreateFaceScopedToken(user, FaceScopeGate.TokenMinutes);
            return new LoginResponse(
                faceToken,
                new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)),
                FaceRequired: true, FaceStatus: faceDecision.Status);
        }

        // Ishonchli qurilmadan kirildi — "oxirgi ko'rilgan" yangilanadi (admin ro'yxatida
        // qaysi telefon hali ishlatilayotgani ko'rinsin).
        await face.TouchAsync(user.Id, req.DeviceId, HttpContext.RequestAborted);
        await db.SaveChangesAsync();

        var token = jwt.CreateToken(user);
        // DUAL-MODE: token + refresh JSON body'da qaytadi (mobil), QO'SHIMCHA `at`/`csrf`/`rt` cookie
        // ham qo'yiladi (web HttpOnly cookie rejimi). Orqaga moslik buzilmaydi.
        var rt = await refreshTokens.IssueAsync(user.Id, ip: ip, ua: UserAgent(), ct: HttpContext.RequestAborted);
        IssueAuthCookies(token, rt.RawToken, rt.ExpiresUtc);
        return new LoginResponse(token, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)),
            RefreshToken: rt.RawToken);
    }

    /// <summary>Bot orqali olingan bir martalik kod bilan login (parol o'rniga) — xuddi shu JWT/UserDto
    /// javobi, LastLoginAt/FirstLoginAt kuzatuvi va arxiv/blok tekshiruvi bilan (parol yo'li bilan bir xil).
    /// Kod xato/eskirgan/ishlatilgan bo'lsa — qaysi sabab ekanini OSHKOR QILMAYMIZ (bir xil 401 xabar),
    /// aks holda tashqi hujumchiga kod hovuzi haqida signal berardi.</summary>
    [HttpPost("otp-login")]
    [AllowAnonymous]
    [EnableRateLimiting("otp-verify")]
    public async Task<ActionResult<LoginResponse>> OtpLogin(OtpLoginRequest req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var userId = await LoginOtpService.VerifyAndConsumeAsync(db, req.Code ?? "", HttpContext.RequestAborted);
        if (userId is null)
        {
            logger.LogWarning("Muvaffaqiyatsiz OTP-login urinishi: IP={IP}", ip);
            return Unauthorized(new { message = "Kod noto'g'ri yoki muddati o'tgan" });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Unauthorized(new { message = "Kod noto'g'ri yoki muddati o'tgan" });

        if (await IsBlockedAsync(user))
            return Unauthorized(new { message = "Akkaunt arxivlangan yoki to'xtatilgan" });
        if (user.Role == Roles.Student && await db.Students.AnyAsync(s => s.UserId == user.Id && s.LoginBlocked))
            return Unauthorized(new { message = "Sizning hisobingiz hali aktiv emas. Administratorga murojaat qiling." });
        if (user.Role == Roles.Teacher && await db.Teachers.AnyAsync(t => t.UserId == user.Id && t.IsBlocked))
            return Unauthorized(new { message = TeacherBlockedMessage });

        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;

        // YUZ BILAN KIRISH — parol yo'li bilan AYNAN bir xil darvoza (`Login` dagi izohga qarang).
        // ⚠️ Bu SHART: OTP ham to'liq huquqli login yo'li. Darvoza faqat parolda tursa, modul
        // yoqilgan bo'lsa ham "botdan kod olib kirish" selfini butunlay chetlab o'tardi — ya'ni
        // login/parolni birovga bergan o'quvchi o'sha odamga bot kodini yuborib qo'ya olardi va
        // yuz tekshiruvi bezakka aylanardi. Ilova yangilanmagan bo'lsa (deviceId yo'q) xatti-
        // harakat ilgarigidek qoladi.
        var faceDecision = await face.DecideAsync(user, req.DeviceId, HttpContext.RequestAborted);
        if (faceDecision.Required)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Yuz tasdig'i talab qilindi (OTP): userId={UserId}, IP={IP}", user.Id, ip);
            return new LoginResponse(
                jwt.CreateFaceScopedToken(user, FaceScopeGate.TokenMinutes),
                new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)),
                FaceRequired: true, FaceStatus: faceDecision.Status);
        }

        await face.TouchAsync(user.Id, req.DeviceId, HttpContext.RequestAborted);
        await db.SaveChangesAsync();

        logger.LogInformation("OTP orqali login: userId={UserId}, IP={IP}", user.Id, ip);
        var token = jwt.CreateToken(user);
        // DUAL-MODE: `Login` bilan bir xil — body'da token + refresh, `at`/`csrf`/`rt` cookie.
        var rt = await refreshTokens.IssueAsync(user.Id, ip: ip, ua: UserAgent(), ct: HttpContext.RequestAborted);
        IssueAuthCookies(token, rt.RawToken, rt.ExpiresUtc);
        return new LoginResponse(token, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)),
            RefreshToken: rt.RawToken);
    }

    /// <summary>
    /// ACCESS TOKENni yangilash (refresh oqimi). DUAL:
    /// <list type="bullet">
    /// <item><b>Web:</b> <c>rt</c> HttpOnly cookie'dan o'qiladi (body kerak emas). Muvaffaqiyatda
    /// yangi <c>at</c>+<c>csrf</c>+<c>rt</c> cookie o'rnatiladi.</item>
    /// <item><b>Mobil:</b> <c>rt</c> cookie yo'q → body'dagi <c>refreshToken</c> ishlatiladi.</item>
    /// </list>
    /// Har ikkalasida javob <c>{ token, refreshToken, user }</c>. Xato (topilmadi/eskirgan/bekor/
    /// reuse) → 401. Rotatsiya + grace + family revocation — <see cref="RefreshTokenService"/>.
    /// CSRF'dan ISTISNO (login kabi).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest? req = null)
    {
        // Web — cookie ustun; mobil — body.
        var raw = Request.Cookies[IntellectCRM.Server.AuthCookies.RtCookie];
        if (string.IsNullOrEmpty(raw)) raw = req?.RefreshToken;
        if (string.IsNullOrEmpty(raw)) return Unauthorized(new { message = "Refresh token topilmadi" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var result = await refreshTokens.RotateAsync(raw, ip, UserAgent(), HttpContext.RequestAborted);
        if (!result.Ok || result.User is null || result.AccessToken is null || result.RefreshToken is null)
        {
            logger.LogWarning("Refresh rad etildi: sabab={Reason}, IP={IP}", result.Error, ip);
            return Unauthorized(new { message = "Sessiya muddati tugagan. Qayta kiring." });
        }

        // Yangi juftlik: at (60 daq) + csrf + rt (30 kun) cookie va body (mobil).
        IssueAuthCookies(result.AccessToken, result.RefreshToken, result.RefreshExpiresUtc);
        var user = result.User;
        return new LoginResponse(result.AccessToken, new UserDto(
            user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user)),
            RefreshToken: result.RefreshToken);
    }

    /// <summary>Web cookie rejimi uchun `at` (HttpOnly, 60 daq), `csrf` (30 kun) va `rt` (refresh,
    /// 30 kun) cookie'larini qo'yadi. `at` muddati token'ning o'z `exp` qiymatidan olinadi.</summary>
    private void IssueAuthCookies(string token, string refreshToken, DateTime refreshExpiresUtc)
    {
        var expiresUtc = new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo; // UTC
        IntellectCRM.Server.AuthCookies.Issue(HttpContext, token, expiresUtc, refreshToken, refreshExpiresUtc);
    }

    /// <summary>Kuzatuv uchun so'rov User-Agent'i (refresh token yozuviga).</summary>
    private string? UserAgent()
    {
        var ua = Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : ua;
    }

    /// <summary>
    /// Akkaunt hozir vaqtincha bloklanganmi (ketma-ket noto'g'ri urinishlar tufayli)?
    /// <paramref name="remainingSeconds"/> — blok tugashiga qolgan soniyalar.
    /// </summary>
    private static bool IsLockedOut(AppUser user, out int remainingSeconds)
    {
        remainingSeconds = 0;
        if (string.IsNullOrEmpty(user.LockoutUntil)) return false;
        if (!DateTime.TryParse(user.LockoutUntil, out var until)) return false;
        var now = AppClock.Now;
        if (now >= until) return false;
        remainingSeconds = (int)Math.Ceiling((until - now).TotalSeconds);
        return true;
    }

    /// <summary>"Vaqtincha aktiv emas" qilingan o'qituvchiga ko'rsatiladigan xabar (arxivdan farqli:
    /// paroli saqlanadi, admin qaytarishi bilan o'sha parol bilan kiraveradi).</summary>
    internal const string TeacherBlockedMessage =
        "Hisobingiz vaqtincha faol emas. Administratorga murojaat qiling.";

    /// <summary>Akkaunt arxivlangan (o'qituvchi/o'quvchi) bo'lsa true — login va token rad etiladi.</summary>
    private async Task<bool> IsBlockedAsync(AppUser user) => user.Role switch
    {
        Roles.Teacher => !await db.Teachers.AnyAsync(t => t.UserId == user.Id && !t.IsArchived),
        Roles.Student => !await db.Students.AnyAsync(s => s.UserId == user.Id && !s.IsArchived),
        _ => false,
    };

    /// <summary>
    /// Ruxsat etilgan bo'limlar: o'qituvchi → Teacher.Permissions; xodim → AppUser.Permissions
    /// (admin bo'limlari); admin/superadmin/o'quvchi → null (cheklov yo'q / kerak emas).
    /// </summary>
    private async Task<List<string>?> PermsFor(AppUser user) => user.Role switch
    {
        Roles.Teacher => (await db.Teachers.FirstOrDefaultAsync(t => t.UserId == user.Id))?.Permissions,
        Roles.Staff => user.Permissions,
        _ => null,
    };

    /// <summary>Chiqish (logout): `up_at` cookie'sini o'chiradi — umumiy kompyuterda keyingi odam
    /// login'siz `/uploads` maxfiy hujjatlarini ocholmasin. JWT'ning o'zi klientda (localStorage'da)
    /// tozalanadi; bu yerda faqat brauzer cookie'si serverdan o'chiriladi.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? req = null)
    {
        // Joriy refresh token'ni DB'da bekor qilamiz (rt cookie yoki mobil body'dan) — logout'dan
        // keyin o'sha token bilan qayta refresh qilib bo'lmasin.
        var raw = Request.Cookies[IntellectCRM.Server.AuthCookies.RtCookie];
        if (string.IsNullOrEmpty(raw)) raw = req?.RefreshToken;
        if (!string.IsNullOrEmpty(raw))
            await refreshTokens.RevokeAsync(raw, HttpContext.RequestAborted);

        IntellectCRM.Server.UploadsGuard.ClearCookie(HttpContext);
        // DUAL-MODE cookie'lari ham o'chiriladi (umumiy kompyuterda keyingi odam kirmasin).
        Response.Cookies.Delete(IntellectCRM.Server.AuthCookies.AtCookie, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(IntellectCRM.Server.AuthCookies.CsrfCookie, new CookieOptions { Path = "/" });
        IntellectCRM.Server.AuthCookies.ClearRefresh(HttpContext);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user), user.Phone);
    }

    /// <summary>Joriy foydalanuvchi o'z login (email) va/yoki parolini o'zgartiradi.
    /// Joriy parol bilan tasdiqlanadi. Email/Id o'zgarmagani uchun token amal qilaveradi.</summary>
    [HttpPut("account")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateAccount(UpdateAccountRequest req)
    {
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var user = await db.Users.FindAsync(id);
        if (user is null) return Unauthorized();

        if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Joriy parol noto'g'ri" });

        var newEmail = req.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(newEmail) && newEmail != user.Email)
        {
            var taken = await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id);
            if (taken) return BadRequest(new { message = "Bu login allaqachon band" });
            user.Email = newEmail;
        }

        if (!string.IsNullOrEmpty(req.NewPassword))
        {
            if (req.NewPassword.Length < 8)
                return BadRequest(new { message = "Yangi parol kamida 8 belgidan iborat bo'lsin" });
            user.SetOwnPassword(req.NewPassword);
        }

        // Telefon — berilsa yangilaymiz (admin/xodim botda yangi lid xabarnomasini olishi uchun).
        if (req.Phone is not null) user.Phone = PhoneUtil.Normalize(req.Phone);

        await db.SaveChangesAsync();
        return new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl, await PermsFor(user), user.Phone);
    }
}
