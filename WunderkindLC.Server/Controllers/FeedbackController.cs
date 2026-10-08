using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Taklif va shikoyatlar — ota-onalar ilova orqali yuboradi (StudentPortalController), admin
/// shu yerda ko'radi va "hal qilindi" deb belgilaydi (xato bosilgan bo'lsa — qayta ochadi).
/// Har ikki amal "O'zgarishlar tarixi"ga yoziladi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("feedback")]
[Route("api/admin/feedback")]
public class FeedbackController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<FeedbackDto>>> GetAll(
        [FromQuery] string? type, [FromQuery] string? status)
    {
        var q = db.Feedbacks.AsQueryable();
        if (!string.IsNullOrEmpty(type)) q = q.Where(f => f.Type == type);
        if (!string.IsNullOrEmpty(status)) q = q.Where(f => f.Status == status);
        var list = await q.OrderByDescending(f => f.CreatedAt).Take(300).ToListAsync();

        var ids = list.Select(f => f.StudentId).Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var byId = students.ToDictionary(s => s.Id);

        return list.Select(f => new FeedbackDto(
            f.Id,
            byId.TryGetValue(f.StudentId, out var s) ? s.FullName : "",
            f.ParentName,
            byId.TryGetValue(f.StudentId, out var s2) ? s2.ClassName : "",
            f.Type, f.Text, f.CreatedAt.ToString("o"), f.Status,
            string.IsNullOrEmpty(f.SenderRole) ? "parent" : f.SenderRole,
            f.SenderName, f.ImageUrl)).ToList();
    }

    [HttpPost("{id}/resolve")]
    public Task<ActionResult<FeedbackStatusDto>> Resolve(string id) => SetStatusAsync(id, "resolved");

    /// <summary>Hal qilingan murojaatni QAYTA OCHISH ("Yangi" ga qaytarish) — ilgari xato bosilgan
    /// "Hal qilindi" ni qaytarib bo'lmasdi va murojaat ro'yxatdan "yo'qolib" qolardi.</summary>
    [HttpPost("{id}/reopen")]
    public Task<ActionResult<FeedbackStatusDto>> Reopen(string id) => SetStatusAsync(id, "new");

    private async Task<ActionResult<FeedbackStatusDto>> SetStatusAsync(string id, string status)
    {
        var f = await db.Feedbacks.FindAsync(id);
        if (f is null) return NotFound(new { message = "Murojaat topilmadi" });
        if (f.Status != status)
        {
            f.Status = status;
            var kind = f.Type == "complaint" ? "Shikoyat" : "Taklif";
            var who = !string.IsNullOrWhiteSpace(f.SenderName) ? f.SenderName : f.ParentName;
            audit.Record("Feedback", f.Id, "update",
                status == "resolved"
                    ? $"{kind} hal qilindi deb belgilandi ({who})"
                    : $"{kind} qayta ochildi ({who})",
                studentId: string.IsNullOrEmpty(f.StudentId) ? null : f.StudentId,
                teacherId: string.IsNullOrEmpty(f.TeacherId) ? null : f.TeacherId);
            await db.SaveChangesAsync();
        }
        return new FeedbackStatusDto(f.Id, f.Status);
    }
}
