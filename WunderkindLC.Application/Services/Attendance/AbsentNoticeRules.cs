using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «KELMADI» XABARLARI NAVBATI — pure decisions (SPEC P3.3, <c>AbsentNoticeTests</c>): when a notice goes, whether it
/// needs a manager's approval, which manager action is allowed in which status, when it expires and the exact texts.
/// <see cref="AttendanceNotify"/> (enqueue / reconcile), <see cref="AttendanceNotifyWorker"/> (send) and
/// <c>AttendanceNoticesController</c> (hold / approve / cancel) only orchestrate.
/// </summary>
public static class AbsentNoticeRules
{
    // ---- statuses (AttendanceNotifyItem.Status) ----
    public const string Pending = "pending";
    public const string Awaiting = "awaiting";
    public const string Held = "held";
    public const string Approved = "approved";
    public const string Sending = "sending";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Unknown = "unknown";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    /// <summary>Still in the queue (a manager can act, the worker may send or expire it).</summary>
    public static readonly IReadOnlyList<string> OpenStatuses = [Pending, Awaiting, Held, Approved];

    public static bool IsOpen(string status) => status is Pending or Awaiting or Held or Approved;

    // ---- manager actions ----
    public const string ActionHold = "hold";
    public const string ActionApprove = "approve";
    public const string ActionCancel = "cancel";
    public static readonly IReadOnlyList<string> Actions = [ActionHold, ActionApprove, ActionCancel];

    // ---- CancelReason ----
    public const string CancelManager = "manager";
    public const string CancelChanged = "changed";
    public const string CancelArchived = "archived";

    /// <summary>An open notice older than SendAfter + this never goes out (<c>expired</c>): a «kelmadi» that
    /// arrives the next morning, or after nobody approved it all evening, misleads the parent.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    public const int MaxNoteLength = 300;
    public const int MaxBulk = 500;

    /// <summary>409 text of a forbidden transition (already sent / closed).</summary>
    public const string ClosedMessage = "Xabar allaqachon yuborilgan yoki yopilgan";

    /// <summary>409 text of a concurrent change (the worker claimed it, another manager acted).</summary>
    public const string ConcurrencyMessage = "Holat o'zgardi, sahifani yangilang";

    /// <summary>
    /// Earliest send time of an ABSENT notice: date + lesson end + delay. Unknown / broken end (missing, or not
    /// after a known start) → <paramref name="markedAt"/> + delay. Never earlier than the mark itself (a mark
    /// entered after the end goes out right away).
    /// </summary>
    public static DateTime SendAfter(string date, string lessonEnd, string lessonStart, DateTime markedAt, int delayMinutes)
    {
        var delay = Math.Clamp(delayMinutes, 0, AttendanceSettingsRules.MaxNoticeDelay);
        var end = LessonEndOf(date, lessonStart, lessonEnd);
        var at = (end ?? markedAt).AddMinutes(delay);
        return at < markedAt ? markedAt : at;
    }

    /// <summary>date + "HH:mm" end, or null when the end is unknown or not after a known start (broken row).</summary>
    public static DateTime? LessonEndOf(string date, string lessonStart, string lessonEnd)
    {
        var end = AttendanceDiscipline.StartOf(date, lessonEnd);
        if (end is null) return null;
        var start = AttendanceDiscipline.StartOf(date, lessonStart);
        return start is not null && end <= start ? null : end;
    }

    /// <summary>Attendance was FIRST taken after lesson start + deadline minutes (no start → never late).</summary>
    public static bool TakenLate(string date, string lessonStart, DateTime? takenAt, int deadlineMinutes) =>
        takenAt is not null
        && AttendanceDiscipline.StartOf(date, lessonStart) is { } start
        && takenAt > start.AddMinutes(deadlineMinutes);

    /// <summary>
    /// Initial status. «Keldi» never needs approval. «Kelmadi»: <c>auto</c> → pending; <c>manual</c> → awaiting;
    /// <c>late_only</c> (default, also any unknown value) → awaiting only when attendance was taken late.
    /// </summary>
    public static string InitialStatus(string kind, string approval, bool takenLate)
    {
        if (kind == AttendanceNotify.KindPresent) return Pending;
        return approval switch
        {
            AttendanceSettingsRules.ApprovalAuto => Pending,
            AttendanceSettingsRules.ApprovalManual => Awaiting,
            _ => takenLate ? Awaiting : Pending,
        };
    }

    /// <summary>
    /// Manager transitions: hold from pending/awaiting/approved; approve from pending/awaiting/held; cancel from
    /// pending/awaiting/held/approved. Anything else (sending, sent, failed, expired, cancelled…) → 409.
    /// </summary>
    public static bool CanApply(string status, string action) => action switch
    {
        ActionHold => status is Pending or Awaiting or Approved,
        ActionApprove => status is Pending or Awaiting or Held,
        ActionCancel => status is Pending or Awaiting or Held or Approved,
        _ => false,
    };

    /// <summary>Status after a manager action (null — unknown action).</summary>
    public static string? TargetStatus(string action) => action switch
    {
        ActionHold => Held,
        ActionApprove => Approved,
        ActionCancel => Cancelled,
        _ => null,
    };

    /// <summary>The worker sends it now: pending or approved, and SendAfter reached. Awaiting / held never go.</summary>
    public static bool Sendable(string status, DateTime sendAfter, DateTime now) =>
        status is Pending or Approved && sendAfter <= now;

    /// <summary>An open notice nobody released in time.</summary>
    public static bool IsExpired(string status, DateTime sendAfter, DateTime now) =>
        IsOpen(status) && now > sendAfter + MaxAge;

    /// <summary>
    /// Does the journal still say what the notice says? Absent: a non-late reason; present: «keldi».
    /// <paramref name="entries"/> — the student's entries of that lesson (any quarter).
    /// </summary>
    public static bool JournalStillValid(string kind, IEnumerable<JournalEntry> entries, IReadOnlySet<string> lateIds) =>
        kind == AttendanceNotify.KindPresent
            ? entries.Any(e => e.Present && e.ReasonId is null)
            : entries.Any(e => e.ReasonId is not null && !lateIds.Contains(e.ReasonId));

    /// <summary>Counter / chip key of a status (the control page and the home widget).</summary>
    public static string CountKey(string status) => status switch
    {
        Awaiting => "awaiting",
        Held => "held",
        Approved => "approved",
        Done => "sent",
        Failed or Unknown => "failed",
        Expired => "expired",
        Cancelled => "cancelled",
        _ => "pending",   // pending, sending
    };

    public static readonly IReadOnlyList<string> CountKeys =
        ["pending", "awaiting", "held", "approved", "sent", "failed", "cancelled", "expired"];

    /// <summary>Uzbek status label (same as the client's `lib/absentNotices.ts`).</summary>
    public static string Label(string status) => status switch
    {
        Pending => "Rejalashtirilgan",
        Awaiting => "Tasdiq kutmoqda",
        Held => "To'xtatilgan",
        Approved => "Tasdiqlangan",
        Sending => "Yuborilmoqda",
        Done => "Yuborildi",
        Failed => "Xato",
        Unknown => "Noma'lum",
        Expired => "Muddati o'tdi",
        Cancelled => "Bekor qilindi",
        _ => status,
    };

    /// <summary>Note: trimmed, ≤ <see cref="MaxNoteLength"/>; empty → null. Error text when too long.</summary>
    public static (string? Note, string? Error) CleanNote(string? note)
    {
        var n = (note ?? "").Trim();
        if (n.Length > MaxNoteLength) return (null, $"Izoh {MaxNoteLength} belgidan oshmasin");
        return (n.Length == 0 ? null : n, null);
    }

    /// <summary>LessonReminderMark key — managers are alerted ONCE per lesson (group, date).</summary>
    public static string AlertKey(string groupId, string date) => $"notice-await:{groupId}:{date}";

    /// <summary>"https://host" from <c>App:Host</c> (scheme and path tolerated), "" when not configured.</summary>
    public static string BaseUrl(string? appHost)
    {
        var h = (appHost ?? "").Trim();
        if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) h = h[8..];
        else if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) h = h[7..];
        var slash = h.IndexOf('/');
        if (slash >= 0) h = h[..slash];
        h = h.Trim();
        return h.Length == 0 ? "" : "https://" + h;
    }

    /// <summary>
    /// Managers' alert (Telegram + bell) when «kelmadi» notices of a lesson wait for approval. Late attendance:
    /// «davomat kech olingan (14:40)»; <c>manual</c> mode with on-time attendance: «tasdiqlash yoqilgan».
    /// The link is omitted when <c>App:Host</c> is empty.
    /// </summary>
    public static string AlertText(string groupName, string date, int count, bool takenLate, DateTime? takenAt, string baseUrl)
    {
        var why = takenLate
            ? $"davomat kech olingan ({(takenAt is { } t ? t.ToString("HH:mm") : "—")})"
            : "har bir xabarni menejer tasdiqlaydi";
        var text = $"🕓 {groupName} ({AttendanceDiscipline.Dmy(date)}) — {count} ta «kelmadi» xabari tasdiq kutmoqda: {why}.";
        if (baseUrl.Length > 0) text += $" Tekshiring: {baseUrl}/admin/nazorat/davomat-xabarlari?date={date}";
        return text;
    }

    /// <summary>Audit summary of a manager action (Uzbek, full sentence).</summary>
    public static string AuditSummary(string action, string kind, string studentName, string groupName, string date, string? note)
    {
        var verb = action switch
        {
            ActionHold => "to'xtatildi",
            ActionApprove => "tasdiqlandi",
            _ => "bekor qilindi",
        };
        var what = kind == AttendanceNotify.KindPresent ? "«Keldi»" : "«Kelmadi»";
        var s = $"{what} xabari {verb}: {studentName}, {groupName}, {AttendanceDiscipline.Dmy(date)}";
        return string.IsNullOrWhiteSpace(note) ? s : $"{s} — izoh: {note}";
    }
}

/// <summary>Audit of the absent-notice control page (`AuditSections`: «Guruhlar»).</summary>
public static class AbsentNoticeAudit
{
    public const string EntityType = "AttendanceNotice";

    /// <summary><c>"{groupId}:{itemId}"</c> — shows on the group's «Tarix» tab too (Membership pattern).</summary>
    public static string EntityId(string groupId, string itemId) => $"{groupId}:{itemId}";
}
