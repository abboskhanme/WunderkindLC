using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// WHEN a scheduled auto-message rule runs (SPEC P2.3, pure, <c>PaymentReminderScheduleTests</c>).
/// <list type="bullet">
///   <item><c>monthly_day_time</c> — <c>payment_debt</c>, <c>rating_top</c>, <c>rating_bottom</c>: each rule has its own
///     day of month (1..31, clamped to the month end) and time;</item>
///   <item><c>daily_time</c> — <c>birthday</c>: every day after the rule's time;</item>
///   <item><c>custom</c> — <c>custom_schedule</c> (its own daily/monthly window, <c>CustomReminderService</c>);</item>
///   <item><c>none</c> — event-driven triggers.</item>
/// </list>
/// </summary>
public static class AutoMessageSchedule
{
    public const string MonthlyDayTime = "monthly_day_time";
    public const string DailyTime = "daily_time";
    public const string Custom = "custom";
    public const string None = "none";

    /// <summary>Default time when a rule's <c>ScheduleTime</c> cannot be parsed (data SQL sets 09:00).</summary>
    public static readonly TimeSpan DefaultTime = new(9, 0, 0);

    public static string ModeOf(string? trigger) => trigger switch
    {
        AutoMessageTriggers.PaymentDebt or AutoMessageTriggers.RatingTop or AutoMessageTriggers.RatingBottom => MonthlyDayTime,
        AutoMessageTriggers.Birthday => DailyTime,
        AutoMessageTriggers.CustomSchedule => Custom,
        _ => None,
    };

    /// <summary>Parses <c>HH:mm</c> (falls back to <see cref="DefaultTime"/>).</summary>
    public static TimeSpan TimeOf(AutoMessageRule rule) =>
        TryParseTime(rule.ScheduleTime, out var t) ? t : DefaultTime;

    /// <summary>Strict <c>HH:mm</c> (00:00..23:59) — used by validation.</summary>
    public static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>Is the rule due at <paramref name="now"/> (centre time)? For <c>monthly_day_time</c>: today is the
    /// rule's day (31 → last day of a short month) and the time has passed; <c>daily_time</c>: the time has passed.
    /// Other modes are never "due" here.</summary>
    public static bool IsDue(AutoMessageRule rule, DateTime now)
    {
        var mode = ModeOf(rule.Trigger);
        var today = DateOnly.FromDateTime(now);
        return mode switch
        {
            MonthlyDayTime => CustomReminderService.IsScheduledDay(
                                  new AutoMessageRule { ScheduleType = "monthly", ScheduleDayOfMonth = rule.ScheduleDayOfMonth },
                                  today)
                              && now.TimeOfDay >= TimeOf(rule),
            DailyTime => now.TimeOfDay >= TimeOf(rule),
            _ => false,
        };
    }
}
