using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// CHEGIRMA REGISTRI — o'quvchi profilidagi «Chegirma» tabi: barcha olgan chegirmalari,
/// yangisini berish, amaldagisini tahrirlash va bekor qilish.
///
/// <para>⚠️ <b>CHEGIRMA HAR FAN (guruh) UCHUN ALOHIDA.</b> Har <c>(o'quvchi, guruh)</c>
/// qamrovida ko'pi bilan BITTA amaldagi qator bo'ladi (<c>groupId: null</c> — «barcha guruhlar»
/// qamrovi). Band qamrovga ikkinchi chegirma berishga urinish — <b>409</b>: jimgina
/// almashtirilsa, admin "yangi berdim" deb o'ylab eskisini bilmasdan o'chirib yuborardi.</para>
///
/// <para>⚠️ <b>REGISTR — PUL MANBASI.</b> Oylik hisob chegirmani AYNAN shu jadvaldan oladi
/// (<c>DiscountBook</c> → <c>TuitionService.DiscountForMonth</c>); <c>Student.Discount*</c> esa
/// faqat KO'ZGU. Yozish yagona joydan: <see cref="StudentDiscountService"/>.
/// Batafsil: <c>.claude/rules/discounts.md</c>.</para>
///
/// <para>⚠️ O'quvchi TAHRIRLASH formasi (<c>PUT /api/admin/students/{id}</c>) chegirmani ENDI
/// YOZMAYDI — bu yagona yuza.</para>
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

        // ⚠️ BAND QAMROV — 409, jimgina ALMASHTIRILMAYDI. Aks holda admin ikkinchi chegirma
        // "qo'shdim" deb o'ylab, aslida birinchisini bilmasdan yopib yuborardi.
        var busy = await StudentDiscountService.ActiveInScopeAsync(db, studentId, spec.GroupId);
        if (busy is not null)
            return Conflict(new
            {
                message = $"«{DiscountRules.ScopeLabel(busy)}» uchun allaqachon chegirma bor "
                          + $"({DiscountRules.ValueLabel(busy.Pct, busy.Amount)}) — uni tahrirlang yoki bekor qiling",
                existingId = busy.Id,
            });

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

        // Qamrov (fan) BOSHQASIGA ko'chirilayotgan bo'lsa — u yerda bo'sh joy bo'lishi shart.
        if (!string.Equals(row.GroupId ?? "", spec.GroupId ?? "", StringComparison.Ordinal))
        {
            var busy = await StudentDiscountService.ActiveInScopeAsync(db, studentId, spec.GroupId);
            if (busy is not null && busy.Id != row.Id)
                return Conflict(new
                {
                    message = $"«{DiscountRules.ScopeLabel(busy)}» uchun allaqachon chegirma bor "
                              + $"({DiscountRules.ValueLabel(busy.Pct, busy.Amount)}) — avval o'shani bekor qiling",
                    existingId = busy.Id,
                });
        }

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

    /// <summary>Audit uchun o'zbekcha, TO'LIQ tavsif:
    /// "Matematika: 20% / 50 000 so'm — «Ko'p bolali oila» (2026-09 dan) [guruh: A guruh]".
    /// ⚠️ QAMROV (fan) boshida turadi — o'quvchida bir nechta chegirma bo'lishi mumkin, ya'ni
    /// tarixda "qaysi fanniki" birinchi savol.</summary>
    private static string Describe(StudentDiscount d)
    {
        var text = $"{DiscountRules.ScopeLabel(d)}: {d.Pct}% / {AuditService.Money(d.Amount)} so'm";
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

    /// <summary>Audit Before/After uchun chegirma KO'ZGUSINING snapshot'i.
    /// ⚠️ Bu maydonlar endi pul manbai EMAS (registr manba) — snapshot faqat "asosiy chegirma
    /// qanday o'zgardi" ni ko'rsatadi; qaysi FAN o'zgargani <c>summary</c> matnida.</summary>
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
