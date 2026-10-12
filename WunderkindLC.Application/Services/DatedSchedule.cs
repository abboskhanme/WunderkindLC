using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DATED SCHEDULE — «Kun» / «Hafta» views of the home page and «Dars jadvali» (CRM Phase 5,
/// <c>.claude/rules/schedule.md</c> §8). Unlike the weekday template (<see cref="ScheduleService.GetGridAsync"/>)
/// it shows REAL lessons of real dates: one source with the teacher schedule, the lesson monitor and the
/// attendance reminder — <see cref="AttendanceWatch.LessonsOn"/> (weekdays + one-off MOVES on their new date and
/// time + group lifetime + SUBSTITUTES).
/// </summary>
public static class DatedSchedule
{
    /// <summary>Longest range in one request (a week: Monday–Sunday).</summary>
    public const int MaxDays = 7;

    public const string RangeTooLongMessage = "Oraliq 7 kundan oshmasligi kerak";
    public const string BadRangeMessage = "Sana oralig'i noto'g'ri";

    /// <summary>
    /// <c>from</c>/<c>to</c> from the query: empty <paramref name="from"/> = <paramref name="today"/>, empty
    /// <paramref name="to"/> = <c>from</c>. Error text (Uzbek) when broken, reversed or longer than 7 days.
    /// </summary>
    public static (DateOnly From, DateOnly To, string? Error) ParseRange(string? from, string? to, DateOnly today)
    {
        DateOnly f = today, t;
        if (!string.IsNullOrWhiteSpace(from) && !DateOnly.TryParseExact(from.Trim(), "yyyy-MM-dd", out f))
            return (today, today, BadRangeMessage);
        if (string.IsNullOrWhiteSpace(to)) t = f;
        else if (!DateOnly.TryParseExact(to.Trim(), "yyyy-MM-dd", out t))
            return (today, today, BadRangeMessage);
        if (t < f) return (f, t, BadRangeMessage);
        if (t.DayNumber - f.DayNumber > MaxDays - 1) return (f, t, RangeTooLongMessage);
        return (f, t, null);
    }

    /// <summary>Everything the pure builder needs (loaded once by <see cref="LoadAsync"/>).</summary>
    public sealed record Input(
        IReadOnlyList<Group> Groups,
        ILookup<string, LessonReschedule> Moves,
        ILookup<string, SubstituteTeacherAssignment> Assignments,
        IReadOnlyList<Room> Rooms,
        IReadOnlyDictionary<string, string> TeacherNames,
        IReadOnlyCollection<string> ActiveTeacherIds,
        IReadOnlyDictionary<string, string> CourseNames,
        IReadOnlyDictionary<string, int> Members,
        IReadOnlySet<string> AttendanceTaken);

    /// <summary>Key of <see cref="Input.AttendanceTaken"/>: "yyyy-MM-dd|groupId" — the group's FIRST attendance mark exists
    /// that day (<see cref="LessonNote.AttendanceTakenAt"/>, <see cref="AttendanceTaking"/>).</summary>
    public static string TakenKey(string date, string groupId) => date + "|" + groupId;

    /// <summary>Pure builder (tested: <c>DatedScheduleTests</c>).</summary>
    public static DatedScheduleDto Build(Input x, DateOnly from, DateOnly to)
    {
        var roomsById = x.Rooms.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var lessons = new List<DatedLessonDto>();

        // Skipped = no weekdays, or no valid time — the same definition as the weekday grid.
        var skipped = x.Groups.Count(g => (g.Days ?? []).Where(d => d is >= 0 and <= 6).All(d =>
            ScheduleRules.MakeSlot(d, g.StartTime, g.EndTime) is null));

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var date = day.ToString("yyyy-MM-dd");
            foreach (var l in AttendanceWatch.LessonsOn(x.Groups, x.Moves, x.Assignments, day))
            {
                if (ScheduleRules.MakeSlot(0, l.StartTime, l.EndTime) is null) continue;
                var g = l.Group;
                var roomId = g.RoomId ?? "";
                roomsById.TryGetValue(roomId, out var room);
                var roomName = room?.Name ?? g.Room ?? "";
                var key = ScheduleGridExport.RoomKey(roomId.Length > 0 && room is not null ? roomId : "", roomName);
                lessons.Add(new DatedLessonDto(
                    date, g.Id, g.Name, g.CourseId ?? "", x.CourseNames.GetValueOrDefault(g.CourseId ?? "", ""),
                    g.TeacherId ?? "", x.TeacherNames.GetValueOrDefault(g.TeacherId ?? "", ""),
                    l.SubstituteTeacherId, l.SubstituteTeacherId.Length > 0
                        ? x.TeacherNames.GetValueOrDefault(l.SubstituteTeacherId, "O'rinbosar") : "",
                    key, roomName, room?.Floor,
                    ScheduleRules.FormatTime(ScheduleRules.ParseTime(l.StartTime)!.Value),
                    ScheduleRules.FormatTime(ScheduleRules.ParseTime(l.EndTime)!.Value),
                    l.Moved, x.Members.GetValueOrDefault(g.Id), g.Capacity,
                    x.AttendanceTaken.Contains(TakenKey(date, g.Id)),
                    g.IsBlocked ? "blocked" : string.IsNullOrWhiteSpace(g.Status) ? "active" : g.Status));
            }
        }

        lessons = lessons
            .OrderBy(l => l.Date, StringComparer.Ordinal)
            .ThenBy(l => ScheduleRules.ParseTime(l.Start) ?? int.MaxValue)
            .ThenBy(l => l.GroupName, ScheduleGridExport.NaturalComparer)
            .ToList();

        // Rooms: every ACTIVE room (an empty room must be visible) + any room a lesson uses (inactive room,
        // legacy free-text name) — a lesson never disappears for lack of a column.
        var rooms = x.Rooms.Where(r => r.IsActive)
            .Select(r => new DatedRoomDto(r.Id, r.Name, r.Floor, r.Capacity)).ToList();
        var known = rooms.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var l in lessons)
        {
            if (l.RoomId.Length == 0 || !known.Add(l.RoomId)) continue;
            roomsById.TryGetValue(l.RoomId, out var r);
            rooms.Add(new DatedRoomDto(l.RoomId, l.RoomName.Length > 0 ? l.RoomName : l.RoomId, l.Floor, r?.Capacity ?? 0));
        }
        rooms = rooms
            .OrderBy(r => r.Floor is null ? 1 : 0).ThenBy(r => r.Floor ?? 0)
            .ThenBy(r => r.Name, ScheduleGridExport.NaturalComparer)
            .ToList();

        // Teachers: non-archived ones (who is free must be visible) + anyone teaching a listed lesson.
        var teacherIds = new HashSet<string>(x.ActiveTeacherIds, StringComparer.Ordinal);
        foreach (var l in lessons)
        {
            if (l.TeacherId.Length > 0) teacherIds.Add(l.TeacherId);
            if (l.SubstituteTeacherId.Length > 0) teacherIds.Add(l.SubstituteTeacherId);
        }
        var teachers = teacherIds
            .Select(id => new DatedTeacherDto(id, x.TeacherNames.GetValueOrDefault(id, "")))
            .Where(t => t.Name.Length > 0)
            .OrderBy(t => t.Name, ScheduleGridExport.NaturalComparer)
            .ToList();

        return new DatedScheduleDto(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), rooms, teachers, lessons, skipped);
    }

    /// <summary>Loads <see cref="Input"/> for <c>[from, to]</c> (read-only, AsNoTracking).</summary>
    public static async Task<Input> LoadAsync(IAppDbContext db, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var lo = from.ToString("yyyy-MM-dd");
        var hi = to.ToString("yyyy-MM-dd");

        var groups = await db.Classes.AsNoTracking().Where(g => !g.IsArchived).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules.AsNoTracking()
                .Where(r => ids.Contains(r.ClassId)
                            && ((string.Compare(r.FromDate, lo) >= 0 && string.Compare(r.FromDate, hi) <= 0)
                                || (string.Compare(r.ToDate, lo) >= 0 && string.Compare(r.ToDate, hi) <= 0)))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => a.IsActive && ids.Contains(a.GroupId)
                            && string.Compare(a.Date, hi) <= 0
                            && string.Compare(a.EndDate ?? a.Date, lo) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);

        var rooms = await db.Rooms.AsNoTracking().ToListAsync(ct);
        var teachers = await db.Teachers.AsNoTracking()
            .Select(t => new { t.Id, t.FullName, t.IsArchived }).ToListAsync(ct);
        var courses = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);

        var members = await MembershipLifecycle.SeatHolders(db.StudentGroups, db.Students)
            .Where(sg => ids.Contains(sg.GroupId))
            .GroupBy(sg => sg.GroupId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        // "Attendance taken" — the single definition (SPEC P3.1, journal.md «YAGONA DAVOMAT TA'RIFI», AttendanceTaking):
        // ANY lesson note of (group, date) with a FIRST attendance mark (AttendanceTakenAt), subject/period agnostic —
        // the lesson monitor, the teacher reminders and the fines read the same. Not `Conducted`: one grade makes a
        // lesson «o'tildi» but takes no attendance.
        var taken = (await db.LessonNotes.AsNoTracking()
                .Where(n => n.AttendanceTakenAt != null && ids.Contains(n.ClassId)
                            && string.Compare(n.Date, lo) >= 0 && string.Compare(n.Date, hi) <= 0)
                .Select(n => new { n.Date, n.ClassId }).ToListAsync(ct))
            .Select(n => TakenKey(n.Date, n.ClassId))
            .ToHashSet(StringComparer.Ordinal);

        return new Input(
            groups, moves, assignments, rooms,
            teachers.ToDictionary(t => t.Id, t => t.FullName, StringComparer.Ordinal),
            teachers.Where(t => !t.IsArchived).Select(t => t.Id).ToList(),
            courses.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal),
            members, taken);
    }

    /// <summary>
    /// The dated DAY as a weekday-grid DTO, so the existing Excel export (<see cref="ScheduleGridExport.Sheets"/>)
    /// can print it unchanged: every lesson becomes one "group" on <paramref name="dayIndex"/> (0=Monday) with its
    /// real (possibly moved) time. Column keys/filters stay identical to the screen.
    /// </summary>
    public static ScheduleGridDto ToGrid(DatedScheduleDto d, int dayIndex) => new(
        d.Lessons.Select(l =>
        {
            var start = ScheduleRules.ParseTime(l.Start) ?? 0;
            var end = ScheduleRules.ParseTime(l.End) ?? start;
            var teacherId = l.SubstituteTeacherId.Length > 0 ? l.SubstituteTeacherId : l.TeacherId;
            var teacherName = l.SubstituteTeacherId.Length > 0 ? l.SubstituteTeacherName : l.TeacherName;
            return new ScheduleGridGroupDto(
                l.GroupId, l.GroupName, l.CourseId, l.CourseName, teacherId, teacherName,
                l.RoomId.StartsWith("name:", StringComparison.Ordinal) ? "" : l.RoomId, l.RoomName,
                [dayIndex], l.Start, l.End, end - start, l.Members, l.Capacity, l.Status);
        }).ToList(),
        d.Rooms.Select(r => new ScheduleOwnerDto(r.Id, r.Name, d.Lessons.Count(l => l.RoomId == r.Id))).ToList(),
        d.Teachers.Select(t => new ScheduleOwnerDto(t.Id, t.Name, d.Lessons.Count(l =>
            (l.SubstituteTeacherId.Length > 0 ? l.SubstituteTeacherId : l.TeacherId) == t.Id))).ToList(),
        d.SkippedGroups);
}
