using System.Globalization;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// META ADS INSIGHTS — javobni o'qishning YAGONA joyi. Butunlay SOF: HTTP ham, DB ham,
/// <c>AppClock</c> ham yo'q (<c>MetaInsightsParserTests</c> bilan qoplangan).
///
/// <para><b>Nega alohida sinf:</b> <see cref="MetaInsightsApi"/> tarmoq bilan band, bu yerda esa
/// PULGA va LIDGA aylanadigan raqamlar hisoblanadi. Ular test ostida bo'lishi shart — bitta
/// noto'g'ri o'qilgan maydon "reklama 2 barobar samarali" degan xulosa berardi va buni
/// hech kim sezmasdi.</para>
///
/// <para><b>⚠️ BARCHA raqamli metrikalar JSON'da MATN</b> (<c>"impressions": "1204"</c>) —
/// har joyda <see cref="CultureInfo.InvariantCulture"/> bilan o'qiladi.</para>
///
/// <para><b>⚠️ ISTISNO OTILMAYDI.</b> Buzuq/bo'sh JSON → bo'sh ro'yxat yoki 0. Sinxronizatsiya
/// fon ishida ketadi va bitta g'alati qator butun kunlik yuklashni yiqitmasligi kerak.</para>
/// </summary>
public static class MetaInsightsParser
{
    /* ═════════════════ action_type konstantalari ═════════════════ */

    /// <summary>Instant Form (IG/FB ichidagi forma) lidi.</summary>
    public const string ActLeadGrouped = "onsite_conversion.lead_grouped";

    /// <summary>Saytdagi piksel <c>Lead</c> hodisasi.</summary>
    public const string ActPixelLead = "offsite_conversion.fb_pixel_lead";

    /// <summary>Yangi yozishma boshlandi (Messenger / IG Direct / WhatsApp).</summary>
    public const string ActMsgStarted = "onsite_conversion.messaging_conversation_started_7d";

    /// <summary>Havola bosilishi.</summary>
    public const string ActLinkClick = "link_click";

    /// <summary>
    /// 🔴 <b>ATAYIN ISHLATILMAYDI — faqat hujjat sifatida turibdi.</b>
    /// <c>lead ≈ onsite_conversion.lead_grouped + offsite_conversion.fb_pixel_lead</c>, ya'ni
    /// uchtasini qo'shsak lidlar IKKI MARTA sanaladi va "lid narxi" ikki barobar arzon
    /// ko'rinadi — bu byudjet qaroriga to'g'ridan-to'g'ri ta'sir qiladigan xato.
    /// Biz <c>LeadsOnsite</c> va <c>LeadsPixel</c> ni ALOHIDA saqlaymiz, UI'da yig'indisini
    /// ko'rsatamiz.
    /// </summary>
    public const string ActLeadTotal = "lead";

    /* ═════════════════ Daraja va platforma ═════════════════ */

    public const string LevelCampaign = "campaign", LevelAdset = "adset", LevelAd = "ad";

    /// <summary>Platforma ajratilmagan (breakdown yo'q) qatorlar uchun qiymat.</summary>
    public const string PlatformAll = "all";

    /* ═════════════════ actions massivi ═════════════════ */

    /// <summary>
    /// Bitta insights qatoridan <paramref name="actionType"/> ning qiymatini oladi.
    ///
    /// <para><b>⚠️ Qiymati 0 bo'lgan <c>action_type</c> massivda UMUMAN BO'LMAYDI</b> — ya'ni
    /// indeks bilan o'qish ("uchinchi element — lidlar") ertami-kechmi boshqa metrikani
    /// lid deb ko'rsatib qo'yardi. Har doim NOM bo'yicha izlanadi, topilmasa 0.</para>
    ///
    /// <para><b>⚠️ <c>action_breakdowns</c> ishlatilganda bitta <c>action_type</c> BIR NECHA
    /// qator bo'lib keladi</b> (har breakdown uchun bittadan) — shuning uchun bu yerda birinchi
    /// moslik qaytarilmaydi, HAMMASI YIG'ILADI.</para>
    ///
    /// <para>⚠️ <c>action_attribution_windows</c> so'ralganda har elementga qo'shimcha kalitlar
    /// qo'shiladi (<c>"7d_click": "27"</c>). Biz FAQAT <c>"value"</c> ni o'qiymiz — u akkauntning
    /// umumiy attribution sozlamasi bo'yicha jami va Ads Manager ekranidagi son bilan mos keladi.</para>
    /// </summary>
    public static int ActionValue(JsonElement row, string actionType)
    {
        if (row.ValueKind != JsonValueKind.Object) return 0;
        if (!row.TryGetProperty("actions", out var actions)) return 0;
        return SumActions(actions, actionType);
    }

    /// <summary>Bazada saqlangan XOM <c>actions</c> massivi (<c>IgAdInsight.ActionsJson</c>) dan
    /// o'qish — kelajakda yangi <c>action_type</c> kerak bo'lsa qayta sinxronizatsiya qilmasdan
    /// hisoblash uchun.</summary>
    public static int ActionValueFromJson(string? actionsJson, string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionsJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(actionsJson);
            return SumActions(doc.RootElement, actionType);
        }
        catch (JsonException) { return 0; }
    }

    private static int SumActions(JsonElement actions, string actionType)
    {
        if (actions.ValueKind != JsonValueKind.Array || string.IsNullOrEmpty(actionType)) return 0;

        var sum = 0m;
        foreach (var a in actions.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(Str(a, "action_type"), actionType, StringComparison.OrdinalIgnoreCase)) continue;
            sum += Dec(a, "value");
        }

        var rounded = Math.Round(sum, MidpointRounding.AwayFromZero);
        if (rounded > int.MaxValue) return int.MaxValue;
        if (rounded < int.MinValue) return int.MinValue;
        return (int)rounded;
    }

    /* ═════════════════ Insights javobi ═════════════════ */

    /// <summary>
    /// Butun insights javobini (<c>{"data":[…],"paging":{…}}</c>) qatorlarga aylantiradi.
    ///
    /// <para><paramref name="currencyOffset"/> — reklama akkaunti valyutasining kasr xonalari
    /// (<see cref="MetaCurrency.OffsetOf"/>). <c>spend</c> MATN va MAJOR unit bo'lgani uchun
    /// aynan shu yerda MINOR ga aylantiriladi (§4.2).</para>
    ///
    /// <para>⚠️ <c>ExternalId</c> yoki <c>date_start</c> topilmagan qator TASHLANADI: ular
    /// unikal kalitning (<c>Level+ExternalId+StatDate+Platform</c>) bir qismi va ularsiz upsert
    /// dublikat yaratardi.</para>
    ///
    /// <para>⚠️ <c>StatDate</c> Meta bergan holicha qoladi — u REKLAMA AKKAUNTI vaqt zonasidagi
    /// kun. Uni markaz zonasiga surish TAQIQLANADI: kun chegarasi surilib, sarf boshqa kunga
    /// tushib qolardi va Ads Manager bilan solishtirganda raqamlar mos kelmasdi.</para>
    /// </summary>
    public static List<MetaInsightRow> ParseRows(string json, int currencyOffset)
    {
        var rows = new List<MetaInsightRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return rows;

            foreach (var r in data.EnumerateArray())
            {
                var row = ReadRow(r, currencyOffset);
                if (row != null) rows.Add(row);
            }
        }
        catch (JsonException) { /* buzuq javob — bo'sh ro'yxat, §"istisno otilmaydi" */ }

        return rows;
    }

    /// <summary>Bitta insights qatori. Mos kelmasa <c>null</c>.</summary>
    internal static MetaInsightRow? ReadRow(JsonElement r, int currencyOffset)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;

        // ⚠️ level=ad so'ralganda javobda campaign_id ham, adset_id ham, ad_id ham bo'ladi —
        // eng ANIQ (eng past) daraja tanlanadi, aks holda barcha qatorlar "campaign" bo'lib
        // ketardi va bitta kampaniyaning 10 ta reklamasi bir-birini yozib yuborardi.
        var (level, externalId) = LevelOf(r);
        if (externalId.Length == 0) return null;

        var statDate = Str(r, "date_start");
        if (statDate.Length == 0) return null;

        var platform = Str(r, "publisher_platform").Trim().ToLowerInvariant();
        if (platform.Length == 0) platform = PlatformAll;

        var actionsJson = r.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array
            ? acts.GetRawText()
            : "";

        return new MetaInsightRow(
            Level: level,
            ExternalId: externalId,
            StatDate: statDate,
            Platform: platform,
            Impressions: Lng(r, "impressions"),
            Reach: Lng(r, "reach"),
            Clicks: Lng(r, "clicks"),
            LinkClicks: Lng(r, "inline_link_clicks"),
            SpendMinor: MetaCurrency.ParseSpendToMinor(Str(r, "spend"), currencyOffset),
            LeadsOnsite: ActionValue(r, ActLeadGrouped),
            LeadsPixel: ActionValue(r, ActPixelLead),
            MsgStarted: ActionValue(r, ActMsgStarted),
            ActionsJson: actionsJson,
            AttributionSetting: Str(r, "attribution_setting"));
    }

    /// <summary>Qator qaysi darajaga tegishli: <c>ad</c> → <c>adset</c> → <c>campaign</c>
    /// tartibida (eng aniqrog'i yutadi).</summary>
    internal static (string Level, string ExternalId) LevelOf(JsonElement r)
    {
        var ad = Str(r, "ad_id");
        if (ad.Length > 0) return (LevelAd, ad);

        var adset = Str(r, "adset_id");
        if (adset.Length > 0) return (LevelAdset, adset);

        var campaign = Str(r, "campaign_id");
        return campaign.Length > 0 ? (LevelCampaign, campaign) : ("", "");
    }

    /* ═════════════════ Sahifalash ═════════════════ */

    /// <summary>
    /// <c>paging.next</c> — keyingi sahifa manzili. Yo'q bo'lsa bo'sh satr.
    ///
    /// <para>⚠️ Faqat <c>https://</c> bilan boshlanadigan MUTLAQ manzil qabul qilinadi: javob
    /// (yoki oradagi proksi) boshqa xostga ishora qilsa, unga ergashish <c>access_token</c> ni
    /// begona serverga yuborish bo'lardi — manzil ichida token bor.</para>
    /// </summary>
    public static string NextPageUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return NextPageUrl(doc.RootElement);
        }
        catch (JsonException) { return ""; }
    }

    internal static string NextPageUrl(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        if (!root.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object) return "";

        var next = Str(paging, "next").Trim();
        if (next.Length == 0) return "";

        return next.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? next : "";
    }

    /* ═════════════════ Iyerarxiya (campaign / adset / ad) ═════════════════ */

    /// <summary>
    /// <c>campaigns</c> / <c>adsets</c> / <c>ads</c> javobini bitta ko'rinishga keltiradi.
    /// <paramref name="level"/> — so'rov QAYSI endpointga ketgani (javobning o'zida daraja
    /// yozilmaydi).
    ///
    /// <para>⚠️ Byudjet maydonlari (<c>daily_budget</c>, <c>lifetime_budget</c>) — allaqachon
    /// MINOR unit ("5000" = 50.00 USD), ya'ni ularga <see cref="MetaCurrency"/> QO'LLANMAYDI.
    /// Aynan shu <c>spend</c> bilan bo'lgan assimetriya eng ko'p xatoga sabab bo'ladi (§4.2).</para>
    ///
    /// <para>⚠️ Ad set'ning tugash vaqti <c>end_time</c> deb ataladi (kampaniyada —
    /// <c>stop_time</c>). Ikkalasi ham o'qiladi.</para>
    /// </summary>
    public static List<MetaAdEntityRow> ParseEntities(string json, string level)
    {
        var rows = new List<MetaAdEntityRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return rows;

            foreach (var e in data.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;

                var id = Str(e, "id");
                if (id.Length == 0) continue;   // id'siz qator upsert qilib bo'lmaydi

                var parent = level switch
                {
                    LevelAd => Str(e, "adset_id").Length > 0 ? Str(e, "adset_id") : Str(e, "campaign_id"),
                    LevelAdset => Str(e, "campaign_id"),
                    _ => "",
                };

                var stop = Str(e, "stop_time");
                if (stop.Length == 0) stop = Str(e, "end_time");

                rows.Add(new MetaAdEntityRow(
                    Level: level,
                    ExternalId: id,
                    ParentId: parent,
                    Name: Str(e, "name"),
                    Status: Str(e, "status"),
                    EffectiveStatus: Str(e, "effective_status"),
                    Objective: Str(e, "objective"),
                    DailyBudgetMinor: Lng(e, "daily_budget"),
                    LifetimeBudgetMinor: Lng(e, "lifetime_budget"),
                    StartTime: InstagramEventParser.ToIso(Str(e, "start_time")),
                    StopTime: InstagramEventParser.ToIso(stop),
                    CreativeStoryId: CreativeStoryId(e)));
            }
        }
        catch (JsonException) { /* buzuq javob — bo'sh ro'yxat */ }

        return rows;
    }

    /// <summary><c>creative{id,effective_object_story_id}</c> — reklama izohlari (E3) shu id
    /// orqali reklamaga bog'lanadi. Creative yo'q bo'lsa bo'sh satr (reklama baribir
    /// saqlanadi — statistika creative'ga bog'liq emas).</summary>
    private static string CreativeStoryId(JsonElement e)
    {
        if (!e.TryGetProperty("creative", out var c) || c.ValueKind != JsonValueKind.Object) return "";
        return Str(c, "effective_object_story_id");
    }

    /* ═════════════════ Akkaunt ═════════════════ */

    /// <summary>
    /// <c>GET /act_{id}?fields=name,currency,timezone_name,account_status[,currency_offset]</c>
    /// javobi.
    ///
    /// <para><c>currency_offset</c> javobda BO'LSA — o'sha ishlatiladi va
    /// <c>OffsetSource = <see cref="MetaOffsetSource.Meta"/></c>; bo'lmasa (yoki qiymati
    /// mantiqsiz bo'lsa) offset <see cref="MetaCurrency.OffsetOf"/> dan olinadi va manba
    /// <see cref="MetaOffsetSource.Table"/> bo'ladi. Ikkala yo'l ham TO'G'RI ishlaydi —
    /// so'rovning o'zi <see cref="MetaInsightsApi.FetchAccountAsync"/> da darvozalangan.</para>
    ///
    /// <para><c>AccountStatus</c> alohida qaytariladi (1 = ACTIVE): u <c>MetaAdAccountInfo</c>
    /// da yo'q, lekin "nega statistika kelmayapti" savoliga ko'pincha javob bo'ladi
    /// (o'chirilgan/to'lovi qolgan akkaunt), shuning uchun chaqiruvchi uni logga yozadi.</para>
    /// </summary>
    public static (MetaAdAccountInfo? Info, int AccountStatus) ParseAccount(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, 0);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, 0);
            if (root.TryGetProperty("error", out _)) return (null, 0);

            var currency = Str(root, "currency").Trim().ToUpperInvariant();
            var (offset, offsetSource) = ReadOffset(root, currency);

            var info = new MetaAdAccountInfo(
                Id: Str(root, "id"),
                Name: Str(root, "name"),
                Currency: currency,
                CurrencyOffset: offset,
                TimezoneName: Str(root, "timezone_name"),
                OffsetSource: offsetSource);

            return (info, (int)Lng(root, "account_status"));
        }
        catch (JsonException) { return (null, 0); }
    }

    /// <summary>
    /// <c>currency_offset</c> — Meta bergan bo'lsa o'shani, aks holda valyuta kodidan.
    ///
    /// <para><b>🔴 Nega qiymat DIAPAZONGA solishtiriladi:</b> maydonning MA'NOSI hujjatlarda
    /// aniq emas. Eskirgan <c>Currency</c> tugunida <c>offset</c> "necha marta bo'lish kerak"
    /// degan KO'PAYTUVCHI edi (<c>100</c>), bizga esa KASR XONALARI SONI (<c>2</c>) kerak.
    /// Ya'ni Meta kutilmaganda <c>100</c> qaytarsa va uni ko'r-ko'rona ishlatsak, sarf
    /// <b>10^98 barobar</b> buzilardi. Shuning uchun faqat <c>0..<see cref="MetaCurrency.MaxOffset"/></c>
    /// oralig'idagi qiymat "kasr xonalari" deb qabul qilinadi; qolgani mantiqsiz hisoblanib,
    /// bizning jadvalga qaytiladi (xavfsiz tomon).</para>
    ///
    /// <para>⚠️ <c>0</c> — HAQIQIY qiymat (JPY kabi kasrsiz valyuta), "to'ldirilmagan" emas.
    /// Shuning uchun maydon BORLIGI <c>TryGetProperty</c> bilan tekshiriladi, <c>Lng</c>
    /// ning <c>0</c> qaytarishi bilan emas.</para>
    /// </summary>
    internal static (int Offset, string Source) ReadOffset(JsonElement root, string currency)
    {
        var table = (MetaCurrency.OffsetOf(currency), MetaOffsetSource.Table);

        if (root.ValueKind != JsonValueKind.Object) return table;
        if (!root.TryGetProperty("currency_offset", out var v)) return table;

        // ⚠️ `Dec` ISHLATILMAYDI: u buzuq qiymatda ham 0 qaytaradi, 0 esa bu yerda HAQIQIY
        // offset (JPY). "abc" ni jimgina 0 deb o'qish sarfni 100 barobar buzardi.
        int offset;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                if (!v.TryGetInt32(out offset)) return table;      // kasrli/ulkan qiymat — mantiqsiz
                break;
            case JsonValueKind.String:
                var raw = (v.GetString() ?? "").Trim();
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
                    return table;
                break;
            default:
                return table;
        }

        return offset >= 0 && offset <= MetaCurrency.MaxOffset
            ? (offset, MetaOffsetSource.Meta)
            : table;
    }

    /// <summary>
    /// Reklama akkaunti id'sini bir ko'rinishga keltiradi: <c>"1234"</c> → <c>"act_1234"</c>,
    /// <c>"act_1234"</c> → o'zgarishsiz. Yaroqsiz qiymat → bo'sh satr (chaqiruvchi tushunarli
    /// xato beradi).
    ///
    /// <para>⚠️ <c>act_</c> prefiksi FAQAT ad account id'sida bo'ladi. Admin uni ba'zan
    /// prefikssiz ko'chiradi (Ads Manager manzil satrida shunday ko'rinadi), ba'zan prefiks
    /// bilan — ikkalasi ham qabul qilinadi, aks holda "OAuthException 100" chiqib, sababi
    /// tushunarsiz bo'lardi.</para>
    /// </summary>
    public static string NormalizeAccountId(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "";

        if (v.StartsWith("act_", StringComparison.OrdinalIgnoreCase)) v = v[4..];
        v = v.Trim();

        if (v.Length == 0) return "";
        foreach (var ch in v) if (ch < '0' || ch > '9') return "";

        return "act_" + v;
    }

    /* ═════════════════ Rate limit sarlavhalari (§4.6) ═════════════════ */

    /// <summary>
    /// <c>X-FB-Ads-Insights-Throttle</c> va <c>X-Business-Use-Case-Usage</c> sarlavhalarini
    /// o'qiydi. Ikkalasi ham bo'lmasa <c>null</c>.
    ///
    /// <para>⚠️ Kvota formulasi: <c>600 + 400 × aktiv reklama − 0.001 × xatolar</c>, ya'ni
    /// <b>bizning 4xx xatolarimiz kvotani KAMAYTIRADI</b> — xatoni qayta urinib takrorlash
    /// ahvolni yomonlashtiradi.</para>
    ///
    /// <para>⚠️ Tier nomlari Meta hujjatlarining o'zida bir xil emas
    /// (<c>development_access</c>/<c>standard_access</c> ↔ "Standard"/"Advanced" ↔
    /// "Limited"/"Full") — SARLAVHADAGI qiymat qanday bo'lsa shunday saqlanadi, tarjima
    /// qilinmaydi.</para>
    /// </summary>
    public static MetaRateLimitInfo? ParseThrottle(string? insightsHeader, string? bucHeader)
    {
        var appPct = 0; var accPct = 0; var tier = "";
        var callPct = 0; var timePct = 0; var cpuPct = 0; var regain = 0;
        var found = false;

        if (!string.IsNullOrWhiteSpace(insightsHeader))
        {
            try
            {
                using var doc = JsonDocument.Parse(insightsHeader);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    appPct = (int)Lng(root, "app_id_util_pct");
                    accPct = (int)Lng(root, "acc_id_util_pct");
                    tier = Str(root, "ads_api_access_tier");
                    found = true;
                }
            }
            catch (JsonException) { }
        }

        if (!string.IsNullOrWhiteSpace(bucHeader))
        {
            try
            {
                using var doc = JsonDocument.Parse(bucHeader);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    // ⚠️ Kalit — BUSINESS ID, ya'ni oldindan noma'lum: barcha kalitlar ko'riladi.
                    foreach (var biz in root.EnumerateObject())
                    {
                        if (biz.Value.ValueKind != JsonValueKind.Array) continue;
                        foreach (var item in biz.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;

                            // Bizni faqat "ads_insights" qiziqtiradi; boshqa turlar
                            // (ads_management va h.k.) shu so'rovga tegishli emas.
                            var type = Str(item, "type");
                            if (type.Length > 0 && !string.Equals(type, "ads_insights", StringComparison.OrdinalIgnoreCase))
                                continue;

                            callPct = Math.Max(callPct, (int)Lng(item, "call_count"));
                            timePct = Math.Max(timePct, (int)Lng(item, "total_time"));
                            cpuPct = Math.Max(cpuPct, (int)Lng(item, "total_cputime"));
                            regain = Math.Max(regain, (int)Lng(item, "estimated_time_to_regain_access"));
                            found = true;
                        }
                    }
                }
            }
            catch (JsonException) { }
        }

        return found
            ? new MetaRateLimitInfo(appPct, accPct, tier, callPct, timePct, cpuPct, regain)
            : null;
    }

    /// <summary>Kvota holatining O'ZBEKCHA qisqa matni — <c>IgAdAccount.LastError</c> ga yoki
    /// logga yoziladi. Foydalanuvchi texnik sarlavhani emas, "qancha band" ni ko'radi.</summary>
    public static string ThrottleSummary(MetaRateLimitInfo? rl)
    {
        if (rl == null) return "";
        var s = $"Meta kvotasi: ilova {rl.AppUtilPct}%, akkaunt {rl.AccountUtilPct}%, so'rovlar {rl.CallCountPct}%";
        if (rl.Tier.Length > 0) s += $" (daraja: {rl.Tier})";
        if (rl.RegainMinutes > 0) s += $"; ruxsat ~{rl.RegainMinutes} daqiqadan keyin tiklanadi";
        return s;
    }

    /* ═════════════════ Kichik yordamchilar ═════════════════ */

    /// <summary>Matn yoki son — ikkalasi ham matn sifatida. Meta metrikalarni MATN qilib
    /// yuboradi, lekin ba'zi maydonlar (masalan <c>account_status</c>) SON bo'lib keladi.</summary>
    internal static string Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    /// <summary>Matn/son → <c>long</c>. Kasr kelsa yaxlitlanadi (<c>reach</c> ba'zan
    /// "1204.0" bo'lib keladi), buzuq qiymat → 0.</summary>
    internal static long Lng(JsonElement e, string name)
    {
        var d = Dec(e, name);
        var r = Math.Round(d, MidpointRounding.AwayFromZero);
        if (r > long.MaxValue) return long.MaxValue;
        if (r < long.MinValue) return long.MinValue;
        return (long)r;
    }

    /// <summary>Matn/son → <c>decimal</c>, HAR DOIM <see cref="CultureInfo.InvariantCulture"/>
    /// bilan.</summary>
    internal static decimal Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0m;

        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.TryGetDecimal(out var n) ? n : 0m;
            case JsonValueKind.String:
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0m;
                return decimal.TryParse(MetaCurrency.NormalizeNumeric(s), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var d) ? d : 0m;
            default:
                return 0m;
        }
    }
}
