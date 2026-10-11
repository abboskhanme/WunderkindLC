using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Infrastructure.Auth;

/// <summary>
/// REFRESH TOKEN oqimi — chiqarish, ROTATSIYA (grace + family revocation) va bekor qilish.
///
/// <para><b>Model:</b> xom (raw) token — 64 bayt kriptografik tasodif (base64url). Bazada faqat
/// uning SHA-256 hashi saqlanadi (<see cref="RefreshToken.TokenHash"/>); raw token FAQAT klientga
/// beriladi (cookie yoki JSON body). Ya'ni baza sizib chiqsa ham tokenlarni tiklab bo'lmaydi.</para>
///
/// <para><b>Rotatsiya:</b> har refresh'da eski token bekor qilinadi va yangisi AYNI family bilan
/// chiqadi (<see cref="RotateAsync"/>). Bu «token bir marta ishlatiladi» qoidasini beradi — o'g'irlangan
/// tokenni aniqlash imkonini yaratadi.</para>
///
/// <para><b>Reuse hujumi:</b> allaqachon BEKOR QILINGAN tokenni qayta ishlatishga urinish ikki xil
/// bo'ladi:
/// <list type="bullet">
/// <item><b>Grace</b> (<see cref="GraceSeconds"/> soniya ichida) — bir vaqtli so'rov / ko'p tab:
/// legitim, yangi juftlik beriladi, family bekor QILINMAYDI.</item>
/// <item><b>Grace'dan keyin</b> — o'g'irlik belgisi: butun <see cref="RefreshToken.FamilyId"/>
/// bekor qilinadi (haqiqiy egasi ham qayta login qiladi) va 401 qaytadi.</item>
/// </list></para>
/// </summary>
public class RefreshTokenService(
    AppDbContext db, JwtTokenService jwt, JwtOptions options, ILogger<RefreshTokenService> logger)
{
    /// <summary>Bekor qilingan tokenni «bir vaqtli so'rov» deb hisoblash oynasi (soniya).
    /// Undan keyingi qayta ishlatish — reuse hujumi.</summary>
    public const int GraceSeconds = 60;

    /// <summary>Rotatsiya/refresh natijasi. <see cref="Ok"/> false bo'lsa qolgan maydonlar bo'sh
    /// (chaqiruvchi 401 qaytaradi).</summary>
    public record RotateResult(
        bool Ok, string? AccessToken, string? RefreshToken, AppUser? User,
        DateTime RefreshExpiresUtc, string? Error);

    /// <summary>Chiqarilgan refresh token: xom qiymat (klientga) + saqlangan yozuv (muddat uchun).</summary>
    public record IssueResult(string RawToken, DateTime ExpiresUtc, Guid FamilyId);

    /// <summary>
    /// Yangi refresh token chiqaradi (login/otp/face muvaffaqiyatidan keyin). Xom token qaytadi,
    /// bazaga faqat hash yoziladi. <paramref name="familyId"/> berilsa mavjud naslga qo'shiladi
    /// (odatda null — yangi login yangi family boshlaydi).
    /// </summary>
    public async Task<IssueResult> IssueAsync(
        string userId, Guid? familyId = null, string? ip = null, string? ua = null,
        CancellationToken ct = default)
    {
        var raw = GenerateRaw();
        var now = DateTime.UtcNow;
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.RefreshDays),
            CreatedByIp = Truncate(ip, 64),
            UserAgent = Truncate(ua, 256),
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new IssueResult(raw, entity.ExpiresAt, entity.FamilyId);
    }

    /// <summary>
    /// Refresh oqimi: xom tokenni tekshiradi, rotatsiya qiladi va yangi (access, refresh) juftlik
    /// qaytaradi. Xato (topilmadi / eskirgan / grace'dan keyin reuse / foydalanuvchi bloklangan)
    /// holatlarda <see cref="RotateResult.Ok"/> = false (chaqiruvchi 401 qaytaradi).
    /// </summary>
    public async Task<RotateResult> RotateAsync(
        string rawToken, string? ip = null, string? ua = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return Fail("empty");

        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return Fail("not_found");

        var now = DateTime.UtcNow;

        // --- Bekor qilingan token bilan kelindi ---
        if (token.RevokedAt is not null)
        {
            // Grace: bir vaqtli so'rov / ko'p tab — eskisini legitim deb yangi juftlik beramiz.
            if (now - token.RevokedAt.Value <= TimeSpan.FromSeconds(GraceSeconds))
            {
                // ⚠️ Grace FAQAT rotatsiya poygasi uchun — nasl hali TIRIK bo'lsa. Sessiya ATAYIN
                // tugatilgan bo'lsa (logout, parol tiklash/almashtirish, qurilma bekor —
                // `RevokeAllForUserAsync`) naslda faol token qolmaydi: aks holda 60 soniyalik oyna
                // eski telefonga parol almashtirilgandan KEYIN ham yangi sessiya berib yuborardi.
                if (!await db.RefreshTokens.AnyAsync(
                        t => t.FamilyId == token.FamilyId && t.RevokedAt == null, ct))
                    return Fail("revoked");
                return await IssueRotatedAsync(token.FamilyId, token.UserId, ip, ua, now, ct);
            }

            // Grace'dan keyin — REUSE HUJUMI: butun naslni bekor qilamiz.
            await RevokeFamilyAsync(token.FamilyId, ct);
            logger.LogWarning(
                "Refresh token REUSE aniqlandi — family bekor qilindi: userId={UserId}, familyId={FamilyId}",
                token.UserId, token.FamilyId);
            return Fail("reuse");
        }

        // --- Muddati o'tgan ---
        if (token.ExpiresAt <= now) return Fail("expired");

        // --- Faol token: rotatsiya ---
        var newRaw = GenerateRaw();
        token.RevokedAt = now;
        token.ReplacedByHash = Hash(newRaw);
        return await IssueRotatedAsync(token.FamilyId, token.UserId, ip, ua, now, ct, newRaw);
    }

    /// <summary>Berilgan xom tokenni bekor qiladi (logout). Topilmasa/allaqachon bekor bo'lsa — jim.</summary>
    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null) return;
        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Butun naslni (family) bekor qiladi — reuse hujumi yoki «hamma qurilmadan chiqish».</summary>
    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = now;
        if (tokens.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// FOYDALANUVCHINING BARCHA SESSIYALARINI TUGATADI — uning hamma faol refresh tokenlari bekor
    /// qilinadi. Chaqiriladigan joylar: parol tiklash/o'rnatish (admin), o'z parolini almashtirish,
    /// ishonchli qurilmani bekor qilish.
    ///
    /// <para>⚠️ Nega SHART: refresh token 30 kun SIRPANUVCHI (har refresh'da yangilanadi). Busiz
    /// yo'qolgan telefon yoki parolini bilib olgan begona odam parol almashtirilgandan keyin ham
    /// cheksiz kirib yuraverardi — parol tiklash bezakka aylanardi.</para>
    ///
    /// <para>⚠️ Access token (JWT) bazada yo'q — u o'z muddati (<c>JwtOptions.AccessMinutes</c>,
    /// standart 60 daq) tugaguncha ishlaydi, keyin refresh rad etiladi va sessiya tugaydi.</para>
    ///
    /// <para><paramref name="keepFamilyId"/> — tirik qoladigan sessiya (foydalanuvchi O'Z parolini
    /// almashtirgan qurilma; qarang <see cref="CurrentFamilyAsync"/>). <c>null</c> — hammasi.</para>
    ///
    /// <para><c>SaveChanges</c> QILMAYDI — parol o'zgarishi bilan BITTA tranzaksiyada saqlansin
    /// (biri yozilib, ikkinchisi yozilmay qolmasin). Bekor qilingan tokenlar sonini qaytaradi.</para>
    /// </summary>
    public static async Task<int> RevokeAllForUserAsync(
        AppDbContext db, string userId, Guid? keepFamilyId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        var now = DateTime.UtcNow;
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        var n = 0;
        foreach (var t in tokens)
        {
            if (keepFamilyId is { } keep && t.FamilyId == keep) continue;
            t.RevokedAt = now;
            n++;
        }

        // AI ulanishlar (read-only MCP) ham — HAR DOIM, joriy sessiya saqlansa ham: ular boshqa
        // "qurilma" (AI ilovasi), parol almashtirilgandan keyin eski ulanish ishlayvermasin.
        var grants = await db.McpGrants
            .Where(g => g.UserId == userId && g.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var g in grants)
        {
            g.RevokedAt = now;
            g.RevokedBy = "system:password-change";
        }
        return n;
    }

    /// <summary>
    /// Shu so'rov qaysi SESSIYAdan (refresh token naslidan) kelgan: avval web'ning <c>rt</c>
    /// cookie'si (xom token → hash → nasl), bo'lmasa access tokendagi
    /// <see cref="JwtTokenService.SessionClaimType"/> claim'i (mobil ilova). Token boshqa
    /// foydalanuvchiniki bo'lsa yoki topilmasa — <c>null</c> (ya'ni hech qaysi sessiya saqlanmaydi).
    /// </summary>
    public static async Task<Guid?> CurrentFamilyAsync(
        AppDbContext db, string userId, string? rawRefreshToken, string? sessionClaim,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var hash = Hash(rawRefreshToken);
            var family = await db.RefreshTokens.AsNoTracking()
                .Where(t => t.TokenHash == hash && t.UserId == userId)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(ct);
            if (family is not null) return family;
        }
        return Guid.TryParse(sessionClaim, out var g) ? g : null;
    }

    // ---- Ichki yordamchilar ----

    /// <summary>Yangi access + refresh juftligini yaratadi (rotatsiya va grace ikkalasida bir xil yo'l).
    /// <paramref name="reuseRaw"/> berilsa (rotatsiyada oldindan hisoblangan) o'sha ishlatiladi, aks
    /// holda (grace) yangi raw yaratiladi.</summary>
    private async Task<RotateResult> IssueRotatedAsync(
        Guid familyId, string userId, string? ip, string? ua, DateTime now,
        CancellationToken ct, string? reuseRaw = null)
    {
        // Foydalanuvchi hali yaroqlimi? (arxivlangan/bloklangan bo'lsa refresh ham rad etiladi —
        // OnTokenValidated dagi revocation bilan bir xil mantiq.)
        var user = await ValidUserAsync(userId, ct);
        if (user is null)
        {
            await db.SaveChangesAsync(ct); // faol token allaqachon RevokedAt bilan belgilangan bo'lishi mumkin
            return Fail("user_invalid");
        }

        var raw = reuseRaw ?? GenerateRaw();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.RefreshDays),
            CreatedByIp = Truncate(ip, 64),
            UserAgent = Truncate(ua, 256),
        };
        db.RefreshTokens.Add(entity);
        // Sessiya nasli access tokenga ham yoziladi — o'z parolini almashtirganda AYNAN shu
        // sessiya saqlanib, qolganlari bekor qilinadi (`RevokeAllForUserAsync`).
        var access = jwt.CreateToken(user, familyId);
        // "Oxirgi kirish" — refresh ham FAOL foydalanish: ilova bir marta login qilib, keyin
        // 30 kun refresh bilan ishlaydi, ya'ni faqat login'da yozilsa "oxirgi kirish" bir oy
        // eskirib turardi. Bu so'rov baribir saqlanadi — qo'shimcha yozuv yo'q.
        StampLastSeen(user, AppClock.Now);
        await db.SaveChangesAsync(ct);
        return new RotateResult(true, access, raw, user, entity.ExpiresAt, null);
    }

    /// <summary>Foydalanuvchi hali kira oladimi (arxiv/blok tekshiruvi) — <c>Program.OnTokenValidated</c>
    /// bilan bir xil. Bloklangan bo'lsa null qaytaradi.</summary>
    private async Task<AppUser?> ValidUserAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        bool blocked = user.Role switch
        {
            Roles.Teacher => !await db.Teachers.AnyAsync(t => t.UserId == userId && !t.IsArchived && !t.IsBlocked, ct),
            Roles.Student => !await db.Students.AnyAsync(s => s.UserId == userId && !s.IsArchived && !s.LoginBlocked, ct),
            _ => false, // admin/superadmin/staff/parent — mavjudligi yetarli
        };
        return blocked ? null : user;
    }

    /// <summary>"Oxirgi ko'rilgan" daqiqasi qanchalik eskirganda yangilanadi. Kichik oynalarda
    /// (bir nechta tab bir vaqtda refresh qilganda) har so'rovda qayta yozilmasin.</summary>
    public const int LastSeenStampMinutes = 5;

    /// <summary>
    /// <see cref="AppUser.LastLoginAt"/> ni yangilaydi — faqat bo'sh yoki
    /// <see cref="LastSeenStampMinutes"/> daqiqadan eski bo'lsa (sof, testlanadi). SaveChanges QILMAYDI.
    /// Format login'dagi bilan bir xil (<c>yyyy-MM-ddTHH:mm:ss</c>, markaz vaqti).
    /// </summary>
    public static bool StampLastSeen(AppUser user, DateTime now)
    {
        if (DateTime.TryParse(user.LastLoginAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var last)
            && (now - last).TotalMinutes < LastSeenStampMinutes && last <= now)
            return false;
        user.LastLoginAt = now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static RotateResult Fail(string error) =>
        new(false, null, null, null, default, error);

    /// <summary>64 bayt kriptografik tasodif → base64url (URL/cookie/JSON'da xavfsiz belgilar).</summary>
    private static string GenerateRaw() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>SHA-256 hex (LoginOtpService.CodeHash bilan bir xil naqsh).</summary>
    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
