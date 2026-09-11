using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Support o'qituvchi slotlari uchun umumiy yordamchi. Slot CRUD'i `TeacherPortalController`
/// (/api/teacher/support) ichida; bu yerda faqat admin va o'qituvchi portali bo'lishadigan mantiq.
/// </summary>
public static class SupportService
{
    /// <summary>Berilgan o'quvchi id'lari (null'lar tashlanadi) → FISH xaritasi.</summary>
    public static async Task<Dictionary<string, string>> StudentNamesAsync(
        IAppDbContext db, IEnumerable<string?> ids)
    {
        var list = ids.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Students.Where(s => list.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName);
    }
}
