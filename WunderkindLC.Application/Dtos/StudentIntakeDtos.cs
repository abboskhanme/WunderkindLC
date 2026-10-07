namespace WunderkindLC.Application.Dtos;

// ============================ QABUL CHEKLISTI (yangi o'quvchi) ============================
// Hisob — `StudentIntake` (Application/Services). Qoidalar: `.claude/rules/student-intake.md`.
// ⚠️ Hech bir DTO'da fayl MANZILI yo'q (shartnoma PDF/DOCX, rasm) — faqat raqam/sana/holat
// (`uploads-security.md`).

/// <summary>Profilda YETISHMAYOTGAN maydon (kalit + o'zbekcha yorliq — klient yorliqni qayta yozmaydi).</summary>
public record StudentIntakeFieldDto(string Key, string Label);

/// <summary>Qo'lda qo'yilgan belgi: kim, qachon, izoh.</summary>
public record StudentIntakeMarkDto(string Note, string MarkedBy, string MarkedAt);

/// <summary>
/// Cheklistning BITTA bandi.
/// </summary>
/// <param name="Done">Bajarilgan — avtomatik aniqlangan YOKI qo'lda belgilangan.</param>
/// <param name="Auto">Mavjud yozuvlardan AVTOMATIK aniqlandi (qo'lda belgi bo'lsa ham shu ustun).</param>
/// <param name="Detail">Qisqa izoh: "№ 12 · 05.10.2026", "Yetishmaydi: Sinf" va h.k.</param>
/// <param name="Missing">FAQAT <c>profile</c> bandida — to'ldirilmagan maydonlar.</param>
/// <param name="Mark">Qo'lda qo'yilgan belgi (bo'lmasa <c>null</c>).</param>
public record StudentIntakeItemDto(
    string Key, string Label, bool Done, bool Auto, string Detail,
    List<StudentIntakeFieldDto> Missing, StudentIntakeMarkDto? Mark);

/// <summary>
/// Bitta o'quvchining qabul cheklisti.
/// </summary>
/// <param name="Groups">Ko'rsatiladigan guruh nomlari (<c>StudentMembershipView.DisplayGroupNames</c>).</param>
/// <param name="MemberState">A'zolik holati (<c>MembershipLifecycle.MemberState</c>).</param>
/// <param name="Since">Qabul sanasi "yyyy-MM-dd" (<c>EnrollmentDate</c>, bo'sh bo'lsa tizimga kiritilgan kun).</param>
/// <param name="LeadId">O'quvchiga aylantirilgan eng yangi lid (daraja testi yuborish uchun havola). <c>null</c> = lidsiz.</param>
public record StudentIntakeDto(
    string StudentId, string FullName, string Phone, List<string> Groups, string MemberState,
    string Since, int Done, int Total, bool Complete, List<StudentIntakeItemDto> Items, string? LeadId);

/// <summary>Profil kartasi uchun: cheklist + Telegram bot nomi (ota-onaga "botga kiring" havolasi).</summary>
public record StudentIntakeDetailDto(StudentIntakeDto Checklist, string TelegramBot);

/// <summary>
/// "Qabul cheklisti" ro'yxati — yangi o'quvchilar kesimi.
/// </summary>
/// <param name="Days">Qamrov oynasi (kun) — shu muddatda qabul qilinganlar + SINOVDAGILARNING hammasi.</param>
/// <param name="From">Oyna boshlanishi "yyyy-MM-dd".</param>
/// <param name="Total">Qamrovdagi o'quvchilar soni (qirqilgunga qadar).</param>
/// <param name="Incomplete">Cheklisti TO'LIQ BO'LMAGANLAR soni.</param>
/// <param name="PendingByItem">Har band bo'yicha — nechta o'quvchida BAJARILMAGAN.</param>
/// <param name="Truncated">Ro'yxat <c>StudentIntake.MaxRows</c> da qirqildi (eng yangilari qoldi).</param>
public record StudentIntakeListDto(
    List<StudentIntakeDto> Items, int Days, string From, int Total, int Incomplete,
    Dictionary<string, int> PendingByItem, bool Truncated);

/// <summary>Qo'lda belgi: <c>Done=true</c> — qo'yish (izoh bilan), <c>false</c> — olib tashlash.</summary>
public record SetStudentIntakeMarkRequest(bool Done, string? Note);
