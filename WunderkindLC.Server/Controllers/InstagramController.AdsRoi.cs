using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → REKLAMA STATISTIKASI — <b>HISOBOT</b> endpointlari (o'qish uchun).
///
/// <para>Ulash/sinxronizatsiya <c>InstagramController.AdsStats.cs</c> da; bu yerda faqat
/// <b>KO'RSATISH</b>: KPI, kunlik grafik, platforma kesimi va §4.8 dagi ROI jadvali.</para>
///
/// <para><b>Yagona manba:</b> uchala endpoint ham AYNAN bitta hisobotdan
/// (<see cref="MetaAdsRoi.BuildAsync"/>) oziqlanadi va uni bir xil kesh yozuvidan oladi.
/// Sabab: "KPI kartochkasida 42 ta lid, jadval ostida 38 ta" holati bitta ekranda darhol
/// ishonchni yo'qotadi — raqam ikki joyda ayri hisoblanmasligi kerak.</para>
///
/// <para><b>Ruxsat:</b> sinf darajasidagi <c>[AdminPerm("marketing", ReadRequiresPerm = true)]</c>
/// avtomatik qo'llanadi. Uchalasi ham FAQAT o'qiydi — hech narsa o'zgartirmaydi, shuning uchun
/// auditga ham yozilmaydi (<c>audit.md</c>: tahlil/hisobot ma'lumotni o'zgartirmaydi).</para>
///
/// <para><b>⚠️ Reklama akkaunti ulanmagan bo'lsa 500 ham, 400 ham EMAS:</b> javob 200 va
/// <c>connected: false</c> — UI "ulanmagan" ekranini chizadi. Xato holati bilan "hali
/// sozlanmagan" holatini aralashtirish foydalanuvchini "nimadir buzildi" deb qo'rqitardi.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Sana berilmaganda ko'riladigan oxirgi kunlar soni.</summary>
    private const int AdsRoiDefaultDays = 30;

    /// <summary>Bir so'rovda ko'riladigan eng uzun oraliq (kun).
    /// <para>⚠️ Chegara bor, chunki hisobot butun oraliqdagi kunlik faktlarni XOTIRAGA yuklaydi:
    /// 200 e'lon × 2 platforma × 730 kun = yuz minglab qator. Oshib ketgan oraliq JIM
    /// qirqilmaydi — 400 va o'zbekcha sabab qaytadi.</para></summary>
    private const int AdsRoiMaxDays = 400;

    /// <summary>ROI hisobotining kesh muddati. Versiyali kalit tufayli bog'liq jadvallar
    /// o'zgarganda kesh ALLAQACHON yangilanadi — TTL faqat zaxira
    /// (<c>course-analytics.md</c> §5 bilan bir xil siyosat).</summary>
    private static readonly TimeSpan AdsRoiTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Hisobot bog'liq bo'lgan jadvallar. Bulardan biri o'zgarsa (yangi lid, yangi to'lov,
    /// sinxronizatsiya) kesh AVTOMATIK eskiradi.
    ///
    /// <para>⚠️ <see cref="IgAdAccount"/> ham ro'yxatda: valyuta, offset va "ulanganmi"
    /// holati SHU jadvaldan olinadi — akkaunt ulangan zahoti hisobot 10 daqiqa "ulanmagan"
    /// deb turib qolmasligi kerak (spetsifikatsiyadagi ro'yxatga qo'shimcha).</para>
    /// </summary>
    private static readonly string[] AdsRoiDeps =
    {
        nameof(IgAdAccount), nameof(IgAdInsight), nameof(IgAdEntity), nameof(IgAdLead),
        nameof(Lead), nameof(LeadStage), nameof(StudentGroup), nameof(FinanceTransaction),
    };

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  Endpointlar
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>KPI + kunlik qator + platforma kesimi</b> — sahifaning tepa qismi.
    ///
    /// <para>Kampaniya daraxti ATAYIN qaytmaydi: bu endpoint har filtr o'zgarganda chaqiriladi
    /// va jadval (yuzlab qator) uni keraksiz og'irlashtirardi.</para>
    /// </summary>
    [HttpGet("adsstats/overview")]
    public async Task<ActionResult<IgRoiOverviewDto>> AdsStatsOverview(
        [FromServices] DataCache dataCache,
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? platform, [FromQuery] string? campaignId,
        CancellationToken ct = default)
    {
        var (ok, error, report) = await LoadAdsRoiAsync(dataCache, from, to, platform, campaignId, ct);
        if (!ok) return BadRequest(new { message = error });

        return new IgRoiOverviewDto(
            report!.Connected, report.AdAccountId, report.AdAccountName,
            report.Currency, report.CurrencyOffset, report.TimezoneName,
            report.From, report.To, report.Platform, report.CampaignId,
            report.LastSyncAt, report.LastError, report.InsightLevel,
            report.Totals, report.Daily, report.Platforms, report.Notes,
            report.AttributionSetting);
    }

    /// <summary>
    /// <b>Kampaniya → adset → e'lon daraxti</b> metrikalari bilan (jadval uchun).
    ///
    /// <para>Har tugunda ROI ustunlari ham bor — daraxt va ROI hisoboti bitta hisobdan
    /// chiqadi, ya'ni ikki tabda bir xil kampaniya boshqa raqam ko'rsatmaydi.</para>
    /// </summary>
    [HttpGet("adsstats/campaigns")]
    public async Task<ActionResult<IgRoiCampaignsDto>> AdsStatsCampaigns(
        [FromServices] DataCache dataCache,
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? platform, [FromQuery] string? campaignId,
        CancellationToken ct = default)
    {
        var (ok, error, report) = await LoadAdsRoiAsync(dataCache, from, to, platform, campaignId, ct);
        if (!ok) return BadRequest(new { message = error });

        return new IgRoiCampaignsDto(
            report!.Connected, report.From, report.To, report.Platform, report.CampaignId,
            report.Currency, report.CurrencyOffset, report.InsightLevel,
            report.Totals, report.Campaigns, report.Notes,
            report.AttributionSetting);
    }

    /// <summary>
    /// 🏆 <b>TO'LIQ ROI HISOBOTI</b> (§4.8): xarajat → lid → o'quvchi → to'lov → daromad.
    ///
    /// <para>Excel'ga chiqarish, chop etish va tahlil uchun hamma narsa bitta javobda:
    /// jamlanma, kunlik qator, platforma kesimi va butun daraxt.</para>
    /// </summary>
    [HttpGet("adsstats/roi")]
    public async Task<ActionResult<IgRoiReportDto>> AdsStatsRoi(
        [FromServices] DataCache dataCache,
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? platform, [FromQuery] string? campaignId,
        CancellationToken ct = default)
    {
        var (ok, error, report) = await LoadAdsRoiAsync(dataCache, from, to, platform, campaignId, ct);
        if (!ok) return BadRequest(new { message = error });
        return report!;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  Yordamchilar
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filtrlarni tekshiradi va hisobotni keshdan (yoki hisoblab) oladi.
    ///
    /// <para><b>Kesh kaliti — <c>marketing:ads-roi:{from}:{to}:{platform}:{campaign}</c>.</b>
    /// ⚠️ Kampaniya ham kalitga KIRADI (spetsifikatsiyadagi kalitga qo'shimcha): jamlanma
    /// filtrga bog'liq hisoblanadi, ya'ni bitta kampaniya tanlanganda ham KPI shu
    /// kampaniyaniki bo'lishi kerak. Kampaniyani kalitdan tashqarida qoldirib, keshlangan
    /// natijani keyin filtrlash "jadval bitta kampaniya, tepadagi KPI esa hammasi" degan
    /// mos kelmaslikni berardi.</para>
    /// </summary>
    private static async Task<(bool Ok, string Error, IgRoiReportDto? Report)> LoadAdsRoiAsync(
        DataCache dataCache, string? from, string? to, string? platform, string? campaignId,
        CancellationToken ct)
    {
        var (rangeOk, rangeError, fromDay, toDay) = NormalizeAdsRange(from, to);
        if (!rangeOk) return (false, rangeError, null);

        var pf = MetaAdsRoi.NormalizePlatform(platform);
        // Kampaniya id — Meta'dan kelgan raqamli satr. Kalitga tushgani uchun uzunligi
        // cheklanadi (cheksiz uzun so'rov parametri kesh kalitini shishirmasin).
        var camp = (campaignId ?? "").Trim();
        if (camp.Length > 64) camp = camp[..64];

        var report = await dataCache.GetOrCreateAsync(
            $"marketing:ads-roi:{fromDay}:{toDay}:{pf}:{(camp.Length == 0 ? "all" : camp)}",
            AdsRoiDeps,
            AdsRoiTtl,
            db => MetaAdsRoi.BuildAsync(db, fromDay, toDay, pf, camp, ct));

        return (true, "", report);
    }

    /// <summary>
    /// Sana oralig'ini normallashtiradi: bo'sh bo'lsa — oxirgi
    /// <see cref="AdsRoiDefaultDays"/> kun; teskari berilgan bo'lsa — joyi almashtiriladi
    /// (foydalanuvchi xatosi uchun bo'sh ekran ko'rsatish shart emas).
    ///
    /// <para>⚠️ "Bugun" — <see cref="AppClock.Today"/> (Toshkent), <c>DateTime.Now</c> EMAS.
    /// Meta esa sanani reklama akkaunti zonasida beradi — farq hisobotning izohida
    /// (<see cref="IgRoiReportDto.Notes"/>) ochiq aytiladi.</para>
    /// </summary>
    private static (bool Ok, string Error, string From, string To) NormalizeAdsRange(string? from, string? to)
    {
        var today = AppClock.Today;

        if (!TryParseDay(to, out var toDay)) toDay = today;
        if (!TryParseDay(from, out var fromDay)) fromDay = toDay.AddDays(-(AdsRoiDefaultDays - 1));

        if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);

        var days = toDay.DayNumber - fromDay.DayNumber + 1;
        if (days > AdsRoiMaxDays)
            return (false, $"Oraliq juda uzun ({days} kun). Ko'pi bilan {AdsRoiMaxDays} kun tanlang.",
                    "", "");

        return (true, "", fromDay.ToString("yyyy-MM-dd"), toDay.ToString("yyyy-MM-dd"));
    }

    /// <summary>"yyyy-MM-dd" ni qat'iy o'qiydi; boshqa har qanday ko'rinish — "berilmagan".</summary>
    private static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out day);
}

// ═══════════════════════════════════════════════════════════════════════════════════════
//  DTO'lar — javobning KESIMLARI. Hisobot obyektining o'zi (`IgRoiReportDto`) Application
//  qatlamida; bu yerdagilar faqat "qaysi endpoint nimani qaytaradi" ni belgilaydi.
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>KPI + kunlik qator + platforma kesimi (kampaniya daraxtisiz).</summary>
public record IgRoiOverviewDto(
    bool Connected,
    string AdAccountId,
    string AdAccountName,
    string Currency,
    int CurrencyOffset,
    string TimezoneName,
    string From,
    string To,
    string Platform,
    string CampaignId,
    string LastSyncAt,
    string LastError,
    string InsightLevel,
    IgRoiNodeDto Totals,
    IReadOnlyList<IgRoiDayDto> Daily,
    IReadOnlyList<IgRoiPlatformDto> Platforms,
    IReadOnlyList<string> Notes,
    /// <summary>Meta bergan atributsiya oynasi (<c>attribution_setting</c>) — HISOBOT OSTIDA
    /// kichik izoh sifatida ko'rsatiladi, TARJIMA QILINMAYDI.</summary>
    string AttributionSetting);

/// <summary>Kampaniya → adset → e'lon daraxti (jamlanma bilan birga — jadval ostidagi
/// "Jami" qatori AYNAN shundan chiziladi, qatorlarni qo'shib chiqarilmaydi).</summary>
public record IgRoiCampaignsDto(
    bool Connected,
    string From,
    string To,
    string Platform,
    string CampaignId,
    string Currency,
    int CurrencyOffset,
    string InsightLevel,
    IgRoiNodeDto Totals,
    IReadOnlyList<IgRoiNodeDto> Campaigns,
    IReadOnlyList<string> Notes,
    /// <summary>Meta bergan atributsiya oynasi — jadval ostidagi izoh uchun (overview bilan
    /// AYNAN bir xil qiymat: ikkala tab bitta hisobotdan oziqlanadi).</summary>
    string AttributionSetting);
