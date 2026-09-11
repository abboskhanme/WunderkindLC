using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

// ═════════════════════════════════════════════════════════════════════════════════════════
//  REKLAMA ROI HISOBOTI — DTO'lar
//  (Ataylab Application qatlamida: hisob-kitob shu yerda, controller faqat HTTP tarjimasi
//   qiladi. `IgRoi` prefiksi — marketing bo'limining boshqa DTO'lari bilan aralashmasin.)
// ═════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hisobotning BITTA qatori — kampaniya, adset yoki e'lon (ichma-ich <see cref="Children"/>).
///
/// <para>⚠️ Pul HAR DOIM <b>minor unit</b> (<c>long</c>): formatlashni UI qiladi
/// (<see cref="MetaCurrency.FormatMinor"/>). Valyuta va offset butun hisobot uchun bitta —
/// <see cref="IgRoiReportDto.Currency"/>.</para>
///
/// <para>⚠️ <see cref="CplMinor"/>, <see cref="CacMinor"/>, <see cref="Roi"/> — <b>null</b>
/// bo'lishi MUMKIN (bo'luvchi nol). Nol bilan almashtirilmaydi: "0 so'mga lid" bilan
/// "hisoblab bo'lmadi" bir xil ko'rinib qolardi. UI null'ni "—" deb chizadi.</para>
/// </summary>
/// <param name="Level">`campaign` | `adset` | `ad` | `total`.</param>
/// <param name="Id">Meta'dagi id (jami qatorda bo'sh).</param>
/// <param name="Name">Nomi; sinxronlanmagan tugunda — id'ning o'zi (sun'iy "Noma'lum" YOZILMAYDI).</param>
/// <param name="Reach">Qamrovning <b>PASTKI chegarasi</b> — qarang <see cref="ReachApprox"/>.</param>
/// <param name="ReachUpper">Qamrovning YUQORI chegarasi (xom yig'indi, takrorlar bilan).</param>
/// <param name="MetaLeads">Meta hisoblagan lidlar: <c>LeadsOnsite + LeadsPixel</c>.</param>
/// <param name="MsgStarted">Meta hisoblagan <b>BOSHLANGAN YOZISHMALAR</b>
/// (<c>onsite_conversion.messaging_conversation_started_7d</c>) — "Xabar yuborish"
/// (Click-to-Direct) maqsadidagi kampaniyaning ASOSIY natijasi: bunday reklamada forma
/// umuman bo'lmaydi, ya'ni <paramref name="MetaLeads"/> nol bo'lib turaveradi.
/// <para>⚠️ Lidlar bilan <b>QO'SHILMAYDI</b>: bitta odam formani ham to'ldirib, DM ham yozishi
/// mumkin. Ayri ustun/KPI sifatida ko'rsatiladi.</para>
/// <para>⚠️ 7 kunlik atributsiya oynasi tufayli bu son ORQAGA qarab o'zgaradi — oxirgi kunlar
/// qayta yuklanadi.</para></param>
/// <param name="AdLeadRows">CRM'ga kelgan XOM lid qatorlari (dublikatlar bilan).</param>
/// <param name="CrmLeads">TAKRORSIZ CRM lidlari (<c>DISTINCT LeadId</c>).</param>
/// <param name="CrmLeadsDeleted">Shulardan CRM'da endi mavjud bo'lmaganlari (o'chirilgan).</param>
/// <param name="RevenueMinor">Lidlar keltirgan SOF o'quv to'lovi — <b>butun umr bo'yi</b>.</param>
public sealed record IgRoiNodeDto(
    string Level,
    string Id,
    string Name,
    string Status,
    long SpendMinor,
    long Impressions,
    long Reach,
    long ReachUpper,
    bool ReachApprox,
    long Clicks,
    long LinkClicks,
    int MetaLeads,
    int MsgStarted,
    int AdLeadRows,
    int CrmLeads,
    int CrmLeadsDeleted,
    long? CplMinor,
    int Converted,
    int Paid,
    long RevenueMinor,
    long? CacMinor,
    decimal? Roi,
    IReadOnlyList<IgRoiNodeDto> Children);

/// <summary>Kunlik qator (grafik uchun). Qamrov ATAYIN yo'q — u kunlar bo'yicha qo'shilmaydi.</summary>
public sealed record IgRoiDayDto(
    string Date, long SpendMinor, long Impressions, long Clicks, int MetaLeads, int CrmLeads);

/// <summary>Platforma kesimi (Instagram / Facebook / ajratilmagan).</summary>
public sealed record IgRoiPlatformDto(
    string Platform, long SpendMinor, long Impressions, int MetaLeads, int CrmLeads);

/// <summary>
/// TO'LIQ hisobot: holat + jamlanma + kunlik qator + platforma kesimi + kampaniya daraxti.
///
/// <para>Uchala endpoint (<c>overview</c> · <c>campaigns</c> · <c>roi</c>) AYNAN shu obyektdan
/// oziqlanadi — aks holda bitta ekranning uch bo'lagi uch xil raqam ko'rsatishi mumkin edi.</para>
/// </summary>
/// <param name="Connected">Reklama akkaunti ulanganmi. <c>false</c> bo'lsa qolgani BO'SH,
/// lekin javob baribir 200 — UI "ulanmagan" holatini chizadi.</param>
/// <param name="InsightLevel">Statistika QAYSI darajadan yig'ildi (<see cref="MetaAdsRoi.PickLevel"/>).</param>
/// <param name="Notes">Foydalanuvchiga OCHIQ aytiladigan ogohlantirishlar (o'zbekcha).</param>
/// <param name="AttributionSetting">Meta bergan <b>ATRIBUTSIYA OYNASI</b>
/// (<c>attribution_setting</c>, masalan <c>7d_click,1d_view</c>) — Meta konversiyalari QAYSI
/// oyna bo'yicha sanalgani.
/// <para>⚠️ Qiymat Meta bergan HOLICHA qaytadi va <b>tarjima qilinmaydi</b>: bu Ads Manager'dagi
/// sozlamaning texnik nomi, uni o'zbekchalashtirish Meta interfeysi bilan solishtirishni
/// buzardi.</para>
/// <para>Bir davrda bir necha xil qiymat uchrasa (sozlama o'rtada o'zgargan) hammasi
/// <c>" · "</c> bilan sanab ko'rsatiladi — jimgina bittasi tanlanmaydi, aks holda hisobot
/// "bitta oyna bo'yicha" bo'lib ko'rinardi.</para>
/// <para>Bo'sh — Meta bermagan (eski sinxronizatsiya yoki xarajat yo'q).</para></param>
public sealed record IgRoiReportDto(
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
    IReadOnlyList<IgRoiNodeDto> Campaigns,
    IReadOnlyList<string> Notes,
    string AttributionSetting);

// ═════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 🏆 <b>REKLAMA ROI</b> — "N so'm sarfladik → M ta lid keldi → P tasi o'quvchi bo'ldi →
/// R so'm to'ladi". Ads Manager'da BO'LMAGAN narsa: Meta lidning SONINI biladi, CRM esa
/// o'sha lid PUL to'laganini biladi.
///
/// <para><b>Zanjir:</b> <c>IgAdInsight</c> (xarajat) → <c>IgAdLead.CampaignId/AdId</c> →
/// <c>IgAdLead.LeadId</c> → <c>Lead</c> → <see cref="LeadOutcome"/> (bosqich · o'quvchi ·
/// to'lov). <see cref="LeadOutcome"/> — <b>YAGONA manba</b>: "to'ladi" so'zi lid formalari,
/// daraja testi va reklama hisobotida bir xil ma'no anglatishi shart.</para>
///
/// <para><b>⚠️ TAQQOSLANMAYDIGAN O'LCHOV:</b> xarajat — faqat TANLANGAN oraliqda, daromad esa
/// o'sha oraliqda kelgan lidlarning <b>BUTUN UMR</b> bo'yicha to'lovi. Bu ataylab shunday
/// (lid bugun keladi, pulni keyingi oyda to'laydi), lekin ROI'ni "aniq foyda" deb o'qib
/// bo'lmaydi — shuning uchun <see cref="IgRoiReportDto.Notes"/> da OCHIQ yozib qo'yiladi.</para>
///
/// <para><b>⚠️ QAMROV (reach) QO'SHILMAYDI</b> — <see cref="ReachOf"/> izohiga qarang.</para>
///
/// <para>Hisob-kitobning o'zi sof funksiyalarda (<see cref="CplMinor"/>, <see cref="CacMinor"/>,
/// <see cref="RoiRatio"/>, <see cref="PickLevel"/>, <see cref="MatchesPlatform"/>) — testlar
/// aynan shularni va <see cref="BuildAsync"/> natijasini qoplaydi
/// (<c>WunderkindLC.Tests/MetaAdsRoiTests.cs</c>).</para>
/// </summary>
public static class MetaAdsRoi
{
    // ───────────────────────── Konstantalar ─────────────────────────

    public const string LevelCampaign = "campaign";
    public const string LevelAdset = "adset";
    public const string LevelAd = "ad";
    /// <summary>Jamlanma qatorining darajasi — u haqiqiy Meta tuguni EMAS.</summary>
    public const string LevelTotal = "total";

    public const string PlatformAll = "all";
    public const string PlatformInstagram = "instagram";
    public const string PlatformFacebook = "facebook";

    /// <summary><c>IgAdLead.Platform</c> Meta'dan QISQA keladi (<c>ig</c>/<c>fb</c>), insights
    /// esa to'liq nom bilan (<c>instagram</c>/<c>facebook</c>) — ikkisi bitta filtr ostida
    /// solishtiriladi.</summary>
    public const string LeadPlatformIg = "ig";
    public const string LeadPlatformFb = "fb";

    /// <summary>Jadvalga chiqadigan kampaniyalar chegarasi. Oshib ketgani JIM tashlanmaydi —
    /// <see cref="IgRoiReportDto.Notes"/> ga qator qo'shiladi (`books.md` dagi saboq).</summary>
    public const int MaxCampaigns = 200;

    /// <summary>Bir kampaniya ostida ko'rsatiladigan adset/e'lon soni.</summary>
    public const int MaxChildren = 100;

    /// <summary>Statistika yig'iladigan daraja tanlash TARTIBI: eng maydasi ustun.
    /// <para>⚠️ <c>IgAdInsight</c> da uch daraja bir jadvalda yotadi va ular
    /// <b>QO'SHILMASLIGI</b> kerak (kampaniya qatori o'z e'lonlari yig'indisi bilan bir xil —
    /// birga sanalsa sarf ikki-uch barobar ko'rinardi). Shuning uchun hisobot HAR DOIM
    /// BITTA darajadan yig'iladi, qolgan darajalar esa <c>ParentId</c> orqali yuqoriga
    /// ko'tariladi (roll-up).</para>
    /// <para>Eng maydasi tanlanadi, chunki faqat u kampaniya→adset→e'lon daraxtini to'liq
    /// chizishga imkon beradi.</para></summary>
    public static readonly string[] LevelPriority = { LevelAd, LevelAdset, LevelCampaign };

    // ───────────────────────── Sof funksiyalar ─────────────────────────

    /// <summary>Platforma filtri nomini normallashtiradi; noma'lum qiymat → <c>all</c>
    /// (bo'sh ekran ko'rsatgandan ko'ra hammasini ko'rsatgan xavfsizroq).</summary>
    public static string NormalizePlatform(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return v switch
        {
            PlatformInstagram or LeadPlatformIg => PlatformInstagram,
            PlatformFacebook or LeadPlatformFb => PlatformFacebook,
            _ => PlatformAll,
        };
    }

    /// <summary>
    /// Insights qatori tanlangan platformaga mos keladimi.
    ///
    /// <para>⚠️ <c>Platform == "all"</c> qatorlar — <c>publisher_platform</c> bo'linmasisiz
    /// yuklangan (eski sinxronizatsiya yoki bo'linma qaytmagan holat). Ular platforma
    /// tanlanganda <b>KIRMAYDI</b>: "Instagram sarfi" deb Facebook pulini ham qo'shib
    /// ko'rsatish hisobotni yolg'on qilardi. Buning natijasi (kesim bo'sh chiqishi)
    /// <see cref="IgRoiReportDto.Notes"/> da aytiladi.</para>
    /// </summary>
    public static bool MatchesPlatform(string? rowPlatform, string filter)
    {
        if (filter == PlatformAll) return true;
        return string.Equals((rowPlatform ?? "").Trim(), filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lid qatori (<c>ig</c>/<c>fb</c>) tanlangan platformaga mos keladimi.</summary>
    public static bool LeadMatchesPlatform(string? leadPlatform, string filter)
    {
        if (filter == PlatformAll) return true;
        var p = (leadPlatform ?? "").Trim().ToLowerInvariant();
        return filter switch
        {
            PlatformInstagram => p is LeadPlatformIg or PlatformInstagram,
            PlatformFacebook => p is LeadPlatformFb or PlatformFacebook,
            _ => true,
        };
    }

    /// <summary>Lid qatori platformasini insights nomiga keltiradi (kesim uchun).</summary>
    public static string LeadPlatformName(string? leadPlatform)
    {
        var p = (leadPlatform ?? "").Trim().ToLowerInvariant();
        return p switch
        {
            LeadPlatformIg or PlatformInstagram => PlatformInstagram,
            LeadPlatformFb or PlatformFacebook => PlatformFacebook,
            _ => PlatformAll,
        };
    }

    /// <summary>
    /// "yyyy-MM-dd" → o'sha KUNNING OXIRI, ISO satrlarni <c>CompareTo</c> bilan solishtirish uchun.
    ///
    /// <para>⚠️ <c>".999"</c> ataylab: <c>IgAdLead.CreatedTime</c> Meta'dan zona qo'shimchasi
    /// bilan kelishi mumkin (<c>2026-08-05T23:59:59+0000</c>). Oddiy <c>"T23:59:59"</c> chegarasi
    /// bilan bunday qator ordinal taqqoslashda CHEGARADAN KATTA chiqib, oxirgi soniya
    /// hisobotdan tushib qolardi.</para>
    /// </summary>
    public static string DayEnd(string day) => (day ?? "").Trim() + "T23:59:59.999";

    /// <summary>
    /// CPL — bitta lidning narxi (minor unit). Xarajat ≤ 0 yoki lid ≤ 0 bo'lsa <b>null</b>:
    /// "lid tekinga tushdi" degan yolg'on xulosa chiqmasin.
    /// </summary>
    public static long? CplMinor(long spendMinor, int leads)
    {
        if (spendMinor <= 0 || leads <= 0) return null;
        return (long)Math.Round((decimal)spendMinor / leads, MidpointRounding.AwayFromZero);
    }

    /// <summary>CAC — bitta TO'LAGAN mijozning narxi (minor unit). Qoida <see cref="CplMinor"/>
    /// bilan bir xil.</summary>
    public static long? CacMinor(long spendMinor, int paidCustomers) => CplMinor(spendMinor, paidCustomers);

    /// <summary>
    /// ROI = (Daromad − Xarajat) / Xarajat. <c>1.5</c> = "+150%".
    ///
    /// <para>Xarajat ≤ 0 → <b>null</b> (nolga bo'lish; "cheksiz ROI" ko'rsatish grafikni
    /// buzardi). Daromad 0 bo'lsa natija <c>-1</c> (butun pul kuydi) — bu HAQIQIY qiymat,
    /// null EMAS.</para>
    /// </summary>
    public static decimal? RoiRatio(long revenueMinor, long spendMinor)
    {
        if (spendMinor <= 0) return null;
        return (decimal)(revenueMinor - spendMinor) / spendMinor;
    }

    /// <summary>
    /// Mavjud darajalardan BITTASINI tanlaydi (<see cref="LevelPriority"/> tartibida).
    /// Hech biri bo'lmasa — bo'sh satr.
    /// </summary>
    public static string PickLevel(IEnumerable<string?> levels)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in levels)
            if (!string.IsNullOrWhiteSpace(l)) set.Add(l.Trim());
        foreach (var candidate in LevelPriority)
            if (set.Contains(candidate)) return candidate;
        return "";
    }

    /// <summary>
    /// 🔴 <b>QAMROV — NEGA QO'SHILMAYDI.</b>
    ///
    /// <para><c>publisher_platform</c> bo'linmasi bilan yuklangan qatorlar Meta tomonidan
    /// <b>dedup QILINMAGAN</b>: bitta odam dushanba ham, seshanba ham reklamani ko'rgan
    /// bo'lsa, u ikkala kunlik qatorda ham sanaladi; Instagram va Facebook kesimlarida ham
    /// alohida. Ya'ni <c>SUM(Reach)</c> — qamrov EMAS, "ko'rsatish-odam"lar yig'indisi.
    /// Uni "qamrov" deb chizish audit paytida darhol yolg'on chiqardi.</para>
    ///
    /// <para><b>Qaror:</b> ikkita HALOL chegara beriladi va ikkalasi ham nomi bilan
    /// belgilanadi:</para>
    /// <list type="bullet">
    ///   <item><b>Pastki chegara</b> = qatorlar bo'yicha <c>MAX</c>. Haqiqiy qamrov undan
    ///         KICHIK bo'lishi mumkin emas (bitta kun/platformadagi noyob odamlar soni butun
    ///         davr qamrovining qismi). UI'da asosiy raqam sifatida shu ko'rsatiladi.</item>
    ///   <item><b>Yuqori chegara</b> = <c>SUM</c> (takrorlar bilan) — "bundan ko'p bo'lishi
    ///         mumkin emas".</item>
    /// </list>
    /// <para>Aniq qamrov faqat Meta'dan <b>butun davr uchun bitta so'rov</b> bilan
    /// (<c>time_increment</c> va bo'linmasiz) olinadi — bu boshqa ish, shu sababli hisobot
    /// taxminiy ekanini yashirmaydi (<see cref="IgRoiNodeDto.ReachApprox"/> = true).</para>
    /// </summary>
    public static (long Lower, long Upper) ReachOf(IEnumerable<long> rowReaches)
    {
        long max = 0, sum = 0;
        foreach (var r in rowReaches)
        {
            if (r <= 0) continue;
            if (r > max) max = r;
            sum += r;
        }
        return (max, sum);
    }

    // ───────────────────────── Ichki yig'uvchi ─────────────────────────

    /// <summary>Bitta tugun (kampaniya/adset/e'lon) bo'yicha yig'ilayotgan xom sonlar.</summary>
    private sealed class Agg
    {
        public long Spend;
        public long Impressions;
        public long Clicks;
        public long LinkClicks;
        public long ReachLower;   // MAX
        public long ReachUpper;   // SUM
        public int MetaLeads;
        /// <summary>Boshlangan yozishmalar (Click-to-Direct natijasi) — lidlarga QO'SHILMAYDI.</summary>
        public int MsgStarted;
        public int AdLeadRows;
        /// <summary>TAKRORSIZ CRM lid id'lari — konversiya AYNAN shular bo'yicha sanaladi.</summary>
        public readonly HashSet<string> LeadIds = new(StringComparer.Ordinal);

        public void AddInsight(long spend, long impressions, long reach, long clicks,
                               long linkClicks, int metaLeads, int msgStarted)
        {
            Spend += spend;
            Impressions += impressions;
            Clicks += clicks;
            LinkClicks += linkClicks;
            MetaLeads += metaLeads;
            MsgStarted += msgStarted;
            if (reach > 0)
            {
                if (reach > ReachLower) ReachLower = reach;   // pastki chegara — MAX
                ReachUpper += reach;                          // yuqori chegara — SUM
            }
        }

        public void AddLeadRow(string leadId)
        {
            AdLeadRows++;
            if (!string.IsNullOrEmpty(leadId)) LeadIds.Add(leadId);
        }
    }

    private static Agg Bucket(Dictionary<string, Agg> map, string key)
    {
        if (!map.TryGetValue(key, out var agg)) map[key] = agg = new Agg();
        return agg;
    }

    // ───────────────────────── Asosiy hisob ─────────────────────────

    /// <summary>
    /// Hisobotni bazadan quradi. <paramref name="from"/>/<paramref name="to"/> — "yyyy-MM-dd"
    /// (kun sifatida, <paramref name="to"/> kunning OXIRIGACHA).
    ///
    /// <para>⚠️ Reklama akkaunti ulanmagan bo'lsa istisno OTILMAYDI — <c>Connected=false</c>
    /// bo'lgan bo'sh hisobot qaytadi (controller 200 beradi, UI "ulanmagan" ekranini chizadi).</para>
    ///
    /// <para>⚠️ Barcha jamlanmalar <b>BUTUN topilma</b> bo'yicha hisoblanadi va faqat
    /// KO'RSATILADIGAN qatorlar kesiladi.</para>
    /// </summary>
    public static async Task<IgRoiReportDto> BuildAsync(
        IAppDbContext db, string from, string to, string? platform, string? campaignId,
        CancellationToken ct = default)
    {
        var pf = NormalizePlatform(platform);
        var camp = (campaignId ?? "").Trim();
        var notes = new List<string>();

        // Bitta faol reklama akkaunti qo'llab-quvvatlanadi (ulash oqimi ham shunday).
        // ⚠️ Tartib `adsstats/status` bilan AYNAN bir xil (eng OXIRGI ulangani) — aks holda
        // holat paneli bir akkauntni, hisobot esa boshqasini ko'rsatishi mumkin edi.
        var account = await db.IgAdAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.ConnectedAt)
            .Select(a => new
            {
                a.AdAccountId, a.Name, a.Currency, a.CurrencyOffset, a.TimezoneName,
                a.LastSyncAt, a.LastError,
            })
            .FirstOrDefaultAsync(ct);

        if (account is null)
            return EmptyReport(from, to, pf, camp);

        var offset = MetaCurrency.Clamp(account.CurrencyOffset);

        // ── 1) Xarajat faktlari ──────────────────────────────────────────────────────────
        var insightQuery = db.IgAdInsights.AsNoTracking()
            .Where(i => i.AdAccountId == account.AdAccountId
                        && i.StatDate.CompareTo(from) >= 0
                        && i.StatDate.CompareTo(to) <= 0);

        var insightRows = await insightQuery
            .Select(i => new
            {
                i.Level, i.ExternalId, i.StatDate, i.Platform,
                i.Impressions, i.Reach, i.Clicks, i.LinkClicks, i.SpendMinor,
                i.LeadsOnsite, i.LeadsPixel, i.MsgStarted, i.AttributionSetting,
            })
            .ToListAsync(ct);

        // Daraja BITTA bo'lishi shart (izoh: LevelPriority).
        var level = PickLevel(insightRows.Select(r => r.Level));
        var usable = insightRows
            .Where(r => string.Equals(r.Level, level, StringComparison.OrdinalIgnoreCase))
            .Where(r => MatchesPlatform(r.Platform, pf))
            .ToList();

        if (pf != PlatformAll && usable.Count == 0 && insightRows.Count > 0)
            notes.Add("Tanlangan platforma bo'yicha xarajat topilmadi: statistika platformalarga "
                      + "ajratilmasdan yuklangan bo'lishi mumkin. \"Hammasi\" ni tanlab ko'ring.");

        // ── 2) Iyerarxiya (nom + ota tugun) ──────────────────────────────────────────────
        var entities = await db.IgAdEntities.AsNoTracking()
            .Where(e => e.AdAccountId == account.AdAccountId)
            .Select(e => new { e.Level, e.ExternalId, e.ParentId, e.Name, e.Status, e.EffectiveStatus })
            .ToListAsync(ct);

        var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        var statusById = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in entities)
        {
            if (string.IsNullOrEmpty(e.ExternalId)) continue;
            nameById[e.ExternalId] = e.Name ?? "";
            // Ekranda AMALDAGI holat muhimroq: kampaniya yoqilgan bo'lsa ham adset o'chgan
            // bo'lishi mumkin (`effective_status` aynan buni ko'rsatadi).
            statusById[e.ExternalId] = string.IsNullOrEmpty(e.EffectiveStatus) ? (e.Status ?? "") : e.EffectiveStatus;
            if (!string.IsNullOrEmpty(e.ParentId)) parentById[e.ExternalId] = e.ParentId;
        }

        // ── 3) CRM lidlari ───────────────────────────────────────────────────────────────
        // ⚠️ Lidlar AYNAN shu oraliqda kelganlari olinadi — CPL/CAC "oraliq xarajati / oraliq
        // lidlari" bo'lishi uchun. DAROMAD esa shu lidlarning BUTUN UMR to'lovi (§4.8).
        var toBound = DayEnd(to);
        var leadRowsRaw = await db.IgAdLeads.AsNoTracking()
            .Where(l => l.CreatedTime.CompareTo(from) >= 0 && l.CreatedTime.CompareTo(toBound) <= 0)
            .Select(l => new { l.CampaignId, l.AdsetId, l.AdId, l.LeadId, l.Platform, l.CreatedTime })
            .ToListAsync(ct);

        var leadRows = leadRowsRaw.Where(l => LeadMatchesPlatform(l.Platform, pf)).ToList();

        // Lid qatorida ham ota-bola bog'lanishi bor (ad → adset → campaign). Iyerarxiya
        // sinxronlanmagan bo'lsa ham daraxt shu manbadan tiklanadi — xarajat "noma'lum"
        // kampaniyaga tushib, lidlar esa yo'qolib ketmasin.
        foreach (var l in leadRows)
        {
            if (!string.IsNullOrEmpty(l.AdId) && !string.IsNullOrEmpty(l.AdsetId))
                parentById.TryAdd(l.AdId, l.AdsetId);
            if (!string.IsNullOrEmpty(l.AdsetId) && !string.IsNullOrEmpty(l.CampaignId))
                parentById.TryAdd(l.AdsetId, l.CampaignId);
        }

        // ── 4) Lid → o'quvchi → to'lov (YAGONA manba) ────────────────────────────────────
        var outcome = await LeadOutcome.BuildAsync(
            db, leadRows.Select(l => l.LeadId).Where(x => !string.IsNullOrEmpty(x)));

        // ── 5) Yig'ish ───────────────────────────────────────────────────────────────────
        var campaigns = new Dictionary<string, Agg>(StringComparer.Ordinal);
        var adsets = new Dictionary<string, Agg>(StringComparer.Ordinal);
        var ads = new Dictionary<string, Agg>(StringComparer.Ordinal);
        var total = new Agg();

        // ⚠️ TARTIBLI to'plam: bir necha qiymat bo'lsa ro'yxat har so'rovda bir xil chiqsin
        // (aks holda kesh yozuvi bir xil bo'lgani holda matn "goh u, goh bu" ko'rinardi).
        var attributionSettings = new SortedSet<string>(StringComparer.Ordinal);

        var daily = new Dictionary<string, (long Spend, long Impressions, long Clicks, int MetaLeads, HashSet<string> Leads)>(StringComparer.Ordinal);
        var byPlatform = new Dictionary<string, (long Spend, long Impressions, int MetaLeads, HashSet<string> Leads)>(StringComparer.Ordinal);

        foreach (var r in usable)
        {
            var metaLeads = r.LeadsOnsite + r.LeadsPixel;
            var adId = level == LevelAd ? r.ExternalId : "";
            var adsetId = level == LevelAdset ? r.ExternalId : ParentIn(r.ExternalId, level, LevelAdset, parentById);
            var campId = CampaignOf(r.ExternalId, level, parentById);

            if (camp.Length > 0 && campId != camp) continue;

            if (!string.IsNullOrEmpty(adId))
                Bucket(ads, adId).AddInsight(r.SpendMinor, r.Impressions, r.Reach, r.Clicks, r.LinkClicks, metaLeads, r.MsgStarted);
            if (!string.IsNullOrEmpty(adsetId))
                Bucket(adsets, adsetId).AddInsight(r.SpendMinor, r.Impressions, r.Reach, r.Clicks, r.LinkClicks, metaLeads, r.MsgStarted);
            Bucket(campaigns, campId).AddInsight(r.SpendMinor, r.Impressions, r.Reach, r.Clicks, r.LinkClicks, metaLeads, r.MsgStarted);
            total.AddInsight(r.SpendMinor, r.Impressions, r.Reach, r.Clicks, r.LinkClicks, metaLeads, r.MsgStarted);

            // Atributsiya oynasi — qaysi qiymatlar UCHRAGANI (kesilgan qatorlardan emas,
            // hisobga KIRGANLARIDAN): ekranda "Meta konversiyalari qaysi oyna bo'yicha" deb
            // ko'rsatiladi.
            if (!string.IsNullOrWhiteSpace(r.AttributionSetting))
                attributionSettings.Add(r.AttributionSetting.Trim());

            var day = daily.TryGetValue(r.StatDate, out var d)
                ? d
                : (0L, 0L, 0L, 0, new HashSet<string>(StringComparer.Ordinal));
            daily[r.StatDate] = (day.Item1 + r.SpendMinor, day.Item2 + r.Impressions,
                                 day.Item3 + r.Clicks, day.Item4 + metaLeads, day.Item5);

            var pkey = string.IsNullOrEmpty(r.Platform) ? PlatformAll : r.Platform;
            var pv = byPlatform.TryGetValue(pkey, out var p)
                ? p
                : (0L, 0L, 0, new HashSet<string>(StringComparer.Ordinal));
            byPlatform[pkey] = (pv.Item1 + r.SpendMinor, pv.Item2 + r.Impressions,
                                pv.Item3 + metaLeads, pv.Item4);
        }

        foreach (var l in leadRows)
        {
            var campId = !string.IsNullOrEmpty(l.CampaignId)
                ? l.CampaignId
                : CampaignOf(l.AdId, LevelAd, parentById);
            if (camp.Length > 0 && campId != camp) continue;

            if (!string.IsNullOrEmpty(l.AdId)) Bucket(ads, l.AdId).AddLeadRow(l.LeadId);
            if (!string.IsNullOrEmpty(l.AdsetId)) Bucket(adsets, l.AdsetId).AddLeadRow(l.LeadId);
            Bucket(campaigns, campId).AddLeadRow(l.LeadId);
            total.AddLeadRow(l.LeadId);

            var day = l.CreatedTime.Length >= 10 ? l.CreatedTime.Substring(0, 10) : "";
            if (day.Length == 10 && !string.IsNullOrEmpty(l.LeadId))
            {
                var d = daily.TryGetValue(day, out var cur)
                    ? cur
                    : (0L, 0L, 0L, 0, new HashSet<string>(StringComparer.Ordinal));
                d.Item5.Add(l.LeadId);
                daily[day] = d;
            }

            if (!string.IsNullOrEmpty(l.LeadId))
            {
                var pkey = LeadPlatformName(l.Platform);
                var pv = byPlatform.TryGetValue(pkey, out var p)
                    ? p
                    : (0L, 0L, 0, new HashSet<string>(StringComparer.Ordinal));
                pv.Item4.Add(l.LeadId);
                byPlatform[pkey] = pv;
            }
        }

        // ── 6) Daraxt ────────────────────────────────────────────────────────────────────
        var campaignNodes = campaigns
            .Select(kv => BuildNode(LevelCampaign, kv.Key, kv.Value, nameById, statusById, outcome, offset,
                Children(kv.Key, adsets, ads, parentById, nameById, statusById, outcome, offset)))
            .OrderByDescending(n => n.SpendMinor)
            .ThenByDescending(n => n.CrmLeads)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        if (campaignNodes.Count > MaxCampaigns)
        {
            notes.Add($"Kampaniyalar {campaignNodes.Count} ta — jadvalda eng ko'p xarajat qilgan "
                      + $"{MaxCampaigns} tasi ko'rsatildi. Yuqoridagi jamlanma BARCHASI bo'yicha.");
            campaignNodes = campaignNodes.Take(MaxCampaigns).ToList();
        }

        var totals = BuildNode(LevelTotal, "", total, nameById, statusById, outcome, offset, []);

        var dailyRows = daily
            .Where(kv => kv.Key.CompareTo(from) >= 0 && kv.Key.CompareTo(to) <= 0)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new IgRoiDayDto(kv.Key, kv.Value.Spend, kv.Value.Impressions,
                                          kv.Value.Clicks, kv.Value.MetaLeads, kv.Value.Leads.Count))
            .ToList();

        var platformRows = byPlatform
            .OrderByDescending(kv => kv.Value.Spend)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new IgRoiPlatformDto(kv.Key, kv.Value.Spend, kv.Value.Impressions,
                                               kv.Value.MetaLeads, kv.Value.Leads.Count))
            .ToList();

        // ── 7) Ogohlantirishlar — hisobotni NOTO'G'RI o'qishdan saqlaydi ─────────────────
        notes.Add("Daromad — shu davrda kelgan lidlarning BUTUN UMR bo'yicha o'quv to'lovi; "
                  + "xarajat esa faqat tanlangan oraliqda. Ikkalasi bir xil davrni bildirmaydi.");
        notes.Add("Qamrov TAXMINIY: Meta kunlar va platformalar bo'yicha noyob odamlarni "
                  + "dedup qilmaydi, shuning uchun \"kamida\" va \"ko'pi bilan\" chegaralari beriladi.");
        if (totals.MetaLeads != totals.CrmLeads)
            notes.Add($"Meta lidlari {totals.MetaLeads} ta, CRM lidlari {totals.CrmLeads} ta. "
                      + "Farq odatiy: telefon dublikati, 90 kunlik oyna yoki token xatosi.");
        if (totals.CrmLeadsDeleted > 0)
            notes.Add($"{totals.CrmLeadsDeleted} ta lid CRM'dan o'chirilgan — sanoqda qoldi, "
                      + "lekin ularning natijasini ko'rib bo'lmaydi.");
        if (!string.IsNullOrEmpty(account.Currency)
            && !string.Equals(account.Currency, "UZS", StringComparison.OrdinalIgnoreCase))
            notes.Add($"⚠️ Reklama akkaunti valyutasi — {account.Currency.ToUpperInvariant()}, "
                      + "CRM to'lovlari esa so'mda. ROI va CAC to'g'ridan-to'g'ri taqqoslanmaydi.");
        if (!string.IsNullOrEmpty(account.TimezoneName))
            notes.Add($"Xarajat sanasi reklama akkaunti vaqt zonasida ({account.TimezoneName}), "
                      + "lidlar esa markaz vaqtida — chegaradagi kunlarda bir kunlik siljish bo'lishi mumkin.");

        return new IgRoiReportDto(
            Connected: true,
            AdAccountId: account.AdAccountId,
            AdAccountName: account.Name ?? "",
            Currency: account.Currency ?? "",
            CurrencyOffset: offset,
            TimezoneName: account.TimezoneName ?? "",
            From: from, To: to, Platform: pf, CampaignId: camp,
            LastSyncAt: account.LastSyncAt ?? "",
            LastError: account.LastError ?? "",
            InsightLevel: level,
            Totals: totals,
            Daily: dailyRows,
            Platforms: platformRows,
            Campaigns: campaignNodes,
            Notes: notes,
            AttributionSetting: string.Join(" · ", attributionSettings));
    }

    // ───────────────────────── Yordamchilar ─────────────────────────

    /// <summary>Tugunning KAMPANIYA id'si (o'zi kampaniya bo'lsa — o'zi). Ota topilmasa
    /// bo'sh satr: bunday xarajat "biriktirilmagan" qatorga tushadi va JAMLANMADAN
    /// tushib qolmaydi.</summary>
    private static string CampaignOf(string id, string level, Dictionary<string, string> parents)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (string.Equals(level, LevelCampaign, StringComparison.OrdinalIgnoreCase)) return id;
        if (string.Equals(level, LevelAdset, StringComparison.OrdinalIgnoreCase))
            return parents.GetValueOrDefault(id, "");
        // ad → adset → campaign
        var adset = parents.GetValueOrDefault(id, "");
        return adset.Length == 0 ? "" : parents.GetValueOrDefault(adset, "");
    }

    /// <summary>E'lon darajasidagi tugunning ADSET id'si (boshqa darajada — bo'sh).</summary>
    private static string ParentIn(string id, string level, string wanted, Dictionary<string, string> parents)
    {
        if (string.Equals(level, LevelAd, StringComparison.OrdinalIgnoreCase)
            && string.Equals(wanted, LevelAdset, StringComparison.OrdinalIgnoreCase))
            return parents.GetValueOrDefault(id, "");
        return "";
    }

    private static List<IgRoiNodeDto> Children(
        string campaignId, Dictionary<string, Agg> adsets, Dictionary<string, Agg> ads,
        Dictionary<string, string> parents, Dictionary<string, string> names,
        Dictionary<string, string> statuses, LeadOutcome outcome, int offset)
    {
        var rows = new List<IgRoiNodeDto>();
        foreach (var kv in adsets)
        {
            if (parents.GetValueOrDefault(kv.Key, "") != campaignId) continue;

            var adNodes = ads
                .Where(a => parents.GetValueOrDefault(a.Key, "") == kv.Key)
                .Select(a => BuildNode(LevelAd, a.Key, a.Value, names, statuses, outcome, offset, []))
                .OrderByDescending(n => n.SpendMinor)
                .ThenBy(n => n.Name, StringComparer.Ordinal)
                .Take(MaxChildren)
                .ToList();

            rows.Add(BuildNode(LevelAdset, kv.Key, kv.Value, names, statuses, outcome, offset, adNodes));
        }

        return rows
            .OrderByDescending(n => n.SpendMinor)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .Take(MaxChildren)
            .ToList();
    }

    /// <summary>
    /// Xom yig'indidan hisobot qatorini yasaydi: konversiya, daromad, CPL/CAC/ROI.
    ///
    /// <para>⚠️ <b>DAROMAD O'QUVCHI bo'yicha DEDUP qilinadi</b>, konversiya esa LID bo'yicha:
    /// bir odam ikki marta ariza qoldirsa CRM'da ikki lid bo'lib, ikkalasi ham BITTA
    /// o'quvchiga ulanishi mumkin. Pulni ikki marta qo'shish "daromad ikki barobar"
    /// degan yolg'on beradi; "nechta lid to'lov qildi" esa sotuv voronkasining o'lchovi va
    /// u lid darajasida qoladi.</para>
    ///
    /// <para>⚠️ To'liq qaytarilgan to'lov (sof summa ≤ 0) daromadga <b>MANFIY qo'shilmaydi</b>
    /// (<c>Math.Max(0, …)</c>) — bir odamning vozvrati boshqa kampaniyaning daromadini
    /// "yeb qo'yardi" (`LeadFormService.Funnel` bilan bir xil qoida).</para>
    /// </summary>
    private static IgRoiNodeDto BuildNode(
        string level, string id, Agg agg,
        Dictionary<string, string> names, Dictionary<string, string> statuses,
        LeadOutcome outcome, int offset, IReadOnlyList<IgRoiNodeDto> children)
    {
        var converted = 0;
        var paid = 0;
        var deleted = 0;
        var revenue = 0m;
        var countedStudents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var leadId in agg.LeadIds)
        {
            if (outcome.IsDeletedLead(leadId)) deleted++;
            var studentId = outcome.StudentOf(leadId);
            if (studentId is not null) converted++;
            if (outcome.HasPaid(leadId)) paid++;
            if (studentId is not null && countedStudents.Add(studentId))
                revenue += Math.Max(0m, outcome.PaidTotal(leadId));
        }

        var revenueMinor = MetaCurrency.ToMinor(revenue, offset);
        var crmLeads = agg.LeadIds.Count;

        // Nomi topilmasa id'ning O'ZI ko'rsatiladi: sun'iy "Noma'lum kampaniya" matni bazadagi
        // haqiqiy tugundan ajratib bo'lmas edi, id esa Ads Manager'da qidirsa bo'ladigan qiymat.
        var name = id.Length == 0
            ? ""
            : names.TryGetValue(id, out var n) && n.Length > 0 ? n : id;

        return new IgRoiNodeDto(
            Level: level,
            Id: id,
            Name: name,
            Status: id.Length == 0 ? "" : statuses.GetValueOrDefault(id, ""),
            SpendMinor: agg.Spend,
            Impressions: agg.Impressions,
            Reach: agg.ReachLower,
            ReachUpper: agg.ReachUpper,
            ReachApprox: true,
            Clicks: agg.Clicks,
            LinkClicks: agg.LinkClicks,
            MetaLeads: agg.MetaLeads,
            MsgStarted: agg.MsgStarted,
            AdLeadRows: agg.AdLeadRows,
            CrmLeads: crmLeads,
            CrmLeadsDeleted: deleted,
            CplMinor: CplMinor(agg.Spend, crmLeads),
            Converted: converted,
            Paid: paid,
            RevenueMinor: revenueMinor,
            CacMinor: CacMinor(agg.Spend, paid),
            Roi: RoiRatio(revenueMinor, agg.Spend),
            Children: children);
    }

    /// <summary>Akkaunt ulanmagan holat — 200, lekin BO'SH va tushunarli.</summary>
    private static IgRoiReportDto EmptyReport(string from, string to, string platform, string campaignId) =>
        new(
            Connected: false,
            AdAccountId: "", AdAccountName: "", Currency: "",
            CurrencyOffset: MetaCurrency.DefaultOffset, TimezoneName: "",
            From: from, To: to, Platform: platform, CampaignId: campaignId,
            LastSyncAt: "", LastError: "",
            InsightLevel: "",
            Totals: EmptyNode(),
            Daily: [], Platforms: [], Campaigns: [],
            Notes: ["Reklama akkaunti ulanmagan — Sozlamalar bo'limidan ulang."],
            AttributionSetting: "");

    /// <summary>Nol qiymatli jamlanma qatori (bo'sh holat uchun).</summary>
    private static IgRoiNodeDto EmptyNode() =>
        new(LevelTotal, "", "", "", 0, 0, 0, 0, true, 0, 0, 0, 0, 0, 0, 0,
            null, 0, 0, 0, null, null, []);
}
