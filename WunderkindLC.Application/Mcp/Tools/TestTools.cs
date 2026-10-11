using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Test results (a named, dated test of a group with a max score + each student's score) and the
/// BALL rating (group and centre). Rules: .claude/rules/tests.md (first section — test results only),
/// journal.md (BALL = Σ manual journal grades + Σ done criteria + Σ manual adjustments, per group;
/// "Reyting hamma joyda YIG'ILGAN BALL bo'yicha"; LRN-1 — criteria grades are not counted twice).
///
/// <para>⚠️ The online-test part (bot PDF, answer key, code, «only by code», external participants) is
/// being removed: its columns are never projected and <c>ExternalTestScore</c> / <c>TestBotSession</c>
/// are never read. That is why <see cref="TestResultService.DetailAsync"/> (it reads external scores and
/// certificates) is NOT called — its in-centre ranking is reproduced here with the same rule.</para>
/// </summary>
[McpServerToolType]
public sealed class TestTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(TestResult), typeof(TestScore), typeof(Group), typeof(Subject), typeof(Teacher),
        typeof(StudentGroup), typeof(Student), typeof(JournalEntry), typeof(CriterionGrade),
        typeof(StudentBallAdjustment), typeof(LessonNote), typeof(AbsenceReason), typeof(GradingCriterion),
        typeof(GroupGradingCriterion),
    ];

    // =====================================================================================
    //  tests_list — TestResultService.ListForGroupAsync (per group) across groups
    // =====================================================================================

    [McpServerTool(Name = "tests_list", Title = "Testlar natijalari ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Testlar natijalari» bo'limidagi testlar (guruhga o'tkazilgan nomli, sanali test va maksimal ball): "
        + "yangisidan eskisiga. Har qatorda: test id, nomi, sanasi, guruh (id, nomi, arxivdami), kurs, o'qituvchi, "
        + "maksimal ball, guruhning hozirgi faol (muzlatilmagan) o'quvchilari soni, ball kiritilganlar soni, o'rtacha ball "
        + "va o'rtacha foiz (o'rtacha ÷ maksimal × 100). Filtrlar: guruh, oy (YYYY-MM) YOKI sana oralig'i (from/to, ko'pi "
        + "bilan 366 kun), nomi bo'yicha qidiruv. Sahifalangan. Ballar ro'yxati — test_scores. "
        + "Group tests with score count, average score and average percent, newest first.")]
    public async Task<string> TestsList(
        [Description("Faqat shu guruh testlari (groups_list natijasidan id)")] string? groupId = null,
        [Description("Oy YYYY-MM (from/to bilan birga berilmaydi)")] string? month = null,
        [Description("Boshlanish sanasi YYYY-MM-DD (month o'rniga)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (month o'rniga; sukut — bugun)")] string? to = null,
        [Description("Test nomi bo'yicha qidiruv (qismi ham bo'ladi)")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.TestResults);
        var gid = Clean(groupId);
        var (mon, fromIso, toIso) = Period(month, from, to);
        var q = Clean(search)?.ToLower();
        var db = t.Db;

        var query = db.TestResults.AsQueryable();
        if (gid is not null) query = query.Where(x => x.GroupId == gid);
        if (mon is not null) query = query.Where(x => x.Date.StartsWith(mon));
        if (fromIso is not null && toIso is not null)
            query = query.Where(x => string.Compare(x.Date, fromIso) >= 0 && string.Compare(x.Date, toIso) <= 0);
        if (q is not null) query = query.Where(x => x.Name.ToLower().Contains(q));

        var total = await query.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        // Newest first — the panel's order (Date desc, CreatedAt desc), Id for a stable page.
        var tests = await query
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new { x.Id, x.GroupId, x.Name, x.Date, x.MaxScore })
            .ToListAsync(ct);

        var testIds = tests.Select(x => x.Id).ToList();
        // Decimal aggregates are computed in memory (SQLite cannot SUM/AVG decimals); at most 200 tests.
        var scoresByTest = (await db.TestScores.Where(s => testIds.Contains(s.TestResultId))
                .Select(s => new { s.TestResultId, s.Score }).ToListAsync(ct))
            .GroupBy(s => s.TestResultId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Score).ToList());

        var groupIds = tests.Select(x => x.GroupId).Distinct().ToList();
        var groups = await GroupHeadersAsync(db, groupIds, ct);
        // Same count as ListForGroupAsync.studentCount: active, not frozen members.
        var studentCounts = (await db.StudentGroups
                .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive && sg.Status != "frozen")
                .GroupBy(sg => sg.GroupId)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);

        var items = tests.Select(x =>
        {
            var scores = scoresByTest.GetValueOrDefault(x.Id) ?? [];
            decimal? avg = scores.Count > 0 ? scores.Average() : null;
            var g = groups.GetValueOrDefault(x.GroupId);
            return new
            {
                x.Id, x.Name, x.Date, x.MaxScore,
                group = new { id = x.GroupId, name = g?.Name ?? "", isArchived = g?.IsArchived ?? false },
                course = g?.Course ?? "",
                teacher = new { id = g?.TeacherId ?? "", name = g?.TeacherName ?? "" },
                groupStudents = studentCounts.GetValueOrDefault(x.GroupId, 0),
                scored = scores.Count,
                averageScore = avg is null ? (decimal?)null : Math.Round(avg.Value, 1),
                averagePercent = Percent(avg, x.MaxScore),
            };
        }).ToList();

        return t.Json(new
        {
            groupId = gid, month = mon, from = fromIso, to = toIso, search = Clean(search),
            note = "«groupStudents» — guruhning HOZIRGI faol (muzlatilmagan) o'quvchilari; «scored» — ball kiritilganlar; "
                + "«averagePercent» = o'rtacha ball ÷ maksimal ball × 100.",
            total, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  test_scores — TestResultService.DetailAsync, in-centre rows only (ranking reproduced)
    // =====================================================================================

    [McpServerTool(Name = "test_scores", Title = "Test ballari (o'rinlar bilan)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta testning natijalari — panelning test sahifasi bilan bir xil: guruhning faol (muzlatilmagan) "
        + "o'quvchilari va guruhdan tashqari ball olgan markaz o'quvchilari (member=false), ball bo'yicha kamayish tartibida. "
        + "O'rin musobaqa qoidasida: teng ball — bir xil o'rin, keyingisi sakraydi (1, 1, 3); ball kiritilmaganlar oxirida, "
        + "o'rni yo'q. Har qatorda ball va foiz (ball ÷ maksimal × 100). Jamlanma: ball kiritilganlar, kiritilmaganlar, "
        + "o'rtacha, eng yuqori, eng past ball va o'rtacha foiz. Testlar ro'yxati — tests_list. "
        + "One test's ranked scores with a summary.")]
    public async Task<string> TestScores(
        [Description("Test id (tests_list natijasidan)")] string testId,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.TestResults);
        var id = Clean(testId) ?? throw new McpException("testId kerak.");
        var db = t.Db;

        var test = await db.TestResults.Where(x => x.Id == id)
            .Select(x => new { x.Id, x.GroupId, x.Name, x.Date, x.MaxScore })
            .FirstOrDefaultAsync(ct);
        if (test is null) return t.Json(new { message = "Test topilmadi." }, 0);
        var group = (await GroupHeadersAsync(db, new List<string> { test.GroupId }, ct)).GetValueOrDefault(test.GroupId);

        // Members as DetailAsync: active, not frozen memberships of the test's group.
        var members = await (from sg in db.StudentGroups
                             join s in db.Students on sg.StudentId equals s.Id
                             where sg.GroupId == test.GroupId && sg.IsActive && sg.Status != "frozen"
                             select new { s.Id, s.FullName }).ToListAsync(ct);
        members = members.GroupBy(m => m.Id).Select(g => g.First()).ToList();

        var scoreRows = (await db.TestScores.Where(s => s.TestResultId == id)
                .Select(s => new { s.StudentId, s.Score }).ToListAsync(ct))
            .GroupBy(s => s.StudentId)
            .ToDictionary(g => g.Key, g => g.First().Score);

        // Centre students who have a score but are not (any more) active members — DetailAsync lists them too.
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var guestIds = scoreRows.Keys.Where(sid => !memberIds.Contains(sid)).ToList();
        var guests = guestIds.Count == 0
            ? []
            : await db.Students.Where(s => guestIds.Contains(s.Id)).Select(s => new { s.Id, s.FullName }).ToListAsync(ct);

        var ranked = RankScores(
            members.Select(m => (m.Id, m.FullName, true))
                .Concat(guests.Select(g => (g.Id, g.FullName, false)))
                .Select(m => new ScoreInput(m.Item1, m.Item2, m.Item3,
                    scoreRows.TryGetValue(m.Item1, out var sc) ? sc : null)));

        var scored = ranked.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).ToList();
        decimal? avg = scored.Count > 0 ? scored.Average() : null;

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = ranked.Skip((p - 1) * size).Take(size).Select(r => new
        {
            rank = r.Rank,
            r.StudentId, r.FullName, r.Member,
            score = r.Score,
            percent = Percent(r.Score, test.MaxScore),
        }).ToList();

        return t.Json(new
        {
            test = new
            {
                test.Id, test.Name, test.Date, test.MaxScore,
                group = new { id = test.GroupId, name = group?.Name ?? "", isArchived = group?.IsArchived ?? false },
                course = group?.Course ?? "",
                teacher = new { id = group?.TeacherId ?? "", name = group?.TeacherName ?? "" },
            },
            summary = new
            {
                students = ranked.Count,
                entered = scored.Count,
                notEntered = ranked.Count - scored.Count,
                averageScore = avg is null ? (decimal?)null : Math.Round(avg.Value, 1),
                maxScore = scored.Count > 0 ? scored.Max() : (decimal?)null,
                minScore = scored.Count > 0 ? scored.Min() : (decimal?)null,
                averagePercent = Percent(avg, test.MaxScore),
            },
            note = "O'rin — ball kiritilganlar orasida (teng ball — bir xil o'rin); ball kiritilmaganlarda rank = null. "
                + "member=false — guruhning faol a'zosi emas, lekin shu testdan ball olgan markaz o'quvchisi.",
            total = ranked.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    internal sealed record ScoreInput(string StudentId, string FullName, bool Member, decimal? Score);

    internal sealed record RankedScore(int? Rank, string StudentId, string FullName, bool Member, decimal? Score);

    /// <summary>
    /// The ranking of <see cref="TestResultService.DetailAsync"/>: scored first (score desc), then by name;
    /// standard competition rank (equal scores share a rank, the next one skips: 1, 1, 3); unscored last, no rank.
    /// </summary>
    internal static List<RankedScore> RankScores(IEnumerable<ScoreInput> rows)
    {
        var ordered = rows
            .OrderByDescending(x => x.Score.HasValue)
            .ThenByDescending(x => x.Score ?? 0)
            .ThenBy(x => x.FullName)
            .ThenBy(x => x.StudentId, StringComparer.Ordinal)
            .ToList();
        var result = new List<RankedScore>(ordered.Count);
        decimal? prev = null;
        int rank = 0, position = 0;
        foreach (var x in ordered)
        {
            if (!x.Score.HasValue)
            {
                result.Add(new RankedScore(null, x.StudentId, x.FullName, x.Member, null));
                continue;
            }
            position++;
            if (prev is null || x.Score.Value != prev.Value) rank = position;
            prev = x.Score.Value;
            result.Add(new RankedScore(rank, x.StudentId, x.FullName, x.Member, x.Score));
        }
        return result;
    }

    // =====================================================================================
    //  student_tests — TestResultService.StudentResultsAsync (admin student profile)
    // =====================================================================================

    [McpServerTool(Name = "student_tests", Title = "O'quvchining test natijalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchining barcha guruhlaridagi test natijalari — admin profilidagi «Testlar natijalari» "
        + "bo'limi bilan bir xil: sana, test nomi, guruh, ball / maksimal, foiz, testdagi o'rni (teng ball — bir xil o'rin) "
        + "va shu testda ball olganlar soni (o'rin shular orasida). Yangisidan eskisiga; ixtiyoriy sana oralig'i "
        + "(from/to, ko'pi bilan 366 kun; berilmasa — butun tarix). Sahifalangan. "
        + "One student's test history with score, percent and rank.")]
    public async Task<string> StudentTests(
        [Description("O'quvchi id (students_search natijasidan)")] string studentId,
        [Description("Boshlanish sanasi YYYY-MM-DD (ixtiyoriy)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (ixtiyoriy; from berilsa sukut — bugun)")] string? to = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        // Either area is enough: the test results page, or the student list/profile.
        if (!t.CanReadArea(McpAreas.TestResults)) t.Require(McpAreas.Students);
        var sid = Clean(studentId) ?? throw new McpException("studentId kerak.");
        string? fromIso = null, toIso = null;
        if (Clean(from) is not null || Clean(to) is not null)
        {
            var (f, tt) = McpToolContext.Range(from, to, defaultDays: McpToolContext.MaxRangeDays);
            fromIso = McpToolContext.Iso(f);
            toIso = McpToolContext.Iso(tt);
        }
        var db = t.Db;

        var student = await db.Students.Where(s => s.Id == sid)
            .Select(s => new { s.Id, s.FullName, s.IsArchived }).FirstOrDefaultAsync(ct);
        if (student is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        var all = await TestResultService.StudentResultsAsync(db, sid);
        var rows = fromIso is null
            ? all
            : all.Where(r => string.CompareOrdinal(r.Date, fromIso) >= 0 && string.CompareOrdinal(r.Date, toIso) <= 0).ToList();

        var percents = rows.Select(r => Percent(r.Score, r.MaxScore)).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = rows.Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.TestId, r.Date, r.Name,
            group = new { id = r.GroupId, name = r.GroupName },
            r.Score, r.MaxScore,
            percent = Percent(r.Score, r.MaxScore),
            r.Rank,
            scoredCount = r.Total,
        }).ToList();

        return t.Json(new
        {
            student = new { student.Id, student.FullName, student.IsArchived },
            from = fromIso, to = toIso,
            summary = new
            {
                tests = rows.Count,
                averagePercent = percents.Count > 0 ? Math.Round(percents.Average(), 1) : (decimal?)null,
            },
            note = "«rank» — shu testda ball olganlar orasidagi o'rin (teng ball — bir xil o'rin), «scoredCount» — ular soni; "
                + "«averagePercent» — ro'yxatdagi testlar foizlarining o'rtachasi.",
            total = rows.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  rating_group — StudentBallService.ComputeByGroupAsync (+ TeacherAsync's per-group row)
    // =====================================================================================

    [McpServerTool(Name = "rating_group", Title = "Guruh reytingi (yig'ilgan ball)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta guruh o'quvchilari YIG'ILGAN BALL bo'yicha (o'rtacha baho EMAS) — guruh sahifasidagi «Reyting» tabi. "
        + "BALL = qo'lda qo'yilgan jurnal baholari yig'indisi + bajarilgan baholash mezonlari soni + qo'lda tuzatish "
        + "(guruhdagi ball manfiy bo'lmaydi); mezondan avtomatik yozilgan jurnal bahosi ikki marta sanalmaydi. Har qatorda: "
        + "o'rin (guruh ichida, ball kamayishi, teng bo'lsa ism), o'quvchi, ball tarkibi (jurnal yig'indisi, baholar soni, "
        + "o'rtacha baho, bajarilgan mezonlar, tuzatish), har mezon bo'yicha bajarilganlar soni va davomat foizi (o'tilgan "
        + "darslar, kech kelish kelmaslik emas). month (YYYY-MM) berilsa — faqat shu oy; berilmasa — butun vaqt. Sukut bo'yicha "
        + "faol va sinovdagi o'quvchilar (guruh sahifasi kabi); includeFrozen=true — muzlatilganlar ham. "
        + "Group students ranked by collected ball with its breakdown.")]
    public async Task<string> RatingGroup(
        [Description("Guruh id (groups_list natijasidan)")] string groupId,
        [Description("Oy YYYY-MM (berilmasa — butun vaqt)")] string? month = null,
        [Description("true — muzlatilgan a'zolar ham (o'qituvchi reytingidagi kabi)")] bool? includeFrozen = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Groups);
        var gid = Clean(groupId) ?? throw new McpException("groupId kerak.");
        var mon = Clean(month) is null ? null : McpToolContext.Month(month);
        var db = t.Db;

        var group = (await GroupHeadersAsync(db, new List<string> { gid }, ct)).GetValueOrDefault(gid);
        if (group is null) return t.Json(new { message = "Guruh topilmadi." }, 0);

        // Rows: live memberships of the group (the group page: active/trial; frozen on request), archived students out.
        var memberQ = db.StudentGroups.Where(sg => sg.GroupId == gid && sg.IsActive);
        if (includeFrozen != true) memberQ = memberQ.Where(sg => sg.Status != "frozen");
        var memberships = (await memberQ.Select(sg => new { sg.StudentId, sg.Status }).ToListAsync(ct))
            .GroupBy(m => m.StudentId).Select(g => g.First()).ToList();
        var memberIds = memberships.Select(m => m.StudentId).ToList();
        var names = await db.Students.Where(s => memberIds.Contains(s.Id) && !s.IsArchived)
            .Select(s => new { s.Id, s.FullName }).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        // BALL — the server formula (per group, month-aware) the teacher/centre ratings use.
        var balls = await StudentBallService.ComputeByGroupAsync(db, new List<string> { gid }, mon);

        // Attendance — StudentBallService.TeacherAsync: conducted lessons of the group (in the month), absences
        // with a NON-late reason on a conducted lesson.
        var lateIds = (await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        var notesQ = db.LessonNotes.Where(n => n.ClassId == gid && n.Conducted);
        if (mon is not null) notesQ = notesQ.Where(n => n.Date.StartsWith(mon));
        var conducted = (await notesQ.Select(n => new { n.SubjectId, n.Date, n.Period }).ToListAsync(ct))
            .Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet();
        var absQ = db.JournalEntries.Where(e => e.ClassId == gid && e.ReasonId != null);
        if (mon is not null) absQ = absQ.Where(e => e.Date.StartsWith(mon));
        var absentByStudent = (await absQ.Select(e => new { e.StudentId, e.SubjectId, e.Date, e.Period, e.ReasonId })
                .ToListAsync(ct))
            .Where(e => !lateIds.Contains(e.ReasonId!) && conducted.Contains((e.SubjectId, e.Date, e.Period)))
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Criteria attached to the group (the "Reyting" tab's columns) and each student's done marks per criterion.
        var assigned = await db.GroupGradingCriteria.Where(a => a.GroupId == gid)
            .Select(a => new { a.CriterionId, a.Order }).ToListAsync(ct);
        var critIds = assigned.Select(a => a.CriterionId).Distinct().ToList();
        var critNames = await db.GradingCriteria.Where(c => critIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var criteria = assigned.Where(a => critNames.ContainsKey(a.CriterionId))
            .GroupBy(a => a.CriterionId).Select(g => g.First())
            .OrderBy(a => a.Order).ThenBy(a => critNames[a.CriterionId], StringComparer.OrdinalIgnoreCase)
            .Select(a => new { id = a.CriterionId, name = critNames[a.CriterionId] })
            .ToList();
        var marksQ = db.CriterionGrades.Where(g => g.GroupId == gid && g.Done && critIds.Contains(g.CriterionId));
        if (mon is not null) marksQ = marksQ.Where(g => g.Date.StartsWith(mon));
        var marks = (await marksQ.GroupBy(g => new { g.StudentId, g.CriterionId })
                .Select(g => new { g.Key.StudentId, g.Key.CriterionId, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => (x.StudentId, x.CriterionId), x => x.N);

        var raw = memberships.Where(m => names.ContainsKey(m.StudentId)).Select(m =>
        {
            var b = balls.GetValueOrDefault((m.StudentId, gid)) ?? new StudentBallService.BallStat(0, 0, 0);
            var absent = absentByStudent.GetValueOrDefault(m.StudentId, 0);
            double? attendance = conducted.Count > 0
                ? Math.Round((double)(conducted.Count - absent) / conducted.Count * 100)
                : null;
            return new GroupRatingRow(m.StudentId, names[m.StudentId], m.Status, b, conducted.Count, absent, attendance);
        }).ToList();

        // Rank inside the group: ball desc, then name — TeacherAsync's rule.
        var ranked = raw
            .OrderByDescending(r => r.Ball.Ball)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StudentId, StringComparer.Ordinal)
            .Select((r, i) => (Rank: i + 1, Row: r))
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = ranked.Skip((p - 1) * size).Take(size).Select(x => new
        {
            rank = x.Rank,
            studentId = x.Row.StudentId,
            fullName = x.Row.FullName,
            membershipStatus = x.Row.Status,
            ball = x.Row.Ball.Ball,
            journalTotal = x.Row.Ball.JournalTotal,
            gradeCount = x.Row.Ball.GradeCount,
            averageGrade = x.Row.Ball.Average,
            criteriaDone = x.Row.Ball.CriteriaDone,
            adjustment = x.Row.Ball.Adjustment,
            criteria = criteria.Select(c => new
            {
                c.id, c.name, done = marks.GetValueOrDefault((x.Row.StudentId, c.id), 0),
            }),
            attendance = new { lessonsConducted = x.Row.Conducted, absent = x.Row.Absent, percent = x.Row.Attendance },
        }).ToList();

        return t.Json(new
        {
            group = new
            {
                id = gid, name = group.Name, isArchived = group.IsArchived, course = group.Course,
                teacher = new { id = group.TeacherId, name = group.TeacherName },
            },
            month = mon ?? "",
            period = mon is null ? "all-time" : "month",
            summary = new
            {
                students = ranked.Count,
                averageBall = ranked.Count > 0 ? Math.Round(ranked.Average(r => (double)r.Row.Ball.Ball), 1) : 0,
                topBall = ranked.Count > 0 ? ranked[0].Row.Ball.Ball : 0,
                lessonsConducted = conducted.Count,
            },
            criteria,
            note = "BALL = jurnal yig'indisi (faqat QO'LDA qo'yilgan baholar) + bajarilgan mezonlar + tuzatish, guruhda 0 dan "
                + "past tushmaydi. «averageGrade» — qo'lda qo'yilgan baholar o'rtachasi; davomatda kech kelish kelmaslik emas.",
            total = ranked.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    private sealed record GroupRatingRow(
        string StudentId, string FullName, string Status, StudentBallService.BallStat Ball,
        int Conducted, int Absent, double? Attendance);

    // =====================================================================================
    //  rating_center — RatingService.SchoolAsync (portal centre rating) + StudentBallService
    // =====================================================================================

    [McpServerTool(Name = "rating_center", Title = "Markaz reytingi (TOP o'quvchilar)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Markaz bo'yicha o'quvchilar reytingi — YIG'ILGAN BALL bo'yicha (o'rtacha baho EMAS). Sukut bo'yicha "
        + "(sortBy=avgBall) o'quvchi portalidagi markaz reytingi kabi GURUHLAR BO'YICHA O'RTACHA ball (jami ball ÷ ball "
        + "tushgan guruhlar soni — ko'p guruhli o'quvchi yig'indisi hisobiga oldinga chiqib ketmasin), teng bo'lsa o'rtacha "
        + "baho; sortBy=ball — jami ball (admin «O'quvchilar» ro'yxatidagi «Ball» saralashi). Har qatorda: o'rin, o'quvchi, "
        + "jami ball, o'rtacha ball, ball tushgan guruhlar soni, o'rtacha baho, davomat foizi, hozirgi guruhlari (har birida "
        + "shu guruhdagi ball). Arxivlangan va guruhsiz o'quvchilar kirmaydi. TOP N — sahifa hajmi (sukut 50, ko'pi bilan 200). "
        + "Natija 5 daqiqa keshlanadi. Centre-wide student rating by collected ball.")]
    public async Task<string> RatingCenter(
        [Description("Saralash: avgBall (sukut — guruhlar bo'yicha o'rtacha ball) yoki ball (jami ball)")] string? sortBy = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi = TOP N (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var sort = (Clean(sortBy) ?? "avgBall").ToLowerInvariant() switch
        {
            "avgball" => "avgBall",
            "ball" => "ball",
            _ => throw new McpException("«sortBy» faqat avgBall yoki ball bo'lishi mumkin."),
        };
        var db = t.Db;

        var all = await t.CachedAsync("rating:center", TimeSpan.FromMinutes(5), () => ComputeCenterAsync(db, ct));

        var ordered = (sort == "ball"
                ? all.OrderByDescending(r => r.Ball).ThenByDescending(r => r.Average)
                : all.OrderByDescending(r => r.AvgBall).ThenByDescending(r => r.Average))
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StudentId, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = ordered.Skip((p - 1) * size).Take(size).Select((r, i) => new
        {
            rank = (p - 1) * size + i + 1,
            r.StudentId, r.FullName,
            r.Ball, r.AvgBall, r.GroupCount,
            averageGrade = r.Average,
            attendancePercent = r.Attendance,
            mainGroup = r.MainGroup,
            groups = r.Groups.Select(g => new { id = g.Id, name = g.Name, ball = g.Ball }),
        }).ToList();

        return t.Json(new
        {
            sortBy = sort,
            summary = new
            {
                studentsRanked = all.Count,
                withBall = all.Count(r => r.Ball > 0),
                averageBall = all.Count > 0 ? Math.Round(all.Average(r => (double)r.Ball), 1) : 0,
            },
            note = "«ball» — barcha guruhlardagi ball yig'indisi (har guruhda alohida 0 dan past emas); «avgBall» — ball ÷ "
                + "ball tushgan guruhlar soni (markaz o'rni shu bo'yicha); «averageGrade» va davomat — o'quvchining hozirgi "
                + "guruhlari bo'yicha (kech kelish kelmaslik emas). Natija 5 daqiqagacha eskirgan bo'lishi mumkin.",
            total = ordered.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    internal sealed record CenterGroup(string Id, string Name, int Ball);

    internal sealed record CenterRow(
        string StudentId, string FullName, int Ball, double AvgBall, int GroupCount,
        double Average, double? Attendance, string MainGroup, List<CenterGroup> Groups);

    /// <summary>
    /// <see cref="RatingService.SchoolAsync"/> reproduced with SQL aggregates. The service loads the WHOLE
    /// journal (<c>db.JournalEntries.ToListAsync()</c> — tens of millions of rows in production) into memory to
    /// average grades and count absences; here the same numbers come from GROUP BY / EXISTS queries, so only
    /// one row per (student, group) leaves the database. The ball is the service's own
    /// (<see cref="StudentBallService.ComputeByGroupAsync"/> + <see cref="StudentBallService.AggregateByStudent"/>).
    /// Same row set: non-archived students with a live membership (or, failing that, a group named like their
    /// legacy <c>ClassName</c>); groups ordered active → trial → frozen, then newest joined.
    /// </summary>
    internal static async Task<List<CenterRow>> ComputeCenterAsync(IAppDbContext db, CancellationToken ct)
    {
        var balls = StudentBallService.AggregateByStudent(await StudentBallService.ComputeByGroupAsync(db));
        var students = await db.Students.Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct);
        var classes = await db.Classes.Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var classById = classes.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var lateIds = await db.AbsenceReasons.Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct);

        var groupsByStudent = (await db.StudentGroups.Where(m => m.IsActive)
                .Select(m => new { m.StudentId, m.GroupId, m.Status, m.JoinedAt }).ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(m => MembershipLifecycle.StateRank(m.Status))
                .ThenByDescending(m => m.JoinedAt ?? string.Empty, StringComparer.Ordinal)
                .Select(m => m.GroupId).Distinct().ToList());

        // Σ grade / count per (student, group) — every grade, as RatingService averages them.
        var grades = (await db.JournalEntries.Where(e => e.Grade != null)
                .GroupBy(e => new { e.StudentId, e.ClassId })
                .Select(g => new { g.Key.StudentId, g.Key.ClassId, Sum = g.Sum(x => x.Grade ?? 0), N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => (x.StudentId, x.ClassId), x => (Sum: (long)x.Sum, x.N));

        // Conducted lessons per group = distinct (subject, date, period) of conducted lesson notes.
        var conductedByClass = (await db.LessonNotes.Where(n => n.Conducted)
                .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period }).Distinct()
                .GroupBy(n => n.ClassId)
                .Select(g => new { ClassId = g.Key, N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.ClassId, x => x.N);

        // Absences (a NON-late reason) on a conducted lesson, per (student, group).
        var conductedNotes = db.LessonNotes.Where(n => n.Conducted);
        var absences = (await db.JournalEntries
                .Where(e => e.ReasonId != null && !lateIds.Contains(e.ReasonId!)
                            && conductedNotes.Any(n => n.ClassId == e.ClassId && n.SubjectId == e.SubjectId
                                                       && n.Date == e.Date && n.Period == e.Period))
                .GroupBy(e => new { e.StudentId, e.ClassId })
                .Select(g => new { g.Key.StudentId, g.Key.ClassId, N = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => (x.StudentId, x.ClassId), x => x.N);

        var result = new List<CenterRow>();
        foreach (var st in students)
        {
            var groupIds = groupsByStudent.TryGetValue(st.Id, out var gs) && gs.Count > 0
                ? gs
                : classes.Where(c => !string.IsNullOrEmpty(st.ClassName) && c.Name == st.ClassName)
                    .Select(c => c.Id).ToList();
            if (groupIds.Count == 0) continue;   // groupless students are not ranked

            long sum = 0;
            int count = 0, conducted = 0, absent = 0;
            foreach (var gid in groupIds)
            {
                if (grades.TryGetValue((st.Id, gid), out var g)) { sum += g.Sum; count += g.N; }
                if (conductedByClass.TryGetValue(gid, out var c))
                {
                    conducted += c;
                    absent += absences.GetValueOrDefault((st.Id, gid), 0);
                }
            }
            var average = count > 0 ? Math.Round((double)sum / count, 1) : 0;
            double? attendance = conducted > 0 ? Math.Round((double)(conducted - absent) / conducted * 100) : null;

            var b = balls.GetValueOrDefault(st.Id);
            var mainGroup = classById.TryGetValue(groupIds[0], out var n0) ? n0 : st.ClassName;
            result.Add(new CenterRow(
                st.Id, st.FullName, b?.Ball ?? 0, b?.AvgBall ?? 0, b?.GroupCount ?? 0, average, attendance, mainGroup,
                groupIds.Select(gid => new CenterGroup(gid, classById.GetValueOrDefault(gid, ""),
                    b?.GroupBalls.GetValueOrDefault(gid, 0) ?? 0)).ToList()));
        }
        return result;
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

    private sealed record GroupHeader(string Name, bool IsArchived, string Course, string TeacherId, string TeacherName);

    /// <summary>Group name, archive flag, course and teacher names for the given ids (three small queries).</summary>
    private static async Task<Dictionary<string, GroupHeader>> GroupHeadersAsync(
        IAppDbContext db, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<string, GroupHeader>(StringComparer.Ordinal);
        var groups = await db.Classes.Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.IsArchived, c.CourseId, c.TeacherId }).ToListAsync(ct);
        var courseIds = groups.Select(g => g.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courses = courseIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Subjects.Where(s => courseIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teachers = teacherIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Teachers.Where(x => teacherIds.Contains(x.Id))
                .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return groups.ToDictionary(g => g.Id, g => new GroupHeader(
            g.Name, g.IsArchived,
            string.IsNullOrEmpty(g.CourseId) ? "" : courses.GetValueOrDefault(g.CourseId, ""),
            g.TeacherId ?? "",
            string.IsNullOrEmpty(g.TeacherId) ? "" : teachers.GetValueOrDefault(g.TeacherId, "")), StringComparer.Ordinal);
    }

    /// <summary>score ÷ max × 100, one decimal; null when there is no score or no max.</summary>
    internal static decimal? Percent(decimal? score, decimal max) =>
        score is null || max <= 0 ? null : Math.Round(score.Value / max * 100, 1);

    /// <summary>month XOR from/to → (month, fromIso, toIso); nothing → all time.</summary>
    private static (string? Month, string? From, string? To) Period(string? month, string? from, string? to)
    {
        var hasRange = Clean(from) is not null || Clean(to) is not null;
        if (Clean(month) is not null)
        {
            if (hasRange) throw new McpException("«month» yoki «from»/«to» — faqat bittasini bering.");
            return (McpToolContext.Month(month), null, null);
        }
        if (!hasRange) return (null, null, null);
        var (f, tt) = McpToolContext.Range(from, to, defaultDays: McpToolContext.MaxRangeDays);
        return (null, McpToolContext.Iso(f), McpToolContext.Iso(tt));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
