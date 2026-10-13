using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// After <see cref="TelegramMiniAppLink.LinkAsync"/>: audit + save + the bot's confirmation + «01» — ONE place for both
/// link paths (Mini App initData → <c>TelegramLinkController</c>; keyboard-button <c>sendData</c> → the bot).
/// </summary>
public static class TelegramLinkFlow
{
    /// <param name="replyWhenNothingNew">The keyboard path closes the Mini App, so the chat always gets an answer.</param>
    public static async Task CompleteAsync(
        IAppDbContext db, AuditService audit, TelegramService telegram, AutoMessageService? autoMsg, ILogger logger,
        TelegramMiniAppLink.Outcome o, long chatId, bool replyWhenNothingNew, CancellationToken ct)
    {
        // One audit row per NEW link (never the chat id).
        foreach (var reg in o.NewStudentLinks)
        {
            var s = o.Students.First(x => x.Id == reg.StudentId);
            audit.Record(AuditService.EntityStudent, s.Id, "update",
                $"Telegram ulandi (Panel orqali kirishda): {s.FullName}", studentId: s.Id);
        }
        foreach (var t in o.NewTeachers)
            audit.Record("TeacherSalary", t.Id, "update",
                $"Telegram ulandi (Panel orqali kirishda): {t.FullName}", teacherId: t.Id);
        if (o.NewStaffLink && o.Staff is not null)
            audit.Record("Staff", o.Staff.Id, "update", $"Telegram ulandi (Panel orqali kirishda): {o.Staff.FullName}");

        await db.SaveChangesAsync(ct);

        if (o.AnyNew || replyWhenNothingNew)
        {
            // Confirmation + refreshed keyboard; a failure must not fail the link.
            try
            {
                var books = (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.BookSalesEnabled ?? true;
                var text = o.AnyNew ? TelegramMiniAppLink.ConfirmationText(o)
                    : o.Students.Count + o.Teachers.Count > 0 || o.Staff is not null
                        ? "✅ Bu chat allaqachon ulangan — markaz xabarlari shu yerga keladi."
                        : "ℹ️ Bu akkauntda botga ulanadigan o'quvchi yoki xodim topilmadi.";
                await telegram.SendMessageAsync(chatId, text, TelegramBotService.RegisteredKeyboard(books, panel: true), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Telegram ulanish tasdig'i yuborilmadi");
            }
        }

        // «01» thank-you (outbox, deduped) — same as the old phone registration.
        if (autoMsg is not null)
            foreach (var reg in o.NewStudentLinks)
                await autoMsg.DispatchBotConnectedAsync(db, o.Students.First(x => x.Id == reg.StudentId), reg, ct);
    }
}
