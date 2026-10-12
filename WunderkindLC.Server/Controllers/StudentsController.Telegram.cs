using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Student profile — Telegram (CRM upgrade Phase 2, P2.7): the relation of a linked chat (father / mother / …,
/// admin-editable) and the student's «Telegram xabarlari» (delivery log with «Qabul qildi» time).
/// </summary>
public partial class StudentsController
{
    /// <summary>Changes the relation of one linked chat. Class gate <c>students.list</c> (PUT → <c>edit</c>).</summary>
    [HttpPut("{id}/telegram-links/{regId}")]
    public async Task<ActionResult<TelegramLinks.LinkDto>> SetTelegramRelation(string id, string regId, SetTelegramRelationRequest req)
    {
        var relation = (req.Relation ?? "").Trim();
        if (!TelegramRelations.IsKnown(relation)) return BadRequest(new { message = "Noma'lum qarindoshlik turi" });
        var s = await db.Students.FindAsync(id);
        if (s is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var reg = await db.TelegramRegistrations.FirstOrDefaultAsync(r => r.Id == regId && r.StudentId == id);
        if (reg is null) return NotFound(new { message = "Ulanish topilmadi — uzilgan bo'lishi mumkin" });

        var before = TelegramRelations.Label(reg.Relation);
        if (reg.Relation != relation)
        {
            reg.Relation = relation;
            audit.Record(AuditService.EntityStudent, id, "update",
                $"Telegram ulanishi turi o'zgardi: {s.FullName} — {before} → {TelegramRelations.Label(relation)}",
                studentId: id);
            await db.SaveChangesAsync();
        }
        return TelegramLinks.ToDto(TelegramLinks.PhoneKeys(s))(reg);
    }

    /// <summary>
    /// The student's Telegram messages (latest first): type, relation, status, «Qabul qildi» time, plain text.
    /// Readable with the students OR the messages permission (<c>ReadRequiresPerm</c> — message texts).
    /// </summary>
    [HttpGet("{id}/telegram-deliveries")]
    [AdminPerm("students.list", "messages.broadcast", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<StudentDeliveryDto>>> TelegramDeliveries(string id, [FromQuery] int limit = 50)
    {
        if (!await db.Students.AnyAsync(s => s.Id == id)) return NotFound(new { message = "O'quvchi topilmadi" });
        return await MessagingReports.StudentDeliveriesAsync(db, id, limit);
    }
}
