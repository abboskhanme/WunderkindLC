using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// "Topshiriqlar" moduli KUNLIK ESLATMASI. Har kuni sozlangan soatdan (CenterMeta.WorkTaskReminder*)
/// keyin har bir mas'ulga — Telegram bot orqali ro'yxatdan o'tgan bo'lsa — BUGUN muddati keladigan
/// va MUDDATI O'TGAN ochiq topshiriqlarini "✅ Bajardim" tugmalari bilan yuboradi.
///
/// <para>Idempotent: har topshiriqda <see cref="WorkTask.ReminderSentDate"/> turadi, ya'ni bir kunda
/// bir marta eslatiladi va xizmat aynan o'sha daqiqada ishlamagan bo'lsa ham keyinroq o'zini
/// tiklaydi.</para>
/// </summary>
public class WorkTaskReminderService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<WorkTaskReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Topshiriq eslatmasi siklida xatolik"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = AppClock.Now;                        // Toshkent vaqti
        var today = now.ToString("yyyy-MM-dd");

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null || !meta.WorkTaskReminderEnabled) return;

        var target = Math.Clamp(meta.WorkTaskReminderHour, 0, 23) * 60
                     + Math.Clamp(meta.WorkTaskReminderMinute, 0, 59);
        if (now.Hour * 60 + now.Minute < target) return;   // hali eslatish vaqti kelmagan

        // Yopiluvchi ustunlar — bajarilgan topshiriq haqida eslatilmaydi.
        var doneCols = (await db.WorkTaskColumns.Where(c => c.IsDone).Select(c => c.Id).ToListAsync(ct))
            .ToHashSet();

        var due = await db.WorkTasks
            .Where(t => !t.IsArchived && t.AssigneeId != "" && t.DueDate != null
                        && t.DueDate.CompareTo(today) <= 0
                        && (t.ReminderSentDate == null || t.ReminderSentDate != today))
            .ToListAsync(ct);
        due = due.Where(t => !doneCols.Contains(t.ColumnId)).ToList();
        if (due.Count == 0) return;

        var chatsByUser = (await db.TelegramRegistrations
                .Where(r => r.UserId != null && r.UserId != "").ToListAsync(ct))
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ChatId).Distinct().ToList());

        foreach (var grp in due.GroupBy(t => t.AssigneeId))
        {
            var items = grp.OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority).ToList();

            // Bog'lanmagan xodim — xabar ketmaydi, lekin belgilab qo'yamiz: aks holda har
            // daqiqada qayta urinilardi (va bog'langan kunida bir yillik "qarz" yog'ilardi).
            if (chatsByUser.TryGetValue(grp.Key, out var chats) && chats.Count > 0)
            {
                var text = WorkTaskTelegram.ReminderText(items, today);
                var keyboard = WorkTaskTelegram.ReminderKeyboard(items.Take(10));
                foreach (var chatId in chats)
                    await telegram.SendMessageAsync(chatId, text, keyboard, ct);
                logger.LogInformation("Topshiriq eslatmasi yuborildi: {UserId} — {Count} ta.",
                    grp.Key, items.Count);
            }

            foreach (var t in items) t.ReminderSentDate = today;
        }

        await db.SaveChangesAsync(ct);
    }
}
