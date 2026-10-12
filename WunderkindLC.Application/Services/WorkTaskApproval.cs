using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TASK APPROVAL rules (CRM Phase 5, <c>.claude/rules/topshiriqlar.md</c> §8) — pure, tested
/// (<c>WorkTaskApprovalTests</c>). The DB side is <c>WorkTaskFlow.Approval.cs</c>.
///
/// <para>A task given to SOMEONE ELSE needs the giver's approval after it is done: «Bajarildi» → «Tasdiq
/// kutmoqda» → «Tasdiqlangan» (or «Qaytarildi» back to the first column with a reason). Self-assigned tasks
/// have no approval step. Existing tasks: <c>ApprovalRequired = false</c> — untouched.</para>
/// </summary>
public static class WorkTaskApproval
{
    public const string StateOpen = "open";
    public const string StateOverdue = "overdue";
    public const string StateAwaiting = "awaiting_approval";
    public const string StateApproved = "approved";
    public const string StateDone = "done";

    /// <summary>Uzbek labels of <see cref="State"/> (the client keeps the same list).</summary>
    public static readonly IReadOnlyDictionary<string, string> StateLabels = new Dictionary<string, string>
    {
        [StateOpen] = "Ochiq",
        [StateOverdue] = "Kechikkan",
        [StateAwaiting] = "Tasdiq kutmoqda",
        [StateApproved] = "Tasdiqlangan",
        [StateDone] = "Bajarildi",
    };

    public const string NotApproverMessage = "Faqat topshiriq bergan xodim tasdiqlaydi";
    public const string NotRequiredMessage = "Bu topshiriq tasdiqlashni talab qilmaydi";
    public const string NotDoneMessage = "Topshiriq hali bajarilmagan";
    public const string AlreadyApprovedMessage = "Topshiriq allaqachon tasdiqlangan";
    public const string ReasonMessage = "Qaytarish sababini yozing (500 belgigacha)";
    public const string NoOpenColumnMessage = "Doskada ochiq ustun yo'q — qaytarib bo'lmaydi";
    public const string ChatForbiddenMessage = "Bu amal sizga ruxsat etilmagan";
    public const int MaxReasonLength = 500;

    /// <summary>
    /// Approval for a NEW task: the explicit choice if given, otherwise on when the assignee is someone other
    /// than the creator (a self-assigned or unassigned task has nobody to approve it).
    /// </summary>
    public static bool DefaultRequired(bool? requested, string? assigneeId, string? creatorId)
    {
        if (requested is { } r) return r;
        var a = (assigneeId ?? "").Trim();
        return a.Length > 0 && a != (creatorId ?? "").Trim();
    }

    /// <summary>
    /// Card/list state: not done → <c>open</c>/<c>overdue</c>; done without approval step → <c>done</c>;
    /// done and waiting → <c>awaiting_approval</c>; approved → <c>approved</c>.
    /// </summary>
    public static string State(bool isDone, bool isOverdue, bool approvalRequired, DateTime? approvedAt) =>
        !isDone ? (isOverdue ? StateOverdue : StateOpen)
        : !approvalRequired ? StateDone
        : approvedAt is null ? StateAwaiting
        : StateApproved;

    /// <summary>Approve/return gate: the task's creator, or any admin/superadmin.</summary>
    public static bool IsApprover(WorkTask t, string? userId, bool isAdmin) =>
        isAdmin || (!string.IsNullOrWhiteSpace(userId) && userId == t.CreatedById);

    /// <summary>Why the task can NOT be approved now (<c>null</c> = it can).</summary>
    public static string? ApproveError(WorkTask t, bool inDoneColumn) =>
        !t.ApprovalRequired ? NotRequiredMessage
        : !inDoneColumn ? NotDoneMessage
        : t.ApprovedAt is not null ? AlreadyApprovedMessage
        : null;

    /// <summary>Why the task can NOT be returned now (<c>null</c> = it can). An approved task may still be
    /// returned (the approval is withdrawn).</summary>
    public static string? ReturnError(WorkTask t, bool inDoneColumn, string? reason)
    {
        var r = (reason ?? "").Trim();
        if (r.Length == 0 || r.Length > MaxReasonLength) return ReasonMessage;
        if (!t.ApprovalRequired) return NotRequiredMessage;
        if (!inDoneColumn) return NotDoneMessage;
        return null;
    }
}
