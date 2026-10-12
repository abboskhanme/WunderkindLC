using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TASK APPROVAL — the single DB-side place (CRM Phase 5, <c>.claude/rules/topshiriqlar.md</c> §8). Panel
/// (<c>WorkTasksController</c>), column flag changes (<see cref="SyncCompletionAsync"/>) and the Telegram bot
/// (<c>wtdone:</c> / <c>wtok:</c>) all call these methods — the same "single place" rule as
/// <see cref="SpawnRepeatAsync"/>. Rules: <see cref="WorkTaskApproval"/>. <b>SaveChanges is the caller's</b>
/// (except <see cref="ApproveFromChatAsync"/>, which is a complete bot action).
/// </summary>
public static partial class WorkTaskFlow
{
    public const string EventSubmitted = "submitted";
    public const string EventApproved = "approved";
    public const string EventReturned = "returned";

    /// <summary>Bell notification type (kabinet qo'ng'iroqchasi).</summary>
    private const string BellType = "work_task";

    /// <summary>
    /// The task has just entered a done column (CompletedAt is set; the repeat copy is spawned by the caller).
    /// If it needs approval: history event <c>submitted</c> and the CREATOR is notified (bell + Telegram with
    /// «✅ Tasdiqlash»). Nothing is sent when the creator completed it himself.
    /// </summary>
    /// <remarks>⚠️ The Telegram message goes out before the caller's SaveChanges (same request, under
    /// <see cref="LockKey"/>); the bell row is saved with the caller's SaveChanges.</remarks>
    public static async Task OnCompletedAsync(
        IAppDbContext db, WorkTask t, string actorId, TelegramService? telegram = null, CancellationToken ct = default)
    {
        if (!t.ApprovalRequired) return;
        t.ApprovedAt = null;
        t.ApprovedById = "";
        db.WorkTaskEvents.Add(new WorkTaskEvent
        {
            TaskId = t.Id, ActorId = actorId, Kind = EventSubmitted,
            Text = "Bajarildi — tasdiqlash kutilmoqda",
        });

        if (string.IsNullOrWhiteSpace(t.CreatedById) || t.CreatedById == actorId) return;
        var assigneeName = await db.Users.Where(u => u.Id == t.AssigneeId)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        var text = WorkTaskTelegram.ApprovalRequestText(t, assigneeName);
        NotificationStore.Add(db, t.CreatedById, "Topshiriq bajarildi — tasdiqlang", text, BellType);
        await SendToUserAsync(db, telegram, t.CreatedById, text, WorkTaskTelegram.ApproveKeyboard(t.Id), ct);
    }

    /// <summary>The task LEFT the done columns (reopened / moved back): its approval is withdrawn.</summary>
    public static void OnReopened(WorkTask t)
    {
        t.ApprovedAt = null;
        t.ApprovedById = "";
    }

    /// <summary>Is the task in a done column now?</summary>
    public static Task<bool> InDoneColumnAsync(IAppDbContext db, WorkTask t, CancellationToken ct = default) =>
        db.WorkTaskColumns.AnyAsync(c => c.Id == t.ColumnId && c.IsDone, ct);

    /// <summary>
    /// APPROVE (gate already checked by the caller — <see cref="WorkTaskApproval.IsApprover"/>). Returns the
    /// refusal text or <c>null</c>. Assignee gets bell + «👍 Topshiriq tasdiqlandi». SaveChanges — caller.
    /// </summary>
    public static async Task<string?> ApproveAsync(
        IAppDbContext db, WorkTask t, string actorId, TelegramService? telegram = null, CancellationToken ct = default)
    {
        var error = WorkTaskApproval.ApproveError(t, await InDoneColumnAsync(db, t, ct));
        if (error is not null) return error;

        t.ApprovedAt = AppClock.Now;
        t.ApprovedById = actorId;
        t.UpdatedAt = AppClock.Now;
        db.WorkTaskEvents.Add(new WorkTaskEvent
        {
            TaskId = t.Id, ActorId = actorId, Kind = EventApproved, Text = "Tasdiqlandi",
        });
        var text = WorkTaskTelegram.ApprovedText(t);
        NotificationStore.Add(db, t.AssigneeId, "Topshiriq tasdiqlandi", text, BellType);
        await SendToUserAsync(db, telegram, t.AssigneeId, text, null, ct);
        return null;
    }

    /// <summary>
    /// RETURN to work with a reason: the task goes to the board's FIRST non-done column (top, order 0),
    /// CompletedAt/approval cleared, <c>ReturnCount++</c>, event <c>returned</c>, assignee notified.
    /// Returns the refusal text or <c>null</c>. SaveChanges — caller.
    /// </summary>
    public static async Task<string?> ReturnAsync(
        IAppDbContext db, WorkTask t, string actorId, string? reason, TelegramService? telegram = null,
        CancellationToken ct = default)
    {
        var error = WorkTaskApproval.ReturnError(t, await InDoneColumnAsync(db, t, ct), reason);
        if (error is not null) return error;

        var first = await db.WorkTaskColumns.Where(c => c.BoardId == t.BoardId && !c.IsDone)
            .OrderBy(c => c.Order).FirstOrDefaultAsync(ct);
        if (first is null) return WorkTaskApproval.NoOpenColumnMessage;

        var why = reason!.Trim();
        var siblings = await db.WorkTasks
            .Where(x => x.ColumnId == first.Id && x.Id != t.Id && !x.IsArchived)
            .OrderBy(x => x.Order).ToListAsync(ct);
        siblings.Insert(0, t);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Order = i;

        t.ColumnId = first.Id;
        t.CompletedAt = null;
        t.CompletedById = "";
        OnReopened(t);
        t.ReturnCount++;
        t.LastReturnedAt = AppClock.Now;
        t.UpdatedAt = AppClock.Now;
        db.WorkTaskEvents.Add(new WorkTaskEvent
        {
            TaskId = t.Id, ActorId = actorId, Kind = EventReturned, Text = $"Qaytarildi: {why}",
        });
        var text = WorkTaskTelegram.ReturnedText(t, why);
        NotificationStore.Add(db, t.AssigneeId, "Topshiriq qaytarildi", text, BellType);
        await SendToUserAsync(db, telegram, t.AssigneeId, text, null, ct);
        return null;
    }

    /// <summary>
    /// The account behind a Telegram chat that may approve <paramref name="t"/>: the creator's own chat, or an
    /// admin/superadmin chat. <c>null</c> — nobody (the bot answers «Bu amal sizga ruxsat etilmagan»).
    /// </summary>
    public static async Task<string?> ChatApproverIdAsync(IAppDbContext db, long chatId, WorkTask t, CancellationToken ct = default)
    {
        var userIds = await db.TelegramRegistrations
            .Where(r => r.ChatId == chatId && r.UserId != null && r.UserId != "")
            .Select(r => r.UserId!).Distinct().ToListAsync(ct);
        if (userIds.Count == 0) return null;
        if (userIds.Contains(t.CreatedById)) return t.CreatedById;
        return await db.Users
            .Where(u => userIds.Contains(u.Id) && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
            .Select(u => u.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Bot «✅ Tasdiqlash» (<c>wtok:{taskId}</c>): gate by chat, approve under <see cref="LockKey"/>, save and
    /// answer in the chat. Complete action (saves itself).
    /// </summary>
    public static async Task ApproveFromChatAsync(
        IAppDbContext db, TelegramService telegram, long chatId, string taskId, CancellationToken ct = default)
    {
        using var gate = await PaymentIdempotency.LockAsync(LockKey(taskId));
        var t = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct);
        if (t is null) return;

        var approver = await ChatApproverIdAsync(db, chatId, t, ct);
        if (approver is null)
        {
            await telegram.SendMessageAsync(chatId, WorkTaskApproval.ChatForbiddenMessage, null, ct);
            return;
        }

        var error = await ApproveAsync(db, t, approver, telegram, ct);
        if (error is not null)
        {
            await telegram.SendMessageAsync(chatId, $"ℹ️ «{t.Title}»: {error}", null, ct);
            return;
        }
        await db.SaveChangesAsync(ct);
        await telegram.SendMessageAsync(chatId, $"👍 Tasdiqlandi: «{t.Title}»", null, ct);
    }

    /// <summary>Staff operational Telegram message (direct send — not the parent outbox, SPEC §0.4).</summary>
    private static async Task SendToUserAsync(
        IAppDbContext db, TelegramService? telegram, string? userId, string text, object? keyboard, CancellationToken ct)
    {
        if (telegram is null || string.IsNullOrWhiteSpace(userId)) return;
        var chats = await db.TelegramRegistrations.Where(r => r.UserId == userId)
            .Select(r => r.ChatId).Distinct().ToListAsync(ct);
        foreach (var chatId in chats)
            await telegram.SendMessageAsync(chatId, text, keyboard, ct);
    }
}
