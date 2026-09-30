namespace WunderkindLC.Domain;

/// <summary>
/// O'quvchining MAKTAB SINFI (1..11) — "markazda nechanchi sinflar o'qiyapti" statistikasi uchun.
///
/// <para>⚠️ <b>SINF HAR YILI O'ZI OSHADI.</b> Bazada qat'iy raqam emas, JUFTLIK saqlanadi:
/// <see cref="Student.SchoolGrade"/> — kiritilgan sinf, <see cref="Student.SchoolGradeYear"/> — u
/// QAYSI o'quv yilida kiritilgani. Joriy sinf shulardan hisoblanadi (<see cref="Current"/>).
/// Aks holda "7-sinf" deb kiritilgan o'quvchi keyingi sentyabrda ham 7-sinf bo'lib qolar va
/// statistika bir yildan keyin butunlay yolg'on chiqardi (hech kim har yili qo'lda tuzatmaydi).</para>
///
/// <para>O'quv yili 1-sentabrda almashadi (<see cref="AcademicYearStartMonth"/>). 11-sinfdan keyin —
/// <see cref="Graduated"/> ("maktabni bitirgan"), undan yuqoriga o'smaydi.</para>
/// </summary>
public static class SchoolGrades
{
    public const int Min = 1;
    public const int Max = 11;
    /// <summary>Maktabni bitirgan — 11-sinfdan keyingi ALOHIDA qiymat (formada ham tanlanadi).</summary>
    public const int Graduated = 12;
    /// <summary>O'quv yili boshlanadigan oy (sentabr).</summary>
    public const int AcademicYearStartMonth = 9;

    /// <summary>Sana qaysi o'quv yiliga tegishli — BOSHLANISH yili (2026-09-01..2027-08-31 → 2026).</summary>
    public static int AcademicYear(DateOnly date) =>
        date.Month >= AcademicYearStartMonth ? date.Year : date.Year - 1;

    /// <summary>Saqlash mumkin bo'lgan qiymat: 1..11 yoki <see cref="Graduated"/>.</summary>
    public static bool IsValid(int grade) => grade >= Min && grade <= Graduated;

    /// <summary>
    /// <paramref name="today"/> kuni o'quvchi nechanchi sinfda. Kiritilmagan yoki buzuq qiymat — <c>null</c>.
    /// Yil yo'q (qo'lda tuzatilgan yozuv) — kiritilgan sinf o'zgarishsiz. Natija 1..<see cref="Graduated"/>
    /// oralig'ida qirqiladi (kelajak yili yozilgan buzuq qator 0 yoki manfiy sinf bermasin).
    /// </summary>
    public static int? Current(int? grade, int? year, DateOnly today)
    {
        if (grade is not int g || !IsValid(g)) return null;
        if (year is not int y) return g;
        return Math.Clamp(g + (AcademicYear(today) - y), Min, Graduated);
    }
}
