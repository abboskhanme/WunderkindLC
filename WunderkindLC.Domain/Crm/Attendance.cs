namespace WunderkindLC.Domain;

// CRM upgrade — Phase 3 «Attendance» (docs/SPEC-crm-upgrade.md §4). Columns added to existing entities
// live here (partial classes) so the phase does not edit the shared Entities.cs.

public partial class LessonNote
{
    /// <summary>
    /// When attendance of this lesson was FIRST taken — the first «keldi» (Present) or absence/late reason,
    /// single cell or bulk (centre local time, <c>AppClock.Now</c>). Grades alone never set it.
    /// <para>"Attendance taken" for teacher reminders, fines and the monitor = ANY LessonNote of
    /// (ClassId, Date) with this value set (subject/period agnostic) — SPEC P3.1. Cell-by-cell teachers
    /// are therefore never fined although <see cref="AttendanceTaken"/> (= COMPLETE) is reached later.</para>
    /// </summary>
    public DateTime? AttendanceTakenAt { get; set; }

    /// <summary>AppUser id of whoever took attendance first (admin, staff or the teacher's account).</summary>
    public string? AttendanceTakenById { get; set; }
}

public partial class AbsenceReason
{
    /// <summary>
    /// «Sababli (uzrli)» — an excused absence (illness, family reason). Only analytics read it: excused
    /// absences do NOT continue a consecutive-absence streak (U3d). Never true together with
    /// <see cref="IsLate"/> (<c>AbsenceReasonRules.ExcusedError</c>). Editable even when the reason is used.
    /// </summary>
    public bool IsExcused { get; set; }
}

public partial class CenterMeta
{
    /// <summary>Master switch of attendance discipline: teacher reminders, admin escalation, fines.</summary>
    public bool AttendanceDisciplineEnabled { get; set; }

    /// <summary>Attendance is "on time" when first taken ≤ lesson start + N minutes (5..120).</summary>
    public int AttendanceDeadlineMinutes { get; set; } = 15;

    /// <summary>Teacher reminders, minutes after lesson start, comma separated, ascending, ≤ 3, each
    /// 1..deadline; empty = no reminders. Example "5,10".</summary>
    public string AttendanceReminderMinutes { get; set; } = "5,10";

    /// <summary>At the deadline, message admins when attendance was not taken.</summary>
    public bool AttendanceEscalateEnabled { get; set; } = true;

    /// <summary>Write a <see cref="TeacherAttendanceFine"/> when attendance was not taken on time.</summary>
    public bool AttendanceFineEnabled { get; set; }

    /// <summary>Fine per lesson (so'm), snapshot into every fine row. Must be &gt; 0 when the fine is on.</summary>
    public decimal AttendanceFineAmount { get; set; }

    /// <summary>Absent notice («kelmadi») goes at lesson end + N minutes (0..180). Used by U3b.</summary>
    public int AbsentNoticeDelayMinutes { get; set; } = 1;

    /// <summary>Absent notice approval: <c>auto</c> · <c>manual</c> · <c>late_only</c> (default). Used by U3b.</summary>
    public string AbsentNoticeApproval { get; set; } = "late_only";

    /// <summary>Id of the seeded contact reason «Ketma-ket darsga kelmadi» (U3d streak requests).</summary>
    public string? AbsenceStreakReasonId { get; set; }
}

/// <summary>
/// Teacher fine: attendance of a lesson was not taken by lesson start + <see cref="CenterMeta.AttendanceDeadlineMinutes"/>
/// (U3c, SPEC P3.4). At most ONE row per group per day (unique (GroupId, LessonDate)). Deducted inside
/// <c>SalaryLedger</c> (Expected) of <see cref="Month"/>; NOT a FinanceTransaction (no cash movement).
/// Waived rows (<c>Status = waived</c>) are kept for history and never deducted.
/// </summary>
public class TeacherAttendanceFine
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>The person teaching that day (substitute when assigned).</summary>
    public string TeacherId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    /// <summary>"yyyy-MM-dd".</summary>
    public string LessonDate { get; set; } = string.Empty;
    /// <summary>"yyyy-MM" — salary attribution month (= LessonDate[..7]).</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary>"HH:mm".</summary>
    public string LessonStart { get; set; } = string.Empty;
    /// <summary>Lesson start + deadline minutes (centre local time).</summary>
    public DateTime Deadline { get; set; }
    public decimal Amount { get; set; }
    /// <summary><c>active</c> · <c>waived</c>.</summary>
    public string Status { get; set; } = "active";
    /// <summary>When attendance was eventually taken that day (information only).</summary>
    public DateTime? AttendanceTakenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? WaivedAt { get; set; }
    public string? WaivedById { get; set; }
    public string? WaivedByName { get; set; }
    public string? WaiveReason { get; set; }
}

/// <summary>
/// A consecutive-unexcused-absence streak reached level 3 or 5 for one (student, group) — U3d, SPEC P3.5.
/// Derived data (excluded from backups); the unique key makes the background job idempotent.
/// </summary>
public class AbsenceStreakAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    /// <summary>First absent date of the streak ("yyyy-MM-dd").</summary>
    public string StreakStart { get; set; } = string.Empty;
    /// <summary>3 or 5.</summary>
    public int Level { get; set; }
    /// <summary>The «Bog'lanish kerak» request created for (or noted on) this streak.</summary>
    public string? ContactRequestId { get; set; }
    public DateTime CreatedAt { get; set; }
}
