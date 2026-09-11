using WunderkindLC.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Har bir xona uchun bandlik, o'quvchi soni va samaradorlik metrikalari.
/// Endpoint: GET /api/admin/rooms/utilization-dashboard
/// </summary>
public class RoomUtilizationService(IAppDbContext db)
{
    public class RoomUtilizationMetric
    {
        public string RoomId { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public int Capacity { get; set; }
        /// <summary>Faol (active/trial, frozen emas) o'quvchilar yig'indisi (per-guruh, unique emas).</summary>
        public int CurrentStudents { get; set; }
        /// <summary>Jami slotlar: Capacity x GroupCount.</summary>
        public int TotalSlots { get; set; }
        /// <summary>Bo'sh slotlar: TotalSlots - CurrentStudents (min 0).</summary>
        public int Gap { get; set; }
        /// <summary>Bandlik foizi: CurrentStudents / TotalSlots * 100.</summary>
        public double OccupancyPercent { get; set; }
        /// <summary>Xonaga biriktirilgan arxivlanmagan guruhlar soni.</summary>
        public int ActiveGroupCount { get; set; }
        /// <summary>Haftalik aktiv soatlar: guruh Days soni * dars davomiyligi soat.</summary>
        public double WeeklyActiveHours { get; set; }
        /// <summary>Haftalik bandlik foizi: weeklyActiveHours / (6 * 14) * 100. Max 100.</summary>
        public double WeeklyUtilizationPercent { get; set; }
        /// <summary>Samaradorlik ball 0-100: bandlik (60%) + haftalik bandlik (40%).</summary>
        public int EfficiencyScore { get; set; }
        /// <summary>"Optimal" | "To'lib toshgan" | "Kam to'lgan" | "Bo'sh"</summary>
        public string EfficiencyStatus { get; set; } = string.Empty;
        public string Building { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        /// <summary>Xonaga biriktirilgan faol guruh nomlari (tezkor ko'rish uchun).</summary>
        public List<string> GroupNames { get; set; } = new();
    }

    public async Task<List<RoomUtilizationMetric>> GetRoomUtilizationAsync()
    {
        var rooms = await db.Rooms
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync();

        // Barcha arxivlanmagan guruhlarni tortib olamiz
        // Guruh xonasi: avval RoomId (FK), bo'lmasa Room (matnli) — ikkalasini ham qo'llab-quvvatlaydi
        var allGroups = await db.Classes
            .Where(g => !g.IsArchived && (
                (g.RoomId != null && g.RoomId != "") ||
                (g.Room != null && g.Room != "")))
            .ToListAsync();

        // Faol a'zoliklar: StudentId + GroupId (frozen emas)
        var activeMembers = await db.StudentGroups
            .Where(sg => sg.IsActive && sg.Status != "frozen")
            .Select(sg => new { sg.GroupId, sg.StudentId })
            .ToListAsync();

        // Xona nomi → Id xaritasi (matnli Room field uchun).
        // DIQQAT: Room.Name unique EMAS — bir xil nomli ikki xona bo'lsa ToDictionary crash berardi.
        // TryAdd bilan birinchisi g'olib (matnli Room moslashda ham birinchi xona tanlanadi).
        var roomByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rooms)
            roomByName.TryAdd(r.Name, r.Id);

        // Guruhlarni xona Id bo'yicha guruhlash (RoomId ustuvor; bo'lmasa Room nomi orqali)
        var groupsByRoom = allGroups
            .GroupBy(g => {
                if (!string.IsNullOrEmpty(g.RoomId)) return g.RoomId;
                if (!string.IsNullOrEmpty(g.Room) && roomByName.TryGetValue(g.Room, out var rid)) return rid;
                return null;
            })
            .Where(grp => grp.Key != null)
            .ToDictionary(g => g.Key!, g => g.ToList());

        var membersByGroup = activeMembers
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).ToHashSet());

        var metrics = new List<RoomUtilizationMetric>();

        foreach (var room in rooms)
        {
            var groups = groupsByRoom.GetValueOrDefault(room.Id, []);

            if (groups.Count == 0)
            {
                metrics.Add(new RoomUtilizationMetric
                {
                    RoomId = room.Id,
                    RoomName = room.Name,
                    Capacity = room.Capacity,
                    CurrentStudents = 0,
                    TotalSlots = 0,
                    Gap = 0,
                    OccupancyPercent = 0,
                    ActiveGroupCount = 0,
                    WeeklyActiveHours = 0,
                    WeeklyUtilizationPercent = 0,
                    EfficiencyScore = 0,
                    EfficiencyStatus = "Bo'sh",
                    Building = room.Building ?? "",
                    Location = room.Location ?? "",
                    GroupNames = [],
                });
                continue;
            }

            // Per-guruh o'quvchilar yig'indisi (bir o'quvchi bir nechta guruhda bo'lsa har guruhda sanaladi)
            int currentStudents = groups.Sum(g =>
                membersByGroup.TryGetValue(g.Id, out var students) ? students.Count : 0);

            int groupCount = groups.Count;
            int totalSlots = room.Capacity * groupCount;
            double occupancyPct = totalSlots > 0
                ? Math.Round((double)currentStudents / totalSlots * 100, 1)
                : 0;
            int gap = Math.Max(0, totalSlots - currentStudents);

            double weeklyHours = CalculateWeeklyActiveHours(groups);
            // 6 ish kuni, kuniga 14 soat (08:00–22:00)
            const double roomCapacityHoursPerWeek = 6.0 * 14.0;
            double weeklyPct = Math.Round(Math.Min(weeklyHours / roomCapacityHoursPerWeek * 100, 100), 1);

            int score = ComputeEfficiencyScore(occupancyPct, weeklyPct);

            string status = occupancyPct > 100 ? "To'lib toshgan"
                : occupancyPct < 30            ? "Kam to'lgan"
                : currentStudents == 0         ? "Bo'sh"
                :                                "Optimal";

            metrics.Add(new RoomUtilizationMetric
            {
                RoomId = room.Id,
                RoomName = room.Name,
                Capacity = room.Capacity,
                CurrentStudents = currentStudents,
                TotalSlots = totalSlots,
                Gap = gap,
                OccupancyPercent = occupancyPct,
                ActiveGroupCount = groups.Count,
                WeeklyActiveHours = Math.Round(weeklyHours, 2),
                WeeklyUtilizationPercent = weeklyPct,
                EfficiencyScore = score,
                EfficiencyStatus = status,
                Building = room.Building ?? "",
                Location = room.Location ?? "",
                GroupNames = groups.Select(g => g.Name).OrderBy(n => n).ToList(),
            });
        }

        return metrics.OrderByDescending(m => m.EfficiencyScore).ToList();
    }

    /// <summary>
    /// Bitta xona uchun sig'im samaradorligini hisoblaydi.
    /// Formula: TotalSlots = Capacity × GroupCount; Utilization = ActualStudents / TotalSlots * 100.
    /// Har guruh alohida slot beradi (bir o'quvchi bir nechta guruhda bo'lsa har guruhda 1 o'rin oladi).
    /// </summary>
    public async Task<WunderkindLC.Application.Dtos.RoomCapacityMetric?> GetRoomCapacityAsync(string roomId)
    {
        var room = await db.Rooms.FindAsync(roomId);
        if (room is null) return null;

        var groups = await db.Classes
            .Where(c => !c.IsArchived && (c.RoomId == roomId ||
                (c.RoomId == null && c.Room == room.Name)))
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToHashSet();

        var membersByGroup = await db.StudentGroups
            .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive && sg.Status != "frozen")
            .Select(sg => new { sg.GroupId, sg.StudentId })
            .ToListAsync();

        var memberLookup = membersByGroup
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).ToHashSet());

        // Kurs nomlari (guruh CourseId → Subject.Name)
        var courseIds = groups.Select(g => g.CourseId).Where(c => c != null).Distinct().ToList();
        var courseNames = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id!, s => s.Name);

        var groupSlots = groups.Select(g => {
            int count = memberLookup.TryGetValue(g.Id, out var set) ? set.Count : 0;
            string? course = g.CourseId != null && courseNames.TryGetValue(g.CourseId, out var cn) ? cn : null;
            return new WunderkindLC.Application.Dtos.RoomGroupSlotDto(g.Id, g.Name, count, course);
        }).ToList();

        int groupCount = groups.Count;
        int totalSlots = room.Capacity * groupCount;
        int actualStudents = groupSlots.Sum(g => g.StudentCount);
        double utilization = totalSlots > 0 ? Math.Round((double)actualStudents / totalSlots * 100, 1) : 0;
        int gap = Math.Max(0, totalSlots - actualStudents);

        string status = totalSlots == 0 ? "Empty"
            : utilization > 100 ? "Overcrowded"
            : utilization < 60  ? "Underutilized"
            :                     "Optimal";

        return new WunderkindLC.Application.Dtos.RoomCapacityMetric(
            roomId, room.Name, room.Capacity, groupCount,
            totalSlots, actualStudents, utilization, gap, status, groupSlots);
    }

    /// <summary>
    /// Bitta xona uchun unified metrika — karta va modal uchun bitta manba.
    /// OccupancyPercent = ActualStudents / Capacity * 100 (peak approximation, unique).
    /// UtilizationPercent = ActualStudents / TotalSlots * 100 (har guruh alohida slot).
    /// </summary>
    public async Task<WunderkindLC.Application.Dtos.RoomDetailMetricDto?> GetRoomDetailMetricAsync(string roomId)
    {
        var room = await db.Rooms.FindAsync(roomId);
        if (room is null) return null;

        var groups = await db.Classes
            .Where(c => !c.IsArchived && (c.RoomId == roomId ||
                (c.RoomId == null && c.Room == room.Name)))
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToHashSet();

        var membersByGroup = await db.StudentGroups
            .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive && sg.Status != "frozen")
            .Select(sg => new { sg.GroupId, sg.StudentId })
            .ToListAsync();

        var memberLookup = membersByGroup
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).ToHashSet());

        // Kurs nomlari
        var courseIds = groups.Select(g => g.CourseId).Where(c => c != null).Distinct().ToList();
        var courseNames = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id!, s => s.Name);

        // O'qituvchi ismlari
        var teacherIds = groups.Select(g => g.TeacherId).Where(t => t != null).Distinct().ToList();
        var teacherNames = await db.Teachers
            .Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName })
            .ToDictionaryAsync(t => t.Id, t => t.FullName);

        int groupCount = groups.Count;
        int totalSlots = room.Capacity * groupCount;

        // FIX: ActualStudents = barcha guruhlardagi o'quvchilar YIG'INDISI (unique emas)
        // Har guruh alohida slot beradi, shuning uchun bir o'quvchi bir nechta guruhda bo'lsa har guruhda sanaladi.
        int actualStudents = groups.Sum(g =>
            memberLookup.TryGetValue(g.Id, out var s) ? s.Count : 0);

        // OccupancyPercent = ActualStudents / TotalSlots (barcha guruhlar birlashtirilgan)
        double occupancyPercent = totalSlots > 0
            ? Math.Round((double)actualStudents / totalSlots * 100, 1)
            : 0;
        // UtilizationPercent = ActualStudents / (Capacity × GroupCount) — xuddi shu formula, alias
        double utilizationPercent = occupancyPercent;

        double weeklyHours = CalculateWeeklyActiveHours(groups);
        const double roomCapacityHoursPerWeek = 6.0 * 14.0;
        double weeklyPct = Math.Round(Math.Min(weeklyHours / roomCapacityHoursPerWeek * 100, 100), 1);

        int efficiencyScore = ComputeEfficiencyScore(occupancyPercent, weeklyPct);

        string status = groupCount == 0 ? "Bo'sh"
            : occupancyPercent > 100 ? "To'lib toshgan"
            : occupancyPercent < 30  ? "Kam to'lgan"
            :                          "Optimal";

        int gap = Math.Max(0, totalSlots - actualStudents);

        var groupDetails = groups.Select(g => {
            int count = memberLookup.TryGetValue(g.Id, out var s) ? s.Count : 0;
            string courseName = g.CourseId != null && courseNames.TryGetValue(g.CourseId, out var cn) ? cn : "";
            string teacherName = g.TeacherId != null && teacherNames.TryGetValue(g.TeacherId, out var tn) ? tn : "";
            double gpct = room.Capacity > 0 ? Math.Round((double)count / room.Capacity * 100, 1) : 0;
            string days = g.Days != null && g.Days.Count > 0
                ? string.Join("-", g.Days.Select(DayName))
                : "";
            string timeSlot = !string.IsNullOrEmpty(g.StartTime) && !string.IsNullOrEmpty(g.EndTime)
                ? $"{g.StartTime}-{g.EndTime}"
                : "";
            return new WunderkindLC.Application.Dtos.RoomGroupDetailDto(
                g.Id, g.Name, courseName, teacherName,
                count, room.Capacity, gpct, days, timeSlot);
        }).ToList();

        return new WunderkindLC.Application.Dtos.RoomDetailMetricDto(
            room.Id, room.Name, room.Building ?? "", room.Location ?? "",
            room.Capacity, groupCount, totalSlots, actualStudents,
            occupancyPercent, utilizationPercent, weeklyPct,
            Math.Round(weeklyHours, 2), efficiencyScore, status, gap, groupDetails);
    }

    // ---------- private helpers ----------

    private static double CalculateWeeklyActiveHours(List<WunderkindLC.Domain.Group> groups)
    {
        double total = 0;
        foreach (var g in groups)
        {
            if (g.Days is null || g.Days.Count == 0) continue;
            if (string.IsNullOrWhiteSpace(g.StartTime) || string.IsNullOrWhiteSpace(g.EndTime)) continue;

            int startMin = TimeToMinutes(g.StartTime);
            int endMin   = TimeToMinutes(g.EndTime);
            double durationHours = (endMin - startMin) / 60.0;
            if (durationHours <= 0) continue;

            total += g.Days.Count * durationHours;
        }
        return total;
    }

    private static string DayName(int day) =>
        day switch { 0 => "Du", 1 => "Se", 2 => "Ch", 3 => "Pa", 4 => "Jum", 5 => "Sha", 6 => "Yak", _ => "?" };

    private static int TimeToMinutes(string hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return 0;
        var parts = hhmm.Split(':');
        if (parts.Length < 2) return 0;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return 0;
        return h * 60 + m;
    }

    /// <summary>
    /// Samaradorlik ball hisoblash:
    ///   - occupancy < 30%  → base = 20 (kam ishlatilmoqda)
    ///   - occupancy 30–100% → base = 50 + (occupancy - 30) * 0.5  (max ~85 @ 100%)
    ///   - occupancy > 100% → base = 60 (to'lib ketgan — maqbul emas)
    /// Yakuniy: base * 0.6 + weeklyPct * 0.4  (clamp 0–100)
    /// </summary>
    private static int ComputeEfficiencyScore(double occupancyPct, double weeklyPct)
    {
        double base_ = occupancyPct > 100
            ? 60
            : occupancyPct < 30
                ? 20 + occupancyPct * 0.33   // 0→20, 30→30
                : 50 + (occupancyPct - 30) * 0.5; // 30→50, 100→85

        double score = base_ * 0.6 + weeklyPct * 0.4;
        return Math.Clamp((int)Math.Round(score), 0, 100);
    }
}
