using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// SaveChanges interceptor'i — har muvaffaqiyatli saqlashdan keyin <see cref="DataCache"/> dagi
/// mos entity turlari versiyasini oshiradi, shu bilan ularga bog'liq keshni avtomatik eskirtiradi.
///
/// <para><b>Nima uchun OLDIN yig'amiz:</b> <c>SavingChanges</c>da ChangeTracker'dagi Added/Modified/
/// Deleted holatidagi yozuvlar hali o'z holatini bildiradi. SaveChanges TUGAGACH esa ular Unchanged
/// bo'lib qoladi (o'zgargan turlarni bilib bo'lmaydi). Shuning uchun turlar to'plamini saqlashdan OLDIN
/// yig'ib, DbContext instansiyasiga bog'lab qo'yamiz; muvaffaqiyatdan keyin (SavedChanges) o'sha to'plam
/// bo'yicha Bump qilamiz. Bir vaqtda bir necha DbContext (har scope alohida) saqlashi mumkin bo'lgani
/// uchun to'plam per-context saqlanadi (<see cref="ConditionalWeakTable{TKey,TValue}"/> — thread-safe,
/// context yig'ilib ketsa yozuv o'zi tozalanadi).</para>
///
/// Interceptor Singleton — ichida holat DbContext'ga bog'langan (umumiy o'zgaruvchan holat yo'q).
/// </summary>
public sealed class CacheInvalidationInterceptor(DataCache dataCache) : SaveChangesInterceptor
{
    // Har bir DbContext saqlash amali uchun o'zgargan entity turi nomlari to'plami (SavingChanges → SavedChanges).
    private readonly ConditionalWeakTable<DbContext, HashSet<string>> _pending = new();

    /// <summary>Saqlashdan OLDIN o'zgargan (Added/Modified/Deleted) entity turlarini yig'ib, context'ga bog'laymiz.</summary>
    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                types.Add(entry.Metadata.ClrType.Name);
        }
        // Bir DbContext bir vaqtda faqat bitta SaveChanges qiladi — mavjud yozuvni almashtiramiz.
        _pending.Remove(context);
        if (types.Count > 0) _pending.Add(context, types);
    }

    /// <summary>Saqlash muvaffaqiyatli tugadi — yig'ilgan turlar versiyasini oshiramiz va to'plamni tozalaymiz.</summary>
    private void Flush(DbContext? context)
    {
        if (context is null) return;
        if (_pending.TryGetValue(context, out var types) && types.Count > 0)
            dataCache.Bump(types);
        _pending.Remove(context);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    // Saqlash muvaffaqiyatsiz bo'lsa — versiyani OSHIRMASDAN (ma'lumot o'zgarmadi) to'plamni tozalaymiz.
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) _pending.Remove(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
