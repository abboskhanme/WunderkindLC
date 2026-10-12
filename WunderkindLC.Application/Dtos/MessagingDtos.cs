namespace WunderkindLC.Application.Dtos;

// CRM upgrade — Phase 2 (Telegram messaging core) API shapes (docs/SPEC-crm-upgrade.md P2.7).
// Property names serialise camelCase (ASP.NET default) — the client types in api/services/messaging.ts mirror them.

/// <summary>
/// WHO receives a campaign. <c>kind</c>: <c>active</c> (non-archived, live membership) · <c>archive</c> (archived only)
/// · <c>selected</c> (exact student ids) · <c>teachers</c>. Relations default to every parent relation
/// (<c>father, mother, parent, other</c>) — «Barcha ota-onalar».
/// </summary>
public sealed record AudienceSpec
{
    public string Kind { get; init; } = "active";
    public List<string>? GroupIds { get; init; }
    public List<string>? CourseIds { get; init; }
    public string? TeacherId { get; init; }
    /// <summary>active: membership statuses (<c>active</c>, <c>trial</c>, <c>frozen</c>); default active + trial.</summary>
    public List<string>? MemberStatuses { get; init; }
    public bool OnlyDebtors { get; init; }
    public List<string>? StudentIds { get; init; }
    public List<string>? Relations { get; init; }
    public string? ArchivedFrom { get; init; }
    public string? ArchivedTo { get; init; }
    public string? ArchiveReason { get; init; }
    public List<string>? TeacherIds { get; init; }
}

public sealed record Paged<T>(List<T> Items, int Total, int Page, int PageSize);

public sealed record MessagePreviewRequest(
    string? Template, AudienceSpec? Audience = null, string? StudentId = null, string? AttachmentId = null,
    string? Trigger = null, string? Kind = null);

public sealed record MessagePreviewSample(string StudentId, string FullName, string Relation);

public sealed record MessagePreviewDto(
    string Html, string Plain, int VisibleLength, string Mode, List<string> Errors, List<string> Warnings,
    MessagePreviewSample? Sample);

public sealed record AudienceCountRequest(AudienceSpec Audience, string? Template = null, string? Kind = null);

public sealed record RelationCountDto(string Relation, int Chats);

public sealed record AudienceCountDto(
    int Students, int Chats, int Messages, List<RelationCountDto> ByRelation, int NotConnected, int Blocked,
    bool PerChat, List<string> SampleNames);

public sealed record MessageAttachmentDto(string Id, string Kind, string FileName, long SizeBytes, string ContentType);

public sealed record CreateCampaignRequest(
    string Kind, AudienceSpec Audience, string? Template, string? AttachmentId, bool AckRequired);

public sealed record CampaignCreatedDto(string Id, int Messages, int Chats, int Students);

public sealed record CampaignRowDto(
    string Id, string Kind, string Title, string TextPlain, string SenderName, string CreatedAt, string Status,
    int Total, int Sent, int Failed, int Blocked, int Pending, int Acked, bool AckRequired, bool HasAttachment);

public sealed record CampaignAttachmentRef(string Id, string Kind, string FileName);

/// <summary><c>CampaignRow &amp; { html, attachment, audience }</c> (flat).</summary>
public sealed record CampaignDetailDto(
    string Id, string Kind, string Title, string TextPlain, string SenderName, string CreatedAt, string Status,
    int Total, int Sent, int Failed, int Blocked, int Pending, int Acked, bool AckRequired, bool HasAttachment,
    string Html, CampaignAttachmentRef? Attachment, AudienceSpec? Audience)
{
    public static CampaignDetailDto From(CampaignRowDto r, string html, CampaignAttachmentRef? attachment, AudienceSpec? audience) =>
        new(r.Id, r.Kind, r.Title, r.TextPlain, r.SenderName, r.CreatedAt, r.Status, r.Total, r.Sent, r.Failed,
            r.Blocked, r.Pending, r.Acked, r.AckRequired, r.HasAttachment, html, attachment, audience);
}

public sealed record CampaignDeliveryRowDto(
    string DeliveryId, string Relation, string RecipientName, string Status, string? SentAt, string? AckAt, string Error);

public sealed record CampaignStudentDeliveriesDto(
    string StudentId, string StudentName, List<string> GroupNames, List<CampaignDeliveryRowDto> Rows);

public sealed record CampaignCancelledDto(int Cancelled);

public sealed record AutoStatsRowDto(
    string Date, string Trigger, string Label, int Total, int Sent, int Failed, int Blocked, int Acked);

public sealed record DebtorItemDto(string GroupId, string GroupName, string CourseName, decimal Debt);

public sealed record DebtorTelegramDto(int Chats, List<string> Relations);

public sealed record LastReminderDto(string At, string Status, string? AckAt);

public sealed record DebtorDto(
    string StudentId, string FullName, decimal TotalDebt, List<DebtorItemDto> Items, DebtorTelegramDto Telegram,
    LastReminderDto? LastReminder);

public sealed record MessageTemplateDto(string Id, string Name, string Text, int Order);

public sealed record SaveMessageTemplateRequest(string? Name, string? Text, int? Order = null);

/// <summary>Student profile «Telegram xabarlari» row.</summary>
public sealed record StudentDeliveryDto(
    string Id, string CreatedAt, string? SentAt, string Trigger, string Label, string Relation, string Status,
    string? AckAt, string TextPlain);

public sealed record SetTelegramRelationRequest(string? Relation);

// ---------- Achievements (U2c) ----------

public sealed record AchievementDto(
    string Id, string StudentId, string StudentName, string Kind, string KindLabel, string Title, string Result,
    string Date, string Note, bool HasPhoto, string? CongratulatedAt, string CreatedAt, string CreatedByName,
    int Messages = 0, string? Warning = null);

public sealed record SaveAchievementRequest(
    string? StudentId, string? Kind, string? Title, string? Result, string? Date, string? Note,
    string? AttachmentId, bool Congratulate = false);

public sealed record CongratulatedDto(int Messages);
