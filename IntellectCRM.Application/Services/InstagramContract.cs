using System.Globalization;
using System.Text;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Instagram AI agenti — KONSTANTALAR (yagona manba).
/// <para>Kanal/holat nomlari, enum ro'yxatlari va vaqt chegaralari SHU YERDA. Xom satr
/// ("dm", "comment", "pending") kodning boshqa joyida yozilmaydi: NUR loyihasida mock provayder
/// <c>price_inquiry</c>, sxema esa <c>price_question</c> qaytarardi va nomuvofiqlik uzoq vaqt
/// sezilmay yurgan.</para>
/// </summary>
public static class IgConst
{
    /* ---- GRAPH API VERSIYASI (yagona manba) ----
       Versiya ilgari har bir URL ichiga yopishtirilgan edi: ko'tarish kerak bo'lganda uni
       fayllar bo'ylab qidirib chiqish kerak bo'lardi va bitta joyi tushib qolsa modulning bir
       qismi eski, bir qismi yangi versiyada ishlab, sababi tushunarsiz farqlar berardi.

       ⚠️ Ikki versiya ATAYIN AYRI konstanta, garchi hozir qiymatlari bir xil bo'lsa ham:
       Instagram Login yo'li (`graph.instagram.com`) va Facebook Graph (reklama, CAPI) —
       Meta'da BOSHQA-BOSHQA mahsulot va ular ayri jadval bo'yicha eskiradi. Bittasini
       ko'tarish kerak bo'lganda ikkinchisiga tegmaslik imkoni qolsin.

       ⚠️ VERSIYANI MUZLATISH SIZNI HIMOYA QILMAYDI. Meta'ning ba'zi o'zgarishlari versiyaga
       BOG'LIQ EMAS va eski versiyada ham darhol kuchga kiradi: metrikaning o'chirilishi,
       atributsiya oynalarining o'zgarishi, ruxsat/limit siyosati. Ya'ni "v23.0 da qoldik,
       demak hech narsa buzilmaydi" degan xulosa NOTO'G'RI — raqamlar jimgina o'zgarishi
       mumkin, shuning uchun `AttributionSetting` kabi kontekst saqlanadi. */

    /// <summary>Instagram Graph API versiyasi ("Instagram Login" yo'li — izoh, DM, kontent chop etish).
    /// <para>Ko'tarishdan oldin O'ZGARISHLAR JURNALI o'qiladi; ko'tarish — bitta joydan.</para></summary>
    public const string GraphVersion = "v23.0";

    /// <summary>Facebook Graph API versiyasi — reklama lidlari, Ads Insights va CAPI uchun.
    /// <para><see cref="GraphVersion"/> dan AYRI: bular boshqa mahsulot va boshqa jadval bo'yicha
    /// eskiradi (yuqoridagi izoh).</para></summary>
    public const string FbGraphVersion = "v23.0";

    /// <summary>⚠️ <c>graph.facebook.com</c> EMAS — "Instagram Login" yo'lida baza shu.</summary>
    public const string GraphBase = $"https://graph.instagram.com/{GraphVersion}";
    /// <summary>Token almashish (OAuth) uchun alohida host.</summary>
    public const string OauthTokenUrl = "https://api.instagram.com/oauth/access_token";
    public const string GraphRoot = "https://graph.instagram.com";
    public const string AuthorizeUrl = "https://www.instagram.com/oauth/authorize";

    /// <summary>
    /// WEBHOOK OBUNASI maydonlari (<c>POST /me/subscribed_apps?subscribed_fields=…</c>) —
    /// modul ISHLATADIGAN maydonlar, boshqasi yo'q.
    ///
    /// <para><b>🔴 <c>message_echoes</c> BU YERDA YO'Q va bu ATAYIN.</b> Meta uni qabul
    /// qiladigan maydonlar ro'yxatidan olib tashlagan: yuborilsa butun so'rov
    /// <c>IGApiException 100 "Param subscribed_fields[N] must be one of {…}"</c> bilan
    /// RAD ETILADI — ya'ni <c>comments</c> ham obuna bo'lmay qoladi. 2026-08-22 da prodda
    /// aynan shu holat topildi: akkaunt faqat <c>messages</c> ga obuna bo'lib turardi va
    /// izohlar UMUMAN kelmasdi.</para>
    ///
    /// <para>Echo hodisasi yo'qolmaydi: markaz akkauntidan chiqqan xabar <c>messages</c>
    /// obunasi ostida <c>message.is_echo = true</c> bilan keladi va
    /// <see cref="InstagramEventParser"/> uni o'sha yerdan o'qiydi (operator pauzasi ishlaydi).</para>
    ///
    /// <para>⚠️ Yangi maydon QO'SHISHDAN oldin parser uni ISHLAY OLISHINI tekshiring:
    /// obuna bo'lingan, lekin qo'llab-quvvatlanmaydigan maydon navbatni <c>skipped</c>
    /// yozuvlar bilan to'ldiradi (<c>mentions</c>, <c>live_comments</c>).</para>
    /// </summary>
    public static readonly string[] WebhookFields = { "comments", "messages" };

    /// <summary>Obuna so'rovi uchun vergul bilan ajratilgan ro'yxat.</summary>
    public static string WebhookFieldsCsv => string.Join(",", WebhookFields);

    /* ---- REKLAMA LIDLARI (Meta Lead Ads) ----
       ⚠️ Bu YAGONA joy, qayerda `graph.facebook.com` ISHLATILADI va bu ATAYIN: reklama lidi
       FACEBOOK PAGE obyektiga tegishli va `graph.instagram.com` da bunday endpoint YO'Q
       (`leads_retrieval` ham Page tokeni bilan ishlaydi). Izoh/DM esa avvalgidek
       `GraphBase` orqali — ikkisini aralashtirib yubormaslik uchun nomlar ochiq ajratilgan. */

    /// <summary>Facebook Graph API bazasi — reklama lidlari, Ads Insights va CAPI uchun.</summary>
    public const string FbGraphBase = $"https://graph.facebook.com/{FbGraphVersion}";

    /// <summary>Webhook payloadidagi obyekt turi: reklama lidi <c>page</c> obyektidan keladi
    /// (izoh/DM esa <c>instagram</c> dan).</summary>
    public const string ObjectPage = "page", ObjectInstagram = "instagram";

    /// <summary>Page webhook maydoni — reklama formasi to'ldirilganda shu keladi.</summary>
    public const string FieldLeadgen = "leadgen";

    /// <summary>Sahifani ilovaga obuna qilishda so'raladigan maydonlar.</summary>
    public const string LeadgenSubscribeFields = "leadgen";

    /// <summary>
    /// Reklama lidini o'qishda so'raladigan maydonlar (<c>GET /{leadgen_id}</c>).
    ///
    /// <para>⚠️ Ro'yxatda BO'LMAGAN maydon so'ralsa Graph butun so'rovni rad etadi
    /// (<c>code 100</c>) — ya'ni bitta ortiqcha nom tufayli lidlar UMUMAN kelmay qo'yadi.
    /// Shuning uchun bu yerda faqat lid tugunida haqiqatan mavjud maydonlar turadi;
    /// forma NOMI bu tugunda YO'Q va alohida olinadi
    /// (<c>MetaAdsApi.FetchFormNameAsync</c>).</para>
    /// </summary>
    public const string LeadgenFields =
        "id,created_time,field_data,form_id,ad_id,ad_name,adset_id,campaign_id,campaign_name,platform";

    /// <summary>Bir so'rovda qaytariladigan reklama lidlari chegarasi (ro'yxat sahifasi).</summary>
    public const int AdLeadsPageSize = 100;

    /// <summary>OAuth ruxsatlari — "Instagram Login" yo'lining YANGI nomlari
    /// (eski <c>pages_*</c> ruxsatlari Facebook Login yo'liga tegishli va bizga kerak emas).</summary>
    /// <summary>
    /// OAuth ruxsatlari.
    /// <para>⚠️ <c>instagram_business_content_publish</c> — KONTENT JOYLASH uchun (E2). U ro'yxatga
    /// qo'shilgani bilan MAVJUD ulangan akkauntga avtomatik qo'llanmaydi: scope tokenga OAuth
    /// paytida biriktiriladi, ya'ni admin Sozlamalardan «Qayta ulash» bosishi SHART. Aks holda
    /// joylash <c>#200 ruxsat yetishmaydi</c> bilan yiqiladi va sabab ekranda ko'rinmasdi.</para>
    /// </summary>
    public const string Scopes =
        "instagram_business_basic,instagram_business_manage_messages,instagram_business_manage_comments";

    /// <summary>
    /// KONTENT JOYLASH scope'i — <b>asosiy ro'yxatga QO'SHILMAGAN</b>, faqat modul yoqilganda
    /// so'raladi (<see cref="ScopesFor"/>).
    ///
    /// <para>🔴 <b>NEGA shartli:</b> Meta ilovada YOQILMAGAN scope so'ralsa authorize so'rovini
    /// butunlay rad etadi. Agar bu scope doimiy ro'yxatda tursa, kontent modulini umuman
    /// ishlatmaydigan markaz «Qayta ulash» bosganda xato olardi — ya'ni ISHLAB TURGAN izoh/DM
    /// agentini qayta ulay olmay qolardi (token 60 kunda yangilanmasa modul o'lardi). Nosozlik
    /// esa kontent moduliga umuman aloqasi yo'qdek ko'rinardi.</para>
    ///
    /// <para>⚠️ Modul yoqilgandan keyin akkaunt <b>QAYTA ULANISHI</b> shart: scope tokenga OAuth
    /// paytida biriktiriladi, ro'yxatga qo'shilgani bilan mavjud tokenga qo'llanmaydi.</para>
    /// </summary>
    public const string ContentPublishScope = "instagram_business_content_publish";

    /// <summary>OAuth uchun scope ro'yxati. Kontent joylash yoqilgan bo'lsa —
    /// <see cref="ContentPublishScope"/> qo'shiladi.</summary>
    public static string ScopesFor(bool contentPublish) =>
        contentPublish ? Scopes + "," + ContentPublishScope : Scopes;

    public const string ChannelComment = "comment", ChannelDm = "dm", ChannelPrivateReply = "private_reply";
    public const string StatusBot = "bot", StatusOperator = "operator", StatusClosed = "closed";
    public const string EvPending = "pending", EvDone = "done", EvFailed = "failed", EvSkipped = "skipped";
    public const string DirIn = "in", DirOut = "out";
    public const string KindComment = "comment", KindDm = "dm", KindEcho = "echo";

    /// <summary>Inbox ro'yxatining MANBA filtri (<c>?source=</c>).
    /// <para>Hozircha yagona qiymat — <c>ads</c>: "reklama ostidagi izohdan boshlangan suhbatlar"
    /// (<see cref="IgConversation.AdId"/> to'ldirilgan). Boshqa har qanday qiymat filtrsiz
    /// qoladi — klientdagi xato kalit tufayli inbox butunlay bo'shab qolmasin (jurnaldagi
    /// noma'lum tur bilan bir xil siyosat).</para>
    /// <para>⚠️ Bu atributsiya TAXMINIY — qarang <see cref="IgAdAttribution"/>.</para></summary>
    public const string SourceAds = "ads";

    /// <summary>Javobni kim yozgani (<see cref="IgMessage.ActorName"/>) — inbox lentasida ko'rinadi.</summary>
    public const string ActorAi = "AI agent";
    public const string ActorRule = "Avto-qoida";
    public const string ActorOperatorIg = "Operator (Instagram ilovasidan)";

    public static readonly string[] Intents =
    {
        "greeting", "price_question", "product_question", "buying_intent", "complaint", "spam", "other"
    };
    public static readonly string[] Languages = { "uz-Cyrl", "uz-Latn", "ru", "en" };

    public const string DefaultIntent = "other";
    public const string DefaultLanguage = "uz-Latn";

    /// <summary>Shu balldan boshlab lid "qaynoq" (operatorga signal + CRM'ga yoziladi).</summary>
    public const int HotLeadScore = 70;

    /// <summary>Meta qoidasi: mijoz yozganidan keyin DM yuborishga BERILGAN vaqt.</summary>
    public const int DmWindowHours = 24;
    /// <summary>Izohga yopiq javob (private reply) muddati — izohdan keyin 7 kun, BIR marta.</summary>
    public const int PrivateReplyDays = 7;
    /* ---- HALQA AVTOMAT O'CHIRGICHI (burst) ----
       Kunlik chegara (`InstagramDailyReplyLimit`, default 200) — juda KENG to'siq: cheksiz halqa
       to'xtaguncha 200 javob ketardi va Instagram akkauntni bundan ancha oldin spam deb belgilardi.
       Shuning uchun QISQA oynali ikkita chegara ham bor. Qiymatlar odam tezligidan yuqori, lekin
       halqa tezligidan past. */

    /// <summary>Burst oynasi (daqiqa) — ikkala chegara ham shu oynada sanaladi.</summary>
    public const int BurstWindowMinutes = 10;
    /// <summary>Bitta POST ostida 10 daqiqada ko'pi bilan shuncha javob.</summary>
    public const int BurstPerPost = 8;
    /// <summary>Butun akkaunt bo'yicha 10 daqiqada ko'pi bilan shuncha javob.</summary>
    public const int BurstGlobal = 30;

    /// <summary>60 kunlik token 45-kunda yangilanadi (15 kun zaxira qoladi).</summary>
    public const int TokenRefreshDays = 45;
    /// <summary>Token muddatiga shundan kam qolganda yangilanadi (45-kun bilan bir xil chegara).</summary>
    public const int TokenRefreshBeforeDays = 60 - TokenRefreshDays;

    /// <summary>AI'ga beriladigan suhbat tarixi (oxirgi N ta xabar).</summary>
    public const int DmHistoryLimit = 20;
    /// <summary>
    /// IZOH uchun tarix — o'sha post ostidagi oxirgi N ta yozishma
    /// (<c>InstagramPipeline.LoadHistoryAsync</c>).
    ///
    /// <para>DM'nikidan (20) ATAYIN qisqa: post ostidagi yozishma bir-ikki qadamdan iborat
    /// («savol → javob → rahmat»), uzun tarix esa modelni ostidagi izohdan chalg'itardi —
    /// izoh yakka savol, uzluksiz muloqot emas.</para>
    /// </summary>
    public const int CommentHistoryLimit = 6;
    /// <summary>Post matni (caption) promptga shuncha belgigacha kiradi.</summary>
    public const int MediaCaptionLimit = 300;
    /// <summary>Bilim bazasi promptga shuncha belgigacha kiradi (token narxi cheksiz o'smasin).</summary>
    public const int KnowledgeLimit = 12000;
    /// <summary>
    /// Instagram xabar matnining HAQIQIY chegarasi — <b>1000 BAYT</b> (Meta: "Message text must
    /// be UTF-8 and be a 1000 bytes or less").
    ///
    /// <para><b>🔴 BAYT, BELGI EMAS — bu yerda jimgina xato bor edi.</b> Ilgari matn
    /// <see cref="MaxReplyLength"/> (900 <i>belgi</i>) bilan kesilardi. UTF-8 da lotin harfi
    /// 1 bayt, <b>kirill 2 bayt</b>, emoji <b>4 baytgacha</b>. Ya'ni 900 belgilik ruscha yoki
    /// kirillcha javob ≈ 1700–1800 bayt bo'lib, Meta uni RAD ETARDI. Nosozlik "goh ishlaydi,
    /// goh ishlamaydi" bo'lib ko'rinardi: lotincha o'zbekcha javoblarda muammo yo'q edi,
    /// shuning uchun sinovda topilmasdi.</para>
    ///
    /// <para>Yuborishdan oldin matn <see cref="TrimBytes"/> bilan kesiladi.</para>
    /// </summary>
    public const int MaxReplyBytes = 1000;

    /// <summary>Operatorning <c>HUMAN_AGENT</c> tegi bilan javob bera oladigan oynasi — 7 kun.
    /// <para>Qoida: <see cref="InstagramContract.HumanAgentWindowOpen"/>. Bot bu oynadan
    /// FOYDALANMAYDI (Meta siyosati: teg faqat tirik odam javobiga).</para></summary>
    public const int HumanAgentWindowHours = 168;

    /// <summary>
    /// AI'ga beriladigan uzunlik MO'LJALI (belgi) — chegara EMAS.
    ///
    /// <para>Haqiqiy chegara — <see cref="MaxReplyBytes"/> (bayt). Bu qiymat AI javobini
    /// oldindan qisqartirish va sifat jurnalidagi solishtirish uchun ishlatiladi: 900 belgi
    /// har qanday yozuvda 1000 baytga sig'masligi mumkin, lekin AI'ga "1000 bayt" deb
    /// tushuntirishdan ko'ra belgi bilan mo'ljal berish aniqroq ishlaydi.</para>
    /// </summary>
    public const int MaxReplyLength = 900;

    /// <summary>Operator qo'lda javob yozsa bot shuncha DAQIQA jim turadi — <b>12 soat</b>.
    /// <para>Operator Instagram ilovasidan qo'lda javob yozgan bo'lsa, bot uning ustiga yozib
    /// mijozni chalkashtirmasin. Botga qaytarish — Inbox'dagi «Botga qaytarish» tugmasi bilan
    /// bir bosishda.</para>
    /// <para>⚠️ Qiymat FAQAT SHU YERDA: echo orqali yoqiladigan pauza ham, operator javobidan
    /// keyingi pauza ham (<c>InstagramController.Reply</c>) shu konstantani o'qiydi — aks holda
    /// ikki yo'l ikki xil xulq berardi.</para></summary>
    public const int OperatorPauseMinutes = 720;

    /// <summary>Echo qaytganda "bu bizning javobimizmi" tekshiruvi shuncha daqiqalik oynada
    /// bo'ladi (o'sha matnli chiquvchi xabar shu oyna ichida yozilgan bo'lsa — bizniki).</summary>
    public const int EchoOwnReplyMinutes = 10;

    /// <summary>Navbatdagi hodisa shuncha marta urinilgach <c>failed</c> bo'ladi.</summary>
    public const int MaxAttempts = 3;
    /// <summary>Bir siklda nechta hodisa olinadi.</summary>
    public const int QueueBatch = 10;
    /// <summary>Bajarilgan hodisalar shuncha kundan keyin tozalanadi.</summary>
    public const int EventRetentionDays = 30;
    /// <summary>OAuth <c>state</c> amal qilish muddati (daqiqa).</summary>
    public const int OAuthStateMinutes = 15;

    /// <summary>Javob kechikishining yuqori chegarasi (soniya) — sozlama xato kiritilsa fon
    /// xizmati soatlab qotib qolmasin.</summary>
    public const int MaxReplyDelaySeconds = 60;

    /// <summary>Bot oshkorligi matni sozlanmagan bo'lsa ishlatiladigan standart (Meta talabi).</summary>
    public const string DefaultGreeting =
        "🤖 Men markazning AI yordamchisiman. Operator kerak bo'lsa yozing — ulaymiz.";
}

/// <summary>
/// AI agentining STRUKTURALI chiqishi (IG-SPEC §5.1).
/// <para>⚠️ Diapazon/enum cheklovlari LLM sxemasida emas, KOD tomonda qo'llanadi
/// (<see cref="InstagramContract.ClampScore"/>, <see cref="InstagramContract.NormalizeIntent"/>) —
/// structured output <c>minimum</c>/<c>maximum</c> kabi cheklovlarni qabul qilmaydi.</para>
/// </summary>
public record IgAgentOutput(
    string Reply,
    string Language,
    string Intent,
    int LeadScore,
    bool IsHotLead,
    bool MoveToDm,
    bool EscalateToHuman,
    string LeadName,
    string LeadContact,
    string LeadProductInterest,
    string LeadSummary);

/// <summary>
/// Instagram moduli qoidalarining SOF (I/O'siz) qismi — baza ham, tarmoq ham chaqirilmaydi,
/// shuning uchun to'liq testlanadi.
/// </summary>
public static class InstagramContract
{
    /// <summary>Ballni 0..100 oralig'iga keltiradi (LLM 150 yoki -5 qaytarishi mumkin).</summary>
    public static int ClampScore(int v) => Math.Clamp(v, 0, 100);

    /// <summary>Noma'lum/bo'sh niyat → <c>other</c> (yozuv YO'QOLMAYDI, faqat "boshqa"ga tushadi).</summary>
    public static string NormalizeIntent(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        foreach (var i in IgConst.Intents) if (i == s) return i;
        return IgConst.DefaultIntent;
    }

    /// <summary>Noma'lum/bo'sh til → <c>uz-Latn</c> (markazning asosiy yozuvi).</summary>
    public static string NormalizeLanguage(string? v)
    {
        var s = (v ?? "").Trim();
        foreach (var l in IgConst.Languages)
            if (string.Equals(l, s, StringComparison.OrdinalIgnoreCase)) return l;
        return IgConst.DefaultLanguage;
    }

    /// <summary>"Qaynoq lid": LLM shunday belgiladi, YOKI ball chegaradan yuqori, YOKI kontakt berildi.
    /// <para>Uchala shart ham mustaqil: LLM ehtiyotkorlik qilib <c>is_hot_lead=false</c> qo'ysa ham,
    /// telefon qoldirgan odam qaynoq lid — bu "operator qo'ng'iroq qilsin" degani.</para></summary>
    public static bool IsHot(IgAgentOutput o) =>
        o.IsHotLead || ClampScore(o.LeadScore) >= IgConst.HotLeadScore || HasContact(o);

    /// <summary>CRM'ga lid yoziladimi. ⚠️ HAR suhbat lid EMAS — salom-alik va spam CRM'ni
    /// ifloslantirmaydi (IG-SPEC §5.3).</summary>
    public static bool ShouldCreateLead(IgAgentOutput o) => IsHot(o);

    /// <summary>Kontakt (telefon yoki boshqa aloqa) berilganmi.</summary>
    public static bool HasContact(IgAgentOutput o) => !string.IsNullOrWhiteSpace(o.LeadContact);

    // ─────────────────────── REKLAMA ATRIBUTSIYASI (E3) — ko'rinadigan qism ───────────────────────

    /// <summary>
    /// Inbox filtri "faqat reklamadan kelganlar" nimi (<c>?source=ads</c>).
    ///
    /// <para>⚠️ Noma'lum qiymat <c>false</c> qaytaradi, ya'ni filtr UMUMAN qo'llanmaydi va
    /// ro'yxat to'liq ko'rinadi. Xato kalit tufayli operator bo'sh ekran ko'rib "suhbat yo'q"
    /// deb o'ylab qolmasin.</para>
    /// </summary>
    public static bool WantsAdsOnly(string? source) =>
        string.Equals((source ?? "").Trim(), IgConst.SourceAds, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Suhbat reklama izohidan boshlanganmi — <b>YAGONA</b> tekshiruv (UI ham, filtr ham shuni
    /// ishlatadi). Kampaniya aniqlanmagan, lekin e'lon topilgan holat ham "reklama" hisoblanadi:
    /// iyerarxiya hali sinxronlanmagan bo'lsa suhbat organik bo'lib ko'rinib qolmasin.
    /// </summary>
    public static bool FromAd(string? adId) => !string.IsNullOrWhiteSpace(adId);

    /// <summary>
    /// Ekranda ko'rsatiladigan kampaniya YORLIG'I.
    ///
    /// <para>Nomi topilmasa <b>id'ning O'ZI</b> qaytadi (<c>MetaAdsRoi.BuildNode</c> bilan bir xil
    /// qoida): sun'iy "Noma'lum kampaniya" matni haqiqiy tugundan ajratib bo'lmas, id esa Ads
    /// Manager'da qidirsa bo'ladigan qiymat. Kampaniya ham, e'lon ham bo'sh bo'lsa — bo'sh satr
    /// (chip umuman chizilmaydi).</para>
    /// </summary>
    public static string AdCampaignLabel(string? campaignId, string? campaignName, string? adId = null)
    {
        var name = (campaignName ?? "").Trim();
        if (name.Length > 0) return name;
        var id = (campaignId ?? "").Trim();
        if (id.Length > 0) return id;
        // Kampaniya aniqlanmagan (iyerarxiya sinxronlanmagan) — hech bo'lmasa e'lon id'si.
        return (adId ?? "").Trim();
    }

    /// <summary>
    /// DM'ning 24 soatlik javob oynasi ochiqmi (mijoz oxirgi marta qachon yozgan).
    /// <para>⚠️ FAIL-CLOSED: sana bo'sh yoki o'qib bo'lmaydigan bo'lsa <c>false</c> — "bilmasak
    /// yubormaymiz". Meta baribir rad etardi, lekin bizda bu holat operator signaliga aylanadi.</para>
    /// </summary>
    public static bool DmWindowOpen(string lastInboundAtIso, DateTime now)
    {
        if (!TryIso(lastInboundAtIso, out var last)) return false;
        var diff = now - last;
        // Kelajakdagi sana (soat farqi/qo'lda tuzatilgan yozuv) — oyna ochiq deb qaraladi.
        if (diff < TimeSpan.Zero) return true;
        return diff < TimeSpan.FromHours(IgConst.DmWindowHours);
    }

    /// <summary>Operator pauzasi kuchdami: suhbat qo'lda "operator"ga olingan yoki echo tufayli
    /// vaqtincha pauzada.</summary>
    public static bool OperatorPaused(IgConversation c, DateTime now)
    {
        if (c.Status == IgConst.StatusOperator) return true;
        if (!TryIso(c.OperatorPausedUntil, out var until)) return false;
        return now < until;
    }

    /// <summary>
    /// <b>OPERATOR</b> uchun kengaytirilgan javob oynasi — <c>HUMAN_AGENT</c> tegi bilan
    /// <b>7 kun</b> (168 soat).
    ///
    /// <para><b>Nega bor:</b> Meta odatiy 24 soatlik oynadan tashqarida ham javob berishga
    /// ruxsat beradi — lekin FAQAT javobni <b>tirik odam</b> yozgan bo'lsa
    /// (<c>messaging_type=MESSAGE_TAG</c>, <c>tag=HUMAN_AGENT</c>). Busiz operator juma kuni
    /// kelgan savolga dushanba kuni javob yoza olmasdi va UI "oyna yopilgan" derdi — bu
    /// Meta'ning mutlaq cheklovi emas, bizning ishlatmagan imkoniyatimiz edi.</para>
    ///
    /// <para>🔴 <b>BOT UCHUN ISHLATILMAYDI.</b> Tegni avtomatik javobga qo'yish Meta siyosatini
    /// buzadi (u aynan "odam javob berdi" degani) va akkauntga cheklov keltirishi mumkin.
    /// Shuning uchun uni faqat <c>InstagramController.Reply</c> (operator qo'lda yozgani)
    /// ishlatadi; <see cref="InstagramPipeline"/> hamon <see cref="DmWindowOpen"/> ga tayanadi.</para>
    /// </summary>
    public static bool HumanAgentWindowOpen(string lastInboundAtIso, DateTime now)
    {
        if (!TryIso(lastInboundAtIso, out var last)) return false;
        var diff = now - last;
        if (diff < TimeSpan.Zero) return true;
        return diff < TimeSpan.FromHours(IgConst.HumanAgentWindowHours);
    }

    /// <summary>Bot javob berishi mumkin bo'lgan suhbatmi (yopiq emas, pauzada emas).</summary>
    public static bool BotMayReply(IgConversation c, DateTime now) =>
        c.Status != IgConst.StatusClosed && !OperatorPaused(c, now);

    /// <summary>
    /// Instagram Login tokeni (<c>IgAccount.AccessToken</c>) HOZIR kerak bo'ladigan modul
    /// yoqilganmi — ya'ni fon xizmati bu tokenni yangilab turishi SHARTMI.
    ///
    /// <para><b>🔴 NEGA ALOHIDA FUNKSIYA.</b> Ilgari <c>InstagramWorkerService</c> da yalang
    /// <c>if (!meta.InstagramEnabled) return;</c> turardi — token faqat AI agenti yoqilganda
    /// yangilanardi. Lekin AYNAN SHU tokenni <see cref="InstagramPublishService"/> (kontent
    /// joylash) ham ishlatadi. Natijada faqat kontent modulini yoqqan markazda 60 kunlik token
    /// JIMGINA o'lardi va post birdan <c>OAuthException 190</c> bilan yiqila boshlardi — sababi
    /// esa "modul o'chiq" degan boshqa joydagi bayroq edi.</para>
    ///
    /// <para>⚠️ Reklama lidlari (Page token), reklama statistikasi (System User) va CAPI
    /// (Dataset) BOSHQA tokenlar bilan ishlaydi va bu yerga KIRMAYDI: ular yangilanmaydi
    /// (muddatsiz). Yangi modul QO'SHSANGIZ — qaysi tokenni ishlatishini aniqlang va
    /// Instagram Login tokeni bo'lsa shu ro'yxatga qo'shing.</para>
    /// </summary>
    public static bool NeedsLoginToken(bool agentEnabled, bool publishEnabled) =>
        agentEnabled || publishEnabled;

    /// <summary>
    /// Akkaunt Meta'da QAYSI kerakli maydonlarga obuna EMAS — bo'sh ro'yxat = hammasi joyida.
    ///
    /// <para><b>🔴 Nega bu tekshiruv kerak.</b> <c>IgAccount.WebhookSubscribed</c> — ULANISH
    /// PAYTIDAGI suratcha, ya'ni "o'sha kuni so'rov muvaffaqiyatli o'tdi" degani. Obuna esa
    /// keyin O'ZGARISHI mumkin: Meta maydon nomini olib tashlaydi, admin Dashboard'dan
    /// belgini olib qo'yadi, ilova qayta ko'rib chiqishga tushadi. Bazadagi <c>true</c>
    /// shundan keyin ham <c>true</c> bo'lib qolaveradi va diagnostika "hammasi joyida"
    /// deb ko'rsatardi — hodisa esa kelmasdi. Shuning uchun holat Meta'dan JONLI o'qiladi.</para>
    ///
    /// <para>⚠️ Solishtirish katta-kichik harfga bog'liq emas va ortiqcha maydonlarga
    /// e'tibor bermaydi: markaz Dashboard'dan qo'shimcha maydon yoqqan bo'lsa (masalan
    /// <c>mentions</c>) bu XATO emas — ular parserda jimgina tashlanadi.</para>
    /// </summary>
    public static IReadOnlyList<string> MissingWebhookFields(IEnumerable<string>? actual)
    {
        var have = new HashSet<string>(
            (actual ?? Array.Empty<string>()).Select(f => (f ?? "").Trim()),
            StringComparer.OrdinalIgnoreCase);
        return IgConst.WebhookFields.Where(f => !have.Contains(f)).ToList();
    }

    /// <summary>
    /// MODULNING TASHQI (OMMAVIY) MANZIL ASOSI — <c>https://host</c>, oxirida <c>/</c> YO'Q.
    /// Webhook manzili, OAuth <c>redirect_uri</c> va Sozlamalar sahifasidagi "nusxa olish"
    /// tugmalari HAMMASI shundan quriladi.
    ///
    /// <para><b>🔴 NEGA so'rov hostiga TAYANIB BO'LMAYDI.</b> Ilgari manzil faqat
    /// <c>{Request.Scheme}://{Request.Host}</c> edi. Meta'da esa <c>redirect_uri</c>
    /// <b>harfma-harf</b> ro'yxatdan o'tgani bilan bir xil bo'lishi shart (§11 tuzoq 10). Ya'ni
    /// admin CRM'ni boshqa nom bilan ochsa (IP, <c>www.</c> prefiksi, vaqtinchalik domen,
    /// lokal port) tugma NOTO'G'RI manzil ko'rsatardi va ulash <c>Invalid redirect_uri</c>
    /// bilan yiqilardi — xato matni esa qayerda ekanini aytmasdi.</para>
    ///
    /// <para>Loyihada bunday "kanonik host" allaqachon bor: <c>App:Host</c> (env
    /// <c>APP_HOST</c>) — telefoniya webhook'i va karyera Mini App manzili ham shundan
    /// quriladi. Instagram ham AYNAN shu manbadan foydalanadi.</para>
    ///
    /// <para>⚠️ Sozlanmagan bo'lsa (dev) eski xatti-harakat saqlanadi — so'rov hostidan.</para>
    /// </summary>
    /// <param name="configuredHost"><c>App:Host</c> qiymati. Sxema bilan ham
    /// (<c>https://crm.uz</c>), sxemasiz ham (<c>crm.uz</c>) bo'lishi mumkin; yo'l qismi
    /// tashlanadi.</param>
    /// <param name="requestScheme">Zaxira: joriy so'rov sxemasi (<c>X-Forwarded-Proto</c> dan
    /// tiklangan).</param>
    /// <param name="requestHost">Zaxira: joriy so'rov hosti.</param>
    public static string PublicBase(string? configuredHost, string requestScheme, string requestHost)
    {
        var h = (configuredHost ?? "").Trim();
        if (h.Length > 0)
        {
            var scheme = "";
            if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) { scheme = "http"; h = h[7..]; }
            else if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { scheme = "https"; h = h[8..]; }

            var slash = h.IndexOf('/');
            if (slash >= 0) h = h[..slash];
            h = h.Trim().TrimEnd('.');

            if (h.Length > 0)
            {
                // Sxema ochiq yozilmagan bo'lsa: Meta faqat HTTPS qabul qiladi, lekin lokal
                // ishlab chiqishda `https://localhost` mavjud bo'lmagan manzil bo'lardi.
                if (scheme.Length == 0) scheme = IsLocalHost(h) ? "http" : "https";
                return scheme + "://" + h;
            }
        }

        return $"{requestScheme}://{requestHost}";
    }

    /// <summary>Lokal (ishlab chiqish) hostimi — <see cref="PublicBase"/> sxemani shunga qarab tanlaydi.</summary>
    private static bool IsLocalHost(string host)
    {
        var name = host.Split(':')[0];
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || name == "127.0.0.1"
               || name == "::1"
               || name.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HALQA AVTOMAT O'CHIRGICHI — shu javobni yuborish MUMKINMI.
    ///
    /// <para>Qaytaradi: bo'sh satr = mumkin, aks holda operatorga ko'rsatiladigan SABAB.
    /// Ikki chegara mustaqil: bitta post ostida qizib ketgan halqa global chegaraga yetmasdan
    /// ham to'xtatiladi, butun akkaunt bo'yicha portlash esa hech bir postga bog'liq emas.</para>
    ///
    /// <para>⚠️ Bu <b>kunlik</b> chegaraning o'rnini bosmaydi — u uzoq muddatli to'siq, bu esa
    /// TEZKOR: halqa daqiqalar ichida yuzlab javob yozadi, kunlik chegara esa unga ulgurmaydi.</para>
    /// </summary>
    /// <param name="perPostInWindow">Shu post ostida oxirgi <see cref="IgConst.BurstWindowMinutes"/>
    /// daqiqada yuborilgan javoblar soni. Post noma'lum bo'lsa (DM) — 0.</param>
    /// <param name="globalInWindow">Butun akkaunt bo'yicha o'sha oynadagi javoblar soni.</param>
    public static string BurstBlockReason(int perPostInWindow, int globalInWindow)
    {
        if (perPostInWindow >= IgConst.BurstPerPost)
            return $"Bitta post ostida {IgConst.BurstWindowMinutes} daqiqada {IgConst.BurstPerPost} ta javob "
                   + "chegarasiga yetildi — avtomatik javob to'xtatildi (halqa himoyasi)";
        if (globalInWindow >= IgConst.BurstGlobal)
            return $"Oxirgi {IgConst.BurstWindowMinutes} daqiqada {IgConst.BurstGlobal} ta javob "
                   + "chegarasiga yetildi — avtomatik javob to'xtatildi (halqa himoyasi)";
        return "";
    }

    /// <summary>
    /// Matndan O'ZBEK telefon raqamini ajratib oladi (topilmasa "").
    /// <para>Qabul qilinadi: <c>+998 90 123 45 67</c>, <c>998901234567</c>, <c>90 123 45 67</c>.
    /// Raqamlar orasidagi ajratuvchilar (bo'sh joy, <c>-</c>, qavs, nuqta) uzilish hisoblanmaydi.</para>
    /// <para>⚠️ Uzunlik AYNAN 9 yoki 998 bilan boshlanuvchi 12 bo'lishi shart: Instagram id'lari
    /// (17 raqam), narx (<c>500000</c>) va yil (<c>2026</c>) telefon deb olinib, begona lidga
    /// biriktirilib ketmasin.</para>
    /// </summary>
    public static string ExtractPhone(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // Matn RAQAM BO'LAKLARIGA ajratiladi; ajratuvchi (bo'sh joy, `-`, qavs...) bo'laklarni
        // BOG'LANGAN deb belgilaydi, harf yoki boshqa belgi esa guruhni UZADI.
        var groups = new List<List<string>>();
        var group = new List<string>();
        var run = new StringBuilder();

        void EndRun()
        {
            if (run.Length == 0) return;
            group.Add(run.ToString());
            run.Clear();
        }
        void EndGroup()
        {
            EndRun();
            if (group.Count > 0) { groups.Add(group); group = new List<string>(); }
        }

        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : '\n';
            if (char.IsDigit(c)) { run.Append(c); continue; }
            if (IsPhoneSeparator(c)) { EndRun(); continue; }   // guruh davom etadi
            EndGroup();
        }
        EndGroup();

        // ⚠️ Har guruhda AVVAL bo'laklarning BIRLASHMASI ("+998 90 123 45 67" → 998901234567),
        // topilmasa HAR BO'LAK ALOHIDA sinaladi. Ilgari faqat birlashma sinalardi va
        // "narxi 500000 901234567" kabi matnda ikki son qo'shilib ketib (15 raqam), telefon
        // BUTUNLAY yo'qolardi. Alohida bo'lak sinovi esa Instagram id'sini (17 raqamli BITTA
        // bo'lak) baribir qabul qilmaydi — uzunlik sharti o'zgarmadi.
        foreach (var g in groups)
        {
            var joined = TryPhone(string.Concat(g));
            if (joined.Length > 0) return joined;
            foreach (var part in g)
            {
                var hit = TryPhone(part);
                if (hit.Length > 0) return hit;
            }
        }
        return "";
    }

    private static bool IsPhoneSeparator(char c) =>
        c is ' ' or '-' or '(' or ')' or '.' or '+' or ' ' or '‑' or '–';

    private static string TryPhone(string digits)
    {
        string local;
        if (digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal)) local = digits[3..];
        else if (digits.Length == 9) local = digits;
        else return "";
        if (local[0] == '0') return "";   // mahalliy raqam 0 bilan boshlanmaydi
        return PhoneUtil.Normalize("998" + local);
    }

    /// <summary>
    /// Kalit so'z qoidasi shu xabarga mos keladimi (AI'dan OLDINGI tez yo'l).
    /// <para>Moslik registr farqisiz va "so'zning bir qismi" bo'yicha (<c>narx</c> → <c>narxi</c>).
    /// Kanal <c>any</c> bo'lsa ikkala kanalda ham ishlaydi. Yopiq javob (<c>private_reply</c>)
    /// izohning davomi bo'lgani uchun <c>comment</c> qoidalari bilan tekshiriladi.</para>
    /// </summary>
    public static bool RuleMatches(IgAutoRule rule, string channel, string text)
    {
        if (!rule.IsActive || string.IsNullOrWhiteSpace(text)) return false;
        var ch = channel == IgConst.ChannelPrivateReply ? IgConst.ChannelComment : channel;
        if (rule.Channel != "any" && rule.Channel != ch) return false;

        var hay = text.ToLowerInvariant();
        foreach (var raw in (rule.Keywords ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kw = raw.ToLowerInvariant();
            if (kw.Length > 0 && hay.Contains(kw, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Matnni belgilangan uzunlikka qisqartiradi (oxiriga "…" qo'yiladi).</summary>
    public static string Trim(string? s, int max)
    {
        var t = (s ?? "").Trim();
        if (max <= 1 || t.Length <= max) return t;
        return t[..(max - 1)] + "…";
    }

    /// <summary>
    /// Matnni <b>UTF-8 BAYTLAR</b> bo'yicha kesadi (Instagram xabar chegarasi baytda —
    /// <see cref="IgConst.MaxReplyBytes"/>).
    ///
    /// <para><b>Nega alohida funksiya:</b> <see cref="Trim"/> <c>string.Length</c> ni sanaydi,
    /// ya'ni BELGI. Kirill harfi UTF-8 da 2 bayt, emoji 4 baytgacha — belgi bo'yicha "sig'gan"
    /// matn baytda chegaradan oshib ketardi va Meta javobni rad etardi.</para>
    ///
    /// <para>⚠️ Kesish <b>matn elementi</b> (grapheme cluster) chegarasida bo'ladi: emoji
    /// surrogat juftligi yoki ZWJ ketma-ketligi (👨‍👩‍👧) o'rtasidan bo'linsa satr buzilib,
    /// javobda "�" chiqardi.</para>
    ///
    /// <para>⚠️ Uch nuqta (<c>…</c>) ning O'ZI ham 3 bayt — u byudjetdan OLDINDAN ayriladi,
    /// aks holda kesilgan matn baribir chegaradan oshardi.</para>
    /// </summary>
    public static string TrimBytes(string? s, int maxBytes)
    {
        var t = (s ?? "").Trim();
        if (maxBytes <= 0) return "";
        if (Encoding.UTF8.GetByteCount(t) <= maxBytes) return t;

        const string ellipsis = "…";
        var budget = maxBytes - Encoding.UTF8.GetByteCount(ellipsis);
        if (budget <= 0) return "";

        var sb = new StringBuilder();
        var used = 0;
        var it = StringInfo.GetTextElementEnumerator(t);
        while (it.MoveNext())
        {
            var element = (string)it.Current;
            var size = Encoding.UTF8.GetByteCount(element);
            if (used + size > budget) break;
            sb.Append(element);
            used += size;
        }

        return sb.ToString().TrimEnd() + ellipsis;
    }

    /// <summary>Loyihadagi ISO satrni (<c>AppClock.Iso()</c>) o'qiydi.</summary>
    public static bool TryIso(string? iso, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(iso)) return false;
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
