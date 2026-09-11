using System.Globalization;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LIDLAR (CRM) VORONKA ANALITIKASI hisob-kitobining <b>YAGONA MANBASI</b> — sof funksiyalar
/// (bazaga bog'liq emas, testlangan: <c>LeadAnalyticsTests</c>). Controller faqat ma'lumot yuklaydi.
///
/// <para><b>Asosiy qiyinchilik — tarix TO'LIQ EMAS.</b> Bosqich o'zgarishi ilgari faqat MATN
/// sifatida yozilardi (<c>LeadEvent.Text = "Bosqich: Yangi"</c>); <c>FromStage</c>/<c>ToStage</c>/
/// <c>ActorUserId</c> maydonlari keyin qo'shildi. Ya'ni ESKI lidlarda bosqich tarixi YO'Q.</para>
///
/// <para>Shu sababli ikki xil hisob bor:</para>
/// <list type="bullet">
///   <item><b>Voronka (<c>Reached</c>)</b> — tarixsiz ham ishlaydi: lidning JORIY bosqichi shu
///   bosqichdan past bo'lmasa, u bu bosqichdan o'tgan deb sanaladi (tarixda yozuv bo'lsa — u ham).
///   Shuning uchun voronka HAR DOIM to'la va pastga qarab kamayib boradi.</item>
///   <item><b>Bosqichda o'tirish vaqti va menejerlar kesimi</b> — FAQAT tarix bor lidlar bo'yicha.
///   Bu yerda taxmin qilinmaydi: o'lchov bo'lmasa <c>AvgHours = null</c>, va har qatorda
///   <c>Samples</c> qaytariladi — raqam nechta haqiqiy o'lchovga asoslangani ko'rinib tursin.</item>
/// </list>
/// </summary>
public static class LeadAnalytics
{
    /// <summary>Bosqich o'zgarishini bildiruvchi hodisa turlari (ToStage shularda to'ldiriladi).</summary>
    public const string TypeStage = "stage";
    public const string TypeCreated = "created";
    public const string TypeConvert = "convert";

    /// <summary>Manba yozilmagan lidlar uchun ko'rsatiladigan nom (<c>Stats()</c> bilan bir xil).</summary>
    public const string UnknownSourceLabel = "Noma'lum";

    /// <summary>Hisob uchun kerak bo'lgan lid maydonlari.</summary>
    /// <param name="CreatedAt">Yaratilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss") — davr filtri shu bo'yicha.</param>
    /// <param name="Paid">Lid haqiqatan PUL to'laganmi (<c>LeadOutcome.HasPaid</c>) — sotuvning o'lchovi.</param>
    /// <param name="Revenue">Lid keltirgan SOF tushum (to'lov − vozvrat).</param>
    /// <param name="Origin">Kanal kaliti (<see cref="LeadOrigins"/>); bo'sh = <c>other</c>.</param>
    public readonly record struct LeadRow(
        string Id, string Stage, string Source, bool Converted, string CreatedAt,
        bool Paid = false, decimal Revenue = 0m, string Origin = "");

    /// <summary>Hisob uchun kerak bo'lgan hodisa maydonlari (<c>LeadEvent</c>).</summary>
    public readonly record struct EventRow(
        string LeadId, string Type, string FromStage, string ToStage,
        string? ActorUserId, string ActorName, string CreatedAt);

    /// <summary>Bosqich (ustun) ma'lumotnomasi.</summary>
    public readonly record struct StageRow(string Id, string Title, string Color, int Order);

    /// <summary>Manba ma'lumotnomasi (<c>LeadSource</c>).</summary>
    public readonly record struct SourceRow(string Id, string Name);

    /* =========================================================================================
     *  DAVR FILTRI
     * ====================================================================================== */

    /// <summary>
    /// Lid shu davrga tushadimi. Sana ISO MATN ko'rinishida saqlanadi ("yyyy-MM-ddTHH:mm:ss"),
    /// shuning uchun solishtirish ordinal (leksikografik) — bu ISO uchun xronologik tartib bilan
    /// bir xil (loyihadagi boshqa sana solishtirishlari ham shunday).
    ///
    /// <para>Chegaralar QO'SHIB olinadi: <paramref name="from"/> va <paramref name="to"/> kunlarining
    /// o'zi ham kiradi (<c>to</c> — kun OXIRIGACHA).</para>
    /// </summary>
    public static bool InRange(string? createdAt, string? from, string? to)
    {
        var v = createdAt ?? "";
        if (v.Length < 10) return string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to);
        var day = v[..10];
        if (!string.IsNullOrWhiteSpace(from) && string.CompareOrdinal(day, from!.Trim()) < 0) return false;
        if (!string.IsNullOrWhiteSpace(to) && string.CompareOrdinal(day, to!.Trim()) > 0) return false;
        return true;
    }

    /* =========================================================================================
     *  UMUMIY YIG'MA
     * ====================================================================================== */

    /// <summary>
    /// Butun analitika. <paramref name="allEvents"/> — barcha lid hodisalari (davr bo'yicha
    /// KESILMAYDI: davr LID yaratilgan sana bo'yicha tanlanadi, shu lidlarning BUTUN tarixi olinadi).
    /// <paramref name="userNames"/> — AppUser.Id → ism (bo'lmasa hodisadagi <c>ActorName</c> ishlatiladi).
    /// </summary>
    public static LeadAnalyticsDto Build(
        IEnumerable<LeadRow> allLeads,
        IEnumerable<EventRow> allEvents,
        IEnumerable<StageRow> stages,
        IEnumerable<SourceRow> sources,
        string? from = null,
        string? to = null,
        IReadOnlyDictionary<string, string>? userNames = null)
    {
        var leads = (allLeads ?? []).Where(l => InRange(l.CreatedAt, from, to)).ToList();
        var leadIds = leads.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        // Davrdan tashqaridagi lidlarning hodisalari hisobga kirmasin.
        var events = (allEvents ?? []).Where(e => leadIds.Contains(e.LeadId)).ToList();

        var stageList = (stages ?? []).ToList();
        var total = leads.Count;
        var converted = leads.Count(l => l.Converted);
        var paid = leads.Count(l => l.Paid);

        return new LeadAnalyticsDto(
            From: from ?? "",
            To: to ?? "",
            Total: total,
            Converted: converted,
            ConversionRate: Percent(converted, total),
            Paid: paid,
            Revenue: leads.Sum(l => l.Revenue),
            PayRate: Percent(paid, total),
            Funnel: BuildFunnel(leads, events, stageList),
            Sources: BuildSources(leads, sources ?? []),
            Managers: BuildManagers(events, userNames, leads, stageList),
            Origins: BuildOrigins(leads));
    }

    /* =========================================================================================
     *  VORONKA
     * ====================================================================================== */

    /// <summary>
    /// Voronka bosqichlari (<c>Order</c> bo'yicha). Lid bosqichga YETIB KELGAN deb sanaladi, agar:
    /// <list type="number">
    ///   <item>uning JORIY bosqichi tartibi shu bosqich tartibidan past bo'lmasa (tarixsiz eski
    ///   lidlar uchun ham ishlaydi), YOKI</item>
    ///   <item>tarixda shu bosqichga o'tgani yozilgan bo'lsa (<c>ToStage == stageId</c>) — lid
    ///   keyin ORQAGA qaytarilgan bo'lsa ham o'tgani yo'qolmaydi.</item>
    /// </list>
    /// </summary>
    public static List<LeadFunnelStageDto> BuildFunnel(
        IEnumerable<LeadRow> leads, IEnumerable<EventRow> events, IEnumerable<StageRow> stages)
    {
        var leadList = leads as IReadOnlyList<LeadRow> ?? leads.ToList();
        var eventList = events as IReadOnlyList<EventRow> ?? events.ToList();
        var ordered = stages
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count == 0) return [];

        // Bosqich id → tartib. Lidning joriy bosqichi o'chirilgan bo'lishi mumkin — u holda
        // "joriy bosqich bo'yicha" qoida ishlamaydi, faqat tarix qoladi.
        var orderById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in ordered) orderById[s.Id] = s.Order;

        // Lid → tarixda yetib kelgan bosqichlar.
        var reachedByLead = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in eventList)
        {
            if (string.IsNullOrEmpty(e.ToStage)) continue;
            if (!reachedByLead.TryGetValue(e.LeadId, out var set))
                reachedByLead[e.LeadId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(e.ToStage);
        }

        var durations = StageDurations(eventList);
        var total = leadList.Count;

        var result = new List<LeadFunnelStageDto>(ordered.Count);
        foreach (var s in ordered)
        {
            var reached = 0;
            foreach (var l in leadList)
            {
                if (orderById.TryGetValue(l.Stage, out var cur) && cur >= s.Order) { reached++; continue; }
                if (reachedByLead.TryGetValue(l.Id, out var set) && set.Contains(s.Id)) reached++;
            }

            durations.TryGetValue(s.Id, out var d);
            result.Add(new LeadFunnelStageDto(
                StageId: s.Id, Title: s.Title, Color: s.Color, Order: s.Order,
                Reached: reached,
                Pct: total == 0 ? 0 : reached * 100 / total,
                // Samples == 0 → o'lchov yo'q. Nol yoki taxminiy son EMAS, aynan null.
                AvgHours: d.Samples == 0 ? null : Math.Round(d.Hours / d.Samples, 1),
                Samples: d.Samples));
        }
        return result;
    }

    /// <summary>
    /// Har bir bosqich uchun unda o'tirilgan JAMI soat va o'lchovlar soni.
    ///
    /// <para>Har lidning bosqich hodisalari (<c>created</c> + <c>stage</c>, <c>ToStage</c> to'ldirilgani)
    /// vaqt bo'yicha tartiblanadi; KETMA-KET ikki hodisa bir oraliqni yopadi: oldingisining
    /// <c>ToStage</c> bosqichiga KIRDI, keyingisi paytida undan CHIQDI. Oxirgi hodisadan keyingi
    /// (ya'ni JORIY) bosqich hali tugamagan — u hisobga OLINMAYDI.</para>
    ///
    /// <para>Bir xil bosqichga takror o'tkazish (<c>ToStage</c> o'zgarmasa) oraliqni yopmaydi —
    /// bosqich almashmagan, demak chiqish bo'lmagan. Vaqti o'qib bo'lmaydigan yoki teskari
    /// (manfiy) oraliqlar tashlab yuboriladi.</para>
    /// </summary>
    public static Dictionary<string, (double Hours, int Samples)> StageDurations(IEnumerable<EventRow> events)
    {
        var acc = new Dictionary<string, (double Hours, int Samples)>(StringComparer.Ordinal);

        var byLead = events
            .Where(e => !string.IsNullOrEmpty(e.ToStage) && (e.Type == TypeStage || e.Type == TypeCreated))
            .GroupBy(e => e.LeadId, StringComparer.Ordinal);

        foreach (var g in byLead)
        {
            string? cur = null;
            var enteredAt = default(DateTime);
            foreach (var e in g.OrderBy(x => x.CreatedAt, StringComparer.Ordinal))
            {
                if (!TryTime(e.CreatedAt, out var t)) continue;
                if (cur is not null && !string.Equals(cur, e.ToStage, StringComparison.Ordinal))
                {
                    var hours = (t - enteredAt).TotalHours;
                    if (hours >= 0)
                    {
                        acc.TryGetValue(cur, out var prev);
                        acc[cur] = (prev.Hours + hours, prev.Samples + 1);
                    }
                }
                if (cur is null || !string.Equals(cur, e.ToStage, StringComparison.Ordinal))
                {
                    cur = e.ToStage;
                    enteredAt = t;
                }
            }
        }
        return acc;
    }

    /* =========================================================================================
     *  MANBALAR
     * ====================================================================================== */

    /// <summary>
    /// Manba kesmalari (ko'pidan kamiga). <c>Label</c> — <c>LeadSource</c> ma'lumotnomasidan:
    /// avval id bo'yicha, so'ng nom bo'yicha (registr farqisiz); topilmasa lidda yozilgan
    /// qiymatning o'zi. Bo'sh manba — <see cref="UnknownSourceLabel"/>.
    ///
    /// <para>Ro'yxat KESILMAYDI (limit yo'q) — kichik kesmalarni "Boshqa"ga yig'ishni frontend
    /// o'zi hal qiladi.</para>
    /// </summary>
    public static List<LeadSourceSliceDto> BuildSources(IEnumerable<LeadRow> leads, IEnumerable<SourceRow> sources)
    {
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in sources)
        {
            var name = (s.Name ?? "").Trim();
            if (name.Length == 0) continue;
            if (!string.IsNullOrEmpty(s.Id)) byId[s.Id] = name;
            byName.TryAdd(name.ToLowerInvariant(), name);
        }

        string Label(string src)
        {
            if (src.Length == 0) return UnknownSourceLabel;
            if (byId.TryGetValue(src, out var n)) return n;
            if (byName.TryGetValue(src.ToLowerInvariant(), out var n2)) return n2;
            return src;
        }

        var leadList = leads as IReadOnlyList<LeadRow> ?? leads.ToList();
        var total = leadList.Count;

        return leadList
            .GroupBy(l => (l.Source ?? "").Trim(), StringComparer.Ordinal)
            .Select(g => new LeadSourceSliceDto(g.Key, Label(g.Key), g.Count(), Percent(g.Count(), total)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /* =========================================================================================
     *  MENEJERLAR
     * ====================================================================================== */

    /// <summary>
    /// Menejerlar (sotuvchilar) kesimi — <c>created</c>, <c>stage</c> va <c>convert</c> hodisalari
    /// <c>ActorUserId</c> bo'yicha.
    ///
    /// <para><b>Nega uchala tur ham?</b> Faqat <c>stage</c> bo'yicha guruhlansa, lidni o'quvchiga
    /// AYLANTIRGAN yoki lidni O'ZI KIRITGAN, lekin bosqichni ko'chirmagan menejer jadvalga umuman
    /// tushmasdi va uning ishi jimgina yo'qolardi. <c>Moves</c> baribir faqat bosqich
    /// ko'chirishlarini sanaydi (ta'rifi shu), ya'ni bunday menejerda 0 bo'ladi.</para>
    ///
    /// <para><b>PUL bir marta sanaladi.</b> <c>Won</c>/<c>Paid</c>/<c>Revenue</c> faqat lidni
    /// AYLANTIRGAN menejerga yoziladi — aks holda bir lidning tushumi bir necha menejerga
    /// qo'shilib, jami haqiqiy tushumdan oshib ketardi. "Kim yordam berdi" savoliga
    /// <c>Stages</c> (bosqich matritsasi) javob beradi.</para>
    ///
    /// <para><c>ActorUserId</c> BO'SH yozuvlar butunlay TASHLAB YUBORILADI (ular "Noma'lum" qatoriga
    /// ham yig'ilmaydi): bu maydon eski hodisalarda yo'q, tizim yozgan hodisalarda esa (sayt formasi,
    /// daraja testi) menejer umuman yo'q — ularni bitta uyumga qo'shish yolg'on qator yasardi.</para>
    ///
    /// <para>Tartib: avval <c>Revenue</c> (pul), keyin <c>Won</c>, so'ng <c>Moves</c> — sotuv
    /// jadvalida natija faollikdan ustun turadi.</para>
    /// </summary>
    /// <param name="leads">Davrdagi lidlar — pul (<c>Paid</c>/<c>Revenue</c>) shulardan olinadi.
    /// Berilmasa pul ustunlari 0 bo'ladi (kesimning o'zi baribir ishlaydi).</param>
    /// <param name="stages">Bosqich matritsasining USTUNLARI (<c>Order</c> bo'yicha). Berilmasa
    /// <c>Stages</c> bo'sh qaytadi.</param>
    public static List<LeadManagerRowDto> BuildManagers(
        IEnumerable<EventRow> events,
        IReadOnlyDictionary<string, string>? userNames = null,
        IEnumerable<LeadRow>? leads = null,
        IEnumerable<StageRow>? stages = null)
    {
        var list = events as IReadOnlyList<EventRow> ?? events.ToList();
        var leadById = (leads ?? []).GroupBy(l => l.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var stageOrder = (stages ?? []).OrderBy(x => x.Order)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // "Won" — shu menejer o'quvchiga aylantirgan lidlar (bir lid faqat bir marta sanaladi).
        var wonLeadsByUser = list
            .Where(e => e.Type == TypeConvert && !string.IsNullOrWhiteSpace(e.ActorUserId))
            .GroupBy(e => e.ActorUserId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.LeadId).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        return list
            .Where(e => (e.Type == TypeStage || e.Type == TypeConvert || e.Type == TypeCreated)
                        && !string.IsNullOrWhiteSpace(e.ActorUserId))
            .GroupBy(e => e.ActorUserId!, StringComparer.Ordinal)
            .Select(g =>
            {
                var won = wonLeadsByUser.GetValueOrDefault(g.Key, []);
                // Pul faqat DAVRDAGI (leadById dagi) lidlardan olinadi — davrdan tashqaridagi
                // lidning tushumi shu davr hisobotiga qo'shilib ketmasin.
                // ⚠️ `default(LeadRow)` da `Id == null` (record struct) — shuning uchun
                // `Length > 0` EMAS, `IsNullOrEmpty`: aks holda davrdan tashqaridagi lid
                // NullReferenceException berardi.
                var money = won.Select(id => leadById.TryGetValue(id, out var l) ? l : default)
                    .Where(l => !string.IsNullOrEmpty(l.Id)).ToList();

                return new LeadManagerRowDto(
                    UserId: g.Key,
                    Name: ResolveName(g.Key, g, userNames),
                    // Faqat bosqich ko'chirishlari — `created`/`convert` bu yerda sanalmaydi.
                    Moves: g.Count(e => e.Type == TypeStage),
                    Leads: g.Select(e => e.LeadId).Distinct(StringComparer.Ordinal).Count(),
                    Won: won.Count,
                    Created: g.Where(e => e.Type == TypeCreated)
                        .Select(e => e.LeadId).Distinct(StringComparer.Ordinal).Count(),
                    Paid: money.Count(l => l.Paid),
                    Revenue: money.Sum(l => l.Revenue),
                    Stages: StageCounts(g, stageOrder));
            })
            .OrderByDescending(m => m.Revenue)
            .ThenByDescending(m => m.Won)
            .ThenByDescending(m => m.Moves)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// «Kim qaysi bosqichgacha olib bordi» — bitta menejerning hodisalaridan bosqich kesimi:
    /// har bosqich uchun u AYNAN shu bosqichga ko'chirgan (yoki shu bosqichga kiritgan) TAKRORSIZ
    /// lidlar soni.
    ///
    /// <para>⚠️ Bu VORONKA EMAS: pastga qarab kamayib borishi SHART emas — menejer lidni
    /// o'rtadagi bosqichga boshqa xodimdan olib, keyingisiga surgan bo'lishi mumkin. Voronka
    /// (<see cref="BuildFunnel"/>) lidning yo'lini, bu jadval esa XODIMNING ishini ko'rsatadi.</para>
    /// </summary>
    private static List<LeadManagerStageDto> StageCounts(
        IEnumerable<EventRow> userEvents, IReadOnlyList<StageRow> stages)
    {
        if (stages.Count == 0) return [];
        var byStage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in userEvents)
        {
            if (e.Type != TypeStage && e.Type != TypeCreated) continue;
            if (string.IsNullOrEmpty(e.ToStage)) continue;
            if (!byStage.TryGetValue(e.ToStage, out var set))
                byStage[e.ToStage] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(e.LeadId);
        }
        return stages
            .Select(s => new LeadManagerStageDto(s.Id, byStage.TryGetValue(s.Id, out var v) ? v.Count : 0))
            .ToList();
    }

    /* =========================================================================================
     *  KANALLAR (lid qayerdan keldi)
     * ====================================================================================== */

    /// <summary>
    /// Kanal kesimi — <see cref="LeadOrigins.Order"/> tartibida, BO'SH kanallar tushirib
    /// qoldiriladi (nol qatorlar jadvalni suyultirardi).
    ///
    /// <para>Savol: "qaysi kanal ko'p lid beradi" emas, "qaysi kanal haqiqatan SOTADI" — shuning
    /// uchun har qatorda konversiya bilan birga TO'LOV ulushi ham bor.</para>
    /// </summary>
    public static List<LeadOriginRowDto> BuildOrigins(IEnumerable<LeadRow> leads)
    {
        var list = leads as IReadOnlyList<LeadRow> ?? leads.ToList();
        var byKey = list
            .GroupBy(l => string.IsNullOrEmpty(l.Origin) ? LeadOrigins.Other : l.Origin, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Katalogda yo'q (kelajakdagi yangi) kalit ham yo'qolmasin — oxiriga qo'shiladi.
        var keys = LeadOrigins.Order.Where(byKey.ContainsKey)
            .Concat(byKey.Keys.Where(k => !LeadOrigins.Order.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            .ToList();

        return keys.Select(k =>
        {
            var rows = byKey[k];
            var conv = rows.Count(l => l.Converted);
            var paid = rows.Count(l => l.Paid);
            return new LeadOriginRowDto(
                Key: k, Label: LeadOrigins.LabelOf(k),
                Leads: rows.Count, Converted: conv, Paid: paid,
                Revenue: rows.Sum(l => l.Revenue),
                ConversionRate: Percent(conv, rows.Count),
                PayRate: Percent(paid, rows.Count));
        }).ToList();
    }

    /// <summary>Menejer ismi: avval joriy ro'yxatdan (ism o'zgargan bo'lishi mumkin), aks holda
    /// hodisada yozilgan ENG SO'NGGI ism; u ham bo'lmasa — bo'sh (frontend id ko'rsatadi).</summary>
    private static string ResolveName(
        string userId, IEnumerable<EventRow> userEvents, IReadOnlyDictionary<string, string>? userNames)
    {
        if (userNames is not null && userNames.TryGetValue(userId, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;
        return userEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.ActorName))
            .OrderBy(e => e.CreatedAt, StringComparer.Ordinal)
            .Select(e => e.ActorName)
            .LastOrDefault() ?? "";
    }

    /* =========================================================================================
     *  KICHIK YORDAMCHILAR
     * ====================================================================================== */

    /// <summary>Yaxlitlangan foiz (0-100). Bo'luvchi 0 bo'lsa — 0.</summary>
    private static int Percent(int part, int total) =>
        total == 0 ? 0 : (int)Math.Round(part * 100.0 / total);

    /// <summary>ISO matn vaqtini o'qiydi. O'qib bo'lmasa — hodisa hisobga olinmaydi.</summary>
    private static bool TryTime(string? iso, out DateTime value) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
