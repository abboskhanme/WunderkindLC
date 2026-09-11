using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// YUZ BILAN KIRISH — admin tomoni (`/api/admin/face/*`): urinishlar jurnali, kutilayotganini
/// tasdiqlash/rad etish, ishonchli qurilmalarni bekor qilish, etalonni tozalash va sozlamalar.
///
/// <para>⚠️ <b>O'QISH ham darvozalangan</b> (<c>ReadRequiresPerm = true</c>): javobda o'quvchining
/// SELFI manzillari (`/uploads/...`) qaytadi. <c>AdminPerm</c> da GET odatda har qanday xodimga
/// ochiq — bu yerda esa manzilni olgan xodim bolalarning suratlarini abadiy saqlab qola olardi
/// (`.claude/rules/uploads-security.md`).</para>
///
/// <para>⚠️ <b>Auditga selfi MANZILI yozilmaydi</b> — tarixni ko'rgan har kim faylni olib
/// qolardi (`.claude/rules/audit.md`). Yozuvlarda faqat o'quvchi, ball va sabab bo'ladi.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.face", ReadRequiresPerm = true)]
[Route("api/admin/face")]
public class AdminFaceController(
    AppDbContext db, FaceLoginService face, AuditService audit,
    IWebHostEnvironment env, ILogger<AdminFaceController> logger) : ControllerBase
{
    /// <summary>Bir so'rovda qaytadigan eng ko'p urinish (audit ro'yxati bilan bir xil siyosat).</summary>
    private const int MaxLimit = 500;

    /* =============================================================================================
     *  URINISHLAR
     * ========================================================================================== */

    /// <summary>Yuz tekshiruvi urinishlari — filtr: holat, o'quvchi, sana oralig'i.</summary>
    [HttpGet("checks")]
    public async Task<ActionResult<List<FaceCheckDto>>> Checks(
        [FromQuery] string? status, [FromQuery] string? studentId,
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var q = db.LoginFaceChecks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(studentId)) q = q.Where(c => c.StudentId == studentId);
        if (!string.IsNullOrWhiteSpace(from))
            q = q.Where(c => string.Compare(c.CreatedAt, from) >= 0);
        // `to` KUN sifatida beriladi — kunning oxirigacha cho'ziladi (audit ro'yxatidagi bilan
        // bir xil qoida, aks holda o'sha kunning o'zi tushib qolardi).
        if (!string.IsNullOrWhiteSpace(to))
            q = q.Where(c => string.Compare(c.CreatedAt, to + "T23:59:59") <= 0);

        var take = Math.Clamp(limit, 1, MaxLimit);
        var rows = await q.OrderByDescending(c => c.CreatedAt).Take(take).ToListAsync(ct);

        var ids = rows.Select(r => r.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        return rows.Select(c => new FaceCheckDto(
            c.Id, c.StudentId, names.GetValueOrDefault(c.StudentId, "—"), c.CreatedAt,
            c.Status, c.Reason, c.Score,
            // ⚠️ `/uploads/...` MANZILI BERILMAYDI — selfilar `uploads/face/` da va u papka
            // statik yo'l bilan umuman ochilmaydi. UI shu API manzilini <img src> ga qo'yadi.
            ImageUrl: string.IsNullOrEmpty(c.ImageUrl) ? "" : $"/api/admin/face/checks/{c.Id}/image",
            c.DeviceId, c.DeviceName, c.Platform, c.AppVersion,
            c.Ip, c.ModelVersion, c.Quality,
            // Tasdiqlash faqat KUTILAYOTGAN va vektori saqlangan urinishda mumkin (vektorsiz
            // etalon yasab bo'lmaydi — UI tugmani ko'rsatmasin).
            CanApprove: c.Status == FaceLoginService.StatusPending && c.Vector is { Length: > 0 },
            Attested: c.Attested, AttestReason: c.AttestReason))
            .ToList();
    }

    /// <summary>
    /// Urinish SELFISI — fayl diskdan o'qib beriladi (sertifikat endpointlari bilan bir xil naqsh,
    /// <c>.claude/rules/uploads-security.md</c>).
    ///
    /// <para>⚠️ Bu YAGONA yo'l: <c>uploads/face</c> papkasi <c>PrivateFolderFileProvider</c> da
    /// bloklangan, ya'ni manzilni bilgan odam ham faylni statik yo'ldan ololmaydi. Bu yerda esa
    /// <c>AdminPerm("students.face", ReadRequiresPerm = true)</c> darvozasi ishlaydi.</para>
    /// </summary>
    [HttpGet("checks/{id}/image")]
    public async Task<IActionResult> CheckImage(string id, CancellationToken ct)
    {
        var url = await db.LoginFaceChecks.AsNoTracking()
            .Where(c => c.Id == id).Select(c => c.ImageUrl).FirstOrDefaultAsync(ct);
        return ServeImage(url);
    }

    /// <summary>Etalonning "dalili" (<c>StudentFaceProfile.SampleUrl</c>) — yuqoridagi bilan bir xil siyosat.</summary>
    [HttpGet("profile/{studentId}/image")]
    public async Task<IActionResult> ProfileImage(string studentId, CancellationToken ct)
    {
        var url = await db.StudentFaceProfiles.AsNoTracking()
            .Where(p => p.StudentId == studentId).Select(p => p.SampleUrl).FirstOrDefaultAsync(ct);
        return ServeImage(url);
    }

    private IActionResult ServeImage(string? url)
    {
        var path = FaceStorage.ResolvePath(env.ContentRootPath, url);
        if (path is null || !System.IO.File.Exists(path)) return NotFound(new { message = "Rasm topilmadi" });
        // Kesh PRIVATE + no-store: biometrik surat proxy/Cloudflare umumiy keshiga tushmasin.
        Response.Headers.CacheControl = "private,no-store";
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            _ => "image/jpeg",
        };
        return PhysicalFile(path, mime);
    }

    /// <summary>Kutilayotgan urinishni TASDIQLAYDI — o'sha selfi ETALON bo'ladi.</summary>
    [HttpPost("checks/{id}/approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        var check = await db.LoginFaceChecks.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (check is null) return NotFound(new { message = "Urinish topilmadi" });

        if (await face.ApproveCheckAsync(check, ct) is { } error)
            return BadRequest(new { message = error });

        var name = await StudentNameAsync(check.StudentId, ct);
        // Selfi MANZILI ataylab yozilmaydi (biometrik ma'lumot tarixga tushmasin).
        audit.Record(FaceLoginService.AuditEntityProfile, check.StudentId, "update",
            $"«{name}» uchun yuz etaloni tasdiqlandi (kirish urinishi asosida)",
            studentId: check.StudentId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Etalon saqlandi" });
    }

    /// <summary>Kutilayotgan urinishni rad etadi (ixtiyoriy sabab bilan).</summary>
    [HttpPost("checks/{id}/reject")]
    public async Task<IActionResult> Reject(string id, [FromBody] FaceRejectPayload? payload, CancellationToken ct)
    {
        var check = await db.LoginFaceChecks.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (check is null) return NotFound(new { message = "Urinish topilmadi" });

        if (face.RejectCheck(check, payload?.Note) is { } error)
            return BadRequest(new { message = error });

        var name = await StudentNameAsync(check.StudentId, ct);
        audit.Record(FaceLoginService.AuditEntityProfile, check.StudentId, "update",
            $"«{name}» ning yuz tekshiruvi rad etildi: {check.Reason}",
            studentId: check.StudentId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Rad etildi" });
    }

    /* =============================================================================================
     *  ISHONCHLI QURILMALAR
     * ========================================================================================== */

    /// <summary>
    /// Ishonchli qurilmalar (ixtiyoriy — bitta o'quvchi bo'yicha).
    ///
    /// <para>⚠️ Tartib: avval QURILMALAR o'qiladi, keyin faqat o'shalarning egalari. Ilgari
    /// teskarisi edi — BUTUN o'quvchilar jadvali xotiraga yig'ilib, ularning ID'lari bitta
    /// ulkan <c>IN (...)</c> ro'yxatiga aylanardi (bir necha ming o'quvchida so'rov cho'kib
    /// ketardi), holbuki javobga ko'pi bilan <see cref="MaxLimit"/> ta qator chiqadi.</para>
    /// </summary>
    [HttpGet("devices")]
    public async Task<ActionResult<List<FaceDeviceDto>>> Devices(
        [FromQuery] string? studentId, CancellationToken ct)
    {
        // Qurilma AKKAUNTGA (AppUser) bog'langan, filtr esa o'quvchi bo'yicha so'raladi.
        var q = db.TrustedDevices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var ownerId = await db.Students.AsNoTracking()
                .Where(s => s.Id == studentId).Select(s => s.UserId).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(ownerId)) return new List<FaceDeviceDto>();
            q = q.Where(d => d.UserId == ownerId);
        }

        var rows = await q.OrderByDescending(d => d.LastSeenAt).Take(MaxLimit).ToListAsync(ct);
        if (rows.Count == 0) return new List<FaceDeviceDto>();

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var owners = await db.Students.AsNoTracking()
            .Where(s => s.UserId != null && userIds.Contains(s.UserId))
            .Select(s => new { s.Id, s.UserId, s.FullName })
            .ToListAsync(ct);
        // ⚠️ `ToDictionary` EMAS: bitta akkauntga ikkita o'quvchi yozuvi biriktirilib qolgan
        // (ma'lumot nuqsoni) baza butun sahifani 500 bilan yiqitmasin.
        var byUser = new Dictionary<string, (string Id, string FullName)>();
        foreach (var s in owners) byUser.TryAdd(s.UserId!, (s.Id, s.FullName));

        return rows.Select(d =>
        {
            var owner = byUser.TryGetValue(d.UserId, out var s) ? s : (Id: "", FullName: "—");
            return new FaceDeviceDto(
                d.Id, d.UserId, owner.Id, owner.FullName,
                d.DeviceId, d.DeviceName, d.Platform, d.CreatedAt, d.LastSeenAt, d.RevokedAt);
        }).ToList();
    }

    /// <summary>Ishonchli qurilmani BEKOR qiladi (telefon yo'qolganda) — o'sha qurilmada keyingi
    /// kirishda yana selfi so'raladi. Yozuv O'CHIRILMAYDI: "qachon bekor qilingan" tarixi qolsin.</summary>
    [HttpPost("devices/{id}/revoke")]
    public async Task<IActionResult> RevokeDevice(string id, CancellationToken ct)
    {
        var device = await db.TrustedDevices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return NotFound(new { message = "Qurilma topilmadi" });
        if (!string.IsNullOrEmpty(device.RevokedAt))
            return BadRequest(new { message = "Bu qurilma allaqachon bekor qilingan" });

        device.RevokedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        var student = await db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == device.UserId, ct);
        var label = string.IsNullOrWhiteSpace(device.DeviceName) ? "qurilma" : device.DeviceName;
        audit.Record(FaceLoginService.AuditEntityDevice, device.Id, "update",
            $"«{student?.FullName ?? "—"}» ning ishonchli qurilmasi bekor qilindi ({label})",
            studentId: student?.Id);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Qurilma bekor qilindi" });
    }

    /* =============================================================================================
     *  ETALON
     * ========================================================================================== */

    /// <summary>O'quvchining etalon holati (bormi, qaysi model, qachon).</summary>
    [HttpGet("profile/{studentId}")]
    public async Task<ActionResult<FaceProfileDto>> Profile(string studentId, CancellationToken ct)
    {
        var p = await db.StudentFaceProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudentId == studentId, ct);
        if (p is null) return NotFound(new { message = "Etalon yo'q" });
        var name = await StudentNameAsync(studentId, ct);
        return new FaceProfileDto(
            p.StudentId, name, p.ModelVersion, p.Source,
            // Manzil emas, API yo'li (yuqoridagi `ServeImage` izohi).
            SampleUrl: string.IsNullOrEmpty(p.SampleUrl) ? "" : $"/api/admin/face/profile/{p.StudentId}/image",
            p.Dim, p.CreatedAt, p.UpdatedAt);
    }

    /// <summary>
    /// Etalonni TOZALAYDI — o'quvchi keyingi kirishda qaytadan ro'yxatdan o'tadi (profil rasmi
    /// yangilanganda yoki bola o'sib, eski etalon mos kelmay qolganda kerak).
    /// Ishonchli qurilmalar TEGILMAYDI: ular allaqachon tasdiqlangan telefonlar; hammasini
    /// birdan chiqarib yuborish kerak bo'lsa qurilmalar alohida bekor qilinadi.
    /// </summary>
    [HttpDelete("profile/{studentId}")]
    public async Task<IActionResult> DeleteProfile(string studentId, CancellationToken ct)
    {
        var p = await db.StudentFaceProfiles.FirstOrDefaultAsync(x => x.StudentId == studentId, ct);
        if (p is null) return NotFound(new { message = "Etalon yo'q" });

        var sampleUrl = p.SampleUrl;
        db.StudentFaceProfiles.Remove(p);

        var name = await StudentNameAsync(studentId, ct);
        audit.Record(FaceLoginService.AuditEntityProfile, studentId, "delete",
            $"«{name}» ning yuz etaloni o'chirildi — keyingi kirishda qayta ro'yxatdan o'tadi",
            studentId: studentId);
        await db.SaveChangesAsync(ct);

        // Etalon "dalili" ham qoladigan joyi yo'q — biometrik ma'lumot ortiqcha yotmasin.
        // ⚠️ Lekin AYNI fayl urinishlar jurnalidagi qatorga ham tegishli bo'lishi mumkin: etalon
        // odatda o'sha urinishning selfisidan yasaladi (`ApproveCheckAsync` / birinchi kirish) va
        // `CleanupAsync` uni ATAYIN o'chirmay saqlab turadi. Shu sabab fayl faqat unga ishora
        // qiladigan urinish QOLMAGANDA o'chiriladi — aks holda admin ro'yxatida buzuq rasm qolardi.
        var stillUsed = !string.IsNullOrEmpty(sampleUrl)
            && await db.LoginFaceChecks.AsNoTracking().AnyAsync(c => c.ImageUrl == sampleUrl, ct);
        if (!stillUsed) DeleteFile(sampleUrl);
        return Ok(new { message = "Etalon o'chirildi" });
    }

    /* =============================================================================================
     *  SOZLAMALAR
     * ========================================================================================== */

    [HttpGet("settings")]
    public async Task<ActionResult<FaceSettingsDto>> GetSettings(CancellationToken ct)
    {
        var s = await face.SettingsAsync(ct);
        return new FaceSettingsDto(
            s.Enabled, s.Threshold, s.ModelVersion, s.KeepChecks,
            s.RequireLiveness, s.RequireAttestation,
            // ⚠️ `.env` HOLATI (kalitning O'ZI EMAS) — `EnvSecretDto` bilan bir xil siyosat:
            // UI faqat "sozlangan / sozlanmagan" ni ko'rsatadi.
            VaultReady: s.VaultReady,
            AttestationConfigured: AppAttestation.Configured);
    }

    /// <summary>
    /// Sozlamalarni saqlaydi. ⚠️ Bular MAXFIY EMAS (kalit/parol emas) — shuning uchun
    /// `CenterMeta`da (`.env` emas), <c>CLAUDE.md</c> "KALITLAR — FAQAT .env" qoidasiga mos.
    /// </summary>
    [HttpPut("settings")]
    public async Task<ActionResult<FaceSettingsDto>> PutSettings(
        [FromBody] FaceSettingsDto payload, CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }

        // ⚠️ HAMMANI QULFLAB QO'YADIGAN SOZLAMA. `AppAttestation.Gate` majburiy rejimda
        // FAIL-CLOSED: `Ok` dan boshqa HAMMA xulosa — `NotConfigured` ham — rad etiladi.
        // Kalitlar qo'yilmagan bo'lsa `VerifyAsync` hech qachon `Ok` qaytara olmaydi, ya'ni
        // yoqilgan zahoti BITTA ham o'quvchi kira olmay qoladi (jumladan admin o'zi ham
        // ilovadan). Shuning uchun yoqishga faqat Android tomoni HAQIQATAN sozlanganda
        // ruxsat beramiz.
        if (payload.RequireAttestation && !AppAttestation.Configured)
        {
            return BadRequest(new
            {
                message = "Ilova haqiqiyligini tekshirish yoqilmadi: PLAY_INTEGRITY_PACKAGE va "
                        + "PLAY_INTEGRITY_SA_JSON sozlanmagan. Hozir yoqilsa hech kim kira olmaydi.",
            });
        }

        var before = new
        {
            meta.LoginFaceEnabled, meta.LoginFaceThreshold, meta.LoginFaceModelVersion,
            meta.LoginFaceKeepChecks, meta.LoginFaceRequireLiveness, meta.LoginFaceRequireAttestation,
        };

        meta.LoginFaceEnabled = payload.Enabled;
        // Chegara 0.05..0.99: 0 hammani kiritardi, 1 esa hech kimni (kosinus amalda 1 ga
        // yetmaydi) — ikkalasi ham modulni jimgina buzardi.
        meta.LoginFaceThreshold = Math.Clamp(payload.Threshold, 0.05, 0.99);
        meta.LoginFaceModelVersion = (payload.ModelVersion ?? "").Trim();
        // ⚠️ ENG KAMI — `MaxAttemptsPerHour`, 1 EMAS. Soatlik urinishlar chegarasi AYNAN shu
        // jurnal qatorlaridan sanaladi (`RecentAttemptsAsync`); saqlanadigan qator soni chegaradan
        // kam bo'lsa hisob hech qachon chegaraga yetmaydi va brute-force himoyasi JIMGINA
        // ishlamay qo'yadi. Bu — `MaxChallengesPerHour` uchun tozalash chegarasi 1 soat qilib
        // qo'yilgani bilan bir xil sabab.
        meta.LoginFaceKeepChecks = Math.Clamp(
            payload.KeepChecks, FaceLoginService.MaxAttemptsPerHour, 100);
        meta.LoginFaceRequireLiveness = payload.RequireLiveness;
        meta.LoginFaceRequireAttestation = payload.RequireAttestation;

        var after = new
        {
            meta.LoginFaceEnabled, meta.LoginFaceThreshold, meta.LoginFaceModelVersion,
            meta.LoginFaceKeepChecks, meta.LoginFaceRequireLiveness, meta.LoginFaceRequireAttestation,
        };
        audit.Record("CenterMeta", "face-login", "update",
            meta.LoginFaceEnabled
                ? $"Yuz bilan kirish YOQILDI (chegara {meta.LoginFaceThreshold:0.00}, model «{meta.LoginFaceModelVersion}», "
                  + $"tiriklik {(meta.LoginFaceRequireLiveness ? "MAJBURIY" : "ixtiyoriy")}, "
                  + $"ilova tekshiruvi {(meta.LoginFaceRequireAttestation ? "MAJBURIY — iOS foydalanuvchilari KIRA OLMAYDI" : "ixtiyoriy")})"
                : "Yuz bilan kirish O'CHIRILDI",
            before: before, after: after);
        await db.SaveChangesAsync(ct);

        var s = await face.SettingsAsync(ct);
        return new FaceSettingsDto(
            s.Enabled, s.Threshold, s.ModelVersion, s.KeepChecks,
            s.RequireLiveness, s.RequireAttestation, s.VaultReady, AppAttestation.Configured);
    }

    /* =============================================================================================
     *  Yordamchilar
     * ========================================================================================== */

    private async Task<string> StudentNameAsync(string studentId, CancellationToken ct) =>
        await db.Students.AsNoTracking().Where(s => s.Id == studentId)
            .Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? "—";

    private void DeleteFile(string? url)
    {
        var path = FaceStorage.ResolvePath(env.ContentRootPath, url);
        if (path is null) return;
        try { System.IO.File.Delete(path); }
        catch (Exception ex) { logger.LogWarning(ex, "Etalon selfi faylini o'chirib bo'lmadi"); }
    }
}
