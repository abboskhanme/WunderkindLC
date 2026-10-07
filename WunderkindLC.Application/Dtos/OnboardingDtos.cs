namespace WunderkindLC.Application.Dtos;

/* =================================================================================================
 *  MOSLASHUV (onboarding) — 30 kunlik adaptatsiya. API: `api/admin/onboarding/*`
 *  (ruxsat `vacancies.onboarding`). Qoidalar: `.claude/rules/onboarding.md`.
 * ================================================================================================= */

/// <summary>Katalog qadami (andoza).</summary>
public record OnboardingStepDto(
    string Id, string Title, string Description, int DayOffset, int Order, bool IsActive);

/// <summary>Katalog qadamini yaratish/tahrirlash so'rovi.</summary>
public record OnboardingStepPayload(string? Title, string? Description, int DayOffset, bool IsActive = true);

/// <summary>Katalog tartibi — id'lar yangi tartibda.</summary>
public record OnboardingReorderPayload(List<string> Ids);

/// <summary>Xodim moslashuvining bitta qadami.</summary>
/// <param name="DueDate">Muddat ("yyyy-MM-dd") = boshlanish + <paramref name="DayOffset"/> kun.</param>
/// <param name="Overdue">Muddat o'tgan va bajarilmagan (faqat FAOL moslashuvda).</param>
public record OnboardingTaskDto(
    string Id, string StepId, string Title, string Description, int DayOffset, int Order,
    string DueDate, bool Done, string DoneAt, string DoneBy, string Note, bool Overdue);

/// <summary>Moslashuv — ro'yxat qatori (qadamlarsiz) yoki tafsilot (<paramref name="Tasks"/> bilan).</summary>
/// <param name="DayNumber">Boshlanganiga necha kun bo'ldi (1-kun = boshlanish kuni; 30 dan oshishi mumkin).</param>
/// <param name="ProgressPct">Bajarilgan qadamlar ulushi (%, pastga yaxlitlangan — 100 faqat hammasi bajarilsa).</param>
/// <param name="NextTitle">Navbatdagi (eng yaqin muddatli) bajarilmagan qadam nomi.</param>
public record EmployeeOnboardingDto(
    string Id, string FullName, string Phone, string Position,
    string JobApplicationId, int ApplicationNumber, string TeacherId, string StaffUserId,
    string StartDate, string MentorId, string MentorName,
    string Status, string Source, string Note,
    string CompletedAt, string CancelledAt, string CreatedAt, string CreatedBy,
    int Total, int DoneCount, int OverdueCount, int ProgressPct, int DayNumber,
    string NextTitle, string NextDueDate,
    List<OnboardingTaskDto>? Tasks = null);

/// <summary>Moslashuvni qo'lda boshlash / sarlavhasini tahrirlash so'rovi.</summary>
public record EmployeeOnboardingPayload(
    string? FullName, string? Phone, string? Position, string? StartDate,
    string? MentorId, string? TeacherId, string? StaffUserId, string? Note);

/// <summary>Qadamni belgilash/belgini olish (+ izoh).</summary>
public record OnboardingTaskPayload(bool Done, string? Note);

/// <summary>Monitoring tepasidagi jamlanma.</summary>
public record OnboardingSummaryDto(int Active, int Completed, int Cancelled, int WithOverdue, int OverdueTasks);

/// <summary>Mentor/akkaunt tanlash uchun odam (faqat ism — nozik ma'lumot yo'q).</summary>
/// <param name="Kind">"teacher" | "staff".</param>
public record OnboardingPersonDto(string Id, string Kind, string FullName);
