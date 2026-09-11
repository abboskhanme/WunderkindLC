namespace WunderkindLC.Application.Dtos;

/* ---------- Moliya → "Bonus" tabi (o'quvchini ushlab turish bonuslari hisoboti) ----------
 *
 * DIQQAT: bonus PUL CHIQIMI EMAS — u faqat QAYD (RetentionBonusAward/RetentionBonusShare).
 * FinanceTransaction yoki SalaryLedger ga ULANMAGAN, shuning uchun Moliyaning kirim/chiqim
 * raqamlariga UMUMAN ta'sir qilmaydi (haqiqiy pul o'qituvchiga maosh to'lovi orqali beriladi).
 * Bu yerdagi DTO'lar ALOHIDA, FAQAT O'QISH uchun hisobot — bonus BERISH/bekor qilish
 * "O'quvchilar → Bonus hisoboti" sahifasida (RetentionBonusController) qoladi.
 */

/// <summary>
/// Hisobotdagi bitta QATOR — award × o'qituvchi. Bitta bonus bir nechta o'qituvchiga
/// bo'linishi mumkin (o'quvchi sikl davomida guruh almashtirgan bo'lsa), shuning uchun
/// HAR ULUSH alohida qator bo'ladi.
/// <para><c>GivenMonth</c> — bonus BERILGAN oy ("YYYY-MM", <c>RetentionBonusAward.CreatedAt</c> dan),
/// <c>PeriodFrom</c>/<c>PeriodTo</c> — bonus QAYSI DAVR uchun berilgani (sikl oylari).</para>
/// </summary>
public record RetentionBonusFinanceRowDto(
    string AwardId, string GivenMonth,
    string TeacherId, string TeacherName,
    string StudentId, string StudentName, string CourseName,
    string PeriodFrom, string PeriodTo,
    // Months — shu o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin), Amount — ulush summasi (so'm).
    decimal Months, decimal Amount,
    // "given" (berilgan) | "cancelled" (bekor qilingan)
    string Status,
    DateTime GivenAt, string GivenBy);

/// <summary>O'qituvchilar kesimi — kim qancha bonus oldi (FAQAT "given").</summary>
public record RetentionBonusByTeacherDto(string TeacherId, string TeacherName, int Count, decimal Total);

/// <summary>Oylar kesimi — qaysi oyda qancha bonus berildi (FAQAT "given").</summary>
public record RetentionBonusByMonthDto(string Month, int Count, decimal Total);

/// <summary>
/// Moliya → "Bonus" tabining to'liq javobi. <c>Total</c>/<c>Count</c> — faqat "given";
/// bekor qilinganlar jamiga QO'SHILMAYDI, ular <c>CancelledTotal</c>/<c>CancelledCount</c> da
/// alohida ko'rsatiladi (ro'yxatda esa chizib tashlangan holda ko'rinadi).
/// </summary>
public record RetentionBonusFinanceDto(
    // From/To — hisobot davri ("YYYY-MM", ikkalasi ham inklyuziv)
    string From, string To,
    decimal Total, int Count,
    decimal CancelledTotal, int CancelledCount,
    List<RetentionBonusByTeacherDto> ByTeacher,
    List<RetentionBonusByMonthDto> ByMonth,
    List<RetentionBonusFinanceRowDto> Rows);
