using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'qituvchilar faollik hisoboti (admin): dars o'tyaptimi, baho qo'ymoqdami, mavzu/uy vazifa
/// bermoqdami.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("teacherReports")]
[Route("api/admin/teacher-reports")]
public class TeacherReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Barcha o'qituvchilar bo'yicha umumiy ko'rinish. month bo'sh/null = Umumiy (barcha oylar yig'indisi).</summary>
    [HttpGet]
    public async Task<ActionResult<TeacherReportOverviewDto>> Overview([FromQuery] string? month = null)
        => await TeacherActivityReport.BuildOverviewAsync(db, month);

    /// <summary>Bitta o'qituvchining batafsil hisoboti (guruh/fan yoyilmasi). month bo'sh/null = Umumiy.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TeacherReportDetailDto>> Detail(string id, [FromQuery] string? month = null)
    {
        var dto = await TeacherActivityReport.BuildDetailAsync(db, id, month);
        return dto is null ? NotFound() : dto;
    }
}
