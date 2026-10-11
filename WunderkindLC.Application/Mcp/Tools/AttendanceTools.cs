using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Student attendance: the daily absence page, lessons whose attendance is not taken, one student's
/// attendance over a period and the absence-reason catalogue.
/// Rules: .claude/rules/journal.md ("late" is NOT absent, RecordedAt, ExpectedOn), teacher-portal.md §3–4,
/// substitute-teachers.md (who teaches on a date). "Attendance taken" = a <see cref="LessonNote"/> with
/// <c>Conducted</c> on that date (the same definition as the panel — not redefined here).
/// </summary>
[McpServerToolType]
public sealed class AttendanceTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(LessonNote), typeof(JournalEntry), typeof(AbsenceReason), typeof(Group), typeof(Subject),
        typeof(Teacher), typeof(Room), typeof(Student), typeof(StudentGroup), typeof(LessonReschedule),
        typeof(SubstituteTeacherAssignment),
    ];

    // =====================================================================================
    //  attendance_daily — mirrors StudentAttendanceController.Absent (Server)
    // =====================================================================================

    [McpServerTool(Name = "attendance_daily", Title = "Kunlik davomat (kelmaganlar)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«O'quvchilar davomati» sahifasi bilan bir xil: tanlangan kun (sukut — bugun) uchun jamlanma — "
        + "davomat olingan guruhlar soni, shu kuni darsda kutilgan o'quvchilar, kelganlar (kech kelgan ham KELGAN), "
        + "kelmaganlar, sababsizlar, kechikkanlar — hamda kelmagan/kechikkan o'quvchilar ro'yxati: guruh, kurs, o'qituvchi, "
        + "dars vaqti, xona, sabab, o'quvchi va ota-ona telefonlari. Arxivlangan o'quvchi kirmaydi. groupId berilsa jamlanma "
        + "ham shu guruh bo'yicha. Ro'yxat sahifalangan. "
        + "Daily absent/late students with the same summary tiles as the panel's student attendance page.")]
    public async Task<string> AttendanceDaily(
        [Description("Sana YYYY-MM-DD (sukut — bugun)")] string? date = null,
        [Description("Faqat shu guruh (groups_list natijasidan id)")] string? groupId = null,
        [Description("true — ro'yxatda faqat kelmaganlar (kechikkanlarsiz); jamlanma o'zgarmaydi")] bool? onlyAbsent = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.StudentAttendance);
        var day = McpToolContext.Iso(McpToolContext.Date(date, McpToolContext.Today, "date"));
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var db = t.Db;

        // Lessons marked "conducted" on that day — the groups where attendance was taken.
        var notesQ = db.LessonNotes.Where(n => n.Date == day && n.Conducted);
        if (gid is not null) notesQ = notesQ.Where(n => n.ClassId == gid);
        var conductedGroupIds = await notesQ.Select(n => n.ClassId).Distinct().ToListAsync(ct);

        // Entries with an attendance reason on that day (absent or late).
        var entriesQ = db.JournalEntries.Where(e => e.Date == day && e.ReasonId != null);
        if (gid is not null) entriesQ = entriesQ.Where(e => e.ClassId == gid);
        var entries = await entriesQ.Select(e => new { e.StudentId, e.ClassId, e.ReasonId }).ToListAsync(ct);

        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, ct);
        var groupIds = entries.Select(e => e.ClassId).Concat(conductedGroupIds).Distinct().ToList();
        var groups = await db.Classes.Where(c => groupIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.CourseId, c.TeacherId, c.StartTime, c.EndTime, c.Room, c.RoomId })
            .ToListAsync(ct);
        var groupById = groups.ToDictionary(g => g.Id);

        var courseIds = groups.Select(g => g.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var roomIds = groups.Select(g => g.RoomId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var roomNames = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var studentIds = entries.Select(e => e.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id) && !s.IsArchived)
            .Select(s => new
            {
                s.Id, s.FullName, s.Phone, s.ParentFullName, s.ParentPhone, s.FatherPhone, s.MotherPhone,
            }).ToListAsync(ct);
        var studentById = students.ToDictionary(s => s.Id);

        // "N of M": only members EXPECTED in that day's lesson (StudentJournalBuilder.ExpectedOn — the same
        // bounds as the journal cell): frozen (after the freeze date), not yet joined and archived students
        // are not counted.
        var markedStudents = 0;
        if (conductedGroupIds.Count > 0)
        {
            var memberRows = await db.StudentGroups.Where(sg => conductedGroupIds.Contains(sg.GroupId)).ToListAsync(ct);
            var memberIds = memberRows.Select(m => m.StudentId).Distinct().ToList();
            var archivedIds = (await db.Students.Where(s => s.IsArchived && memberIds.Contains(s.Id))
                .Select(s => s.Id).ToListAsync(ct)).ToHashSet();
            markedStudents = memberRows
                .Where(m => !archivedIds.Contains(m.StudentId) && StudentJournalBuilder.ExpectedOn(m, day))
                .Select(m => m.StudentId).Distinct().Count();
        }

        var rows = new List<DailyRow>();
        foreach (var e in entries)
        {
            if (!studentById.TryGetValue(e.StudentId, out var st)) continue;   // archived / deleted
            if (!reasons.TryGetValue(e.ReasonId!, out var reason)) continue;
            groupById.TryGetValue(e.ClassId, out var g);
            rows.Add(new DailyRow(
                st.Id, st.FullName, st.Phone, st.ParentFullName, st.ParentPhone, st.FatherPhone, st.MotherPhone,
                e.ClassId, g?.Name ?? "",
                g is null || string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
                g is null || string.IsNullOrEmpty(g.TeacherId) ? "" : teacherNames.GetValueOrDefault(g.TeacherId, ""),
                g?.StartTime ?? "", g?.EndTime ?? "",
                g is null ? "" : RoomName(g.RoomId, g.Room, roomNames),
                reason.Id, reason.Name, reason.Short, reason.IsLate));
        }

        rows = rows
            .OrderBy(r => r.IsLate)                                     // absent first
            .ThenBy(r => r.StartTime, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StudentId, StringComparer.Ordinal)
            .ToList();

        // Present = students whose attendance was taken minus absent ones (distinct); late counts as PRESENT.
        var absentStudents = rows.Where(r => !r.IsLate).Select(r => r.StudentId).Distinct().Count();
        var unexcused = rows.Count(r => !r.IsLate
            && r.ReasonName.Contains("sababsiz", StringComparison.OrdinalIgnoreCase));

        var listed = onlyAbsent == true ? rows.Where(r => !r.IsLate).ToList() : rows;
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = listed.Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.StudentId, r.FullName, r.Phone,
            parent = new { name = r.ParentFullName, phone = r.ParentPhone, fatherPhone = r.FatherPhone, motherPhone = r.MotherPhone },
            group = new { id = r.GroupId, name = r.GroupName, course = r.CourseName, teacher = r.TeacherName },
            lesson = new { r.StartTime, r.EndTime, room = r.Room },
            reason = new { id = r.ReasonId, name = r.ReasonName, @short = r.ReasonShort, isLate = r.IsLate },
        }).ToList();

        return t.Json(new
        {
            date = day,
            scope = gid is null ? "center" : "group",
            groupId = gid,
            summary = new
            {
                conductedGroups = conductedGroupIds.Count,
                markedStudents,
                present = Math.Max(0, markedStudents - absentStudents),
                absent = rows.Count(r => !r.IsLate),
                absentStudents,
                unexcused,
                late = rows.Count(r => r.IsLate),
            },
            note = "«markedStudents» — davomat olingan guruhlarda shu kuni darsda kutilgan o'quvchilar; "
                + "«present» = markedStudents − kelmaganlar (kech kelgan KELGAN hisoblanadi).",
            total = listed.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    private sealed record DailyRow(
        string StudentId, string FullName, string Phone,
        string ParentFullName, string ParentPhone, string FatherPhone, string MotherPhone,
        string GroupId, string GroupName, string CourseName, string TeacherName,
        string StartTime, string EndTime, string Room,
        string ReasonId, string ReasonName, string ReasonShort, bool IsLate);

    /// <summary>Room as the lessons monitor shows it: the linked room's name, else the legacy text.</summary>
    private static string RoomName(string? roomId, string? roomText, IReadOnlyDictionary<string, string> names) =>
        !string.IsNullOrEmpty(roomId) ? names.GetValueOrDefault(roomId, roomText ?? "") : roomText ?? "";

    // =====================================================================================
    //  attendance_not_taken — mirrors DashboardController.TodayLessons (Server) + AttendanceWatch
    // =====================================================================================

    [McpServerTool(Name = "attendance_not_taken", Title = "Davomat qilinmagan guruhlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«Davomat qilinmagan guruhlar» monitori bilan bir xil: tanlangan kunda (sukut — bugun) darsi BOR, lekin "
        + "davomati hali olinmagan (dars «o'tildi» deb belgilanmagan) guruhlar. Dars kunlari ko'chirilgan darslar va guruh "
        + "umri bilan hisoblanadi; o'qituvchi — o'sha kuni darsni HAQIQATAN o'tadigan odam (o'rinbosar bo'lsa — o'rinbosar). "
        + "Har qatorda: guruh, kurs, o'qituvchi, xona, vaqt, bosqich (upcoming — hali boshlanmagan, started — ketyapti, "
        + "ended — tugagan), kechikkanmi va necha daqiqa kechikkani. Jamlanma: kundagi darslar, davomati olinganlar, "
        + "olinmaganlar, kechikkanlar. Kechikkanlar tepada. "
        + "Lessons of a day whose attendance has not been taken, with phase and minutes overdue.")]
    public async Task<string> AttendanceNotTaken(
        [Description("Sana YYYY-MM-DD (sukut — bugun)")] string? date = null,
        [Description("true — faqat kechikkanlar (dars boshlangan yoki tugagan, davomat yo'q)")] bool? onlyOverdue = null,
        [Description("O'qituvchi id: o'sha kuni darsni o'tadigan (o'rinbosar) yoki guruhning o'z o'qituvchisi")] string? teacherId = null,
        [Description("Faqat shu guruh (groups_list natijasidan id)")] string? groupId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.NotAttended);
        var dayDate = McpToolContext.Date(date, McpToolContext.Today, "date");
        var day = McpToolContext.Iso(dayDate);
        var db = t.Db;

        // Lesson days — the same source as the teacher's timetable (AttendanceWatch.LessonsOn →
        // TeacherHomeService.PlanLessons): moved lessons, the group's life span, substitutes.
        // Group.Days (List<int>) is filtered in memory — the group list is small.
        var allGroups = await db.Classes.Where(g => !g.IsArchived).ToListAsync(ct);
        var allIds = allGroups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules
                .Where(r => allIds.Contains(r.ClassId) && (r.FromDate == day || r.ToDate == day))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments
                .Where(a => a.IsActive && allIds.Contains(a.GroupId)
                            && string.Compare(a.Date, day) <= 0
                            && string.Compare(a.EndDate ?? a.Date, day) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);
        var planned = AttendanceWatch.LessonsOn(allGroups, moves, assignments, dayDate);

        var tid = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim();
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        if (gid is not null) planned = planned.Where(l => l.Group.Id == gid).ToList();
        if (tid is not null)
            planned = planned.Where(l => l.SubstituteTeacherId == tid || l.Group.TeacherId == tid).ToList();

        var groupIds = planned.Select(l => l.Group.Id).Distinct().ToList();
        var teacherIds = planned.Select(l => l.Group.TeacherId).Concat(planned.Select(l => l.SubstituteTeacherId))
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courseIds = planned.Select(l => l.Group.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var roomIds = planned.Select(l => l.Group.RoomId ?? "").Where(x => x.Length > 0).Distinct().ToList();

        var teacherNames = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var roomNames = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        // Active members per group (as the monitor shows).
        var membersByGroup = (await db.StudentGroups
                .Where(sg => sg.IsActive && groupIds.Contains(sg.GroupId))
                .GroupBy(sg => sg.GroupId)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);

        // Attendance taken = a lesson of that group marked "conducted" on that day.
        var attendanceDone = (await db.LessonNotes
                .Where(n => n.Date == day && n.Conducted && groupIds.Contains(n.ClassId))
                .Select(n => n.ClassId).Distinct().ToListAsync(ct)).ToHashSet();

        // Phase/overdue depend on the clock — computed per call (the panel does it after its cache too).
        var now = AppClock.Now;
        var lessons = planned.Select(l =>
        {
            var g = l.Group;
            var start = l.StartTime.Length > 0 ? l.StartTime : g.StartTime;   // moved lesson — the NEW time
            var end = l.EndTime.Length > 0 ? l.EndTime : g.EndTime;
            var done = attendanceDone.Contains(g.Id);
            var phase = AttendanceWatch.Phase(day, start, end, now);
            var overdue = AttendanceWatch.Overdue(done, phase);
            var substitute = l.SubstituteTeacherId.Length > 0;
            var actualId = substitute ? l.SubstituteTeacherId : g.TeacherId;
            var actualName = substitute
                ? teacherNames.GetValueOrDefault(l.SubstituteTeacherId, "O'rinbosar")
                : teacherNames.GetValueOrDefault(g.TeacherId, "Biriktirilmagan");
            return new
            {
                groupId = g.Id, groupName = g.Name,
                course = string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
                teacher = new { id = actualId, name = actualName },
                substitute,
                groupTeacher = new { id = g.TeacherId, name = teacherNames.GetValueOrDefault(g.TeacherId, "Biriktirilmagan") },
                room = RoomName(g.RoomId, g.Room, roomNames),
                startTime = start, endTime = end,
                moved = l.Moved,
                studentsCount = membersByGroup.GetValueOrDefault(g.Id, 0),
                attendanceDone = done,
                phase,
                phaseLabel = PhaseLabel(phase),
                overdue,
                minutesOverdue = overdue ? MinutesSince(dayDate, start, now) : null,
            };
        }).ToList();

        // The monitor's list: attendance not taken, overdue first, then by time and group name.
        var notTaken = lessons
            .Where(l => !l.attendanceDone && (onlyOverdue != true || l.overdue))
            .OrderBy(l => l.overdue ? 0 : 1)
            .ThenBy(l => string.IsNullOrEmpty(l.startTime) ? "99:99" : l.startTime, StringComparer.Ordinal)
            .ThenBy(l => l.groupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.groupId, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = notTaken.Skip((p - 1) * size).Take(size).ToList();

        return t.Json(new
        {
            date = day,
            summary = new
            {
                lessons = lessons.Count,
                attendanceDone = lessons.Count(l => l.attendanceDone),
                notTaken = lessons.Count(l => !l.attendanceDone),
                overdue = lessons.Count(l => l.overdue),
            },
            total = notTaken.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    private static string PhaseLabel(string phase) => phase switch
    {
        AttendanceWatch.PhaseUpcoming => "Hali boshlanmagan",
        AttendanceWatch.PhaseEnded => "Dars tugagan",
        _ => "Dars ketyapti",
    };

    /// <summary>Minutes since the lesson started (centre time); null when the start time is unknown.</summary>
    private static int? MinutesSince(DateOnly day, string startTime, DateTime now)
    {
        var start = ScheduleRules.ParseTime(startTime);
        if (start is null) return null;
        var startAt = day.ToDateTime(TimeOnly.MinValue).AddMinutes(start.Value);
        return Math.Max(0, (int)(now - startAt).TotalMinutes);
    }

    // =====================================================================================
    //  student_attendance — StudentJournalBuilder.PeriodAsync (the student app's core)
    // =====================================================================================

    [McpServerTool(Name = "student_attendance", Title = "O'quvchi davomati (davr bo'yicha)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchining sana oralig'idagi (sukut — oxirgi 30 kun, ko'pi bilan 366 kun) davomati: har o'tilgan "
        + "dars — sana, guruh, kurs, holat (present — keldi, absent — kelmadi, late — kech keldi, unmarked — belgilanmagan), "
        + "sabab va baho; hamda jami: o'tilgan darslar, kelgan, kelmagan, kech kelgan, davomat foizi va kurslar kesimi. "
        + "Hisob o'quvchi ilovasi va admin jurnal oynasi bilan BIR XIL (StudentJournalBuilder): davomat olingan darsda yozuvi "
        + "yo'q o'quvchi «keldi», a'zolik tizimga kiritilishidan oldingi belgilanmagan dars (beforeRecorded) jamiga kirmaydi, "
        + "muzlatish/chiqishdan keyingi darslar ko'rinmaydi. groupId bilan bitta guruhga toraytiriladi. Darslar yangisidan "
        + "eskisiga, sahifalangan. One student's per-lesson attendance and totals over a date range.")]
    public async Task<string> StudentAttendance(
        [Description("O'quvchi id (students_search natijasidan)")] string studentId,
        [Description("Boshlanish sanasi YYYY-MM-DD (sukut — 'to' dan 29 kun oldin)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Faqat shu guruh (o'quvchi a'zo bo'lgan guruh id)")] string? groupId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        // Either area is enough: the attendance page, or the student list/profile.
        if (!t.CanReadArea(McpAreas.Students)) t.Require(McpAreas.StudentAttendance);
        if (string.IsNullOrWhiteSpace(studentId)) throw new McpException("studentId kerak.");
        var (f, tt) = McpToolContext.Range(from, to);
        var sid = studentId.Trim();
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var db = t.Db;

        var student = await db.Students.Where(s => s.Id == sid)
            .Select(s => new { s.Id, s.FullName, s.IsArchived }).FirstOrDefaultAsync(ct);
        if (student is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        var journal = await StudentJournalBuilder.PeriodAsync(db, sid, McpToolContext.Iso(f), McpToolContext.Iso(tt), gid);
        if (journal is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        // PresentDefaultFrom (RecordedAt) per group — to flag the unmarked lessons the totals leave out.
        var memberships = await db.StudentGroups.Where(sg => sg.StudentId == sid).ToListAsync(ct);
        var recordedFrom = memberships
            .GroupBy(m => m.GroupId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => StudentJournalBuilder.BoundsOf(x.First()).PresentDefaultFrom, StringComparer.Ordinal);

        var sum = journal.Summary;
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var ordered = journal.Lessons
            .OrderByDescending(l => l.Date, StringComparer.Ordinal)
            .ThenByDescending(l => l.Period)
            .ThenBy(l => l.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.GroupId, StringComparer.Ordinal)
            .ToList();
        var items = ordered.Skip((p - 1) * size).Take(size).Select(l =>
        {
            var status = StatusOf(l.ReasonName, l.IsLate, l.Present, l.Grade);
            var pdf = recordedFrom.GetValueOrDefault(l.GroupId);
            return new
            {
                l.Date, l.Period,
                group = new { id = l.GroupId, name = l.GroupName },
                course = l.SubjectName,
                status,
                reason = l.ReasonName is null ? null : new { name = l.ReasonName, @short = l.ReasonShort, isLate = l.IsLate },
                grade = l.Grade,
                beforeRecorded = status == "unmarked" && pdf is not null && string.CompareOrdinal(l.Date, pdf) < 0,
            };
        }).ToList();

        return t.Json(new
        {
            student = new { student.Id, student.FullName, student.IsArchived },
            from = journal.From, to = journal.To,
            groupId = gid,
            groups = journal.Groups.Select(g => new { id = g.GroupId, name = g.GroupName, course = g.CourseName, teacher = g.TeacherName }),
            totals = new
            {
                conducted = sum.Held, attended = sum.Attended, absent = sum.Absent, late = sum.Late, percent = sum.AttendancePct,
                lessonsListed = journal.Lessons.Count,
                excludedFromTotals = journal.Lessons.Count - sum.Held,
            },
            byCourse = journal.Subjects.Select(c => new { course = c.SubjectName, conducted = c.Held, attended = c.Attended }),
            note = "«attended» — «keldi» belgisi yoki baho olgan darslar (o'quvchi ilovasi va admin jurnal oynasidagi hisob; "
                + "bahosiz «kech» dars bu yerda attended ga kirmaydi, «late» da sanaladi); «excludedFromTotals» — a'zolik "
                + "tizimga kiritilishidan oldingi belgilanmagan darslar, foizga kirmaydi.",
            total = ordered.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    /// <summary>One lesson's status from the journal cell: a reason wins (late / absent), then present (or graded).</summary>
    internal static string StatusOf(string? reasonName, bool isLate, bool present, int? grade) =>
        reasonName is not null ? (isLate ? "late" : "absent")
        : present || grade.HasValue ? "present"
        : "unmarked";

    // =====================================================================================
    //  absence_reasons — the catalogue (SettingsController.Get → AbsenceReasons)
    // =====================================================================================

    [McpServerTool(Name = "absence_reasons", Title = "Davomat sabablari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Davomat sabablari katalogi: id, nomi, qisqa belgisi va «kech» turidami (isLate — kech kelgan o'quvchi "
        + "KELGAN hisoblanadi, kelmaganlar sanog'iga kirmaydi). Avval kelmaslik sabablari, keyin «kech» turlari. "
        + "The absence-reason catalogue.")]
    public async Task<string> AbsenceReasons(
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.StudentAttendance);
        var db = t.Db;
        var total = await db.AbsenceReasons.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = await db.AbsenceReasons
            .OrderBy(r => r.IsLate).ThenBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(r => new { r.Id, r.Name, r.Short, r.IsLate })
            .ToListAsync(ct);
        return t.Json(new { total, page = p, pageSize = size, items }, items.Count);
    }
}
