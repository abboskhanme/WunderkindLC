using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>One deliverable chat of the audience: the registration plus the student (or teacher) it belongs to.</summary>
public sealed record AudienceTarget(Student? Student, Teacher? Teacher, TelegramRegistration Reg);

/// <summary>Resolved audience. <see cref="Targets"/> are usable (unblocked, matching relation) registrations.</summary>
public sealed class AudienceResult
{
    public List<Student> Students { get; } = new();
    public List<Teacher> Teachers { get; } = new();
    public List<AudienceTarget> Targets { get; } = new();
    /// <summary>Distinct chats excluded because they blocked the bot.</summary>
    public int Blocked { get; set; }
    /// <summary>Students (teachers) of the audience without any usable chat of the selected relations.</summary>
    public int NotConnected { get; set; }
    public string Label { get; set; } = "";
    public bool IsTeachers { get; set; }
}

/// <summary>
/// AUDIENCE RESOLUTION (SPEC P2.3 <c>AudienceSpec</c>, IO, <c>MessageAudienceTests</c>) — one query per table, no N+1.
/// <list type="bullet">
///   <item><c>active</c> — non-archived students with a LIVE membership (<c>StudentGroup.IsActive</c> + group not
///     archived) whose status is in <c>memberStatuses</c> (default active + trial) and that matches the group /
///     course / teacher filters. Legacy students with no membership at all are included only without such filters.
///     Closed-group students are excluded (<see cref="MessagingAudience.ClosedGroupStudentIdsAsync"/>).</item>
///   <item><c>archive</c> — ARCHIVED students only (the two lists never mix); course/group/teacher match any
///     membership ever.</item>
///   <item><c>selected</c> — exactly the given ids (1..2000).</item>
///   <item><c>teachers</c> — chats registered for teachers.</item>
/// </list>
/// Registrations: relation in <c>relations</c> (default: every parent relation); blocked chats are excluded and counted.
/// </summary>
public static class MessageAudienceResolver
{
    public const string KindActive = "active";
    public const string KindArchive = "archive";
    public const string KindSelected = "selected";
    public const string KindTeachers = "teachers";
    public const int MaxSelected = 2000;

    private static readonly string[] MemberStatuses = { "active", "trial", "frozen" };

    /// <summary>Validates a spec (400 message in Uzbek, or null).</summary>
    public static string? Validate(AudienceSpec? spec)
    {
        if (spec is null) return "Oluvchilar tanlanmagan";
        if (spec.Kind is not (KindActive or KindArchive or KindSelected or KindTeachers)) return "Oluvchilar turi noto'g'ri";
        if (spec.Relations is { } rel && rel.Any(r => !TelegramRelations.IsKnown(r))) return "Noma'lum qarindoshlik turi";
        if (spec.MemberStatuses is { } ms && ms.Any(s => !MemberStatuses.Contains(s))) return "Noma'lum a'zolik holati";
        if (spec.Kind == KindSelected)
        {
            var n = spec.StudentIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count() ?? 0;
            if (n == 0) return "Hech kim tanlanmadi";
            if (n > MaxSelected) return $"Ko'pi bilan {MaxSelected} ta o'quvchi tanlang";
        }
        if (!IsDateOrEmpty(spec.ArchivedFrom) || !IsDateOrEmpty(spec.ArchivedTo)) return "Arxiv sanasi noto'g'ri (yyyy-MM-dd)";
        return null;
    }

    /// <summary>Effective relations of a spec.</summary>
    public static IReadOnlyList<string> RelationsOf(AudienceSpec spec) =>
        spec.Relations is { Count: > 0 } r ? r.Distinct().ToList() : TelegramRelations.Parents;

    public static async Task<AudienceResult> ResolveAsync(IAppDbContext db, AudienceSpec spec, CancellationToken ct = default)
    {
        var result = new AudienceResult();
        var relations = RelationsOf(spec).ToHashSet(StringComparer.Ordinal);

        if (spec.Kind == KindTeachers)
        {
            result.IsTeachers = true;
            var ids = spec.TeacherIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() ?? new List<string>();
            var tq = db.Teachers.AsNoTracking().Where(t => !t.IsArchived);
            if (ids.Count > 0) tq = tq.Where(t => ids.Contains(t.Id));
            result.Teachers.AddRange(await tq.OrderBy(t => t.FullName).ToListAsync(ct));
            var byId = result.Teachers.ToDictionary(t => t.Id);
            var regs = await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.TeacherId != null && r.TeacherId != "").ToListAsync(ct);
            var blocked = new HashSet<long>();
            var connected = new HashSet<string>();
            foreach (var r in regs.OrderBy(r => r.CreatedAt))
            {
                if (!byId.TryGetValue(r.TeacherId!, out var t)) continue;
                if (r.BlockedAt is not null) { blocked.Add(r.ChatId); continue; }
                result.Targets.Add(new AudienceTarget(null, t, r));
                connected.Add(t.Id);
            }
            result.Blocked = blocked.Count;
            result.NotConnected = result.Teachers.Count(t => !connected.Contains(t.Id));
            result.Label = ids.Count > 0 ? $"O'qituvchilar ({result.Teachers.Count})" : "Barcha o'qituvchilar";
            return result;
        }

        List<Student> students;
        switch (spec.Kind)
        {
            case KindSelected:
            {
                var ids = spec.StudentIds!.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
                result.Label = $"Tanlangan ({students.Count})";
                break;
            }
            case KindArchive:
                students = await ArchiveAsync(db, spec, ct);
                result.Label = "Arxiv";
                break;
            default:
                students = await ActiveAsync(db, spec, ct);
                result.Label = await ActiveLabelAsync(db, spec, ct);
                break;
        }
        if (spec.OnlyDebtors) students = students.Where(s => s.Balance < 0).ToList();
        students = students.OrderBy(s => s.FullName, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        result.Students.AddRange(students);

        var byStudent = students.ToDictionary(s => s.Id);
        var studentRegs = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.StudentId != "").ToListAsync(ct);
        var blockedChats = new HashSet<long>();
        var connectedStudents = new HashSet<string>();
        foreach (var r in studentRegs.OrderBy(r => r.CreatedAt))
        {
            if (!byStudent.TryGetValue(r.StudentId, out var s)) continue;
            if (!relations.Contains(TelegramRelations.Effective(r.Relation))) continue;
            if (r.BlockedAt is not null) { blockedChats.Add(r.ChatId); continue; }
            result.Targets.Add(new AudienceTarget(s, null, r));
            connectedStudents.Add(s.Id);
        }
        result.Blocked = blockedChats.Count;
        result.NotConnected = students.Count(s => !connectedStudents.Contains(s.Id));
        if (spec.OnlyDebtors) result.Label += " — qarzdorlar";
        return result;
    }

    private static async Task<List<Student>> ActiveAsync(IAppDbContext db, AudienceSpec spec, CancellationToken ct)
    {
        var statuses = (spec.MemberStatuses is { Count: > 0 } ms ? ms : new List<string> { "active", "trial" }).ToHashSet();
        var groupIds = spec.GroupIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet() ?? new HashSet<string>();
        var courseIds = spec.CourseIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet() ?? new HashSet<string>();
        var teacherId = string.IsNullOrWhiteSpace(spec.TeacherId) ? null : spec.TeacherId;
        var filtered = groupIds.Count > 0 || courseIds.Count > 0 || teacherId is not null;

        var live = await (from sg in db.StudentGroups.AsNoTracking()
                          join c in db.Classes.AsNoTracking() on sg.GroupId equals c.Id
                          where sg.IsActive && !c.IsArchived
                          select new { sg.StudentId, sg.Status, GroupId = c.Id, c.CourseId, c.TeacherId }).ToListAsync(ct);
        var ids = live.Where(m => statuses.Contains(m.Status)
                                  && (groupIds.Count == 0 || groupIds.Contains(m.GroupId))
                                  && (courseIds.Count == 0 || courseIds.Contains(m.CourseId))
                                  && (teacherId is null || m.TeacherId == teacherId))
            .Select(m => m.StudentId).ToHashSet();

        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived).ToListAsync(ct);
        if (!filtered)
        {
            // Legacy (ClassName-only) students: no membership row at all.
            var withMembership = (await db.StudentGroups.AsNoTracking().Select(m => m.StudentId).Distinct().ToListAsync(ct))
                .ToHashSet();
            foreach (var s in students.Where(s => !withMembership.Contains(s.Id))) ids.Add(s.Id);
        }
        var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        return students.Where(s => ids.Contains(s.Id) && !closed.Contains(s.Id)).ToList();
    }

    private static async Task<List<Student>> ArchiveAsync(IAppDbContext db, AudienceSpec spec, CancellationToken ct)
    {
        var students = await db.Students.AsNoTracking().Where(s => s.IsArchived).ToListAsync(ct);
        var from = spec.ArchivedFrom?.Trim() ?? "";
        var to = spec.ArchivedTo?.Trim() ?? "";
        if (from.Length > 0) students = students.Where(s => string.CompareOrdinal(Day(s.ArchivedAt), from) >= 0).ToList();
        if (to.Length > 0) students = students.Where(s => Day(s.ArchivedAt).Length > 0 && string.CompareOrdinal(Day(s.ArchivedAt), to) <= 0).ToList();
        var reason = spec.ArchiveReason?.Trim() ?? "";
        if (reason.Length > 0)
            students = students.Where(s => (s.ArchiveReason ?? "").Contains(reason, StringComparison.OrdinalIgnoreCase)).ToList();

        var groupIds = spec.GroupIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet() ?? new HashSet<string>();
        var courseIds = spec.CourseIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet() ?? new HashSet<string>();
        var teacherId = string.IsNullOrWhiteSpace(spec.TeacherId) ? null : spec.TeacherId;
        if (groupIds.Count > 0 || courseIds.Count > 0 || teacherId is not null)
        {
            // ANY membership ever (archived students' memberships are closed).
            var ever = await (from sg in db.StudentGroups.AsNoTracking()
                              join c in db.Classes.AsNoTracking() on sg.GroupId equals c.Id
                              select new { sg.StudentId, GroupId = c.Id, c.CourseId, c.TeacherId }).ToListAsync(ct);
            var ids = ever.Where(m => (groupIds.Count == 0 || groupIds.Contains(m.GroupId))
                                      && (courseIds.Count == 0 || courseIds.Contains(m.CourseId))
                                      && (teacherId is null || m.TeacherId == teacherId))
                .Select(m => m.StudentId).ToHashSet();
            students = students.Where(s => ids.Contains(s.Id)).ToList();
        }
        return students;
    }

    private static async Task<string> ActiveLabelAsync(IAppDbContext db, AudienceSpec spec, CancellationToken ct)
    {
        var parts = new List<string>();
        if (spec.GroupIds is { Count: > 0 } g)
        {
            var names = await db.Classes.AsNoTracking().Where(c => g.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct);
            parts.Add(names.Count <= 3 ? string.Join(", ", names) : $"{names.Count} ta guruh");
        }
        if (spec.CourseIds is { Count: > 0 } c2)
        {
            var names = await db.Subjects.AsNoTracking().Where(s => c2.Contains(s.Id)).Select(s => s.Name).ToListAsync(ct);
            parts.Add(names.Count <= 3 ? string.Join(", ", names) : $"{names.Count} ta kurs");
        }
        if (!string.IsNullOrWhiteSpace(spec.TeacherId))
        {
            var t = await db.Teachers.AsNoTracking().Where(x => x.Id == spec.TeacherId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(t)) parts.Add(t);
        }
        return parts.Count == 0 ? "Faol o'quvchilar" : string.Join(" · ", parts);
    }

    private static string Day(string? iso) => string.IsNullOrEmpty(iso) ? "" : iso.Length >= 10 ? iso[..10] : iso;

    private static bool IsDateOrEmpty(string? v) =>
        string.IsNullOrWhiteSpace(v)
        || DateOnly.TryParseExact(v.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
