using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>Telegram status of one student for lists: usable chats, their relations, blocked chats.</summary>
public sealed record TelegramLinkInfo(int Linked, List<string> Relations, int Blocked)
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
        return rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g =>
        {
            var usable = g.Where(r => !r.Blocked).ToList();
            return new TelegramLinkInfo(
                usable.Select(r => r.ChatId).Distinct().Count(),
                usable.Select(r => TelegramRelations.Effective(r.Relation)).Distinct()
                    .OrderBy(r => Array.IndexOf(TelegramRelations.All, r)).ToList(),
                g.Where(r => r.Blocked).Select(r => r.ChatId).Distinct().Count());
        });
    }
}
