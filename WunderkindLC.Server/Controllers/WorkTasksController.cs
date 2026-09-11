using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// "Topshiriqlar" bo'limi — xodimlarga beriladigan LOYIHAVIY topshiriqlar (Kanban doskasi,
/// ro'yxat, kalendar va nazorat paneli): muddat/mas'ul/muhimlik bilan bir martalik (yoki
/// takroriy) topshiriqlar.
///
/// <para><b>Ruxsat lineyasi</b> (<c>.claude/rules/permissions.md</c>): sinf darajasi
/// <c>tasks</c> — o'qish (GET) odatdagidek ochiq; YOZISH esa sahifa kaliti bilan ajratilgan:
/// topshiriqlar — <c>tasks.board</c>, doska/ustun sozlamalari — <c>tasks.settings</c>,
/// kunlik eslatma sozlamasi — <c>tasks.dashboard</c>.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("tasks")]
[Route("api/admin/work-tasks")]
public partial class WorkTasksController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");
    private static string NowIso => AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    // =================================================================================
    //  DOSKALAR va USTUNLAR
    // =================================================================================

    /// <summary>Doskalar (ustunlari bilan). Bazada birorta doska bo'lmasa — birinchi ochilishda
    /// standart "Umumiy" doskasi 4 ta ustun bilan avtomatik yaratiladi (bo'sh ekran chiqmasin).</summary>
    [HttpGet("boards")]
    public async Task<ActionResult<IEnumerable<WorkTaskBoardDto>>> Boards([FromQuery] bool includeArchived = false)
    {
        await EnsureSeedAsync();

        var boards = await db.WorkTaskBoards
            .Where(b => includeArchived || !b.IsArchived)
            .OrderBy(b => b.Order).ThenBy(b => b.Title).ToListAsync();
        var cols = await db.WorkTaskColumns.OrderBy(c => c.Order).ToListAsync();
        var counts = (await db.WorkTasks.Where(t => !t.IsArchived)
                .Select(t => new { t.BoardId, t.ColumnId }).ToListAsync());

        var byColumn = counts.GroupBy(x => x.ColumnId).ToDictionary(g => g.Key, g => g.Count());
        var byBoard = counts.GroupBy(x => x.BoardId).ToDictionary(g => g.Key, g => g.Count());

        return boards.Select(b => new WorkTaskBoardDto(
            b.Id, b.Title, b.Color, b.Order, b.IsArchived,
            cols.Where(c => c.BoardId == b.Id)
                .Select(c => new WorkTaskColumnDto(c.Id, c.BoardId, c.Title, c.Color, c.Order,
                    c.IsDone, c.IsSystem, byColumn.GetValueOrDefault(c.Id, 0)))
                .ToList(),
            byBoard.GetValueOrDefault(b.Id, 0))).ToList();
    }

    [HttpPost("boards")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult<WorkTaskBoardDto>> CreateBoard(WorkTaskBoardInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Doska nomi bo'sh" });

        var order = await db.WorkTaskBoards.Select(b => (int?)b.Order).MaxAsync() ?? -1;
        var board = new WorkTaskBoard
        {
            Title = input.Title.Trim(),
            Color = Color(input.Color, "violet"),
            Order = order + 1,
            CreatedById = Uid,
        };
        db.WorkTaskBoards.Add(board);
        db.WorkTaskColumns.AddRange(DefaultColumns(board.Id));
        await db.SaveChangesAsync();

        var cols = await db.WorkTaskColumns.Where(c => c.BoardId == board.Id).OrderBy(c => c.Order).ToListAsync();
        return new WorkTaskBoardDto(board.Id, board.Title, board.Color, board.Order, false,
            cols.Select(c => new WorkTaskColumnDto(c.Id, c.BoardId, c.Title, c.Color, c.Order, c.IsDone, c.IsSystem, 0))
                .ToList(), 0);
    }

    [HttpPut("boards/{id}")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> UpdateBoard(string id, WorkTaskBoardInput input)
    {
        var b = await db.WorkTaskBoards.FindAsync(id);
        if (b is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Doska nomi bo'sh" });
        b.Title = input.Title.Trim();
        b.Color = Color(input.Color, b.Color);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Doskani ARXIVLASH (o'chirish emas) — topshiriqlari va tarixi saqlanadi.
    /// <c>?hard=true</c> berilsa butunlay o'chiriladi (topshiriqlari bilan).</summary>
    [HttpDelete("boards/{id}")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> DeleteBoard(string id, [FromQuery] bool hard = false)
    {
        var b = await db.WorkTaskBoards.FindAsync(id);
        if (b is null) return NotFound();

        if (!hard)
        {
            b.IsArchived = true;
            await db.SaveChangesAsync();
            return NoContent();
        }

        var taskIds = await db.WorkTasks.Where(t => t.BoardId == id).Select(t => t.Id).ToListAsync();
        db.WorkTaskItems.RemoveRange(db.WorkTaskItems.Where(i => taskIds.Contains(i.TaskId)));
        db.WorkTaskComments.RemoveRange(db.WorkTaskComments.Where(c => taskIds.Contains(c.TaskId)));
        db.WorkTaskEvents.RemoveRange(db.WorkTaskEvents.Where(e => taskIds.Contains(e.TaskId)));
        db.WorkTasks.RemoveRange(db.WorkTasks.Where(t => t.BoardId == id));
        db.WorkTaskColumns.RemoveRange(db.WorkTaskColumns.Where(c => c.BoardId == id));
        db.WorkTaskBoards.Remove(b);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("columns")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult<WorkTaskColumnDto>> CreateColumn(WorkTaskColumnInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Ustun nomi bo'sh" });
        if (!await db.WorkTaskBoards.AnyAsync(b => b.Id == input.BoardId))
            return BadRequest(new { message = "Doska topilmadi" });

        var order = await db.WorkTaskColumns.Where(c => c.BoardId == input.BoardId)
            .Select(c => (int?)c.Order).MaxAsync() ?? -1;
        var col = new WorkTaskColumn
        {
            BoardId = input.BoardId,
            Title = input.Title.Trim(),
            Color = Color(input.Color, "slate"),
            Order = order + 1,
            IsDone = input.IsDone,
        };
        db.WorkTaskColumns.Add(col);
        await db.SaveChangesAsync();
        return new WorkTaskColumnDto(col.Id, col.BoardId, col.Title, col.Color, col.Order, col.IsDone, false, 0);
    }

    [HttpPut("columns/{id}")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> UpdateColumn(string id, WorkTaskColumnInput input)
    {
        var c = await db.WorkTaskColumns.FindAsync(id);
        if (c is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Ustun nomi bo'sh" });

        // Doskada kamida bitta YOPILUVCHI ustun qolishi shart — aks holda topshiriqni
        // "bajarildi" qilib bo'lmasdi (bot tugmasi ham ishlamay qolardi).
        if (c.IsDone && !input.IsDone
            && !await db.WorkTaskColumns.AnyAsync(x => x.BoardId == c.BoardId && x.Id != id && x.IsDone))
            return BadRequest(new { message = "Doskada kamida bitta «bajarildi» ustuni qolishi kerak" });

        c.Title = input.Title.Trim();
        c.Color = Color(input.Color, c.Color);
        c.IsDone = input.IsDone;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ustunni chapga (-1) yoki o'ngga (+1) surish.</summary>
    [HttpPut("columns/{id}/move")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> MoveColumn(string id, [FromQuery] int dir)
    {
        var c = await db.WorkTaskColumns.FindAsync(id);
        if (c is null) return NotFound();
        var cols = await db.WorkTaskColumns.Where(x => x.BoardId == c.BoardId)
            .OrderBy(x => x.Order).ToListAsync();
        var i = cols.FindIndex(x => x.Id == id);
        var j = i + (dir < 0 ? -1 : 1);
        if (i < 0 || j < 0 || j >= cols.Count) return NoContent();
        (cols[i].Order, cols[j].Order) = (cols[j].Order, cols[i].Order);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ustunni o'chirish. Ichidagi topshiriqlar YO'QOLMAYDI — o'sha doskaning birinchi
    /// ustuniga ko'chiriladi (tizim ustuni umuman o'chirilmaydi).</summary>
    [HttpDelete("columns/{id}")]
    [AdminPerm("tasks.settings")]
    public async Task<ActionResult> DeleteColumn(string id)
    {
        var c = await db.WorkTaskColumns.FindAsync(id);
        if (c is null) return NotFound();
        if (c.IsSystem) return BadRequest(new { message = "Tizim ustunini o'chirib bo'lmaydi" });

        var rest = await db.WorkTaskColumns.Where(x => x.BoardId == c.BoardId && x.Id != id)
            .OrderBy(x => x.Order).ToListAsync();
        if (rest.Count == 0) return BadRequest(new { message = "Oxirgi ustunni o'chirib bo'lmaydi" });

        var target = rest[0];
        foreach (var t in await db.WorkTasks.Where(t => t.ColumnId == id).ToListAsync())
            t.ColumnId = target.Id;
        db.WorkTaskColumns.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // =================================================================================
    //  TOPSHIRIQLAR
    // =================================================================================

    /// <summary>Topshiriqlar ro'yxati (doska/mas'ul/holat/muddat bo'yicha filtrlar bilan).
    /// Doska va ro'yxat, kalendar — hammasi shu bitta endpointdan oziqlanadi.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkTaskDto>>> List(
        [FromQuery] string? boardId, [FromQuery] string? assigneeId, [FromQuery] string? q,
        [FromQuery] string? status, [FromQuery] int? priority,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] bool archived = false)
    {
        await EnsureSeedAsync();

        var query = db.WorkTasks.Where(t => t.IsArchived == archived);
        if (!string.IsNullOrWhiteSpace(boardId)) query = query.Where(t => t.BoardId == boardId);
        if (!string.IsNullOrWhiteSpace(assigneeId)) query = query.Where(t => t.AssigneeId == assigneeId);
        if (priority is not null) query = query.Where(t => t.Priority == priority);
        if (!string.IsNullOrWhiteSpace(from)) query = query.Where(t => t.DueDate != null && t.DueDate.CompareTo(from) >= 0);
        if (!string.IsNullOrWhiteSpace(to)) query = query.Where(t => t.DueDate != null && t.DueDate.CompareTo(to) <= 0);
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Npgsql'da ILike, SQLite testlarida provayderga bog'liq bo'lmagan tarmoq
            // (`StudentsController.Search` bilan AYNAN bir xil sabab — ILike SQLite'da yo'q).
            var term = q.Trim();
            if (db.Database.IsNpgsql())
            {
                var like = $"%{term}%";
                query = query.Where(t => EF.Functions.ILike(t.Title, like)
                                         || EF.Functions.ILike(t.Description, like));
            }
            else
            {
                var lower = term.ToLower();
                query = query.Where(t => t.Title.ToLower().Contains(lower)
                                         || t.Description.ToLower().Contains(lower));
            }
        }

        var tasks = await query.OrderBy(t => t.Order).ThenByDescending(t => t.CreatedAt).ToListAsync();
        var dtos = await MapAsync(tasks);

        // "status" — hisoblanadigan kesim (ustunning IsDone bayrog'i + muddat), shuning uchun
        // bazada emas, DTO ustida filtrlanadi.
        return status switch
        {
            "open" => dtos.Where(d => !d.IsDone).ToList(),
            "done" => dtos.Where(d => d.IsDone).ToList(),
            "overdue" => dtos.Where(d => d.IsOverdue).ToList(),
            "today" => dtos.Where(d => !d.IsDone && d.DueDate == Today).ToList(),
            _ => dtos,
        };
    }

    /// <summary>Bitta topshiriq DETALI — qadamlar, izohlar va harakatlar tarixi bilan.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkTaskDetailDto>> Detail(string id)
    {
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();

        var dto = (await MapAsync([t])).First();
        var steps = await db.WorkTaskItems.Where(i => i.TaskId == id)
            .OrderBy(i => i.Order).ToListAsync();
        var comments = await db.WorkTaskComments.Where(c => c.TaskId == id)
            .OrderBy(c => c.CreatedAt).ToListAsync();
        var events = await db.WorkTaskEvents.Where(e => e.TaskId == id)
            .OrderByDescending(e => e.CreatedAt).Take(80).ToListAsync();

        var names = await NamesAsync(comments.Select(c => c.AuthorId).Concat(events.Select(e => e.ActorId)));

        return new WorkTaskDetailDto(
            dto,
            steps.Select(s => new WorkTaskStepDto(s.Id, s.Title, s.Done, Iso(s.DoneAt), s.Order)).ToList(),
            comments.Select(c => new WorkTaskCommentDto(
                c.Id, c.AuthorId, names.GetValueOrDefault(c.AuthorId, "—"), c.Text, Iso(c.CreatedAt)!)).ToList(),
            events.Select(e => new WorkTaskEventDto(
                e.Id, e.ActorId, names.GetValueOrDefault(e.ActorId, "—"), e.Kind, e.Text, Iso(e.CreatedAt)!)).ToList());
    }

    [HttpPost]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskDto>> Create(WorkTaskInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Topshiriq nomi bo'sh" });

        var board = await db.WorkTaskBoards.FindAsync(input.BoardId);
        if (board is null) return BadRequest(new { message = "Doska topilmadi" });

        var column = await ResolveColumnAsync(board.Id, input.ColumnId);
        if (column is null) return BadRequest(new { message = "Ustun topilmadi" });

        var order = await db.WorkTasks.Where(t => t.ColumnId == column.Id)
            .Select(t => (int?)t.Order).MinAsync() ?? 0;

        var task = new WorkTask
        {
            BoardId = board.Id,
            ColumnId = column.Id,
            Title = input.Title.Trim(),
            Description = input.Description?.Trim() ?? "",
            AssigneeId = input.AssigneeId?.Trim() ?? "",
            CreatedById = Uid,
            Priority = Math.Clamp(input.Priority, 0, 3),
            DueDate = Day(input.DueDate),
            DueTime = string.IsNullOrWhiteSpace(input.DueTime) ? null : input.DueTime.Trim(),
            Order = order - 1,                       // yangi topshiriq ustun BOSHIDA turadi
            Tags = (input.Tags ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct().ToList(),
            Repeat = Repeat(input.Repeat),
        };
        if (column.IsDone) { task.CompletedAt = AppClock.Now; task.CompletedById = Uid; }

        db.WorkTasks.Add(task);
        Log(task.Id, "created", $"Topshiriq yaratildi: «{task.Title}»");
        await db.SaveChangesAsync();

        await NotifyAssignedAsync(task, board.Title);
        return (await MapAsync([task])).First();
    }

    [HttpPut("{id}")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskDto>> Update(string id, WorkTaskInput input)
    {
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Topshiriq nomi bo'sh" });

        var prevAssignee = t.AssigneeId;

        if (t.Title != input.Title.Trim())
            Log(t.Id, "renamed", $"Nomi o'zgardi: «{t.Title}» → «{input.Title.Trim()}»");
        t.Title = input.Title.Trim();
        t.Description = input.Description?.Trim() ?? "";

        var newAssignee = input.AssigneeId?.Trim() ?? "";
        if (newAssignee != t.AssigneeId)
        {
            var names = await NamesAsync([t.AssigneeId, newAssignee]);
            Log(t.Id, "assigned",
                $"Mas'ul: {names.GetValueOrDefault(t.AssigneeId, "—")} → {names.GetValueOrDefault(newAssignee, "—")}");
            t.AssigneeId = newAssignee;
        }

        var newPriority = Math.Clamp(input.Priority, 0, 3);
        if (newPriority != t.Priority)
        {
            Log(t.Id, "priority", $"Muhimlik: {WorkTaskTelegram.Priority(t.Priority)} → {WorkTaskTelegram.Priority(newPriority)}");
            t.Priority = newPriority;
        }

        var newDue = Day(input.DueDate);
        if (newDue != t.DueDate)
        {
            Log(t.Id, "due", $"Muddat: {WorkTaskTelegram.Day(t.DueDate)} → {WorkTaskTelegram.Day(newDue)}");
            t.DueDate = newDue;
            t.ReminderSentDate = null;               // muddat surildi — eslatma qaytadan ishlaydi
        }
        t.DueTime = string.IsNullOrWhiteSpace(input.DueTime) ? null : input.DueTime.Trim();
        t.Tags = (input.Tags ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct().ToList();
        t.Repeat = Repeat(input.Repeat);
        t.UpdatedAt = AppClock.Now;

        // Ustun ham berilgan bo'lsa — ko'chirish (oyna ichidan holat o'zgartirish).
        if (!string.IsNullOrWhiteSpace(input.ColumnId) && input.ColumnId != t.ColumnId)
            await ApplyMoveAsync(t, input.ColumnId!, 0);

        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(t.AssigneeId) && t.AssigneeId != prevAssignee)
        {
            var board = await db.WorkTaskBoards.FindAsync(t.BoardId);
            await NotifyAssignedAsync(t, board?.Title ?? "");
        }
        return (await MapAsync([t])).First();
    }

    /// <summary>SUDRAB KO'CHIRISH — maqsad ustun + ustun ichidagi yangi o'rin. Yopiluvchi ustunga
    /// tushganda topshiriq "bajarildi" bo'ladi va takroriy bo'lsa keyingi nusxasi tug'iladi.</summary>
    [HttpPut("{id}/move")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskDto>> Move(string id, WorkTaskMoveInput input)
    {
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        var ok = await ApplyMoveAsync(t, input.ColumnId, input.Order);
        if (!ok) return BadRequest(new { message = "Ustun topilmadi" });
        await db.SaveChangesAsync();
        return (await MapAsync([t])).First();
    }

    /// <summary>Arxivlash / arxivdan qaytarish.</summary>
    [HttpPut("{id}/archive")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult> Archive(string id, [FromQuery] bool value = true)
    {
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        t.IsArchived = value;
        t.UpdatedAt = AppClock.Now;
        Log(t.Id, "archived", value ? "Arxivlandi" : "Arxivdan qaytarildi");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Topshiriqni BUTUNLAY o'chirish (qadamlari, izohlari va tarixi bilan).</summary>
    [HttpDelete("{id}")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult> Delete(string id)
    {
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();
        db.WorkTaskItems.RemoveRange(db.WorkTaskItems.Where(i => i.TaskId == id));
        db.WorkTaskComments.RemoveRange(db.WorkTaskComments.Where(c => c.TaskId == id));
        db.WorkTaskEvents.RemoveRange(db.WorkTaskEvents.Where(e => e.TaskId == id));
        db.WorkTasks.Remove(t);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Qadamlar (checklist) ----------

    [HttpPost("{id}/steps")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskStepDto>> AddStep(string id, WorkTaskStepInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "Qadam nomi bo'sh" });
        if (!await db.WorkTasks.AnyAsync(t => t.Id == id)) return NotFound();

        var order = await db.WorkTaskItems.Where(i => i.TaskId == id)
            .Select(i => (int?)i.Order).MaxAsync() ?? -1;
        var step = new WorkTaskItem { TaskId = id, Title = input.Title!.Trim(), Order = order + 1 };
        db.WorkTaskItems.Add(step);
        Log(id, "step", $"Qadam qo'shildi: «{step.Title}»");
        await db.SaveChangesAsync();
        return new WorkTaskStepDto(step.Id, step.Title, false, null, step.Order);
    }

    [HttpPut("steps/{stepId}")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult> UpdateStep(string stepId, WorkTaskStepInput input)
    {
        var s = await db.WorkTaskItems.FindAsync(stepId);
        if (s is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(input.Title)) s.Title = input.Title!.Trim();
        if (input.Done is not null && input.Done != s.Done)
        {
            s.Done = input.Done.Value;
            s.DoneAt = s.Done ? AppClock.Now : null;
            Log(s.TaskId, "step", (s.Done ? "Qadam bajarildi: " : "Qadam qaytarildi: ") + $"«{s.Title}»");
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("steps/{stepId}")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult> DeleteStep(string stepId)
    {
        var s = await db.WorkTaskItems.FindAsync(stepId);
        if (s is null) return NotFound();
        db.WorkTaskItems.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Izohlar ----------

    [HttpPost("{id}/comments")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult<WorkTaskCommentDto>> AddComment(string id, WorkTaskCommentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            return BadRequest(new { message = "Izoh bo'sh" });
        var t = await db.WorkTasks.FindAsync(id);
        if (t is null) return NotFound();

        var c = new WorkTaskComment { TaskId = id, AuthorId = Uid, Text = input.Text.Trim() };
        db.WorkTaskComments.Add(c);
        Log(id, "comment", "Izoh yozildi");
        await db.SaveChangesAsync();

        var names = await NamesAsync([Uid]);
        return new WorkTaskCommentDto(c.Id, c.AuthorId, names.GetValueOrDefault(Uid, "—"), c.Text, Iso(c.CreatedAt)!);
    }

    [HttpDelete("comments/{commentId}")]
    [AdminPerm("tasks.board")]
    public async Task<ActionResult> DeleteComment(string commentId)
    {
        var c = await db.WorkTaskComments.FindAsync(commentId);
        if (c is null) return NotFound();
        db.WorkTaskComments.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
