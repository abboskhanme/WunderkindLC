namespace WunderkindLC.Application.Services;

/// <summary>
/// «BIRINCHI DARSGA YOZILGANLAR» (edutizim: Lidlar → Birinchi darsga yozilganlar) va lidlar
/// jadvalidagi "O'QITUVCHI" ustuni — sof funksiyalar (<c>LeadFirstLessonTests</c>).
///
/// <para><b>TA'RIF — bosh sahifadagi kartochka bilan BITTA</b>
/// (<see cref="DashboardSummary.FirstLessonLeads"/>): ochiq (o'quvchiga aylantirilmagan) lid +
/// natijasi hali belgilanmagan (<see cref="DashboardSummary.TrialPending"/>) sinov darsi.
/// Ikkinchi ta'rif YARATILMAGAN — farq faqat bitta: ro'yxatda O'TIB KETGAN sana ham qoladi
/// (qizil qator, edutizimdagi <c>#FFCACA</c>), kartochka esa faqat bugun va keyingilarini
/// sanaydi. Ya'ni <b>kartochkadagi son == ro'yxatdagi qizil BO'LMAGAN qatorlar soni</b> —
/// test buni qulflaydi.</para>
///
/// <para>O'tib ketgan, lekin belgilanmagan sinov ro'yxatdan ATAYIN olib tashlanmaydi: bu —
/// "keldi/kelmadi" belgilanmay qolgan lid, ya'ni aynan menejer qaytib ko'rishi kerak bo'lgan
/// qator. Natija belgilangach (keldi/kelmadi/qoldi/ketdi) u ro'yxatdan o'zi chiqadi.</para>
/// </summary>
public static class LeadFirstLesson
{
    /// <summary>Sinov darsi: <paramref name="ScheduledAt"/> — ISO "yyyy-MM-ddTHH:mm".</summary>
    public readonly record struct TrialRow(string Id, string LeadId, string GroupId, string Result, string ScheduledAt);

    /// <summary>
    /// Sanasi O'TIB KETGANmi — kun bo'yicha (bugungi sinov "o'tgan" EMAS, u bugun keladi).
    /// Sanasi buzuq/bo'sh sinov ham "o'tgan" hisoblanadi: kartochka uni "keladigan" deb
    /// sanamaydi (<see cref="DashboardSummary.FirstLessonLeads"/>), ro'yxat ham shunday ko'rsatsin.
    /// </summary>
    public static bool IsPast(string? scheduledAt, string today) =>
        scheduledAt is not { Length: >= 10 } || string.CompareOrdinal(scheduledAt[..10], today) < 0;

    /// <summary>
    /// Lidning "birinchi dars" sinovi — faqat natijasi belgilanmaganlar (pending) orasidan:
    /// bugun yoki keyinroqqa rejalashtirilganlarning ENG YAQINI; bunday yo'q bo'lsa — o'tib
    /// ketganlarning ENG SO'NGGISI. Pending sinov umuman bo'lmasa — <c>null</c>.
    ///
    /// <para>Nega shunday: sinov qayta belgilanganda (eski "kutilmoqda" yozuvi yopilmay qolgan)
    /// qator KELADIGAN sana bilan ko'rinsin — aks holda odam kartochkada "keladi" deb sanalib,
    /// ro'yxatda qizil (o'tib ketgan) bo'lib turardi.</para>
    /// </summary>
    public static TrialRow? Pick(IEnumerable<TrialRow> trialsOfLead, string today)
    {
        TrialRow? upcoming = null, past = null;
        foreach (var t in trialsOfLead)
        {
            if (t.Result != DashboardSummary.TrialPending) continue;
            if (IsPast(t.ScheduledAt, today))
            {
                if (past is null || string.CompareOrdinal(t.ScheduledAt, past.Value.ScheduledAt) > 0) past = t;
            }
            else if (upcoming is null || string.CompareOrdinal(t.ScheduledAt, upcoming.Value.ScheduledAt) < 0)
            {
                upcoming = t;
            }
        }
        return upcoming ?? past;
    }

    /// <summary>
    /// Lidlar jadvalidagi O'QITUVCHI/GURUH uchun sinov: avval <see cref="Pick"/> (kutilayotgan
    /// birinchi dars), u yo'q bo'lsa — eng so'nggi rejalashtirilgan sinov (natijasidan qat'i nazar:
    /// "keldi"/"qoldi" bo'lgan lid ham qaysi o'qituvchiga borgani bilan ko'rinsin).
    /// </summary>
    public static TrialRow? Display(IEnumerable<TrialRow> trialsOfLead, string today)
    {
        var list = trialsOfLead as IReadOnlyCollection<TrialRow> ?? trialsOfLead.ToList();
        var pick = Pick(list, today);
        if (pick is not null) return pick;
        TrialRow? latest = null;
        foreach (var t in list)
            if (latest is null || string.CompareOrdinal(t.ScheduledAt, latest.Value.ScheduledAt) > 0) latest = t;
        return latest;
    }

    /// <summary>
    /// Ro'yxatga TUSHADIGAN lidlar: ochiq lid → uning birinchi dars sinovi.
    /// Aylantirilgan lid (<paramref name="leads"/>'da <c>Converted = true</c>) va pending sinovi
    /// yo'q lid tushmaydi.
    /// </summary>
    public static Dictionary<string, TrialRow> Select(
        IEnumerable<DashboardSummary.LeadRow> leads, IEnumerable<TrialRow> trials, string today)
    {
        var open = leads.Where(l => !l.Converted).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, TrialRow>(StringComparer.Ordinal);
        foreach (var g in trials.Where(t => open.Contains(t.LeadId)).GroupBy(t => t.LeadId))
        {
            var pick = Pick(g, today);
            if (pick is not null) result[g.Key] = pick.Value;
        }
        return result;
    }

    // ------------------------------------------------------------------ filtrlar

    /// <summary>Filtrlanadigan qator (controller bazadan yig'adi).</summary>
    public sealed record Row(
        string LeadId,
        string FullName,
        IReadOnlyList<string> Phones,
        // Qidiruv uchun qo'shimcha matn: ota-ona ismi, izoh, guruh nomi.
        string SearchText,
        string FirstLessonAt,
        bool IsPast,
        string Course,
        string Level,
        string TeacherId,
        string AssigneeUserId,
        IReadOnlyList<int> GroupDays);

    /// <summary>
    /// edutizim filtrlaridan bizda MA'LUMOTI borlari. Hammasi ixtiyoriy (bo'sh = filtr yo'q).
    /// <list type="bullet">
    /// <item><c>Date</c> — "Sanani tanlang": birinchi dars AYNAN shu kuni;</item>
    /// <item><c>From</c>/<c>To</c> — "Oraliqni tanlang" (chegaralar kiradi);</item>
    /// <item><c>Course</c>/<c>Level</c> — "Kurs"/"Daraja" (registr farqisiz);</item>
    /// <item><c>Weekday</c> — "Kun": birinchi dars sanasining hafta kuni (0=Dushanba … 6=Yakshanba);</item>
    /// <item><c>Parity</c> — "Toq/Juft kunlar": guruh jadvali, <c>odd</c> = Du/Chor/Ju, <c>even</c> = Se/Pay/Sha;</item>
    /// <item><c>Assignee</c> — "Moderator" (<see cref="NoAssignee"/> = biriktirilmagan);</item>
    /// <item><c>Teacher</c> — "O'qituvchi";</item>
    /// <item><c>Color</c> — "Ranglar bo'yicha": <c>past</c> (qizil) | <c>upcoming</c>;</item>
    /// <item><c>Q</c> — "Qidirish": ism/izoh/ota-ona/guruh; 3+ raqam bo'lsa telefonlar bo'yicha ham.</item>
    /// </list>
    /// </summary>
    public sealed record Filter(
        string? Date = null, string? From = null, string? To = null,
        string? Course = null, string? Level = null,
        int? Weekday = null, string? Parity = null,
        string? Assignee = null, string? Teacher = null,
        string? Color = null, string? Q = null);

    /// <summary>"Moderator" filtrida mas'ulsiz lidlar uchun maxsus qiymat.</summary>
    public const string NoAssignee = "__none__";

    /// <summary>Toq kunlar (Du, Chor, Juma) — 0=Dushanba indekslashda.</summary>
    public static readonly IReadOnlySet<int> OddDays = new HashSet<int> { 0, 2, 4 };
    /// <summary>Juft kunlar (Se, Pay, Shanba).</summary>
    public static readonly IReadOnlySet<int> EvenDays = new HashSet<int> { 1, 3, 5 };

    /// <summary>Filtrni qo'llaydi va birinchi dars sanasi bo'yicha O'SIB boruvchi tartibda qaytaradi
    /// (edutizimdagidek: o'tib ketganlar — tepada, qizil).</summary>
    public static List<Row> Apply(IEnumerable<Row> rows, Filter f)
    {
        var q = (f.Q ?? "").Trim().ToLowerInvariant();
        var qDigits = new string(q.Where(char.IsDigit).ToArray());

        return rows.Where(r =>
            {
                var day = r.FirstLessonAt.Length >= 10 ? r.FirstLessonAt[..10] : "";
                if (Has(f.Date) && day != f.Date!.Trim()) return false;
                if (Has(f.From) && string.CompareOrdinal(day, f.From!.Trim()) < 0) return false;
                if (Has(f.To) && string.CompareOrdinal(day, f.To!.Trim()) > 0) return false;
                if (Has(f.Course) && !Same(r.Course, f.Course)) return false;
                if (Has(f.Level) && !Same(r.Level, f.Level)) return false;
                if (f.Weekday is { } wd && WeekdayOf(day) != wd) return false;
                if (Has(f.Parity) && !MatchesParity(r.GroupDays, f.Parity!)) return false;
                if (Has(f.Assignee))
                {
                    if (f.Assignee == NoAssignee ? Has(r.AssigneeUserId) : r.AssigneeUserId != f.Assignee) return false;
                }
                if (Has(f.Teacher) && r.TeacherId != f.Teacher) return false;
                if (f.Color == "past" && !r.IsPast) return false;
                if (f.Color == "upcoming" && r.IsPast) return false;
                if (q.Length > 0 && !MatchesSearch(r, q, qDigits)) return false;
                return true;
            })
            .OrderBy(r => r.FirstLessonAt, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>ISO sananing hafta kuni (0=Dushanba … 6=Yakshanba); buzuq sana — <c>-1</c>.</summary>
    public static int WeekdayOf(string? isoDay) =>
        isoDay is { Length: >= 10 } && DateOnly.TryParseExact(isoDay[..10], "yyyy-MM-dd", out var d)
            ? ((int)d.DayOfWeek + 6) % 7
            : -1;

    /// <summary>Guruh jadvali toq/juft kunlarga TO'LIQ tushadimi (bo'sh jadval — hech biriga).</summary>
    public static bool MatchesParity(IReadOnlyList<int> days, string parity)
    {
        if (days.Count == 0) return false;
        var set = parity == "odd" ? OddDays : parity == "even" ? EvenDays : null;
        return set is not null && days.All(set.Contains);
    }

    private static bool MatchesSearch(Row r, string q, string qDigits)
    {
        if ($"{r.FullName} {r.SearchText}".ToLowerInvariant().Contains(q)) return true;
        return qDigits.Length >= 3
               && r.Phones.Any(p => new string((p ?? "").Where(char.IsDigit).ToArray()).Contains(qDigits));
    }

    private static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

    private static bool Same(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
