using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>Groups, rosters, the monthly journal summary, rooms and courses. Area: classes.list (+ rooms, courses).</summary>
[McpServerToolType]
public sealed class GroupTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Group), typeof(StudentGroup), typeof(Student), typeof(Subject), typeof(SubjectPriceBand),
        typeof(Teacher), typeof(Room), typeof(JournalEntry), typeof(LessonNote), typeof(LessonReschedule),
        typeof(AbsenceReason), typeof(MonthlyCharge), typeof(FinanceTransaction),
    ];

    private static readonly string[] DayNames = ["Du", "Se", "Ch", "Pa", "Ju", "Sh", "Ya"];

    internal static IEnumerable<string> DayLabels(IEnumerable<int> days) =>
        days.Where(d => d is >= 0 and <= 6).OrderBy(d => d).Select(d => DayNames[d]);

    [McpServerTool(Name = "groups_list", Title = "Guruhlar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Guruhlar ro'yxati: kurs, o'qituvchi, xona, dars kunlari va vaqti, sig'im, oylik narx hamda hozirgi a'zolar soni "
        + "(faol / sinovda / muzlatilgan). Filtrlar: nom bo'yicha qidirish, kurs, o'qituvchi, holat (faol/arxiv). Sahifalangan. "
        + "List groups with course, teacher, room, schedule and current member counts.")]
    public async Task<string> GroupsList(
        [Description("Guruh nomi bo'lagi")] string? search = null,
        [Description("Kurs id (courses_list dan)")] string? courseId = null,
        [Description("O'qituvchi id (teachers_list dan)")] string? teacherId = null,
        [Description("active (sukut) | archived | all")] string? state = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Groups);
        var db = t.Db;

        var q = db.Classes.AsQueryable();
        q = (state ?? "active").Trim().ToLowerInvariant() switch
        {
            "active" => q.Where(g => !g.IsArchived),
            "archived" => q.Where(g => g.IsArchived),
            "all" => q,
            _ => throw new McpException("«state»: active | archived | all."),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(g => g.Name.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(courseId)) q = q.Where(g => g.CourseId == courseId.Trim());
        if (!string.IsNullOrWhiteSpace(teacherId)) q = q.Where(g => g.TeacherId == teacherId.Trim());

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var groups = await q.OrderBy(g => g.Name).ThenBy(g => g.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var ids = groups.Select(g => g.Id).ToList();
        var counts = await (from sg in db.StudentGroups
                            join s in db.Students on sg.StudentId equals s.Id
                            where ids.Contains(sg.GroupId) && sg.IsActive && !s.IsArchived
                            group sg by new { sg.GroupId, sg.Status } into g
                            select new { g.Key.GroupId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var names = await NamesAsync(groups, ct);

        var items = groups.Select(g =>
        {
            int C(string status) => counts.Where(c => c.GroupId == g.Id && c.Status == status).Sum(c => c.Count);
            return new
            {
                g.Id, g.Name,
                course = names.Courses.GetValueOrDefault(g.CourseId),
                g.CourseId,
                teacher = names.Teachers.GetValueOrDefault(g.TeacherId),
                g.TeacherId,
                room = RoomOf(g, names.Rooms),
                days = g.Days.OrderBy(d => d),
                dayLabels = DayLabels(g.Days),
                g.StartTime, g.EndTime, g.StartDate, g.EndDate, g.Capacity,
                monthlyFee = g.MonthlyFee,
                members = new { active = C("active"), trial = C("trial"), frozen = C("frozen") },
                g.IsArchived, g.IsBlocked,
            };
        }).ToList();

        return t.Json(new { total, page = p, pageSize = size, items }, items.Count);
    }

    [McpServerTool(Name = "group_roster", Title = "Guruh a'zolari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta guruhning a'zolari: holati (faol/sinov/muzlatilgan; chiqqan/tugatgan), qo'shilgan, aktivlashgan, muzlatilgan va chiqqan "
        + "sanalari, telefonlar. Moliya ruxsati bo'lsa — SHU GURUH bo'yicha balans va qarzdor oylar soni. Guruh id yoki nomi bilan; "
        + "bir nechta guruh mos kelsa nomzodlar qaytadi. Group roster with membership dates and (with finance access) per-group balance.")]
    public async Task<string> GroupRoster(
        [Description("Guruh id (groups_list dan)")] string? groupId = null,
        [Description("Yoki guruh nomi (to'liq yoki bo'lagi)")] string? name = null,
        [Description("Guruhdan chiqqanlar ham ko'rsatilsinmi (sukut: yo'q)")] bool? includeLeft = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Groups);
        var db = t.Db;
        var (group, candidates) = await FindGroupAsync(groupId, name, ct);
        if (candidates is not null) return candidates;
        if (group is null) return t.Json(new { message = "Guruh topilmadi." }, 0);

        // Same rule as the panel's members list: an ARCHIVED student's still-open membership (old data)
        // is not a current member; a closed (historic) membership is shown.
        var gid = group.Id;   // `group` is a query keyword
        var all = await (from sg in db.StudentGroups
                         join s in db.Students on sg.StudentId equals s.Id
                         where sg.GroupId == gid && !(sg.IsActive && s.IsArchived)
                         orderby sg.IsActive descending, s.FullName
                         select new
                         {
                             s.Id, s.FullName, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone,
                             sg.IsActive, sg.Status, sg.YearFreeze, sg.JoinedAt, sg.ActivatedAt, sg.FrozenAt, sg.LeftAt,
                         }).ToListAsync(ct);
        // Counts cover the whole history even when the ended memberships are not listed.
        var counts = new
        {
            active = all.Count(r => MemberState(r.IsActive, r.Status) == "active"),
            trial = all.Count(r => MemberState(r.IsActive, r.Status) == "trial"),
            frozen = all.Count(r => MemberState(r.IsActive, r.Status) == "frozen"),
            left = all.Count(r => MemberState(r.IsActive, r.Status) == "left"),
            completed = all.Count(r => MemberState(r.IsActive, r.Status) == "completed"),
        };
        var rows = includeLeft == true ? all : all.Where(r => r.IsActive).ToList();

        var finance = t.CanReadFinance;
        // Per-group balance — the same source as the panel's members list (ClassesController.Members/Roster).
        var balances = finance
            ? await GroupBalanceService.DetailedForGroupAsync(db, group.Id, rows.Select(r => r.Id))
            : null;
        var names = await NamesAsync([group], ct);

        var members = rows.Select(r => new
        {
            studentId = r.Id, r.FullName, r.Phone,
            parentPhones = new[] { r.ParentPhone, r.FatherPhone, r.MotherPhone }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(),
            status = MemberState(r.IsActive, r.Status),
            r.YearFreeze, r.JoinedAt, r.ActivatedAt, r.FrozenAt, r.LeftAt,
            balance = balances is null ? (decimal?)null : balances.GetValueOrDefault(r.Id).Balance,
            debtMonths = balances is null ? (int?)null : balances.GetValueOrDefault(r.Id).DebtMonths,
        }).ToList();

        return t.Json(new
        {
            group = new
            {
                group.Id, group.Name,
                course = names.Courses.GetValueOrDefault(group.CourseId),
                teacher = names.Teachers.GetValueOrDefault(group.TeacherId),
                room = RoomOf(group, names.Rooms),
                dayLabels = DayLabels(group.Days), group.StartTime, group.EndTime, group.IsArchived,
            },
            counts,
            members,
            note = finance ? null : "Balans ko'rsatilmadi: moliya ruxsati yo'q.",
        }, members.Count);
    }

    [McpServerTool(Name = "group_journal", Title = "Guruh jurnali (oylik xulosa)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Guruhning bir oylik jurnali xulosasi: oyning dars sanalari (o'tilganmi, davomat olinganmi) va shu oyda guruhda "
        + "bo'lgan har o'quvchi bo'yicha o'tilgan darslar, keldi / kelmadi / kech qoldi / belgilanmagan soni, kelmaslik sabablari, "
        + "baholar soni va o'rtachasi. Qoidalar panel jurnali bilan bir xil: davomat olingan darsda yozuvi yo'q o'quvchi — keldi; "
        + "baho qo'yilgan yoki «kech qoldi» — keldi (kelmadi emas); a'zolikdan oldingi va muzlatish/chiqishdan keyingi darslar "
        + "hisoblanmaydi; a'zolik tizimga kiritilishidan oldingi belgisiz darslar — «unknown», hisobga kirmaydi. "
        + "Monthly journal summary of one group: lesson dates and per-student attendance and grades.")]
    public async Task<string> GroupJournal(
        [Description("Guruh id (groups_list dan)")] string groupId,
        [Description("Oy YYYY-MM (sukut — joriy oy)")] string? month = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Groups);
        var db = t.Db;
        var m = McpToolContext.Month(month);
        var id = (groupId ?? "").Trim();
        var group = id.Length == 0 ? null : await db.Classes.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return t.Json(new { message = "Guruh topilmadi." }, 0);

        var from = m + "-01";
        var to = m + "-99";
        var moves = await db.LessonReschedules.Where(r => r.ClassId == group.Id)
            .Select(r => new { r.FromDate, r.ToDate }).ToListAsync(ct);
        var dates = JournalService.EffectiveLessonDatesInMonth(group.Days, m,
            moves.Select(x => new JournalService.LessonMove(x.FromDate, x.ToDate)));

        // A journal exists only for a group with a course; rows of another (old) course are ignored —
        // the panel's rule (JournalService.GroupMonthAsync / StudentJournalBuilder).
        var subjectId = group.CourseId ?? "";
        var entries = string.IsNullOrEmpty(subjectId) ? [] : await db.JournalEntries
            .Where(e => e.ClassId == group.Id && e.SubjectId == subjectId
                        && e.Date.CompareTo(from) >= 0 && e.Date.CompareTo(to) <= 0)
            .ToListAsync(ct);
        var notes = string.IsNullOrEmpty(subjectId) ? [] : await db.LessonNotes
            .Where(n => n.ClassId == group.Id && n.SubjectId == subjectId
                        && n.Date.CompareTo(from) >= 0 && n.Date.CompareTo(to) <= 0)
            .ToListAsync(ct);
        var reasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, ct);
        var lessonByDate = StudentJournalBuilder.AggregateNotes(notes);

        var gid = group.Id;   // `group` is a query keyword
        var memberships = await (from sg in db.StudentGroups
                                 join s in db.Students on sg.StudentId equals s.Id
                                 where sg.GroupId == gid
                                 select new { Membership = sg, s.FullName, s.IsArchived }).ToListAsync(ct);
        // Panel rule (JournalService.GroupMonthAsync): an ARCHIVED student's still-open membership (old data)
        // is hidden from the current (and a later) month only — in past months it stays where they studied.
        var hideArchivedLive = string.CompareOrdinal(m, TuitionService.CurrentMonth()) >= 0;
        var entriesByStudent = entries.ToLookup(e => e.StudentId, StringComparer.Ordinal);

        var students = new List<object>();
        foreach (var row in memberships
                     .Where(x => !(x.Membership.IsActive && x.IsArchived && hideArchivedLive))
                     .Where(x => MemberDuring(x.Membership, m))
                     .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Membership.StudentId, StringComparer.Ordinal))
        {
            var bounds = StudentJournalBuilder.BoundsOf(row.Membership);
            var entryByDate = entriesByStudent[row.Membership.StudentId]
                .GroupBy(e => e.Date, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var cells = StudentJournalBuilder.BuildCells(group, bounds, dates, entryByDate, lessonByDate, reasons);

            // Counted = conducted, inside the membership, not "unknown" (a lesson before the membership was
            // actually entered, RecordedAt, with no mark) — the student journal's summary rule.
            var held = cells.Where(c => !c.Blocked && c.Conducted && !c.Unknown).ToList();
            var absentCells = held.Where(IsAbsent).ToList();
            var grades = cells.Where(c => c.Grade is not null).Select(c => c.Grade!.Value).ToList();
            var present = held.Count(IsPresent);
            students.Add(new
            {
                studentId = row.Membership.StudentId, row.FullName,
                status = MemberState(row.Membership.IsActive, row.Membership.Status),
                lessonsHeld = held.Count,
                present,
                late = held.Count(c => c.ReasonName is not null && c.IsLate),
                absent = absentCells.Count,
                unmarked = held.Count - present - absentCells.Count,
                unknown = cells.Count(c => !c.Blocked && c.Conducted && c.Unknown),
                absenceReasons = absentCells.GroupBy(c => c.ReasonName!)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new { reason = g.Key, count = g.Count() }),
                grades = new { count = grades.Count, average = grades.Count == 0 ? (double?)null : Math.Round(grades.Average(), 1) },
            });
        }

        var lessons = dates.Select(d =>
        {
            lessonByDate.TryGetValue(d, out var info);
            return new
            {
                date = d, conducted = info?.Conducted == true, attendanceTaken = info?.AttendanceTaken == true,
                topic = string.IsNullOrWhiteSpace(info?.Topic) ? null : info!.Topic,
            };
        }).ToList();

        var notesOut = new List<string>();
        if (string.IsNullOrEmpty(subjectId))
            notesOut.Add("Guruhga kurs biriktirilmagan — bunday guruhda jurnal yuritilmaydi.");
        if (group.Days.Count == 0)
            notesOut.Add("Guruhda dars kunlari belgilanmagan — oyning rejadagi dars sanalari yo'q.");

        return t.Json(new
        {
            group = new { group.Id, group.Name, group.IsArchived },
            month = m,
            lessons,
            summary = new
            {
                planned = lessons.Count,
                conducted = lessons.Count(l => l.conducted),
                attendanceTaken = lessons.Count(l => l.attendanceTaken),
            },
            students,
            note = notesOut.Count == 0 ? null : string.Join(" ", notesOut),
        }, students.Count);
    }

    /// <summary>
    /// Attended: an explicit "present" mark, the default "present" (attendance taken, no mark, after
    /// RecordedAt), a grade or a mastery mark (the group page shows those cells green), or a LATE reason —
    /// "late" is not "absent" (journal.md: the student was in the lesson).
    /// </summary>
    private static bool IsPresent(StudentJournalBuilder.Cell c) =>
        c.Present || c.Grade is not null || c.Mastery is not null || (c.ReasonName is not null && c.IsLate);

    /// <summary>Absent: a non-late reason on a cell without a grade/mastery mark.</summary>
    private static bool IsAbsent(StudentJournalBuilder.Cell c) =>
        c.ReasonName is not null && !c.IsLate && c.Grade is null && c.Mastery is null;

    [McpServerTool(Name = "rooms_list", Title = "Xonalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Xonalar: nomi, sig'imi, bino/qavat, faolligi va shu xonadagi arxivlanmagan guruhlar soni "
        + "(guruh xonasi — avval biriktirilgan xona, bo'lmasa eski matnli xona nomi; panel xonalar bandligi bilan bir xil). "
        + "Rooms with capacity and the number of groups using each.")]
    public async Task<string> RoomsList(
        [Description("Nofaol xonalar ham kirsinmi (sukut: yo'q)")] bool? includeInactive = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Rooms);
        var db = t.Db;
        var q = db.Rooms.AsQueryable();
        if (includeInactive != true) q = q.Where(r => r.IsActive);
        var rooms = await q.OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(ct);

        // Same matching as RoomUtilizationService (the panel's room dashboard): the linked RoomId first,
        // otherwise the legacy free-text Room by name (case-insensitive; the first room of a name wins).
        var groupRooms = await db.Classes
            .Where(g => !g.IsArchived && ((g.RoomId != null && g.RoomId != "") || (g.Room != null && g.Room != "")))
            .Select(g => new { g.RoomId, g.Room }).ToListAsync(ct);
        var roomByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rooms.OrderByDescending(r => r.IsActive)) roomByName.TryAdd(r.Name, r.Id);
        var usage = groupRooms
            .Select(g => !string.IsNullOrEmpty(g.RoomId) ? g.RoomId
                : roomByName.TryGetValue(g.Room!, out var rid) ? rid : null)
            .Where(id => id is not null)
            .GroupBy(id => id!)
            .ToDictionary(x => x.Key, x => x.Count());

        var items = rooms.Select(r => new
        {
            r.Id, r.Name, r.Capacity, r.Building, r.Location, r.IsActive,
            groups = usage.GetValueOrDefault(r.Id),
        }).ToList();
        return t.Json(new { total = items.Count, items }, items.Count);
    }

    [McpServerTool(Name = "courses_list", Title = "Kurslar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kurslar katalogi: oylik narx, bitta dars narxi, maktab sinfiga qarab narxlar (bo'lsa), arxivlanmagan guruhlar "
        + "va o'qituvchilar soni hamda hozirgi o'quvchilar soni holat bo'yicha (faol / sinovda / muzlatilgan / jami; bir o'quvchi "
        + "bir marta, arxivdagi o'quvchi kirmaydi — «Kurslar analitikasi» bilan bir xil). "
        + "Courses with prices, grade price bands, group, teacher and current student counts.")]
    public async Task<string> CoursesList(CancellationToken ct = default)
    {
        t.Require(McpAreas.Courses);
        var db = t.Db;
        var courses = await db.Subjects.OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(ct);
        var bands = await db.SubjectPriceBands.ToListAsync(ct);
        var groups = await db.Classes.Where(g => !g.IsArchived && g.CourseId != "")
            .Select(g => new { g.CourseId, g.TeacherId }).ToListAsync(ct);
        var groupsByCourse = groups.ToLookup(g => g.CourseId, StringComparer.Ordinal);

        // CourseAnalyticsController's "current" rule: a live membership (IsActive) of a non-archived student,
        // the course taken from the group (archived groups included — a closed group's frozen members still
        // count as frozen), one student once per course and state.
        var live = await (from sg in db.StudentGroups
                          join g in db.Classes on sg.GroupId equals g.Id
                          join s in db.Students on sg.StudentId equals s.Id
                          where sg.IsActive && !s.IsArchived && g.CourseId != ""
                          select new { g.CourseId, sg.StudentId, sg.Status }).Distinct().ToListAsync(ct);
        var liveByCourse = live.ToLookup(x => x.CourseId, StringComparer.Ordinal);
        int CountOf(string courseId, string? status) => liveByCourse[courseId]
            .Where(x => status is null || x.Status == status).Select(x => x.StudentId).Distinct().Count();

        var items = courses.Select(c => new
        {
            c.Id, c.Name, monthlyPrice = c.Price, c.LessonPrice,
            gradePrices = bands.Where(b => b.SubjectId == c.Id).OrderBy(b => b.GradeFrom)
                .Select(b => new { b.GradeFrom, b.GradeTo, monthlyPrice = b.Price, b.LessonPrice }),
            groups = groupsByCourse[c.Id].Count(),
            teachers = groupsByCourse[c.Id].Select(g => g.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count(),
            students = new
            {
                active = CountOf(c.Id, "active"),
                trial = CountOf(c.Id, "trial"),
                frozen = CountOf(c.Id, "frozen"),
                total = CountOf(c.Id, null),
            },
        }).ToList();
        return t.Json(new { total = items.Count, items }, items.Count);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Group? Group, string? Candidates)> FindGroupAsync(string? groupId, string? name, CancellationToken ct)
    {
        var db = t.Db;
        if (!string.IsNullOrWhiteSpace(groupId))
            return (await db.Classes.FirstOrDefaultAsync(g => g.Id == groupId.Trim(), ct), null);
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("groupId yoki name kerak.");

        var term = name.Trim().ToLower();
        var exact = await db.Classes.Where(g => g.Name.ToLower() == term).OrderBy(g => g.IsArchived).ToListAsync(ct);
        if (exact.Count == 1) return (exact[0], null);
        var matches = exact.Count > 1 ? exact
            : await db.Classes.Where(g => g.Name.ToLower().Contains(term)).OrderBy(g => g.IsArchived).ThenBy(g => g.Name)
                .Take(11).ToListAsync(ct);
        if (matches.Count == 1) return (matches[0], null);
        if (matches.Count == 0) return (null, null);
        return (null, t.Json(new
        {
            message = "Bir nechta guruh mos keldi — groupId bilan qayta so'rang.",
            candidates = matches.Take(10).Select(g => new { g.Id, g.Name, g.IsArchived }),
        }, matches.Count));
    }

    /// <summary>Membership state for output: the live status, or how an ended membership ended.</summary>
    private static string MemberState(bool isActive, string? status) =>
        isActive ? status ?? "trial" : status == "completed" ? "completed" : "left";

    /// <summary>
    /// Was the membership in the group during <paramref name="month"/> ("yyyy-MM")? Started by the month's end
    /// (earliest activation, else JoinedAt — <see cref="JournalService.MemberStart"/>) and not ended before the
    /// month: the end is the freeze date of a frozen membership, otherwise the leave date (the panel's
    /// <c>EndedVisibleIn</c> for ended memberships). An ENDED membership with no known end date is not shown
    /// (as in the panel); a live one with no end is.
    /// </summary>
    internal static bool MemberDuring(StudentGroup m, string month)
    {
        var start = JournalService.MemberStart(m);
        if (start is { Length: >= 7 } && string.CompareOrdinal(start[..7], month) > 0) return false;
        string? end;
        if (m.IsActive)
        {
            end = StudentJournalBuilder.BoundsOf(m).MemberEnd;
        }
        else
        {
            // Ended: the earlier of the freeze and the leave date (JournalService.EndOf).
            var left = m.LeftAt is { Length: >= 10 } l ? l[..10] : null;
            var frozen = m.FrozenAt is { Length: >= 10 } f ? f[..10] : null;
            end = frozen is not null && (left is null || string.CompareOrdinal(frozen, left) < 0) ? frozen : left;
            if (end is null) return false;
        }
        return end is not { Length: >= 7 } || string.CompareOrdinal(end[..7], month) >= 0;
    }

    private sealed record NameMaps(
        Dictionary<string, string> Courses, Dictionary<string, string> Teachers, Dictionary<string, string> Rooms);

    private async Task<NameMaps> NamesAsync(IReadOnlyCollection<Group> groups, CancellationToken ct)
    {
        var db = t.Db;
        var courseIds = groups.Select(g => g.CourseId).Distinct().ToList();
        var teacherIds = groups.Select(g => g.TeacherId).Distinct().ToList();
        var roomIds = groups.Where(g => g.RoomId != null).Select(g => g.RoomId!).Distinct().ToList();
        return new NameMaps(
            await db.Subjects.Where(s => courseIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct),
            await db.Teachers.Where(x => teacherIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct),
            await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct));
    }

    /// <summary>Room name: the linked room, otherwise the legacy free-text field.</summary>
    private static string? RoomOf(Group g, IReadOnlyDictionary<string, string> rooms) =>
        g.RoomId is not null && rooms.TryGetValue(g.RoomId, out var n) ? n
        : string.IsNullOrWhiteSpace(g.Room) ? null : g.Room;
}
