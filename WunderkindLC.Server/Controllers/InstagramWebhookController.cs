using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// INSTAGRAM — OCHIQ (login talab qilmaydigan) kirish nuqtalari. Uchta marshrut bor va uchalasi
/// ham <b>Meta tomonidan</b> chaqiriladi, ya'ni ularga bizning JWT'imiz kelmaydi:
/// <list type="bullet">
///   <item><c>GET /webhook</c> — Meta manzilni ro'yxatga olayotganda yuboradigan tasdiq (verify);</item>
///   <item><c>POST /webhook</c> — izoh/DM hodisalari (imzo bilan);</item>
///   <item><c>GET /callback</c> — OAuth (akkauntni ulash) qaytish manzili.</item>
/// </list>
///
/// <para><b>⚠️ ENG MUHIM QOIDA — 5 SONIYA.</b> Meta webhook javobini ~5 soniyada kutadi; kechiksa
/// hodisani muvaffaqiyatsiz deb belgilaydi, 36 soat davomida qayta yuboradi va takrorlansa
/// webhookni butunlay o'chirib qo'yishi mumkin. AI chaqiruvi esa bundan uzoq. Shuning uchun bu
/// yerda <b>HECH QANDAY</b> AI/Graph/HTTP ishi bajarilmaydi: imzo tekshiriladi, xom JSON
/// <see cref="IgWebhookEvent"/> navbatiga yoziladi va DARHOL 200 qaytariladi. Haqiqiy ish —
/// <c>InstagramWorkerService</c> + <c>InstagramPipeline</c> da.</para>
///
/// <para><b>⚠️ FAIL-CLOSED.</b> <c>INSTAGRAM_APP_SECRET</c> bo'sh bo'lsa imzoni tekshirib
/// bo'lmaydi va so'rov RAD ETILADI (403). "Sozlanmagan bo'lsa o'tkazib yuborish" varianti
/// ATAYIN yo'q: u endpointni butunlay himoyasiz qoldirardi.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/instagram")]
public class InstagramWebhookController(
    AppDbContext db,
    InstagramApi api,
    // FAQ tugmalarini (ice breakers) Meta'ga sinxronlash — YAGONA manba (§22). OAuth callback
    // yangi akkaunt ulanganda uni chaqiradi; `InstagramController` (connect-token/SaveSettings)
    // AYNAN shu servisdan foydalanadi, ya'ni mantiq ikki joyda ayri ketmaydi.
    InstagramFaqSync faqSync,
    AuditService audit,
    ILogger<InstagramWebhookController> logger) : ControllerBase
{
    /// <summary>Audit yozuvlaridagi yagona `EntityType` (`AuditSections`: `marketing`).</summary>
    private const string AuditEntity = "Instagram";


    /// <summary>Xom body uchun yuqori chegara — Meta payloadlari kichik (bir necha KB).
    /// Chegarasiz o'qish ochiq endpointda xotira hujumiga yo'l qoldirardi.</summary>
    private const int MaxBodyBytes = 512 * 1024;

    /// <summary>`EventKey` ustuni uzun bo'lib ketmasin — bundan uzun kalit hash bilan qisqartiriladi.</summary>
    private const int MaxEventKeyLength = 400;

    // =============================================================================================
    //  GET /webhook — Meta tasdig'i (verify)
    // =============================================================================================

    /// <summary>
    /// Meta webhook manzilini ro'yxatga olayotganda (va vaqti-vaqti bilan) yuboradi.
    /// Mos kelsa <c>hub.challenge</c> <b>xom matn</b> sifatida qaytariladi (JSON EMAS,
    /// qo'shtirnoqsiz), aks holda 403.
    ///
    /// <para>⚠️ Parametr nomlarida NUQTA bor (<c>hub.mode</c>) — ASP.NET model binding'i uni
    /// argumentga bog'lay olmaydi (nuqta ichma-ich obyekt deb qaraladi), shuning uchun
    /// <c>Request.Query[...]</c> dan QO'LDA o'qiladi.</para>
    /// </summary>
    [HttpGet("webhook")]
    public IActionResult Verify()
    {
        var mode = Request.Query["hub.mode"].ToString();
        var token = Request.Query["hub.verify_token"].ToString();
        var challenge = Request.Query["hub.challenge"].ToString();

        var ok = InstagramSignature.VerifyChallenge(mode, token, challenge, AppSecrets.InstagramVerifyToken);
        if (ok is null)
        {
            logger.LogWarning("[instagram] webhook verify rad etildi (mode: {Mode})", mode);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        logger.LogInformation("[instagram] webhook verify muvaffaqiyatli");
        return Content(ok, "text/plain");
    }

    // =============================================================================================
    //  POST /webhook — hodisa qabul qilish (faqat NAVBATGA yoziladi)
    // =============================================================================================

    /// <summary>
    /// Izoh/DM hodisasi. Ketma-ketlik QAT'IY:
    /// <list type="number">
    ///   <item>xom body <b>BAYT</b> sifatida o'qiladi (deserializatsiyadan OLDIN — HMAC aynan
    ///     shu baytlardan hisoblanadi; qayta seriyalangan JSON'da bo'sh joy/kalit tartibi
    ///     o'zgarib, imzo HECH QACHON mos kelmasdi);</item>
    ///   <item>imzo tekshiriladi — xato bo'lsa 403 va body umuman ishlanmaydi;</item>
    ///   <item>navbatga (<see cref="IgWebhookEvent"/>, <c>pending</c>) yoziladi;</item>
    ///   <item>darhol 200.</item>
    /// </list>
    ///
    /// <para><b>Dedup:</b> <c>EventKey</c> ustunida UNIKAL indeks bor. Meta bir hodisani bir necha
    /// marta yuborishi mumkin (kafolat "at-least-once"); takror kelsa <c>DbUpdateException</c>
    /// ushlanadi va baribir <b>200</b> qaytariladi — aks holda Meta uni "yetkazilmadi" deb
    /// hisoblab, 36 soat davomida qayta yuboraverardi.</para>
    /// </summary>
    [HttpPost("webhook")]
    [EnableRateLimiting("instagram-webhook")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // (1) XOM BAYTLAR — hech qanday parse/trim/qayta seriyalashsiz.
        var raw = await ReadBodyAsync(ct);
        if (raw is null)
        {
            logger.LogWarning("[instagram] webhook body juda katta yoki o'qib bo'lmadi");
            return BadRequest();
        }

        // (2) IMZO. Bo'sh secret → `InstagramSignature.Verify` false qaytaradi (fail-closed).
        var header = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!InstagramSignature.Verify(raw, header, AppSecrets.InstagramAppSecret))
        {
            // ⚠️ SABAB nomlanadi: «mos kelmadi» ning olti xil sababi bor va ular butunlay
            // boshqa ishni talab qiladi (`InstagramSignature.DescribeFailure`). Yalang matn
            // bilan har safar tekshiruv noldan boshlanardi.
            logger.LogWarning(
                "[instagram] webhook imzosi mos kelmadi — so'rov rad etildi. Sabab: {Reason} "
                + "(obyekt: {Object}, body: {Bytes} bayt, UA: {UserAgent})",
                InstagramSignature.DescribeFailure(
                    raw, header, AppSecrets.InstagramAppSecret, AppSecrets.MetaAppSecret),
                RejectedObjectType(raw),
                raw.Length, ShortUa(Request.Headers.UserAgent.ToString()));
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var json = Encoding.UTF8.GetString(raw);
        LogPolicyEnforcement(json, "instagram");

        // (3) NAVBATGA. Hodisa kaliti qayta ishlashdan OLDIN kerak (unikal indeks dedupni shu
        // yerda, bitta INSERT bilan bajaradi — alohida "bormi?" so'rovi poygaga ochiq bo'lardi).
        var ourIgUserId = await db.IgAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => a.IgUserId)
            .FirstOrDefaultAsync(ct) ?? "";

        await EnqueueAsync(json, raw, ourIgUserId, "instagram", ct);

        // (4) DARHOL 200 — qolgan ishni fon xizmati bajaradi.
        return Ok();
    }

    // =============================================================================================
    //  GET|POST /leadgen — REKLAMA LIDLARI (Meta Lead Ads)
    // =============================================================================================

    /// <summary>
    /// Reklama lidlari uchun ALOHIDA webhook manzili.
    ///
    /// <para><b>Nega alohida?</b> Meta konsolida callback URL <b>obyekt turi bo'yicha</b>
    /// sozlanadi: izoh/DM <c>instagram</c> obyektidan, reklama lidi esa <c>page</c> obyektidan
    /// keladi. Ular BOSHQA Meta ilovasida bo'lishi ham mumkin — u holda app secret ham, verify
    /// token ham boshqa bo'ladi va bitta manzil bilan ikkalasini ham tekshirib bo'lmasdi.
    /// Bitta ilova ishlatilsa <see cref="AppSecrets.MetaAppSecret"/> Instagram kalitiga
    /// qaytadi, ya'ni admin uchun hech narsa o'zgarmaydi.</para>
    ///
    /// <para>⚠️ Eski <c>/webhook</c> manzili ham <c>page</c> payloadini QABUL QILADI (pastdagi
    /// <see cref="BuildEventKey"/> leadgen kalitini ham quradi) — admin ikkala obyektni bitta
    /// manzilga ulab qo'ysa hodisa jimgina yo'qolmasin.</para>
    /// </summary>
    [HttpGet("leadgen")]
    public IActionResult VerifyLeadgen()
    {
        var mode = Request.Query["hub.mode"].ToString();
        var token = Request.Query["hub.verify_token"].ToString();
        var challenge = Request.Query["hub.challenge"].ToString();

        var ok = InstagramSignature.VerifyChallenge(mode, token, challenge, AppSecrets.MetaVerifyToken);
        if (ok is null)
        {
            logger.LogWarning("[leadgen] webhook verify rad etildi (mode: {Mode})", mode);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        logger.LogInformation("[leadgen] webhook verify muvaffaqiyatli");
        return Content(ok, "text/plain");
    }

    /// <summary>
    /// Reklama formasi to'ldirilgani haqidagi hodisa. Ketma-ketlik <c>POST /webhook</c> bilan
    /// AYNAN bir xil: xom baytlar → imzo (fail-closed) → navbat → darhol 200. Og'ir ish
    /// (Graph so'rovi, lid yaratish) fon xizmatida — Meta 5 soniya kutadi.
    ///
    /// <para>⚠️ Payloadda mijozning ismi ham, telefoni ham YO'Q — faqat <c>leadgen_id</c>.
    /// Ma'lumot keyin Page tokeni bilan olinadi.</para>
    /// </summary>
    [HttpPost("leadgen")]
    [EnableRateLimiting("instagram-webhook")]
    public async Task<IActionResult> ReceiveLeadgen(CancellationToken ct)
    {
        var raw = await ReadBodyAsync(ct);
        if (raw is null)
        {
            logger.LogWarning("[leadgen] webhook body juda katta yoki o'qib bo'lmadi");
            return BadRequest();
        }

        var header = Request.Headers["X-Hub-Signature-256"].ToString();
        if (!InstagramSignature.Verify(raw, header, AppSecrets.MetaAppSecret))
        {
            logger.LogWarning(
                "[leadgen] webhook imzosi mos kelmadi — so'rov rad etildi. Sabab: {Reason} "
                + "(obyekt: {Object}, body: {Bytes} bayt, UA: {UserAgent})",
                InstagramSignature.DescribeFailure(
                    raw, header, AppSecrets.MetaAppSecret, AppSecrets.InstagramAppSecret),
                RejectedObjectType(raw),
                raw.Length, ShortUa(Request.Headers.UserAgent.ToString()));
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var json = Encoding.UTF8.GetString(raw);
        LogPolicyEnforcement(json, "leadgen");

        await EnqueueAsync(json, raw, ourIgUserId: "", source: "leadgen", ct: ct);

        return Ok();
    }

    /// <summary>
    /// Hodisani navbatga yozadi — <b>ikkala</b> webhook marshruti uchun yagona yo'l.
    ///
    /// <para><b>Dedup IKKI QATLAM:</b> (1) tez yo'l — kalit bazada bormi; (2) KAFOLAT —
    /// <c>EventKey</c> ustunidagi UNIKAL indeks.</para>
    ///
    /// <para>⚠️ (1) ni (2) ning O'RNIGA tushunmang: bir vaqtda kelgan ikki bir xil webhook
    /// tekshiruvdan ikkalasi ham o'tib ketishi mumkin va takrorni FAQAT indeks to'xtatadi.
    /// Tez yo'l XATTI-HARAKAT uchun emas, <b>LOG</b> uchun qo'shilgan: Meta takroriy
    /// yuborishlarni soniya/daqiqa oralig'ida qiladi, ya'ni ular deyarli har doim shu yerda
    /// to'xtaydi.</para>
    ///
    /// <para>🔴 <b>Nega bu muhim (2026-08-23, prodda o'lchangan):</b> rad etilgan INSERT uchun
    /// EF Core <c>fail:</c> darajasida <b>~60 qatorlik stack trace</b> yozadi (
    /// <c>Database.Command[20102]</c> + <c>Update[10000]</c>). Meta'ning bir nechta test
    /// hodisasi shu tarzda logni to'ldirib yuborgan va haqiqiy ogohlantirishlar ko'rinmay
    /// qolgan edi. Buni EF log darajasini tushirish bilan yechib BO'LMAYDI — u holda
    /// haqiqiy baza xatolari ham yashirinardi; shuning uchun yechim istisnoning O'ZINI
    /// kamaytirish.</para>
    /// </summary>
    private async Task EnqueueAsync(
        string json, byte[] raw, string ourIgUserId, string source, CancellationToken ct)
    {
        var key = BuildEventKey(json, raw, ourIgUserId);

        if (await db.IgWebhookEvents.AsNoTracking().AnyAsync(e => e.EventKey == key, ct))
        {
            logger.LogInformation("[{Source}] takroriy webhook hodisasi o'tkazib yuborildi", source);
            return;
        }

        db.IgWebhookEvents.Add(new IgWebhookEvent
        {
            EventKey = key,
            RawJson = json,
            Status = IgConst.EvPending,
            ReceivedAt = AppClock.Iso(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // POYGA: tekshiruvdan keyin, INSERT dan oldin boshqa nusxa yozib ulgurdi.
            // Kafolat shu yerda — unikal indeks. Normal holat, xato EMAS.
            db.ChangeTracker.Clear();
            logger.LogInformation(
                "[{Source}] takroriy webhook hodisasi (poyga) o'tkazib yuborildi", source);
        }
    }

    /// <summary>
    /// Dedup kaliti. Odatda bitta POST ichida bitta hodisa keladi — u holda kalit AYNAN
    /// <c>comment_id</c> / <c>mid</c> bo'ladi (barqaror, Meta beradi). Bir necha hodisa bo'lsa
    /// ular birlashtiriladi, juda uzayib ketsa hash'ga aylantiriladi.
    ///
    /// <para>Hodisa umuman ajratilmasa (buzuq JSON, e'tiborsiz maydon yoki O'ZIMIZ yozgan izoh —
    /// parser uni tashlaydi) kalit sifatida <b>xom bodyning hash'i</b> ishlatiladi: u ham
    /// deterministik, ya'ni Meta o'sha payloadni qayta yuborsa takror yozilmaydi.</para>
    /// </summary>
    private static string BuildEventKey(string json, byte[] raw, string ourIgUserId)
    {
        var keys = new List<string>();
        try
        {
            foreach (var e in InstagramEventParser.Parse(json, ourIgUserId))
                if (!string.IsNullOrWhiteSpace(e.EventKey)) keys.Add(e.EventKey);

            // REKLAMA LIDI — kalit `leadgen:{id}`. Bu yerda ATAYIN ikkala parser ham
            // chaqiriladi: manzil qaysi biri bo'lishidan qat'i nazar (admin ikkala obyektni
            // bitta URL'ga ulashi mumkin) kalit BARQAROR bo'lishi kerak. Xom bodyning hash'iga
            // qaytilsa Meta qayta yuborgan (vaqti boshqacha) payload YANGI hodisa bo'lib
            // tushardi va bitta mijoz uchun ikkinchi lid ochilardi.
            foreach (var e in MetaLeadgenParser.Parse(json))
                if (!string.IsNullOrWhiteSpace(e.EventKey)) keys.Add(e.EventKey);
        }
        catch
        {
            // Parser bu yerda hech qachon yiqilmasligi kerak, lekin webhook qabul qilish
            // undan MUHIMROQ — kalitni hash'dan quramiz va hodisa navbatda qoladi.
        }

        if (keys.Count == 0) return "body:" + Sha256Hex(raw);

        var joined = string.Join("|", keys);
        return joined.Length <= MaxEventKeyLength
            ? joined
            : "multi:" + Sha256Hex(Encoding.UTF8.GetBytes(joined));
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// <c>messaging_policy_enforcement</c> kelgan bo'lsa DARHOL logga yozadi (E6.7).
    ///
    /// <para>Haqiqiy ish (avtomatikani pauza qilish + Telegram alert) avvalgidek fon xizmatida —
    /// bu yerda HECH QANDAY og'ir ish bajarilmaydi (5 soniya qoidasi). Lekin bu Meta'ning
    /// cheklovdan OLDINGI yagona ogohlantirishi: navbat kechiksa yoki modul o'chiq bo'lsa u
    /// hech qayerda ko'rinmay qolardi, log esa har doim qoladi.</para>
    ///
    /// <para>Sabab matni logga YOZILMAYDI — u Meta'dan keladigan xom matn; sabab admin ko'radigan
    /// Telegram xabarida va navbat yozuvida bor.</para>
    /// </summary>
    private void LogPolicyEnforcement(string json, string source)
    {
        try
        {
            if (!InstagramEventParser.ContainsPolicyEnforcement(json)) return;
            logger.LogWarning(
                "[{Source}] ⚠️ META SIYOSATI OGOHLANTIRISHI keldi — navbatda qayta ishlanadi "
                + "(avtomatik javoblar pauza qilinadi)", source);
        }
        catch
        {
            // Log yozish webhook qabul qilishni HECH QACHON buzmaydi.
        }
    }

    /// <summary>
    /// Rad etilgan payloadning <c>object</c> turi — log uchun.
    ///
    /// <para><b>Nega imzosi mos kelmagan body'ga umuman qaralyapti?</b> Undan HECH QANDAY
    /// qaror chiqarilmaydi (so'rov baribir 403), faqat <b>bitta oq ro'yxatdagi</b> so'z
    /// logga yoziladi. Busiz "qaysi mahsulot yuboryapti" savoli javobsiz qolardi, tuzatish
    /// esa aynan shunga bog'liq: <c>instagram</c> → Instagram App Secret, <c>page</c> →
    /// so'rov <c>/leadgen</c> manziliga ketishi kerak.</para>
    ///
    /// <para>Xato yutiladi: log yozish webhook qabul qilishni HECH QACHON buzmaydi.</para>
    /// </summary>
    private static string RejectedObjectType(byte[] raw)
    {
        try { return InstagramEventParser.ObjectType(Encoding.UTF8.GetString(raw)); }
        catch { return "noma'lum"; }
    }

    /// <summary>
    /// Log uchun User-Agent: qisqartiriladi va yangi qator olib tashlanadi.
    /// <para>⚠️ Qiymatni TASHQARIDAGI yuboruvchi belgilaydi — uzun yoki ko'p qatorli UA log
    /// faylini to'ldirib, soxta qatorlar "yozib" qo'yishi mumkin edi (log injection).</para>
    /// </summary>
    private static string ShortUa(string? ua)
    {
        var s = (ua ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (s.Length == 0) return "yo'q";
        return s.Length <= 80 ? s : s[..80] + "…";
    }

    /// <summary>
    /// Body'ni to'liq baytlar sifatida o'qiydi (chegaradan oshsa <c>null</c>).
    ///
    /// <para>⚠️ <b>Oqim BOSHIDAN o'qilishi SHART.</b> HMAC aynan Meta yuborgan baytlardan
    /// hisoblanadi; body'ni kimdir (middleware, so'rov logeri, kelajakda qo'shiladigan filtr)
    /// allaqachon o'qib qo'ygan bo'lsa, bu yerda BO'SH massiv qaytardi va imzo <b>hech qachon</b>
    /// mos kelmasdi — tashqaridan bu "Meta tasdiqlamayapti" bo'lib ko'rinadi va sababini topish
    /// juda qiyin. Shuning uchun oqim buferlanadi va pozitsiya nolga qaytariladi.</para>
    /// </summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken ct)
    {
        try
        {
            if (Request.ContentLength is > MaxBodyBytes) return null;

            // Buferlash: oqimni qayta o'qish mumkin bo'lsin (o'zimiz uchun ham, keyingi
            // bosqichlar uchun ham). Allaqachon buferlangan bo'lsa — zararsiz.
            Request.EnableBuffering();
            if (Request.Body.CanSeek) Request.Body.Position = 0;

            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            if (Request.Body.CanSeek) Request.Body.Position = 0;   // keyingi o'quvchiga to'liq qoldiriladi

            if (ms.Length > MaxBodyBytes) return null;
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    // =============================================================================================
    //  GET /callback — OAuth (akkauntni ulash)
    // =============================================================================================

    /// <summary>
    /// Instagram "Allow" bosilgandan keyin foydalanuvchini SHU manzilga qaytaradi.
    /// Oqim: <c>state</c> tekshiruvi → kod → qisqa token → 60 kunlik token → <c>me</c> →
    /// <see cref="IgAccount"/> yoziladi → webhook obunasi → SPA'ga redirect.
    ///
    /// <para>⚠️ Javob har doim <b>redirect</b>: bu manzilni foydalanuvchining BRAUZERI ochadi,
    /// ya'ni u JSON emas, sahifa ko'rishi kerak. Xato bo'lsa URL'ga faqat QISQA KOD qo'shiladi
    /// (<c>?error=token</c>) — Meta'ning to'liq xato matnida token/secret bo'lakchalari bo'lishi
    /// mumkin va ular brauzer tarixiga, proxy loglariga tushib qolardi.</para>
    /// </summary>
    [HttpGet("callback")]
    [EnableRateLimiting("public-lead")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        // Foydalanuvchi Instagram oynasida "Cancel" bosgan bo'lsa Meta `error` bilan qaytaradi.
        if (!string.IsNullOrWhiteSpace(error)) return Back("bekor");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Back("kod");

        // --- state: mavjud, ishlatilmagan, muddati o'tmagan ---
        var st = await db.IgOAuthStates.FirstOrDefaultAsync(s => s.Id == state, ct);
        if (st is null || st.Used) return Back("state");
        if (string.Compare(st.ExpiresAt, AppClock.Iso(), StringComparison.Ordinal) < 0) return Back("muddat");
        st.Used = true;   // bir martalik — natijadan qat'i nazar yopiladi
        await db.SaveChangesAsync(ct);

        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var appId = (meta?.InstagramAppId ?? "").Trim();
        var appSecret = AppSecrets.InstagramAppSecret;
        if (appId.Length == 0 || appSecret.Length == 0) return Back("sozlama");

        var redirectUri = CallbackUrl(Request);

        // --- kod → qisqa token → uzoq token ---
        var (okCode, shortToken, _, errCode) = await api.ExchangeCodeAsync(appId, appSecret, redirectUri, code, ct);
        if (!okCode)
        {
            logger.LogWarning("[instagram] kodni almashtirib bo'lmadi: {Err}", errCode);
            return Back("kod");
        }

        var (okLong, longToken, expiresIn, errLong) = await api.ExchangeLongLivedAsync(appSecret, shortToken, ct);
        if (!okLong)
        {
            logger.LogWarning("[instagram] uzoq muddatli tokenni olib bo'lmadi: {Err}", errLong);
            return Back("token");
        }

        // --- biz kimmiz (cheksiz halqa himoyasi shu ID'ga tayanadi) ---
        var (okMe, igUserId, appScopedId, username, name, pictureUrl, errMe) = await api.MeAsync(longToken, ct);
        if (!okMe || string.IsNullOrWhiteSpace(igUserId))
        {
            logger.LogWarning("[instagram] akkaunt ma'lumotini olib bo'lmadi: {Err}", errMe);
            return Back("profil");
        }

        // --- webhook obunasi (`IgConst.WebhookFields`: comments, messages) ---
        // Obuna bo'lmasa hodisalar UMUMAN kelmaydi, lekin akkaunt baribir saqlanadi: holat
        // Sozlamalar sahifasida qizil ko'rinadi va "Qayta ulash" bilan tuzatiladi.
        var (okSub, errSub) = await api.SubscribeWebhookAsync(longToken, ct);
        if (!okSub) logger.LogWarning("[instagram] webhook obunasi bo'lmadi: {Err}", errSub);

        // --- saqlash: eskisi arxivga, yangisi faol ---
        var now = AppClock.Iso();
        var olds = await db.IgAccounts.Where(a => a.IsActive).ToListAsync(ct);
        foreach (var o in olds)
        {
            o.IsActive = false;
            o.AccessToken = "";   // eski token bazada qolib ketmasin
        }

        var account = new IgAccount
        {
            IgUserId = igUserId,
            // ⚠️ App-scoped id ham SAQLANADI: webhook'da `from.id` ba'zan biri, ba'zan ikkinchisi
            // bo'lib keladi va faqat bittasiga tayanish halqa himoyasini teshadi (§4).
            AppScopedUserId = appScopedId ?? "",
            Username = username ?? "",
            Name = name ?? "",
            ProfilePictureUrl = pictureUrl ?? "",
            AccessToken = longToken,
            TokenExpiresAt = AppClock.Now.AddSeconds(expiresIn > 0 ? expiresIn : 0).ToString("yyyy-MM-ddTHH:mm:ss"),
            TokenRefreshedAt = now,
            WebhookSubscribed = okSub,
            IsActive = true,
            ConnectedAt = now,
            ConnectedBy = st.CreatedBy,
        };
        db.IgAccounts.Add(account);

        // ⚠️ Bu marshrut `[AllowAnonymous]` — so'rovni foydalanuvchining brauzeri olib keladi,
        // lekin JWT'siz, ya'ni `AuditService` aktyorni "Tizim" deb yozadi. Shuning uchun oqimni
        // KIM boshlagani (`IgOAuthState.CreatedBy`) summary matniga qo'lda qo'shiladi.
        // Token/secret bu yerga HECH QACHON yozilmaydi.
        audit.Record(AuditEntity, account.Id, "create",
            $"Instagram akkaunti ulandi: @{account.Username} "
            + $"(ulagan: {(st.CreatedBy.Length > 0 ? st.CreatedBy : "noma'lum")}, "
            + $"webhook obunasi: {(okSub ? "bor" : "YO'Q")})");
        await db.SaveChangesAsync(ct);

        // 🔴 Yangi akkaunt ulandi — modul yoqilgan bo'lsa faol FAQ tugmalarini Meta'ga yuboramiz
        // (aks holda yangi ulangan akkauntda tugmalar ko'rinmasdi, §22). Sinxron
        // SaveChangesAsync'dan KEYIN: u faol akkauntni bazadan o'qiydi. Mantiq YAGONA servisda —
        // `InstagramController` ham (connect-token/SaveSettings) shuni chaqiradi. BEST-EFFORT:
        // yiqilsa callback redirect'i BUZILMAYDI — sabab IgAccount.FaqSyncError da qoladi.
        try
        {
            var faq = await faqSync.SyncAsync(ct);
            if (!faq.Ok)
                logger.LogWarning("[instagram] OAuth ulash: FAQ sinxroni: {Msg}", faq.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[instagram] OAuth ulash: FAQ sinxronida kutilmagan xato");
        }

        logger.LogInformation("[instagram] akkaunt ulandi: @{Username} (obuna: {Sub})", username, okSub);
        return Redirect("/admin/marketing/settings?connected=1");
    }

    /// <summary>Xato bilan SPA'ga qaytish — faqat QISQA KOD (maxfiy tafsilot URL'ga tushmaydi).</summary>
    private RedirectResult Back(string errorCode) =>
        Redirect("/admin/marketing/settings?error=" + Uri.EscapeDataString(errorCode));

    /// <summary>
    /// OAuth qaytish manzili — so'rov HOSTIDAN quriladi (Meta'dagi "OAuth redirect URI" bilan
    /// AYNAN bir xil bo'lishi shart, oxirida `/` YO'Q). Sozlamalar sahifasi ham xuddi shu
    /// funksiyadan olingan manzilni "nusxa olish" tugmasi bilan ko'rsatadi — ikki joyda ayri
    /// yozilsa <c>Invalid redirect_uri</c> xatosi chiqardi.
    /// </summary>
    public static string CallbackUrl(HttpRequest req) => PublicBase(req) + "/api/public/instagram/callback";

    /// <summary>Meta Dashboard'ga kiritiladigan webhook manzili.</summary>
    public static string WebhookUrl(HttpRequest req) => PublicBase(req) + "/api/public/instagram/webhook";

    /// <summary>REKLAMA LIDLARI webhook manzili — Meta'da <b>Page</b> obyektining «Callback URL»
    /// maydoniga qo'yiladi (izoh/DM manzilidan BOSHQA).</summary>
    public static string LeadgenUrl(HttpRequest req) => PublicBase(req) + "/api/public/instagram/leadgen";

    /// <summary>
    /// Tashqi manzil asosi — <b>birinchi navbatda <c>App:Host</c></b> (env <c>APP_HOST</c>),
    /// u bo'sh bo'lsa joriy so'rov hostidan.
    ///
    /// <para><b>Nega kanonik host ustun:</b> Meta'dagi <c>redirect_uri</c> harfma-harf bir xil
    /// bo'lishi shart (§11 tuzoq 10). CRM boshqa nom bilan ochilganda (IP, <c>www.</c>,
    /// vaqtinchalik domen) so'rov hostidan qurilgan manzil boshqa chiqar va ulash
    /// <c>Invalid redirect_uri</c> bilan yiqilardi. Qoida <see cref="InstagramContract.PublicBase"/>
    /// da — sof funksiya, testlangan.</para>
    ///
    /// <para>Cloudflare Tunnel ortida zaxira yo'ldagi sxema <c>X-Forwarded-Proto</c> dan
    /// tiklanadi (Program.cs <c>UseForwardedHeaders</c>).</para>
    /// </summary>
    private static string PublicBase(HttpRequest req)
    {
        var config = req.HttpContext.RequestServices.GetService<IConfiguration>();
        var host = config?["App:Host"] ?? config?["APP_HOST"] ?? "";
        return InstagramContract.PublicBase(host, req.Scheme, req.Host.ToString());
    }
}
