using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// TELEGRAM MESSAGING (CRM upgrade Phase 2, docs/SPEC-crm-upgrade.md P2.7): server-side preview, pre-send recipient
/// count, attachments, campaigns (outbox) with per-recipient reports, automatic-message statistics, the debtors list
/// and manual templates.
/// <para>RBAC: <c>messages.broadcast</c> — reads too (<c>ReadRequiresPerm</c>: names, relations, delivery status);
/// POST → <c>create</c>, PUT → <c>edit</c>, DELETE → <c>delete</c>. Errors are <c>{ message }</c> in Uzbek.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("messages.broadcast", ReadRequiresPerm = true)]
[Route("api/admin/messaging")]
public class MessagingController(
    AppDbContext db, AuditService audit, TelegramService telegram, MessageAttachmentStore store,
    IOutboxSignal outbox) : ControllerBase
{
    private string? ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "Administrator";

    // ---------- Preview & count ----------

    /// <summary>Exactly what the renderer produces for a sample recipient; template problems in <c>errors</c>.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<MessagePreviewDto>> Preview(MessagePreviewRequest req, CancellationToken ct) =>
        await MessagePreviewService.PreviewAsync(db, req, ct);

    /// <summary>Pre-send count — the SAME planner as <c>POST campaigns</c> (count == rows created).</summary>
    [HttpPost("audience/count")]
    public async Task<ActionResult<AudienceCountDto>> Count(AudienceCountRequest req, CancellationToken ct)
    {
        var kind = string.IsNullOrWhiteSpace(req.Kind)
            ? (req.Audience?.Kind == MessageAudienceResolver.KindArchive ? CampaignService.KindArchive : CampaignService.KindBroadcast)
            : req.Kind!;
        var (plan, error) = await CampaignService.PlanAsync(db, kind, req.Audience!, req.Template, null, false,
            render: false, ct: ct);
        if (error is not null) return BadRequest(new { message = error });
        return plan!.ToCount();
    }

    // ---------- Attachments ----------

    [HttpPost("attachments")]
    [RequestSizeLimit(52_428_800)]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    public async Task<ActionResult<MessageAttachmentDto>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = MessageAttachmentRules.ErrEmpty });
        await using var stream = file.OpenReadStream();
        var (a, error) = await MessageAttachments.CreateAsync(db, store, stream, file.FileName, file.ContentType,
            file.Length, ActorId, ActorName, ct);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync(ct);
        return new MessageAttachmentDto(a!.Id, a.Kind, a.FileName, a.SizeBytes, a.ContentType);
    }

    /// <summary>The file itself (composer thumbnail / report) — never a static <c>/uploads</c> path.</summary>
    [HttpGet("attachments/{id}/content")]
    public async Task<IActionResult> AttachmentContent(string id, CancellationToken ct)
    {
        var a = await db.MessageAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.FileDeletedAt is not null || !store.Exists(a.FileUrl))
            return NotFound(new { message = "Fayl topilmadi yoki o'chirilgan" });
        Response.Headers.CacheControl = "private, no-store";
        return File(store.OpenRead(a.FileUrl), a.ContentType, a.Kind == "photo" ? null : a.FileName, enableRangeProcessing: true);
    }

    // ---------- Campaigns ----------

    [HttpPost("campaigns")]
    public async Task<ActionResult<CampaignCreatedDto>> CreateCampaign(CreateCampaignRequest req, CancellationToken ct)
    {
        var (created, error) = await CampaignService.CreateAsync(db, audit, req, ActorId, ActorName, telegram.IsConfigured, ct);
        if (error is not null) return BadRequest(new { message = error });
        outbox.Wake();
        return StatusCode(StatusCodes.Status202Accepted, created);
    }

    [HttpGet("campaigns")]
    public async Task<ActionResult<Paged<CampaignRowDto>>> Campaigns(
        [FromQuery] string? kind, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        await MessagingReports.CampaignsAsync(db, kind, from, to, page, pageSize, ct);

    [HttpGet("campaigns/{id}")]
    public async Task<ActionResult<CampaignDetailDto>> Campaign(string id, CancellationToken ct)
    {
        var dto = await MessagingReports.CampaignAsync(db, id, ct);
        return dto is null ? NotFound(new { message = "Xabar topilmadi" }) : dto;
    }

    [HttpGet("campaigns/{id}/deliveries")]
    public async Task<ActionResult<Paged<CampaignStudentDeliveriesDto>>> Deliveries(
        string id, [FromQuery] string? status, [FromQuery] string? q,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var dto = await MessagingReports.DeliveriesAsync(db, id, status, q, page, pageSize, ct);
        return dto is null ? NotFound(new { message = "Xabar topilmadi" }) : dto;
    }

    [HttpPost("campaigns/{id}/cancel")]
    public async Task<ActionResult<CampaignCancelledDto>> Cancel(string id, CancellationToken ct)
    {
        var n = await CampaignService.CancelAsync(db, audit, id, ct);
        return n is null ? NotFound(new { message = "Xabar topilmadi" }) : new CampaignCancelledDto(n.Value);
    }

    // ---------- Reports ----------

    [HttpGet("auto-stats")]
    public async Task<ActionResult<List<AutoStatsRowDto>>> AutoStats(
        [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct) =>
        await MessagingReports.AutoStatsAsync(db, from, to, ct);

    [HttpGet("debtors")]
    public async Task<ActionResult<List<DebtorDto>>> Debtors(
        [FromQuery] string? groupId, [FromQuery] string? courseId, [FromQuery] string? teacherId,
        [FromQuery] decimal? minDebt, [FromQuery] string? memberStatus, CancellationToken ct) =>
        await MessagingReports.DebtorsAsync(db, groupId, courseId, teacherId, minDebt, memberStatus, ct);

    // ---------- Manual templates (table SmsTemplates — historical name) ----------

    [HttpGet("templates")]
    public async Task<ActionResult<List<MessageTemplateDto>>> Templates(CancellationToken ct) =>
        (await db.SmsTemplates.AsNoTracking().OrderBy(t => t.Order).ThenBy(t => t.Name).ToListAsync(ct))
        .Select(ToDto).ToList();

    [HttpGet("templates/{id}")]
    public async Task<ActionResult<MessageTemplateDto>> Template(string id, CancellationToken ct)
    {
        var t = await db.SmsTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? NotFound(new { message = "Shablon topilmadi" }) : ToDto(t);
    }

    [HttpPost("templates")]
    public async Task<ActionResult<MessageTemplateDto>> CreateTemplate(SaveMessageTemplateRequest req, CancellationToken ct)
    {
        var error = ValidateTemplate(req);
        if (error is not null) return BadRequest(new { message = error });
        var order = req.Order ?? (await db.SmsTemplates.MaxAsync(t => (int?)t.Order, ct) ?? 0) + 1;
        var t = new SmsTemplate { Name = req.Name!.Trim(), Text = req.Text!.Trim(), Order = order };
        db.SmsTemplates.Add(t);
        audit.Record("MessageTemplate", t.Id, "create", $"Xabar shabloni qo'shildi: {t.Name}");
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    [HttpPut("templates/{id}")]
    public async Task<ActionResult<MessageTemplateDto>> UpdateTemplate(string id, SaveMessageTemplateRequest req, CancellationToken ct)
    {
        var t = await db.SmsTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { message = "Shablon topilmadi" });
        var error = ValidateTemplate(req);
        if (error is not null) return BadRequest(new { message = error });
        t.Name = req.Name!.Trim();
        t.Text = req.Text!.Trim();
        if (req.Order is int o) t.Order = o;
        audit.Record("MessageTemplate", t.Id, "update", $"Xabar shabloni tahrirlandi: {t.Name}");
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id, CancellationToken ct)
    {
        var t = await db.SmsTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound(new { message = "Shablon topilmadi" });
        db.SmsTemplates.Remove(t);
        audit.Record("MessageTemplate", t.Id, "delete", $"Xabar shabloni o'chirildi: {t.Name}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? ValidateTemplate(SaveMessageTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Text)) return "Nom va matn kerak";
        var errors = CampaignService.TemplateErrors(req.Text);
        return errors.Count > 0 ? errors[0] : null;
    }

    private static MessageTemplateDto ToDto(SmsTemplate t) => new(t.Id, t.Name, t.Text, t.Order);
}
