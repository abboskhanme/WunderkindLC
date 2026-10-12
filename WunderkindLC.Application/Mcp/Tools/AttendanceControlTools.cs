using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Attendance CONTROL (CRM upgrade Phase 3, U3m — docs/SPEC-crm-upgrade.md P3.4–P3.6): the red list of consecutive
/// unexcused absences, the teachers' attendance discipline (taken on time / late / not taken) and the attendance fines.
///
/// <para>Definitions are the panel's, never re-derived:
/// <list type="bullet">
///   <item>red list — <see cref="AbsenceStreakLoader.ComputeAsync"/> (the same function as the «Qizil ro'yxat» tab, the KPI
///     and the automatic «Bog'lanish kerak» job): per (student, group), excused or late absence breaks the streak,
///     unmarked lessons are skipped; levels 3 and 5;</item>
///   <item>discipline — the lessons of <see cref="AttendanceDisciplineService"/> (visible groups, moved lessons at their new
///     time, the substitute teaches, a start time and a course, at least one expected student; the earliest lesson of the
///     group's day), "taken" = the first attendance mark (<see cref="AttendanceTaking.FirstMarksAsync"/>) and "on time" =
///     <see cref="AttendanceDiscipline.Decide"/> with the configured deadline;</item>
///   <item>fines — <see cref="AttendanceFineQueries.ListAsync"/> (Moliya → Jarima → «Davomat jarimalari»), money: finance only.</item>
/// </list></para>
/// </summary>
[McpServerToolType]
public sealed class AttendanceControlTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Group), typeof(LessonNote), typeof(StudentGroup), typeof(Student), typeof(JournalEntry), typeof(AbsenceReason),
        typeof(Teacher), typeof(Subject), typeof(ContactRequest), typeof(AbsenceStreakAlert), typeof(TeacherAttendanceFine),
        typeof(CenterMeta), typeof(LessonReschedule), typeof(SubstituteTeacherAssignment),
    ];

    /// <summary>Longest range of the discipline report.</summary>
    public const int MaxDisciplineDays = 31;

    private static readonly string[] DisciplineStatuses = ["problems", "all", "on_time", "late", "not_taken", "pending"];

    // =====================================================================================
    //  attendance_red_list — AttendanceRedListController (Server) over AbsenceStreakLoader
    // =====================================================================================

    [McpServerTool(Name = "attendance_red_list", Title = "Qizil ro'yxat (ketma-ket kelmaganlar)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("«O'quvchilar davomati → Qizil ro'yxat» bilan bir xil: ketma-ket SABABSIZ dars qoldirayotgan o'quvchilar "
        + "(oxirgi 60 kun). Seriya har (o'quvchi, guruh) bo'yicha alohida: sababli (uzrli) yo'qlik, «kech keldi» yoki «keldi» "
        + "seriyani to'xtatadi, davomati olinmagan dars o'tkazib yuboriladi. Har qatorda: o'quvchi, guruh, o'qituvchi, seriya "
        + "uzunligi, daraja (3 — 3+, 5 — 5+), qoldirilgan sanalar, oxirgi sana, yangiligi (oxirgi yo'qlik ≤ 14 kun) va "
        + "«Bog'lanish kerak» talabi (id, holati). Filtrlar: eng kam seriya (sukut 3), guruh, o'qituvchi. Eng uzun seriyalar "
        + "tepada. Natija 5 daqiqagacha keshlanadi. Students with consecutive unexcused absences (red list).")]
    public async Task<string> AttendanceRedList(
        [Description("Eng kam ketma-ket sababsiz darslar soni (sukut 3; 1 dan)")] int? min = null,
        [Description("Faqat shu guruh (groups_list natijasidan id)")] string? groupId = null,
        [Description("Faqat shu o'qituvchining (guruh egasi) guruhlari")] string? teacherId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.StudentAttendance);
        var today = McpToolContext.Today;
        var all = await t.CachedAsync("attendance:red-list:" + McpToolContext.Iso(today), TimeSpan.FromMinutes(5),
            () => BuildRedListAsync(today, ct));

        var threshold = Math.Max(1, min ?? AbsenceStreaks.Level3);
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var tid = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim();
        var list = all
            .Where(r => r.Streak >= threshold)
            .Where(r => gid is null || r.GroupId == gid)
            .Where(r => tid is null || r.TeacherId == tid)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = list.Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.StudentId, r.StudentName,
            group = new { id = r.GroupId, name = r.GroupName },
            teacher = new { id = r.TeacherId, name = r.TeacherName },
            streak = r.Streak, level = r.Level, r.Dates, r.LastDate, fresh = r.Fresh,
            contact = r.ContactRequestId is null ? null : new
            {
                requestId = r.ContactRequestId, status = r.ContactStatus, statusLabel = ContactService.StatusLabel(r.ContactStatus),
            },
        }).ToList();

        return t.Json(new
        {
            date = McpToolContext.Iso(today),
            min = threshold,
            summary = new
            {
                rows = list.Count,
                students = list.Select(r => r.StudentId).Distinct().Count(),
                level5 = list.Count(r => r.Level >= AbsenceStreaks.Level5),
                level3 = list.Count(r => r.Level == AbsenceStreaks.Level3),
            },
            note = "Seriya — eng oxirgi darsdan orqaga sanalgan SABABSIZ yo'qliklar (sababli yo'qlik, kech kelish yoki kelish "
                + "uni to'xtatadi). «fresh» — oxirgi yo'qlik 14 kun ichida: tizim faqat shunday seriyaga avtomatik «Bog'lanish "
                + "kerak» talabi ochadi. Natija 5 daqiqagacha eskirgan bo'lishi mumkin.",
            total = list.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    internal sealed record RedRow(
        string StudentId, string StudentName, string GroupId, string GroupName, string TeacherId, string TeacherName,
        int Streak, int Level, List<string> Dates, string LastDate, bool Fresh, string? ContactRequestId, string? ContactStatus);

    /// <summary>The panel's red list (AttendanceRedListController.BuildAsync) — the open request first, else the one this
    /// streak created.</summary>
    private async Task<List<RedRow>> BuildRedListAsync(DateOnly today, CancellationToken ct)
    {
        var db = t.Db;
        var rows = await AbsenceStreakLoader.ComputeAsync(db, today, ct);
        if (rows.Count == 0) return [];

        var teacherIds = rows.Select(r => r.TeacherId).Where(x => x.Length > 0).Distinct().ToList();
        var teachers = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var open = (await db.ContactRequests
                .Where(c => studentIds.Contains(c.StudentId)
                            && (c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback))
                .Select(c => new { c.Id, c.StudentId, c.Status })
                .ToListAsync(ct))
            .GroupBy(c => c.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id, StringComparer.Ordinal).First());
        var alerts = await db.AbsenceStreakAlerts
            .Where(a => studentIds.Contains(a.StudentId) && a.ContactRequestId != null)
            .Select(a => new { a.StudentId, a.GroupId, a.StreakStart, a.ContactRequestId })
            .ToListAsync(ct);
        var linkedIds = alerts.Select(a => a.ContactRequestId!).Distinct().ToList();
        var linked = await db.ContactRequests.Where(c => linkedIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Status }).ToDictionaryAsync(c => c.Id, c => c.Status, ct);

        return rows
            .OrderByDescending(r => r.Streak.Count)
            .ThenByDescending(r => r.Streak.LastDate, StringComparer.Ordinal)
            .ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StudentId, StringComparer.Ordinal)
            .ThenBy(r => r.GroupId, StringComparer.Ordinal)
            .Select(r =>
            {
                string? requestId = null, status = null;
                if (open.TryGetValue(r.StudentId, out var o)) { requestId = o.Id; status = o.Status; }
                else
                {
                    var a = alerts.FirstOrDefault(x => x.StudentId == r.StudentId && x.GroupId == r.GroupId
                                                       && x.StreakStart == r.Streak.StartDate);
                    if (a?.ContactRequestId is not null && linked.TryGetValue(a.ContactRequestId, out var st))
                    { requestId = a.ContactRequestId; status = st; }
                }
                return new RedRow(r.StudentId, r.StudentName, r.GroupId, r.GroupName, r.TeacherId,
                    teachers.GetValueOrDefault(r.TeacherId, ""), r.Streak.Count, r.Level, r.Streak.Dates.ToList(),
                    r.Streak.LastDate, AbsenceStreaks.IsFresh(r.Streak, today), requestId, status);
            })
            .ToList();
    }

    // =====================================================================================
    //  attendance_discipline — AttendanceDisciplineService's lessons + AttendanceDiscipline.Decide
    // =====================================================================================

    [McpServerTool(Name = "attendance_discipline", Title = "Davomat intizomi (o'z vaqtida olinmagan darslar)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'qituvchilar davomatni O'Z VAQTIDA olganmi — davomat intizomi xizmati bilan bir xil hisob: har dars uchun "
        + "boshlanish vaqti, muddat (boshlanish + sozlamadagi daqiqalar), davomat birinchi marta qachon olingani (birinchi "
        + "«keldi»/«kelmadi» belgisi) va holat: on_time — o'z vaqtida, late — kech olingan, not_taken — muddat o'tdi, hali "
        + "olinmagan, pending — muddat hali kelmagan. Darsni o'sha kuni o'tadigan o'qituvchi (o'rinbosar bo'lsa — o'rinbosar), "
        + "ko'chirilgan dars yangi vaqtida; boshlanish vaqti yoki kursi yo'q, kutilgan o'quvchisi yo'q guruhlar kirmaydi. "
        + "Jamlanma va O'QITUVCHILAR kesimi (darslar, o'z vaqtida, kech, olinmagan). Moliya ruxsati bo'lsa — yozilgan jarima "
        + "(summa, holati). Sukut — bugun va faqat muammolilar (late + not_taken); sana oralig'i ko'pi bilan 31 kun. "
        + "Whether teachers took attendance on time, per lesson and per teacher.")]
    public async Task<string> AttendanceDisciplineReport(
        [Description("Sana — dan, YYYY-MM-DD (sukut — bugun)")] string? from = null,
        [Description("Sana — gacha, YYYY-MM-DD (sukut — 'from')")] string? to = null,
        [Description("problems (sukut — late + not_taken) | all | on_time | late | not_taken | pending")] string? status = null,
        [Description("O'qituvchi id (darsni o'sha kuni o'tadigan yoki guruh egasi)")] string? teacherId = null,
        [Description("Faqat shu guruh")] string? groupId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.NotAttended);
        var st = (status ?? "problems").Trim().ToLowerInvariant();
        if (!DisciplineStatuses.Contains(st)) throw new McpException("«status»: " + string.Join(" | ", DisciplineStatuses) + ".");
        var fromDate = McpToolContext.Date(from, McpToolContext.Today, "from");
        var toDate = McpToolContext.Date(to, fromDate, "to");
        if (fromDate > toDate) throw new McpException("«from» sanasi «to» dan keyin bo'lishi mumkin emas.");
        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxDisciplineDays)
            throw new McpException($"Sana oralig'i ko'pi bilan {MaxDisciplineDays} kun bo'lishi mumkin.");
        var tid = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim();
        var gid = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var finance = t.CanReadFinance;
        var db = t.Db;
        var lo = McpToolContext.Iso(fromDate);
        var hi = McpToolContext.Iso(toDate);

        // Settings (CenterMeta allow-list columns only — the row also holds integration secrets).
        var meta = await db.CenterMeta.Select(m => new
        {
            m.AttendanceDisciplineEnabled, m.AttendanceDeadlineMinutes, m.AttendanceFineEnabled, m.AttendanceFineAmount,
        }).FirstOrDefaultAsync(ct);
        var defaults = new CenterMeta();
        var settings = new AttendanceSettingsDto(
            meta?.AttendanceDisciplineEnabled ?? defaults.AttendanceDisciplineEnabled,
            meta?.AttendanceDeadlineMinutes ?? defaults.AttendanceDeadlineMinutes,
            [], false,
            meta?.AttendanceFineEnabled ?? defaults.AttendanceFineEnabled,
            meta?.AttendanceFineAmount ?? defaults.AttendanceFineAmount,
            0, AttendanceSettingsRules.ApprovalLateOnly);

        // Lessons of the range — the discipline service's source (LessonAttendanceReminderService.LessonsTodayAsync for
        // each day), loaded ONCE for the whole range.
        var groups = await db.Classes.Where(TeacherGroupAccess.VisibleQuery).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var moves = (await db.LessonReschedules
                .Where(r => ids.Contains(r.ClassId)
                            && ((string.Compare(r.FromDate, lo) >= 0 && string.Compare(r.FromDate, hi) <= 0)
                                || (string.Compare(r.ToDate, lo) >= 0 && string.Compare(r.ToDate, hi) <= 0)))
                .ToListAsync(ct))
            .ToLookup(r => r.ClassId, StringComparer.Ordinal);
        var assignments = (await db.SubstituteTeacherAssignments
                .Where(a => a.IsActive && ids.Contains(a.GroupId)
                            && string.Compare(a.Date, hi) <= 0
                            && string.Compare(a.EndDate ?? a.Date, lo) >= 0)
                .ToListAsync(ct))
            .ToLookup(a => a.GroupId, StringComparer.Ordinal);

        var planned = new List<(string Date, AttendanceWatch.DayLesson Lesson)>();
        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            var date = McpToolContext.Iso(day);
            planned.AddRange(AttendanceWatch.LessonsOn(groups, moves, assignments, day)
                .Where(l => !string.IsNullOrEmpty(l.Group.CourseId) && ScheduleRules.ParseTime(l.StartTime) is not null)
                .GroupBy(l => l.Group.Id, StringComparer.Ordinal)
                .Select(g => (date, g.OrderBy(l => ScheduleRules.ParseTime(l.StartTime)).First())));
        }

        // Only lessons with at least one student EXPECTED that day (the service skips the rest).
        var plannedIds = planned.Select(x => x.Lesson.Group.Id).Distinct().ToList();
        var memberships = await db.StudentGroups.Where(sg => plannedIds.Contains(sg.GroupId)).ToListAsync(ct);
        var memberStudentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var archived = (await db.Students.Where(s => memberStudentIds.Contains(s.Id) && s.IsArchived)
            .Select(s => s.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var membersByGroup = memberships.Where(m => !archived.Contains(m.StudentId))
            .ToLookup(m => m.GroupId, StringComparer.Ordinal);
        var expected = planned
            .Select(x => (x.Date, x.Lesson, Expected: membersByGroup[x.Lesson.Group.Id]
                .Where(m => StudentJournalBuilder.ExpectedOn(m, x.Date)).Select(m => m.StudentId).Distinct().Count()))
            .Where(x => x.Expected > 0)
            .ToList();

        // First attendance mark per (group, date) and fines of the range — one query each.
        var marks = (await db.LessonNotes
                .Where(n => plannedIds.Contains(n.ClassId) && n.AttendanceTakenAt != null
                            && string.Compare(n.Date, lo) >= 0 && string.Compare(n.Date, hi) <= 0)
                .Select(n => new { n.ClassId, n.Date, n.AttendanceTakenAt })
                .ToListAsync(ct))
            .GroupBy(n => (n.ClassId, n.Date))
            .ToDictionary(g => g.Key, g => g.Min(x => x.AttendanceTakenAt!.Value));
        var fines = (await db.TeacherAttendanceFines
                .Where(f => plannedIds.Contains(f.GroupId)
                            && string.Compare(f.LessonDate, lo) >= 0 && string.Compare(f.LessonDate, hi) <= 0)
                .Select(f => new { f.GroupId, f.LessonDate, f.Amount, f.Status })
                .ToListAsync(ct))
            .GroupBy(f => (f.GroupId, f.LessonDate))
            .ToDictionary(g => g.Key, g => g.First());

        var teacherIds = expected.SelectMany(x => new[] { x.Lesson.Group.TeacherId ?? "", x.Lesson.SubstituteTeacherId })
            .Where(x => x.Length > 0).Distinct().ToList();
        var teacherNames = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var courseIds = expected.Select(x => x.Lesson.Group.CourseId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var now = AppClock.Now;
        var today = McpToolContext.Iso(McpToolContext.Today);
        var rows = new List<DisciplineRow>();
        foreach (var (date, l, exp) in expected)
        {
            var g = l.Group;
            DateTime? takenAt = marks.TryGetValue((g.Id, date), out var at) ? at : null;
            var d = AttendanceDiscipline.Decide(date, l.StartTime, now, takenAt, settings, isToday: date == today);
            if (d.Start is null) continue;
            var state = !d.Late ? (takenAt is not null ? "on_time" : "pending") : (takenAt is not null ? "late" : "not_taken");
            var teacher = LessonAttendanceReminderService.RecipientTeacherId(l);
            var fine = fines.GetValueOrDefault((g.Id, date));
            rows.Add(new DisciplineRow(
                date, g.Id, g.Name, courseNames.GetValueOrDefault(g.CourseId ?? "", ""),
                teacher, teacherNames.GetValueOrDefault(teacher, ""), l.SubstituteTeacherId.Length > 0, g.TeacherId ?? "",
                ScheduleRules.FormatTime(ScheduleRules.ParseTime(l.StartTime)!.Value), l.Moved, exp,
                d.Deadline!.Value, takenAt, state,
                takenAt is not null && state == "late" ? (int)Math.Ceiling((takenAt.Value - d.Deadline.Value).TotalMinutes) : null,
                fine?.Amount, fine?.Status));
        }

        if (tid is not null) rows = rows.Where(r => r.TeacherId == tid || r.GroupTeacherId == tid).ToList();
        if (gid is not null) rows = rows.Where(r => r.GroupId == gid).ToList();

        var byTeacher = rows
            .GroupBy(r => r.TeacherId)
            .Select(g => new
            {
                teacherId = g.Key, name = g.First().TeacherName,
                lessons = g.Count(),
                onTime = g.Count(r => r.State == "on_time"),
                late = g.Count(r => r.State == "late"),
                notTaken = g.Count(r => r.State == "not_taken"),
                pending = g.Count(r => r.State == "pending"),
                fines = finance ? g.Count(r => r.FineStatus == AttendanceDiscipline.FineActive) : (int?)null,
            })
            .OrderByDescending(x => x.late + x.notTaken).ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.teacherId, StringComparer.Ordinal)
            .ToList();

        var listed = rows
            .Where(r => st switch
            {
                "all" => true,
                "problems" => r.State is "late" or "not_taken",
                _ => r.State == st,
            })
            .OrderByDescending(r => r.Date, StringComparer.Ordinal)
            .ThenBy(r => r.Start, StringComparer.Ordinal)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.GroupId, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = listed.Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.Date,
            group = new { id = r.GroupId, name = r.GroupName, course = r.CourseName },
            teacher = new { id = r.TeacherId, name = r.TeacherName },
            substitute = r.Substitute,
            startTime = r.Start, moved = r.Moved, expectedStudents = r.Expected,
            deadline = r.Deadline.ToString("yyyy-MM-ddTHH:mm:ss"),
            attendanceTakenAt = r.TakenAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            status = r.State,
            statusLabel = StateLabel(r.State),
            minutesLate = r.MinutesLate,
            fine = !finance || r.FineAmount is null ? null : new { amount = r.FineAmount, status = r.FineStatus },
        }).ToList();

        return t.Json(new
        {
            from = lo, to = hi, status = st,
            settings = new
            {
                disciplineEnabled = settings.DisciplineEnabled,
                deadlineMinutes = settings.DeadlineMinutes,
                fineEnabled = settings.FineEnabled,
                fineAmount = finance ? settings.FineAmount : (decimal?)null,
            },
            summary = new
            {
                lessons = rows.Count,
                onTime = rows.Count(r => r.State == "on_time"),
                late = rows.Count(r => r.State == "late"),
                notTaken = rows.Count(r => r.State == "not_taken"),
                pending = rows.Count(r => r.State == "pending"),
            },
            byTeacher,
            note = (settings.DisciplineEnabled
                    ? "Davomat intizomi YOQILGAN: muddatdan keyin olingan yoki olinmagan darsga (jarima yoqilgan bo'lsa) jarima yoziladi. "
                    : "Davomat intizomi o'chiq (eslatma va jarima yo'q) — holatlar baribir shu muddat bo'yicha hisoblangan. ")
                + "«Olindi» — birinchi «keldi»/«kelmadi» belgisi (faqat baho qo'yilgani davomat emas)."
                + (finance ? "" : " Jarima summalari ko'rsatilmadi: moliya ruxsati yo'q."),
            total = listed.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    private sealed record DisciplineRow(
        string Date, string GroupId, string GroupName, string CourseName, string TeacherId, string TeacherName,
        bool Substitute, string GroupTeacherId, string Start, bool Moved, int Expected, DateTime Deadline,
        DateTime? TakenAt, string State, int? MinutesLate, decimal? FineAmount, string? FineStatus);

    private static string StateLabel(string state) => state switch
    {
        "on_time" => "O'z vaqtida olingan",
        "late" => "Kech olingan",
        "not_taken" => "Olinmagan",
        _ => "Muddat hali kelmagan",
    };

    // =====================================================================================
    //  teacher_attendance_fines — AttendanceFinesController.List (AttendanceFineQueries.ListAsync)
    // =====================================================================================

    [McpServerTool(Name = "teacher_attendance_fines", Title = "Davomat jarimalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'qituvchilarga davomatni o'z vaqtida olmagani uchun yozilgan jarimalar — «Moliya → Jarima → Davomat "
        + "jarimalari» bilan bir xil: o'qituvchi (o'sha kuni darsni o'tgan, o'rinbosar bo'lsa — o'rinbosar), guruh, dars sanasi "
        + "va vaqti, muddat, summa, holat (active — maoshdan ushlanadi, waived — bekor qilingan, kim/qachon/sabab), davomat "
        + "keyinroq qachon olingani. Jamlanma: faol jarimalar soni va summasi, o'qituvchilar kesimi. Jarima maoshdan shu oy "
        + "hisobida ushlanadi (teacher_salaries → attendanceFines). Filtrlar: oy (sukut — joriy), o'qituvchi, holat. "
        + "Moliya ruxsati kerak. Attendance fines of teachers (deducted from salary).")]
    public async Task<string> TeacherAttendanceFines(
        [Description("Oy YYYY-MM (sukut — joriy oy)")] string? month = null,
        [Description("O'qituvchi id")] string? teacherId = null,
        [Description("Holat: active | waived (sukut — hammasi)")] string? status = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Finance);
        var m = McpToolContext.Month(month);
        var stt = (status ?? "").Trim().ToLowerInvariant();
        if (stt.Length > 0 && stt is not (AttendanceDiscipline.FineActive or AttendanceDiscipline.FineWaived))
            throw new McpException("«status»: active | waived.");
        var tid = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim();

        var list = await AttendanceFineQueries.ListAsync(t.Db, m, tid, stt.Length == 0 ? null : stt, ct);
        var rows = list.Rows
            .OrderByDescending(r => r.LessonDate, StringComparer.Ordinal)
            .ThenBy(r => r.LessonStart, StringComparer.Ordinal)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var byTeacher = rows.GroupBy(r => r.TeacherId)
            .Select(g => new
            {
                teacherId = g.Key, name = g.First().TeacherName,
                active = g.Count(r => r.Status == AttendanceDiscipline.FineActive),
                activeAmount = g.Where(r => r.Status == AttendanceDiscipline.FineActive).Sum(r => r.Amount),
                waived = g.Count(r => r.Status == AttendanceDiscipline.FineWaived),
            })
            .OrderByDescending(x => x.activeAmount).ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.teacherId, StringComparer.Ordinal)
            .ToList();

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = rows.Skip((p - 1) * size).Take(size).Select(r => new
        {
            r.Id,
            teacher = new { id = r.TeacherId, name = r.TeacherName },
            group = new { id = r.GroupId, name = r.GroupName },
            r.LessonDate, r.LessonStart, r.Deadline, r.Amount, r.Status,
            statusLabel = r.Status == AttendanceDiscipline.FineActive ? "Faol (maoshdan ushlanadi)" : "Bekor qilingan",
            r.AttendanceTakenAt,
            waived = r.WaivedAt is null ? null : new { by = r.WaivedByName, at = r.WaivedAt, reason = r.WaiveReason },
        }).ToList();

        return t.Json(new
        {
            month = m,
            summary = new { totalActive = list.TotalActive, countActive = list.CountActive, rows = rows.Count },
            byTeacher,
            truncated = list.Rows.Count >= AttendanceFineQueries.MaxRows,
            total = rows.Count, page = p, pageSize = size,
            items,
        }, items.Count);
    }
}
