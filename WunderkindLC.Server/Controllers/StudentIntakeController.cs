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
/// QABUL CHEKLISTI — yangi o'quvchini qabul qilishdagi 5 ta ish (ma'lumotlar, daraja testi,
/// shartnoma, Telegram, to'lov). Hisob — <see cref="StudentIntake"/>, qoidalar —
/// <c>.claude/rules/student-intake.md</c>.
///
/// <para><b>RUXSAT — mavjud «O'quvchilar ro'yxati va profili» kaliti (<c>students.list</c>).</b>
/// Yangi kalit OCHILMADI: cheklist profil sahifasining bir qismi. O'qish ham darvozalanadi
/// (<c>ReadRequiresPerm</c>) — ro'yxatda butun markazning yangi o'quvchilari, telefonlari va
/// "to'lov qildimi / shartnoma bormi" holati bor; bu bo'limlararo o'qish uchun kerak emas.
/// Qo'lda belgi — <c>PUT</c>, ya'ni <c>students.list:edit</c> (o'quvchini tahrirlash huquqi).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.list", ReadRequiresPerm = true)]
[Route("api/admin/student-intake")]
public class StudentIntakeController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Yangi o'quvchilar kesimi: oxirgi <paramref name="days"/> kunda qabul qilinganlar + barcha
    /// SINOVDAGILAR (arxivlanmagan). To'liq bo'lmaganlar tepada.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<StudentIntakeListDto>> List([FromQuery] int? days, CancellationToken ct) =>
        await StudentIntake.ListAsync(db, days, AppClock.Today, ct);

    /// <summary>Bitta o'quvchining cheklisti (profil kartasi uchun).</summary>
    [HttpGet("{studentId}")]
    public async Task<ActionResult<StudentIntakeDetailDto>> Get(string studentId, CancellationToken ct)
    {
        var detail = await DetailAsync(studentId, ct);
        return detail is null ? NotFound(new { message = "O'quvchi topilmadi" }) : detail;
    }

    /// <summary>
    /// QO'LDA belgi: <c>done=true</c> — "bajarildi" (izoh bilan), <c>false</c> — belgini olib tashlash.
    /// Avtomatik aniqlangan holat bu bilan O'ZGARMAYDI (u mavjud yozuvlardan hisoblanadi).
    /// Javob — yangilangan cheklist.
    /// </summary>
    [HttpPut("{studentId}/{item}")]
    public async Task<ActionResult<StudentIntakeDetailDto>> SetMark(
        string studentId, string item, SetStudentIntakeMarkRequest req, CancellationToken ct)
    {
        if (!StudentIntake.IsKnownItem(item))
            return BadRequest(new { message = $"Noma'lum cheklist bandi: {item}" });
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId).Select(s => new { s.Id, s.FullName }).FirstOrDefaultAsync(ct);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var change = await StudentIntake.ApplyMarkAsync(
            db, studentId, item, req.Done, req.Note,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin", ct);

        if (change is not null)
        {
            // Audit SAQLASHDAN OLDIN — bitta tranzaksiyada (audit.md §1).
            audit.Record("StudentIntake", studentId, change.Action, $"{change.Summary} — {student.FullName}",
                studentId: studentId);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Unikal (o'quvchi, band) — parallel ikkinchi bosish. Jim yutilmaydi: sabab ko'rinsin.
                return Conflict(new { message = "Belgi shu payt boshqa so'rov bilan o'zgartirildi — sahifani yangilang" });
            }
        }

        return (await DetailAsync(studentId, ct))!;
    }

    private async Task<StudentIntakeDetailDto?> DetailAsync(string studentId, CancellationToken ct)
    {
        var checklist = await StudentIntake.ForStudentAsync(db, studentId, AppClock.Today, ct);
        if (checklist is null) return null;
        // Bot nomi — ota-onaga "botga kiring" havolasi uchun (ommaviy ma'lumot, token EMAS).
        var bot = await db.CenterMeta.AsNoTracking().Select(m => m.TelegramBotUsername).FirstOrDefaultAsync(ct);
        return new StudentIntakeDetailDto(checklist, (bot ?? "").Trim().TrimStart('@'));
    }
}
