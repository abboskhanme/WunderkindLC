using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// LC: SINOV DARSI TOPSHIRIQLARI (LMS'da yo'q) — `.claude/rules/hr-tasks.md` §8, qoidalar <see cref="HrTrialRules"/>.
/// Rejalashtiruvchining HAR siklida (5 daq) yuguradi: idempotent — bir (xodim, tur, kun) uchun bitta ro'yxat
/// (unikal kalit), kech qo'shilgan sinov o'sha ro'yxatga qo'shiladi, hammasi hal bo'lgan ro'yxat o'zi yopiladi.
/// </summary>
public sealed partial class HrTasksService
{
    /// <summary>Yopiladigan ro'yxatlar shu muddatdan eski bo'lmasin (cheksiz o'smasin).</summary>
    private const int TrialCloseWindowDays = 45;

    /// <summary>Bitta sikl: avval yopish, 09:00 dan keyin — ertangi va kechagi ro'yxatlar. Qaytaradi: nechta o'zgardi.</summary>
    public async Task<int> RunTrialTasksAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var local = HrValues.Local(now, tz);
        var today = DateOnly.FromDateTime(local);
        var changed = await AutoCloseTrialTasksAsync(today, now, ct);
        if (TimeOnly.FromDateTime(local) >= HrTrialRules.SendAt)
            foreach (var kind in HrTrialTaskKinds.All)
                changed += await EnsureTrialListsAsync(kind, today, local, now, tz, ct);
        return changed;
    }

    private async Task<int> EnsureTrialListsAsync(
        string kind, DateOnly today, DateTime local, DateTimeOffset now, TimeZoneInfo tz, CancellationToken ct)
    {
        var day = HrTrialRules.LessonDay(kind, today);
        var prefix = day.ToString("yyyy-MM-dd");
        var dayTrials = await db.TrialLessons.AsNoTracking().Where(t => t.ScheduledAt.StartsWith(prefix)).ToListAsync(ct);
        if (dayTrials.Count == 0) return 0;

        var leadIds = dayTrials.Select(t => t.LeadId).Distinct().ToList();
        var leads = await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var eligible = dayTrials
            .Where(t => leads.TryGetValue(t.LeadId, out var l)
                && HrTrialRules.Eligible(kind, t.Result, HrTrialRules.LeadClosed(l.ConvertedStudentId, l.ClosedAt, l.LostAt)))
            .OrderBy(t => t.ScheduledAt, StringComparer.Ordinal)
            .ToList();
        if (eligible.Count == 0) return 0;

        // Kimga: mas'ul → sinovga yozgan → direktor(lar). Faqat FAOL HR xodimi.
        var employees = await db.HrEmployees.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);
        var employeeByUser = employees.Where(e => !e.UserId.StartsWith("deleted:"))
            .GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.First().Id);
        var directors = employees.Where(e => e.HrRole == HrRoles.Director).Select(e => e.Id).ToList();
        var bookedBy = (await db.LeadEvents.AsNoTracking()
                .Where(e => leadIds.Contains(e.LeadId) && e.Type == "trial" && e.ActorUserId != null && e.ActorUserId != "")
                .Select(e => new { e.LeadId, e.ActorUserId, e.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(e => e.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAt, StringComparer.Ordinal).First().ActorUserId);

        var byEmployee = new Dictionary<string, List<TrialLesson>>();
        foreach (var trial in eligible)
        {
            var lead = leads[trial.LeadId];
            var to = HrTrialRules.Recipients(lead.AssigneeUserId, bookedBy.GetValueOrDefault(trial.LeadId),
                u => employeeByUser.GetValueOrDefault(u), directors);
            if (to.Count == 0)
                logger.LogWarning("HR sinov: {Lead} lidi uchun topshiriq oluvchi topilmadi (mas'ul, yozgan va direktor yo'q)", trial.LeadId);
            foreach (var employeeId in to)
                (byEmployee.TryGetValue(employeeId, out var list) ? list : byEmployee[employeeId] = []).Add(trial);
        }
        if (byEmployee.Count == 0) return 0;

        var items = await TrialItemsAsync(dayTrials, leads, ct);
        var targetIds = byEmployee.Keys.ToList();
        var rows = await db.HrTrialTasks
            .Where(r => r.Kind == kind && r.LessonDate == day && targetIds.Contains(r.EmployeeId))
            .ToDictionaryAsync(r => r.EmployeeId, ct);
        var employeeRows = employees.ToDictionary(e => e.Id);
        var changed = 0;

        foreach (var (employeeId, trials) in byEmployee)
        {
            var employee = employeeRows[employeeId];
            if (rows.TryGetValue(employeeId, out var row))
            {
                var added = trials.Where(t => !row.TrialIdList.Contains(t.Id)).ToList();
                if (added.Count == 0) continue;
                var task = await db.HrTasks.FirstOrDefaultAsync(t => t.Id == row.TaskId, ct);
                if (task is null || task.IsFinal) continue;   // bekor qilingan ro'yxat tirilmaydi
                var all = row.TrialIdList.Concat(added.Select(t => t.Id)).ToList();
                row.TrialIds = string.Join(",", all);
                row.UpdatedAt = now;
                var listed = all.Where(items.ContainsKey).Select(id => items[id]).ToList();
                task.Title = HrTrialRules.Title(kind, day, listed.Count);
                task.Description = HrTrialRules.Description(kind, listed);
                task.UpdatedAt = now;
                // Bajarilgan ro'yxatga yangi o'quvchi qo'shildi — u yana ochiq (yangi ish paydo bo'ldi).
                if (task.Status == HrTaskStatuses.Done) task.Reopen(now);
                await db.SaveChangesAsync(ct);
                changed++;
                var text = HrTasksTexts.TrialAdded(task.Title,
                    [.. added.Where(t => items.ContainsKey(t.Id)).Select(t => HrTrialRules.ItemText(listed.IndexOf(items[t.Id]) + 1, kind, items[t.Id]))]);
                await SendToUserAsync(employee.UserId, text, ct);
                continue;
            }

            var listItems = trials.Where(t => items.ContainsKey(t.Id)).Select(t => items[t.Id]).ToList();
            var newTask = new HrTask
            {
                EmployeeId = employeeId,
                Source = HrTaskSources.Director,
                Title = HrTrialRules.Title(kind, day, listItems.Count),
                Description = HrTrialRules.Description(kind, listItems),
                Kind = HrTaskKinds.Check,
                ProofRequired = false,
                OverduePolicy = HrOverduePolicies.KeepOpen,
                PeriodStart = today,
                DueAt = DueInstant(HrTrialRules.DueLocal(local), tz),
                Status = HrTaskStatuses.Open,
                CreatedBy = null,   // tizim — Telegram'da «Berdi: Tizim (sinov darsi)»
                CreatedAt = now,
                UpdatedAt = now,
            };
            var link = new HrTrialTask
            {
                TaskId = newTask.Id, EmployeeId = employeeId, Kind = kind, LessonDate = day,
                TrialIds = string.Join(",", trials.Select(t => t.Id)), CreatedAt = now, UpdatedAt = now,
            };
            db.HrTasks.Add(newTask);
            db.HrTrialTasks.Add(link);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Unikal kalit: parallel sikl (ikkinchi server/restart) shu ro'yxatni allaqachon yaratgan — ikkinchisi yo'q.
                if (db is DbContext ctx)
                {
                    ctx.Entry(newTask).State = EntityState.Detached;
                    ctx.Entry(link).State = EntityState.Detached;
                }
                logger.LogInformation(ex, "HR sinov: {Kind} {Day} ro'yxati {Employee} uchun allaqachon bor", kind, day, employeeId);
                continue;
            }
            changed++;
            await NotifyAssignedAsync(employee.UserId, newTask, tz, HrTrialRules.GiverName, ct);
        }
        return changed;
    }

    private static DateTimeOffset DueInstant(DateTime local, TimeZoneInfo tz) =>
        HrValues.At(DateOnly.FromDateTime(local), new TimeOnly(local.Hour, local.Minute), tz);

    /// <summary>Kunning sinovlari → ro'yxat qatorlari (ism, telefonlar, guruh, ustoz, kurs, daraja, mas'ul, natija).</summary>
    private async Task<Dictionary<string, HrTrialRules.Item>> TrialItemsAsync(
        List<TrialLesson> trials, Dictionary<string, Lead> leads, CancellationToken ct)
    {
        var groupIds = trials.Select(t => t.GroupId).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId }).ToDictionaryAsync(g => g.Id, ct);
        var teacherIds = groups.Values.Select(g => g.TeacherId).Where(x => x != "").Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var courseIds = groups.Values.Select(g => g.CourseId).Where(x => x != "").Distinct().ToList();
        var courses = await db.Subjects.AsNoTracking().Where(s => courseIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var assigneeIds = leads.Values.Select(l => l.AssigneeUserId).OfType<string>().Where(x => x != "").Distinct().ToList();
        var assignees = await db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var leadIds = leads.Keys.ToList();
        // Daraja: lidda saqlangani USTUN, bo'lmasa oxirgi daraja testi natijasi (LeadsController bilan bir xil).
        var testLevels = (await db.LevelTestSubmissions.AsNoTracking()
                .Where(s => leadIds.Contains(s.LeadId) && s.Level != "")
                .Select(s => new { s.LeadId, s.Level, s.CreatedAt }).ToListAsync(ct))
            .GroupBy(s => s.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAt ?? "", StringComparer.Ordinal).First().Level ?? "");

        var result = new Dictionary<string, HrTrialRules.Item>();
        foreach (var t in trials)
        {
            if (!leads.TryGetValue(t.LeadId, out var l)) continue;
            var g = groups.GetValueOrDefault(t.GroupId);
            result[t.Id] = new HrTrialRules.Item(
                t.Id, l.FullName, l.Phone, l.FatherPhone, l.MotherPhone, t.ScheduledAt,
                g?.Name ?? "", g is null ? "" : teachers.GetValueOrDefault(g.TeacherId, ""),
                g is null ? "" : courses.GetValueOrDefault(g.CourseId, ""),
                string.IsNullOrWhiteSpace(l.Level) ? testLevels.GetValueOrDefault(l.Id, "") : l.Level,
                l.AssigneeUserId is { } a ? assignees.GetValueOrDefault(a, "") : "",
                t.Result);
        }
        return result;
    }

    /// <summary>Ro'yxatdagi HAMMA sinov hal bo'lgan ochiq topshiriqni bajarilgan qiladi (izoh bilan).</summary>
    private async Task<int> AutoCloseTrialTasksAsync(DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var since = today.AddDays(-TrialCloseWindowDays);
        var open = await (from r in db.HrTrialTasks
                          join t in db.HrTasks on r.TaskId equals t.Id
                          where r.LessonDate >= since
                              && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                          select new { Row = r, Task = t })
            .ToListAsync(ct);
        if (open.Count == 0) return 0;

        var trialIds = open.SelectMany(o => o.Row.TrialIdList).Distinct().ToList();
        var trials = await db.TrialLessons.AsNoTracking().Where(t => trialIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var leadIds = trials.Values.Select(t => t.LeadId).Distinct().ToList();
        var leads = await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.ConvertedStudentId, l.ClosedAt, l.LostAt }).ToDictionaryAsync(l => l.Id, ct);
        var trialDays = (await db.TrialLessons.AsNoTracking().Where(t => leadIds.Contains(t.LeadId))
                .Select(t => new { t.LeadId, t.ScheduledAt }).ToListAsync(ct))
            .GroupBy(t => t.LeadId)
            .ToDictionary(g => g.Key, g => g.Select(t => HrTrialRules.DayOf(t.ScheduledAt)).OfType<DateOnly>().ToList());

        var closed = 0;
        foreach (var o in open)
        {
            var ids = o.Row.TrialIdList;
            if (ids.Count == 0) continue;
            var allResolved = ids.All(id =>
            {
                // O'chirilgan sinov — lidini bilib bo'lmaydi: hal deb olinadi (ro'yxatda abadiy osilib qolmasin).
                if (!trials.TryGetValue(id, out var trial)) return true;
                var lead = leads.GetValueOrDefault(trial.LeadId);
                return HrTrialRules.Resolved(o.Row.Kind, o.Row.LessonDate, lead is null,
                    lead is not null && HrTrialRules.LeadClosed(lead.ConvertedStudentId, lead.ClosedAt, lead.LostAt),
                    new HrTrialRules.TrialState(trial.Result, HrTrialRules.DayOf(trial.ScheduledAt)),
                    trialDays.GetValueOrDefault(trial.LeadId) ?? []);
            });
            if (!allResolved) continue;
            o.Task.Complete(now, HrTrialRules.AutoDoneComment, 0);
            closed++;
        }
        if (closed > 0) await db.SaveChangesAsync(ct);
        return closed;
    }
}
