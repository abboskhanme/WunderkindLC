using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KETISHNING SABABI va IZOHI — «Ketgan o'quvchilar» ro'yxati (bosh sahifa kartochkasi) va «Guruhsiz
/// qolganlarni arxivlash» oynasi uchun.
///
/// <para>Yangi qoida YO'Q: sabab — «Ketish sabablari» hisobotidagi AYNAN o'sha
/// <see cref="MembershipReasons.Resolve"/> (tuzilgan yozuv → muzlatish → eski audit matni → arxiv sababi),
/// izoh — chiqarish oynasida yozilgan profil izohi (<see cref="LeaveNotes.PickNote"/>). Mijoz shikoyati
/// (2026-10-23): "chiqarishda batafsil izoh yozamiz, lekin ketganlar ro'yxatida u ham, sabab ham
/// ko'rinmaydi" — ular faqat hisobotda va profilda edi.</para>
///
/// <para>Barcha so'rovlar faqat-o'qish, faqat berilgan o'quvchilar bo'yicha (audit jadvali katta).</para>
/// </summary>
public static class LeaveDetails
{
    /// <summary>Bitta ketish: oraliqni YOPGAN a'zolikning guruhi va chiqish sanasi.</summary>
    public readonly record struct Exit(string StudentId, string GroupId, string LeftAt);

    /// <summary>Topilgan sabab (bo'sh — ko'rsatilmagan) va izoh (prefiksiz, bo'sh — yozilmagan).</summary>
    public readonly record struct Info(string? ReasonId, string Reason, string Note);

    public static async Task<Dictionary<Exit, Info>> LoadAsync(
        IAppDbContext db, IReadOnlyCollection<Exit> exits, CancellationToken ct = default)
    {
        var result = new Dictionary<Exit, Info>();
        if (exits.Count == 0) return result;

        var studentIds = exits.Select(e => e.StudentId).Distinct().ToList();
        var groupIds = exits.Select(e => e.GroupId).Where(g => g.Length > 0).Distinct().ToList();
        var pairKeys = exits.Select(e => $"{e.GroupId}:{e.StudentId}").Distinct().ToList();

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => studentIds.Contains(sg.StudentId) && groupIds.Contains(sg.GroupId))
            .Select(sg => new { sg.StudentId, sg.GroupId, sg.LeftAt, sg.FrozenAt })
            .ToListAsync(ct);
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.IsArchived, s.ArchivedAt, s.ArchiveReason })
            .ToDictionaryAsync(s => s.Id, ct);
        var groupNames = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name ?? "", ct);
        var logs = (await db.MembershipReasonLogs.AsNoTracking()
                .Where(l => studentIds.Contains(l.StudentId)).ToListAsync(ct))
            .GroupBy(l => (l.StudentId, l.GroupId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var audits = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "Membership" && pairKeys.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Timestamp, a.Summary })
                .ToListAsync(ct))
            .GroupBy(a => a.EntityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.Select(a => new MembershipReasons.AuditRow(a.Timestamp ?? "", a.Summary ?? "")).ToList(),
                StringComparer.Ordinal);
        var notes = (await db.StudentNotes.AsNoTracking()
                .Where(n => studentIds.Contains(n.StudentId))
                .Select(n => new { n.StudentId, n.Text, n.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(n => n.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => g.Select(n => new LeaveNotes.NoteRow(n.Text ?? "", n.CreatedAt ?? "")).ToList(),
                StringComparer.Ordinal);

        foreach (var e in exits)
        {
            if (result.ContainsKey(e)) continue;
            // Oraliqni yopgan a'zolik — AYNAN shu sanada chiqqani (muzlatish sanasi sabab qidirishda kerak).
            var closing = memberships.FirstOrDefault(m =>
                m.StudentId == e.StudentId && m.GroupId == e.GroupId && (m.LeftAt ?? "") == e.LeftAt);
            var st = students.GetValueOrDefault(e.StudentId);
            var reason = MembershipReasons.Resolve(
                e.LeftAt, closing?.FrozenAt,
                logs.GetValueOrDefault((e.StudentId, e.GroupId)) ?? [],
                audits.GetValueOrDefault($"{e.GroupId}:{e.StudentId}") ?? [],
                new MembershipReasons.ArchiveInfo(st?.IsArchived ?? false, st?.ArchivedAt, st?.ArchiveReason));
            var note = LeaveNotes.PickNote(
                notes.GetValueOrDefault(e.StudentId) ?? [],
                groupNames.GetValueOrDefault(e.GroupId, ""), e.LeftAt, st?.ArchivedAt);
            result[e] = new Info(reason.ReasonId, reason.Label, note);
        }
        return result;
    }
}
