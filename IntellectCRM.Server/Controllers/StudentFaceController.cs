using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Auth;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// YUZ BILAN KIRISH — o'quvchi tomoni (`/api/student/face/*`).
///
/// <para>⚠️ Bu uchta endpoint CHEKLANGAN token (<c>scope=face</c>) bilan ham ishlaydi — aynan
/// ular yuz tasdiqlanmagan sessiyaga ochiq bo'lgan YAGONA yo'llar (<see cref="FaceScopeGate"/>).
/// Qolgan hamma narsa 401 oladi, ya'ni tasdiqlanmagan foydalanuvchi jurnal/baho/chatga
/// yeta olmaydi.</para>
///
/// <para><b>Model bu yerda ishlamaydi:</b> ilova telefonda vektor hisoblab yuboradi, server esa
/// kosinus bilan solishtiradi (1 GB serverda ONNX/detektor sig'maydi — `FACE-DETEKT-PLAN.md` §2).</para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Student)]
[Route("api/student/face")]
public class StudentFaceController(
    AppDbContext db, FaceLoginService face, JwtTokenService jwt, AppAttestation attest,
    IWebHostEnvironment env, ILogger<StudentFaceController> logger) : ControllerBase
{
    /// <summary>Selfi uchun eng katta fayl hajmi. Kichik ATAYIN: rasm faqat "dalil" sifatida
    /// saqlanadi, qaror baribir VEKTOR bo'yicha chiqadi (va biometrik fayllar disk/zaxirani
    /// shishirmasin).</summary>
    private const long MaxImageBytes = 2_000_000;

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private async Task<Student?> MeAsync(CancellationToken ct) =>
        UserId is null ? null : await db.Students.FirstOrDefaultAsync(s => s.UserId == UserId, ct);

    /* =============================================================================================
     *  HOLAT — ilova selfi ekranini shundan quradi
     * ========================================================================================== */

    /// <summary>Yuz tekshiruvi sozlamalari va joriy holat (etalon bormi, profil rasmi bormi).</summary>
    [HttpGet("status")]
    public async Task<ActionResult<FaceStatusDto>> Status(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        if (me is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var settings = await face.SettingsAsync(ct);
        // ⚠️ "Qatori bormi" YETARLI EMAS — etalonni YARATGAN model markaz kutayotgani bilan bir xil
        // bo'lishi shart (`FaceLoginService.TemplateUsable` — sabab o'sha yerda yozilgan). Ilova
        // AYNAN shu bayroqqa qarab profil rasmidan `refVector` yuborish/yubormaslikni hal qiladi.
        var templateModel = await db.StudentFaceProfiles.AsNoTracking()
            .Where(p => p.StudentId == me.Id)
            .Select(p => p.ModelVersion)
            .FirstOrDefaultAsync(ct);
        var enrolled = templateModel is not null
            && FaceLoginService.TemplateUsable(templateModel, settings.ModelVersion);
        var used = await face.RecentAttemptsAsync(me.Id, ct);
        var limits = FaceMatch.DefaultLimits;

        return new FaceStatusDto(
            settings.Enabled, enrolled,
            HasPhoto: !string.IsNullOrWhiteSpace(me.BirthCertificateUrl),
            settings.ModelVersion, settings.Threshold,
            Math.Max(0, FaceLoginService.MaxAttemptsPerHour - used),
            new FaceQualityLimitsDto(
                limits.MinSharpness, limits.MinBrightness, limits.MaxBrightness,
                limits.MinFaceRatio, limits.MaxYaw, limits.MaxRoll),
            RequireLiveness: settings.RequireLiveness,
            RequireAttestation: settings.RequireAttestation,
            // Harakatlar KATALOGI (aniq ketma-ketlik `challenge` dan keladi) — ilova qaysi
            // harakatlarni o'lchashi kerakligini shundan biladi, o'zida ro'yxat tutmasin.
            LivenessActions: FaceLiveness.All,
            LivenessMinMs: FaceLiveness.MinActionMs,
            LivenessMaxMs: FaceLiveness.MaxActionMs);
    }

    /* =============================================================================================
     *  TIRIKLIK CHAQIRUVI — har selfi urinishidan OLDIN
     * ========================================================================================== */

    /// <summary>
    /// BIR MARTALIK chaqiruv: tasodifiy <c>nonce</c> + TASODIFIY harakatlar ketma-ketligi.
    ///
    /// <para>Ilova: (1) shu endpointni chaqiradi, (2) so'ralgan harakatlarni foydalanuvchidan
    /// oladi va HAR BIRINING bajarilish vaqtini o'lchaydi, (3) <c>verify</c> ga <c>nonce</c> +
    /// <c>liveness</c> bilan yuboradi. Play Integrity ishlatilsa — o'sha <c>nonce</c> integrity
    /// so'roviga ham qo'yiladi (server ikkalasini solishtiradi).</para>
    ///
    /// <para>⚠️ CHEKLANGAN token (<c>scope=face</c>) bilan ishlaydi — <see cref="FaceScopeGate"/>
    /// ro'yxatida. Aks holda selfi ekranidagi foydalanuvchi chaqiruv ololmasdi.</para>
    /// </summary>
    [HttpPost("challenge")]
    public async Task<ActionResult<FaceChallengeDto>> Challenge(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        if (me is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (UserId is null) return Unauthorized();

        var result = await face.IssueChallengeAsync(UserId, me.Id, ct);
        if (!result.Ok) return BadRequest(new { message = result.Reason });

        return new FaceChallengeDto(
            result.Nonce, result.Actions, result.ExpiresAt,
            FaceLiveness.ChallengeTtlSeconds, FaceLiveness.MinActionMs, FaceLiveness.MaxActionMs);
    }

    /* =============================================================================================
     *  PROFIL RASMI — etalon hisoblash uchun (FAQAT o'ziniki)
     * ========================================================================================== */

    /// <summary>
    /// O'quvchining O'Z profil rasmini BAYT sifatida beradi — ilova undan (xuddi shu model bilan)
    /// <c>refVector</c> hisoblaydi.
    ///
    /// <para>⚠️ Fayl `/uploads` STATIK yo'lidan berilmaydi: cheklangan tokenli sessiyaga
    /// `up_at` cookie'si qo'yilmaydi (Program.cs) va qo'yilganda ham u BUTUN `/uploads` ni
    /// ochib qo'yardi. Bu yerda esa faqat SO'RAGAN O'QUVCHINING o'z rasmi, diskdan o'qib
    /// beriladi (sertifikat endpointlari bilan bir xil naqsh — `uploads-security.md`).</para>
    /// </summary>
    [HttpGet("photo")]
    public async Task<IActionResult> Photo(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        if (me is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var url = me.BirthCertificateUrl;   // nomi ESKI — bu O'QUVCHI SURATI (CLAUDE.md)
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/", StringComparison.Ordinal))
            return NotFound(new { message = "Rasm yuklanmagan" });

        // Yo'l manipulyatsiyasidan himoya: bazadagi qiymatdan FAQAT fayl nomi olinadi.
        var name = Path.GetFileName(url);
        if (string.IsNullOrEmpty(name)) return NotFound();
        var path = Path.Combine(env.ContentRootPath, "uploads", name);
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "Rasm topilmadi" });

        var mime = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            _ => "image/jpeg",
        };
        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        // Kesh PRIVATE: biometrik ma'lumot proxy/Cloudflare umumiy keshiga tushmasin.
        Response.Headers.CacheControl = "private,no-store";
        return File(bytes, mime);
    }

    /* =============================================================================================
     *  TEKSHIRISH
     * ========================================================================================== */

    /// <summary>
    /// Selfi + vektor yuboriladi; server solishtiradi va muvaffaqiyatda TO'LIQ token qaytaradi.
    ///
    /// <para><b>multipart/form-data:</b> <c>image</c> (jpeg, ≤2 MB), <c>vector</c> (base64 float32 LE),
    /// <c>refVector</c> (ixtiyoriy — profil rasmidan, etalon yo'q bo'lganda), <c>quality</c> (JSON),
    /// <c>deviceId</c>, <c>deviceName</c>, <c>platform</c>, <c>appVersion</c>, <c>modelVersion</c>,
    /// <c>nonce</c> (`challenge` dan), <c>liveness</c> (JSON massiv),
    /// <c>integrityToken</c> (ixtiyoriy — Play Integrity).</para>
    ///
    /// <para>Rad etish HTTP <b>200</b> bilan qaytadi (<c>ok:false</c> + sabab) — ilova sababni
    /// ko'rsatadi va qayta urinadi. 4xx faqat TEXNIK xatolarda (buzuq vektor, katta fayl).</para>
    /// </summary>
    [HttpPost("verify")]
    [RequestSizeLimit(4_000_000)]
    public async Task<ActionResult<FaceVerifyResponse>> Verify(
        [FromForm] IFormFile? image,
        [FromForm] string? vector,
        [FromForm] string? refVector,
        [FromForm] string? quality,
        [FromForm] string? deviceId,
        [FromForm] string? deviceName,
        [FromForm] string? platform,
        [FromForm] string? appVersion,
        [FromForm] string? modelVersion,
        [FromForm] string? nonce,
        [FromForm] string? liveness,
        [FromForm] string? integrityToken,
        CancellationToken ct)
    {
        var me = await MeAsync(ct);
        if (me is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return Unauthorized();

        var settings = await face.SettingsAsync(ct);
        // Sozlama o'chirilgan bo'lsa tekshiradigan narsa yo'q — darhol TO'LIQ token
        // (admin modulni o'chirib qo'yganda ilovada "selfi" ekrani osilib qolmasin).
        if (!settings.Enabled)
        {
            var fullToken = jwt.CreateToken(user);
            IssueAuthCookies(fullToken);   // DUAL-MODE: web cookie rejimi (mobil e'tibor bermaydi)
            return new FaceVerifyResponse(true, FaceLoginService.StatusApproved, "", null,
                FaceLoginService.MaxAttemptsPerHour, fullToken);
        }

        var device = (deviceId ?? "").Trim();
        if (device.Length == 0)
            return BadRequest(new { message = "deviceId berilmagan" });

        // --- Vektorlar (klient ma'lumoti ISHONCHSIZ: buzuq bo'lsa 400, istisno EMAS) ---
        var selfie = FaceMatch.TryParse(vector, out var vecError);
        if (selfie is null) return BadRequest(new { message = vecError });

        float[]? reference = null;
        if (!string.IsNullOrWhiteSpace(refVector))
        {
            reference = FaceMatch.TryParse(refVector, out var refError);
            if (reference is null) return BadRequest(new { message = refError });
        }

        // --- Rasm ---
        if (image is null || image.Length == 0) return BadRequest(new { message = "Rasm berilmagan" });
        if (image.Length > MaxImageBytes) return BadRequest(new { message = "Rasm 2 MB dan katta" });
        var ext = Path.GetExtension(image.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext))
            return BadRequest(new { message = "Selfi faqat JPEG bo'lishi kerak" });

        // Selfi `uploads/face/` ga yoziladi — o'sha papka zaxira arxividan chiqarilgan va statik
        // yo'l bilan berilmaydi (`FaceStorage` izohi).
        var imageUrl = FaceStorage.NewUrl();
        var target = FaceStorage.ResolvePath(env.ContentRootPath, imageUrl)!;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var fs = System.IO.File.Create(target))
            await image.CopyToAsync(fs, ct);

        // ILOVA HAQIQIYLIGI — tashqi so'rov (5 s timeout, xatoda `unavailable`). Sozlama
        // o'chiq bo'lsa ham chaqiriladi: natija jurnalga yozilib, admin "majburiy qilsammi"
        // degan qarorni RAQAMLARGA tayanib qabul qilsin.
        var attestation = await attest.VerifyAsync(integrityToken, platform, nonce, ct);

        var result = await face.VerifyAsync(new FaceLoginService.VerifyRequest(
            Student: me,
            UserId: user.Id,
            ImageUrl: imageUrl,
            Selfie: selfie,
            RefVector: reference,
            QualityJson: quality ?? "",
            ModelVersion: (modelVersion ?? "").Trim(),
            DeviceId: device,
            DeviceName: (deviceName ?? "").Trim(),
            Platform: (platform ?? "").Trim(),
            AppVersion: (appVersion ?? "").Trim(),
            Ip: ClientIp(),
            Nonce: (nonce ?? "").Trim(),
            LivenessJson: liveness ?? "",
            AttestVerdict: attestation.Verdict,
            AttestReason: attestation.Reason), ct);

        // MAXFIYLIK: chegaradan oshgan eski selfilar — yozuvi servis tomonidan o'chirildi,
        // FAYLLARI shu yerda o'chiriladi (biometrik ma'lumot cheksiz to'planmasin).
        DeleteFiles(result.RemovedImages);
        // Urinish umuman YOZILMAGAN bo'lsa (chegara / model mos emas / nonce poygasi) — endigina
        // saqlangan rasm ham keraksiz: uni qoldirsak diskda "egasiz" selfilar yig'ilib qolardi.
        // ⚠️ Bu qaror SABAB MATNI bo'yicha emas, `Recorded` bayrog'i bo'yicha qabul qilinadi —
        // `ReasonOldApp` ikki joydan keladi va ulardan birida yozuv BOR (`VerifyResult.Recorded`
        // izohi). Ilgari o'sha holatda fayl o'chib, admin ro'yxatida buzuq rasm qolardi.
        if (!result.Recorded) DeleteFiles(new[] { imageUrl });

        if (!result.Ok)
            return new FaceVerifyResponse(false, result.Status, result.Reason, result.Score,
                result.AttemptsLeft);

        logger.LogInformation(
            "Yuz tasdiqlandi: studentId={StudentId}, ball={Score}, etalon={Enrolled}",
            me.Id, result.Score, result.Enrolled);
        var token = jwt.CreateToken(user);
        IssueAuthCookies(token);   // DUAL-MODE: web cookie rejimi (mobil e'tibor bermaydi)
        return new FaceVerifyResponse(true, result.Status, "", result.Score, result.AttemptsLeft,
            token, result.Enrolled);
    }

    /// <summary>Web cookie rejimi uchun `at`/`csrf` cookie'larini qo'yadi (token JSON'da ham qaytadi).</summary>
    private void IssueAuthCookies(string token)
    {
        var expiresUtc = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token).ValidTo; // UTC
        IntellectCRM.Server.AuthCookies.Issue(HttpContext, token, expiresUtc);
    }

    private void DeleteFiles(IReadOnlyList<string> urls)
    {
        foreach (var url in urls)
        {
            // `FaceStorage` yangi (`/uploads/face/...`) va eski (`/uploads/...`) manzillarni ham
            // tushunadi, yo'l manipulyatsiyasini esa `null` bilan rad etadi.
            var path = FaceStorage.ResolvePath(env.ContentRootPath, url);
            if (path is null) continue;
            try { System.IO.File.Delete(path); }
            catch (Exception ex)
            {
                // Fayl o'chmasa ham kirish jarayoni buzilmasin — faqat logga.
                logger.LogWarning(ex, "Eski selfi faylini o'chirib bo'lmadi");
            }
        }
    }

    /// <summary>Haqiqiy mijoz IP'si (Cloudflare tunnel ortida RemoteIpAddress — tunnel IP'si).</summary>
    private string ClientIp()
    {
        var cf = Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
        var xff = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    }
}
