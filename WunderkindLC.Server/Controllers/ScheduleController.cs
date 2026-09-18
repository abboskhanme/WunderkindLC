using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// DARS JADVALI — haftalik ko'rinish, TIG'IZLIK va bo'sh oraliqlar bo'yicha TAVSIYALAR.
///
/// <para>⚠️ Bu controller jadvalni O'ZGARTIRMAYDI (POST/PUT yo'q): guruhning vaqti, xonasi va
/// o'qituvchisi avvalgidek guruh formasidan tahrirlanadi. Sabab: jadvalni avtomatik ko'chirish
/// maosh, jurnal va o'quvchi xabarlariga tegib ketardi — qaror odamniki bo'lib qolsin.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule.timetable")]
[Route("api/admin/schedule")]
public class ScheduleController(ScheduleService schedule) : ControllerBase
{
    /// <summary>Haftalik jadval: barcha darslar, xonalar/o'qituvchilar ro'yxati va tig'izlik.</summary>
    [HttpGet]
    public Task<ScheduleBoardDto> GetBoard() => schedule.GetBoardAsync();

    /// <summary>
    /// BOSH SAHIFA jadval to'ri: har guruh bitta qator (kunlari bilan), barcha faol xonalar va
    /// o'qituvchilar. Kun/rejim/filtrlar klientda qo'llanadi (bitta so'rov — kun almashtirish
    /// serverga bormaydi).
    /// </summary>
    [HttpGet("grid")]
    public Task<ScheduleGridDto> GetGrid() => schedule.GetGridAsync();

    /// <summary>
    /// Bir kunlik jadvalni Excel (.xlsx) ga eksport: "Jadval" (vaqt × xona/o'qituvchi) va
    /// "Ro'yxat" varaqlari. Filtrlar bosh sahifadagi bilan AYNAN bir xil
    /// (<see cref="ScheduleGridExport"/>); noto'g'ri qiymat standartga tushadi.
    /// </summary>
    /// <param name="day">0=Dushanba … 6=Yakshanba (bo'sh — bugun).</param>
    /// <param name="groupBy">"room" (standart) yoki "teacher".</param>
    /// <param name="fromHour">"HH:mm", standart 08:00.</param>
    /// <param name="toHour">"HH:mm", standart 22:00.</param>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] int? day = null,
        [FromQuery] string? groupBy = null,
        [FromQuery] string? fromHour = null,
        [FromQuery] string? toHour = null,
        [FromQuery] string? courseId = null,
        [FromQuery] string? teacherId = null,
        [FromQuery] string? roomId = null,
        [FromQuery] string? groupId = null,
        [FromQuery] string? status = null)
    {
        var today = AppClock.Today;
        var filter = ScheduleGridExport.MakeFilter(
            day, groupBy, fromHour, toHour, courseId, teacherId, roomId,
            todayDay: ((int)today.DayOfWeek + 6) % 7, groupId, status);
        var grid = await schedule.GetGridAsync();
        var bytes = ExcelExport.Build(ScheduleGridExport.Sheets(grid, filter));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"dars_jadvali_{today:yyyy-MM-dd}_{filter.Day + 1}.xlsx");
    }

    /// <summary>
    /// Ikki dars orasida qolib ketgan BO'SH oraliqlar va ularni to'ldirish tavsiyalari.
    /// </summary>
    /// <param name="scope">"room" (standart) — xona bekor turibdi; "teacher" — o'qituvchi kutyapti.</param>
    /// <param name="ownerId">Bitta xona/o'qituvchi kerak bo'lsa; bo'sh = hammasi.</param>
    /// <param name="minMinutes">Shundan qisqa oraliq hisobga olinmaydi (standart 60, 15..480).</param>
    [HttpGet("gaps")]
    public Task<List<ScheduleGapDto>> GetGaps(
        [FromQuery] string scope = "room",
        [FromQuery] string? ownerId = null,
        [FromQuery] int? minMinutes = null)
        // Noma'lum qiymat "room" ga tushadi — klientdagi xato kalit tufayli ro'yxat
        // butunlay bo'shab qolmasin (`contacts` jurnalidagi bilan bir xil qoida).
        => schedule.GetGapsAsync(scope == "teacher" ? "teacher" : "room", ownerId, minMinutes);
}
