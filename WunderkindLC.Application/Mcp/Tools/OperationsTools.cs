using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Operations: the "Bog'lanish kerak" follow-up queue and its report, work tasks (Kanban), Telegram
/// announcements, automatic message rules and the change history (audit).
///
/// <para>Definitions follow the panel:
/// <list type="bullet">
///   <item>contact queue — <c>ContactsController.List</c>/<c>Meta</c> (.claude/rules/contacts.md §2–3.6):
///     open = new + callback, due buckets come from <see cref="ContactService.BucketOf"/>, a request with an
///     empty/unknown <c>StageId</c> sits in its status' SYSTEM column (stage id = status key);</item>
///   <item>contact report — <see cref="ContactReport.BuildAsync"/>, the same function as <c>GET contacts/stats</c>;
///     superadmin only in the panel (hard role check) → <see cref="McpAreas.ContactReport"/>;</item>
///   <item>tasks — <c>WorkTasksController.List</c>/<c>MapAsync</c> (.claude/rules/topshiriqlar.md §2–3): done =
///     the task's column has <c>IsDone</c>; overdue = not done and due date before today; archived tasks and
///     (without a board filter) tasks on archived boards are hidden like the dashboard does;</item>
///   <item>audit — <c>AuditController.ApplyFilters</c> (.claude/rules/audit.md §2–5): section ↔ entity types via
///     <see cref="AuditSections"/>, unknown types fall into <c>other</c>; the before/after snapshots are never read.</item>
/// </list></para>
/// </summary>
[McpServerToolType]
public sealed class OperationsTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(ContactRequest), typeof(ContactAttempt), typeof(ContactStage), typeof(Student),
        typeof(WorkTask), typeof(WorkTaskBoard), typeof(WorkTaskColumn), typeof(WorkTaskItem), typeof(AppUser),
        typeof(Broadcast), typeof(AutoMessageRule), typeof(AuditLog),
    ];

    /// <summary>Longest announcement text returned (the rest is cut, <c>truncated = true</c>).</summary>
    public const int MaxBroadcastText = 500;

    /// <summary>Longest task description returned.</summary>
    public const int MaxTaskDescription = 300;

    /// <summary>The four audit actions (.claude/rules/audit.md §1).</summary>
    private static readonly string[] AuditActions = ["create", "update", "delete", "complete-and-transfer"];

    /// <summary>Task priorities — the client's PRIORITIES catalog (pages/admin/tasks/model.ts), server keeps 0..3.</summary>
    private static readonly string[] PriorityLabels = ["Past", "O'rta", "Yuqori", "Shoshilinch"];

    private static string PriorityLabel(int p) => PriorityLabels[Math.Clamp(p, 0, PriorityLabels.Length - 1)];

    private static string TodayIso => McpToolContext.Iso(McpToolContext.Today);

    // =====================================================================================
    //  contact_queue
    // =====================================================================================

    [McpServerTool(Name = "contact_queue", Title = "Bog'lanish kerak navbati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Bog'lanish kerak» navbati — o'quvchi (ota-ona) bilan bog'lanish talablari. Holat (sukut — ochiqlar: "
        + "«Bog'lanish kerak» + «Qayta qo'ng'iroq» | done | failed | all), muddat guruhi (todo — bugun qilish kerak: muddati "
        + "o'tgan + bugungi + sanasiz | overdue | today | tomorrow | week | later | nodate), sabab va o'quvchi ismi bo'yicha "
        + "filtrlar. Har qatorda o'quvchi (telefonlari, arxivdami), sabab, holat va taxtadagi ustun, qayta qo'ng'iroq sanasi va "
        + "muddat guruhi, urinishlar soni, oxirgi javob va kim bog'langani. Ochiqlar taxtadagidek (muddati o'tganlar tepada), "
        + "yopilganlar eng yangisi tepada. Ustida navbatning joriy sanoqlari (ochiq, muddati o'tgan, bugun qilish kerak). "
        + "Sahifalangan (ko'pi bilan 200 qator). The follow-up contact queue with status, due-bucket, reason and name filters.")]
    public async Task<string> ContactQueue(
        [Description("open (sukut — new + callback) | new | callback | done | failed | all")] string? status = null,
        [Description("Muddat guruhi: todo | overdue | today | tomorrow | week | later | nodate")] string? due = null,
        [Description("Sabab id yoki sabab nomining bo'lagi")] string? reason = null,
        [Description("O'quvchi ismi yoki sabab nomining bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Contacts);
        var db = t.Db;
        var today = TodayIso;

        var q = db.ContactRequests.AsQueryable();
        var st = (status ?? "open").Trim().ToLowerInvariant();
        if (st == "open")
            q = q.Where(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback);
        else if (st != "all")
        {
            if (!ContactService.IsValidStatus(st))
                throw new McpException("«status»: open | new | callback | done | failed | all.");
            q = q.Where(c => c.Status == st);
        }

        var dueKey = (due ?? "").Trim().ToLowerInvariant();
        if (dueKey.Length > 0)
        {
            if (!ContactService.IsKnownDue(dueKey))
                throw new McpException("«due»: todo | overdue | today | tomorrow | week | later | nodate.");
            // The bucket depends only on (status, due date): take the distinct pairs of OPEN requests,
            // classify them with the panel's own rule and filter by the matching values in SQL.
            var pairs = await db.ContactRequests
                .Where(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback)
                .Select(c => new { c.Status, c.DueDate }).Distinct().ToListAsync(ct);
            var hit = pairs.Where(p => InBucket(ContactService.BucketOf(p.Status, p.DueDate, today), dueKey)).ToList();
            var withNew = hit.Any(p => p.Status == ContactStatuses.New);
            var dates = hit.Where(p => p.Status == ContactStatuses.Callback).Select(p => p.DueDate).Distinct().ToList();
            q = q.Where(c => (withNew && c.Status == ContactStatuses.New)
                             || (c.Status == ContactStatuses.Callback && dates.Contains(c.DueDate)));
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var rid = reason.Trim();
            var rl = rid.ToLower();
            q = q.Where(c => c.ReasonId == rid || c.ReasonLabel.ToLower().Contains(rl));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(c => c.StudentName.ToLower().Contains(term) || c.ReasonLabel.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        // Open first, in the board's order (callbacks by due date — overdue on top — then new, newest first);
        // closed after them, newest closed first (ContactsController.List).
        var rows = await q
            .OrderBy(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback ? 0 : 1)
            .ThenBy(c => c.Status == ContactStatuses.Callback ? 0 : 1)
            .ThenBy(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback ? c.DueDate : "")
            .ThenByDescending(c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback ? "" : c.ClosedAt)
            .ThenByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(c => new
            {
                c.Id, c.StudentId, c.StudentName, c.ReasonId, c.ReasonLabel, c.Note, c.Status, c.StageId, c.DueDate,
                c.AttemptCount, c.LastResponse, c.LastActorName, c.LastActionAt, c.CreatedAt, c.CreatedBy,
                c.ClosedAt, c.ClosedBy,
            }).ToListAsync(ct);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone, s.IsArchived })
            .ToDictionaryAsync(s => s.Id, ct);
        var stages = await db.ContactStages.Select(s => new { s.Id, s.Title })
            .ToDictionaryAsync(s => s.Id, s => s.Title, ct);

        var items = rows.Select(r =>
        {
            var s = students.GetValueOrDefault(r.StudentId);
            var phones = s is null
                ? new List<string>()
                : new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone }
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            var bucket = ContactService.BucketOf(r.Status, r.DueDate, today);
            return new
            {
                r.Id,
                student = new { id = r.StudentId, name = r.StudentName, phones, isArchived = s?.IsArchived ?? false },
                reason = new { id = r.ReasonId, label = r.ReasonLabel },
                r.Note,
                r.Status,
                statusLabel = ContactService.StatusLabel(r.Status),
                stage = stages.GetValueOrDefault(r.StageId) ?? stages.GetValueOrDefault(r.Status) ?? ContactService.StatusLabel(r.Status),
                dueDate = r.DueDate.Length > 0 ? r.DueDate : null,
                dueBucket = bucket.Length > 0 ? bucket : null,
                isOverdue = ContactService.IsOverdue(r.Status, r.DueDate, today),
                r.AttemptCount, r.LastResponse,
                lastActor = r.LastActorName, r.LastActionAt,
                r.CreatedBy, r.CreatedAt,
                closedAt = r.ClosedAt.Length > 0 ? r.ClosedAt : null,
                closedBy = r.ClosedBy.Length > 0 ? r.ClosedBy : null,
            };
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            summary = await QueueSummaryAsync(today, ct),
            items,
        }, items.Count);
    }

    private static bool InBucket(string bucket, string key) =>
        key == ContactService.Due.Todo ? ContactService.IsTodo(bucket) : bucket == key;

    /// <summary>Current queue counters — the same computation as <c>ContactsController.Meta</c>.</summary>
    private async Task<object> QueueSummaryAsync(string today, CancellationToken ct)
    {
        var db = t.Db;
        var counts = (await db.ContactRequests.GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Status, x => x.Count);
        var byDay = await db.ContactRequests.Where(c => c.Status == ContactStatuses.Callback)
            .GroupBy(c => c.DueDate).Select(g => new { Date = g.Key, Count = g.Count() }).ToListAsync(ct);

        int todo = 0, overdue = 0, dueToday = 0, tomorrow = 0, week = 0, later = 0, nodate = 0;
        foreach (var row in byDay)
        {
            var bucket = ContactService.BucketOf(ContactStatuses.Callback, row.Date, today);
            switch (bucket)
            {
                case ContactService.Due.Overdue: overdue += row.Count; break;
                case ContactService.Due.Today: dueToday += row.Count; break;
                case ContactService.Due.Tomorrow: tomorrow += row.Count; break;
                case ContactService.Due.Week: week += row.Count; break;
                case ContactService.Due.Later: later += row.Count; break;
                default: nodate += row.Count; break;
            }
            if (ContactService.IsTodo(bucket)) todo += row.Count;
        }
        var newCount = counts.GetValueOrDefault(ContactStatuses.New);
        nodate += newCount;
        todo += newCount;

        return new
        {
            openNow = newCount + counts.GetValueOrDefault(ContactStatuses.Callback),
            overdueNow = overdue,
            todo,
            due = new { overdue, today = dueToday, tomorrow, week, later, nodate },
            byStatus = ContactService.Statuses
                .Select(s => new { status = s.Key, label = s.Label, s.IsOpen, count = counts.GetValueOrDefault(s.Key) }),
        };
    }

    // =====================================================================================
    //  contact_report
    // =====================================================================================

    [McpServerTool(Name = "contact_report", Title = "Bog'lanish hisoboti",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Bog'lanish kerak» hisoboti davr bo'yicha (sukut — oxirgi 7 kun, ko'pi bilan 366 kun): yangi talablar, "
        + "urinishlar, haqiqatan gaplashilganlar (ko'tarmagan qo'ng'iroq «bog'lanildi» emas), hal bo'ldi / qayta qo'ng'iroq / "
        + "bog'lanib bo'lmadi; kunlik oqim, xodimlar kesimi, sabablar kesimi (talab ochilgan sana bo'yicha), natijalar kesimi va "
        + "javoblarda eng ko'p uchragan so'zlar. Joriy ochiq va muddati o'tgan talablar soni ham. Panelda bu hisobot FAQAT "
        + "superadmin uchun. Contact follow-up report for a period (superadmin only).")]
    public async Task<string> ContactReportTool(
        [Description("Davr boshi YYYY-MM-DD (sukut — 6 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.ContactReport);
        var (f, tt) = McpToolContext.Range(from, to, defaultDays: 7);
        var m = await ContactReport.BuildAsync(t.Db, McpToolContext.Iso(f), McpToolContext.Iso(tt), TodayIso, 0, ct);

        return t.Json(new
        {
            from = m.From, to = m.To,
            totals = new { m.Created, m.Attempts, m.Reached, m.Done, m.Callback, m.Failed, m.WithResponse },
            current = new { openNow = m.OpenNow, overdueNow = m.OverdueNow },
            daily = m.Daily,
            byStaff = m.ByStaff,
            byReason = m.ByReason.Select(r => new { reason = r.ReasonLabel, r.Created, r.Done, r.Failed, r.Open }),
            byResult = m.ByResult,
            topWords = m.TopWords,
            note = "«attempts» — barcha bog'lanish urinishlari; «reached» — faqat odam bilan haqiqatan gaplashilganlari. "
                + "Sabablar kesimi talab OCHILGAN sana bo'yicha.",
        }, m.Daily.Count);
    }

    // =====================================================================================
    //  tasks_list
    // =====================================================================================

    [McpServerTool(Name = "tasks_list", Title = "Topshiriqlar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Xodimlarga berilgan topshiriqlar (Kanban): doska, mas'ul xodim, holat (sukut — ochiq | done — bajarilgan | "
        + "overdue — muddati o'tgan | today — bugun muddati | all), muddat oralig'i va nom/tavsif bo'yicha qidirish. «Bajarilgan» "
        + "— topshiriq yopiluvchi ustunda turibdi. Har qatorda doska, ustun, mas'ul, muhimlik, muddat, kechikkanmi, bajarilgan "
        + "vaqti, kim yaratgani va qadamlar (cheklist) bajarilishi. Arxivlangan topshiriqlar va (doska tanlanmasa) arxivlangan "
        + "doskalar sukut bo'yicha kirmaydi. Sahifalangan (ko'pi bilan 200 qator). "
        + "List work tasks with board, assignee, state, due-date and text filters.")]
    public async Task<string> TasksList(
        [Description("Doska id")] string? boardId = null,
        [Description("Mas'ul xodim id; biriktirilmaganlar uchun «none»")] string? assigneeId = null,
        [Description("open (sukut) | done | overdue | today | all")] string? state = null,
        [Description("Muddat — dan, YYYY-MM-DD")] string? dueFrom = null,
        [Description("Muddat — gacha (shu kun ham kiradi), YYYY-MM-DD")] string? dueTo = null,
        [Description("Topshiriq nomi yoki tavsifi bo'lagi")] string? search = null,
        [Description("Arxivlangan topshiriq va doskalarni ham qo'shish: true")] bool? includeArchived = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Tasks);
        var st = (state ?? "open").Trim().ToLowerInvariant();
        if (st is not ("open" or "done" or "overdue" or "today" or "all"))
            throw new McpException("«state»: open | done | overdue | today | all.");
        string? fromIso = string.IsNullOrWhiteSpace(dueFrom) ? null : McpToolContext.Iso(McpToolContext.Date(dueFrom, default, "dueFrom"));
        string? toIso = string.IsNullOrWhiteSpace(dueTo) ? null : McpToolContext.Iso(McpToolContext.Date(dueTo, default, "dueTo"));
        var db = t.Db;
        var today = TodayIso;

        // Columns are few; "done" is a property of the column (WorkTaskColumn.IsDone).
        var doneCols = await db.WorkTaskColumns.Where(c => c.IsDone).Select(c => c.Id).ToListAsync(ct);

        var q = db.WorkTasks.AsQueryable();
        if (includeArchived != true)
        {
            q = q.Where(x => !x.IsArchived);
            // Without a board filter, tasks of archived boards are hidden (dashboard, reminders).
            if (string.IsNullOrWhiteSpace(boardId))
                q = q.Where(x => !db.WorkTaskBoards.Any(b => b.Id == x.BoardId && b.IsArchived));
        }
        if (!string.IsNullOrWhiteSpace(boardId))
        {
            var bid = boardId.Trim();
            q = q.Where(x => x.BoardId == bid);
        }
        if (!string.IsNullOrWhiteSpace(assigneeId))
        {
            var aid = assigneeId.Trim();
            q = aid.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? q.Where(x => x.AssigneeId == "")
                : q.Where(x => x.AssigneeId == aid);
        }
        if (fromIso is not null) q = q.Where(x => x.DueDate != null && x.DueDate.CompareTo(fromIso) >= 0);
        if (toIso is not null) q = q.Where(x => x.DueDate != null && x.DueDate.CompareTo(toIso) <= 0);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(x => x.Title.ToLower().Contains(term) || x.Description.ToLower().Contains(term));
        }
        q = st switch
        {
            "open" => q.Where(x => !doneCols.Contains(x.ColumnId)),
            "done" => q.Where(x => doneCols.Contains(x.ColumnId)),
            "overdue" => q.Where(x => !doneCols.Contains(x.ColumnId) && x.DueDate != null && x.DueDate != ""
                                      && x.DueDate.CompareTo(today) < 0),
            "today" => q.Where(x => !doneCols.Contains(x.ColumnId) && x.DueDate == today),
            _ => q,
        };

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var ordered = st == "done"
            ? q.OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            : q.OrderBy(x => x.DueDate == null || x.DueDate == "").ThenBy(x => x.DueDate)
                .ThenByDescending(x => x.Priority).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id);
        var rows = await ordered.Skip((p - 1) * size).Take(size)
            .Select(x => new
            {
                x.Id, x.BoardId, x.ColumnId, x.Title, x.Description, x.AssigneeId, x.CreatedById, x.Priority,
                x.DueDate, x.DueTime, x.CreatedAt, x.CompletedAt, x.Repeat, x.IsArchived,
            }).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var boardIds = rows.Select(r => r.BoardId).Distinct().ToList();
        var colIds = rows.Select(r => r.ColumnId).Distinct().ToList();
        var boards = await db.WorkTaskBoards.Where(b => boardIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.IsArchived }).ToDictionaryAsync(b => b.Id, ct);
        var cols = await db.WorkTaskColumns.Where(c => colIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title, c.IsDone }).ToDictionaryAsync(c => c.Id, ct);
        var steps = await db.WorkTaskItems.Where(i => ids.Contains(i.TaskId))
            .GroupBy(i => i.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count(), Done = g.Sum(i => i.Done ? 1 : 0) })
            .ToDictionaryAsync(g => g.TaskId, ct);
        var names = await t.UserNamesAsync(rows.SelectMany(r => new[] { r.AssigneeId, r.CreatedById }), ct);

        var items = rows.Select(r =>
        {
            var col = cols.GetValueOrDefault(r.ColumnId);
            var board = boards.GetValueOrDefault(r.BoardId);
            var isDone = col?.IsDone ?? false;
            var step = steps.GetValueOrDefault(r.Id);
            return new
            {
                r.Id, r.Title,
                description = r.Description.Length > MaxTaskDescription ? r.Description[..MaxTaskDescription] : r.Description,
                board = new { id = r.BoardId, title = board?.Title ?? "", isArchived = board?.IsArchived ?? false },
                column = new { id = r.ColumnId, title = col?.Title ?? "", isDone },
                assignee = r.AssigneeId.Length == 0 ? null : new { id = r.AssigneeId, name = names.GetValueOrDefault(r.AssigneeId, "") },
                r.Priority,
                priorityLabel = PriorityLabel(r.Priority),
                r.DueDate, r.DueTime,
                isOverdue = !isDone && !string.IsNullOrWhiteSpace(r.DueDate) && string.CompareOrdinal(r.DueDate, today) < 0,
                completedAt = r.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                createdBy = names.GetValueOrDefault(r.CreatedById, ""),
                createdAt = r.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                checklist = new { done = step?.Done ?? 0, total = step?.Total ?? 0 },
                r.Repeat, r.IsArchived,
            };
        }).ToList();

        return t.Json(new { total, page = p, pageSize = size, items }, items.Count);
    }

    // =====================================================================================
    //  messages_history
    // =====================================================================================

    [McpServerTool(Name = "messages_history", Title = "Telegram e'lonlari tarixi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Paneldan Telegram bot orqali yuborilgan e'lonlar tarixi (eng yangisi tepada): sana, auditoriya (guruh / "
        + "barcha guruhlar / tanlangan, qarzdorlar), matn (ko'pi bilan 500 belgi), kim yuborgani, nechta chatga mo'ljallangan "
        + "va nechtasiga yetib borgani. Sana oralig'i va matn/auditoriya bo'yicha qidirish. Sahifalangan (ko'pi bilan 200 qator). "
        + "History of Telegram announcements sent from the panel.")]
    public async Task<string> MessagesHistory(
        [Description("Yuborilgan sana — dan, YYYY-MM-DD")] string? from = null,
        [Description("Yuborilgan sana — gacha (shu kun ham kiradi), YYYY-MM-DD")] string? to = null,
        [Description("Matn, auditoriya yoki yuboruvchi ismi bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Messages);
        DateOnly? f = string.IsNullOrWhiteSpace(from) ? null : McpToolContext.Date(from, default, "from");
        DateOnly? tt = string.IsNullOrWhiteSpace(to) ? null : McpToolContext.Date(to, default, "to");
        if (f is not null && tt is not null && f > tt)
            throw new McpException("«from» sanasi «to» dan keyin bo'lishi mumkin emas.");
        var db = t.Db;

        var q = db.Broadcasts.AsQueryable();
        if (f is not null)
        {
            var fromDt = f.Value.ToDateTime(TimeOnly.MinValue);
            q = q.Where(b => b.CreatedAt >= fromDt);
        }
        if (tt is not null)
        {
            var toDt = tt.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            q = q.Where(b => b.CreatedAt < toDt);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(b => b.Text.ToLower().Contains(term) || b.ClassName.ToLower().Contains(term)
                             || b.SenderName.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(b => new
            {
                b.Id, b.CreatedAt, b.ClassName, b.SenderName, b.RecipientCount, b.SentCount,
                Text = b.Text.Length > MaxBroadcastText ? b.Text.Substring(0, MaxBroadcastText) : b.Text,
                Truncated = b.Text.Length > MaxBroadcastText,
            }).ToListAsync(ct);

        var items = rows.Select(r => new
        {
            r.Id,
            sentAt = r.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            audience = r.ClassName,
            text = r.Text,
            truncated = r.Truncated,
            sender = r.SenderName,
            recipients = r.RecipientCount,
            sent = r.SentCount,
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            note = "Faqat Telegram e'lonlari. «recipients» — botga ulangan chatlar soni, «sent» — yetib borganlari.",
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  auto_message_rules
    // =====================================================================================

    [McpServerTool(Name = "auto_message_rules", Title = "Avto-xabar qoidalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Sozlangan avtomatik xabar qoidalari («Xabarlar → Avto xabarlar»): qaysi hodisada (to'lov qabul qilindi, "
        + "qarzdorlik, darsga kelmadi, tug'ilgan kun, yangi lid va h.k.) kimga (ota-ona / o'quvchi / o'qituvchi) qanday matn "
        + "yuboriladi, qoida yoqilganmi, Telegram kanali yoqilganmi va jadval maydonlari (turi, vaqti, oy kuni, daqiqa "
        + "siljishi, qamrov). Hodisa, toifa va yoqilgan/o'chirilgan bo'yicha filtr. Qoidasi yo'q hodisalar ham ro'yxatda. "
        + "Configured automatic notification rules.")]
    public async Task<string> AutoMessageRules(
        [Description("Hodisa kaliti (masalan payment_debt, attendance_absent)")] string? trigger = null,
        [Description("Toifa: Lidlar | O'quv jarayoni | Moliya | Boshqa")] string? category = null,
        [Description("Faqat yoqilganlar: true; faqat o'chirilganlar: false")] bool? enabled = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Messages);
        var trig = (trigger ?? "").Trim().ToLowerInvariant();
        if (trig.Length > 0 && !AutoMessageTriggers.IsKnown(trig))
            throw new McpException("«trigger» noma'lum. Mavjudlari: " + string.Join(", ", AutoMessageTriggers.All.Select(x => x.Key)) + ".");
        var cat = (category ?? "").Trim();
        var db = t.Db;

        var q = db.AutoMessageRules.AsQueryable();
        if (trig.Length > 0) q = q.Where(r => r.Trigger == trig);
        if (enabled is not null) q = q.Where(r => r.Enabled == enabled.Value);
        // Explicit projection: the SMS / push channel flags and the SMS provider are not read.
        var rows = await q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(McpToolContext.MaxPageSize + 1)
            .Select(r => new
            {
                r.Id, r.Name, r.Trigger, r.Enabled, r.SendTelegram, r.Audience, r.Template,
                r.ScheduleType, r.ScheduleTime, r.ScheduleDayOfMonth, r.OffsetMinutes, r.SendScope, r.CreatedAt,
            }).ToListAsync(ct);
        var truncated = rows.Count > McpToolContext.MaxPageSize;

        var items = rows.Take(McpToolContext.MaxPageSize)
            .Select(r => new { r, info = AutoMessageTriggers.Get(r.Trigger) })
            .Where(x => cat.Length == 0 || string.Equals(x.info?.Category, cat, StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                x.r.Id, x.r.Name,
                trigger = new
                {
                    key = x.r.Trigger,
                    label = x.info?.Label ?? x.r.Trigger,
                    category = x.info?.Category ?? AutoMessageTriggers.CategoryOther,
                },
                x.r.Enabled,
                x.r.SendTelegram,
                x.r.Audience,
                x.r.Template,
                schedule = new
                {
                    type = x.r.ScheduleType, time = x.r.ScheduleTime, dayOfMonth = x.r.ScheduleDayOfMonth,
                    offsetMinutes = x.r.OffsetMinutes, scope = x.r.SendScope,
                    usesSchedule = x.info?.SupportsSchedule ?? false,
                    usesSendScope = x.info?.SupportsSendScope ?? false,
                },
                createdAt = x.r.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
            }).ToList();

        var configured = rows.Select(r => r.Trigger).ToHashSet(StringComparer.Ordinal);
        var unconfigured = trig.Length > 0 || enabled is not null
            ? null
            : AutoMessageTriggers.All
                .Where(x => !configured.Contains(x.Key))
                .Where(x => cat.Length == 0 || string.Equals(x.Category, cat, StringComparison.OrdinalIgnoreCase))
                .Select(x => new { key = x.Key, label = x.Label, category = x.Category }).ToList();

        return t.Json(new
        {
            total = items.Count,
            truncated,
            note = "«schedule» maydonlari faqat «usesSchedule» / «usesSendScope» true bo'lgan hodisalarda ishlatiladi.",
            items,
            triggersWithoutRule = unconfigured,
        }, items.Count);
    }

    // =====================================================================================
    //  audit_log
    // =====================================================================================

    [McpServerTool(Name = "audit_log", Title = "O'zgarishlar tarixi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'zgarishlar tarixi (audit) — kim, qachon, nimani o'zgartirgani, eng yangisi tepada. Filtrlar: bo'lim "
        + "(students, contacts, classes, teachers, schedule, finance, leads, marketing, books, contracts, vacancies, staff, "
        + "settings, other), amal (create | update | delete | complete-and-transfer), xodim ismi bo'lagi, sana oralig'i, izoh "
        + "matni bo'yicha qidirish, o'quvchi yoki o'qituvchi id. Har qatorda vaqt, xodim, bo'lim, yozuv turi va id, amal va "
        + "izoh. «O'zgarishlar tarixi» ruxsati panelidagidek BUTUN tarixni ochadi (to'lov summalari ham). "
        + "Sahifalangan (sukut 50, ko'pi bilan 200). "
        + "The change history (audit log) with section, action, actor, date and text filters.")]
    public async Task<string> AuditLogTool(
        [Description("Bo'lim kaliti: students | contacts | classes | teachers | schedule | finance | leads | marketing | books | contracts | vacancies | staff | settings | other")] string? section = null,
        [Description("Amal: create | update | delete | complete-and-transfer")] string? action = null,
        [Description("Xodim ismi bo'lagi")] string? actor = null,
        [Description("Sana — dan, YYYY-MM-DD")] string? from = null,
        [Description("Sana — gacha (shu kun ham kiradi), YYYY-MM-DD")] string? to = null,
        [Description("Izoh matni bo'lagi")] string? search = null,
        [Description("Faqat shu o'quvchiga oid yozuvlar (o'quvchi id)")] string? studentId = null,
        [Description("Faqat shu o'qituvchiga oid yozuvlar (o'qituvchi id)")] string? teacherId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Audit);
        var sec = (section ?? "").Trim().ToLowerInvariant();
        if (sec.Length > 0 && !AuditSections.IsKnownSection(sec))
            throw new McpException("«section»: " + string.Join(" | ", AuditSections.All.Select(s => s.Key)) + ".");
        var act = (action ?? "").Trim().ToLowerInvariant();
        if (act.Length > 0 && !AuditActions.Contains(act))
            throw new McpException("«action»: " + string.Join(" | ", AuditActions) + ".");
        DateOnly? f = string.IsNullOrWhiteSpace(from) ? null : McpToolContext.Date(from, default, "from");
        DateOnly? tt = string.IsNullOrWhiteSpace(to) ? null : McpToolContext.Date(to, default, "to");
        if (f is not null && tt is not null && f > tt)
            throw new McpException("«from» sanasi «to» dan keyin bo'lishi mumkin emas.");
        // The `audit` key opens the WHOLE history, money included — deliberately, as in the panel (audit.md §3).
        var db = t.Db;

        var q = db.AuditLogs.AsQueryable();
        if (sec == AuditSections.Other)
        {
            var known = AuditSections.KnownEntityTypes.ToList();
            q = q.Where(a => !known.Contains(a.EntityType));
        }
        else if (sec.Length > 0)
        {
            var types = AuditSections.EntityTypesOf(sec).ToList();
            q = q.Where(a => types.Contains(a.EntityType));
        }
        if (act.Length > 0) q = q.Where(a => a.Action == act);
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var who = actor.Trim().ToLower();
            q = q.Where(a => a.ActorName != null && a.ActorName.ToLower().Contains(who));
        }
        // Timestamps are "yyyy-MM-ddTHH:mm:ss": a date prefix compares correctly as a string.
        if (f is not null)
        {
            var fromIso = McpToolContext.Iso(f.Value);
            q = q.Where(a => string.Compare(a.Timestamp, fromIso) >= 0);
        }
        if (tt is not null)
        {
            var nextDay = McpToolContext.Iso(tt.Value.AddDays(1));
            q = q.Where(a => string.Compare(a.Timestamp, nextDay) < 0);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            q = q.Where(a => a.Summary.ToLower().Contains(needle));
        }
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var sid = studentId.Trim();
            q = q.Where(a => a.StudentId == sid);
        }
        if (!string.IsNullOrWhiteSpace(teacherId))
        {
            var tid = teacherId.Trim();
            q = q.Where(a => a.TeacherId == tid);
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        // Only the listed columns are selected — never the before/after snapshots.
        var rows = await q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(a => new
            {
                a.Id, a.Timestamp, a.ActorName, a.EntityType, a.EntityId, a.Action, a.Summary, a.StudentId, a.TeacherId,
            }).ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var key = AuditSections.SectionOf(r.EntityType);
            return new
            {
                r.Id,
                time = r.Timestamp,
                actor = r.ActorName ?? "",
                section = key,
                sectionLabel = AuditSections.All.FirstOrDefault(s => s.Key == key).Label ?? key,
                entityType = r.EntityType,
                entityId = r.EntityId,
                action = r.Action,
                summary = r.Summary,
                studentId = string.IsNullOrEmpty(r.StudentId) ? null : r.StudentId,
                teacherId = string.IsNullOrEmpty(r.TeacherId) ? null : r.TeacherId,
            };
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            items,
        }, items.Count);
    }
}
