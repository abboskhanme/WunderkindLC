using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>CRM upgrade — Phase 2 (Telegram messaging core) tables.</summary>
public partial interface IAppDbContext
{
    DbSet<MessageDelivery> MessageDeliveries { get; }
    DbSet<MessageAttachment> MessageAttachments { get; }
    DbSet<StudentAchievement> StudentAchievements { get; }
}
