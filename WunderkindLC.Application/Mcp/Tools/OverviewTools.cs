using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>Centre-wide snapshot: counts only, no personal data. Money only with finance access.</summary>
[McpServerToolType]
public sealed class OverviewTools(McpToolContext t)
{
    public static readonly Type[] Reads =
        [typeof(Student), typeof(StudentGroup), typeof(Group), typeof(Teacher), typeof(Subject), typeof(Room)];

    [McpServerTool(Name = "center_overview", Title = "Markaz bo'yicha umumiy ko'rsatkichlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quv markazining umumiy holati: o'quvchilar soni (faol / sinovda / muzlatilgan / guruhsiz / arxivda), "
        + "guruhlar, kurslar, o'qituvchilar va xonalar soni. Moliya ruxsati bo'lsa — qarzdorlar soni va jami qarz. "
        + "Bir o'quvchi bir nechta guruhda bo'lsa ham BIR marta sanaladi. "
        + "Centre overview: counts of students by status, groups, courses, teachers, rooms; debt totals with finance access.")]
    public async Task<string> CenterOverview(CancellationToken ct = default)
    {
        t.Require(McpAreas.Any);
        var db = t.Db;

        // Live memberships: the membership is open, the group is not archived, the student has not left.
        var live = await (from sg in db.StudentGroups
                          join g in db.Classes on sg.GroupId equals g.Id
                          join s in db.Students on sg.StudentId equals s.Id
                          where sg.IsActive && !g.IsArchived && !s.IsArchived
                          select new { sg.StudentId, sg.Status }).ToListAsync(ct);

        // One student = one state, by precedence active > trial > frozen (the students list rule).
        var byStudent = live.GroupBy(x => x.StudentId).Select(g =>
            g.Any(x => x.Status == "active") ? "active"
            : g.Any(x => x.Status == "trial") ? "trial"
            : "frozen").ToList();

        var notArchived = await db.Students.CountAsync(s => !s.IsArchived, ct);
        var archived = await db.Students.CountAsync(s => s.IsArchived, ct);
        var groups = await db.Classes.CountAsync(g => !g.IsArchived, ct);
        var teachers = await db.Teachers.CountAsync(x => !x.IsArchived, ct);
        var courses = await db.Subjects.CountAsync(ct);
        var rooms = await db.Rooms.CountAsync(r => r.IsActive, ct);

        object? money = null;
        if (t.CanReadFinance)
        {
            var debts = await db.Students.Where(s => !s.IsArchived && s.Balance < 0)
                .Select(s => s.Balance).ToListAsync(ct);
            money = new { debtors = debts.Count, totalDebt = -debts.Sum() };
        }

        return t.Json(new
        {
            date = McpToolContext.Iso(McpToolContext.Today),
            students = new
            {
                active = byStudent.Count(x => x == "active"),
                trial = byStudent.Count(x => x == "trial"),
                frozen = byStudent.Count(x => x == "frozen"),
                withoutGroup = notArchived - byStudent.Count,
                total = notArchived,
                archived,
            },
            groups,
            courses,
            teachers,
            rooms,
            money,
            note = money is null ? "Qarzdorlik ko'rsatilmadi: moliya ruxsati yo'q." : null,
        }, 1);
    }
}
