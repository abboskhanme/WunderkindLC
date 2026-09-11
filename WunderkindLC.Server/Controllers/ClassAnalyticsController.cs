using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin,staff")]
public class ClassAnalyticsController(AppDbContext db, DataCache dataCache) : ControllerBase
{
    private async Task<(List<Student>, List<Subject>, List<StudentGroup>)> LoadCommon() =>
        (await db.Students.AsNoTracking().ToListAsync(),
         await db.Subjects.AsNoTracking().ToListAsync(),
         await db.StudentGroups.AsNoTracking().ToListAsync());

    private async Task<Analytics.ClassResult> BuildFor(Group cls, List<Student> students, List<Subject> subjects, List<StudentGroup> memberships)
    {
        var assignedSubjectIds = string.IsNullOrEmpty(cls.CourseId)
            ? new List<string>() : new List<string> { cls.CourseId };
        var entries = await db.JournalEntries.AsNoTracking().Where(e => e.ClassId == cls.Id).ToListAsync();
        var notes = await db.LessonNotes.AsNoTracking().Where(n => n.ClassId == cls.Id).ToListAsync();
        var lateIds = await db.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync();
        // Guruh a'zolari = M2M StudentGroup (a'zolar oynasidagi manba). Faol a'zolik = roster;
        // a'zolik yozuvi umuman bo'lmagan eski ClassName-o'quvchilari fallback bilan qo'shiladi.
        var groupMs = memberships.Where(m => m.GroupId == cls.Id).ToList();
        var activeIds = groupMs.Where(m => m.IsActive).Select(m => m.StudentId).ToHashSet();
        var anyIds = groupMs.Select(m => m.StudentId).ToHashSet();
        return Analytics.BuildClass(cls, students, subjects, assignedSubjectIds, entries, notes,
            lateReasonIds: lateIds, activeMemberIds: activeIds, anyMemberIds: anyIds);
    }

    [HttpGet("api/admin/classes/{classId}/performance")]
    public async Task<ActionResult<ClassPerformanceDataDto>> Performance(string classId)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return new ClassPerformanceDataDto([], []);
        var (students, subjects, memberships) = await LoadCommon();
        var res = await BuildFor(cls, students, subjects, memberships);
        return new ClassPerformanceDataDto(res.Subjects, res.Rows);
    }

    [HttpGet("api/admin/classes/stats")]
    public async Task<ActionResult<Dictionary<string, ClassStatsDto>>> Stats() =>
        // Butun natijani keshlaymiz — bog'liq jadvallar o'zgarsa (interceptor) avtomatik yangilanadi.
        await dataCache.GetOrCreateAsync(
            "classes:stats",
            new[]
            {
                nameof(JournalEntry), nameof(LessonNote), nameof(Student),
                nameof(StudentGroup), nameof(Group), nameof(Subject), nameof(AbsenceReason),
            },
            TimeSpan.FromMinutes(15),
            ComputeStatsAsync);

    /// <summary>Har guruh statistikasini hisoblaydi. N+1 ni yo'qotish uchun jurnal yozuvlari va dars
    /// izohlari GURUHLAR ro'yxati bo'yicha BIR so'rovda olinib, Dictionary'ga guruhlanadi (ilgari har
    /// guruh uchun 3 alohida so'rov ketardi). Semantika o'zgarmaydi.</summary>
    private static async Task<Dictionary<string, ClassStatsDto>> ComputeStatsAsync(IAppDbContext db2)
    {
        var students = await db2.Students.AsNoTracking().ToListAsync();
        var subjects = await db2.Subjects.AsNoTracking().ToListAsync();
        var memberships = await db2.StudentGroups.AsNoTracking().ToListAsync();
        var classes = await db2.Classes.AsNoTracking().ToListAsync();
        var classIds = classes.Select(c => c.Id).ToList();

        var entriesByClass = (await db2.JournalEntries.AsNoTracking()
                .Where(e => classIds.Contains(e.ClassId)).ToListAsync())
            .GroupBy(e => e.ClassId).ToDictionary(g => g.Key, g => g.ToList());
        var notesByClass = (await db2.LessonNotes.AsNoTracking()
                .Where(n => classIds.Contains(n.ClassId)).ToListAsync())
            .GroupBy(n => n.ClassId).ToDictionary(g => g.Key, g => g.ToList());
        var lateIds = await db2.AbsenceReasons.AsNoTracking().Where(r => r.IsLate).Select(r => r.Id).ToListAsync();

        var result = new Dictionary<string, ClassStatsDto>();
        foreach (var cls in classes)
        {
            var assignedSubjectIds = string.IsNullOrEmpty(cls.CourseId)
                ? new List<string>() : new List<string> { cls.CourseId };
            var entries = entriesByClass.TryGetValue(cls.Id, out var es) ? es : new List<JournalEntry>();
            var notes = notesByClass.TryGetValue(cls.Id, out var ns) ? ns : new List<LessonNote>();
            var groupMs = memberships.Where(m => m.GroupId == cls.Id).ToList();
            var activeIds = groupMs.Where(m => m.IsActive).Select(m => m.StudentId).ToHashSet();
            var anyIds = groupMs.Select(m => m.StudentId).ToHashSet();

            var rows = Analytics.BuildClass(cls, students, subjects, assignedSubjectIds, entries, notes,
                lateReasonIds: lateIds, activeMemberIds: activeIds, anyMemberIds: anyIds).Rows;
            var n = rows.Count;
            var att = rows.Where(r => r.Attendance.HasValue).Select(r => r.Attendance!.Value).ToList();
            result[cls.Id] = new ClassStatsDto(
                n,
                n > 0 ? Math.Round(rows.Average(r => r.Average), 1) : 0,
                att.Count > 0 ? Math.Round(att.Average()) : null);
        }
        return result;
    }

    [HttpGet("api/admin/students/rating")]
    public async Task<ActionResult<IEnumerable<StudentRatingRowDto>>> Rating() =>
        // Reyting butun markaz bo'yicha og'ir hisob — keshlaymiz; bog'liq jadval o'zgarsa avto-yangilanadi.
        await dataCache.GetOrCreateAsync(
            "rating:school",
            new[]
            {
                nameof(JournalEntry), nameof(LessonNote), nameof(Student),
                nameof(StudentGroup), nameof(Group), nameof(AbsenceReason), nameof(CriterionGrade),
                // Qo'lda tuzatish ballni darhol o'zgartiradi — bo'lmasa 15 daqiqagacha ko'rinmasdi.
                nameof(StudentBallAdjustment),
            },
            TimeSpan.FromMinutes(15),
            db2 => RatingService.SchoolAsync(db2));

    /// <summary>
    /// Markaz bo'yicha o'quvchilar BALLI (jurnal baholari + bajarilgan baholash mezonlari) —
    /// admin "O'quvchilar" ro'yxatidagi "Ball" ustuni va saralash uchun. Keshlangan.
    /// </summary>
    [HttpGet("api/admin/students/balls")]
    public async Task<ActionResult<IEnumerable<StudentBallDto>>> Balls() =>
        await dataCache.GetOrCreateAsync(
            "ball:students",
            // O'rtacha ball (AvgBall) endi A'ZOLIKKA ham bog'liq — StudentGroup/Group ham deps'da.
            new[]
            {
                nameof(JournalEntry), nameof(CriterionGrade), nameof(Student),
                nameof(StudentGroup), nameof(Group), nameof(StudentBallAdjustment),
            },
            TimeSpan.FromMinutes(10),
            db2 => StudentBallService.SchoolAsync(db2));
}
