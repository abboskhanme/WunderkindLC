using System.ComponentModel.DataAnnotations.Schema;

namespace WunderkindLC.Domain;

// CRM upgrade — Phase 2 (Telegram messaging core). docs/SPEC-crm-upgrade.md §3 (P2.2).
// Partial extensions of existing entities + the new outbox / attachment / achievement tables.
// Model configuration: WunderkindLC.Infrastructure/Data/AppDbContext.CrmMessaging.cs.

public partial class TelegramRegistration
{
    /// <summary>Who owns the chat: <c>father</c> · <c>mother</c> · <c>parent</c> · <c>student</c> ·
    /// <c>other</c> (rule: <c>TelegramRelations.Detect</c>, admin-editable). <c>""</c> on teacher/staff
    /// rows and on student rows not yet backfilled (treated as a parent).</summary>
    public string Relation { get; set; } = string.Empty;

    /// <summary>Set when Telegram says the user blocked the bot / the chat is gone. <c>null</c> = deliverable.
    /// Cleared when the user writes to the bot again (<c>TelegramBlocks</c>).</summary>
    public DateTime? BlockedAt { get; set; }
}

/// <summary>
/// TELEGRAM OUTBOX + per-recipient delivery log. Every parent/student-facing Telegram message is ONE row:
/// written by <c>TelegramOutbox.Enqueue</c> (inside the caller's SaveChanges) and sent by
/// <c>MessageDeliveryWorker</c>. <see cref="Status"/> is the concurrency token (claim = <c>pending → sending</c>).
/// <see cref="ChatId"/> is never returned by any API.
/// </summary>
public class MessageDelivery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Campaign (<see cref="Broadcast.Id"/>); null for auto messages.</summary>
    public string? BroadcastId { get; set; }
    /// <summary><c>campaign</c> · <c>auto</c>.</summary>
    public string Source { get; set; } = "auto";
    /// <summary>Campaign kind (<c>broadcast</c>, <c>archive</c>, <c>debt</c>) or an <c>AutoMessageTriggers</c> key.</summary>
    public string Trigger { get; set; } = string.Empty;
    /// <summary>Originating record (attendance notice item, test, achievement, rule …).</summary>
    public string? SourceId { get; set; }
    /// <summary>Subject student (the message is ABOUT this student).</summary>
    public string? StudentId { get; set; }
    /// <summary>Teacher recipient (staff messages).</summary>
    public string? TeacherId { get; set; }
    /// <summary><see cref="TelegramRegistration.Id"/> the message goes to.</summary>
    public string? RegistrationId { get; set; }
    /// <summary>Destination chat — never returned by an API, never logged with the text.</summary>
    public long ChatId { get; set; }
    /// <summary>Relation snapshot (<see cref="TelegramRegistration.Relation"/>).</summary>
    public string Relation { get; set; } = string.Empty;
    /// <summary>Snapshot: student full name (teacher name for staff rows).</summary>
    public string RecipientName { get; set; } = string.Empty;
    /// <summary>Final Telegram-HTML text (rendered at enqueue time — what was previewed is what is stored).</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary><see cref="MessageAttachment.Id"/> (one file per message).</summary>
    public string? AttachmentId { get; set; }
    /// <summary>Adds the «✅ Qabul qildim» button (callback <c>mack:{Id}</c>).</summary>
    public bool AckRequired { get; set; }
    /// <summary>pending · sending · sent · failed · blocked · unknown · expired · cancelled (concurrency token).</summary>
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    /// <summary>The worker picks <c>pending</c> rows with <c>NextAttemptAt &lt;= now</c>.</summary>
    public DateTime NextAttemptAt { get; set; } = AppClock.Now;
    /// <summary>Not sent after this moment → <c>expired</c>.</summary>
    public DateTime ExpiresAt { get; set; } = AppClock.Now.AddHours(6);
    /// <summary>Natural key of the event (unique). Defaults to the row id; keys longer than 200 chars are
    /// stored as <c>"h:" + sha256hex(key)</c>.</summary>
    public string DedupeKey { get; set; } = string.Empty;
    /// <summary>Telegram id of the message that carries the button (the LAST part of a multi-part send).</summary>
    public long? TelegramMessageId { get; set; }
    /// <summary>Sanitised Telegram description (never the bot token, never the text).</summary>
    public string Error { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public DateTime? SentAt { get; set; }
    /// <summary>First press of «✅ Qabul qildim» (bots get no read receipts — this is the only "seen" status).</summary>
    public DateTime? AckAt { get; set; }
    /// <summary>Sender of a campaign (null for auto messages).</summary>
    public string? CreatedById { get; set; }
}

/// <summary>
/// One file attached to a Telegram message (photo / video / document). Stored privately under
/// <c>uploads/messaging/</c> (never served statically, never returned as a path); after the first successful
/// upload Telegram's <see cref="TelegramFileId"/> is reused and the local copy may be cleaned up after 60 days.
/// </summary>
public class MessageAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary><c>photo</c> · <c>video</c> · <c>document</c>.</summary>
    public string Kind { get; set; } = "document";
    /// <summary>Original name (sanitised, ≤ 200) — shown in the UI and used as the Telegram document name.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Internal storage path <c>/uploads/messaging/{guid:N}{ext}</c> — never returned by an API.</summary>
    public string FileUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>Telegram <c>file_id</c> after the first successful upload (<c>""</c> until then).</summary>
    public string TelegramFileId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public string? CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    /// <summary>The local file was removed by the cleanup (the Telegram <c>file_id</c> keeps working).</summary>
    public DateTime? FileDeletedAt { get; set; }
}

/// <summary>Campaign header (Telegram broadcast) — extended in Phase 2. Counts shown in the UI come from
/// <see cref="MessageDelivery"/> rows; legacy rows (no deliveries) show <c>RecipientCount/SentCount</c>.</summary>
public partial class Broadcast
{
    /// <summary><c>broadcast</c> · <c>archive</c> · <c>debt</c>.</summary>
    public string Kind { get; set; } = "broadcast";
    /// <summary>The <c>AudienceSpec</c> snapshot (JSON).</summary>
    public string AudienceJson { get; set; } = string.Empty;
    public string? AttachmentId { get; set; }
    public bool AckRequired { get; set; }
    /// <summary><c>queued</c> · <c>sending</c> · <c>done</c> · <c>cancelled</c> (legacy rows = <c>done</c>).</summary>
    public string Status { get; set; } = "done";
    public DateTime? FinishedAt { get; set; }
}

public partial class AutoMessageRule
{
    /// <summary>Adds the «✅ Qabul qildim» button to every Telegram message of the rule.</summary>
    public bool AckButton { get; set; }
    /// <summary>Optional media sent with every message of the rule (e.g. a birthday card).</summary>
    public string? AttachmentId { get; set; }
}

/// <summary>
/// Student achievement (IELTS / CEFR / certificate / olympiad …) — shown in the admin profile and the cabinet,
/// optionally congratulated through the <c>achievement</c> auto-message trigger.
/// </summary>
public class StudentAchievement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary><c>ielts</c> · <c>cefr</c> · <c>certificate</c> · <c>olympiad</c> · <c>other</c>.</summary>
    public string Kind { get; set; } = "other";
    public string Title { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    /// <summary><c>yyyy-MM-dd</c>, not in the future.</summary>
    public string Date { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    /// <summary>Photo (<see cref="MessageAttachment"/> of kind <c>photo</c>) — never cleaned up.</summary>
    public string? AttachmentId { get; set; }
    /// <summary>Last time the congratulation was queued.</summary>
    public DateTime? CongratulatedAt { get; set; }
    /// <summary>Explicit resends so far (part of the outbox dedupe key: <c>{id}:{n}</c>).</summary>
    public int CongratulationCount { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
    public string? CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
}

public partial class Student
{
    /// <summary>U6b — usable Telegram chats (<c>BlockedAt == null</c>). Filled by <c>StudentListView.EnrichExtrasAsync</c>.</summary>
    [NotMapped]
    public int TelegramLinked { get; set; }

    /// <summary>U6b — distinct relations of the usable chats (<c>father</c>, <c>mother</c>, …).</summary>
    [NotMapped]
    public List<string> TelegramRelations { get; set; } = new();

    /// <summary>U6b — chats that blocked the bot.</summary>
    [NotMapped]
    public int TelegramBlocked { get; set; }
}
