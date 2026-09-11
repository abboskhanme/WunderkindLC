using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → CAPI (Meta Conversions API) — "lid sifatini Meta'ga qaytarish" ekranining API'si.
///
/// <para><b>Nega alohida partial fayl:</b> <c>InstagramController</c> allaqachon olti ekranni
/// (agent, inbox, qoidalar, bilim bazasi, analitika, reklama lidlari) boqadi. CAPI esa mustaqil
/// modul — o'z bayrog'i, o'z tokeni (Dataset tokeni, Page tokeni EMAS) va o'z navbati bilan.
/// Marshrut prefiksi, <c>[Authorize]</c> va sinf darajasidagi
/// <c>[AdminPerm("marketing", ReadRequiresPerm = true)]</c> asosiy qismdan MEROS bo'lib keladi.</para>
///
/// <para>🔴 <b>MAXFIYLIK CHEGARASI:</b> Dataset ID ham, CAPI tokeni ham javobga
/// <b>QIYMAT</b> sifatida tushmaydi — faqat "sozlangan / sozlanmagan" bayrog'i. Navbat
/// ro'yxatida <c>PayloadJson</c> ham berilmaydi (uzun, va uni ro'yxatga chiqarishning
/// diagnostik foydasi yo'q — kerakli hammasi <c>eventName</c>/<c>status</c>/<c>error</c> da).</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Navbat ro'yxatining sahifa hajmi (reklama lidlari ro'yxati bilan bir xil).</summary>
    private const int CapiPageSize = 100;

    /// <summary>Navbat holatlari filtri uchun ruxsat etilgan qiymatlar.</summary>
    private static readonly string[] CapiStatuses =
    [
        MetaCapiService.StatusPending, MetaCapiService.StatusSent,
        MetaCapiService.StatusFailed, MetaCapiService.StatusSkipped,
    ];

    // =============================================================================================
    //  HOLAT VA SOZLAMALAR
    // =============================================================================================

    /// <summary>
    /// CAPI DIAGNOSTIKASI — "nega hodisa ketmayapti" savolining barcha sabablari bitta ekranda:
    /// modul yoqilganmi, Dataset ID va token kiritilganmi, navbatda nechta qator turibdi,
    /// nechtasi yuborilgan/yiqilgan/o'tkazib yuborilgan, oxirgi yuborish qachon bo'lgan va
    /// oxirgi xato nima edi.
    /// <para>⚠️ Dataset ID va token QIYMATI qaytmaydi — faqat bayroq.</para>
    /// </summary>
    [HttpGet("capi/status")]
    public async Task<ActionResult<IgCapiStatusDto>> CapiStatus(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);

        // Oxirgi muvaffaqiyatli yuborish — `MaxAsync` EMAS: bo'sh jadvalda u NULL qaytarib
        // istisno berardi (`SentAt` — bo'sh satrli, nullable bo'lmagan ustun).
        var lastSentAt = await db.IgCapiEvents.AsNoTracking()
            .Where(e => e.SentAt != "")
            .OrderByDescending(e => e.SentAt)
            .Select(e => e.SentAt)
            .FirstOrDefaultAsync(ct) ?? "";

        // Oxirgi xato — YARATILISH vaqti bo'yicha eng yangisi (yuborilmagan qatorda `SentAt`
        // bo'sh, ya'ni u bo'yicha tartiblab bo'lmasdi).
        var lastError = await db.IgCapiEvents.AsNoTracking()
            .Where(e => e.Error != "")
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Error)
            .FirstOrDefaultAsync(ct) ?? "";

        return new IgCapiStatusDto(
            Enabled: meta?.InstagramCapiEnabled ?? false,
            DatasetIdSet: !string.IsNullOrWhiteSpace(meta?.InstagramCapiDatasetId),
            TokenSet: !string.IsNullOrWhiteSpace(meta?.InstagramCapiToken),
            StageQualified: meta?.InstagramCapiStageQualified ?? "",
            StageWon: meta?.InstagramCapiStageWon ?? "",
            Pending: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusPending, ct),
            Sent: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusSent, ct),
            Failed: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusFailed, ct),
            Skipped: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusSkipped, ct),
            LastSentAt: lastSentAt,
            LastError: lastError);
    }

    /// <summary>
    /// CAPI sozlamalarini saqlash.
    ///
    /// <para>⚠️ <b>Token BO'SH kelsa mavjudi SAQLANADI</b> — forma tokenni hech qachon
    /// ko'rsatmaydi (`ads/page` bilan bir xil qoida), ya'ni faqat Dataset ID yoki bosqich nomi
    /// tahrirlanganda tokenni qayta yozdirish shart emas.</para>
    ///
    /// <para>🔴 <c>event_name</c> — ERKIN MATN va u Events Manager'dagi bosqich nomi bilan
    /// AYNAN bir xil bo'lishi kerak, shuning uchun nomlar sozlamada. Bo'sh yuborilsa
    /// <c>CenterMeta</c> dagi default nom qoladi: bo'sh <c>event_name</c> bilan ketgan so'rovni
    /// Meta rad etardi.</para>
    /// </summary>
    [HttpPut("capi/settings")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgCapiStatusDto>> CapiSaveSettings(
        IgCapiSettingsPayload payload, CancellationToken ct)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }

        var token = (payload.Token ?? "").Trim();

        meta.InstagramCapiEnabled = payload.Enabled;
        // ⚠️ Dataset ID ham TOKEN BILAN BIR XIL qoidada: BO'SH kelsa mavjudi SAQLANADI.
        // Sabab: qiymat javobga tushmaydi (faqat `DatasetIdSet` bayrog'i), ya'ni forma har safar
        // BO'SH ochiladi. Shartsiz yozilsa, faqat toggle'ni o'zgartirib "Saqlash" bosgan admin
        // Dataset ID'ni BILMASDAN o'chirib qo'yardi va CAPI jimgina ishlamay qolardi.
        var datasetId = (payload.DatasetId ?? "").Trim();
        if (datasetId.Length > 0) meta.InstagramCapiDatasetId = datasetId;
        if (token.Length > 0) meta.InstagramCapiToken = token;
        if (!string.IsNullOrWhiteSpace(payload.StageQualified))
            meta.InstagramCapiStageQualified = payload.StageQualified.Trim();
        if (!string.IsNullOrWhiteSpace(payload.StageWon))
            meta.InstagramCapiStageWon = payload.StageWon.Trim();

        // ⚠️ Auditga TOKEN yozilmaydi (audit.md §1). Dataset ID — sir emas, oddiy identifikator
        //    (Page ID bilan bir xil maqom) va u yoziladi: "qaysi datasetga ulandik" savoli
        //    tarixdan javobsiz qolmasin.
        audit.Record(AuditEntity, "capi", "update",
            "CAPI (lid sifatini Meta'ga qaytarish) sozlamalari o'zgartirildi — modul: "
            + (meta.InstagramCapiEnabled ? "YOQILGAN" : "O'CHIRILGAN")
            + ", Dataset ID: " + (meta.InstagramCapiDatasetId.Length > 0 ? meta.InstagramCapiDatasetId : "kiritilmagan")
            + ", token: " + (meta.InstagramCapiToken.Length > 0 ? "sozlangan" : "sozlanmagan")
            + $", bosqichlar: \"{meta.InstagramCapiStageQualified}\" / \"{meta.InstagramCapiStageWon}\"");
        await db.SaveChangesAsync(ct);

        return await CapiStatus(ct);
    }

    // =============================================================================================
    //  NAVBAT
    // =============================================================================================

    /// <summary>
    /// Hodisalar NAVBATI — sahifalangan ro'yxat + jamlanma.
    ///
    /// <para>⚠️ Jamlanma <b>BUTUN jadval</b> bo'yicha, ro'yxatning ko'rinadigan qismidan emas
    /// va <paramref name="status"/> filtriga ham bog'liq emas: u "navbat qanday holatda" degan
    /// savolga javob beradi va filtr o'zgarganda sakramasligi kerak.</para>
    ///
    /// <para>⚠️ <c>PayloadJson</c> javobga TUSHMAYDI — uzun va ro'yxat uchun keraksiz.</para>
    /// </summary>
    /// <param name="status">`pending` · `sent` · `failed` · `skipped`; boshqasi — hammasi.</param>
    [HttpGet("capi/events")]
    public async Task<ActionResult<IgCapiEventListDto>> CapiEvents(
        [FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var query = db.IgCapiEvents.AsNoTracking().AsQueryable();

        // Noma'lum qiymat JIM e'tiborsiz qoldiriladi (klientdagi xato kalit tufayli ro'yxat
        // butunlay bo'shab qolmasin) — `ContactController` jurnalidagi bilan bir xil siyosat.
        if (!string.IsNullOrWhiteSpace(status) && CapiStatuses.Contains(status))
            query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        if (page < 1) page = 1;

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * CapiPageSize)
            .Take(CapiPageSize)
            .Select(e => new IgCapiEventDto(
                e.Id, e.LeadId, e.LeadgenId, e.EventName, e.EventId, e.Status,
                e.Attempts, e.Error, e.EventTime, e.CreatedAt, e.SentAt))
            .ToListAsync(ct);

        var totals = new IgCapiTotalsDto(
            Total: await db.IgCapiEvents.CountAsync(ct),
            Pending: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusPending, ct),
            Sent: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusSent, ct),
            Failed: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusFailed, ct),
            Skipped: await db.IgCapiEvents.CountAsync(e => e.Status == MetaCapiService.StatusSkipped, ct));

        return new IgCapiEventListDto(items, total, page, CapiPageSize, totals);
    }

    /// <summary>
    /// QO'LDA skan + yuborish ("kutmasdan hozir yubor" tugmasi).
    ///
    /// <para>Worker buni kuniga bir marta o'zi bajaradi; bu endpoint sozlashdan keyin natijani
    /// DARHOL ko'rish uchun (aks holda admin "ishladimi?" degan savol bilan ertagacha
    /// kutib qolardi).</para>
    ///
    /// <para>⚠️ Servis <c>[FromServices]</c> orqali olinadi — controller konstruktori
    /// o'zgartirilmaydi (u boshqa besh ekran bilan umumiy).</para>
    ///
    /// <para>Xato bo'lsa HTTP baribir <b>200</b>: sabab (masalan "modul o'chirilgan") ekranda
    /// matn bo'lib ko'rsatiladi va navbat sonlari baribir yangilanadi.</para>
    /// </summary>
    [HttpPost("capi/send")]
    [AdminPerm("marketing.settings")]
    public async Task<ActionResult<IgCapiSendResultDto>> CapiSend(
        [FromServices] MetaCapiService capi, CancellationToken ct)
    {
        var (ok, created, sent, error) = await capi.ScanAndSendAsync(ct);

        audit.Record(AuditEntity, "capi", "update",
            ok
                ? $"CAPI hodisalari qo'lda yuborildi — yangi: {created}, yuborilgan: {sent}"
                : $"CAPI hodisalarini qo'lda yuborishga urinildi — bajarilmadi: {error}");
        await db.SaveChangesAsync(ct);

        return new IgCapiSendResultDto(ok, created, sent, error);
    }
}

// =================================================================================================
//  CAPI DTO'LARI — ⚠️ hech qaysisida token, Dataset ID qiymati yoki xom PII YO'Q.
// =================================================================================================

/// <summary>CAPI diagnostikasi. ⚠️ <paramref name="DatasetIdSet"/> va <paramref name="TokenSet"/> —
/// faqat BAYROQ; qiymatlar hech qachon qaytmaydi.</summary>
public record IgCapiStatusDto(
    bool Enabled, bool DatasetIdSet, bool TokenSet,
    string StageQualified, string StageWon,
    int Pending, int Sent, int Failed, int Skipped,
    string LastSentAt, string LastError);

/// <summary><paramref name="Token"/> BO'SH yuborilsa mavjud token saqlanadi (forma tokenni
/// hech qachon ko'rsatmaydi). Bosqich nomlari bo'sh bo'lsa oldingilari qoladi.</summary>
public record IgCapiSettingsPayload(
    bool Enabled, string? DatasetId, string? Token, string? StageQualified, string? StageWon);

/// <summary>Navbat qatori. ⚠️ <c>PayloadJson</c> ATAYIN yo'q — uzun va ro'yxat uchun keraksiz.
/// <paramref name="EventId"/> esa QAYTADI: Meta qo'llab-quvvatlash xizmati "hodisa ko'rinmayapti"
/// savolida aynan shu kalitni so'raydi.</summary>
public record IgCapiEventDto(
    string Id, string LeadId, string LeadgenId, string EventName, string EventId,
    string Status, int Attempts, string Error, string EventTime, string CreatedAt, string SentAt);

/// <summary>⚠️ Sonlar BUTUN navbat bo'yicha — filtr va sahifaga bog'liq EMAS.</summary>
public record IgCapiTotalsDto(int Total, int Pending, int Sent, int Failed, int Skipped);

public record IgCapiEventListDto(
    List<IgCapiEventDto> Items, int Total, int Page, int PageSize, IgCapiTotalsDto Totals);

/// <summary><paramref name="Ok"/> false bo'lsa <paramref name="Error"/> da o'zbekcha sabab
/// (HTTP baribir 200).</summary>
public record IgCapiSendResultDto(bool Ok, int Created, int Sent, string Error);
