namespace WunderkindLC.Application.Dtos;

/* ---------- «Ketish sabablari» (Retention / Loss) hisoboti ---------- */

/// <summary>
/// «Ketish sabablari» hisoboti — <c>GET /api/admin/retention-loss</c>. Hisob-kitob —
/// <c>RetentionLossReport</c> (sof funksiya). "Ketdi" ta'rifi bosh sahifadagi «Ketgan o'quvchilar»
/// kartochkasi bilan AYNAN bir xil (<c>DashboardSummary.LeftEvents</c>).
/// </summary>
/// <param name="From">Davr boshi ("yyyy-MM").</param>
/// <param name="To">Davr oxiri ("yyyy-MM"), kiradi.</param>
/// <param name="Kind">all | active | trial — diagramma va jadval qaysi ketishlarni ko'rsatadi.</param>
public record RetentionLossReportDto(
    string From, string To, string Kind, string? TeacherId, string? CourseId,
    RetentionLossTotalsDto Totals,
    List<RetentionLossReasonDto> Reasons,
    List<RetentionLossTeacherDto> Teachers,
    List<RetentionLossMonthDto> Months,
    List<RetentionLossRowDto> Rows,
    List<RetentionLossOptionDto> TeacherOptions,
    List<RetentionLossOptionDto> CourseOptions);

/// <summary>Jamlanma. <paramref name="Left"/>/<paramref name="Students"/>/<paramref name="OutOfControl"/>/
/// <paramref name="NoReason"/> — tanlangan <c>Kind</c> bo'yicha; <paramref name="LeftActive"/>/
/// <paramref name="LeftTrial"/> — har doim ikkalasi.</summary>
/// <param name="Left">Ketishlar soni (har bir (o'quvchi, kurs) ketishi alohida).</param>
/// <param name="Students">Ketgan TAKRORSIZ o'quvchilar (ikki kursdan ketgan — bitta).</param>
/// <param name="ActiveBase">Davr ichida AKTIV (pullik) bo'lgan (o'quvchi, kurs) juftliklari —
/// ketish foizining maxraji.</param>
/// <param name="LossRate">Aktivdan ketganlar / <paramref name="ActiveBase"/> × 100; maxraj 0 bo'lsa null.</param>
/// <param name="Retention">100 − <paramref name="LossRate"/> (ushlab qolish foizi).</param>
public record RetentionLossTotalsDto(
    int Left, int LeftActive, int LeftTrial, int Students, int OutOfControl, int NoReason,
    int ActiveBase, double? LossRate, double? Retention);

/// <summary>Sabab kesimi (doiraviy diagramma). <paramref name="OutOfControl"/> — "nazoratdan tashqari" sabab.</summary>
public record RetentionLossReasonDto(string Label, int Count, bool OutOfControl);

/// <summary>O'qituvchi kesimi. Ketish o'sha paytda guruhni O'QITGAN o'qituvchiga yoziladi
/// (<c>GroupTeacherHistory</c>), hozirgisiga emas.</summary>
/// <param name="Left">Tanlangan <c>Kind</c> bo'yicha ketishlar.</param>
/// <param name="LossRate">Aktivdan ketganlar / <paramref name="ActiveBase"/> × 100.</param>
public record RetentionLossTeacherDto(
    string TeacherId, string TeacherName, int Left, int LeftActive, int LeftTrial,
    int ActiveBase, double? LossRate);

/// <summary>Oylik dinamika (ustunli diagramma) — ikkala tur ham.</summary>
public record RetentionLossMonthDto(string Month, int LeftActive, int LeftTrial);

/// <summary>Bitta ketish (jadval qatori).</summary>
/// <param name="Date">Ketgan sana — oraliqni yopgan a'zolikning chiqish sanasi.</param>
/// <param name="FromActive">Aktivdan ketgan (<c>false</c> — sinovdan, hech qachon aktivlashmagan).</param>
/// <param name="ReasonSource">log | audit | archive | "" — sabab qayerdan topilgani.</param>
/// <param name="StillStudying">Hozir boshqa guruhda (aktiv yoki sinovda) o'qiyaptimi — ya'ni
/// markazdan emas, faqat shu KURSDAN ketgan.</param>
public record RetentionLossRowDto(
    string StudentId, string StudentName, string GroupId, string GroupName,
    string CourseId, string CourseName, string TeacherId, string TeacherName,
    string Date, bool FromActive, string? ReasonId, string Reason, bool OutOfControl,
    string ReasonSource, bool StillStudying);

/// <summary>Filtr varianti (o'qituvchi yoki kurs).</summary>
public record RetentionLossOptionDto(string Id, string Name);
