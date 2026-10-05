using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINING SINFI o'zgarganda oylikni DARHOL qayta hisoblash — YAGONA kirish nuqtasi. Sinf yoziladigan
/// HAR joy shuni chaqiradi: admin tahriri (`StudentsController.Update`), Excel importi (mavjud o'quvchiga
/// sinf qo'shilishi) va o'qituvchi tahriri (`TeacherPortalController`). Hisob — <see cref="TuitionService.ApplyGradeChangeAsync"/>.
///
/// <para>Har o'zgargan oy tarixga ALOHIDA yoziladi (o'quvchi bo'limi, `StudentDiscount` turi — qo'lda
/// oylik tahriri bilan bir joyda): balans nega o'zgargani "O'zgarishlar tarixi"da ko'rinsin.</para>
/// </summary>
public static class GradeRepricing
{
    /// <summary>Sinfni yozishdan OLDIN olingan qiymatlar bilan chaqiriladi. SaveChanges — chaqiruvchida.
    /// Qaytaradi: nechta oy qayta hisoblandi.</summary>
    public static async Task<int> ApplyAsync(Abstractions.IAppDbContext db, AuditService audit, Student s,
                                             int? oldGrade, int? oldGradeYear)
    {
        var changes = await TuitionService.ApplyGradeChangeAsync(db, s, oldGrade, oldGradeYear);
        if (changes.Count == 0) return 0;

        var today = AppClock.Today;
        var gradeText = $"{Label(SchoolGrades.Current(oldGrade, oldGradeYear, today))} → "
                        + Label(SchoolGrades.Current(s.SchoolGrade, s.SchoolGradeYear, today));
        foreach (var c in changes)
            audit.Record(AuditService.EntityStudentDiscount, s.Id, "update",
                $"Oylik sinfga qarab qayta hisoblandi ({gradeText}): {c.GroupName}, {c.Month} — "
                + $"{AuditService.Money(c.OldAmount)} → {AuditService.Money(c.NewAmount)} so'm — {s.FullName}",
                studentId: s.Id);
        return changes.Count;
    }

    private static string Label(int? grade) => grade switch
    {
        null => "sinfsiz",
        SchoolGrades.Graduated => "bitirgan",
        _ => $"{grade}-sinf",
    };
}
