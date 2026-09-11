using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'QUVCHILAR DAVOMATI (admin):
///   • <c>GET absent?date=</c> — shu kunda darsga kelmagan (va kechikkan) o'quvchilar ro'yxati,
///     telefon raqamlari bilan (ota-onaga darrov qo'ng'iroq qilish uchun).
///   • <c>GET journal?studentId=&amp;groupId=&amp;month=</c> — bitta o'quvchining guruh jurnalidagi
///     O'Z QATORI (faqat o'qish; baho qo'yish/tahrirlash yo'q).
///
/// Manba: jurnal yozuvlari (<see cref="JournalEntry"/>) + o'tilgan darslar (<see cref="LessonNote"/>).
/// "Kelmadi" = davomat sababi qo'yilgan va sabab <see cref="AbsenceReason.IsLate"/> EMAS
/// (kech kelgan o'quvchi darsda qatnashgan hisoblanadi).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.attendance")]
[Route("api/admin/student-attendance")]
public class StudentAttendanceController(AppDbContext db) : ControllerBase
{
    /// <summary>Berilgan kundagi kelmagan/kechikkan o'quvchilar (sana berilmasa — bugun).</summary>
    [HttpGet("absent")]
    public async Task<ActionResult<DailyAbsenceDto>> Absent([FromQuery] string? date)
    {
        var day = string.IsNullOrWhiteSpace(date)
            ? AppClock.Today.ToString("yyyy-MM-dd")
            : (DateOnly.TryParse(date, out var d) ? d.ToString("yyyy-MM-dd") : null);
        if (day is null) return BadRequest(new { message = "Sana noto'g'ri" });

        // Shu kunda "o'tildi" deb belgilangan darslar — davomat olingan guruhlar.
        var conductedGroupIds = await db.LessonNotes.AsNoTracking()
            .Where(n => n.Date == day && n.Conducted)
            .Select(n => n.ClassId).Distinct().ToListAsync();

        // Shu kundagi davomat sababi qo'yilgan yozuvlar (kelmadi yoki kechikdi).
        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.Date == day && e.ReasonId != null)
            .Select(e => new { e.StudentId, e.ClassId, e.ReasonId })
            .ToListAsync();

        var reasons = await db.AbsenceReasons.AsNoTracking().ToDictionaryAsync(r => r.Id);
        var groupIds = entries.Select(e => e.ClassId).Concat(conductedGroupIds).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking()
            .Where(c => groupIds.Contains(c.Id)).ToListAsync();
        var groupById = groups.ToDictionary(g => g.Id);

        var courseNames = (await db.Subjects.AsNoTracking().ToListAsync()).ToDictionary(s => s.Id, s => s.Name);
        var teacherNames = (await db.Teachers.AsNoTracking().ToListAsync()).ToDictionary(t => t.Id, t => t.FullName);

        var studentIds = entries.Select(e => e.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && !s.IsArchived).ToListAsync();
        var studentById = students.ToDictionary(s => s.Id);

        // Davomat olingan o'quvchilar soni (o'tilgan darsdagi faol a'zolar) — "N tadan M tasi kelmadi".
        var markedStudents = conductedGroupIds.Count == 0 ? 0 : await db.StudentGroups.AsNoTracking()
            .Where(sg => conductedGroupIds.Contains(sg.GroupId) && sg.IsActive)
            .Select(sg => sg.StudentId).Distinct().CountAsync();

        var rows = new List<AbsentStudentDto>();
        foreach (var e in entries)
        {
            if (!studentById.TryGetValue(e.StudentId, out var st)) continue;      // arxivlangan/o'chirilgan
            if (!reasons.TryGetValue(e.ReasonId!, out var reason)) continue;
            groupById.TryGetValue(e.ClassId, out var g);

            rows.Add(new AbsentStudentDto(
                st.Id, st.FullName, st.Phone,
                st.ParentFullName, st.ParentPhone, st.FatherPhone, st.MotherPhone,
                e.ClassId, g?.Name ?? "", g is null || string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
                g is null || string.IsNullOrEmpty(g.TeacherId) ? "" : teacherNames.GetValueOrDefault(g.TeacherId, ""),
                g?.StartTime ?? "", g?.EndTime ?? "", g?.Room ?? "",
                reason.Id, reason.Name, reason.Short, reason.IsLate));
        }

        rows = rows
            .OrderBy(r => r.IsLate)                                     // avval kelmaganlar
            .ThenBy(r => r.StartTime, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DailyAbsenceDto(
            day, conductedGroupIds.Count, markedStudents,
            rows.Count(r => !r.IsLate), rows.Count(r => r.IsLate), rows);
    }

    /// <summary>
    /// O'quvchining guruh jurnalidagi o'z qatori (oy bo'yicha, faqat o'qish uchun).
    /// Guruh berilmasa — birinchi faol guruhi; oy berilmasa — oxirgi (joriy) oy.
    ///
    /// <para><b>Mantiq bu yerda EMAS</b> — <see cref="StudentJournalBuilder.GroupMonthAsync"/> da.
    /// Sabab: AYNAN shu hisob (blocked / RecordedAt / "noma'lum dars" qoidalari) o'quvchi ilovasining
    /// «Umumiy statistika» ekraniga ham kerak (<c>GET /api/student/journal</c>). Nusxalansa ikki ekran
    /// bir xil o'quvchi uchun turli davomat foizini ko'rsatib qolardi.</para>
    /// </summary>
    [HttpGet("journal")]
    public async Task<ActionResult<StudentJournalDto>> Journal(
        [FromQuery] string studentId, [FromQuery] string? groupId, [FromQuery] string? month)
    {
        var dto = await StudentJournalBuilder.GroupMonthAsync(db, studentId, groupId, month);
        return dto is null ? NotFound() : dto;
    }
}
