using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Admin "O'qituvchilar (kabinet)" bo'limi — o'qituvchilarning kabinetga kirish faolligi
/// (birinchi/oxirgi login). "Ota-onalar" bo'limiga o'xshash. (Qurilma ustunlari 2026-10 da olib
/// tashlandi — o'rnatiladigan ilova ishlatilmaydi.)
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app.teachers")]
[Route("api/admin/app/teachers")]
public class AppTeachersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherAppRowDto>>> GetAll()
    {
        var teachers = await db.Teachers.Where(t => !t.IsArchived).ToListAsync();
        var userIds = teachers.Where(t => t.UserId != null).Select(t => t.UserId!).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u);

        return teachers.Select(t =>
        {
            string? first = null, last = null;
            if (t.UserId is not null && users.TryGetValue(t.UserId, out var u)) { first = u.FirstLoginAt; last = u.LastLoginAt; }
            return new TeacherAppRowDto(
                t.Id, t.FullName, t.Phone ?? "",
                first is not null, first, last);
        })
        .OrderByDescending(r => r.LastSeenAt, StringComparer.Ordinal)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
}
