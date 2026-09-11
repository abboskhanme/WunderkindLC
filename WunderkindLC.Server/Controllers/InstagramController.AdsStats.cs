using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → REKLAMA STATISTIKASI (Meta Ads Insights) — ULANISH va SINXRONIZATSIYA
/// endpointlari.
///
/// <para><b>Nega alohida fayl?</b> <see cref="InstagramController"/> allaqachon oltita ekranga
/// xizmat qiladi; reklama statistikasi esa o'z tokeni (System User, <c>ads_read</c>), o'z
/// bayrog'i (<c>InstagramAdsStatsEnabled</c>) va o'z sinxronizatori bo'lgan MUSTAQIL modul —
/// shuning uchun u <c>partial</c> qismda turadi. Marshrut, ruxsat va audit siyosati esa
/// asosiy fayl bilan BIR XIL: sinf darajasidagi <c>[Authorize]</c> +
/// <c>[AdminPerm("marketing", ReadRequiresPerm = true)]</c> avtomatik qo'llanadi.</para>
///
/// <para><b>⚠️ MAXFIYLIK:</b> reklama akkauntining <c>AccessToken</c>i HECH QAYSI javobga
/// tushmaydi — tashqariga faqat <c>tokenSet</c> bayrog'i chiqadi. Auditga ham token
/// yozilmaydi (<c>audit.md</c> §1).</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>
    /// DIAGNOSTIKA — "nega statistika yo'q" savolining barcha sabablari bitta javobda: modul
    /// yoqilganmi, akkaunt ulanganmi, tokeni bormi, oxirgi marta qachon yangilangan, oxirgi
    /// xato nima edi va bazada qancha ma'lumot bor.
    ///
    /// <para><paramref name="ct"/> dan boshqa parametr yo'q — ekran ochilganda birinchi
    /// so'raladigan endpoint shu.</para>
    /// </summary>
    [HttpGet("adsstats/status")]
    public async Task<ActionResult<IgAdsStatusDto>> AdsStatsStatus(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);

        var acc = await db.IgAdAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        // ⚠️ Sanoqlar AKKAUNT bo'yicha filtrlanadi: akkaunt almashtirilganda eski akkauntning
        // qatorlari bazada QOLADI (tarix o'chirilmaydi) va ularni "yangi akkauntda 9000 qator
        // bor" deb ko'rsatish chalg'itardi.
        var act = acc?.AdAccountId ?? "";
        var insightRows = act.Length == 0 ? 0 : await db.IgAdInsights.CountAsync(i => i.AdAccountId == act, ct);
        var entityRows = act.Length == 0 ? 0 : await db.IgAdEntities.CountAsync(e => e.AdAccountId == act, ct);

        var lastStatDate = act.Length == 0
            ? ""
            : await db.IgAdInsights
                .Where(i => i.AdAccountId == act)
                .OrderByDescending(i => i.StatDate)
                .Select(i => i.StatDate)
                .FirstOrDefaultAsync(ct) ?? "";

        return new IgAdsStatusDto(
            Enabled: meta?.InstagramAdsStatsEnabled ?? false,
            Connected: acc is not null,
            AdAccountId: acc?.AdAccountId ?? "",
            Name: acc?.Name ?? "",
            Currency: acc?.Currency ?? "",
            CurrencyOffset: acc?.CurrencyOffset ?? MetaCurrency.DefaultOffset,
            // ⚠️ Manba HISOBLANADI (bazada ustun yo'q — migratsiya kerak emas): jadvalimizdan
            // farq qiladigan offsetni faqat Meta bergan bo'lishi mumkin.
            CurrencyOffsetSource: acc is null
                ? ""
                : MetaInsightsService.OffsetSourceOf(acc.Currency, acc.CurrencyOffset),
            TimezoneName: acc?.TimezoneName ?? "",
            TokenSet: !string.IsNullOrWhiteSpace(acc?.AccessToken),
            ConnectedAt: acc?.ConnectedAt ?? "",
            ConnectedBy: acc?.ConnectedBy ?? "",
            LastSyncAt: acc?.LastSyncAt ?? "",
            LastError: acc?.LastError ?? "",
            SyncHour: meta?.InstagramAdsSyncHour ?? 5,
            BackfillDays: meta?.InstagramAdsBackfillDays ?? 90,
            InsightRows: insightRows,
            EntityRows: entityRows,
            LastStatDate: lastStatDate);
    }

    /// <summary>
    /// Reklama akkauntini ULASH: <c>act_...</c> id va System User tokeni.
    ///
    /// <para><b>⚠️ SAQLASHDAN OLDIN TEKSHIRILADI</b> (<see cref="MetaInsightsApi.FetchAccountAsync"/>):
    /// token noto'g'ri bo'lsa yoki unda <c>ads_read</c> yo'q bo'lsa xato DARHOL ko'rinadi. Aks
    /// holda nosozlik "ulandi, lekin statistika kelmayapti" bo'lib bir haftadan keyin sezilardi.
    /// Tekshiruv o'tmasa <b>hech narsa saqlanmaydi</b>.</para>
    ///
    /// <para>Valyuta, VAQT ZONASI va offset shu tekshiruvdan olinadi — admin ularni qo'lda
    /// kiritmaydi. Offset avval Meta'dan so'raladi, u bermasa valyuta kodidan sof funksiya
    /// bilan hisoblanadi (<see cref="MetaInsightsApi.FetchAccountAsync"/>) — MANBA esa
    /// auditga ham, holat javobiga ham chiqadi.</para>
    ///
    /// <para>Token BO'SH kelsa mavjudi saqlanadi — forma tokenni ko'rsatmaydi, ya'ni faqat
    /// akkaunt id'sini tuzatish uchun uni qayta yozdirish shart emas (<c>ads/page</c> bilan
    /// bir xil naqsh).</para>
    /// </summary>
    [HttpPut("adsstats/account")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgAdsStatusDto>> SaveAdsAccount(
        IgAdsAccountPayload payload, [FromServices] MetaInsightsApi insightsApi, CancellationToken ct)
    {
        // "1234" ham, "act_1234" ham qabul qilinadi — bazada HAR DOIM prefiksli qiymat turadi
        // (aks holda bir akkaunt ikki xil satr bo'lib, unikal indeks ham yordam bermasdi).
        var act = MetaInsightsParser.NormalizeAccountId(payload.AdAccountId);
        if (act.Length == 0)
            return BadRequest(new { message = "Reklama akkaunti ID noto'g'ri — u 'act_1234567890' ko'rinishida (yoki faqat raqamlar) bo'lishi kerak." });

        var token = (payload.AccessToken ?? "").Trim();

        // Mavjud qator AVVAL id bo'yicha qidiriladi: bir marta uzilgan (IsActive=false) akkaunt
        // qayta ulanganda YANGI qator yaratilsa, unikal indeks buzilardi.
        var existing = await db.IgAdAccounts.FirstOrDefaultAsync(a => a.AdAccountId == act, ct);
        var active = await db.IgAdAccounts.Where(a => a.IsActive).ToListAsync(ct);

        if (token.Length == 0) token = existing?.AccessToken ?? "";
        if (token.Length == 0)
            return BadRequest(new { message = "Reklama akkaunti tokeni kiritilmagan." });

        var (ok, info, err) = await insightsApi.FetchAccountAsync(act, token, ct);
        if (!ok || info is null) return BadRequest(new { message = err });

        // ⚠️ Boshqa akkauntlar UZILADI (o'chirilmaydi — statistikasi tarixi qoladi) va tokeni
        // TOZALANADI: ishlatilmayotgan token bazada qolib ketishi keraksiz xavf.
        foreach (var other in active.Where(a => a.AdAccountId != act))
        {
            other.IsActive = false;
            other.AccessToken = "";
        }

        var acc = existing;
        if (acc is null)
        {
            acc = new IgAdAccount { AdAccountId = act, ConnectedAt = AppClock.Iso() };
            db.IgAdAccounts.Add(acc);
        }

        MetaInsightsService.ApplyAccountInfo(acc, info);
        acc.AccessToken = token;
        acc.IsActive = true;
        acc.ConnectedBy = Actor;
        acc.LastError = "";
        if (acc.ConnectedAt.Length == 0) acc.ConnectedAt = AppClock.Iso();

        // ⚠️ Auditga TOKEN yozilmaydi — faqat qaysi akkaunt ulangani.
        // ⚠️ Offset MANBASI ham yoziladi: "nega summalar 100 barobar boshqacha" degan savol
        // bir yildan keyin berilsa, javob tarixda turishi kerak.
        var offsetSource = info.OffsetSource == MetaOffsetSource.Meta
            ? "Meta bergan"
            : "valyuta jadvalidan";

        audit.Record(AuditEntity, acc.Id, "update",
            $"Reklama statistikasi uchun Meta akkaunti ulandi: {acc.Name} ({act}), "
            + $"valyuta {(acc.Currency.Length > 0 ? acc.Currency : "noma'lum")} "
            + $"(kasr xonalari {acc.CurrencyOffset} — {offsetSource}), "
            + $"vaqt zonasi {(acc.TimezoneName.Length > 0 ? acc.TimezoneName : "noma'lum")}");

        await db.SaveChangesAsync(ct);
        return await AdsStatsStatus(ct);
    }

    /// <summary>Reklama akkauntini UZISH. Qator O'CHIRILMAYDI (yig'ilgan statistika va ROI
    /// tarixi saqlansin) — faqat <c>IsActive=false</c> va token TOZALANADI.</summary>
    [HttpDelete("adsstats/account")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgAdsStatusDto>> DisconnectAdsAccount(CancellationToken ct)
    {
        var accounts = await db.IgAdAccounts.Where(a => a.IsActive).ToListAsync(ct);
        if (accounts.Count == 0) return BadRequest(new { message = "Ulangan reklama akkaunti yo'q." });

        foreach (var a in accounts)
        {
            a.IsActive = false;
            a.AccessToken = "";
        }

        audit.Record(AuditEntity, accounts[0].Id, "update",
            $"Reklama statistikasi akkaunti uzildi ({accounts[0].AdAccountId}) — statistika yangilanmaydi, "
            + "yig'ilgan ma'lumot saqlanib qoladi");

        await db.SaveChangesAsync(ct);
        return await AdsStatsStatus(ct);
    }

    /// <summary>
    /// QO'LDA sinxronizatsiya ("Yangilash" tugmasi).
    ///
    /// <para><c>since</c> va <c>until</c> berilsa — AYNAN o'sha oraliq
    /// (<see cref="MetaInsightsService.SyncRangeAsync"/>); berilmasa odatdagi kunlik siyosat
    /// (birinchi marta backfill, keyin oxirgi kunlar) ishlaydi.</para>
    ///
    /// <para>⚠️ <b>HTTP 200 qaytadi, muvaffaqiyatsiz bo'lsa ham</b> — javobda <c>ok=false</c>
    /// va o'zbekcha sabab bo'ladi. Sabab: sinxronizatsiya qisman bajarilishi mumkin (bir necha
    /// bo'lak yozildi, keyingisi kvotaga urildi) va bu holatni "400 Bad Request" ifodalay
    /// olmaydi — foydalanuvchi nechta qator kelganini ham, nima uchun to'xtaganini ham
    /// ko'rishi kerak.</para>
    ///
    /// <para>⚠️ Sanalar — AKKAUNT vaqt zonasidagi kunlar (Meta statistikani o'sha zonada
    /// kesadi), Toshkent kuni bilan bir kunga farq qilishi mumkin.</para>
    /// </summary>
    [HttpPost("adsstats/sync")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgAdsSyncResultDto>> SyncAdsStats(
        IgAdsSyncPayload? payload, [FromServices] MetaInsightsService svc, CancellationToken ct)
    {
        var since = (payload?.Since ?? "").Trim();
        var until = (payload?.Until ?? "").Trim();
        var manual = since.Length > 0 && until.Length > 0;

        // ⚠️ Audit YOZUVI sinxronizatsiyadan KEYIN qo'shiladi: `MetaInsightsService` o'z
        // `SaveChangesAsync`ini chaqiradi va tranzaksiya BIR XIL DbContext'da (`IAppDbContext`
        // → o'sha `AppDbContext`), ya'ni oldin yozilgan audit qatori yarim yo'lda saqlanib
        // ketardi — "sinxronizatsiya qilindi" deb yozilib, aslida boshlanmagan bo'lardi.
        var (ok, rows, err) = manual
            ? await svc.SyncRangeAsync(since, until, ct)
            : await svc.SyncAsync(ct);

        var range = manual ? $"{since} … {until}" : "avtomatik oraliq";

        // Audit yozuvi AKKAUNT qatoriga bog'lanadi (uzilgan bo'lsa ham topiladi) — shunda
        // "kim qachon qaysi akkauntni yangilagan" tarixi bitta obyekt ostida turadi.
        var accId = await db.IgAdAccounts
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.ConnectedAt)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct) ?? "adsstats";

        audit.Record(AuditEntity, accId, "update",
            ok
                ? $"Reklama statistikasi qo'lda yangilandi ({range}) — {rows} ta qator"
                : $"Reklama statistikasini qo'lda yangilash bajarilmadi ({range}): {err}");
        await db.SaveChangesAsync(ct);

        return new IgAdsSyncResultDto(ok, rows, err, await AdsStatsStatusValueAsync(ct));
    }

    /// <summary>Holatni DTO sifatida oladi (sinxronizatsiya javobiga qo'shish uchun) —
    /// <see cref="AdsStatsStatus"/> <c>ActionResult</c> qaytargani uchun uni to'g'ridan-to'g'ri
    /// ichma-ich ishlatib bo'lmaydi.</summary>
    private async Task<IgAdsStatusDto> AdsStatsStatusValueAsync(CancellationToken ct)
    {
        var result = await AdsStatsStatus(ct);
        return result.Value ?? throw new InvalidOperationException("Holat DTO'si bo'sh qaytdi.");
    }
}

/* ═════════════════════════ DTO'lar ═════════════════════════ */

/// <summary>Reklama statistikasi moduli holati.
/// <para>⚠️ <paramref name="TokenSet"/> — TOKEN QIYMATI EMAS, faqat "sozlangan/sozlanmagan"
/// bayrog'i. Token hech qaysi javobga tushmaydi.</para>
/// <para><paramref name="CurrencyOffset"/> pul summalarini ekranda to'g'ri ko'rsatish uchun
/// kerak: baza MINOR unit'da (tiyin/sent) saqlaydi.</para>
/// <para><paramref name="CurrencyOffsetSource"/> (<see cref="MetaOffsetSource"/>:
/// <c>"meta"</c> | <c>"jadval"</c>) — offset Meta javobidan olindimi yoki bizning valyuta
/// jadvalimizdan. Noto'g'ri offset pul hisobini 100 barobar buzadi, ya'ni admin summalar
/// QAYSI manbadagi offset bilan hisoblanayotganini ko'rishi kerak. Akkaunt ulanmagan bo'lsa
/// bo'sh satr ("hali ma'lum emas"). ⚠️ Qiymat HISOBLANADI — bazada bunday ustun YO'Q
/// (<see cref="MetaInsightsService.OffsetSourceOf"/>).</para>
/// <para><paramref name="TimezoneName"/> — statistika kunlari AYNAN shu zonada kesiladi;
/// UI'da "sanalar reklama akkaunti vaqt zonasida" deb tushuntiriladi.</para></summary>
public record IgAdsStatusDto(
    bool Enabled,
    bool Connected,
    string AdAccountId,
    string Name,
    string Currency,
    int CurrencyOffset,
    string CurrencyOffsetSource,
    string TimezoneName,
    bool TokenSet,
    string ConnectedAt,
    string ConnectedBy,
    string LastSyncAt,
    string LastError,
    int SyncHour,
    int BackfillDays,
    int InsightRows,
    int EntityRows,
    string LastStatDate);

/// <summary>Ulash formasi. <paramref name="AccessToken"/> bo'sh bo'lsa mavjud token
/// saqlanadi.</summary>
public record IgAdsAccountPayload(string? AdAccountId, string? AccessToken);

/// <summary>Qo'lda sinxronizatsiya so'rovi. Ikkala sana ham berilmasa — kunlik siyosat.</summary>
public record IgAdsSyncPayload(string? Since, string? Until);

/// <summary>Sinxronizatsiya natijasi + YANGILANGAN holat (klient ikkinchi so'rov
/// yubormasin).</summary>
public record IgAdsSyncResultDto(bool Ok, int Rows, string Error, IgAdsStatusDto Status);
