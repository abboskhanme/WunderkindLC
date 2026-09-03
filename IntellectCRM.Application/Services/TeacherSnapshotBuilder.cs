using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// O'QITUVCHI DAFTARI — bitta o'qituvchi haqida BARCHA ma'lumotni bir joyga yig'adi (AI tahlili uchun
/// va profildagi "AI tahlil" tabidagi diagrammalar uchun). Raqamlar DETERMINISTIK (AI emas) va
/// mavjud yagona manbalardan olinadi:
/// <list type="bullet">
/// <item>o'quvchi OQIMI (kelgan/aktivlashgan/muzlatilgan/ketgan) — <see cref="StudentGroup"/> sanalari,
///   <see cref="MembershipLifecycle"/> bilan bir xil ta'rif (performance/hisobot bilan mos);</item>
/// <item>KETISH SABABLARI — <see cref="AuditLog"/> ("Membership" yozuvlaridagi "sabab: ...") va
///   markazdan butunlay arxivlangan o'quvchilar (<see cref="ArchivedRecord"/>);</item>
/// <item>JURNAL INTIZOMI — <see cref="SalaryJournalStats"/> (reja/o'tilgan/o'tkazib yuborilgan darslar,
///   "o'z vaqtida to'ldirish" shu yerdan) + <see cref="LessonNote"/> (mavzu/uy vazifa/davomat olindi);</item>
/// <item>RIVOJLANISH — oyma-oy o'rtacha baho, o'quvchilar davomati va bali
///   (<see cref="StudentBallService.TeacherAsync"/>), testlar natijasi;</item>
/// <item>o'qituvchining O'Z davomati (<see cref="TeacherAttendance"/>) va guruh ota-onalaridan kelgan
///   shikoyat/takliflar (<see cref="Feedback"/>).</item>
/// </list>
/// </summary>
public static class TeacherSnapshotBuilder
{
    /// <summary>Tahlil oynasi — necha oy orqaga qaraladi (joriy oy ham kiradi).</summary>
    public const int MonthsWindow = 12;

    /// <summary>"yyyy-MM" oy yorlig'i ISO sanadan (bo'sh/kalta bo'lsa "").</summary>
    private static string MonthOf(string? date) =>
        !string.IsNullOrEmpty(date) && date.Length >= 7 ? date[..7] : "";

    private static int Pct(int a, int b) => b <= 0 ? 0 : (int)Math.Round(a * 100.0 / b);

    /// <summary>O'qituvchining to'liq ko'rsatkichlari + AI promptiga beriladigan JSON snapshot.</summary>
    public static async Task<(TeacherAiMetricsDto Metrics, string SnapshotJson)> BuildAsync(
        IAppDbContext db, Teacher teacher, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var curMonth = today.ToString("yyyy-MM");
        var prevMonth = today.AddMonths(-1).ToString("yyyy-MM");
        var fromMonth = today.AddMonths(-(MonthsWindow - 1)).ToString("yyyy-MM");
        var fromDate = $"{fromMonth}-01";
        var sinceDt = AppClock.Now.AddMonths(-MonthsWindow);
        var months = TuitionService.MonthRange(fromMonth, curMonth).ToList();

        // ---------- 1. Guruhlar ----------
        var groups = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacher.Id)
            .ToListAsync(ct);
        var activeGroups = groups.Where(g => !g.IsArchived).ToList();
        var groupIds = groups.Select(g => g.Id).ToList();
        var activeGroupIds = activeGroups.Select(g => g.Id).ToHashSet();
        var courseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // ---------- 2. A'zoliklar (o'quvchi oqimi) ----------
        // Lifecycle FAQAT arxivlanmagan guruhlar bo'yicha — guruh tugaganda a'zoliklar ommaviy yopiladi,
        // ular "ketgan" emas (TeacherActivityReport/performance bilan bir xil qoida).
        var memberships = groupIds.Count == 0
            ? new List<StudentGroup>()
            : await db.StudentGroups.AsNoTracking().Where(sg => groupIds.Contains(sg.GroupId)).ToListAsync(ct);
        var liveMembers = memberships.Where(m => activeGroupIds.Contains(m.GroupId)).ToList();
        var tally = MembershipLifecycle.Tally(liveMembers.Select(m => (m.Status, m.IsActive, m.LeftAt)));

        var flow = months.Select(m => new TeacherFlowPointDto(
            m,
            liveMembers.Count(x => MonthOf(x.JoinedAt) == m),
            // Yopilgan davrlar ham: qayta aktivlashtirilgan a'zolikda eski aktivlashish
            // hodisasi yo'qolib, grafikda keyingi oyga ko'chib ketardi.
            liveMembers.Count(x => MembershipLifecycle.ActivatedInMonth(x, m)),
            liveMembers.Count(x => MonthOf(x.FrozenAt) == m),
            liveMembers.Count(x => MonthOf(x.LeftAt) == m))).ToList();

        var myStudentIds = memberships.Select(m => m.StudentId).ToHashSet();

        // ---------- 3. Ketish sabablari ----------
        var reasons = await DepartureReasonsAsync(db, groupIds, myStudentIds, fromDate, ct);

        // ---------- 4. Jurnal intizomi (reja / o'tilgan / o'tkazib yuborilgan) ----------
        var policy = await JournalPolicy.GetAsync(db);
        var startDate = TeacherSalaryCalc.StartDateOf(teacher);
        var lessonStats = activeGroups.Count == 0
            ? new Dictionary<(string Month, string GroupId), SalaryJournalStats.Stat>()
            : await SalaryJournalStats.BuildAsync(db,
                activeGroups.Select(g => new SalaryJournalStats.GroupInfo(g.Id, g.Name, g.Days, g.StartDate, g.EndDate)).ToList(),
                fromMonth, curMonth, policy.SalaryGraceDays, startDate);

        var notes = groupIds.Count == 0
            ? new List<LessonNote>()
            : await db.LessonNotes.AsNoTracking()
                .Where(n => groupIds.Contains(n.ClassId) && string.Compare(n.Date, fromDate) >= 0)
                .ToListAsync(ct);
        var conductedNotes = notes.Where(n => n.Conducted).ToList();

        var gradeRows = groupIds.Count == 0
            ? new List<(string Date, string ClassId, int Grade)>()
            : (await db.JournalEntries.AsNoTracking()
                .Where(e => groupIds.Contains(e.ClassId) && e.Grade != null && string.Compare(e.Date, fromDate) >= 0)
                .Select(e => new { e.Date, e.ClassId, e.Grade })
                .ToListAsync(ct))
                .Select(e => (e.Date, e.ClassId, Grade: e.Grade!.Value)).ToList();

        double AvgGradeIn(IEnumerable<(string Date, string ClassId, int Grade)> rows)
        {
            var list = rows.Select(r => (double)r.Grade).ToList();
            return list.Count > 0 ? Math.Round(list.Average(), 2) : 0;
        }

        var journalByMonth = months.Select(m =>
        {
            var planned = lessonStats.Where(kv => kv.Key.Month == m).Sum(kv => kv.Value.Planned);
            var done = lessonStats.Where(kv => kv.Key.Month == m).Sum(kv => kv.Value.Conducted);
            var missed = lessonStats.Where(kv => kv.Key.Month == m).Sum(kv => kv.Value.Missed);
            var mNotes = conductedNotes.Where(n => MonthOf(n.Date) == m).ToList();
            var mGrades = gradeRows.Where(g => MonthOf(g.Date) == m).ToList();
            return new TeacherJournalMonthDto(
                m, planned, done, missed,
                Pct(mNotes.Count(n => !string.IsNullOrWhiteSpace(n.Topic)), mNotes.Count),
                Pct(mNotes.Count(n => !string.IsNullOrWhiteSpace(n.Homework)), mNotes.Count),
                Pct(mNotes.Count(n => n.AttendanceTaken), mNotes.Count),
                mGrades.Count, AvgGradeIn(mGrades));
        }).ToList();

        var plannedTotal = lessonStats.Sum(kv => kv.Value.Planned);
        var conductedTotal = lessonStats.Sum(kv => kv.Value.Conducted);
        var missedTotal = lessonStats.Sum(kv => kv.Value.Missed);
        // "O'z vaqtida to'ldirilmagan" darslar — muhlati o'tgan, lekin belgilanmagan sanalar (eng yangisi tepada).
        var recentMissed = lessonStats
            .SelectMany(kv => kv.Value.MissedDates.Select(d => (Date: d, Group: kv.Key.GroupId)))
            .OrderByDescending(x => x.Date, StringComparer.Ordinal)
            .Take(15)
            .Select(x => $"{x.Date} ({groups.FirstOrDefault(g => g.Id == x.Group)?.Name ?? "—"})")
            .ToList();

        // ---------- 5. Rivojlanish: ball, davomat, testlar ----------
        var rating = await StudentBallService.TeacherAsync(db, teacher);
        var attendances = rating.Rows.Where(r => r.Attendance != null).Select(r => r.Attendance!.Value).ToList();
        var attendancePct = attendances.Count > 0 ? Math.Round(attendances.Average(), 1) : 0;

        var tests = groupIds.Count == 0
            ? new List<TestResult>()
            : await db.TestResults.AsNoTracking()
                .Where(t => groupIds.Contains(t.GroupId) && string.Compare(t.Date, fromDate) >= 0)
                .ToListAsync(ct);
        var testIds = tests.Select(t => t.Id).ToList();
        var testScores = testIds.Count == 0
            ? new List<TestScore>()
            : await db.TestScores.AsNoTracking().Where(s => testIds.Contains(s.TestResultId)).ToListAsync(ct);
        var maxByTest = tests.ToDictionary(t => t.Id, t => t.MaxScore);
        var testPcts = testScores
            .Where(s => maxByTest.TryGetValue(s.TestResultId, out var mx) && mx > 0)
            .Select(s => (double)(s.Score / maxByTest[s.TestResultId]) * 100)
            .ToList();
        var testAvgPct = testPcts.Count > 0 ? Math.Round(testPcts.Average(), 1) : 0;

        // ---------- 6. O'qituvchining O'Z davomati ----------
        var myAttendance = await db.TeacherAttendances.AsNoTracking()
            .Where(a => a.TeacherId == teacher.Id && string.Compare(a.Date, fromDate) >= 0)
            .Select(a => a.Status)
            .ToListAsync(ct);
        var present = myAttendance.Count(s => s == "present");
        var late = myAttendance.Count(s => s == "late");
        var absent = myAttendance.Count(s => s == "absent");

        // ---------- 7. Ota-onalar fikri (shu o'qituvchi guruhlaridagi o'quvchilardan) ----------
        var feedback = (await db.Feedbacks.AsNoTracking()
                .Where(f => f.CreatedAt >= sinceDt)
                .Select(f => new { f.StudentId, f.Type, f.Text, f.CreatedAt })
                .ToListAsync(ct))
            .Where(f => myStudentIds.Contains(f.StudentId))
            .OrderByDescending(f => f.CreatedAt)
            .ToList();
        var complaints = feedback.Count(f => f.Type == "complaint");
        var suggestions = feedback.Count(f => f.Type != "complaint");
        var feedbackTexts = feedback.Take(6)
            .Select(f => $"[{(f.Type == "complaint" ? "shikoyat" : "taklif")}] " + Trim(f.Text, 200))
            .ToList();

        // ---------- 7b. O'QUVCHILARNING O'QITUVCHI HAQIDAGI FIKRI ----------
        // Admin o'quvchi profilida yozib boradigan MATNLI mulohazalar. AI aynan shu matnlardan
        // takrorlanuvchi naqshlarni (kuchli tomon / o'sish nuqtasi) ajratadi.
        // MAXFIYLIK: o'quvchi ISMI berilmaydi (xulosa o'qituvchiga ko'rsatiladi) — faqat sana va
        // guruh nomi; xom matn o'qituvchi profilida hech qachon chiqmaydi.
        var (reviewCount, reviewTexts) = await TeacherReviewService.TextsForTeacherAsync(
            db, teacher.Id, sinceDt.ToString("yyyy-MM-ddTHH:mm:ss"), max: 25, ct);

        // ---------- 8. Guruh kesimi ----------
        var groupStats = groups.Select(g =>
        {
            var mem = memberships.Where(m => m.GroupId == g.Id).ToList();
            var t = MembershipLifecycle.Tally(mem.Select(m => (m.Status, m.IsActive, m.LeftAt)));
            var st = lessonStats.Where(kv => kv.Key.GroupId == g.Id).ToList();
            return new TeacherGroupStatDto(
                g.Id, g.Name, courseNames.GetValueOrDefault(g.CourseId, ""), g.IsArchived,
                t.Active, t.Trial, t.Frozen, t.Left,
                st.Sum(x => x.Value.Planned), st.Sum(x => x.Value.Conducted), st.Sum(x => x.Value.Missed),
                AvgGradeIn(gradeRows.Where(x => x.ClassId == g.Id)));
        })
        .OrderBy(g => g.IsArchived).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        var metrics = new TeacherAiMetricsDto(
            groups.Count, activeGroups.Count,
            tally.Came, tally.Active, tally.Trial, tally.Frozen, tally.Left,
            tally.Retention, tally.Loss,
            plannedTotal, conductedTotal, missedTotal, Pct(conductedTotal, plannedTotal),
            Pct(conductedNotes.Count(n => !string.IsNullOrWhiteSpace(n.Topic)), conductedNotes.Count),
            Pct(conductedNotes.Count(n => !string.IsNullOrWhiteSpace(n.Homework)), conductedNotes.Count),
            Pct(conductedNotes.Count(n => n.AttendanceTaken), conductedNotes.Count),
            gradeRows.Count,
            AvgGradeIn(gradeRows.Where(g => MonthOf(g.Date) == curMonth)),
            AvgGradeIn(gradeRows.Where(g => MonthOf(g.Date) == prevMonth)),
            attendancePct, rating.AverageBall,
            tests.Count, testAvgPct,
            present, late, absent,
            complaints, suggestions,
            flow, journalByMonth, reasons, groupStats, recentMissed,
            reviewCount);

        // ---------- 9. AI promptiga beriladigan snapshot ----------
        var snapshot = new
        {
            oqituvchi = new
            {
                ism = teacher.FullName,
                toifa = teacher.Category,
                ishBoshlagan = startDate ?? "",
                fanlar = teacher.SubjectIds.Select(id => courseNames.GetValueOrDefault(id, id)).ToList(),
            },
            sana = today.ToString("yyyy-MM-dd"),
            davr = new { boshi = fromMonth, oxiri = curMonth },
            guruhlar = new
            {
                jami = groups.Count,
                faol = activeGroups.Count,
                royxat = groupStats,
            },
            oquvchiOqimi = new
            {
                jamiKelgan = tally.Came,
                hozirFaol = tally.Active,
                sinovda = tally.Trial,
                muzlatilgan = tally.Frozen,
                ketgan = tally.Left,
                saqlashFoizi = tally.Retention,
                yoqotishFoizi = tally.Loss,
                oymaOy = flow,
            },
            ketishSabablari = reasons,
            jurnal = new
            {
                rejadagiDarslar = plannedTotal,
                otilganDarslar = conductedTotal,
                belgilanmaganDarslar = missedTotal,
                bajarilishFoizi = Pct(conductedTotal, plannedTotal),
                mavzuFoizi = metrics.TopicPct,
                uyVazifaFoizi = metrics.HomeworkPct,
                davomatOlinganFoizi = metrics.AttendanceTakenPct,
                qoyilganBaholar = gradeRows.Count,
                oymaOy = journalByMonth,
                oxirgiBelgilanmaganSanalar = recentMissed,
                izoh = "belgilanmaganDarslar = muhlati o'tgan, lekin jurnalda \"o'tildi\" deb belgilanmagan darslar " +
                       $"(muhlat: {policy.SalaryGraceDays} kun) — jurnalni o'z vaqtida to'ldirish ko'rsatkichi",
            },
            rivojlanish = new
            {
                ortachaBahoShuOy = metrics.AvgGradeThisMonth,
                ortachaBahoOtganOy = metrics.AvgGradePrevMonth,
                oquvchilarDavomatiFoiz = attendancePct,
                ortachaBall = rating.AverageBall,
                testlarSoni = tests.Count,
                testOrtachaFoiz = testAvgPct,
            },
            oqituvchiDavomati = new { keldi = present, kechikdi = late, kelmadi = absent },
            otaOnalarFikri = new { shikoyatlar = complaints, takliflar = suggestions, oxirgilari = feedbackTexts },
            // O'quvchilarning o'qituvchi haqidagi fikri — MATNLI tahlilning asosiy manbai.
            oquvchilarFikri = new { soni = reviewCount, matnlar = reviewTexts },
        };

        var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });

        return (metrics, snapshotJson);
    }

    /// <summary>
    /// Ketish/muzlatish SABABLARI. Sabab alohida ustunda saqlanmaydi — u amal bajarilganda audit
    /// yozuviga ("... — sabab: X") yoziladi, shuning uchun shu yerdan ajratib olinadi. Qo'shimcha
    /// manba: markazdan butunlay arxivlangan o'quvchilar (<see cref="ArchivedRecord.Reason"/>).
    /// </summary>
    private static async Task<List<CenterPointDto>> DepartureReasonsAsync(
        IAppDbContext db, List<string> groupIds, HashSet<string> studentIds, string fromDate, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();
        void Add(string? label)
        {
            var key = string.IsNullOrWhiteSpace(label) ? "Sabab ko'rsatilmagan" : label!.Trim();
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        if (groupIds.Count > 0)
        {
            var groupSet = groupIds.ToHashSet();
            var logs = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "Membership" && string.Compare(a.Timestamp, fromDate) >= 0)
                .Select(a => new { a.EntityId, a.Summary })
                .ToListAsync(ct);
            foreach (var log in logs)
            {
                // EntityId = "{groupId}:{studentId}"
                var sep = log.EntityId.IndexOf(':');
                if (sep <= 0 || !groupSet.Contains(log.EntityId[..sep])) continue;
                var s = log.Summary ?? "";
                // Faqat KETISH hodisalari (chiqarish/muzlatish) — qo'shish/aktivlashtirish emas.
                if (!s.StartsWith("Guruhdan chiqarildi") && !s.StartsWith("Muzlatildi")) continue;
                const string marker = "sabab: ";
                var i = s.IndexOf(marker, StringComparison.Ordinal);
                Add(i < 0 ? null : s[(i + marker.Length)..]);
            }
        }

        if (studentIds.Count > 0)
        {
            var archived = await db.ArchivedRecords.AsNoTracking()
                .Where(a => a.Type == "student" && string.Compare(a.DeletedAt, fromDate) >= 0)
                .Select(a => new { a.EntityId, a.Reason })
                .ToListAsync(ct);
            foreach (var a in archived)
                if (studentIds.Contains(a.EntityId)) Add(a.Reason);
        }

        return counts
            .Select(kv => new CenterPointDto(kv.Key, kv.Value))
            .OrderByDescending(p => p.Value).ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Trim(string? s, int max)
    {
        s = (s ?? "").Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "...";
    }
}
