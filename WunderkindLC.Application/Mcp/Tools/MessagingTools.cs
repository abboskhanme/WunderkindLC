using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Mcp.Tools;

/// <summary>
/// Telegram messaging (CRM upgrade Phase 2, U2m — docs/SPEC-crm-upgrade.md P2.10): one campaign's delivery and
/// «Qabul qildim» report, the per-recipient delivery log, automatic-message statistics and student achievements.
///
/// <para>Numbers come from the panel's own read side (<see cref="MessagingReports"/> — «Xabarlar → Tarix», the
/// campaign report, «Avto xabarlar» statistics): counts are always <see cref="MessageDelivery"/> rows (legacy
/// campaigns without rows show the stored <c>RecipientCount/SentCount</c>). Rules: .claude/rules/messaging.md §0.5.</para>
///
/// <para>⚠️ NEVER a Telegram chat id: <c>MessageDelivery.ChatId</c> and <c>TelegramRegistration.ChatId</c> are not
/// granted to the read-only role (sensitive column name) and no query here names them. Attachments are described
/// by kind and file name only — never the storage path or the Telegram file id.</para>
/// </summary>
[McpServerToolType]
public sealed class MessagingTools(McpToolContext t)
{
    public static readonly Type[] Reads =
    [
        typeof(Broadcast), typeof(MessageDelivery), typeof(MessageAttachment), typeof(StudentGroup), typeof(Group),
        typeof(Student), typeof(StudentAchievement),
    ];

    /// <summary>Longest range of the delivery log / auto statistics (the panel's auto-stats limit).</summary>
    public const int MaxLogDays = 92;

    /// <summary>Longest plain message text returned per delivery row.</summary>
    public const int MaxDeliveryText = 300;

    private static readonly string[] ReportStatuses = ["sent", "acked", "failed", "blocked", "pending", "cancelled"];

    private static readonly string[] DeliveryStatuses =
    [
        DeliveryStatus.Pending, DeliveryStatus.Sending, DeliveryStatus.Sent, DeliveryStatus.Failed,
        DeliveryStatus.Blocked, DeliveryStatus.Unknown, DeliveryStatus.Expired, DeliveryStatus.Cancelled,
    ];

    // =====================================================================================
    //  message_campaign_report — MessagingController campaigns/{id} + campaigns/{id}/deliveries
    // =====================================================================================

    [McpServerTool(Name = "message_campaign_report", Title = "Telegram e'lon hisoboti",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bitta Telegram e'lonining (kampaniyaning) hisoboti — «Xabarlar → Tarix» dagi hisobot oynasi bilan bir xil: "
        + "turi (broadcast — e'lon, archive — arxivdagilarga, debt — qarz eslatmasi), holati (queued/sending/done/cancelled), "
        + "yuborilgan matn (oddiy matn), fayl (turi va nomi), auditoriya qisqacha, jami/yetkazildi/xato/bloklagan/navbatda/"
        + "«Qabul qildim» bosganlar soni va qarindoshlik (otasi/onasi/o'zi…) kesimi. Ro'yxat O'QUVCHI bo'yicha sahifalangan: "
        + "har o'quvchida guruhlari va har chat holati (yuborildi, qabul qildi vaqti, xato sababi). Holat filtri: sent | acked | "
        + "failed | blocked | pending | cancelled; o'quvchi ismi bo'yicha qidiruv. Telegram chat id HECH QACHON chiqmaydi. "
        + "Kampaniya id — messages_history natijasidan. "
        + "One Telegram campaign: delivery and acknowledgement report per student and chat.")]
    public async Task<string> MessageCampaignReport(
        [Description("Kampaniya (e'lon) id — messages_history natijasidan")] string campaignId,
        [Description("Holat: sent | acked | failed | blocked | pending | cancelled (sukut — hammasi)")] string? status = null,
        [Description("O'quvchi (oluvchi) ismi bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi — o'quvchilar soni (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Messages);
        if (string.IsNullOrWhiteSpace(campaignId)) throw new McpException("campaignId kerak.");
        var st = (status ?? "").Trim().ToLowerInvariant();
        if (st.Length > 0 && !ReportStatuses.Contains(st))
            throw new McpException("«status»: " + string.Join(" | ", ReportStatuses) + ".");
        var id = campaignId.Trim();
        var db = t.Db;

        var detail = await MessagingReports.CampaignAsync(db, id, ct);
        if (detail is null) return t.Json(new { message = "Kampaniya topilmadi." }, 0);

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var report = await MessagingReports.DeliveriesAsync(db, id, st.Length == 0 ? null : st, search, p, size, ct);

        // Relation breakdown over ALL rows of the campaign (not just this page) — one grouped query.
        var byRelation = (await db.MessageDeliveries.Where(d => d.BroadcastId == id)
                .GroupBy(d => new { d.Relation, d.Status, Acked = d.AckAt != null })
                .Select(g => new { g.Key.Relation, g.Key.Status, g.Key.Acked, N = g.Count() })
                .ToListAsync(ct))
            .GroupBy(r => TelegramRelations.Effective(r.Relation))
            .OrderBy(g => Array.IndexOf(TelegramRelations.All, g.Key))
            .Select(g => new
            {
                relation = g.Key,
                label = TelegramRelations.Label(g.Key),
                total = g.Sum(x => x.N),
                sent = g.Where(x => x.Status == DeliveryStatus.Sent).Sum(x => x.N),
                acked = g.Where(x => x.Acked).Sum(x => x.N),
            }).ToList();

        var audience = detail.Audience;
        var items = (report?.Items ?? []).Select(s => new
        {
            s.StudentId, s.StudentName,
            groups = s.GroupNames,
            chats = s.Rows.Select(r => new
            {
                r.DeliveryId,
                r.Relation,
                relationLabel = TelegramRelations.Label(r.Relation),
                r.Status,
                statusLabel = DeliveryStatus.Label(r.Status),
                r.SentAt, r.AckAt,
                error = string.IsNullOrEmpty(r.Error) ? null : r.Error,
            }),
        }).ToList();

        return t.Json(new
        {
            campaign = new
            {
                detail.Id, detail.Kind, kindLabel = MessagingReports.TriggerLabel(TelegramOutbox.SourceCampaign, detail.Kind),
                audienceLabel = detail.Title,
                text = TelegramHtml.ToPlain(detail.Html),
                sender = detail.SenderName, detail.CreatedAt, detail.Status,
                detail.AckRequired,
                attachment = detail.Attachment is null ? null : new { kind = detail.Attachment.Kind, fileName = detail.Attachment.FileName },
                audience = audience is null ? null : new
                {
                    kind = audience.Kind,
                    groups = audience.GroupIds?.Count ?? 0,
                    courses = audience.CourseIds?.Count ?? 0,
                    teacherId = audience.TeacherId,
                    memberStatuses = audience.MemberStatuses,
                    audience.OnlyDebtors,
                    selectedStudents = audience.StudentIds?.Count ?? 0,
                    relations = audience.Relations,
                    audience.ArchivedFrom, audience.ArchivedTo, audience.ArchiveReason,
                    teachers = audience.TeacherIds?.Count ?? 0,
                },
            },
            counts = new
            {
                total = detail.Total, sent = detail.Sent, failed = detail.Failed, blocked = detail.Blocked,
                pending = detail.Pending, acked = detail.Acked,
            },
            byRelation,
            status = st.Length == 0 ? null : st,
            note = "«acked» — «✅ Qabul qildim» tugmasini bosganlar (Telegram «o'qildi» belgisini bermaydi, bu yagona "
                + "«ko'rdi» holati). «failed» ga xato, noma'lum (server qayta yuklangan) va muddati o'tganlar kiradi. "
                + "Eski e'lonlarda (yetkazish jurnali yo'q) faqat jami va yetkazilganlar soni bor.",
            total = report?.Total ?? 0, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  message_deliveries — the per-recipient log (outbox rows)
    // =====================================================================================

    [McpServerTool(Name = "message_deliveries", Title = "Telegram xabarlar jurnali",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ota-ona va o'quvchilarga ketgan Telegram xabarlarining BITTA-BITTA jurnali (yangisi tepada): qachon "
        + "yaratilgan va yuborilgan, manba (campaign — e'lon, auto — avto-xabar), hodisa (masalan attendance_absent, "
        + "payment_debt, birthday, achievement) va uning nomi, o'quvchi, chat egasi (otasi/onasi/o'zi…), holat (sent, "
        + "pending, failed, blocked, expired, cancelled, unknown), «Qabul qildim» vaqti, xato sababi va matn (oddiy, "
        + "300 belgigacha). Filtrlar: o'quvchi, kampaniya, hodisa, manba, holat, faqat qabul qilganlar, sana oralig'i (sukut — "
        + "oxirgi 30 kun, ko'pi bilan 92). Jamlanma — filtr bo'yicha holatlar soni. O'quvchi berilsa «O'quvchilar» ruxsati ham "
        + "yetadi (profil «Telegram xabarlari» kabi). Telegram chat id chiqmaydi. "
        + "Per-recipient Telegram delivery log with student, trigger, campaign, status and date filters.")]
    public async Task<string> MessageDeliveries(
        [Description("Faqat shu o'quvchi haqidagi xabarlar (students_search natijasidan id)")] string? studentId = null,
        [Description("Faqat shu kampaniya (messages_history natijasidan id)")] string? campaignId = null,
        [Description("Hodisa kaliti (auto_message_rules dagi trigger) yoki kampaniya turi: broadcast | archive | debt")] string? trigger = null,
        [Description("Manba: campaign | auto")] string? source = null,
        [Description("Holat: pending | sending | sent | failed | blocked | unknown | expired | cancelled")] string? status = null,
        [Description("true — faqat «Qabul qildim» bosilganlar; false — bosilmaganlar")] bool? acked = null,
        [Description("Yaratilgan sana — dan, YYYY-MM-DD (sukut — 'to' dan 29 kun oldin)")] string? from = null,
        [Description("Yaratilgan sana — gacha (shu kun ham kiradi), YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        var sid = string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim();
        // One student's log is part of the profile (panel: students.list OR messages.broadcast); the whole log is
        // the messaging module's.
        if (sid is null || !t.CanReadArea(McpAreas.Students)) t.Require(McpAreas.Messages);
        var src = (source ?? "").Trim().ToLowerInvariant();
        if (src.Length > 0 && src is not (TelegramOutbox.SourceCampaign or TelegramOutbox.SourceAuto))
            throw new McpException("«source»: campaign | auto.");
        var stat = (status ?? "").Trim().ToLowerInvariant();
        if (stat.Length > 0 && !DeliveryStatuses.Contains(stat))
            throw new McpException("«status»: " + string.Join(" | ", DeliveryStatuses) + ".");
        var (f, tt) = McpToolContext.Range(from, to, defaultDays: 30, maxDays: MaxLogDays);
        var db = t.Db;

        var fromDt = f.ToDateTime(TimeOnly.MinValue);
        var toDt = tt.AddDays(1).ToDateTime(TimeOnly.MinValue);
        // Staff rows (teacher recipients) are not part of the parent/student log — the profile excludes them too.
        var q = db.MessageDeliveries.Where(d => d.CreatedAt >= fromDt && d.CreatedAt < toDt && d.TeacherId == null);
        if (sid is not null) q = q.Where(d => d.StudentId == sid);
        if (!string.IsNullOrWhiteSpace(campaignId))
        {
            var cid = campaignId.Trim();
            q = q.Where(d => d.BroadcastId == cid);
        }
        if (!string.IsNullOrWhiteSpace(trigger))
        {
            var trig = trigger.Trim().ToLowerInvariant();
            q = q.Where(d => d.Trigger == trig);
        }
        if (src.Length > 0) q = q.Where(d => d.Source == src);
        if (stat.Length > 0) q = q.Where(d => d.Status == stat);
        if (acked == true) q = q.Where(d => d.AckAt != null);
        else if (acked == false) q = q.Where(d => d.AckAt == null);

        var byStatus = (await q.GroupBy(d => d.Status).Select(g => new { Status = g.Key, N = g.Count() }).ToListAsync(ct))
            .OrderBy(x => Array.IndexOf(DeliveryStatuses, x.Status))
            .Select(x => new { status = x.Status, label = DeliveryStatus.Label(x.Status), count = x.N })
            .ToList();
        var total = byStatus.Sum(x => x.count);
        var ackedCount = await q.CountAsync(d => d.AckAt != null, ct);

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(d => new
            {
                d.Id, d.CreatedAt, d.SentAt, d.Source, d.Trigger, d.BroadcastId, d.StudentId, d.RecipientName,
                d.Relation, d.Status, d.AckAt, d.AckRequired, d.Attempts, d.Error, d.Text,
            }).ToListAsync(ct);

        var items = rows.Select(d =>
        {
            var plain = TelegramHtml.ToPlain(d.Text);
            if (plain.Length > MaxDeliveryText) plain = plain[..MaxDeliveryText] + "…";
            return new
            {
                d.Id,
                createdAt = Iso(d.CreatedAt), sentAt = Iso(d.SentAt),
                source = d.Source,
                trigger = d.Trigger,
                label = MessagingReports.TriggerLabel(d.Source, d.Trigger),
                campaignId = d.BroadcastId,
                student = d.StudentId is null ? null : new { id = d.StudentId, name = d.RecipientName },
                relation = TelegramRelations.Effective(d.Relation),
                relationLabel = TelegramRelations.Label(d.Relation),
                d.Status,
                statusLabel = DeliveryStatus.Label(d.Status),
                d.AckRequired,
                ackAt = Iso(d.AckAt),
                d.Attempts,
                error = string.IsNullOrEmpty(d.Error) ? null : d.Error,
                text = plain,
            };
        }).ToList();

        return t.Json(new
        {
            from = McpToolContext.Iso(f), to = McpToolContext.Iso(tt),
            summary = new { total, acked = ackedCount, byStatus },
            note = "Har qator — bitta chatga bitta xabar. «expired» — muddatida (e'lon 12 soat, avto-xabar 6 soat) "
                + "yuborilmagan; «unknown» — yuborilayotganda server qayta yuklangan (qayta yuborilmaydi).",
            total, page = p, pageSize = size,
            items,
        }, items.Count);
    }

    // =====================================================================================
    //  auto_message_stats — MessagingController auto-stats (MessagingReports.AutoStatsAsync)
    // =====================================================================================

    [McpServerTool(Name = "auto_message_stats", Title = "Avto-xabarlar statistikasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Avtomatik Telegram xabarlari statistikasi («Xabarlar → Tarix → Avto xabarlar» bilan bir xil): har kun × "
        + "hodisa (kelmadi, keldi, to'lov, qarzdorlik, tug'ilgan kun, yutuq…) bo'yicha jami, yetkazildi, xato, bloklagan va "
        + "«Qabul qildim» bosganlar; hamda hodisalar bo'yicha davr jamlanmasi. Sana oralig'i (sukut — oxirgi 7 kun, ko'pi "
        + "bilan 92). Daily automatic-message delivery statistics by trigger.")]
    public async Task<string> AutoMessageStats(
        [Description("Sana — dan, YYYY-MM-DD (sukut — 'to' dan 6 kun oldin)")] string? from = null,
        [Description("Sana — gacha (shu kun ham kiradi), YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Faqat shu hodisa kaliti (masalan attendance_absent)")] string? trigger = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Messages);
        var (f, tt) = McpToolContext.Range(from, to, defaultDays: 7, maxDays: MaxLogDays);
        var trig = (trigger ?? "").Trim().ToLowerInvariant();
        var rows = await MessagingReports.AutoStatsAsync(t.Db, McpToolContext.Iso(f), McpToolContext.Iso(tt), ct);
        if (trig.Length > 0) rows = rows.Where(r => r.Trigger == trig).ToList();

        var byTrigger = rows.GroupBy(r => r.Trigger)
            .Select(g => new
            {
                trigger = g.Key, label = g.First().Label,
                total = g.Sum(r => r.Total), sent = g.Sum(r => r.Sent), failed = g.Sum(r => r.Failed),
                blocked = g.Sum(r => r.Blocked), acked = g.Sum(r => r.Acked),
            })
            .OrderByDescending(x => x.total).ThenBy(x => x.trigger, StringComparer.Ordinal)
            .ToList();
        var days = rows.Select(r => new { r.Date, r.Trigger, r.Label, r.Total, r.Sent, r.Failed, r.Blocked, r.Acked }).ToList();

        return t.Json(new
        {
            from = McpToolContext.Iso(f), to = McpToolContext.Iso(tt),
            totals = new
            {
                total = rows.Sum(r => r.Total), sent = rows.Sum(r => r.Sent), failed = rows.Sum(r => r.Failed),
                blocked = rows.Sum(r => r.Blocked), acked = rows.Sum(r => r.Acked),
            },
            byTrigger,
            days,
            note = "Kun — xabar NAVBATGA qo'yilgan kun. «failed» ga xato, noma'lum va muddati o'tganlar kiradi.",
        }, days.Count);
    }

    // =====================================================================================
    //  student_achievements — StudentAchievementsController.List
    // =====================================================================================

    [McpServerTool(Name = "student_achievements", Title = "O'quvchilar yutuqlari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchilar yutuqlari (IELTS, CEFR, sertifikat, olimpiada va boshqalar) — profil «Yutuqlar» tabi bilan bir "
        + "xil: o'quvchi, turi, nomi, natijasi (masalan 7.5 yoki B2), sanasi, izoh, rasmi bormi, ota-onaga tabrik qachon "
        + "yuborilgani va kim kiritgani. Yangisidan eskisiga. Filtrlar: o'quvchi, turi (ielts | cefr | certificate | olympiad | "
        + "other), sana oralig'i (ko'pi bilan 366 kun), nomi bo'yicha qidiruv. Jamlanma — turlar bo'yicha soni. Rasm fayli "
        + "chiqmaydi. Student achievements (IELTS, CEFR, certificates, olympiads) with filters.")]
    public async Task<string> StudentAchievements(
        [Description("Faqat shu o'quvchi (students_search natijasidan id)")] string? studentId = null,
        [Description("Turi: ielts | cefr | certificate | olympiad | other")] string? kind = null,
        [Description("Yutuq sanasi — dan, YYYY-MM-DD")] string? from = null,
        [Description("Yutuq sanasi — gacha, YYYY-MM-DD")] string? to = null,
        [Description("Yutuq nomi yoki natijasi bo'lagi")] string? search = null,
        [Description("Sahifa (1 dan)")] int? page = null,
        [Description("Sahifa hajmi (1..200, sukut 50)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Students);
        var k = (kind ?? "").Trim().ToLowerInvariant();
        if (k.Length > 0 && !AchievementService.Kinds.Contains(k))
            throw new McpException("«kind»: " + string.Join(" | ", AchievementService.Kinds) + ".");
        string? fromIso = null, toIso = null;
        if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
        {
            var (f, tt) = McpToolContext.Range(from, to, defaultDays: McpToolContext.MaxRangeDays);
            fromIso = McpToolContext.Iso(f);
            toIso = McpToolContext.Iso(tt);
        }
        var db = t.Db;

        var q = db.StudentAchievements.AsQueryable();
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var sid = studentId.Trim();
            q = q.Where(a => a.StudentId == sid);
        }
        if (k.Length > 0) q = q.Where(a => a.Kind == k);
        if (fromIso is not null) q = q.Where(a => string.Compare(a.Date, fromIso) >= 0);
        if (toIso is not null) q = q.Where(a => string.Compare(a.Date, toIso) <= 0);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(a => a.Title.ToLower().Contains(term) || a.Result.ToLower().Contains(term));
        }

        var byKind = (await q.GroupBy(a => a.Kind).Select(g => new { Kind = g.Key, N = g.Count() }).ToListAsync(ct))
            .OrderBy(x => Array.IndexOf(AchievementService.Kinds, x.Kind))
            .Select(x => new { kind = x.Kind, label = AchievementService.KindLabel(x.Kind), count = x.N })
            .ToList();
        var total = byKind.Sum(x => x.count);

        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(a => a.Date).ThenByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(a => new
            {
                a.Id, a.StudentId, a.Kind, a.Title, a.Result, a.Date, a.Note,
                HasPhoto = a.AttachmentId != null && a.AttachmentId != "",
                a.CongratulatedAt, a.CongratulationCount, a.CreatedAt, a.CreatedByName,
            }).ToListAsync(ct);
        var ids = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.IsArchived }).ToDictionaryAsync(s => s.Id, ct);

        var items = rows.Select(r => new
        {
            r.Id,
            student = new
            {
                id = r.StudentId,
                name = students.GetValueOrDefault(r.StudentId)?.FullName ?? "",
                isArchived = students.GetValueOrDefault(r.StudentId)?.IsArchived ?? false,
            },
            r.Kind, kindLabel = AchievementService.KindLabel(r.Kind),
            r.Title, r.Result, r.Date, r.Note, r.HasPhoto,
            congratulatedAt = Iso(r.CongratulatedAt), r.CongratulationCount,
            createdAt = Iso(r.CreatedAt), createdBy = r.CreatedByName,
        }).ToList();

        return t.Json(new { total, byKind, page = p, pageSize = size, items }, items.Count);
    }

    private static string? Iso(DateTime? v) => v?.ToString("yyyy-MM-ddTHH:mm:ss");
}
