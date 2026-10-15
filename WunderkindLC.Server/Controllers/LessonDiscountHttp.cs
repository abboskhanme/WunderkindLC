using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «CHEGIRMA» (2026-10-15 dan kelgan darslar bo'yicha) — HTTP qismi BITTA joyda: <see cref="StudentsController"/>
/// (`students.list`) ham, <see cref="KassaController"/> (`kassa`) ham shuni chaqiradi — to'lovni kim qabul
/// qila olsa, chegirmani ham o'sha qo'ya oladi (WunderkindLMS dagi kabi). Ruxsat har controllerning
/// sinf darajasidagi darvozasida. Qoida — <see cref="LessonDiscountService"/>.
///
/// <para>Pul amallari bilan BIR qulf ostida (`PaymentIdempotency`, kalit `student:{id}`): parallel
/// to'lov va chegirma bir-birining balansini eski qiymat ustidan yozib yubormasin.</para>
/// </summary>
public static class LessonDiscountHttp
{
    public static async Task<ActionResult<List<LessonDiscountDto>>> ListAsync(AppDbContext db, string studentId)
    {
        var rows = await db.LessonDiscounts.AsNoTracking()
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
        var gids = rows.Select(r => r.GroupId).Distinct().ToList();
        var names = await db.Classes.AsNoTracking().Where(c => gids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        return rows.Select(r => ToDto(r, names.GetValueOrDefault(r.GroupId, ""))).ToList();
    }

    /// <summary>Eski sahifa (kelmagan darslar soni bilan) — rad etiladi, foydalanuvchi sahifani yangilaydi.</summary>
    public const string StaleClientMessage =
        "Chegirma endi KELGAN darslar soni bo'yicha hisoblanadi — sahifani yangilang (Ctrl+F5)";

    public static async Task<ActionResult<LessonDiscountQuoteDto>> QuoteAsync(
        AppDbContext db, string studentId, string? groupId, string? month, int? attended)
    {
        if (attended is null) return new BadRequestObjectResult(new { message = StaleClientMessage });
        var (s, g, error) = await LoadAsync(db, studentId, groupId);
        if (error is not null) return error;
        try
        {
            var q = await LessonDiscountService.QuoteAttendedAsync(db, s!, g!, month ?? "", attended.Value);
            return new LessonDiscountQuoteDto(q.GroupId, q.Month, q.Attended, q.LessonsInMonth, q.LessonPrice,
                q.AttendedPrice, q.MonthAmount, q.Payable, q.Remaining, q.Amount);
        }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    public static async Task<ActionResult<LessonDiscountDto>> ApplyAsync(
        AppDbContext db, AuditService audit, ClaimsPrincipal user, string studentId, LessonDiscountRequest req)
    {
        if (req.Attended is null) return new BadRequestObjectResult(new { message = StaleClientMessage });
        using var _ = await PaymentIdempotency.LockAsync("student:" + studentId);
        var (s, g, error) = await LoadAsync(db, studentId, req.GroupId);
        if (error is not null) return error;
        try
        {
            var row = await LessonDiscountService.ApplyAttendedAsync(
                db, s!, g!, req.Month ?? "", req.Attended.Value, req.Reason, ActorId(user), ActorName(user));
            audit.Record(AuditService.EntityStudentDiscount, s!.Id, "create",
                $"Chegirma (kelgan darslar bo'yicha): {row.AttendedLessons} dars keldi × {AuditService.Money(row.LessonPrice)} — "
                + $"ayrildi {AuditService.Money(row.Amount)} so'm — {g!.Name}, {row.Month} — sabab: {row.Reason} ({s.FullName})",
                after: new { row.Month, row.AttendedLessons, row.LessonPrice, row.Amount, row.Reason }, studentId: s.Id);
            await db.SaveChangesAsync();
            return ToDto(row, g.Name);
        }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    public static async Task<ActionResult<LessonDiscountDto>> CancelAsync(
        AppDbContext db, AuditService audit, ClaimsPrincipal user, string studentId, string discountId)
    {
        using var _ = await PaymentIdempotency.LockAsync("student:" + studentId);
        var s = await db.Students.FindAsync(studentId);
        if (s is null) return new NotFoundObjectResult(new { message = "O'quvchi topilmadi" });
        try
        {
            var row = await LessonDiscountService.CancelAsync(db, s, discountId, ActorName(user));
            var groupName = await db.Classes.AsNoTracking().Where(c => c.Id == row.GroupId).Select(c => c.Name).FirstOrDefaultAsync() ?? "";
            audit.Record(AuditService.EntityStudentDiscount, s.Id, "update",
                $"Chegirma ({(row.AttendedLessons is null ? "kelmagan darslar" : "kelgan darslar bo'yicha")}) bekor qilindi: {AuditService.Money(row.Amount)} so'm — {groupName}, {row.Month} ({s.FullName})",
                before: new { row.Month, row.Lessons, row.AttendedLessons, row.Amount, row.Reason }, studentId: s.Id);
            await db.SaveChangesAsync();
            return ToDto(row, groupName);
        }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { message = ex.Message }); }
    }

    private static async Task<(Domain.Student? S, Domain.Group? G, ActionResult? Error)> LoadAsync(
        AppDbContext db, string studentId, string? groupId)
    {
        var s = await db.Students.FindAsync(studentId);
        if (s is null) return (null, null, new NotFoundObjectResult(new { message = "O'quvchi topilmadi" }));
        if (string.IsNullOrWhiteSpace(groupId)) return (null, null, new BadRequestObjectResult(new { message = "Guruhni tanlang" }));
        // Faqat o'quvchi a'zo bo'lgan (yoki bo'lgan) guruh — begona guruh hisobiga chegirma yozilmasin.
        var isMember = await db.StudentGroups.AnyAsync(m => m.StudentId == studentId && m.GroupId == groupId);
        var g = isMember ? await db.Classes.FindAsync(groupId) : null;
        if (g is null) return (null, null, new BadRequestObjectResult(new { message = "O'quvchi bu guruhda emas" }));
        return (s, g, null);
    }

    private static LessonDiscountDto ToDto(Domain.LessonDiscount r, string groupName) => new(
        r.Id, r.GroupId, groupName, r.Month, r.Lessons, r.LessonPrice, r.Amount, r.Reason,
        r.CreatedAt, r.CreatedByName, r.CancelledAt is not null, r.CancelledAt, r.CancelledByName, r.AttendedLessons);

    private static string ActorId(ClaimsPrincipal u) => u.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
    private static string ActorName(ClaimsPrincipal u) => u.FindFirst(ClaimTypes.Name)?.Value ?? "";
}
