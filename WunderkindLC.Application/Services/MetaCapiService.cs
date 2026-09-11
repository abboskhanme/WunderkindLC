using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CAPI (Meta Conversions API) NAVBATI — "bu lid o'quvchi bo'ldi va PUL to'ladi" xabarini
/// Meta'ga qaytaradi. Meta hozir faqat "lid keldi"ni biladi; sifat qaytarilsa u HAQIQIY
/// mijoz keltiradigan auditoriyaga optimallashadi.
///
/// <para><b>Ikki bosqich, ikkita metod:</b></para>
/// <list type="number">
///   <item><see cref="ScanAndSendAsync"/> — KUNLIK skan: reklama lidlarining hozirgi holatini
///     hisoblab, hali yuborilmagan holat uchun navbatga qator qo'shadi va navbatni yuboradi;</item>
///   <item><see cref="SendPendingAsync"/> — faqat navbatni yuboradi (yangi qator yaratmaydi).</item>
/// </list>
///
/// <para>🔴 <b>YANGI HOOK YOZILMAYDI</b> (§7.6). <c>Lead.Stage</c> o'zgarishini ushlash uchun
/// hodisa tinglovchisi qo'shish vasvasasi bor, lekin lid holati bir necha joydan o'zgaradi
/// (kanban, konvertatsiya, kassa) va bitta joyi tushib qolsa hodisa JIMGINA yo'qolardi.
/// Kunlik skan esa "hozirgi holat" ni qayta hisoblaydi — o'tkazib yuborilgan o'zgarish
/// keyingi kuni o'z-o'zidan tuziladi.</para>
///
/// <para><b>DARVOZA:</b> <c>CenterMeta.InstagramCapiEnabled == false</c> yoki Dataset ID /
/// token bo'sh bo'lsa tashqariga <b>HECH QANDAY so'rov ketmaydi</b> va navbatga qator ham
/// yozilmaydi (modul o'chiq — tashqariga hech narsa chiqmaydi qoidasi, <see cref="MetaLeadgenService"/>
/// bilan bir xil).</para>
///
/// <para>🔴 <b>MAXFIYLIK:</b> <c>IgCapiEvent.PayloadJson</c> ga xom telefon/email HECH QACHON
/// yozilmaydi — payload faqat <see cref="MetaCapiPayload.BuildEvent(MetaCapiEventInput)"/>
/// orqali quriladi va u <see cref="MetaCapiUserData"/> (hashlangan record) dan boshqasini
/// qabul qilmaydi.</para>
///
/// <para>DI: <c>builder.Services.AddScoped&lt;MetaCapiService&gt;();</c>
/// (+ <c>builder.Services.AddHttpClient&lt;MetaCapiApi&gt;();</c>)</para>
/// </summary>
public sealed class MetaCapiService(
    IAppDbContext db,
    MetaCapiApi api,
    ILogger<MetaCapiService> logger)
{
    /* ═════════════════════════ Konstantalar ═════════════════════════ */

    /// <summary>Navbat holatlari. ⚠️ <c>IgConst.Ev*</c> dan ATAYIN ayri: u yerda muvaffaqiyat
    /// <c>done</c>, bu yerda <c>sent</c> (entity izohi va UI shu nomlarni kutadi).</summary>
    public const string StatusPending = "pending";
    public const string StatusSent = "sent";
    public const string StatusFailed = "failed";
    public const string StatusSkipped = "skipped";

    /// <summary>Bir qatorga nechta urinish. Undan keyin <see cref="StatusFailed"/> —
    /// aks holda doim yiqiladigan qator har ishga tushishda so'rov sarflab, cheksiz aylanardi.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Skan oynasi (kun) — bundan eski reklama lidi umuman ko'rilmaydi.
    ///
    /// <para>Meta "Conversion Leads" optimizatsiyasi maqsadli bosqich <b>28 kun ichida</b>
    /// sodir bo'lishini kutadi (§7.1). 90 kun — shu talabga uch barobar zaxira: bir yillik
    /// arxiv har kuni qayta skanerlanib, bazani bekorga o'qib chiqmasin.</para>
    /// </summary>
    public const int ScanWindowDays = 90;

    /// <summary>Bitta ishga tushishda yuboriladigan qatorlar chegarasi (5 paket).</summary>
    public const int MaxPerRun = 5 * MetaCapiPayload.MaxEventsPerRequest;

    /// <summary>Sozlama bo'sh qolsa ishlatiladigan nomlar (<c>CenterMeta</c> dagi default bilan bir xil).</summary>
    private const string DefaultQualified = "Sifatli lid";
    private const string DefaultWon = "To'lov qildi";

    /// <summary>
    /// "Sifatli" deb hisoblanadigan kanban bosqichlari — <b>nom bo'yicha</b>.
    ///
    /// <para>⚠️ Bosqichlar admin tomonidan erkin yaratiladi/nomlanadi (<c>LeadStage</c> da
    /// "tur" ustuni YO'Q), ya'ni id bo'yicha bog'lab bo'lmaydi. Standart voronka
    /// ("Yangi · Bog'lanildi · Sinov darsi · O'ylanmoqda · Aylantirildi") shu kalitlar bilan
    /// to'g'ri tasniflanadi; markaz bosqichni boshqacha nomlagan bo'lsa hodisa baribir
    /// <c>ConvertedStudentId</c> orqali yuboriladi — ya'ni ro'yxat "qo'shimcha", yagona
    /// shart emas.</para>
    /// </summary>
    private static readonly string[] QualifiedStageKeywords =
        ["sifatli", "sinov", "trial", "qualified", "aylantir", "convert"];

    /* ═════════════════════════ Public API ═════════════════════════ */

    /// <summary>
    /// KUNLIK SKAN + YUBORISH. Worker shuni kuniga bir marta chaqiradi (§7.1: "kuniga kamida
    /// bir marta yuklash").
    /// </summary>
    /// <returns><c>Created</c> — navbatga qo'shilgan yangi qatorlar, <c>Sent</c> — Meta'ga
    /// muvaffaqiyatli ketgan qatorlar. <c>Ok=false</c> bo'lsa <c>Error</c> o'zbekcha sabab.</returns>
    public async Task<(bool Ok, int Created, int Sent, string Error)> ScanAndSendAsync(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var gate = GateError(meta);
        if (gate.Length > 0) return (false, 0, 0, gate);

        var created = await ScanAsync(meta!, ct);
        var (ok, sent, err) = await SendCoreAsync(meta!, ct);
        return (ok, created, sent, err);
    }

    /// <summary>
    /// Navbatdagi (<see cref="StatusPending"/>) qatorlarni yuboradi — YANGI qator yaratmaydi.
    /// Xato bo'lgan paket <b>yo'qolmaydi</b>: qatorlar <c>pending</c> bo'lib qoladi.
    /// </summary>
    public async Task<(bool Ok, int Sent, string Error)> SendPendingAsync(CancellationToken ct)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var gate = GateError(meta);
        if (gate.Length > 0) return (false, 0, gate);

        return await SendCoreAsync(meta!, ct);
    }

    /// <summary>
    /// Kanban bosqichi "sifatli lid" ma'nosini beradimi — SOF funksiya (testlangan).
    /// </summary>
    public static bool IsQualifiedStage(string? stageTitle)
    {
        if (string.IsNullOrWhiteSpace(stageTitle)) return false;

        var title = stageTitle.ToLowerInvariant();
        foreach (var key in QualifiedStageKeywords)
            if (title.Contains(key, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    /// Modul darvozasi — bo'sh satr bo'lsa ishlash mumkin. Sabablar AYRI-AYRI: "ishlamayapti"
    /// deb qarab turgan admin qaysi maydon to'ldirilmaganini darhol ko'rsin.
    /// </summary>
    public static string GateError(CenterMeta? meta)
    {
        if (meta is null || !meta.InstagramCapiEnabled)
            return "CAPI moduli o'chirilgan — Marketing → Sozlamalar bo'limida yoqing.";
        if (string.IsNullOrWhiteSpace(meta.InstagramCapiDatasetId))
            return "Dataset ID kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.";
        if (string.IsNullOrWhiteSpace(meta.InstagramCapiToken))
            return "CAPI tokeni kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.";

        return "";
    }

    /* ═════════════════════════ 1) SKAN ═════════════════════════ */

    /// <summary>
    /// Reklama lidlarining HOZIRGI holatini hisoblab, hali navbatga tushmagan hodisalarni
    /// yaratadi (§7.6 jadvali).
    ///
    /// <para>⚠️ <b>Lid YARATILGANI uchun hodisa YO'Q</b> — Meta lidni o'zi qabul qilgan va
    /// buni allaqachon biladi; qaytarilsa konversiya ikkilanardi.</para>
    /// </summary>
    private async Task<int> ScanAsync(CenterMeta meta, CancellationToken ct)
    {
        var cutoff = AppClock.Now.AddDays(-ScanWindowDays).ToString("yyyy-MM-ddTHH:mm:ss");

        // ⚠️ FAQAT reklama formasidan kelgan lidlar: `lead_id` usiz Meta hodisani hech qanday
        //    e'longa bog'lay olmaydi (DM/izohdan kelgan lid bu navbatga umuman tushmaydi).
        // ⚠️ `CreatedTime` bo'sh/buzuq bo'lsa `ReceivedAt` bo'yicha ham tekshiriladi —
        //    aks holda vaqti yozilmagan lid jimgina tushib qolardi.
        var adLeads = await db.IgAdLeads.AsNoTracking()
            .Where(l => l.LeadId != "" && l.LeadgenId != ""
                        && (l.CreatedTime.CompareTo(cutoff) >= 0 || l.ReceivedAt.CompareTo(cutoff) >= 0))
            .OrderBy(l => l.CreatedTime)
            .ToListAsync(ct);
        if (adLeads.Count == 0) return 0;

        // Bitta CRM lidiga bir necha reklama lidi to'g'ri kelishi mumkin (o'sha odam formani
        // qayta to'ldirgan). FIRST-TOUCH: eng birinchisi olinadi — `LeadIntake` dagi bilan bir
        // xil qoida, ya'ni konversiya BIRINCHI e'longa yoziladi.
        var byLead = new Dictionary<string, IgAdLead>(StringComparer.Ordinal);
        foreach (var l in adLeads) byLead.TryAdd(l.LeadId, l);

        var leadIds = byLead.Keys.ToList();
        var outcome = await LeadOutcome.BuildAsync(db, leadIds);

        // ── DEDUP (birinchi qavat) ──
        // Kalit — (lid, hodisa nomi). ⚠️ `EventId` ga TAYANIB bo'lmaydi: unda vaqt bor va u
        // har kuni boshqacha chiqadi, ya'ni unikal indeks bir xil holatni har kuni qayta
        // yozilishidan SAQLAMASDI. Unikal indeks ikkinchi qavat bo'lib qoladi (poyga uchun).
        var existing = (await db.IgCapiEvents.AsNoTracking()
                .Where(e => leadIds.Contains(e.LeadId))
                .Select(e => new { e.LeadId, e.EventName })
                .ToListAsync(ct))
            .Select(e => Key(e.LeadId, e.EventName))
            .ToHashSet(StringComparer.Ordinal);

        var qualifiedName = StageName(meta.InstagramCapiStageQualified, DefaultQualified);
        var wonName = StageName(meta.InstagramCapiStageWon, DefaultWon);

        var now = AppClock.Now;
        var nowUnix = MetaCapiPayload.ToUnix(now);
        var created = 0;

        foreach (var leadId in leadIds)
        {
            var ad = byLead[leadId];

            // ── SIFATLI LID ──
            // Ikki manba: konvertatsiya (`ConvertedStudentId` to'ldi) YOKI kanban bosqichi.
            // Ikkalasi ham BITTA hodisa beradi — Events Manager'da bosqich ham bitta.
            var qualified = outcome.StudentOf(leadId) != null
                            || IsQualifiedStage(outcome.StageOf(leadId).Title);

            if (qualified && existing.Add(Key(leadId, qualifiedName)))
            {
                // ⚠️ VAQT — SKAN VAQTI. Bosqichga o'tishning aniq soati saqlanmaydi (kunlik
                //    skan qoidasining bahosi), ya'ni eng ko'p 24 soat kechikish bo'ladi.
                //    Meta uchun bu ahamiyatsiz, 7 kunlik chegara esa hech qachon buzilmaydi.
                if (await AddAsync(ad, qualifiedName, nowUnix, value: null, now, ct)) created++;
            }

            // ── TO'LOV QILDI ──
            if (outcome.HasPaid(leadId) && existing.Add(Key(leadId, wonName)))
            {
                // ⚠️ VAQT — BIRINCHI TO'LOV SANASI, skan vaqti EMAS: Meta hodisani atributsiya
                //    oynasiga aynan shu vaqt bo'yicha joylashtiradi. Sana 7 kundan eski bo'lsa
                //    qator `skipped` bo'ladi (modul birinchi marta yoqilganda eski to'lovlar
                //    ATAYIN yuborilmaydi — "bugun to'ladi" deb yuborish yolg'on ma'lumot bo'lardi).
                var unix = PaidUnix(outcome.FirstPaidAt(leadId), nowUnix);
                if (await AddAsync(ad, wonName, unix, outcome.PaidTotal(leadId), now, ct)) created++;
            }
        }

        if (created > 0)
            logger.LogInformation("CAPI: navbatga {Count} ta yangi hodisa qo'shildi", created);

        return created;
    }

    /// <summary>Navbatga bitta qator qo'shadi. <c>false</c> — takror (dedup ishladi).</summary>
    private async Task<bool> AddAsync(
        IgAdLead ad, string eventName, long unix, decimal? value, DateTime now, CancellationToken ct)
    {
        // 🔴 Xom telefon/email SHU YERDA hashlanadi va boshqa hech qayerda saqlanmaydi.
        // ⚠️ Ism/familiya (`fn`/`ln`) ATAYIN yuborilmaydi: O'zbekistonda formaga "Familiya Ism"
        //    ham, "Ism Familiya" ham yoziladi va tartibni aniqlashning ishonchli yo'li yo'q.
        //    Noto'g'ri joylashgan `fn`/`ln` moslikni oshirmaydi (ikkalasi ham 0 chiqadi),
        //    lekin Meta hisobotida "sifatsiz integratsiya" bo'lib ko'rinardi. `lead_id`
        //    baribir eng kuchli identifikator (§7.3 misolidagi to'plam — `lead_id` + `ph`).
        var user = MetaCapiUserData.FromRaw(ad.LeadgenId, ad.Phone, ad.Email);
        var input = new MetaCapiEventInput(
            eventName, unix, user, value, MetaCapiPayload.DefaultCurrency);

        var row = new IgCapiEvent
        {
            LeadId = ad.LeadId,
            LeadgenId = ad.LeadgenId,
            EventName = eventName,
            EventId = MetaCapiPayload.EventId(ad.LeadgenId, unix),
            EventTime = MetaCapiPayload.IsoFromUnix(unix),
            Status = StatusPending,
            PayloadJson = MetaCapiPayload.BuildEvent(input),
            CreatedAt = AppClock.Iso(),
        };

        // ⚠️ Eskirgan hodisa NAVBATGA TUSHMAYDI: bitta eski `event_time` BUTUN so'rovni rad
        //    ettiradi. `skipped` — "urinilmadi va urinilmaydi", `failed` dan ATAYIN ayri
        //    (admin xato deb o'ylab muammo izlab yurmasin). Sababi ochiq yoziladi.
        var timeErr = MetaCapiPayload.EventTimeError(unix, now);
        if (timeErr.Length > 0)
        {
            row.Status = StatusSkipped;
            row.Error = timeErr;
        }

        db.IgCapiEvents.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // `IX_IgCapiEvents_EventId` UNIKAL — takror kelishi XATO EMAS (dedupning ikkinchi
            // qavati ishladi). ⚠️ Muvaffaqiyatsiz qator kuzatuvda `Added` bo'lib qolsa keyingi
            // HAR BIR `SaveChanges` yiqilardi; `Remove` esa `Added` yozuvni kuzatuvdan
            // butunlay chiqaradi (EF xulqi) — `Entry()` `IAppDbContext` da yo'q.
            db.IgCapiEvents.Remove(row);
            logger.LogInformation("CAPI: takroriy hodisa tashlandi ({EventId})", row.EventId);
            return false;
        }
    }

    /* ═════════════════════════ 2) YUBORISH ═════════════════════════ */

    private async Task<(bool Ok, int Sent, string Error)> SendCoreAsync(CenterMeta meta, CancellationToken ct)
    {
        var rows = await db.IgCapiEvents
            .Where(e => e.Status == StatusPending && e.Attempts < MaxAttempts)
            .OrderBy(e => e.CreatedAt)
            .Take(MaxPerRun)
            .ToListAsync(ct);
        if (rows.Count == 0) return (true, 0, "");

        var now = AppClock.Now;
        var ready = new List<(IgCapiEvent Row, MetaCapiEventInput Input)>();

        foreach (var row in rows)
        {
            var input = ToInput(row);
            if (input is null)
            {
                // Payload o'qilmadi (bo'sh yoki buzuq) — qayta urinishning ma'nosi yo'q.
                row.Status = StatusFailed;
                row.Error = "Hodisa tanasi o'qilmadi — qator qayta yaratilishi kerak.";
                continue;
            }

            // ⚠️ Vaqt navbatda turganda ESKIRGAN bo'lishi mumkin (yuborish bir necha kun
            //    yiqilib turgan bo'lsa). Bunday qator paketdan CHIQARILADI — aks holda u
            //    o'zi bilan birga qolgan 999 tasini ham yiqitardi.
            var timeErr = MetaCapiPayload.EventTimeError(input.EventTimeUnix, now);
            if (timeErr.Length > 0)
            {
                row.Status = StatusSkipped;
                row.Error = timeErr;
                continue;
            }

            ready.Add((row, input));
        }

        var sent = 0;
        var lastError = "";

        foreach (var chunk in MetaCapiPayload.Chunk(ready))
        {
            var (ok, received, err) = await api.SendAsync(
                meta.InstagramCapiDatasetId, meta.InstagramCapiToken,
                chunk.Select(c => c.Input).ToList(), ct);

            var iso = AppClock.Iso();
            foreach (var (row, _) in chunk)
            {
                row.Attempts++;
                if (ok)
                {
                    row.Status = StatusSent;
                    row.SentAt = iso;
                    row.Error = "";
                }
                else
                {
                    // 🔴 PAKET YO'QOLMAYDI: qator `pending` bo'lib qoladi va keyingi ishga
                    //    tushishda qayta yuboriladi. `event_id` deterministik bo'lgani uchun
                    //    qayta yuborish XAVFSIZ — Meta takrorni o'zi tashlaydi.
                    row.Error = err;
                    if (row.Attempts >= MaxAttempts) row.Status = StatusFailed;
                }
            }

            if (ok) sent += chunk.Count;
            else lastError = err;

            logger.LogInformation(
                "CAPI: {Count} ta hodisa yuborildi — {Result} (qabul qilindi: {Received})",
                chunk.Count, ok ? "muvaffaqiyatli" : "xato", received);
        }

        await db.SaveChangesAsync(ct);
        return (lastError.Length == 0, sent, lastError);
    }

    /// <summary>
    /// Saqlangan qatordan yuboriladigan hodisani TIKLAYDI.
    ///
    /// <para>⚠️ Manba — <c>PayloadJson</c>, xom ma'lumot EMAS: navbatga tushgan payni Meta'ga
    /// AYNAN yuboramiz. Lidning telefoni/summasi orada o'zgargan bo'lsa ham hodisa o'zgarmaydi
    /// (aks holda <c>event_id</c> bir xil bo'lib, mazmuni boshqa bo'lgan hodisa ketardi).</para>
    ///
    /// <para>⚠️ <c>event_id</c> qayta hisoblanadi va <c>row.EventId</c> bilan AYNAN mos tushadi:
    /// ikkalasi ham <c>MetaCapiPayload.EventId(leadgenId, unix)</c> dan, ISO ⇄ unix aylanishi
    /// esa soniya aniqligida teskarilanadi.</para>
    /// </summary>
    private static MetaCapiEventInput? ToInput(IgCapiEvent row)
    {
        if (!DateTime.TryParse(row.EventTime, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var eventTime))
            return null;

        var unix = MetaCapiPayload.ToUnix(eventTime);

        string phoneHash = "", emailHash = "", currency = "";
        decimal? value = null;

        try
        {
            using var doc = JsonDocument.Parse(row.PayloadJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("user_data", out var user))
            {
                phoneHash = First(user, "ph");
                emailHash = First(user, "em");
            }

            if (root.TryGetProperty("custom_data", out var custom))
            {
                if (custom.TryGetProperty("value", out var v)
                    && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
                    value = d;
                if (custom.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String)
                    currency = c.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            return null;
        }

        var data = new MetaCapiUserData(row.LeadgenId, PhoneHash: phoneHash, EmailHash: emailHash);
        if (!data.HasAnyIdentifier) return null;

        return new MetaCapiEventInput(row.EventName, unix, data, value, currency);
    }

    /* ═════════════════════════ Kichik yordamchilar ═════════════════════════ */

    /// <summary>Dedup kaliti: (lid, hodisa nomi). <c>\n</c> — nomda uchramaydigan ajratgich.</summary>
    private static string Key(string leadId, string eventName) => leadId + "\n" + eventName;

    /// <summary>Sozlama bo'sh saqlangan bo'lsa default nom (bo'sh <c>event_name</c> ni Meta rad etadi).</summary>
    private static string StageName(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    /// <summary>
    /// "yyyy-MM-dd" → unix (kun boshi, Toshkent vaqti).
    ///
    /// <para>⚠️ Kun BOSHI olinadi — to'lovning aniq soati <c>FinanceTransaction.Date</c> da yo'q.
    /// Kun oxiri olinsa bugungi to'lov KELAJAKDA turib qolib, Meta uni rad etardi.</para>
    /// <para>Sana o'qilmasa — skan vaqti (to'lov aniq bo'lgan, faqat sanasi buzuq: hodisani
    /// butunlay yo'qotgandan ko'ra bugungi sana bilan yuborish yaxshiroq).</para>
    /// </summary>
    private static long PaidUnix(string firstPaidAt, long fallbackUnix) =>
        DateTime.TryParseExact(firstPaidAt, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? MetaCapiPayload.ToUnix(d)
            : fallbackUnix;

    /// <summary>Massiv ko'rinishidagi hashlangan maydondan birinchi qiymat.</summary>
    private static string First(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var node)) return "";

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? "";
            return "";
        }

        return node.ValueKind == JsonValueKind.String ? node.GetString() ?? "" : "";
    }
}
