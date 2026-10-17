namespace WunderkindLC.Application.Dtos;

/* ---------- DAVOMAT — DAVR TAHLILI (SPEC-backlog-2026-10-17 A1, TZ 2.1) ----------
 *
 * Sanoqlar YAGONA davomat ta'rifidan (`AttendanceRules` / `MembershipAttendance`, journal.md «YAGONA DAVOMAT
 * TA'RIFI»): kech = keldi; belgilanmagan dars foizga KIRMAYDI.
 * ⚠️ Shakl klient bilan bir xil: `WunderkindLC.Client/src/api/services/attendancePeriod.ts`.
 */

/// <param name="Held">Foiz maxraji: keldi + kech + sababli + sababsiz.</param>
/// <param name="Percent">round((keldi + kech) × 100 ÷ held); dars bo'lmasa null.</param>
public record AttendanceCountsDto(int Held, int Present, int Late, int Excused, int Unexcused, int? Percent);

/// <param name="DeltaPp">Joriy foiz − oldingi davr foizi (foiz punkt); biri yo'q bo'lsa null.</param>
/// <param name="Dropped">DeltaPp ≤ −<c>AttendancePeriodReport.DropPp</c>.</param>
public record AttendancePeriodGroupDto(
    string GroupId, string GroupName, string CourseName, string TeacherName, int Students,
    AttendanceCountsDto Current, AttendanceCountsDto Previous, int? DeltaPp, bool Dropped);

/// <param name="Count">Shu sabab bilan belgilangan dars kataklari.</param>
public record AttendanceReasonStatDto(string ReasonId, string Name, bool IsExcused, bool IsLate, int Count, int Students);

/// <param name="Grade">Maktab sinfi (12 — bitirgan); null — kiritilmagan.</param>
public record AttendanceGradeStatDto(int? Grade, string Label, int Students, AttendanceCountsDto Counts);

/// <summary>Davrda ko'p dars qoldirgan o'quvchi (TZ «Qizil ro'yxat» — jami 3+ / 5+).</summary>
public record AttendanceRiskStudentDto(
    string StudentId, string StudentName, string Phone, IReadOnlyList<string> Groups,
    int Held, int Absent, int Excused, int Unexcused, int Late, int? Percent);

public record AttendancePeriodDto(
    string From, string To, string PrevFrom, string PrevTo, int Days, int DropPp,
    AttendanceCountsDto Total, AttendanceCountsDto PreviousTotal,
    IReadOnlyList<AttendancePeriodGroupDto> Groups,
    IReadOnlyList<AttendanceReasonStatDto> Reasons,
    IReadOnlyList<AttendanceGradeStatDto> Grades,
    IReadOnlyList<AttendanceRiskStudentDto> AtRisk);
