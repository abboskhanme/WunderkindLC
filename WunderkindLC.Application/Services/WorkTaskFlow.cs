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
    /// <remarks>
    /// ⚠️ IDEMPOTENT: zanjirda shu keyingi muddatli nusxa ALLAQACHON bo'lsa (bazada yoki shu
    /// kontekstda qo'shilgan) — yangisi tug'ilmaydi. Ilgari "yopish → qayta ochish → yopish",
    /// "Bajardim" tugmasini ikki marta bosish yoki ikkinchi "bajarildi" ustuniga ko'chirish har safar
    /// YANA bitta nusxa tug'dirardi. Poyga (ikki so'rov bir vaqtda) uchun chaqiruvchilar topshiriqni
    /// <see cref="LockKey"/> qulfi ostida o'qiydi va saqlaydi.
    /// </remarks>
    /// <returns>Yangi nusxa tug'ildimi.</returns>
    public static async Task<bool> SpawnRepeatAsync(IAppDbContext db, WorkTask t, CancellationToken ct = default)
    {
        if (NextDueDate(t.DueDate, t.Repeat, AppClock.Today) is not { } nextDay) return false;
        var next = nextDay.ToString("yyyy-MM-dd");
        var root = string.IsNullOrWhiteSpace(t.RepeatOfId) ? t.Id : t.RepeatOfId!;

        // Zanjirda (ildiz yoki uning nusxalari) shu muddatli boshqa topshiriq bormi.
        bool SameSlot(WorkTask x) => x.Id != t.Id && (x.Id == root || x.RepeatOfId == root) && x.DueDate == next;
        if (db.WorkTasks.Local.Any(SameSlot)
            || await db.WorkTasks.AnyAsync(x => x.Id != t.Id && (x.Id == root || x.RepeatOfId == root)
                                                && x.DueDate == next, ct))
            return false;

        var first = await db.WorkTaskColumns.Where(c => c.BoardId == t.BoardId && !c.IsDone)
            .OrderBy(c => c.Order).FirstOrDefaultAsync(ct);
        if (first is null) return false;

        var copy = new WorkTask
        {
            BoardId = t.BoardId,
            ColumnId = first.Id,
            Title = t.Title,
            Description = t.Description,
            AssigneeId = t.AssigneeId,
            CreatedById = t.CreatedById,
            Priority = t.Priority,
            DueDate = next,
            DueTime = t.DueTime,
            Order = -1,                       // yangi nusxa ustun BOSHIDA
            Tags = [.. t.Tags],
            Repeat = t.Repeat,
            RepeatOfId = root,
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
        return true;
    }

    /// <summary>Takroriy topshiriqning keyingi muddati (sof). Takroriy bo'lmasa — null.
    /// Muddat bo'sh/buzuq bo'lsa <paramref name="today"/> dan hisoblanadi.</summary>
    public static DateOnly? NextDueDate(string? dueDate, string? repeat, DateOnly today)
    {
        if (repeat is not ("daily" or "weekly" or "monthly")) return null;
        // ⚠️ SANA-ONLY: AppClock.Today — DateOnly, shuning uchun parse ham DateOnly bo'lishi shart.
        var baseDay = DateOnly.TryParseExact(dueDate ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : today;
        return repeat switch
        {
            "daily" => baseDay.AddDays(1),
            "weekly" => baseDay.AddDays(7),
            _ => baseDay.AddMonths(1),
        };
    }

    /// <summary>Topshiriq holatini o'zgartiradigan amallar (panelda ko'chirish, botda "Bajardim")
    /// uchun qulf kaliti — <c>PaymentIdempotency.LockAsync</c> bilan ishlatiladi.</summary>
    public static string LockKey(string taskId) => "work-task:" + taskId;

    /// <summary>
    /// Ustunning "yopiluvchi" (<see cref="WorkTaskColumn.IsDone"/>) holatini topshiriqqa MOSLAYDI:
    /// yopiluvchi ustunda turib <c>CompletedAt</c> bo'sh bo'lsa — yopiladi (+ takroriysi tug'iladi),
    /// oddiy ustunda turib yopilgan bo'lsa — qayta ochiladi. <b>SaveChanges CHAQIRUVCHIDA.</b>
    /// </summary>
    /// <remarks>
    /// ⚠️ Ustunning <c>IsDone</c> bayrog'i almashtirilganda va ustun o'chirilib topshiriqlar boshqa
    /// ustunga ko'chganda chaqiriladi. Ilgari bunda <c>CompletedAt</c> o'zgarmay qolardi: statistika
    /// ("muddatida yopildi") va botdagi "allaqachon bajarilgan" tekshiruvi ustun bilan ziddiyatga tushardi.
    /// </remarks>
    /// <returns>Holat o'zgardimi.</returns>
    public static async Task<bool> SyncCompletionAsync(
        IAppDbContext db, WorkTask t, bool columnIsDone, string actorId, CancellationToken ct = default)
    {
        if (columnIsDone && t.CompletedAt is null)
        {
            t.CompletedAt = AppClock.Now;
            t.CompletedById = actorId;
            t.UpdatedAt = AppClock.Now;
            db.WorkTaskEvents.Add(new WorkTaskEvent
            {
                TaskId = t.Id, ActorId = actorId, Kind = "moved",
                Text = "Ustun «bajarildi» deb belgilandi — topshiriq yopildi",
            });
            await SpawnRepeatAsync(db, t, ct);
            return true;
        }
        if (!columnIsDone && t.CompletedAt is not null)
        {
            t.CompletedAt = null;
            t.CompletedById = "";
            t.UpdatedAt = AppClock.Now;
            db.WorkTaskEvents.Add(new WorkTaskEvent
            {
                TaskId = t.Id, ActorId = actorId, Kind = "reopened",
                Text = "Ustun endi «bajarildi» emas — topshiriq qayta ochildi",
            });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Kunlik eslatma yuboriladigan topshiriqlar: ochiq (yopiluvchi ustunda emas), arxivlanmagan,
    /// mas'uli bor, muddati bugun yoki o'tgan, bugun hali eslatilmagan — va ARXIVLANGAN DOSKADA EMAS.
    /// </summary>
    /// <remarks>
    /// ⚠️ Arxivlangan doska ATAYIN chiqariladi: doska arxivlansa uning topshiriqlari hech qayerda
    /// ko'rinmaydi, lekin ilgari har kuni eslatma ketaverardi va nazorat panelida "kechikkan" bo'lib
    /// sanalardi (<c>WorkTasksController.Dashboard</c> ham shu qoida bilan).
    /// </remarks>
    public static async Task<List<WorkTask>> DueForReminderAsync(IAppDbContext db, string today, CancellationToken ct = default)
    {
        var doneCols = (await db.WorkTaskColumns.Where(c => c.IsDone).Select(c => c.Id).ToListAsync(ct))
            .ToHashSet();
        var archivedBoards = (await db.WorkTaskBoards.Where(b => b.IsArchived).Select(b => b.Id).ToListAsync(ct))
            .ToHashSet();

        var due = await db.WorkTasks
            .Where(t => !t.IsArchived && t.AssigneeId != "" && t.DueDate != null
                        && t.DueDate.CompareTo(today) <= 0
                        && (t.ReminderSentDate == null || t.ReminderSentDate != today))
            .ToListAsync(ct);
        return due.Where(t => !doneCols.Contains(t.ColumnId) && !archivedBoards.Contains(t.BoardId)).ToList();
    }
}
