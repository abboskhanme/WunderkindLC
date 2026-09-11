using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Meta webhook payloadidan ajratib olingan BITTA hodisa (normalizatsiyalangan ichki model).
/// </summary>
/// <param name="Kind"><c>comment</c> | <c>dm</c> | <c>echo</c> — va E6 bilan qo'shilgan
/// <c>deleted</c> (mijoz xabarini o'chirdi) hamda <c>policy</c> (Meta siyosati ogohlantirishi —
/// ⚠️ joriy yo'lda KELMAYDI, <see cref="InstagramEventParser.KindPolicy"/>).
/// Oxirgi ikkisi SUHBAT hodisasi emas: ular yangi xabar yozmaydi, mavjud yozuvni tozalaydi
/// yoki butun modulga ta'sir qiladi.</param>
/// <param name="Text">Xabar matni. <b>Bo'sh bo'lishi mumkin</b> (rasm/stiker/ovoz) — hodisa
/// baribir qaytariladi, chunki jimgina yo'qolgan mijoz eng yomon holat.</param>
/// <param name="SenderId">SUHBATDOSHNING id'si. ⚠️ <c>echo</c> da bu BIZ EMAS, xabar KIMGA
/// ketgan bo'lsa o'sha (recipient) — pauza aynan o'sha suhbatga qo'yilishi kerak.</param>
/// <param name="Username">Faqat izohda ishonchli keladi; DM'da odatda bo'sh.</param>
/// <param name="CommentId">Izohda majburiy — javob aynan shu izoh ostiga yoziladi.</param>
/// <param name="MediaId">Izoh qaysi post ostida (AI kontekst uchun).</param>
/// <param name="IgMessageId">DM'dagi <c>mid</c> — dedup kaliti shundan.</param>
/// <param name="EventKey">Deterministik dedup kaliti.</param>
/// <param name="IsEcho">Bizning akkauntimizdan chiqqan xabar.</param>
public record IgIncomingEvent(
    string Kind,
    string Text,
    string SenderId,
    string Username,
    string CommentId,
    string MediaId,
    string IgMessageId,
    string EventKey,
    bool IsEcho,
    /// <summary>Meta bergan hodisa vaqti (ISO, mahalliy mintaqada). Bo'sh = payloadda yo'q.
    /// <para>⚠️ 24 soatlik DM oynasi SHU vaqtdan hisoblanishi kerak, hodisa QAYTA ISHLANGAN
    /// vaqtdan emas: navbat uzoq turib qolsa (modul o'chiq bo'lib keyin yoqilsa) oyna "ochiq"
    /// bo'lib ko'rinardi va Instagram javobni rad etardi.</para></summary>
    string SentAtIso = "",

    /* ─────────── E6: STORY, ULASHILGAN POST, O'CHIRISH, SIYOSAT ───────────
       Quyidagi maydonlar ATAYIN qo'shimcha (default qiymatli) parametrlar: mavjud chaqiruvchilar
       va testlar o'zgarishsiz ishlayveradi, yangi kontekst esa jimgina yo'qolmaydi. */

    /// <summary>Mijoz QAYSI story'ga javob yozgani (<c>message.reply_to.story.id</c>).
    /// Bo'sh = story javobi emas.</summary>
    string StoryId = "",

    /// <summary>O'sha story rasmining CDN manzili (<c>message.reply_to.story.url</c>).
    /// <para>⚠️ Bu manzil <b>TEZ O'LADI</b> (story 24 soatda yo'qoladi, imzolangan CDN havolasi
    /// undan ham oldin). Rasmni o'zimizga ko'chirish uchun alohida fayl saqlash (va migratsiya)
    /// kerak bo'lardi — hozircha id va manzil xabar KONTEKSTIDA matn sifatida saqlanadi, ya'ni
    /// operator hech bo'lmaganda "qaysi story haqida gap ketyapti" ni ko'radi.</para></summary>
    string StoryUrl = "",

    /// <summary>Mijoz o'z story'sida BIZNI eslatib o'tgan (<c>attachments[].type == "story_mention"</c>).
    /// Bu odatiy DM emas — javob berish odobi ham boshqacha, shuning uchun alohida belgilanadi.</summary>
    bool IsStoryMention = false,

    /// <summary>Mijoz IG postni ulashgan (<c>attachments[].type == "ig_post"</c>).
    /// <para>⚠️ Eski <c>share</c> turi 2026-02-01 da OLIB TASHLANGAN — faqat <c>ig_post</c>
    /// parse qilinadi.</para></summary>
    bool HasSharedPost = false,

    /// <summary>Ulashilgan postning manzili (bo'lsa).</summary>
    string SharedPostUrl = "",

    /// <summary>Mijoz xabarni O'CHIRDI (<c>message.is_deleted</c>). Bu YANGI xabar emas —
    /// mavjud yozuvning mazmuni tozalanishi kerak (Platform Terms talabi).</summary>
    bool IsDeleted = false,

    /// <summary>Meta'ning siyosat ogohlantirishi: amal (<c>warning</c> | <c>block</c> ...).
    /// Bo'sh = bu hodisa siyosat ogohlantirishi emas.</summary>
    string PolicyAction = "",

    /// <summary>Siyosat ogohlantirishining sababi (Meta bergan matn, xom holda).</summary>
    string PolicyReason = "",

    /* ─────────── E7: REKLAMA ATRIBUTSIYASI (DM'da ANIQ `ad_id`) ───────────
       Yana ATAYIN qo'shimcha (default qiymatli) parametrlar — E6 dagi bilan bir xil sabab:
       mavjud chaqiruvchilar va testlar o'zgarishsiz ishlayveradi, yangi kontekst esa jimgina
       yo'qolmaydi. */

    /// <summary>«Click to Instagram Direct» reklamasidan kelgan DM'ning e'lon id'si
    /// (<c>referral.ad_id</c>).
    ///
    /// <para>🔴 Bu — <b>ANIQ</b> atributsiya, ya'ni izohdagi <see cref="IgAdAttribution"/>
    /// TAXMINidan tubdan farq qiladi: qiymatni Meta'ning O'ZI beradi, biz uni
    /// <c>media.id</c> ↔ <c>CreativeStoryId</c> bo'yicha TIKLAMAYMIZ. Demak bu qiymatni
    /// «taxminiy» deb belgilash SHART EMAS (qoidalar §20.1 dagi ogohlantirish faqat IZOH
    /// atributsiyasiga tegishli).</para>
    ///
    /// <para>⚠️ Bo'sh qiymat «reklamadan kelmagan» degani EMAS: <c>referral</c> faqat
    /// SUHBATNI BOSHLAGAN xabarda keladi, keyingi xabarlarda umuman bo'lmaydi. Ya'ni
    /// atributsiya SUHBAT darajasida (birinchi xabarda bir marta) saqlanishi kerak, har
    /// xabarda kutilmasin.</para></summary>
    string AdId = "",

    /// <summary>Postback (FAQ tugmasi bosilgani) payload'i — <c>messaging[].postback.payload</c>.
    /// <para>Faqat <c>Kind == InstagramEventParser.KindPostback</c> hodisada to'ladi. Qiymat
    /// Meta bergan HOLICHA saqlanadi; "bu bizning FAQ'imizmi" qarori pipeline'da
    /// (<c>InstagramContract.FaqIdFromPayload</c>) — parser payload mazmuniga aralashmaydi.</para></summary>
    string PostbackPayload = "",

    /// <summary><c>referral.source</c> — murojaat qayerdan boshlangani (masalan <c>ADS</c>,
    /// <c>SHORTLINK</c>, <c>QR_CODE</c>, <c>IG_ME_LINK</c>).
    /// <para>Meta bergan HOLICHA saqlanadi (tarjima/normalizatsiya YO'Q): ro'yxat Meta tomonida
    /// o'sib boradi va noma'lum qiymatni "boshqa" ga aylantirish yangi kanalni JIMGINA
    /// yashirardi. <c>ad_id</c> odatda faqat <c>ADS</c> da keladi.</para></summary>
    string AdReferralSource = "",

    /// <summary>E'lon sarlavhasi (<c>referral.ads_context_data.ad_title</c>) — bo'lsa.
    /// <para>ATAYIN olinadi, chunki <b>kontekst matniga aynan SHU tushadi</b>: 17 xonali
    /// <c>ad_id</c> na AI'ga, na operatorga hech narsa aytmaydi, e'lon nomi esa aytadi
    /// («50% chegirma» e'lonidan kelgan odam aynan o'sha chegirmani so'raydi) — qarang
    /// <see cref="InstagramEventParser.ContextNote"/>.</para></summary>
    string AdTitle = "");

/// <summary>
/// Meta'ning XOM webhook JSON'ini ichki hodisalarga aylantiradi.
///
/// <para><b>Cheksiz halqa himoyasining 1-qavati SHU YERDA:</b> o'z akkauntimizdan kelgan izoh
/// (<c>from.id</c> bizniki) umuman qaytarilmaydi. NUR loyihasida bot o'z javobini begona izoh deb
/// hisoblab, unga yana javob yozgan va akkaunt spam sifatida bloklanish arafasiga kelgan.
/// Solishtirish UCHALA identifikator bo'yicha (<see cref="InstagramEventParser.IgSelf"/>):
/// saqlangan <c>IgUserId</c>, app-scoped <c>id</c>, <c>username</c> va payloaddagi
/// <c>entry.id</c> — <c>from.id</c> ba'zan biri, ba'zan boshqasi formatida keladi.</para>
///
/// <para><b>Dedup kaliti DETERMINISTIK.</b> NUR'da kalit matnning runtime hash'idan qurilgan edi:
/// har jarayonda boshqacha chiqib, restartdan keyin dedup umuman ishlamasdi. Bu yerda kalit —
/// Meta bergan <c>comment_id</c>/<c>mid</c>, ular bo'lmasa SHA-256 (barqaror).</para>
///
/// <para>Sof funksiyalar — baza/tarmoq yo'q, to'liq testlanadi. Buzuq JSON → BO'SH ro'yxat
/// (istisno otilmaydi: bitta noto'g'ri payload navbatni to'xtatib qo'ymasin).</para>
/// </summary>
public static class InstagramEventParser
{
    /// <summary>
    /// AKKAUNTIMIZNING IDENTIFIKATORLARI — halqa himoyasining 1-qavati shular bo'yicha ishlaydi.
    ///
    /// <para>⚠️ Webhook'da <c>from.id</c> <b>ba'zan</b> IG professional akkaunt id'si,
    /// <b>ba'zan</b> app-scoped id (IGSID) bo'lib keladi — bittasiga tayanish bot o'z izohini
    /// begona deb bilib, unga javob yozib CHEKSIZ HALQAGA tushishining aynan sababi.
    /// Shuning uchun uchala qiymat ham solishtiriladi.</para>
    /// </summary>
    /// <param name="IgUserId">IG professional akkaunt id (<c>me.user_id</c>).</param>
    /// <param name="AppScopedId">App-scoped id (<c>me.id</c>) — DM'larda shu keladi.</param>
    /// <param name="Username">Zaxira: id umuman kelmagan/boshqa formatdagi hollarda (registr e'tiborsiz).</param>
    public readonly record struct IgSelf(string IgUserId = "", string AppScopedId = "", string Username = "");

    /* ─────────────── E6 HODISA TURLARI VA MAYDON NOMLARI ───────────────
       ⚠️ Nega `IgConst` da EMAS: bu qiymatlar parser bilan pipeline O'RTASIDA qoladi va bazaga
       hech qachon yozilmaydi (`IgMessage.Channel` qiymatlari o'zgarmagan). Ular hodisa turining
       ICHKI belgisi, ya'ni modulning tashqi shartnomasi emas. */

    /// <summary>Mijoz o'z xabarini O'CHIRDI (<c>message.is_deleted</c>).
    /// <para>⚠️ Kalit ATAYIN <c>dm:</c> dan farq qiladi: o'chirish hodisasi AYNAN o'sha
    /// <c>mid</c> bilan keladi va kalit bir xil bo'lsa navbatdagi UNIKAL indeks uni "takror"
    /// deb rad etardi — ya'ni o'chirish so'rovi bizga UMUMAN yetib kelmasdi.</para></summary>
    public const string KindDeleted = "deleted";

    /// <summary>
    /// Meta'ning siyosat ogohlantirishi (<c>messaging_policy_enforcement</c>).
    ///
    /// <para>🔴 <b>JORIY YO'LDA BU HODISA KELMAYDI — UNGA TAYANIB BO'LMAYDI.</b> Maydon faqat
    /// «Instagram Messaging API (Messenger Platform)» yo'lida mavjud; loyiha esa «Instagram API
    /// with Instagram Login» yo'lida ishlaydi (<c>graph.instagram.com</c>,
    /// <c>instagram_business_*</c> ruxsatlari), Meta ruxsat bergan webhook maydonlari ro'yxatida
    /// esa <c>messaging_policy_enforcement</c> YO'Q — ya'ni unga <b>obuna bo'lib ham
    /// bo'lmaydi</b>.</para>
    ///
    /// <para>Shuning uchun quyidagi kodni «bizda Meta cheklovi haqida ogohlantirish bor» deb
    /// tushunmang: amalda cheklovni bilishning yagona yo'li — Graph javoblaridagi xato kodlari
    /// (190/200/10/613 …) va Meta konsolidagi akkaunt holati. Ogohlantirishga bog'lab yangi
    /// himoya QURMANG: u hech qachon ishga tushmaydi.</para>
    ///
    /// <para>Kod ATAYIN <b>o'chirilmaydi</b>: kelib qolsa to'g'ri ishlaydi va hech qanday zarar
    /// qilmaydi (yo'l almashsa yoki Meta maydonni ochsa — tayyor turadi), o'chirish esa
    /// keyinchalik xuddi shu ishni qaytadan yozishni talab qilardi.</para>
    /// </summary>
    public const string KindPolicy = "policy";

    /// <summary>
    /// FAQ tugmasi (ice breaker) bosildi — <c>messaging[].postback</c>.
    ///
    /// <para><b>Nega alohida tur:</b> postback'da <c>message</c> obyekti UMUMAN yo'q, ya'ni u
    /// ilgari "xabar emas" deb jimgina tashlanardi — tugmani bosgan mijoz javobsiz qolardi.
    /// Matn o'rnida tugma SARLAVHASI (<c>postback.title</c>) yuradi, payload esa
    /// <see cref="IgIncomingEvent.PostbackPayload"/> da.</para>
    ///
    /// <para>⚠️ Dedup kaliti <c>postback:{mid}</c> — <c>dm:</c> dan ATAYIN farq qiladi
    /// (<see cref="KindDeleted"/> bilan bir xil sabab: kalit turlari aralashsa unikal indeks
    /// boshqa turdagi hodisani "takror" deb rad etishi mumkin edi).</para>
    /// </summary>
    public const string KindPostback = "postback";

    /// <summary>Story'da eslatib o'tish attachment turi.</summary>
    public const string AttachStoryMention = "story_mention";

    /// <summary>Ulashilgan IG post attachment turi (eski <c>share</c> 2026-02-01 da olib tashlangan).</summary>
    public const string AttachIgPost = "ig_post";

    /// <summary>Siyosat ogohlantirishi keladigan webhook maydoni.
    /// <para>⚠️ Joriy yo'lda (Instagram Login) bu maydonga obuna bo'lib bo'lmaydi va hodisa
    /// KELMAYDI — batafsil <see cref="KindPolicy"/> izohida. Nom bu yerda ikki ish qiladi:
    /// (a) kelib qolgan hodisani tanish, (b) <see cref="UnsupportedFields"/> uni
    /// "qo'llab-quvvatlanmaydigan maydon" deb ogohlantirmasligi.</para></summary>
    public const string FieldPolicyEnforcement = "messaging_policy_enforcement";

    /// <summary>Story manzili kontekst matniga shuncha belgigacha kiradi (CDN havolalari juda uzun).</summary>
    private const int MaxContextUrlLength = 300;

    /// <summary>E'lon nomi kontekst matniga shuncha belgigacha kiradi.
    /// <para>Story manzilidan qisqaroq: reklama sarlavhasi odam o'qiydigan jumla, uzun bo'lsa
    /// AI promptida asosiy savolni "bo'g'ib" qo'yardi (qoidalar §21.5 dagi caption chegarasi
    /// bilan bir xil mulohaza).</para></summary>
    private const int MaxAdTitleLength = 120;

    /// <summary>Siyosat hodisasi kelishi mumkin bo'lgan kalitlar (§<see cref="TryPolicy"/>).
    /// <para>⚠️ Uchala yozilish ham ATAYIN qabul qilinadi, LEKIN bu "har ehtimolga qarshi"
    /// kengashuv: joriy yo'lda hodisaning O'ZI kelmaydi (<see cref="KindPolicy"/>).</para></summary>
    private static readonly string[] PolicyKeys =
        { "policy-enforcement", "policy_enforcement", FieldPolicyEnforcement };

    /// <summary>Xom JSON → 0..N hodisa. <paramref name="ourIgUserId"/> — bizning akkaunt id'miz
    /// (bo'sh bo'lsa ham parser ishlaydi, lekin halqa himoyasi faqat <c>entry.id</c> ga tayanadi).
    /// <para>Uchala identifikatorni beradigan ko'rinishi: <see cref="Parse(string, IgSelf)"/>.</para></summary>
    public static IReadOnlyList<IgIncomingEvent> Parse(string rawJson, string ourIgUserId) =>
        Parse(rawJson, new IgSelf(IgUserId: ourIgUserId ?? ""));

    /// <summary>Xom JSON → 0..N hodisa (halqa himoyasi UCHALA identifikator bo'yicha).</summary>
    public static IReadOnlyList<IgIncomingEvent> Parse(string rawJson, IgSelf self)
    {
        var result = new List<IgIncomingEvent>();
        if (string.IsNullOrWhiteSpace(rawJson)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawJson); }
        catch (JsonException) { return result; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var entryId = Str(entry, "id");
                var entryTime = Raw(entry, "time");

                ReadComments(entry, entryId, entryTime, self, result);
                ReadMessaging(entry, entryId, entryTime, self, result);
            }
        }

        return result;
    }

    /// <summary>
    /// Payloadda QO'LLAB-QUVVATLANMAYDIGAN <c>changes[].field</c> nomlari (masalan
    /// <c>mentions</c>, <c>live_comments</c>) — vergul bilan, takrorsiz. Hech nima bo'lmasa
    /// bo'sh satr.
    ///
    /// <para><b>Nega kerak:</b> parser bunday hodisani jimgina tashlab yuboradi va navbatda
    /// faqat "qayta ishlanadigan hodisa topilmadi" degan UMUMIY sabab qolardi. Meta'da esa
    /// keraksiz maydonga obuna bo'lib qolish oson — o'shanda admin "hodisa kelyapti, lekin
    /// hech narsa bo'lmayapti" holatini ko'rar, sababini esa hech qayerdan topa olmasdi
    /// (qoidalar §11 tuzoq 8: "ishlanmaydi, lekin LOGGA yoziladi").</para>
    ///
    /// <para>⚠️ Sof funksiya: hech narsa o'zgartirmaydi, buzuq JSON'da bo'sh satr qaytaradi.</para>
    /// </summary>
    public static string UnsupportedFields(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "";

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawJson); }
        catch (JsonException) { return ""; }

        var found = new List<string>();
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "";
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return "";

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                // (a) IZOH tomoni — `changes[].field`.
                if (entry.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ch in changes.EnumerateArray())
                    {
                        if (ch.ValueKind != JsonValueKind.Object) continue;
                        var field = Str(ch, "field");
                        if (field.Length == 0) continue;
                        // `messages` — Meta «Test» tugmasining DM konverti, biz uni ISHLAYMIZ
                        // (`ReadComments`), shuning uchun "qo'llab-quvvatlanmaydi" ro'yxatiga
                        // tushmasligi kerak.
                        if (field is "comments" or "messages" || field == FieldPolicyEnforcement) continue;
                        if (!found.Contains(field)) found.Add(field);
                    }
                }

                // (b) XABAR tomoni — `messaging[]` ELEMENTINING KALITI.
                //
                // 🔴 Bu ATAYIN qo'shildi (2026-08-22, prodda). DM hodisalarida maydon nomi
                // `changes[].field` da EMAS, `messaging[]` obyektining kalitida keladi:
                // `{"messaging":[{"message_edit":{…}}]}`. Ya'ni (a) qismi bunday hodisani
                // KO'RMASDI va navbatda faqat "qo'llab-quvvatlanmaydigan tur" degan UMUMIY
                // matn qolardi.
                //
                // Aynan shu tufayli haqiqiy nosozlik yashiringan edi: Meta bizga faqat
                // `message_edit` yuborardi (ilova darajasida `messages` maydoni belgilanmagan),
                // sabab esa hech qayerda yozilmagani uchun "DM kelmayapti" savoli javobsiz
                // qolardi.
                if (entry.TryGetProperty("messaging", out var messaging) && messaging.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in messaging.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object) continue;
                        // `message` va `postback` — biz ishlaydigan kalitlar; qolgan uchtasi
                        // konvert ma'lumoti (kim, kimga, qachon), hodisa turi emas.
                        foreach (var prop in m.EnumerateObject())
                        {
                            var key = prop.Name;
                            if (key is "message" or "postback" or "sender" or "recipient" or "timestamp") continue;
                            if (!found.Contains(key)) found.Add(key);
                        }
                    }
                }
            }
        }

        return string.Join(", ", found);
    }

    /* ---------------- izohlar (entry.changes[]) ---------------- */

    private static void ReadComments(
        JsonElement entry, string entryId, string entryTime, IgSelf self, List<IgIncomingEvent> outList)
    {
        if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) return;

        foreach (var ch in changes.EnumerateArray())
        {
            if (ch.ValueKind != JsonValueKind.Object) continue;

            // SIYOSAT OGOHLANTIRISHI `changes[]` ko'rinishida ham kelishi mumkin (shakl
            // hujjatlarda qat'iy qotirilmagan), shuning uchun ikkala ko'rinish ham qabul
            // qilinadi.
            // ⚠️ HALOL ESLATMA: joriy yo'lda (Instagram Login) bu hodisa KELMAYDI va unga
            // obuna ham bo'lib bo'lmaydi — batafsil `KindPolicy` izohida. Bu tarmoq amalda
            // O'LIK, ya'ni "bizda ogohlantirish bor" deb hisoblash XATO.
            var field = Str(ch, "field");
            if (field == FieldPolicyEnforcement)
            {
                if (ch.TryGetProperty("value", out var pv) && pv.ValueKind == JsonValueKind.Object)
                    AddPolicy(pv, entryId, entryTime, outList);
                continue;
            }

            // ── XABAR, LEKIN `changes[]` KONVERTIDA (Meta Dashboard'ning «Test» tugmasi) ──
            //
            // 🔴 2026-08-22 da prodda aniqlandi. HAQIQIY DM hodisasi `entry[].messaging[]` da
            // keladi, Meta konsolidagi «Test» tugmasi esa HAR QANDAY maydonni bir xil konvertda
            // yuboradi: `changes[].field = "messages"`, `value` ichida esa AYNAN `messaging[]`
            // elementining o'zi (`sender` · `recipient` · `timestamp` · `message`).
            //
            // Qo'llab-quvvatlanmasa sinov "Qo'llab-quvvatlanmaydigan hodisa: messages" degan
            // CHALG'ITUVCHI xato berardi — ya'ni biz ISHLAYDIGAN maydon nomi "qo'llab-
            // quvvatlanmaydi" deb ko'rsatilardi va sozlayotgan odam modulda nuqson bor deb
            // o'ylardi. Endi sinov ham uchdan-uchgacha o'tadi.
            if (field == "messages")
            {
                if (ch.TryGetProperty("value", out var mv) && mv.ValueKind == JsonValueKind.Object)
                    ReadMessagingItem(mv, entryId, entryTime, self, outList);
                continue;
            }

            // `mentions`, `live_comments` va boshqa maydonlar qo'llab-quvvatlanmaydi — ular
            // tashlanadi, lekin hodisa navbatda `skipped` bo'lib ko'rinadi (jimgina yo'qolmaydi).
            if (field != "comments") continue;
            if (!ch.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Object) continue;

            var commentId = Str(v, "id");
            var text = Str(v, "text");
            var fromId = "";
            var username = "";
            if (v.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
            {
                fromId = Str(from, "id");
                username = Str(from, "username");
            }
            var mediaId = "";
            if (v.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
                mediaId = Str(media, "id");

            // ⚠️ HALQA HIMOYASI: o'z izohimizga javob yozmaymiz.
            if (IsOurs(fromId, username, self, entryId)) continue;
            if (fromId.Length == 0 && commentId.Length == 0) continue;   // taniqli hech narsa yo'q

            var ts = Str(v, "timestamp") is { Length: > 0 } t ? t : entryTime;
            var key = EventKeyOf(IgConst.KindComment, commentId, "", fromId, ts, text);
            outList.Add(new IgIncomingEvent(
                IgConst.KindComment, text, fromId, username, commentId, mediaId, "", key, false, ToIso(ts)));
        }
    }

    /* ---------------- DM va echo (entry.messaging[]) ---------------- */

    private static void ReadMessaging(
        JsonElement entry, string entryId, string entryTime, IgSelf self, List<IgIncomingEvent> outList)
    {
        if (!entry.TryGetProperty("messaging", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

        foreach (var m in arr.EnumerateArray())
            ReadMessagingItem(m, entryId, entryTime, self, outList);
    }

    /// <summary>
    /// BITTA xabar hodisasi (`messaging[]` elementi yoki unga TENG shakl).
    ///
    /// <para><b>Nega alohida:</b> Meta Dashboard'dagi <b>«Test»</b> tugmasi AYNAN shu obyektni
    /// boshqa konvertda yuboradi — <c>changes[].field = "messages"</c> ning <c>value</c> si
    /// sifatida (<see cref="ReadComments"/> ga qarang). Ikkala konvert ham shu yerga keladi,
    /// ya'ni parse qoidasi IKKI JOYDA ayri ketmaydi.</para>
    /// </summary>
    private static void ReadMessagingItem(
        JsonElement m, string entryId, string entryTime, IgSelf self, List<IgIncomingEvent> outList)
    {
        {
            if (m.ValueKind != JsonValueKind.Object) return;

            // ── META SIYOSATI OGOHLANTIRISHI ──
            // `message` obyekti YO'Q, shuning uchun u pastdagi tekshiruvdan OLDIN ko'riladi
            // (aks holda hodisa jimgina tashlanardi).
            if (TryPolicy(m, out var policyValue))
            {
                AddPolicy(policyValue, entryId, entryTime, outList);
                return;
            }

            // ── POSTBACK (FAQ tugmasi bosildi) ──
            // `message` obyekti YO'Q, shuning uchun u pastdagi "message majburiy" tekshiruvidan
            // OLDIN ko'riladi — aks holda tugma bosilgani jimgina tashlanib, mijoz javobsiz
            // qolardi. `message` majburiyligi FAQAT shu tur uchun yumshaydi: qolgan hodisalar
            // (reaction, read, delivery) avvalgidek e'tiborga olinmaydi.
            if (!m.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            {
                if (m.TryGetProperty("postback", out var pb) && pb.ValueKind == JsonValueKind.Object)
                    ReadPostback(m, pb, entryId, entryTime, self, outList);
                return;
            }

            var senderId = Sub(m, "sender", "id");
            var recipientId = Sub(m, "recipient", "id");
            var mid = Str(msg, "mid");
            var text = Str(msg, "text");
            var ts = Raw(m, "timestamp") is { Length: > 0 } t ? t : entryTime;

            // Echo — Meta bayrog'i YOKI jo'natuvchi biz (ikki qavatli tekshiruv: bayroq
            // kelmasa ham o'z xabarimizga javob yozib qo'ymaymiz).
            var isEcho = Bool(msg, "is_echo") || IsOurs(senderId, "", self, entryId);
            var counterparty = isEcho ? recipientId : senderId;
            if (counterparty.Length == 0) return;
            if (IsOurs(counterparty, "", self, entryId)) return;   // o'zimizga o'zimiz — mumkin emas

            // ── XABAR O'CHIRILDI (`is_deleted`) ──
            // Bu YANGI xabar emas: mavjud yozuvning MAZMUNI o'chirilishi kerak (Platform Terms).
            // Kalit ham alohida (`deleted:{mid}`) — aks holda navbatdagi unikal indeks uni
            // asl xabarning takrori deb rad etardi.
            if (Bool(msg, "is_deleted"))
            {
                var delKey = EventKeyOf(KindDeleted, "", mid, counterparty, ts, text);
                outList.Add(new IgIncomingEvent(
                    KindDeleted, "", counterparty, "", "", "", mid, delKey, isEcho, ToIso(ts),
                    IsDeleted: true));
                return;
            }

            // ── STORY JAVOBI · STORY MENTION · ULASHILGAN IG POST ──
            var (storyId, storyUrl) = ReadStoryReply(msg);
            var (isMention, mentionUrl, hasPost, postUrl) = ReadAttachments(msg);

            // ── REKLAMA ATRIBUTSIYASI (`referral`) ──
            // ⚠️ Reklamadan kelgan DM'da e'lon id'si ANIQ ko'rinishda keladi; uni o'qimaslik
            // TO'LIQ JIMGINA yo'qotish edi (suhbat `?source=ads` filtriga tushmasdi, ROI
            // hisobotida esa reklama samarasi kam ko'rinardi, xato esa chiqmasdi).
            var (adId, adSource, adTitle) = ReadReferral(m, msg);

            var kind = isEcho ? IgConst.KindEcho : IgConst.KindDm;
            var key = EventKeyOf(kind, "", mid, counterparty, ts, text);
            outList.Add(new IgIncomingEvent(
                kind, text, counterparty, "", "", "", mid, key, isEcho, ToIso(ts),
                StoryId: storyId,
                // Story rasmi manzili ikki yo'ldan keladi: javobda `reply_to.story.url`,
                // eslatishda esa attachment payload'ida. Ustun — javobniki.
                StoryUrl: storyUrl.Length > 0 ? storyUrl : mentionUrl,
                IsStoryMention: isMention, HasSharedPost: hasPost, SharedPostUrl: postUrl,
                AdId: adId, AdReferralSource: adSource, AdTitle: adTitle));
        }
    }

    /// <summary>
    /// FAQ tugmasi (ice breaker) postback'i: <c>messaging[].postback.{mid, title, payload}</c>.
    ///
    /// <para>Matn — tugma SARLAVHASI (<c>title</c>): u suhbat lentasida mijozning "savoli"
    /// bo'lib ko'rinadi (payload esa texnik qiymat, uni lentaga chiqarish operatorga hech
    /// narsa aytmasdi). Payload <see cref="IgIncomingEvent.PostbackPayload"/> da alohida
    /// yuradi — mos FAQ javobini pipeline aynan u bo'yicha topadi.</para>
    ///
    /// <para>⚠️ Halqa himoyasi bu yerda ham ishlaydi (o'z akkauntimizdan kelgan postback
    /// tashlanadi) va <c>postback.referral</c> dagi reklama atributsiyasi ham o'qiladi
    /// (mavjud <see cref="ReadReferral"/> bilan bir xil ustunlik qoidasi).</para>
    /// </summary>
    private static void ReadPostback(
        JsonElement m, JsonElement pb, string entryId, string entryTime, IgSelf self, List<IgIncomingEvent> outList)
    {
        var senderId = Sub(m, "sender", "id");
        if (senderId.Length == 0) return;
        if (IsOurs(senderId, "", self, entryId)) return;   // halqa himoyasi — o'z tugmamiz "bosilishi"

        var mid = Str(pb, "mid");
        var title = Str(pb, "title");
        var payload = Str(pb, "payload");
        if (payload.Length == 0 && title.Length == 0 && mid.Length == 0) return;   // taniqli hech narsa yo'q

        var ts = Raw(m, "timestamp") is { Length: > 0 } t ? t : entryTime;

        // Reklama atributsiyasi: postback ichidagi (yoki elementdagi) `referral`.
        var (adId, adSource, adTitle) = ("", "", "");
        if (TryReferral(pb, out var r) || TryReferral(m, out r))
        {
            adTitle = r.TryGetProperty("ads_context_data", out var ctx) && ctx.ValueKind == JsonValueKind.Object
                ? Str(ctx, "ad_title")
                : "";
            adId = Raw(r, "ad_id");
            adSource = Str(r, "source");
        }

        // ⚠️ Kalit hash tarmog'ida MATN sifatida PAYLOAD ishlatiladi (title emas): tugmani
        // aynan payload identifikatsiya qiladi va u tahrirlanmaydi — kalit barqaror qoladi.
        var key = EventKeyOf(KindPostback, "", mid, senderId, ts, payload);
        outList.Add(new IgIncomingEvent(
            KindPostback, title, senderId, "", "", "", mid, key, false, ToIso(ts),
            PostbackPayload: payload,
            AdId: adId, AdReferralSource: adSource, AdTitle: adTitle));
    }

    /* ---------------- E6: story, attachment, o'chirish, siyosat ---------------- */

    /// <summary>Story'ga javob: <c>message.reply_to.story.{id,url}</c>.
    /// <para>⚠️ <c>reply_to</c> ODATIY xabarga javobda ham keladi (u yerda faqat <c>mid</c>) —
    /// story konteksti FAQAT ichki <c>story</c> obyekti bo'lganda paydo bo'ladi.</para></summary>
    private static (string Id, string Url) ReadStoryReply(JsonElement msg)
    {
        if (!msg.TryGetProperty("reply_to", out var replyTo) || replyTo.ValueKind != JsonValueKind.Object)
            return ("", "");
        if (!replyTo.TryGetProperty("story", out var story) || story.ValueKind != JsonValueKind.Object)
            return ("", "");
        return (Str(story, "id"), Str(story, "url"));
    }

    /// <summary>Attachment turlari: story'da eslatish va ulashilgan IG post.
    /// <para>⚠️ Eski <c>share</c> turi 2026-02-01 da olib tashlangan — <c>ig_post</c> ko'riladi.
    /// Qolgan turlar (rasm, video, ovoz) bu yerda e'tiborga olinmaydi: ular uchun mavjud
    /// "matnsiz xabar → operator" qoidasi ishlaydi.</para></summary>
    private static (bool StoryMention, string MentionUrl, bool SharedPost, string PostUrl) ReadAttachments(
        JsonElement msg)
    {
        if (!msg.TryGetProperty("attachments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (false, "", false, "");

        var mention = false;
        var mentionUrl = "";
        var post = false;
        var postUrl = "";
        foreach (var a in arr.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            var type = Str(a, "type");
            var payloadUrl = a.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object
                ? Str(p, "url")
                : "";

            if (string.Equals(type, AttachStoryMention, StringComparison.OrdinalIgnoreCase))
            {
                mention = true;
                if (mentionUrl.Length == 0) mentionUrl = payloadUrl;
            }
            else if (string.Equals(type, AttachIgPost, StringComparison.OrdinalIgnoreCase))
            {
                post = true;
                if (postUrl.Length == 0) postUrl = payloadUrl;
            }
        }

        return (mention, mentionUrl, post, postUrl);
    }

    /// <summary>
    /// REKLAMA ATRIBUTSIYASI: <c>referral.{ad_id, source, ads_context_data.ad_title}</c>.
    ///
    /// <para>«Click to Instagram Direct» reklamasini bosgan odam DM yozganda Meta e'lon id'sini
    /// O'ZI yuboradi — ya'ni bu <b>ANIQ</b> atributsiya (izohdagi <see cref="IgAdAttribution"/>
    /// esa TAXMINIY: u yerda <c>ad_id</c> umuman kelmaydi va bog'lanish <c>media.id</c> orqali
    /// tiklanadi).</para>
    ///
    /// <para>⚠️ Obyekt UCH joydan kelishi mumkin, parser uchalasini ham ko'radi (ustunlik
    /// tartibida):</para>
    /// <list type="number">
    ///   <item><c>message.referral</c> — reklamadan kelgan odam BIRINCHI xabarni yozganda
    ///         (amaldagi ASOSIY holat);</item>
    ///   <item><c>messaging[].referral</c> — xabardan TASHQARIDA (m.me havolasi, ice breaker);</item>
    ///   <item><c>messaging[].postback.referral</c> — tugma bosilganda.</item>
    /// </list>
    /// <para>Ustunlik <c>message</c> nikida: xabar bilan birga kelgan referral aynan SHU
    /// xabarga tegishli, tashqaridagisi esa umumiy suhbat konteksti bo'lishi mumkin.</para>
    ///
    /// <para>⚠️ <b>ANIQ CHEGARA:</b> <c>message</c> obyekti UMUMAN bo'lmagan sof
    /// <c>referral</c>/<c>postback</c> hodisasi bu yergacha yetib kelmaydi — u yuqorida
    /// (<c>message</c> tekshiruvida) tashlanadi. Uni qaytarish uchun YANGI hodisa turi kerak
    /// bo'lardi: matnsiz "xabar" suhbat lentasiga bo'sh qator bo'lib tushardi va dedup kaliti
    /// ham boshqacha qurilishi kerak edi. Click-to-Direct reklamasida referral BIRINCHI XABAR
    /// bilan birga keladi, ya'ni asosiy holat qoplangan; qolgani — ochiq ish.</para>
    /// </summary>
    private static (string AdId, string Source, string Title) ReadReferral(JsonElement m, JsonElement msg)
    {
        var found = TryReferral(msg, out var r)
                    || TryReferral(m, out r)
                    || (m.TryGetProperty("postback", out var pb) && TryReferral(pb, out r));
        if (!found) return ("", "", "");

        var title = r.TryGetProperty("ads_context_data", out var ctx) && ctx.ValueKind == JsonValueKind.Object
            ? Str(ctx, "ad_title")
            : "";

        // ⚠️ `ad_id` ATAYIN `Raw` bilan o'qiladi: Meta uni odatda MATN qilib beradi, lekin
        // ba'zi payloadlarda SON bo'lib keladi — `Str` bunday qiymatni jimgina bo'sh qaytarardi
        // va atributsiya yana yo'qolardi.
        return (Raw(r, "ad_id"), Str(r, "source"), title);
    }

    /// <summary>Berilgan tugunda <c>referral</c> OBYEKTI bormi.
    /// <para>Obyekt bo'lmagan qiymat (satr, <c>null</c>, massiv) jimgina "yo'q" hisoblanadi —
    /// buzuq payload butun hodisani yiqitmasin (sinf izohidagi umumiy siyosat).</para></summary>
    private static bool TryReferral(JsonElement e, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("referral", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// <c>messaging[]</c> elementida siyosat ogohlantirishi bormi.
    ///
    /// <para>⚠️ Meta hujjatida maydon <c>messaging_policy_enforcement</c> deb ataladi, hodisa
    /// obyekti esa <c>policy-enforcement</c> (DEFIS bilan) kaliti ostida keladi. Shakl QAT'IY
    /// qotirilmagan, shuning uchun parser ATAYIN kechirimli: uchala yozilishni ham qabul
    /// qiladi.</para>
    ///
    /// <para>🔴 <b>Lekin joriy yo'lda bu hodisa umuman KELMAYDI</b> (<see cref="KindPolicy"/>):
    /// maydon Messenger Platform yo'liga tegishli, bizdagi Instagram Login yo'lida esa unga
    /// obuna bo'lib bo'lmaydi. Ya'ni bu metod amalda HECH QACHON <c>true</c> qaytarmaydi —
    /// undan kelayotgan "signal" ni rejaga kiritmang.</para>
    /// </summary>
    private static bool TryPolicy(JsonElement m, out JsonElement value)
    {
        foreach (var name in PolicyKeys)
        {
            if (m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            {
                value = v;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Siyosat hodisasini ro'yxatga qo'shadi. Kalit deterministik: <c>action</c> va
    /// <c>reason</c> ning barqaror hash'i (Meta bu hodisada <c>mid</c> bermaydi).</summary>
    private static void AddPolicy(JsonElement value, string entryId, string entryTime, List<IgIncomingEvent> outList)
    {
        var action = Str(value, "action");
        var reason = Str(value, "reason");
        if (action.Length == 0 && reason.Length == 0) action = "warning";   // shakl kutilmagan — signal baribir qoladi

        var key = EventKeyOf(KindPolicy, "", "", entryId, entryTime, action + "|" + reason);
        outList.Add(new IgIncomingEvent(
            KindPolicy, "", entryId, "", "", "", "", key, false, ToIso(entryTime),
            PolicyAction: action, PolicyReason: reason));
    }

    /// <summary>
    /// Payloadning <c>object</c> maydoni — <b>qaysi Meta mahsuloti</b> yuborganini bildiradi
    /// (<c>instagram</c> — izoh/DM, <c>page</c> — Facebook Page / reklama lidi).
    ///
    /// <para><b>Nega kerak:</b> imzo mos kelmagan so'rov qayta ishlanmaydi, ya'ni "nima keldi"
    /// hech qayerda ko'rinmaydi. Kalit xatosini tuzatish esa AYNAN shu javobga bog'liq:
    /// <c>instagram</c> bo'lsa Instagram App Secret kerak, <c>page</c> bo'lsa so'rov umuman
    /// boshqa manzilga (<c>/leadgen</c>) ketishi kerak edi.</para>
    ///
    /// <para>⚠️ Natija ATAYIN <b>oq ro'yxatdan</b>: qiymatni tashqaridagi yuboruvchi belgilaydi
    /// va u to'g'ridan-to'g'ri logga tushadi (log injection — <c>ShortUa</c> bilan bir xil
    /// mulohaza). Notanish qiymat "boshqa" deb yoziladi, xom matn sifatida EMAS.</para>
    ///
    /// <para>Sof funksiya: buzuq JSON'da ham yiqilmaydi, "noma'lum" qaytaradi.</para>
    /// </summary>
    public static string ObjectType(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "noma'lum";

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "noma'lum";

            var value = Str(root, "object");
            return value switch
            {
                "instagram" => "instagram (izoh/DM)",
                "page" => "page (Facebook Page / reklama lidi)",
                "user" or "permissions" or "application" => value,
                "" => "noma'lum",
                _ => "boshqa",
            };
        }
        catch (JsonException)
        {
            return "noma'lum";
        }
    }

    /// <summary>
    /// Payloadda siyosat ogohlantirishi bormi — <b>webhook controlleri</b> uchun tez tekshiruv.
    /// <para>Navbat fon xizmatida qayta ishlanadi, ya'ni ogohlantirish logga bir necha soniya
    /// (yoki modul o'chiq bo'lsa — umuman) kechikib tushardi. Bu funksiya so'rov kelgan
    /// ZAHOTI log yozish imkonini beradi va HECH QANDAY og'ir ish qilmaydi.</para>
    /// <para>⚠️ Joriy yo'lda amalda DOIM <c>false</c> qaytaradi — hodisa Instagram Login
    /// webhook'ida mavjud emas (<see cref="KindPolicy"/>). Uni "monitoring bor" deb hisoblamang.</para>
    /// </summary>
    public static bool ContainsPolicyEnforcement(string? rawJson)
    {
        var json = rawJson ?? "";
        // Arzon oldingi tekshiruv: payloadlarning aksariyatida bu so'z umuman yo'q.
        if (json.IndexOf("policy", StringComparison.OrdinalIgnoreCase) < 0) return false;

        foreach (var e in Parse(json, new IgSelf()))
            if (e.Kind == KindPolicy) return true;
        return false;
    }

    /// <summary>
    /// Xabarning QO'SHIMCHA KONTEKSTI o'zbekcha, bitta qatorda ("[Story'ga javob …]").
    ///
    /// <para>Nega matnga qo'shiladi: story id/url uchun alohida ustun YO'Q (migratsiya bu
    /// bosqichda qilinmaydi), AI esa "nimaga javob yozilyapti" ni bilmasa mazmunsiz javob
    /// beradi ("Salom!" degan story javobi kontekstsiz umuman tushunarsiz). Kontekst
    /// operatorga ham ko'rinadi — lentada xabar ustida turadi.</para>
    ///
    /// <para>Konteksti yo'q oddiy xabarda BO'SH satr qaytadi, ya'ni mavjud xulq o'zgarmaydi.</para>
    ///
    /// <para><b>REKLAMA KONTEKSTI — QAROR (E7):</b> matnga faqat e'lonning NOMI qo'shiladi
    /// («Reklamadan keldi: …»), <c>ad_id</c> ning O'ZI esa QO'SHILMAYDI. Uch sabab:
    /// (1) 17 xonali raqam na AI'ga, na operatorga hech narsa aytmaydi va AI uni mijozga
    /// qaytarib yozib qo'yishi mumkin edi; (2) atributsiya baribir MAYDON sifatida
    /// (<c>AdId</c>/<c>AdReferralSource</c>) tuzilma darajasida saqlanadi va Inbox'da kampaniya
    /// chipi bo'lib chiziladi — matnga takrorlash shovqin bo'lardi; (3) e'lon NOMI esa haqiqiy
    /// kontekst: reklamada nima va'da qilingan bo'lsa, odam aynan shuni so'raydi, AI esa buni
    /// bilmasa mavzuni noldan izlab yurardi.</para>
    /// </summary>
    public static string ContextNote(IgIncomingEvent e)
    {
        var parts = new List<string>();

        // ⚠️ REKLAMA BIRINCHI o'rinda: "bu odam qayerdan keldi" savoli javobning MAVZUSINI
        // belgilaydi, story/post esa faqat "nimaga javob yozilyapti" ni aniqlaydi.
        if (e.AdId.Length > 0 || e.AdTitle.Length > 0)
            parts.Add(e.AdTitle.Length > 0
                ? $"Reklamadan keldi: {Shorten(e.AdTitle, MaxAdTitleLength)}"
                : "Reklamadan keldi");

        // ⚠️ ESLATISH ustun: mijoz o'z story'sida bizni belgilagan bo'lsa bu "javob" emas,
        // boshqa hodisa — ikkalasi bir vaqtda kelsa ham matn chalkashmasin.
        if (e.IsStoryMention)
        {
            var s = "Story'da bizni eslatib o'tdi";
            if (e.StoryUrl.Length > 0) s += $" · rasm: {ShortUrl(e.StoryUrl)}";
            parts.Add(s);
        }
        else if (e.StoryId.Length > 0 || e.StoryUrl.Length > 0)
        {
            var s = "Story'ga javob";
            if (e.StoryId.Length > 0) s += $" · story: {e.StoryId}";
            if (e.StoryUrl.Length > 0) s += $" · rasm: {ShortUrl(e.StoryUrl)}";
            parts.Add(s);
        }

        if (e.HasSharedPost)
            parts.Add(e.SharedPostUrl.Length > 0
                ? $"IG post ulashildi: {ShortUrl(e.SharedPostUrl)}"
                : "IG post ulashildi");

        return parts.Count == 0 ? "" : "[" + string.Join(" | ", parts) + "]";
    }

    /// <summary>Kontekst matni CDN havolasi bilan cheksiz uzayib ketmasin.</summary>
    private static string ShortUrl(string url) => Shorten(url, MaxContextUrlLength);

    /// <summary>Kontekst bo'lagini chegaraga solish. Qirqilgani <b>KO'RINIB tursin</b> uchun
    /// oxiriga «…» qo'yiladi (jimgina kesish o'qiyotgan odamni aldardi —
    /// <c>InstagramCaptionService</c> dagi bilan bir xil qoida).</summary>
    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    /* ---------------- dedup kaliti ---------------- */

    /// <summary>
    /// DETERMINISTIK dedup kaliti: izohda <c>comment_id</c>, DM/echo'da <c>mid</c>; ikkalasi ham
    /// bo'lmasa jo'natuvchi + vaqt + matnning SHA-256 hash'i (16 hex belgi).
    /// <para>⚠️ <c>Guid</c>, <c>DateTime.Now</c> yoki <c>string.GetHashCode()</c> ISHLATILMAYDI —
    /// bir xil hodisa qayta kelganda AYNAN bir xil kalit chiqishi shart, aks holda Meta'ning
    /// 36 soatlik qayta yuborishlari mijozga takroriy javob bo'lib ketardi.</para>
    /// </summary>
    public static string EventKeyOf(string kind, string commentId, string mid, string senderId, string timestamp, string text)
    {
        if (kind == IgConst.KindComment && !string.IsNullOrWhiteSpace(commentId))
            return $"comment:{commentId.Trim()}";
        if (!string.IsNullOrWhiteSpace(mid))
            return $"{kind}:{mid.Trim()}";
        return $"{kind}:{senderId}:{timestamp}:{Sha256Short(text)}";
    }

    private static string Sha256Short(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    /* ---------------- yordamchilar ---------------- */

    /// <summary>Berilgan id bizga tegishlimi — saqlangan akkaunt id'si YOKI payloaddagi
    /// <c>entry.id</c> bilan mos kelsa (ID formatlari farq qilishi mumkin).</summary>
    /// <summary>
    /// Bu yozuv BIZNIKIMI — cheksiz halqa himoyasining 1-qavati.
    ///
    /// <para>To'rtta manba solishtiriladi: saqlangan IG id, saqlangan app-scoped id,
    /// payloaddagi <c>entry.id</c> (shu hodisa qaysi akkauntga tegishli) va zaxira sifatida
    /// <c>username</c> (registr e'tiborsiz). Bittasiga tayanish — halqaning aynan sababi
    /// (<c>marketing-instagram.md</c> §4).</para>
    /// </summary>
    private static bool IsOurs(string id, string username, IgSelf self, string entryId)
    {
        if (id.Length > 0)
        {
            if (self.IgUserId.Length > 0 && string.Equals(id, self.IgUserId, StringComparison.Ordinal)) return true;
            if (self.AppScopedId.Length > 0 && string.Equals(id, self.AppScopedId, StringComparison.Ordinal)) return true;
            if (entryId.Length > 0 && string.Equals(id, entryId, StringComparison.Ordinal)) return true;
        }
        // Zaxira: id formati kutilmagan bo'lsa ham o'z username'imizga javob yozmaymiz.
        return username.Length > 0 && self.Username.Length > 0
               && string.Equals(username.TrimStart('@'), self.Username.TrimStart('@'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Meta vaqtini loyihaning ISO ko'rinishiga o'giradi ("yyyy-MM-ddTHH:mm:ss", MAHALLIY vaqt).
    ///
    /// <para>Meta ikki xil beradi: <c>entry.time</c> — epoch (soniya yoki millisekund),
    /// izoh <c>value.timestamp</c> — ISO satr. O'qib bo'lmasa BO'SH qaytadi va chaqiruvchi
    /// o'zining joriy vaqtiga qaytadi — noto'g'ri vaqt yozgandan ko'ra "noma'lum" yaxshiroq.</para>
    /// </summary>
    internal static string ToIso(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "";

        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num > 0)
        {
            // 13 xonali — millisekund, 10 xonali — soniya (Meta ikkalasini ham ishlatadi).
            var utc = v.Length >= 12
                ? DateTimeOffset.FromUnixTimeMilliseconds(num)
                : DateTimeOffset.FromUnixTimeSeconds(num);
            return utc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        return DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
            ? iso.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : "";
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static string Raw(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? v.ToString()
            : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Sub(JsonElement e, string obj, string name) =>
        e.TryGetProperty(obj, out var o) && o.ValueKind == JsonValueKind.Object ? Str(o, name) : "";
}
