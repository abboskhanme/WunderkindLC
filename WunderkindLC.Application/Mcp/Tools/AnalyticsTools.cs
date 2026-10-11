using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Centre-wide analytics: course flow (came / left / completed per month, overlap) and the
/// school-grade breakdown. Areas: schedule.analytics (courses), students.list (grades).
/// </summary>
[McpServerToolType]
public sealed class AnalyticsTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Group), typeof(Subject), typeof(StudentGroup), typeof(Student),
    ];

    private const int DefaultMonths = 6;
    private const int MaxMonths = 24;

    /// <summary>Same cache lifetime for every user — the permission check runs before the cache is read.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // =============================================================================================
    //  course_analytics
    // =============================================================================================

    [McpServerTool(Name = "course_analytics", Title = "Kurslar analitikasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kurslar kesimida oyma-oy o'quvchilar oqimi: nechtasi KELDI, nechtasi aktivlashdi, nechtasi KETDI "
        + "(churn) va nechtasi kursni TUGATDI (sertifikat bilan — churn emas), oy oxirida nechta faol o'quvchi bor edi. "
        + "Har kurs uchun hozirgi holat (faol / sinov / muzlatilgan, takrorsiz o'quvchilar), guruhlar va o'qituvchilar soni, "
        + "hamda kurslar kesishuvi (nechta o'quvchi 1, 2, 3… kursda faol o'qiydi va qaysi kurslar birga olinadi). "
        + "Guruh almashtirish (7 kungacha tanaffus) ketish hisoblanmaydi — panel bilan bir xil qoida. "
        + "Kutilayotgan oylik tushum faqat moliya ruxsati bo'lsa. "
        + "Per-course monthly student flow (joined/left/completed), current counts and course overlap.")]
    public async Task<string> CourseAnalytics(
        [Description("Nechta oxirgi oy (1..24, sukut 6)")] int? months = null,
        [Description("Faqat bitta kurs (kurs id) — bo'sh bo'lsa barcha kurslar")] string? courseId = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.CourseAnalytics);
        var finance = t.CanReadFinance;
        var n = Math.Clamp(months is null or <= 0 ? DefaultMonths : months.Value, 1, MaxMonths);

        var data = await t.CachedAsync($"course_analytics:{n}:{McpToolContext.Iso(McpToolContext.Today)}", CacheTtl,
            () => BuildCourseAnalyticsAsync(t.Db, n, ct));

        var courses = data.Courses.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(courseId))
        {
            var id = courseId.Trim();
            courses = courses.Where(c => c.Id == id);
            if (!courses.Any())
                return t.Json(new { message = "Kurs topilmadi (yoki analitikada yo'q).", courseId = id }, 0);
        }

        var items = courses.Select(c => new
        {
            c.Id, c.Name, price = c.Price,
            c.Groups, c.Teachers,
            current = new { c.Active, c.Trial, c.Frozen, students = c.Students },
            c.TotalEver,
            monthlyRevenue = finance ? c.MonthlyRevenue : (decimal?)null,
            monthly = c.Monthly.Select(f => new
            {
                f.Month, joined = f.Joined, activated = f.Activated, left = f.Left,
                completed = f.Completed, activeAtMonthEnd = f.ActiveEnd,
            }),
        }).ToList();

        return t.Json(new
        {
            months = data.Months,
            totals = new
            {
                activeStudents = data.ActiveStudents,
                groups = data.TotalGroups,
                monthlyRevenue = finance ? data.MonthlyRevenue : (decimal?)null,
            },
            courses = items,
            overlap = new
            {
                totalStudents = data.Overlap.TotalStudents,
                oneCourse = data.Overlap.OneCourse,
                multiCourse = data.Overlap.MultiCourse,
                buckets = data.Overlap.Buckets.Select(b => new { courses = b.Courses, students = b.Students }),
                pairs = data.Overlap.Pairs.Select(p => new
                {
                    a = new { id = p.AId, name = p.AName },
                    b = new { id = p.BId, name = p.BName },
                    students = p.Students,
                }),
            },
            rules = "keldi = oraliq boshlandi; ketdi = oraliq tugadi va yopgan a'zolik «completed» emas; "
                + "tugatdi = sertifikat bilan yopildi (churn emas); bir o'quvchi kursda bir marta sanaladi; "
                + "arxivlangan o'quvchi joriy sanoqlarga kirmaydi.",
            note = finance ? null : "Oylik tushum ko'rsatilmadi: moliya ruxsati yo'q.",
        }, items.Count);
    }

    // ---- cached aggregate (user-independent) -----------------------------------------------------

    private sealed record FlowRow(string Month, int Joined, int Activated, int Left, int Completed, int ActiveEnd);

    private sealed record CourseRowData(
        string Id, string Name, decimal Price, int Groups, int Teachers,
        int Active, int Trial, int Frozen, int Students, int TotalEver, decimal MonthlyRevenue,
        List<FlowRow> Monthly);

    private sealed record BucketRow(int Courses, int Students);

    private sealed record PairRow(string AId, string AName, string BId, string BName, int Students);

    private sealed record OverlapData(int TotalStudents, int OneCourse, int MultiCourse, List<BucketRow> Buckets, List<PairRow> Pairs);

    private sealed record CourseAnalyticsData(
        List<string> Months, List<CourseRowData> Courses, OverlapData Overlap,
        int ActiveStudents, int TotalGroups, decimal MonthlyRevenue);

    /// <summary>
    /// Faithful copy of the panel's <c>CourseAnalyticsController.BuildAsync</c> (the controller lives in the
    /// Server project): same loads, same projection (incl. <c>PastPeriods</c> — membership-periods.md §7),
    /// same "archived student = left" rule, all arithmetic in <see cref="Services.CourseAnalytics"/>.
    /// </summary>
    private static async Task<CourseAnalyticsData> BuildCourseAnalyticsAsync(IAppDbContext db, int monthCount, CancellationToken ct)
    {
        var months = Services.CourseAnalytics.LastMonths(McpToolContext.Today, monthCount);

        // Archived groups are needed too: the history of pupils who went through them must not vanish.
        var groups = await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.CourseId, g.TeacherId, g.MonthlyFee, g.IsArchived })
            .ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Price }).ToListAsync(ct);
        var memberships = await db.StudentGroups.AsNoTracking()
            .Select(m => new
            {
                m.StudentId, m.GroupId, m.JoinedAt, m.ActivatedAt, m.LeftAt, m.FrozenAt,
                m.Status, m.IsActive,
                m.PastPeriods,   // projection: without it re-activated memberships are counted wrong
            })
            .ToListAsync(ct);

        var groupById = groups.ToDictionary(g => g.Id);
        var archivedStudents = (await db.Students.AsNoTracking().Where(s => s.IsArchived)
            .Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        bool Live(Services.CourseAnalytics.MembershipRow r) => r.IsActive && !archivedStudents.Contains(r.StudentId);

        var rowsByCourse = new Dictionary<string, Dictionary<string, List<Services.CourseAnalytics.MembershipRow>>>();
        foreach (var m in memberships)
        {
            if (!groupById.TryGetValue(m.GroupId, out var g)) continue;
            var cid = g.CourseId ?? "";
            if (cid.Length == 0) continue;   // a group without a course has no meaning per course

            var row = new Services.CourseAnalytics.MembershipRow(
                m.StudentId, cid, m.JoinedAt, m.ActivatedAt ?? "", m.LeftAt, m.FrozenAt ?? "",
                m.Status ?? "", m.IsActive, g.MonthlyFee, m.PastPeriods);

            if (!rowsByCourse.TryGetValue(cid, out var byStudent))
                rowsByCourse[cid] = byStudent = new Dictionary<string, List<Services.CourseAnalytics.MembershipRow>>();
            if (!byStudent.TryGetValue(m.StudentId, out var list))
                byStudent[m.StudentId] = list = new List<Services.CourseAnalytics.MembershipRow>();
            list.Add(row);
        }

        var courseRows = new List<CourseRowData>();
        foreach (var s in subjects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var byStudent = rowsByCourse.GetValueOrDefault(s.Id)
                            ?? new Dictionary<string, List<Services.CourseAnalytics.MembershipRow>>();
            var all = byStudent.Values.SelectMany(v => v).ToList();

            // Current state — DISTINCT students (two groups of one course count once).
            var active = all.Where(r => Live(r) && r.Status == "active").Select(r => r.StudentId).Distinct().Count();
            var trial = all.Where(r => Live(r) && r.Status == "trial").Select(r => r.StudentId).Distinct().Count();
            var frozen = all.Where(r => Live(r) && r.Status == "frozen").Select(r => r.StudentId).Distinct().Count();
            var current = all.Where(Live).Select(r => r.StudentId).Distinct().Count();

            // Expected monthly revenue — per MEMBERSHIP (two groups = two fees), archived pupils untouched.
            var revenue = all.Where(r => r.IsActive && r.Status == "active").Sum(r => r.MonthlyFee);

            var courseGroups = groups.Where(g => g.CourseId == s.Id && !g.IsArchived).ToList();
            courseRows.Add(new CourseRowData(
                s.Id, s.Name, s.Price,
                courseGroups.Count,
                courseGroups.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count(),
                active, trial, frozen, current, byStudent.Count, revenue,
                Services.CourseAnalytics.MonthlyFlow(byStudent, months)
                    .Select(f => new FlowRow(f.Month, f.Joined, f.Activated, f.Left, f.Completed, f.ActiveEnd))
                    .ToList()));
        }

        // Overlap — ACTIVE memberships only (trial / frozen do not count).
        var activeByStudent = new Dictionary<string, HashSet<string>>();
        foreach (var (cid, byStudent) in rowsByCourse)
            foreach (var (studentId, list) in byStudent)
            {
                if (!list.Any(r => Live(r) && r.Status == "active")) continue;
                if (!activeByStudent.TryGetValue(studentId, out var set))
                    activeByStudent[studentId] = set = new HashSet<string>();
                set.Add(cid);
            }

        var (buckets, pairs) = Services.CourseAnalytics.Overlap(activeByStudent);
        var nameById = subjects.ToDictionary(x => x.Id, x => x.Name);
        var overlap = new OverlapData(
            activeByStudent.Count,
            buckets.FirstOrDefault(b => b.Courses == 1).Students,
            buckets.Where(b => b.Courses > 1).Sum(b => b.Students),
            buckets.Select(b => new BucketRow(b.Courses, b.Students)).ToList(),
            pairs.Take(20).Select(p => new PairRow(
                p.AId, nameById.GetValueOrDefault(p.AId, "?"),
                p.BId, nameById.GetValueOrDefault(p.BId, "?"), p.Students)).ToList());

        return new CourseAnalyticsData(
            months, courseRows, overlap,
            activeByStudent.Count,
            groups.Count(g => !g.IsArchived && !string.IsNullOrEmpty(g.CourseId)),
            courseRows.Sum(c => c.MonthlyRevenue));
    }

    // =============================================================================================
    //  school_grades_breakdown
    // =============================================================================================

    [McpServerTool(Name = "school_grades_breakdown", Title = "Sinflar kesimi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Markazda maktabning nechanchi sinf o'quvchilari o'qiyapti: har sinf (1..11), «maktabni bitirgan» va "
        + "«sinfi kiritilmagan» bo'yicha o'quvchilar soni. Sinf har yili 1-sentabrda o'zi oshadi — JORIY sinf olinadi. "
        + "Qamrov: active (sukut — kamida bitta faol guruhda AKTIV o'qiyotganlar), studying (aktiv + sinovda), "
        + "all (arxivdan tashqari barcha o'quvchilar). Har uch qamrovning jami soni ham qaytadi. "
        + "Number of students per school grade (current grade, auto-promoted every September).")]
    public async Task<string> SchoolGradesBreakdown(
        [Description("active (sukut) | studying | all")] string? scope = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var key = (scope ?? "active").Trim().ToLowerInvariant();
        if (key is not ("active" or "studying" or "all"))
            throw new McpException("«scope»: active | studying | all.");
        var db = t.Db;

        // Live membership = open membership in a non-archived group (the same "live" as students_search).
        var live = from sg in db.StudentGroups
                   join g in db.Classes on sg.GroupId equals g.Id
                   where sg.IsActive && !g.IsArchived
                   select new { sg.StudentId, sg.Status };

        IQueryable<Student> Scoped(string s)
        {
            var q = db.Students.Where(x => !x.IsArchived);
            return s switch
            {
                "active" => q.Where(x => live.Any(m => m.StudentId == x.Id && m.Status == "active")),
                "studying" => q.Where(x => live.Any(m => m.StudentId == x.Id && (m.Status == "active" || m.Status == "trial"))),
                _ => q,
            };
        }

        // Grouped in SQL by the stored pair (grade, year) — a handful of rows; the CURRENT grade is derived below.
        var pairs = await Scoped(key)
            .GroupBy(s => new { s.SchoolGrade, s.SchoolGradeYear })
            .Select(g => new { g.Key.SchoolGrade, g.Key.SchoolGradeYear, Count = g.Count() })
            .ToListAsync(ct);

        var today = McpToolContext.Today;
        var counts = new Dictionary<int, int>();
        var notSet = 0;
        foreach (var p in pairs)
        {
            var now = SchoolGrades.Current(p.SchoolGrade, p.SchoolGradeYear, today);
            if (now is { } gr && SchoolGrades.IsValid(gr)) counts[gr] = counts.GetValueOrDefault(gr) + p.Count;
            else notSet += p.Count;
        }

        var rows = Enumerable.Range(SchoolGrades.Min, SchoolGrades.Graduated)
            .Select(g => new { grade = (int?)g, label = GradeLabel(g), count = counts.GetValueOrDefault(g) })
            .Append(new { grade = (int?)null, label = "Sinfi kiritilmagan", count = notSet })
            .ToList();
        var total = rows.Sum(r => r.count);
        var top = rows.Where(r => r.grade is not null && r.count > 0).OrderByDescending(r => r.count).FirstOrDefault();

        var scopeCounts = new List<object>();
        foreach (var s in new[] { "active", "studying", "all" })
            scopeCounts.Add(new { scope = s, count = s == key ? total : await Scoped(s).CountAsync(ct) });

        return t.Json(new
        {
            scope = key,
            total,
            rows = rows.Select(r => new
            {
                r.grade, r.label, r.count,
                percent = total == 0 ? 0 : Math.Round(r.count * 100.0 / total, 1),
            }),
            mostCommon = top is null ? null : new { top.grade, top.label, top.count },
            scopeCounts,
        }, rows.Count);
    }

    private static string GradeLabel(int grade) =>
        grade == SchoolGrades.Graduated ? "Maktabni bitirgan" : $"{grade}-sinf";
}
