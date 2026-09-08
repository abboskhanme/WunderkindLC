using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using IntellectCRM.Infrastructure.Data;

namespace IntellectCRM.Server.Controllers;

/// <summary>
/// CHEGIRMA REGISTRI — o'quvchi profilidagi «Chegirma» tabi: barcha olgan chegirmalari,
/// yangisini berish, amaldagisini tahrirlash va bekor qilish.
///
/// <para>⚠️ <b>PUL MANTIG'I BU YERDA O'ZGARMAYDI.</b> Hisob-kitob avvalgidek
/// <c>Student.Discount*</c> maydonlariga tayanadi (<c>TuitionService.DiscountForMonth</c>) —
/// registr uni AKS ETTIRADI, almashtirmaydi. Yozish yagona joydan:
/// <see cref="StudentDiscountService"/>. Batafsil: <c>.claude/rules/discounts.md</c>.</para>
///
/// <para>RUXSAT — <c>students.list</c>: chegirma allaqachon o'quvchi formasidan shu kalit bilan
/// tahrirlanardi, yangi kalit kiritish o'sha lineyani ikkiga bo'lardi. Yozish amallari odatdagi
/// qoida bo'yicha <c>students.list:edit</c> talab qiladi (<c>AdminPermAttribute</c>).</para>
///
/// <para>Har amal TO'LIQ javob qaytaradi — klient qayta so'rov yubormasin.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.list")]
[Route("api/admin/students/{studentId}/discounts")]
public class StudentDiscountsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private string? ActorId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>O'quvchining chegirma registri + qo'llangan chegirmalar tarixi.</summary>
    [HttpGet]
    public async Task<ActionResult<StudentDiscountsResponseDto>> Get(string studentId)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId))
            return NotFound(new { message = "O'quvchi topilmadi" });
        return await StudentDiscountService.BuildAsync(db, studentId);
    }

    /// <summary>
    /// YANGI chegirma. Mavjud amaldagi qator <c>replaced</c> bo'lib yopiladi (o'chmaydi — tarix).
    ///
    /// <para><paramref name="applyCurrentMonth"/> — joriy oy hisobiga DARHOL qo'llansinmi
    /// (aks holda keyingi oydan). <c>PUT /students/{id}?applyDiscount=true</c> bilan AYNAN bir
    /// xil mantiq va AYNAN bir xil kod.</para>
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<StudentDiscountsResponseDto>> Create(
        string studentId, [FromBody] StudentDiscountPayloadDto p, [FromQuery] bool applyCurrentMonth = true)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var (spec, error) = Validate(p);
        if (error is not null) return BadRequest(new { message = error });

        // NOLINCHI chegirmani YARATIB bo'lmaydi: "0% / 0 so'm" — chegirma emas. Chegirmani olib
        // tashlash uchun BEKOR QILISH bor (u sababni ham yozadi, tarixda savol qolmaydi).
        if (spec!.IsEmpty)
            return BadRequest(new { message = "Chegirma bo'sh: foiz yoki summa kiritilishi kerak" });

        var before = Snapshot(student);
        var row = await StudentDiscountService.ApplyAsync(db, student, spec, Actor, ActorId);
        var applied = applyCurrentMonth && await StudentDiscountService.ReapplyCurrentMonthAsync(db, student);

        // ⚠️ audit.Record SaveChanges QILMAYDI — chaqiruvchining tranzaksiyasiga qo'shiladi
        // (.claude/rules/audit.md §1), shuning uchun SaveChangesAsync dan OLDIN chaqiriladi.
        audit.Record(AuditService.EntityStudentDiscount, student.Id, "create",
            $"Chegirma berildi: {Describe(row!)}{AppliedSuffix(applied)} ({student.FullName})",
            before: before, after: Snapshot(student), studentId: student.Id);

        await db.SaveChangesAsync();
        return await StudentDiscountService.BuildAsync(db, studentId);
    }

    /// <summary>
    /// AMALDAGI chegirmani tahrirlash (joyida — yangi qator ochilmaydi).
    ///
    /// <para>⚠️ TARIXIY qator (<c>replaced</c>/<c>cancelled</c>) tahrirlanMAYDI — 400. O'tgan
    /// oylarning hisobi allaqachon <c>MonthlyCharge</c> da yozilgan; tarixiy qatorni o'zgartirish
    /// hisobot bilan pulni bir-biridan ayirib yuborardi.</para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<StudentDiscountsResponseDto>> Update(
        string studentId, string id, [FromBody] StudentDiscountPayloadDto p,
        [FromQuery] bool applyCurrentMonth = true)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var row = await db.StudentDiscounts.FirstOrDefaultAsync(d => d.Id == id && d.StudentId == studentId);
        if (row is null) return NotFound(new { message = "Chegirma topilmadi" });

        var (spec, error) = Validate(p);
        if (error is not null) return BadRequest(new { message = error });
        if (spec!.IsEmpty)
            return BadRequest(new { message = "Chegirma bo'sh: foiz yoki summa kiritilishi kerak" });

        var before = Snapshot(student);
        if (!await StudentDiscountService.UpdateAsync(db, student, row, spec, Actor))
            return BadRequest(new { message = "Tarixiy chegirmani tahrirlab bo'lmaydi" });
        var applied = applyCurrentMonth && await StudentDiscountService.ReapplyCurrentMonthAsync(db, student);

        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"Chegirma tahrirlandi: {Describe(row)}{AppliedSuffix(applied)} ({student.FullName})",
            before: before, after: Snapshot(student), studentId: student.Id);

        await db.SaveChangesAsync();
        return await StudentDiscountService.BuildAsync(db, studentId);
    }

    /// <summary>
    /// Chegirmani BEKOR qilish: qator o'chmaydi — <c>cancelled</c> bo'ladi (sabab bilan), o'quvchidan
    /// esa chegirma OLINADI. Faqat AMALDAGI qator bekor qilinadi (tarixiy — 400).
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<StudentDiscountsResponseDto>> Cancel(
        string studentId, string id, [FromBody] StudentDiscountCancelDto body,
        [FromQuery] bool applyCurrentMonth = true)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        var row = await db.StudentDiscounts.FirstOrDefaultAsync(d => d.Id == id && d.StudentId == studentId);
        if (row is null) return NotFound(new { message = "Chegirma topilmadi" });

        var before = Snapshot(student);
        var reason = (body?.Reason ?? "").Trim();
        if (!await StudentDiscountService.CancelAsync(db, student, row, reason, Actor))
            return BadRequest(new { message = "Bu chegirma allaqachon yopilgan — bekor qilib bo'lmaydi" });
        var applied = applyCurrentMonth && await StudentDiscountService.ReapplyCurrentMonthAsync(db, student);

        var why = reason.Length == 0 ? "" : $" (sabab: {reason})";
        audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"Chegirma bekor qilindi{why}: {Describe(row)}{AppliedSuffix(applied)} ({student.FullName})",
            before: before, after: Snapshot(student), studentId: student.Id);

        await db.SaveChangesAsync();
        return await StudentDiscountService.BuildAsync(db, studentId);
    }

    // ==================== yordamchilar ====================

    /// <summary>Tanani tekshiradi va <see cref="DiscountSpec"/> ga o'giradi. Xato bo'lsa
    /// (spec = null, xabar) qaytadi — chaqiruvchi 400 beradi.</summary>
    private static (DiscountSpec? Spec, string? Error) Validate(StudentDiscountPayloadDto p)
    {
        if (p is null) return (null, "So'rov tanasi bo'sh");
        var start = (p.StartMonth ?? "").Trim();
        var end = (p.EndMonth ?? "").Trim();
        if (!IsMonth(start)) return (null, "Boshlanish oyi noto'g'ri formatda (kutilgan: YYYY-MM)");
        if (!IsMonth(end)) return (null, "Tugash oyi noto'g'ri formatda (kutilgan: YYYY-MM)");
        // Teskari oraliq: chegirma HECH QAYSI oyda amal qilmasdi — jimgina saqlansa
        // "chegirma berdim, lekin ishlamayapti" holati chiqardi.
        if (start.Length > 0 && end.Length > 0 && string.CompareOrdinal(end, start) < 0)
            return (null, "Tugash oyi boshlanish oyidan oldin bo'lishi mumkin emas");

        return (new DiscountSpec(p.Pct, p.Amount, start, end, p.Reason ?? "", p.GroupId).Normalized(), null);
    }

    /// <summary>Bo'sh (cheklovsiz) yoki "yyyy-MM".</summary>
    private static bool IsMonth(string v) =>
        v.Length == 0 || (v.Length == 7 && v[4] == '-' && DateOnly.TryParse($"{v}-01", out _));

    /// <summary>Audit uchun o'zbekcha, TO'LIQ tavsif: "20% / 50 000 so'm — «Ko'p bolali oila» (2026-09 dan)".</summary>
    private static string Describe(StudentDiscount d)
    {
        var text = $"{d.Pct}% / {AuditService.Money(d.Amount)} so'm";
        if (!string.IsNullOrWhiteSpace(d.Reason)) text += $" — \"{d.Reason}\"";
        var period = (d.StartMonth.Length, d.EndMonth.Length) switch
        {
            (> 0, > 0) => $" ({d.StartMonth} – {d.EndMonth})",
            (> 0, 0) => $" ({d.StartMonth} dan)",
            (0, > 0) => $" ({d.EndMonth} gacha)",
            _ => "",
        };
        var group = string.IsNullOrWhiteSpace(d.GroupName) ? "" : $" [guruh: {d.GroupName}]";
        return text + period + group;
    }

    /// <summary>Joriy oy hisobiga qo'llandimi — audit matnining oxiri (mavjud o'quvchi formasidagi
    /// matn bilan bir xil ohang).</summary>
    private static string AppliedSuffix(bool applied) =>
        applied ? " — joriy oy hisobi to'g'rilandi" : " — keyingi oydan amal qiladi";

    /// <summary>Audit Before/After uchun chegirma snapshot'i (<c>Student.Discount*</c> — pul
    /// haqiqati aynan shu maydonlarda).</summary>
    private static object Snapshot(Student s) => new
    {
        s.DiscountPct,
        s.DiscountAmount,
        s.DiscountNote,
        s.DiscountStartMonth,
        s.DiscountEndMonth,
        s.DiscountGroupId,
    };
}
