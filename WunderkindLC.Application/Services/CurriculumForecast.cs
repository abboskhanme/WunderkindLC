using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Guruh sillabus (o'quv dasturi) o'tilishi + tugash prognozi. Admin, o'qituvchi va o'quvchi
/// portallari shu yagona mantiqdan foydalanadi (uch joyda farq qilmasligi uchun).
///
/// Guruh kursiga (Subject) BIR NECHTA o'quv dasturi biriktirilgan bo'lishi mumkin
/// (<see cref="SubjectCurriculum"/>) — bu funksiya ularning HAMMASINI (biriktirish tartibi bo'yicha,
/// har biri ichida o'z Mavzu tartibi bo'yicha) BITTA ketma-ket ro'yxatga birlashtirib qaytaradi —
/// guruh/o'qituvchi/o'quvchi ko'rinishi shu bitta birlashtirilgan ro'yxatni ko'radi.
///
/// "O'tilgan" = <see cref="GroupCurriculumLog"/> (revision bo'lmagan, mavjud band) yozuvlari.
/// "Tugash prognozi" = pace (o'tilgan / jami dars) bo'yicha qolgan bandlar uchun kerakli darslar,
/// guruh dars kunlari (Days) bo'yicha oldinga yurib sana hisoblanadi.
/// </summary>
public static class CurriculumForecast
{
    public static async Task<GroupCurriculumDto> BuildGroupAsync(IAppDbContext db, Group group)
    {
        var courseId = group.CourseId;
        var subject = await db.Subjects.FindAsync(courseId);
        var courseName = subject?.Name ?? "";

        var links = string.IsNullOrEmpty(courseId)
            ? new List<SubjectCurriculum>()
            : await db.SubjectCurricula.Where(sc => sc.SubjectId == courseId).OrderBy(sc => sc.Order).ToListAsync();
        var curriculumIds = links.Select(l => l.CurriculumId).ToList();
        // Biriktirish tartibi — birlashtirilgan ro'yxatda qaysi dastur oldin kelishini belgilaydi.
        var attachOrder = links.Select((l, idx) => (l.CurriculumId, idx))
            .ToDictionary(x => x.CurriculumId, x => x.idx);

        var modules = (await db.CourseModules
                .Where(m => curriculumIds.Contains(m.CurriculumId)).ToListAsync())
            .OrderBy(m => attachOrder.GetValueOrDefault(m.CurriculumId, 0)).ThenBy(m => m.Order).ToList();
        var topics = await db.CourseTopics
            .Where(t => curriculumIds.Contains(t.CurriculumId)).OrderBy(t => t.Order).ToListAsync();
        var lessons = await db.CourseLessons
            .Where(s => curriculumIds.Contains(s.CurriculumId)).ToListAsync();
        var items = await db.CourseItems
            .Where(i => curriculumIds.Contains(i.CurriculumId)).OrderBy(i => i.Order).ToListAsync();
        var existingItemIds = items.Select(i => i.Id).ToHashSet();
        // Item → uning to'g'ridan-to'g'ri ota TOPICI (dars qatlamini "shaffof" qilib o'tkazib
        // yuboradi — bu DTO Modul→Mavzu→Band tekis ko'rinishida qoladi, frontend o'zgarmaydi).
        var topicIdByLesson = lessons.ToDictionary(s => s.Id, s => s.TopicId);

        var logs = await db.GroupCurriculumLogs.Where(g => g.GroupId == group.Id).ToListAsync();

        var coveredItemIds = logs
            .Where(l => !l.IsRevision && l.ItemId != "" && existingItemIds.Contains(l.ItemId))
            .Select(l => l.ItemId).Distinct().ToHashSet();

        var coverDateByItem = logs
            .Where(l => !l.IsRevision && l.ItemId != "")
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).First().Date);

        var totalItems = items.Count;
        var coveredCount = coveredItemIds.Count;
        var revisionLessons = logs.Count(l => l.IsRevision);
        var totalLessons = coveredCount + revisionLessons;
        var remainingItems = Math.Max(0, totalItems - coveredCount);

        var pace = totalLessons > 0 ? (double)coveredCount / totalLessons : 1.0;
        pace = Math.Max(pace, 0.1);
        var estLessonsLeft = remainingItems == 0 ? 0 : (int)Math.Ceiling(remainingItems / pace);

        var days = group.Days ?? new List<int>();
        var lessonsPerWeek = days.Count > 0 ? days.Count : 3;

        var estFinishDate = "";
        if (estLessonsLeft > 0)
        {
            if (days.Count > 0)
            {
                var daySet = days.ToHashSet();
                var date = AppClock.Today;
                var counted = 0;
                var guard = 0;
                while (counted < estLessonsLeft && guard < 3000)
                {
                    date = date.AddDays(1);
                    var weekday = ((int)date.DayOfWeek + 6) % 7; // Monday=0..Sunday=6
                    if (daySet.Contains(weekday)) counted++;
                    guard++;
                }
                estFinishDate = date.ToString("yyyy-MM-dd");
            }
            else
            {
                estFinishDate = AppClock.Today.AddDays(estLessonsLeft * 7 / 3).ToString("yyyy-MM-dd");
            }
        }

        var moduleDtos = modules.Select(m => new GroupCurriculumModuleDto(
            m.Id, m.Name, m.Note, m.Order,
            topics.Where(t => t.ModuleId == m.Id).Select(t => new GroupCurriculumTopicDto(
                t.Id, t.Title, t.Note, t.Order,
                items.Where(i => topicIdByLesson.GetValueOrDefault(i.LessonId) == t.Id).Select(i => new GroupCurriculumItemDto(
                    i.Id, i.Text, i.Note, i.Order, coveredItemIds.Contains(i.Id),
                    coveredItemIds.Contains(i.Id) && coverDateByItem.TryGetValue(i.Id, out var cd) ? cd : "")).ToList()
            )).ToList()
        )).ToList();

        return new GroupCurriculumDto(
            group.Id, courseId, courseName,
            totalItems, coveredCount, revisionLessons, totalLessons,
            remainingItems, estLessonsLeft, lessonsPerWeek, estFinishDate,
            moduleDtos);
    }
}
