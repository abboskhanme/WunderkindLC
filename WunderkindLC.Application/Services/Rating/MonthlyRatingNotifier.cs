using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Rating;

/// <summary>
/// MONTHLY TOP / BOTTOM messages (CRM upgrade U4d, docs/SPEC-crm-upgrade.md P4.5; rules <c>.claude/rules/rating.md</c>).
///
/// <para>Every 10 minutes: each enabled <c>rating_top</c> / <c>rating_bottom</c> rule that is DUE
/// (<see cref="AutoMessageSchedule.IsDue"/> — its own day of month and time, default 3rd 10:00) and not yet done for
/// the RATED month (= the previous calendar month) builds the lists (<see cref="TopBottomService"/> — the same lists
/// as the report tab) and dispatches rule by rule through the outbox: <c>rating_top</c> → TOP students,
/// <c>rating_bottom</c> → BOTTOM students. Tokens {oy} (rated month), {orin} "r/n", {foiz} T, {guruh}
/// (group name, «markaz» in centre scope); <c>eventKey = "{yyyy-MM}"</c>.</para>
///
/// <para><b>Once per month, in the database</b> (<see cref="JobRunMarker"/>, the marker "date" is the rated month's
/// first day): per student <c>rating:{ruleId}:{yyyy-MM}:{studentId}</c> is claimed BEFORE dispatch, the rule
/// <c>rating:{ruleId}:{yyyy-MM}</c> when the run finished without an error — a restart never repeats a message, a
/// failed run resumes on the next tick. Changing the rule's day after it ran does not send the month again.</para>
///
/// <para>A student in the lists of two groups gets ONE message per rule (TOP: the best place, BOTTOM: the lowest T).
/// Students of closed groups only (<see cref="MessagingAudience.ClosedGroupStudentIdsAsync"/>) and archived students
/// get nothing (system-initiated message — messaging.md).</para>
///
/// <para><b>No catch-up</b> (like every monthly reminder): if the server is down for the whole scheduled day, that
/// month's TOP/BOTTOM is skipped — logged once (<c>rating-skip:{ruleId}:{yyyy-MM}</c>).</para>
/// </summary>
public class MonthlyRatingNotifier(
    IServiceProvider services,
    AutoMessageService autoMsg,
    ILogger<MonthlyRatingNotifier> logger) : BackgroundService
{
    public const string JobPrefix = "rating";
    public const string SkipPrefix = "rating-skip";

    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(10);

    public static string RuleKey(string ruleId, string month) => $"{JobPrefix}:{ruleId}:{month}";

    public static string StudentKey(string ruleId, string month, string studentId) => $"{JobPrefix}:{ruleId}:{month}:{studentId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(AppClock.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oylik TOP/BOTTOM siklida xatolik");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>One wake-up at <paramref name="now"/> (centre time) — every due rule, once per rated month.</summary>
    internal async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var month = TopBottomService.PreviousMonth(today);
        var marker = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd");

        List<AutoMessageRule> rules;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            rules = await db.AutoMessageRules.AsNoTracking()
                .Where(r => r.Enabled
                            && (r.Trigger == AutoMessageTriggers.RatingTop || r.Trigger == AutoMessageTriggers.RatingBottom))
                .ToListAsync(ct);
        }

        foreach (var rule in rules)
        {
            var key = RuleKey(rule.Id, month);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                if (await JobRunMarker.IsDoneAsync(db, key, marker, ct)) continue;
                if (!AutoMessageSchedule.IsDue(rule, now))
                {
                    // The scheduled day of this month has passed and the month was never sent — no catch-up, log once.
                    if (DayPassed(rule, today)
                        && await JobRunMarker.TryClaimAsync(db, $"{SkipPrefix}:{rule.Id}:{month}", marker, ct))
                        logger.LogWarning(
                            "Oylik TOP/BOTTOM ({Trigger}, qoida {Rule}) {Month} uchun YUBORILMADI — qoida kuni o'tib ketgan (server o'chiq edi yoki qoida keyin yoqilgan)",
                            rule.Trigger, rule.Id, month);
                    continue;
                }
            }

            var sent = await RunRuleAsync(rule, month, marker, today, ct);
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                await JobRunMarker.TryClaimAsync(db, key, marker, ct);
                await JobRunMarker.PruneAsync(db, JobPrefix + ":", marker.AddMonths(-3), ct);
                await JobRunMarker.PruneAsync(db, SkipPrefix + ":", marker.AddMonths(-3), ct);
            }
            logger.LogInformation("Oylik TOP/BOTTOM ({Trigger}, qoida {Rule}) {Month}: {Count} o'quvchiga yuborildi.",
                rule.Trigger, rule.Id, month, sent);
        }
    }

    /// <summary>Today is AFTER the rule's (clamped) day of this month.</summary>
    internal static bool DayPassed(AutoMessageRule rule, DateOnly today)
    {
        var day = Math.Min(Math.Clamp(rule.ScheduleDayOfMonth, 1, 31), DateTime.DaysInMonth(today.Year, today.Month));
        return today.Day > day;
    }

    /// <summary>Dispatches one rule; returns the number of students dispatched. Throws on an infrastructure error —
    /// the rule is then NOT marked done (claimed students are not repeated on the retry).</summary>
    private async Task<int> RunRuleAsync(AutoMessageRule rule, string month, DateOnly marker, DateOnly today, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var cache = scope.ServiceProvider.GetService<DataCache>();
        var load = cache is null ? RatingEngine.Direct(db) : RatingEngine.Cached(cache);

        var (dto, lists, error) = await TopBottomService.BuildAsync(db, load, month, null, today, ct);
        if (dto is null)
        {
            logger.LogWarning("Oylik TOP/BOTTOM: {Month} davri hal qilinmadi — {Error}", month, error);
            return 0;
        }
        var centre = dto.Scope == TopBottomRules.ScopeCentre;
        var top = rule.Trigger == AutoMessageTriggers.RatingTop;
        var targets = Targets(lists, top);
        if (targets.Count == 0) return 0;

        var ids = targets.Select(t => t.Entry.StudentId).ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id) && !s.IsArchived).ToDictionaryAsync(s => s.Id, ct);
        var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        var groupIds = targets.Select(t => t.List.GroupId).Where(g => g.Length > 0).Distinct().ToList();
        var groups = await db.Classes.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var monthName = MessageTokenizer.MonthNameUz(int.Parse(month[5..7]));

        var sent = 0;
        foreach (var (list, entry) in targets)
        {
            if (!students.TryGetValue(entry.StudentId, out var s) || closed.Contains(s.Id)) continue;
            // OLUVCHI BELGISI — yuborishdan OLDIN (restart/ikkinchi nusxa takrorlamasin).
            if (!await JobRunMarker.TryClaimAsync(db, StudentKey(rule.Id, month, s.Id), marker, ct)) continue;
            var extra = new Dictionary<string, string>
            {
                ["{oy}"] = monthName,
                ["{orin}"] = $"{entry.Rank}/{list.Scored}",
                ["{foiz}"] = $"{(int)Math.Round(entry.TestAvg, MidpointRounding.AwayFromZero)}%",
                ["{guruh}"] = centre ? "markaz" : list.GroupName,
            };
            await autoMsg.DispatchStudentAsync(db, rule.Trigger, s, extra,
                centre ? null : groups.GetValueOrDefault(list.GroupId), ct,
                eventKey: month, sourceId: rule.Id, onlyRuleId: rule.Id);
            sent++;
        }
        return sent;
    }

    /// <summary>
    /// The students a rule addresses — ONE entry per student: TOP → the best place (then the higher T), BOTTOM → the
    /// lowest T (then the worse place).
    /// </summary>
    internal static List<(TopBottomList List, TopBottomEntry Entry)> Targets(IReadOnlyList<TopBottomList> lists, bool top)
    {
        var all = lists.SelectMany(l => (top ? l.Top : l.Bottom).Select(e => (List: l, Entry: e)));
        var picked = top
            ? all.GroupBy(x => x.Entry.StudentId).Select(g => g.OrderBy(x => x.Entry.Rank).ThenByDescending(x => x.Entry.TestAvg).First())
            : all.GroupBy(x => x.Entry.StudentId).Select(g => g.OrderBy(x => x.Entry.TestAvg).ThenByDescending(x => x.Entry.Rank).First());
        return picked.OrderBy(x => x.List.GroupName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Entry.Rank).ToList();
    }
}
