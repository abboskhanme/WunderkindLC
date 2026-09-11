using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quv xonalari (auditoriyalar) — yaratish, tahrirlash, o'chirish (soft delete).
/// Guruh Group.RoomId orqali xonaga bog'lanadi; xona o'chirilsa GroupId SET NULL bo'ladi.
/// </summary>
// "classes" — nav/route "Xonalar" ham shu perm bilan ochiladi (Guruhlar ostida); "settings" bo'lsa
// Guruhlarni boshqaradigan xodim xona qo'sha olmas edi (frontend/backend ruxsat kaliti mos kelmasdi).
[ApiController]
[Authorize]
[AdminPerm("classes.rooms")]
[Route("api/admin/rooms")]
public class RoomsController(AppDbContext db, WunderkindLC.Application.Services.RoomUtilizationService utilization) : ControllerBase
{
    [HttpGet]
    public async Task<List<RoomDto>> GetAll() =>
        (await db.Rooms
            .OrderBy(r => r.Name)
            .ToListAsync())
        .Select(ToDto)
        .ToList();

    [HttpPost]
    public async Task<ActionResult<RoomDto>> Create(CreateRoomRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Xona nomi kerak" });
        if (req.Capacity < 1 || req.Capacity > 1000)
            return BadRequest(new { message = "Sig'im 1–1000 oralig'ida bo'lishi kerak" });

        var name = req.Name.Trim();
        if (await db.Rooms.AnyAsync(r => r.IsActive && r.Name.ToLower() == name.ToLower()))
            return BadRequest(new { message = "Bu nomli faol xona allaqachon mavjud" });

        var room = new Room
        {
            Name     = name,
            Capacity = req.Capacity,
            Building = req.Building?.Trim(),
            Location = req.Location?.Trim(),
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return ToDto(room);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RoomDto>> Update(string id, UpdateRoomRequest req)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Xona nomi kerak" });
        if (req.Capacity < 1 || req.Capacity > 1000)
            return BadRequest(new { message = "Sig'im 1–1000 oralig'ida bo'lishi kerak" });

        var name = req.Name.Trim();
        if (await db.Rooms.AnyAsync(r => r.IsActive && r.Id != id && r.Name.ToLower() == name.ToLower()))
            return BadRequest(new { message = "Bu nomli faol xona allaqachon mavjud" });

        room.Name     = name;
        room.Capacity = req.Capacity;
        room.Building = req.Building?.Trim();
        room.Location = req.Location?.Trim();
        room.IsActive = req.IsActive;

        await db.SaveChangesAsync();
        return ToDto(room);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        // Soft delete — xona ma'lumoti saqlanadi, guruhlarda SET NULL (FK)
        room.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Barcha faol xonalar uchun bandlik + samaradorlik metrikalari.
    /// Saralash: EfficiencyScore desc (eng samarali xonalar yuqorida).
    /// </summary>
    [HttpGet("utilization-dashboard")]
    public async Task<List<RoomUtilizationDto>> GetUtilizationDashboard()
    {
        var metrics = await utilization.GetRoomUtilizationAsync();
        return metrics.Select(ToUtilizationDto).ToList();
    }

    /// <summary>
    /// Bitta xona uchun batafsil samaradorlik metrikasi.
    /// </summary>
    [HttpGet("{id}/utilization")]
    public async Task<ActionResult<RoomUtilizationDto>> GetRoomUtilization(string id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        var metrics = await utilization.GetRoomUtilizationAsync();
        var metric = metrics.FirstOrDefault(m => m.RoomId == id);
        if (metric is null) return NotFound();

        return ToUtilizationDto(metric);
    }

    /// <summary>
    /// Bitta xona uchun sig'im samaradorlik metrikasi.
    /// TotalSlots = Capacity × GroupCount; Utilization = ActualStudents / TotalSlots * 100.
    /// </summary>
    [HttpGet("{id}/capacity")]
    public async Task<ActionResult<RoomCapacityMetric>> GetRoomCapacity(string id)
    {
        var metric = await utilization.GetRoomCapacityAsync(id);
        return metric is null ? NotFound() : metric;
    }

    /// <summary>
    /// Bitta xona uchun unified metrika — karta va modal bitta endpointdan.
    /// OccupancyPercent = unique o'quvchilar / sig'im * 100.
    /// UtilizationPercent = jami o'quvchi-slot / jami slotlar * 100.
    /// Groups — har guruh tafsiloti (o'quvchi soni, kunlar, vaqt, o'qituvchi).
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<RoomDetailMetricDto>> GetRoomDetail(string id)
    {
        var metric = await utilization.GetRoomDetailMetricAsync(id);
        return metric is null ? NotFound() : metric;
    }

    private static RoomDto ToDto(Room r) =>
        new(r.Id, r.Name, r.Capacity, r.Building, r.Location,
            r.IsActive, r.CreatedAt.ToString("o"));

    private static RoomUtilizationDto ToUtilizationDto(
        WunderkindLC.Application.Services.RoomUtilizationService.RoomUtilizationMetric m) =>
        new(m.RoomId, m.RoomName, m.Capacity, m.CurrentStudents,
            m.TotalSlots, m.Gap, m.ActiveGroupCount,
            m.OccupancyPercent, m.ActiveGroupCount, m.WeeklyActiveHours,
            m.WeeklyUtilizationPercent, m.EfficiencyScore, m.EfficiencyStatus,
            m.Building, m.Location, m.GroupNames);
}
