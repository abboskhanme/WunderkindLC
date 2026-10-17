using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QITUVCHI — guruhning UMUMIY reytingi (testlar + ball [+ uy vazifasi]) — SPEC-backlog F4 A4 (TZ 2.2). Admin
/// «Hisobotlar → Reyting» bilan AYNAN bir xil jadval (`RatingEngine.GroupTable`, keshlangan davr reytingi).
///
/// <para>⚠️ FAQAT O'Z guruhi (<see cref="TeacherGroupAccess.OwnedBy"/>; o'rinbosar ko'rmaydi — u vaqtincha keladi) va
/// <c>journal</c> ruxsati bilan. Javobda ism, o'rin va ballar — o'qituvchi jurnalda baribir ko'radigan ma'lumot;
/// boshqa guruhlar va markaz jadvali BERILMAYDI.</para>
/// </summary>
public partial class TeacherPortalController
{
    [HttpGet("groups/{classId}/combined-rating")]
    public async Task<ActionResult<RatingGroupDto>> CombinedRating(
        string classId, [FromQuery] string? period, [FromQuery] string? key, [FromServices] DataCache cache)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal)) return Forbid();
        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == classId);
        if (group is null || !TeacherGroupAccess.OwnedBy(group, t.Id)) return NotFound(new { message = "Guruh topilmadi" });

        var (p, error) = RatingPeriods.Resolve(period, key, AppClock.Today);
        if (p is null) return BadRequest(new { message = error });
        var r = await RatingEngine.Cached(cache)(p);
        var info = r.GroupInfo.GetValueOrDefault(classId);
        if (info is null) return NotFound(new { message = "Guruh topilmadi" });

        // Admin guruh jadvali bilan bir xil a'zolar to'plami (faol/sinov, arxivlanmagan o'quvchi).
        var members = await (from sg in db.StudentGroups.AsNoTracking()
                             join s in db.Students.AsNoTracking() on sg.StudentId equals s.Id
                             where sg.GroupId == classId && sg.IsActive && sg.Status != "frozen" && !s.IsArchived
                             select new { s.Id, s.FullName })
            .ToListAsync();
        var byId = members.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().FullName, StringComparer.Ordinal);
        return RatingEngine.GroupTable(r, info, byId);
    }
}
