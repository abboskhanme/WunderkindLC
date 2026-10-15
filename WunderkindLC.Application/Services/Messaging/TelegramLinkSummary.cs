using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>Telegram status of one student for lists: usable chats, their relations, blocked chats, and
/// <paramref name="Cabinet"/> — the student's account has logged in at least once (owner, 2026-10-15: a logged-in
/// account counts as «Ulangan» in every list; Telegram messages still go only to <paramref name="Linked"/> chats).</summary>
public sealed record TelegramLinkInfo(int Linked, List<string> Relations, int Blocked, bool Cabinet = false)
{
    public static readonly TelegramLinkInfo None = new(0, new List<string>(), 0);
}

/// <summary>
/// «TELEGRAM ULANGAN / ULANMAGAN» (SPEC P6.2, U6b, <c>TelegramLinkSummaryTests</c>): counts and relations only — never
/// phones, names or chat ids (the same summary is shown to teachers). Blocked chats are NOT counted as linked.
/// One grouped query for a whole list.
/// </summary>
public static class TelegramLinkSummary
{
    public static async Task<Dictionary<string, TelegramLinkInfo>> ForStudentsAsync(
        IAppDbContext db, IReadOnlyCollection<string> studentIds, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return new Dictionary<string, TelegramLinkInfo>();
        var ids = studentIds.Distinct().ToHashSet();
        var rows = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => r.StudentId != "")
                .Select(r => new { r.StudentId, r.ChatId, r.Relation, Blocked = r.BlockedAt != null })
                .ToListAsync(ct))
            .Where(r => ids.Contains(r.StudentId));
        var map = rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g =>
        {
            var usable = g.Where(r => !r.Blocked).ToList();
            return new TelegramLinkInfo(
                usable.Select(r => r.ChatId).Distinct().Count(),
                usable.Select(r => TelegramRelations.Effective(r.Relation)).Distinct()
                    .OrderBy(r => Array.IndexOf(TelegramRelations.All, r)).ToList(),
                g.Where(r => r.Blocked).Select(r => r.ChatId).Distinct().Count());
        });

        // Kabinetga KIRGAN akkauntlar (o'quvchi akkaunti bilan kamida bir marta login) — ro'yxatlarda ham «Ulangan».
        var idList = ids.ToList();
        var loggedIn = await (from s in db.Students.AsNoTracking()
                              join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                              where idList.Contains(s.Id) && u.FirstLoginAt != null && u.FirstLoginAt != ""
                              select s.Id).ToListAsync(ct);
        foreach (var id in loggedIn)
            map[id] = map.TryGetValue(id, out var info)
                ? info with { Cabinet = true }
                : TelegramLinkInfo.None with { Relations = new List<string>(), Cabinet = true };
        return map;
    }
}
