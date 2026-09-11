namespace WunderkindLC.Application.Services;

/// <summary>
/// REKLAMA IZOHI ATRIBUTSIYASI (E3) — "bu izoh qaysi reklama ostida yozilgan?" savoliga
/// javob beradigan SOF funksiyalar (baza ham, tarmoq ham yo'q — to'liq testlanadi).
///
/// <para><b>Muammo.</b> "Instagram API with Instagram Login" yo'lidagi <c>comments</c>
/// webhook'ida <c>ad_id</c> UMUMAN YO'Q — u faqat Facebook Login yo'lidagi payloadda bor
/// (<c>value.media.ad_id</c>). Bizda esa faqat <c>value.media.id</c> keladi.</para>
///
/// <para><b>Yechim (bilvosita).</b> Reklama iyerarxiyasi sinxronizatsiyasi (E1) har e'lon uchun
/// creative'ning <c>effective_object_story_id</c> qiymatini saqlaydi
/// (<see cref="WunderkindLC.Domain.IgAdEntity.CreativeStoryId"/>) — bu reklama ostidagi
/// HAQIQIY post identifikatori. Izoh kelganda <c>media.id</c> shu ustun bilan solishtiriladi:</para>
/// <code>
/// webhook value.media.id  →  IgAdEntity.CreativeStoryId
///    topildi   → IgMessage/IgConversation ga AdId + AdCampaignId yoziladi
///    topilmadi → organik (bo'sh qoladi)
/// </code>
///
/// <para>🔴 <b>BU — TAXMINIY ATRIBUTSIYA. Hech qayerda "aniq" deb ko'rsatilmaydi.</b></para>
/// <list type="table">
///   <item><term>Boostlangan organik post</term><description><b>ISHLAYDI</b> — post bizning
///     media ro'yxatimizda bor va creative aynan o'sha postga ishora qiladi.</description></item>
///   <item><term>Dark post (chop etilmagan reklama)</term><description><b>ISHLAMAYDI</b> —
///     bunday post akkaunt lentasida yo'q; <c>effective_object_story_id</c> bo'lsa ham
///     webhook'dagi <c>media.id</c> u bilan mos kelmasligi mumkin.</description></item>
///   <item><term>Dinamik (katalog) reklama</term><description><b>HECH QANDAY YO'L BILAN</b>
///     aniqlab bo'lmaydi — Meta hujjati ochiq aytadi: dinamik reklamalarda ishlatilgan media
///     uchun <c>ad_id</c> qaytarilmaydi.</description></item>
/// </list>
///
/// <para>Ya'ni bo'sh natija "reklamadan kelmagan" degani EMAS, "aniqlanmadi" degani.
/// Shuning uchun UI'da ham, hisobotda ham chip "taxminiy" deb yoziladi.</para>
/// </summary>
public static class IgAdAttribution
{
    /// <summary>Iyerarxiya darajalari — <c>MetaInsightsParser</c> dagi qiymatlar bilan AYNAN
    /// bir xil (bu yerda ular sof funksiyaga kirish sifatida ishlatiladi).</summary>
    public const string LevelCampaign = "campaign", LevelAdset = "adset", LevelAd = "ad";

    /// <summary>Bitta so'rovda ko'riladigan nomzodlar chegarasi — bitta post ostida o'nlab
    /// e'lon bo'lishi mumkin, lekin ro'yxat cheksiz o'smasin (izoh oqimi tez keladi).</summary>
    public const int MaxCandidates = 50;

    /// <summary>
    /// Iyerarxiya tugunining atributsiya uchun kerakli MINIMAL ko'rinishi
    /// (<c>IgAdEntity</c> ning to'liq nusxasi tashilmasin: sof funksiya domenga bog'lanmasin).
    /// </summary>
    /// <param name="ExternalId">Meta id (e'lon/adset/kampaniya).</param>
    /// <param name="Level"><see cref="LevelCampaign"/> | <see cref="LevelAdset"/> | <see cref="LevelAd"/>.</param>
    /// <param name="ParentId">Ota tugun: ad → adset, adset → campaign.</param>
    /// <param name="CreativeStoryId"><c>effective_object_story_id</c> (bo'lishi shart emas).</param>
    public readonly record struct AdRow(
        string ExternalId, string Level, string ParentId, string CreativeStoryId);

    /// <summary>Topilgan atributsiya. Ikkalasi ham bo'sh bo'lsa — organik/aniqlanmagan.</summary>
    public readonly record struct AdMatch(string AdId, string CampaignId)
    {
        /// <summary>Reklama topildimi (e'lon id'si bor).</summary>
        public bool Found => !string.IsNullOrEmpty(AdId);

        /// <summary>"Aniqlanmadi" holati.
        /// <para>⚠️ <c>default(AdMatch)</c> ISHLATILMAYDI: struct'ning default qiymatida satrlar
        /// <c>null</c> bo'ladi va u bazaga <c>null</c> bo'lib tushardi (loyihada "yo'q" qiymat —
        /// BO'SH SATR).</para></summary>
        public static AdMatch None => new("", "");
    }

    /// <summary>
    /// <c>CreativeStoryId</c> ning MEDIA qismi.
    ///
    /// <para>⚠️ <c>effective_object_story_id</c> odatda <c>"{page_id}_{post_id}"</c> ko'rinishida
    /// keladi, webhook'dagi <c>media.id</c> esa YALANG id bo'ladi. Ikki ko'rinishni to'g'ridan-to'g'ri
    /// solishtirish HECH QACHON mos kelmasdi — shuning uchun oxirgi <c>_</c> dan keyingi qism
    /// olinadi. Ajratgich yo'q bo'lsa qiymat o'zgarishsiz qaytadi.</para>
    /// </summary>
    public static string MediaPart(string? id)
    {
        var v = (id ?? "").Trim();
        if (v.Length == 0) return "";
        var i = v.LastIndexOf('_');
        if (i < 0) return v;
        var tail = v[(i + 1)..];
        // "abc_" kabi buzuq qiymatda dumi bo'sh — o'shanda to'liq qiymat qoladi (jimgina
        // hamma narsaga mos keladigan bo'sh kalit chiqmasin).
        return tail.Length > 0 ? tail : v;
    }

    /// <summary>Webhook'dagi <c>media.id</c> shu creative'ga tegishlimi.
    /// <para>Uch xil moslik qabul qilinadi: aynan teng; creative <c>"{page}_{media}"</c> bo'lib
    /// dumi teng; media id'ning o'zi prefiksli kelgan holat (ikkala tomon ham normallashtiriladi).</para></summary>
    public static bool Matches(string? mediaId, string? creativeStoryId)
    {
        var media = (mediaId ?? "").Trim();
        var story = (creativeStoryId ?? "").Trim();
        if (media.Length == 0 || story.Length == 0) return false;
        if (string.Equals(media, story, StringComparison.Ordinal)) return true;

        var a = MediaPart(media);
        var b = MediaPart(story);
        return a.Length > 0 && b.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>
    /// Berilgan nomzodlardan shu media'ga mos keladiganini topadi.
    ///
    /// <para><b>Deterministik tanlov:</b> bitta post bir necha e'londa ishlatilishi mumkin
    /// (masalan A/B test yoki qayta boost). Bunda avval <c>ad</c> darajasi, keyin
    /// <c>ExternalId</c> bo'yicha ORDINAL tartibda birinchisi olinadi — aks holda bir xil izoh
    /// har safar boshqa e'longa biriktirilib, hisobot beqaror bo'lardi.</para>
    ///
    /// <para>Bo'sh kirish, bo'sh ro'yxat yoki mos kelmagan holat — <c>null</c> (organik).</para>
    /// </summary>
    public static AdRow? FindAd(string? mediaId, IReadOnlyList<AdRow>? rows)
    {
        if (rows is null || rows.Count == 0) return null;
        var media = (mediaId ?? "").Trim();
        if (media.Length == 0) return null;

        AdRow? best = null;
        foreach (var r in rows)
        {
            if (!Matches(media, r.CreativeStoryId)) continue;
            if (best is null || Better(r, best.Value)) best = r;
        }
        return best;
    }

    /// <summary>Ikki nomzoddan qaysi biri "yaxshiroq": avval <c>ad</c> darajasi, keyin
    /// id bo'yicha ordinal tartib (barqarorlik uchun).</summary>
    private static bool Better(AdRow candidate, AdRow current)
    {
        var candIsAd = IsLevel(candidate.Level, LevelAd);
        var curIsAd = IsLevel(current.Level, LevelAd);
        if (candIsAd != curIsAd) return candIsAd;
        return string.CompareOrdinal(candidate.ExternalId, current.ExternalId) < 0;
    }

    /// <summary>
    /// Tugunning KAMPANIYA id'si. <paramref name="known"/> — allaqachon ma'lum bo'lgan ota
    /// tugunlar (chaqiruvchi ularni bazadan oldindan olib beradi).
    ///
    /// <para>Zanjir: <c>ad → adset → campaign</c> (ko'pi bilan ikki qadam). Ota topilmasa BO'SH
    /// qaytadi — <b>atributsiyaning o'zi bekor qilinmaydi</b>: e'lon id'si baribir qimmatli va
    /// kampaniyani keyingi sinxronizatsiya to'ldiradi.</para>
    /// </summary>
    public static string CampaignOf(AdRow node, IReadOnlyList<AdRow>? known)
    {
        if (IsLevel(node.Level, LevelCampaign)) return node.ExternalId ?? "";

        var parentId = (node.ParentId ?? "").Trim();
        if (parentId.Length == 0) return "";
        if (IsLevel(node.Level, LevelAdset)) return parentId;   // adset ning otasi — kampaniya

        // `ad`: otasi adset, buvasi kampaniya.
        var adset = Find(known, parentId);
        if (adset is null) return "";
        if (IsLevel(adset.Value.Level, LevelCampaign)) return adset.Value.ExternalId ?? "";
        return (adset.Value.ParentId ?? "").Trim();
    }

    /// <summary>Bitta chaqiruvda: mos e'lon + uning kampaniyasi.</summary>
    public static AdMatch Resolve(string? mediaId, IReadOnlyList<AdRow>? candidates, IReadOnlyList<AdRow>? parents)
    {
        var ad = FindAd(mediaId, candidates);
        if (ad is null) return new AdMatch("", "");
        return new AdMatch(ad.Value.ExternalId ?? "", CampaignOf(ad.Value, parents));
    }

    private static AdRow? Find(IReadOnlyList<AdRow>? rows, string externalId)
    {
        if (rows is null) return null;
        foreach (var r in rows)
            if (string.Equals(r.ExternalId, externalId, StringComparison.Ordinal)) return r;
        return null;
    }

    private static bool IsLevel(string? level, string wanted) =>
        string.Equals((level ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase);
}
