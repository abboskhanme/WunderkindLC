using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «QARZDORLIK TAHLILI» (TZ 1.1) — oylar × (o'quvchi, guruh) matritsasi, qarz yoshi (7 / 30 / 60+ kun),
/// hisoblangan · to'langan · qarz · yig'ilish foizi va arxivdagi qarzdorlar belgisi.
///
/// <para>Asos — <see cref="DebtLedger"/> (kassadagi to'lov oynasi qoidasi). Qarz yoshi — qatordagi ENG ESKI
/// to'lanmagan oyning to'lov muddatidan (<c>JournalUnpaidCutoffDay</c>-kun) beri o'tgan kunlar.</para>
///
/// <para>Faqat JORIY oygacha: kelgusi oy hisobini faqat avans ochadi — u hali qarz emas.</para>
/// </summary>
public static class DebtReport
{
    /// <summary>Standart davr — oxirgi 6 oy.</summary>
    public const int DefaultMonths = 6;

    public sealed record Query(
        string? From = null, string? To = null, bool IncludeArchived = false,
        string? GroupId = null, string? TeacherId = null, int MinAge = 0);

    public static async Task<DebtReportDto> BuildAsync(
        IAppDbContext db, Query q, DateOnly today, CancellationToken ct = default)
    {
        var current = today.ToString("yyyy-MM");
        var defFrom = today.AddMonths(-(DefaultMonths - 1)).ToString("yyyy-MM");
        var (from, to) = DebtLedger.ClampRange(q.From, q.To, defFrom, current);
        if (string.CompareOrdinal(to, current) > 0) to = current;
        if (string.CompareOrdinal(from, to) > 0) from = to;
        var months = TuitionService.MonthRange(from, to).ToList();
        var dueDay = await DebtLedger.DueDayAsync(db, ct);

        var cells = (await DebtLedger.LoadAsync(db, from, to, ct)).Where(c => c.HasCharge).ToList();

        var ctx = await NamesAsync(db, cells.Select(c => c.StudentId).Distinct().ToList(), ct);

        // Guruh / o'qituvchi filtri — guruhning HOZIRGI o'qituvchisi (bog'lanadigan odam — hozir o'qitayotgan).
        IEnumerable<LedgerCell> scoped = cells;
        if (!string.IsNullOrEmpty(q.GroupId)) scoped = scoped.Where(c => c.GroupId == q.GroupId);
        if (!string.IsNullOrEmpty(q.TeacherId))
            scoped = scoped.Where(c => ctx.Groups.TryGetValue(c.GroupId, out var g) && g.TeacherId == q.TeacherId);
        var scopedList = scoped.ToList();

        bool Archived(LedgerCell c) => ctx.Students.TryGetValue(c.StudentId, out var s) && s.IsArchived;
        var activeCells = scopedList.Where(c => !Archived(c)).ToList();
        var archivedCells = scopedList.Where(Archived).ToList();
        var inScope = q.IncludeArchived ? scopedList : activeCells;

        // Matritsa qatorlari — qarzi bor (o'quvchi, guruh).
        var rows = new List<DebtRowDto>();
        foreach (var grp in inScope.GroupBy(c => (c.StudentId, c.GroupId)))
        {
            var owing = grp.Where(c => c.Remaining > 0).OrderBy(c => c.Month, StringComparer.Ordinal).ToList();
            if (owing.Count == 0) continue;
            var oldest = owing[0].Month;
            var age = DebtLedger.AgeDays(oldest, dueDay, today);
            ctx.Students.TryGetValue(grp.Key.StudentId, out var st);
            ctx.Groups.TryGetValue(grp.Key.GroupId, out var gi);
            rows.Add(new DebtRowDto(
                grp.Key.StudentId,
                st?.Name ?? "—",
                st?.Phone ?? "",
                grp.Key.GroupId,
                gi?.Name ?? (grp.Key.GroupId == DebtLedger.NoGroup ? "Guruhsiz" : "—"),
                gi?.Course ?? "",
                gi?.Teacher ?? "",
                ctx.Membership.GetValueOrDefault((grp.Key.StudentId, grp.Key.GroupId), ""),
                st?.IsArchived ?? false,
                owing.ToDictionary(c => c.Month, c => c.Remaining),
                owing.Sum(c => c.Remaining),
                oldest,
                age,
                DebtLedger.Bucket(age)));
        }

        var buckets = DebtLedger.Buckets.Select(b =>
        {
            var inB = rows.Where(r => r.Bucket == b.Key).ToList();
            return new DebtAgeBucketDto(b.Key, b.Label, inB.Count,
                inB.Select(r => r.StudentId).Distinct().Count(), inB.Sum(r => r.Debt));
        }).ToList();

        if (q.MinAge > 0) rows = rows.Where(r => r.AgeDays >= q.MinAge).ToList();
        rows = rows
            .OrderBy(r => r.GroupName, StringComparer.CurrentCulture)
            .ThenByDescending(r => r.Debt)
            .ThenBy(r => r.StudentName, StringComparer.CurrentCulture)
            .ToList();

        return new DebtReportDto(
            from, to, months, dueDay, q.IncludeArchived,
            DebtLedger.Totals(inScope), DebtLedger.Totals(activeCells), DebtLedger.Totals(archivedCells),
            buckets, rows);
    }

    internal sealed record StudentInfo(string Name, string Phone, bool IsArchived);
    internal sealed record GroupInfo(string Name, string Course, string TeacherId, string Teacher, bool IsArchived);
    internal sealed record NameContext(
        Dictionary<string, StudentInfo> Students,
        Dictionary<string, GroupInfo> Groups,
        Dictionary<(string, string), string> Membership);

    /// <summary>Ism/telefon, guruh → kurs/o'qituvchi va a'zolik holati — ommaviy (N+1 emas).</summary>
    internal static async Task<NameContext> NamesAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct)
    {
        var ids = studentIds.ToHashSet(StringComparer.Ordinal);
        var students = (await db.Students.AsNoTracking()
                .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone, s.IsArchived })
                .ToListAsync(ct))
            .Where(s => ids.Contains(s.Id))
            .ToDictionary(s => s.Id, s => new StudentInfo(
                s.FullName,
                FirstPhone(s.ParentPhone, s.FatherPhone, s.MotherPhone, s.Phone),
                s.IsArchived), StringComparer.Ordinal);

        var teachers = await db.Teachers.AsNoTracking().Select(t => new { t.Id, t.FullName }).ToListAsync(ct);
        var teacherById = teachers.ToDictionary(t => t.Id, t => t.FullName, StringComparer.Ordinal);
        var courses = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var courseById = courses.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
        var groups = (await db.Classes.AsNoTracking()
                .Select(g => new { g.Id, g.Name, g.CourseId, g.TeacherId, g.IsArchived }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => new GroupInfo(
                g.Name,
                courseById.GetValueOrDefault(g.CourseId, ""),
                g.TeacherId,
                teacherById.GetValueOrDefault(g.TeacherId, ""),
                g.IsArchived), StringComparer.Ordinal);

        var memberships = (await db.StudentGroups.AsNoTracking()
                .Select(m => new { m.StudentId, m.GroupId, m.Status, m.IsActive, m.JoinedAt })
                .ToListAsync(ct))
            .Where(m => ids.Contains(m.StudentId))
            .GroupBy(m => (m.StudentId, m.GroupId))
            // Bir (o'quvchi, guruh) da bir nechta qator bo'lsa — tirigi, bo'lmasa eng oxirgisi.
            .ToDictionary(g => g.Key, g =>
            {
                var m = g.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.JoinedAt, StringComparer.Ordinal).First();
                return MemberStatus(m.Status, m.IsActive);
            });

        return new NameContext(students, groups, memberships);
    }

    /// <summary>A'zolik holati yorlig'i kaliti: chiqqan a'zolik — <c>left</c> (sertifikat bilan — <c>completed</c>).</summary>
    public static string MemberStatus(string status, bool isActive) =>
        isActive ? status : status == "completed" ? "completed" : "left";

    /// <summary>Bog'lanish uchun telefon: ota-ona → ota → ona → o'quvchining o'zi.</summary>
    public static string FirstPhone(params string?[] phones) =>
        phones.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim() ?? "";
}
