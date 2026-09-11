using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

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
