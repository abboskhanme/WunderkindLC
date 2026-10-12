using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
// ⚠️ SINF darajasidagi kalit — O'QISH uchun (GET xodimga ochiq, `HasSection` sahifa
// ruxsatini ham hisobga oladi). YOZISH esa har bir SAHIFA kaliti bilan alohida
// darvozalangan (metod darajasidagi `[AdminPerm]` sinfdagisini bekor qiladi), chunki bu
// controller o'nga yaqin MUSTAQIL sozlama sahifasiga xizmat qiladi: "Zaxira nusxa" berilgan
// xodim "Markaz ma'lumotlari"ni o'zgartira olmasin.
[AdminPerm("settings")]
[Route("api/admin/settings")]
public class SettingsController(AppDbContext db, TelegramService telegram, IWebHostEnvironment env, IConfiguration config, AuditService audit, CameraGateway cameraGateway) : ControllerBase
{
    /// <summary>
    /// Sozlama o'zgarishini "O'zgarishlar tarixi"ga yozadi (bo'lim: <b>Sozlamalar</b>).
    ///
    /// <para>O'zgargan MAYDONLAR ro'yxatini EF ChangeTracker'dan o'zi oladi — har bir endpointda
    /// qo'lda sanab o'tirmaslik uchun. Shu sababdan <c>SaveChangesAsync</c> dan <b>OLDIN</b>
    /// chaqirilishi SHART: saqlangandan keyin "o'zgargan" belgisi tozalanadi va ro'yxat bo'sh chiqadi.
    /// Yozuvning o'zi ham o'sha SaveChanges bilan birga saqlanadi (audit alohida saqlamaydi).</para>
    ///
    /// <para>Maxfiy qiymatlar (bot tokeni, API kalitlari) bazada UMUMAN saqlanmaydi — ular
    /// <c>.env</c> da (qarang: CLAUDE.md "KALITLAR"), demak bu yerga ham tushmaydi. Baribir
    /// ehtiyot uchun faqat maydon NOMLARI yoziladi, qiymatlari emas.</para>
    ///
    /// <para>Mantiqning O'ZI endi <see cref="SettingsAudit"/> da — boshqa controllerlardagi sozlamalar
    /// (bonus, AI tekshiruv, landing) ham AYNAN shu yo'l bilan yoziladi.</para>
    /// </summary>
    private void LogSettings(string what) =>
        SettingsAudit.Record<CenterMeta>(audit, db.ChangeTracker, what);

    [HttpGet]
    public async Task<ActionResult<SchoolSettingsDto>> Get()
    {
        var reasons = await db.AbsenceReasons
            .Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate, r.IsExcused)).ToListAsync();
        var quarters = await TuitionService.SyntheticPeriodsAsync(db);
        // Dars vaqtlari (qo'ng'iroqlar jadvali) olib tashlandi — bo'sh ro'yxat.
        return new SchoolSettingsDto(new List<LessonTimeDto>(), reasons, quarters);
    }

    [HttpGet("school")]
    public async Task<ActionResult<SchoolInfoDto>> GetSchool()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new SchoolInfoDto(
            m?.Name ?? "", m?.Director ?? "", m?.Phone ?? "", m?.Email ?? "",
            m?.Address ?? "", m?.Region ?? "", m?.District ?? "", m?.LogoUrl ?? "");
    }

    [HttpPut("school")]
    [AdminPerm("settings.school")]
    public async Task<IActionResult> SaveSchool(SchoolInfoDto req)
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null)
        {
            m = new CenterMeta();
            db.CenterMeta.Add(m);
        }
        m.Name = req.Name;
        m.Director = req.Director;
        m.Phone = req.Phone;
        m.Email = req.Email;
        m.Address = req.Address;
        m.Region = req.Region;
        m.District = req.District;
        LogSettings("Markaz ma'lumotlari");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Markaz logotipini yuklaydi (rasm) — barcha foydalanuvchi ko'radigan joylarda ko'rsatiladi.</summary>
    [HttpPost("logo")]
    [AdminPerm("settings.school")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<SchoolInfoDto>> UploadLogo(IFormFile file)
    {
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        m.LogoUrl = $"/uploads/{stored}";
        LogSettings("Markaz logotipi yuklandi");
        await db.SaveChangesAsync();
        return await GetSchool();
    }

    /// <summary>Logotipni o'chiradi.</summary>
    [HttpDelete("logo")]
    [AdminPerm("settings.school")]
    public async Task<ActionResult<SchoolInfoDto>> DeleteLogo()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is not null)
        {
            m.LogoUrl = "";
            LogSettings("Markaz logotipi o'chirildi");
            await db.SaveChangesAsync();
        }
        return await GetSchool();
    }

    // ---------- Telegram bot ----------
    // TOKEN (maxfiy) — faqat .env: TELEGRAM_BOT_TOKEN. Bu yerda faqat maxfiy BO'LMAGAN qismlar
    // (bot username/nomi, kanal, telefon moslash) saqlanadi.

    [HttpGet("telegram")]
    public async Task<ActionResult<TelegramSettingsDto>> GetTelegram()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        // Majburiy obuna tekshiruvi ishlayaptimi (bot kanalda admin bo'lishi shart) — 5 soniyalik
        // chegara bilan, Telegram javob bermasa sozlamalar sahifasi kutib qolmasin.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var (chStatus, chMessage) = await telegram.CheckChannelAsync(m?.TelegramChannel, cts.Token);
        return new TelegramSettingsDto(
            m?.TelegramBotUsername ?? "", m?.TelegramBotName ?? "",
            telegram.IsConfigured, m?.TelegramChannel ?? "",
            m?.TelegramPhoneMatchField is "student" ? "student" : "parent",
            chStatus, chMessage,
            new EnvSecretDto(AppSecrets.EnvKeys.TelegramBotToken, AppSecrets.TelegramConfigured));
    }

    [HttpPut("telegram")]
    [AdminPerm("settings.channels")]
    public async Task<ActionResult<TelegramSettingsDto>> SaveTelegram(SaveTelegramSettingsRequest req)
    {
        if (EnvOnly(req.BotToken, AppSecrets.EnvKeys.TelegramBotToken) is { } err) return err;

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null)
        {
            m = new CenterMeta();
            db.CenterMeta.Add(m);
        }
        m.TelegramBotUsername = (req.BotUsername ?? "").Trim().TrimStart('@');
        m.TelegramBotName = (req.BotName ?? "").Trim();
        m.TelegramChannel = (req.Channel ?? "").Trim();
        m.TelegramPhoneMatchField = req.PhoneMatchField is "student" ? "student" : "parent";
        LogSettings("Telegram bot");
        await db.SaveChangesAsync();

        // Ishlab turgan xizmat (va bot) darrov yangi nomni ishlatishi uchun keshni yangilaymiz.
        telegram.Set(m.TelegramBotUsername, m.TelegramBotName);

        return await GetTelegram();
    }

    /// <summary>
    /// MAXFIY QIYMATNI QABUL QILMASLIK: kalit/parol endi FAQAT <c>.env</c> orqali beriladi
    /// (<see cref="AppSecrets"/>) va bazada saqlanmaydi. Eski mijoz (keshlangan SPA) qiymat
    /// yuborsa — jimgina yutib yubormasdan tushunarli 400 qaytaramiz.
    /// Bo'sh/berilmagan qiymat — normal (xato yo'q).
    /// </summary>
    /// <param name="current">Ayni paytda amalda bo'lgan (.env dagi) qiymat — GET qaytaradigan
    /// maydonlar uchun (turniket logini, Azure regioni). Mijoz o'sha qiymatni O'ZGARTIRMASDAN
    /// qaytarib yuborsa bu "yozish" emas — jimgina o'tkazamiz, aks holda admin o'sha formadagi
    /// boshqa (maxfiy bo'lmagan) sozlamalarni ham saqlay olmay qolardi.</param>
    private BadRequestObjectResult? EnvOnly(string? value, string envKey, string? current = null)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        if (!string.IsNullOrEmpty(current) && v == current.Trim()) return null;
        return BadRequest(new
        {
            message = $"Bu kalit endi UI'dan saqlanmaydi — serverdagi .env fayliga "
                      + $"{envKey}=... qatorini qo'shing va `docker compose up -d` qiling.",
        });
    }

    // ---------- Telegram backup ----------

    [HttpGet("telegram-backup")]
    public async Task<ActionResult<TelegramBackupConfigDto>> GetTelegramBackupConfig()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new TelegramBackupConfigDto(
            m?.TelegramAdminChatId ?? "",
            m?.BackupScheduleHour ?? 21,
            m?.BackupScheduleMinute ?? 0,
            m?.TelegramBackupEnabled ?? true,
            m?.TelegramBackupLastSentAt);
    }

    [HttpPost("telegram-backup")]
    [AdminPerm("settings.backup")]
    public async Task<ActionResult<TelegramBackupConfigDto>> SaveTelegramBackupConfig(SaveTelegramBackupConfigRequest req)
    {
        if (req.ScheduleHour is < 0 or > 23)
            return BadRequest(new { message = "ScheduleHour 0-23 oralig'ida bo'lishi kerak" });
        if (req.ScheduleMinute is < 0 or > 59)
            return BadRequest(new { message = "ScheduleMinute 0-59 oralig'ida bo'lishi kerak" });

        var chatId = (req.AdminChatId ?? "").Trim();
        if (chatId.Length > 0)
        {
            if (!long.TryParse(chatId, out var parsed) || parsed == 0)
                return BadRequest(new { message = "AdminChatId faqat raqam bo'lishi kerak (masalan: 123456789)" });
        }

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }

        m.TelegramAdminChatId = chatId.Length > 0 ? chatId : null;
        m.BackupScheduleHour = req.ScheduleHour;
        m.BackupScheduleMinute = req.ScheduleMinute;
        m.TelegramBackupEnabled = req.Enabled;
        LogSettings("Zaxira nusxa (Telegram)");
        await db.SaveChangesAsync();

        return new TelegramBackupConfigDto(
            m.TelegramAdminChatId ?? "",
            m.BackupScheduleHour,
            m.BackupScheduleMinute,
            m.TelegramBackupEnabled,
            m.TelegramBackupLastSentAt);
    }

    // (Push (Firebase) sozlamasi — settings/firebase — 2026-10 da push bilan birga olib tashlandi.
    //  FCM service account kaliti .env da qoladi: yuz bilan kirish (Play Integrity) zaxira kaliti.)

    // ---------- Speaking (Azure Pronunciation Assessment) ----------

    // Kalit ham, region ham .env dan (AZURE_SPEECH_KEY / AZURE_SPEECH_REGION) — UI faqat holatni ko'rsatadi.

    [HttpGet("azure-speech")]
    public ActionResult<AzureSpeechSettingsDto> GetAzureSpeech() =>
        new AzureSpeechSettingsDto(
            AppSecrets.AzureSpeechRegion,
            AppSecrets.AzureSpeechConfigured,
            new EnvSecretDto(AppSecrets.EnvKeys.AzureSpeechKey, AppSecrets.AzureSpeechKey.Length > 0),
            AppSecrets.EnvKeys.AzureSpeechRegion);

    [HttpPut("azure-speech")]
    [AdminPerm("settings.azure-speech")]
    public ActionResult<AzureSpeechSettingsDto> SaveAzureSpeech(SaveAzureSpeechRequest req)
    {
        if (EnvOnly(req.Key, AppSecrets.EnvKeys.AzureSpeechKey) is { } keyErr) return keyErr;
        if (EnvOnly(req.Region, AppSecrets.EnvKeys.AzureSpeechRegion, AppSecrets.AzureSpeechRegion) is { } regionErr) return regionErr;
        return GetAzureSpeech();
    }

    // ---------- AI Tahlil (Google Gemini) ----------
    // Kalit .env dan (GEMINI_API_KEY), model ham env'dan (GEMINI_MODEL).

    [HttpGet("gemini")]
    public ActionResult<GeminiSettingsDto> GetGemini() =>
        new GeminiSettingsDto(
            GeminiService.ResolveModel(config),
            AppSecrets.GeminiConfigured,
            new EnvSecretDto(AppSecrets.EnvKeys.GeminiApiKey, AppSecrets.GeminiConfigured));

    [HttpPut("gemini")]
    [AdminPerm("settings.gemini")]
    public ActionResult<GeminiSettingsDto> SaveGemini(SaveGeminiRequest req)
    {
        if (EnvOnly(req.Key, AppSecrets.EnvKeys.GeminiApiKey) is { } err) return err;
        return GetGemini();
    }

    // ---------- To'lov cheki (termal kvitansiya) sozlamalari ----------

    /// <summary>Chek sozlamalari (JSON). Bo'sh = standart shablon (frontend default'i ishlaydi).</summary>
    [HttpGet("check")]
    public async Task<ActionResult<CheckSettingsDto>> GetCheck()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new CheckSettingsDto(m?.CheckSettings ?? "");
    }

    [HttpPut("check")]
    [AdminPerm("settings.check")]
    public async Task<ActionResult<CheckSettingsDto>> SaveCheck(CheckSettingsDto req)
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        m.CheckSettings = (req.Json ?? "").Trim();
        LogSettings("To'lov cheki");
        await db.SaveChangesAsync();
        return new CheckSettingsDto(m.CheckSettings);
    }

    // (SMS, Local SMS va ilova (APK) sozlamalari — settings/eskiz, settings/local-sms, settings/app-apk —
    //  2026-10 da kanallar bilan birga olib tashlandi. Telegram yagona tashqi kanal.)

    // ---------- Turniket / FaceID integratsiyasi ----------

    [HttpGet("turnstile")]
    public async Task<ActionResult<TurnstileSettingsDto>> GetTurnstile()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        var teachers = (await db.Teachers.Where(t => !t.IsArchived).OrderBy(t => t.FullName).ToListAsync())
            .Select(t => new TeacherDeviceMapDto(t.Id, t.FullName, t.DeviceUserId)).ToList();
        // Qurilma login/paroli — .env dan (TURNSTILE_USERNAME / TURNSTILE_PASSWORD).
        return new TurnstileSettingsDto(
            m?.TurnstileEnabled ?? false,
            string.IsNullOrEmpty(m?.TurnstileVendor) ? "hikvision" : m!.TurnstileVendor,
            m?.TurnstileHost ?? "", m?.TurnstilePort ?? 80, AppSecrets.TurnstileUsername,
            AppSecrets.TurnstilePassword.Length > 0,
            string.IsNullOrEmpty(m?.WorkStartTime) ? "08:30" : m!.WorkStartTime,
            m?.LateGraceMinutes ?? 10, m?.TurnstileLastSync ?? "", teachers,
            new EnvSecretDto(AppSecrets.EnvKeys.TurnstilePassword, AppSecrets.TurnstileCredentialsConfigured));
    }

    [HttpPut("turnstile")]
    [AdminPerm("settings.turnstile")]
    public async Task<ActionResult<TurnstileSettingsDto>> SaveTurnstile(SaveTurnstileSettingsRequest req)
    {
        if (EnvOnly(req.Username, AppSecrets.EnvKeys.TurnstileUsername, AppSecrets.TurnstileUsername) is { } userErr) return userErr;
        if (EnvOnly(req.Password, AppSecrets.EnvKeys.TurnstilePassword) is { } passErr) return passErr;

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        m.TurnstileEnabled = req.Enabled;
        m.TurnstileVendor = (req.Vendor ?? m.TurnstileVendor).Trim().ToLowerInvariant();
        m.TurnstileHost = (req.Host ?? "").Trim();
        m.TurnstilePort = req.Port is > 0 ? req.Port.Value : 80;
        if (!string.IsNullOrEmpty(req.WorkStartTime)) m.WorkStartTime = req.WorkStartTime.Trim();
        if (req.LateGraceMinutes is >= 0) m.LateGraceMinutes = req.LateGraceMinutes.Value;

        // O'qituvchi ↔ qurilma ID moslamasi.
        if (req.Teachers is not null)
        {
            var byId = await db.Teachers.ToDictionaryAsync(t => t.Id);
            var changedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in req.Teachers)
                if (byId.TryGetValue(map.TeacherId, out var te))
                {
                    var next = (map.DeviceUserId ?? "").Trim();
                    if (next.Length > 0 && next != te.DeviceUserId) changedIds.Add(next);
                    te.DeviceUserId = next;
                }

            // ⚠️ YANGI berilgan ID band bo'lmasin — boshqa o'qituvchida ham, o'quvchida ham (AUTH-7):
            // aks holda bir odamning o'tishi ikkinchisining davomatiga (maosh, kechikish) yozilardi.
            // Faqat O'ZGARGAN ID'lar tekshiriladi — eski (allaqachon bor) to'qnashuv boshqa
            // sozlamani (ish vaqti, manzil) saqlashni to'smasin. Rad etilsa hech narsa saqlanmaydi.
            if (changedIds.Count > 0)
            {
                var owners = byId.Values
                    .Where(t => !t.IsArchived && changedIds.Contains(t.DeviceUserId))
                    .Select(t => new TurnstileService.DeviceOwner("teacher", t.Id, t.FullName, t.DeviceUserId))
                    .Concat(await TurnstileService.DeviceOwnersAsync(db, changedIds, teachers: false))
                    .ToList();
                if (TurnstileService.DeviceConflict(owners) is { } conflict)
                    return BadRequest(new { message = conflict });
            }
        }
        LogSettings("Turniket integratsiyasi");
        await db.SaveChangesAsync();
        return await GetTurnstile();
    }

    // ---------- Kamera (videokuzatuv) integratsiyasi ----------

    [HttpGet("cameras")]
    public async Task<ActionResult<CameraSettingsDto>> GetCameras()
    {
        var m = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync();
        var cams = await db.Cameras.AsNoTracking().ToListAsync();
        var recordOn = m?.CameraRecordEnabled ?? false;
        var nvrOn = NvrArchiveService.IsConfigured(m);
        return new CameraSettingsDto(
            m?.CameraEnabled ?? false,
            cams.Count,
            recordOn,
            cams.Count(c => CameraRules.ShouldRecordWithNvr(nvrOn, recordOn, c)),
            m?.NvrEnabled ?? false,
            m?.NvrHost ?? "",
            m?.NvrRtspPort is > 0 ? m.NvrRtspPort : 554,
            m?.NvrIsapiPort is > 0 ? m.NvrIsapiPort : 80,
            string.IsNullOrEmpty(m?.NvrVendor) ? "hikvision" : m.NvrVendor,
            // ⚠️ Parolning O'ZI hech qachon qaytmaydi — faqat "berilganmi" bayrog'i.
            AppSecrets.NvrCredentialsConfigured,
            cams.Count(c => nvrOn && c.NvrChannel > 0));
    }

    /// <summary>
    /// Kamera integratsiyasi + 24/7 YOZUV bosh kaliti.
    ///
    /// <para>⚠️ Yozuv kaliti o'zgarganda BARCHA kameralar shlyuzga QAYTA yuboriladi. Aks holda
    /// sozlama bazada o'zgarib, shlyuz esa eski holatda qolardi — ya'ni "yozuvni o'chirdim,
    /// lekin disk baribir to'lyapti" holati. Yangi qiymat har kamera keyingi marta tahrirlanganda
    /// yoki jonli ochilganda emas, AYNAN SHU YERDA qo'llanadi.</para>
    /// </summary>
    /// <summary>NVR manzili/portlarini superadmin bo'lmagan foydalanuvchi o'zgartirmoqchi bo'lsa.</summary>
    public const string NvrTargetSuperAdminMessage =
        "NVR manzili va portlarini faqat superadmin o'zgartira oladi (ular .env dagi NVR paroli qayerga yuborilishini belgilaydi).";

    [HttpPut("cameras")]
    [AdminPerm("settings.cameras")]
    public async Task<ActionResult<CameraSettingsDto>> SaveCameras(SaveCameraSettingsRequest req)
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        // ⚠️ NVR MANZILI (host + RTSP/ISAPI portlari) — FAQAT SUPERADMIN (hard rol, `[AdminPerm]` emas:
        // u `admin` ni ham o'tkazardi). Shu uchta maydon `.env` dagi NVR login/paroli QAYERGA yuborilishini
        // hal qiladi: ilgari `settings.cameras` ruxsati bor har kim hostni o'z serveriga almashtirib, klip
        // so'rovida (RTSP, ffmpeg — Basic'ga ham javob beradi) parolni qo'lga kiritardi (cameras.md §8).
        // Qolgan sozlamalar (kalitlar, rusum) avvalgidek; manzil o'zgarmasa so'rov odatdagidek o'tadi.
        var newHost = (req.NvrHost ?? m?.NvrHost ?? "").Trim();
        if (!User.IsInRole(Roles.SuperAdmin)
            && NvrArchiveService.TargetChanged(m, newHost, req.NvrRtspPort, req.NvrIsapiPort))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = NvrTargetSuperAdminMessage });
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        var wasNvrOn = NvrArchiveService.IsConfigured(m);
        var recordChanged = m.CameraRecordEnabled != req.RecordEnabled;
        m.CameraEnabled = req.Enabled;
        m.CameraRecordEnabled = req.RecordEnabled;
        m.NvrEnabled = req.NvrEnabled;
        m.NvrHost = newHost;
        m.NvrRtspPort = NvrArchiveService.EffectiveRtspPort(req.NvrRtspPort);
        m.NvrIsapiPort = NvrArchiveService.EffectiveIsapiPort(req.NvrIsapiPort);
        m.NvrVendor = string.IsNullOrWhiteSpace(req.NvrVendor)
            ? (string.IsNullOrEmpty(m.NvrVendor) ? "hikvision" : m.NvrVendor)
            : req.NvrVendor.Trim().ToLowerInvariant();
        LogSettings("Kamera integratsiyasi");
        await db.SaveChangesAsync();

        // ⚠️ NVR yoqilishi ham shlyuzga ta'sir qiladi: NVR'li kamera BIZDA yozilmaydi
        // (takroriy nusxa). Shuning uchun yozuv kaliti bilan bir qatorda NVR holati ham
        // kuzatiladi — aks holda NVR ulangandan keyin disk baribir to'lib turardi.
        var nvrOn = NvrArchiveService.IsConfigured(m);
        if (recordChanged || wasNvrOn != nvrOn)
            foreach (var c in await db.Cameras.AsNoTracking().ToListAsync())
                await cameraGateway.EnsureAsync(c,
                    CameraRules.ShouldRecordWithNvr(nvrOn, req.RecordEnabled, c));

        return await GetCameras();
    }

    [HttpPost("telegram-backup/test")]
    [AdminPerm("settings.backup")]
    public async Task<ActionResult<object>> TestTelegramBackup()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        var chatIdStr = m?.TelegramAdminChatId ?? "";
        if (string.IsNullOrWhiteSpace(chatIdStr))
            return BadRequest(new { success = false, message = "Admin Chat ID kiritilmagan. Avval Chat ID saqlang." });
        if (!long.TryParse(chatIdStr.Trim(), out var chatId) || chatId == 0)
            return BadRequest(new { success = false, message = "Admin Chat ID noto'g'ri format (faqat raqam bo'lishi kerak)." });
        if (!telegram.IsConfigured)
            return BadRequest(new { success = false, message = "Telegram bot sozlanmagan. Avval bot token saqlang." });

        var sent = await telegram.SendMessageAsync(chatId,
            $"WunderkindLC Backup testi — {AppClock.Now:yyyy-MM-dd HH:mm}. Bu xabar test maqsadida yuborildi.");
        if (sent)
            return Ok(new { success = true, message = "Test xabari muvaffaqiyatli yuborildi." });
        return Ok(new { success = false, message = "Xabar yuborishda xatolik. Chat ID va bot tokenni tekshiring." });
    }

    /// <summary>Backupni HOZIR yuboradi — markaz ma'lumotlari JSON qilib Telegram orqali adminga.</summary>
    [HttpPost("telegram-backup/run")]
    [AdminPerm("settings.backup")]
    public async Task<ActionResult<object>> RunTelegramBackup()
    {
        var (ok, msg) = await BackupService.SendAsync(db, telegram);
        return Ok(new { success = ok, message = msg });
    }

    [HttpPut("absence-reasons")]
    [AdminPerm("settings.reasons")]
    public async Task<IActionResult> SaveAbsenceReasons(SaveAbsenceReasonsRequest req)
    {
        var incoming = (req.AbsenceReasons ?? new())
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r with
            {
                Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString() : r.Id.Trim(),
                Name = r.Name.Trim(),
                Short = (r.Short ?? "").Trim(),
            })
            .GroupBy(r => r.Id).Select(g => g.First())
            .ToList();

        // ⚠️ Jurnalda ISHLATILGAN sababni olib tashlash va uning «kech keldi» belgisini almashtirish RAD
        // ETILADI (sanoq bilan) — ilgari butun ro'yxat almashtirilar, jurnal yozuvlari yetim qolar va
        // o'tgan kechikishlar yo'qlikka aylanib ketardi (AbsenceReasonRules izohi).
        if (await AbsenceReasonRules.ValidateReplaceAsync(db, incoming) is { } error)
            return BadRequest(new { message = error });
        // «Kech keldi» + «Sababli» bir vaqtda — rad (SPEC P3.1). «Sababli» bayrog'i ishlatilgan sababda ham
        // o'zgaradi (faqat analitika), shuning uchun ValidateReplaceAsync uni tekshirmaydi.
        foreach (var r in incoming)
            if (AbsenceReasonRules.ExcusedError(r.IsLate, r.IsExcused) is { } excusedError)
                return BadRequest(new { message = $"«{r.Name}»: {excusedError}" });

        // Joyida yangilash (upsert) — id'lar saqlanadi (jurnal yozuvlari reasonId orqali bog'langan).
        var existing = await db.AbsenceReasons.ToListAsync();
        var byId = existing.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var keep = incoming.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var changes = new List<string>();
        foreach (var r in existing.Where(r => !keep.Contains(r.Id)))
        {
            changes.Add($"o'chirildi «{r.Name}»");
            db.AbsenceReasons.Remove(r);
        }
        foreach (var r in incoming)
        {
            if (byId.TryGetValue(r.Id, out var row))
            {
                if (row.Name != r.Name) changes.Add($"«{row.Name}» → «{r.Name}»");
                if (row.Short != r.Short) changes.Add($"«{r.Name}» qisqa nomi: {row.Short} → {r.Short}");
                if (row.IsLate != r.IsLate) changes.Add($"«{r.Name}»: kech keldi {(r.IsLate ? "yoqildi" : "o'chirildi")}");
                if (row.IsExcused != r.IsExcused) changes.Add($"«{r.Name}»: sababli (uzrli) {(r.IsExcused ? "yoqildi" : "o'chirildi")}");
                row.Name = r.Name;
                row.Short = r.Short;
                row.IsLate = r.IsLate;
                row.IsExcused = r.IsExcused;
            }
            else
            {
                changes.Add($"qo'shildi «{r.Name}»" + (r.IsLate ? " (kech keldi)" : r.IsExcused ? " (sababli)" : ""));
                db.AbsenceReasons.Add(new AbsenceReason
                {
                    Id = r.Id, Name = r.Name, Short = r.Short, IsLate = r.IsLate, IsExcused = r.IsExcused,
                });
            }
        }
        audit.Record("AbsenceReason", "absence-reasons", "update",
            "Davomat sabablari o'zgartirildi" + (changes.Count > 0 ? ": " + string.Join("; ", changes) : " (o'zgarish yo'q)"));
        await db.SaveChangesAsync();
        return NoContent();
    }
}
