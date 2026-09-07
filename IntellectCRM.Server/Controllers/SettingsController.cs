using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Infrastructure.Data;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;
using IntellectCRM.Application.Services;

namespace IntellectCRM.Server.Controllers;

[ApiController]
[Authorize]
// ⚠️ SINF darajasidagi kalit — O'QISH uchun (GET xodimga ochiq, `HasSection` sahifa
// ruxsatini ham hisobga oladi). YOZISH esa har bir SAHIFA kaliti bilan alohida
// darvozalangan (metod darajasidagi `[AdminPerm]` sinfdagisini bekor qiladi), chunki bu
// controller o'nga yaqin MUSTAQIL sozlama sahifasiga xizmat qiladi: "Zaxira nusxa" berilgan
// xodim "Markaz ma'lumotlari"ni o'zgartira olmasin.
[AdminPerm("settings")]
[Route("api/admin/settings")]
public class SettingsController(AppDbContext db, TelegramService telegram, IWebHostEnvironment env, IConfiguration config, EskizService eskiz, AuditService audit, CameraGateway cameraGateway) : ControllerBase
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
    /// </summary>
    private void LogSettings(string what)
    {
        var changed = db.ChangeTracker.Entries<CenterMeta>()
            .SelectMany(e => e.Properties.Where(p => e.State == EntityState.Added || p.IsModified))
            .Select(p => p.Metadata.Name)
            .Distinct().Order().ToList();
        var detail = changed.Count > 0 ? $" ({string.Join(", ", changed)})" : "";
        audit.Record("CenterMeta", "center", "update", $"Sozlama o'zgartirildi — {what}{detail}");
    }

    [HttpGet]
    public async Task<ActionResult<SchoolSettingsDto>> Get()
    {
        var reasons = await db.AbsenceReasons
            .Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate)).ToListAsync();
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

    // ---------- Push (Firebase / FCM) ----------
    // Ikki qatlam: (1) ServiceAccountJson — server FCM'ga push YUBORISHI uchun (maxfiy);
    // (2) WebConfigJson + VapidKey — brauzer/PWA token OLISHI uchun (ommaviy). Native (Flutter)
    // ilova tokenni o'zi oladi; web/PWA esa quyidagi web config bilan Firebase JS SDK orqali oladi.

    [HttpGet("firebase")]
    public async Task<ActionResult<FirebaseSettingsDto>> GetFirebase()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        var web = m?.FcmWebConfigJson ?? "";
        var vapid = m?.FcmVapidKey ?? "";
        // Service account (maxfiy) — .env dan; qiymatning O'ZI hech qachon qaytmaydi.
        var saConfigured = FcmService.IsConfigured(AppSecrets.FcmServiceAccountJson);
        return new FirebaseSettingsDto(
            saConfigured, web, vapid, WebPushConfigured(web, vapid),
            new EnvSecretDto(AppSecrets.EnvKeys.FcmServiceAccountJson, saConfigured));
    }

    [HttpPut("firebase")]
    [AdminPerm("settings.channels")]
    public async Task<ActionResult<FirebaseSettingsDto>> SaveFirebase(SaveFirebaseSettingsRequest req)
    {
        if (EnvOnly(req.ServiceAccountJson, AppSecrets.EnvKeys.FcmServiceAccountJson) is { } err) return err;

        var web = (req.WebConfigJson ?? "").Trim();
        if (web.Length > 0 && !IsValidJsonObject(web))
            return BadRequest(new { message = "Web app config noto'g'ri — Firebase Console'dagi JSON obyektni to'liq qo'ying." });
        var vapid = (req.VapidKey ?? "").Trim();

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        m.FcmWebConfigJson = web;
        m.FcmVapidKey = vapid;
        LogSettings("Push (Firebase)");
        await db.SaveChangesAsync();
        return await GetFirebase();
    }

    /// <summary>Web/PWA push tayyor — web config JSON obyekti va VAPID kalit ham kiritilgan.</summary>
    private static bool WebPushConfigured(string webConfigJson, string vapidKey) =>
        IsValidJsonObject(webConfigJson) && vapidKey.Trim().Length > 0;

    private static bool IsValidJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch { return false; }
    }

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

    // ---------- SMS (Eskiz.uz) ----------

    // Login/parol — faqat .env (ESKIZ_EMAIL / ESKIZ_PASSWORD). UI'dan faqat jo'natuvchi nomi (From).

    [HttpGet("eskiz")]
    public async Task<ActionResult<EskizSettingsDto>> GetEskiz()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        // Balans — sozlangan bo'lsa best-effort (tarmoq xatosi UI'ni buzmaydi).
        decimal? balance = eskiz.IsConfigured() ? await eskiz.GetBalanceAsync(db) : null;
        // ⚠️ Tasdiqlangan nomlar ro'yxati — ilgari "Jo'natuvchi nomi" ERKIN MATN edi va
        // tasdiqlangani bor-yo'qligini bilishning yo'li yo'q edi (prodda namuna matn turgan).
        var nicknames = eskiz.IsConfigured() ? await eskiz.GetNicknamesAsync(db) : [];
        var from = eskiz.SenderOf(m);
        return new EskizSettingsDto(
            eskiz.DisplayEmail(), from, eskiz.IsConfigured(), balance,
            new EnvSecretDto(AppSecrets.EnvKeys.EskizEmail, AppSecrets.EskizEmail.Length > 0),
            new EnvSecretDto(AppSecrets.EnvKeys.EskizPassword, AppSecrets.EskizPassword.Length > 0),
            nicknames,
            // "4546" — Eskiz'ning umumiy raqami: tasdiq talab qilmaydi, har doim ishlaydi.
            from == "4546" || nicknames.Contains(from, StringComparer.OrdinalIgnoreCase));
    }

    [HttpPut("eskiz")]
    [AdminPerm("settings.channels")]
    public async Task<ActionResult<EskizSettingsDto>> SaveEskiz(SaveEskizRequest req)
    {
        if (EnvOnly(req.Email, AppSecrets.EnvKeys.EskizEmail, AppSecrets.EskizEmail) is { } emailErr) return emailErr;
        if (EnvOnly(req.Password, AppSecrets.EnvKeys.EskizPassword) is { } passErr) return passErr;

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        if (req.From is not null)
        {
            // ⚠️ Namuna matnni ("tasdiqlangan_nikname") saqlashga YO'L QO'YMAYMIZ: u Eskiz'da
            // tasdiqlanmagan va prodda aynan shu qiymat yozib qo'yilgani topilgan — SMS "4546"
            // dan ketardi, markaz nomi ko'rinmasdi va buni hech narsa ko'rsatmasdi.
            if (EskizService.IsPlaceholderSender(req.From))
                return BadRequest(new
                {
                    message = "Bu — namuna matn, jo'natuvchi nomi emas. Eskiz kabinetida "
                            + "TASDIQLANGAN nomni tanlang yoki bo'sh qoldiring (u holda \"4546\" ishlatiladi).",
                });
            m.EskizFrom = string.IsNullOrWhiteSpace(req.From) ? "4546" : req.From.Trim();
        }
        LogSettings("SMS (Eskiz)");
        await db.SaveChangesAsync();
        return await GetEskiz();
    }

    // ---------- Local SMS (CTI agent telefonining SIM-kartasidan) ----------

    [HttpGet("local-sms")]
    public async Task<ActionResult<LocalSmsSettingsDto>> GetLocalSms()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return new LocalSmsSettingsDto(m?.LocalSmsEnabled ?? false, m?.LocalSmsDefaultAgentId, m?.LocalSmsDelaySeconds ?? 0);
    }

    [HttpPut("local-sms")]
    [AdminPerm("settings.channels")]
    public async Task<ActionResult<LocalSmsSettingsDto>> SaveLocalSms(SaveLocalSmsRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.DefaultAgentId)
            && await db.CtiAgents.FindAsync(req.DefaultAgentId) is null)
            return BadRequest(new { message = "Tanlangan agent topilmadi" });
        if (req.DelaySeconds < 0 || req.DelaySeconds > 300)
            return BadRequest(new { message = "Kutish vaqti 0-300 soniya oralig'ida bo'lishi kerak" });

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        m.LocalSmsEnabled = req.Enabled;
        m.LocalSmsDefaultAgentId = string.IsNullOrWhiteSpace(req.DefaultAgentId) ? null : req.DefaultAgentId;
        m.LocalSmsDelaySeconds = req.DelaySeconds;
        LogSettings("Local SMS");
        await db.SaveChangesAsync();
        return new LocalSmsSettingsDto(m.LocalSmsEnabled, m.LocalSmsDefaultAgentId, m.LocalSmsDelaySeconds);
    }

    // ---------- Ilova (APK) — Telegram bot ro'yxatdan o'tganga yuboradi ----------

    private const long MaxApkBytes = 50_000_000; // Telegram bot sendDocument chegarasi ~50 MB

    [HttpGet("app-apk")]
    public async Task<ActionResult<AppApkSettingsDto>> GetAppApk()
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        return AppApkDto(m);
    }

    [HttpPost("app-apk/{role}")]
    [AdminPerm("settings.apk")]
    [RequestSizeLimit(MaxApkBytes + 2_000_000)]
    public async Task<ActionResult<AppApkSettingsDto>> UploadAppApk(string role, IFormFile file)
    {
        if (role is not ("student" or "teacher"))
            return BadRequest(new { message = "role 'student' yoki 'teacher' bo'lishi kerak" });
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > MaxApkBytes)
            return BadRequest(new { message = "APK 50 MB dan katta — Telegram bot orqali yuborib bo'lmaydi." });
        if (!file.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .apk fayl qabul qilinadi" });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = $"app-{role}-{Guid.NewGuid():N}.apk";
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        var relPath = $"uploads/{stored}";
        var name = System.IO.Path.GetFileName(file.FileName);
        if (role == "student")
        {
            DeleteApk(m.StudentApkPath);
            m.StudentApkName = name; m.StudentApkPath = relPath; m.StudentApkFileId = ""; // kesh bo'shatiladi
        }
        else
        {
            DeleteApk(m.TeacherApkPath);
            m.TeacherApkName = name; m.TeacherApkPath = relPath; m.TeacherApkFileId = "";
        }
        LogSettings("Mobil ilova (APK) yuklandi");
        await db.SaveChangesAsync();
        return AppApkDto(m);
    }

    [HttpDelete("app-apk/{role}")]
    [AdminPerm("settings.apk")]
    public async Task<ActionResult<AppApkSettingsDto>> DeleteAppApk(string role)
    {
        if (role is not ("student" or "teacher"))
            return BadRequest(new { message = "role 'student' yoki 'teacher' bo'lishi kerak" });
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) return AppApkDto(null);
        if (role == "student")
        {
            DeleteApk(m.StudentApkPath);
            m.StudentApkName = ""; m.StudentApkPath = ""; m.StudentApkFileId = "";
        }
        else
        {
            DeleteApk(m.TeacherApkPath);
            m.TeacherApkName = ""; m.TeacherApkPath = ""; m.TeacherApkFileId = "";
        }
        LogSettings("Mobil ilova (APK) o'chirildi");
        await db.SaveChangesAsync();
        return AppApkDto(m);
    }

    private void DeleteApk(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return;
        try
        {
            var abs = System.IO.Path.Combine(env.ContentRootPath, relPath);
            if (System.IO.File.Exists(abs)) System.IO.File.Delete(abs);
        }
        catch { /* eski faylni o'chirib bo'lmasa — e'tiborsiz */ }
    }

    private AppApkSettingsDto AppApkDto(CenterMeta? m)
    {
        long Size(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath)) return 0;
            var abs = System.IO.Path.Combine(env.ContentRootPath, relPath);
            return System.IO.File.Exists(abs) ? new System.IO.FileInfo(abs).Length : 0;
        }
        return new AppApkSettingsDto(
            m?.StudentApkName ?? "", Size(m?.StudentApkPath ?? ""),
            m?.TeacherApkName ?? "", Size(m?.TeacherApkPath ?? ""));
    }

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
            foreach (var map in req.Teachers)
                if (byId.TryGetValue(map.TeacherId, out var te))
                    te.DeviceUserId = (map.DeviceUserId ?? "").Trim();
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
    [HttpPut("cameras")]
    [AdminPerm("settings.cameras")]
    public async Task<ActionResult<CameraSettingsDto>> SaveCameras(SaveCameraSettingsRequest req)
    {
        var m = await db.CenterMeta.FirstOrDefaultAsync();
        if (m is null) { m = new CenterMeta(); db.CenterMeta.Add(m); }
        var wasNvrOn = NvrArchiveService.IsConfigured(m);
        var recordChanged = m.CameraRecordEnabled != req.RecordEnabled;
        m.CameraEnabled = req.Enabled;
        m.CameraRecordEnabled = req.RecordEnabled;
        m.NvrEnabled = req.NvrEnabled;
        m.NvrHost = (req.NvrHost ?? m.NvrHost).Trim();
        m.NvrRtspPort = req.NvrRtspPort is > 0 and <= 65535 ? req.NvrRtspPort : 554;
        m.NvrIsapiPort = req.NvrIsapiPort is > 0 and <= 65535 ? req.NvrIsapiPort : 80;
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
            $"IntellectCRM Backup testi — {AppClock.Now:yyyy-MM-dd HH:mm}. Bu xabar test maqsadida yuborildi.");
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
        // Mavjud id'larni saqlab qolamiz (jurnal yozuvlari reasonId orqali bog'langan).
        db.AbsenceReasons.RemoveRange(db.AbsenceReasons);
        db.AbsenceReasons.AddRange(req.AbsenceReasons.Select(r => new AbsenceReason
        {
            Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString() : r.Id,
            Name = r.Name,
            Short = r.Short,
            IsLate = r.IsLate,
        }));
        LogSettings("Davomat sabablari");
        await db.SaveChangesAsync();
        return NoContent();
    }
}
