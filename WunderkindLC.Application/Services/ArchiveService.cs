using System.Text.Json;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Arxiv (soft-delete surat) xizmati. O'chirish endpointlari entity'ni hard-delete
/// qilishdan OLDIN bu yerda <see cref="ArchivedRecord"/> yaratadi — entity'ning to'liq
/// JSON surati saqlanadi, shuning uchun keyinchalik ko'rish va TIKLASH mumkin.
/// </summary>
public static class ArchiveService
{
    /// <summary>
    /// O'chirilayotgan <paramref name="entity"/> uchun arxiv surati qo'shadi (SaveChanges'ni
    /// chaqiruvchi bajaradi — odatda mavjud delete SaveChanges'iga qo'shilib ketadi).
    /// </summary>
    public static void Snapshot(
        IAppDbContext db, string type, string entityId, string title, string subtitle,
        object entity, string? reason, string actorName)
    {
        db.ArchivedRecords.Add(new ArchivedRecord
        {
            Type = type,
            EntityId = entityId,
            Title = title ?? string.Empty,
            Subtitle = subtitle ?? string.Empty,
            Json = JsonSerializer.Serialize(entity),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            DeletedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName,
        });
    }
}
