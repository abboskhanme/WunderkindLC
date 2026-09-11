using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// REKLAMA STATISTIKASI (Meta Ads Insights) — Meta'dagi kunlik faktlarni bazaga ko'chiruvchi
/// sinxronizator: <c>iyerarxiya (campaign/adset/ad) → kunlik insights → UPSERT</c>.
///
/// <para><b>Nega mahalliy nusxa kerak?</b> ROI hisoboti sarfni CRM'dagi to'lovlar bilan JOIN
/// qiladi — buni Meta tomonida qilib bo'lmaydi; Insights rate limiti esa har ekran ochilganda
/// so'rov yuborishga imkon bermaydi (§4.6).</para>
///
/// <para><b>Uslub — <see cref="MetaLeadgenService"/> bilan bir xil: ISTISNO OTILMAYDI.</b>
/// Har metod <c>(Ok, Rows, Error)</c> qaytaradi; <c>Error</c> — foydalanuvchi ko'radigan
/// O'ZBEKCHA matn va u <see cref="IgAdAccount.LastError"/> ga ham yoziladi (aks holda nosozlik
/// "reklama ishlayapti, statistika yangilanmayapti" bo'lib bir oydan keyin sezilardi).</para>
///
/// <para><b>⚠️ MODUL DARVOZASI:</b> <c>CenterMeta.InstagramAdsStatsEnabled == false</c> bo'lsa
/// tashqariga <b>hech qanday so'rov ketmaydi</b> — akkaunt va tokendan oldin AYNAN shu bayroq
/// tekshiriladi.</para>
///
/// <para>DI: <c>builder.Services.AddScoped&lt;MetaInsightsService&gt;();</c></para>
/// </summary>
public sealed class MetaInsightsService(
    IAppDbContext db,
    MetaInsightsApi api,
    TelegramService telegram,
    ILogger<MetaInsightsService> logger)
{
    /// <summary>Bitta so'rovga tushadigan eng katta oraliq (kun).
    /// <para>⚠️ 90 kunlik backfill BITTA so'rovda so'ralmaydi: <c>level=ad</c> +
    /// <c>time_increment=1</c> + <c>breakdowns=publisher_platform</c> bilan bir kunda bitta
    /// reklama 2–3 qator beradi, ya'ni 90 kun × 50 reklama × 2 = 9000 qator — bu
    /// <see cref="MetaInsightsApi.MaxPages"/> to'sig'idan ham, Meta'ning "juda ko'p ma'lumot"
    /// xatosidan (100/1487534) ham o'tolmasdi.</para></summary>
    public const int ChunkDays = 10;

    /// <summary>Kunlik sinxronizatsiyada QAYTA yuklanadigan kunlar soni.
    /// <para>⚠️ Faqat "kecha" emas: Meta atributsiyani 48 soatgacha (ba'zi hodisalarda 7 kunlik
    /// oyna bilan) tuzatib turadi, ya'ni bir marta yozilgan kun keyin ham o'zgaradi. Upsert
    /// bo'lgani uchun qayta yuklash dublikat yaratmaydi.</para></summary>
    public const int ReloadDays = 7;

    /// <summary>Bir sinxronizatsiyada ruxsat etilgan eng uzun oraliq (kun) — qo'lda kiritilgan
    /// "2020-01-01 dan bugungacha" so'rovi kvotani bir zumda yeb qo'ymasin.</summary>
    public const int MaxRangeDays = 365;

    /// <summary>Kvota shu foizdan oshsa sinxronizatsiya TO'XTATILADI (§4.6).
    /// <para>⚠️ Meta ochiq aytadi: limitga yetganda chaqiruvni to'xtatish kerak, davom etilsa
    /// blok UZAYADI. 100% ni kutib o'tirmaymiz — oxirgi bo'lak baribir yarim kelardi.</para></summary>
    public const int QuotaStopPct = 95;

    /// <summary>Oraliqni qisqartirish (100/1487534) ko'pi bilan shuncha marta bo'linadi —
    /// buzuq javob cheksiz bo'linishga aylanib ketmasin.</summary>
    private const int MaxSplits = 24;

    /// <summary><see cref="IgAdAccount.LastError"/> ga yoziladigan matn uzunligi.</summary>
    private const int ErrorMax = 400;

    /* ═════════════════════════ Ommaviy kirish nuqtalari ═════════════════════════ */

    /// <summary>
    /// KUNLIK sinxronizatsiya (worker chaqiradi). Oraliqni O'ZI tanlaydi:
    /// <list type="bullet">
    ///   <item>birinchi ulanish (<c>LastSyncAt</c> bo'sh) → <c>InstagramAdsBackfillDays</c> kun
    ///         orqaga, <see cref="ChunkDays"/> kunlik bo'laklarda;</item>
    ///   <item>keyingi kunlar → oxirgi <see cref="ReloadDays"/> kun QAYTA yuklanadi.</item>
    /// </list>
    /// </summary>
    public async Task<(bool Ok, int Rows, string Error)> SyncAsync(CancellationToken ct)
    {
        var (meta, acc, gateError) = await GateAsync(ct);
        if (gateError.Length > 0) return (false, 0, gateError);

        var today = TodayInAccountZone(acc!.TimezoneName);

        // ⚠️ Backfill kunlari CenterMeta'dan keladi, ya'ni admin uni 5000 qilib qo'yishi mumkin —
        // qiymat qisiladi, aks holda birinchi ishga tushish kvotani butunlay yeb qo'yardi.
        var backfill = Math.Clamp(meta!.InstagramAdsBackfillDays, 1, MaxRangeDays);
        var first = acc.LastSyncAt.Length == 0;
        var days = first ? backfill : ReloadDays;

        var since = today.AddDays(-(days - 1));
        logger.LogInformation(
            "Meta Ads Insights sinxronizatsiyasi: {Since} … {Until} ({Kind})",
            Fmt(since), Fmt(today), first ? "birinchi yuklash" : "kunlik");

        return await RunAsync(meta, acc, since, today, ct);
    }

    /// <summary>
    /// QO'LDA tanlangan oraliq ("Yangilash" tugmasi). Sanalar <c>yyyy-MM-dd</c> ko'rinishida —
    /// ular AKKAUNT vaqt zonasidagi kunlar (§4.5), server zonasi bilan aralashtirilmaydi.
    /// </summary>
    public async Task<(bool Ok, int Rows, string Error)> SyncRangeAsync(
        string since, string until, CancellationToken ct)
    {
        var (meta, acc, gateError) = await GateAsync(ct);
        if (gateError.Length > 0) return (false, 0, gateError);

        if (!TryDate(since, out var from) || !TryDate(until, out var to))
            return (false, 0, "Sana formati noto'g'ri — \"yyyy-MM-dd\" bo'lishi kerak.");

        if (from > to) return (false, 0, "Sana oralig'i teskari — boshlanish sanasi tugash sanasidan keyin.");

        // ⚠️ Kelajakdagi kun so'ralmaydi: Meta bo'sh qaytaradi, kvota esa baribir sarflanadi.
        // Chegara AKKAUNT zonasidagi bugun bo'yicha (bizning "ertaga" u yerda hali "bugun").
        var today = TodayInAccountZone(acc!.TimezoneName);
        if (from > today) return (false, 0, "Tanlangan oraliq kelajakda — statistika hali yo'q.");
        if (to > today) to = today;

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            return (false, 0, $"Oraliq juda uzun — ko'pi bilan {MaxRangeDays} kun so'rash mumkin.");

        return await RunAsync(meta!, acc, from, to, ct);
    }

    /* ═════════════════════════ Darvoza ═════════════════════════ */

    /// <summary>
    /// Tashqi so'rovdan OLDIN tekshiriladigan uchta shart: modul yoqilganmi, akkaunt ulanganmi,
    /// tokeni bormi.
    ///
    /// <para><b>⚠️ Tartib muhim:</b> bayroq birinchi tekshiriladi — o'chirilgan modulda akkaunt
    /// bor-yo'qligini aytish ham keraksiz, lekin asosiysi shundan keyin HECH QANDAY tarmoq
    /// chaqiruvi bo'lmasligi (modul darvozasi qoidasi).</para>
    /// </summary>
    private async Task<(CenterMeta? Meta, IgAdAccount? Account, string Error)> GateAsync(CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.InstagramAdsStatsEnabled)
            return (null, null, "Reklama statistikasi moduli o'chirilgan — Marketing → Sozlamalar bo'limidan yoqing.");

        // Kuzatilgan holda (AsNoTracking SIZ) — LastSyncAt/LastError shu obyektga yoziladi.
        var acc = await db.IgAdAccounts
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        if (acc is null)
            return (meta, null, "Reklama akkaunti ulanmagan — Marketing → Sozlamalar bo'limida ulang.");

        if (string.IsNullOrWhiteSpace(acc.AccessToken))
            return (meta, acc, "Reklama akkaunti tokeni yo'q — Marketing → Sozlamalar bo'limida saqlang.");

        return (meta, acc, "");
    }

    /* ═════════════════════════ Asosiy oqim ═════════════════════════ */

    private async Task<(bool Ok, int Rows, string Error)> RunAsync(
        CenterMeta meta, IgAdAccount acc, DateOnly since, DateOnly until, CancellationToken ct)
    {
        // ── 1) Akkaunt ma'lumoti (valyuta + zona) ──
        // Faqat BIR MARTA (yoki valyuta noma'lum bo'lsa) so'raladi: har kunlik sinxronizatsiyada
        // qayta so'rash kvotadan bekorga yeyishdir, valyuta esa deyarli o'zgarmaydi.
        if (acc.Currency.Length == 0)
        {
            var (okAcc, info, errAcc) = await api.FetchAccountAsync(acc.AdAccountId, acc.AccessToken, ct);
            if (!okAcc) return await FailAsync(meta, acc, errAcc, 0, ct);

            ApplyAccountInfo(acc, info!);
            await db.SaveChangesAsync(ct);
        }

        // ── 2) IYERARXIYA ──
        // Insights faqat ID qaytaradi, NOM qaytarmaydi — nomlarsiz hisobot o'qib bo'lmaydigan
        // raqamlar to'plamiga aylanadi.
        var (okEnt, entRows, errEnt) = await api.FetchEntitiesAsync(acc.AdAccountId, acc.AccessToken, ct);

        // ⚠️ Qisman kelgan iyerarxiya ham SAQLANADI: yig'ilgan qatorlarda ism/holat baribir
        // yangilangan, ularni tashlab yuborish faqat zarar qilardi.
        if (entRows.Count > 0)
        {
            await UpsertEntitiesAsync(acc, entRows, ct);
            await db.SaveChangesAsync(ct);
        }
        if (!okEnt) return await FailAsync(meta, acc, errEnt, 0, ct);

        // ── 3) KUNLIK STATISTIKA (bo'laklab) ──
        var offset = MetaCurrency.Clamp(acc.CurrencyOffset);
        var queue = new LinkedList<(DateOnly From, DateOnly To)>();
        foreach (var chunk in Chunks(since, until, ChunkDays)) queue.AddLast(chunk);

        var total = 0;
        var splits = 0;

        while (queue.First is { } node)
        {
            var (from, to) = node.Value;
            queue.RemoveFirst();

            var (ok, rows, err) = await api.FetchInsightsAsync(
                acc.AdAccountId, acc.AccessToken, Fmt(from), Fmt(to), ct, offset);

            if (!ok)
            {
                // ⚠️ "Bir so'rovda juda ko'p ma'lumot" (100/1487534) — YAGONA holat, unda KUTISH
                // yordam bermaydi: oraliq ikkiga bo'linadi va qaytadan so'raladi. Qolgan barcha
                // xatolarda (token, ruxsat, kvota) qayta urinish TAQIQ — u kvotani yanada
                // kamaytiradi (formulada `− 0.001 × xatolar`).
                if (Classify(api.LastErrorCode, api.LastErrorSubcode, err) == SyncFailure.Shrink
                    && from < to && splits < MaxSplits)
                {
                    splits++;
                    var mid = from.AddDays((to.DayNumber - from.DayNumber) / 2);
                    queue.AddFirst((mid.AddDays(1), to));
                    queue.AddFirst((from, mid));
                    logger.LogWarning(
                        "Meta Ads Insights: {From}…{To} oralig'i katta keldi — ikkiga bo'linadi.", Fmt(from), Fmt(to));
                    continue;
                }

                return await FailAsync(meta, acc, err, total, ct);
            }

            // ⚠️ Har bo'lak O'Z tranzaksiyasida saqlanadi: 90 kunlik backfillni bitta ulkan
            // SaveChanges bilan yozish oxirida yiqilsa, hamma narsa yo'qolardi.
            total += await UpsertInsightsAsync(acc, rows, ct);
            await db.SaveChangesAsync(ct);

            // ⚠️ Kvota har javobdan o'qiladi. Chegaraga yaqinlashsak — o'z ixtiyorimiz bilan
            // to'xtaymiz (qolgan bo'laklar ertangi ishga qoladi va upsert tufayli yo'qolmaydi).
            var quota = QuotaError(api.LastRateLimit);
            if (quota.Length > 0 && queue.First is not null)
                return await FailAsync(meta, acc, quota, total, ct);
        }

        // ── 4) MUVAFFAQIYAT ──
        acc.LastSyncAt = AppClock.Iso();
        // ⚠️ Kvota juda band bo'lsa xato emas, lekin OGOHLANTIRISH sifatida ko'rinib tursin:
        // ertangi sinxronizatsiya rad etilishi mumkinligini admin oldindan bilsin.
        acc.LastError = QuotaWarning(api.LastRateLimit);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Meta Ads Insights sinxronizatsiyasi tugadi: {Rows} qator ({Since} … {Until})",
            total, Fmt(since), Fmt(until));

        return (true, total, "");
    }

    /// <summary>
    /// Xatoni akkauntga yozadi va (kerak bo'lsa) adminlarga signal yuboradi.
    ///
    /// <para>⚠️ <see cref="IgAdAccount.LastSyncAt"/> YANGILANMAYDI. Ya'ni backfill yarmida
    /// yiqilgan bo'lsa ertaga u yana BOSHIDAN boshlanadi. Bu ataylab: upsert takroriy yuklashni
    /// zararsiz qiladi, "yarim yuklangan tarix" esa hisobotda jimgina teshik qoldirardi.</para>
    /// </summary>
    private async Task<(bool Ok, int Rows, string Error)> FailAsync(
        CenterMeta meta, IgAdAccount acc, string error, int rows, CancellationToken ct)
    {
        var text = InstagramContract.Trim(error, ErrorMax);
        var changed = !string.Equals(acc.LastError, text, StringComparison.Ordinal);

        acc.LastError = text;
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Meta Ads Insights sinxronizatsiyasi to'xtadi: {Error}", text);

        // ⚠️ Signal FAQAT tuzatib bo'ladigan (token/ruxsat) xatoda va FAQAT xato o'zgarganda:
        // kvota xatosi o'zi tiklanadi, bir xil matnni har kuni yuborish esa signalni shovqinga
        // aylantirib, keyingi safar hech kim o'qimaydigan qilib qo'yardi.
        if (changed && Classify(text) == SyncFailure.Fatal)
            await NotifyAdminsAsync(meta, "⚠️ Reklama statistikasi yangilanmadi: " + text, ct);

        return (false, rows, text);
    }

    /* ═════════════════════════ Upsert ═════════════════════════ */

    /// <summary>
    /// Iyerarxiyani UPSERT qiladi (<see cref="IgAdEntity.ExternalId"/> — unikal kalit).
    /// Reklama Meta'da o'chirilgan bo'lsa ham qator QOLADI: o'tgan oyning hisoboti buzilmasin.
    /// </summary>
    private async Task<int> UpsertEntitiesAsync(IgAdAccount acc, List<MetaAdEntityRow> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.ExternalId).Where(v => v.Length > 0).Distinct().ToList();
        if (ids.Count == 0) return 0;

        // ⚠️ Akkaunt bo'yicha EMAS, AYNAN `ExternalId` bo'yicha o'qiladi — unikal indeks ham
        // shunday. Akkaunt qayta ulanganda (id tuzatilganda) eski qatorni ko'rmay qolsak,
        // saqlashda unikal indeks buzilardi.
        var existing = await db.IgAdEntities
            .Where(e => ids.Contains(e.ExternalId))
            .ToDictionaryAsync(e => e.ExternalId, ct);

        var now = AppClock.Iso();
        var n = 0;

        foreach (var r in rows)
        {
            if (r.ExternalId.Length == 0) continue;

            if (!existing.TryGetValue(r.ExternalId, out var e))
            {
                e = new IgAdEntity { ExternalId = r.ExternalId };
                db.IgAdEntities.Add(e);
                // ⚠️ Xotiradagi xaritaga ham qo'shiladi: bitta javobda bir id ikki marta kelsa
                // (Meta sahifalashda takrorlashi mumkin) ikkinchi nusxa YARATILMAYDI.
                existing[r.ExternalId] = e;
            }

            e.AdAccountId = acc.AdAccountId;
            e.Level = r.Level;
            e.ParentId = r.ParentId;
            e.Name = r.Name;
            e.Status = r.Status;
            e.EffectiveStatus = r.EffectiveStatus;
            e.Objective = r.Objective;
            e.DailyBudgetMinor = r.DailyBudgetMinor;
            e.LifetimeBudgetMinor = r.LifetimeBudgetMinor;
            e.StartTime = r.StartTime;
            e.StopTime = r.StopTime;
            e.CreativeStoryId = r.CreativeStoryId;
            e.SyncedAt = now;
            n++;
        }

        return n;
    }

    /// <summary>
    /// Kunlik faktlarni UPSERT qiladi. Kalit — <c>(Level, ExternalId, StatDate, Platform)</c>,
    /// AYNAN bazadagi unikal indeks bilan bir xil.
    ///
    /// <para>⚠️ Kalitga <c>AdAccountId</c> KIRMAYDI (indeksda ham yo'q) — shu sabab mavjud
    /// qatorlar ham akkauntga qaramasdan qidiriladi, aks holda takroriy yuklash unikal indeksni
    /// buzardi.</para>
    /// </summary>
    private async Task<int> UpsertInsightsAsync(IgAdAccount acc, List<MetaInsightRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return 0;

        var dates = rows.Select(r => r.StatDate).Distinct().ToList();

        var existing = await db.IgAdInsights
            .Where(i => dates.Contains(i.StatDate))
            .ToListAsync(ct);

        var map = new Dictionary<string, IgAdInsight>(StringComparer.Ordinal);
        foreach (var i in existing) map[Key(i.Level, i.ExternalId, i.StatDate, i.Platform)] = i;

        var now = AppClock.Iso();
        var n = 0;

        foreach (var r in rows)
        {
            var key = Key(r.Level, r.ExternalId, r.StatDate, r.Platform);

            if (!map.TryGetValue(key, out var row))
            {
                row = new IgAdInsight
                {
                    Level = r.Level,
                    ExternalId = r.ExternalId,
                    StatDate = r.StatDate,
                    Platform = r.Platform,
                };
                db.IgAdInsights.Add(row);
                map[key] = row;
            }

            row.AdAccountId = acc.AdAccountId;
            row.Impressions = r.Impressions;
            row.Reach = r.Reach;
            row.Clicks = r.Clicks;
            row.LinkClicks = r.LinkClicks;
            row.SpendMinor = r.SpendMinor;
            row.LeadsOnsite = r.LeadsOnsite;
            row.LeadsPixel = r.LeadsPixel;
            row.MsgStarted = r.MsgStarted;
            row.ActionsJson = r.ActionsJson;
            row.AttributionSetting = r.AttributionSetting;
            row.FetchedAt = now;
            n++;
        }

        return n;
    }

    private static string Key(string level, string externalId, string statDate, string platform) =>
        level + "\n" + externalId + "\n" + statDate + "\n" + platform;

    /// <summary>Akkaunt ma'lumotini entityga ko'chiradi (ulash va sinxronizatsiya ikkalasida
    /// bir xil bo'lsin — offset ikki joyda ayri hisoblanib ketmasin).</summary>
    public static void ApplyAccountInfo(IgAdAccount acc, MetaAdAccountInfo info)
    {
        acc.Name = info.Name;
        acc.Currency = info.Currency;

        // ⚠️ Offset QAYTA HISOBLANMAYDI: `info` ni bergan `FetchAccountAsync` allaqachon
        // "Meta berdimi yoki jadvaldan olindimi" savolini hal qilgan (§17.3). Bu yerda yana
        // `MetaCurrency.OffsetOf` chaqirilsa Meta bergan qiymat JIMGINA yo'q qilinardi —
        // ya'ni butun ish vaqtidagi aniqlashning ma'nosi qolmasdi.
        // `Clamp` — bazadagi/kutilmagan buzuq qiymatdan himoya (0 esa HAQIQIY offset: JPY).
        acc.CurrencyOffset = MetaCurrency.Clamp(info.CurrencyOffset);
        acc.TimezoneName = info.TimezoneName;
    }

    /// <summary>
    /// Bazadagi offset QAYSI manbadan ekanini aytadi (<see cref="MetaOffsetSource"/>) —
    /// Sozlamalar ekranidagi "offset qayerdan olindi" ma'lumoti uchun.
    ///
    /// <para><b>⚠️ Nega HISOBLANADI, saqlanmaydi:</b> <c>IgAdAccount</c> ga yangi ustun
    /// qo'shish migratsiya talab qilardi, offsetning O'ZI esa bazada allaqachon bor. Bizning
    /// jadvalimizdan FARQ qiladigan qiymatni faqat Meta bergan bo'lishi mumkin — ya'ni farq
    /// bor bo'lsa manba ANIQ ("meta").</para>
    ///
    /// <para>⚠️ Qiymatlar TENG bo'lganda "jadval" deyiladi: bunda Meta bergan-bermagani
    /// AMALIY farq qilmaydi (raqam bir xil), ya'ni admin ko'radigan summa ikkala holatda ham
    /// aynan bir xil hisoblanadi. Yolg'on "aniqlik" ko'rsatgandan ko'ra kamtar javob to'g'ri.</para>
    /// </summary>
    public static string OffsetSourceOf(string? currency, int storedOffset) =>
        MetaCurrency.Clamp(storedOffset) == MetaCurrency.OffsetOf(currency)
            ? MetaOffsetSource.Table
            : MetaOffsetSource.Meta;

    /* ═════════════════════════ Sana va zona ═════════════════════════ */

    /// <summary>
    /// AKKAUNT vaqt zonasidagi bugungi kun.
    ///
    /// <para><b>⚠️ Nega <see cref="AppClock.Today"/> emas?</b> <c>AppClock</c> — Toshkent kuni
    /// (markazning kuni), Meta esa statistikani REKLAMA AKKAUNTI zonasida kesadi
    /// (<see cref="IgAdAccount.TimezoneName"/>, masalan <c>America/Los_Angeles</c>). Ikkalasi
    /// aralashsa "kechagi sarf 0" holati chiqadi: Toshkentda kun allaqachon almashgan,
    /// akkaunt zonasida esa hali kechagi kun davom etyapti.</para>
    ///
    /// <para>Zona nomi bo'sh yoki serverda tanilmasa (tzdata yo'q konteyner) — Toshkent kuniga
    /// qaytamiz: bir kunlik siljish ehtimoli bor, lekin sinxronizatsiya UMUMAN ishlamay
    /// qolgandan yaxshiroq. Chegaraviy kunlar baribir qayta yuklanadi
    /// (<see cref="ReloadDays"/>).</para>
    /// </summary>
    public static DateOnly TodayInAccountZone(string? timezoneName)
    {
        var name = (timezoneName ?? "").Trim();
        if (name.Length == 0) return AppClock.Today;

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(name);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        }
        catch (TimeZoneNotFoundException) { return AppClock.Today; }
        catch (InvalidTimeZoneException) { return AppClock.Today; }
    }

    /// <summary>Oraliqni <paramref name="days"/> kunlik bo'laklarga ajratadi (chegaralar
    /// KIRADI). Bo'laklar tartibi — eskisidan yangisiga.</summary>
    public static List<(DateOnly From, DateOnly To)> Chunks(DateOnly since, DateOnly until, int days)
    {
        var list = new List<(DateOnly, DateOnly)>();
        if (since > until) return list;

        var step = Math.Max(1, days);
        var cursor = since;

        while (cursor <= until)
        {
            var end = cursor.AddDays(step - 1);
            if (end > until) end = until;
            list.Add((cursor, end));
            cursor = end.AddDays(1);
        }

        return list;
    }

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(string? v, out DateOnly d) =>
        DateOnly.TryParseExact((v ?? "").Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    /* ═════════════════════════ Kvota va xato turlari ═════════════════════════ */

    /// <summary>Sinxronizatsiyani to'xtatgan xatoning TURI — undan keyin nima qilish kerakligini
    /// belgilaydi.</summary>
    internal enum SyncFailure
    {
        /// <summary>To'xtaymiz va keyingi ishga qoldiramiz (kvota, tarmoq, noma'lum xato).</summary>
        Stop,

        /// <summary>Oraliq katta — ikkiga bo'linadi (kutish yordam bermaydi).</summary>
        Shrink,

        /// <summary>Odam aralashuvi kerak: token yoki ruxsat. Signal yuboriladi.</summary>
        Fatal,
    }

    /// <summary>
    /// Xato MATNIDAN turini aniqlaydi.
    ///
    /// <para><b>⚠️ Nega matn bo'yicha?</b> <see cref="MetaInsightsApi"/> tashqariga Meta
    /// KODINI (190/100/80000) chiqarmaydi — u faqat foydalanuvchi o'qiydigan o'zbekcha matn
    /// qaytaradi va bu ataylab (xato matni bitta joyda tarjima qilinsin). Sinxronizatorga esa
    /// faqat UCH xil qaror kerak, shuning uchun kodni oshkor qilish o'rniga o'sha matnlardagi
    /// BARQAROR bo'laklar tekshiriladi. Matn o'zgarsa eng yomoni "Stop" bo'ladi — ya'ni
    /// xavfsiz tomonga (qayta urinmaymiz).</para>
    /// </summary>
    internal static SyncFailure Classify(string? error) => Classify(0, 0, error);

    /// <summary>
    /// Xato TURI — avval Meta KODI bo'yicha, kod bo'lmasa matn bo'yicha.
    ///
    /// <para><b>Nega kod birinchi:</b> matn <c>MetaInsightsApi.MapError</c> da yozilgan va u
    /// foydalanuvchiga ko'rsatiladigan jumla — ya'ni uni tahrirlash NORMAL ish. Agar qaror
    /// faqat matnga tayansa, bitta so'z o'zgarishi bilan backfill hech qachon BO'LINMAY
    /// qolardi (va sabab hech qayerda ko'rinmasdi). Kod esa Meta bilan shartnoma.</para>
    ///
    /// <para>⚠️ Matn baribir zaxira: tarmoq uzilishi/timeout'da Meta kodi umuman bo'lmaydi (0).
    /// Noma'lum kodda ham matnga tushiladi, u ham tanimasa — <see cref="SyncFailure.Stop"/>,
    /// ya'ni XAVFSIZ tomon (kutamiz, davom etib blokni uzaytirmaymiz).</para>
    /// </summary>
    /// <param name="code">`error.code` (`MetaInsightsApi.LastErrorCode`), noma'lum bo'lsa 0.</param>
    /// <param name="subcode">`error.error_subcode`, noma'lum bo'lsa 0.</param>
    internal static SyncFailure Classify(int code, int subcode, string? error)
    {
        // ── 1) KOD bo'yicha (barqaror shartnoma) ──
        switch (code)
        {
            // Juda ko'p ma'lumot so'ralgan — backoff YORDAM BERMAYDI, oraliq qisqartiriladi.
            case 100 when subcode == 1487534:
                return SyncFailure.Shrink;
            // Token yaroqsiz / ruxsat yetishmaydi — odam aralashuvi kerak, qayta urinish behuda.
            case 190:
            case 200:
            case 10:
            case 299:
                return SyncFailure.Fatal;
            // ads_insights BUC limiti va app/user/custom limitlar — TO'XTAYMIZ.
            // Meta ochiq aytadi: limitga yetganda davom etilsa blok UZAYADI.
            case 80000:
            case 80004:
            case 4:
            case 17:
            case 32:
            case 613:
                return SyncFailure.Stop;
        }

        // ── 2) MATN bo'yicha (zaxira — kod yo'q yoki noma'lum) ──
        var e = (error ?? "").ToLowerInvariant();
        if (e.Length == 0) return SyncFailure.Stop;

        // "…sana oralig'ini qisqartiring" — 100/1487534 va sahifa chegarasi.
        if (e.Contains("qisqartiring")) return SyncFailure.Shrink;

        // "Meta tokeni yaroqsiz…" / "Ruxsat yetishmaydi…" — 190 / 200 / 10.
        if (e.Contains("token") || e.Contains("ruxsat")) return SyncFailure.Fatal;

        return SyncFailure.Stop;
    }

    /// <summary>Kvota TUGAGANDA (yoki tugashiga oz qolganda) — to'xtatuvchi xato matni.
    /// Bo'sh satr = davom etish mumkin.</summary>
    private static string QuotaError(MetaRateLimitInfo? rl)
    {
        if (rl is null) return "";
        var worst = Math.Max(Math.Max(rl.AppUtilPct, rl.AccountUtilPct), rl.CallCountPct);
        if (worst < QuotaStopPct) return "";

        var wait = rl.RegainMinutes > 0
            ? $" Taxminan {rl.RegainMinutes} daqiqadan keyin davom etadi."
            : " Keyingi sinxronizatsiyada davom etadi.";

        return $"Meta so'rovlar chegarasiga yetildi ({worst}%) — yuklash to'xtatildi." + wait;
    }

    /// <summary>Muvaffaqiyatli tugaganda ham kvota band bo'lsa — OGOHLANTIRISH (xato emas).</summary>
    private static string QuotaWarning(MetaRateLimitInfo? rl)
    {
        if (rl is null) return "";
        var worst = Math.Max(Math.Max(rl.AppUtilPct, rl.AccountUtilPct), rl.CallCountPct);
        return worst >= 80
            ? $"⚠️ {MetaInsightsParser.ThrottleSummary(rl)} — keyingi yuklash chegaraga urilishi mumkin."
            : "";
    }

    /* ═════════════════════════ Telegram signali ═════════════════════════ */

    /// <summary>
    /// Adminlarga signal (naqsh: <see cref="InstagramPipeline.NotifyAdminsAsync"/>).
    ///
    /// <para><b>⚠️ Nega o'sha metod QAYTA ISHLATILMADI?</b> U <c>InstagramEnabled</c> (AI
    /// agenti) bayrog'i bilan darvozalangan, reklama statistikasi esa undan MUSTAQIL modul:
    /// agent o'chirilgan markazda signal jimgina yuborilmay qolardi va "token muddati tugagan"
    /// haqida hech kim bilmasdi. Shu sabab darvoza shu yerda —
    /// <c>InstagramAdsStatsEnabled</c> (yuqorida allaqachon tekshirilgan) +
    /// <c>InstagramNotifyTelegram</c>.</para>
    ///
    /// <para>Xatosi JIM yutiladi (<c>LeadNotifier</c> siyosati): xabarnoma sinxronizatsiya
    /// natijasini o'zgartirmasin.</para>
    /// </summary>
    private async Task NotifyAdminsAsync(CenterMeta meta, string text, CancellationToken ct)
    {
        try
        {
            if (!meta.InstagramNotifyTelegram || !telegram.IsConfigured) return;

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
                if (!sent.Add(r.ChatId)) continue;      // bir chatga bir marta
                await telegram.SendMessageAsync(r.ChatId, text, ct: ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reklama statistikasi signali yuborilmadi (e'tiborsiz qoldirildi)");
        }
    }
}
