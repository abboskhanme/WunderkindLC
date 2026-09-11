using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KONTENT REJALASHTIRISH (Instagram Content Publishing) — KONSTANTALAR (yagona manba).
///
/// <para><b>🔴 INSTAGRAM'DA NATIVE REJALASHTIRISH YO'Q.</b> <c>POST /{ig-user-id}/media</c> da
/// <c>scheduled_publish_time</c> parametri <b>mavjud emas</b> (bu faqat Facebook Page
/// <c>/feed</c> da bor). Demak rejalashtirilgan vaqt FAQAT bizning navbatimizda
/// (<c>IgScheduledPost.ScheduledAt</c>) turadi va konteyner <b>oldindan yaratilmaydi</b> —
/// faqat chop etish vaqti kelganda. Sabab: konteyner <b>24 soatdan keyin o'ladi</b>
/// (<see cref="ContainerLifetimeHours"/>), ya'ni "ertaga ertalabga" oldindan yaratilgan
/// konteyner vaqti kelganda allaqachon <c>EXPIRED</c> bo'lardi va post jimgina yo'qolardi.</para>
///
/// <para>Xom satr ("REELS", "FINISHED", "published") kodning boshqa joyida yozilmaydi —
/// <see cref="IgConst"/> bilan bir xil qoida.</para>
/// </summary>
public static class IgPublishConst
{
    /* ═════════════ Post turlari (IgScheduledPost.PostType) ═════════════ */

    public const string TypeImage = "image";
    public const string TypeVideo = "video";
    public const string TypeReels = "reels";
    public const string TypeStory = "story";
    public const string TypeCarousel = "carousel";

    public static readonly string[] PostTypes = { TypeImage, TypeVideo, TypeReels, TypeStory, TypeCarousel };

    /* ═════════════ Post holatlari (IgScheduledPost.Status) ═════════════ */

    public const string StScheduled = "scheduled";
    public const string StProcessing = "processing";
    public const string StPublished = "published";
    public const string StFailed = "failed";
    public const string StCancelled = "cancelled";

    public static readonly string[] Statuses = { StScheduled, StProcessing, StPublished, StFailed, StCancelled };

    /* ═════════════ Konteyner holatlari (Meta: status_code) ═════════════ */

    public const string CsInProgress = "IN_PROGRESS";
    public const string CsFinished = "FINISHED";
    public const string CsError = "ERROR";
    public const string CsExpired = "EXPIRED";
    public const string CsPublished = "PUBLISHED";

    /* ═════════════ Meta parametr qiymatlari (media_type) ═════════════ */

    public const string MtImage = "IMAGE";
    public const string MtVideo = "VIDEO";
    public const string MtReels = "REELS";
    public const string MtStories = "STORIES";
    public const string MtCarousel = "CAROUSEL";

    /// <summary>Media elementining turi (bizning ichki nomlanish).</summary>
    public const string KindImage = "image", KindVideo = "video";

    /* ═════════════ Caption chegaralari ═════════════ */

    /// <summary>Caption uzunligi (Meta: 2200 belgi).</summary>
    public const int MaxCaptionLength = 2200;
    /// <summary>Bir postdagi hashtag chegarasi.</summary>
    public const int MaxHashtags = 30;
    /// <summary>Bir postdagi mention (@username) chegarasi.</summary>
    public const int MaxMentions = 20;
    /// <summary><c>alt_text</c> chegarasi (faqat yakka rasm, 2025-03-24 dan).</summary>
    public const int MaxAltTextLength = 1000;
    /// <summary><c>collaborators</c> — ko'pi bilan 3 ta username (ular qabul qilishi kerak).</summary>
    public const int MaxCollaborators = 3;

    /* ═════════════ Media chegaralari (§5.5) ═════════════ */

    public const long MaxImageBytes = 8L * 1024 * 1024;        // rasm ≤ 8 MB

    /// <summary>
    /// Reels/video hajmi — <b>1 GB</b> (Meta spetsifikatsiyasi).
    /// <para>⚠️ Ilgari bu yerda <b>300 MB</b> turardi — bu <b>bizning</b> cheklovimiz edi, Meta'niki
    /// emas: telefonda olingan bir daqiqalik 4K video ham 300 MB dan oshadi va post
    /// Instagram'ga <b>umuman yuborilmasdi</b>. Qoida bitta: bu yerda faqat Meta'ning
    /// haqiqiy chegarasi turadi — "ehtiyot uchun" qisqartirilgan son ishlaydigan postni
    /// jimgina to'sib qo'yadi.</para>
    /// <para>⚠️ Bu konstanta yuklash marshrutining hajm chegarasi hamdir
    /// (<c>InstagramController.Media</c> dagi <c>RequestSizeLimit</c> va
    /// <c>MarketingPublicMedia.MaxBytesFor</c>) — ya'ni o'zgartirilsa yuklanadigan faylning
    /// eng katta hajmi ham o'zgaradi. Bu ATAYIN bitta joydan: "yuklab bo'ldi, lekin joylab
    /// bo'lmadi" holati eng chalg'ituvchisi bo'lardi.</para>
    /// </summary>
    public const long MaxReelsBytes = 1024L * 1024 * 1024;     // reels ≤ 1 GB (Meta)

    public const long MaxStoryVideoBytes = 100L * 1024 * 1024; // story video ≤ 100 MB

    public const double MinReelsSeconds = 3, MaxReelsSeconds = 900;   // 3 s – 15 daqiqa
    public const double MinStoryVideoSeconds = 3, MaxStoryVideoSeconds = 60;

    /// <summary>Feed rasmi nisbati: 4:5 (=0.8) dan 1.91:1 gacha.</summary>
    public const double FeedMinRatio = 0.8, FeedMaxRatio = 1.91;
    /// <summary>Feed rasmining kengligi (px).</summary>
    public const int FeedMinWidth = 320, FeedMaxWidth = 1440;

    /// <summary>
    /// <b>STORY</b> nisbati — 9:16 (to'liq ekran).
    /// <para>⚠️ Nomi "Story" — bu qiymat REELS uchun <b>MAJBURIY EMAS</b> (qarang
    /// <see cref="VideoMinRatio"/>): story haqiqatan butun ekranni egallaydi, reels esa
    /// lentada ham ko'rinadi va Instagram uni o'zi moslaydi.</para>
    /// </summary>
    public const double StoryRatio = 9.0 / 16.0;

    /// <summary>
    /// 9:16 dan ruxsat etilgan chetlanish (STORY uchun qattiq, REELS uchun faqat
    /// <b>ogohlantirish</b> chegarasi — <see cref="InstagramPublishContract.MediaWarning"/>).
    /// <para>⚠️ Aynan 0.5625 talab qilinsa 1080×1921 kabi bir piksellik farq ham postni rad
    /// etardi. Chetlanish ATAYIN kichik (±0.02): kengroq nisbat Instagram tomonidan qirqiladi
    /// va foydalanuvchi buni "rasmimning yarmi kesilib qolibdi" deb ko'radi.</para>
    /// </summary>
    public const double StoryRatioTolerance = 0.02;

    /// <summary>
    /// <b>VIDEO/REELS nisbatining HAQIQIY chegarasi — 0.01:1 dan 10:1 gacha</b> (Meta
    /// spetsifikatsiyasi, Reels va feed videosi uchun bir xil).
    ///
    /// <para>⚠️ Ilgari video uchun <b>9:16 MAJBURIY</b> edi va 1:1 (kvadrat) yoki 4:5
    /// (vertikal lenta) video "Video 9:16 nisbatda bo'lishi kerak" xatosi bilan Instagram'ga
    /// <b>umuman yuborilmasdi</b>. Bu bizning cheklovimiz edi: Meta bunday videoni qabul
    /// qiladi va kerak bo'lsa o'zi qirqadi/chekka qo'shadi.</para>
    ///
    /// <para>9:16 dan uzoq nisbat endi <b>XATO EMAS, OGOHLANTIRISH</b>
    /// (<see cref="InstagramPublishContract.MediaWarning"/>) — qaror foydalanuvchining
    /// o'zida qoladi. Diapazon esa "aql bovar qilmaydigan" qiymatni (masalan 20:1 lenta)
    /// baribir ushlab qoladi, aks holda post faqat 10 daqiqalik poll'dan keyin
    /// <c>2207009</c> bilan yiqilardi.</para>
    ///
    /// <para>⚠️ STORY bu diapazonga KIRMAYDI — u yerda 9:16 talabi qoladi
    /// (<see cref="StoryRatio"/>).</para>
    /// </summary>
    public const double VideoMinRatio = 0.01, VideoMaxRatio = 10.0;

    public const int MinCarouselItems = 2, MaxCarouselItems = 10;

    /* ═════════════ Poll jadvali (§5.3) ═════════════ */

    /// <summary>Konteyner holatini so'rash jadvali (soniya): 30 → 60 → 120 → 300 → 300 …
    /// <para>Meta tavsiyasi: "daqiqada bir marta, 5 daqiqadan ko'p emas" — oxirgi qadam
    /// aynan 300 s (5 daqiqa) da to'xtaydi.</para></summary>
    public static readonly int[] PollDelaysSeconds = { 30, 60, 120, 300 };

    /// <summary>Shuncha soniyadan keyin poll to'xtaydi va post <c>failed</c> bo'ladi (10 daqiqa).</summary>
    public const int PollTimeoutSeconds = 600;

    /// <summary>Konteyner yaratilgandan keyin shuncha soatdan so'ng o'ladi (Meta qat'iy qoidasi).</summary>
    public const int ContainerLifetimeHours = 24;

    /// <summary>Post shuncha marta urinilgach <c>failed</c> bo'ladi (<see cref="IgConst.MaxAttempts"/> bilan bir xil).</summary>
    public const int MaxAttempts = 3;

    /// <summary>Bir siklda ko'pi bilan shuncha post chop etiladi (§5.7).</summary>
    public const int QueueBatch = 3;

    /* ═════════════ Chop etish limiti (§5.4) ═════════════ */

    /// <summary>
    /// ⚠️ <b>quota_total KODGA YOZILMAYDI.</b> Meta hujjatlari zid: qo'llanmada 24 soatda
    /// <b>100</b> post, reference namunasida esa <b>50</b>. Qiymat ish vaqtida
    /// <c>content_publishing_limit</c> javobidagi <c>config.quota_total</c> dan o'qiladi
    /// (<see cref="InstagramPublishApi.GetPublishingLimitAsync"/>). Bu konstanta faqat
    /// "noma'lum" belgisidir.
    /// </summary>
    public const int UnknownQuota = 0;

    /* ═════════════ Xato kodlari (§5.8) ═════════════ */

    /* ⚠️ Rasmiy Instagram publishing xato kodlari sahifasi MAVJUD EMAS — quyidagilar
       amaliyotdan (uchinchi tomon manbasi) olingan. Shuning uchun xarita "yopiq" emas:
       noma'lum kod ham UMUMIY matn oladi, jimgina yutilmaydi (§ErrorText). */

    public const int ErrMediaDownload = 2207052;   // media yuklab bo'lmadi (ENG KO'P uchraydi)
    public const int ErrContainerExpired = 2207020; // konteyner muddati o'tdi (24 soat)
    public const int ErrDownloadTimeout = 2207003;  // yuklab olish timeout
    public const int ErrNotJpeg = 2207005;          // JPEG emas
    public const int ErrBadRatio = 2207009;         // nisbat noto'g'ri
    public const int ErrCaptionTooLong = 2207010;   // caption juda uzun
    public const int ErrVideoCodec = 2207026;       // video kodek qo'llab-quvvatlanmaydi
    public const int ErrDailyLimit = 2207042;       // kunlik limit
    public const int ErrSpam = 2207001;             // spam deb belgilandi

    /// <summary>
    /// OAuth ruxsati — kontent chop etish uchun QO'SHIMCHA scope.
    /// <para>⚠️ Bu satr <see cref="IgConst.Scopes"/> ga qo'shilishi kerak va scope
    /// qo'shilishi <b>qayta OAuth</b> talab qiladi (Sozlamalarda «Qayta ulash»).</para>
    /// </summary>
    public const string PublishScope = "instagram_business_content_publish";
}

/// <summary>
/// Rejalashtirilgan postning BITTA media elementi (<c>IgScheduledPost.MediaJson</c> dagi yozuv).
///
/// <para>⚠️ O'lchamlar (<paramref name="SizeBytes"/>, <paramref name="DurationSeconds"/>,
/// <paramref name="Width"/>, <paramref name="Height"/>) <b>0 bo'lsa "noma'lum"</b> degani va
/// tegishli tekshiruv O'TKAZIB YUBORILADI. Sabab: fayl serverda o'lchanmagan bo'lishi mumkin
/// (masalan tashqi CDN havolasi), va "bilmasak — rad etamiz" qoidasi butunlay ishlaydigan
/// postlarni to'sib qo'yardi. Bu holda qarorni Meta chiqaradi va xato kodi
/// (<c>2207005</c>/<c>2207009</c>) o'zbekcha matnga aylantiriladi.</para>
///
/// <para><paramref name="Caption"/> — FAQAT karusel bolasidagi xatoni ushlash uchun:
/// Meta karusel BOLALARIDA caption'ni e'tiborsiz qoldiradi, ya'ni foydalanuvchi yozgan matn
/// jimgina yo'qolardi.</para>
/// </summary>
public record IgMediaItem(
    string Url,
    string Kind = IgPublishConst.KindImage,
    long SizeBytes = 0,
    double DurationSeconds = 0,
    int Width = 0,
    int Height = 0,
    string CoverUrl = "",
    long ThumbOffsetMs = -1,
    string AltText = "",
    string Caption = "");

/// <summary>Post sozlamalari (<c>IgScheduledPost.OptionsJson</c>).</summary>
public record IgPublishOptions(
    bool ShareToFeed = true,
    string LocationId = "",
    IReadOnlyList<string>? Collaborators = null,
    string AudioName = "");

/// <summary>
/// <c>POST /{ig-user-id}/media</c> uchun tayyor parametrlar to'plami.
/// <para>Buni <see cref="InstagramPublishContract.BuildContainerRequest"/> quradi (SOF funksiya),
/// <see cref="InstagramPublishApi"/> esa faqat forma qilib yuboradi — "qaysi parametr qaysi
/// turga tegishli" qoidasi HTTP kodida takrorlanmasin.</para>
/// </summary>
public record IgContainerRequest(
    string MediaType = "",
    string ImageUrl = "",
    string VideoUrl = "",
    string Caption = "",
    string CoverUrl = "",
    long ThumbOffsetMs = -1,
    bool ShareToFeed = true,
    bool IsCarouselItem = false,
    IReadOnlyList<string>? Children = null,
    string AltText = "",
    string LocationId = "",
    IReadOnlyList<string>? Collaborators = null,
    string AudioName = "");

/// <summary>
/// Kontent rejalashtirishning SOF (I/O'siz) qoidalari — baza ham, tarmoq ham chaqirilmaydi,
/// shuning uchun to'liq testlanadi (<c>InstagramPublishContractTests</c>).
///
/// <para>Bu yerda modulning eng qimmat qarorlari turadi: <b>postni Meta'ga umuman
/// yubormaslik</b> (validatsiya), <b>qachon qayta so'rash</b> (poll jadvali) va
/// <b>xato kodini odam o'qiydigan matnga aylantirish</b>.</para>
/// </summary>
public static class InstagramPublishContract
{
    /* ═════════════════════════ 1) Normalizatsiya ═════════════════════════ */

    /// <summary>Noma'lum/bo'sh post turi → <c>image</c> (yozuv YO'QOLMAYDI).</summary>
    public static string NormalizePostType(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        foreach (var t in IgPublishConst.PostTypes) if (t == s) return t;
        return IgPublishConst.TypeImage;
    }

    /// <summary>Noma'lum/bo'sh holat → <c>scheduled</c>.</summary>
    public static string NormalizeStatus(string? v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        foreach (var t in IgPublishConst.Statuses) if (t == s) return t;
        return IgPublishConst.StScheduled;
    }

    /// <summary>Meta'dan kelgan <c>status_code</c> ni kanonik shaklga keltiradi (noma'lum → <c>IN_PROGRESS</c>).
    /// <para>⚠️ Noma'lum kod ATAYIN <c>IN_PROGRESS</c> ga tushadi, <c>ERROR</c> ga emas: yangi/kutilmagan
    /// qiymat tufayli tayyor bo'layotgan post o'chirilib ketmasin — poll baribir 10 daqiqada to'xtaydi.</para></summary>
    public static string NormalizeContainerStatus(string? v)
    {
        var s = (v ?? "").Trim().ToUpperInvariant();
        return s switch
        {
            IgPublishConst.CsFinished => IgPublishConst.CsFinished,
            IgPublishConst.CsError => IgPublishConst.CsError,
            IgPublishConst.CsExpired => IgPublishConst.CsExpired,
            IgPublishConst.CsPublished => IgPublishConst.CsPublished,
            _ => IgPublishConst.CsInProgress,
        };
    }

    /// <summary>Konteyner chop etishga TAYYORmi.</summary>
    public static bool IsReadyToPublish(string? statusCode) =>
        NormalizeContainerStatus(statusCode) == IgPublishConst.CsFinished;

    /// <summary>Konteyner holati YAKUNIYmi (poll to'xtaydi).</summary>
    public static bool IsTerminal(string? statusCode) =>
        NormalizeContainerStatus(statusCode) is IgPublishConst.CsFinished
            or IgPublishConst.CsError or IgPublishConst.CsExpired or IgPublishConst.CsPublished;

    /// <summary>
    /// Bizning post turimiz → Meta'ning <c>media_type</c> qiymati.
    /// <para>⚠️ <c>video</c> ATAYIN <c>REELS</c> ga aylanadi: Meta 2022-yildan beri feed
    /// videosini baribir Reels sifatida joylaydi va <c>media_type=VIDEO</c> eskirgan yo'l.
    /// <c>image</c> uchun esa parametr UMUMAN yuborilmaydi (standart qiymat) — ortiqcha
    /// parametr Graph'da <c>code 100</c> berishi mumkin.</para>
    ///
    /// <para>⚠️ <b>"REELS" degani "9:16 shart" DEGANI EMAS.</b> Bu xarita faqat Meta'ning
    /// <c>media_type</c> qiymatini tanlaydi; nisbat qoidasi esa <see cref="ValidateVideo"/>
    /// da va u REELS uchun <b>0.01:1 – 10:1</b> (<see cref="IgPublishConst.VideoMinRatio"/>).
    /// Ilgari aynan shu ikkisi aralashtirilgan edi: "video ham REELS bo'ladi" degan mantiqdan
    /// "demak 9:16 bo'lishi shart" xulosasi chiqarilgan va kvadrat video bloklanardi.</para>
    /// </summary>
    public static string MediaTypeOf(string? postType) => NormalizePostType(postType) switch
    {
        IgPublishConst.TypeReels or IgPublishConst.TypeVideo => IgPublishConst.MtReels,
        IgPublishConst.TypeStory => IgPublishConst.MtStories,
        IgPublishConst.TypeCarousel => IgPublishConst.MtCarousel,
        _ => "",   // IMAGE — standart, yuborilmaydi
    };

    /* ═════════════════════════ 2) Caption ═════════════════════════ */

    /// <summary>
    /// Hashtag soni. Sanaladi: satr boshida yoki HARF/RAQAM BO'LMAGAN belgidan keyin turgan
    /// <c>#</c> va undan keyingi kamida bitta harf/raqam/pastki chiziq.
    /// <para>⚠️ <c>abc#def</c> hashtag EMAS (Instagram ham shunday o'qiydi), <c>##</c> ham emas.</para>
    /// </summary>
    public static int CountHashtags(string? text) => CountTags(text, '#');

    /// <summary>
    /// Mention (<c>@username</c>) soni.
    /// <para>⚠️ Elektron pochta mention deb sanalmaydi: <c>ali@mail.uz</c> dagi <c>@</c> dan
    /// oldin harf turibdi, ya'ni qoida uni o'tkazmaydi.</para>
    /// </summary>
    public static int CountMentions(string? text) => CountTags(text, '@');

    private static int CountTags(string? text, char marker)
    {
        var s = text ?? "";
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != marker) continue;
            // Oldingi belgi harf/raqam bo'lsa — bu teg emas (so'z ichidagi belgi).
            if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) continue;
            // Keyingi belgi harf/raqam/`_` bo'lishi SHART.
            if (i + 1 >= s.Length) continue;
            var n = s[i + 1];
            if (!char.IsLetterOrDigit(n) && n != '_') continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Caption validatsiyasi: uzunlik, hashtag va mention chegaralari.
    /// <para>Bo'sh caption — XATO EMAS (Instagram matnsiz postni qabul qiladi).</para>
    /// </summary>
    public static (bool Ok, string Error) ValidateCaption(string? caption)
    {
        var s = caption ?? "";
        if (s.Length > IgPublishConst.MaxCaptionLength)
            return (false, $"Matn juda uzun: {s.Length} belgi (ruxsat {IgPublishConst.MaxCaptionLength}).");

        var tags = CountHashtags(s);
        if (tags > IgPublishConst.MaxHashtags)
            return (false, $"Hashtag ko'p: {tags} ta (ruxsat {IgPublishConst.MaxHashtags}).");

        var mentions = CountMentions(s);
        if (mentions > IgPublishConst.MaxMentions)
            return (false, $"Mention (@) ko'p: {mentions} ta (ruxsat {IgPublishConst.MaxMentions}).");

        return (true, "");
    }

    /* ═════════════════════════ 3) Media (§5.5) ═════════════════════════ */

    /// <summary>URL kengaytmasi JPEG'mi (so'rov qismi va fragment hisobga olinmaydi).</summary>
    public static bool IsJpegUrl(string? url) => HasExtension(url, ".jpg", ".jpeg");

    /// <summary>URL kengaytmasi video'mi (MOV/MP4).</summary>
    public static bool IsVideoUrl(string? url) => HasExtension(url, ".mp4", ".mov");

    private static bool HasExtension(string? url, params string[] exts)
    {
        var s = (url ?? "").Trim();
        if (s.Length == 0) return false;
        // `?` va `#` dan keyingi qism — parametr, kengaytmaga kirmaydi.
        var cut = s.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) s = s[..cut];
        foreach (var e in exts)
            if (s.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Media manzili Meta uchun yaroqlimi: <b>ochiq HTTPS</b> bo'lishi SHART.
    /// <para>⚠️ Meta faylni O'ZI yuklab oladi — auth, IP cheklov va redirect ishlamaydi
    /// (§5.6). Bu yerda faqat sxema tekshiriladi; "haqiqatan ochiqmi" degan savolga
    /// tarmoqsiz javob berib bo'lmaydi va u <c>2207052</c> xatosi orqali qaytadi.</para>
    /// </summary>
    public static (bool Ok, string Error) ValidateMediaUrl(string? url)
    {
        var s = (url ?? "").Trim();
        if (s.Length == 0) return (false, "Media manzili bo'sh.");
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            return (false, "Media manzili noto'g'ri (to'liq URL bo'lishi kerak).");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return (false, "Media manzili HTTPS bo'lishi shart — Instagram faylni o'zi yuklab oladi.");
        return (true, "");
    }

    /// <summary>
    /// BITTA media elementini post turiga qarab tekshiradi.
    /// </summary>
    /// <param name="postType">image | video | reels | story | carousel.</param>
    /// <param name="item">Tekshiriladigan element.</param>
    /// <param name="checkRatio">Nisbat tekshirilsinmi. Karuselda FAQAT birinchi element uchun
    /// <c>true</c>: Instagram qolganlarini birinchisining nisbatiga QIRQADI, ya'ni ularni rad
    /// etish foydalanuvchini bekorga to'sib qo'yardi.</param>
    public static (bool Ok, string Error) ValidateMedia(string? postType, IgMediaItem? item, bool checkRatio = true)
    {
        if (item is null) return (false, "Media elementi yo'q.");

        var type = NormalizePostType(postType);
        var (urlOk, urlErr) = ValidateMediaUrl(item.Url);
        if (!urlOk) return (false, urlErr);

        var kind = (item.Kind ?? "").Trim().ToLowerInvariant() == IgPublishConst.KindVideo
            ? IgPublishConst.KindVideo : IgPublishConst.KindImage;

        // Reels — faqat video. (Qavslar ATAYIN: `is A or B && c` o'qishda chalkash.)
        if ((type is IgPublishConst.TypeReels or IgPublishConst.TypeVideo) && kind != IgPublishConst.KindVideo)
            return (false, "Reels uchun video kerak (MOV yoki MP4).");

        if (item.AltText.Length > IgPublishConst.MaxAltTextLength)
            return (false, $"Alt matn juda uzun: {item.AltText.Length} belgi (ruxsat {IgPublishConst.MaxAltTextLength}).");

        return kind == IgPublishConst.KindVideo
            ? ValidateVideo(type, item, checkRatio)
            : ValidateImage(type, item, checkRatio);
    }

    private static (bool Ok, string Error) ValidateImage(string type, IgMediaItem item, bool checkRatio)
    {
        // ⚠️ FAQAT JPEG. PNG/WebP/HEIC Meta tomonidan rad etiladi (`2207005`) — buni oldindan
        // aytish foydalanuvchiga 10 daqiqalik poll'dan keyingi xatodan ko'ra foydaliroq.
        if (!IsJpegUrl(item.Url))
            return (false, "Rasm faqat JPEG bo'lishi kerak (.jpg yoki .jpeg).");

        if (item.SizeBytes > IgPublishConst.MaxImageBytes)
            return (false, $"Rasm hajmi katta: {Mb(item.SizeBytes)} MB (ruxsat {Mb(IgPublishConst.MaxImageBytes)} MB).");

        if (item.Width <= 0 || item.Height <= 0) return (true, "");   // o'lcham noma'lum — tekshirilmaydi

        var ratio = (double)item.Width / item.Height;

        if (type == IgPublishConst.TypeStory)
        {
            if (checkRatio && !NearStoryRatio(ratio))
                return (false, "Story rasmi 9:16 nisbatda bo'lishi kerak (masalan 1080×1920).");
            return (true, "");
        }

        if (item.Width < IgPublishConst.FeedMinWidth || item.Width > IgPublishConst.FeedMaxWidth)
            return (false, $"Rasm kengligi {IgPublishConst.FeedMinWidth}–{IgPublishConst.FeedMaxWidth} px oralig'ida bo'lishi kerak (hozir {item.Width} px).");

        if (checkRatio && (ratio < IgPublishConst.FeedMinRatio || ratio > IgPublishConst.FeedMaxRatio))
            return (false, "Rasm nisbati 4:5 dan 1.91:1 gacha bo'lishi kerak.");

        return (true, "");
    }

    private static (bool Ok, string Error) ValidateVideo(string type, IgMediaItem item, bool checkRatio = true)
    {
        if (!IsVideoUrl(item.Url))
            return (false, "Video faqat MP4 yoki MOV bo'lishi kerak.");

        var isStory = type == IgPublishConst.TypeStory;
        var maxBytes = isStory ? IgPublishConst.MaxStoryVideoBytes : IgPublishConst.MaxReelsBytes;
        if (item.SizeBytes > maxBytes)
            return (false, $"Video hajmi katta: {Mb(item.SizeBytes)} MB (ruxsat {Mb(maxBytes)} MB).");

        if (item.DurationSeconds > 0)
        {
            var min = isStory ? IgPublishConst.MinStoryVideoSeconds : IgPublishConst.MinReelsSeconds;
            var max = isStory ? IgPublishConst.MaxStoryVideoSeconds : IgPublishConst.MaxReelsSeconds;
            if (item.DurationSeconds < min || item.DurationSeconds > max)
                return (false, $"Video davomiyligi {min:0}–{max:0} soniya oralig'ida bo'lishi kerak (hozir {item.DurationSeconds:0.#} s).");
        }

        if (item.CoverUrl.Length > 0)
        {
            var (coverOk, coverErr) = ValidateMediaUrl(item.CoverUrl);
            if (!coverOk) return (false, "Muqova: " + coverErr);
        }

        // ═══ NISBAT — STORY va REELS uchun qoida ATAYIN HAR XIL ═══
        //
        // ⚠️ STORY: 9:16 QATTIQ talab. Story butun ekranni egallaydi va boshqa nisbat
        //    yuqori/pastda katta bo'sh chekka bo'lib chiqadi — bu foydalanuvchi KUTMAGAN
        //    natija, ya'ni uni oldindan aytgan ma'qul.
        //
        // ⚠️ REELS / feed videosi: FAQAT Meta diapazoni (0.01:1 – 10:1). Ilgari bu yerda ham
        //    9:16 talab qilinardi va kvadrat (1:1) yoki 4:5 video Instagram'ga UMUMAN
        //    yuborilmasdi — bizning cheklovimiz haqiqiy postlarni bloklardi. 9:16 dan uzoq
        //    nisbat endi XATO emas, OGOHLANTIRISH (`MediaWarning`).
        if (item.Width > 0 && item.Height > 0 && checkRatio)
        {
            var ratio = (double)item.Width / item.Height;

            if (isStory)
            {
                if (!NearStoryRatio(ratio))
                    return (false, "Story videosi 9:16 nisbatda bo'lishi kerak (masalan 1080×1920).");
            }
            else if (ratio < IgPublishConst.VideoMinRatio || ratio > IgPublishConst.VideoMaxRatio)
            {
                return (false, $"Video nisbati {RatioText(ratio)} — Instagram {RatioText(IgPublishConst.VideoMinRatio)} dan {RatioText(IgPublishConst.VideoMaxRatio)} gacha qabul qiladi.");
            }
        }

        return (true, "");
    }

    private static bool NearStoryRatio(double ratio) =>
        Math.Abs(ratio - IgPublishConst.StoryRatio) <= IgPublishConst.StoryRatioTolerance;

    /// <summary>Nisbatni odam o'qiydigan ko'rinishga keltiradi: <c>0.5625</c> → <c>"0.56:1"</c>.</summary>
    private static string RatioText(double ratio) => $"{ratio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}:1";

    private static string Mb(long bytes) => (bytes / 1024d / 1024d).ToString("0.#");

    /* ═════════════════════ 3.5) OGOHLANTIRISHLAR — "xato emas, lekin bilib qo'ying" ═════════════════════ */

    /// <summary>
    /// Media bo'yicha OGOHLANTIRISH matni (bo'sh satr — ogohlantirish yo'q).
    ///
    /// <para><b>Nega alohida mexanizm kerak bo'ldi.</b> Validatsiya faqat ikki javob bera
    /// olardi: "o'tdi" yoki "post umuman yuborilmaydi". Nisbat esa uchinchi holatga tegishli —
    /// <b>Instagram qabul qiladi, lekin natija foydalanuvchi kutgandek chiqmasligi mumkin</b>
    /// (masalan 1:1 reels lentada qirqiladi yoki chekka bo'sh joy qo'shiladi). Ilgari bu holat
    /// XATO deb qaytarilar va butunlay ishlaydigan postni bloklardi.</para>
    ///
    /// <para>⚠️ Bu funksiya <b>hech qachon ish oqimini to'xtatmaydi</b>: uni UI (composer)
    /// yoki log ko'rsatadi, <see cref="ValidatePost"/> esa unga QARAMAYDI. Ya'ni yangi
    /// ogohlantirish qo'shish hech qachon postni bloklab qo'ymaydi — bu ATAYIN shunday
    /// ajratilgan.</para>
    ///
    /// <para>⚠️ O'lcham noma'lum (0) bo'lsa ogohlantirish ham YO'Q — "bilmasak, qo'rqitmaymiz"
    /// (<see cref="IgMediaItem"/> kelishuvi bilan bir xil).</para>
    /// </summary>
    public static string MediaWarning(string? postType, IgMediaItem? item)
    {
        if (item is null) return "";

        var type = NormalizePostType(postType);
        var kind = (item.Kind ?? "").Trim().ToLowerInvariant() == IgPublishConst.KindVideo
            ? IgPublishConst.KindVideo : IgPublishConst.KindImage;

        // Story'da nisbat QATTIQ tekshiriladi (ValidateMedia xato qaytaradi) — bu yerda
        // takrorlash foydalanuvchiga bir xil gapni ikki marta aytish bo'lardi.
        if (kind != IgPublishConst.KindVideo || type == IgPublishConst.TypeStory) return "";
        if (item.Width <= 0 || item.Height <= 0) return "";

        var ratio = (double)item.Width / item.Height;
        if (NearStoryRatio(ratio)) return "";

        return $"Video 9:16 emas ({item.Width}×{item.Height}) — Instagram uni qirqishi yoki "
             + "chetiga bo'sh joy qo'shishi mumkin. To'liq ekran uchun 1080×1920 tavsiya etiladi.";
    }

    /// <summary>
    /// BUTUN post bo'yicha ogohlantirishlar ro'yxati (bo'sh ro'yxat — hammasi joyida).
    /// <para>Karuselda faqat <b>BIRINCHI</b> element ko'riladi — <see cref="ValidatePost"/>
    /// dagi nisbat qoidasi bilan AYNAN bir xil (qolganlari birinchisiga qirqiladi, ya'ni
    /// ular haqida ogohlantirish chalg'itardi).</para>
    /// </summary>
    public static IReadOnlyList<string> PostWarnings(string? postType, IReadOnlyList<IgMediaItem>? media)
    {
        var items = media ?? Array.Empty<IgMediaItem>();
        if (items.Count == 0) return Array.Empty<string>();

        var type = NormalizePostType(postType);
        if (type == IgPublishConst.TypeCarousel)
        {
            var first = MediaWarning(type, items[0]);
            return first.Length == 0 ? Array.Empty<string>() : new[] { first };
        }

        var list = new List<string>();
        foreach (var it in items)
        {
            var w = MediaWarning(type, it);
            if (w.Length > 0) list.Add(w);
        }
        return list;
    }

    /// <summary>
    /// BUTUN postni tekshiradi (caption + media to'plami) — worker konteyner yaratishdan
    /// OLDIN aynan shuni chaqiradi.
    /// <para>Qaytadi <c>(false, sabab)</c> bo'lsa post Meta'ga umuman yuborilmaydi va sabab
    /// <c>IgScheduledPost.Error</c> ga yoziladi.</para>
    /// </summary>
    public static (bool Ok, string Error) ValidatePost(string? postType, string? caption, IReadOnlyList<IgMediaItem>? media)
    {
        var type = NormalizePostType(postType);

        var (capOk, capErr) = ValidateCaption(caption);
        if (!capOk) return (false, capErr);

        var items = media ?? Array.Empty<IgMediaItem>();
        if (items.Count == 0) return (false, "Postda media yo'q — kamida bitta rasm yoki video kerak.");

        if (type == IgPublishConst.TypeCarousel)
        {
            if (items.Count < IgPublishConst.MinCarouselItems || items.Count > IgPublishConst.MaxCarouselItems)
                return (false, $"Karuselda {IgPublishConst.MinCarouselItems}–{IgPublishConst.MaxCarouselItems} ta element bo'lishi kerak (hozir {items.Count}).");

            for (var i = 0; i < items.Count; i++)
            {
                // ⚠️ Karusel BOLASIDA caption ISHLAMAYDI — Meta uni jimgina e'tiborsiz qoldiradi,
                // ya'ni foydalanuvchi yozgan matn hech qayerda ko'rinmasdi. Shuning uchun XATO.
                if (!string.IsNullOrWhiteSpace(items[i].Caption))
                    return (false, $"{i + 1}-elementga matn yozilgan: karusel elementlarida matn ishlamaydi, uni umumiy matn maydoniga yozing.");

                // Nisbat faqat BIRINCHI element bo'yicha (qolganlari shunga qirqiladi).
                var (ok, err) = ValidateMedia(type, items[i], checkRatio: i == 0);
                if (!ok) return (false, $"{i + 1}-element: {err}");
            }
            return (true, "");
        }

        if (items.Count > 1)
            return (false, "Bu tur uchun bitta media bo'lishi kerak — bir nechta rasm uchun «Karusel» turini tanlang.");

        return ValidateMedia(type, items[0]);
    }

    /// <summary>Collaborator ro'yxati chegarasi (≤3, va ular Instagram'da qabul qilishi kerak).</summary>
    public static (bool Ok, string Error) ValidateCollaborators(IReadOnlyList<string>? names)
    {
        var n = names?.Count ?? 0;
        return n > IgPublishConst.MaxCollaborators
            ? (false, $"Hammuallif ko'pi bilan {IgPublishConst.MaxCollaborators} ta bo'lishi mumkin (hozir {n}).")
            : (true, "");
    }

    /* ═════════════════════════ 4) Konteyner so'rovi (sof quruvchi) ═════════════════════════ */

    /// <summary>
    /// Post turi + media + sozlamalardan <c>POST /media</c> parametrlarini quradi.
    /// <para>Karusel uchun IKKI bosqich bor: avval har bola <paramref name="asCarouselChild"/>
    /// bilan quriladi (<c>is_carousel_item=true</c>, caption YO'Q), keyin
    /// <see cref="BuildCarouselParent"/> ota-onani quradi.</para>
    /// </summary>
    public static IgContainerRequest BuildContainerRequest(
        string? postType, IgMediaItem item, string? caption = "",
        IgPublishOptions? options = null, bool asCarouselChild = false)
    {
        var type = NormalizePostType(postType);
        var opt = options ?? new IgPublishOptions();
        var isVideo = (item.Kind ?? "").Trim().ToLowerInvariant() == IgPublishConst.KindVideo;

        // Karusel bolasi: media_type FAQAT video bo'lsa yuboriladi, caption esa HECH QACHON.
        if (asCarouselChild)
            return new IgContainerRequest(
                MediaType: isVideo ? IgPublishConst.MtVideo : "",
                ImageUrl: isVideo ? "" : item.Url,
                VideoUrl: isVideo ? item.Url : "",
                IsCarouselItem: true,
                AltText: item.AltText);

        var mt = MediaTypeOf(type);
        var isReels = mt == IgPublishConst.MtReels;

        return new IgContainerRequest(
            MediaType: mt,
            ImageUrl: isVideo ? "" : item.Url,
            VideoUrl: isVideo ? item.Url : "",
            Caption: type == IgPublishConst.TypeStory ? "" : (caption ?? ""),   // story'da caption yo'q
            CoverUrl: isReels ? item.CoverUrl : "",
            ThumbOffsetMs: isReels ? item.ThumbOffsetMs : -1,
            ShareToFeed: isReels && opt.ShareToFeed,
            AltText: type == IgPublishConst.TypeImage ? item.AltText : "",       // alt_text — faqat yakka rasm
            LocationId: type == IgPublishConst.TypeStory ? "" : opt.LocationId,
            Collaborators: type == IgPublishConst.TypeStory ? null : opt.Collaborators,
            AudioName: isReels ? opt.AudioName : "");
    }

    /// <summary>
    /// Karusel OTA-ONASI: <c>media_type=CAROUSEL</c> + bolalar id'lari + caption.
    ///
    /// <para>🔴 <b><c>location_id</c> KARUSELDA YUBORILMAYDI</b> — bu ATAYIN qabul qilingan
    /// qaror, "unutilgan maydon" emas.</para>
    ///
    /// <para><b>Nega.</b> Meta hujjatlari bu yerda ZID: qo'llanma matnida <c>location_id</c>
    /// umumiy parametr sifatida sanaladi, endpoint reference'ning parametrlar jadvalida esa u
    /// <c>IMAGE</c> ✓ va <c>REELS</c> ✓ uchun belgilangan, <c>CAROUSEL</c> uchun ✗. Graph
    /// qo'llab-quvvatlamaydigan parametrni <b>jimgina e'tiborsiz qoldirmaydi</b> — u butun
    /// so'rovni <c>code 100</c> bilan rad etadi. Ya'ni ikki ehtimol teng emas:</para>
    /// <list type="bullet">
    ///   <item>yubormasak va Meta aslida qo'llab-quvvatlasa — joylashuv belgisi yo'q post
    ///   (kichik yo'qotish, foydalanuvchi kutgan narsaning bir qismi);</item>
    ///   <item>yuborsak va Meta qo'llab-quvvatlamasa — <b>joylashuv tanlangan HAR BIR karusel
    ///   yiqiladi</b>, sabab esa "code 100" degan tushunarsiz matn bo'lib ko'rinadi.</item>
    /// </list>
    /// <para>Shuning uchun xavfsiz tomon tanlandi. Bolalarda ham yuborilmaydi
    /// (<see cref="BuildContainerRequest"/> dagi <c>asCarouselChild</c> tarmog'i) — karusel
    /// bolasi faqat media konteyneri.</para>
    ///
    /// <para><b>QANDAY TEKSHIRISH KERAK</b> (qaror ochiq qoldirilgan): sinov akkauntida
    /// bitta karusel konteyner qurib, unga <c>location_id</c> qo'shib
    /// <c>POST /{ig-user-id}/media</c> chaqiriladi. <c>code 100</c> qaytsa — hozirgi xulq
    /// to'g'ri va bu izoh tasdiqlangan deb belgilanadi; muvaffaqiyatli o'tsa va joylashuv
    /// postda ko'rinsa — quyidagi qatorni <c>LocationId: opt.LocationId</c> ga qaytarish
    /// yetarli (<paramref name="options"/> shu maqsadda saqlab qolingan).</para>
    ///
    /// <para>⚠️ <c>collaborators</c> esa YUBORILADI — u reference'da karusel uchun ham
    /// belgilangan va bu yerda hech qanday ziddiyat yo'q.</para>
    /// </summary>
    public static IgContainerRequest BuildCarouselParent(
        IReadOnlyList<string> childIds, string? caption = "", IgPublishOptions? options = null)
    {
        var opt = options ?? new IgPublishOptions();
        return new IgContainerRequest(
            MediaType: IgPublishConst.MtCarousel,
            Caption: caption ?? "",
            Children: childIds,
            LocationId: "",                       // ⚠️ ATAYIN bo'sh — sabab yuqoridagi izohda
            Collaborators: opt.Collaborators);
    }

    /* ═════════════════════════ 5) Poll jadvali va muddat ═════════════════════════ */

    /// <summary>
    /// Konteyner holatini keyingi so'rashgacha kutiladigan soniya.
    /// <para>Jadval: 1-urinish → 30 s, 2 → 60 s, 3 → 120 s, 4 va undan keyin → 300 s.
    /// Meta tavsiyasi "daqiqada bir marta, 5 daqiqadan ko'p emas" — shu sababli oxirgi
    /// qadam 300 s da TO'XTAYDI, cheksiz o'smaydi.</para>
    /// <para>⚠️ Nol/manfiy urinish raqami ham birinchi qadamni beradi — chaqiruvchidagi
    /// hisob xatosi kutish vaqtini buzib yubormasin.</para>
    /// </summary>
    public static int NextPollDelaySeconds(int attempt)
    {
        var d = IgPublishConst.PollDelaysSeconds;
        if (attempt <= 1) return d[0];
        return attempt - 1 < d.Length ? d[attempt - 1] : d[^1];
    }

    /// <summary>Poll boshlanganidan 10 daqiqa o'tdimi (o'tgan bo'lsa post <c>failed</c>).
    /// <para>⚠️ Sana o'qilmasa <c>true</c> — "bilmasak, cheksiz kutmaymiz". Poll'ni abadiy
    /// aylantirgandan ko'ra postni xatoga chiqarib, operatorga ko'rsatgan yaxshi.</para></summary>
    public static bool IsPollExpired(string? startedAtIso, DateTime now)
    {
        if (!InstagramContract.TryIso(startedAtIso, out var started)) return true;
        var elapsed = now - started;
        if (elapsed < TimeSpan.Zero) return false;   // kelajakdagi sana — hali boshlanmagan
        return elapsed.TotalSeconds >= IgPublishConst.PollTimeoutSeconds;
    }

    /// <summary>
    /// Konteyner o'ldimi (yaratilgandan 24 soat o'tdimi).
    /// <para>⚠️ Sana o'qilmasa <c>true</c> (o'lgan deb qaraladi). Bu XAVFSIZ tomon: konteyner
    /// yaratish arzon va kvota faqat <c>media_publish</c> bosqichida sanaladi, ya'ni ortiqcha
    /// konteyner hech narsa yo'qotmaydi. Teskarisi esa <c>2207020</c> bilan tugaydigan
    /// befoyda publish urinishi bo'lardi.</para>
    /// </summary>
    public static bool IsContainerExpired(string? createdAtIso, DateTime now)
    {
        if (!InstagramContract.TryIso(createdAtIso, out var created)) return true;
        var age = now - created;
        if (age < TimeSpan.Zero) return false;   // kelajakdagi sana (soat farqi) — tirik deb qaraladi
        return age.TotalHours >= IgPublishConst.ContainerLifetimeHours;
    }

    /// <summary>Xuddi shu tekshiruv, "hozir" ham ISO satr sifatida.
    /// <para><paramref name="nowIso"/> o'qilmasa <see cref="AppClock.Now"/> ishlatiladi —
    /// <c>DateTime.Now</c> loyihada TAQIQLANGAN.</para></summary>
    public static bool IsContainerExpired(string? createdAtIso, string? nowIso)
    {
        var now = InstagramContract.TryIso(nowIso, out var parsed) ? parsed : AppClock.Now;
        return IsContainerExpired(createdAtIso, now);
    }

    /// <summary>Post chop etish vaqti keldimi (<c>ScheduledAt &lt;= now</c>).
    /// <para>⚠️ Sana o'qilmasa <c>false</c> — buzuq yozuv butun navbatni band qilmasin.</para></summary>
    public static bool IsDue(string? scheduledAtIso, DateTime now) =>
        InstagramContract.TryIso(scheduledAtIso, out var at) && at <= now;

    /* ═════════════════════════ 6) Kvota ═════════════════════════ */

    /// <summary>
    /// Kunlik chop etish limiti to'ldimi.
    /// <para>⚠️ <paramref name="quotaTotal"/> 0 yoki manfiy bo'lsa (Meta javob bermadi yoki
    /// maydon yo'q) <c>false</c> qaytadi: <b>limitni taxmin qilib post to'xtatilmaydi</b>.
    /// Meta hujjatlari 100 va 50 deb zid yozadi, shuning uchun bu yerda hech qanday standart
    /// qiymat YO'Q — noma'lum bo'lsa urinib ko'ramiz va Meta o'zi <c>2207042</c> qaytaradi.</para>
    /// </summary>
    public static bool QuotaExceeded(int quotaUsage, int quotaTotal) =>
        quotaTotal > 0 && quotaUsage >= quotaTotal;

    /// <summary>Kvota holatini foydalanuvchiga ko'rsatiladigan matn ("2 / 50" yoki "2 / noma'lum").</summary>
    public static string QuotaText(int quotaUsage, int quotaTotal) =>
        quotaTotal > 0 ? $"{quotaUsage} / {quotaTotal}" : $"{quotaUsage} / noma'lum";

    /* ═════════════════════════ 7) Xato kodlari (§5.8) ═════════════════════════ */

    /// <summary>
    /// Instagram publishing xato kodini O'ZBEKCHA matnga aylantiradi.
    ///
    /// <para>⚠️ Xarita "yopiq" EMAS: rasmiy kodlar sahifasi mavjud emas va Meta yangi kod
    /// qo'shishi mumkin. Noma'lum kod <b>jimgina yutilmaydi</b> — kod raqami bilan umumiy
    /// matn qaytadi, ya'ni operator qidiruvga soladigan narsa qoladi.</para>
    /// </summary>
    /// <param name="code">Meta kodi (<c>error_subcode</c> yoki konteyner <c>status</c> matnidan).</param>
    /// <param name="metaMessage">Meta bergan inglizcha matn (bo'lsa, noma'lum kodga qo'shiladi).</param>
    public static string ErrorText(int code, string? metaMessage = "") => code switch
    {
        IgPublishConst.ErrMediaDownload =>
            "Instagram media faylni yuklab ololmadi — manzil ochiq HTTPS bo'lishi va tez javob berishi kerak.",
        IgPublishConst.ErrContainerExpired =>
            "Tayyorlangan post muddati o'tdi (24 soat) — qaytadan urinib ko'ring.",
        IgPublishConst.ErrDownloadTimeout =>
            "Media faylni yuklab olish vaqti tugadi — fayl juda katta yoki server sekin javob berdi.",
        IgPublishConst.ErrNotJpeg =>
            "Rasm JPEG emas — faqat .jpg/.jpeg qabul qilinadi.",
        IgPublishConst.ErrBadRatio =>
            "Rasm/video nisbati noto'g'ri (feed rasmi 4:5–1.91:1, story 9:16, video 0.01:1–10:1).",
        IgPublishConst.ErrCaptionTooLong =>
            $"Post matni juda uzun (ruxsat {IgPublishConst.MaxCaptionLength} belgi).",
        IgPublishConst.ErrVideoCodec =>
            "Video kodeki qo'llab-quvvatlanmaydi — MP4 (H.264) va AAC audio bilan qayta saqlang.",
        IgPublishConst.ErrDailyLimit =>
            "Instagram kunlik chop etish limiti to'ldi — post keyingi sutkada joylanadi.",
        IgPublishConst.ErrSpam =>
            "Instagram bu postni spam deb belgiladi — matn va hashtag'larni o'zgartirib ko'ring.",
        _ => string.IsNullOrWhiteSpace(metaMessage)
            ? (code != 0
                ? $"Instagram postni qabul qilmadi (kod {code})."
                : "Instagram postni qabul qilmadi (noma'lum xato).")
            : $"Instagram postni qabul qilmadi (kod {code}): {metaMessage}",
    };

    /// <summary>
    /// Matndan Instagram publishing xato kodini ajratib oladi (topilmasa 0).
    ///
    /// <para>⚠️ Kerak, chunki konteyner xatosi HTTP xatosi sifatida emas, <c>status</c>
    /// MATNI ichida keladi: <c>"Error: 2207020 - The media container has expired"</c>.
    /// Faqat <c>2207xxx</c> shaklidagi 7 xonali son olinadi — aks holda matndagi tasodifiy
    /// son (o'lcham, vaqt) xato kodi deb o'qilardi.</para>
    /// </summary>
    public static int ExtractErrorCode(string? text)
    {
        var s = text ?? "";
        for (var i = 0; i + 7 <= s.Length; i++)
        {
            if (s[i] != '2') continue;
            // Oldin/keyin raqam turgan bo'lsa — bu uzunroq sonning bo'lagi, kod emas.
            if (i > 0 && char.IsDigit(s[i - 1])) continue;
            if (i + 7 < s.Length && char.IsDigit(s[i + 7])) continue;

            var chunk = s.AsSpan(i, 7);
            var allDigits = true;
            foreach (var c in chunk) if (!char.IsDigit(c)) { allDigits = false; break; }
            if (!allDigits) continue;
            if (!chunk.StartsWith("2207")) continue;

            return int.Parse(chunk);
        }
        return 0;
    }

    /// <summary>Konteyner <c>ERROR</c>/<c>EXPIRED</c> holatidagi <c>status</c> matnini
    /// o'zbekcha sababga aylantiradi (kod topilmasa ham umumiy matn qaytadi).</summary>
    public static string ContainerErrorText(string? statusCode, string? statusText)
    {
        var code = ExtractErrorCode(statusText);
        if (code != 0) return ErrorText(code);
        return NormalizeContainerStatus(statusCode) == IgPublishConst.CsExpired
            ? ErrorText(IgPublishConst.ErrContainerExpired)
            : ErrorText(0, statusText);
    }
}
