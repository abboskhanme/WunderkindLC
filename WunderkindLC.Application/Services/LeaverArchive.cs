using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUHSIZ QOLGANLARNI ARXIVLASH — guruhdan chiqarilgan, lekin arxivga o'tkazilmagan o'quvchilar.
///
/// <para>Mijoz shikoyati (2026-10-23): "58 ta o'quvchini guruhdan chiqardim, arxiv 0 turibdi". Chiqarish
/// o'quvchini arxivlamas edi (boshqa guruhi bo'lishi mumkin). Endi chiqarish oynasida «Boshqa guruhi qolmasa
/// — arxivga ham o'tkazish» bor (<c>RemoveMember?archiveIfLast=true</c>), ESKI chiqarilganlar esa shu
/// ro'yxatdan — ko'rib, belgilab — arxivlanadi.</para>
///
/// <para><b>Nomzod</b> (<see cref="LastExit"/>, sof): arxivda emas, kamida bitta a'zoligi bor, birortasi
/// ham TIRIK emas (<c>IsActive</c>), eng oxirgi chiqqan a'zoligi <c>completed</c> EMAS. ⚠️ Sertifikat bilan
/// tugatgan — ketgan emas (keyingi darajaga yozilishi mumkin, <c>course-analytics.md</c> §1), shuning uchun
/// u bu ro'yxatga tushmaydi. Muzlatilgan a'zolik TIRIK — muzlatilganlar ham tushmaydi.</para>
/// </summary>
public static class LeaverArchive
{
    /// <summary>Bir so'rovda ko'pi bilan nechta o'quvchi arxivlanadi.</summary>
    public const int MaxBatch = 500;

    /// <summary>
    /// Nomzodning OXIRGI chiqishi (sabab va izoh shu chiqishdan olinadi) yoki <c>null</c> — nomzod emas.
    /// </summary>
    public static StudentGroup? LastExit(bool isArchived, IEnumerable<StudentGroup> memberships)
    {
        if (isArchived) return null;
        var list = memberships as IReadOnlyCollection<StudentGroup> ?? memberships.ToList();
        if (list.Count == 0 || list.Any(m => m.IsActive)) return null;
        var last = list
            .Where(m => (m.LeftAt ?? "").Length >= 10)
            .OrderByDescending(m => m.LeftAt, StringComparer.Ordinal)
            .ThenByDescending(m => m.RecordedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        return last is null || last.Status == "completed" ? null : last;
    }

    /// <summary>Nomzodlar (eng yangi chiqqani tepada). <paramref name="onlyIds"/> berilsa — faqat ular orasidan.</summary>
    public static async Task<List<(ArchiveLeaverDto Row, string? ReasonId)>> LoadAsync(
        IAppDbContext db, IReadOnlyCollection<string>? onlyIds = null, CancellationToken ct = default)
    {
        var studentsQ = db.Students.AsNoTracking().Where(s => !s.IsArchived);
        if (onlyIds is not null) studentsQ = studentsQ.Where(s => onlyIds.Contains(s.Id));
        var students = await studentsQ
            .Select(s => new { s.Id, s.FullName, s.Phone, s.ParentPhone, s.Balance })
            .ToListAsync(ct);
        if (students.Count == 0) return [];

        var ids = students.Select(s => s.Id).ToList();
        var memberships = (await db.StudentGroups.AsNoTracking()
                .Where(sg => ids.Contains(sg.StudentId)).ToListAsync(ct))
            .GroupBy(sg => sg.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var exits = new Dictionary<string, LeaveDetails.Exit>(StringComparer.Ordinal);
        foreach (var s in students)
        {
            var last = LastExit(false, memberships.GetValueOrDefault(s.Id) ?? []);
            if (last is not null) exits[s.Id] = new LeaveDetails.Exit(s.Id, last.GroupId, last.LeftAt!);
        }
        if (exits.Count == 0) return [];

        var details = await LeaveDetails.LoadAsync(db, exits.Values.ToList(), ct);
        var groupIds = exits.Values.Select(e => e.GroupId).Distinct().ToList();
        var groupNames = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name ?? "", ct);

        return students
            .Where(s => exits.ContainsKey(s.Id))
            .Select(s =>
            {
                var e = exits[s.Id];
                var d = details.GetValueOrDefault(e);
                var phone = !string.IsNullOrWhiteSpace(s.Phone) ? s.Phone : s.ParentPhone;
                return (new ArchiveLeaverDto(s.Id, s.FullName, phone, s.Balance,
                        groupNames.GetValueOrDefault(e.GroupId, ""), e.LeftAt, d.Reason ?? "", d.Note ?? ""),
                    d.ReasonId);
            })
            .OrderByDescending(x => x.Item1.LeftAt, StringComparer.Ordinal)
            .ThenBy(x => x.Item1.FullName, StringComparer.Ordinal)
            .ToList();
    }
}
