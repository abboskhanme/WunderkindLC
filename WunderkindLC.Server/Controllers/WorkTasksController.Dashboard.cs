using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// "Topshiriqlar" controllerining NAZORAT qismi — mas'ullar ro'yxati, xodimlar kesimidagi
/// statistika (nazorat paneli) va kunlik eslatma sozlamalari.
/// </summary>
public partial class WorkTasksController
{
    /// <summary>Topshiriq biriktirish mumkin bo'lgan xodimlar (superadmin/admin/staff) — ochiq va
    /// kechikkan topshiriqlari soni bilan (mas'ul tanlashda kim band ekani ko'rinib tursin).</summary>
    [HttpGet("assignees")]
    public async Task<ActionResult<IEnumerable<WorkTaskAssigneeDto>>> Assignees()
    {
        var users = await db.Users
            .Where(u => u.Role == Roles.Admin || u.Role == Roles.Staff || u.Role == Roles.SuperAdmin)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Role, u.Position, u.AvatarUrl })
            .ToListAsync();

        var doneCols = (await db.WorkTaskColumns.Where(c => c.IsDone).Select(c => c.Id).ToListAsync())
            .ToHashSet();
        var open = await db.WorkTasks.Where(t => !t.IsArchived && t.AssigneeId != "")
            .Select(t => new { t.AssigneeId, t.ColumnId, t.DueDate }).ToListAsync();

        var linked = (await db.TelegramRegistrations.Where(r => r.UserId != null && r.UserId != "")
                .Select(r => r.UserId!).ToListAsync())
            .ToHashSet();

        var today = Today;
        var openBy = open.Where(x => !doneCols.Contains(x.ColumnId))
            .GroupBy(x => x.AssigneeId)
            .ToDictionary(g => g.Key, g => (
                Open: g.Count(),
                Overdue: g.Count(x => !string.IsNullOrWhiteSpace(x.DueDate)
                                      && string.CompareOrdinal(x.DueDate, today) < 0)));

        return users.Select(u =>
        {
            var c = openBy.GetValueOrDefault(u.Id);
            return new WorkTaskAssigneeDto(u.Id, u.FullName, u.Role, u.Position, u.AvatarUrl,
                linked.Contains(u.Id), c.Open, c.Overdue);
        }).ToList();
    }

    /// <summary>
    /// NAZORAT PANELI — umumiy raqamlar, har bir xodim kesimi, kunlik dinamika va "e'tibor
    /// talab qiladi" ro'yxati. <paramref name="from"/>/<paramref name="to"/> berilmasa oxirgi 30 kun.
    ///
    /// <para>Umumiy raqamlar (jami/ochiq/kechikkan) — HOZIRGI holat bo'yicha; dinamika esa
    /// oraliqdagi kunlar bo'yicha (yaratilgan va bajarilgan).</para>
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<WorkTaskDashboardDto>> Dashboard(
        [FromQuery] string? boardId, [FromQuery] string? from, [FromQuery] string? to)
    {
        await EnsureSeedAsync();

        var today = AppClock.Today;
        var toDay = ParseDay(to) ?? today;
        var fromDay = ParseDay(from) ?? toDay.AddDays(-29);
        if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);
        var todayIso = Today;

        var q = db.WorkTasks.Where(t => !t.IsArchived);
        if (!string.IsNullOrWhiteSpace(boardId)) q = q.Where(t => t.BoardId == boardId);
        var tasks = await q.ToListAsync();

        var doneCols = (await db.WorkTaskColumns.Where(c => c.IsDone).Select(c => c.Id).ToListAsync())
            .ToHashSet();

        bool IsDone(WorkTask t) => doneCols.Contains(t.ColumnId);
        bool IsOverdue(WorkTask t) => !IsDone(t) && !string.IsNullOrWhiteSpace(t.DueDate)
                                      && string.CompareOrdinal(t.DueDate, todayIso) < 0;
        // Muddatida yopildimi: bajarilgan kun muddatdan keyin bo'lmasa (muddatsiz — muddatida).
        bool OnTime(WorkTask t) => string.IsNullOrWhiteSpace(t.DueDate) || t.CompletedAt is null
                                   || string.CompareOrdinal(t.CompletedAt.Value.ToString("yyyy-MM-dd"), t.DueDate!) <= 0;

        var total = tasks.Count;
        var done = tasks.Count(IsDone);
        var overdue = tasks.Count(IsOverdue);
        var dueToday = tasks.Count(t => !IsDone(t) && t.DueDate == todayIso);

        var names = await db.Users
            .Where(u => u.Role == Roles.Admin || u.Role == Roles.Staff || u.Role == Roles.SuperAdmin)
            .Select(u => new { u.Id, u.FullName, u.Position }).ToListAsync();

        var rows = names.Select(u =>
        {
            var mine = tasks.Where(t => t.AssigneeId == u.Id).ToList();
            var myDone = mine.Where(IsDone).ToList();
            return new WorkTaskStatRowDto(
                u.Id, u.FullName, u.Position,
                mine.Count, myDone.Count, mine.Count - myDone.Count,
                mine.Count(IsOverdue),
                mine.Count(t => !IsDone(t) && t.DueDate == todayIso),
                myDone.Count(OnTime), myDone.Count(t => !OnTime(t)),
                mine.Count == 0 ? 0 : (int)Math.Round(myDone.Count * 100.0 / mine.Count));
        })
            .Where(r => r.Total > 0)
            .OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Open).ThenBy(r => r.FullName)
            .ToList();

        var trend = new List<WorkTaskTrendPointDto>();
        for (var d = fromDay; d <= toDay; d = d.AddDays(1))
        {
            var day = d;
            trend.Add(new WorkTaskTrendPointDto(
                day.ToString("yyyy-MM-dd"),
                tasks.Count(t => DateOnly.FromDateTime(t.CreatedAt) == day),
                tasks.Count(t => t.CompletedAt is not null
                                 && DateOnly.FromDateTime(t.CompletedAt.Value) == day)));
        }

        // "E'tibor talab qiladi" — avval kechikkanlar, keyin muhimligi yuqori ochiqlar.
        var attention = tasks.Where(t => !IsDone(t))
            .OrderByDescending(IsOverdue)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate ?? "9999-99-99")
            .Take(12).ToList();

        return new WorkTaskDashboardDto(
            total, done, total - done, overdue, dueToday,
            total == 0 ? 0 : (int)Math.Round(done * 100.0 / total),
            rows, trend, await MapAsync(attention));
    }

    // ---------- Kunlik eslatma sozlamalari ----------

    [HttpGet("settings")]
    public async Task<ActionResult<WorkTaskSettingsDto>> GetSettings()
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        return new WorkTaskSettingsDto(
            meta?.WorkTaskReminderEnabled ?? true,
            meta?.WorkTaskReminderHour ?? 9,
            meta?.WorkTaskReminderMinute ?? 30);
    }

    [HttpPut("settings")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> SetSettings(WorkTaskSettingsDto req)
    {
        var meta = await db.CenterMeta.FirstOrDefaultAsync();
        if (meta is null) return NotFound(new { message = "Markaz sozlamalari topilmadi" });
        meta.WorkTaskReminderEnabled = req.Enabled;
        meta.WorkTaskReminderHour = Math.Clamp(req.Hour, 0, 23);
        meta.WorkTaskReminderMinute = Math.Clamp(req.Minute, 0, 59);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>"yyyy-MM-dd" → sana (noto'g'ri/bo'sh — null).
    /// ⚠️ <see cref="AppClock.Today"/> — <c>DateOnly</c>, shuning uchun bu yerda ham
    /// <c>DateOnly</c> (aralashsa <c>??</c> va solishtiruvlar kompilyatsiya bo'lmaydi).</summary>
    private static DateOnly? ParseDay(string? value) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
}
