using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// SERVER-SIDE PREVIEW (SPEC P2.7 <c>POST preview</c>): the composer and the auto-rule editor show EXACTLY what the
/// renderer produces for a sample recipient. Template problems are returned in <c>errors</c> (never a 400).
/// </summary>
public static class MessagePreviewService
{
    public const string WarnSeparate = "Matn izoh uchun uzun — fayl va matn ikki alohida xabar bo'lib ketadi";
    public const string WarnNoChat = "Namuna o'quvchida Telegram ulanmagan — ko'rinish karta ma'lumotidan";

    public static async Task<MessagePreviewDto> PreviewAsync(IAppDbContext db, MessagePreviewRequest req, CancellationToken ct = default)
    {
        var tpl = req.Template ?? "";
        var errors = CampaignService.TemplateErrors(tpl);
        var warnings = new List<string>();
        var center = (await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct))?.Name ?? "";

        var hasAttachment = false;
        if (!string.IsNullOrWhiteSpace(req.AttachmentId))
            hasAttachment = await db.MessageAttachments.AnyAsync(a => a.Id == req.AttachmentId, ct);

        var trigger = string.IsNullOrWhiteSpace(req.Trigger) ? null : req.Trigger;
        var kind = string.IsNullOrWhiteSpace(req.Kind) ? CampaignService.KindBroadcast : req.Kind!;
        var header = trigger is not null
            ? (trigger == AutoMessageTriggers.PaymentDebt
                ? MessageRender.HeaderDebt
                : MessageRender.HeaderAuto(AutoMessageTriggers.Get(trigger)?.Label ?? "Xabar"))
            : MessageRender.HeaderForCampaign(kind);
        var extras = MessageRender.PreviewExtras(tpl, trigger);

        // ---- Teachers audience: sample teacher ----
        if (req.Audience?.Kind == MessageAudienceResolver.KindTeachers)
        {
            var ids = req.Audience.TeacherIds ?? new List<string>();
            var teacher = await db.Teachers.AsNoTracking()
                .Where(t => !t.IsArchived && (ids.Count == 0 || ids.Contains(t.Id)))
                .OrderBy(t => t.FullName).FirstOrDefaultAsync(ct);
            var html = teacher is null
                ? header + TelegramHtml.SanitizeTemplate(tpl).Html
                : header + MessageRender.RenderTeacher(tpl, teacher, null, extras, center).Html;
            return Build(html, hasAttachment, errors, warnings,
                teacher is null ? null : new MessagePreviewSample(teacher.Id, teacher.FullName, ""));
        }

        // ---- Sample student ----
        Student? student = null;
        TelegramRegistration? reg = null;
        if (!string.IsNullOrWhiteSpace(req.StudentId))
            student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StudentId, ct);
        if (student is null && req.Audience is not null && MessageAudienceResolver.Validate(req.Audience) is null)
        {
            var audience = await MessageAudienceResolver.ResolveAsync(db, req.Audience, ct);
            var first = audience.Targets.FirstOrDefault();
            student = first?.Student ?? audience.Students.FirstOrDefault();
            reg = first?.Reg;
        }
        student ??= await db.Students.AsNoTracking().Where(s => !s.IsArchived)
            .OrderBy(s => s.FullName).FirstOrDefaultAsync(ct);
        if (student is not null && reg is null)
        {
            var relations = req.Audience is null
                ? TelegramRelations.All
                : MessageAudienceResolver.RelationsOf(req.Audience);
            reg = (await db.TelegramRegistrations.AsNoTracking()
                    .Where(r => r.StudentId == student.Id && r.BlockedAt == null).ToListAsync(ct))
                .OrderBy(r => r.CreatedAt)
                .FirstOrDefault(r => relations.Contains(TelegramRelations.Effective(r.Relation)));
        }
        if (student is null)
            return Build(header + TelegramHtml.SanitizeTemplate(tpl).Html, hasAttachment, errors, warnings, null);
        if (reg is null) warnings.Add(WarnNoChat);
        if (reg is not null) extras.Remove("{qarindosh}");

        var groupByName = await DebtMessages.GroupsByNameAsync(db, ct);
        var group = groupByName.GetValueOrDefault(student.ClassName ?? "");
        var teacherNames = await MessageTokenizer.TeacherNamesByIdAsync(db, ct);
        string body;
        var isDebt = trigger == AutoMessageTriggers.PaymentDebt || (trigger is null && kind == CampaignService.KindDebt);
        if (isDebt)
        {
            var items = await DebtMessages.BuildForStudentAsync(db, student, groupByName, ct);
            var item = items.FirstOrDefault() ?? DebtMessages.Compose(student, group, group?.Name ?? "Ingliz tili", 350_000m);
            body = string.IsNullOrWhiteSpace(tpl)
                ? TelegramHtml.Escape(item.Body)
                : MessageRender.RenderStudent(tpl, student, reg, item.Group,
                    MessageTokenizer.TeacherNameOf(item.Group, teacherNames),
                    Merge(extras, DebtMessages.Extra(item)), center).Html;
        }
        else
        {
            body = MessageRender.RenderStudent(tpl, student, reg, group,
                MessageTokenizer.TeacherNameOf(group, teacherNames), extras, center).Html;
        }
        var sample = new MessagePreviewSample(student.Id, student.FullName, reg is null ? "" : TelegramRelations.Effective(reg.Relation));
        return Build(header + body, hasAttachment, errors, warnings, sample);
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var d = new Dictionary<string, string>(a, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in b) d[k] = v;
        return d;
    }

    private static MessagePreviewDto Build(
        string html, bool hasAttachment, List<string> errors, List<string> warnings, MessagePreviewSample? sample)
    {
        var plain = TelegramHtml.ToPlain(html);
        var visible = plain.Length;
        var mode = TelegramHtml.DeliveryMode(hasAttachment, visible);
        if (mode == "separate") warnings.Add(WarnSeparate);
        if (visible > TelegramHtml.MaxText)
            errors.Add($"Xabar juda uzun ({visible} belgi) — 4096 dan oshmasin");
        return new MessagePreviewDto(html, plain, visible, mode, errors, warnings, sample);
    }
}
