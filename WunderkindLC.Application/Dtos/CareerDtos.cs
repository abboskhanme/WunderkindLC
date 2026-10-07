namespace WunderkindLC.Application.Dtos;

/* =================================================================================================
 *  KARYERA (Wunderkind Career) — DTO'lar
 *  Admin API: `api/admin/career/*`  ·  Mini App API: `api/career/*` (autentifikatsiyasiz, initData)
 * ================================================================================================= */

/* ---------- Biz haqimizda ---------- */

/// <summary>"Biz haqimizda" bo'limi — Mini App'ning birinchi ekrani (admin to'ldiradi).</summary>
public record CareerAboutDto(
    string Title, string Tagline, string About, string Benefits, string LogoUrl,
    string Address, string Landmark, string MapUrl, string WorkTime,
    string Phone, string Phone2, string Email,
    string Telegram, string Instagram, string Facebook, string Youtube, string Tiktok, string Website,
    string UpdatedAt, string UpdatedBy);

/// <summary>"Biz haqimizda"ni saqlash so'rovi (barcha maydonlar ixtiyoriy — bo'sh qoldirilsa ko'rinmaydi).</summary>
public record CareerAboutPayload(
    string? Title, string? Tagline, string? About, string? Benefits, string? LogoUrl,
    string? Address, string? Landmark, string? MapUrl, string? WorkTime,
    string? Phone, string? Phone2, string? Email,
    string? Telegram, string? Instagram, string? Facebook, string? Youtube, string? Tiktok, string? Website);

/* ---------- Vakansiya ---------- */

/// <summary>Vakansiya (admin ko'rinishi — arizalar soni bilan).</summary>
/// <param name="ApplicationCount">Shu vakansiyaga tushgan jami ariza soni.</param>
/// <param name="NewCount">Ulardan hali ko'rilmagani ("new" bosqichida).</param>
public record VacancyDto(
    string Id, string Title, string Department, string EmploymentType, string Location,
    decimal SalaryFrom, decimal SalaryTo, string SalaryNote,
    string Description, string Requirements, string Responsibilities, string Conditions,
    string Status, string Deadline, int Order,
    string CreatedAt, string CreatedBy, string ArchivedAt, string ArchivedBy,
    int ApplicationCount, int NewCount);

/// <summary>Vakansiya yaratish/tahrirlash so'rovi.</summary>
public record VacancyPayload(
    string? Title, string? Department, string? EmploymentType, string? Location,
    decimal SalaryFrom, decimal SalaryTo, string? SalaryNote,
    string? Description, string? Requirements, string? Responsibilities, string? Conditions,
    string? Deadline, int Order);

/* ---------- Ariza ---------- */

/// <summary>Bosqich katalogi elementi (admin va Mini App bir xil ro'yxatni ishlatadi).</summary>
/// <param name="IsLegacy">ESKI bosqich (masalan <c>review</c>): mavjud arizalarda ko'rsatiladi, lekin
/// yangi o'tish uchun taklif qilinmaydi va yo'l-xaritaga alohida qadam bo'lib chizilmaydi.</param>
public record CareerStageDto(string Key, string Label, string CandidateText, string Icon, int Order, bool IsFinal,
    bool IsLegacy = false);

/// <summary>Ariza bosqichi tarixidagi bitta yozuv.</summary>
public record JobApplicationEventDto(string Status, string Note, string CreatedAt, string CreatedBy);

/// <summary>Ariza — ADMIN ko'rinishi (barcha maydonlar, ichki izoh bilan).</summary>
public record JobApplicationDto(
    string Id, int Number, string VacancyId, string VacancyTitle,
    long ChatId, string TgUsername,
    string FullName, string Phone, string Experience, string Motivation,
    string CvUrl, string CvName,
    string Status, string StatusNote, string StatusChangedAt, string StatusChangedBy,
    string AdminNote, string CreatedAt,
    List<JobApplicationEventDto>? History = null);

/// <summary>Bosqichni o'zgartirish so'rovi (izoh nomzodga ko'rinadi).</summary>
public record JobApplicationStatusPayload(string Status, string? Note);

/// <summary>Faqat admin ko'radigan ichki izoh.</summary>
public record JobApplicationNotePayload(string? AdminNote);

/// <summary>Arizalar bo'limining tepasidagi jamlanma (bosqich bo'yicha son + voronka konversiyasi).</summary>
public record CareerStatsDto(int Total, int Active, int Hired, int Rejected, Dictionary<string, int> ByStatus,
    List<CareerFunnelStepDto>? Funnel = null);

/// <summary>Voronka qadami: shu bosqichgacha yetib borganlar va o'tish ulushi
/// (hisob — <c>CareerService.ComputeFunnel</c>).</summary>
/// <param name="Reached">Shu bosqichga (yoki undan keyingisiga) yetib borgan arizalar.</param>
/// <param name="Current">Hozir aynan shu bosqichda turganlar.</param>
/// <param name="RejectedHere">Shu bosqichda to'xtab, rad etilganlar.</param>
/// <param name="FromPrevPct">Oldingi bosqichdan o'tish ulushi (%).</param>
/// <param name="FromStartPct">Jami arizalardan ulush (%).</param>
public record CareerFunnelStepDto(string Key, string Label, string Icon, int Order,
    int Reached, int Current, int RejectedHere, int FromPrevPct, int FromStartPct);

/// <summary>Vakansiyani bitta tugma bilan yoqish/o'chirish (ON/OFF).</summary>
public record VacancyActivePayload(bool Active);

/* =================================================================================================
 *  MINI APP (nomzod tomoni) — qisqartirilgan, maxfiy maydonlarsiz DTO'lar
 * ================================================================================================= */

/// <summary>Mini App uchun vakansiya (faqat faol vakansiyalar chiqadi).</summary>
/// <param name="Applied">Joriy foydalanuvchi bu vakansiyaga ariza yuborganmi.</param>
/// <param name="Expired">Ariza qabul qilish muddati o'tganmi.</param>
public record PublicVacancyDto(
    string Id, string Title, string Department, string EmploymentType, string Location,
    string Salary, string Description, string Requirements, string Responsibilities, string Conditions,
    string Deadline, bool Expired, bool Applied, string CreatedAt);

/// <summary>Mini App uchun ariza — nomzodning O'Z arizasi (ichki izohsiz).</summary>
public record PublicApplicationDto(
    string Id, int Number, string VacancyTitle,
    string Status, string StatusLabel, string StatusIcon, string StatusText, string StatusNote,
    string CreatedAt, string StatusChangedAt,
    List<JobApplicationEventDto> History);

/// <summary>Mini App ochilganda BIR SO'ROVDA keladigan boshlang'ich holat.</summary>
/// <param name="Authenticated">Telegram imzosi tekshirildimi (brauzerdan ochilsa — false: faqat ko'rish).</param>
public record CareerBootstrapDto(
    bool Authenticated, string Name, string Phone,
    CareerAboutDto About, List<PublicVacancyDto> Vacancies, List<PublicApplicationDto> Applications,
    List<CareerStageDto> Stages);

/// <summary>Mini App'dan ariza yuborish so'rovi.</summary>
public record PublicApplyPayload(
    string VacancyId, string? FullName, string? Phone, string? Experience, string? Motivation,
    string? CvUrl, string? CvName);
