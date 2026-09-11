using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Valyuta offseti QAYERDAN olingani — <see cref="MetaAdAccountInfo.OffsetSource"/> qiymatlari.
///
/// <para><b>Nega bu ko'rinadigan qilingan:</b> noto'g'ri offset butun pul hisobini <b>100
/// barobar</b> buzadi, lekin xato hech qayerda "xato" bo'lib chiqmaydi — raqamlar shunchaki
/// yolg'on bo'lib qoladi. Shuning uchun admin Sozlamalar ekranida summa qaysi manbadagi
/// offset bilan hisoblanayotganini KO'RISHI kerak.</para>
/// </summary>
public static class MetaOffsetSource
{
    /// <summary>Meta javobida <c>currency_offset</c> BOR edi — haqiqat manbai o'sha.</summary>
    public const string Meta = "meta";

    /// <summary>Meta maydonni bermadi (yoki so'rovni rad etdi) — offset bizning
    /// <see cref="MetaCurrency.OffsetOf"/> jadvalimizdan.</summary>
    public const string Table = "jadval";
}

/// <summary>Reklama akkauntining asosiy ma'lumoti.
/// <para><paramref name="CurrencyOffset"/> — valyutaning kasr xonalari.
/// <paramref name="OffsetSource"/> (<see cref="MetaOffsetSource"/>) aytadi: u Meta javobidan
/// olindimi yoki bizning jadvaldan (§17.3). Hujjatlar bu maydon bo'yicha ZID
/// (<c>META-API-MALUMOTNOMA.md</c> §11.1 — bor; <c>KENGAYTIRISH-PROMPT.md</c> §4.2 — yo'q),
/// shuning uchun javob TAXMIN qilinmaydi, ISH VAQTIDA aniqlanadi.</para></summary>
public record MetaAdAccountInfo(
    string Id,
    string Name,
    string Currency,
    int CurrencyOffset,
    string TimezoneName,
    string OffsetSource);

/// <summary>Iyerarxiyaning bitta tuguni: campaign / adset / ad.
/// <para>⚠️ Byudjetlar MINOR unit (<c>5000</c> = 50.00) — Meta shunday beradi.</para></summary>
public record MetaAdEntityRow(
    string Level,
    string ExternalId,
    string ParentId,
    string Name,
    string Status,
    string EffectiveStatus,
    string Objective,
    long DailyBudgetMinor,
    long LifetimeBudgetMinor,
    string StartTime,
    string StopTime,
    string CreativeStoryId);

/// <summary>Bitta kunlik statistika qatori (bitta obyekt × bitta kun × bitta platforma).
/// <para>⚠️ <paramref name="SpendMinor"/> — MINOR unit; Meta uni MATN va MAJOR unit
/// (<c>"312.45"</c>) qilib beradi, o'girish <see cref="MetaCurrency.ParseSpendToMinor"/> da.</para>
/// <para>⚠️ <paramref name="LeadsOnsite"/> va <paramref name="LeadsPixel"/> ALOHIDA — Meta'ning
/// <c>lead</c> turi ikkalasining yig'indisi, uchtasini qo'shish lidlarni ikki marta sanardi.</para></summary>
public record MetaInsightRow(
    string Level,
    string ExternalId,
    string StatDate,
    string Platform,
    long Impressions,
    long Reach,
    long Clicks,
    long LinkClicks,
    long SpendMinor,
    int LeadsOnsite,
    int LeadsPixel,
    int MsgStarted,
    string ActionsJson,
    string AttributionSetting);

/// <summary>Meta kvota sarlavhalarining o'qilgan ko'rinishi (§4.6). Foizlar — 0..100.
/// <para><paramref name="RegainMinutes"/> — <c>estimated_time_to_regain_access</c>: limitga
/// yetilganda SHUNCHA DAQIQA kutish kerak.</para></summary>
public record MetaRateLimitInfo(
    int AppUtilPct,
    int AccountUtilPct,
    string Tier,
    int CallCountPct,
    int TotalTimePct,
    int TotalCpuTimePct,
    int RegainMinutes);

/// <summary>
/// META ADS INSIGHTS (reklama statistikasi) uchun Graph API mijozi.
///
/// <para><b>⚠️ <see cref="MetaAdsApi"/> ga TEGILMAYDI</b> — u LID uchun (Page tokeni,
/// <c>leads_retrieval</c>). Bu yerda esa System User tokeni va <c>ads_read</c> ruxsati kerak.
/// Tokenlarni almashtirib yuborish "OAuthException 190" bo'lib chiqadi va sababini topish
/// qiyin, shuning uchun mijozlar ATAYIN alohida sinflarda — <see cref="IgConst.FbGraphBase"/>
/// esa ikkalasida bir xil.</para>
///
/// <para><b>Uslub — <see cref="MetaAdsApi"/> bilan AYNAN bir xil: ISTISNO OTILMAYDI.</b>
/// Har metod <c>(Ok, …, Error)</c> qaytaradi, <c>Error</c> — foydalanuvchiga ko'rsatiladigan
/// O'ZBEKCHA matn (<c>IgAdAccount.LastError</c> ga yoziladi).</para>
///
/// <para><b>⚠️ Manzil hech qachon LOGGA yozilmaydi</b> — uning ichida <c>access_token</c> bor.
/// Shu sabab <c>appsettings.json</c> da <c>"System.Net.Http.HttpClient": "Warning"</c> turadi
/// va o'zgartirilmaydi.</para>
///
/// <para>DI: <c>builder.Services.AddHttpClient&lt;MetaInsightsApi&gt;();</c></para>
/// </summary>
public sealed class MetaInsightsApi(HttpClient http, ILogger<MetaInsightsApi> logger)
{
    /// <summary>Bitta so'rovni ko'pi bilan shuncha marta urinamiz (1s → 2s → 4s).</summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Sahifalash to'sig'i. Meta <c>paging.next</c> ni cheksiz uzatishi mumkin (masalan noto'g'ri
    /// breakdown bilan), 20 sahifa × 500 qator = 10 000 qator — bir akkauntning oylik statistikasi
    /// uchun bundan ko'pi bo'lmasligi kerak.
    ///
    /// <para>⚠️ Chegara oshsa ma'lumot JIMGINA KESILMAYDI: <c>LogWarning</c> yoziladi VA metod
    /// <c>Ok=false</c> qaytaradi — yarim yuklangan kunni "to'liq" deb yozib qo'yish hisobotni
    /// sekin-asta buzardi va buni hech kim sezmasdi.</para>
    /// </summary>
    public const int MaxPages = 20;

    /// <summary>Bir sahifadagi insights qatorlari.</summary>
    private const int InsightsPageSize = 500;

    /// <summary>Bir sahifadagi iyerarxiya obyektlari.</summary>
    private const int EntityPageSize = 200;

    /* ═════════════════ So'raladigan maydonlar ═════════════════
       ⚠️ Ro'yxatda BO'LMAGAN (yoki eskirgan) maydon so'ralsa Graph BUTUN so'rovni `code 100`
       bilan rad etadi — ya'ni statistika UMUMAN kelmay qo'yadi. Shuning uchun `currency_offset`
       faqat AKKAUNT so'rovida va faqat "rad etilsa maydonsiz qayta so'raymiz" himoyasi bilan
       ishlatiladi (`FetchAccountAsync`); iyerarxiya va insights ro'yxatlariga u TEGMAYDI. */

    private const string AccountFields = "name,currency,timezone_name,account_status";

    /// <summary>Akkaunt maydonlari + <c>currency_offset</c>. Meta uni bersa — offset TAXMIN
    /// qilinmaydi (qarang: <see cref="FetchAccountAsync"/>).</summary>
    private const string AccountFieldsWithOffset = AccountFields + ",currency_offset";

    private const string CampaignFields =
        "id,name,status,effective_status,objective,daily_budget,lifetime_budget,start_time,stop_time";

    private const string AdsetFields =
        "id,name,campaign_id,status,effective_status,daily_budget,lifetime_budget,start_time,end_time";

    private const string AdFields =
        "id,name,adset_id,campaign_id,status,effective_status,creative{id,effective_object_story_id}";

    private const string InsightFields =
        "campaign_id,campaign_name,adset_id,adset_name,ad_id,ad_name,"
        + "impressions,reach,clicks,inline_link_clicks,spend,actions,"
        + "cost_per_action_type,attribution_setting,date_start,date_stop";

    /// <summary>
    /// OXIRGI javobning kvota sarlavhalari (§4.6). Chaqiruvchi buni
    /// <c>IgAdAccount.LastError</c>/logga yozadi yoki keyingi akkauntga o'tishdan oldin
    /// pauza qilish uchun ishlatadi.
    ///
    /// <para>⚠️ Bu HOLAT (state), lekin xavfsiz: typed <c>HttpClient</c> DI'da <b>transient</b>
    /// bo'lib beriladi, ya'ni har servis o'z nusxasini oladi va so'rovlarni KETMA-KET yuboradi
    /// (akkaunt boshiga parallellik 1–2 ta — Meta shuni tavsiya qiladi).</para>
    /// </summary>
    public MetaRateLimitInfo? LastRateLimit { get; private set; }

    /// <summary>
    /// OXIRGI so'rovdagi Meta xato KODI (<c>error.code</c>) va subkodi. Muvaffaqiyatda 0.
    ///
    /// <para><b>Nega kerak:</b> chaqiruvchi (<c>MetaInsightsService</c>) xatoning TURIGA qarab
    /// har xil qaror qabul qiladi — oraliqni qisqartirish, to'xtash yoki Telegram signali.
    /// Ilgari bu qaror faqat XATO MATNIGA qarab qilinardi (<c>Contains("qisqartiring")</c>),
    /// ya'ni <see cref="MapError"/> dagi bitta so'z tahrirlansa mantiq jimgina buzilardi va
    /// backfill hech qachon bo'linmay qolardi. Kod — barqaror shartnoma, matn esa yo'q.</para>
    ///
    /// <para>⚠️ Matn baribir zaxira sifatida qoladi: tarmoq uzilishi yoki timeout'da Meta kodi
    /// umuman bo'lmaydi (0) va qaror matndan olinadi.</para>
    /// </summary>
    public int LastErrorCode { get; private set; }

    /// <inheritdoc cref="LastErrorCode"/>
    public int LastErrorSubcode { get; private set; }

    /* ═════════════════════════ [1] Akkaunt ═════════════════════════ */

    /// <summary>
    /// Reklama akkauntining nomi, valyutasi va VAQT ZONASI
    /// (<c>GET /act_{id}?fields=name,currency,timezone_name,account_status[,currency_offset]</c>).
    ///
    /// <para>⚠️ Vaqt zonasi shu yerda olinadi va saqlanadi: Insights kunlari AKKAUNT zonasida
    /// hisoblanadi. CRM foydalanuvchisi "bugun" deganda Toshkent kunini nazarda tutadi — farq
    /// UI'da tushuntiriladi, lekin buning uchun zona nomi bazada bo'lishi shart.</para>
    ///
    /// <para><b>⚠️ <c>currency_offset</c> — TAXMIN QILINMAYDI, ISH VAQTIDA aniqlanadi.</b>
    /// Meta hujjatlari zid: bir joyda bu maydon Ad Account tugunida bor deyiladi, boshqasida
    /// umuman yo'q deyiladi. Noto'g'ri offset esa butun pul hisobini <b>100 barobar</b> buzadi,
    /// ya'ni "xavfsiz taxmin" ham baribir TAXMIN bo'lib qolardi. Shuning uchun:</para>
    /// <list type="number">
    ///   <item>avval maydon SO'RALADI (<see cref="AccountFieldsWithOffset"/>);</item>
    ///   <item>Meta uni rad etsa (<c>code 100</c> — "nonexisting field") — <b>BIR MARTA</b>
    ///         maydonsiz qayta so'raladi va offset <see cref="MetaCurrency.OffsetOf"/> dan
    ///         olinadi (eski xatti-harakat, ya'ni hech narsa buzilmaydi);</item>
    ///   <item>Meta qiymat bersa — HAQIQAT MANBAI o'sha, lekin u bizning jadval bilan
    ///         solishtiriladi va farq bo'lsa OGOHLANTIRISH logi yoziladi (jadval eskirgan).</item>
    /// </list>
    ///
    /// <para><b>⚠️ Qayta so'rov FAQAT <c>code 100</c> da</b> (<see cref="IsUnknownFieldError"/>):
    /// token (190), ruxsat (200/10) yoki kvota (80000) xatosida takroriy so'rov foydasiz va
    /// ZARARLI — <c>ads_insights</c> kvotasi formulasida <c>− 0.001 × xatolar</c> bor, ya'ni
    /// har bir 4xx kvotani KAMAYTIRADI (§17.5).</para>
    ///
    /// <para>⚠️ Bu <b>bir martalik</b> so'rov (akkaunt ulanganda va sinxronizatsiya boshida
    /// valyuta noma'lum bo'lganda), ya'ni ISSIQ YO'LDA emas — qo'shimcha so'rovning narxi
    /// noto'g'ri pul hisobining narxi oldida hech narsa.</para>
    /// </summary>
    public async Task<(bool Ok, MetaAdAccountInfo? Info, string Error)> FetchAccountAsync(
        string actId, string token, CancellationToken ct)
    {
        var act = MetaInsightsParser.NormalizeAccountId(actId);
        if (act.Length == 0) return (false, null, AccountIdError);
        if (string.IsNullOrWhiteSpace(token)) return (false, null, TokenError);

        var tk = Uri.EscapeDataString(token.Trim());
        string Url(string fields) =>
            $"{IgConst.FbGraphBase}/{act}?fields={Uri.EscapeDataString(fields)}&access_token={tk}";

        var (ok, body, err) = await SendAsync(Url(AccountFieldsWithOffset), ct);

        // ⚠️ AYNAN BITTA qayta so'rov: `IsUnknownFieldError` faqat `code 100` ni tan oladi,
        // va bu shox faqat shu yerda — halqa yoki takroriy tushish imkoni yo'q.
        if (!ok && IsUnknownFieldError(LastErrorCode, LastErrorSubcode))
        {
            logger.LogInformation(
                "Meta `currency_offset` maydonini qabul qilmadi — akkaunt maydonsiz qayta so'raladi, "
                + "offset valyuta kodidan (MetaCurrency) olinadi.");

            (ok, body, err) = await SendAsync(Url(AccountFields), ct);
        }

        if (!ok) return (false, null, err);

        var (info, status) = MetaInsightsParser.ParseAccount(body);
        if (info == null) return (false, null, "Meta javobini o'qib bo'lmadi (kutilmagan format).");

        // Javobda `id` bo'lmasa — biz so'ragan id ishlatiladi (bazadagi kalit shu).
        if (info.Id.Length == 0) info = info with { Id = act };

        // ⚠️ Meta qiymati G'OLIB, lekin JIM emas: bizning jadval bilan farq qilsa u eskirgan
        // degani (yangi valyuta, Meta qoidasi o'zgargan) va buni kelajakda kimdir bilishi kerak.
        if (info.OffsetSource == MetaOffsetSource.Meta)
        {
            var ours = MetaCurrency.OffsetOf(info.Currency);
            if (ours != info.CurrencyOffset)
                logger.LogWarning(
                    "Meta reklama akkaunti valyutasi {Currency}: Meta currency_offset={MetaOffset}, "
                    + "bizning jadval={OurOffset}. Meta qiymati ishlatiladi — MetaCurrency jadvali "
                    + "eskirgan bo'lishi mumkin.",
                    info.Currency, info.CurrencyOffset, ours);
        }

        // 1 = ACTIVE. Boshqasi (o'chirilgan, to'lovi qolgan, ko'rib chiqilmoqda) — statistika
        // bo'sh kelishining eng ko'p uchraydigan sababi, shuning uchun logda ko'rinib tursin.
        if (status != 0 && status != 1)
            logger.LogWarning("Meta reklama akkaunti faol emas (account_status={Status}) — statistika bo'sh kelishi mumkin.", status);

        return (true, info, "");
    }

    /* ═════════════════════════ [2] Iyerarxiya ═════════════════════════ */

    /// <summary>
    /// Kampaniya → ad set → reklama iyerarxiyasi. <b>Uchta alohida so'rov</b>, natija BITTA
    /// ro'yxatda va ATAYIN shu tartibda (ota-onalar birinchi) — chaqiruvchi upsert qilganda
    /// <c>ParentId</c> allaqachon mavjud bo'ladi.
    ///
    /// <para>Bitta so'rov yiqilsa <c>Ok=false</c> qaytadi, LEKIN shu paytgacha yig'ilgan
    /// qatorlar ham beriladi: chaqiruvchi ularni saqlab qo'yishi (ism/holat baribir
    /// yangilanadi) yoki tashlab yuborishi mumkin — qaror unda.</para>
    /// </summary>
    public async Task<(bool Ok, List<MetaAdEntityRow> Rows, string Error)> FetchEntitiesAsync(
        string actId, string token, CancellationToken ct)
    {
        var rows = new List<MetaAdEntityRow>();

        var act = MetaInsightsParser.NormalizeAccountId(actId);
        if (act.Length == 0) return (false, rows, AccountIdError);
        if (string.IsNullOrWhiteSpace(token)) return (false, rows, TokenError);

        var tk = Uri.EscapeDataString(token.Trim());

        var plan = new (string Edge, string Fields, string Level)[]
        {
            ("campaigns", CampaignFields, MetaInsightsParser.LevelCampaign),
            ("adsets",    AdsetFields,    MetaInsightsParser.LevelAdset),
            ("ads",       AdFields,       MetaInsightsParser.LevelAd),
        };

        foreach (var (edge, fields, level) in plan)
        {
            var url = $"{IgConst.FbGraphBase}/{act}/{edge}"
                      + $"?fields={Uri.EscapeDataString(fields)}"
                      + $"&limit={EntityPageSize}"
                      + $"&access_token={tk}";

            var (ok, err) = await ForEachPageAsync(
                url, edge, body => rows.AddRange(MetaInsightsParser.ParseEntities(body, level)), ct);

            if (!ok) return (false, rows, err);
        }

        return (true, rows, "");
    }

    /* ═════════════════════════ [3] Kunlik statistika ═════════════════════════ */

    /// <summary>
    /// Kunlik statistika (<c>GET /act_{id}/insights</c>), <c>level=ad</c> va
    /// <c>time_increment=1</c> bilan.
    ///
    /// <para>⚠️ <c>breakdowns=publisher_platform</c> — Instagram va Facebook xarajatini
    /// ajratishning YAGONA yo'li; alohida "Instagram insights" endpointi Meta'da YO'Q.
    /// Buning narxi: bitta reklama-kun bir necha qator bo'lib keladi (platforma boshiga
    /// bittadan), shuning uchun bazadagi unikal kalitga <c>Platform</c> ham kiradi.</para>
    ///
    /// <para>⚠️ <c>reach</c> platformalar bo'yicha YIG'ILMAYDI (bir odam ikkala platformada
    /// ko'rgan bo'lishi mumkin) — qatorlarni qo'shib "umumiy qamrov" chiqarish noto'g'ri.</para>
    ///
    /// <para><b><paramref name="currencyOffset"/> nega ixtiyoriy:</b> §4.3 dagi imzo uni
    /// ko'zda tutmagan, lekin <c>spend</c> ni MINOR ga o'girish uchun u SHART. Default 2 —
    /// UZS va USD uchun to'g'ri; JPY kabi kasrsiz valyutada chaqiruvchi 0 uzatadi, aks holda
    /// sarf 100 barobar ko'p ko'rinardi.</para>
    /// </summary>
    public async Task<(bool Ok, List<MetaInsightRow> Rows, string Error)> FetchInsightsAsync(
        string actId, string token, string since, string until, CancellationToken ct,
        int currencyOffset = MetaCurrency.DefaultOffset)
    {
        var rows = new List<MetaInsightRow>();

        var act = MetaInsightsParser.NormalizeAccountId(actId);
        if (act.Length == 0) return (false, rows, AccountIdError);
        if (string.IsNullOrWhiteSpace(token)) return (false, rows, TokenError);

        if (!IsDate(since) || !IsDate(until))
            return (false, rows, "Sana formati noto'g'ri — \"yyyy-MM-dd\" bo'lishi kerak.");
        if (string.CompareOrdinal(since, until) > 0)
            return (false, rows, "Sana oralig'i teskari — boshlanish sanasi tugash sanasidan keyin.");

        // ⚠️ time_range — JSON obyekt, shuning uchun TO'LIQ kodlanadi (qo'shtirnoq va qavslar
        // xom holda yuborilsa Graph parametrni umuman o'qimaydi).
        var range = "{\"since\":\"" + since + "\",\"until\":\"" + until + "\"}";

        var url = $"{IgConst.FbGraphBase}/{act}/insights"
                  + $"?level={MetaInsightsParser.LevelAd}"
                  + $"&fields={Uri.EscapeDataString(InsightFields)}"
                  + $"&time_range={Uri.EscapeDataString(range)}"
                  + "&time_increment=1"
                  + "&breakdowns=publisher_platform"
                  + "&action_breakdowns=action_type"
                  + $"&limit={InsightsPageSize}"
                  + $"&access_token={Uri.EscapeDataString(token.Trim())}";

        var (ok, err) = await ForEachPageAsync(
            url, "insights", body => rows.AddRange(MetaInsightsParser.ParseRows(body, currencyOffset)), ct);

        return ok ? (true, rows, "") : (false, rows, err);
    }

    /* ═════════════════════════ Sahifalash ═════════════════════════ */

    /// <summary>
    /// Birinchi manzildan boshlab <c>paging.next</c> bo'yicha yuradi va har sahifani
    /// <paramref name="onPage"/> ga beradi.
    ///
    /// <para>⚠️ <c>paging.next</c> ichida <c>access_token</c> bor — manzil logga TUSHMAYDI va
    /// faqat <c>https://</c> bo'lsa ergashiladi (<see cref="MetaInsightsParser.NextPageUrl(string)"/>).</para>
    /// </summary>
    private async Task<(bool Ok, string Error)> ForEachPageAsync(
        string firstUrl, string what, Action<string> onPage, CancellationToken ct)
    {
        var url = firstUrl;

        for (var page = 1; ; page++)
        {
            var (ok, body, err) = await SendAsync(url, ct);
            if (!ok) return (false, err);

            onPage(body);

            var next = MetaInsightsParser.NextPageUrl(body);
            if (next.Length == 0) return (true, "");

            if (page >= MaxPages)
            {
                logger.LogWarning(
                    "Meta Ads Insights: {What} uchun sahifa chegarasi ({Max}) oshdi — qolgan sahifalar OLINMADI. "
                    + "Sana oralig'ini qisqartiring (masalan 10 kunlik bo'laklarga bo'ling).",
                    what, MaxPages);

                return (false,
                    $"Ma'lumot juda ko'p ({MaxPages} sahifadan oshdi) — sana oralig'ini qisqartiring "
                    + "(masalan 10 kunlik bo'laklarga bo'lib yuklang).");
            }

            url = next;
        }
    }

    /* ═════════════════════════ Transport ═════════════════════════ */

    /// <summary>
    /// Bitta so'rov + qayta urinish (1s → 2s → 4s) va kvota sarlavhalarini o'qish.
    ///
    /// <para>⚠️ Qayta urinish FAQAT VAQTINCHALIK xatolarda. Token (190), ruxsat (200/10) yoki
    /// noto'g'ri parametr (100) da urinish foydasiz, bundan tashqari ZARARLI:
    /// <c>ads_insights</c> kvotasi formulasida <c>− 0.001 × xatolar</c> bor, ya'ni bizning
    /// 4xx xatolarimiz kvotani KAMAYTIRADI.</para>
    ///
    /// <para>⚠️ <c>80000</c> (BUC limiti) ham qayta URINILMAYDI: Meta ochiq aytadi — limitga
    /// yetganda chaqiruvni to'xtatish kerak, davom etilsa blok UZAYADI. Kutish vaqti
    /// (<c>estimated_time_to_regain_access</c>) daqiqalar bilan o'lchanadi, uni HTTP chaqiruvi
    /// ichida kutib turish mumkin emas — xato qaytadi, kunlik worker keyinroq qayta uradi.</para>
    /// </summary>
    private async Task<(bool Ok, string Body, string Error)> SendAsync(string url, CancellationToken ct)
    {
        var delayMs = 1000;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                LastRateLimit = MetaInsightsParser.ParseThrottle(
                    Header(resp, "X-FB-Ads-Insights-Throttle"),
                    Header(resp, "X-Business-Use-Case-Usage"));

                if (resp.IsSuccessStatusCode)
                {
                    LastErrorCode = 0;
                    LastErrorSubcode = 0;
                    return (true, body, "");
                }

                var (code, sub, msg) = ParseError(body);
                LastErrorCode = code;
                LastErrorSubcode = sub;

                // ⚠️ Manzil LOGGA yozilmaydi — unda `access_token` bor.
                logger.LogWarning(
                    "Meta Ads Insights xato ({Status}/{Code}/{Sub}): {Msg} · {Quota}",
                    (int)resp.StatusCode, code, sub, msg, MetaInsightsParser.ThrottleSummary(LastRateLimit));

                if (IsRetryable(resp.StatusCode, code) && attempt < MaxAttempts)
                {
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                    continue;
                }

                return (false, body, MapError((int)resp.StatusCode, code, sub, msg, LastRateLimit));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LastErrorCode = 0; LastErrorSubcode = 0;
                return (false, "", "So'rov bekor qilindi.");
            }
            catch (TaskCanceledException)
            {
                if (attempt < MaxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                LastErrorCode = 0; LastErrorSubcode = 0;
                return (false, "", "Meta javob bermadi (vaqt tugadi) — keyinroq qayta urinamiz.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < MaxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                LastErrorCode = 0; LastErrorSubcode = 0;
                return (false, "", $"Tarmoq xatosi: {ex.Message}");
            }
        }
    }

    private static string Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var values) ? (values.FirstOrDefault() ?? "") : "";

    /// <summary>Qaysi xatoda qayta urinish MA'NOLI (§4.6 jadvali).</summary>
    private static bool IsRetryable(HttpStatusCode status, int code)
    {
        // Doimiy xatolar — hech qachon takrorlanmaydi (kvotani bekorga yemasin).
        if (code is 190 or 200 or 10 or 299 or 100 or 803) return false;

        // BUC/insights limiti — Meta "to'xtat" deydi; kunlik worker keyinroq uradi.
        if (code is 80000 or 80004) return false;

        // App/user/custom limit — qisqa backoff bilan urinib ko'riladi, keyin navbatga qaytadi.
        if (code is 4 or 17 or 32 or 613 or 2) return true;

        return status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    /// <summary>
    /// Meta xato kodi → O'ZBEKCHA matn. Matnlar ATAYIN "nima qilish kerak" bilan tugaydi:
    /// admin bu matnni Sozlamalar sahifasida o'qiydi va texnik kod unga hech narsa demaydi.
    /// </summary>
    private static string MapError(int httpStatus, int code, int sub, string msg, MetaRateLimitInfo? rl)
    {
        // ⚠️ 100/1487534 — "bir so'rovda juda ko'p ma'lumot". Bu YAGONA rate-limit'ga o'xshagan
        // xato, unda kutish YORDAM BERMAYDI: oraliqni qisqartirish kerak.
        if (code == 100 && sub == 1487534)
            return "Bir so'rovda juda ko'p ma'lumot so'raldi — sana oralig'ini qisqartiring "
                 + "(kutish yordam bermaydi).";

        return code switch
        {
            190 => "Meta tokeni yaroqsiz yoki muddati tugagan — Marketing → Sozlamalar bo'limida "
                 + "reklama akkaunti tokenini yangilang.",

            200 or 10 or 299 => "Ruxsat yetishmaydi — ilovada 'ads_read' ruxsati borligini va token "
                 + "egasi shu reklama akkauntiga kira olishini tekshiring.",

            80000 or 80004 => rl is { RegainMinutes: > 0 }
                ? $"Meta statistika so'rovlari chegarasiga yetildi — taxminan {rl.RegainMinutes} daqiqadan "
                  + "keyin avtomatik qayta urinamiz."
                : "Meta statistika so'rovlari chegarasiga yetildi — keyinroq avtomatik qayta urinamiz.",

            4 or 17 or 32 or 613 => "Meta so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.",

            100 => $"Noto'g'ri so'rov parametri: {msg}",

            _ => msg.Length > 0
                ? $"Meta xato ({(code != 0 ? code : httpStatus)}): {msg}"
                : $"Meta xato ({httpStatus}).",
        };
    }

    /// <summary>
    /// Xato "so'ralgan MAYDON yo'q" turidanmi — ya'ni so'rovni maydonsiz QAYTA yuborish
    /// ma'noli-mi?
    ///
    /// <para><b>⚠️ Nega qaror faqat KOD bo'yicha, MATN bo'yicha emas:</b> Meta xato matnini
    /// versiyadan versiyaga va tilga qarab o'zgartiradi ("Tried accessing nonexisting field",
    /// "Unknown fields: …"), ya'ni matn SHARTNOMA emas. Loyihada bu saboq allaqachon bor —
    /// <see cref="LastErrorCode"/> izohi va §17.5 (<c>Classify</c> avval kodga qaraydi). Matnga
    /// tayansak, Meta bir kun boshqacha yozganda offset jimgina jadvaldan olinmay qolardi.</para>
    ///
    /// <para><b>⚠️ FAQAT <c>100</c>:</b> 190 (token), 200/10 (ruxsat), 4/17/32/80000
    /// (kvota) — bularda qayta so'rov foydasiz va kvotani yeydi.</para>
    ///
    /// <para>⚠️ <c>100 + 1487534</c> ("bir so'rovda juda ko'p ma'lumot") CHIQARIB tashlanadi:
    /// u insights so'roviga tegishli va maydonlarga hech qanday aloqasi yo'q.</para>
    /// </summary>
    internal static bool IsUnknownFieldError(int code, int subcode) =>
        code == 100 && subcode != 1487534;

    private static (int Code, int Sub, string Message) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
                return (0, 0, "");

            var code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            var sub = e.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : 0;
            return (code, sub, MetaInsightsParser.Str(e, "message"));
        }
        catch (JsonException) { return (0, 0, ""); }
    }

    private static bool IsDate(string? v) =>
        !string.IsNullOrWhiteSpace(v)
        && DateOnly.TryParseExact(v.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out _);

    private const string AccountIdError =
        "Reklama akkaunti ID noto'g'ri — u 'act_1234567890' ko'rinishida (yoki faqat raqamlar) bo'lishi kerak.";

    private const string TokenError =
        "Reklama akkaunti tokeni kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.";
}
