using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quv xonalari (auditoriyalar) — yaratish, tahrirlash, o'chirish (soft delete).
/// Guruh Group.RoomId orqali xonaga bog'lanadi. "O'chirish" = nofaol qilish (yozuv qoladi); faol
/// guruhi bor xonani o'chirib bo'lmaydi (400, soni bilan). Har o'zgarish tarixga yoziladi ("Room").
/// </summary>
// "classes" — nav/route "Xonalar" ham shu perm bilan ochiladi (Guruhlar ostida); "settings" bo'lsa
// Guruhlarni boshqaradigan xodim xona qo'sha olmas edi (frontend/backend ruxsat kaliti mos kelmasdi).
[ApiController]
[Authorize]
[AdminPerm("classes.rooms")]
[Route("api/admin/rooms")]
public class RoomsController(AppDbContext db, RoomUtilizationService utilization, AuditService audit) : ControllerBase
{
    /// <summary>Tarixdagi tur — "Guruhlar" bo'limiga tushadi (<c>AuditSections</c>): xona ro'yxati,
    /// bandlik hisoboti va guruh formasi xonalarga tayanadi.</summary>
    private const string AuditRoom = "Room";

    private static object Snapshot(Room r) => new { r.Name, r.Capacity, r.Building, r.Location, r.IsActive };

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
        audit.Record(AuditRoom, room.Id, "create",
            $"Xona qo'shildi: {room.Name} (sig'im {room.Capacity})", after: Snapshot(room));
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
        // ⚠️ `IsActive` YUBORILMASA (null) — TEGILMAYDI. Ilgari maydon majburiy `bool` edi, klient uni
        // yubormasdi va u `false` bo'lib bog'lanardi: sig'imni tuzatish xonani ro'yxatdan, guruh
        // formasidan va bandlik hisobotidan yo'qotardi.
        var isActive = RoomRules.ResolveIsActive(room.IsActive, req.IsActive);
        // Nom noyobligi faqat FAOL xonalar orasida (nofaol xonaning nomi bo'shatilgan hisoblanadi).
        if (isActive && await db.Rooms.AnyAsync(r => r.IsActive && r.Id != id && r.Name.ToLower() == name.ToLower()))
            return BadRequest(new { message = "Bu nomli faol xona allaqachon mavjud" });
        if (room.IsActive && !isActive && await RoomRules.LiveGroupsAsync(db, id) is var live and > 0)
            return BadRequest(new { message = $"Xonaga {live} ta faol guruh biriktirilgan — avval guruhlarni boshqa xonaga o'tkazing" });

        var before = Snapshot(room);
        room.Name     = name;
        room.Capacity = req.Capacity;
        room.Building = req.Building?.Trim();
        room.Location = req.Location?.Trim();
        room.IsActive = isActive;

        audit.Record(AuditRoom, room.Id, "update", $"Xona tahrirlandi: {room.Name}",
            before: before, after: Snapshot(room));
        await db.SaveChangesAsync();
        return ToDto(room);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return NotFound();

        // Soft delete — xona yozuvi saqlanadi (guruhlarning o'tgan jadvali unga tayanadi). FAOL guruhi bor
        // xona o'chirilmaydi: aks holda guruh "ko'rinmas" xonada qolardi — guruh formasida xona bo'sh
        // ko'rinib, keyingi saqlashda jimgina tushib qolardi, to'qnashuv tekshiruvi ham chalg'irdi.
        var live = await RoomRules.LiveGroupsAsync(db, id);
        if (live > 0)
            return BadRequest(new { message = $"Xonaga {live} ta faol guruh biriktirilgan — avval guruhlarni boshqa xonaga o'tkazing" });
        if (!room.IsActive) return NoContent();

        audit.Record(AuditRoom, room.Id, "delete", $"Xona o'chirildi (nofaol qilindi): {room.Name}",
            before: Snapshot(room));
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
        RoomUtilizationService.RoomUtilizationMetric m) =>
        new(m.RoomId, m.RoomName, m.Capacity, m.CurrentStudents,
            m.TotalSlots, m.Gap, m.ActiveGroupCount,
            m.OccupancyPercent, m.ActiveGroupCount, m.WeeklyActiveHours,
            m.WeeklyUtilizationPercent, m.EfficiencyScore, m.EfficiencyStatus,
            m.Building, m.Location, m.GroupNames);
}
