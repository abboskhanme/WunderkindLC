using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>CRM upgrade — Phase 2 (Telegram messaging core): DbSets and model configuration
/// (docs/SPEC-crm-upgrade.md §3, P2.2). Indexed string columns get <c>HasMaxLength(200)</c>.</summary>
public partial class AppDbContext
{
    public DbSet<MessageDelivery> MessageDeliveries => Set<MessageDelivery>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<StudentAchievement> StudentAchievements => Set<StudentAchievement>();

    partial void ConfigureCrmMessaging(ModelBuilder b)
    {
        // --- TelegramRegistration (relation + blocked flag) ---
        b.Entity<TelegramRegistration>().Property(r => r.Relation).HasMaxLength(16).HasDefaultValue("");
        b.Entity<TelegramRegistration>().HasIndex(r => new { r.StudentId, r.BlockedAt });

        // --- Outbox / delivery log ---
        var d = b.Entity<MessageDelivery>();
        d.HasKey(x => x.Id);
        d.Property(x => x.Id).HasMaxLength(36);
        d.Property(x => x.BroadcastId).HasMaxLength(36);
        d.Property(x => x.Source).HasMaxLength(16);
        d.Property(x => x.Trigger).HasMaxLength(40);
        d.Property(x => x.SourceId).HasMaxLength(200);
        d.Property(x => x.StudentId).HasMaxLength(200);
        d.Property(x => x.TeacherId).HasMaxLength(200);
        d.Property(x => x.RegistrationId).HasMaxLength(36);
        d.Property(x => x.Relation).HasMaxLength(16);
        d.Property(x => x.RecipientName).HasMaxLength(200);
        d.Property(x => x.AttachmentId).HasMaxLength(36);
        d.Property(x => x.Status).HasMaxLength(16).IsConcurrencyToken();
        d.Property(x => x.DedupeKey).HasMaxLength(200);
        d.Property(x => x.Error).HasMaxLength(300);
        d.Property(x => x.CreatedById).HasMaxLength(36);
        d.HasIndex(x => new { x.Status, x.NextAttemptAt });
        d.HasIndex(x => x.BroadcastId);
        d.HasIndex(x => new { x.StudentId, x.CreatedAt });
        d.HasIndex(x => x.SourceId);
        d.HasIndex(x => x.DedupeKey).IsUnique();

        // --- Attachments ---
        var a = b.Entity<MessageAttachment>();
        a.HasKey(x => x.Id);
        a.Property(x => x.Id).HasMaxLength(36);
        a.Property(x => x.Kind).HasMaxLength(16);
        a.Property(x => x.FileName).HasMaxLength(200);
        a.Property(x => x.FileUrl).HasMaxLength(300);
        a.Property(x => x.ContentType).HasMaxLength(100);
        a.Property(x => x.TelegramFileId).HasMaxLength(300);
        a.Property(x => x.CreatedById).HasMaxLength(36);
        a.Property(x => x.CreatedByName).HasMaxLength(200);

        // --- Broadcast (campaign header) ---
        b.Entity<Broadcast>().Property(x => x.Kind).HasMaxLength(20).HasDefaultValue("broadcast");
        b.Entity<Broadcast>().Property(x => x.AttachmentId).HasMaxLength(36);
        b.Entity<Broadcast>().Property(x => x.Status).HasMaxLength(16).HasDefaultValue("done");

        // --- AutoMessageRule ---
        b.Entity<AutoMessageRule>().Property(x => x.AttachmentId).HasMaxLength(36);

        // --- Achievements ---
        var s = b.Entity<StudentAchievement>();
        s.HasKey(x => x.Id);
        s.Property(x => x.Id).HasMaxLength(36);
        s.Property(x => x.StudentId).HasMaxLength(200);
        s.Property(x => x.Kind).HasMaxLength(16);
        s.Property(x => x.Title).HasMaxLength(200);
        s.Property(x => x.Result).HasMaxLength(50);
        s.Property(x => x.Date).HasMaxLength(10);
        s.Property(x => x.Note).HasMaxLength(1000);
        s.Property(x => x.AttachmentId).HasMaxLength(36);
        s.Property(x => x.CreatedById).HasMaxLength(36);
        s.Property(x => x.CreatedByName).HasMaxLength(200);
        s.HasIndex(x => x.StudentId);
    }
}
