using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// The timetable: one weekday (the weekly template — <see cref="ScheduleService.GetGridAsync"/>, the panel's
/// "Dars jadvali" grid) or one date (template + lesson moves + group life span + substitutes —
/// <see cref="AttendanceWatch.LessonsOn"/>), and the free gaps between lessons (<see cref="ScheduleService.GetGapsAsync"/>).
/// Rules: .claude/rules/schedule.md — one group = one time; broken rows (no days, end before start) are left out of
/// the timetable but REPORTED (here with their names); gaps are only BETWEEN two lessons.
/// </summary>
[McpServerToolType]
public sealed class ScheduleTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Group), typeof(Teacher), typeof(Room), typeof(Subject), typeof(StudentGroup), typeof(Student),
        typeof(LessonNote), typeof(LessonReschedule), typeof(SubstituteTeacherAssignment),
    ];

    // =====================================================================================
    //  schedule_day — ScheduleController.GetGrid (Server) / DashboardController.TodayLessons for a date
    // =====================================================================================

    [McpServerTool(Name = "schedule_day", Title = "Kunlik dars jadvali",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bir kunlik dars jadvali. «day» (0=Dushanba … 6=Yakshanba) berilsa — haftalik SHABLON (paneldagi «Dars jadvali» "
        + "to'ri bilan bir xil); «date» (YYYY-MM-DD) berilsa — o'sha sananing HAQIQIY darslari: ko'chirilgan darslar (yangi kun "
        + "va vaqt), guruh boshlanish/tugash sanalari va o'rinbosar o'qituvchi hisobga olinadi. Ikkalasi ham berilmasa — bugungi "
        + "sana. Har darsda: guruh, kurs, o'qituvchi (sanada — darsni haqiqatan o'tadigan odam), xona, boshlanish/tugash vaqti, "
        + "a'zolar soni (o'rin egallaganlar: faol + sinov, muzlatilgan va arxivdagi o'quvchi emas). «skippedGroups» — vaqti yoki "
        + "kunlari buzuq/to'ldirilmagan, shuning uchun jadvalga TUSHMAGAN guruhlar (nomlari bilan). Filtrlar: o'qituvchi, xona. "
        + "Sahifalangan. One day's timetable: weekly template for a weekday, or the real lessons of a date.")]
    public async Task<string> ScheduleDay(
        [Description("Hafta kuni: 0=Dushanba, 1=Seshanba … 6=Yakshanba (shablon)")] int? day = null,
        [Description("Yoki sana YYYY-MM-DD (ko'chirishlar va o'rinbosarlar bilan)")] string? date = null,
        [Description("O'qituvchi id (sanada — o'rinbosar yoki guruhning o'z o'qituvchisi)")] string? teacherId = null,
        [Description("Xona id (rooms_list dan; eski matnli xona uchun «name:<nomi>»)")] string? roomId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Timetable);
        if (day is not null && !string.IsNullOrWhiteSpace(date))
            throw new McpException("«day» yoki «date» dan faqat bittasini bering.");
        if (day is not (null or (>= 0 and <= 6)))
            throw new McpException("«day»: 0 (Dushanba) … 6 (Yakshanba).");
        var tid = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim();
        var rid = string.IsNullOrWhiteSpace(roomId) ? null : roomId.Trim();
        var db = t.Db;

        var skipped = await BrokenGroupsAsync(tid, rid, ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);

        if (day is int weekday)
        {
            // The panel's grid (ScheduleService.GetGridAsync) + its day filter (ScheduleGridExport.LessonsOfDay),
            // the whole day (00:00–24:00) instead of the screen's 08:00–22:00 window.
            var grid = await new ScheduleService(db).GetGridAsync();
            var filter = new ScheduleGridExport.Filter(weekday, false, 0, 24 * 60, TeacherId: tid, RoomId: rid);
            var lessons = ScheduleGridExport.LessonsOfDay(grid.Groups, filter);
            var items = lessons.Skip((p - 1) * size).Take(size).Select(g => new
            {
                groupId = g.GroupId, groupName = g.GroupName,
                course = new { id = g.CourseId, name = g.CourseName },
                teacher = new { id = g.TeacherId, name = g.TeacherName },
                room = new { id = ScheduleGridExport.RoomKey(g.RoomId, g.RoomName), name = g.RoomName },
                startTime = g.Start, endTime = g.End, g.Minutes,
                members = g.Members, g.Capacity, g.Status,
                dayLabels = GroupTools.DayLabels(g.Days),
                g.StartDate, g.EndDate,
            }).ToList();

            return t.Json(new
            {
                mode = "weekday",
                day = weekday, dayLabel = ScheduleRules.DayShort(weekday),
                total = lessons.Count, page = p, pageSize = size,
                items,
                skippedGroups = new { count = skipped.Count, groups = skipped },
                note = "Haftalik shablon: ko'chirilgan darslar va o'rinbosarlar hisobga OLINMAGAN — aniq kun uchun «date» bering.",
            }, items.Count);
        }

        // A date: the same lesson source as the attendance monitor and the teacher's timetable.
        var dayDate = McpToolContext.Date(date, McpToolContext.Today, "date");
        var dayIso = McpToolContext.Iso(dayDate);
        var weekdayOfDate = ((int)dayDate.DayOfWeek + 6) % 7;

        var allGroups = await db.Classes.Where(g => !g.IsArchived).ToListAsync(ct);
        var allIds = allGroups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules
                .Where(r => allIds.Contains(r.ClassId) && (r.FromDate == dayIso || r.ToDate == dayIso))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments
                .Where(a => a.IsActive && allIds.Contains(a.GroupId)
                            && string.Compare(a.Date, dayIso) <= 0
                            && string.Compare(a.EndDate == null || a.EndDate == "" ? a.Date : a.EndDate, dayIso) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);
        var planned = AttendanceWatch.LessonsOn(allGroups, moves, assignments, dayDate);

        // A lesson whose (possibly moved) time is broken does not enter the timetable — reported instead.
        var valid = new List<AttendanceWatch.DayLesson>();
        foreach (var l in planned)
        {
            if (ScheduleRules.MakeSlot(weekdayOfDate, l.StartTime, l.EndTime) is not null) valid.Add(l);
            else if (skipped.All(s => s.Id != l.Group.Id)
                     && (tid is null || l.Group.TeacherId == tid || l.SubstituteTeacherId == tid)
                     && (rid is null || ScheduleGridExport.RoomKey(l.Group.RoomId, l.Group.Room) == rid))
                skipped.Add(new SkippedGroup(l.Group.Id, l.Group.Name, "Ko'chirilgan darsning vaqti buzuq"));
        }
        if (tid is not null) valid = valid.Where(l => l.SubstituteTeacherId == tid || l.Group.TeacherId == tid).ToList();
        if (rid is not null) valid = valid.Where(l => ScheduleGridExport.RoomKey(l.Group.RoomId, l.Group.Room) == rid).ToList();

        var groupIds = valid.Select(l => l.Group.Id).Distinct().ToList();
        var teacherIds = valid.Select(l => l.Group.TeacherId).Concat(valid.Select(l => l.SubstituteTeacherId))
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courseIds = valid.Select(l => l.Group.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var roomIds = valid.Select(l => l.Group.RoomId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var teacherNames = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var roomNames = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        // Members — the grid's definition (seat holders: active + trial, not frozen, student not archived).
        var members = await MembershipLifecycle.SeatHolders(db.StudentGroups, db.Students)
            .Where(sg => groupIds.Contains(sg.GroupId))
            .GroupBy(sg => sg.GroupId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var rows = valid.Select(l =>
            {
                var g = l.Group;
                var substitute = l.SubstituteTeacherId.Length > 0;
                var actualId = substitute ? l.SubstituteTeacherId : g.TeacherId;
                var start = ScheduleRules.ParseTime(l.StartTime)!.Value;
                var end = ScheduleRules.ParseTime(l.EndTime)!.Value;
                var roomName = !string.IsNullOrEmpty(g.RoomId)
                    ? roomNames.GetValueOrDefault(g.RoomId, g.Room ?? "")
                    : g.Room ?? "";
                return new
                {
                    groupId = g.Id, groupName = g.Name,
                    course = new { id = g.CourseId, name = courseNames.GetValueOrDefault(g.CourseId ?? "", "") },
                    teacher = new { id = actualId, name = teacherNames.GetValueOrDefault(actualId ?? "", "") },
                    substitute,
                    groupTeacher = new { id = g.TeacherId, name = teacherNames.GetValueOrDefault(g.TeacherId ?? "", "") },
                    room = new { id = ScheduleGridExport.RoomKey(g.RoomId, roomName), name = roomName },
                    startTime = ScheduleRules.FormatTime(start), endTime = ScheduleRules.FormatTime(end),
                    minutes = end - start,
                    moved = l.Moved,
                    members = members.GetValueOrDefault(g.Id),
                    g.Capacity,
                    status = g.IsBlocked ? "blocked" : (g.Status ?? "active"),
                };
            })
            .OrderBy(r => r.startTime, StringComparer.Ordinal)
            .ThenBy(r => r.groupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.groupId, StringComparer.Ordinal)
            .ToList();
        var pageItems = rows.Skip((p - 1) * size).Take(size).ToList();

        return t.Json(new
        {
            mode = "date",
            date = dayIso, day = weekdayOfDate, dayLabel = ScheduleRules.DayShort(weekdayOfDate),
            total = rows.Count, page = p, pageSize = size,
            items = pageItems,
            skippedGroups = new { count = skipped.Count, groups = skipped },
            note = "«teacher» — shu kuni darsni o'tadigan o'qituvchi (o'rinbosar bo'lsa — o'rinbosar), «moved» — dars "
                + "boshqa kundan ko'chirilgan. Bayram/dam olish kunlari modeli yo'q — bunday kunda ham dars rejada turadi.",
        }, pageItems.Count);
    }

    private sealed record SkippedGroup(string Id, string Name, string Reason);

    /// <summary>
    /// Non-archived groups left out of the timetable — the same rule as <c>ScheduleService.LoadLessonsAsync</c>
    /// (no lesson days, or no day yields a valid slot: broken time / end not after start). The panel shows only
    /// their COUNT; here the names too, so they can be fixed.
    /// </summary>
    private async Task<List<SkippedGroup>> BrokenGroupsAsync(string? teacherId, string? roomId, CancellationToken ct)
    {
        var db = t.Db;
        var groups = await db.Classes.Where(g => !g.IsArchived)
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new { g.Id, g.Name, g.Days, g.StartTime, g.EndTime, g.TeacherId, g.RoomId, g.Room })
            .ToListAsync(ct);
        var result = new List<SkippedGroup>();
        foreach (var g in groups)
        {
            if (teacherId is not null && g.TeacherId != teacherId) continue;
            if (roomId is not null && ScheduleGridExport.RoomKey(g.RoomId, g.Room) != roomId) continue;
            var days = g.Days ?? [];
            if (days.Count == 0)
                result.Add(new SkippedGroup(g.Id, g.Name, "Dars kunlari belgilanmagan"));
            else if (days.Distinct().All(d => ScheduleRules.MakeSlot(d, g.StartTime, g.EndTime) is null))
                result.Add(new SkippedGroup(g.Id, g.Name,
                    $"Dars vaqti buzuq: «{g.StartTime}» → «{g.EndTime}»"));
        }
        return result;
    }

    // =====================================================================================
    //  schedule_gaps — ScheduleController.GetGaps (ScheduleService.GetGapsAsync)
    // =====================================================================================

    [McpServerTool(Name = "schedule_gaps", Title = "Jadvaldagi bo'sh oraliqlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Haftalik jadvalda IKKI DARS ORASIDA qolib ketgan bo'sh oraliqlar (kun boshi/oxiridagi bo'sh vaqt hisoblanmaydi) "
        + "va ularni to'ldirish tavsiyalari — paneldagi bilan bir xil. scope=room — xona bekor turibdi, tavsiya: shu paytda bo'sh "
        + "o'qituvchilar (avval yonma-yon darsi borlar, keyin shu kuni markazdagilar, keyin umuman bo'shlar; teng bo'lsa haftalik "
        + "yuki kami); scope=teacher — o'qituvchi kutib o'tiribdi, tavsiya: shu paytda bo'sh xonalar. Ro'yxat tig'izlik (o'sha "
        + "paytda markazda nechta dars ketyapti) bo'yicha kamayish tartibida. Filtrlar: xona/o'qituvchi id, hafta kuni, eng qisqa "
        + "oraliq (sukut 60 daqiqa, 15..480). Sahifalangan. "
        + "Free gaps between lessons per room or teacher, with fill-in suggestions.")]
    public async Task<string> ScheduleGaps(
        [Description("room (sukut) — xonalar | teacher — o'qituvchilar")] string? scope = null,
        [Description("Faqat shu xona yoki o'qituvchi id")] string? ownerId = null,
        [Description("Faqat shu hafta kuni: 0=Dushanba … 6=Yakshanba")] int? day = null,
        [Description("Eng qisqa oraliq, daqiqa (sukut 60; 15..480)")] int? minMinutes = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Timetable);
        var sc = (scope ?? "room").Trim().ToLowerInvariant();
        if (sc is not ("room" or "teacher")) throw new McpException("«scope»: room | teacher.");
        if (day is not (null or (>= 0 and <= 6))) throw new McpException("«day»: 0 (Dushanba) … 6 (Yakshanba).");
        var min = ScheduleRules.ClampGapMinutes(minMinutes);

        var gaps = await new ScheduleService(t.Db).GetGapsAsync(sc, string.IsNullOrWhiteSpace(ownerId) ? null : ownerId.Trim(), min);
        // The service's order (peak, length, day, start) + owner as the final tie-breaker (deterministic paging).
        var list = gaps
            .Where(g => day is null || g.Day == day)
            .OrderByDescending(g => g.PeakScore).ThenByDescending(g => g.Minutes).ThenBy(g => g.Day)
            .ThenBy(g => g.Start, StringComparer.Ordinal)
            .ThenBy(g => g.OwnerName, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.OwnerId, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = list.Skip((p - 1) * size).Take(size).Select(g => new
        {
            g.Scope,
            owner = new { id = g.OwnerId, name = g.OwnerName },
            g.Day, g.DayLabel, g.Start, g.End, g.Minutes,
            g.BeforeGroup, g.AfterGroup,
            peakScore = g.PeakScore,
            suggestions = g.Suggestions.Select(s => new { s.Kind, s.Id, s.Name, s.Note, s.WeeklyMinutes }),
        }).ToList();

        return t.Json(new
        {
            scope = sc, minMinutes = min,
            total = list.Count, page = p, pageSize = size,
            note = "«peakScore» — shu oraliqda markazda nechta dars ketyapti (qanchalik tig'iz). Haftalik shablon bo'yicha; "
                + "vaqti buzuq guruhlar hisobga olinmagan.",
            items,
        }, items.Count);
    }
}
