using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → INSTAGRAM KONTENT REJALASHTIRISH (E2) — admin API'si.
///
/// <para>Bu <see cref="InstagramController"/> ning DAVOMI (<c>partial</c>): marshrut prefiksi
/// (<c>api/admin/instagram</c>), sinf darajasidagi <c>[AdminPerm("marketing",
/// ReadRequiresPerm = true)]</c> va <see cref="AuditEntity"/> asosiy fayldan MEROS bo'ladi.
/// Yozish amallari esa YANGI sahifa kaliti bilan — <c>marketing.content</c>.</para>
///
/// <para><b>🔴 REJALASHTIRISH BIZNIKI, META'NIKI EMAS</b> (§5.2): <c>scheduled_publish_time</c>
/// parametri Instagram'da mavjud emas va media konteyneri 24 soatda o'ladi. Shuning uchun
/// vaqt <c>IgScheduledPost.ScheduledAt</c> da turadi, konteyner esa faqat chop etish payti
/// yaratiladi. Butun oqim <see cref="InstagramPublishService"/> da — controller faqat HTTP
/// tarjimasi va CRUD.</para>
///
/// <para><b>⚠️ JOYLANGAN POSTNI API ORQALI O'ZGARTIRIB HAM, O'CHIRIB HAM BO'LMAYDI</b>
/// (§5.9.1). Shuning uchun <c>PUT</c> faqat <c>scheduled</c> holatida ishlaydi, <c>DELETE</c>
/// esa joylangan postda faqat CRM yozuvini o'chiradi va buni javobda OCHIQ aytadi.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Kontent ro'yxati bir sahifada shuncha post qaytaradi.</summary>
    private const int PostPageSize = 50;

    /// <summary>Yozish amallari uchun sahifa kaliti (yangi).</summary>
    private const string ContentPerm = "marketing.content";

    // =============================================================================================
    //  RO'YXAT
    // =============================================================================================

    /// <summary>
    /// Rejalashtirilgan postlar ro'yxati + jamlanma.
    ///
    /// <para>⚠️ Jamlanma <b>BUTUN topilma</b> bo'yicha hisoblanadi, ko'rinadigan sahifadan emas
    /// — aks holda kalendar ostidagi son ro'yxatdagidan kichik chiqib, "raqamlar to'g'ri
    /// kelmayapti" bo'lardi (<c>books.md</c> dagi bir xil saboq).</para>
    /// </summary>
    /// <param name="from">Boshlanish kuni (<c>ScheduledAt</c> bo'yicha).</param>
    /// <param name="to">Tugash KUNI — kunning oxirigacha cho'ziladi (<c>audit.md</c> §5).</param>
    /// <param name="status">`scheduled` | `processing` | `published` | `failed` | `cancelled`;
    /// bo'sh yoki `all` — hammasi.</param>
    [HttpGet("content/posts")]
    public async Task<ActionResult<IgPostListDto>> ContentPosts(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var query = db.IgScheduledPosts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(from)) query = query.Where(p => p.ScheduledAt.CompareTo(from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) query = query.Where(p => p.ScheduledAt.CompareTo(to + "T23:59:59") <= 0);

        // ⚠️ Noma'lum holat JIM tashlanmaydi — u umuman qo'llanmaydi (ro'yxat bo'shab qolmasin),
        // ya'ni klientdagi xato kalit "hech narsa topilmadi" ga aylanmaydi.
        var wanted = (status ?? "").Trim().ToLowerInvariant();
        if (wanted.Length > 0 && wanted != "all" && IgPublishConst.Statuses.Contains(wanted))
            query = query.Where(p => p.Status == wanted);

        var totals = new IgPostTotalsDto(
            Total: await query.CountAsync(ct),
            Scheduled: await query.CountAsync(p => p.Status == IgPublishConst.StScheduled, ct),
            Processing: await query.CountAsync(p => p.Status == IgPublishConst.StProcessing, ct),
            Published: await query.CountAsync(p => p.Status == IgPublishConst.StPublished, ct),
            Failed: await query.CountAsync(p => p.Status == IgPublishConst.StFailed, ct),
            Cancelled: await query.CountAsync(p => p.Status == IgPublishConst.StCancelled, ct));

        var pageNo = page < 1 ? 1 : page;
        var rows = await query
            .OrderByDescending(p => p.ScheduledAt)
            .Skip((pageNo - 1) * PostPageSize)
            .Take(PostPageSize)
            .ToListAsync(ct);

        return new IgPostListDto(
            rows.Select(ToPostDto).ToList(), totals.Total, pageNo, PostPageSize, totals);
    }

    /// <summary>Bitta post (tahrirlash oynasi uchun).</summary>
    [HttpGet("content/posts/{id}")]
    public async Task<ActionResult<IgPostDto>> ContentPost(string id, CancellationToken ct)
    {
        var post = await db.IgScheduledPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return post is null ? NotFound(new { message = "Post topilmadi." }) : ToPostDto(post);
    }

    // =============================================================================================
    //  YARATISH / TAHRIRLASH / BEKOR QILISH
    // =============================================================================================

    /// <summary>
    /// Yangi reja.
    ///
    /// <para>⚠️ <b>Saqlashdan OLDIN <see cref="InstagramPublishContract.ValidatePost"/></b>:
    /// media JPEG emasligi, nisbat, hajm, caption uzunligi va karusel qoidalari shu yerda
    /// aniqlanadi. Aks holda xato faqat rejalashtirilgan vaqt kelganda, 10 daqiqalik poll'dan
    /// SO'NG ko'rinardi — ya'ni post o'z vaqtida chiqmasdi va nima uchunligi kech ma'lum
    /// bo'lardi.</para>
    /// </summary>
    [HttpPost("content/posts")]
    [AdminPerm(ContentPerm)]
    public async Task<ActionResult<IgPostDto>> CreateContentPost(IgPostPayload payload, CancellationToken ct)
    {
        var (ok, error, type, caption, mediaJson, optionsJson, scheduledAt) = ReadPostPayload(payload);
        if (!ok) return BadRequest(new { message = error });

        var post = new IgScheduledPost
        {
            PostType = type,
            Caption = caption,
            MediaJson = mediaJson,
            OptionsJson = optionsJson,
            ScheduledAt = scheduledAt,
            Status = IgPublishConst.StScheduled,
            CreatedBy = Actor,
            CreatedAt = AppClock.Iso(),
        };
        db.IgScheduledPosts.Add(post);

        audit.Record(AuditEntity, post.Id, "create",
            $"Instagram posti rejalashtirildi ({PostTypeLabel(type)}, {scheduledAt})"
            + (caption.Length > 0 ? $": «{Preview(caption)}»" : ""));
        await db.SaveChangesAsync(ct);

        return ToPostDto(post);
    }

    /// <summary>
    /// Tahrirlash.
    ///
    /// <para>⚠️ FAQAT <c>scheduled</c> holatida. <c>processing</c> — konteyner allaqachon
    /// Meta'da yaratilgan va matnni o'zgartirish unga TA'SIR QILMASDI; <c>published</c> —
    /// joylangan postni API orqali tahrirlab bo'lmaydi (§5.9.1). Ikkalasida ham foydalanuvchi
    /// "o'zgartirdim" deb o'ylab, aslida hech narsa o'zgarmagan bo'lardi.</para>
    /// </summary>
    [HttpPut("content/posts/{id}")]
    [AdminPerm(ContentPerm)]
    public async Task<ActionResult<IgPostDto>> UpdateContentPost(
        string id, IgPostPayload payload, CancellationToken ct)
    {
        var post = await db.IgScheduledPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound(new { message = "Post topilmadi." });

        if (post.Status != IgPublishConst.StScheduled)
            return BadRequest(new { message = EditBlockedText(post.Status) });

        var (ok, error, type, caption, mediaJson, optionsJson, scheduledAt) = ReadPostPayload(payload);
        if (!ok) return BadRequest(new { message = error });

        // Almashtirilgan media diskda yetim qolmasin — ESKI nomlar SAQLASHDAN OLDIN olinadi
        // (keyin `post.MediaJson` yangisi bilan almashadi va eskisini bilib bo'lmaydi).
        var oldNames = MarketingMediaCleanup.NamesOf(post.MediaJson, post.OptionsJson);

        post.PostType = type;
        post.Caption = caption;
        post.MediaJson = mediaJson;
        post.OptionsJson = optionsJson;
        post.ScheduledAt = scheduledAt;
        // Tahrirdan keyin urinishlar hisobi NOLDAN: odam sababni tuzatgan bo'lishi mumkin.
        post.Attempts = 0;
        post.Error = "";

        audit.Record(AuditEntity, post.Id, "update",
            $"Instagram posti tahrirlandi ({PostTypeLabel(type)}, {scheduledAt})");
        await db.SaveChangesAsync(ct);

        // Postdan CHIQIB KETGAN fayllar (eskisida bor, yangisida yo'q) — darhol o'chiriladi.
        // Tekshiruv bazaning SAQLANGANDAN KEYINGI holati bo'yicha, ya'ni fayl boshqa postda
        // ham ishlatilayotgan bo'lsa qoladi (batafsil: `MarketingMediaCleanup`).
        await CleanupContentMedia().RemoveUnusedAsync(
            oldNames.Except(MarketingMediaCleanup.NamesOf(mediaJson, optionsJson)), ct);

        return ToPostDto(post);
    }

    /// <summary>
    /// Bekor qilish yoki yozuvni o'chirish.
    ///
    /// <para>⚠️ <b>Ikki xil ma'no</b>, va foydalanuvchi ularni ADASHTIRMASLIGI kerak:</para>
    /// <list type="bullet">
    ///   <item><c>scheduled</c> — post hali joylanmagan, u BEKOR qilinadi
    ///     (<c>cancelled</c>) va yozuv tarixda qoladi.</item>
    ///   <item><c>published</c> — <b>Instagram'dagi post QOLADI</b>: uni API orqali o'chirib
    ///     bo'lmaydi (§5.9.1). O'chirilayotgani faqat CRM yozuvi va javobda shu OCHIQ
    ///     yoziladi — aks holda admin "o'chirdim" deb o'ylab, post profilda turaverardi.</item>
    ///   <item><c>processing</c> — rad etiladi: konteyner Meta'da, natija noaniq.</item>
    /// </list>
    /// </summary>
    [HttpDelete("content/posts/{id}")]
    [AdminPerm(ContentPerm)]
    public async Task<ActionResult<IgPostDeleteDto>> DeleteContentPost(string id, CancellationToken ct)
    {
        var post = await db.IgScheduledPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound(new { message = "Post topilmadi." });

        if (post.Status == IgPublishConst.StProcessing)
            return BadRequest(new
            {
                message = "Post hozir joylanmoqda — natijasi ma'lum bo'lguncha o'chirib bo'lmaydi. "
                          + "Bir necha daqiqadan keyin qayta urinib ko'ring.",
            });

        if (post.Status == IgPublishConst.StScheduled)
        {
            // ⚠️ BEKOR QILISHDA FAYL O'CHIRILMAYDI: yozuv (media'si bilan birga) CRM'da qoladi
            // va admin uni ochib ko'radi — fayl darhol o'chirilsa ekranda sinuq rasm chiqardi.
            // Bekor qilingan post keyin butunlay o'chirilganda fayl ham o'sha yerda o'chadi.
            post.Status = IgPublishConst.StCancelled;
            post.Error = "";
            audit.Record(AuditEntity, post.Id, "update",
                $"Instagram posti bekor qilindi ({PostTypeLabel(post.PostType)}, {post.ScheduledAt})");
            await db.SaveChangesAsync(ct);
            return new IgPostDeleteDto(Cancelled: true, Removed: false,
                Message: "Post bekor qilindi — Instagram'ga joylanmaydi.");
        }

        var wasPublished = post.Status == IgPublishConst.StPublished;
        // Yozuv butunlay o'chib ketyapti — uning fayllari boshqa hech qayerda ko'rinmaydi.
        var mediaNames = MarketingMediaCleanup.NamesOf(post.MediaJson, post.OptionsJson);
        db.IgScheduledPosts.Remove(post);
        audit.Record(AuditEntity, post.Id, "delete",
            wasPublished
                ? $"Instagram posti CRM yozuvi o'chirildi ({PostTypeLabel(post.PostType)}) — "
                  + "post Instagram'da QOLADI"
                : $"Instagram posti o'chirildi ({PostTypeLabel(post.PostType)}, {post.ScheduledAt})");
        await db.SaveChangesAsync(ct);

        // Yetim qolmasin: post yozuvi bilan birga uning OCHIQ media fayllari ham ketadi.
        // Fayl boshqa postda ham ishlatilayotgan bo'lsa QOLADI, va o'chirilmasa ham bu amal
        // muvaffaqiyatli hisoblanadi (`MarketingMediaCleanup.RemoveUnusedAsync` jim ishlaydi).
        await CleanupContentMedia().RemoveUnusedAsync(mediaNames, ct);

        return new IgPostDeleteDto(Cancelled: false, Removed: true,
            Message: wasPublished
                ? "CRM yozuvi o'chirildi. ⚠️ Instagram'dagi postning O'ZI qoladi — uni faqat "
                  + "Instagram ilovasidan o'chirish mumkin."
                : "Post o'chirildi.");
    }

    // =============================================================================================
    //  HOZIROQ JOYLASH
    // =============================================================================================

    /// <summary>
    /// «Hoziroq joylash» / «Qayta urinish».
    ///
    /// <para>⚠️ So'rov joylanishni KUTMAYDI: konteyner tayyorlanishi daqiqalar davom etadi.
    /// Rasm odatda shu yerdayoq joylanadi, video/reels esa <c>processing</c> bo'lib qoladi va
    /// worker uni oxiriga yetkazadi — javobdagi post holati shuni ko'rsatadi.</para>
    /// </summary>
    [HttpPost("content/posts/{id}/publish")]
    [AdminPerm(ContentPerm)]
    public async Task<ActionResult<IgPostDto>> PublishContentPost(
        string id, [FromServices] InstagramPublishService svc, CancellationToken ct)
    {
        var exists = await db.IgScheduledPosts.AsNoTracking().AnyAsync(p => p.Id == id, ct);
        if (!exists) return NotFound(new { message = "Post topilmadi." });

        var (ok, error) = await svc.PublishNowAsync(id, ct);

        // ⚠️ Audit HAR DOIM yoziladi — muvaffaqiyatda ham, xatoda ham: "kim qo'lda joylashga
        // urindi" savoli aynan nosozlikdan keyin beriladi (`audit.md` §3.5).
        var post = await db.IgScheduledPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        audit.Record(AuditEntity, id, "update",
            ok
                ? $"Instagram posti qo'lda joylashga yuborildi ({PostTypeLabel(post?.PostType)}) — "
                  + $"holat: {StatusLabel(post?.Status)}"
                : $"Instagram postini qo'lda joylash muvaffaqiyatsiz: {error}");
        await db.SaveChangesAsync(ct);

        if (!ok) return BadRequest(new { message = error });
        return post is null ? NotFound(new { message = "Post topilmadi." }) : ToPostDto(post);
    }

    // =============================================================================================
    //  LIMIT VA HOLAT
    // =============================================================================================

    /// <summary>
    /// Kunlik chop etish limiti (§5.4).
    ///
    /// <para>⚠️ <c>total = 0</c> bo'lsa <c>unknown = true</c> qaytadi va UI "noma'lum" deb
    /// yozadi. <b>Taxminiy 50/100 KO'RSATILMAYDI</b>: Meta hujjatlari qo'llanmada 100, reference
    /// namunasida 50 deb ZID yozadi — noto'g'ri raqam esa foydalanuvchini "yana 40 ta post
    /// joylasam bo'ladi" deb chalg'itardi.</para>
    /// </summary>
    [HttpGet("content/limit")]
    public async Task<ActionResult<IgPostLimitDto>> ContentLimit(
        [FromServices] InstagramPublishService svc, CancellationToken ct)
    {
        var (ok, usage, total, error) = await svc.GetLimitAsync(ct);
        if (!ok)
            return new IgPostLimitDto(0, IgPublishConst.UnknownQuota, Unknown: true, Text: "", Error: error);

        return new IgPostLimitDto(
            Usage: usage,
            Total: total,
            Unknown: total <= 0,
            Text: InstagramPublishContract.QuotaText(usage, total),
            Error: "");
    }

    /// <summary>
    /// Kontent bo'limi DIAGNOSTIKASI — "nega post chiqmayapti" savolining sabablari.
    ///
    /// <para>⚠️ <c>ScopeGranted</c> ATAYIN <c>null</c> ("noma'lum") bo'lishi mumkin: berilgan
    /// OAuth ruxsatlari ro'yxati saqlanmaydi, ya'ni <c>instagram_business_content_publish</c>
    /// olinganini ishonch bilan ayta olmaymiz. Yolg'on "ha" dan ko'ra ochiq "noma'lum" yaxshi —
    /// UI shu holatda «Qayta ulash» maslahatini ko'rsatadi (scope qo'shilishi qayta OAuth
    /// talab qiladi, §5).</para>
    /// </summary>
    [HttpGet("content/status")]
    public async Task<ActionResult<IgPostContentStatusDto>> ContentStatus(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var acc = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        var weekAgo = AppClock.Now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss");

        return new IgPostContentStatusDto(
            Enabled: meta?.InstagramPublishEnabled ?? false,
            AccountConnected: acc is not null && !string.IsNullOrWhiteSpace(acc.AccessToken),
            ScopeGranted: null,
            PublishScope: IgPublishConst.PublishScope,
            Scheduled: await db.IgScheduledPosts.CountAsync(p => p.Status == IgPublishConst.StScheduled, ct),
            Processing: await db.IgScheduledPosts.CountAsync(p => p.Status == IgPublishConst.StProcessing, ct),
            Failed: await db.IgScheduledPosts.CountAsync(p => p.Status == IgPublishConst.StFailed, ct),
            PublishedThisWeek: await db.IgScheduledPosts.CountAsync(
                p => p.Status == IgPublishConst.StPublished && p.PublishedAt.CompareTo(weekAgo) >= 0, ct));
    }

    // =============================================================================================
    //  ICHKI YORDAMCHILAR
    // =============================================================================================

    /// <summary>
    /// OCHIQ media papkasini tozalaydigan yordamchi.
    ///
    /// <para>Papka yo'li <see cref="PublicMediaDir"/> dan olinadi — yo'l bitta joyda
    /// hisoblansin (u yerda papka mavjudligi ham ta'minlanadi).</para>
    ///
    /// <para>⚠️ Bu tozalash <b>yordamchi</b> ish: u post o'chirish/tahrirlash natijasiga
    /// TA'SIR QILMAYDI (istisno tashqariga chiqmaydi, xato faqat logga tushadi). O'chirilmay
    /// qolgan fayl baribir kunlik <c>SweepAsync</c> ga qoladi.</para>
    /// </summary>
    private MarketingMediaCleanup CleanupContentMedia() => new(db, PublicMediaDir(), logger);

    /// <summary>
    /// So'rov tanasini tekshiradi va SAQLASHGA tayyor qiymatlarni qaytaradi.
    /// <para>Yaratish ham, tahrirlash ham AYNAN shu yordamchidan o'tadi — validatsiya qoidasi
    /// ikki joyda ayri ketmasin.</para>
    /// </summary>
    private static (bool Ok, string Error, string Type, string Caption,
        string MediaJson, string OptionsJson, string ScheduledAt) ReadPostPayload(IgPostPayload payload)
    {
        static (bool, string, string, string, string, string, string) Fail(string msg) =>
            (false, msg, "", "", "", "", "");

        var type = InstagramPublishContract.NormalizePostType(payload.PostType);
        var caption = (payload.Caption ?? "").Trim();

        var mediaJson = IgPublishPayload.WriteMedia(payload.Media);
        // ⚠️ Tekshiruv AYNAN saqlanadigan JSON ustida: worker keyinchalik shu satrni o'qiydi,
        // ya'ni "tekshirilgan narsa" va "saqlangan narsa" bir xil bo'lishi kafolatlanadi.
        var (mediaOk, media, mediaErr) = IgPublishPayload.ReadMedia(mediaJson);
        if (!mediaOk) return Fail(mediaErr);

        var (valid, validErr) = InstagramPublishContract.ValidatePost(type, caption, media);
        if (!valid) return Fail(validErr);

        var options = payload.Options ?? new IgOptionsJson();
        var (colOk, colErr) = InstagramPublishContract.ValidateCollaborators(options.Collaborators);
        if (!colOk) return Fail(colErr);

        // Vaqt berilmasa — HOZIR (post keyingi worker tsiklida joylanadi).
        var scheduledAt = AppClock.Iso();
        if (!string.IsNullOrWhiteSpace(payload.ScheduledAt))
        {
            if (!InstagramContract.TryIso(payload.ScheduledAt, out var at))
                return Fail("Rejalashtirilgan vaqt noto'g'ri.");
            scheduledAt = at.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        return (true, "", type, caption, mediaJson, IgPublishPayload.WriteOptions(options), scheduledAt);
    }

    private static IgPostDto ToPostDto(IgScheduledPost p) => new(
        p.Id, p.PostType, PostTypeLabel(p.PostType), p.Caption,
        IgPublishPayload.ReadMediaRaw(p.MediaJson),
        IgPublishPayload.ReadOptionsRaw(p.OptionsJson),
        p.ScheduledAt, p.Status, StatusLabel(p.Status),
        p.ContainerId.Length > 0, p.ContainerStatus,
        p.MediaId, p.Permalink, p.Attempts, p.Error,
        p.CreatedBy, p.CreatedAt, p.PublishedAt);

    private static string EditBlockedText(string status) => status switch
    {
        IgPublishConst.StPublished =>
            "Joylangan post tahrirlanmaydi — Instagram API'si buni qo'llab-quvvatlamaydi. "
            + "Matnni faqat Instagram ilovasidan o'zgartirish mumkin.",
        IgPublishConst.StProcessing =>
            "Post hozir joylanmoqda — tahrirlash uni o'zgartirmaydi. Bir necha daqiqa kuting.",
        IgPublishConst.StCancelled =>
            "Bekor qilingan post tahrirlanmaydi — yangi reja yarating.",
        _ => "Bu post tahrirlanmaydi — faqat rejalashtirilgan post o'zgartiriladi.",
    };

    private static string PostTypeLabel(string? type) => InstagramPublishContract.NormalizePostType(type) switch
    {
        IgPublishConst.TypeReels => "Reels",
        IgPublishConst.TypeVideo => "Video",
        IgPublishConst.TypeStory => "Story",
        IgPublishConst.TypeCarousel => "Karusel",
        _ => "Rasm",
    };

    private static string StatusLabel(string? status) => InstagramPublishContract.NormalizeStatus(status) switch
    {
        IgPublishConst.StProcessing => "Joylanmoqda",
        IgPublishConst.StPublished => "Joylandi",
        IgPublishConst.StFailed => "Xato",
        IgPublishConst.StCancelled => "Bekor qilingan",
        _ => "Rejalashtirilgan",
    };

    /// <summary>Audit yozuvidagi qisqa ko'rinish (tarixda butun caption o'rinsiz uzun bo'lardi).</summary>
    private static string Preview(string text) => InstagramContract.Trim(text, 80);
}

// =================================================================================================
//  DTO'LAR — `IgPost*` prefiksi (boshqa Instagram partial'lari bilan to'qnashmasin).
// =================================================================================================

/// <summary>Rejalashtirilgan post (ro'yxat va tahrirlash oynasi uchun).</summary>
/// <param name="HasContainer">Meta'da konteyner yaratilganmi — id'ning O'ZI UI'ga kerak emas.</param>
public record IgPostDto(
    string Id, string PostType, string PostTypeLabel, string Caption,
    List<IgMediaJson> Media, IgOptionsJson Options,
    string ScheduledAt, string Status, string StatusLabel,
    bool HasContainer, string ContainerStatus,
    string MediaId, string Permalink, int Attempts, string Error,
    string CreatedBy, string CreatedAt, string PublishedAt);

/// <summary>Yaratish/tahrirlash so'rovi.</summary>
public record IgPostPayload(
    string? PostType, string? Caption, List<IgMediaJson>? Media,
    IgOptionsJson? Options, string? ScheduledAt);

/// <summary>Holatlar bo'yicha sanoq — BUTUN topilma bo'yicha.</summary>
public record IgPostTotalsDto(
    int Total, int Scheduled, int Processing, int Published, int Failed, int Cancelled);

public record IgPostListDto(
    List<IgPostDto> Items, int Total, int Page, int PageSize, IgPostTotalsDto Totals);

/// <summary>O'chirish natijasi — "bekor qilindi" va "yozuv o'chdi" ATAYIN ajratilgan.</summary>
public record IgPostDeleteDto(bool Cancelled, bool Removed, string Message);

/// <summary>Kunlik chop etish limiti. <c>Unknown</c> — Meta jami kvotani bermadi.</summary>
public record IgPostLimitDto(int Usage, int Total, bool Unknown, string Text, string Error);

/// <summary>Kontent bo'limi diagnostikasi. <c>ScopeGranted</c> <c>null</c> — "noma'lum".</summary>
public record IgPostContentStatusDto(
    bool Enabled, bool AccountConnected, bool? ScopeGranted, string PublishScope,
    int Scheduled, int Processing, int Failed, int PublishedThisWeek);
