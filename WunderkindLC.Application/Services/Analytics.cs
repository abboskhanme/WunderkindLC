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
        IReadOnlyCollection<string>? anyMemberIds = null,
        IReadOnlyDictionary<string, StudentJournalBuilder.Bounds>? boundsByStudent = null)
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
        // (fan, sana, dars raqami) → davomati TO'LIQ olinganmi (AttendanceRules: faqat shunda belgisiz = keldi).
        var conductedNotes = classNotes.Where(n => n.Conducted)
            .GroupBy(n => (n.SubjectId, n.Date, n.Period))
            .ToDictionary(g => g.Key, g => g.Any(n => n.AttendanceTaken));
        var groupStart = cls.StartDate is { Length: >= 10 } ? cls.StartDate[..10] : null;

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

            // YAGONA ta'rif (AttendanceRules, SPEC P3.1): o'tilgan har dars uchun BITTA belgi — kech = keldi,
            // davomat olingan + belgisiz = keldi, davomat olinmagan + belgisiz = BELGILANMAGAN (foizga kirmaydi),
            // a'zolikdan tashqari kun (chegaralar berilgan bo'lsa) — hisobga olinmaydi. Dars bo'lmasa — null.
            var bounds = boundsByStudent?.GetValueOrDefault(student.Id);
            var entryByLesson = studentEntries
                .GroupBy(e => (e.SubjectId, e.Date, e.Period))
                .ToDictionary(g => g.Key, g => g.First());
            var tally = AttendanceRules.Tally(conductedNotes.Select(kv =>
            {
                var date = kv.Key.Date;
                var blocked = (groupStart is not null && string.CompareOrdinal(date, groupStart) < 0)
                    || (bounds?.MemberStart is not null && string.CompareOrdinal(date, bounds.MemberStart) < 0)
                    || (bounds?.MemberEnd is not null && string.CompareOrdinal(date, bounds.MemberEnd) > 0);
                var e = entryByLesson.GetValueOrDefault(kv.Key);
                // Sabab lug'ati bu yerda yo'q — "kelmadi/kech" uchun faqat IsLate kerak (sababli/sababsiz farqi
                // foizga ta'sir qilmaydi).
                var reason = e?.ReasonId is null ? null : new AbsenceReason { IsLate = lateSet.Contains(e.ReasonId) };
                return AttendanceRules.Classify(true, kv.Value, blocked,
                    AttendanceRules.BeforePresentDefault(date, bounds?.PresentDefaultFrom), e, reason);
            }));
            double? attendance = tally.Percent;

            return new ClassStudentRowDto(Map(student), grades, average, attendance);
        }).ToList();

        return new ClassResult(subjects, rows);
    }
}
