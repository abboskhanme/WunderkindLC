using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Jurnal yozuvlaridan reyting/ko'rsatkichlarni hisoblovchi yordamchi.
/// Baholar JournalEntry.Grade, davomat esa ReasonId belgilangan yozuvlardan olinadi.
/// </summary>
public static class Analytics
{
    public record ClassResult(List<SubjectDto> Subjects, List<ClassStudentRowDto> Rows);

    private static double Round1(double v) => Math.Round(v, 1);

    private static StudentDto Map(Student s) => new(
        s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
        s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance);

    public static ClassResult BuildClass(
        Group cls,
        IReadOnlyList<Student> allStudents,
        IReadOnlyList<Subject> allSubjects,
        IReadOnlyList<string> assignedSubjectIds,
        IReadOnlyList<JournalEntry> classEntries,
        IReadOnlyList<LessonNote> classNotes,
        IReadOnlyCollection<string>? lateReasonIds = null,
        IReadOnlyCollection<string>? activeMemberIds = null,
        IReadOnlyCollection<string>? anyMemberIds = null)
    {
        // "Kech keldi" turidagi sabablar davomatsizlik (absence) sifatida hisoblanmaydi.
        var lateSet = lateReasonIds is null ? new HashSet<string>() : new HashSet<string>(lateReasonIds);

        // Guruh a'zolari M2M `StudentGroup` jadvalidan (a'zolar oynasi "azolar" shu manbadan ishlaydi).
        // `activeMemberIds` berilsa — guruh roster'i = FAOL a'zolik (IsActive). Eski (M2M'gacha) o'quvchilar
        // (bu guruh uchun umuman a'zolik yozuvi YO'Q) `ClassName` yorlig'i bo'yicha qo'shiladi — orqaga moslik.
        // `activeMemberIds` berilmasa (null) — eski xulq: faqat `ClassName == guruh nomi`.
        List<Student> students;
        if (activeMemberIds is not null)
        {
            var activeSet = activeMemberIds as HashSet<string> ?? new HashSet<string>(activeMemberIds);
            var anySet = anyMemberIds is null
                ? new HashSet<string>()
                : (anyMemberIds as HashSet<string> ?? new HashSet<string>(anyMemberIds));
            students = allStudents
                .Where(s => activeSet.Contains(s.Id) || (s.ClassName == cls.Name && !anySet.Contains(s.Id)))
                .ToList();
        }
        else
        {
            students = allStudents.Where(s => s.ClassName == cls.Name).ToList();
        }

        // Davomat FAQAT o'tilgan darslar bo'yicha (ptichka/baho/davomat). Bir kun ichidagi har dars
        // (sana+dars raqami) alohida hisoblanadi.
        var conductedNotes = classNotes.Where(n => n.Conducted)
            .Select(n => (n.SubjectId, n.Date, n.Period)).ToHashSet();

        var subjectIds = assignedSubjectIds.Count > 0
            ? assignedSubjectIds.Distinct().ToList()
            : allSubjects.Select(s => s.Id).ToList();
        var subjects = subjectIds
            .Select(id => allSubjects.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => new SubjectDto(s!.Id, s.Name, s.Price))
            .ToList();

        var rows = students.Select(student =>
        {
            var studentEntries = classEntries.Where(e => e.StudentId == student.Id).ToList();

            var grades = new Dictionary<string, double>();
            foreach (var subj in subjects)
            {
                // Kunlik baholar o'rtachasi.
                var subjectGrades = studentEntries
                    .Where(e => e.SubjectId == subj.Id && e.Grade.HasValue)
                    .Select(e => (double)e.Grade!.Value)
                    .ToList();
                grades[subj.Id] = subjectGrades.Count > 0 ? Round1(subjectGrades.Average()) : 0;
            }

            // O'rtacha: kunlik baholar o'rtachasi.
            var allGrades = studentEntries.Where(e => e.Grade.HasValue).Select(e => (double)e.Grade!.Value).ToList();
            double average = allGrades.Count > 0 ? Round1(allGrades.Average()) : 0;

            // Shu o'quvchi qatnashadigan o'tilgan darslar.
            var studentConducted = conductedNotes;
            var conducted = studentConducted.Count;

            // O'tilgan darslardagi davomatsizliklar (sababli/sababsiz/kasal — KECH KELDI bundan mustasno).
            // Dars o'tilmagan bo'lsa — null.
            var absent = studentEntries.Count(e =>
                e.ReasonId != null && !lateSet.Contains(e.ReasonId)
                && studentConducted.Contains((e.SubjectId, e.Date, e.Period)));
            double? attendance = conducted > 0 ? Math.Round((double)(conducted - absent) / conducted * 100) : null;

            return new ClassStudentRowDto(Map(student), grades, average, attendance);
        }).ToList();

        return new ClassResult(subjects, rows);
    }
}
