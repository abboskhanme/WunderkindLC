using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// "Topshiriqlar" controllerining YORDAMCHI qismi — seed, ko'chirish mantig'i, DTO ga o'girish,
/// harakatlar tarixi va Telegram bildirishnomasi. Marshrutlar asosiy faylda
/// (<c>WorkTasksController.cs</c>) qoladi, bu yerda faqat ichki mantiq.
/// </summary>
public partial class WorkTasksController
{
    // ---------- Seed va standart ustunlar ----------

    /// <summary>Birorta doska bo'lmasa — "Umumiy" doskasini standart ustunlar bilan yaratadi.
    /// Idempotent: bir marta ishlaydi, keyin darrov qaytadi.</summary>
    private async Task EnsureSeedAsync()
    {
        if (await db.WorkTaskBoards.AnyAsync()) return;
        var board = new WorkTaskBoard { Title = "Umumiy", Color = "violet", Order = 0, CreatedById = Uid };
        db.WorkTaskBoards.Add(board);
        db.WorkTaskColumns.AddRange(DefaultColumns(board.Id));
        await db.SaveChangesAsync();
    }

    /// <summary>Yangi doskaning standart ustunlari. Birinchisi va yopiluvchisi TIZIM ustuni —
    /// o'chirilmaydi (topshiriqning "uyi" va "bajarildi" nuqtasi doim qolsin).</summary>
    private static List<WorkTaskColumn> DefaultColumns(string boardId) =>
    [
        new() { BoardId = boardId, Title = "Rejada",     Color = "slate",   Order = 0, IsSystem = true },
        new() { BoardId = boardId, Title = "Jarayonda",  Color = "blue",    Order = 1 },
        new() { BoardId = boardId, Title = "Tekshiruvda", Color = "amber",  Order = 2 },
        new() { BoardId = boardId, Title = "Bajarildi",  Color = "emerald", Order = 3, IsDone = true, IsSystem = true },
    ];

    // ---------- Kichik yordamchilar ----------

    private static readonly string[] AllowedColors =
        ["slate", "blue", "emerald", "amber", "violet", "rose", "cyan", "orange"];

    /// <summary>Rang kaliti ro'yxatdagilardan bo'lishi shart (klientdagi `stageColors` bilan bir xil).</summary>
    private static string Color(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && AllowedColors.Contains(value) ? value : fallback;

    /// <summary>Takroriylik kaliti: none | daily | weekly | monthly.</summary>
    private static string Repeat(string? value) =>
        value is "daily" or "weekly" or "monthly" ? value : "none";

    /// <summary>"yyyy-MM-dd" ni tekshirib qaytaradi (noto'g'ri/bo'sh — null).</summary>
    private static string? Day(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _) ? s : null;
    }

    private static string? Iso(DateTime? dt) => dt?.ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>Harakatlar tarixiga yozuv (SaveChanges CHAQIRUVCHIDA).</summary>
    private void Log(string taskId, string kind, string text) =>
        db.WorkTaskEvents.Add(new WorkTaskEvent { TaskId = taskId, ActorId = Uid, Kind = kind, Text = text });

    /// <summary>Foydalanuvchi id → F.I.Sh. (topilmaganlar chiqmaydi).</summary>
    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> ids)
    {
        var list = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Users.Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
    }

    /// <summary>Berilgan doskaning ustuni (kalit bo'sh bo'lsa — BIRINCHI ustun).</summary>
    private async Task<WorkTaskColumn?> ResolveColumnAsync(string boardId, string? columnId)
    {
        if (!string.IsNullOrWhiteSpace(columnId))
            return await db.WorkTaskColumns.FirstOrDefaultAsync(c => c.Id == columnId && c.BoardId == boardId);
        return await db.WorkTaskColumns.Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Order).FirstOrDefaultAsync();
    }

    // ---------- Ko'chirish (drag & drop) ----------

    /// <summary>
    /// Topshiriqni ustunga ko'chiradi va ustun ichidagi tartibni qayta hisoblaydi.
    /// Yopiluvchi ustunga tushsa — "bajarildi" (+ takroriy bo'lsa keyingi nusxa tug'iladi);
    /// undan CHIQSA — qayta ochiladi. SaveChanges CHAQIRUVCHIDA.
    /// </summary>
    private async Task<bool> ApplyMoveAsync(WorkTask t, string columnId, int order)
    {
        var col = await db.WorkTaskColumns.FirstOrDefaultAsync(c => c.Id == columnId && c.BoardId == t.BoardId);
        if (col is null) return false;

        if (col.Id != t.ColumnId)
        {
            var from = await db.WorkTaskColumns.FirstOrDefaultAsync(c => c.Id == t.ColumnId);
            Log(t.Id, "moved", $"«{from?.Title ?? "—"}» → «{col.Title}»");
        }
        t.ColumnId = col.Id;
        t.UpdatedAt = AppClock.Now;

        // Ustun ichidagi tartib: qo'shnilarni olib, kerakli o'ringa qo'yamiz va 0..N qilib
        // qayta raqamlaymiz (kasr/oraliq raqamlar bilan ovora bo'lmaymiz — ustunlar kichik).
        var siblings = await db.WorkTasks
            .Where(x => x.ColumnId == col.Id && x.Id != t.Id && !x.IsArchived)
            .OrderBy(x => x.Order).ToListAsync();
        var idx = Math.Clamp(order, 0, siblings.Count);
        siblings.Insert(idx, t);
        for (var i = 0; i < siblings.Count; i++) siblings[i].Order = i;

        var wasDone = t.CompletedAt is not null;
        if (col.IsDone && !wasDone)
        {
            t.CompletedAt = AppClock.Now;
            t.CompletedById = Uid;
            Log(t.Id, "moved", "Bajarildi deb belgilandi");
            await SpawnRepeatAsync(t);
        }
        else if (!col.IsDone && wasDone)
        {
            t.CompletedAt = null;
            t.CompletedById = "";
            Log(t.Id, "reopened", "Qayta ochildi");
        }
        return true;
    }

    /// <summary>Takroriy topshiriq yopilganda keyingi nusxasini tug'diradi. Mantiq
    /// <see cref="WorkTaskFlow"/> da — Telegram bot ham AYNAN shundan foydalanadi.</summary>
    private Task SpawnRepeatAsync(WorkTask t) => WorkTaskFlow.SpawnRepeatAsync(db, t);

    // ---------- DTO ga o'girish ----------

    /// <summary>Topshiriqlarni kartochka DTO'siga o'giradi: mas'ul/muallif nomlari, qadamlar
    /// ulushi, izohlar soni va "bajarildi/kechikdi" holati (ustunning <c>IsDone</c> bayrog'idan).</summary>
    private async Task<List<WorkTaskDto>> MapAsync(IReadOnlyList<WorkTask> tasks)
    {
        if (tasks.Count == 0) return [];

        var ids = tasks.Select(t => t.Id).ToList();
        var colIds = tasks.Select(t => t.ColumnId).Distinct().ToList();
        var doneCols = (await db.WorkTaskColumns.Where(c => colIds.Contains(c.Id) && c.IsDone)
            .Select(c => c.Id).ToListAsync()).ToHashSet();

        var steps = await db.WorkTaskItems.Where(i => ids.Contains(i.TaskId))
            .Select(i => new { i.TaskId, i.Done }).ToListAsync();
        var stepsTotal = steps.GroupBy(x => x.TaskId).ToDictionary(g => g.Key, g => g.Count());
        var stepsDone = steps.GroupBy(x => x.TaskId).ToDictionary(g => g.Key, g => g.Count(x => x.Done));

        var comments = (await db.WorkTaskComments.Where(c => ids.Contains(c.TaskId))
                .Select(c => c.TaskId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var userIds = tasks.SelectMany(t => new[] { t.AssigneeId, t.CreatedById })
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl }).ToListAsync();
        var names = users.ToDictionary(u => u.Id, u => u.FullName);
        var avatars = users.ToDictionary(u => u.Id, u => u.AvatarUrl);

        var today = Today;
        return tasks.Select(t =>
        {
            var isDone = doneCols.Contains(t.ColumnId);
            var overdue = !isDone && !string.IsNullOrWhiteSpace(t.DueDate)
                                  && string.CompareOrdinal(t.DueDate, today) < 0;
            return new WorkTaskDto(
                t.Id, t.BoardId, t.ColumnId, t.Title, t.Description,
                t.AssigneeId, names.GetValueOrDefault(t.AssigneeId, ""), avatars.GetValueOrDefault(t.AssigneeId),
                t.CreatedById, names.GetValueOrDefault(t.CreatedById, ""),
                t.Priority, t.DueDate, t.DueTime, t.Order, t.Tags,
                isDone, overdue,
                stepsTotal.GetValueOrDefault(t.Id, 0), stepsDone.GetValueOrDefault(t.Id, 0),
                comments.GetValueOrDefault(t.Id, 0),
                Iso(t.CreatedAt)!, Iso(t.UpdatedAt)!, Iso(t.CompletedAt), t.Repeat, t.IsArchived);
        }).ToList();
    }

    // ---------- Telegram ----------

    /// <summary>Mas'ulga "sizga yangi topshiriq" xabari (bot orqali ro'yxatdan o'tgan bo'lsa).
    /// Xabar yuborilmasligi topshiriq yaratilishini BUZMAYDI — u shunchaki botsiz qoladi.</summary>
    private async Task NotifyAssignedAsync(WorkTask t, string boardTitle)
    {
        if (string.IsNullOrWhiteSpace(t.AssigneeId)) return;

        var chats = await db.TelegramRegistrations
            .Where(r => r.UserId == t.AssigneeId).Select(r => r.ChatId).Distinct().ToListAsync();
        if (chats.Count == 0) return;

        var author = await db.Users.Where(u => u.Id == t.CreatedById)
            .Select(u => u.FullName).FirstOrDefaultAsync() ?? "";
        var text = WorkTaskTelegram.NewTaskText(t, boardTitle, author, Today);
        foreach (var chatId in chats)
            await telegram.SendMessageAsync(chatId, text, WorkTaskTelegram.DoneKeyboard(t.Id));
    }
}
