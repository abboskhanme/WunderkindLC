using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// READ side of the Telegram messaging module (SPEC P2.7): campaign history and reports, automatic-message statistics,
/// the debtors list and the student's «Telegram xabarlari». Counts always come from <see cref="MessageDelivery"/>
/// rows (legacy campaigns without rows show <c>RecipientCount/SentCount</c>). Chat ids are never returned.
/// </summary>
public static class MessagingReports
{
    public const int MaxPageSize = 200;

    public static (int Page, int Size) Paging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, MaxPageSize));

    /// <summary>Delivery counts of one campaign (internal: the read-only MCP <c>messages_history</c> reuses them).</summary>
    internal sealed record Counts(int Total, int Sent, int Failed, int Blocked, int Pending, int Acked);

    internal static async Task<Dictionary<string, Counts>> CountsAsync(IAppDbContext db, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<string, Counts>();
        var rows = await db.MessageDeliveries.AsNoTracking()
            .Where(d => d.BroadcastId != null && ids.Contains(d.BroadcastId))
            .GroupBy(d => new { d.BroadcastId, d.Status })
            .Select(g => new { g.Key.BroadcastId, g.Key.Status, N = g.Count(), Acked = g.Count(x => x.AckAt != null) })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.BroadcastId!).ToDictionary(g => g.Key, g => new Counts(
            g.Sum(r => r.N),
            g.Where(r => r.Status == DeliveryStatus.Sent).Sum(r => r.N),
            g.Where(r => DeliveryStatus.IsFailure(r.Status)).Sum(r => r.N),
            g.Where(r => r.Status == DeliveryStatus.Blocked).Sum(r => r.N),
            g.Where(r => DeliveryStatus.IsOpen(r.Status)).Sum(r => r.N),
            g.Sum(r => r.Acked)));
    }

    internal static CampaignRowDto Row(Broadcast b, Counts? c)
    {
        var plain = TelegramHtml.ToPlain(TelegramHtml.SanitizeTemplate(b.Text).Html);
        if (plain.Length > 200) plain = plain[..200] + "…";
        var counts = c ?? new Counts(b.RecipientCount, b.SentCount, Math.Max(0, b.RecipientCount - b.SentCount), 0, 0, 0);
        return new CampaignRowDto(b.Id, string.IsNullOrEmpty(b.Kind) ? "broadcast" : b.Kind, b.ClassName, plain,
            b.SenderName, b.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"), string.IsNullOrEmpty(b.Status) ? "done" : b.Status,
            counts.Total, counts.Sent, counts.Failed, counts.Blocked, counts.Pending, counts.Acked,
            b.AckRequired, b.AttachmentId is not null);
    }

    public static async Task<Paged<CampaignRowDto>> CampaignsAsync(
        IAppDbContext db, string? kind, string? from, string? to, int? page, int? pageSize, CancellationToken ct = default)
    {
        var (p, size) = Paging(page, pageSize);
        var q = db.Broadcasts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(b => b.Kind == kind);
        if (DateOnly.TryParse(from, out var f)) { var fd = f.ToDateTime(TimeOnly.MinValue); q = q.Where(b => b.CreatedAt >= fd); }
        if (DateOnly.TryParse(to, out var t)) { var td = t.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(b => b.CreatedAt < td); }
        var total = await q.CountAsync(ct);
        var list = await q.OrderByDescending(b => b.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var counts = await CountsAsync(db, list.Select(b => b.Id).ToList(), ct);
        return new Paged<CampaignRowDto>(list.Select(b => Row(b, counts.GetValueOrDefault(b.Id))).ToList(), total, p, size);
    }

    public static async Task<CampaignDetailDto?> CampaignAsync(IAppDbContext db, string id, CancellationToken ct = default)
    {
        var b = await db.Broadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return null;
        var counts = await CountsAsync(db, new List<string> { b.Id }, ct);
        CampaignAttachmentRef? att = null;
        if (b.AttachmentId is not null)
            att = await db.MessageAttachments.AsNoTracking().Where(a => a.Id == b.AttachmentId)
                .Select(a => new CampaignAttachmentRef(a.Id, a.Kind, a.FileName)).FirstOrDefaultAsync(ct);
        // The stored text of the first row is exactly what was sent (rendered at enqueue time).
        var html = await db.MessageDeliveries.AsNoTracking().Where(d => d.BroadcastId == b.Id)
            .OrderBy(d => d.CreatedAt).Select(d => d.Text).FirstOrDefaultAsync(ct)
            ?? TelegramHtml.Escape(b.Text);
        return CampaignDetailDto.From(Row(b, counts.GetValueOrDefault(b.Id)), html, att, CampaignService.ParseAudience(b.AudienceJson));
    }

    /// <summary>Report rows paged BY STUDENT. <paramref name="status"/>: sent · acked · failed · blocked · pending ·
    /// cancelled (empty = all).</summary>
    public static async Task<Paged<CampaignStudentDeliveriesDto>?> DeliveriesAsync(
        IAppDbContext db, string id, string? status, string? q, int? page, int? pageSize, CancellationToken ct = default)
    {
        if (!await db.Broadcasts.AnyAsync(b => b.Id == id, ct)) return null;
        var (p, size) = Paging(page, pageSize);
        var rows = await db.MessageDeliveries.AsNoTracking().Where(d => d.BroadcastId == id)
            .Select(d => new { d.Id, d.StudentId, d.TeacherId, d.RecipientName, d.Relation, d.Status, d.SentAt, d.AckAt, d.Error })
            .ToListAsync(ct);
        rows = (status ?? "") switch
        {
            "sent" => rows.Where(r => r.Status == DeliveryStatus.Sent).ToList(),
            "acked" => rows.Where(r => r.AckAt != null).ToList(),
            "failed" => rows.Where(r => DeliveryStatus.IsFailure(r.Status)).ToList(),
            "blocked" => rows.Where(r => r.Status == DeliveryStatus.Blocked).ToList(),
            "pending" => rows.Where(r => DeliveryStatus.IsOpen(r.Status)).ToList(),
            "cancelled" => rows.Where(r => r.Status == DeliveryStatus.Cancelled).ToList(),
            _ => rows,
        };
        var needle = (q ?? "").Trim();
        if (needle.Length > 0)
            rows = rows.Where(r => r.RecipientName.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        var grouped = rows.GroupBy(r => r.StudentId ?? r.TeacherId ?? r.Id)
            .Select(g => new { Key = g.Key, Name = g.First().RecipientName, Rows = g.ToList() })
            .OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var pageItems = grouped.Skip((p - 1) * size).Take(size).ToList();

        var studentIds = pageItems.Select(g => g.Key).ToList();
        var groupNames = (await (from sg in db.StudentGroups.AsNoTracking()
                                 join c in db.Classes.AsNoTracking() on sg.GroupId equals c.Id
                                 where studentIds.Contains(sg.StudentId) && sg.IsActive
                                 select new { sg.StudentId, c.Name }).ToListAsync(ct))
            .GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().OrderBy(n => n).ToList());

        var items = pageItems.Select(g => new CampaignStudentDeliveriesDto(
            g.Key, g.Name, groupNames.GetValueOrDefault(g.Key) ?? new List<string>(),
            g.Rows.OrderBy(r => Array.IndexOf(TelegramRelations.All, r.Relation)).Select(r => new CampaignDeliveryRowDto(
                r.Id, r.Relation, r.RecipientName, r.Status, Iso(r.SentAt), Iso(r.AckAt), r.Error)).ToList())).ToList();
        return new Paged<CampaignStudentDeliveriesDto>(items, grouped.Count, p, size);
    }

    /// <summary>Automatic messages by day × trigger (range ≤ 92 days, clamped).</summary>
    public static async Task<List<AutoStatsRowDto>> AutoStatsAsync(IAppDbContext db, string? from, string? to, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var end = DateOnly.TryParse(to, out var t) ? t : today;
        var start = DateOnly.TryParse(from, out var f) ? f : end.AddDays(-6);
        if (start > end) (start, end) = (end, start);
        if (end.DayNumber - start.DayNumber > 91) start = end.AddDays(-91);
        var s = start.ToDateTime(TimeOnly.MinValue);
        var e = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var rows = await db.MessageDeliveries.AsNoTracking()
            .Where(d => d.Source == TelegramOutbox.SourceAuto && d.CreatedAt >= s && d.CreatedAt < e)
            .Select(d => new { d.CreatedAt, d.Trigger, d.Status, Acked = d.AckAt != null })
            .ToListAsync(ct);
        return rows.GroupBy(r => (Date: r.CreatedAt.ToString("yyyy-MM-dd"), r.Trigger))
            .OrderByDescending(g => g.Key.Date).ThenBy(g => g.Key.Trigger)
            .Select(g => new AutoStatsRowDto(g.Key.Date, g.Key.Trigger,
                AutoMessageTriggers.Get(g.Key.Trigger)?.Label ?? g.Key.Trigger,
                g.Count(), g.Count(r => r.Status == DeliveryStatus.Sent),
                g.Count(r => DeliveryStatus.IsFailure(r.Status)), g.Count(r => r.Status == DeliveryStatus.Blocked),
                g.Count(r => r.Acked)))
            .ToList();
    }

    /// <summary>
    /// «Qarzdorlar» — population of the automatic reminder (non-archived, <c>Balance &lt; 0</c>, not closed-group),
    /// debts per course from <see cref="DebtMessages"/>, usable Telegram chats and the last debt reminder.
    /// </summary>
    public static async Task<List<DebtorDto>> DebtorsAsync(
        IAppDbContext db, string? groupId, string? courseId, string? teacherId, decimal? minDebt, string? memberStatus,
        CancellationToken ct = default)
    {
        var debtors = await db.Students.AsNoTracking().Where(s => !s.IsArchived && s.Balance < 0)
            .OrderBy(s => s.FullName).ToListAsync(ct);
        var closed = await MessagingAudience.ClosedGroupStudentIdsAsync(db, ct);
        debtors = debtors.Where(s => !closed.Contains(s.Id)).ToList();

        if (memberStatus is "active" or "frozen")
        {
            var withStatus = (await db.StudentGroups.AsNoTracking()
                    .Where(m => m.IsActive && m.Status == memberStatus).Select(m => m.StudentId).Distinct().ToListAsync(ct))
                .ToHashSet();
            debtors = debtors.Where(s => withStatus.Contains(s.Id)).ToList();
        }

        var ids = debtors.Select(s => s.Id).ToList();
        var regs = (await db.TelegramRegistrations.AsNoTracking().Where(r => r.StudentId != "" && r.BlockedAt == null)
                .Select(r => new { r.StudentId, r.ChatId, r.Relation }).ToListAsync(ct))
            .Where(r => ids.Contains(r.StudentId)).GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.ToList());
        var lastReminders = (await db.MessageDeliveries.AsNoTracking()
                .Where(d => d.StudentId != null && (d.Trigger == AutoMessageTriggers.PaymentDebt || d.Trigger == CampaignService.KindDebt))
                .Select(d => new { d.StudentId, d.CreatedAt, d.SentAt, d.Status, d.AckAt }).ToListAsync(ct))
            .GroupBy(d => d.StudentId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());
        var groupByName = await DebtMessages.GroupsByNameAsync(db, ct);

        var result = new List<DebtorDto>();
        foreach (var s in debtors)
        {
            var items = await DebtMessages.BuildForStudentAsync(db, s, groupByName, ct);
            if (items.Count == 0) continue;
            if (!string.IsNullOrWhiteSpace(groupId) && !items.Any(i => i.Group?.Id == groupId)) continue;
            if (!string.IsNullOrWhiteSpace(courseId) && !items.Any(i => i.Group?.CourseId == courseId)) continue;
            if (!string.IsNullOrWhiteSpace(teacherId) && !items.Any(i => i.Group?.TeacherId == teacherId)) continue;
            var total = items.Sum(i => i.Amount);
            if (minDebt is > 0 && total < minDebt) continue;
            var chats = regs.GetValueOrDefault(s.Id);
            var last = lastReminders.GetValueOrDefault(s.Id);
            result.Add(new DebtorDto(s.Id, s.FullName, total,
                items.Select(i => new DebtorItemDto(i.Group?.Id ?? "", i.Group?.Name ?? "", i.Label, i.Amount)).ToList(),
                new DebtorTelegramDto(chats?.Select(c => c.ChatId).Distinct().Count() ?? 0,
                    chats?.Select(c => TelegramRelations.Effective(c.Relation)).Distinct()
                        .OrderBy(r => Array.IndexOf(TelegramRelations.All, r)).ToList() ?? new List<string>()),
                last is null ? null : new LastReminderDto(Iso(last.SentAt ?? last.CreatedAt)!, last.Status, Iso(last.AckAt))));
        }
        return result;
    }

    /// <summary>Student profile «Telegram xabarlari» (latest first).</summary>
    public static async Task<List<StudentDeliveryDto>> StudentDeliveriesAsync(
        IAppDbContext db, string studentId, int limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, 200);
        var rows = await db.MessageDeliveries.AsNoTracking()
            .Where(d => d.StudentId == studentId && d.TeacherId == null)
            .OrderByDescending(d => d.CreatedAt).Take(take)
            .Select(d => new { d.Id, d.CreatedAt, d.SentAt, d.Trigger, d.Relation, d.Status, d.AckAt, d.Text, d.Source })
            .ToListAsync(ct);
        return rows.Select(d =>
        {
            var plain = TelegramHtml.ToPlain(d.Text);
            if (plain.Length > 300) plain = plain[..300] + "…";
            return new StudentDeliveryDto(d.Id, Iso(d.CreatedAt)!, Iso(d.SentAt), d.Trigger, TriggerLabel(d.Source, d.Trigger),
                d.Relation, d.Status, Iso(d.AckAt), plain);
        }).ToList();
    }

    public static string TriggerLabel(string source, string trigger) => source == TelegramOutbox.SourceCampaign
        ? trigger switch
        {
            CampaignService.KindArchive => "Arxiv xabari",
            CampaignService.KindDebt => "Qarz eslatmasi",
            _ => "E'lon",
        }
        : AutoMessageTriggers.Get(trigger)?.Label ?? trigger;

    private static string? Iso(DateTime? v) => v?.ToString("yyyy-MM-ddTHH:mm:ss");
}
