using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Hubs;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quvchilar turniketi — o'quvchining turniket/FaceID kirgan va chiqqan vaqtlari.
/// Ma'lumot turniket integratsiyasidan keladi (xom hodisalar Student.DeviceUserId bo'yicha moslanadi).
/// Tarix hodisalar jurnalida (TurnstileEvent) saqlanadi — istalgan kunni ko'rish mumkin.
/// <para>⚠️ "Barcha o'quvchilar bir kunda" sahifasi OLIB TASHLANDI — tarix endi o'quvchi
/// PROFILIDA ko'rsatiladi, ya'ni bitta o'quvchi haqidagi barcha ma'lumot bir joyda turadi.
/// Shu sabab bu yerda ro'yxat emas, <c>GET {studentId}</c> bor.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.turnstile")]
[Route("api/admin/students/turnstile")]
public class StudentTurnstileController(
    AppDbContext db, TurnstileService turnstile, IHubContext<LiveHub> live) : ControllerBase
{
    /// <summary>
    /// BITTA o'quvchining turniket tarixi: <c>date</c> (yyyy-MM-dd) kunidagi o'tishlar va
    /// <c>month</c> (yyyy-MM) ichidagi faol kunlar (kalendar chizig'i uchun).
    /// Ikkalasi ham ixtiyoriy: <c>date</c> — bugun, <c>month</c> — o'sha kunning oyi.
    /// </summary>
    [HttpGet("{studentId}")]
    public async Task<ActionResult<StudentTurnstileHistoryDto>> History(
        string studentId, [FromQuery] string? date, [FromQuery] string? month)
    {
        // AsNoTracking — javob faqat O'QISH uchun, tasodifan bazaga yozib yubormaylik.
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
        if (student is null) return NotFound();

        var d = string.IsNullOrEmpty(date) || date.Length < 10 ? AppClock.Today.ToString("yyyy-MM-dd") : date[..10];
        var m = string.IsNullOrEmpty(month) || month.Length < 7 ? d[..7] : month[..7];
        return await turnstile.BuildStudentHistoryAsync(db, student, d, m);
    }

    /// <summary>Turniket qurilmasidan so'nggi hodisalarni tortib oladi (o'quvchi/o'qituvchi — barchasi bir sync).</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TurnstileSyncResultDto>> Sync()
    {
        var res = await turnstile.SyncAsync(db);
        if (res.Ok && res.EventsFetched > 0)
            await live.Clients.Group(LiveHub.Group("turnstile"))
                .SendAsync("turnstileChanged", new { at = AppClock.Iso() });
        return res;
    }

    /// <summary>O'quvchiga turniket qurilma ID'sini (employeeNo) biriktiradi. Bo'sh = aloqani uzadi.</summary>
    [HttpPut("device")]
    public async Task<IActionResult> SetDevice(SetStudentDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.StudentId))
            return BadRequest(new { message = "O'quvchi ko'rsatilishi shart" });
        var s = await db.Students.FindAsync(req.StudentId);
        if (s is null) return NotFound();
        s.DeviceUserId = (req.DeviceUserId ?? "").Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }
}
