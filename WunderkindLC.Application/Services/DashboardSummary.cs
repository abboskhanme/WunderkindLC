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
    /// <summary>Lid: aylantirilganmi (o'quvchi bo'lganmi).</summary>
    public readonly record struct LeadRow(string Id, bool Converted);

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
                .Select(l => new { l.Id, l.ConvertedStudentId }).ToListAsync(ct))
            .Select(l => new LeadRow(l.Id, l.ConvertedStudentId != null)).ToList();
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
        var groups = (await db.Classes.AsNoTracking()
                .Select(g => new { g.Id, g.CourseId, g.IsArchived, g.Status }).ToListAsync(ct))
            .Select(g => new GroupRow(g.Id, g.CourseId ?? "", g.IsArchived, g.Status ?? "")).ToList();
        var memberships = await db.StudentGroups.AsNoTracking().ToListAsync(ct);
        var payments = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && t.Direction == "income" && t.Category == "tuition")
                .Select(t => new { StudentId = t.StudentId!, t.Date }).ToListAsync(ct))
            .Select(t => new PaymentRow(t.StudentId, t.Date ?? "")).ToList();

        return Compute(leads, trials, deletedLeads, students, memberships, groups, payments,
            month, today.ToString("yyyy-MM-dd"));
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

    /// <summary>BUYURTMALAR — ochiq lidlar: hali o'quvchiga aylantirilmagan.
    /// <para>Loyihada "yo'qotilgan lid" bosqichi YO'Q: rad etilgan lid O'CHIRILADI (arxivga tushadi),
    /// ya'ni jadvalda qolgan har bir aylantirilmagan lid — ochiq buyurtma.</para></summary>
    public static int OpenLeads(IEnumerable<LeadRow> leads) => leads.Count(l => !l.Converted);

    /// <summary>
    /// BIRINCHI DARSGA KELADIGANLAR — ochiq lid, unda natijasi hali belgilanmagan (<see cref="TrialPending"/>)
    /// va BUGUN yoki keyinroqqa rejalashtirilgan sinov darsi bor. Takrorsiz (bir lidda ikki sinov — bitta).
    /// <para>O'tib ketgan, lekin belgilanmagan sinov "keladigan" emas — u eskirgan yozuv, sanalsa
    /// raqam oy sayin shishib borardi.</para>
    /// </summary>
    public static int FirstLessonLeads(IEnumerable<LeadRow> leads, IEnumerable<TrialRow> trials, string today)
    {
        var open = leads.Where(l => !l.Converted).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
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
        var fromNew = new HashSet<string>(StringComparer.Ordinal);
        var fromActive = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in memberships.GroupBy(m => (m.StudentId, Course: CourseKey(m.GroupId, courseByGroup))))
        {
            var list = pair.ToList();
            var intervals = CourseAnalytics.MergeIntervals(list.Select(m => ToRow(m, pair.Key.Course)));

            for (var i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                if (iv.Completed || !InMonth(iv.End, month)) continue;

                // Oraliqqa tegishli a'zoliklar: boshlanishi [iv.Start, keyingi oraliq boshi) ichida.
                // MergeIntervals a'zoliklarni JoinedAt bo'yicha tartiblab yig'adi, ya'ni har a'zolik
                // aynan bitta oraliqqa tushadi.
                var next = i + 1 < intervals.Count ? intervals[i + 1].Start : null;
                var everActive = list.Any(m =>
                    (m.JoinedAt ?? "").Length >= 10
                    && string.CompareOrdinal(m.JoinedAt, iv.Start) >= 0
                    && (next is null || string.CompareOrdinal(m.JoinedAt, next) < 0)
                    && MembershipLifecycle.FirstActivatedAt(m).Length >= 7);

                (everActive ? fromActive : fromNew).Add(pair.Key.StudentId);
            }
        }
        return (fromNew.Count, fromActive.Count);
    }

    /// <summary>BIRINCHI TO'LOVNI QILGANLAR — o'quvchining eng birinchi o'quv to'lovi shu oyda.</summary>
    public static int FirstPaymentsInMonth(IEnumerable<PaymentRow> payments, string month) =>
        payments
            .Where(p => p.Date.Length >= 7)
            .GroupBy(p => p.StudentId)
            .Count(g => g.Select(p => p.Date).Min(StringComparer.Ordinal)![..7] == month);

    private static bool InMonth(string? iso, string month) =>
        iso is { Length: >= 7 } && iso[..7] == month;

    private static string CourseKey(string groupId, IReadOnlyDictionary<string, string> courseByGroup) =>
        courseByGroup.TryGetValue(groupId, out var c) && !string.IsNullOrEmpty(c) ? c : "group:" + groupId;

    private static CourseAnalytics.MembershipRow ToRow(StudentGroup m, string course) => new(
        m.StudentId, course, m.JoinedAt ?? "", m.ActivatedAt ?? "", m.LeftAt, m.FrozenAt ?? "",
        m.Status ?? "", m.IsActive, 0m, m.PastPeriods);
}
