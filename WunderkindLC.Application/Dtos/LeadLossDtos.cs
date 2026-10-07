namespace WunderkindLC.Application.Dtos;

/* ---------- YO'QOTILGAN LIDLAR: belgilash/tiklash va hisobot ----------
 *
 * Qoidalar — `.claude/rules/crm-leads.md` «YO'QOTILGAN LIDLAR». Hisob-kitob `LeadLossReport` da
 * (sof funksiya, testlangan), belgilash/tiklash — `LeadLoss`.
 *
 * ⚠️ Shakl KLIENT SHARTNOMASI bilan aynan bir xil bo'lishi SHART:
 * `WunderkindLC.Client/src/api/services/leadLoss.ts`. Maydon qo'shish/olib tashlash ikkala
 * tomonda BIRGA qilinadi.
 */

/// <summary><c>PATCH api/admin/leads/{id}/lost</c> tanasi. Sabab MAJBURIY, izoh ixtiyoriy.</summary>
public record MarkLeadLostRequest(string? ReasonId, string? Comment);

/// <summary>
/// <c>PATCH api/admin/leads/{id}/restore</c> tanasi. <paramref name="Stage"/> berilsa lid shu
/// bosqichga qaytadi (kanbanda "Yo'qotilgan" ustunidan boshqa ustunga sudralganda), berilmasa —
/// yo'qotilishdan oldingi o'z bosqichiga.
/// </summary>
public record RestoreLeadRequest(string? Stage);

/// <summary>
/// Belgilash/tiklashdan keyingi lid HOLATI — klient ro'yxatdagi qatorni shu bilan yangilaydi
/// (butun ro'yxatni qayta yuklamasdan). Tiklanganda <c>Lost*</c> maydonlari null.
/// </summary>
public record LeadLossStateDto(
    string Id, string Stage,
    string? LostAt, string? LostReasonId, string? LostReason, string? LostComment, string? LostByName,
    string? AssigneeUserId, string? AssigneeName);

/// <summary>Sabablar kesimi (PIE): <paramref name="Share"/> — yo'qotilganlar ichidagi ulush, %.</summary>
public record LeadLossReasonDto(string Label, int Count, double Share);

/// <summary>
/// Manba yoki mas'ul xodim kesimi: shu guruhga tushgan JAMI lid va ulardan nechtasi yo'qotilgan.
/// <paramref name="Key"/> — manba nomi / xodim id'si (<c>""</c> = biriktirilmagan).
/// </summary>
public record LeadLossGroupDto(string Key, string Label, int Total, int Lost, double Rate);

/// <summary>Oylik oqim: shu oyda KELGAN lidlar va ulardan hozir yo'qotilganlar.</summary>
public record LeadLossMonthDto(string Month, int Total, int Lost, double Rate);

/// <summary>Yo'qotilgan lid qatori (hisobot ostidagi ro'yxat — eng yangisi tepada).</summary>
public record LeadLossItemDto(
    string Id, string FullName, string CreatedAt, string LostAt, string Reason, string Comment,
    string Source, string Manager, string LostBy);

/// <summary>
/// «Yo'qotilgan lidlar» hisoboti. DAVR — lid KELGAN sana (<c>Lead.CreatedAt</c>) bo'yicha,
/// CRM analitikasidagi bilan bir xil: «shu davrda kelgan lidlarning qanchasini yo'qotdik».
/// Ya'ni barcha foizlar bitta to'plamdan va 100% dan oshmaydi.
/// </summary>
public record LeadLossReportDto(
    string From, string To,
    int Total, int Lost, double LostRate, int Converted,
    List<LeadLossReasonDto> ByReason,
    List<LeadLossGroupDto> BySource,
    List<LeadLossGroupDto> ByManager,
    List<LeadLossMonthDto> Monthly,
    List<LeadLossItemDto> Recent,
    /// <summary>Ro'yxatga sig'magan (qirqilgan) yo'qotilgan lidlar soni — jimgina yo'qolmasin.</summary>
    int RecentTruncated);
