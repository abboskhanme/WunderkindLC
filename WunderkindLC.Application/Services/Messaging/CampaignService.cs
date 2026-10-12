using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>A campaign planned but not saved: the exact rows <c>POST campaigns</c> would write.</summary>
public sealed class CampaignPlan
{
    public required AudienceResult Audience { get; init; }
    public List<OutboxItem> Items { get; } = new();
    public bool PerChat { get; init; }
    public int Chats => Items.Select(i => i.ChatId).Distinct().Count();

    public AudienceCountDto ToCount()
    {
        var byRelation = Items.GroupBy(i => i.Relation)
            .Select(g => new RelationCountDto(g.Key, g.Select(i => i.ChatId).Distinct().Count()))
            .OrderBy(r => Array.IndexOf(TelegramRelations.All, r.Relation))
            .ToList();
        var subjects = Audience.IsTeachers ? Audience.Teachers.Count : Audience.Students.Count;
        var names = Items.Select(i => i.RecipientName).Where(n => n.Length > 0).Distinct().Take(5).ToList();
        return new AudienceCountDto(subjects, Chats, Items.Count, byRelation, Audience.NotConnected, Audience.Blocked,
            PerChat, names);
    }
}

/// <summary>
/// TELEGRAM CAMPAIGNS (SPEC P2.7 <c>POST campaigns</c>): validate → resolve audience → render EVERY message → write
/// the <see cref="Broadcast"/> header + all outbox rows in ONE <c>SaveChanges</c>. Rendering happens at enqueue time —
/// what was previewed is what is stored. The pre-send count (<c>audience/count</c>) runs the SAME planner, so the
/// number shown equals the rows created.
/// </summary>
public static class CampaignService
{
    public const string KindBroadcast = "broadcast";
    public const string KindArchive = "archive";
    public const string KindDebt = "debt";
    public const int MaxMessages = 20_000;

    public const string ErrEmpty = "Matn yoki fayl kerak";
    public const string ErrNoRecipients = "Oluvchi topilmadi";
    public const string ErrTooMany = "Juda ko'p oluvchi (20 000 dan oshmasin)";
    public const string ErrArchiveOnly = "Arxiv xabarida faqat arxivdagi o'quvchilar";
    public const string ErrUseArchive = "Arxivdagilarga alohida «Arxiv» xabaridan yuboring";
    public const string ErrDebtAudience = "Qarz eslatmasi faqat faol yoki tanlangan o'quvchilarga yuboriladi";
    public const string ErrBotOff = "Telegram bot sozlanmagan (.env: TELEGRAM_BOT_TOKEN)";
    public const string ErrAttachment = "Biriktirilgan fayl topilmadi — qaytadan yuklang";

    public static bool IsKind(string? kind) => kind is KindBroadcast or KindArchive or KindDebt;

    /// <summary>Template errors (HTML + unknown tokens) — empty list when the template is fine.</summary>
    public static List<string> TemplateErrors(string? template)
    {
        var errors = TelegramHtml.SanitizeTemplate(template).Errors.ToList();
        var unknown = MessageRender.UnknownTokens(template);
        if (unknown.Count > 0) errors.Add(MessageRender.UnknownTokensError(unknown));
        return errors;
    }

    /// <summary>Audience ↔ kind rules (Uzbek error or null). Needs the resolved audience for the "selected" mix check.</summary>
    private static string? KindError(string kind, AudienceSpec spec, AudienceResult audience)
    {
        switch (kind)
        {
            case KindArchive:
                if (spec.Kind == MessageAudienceResolver.KindArchive) return null;
                if (spec.Kind == MessageAudienceResolver.KindSelected && audience.Students.All(s => s.IsArchived)) return null;
                return ErrArchiveOnly;
            case KindBroadcast:
                if (spec.Kind == MessageAudienceResolver.KindArchive) return ErrUseArchive;
                if (spec.Kind == MessageAudienceResolver.KindSelected && audience.Students.Any(s => s.IsArchived)) return ErrUseArchive;
                return null;
            case KindDebt:
                if (spec.Kind is not (MessageAudienceResolver.KindActive or MessageAudienceResolver.KindSelected)) return ErrDebtAudience;
                if (audience.Students.Any(s => s.IsArchived)) return ErrUseArchive;
                return null;
            default:
                return "Xabar turi noto'g'ri";
        }
    }

    /// <summary>
    /// Plans a campaign. <paramref name="render"/> false skips text rendering (count only — the row count does not
    /// depend on the text except through <see cref="MessageRender.HasStudentTokens"/>).
    /// </summary>
    public static async Task<(CampaignPlan? Plan, string? Error)> PlanAsync(
        IAppDbContext db, string kind, AudienceSpec spec, string? template, string? attachmentId, bool ackRequired,
        bool render, string? createdById = null, CancellationToken ct = default)
    {
        if (!IsKind(kind)) return (null, "Xabar turi noto'g'ri");
        var specError = MessageAudienceResolver.Validate(spec);
        if (specError is not null) return (null, specError);

        var audience = await MessageAudienceResolver.ResolveAsync(db, spec, ct);
        var kindError = KindError(kind, spec, audience);
        if (kindError is not null) return (null, kindError);

        var tpl = template ?? "";
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var center = meta?.Name ?? "";
        var header = MessageRender.HeaderForCampaign(kind);
        var perChat = audience.IsTeachers || (kind != KindDebt && !MessageRender.HasStudentTokens(tpl));
        var plan = new CampaignPlan { Audience = audience, PerChat = perChat };

        OutboxItem Item(long chatId, string text, Student? s, Teacher? t, TelegramRegistration reg) => new(
            TelegramOutbox.SourceCampaign, kind, chatId, text,
            StudentId: s?.Id, TeacherId: t?.Id, RegistrationId: reg.Id,
            Relation: t is not null ? "" : TelegramRelations.Effective(reg.Relation),
            RecipientName: s?.FullName ?? t?.FullName ?? "",
            AttachmentId: attachmentId, AckRequired: ackRequired, CreatedById: createdById);

        if (audience.IsTeachers)
        {
            foreach (var target in audience.Targets.GroupBy(x => x.Reg.ChatId).Select(g => g.First()))
            {
                var text = render ? header + MessageRender.RenderTeacher(tpl, target.Teacher!, null, null, center).Html : "";
                plan.Items.Add(Item(target.Reg.ChatId, text, null, target.Teacher, target.Reg));
            }
            return (plan, null);
        }

        // Group context ({guruh}, {oqituvchi}, {dars_*}): the ONE selected group, else the student's main group.
        var groupByName = render ? await DebtMessages.GroupsByNameAsync(db, ct) : new Dictionary<string, Group>();
        var teacherNames = render ? await MessageTokenizer.TeacherNamesByIdAsync(db, ct) : new Dictionary<string, string>();
        Group? scopeGroup = null;
        if (render && spec.GroupIds is { Count: 1 } one)
            scopeGroup = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == one[0], ct);

        if (kind == KindDebt)
        {
            var byStudent = audience.Targets.GroupBy(t => t.Student!.Id).ToDictionary(g => g.Key, g => g.ToList());
            var namesForDebt = render ? groupByName : await DebtMessages.GroupsByNameAsync(db, ct);
            foreach (var s in audience.Students)
            {
                if (!byStudent.TryGetValue(s.Id, out var targets)) continue;
                var items = await DebtMessages.BuildForStudentAsync(db, s, namesForDebt, ct);
                foreach (var item in items)
                    foreach (var target in targets)
                    {
                        var text = "";
                        if (render)
                        {
                            text = string.IsNullOrWhiteSpace(tpl)
                                ? TelegramHtml.Escape(item.Body)
                                : MessageRender.RenderStudent(tpl, s, target.Reg, item.Group,
                                    MessageTokenizer.TeacherNameOf(item.Group, teacherNames),
                                    DebtMessages.Extra(item), center).Html;
                        }
                        plan.Items.Add(Item(target.Reg.ChatId, text, s, null, target.Reg));
                    }
            }
            return (plan, null);
        }

        IEnumerable<AudienceTarget> chosen;
        if (perChat)
        {
            // ONE message per chat, rendered for the chat's first student (FullName, then Id — the audience order).
            var order = audience.Students.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
            chosen = audience.Targets.GroupBy(t => t.Reg.ChatId)
                .Select(g => g.OrderBy(t => order[t.Student!.Id]).First())
                .OrderBy(t => order[t.Student!.Id]);
        }
        else chosen = audience.Targets;

        foreach (var target in chosen)
        {
            var s = target.Student!;
            var text = "";
            if (render)
            {
                var grp = scopeGroup ?? groupByName.GetValueOrDefault(s.ClassName ?? "");
                var extra = scopeGroup is null ? null : new Dictionary<string, string> { ["{guruh}"] = scopeGroup.Name };
                text = header + MessageRender.RenderStudent(tpl, s, target.Reg, grp,
                    MessageTokenizer.TeacherNameOf(grp, teacherNames), extra, center).Html;
            }
            plan.Items.Add(Item(target.Reg.ChatId, text, s, null, target.Reg));
        }
        return (plan, null);
    }

    /// <summary><c>POST campaigns</c>. Returns the created campaign or an Uzbek 400 reason.</summary>
    public static async Task<(CampaignCreatedDto? Created, string? Error)> CreateAsync(
        IAppDbContext db, AuditService? audit, CreateCampaignRequest req, string? actorId, string actorName,
        bool botConfigured, CancellationToken ct = default)
    {
        if (!botConfigured) return (null, ErrBotOff);
        var kind = (req.Kind ?? "").Trim();
        if (!IsKind(kind)) return (null, "Xabar turi noto'g'ri");
        var tpl = (req.Template ?? "").Trim();

        MessageAttachment? attachment = null;
        if (!string.IsNullOrWhiteSpace(req.AttachmentId))
        {
            attachment = await db.MessageAttachments.FirstOrDefaultAsync(a => a.Id == req.AttachmentId, ct);
            if (attachment is null || (attachment.FileDeletedAt is not null && attachment.TelegramFileId.Length == 0))
                return (null, ErrAttachment);
        }
        if (tpl.Length == 0 && attachment is null && kind != KindDebt) return (null, ErrEmpty);

        var templateErrors = TemplateErrors(tpl);
        if (templateErrors.Count > 0) return (null, templateErrors[0]);

        var (plan, error) = await PlanAsync(db, kind, req.Audience, tpl, attachment?.Id, req.AckRequired,
            render: true, createdById: actorId, ct);
        if (error is not null) return (null, error);
        if (plan!.Items.Count == 0) return (null, ErrNoRecipients);
        if (plan.Items.Count > MaxMessages) return (null, ErrTooMany);

        var tooLong = plan.Items.FirstOrDefault(i => TelegramHtml.VisibleLength(i.Text) > TelegramHtml.MaxText);
        if (tooLong is not null)
            return (null, $"Xabar juda uzun ({TelegramHtml.VisibleLength(tooLong.Text)} belgi, {tooLong.RecipientName}) — 4096 dan oshmasin");

        var now = AppClock.Now;
        var bc = new Broadcast
        {
            Kind = kind,
            ClassName = plan.Audience.Label,
            Text = tpl,
            SenderUserId = actorId ?? "",
            SenderName = actorName,
            CreatedAt = now,
            RecipientCount = plan.Items.Count,
            SentCount = 0,
            AudienceJson = JsonSerializer.Serialize(req.Audience, JsonOpts),
            AttachmentId = attachment?.Id,
            AckRequired = req.AckRequired,
            Status = "queued",
        };
        db.Broadcasts.Add(bc);
        foreach (var item in plan.Items)
            db.MessageDeliveries.Add(TelegramOutbox.Build(item with { BroadcastId = bc.Id }, now));

        var plain = TelegramHtml.ToPlain(TelegramHtml.SanitizeTemplate(tpl).Html);
        audit?.Record("Broadcast", bc.Id, "create",
            $"Telegram xabar yuborildi: {plan.Audience.Label}, {plan.Items.Count} ta xabar"
            + (plain.Length == 0 ? "" : " — " + (plain.Length > 120 ? plain[..120] + "…" : plain)));
        await db.SaveChangesAsync(ct);

        var students = plan.Audience.IsTeachers
            ? plan.Items.Select(i => i.TeacherId).Distinct().Count()
            : plan.Items.Select(i => i.StudentId).Distinct().Count();
        return (new CampaignCreatedDto(bc.Id, plan.Items.Count, plan.Chats, students), null);
    }

    /// <summary><c>POST campaigns/{id}/cancel</c>: pending rows → cancelled. Returns null when not found.</summary>
    public static async Task<int?> CancelAsync(IAppDbContext db, AuditService? audit, string id, CancellationToken ct = default)
    {
        var bc = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bc is null) return null;
        var pending = await db.MessageDeliveries
            .Where(d => d.BroadcastId == id && d.Status == DeliveryStatus.Pending).ToListAsync(ct);
        foreach (var d in pending)
        {
            d.Status = DeliveryStatus.Cancelled;
            d.Error = "Bekor qilindi";
        }
        if (bc.Status is not "done")
        {
            bc.Status = "cancelled";
            bc.FinishedAt = AppClock.Now;
        }
        var cancelled = pending.Count;
        audit?.Record("Broadcast", bc.Id, "update", $"Telegram xabar bekor qilindi ({cancelled} ta): {bc.ClassName}");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                // The worker claimed some rows meanwhile — they go out; only the conflicting rows are dropped.
                foreach (var e in ex.Entries)
                {
                    if (e.Entity is MessageDelivery) cancelled--;
                    e.State = EntityState.Detached;
                }
            }
        }
        return cancelled;
    }

    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static AudienceSpec? ParseAudience(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AudienceSpec>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }
}
