using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// BOSH SAHIFA KARTOCHKALARI ("Dars jadvali" sahifasining tepasidagi 12 ta raqam).
///
/// <para>Hisob-kitob <see cref="Compute"/> da — sof funksiya (bazaga bog'liq emas,
/// <c>DashboardSummaryTests</c> bilan qoplangan). <see cref="BuildAsync"/> faqat ma'lumot yuklaydi.</para>
///
/// <para>⚠️ Qoidalar QAYTA YOZILMAGAN — mavjud manbalarga tayanadi:</para>
/// <list type="bullet">
/// <item>"ketdi" — <see cref="CourseAnalytics.MergeIntervals"/> (guruh almashtirish churn EMAS,
/// sertifikat bilan tugatish ham churn EMAS: <c>.claude/rules/course-analytics.md</c> §1);</item>
/// <item>"aktivlashganmi" — <see cref="MembershipLifecycle.FirstActivatedAt(StudentGroup)"/>
/// (yopilgan davrlar ham ko'riladi: <c>.claude/rules/membership-periods.md</c> §7);</item>
/// <item>"muzlatilgan o'quvchi" — <see cref="MembershipLifecycle.MemberState"/> (o'quvchi holati
/// BITTA a'zolikdan olinmaydi);</item>
/// <item>"birinchi to'lov" — <see cref="LeadOutcome"/> dagi konvensiya: kirim/tuition, vozvrat
/// hisobga olinmaydi (savol "qachon pul keldi").</item>
/// </list>
/// </summary>
public static class DashboardSummary
{
    /// <summary>Lid: aylantirilganmi (o'quvchi bo'lganmi) va YO'QOTILGANmi (<see cref="LeadLoss"/>).
    /// <see cref="IsOpen"/> — OCHIQ lid (faol ish): ikkalasi ham emas. Bosh sahifa kartochkalari,
    /// «Birinchi darsga yozilganlar» va sinov eslatmasi AYNAN shu ta'rifdan foydalanadi.</summary>
    public readonly record struct LeadRow(string Id, bool Converted, bool Lost = false)
    {
        public bool IsOpen => !Converted && !Lost;
    }

    /// <summary>Sinov darsi: <paramref name="ScheduledAt"/> — ISO "yyyy-MM-ddTHH:mm".</summary>
    public readonly record struct TrialRow(string LeadId, string Result, string ScheduledAt);

    public readonly record struct StudentRow(string Id, bool IsArchived, decimal Balance);

    /// <summary>Guruh: kursi (a'zoliklarni kurs kesimida birlashtirish uchun) va holati.</summary>
    public readonly record struct GroupRow(string Id, string CourseId, bool IsArchived, string Status);

    /// <summary>O'quv to'lovi (kirim/tuition): kim va qachon ("yyyy-MM-dd").</summary>
    public readonly record struct PaymentRow(string StudentId, string Date);

    /// <summary>Hali natijasi belgilanmagan sinov darsi (<see cref="TrialLesson.Result"/>).</summary>
    public const string TrialPending = "pending";

    /// <summary>Bazadan yuklab, <see cref="Compute"/> ni chaqiradi. Barcha so'rovlar faqat-o'qish.</summary>
    public static async Task<DashboardSummaryDto> BuildAsync(
        IAppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var month = today.ToString("yyyy-MM");

        var leads = (await db.Leads.AsNoTracking()
                .Select(l => new { l.Id, l.ConvertedStudentId, l.LostAt }).ToListAsync(ct))
            .Select(l => new LeadRow(l.Id, l.ConvertedStudentId != null, !string.IsNullOrWhiteSpace(l.LostAt)))
            .ToList();
        var trials = (await db.TrialLessons.AsNoTracking()
                .Where(t => t.Result == TrialPending)
                .Select(t => new { t.LeadId, t.Result, t.ScheduledAt }).ToListAsync(ct))
            .Select(t => new TrialRow(t.LeadId, t.Result, t.ScheduledAt ?? "")).ToList();
        // O'chirilgan lid ARXIV suratiga tushadi (`LeadsController.Delete` → `ArchiveService.Snapshot`)
        // — "shu oyda ketgan o'quvchilar" (`CenterAiAnalysisService`) bilan bir xil manba.
        var deletedLeads = await db.ArchivedRecords.AsNoTracking()
            .Where(a => a.Type == "lead" && a.DeletedAt.StartsWith(month))
            .Select(a => a.DeletedAt).ToListAsync(ct);
        var students = (await db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.IsArchived, s.Balance }).ToListAsync(ct))
            .Select(s => new StudentRow(s.Id, s.IsArchived, s.Balance)).ToList();
        // Arxivlangan guruhlar ham YUKLANADI — ular orqali o'tgan o'quvchilarning "ketdi" tarixi
        // yo'qolmasin (`course-analytics.md` §5). "Guruhlar" soni esa faqat arxivlanmaganlardan.
        var groups = await LoadGroupsAsync(db, ct);
        var memberships = await db.StudentGroups.AsNoTracking().ToListAsync(ct);
        var payments = await LoadPaymentsAsync(db, ct);

        return Compute(leads, trials, deletedLeads, students, memberships, groups, payments,
            month, today.ToString("yyyy-MM-dd"));
    }

    private static async Task<List<GroupRow>> LoadGroupsAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.CourseId, g.IsArchived, g.Status }).ToListAsync(ct))
        .Select(g => new GroupRow(g.Id, g.CourseId ?? "", g.IsArchived, g.Status ?? "")).ToList();

    private static async Task<List<PaymentRow>> LoadPaymentsAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId != null && t.Direction == "income" && t.Category == "tuition")
            .Select(t => new { StudentId = t.StudentId!, t.Date }).ToListAsync(ct))
        .Select(t => new PaymentRow(t.StudentId, t.Date ?? "")).ToList();

    // ============================ KARTOCHKA RO'YXATLARI ============================

    /// <summary>
    /// Kartochka ro'yxatidagi bitta o'quvchi: hodisa SANASI ("ketgan" — oraliq tugagan kun,
    /// "birinchi to'lov" — to'lov kuni) va ketganlarda chiqib ketgan GURUH (to'lovda bo'sh).
    /// </summary>
    public readonly record struct MovementRow(string Date, string GroupId);

    /// <summary>Bosh sahifadagi qaysi kartochkalarning RO'YXATI bor (<see cref="MovementAsync"/>).</summary>
    public const string LeftNew = "leftNew";
    public const string LeftActive = "leftActive";
    public const string FirstPayment = "firstPayment";
    public static readonly IReadOnlyList<string> MovementKinds = [LeftNew, LeftActive, FirstPayment];

    /// <summary>
    /// "Yangi/Aktiv o'quvchidan ketganlar" yoki "Birinchi to'lovni qilganlar" — O'QUVCHILARI
    /// (o'quvchi id → <see cref="MovementRow"/>). Kartochka sonini hisoblaydigan AYNAN o'sha
    /// funksiyalardan (<see cref="LeftStudentsInMonth"/>, <see cref="FirstPaymentStudentsInMonth"/>),
    /// ya'ni ro'yxatdagi qatorlar soni kartochkadagi son bilan TENG.
    /// <para>⚠️ Bazada yo'q o'quvchi (yetim a'zolik/to'lov) bu yerda qolishi mumkin — kartochka uni
    /// sanamaydi (<c>knownIds</c>), chaqiruvchi esa o'quvchilarni bazadan yuklaganda u o'zi tushadi.</para>
    /// </summary>
    public static async Task<Dictionary<string, MovementRow>> MovementAsync(
        IAppDbContext db, string kind, string month, CancellationToken ct = default)
    {
        if (kind == FirstPayment)
            return FirstPaymentStudentsInMonth(await LoadPaymentsAsync(db, ct), month);

        var groups = await LoadGroupsAsync(db, ct);
        var memberships = await db.StudentGroups.AsNoTracking().ToListAsync(ct);
        var (fromNew, fromActive) = LeftStudentsInMonth(
            memberships, groups.ToDictionary(g => g.Id, g => g.CourseId, StringComparer.Ordinal), month);
        return kind == LeftNew ? fromNew : fromActive;
    }

    /// <summary>12 ta ko'rsatkich. <paramref name="month"/> — "yyyy-MM", <paramref name="today"/> — "yyyy-MM-dd".</summary>
    /// <param name="deletedLeadAts">Shu oyda o'chirilgan lidlarning <see cref="ArchivedRecord.DeletedAt"/> qiymatlari.</param>
    public static DashboardSummaryDto Compute(
        IReadOnlyList<LeadRow> leads,
        IReadOnlyList<TrialRow> trials,
        IReadOnlyList<string> deletedLeadAts,
        IReadOnlyList<StudentRow> students,
        IReadOnlyList<StudentGroup> memberships,
        IReadOnlyList<GroupRow> groups,
        IReadOnlyList<PaymentRow> payments,
        string month,
        string today)
    {
        // Arxivlanmagan o'quvchilar — joriy holat sanoqlari (aktiv/sinov/muzlatilgan/qarzdor)
        // eski bosh sahifadagi bilan AYNAN bir xil doirada.
        var liveIds = students.Where(s => !s.IsArchived).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var knownIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var live = memberships.Where(m => m.IsActive && liveIds.Contains(m.StudentId)).ToList();

        var active = live.Where(m => m.Status == "active").Select(m => m.StudentId).Distinct().Count();
        var trial = live.Where(m => m.Status == "trial").Select(m => m.StudentId).Distinct().Count();
        var frozen = FrozenStudents(memberships.Where(m => liveIds.Contains(m.StudentId)));

        var (newLeft, activeLeft) = LeftInMonth(
            memberships.Where(m => knownIds.Contains(m.StudentId)),
            groups.ToDictionary(g => g.Id, g => g.CourseId, StringComparer.Ordinal),
            month);

        return new DashboardSummaryDto(
            Month: month,
            Orders: OpenLeads(leads),
            FirstLesson: FirstLessonLeads(leads, trials, today),
            NewStudents: trial,
            ActiveStudents: active,
            OrdersLeft: deletedLeadAts.Count(d => InMonth(d, month)),
            NewLeft: newLeft,
            ActiveLeft: activeLeft,
            Debtors: students.Count(s => !s.IsArchived && s.Balance < 0),
            Groups: groups.Count(g => !g.IsArchived && g.Status != "archived"),
            FirstPayments: FirstPaymentsInMonth(payments.Where(p => knownIds.Contains(p.StudentId)), month),
            Frozen: frozen,
            Archived: students.Count(s => s.IsArchived));
    }

    /// <summary>BUYURTMALAR — ochiq lidlar: hali o'quvchiga aylantirilmagan VA yo'qotilmagan.
    /// <para>2026-10 gacha "yo'qotilgan lid" holati YO'Q edi (rad etilgan lid O'CHIRILARDI). Endi
    /// lid sabab bilan YO'QOTILGAN deb belgilanadi (<see cref="LeadLoss"/>) — u faol ish emas, shuning
    /// uchun bu yerda sanalmaydi. ⚠️ "Buyurtmadan ketganlar" (<c>OrdersLeft</c>) esa avvalgidek faqat
    /// O'CHIRILGAN lidlar: kartochka ro'yxati arxiv sahifasi, yo'qotilganlar u yerda yo'q — sanoq
    /// ro'yxatdan farq qilmasin. Yo'qotilganlar — «Yo'qotilgan lidlar» hisobotida.</para></summary>
    public static int OpenLeads(IEnumerable<LeadRow> leads) => leads.Count(l => l.IsOpen);

    /// <summary>
    /// BIRINCHI DARSGA KELADIGANLAR — ochiq lid, unda natijasi hali belgilanmagan (<see cref="TrialPending"/>)
    /// va BUGUN yoki keyinroqqa rejalashtirilgan sinov darsi bor. Takrorsiz (bir lidda ikki sinov — bitta).
    /// <para>O'tib ketgan, lekin belgilanmagan sinov "keladigan" emas — u eskirgan yozuv, sanalsa
    /// raqam oy sayin shishib borardi.</para>
    /// </summary>
    public static int FirstLessonLeads(IEnumerable<LeadRow> leads, IEnumerable<TrialRow> trials, string today)
    {
        var open = leads.Where(l => l.IsOpen).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        return trials
            .Where(t => t.Result == TrialPending
                        && t.ScheduledAt.Length >= 10
                        && string.CompareOrdinal(t.ScheduledAt[..10], today) >= 0
                        && open.Contains(t.LeadId))
            .Select(t => t.LeadId).Distinct().Count();
    }

    /// <summary>
    /// MUZLATILGAN — o'quvchining UMUMIY holati muzlatilgan (<see cref="MembershipLifecycle.MemberState"/>
    /// = "frozen" yoki "yearFrozen"): ya'ni faol ham, sinovdagi ham a'zoligi qolmagan.
    /// <para>Eski guruhida muzlatilib, yangisida o'qiyotgan o'quvchi bu yerga TUSHMAYDI — u aktiv.</para>
    /// </summary>
    public static int FrozenStudents(IEnumerable<StudentGroup> memberships) =>
        memberships.GroupBy(m => m.StudentId)
            .Count(g => MembershipLifecycle.MemberState(g) is "frozen" or "yearFrozen");

    /// <summary>
    /// SHU OYDA KETGANLAR — (o'quvchi, kurs) oraliqlari bo'yicha (<see cref="CourseAnalytics.MergeIntervals"/>):
    /// oraliq shu oyda TUGAGAN va u "tugatdi" (sertifikat) EMAS.
    /// <list type="bullet">
    /// <item><c>FromActive</c> — oraliq ichidagi birorta a'zolik aktivlashtirilgan bo'lgan;</item>
    /// <item><c>FromNew</c> — oraliq faqat sinovdan iborat (hech qachon aktivlashmagan).</item>
    /// </list>
    /// Ikkalasi ham TAKRORSIZ o'quvchilar soni.
    /// <para>Kurssiz guruh (<c>CourseId</c> bo'sh) o'zi alohida "kurs" hisoblanadi — aks holda
    /// undan ketgan o'quvchi bosh sahifada umuman ko'rinmasdi.</para>
    /// </summary>
    public static (int FromNew, int FromActive) LeftInMonth(
        IEnumerable<StudentGroup> memberships,
        IReadOnlyDictionary<string, string> courseByGroup,
        string month)
    {
        var (fromNew, fromActive) = LeftStudentsInMonth(memberships, courseByGroup, month);
        return (fromNew.Count, fromActive.Count);
    }

    /// <summary>
    /// <see cref="LeftInMonth"/> ning O'QUVCHILARI (kartochka ro'yxati uchun) — qoida AYNAN o'sha,
    /// sanoq shu lug'atlarning <c>Count</c> i. Qiymat — oraliq tugagan sana va oraliqni YOPGAN
    /// a'zolikning guruhi. Bir o'quvchi ikki kursdan ketgan bo'lsa — ENG KECHKI ketish.
    /// </summary>
    public static (Dictionary<string, MovementRow> FromNew, Dictionary<string, MovementRow> FromActive) LeftStudentsInMonth(
        IEnumerable<StudentGroup> memberships,
        IReadOnlyDictionary<string, string> courseByGroup,
        string month)
    {
        var fromNew = new Dictionary<string, MovementRow>(StringComparer.Ordinal);
        var fromActive = new Dictionary<string, MovementRow>(StringComparer.Ordinal);

        foreach (var e in LeftEvents(memberships, courseByGroup, end => InMonth(end, month)))
        {
            var row = new MovementRow(e.Date, e.GroupId);
            var target = e.EverActive ? fromActive : fromNew;
            if (!target.TryGetValue(e.StudentId, out var prev) || string.CompareOrdinal(row.Date, prev.Date) > 0)
                target[e.StudentId] = row;
        }
        return (fromNew, fromActive);
    }

    /// <summary>
    /// Bitta KETISH hodisasi — (o'quvchi, kurs) oralig'i tugadi va bu "tugatdi" (sertifikat) EMAS.
    /// </summary>
    /// <param name="CourseKey">Kurs id; kurssiz guruhda <c>"group:{guruh id}"</c> (o'zi alohida "kurs").</param>
    /// <param name="Date">Oraliq tugagan sana (yopgan a'zolikning <c>LeftAt</c>).</param>
    /// <param name="GroupId">Oraliqni YOPGAN a'zolikning guruhi — eng kech chiqqani (parallel guruhda ham).</param>
    /// <param name="EverActive">Oraliq ichidagi birorta a'zolik aktivlashtirilgan bo'lganmi
    /// ("aktivdan ketgan"); <c>false</c> — faqat sinovdan iborat ("yangidan ketgan").</param>
    /// <param name="Closing">Oraliqni yopgan a'zolikning o'zi (sabab qidirish uchun — <c>FrozenAt</c>).</param>
    public readonly record struct LeftEvent(
        string StudentId, string CourseKey, string Date, string GroupId, bool EverActive, StudentGroup? Closing);

    /// <summary>
    /// KETISH HODISALARI — "ketdi" qoidasining YAGONA amalga oshirilishi: bosh sahifa kartochkasi
    /// (<see cref="LeftStudentsInMonth"/>) ham, «Ketish sabablari» hisoboti
    /// (<see cref="RetentionLossReport"/>) ham AYNAN shuni chaqiradi.
    /// <para>(o'quvchi, kurs) juftligi bo'yicha <see cref="CourseAnalytics.MergeIntervals"/>: guruh
    /// almashtirish (bir kurs ichida, 7 kungacha tanaffus) va sertifikat bilan tugatish KETISH EMAS
    /// (<c>.claude/rules/course-analytics.md</c> §1).</para>
    /// </summary>
    /// <param name="endFilter">Oraliq tugash sanasi ("yyyy-MM-dd") qaysi davrga tushishi kerak.</param>
    public static IEnumerable<LeftEvent> LeftEvents(
        IEnumerable<StudentGroup> memberships,
        IReadOnlyDictionary<string, string> courseByGroup,
        Func<string, bool> endFilter)
    {
        foreach (var pair in memberships.GroupBy(m => (m.StudentId, Course: CourseKey(m.GroupId, courseByGroup))))
        {
            var list = pair.ToList();
            var intervals = CourseAnalytics.MergeIntervals(list.Select(m => ToRow(m, pair.Key.Course)));

            for (var i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                if (iv.Completed || iv.End is null || !endFilter(iv.End)) continue;

                // Oraliqqa tegishli a'zoliklar: boshlanishi [iv.Start, keyingi oraliq boshi) ichida.
                // MergeIntervals a'zoliklarni JoinedAt bo'yicha tartiblab yig'adi, ya'ni har a'zolik
                // aynan bitta oraliqqa tushadi.
                var next = i + 1 < intervals.Count ? intervals[i + 1].Start : null;
                var inInterval = list.Where(m =>
                    (m.JoinedAt ?? "").Length >= 10
                    && string.CompareOrdinal(m.JoinedAt, iv.Start) >= 0
                    && (next is null || string.CompareOrdinal(m.JoinedAt, next) < 0)).ToList();
                var everActive = inInterval.Any(m => MembershipLifecycle.FirstActivatedAt(m).Length >= 7);
                // Oraliqni YOPGAN a'zolik — eng kech chiqqani (parallel guruhda ham).
                var closing = inInterval.OrderByDescending(m => m.LeftAt ?? "", StringComparer.Ordinal).FirstOrDefault();
                yield return new LeftEvent(pair.Key.StudentId, pair.Key.Course, iv.End, closing?.GroupId ?? "", everActive, closing);
            }
        }
    }

    /// <summary>BIRINCHI TO'LOVNI QILGANLAR — o'quvchining eng birinchi o'quv to'lovi shu oyda.</summary>
    public static int FirstPaymentsInMonth(IEnumerable<PaymentRow> payments, string month) =>
        FirstPaymentStudentsInMonth(payments, month).Count;

    /// <summary><see cref="FirstPaymentsInMonth"/> ning O'QUVCHILARI: id → birinchi to'lov sanasi.</summary>
    public static Dictionary<string, MovementRow> FirstPaymentStudentsInMonth(IEnumerable<PaymentRow> payments, string month) =>
        payments
            .Where(p => p.Date.Length >= 7)
            .GroupBy(p => p.StudentId)
            .Select(g => (Id: g.Key, First: g.Select(p => p.Date).Min(StringComparer.Ordinal)!))
            .Where(x => x.First[..7] == month)
            .ToDictionary(x => x.Id, x => new MovementRow(x.First, ""), StringComparer.Ordinal);

    private static bool InMonth(string? iso, string month) =>
        iso is { Length: >= 7 } && iso[..7] == month;

    /// <summary>A'zolikni kurs kesimida birlashtirish kaliti — kurssiz guruh o'zi alohida "kurs".</summary>
    public static string CourseKey(string groupId, IReadOnlyDictionary<string, string> courseByGroup) =>
        courseByGroup.TryGetValue(groupId, out var c) && !string.IsNullOrEmpty(c) ? c : "group:" + groupId;

    private static CourseAnalytics.MembershipRow ToRow(StudentGroup m, string course) => new(
        m.StudentId, course, m.JoinedAt ?? "", m.ActivatedAt ?? "", m.LeftAt, m.FrozenAt ?? "",
        m.Status ?? "", m.IsActive, 0m, m.PastPeriods);
}
