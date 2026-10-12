using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «DAVOMAT XABARLARI» nazorati (CRM Phase 3, U3b — SPEC P3.3): Nazorat → «Davomat xabarlari». Bugungi (yoki
/// tanlangan kun) «kelmadi»/«keldi» navbati darslar kesimida; menejer xabarni ota-onaga ketishidan OLDIN
/// to'xtatadi, tasdiqlaydi yoki bekor qiladi (yakka va ommaviy). Qaror — <see cref="AbsentNoticeRules"/>,
/// yuborish — <see cref="AttendanceNotifyWorker"/>.
/// <para>Ruxsat — <c>students.notices</c>, O'QISH ham (<c>ReadRequiresPerm</c>): javobda o'quvchi ismlari, sabablar
/// va ota-onaga yetkazish holati bor. Har amal tarixga yoziladi (<c>AttendanceNotice</c> → «Guruhlar»).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.notices", ReadRequiresPerm = true)]
[Route("api/admin/attendance-notices")]
public class AttendanceNoticesController(AppDbContext db, AuditService audit, AttendanceNotifyWorker worker) : ControllerBase
{
    private static bool IsDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    /// <summary>Kun navbati: sanoqlar (holat chiplari) + darslar kesimida xabarlar. Standart — bugun.</summary>
    [HttpGet]
    public async Task<ActionResult<AbsentNoticeListDto>> List(
        [FromQuery] string? date, [FromQuery] string? status, [FromQuery] string? groupId, [FromQuery] string? teacherId)
    {
        var day = string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date.Trim();
        if (!IsDate(day)) return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        if (!string.IsNullOrWhiteSpace(status) && !AbsentNoticeRules.CountKeys.Contains(status))
            return BadRequest(new { message = "Holat noto'g'ri" });
        return await AbsentNoticeQueries.ListAsync(db, day, status, groupId, teacherId);
    }

    /// <summary>Faqat sanoqlar (bosh sahifa vidjeti). Standart — bugun.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AbsentNoticeSummaryDto>> Summary([FromQuery] string? date)
    {
        var day = string.IsNullOrWhiteSpace(date) ? AppClock.Today.ToString("yyyy-MM-dd") : date.Trim();
        if (!IsDate(day)) return BadRequest(new { message = "Sana noto'g'ri (yyyy-MM-dd)" });
        return await AbsentNoticeQueries.SummaryAsync(db, day);
    }

    /// <summary>To'xtatish — ota-onaga ketmaydi (tasdiqlansa yana ketadi; hech kim tasdiqlamasa muddati o'tadi).</summary>
    [HttpPut("{id}/hold")]
    public Task<ActionResult<AbsentNoticeItemDto>> Hold(string id, AbsentNoticeActionRequest? req) =>
        ApplyOneAsync(id, AbsentNoticeRules.ActionHold, req?.Note);

    /// <summary>Tasdiqlash — dars tugashi + kechikishda (vaqti o'tgan bo'lsa darhol) yuboriladi.</summary>
    [HttpPut("{id}/approve")]
    public Task<ActionResult<AbsentNoticeItemDto>> Approve(string id, AbsentNoticeActionRequest? req) =>
        ApplyOneAsync(id, AbsentNoticeRules.ActionApprove, req?.Note);

    /// <summary>Bekor qilish — hech qachon yuborilmaydi.</summary>
    [HttpPut("{id}/cancel")]
    public Task<ActionResult<AbsentNoticeItemDto>> Cancel(string id, AbsentNoticeActionRequest? req) =>
        ApplyOneAsync(id, AbsentNoticeRules.ActionCancel, req?.Note);

    /// <summary>
    /// Ommaviy amal (ko'pi bilan <see cref="AbsentNoticeRules.MaxBulk"/>). Ruxsat etilmagan holatdagilar (allaqachon
    /// yuborilgan/yopilgan) CHETLAB o'tiladi — <c>skipped</c>. Bitta tranzaksiya: parallel o'zgarish bo'lsa hech narsa
    /// saqlanmaydi va 409 (sahifani yangilash kerak).
    /// </summary>
    [HttpPut("bulk")]
    public async Task<ActionResult<AbsentNoticeBulkResult>> Bulk(AbsentNoticeBulkRequest req)
    {
        var action = (req.Action ?? "").Trim();
        if (!AbsentNoticeRules.Actions.Contains(action)) return BadRequest(new { message = "Amal noto'g'ri" });
        var ids = (req.Ids ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "Xabar tanlanmagan" });
        if (ids.Count > AbsentNoticeRules.MaxBulk)
            return BadRequest(new { message = $"Bir martada ko'pi bilan {AbsentNoticeRules.MaxBulk} ta xabar" });
        var (note, noteError) = AbsentNoticeRules.CleanNote(req.Note);
        if (noteError is not null) return BadRequest(new { message = noteError });

        var items = await db.AttendanceNotifyItems.Where(i => ids.Contains(i.Id)).ToListAsync();
        var names = await NamesAsync(items);
        var changed = 0;
        foreach (var item in items.Where(i => AbsentNoticeRules.CanApply(i.Status, action)))
        {
            Apply(item, action, note, names);
            changed++;
        }
        if (changed > 0)
        {
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = AbsentNoticeRules.ConcurrencyMessage });
            }
            if (action == AbsentNoticeRules.ActionApprove) worker.Wake();
        }
        return new AbsentNoticeBulkResult(changed, ids.Count - changed);
    }

    private async Task<ActionResult<AbsentNoticeItemDto>> ApplyOneAsync(string id, string action, string? rawNote)
    {
        var (note, noteError) = AbsentNoticeRules.CleanNote(rawNote);
        if (noteError is not null) return BadRequest(new { message = noteError });
        var item = await db.AttendanceNotifyItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound(new { message = "Xabar topilmadi" });
        if (!AbsentNoticeRules.CanApply(item.Status, action))
            return Conflict(new { message = AbsentNoticeRules.ClosedMessage });

        Apply(item, action, note, await NamesAsync([item]));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            // Ishchi aynan shu payt band qildi (yuborilmoqda) yoki boshqa menejer ulgurdi.
            return Conflict(new { message = AbsentNoticeRules.ConcurrencyMessage });
        }
        if (action == AbsentNoticeRules.ActionApprove) worker.Wake();
        return await AbsentNoticeQueries.ItemAsync(db, item);
    }

    /// <summary>Holat + qaror maydonlari + tarix qatori (saqlash chaqiruvchida).</summary>
    private void Apply(AttendanceNotifyItem item, string action, string? note,
        (Dictionary<string, string> Students, Dictionary<string, string> Groups) names)
    {
        var now = AppClock.Now;
        item.Status = AbsentNoticeRules.TargetStatus(action)!;
        item.DecidedById = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        item.DecidedByName = User.FindFirst(ClaimTypes.Name)?.Value;
        item.DecidedAt = now;
        item.DecisionNote = note;
        if (action == AbsentNoticeRules.ActionCancel) item.CancelReason = AbsentNoticeRules.CancelManager;
        item.UpdatedAt = now;
        audit.Record(AbsentNoticeAudit.EntityType, AbsentNoticeAudit.EntityId(item.ClassId, item.Id), "update",
            AbsentNoticeRules.AuditSummary(action, item.Kind, names.Students.GetValueOrDefault(item.StudentId, ""),
                names.Groups.GetValueOrDefault(item.ClassId, ""), item.Date, note),
            studentId: item.StudentId);
    }

    private async Task<(Dictionary<string, string> Students, Dictionary<string, string> Groups)> NamesAsync(
        IReadOnlyCollection<AttendanceNotifyItem> items)
    {
        var sids = items.Select(i => i.StudentId).Distinct().ToList();
        var gids = items.Select(i => i.ClassId).Distinct().ToList();
        return (
            await db.Students.AsNoTracking().Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName),
            await db.Classes.AsNoTracking().Where(g => gids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name));
    }
}
