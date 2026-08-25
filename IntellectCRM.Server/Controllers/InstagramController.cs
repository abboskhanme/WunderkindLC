using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// MARKETING → INSTAGRAM AI AGENTI — admin API'si. Bo'lim oltita ekrandan iborat va hammasi
/// shu controllerdan oziqlanadi: <b>Boshqaruv paneli</b> (holat/diagnostika), <b>Inbox</b>
/// (suhbatlar + operator javobi), <b>Javob qoidalari</b>, <b>Bilim bazasi</b>, <b>Analitika</b>
/// va <b>Sozlamalar</b> (akkauntni ulash).
///
/// <para><b>Ruxsat: <c>marketing</c></b> (yangi kalit yasalmadi — u `adminPermissions` da
/// allaqachon bor). <c>ReadRequiresPerm = true</c> ATAYIN: javobda begona odamlarning shaxsiy
/// xabarlari, ismi va (matndan ajratilgan) telefon raqamlari qaytadi — bunday o'qishni odatdagidek
/// "har qanday xodimga ochiq" qoldirib bo'lmaydi (`uploads-security.md` dagi bir xil mantiq).</para>
///
/// <para><b>⚠️ MAXFIYLIK CHEGARASI:</b> ulangan akkauntning <c>AccessToken</c>i, App Secret va
/// Verify Token HECH QAYSI javobga tushmaydi. Tashqariga faqat <b>holat</b> chiqadi:
/// "ulangan / muddati N kun qoldi", "kalit sozlangan / sozlanmagan". Audit yozuvlarida ham
/// maxfiy qiymat yo'q.</para>
///
/// <para>Bu yerda faqat HTTP tarjimasi va CRUD bor; Instagram bilan gaplashish, AI va navbat
/// mantiqi <c>Application/Services/Instagram*.cs</c> da (yagona manba — bot/fon xizmati va
/// controller bir xil qoidaga bo'ysunsin).</para>
/// </summary>
[ApiController]
[Authorize]
// ⚠️ SINF darajasi — O'QISH uchun (`ReadRequiresPerm`: javobda mijozlar bilan yozishmalar bor,
// bo'limlararo o'qishga ochib bo'lmaydi). YOZISH esa SAHIFA kaliti bilan alohida: "Inbox"
// berilgan xodim "Javob qoidalari"ni yoki ulanish sozlamalarini o'zgartira olmasin.
[AdminPerm("marketing", ReadRequiresPerm = true)]
[Route("api/admin/instagram")]
public partial class InstagramController(
    AppDbContext db,
    InstagramApi api,
    // FAQ tugmalarini (ice breakers) Meta'ga sinxronlash — YAGONA manba (§22): FAQ CRUD,
    // modul yoqilishi va akkaunt ulash (connect-token) shu servisdan o'tadi.
    InstagramFaqSync faqSync,
    MetaAdsApi adsApi,
    AuditService audit,
    IConfiguration config,
    // Lid kartasini guruhda JOYIDA yangilash uchun (`LeadNotifier.SyncCardAsync`) — bot
    // sozlanmagan bo'lsa chaqiruv o'zi darhol qaytadi, ya'ni qo'shimcha shart kerak emas.
    TelegramService telegram,
    ILogger<InstagramController> logger) : ControllerBase
{
    /// <summary>Audit yozuvlaridagi yagona `EntityType` (`AuditSections`: `marketing`).</summary>
    private const string AuditEntity = "Instagram";

    /// <summary>Suhbat detalida ko'rsatiladigan xabarlar chegarasi.</summary>
    private const int MessageLimit = 200;

    /// <summary>Navbat diagnostikasi (`GET /events`) chegarasi.</summary>
    private const int EventLimit = 200;

    /// <summary>OAuth `state` amal qilish muddati (daqiqa).</summary>
    private const int StateMinutes = 15;

    /// <summary>Analitika bir so'rovda ko'pi bilan shuncha xabar ustida hisoblanadi.</summary>
    private const int AnalyticsScanLimit = 20000;

    private string Actor =>
        User.Identity?.Name
        ?? User.FindFirst(ClaimTypes.Name)?.Value
        ?? "Admin";

    // =============================================================================================
    //  HOLAT VA SOZLAMALAR
    // =============================================================================================

    /// <summary>
    /// DIAGNOSTIKA EKRANI — "nima ishlayapti, nima yo'q". Sozlash 6-7 qadamdan iborat (Meta App,
    /// `.env`, webhook, OAuth, bilim bazasi) va ularning ISTALGAN biri tushib qolsa alomat bir xil:
    /// "bot javob bermayapti". Shuning uchun har qadam alohida bayroq bilan qaytadi.
    /// <para>⚠️ Kalitlarning QIYMATI emas, faqat "sozlangan/sozlanmagan" holati beriladi.</para>
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<IgStatusDto>> Status(CancellationToken ct) => await BuildStatusAsync(ct);

    /// <summary>Diagnostika holatini yig'adi. `GET /status` ham, `POST /refresh-token` ham AYNAN
    /// shuni qaytaradi — Sozlamalar sahifasi tokenni yangilagach holatni qayta so'ramasdan,
    /// javobning o'zidan yangilaydi.</summary>
    private async Task<IgStatusDto> BuildStatusAsync(CancellationToken ct)
    {
        var acc = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        return new IgStatusDto(
            Connected: acc is not null,
            Username: acc?.Username ?? "",
            Name: acc?.Name ?? "",
            PictureUrl: acc?.ProfilePictureUrl ?? "",
            TokenDaysLeft: DaysLeft(acc?.TokenExpiresAt),
            WebhookSubscribed: acc?.WebhookSubscribed ?? false,
            ConnectedAt: acc?.ConnectedAt ?? "",
            ConnectedBy: acc?.ConnectedBy ?? "",
            TokenRefreshedAt: acc?.TokenRefreshedAt ?? "",
            Enabled: meta?.InstagramEnabled ?? false,
            AutoReplyComments: meta?.InstagramAutoReplyComments ?? false,
            AutoReplyDm: meta?.InstagramAutoReplyDm ?? false,
            AppIdSet: !string.IsNullOrWhiteSpace(meta?.InstagramAppId),
            AppSecretSet: AppSecrets.InstagramAppSecret.Length > 0,
            VerifyTokenSet: AppSecrets.InstagramVerifyToken.Length > 0,
            GeminiConfigured: AppSecrets.GeminiConfigured,
            KnowledgeCount: await db.IgKnowledges.CountAsync(k => k.IsActive, ct),
            RuleCount: await db.IgAutoRules.CountAsync(r => r.IsActive, ct),
            PendingEvents: await db.IgWebhookEvents.CountAsync(e => e.Status == IgConst.EvPending, ct),
            FailedEvents: await db.IgWebhookEvents.CountAsync(e => e.Status == IgConst.EvFailed, ct),
            NeedsOperator: await db.IgConversations.CountAsync(c => c.NeedsOperator && c.Status != IgConst.StatusClosed, ct),
            Unread: await db.IgConversations.CountAsync(c => c.Unread && c.Status != IgConst.StatusClosed, ct),
            TodayReplies: await db.IgMessages.CountAsync(
                m => m.Direction == "out" && m.CreatedAt.Substring(0, 10) == today, ct),
            DailyLimit: meta?.InstagramDailyReplyLimit ?? 0,
            WebhookUrl: InstagramWebhookController.WebhookUrl(Request),
            CallbackUrl: InstagramWebhookController.CallbackUrl(Request),
            EnvKeyAppSecret: AppSecrets.EnvKeys.InstagramAppSecret,
            EnvKeyVerifyToken: AppSecrets.EnvKeys.InstagramVerifyToken);
    }

    /// <summary>Tokenga necha kun qolgani. Sana buzuq/bo'sh bo'lsa 0 (UI "noma'lum" ko'rsatadi).</summary>
    private static int DaysLeft(string? expiresAtIso)
    {
        if (string.IsNullOrWhiteSpace(expiresAtIso)) return 0;
        if (!DateTime.TryParse(expiresAtIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)) return 0;
        var days = (exp - AppClock.Now).TotalDays;
        return days <= 0 ? 0 : (int)Math.Floor(days);
    }

    /// <summary>Modul sozlamalari (`CenterMeta`). Maxfiy emas — App ID OAuth havolasida
    /// baribir ochiq ko'rinadi; token/secret bu javobda YO'Q.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<IgSettingsDto>> GetSettings(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct) ?? new CenterMeta();
        return ToSettings(meta);
    }

    [HttpPut("settings")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgSettingsDto>> SaveSettings(IgSettingsDto payload, CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }

        var wasEnabled = meta.InstagramEnabled;

        meta.InstagramEnabled = payload.InstagramEnabled;
        meta.InstagramAutoReplyComments = payload.InstagramAutoReplyComments;
        meta.InstagramAutoReplyDm = payload.InstagramAutoReplyDm;
        meta.InstagramPrivateReplyEnabled = payload.InstagramPrivateReplyEnabled;
        meta.InstagramAppId = (payload.InstagramAppId ?? "").Trim();
        meta.InstagramAiModel = (payload.InstagramAiModel ?? "").Trim();
        meta.InstagramLeadSource = string.IsNullOrWhiteSpace(payload.InstagramLeadSource)
            ? "Instagram"
            : payload.InstagramLeadSource.Trim();
        meta.InstagramNotifyTelegram = payload.InstagramNotifyTelegram;
        meta.InstagramReplyDelaySeconds = Math.Clamp(payload.InstagramReplyDelaySeconds, 0, 300);
        meta.InstagramDailyReplyLimit = Math.Clamp(payload.InstagramDailyReplyLimit, 0, 100000);
        meta.InstagramGreeting = (payload.InstagramGreeting ?? "").Trim();
        meta.InstagramLeadAdsEnabled = payload.InstagramLeadAdsEnabled;
        meta.InstagramAdsLeadSource = string.IsNullOrWhiteSpace(payload.InstagramAdsLeadSource)
            ? MetaLeadBridge.DefaultSource
            : payload.InstagramAdsLeadSource.Trim();
        meta.InstagramAdsStatsEnabled = payload.InstagramAdsStatsEnabled;
        meta.InstagramPublishEnabled = payload.InstagramPublishEnabled;

        // ⚠️ Auditga faqat "nima yoqildi/o'chirildi" yoziladi — App ID ham, matnlar ham emas.
        var summary = "Instagram agenti sozlamalari o'zgartirildi — modul: "
                      + (meta.InstagramEnabled ? "YOQILGAN" : "O'CHIRILGAN")
                      + ", izohlarga avto-javob: " + (meta.InstagramAutoReplyComments ? "ha" : "yo'q")
                      + ", DM'ga avto-javob: " + (meta.InstagramAutoReplyDm ? "ha" : "yo'q")
                      + ", kunlik chegara: " + meta.InstagramDailyReplyLimit
                      + ", reklama lidlari: " + (meta.InstagramLeadAdsEnabled ? "YOQILGAN" : "O'CHIRILGAN")
                      + ", reklama statistikasi: " + (meta.InstagramAdsStatsEnabled ? "YOQILGAN" : "O'CHIRILGAN")
                      + ", kontent joylash: " + (meta.InstagramPublishEnabled ? "YOQILGAN" : "O'CHIRILGAN");
        audit.Record(AuditEntity, "settings", "update", summary);
        await db.SaveChangesAsync(ct);

        if (wasEnabled != meta.InstagramEnabled)
            logger.LogInformation("[instagram] modul {State}", meta.InstagramEnabled ? "yoqildi" : "o'chirildi");

        // 🔴 Modul O'CHIQDAN → YOQILGANga o'tganda FAQ tugmalarini Meta'ga QAYTA yuboramiz:
        // tugmalar modul o'chiq paytida qo'shilgan bo'lsa bazada bor, Meta'da esa yo'q edi —
        // ular «Qayta yuborish» qo'lda bosilmaguncha ko'rinmasdi (§22). Sinxron o'zi §3
        // darvozasidan o'tadi (endi modul yoqilgan, ya'ni so'rov ketadi). BEST-EFFORT: yiqilsa
        // sozlama saqlash BUZILMAYDI — sabab IgAccount.FaqSyncError da qoladi. ToSettings javobi
        // o'zgarmaydi (sinxron holati alohida `GET /faq` da ko'rinadi).
        if (!wasEnabled && meta.InstagramEnabled)
        {
            try
            {
                var faq = await faqSync.SyncAsync(ct);
                if (!faq.Ok)
                    logger.LogWarning("[instagram] modul yoqilganda FAQ sinxroni: {Msg}", faq.Message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[instagram] modul yoqilganda FAQ sinxronida kutilmagan xato");
            }
        }

        return ToSettings(meta);
    }

    private static IgSettingsDto ToSettings(CenterMeta m) => new(
        m.InstagramEnabled, m.InstagramAutoReplyComments, m.InstagramAutoReplyDm,
        m.InstagramPrivateReplyEnabled, m.InstagramAppId, m.InstagramAiModel,
        m.InstagramLeadSource, m.InstagramNotifyTelegram, m.InstagramReplyDelaySeconds,
        m.InstagramDailyReplyLimit, m.InstagramGreeting,
        m.InstagramLeadAdsEnabled, m.InstagramAdsLeadSource,
        m.InstagramAdsStatsEnabled, m.InstagramPublishEnabled);

    // =============================================================================================
    //  AKKAUNTNI ULASH / UZISH
    // =============================================================================================

    /// <summary>
    /// "Instagram'ni ulash" tugmasi uchun authorize havolasi. Bir martalik <c>state</c> yaratiladi
    /// (CSRF himoyasi): callback aynan BIZ boshlagan oqimdanmi va kim boshlaganini shundan bilamiz.
    /// </summary>
    [HttpGet("connect-url")]
    public async Task<ActionResult<IgConnectUrlDto>> ConnectUrl(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var appId = (meta?.InstagramAppId ?? "").Trim();
        if (appId.Length == 0)
            return BadRequest(new { message = "Instagram App ID kiritilmagan — avval Sozlamalar bo'limida saqlang." });
        if (AppSecrets.InstagramAppSecret.Length == 0)
            return BadRequest(new
            {
                message = $"{AppSecrets.EnvKeys.InstagramAppSecret} sozlanmagan — uni serverdagi `.env` fayliga qo'shing.",
            });

        var state = new IgOAuthState
        {
            CreatedBy = Actor,
            CreatedAt = AppClock.Iso(),
            ExpiresAt = AppClock.Now.AddMinutes(StateMinutes).ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        db.IgOAuthStates.Add(state);
        await db.SaveChangesAsync(ct);

        var redirectUri = InstagramWebhookController.CallbackUrl(Request);

        // ⚠️ Kontent joylash scope'i FAQAT modul yoqilganda so'raladi. Meta ilovada YOQILMAGAN
        // scope so'ralsa butun authorize so'rovini rad etadi — ya'ni doimiy ro'yxatga qo'shilsa,
        // kontent modulini ishlatmaydigan markaz «Qayta ulash» bosganda ISHLAB TURGAN izoh/DM
        // agentini qayta ulay olmay qolardi. Batafsil: `IgConst.ContentPublishScope`.
        var scopes = IgConst.ScopesFor(meta?.InstagramPublishEnabled ?? false);
        return new IgConnectUrlDto(
            InstagramApi.BuildAuthorizeUrl(appId, redirectUri, state.Id, scopes), redirectUri);
    }

    /// <summary>Akkauntni uzish. Qator O'CHIRILMAYDI (suhbatlar tarixi va analitika saqlansin) —
    /// faqat <c>IsActive=false</c> va token TOZALANADI (uzilgan akkauntning tokeni bazada
    /// qolib ketmasin).</summary>
    [HttpPost("disconnect")]
    [AdminPerm("marketing.settings")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var accounts = await db.IgAccounts.Where(a => a.IsActive).ToListAsync(ct);
        if (accounts.Count == 0) return BadRequest(new { message = "Ulangan Instagram akkaunt yo'q." });

        foreach (var a in accounts)
        {
            a.IsActive = false;
            a.AccessToken = "";
            a.WebhookSubscribed = false;
        }

        audit.Record(AuditEntity, accounts[0].Id, "update",
            $"Instagram akkaunti uzildi (@{accounts[0].Username}) — avtomatik javoblar to'xtadi");
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Instagram akkaunti uzildi." });
    }

    /// <summary>Tokenni QO'LDA yangilash. Odatda buni fon xizmati 45-kunda o'zi qiladi; bu tugma
    /// "muddati tugayapti" ogohlantirishi chiqqanda kutmasdan tuzatish uchun.</summary>
    /// <remarks>Javob — to'liq <see cref="IgStatusDto"/> (`GET /status` bilan bir xil):
    /// sahifa yangilangan "N kun qoldi" ni darhol ko'rsatadi.</remarks>
    [HttpPost("refresh-token")]
    [AdminPerm("marketing.settings")]
    public async Task<IActionResult> RefreshToken(CancellationToken ct)
    {
        var acc = await db.IgAccounts.FirstOrDefaultAsync(a => a.IsActive, ct);
        if (acc is null || acc.AccessToken.Length == 0)
            return BadRequest(new { message = "Ulangan Instagram akkaunt yo'q — avval akkauntni ulang." });

        var (ok, token, expiresIn, err) = await api.RefreshTokenAsync(acc.AccessToken, ct);
        if (!ok) return BadRequest(new { message = err });

        acc.AccessToken = token;
        acc.TokenExpiresAt = AppClock.Now.AddSeconds(expiresIn > 0 ? expiresIn : 0).ToString("yyyy-MM-ddTHH:mm:ss");
        acc.TokenRefreshedAt = AppClock.Iso();
        audit.Record(AuditEntity, acc.Id, "update",
            $"Instagram tokeni qo'lda yangilandi (@{acc.Username}) — yangi muddat: {DaysLeft(acc.TokenExpiresAt)} kun");
        await db.SaveChangesAsync(ct);

        return Ok(await BuildStatusAsync(ct));
    }

    /// <summary>
    /// QO'LDA TOKEN BILAN ULASH — OAuth'ga <b>ZAXIRA</b> yo'l.
    ///
    /// <para><b>Asosiy yo'l baribir «Ulash» tugmasi (OAuth)</b>: u tokenni ham, uchala
    /// identifikatorni ham o'zi oladi va admin hech narsa ko'chirmaydi. Bu endpoint OAuth
    /// yurmaydigan holatlar uchun: <c>Invalid redirect_uri</c> tuzatilmayotgan bo'lsa, domen
    /// hali ochilmagan bo'lsa yoki token Meta konsolidagi «Generate token» dan olingan bo'lsa.</para>
    ///
    /// <para>⚠️ <b>Token QAYTA TEKSHIRILADI, ishonch bilan qabul qilinmaydi.</b> Ketma-ketlik
    /// callback bilan AYNAN bir xil: <c>me</c> → uzoq muddatliga aylantirish → webhook obunasi →
    /// saqlash. Sabab: qo'lda kiritilgan qiymat noto'g'ri akkauntniki, muddati o'tgan yoki
    /// qisqa muddatli bo'lishi mumkin — bularning hammasi keyinchalik "bot javob bermayapti"
    /// bo'lib ko'rinardi.</para>
    ///
    /// <para>⚠️ <b>Uchala identifikator ham saqlanadi</b> (<c>user_id</c>, app-scoped <c>id</c>,
    /// <c>username</c>) — faqat bittasiga tayanish cheksiz halqa himoyasini teshadi
    /// (<c>.claude/rules/marketing-instagram.md</c> §4). Shuning uchun ham token yolg'iz
    /// yozib qo'yiladigan maydon EMAS: u avval <c>me</c> ga yuboriladi.</para>
    /// </summary>
    [HttpPost("connect-token")]
    [AdminPerm("marketing.settings")]
    public async Task<IActionResult> ConnectWithToken(IgConnectTokenPayload payload, CancellationToken ct)
    {
        var token = (payload.AccessToken ?? "").Trim();
        if (token.Length == 0)
            return BadRequest(new { message = "Token kiritilmagan." });

        // --- 1) Token haqiqiymi va KIMNIKI (halqa himoyasi shu qiymatlarga tayanadi) ---
        var (okMe, igUserId, appScopedId, username, name, pictureUrl, errMe) = await api.MeAsync(token, ct);
        if (!okMe || string.IsNullOrWhiteSpace(igUserId))
        {
            logger.LogWarning("[instagram] qo'lda kiritilgan token rad etildi: {Err}", errMe);
            return BadRequest(new
            {
                message = "Token yaroqsiz — Instagram uni qabul qilmadi. "
                          + "Meta konsolidagi «Generate token» dan YANGISINI oling. "
                          + (errMe.Length > 0 ? $"Instagram javobi: {errMe}" : ""),
            });
        }

        // --- 2) UZOQ MUDDATLIGA aylantirish ---
        // ⚠️ Bu qadam ATAYIN: `ig_refresh_token` faqat uzoq muddatli tokenda ishlaydi, ya'ni
        // muvaffaqiyat bir vaqtning o'zida "token 60 kunlik" ekanining ISBOTI va aniq muddat
        // manbai. Yiqilsa token BARIBIR saqlanadi (u ishlayotgani `me` bilan tasdiqlangan),
        // faqat muddat "noma'lum" qoladi — fon xizmati uni keyingi yurishida o'zi yangilaydi
        // (`RefreshTokensAsync`: muddat noma'lum bo'lsa ham yangilanadi).
        var (okRef, freshToken, expiresIn, errRef) = await api.RefreshTokenAsync(token, ct);
        var saveToken = okRef && freshToken.Length > 0 ? freshToken : token;
        var expiresAt = okRef && expiresIn > 0
            ? AppClock.Now.AddSeconds(expiresIn).ToString("yyyy-MM-ddTHH:mm:ss")
            : "";
        if (!okRef)
            logger.LogWarning("[instagram] qo'lda ulash: tokenni uzaytirib bo'lmadi: {Err}", errRef);

        // --- 3) Webhook obunasi (obunasiz hodisa UMUMAN kelmaydi) ---
        var (okSub, errSub) = await api.SubscribeWebhookAsync(saveToken, ct);
        if (!okSub) logger.LogWarning("[instagram] qo'lda ulash: webhook obunasi bo'lmadi: {Err}", errSub);

        // --- 4) Saqlash: eskisi arxivga (tokeni tozalanadi), yangisi faol ---
        var now = AppClock.Iso();
        var olds = await db.IgAccounts.Where(a => a.IsActive).ToListAsync(ct);
        foreach (var o in olds)
        {
            o.IsActive = false;
            o.AccessToken = "";
        }

        var account = new IgAccount
        {
            IgUserId = igUserId,
            AppScopedUserId = appScopedId ?? "",
            Username = username ?? "",
            Name = name ?? "",
            ProfilePictureUrl = pictureUrl ?? "",
            AccessToken = saveToken,
            TokenExpiresAt = expiresAt,
            TokenRefreshedAt = okRef ? now : "",
            WebhookSubscribed = okSub,
            IsActive = true,
            ConnectedAt = now,
            ConnectedBy = Actor,
        };
        db.IgAccounts.Add(account);

        // ⚠️ Token QIYMATI auditga HECH QACHON tushmaydi (sinf izohidagi maxfiylik chegarasi).
        // Yozuvda faqat "qo'lda ulandi" fakti — OAuth yozuvidan farqi ko'rinib tursin.
        audit.Record(AuditEntity, account.Id, "create",
            $"Instagram akkaunti QO'LDA (token bilan) ulandi: @{account.Username} "
            + $"(webhook obunasi: {(okSub ? "bor" : "YO'Q")}, "
            + $"token muddati: {(expiresAt.Length > 0 ? $"{DaysLeft(expiresAt)} kun" : "noma'lum")})");
        await db.SaveChangesAsync(ct);

        // 🔴 Yangi akkaunt ulandi — modul yoqilgan bo'lsa faol FAQ tugmalarini darhol Meta'ga
        // yuboramiz (yangi ulangan akkauntda tugmalar ro'yxatga tushsin, §22). Sinxron
        // SaveChangesAsync'dan KEYIN: u faol akkauntni bazadan o'qiydi. Modul o'chiq yoki tugma
        // yo'q bo'lsa SyncAsync jimgina qaytadi. BEST-EFFORT: yiqilsa ulash BUZILMAYDI —
        // sabab IgAccount.FaqSyncError da qoladi.
        try
        {
            var faq = await faqSync.SyncAsync(ct);
            if (!faq.Ok)
                logger.LogWarning("[instagram] qo'lda ulash: FAQ sinxroni: {Msg}", faq.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[instagram] qo'lda ulash: FAQ sinxronida kutilmagan xato");
        }

        logger.LogInformation(
            "[instagram] akkaunt QO'LDA ulandi: @{Username} (obuna: {Sub}, muddat aniq: {Known})",
            username, okSub, okRef);

        return Ok(await BuildStatusAsync(ct));
    }

    // =============================================================================================
    //  SUHBATLAR (INBOX)
    // =============================================================================================

    /// <summary>
    /// Suhbatlar ro'yxati. Tartib ATAYIN shunday: avval <b>operator kerak</b> bo'lganlar, so'ng
    /// oxirgi KIRUVCHI xabar bo'yicha — operatorning savoli "kim javob kutyapti".
    /// </summary>
    /// <param name="source">
    /// <c>ads</c> — faqat REKLAMA izohidan boshlangan suhbatlar (E3 atributsiyasi topilgan).
    /// Boshqa qiymat filtrsiz qoladi (<see cref="InstagramContract.WantsAdsOnly"/>).
    /// <para>⚠️ Atributsiya TAXMINIY: bu kesim "reklamadan kelganlarning ANIQLANGAN qismi",
    /// "reklamadan kelganlarning HAMMASI" emas.</para>
    /// </param>
    [HttpGet("conversations")]
    public async Task<ActionResult<IgConversationListDto>> Conversations(
        [FromQuery] string? status, [FromQuery] bool? needsOperator, [FromQuery] string? q,
        [FromQuery] string? channel, [FromQuery] string? source,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.IgConversations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
        if (needsOperator == true) query = query.Where(c => c.NeedsOperator);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().TrimStart('@').ToLower();
            query = query.Where(c => c.Username.ToLower().Contains(needle)
                                     || c.LastMessageText.ToLower().Contains(needle));
        }
        if (!string.IsNullOrWhiteSpace(channel))
            query = query.Where(c => db.IgMessages.Any(m => m.ConversationId == c.Id && m.Channel == channel));
        // Suhbat darajasidagi `AdId` — birinchi atributsiyalangan xabarda TO'LDIRILADI
        // (`InstagramPipeline`), ya'ni xabarlar jadvaliga kirish shart emas.
        if (InstagramContract.WantsAdsOnly(source))
            query = query.Where(c => c.AdId != "");

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.NeedsOperator)
            .ThenByDescending(c => c.LastInboundAt)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new IgConversationDto(
                c.Id, c.IgUserId, c.Username, c.Status, c.LastInboundAt, c.LastOutboundAt,
                c.LastMessageText, c.MessageCount, c.Unread, c.NeedsOperator, c.NeedsOperatorReason,
                c.Language, c.Intent, c.LeadScore, c.LeadId, c.CreatedAt, c.OperatorPausedUntil,
                c.AdId, c.AdCampaignId, ""))
            .ToListAsync(ct);

        // Kampaniya NOMLARI — BITTA so'rovda (N+1 emas): sahifadagi takrorsiz id'lar yig'iladi
        // va bitta `IN (...)` bilan olinadi. Nom topilmasa id'ning O'ZI qoladi.
        items = await AttachCampaignNamesAsync(items, ct);

        return new IgConversationListDto(items, total, page, pageSize);
    }

    /// <summary>Bitta suhbat: xabarlar lentasi (oxirgi 200) + bog'langan lid qisqacha
    /// + 24 soatlik oyna ochiqmi (UI javob maydonini shunga qarab bloklaydi).</summary>
    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<IgConversationDetailDto>> Conversation(string id, CancellationToken ct)
    {
        var c = await db.IgConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Suhbat topilmadi." });

        var messages = await db.IgMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MessageLimit)
            .ToListAsync(ct);
        messages.Reverse();   // ekranda eskidan yangiga

        IgLeadBriefDto? lead = null;
        if (!string.IsNullOrEmpty(c.LeadId))
        {
            var l = await db.Leads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.LeadId, ct);
            if (l is not null)
            {
                var stageTitle = await db.LeadStages.AsNoTracking()
                    .Where(s => s.Id == l.Stage).Select(s => s.Title).FirstOrDefaultAsync(ct) ?? "";
                lead = new IgLeadBriefDto(l.Id, l.FullName, l.Phone, l.Source, stageTitle, l.CreatedAt);
            }
        }

        return new IgConversationDetailDto(
            ToConversationDto(c, await CampaignNameAsync(c.AdCampaignId, ct)),
            messages.Select(ToMessageDto).ToList(),
            lead,
            InstagramContract.DmWindowOpen(c.LastInboundAt, AppClock.Now));
    }

    /// <summary>
    /// Ro'yxatdagi qatorlarga KAMPANIYA NOMINI biriktiradi — <b>bitta</b> so'rov bilan.
    ///
    /// <para>⚠️ N+1 dan qochish: sahifada 100 tagacha suhbat bo'ladi va har biri uchun
    /// <c>IgAdEntity</c> ga alohida borish 100 ta so'rov degani. Shu sabab avval TAKRORSIZ
    /// kampaniya id'lari yig'iladi, keyin ular bitta <c>Contains</c> (SQL <c>IN</c>) bilan
    /// olinadi. Reklama izohi kam uchraydi, ya'ni ro'yxatda umuman atributsiya bo'lmasa
    /// qo'shimcha so'rov <b>umuman ketmaydi</b>.</para>
    ///
    /// <para>Nom topilmasa qator o'zgarishsiz qoladi — DTO'da allaqachon id turadi
    /// (<see cref="InstagramContract.AdCampaignLabel"/>).</para>
    /// </summary>
    private async Task<List<IgConversationDto>> AttachCampaignNamesAsync(
        List<IgConversationDto> items, CancellationToken ct)
    {
        var ids = items
            .Where(i => i.AdCampaignId.Length > 0)
            .Select(i => i.AdCampaignId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            return items.Select(i => i with
            {
                AdCampaignName = InstagramContract.AdCampaignLabel(i.AdCampaignId, "", i.AdId),
            }).ToList();

        var names = await db.IgAdEntities.AsNoTracking()
            .Where(e => ids.Contains(e.ExternalId))
            .Select(e => new { e.ExternalId, e.Name })
            .ToDictionaryAsync(e => e.ExternalId, e => e.Name ?? "", StringComparer.Ordinal, ct);

        return items.Select(i => i with
        {
            AdCampaignName = InstagramContract.AdCampaignLabel(
                i.AdCampaignId, names.GetValueOrDefault(i.AdCampaignId, ""), i.AdId),
        }).ToList();
    }

    /// <summary>BITTA suhbat uchun kampaniya nomi (detal paneli). Atributsiya yo'q bo'lsa
    /// so'rov umuman yuborilmaydi.</summary>
    private async Task<string> CampaignNameAsync(string campaignId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(campaignId)) return "";
        return await db.IgAdEntities.AsNoTracking()
            .Where(e => e.ExternalId == campaignId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(ct) ?? "";
    }

    private static IgMessageDto ToMessageDto(IgMessage m) => new(
        m.Id, m.ConversationId, m.Direction, m.Channel, m.Text, m.ActorName, m.IsAi, m.AiIntent,
        m.AiScore, m.Error, m.CommentId, m.MediaId, m.IgMessageId, m.CreatedAt);

    /// <summary>Entity → DTO. <paramref name="adCampaignName"/> chaqiruvchidan beriladi:
    /// bu metod SOF (bazaga bormaydi), aks holda har chaqirilganda kampaniya nomi uchun
    /// alohida so'rov ketardi.</summary>
    private static IgConversationDto ToConversationDto(IgConversation c, string adCampaignName = "") => new(
        c.Id, c.IgUserId, c.Username, c.Status, c.LastInboundAt, c.LastOutboundAt,
        c.LastMessageText, c.MessageCount, c.Unread, c.NeedsOperator, c.NeedsOperatorReason,
        c.Language, c.Intent, c.LeadScore, c.LeadId, c.CreatedAt, c.OperatorPausedUntil,
        c.AdId, c.AdCampaignId,
        InstagramContract.AdCampaignLabel(c.AdCampaignId, adCampaignName, c.AdId));

    /// <summary>
    /// OPERATOR JAVOBI (DM). Yuborishdan oldin <b>24 soatlik oyna</b> tekshiriladi: mijoz oxirgi
    /// marta 24 soatdan oldin yozgan bo'lsa Instagram xabarni RAD ETADI. Ilgari (NUR loyihasida)
    /// bu tekshirilmasdi — so'rov xato berardi, log'da qolib ketardi va operator "yubordim" deb
    /// o'ylab yurardi. Shu sabab bu yerda aniq o'zbekcha 400 qaytadi.
    ///
    /// <para>Javob yuborilgach bot <c>IgConst.OperatorPauseMinutes</c> davomida jim bo'ladi —
    /// mijoz bir vaqtda odam va bot bilan gaplashmasin.</para>
    /// </summary>
    /// <remarks>
    /// Javob <b>yaratilgan xabar</b> (<see cref="IgMessageDto"/>) bilan qaytadi: Inbox uni
    /// lentaga darhol qo'shadi (butun suhbatni qayta yuklamasdan). Faqat "bajarildi" qaytarilsa
    /// lentaga bo'sh qator tushib qolardi.
    /// </remarks>
    [HttpPost("conversations/{id}/reply")]
    [AdminPerm("marketing.inbox")]
    public async Task<IActionResult> Reply(string id, IgReplyPayload payload, CancellationToken ct)
    {
        var text = (payload.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni bo'sh." });

        var c = await db.IgConversations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Suhbat topilmadi." });

        var acc = await db.IgAccounts.FirstOrDefaultAsync(a => a.IsActive, ct);
        if (acc is null || acc.AccessToken.Length == 0)
            return BadRequest(new { message = "Instagram akkaunt ulanmagan — Sozlamalar bo'limidan ulang." });

        // ── JAVOB OYNASI: operator uchun IKKI BOSQICH ──
        //
        // 24 soat ichida — odatdagi javob. 24 soatdan keyin, lekin 7 kun ichida — Meta'ning
        // `HUMAN_AGENT` tegi bilan (u aynan "javobni TIRIK ODAM yozdi" degani va operator
        // yo'lida bu rost). Ilgari bu yerda faqat 24 soat tekshirilar va operator juma kuni
        // kelgan savolga dushanba kuni javob YOZA OLMASDI — UI esa buni Instagram'ning mutlaq
        // cheklovi qilib ko'rsatardi, aslida bu bizning ishlatmagan imkoniyatimiz edi.
        //
        // 🔴 Teg BOTGA berilmaydi (`InstagramPipeline` hamon `DmWindowOpen` ga tayanadi):
        // avtomatik javobni "odam yozdi" deb belgilash Meta siyosatini buzadi.
        var standardWindow = InstagramContract.DmWindowOpen(c.LastInboundAt, AppClock.Now);
        var humanAgentWindow = InstagramContract.HumanAgentWindowOpen(c.LastInboundAt, AppClock.Now);

        if (!standardWindow && !humanAgentWindow)
            return BadRequest(new
            {
                message = "Javob berish oynasi yopilgan: mijoz oxirgi yozganidan beri "
                          + $"{IgConst.HumanAgentWindowHours / 24} kundan ko'p vaqt o'tdi. "
                          + "Instagram bu holatda DM yuborishga umuman ruxsat bermaydi — "
                          + "telefon orqali bog'laning.",
            });

        var (ok, err) = await api.SendDmAsync(
            acc.IgUserId, c.IgUserId, text, acc.AccessToken, ct, humanAgent: !standardWindow);

        var now = AppClock.Iso();
        var sent = new IgMessage
        {
            ConversationId = c.Id,
            Direction = "out",
            Channel = IgConst.ChannelDm,
            Text = text,
            ActorName = Actor,
            IsAi = false,
            // Xato bo'lsa ham qator SAQLANADI: "javob ketmadi" ni operator ko'rishi kerak.
            Error = ok ? "" : err,
            CreatedAt = now,
        };

        // E6.6 — JAVOB SIFATI JURNALI: agar shu javobdan oldin AI taklif qilgan matn bo'lsa,
        // "AI shunday dedi → operator shunday yozdi" juftligi saqlanadi (`IgQualityLog`).
        // ⚠️ `Add` dan OLDIN chaqiriladi — so'rov bazaga ketadi va hali yozilmagan qatorni
        // taklif deb olib qo'ymaydi. Xato bo'lsa jim yutiladi: sifat jurnali tufayli
        // operatorning javobi yo'qolmasin.
        await IgQualityLog.AttachSuggestionAsync(db, c.Id, sent, AppClock.Now, ct);

        db.IgMessages.Add(sent);

        if (ok)
        {
            c.LastOutboundAt = now;
            c.LastMessageText = text;
            c.MessageCount += 1;
            c.Unread = false;
            c.NeedsOperator = false;
            c.NeedsOperatorReason = "";
            // Operator javob yozdi → bot shu suhbatda jim bo'ladi (mijoz bir vaqtda "ikki odam"
            // bilan gaplashmasin). Muddat `IgConst` dan — echo orqali yoqiladigan pauza bilan
            // AYNAN bir xil bo'lsin, aks holda ikki yo'l ikki xil xulq berardi. Uzoqroq ushlab
            // turish kerak bo'lsa operator "O'zim javob beraman" (takeover) tugmasini bosadi.
            c.OperatorPausedUntil = AppClock.Now.AddMinutes(IgConst.OperatorPauseMinutes).ToString("yyyy-MM-ddTHH:mm:ss");
        }

        // ⚠️ Auditga xabar MATNI yozilmaydi (begona odamning shaxsiy yozishmasi tarixga
        // ko'chirilmasin) — faqat "kim, kimga, javob berdi".
        audit.Record(AuditEntity, c.Id, "update",
            ok
                ? $"Instagram suhbatiga operator javob yozdi (@{c.Username})"
                : $"Instagram suhbatiga operator javobi YUBORILMADI (@{c.Username}): {err}");
        await db.SaveChangesAsync(ct);

        if (!ok) return BadRequest(new { message = err });
        return Ok(ToMessageDto(sent));
    }

    /// <summary>Suhbatni operator O'ZIGA oladi — bot bu suhbatda umuman javob bermaydi.</summary>
    [HttpPost("conversations/{id}/takeover")]
    [AdminPerm("marketing.inbox")]
    public Task<IActionResult> Takeover(string id, CancellationToken ct) =>
        SetStatusAsync(id, IgConst.StatusOperator, "operator o'z zimmasiga oldi (bot jim)", ct);

    /// <summary>Suhbatni botga qaytarish — pauza ham bekor qilinadi.</summary>
    [HttpPost("conversations/{id}/release")]
    [AdminPerm("marketing.inbox")]
    public Task<IActionResult> Release(string id, CancellationToken ct) =>
        SetStatusAsync(id, IgConst.StatusBot, "botga qaytarildi", ct);

    /// <summary>Suhbatni yopish (hal bo'ldi).</summary>
    [HttpPost("conversations/{id}/close")]
    [AdminPerm("marketing.inbox")]
    public Task<IActionResult> Close(string id, CancellationToken ct) =>
        SetStatusAsync(id, IgConst.StatusClosed, "yopildi", ct);

    private async Task<IActionResult> SetStatusAsync(string id, string status, string what, CancellationToken ct)
    {
        var c = await db.IgConversations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Suhbat topilmadi." });

        c.Status = status;
        if (status == IgConst.StatusBot)
        {
            c.OperatorPausedUntil = "";
            c.NeedsOperator = false;
            c.NeedsOperatorReason = "";
        }
        if (status == IgConst.StatusClosed) c.Unread = false;

        audit.Record(AuditEntity, c.Id, "update", $"Instagram suhbati (@{c.Username}) — {what}");
        await db.SaveChangesAsync(ct);
        // Yangilangan SUHBAT qaytadi — ro'yxat/sarlavha darhol yangi holatni ko'rsatsin.
        return Ok(ToConversationDto(c));
    }

    /// <summary>"O'qildi" belgisi. Audit YOZILMAYDI — bu ko'rish amali, ma'lumot o'zgarmaydi
    /// va har suhbat ochilganda tarixni ko'chki bilan to'ldirardi.</summary>
    [HttpPost("conversations/{id}/read")]
    [AdminPerm("marketing.inbox")]
    public async Task<IActionResult> MarkRead(string id, CancellationToken ct)
    {
        var c = await db.IgConversations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Suhbat topilmadi." });
        c.Unread = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Belgilandi." });
    }

    /// <summary>
    /// Suhbatni QO'LDA lidga aylantirish — AI "qaynoq" deb bilmagan, lekin operator gaplashib
    /// ko'rib "bu odam keladi" degan holat uchun. Lid yaratish mantig'i AI oqimi bilan BITTA
    /// joyda (<see cref="InstagramLeadBridge"/>): dublikat tekshiruvi, first-touch qoidasi va
    /// lid hodisasi ikki yo'lda ayri ketmasin.
    /// </summary>
    /// <remarks>
    /// ⚠️ Payload IXTIYORIY (<c>[FromBody]</c> + nullable): Inbox'dagi «Lidga aylantirish» tugmasi
    /// hech qanday forma so'ramasdan, BO'SH tanali POST yuboradi. <c>[ApiController]</c> majburiy
    /// tanani talab qilgani uchun nullable bo'lmasa tugma har doim 400 bilan qaytardi.
    /// Ism/telefon berilmasa ular suhbatning o'zidan olinadi.
    /// </remarks>
    [HttpPost("conversations/{id}/create-lead")]
    [AdminPerm("marketing.inbox")]
    public async Task<IActionResult> CreateLead(string id, [FromBody] IgCreateLeadPayload? body, CancellationToken ct)
    {
        var payload = body ?? new IgCreateLeadPayload(null, null, null, null);

        var c = await db.IgConversations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound(new { message = "Suhbat topilmadi." });

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var source = string.IsNullOrWhiteSpace(meta?.InstagramLeadSource) ? "Instagram" : meta!.InstagramLeadSource;

        // Telefon: operator kiritgani ustun; kiritmasa suhbat matnlaridan ajratib olamiz.
        var phone = (payload.Phone ?? "").Trim();
        if (phone.Length == 0) phone = await FindPhoneAsync(c.Id, ct);

        var output = new IgAgentOutput(
            Reply: "",
            Language: string.IsNullOrWhiteSpace(c.Language) ? "uz-Latn" : c.Language,
            Intent: string.IsNullOrWhiteSpace(c.Intent) ? "other" : c.Intent,
            LeadScore: Math.Max(c.LeadScore, IgConst.HotLeadScore),
            IsHotLead: true,
            MoveToDm: false,
            EscalateToHuman: false,
            LeadName: (payload.Name ?? "").Trim(),
            LeadContact: phone,
            LeadProductInterest: (payload.Interest ?? "").Trim(),
            LeadSummary: string.IsNullOrWhiteSpace(payload.Note)
                ? $"Instagram suhbatidan qo'lda yaratildi (@{c.Username})"
                : payload.Note!.Trim());

        // ⚠️ Upsert `c.LeadId` ni to'ldirib qo'yadi — "birinchi bog'lanish" undan OLDIN o'qiladi.
        var firstLink = string.IsNullOrWhiteSpace(c.LeadId);
        var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(db, c, output, source, ct);
        if (string.IsNullOrEmpty(leadId))
            return BadRequest(new { message = "Lid yaratilmadi — keyinroq qaytadan urinib ko'ring." });

        audit.Record(AuditEntity, c.Id, "update",
            isNew
                ? $"Instagram suhbati lidga aylantirildi (@{c.Username}, manba: {source})"
                : $"Instagram suhbati mavjud lidga bog'landi (@{c.Username})");
        await db.SaveChangesAsync(ct);

        // Telegram kartasi. Suhbat lidga BIRINCHI marta bog'lanayotgan bo'lsa — guruhga YANGI
        // LID sifatida yuboriladi (AI oqimidagi §9.1 bilan AYNAN bir xil qoida: operator
        // «Lidga aylantirish» ni bosgani ham lidning tug'ilishi). Aks holda — mavjud karta
        // JIMGINA yangilanadi: `RepeatCount`, izoh va hodisalar o'zgargan, karta esa eski
        // ma'lumot bilan qolib ketardi.
        // SaveChanges'dan KEYIN chaqiriladi (karta bazadagi yozilgan holatdan quriladi).
        if (firstLink && (meta?.InstagramNotifyTelegram ?? true))
        {
            var fresh = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
            if (fresh is not null)
                await LeadNotifier.NotifyNewLeadAsync(
                    db, telegram, fresh, isNewLead: isNew,
                    createdBy: InstagramLeadBridge.ActorName, ct: ct, logger: logger);
        }
        else
        {
            await LeadNotifier.SyncCardAsync(db, telegram, leadId, ct, logger);
        }

        // Yaratilgan/bog'langan LID qaytadi — suhbat sarlavhasidagi "Lidga bog'langan" bloki
        // shu javobdan chiziladi (suhbat detalidagi `lead` bilan bir xil shakl).
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == leadId, ct);
        if (lead is null) return Ok(new IgLeadBriefDto(leadId, "", "", source, "", ""));

        var stage = await db.LeadStages.AsNoTracking()
            .Where(s => s.Id == lead.Stage).Select(s => s.Title).FirstOrDefaultAsync(ct) ?? "";
        return Ok(new IgLeadBriefDto(lead.Id, lead.FullName, lead.Phone, lead.Source, stage, lead.CreatedAt));
    }

    /// <summary>Suhbatdagi KIRUVCHI xabarlardan telefon raqamini qidiradi (eng oxirgisi ustun).</summary>
    private async Task<string> FindPhoneAsync(string conversationId, CancellationToken ct)
    {
        var texts = await db.IgMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Direction == "in")
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Text)
            .Take(50)
            .ToListAsync(ct);

        foreach (var t in texts)
        {
            var phone = InstagramContract.ExtractPhone(t ?? "");
            if (!string.IsNullOrWhiteSpace(phone)) return phone;
        }
        return "";
    }

    // =============================================================================================
    //  JAVOB QOIDALARI (kalit so'z)
    // =============================================================================================

    /// <summary>Kalit so'z qoidalari — tekshirish TARTIBIDA (aniqroq qoidalar yuqorida).</summary>
    [HttpGet("rules")]
    public async Task<ActionResult<List<IgRuleDto>>> Rules(CancellationToken ct) =>
        await db.IgAutoRules.AsNoTracking()
            .OrderBy(r => r.Order).ThenBy(r => r.Title)
            .Select(r => new IgRuleDto(r.Id, r.Title, r.Keywords, r.Channel, r.ReplyText,
                r.StopAi, r.IsActive, r.Order, r.MatchCount, r.CreatedAt))
            .ToListAsync(ct);

    [HttpPost("rules")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgRuleDto>> CreateRule(IgRulePayload payload, CancellationToken ct)
    {
        var err = ValidateRule(payload);
        if (err is not null) return BadRequest(new { message = err });

        var rule = new IgAutoRule
        {
            Title = payload.Title.Trim(),
            Keywords = payload.Keywords.Trim(),
            Channel = NormalizeChannel(payload.Channel),
            ReplyText = payload.ReplyText.Trim(),
            StopAi = payload.StopAi,
            IsActive = payload.IsActive,
            Order = payload.Order,
            CreatedAt = AppClock.Iso(),
        };
        db.IgAutoRules.Add(rule);
        audit.Record(AuditEntity, rule.Id, "create",
            $"Instagram javob qoidasi yaratildi: «{rule.Title}» (kanal: {ChannelLabel(rule.Channel)})");
        await db.SaveChangesAsync(ct);
        return ToRuleDto(rule);
    }

    [HttpPut("rules/{id}")]
    [AdminPerm("marketing.rules")]
    public async Task<ActionResult<IgRuleDto>> UpdateRule(string id, IgRulePayload payload, CancellationToken ct)
    {
        var err = ValidateRule(payload);
        if (err is not null) return BadRequest(new { message = err });

        var rule = await db.IgAutoRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound(new { message = "Qoida topilmadi." });

        var before = RuleSnapshot(rule);
        rule.Title = payload.Title.Trim();
        rule.Keywords = payload.Keywords.Trim();
        rule.Channel = NormalizeChannel(payload.Channel);
        rule.ReplyText = payload.ReplyText.Trim();
        rule.StopAi = payload.StopAi;
        rule.IsActive = payload.IsActive;
        rule.Order = payload.Order;

        audit.Record(AuditEntity, rule.Id, "update",
            $"Instagram javob qoidasi tahrirlandi: «{rule.Title}»", before: before, after: RuleSnapshot(rule));
        await db.SaveChangesAsync(ct);
        return ToRuleDto(rule);
    }

    [HttpDelete("rules/{id}")]
    [AdminPerm("marketing.rules")]
    public async Task<IActionResult> DeleteRule(string id, CancellationToken ct)
    {
        var rule = await db.IgAutoRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound(new { message = "Qoida topilmadi." });

        db.IgAutoRules.Remove(rule);
        audit.Record(AuditEntity, rule.Id, "delete", $"Instagram javob qoidasi o'chirildi: «{rule.Title}»");
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Qoida o'chirildi." });
    }

    private static string? ValidateRule(IgRulePayload p)
    {
        if (string.IsNullOrWhiteSpace(p.Title)) return "Qoida nomi bo'sh bo'lmasin.";
        if (string.IsNullOrWhiteSpace(p.Keywords)) return "Kamida bitta kalit so'z kiriting (vergul bilan ajrating).";
        if (string.IsNullOrWhiteSpace(p.ReplyText)) return "Javob matni bo'sh bo'lmasin.";
        return null;
    }

    private static string NormalizeChannel(string? c) =>
        c is IgConst.ChannelComment or IgConst.ChannelDm ? c : "any";

    private static string ChannelLabel(string c) => c switch
    {
        IgConst.ChannelComment => "izohlar",
        IgConst.ChannelDm => "shaxsiy xabarlar",
        _ => "hammasi",
    };

    private static object RuleSnapshot(IgAutoRule r) =>
        new { r.Title, r.Keywords, r.Channel, r.ReplyText, r.StopAi, r.IsActive, r.Order };

    private static IgRuleDto ToRuleDto(IgAutoRule r) =>
        new(r.Id, r.Title, r.Keywords, r.Channel, r.ReplyText, r.StopAi, r.IsActive, r.Order, r.MatchCount, r.CreatedAt);

    // =============================================================================================
    //  BILIM BAZASI
    // =============================================================================================

    /// <summary>Bilim bazasi bo'laklari (AI FAQAT shulardan javob beradi).</summary>
    [HttpGet("knowledge")]
    public async Task<ActionResult<List<IgKnowledgeDto>>> Knowledge(CancellationToken ct) =>
        await KnowledgeRowsAsync(ct);

    /// <summary>
    /// Bilim bazasini BUTUNLIGICHA saqlash (bulk). Sahifa bir nechta bo'lakni birdaniga
    /// tahrirlaydi — har bo'lak uchun alohida so'rov bo'lsa yarim saqlangan holat qolib ketardi.
    /// Ro'yxatda yo'q bo'laklar O'CHIRILADI (ekranda ko'rinib turgan holat = saqlangan holat).
    /// </summary>
    [HttpPut("knowledge")]
    [AdminPerm("marketing.knowledge")]
    public async Task<ActionResult<List<IgKnowledgeDto>>> SaveKnowledge(IgKnowledgeBulkPayload payload, CancellationToken ct)
    {
        var items = payload.Items ?? new List<IgKnowledgeItemPayload>();
        if (items.Any(i => string.IsNullOrWhiteSpace(i.Title)))
            return BadRequest(new { message = "Har bir bo'lakning sarlavhasi bo'lishi shart." });

        var now = AppClock.Iso();
        var existing = await db.IgKnowledges.ToListAsync(ct);
        var keptIds = items.Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => i.Id!).ToHashSet();

        foreach (var old in existing.Where(e => !keptIds.Contains(e.Id)))
            db.IgKnowledges.Remove(old);

        var order = 0;
        foreach (var item in items)
        {
            var row = existing.FirstOrDefault(e => e.Id == item.Id);
            if (row is null)
            {
                row = new IgKnowledge();
                db.IgKnowledges.Add(row);
            }
            row.Title = item.Title.Trim();
            row.Content = (item.Content ?? "").Trim();
            row.Order = order++;
            row.IsActive = item.IsActive;
            row.UpdatedAt = now;
            row.UpdatedBy = Actor;
            // ⚠️ `SourceFile` ATAYIN tegilmaydi: u payloadda yo'q va mavjud qator bazadan
            // o'qilgani uchun o'z qiymatida qoladi. Aks holda bilim bazasini bir marta
            // saqlash Word'dan kelgan bo'laklarning manbasini o'chirib yuborardi va
            // «shu fayldan kelganlarini o'chirish» amali ishlamay qolardi.
        }

        audit.Record(AuditEntity, "knowledge", "update",
            $"Instagram bilim bazasi saqlandi — {items.Count} ta bo'lak");
        await db.SaveChangesAsync(ct);
        return await Knowledge(ct);
    }

    // =============================================================================================
    //  DIAGNOSTIKA — test va simulyatsiya
    // =============================================================================================

    /// <summary>
    /// AI javobini SINAB ko'rish. <b>Javob mijozga YUBORILMAYDI</b> — faqat ekranda ko'rsatiladi
    /// (bilim bazasi to'g'ri yozilganini, tilni va lid bahosini tekshirish uchun).
    /// </summary>
    [HttpPost("test-agent")]
    [AdminPerm("marketing.knowledge")]
    public async Task<ActionResult<IgTestAgentDto>> TestAgent(IgTestAgentPayload payload, CancellationToken ct)
    {
        var message = (payload.Message ?? "").Trim();
        if (message.Length == 0) return BadRequest(new { message = "Sinov xabari bo'sh." });

        var channel = payload.Channel == IgConst.ChannelComment ? IgConst.ChannelComment : IgConst.ChannelDm;
        var (ok, output, err) = await InstagramAgentService.AskAsync(
            db, config, channel,
            username: string.IsNullOrWhiteSpace(payload.Username) ? "sinov" : payload.Username!.Trim(),
            mediaCaption: (payload.MediaCaption ?? "").Trim(),
            message: message,
            history: new List<IgMessage>(),
            ct: ct);

        // ⚠️ AI javob bermasa ham 200 qaytadi (`Ok = false` + sabab): bu DIAGNOSTIKA ekrani va
        // "Gemini kaliti yo'q / bilim bazasi bo'sh" degan javobning O'ZI kerakli natija. 400
        // qaytarilsa sabab umumiy xato bannerida yo'qolib, sinov shakli bo'sh qolardi.
        if (!ok || output is null)
            return new IgTestAgentDto(
                Ok: false, Reply: "", Language: "", Intent: "", LeadScore: 0, IsHotLead: false,
                MoveToDm: false, EscalateToHuman: false, LeadName: "", LeadContact: "",
                LeadProductInterest: "", LeadSummary: "", WouldCreateLead: false,
                Error: string.IsNullOrWhiteSpace(err) ? "AI javob bermadi." : err);

        return new IgTestAgentDto(
            Ok: true,
            Reply: output.Reply,
            Language: output.Language,
            Intent: output.Intent,
            LeadScore: InstagramContract.ClampScore(output.LeadScore),
            IsHotLead: InstagramContract.IsHot(output),
            MoveToDm: output.MoveToDm,
            EscalateToHuman: output.EscalateToHuman,
            LeadName: output.LeadName,
            LeadContact: output.LeadContact,
            LeadProductInterest: output.LeadProductInterest,
            LeadSummary: output.LeadSummary,
            WouldCreateLead: InstagramContract.ShouldCreateLead(output),
            Error: "");
    }

    /// <summary>
    /// SOXTA webhook hodisasi — navbatga qo'yiladi va butun oqim (qoida → AI → javob → lid)
    /// haqiqiy yo'l bilan tekshiriladi.
    ///
    /// <para>⚠️ Bu endpoint AYNAN shu ruxsat ostida turadi. NUR loyihasida <c>/simulate</c>
    /// autentifikatsiyasiz ochiq edi — ya'ni tashqaridagi odam bizning nomimizdan xabar
    /// yubortirishi va AI tokenimizni yeyishi mumkin edi. Bu xato takrorlanmaydi.</para>
    ///
    /// <para>Modul o'chirilgan bo'lsa hodisa navbatda kutadi, lekin qayta ishlanmaydi —
    /// shuning uchun oldindan ogohlantiramiz.</para>
    /// </summary>
    [HttpPost("simulate")]
    [AdminPerm("marketing.knowledge")]
    public async Task<IActionResult> Simulate(IgSimulatePayload payload, CancellationToken ct)
    {
        var text = (payload.Text ?? "").Trim();
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni bo'sh." });

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramEnabled)
            return BadRequest(new { message = "Modul o'chirilgan — avval Sozlamalarda «Instagram agenti»ni yoqing." });

        var acc = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        if (acc is null) return BadRequest(new { message = "Instagram akkaunt ulanmagan." });

        var kind = payload.Kind == IgConst.ChannelComment ? IgConst.ChannelComment : IgConst.ChannelDm;
        var senderId = string.IsNullOrWhiteSpace(payload.SenderId) ? "sim-user-1" : payload.SenderId!.Trim();
        var username = string.IsNullOrWhiteSpace(payload.Username) ? "sinov_mijoz" : payload.Username!.Trim().TrimStart('@');
        var unique = Guid.NewGuid().ToString("N");

        db.IgWebhookEvents.Add(new IgWebhookEvent
        {
            // "sim:" prefiksi — navbat diagnostikasida sinov hodisalari darhol ajralib tursin.
            EventKey = "sim:" + unique,
            RawJson = BuildSimulatedPayload(kind, acc.IgUserId, senderId, username, text, unique),
            Status = IgConst.EvPending,
            ReceivedAt = AppClock.Iso(),
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[instagram] simulyatsiya navbatga qo'yildi ({Kind}) — {Actor}", kind, Actor);
        return Ok(new { message = "Sinov hodisasi navbatga qo'yildi — bir necha soniyada Inbox'da ko'rinadi." });
    }

    /// <summary>Meta payload SHAKLIDA soxta hodisa (parser haqiqiy hodisadagidek ishlasin).</summary>
    private static string BuildSimulatedPayload(
        string kind, string ourIgUserId, string senderId, string username, string text, string unique)
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        object entry = kind == IgConst.ChannelComment
            ? new
            {
                id = ourIgUserId,
                time = unix,
                changes = new[]
                {
                    new
                    {
                        field = "comments",
                        value = new
                        {
                            id = "sim-comment-" + unique,
                            text,
                            timestamp = AppClock.Iso(),
                            from = new { id = senderId, username },
                            media = new { id = "sim-media-" + unique },
                        },
                    },
                },
            }
            : new
            {
                id = ourIgUserId,
                time = unix,
                messaging = new[]
                {
                    new
                    {
                        sender = new { id = senderId },
                        recipient = new { id = ourIgUserId },
                        timestamp = unix * 1000,
                        message = new { mid = "sim-mid-" + unique, text },
                    },
                },
            };

        return JsonSerializer.Serialize(new { @object = "instagram", entry = new[] { entry } },
            new JsonSerializerOptions
            {
                // O'zbekcha apostrof va harflar `\u...` bo'lib ketmasin (prompt/log o'qilishi uchun).
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }

    /// <summary>Navbat diagnostikasi — "hodisa keldimi, qayta ishlandimi, xato nima edi".</summary>
    [HttpGet("events")]
    public async Task<ActionResult<List<IgEventDto>>> Events([FromQuery] string? status, CancellationToken ct)
    {
        var q = db.IgWebhookEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(e => e.Status == status);
        return await q
            .OrderByDescending(e => e.ReceivedAt)
            .Take(EventLimit)
            .Select(e => new IgEventDto(e.Id, e.EventKey, e.Status, e.Attempts, e.Error,
                e.ReceivedAt, e.ProcessedAt, e.RawJson.Length))
            .ToListAsync(ct);
    }

    // =============================================================================================
    //  ANALITIKA
    // =============================================================================================

    /// <summary>
    /// Davr bo'yicha kesimlar. Sana FILTRLARI ISO satr ustida ishlaydi (loyihadagi konvensiya):
    /// <c>to</c> KUN sifatida beriladi va <c>T23:59:59</c> gacha cho'ziladi — aks holda o'sha
    /// kunning o'zi tushib qolardi.
    /// </summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<IgAnalyticsDto>> Analytics(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        // Buzuq sana 500 bermasin — standart davr (oxirgi 30 kun) ga qaytamiz.
        if (!DateOnly.TryParse(from, out var fromDay)) fromDay = AppClock.Today.AddDays(-29);
        if (!DateOnly.TryParse(to, out var toDay)) toDay = AppClock.Today;
        if (toDay < fromDay) (fromDay, toDay) = (toDay, fromDay);

        var fromDate = fromDay.ToString("yyyy-MM-dd");
        var toDate = toDay.ToString("yyyy-MM-dd");
        var fromIso = fromDate + "T00:00:00";
        var toIso = toDate + "T23:59:59";

        var messages = await db.IgMessages.AsNoTracking()
            .Where(m => string.Compare(m.CreatedAt, fromIso) >= 0 && string.Compare(m.CreatedAt, toIso) <= 0)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.CreatedAt, m.Direction, m.Channel, m.IsAi, m.AiIntent, m.Error })
            .Take(AnalyticsScanLimit)   // tartiblangan — chegara kesganda qaysi qatorlar qolgani aniq
            .ToListAsync(ct);

        var convs = await db.IgConversations.AsNoTracking()
            .Where(c => string.Compare(c.CreatedAt, fromIso) >= 0 && string.Compare(c.CreatedAt, toIso) <= 0)
            .Select(c => new { c.CreatedAt, c.Language, c.LeadScore, c.LeadId, c.NeedsOperator })
            .ToListAsync(ct);

        var events = await db.IgWebhookEvents.AsNoTracking()
            .Where(e => string.Compare(e.ReceivedAt, fromIso) >= 0 && string.Compare(e.ReceivedAt, toIso) <= 0)
            .Select(e => new { e.ReceivedAt, e.Status })
            .ToListAsync(ct);

        // --- kunlik qator (bo'sh kunlar ham o'z o'rnida — grafik sakramasin) ---
        var daily = new List<IgDailyDto>();
        for (var d = fromDay; d <= toDay; d = d.AddDays(1))
        {
            var day = d.ToString("yyyy-MM-dd");
            daily.Add(new IgDailyDto(
                day,
                events.Count(e => e.ReceivedAt.StartsWith(day, StringComparison.Ordinal)),
                messages.Count(m => m.Direction == "in" && m.CreatedAt.StartsWith(day, StringComparison.Ordinal)),
                messages.Count(m => m.Direction == "out" && m.CreatedAt.StartsWith(day, StringComparison.Ordinal)),
                convs.Count(c => c.LeadId != null && c.CreatedAt.StartsWith(day, StringComparison.Ordinal)),
                // "Qaynoq" — o'sha kuni BOSHLANGAN suhbatlardan bali chegaradan yuqorilari
                // (`Totals.Hot` bilan bir xil o'lchov, faqat kun kesimida).
                convs.Count(c => c.LeadScore >= IgConst.HotLeadScore
                                 && c.CreatedAt.StartsWith(day, StringComparison.Ordinal))));
        }

        var totals = new IgTotalsDto(
            Events: events.Count,
            Inbound: messages.Count(m => m.Direction == "in"),
            Replies: messages.Count(m => m.Direction == "out"),
            AiReplies: messages.Count(m => m.Direction == "out" && m.IsAi),
            FailedReplies: messages.Count(m => m.Direction == "out" && m.Error.Length > 0),
            Conversations: convs.Count,
            Leads: convs.Count(c => c.LeadId != null),
            Hot: convs.Count(c => c.LeadScore >= IgConst.HotLeadScore),
            Escalations: convs.Count(c => c.NeedsOperator),
            FailedEvents: events.Count(e => e.Status == IgConst.EvFailed));

        // Niyat qaysi xabar qatoriga yozilgani (kiruvchi yoki AI javobi) pipeline'ning ichki
        // qarori — kesim ikkalasini ham hisobga oladi, aks holda jadval bo'sh chiqib qolardi.
        var byIntent = messages
            .Where(m => !string.IsNullOrEmpty(m.AiIntent))
            .GroupBy(m => m.AiIntent)
            .Select(g => new IgBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();

        var byLanguage = convs
            .Where(c => !string.IsNullOrEmpty(c.Language))
            .GroupBy(c => c.Language)
            .Select(g => new IgBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();

        var byChannel = messages
            .GroupBy(m => m.Channel)
            .Select(g => new IgBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();

        // ⚠️ `MatchCount` — qoidaning BUTUN umri bo'yicha sanog'i, davrga bog'liq EMAS
        // (har ishlaganda hodisa yozilmaydi). UI'da shu izoh bilan ko'rsatiladi.
        var topRules = await db.IgAutoRules.AsNoTracking()
            .Where(r => r.MatchCount > 0)
            .OrderByDescending(r => r.MatchCount)
            .Take(10)
            .Select(r => new IgTopRuleDto(r.Id, r.Title, r.MatchCount))
            .ToListAsync(ct);

        return new IgAnalyticsDto(fromDate, toDate, daily, totals, byIntent, byLanguage, byChannel, topRules);
    }

    // =============================================================================================
    //  REKLAMA LIDLARI (Meta Lead Ads)
    // =============================================================================================
    //  Izoh/DM oqimidan MUSTAQIL: o'z bayrog'i (`InstagramLeadAdsEnabled`), o'z tokeni (Page
    //  Access Token) va o'z webhook manzili bor. Sozlash HAMMASI shu bo'limdan qilinadi.

    /// <summary>
    /// Reklama lidlari DIAGNOSTIKASI — "nega lid kelmayapti" savolining barcha sabablari bitta
    /// ekranda: modul yoqilganmi, sahifa ulanganmi, tokeni bormi, obuna qilinganmi, oxirgi lid
    /// qachon kelgan va oxirgi xato nima edi.
    /// <para>⚠️ Token QIYMATI qaytmaydi — faqat "sozlangan/sozlanmagan".</para>
    /// </summary>
    [HttpGet("ads/status")]
    public async Task<ActionResult<IgAdStatusDto>> AdStatus(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var page = await db.IgAdPages.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var monthStart = AppClock.Today.AddDays(-29).ToString("yyyy-MM-dd");

        return new IgAdStatusDto(
            Enabled: meta?.InstagramLeadAdsEnabled ?? false,
            PageConnected: page is not null,
            PageId: page?.PageId ?? "",
            PageName: page?.PageName ?? "",
            TokenSet: !string.IsNullOrWhiteSpace(page?.AccessToken),
            LeadgenSubscribed: page?.LeadgenSubscribed ?? false,
            ConnectedAt: page?.ConnectedAt ?? "",
            ConnectedBy: page?.ConnectedBy ?? "",
            LastLeadAt: page?.LastLeadAt ?? "",
            LastError: page?.LastError ?? "",
            AppSecretSet: AppSecrets.MetaAppSecret.Length > 0,
            VerifyTokenSet: AppSecrets.MetaVerifyToken.Length > 0,
            LeadsTotal: await db.IgAdLeads.CountAsync(ct),
            LeadsToday: await db.IgAdLeads.CountAsync(l => l.CreatedTime.Substring(0, 10) == today, ct),
            Leads30Days: await db.IgAdLeads.CountAsync(l => l.CreatedTime.CompareTo(monthStart) >= 0, ct),
            LeadsFailed: await db.IgAdLeads.CountAsync(l => l.Error != "", ct),
            LeadgenUrl: InstagramWebhookController.LeadgenUrl(Request),
            EnvKeyAppSecret: AppSecrets.EnvKeys.MetaAppSecret,
            EnvKeyVerifyToken: AppSecrets.EnvKeys.MetaVerifyToken);
    }

    /// <summary>
    /// Facebook Page'ni ULASH — Page ID va Page Access Token kiritiladi.
    ///
    /// <para><b>Nega OAuth emas, qo'lda?</b> Reklama lidlari uchun System User tokeni ishlatiladi
    /// va u <b>muddatsiz</b> — bir marta kiritiladi va qayta ulash kerak bo'lmaydi. OAuth oqimi
    /// esa Facebook Login mahsulotini, yana bir redirect URI'ni va 60 kunlik tokenni yangilash
    /// mexanizmini talab qilardi — bir martalik sozlama uchun bu ortiqcha.</para>
    ///
    /// <para><b>Saqlashdan OLDIN tekshiriladi</b> (<c>GET /{page-id}</c>): token noto'g'ri
    /// sahifaniki bo'lsa yoki muddati tugagan bo'lsa xato DARHOL ko'rinadi. Aks holda nosozlik
    /// "reklama ishlayapti, lekin lid kelmayapti" bo'lib bir haftadan keyin sezilardi.</para>
    ///
    /// <para>Keyin sahifa <c>leadgen</c> maydoniga OBUNA qilinadi — busiz Meta hodisani umuman
    /// yubormaydi. Obuna muvaffaqiyatsiz bo'lsa sozlama BARIBIR saqlanadi (token to'g'ri, faqat
    /// ruxsat yetishmayapti) va holat ekranda qizil bo'lib turadi.</para>
    /// </summary>
    [HttpPut("ads/page")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgAdStatusDto>> SaveAdPage(IgAdPagePayload payload, CancellationToken ct)
    {
        var pageId = (payload.PageId ?? "").Trim();
        var token = (payload.AccessToken ?? "").Trim();
        if (pageId.Length == 0) return BadRequest(new { message = "Page ID kiritilmagan." });

        var existing = await db.IgAdPages.FirstOrDefaultAsync(p => p.IsActive, ct);

        // Token bo'sh kelsa — MAVJUDI saqlanadi (forma tokenni ko'rsatmaydi, ya'ni faqat Page ID
        // tahrirlanganda uni qayta yozdirish shart emas).
        if (token.Length == 0) token = existing?.AccessToken ?? "";
        if (token.Length == 0)
            return BadRequest(new { message = "Page Access Token kiritilmagan." });

        var (okPage, pageName, errPage) = await adsApi.FetchPageAsync(pageId, token, ct);
        if (!okPage) return BadRequest(new { message = errPage });

        var (okSub, errSub) = await adsApi.SubscribeLeadgenAsync(pageId, token, ct);
        if (!okSub)
            logger.LogWarning("[leadgen] sahifani obuna qilib bo'lmadi ({Page}): {Err}", pageId, errSub);

        var page = existing;
        if (page is null)
        {
            page = new IgAdPage { ConnectedAt = AppClock.Iso() };
            db.IgAdPages.Add(page);
        }
        page.PageId = pageId;
        page.PageName = pageName;
        page.AccessToken = token;
        page.LeadgenSubscribed = okSub;
        page.IsActive = true;
        page.ConnectedBy = Actor;
        page.LastError = okSub ? "" : errSub;

        // ⚠️ Auditga TOKEN yozilmaydi (audit.md §1) — faqat qaysi sahifa ulangani.
        audit.Record(AuditEntity, page.Id, "update",
            $"Reklama lidlari uchun Facebook sahifa ulandi: {pageName} ({pageId})"
            + (okSub ? " — leadgen obunasi faol" : " — ⚠️ obuna qilinmadi: " + errSub));
        await db.SaveChangesAsync(ct);

        return await AdStatus(ct);
    }

    /// <summary>Sahifani UZISH. Qator O'CHIRILMAYDI (kelgan lidlar tarixi va analitika saqlansin) —
    /// faqat <c>IsActive=false</c> va token TOZALANADI.</summary>
    [HttpDelete("ads/page")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgAdStatusDto>> DisconnectAdPage(CancellationToken ct)
    {
        var pages = await db.IgAdPages.Where(p => p.IsActive).ToListAsync(ct);
        if (pages.Count == 0) return BadRequest(new { message = "Ulangan sahifa yo'q." });

        foreach (var p in pages)
        {
            p.IsActive = false;
            p.AccessToken = "";
            p.LeadgenSubscribed = false;
        }

        audit.Record(AuditEntity, pages[0].Id, "update",
            $"Reklama lidlari uchun ulangan sahifa uzildi ({pages[0].PageName}) — yangi lidlar kelmaydi");
        await db.SaveChangesAsync(ct);
        return await AdStatus(ct);
    }

    /// <summary>
    /// Kelgan reklama lidlari ro'yxati + jamlanma va kesimlar.
    ///
    /// <para>Tartib — <b>CreatedTime bo'yicha, eng yangisi tepada</b> (Meta bergan vaqt; navbat
    /// kechikkanda "qabul qilingan vaqt" bo'yicha tartiblash kunlarni aralashtirib yuborardi).</para>
    ///
    /// <para>⚠️ Jamlanma va kesimlar <b>BUTUN topilma</b> bo'yicha hisoblanadi, ro'yxatning
    /// ko'rinadigan qismidan emas: chegara (<see cref="IgConst.AdLeadsPageSize"/>) tufayli
    /// jadval ostidagi son ro'yxatdan kichik chiqib, "raqamlar to'g'ri kelmayapti" bo'lardi
    /// (`books.md` dagi bir xil saboq).</para>
    /// </summary>
    /// <param name="status">`all` (default) · `ok` (lid yaratilgan) · `failed` (xato bilan).</param>
    [HttpGet("ads/leads")]
    public async Task<ActionResult<IgAdLeadListDto>> AdLeads(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? q,
        [FromQuery] string? status, [FromQuery] string? campaign, [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var query = db.IgAdLeads.AsNoTracking().AsQueryable();

        // Kampaniya bo'yicha filtr — "Reklama statistikasi" jadvalidagi «Lidlarni ko'rish →»
        // havolasi shu parametr bilan keladi. Usiz havola ochilar, lekin ro'yxat FILTRLANMASDAN
        // to'liq chiqar va foydalanuvchi "nega hammasi ko'rinyapti" deb chalkashardi.
        if (!string.IsNullOrWhiteSpace(campaign))
            query = query.Where(l => l.CampaignId == campaign);

        if (!string.IsNullOrWhiteSpace(from)) query = query.Where(l => l.CreatedTime.CompareTo(from) >= 0);
        // `to` KUN sifatida keladi — kunning oxirigacha cho'ziladi (audit.md §5 bilan bir xil).
        if (!string.IsNullOrWhiteSpace(to)) query = query.Where(l => l.CreatedTime.CompareTo(to + "T23:59:59") <= 0);

        if (string.Equals(status, "ok", StringComparison.Ordinal)) query = query.Where(l => l.LeadId != "");
        else if (string.Equals(status, "failed", StringComparison.Ordinal)) query = query.Where(l => l.Error != "");

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Qidiruv `ToLower().Contains` bilan — provayderga bog'liq emas (`ILike` SQLite
            // testlarida ishlamasdi, audit.md §5).
            var needle = q.Trim().ToLower();
            query = query.Where(l =>
                l.FullName.ToLower().Contains(needle)
                || l.Phone.Contains(needle)
                || l.FormName.ToLower().Contains(needle)
                || l.CampaignName.ToLower().Contains(needle));
        }

        var total = await query.CountAsync(ct);
        var withLead = await query.CountAsync(l => l.LeadId != "", ct);
        var newLeads = await query.CountAsync(l => l.IsNewLead, ct);
        var failed = await query.CountAsync(l => l.Error != "", ct);

        // ⚠️ Guruhlash ANONIM turga proyeksiya qilinadi va `record` ga XOTIRADA aylantiriladi:
        // konstruktorli proyeksiya EF tarjimasida provayderga bog'liq bo'lib qoladi, anonim
        // tur esa har joyda ishlaydi (testlar SQLite'da, prod Npgsql'da).
        var byForm = (await query
                .Where(l => l.FormName != "")
                .GroupBy(l => l.FormName)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(20).ToListAsync(ct))
            .Select(x => new IgBreakdownDto(x.Key, x.Count)).ToList();

        var byCampaign = (await query
                .Where(l => l.CampaignName != "")
                .GroupBy(l => l.CampaignName)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(20).ToListAsync(ct))
            .Select(x => new IgBreakdownDto(x.Key, x.Count)).ToList();

        var pageNo = page < 1 ? 1 : page;
        var items = await query
            .OrderByDescending(l => l.CreatedTime)
            .Skip((pageNo - 1) * IgConst.AdLeadsPageSize)
            .Take(IgConst.AdLeadsPageSize)
            .ToListAsync(ct);

        var dtos = items.Select(l => new IgAdLeadDto(
            l.Id, l.LeadgenId, l.FullName, l.Phone, l.Email, l.FormName, l.CampaignName,
            l.AdName, l.Platform, l.LeadId, l.IsNewLead, l.CreatedTime, l.ReceivedAt, l.Error)).ToList();

        return new IgAdLeadListDto(
            dtos, total, pageNo, IgConst.AdLeadsPageSize,
            new IgAdTotalsDto(total, withLead, newLeads, failed), byForm, byCampaign);
    }

    /// <summary>
    /// XATO bilan qolgan lidni QAYTA olish. Eng ko'p uchraydigan holat: lid kelgan paytda token
    /// hali kiritilmagan yoki muddati tugagan edi — ya'ni ism va telefon olinmay qolgan.
    ///
    /// <para>⚠️ Meta lidni ~90 kun saqlaydi; bundan eskisiga "topilmadi" xatosi qaytadi va
    /// bu foydalanuvchiga OCHIQ aytiladi (jimgina muvaffaqiyat ko'rsatilmaydi).</para>
    /// </summary>
    [HttpPost("ads/leads/{id}/retry")]
    [AdminPerm("marketing.settings")]
    public async Task<IActionResult> RetryAdLead(string id, CancellationToken ct)
    {
        var row = await db.IgAdLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (row is null) return NotFound(new { message = "Reklama lidi topilmadi." });
        if (row.LeadId.Length > 0)
            return BadRequest(new { message = "Bu lid allaqachon CRM'ga qo'shilgan." });

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramLeadAdsEnabled)
            return BadRequest(new { message = "Reklama lidlari moduli o'chirilgan — avval uni yoqing." });

        var page = await db.IgAdPages.FirstOrDefaultAsync(p => p.IsActive, ct);
        if (page is null || page.AccessToken.Length == 0)
            return BadRequest(new { message = "Facebook sahifa ulanmagan — avval Page Access Token kiriting." });

        var (ok, data, err) = await adsApi.FetchLeadAsync(row.LeadgenId, page.AccessToken, ct);
        if (!ok || data is null)
        {
            row.Error = err;
            await db.SaveChangesAsync(ct);
            return BadRequest(new { message = err });
        }

        row.FullName = data.FullName;
        row.Phone = PhoneUtil.Normalize(data.Phone);
        row.Email = data.Email;
        row.RawFieldsJson = data.FieldsJson;
        row.AdName = data.AdName;
        row.CampaignId = data.CampaignId;
        row.CampaignName = data.CampaignName;
        row.Platform = data.Platform;
        if (data.FormId.Length > 0) row.FormId = data.FormId;
        if (data.CreatedTimeIso.Length > 0) row.CreatedTime = data.CreatedTimeIso;
        if (row.FormName.Length == 0)
            row.FormName = await adsApi.FetchFormNameAsync(row.FormId, page.AccessToken, ct);

        var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(db, row, meta.InstagramAdsLeadSource, ct);
        row.LeadId = leadId;
        row.IsNewLead = isNew;
        row.Error = "";

        audit.Record(AuditEntity, row.Id, "update",
            $"Reklama lidi qayta olindi va CRM'ga qo'shildi: {row.FullName} {row.Phone}".TrimEnd());
        await db.SaveChangesAsync(ct);

        return Ok(new { leadId, isNew });
    }
}

// =================================================================================================
//  DTO'LAR — barchasi `record` (loyiha konvensiyasi). ⚠️ Hech qaysisida token/secret YO'Q.
// =================================================================================================

/// <summary>Diagnostika ekrani. Kalitlar faqat BAYROQ sifatida (`*Set`) — qiymat berilmaydi.</summary>
public record IgStatusDto(
    bool Connected, string Username, string Name, string PictureUrl, int TokenDaysLeft,
    bool WebhookSubscribed, string ConnectedAt, string ConnectedBy, string TokenRefreshedAt,
    bool Enabled, bool AutoReplyComments, bool AutoReplyDm,
    bool AppIdSet, bool AppSecretSet, bool VerifyTokenSet, bool GeminiConfigured,
    int KnowledgeCount, int RuleCount, int PendingEvents, int FailedEvents,
    int NeedsOperator, int Unread, int TodayReplies, int DailyLimit,
    string WebhookUrl, string CallbackUrl, string EnvKeyAppSecret, string EnvKeyVerifyToken);

/// <summary>
/// `CenterMeta` dagi Instagram sozlamalari (maxfiy emas).
/// <para>⚠️ Maydon nomlari <b>ataylab</b> <c>CenterMeta</c> dagi nomlar bilan bir xil
/// (<c>instagramEnabled</c>, <c>instagramAutoReplyDm</c> …): sozlamalar formasi shu nomlarni
/// to'g'ridan-to'g'ri o'qiydi va qaytaradi. Qisqartirilgan nom (<c>enabled</c>) berilsa
/// forma barcha bayroqlarni <c>undefined</c> ko'rib, o'chirilgan holda ko'rsatardi.</para>
/// </summary>
public record IgSettingsDto(
    bool InstagramEnabled, bool InstagramAutoReplyComments, bool InstagramAutoReplyDm,
    bool InstagramPrivateReplyEnabled, string InstagramAppId, string InstagramAiModel,
    string InstagramLeadSource, bool InstagramNotifyTelegram,
    int InstagramReplyDelaySeconds, int InstagramDailyReplyLimit, string InstagramGreeting,
    bool InstagramLeadAdsEnabled, string InstagramAdsLeadSource,
    // ⚠️ Bu ikki bayroq SHU YERDA bo'lishi SHART. Ular `CenterMeta` da bor edi, lekin hech qaysi
    // `PUT` da o'zlashtirilmasdi — ya'ni modullarni UI'dan YOQIB BO'LMASDI va admin ularni faqat
    // bazadan qo'lda yoqishi mumkin edi. Sozlamalar sahifasi aynan shu DTO'ni o'qiydi.
    bool InstagramAdsStatsEnabled, bool InstagramPublishEnabled);

/// <summary>
/// QO'LDA ulash uchun Instagram Login tokeni (<c>POST /connect-token</c>).
///
/// <para>⚠️ <b>Bo'sh yuborilsa mavjud token SAQLANMAYDI</b> — <see cref="IgAdPagePayload"/>
/// dagidan farqi shu. Sabab: u yerda forma Page ID va tokenni birga tahrirlaydi, bu yerda esa
/// yagona maydon bor va uni bo'sh yuborish "akkauntni tokensiz ulash" degani bo'lardi.</para>
/// </summary>
public record IgConnectTokenPayload(string? AccessToken);

/* ---------------- REKLAMA LIDLARI (Meta Lead Ads) ---------------- */

/// <summary>Reklama lidlari diagnostikasi. ⚠️ Page Access Token QIYMATI yo'q — faqat
/// <paramref name="TokenSet"/> bayrog'i.</summary>
public record IgAdStatusDto(
    bool Enabled, bool PageConnected, string PageId, string PageName, bool TokenSet,
    bool LeadgenSubscribed, string ConnectedAt, string ConnectedBy, string LastLeadAt,
    string LastError, bool AppSecretSet, bool VerifyTokenSet,
    int LeadsTotal, int LeadsToday, int Leads30Days, int LeadsFailed,
    string LeadgenUrl, string EnvKeyAppSecret, string EnvKeyVerifyToken);

/// <summary><paramref name="AccessToken"/> BO'SH yuborilsa mavjud token saqlanadi (forma tokenni
/// hech qachon ko'rsatmaydi, ya'ni faqat Page ID tahrirlansa uni qayta yozdirish shart emas).</summary>
public record IgAdPagePayload(string? PageId, string? AccessToken);

/// <summary><paramref name="LeadId"/> bo'sh = CRM lidi yaratilmagan (sababi <paramref name="Error"/> da).</summary>
public record IgAdLeadDto(
    string Id, string LeadgenId, string FullName, string Phone, string Email,
    string FormName, string CampaignName, string AdName, string Platform,
    string LeadId, bool IsNewLead, string CreatedTime, string ReceivedAt, string Error);

/// <summary>⚠️ Sonlar BUTUN topilma bo'yicha (ro'yxat sahifalangani uchun undan qo'shib
/// chiqarish noto'g'ri bo'lardi).</summary>
public record IgAdTotalsDto(int Total, int WithLead, int NewLeads, int Failed);

public record IgAdLeadListDto(
    List<IgAdLeadDto> Items, int Total, int Page, int PageSize, IgAdTotalsDto Totals,
    List<IgBreakdownDto> ByForm, List<IgBreakdownDto> ByCampaign);

/// <summary><paramref name="RedirectUri"/> ham qaytadi — Meta'dagi "OAuth redirect URI" bilan
/// AYNAN bir xil bo'lishi kerak va admin uni nusxa oladi.</summary>
public record IgConnectUrlDto(string Url, string RedirectUri);

/// <summary>
/// Inbox qatori.
///
/// <para>🔴 <paramref name="AdId"/> · <paramref name="AdCampaignId"/> ·
/// <paramref name="AdCampaignName"/> — <b>TAXMINIY</b> reklama atributsiyasi (E3):
/// izoh kelgan media <c>IgAdEntity.CreativeStoryId</c> bilan solishtirib TIKLANADI.
/// Boostlangan postda ishlaydi, "dark post" va dinamik reklamada ishlamaydi, ya'ni
/// <b>bo'sh qiymat "reklamadan kelmagan" degani EMAS</b> — "aniqlanmadi" degani.
/// Shu sabab UI'da chip HAR DOIM "taxminiy" deb belgilanadi (<c>IgAdAttribution</c> izohi).</para>
///
/// <para><paramref name="AdCampaignName"/> — <c>IgAdEntity</c> dan olingan nom; topilmasa
/// id'ning O'ZI (sun'iy "Noma'lum" yozilmaydi). Ro'yxatda nomlar <b>bitta</b> so'rovda
/// olinadi — qator boshiga so'rov (N+1) YO'Q.</para>
/// </summary>
public record IgConversationDto(
    string Id, string IgUserId, string Username, string Status, string LastInboundAt,
    string LastOutboundAt, string LastMessageText, int MessageCount, bool Unread,
    bool NeedsOperator, string NeedsOperatorReason, string Language, string Intent,
    int LeadScore, string? LeadId, string CreatedAt, string OperatorPausedUntil,
    string AdId, string AdCampaignId, string AdCampaignName);

public record IgConversationListDto(List<IgConversationDto> Items, int Total, int Page, int PageSize);

/// <summary><paramref name="DmWindowOpen"/> — 24 soatlik oyna ochiqmi; UI javob maydonini
/// shunga qarab bloklaydi (operator bekorga yozib o'tirmasin).</summary>
public record IgConversationDetailDto(
    IgConversationDto Conversation, List<IgMessageDto> Messages, IgLeadBriefDto? Lead, bool DmWindowOpen);

public record IgMessageDto(
    string Id, string ConversationId, string Direction, string Channel, string Text,
    string ActorName, bool IsAi, string AiIntent, int AiScore, string Error,
    string CommentId, string MediaId, string IgMessageId, string CreatedAt);

public record IgLeadBriefDto(string Id, string FullName, string Phone, string Source, string Stage, string CreatedAt);

public record IgReplyPayload(string? Text);

public record IgCreateLeadPayload(string? Name, string? Phone, string? Interest, string? Note);

public record IgRuleDto(
    string Id, string Title, string Keywords, string Channel, string ReplyText,
    bool StopAi, bool IsActive, int Order, int MatchCount, string CreatedAt);

public record IgRulePayload(
    string Title, string Keywords, string? Channel, string ReplyText,
    bool StopAi, bool IsActive, int Order);

/// <param name="SourceFile">Qaysi Word hujjatidan kelgani; qo'lda yozilgan bo'lakda BO'SH
/// (<c>InstagramController.Knowledge.cs</c>).</param>
public record IgKnowledgeDto(
    string Id, string Title, string Content, int Order, bool IsActive, string UpdatedAt, string UpdatedBy,
    string SourceFile);

public record IgKnowledgeItemPayload(string? Id, string Title, string? Content, bool IsActive);

public record IgKnowledgeBulkPayload(List<IgKnowledgeItemPayload>? Items);

public record IgTestAgentPayload(string? Channel, string? Message, string? Username, string? MediaCaption);

/// <summary>Sinov natijasi. <paramref name="Ok"/> — AI javob berdimi; <c>false</c> bo'lsa
/// <paramref name="Error"/> da o'zbekcha sabab bo'ladi (HTTP baribir 200).</summary>
public record IgTestAgentDto(
    bool Ok, string Reply, string Language, string Intent, int LeadScore, bool IsHotLead,
    bool MoveToDm, bool EscalateToHuman, string LeadName, string LeadContact,
    string LeadProductInterest, string LeadSummary, bool WouldCreateLead, string Error);

public record IgSimulatePayload(string? Kind, string? Text, string? Username, string? SenderId);

/// <summary><paramref name="RawLength"/> — xom JSON hajmi (payloadning o'zi ro'yxatga
/// chiqarilmaydi: unda begona odamning xabar matni bor).</summary>
public record IgEventDto(
    string Id, string EventKey, string Status, int Attempts, string Error,
    string ReceivedAt, string ProcessedAt, int RawLength);

public record IgDailyDto(string Date, int Events, int Inbound, int Replies, int Leads, int Hot);

/// <summary>Eng ko'p ishlagan qoida. <paramref name="Id"/> ATAYIN qaytadi — ro'yxatdan
/// qoidaning o'ziga o'tish uchun (nom takrorlanishi mumkin, id yo'q).</summary>
public record IgTopRuleDto(string Id, string Title, int Count);

public record IgTotalsDto(
    int Events, int Inbound, int Replies, int AiReplies, int FailedReplies,
    int Conversations, int Leads, int Hot, int Escalations, int FailedEvents);

public record IgBreakdownDto(string Key, int Count);

public record IgAnalyticsDto(
    string From, string To, List<IgDailyDto> Daily, IgTotalsDto Totals,
    List<IgBreakdownDto> ByIntent, List<IgBreakdownDto> ByLanguage,
    List<IgBreakdownDto> ByChannel, List<IgTopRuleDto> TopRules);
