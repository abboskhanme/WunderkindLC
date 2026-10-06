using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QITUVCHI PORTALI — bosh sahifa, dars jadvali va "O'quvchilarim" (faqat O'QISH).
///
/// <para><b>Ruxsat:</b> sinf darajasidagi <c>[Authorize(Roles = "teacher")]</c> avtomatik
/// qo'llanadi va har endpoint o'qituvchini TOKENDAN aniqlaydi (<c>Me()</c>) — begona
/// o'qituvchining ma'lumoti hech qanday parametr bilan ochilmaydi.
/// Bo'lim ruxsatlari (<see cref="TeacherPermissions"/>):
/// <list type="bullet">
/// <item><c>overview</c> — hammaga; "Dars jadvali" ruxsati bo'lmasa bugungi darslar BO'SH qaytadi
///   (403 emas: bosh sahifa baribir ochilishi kerak).</item>
/// <item><c>schedule</c> — <c>schedule</c> ("Dars jadvali"), aks holda 403.</item>
/// <item><c>students</c> — <c>journal</c> (o'quvchilar ro'yxati jurnalning bir qismi), aks holda 403.</item>
/// </list></para>
///
/// <para>Mantiq to'liq <see cref="TeacherHomeService"/> da — u jurnal, o'rinbosar va to'lov
/// darvozasi qoidalarini QAYTA YOZMAYDI, ularni chaqiradi.</para>
/// </summary>
public partial class TeacherPortalController
{
    [HttpGet("overview")]
    public async Task<ActionResult<TeacherOverviewDto>> Overview()
    {
        var t = await Me();
        if (t is null) return NotFound();
        return await TeacherHomeService.OverviewAsync(
            db, t.Id, AppClock.Today, t.Permissions.Contains(TeacherPermissions.Schedule));
    }

    /// <summary>O'z guruhlaridagi qarzdorlar soni va jami qarz (maosh sahifasi: "qarzdorlar to'lasa").</summary>
    [HttpGet("debt-summary")]
    public async Task<ActionResult<TeacherDebtSummaryDto>> DebtSummary()
    {
        var t = await Me();
        if (t is null) return NotFound();
        return await TeacherHomeService.DebtSummaryForTeacherAsync(db, t.Id);
    }

    /// <summary>Oraliqdagi darslar (<c>from</c>/<c>to</c> "yyyy-MM-dd", ikkalasi ham kiradi).
    /// Berilmasa — joriy hafta (Du..Ya). Oraliq ko'pi bilan <see cref="TeacherHomeService.MaxScheduleDays"/> kun.</summary>
    [HttpGet("schedule")]
    public async Task<ActionResult<List<TeacherLessonDto>>> Schedule([FromQuery] string? from, [FromQuery] string? to)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Schedule)) return Forbid();

        var today = AppClock.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateOnly f, e;
        if (string.IsNullOrWhiteSpace(from)) f = weekStart;
        else if (!DateOnly.TryParse(from, out f)) return BadRequest(new { message = "Boshlanish sanasi noto'g'ri" });
        if (string.IsNullOrWhiteSpace(to)) e = f.AddDays(6);
        else if (!DateOnly.TryParse(to, out e)) return BadRequest(new { message = "Tugash sanasi noto'g'ri" });
        if (e < f) return BadRequest(new { message = "Tugash sanasi boshlanishidan oldin" });
        if (e.DayNumber - f.DayNumber + 1 > TeacherHomeService.MaxScheduleDays)
            return BadRequest(new { message = $"Oraliq {TeacherHomeService.MaxScheduleDays} kundan oshmasin" });

        return await TeacherHomeService.ScheduleAsync(db, t.Id, f, e);
    }

    /// <summary>Barcha guruhlardagi o'quvchilar + oylik davomat (<c>month</c> "yyyy-MM", ixtiyoriy).
    /// To'lov darvozasi yashirgan o'quvchi jurnaldagidek bu yerda ham ko'rinmaydi.</summary>
    [HttpGet("students")]
    public async Task<ActionResult<TeacherStudentsDto>> Students([FromQuery] string? month)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        return await TeacherHomeService.StudentsAsync(db, t.Id, month);
    }
}
