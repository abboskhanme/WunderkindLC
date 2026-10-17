namespace WunderkindLC.Application.Dtos;

/* ---------- MOLIYA HISOBOTLARI: qarzdorlik, guruhlar va tariflar, hisob-fakturalar ----------
 *
 * Hammasi BITTA hisob asosida — `DebtLedger` (o'quvchi, guruh, oy) kataklari: hisob
 * (`MonthlyCharge`) va SHU guruh+oyga teglangan to'lov (vozvrat ayrilgan). Kassadagi to'lov oynasi
 * (`StudentGroupLedger`) bilan AYNAN bir qoida: keyingi oyga to'langan avans o'tgan oy qarzini yopmaydi.
 *
 * ⚠️ Shakl klient shartnomasi bilan bir xil: `WunderkindLC.Client/src/api/services/financeReports.ts`.
 */

/// <summary>Pul yig'indisi: hisoblangan (to'liq), chegirma, sof, to'langan (sofdan oshmagan qismi), qarz.</summary>
/// <param name="CollectionPct">To'langan ÷ sof × 100 (sof 0 bo'lsa 100).</param>
public record DebtTotalsDto(
    decimal Charged, decimal Discount, decimal Net, decimal Paid, decimal Debt,
    decimal CollectionPct, int Debtors);

/// <summary>Hisobi YO'Q oyga tushgan to'lovlar (sinovdagi avans, guruhga teglanmagan eski to'lov) — qarzni yopmaydi,
/// shuning uchun hisobotda alohida aytiladi (jimgina yo'qolmasin).</summary>
public record UnmatchedPaymentsDto(int Count, decimal Amount);

/// <summary>Qarz yoshi guruhi: <c>notdue</c> (muddat kuni ham shu) · <c>0-6</c> (1–6 kun) · <c>7-29</c> · <c>30-59</c> · <c>60+</c>.</summary>
public record DebtAgeBucketDto(string Key, string Label, int Rows, int Students, decimal Debt);

/// <summary>Matritsa qatori — bitta (o'quvchi, guruh).</summary>
/// <param name="Cells">Oy ("yyyy-MM") → shu oyning qolgan qarzi (faqat &gt; 0 bo'lganlari).</param>
/// <param name="OldestMonth">Eng eski to'lanmagan oy.</param>
/// <param name="AgeDays">Eng eski oyning to'lov muddatidan beri o'tgan kun (manfiy — muddati kelmagan).</param>
/// <param name="MemberStatus"><c>active</c> · <c>frozen</c> · <c>trial</c> · <c>completed</c> · <c>left</c> · <c>""</c>.</param>
public record DebtRowDto(
    string StudentId, string StudentName, string Phone,
    string GroupId, string GroupName, string CourseName, string TeacherName,
    string MemberStatus, bool IsArchived,
    IReadOnlyDictionary<string, decimal> Cells,
    decimal Debt, string OldestMonth, int AgeDays, string Bucket);

/// <param name="Total">Tanlangan qamrov (arxiv belgisi bilan) bo'yicha.</param>
/// <param name="Active">Faqat arxivlanmagan o'quvchilar.</param>
/// <param name="Archived">Faqat arxivdagi o'quvchilar (belgi o'chiq bo'lsa ham — ma'lumot uchun).</param>
/// <param name="DueDay">To'lov muddati — oyning shu kuni (<c>CenterMeta.JournalUnpaidCutoffDay</c>).</param>
public record DebtReportDto(
    string From, string To, IReadOnlyList<string> Months, int DueDay, bool IncludeArchived,
    DebtTotalsDto Total, DebtTotalsDto Active, DebtTotalsDto Archived,
    IReadOnlyList<DebtAgeBucketDto> Buckets,
    IReadOnlyList<DebtRowDto> Rows,
    UnmatchedPaymentsDto Unmatched);

/// <summary>Guruhdagi tarif: shu narxda nechta o'quvchi (to'liq oy) va nechtasi qisman oy bilan.</summary>
public record TariffPriceDto(decimal Price, int Students, int Partial);

/// <param name="TeacherName">Shu OYNING egasi (o'qituvchi tarixi bo'yicha, maosh bilan bir xil).</param>
/// <param name="Planned">Hisoblangan to'liq summa (chegirmasiz).</param>
/// <param name="Collected">Shu oy hisobini yopgan to'lovlar (guruhga teglangan, vozvrat ayrilgan, sofdan oshmaydi) —
/// Planned − Discount − Collected = Debt.</param>
/// <param name="Weak">Yig'ilish &lt; <c>TariffReport.WeakPct</c> — to'lov intizomi sust.</param>
public record TariffGroupDto(
    string GroupId, string GroupName, string CourseName, string TeacherId, string TeacherName, bool IsArchived,
    int Students, IReadOnlyList<TariffPriceDto> Prices,
    decimal Planned, decimal Discount, decimal Net, decimal Collected, decimal Debt, decimal CollectionPct,
    int Debtors, bool Weak);

public record TariffTeacherDto(
    string TeacherId, string TeacherName, int Groups, int Students,
    decimal Planned, decimal Discount, decimal Net, decimal Collected, decimal Debt, decimal CollectionPct,
    int WeakGroups);

public record TariffReportDto(
    string Month, decimal WeakPct, DebtTotalsDto Totals,
    IReadOnlyList<TariffGroupDto> Groups, IReadOnlyList<TariffTeacherDto> Teachers);

/// <summary>Bitta hisob-faktura — bitta <c>MonthlyCharge</c> qatori.</summary>
/// <param name="Date">Hisob yozilgan sana.</param>
/// <param name="Status"><c>paid</c> · <c>partial</c> · <c>unpaid</c>.</param>
/// <param name="OverdueDays">Muddatdan o'tgan kun (to'langan yoki muddati kelmagan bo'lsa 0).</param>
public record InvoiceRowDto(
    string Id, string Date, string Month, string DueDate,
    string StudentId, string StudentName, string GroupId, string GroupName, string CourseName,
    decimal Amount, decimal Discount, decimal Net, decimal Paid, decimal Remaining,
    string Status, int OverdueDays, bool IsArchived);

/// <param name="Totals">BUTUN filtr bo'yicha (faqat sahifa emas).</param>
public record InvoiceListDto(
    string From, string To, int DueDay, int TotalCount, int Page, int PageSize,
    DebtTotalsDto Totals, int PaidCount, int PartialCount, int UnpaidCount, int OverdueCount,
    IReadOnlyList<InvoiceRowDto> Items,
    UnmatchedPaymentsDto Unmatched);
