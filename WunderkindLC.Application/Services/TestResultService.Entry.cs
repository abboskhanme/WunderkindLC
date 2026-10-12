using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CRM upgrade U4a — RESULT-ONLY test entry (docs/SPEC-crm-upgrade.md P4.2; rules <c>.claude/rules/tests.md</c>):
/// kind + «Savollar soni» on create/update, batch grid save that REJECTS invalid values (no clamp), centre overview,
/// audit. Admin (<c>TestResultsController</c>) and teacher (<c>TeacherPortalController</c>) call the SAME methods —
/// only the access gate differs.
/// </summary>
public static partial class TestResultService
{
    /// <summary>Audit entity type (section «Guruhlar» — <see cref="AuditSections"/>).</summary>
    public const string AuditEntity = "TestResult";

    /// <summary><c>"{groupId}:{testId}"</c> — the group's «Tarix» tab finds it (<c>EntityId.StartsWith(groupId + ":")</c>).</summary>
    public static string AuditEntityId(string groupId, string testId) => $"{groupId}:{testId}";

    /// <summary>Before/after snapshot of the editable fields (no ids, no file paths).</summary>
    public static object Snapshot(TestResult t) => new
    {
        t.Name, t.Date, Kind = TestEntryRules.KindLabel(t.Kind), t.QuestionCount, t.MaxScore,
    };

    /// <summary>Mean percent (one decimal) of the given scores against <paramref name="max"/>; null when none.</summary>
    public static double? AvgPercent(IEnumerable<decimal> scores, decimal max)
    {
        var list = scores.Select(s => Rating.RatingFormula.Percent(s, max)).Where(p => p is not null)
            .Select(p => p!.Value).ToList();
        return list.Count == 0 ? null : Rating.RatingFormula.Round1(list.Average());
    }

    /// <summary>Current (active, not frozen) members of a group — the grid rows and the «members» count.</summary>
    private static IQueryable<StudentGroup> CurrentMembers(IAppDbContext db, string groupId) =>
        db.StudentGroups.AsNoTracking().Where(sg => sg.GroupId == groupId && sg.IsActive && sg.Status != "frozen");

    /// <summary>Per group: tests in <paramref name="month"/> (null = all time) and the mean percent of their scores.</summary>
    private static async Task<Dictionary<string, (int Tests, double? AvgPercent)>> MonthStatsByGroupAsync(
        IAppDbContext db, string? month)
    {
        var tests = db.TestResults.AsNoTracking();
        if (month is not null)
        {
            var (from, to) = TestEntryRules.MonthRange(month);
            tests = tests.Where(t => t.Date.CompareTo(from) >= 0 && t.Date.CompareTo(to) <= 0);
        }
        var counts = await tests.GroupBy(t => t.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);
        // Decimal aggregation is not portable to SQLite — a small (group, score, max) projection, averaged here.
        var scores = await (from s in db.TestScores.AsNoTracking()
                            join t in tests on s.TestResultId equals t.Id
                            select new { t.GroupId, s.Score, t.MaxScore }).ToListAsync();
        var avg = scores.GroupBy(x => x.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => Rating.RatingFormula.Percent(x.Score, x.MaxScore))
                .Where(p => p is not null).Select(p => p!.Value).ToList());
        return counts.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value, avg.TryGetValue(kv.Key, out var ps) && ps.Count > 0
                ? Rating.RatingFormula.Round1(ps.Average())
                : (double?)null));
    }

    // =========================================================================================
    //  Create / update / delete (kind + «Savollar soni»)
    // =========================================================================================

    /// <summary>
    /// Creates a result-only test: name, date (empty = today), KIND (required — «Test turini tanlang») and
    /// «Savollar soni» 1..200 (required); <c>MaxScore = QuestionCount</c>. Audited (<paramref name="audit"/>).
    /// </summary>
    public static async Task<(GroupTestDto? Dto, string? Error)> CreateEntryAsync(
        IAppDbContext db, CreateTestResultRequest req, string createdBy, AuditService? audit = null)
    {
        var group = await db.Classes.FindAsync(req.GroupId);
        if (group is null) return (null, "Guruh topilmadi");
        var err = TestEntryRules.ValidateNew(req.Name, req.Kind, req.QuestionCount);
        if (err is not null) return (null, err);
        var date = string.IsNullOrWhiteSpace(req.Date) ? AppClock.Today.ToString("yyyy-MM-dd") : req.Date.Trim();
        if (!TestEntryRules.IsIsoDate(date)) return (null, TestEntryRules.DateInvalid);

        var questions = req.QuestionCount!.Value;
        var t = new TestResult
        {
            GroupId = group.Id,
            Name = req.Name.Trim(),
            Date = date,
            Kind = TestEntryRules.NormalizeKind(req.Kind)!,
            QuestionCount = questions,
            MaxScore = questions,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            CreatedBy = createdBy,
            CertificateEnabled = req.CertificateEnabled,
            CertificateTemplateId = req.CertificateEnabled ? (req.CertificateTemplateId ?? "").Trim() : "",
        };
        db.TestResults.Add(t);
        audit?.Record(AuditEntity, AuditEntityId(t.GroupId, t.Id), "create",
            $"Test yaratildi: {t.Name} ({TestEntryRules.KindLabel(t.Kind)}, {t.Date}, {questions} ta savol) — {group.Name} guruhi",
            after: Snapshot(t));
        await db.SaveChangesAsync();

        var members = await CurrentMembers(db, t.GroupId).CountAsync();
        return (new GroupTestDto(t.Id, t.GroupId, t.Name, t.Date, t.MaxScore,
            t.CreatedAt, t.CreatedBy, members, 0, null,
            t.CertificateEnabled, t.CertificateTemplateId, 0, t.Kind, t.QuestionCount), null);
    }

    /// <summary>
    /// Edits a test. Kind — kept when not sent. «Savollar soni»: a new value (1..200) sets <c>MaxScore</c> too and is
    /// REFUSED while any score exceeds it (results are never clamped silently). Not sent (null/0): a question-based
    /// test keeps its count; an OLD test (<c>QuestionCount = 0</c>) edits <c>MaxScore</c> as before (scores clamped,
    /// <see cref="TestUpdateResult.Regraded"/> — certificates are regenerated by the caller).
    /// </summary>
    public static async Task<TestUpdateResult> UpdateEntryAsync(
        IAppDbContext db, string id, UpdateTestResultRequest req, AuditService? audit = null)
    {
        var t = await db.TestResults.FindAsync(id);
        if (t is null) return new(false, "Test topilmadi");
        if (string.IsNullOrWhiteSpace(req.Name)) return new(false, TestEntryRules.NameRequired);
        if (!string.IsNullOrWhiteSpace(req.Date) && !TestEntryRules.IsIsoDate(req.Date))
            return new(false, TestEntryRules.DateInvalid);
        string? kind = null;
        if (!string.IsNullOrWhiteSpace(req.Kind))
        {
            kind = TestEntryRules.NormalizeKind(req.Kind);
            if (kind is null) return new(false, TestEntryRules.KindRequired);
        }

        var scores = await db.TestScores.Where(x => x.TestResultId == t.Id).ToListAsync();
        var questions = req.QuestionCount ?? 0;
        if (questions != 0)
        {
            if (questions is < TestEntryRules.MinQuestions or > TestEntryRules.MaxQuestions)
                return new(false, TestEntryRules.QuestionsRange);
            var lowering = TestEntryRules.ValidateQuestionLowering(questions, scores.Select(x => x.Score));
            if (lowering is not null) return new(false, lowering);
        }
        else if (t.QuestionCount == 0 && req.MaxScore <= 0)
        {
            return new(false, "Maksimal ball 0 dan katta bo'lishi kerak");
        }

        var before = Snapshot(t);
        t.Name = req.Name.Trim();
        if (!string.IsNullOrWhiteSpace(req.Date)) t.Date = req.Date.Trim();
        if (kind is not null) t.Kind = kind;
        t.CertificateEnabled = req.CertificateEnabled;
        t.CertificateTemplateId = req.CertificateEnabled ? (req.CertificateTemplateId ?? "").Trim() : "";

        var regraded = 0;
        if (questions != 0)
        {
            t.QuestionCount = questions;
            t.MaxScore = questions;
        }
        else if (t.QuestionCount == 0)
        {
            // Old test (before «Savollar soni»): the previous behaviour — max score edited, scores clamped.
            t.MaxScore = req.MaxScore;
            foreach (var sc in scores)
            {
                var next = ClampScore(sc.Score, t.MaxScore);
                if (next != sc.Score) { sc.Score = next; regraded++; }
            }
        }

        var group = await db.Classes.AsNoTracking().Where(g => g.Id == t.GroupId).Select(g => g.Name).FirstOrDefaultAsync();
        audit?.Record(AuditEntity, AuditEntityId(t.GroupId, t.Id), "update",
            $"Test tahrirlandi: {t.Name} — {group ?? "guruh"}", before: before, after: Snapshot(t));
        await db.SaveChangesAsync();
        return new(true, null, regraded);
    }

    /// <summary>Deletes a test (scores cascade) — audited with the number of results it had.</summary>
    public static async Task<bool> DeleteEntryAsync(IAppDbContext db, string id, AuditService? audit = null)
    {
        var t = await db.TestResults.FindAsync(id);
        if (t is null) return false;
        var scored = await db.TestScores.CountAsync(x => x.TestResultId == id);
        var group = await db.Classes.AsNoTracking().Where(g => g.Id == t.GroupId).Select(g => g.Name).FirstOrDefaultAsync();
        db.TestResults.Remove(t);
        audit?.Record(AuditEntity, AuditEntityId(t.GroupId, t.Id), "delete",
            $"Test o'chirildi: {t.Name} ({t.Date}) — {group ?? "guruh"}, {scored} ta natija bilan", before: Snapshot(t));
        await db.SaveChangesAsync();
        return true;
    }

    // =========================================================================================
    //  Centre overview
    // =========================================================================================

    /// <summary>
    /// Every test of the centre for a month (<c>yyyy-MM</c>, null = all time), optional kind / course / teacher
    /// filters, newest first: group, teacher, questions, scored / members and the mean percent.
    /// </summary>
    public static async Task<List<TestOverviewRowDto>> OverviewAsync(
        IAppDbContext db, string? month, string? kind, string? courseId, string? teacherId)
    {
        var groups = await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId })
            .ToListAsync();
        var groupIds = groups
            .Where(g => string.IsNullOrWhiteSpace(courseId) || g.CourseId == courseId)
            .Where(g => string.IsNullOrWhiteSpace(teacherId) || g.TeacherId == teacherId)
            .Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        var query = db.TestResults.AsNoTracking();
        var m = TestEntryRules.NormalizeMonth(month);
        if (m is not null)
        {
            var (from, to) = TestEntryRules.MonthRange(m);
            query = query.Where(t => t.Date.CompareTo(from) >= 0 && t.Date.CompareTo(to) <= 0);
        }
        var k = TestEntryRules.NormalizeKind(kind);
        if (k is not null) query = query.Where(t => t.Kind == k);
        var tests = (await query.ToListAsync()).Where(t => groupIds.Contains(t.GroupId)).ToList();
        if (tests.Count == 0) return new List<TestOverviewRowDto>();

        var testIds = tests.Select(t => t.Id).ToList();
        var scores = (await db.TestScores.AsNoTracking()
                .Where(s => testIds.Contains(s.TestResultId))
                .Select(s => new { s.TestResultId, s.Score })
                .ToListAsync())
            .ToLookup(s => s.TestResultId, s => s.Score);
        var usedGroups = tests.Select(t => t.GroupId).Distinct().ToList();
        var members = await db.StudentGroups.AsNoTracking()
            .Where(sg => usedGroups.Contains(sg.GroupId) && sg.IsActive && sg.Status != "frozen")
            .GroupBy(sg => sg.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName);
        var byId = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

        return tests
            .OrderByDescending(t => t.Date, StringComparer.Ordinal)
            .ThenByDescending(t => t.CreatedAt, StringComparer.Ordinal)
            .Select(t =>
            {
                var g = byId.GetValueOrDefault(t.GroupId);
                var s = scores[t.Id].ToList();
                return new TestOverviewRowDto(
                    t.Id, t.Name, t.Kind, t.Date, t.GroupId, g?.Name ?? "",
                    g is null || string.IsNullOrEmpty(g.TeacherId) ? "" : teacherNames.GetValueOrDefault(g.TeacherId, ""),
                    t.QuestionCount, t.MaxScore, s.Count, members.GetValueOrDefault(t.GroupId, 0),
                    AvgPercent(s, t.MaxScore), t.ResultsSentAt);
            })
            .ToList();
    }

    // =========================================================================================
    //  Batch grid save
    // =========================================================================================

    /// <summary>
    /// Saves a batch of grid cells. Each row: <c>solved = null</c> clears; otherwise a WHOLE number in
    /// 0..«Savollar soni» (old tests: 0..⌊MaxScore⌋). Valid rows are saved in one transaction; invalid rows are NOT
    /// saved and NOT clamped — they come back in <c>Errors</c> («Natija 0 dan {n} gacha butun son bo'lsin»,
    /// «O'quvchi bu guruh a'zosi emas»). A row is accepted for a current (active, not frozen) member, or for a
    /// student who already has a score in this test (a left member's result can still be corrected or cleared).
    /// A repeated student id — the LAST row wins.
    /// </summary>
    public static async Task<(TestScoreBatchResult? Result, string? Error)> SaveScoresBatchAsync(
        IAppDbContext db, string testId, IReadOnlyList<TestScoreBatchRow>? rows)
    {
        var t = await db.TestResults.FindAsync(testId);
        if (t is null) return (null, "Test topilmadi");
        rows ??= Array.Empty<TestScoreBatchRow>();
        if (rows.Count > TestEntryRules.MaxBatchRows) return (null, TestEntryRules.TooManyRows);

        var max = TestEntryRules.MaxSolved(t.QuestionCount, t.MaxScore);
        var existing = await db.TestScores.Where(s => s.TestResultId == testId).ToListAsync();
        var byStudent = existing.GroupBy(s => s.StudentId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var memberIds = (await CurrentMembers(db, t.GroupId).Select(sg => sg.StudentId).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        var errors = new List<TestScoreBatchError>();
        var last = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.StudentId))
            .GroupBy(r => r.StudentId.Trim(), StringComparer.Ordinal)
            .Select(g => g.Last() with { StudentId = g.Key });
        foreach (var row in last)
        {
            var sid = row.StudentId;
            byStudent.TryGetValue(sid, out var score);
            if (!memberIds.Contains(sid) && score is null)
            {
                errors.Add(new TestScoreBatchError(sid, TestEntryRules.NotMember));
                continue;
            }
            var err = TestEntryRules.ValidateSolved(row.Solved, max);
            if (err is not null)
            {
                errors.Add(new TestScoreBatchError(sid, err));
                continue;
            }
            if (row.Solved is null)
            {
                if (score is not null)
                {
                    db.TestScores.Remove(score);
                    byStudent.Remove(sid);
                }
            }
            else if (score is null)
            {
                var added = new TestScore { TestResultId = testId, StudentId = sid, Score = row.Solved.Value };
                db.TestScores.Add(added);
                byStudent[sid] = added;
            }
            else if (score.Score != row.Solved.Value)
            {
                score.Score = row.Solved.Value;
            }
        }
        await db.SaveChangesAsync();
        var detail = await DetailAsync(db, testId);
        return (new TestScoreBatchResult(detail!, errors), null);
    }
}
