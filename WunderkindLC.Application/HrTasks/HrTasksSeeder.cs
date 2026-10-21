using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>Start-up: the single <c>hr_settings</c> row with the old defaults, only when missing.</summary>
public static class HrTasksSeeder
{
    public static async Task<bool> SeedAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.HrSettings.AnyAsync(s => s.Id == HrSettings.SingletonId, ct)) return false;
        db.HrSettings.Add(new HrSettings { UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
