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
/// STUDENT ACHIEVEMENTS (CRM upgrade Phase 2, P2.6/P2.7): IELTS / CEFR / certificates / olympiads in the student
/// profile («Yutuqlar» tab) with an optional congratulation to the parents (outbox, <c>achievement</c> trigger).
/// RBAC: <c>students.list</c> (reads too — names and photos); POST → create, PUT → edit, DELETE → delete.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.list", ReadRequiresPerm = true)]
[Route("api/admin/achievements")]
public class StudentAchievementsController(
    AppDbContext db, AuditService audit, AutoMessageService autoMsg, MessageAttachmentStore store) : ControllerBase
{
    private string? ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string ActorName => User.FindFirst(ClaimTypes.Name)?.Value ?? "Administrator";

    [HttpGet]
    public async Task<ActionResult<List<AchievementDto>>> List(
        [FromQuery] string? studentId, [FromQuery] string? kind, [FromQuery] string? from, [FromQuery] string? to)
    {
        var q = db.StudentAchievements.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(studentId)) q = q.Where(a => a.StudentId == studentId);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(a => a.Kind == kind);
        var list = await q.ToListAsync();
        if (!string.IsNullOrWhiteSpace(from)) list = list.Where(a => string.CompareOrdinal(a.Date, from) >= 0).ToList();
        if (!string.IsNullOrWhiteSpace(to)) list = list.Where(a => string.CompareOrdinal(a.Date, to) <= 0).ToList();
        var ids = list.Select(a => a.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName);
        return list.OrderByDescending(a => a.Date, StringComparer.Ordinal).ThenByDescending(a => a.CreatedAt)
            .Select(a => AchievementService.ToDto(a, names.GetValueOrDefault(a.StudentId, ""))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<AchievementDto>> Create(SaveAchievementRequest req)
    {
        var s = string.IsNullOrWhiteSpace(req.StudentId) ? null : await db.Students.FindAsync(req.StudentId);
        if (s is null) return BadRequest(new { message = "O'quvchi topilmadi" });
        var error = await AchievementService.ValidateAsync(db, req);
        if (error is not null) return BadRequest(new { message = error });
        if (req.Congratulate && s.IsArchived) return BadRequest(new { message = AchievementService.ErrArchived });

        var a = new StudentAchievement { StudentId = s.Id, CreatedById = ActorId, CreatedByName = ActorName, CreatedAt = AppClock.Now };
        AchievementService.Apply(a, req);
        db.StudentAchievements.Add(a);
        audit.Record(AchievementService.EntityType, a.Id, "create",
            "Yutuq qo'shildi: " + AchievementService.Describe(a, s.FullName), studentId: s.Id);
        await db.SaveChangesAsync();

        // The achievement is SAVED either way — a congratulation problem is a warning (a 400 here would make the user
        // resubmit and create a duplicate).
        var messages = 0;
        string? warning = null;
        if (req.Congratulate)
        {
            var (n, congratsError) = await AchievementService.QueueCongratulationAsync(db, autoMsg, a, s);
            if (congratsError is not null) warning = $"Yutuq saqlandi, lekin tabrik yuborilmadi: {congratsError}";
            else if (n == 0) warning = "Yutuq saqlandi. Tabrik navbatga qo'yilmadi: ota-ona Telegram botga ulanmagan";
            messages = n;
        }
        return AchievementService.ToDto(a, s.FullName, messages) with { Warning = warning };
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AchievementDto>> Update(string id, SaveAchievementRequest req)
    {
        var a = await db.StudentAchievements.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Yutuq topilmadi" });
        var error = await AchievementService.ValidateAsync(db, req);
        if (error is not null) return BadRequest(new { message = error });
        var name = await db.Students.AsNoTracking().Where(s => s.Id == a.StudentId).Select(s => s.FullName).FirstOrDefaultAsync() ?? "";
        var before = AchievementService.Describe(a, name);
        AchievementService.Apply(a, req);
        audit.Record(AchievementService.EntityType, a.Id, "update",
            $"Yutuq tahrirlandi: {before} → {AchievementService.Describe(a, name)}", studentId: a.StudentId);
        await db.SaveChangesAsync();
        return AchievementService.ToDto(a, name);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var a = await db.StudentAchievements.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Yutuq topilmadi" });
        var name = await db.Students.AsNoTracking().Where(s => s.Id == a.StudentId).Select(s => s.FullName).FirstOrDefaultAsync() ?? "";
        db.StudentAchievements.Remove(a);
        audit.Record(AchievementService.EntityType, a.Id, "delete",
            "Yutuq o'chirildi: " + AchievementService.Describe(a, name), studentId: a.StudentId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Explicit (re)send of the congratulation — a new dedupe key every time (<c>{id}:{n}</c>).</summary>
    [HttpPost("{id}/congratulate")]
    public async Task<ActionResult<CongratulatedDto>> Congratulate(string id)
    {
        var a = await db.StudentAchievements.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Yutuq topilmadi" });
        var s = await db.Students.FindAsync(a.StudentId);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var (n, error) = await AchievementService.QueueCongratulationAsync(db, autoMsg, a, s);
        if (error is not null) return BadRequest(new { message = error });
        audit.Record(AchievementService.EntityType, a.Id, "update",
            $"Yutuq uchun tabrik yuborildi ({n} ta xabar): " + AchievementService.Describe(a, s.FullName), studentId: s.Id);
        await db.SaveChangesAsync();
        return new CongratulatedDto(n);
    }

    /// <summary>
    /// Photo upload for an achievement — under THIS controller's gate (<c>students.list</c>, POST → create), so a
    /// staff member who manages achievements does not need the messaging permission. Photos only (jpg/png ≤ 10 MB,
    /// extension + Content-Type + magic bytes). Same private storage as message attachments.
    /// </summary>
    [HttpPost("photo")]
    [RequestSizeLimit(10_485_760 + 65_536)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760 + 65_536)]
    public async Task<ActionResult<MessageAttachmentDto>> UploadPhoto(IFormFile? file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = MessageAttachmentRules.ErrEmpty });
        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png"))
            return BadRequest(new { message = "Yutuq uchun faqat rasm (jpg, png) biriktiriladi" });
        await using var stream = file.OpenReadStream();
        var (a, error) = await MessageAttachments.CreateAsync(db, store, stream, file.FileName, file.ContentType,
            file.Length, ActorId, ActorName);
        if (error is not null) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        return new MessageAttachmentDto(a!.Id, a.Kind, a.FileName, a.SizeBytes, a.ContentType);
    }

    /// <summary>The achievement photo (private storage — never a static <c>/uploads</c> path).</summary>
    [HttpGet("{id}/photo")]
    public async Task<IActionResult> Photo(string id)
    {
        var a = await db.StudentAchievements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a?.AttachmentId is null) return NotFound(new { message = "Rasm yo'q" });
        return await AchievementPhoto.StreamAsync(this, db, store, a.AttachmentId);
    }
}

/// <summary>Streams an achievement photo from the private attachment store (admin and cabinet endpoints).</summary>
internal static class AchievementPhoto
{
    public static async Task<IActionResult> StreamAsync(ControllerBase c, AppDbContext db, MessageAttachmentStore store, string attachmentId)
    {
        var att = await db.MessageAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId);
        if (att is null || att.Kind != "photo" || !store.Exists(att.FileUrl)) return c.NotFound(new { message = "Rasm topilmadi" });
        c.Response.Headers.CacheControl = "private, no-store";
        return c.File(store.OpenRead(att.FileUrl), att.ContentType);
    }
}
