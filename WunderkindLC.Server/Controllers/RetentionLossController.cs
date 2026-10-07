using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «KETISH SABABLARI» (Retention / Loss) — Hisobotlar → O'qituvchilar.
///
/// <para>Savol: tanlangan davrda qaysi o'qituvchidan nechta o'quvchi KETDI, NEGA ketdi va bu uning
/// aktiv o'quvchilariga nisbatan qancha. Hisob — <see cref="RetentionLossReport"/> (sof funksiya,
/// testlangan); bu controller faqat so'rovni tekshiradi va ma'lumot yuklashni chaqiradi.</para>
///
/// <para>RUXSAT: <c>teachers.retention</c>, <c>ReadRequiresPerm = true</c> — javobda o'quvchi
/// ISMLARI va ketish sabablari bor, odatdagi "har qanday xodim GET o'qiy oladi" qoidasi bu yerda
/// ishlamasin (<c>audit</c>/<c>contacts</c> bilan bir xil sabab). Yozuvchi amal YO'Q.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teachers.retention", ReadRequiresPerm = true)]
[Route("api/admin/retention-loss")]
public class RetentionLossController(IAppDbContext db) : ControllerBase
{
    /// <param name="from">Davr boshi "yyyy-MM" (bo'sh — oxirgi 6 oy).</param>
    /// <param name="to">Davr oxiri "yyyy-MM" (bo'sh — joriy oy).</param>
    /// <param name="teacherId">Faqat shu o'qituvchi (ketish paytida guruhni o'qitgan).</param>
    /// <param name="courseId">Faqat shu kurs.</param>
    /// <param name="kind">all | active | trial — diagramma va jadval qaysi ketishlarni ko'rsatadi.</param>
    [HttpGet]
    public async Task<ActionResult<RetentionLossReportDto>> Get(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? teacherId,
        [FromQuery] string? courseId, [FromQuery] string? kind, CancellationToken ct)
    {
        var (query, error) = RetentionLossReport.Normalize(from, to, teacherId, courseId, kind, AppClock.Today);
        if (error is not null) return BadRequest(new { message = error });
        return await RetentionLossReport.BuildAsync(db, query, ct);
    }
}
