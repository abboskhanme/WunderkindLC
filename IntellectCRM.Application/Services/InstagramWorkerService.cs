using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Instagram modulining FON XIZMATI — uchta vazifa:
/// <list type="number">
///   <item><b>Navbat:</b> har 2 soniyada <c>pending</c> hodisalarni (10 tagacha)
///     <see cref="InstagramPipeline"/> ga beradi. Webhook controlleri faqat yozib 200 qaytaradi
///     (Meta 5 soniya kutadi), haqiqiy ish shu yerda bajariladi.</item>
///   <item><b>Token:</b> kuniga bir marta — muddatiga 15 kundan kam qolgan tokenni yangilaydi
///     (60 kunlik token amalda 45-kunda yangilanadi). Muvaffaqiyatsiz bo'lsa Telegram signali.</item>
///   <item><b>Tozalash:</b> kuniga bir marta — 30 kundan eski <c>done</c>/<c>skipped</c>
///     hodisalar o'chiriladi (jadval cheksiz o'smasin).</item>
/// </list>
///
/// <para><b>⚠️ IKKALA bayroq ham o'chiq bo'lsa (<c>CenterMeta.InstagramEnabled</c> — avtojavob,
/// <c>InstagramLeadAdsEnabled</c> — reklama lidlari) xizmat UMUMAN ishlamaydi: navbat ham qayta
/// ishlanmaydi, token ham yangilanmaydi, ya'ni HECH QANDAY tashqi so'rov ketmaydi. Webhook
/// baribir qabul qilinaveradi: modul yoqilganda tarix joyida turadi.</para>
///
/// <para>Bayroqlar MUSTAQIL: markaz AI agentini ishlatmasdan ham reklama lidlarini olishi mumkin.
/// Har hodisa turining o'z darvozasi bor, shuning uchun navbat ochiq bo'lgani "hammasi ishlaydi"
/// degani emas — yaroqsiz turdagi hodisa `skipped` bo'lib sababi bilan qoladi.</para>
///
/// <para>DI: <c>builder.Services.AddHostedService&lt;InstagramWorkerService&gt;();</c></para>
/// </summary>
public class InstagramWorkerService(
    IServiceProvider services,
    ILogger<InstagramWorkerService> logger) : BackgroundService
{
    private DateOnly _lastDaily = DateOnly.MinValue;
    /// <summary>Reklama statistikasi oxirgi marta qaysi kuni sinxronlangani (xotirada).</summary>
    private DateOnly _lastAdsSync = DateOnly.MinValue;
    /// <summary>CAPI skani oxirgi marta qaysi kuni bajarilgani (xotirada).</summary>
    private DateOnly _lastCapiScan = DateOnly.MinValue;
    /// <summary>Kontent joylash tsikli oxirgi marta qachon ishlagani.</summary>
    private DateTime _lastPublishTick = DateTime.MinValue;
    /// <summary>Bilim bazasi vektorlari oxirgi marta qachon hisoblangani.</summary>
    private DateTime _lastEmbedTick = DateTime.MinValue;

    /// <summary>Kontent joylash tsikli oralig'i (soniya) — navbat tsiklidan sekinroq.</summary>
    private const int PublishTickSeconds = 30;

    /// <summary>Bilim bazasi vektorlari tsikli oralig'i (soniya).
    /// <para>Sekinroq: bilim bazasi kamdan-kam o'zgaradi, har tsiklda so'rash esa Gemini
    /// kvotasini bekorga yeyardi.</para></summary>
    private const int EmbedTickSeconds = 60;

    /// <summary>CenterMeta kesh muddati (soniya) — sozlamalar HAR tsiklda emas, shu oraliqda
    /// bir marta o'qiladi. Har 2 soniyada bazaga borish (modul o'chiq bo'lsa ham) bekorga yuk edi.</summary>
    private const int MetaCacheSeconds = 30;

    /// <summary>Barcha Instagram modullari O'CHIQ bo'lganda tsikl oralig'i (soniya) —
    /// qiladigan tezkor ish yo'q, 2 soniyalik aylanish bekorga scope ochardi.</summary>
    private const int IdleTickSeconds = 60;

    /// <summary>Keshlangan CenterMeta (AsNoTracking — scope yopilgach ham xavfsiz o'qiladigan POCO).</summary>
    private CenterMeta? _cachedMeta;
    /// <summary>CenterMeta oxirgi o'qilgan vaqti.</summary>
    private DateTime _lastMetaRead = DateTime.MinValue;
    /// <summary>Oxirgi tsiklda barcha modullar o'chiq bo'lganmi — keyingi kutish shundan tanlanadi.</summary>
    private bool _idle;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Instagram fon siklida xatolik"); }
            // Barcha modullar o'chiq bo'lsa tsikl 60 soniyaga cho'ziladi. Modul yoqilganda
            // (meta keshi yangilangach) 2 soniyalik tsikl O'ZI qaytadi — yoqish ko'pi bilan
            // ~60 soniyada seziladi. QABUL QILINGAN SAVDO: fon xizmati uchun bir daqiqagacha
            // kechikish zararsiz, tejaladigan yuk esa doimiy. Modul yoqiq bo'lsa xulq
            // O'ZGARMAGAN — 2 soniyalik tsikl qoladi (webhook navbati kechikmasin).
            try { await Task.Delay(TimeSpan.FromSeconds(_idle ? IdleTickSeconds : 2), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();

        // CenterMeta HAR tsiklda emas, MetaCacheSeconds da bir o'qiladi. Sozlama (bayroq)
        // o'zgarishi ko'pi bilan 30 soniyada seziladi — qabul qilingan savdo.
        if (_cachedMeta is null || (AppClock.Now - _lastMetaRead).TotalSeconds >= MetaCacheSeconds)
        {
            _cachedMeta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
            _lastMetaRead = AppClock.Now;
        }
        var meta = _cachedMeta;
        if (meta is null) { _idle = true; return; }

        // Hech bir modul yoqilmagan bo'lsa keyingi kutish IdleTickSeconds (ExecuteAsync o'qiydi).
        // Kunlik ishlar (tozalash §5, yetim media) 60 soniyalik tsiklda ham o'z vaqtida bajariladi.
        _idle = !(meta.InstagramEnabled || meta.InstagramLeadAdsEnabled
                  || meta.InstagramPublishEnabled || meta.InstagramAdsStatsEnabled
                  || meta.InstagramCapiEnabled);

        // ⚠️ BIR NECHTA MUSTAQIL MODUL, bitta fon xizmati. Har birining O'Z bayrog'i bor va
        // biri o'chiq bo'lgani boshqasini to'xtatmasligi SHART. Ilgari bu yerda yalang
        // `if (!meta.InstagramEnabled) return;` turardi — AI agentini ishlatmaydigan markazda
        // reklama lidlari navbatda TURIB QOLARDI. Yangi vazifa qo'shsangiz uni ham ALOHIDA
        // darvoza ostiga qo'ying, umumiy `return` ga qo'shmang.
        //
        //   InstagramEnabled        → izoh/DM avtojavobi + token yangilash
        //   InstagramLeadAdsEnabled → reklama formasi lidlari (webhook navbati)
        //   InstagramPublishEnabled → kontent joylash (har 30 soniyada)
        //   InstagramAdsStatsEnabled→ reklama statistikasi (kuniga bir marta, belgilangan soatda)
        //   InstagramCapiEnabled    → lid sifatini Meta'ga qaytarish (kuniga bir marta)

        // ── 1) WEBHOOK NAVBATI (izoh/DM va reklama lidlari umumiy navbatda) ──
        if (meta.InstagramEnabled || meta.InstagramLeadAdsEnabled)
            await ProcessQueueAsync(sp, db, ct);

        // ── 2) KONTENT JOYLASH — har 30 soniyada ──
        // Navbat tsikli 2 soniyada bir marta aylanadi; joylash bundan tez-tez kerak emas va
        // `content_publishing_limit` so'rovi bekorga sarflanardi.
        if (meta.InstagramPublishEnabled && (AppClock.Now - _lastPublishTick).TotalSeconds >= PublishTickSeconds)
        {
            _lastPublishTick = AppClock.Now;
            try { await sp.GetRequiredService<InstagramPublishService>().ProcessDueAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Instagram kontentini joylashda xatolik"); }
        }

        // ── 2b) BILIM BAZASI VEKTORLARI (RAG) — har 60 soniyada ──
        // ⚠️ `InstagramEnabled` darvozasi ostida: vektor faqat AI agenti uchun kerak.
        // `EmbedPendingAsync` o'zi ham shu bayroqni tekshiradi (ikki qavat) va birinchi
        // xatoda to'xtaydi — kvota bekorga sarflanmasin.
        if (meta.InstagramEnabled && (AppClock.Now - _lastEmbedTick).TotalSeconds >= EmbedTickSeconds)
        {
            _lastEmbedTick = AppClock.Now;
            try { await sp.GetRequiredService<IgEmbeddingService>().EmbedPendingAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Bilim bazasi vektorlarini hisoblashda xatolik"); }
        }

        // ── 3) REKLAMA STATISTIKASI — kuniga bir marta, `InstagramAdsSyncHour` da ──
        // ⚠️ Soat tekshiruvi SHU YERDA, servisda emas: servis qo'lda ham chaqiriladi
        // («Yangilash» tugmasi) va u yerda soatning ahamiyati yo'q.
        // Marker XOTIRADA: qayta ishga tushirilsa o'sha soat ichida yana bir marta ishlashi
        // mumkin, lekin sinxronizatsiya upsert bo'lgani uchun bu zararsiz (dublikat yaratmaydi).
        if (meta.InstagramAdsStatsEnabled
            && _lastAdsSync != AppClock.Today
            && AppClock.Now.Hour == Math.Clamp(meta.InstagramAdsSyncHour, 0, 23))
        {
            _lastAdsSync = AppClock.Today;
            try { await sp.GetRequiredService<MetaInsightsService>().SyncAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Reklama statistikasini sinxronlashda xatolik"); }
        }

        // ── 4) CAPI — kuniga bir marta ──
        // Kechikish muhim emas: hodisa vaqti CRM'dagi haqiqiy sana bo'yicha qo'yiladi,
        // Meta esa 7 kungacha eski hodisani qabul qiladi (`MetaCapiPayload.MaxEventAgeDays`).
        if (meta.InstagramCapiEnabled && _lastCapiScan != AppClock.Today)
        {
            _lastCapiScan = AppClock.Today;
            try { await sp.GetRequiredService<MetaCapiService>().ScanAndSendAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "CAPI hodisalarini yuborishda xatolik"); }
        }

        // ── 5) KUNLIK: token yangilash + navbatni tozalash ──
        var today = AppClock.Today;
        if (_lastDaily == today) return;
        _lastDaily = today;

        // Tozalash HAR DOIM bajariladi — navbat jadvali qaysi modul yoqilganidan qat'i nazar
        // o'sadi (webhook hodisani modul o'chiq bo'lsa ham qabul qiladi).
        try { await CleanupAsync(db, ct); }
        catch (Exception ex) { logger.LogError(ex, "Instagram navbatini tozalashda xatolik"); }

        // YETIM MEDIA FAYLLARI — `uploads/marketing-public/` da hech qaysi postda ishlatilmayotgan
        // fayllar. ⚠️ DARVOZASIZ, ATAYIN: modul o'chirilgandan keyin ham eski yetimlar tozalanishi
        // kerak (bu papka OCHIQ va tungi zaxiraga kiradi). Tashqi so'rov ketmaydi — faqat disk.
        // ⚠️ Kuniga bir marta yetarli: 24 soatlik yosh sharti tufayli undan yosh fayl baribir
        // tegilmaydi (foydalanuvchi yuklab, postni hali saqlamagan bo'lishi mumkin).
        try
        {
            await sp.GetRequiredService<MarketingMediaCleanup>().SweepAsync(ct);
        }
        catch (Exception ex) { logger.LogError(ex, "Yetim media fayllarini tozalashda xatolik"); }

        // Token yangilash — Instagram Login tokenini ISHLATADIGAN modullardan kamida bittasi
        // yoqilgan bo'lsa. Reklama lidlari/statistikasi va CAPI Page / System User / Dataset
        // tokenlari bilan ishlaydi, ular bu yerda yangilanmaydi (muddatsiz).
        //
        // ⚠️ Ilgari bu yerda faqat `InstagramEnabled` turardi va faqat kontent joylashni
        // ishlatadigan markazda token 60 kunda JIMGINA o'lardi (`InstagramPublishService` ham
        // AYNAN shu tokendan foydalanadi). Qoida — `InstagramContract.NeedsLoginToken`.
        if (!InstagramContract.NeedsLoginToken(meta.InstagramEnabled, meta.InstagramPublishEnabled)) return;

        var telegram = sp.GetRequiredService<TelegramService>();
        try { await RefreshTokensAsync(sp, db, meta, telegram, ct); }
        catch (Exception ex) { logger.LogError(ex, "Instagram tokenini yangilashda xatolik"); }
    }

    /* ═════════════════════════ 1) Navbat ═════════════════════════ */

    private async Task ProcessQueueAsync(IServiceProvider sp, IAppDbContext db, CancellationToken ct)
    {
        var pending = await db.IgWebhookEvents
            .Where(e => e.Status == IgConst.EvPending)
            .OrderBy(e => e.ReceivedAt)
            .Take(IgConst.QueueBatch)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        // Uch marta urinilgani — `failed`. Cheksiz sikl bo'lmasin, xato esa diagnostikada qolsin.
        var ready = new List<string>();
        var changed = false;
        foreach (var e in pending)
        {
            if (e.Attempts >= IgConst.MaxAttempts)
            {
                e.Status = IgConst.EvFailed;
                e.ProcessedAt = AppClock.Iso();
                if (e.Error.Length == 0) e.Error = $"{IgConst.MaxAttempts} marta urinildi — muvaffaqiyatsiz.";
                changed = true;
                continue;
            }
            ready.Add(e.Id);
        }
        if (changed) await db.SaveChangesAsync(ct);
        if (ready.Count == 0) return;

        var pipeline = sp.GetRequiredService<InstagramPipeline>();
        foreach (var id in ready)
        {
            if (ct.IsCancellationRequested) return;
            // Pipeline O'Z scope'ida ishlaydi va o'zi holatni yozadi — bu yerda faqat id uzatiladi.
            await pipeline.ProcessAsync(id, ct);
        }
    }

    /* ═════════════════════════ 2) Token ═════════════════════════ */

    private async Task RefreshTokensAsync(
        IServiceProvider sp, IAppDbContext db, CenterMeta meta, TelegramService telegram, CancellationToken ct)
    {
        var accounts = await db.IgAccounts.Where(a => a.IsActive && a.AccessToken != "").ToListAsync(ct);
        if (accounts.Count == 0) return;

        var api = sp.GetRequiredService<InstagramApi>();
        var now = AppClock.Now;

        foreach (var acc in accounts)
        {
            // Muddat noma'lum bo'lsa ham yangilaymiz: "bilmaymiz" holati tokenning jimgina
            // o'lishiga olib kelmasin.
            var known = InstagramContract.TryIso(acc.TokenExpiresAt, out var expires);
            if (known && (expires - now).TotalDays > IgConst.TokenRefreshBeforeDays) continue;

            var (ok, token, expiresIn, err) = await api.RefreshTokenAsync(acc.AccessToken, ct);
            if (ok)
            {
                acc.AccessToken = token;
                acc.TokenRefreshedAt = AppClock.Iso();
                acc.TokenExpiresAt = now.AddSeconds(expiresIn > 0 ? expiresIn : 60 * 24 * 3600)
                    .ToString("yyyy-MM-ddTHH:mm:ss");
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Instagram tokeni yangilandi (@{User})", acc.Username);
            }
            else
            {
                logger.LogWarning("Instagram tokenini yangilab bo'lmadi (@{User}): {Err}", acc.Username, err);
                await InstagramPipeline.NotifyAdminsAsync(db, telegram, meta,
                    $"🔑 Instagram: tokenni yangilab bo'lmadi (@{acc.Username}). {err}\n" +
                    "Marketing → Sozlamalar bo'limidan akkauntni QAYTA ULANG.", ct);
            }
        }
    }

    /* ═════════════════════════ 3) Tozalash ═════════════════════════ */

    private async Task CleanupAsync(IAppDbContext db, CancellationToken ct)
    {
        var cutoff = AppClock.Now.AddDays(-IgConst.EventRetentionDays).ToString("yyyy-MM-ddTHH:mm:ss");
        var old = await db.IgWebhookEvents
            .Where(e => (e.Status == IgConst.EvDone || e.Status == IgConst.EvSkipped)
                        && e.ReceivedAt.CompareTo(cutoff) < 0)
            .Take(1000)   // bir kunda 1000 tadan — katta jadvalda ham xotira shishmasin
            .ToListAsync(ct);
        if (old.Count == 0) return;

        db.IgWebhookEvents.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Instagram navbatidan {Count} ta eski hodisa tozalandi", old.Count);
    }
}
