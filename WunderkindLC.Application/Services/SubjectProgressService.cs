using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "Fan progresi" — qo'lda qo'shilgan darslar (<see cref="LessonNote"/>) va ulardagi "dars o'tildi"
/// belgisi (<see cref="LessonNote.Conducted"/>) asosida. Dars jadvali olib tashlandi.
///
/// <para><b>Reja (Planned)</b> = shu fan uchun qo'shilgan darslar (LessonNote) soni.
/// <b>O'tilgan (Conducted)</b> = o'qituvchi "o'tildi" deb belgilaganlari. <b>Progress</b> = Conducted / Planned.</para>
/// </summary>
public static class SubjectProgressService
{
    /// <summary>Bitta dars nusxasi (aniq kun + dars raqami).</summary>
    public sealed class LessonSlot
    {
        public string Date { get; init; } = "";
        public int Period { get; init; }
        public string SubjectId { get; init; } = "";
        public string TeacherId { get; init; } = "";
        public bool Conducted { get; init; }
        public string Topic { get; init; } = "";
        public string? Homework { get; init; }
    }

    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    private static int Pct(int a, int b) => b <= 0 ? 0 : (int)Math.Round(a * 100.0 / b);

    /// <summary>Guruhning BARCHA darslari (qo'lda qo'shilgan LessonNote nusxalari).</summary>
    public static async Task<List<LessonSlot>> ClassSlotsAsync(
        IAppDbContext db, string classId, int quarter)
    {
        var notes = await db.LessonNotes
            .Where(n => n.ClassId == classId && n.Quarter == quarter)
            .ToListAsync();
        return notes.Select(n => new LessonSlot
        {
            Date = n.Date,
            Period = n.Period,
            SubjectId = n.SubjectId,
            TeacherId = "",
            Conducted = n.Conducted,
            Topic = n.Topic,
            Homework = n.Homework,
        }).ToList();
    }

    /// <summary>O'quvchi/ota-ona: umumiy + har bir fan progresi.</summary>
    public static async Task<StudentSubjectsProgressDto> ForStudentAsync(
        IAppDbContext db, string classId, int quarter)
    {
        var slots = await ClassSlotsAsync(db, classId, quarter);
        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var today = Today;

        var subjects = slots
            .GroupBy(s => s.SubjectId)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Period).ToList();
                var planned = ordered.Count;
                var conducted = ordered.Count(x => x.Conducted);
                var expected = ordered.Count(x => string.CompareOrdinal(x.Date, today) <= 0);
                var next = ordered.FirstOrDefault(x => !x.Conducted)?.Date;
                var last = ordered.Count > 0 ? ordered[^1].Date : null;
                return new SubjectProgressDto(
                    g.Key, subjectNames.GetValueOrDefault(g.Key, ""),
                    planned, conducted, Math.Max(0, planned - conducted),
                    Pct(conducted, planned), expected, next, last);
            })
            .OrderBy(s => s.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalPlanned = subjects.Sum(s => s.Planned);
        var totalConducted = subjects.Sum(s => s.Conducted);
        return new StudentSubjectsProgressDto(
            quarter, totalPlanned, totalConducted, Pct(totalConducted, totalPlanned), subjects);
    }

    /// <summary>O'quvchi: bitta fanga kirilganda darslar ro'yxati (yashil/qizil). Fan topilmasa null.</summary>
    public static async Task<SubjectProgressDetailDto?> ForStudentSubjectAsync(
        IAppDbContext db, string classId, int quarter, string subjectId)
    {
        var slots = (await ClassSlotsAsync(db, classId, quarter))
            .Where(s => s.SubjectId == subjectId)
            .OrderBy(s => s.Date, StringComparer.Ordinal).ThenBy(s => s.Period)
            .ToList();
        if (slots.Count == 0) return null;

        var name = (await db.Subjects.FindAsync(subjectId))?.Name ?? "";
        var today = Today;

        // Dars vaqtlari (qo'ng'iroqlar jadvali) olib tashlandi — boshlanish/tugash vaqti yo'q (null).
        var lessons = slots.Select(s =>
            new SubjectLessonDto(
                s.Date, s.Period, null, null,
                s.Topic, s.Homework, s.Conducted,
                string.CompareOrdinal(s.Date, today) <= 0)).ToList();

        var planned = slots.Count;
        var conducted = slots.Count(s => s.Conducted);
        return new SubjectProgressDetailDto(
            subjectId, name, quarter, planned, conducted,
            Math.Max(0, planned - conducted), Pct(conducted, planned), lessons);
    }

    /// <summary>
    /// O'qituvchi: o'zi biriktirilgan barcha (guruh, fan) bo'yicha o'tilgan darslar progresi.
    /// O'qituvchining guruhlari <see cref="Group.TeacherId"/> orqali, har guruhning fani esa
    /// uning <see cref="Group.CourseId"/> orqali aniqlanadi (bir guruh — bitta kurs).
    /// </summary>
    public static async Task<TeacherProgressDto> ForTeacherAsync(IAppDbContext db, string teacherId, int quarter)
    {
        var groups = await db.Classes.Where(c => c.TeacherId == teacherId).ToListAsync();
        if (groups.Count == 0) return new TeacherProgressDto(quarter, 0, 0, 0, new());

        var classIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var classNames = await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);
        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);

        var notes = await db.LessonNotes
            .Where(n => n.Quarter == quarter && classIds.Contains(n.ClassId))
            .ToListAsync();
        var today = Today;

        // Biriktirilgan (guruh, fan=CourseId) lar bo'yicha jamlash.
        var agg = new Dictionary<(string ClassId, string SubjectId), (int Planned, int Conducted, int Expected)>();
        foreach (var g in groups)
            if (!string.IsNullOrEmpty(g.CourseId))
                agg[(g.Id, g.CourseId)] = (0, 0, 0);

        foreach (var n in notes)
        {
            var key = (n.ClassId, n.SubjectId);
            if (!agg.TryGetValue(key, out var cur)) continue; // bu o'qituvchiga biriktirilmagan
            cur.Planned++;
            if (n.Conducted) cur.Conducted++;
            if (string.CompareOrdinal(n.Date, today) <= 0) cur.Expected++;
            agg[key] = cur;
        }

        var items = agg
            .Select(kv => new TeacherSubjectProgressDto(
                kv.Key.ClassId, classNames.GetValueOrDefault(kv.Key.ClassId, ""),
                kv.Key.SubjectId, subjectNames.GetValueOrDefault(kv.Key.SubjectId, ""),
                kv.Value.Planned, kv.Value.Conducted,
                Math.Max(0, kv.Value.Planned - kv.Value.Conducted),
                Pct(kv.Value.Conducted, kv.Value.Planned), kv.Value.Expected))
            .OrderBy(i => i.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalPlanned = items.Sum(i => i.Planned);
        var totalConducted = items.Sum(i => i.Conducted);
        return new TeacherProgressDto(
            quarter, totalPlanned, totalConducted, Pct(totalConducted, totalPlanned), items);
    }
}
