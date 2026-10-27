using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>Result of pressing the task's «✅ Qabul qildim».</summary>
public enum HrAckOutcome { NotFound, Closed, Accepted, AlreadyAccepted }

/// <summary>
/// LC: «✅ Qabul qildim» under a task's Telegram message (callback <c>hrack:{taskId}</c>, egasining talabi 2026-10-27).
/// Pressing it is the SAME as «Ko'rdim» in the panel (<see cref="HrTask.Accept"/>): the task is really accepted —
/// the director sees it in the list. Only a chat linked to the task's OWNER may accept (a staff chat by
/// <c>UserId</c>, a teacher chat by the owner's teacher card); anything else gets the «not found» toast (the same
/// one — a guessed id is never confirmed). After the press the button turns into «✅ Qabul qilindi».
/// </summary>
public static class HrTaskAck
{
    public const string CallbackPrefix = "hrack:";
    public const string ButtonText = "✅ Qabul qildim";
    public const string AcceptedButtonText = "✅ Qabul qilindi · Topshiriqlarim";
    public const string ToastNotFound = "Topshiriq topilmadi yoki sizga tegishli emas";
    public const string ToastClosed = "Bu topshiriq bekor qilingan";
    public const string ToastOk = "✅ Qabul qilindi. Rahmat!";

    /// <summary>Task message keyboard: «Qabul qildim» + (optional) the «📋 Topshiriqlarim» Mini App button.</summary>
    public static object Keyboard(string taskId, string? openUrl)
    {
        var rows = new List<object[]> { new object[] { new { text = ButtonText, callback_data = CallbackPrefix + taskId } } };
        if (!string.IsNullOrWhiteSpace(openUrl))
            rows.Add([new { text = TelegramHrSender.OpenButtonText, web_app = new { url = openUrl } }]);
        return new { inline_keyboard = rows };
    }

    /// <summary>After the press: one Mini App button that also says it was accepted (<c>null</c> url → no keyboard).</summary>
    public static object? AcceptedKeyboard(string? openUrl) => string.IsNullOrWhiteSpace(openUrl)
        ? null
        : new { inline_keyboard = new[] { new object[] { new { text = AcceptedButtonText, web_app = new { url = openUrl } } } } };

    public static async Task<HrAckOutcome> HandleAsync(
        IAppDbContext db, ITelegramSender sender, long chatId, string callbackId, long? messageId, string taskId,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var task = string.IsNullOrWhiteSpace(taskId) ? null : await db.HrTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        var ownerUserId = task is null ? null
            : await db.HrEmployees.AsNoTracking().Where(e => e.Id == task.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        if (task is null || string.IsNullOrEmpty(ownerUserId) || !await ChatOwnedByAsync(db, chatId, ownerUserId, ct))
        {
            await Answer(ToastNotFound);
            return HrAckOutcome.NotFound;
        }
        if (task.IsFinal)
        {
            await Answer(ToastClosed);
            await Unbutton(accepted: false);   // a cancelled task — the button just goes away
            return HrAckOutcome.Closed;
        }

        var isNew = task.Accept(now);
        if (isNew) await db.SaveChangesAsync(ct);
        await Unbutton(accepted: true);
        await Answer(ToastOk);
        return isNew ? HrAckOutcome.Accepted : HrAckOutcome.AlreadyAccepted;

        async Task Unbutton(bool accepted)
        {
            if (messageId is not long mid) return;
            try
            {
                object? markup = null;
                if (accepted)
                {
                    var role = await db.Users.AsNoTracking().Where(u => u.Id == ownerUserId).Select(u => u.Role).FirstOrDefaultAsync(ct);
                    markup = AcceptedKeyboard(HrTasksService.MyTasksUrl(role));
                }
                await sender.EditMessageReplyMarkupAsync(chatId, mid, markup, ct);
            }
            catch (Exception) { /* the acceptance is stored; a button left on screen is harmless */ }
        }

        async Task Answer(string text)
        {
            if (callbackId.Length == 0) return;
            try { await sender.AnswerCallbackAsync(callbackId, text, ct); }
            catch (Exception) { /* toast only */ }
        }
    }

    /// <summary>Is <paramref name="chatId"/> a (not blocked) chat of this account — staff by UserId, teacher by card?</summary>
    private static async Task<bool> ChatOwnedByAsync(IAppDbContext db, long chatId, string userId, CancellationToken ct)
    {
        var teacherIds = await db.Teachers.AsNoTracking().Where(t => t.UserId == userId).Select(t => t.Id).ToListAsync(ct);
        return await db.TelegramRegistrations.AsNoTracking().AnyAsync(r => r.ChatId == chatId && r.BlockedAt == null
            && (r.UserId == userId || (r.TeacherId != null && teacherIds.Contains(r.TeacherId))), ct);
    }
}
