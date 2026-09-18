namespace WunderkindLC.Application.Dtos;

/// <summary>
/// "Joriy oyda obunasi tugaydiganlar" — bitta o'quvchi qatori (edutizim <c>/students/debt-risk</c>).
/// Hisob — <c>SubscriptionRisk</c> (Application/Services).
/// </summary>
/// <param name="LessonsTotal">JAMI DARSLAR NARXI — o'quvchining JORIY OY o'quv to'lovi (barcha
/// guruhlar, chegirma ayrilgan): yozilgan hisoblar + hali yozilmagan (accrual kutilayotgan) oylik.</param>
/// <param name="Balance">JORIY BALANS — <c>Student.Balance</c> (profil va boshqa ro'yxatlar bilan bir xil).</param>
/// <param name="Expected">KUTILAYOTGAN BALANS — joriy oyning BUTUN hisobi yozilgandan keyingi balans
/// (<c>Balance</c> − hali yozilmagan qism). Manfiy = oy darslarini qoplamaydi.</param>
/// <param name="MemberState">A'zolik holati (<c>MembershipLifecycle.MemberState</c>) — "Statusi" filtri uchun.</param>
public record SubscriptionRiskRowDto(
    string StudentId, string FullName, string Phone, string ParentPhone,
    decimal LessonsTotal, decimal Balance, decimal Expected, string MemberState);

/// <summary>Butun hisobot: oy, tepadagi uch jami (FAQAT ro'yxatdagilar bo'yicha) va qatorlar.</summary>
public record SubscriptionRiskReportDto(
    string Month, decimal TotalLessons, decimal TotalBalance, decimal TotalExpected,
    List<SubscriptionRiskRowDto> Items);
