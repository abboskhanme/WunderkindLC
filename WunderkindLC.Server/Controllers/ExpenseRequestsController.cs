using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

public record ExpenseRequestDto(
    string Id, string Status, string Date, string Category, string? TypeName, decimal Amount,
    string? Note, string? Comment, string? TeacherName, string? StaffName, string? Month, string? Method,
    string? CashboxId, string? CashboxName, string? SalaryPayKind,
    string CreatedAt, string? CreatedBy, string? DecidedAt, string? DecidedBy, string? DecisionNote,
    string? TransactionId, bool CanDecide, bool IsMine);

public record ExpenseDecisionRequest(string? Note, string? CashboxId);

public record ExpenseApprovalSettingsDto(decimal Threshold, bool CanApprove, bool CanEditThreshold);

public record ExpenseApprovalSettingsPayload(decimal Threshold);

/// <summary>
/// «CHIQIMLAR TASDIG'I» (SPEC-backlog-2026-10-17 F3 K2; qoidalar — <see cref="ExpenseApproval"/>): chegaradan katta chiqim
/// so'rovlari ro'yxati, tasdiqlash (shu paytda tranzaksiya yoziladi va kassadan chiqadi), rad etish (sabab bilan) va
/// kiritgan odamning o'zi qaytarib olishi. Chegara — FAQAT superadmin o'zgartiradi (HARD rol).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", "expenseApproval", ReadRequiresPerm = true)]
[Route("api/admin/finance/expense-requests")]
public class ExpenseRequestsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string? Me => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? MyName => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
    private bool CanApprove => AdminPermAttribute.IsSuperAdminOrGranted(User, ExpenseApproval.PermKey);

    [HttpGet]
    [AdminPerm("finance.main", "expenseApproval", ReadRequiresPerm = true)]
    public async Task<ActionResult<List<ExpenseRequestDto>>> List([FromQuery] string? status, CancellationToken ct)
    {
        var q = db.ExpenseRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(300).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    [HttpGet("count")]
    [AdminPerm("finance.main", "expenseApproval", ReadRequiresPerm = true)]
    public async Task<ActionResult<object>> Count(CancellationToken ct) =>
        new { pending = await db.ExpenseRequests.CountAsync(r => r.Status == ExpenseApproval.Pending, ct) };

    [HttpGet("settings")]
    [AdminPerm("finance.main", "expenseApproval", ReadRequiresPerm = true)]
    public async Task<ActionResult<ExpenseApprovalSettingsDto>> GetSettings(CancellationToken ct)
    {
        var threshold = await db.CenterMeta.AsNoTracking().Select(m => m.ExpenseApprovalThreshold).FirstOrDefaultAsync(ct);
        return new ExpenseApprovalSettingsDto(threshold, CanApprove, User.IsInRole(Roles.SuperAdmin));
    }

    /// <summary>Chegara — FAQAT superadmin (HARD rol: `AdminPerm` admin'ni ham o'tkazardi). 0 — tasdiq o'chiq.</summary>
    [HttpPut("settings")]
    [AdminPerm("finance.main", "expenseApproval")]
    public async Task<ActionResult<ExpenseApprovalSettingsDto>> SaveSettings(ExpenseApprovalSettingsPayload p, CancellationToken ct)
    {
        if (!User.IsInRole(Roles.SuperAdmin))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Tasdiqlash chegarasini faqat superadmin o'zgartiradi" });
        if (p.Threshold < 0) return BadRequest(new { message = "Chegara manfiy bo'lmasin (0 — tasdiq o'chiq)" });
        var meta = await db.CenterMeta.FirstOrDefaultAsync(ct);
        if (meta is null) { meta = new CenterMeta(); db.CenterMeta.Add(meta); }
        var old = meta.ExpenseApprovalThreshold;
        meta.ExpenseApprovalThreshold = decimal.Round(p.Threshold, 0);
        if (old != meta.ExpenseApprovalThreshold)
            audit.Record("CenterMeta", meta.Id, "update",
                $"Chiqim tasdig'i chegarasi: {AuditService.Money(old)} → {AuditService.Money(meta.ExpenseApprovalThreshold)} so'm");
        await db.SaveChangesAsync(ct);
        return new ExpenseApprovalSettingsDto(meta.ExpenseApprovalThreshold, CanApprove, true);
    }

    /// <summary>Tasdiqlash — shu paytda chiqim tranzaksiyasi yoziladi (sana — kiritilgan sana, kiritgan — so'rov egasi).</summary>
    [HttpPost("{id}/approve")]
    [AdminPerm("finance.main", "expenseApproval")]
    public async Task<ActionResult<ExpenseRequestDto>> Approve(string id, ExpenseDecisionRequest? body, CancellationToken ct)
    {
        if (!CanApprove)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Chiqimni faqat superadmin yoki tasdiqlash ruxsati berilgan mas'ul tasdiqlaydi" });
        using var gate = await PaymentIdempotency.LockAsync("finance:expreq:" + id);
        var r = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "So'rov topilmadi" });
        var block = ExpenseApproval.DecisionBlock(r, Me);
        if (block is not null) return BadRequest(new { message = block });

        // Kassa: so'rovdagisi (yoki tasdiqlovchi tanlagani); arxivlangan bo'lsa — 400, jimgina boshqasiga tushmaydi.
        var cashbox = await CashboxService.ResolveAsync(db, string.IsNullOrWhiteSpace(body?.CashboxId) ? r.CashboxId : body!.CashboxId, r.CreatedById);
        if (cashbox.Error is not null) return BadRequest(new { message = cashbox.Error });

        var tx = ExpenseApproval.ToTransaction(r, cashbox.CashboxId);
        db.FinanceTransactions.Add(tx);
        r.Status = ExpenseApproval.Approved;
        r.DecidedAt = AppClock.Iso();
        r.DecidedById = Me;
        r.DecidedBy = MyName;
        r.DecisionNote = Trim(body?.Note);
        r.TransactionId = tx.Id;
        r.CashboxId = cashbox.CashboxId;
        audit.Record(AuditService.EntityFinanceTransaction, tx.Id, "create",
            $"Chiqim tasdiqlandi: {r.Category} — {AuditService.Money(r.Amount)} so'm (kiritdi: {r.CreatedBy}, tasdiqladi: {MyName})",
            after: AuditService.Snapshot(tx), teacherId: tx.TeacherId);
        NotificationStore.Add(db, r.CreatedById, "Chiqim tasdiqlandi",
            $"{AuditService.Money(r.Amount)} so'm — {MyName} tasdiqladi", "finance");
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    [HttpPost("{id}/reject")]
    [AdminPerm("finance.main", "expenseApproval")]
    public async Task<ActionResult<ExpenseRequestDto>> Reject(string id, ExpenseDecisionRequest body, CancellationToken ct)
    {
        if (!CanApprove)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Chiqimni faqat superadmin yoki tasdiqlash ruxsati berilgan mas'ul rad etadi" });
        var reason = Trim(body.Note);
        if (reason is null) return BadRequest(new { message = "Rad etish sababini yozing" });
        using var gate = await PaymentIdempotency.LockAsync("finance:expreq:" + id);
        var r = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "So'rov topilmadi" });
        var block = ExpenseApproval.DecisionBlock(r, Me);
        if (block is not null) return BadRequest(new { message = block });
        r.Status = ExpenseApproval.Rejected;
        r.DecidedAt = AppClock.Iso();
        r.DecidedById = Me;
        r.DecidedBy = MyName;
        r.DecisionNote = reason;
        audit.Record(AuditService.EntityFinanceTransaction, r.Id, "update",
            $"Chiqim rad etildi: {r.Category} — {AuditService.Money(r.Amount)} so'm (kiritdi: {r.CreatedBy}) — sabab: {reason}");
        NotificationStore.Add(db, r.CreatedById, "Chiqim rad etildi",
            $"{AuditService.Money(r.Amount)} so'm — {reason}", "finance");
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    /// <summary>Kiritgan odamning O'ZI qaytarib oladi (hali ko'rib chiqilmagan bo'lsa).</summary>
    [HttpPost("{id}/cancel")]
    [AdminPerm("finance.main", "expenseApproval")]
    public async Task<ActionResult<ExpenseRequestDto>> Cancel(string id, CancellationToken ct)
    {
        using var gate = await PaymentIdempotency.LockAsync("finance:expreq:" + id);
        var r = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound(new { message = "So'rov topilmadi" });
        if (r.CreatedById != Me) return StatusCode(StatusCodes.Status403Forbidden, new { message = "So'rovni faqat kiritgan xodim qaytarib oladi" });
        if (r.Status != ExpenseApproval.Pending) return BadRequest(new { message = "Bu so'rov allaqachon ko'rib chiqilgan" });
        r.Status = ExpenseApproval.Cancelled;
        r.DecidedAt = AppClock.Iso();
        r.DecidedById = Me;
        r.DecidedBy = MyName;
        audit.Record(AuditService.EntityFinanceTransaction, r.Id, "update",
            $"Chiqim so'rovi qaytarib olindi: {r.Category} — {AuditService.Money(r.Amount)} so'm");
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    /// <summary>Tasdiqlovchilarga (superadmin + ruxsat berilganlar, kiritgan odamdan tashqari) qo'ng'iroqcha.
    /// SaveChanges — chaqiruvchida.</summary>
    public static async Task NotifyApproversAsync(IAppDbContext db, ExpenseRequest r)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.SuperAdmin || u.Role == Roles.Staff)
            .Select(u => new { u.Id, u.Role, u.Permissions })
            .ToListAsync();
        foreach (var u in users)
        {
            var approver = u.Role == Roles.SuperAdmin
                           || u.Permissions.Any(p => p == ExpenseApproval.PermKey || p.StartsWith(ExpenseApproval.PermKey + ":", StringComparison.Ordinal));
            if (!approver || u.Id == r.CreatedById) continue;
            NotificationStore.Add(db, u.Id, "Chiqim tasdiq kutmoqda",
                $"{r.CreatedBy}: {AuditService.Money(r.Amount)} so'm ({r.Category}) — Moliya → Chiqimlar tasdig'i", "finance");
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(300, s.Trim().Length)];

    private async Task<List<ExpenseRequestDto>> ToDtosAsync(List<ExpenseRequest> rows, CancellationToken ct)
    {
        var teacherIds = rows.Select(r => r.TeacherId).OfType<string>().Distinct().ToList();
        var staffIds = rows.Select(r => r.StaffUserId).OfType<string>().Distinct().ToList();
        var typeIds = rows.Select(r => r.TypeId).OfType<string>().Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var staff = await db.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var types = await db.TransactionTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var boxes = await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var me = Me;
        var canApprove = CanApprove;
        return rows.Select(r => new ExpenseRequestDto(
            r.Id, r.Status, r.Date, r.Category, r.TypeId is null ? null : types.GetValueOrDefault(r.TypeId), r.Amount,
            r.Note, r.Comment,
            r.TeacherId is null ? null : teachers.GetValueOrDefault(r.TeacherId),
            r.StaffUserId is null ? null : staff.GetValueOrDefault(r.StaffUserId),
            r.Month, r.Method, r.CashboxId, r.CashboxId is null ? null : boxes.GetValueOrDefault(r.CashboxId),
            r.SalaryPayKind, r.CreatedAt, r.CreatedBy, r.DecidedAt, r.DecidedBy, r.DecisionNote, r.TransactionId,
            canApprove && ExpenseApproval.DecisionBlock(r, me) is null,
            r.CreatedById == me)).ToList();
    }
}
