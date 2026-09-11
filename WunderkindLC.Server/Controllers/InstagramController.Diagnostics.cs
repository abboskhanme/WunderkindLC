using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → SOZLAMALAR: <b>«Meta bilan aloqani tekshirish»</b>.
///
/// <para><b>Muammo.</b> Marketing kengaytirishidagi to'rtta modul Meta API bilan ishlaydi
/// (izoh/DM, reklama lidlari, reklama statistikasi, kontent joylash) va sozlamalar
/// saqlangandan keyin admin "ishladimi yoki yo'qmi" ni faqat BIR NECHA KUN KUTIB bilardi:
/// lid kelmasa, post yiqilsa, statistika bo'sh chiqsa. Meta tomonidagi nosozliklar esa
/// bir xil ko'rinadi ("hech narsa kelmayapti"), sabablari har xil: token yaroqsiz ·
/// ruxsat yetishmaydi · obuna qilinmagan · App Review o'tmagan · id noto'g'ri.</para>
///
/// <para><b>Yechim.</b> Bitta tugma — har yoqilgan modul uchun ENG YENGIL O'QISH so'rovi va
/// har biri bo'yicha aniq javob: nima bo'ldi va NIMA QILISH kerak.</para>
///
/// <para><b>🔴 Yangi HTTP kod YOZILMAGAN.</b> Barcha so'rovlar mavjud mijozlar orqali
/// (<see cref="InstagramApi"/>, <see cref="MetaAdsApi"/>, <see cref="MetaInsightsApi"/>,
/// <see cref="InstagramPublishApi"/>) — ular allaqachon Meta xato kodini o'zbekcha matnga
/// aylantiradi, retry va throttle qoidalari ham o'sha yerda. Diagnostika ularning ustiga
/// faqat "nima qilish kerak" maslahatini qo'shadi
/// (<see cref="IgDiagnostics"/> — sof funksiyalar, testlangan).</para>
///
/// <para><b>⚠️ CAPI AYRI:</b> uni haqiqatan sinash Meta'ga HODISA yuborishni talab qiladi,
/// hodisa esa Events Manager statistikasiga tushib qoladi va uni qaytarib bo'lmaydi. Ya'ni
/// "tekshiruv" ma'lumotni o'zgartirib yuborardi. Shuning uchun CAPI'da faqat sozlama
/// to'liqligi ko'riladi va bu javobda OCHIQ yoziladi — sinalmagan holatni "ishlayapti" deb
/// ko'rsatish eng yomon variant bo'lardi.</para>
///
/// <para><b>🔴 MAXFIYLIK:</b> javobda token, secret yoki Dataset ID QIYMATI yo'q — faqat
/// holat, ochiq identifikatorlar (Page nomi, akkaunt nomi) va maslahat.</para>
///
/// <para><b>Audit:</b> yozilmaydi — tekshiruv hech narsani o'zgartirmaydi
/// (<c>POST {id}/ai-analysis</c> bilan bir xil mantiq, <c>.claude/rules/audit.md</c> §3.5).</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>
    /// Bitta tekshiruvga ajratilgan vaqt.
    ///
    /// <para><b>Nega kerak:</b> mijozlarda 3 martagacha qayta urinish (1s → 2s → 4s) va
    /// <c>HttpClient</c>ning standart 100 soniyalik timeout'i bor — Meta "osilib" qolsa
    /// admin tugmani bosib, bir necha daqiqa kutib turardi. Bu yerdagi chegara qolgan
    /// modullarning tekshirilishini kafolatlaydi.</para>
    /// </summary>
    private static readonly TimeSpan DiagTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Timeout ishlaganda ko'rsatiladigan matn (mijozning "so'rov bekor qilindi"
    /// matni bu yerda chalg'itardi — bekor qilgan biz edik).</summary>
    private const string DiagTimeoutText = "Meta belgilangan vaqtda javob bermadi (20 soniya).";

    // =============================================================================================
    //  TEKSHIRUV
    // =============================================================================================

    /// <summary>
    /// META BILAN ALOQANI TEKSHIRISH — har modul uchun bitta yengil o'qish so'rovi.
    ///
    /// <para><b>Ruxsat <c>marketing.settings</c>:</b> GET emas, POST — chunki amal TASHQI
    /// so'rov yuboradi (Meta rate-limitini yeydi). Sinf darajasidagi "xodimga o'qish ochiq"
    /// yumshatishi bu yerda ishlamasligi kerak.</para>
    ///
    /// <para><b>⚠️ Natija SAQLANMAYDI</b> — har bosishda yangisi. Sabab: "oxirgi tekshiruv
    /// yashil edi" degan eski yozuv adminni chalg'itardi, holat esa har daqiqada o'zgarishi
    /// mumkin (token muddati tugaydi, ruxsat olib qo'yiladi).</para>
    ///
    /// <para><b>⚠️ Tekshiruvlar KETMA-KET</b> va har biri ALOHIDA <c>try/catch</c> ichida:
    /// (1) biri yiqilsa qolganlari baribir tekshiriladi; (2) <c>DbContext</c> parallel
    /// ishlashga yaroqsiz; (3) <see cref="InstagramApi"/> da butun ilova bo'yicha yagona
    /// throttle bor — parallel yuborish baribir navbatda kutardi.</para>
    /// </summary>
    [HttpPost("diagnostics/check")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgDiagResultDto>> DiagnosticsCheck(
        [FromServices] MetaInsightsApi insightsApi,
        [FromServices] InstagramPublishApi publishApi,
        CancellationToken ct)
    {
        // ⚠️ Bazadan hammasi OLDINDAN o'qiladi: tashqi so'rovlar davomida `DbContext` band
        // bo'lib turmasin (va tekshiruv o'rtasida sozlama o'zgarsa, natija bir xil suratdan
        // hisoblansin).
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var acc = await db.IgAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        var page = await db.IgAdPages.AsNoTracking().FirstOrDefaultAsync(p => p.IsActive, ct);
        var adAcc = await db.IgAdAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);

        var accId = acc?.IgUserId ?? "";
        var accToken = acc?.AccessToken ?? "";

        var items = new List<IgDiagItemDto>
        {
            // ── 1) Instagram akkaunt (izoh va DM) ──
            await ProbeAsync(
                IgDiagnostics.KeyAccount,
                enabled: meta?.InstagramEnabled ?? false,
                hasId: accId.Length > 0, hasToken: accToken.Length > 0,
                probe: c => CheckAccountAsync(acc!, c), ct),

            // ── 2) Reklama lidlari ──
            await ProbeAsync(
                IgDiagnostics.KeyAdLeads,
                enabled: meta?.InstagramLeadAdsEnabled ?? false,
                hasId: (page?.PageId ?? "").Length > 0, hasToken: (page?.AccessToken ?? "").Length > 0,
                probe: c => CheckAdLeadsAsync(page!, c), ct),

            // ── 3) Reklama statistikasi ──
            await ProbeAsync(
                IgDiagnostics.KeyAdsStats,
                enabled: meta?.InstagramAdsStatsEnabled ?? false,
                hasId: (adAcc?.AdAccountId ?? "").Length > 0, hasToken: (adAcc?.AccessToken ?? "").Length > 0,
                probe: c => CheckAdsStatsAsync(insightsApi, adAcc!, c), ct),

            // ── 4) Kontent joylash (o'sha Instagram tokeni + `content_publish` ruxsati) ──
            await ProbeAsync(
                IgDiagnostics.KeyContent,
                enabled: meta?.InstagramPublishEnabled ?? false,
                hasId: accId.Length > 0, hasToken: accToken.Length > 0,
                probe: c => CheckContentAsync(publishApi, accId, accToken, c), ct),

            // ── 5) CAPI — TASHQI SO'ROV YO'Q (yuqoridagi izoh) ──
            CheckCapi(meta),
        };

        return new IgDiagResultDto(
            CheckedAt: AppClock.Iso(),
            Total: items.Count,
            // ⚠️ Sanoqlar FAQAT haqiqatan tekshirilganlar bo'yicha: CAPI va o'chirilgan modullar
            // "muvaffaqiyat" ham, "nosozlik" ham emas — aks holda jamlanma yolg'on chiqardi.
            OkCount: items.Count(i => i.Checked && i.Ok),
            FailCount: items.Count(i => i.Checked && !i.Ok),
            SkippedCount: items.Count(i => !i.Checked),
            Items: items);
    }

    // =============================================================================================
    //  MODULLAR BO'YICHA TEKSHIRUVLAR (har biri — bitta ENG YENGIL O'QISH)
    // =============================================================================================

    /// <summary>
    /// Instagram akkaunt: <c>GET /me?fields=id,user_id,username,…</c>
    /// (<see cref="InstagramApi.MeAsync"/>).
    ///
    /// <para><b>Nega <c>/me</c>, <c>/{ig-user-id}</c> emas:</b> mavjud mijozda AYNAN shu o'qish
    /// bor, ya'ni yangi HTTP kod yozish shart emas. Tugun ham, token ham o'sha — lekin
    /// <c>/me</c> qo'shimcha foyda beradi: javobdagi id bazadagi bilan solishtiriladi va
    /// <b>"token boshqa akkauntniki"</b> holati topiladi (bu xato aks holda "bot javob
    /// bermayapti" bo'lib ko'rinardi).</para>
    ///
    /// <para>⚠️ Token ishlagani YETMAYDI: <c>subscribed_apps</c> obunasi bo'lmasa Meta
    /// hodisani UMUMAN yubormaydi. Shuning uchun obuna <b>Meta'dan JONLI</b> o'qiladi
    /// (<see cref="InstagramApi.GetSubscribedFieldsAsync"/>) va bazadagi
    /// <c>WebhookSubscribed</c> bayrog'iga ishonilmaydi — u ulanish paytidagi suratcha va
    /// eskirishi mumkin. Kerakli maydon yetishmasa AYNAN qaysi biri yozib ko'rsatiladi.</para>
    /// </summary>
    private async Task<IgDiagProbe> CheckAccountAsync(IgAccount acc, CancellationToken c)
    {
        var (ok, igUserId, appScoped, username, _, _, err) = await api.MeAsync(acc.AccessToken, c);
        if (!ok) return IgDiagProbe.Fail(err);

        // Token boshqa akkauntniki bo'lsa — id'lar mos kelmaydi. Eski qatorlarda `AppScopedUserId`
        // bo'sh bo'lishi mumkin, shuning uchun ikkala id ham solishtiriladi.
        var known = igUserId == acc.IgUserId || igUserId == acc.AppScopedUserId
                    || (appScoped.Length > 0 && (appScoped == acc.IgUserId || appScoped == acc.AppScopedUserId));
        if (!known)
            return IgDiagProbe.Fail(
                $"Token BOSHQA akkauntniki: Meta @{Or(username, "—")} akkauntini qaytardi.",
                "Akkauntni uzib, kerakli Instagram profili bilan qaytadan ulang.");

        // ── OBUNA — META'DAN JONLI (bazadagi bayroqqa ISHONILMAYDI) ──
        //
        // 🔴 `acc.WebhookSubscribed` — ULANISH PAYTIDAGI suratcha. U keyin eskiradi: Meta maydon
        // nomini olib tashlashi, admin Dashboard'dan belgini olib qo'yishi mumkin. 2026-08-22 da
        // prodda aynan shu holat topildi — bazada "obuna bor" turardi, Meta'da esa faqat
        // `messages` obunasi bor edi va IZOHLAR UMUMAN KELMASDI. Ya'ni diagnostika yashil
        // ko'rsatib, muammoni yashirardi.
        var (subOk, fields, subErr) = await api.GetSubscribedFieldsAsync(acc.AccessToken, c);
        if (!subOk)
            return IgDiagProbe.Fail(
                $"Token ishlayapti (@{Or(username, acc.Username)}), lekin webhook obunasini "
                + $"tekshirib bo'lmadi: {subErr}",
                "Birozdan keyin qaytadan urinib ko'ring — bu vaqtinchalik xato bo'lishi mumkin.");

        var missing = InstagramContract.MissingWebhookFields(fields);
        if (missing.Count > 0)
            return IgDiagProbe.Fail(
                $"Token ishlayapti (@{Or(username, acc.Username)}), lekin webhook obunasida "
                + $"YETISHMAYDI: {string.Join(", ", missing)}"
                + (fields.Count > 0 ? $" (hozir obuna: {string.Join(", ", fields)})" : " (obuna umuman yo'q)")
                + ". Bu maydonlar bo'yicha hodisa UMUMAN kelmaydi.",
                "Marketing → Sozlamalar → «Instagram'ni ulash» bilan akkauntni QAYTA ULANG — "
                + "obuna aynan ulanish paytida qilinadi.");

        return IgDiagProbe.Pass(
            $"Aloqa bor — @{Or(username, acc.Username)} (obuna: {string.Join(", ", fields)}).");
    }

    /// <summary>
    /// Reklama lidlari: <c>GET /{page-id}?fields=id,name</c>
    /// (<see cref="MetaAdsApi.FetchPageAsync"/>) — mijozda tokenni "to'g'ri sahifaniki"ligini
    /// tekshirish uchun allaqachon bor eng yengil so'rov.
    ///
    /// <para>⚠️ Sahifa nomi kelgani ham YETMAYDI: <c>leadgen</c> obunasisiz Meta hodisani
    /// yubormaydi va nosozlik "reklama ishlayapti, lid kelmayapti" bo'lib ko'rinadi.</para>
    /// </summary>
    private async Task<IgDiagProbe> CheckAdLeadsAsync(IgAdPage page, CancellationToken c)
    {
        var (ok, pageName, err) = await adsApi.FetchPageAsync(page.PageId, page.AccessToken, c);
        if (!ok) return IgDiagProbe.Fail(err);

        if (!page.LeadgenSubscribed)
            return IgDiagProbe.Fail(
                $"Token ishlayapti (sahifa «{Or(pageName, page.PageName)}»), lekin `leadgen` "
                + "obunasi yo'q — lidlar kelmaydi.",
                "«Reklama lidlari» kartasida sozlamani qayta saqlang — obuna saqlash paytida qilinadi.");

        return IgDiagProbe.Pass(
            $"Aloqa bor — sahifa «{Or(pageName, page.PageName)}» (`leadgen` obunasi faol).");
    }

    /// <summary>
    /// Reklama statistikasi: <c>GET /act_{id}?fields=name,currency,…</c>
    /// (<see cref="MetaInsightsApi.FetchAccountAsync"/>).
    ///
    /// <para>⚠️ Xato TURI matndan emas, <see cref="MetaInsightsApi.LastErrorCode"/> dan
    /// aniqlanadi — bu mijoz Meta kodini ATAYIN tashqariga chiqaradi va kod matndan
    /// barqarorroq shartnoma.</para>
    /// </summary>
    private static async Task<IgDiagProbe> CheckAdsStatsAsync(
        MetaInsightsApi insightsApi, IgAdAccount adAcc, CancellationToken c)
    {
        var (ok, info, err) = await insightsApi.FetchAccountAsync(adAcc.AdAccountId, adAcc.AccessToken, c);
        if (!ok || info is null) return IgDiagProbe.Fail(err, metaCode: insightsApi.LastErrorCode);

        var cur = info.Currency.Length > 0 ? info.Currency : "noma'lum valyuta";
        return IgDiagProbe.Pass($"Aloqa bor — «{Or(info.Name, adAcc.AdAccountId)}», {cur}.");
    }

    /// <summary>
    /// Kontent joylash: <c>GET /{ig-user-id}/content_publishing_limit</c>
    /// (<see cref="InstagramPublishApi.GetPublishingLimitAsync"/>).
    ///
    /// <para><b>Nega aynan shu:</b> u YAGONA o'qish so'rovi, qaysiki <c>content_publish</c>
    /// ruxsatini talab qiladi. Oddiy profil o'qishi ruxsatsiz ham ishlayverardi, ya'ni
    /// "yashil" ko'rsatib, birinchi post paytida yiqilardi.</para>
    ///
    /// <para>Limit 0 (<c>UnknownQuota</c>) bo'lishi ODATIY hol — Meta ba'zan
    /// <c>quota_total</c> bermaydi, bu nosozlik EMAS.</para>
    /// </summary>
    private static async Task<IgDiagProbe> CheckContentAsync(
        InstagramPublishApi publishApi, string igUserId, string token, CancellationToken c)
    {
        var (ok, usage, total, err) = await publishApi.GetPublishingLimitAsync(igUserId, token, c);
        if (!ok) return IgDiagProbe.Fail(err);

        var quota = total > IgPublishConst.UnknownQuota
            ? $"24 soatda {usage}/{total} post ishlatilgan"
            : $"24 soatda {usage} post ishlatilgan (limitni Meta bermadi)";
        return IgDiagProbe.Pass($"Aloqa bor va kontent joylash ruxsati mavjud — {quota}.");
    }

    /// <summary>
    /// CAPI — <b>SOZLAMA TEKSHIRUVI, ALOQA SINOVI EMAS</b>.
    ///
    /// <para>🔴 Bu yerdan Meta'ga hech narsa yuborilmaydi (sinf izohidagi sabab), shuning
    /// uchun <c>Checked = false</c>. Frontend <c>checked</c> bo'yicha NEYTRAL belgi chizadi —
    /// sinalmagan modul hech qachon yashil "ishlayapti" bo'lib ko'rinmaydi.</para>
    /// </summary>
    private static IgDiagItemDto CheckCapi(CenterMeta? meta)
    {
        const string key = IgDiagnostics.KeyCapi;
        var label = IgDiagnostics.Label(key);

        if (!(meta?.InstagramCapiEnabled ?? false))
            return new IgDiagItemDto(key, label, false, false, false,
                IgDiagnostics.DisabledText, IgDiagnostics.DisabledHint);

        // ⚠️ Faqat "bor/yo'q" bayrog'i — Dataset ID ham, token ham javobga TUSHMAYDI.
        var hasId = meta!.InstagramCapiDatasetId.Trim().Length > 0;
        var hasToken = meta.InstagramCapiToken.Trim().Length > 0;

        var missing = IgDiagnostics.MissingText(key, hasId, hasToken);
        if (missing.Length > 0)
            return new IgDiagItemDto(key, label, true, false, false, missing, IgDiagnostics.MissingHint(key));

        return new IgDiagItemDto(key, label, true, false, true,
            IgDiagnostics.CapiNotProbedText, IgDiagnostics.CapiNotProbedHint);
    }

    // =============================================================================================
    //  UMUMIY QISM
    // =============================================================================================

    /// <summary>
    /// Bitta modulning to'liq sikli: <b>o'chiqmi → sozlanganmi → tekshirish</b>.
    ///
    /// <para>⚠️ <c>try/catch</c> AYNAN shu yerda: bitta modul kutilmagan istisno bilan yiqilsa
    /// ham qolganlarining natijasi baribir qaytadi (aks holda admin butunlay xato oynasini
    /// ko'rib, qaysi modul nosoz ekanini bilmasdi).</para>
    /// </summary>
    private async Task<IgDiagItemDto> ProbeAsync(
        string key, bool enabled, bool hasId, bool hasToken,
        Func<CancellationToken, Task<IgDiagProbe>> probe, CancellationToken ct)
    {
        var label = IgDiagnostics.Label(key);

        if (!enabled)
            return new IgDiagItemDto(key, label, false, false, false,
                IgDiagnostics.DisabledText, IgDiagnostics.DisabledHint);

        var missing = IgDiagnostics.MissingText(key, hasId, hasToken);
        if (missing.Length > 0)
            return new IgDiagItemDto(key, label, true, false, false, missing, IgDiagnostics.MissingHint(key));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DiagTimeout);

        try
        {
            var r = await probe(cts.Token);

            // Bizning timeout ishlagan bo'lsa — mijozning "so'rov bekor qilindi" matni
            // chalg'itardi (bekor qilgan biz edik, Meta emas).
            if (!r.Ok && cts.IsCancellationRequested && !ct.IsCancellationRequested)
                return Fail(key, label, DiagTimeoutText, IgDiagFault.Network);

            if (r.Ok) return new IgDiagItemDto(key, label, true, true, true, r.Message, r.Hint);

            var hint = r.Hint.Length > 0 ? r.Hint : IgDiagnostics.HintFor(key, r.Message, r.MetaCode);
            return new IgDiagItemDto(key, label, true, true, false, r.Message, hint);
        }
        // Foydalanuvchi sahifani yopgan bo'lsa — bu bizning nosozligimiz emas, yuqoriga o'tadi.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fail(key, label, DiagTimeoutText, IgDiagFault.Network);
        }
        catch (Exception ex)
        {
            // ⚠️ Istisno matni foydalanuvchiga KO'RSATILMAYDI: unda so'rov manzili, ya'ni
            // `access_token` bo'lishi mumkin (mijozlardagi bir xil qoida).
            logger.LogWarning(ex, "Instagram diagnostikasi: '{Key}' tekshiruvi kutilmagan xato bilan tugadi.", key);
            return Fail(key, label, "Tekshiruv bajarilmadi — kutilmagan xato (batafsili server jurnalida).",
                IgDiagFault.Unknown);
        }
    }

    private static IgDiagItemDto Fail(string key, string label, string message, IgDiagFault fault) =>
        new(key, label, true, true, false, message, IgDiagnostics.Hint(key, fault));

    /// <summary>Meta bergan qiymat bo'sh bo'lsa — bazadagisi (yoki tire).</summary>
    private static string Or(string a, string b) => a.Length > 0 ? a : (b.Length > 0 ? b : "—");
}

// =================================================================================================
//  DTO'LAR (prefiks `IgDiag` — boshqa partial qismlar bilan to'qnashmasin)
// =================================================================================================

/// <summary>
/// Bitta modulning tekshiruv natijasi.
///
/// <para><b>Uchta bayroq ATAYIN AYRI</b>, chunki uchta boshqa-boshqa savolga javob beradi:
/// <c>enabled</c> — "modul yoqilganmi", <c>checked</c> — "Meta'ga so'rov KETDIMI",
/// <c>ok</c> — "natija yaxshimi". Ularni bitta "status"ga qisqartirsak, CAPI'ning
/// «sozlangan, lekin sinalmagan» holati yo'qolib, yashil belgi bilan aralashib ketardi.</para>
/// </summary>
/// <param name="Key">Modul kaliti (<see cref="IgDiagnostics"/> konstantalari).</param>
/// <param name="Label">Ekrandagi nom (o'zbekcha).</param>
/// <param name="Enabled">Modul bayrog'i yoqilganmi.</param>
/// <param name="Checked">Meta'ga HAQIQATAN so'rov ketdimi. <c>false</c> — o'chirilgan,
/// sozlanmagan yoki (CAPI) ataylab sinalmagan.</param>
/// <param name="Ok">Natija yaxshimi. <c>Checked = false</c> bo'lganda bu "sozlama to'liqmi"
/// degani, "ishlayapti" degani EMAS.</param>
/// <param name="Message">Nima bo'lgani — o'zbekcha, tayyor jumla.</param>
/// <param name="Hint">NIMA QILISH kerak. Hammasi joyida bo'lsa — bo'sh.</param>
public record IgDiagItemDto(
    string Key, string Label, bool Enabled, bool Checked, bool Ok, string Message, string Hint);

/// <summary>Butun tekshiruvning natijasi. Saqlanmaydi — har bosishda yangisi.</summary>
/// <param name="CheckedAt">Tekshiruv vaqti (markaz mintaqasi, <c>AppClock</c>).</param>
/// <param name="Total">Modullar soni.</param>
/// <param name="OkCount">Tekshirilgan VA muvaffaqiyatli.</param>
/// <param name="FailCount">Tekshirilgan VA nosoz.</param>
/// <param name="SkippedCount">Umuman tekshirilmagan (o'chirilgan · sozlanmagan · CAPI).</param>
public record IgDiagResultDto(
    string CheckedAt, int Total, int OkCount, int FailCount, int SkippedCount,
    List<IgDiagItemDto> Items);

/// <summary>
/// Bitta tekshiruv funksiyasining ICHKI natijasi (javobga tushmaydi).
/// <para><see cref="Hint"/> bo'sh bo'lsa maslahat <see cref="IgDiagnostics.HintFor"/> orqali
/// xato matnidan/kodidan chiqariladi; to'ldirilgan bo'lsa — modulga xos maxsus holat
/// (masalan "obuna yo'q"), uni umumiy xarita bilan topib bo'lmasdi.</para>
/// </summary>
internal readonly record struct IgDiagProbe(bool Ok, string Message, string Hint, int MetaCode)
{
    public static IgDiagProbe Pass(string message) => new(true, message, "", 0);

    public static IgDiagProbe Fail(string message, string hint = "", int metaCode = 0) =>
        new(false, message, hint, metaCode);
}
