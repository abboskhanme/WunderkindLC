using System.Globalization;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Attendance discipline — pure decisions (SPEC P3.4, <c>AttendanceDisciplineTests</c>): when a teacher gets a
/// reminder, when the lesson's attendance counts as NOT taken on time (fine + admin escalation) and the exact
/// message texts. The hosted <see cref="AttendanceDisciplineService"/> only orchestrates.
/// </summary>
public static class AttendanceDiscipline
{
    public const string FineActive = "active";
    public const string FineWaived = "waived";

    /// <summary>A reminder is sent within this window after its moment (the service ticks every minute; a short
    /// delay must not lose it, a long downtime must not send a stale one).</summary>
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(10);

    /// <summary>Decision for one lesson at <c>now</c>.</summary>
    /// <param name="Start">Lesson start (null — no/invalid start time: the lesson is skipped entirely).</param>
    /// <param name="Deadline">Start + deadline minutes.</param>
    /// <param name="DueReminders">Reminder minutes due right now (attendance not taken yet).</param>
    /// <param name="FineDue">The deadline passed and attendance was not taken ON TIME, fine is enabled.</param>
    /// <param name="EscalateDue">Same condition, escalation enabled, lesson is TODAY.</param>
    /// <param name="Late">The deadline passed and attendance was not taken on time (fine or not).</param>
    public sealed record Decision(
        DateTime? Start, DateTime? Deadline, IReadOnlyList<int> DueReminders,
        bool FineDue, bool EscalateDue, bool Late);

    private static readonly Decision Skip = new(null, null, [], false, false, false);

    /// <summary>"yyyy-MM-dd" + "HH:mm" → local DateTime, or null when either is not a valid value.</summary>
    public static DateTime? StartOf(string date, string startTime)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;
        var minutes = ScheduleRules.ParseTime(startTime);
        if (minutes is null) return null;
        return d.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes.Value);
    }

    /// <summary>
    /// <para>Reminders — TODAY only, at start + m for each m (inside <see cref="ReminderWindow"/>), only while
    /// attendance is not taken.</para>
    /// <para>At/after the deadline, attendance not taken ON TIME (<c>takenAt == null || takenAt &gt; deadline</c>)
    /// → fine (if enabled and amount &gt; 0) and escalation (if enabled, today only). Attendance taken AFTER the
    /// deadline still gets the fine — the rule is "during the lesson"; entry itself is never blocked.</para>
    /// <para>⚠️ NOT RETROACTIVE (2026-10-12): a lesson whose deadline is before <paramref name="disciplineSince"/>
    /// (the discipline was switched on later) is skipped entirely — no reminder, escalation or fine; a lesson whose
    /// deadline is before <paramref name="fineSince"/> is never fined. <c>null</c> = no lower bound (legacy rows).</para>
    /// </summary>
    public static Decision Decide(
        string date, string startTime, DateTime now, DateTime? takenAt, AttendanceSettingsDto s, bool isToday,
        DateTime? disciplineSince = null, DateTime? fineSince = null)
    {
        var start = StartOf(date, startTime);
        if (start is null) return Skip;
        var deadline = start.Value.AddMinutes(s.DeadlineMinutes);
        if (disciplineSince is { } ds && deadline < ds) return Skip;
        var fineAllowed = fineSince is not { } fs || deadline >= fs;

        var due = new List<int>();
        if (isToday && takenAt is null)
            foreach (var m in s.ReminderMinutes ?? [])
            {
                var at = start.Value.AddMinutes(m);
                if (now >= at && now < at + ReminderWindow) due.Add(m);
            }

        var late = now >= deadline && (takenAt is null || takenAt > deadline);
        return new Decision(
            start, deadline, due,
            FineDue: late && s.FineEnabled && s.FineAmount > 0 && fineAllowed,
            EscalateDue: late && isToday && s.EscalateEnabled,
            Late: late);
    }

    /// <summary>"Dars boshlandi" reminder to the teacher.</summary>
    public static string ReminderText(
        string groupName, string courseName, string startTime, DateTime deadline, bool fineEnabled, decimal fineAmount)
    {
        var course = string.IsNullOrWhiteSpace(courseName) ? "" : $" ({courseName})";
        var text = $"⏰ {groupName}{course} — dars {startTime} da boshlandi. Davomatni kiriting.";
        if (fineEnabled && fineAmount > 0)
            text += $"\n{deadline:HH:mm} gacha olinmasa {AuditService.Money(fineAmount)} so'm jarima yoziladi.";
        return text;
    }

    /// <summary>Fine notice to the teacher.</summary>
    public static string FineText(string date, string groupName, DateTime deadline, decimal amount) =>
        $"❗️ {Dmy(date)} — {groupName}: davomat {deadline:HH:mm} gacha olinmadi. {AuditService.Money(amount)} so'm jarima yozildi. "
        + "E'tirozingiz bo'lsa, administratorga murojaat qiling.";

    /// <summary>Escalation to the admins.</summary>
    public static string EscalationText(string groupName, string teacherName, string startTime, decimal? fine) =>
        $"⚠️ Davomat olinmadi: {groupName} — {(string.IsNullOrWhiteSpace(teacherName) ? "o'qituvchi biriktirilmagan" : teacherName)}, dars {startTime}."
        + (fine is > 0 ? $" Jarima: {AuditService.Money(fine.Value)} so'm." : "");

    /// <summary>"yyyy-MM-dd" → "dd.MM.yyyy" (unchanged when not a date).</summary>
    public static string Dmy(string date) =>
        date is { Length: >= 10 } ? $"{date[8..10]}.{date[5..7]}.{date[..4]}" : date;

    /// <summary>Waive reason: 1..300 characters after trimming, else an Uzbek error.</summary>
    public static string? WaiveReasonError(string? reason)
    {
        var r = (reason ?? "").Trim();
        if (r.Length == 0) return "Bekor qilish sababini yozing";
        if (r.Length > 300) return "Sabab 300 belgidan oshmasin";
        return null;
    }

    /// <summary>LessonReminderMark keys (per group, per day) — at most once even across restarts.</summary>
    public static string ReminderKey(string groupId, string date, int minute) => $"disc:rem:{groupId}:{date}:{minute}";
    public static string FineKey(string groupId, string date) => $"disc:fine:{groupId}:{date}";
    public static string EscalationKey(string groupId, string date) => $"disc:esc:{groupId}:{date}";
}
