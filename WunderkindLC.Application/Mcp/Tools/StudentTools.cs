using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>Students: search and profile. Area: students.list; money only with finance access.</summary>
[McpServerToolType]
public sealed class StudentTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Student), typeof(StudentGroup), typeof(Group), typeof(Subject), typeof(Teacher),
        typeof(TelegramRegistration), typeof(JournalEntry), typeof(AbsenceReason), typeof(LessonNote),
    ];

    [McpServerTool(Name = "students_search", Title = "O'quvchilarni qidirish",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchilar ro'yxati: ism yoki telefon bo'yicha qidirish; holat (faol/sinov/muzlatilgan), guruh, arxiv va "
        + "qarz filtrlari. Har qatorda guruhlari, maktab sinfi va Telegram holati: botga ulangan (bloklamagan) chatlar soni, "
        + "ular kimniki (father — otasi, mother — onasi, parent — ota-onasi, student — o'zi, other — boshqa) va botni "
        + "bloklaganlar soni (chat id chiqmaydi). "
        + "Balans faqat moliya ruxsati bo'lsa. Sahifalangan (ko'pi bilan 200 qator). "
        + "Search students by name/phone with status, group, archive and debt filters.")]
    public async Task<string> StudentsSearch(
        [Description("Ism, familiya yoki telefon bo'lagi")] string? search = null,
        [Description("active (sukut — arxivda emaslar) | archived | all")] string? state = null,
        [Description("A'zolik holati: active | trial | frozen | none (guruhsiz)")] string? memberState = null,
        [Description("Guruh id (groups_list natijasidan)")] string? groupId = null,
        [Description("Faqat qarzdorlar: true (moliya ruxsati kerak)")] bool? debtorsOnly = null,
        [Description("Telegram: linked (bloklanmagan chat bor) | unlinked")] string? telegram = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var finance = t.CanReadFinance;
        if (debtorsOnly == true && !finance) t.Require(McpAreas.Finance);
        var db = t.Db;

        var q = db.Students.AsQueryable();
        q = (state ?? "active").Trim().ToLowerInvariant() switch
        {
            "archived" => q.Where(s => s.IsArchived),
            "all" => q,
            "active" => q.Where(s => !s.IsArchived),
            _ => throw new McpException("«state»: active | archived | all."),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var digits = new string(term.Where(char.IsDigit).ToArray());
            q = digits.Length >= 4
                ? q.Where(s => s.FullName.ToLower().Contains(term) || s.Phone.Contains(digits)
                               || s.ParentPhone.Contains(digits) || s.FatherPhone.Contains(digits)
                               || s.MotherPhone.Contains(digits))
                : q.Where(s => s.FullName.ToLower().Contains(term));
        }
        if (debtorsOnly == true) q = q.Where(s => s.Balance < 0);

        // Live memberships (open membership in a non-archived group) for the students in scope.
        var live = from sg in db.StudentGroups
                   join g in db.Classes on sg.GroupId equals g.Id
                   where sg.IsActive && !g.IsArchived
                   select new { sg.StudentId, sg.GroupId, GroupName = g.Name, sg.Status, sg.YearFreeze };

        if (!string.IsNullOrWhiteSpace(groupId))
        {
            var gid = groupId.Trim();
            q = q.Where(s => live.Any(m => m.StudentId == s.Id && m.GroupId == gid));
        }
        switch ((memberState ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case "active": q = q.Where(s => live.Any(m => m.StudentId == s.Id && m.Status == "active")); break;
            case "trial":
                q = q.Where(s => live.Any(m => m.StudentId == s.Id && m.Status == "trial")
                                 && !live.Any(m => m.StudentId == s.Id && m.Status == "active"));
                break;
            case "frozen":
                q = q.Where(s => live.Any(m => m.StudentId == s.Id && m.Status == "frozen")
                                 && !live.Any(m => m.StudentId == s.Id && (m.Status == "active" || m.Status == "trial")));
                break;
            case "none": q = q.Where(s => !live.Any(m => m.StudentId == s.Id)); break;
            default: throw new McpException("«memberState»: active | trial | frozen | none.");
        }
        switch ((telegram ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            // Linked = at least one USABLE chat (not blocked) — the panel's «Telegram» column (TelegramLinkSummary).
            case "linked": q = q.Where(s => db.TelegramRegistrations.Any(r => r.StudentId == s.Id && r.BlockedAt == null)); break;
            case "unlinked": q = q.Where(s => !db.TelegramRegistrations.Any(r => r.StudentId == s.Id && r.BlockedAt == null)); break;
            default: throw new McpException("«telegram»: linked | unlinked.");
        }

        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderBy(s => s.FullName).ThenBy(s => s.Id).Skip((p - 1) * size).Take(size)
            .Select(s => new
            {
                s.Id, s.FullName, s.Phone, s.ParentFullName, s.ParentPhone, s.FatherPhone, s.MotherPhone,
                s.SchoolGrade, s.SchoolGradeYear, s.EnrollmentDate, s.IsArchived, s.ArchivedAt, s.ArchiveReason,
                s.Balance,
            }).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var memberships = await live.Where(m => ids.Contains(m.StudentId)).ToListAsync(ct);
        var tgLinks = await TelegramLinksAsync(ids, ct);

        var items = rows.Select(r =>
        {
            var own = memberships.Where(m => m.StudentId == r.Id).ToList();
            return new
            {
                r.Id, r.FullName, r.Phone,
                parent = new { name = r.ParentFullName, phone = r.ParentPhone, fatherPhone = r.FatherPhone, motherPhone = r.MotherPhone },
                schoolGrade = SchoolGrades.Current(r.SchoolGrade, r.SchoolGradeYear, McpToolContext.Today),
                r.EnrollmentDate,
                memberState = StateOf(own.Select(m => (m.Status, m.YearFreeze))),
                groups = own.Select(m => new { id = m.GroupId, name = m.GroupName, status = m.Status }),
                telegramLinked = tgLinks.GetValueOrDefault(r.Id)?.Linked > 0,
                telegram = TelegramOf(tgLinks.GetValueOrDefault(r.Id)),
                r.IsArchived, r.ArchivedAt, r.ArchiveReason,
                balance = finance ? r.Balance : (decimal?)null,
            };
        }).ToList();

        return t.Json(new
        {
            total, page = p, pageSize = size,
            note = finance ? null : "Balans ko'rsatilmadi: moliya ruxsati yo'q.",
            items,
        }, items.Count);
    }

    [McpServerTool(Name = "student_profile", Title = "O'quvchi kartochkasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta o'quvchi haqida to'liq ma'lumot: guruhlari (holati, qo'shilgan/aktivlashgan sana, o'qituvchi, kurs), "
        + "ota-ona, maktab sinfi, davomat xulosasi (sabablar bo'yicha), oyma-oy baholar, Telegram botga ulangan chatlar (qarindoshlik bo'yicha, bloklaganlar alohida; yutuqlar — "
        + "student_achievements, Telegram xabarlari — message_deliveries); "
        + "moliya ruxsati bo'lsa balans. id yoki ism bo'yicha topiladi — bir nechta mos kelsa, nomzodlar ro'yxati qaytadi. "
        + "Full student card by id or name.")]
    public async Task<string> StudentProfile(
        [Description("O'quvchi id (students_search natijasidan)")] string? studentId = null,
        [Description("Yoki to'liq/qisman ism")] string? name = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var db = t.Db;
        Student? st;
        if (!string.IsNullOrWhiteSpace(studentId))
            st = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId.Trim(), ct);
        else if (!string.IsNullOrWhiteSpace(name))
        {
            var term = name.Trim().ToLower();
            var matches = await db.Students.Where(s => s.FullName.ToLower().Contains(term))
                .OrderBy(s => s.IsArchived).ThenBy(s => s.FullName).Take(11)
                .Select(s => new { s.Id, s.FullName, s.Phone, s.IsArchived }).ToListAsync(ct);
            if (matches.Count > 1)
                return t.Json(new
                {
                    message = "Bir nechta o'quvchi mos keldi — studentId bilan qayta so'rang.",
                    candidates = matches.Take(10),
                }, matches.Count);
            var only = matches.FirstOrDefault();
            st = only is null ? null : await db.Students.FirstOrDefaultAsync(s => s.Id == only.Id, ct);
        }
        else throw new McpException("studentId yoki name kerak.");
        if (st is null) return t.Json(new { message = "O'quvchi topilmadi." }, 0);

        var nb = await StudentProfileBuilder.BuildAsync(db, st);
        var finance = t.CanReadFinance;

        var memberships = await (from sg in db.StudentGroups
                                 join g in db.Classes on sg.GroupId equals g.Id
                                 where sg.StudentId == st.Id
                                 orderby sg.IsActive descending, sg.JoinedAt descending
                                 select new
                                 {
                                     groupId = g.Id, groupName = g.Name, g.CourseId, g.TeacherId, groupArchived = g.IsArchived,
                                     sg.Status, sg.IsActive, sg.YearFreeze, sg.JoinedAt, sg.ActivatedAt, sg.FrozenAt, sg.LeftAt,
                                 }).ToListAsync(ct);
        var courseIds = memberships.Select(m => m.CourseId).Distinct().ToList();
        var teacherIds = memberships.Select(m => m.TeacherId).Distinct().ToList();
        var courses = await db.Subjects.Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teachers = await db.Teachers.Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var telegram = (await TelegramLinksAsync([st.Id], ct)).GetValueOrDefault(st.Id);

        var subjectName = nb.Subjects.ToDictionary(s => s.Id, s => s.Name);
        var grades = nb.Grades.Select(g => new
        {
            subject = subjectName.GetValueOrDefault(g.Key, g.Key),
            months = g.Value.OrderBy(m => m.Key).ToDictionary(m => m.Key, m => Math.Round(m.Value, 2)),
        }).ToList();

        return t.Json(new
        {
            st.Id, st.FullName, st.Phone, st.BirthDate, st.Address, st.EnrollmentDate,
            schoolGrade = st.SchoolGradeNow,
            st.IsArchived, st.ArchivedAt, st.ArchiveReason,
            parent = new
            {
                name = st.ParentFullName, phone = st.ParentPhone,
                father = new { name = st.FatherFullName, phone = st.FatherPhone },
                mother = new { name = st.MotherFullName, phone = st.MotherPhone },
            },
            memberState = StateOf(memberships.Where(m => m.IsActive && !m.groupArchived).Select(m => (m.Status, m.YearFreeze))),
            groups = memberships.Select(m => new
            {
                m.groupId, name = m.groupName,
                course = courses.GetValueOrDefault(m.CourseId),
                teacher = teachers.GetValueOrDefault(m.TeacherId),
                status = m.IsActive ? m.Status : "left",
                m.YearFreeze, m.JoinedAt, m.ActivatedAt, m.FrozenAt, m.LeftAt, m.groupArchived,
            }),
            telegram = new
            {
                linked = telegram?.Linked > 0,
                chats = telegram?.Linked ?? 0,
                blocked = telegram?.Blocked ?? 0,
                relations = (telegram?.Relations ?? []).Select(x => new { relation = x.Relation, label = x.Label, chats = x.Chats }),
            },
            attendance = new
            {
                lessonsConducted = nb.Conducted, lessonsAttended = nb.Attended, percent = nb.AttendancePct,
                byReason = nb.Reasons.Select(r => new { r.Name, r.IsLate, r.Count }),
            },
            grades = new { average = Math.Round(nb.AvgGrade, 2), bySubject = grades },
            homework = new { done = nb.HomeworkDone, missed = nb.HomeworkMissed },
            behavior = new { good = nb.BehaviorGood, bad = nb.BehaviorBad },
            balance = finance ? st.Balance : (decimal?)null,
            note = finance ? null : "Balans ko'rsatilmadi: moliya ruxsati yo'q.",
        }, 1);
    }

    /// <summary>Telegram status of one student: usable chats, their relations, chats that blocked the bot.</summary>
    internal sealed record TelegramRelationCount(string Relation, string Label, int Chats);

    internal sealed record TelegramLinks(int Linked, int Blocked, List<TelegramRelationCount> Relations);

    /// <summary>
    /// The panel's «Telegram» summary (<c>TelegramLinkSummary</c>, SPEC P6.2): USABLE chats (<c>BlockedAt == null</c>)
    /// counted per relation, blocked chats separately. One grouped query; the chat id column is never selected
    /// (closed to the read-only role) — a registration row is one chat (unique StudentId + ChatId).
    /// </summary>
    private async Task<Dictionary<string, TelegramLinks>> TelegramLinksAsync(List<string> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0) return new Dictionary<string, TelegramLinks>(StringComparer.Ordinal);
        var rows = await t.Db.TelegramRegistrations.Where(r => studentIds.Contains(r.StudentId))
            .GroupBy(r => new { r.StudentId, r.Relation, Blocked = r.BlockedAt != null })
            .Select(g => new { g.Key.StudentId, g.Key.Relation, g.Key.Blocked, N = g.Count() })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.StudentId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => new TelegramLinks(
            g.Where(r => !r.Blocked).Sum(r => r.N),
            g.Where(r => r.Blocked).Sum(r => r.N),
            g.Where(r => !r.Blocked)
                .GroupBy(r => TelegramRelations.Effective(r.Relation))
                .OrderBy(x => Array.IndexOf(TelegramRelations.All, x.Key))
                .Select(x => new TelegramRelationCount(x.Key, TelegramRelations.Label(x.Key), x.Sum(r => r.N)))
                .ToList()), StringComparer.Ordinal);
    }

    private static object TelegramOf(TelegramLinks? l) => new
    {
        chats = l?.Linked ?? 0,
        relations = (l?.Relations ?? []).Select(x => x.Relation),
        blocked = l?.Blocked ?? 0,
    };

    /// <summary>One state per student: active &gt; trial &gt; yearFrozen &gt; frozen &gt; none (.claude/rules/year-freeze.md §5).</summary>
    internal static string StateOf(IEnumerable<(string Status, bool YearFreeze)> memberships)
    {
        var list = memberships.ToList();
        if (list.Any(m => m.Status == "active")) return "active";
        if (list.Any(m => m.Status == "trial")) return "trial";
        if (list.Any(m => m.Status == "frozen" && m.YearFreeze)) return "yearFrozen";
        if (list.Any(m => m.Status == "frozen")) return "frozen";
        return "none";
    }
}
