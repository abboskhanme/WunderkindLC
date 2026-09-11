using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KURSLAR ANALITIKASI (O'quv bo'limi → "Kurslar analitikasi").
///
/// <para>Savollar: qaysi kursga oyma-oy nechta o'quvchi KELDI va nechtasi KETDI, hozir qaysi kursda
/// nechta o'quvchi bor, nechtasi BIRDAN ORTIQ kursga qatnaydi va qaysi kurslar birga o'qiladi.</para>
///
/// <para>Butun hisob-kitob <see cref="CourseAnalytics"/> da (sof funksiyalar, testlangan) — bu
/// controller faqat ma'lumot yuklaydi va DTO yasaydi.</para>
///
/// <para>Natija <see cref="DataCache"/> da keshlanadi: bog'liq jadvallar (a'zolik/guruh/kurs)
/// o'zgarsa kesh AVTOMATIK yangilanadi (CacheInvalidationInterceptor), TTL faqat zaxira.</para>
///
/// <para>RUXSAT: <c>schedule</c> ("Kurslar" bo'limi). Javobda o'quvchi ISMLARI yo'q — faqat sonlar,
/// shuning uchun odatdagi o'qish qoidasi yetarli.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("schedule.analytics")]
[Route("api/admin/course-analytics")]
public class CourseAnalyticsController(DataCache dataCache) : ControllerBase
{
    /// <summary>Standart va eng katta ko'riladigan oylar soni.</summary>
    private const int DefaultMonths = 12;
    private const int MaxMonths = 36;

    /// <summary>
    /// Butun analitika bitta so'rovda. <paramref name="months"/> — nechta oy ko'rsatilsin (1..36).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CourseAnalyticsDto>> Get([FromQuery] int months = DefaultMonths)
    {
        var n = Math.Clamp(months <= 0 ? DefaultMonths : months, 1, MaxMonths);
        return await dataCache.GetOrCreateAsync(
            $"courses:analytics:{n}",
            new[] { nameof(StudentGroup), nameof(Group), nameof(Subject), nameof(Teacher) },
            TimeSpan.FromMinutes(10),
            db => BuildAsync(db, n));
    }

    private static async Task<CourseAnalyticsDto> BuildAsync(IAppDbContext db, int monthCount)
    {
        var months = CourseAnalytics.LastMonths(AppClock.Today, monthCount);

        // Arxivlangan guruhlar ham KERAK: ular orqali o'tgan o'quvchilar tarixi (kelgan/ketgan)
        // yo'qolmasin. "Hozirgi guruhlar soni" esa faqat arxivlanmaganlardan sanaladi.
        var groups = await db.Classes.AsNoTracking()
            .Select(g => new { g.Id, g.CourseId, g.TeacherId, g.MonthlyFee, g.IsArchived })
            .ToListAsync();
        var subjects = await db.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Price }).ToListAsync();
        var memberships = await db.StudentGroups.AsNoTracking()
            .Select(m => new
            {
                m.StudentId, m.GroupId, m.JoinedAt, m.ActivatedAt, m.LeftAt, m.FrozenAt,
                m.Status, m.IsActive,
                // ⚠️ Proyeksiya — yangi maydon O'ZI kelmaydi. Usiz CourseAnalytics.WasActiveAt va
                // "birinchi aktivlashish" qayta aktivlashtirilgan a'zolikda noto'g'ri hisoblanardi.
                m.PastPeriods,
            })
            .ToListAsync();

        var groupById = groups.ToDictionary(g => g.Id);

        // A'zoliklarni KURS bo'yicha guruhlaymiz. Kursi yo'q guruh (CourseId bo'sh) analitikaga
        // kirmaydi — u kurs kesimida ma'noga ega emas (guruh sanog'ida ham ko'rinmaydi).
        var rowsByCourse = new Dictionary<string, Dictionary<string, List<CourseAnalytics.MembershipRow>>>();
        foreach (var m in memberships)
        {
            if (!groupById.TryGetValue(m.GroupId, out var g)) continue;
            var courseId = g.CourseId ?? "";
            if (courseId.Length == 0) continue;

            var row = new CourseAnalytics.MembershipRow(
                m.StudentId, courseId, m.JoinedAt, m.ActivatedAt ?? "", m.LeftAt, m.FrozenAt ?? "",
                m.Status ?? "", m.IsActive, g.MonthlyFee, m.PastPeriods);

            if (!rowsByCourse.TryGetValue(courseId, out var byStudent))
                rowsByCourse[courseId] = byStudent = new Dictionary<string, List<CourseAnalytics.MembershipRow>>();
            if (!byStudent.TryGetValue(m.StudentId, out var list))
                byStudent[m.StudentId] = list = new List<CourseAnalytics.MembershipRow>();
            list.Add(row);
        }

        var courseRows = new List<CourseAnalyticsRowDto>();
        foreach (var s in subjects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var byStudent = rowsByCourse.GetValueOrDefault(s.Id)
                            ?? new Dictionary<string, List<CourseAnalytics.MembershipRow>>();
            var all = byStudent.Values.SelectMany(v => v).ToList();

            // JORIY holat — takrorsiz O'QUVCHILAR bo'yicha (bir o'quvchi shu kursning ikki guruhida
            // bo'lsa ham bitta sanaladi).
            var active = all.Where(r => r.IsActive && r.Status == "active").Select(r => r.StudentId).Distinct().Count();
            var trial = all.Where(r => r.IsActive && r.Status == "trial").Select(r => r.StudentId).Distinct().Count();
            var frozen = all.Where(r => r.IsActive && r.Status == "frozen").Select(r => r.StudentId).Distinct().Count();
            var current = all.Where(r => r.IsActive).Select(r => r.StudentId).Distinct().Count();

            // Kutilayotgan oylik tushum — FAOL a'zoliklar (a'zolik bo'yicha, o'quvchi bo'yicha emas:
            // ikki guruhda o'qiydigan o'quvchi ikki marta to'laydi).
            var revenue = all.Where(r => r.IsActive && r.Status == "active").Sum(r => r.MonthlyFee);

            var courseGroups = groups.Where(g => g.CourseId == s.Id && !g.IsArchived).ToList();

            courseRows.Add(new CourseAnalyticsRowDto(
                s.Id, s.Name, s.Price,
                Groups: courseGroups.Count,
                Teachers: courseGroups.Select(g => g.TeacherId).Where(t => !string.IsNullOrEmpty(t)).Distinct().Count(),
                Active: active, Trial: trial, Frozen: frozen,
                Students: current,
                TotalEver: byStudent.Count,
                MonthlyRevenue: revenue,
                Monthly: CourseAnalytics.MonthlyFlow(byStudent, months)
                    .Select(f => new CourseMonthFlowDto(f.Month, f.Joined, f.Activated, f.Left, f.Completed, f.ActiveEnd))
                    .ToList()));
        }

        // KESISHUV — FAOL a'zoliklar bo'yicha (sinovdagi/muzlatilgan kirmaydi: savol "kim haqiqatan
        // bir nechta kursda o'qiyapti").
        var activeByStudent = new Dictionary<string, HashSet<string>>();
        foreach (var (courseId, byStudent) in rowsByCourse)
            foreach (var (studentId, list) in byStudent)
            {
                if (!list.Any(r => r.IsActive && r.Status == "active")) continue;
                if (!activeByStudent.TryGetValue(studentId, out var set))
                    activeByStudent[studentId] = set = new HashSet<string>();
                set.Add(courseId);
            }

        var (buckets, pairs) = CourseAnalytics.Overlap(activeByStudent);
        var nameById = subjects.ToDictionary(x => x.Id, x => x.Name);

        var overlap = new CourseOverlapDto(
            TotalStudents: activeByStudent.Count,
            OneCourse: buckets.FirstOrDefault(b => b.Courses == 1).Students,
            MultiCourse: buckets.Where(b => b.Courses > 1).Sum(b => b.Students),
            Buckets: buckets.Select(b => new CourseOverlapBucketDto(b.Courses, b.Students)).ToList(),
            Pairs: pairs.Take(20).Select(p => new CoursePairDto(
                p.AId, nameById.GetValueOrDefault(p.AId, "?"),
                p.BId, nameById.GetValueOrDefault(p.BId, "?"),
                p.Students)).ToList());

        return new CourseAnalyticsDto(
            months, courseRows, overlap,
            ActiveStudents: activeByStudent.Count,
            TotalGroups: groups.Count(g => !g.IsArchived && !string.IsNullOrEmpty(g.CourseId)),
            MonthlyRevenue: courseRows.Sum(c => c.MonthlyRevenue));
    }
}
