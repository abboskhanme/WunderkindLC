namespace WunderkindLC.Application.Dtos;

/* ---------- CHEGIRMALAR: o'quvchi REGISTRI va markaz bo'yicha HISOBOT ----------
 *
 * ⚠️ DIQQAT: registr (`StudentDiscount`) endi PUL MANBASI — oylik hisob chegirmani AYNAN
 * shundan oladi (`TuitionService.DiscountForMonth(rows, ...)`), `Student.Discount*` esa faqat
 * KO'ZGU (denormalizatsiya). Chegirma HAR FAN (guruh) uchun alohida bo'lishi mumkin va ular
 * HECH QACHON QO'SHILMAYDI — eng aniq moslik g'olib. Batafsil: `.claude/rules/discounts.md`.
 *
 * ⚠️ Shakl KLIENT SHARTNOMASI bilan aynan bir xil bo'lishi SHART:
 * `WunderkindLC.Client/src/api/services/discounts.ts`. Maydon qo'shish/olib tashlash ikkala
 * tomonda BIRGA qilinadi.
 */

/// <summary>Bitta registr qatori (o'quvchiga berilgan chegirma).</summary>
/// <param name="GroupId">null — BARCHA guruh hisoblariga; id — faqat o'sha FAN (guruh) hisobiga.</param>
/// <param name="CourseName">Guruhning FANI — SNAPSHOT (<c>""</c> — barcha guruhlar).</param>
/// <param name="StatusLabel">Holatning o'zbekcha yorlig'i (<c>DiscountRules.StatusLabel</c>).</param>
/// <param name="InForce">Hozir HAQIQATAN amalda: <c>active</c> VA joriy oy davr ichida.</param>
public record StudentDiscountItemDto(
    string Id, string StudentId, string StudentName,
    string? GroupId, string GroupName,
    string? TeacherId, string TeacherName,
    string CourseName,
    int Pct, decimal Amount,
    string StartMonth, string EndMonth,
    string Reason,
    string Status, string StatusLabel, bool InForce,
    string CreatedAt, string CreatedBy,
    string EndedAt, string EndedBy, string CancelReason);

/// <summary>
/// Oyda HAQIQATAN qo'llangan chegirma — <see cref="WunderkindLC.Domain.MonthlyCharge"/> dan
/// (pul haqiqati registrdan EMAS, hisob qatorlaridan olinadi).
/// </summary>
/// <param name="CourseName">Guruhning FANI. ⚠️ Server beradi — klient guruh id bo'yicha topa
/// OLMAYDI: o'quvchi chiqib ketgan guruhning oylari na faol a'zoliklarda, na registrda bo'ladi.</param>
public record StudentDiscountMonthDto(
    string Month, string? GroupId, string GroupName, string CourseName, string TeacherName,
    decimal Charged, decimal Discount);

/// <summary>
/// Chegirma BERISH mumkin bo'lgan QAMROV (scope) — o'quvchining har bir FANI (faol guruhi) va
/// ustiga «Barcha guruhlar». Modal aynan shundan quriladi.
/// </summary>
/// <param name="GroupId">null — «Barcha guruhlar».</param>
/// <param name="MonthlyFee">Guruhning joriy oyligi — chegirma ta'sirini oldindan ko'rsatish uchun.</param>
/// <param name="HasActive">Shu qamrovda ALLAQACHON amaldagi chegirma bormi (server 409 qaytaradi).</param>
/// <param name="CurrentCharge">
/// JORIY oydagi HAQIQIY hisob summasi (<see cref="WunderkindLC.Domain.MonthlyCharge"/>.Amount,
/// chegirmagacha); qator hali yozilmagan bo'lsa — null.
///
/// <para>⚠️ Chegirma AYNAN shu summa ustida hisoblanadi, guruhning joriy narxi ustida EMAS.
/// Ikkisi farq qilishi mumkin: qisman (prorate) oy — aktivlashtirish/muzlatish oyi — va
/// superadmin qo'lda tahrirlagan (<c>Locked</c>) qator. Modal <see cref="MonthlyFee"/> ni baza
/// qilib ko'rsatsa, admin oynada bir raqamni, to'lov tarixida boshqasini ko'rardi.</para>
///
/// <para>«Barcha guruhlar» qamrovida bitta summa YO'Q (bir nechta guruh) — u yerda null.</para>
/// </param>
public record DiscountScopeOptionDto(
    string? GroupId, string GroupName, string CourseName, string TeacherName,
    decimal MonthlyFee, bool HasActive, decimal? CurrentCharge);

/// <summary>O'quvchining chegirma registri + qo'llangan chegirmalar tarixi (bitta javobda).</summary>
/// <param name="Items">Registr qatorlari — eng yangisi birinchi (TARIX ham shu ro'yxatda).</param>
/// <param name="Active">HOZIR amaldagi chegirmalar — HAR QAMROVDAN bittadan.</param>
/// <param name="Scopes">Chegirma berish mumkin bo'lgan qamrovlar.</param>
/// <param name="Applied">Oylar bo'yicha qo'llangan chegirma — eng yangi oy birinchi.</param>
/// <param name="TotalDiscount"><paramref name="Applied"/> yig'indisi.</param>
/// <param name="CurrentMonthDiscount">Joriy oy hisoblarida beriladigan chegirma — BARCHA fanlar bo'yicha JAMI.</param>
public record StudentDiscountsResponseDto(
    List<StudentDiscountItemDto> Items,
    List<StudentDiscountItemDto> Active,
    List<DiscountScopeOptionDto> Scopes,
    List<StudentDiscountMonthDto> Applied,
    decimal TotalDiscount,
    decimal CurrentMonthDiscount);

/// <summary>Chegirma yaratish/tahrirlash tanasi.</summary>
public record StudentDiscountPayloadDto(
    int Pct, decimal Amount,
    string? StartMonth, string? EndMonth, string? Reason, string? GroupId);

/// <summary>Chegirmani bekor qilish tanasi.</summary>
public record StudentDiscountCancelDto(string? Reason);

/* ══════════════════════ HISOBOT ══════════════════════ */

/// <summary>Hisobot sarlavhasi — "markazda chegirma qanchaga tushyapti".</summary>
/// <param name="ActiveCount">Hozir amaldagi registr qatorlari soni.</param>
/// <param name="StudentCount">Chegirmasi bor (arxivlanmagan) o'quvchilar soni.</param>
/// <param name="TotalStudents">Arxivlanmagan o'quvchilar soni — ulush uchun maxraj.</param>
/// <param name="PeriodCharged">Davrdagi BARCHA hisoblar yig'indisi (chegirmalilar emas).</param>
public record DiscountReportSummaryDto(
    int ActiveCount, int StudentCount, int TotalStudents, decimal StudentSharePct,
    decimal PeriodCharged, decimal PeriodDiscount, decimal SharePct,
    decimal CurrentMonthDiscount);

/// <summary>Oy kesimi. Chegirmasiz oy ham 0 bilan qaytadi — grafik uzilmasin.</summary>
public record DiscountReportMonthDto(string Month, decimal Charged, decimal Discount, int Students);

/// <summary>O'qituvchi kesimi (hisob QAYSI guruhda yozilganiga qarab).</summary>
public record DiscountReportTeacherDto(
    string TeacherId, string TeacherName, decimal Charged, decimal Discount, int Students, int Groups);

/// <summary>Guruh kesimi.</summary>
public record DiscountReportGroupDto(
    string GroupId, string GroupName, string TeacherName, string CourseName,
    decimal Charged, decimal Discount, int Students);

/// <summary>O'quvchi kesimi.</summary>
/// <param name="ActiveCount">Hozir amaldagi chegirmalar SONI (har fan uchun alohida sanaladi).</param>
/// <param name="ActiveLabel">Ularning qisqa yorlig'i — «Matematika 20%, Ingliz tili 50 000 so'm»
/// (<c>DiscountRules.ActiveLabel</c>). ⚠️ SERVERDA quriladi: klientda qayta yig'ilsa, qoida
/// o'zgarganda ikkinchisi jimgina eskirardi. <c>""</c> — amaldagi chegirma yo'q.</param>
public record DiscountReportStudentDto(
    string StudentId, string StudentName,
    decimal Charged, decimal Discount, int Months,
    List<string> GroupNames, List<string> TeacherNames,
    int ActiveCount, string ActiveLabel, bool HasActive);

/// <summary>Sabab kesimi (registrdagi amaldagi qatorlar bo'yicha). <c>""</c> — sababsiz.</summary>
public record DiscountReportReasonDto(string Reason, int Count, decimal Discount);

/// <summary>Markaz bo'yicha chegirmalar hisoboti. <c>From</c>/<c>To</c> — "yyyy-MM" (inklyuziv).</summary>
public record DiscountReportDto(
    string From, string To,
    DiscountReportSummaryDto Summary,
    List<DiscountReportMonthDto> Months,
    List<DiscountReportTeacherDto> ByTeacher,
    List<DiscountReportGroupDto> ByGroup,
    List<DiscountReportStudentDto> ByStudent,
    List<DiscountReportReasonDto> ByReason,
    List<StudentDiscountItemDto> Active);
