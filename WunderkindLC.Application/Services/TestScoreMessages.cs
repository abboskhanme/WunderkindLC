using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «Ota-onalarga yuborish» — a test's results to parents through the Telegram OUTBOX (CRM upgrade U4a,
/// docs/SPEC-crm-upgrade.md P4.2; rules <c>.claude/rules/tests.md</c>, <c>messaging.md</c> §0.5).
///
/// <para><b>Manual, per test</b> (owner decision 2026-10-11): scores are typed one by one, per-save messages would
/// spam parents with shifting ranks. The trigger is <c>test_score</c>; its enabled rules decide the audience and the
/// template.</para>
///
/// <para><b>Only changed scores are re-sent.</b> The outbox dedupe key carries the score
/// (<c>eventKey = "{testId}:{score}"</c>): a student whose every reachable chat already has the row for the CURRENT
/// score is skipped BEFORE dispatch (counted in <c>skippedUnchanged</c>) — so the cabinet bell is not repeated either.
/// <c>resend = true</c> appends <c>":r{ticks}"</c> — a new key, everyone gets the message again.</para>
///
/// <para>A student without a reachable chat (no linked, non-blocked parent chat for any enabled rule) is counted in
/// <c>noTelegram</c> and NOT dispatched (no cabinet bell either): the button sends to parents; the cabinet already shows
/// test results.</para>
/// </summary>
public static class TestScoreMessages
{
    public const string RuleMissing =
        "\"Test natijasi (ota-onaga)\" avto-xabar qoidasi yoqilmagan — Xabarlar → Avto xabarlar";

    /// <summary>Unranked place in the rating tokens.</summary>
    public const string NoRank = "—";

    /// <summary>Outbox event key of one result: <c>"{testId}:{score}"</c> (+ <c>":r{ticks}"</c> on an explicit resend).</summary>
    public static string EventKey(string testId, decimal score, long? resendTicks = null) =>
        resendTicks is null
            ? $"{testId}:{TestEntryRules.FormatScore(score)}"
            : $"{testId}:{TestEntryRules.FormatScore(score)}:r{resendTicks.Value}";

    /// <summary>The dedupe key the dispatcher writes for (rule, student, chat) — <c>AutoMessageService</c>'s format.</summary>
    public static string DedupeKey(string ruleId, string studentId, long chatId, string eventKey) =>
        TelegramOutbox.NormalizeKey($"{AutoMessageTriggers.TestScore}:{ruleId}:{studentId}:{chatId}:{eventKey}");

    /// <summary>"3/18" or «—».</summary>
    public static string Place(int? rank, int total) => rank is { } r && total > 0 ? $"{r}/{total}" : NoRank;

    /// <summary>
    /// Message tokens of one result (pure): {test}, {turi}, {sana} (dd.MM.yyyy), {ball} "25/30", {foiz} "83%",
    /// {orin} "3/18" (place in this test / scored), {guruh_orin} and {reyting_orin} (the month's rating, «—» when
    /// unranked), {guruh}.
    /// </summary>
    public static Dictionary<string, string> Tokens(
        TestResult test, string groupName, decimal score, int rankInTest, int scoredInTest,
        string groupPlace, string centrePlace)
    {
        var pct = RatingFormula.Percent(score, test.MaxScore);
        var max = test.QuestionCount > 0 ? test.QuestionCount : test.MaxScore;
        return new Dictionary<string, string>
        {
            ["{test}"] = test.Name,
            ["{turi}"] = TestEntryRules.KindLabel(test.Kind),
            ["{sana}"] = DisplayDate(test.Date),
            ["{ball}"] = $"{TestEntryRules.FormatScore(score)}/{TestEntryRules.FormatScore(max)}",
            ["{foiz}"] = pct is null ? NoRank : $"{(int)Math.Round(pct.Value, MidpointRounding.AwayFromZero)}%",
            ["{orin}"] = Place(rankInTest, scoredInTest),
            ["{guruh_orin}"] = groupPlace,
            ["{reyting_orin}"] = centrePlace,
            ["{guruh}"] = groupName,
        };
    }

    /// <summary>"2026-10-12" → "12.10.2026" (anything else as is).</summary>
    public static string DisplayDate(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : iso;

    /// <summary>
    /// Queues the results of <paramref name="testId"/>. Errors: test not found, no enabled <c>test_score</c> rule
    /// (<see cref="RuleMissing"/>). <paramref name="rating"/> loads the month's rating (cached in the app, direct in
    /// tests) for {guruh_orin}/{reyting_orin}. Sets <c>ResultsSentAt/ById</c> and writes one audit row — only when
    /// something was queued.
    /// </summary>
    public static async Task<(SendTestResultsResultDto? Result, string? Error)> SendAsync(
        IAppDbContext db, AutoMessageService autoMsg, RatingLoader rating, string testId, bool resend,
        string? actorId, AuditService? audit, DateOnly today, CancellationToken ct = default)
    {
        var test = await db.TestResults.FirstOrDefaultAsync(t => t.Id == testId, ct);
        if (test is null) return (null, "Test topilmadi");

        var rules = (await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Enabled && r.Trigger == AutoMessageTriggers.TestScore)
                .ToListAsync(ct))
            .Where(AutoMessageChannels.Telegram)
            .ToList();
        if (rules.Count == 0) return (null, RuleMissing);

        var group = await db.Classes.FirstOrDefaultAsync(g => g.Id == test.GroupId, ct);
        var groupName = group?.Name ?? "";

        var scores = await db.TestScores.AsNoTracking().Where(s => s.TestResultId == testId)
            .Select(s => new { s.StudentId, s.Score }).ToListAsync(ct);
        // Place inside the test — among EVERY scored row (the detail page's numbers).
        var ranks = TestEntryRules.Ranks(scores.Select(s => (s.StudentId, s.Score)));
        var ids = scores.Select(s => s.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id) && !s.IsArchived).ToListAsync(ct);

        // The month's rating (the month of the test date) — «—» when the month is in the future.
        RatingResult? month = null;
        var (period, _) = RatingPeriods.Resolve(RatingPeriods.Month, test.Date.Length >= 7 ? test.Date[..7] : null, today);
        if (period is not null) month = await rating(period);

        // Reachable chats per student (parents / the student's own / the group's teacher — by rule audience).
        var regs = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.BlockedAt == null && ids.Contains(r.StudentId))
            .Select(r => new { r.StudentId, r.ChatId, r.Relation })
            .ToListAsync(ct);
        var regsByStudent = regs.ToLookup(r => r.StudentId, StringComparer.Ordinal);
        var teacherChats = new List<long>();
        if (rules.Any(r => r.Audience == "teachers") && !string.IsNullOrEmpty(group?.TeacherId))
            teacherChats = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.TeacherId == group!.TeacherId && r.BlockedAt == null)
                .Select(r => r.ChatId).Distinct().ToListAsync(ct);

        var sentKeys = (await db.MessageDeliveries.AsNoTracking()
                .Where(d => d.Trigger == AutoMessageTriggers.TestScore && d.SourceId == testId)
                .Select(d => d.DedupeKey)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        long? resendTicks = resend ? AppClock.Now.Ticks : null;
        int queued = 0, noTelegram = 0, unchanged = 0;
        foreach (var s in students.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
        {
            var score = scores.First(x => x.StudentId == s.Id).Score;
            var eventKey = EventKey(testId, score, resendTicks);

            var expected = new List<string>();
            foreach (var rule in rules)
            {
                IEnumerable<long> chats = rule.Audience switch
                {
                    "students" => regsByStudent[s.Id].Where(r => r.Relation == TelegramRelations.StudentSelf).Select(r => r.ChatId),
                    "teachers" => teacherChats,
                    _ => regsByStudent[s.Id].Where(r => TelegramRelations.IsParent(r.Relation)).Select(r => r.ChatId),
                };
                expected.AddRange(chats.Distinct().Select(c => DedupeKey(rule.Id, s.Id, c, eventKey)));
            }
            if (expected.Count == 0) { noTelegram++; continue; }
            if (!resend && expected.All(sentKeys.Contains)) { unchanged++; continue; }

            var gRow = month?.RowsByGroup[test.GroupId].FirstOrDefault(r => r.StudentId == s.Id);
            var cRow = month?.CentreByStudent.GetValueOrDefault(s.Id);
            var extra = Tokens(test, groupName, score,
                ranks.GetValueOrDefault(s.Id), scores.Count,
                Place(gRow?.Rank, month?.RankedInGroup.GetValueOrDefault(test.GroupId) ?? 0),
                Place(cRow?.Rank, month?.CentreTotal ?? 0));
            queued += await autoMsg.DispatchStudentAsync(db, AutoMessageTriggers.TestScore, s, extra, group, ct,
                eventKey: eventKey, sourceId: testId);
        }

        if (queued > 0)
        {
            test.ResultsSentAt = AppClock.Now;
            test.ResultsSentById = actorId;
            audit?.Record(TestResultService.AuditEntity, TestResultService.AuditEntityId(test.GroupId, test.Id), "update",
                $"Test natijalari ota-onalarga yuborildi: {test.Name}, {queued} ta");
            await db.SaveChangesAsync(ct);
        }
        return (new SendTestResultsResultDto(queued, noTelegram, unchanged), null);
    }
}
