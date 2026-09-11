using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/* ═══════════════════════════════════════════════════════════════════════════════════════════
   JSON SHAKLLARI — `IgScheduledPost.MediaJson` / `OptionsJson` AYNAN shu ko'rinishda saqlanadi.

   ⚠️ Nega `record` (IgMediaItem/IgPublishOptions) TO'G'RIDAN-TO'G'RI deserializatsiya
   qilinmaydi: .NET 8 dagi System.Text.Json konstruktor parametrlarining STANDART QIYMATINI
   e'tiborsiz qoldiradi va yo'q maydonga `default(T)` beradi. Ya'ni JSON'da `thumbOffsetMs`
   bo'lmasa record'dagi `-1` ("berilmagan") o'rniga `0` ("videoning birinchi kadri") tushardi
   va Meta'ga ortiqcha `thumb_offset=0` ketardi; `kind` esa `null` bo'lib, `ValidateMedia`
   ichidagi `item.AltText.Length` NullReference bilan yiqilardi. Shuning uchun oraliq
   O'ZGARUVCHAN sinf: xossa initializatorlari HAR DOIM ishlaydi.
   ═══════════════════════════════════════════════════════════════════════════════════════════ */

/// <summary>Bitta media elementining saqlanadigan JSON ko'rinishi (`MediaJson` massiv elementi).</summary>
public sealed class IgMediaJson
{
    /// <summary>🔴 OCHIQ HTTPS manzil — faylni Meta O'ZI yuklab oladi (§5.6).</summary>
    public string Url { get; set; } = "";

    /// <summary><c>image</c> | <c>video</c> (<see cref="IgPublishConst.KindImage"/>).</summary>
    public string Kind { get; set; } = IgPublishConst.KindImage;

    /// <summary>0 = "noma'lum", tegishli tekshiruv o'tkazib yuboriladi (<see cref="IgMediaItem"/>).</summary>
    public long SizeBytes { get; set; }

    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Reels muqovasi (ixtiyoriy).</summary>
    public string CoverUrl { get; set; } = "";

    /// <summary>Reels muqova kadri (ms). <b>-1 = berilmagan</b>; 0 — haqiqiy qiymat.</summary>
    public long ThumbOffsetMs { get; set; } = -1;

    public string AltText { get; set; } = "";

    /// <summary>Karusel BOLASIDA matn ishlamaydi — bu maydon faqat shu xatoni
    /// foydalanuvchiga aytish uchun o'qiladi (<c>ValidatePost</c>).</summary>
    public string Caption { get; set; } = "";
}

/// <summary>Post sozlamalarining saqlanadigan JSON ko'rinishi (`OptionsJson`).</summary>
public sealed class IgOptionsJson
{
    /// <summary>Reels'ni lentaga ham chiqarish (faqat Reels uchun ma'noli).</summary>
    public bool ShareToFeed { get; set; } = true;

    public string LocationId { get; set; } = "";

    /// <summary>Hammualliflar (≤3) — ular Instagram'da taklifni QABUL QILISHI kerak.</summary>
    public List<string> Collaborators { get; set; } = new();

    /// <summary>Reels audio nomi — Instagram'da BIR MARTA o'zgartiriladi (§5.9.2).</summary>
    public string AudioName { get; set; } = "";
}

/// <summary>
/// `MediaJson`/`OptionsJson` ni o'qish-yozish — SOF funksiyalar (baza ham, tarmoq ham yo'q),
/// shuning uchun to'liq testlanadi. Controller ham, worker ham AYNAN shu yerdan foydalanadi:
/// aks holda "qanday saqlanadi" va "qanday o'qiladi" ikki joyda ayri ketardi.
/// </summary>
public static class IgPublishPayload
{
    /// <summary>camelCase — frontend bilan bir xil (loyihaning umumiy JSON konvensiyasi).</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Media massivini o'qiydi.
    /// <para>⚠️ Buzuq JSON istisno OTMAYDI — <c>(false, …, sabab)</c> qaytadi va chaqiruvchi
    /// postni tushunarli sabab bilan <c>failed</c> qiladi (fon xizmati yiqilmasin).</para></summary>
    public static (bool Ok, List<IgMediaItem> Items, string Error) ReadMedia(string? json)
    {
        var s = (json ?? "").Trim();
        if (s.Length == 0) return (true, new List<IgMediaItem>(), "");

        try
        {
            var raw = JsonSerializer.Deserialize<List<IgMediaJson>>(s, Json);
            if (raw is null) return (false, new List<IgMediaItem>(), BrokenMedia);

            var items = new List<IgMediaItem>(raw.Count);
            foreach (var m in raw)
            {
                if (m is null) return (false, new List<IgMediaItem>(), BrokenMedia);
                items.Add(ToItem(m));
            }
            return (true, items, "");
        }
        catch (JsonException)
        {
            return (false, new List<IgMediaItem>(), BrokenMedia);
        }
    }

    /// <summary>Sozlamalarni o'qiydi. Buzuq bo'lsa STANDART sozlama qaytadi — sozlama posti
    /// yiqitmasligi kerak (media'dan farqli, u yerda ma'lumot YO'Q).</summary>
    public static IgPublishOptions ReadOptions(string? json)
    {
        var s = (json ?? "").Trim();
        if (s.Length == 0) return new IgPublishOptions();

        try
        {
            var raw = JsonSerializer.Deserialize<IgOptionsJson>(s, Json) ?? new IgOptionsJson();
            return new IgPublishOptions(
                ShareToFeed: raw.ShareToFeed,
                LocationId: (raw.LocationId ?? "").Trim(),
                Collaborators: (raw.Collaborators ?? new List<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .ToList(),
                AudioName: (raw.AudioName ?? "").Trim());
        }
        catch (JsonException)
        {
            return new IgPublishOptions();
        }
    }

    /// <summary>Sof o'qish uchun (UI ro'yxati) — o'zgaruvchan shakl qaytadi.</summary>
    public static List<IgMediaJson> ReadMediaRaw(string? json)
    {
        var s = (json ?? "").Trim();
        if (s.Length == 0) return new List<IgMediaJson>();
        try
        {
            return JsonSerializer.Deserialize<List<IgMediaJson>>(s, Json)?.Where(m => m is not null).ToList()
                   ?? new List<IgMediaJson>();
        }
        catch (JsonException) { return new List<IgMediaJson>(); }
    }

    /// <summary>Sof o'qish uchun (UI) — buzuq bo'lsa standart sozlama.</summary>
    public static IgOptionsJson ReadOptionsRaw(string? json)
    {
        var s = (json ?? "").Trim();
        if (s.Length == 0) return new IgOptionsJson();
        try { return JsonSerializer.Deserialize<IgOptionsJson>(s, Json) ?? new IgOptionsJson(); }
        catch (JsonException) { return new IgOptionsJson(); }
    }

    public static string WriteMedia(IEnumerable<IgMediaJson>? items) =>
        JsonSerializer.Serialize(items ?? Enumerable.Empty<IgMediaJson>(), Json);

    public static string WriteOptions(IgOptionsJson? options) =>
        JsonSerializer.Serialize(options ?? new IgOptionsJson(), Json);

    /// <summary>Saqlangan JSON'ni tekshiruvga tayyor <see cref="IgMediaItem"/> ga o'giradi
    /// (barcha satrlar `null` bo'lmasligi KAFOLATLANADI).</summary>
    private static IgMediaItem ToItem(IgMediaJson m) => new(
        Url: (m.Url ?? "").Trim(),
        Kind: string.Equals((m.Kind ?? "").Trim(), IgPublishConst.KindVideo, StringComparison.OrdinalIgnoreCase)
            ? IgPublishConst.KindVideo
            : IgPublishConst.KindImage,
        SizeBytes: m.SizeBytes,
        DurationSeconds: m.DurationSeconds,
        Width: m.Width,
        Height: m.Height,
        CoverUrl: (m.CoverUrl ?? "").Trim(),
        ThumbOffsetMs: m.ThumbOffsetMs,
        AltText: m.AltText ?? "",
        Caption: m.Caption ?? "");

    private const string BrokenMedia =
        "Post media ma'lumoti buzilgan (JSON o'qib bo'lmadi) — postni tahrirlab, media'ni qayta tanlang.";
}

/// <summary>
/// KONTENT JOYLASH — rejalashtirilgan Instagram postini konteyner yaratishdan chop etishgacha
/// olib boradigan xizmat (§5.7). Bo'lim mantig'ining YAGONA joyi: worker ham
/// (<see cref="ProcessDueAsync"/>), «Hoziroq joylash» tugmasi ham
/// (<see cref="PublishNowAsync"/>) AYNAN shu koddan o'tadi.
///
/// <para><b>🔴 INSTAGRAM'DA NATIVE REJALASHTIRISH YO'Q</b> (§5.2): vaqt bizning navbatimizda,
/// konteyner esa faqat chop etish payti yaratiladi — u 24 soatdan keyin o'ladi.</para>
///
/// <para><b>Darvoza:</b> <c>CenterMeta.InstagramPublishEnabled == false</c> bo'lsa tashqariga
/// HECH QANDAY so'rov ketmaydi (<see cref="MetaLeadgenService"/> dagi bir xil qoida).</para>
///
/// <para><b>⚠️ POLL WORKER'NI BLOKLAMAYDI.</b> Konteyner tayyorlanishi daqiqalar davom etadi,
/// worker tsikli esa 30 soniya. Shuning uchun bu yerda <c>Task.Delay(300s)</c> YO'Q: konteyner
/// <c>IN_PROGRESS</c> bo'lsa post <c>processing</c> holatida QOLADI va keyingi tsiklda davom
/// etadi. Qachon qayta so'rash kerakligini
/// <see cref="InstagramPublishContract.NextPollDelaySeconds"/> (30→60→120→300 s) aytadi.</para>
///
/// <para>Uslub — istisno OTILMAYDI, tuple qaytariladi, xato matni O'ZBEKCHA.</para>
///
/// <para>DI: <c>builder.Services.AddScoped&lt;InstagramPublishService&gt;();</c></para>
/// </summary>
public sealed class InstagramPublishService(
    IAppDbContext db,
    InstagramPublishApi api,
    TelegramService telegram,
    ILogger<InstagramPublishService> logger)
{
    /// <summary>
    /// ⚠️ IKKI OQIM BIR POSTNI OLMASLIGI uchun jarayon ichidagi qulf: worker tsikli va
    /// «Hoziroq joylash» tugmasi bir vaqtda bir postni ko'rsa <b>IKKITA konteyner</b> yaratilib,
    /// post IKKI MARTA chop etilardi — chop etilgan IG media'ni esa API orqali o'chirib
    /// bo'lmaydi (§5.9.1). Ilova bitta nusxada ishlaydi, shuning uchun jarayon ichidagi qulf
    /// yetarli (<c>BookSalesService.NumberGate</c> bilan bir xil yondashuv).
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// KONTEYNER SO'RASH JADVALI — post id → (poll boshlangan vaqt, keyingi so'rov vaqti, sanoq).
    ///
    /// <para>⚠️ <b>Nega xotirada, bazada emas:</b> <see cref="IgScheduledPost"/> da "oxirgi
    /// so'rov vaqti" ustuni YO'Q va migratsiya bu ish doirasidan tashqarida. Bu holat faqat
    /// TEZLIK maslahati: yo'qolsa (ilova qayta ishga tushsa) post keyingi tsiklda darhol
    /// so'raladi — ya'ni eng yomon oqibat bitta ortiqcha so'rov. Ma'lumot yo'qolmaydi, chunki
    /// haqiqiy holat (<c>Status</c>, <c>ContainerId</c>, <c>ContainerStatus</c>) BAZADA.</para>
    ///
    /// <para>⚠️ 10 daqiqalik poll muddati ham shu tayanchdan hisoblanadi; tayanch yo'qolsa
    /// muddat qaytadan boshlanadi. Post baribir cheksiz osilib qolmaydi: konteyner 24 soatda
    /// o'ladi va Meta <c>EXPIRED</c> qaytaradi, biz esa uni <c>failed</c> qilamiz.</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, PollState> Polls = new();

    private sealed record PollState(DateTime StartedAt, DateTime NextPollAt, int PollCount);

    /// <summary>Faqat testlar uchun: xotiradagi poll jadvalini tozalaydi (har test o'z bazasi
    /// bilan ishlaydi, static holat esa testlar orasida oqib ketardi).</summary>
    public static void ResetPollState() => Polls.Clear();

    // =============================================================================================
    //  1) NAVBAT — worker har 30 soniyada chaqiradi
    // =============================================================================================

    /// <summary>
    /// Vaqti kelgan postlarni bir qadam oldinga suradi (§5.7).
    ///
    /// <para>Bir tsiklda ko'pi bilan <see cref="IgPublishConst.QueueBatch"/> (3) ta post
    /// ko'riladi va ULARNING ICHIDA davom etayotganlari (<c>processing</c>) BIRINCHI o'rinda
    /// turadi — aks holda yangi postlar navbatga tushib, boshlangan ish oxiriga yetmasdi.</para>
    /// </summary>
    /// <returns><c>Ok</c> — tsikl muammosiz o'tdimi; <c>Processed</c> — nechta post
    /// oldinga surildi; <c>Error</c> — butun navbatni to'xtatgan sabab (token/akkaunt).</returns>
    public async Task<(bool Ok, int Processed, string Error)> ProcessDueAsync(CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);

        // ── DARVOZA: modul o'chiq bo'lsa tashqariga HECH NARSA chiqmaydi ──
        if (meta is null || !meta.InstagramPublishEnabled) return (true, 0, "");

        await Gate.WaitAsync(ct);
        try
        {
            var now = AppClock.Now;
            var queue = await LoadQueueAsync(now, ct);
            if (queue.Count == 0) return (true, 0, "");

            // ── ⚠️ TOKEN HOLATI — ISH BOSHIDA (§5.9.5) ──
            // "Soat 3:00 da ishga tushgan job'ning tokeni o'lik" — bu modulning eng ko'p
            // uchraydigan nosozligi. Tekshiruv navbatda ISH BOR bo'lgandagina qilinadi:
            // bo'sh navbatda har 30 soniyada xato yozib o'tirishning ma'nosi yo'q.
            var account = await db.IgAccounts.FirstOrDefaultAsync(a => a.IsActive, ct);
            var tokenProblem = TokenProblem(account, now);
            if (tokenProblem.Length > 0)
            {
                // ⚠️ Post `failed` QILINMAYDI — sabab ULANISHDA, postda emas. Admin akkauntni
                // qayta ulaganda navbat o'zi davom etadi. Sabab esa ro'yxatda ko'rinib turadi
                // (jimgina "hech narsa bo'lmayapti" holati eng yomoni).
                foreach (var p in queue) p.Error = tokenProblem;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Instagram kontent navbati to'xtadi: {Error}", tokenProblem);
                return (false, 0, tokenProblem);
            }

            var processed = 0;
            foreach (var post in queue)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (await RunOneAsync(post, account!, meta, ct)) processed++;
                }
                catch (Exception ex)
                {
                    // Bitta post butun navbatni yiqitmasin.
                    logger.LogError(ex, "Instagram postini joylashda kutilmagan xatolik ({Id})", post.Id);
                    await FailAsync(post, meta, "Kutilmagan xatolik: " + ex.Message, hard: false, ct);
                }
            }

            return (true, processed, "");
        }
        finally { Gate.Release(); }
    }

    /// <summary>
    /// Bir tsiklda ko'riladigan postlar: avval DAVOM ETAYOTGANLAR, keyin vaqti kelganlar.
    /// <para>⚠️ SQL faqat QO'POL saralash qiladi (satr taqqoslash), yakuniy qaror esa sof
    /// funksiyada — <see cref="InstagramPublishContract.IsDue"/>. Sabab: buzuq sana yozilgan
    /// qator SQL filtridan o'tib ketishi mumkin, qoida esa BITTA joyda turishi kerak
    /// (<c>contacts.md</c> §3.6 bilan bir xil yondashuv).</para>
    /// </summary>
    private async Task<List<IgScheduledPost>> LoadQueueAsync(DateTime now, CancellationToken ct)
    {
        var batch = IgPublishConst.QueueBatch;

        var processing = await db.IgScheduledPosts
            .Where(p => p.Status == IgPublishConst.StProcessing)
            .OrderBy(p => p.ScheduledAt)
            .Take(batch)
            .ToListAsync(ct);

        var room = batch - processing.Count;
        if (room <= 0) return processing;

        var nowIso = now.ToString("yyyy-MM-ddTHH:mm:ss");
        var due = await db.IgScheduledPosts
            .Where(p => p.Status == IgPublishConst.StScheduled && p.ScheduledAt.CompareTo(nowIso) <= 0)
            .OrderBy(p => p.ScheduledAt)
            .Take(room)
            .ToListAsync(ct);

        processing.AddRange(due.Where(p => InstagramPublishContract.IsDue(p.ScheduledAt, now)));
        return processing;
    }

    // =============================================================================================
    //  2) «HOZIROQ JOYLASH» — qo'lda
    // =============================================================================================

    /// <summary>
    /// Postni navbatni kutmasdan joylash (yoki xatodan keyin QAYTA urinish).
    ///
    /// <para>⚠️ Urinishlar hisobi NOLDAN boshlanadi: odam sababni ko'rib (masalan media
    /// manzilini tuzatib) qayta bosadi, avtomatik hisob esa unga tegishli emas.</para>
    ///
    /// <para>⚠️ Konteyner tayyor bo'lmasa metod KUTMAYDI — post <c>processing</c> bo'lib
    /// qoladi va worker uni oxiriga yetkazadi. So'rov ip'ini 10 daqiqa ushlab turish
    /// mumkin emas.</para>
    /// </summary>
    public async Task<(bool Ok, string Error)> PublishNowAsync(string postId, CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramPublishEnabled)
            return (false, "Kontent joylash moduli o'chirilgan — avval uni Marketing → Sozlamalar bo'limida yoqing.");

        await Gate.WaitAsync(ct);
        try
        {
            var post = await db.IgScheduledPosts.FirstOrDefaultAsync(p => p.Id == postId, ct);
            if (post is null) return (false, "Post topilmadi.");

            if (post.Status == IgPublishConst.StPublished)
                return (false, "Bu post allaqachon Instagram'ga joylangan.");
            if (post.Status == IgPublishConst.StProcessing)
                return (false, "Post hozir joylanmoqda — bir necha daqiqa kuting.");
            if (post.Status == IgPublishConst.StCancelled)
                return (false, "Post bekor qilingan — avval uni tahrirlab, vaqtini qayta belgilang.");

            var now = AppClock.Now;
            var account = await db.IgAccounts.FirstOrDefaultAsync(a => a.IsActive, ct);
            var tokenProblem = TokenProblem(account, now);
            if (tokenProblem.Length > 0) return (false, tokenProblem);

            post.Attempts = 0;
            post.Error = "";
            post.ContainerId = "";
            post.ContainerStatus = "";
            Polls.TryRemove(post.Id, out _);

            await StartAsync(post, account!, meta, now, ct);

            if (post.Status is IgPublishConst.StPublished or IgPublishConst.StProcessing) return (true, "");
            return (false, post.Error.Length > 0 ? post.Error : "Post joylanmadi.");
        }
        finally { Gate.Release(); }
    }

    // =============================================================================================
    //  3) KUNLIK LIMIT — UI indikatori uchun
    // =============================================================================================

    /// <summary>
    /// <c>content_publishing_limit</c> (§5.4).
    /// <para>Darvoza SHU YERDA ham qo'llanadi — "modul o'chiq bo'lsa tashqariga so'rov
    /// ketmaydi" qoidasi controllerda takrorlanmasin.</para>
    /// <para>⚠️ <c>Total == 0</c> — "NOMA'LUM". Meta hujjatlari 100 va 50 deb zid yozadi,
    /// shuning uchun taxminiy qiymat qaytarilmaydi.</para>
    /// </summary>
    public async Task<(bool Ok, int Usage, int Total, string Error)> GetLimitAsync(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramPublishEnabled)
            return (false, 0, IgPublishConst.UnknownQuota,
                "Kontent joylash moduli o'chirilgan — avval uni Marketing → Sozlamalar bo'limida yoqing.");

        var account = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        var tokenProblem = TokenProblem(account, AppClock.Now);
        if (tokenProblem.Length > 0) return (false, 0, IgPublishConst.UnknownQuota, tokenProblem);

        return await api.GetPublishingLimitAsync(account!.IgUserId, account.AccessToken, ct);
    }

    // =============================================================================================
    //  4) BITTA POSTNING QADAMI
    // =============================================================================================

    /// <summary>Postni bir qadam oldinga suradi: konteyner bo'lsa holatini so'raydi, bo'lmasa
    /// yangisini yaratadi.</summary>
    /// <returns>Tarmoqqa chiqib, biror ish qilindimi (statistika uchun).</returns>
    private async Task<bool> RunOneAsync(IgScheduledPost post, IgAccount account, CenterMeta meta, CancellationToken ct)
    {
        var now = AppClock.Now;

        return post.Status == IgPublishConst.StProcessing && post.ContainerId.Length > 0
            ? await ContinueAsync(post, account, meta, now, ct)
            // ⚠️ `processing` bo'lib, lekin konteynersiz qolgan post ham SHU YO'LDAN ketadi:
            // konteyner yaratilayotganda ilova qayta ishga tushgan bo'lsa post o'zini o'zi tiklaydi.
            : await StartAsync(post, account, meta, now, ct);
    }

    /// <summary>1-BOSQICH: tekshirish → limit → konteyner yaratish → darhol birinchi poll.</summary>
    private async Task<bool> StartAsync(
        IgScheduledPost post, IgAccount account, CenterMeta meta, DateTime now, CancellationToken ct)
    {
        // ── a) MA'LUMOTNI O'QISH ──
        var (mediaOk, media, mediaErr) = IgPublishPayload.ReadMedia(post.MediaJson);
        if (!mediaOk)
        {
            // Buzuq JSON qayta urinishdan o'zi tuzalmaydi — DARHOL `failed` (hard).
            await FailAsync(post, meta, mediaErr, hard: true, ct);
            return false;
        }
        var options = IgPublishPayload.ReadOptions(post.OptionsJson);

        // ── b) VALIDATSIYA — o'tmasa TARMOQQA UMUMAN CHIQILMAYDI ──
        var (valid, validErr) = InstagramPublishContract.ValidatePost(post.PostType, post.Caption, media);
        if (!valid)
        {
            await FailAsync(post, meta, validErr, hard: true, ct);
            return false;
        }

        // ── c) KUNLIK LIMIT (§5.4) ──
        var (limitOk, usage, total, limitErr) = await api.GetPublishingLimitAsync(
            account.IgUserId, account.AccessToken, ct);

        if (limitOk && InstagramPublishContract.QuotaExceeded(usage, total))
        {
            // ⚠️ `failed` EMAS: limit sutkalik va o'zi bo'shaydi. Post `scheduled` bo'lib qoladi,
            // urinishlar hisobi ham OSHMAYDI — aks holda limit tugagan kuni post uch tsiklda
            // "xato" bo'lib yonardi.
            post.Error = $"Instagram kunlik chop etish limiti to'ldi ({InstagramPublishContract.QuotaText(usage, total)}) — "
                         + "post limit bo'shashi bilan joylanadi.";
            post.Status = IgPublishConst.StScheduled;
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (!limitOk)
        {
            // ⚠️ Limit so'rovi MASLAHAT xarakterida: javob olinmasa ish TO'XTAMAYDI va urinish
            // ham "kuymaydi". Haqiqiy limitni Meta `media_publish` bosqichida o'zi tekshiradi
            // va `2207042` qaytaradi — u o'zbekcha matnga aylanadi. Ya'ni bu tekshiruvning
            // nosozligi tufayli ishlaydigan postni to'xtatib qo'yish noto'g'ri bo'lardi.
            logger.LogWarning("Instagram kunlik limitini o'qib bo'lmadi (post {Id}): {Error}", post.Id, limitErr);
        }

        // ── d) KONTEYNER ──
        post.Status = IgPublishConst.StProcessing;
        post.ContainerId = "";
        post.ContainerStatus = "";
        post.Error = "";
        // ⚠️ Konteyner yaratishdan OLDIN saqlanadi: shu paytda ilova yiqilsa post `processing`
        // bo'lib qoladi va konteynersiz ekani ko'rinadi — takroriy chop etish emas, qayta
        // BOSHLASH bo'ladi.
        await db.SaveChangesAsync(ct);

        var type = InstagramPublishContract.NormalizePostType(post.PostType);
        string containerId;

        if (type == IgPublishConst.TypeCarousel)
        {
            var childIds = new List<string>(media.Count);
            for (var i = 0; i < media.Count; i++)
            {
                var childReq = InstagramPublishContract.BuildContainerRequest(
                    type, media[i], asCarouselChild: true);
                var (childOk, childId, childErr) = await api.CreateContainerAsync(
                    account.IgUserId, account.AccessToken, childReq, ct);
                if (!childOk)
                {
                    await FailAsync(post, meta, $"{i + 1}-element: {childErr}", hard: false, ct);
                    return true;
                }
                childIds.Add(childId);
            }

            var parentReq = InstagramPublishContract.BuildCarouselParent(childIds, post.Caption, options);
            var (parentOk, parentId, parentErr) = await api.CreateContainerAsync(
                account.IgUserId, account.AccessToken, parentReq, ct);
            if (!parentOk)
            {
                await FailAsync(post, meta, parentErr, hard: false, ct);
                return true;
            }
            containerId = parentId;
        }
        else
        {
            var req = InstagramPublishContract.BuildContainerRequest(type, media[0], post.Caption, options);
            var (ok, id, err) = await api.CreateContainerAsync(account.IgUserId, account.AccessToken, req, ct);
            if (!ok)
            {
                await FailAsync(post, meta, err, hard: false, ct);
                return true;
            }
            containerId = id;
        }

        post.ContainerId = containerId;
        post.ContainerStatus = IgPublishConst.CsInProgress;
        Polls[post.Id] = new PollState(now, now, 0);
        await db.SaveChangesAsync(ct);

        // Rasm konteyneri odatda DARHOL `FINISHED` bo'ladi — birinchi so'rovni shu yerda qilamiz,
        // ya'ni oddiy post AYNI tsiklda joylanadi (30 soniya kutilmaydi).
        await ContinueAsync(post, account, meta, AppClock.Now, ct);
        return true;
    }

    /// <summary>
    /// 2-BOSQICH: konteyner holatini so'rash va tayyor bo'lsa chop etish.
    /// <para>⚠️ Bu metod HECH QACHON kutmaydi (<c>Task.Delay</c> yo'q): vaqti kelmagan bo'lsa
    /// shunchaki chiqadi va keyingi tsiklda qayta chaqiriladi.</para>
    /// </summary>
    private async Task<bool> ContinueAsync(
        IgScheduledPost post, IgAccount account, CenterMeta meta, DateTime now, CancellationToken ct)
    {
        var state = Polls.GetOrAdd(post.Id, _ => new PollState(now, now, 0));

        // ⚠️ Konteyner 24 soatda O'LADI — kutishning ma'nosi yo'q, yangisini yaratamiz.
        if (InstagramPublishContract.IsContainerExpired(
                state.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss"), now))
        {
            post.ContainerId = "";
            post.ContainerStatus = IgPublishConst.CsExpired;
            post.Status = IgPublishConst.StScheduled;
            post.Error = InstagramPublishContract.ErrorText(IgPublishConst.ErrContainerExpired);
            Polls.TryRemove(post.Id, out _);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (now < state.NextPollAt) return false;   // navbatdagi so'rov vaqti hali kelmagan

        var (ok, code, statusText, err) = await api.GetContainerStatusAsync(
            post.ContainerId, account.AccessToken, ct);

        if (!ok)
        {
            // Tarmoq/Graph xatosi — post YIQITILMAYDI, keyingi tsiklda qayta so'raymiz.
            // Muddat (10 daqiqa) o'tgan bo'lsa esa to'xtaymiz.
            post.Error = InstagramContract.Trim(err, 400);
            if (ExpiredPoll(state, now))
            {
                await FailAsync(post, meta, PollTimeoutText(err), hard: false, ct);
                return true;
            }
            Advance(post.Id, state, now);
            await db.SaveChangesAsync(ct);
            return true;
        }

        post.ContainerStatus = code;

        // ⚠️ `PUBLISHED` — konteyner ALLAQACHON chop etilgan (masalan avvalgi urinishda javob
        // yo'qolgan). Ikkinchi marta chop etish TAQIQ: dublikat postni API orqali o'chirib
        // bo'lmaydi. Shuning uchun holat `published` deb yopiladi va sabab ochiq yoziladi.
        if (code == IgPublishConst.CsPublished)
        {
            post.Status = IgPublishConst.StPublished;
            post.PublishedAt = post.PublishedAt.Length > 0 ? post.PublishedAt : AppClock.Iso();
            post.Error = "Post Instagram tomonidan allaqachon chop etilgan deb belgilangan — "
                         + "profilda tekshiring.";
            Polls.TryRemove(post.Id, out _);
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (code == IgPublishConst.CsError || code == IgPublishConst.CsExpired)
        {
            await FailAsync(post, meta, InstagramPublishContract.ContainerErrorText(code, statusText), hard: false, ct);
            return true;
        }

        if (!InstagramPublishContract.IsReadyToPublish(code))
        {
            // IN_PROGRESS — post `processing` da QOLADI, worker bloklanmaydi.
            if (ExpiredPoll(state, now))
            {
                await FailAsync(post, meta,
                    "Instagram postni 10 daqiqada tayyorlab ulgurmadi — media juda katta bo'lishi mumkin. "
                    + "Fayl hajmini kamaytirib, qayta urinib ko'ring.", hard: false, ct);
                return true;
            }
            Advance(post.Id, state, now);
            await db.SaveChangesAsync(ct);
            return true;
        }

        // ── FINISHED → CHOP ETISH ──
        return await PublishContainerAsync(post, account, meta, ct);
    }

    /// <summary>
    /// 3-BOSQICH: <c>media_publish</c>.
    ///
    /// <para>⚠️ <b>QAYTA URINISH YO'Q.</b> Meta postni joylab bo'lib javobni yetkaza olmagan
    /// bo'lsa (5xx/timeout), takroriy so'rov IKKINCHI POST yaratardi — chop etilgan IG media'ni
    /// esa API orqali o'chirib ham, tahrirlab ham bo'lmaydi (§5.9.1). Shuning uchun noaniq
    /// holatda post <c>failed</c> qilinadi va xato matnida "Instagram'da tekshiring" deyiladi:
    /// qayta urinish qarorini ODAM qabul qiladi.</para>
    /// </summary>
    private async Task<bool> PublishContainerAsync(
        IgScheduledPost post, IgAccount account, CenterMeta meta, CancellationToken ct)
    {
        var (ok, mediaId, err) = await api.PublishAsync(
            account.IgUserId, account.AccessToken, post.ContainerId, ct);

        if (!ok)
        {
            await FailAsync(post, meta,
                err + " ⚠️ Post Instagram'da joylangan bo'lishi ham mumkin — avtomatik qayta "
                    + "urinilmaydi, Instagram'da tekshiring.",
                hard: true, ct);
            return true;
        }

        post.MediaId = mediaId;
        post.Status = IgPublishConst.StPublished;
        post.PublishedAt = AppClock.Iso();
        post.Error = "";
        post.ContainerStatus = IgPublishConst.CsPublished;
        Polls.TryRemove(post.Id, out _);
        await db.SaveChangesAsync(ct);

        // Havola IXTIYORIY — olinmasa post baribir `published` bo'lib qoladi (faqat
        // "Instagram'da ochish" tugmasi ko'rinmaydi).
        var (linkOk, permalink, linkErr) = await api.GetMediaPermalinkAsync(mediaId, account.AccessToken, ct);
        if (linkOk && permalink.Length > 0)
        {
            post.Permalink = permalink;
            await db.SaveChangesAsync(ct);
        }
        else if (!linkOk)
        {
            logger.LogInformation("Instagram post havolasi olinmadi ({Id}): {Error}", post.Id, linkErr);
        }

        logger.LogInformation("Instagram posti joylandi ({Id})", post.Id);
        return true;
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    /// <summary>Keyingi so'rov vaqtini belgilaydi (30 → 60 → 120 → 300 s).</summary>
    private static void Advance(string postId, PollState state, DateTime now)
    {
        var next = state.PollCount + 1;
        Polls[postId] = state with
        {
            NextPollAt = now.AddSeconds(InstagramPublishContract.NextPollDelaySeconds(next)),
            PollCount = next,
        };
    }

    private static bool ExpiredPoll(PollState state, DateTime now) =>
        InstagramPublishContract.IsPollExpired(state.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss"), now);

    private static string PollTimeoutText(string lastError) =>
        "Instagram konteyner holatini 10 daqiqa davomida aniqlab bo'lmadi"
        + (lastError.Length > 0 ? $" (oxirgi xato: {lastError})" : "") + ".";

    /// <summary>
    /// XATO: urinishlar hisobi oshadi, chegara oshsa post <c>failed</c> bo'ladi va adminlarga
    /// Telegram signali ketadi.
    /// </summary>
    /// <param name="hard"><c>true</c> — qayta urinishning ma'nosi YO'Q (validatsiya xatosi,
    /// buzuq JSON, noaniq chop etish natijasi): post DARHOL <c>failed</c>.</param>
    private async Task FailAsync(
        IgScheduledPost post, CenterMeta meta, string error, bool hard, CancellationToken ct)
    {
        post.Attempts = hard ? IgPublishConst.MaxAttempts : post.Attempts + 1;
        post.Error = InstagramContract.Trim(error, 400);
        post.ContainerId = "";
        post.ContainerStatus = "";
        Polls.TryRemove(post.Id, out _);

        if (post.Attempts >= IgPublishConst.MaxAttempts)
        {
            post.Status = IgPublishConst.StFailed;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Instagram posti joylanmadi ({Id}): {Error}", post.Id, post.Error);
            await NotifyAdminsAsync(meta,
                $"⚠️ Instagram: rejalashtirilgan post joylanmadi — {post.Error}", ct);
            return;
        }

        // Hali urinish bor — post navbatga QAYTADI (keyingi tsiklda yangi konteyner yaratiladi).
        post.Status = IgPublishConst.StScheduled;
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Instagram posti joylanmadi, qayta urinamiz ({Id}, {Attempts}-urinish): {Error}",
            post.Id, post.Attempts, post.Error);
    }

    /// <summary>
    /// Akkaunt/token holatini tekshiradi — bo'sh satr "hammasi joyida".
    /// <para>⚠️ <c>TokenExpiresAt</c> o'qilmasa (eski qatorda bo'sh bo'lishi mumkin) ish
    /// TO'XTATILMAYDI: muddatni bilmaslik "muddati o'tgan" degani emas, va bu tekshiruv
    /// ishlaydigan modulni bloklab qo'ymasligi kerak.</para>
    /// </summary>
    private static string TokenProblem(IgAccount? account, DateTime now)
    {
        if (account is null || string.IsNullOrWhiteSpace(account.AccessToken))
            return "Instagram akkaunt ulanmagan — Marketing → Sozlamalar bo'limidan ulang.";
        if (string.IsNullOrWhiteSpace(account.IgUserId))
            return "Instagram akkaunt id'si noma'lum — akkauntni qayta ulang.";
        if (InstagramContract.TryIso(account.TokenExpiresAt, out var expires) && expires <= now)
            return "Instagram tokeni muddati tugagan — Sozlamalarda «Qayta ulash» bosing.";
        return "";
    }

    /// <summary>
    /// Adminlarga Telegram signali (xatosi JIM yutiladi — xabarnoma navbatni buzmasin).
    ///
    /// <para>⚠️ <see cref="InstagramPipeline.NotifyAdminsAsync"/> TO'G'RIDAN-TO'G'RI
    /// chaqirilmadi: u <c>InstagramEnabled</c> (AI agenti) bayrog'i bilan darvozalangan, bu
    /// modul esa undan MUSTAQIL — avtojavob o'chirilgan markazda ham kontent joylanishi
    /// mumkin va o'shanda signal jimgina yo'qolardi. Qolgan qoidalar (bir chatga bir marta,
    /// faqat admin/superadmin) AYNAN o'sha naqshda.</para>
    /// </summary>
    private async Task NotifyAdminsAsync(CenterMeta? meta, string text, CancellationToken ct)
    {
        try
        {
            if (meta is null || !meta.InstagramPublishEnabled || !meta.InstagramNotifyTelegram) return;
            if (!telegram.IsConfigured) return;

            var regs = await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "")
                .ToListAsync(ct);
            if (regs.Count == 0) return;

            var userIds = regs.Select(r => r.UserId!).Distinct().ToList();
            var adminIds = (await db.Users
                .Where(u => userIds.Contains(u.Id) && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
                .Select(u => u.Id)
                .ToListAsync(ct)).ToHashSet();
            if (adminIds.Count == 0) return;

            var sent = new HashSet<long>();
            foreach (var r in regs)
            {
                if (r.UserId is null || !adminIds.Contains(r.UserId)) continue;
                if (!sent.Add(r.ChatId)) continue;
                await telegram.SendMessageAsync(r.ChatId, text, ct: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instagram kontent signali yuborilmadi");
        }
    }
}
