using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using Microsoft.EntityFrameworkCore;
using Slot = WunderkindLC.Application.Services.ScheduleRules.Slot;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DARS JADVALI — haftalik ko'rinish, BO'SH ORALIQLAR va ularni to'ldirish TAVSIYALARI.
///
/// <para>Hisob-kitob qoidalari <see cref="ScheduleRules"/> da (sof funksiyalar, testlangan);
/// bu yerda faqat bazadan o'qish va tavsiyalarni saralash.</para>
///
/// <para>⚠️ Bu servis JADVALNI O'ZGARTIRMAYDI — u faqat TAVSIYA beradi. Guruhning vaqti/xonasi/
/// o'qituvchisi avvalgidek guruh formasidan tahrirlanadi (u yerda <see cref="RoomConflictService"/>
/// to'qnashuvni tekshiradi). Sabab: jadvalni avtomatik ko'chirish pul (maosh), jurnal va
/// o'quvchi xabarlariga tegib ketardi — qaror odamniki bo'lib qolsin.</para>
/// </summary>
public class ScheduleService(IAppDbContext db)
{
    /// <summary>Tavsiyalar soni — bitta bo'sh oraliq uchun. Uzun ro'yxat tanlashni qiyinlashtiradi.</summary>
    public const int MaxSuggestions = 5;

    /// <summary>Jadvalga tushadigan bitta dars (guruh + kun).</summary>
    private sealed record Lesson(
        string GroupId, string GroupName, string TeacherId, string TeacherName,
        string RoomId, string RoomName, string CourseName, Slot Slot,
        string CourseId = "");

    /// <summary>Haftalik jadval + tig'izlik. Bitta so'rovda sahifaning hamma ma'lumoti.</summary>
    public async Task<ScheduleBoardDto> GetBoardAsync()
    {
        var (lessons, skipped) = await LoadLessonsAsync();

        var rooms = lessons
            .Where(l => l.RoomId.Length > 0)
            .GroupBy(l => l.RoomId)
            .Select(g => new ScheduleOwnerDto(g.Key, g.First().RoomName, g.Select(x => x.GroupId).Distinct().Count()))
            .OrderBy(r => r.Name)
            .ToList();

        var teachers = lessons
            .Where(l => l.TeacherId.Length > 0)
            .GroupBy(l => l.TeacherId)
            .Select(g => new ScheduleOwnerDto(g.Key, g.First().TeacherName, g.Select(x => x.GroupId).Distinct().Count()))
            .OrderBy(t => t.Name)
            .ToList();

        var histogram = ScheduleRules.BusyHistogram(lessons.Select(l => l.Slot));
        var peak = histogram
            .OrderBy(kv => kv.Key.Day).ThenBy(kv => kv.Key.Bucket)
            .Select(kv => new SchedulePeakDto(kv.Key.Day, ScheduleRules.FormatTime(kv.Key.Bucket), kv.Value))
            .ToList();

        return new ScheduleBoardDto(
            Lessons: lessons.Select(ToDto).OrderBy(l => l.Day).ThenBy(l => l.Start).ToList(),
            Rooms: rooms,
            Teachers: teachers,
            Peak: peak,
            SkippedGroups: skipped);
    }

    /// <summary>
    /// BOSH SAHIFA jadval to'ri: har GURUH bitta qator (barcha dars kunlari bilan), BARCHA faol
    /// xonalar (dars yo'q xona ham ustun bo'lib chiqsin — bo'sh xona ko'rinishi kerak) va
    /// o'qituvchilar. Vaqti buzuq guruhlar <see cref="LoadLessonsAsync"/> dagi kabi tashlanadi,
    /// lekin SONI qaytariladi.
    /// </summary>
    public async Task<ScheduleGridDto> GetGridAsync()
    {
        var (lessons, skipped) = await LoadLessonsAsync();
        var groupIds = lessons.Select(l => l.GroupId).Distinct().ToList();

        // A'zolar — sig'im bilan BIR XIL ta'rif: o'rin band qilganlar (faol + sinov, muzlatilgan emas).
        var members = await db.StudentGroups
            .Where(MembershipLifecycle.OccupiesSeatExpr)
            .Where(sg => groupIds.Contains(sg.GroupId))
            .GroupBy(sg => sg.GroupId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

        var info = await db.Classes
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Capacity, g.Status, g.IsBlocked, g.StartDate, g.EndDate })
            .ToDictionaryAsync(g => g.Id);

        // O'TILGAN darslar — jurnalda "dars o'tildi" belgilangan (kun, para) juftliklari.
        var done = (await db.LessonNotes
                .Where(n => n.Conducted && groupIds.Contains(n.ClassId))
                .Select(n => new { n.ClassId, n.Date, n.Period })
                .Distinct()
                .ToListAsync())
            .GroupBy(n => n.ClassId)
            .ToDictionary(g => g.Key, g => g.Count());

        var groups = lessons
            .GroupBy(l => l.GroupId)
            .Select(g =>
            {
                var l = g.First();
                var days = g.Select(x => x.Slot.Day).Distinct().Order().ToList();
                var i = info.GetValueOrDefault(l.GroupId);
                return new ScheduleGridGroupDto(
                    l.GroupId, l.GroupName, l.CourseId, l.CourseName,
                    l.TeacherId, l.TeacherName, l.RoomId, l.RoomName,
                    days,
                    ScheduleRules.FormatTime(l.Slot.StartMin), ScheduleRules.FormatTime(l.Slot.EndMin),
                    l.Slot.EndMin - l.Slot.StartMin,
                    members.GetValueOrDefault(l.GroupId), i?.Capacity ?? 0,
                    Status: i is null ? "active" : i.IsBlocked ? "blocked" : (i.Status ?? "active"),
                    StartDate: i?.StartDate ?? "",
                    EndDate: i?.EndDate ?? "",
                    LessonsDone: done.GetValueOrDefault(l.GroupId),
                    LessonsTotal: ScheduleGridExport.PlannedLessons(days, i?.StartDate, i?.EndDate));
            })
            .OrderBy(g => g.Start).ThenBy(g => g.GroupName)
            .ToList();

        var groupsByRoom = groups
            .GroupBy(g => ScheduleGridExport.RoomKey(g.RoomId, g.RoomName))
            .ToDictionary(g => g.Key, g => g.Count());
        var rooms = (await db.Rooms.Where(r => r.IsActive).Select(r => new { r.Id, r.Name }).ToListAsync())
            .Select(r => new ScheduleOwnerDto(r.Id, r.Name, groupsByRoom.GetValueOrDefault(r.Id)))
            .ToList();
        // Faol ro'yxatda yo'q, lekin darsi bor xonalar (o'chirilgan xona yoki eski matnli nom) ham
        // ustun bo'lsin — aks holda o'sha darslar to'rda JIMGINA yo'qolardi.
        var known = rooms.Select(r => r.Id).ToHashSet();
        foreach (var g in groups)
        {
            var key = ScheduleGridExport.RoomKey(g.RoomId, g.RoomName);
            if (key.Length == 0 || !known.Add(key)) continue;
            rooms.Add(new ScheduleOwnerDto(key, g.RoomName.Length > 0 ? g.RoomName : key, groupsByRoom[key]));
        }

        // O'qituvchilar — BARCHA arxivlanmaganlar (o'qituvchi rejimida darsi yo'q o'qituvchi ham
        // ustun: kim bo'sh ekani ko'rinsin) + darsi bor, lekin arxivlangan o'qituvchi (darsi yo'qolmasin).
        var groupsByTeacher = groups
            .Where(g => g.TeacherId.Length > 0)
            .GroupBy(g => g.TeacherId)
            .ToDictionary(g => g.Key, g => (Name: g.First().TeacherName, Count: g.Count()));
        var teachers = (await db.Teachers.Where(t => !t.IsArchived).Select(t => new { t.Id, t.FullName }).ToListAsync())
            .Select(t => new ScheduleOwnerDto(t.Id, t.FullName, groupsByTeacher.GetValueOrDefault(t.Id).Count))
            .ToList();
        var knownTeachers = teachers.Select(t => t.Id).ToHashSet();
        foreach (var (id, v) in groupsByTeacher)
            if (knownTeachers.Add(id)) teachers.Add(new ScheduleOwnerDto(id, v.Name, v.Count));

        return new ScheduleGridDto(
            groups,
            rooms.OrderBy(r => r.Name, ScheduleGridExport.NaturalComparer).ToList(),
            teachers.OrderBy(t => t.Name, ScheduleGridExport.NaturalComparer).ToList(),
            skipped);
    }

    /// <summary>
    /// BO'SH ORALIQLAR va ularni to'ldirish tavsiyalari.
    /// </summary>
    /// <param name="scope">"room" — xona kesimida (asosiy savol: xona bekor turibdi),
    /// "teacher" — o'qituvchi kesimida (o'qituvchi markazda kutib o'tiribdi).</param>
    /// <param name="ownerId">Faqat bitta xona/o'qituvchi kerak bo'lsa; bo'sh = hammasi.</param>
    /// <param name="minMinutes">Shundan qisqa oraliq "teshik" hisoblanmaydi.</param>
    public async Task<List<ScheduleGapDto>> GetGapsAsync(string scope, string? ownerId, int? minMinutes)
    {
        var byTeacher = scope == "teacher";
        var min = ScheduleRules.ClampGapMinutes(minMinutes);
        var (lessons, _) = await LoadLessonsAsync();

        var histogram = ScheduleRules.BusyHistogram(lessons.Select(l => l.Slot));
        // O'qituvchining BUTUN bandligi (qaysi xonada ekani muhim emas) — tavsiya shundan.
        var teacherBusy = lessons
            .Where(l => l.TeacherId.Length > 0)
            .GroupBy(l => l.TeacherId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Slot).ToList());
        var roomBusy = lessons
            .Where(l => l.RoomId.Length > 0)
            .GroupBy(l => l.RoomId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Slot).ToList());

        var teacherNames = lessons
            .Where(l => l.TeacherId.Length > 0)
            .GroupBy(l => l.TeacherId)
            .ToDictionary(g => g.Key, g => g.First().TeacherName);
        var roomNames = lessons
            .Where(l => l.RoomId.Length > 0)
            .GroupBy(l => l.RoomId)
            .ToDictionary(g => g.Key, g => g.First().RoomName);

        // O'qituvchining haftalik yuki (daqiqa) — "kimda bo'sh quvvat bor" uchun.
        var teacherLoad = teacherBusy.ToDictionary(
            kv => kv.Key, kv => kv.Value.Sum(s => s.EndMin - s.StartMin));

        // Jadvalda umuman darsi yo'q o'qituvchilar ham tavsiyaga kirishi kerak — ular ENG bo'sh.
        var idleTeachers = await db.Teachers
            .Where(t => !t.IsArchived && !teacherBusy.Keys.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName })
            .ToListAsync();
        foreach (var t in idleTeachers)
        {
            teacherBusy[t.Id] = [];
            teacherNames[t.Id] = t.FullName;
            teacherLoad[t.Id] = 0;
        }

        var owners = byTeacher ? teacherBusy : roomBusy;
        var names = byTeacher ? teacherNames : roomNames;

        var result = new List<ScheduleGapDto>();
        foreach (var (id, slots) in owners)
        {
            if (!string.IsNullOrWhiteSpace(ownerId) && id != ownerId) continue;
            // Darsi umuman yo'q o'qituvchida "orada qolgan teshik" tushunchasi yo'q.
            if (slots.Count < 2) continue;

            foreach (var gap in ScheduleRules.FindGaps(slots, min))
            {
                var before = lessons
                    .Where(l => (byTeacher ? l.TeacherId : l.RoomId) == id
                                && l.Slot.Day == gap.Day && l.Slot.EndMin == gap.StartMin)
                    .Select(l => l.GroupName).FirstOrDefault() ?? "";
                var after = lessons
                    .Where(l => (byTeacher ? l.TeacherId : l.RoomId) == id
                                && l.Slot.Day == gap.Day && l.Slot.StartMin == gap.EndMin)
                    .Select(l => l.GroupName).FirstOrDefault() ?? "";

                var suggestions = byTeacher
                    ? SuggestRooms(roomBusy, roomNames, gap)
                    : SuggestTeachers(teacherBusy, teacherNames, teacherLoad, gap);

                result.Add(new ScheduleGapDto(
                    Scope: byTeacher ? "teacher" : "room",
                    OwnerId: id,
                    OwnerName: names.GetValueOrDefault(id, id),
                    Day: gap.Day,
                    DayLabel: ScheduleRules.DayShort(gap.Day),
                    Start: ScheduleRules.FormatTime(gap.StartMin),
                    End: ScheduleRules.FormatTime(gap.EndMin),
                    Minutes: gap.Minutes,
                    BeforeGroup: before,
                    AfterGroup: after,
                    PeakScore: ScheduleRules.PeakInRange(histogram, gap.Day, gap.StartMin, gap.EndMin),
                    Suggestions: suggestions));
            }
        }

        // ENG TIG'IZ vaqtdagi teshik tepada: aynan o'sha payt o'quvchi kela oladi, ya'ni
        // bo'sh turgan xona/o'qituvchining "narxi" eng baland. Keyin uzun teshiklar.
        return result
            .OrderByDescending(g => g.PeakScore)
            .ThenByDescending(g => g.Minutes)
            .ThenBy(g => g.Day)
            .ThenBy(g => g.Start)
            .ToList();
    }

    /// <summary>XONA teshigi uchun: shu paytda BO'SH o'qituvchilar.</summary>
    private static List<ScheduleSuggestionDto> SuggestTeachers(
        Dictionary<string, List<Slot>> teacherBusy,
        Dictionary<string, string> names,
        Dictionary<string, int> load,
        ScheduleRules.Gap gap)
    {
        var list = new List<(ScheduleSuggestionDto Dto, int Rank, int Load)>();
        foreach (var (teacherId, slots) in teacherBusy)
        {
            if (!ScheduleRules.IsFree(slots, gap.Day, gap.StartMin, gap.EndMin)) continue;

            var touches = ScheduleRules.TouchesLesson(slots, gap.Day, gap.StartMin, gap.EndMin);
            var sameDay = slots.Any(s => s.Day == gap.Day);
            // Saralash: (1) darsiga TEGIB turadi — kuni uzluksiz bo'ladi; (2) shu kuni markazda
            // baribir bor; (3) umuman bo'sh — qo'shimcha qatnov kerak, lekin quvvati bor.
            var rank = touches ? 0 : sameDay ? 1 : 2;
            var note = touches
                ? "Shu kuni yonma-yon darsi bor — kuni uzluksiz bo'ladi"
                : sameDay
                    ? "Shu kuni markazda darsi bor"
                    : slots.Count == 0
                        ? "Jadvalda darsi yo'q — to'liq bo'sh"
                        : "Bu kuni bo'sh";
            list.Add((
                new ScheduleSuggestionDto("teacher", teacherId, names.GetValueOrDefault(teacherId, teacherId),
                    note, WeeklyMinutes: load.GetValueOrDefault(teacherId)),
                rank,
                load.GetValueOrDefault(teacherId)));
        }
        return list
            .OrderBy(x => x.Rank).ThenBy(x => x.Load).ThenBy(x => x.Dto.Name)
            .Take(MaxSuggestions).Select(x => x.Dto).ToList();
    }

    /// <summary>O'QITUVCHI teshigi uchun: shu paytda BO'SH xonalar.</summary>
    private static List<ScheduleSuggestionDto> SuggestRooms(
        Dictionary<string, List<Slot>> roomBusy,
        Dictionary<string, string> names,
        ScheduleRules.Gap gap)
    {
        var list = new List<(ScheduleSuggestionDto Dto, int Load)>();
        foreach (var (roomId, slots) in roomBusy)
        {
            if (!ScheduleRules.IsFree(slots, gap.Day, gap.StartMin, gap.EndMin)) continue;
            var load = slots.Sum(s => s.EndMin - s.StartMin);
            list.Add((
                new ScheduleSuggestionDto("room", roomId, names.GetValueOrDefault(roomId, roomId),
                    "Shu paytda bo'sh", WeeklyMinutes: load),
                load));
        }
        // Eng kam bandi tepada — jadval xonalar orasida tekis taqsimlansin.
        return list.OrderBy(x => x.Load).ThenBy(x => x.Dto.Name)
            .Take(MaxSuggestions).Select(x => x.Dto).ToList();
    }

    /// <summary>Arxivlanmagan guruhlardan haftalik darslar ro'yxati. Vaqti buzuq guruhlar
    /// tashlanadi, lekin SONI qaytariladi — jimgina yo'qolib ketmasin.</summary>
    private async Task<(List<Lesson> Lessons, int Skipped)> LoadLessonsAsync()
    {
        var groups = await db.Classes
            .Where(g => !g.IsArchived)
            .Select(g => new
            {
                g.Id, g.Name, g.TeacherId, g.RoomId, g.Room, g.CourseId, g.Days, g.StartTime, g.EndTime,
            })
            .ToListAsync();

        var teacherNames = await db.Teachers
            .Select(t => new { t.Id, t.FullName })
            .ToDictionaryAsync(t => t.Id, t => t.FullName);
        var roomNames = await db.Rooms
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name);
        var courseNames = await db.Subjects
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var lessons = new List<Lesson>();
        var skipped = 0;
        foreach (var g in groups)
        {
            var days = g.Days ?? [];
            if (days.Count == 0)
            {
                skipped++;
                continue;
            }
            var any = false;
            foreach (var day in days.Distinct())
            {
                var slot = ScheduleRules.MakeSlot(day, g.StartTime, g.EndTime);
                if (slot is null) continue;
                any = true;
                // Xona FK bo'lmasa eski matnli nomga tushamiz — u ham jadvalda ko'rinsin.
                var roomId = g.RoomId ?? "";
                var roomName = roomId.Length > 0
                    ? roomNames.GetValueOrDefault(roomId, g.Room ?? "")
                    : (g.Room ?? "");
                lessons.Add(new Lesson(
                    g.Id, g.Name,
                    g.TeacherId ?? "", teacherNames.GetValueOrDefault(g.TeacherId ?? "", ""),
                    roomId, roomName,
                    courseNames.GetValueOrDefault(g.CourseId ?? "", ""),
                    slot.Value,
                    g.CourseId ?? ""));
            }
            if (!any) skipped++;
        }
        return (lessons, skipped);
    }

    private static ScheduleLessonDto ToDto(Lesson l) => new(
        l.GroupId, l.GroupName, l.TeacherId, l.TeacherName, l.RoomId, l.RoomName, l.CourseName,
        l.Slot.Day, ScheduleRules.DayShort(l.Slot.Day),
        ScheduleRules.FormatTime(l.Slot.StartMin), ScheduleRules.FormatTime(l.Slot.EndMin),
        l.Slot.EndMin - l.Slot.StartMin);
}
