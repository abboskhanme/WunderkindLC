using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Topshiriq HOLATI bilan bog'liq, ikki joydan (admin paneli va Telegram bot) chaqiriladigan
/// mantiq — YAGONA nusxa. Aks holda botda yopilgan takroriy topshiriq keyingi nusxasini
/// tug'dirmasdan qolib ketardi (panelda esa tug'dirardi).
/// </summary>
public static class WorkTaskFlow
{
    /// <summary>
    /// TAKRORIY topshiriq yopilganda keyingi nusxasini tug'diradi: muddat kun/hafta/oyga suriladi,
    /// qadamlar belgisiz nusxalanadi, zanjir <see cref="WorkTask.RepeatOfId"/> orqali saqlanadi.
    /// Takroriy bo'lmasa hech narsa qilmaydi. <b>SaveChanges CHAQIRUVCHIDA.</b>
    /// </summary>
    public static async Task SpawnRepeatAsync(IAppDbContext db, WorkTask t, CancellationToken ct = default)
    {
        if (t.Repeat is not ("daily" or "weekly" or "monthly")) return;

        // ⚠️ SANA-ONLY: AppClock.Today — DateOnly, shuning uchun parse ham DateOnly bo'lishi shart
        // (DateTime bilan aralashsa ternar tur aniqlanmaydi va vaqt qismi keraksiz sudralardi).
        var baseDay = DateOnly.TryParseExact(t.DueDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : AppClock.Today;
        var next = t.Repeat switch
        {
            "daily" => baseDay.AddDays(1),
            "weekly" => baseDay.AddDays(7),
            _ => baseDay.AddMonths(1),
        };

        var first = await db.WorkTaskColumns.Where(c => c.BoardId == t.BoardId && !c.IsDone)
            .OrderBy(c => c.Order).FirstOrDefaultAsync(ct);
        if (first is null) return;

        var copy = new WorkTask
        {
            BoardId = t.BoardId,
            ColumnId = first.Id,
            Title = t.Title,
            Description = t.Description,
            AssigneeId = t.AssigneeId,
            CreatedById = t.CreatedById,
            Priority = t.Priority,
            DueDate = next.ToString("yyyy-MM-dd"),
            DueTime = t.DueTime,
            Order = -1,                       // yangi nusxa ustun BOSHIDA
            Tags = [.. t.Tags],
            Repeat = t.Repeat,
            RepeatOfId = string.IsNullOrWhiteSpace(t.RepeatOfId) ? t.Id : t.RepeatOfId,
        };
        db.WorkTasks.Add(copy);

        var steps = await db.WorkTaskItems.Where(i => i.TaskId == t.Id).OrderBy(i => i.Order).ToListAsync(ct);
        db.WorkTaskItems.AddRange(steps.Select(s => new WorkTaskItem
        {
            TaskId = copy.Id, Title = s.Title, Order = s.Order,
        }));

        db.WorkTaskEvents.Add(new WorkTaskEvent
        {
            TaskId = copy.Id, ActorId = t.AssigneeId, Kind = "created",
            Text = $"Takroriy topshiriq: {WorkTaskTelegram.Day(copy.DueDate)}",
        });
    }
}
