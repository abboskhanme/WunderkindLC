using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// GURUH DAFTARI — bitta guruh haqida BARCHA ma'lumotni bir joyga yig'adi (AI tahlili uchun va guruh
/// sahifasidagi "AI tahlil" tabidagi diagrammalar uchun). Raqamlar DETERMINISTIK (AI emas) va MAVJUD
/// yagona manbalardan olinadi — yangi hisoblash mantig'i yaratilmaydi:
/// <list type="bullet">
/// <item><b>a'zolik oqimi</b> (kelgan/aktivlashgan/muzlatilgan/ketgan) — <see cref="StudentGroup"/>
///   sanalari + <see cref="MembershipLifecycle"/> (performance/hisobot bilan bir xil ta'rif);</item>
/// <item><b>ketish/muzlatish sabablari</b> — <see cref="AuditLog"/> ("Membership" yozuvidagi
///   "sabab: ...") va markazdan arxivlanganlar uchun <see cref="ArchivedRecord.Reason"/>;</item>
/// <item><b>davomat</b> — jurnal "Davomat" tabi bilan AYNAN bir xil qoida: o'tilgan darslar
///   (<see cref="LessonNote.Conducted"/>) a'zolik oynasi ichida (qo'shilgan..muzlatilgan/chiqqan),
///   qoldirgan = sababli belgi (kech kelish MUSTASNO);</item>
/// <item><b>jurnal intizomi</b> — <see cref="SalaryJournalStats"/> (reja/o'tilgan/muhlati o'tgan,
///   lekin belgilanmagan darslar) + mavzu/uy vazifa/davomat olinishi;</item>
/// <item><b>o'zlashtirish</b> — baholar dinamikasi, ball (<see cref="StudentBallService"/>), uy vazifa
///   va xulq belgilari, dastur qamrovi (<see cref="CurriculumForecast"/>);</item>
/// <item><b>imtihonlar</b> — <see cref="TestResult"/>/<see cref="TestScore"/>;</item>
/// <item><b>to'lovlar</b> — <see cref="CourseFinanceReport"/> (hisoblangan/yig'ilgan/qarz) va
///   <see cref="GroupBalanceService"/> (per-o'quvchi guruh balansi). FAQAT moliya ruxsati bo'lganda.</item>
/// </list>
/// </summary>
public static class GroupSnapshotBuilder
{
    /// <summary>Tahlil oynasi — necha oy orqaga qaraladi (joriy oy ham kiradi).</summary>
    public const int MonthsWindow = 12;

    private static readonly string[] WeekdayShort = { "Du", "Se", "Chor", "Pay", "Jum", "Shan", "Yak" };

    private static string MonthOf(string? date) =>
        !string.IsNullOrEmpty(date) && date.Length >= 7 ? date[..7] : "";

    private static int Pct(int a, int b) => b <= 0 ? 0 : (int)Math.Round(a * 100.0 / b);

    /// <summary>Guruhning to'liq ko'rsatkichlari + AI promptiga beriladigan JSON snapshot.
    /// <paramref name="includeFinance"/>=false bo'lsa to'lov raqamlari umuman yig'ilmaydi
    /// (moliya ruxsati yo'q foydalanuvchi tahlilda ham summalarni ko'rmasin).</summary>
    public static async Task<(GroupAiMetricsDto Metrics, string SnapshotJson)> BuildAsync(
        IAppDbContext db, Group group, bool includeFinance, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var curMonth = today.ToString("yyyy-MM");
        var prevMonth = today.AddMonths(-1).ToString("yyyy-MM");
        var fromMonth = today.AddMonths(-(MonthsWindow - 1)).ToString("yyyy-MM");
        var fromDate = $"{fromMonth}-01";
        var months = TuitionService.MonthRange(fromMonth, curMonth).ToList();
        var gid = group.Id;

        // ---------- 1. Guruh pasporti ----------
        var courseName = string.IsNullOrEmpty(group.CourseId)
            ? ""
            : (await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == group.CourseId, ct))?.Name ?? "";
        var teacherName = string.IsNullOrEmpty(group.TeacherId)
            ? ""
            : (await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == group.TeacherId, ct))?.FullName ?? "";
        var daysLabel = string.Join(", ", group.Days.Where(d => d is >= 0 and <= 6).Select(d => WeekdayShort[d]));
        var timeLabel = string.IsNullOrEmpty(group.StartTime) ? "" : $"{group.StartTime}–{group.EndTime}";

        // ---------- 2. A'zolik oqimi ----------
        var memberships = await db.StudentGroups.AsNoTracking().Where(sg => sg.GroupId == gid).ToListAsync(ct);
        var tally = MembershipLifecycle.Tally(memberships.Select(m => (m.Status, m.IsActive, m.LeftAt)));
        var flow = months.Select(m => new GroupFlowPointDto(
            m,
            memberships.Count(x => MonthOf(x.JoinedAt) == m),
            // Yopilgan davrlar ham: qayta aktivlashtirilgan a'zolikda eski aktivlashish
            // hodisasi yo'qolib, grafikda keyingi oyga ko'chib ketardi.
            memberships.Count(x => MembershipLifecycle.ActivatedInMonth(x, m)),
            memberships.Count(x => MonthOf(x.FrozenAt) == m),
            memberships.Count(x => MonthOf(x.LeftAt) == m))).ToList();

        var studentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var studentNames = studentIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        // ---------- 3. Ketish / muzlatish sabablari ----------
        var departureReasons = await DepartureReasonsAsync(db, gid, studentIds.ToHashSet(), fromDate, ct);

        // ---------- 4. Jurnal intizomi (reja / o'tilgan / belgilanmagan) ----------
        var policy = await JournalPolicy.GetAsync(db);
        var lessonStats = await SalaryJournalStats.BuildAsync(db,
            new List<SalaryJournalStats.GroupInfo>
            {
                new(group.Id, group.Name, group.Days, group.StartDate, group.EndDate),
            },
            fromMonth, curMonth, policy.SalaryGraceDays, notBefore: null);

        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => n.ClassId == gid && string.Compare(n.Date, fromDate) >= 0)
            .ToListAsync(ct);
        var conductedNotes = notes.Where(n => n.Conducted).ToList();
        var conductedDates = conductedNotes.Select(n => n.Date).Distinct()
            .OrderBy(d => d, StringComparer.Ordinal).ToList();

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == gid && string.Compare(e.Date, fromDate) >= 0)
            .ToListAsync(ct);

        var reasons = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var reasonById = reasons.ToDictionary(r => r.Id);
        var lateIds = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();

        // ---------- 5. Davomat (jurnaldagi "Davomat" tabi bilan bir xil qoida) ----------
        var entryByKey = entries
            .Where(e => e.ReasonId != null)
            .GroupBy(e => (e.StudentId, e.Date))
            .ToDictionary(g => g.Key, g => g.First().ReasonId!);

        (int Held, int Absent, int? Pct) AttendanceOf(StudentGroup m, string? monthFilter = null)
        {
            var start = JournalService.MemberStart(m);
            var end = m.Status == "frozen" && m.FrozenAt is { Length: >= 10 } ? m.FrozenAt[..10]
                    : !m.IsActive && m.LeftAt is { Length: >= 10 } ? m.LeftAt[..10]
                    : null;
            var held = 0;
            var absent = 0;
            foreach (var d in conductedDates)
            {
                if (monthFilter != null && MonthOf(d) != monthFilter) continue;
                if (start != null && string.CompareOrdinal(d, start) < 0) continue;
                if (end != null && string.CompareOrdinal(d, end) > 0) continue;
                held++;
                if (entryByKey.TryGetValue((m.StudentId, d), out var rid) && !lateIds.Contains(rid)) absent++;
            }
            return (held, absent, held > 0 ? (int)Math.Round((held - absent) * 100.0 / held) : null);
        }

        var attendanceAll = memberships.Select(m => AttendanceOf(m)).ToList();
        var attendancePcts = attendanceAll.Where(a => a.Pct != null).Select(a => a.Pct!.Value).ToList();
        var groupAttendancePct = attendancePcts.Count > 0 ? (int)Math.Round(attendancePcts.Average()) : 0;

        var absenceReasonCounts = entries
            .Where(e => e.ReasonId != null && reasonById.ContainsKey(e.ReasonId))
            .GroupBy(e => reasonById[e.ReasonId!].Name)
            .Select(g => new CenterPointDto(g.Key, g.Count()))
            .OrderByDescending(p => p.Value).ToList();
        var absenceCount = entries.Count(e => e.ReasonId != null && !lateIds.Contains(e.ReasonId));
        var lateCount = entries.Count(e => e.ReasonId != null && lateIds.Contains(e.ReasonId));

        // ---------- 6. O'zlashtirish ----------
        var gradeEntries = entries.Where(e => e.Grade != null).ToList();
        double AvgGradeIn(IEnumerable<JournalEntry> rows)
        {
            var list = rows.Select(r => (double)r.Grade!.Value).ToList();
            return list.Count > 0 ? Math.Round(list.Average(), 2) : 0;
        }

        var balls = await StudentBallService.ComputeAsync(db, new List<string> { gid });
        var ballValues = memberships.Where(m => m.IsActive)
            .Select(m => balls.GetValueOrDefault(m.StudentId, new StudentBallService.BallStat(0, 0, 0)).Ball)
            .ToList();
        var avgBall = ballValues.Count > 0 ? Math.Round(ballValues.Average(), 1) : 0;

        // ---------- 7. Imtihonlar / testlar ----------
        var tests = await db.TestResults.AsNoTracking()
            .Where(t => t.GroupId == gid && string.Compare(t.Date, fromDate) >= 0)
            .ToListAsync(ct);
        var testIds = tests.Select(t => t.Id).ToList();
        var testScores = testIds.Count == 0
            ? new List<TestScore>()
            : await db.TestScores.AsNoTracking().Where(s => testIds.Contains(s.TestResultId)).ToListAsync(ct);
        var scoresByTest = testScores.GroupBy(s => s.TestResultId).ToDictionary(g => g.Key, g => g.ToList());
        var activeCount = tally.Active + tally.Trial + tally.Frozen;
        var testStats = tests
            .OrderByDescending(t => t.Date, StringComparer.Ordinal)
            .Select(t =>
            {
                var sc = scoresByTest.GetValueOrDefault(t.Id) ?? new List<TestScore>();
                var pct = t.MaxScore > 0 && sc.Count > 0
                    ? Math.Round(sc.Average(x => (double)(x.Score / t.MaxScore)) * 100, 1)
                    : 0;
                return new GroupTestStatDto(
                    t.Id, t.Name, t.Date, string.IsNullOrEmpty(t.Mode) ? "offline" : t.Mode,
                    t.MaxScore, sc.Count, activeCount, pct);
            })
            .ToList();
        var testAvgPct = testStats.Count(t => t.Scored > 0) > 0
            ? Math.Round(testStats.Where(t => t.Scored > 0).Average(t => t.AvgPct), 1)
            : 0;

        // ---------- 8. To'lovlar (faqat moliya ruxsati bo'lsa) ----------
        decimal billed = 0, collected = 0, debt = 0;
        int paidCount = 0, unpaidCount = 0;
        var debtByStudent = new Dictionary<string, decimal>();
        var billedByMonth = new Dictionary<string, decimal>();
        var collectedByMonth = new Dictionary<string, decimal>();
        if (includeFinance)
        {
            var report = await CourseFinanceReport.BuildGroupPaymentsAsync(db, gid, fromDate, today.ToString("yyyy-MM-dd"));
            billed = report.Billed;
            collected = report.Collected;
            paidCount = report.PaidCount;
            unpaidCount = report.UnpaidCount;
            debt = report.Rows.Sum(r => Math.Max(0m, r.Debt));
            debtByStudent = report.Rows.ToDictionary(r => r.StudentId, r => r.Debt);

            var charges = await db.MonthlyCharges.AsNoTracking()
                .Where(c => c.GroupId == gid && string.Compare(c.Month, fromMonth) >= 0)
                .Select(c => new { c.Month, c.Amount, c.Discount })
                .ToListAsync(ct);
            billedByMonth = charges.GroupBy(c => c.Month)
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, x.Amount - x.Discount)));

            var payments = await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.GroupId == gid && t.Direction == "income" && t.Category == "tuition"
                            && string.Compare(t.Date, fromDate) >= 0)
                .Select(t => new { t.Date, t.Amount })
                .ToListAsync(ct);
            collectedByMonth = payments.Where(p => p.Date.Length >= 7)
                .GroupBy(p => p.Date[..7]).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        }

        // ---------- 9. Oyma-oy kesim ----------
        var monthStats = months.Select(m =>
        {
            var st = lessonStats.Where(kv => kv.Key.Month == m).ToList();
            var mAtt = memberships.Select(x => AttendanceOf(x, m)).Where(a => a.Pct != null).Select(a => a.Pct!.Value).ToList();
            var mGrades = gradeEntries.Where(e => MonthOf(e.Date) == m).ToList();
            return new GroupMonthStatDto(
                m, st.Sum(x => x.Value.Planned), st.Sum(x => x.Value.Conducted), st.Sum(x => x.Value.Missed),
                mAtt.Count > 0 ? (int)Math.Round(mAtt.Average()) : 0,
                mGrades.Count, AvgGradeIn(mGrades),
                billedByMonth.GetValueOrDefault(m, 0m), collectedByMonth.GetValueOrDefault(m, 0m));
        }).ToList();

        var plannedTotal = lessonStats.Sum(kv => kv.Value.Planned);
        var conductedTotal = lessonStats.Sum(kv => kv.Value.Conducted);
        var missedTotal = lessonStats.Sum(kv => kv.Value.Missed);
        var recentMissed = lessonStats.SelectMany(kv => kv.Value.MissedDates)
            .OrderByDescending(d => d, StringComparer.Ordinal).Take(15).ToList();

        // ---------- 10. O'quvchilar kesimi (hozirgi a'zolar) ----------
        var students = memberships
            .Where(m => m.IsActive)
            .Select(m =>
            {
                var att = AttendanceOf(m);
                var b = balls.GetValueOrDefault(m.StudentId, new StudentBallService.BallStat(0, 0, 0));
                return new GroupStudentStatDto(
                    m.StudentId, studentNames.GetValueOrDefault(m.StudentId, "—"), m.Status,
                    b.Ball, b.Average, att.Pct, att.Absent,
                    includeFinance ? debtByStudent.GetValueOrDefault(m.StudentId, 0m) : 0m);
            })
            .OrderByDescending(s => s.Ball).ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---------- 11. Dastur qamrovi ----------
        var curriculum = await CurriculumForecast.BuildGroupAsync(db, group);

        var metrics = new GroupAiMetricsDto(
            group.Name, courseName, teacherName, daysLabel, timeLabel,
            group.StartDate ?? "", group.EndDate ?? "", group.IsArchived, group.Capacity, group.MonthlyFee,
            tally.Came, tally.Active, tally.Trial, tally.Frozen, tally.Left,
            tally.Retention, tally.Loss,
            group.Capacity > 0 ? Pct(tally.Active + tally.Trial, group.Capacity) : 0,
            plannedTotal, conductedTotal, missedTotal, Pct(conductedTotal, plannedTotal),
            Pct(conductedNotes.Count(n => !string.IsNullOrWhiteSpace(n.Topic)), conductedNotes.Count),
            Pct(conductedNotes.Count(n => !string.IsNullOrWhiteSpace(n.Homework)), conductedNotes.Count),
            Pct(conductedNotes.Count(n => n.AttendanceTaken), conductedNotes.Count),
            groupAttendancePct, absenceCount, lateCount,
            gradeEntries.Count,
            AvgGradeIn(gradeEntries.Where(e => MonthOf(e.Date) == curMonth)),
            AvgGradeIn(gradeEntries.Where(e => MonthOf(e.Date) == prevMonth)),
            avgBall,
            entries.Count(e => e.Homework is 1 or 3), entries.Count(e => e.Homework == 2),
            entries.Count(e => e.Behavior == 1), entries.Count(e => e.Behavior == 2),
            testStats.Count, testAvgPct,
            includeFinance, billed, collected,
            billed > 0 ? (int)Math.Round(collected / billed * 100) : 0,
            debt, paidCount, unpaidCount,
            curriculum.TotalItems, curriculum.CoveredCount, curriculum.RemainingItems, curriculum.EstFinishDate,
            flow, monthStats, departureReasons, absenceReasonCounts, testStats, students, recentMissed);

        // ---------- 12. AI promptiga beriladigan snapshot ----------
        var snapshot = new
        {
            sana = today.ToString("yyyy-MM-dd"),
            davr = new { boshi = fromMonth, oxiri = curMonth },
            guruh = new
            {
                nom = group.Name,
                kurs = courseName,
                oqituvchi = teacherName,
                darsKunlari = daysLabel,
                vaqt = timeLabel,
                boshlanish = group.StartDate ?? "",
                tugash = group.EndDate ?? "",
                arxivlangan = group.IsArchived,
                sigim = group.Capacity,
                oylikNarx = group.MonthlyFee,
                toldirilganlikFoizi = metrics.FillPct,
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
            ketishSabablari = departureReasons,
            davomat = new
            {
                ortachaFoiz = groupAttendancePct,
                sababliQoldirish = absenceCount,
                kechKelish = lateCount,
                sabablarTaqsimoti = absenceReasonCounts,
                otilganDarslar = conductedDates.Count,
            },
            jurnal = new
            {
                rejadagiDarslar = plannedTotal,
                otilganDarslar = conductedTotal,
                belgilanmaganDarslar = missedTotal,
                bajarilishFoizi = Pct(conductedTotal, plannedTotal),
                mavzuFoizi = metrics.TopicPct,
                uyVazifaFoizi = metrics.HomeworkPct,
                davomatOlinganFoizi = metrics.AttendanceTakenPct,
                qoyilganBaholar = gradeEntries.Count,
                oxirgiBelgilanmaganSanalar = recentMissed,
                izoh = $"belgilanmaganDarslar = muhlati o'tgan (muhlat {policy.SalaryGraceDays} kun), " +
                       "lekin jurnalda \"o'tildi\" deb belgilanmagan darslar",
            },
            ozlashtirish = new
            {
                ortachaBahoShuOy = metrics.AvgGradeThisMonth,
                ortachaBahoOtganOy = metrics.AvgGradePrevMonth,
                ortachaBall = avgBall,
                uyVazifaQildi = metrics.HomeworkDone,
                uyVazifaQilmadi = metrics.HomeworkMissed,
                xulqYaxshi = metrics.BehaviorGood,
                xulqYomon = metrics.BehaviorBad,
                dastur = new
                {
                    jamiBand = curriculum.TotalItems,
                    otilgan = curriculum.CoveredCount,
                    qolgan = curriculum.RemainingItems,
                    taxminiyTugash = curriculum.EstFinishDate,
                },
            },
            imtihonlar = new { soni = testStats.Count, ortachaFoiz = testAvgPct, royxat = testStats },
            tolovlar = includeFinance
                ? new
                {
                    hisoblangan = billed,
                    yigilgan = collected,
                    yigilishFoizi = metrics.CollectionPct,
                    qarzdorlik = debt,
                    tolaganlar = paidCount,
                    tolamaganlar = unpaidCount,
                    oymaOy = monthStats.Select(m => new { m.Month, m.Billed, m.Collected }),
                }
                : null,
            oquvchilar = students,
            oymaOyKesim = monthStats,
        };

        var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });

        return (metrics, snapshotJson);
    }

    /// <summary>
    /// Guruhdan KETISH/MUZLATISH sabablari. Sabab alohida ustunda saqlanmaydi — amal bajarilganda
    /// audit yozuviga ("... — sabab: X") yoziladi, shu yerdan ajratib olinadi. Qo'shimcha manba:
    /// shu guruh o'quvchilaridan markazdan butunlay arxivlanganlar (<see cref="ArchivedRecord"/>).
    /// </summary>
    private static async Task<List<CenterPointDto>> DepartureReasonsAsync(
        IAppDbContext db, string groupId, HashSet<string> studentIds, string fromDate, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();
        void Add(string? label)
        {
            var key = string.IsNullOrWhiteSpace(label) ? "Sabab ko'rsatilmagan" : label!.Trim();
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var prefix = groupId + ":";
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Membership" && a.EntityId.StartsWith(prefix)
                        && string.Compare(a.Timestamp, fromDate) >= 0)
            .Select(a => a.Summary)
            .ToListAsync(ct);
        foreach (var raw in logs)
        {
            var s = raw ?? "";
            if (!s.StartsWith("Guruhdan chiqarildi") && !s.StartsWith("Muzlatildi")) continue;
            const string marker = "sabab: ";
            var i = s.IndexOf(marker, StringComparison.Ordinal);
            Add(i < 0 ? null : s[(i + marker.Length)..]);
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
}
