using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Teachers, panel staff accounts and substitute-teacher assignments.
/// Rules: .claude/rules/substitute-teachers.md (SelectedDates is the single source of truth — <see cref="SubstituteTeacherService.CoversDate"/>,
/// <see cref="SubstituteTeacherService.EffectiveDates"/>), staff-roles.md (panel accounts and role templates),
/// journal.md (BALL rating — <see cref="StudentBallService.TeacherAsync"/>), billing.md (salary settings = money).
/// Never output: the teacher's login / user id, <c>Permissions</c>, <c>DeviceUserId</c>, photo URL; user e-mail / password columns.
/// </summary>
[McpServerToolType]
public sealed class TeacherTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Teacher), typeof(Subject), typeof(Group), typeof(StudentGroup), typeof(Student), typeof(Room),
        typeof(SubstituteTeacherAssignment), typeof(LessonReschedule),
        // StudentBallService.TeacherAsync (the admin teacher page's "Reyting" tab)
        typeof(JournalEntry), typeof(CriterionGrade), typeof(StudentBallAdjustment), typeof(AbsenceReason), typeof(LessonNote),
        // staff_list — AppUser is granted column by column (McpReadOnlyGrants.RestrictedColumns)
        typeof(AppUser), typeof(StaffRoleTemplate),
    ];

    /// <summary>Panel accounts (StaffController.PanelRoles) — teachers / students / parents are not listed.</summary>
    private static readonly string[] PanelRoles = [Roles.SuperAdmin, Roles.Admin, Roles.Staff];

    /// <summary>Substitutions shown on the teacher card: ±this many days around today.</summary>
    private const int ProfileSubstitutionDays = 30;

    /// <summary>Rating rows shown on the teacher card (the panel's "Reyting" tab shows all; the AI gets the top).</summary>
    private const int ProfileRatingRows = 20;

    private const string SalaryNote = "Maosh sozlamalari ko'rsatilmadi: moliya ruxsati yo'q.";

    private sealed record TeacherRow(
        string Id, string FullName, string Phone, string Gender, List<string> SubjectIds, string Category,
        bool IsSupport, bool IsArchived, string? ArchivedAt, string? ArchiveReason, bool IsBlocked,
        string SalaryMode, decimal Salary, decimal SalaryPercent, decimal BonusPct, string SalaryStartMonth);

    /// <summary>Only non-sensitive columns (no UserId, Permissions, DeviceUserId, PhotoUrl).</summary>
    private static IQueryable<TeacherRow> Rows(IQueryable<Teacher> q) =>
        q.Select(x => new TeacherRow(
            x.Id, x.FullName, x.Phone, x.Gender, x.SubjectIds, x.Category,
            x.IsSupport, x.IsArchived, x.ArchivedAt, x.ArchiveReason, x.IsBlocked,
            x.SalaryMode, x.Salary, x.SalaryPercent, x.BonusPct, x.SalaryStartMonth));

    private static object? SalaryOf(TeacherRow r, bool finance) => finance
        ? new
        {
            mode = r.SalaryMode,
            @fixed = r.SalaryMode == "percent" ? (decimal?)null : r.Salary,
            percent = r.SalaryMode == "percent" ? r.SalaryPercent : (decimal?)null,
            bonusPct = r.BonusPct,
            startMonth = r.SalaryStartMonth,
        }
        : null;

    // =====================================================================================
    //  teachers_list — mirrors TeachersController.GetAll / GetArchived (Server)
    // =====================================================================================

    [McpServerTool(Name = "teachers_list", Title = "O'qituvchilar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'qituvchilar ro'yxati: telefon, fanlari (kurslar), toifasi, support belgisi, arxiv/blok holati, "
        + "arxivlanmagan guruhlari soni va shu guruhlardagi o'quvchilar soni (guruhdan chiqmagan, arxivda emas; ikki guruhdagi "
        + "o'quvchi bir marta). Maosh sozlamalari (rejim, qat'iy summa, foiz, ustama foizi) faqat moliya ruxsati bo'lsa. "
        + "Filtrlar: ism/telefon bo'yicha qidirish, holat (active — ishlayotgan, archived, blocked, all), kurs. Sahifalangan. "
        + "List teachers with subjects, group and student counts; salary settings only with finance access.")]
    public async Task<string> TeachersList(
        [Description("Ism yoki telefon bo'lagi")] string? search = null,
        [Description("active (sukut — arxivda emas, bloklanmagan) | archived | blocked | all")] string? state = null,
        [Description("Kurs id (courses_list dan) — shu fanni o'qitadiganlar")] string? courseId = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Teachers);
        var finance = t.CanReadFinance;
        var db = t.Db;

        var q = db.Teachers.AsQueryable();
        q = (state ?? "active").Trim().ToLowerInvariant() switch
        {
            "active" => q.Where(x => !x.IsArchived && !x.IsBlocked),
            "archived" => q.Where(x => x.IsArchived),
            "blocked" => q.Where(x => x.IsBlocked && !x.IsArchived),
            "all" => q,
            _ => throw new McpException("«state»: active | archived | blocked | all."),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            q = digits.Length >= 4
                ? q.Where(x => x.FullName.ToLower().Contains(term) || x.Phone.Contains(digits))
                : q.Where(x => x.FullName.ToLower().Contains(term));
        }

        // Teacher.SubjectIds is a List<string> column — the course filter runs in memory on the (small) teacher rows.
        var rows = await Rows(q.OrderBy(x => x.FullName).ThenBy(x => x.Id)).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(courseId))
        {
            var cid = courseId.Trim();
            rows = rows.Where(r => r.SubjectIds.Contains(cid)).ToList();
        }

        var total = rows.Count;
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var pageRows = rows.Skip((p - 1) * size).Take(size).ToList();

        var ids = pageRows.Select(r => r.Id).ToList();
        var (groupCounts, studentCounts) = await CountsAsync(ids, ct);
        var courseIds = pageRows.SelectMany(r => r.SubjectIds).Distinct().ToList();
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var items = pageRows.Select(r => new
        {
            r.Id, r.FullName, r.Phone,
            subjects = r.SubjectIds.Where(courseNames.ContainsKey).Select(id => new { id, name = courseNames[id] }),
            r.Category, r.IsSupport,
            groups = groupCounts.GetValueOrDefault(r.Id),
            students = studentCounts.GetValueOrDefault(r.Id),
            r.IsArchived, r.ArchivedAt, r.IsBlocked,
            salary = SalaryOf(r, finance),
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            note = finance ? null : SalaryNote,
            items,
        }, items.Count);
    }

    /// <summary>Non-archived groups per teacher and DISTINCT students with an open membership there (student not archived).</summary>
    private async Task<(Dictionary<string, int> Groups, Dictionary<string, int> Students)> CountsAsync(
        List<string> teacherIds, CancellationToken ct)
    {
        var db = t.Db;
        if (teacherIds.Count == 0) return (new(), new());
        var groups = await db.Classes.Where(g => !g.IsArchived && teacherIds.Contains(g.TeacherId))
            .GroupBy(g => g.TeacherId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var pairs = await (from sg in db.StudentGroups
                           join g in db.Classes on sg.GroupId equals g.Id
                           join s in db.Students on sg.StudentId equals s.Id
                           where !g.IsArchived && teacherIds.Contains(g.TeacherId) && sg.IsActive && !s.IsArchived
                           select new { g.TeacherId, sg.StudentId }).Distinct().ToListAsync(ct);
        var students = pairs.GroupBy(x => x.TeacherId).ToDictionary(x => x.Key, x => x.Count());
        return (groups, students);
    }

    // =====================================================================================
    //  teacher_profile — TeachersController.GetOne + groups + substitutions + Rating (Server)
    // =====================================================================================

    [McpServerTool(Name = "teacher_profile", Title = "O'qituvchi kartochkasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'qituvchi haqida: telefon, fanlari, toifasi, holati; arxivlanmagan guruhlari (kurs, dars kunlari, vaqti, "
        + "xona, a'zolar soni holat bo'yicha); bugundan ±30 kun ichidagi o'rinbosarlik tayinlovlari (u o'rinbosar yoki asosiy "
        + "o'qituvchi bo'lgan, faqat faollari); o'qituvchi sahifasidagi «Reyting» tabi bilan bir xil o'quvchilar reytingi (ball = "
        + "jurnal baholari + bajarilgan mezonlar; TOP-20 qator, umumiy yoki tanlangan oy). Maosh sozlamalari faqat moliya ruxsati "
        + "bilan. id yoki ism bo'yicha topiladi — bir nechta mos kelsa nomzodlar qaytadi. "
        + "Teacher card with groups, nearby substitutions and the students' ball rating.")]
    public async Task<string> TeacherProfile(
        [Description("O'qituvchi id (teachers_list dan)")] string? teacherId = null,
        [Description("Yoki to'liq/qisman ism")] string? name = null,
        [Description("Reyting oyi YYYY-MM (bo'sh — umumiy, barcha vaqt)")] string? ratingMonth = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Teachers);
        var month = string.IsNullOrWhiteSpace(ratingMonth) ? null : McpToolContext.Month(ratingMonth, "ratingMonth");
        var db = t.Db;

        TeacherRow? tr;
        if (!string.IsNullOrWhiteSpace(teacherId))
        {
            var id = teacherId.Trim();
            tr = await Rows(db.Teachers.Where(x => x.Id == id)).FirstOrDefaultAsync(ct);
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            var term = name.Trim().ToLower();
            var matches = await db.Teachers.Where(x => x.FullName.ToLower().Contains(term))
                .OrderBy(x => x.IsArchived).ThenBy(x => x.FullName).ThenBy(x => x.Id).Take(11)
                .Select(x => new { x.Id, x.FullName, x.Phone, x.IsArchived, x.IsBlocked }).ToListAsync(ct);
            if (matches.Count > 1)
                return t.Json(new
                {
                    message = "Bir nechta o'qituvchi mos keldi — teacherId bilan qayta so'rang.",
                    candidates = matches.Take(10),
                }, matches.Count);
            var only = matches.FirstOrDefault();
            tr = only is null ? null : await Rows(db.Teachers.Where(x => x.Id == only.Id)).FirstOrDefaultAsync(ct);
        }
        else throw new McpException("teacherId yoki name kerak.");
        if (tr is null) return t.Json(new { message = "O'qituvchi topilmadi." }, 0);

        var finance = t.CanReadFinance;
        var tid = tr.Id;

        // Groups (non-archived) with member counts by status — the same rule as groups_list.
        var groups = await db.Classes.Where(g => g.TeacherId == tid && !g.IsArchived)
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Select(g => new
            {
                g.Id, g.Name, g.CourseId, g.Days, g.StartTime, g.EndTime, g.RoomId, g.Room,
                g.StartDate, g.EndDate, g.Capacity, g.IsBlocked,
            }).ToListAsync(ct);
        var groupIds = groups.Select(g => g.Id).ToList();
        var counts = await (from sg in db.StudentGroups
                            join s in db.Students on sg.StudentId equals s.Id
                            where groupIds.Contains(sg.GroupId) && sg.IsActive && !s.IsArchived
                            group sg by new { sg.GroupId, sg.Status } into g
                            select new { g.Key.GroupId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var courseIds = groups.Select(g => g.CourseId).Concat(tr.SubjectIds).Distinct().ToList();
        var courseNames = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var roomIds = groups.Select(g => g.RoomId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var roomNames = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        // Substitutions around today (active only), as original or substitute.
        var today = McpToolContext.Today;
        var from = McpToolContext.Iso(today.AddDays(-ProfileSubstitutionDays));
        var to = McpToolContext.Iso(today.AddDays(ProfileSubstitutionDays));
        var subs = await SubstitutionsAsync(from, to, tid, null, "active", ct);

        // "Reyting" tab — StudentBallService.TeacherAsync (only Id / FullName are used).
        var rating = await StudentBallService.TeacherAsync(db, new Teacher { Id = tr.Id, FullName = tr.FullName }, month);

        return t.Json(new
        {
            tr.Id, tr.FullName, tr.Phone, tr.Gender,
            subjects = tr.SubjectIds.Where(courseNames.ContainsKey).Select(id => new { id, name = courseNames[id] }),
            tr.Category, tr.IsSupport, tr.IsArchived, tr.ArchivedAt, tr.ArchiveReason, tr.IsBlocked,
            salary = SalaryOf(tr, finance),
            note = finance ? null : SalaryNote,
            groups = groups.Select(g =>
            {
                int C(string status) => counts.Where(c => c.GroupId == g.Id && c.Status == status).Sum(c => c.Count);
                return new
                {
                    g.Id, g.Name,
                    course = courseNames.GetValueOrDefault(g.CourseId), g.CourseId,
                    dayLabels = GroupTools.DayLabels(g.Days),
                    g.StartTime, g.EndTime,
                    room = RoomName(g.RoomId, g.Room, roomNames),
                    g.StartDate, g.EndDate, g.Capacity, g.IsBlocked,
                    members = new { active = C("active"), trial = C("trial"), frozen = C("frozen") },
                };
            }),
            substitutions = new
            {
                from, to,
                items = subs.Select(s => new
                {
                    s.Id,
                    role = s.SubstituteTeacherId == tid ? "substitute" : "original",
                    group = new { id = s.GroupId, name = s.GroupName },
                    originalTeacher = new { id = s.OriginalTeacherId, name = s.OriginalTeacherName },
                    substituteTeacher = new { id = s.SubstituteTeacherId, name = s.SubstituteTeacherName },
                    s.DatesInRange, s.LessonCount, s.Reason,
                }),
            },
            rating = new
            {
                month = rating.Month.Length == 0 ? "umumiy" : rating.Month,
                rating.GroupsCount, rating.StudentsCount, rating.AverageBall, rating.RowsCount,
                note = "Ball = qo'lda qo'yilgan jurnal baholari + bajarilgan baholash mezonlari (+ tuzatishlar); "
                    + "o'rin — guruh ichida. Ro'yxat ball kamayishi bo'yicha, ko'pi bilan 20 qator.",
                top = rating.Rows.Take(ProfileRatingRows).Select(r => new
                {
                    r.Rank, r.StudentId, r.FullName,
                    group = new { id = r.GroupId, name = r.GroupName },
                    r.Ball, r.JournalTotal, r.CriteriaDone, averageGrade = r.Average, attendancePercent = r.Attendance,
                }),
            },
        }, 1);
    }

    // =====================================================================================
    //  staff_list — mirrors StaffController.GetAll (Server)
    // =====================================================================================

    [McpServerTool(Name = "staff_list", Title = "Xodimlar (panel akkauntlari)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Admin paneliga kiradigan akkauntlar (superadmin, admin, xodim): ism, telefon, lavozim, rol va xodim roli "
        + "(rol shabloni nomi; bo'sh — «rolsiz/individual»). Login, parol va ruxsatlar ro'yxati QAYTARILMAYDI. O'qituvchilar bu "
        + "yerda yo'q — ular teachers_list da. Filtrlar: rol, ism/telefon/lavozim bo'yicha qidirish. Tartib: superadminlar, "
        + "adminlar, keyin xodimlar (ism bo'yicha). Sahifalangan. "
        + "List panel accounts (superadmin/admin/staff) with position and staff role template.")]
    public async Task<string> StaffList(
        [Description("Rol: superadmin | admin | staff | all (sukut)")] string? role = null,
        [Description("Ism, telefon yoki lavozim bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Staff);
        var db = t.Db;

        string[] roles = (role ?? "all").Trim().ToLowerInvariant() switch
        {
            "all" or "" => PanelRoles,
            Roles.SuperAdmin => [Roles.SuperAdmin],
            Roles.Admin => [Roles.Admin],
            Roles.Staff => [Roles.Staff],
            _ => throw new McpException("«role»: superadmin | admin | staff | all."),
        };

        // ⚠️ AppUser is column-restricted for the read-only role: project ONLY the granted columns.
        var q = db.Users.Where(u => roles.Contains(u.Role))
            .Select(u => new { u.Id, u.FullName, u.Role, u.Phone, u.Position, u.RoleTemplateId });
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            q = digits.Length >= 4
                ? q.Where(u => u.FullName.ToLower().Contains(term) || u.Position.ToLower().Contains(term) || u.Phone.Contains(digits))
                : q.Where(u => u.FullName.ToLower().Contains(term) || u.Position.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q
            .OrderBy(u => u.Role == Roles.SuperAdmin ? 0 : u.Role == Roles.Admin ? 1 : 2)
            .ThenBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var templateIds = rows.Select(r => r.RoleTemplateId ?? "").Where(x => x.Length > 0).Distinct().ToList();
        var templates = await db.StaffRoleTemplates.Where(x => templateIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var items = rows.Select(r => new
        {
            r.Id, r.FullName, r.Phone, r.Position, r.Role,
            roleLabel = r.Role == Roles.SuperAdmin ? "Superadmin" : r.Role == Roles.Admin ? "Admin" : "Xodim",
            staffRole = !string.IsNullOrEmpty(r.RoleTemplateId) && templates.TryGetValue(r.RoleTemplateId, out var tn)
                ? new { id = r.RoleTemplateId, name = tn }
                : null,
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            note = "«staffRole» bo'sh — xodim rolga bog'lanmagan (individual ruxsatlar) yoki u admin/superadmin.",
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  substitutions_list — mirrors SubstituteTeachersController GET / (SubstituteTeacherService)
    // =====================================================================================

    [McpServerTool(Name = "substitutions_list", Title = "O'rinbosar o'qituvchilar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'rinbosar o'qituvchi tayinlovlari sana oralig'ida (sukut: bugundan 30 kun oldin … 30 kun keyin, ko'pi bilan "
        + "366 kun): guruh, asosiy o'qituvchi, o'rinbosar, tanlangan dars sanalari (va ulardan oraliqqa tushganlari), darslar soni "
        + "(ko'chirilgan darslar va guruh umri bilan — panel bilan bir xil), sabab, faol/bekor qilingan. Tayinlov oraliqqa faqat "
        + "TANLANGAN sanalaridan biri tushsa kiradi. Filtrlar: o'qituvchi (asosiy yoki o'rinbosar), guruh, holat. Maosh summalari "
        + "bu vositada yo'q. Sahifalangan. "
        + "Substitute-teacher assignments overlapping a date range.")]
    public async Task<string> SubstitutionsList(
        [Description("Boshlanish sanasi YYYY-MM-DD (sukut — bugundan 30 kun oldin)")] string? from = null,
        [Description("Tugash sanasi YYYY-MM-DD (sukut — bugundan 30 kun keyin)")] string? to = null,
        [Description("O'qituvchi id — asosiy yoki o'rinbosar sifatida qatnashgan")] string? teacherId = null,
        [Description("Guruh id")] string? groupId = null,
        [Description("active (sukut) | cancelled (bekor qilingan) | all")] string? state = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Substitutions);
        var today = McpToolContext.Today;
        var (f, tt) = McpToolContext.Range(
            string.IsNullOrWhiteSpace(from) ? McpToolContext.Iso(today.AddDays(-30)) : from,
            string.IsNullOrWhiteSpace(to) ? McpToolContext.Iso(today.AddDays(30)) : to);
        var st = (state ?? "active").Trim().ToLowerInvariant();
        if (st is not ("active" or "cancelled" or "all"))
            throw new McpException("«state»: active | cancelled | all.");
        var fromIso = McpToolContext.Iso(f);
        var toIso = McpToolContext.Iso(tt);

        var all = await SubstitutionsAsync(fromIso, toIso,
            string.IsNullOrWhiteSpace(teacherId) ? null : teacherId.Trim(),
            string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim(), st, ct);

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var items = all.Skip((p - 1) * size).Take(size).Select(s => new
        {
            s.Id,
            group = new { id = s.GroupId, name = s.GroupName },
            originalTeacher = new { id = s.OriginalTeacherId, name = s.OriginalTeacherName },
            substituteTeacher = new { id = s.SubstituteTeacherId, name = s.SubstituteTeacherName },
            s.Date, s.EndDate, s.Dates, s.DatesInRange, s.LessonCount,
            s.Reason, s.IsActive, s.CreatedBy, s.CreatedAt,
        }).ToList();

        return t.Json(new
        {
            from = fromIso, to = toIso,
            total = all.Count, page = p, pageSize = size,
            note = "«dates» — admin tanlagan sanalar; «lessonCount» — haqiqiy darslar soni (ko'chirishlar va guruh umri bilan).",
            items,
        }, items.Count);
    }

    private sealed record SubstitutionRow(
        string Id, string GroupId, string GroupName,
        string OriginalTeacherId, string OriginalTeacherName,
        string SubstituteTeacherId, string SubstituteTeacherName,
        string Date, string? EndDate, List<string> Dates, List<string> DatesInRange, int LessonCount,
        string Reason, bool IsActive, string CreatedBy, DateTime CreatedAt);

    /// <summary>
    /// Assignments with at least one covered date in [from, to]. Two steps, like
    /// <see cref="SubstituteTeacherService.GetAssignmentsPageAsync"/>: a coarse SQL filter on Date..EndDate (SelectedDates
    /// always lie inside it), then the exact rule in memory — with SelectedDates only those days count
    /// (<see cref="SubstituteTeacherService.CoversDate"/> semantics); a legacy range row covers its whole range.
    /// </summary>
    private async Task<List<SubstitutionRow>> SubstitutionsAsync(
        string from, string to, string? teacherId, string? groupId, string state, CancellationToken ct)
    {
        var db = t.Db;
        var q = db.SubstituteTeacherAssignments.Where(a =>
            string.Compare(a.Date, to) <= 0
            && string.Compare(a.EndDate == null || a.EndDate == "" ? a.Date : a.EndDate, from) >= 0);
        if (teacherId is not null) q = q.Where(a => a.SubstituteTeacherId == teacherId || a.OriginalTeacherId == teacherId);
        if (groupId is not null) q = q.Where(a => a.GroupId == groupId);
        if (state == "active") q = q.Where(a => a.IsActive);
        else if (state == "cancelled") q = q.Where(a => !a.IsActive);

        var list = (await q.ToListAsync(ct))
            .Where(a => a.SelectedDates is not { Count: > 0 }
                        || a.SelectedDates.Any(d => d is not null && InRange(d.Trim(), from, to)))
            .ToList();
        if (list.Count == 0) return [];

        var groupIds = list.Select(a => a.GroupId).Distinct().ToList();
        var groups = await db.Classes.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var moves = await SubstituteTeacherService.MovesByGroupAsync(db, groupIds, ct);
        var teacherIds = list.SelectMany(a => new[] { a.OriginalTeacherId, a.SubstituteTeacherId }).Distinct().ToList();
        var names = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        return list.Select(a =>
            {
                var g = groups.GetValueOrDefault(a.GroupId);
                var mv = moves.GetValueOrDefault(a.GroupId);
                var dates = SubstituteTeacherService.SummaryDates(a, g, mv);
                return new SubstitutionRow(
                    a.Id, a.GroupId, g?.Name ?? "Noma'lum guruh",
                    a.OriginalTeacherId, names.GetValueOrDefault(a.OriginalTeacherId, "Noma'lum o'qituvchi"),
                    a.SubstituteTeacherId, names.GetValueOrDefault(a.SubstituteTeacherId, "Noma'lum o'qituvchi"),
                    a.Date, string.IsNullOrEmpty(a.EndDate) ? null : a.EndDate,
                    dates, dates.Where(d => InRange(d, from, to)).ToList(),
                    SubstituteTeacherService.EffectiveDates(a, g, mv).Count,
                    a.Reason, a.IsActive, a.CreatedBy, a.CreatedAt);
            })
            .OrderBy(r => r.DatesInRange.FirstOrDefault() ?? r.Date, StringComparer.Ordinal)
            .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool InRange(string date, string from, string to) =>
        string.CompareOrdinal(date, from) >= 0 && string.CompareOrdinal(date, to) <= 0;

    /// <summary>Room as the panel shows it: the linked room's name, else the legacy text.</summary>
    private static string RoomName(string? roomId, string? roomText, IReadOnlyDictionary<string, string> names) =>
        !string.IsNullOrEmpty(roomId) ? names.GetValueOrDefault(roomId, roomText ?? "") : roomText ?? "";
}
