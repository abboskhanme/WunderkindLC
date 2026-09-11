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
/// O'qituvchilar davomati — har o'qituvchining kunlik ish davomati (keldi / kelmadi / kechikdi).
/// Oylik board: o'qituvchilar × kunlar.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers.attendance")]
[Route("api/admin/teacher-attendance")]
public class TeacherAttendanceController(
    AppDbContext db, TurnstileService turnstile, IHubContext<LiveHub> live) : ControllerBase
{
    private static readonly HashSet<string> Valid = new() { "present", "absent", "late" };

    /// <summary>Kunlik dashboard: har o'qituvchi holati, kelgan/ketgan vaqti, kechikishi + jamlama.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<TeacherAttendanceDashboardDto>> Dashboard([FromQuery] string? date)
    {
        var d = string.IsNullOrEmpty(date) || date.Length < 10 ? AppClock.Today.ToString("yyyy-MM-dd") : date[..10];
        return await turnstile.BuildDashboardAsync(db, d);
    }

    /// <summary>Turniket qurilmasidan so'nggi hodisalarni tortib olib davomatni yangilash.</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TurnstileSyncResultDto>> Sync()
    {
        var res = await turnstile.SyncAsync(db);
        if (res.Ok && res.EventsFetched > 0)
            await live.Clients.Group(LiveHub.Group("turnstile"))
                .SendAsync("turnstileChanged", new { at = AppClock.Iso() });
        return res;
    }

    /// <summary>Tanlangan oy ("yyyy-MM") uchun board: faol o'qituvchilar + o'sha oy belgilari.</summary>
    [HttpGet]
    public async Task<ActionResult<TeacherAttendanceBoardDto>> Board([FromQuery] string? month)
    {
        var m = string.IsNullOrEmpty(month) || month.Length < 7 ? AppClock.Now.ToString("yyyy-MM") : month[..7];

        var teachers = (await db.Teachers.Where(t => !t.IsArchived)
                .OrderBy(t => t.FullName).ToListAsync())
            .Select(t => new TeacherNameDto(t.Id, t.FullName, TeacherSalaryCalc.StartDateOf(t) ?? ""))
            .ToList();

        var entries = await db.TeacherAttendances
            .Where(a => a.Date.StartsWith(m))
            .Select(a => new TeacherAttendanceDto(a.TeacherId, a.Date, a.Status, a.Note))
            .ToListAsync();

        return new TeacherAttendanceBoardDto(teachers, entries);
    }

    /// <summary>Bitta kun-katakni belgilash. Status bo'sh / noto'g'ri bo'lsa — belgini o'chiradi.</summary>
    [HttpPut]
    public async Task<IActionResult> Set(SetTeacherAttendanceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TeacherId) || string.IsNullOrEmpty(req.Date) || req.Date.Length < 10)
            return BadRequest(new { message = "O'qituvchi va sana ko'rsatilishi shart" });

        var teacher = await db.Teachers.FindAsync(req.TeacherId);
        if (teacher is null) return NotFound();
        // O'qituvchi ishga kirgan sanadan OLDINGI kunlarga davomat belgilab bo'lmaydi.
        var start = TeacherSalaryCalc.StartDateOf(teacher);
        if (start is not null && string.CompareOrdinal(req.Date, start) < 0)
            return BadRequest(new { message = "O'qituvchi bu sanadan keyin ishga kirgan — davomat belgilab bo'lmaydi" });

        var existing = await db.TeacherAttendances
            .FirstOrDefaultAsync(a => a.TeacherId == req.TeacherId && a.Date == req.Date);

        var status = req.Status ?? "";
        if (!Valid.Contains(status))
        {
            // Bo'sh / noto'g'ri — belgini o'chiramiz.
            if (existing is not null) db.TeacherAttendances.Remove(existing);
        }
        else if (existing is null)
        {
            db.TeacherAttendances.Add(new TeacherAttendance
            {
                TeacherId = req.TeacherId,
                Date = req.Date,
                Status = status,
                Note = req.Note ?? "",
                Source = "manual",
            });
        }
        else
        {
            existing.Status = status;
            existing.Note = req.Note ?? "";
            existing.Source = "manual";
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bitta kun uchun barcha faol o'qituvchini belgilash: status berilsa hammasi shu holatga,
    /// bo'sh/noto'g'ri bo'lsa o'sha kun belgilari to'liq o'chiriladi. Sarlavhani bosish (toggle) uchun.</summary>
    [HttpPut("day")]
    public async Task<IActionResult> SetDay(SetTeacherAttendanceDayRequest req)
    {
        if (string.IsNullOrEmpty(req.Date) || req.Date.Length < 10)
            return BadRequest(new { message = "Sana ko'rsatilishi shart" });

        var existing = await db.TeacherAttendances.Where(a => a.Date == req.Date).ToListAsync();
        var status = req.Status ?? "";

        if (!Valid.Contains(status))
        {
            db.TeacherAttendances.RemoveRange(existing);
        }
        else
        {
            var byTeacher = existing.ToDictionary(a => a.TeacherId);
            var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync();
            foreach (var te in teachers)
            {
                // Ishga kirgan sanadan oldingi kun bo'lsa — o'tkazib yuboramiz.
                var start = TeacherSalaryCalc.StartDateOf(te);
                if (start is not null && string.CompareOrdinal(req.Date, start) < 0) continue;
                if (byTeacher.TryGetValue(te.Id, out var a)) a.Status = status;
                else db.TeacherAttendances.Add(new TeacherAttendance { TeacherId = te.Id, Date = req.Date, Status = status });
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
