using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// LC: a new task reaches the employee's Telegram with EVERYTHING (egasining talabi, 2026-10-27): title, description,
/// due, who gave it, the photos / files / voice note attached to it — and «✅ Qabul qildim» (<see cref="HrTaskAck"/>).
/// LMS sends only the title and due; the rest of the HR texts stay as ported.
/// </summary>
public sealed partial class HrTasksService
{
    /// <summary>A batch up to this size → a FULL message per task; a larger one → the old single summary.</summary>
    public const int MaxFullTaskMessages = 3;

    /// <summary>Never throws (a Telegram failure never fails the request). Returns messages delivered.</summary>
    private async Task<int> NotifyAssignedAsync(string userId, HrTask task, TimeZoneInfo tz, string? giverName, CancellationToken ct)
    {
        if (!telegram.IsConfigured || string.IsNullOrEmpty(userId)) return 0;
        try
        {
            var chats = await ChatsAsync([userId], ct);
            if (!chats.TryGetValue(userId, out var chatIds)) return 0;
            var role = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
            giverName ??= string.IsNullOrEmpty(task.CreatedBy) ? "Tizim"
                : await db.Users.AsNoTracking().Where(u => u.Id == task.CreatedBy).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Rahbar";
            var attachments = await db.HrTaskAttachments.AsNoTracking().Where(a => a.TaskId == task.Id)
                .OrderBy(a => a.CreatedAt).ToListAsync(ct);
            var html = HrTasksTexts.AssignedFull(task.Title, task.Description, task.DueAt, tz, giverName, attachments.Count);

            // read each file ONCE, send to every chat of the user
            var blobs = new List<(HrTaskAttachment Meta, byte[] Bytes)>();
            foreach (var a in attachments)
            {
                try
                {
                    if (await files.ReadAsync(a.FilePath, ct) is { } bytes) blobs.Add((a, bytes));
                }
                catch (IOException ex) { logger.LogWarning(ex, "HR: biriktirma fayli o'qilmadi {Path}", a.FilePath); }
            }

            var sent = 0;
            foreach (var chat in chatIds)
            {
                if (await telegram.SendTaskAsync(chat, html, MyTasksUrl(role), task.Id, ct)) sent++;
                foreach (var (meta, bytes) in blobs)
                {
                    var name = string.IsNullOrWhiteSpace(meta.OriginalName) ? Path.GetFileName(meta.FilePath) : meta.OriginalName!;
                    if (meta.Kind == HrAttachmentKinds.Voice)
                        await telegram.SendVoiceAsync(chat, bytes, Path.GetFileName(meta.FilePath), meta.ContentType, HrTasksTexts.VoiceCaption, ct);
                    else if (IsPhoto(meta.ContentType))
                        await telegram.SendPhotoAsync(chat, bytes, name, "🖼 " + task.Title, ct);
                    else
                        await telegram.SendDocumentAsync(chat, bytes, name, "📎 " + task.Title, ct);
                }
            }
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "HR: topshiriq Telegram xabari yuborilmadi (user {UserId})", userId);
            return 0;
        }
    }

    /// <summary>Telegram shows these as a photo; anything else (heic, svg...) goes as a file.</summary>
    internal static bool IsPhoto(string? contentType) =>
        contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif";
}
