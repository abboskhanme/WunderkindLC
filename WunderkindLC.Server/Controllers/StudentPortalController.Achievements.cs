using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Cabinet «Yutuqlarim» (CRM upgrade Phase 2, P2.7): the student's / parent's own achievements and their photos.
/// Ownership through <c>TargetAsync</c> (student — own; parent — own child; admin — <c>?studentId</c>).
/// </summary>
public partial class StudentPortalController
{
    [HttpGet("achievements")]
    public async Task<ActionResult<List<AchievementDto>>> MyAchievements([FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var list = await db.StudentAchievements.AsNoTracking().Where(a => a.StudentId == s.Id).ToListAsync();
        return list.OrderByDescending(a => a.Date, StringComparer.Ordinal).ThenByDescending(a => a.CreatedAt)
            .Select(a => AchievementService.ToDto(a, s.FullName) with { CreatedByName = "" })
            .ToList();
    }

    [HttpGet("achievements/{id}/photo")]
    public async Task<IActionResult> MyAchievementPhoto(string id, [FromQuery] string? studentId)
    {
        var s = await TargetAsync(studentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var a = await db.StudentAchievements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.StudentId == s.Id);
        if (a?.AttachmentId is null) return NotFound(new { message = "Rasm yo'q" });
        var store = HttpContext.RequestServices.GetRequiredService<MessageAttachmentStore>();
        return await AchievementPhoto.StreamAsync(this, db, store, a.AttachmentId);
    }
}
