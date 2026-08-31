namespace IntellectCRM.Application.Dtos;

using System.ComponentModel.DataAnnotations;
using IntellectCRM.Domain;

// Frontend servislari kutadigan so'rov (request) va javob (response) shakllari.
// JSON camelCase'ga ASP.NET Core standart sozlamasi orqali aylantiriladi.

/* ---------- Auth ---------- */
/// <summary>
/// Login so'rovi. <paramref name="DeviceId"/> va qolgan qurilma maydonlari — MOBIL ILOVA uchun
/// (yuz bilan kirish: "yangi qurilmami?"). ⚠️ Ular IXTIYORIY: eski klientlar (web SPA va
/// yangilanmagan ilovalar) ularni yubormaydi va ular uchun xatti-harakat HOZIRGIDEK qoladi —
/// yuz so'ralmaydi (batafsil: <c>FaceLoginService.DecideAsync</c>).
/// </summary>
public record LoginRequest(
    string Email, string Password,
    string? DeviceId = null, string? DeviceName = null,
    string? Platform = null, string? AppVersion = null);
/// <summary>
/// Bot orqali olingan bir martalik kirish kodi bilan login (parol o'rniga).
///
/// <para>⚠️ Qurilma maydonlari <see cref="LoginRequest"/> dagi bilan AYNAN bir xil sabab bilan
/// bor: OTP ham LOGIN yo'li, ya'ni yuz tasdig'i darvozasi unga ham tegishli. Ular bo'lmaganda
/// modul YOQILGAN bo'lsa ham "bot orqali kod olib kirish" selfini butunlay chetlab o'tardi.
/// Maydonlar IXTIYORIY — eski klientlar uchun xatti-harakat o'zgarmaydi.</para>
/// </summary>
public record OtpLoginRequest(
    string Code,
    string? DeviceId = null, string? DeviceName = null,
    string? Platform = null, string? AppVersion = null);
public record UserDto(
    string Id, string FullName, string Role, string Email, string? AvatarUrl,
    List<string>? Permissions = null, string Phone = "");
/// <summary>
/// Login javobi. <paramref name="FaceRequired"/> true bo'lsa <paramref name="Token"/> — CHEKLANGAN
/// token (faqat yuz tasdiqlash endpointlariga yetadi), ilova selfi ekranini ochishi kerak.
/// Eski klientlar bu ikki maydonni e'tiborsiz qoldiradi (JSON'da qo'shimcha maydon).
/// </summary>
/// <param name="FaceStatus">enroll (etalon yo'q — birinchi marta) | verify.</param>
/// <param name="RefreshToken">Uzoq muddatli (30 kun) refresh token — MOBIL uchun (web uni cookie'dan
/// oladi va e'tiborsiz qoldiradi). Access token (60 daq) eskirganda <c>POST /api/auth/refresh</c> ga
/// yuboriladi. FaceRequired javobida bo'lmaydi (cheklangan token to'liq sessiya emas).</param>
public record LoginResponse(
    string Token, UserDto User, bool FaceRequired = false, string? FaceStatus = null,
    string? RefreshToken = null);
/// <summary>Refresh so'rovi (MOBIL). Web'da <c>rt</c> cookie ishlatiladi, body kerak emas.
/// Nullable — cookie bilan kelganda body umuman bo'lmasligi mumkin.</summary>
public record RefreshRequest(string? RefreshToken = null);
/// <summary>O'quvchi/o'qituvchiga biriktirilgan tizim akkaunti ma'lumotlari (admin uchun).</summary>
public record CredentialsDto(string Login, string Password, string Role);
/// <summary>Joriy foydalanuvchi o'z login (email) va/yoki parolini o'zgartirishi uchun.
/// NewPassword bo'sh bo'lsa — parol o'zgarmaydi. CurrentPassword har doim talab qilinadi.
/// <paramref name="Phone"/> — ixtiyoriy, kiritilsa PhoneUtil.Normalize() orqali standartlashtirilib saqlanadi
/// (format: +998-XX-XXX-XX-XX; maksimum 32 belgi).</summary>
public record UpdateAccountRequest(string? Email, string CurrentPassword, string? NewPassword, string? Phone = null);
/// <summary>O'quvchi/ota-ona ilova ichida o'z parolini almashtirishi uchun.
/// Joriy parol bilan tasdiqlanadi; yangi parol kamida 8 belgi.</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/* ---------- Students ---------- */
/// <summary>
/// O'quvchi yaratish/tahrirlash so'rovi. FISH alohida-alohida kiritiladi (LastName/FirstName/MiddleName);
/// agar bo'lsa, ulardan FullName yig'iladi. Ota-ona FISH ham alohida.
/// FullName/ParentFullName ixtiyoriy — yo'q bo'lsa parts'dan yig'iladi.
///
/// TELEFON VALIDATSIYA (PhoneUtil.Normalize orqali standartlashtirilib saqlanadi):
/// - <paramref name="Phone"/> — o'quvchi o'z raqami (ixtiyoriy, max 32 belgi); format: +998-XX-XXX-XX-XX
/// - <paramref name="FatherPhone"/> — ota raqami (ixtiyoriy, max 32 belgi)
/// - <paramref name="MotherPhone"/> — ona raqami (ixtiyoriy, max 32 belgi)
/// - <paramref name="ParentPhone"/> — asosiy ota-ona kontakti (ixtiyoriy, max 32 belgi);
///   tahrirda fatherPhone/motherPhone bo'lsa ular asosiy kontakt uchun ishlatiladi
/// Raqamlar ixtiyoriy; kiritilsa kamida 7 ta raqam bo'lishi kerak va 998 prefiksini o'z ichiga olishi kerak yoki
/// avtomatik 998 prefiksi qo'shiladi. Standartlashtirilgan format: +998-XX-XXX-XX-XX.
/// </summary>
public record StudentPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string? ParentFullName, string? ParentPhone, string ClassName, string? EnrollmentDate,
    string? NewPassword = null,
    int? DiscountPct = null, decimal? DiscountAmount = null, string? DiscountNote = null,
    string? LastName = null, string? FirstName = null, string? MiddleName = null,
    string? BirthCertificateUrl = null,
    string? ParentLastName = null, string? ParentFirstName = null, string? ParentMiddleName = null,
    string? ParentPassportUrl = null,
    string? Phone = null,
    string? FatherFullName = null, string? FatherPhone = null,
    string? MotherFullName = null, string? MotherPhone = null,
    string? DiscountStartMonth = null, string? DiscountEndMonth = null,
    string? DistrictId = null, string? SchoolId = null,
    string? DiscountGroupId = null,
    /// <summary>Ushlab turish bonusi ptichkasi. null = tegilmaydi (eski chaqiruvlar buzilmaydi).</summary>
    bool? RetentionBonus = null,
    /// <summary>Bonus sanog'i boshlanadigan oy "YYYY-MM" (admin QO'LDA kiritadi). null = tegilmaydi.</summary>
    string? RetentionBonusStartMonth = null);
/// <summary>O'quvchi to'lovi kiritish so'rovi. <paramref name="Date"/> — to'lov haqiqatan sodir bo'lgan
/// sana (ISO "YYYY-MM-DD"), ixtiyoriy — bo'sh bo'lsa bugungi sana ishlatiladi (masalan bugun to'lagan,
/// lekin tizimga ertaga kiritilayotgan to'lov uchun eski sanani tanlash imkoni).</summary>
/// <summary>O'quvchiga to'lov kiritish. <paramref name="ReceiptNo"/> — NAQD to'lovda qog'oz kvitansiya
/// raqami ("KV" seriyasi + raqam), <paramref name="PaidTime"/> — KARTA to'lovida haqiqiy to'lov vaqti "HH:mm".</summary>
public record PaymentRequest(decimal Amount, string? Month, string? GroupId = null, string? Comment = null,
    string? Method = null, string? Date = null, string? ReceiptNo = null, string? PaidTime = null,
    /// <summary>Kvitansiya raqami band bo'lsa ham SAQLASH (kassir "Baribir saqlash"ni bosgan) —
    /// haqiqatan takroriy blank ishlatilgan holatlar uchun. Auditda alohida qayd etiladi.</summary>
    bool ForceReceipt = false,
    /// <summary>KARTA to'lovida — karta raqamining oxirgi 4 raqami (faqat shu qismi saqlanadi).</summary>
    string? CardLast4 = null);

/// <summary>
/// ALLAQACHON kiritilgan kvitansiya raqami haqidagi ma'lumot — bitta qog'oz blank ikki marta
/// yozilmasligi uchun. To'lov saqlanayotganda shu raqam band bo'lsa server 409 Conflict bilan
/// shu kartochkani qaytaradi (kim to'lagan, qaysi guruh/o'qituvchi, qancha, qachon kiritilgan).
/// </summary>
public record DuplicateReceiptDto(
    string ReceiptNo, string TransactionId, string? StudentId, string StudentName,
    string GroupName, string CourseName, string TeacherName,
    decimal Amount, string Date, string Month, string Method,
    string CreatedBy, DateTime CreatedAt);

/* ---------- Tuman + maktab (sozlamalar) ---------- */
public record DistrictDto(string Id, string Name, int Order, List<SchoolDto> Schools);
public record SchoolDto(string Id, string DistrictId, string Name, int Order);
public record DistrictCreate(string? Name);
public record DistrictUpdate(string? Name);
public record SchoolCreate(string? Name);
public record SchoolUpdate(string? Name);

/* ---------- AI tekshiruv (Speaking / Writing) ---------- */
/// <summary>Gemini strukturali tahlil natijasi (diagramma + tuzatish + so'z tahlili).</summary>
public record AiCheckScoresDto(int Grammar, int Vocabulary, int Coherence, int Task, int Mechanics, int Pronunciation, int Fluency);
public record AiCorrectionDto(string Original, string Suggestion, string Explanation);
public record AiVocabDto(string Word, string Suggestion, string Note);
public record AiCheckAnalysisDto(
    int Overall, string Level, AiCheckScoresDto Scores, string Summary,
    List<string> Strengths, List<string> Weaknesses,
    List<AiCorrectionDto> Corrections, List<AiVocabDto> Vocabulary,
    string Improved, List<string> Recommendations,
    AiCheckIeltsDto? Ielts = null);

/// <summary>
/// IELTS Writing band bahosi (0-9, 0.5 qadam) — 4 mezon + umumiy band. Task1: TaskAchievement,
/// Task2: TaskResponse (ikkalasi ham <see cref="Task"/> da). Faqat IELTS task tanlansa to'ladi.
/// </summary>
public record AiCheckIeltsDto(
    double Task, double Coherence, double Lexical, double Grammar, double Overall, string TaskType);

/// <summary>Speaking uchun Azure talaffuz bahosi (frontend diagramma uchun) — ixtiyoriy.</summary>
public record AiCheckSpeechDto(
    string RecognizedText, double PronScore, double Accuracy, double Fluency,
    double Completeness, double Prosody, List<SpeakingWordDto> Words);

/// <summary>To'liq AI tekshiruv yozuvi (o'quvchi/admin ko'rinishi).</summary>
public record AiCheckDto(
    string Id, string Type, string Prompt, string InputText, string RecognizedText,
    string AudioUrl, double Score, string Date, string CreatedAt,
    AiCheckAnalysisDto? Analysis, AiCheckSpeechDto? Speech, string TaskType = "");

/// <summary>Tarix elementi (ro'yxat — yengil).</summary>
public record AiCheckListItemDto(string Id, string Type, string Prompt, double Score, string Date, string CreatedAt, bool HasAudio);

/// <summary>O'quvchi AI tekshiruv holati (limit/premium/blok + bugungi foydalanish).
/// <paramref name="Enabled"/> — bo'lim markaz tomonidan ilovada ochilganmi (CenterMeta.AiCheckEnabled).</summary>
public record AiCheckStatusDto(
    bool GeminiReady, bool AzureReady, bool Premium, bool Blocked,
    int Limit, int UsedToday, int Remaining, bool Enabled = false);

/// <summary>TaskType: "" (umumiy) | "ielts_task1" | "ielts_task2".</summary>
public record AiCheckWritingRequest(string? Prompt, string? Text, string? TaskType = null);

/* ---------- AI tekshiruv — admin ---------- */
public record AiCheckOverviewRowDto(
    string StudentId, string FullName, string ClassName,
    int SpeakingCount, int WritingCount, int Total, int TodayUsed,
    int EffectiveLimit, bool Premium, bool Blocked);
/// <summary><paramref name="Enabled"/> — o'quvchi ilovasida bo'lim ochilganmi.</summary>
public record AiCheckSettingsDto(int DefaultDailyLimit, bool Enabled = false);
public record SaveAiCheckSettingsRequest(int DailyLimit);
/// <summary>Bo'limni ilovada ochish/yopish (ALOHIDA endpoint — limitni saqlash bayroqqa tegmasin).</summary>
public record SetAiCheckEnabledRequest(bool Enabled);
public record SaveAiAccessRequest(int DailyLimit, bool IsPremium, bool IsBlocked);

/* ---------- Telefon dublikatini tekshirish (o'quvchi/ota-ona raqami) ---------- */
public record CheckPhonesRequest(
    string? Phone, string? FatherPhone, string? MotherPhone, string? ParentPhone, string? ExcludeId);
/// <summary>Mos kelgan mavjud o'quvchi: <paramref name="Phone"/> — kiritilgan (mos kelgan) raqam,
/// <paramref name="Role"/> — mavjud yozuvda qaysi raqam (O'quvchi/Ota/Ona/Ota-ona).</summary>
public record PhoneMatchDto(
    string Phone, string StudentId, string FullName, string ClassName, bool IsArchived, string Role);

/* ---------- Excel'dan ommaviy import ---------- */
public record StudentImportRowErrorDto(int Row, string Message);
public record StudentImportResultDto(int Created, int Failed, int Skipped, List<StudentImportRowErrorDto> Errors);

/// <summary>Tanlangan o'quvchilarni Excel'ga eksport qilish so'rovi.</summary>
public record StudentExportSelectedRequest(List<string> StudentIds);

/* ---------- O'quv dasturi Excel importi (Modul→Mavzu→Dars skeletini yaratadi, Topshiriq YO'Q —
   ular keyin qo'lda, tezkor ko'p-yaratish paneli orqali qo'shiladi) ---------- */
public record CurriculumExcelImportResultDto(
    int Modules, int Topics, int Lessons, int Skipped, List<StudentImportRowErrorDto> Errors);

/* ---------- Teachers ---------- */
/// <summary>O'qituvchi yaratish/tahrirlash so'rovi.
/// <paramref name="Phone"/> — o'qituvchi telefoni (ixtiyoriy, max 32 belgi);
/// PhoneUtil.Normalize() orqali standartlashtirilib saqlanadi (format: +998-XX-XXX-XX-XX).
/// Kiritilsa kamita 7 ta raqam bo'lishi kerak. 998 prefiksi avtomatik qo'shiladi.
/// </summary>
public record TeacherPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string HomeroomClass, List<string> SubjectIds, decimal Salary, string? SalaryStartMonth,
    string? NewPassword = null, List<string>? Permissions = null, string? Phone = null,
    string? PhotoUrl = null, string? Category = null, string? SalaryStartDate = null,
    string? SalaryMode = null, decimal SalaryPercent = 0, bool IsSupport = false);
/// <summary>
/// Bitta oyda bitta guruhning jurnal holati (maosh ushlanmasini tushuntirish uchun):
/// rejadagi darslar (guruh kunlari bo'yicha), "o'tildi" belgilangani, belgilanmagani va shu sababli
/// ushlangan summa. <paramref name="MissedDates"/> — belgilanmagan dars sanalari ("yyyy-MM-dd").
/// </summary>
public record SalaryLessonStatDto(
    string GroupId, string GroupName, int Planned, int Conducted, int Missed,
    decimal Deduction, List<string> MissedDates);
/// <summary>
/// Oy bo'yicha maosh. <paramref name="Expected"/> — YAKUNIY (ushlanmadan keyingi) summa.
/// <paramref name="BaseExpected"/> — ushlanmagacha bo'lgan summa; <paramref name="Deduction"/> — jurnalda
/// belgilanmagan darslar uchun ushlanma (CenterMeta.SalaryRequireJournal yoqilgan bo'lsa).
/// </summary>
/// <param name="Collected">FOIZLI maosh bazasi — shu OY UCHUN o'qituvchi guruhlaridan yig'ilgan
/// tuition summasi (vozvrat ayrilgan). Qat'iy maoshda 0. Shu raqam ko'rsatilgani uchun o'qituvchi
/// "Hisoblandi" qayerdan chiqqanini ko'radi: yig'ilgan × foiz = hisoblangan.</param>
/// <param name="Charged">O'quvchilarga SHU OY UCHUN HISOBLANGAN (chegirma ayrilgan, qarz ham kiradi)
/// tuition summasi — o'qituvchi guruhlari bo'yicha. Qat'iy maoshda 0.</param>
/// <param name="PotentialExpected">"Hammasi to'lansa" maosh qancha bo'lardi (hisoblangan × foiz,
/// jurnal ushlanmasi ayrilgan). Qat'iy maoshda <paramref name="Expected"/> ga teng.
/// <para>NEGA KERAK: guruh yopilib o'quvchilar muzlatilganda o'sha oyning puli hali yig'ilmagan
/// bo'lishi mumkin va foizli maosh 0 bo'lib ko'rinardi — o'qituvchi/admin "oylik yozilmadi" deb
/// o'ylardi. Endi yonida "hisoblangan bo'yicha" raqami turadi va pul kelgani sari
/// <paramref name="Expected"/> unga yaqinlashadi.</para></param>
/// <param name="TuitionCharged">TUSHUM REJASI — o'qituvchining BARCHA guruhlarida shu oy uchun
/// o'quvchilarga hisoblangan (chegirma ayrilgan) tuition summasi: "aslida qancha tushum bo'lishi
/// kerak edi". <see cref="SalaryLedger.BuildAsync"/> ga <c>withRevenue: true</c> berilgandagina
/// to'ldiriladi (aks holda 0).</param>
/// <param name="TuitionCollected">Shu oy uchun HAQIQATDA yig'ilgan tuition (vozvrat ayrilgan) —
/// o'qituvchining BARCHA guruhlari bo'yicha. Farqi (<paramref name="TuitionCharged"/> −
/// <paramref name="TuitionCollected"/>) = shu oy bo'yicha yig'ilmagan qarz.
/// <para>⚠️ <paramref name="Collected"/>/<paramref name="Charged"/> bilan ADASHTIRMANG: ular MAOSH
/// bazasi (faqat foizli ulushi bor guruhlar), bu ikkisi esa markazning TUSHUMI (hamma guruh).
/// Barcha guruhlari foizli bo'lgan o'qituvchida ular teng chiqadi, aralash sozlamada esa farq
/// qiladi.</para></param>
public record MonthSalaryDto(
    string Month, decimal Expected, decimal Paid, decimal Remaining, string Status,
    decimal BaseExpected = 0, decimal Deduction = 0,
    int PlannedLessons = 0, int ConductedLessons = 0, int MissedLessons = 0,
    List<SalaryLessonStatDto>? Lessons = null, decimal Collected = 0,
    decimal Charged = 0, decimal PotentialExpected = 0,
    decimal TuitionCharged = 0, decimal TuitionCollected = 0,
    decimal SubstituteFee = 0, decimal SubstituteDeduction = 0);
/// <summary>
/// Maosh hisobida bitta guruhning ulushi (davr bo'yicha): qaysi rejim (foiz/qat'iy), qiymati,
/// shu davrda guruhdan yig'ilgan to'lov bazasi va shu guruh keltirgan hisoblangan maosh.
/// </summary>
public record GroupSalaryLineDto(
    string GroupId, string GroupName, string CourseName, decimal MonthlyFee,
    string Mode, decimal Percent, decimal Fixed,
    decimal PeriodCollected, decimal PeriodExpected,
    decimal SubstituteDeduction = 0, int SubstitutedLessons = 0);
public record SalaryLedgerDto(
    string TeacherId, string FullName, decimal Salary,
    decimal TotalExpected, decimal TotalPaid, decimal Remaining,
    List<MonthSalaryDto> Months, List<PaymentDto> Payments,
    string SalaryMode = "fixed", decimal SalaryPercent = 0,
    List<GroupSalaryLineDto>? Groups = null,
    decimal TotalDeduction = 0, bool JournalLinked = false);

/// <summary>O'qituvchi guruhlari maosh sozlamasini yangilash (per-guruh foiz/qat'iy summa).</summary>
public record GroupSalaryItemDto(string GroupId, string Mode, decimal Percent, decimal Fixed);
public record GroupSalaryUpdateRequest(List<GroupSalaryItemDto> Items);
public record SalaryReportRowDto(
    string TeacherId, string TeacherName, decimal Salary, decimal TotalPaid, int PaymentsCount,
    int Months, decimal Expected, decimal Remaining,
    string SalaryMode = "fixed", decimal SalaryPercent = 0,
    decimal Deduction = 0, int MissedLessons = 0);

/// <summary>O'qituvchilar davomati — oylik board (o'qituvchilar + belgilangan kunlar).</summary>
public record TeacherNameDto(string Id, string FullName, string StartDate = "");
public record TeacherAttendanceDto(string TeacherId, string Date, string Status, string Note);
public record TeacherAttendanceBoardDto(
    List<TeacherNameDto> Teachers, List<TeacherAttendanceDto> Entries);
public record SetTeacherAttendanceRequest(string TeacherId, string Date, string? Status, string? Note);
/// <summary>Bitta kun uchun BARCHA faol o'qituvchini belgilash (status bo'sh = o'sha kun tozalanadi).</summary>
public record SetTeacherAttendanceDayRequest(string Date, string? Status);

// ---------- Turniket/FaceID: o'qituvchilar davomati dashboard ----------
/// <summary>Dashboard bitta o'qituvchi qatori (kunlik).</summary>
public record TeacherDashboardRowDto(
    string TeacherId, string FullName, string? PhotoUrl, string DeviceUserId,
    string Status, string CheckIn, string CheckOut, string Expected, int LateMinutes, string Source);
/// <summary>Kunlik davomat jamlamasi.</summary>
public record AttendanceSummaryDto(int Total, int Present, int Late, int Absent, int NotArrived);
/// <summary>O'qituvchilar davomati dashboard (tanlangan kun).</summary>
public record TeacherAttendanceDashboardDto(
    string Date, bool TurnstileEnabled, string LastSync,
    AttendanceSummaryDto Summary, List<TeacherDashboardRowDto> Rows);
/// <summary>Sinxronlash natijasi.</summary>
public record TurnstileSyncResultDto(bool Ok, string Message, int EventsFetched, int Updated, string LastSync);

// ---------- Turniket: BITTA o'quvchining kirgan/chiqqan tarixi ----------
// ⚠️ Ilgari bu yerda "barcha o'quvchilar bir kunda" dashboardi turardi. U OLIB TASHLANDI:
// turniket tarixi endi o'quvchi PROFILIDA ko'rsatiladi — savol "bugun kim keldi" emas,
// "shu o'quvchi qachon kirgan-chiqqan" (profildagi qolgan tarix bilan bir joyda).
/// <summary>Bitta o'tish hodisasi: vaqt ("HH:mm"), yo'nalish ("in"|"out"), qaysi eshik.</summary>
public record StudentTurnstileEventDto(string Time, string Direction, string DeviceName);
/// <summary>
/// O'quvchining turniket tarixi: tanlangan KUN hodisalari + kalendar uchun shu OYdagi faol kunlar.
/// <para><c>Enabled</c>/<c>LastSync</c> — integratsiya holati (sahifada "sinxronlash" tugmasi uchun).</para>
/// <para>Qurilma biriktirilmagan bo'lsa <c>DeviceUserId</c> bo'sh va sanoqlar nol — bu XATO emas,
/// frontend "qurilma biriktirilmagan" deb ko'rsatadi.</para>
/// </summary>
public record StudentTurnstileHistoryDto(
    string Date, string Month, bool Enabled, string LastSync,
    string DeviceUserId, int Passes, string FirstIn, string LastOut,
    List<StudentTurnstileEventDto> Events, List<string> ActiveDays);
/// <summary>O'quvchiga qurilma (turniket) ID biriktirish.</summary>
public record SetStudentDeviceRequest(string StudentId, string? DeviceUserId);
// ---------- Kamera (videokuzatuv) ----------
public record CameraDto(
    string Id, string Name, string Location, string RtspUrl, string RtspSubUrl,
    int RetentionDays, bool IsActive, string Note);
public record SaveCameraRequest(
    string Name, string? Location, string RtspUrl, string? RtspSubUrl,
    int RetentionDays = 7, bool IsActive = true, string? Note = null);
/// <summary>Kamera integratsiya sozlamasi.</summary>
public record CameraSettingsDto(bool Enabled, int CameraCount);
public record SaveCameraSettingsRequest(bool Enabled);

/* ---------- Avto xabarlar (yagona SMS+Push+Telegram model) — Xabarlar → Avto xabarlar ---------- */

/// <summary>Qaysi kanallar shu hodisada mavjud (frontend faqat shu toggle'larni ko'rsatadi).</summary>
public record AutoMessageChannelsDto(bool Sms, bool Push, bool Telegram);
/// <summary>Avto-xabar hodisasi katalogidagi bitta yozuv (frontend forma shundan quriladi).
/// Category — guruhlash toifasi ("Lidlar" | "O'quv jarayoni" | "Moliya" | "Boshqa").</summary>
public record AutoMessageTriggerInfoDto(
    string Key, string Label, string Description, string[] Tokens, AutoMessageChannelsDto Channels,
    bool SupportsSchedule, bool SupportsSendScope, string[] Audiences, string DefaultAudience,
    string DefaultTemplate, string Category, bool TemplateOptional = false);
/// <summary>Xabar {token}i katalogdagi bitta yozuv. Group: "student" | "lead" | "common" | "event".</summary>
public record MessageTokenDto(string Token, string Label, string Group);
/// <summary>Avto-xabar qoidasi (o'qish uchun). SmsProvider: "eskiz" | "local".</summary>
public record AutoMessageRuleDto(
    string Id, string Trigger, string Name, bool Enabled,
    bool SendSms, bool SendPush, bool SendTelegram, string Audience, string Template,
    int OffsetMinutes, string SendScope, string ScheduleType, string ScheduleTime, int ScheduleDayOfMonth,
    string CreatedAt, string SmsProvider);
/// <summary>Avto-xabar qoidasini saqlash (yaratish/tahrirlash).</summary>
public record SaveAutoMessageRuleRequest(
    string Trigger, string Name, bool Enabled,
    bool SendSms, bool SendPush, bool SendTelegram, string Audience, string Template,
    int OffsetMinutes = 5, string SendScope = "lesson_start",
    string ScheduleType = "daily", string ScheduleTime = "09:00", int ScheduleDayOfMonth = 1,
    string SmsProvider = "eskiz");

/* ---------- Call Center (MoiZvonki) ---------- */

/// <summary>Chiquvchi qo'ng'iroq so'rovi: studentId YOKI phoneNumber (dialpad'dan qo'lda) beriladi.</summary>
public record OriginateCallRequest(string? StudentId, string? PhoneNumber, string? OperatorExtension);
/// <summary>Qo'ng'iroq qatori (ro'yxat/tarix uchun) — sanalar ISO string.</summary>
public record CallDto(
    string Id, string? StudentId, string StudentName, string PhoneNumber, string Direction,
    string Status, string StartedAt, string? AnsweredAt, string? EndedAt, int DurationSeconds,
    string OperatorName, bool HasRecording, string Note);
/// <summary>Sahifalangan qo'ng'iroqlar ro'yxati.</summary>
public record CallListDto(int Total, List<CallDto> Items);
/// <summary>Raqam bo'yicha guruhlangan qator — "Yozuvlar tarixi"da bitta raqam = bitta qator,
/// ichiga kirilganda shu raqam bilan barcha suhbatlar ochiladi.</summary>
public record CallGroupDto(
    string PhoneNumber, string? StudentId, string StudentName, int CallsCount, int AnsweredCount,
    int TotalDurationSeconds, string LastCallAt, string LastStatus, string LastDirection);
/// <summary>Sahifalangan raqam-guruhlar ro'yxati.</summary>
public record CallGroupListDto(int Total, List<CallGroupDto> Items);
/// <summary>Bitta qo'ng'iroqning TO'LIQ tafsiloti (detal oynasi) — jurnal maydonlari +
/// so'zma-so'z transkript + AI tahlil.</summary>
public record CallDetailDto(
    string Id, string? StudentId, string StudentName, string PhoneNumber, string Direction,
    string Status, string StartedAt, string? AnsweredAt, string? EndedAt, int DurationSeconds,
    string OperatorName, bool HasRecording, string Note, string Transcript, string AiAnalysis);
/// <summary>Moliyada o'quvchi qatori. Charged = jami to'liq oylik (chegirmasiz);
/// Discount = jami berilgan chegirma; Paid = haqiqiy naqd to'lovlar yig'indisi (turli oylar uchun);
/// Debt / Advance — joriy holatdan (balans). DiscountPct/Amount — qoidani ko'rsatish uchun.</summary>
public record StudentFinanceRowDto(
    string StudentId, string FullName, string ClassName,
    decimal Charged, decimal Discount, decimal Paid, decimal Debt, decimal Advance,
    int DiscountPct = 0, decimal DiscountAmount = 0);

/* ---------- CTI (Local Call) ---------- */

/// <summary>Mobil (Android agent) login so'rovi. DIQQAT: maydonlar ATAYIN nullable va muqobil
/// nomli (username/user, pass) — ilova versiyalari turlicha nom yuborishi mumkin; nullable
/// bo'lmasa [ApiController] avtomatik 400 qaytaradi va ilova sababsiz "HTTP 400"da qoladi.</summary>
public record CtiLoginRequest(
    string? Login, string? Username, string? User, string? Password, string? Pass)
{
    /// <summary>Qaysi nom bilan kelgan bo'lsa ham login qiymati.</summary>
    public string LoginValue => (Login ?? Username ?? User ?? "").Trim();
    /// <summary>Qaysi nom bilan kelgan bo'lsa ham parol qiymati.</summary>
    public string PasswordValue => Password ?? Pass ?? "";
}
/// <summary>Login javobi: JWT token, agent id va WebSocket manzili.</summary>
public record CtiLoginResponse(string Token, string AgentId, string WsUrl);
/// <summary>Ilova yuborgan qo'ng'iroq metadatasi (sanalar ISO string). Maydonlar nullable —
/// avtomatik 400 o'rniga controller o'zi oqilona default qo'llaydi.</summary>
public record CtiCallCreateRequest(
    string? Direction, string? RemoteNumber, string? ContactName, string? StartedAt,
    string? AnsweredAt, string? EndedAt, int DurationSec);
/// <summary>Qo'ng'iroq yaratilgach — server tomonidagi id (audio/hodisalar shu bo'yicha yuboriladi).
/// DIQQAT: javobda id ATAYIN 3 nom bilan ({serverCallId, id, callId}) — ilova versiyasi qaysi
/// maydonni kutishidan qat'i nazar o'qiy oladi (aks holda "sinxronlanmagan" bo'lib qolib qayta yuboradi).</summary>
public record CtiCallCreatedResponse(string ServerCallId)
{
    public string Id => ServerCallId;
    public string CallId => ServerCallId;
}
/// <summary>Ilova yuborgan bitta qo'ng'iroq hodisasi (nullable — 400 o'rniga aniq xabar).</summary>
public record CtiCallEventRequest(string? Type, string? At);
/// <summary>Ilova FCM tokenini yangilash.</summary>
public record CtiFcmTokenRequest(string? Token);

/// <summary>Operator paneli — agent qatori (jonli holat konnektsiya menejeridan).</summary>
public record CtiAgentDto(
    string Id, string Login, string DisplayName, bool IsActive, bool IsOnline,
    string? LastSeenAt, bool HasFcmToken, string? StaffUserId, string StaffUserName);
/// <summary>Yangi agent yaratish. StaffUserId — SuperAdmin biriktirmoqchi bo'lsa (ixtiyoriy);
/// oddiy xodim (SuperAdmin bo'lmagan) yaratsa har doim o'ziga biriktiriladi (server tomonda).</summary>
public record CtiAgentCreateRequest(string Login, string Password, string DisplayName, string? StaffUserId);
/// <summary>Agentni tahrirlash (Password bo'sh/null bo'lsa parol o'zgarmaydi). StaffUserId faqat
/// SuperAdmin uchun qayta biriktirish imkoni beradi (boshqalar uchun e'tiborga olinmaydi).</summary>
public record CtiAgentUpdateRequest(string DisplayName, bool IsActive, string? Password, string? StaffUserId);
/// <summary>Click-to-call so'rovi.</summary>
public record CtiDialRequest(string Number);
/// <summary>Click-to-call natijasi: buyruq id + yetkazildimi (WS yoki FCM+WS orqali).</summary>
public record CtiDialResponse(string CommandId, bool Delivered);
/// <summary>Agent telefonining SIM-kartasidan SMS yuborish so'rovi (ixtiyoriy matn).</summary>
public record CtiSmsRequest(string Number, string Text);
/// <summary>SMS yuborish natijasi: buyruq id + yetkazildimi (WS yoki FCM+WS orqali).</summary>
public record CtiSmsResponse(string CommandId, bool Delivered);
/// <summary>Operator tarixidagi qo'ng'iroq qatori (sanalar ISO string).</summary>
public record CtiCallDto(
    string Id, string AgentId, string AgentName, string Direction, string RemoteNumber,
    string ContactName, string? StudentId, string StudentName, string StartedAt,
    string? AnsweredAt, string? EndedAt, int DurationSec, bool HasAudio, string Note);
/// <summary>Sahifalangan CTI qo'ng'iroqlar ro'yxati.</summary>
public record CtiCallListDto(int Total, List<CtiCallDto> Items);
/// <summary>Raqam bo'yicha GURUHLANGAN qator — bitta raqam: jami/o'tkazib yuborilgan soni va
/// OXIRGI qo'ng'iroq ma'lumotlari (tarix ro'yxati raqam-per-qator ko'rinishi uchun).</summary>
public record CtiNumberGroupDto(
    string RemoteNumber, string ContactName, string? StudentId, string StudentName,
    int CallCount, int MissedCount, bool HasAudio,
    string LastCallAt, string LastDirection, int LastDurationSec, string LastAgentName);
/// <summary>Sahifalangan raqam-guruhlar ro'yxati (Total = jami NECHTA RAQAM).</summary>
public record CtiNumberGroupListDto(int Total, List<CtiNumberGroupDto> Items);
/// <summary>CTI qo'ng'irog'ining hodisasi (detal).</summary>
public record CtiCallEventDto(string Type, string At);
/// <summary>CTI qo'ng'irog'ining to'liq tafsiloti (qator + hodisalar).</summary>
public record CtiCallDetailDto(
    string Id, string AgentId, string AgentName, string Direction, string RemoteNumber,
    string ContactName, string? StudentId, string StudentName, string StartedAt,
    string? AnsweredAt, string? EndedAt, int DurationSec, bool HasAudio, string Note,
    List<CtiCallEventDto> Events, string Transcript, string AiAnalysis);
/// <summary>Operator izohini yangilash.</summary>
public record CtiNoteRequest(string Note);
/// <summary>Berilgan raqamga yuborilgan SMS — Local Call raqam tarixida qo'ng'iroqlar bilan birga
/// ko'rsatish uchun (SmsLog'dan, Eskiz+Local ikkalasi ham). Provider: eskiz|local.</summary>
public record CtiSmsHistoryDto(string Id, string Message, string Status, string Provider, string CreatedAt);

/// <summary>O'quvchi profilidagi qo'ng'iroq (MoiZvonki + Local birlashgan). Source: "cloud"|"local".
/// Direction: "incoming"|"outgoing" (javobsiz = Answered=false). Handler: operator yoki agent nomi.</summary>
public record StudentCallDto(
    string Id, string Source, string Direction, string PhoneNumber,
    string StartedAt, int DurationSec, bool Answered, bool HasAudio, string Handler);

/// <summary>O'quvchiga (yoki ota-onasiga) yuborilgan SMS — profil "SMS tarixi" bo'limi uchun. Provider: eskiz|local.</summary>
public record StudentSmsDto(
    string Id, string PhoneNumber, string Message, string Status, string Provider, string CreatedAt);

/* ---------- Subjects (Kurslar) ---------- */
public record SubjectPayload(string Name, decimal Price = 0, decimal LessonPrice = 0);

/* ---------- Guruhlar (Groups) ---------- */
/// <summary>Jadval konflikti: mavjud guruh bir xil xona yoki o'qituvchi, kun va vaqtda ishlaydi.</summary>
public record RoomConflictDto(
    string GroupId, string GroupName, string SharedDays, string ExistingSlot, string Reason);

public record ClassPayload(
    string Name, int Grade, string Language, decimal MonthlyFee, string? Room,
    string? Status = null, string? StartDate = null, string? EndDate = null, int Capacity = 0,
    string? CourseId = null, string? TeacherId = null, string? Note = null,
    List<int>? Days = null, string? StartTime = null, string? EndTime = null,
    string? RoomId = null);

/// <summary>O'quvchining bitta guruh a'zoligi (M2M).</summary>
/// <param name="YearFreeze">«Aktiv muzlatish» belgisi — FAQAT ko'rsatish uchun. <c>Status</c> baribir
/// "frozen", hisob-kitobda oddiy muzlatishdan farqi YO'Q.</param>
public record StudentGroupDto(
    string Id, string GroupId, string GroupName, string JoinedAt, string? LeftAt, bool IsActive,
    string Status, string CourseName, string TeacherName, decimal MonthlyFee,
    List<int> Days, string StartTime, string EndTime, string Room,
    string ActivatedAt, string FrozenAt, bool YearFreeze);
/// <summary>Guruhdagi bitta o'quvchi (a'zolar ro'yxati). <c>Balance</c> — SHU GURUH bo'yicha balans
/// (manfiy = qarz), umumiy <see cref="Student.Balance"/> EMAS (qarang: GroupBalanceService).</summary>
/// <param name="YearFreeze">«Aktiv muzlatish» belgisi — FAQAT ko'rsatish uchun. <c>Status</c> baribir
/// "frozen", hisob-kitobda oddiy muzlatishdan farqi YO'Q.</param>
public record GroupMemberDto(
    string StudentId, string FullName, string JoinedAt, string? LeftAt, bool IsActive,
    string Status, string ActivatedAt, string FrozenAt, decimal Balance, bool YearFreeze);
/// <summary>
/// Guruhning FAOL a'zosi — SMS modali uchun (telefonlar + a'zolik holati + SHU GURUH balansi).
/// <para>⚠️ Telefonlar ATAYIN <see cref="GroupMemberDto"/> ga qo'shilmadi, alohida DTO qilindi:
/// a'zolar ro'yxati guruh sahifasida doim yuklanadi, telefon esa faqat SMS yuborishda kerak.</para>
/// <para>Bu DTO butun <c>Student</c> entity'sining O'RNINI bosadi: ilgari SMS modali
/// <c>GET /admin/students</c> bilan markazning BARCHA o'quvchisini (hamma maydoni bilan)
/// tortib, keyin ~15 a'zoni brauzerda filtrlab olardi.</para>
/// </summary>
public record GroupSmsRecipientDto(
    string StudentId, string FullName, string Phone, string ParentPhone,
    string FatherPhone, string MotherPhone, string Status, decimal Balance);
/// <summary>O'quvchini guruhga qo'shish so'rovi.</summary>
public record AddStudentToGroupRequest(string StudentId, string? JoinedAt);
/// <summary>A'zolikni aktivlashtirish/muzlatish so'rovi (sana ISO "YYYY-MM-DD"; bo'sh = bugun).</summary>
/// <summary>
/// A'zolikni aktivlashtirish/muzlatish so'rovi (sana ISO "YYYY-MM-DD"; bo'sh = bugun).
/// <paramref name="RetentionBonus"/> — FAQAT aktivlashtirishda: shu guruh FANI bo'yicha ushlab
/// turish bonusi hisoblansinmi. Sanoq AKTIVLASHTIRILGAN oydan boshlanadi (o'quvchi guruhga bir
/// oyda qo'shilib, keyingi oydan aktivlashtirilishi mumkin — shuning uchun ptichka qo'shishda
/// emas, aynan shu yerda). <c>null</c> = tegilmaydi (eski chaqiruvlar buzilmaydi).
/// <paramref name="YearFreeze"/> — FAQAT muzlatishda: «AKTIV MUZLATISH» (yangi o'quv yiliga o'tish).
/// Hisob-kitobga TA'SIR QILMAYDI — a'zolik holati baribir "frozen", oylik baribir hisoblanmaydi;
/// belgi faqat o'quvchilar ro'yxatida ajratib ko'rsatish uchun ("yangi yilga nechta o'quvchi bilan
/// o'tyapmiz"). FAQAT superadmin qo'ya oladi: controller HARD rol tekshiruvi bilan 403 qaytaradi —
/// jimgina oddiy muzlatishga TUSHIRILMAYDI, aks holda tugma "Aktiv muzlatish" deb bosilib hisobot
/// yolg'on chiqardi.
/// </summary>
public record MembershipStatusRequest(
    string? Date, string? ReasonId = null, bool? RetentionBonus = null, bool? YearFreeze = null);
/// <summary>
/// O'quvchini boshqa guruhga o'tkazish so'rovi: joriy guruh <paramref name="FreezeDate"/>dan
/// muzlatiladi, maqsad guruh (<paramref name="ToGroupId"/>) <paramref name="ActivateDate"/>dan
/// aktivlashtiriladi (ikkalasi bo'sh bo'lsa — bugun; ActivateDate bo'sh, FreezeDate berilgan bo'lsa — FreezeDate).
/// </summary>
public record TransferMemberRequest(string ToGroupId, string? FreezeDate, string? ActivateDate, string? ReasonId = null);
/// <summary>
/// OMMAVIY (bir paytda ko'p o'quvchi) muzlatish/aktivlashtirish so'rovi.
/// <para><paramref name="StudentIds"/> — TANLANGAN o'quvchilar; BO'SH bo'lishi mumkin emas
/// ("hammasi" ni server o'zi qidirmaydi — tanlov har doim UI'da ko'rinib turadi va tasodifiy
/// butun guruhni muzlatib qo'yish yo'li ochilmaydi).</para>
/// <para>Guruh sahifasidan chaqirilsa marshrutda guruh bo'ladi (faqat SHU guruh a'zoliklari),
/// o'quvchilar ro'yxatidan chaqirilsa guruhsiz — o'quvchining BARCHA faol a'zoliklari.</para>
/// </summary>
/// <para><paramref name="YearFreeze"/> — FAQAT muzlatishda: «AKTIV MUZLATISH»
/// (qarang: <see cref="MembershipStatusRequest"/>). FAQAT superadmin qo'ya oladi.</para>
public record BulkMembershipRequest(
    string[]? StudentIds, string? Date = null, string? ReasonId = null, bool? RetentionBonus = null,
    bool? YearFreeze = null);
/// <summary>
/// Ommaviy a'zolik amalining natijasi. Amal BITTA xato tufayli to'xtamaydi, shuning uchun
/// javob "nima bo'ldi" ni to'liq ochib beradi (jimgina tushib qolgan o'quvchi bo'lmasin).
/// </summary>
/// <param name="Requested">So'ralgan o'quvchilar soni.</param>
/// <param name="Memberships">Mos kelgan a'zoliklar soni (bitta o'quvchi bir necha guruhda bo'lishi mumkin).</param>
/// <param name="Changed">Haqiqatan o'zgargan a'zoliklar soni.</param>
/// <param name="Students">Haqiqatan o'zgargan O'QUVCHILAR soni.</param>
/// <param name="Skipped">Allaqachon kerakli holatda bo'lgani uchun o'tkazib yuborilgani.</param>
/// <param name="Failed">Xato bergani (qolganlari baribir bajarilgan).</param>
/// <param name="NoMembership">Umuman faol a'zoligi topilmagan o'quvchilar soni.</param>
/// <param name="Restored">Muzlatishda bekor qilinib balansga qaytarilgan umumiy summa.</param>
/// <param name="MovedAdvance">Aktivlashtirishda boshqa guruhdan ko'chirilgan avans summasi.</param>
/// <param name="CatchUpMonths">Orqaga sanalgan aktivlashtirishda yozilgan oylar soni (jami).</param>
/// <param name="Errors">Qisqa xato xabarlari (ko'pi bilan 20 ta; qolganlari serverda logda).</param>
public record BulkMembershipResultDto(
    int Requested, int Memberships, int Changed, int Students, int Skipped, int Failed,
    int NoMembership, decimal Restored, decimal MovedAdvance, int CatchUpMonths, string[] Errors);
/* ---------- DARS JADVALI (ScheduleService / ScheduleRules) ---------- */

/// <summary>Jadvaldagi bitta dars (guruh × hafta kuni).</summary>
/// <param name="Day">0=Dushanba … 6=Yakshanba.</param>
/// <param name="Minutes">Dars uzunligi (daqiqa) — kataklarning balandligi shundan chiziladi.</param>
public record ScheduleLessonDto(
    string GroupId, string GroupName, string TeacherId, string TeacherName,
    string RoomId, string RoomName, string CourseName,
    int Day, string DayLabel, string Start, string End, int Minutes);

/// <summary>Jadval egasi — xona yoki o'qituvchi (ro'yxatdan tanlash uchun).</summary>
/// <param name="GroupCount">Unga biriktirilgan faol guruhlar soni.</param>
public record ScheduleOwnerDto(string Id, string Name, int GroupCount);

/// <summary>TIG'IZLIK katagi: shu kun va shu yarim soatda nechta dars davom etyapti.</summary>
public record SchedulePeakDto(int Day, string Bucket, int Count);

/// <summary>Bo'sh oraliqni to'ldirish TAVSIYASI.</summary>
/// <param name="Kind">"teacher" (xona teshigi uchun — bo'sh o'qituvchi) yoki
/// "room" (o'qituvchi teshigi uchun — bo'sh xona).</param>
/// <param name="Note">Nega aynan shu tavsiya etilgani — o'zbekcha, to'liq jumla.</param>
/// <param name="WeeklyMinutes">Haftalik yuki (daqiqa): kimda bo'sh quvvat borligi ko'rinsin.</param>
public record ScheduleSuggestionDto(string Kind, string Id, string Name, string Note, int WeeklyMinutes);

/// <summary>
/// IKKI DARS ORASIDA qolib ketgan bo'sh oraliq ("4-soatda dars bor, 5-soat bo'sh, 6-soatda yana dars").
/// </summary>
/// <param name="Scope">"room" — xona bekor turibdi; "teacher" — o'qituvchi kutib o'tiribdi.</param>
/// <param name="PeakScore">Shu oraliqda markazda nechta dars ketyapti — TIG'IZLIK o'lchovi.
/// Ro'yxat aynan shu bo'yicha saralanadi: tig'iz paytdagi bo'sh xonaning "narxi" eng baland.</param>
public record ScheduleGapDto(
    string Scope, string OwnerId, string OwnerName,
    int Day, string DayLabel, string Start, string End, int Minutes,
    string BeforeGroup, string AfterGroup, int PeakScore,
    List<ScheduleSuggestionDto> Suggestions);

/// <summary>Jadval sahifasining butun ma'lumoti — bitta so'rovda.</summary>
/// <param name="SkippedGroups">Vaqti/kunlari to'ldirilmagan yoki BUZUQ guruhlar soni
/// (masalan "13:30 → 03:00"). Ular jadvalga kirmaydi — son ekranda ochiq yoziladi,
/// aks holda guruh jimgina yo'qolib qolardi.</param>
public record ScheduleBoardDto(
    List<ScheduleLessonDto> Lessons,
    List<ScheduleOwnerDto> Rooms,
    List<ScheduleOwnerDto> Teachers,
    List<SchedulePeakDto> Peak,
    int SkippedGroups);

/// <summary>Guruh to'ldirish hisoboti qatori: sig'im vs ro'yxatdagilar.</summary>
public record GroupFillRowDto(
    string GroupId, string Name, int Grade, int Capacity, int Enrolled, int FreeSeats, string Status);

/* ---------- Test natijalari ---------- */
/// <summary>Guruh kartasi (Testlar natijalari bosh sahifasi) — guruh + yaratilgan testlar soni.</summary>
public record TestGroupOverviewDto(
    string GroupId, string Name, string CourseName, string TeacherId, string TeacherName,
    int StudentCount, int TestCount);
/// <summary>Onlayn test sozlamalari — yaratish/tahrirlash va ro'yxat/tafsilotda BIR XIL shakl.
/// <c>Mode</c>="offline" bo'lsa qolgan maydonlar e'tiborga olinmaydi (eski tizim o'zgarmaydi).</summary>
/// <param name="Mode">"offline" (qo'lda ball) | "online" (bot orqali)</param>
/// <param name="PdfUrl">Savollar PDF fayli ("/uploads/xxx.pdf") — botga shu yuboriladi</param>
/// <param name="QuestionCount">Savollar soni (onlayn testda MaxScore shunga teng)</param>
/// <param name="OptionCount">Variantlar soni: 4 → A–D, 5 → A–E</param>
/// <param name="AnswerKey">To'g'ri javoblar ("ABCDA...", uzunligi = QuestionCount)</param>
/// <param name="StartAt">Javob qabul qilish boshlanishi (ISO "yyyy-MM-ddTHH:mm")</param>
/// <param name="EndAt">Javob qabul qilish tugashi (ISO "yyyy-MM-ddTHH:mm")</param>
/// <param name="Code">TEST KODI — markazda o'qimaydigan odam ham botda shu kod bilan testni ishlaydi.
/// Bo'sh yuborilsa server O'ZI noyob kod yaratadi (onlayn testda kod HAR DOIM bo'ladi).</param>
/// <param name="GroupOpen">true — guruh a'zolariga ham e'lon qilinadi (va kod bilan tashqi odam ham
/// qo'shiladi); false — "FAQAT ONLAYN": guruhga e'lon qilinmaydi, faqat kod bilan ishlanadi.</param>
public record OnlineTestDto(
    string Mode, string PdfUrl, string PdfName, int QuestionCount, int OptionCount,
    string AnswerKey, string StartAt, string EndAt,
    string Code = "", bool GroupOpen = true);
/// <summary>Bitta test qatori (guruh testlar ro'yxatida) — soni/o'rtacha bilan.
/// <c>Online</c> — onlayn test sozlamalari (Mode="offline" bo'lsa ham to'ldiriladi).
/// <c>SubmittedCount</c> — botdan javob yuborgan O'QUVCHILAR soni (onlayn).
/// <c>ExternalCount</c> — MARKAZDAN TASHQARI (kod bilan kirgan) ishtirokchilar soni.</summary>
public record GroupTestDto(
    string Id, string GroupId, string Name, string Date, decimal MaxScore,
    string CreatedAt, string CreatedBy, int StudentCount, int ScoredCount, decimal? AvgScore,
    OnlineTestDto Online, int SubmittedCount, int ExternalCount = 0,
    // Sertifikat: yoqilganmi, qaysi andoza, nechta sertifikat berilgan.
    bool CertificateEnabled = false, string CertificateTemplateId = "", int CertificateCount = 0);
public record CreateTestResultRequest(
    string GroupId, string Name, string Date, decimal MaxScore, OnlineTestDto? Online = null,
    bool CertificateEnabled = false, string? CertificateTemplateId = null);
public record UpdateTestResultRequest(
    string Name, string Date, decimal MaxScore, OnlineTestDto? Online = null,
    bool CertificateEnabled = false, string? CertificateTemplateId = null);
/// <summary>Test natijasi qatori — bitta o'quvchining bali (ball bo'yicha saralanadi). Rank = 0 → ball kiritilmagan.
/// <c>Answers</c>/<c>SubmittedAt</c> — onlayn testda botdan yuborilgan javoblar va vaqti (aks holda bo'sh).</summary>
/// <param name="Member">Guruhning FAOL a'zosimi. <c>false</c> — markazning BOSHQA guruhidagi o'quvchi
/// test KODI bilan qo'shilgan (bali bor, lekin bu guruh ro'yxatida yo'q).</param>
public record TestScoreRowDto(
    string StudentId, string FullName, decimal? Score, int Rank,
    string Answers = "", string SubmittedAt = "", string Source = "", bool Member = true);
/// <summary>MARKAZDAN TASHQARI ishtirokchi qatori — test kodi bilan kirgan, markazda o'qimaydigan odam.
/// <c>Rank</c> — shu ro'yxat ICHIDAGI o'rin (markazdagilar bilan aralashtirilmaydi).</summary>
public record ExternalTestScoreRowDto(
    string Id, string FullName, string Phone, decimal Score, int Rank,
    string Answers, string SubmittedAt);
/// <summary>Test tafsiloti — test ma'lumoti + faol o'quvchilar ballari (ball desc bo'yicha saralangan).
/// <c>ExternalRows</c> — MARKAZDAN TASHQARI (kod bilan kirgan) ishtirokchilar, alohida ro'yxat.</summary>
public record TestResultDetailDto(
    string Id, string GroupId, string GroupName, string Name, string Date, decimal MaxScore,
    string CreatedAt, string CreatedBy, List<TestScoreRowDto> Rows, OnlineTestDto Online,
    List<ExternalTestScoreRowDto>? ExternalRows = null,
    // Sertifikat: yoqilganmi, qaysi andoza va shu testda allaqachon berilganlari.
    bool CertificateEnabled = false, string CertificateTemplateId = "",
    List<TestCertificateDto>? Certificates = null);
/// <summary>Bitta o'quvchiga ball qo'yish/tozalash (Score=null → tozalash).</summary>
public record SetTestScoreRequest(string StudentId, decimal? Score);

// ==== TEST SERTIFIKATI (Word andoza → PDF) ====

/// <summary>Andozada ishlatiladigan bitta <c>@</c>-o'zgaruvchi (admin paneli shu ro'yxatni ko'rsatadi).</summary>
public record CertificateTokenDto(string Token, string Label, string Example);

/// <summary>O'QUVCHI SURATI andozaga qanday qo'yilishi — admin panelidagi yo'riqnoma.
/// Bu matn tokeni EMAS (rasmning o'lchami/joyini kod taxmin qila olmaydi), shuning uchun
/// alohida tushuntiriladi.</summary>
public record CertificatePhotoHelpDto(string Title, List<string> Steps, string Note);

/// <summary>Sertifikat Word andozasi (ro'yxat/karta uchun).</summary>
public record TestCertificateTemplateDto(
    string Id, string Name, string FileUrl, string FileName,
    bool IsDefault, bool IsActive, string CreatedAt, string CreatedBy);

/// <summary>Andoza yaratish/tahrirlash. <c>FileUrl</c> — avval <c>/api/admin/uploads</c> ga
/// yuklangan .docx manzili. Tahrirlashda bo'sh qoldirilsa fayl o'zgarmaydi.</summary>
public record TestCertificateTemplatePayload(
    string Name, string? FileUrl = null, string? FileName = null,
    bool IsDefault = false, bool IsActive = true);

/// <summary>Berilgan sertifikat. <c>Status</c>: <c>"ready"</c> — PDF tayyor;
/// <c>"docx"</c> — PDF konvertori (LibreOffice) yo'q, faqat Word fayl bor.</summary>
public record TestCertificateDto(
    string Id, string TestResultId, string StudentId, string StudentName,
    string Number, string TemplateName, string DocxUrl, string PdfUrl, string Status,
    decimal Score, decimal MaxScore, int Percent, string IssuedAt);

/// <summary>
/// SERTIFIKAT YARATISH — fon ishining holati (boshlashda ham, keyingi so'rovlarda ham shu qaytadi).
///
/// <para><c>Running</c> — hali yaratilmoqda; <c>Done</c>/<c>Total</c> — nechtadan nechtasi tayyor
/// (UI shuni "12/30" qilib ko'rsatadi va ZIP tugmasini faqat tugagach ochadi).
/// <c>Items</c> — SHU DAQIQADA tayyor bo'lgan sertifikatlar: ish tugashini kutmasdan yuklab olinadi.
/// <c>PdfAvailable=false</c> bo'lsa serverda LibreOffice o'rnatilmagan (<c>Warning</c> to'ldiriladi) —
/// sertifikatlar faqat Word bo'lib chiqadi.</para>
/// </summary>
public record TestCertificateJobDto(
    bool Running, int Total, int Done, bool PdfAvailable,
    string? Error, string? Warning, List<TestCertificateDto> Items);
/// <summary>O'quvchi profilidagi test natijasi qatori (barcha guruhlaridan, sana desc).</summary>
public record StudentGroupTestDto(
    string TestId, string GroupId, string GroupName, string Name, string Date,
    decimal MaxScore, decimal? Score, int Rank, int Total);

/* ---------- ONLAYN TEST — o'quvchi ilovasi (bot oqimi bilan bir xil mantiq) ---------- */
/// <summary>O'quvchi ko'radigan onlayn test qatori.
/// <paramref name="State"/>: <c>upcoming</c> (hali boshlanmagan) | <c>open</c> (hozir ishlash mumkin) |
/// <c>closed</c> (vaqti tugagan, topshirmagan) | <c>submitted</c> (topshirilgan).
/// <paramref name="PdfUrl"/> — savollar fayli ("/uploads/..."), statik tarqatiladi.</summary>
public record StudentOnlineTestDto(
    string Id, string GroupId, string GroupName, string Name, string Date,
    int QuestionCount, int OptionCount, string StartAt, string EndAt,
    string PdfUrl, string PdfName, string State,
    decimal? Score, string Answers, string SubmittedAt);

/// <summary>Onlayn test tafsiloti — qator ma'lumoti + o'rin va (vaqt tugagach) javob kaliti.
/// <paramref name="AnswerKey"/> test vaqti TUGAGUNCHA bo'sh keladi (kalit tarqalib ketmasin).</summary>
public record StudentOnlineTestDetailDto(
    string Id, string GroupId, string GroupName, string Name, string Date,
    int QuestionCount, int OptionCount, string StartAt, string EndAt,
    string PdfUrl, string PdfName, string State,
    decimal? Score, string Answers, string SubmittedAt,
    string AnswerKey, int Rank, int Participants);

/// <summary>Javoblarni yuborish so'rovi — "ABCDA…" (javobsiz savol uchun '-' yoki bo'sh).</summary>
public record OnlineTestSubmitRequest(string Answers);

/* ---------- Leads (CRM) ---------- */
/// <summary>Lid (bo'lajak o'quvchi) yaratish so'rovi.
/// TELEFON VALIDATSIYA (PhoneUtil.Normalize orqali standartlashtirilib saqlanadi):
/// - <paramref name="Phone"/> — lidning o'z raqami (ixtiyoriy, max 32 belgi); format: +998-XX-XXX-XX-XX
/// - <paramref name="FatherPhone"/> — ota raqami (ixtiyoriy, max 32 belgi)
/// - <paramref name="MotherPhone"/> — ona raqami (ixtiyoriy, max 32 belgi)
/// Raqamlar ixtiyoriy; kiritilsa kamita 7 ta raqam bo'lishi kerak. 998 prefiksi avtomatik qo'shiladi.
/// </summary>
public record LeadCreateRequest(
    string FullName, string Gender, string BirthDate,
    string? Phone, string? FatherFullName, string? FatherPhone,
    string? MotherFullName, string? MotherPhone, string? Note, string Stage,
    string? Source = null, string? InterestSubject = null,
    string? DistrictId = null, string? SchoolId = null);
/// <summary>Lid (bo'lajak o'quvchi) tahrirlash so'rovi.
/// TELEFON VALIDATSIYA (PhoneUtil.Normalize orqali standartlashtirilib saqlanadi):
/// - <paramref name="Phone"/> — lidning o'z raqami (ixtiyoriy, max 32 belgi); format: +998-XX-XXX-XX-XX
/// - <paramref name="FatherPhone"/> — ota raqami (ixtiyoriy, max 32 belgi)
/// - <paramref name="MotherPhone"/> — ona raqami (ixtiyoriy, max 32 belgi)
/// Raqamlar ixtiyoriy; kiritilsa kamita 7 ta raqam bo'lishi kerak. 998 prefiksi avtomatik qo'shiladi.
/// </summary>
public record LeadUpdateRequest(
    string FullName, string Gender, string BirthDate,
    string? Phone, string? FatherFullName, string? FatherPhone,
    string? MotherFullName, string? MotherPhone, string? Note,
    string? Source = null, string? InterestSubject = null,
    string? DistrictId = null, string? SchoolId = null);
public record LeadStageRequest(string Stage);

/// <summary>O'quvchi profilidagi izoh (tarix). CanDelete/CanEdit — joriy foydalanuvchi o'chira/tahrirlay
/// oladimi (o'z izohi yoki superadmin) — frontend tugmalarni shunga qarab ko'rsatadi.
/// EditedAt — tahrirlangan bo'lsa oxirgi tahrir vaqti (ISO), aks holda null.</summary>
public record StudentNoteDto(
    string Id, string Text, string AuthorName, string CreatedAt, bool CanDelete,
    bool CanEdit = false, string? EditedAt = null);
/// <summary>O'quvchiga izoh qo'shish.</summary>
public record AddStudentNoteRequest(string Text);
/// <summary>Izoh matnini tahrirlash (faqat muallifi yoki superadmin).</summary>
public record EditStudentNoteRequest(string Text);

/// <summary>Lid hodisasi (tarix).</summary>
public record LeadEventDto(string Id, string Type, string Text, string ActorName, string CreatedAt);
/// <summary>Lidga hodisa/izoh qo'shish.</summary>
public record AddLeadEventRequest(string Type, string Text);
/// <summary>Lidni o'quvchiga aylantirish. EnrollmentDate berilmasa — bugun; GroupId berilsa o'quvchi shu guruhga qo'shiladi.</summary>
public record ConvertLeadRequest(string? EnrollmentDate, string? GroupId);
/// <summary>Sinov darsi.</summary>
public record TrialLessonDto(
    string Id, string LeadId, string GroupId, string GroupName, string ScheduledAt, string Result, string CreatedAt);
/// <summary>Sinov darsini belgilash.</summary>
public record ScheduleTrialRequest(string GroupId, string ScheduledAt);
/// <summary>Sinov darsi natijasi: stayed (qoldi) | left (ketdi).</summary>
public record TrialResultRequest(string Result);

/// <summary>Lid + birinchi dars davomat holati: "attended" | "absent" | "no-lesson".
/// DistrictId/SchoolId — lid o'qiydigan TASHQI maktab (filtr uchun; nomlarini frontend
/// tumanlar ma'lumotnomasidan (GET /admin/districts) yechadi).
/// RepeatCount/LastRepeatAt — TAKRORIY murojaat (ommaviy forma yoki daraja testi orqali yana
/// yozilgan): kanban kartasidagi «Takroriy N» belgisi uchun.</summary>
public record LeadWithAttendanceDto(
    string Id, string FullName, string Gender, string BirthDate, string Phone,
    string FatherFullName, string FatherPhone, string MotherFullName, string MotherPhone,
    string? Note, string Stage, string Source, string InterestSubject, string? CreatedAt,
    string? ConvertedStudentId, string? FirstLessonAttendance,
    string DistrictId, string SchoolId,
    int RepeatCount, string LastRepeatAt);

/// <summary>Lid manbasi (ma'lumotnoma) — "O'quv bo'limi → Sabablar" sahifasida boshqariladi.</summary>
public record LeadSourceDto(string Id, string Name, int Order);
public record LeadSourceInput(string Name);

/// <summary>CRM statistikasi: jami, bosqich/manba bo'yicha, konversiya %, oylik dinamika.</summary>
public record CrmStatChartItemDto(string Label, int Count);
public record CrmMonthlyDto(string Month, int Created, int Converted);

/// <summary>Qiziqish fani (kurs) bo'yicha lid statistikasi qatori.</summary>
public record CrmInterestStatDto(
    /// <summary>Kurs nomi (yoki lidda yozilgan matn; bo'sh bo'lsa "Ko'rsatilmagan").</summary>
    string Label,
    int Count,
    int Converted,
    /// <summary>Shu fan bo'yicha konversiya foizi (0-100).</summary>
    double ConversionRate);

public record CrmStatsDto(
    int TotalLeads, int Converted, double ConversionRate,
    List<CrmStatChartItemDto> ByStage, List<CrmStatChartItemDto> BySource,
    List<CrmMonthlyDto> Monthly,
    /// <summary>Qiziqish fanlari (kurslar) bo'yicha — eng ko'pidan kamiga.</summary>
    List<CrmInterestStatDto>? ByInterest = null);

/* ---------- CRM voronka analitikasi (amoCRM uslubidagi dashboard) ---------- */

/// <summary>
/// Voronka bosqichi. <c>Reached</c> — shu bosqichga YETIB KELGAN lidlar soni (joriy bosqichi shundan
/// past bo'lmagan YOKI tarixda shu bosqichga o'tgani yozilgan lidlar), shuning uchun ro'yxat pastga
/// qarab kamayib boradi.
///
/// <para><b>HALOLLIK:</b> <c>AvgHours</c> (bosqichda o'rtacha necha soat turilgani) faqat bosqich
/// TARIXI yozila boshlagandan keyingi ma'lumot bo'yicha hisoblanadi — eski lidlarda bunday o'lchov
/// yo'q. <c>Samples</c> aynan shuning uchun qaytariladi: nechta TO'LIQ (kirdi→chiqdi) oraliq
/// o'lchangani. <c>Samples == 0</c> bo'lsa <c>AvgHours</c> — <c>null</c> (taxminiy son EMAS).</para>
/// </summary>
public record LeadFunnelStageDto(
    string StageId, string Title, string Color, int Order,
    int Reached, int Pct,
    double? AvgHours, int Samples);

/// <summary>Manba kesmasi. <c>Source</c> — lidda yozilgan xom qiymat, <c>Label</c> — ko'rsatiladigan nom.</summary>
public record LeadSourceSliceDto(string Source, string Label, int Count, int Pct);

/// <summary>
/// Menejer BOSQICH kesmasi — «kim qaysi bosqichgacha olib bordi» jadvalining bitta katagi:
/// shu menejer NECHTA takrorsiz lidni AYNAN shu bosqichga olib kelgan.
///
/// <para>Lid kiritish (<c>created</c>) ham hisobga olinadi — lidni birinchi ustunga QO'YISH ham
/// uni o'sha bosqichga olib kelish demak.</para>
///
/// <para>⚠️ Bir lidni ikki menejer surgan bo'lsa, HAR BIRI o'zi ko'chirgan bosqich uchun sanaladi —
/// bu takror EMAS, savol "kim nima qildi". Pul (<c>Revenue</c>) esa faqat AYLANTIRGANga
/// yoziladi, ya'ni tushum hech qachon ikki marta sanalmaydi.</para>
/// </summary>
public record LeadManagerStageDto(string StageId, int Reached);

/// <summary>
/// Menejer (sotuvchi) qatori — sotuv bo'limining KPI jadvali.
///
/// <list type="bullet">
///   <item><c>Moves</c> — bosqich o'zgartirishlar soni (faollik);</item>
///   <item><c>Leads</c> — nechta HAR XIL lid bilan ishlagani (kiritgan yoki ko'chirgan);</item>
///   <item><c>Created</c> — shundan nechtasini O'ZI kiritgan;</item>
///   <item><c>Won</c> — o'quvchiga aylantirgan lidlar soni;</item>
///   <item><c>Paid</c> — shulardan nechtasi haqiqatan PUL to'lagan;</item>
///   <item><c>Revenue</c> — shu lidlar keltirgan SOF tushum (to'lov − vozvrat);</item>
///   <item><c>Stages</c> — bosqichlar kesimi (<see cref="LeadManagerStageDto"/>).</item>
/// </list>
///
/// <para><b>NEGA <c>Paid</c> va <c>Revenue</c> aynan AYLANTIRGANga yoziladi:</b> lid bir marta
/// aylantiriladi, ya'ni pul bitta menejerga tegishli bo'ladi va jamlaganda haqiqiy tushumdan
/// oshib ketmaydi. "Kim yordam berdi" savoliga esa <c>Stages</c> javob beradi.</para>
///
/// <para><b>HALOLLIK:</b> bu kesim <c>LeadEvent.ActorUserId</c> ga tayanadi — u eski yozuvlarda
/// YO'Q, shuning uchun ro'yxat faqat tarix yozila boshlagandan keyingi ishni ko'rsatadi.</para>
/// </summary>
public record LeadManagerRowDto(
    string UserId, string Name,
    int Moves, int Leads, int Won,
    int Created, int Paid, decimal Revenue,
    List<LeadManagerStageDto> Stages);

/// <summary>
/// Lid KANALI kesmasi — «lidlar qayerdan keladi va qaysi kanal haqiqatan SOTADI».
/// <c>Key</c> — <see cref="IntellectCRM.Application.Services.LeadOrigins"/> kaliti.
/// <para><c>ConversionRate</c> — o'quvchiga aylanganlar ulushi, <c>PayRate</c> — PUL to'laganlar
/// ulushi (sotuvning haqiqiy o'lchovi: "o'quvchi bo'ldi" hali pul degani emas).</para>
/// </summary>
public record LeadOriginRowDto(
    string Key, string Label,
    int Leads, int Converted, int Paid, decimal Revenue,
    int ConversionRate, int PayRate);

/// <summary>
/// «BUTUN CRM MANZARASI» — markazdagi BARCHA lidlar (qo'lda kiritilgani ham).
///
/// <para>"Formalar" bo'limidagi ikkala statistika ham (lid formalari va daraja testi) faqat O'Z
/// kanalini sanaydi; bu blok esa ularni butun manzara ichiga qo'yadi. Hisob-kitob —
/// <see cref="IntellectCRM.Application.Services.LeadCrmOverview"/> (ikkala sahifa uchun YAGONA).</para>
/// </summary>
public record CrmOverviewDto(
    int Leads, int Converted, int Paid, decimal Revenue,
    List<LeadOriginRowDto> Origins, List<LeadStageCountDto> ByStage);

/// <summary>
/// CRM voronka + SOTUV analitikasi. <c>From</c>/<c>To</c> — so'ralgan davr (bo'sh = cheklanmagan).
/// <para><c>Paid</c>/<c>Revenue</c> — davrdagi lidlardan haqiqatan pul to'laganlar soni va sof
/// tushumi; <c>PayRate</c> — sotuv konversiyasi (to'lagan / jami lid).</para>
/// </summary>
public record LeadAnalyticsDto(
    string From, string To,
    int Total, int Converted, int ConversionRate,
    int Paid, decimal Revenue, int PayRate,
    List<LeadFunnelStageDto> Funnel,
    List<LeadSourceSliceDto> Sources,
    List<LeadManagerRowDto> Managers,
    List<LeadOriginRowDto> Origins);

/* ---------- Lead stages ---------- */
public record StagePayload(string Title, string Color);
public record ReorderRequest(List<string> Ids);

/* ---------- Journal ---------- */
/// <summary>Jurnal ustuni — bir dars (sana + dars raqami).</summary>
/* ---------- O'quvchilar davomati (kunlik) + o'quvchining shaxsiy jurnali ---------- */

/// <summary>Berilgan kunda darsga KELMAGAN (yoki kechikkan) o'quvchi — qo'ng'iroq qilish uchun telefonlari bilan.</summary>
public record AbsentStudentDto(
    string StudentId, string FullName, string Phone,
    string ParentFullName, string ParentPhone, string FatherPhone, string MotherPhone,
    string GroupId, string GroupName, string CourseName, string TeacherName,
    string StartTime, string EndTime, string Room,
    string ReasonId, string ReasonName, string ReasonShort, bool IsLate);

/// <summary>Bir kunlik davomat xulosasi: o'tilgan darslar, davomat olingan o'quvchilar, kelmaganlar/kechikkanlar.</summary>
public record DailyAbsenceDto(
    string Date, int ConductedGroups, int MarkedStudents, int AbsentCount, int LateCount,
    List<AbsentStudentDto> Rows);

/// <summary>O'quvchi jurnalidagi bitta dars (faqat o'qish uchun).</summary>
public record StudentJournalCellDto(
    string Date, bool Conducted, bool Blocked, bool Present,
    int? Grade, string? ReasonName, string? ReasonShort, bool IsLate,
    int Homework, int Behavior, MasteryLevel? Mastery);

/// <summary>O'quvchi jurnal oynasidagi guruh tanlovi.</summary>
public record StudentJournalGroupDto(string GroupId, string GroupName, string CourseName, string TeacherName);

/// <summary>
/// O'quvchining BITTA guruhdagi oylik jurnali — faqat SHU o'quvchi qatori (read-only).
/// Guruh jurnalidagi kabi kataklar: baho / davomat sababi / kelgan (✓) / dars bo'lmagan.
/// </summary>
public record StudentJournalDto(
    string StudentId, string FullName,
    List<StudentJournalGroupDto> Groups, string GroupId,
    List<string> Months, string Month,
    List<StudentJournalCellDto> Cells,
    int Conducted, int Attended, int Absent, int Late, double AvgGrade);

public record JournalColumnDto(string Date, int Period);
/// <summary>Mavzular Excel importidagi xato qator (Excel qator raqami + sabab).</summary>
public record TopicImportRowErrorDto(int Row, string Reason);
/// <summary>Mavzular Excel import natijasi: to'ldirilgan / o'tkazib yuborilgan (bo'sh) / xato qatorlar.</summary>
public record TopicImportResultDto(int Imported, int Skipped, int Errors, List<TopicImportRowErrorDto> RowErrors);
/// <summary>Present — ANIQ "keldi (bor)" belgisi (RecordedAt/PresentDefaultFrom cheklovidan qat'i nazar
/// yashil ✓); Homework: 0=belgilanmagan, 1=qildi, 2=qilmadi, 3=chala qildi.</summary>
public record JournalEntryDto(
    string StudentId, string Date, int Period, int? Grade, string? ReasonId,
    int Homework, int Behavior, MasteryLevel? Mastery, bool Present);
public record SetJournalEntryRequest(
    string ClassId, string SubjectId, int Quarter, string StudentId, string Date, int Period,
    int? Grade, string? ReasonId, int Homework = 0, int Behavior = 0, MasteryLevel? Mastery = null,
    bool Present = false);
public record JournalTopicDto(string Date, int Period, string Topic, string? Homework, bool Conducted);
/// <summary>Berilgan sanada o'tilgan (conducted) darslar — guruh+fan+dars raqami.</summary>
public record ConductedLessonDto(string ClassId, string SubjectId, int Period);

/* ---------- Guruh OYLIK jurnali (guruh sahifasidan) ---------- */
/// <summary>Guruh jurnali sarlavhasi: guruh + kurs + o'qituvchi + jadval ma'lumotlari.</summary>
public record GroupJournalInfoDto(
    string Id, string Name, string CourseId, string CourseName, string TeacherName,
    List<int> Days, string StartTime, string EndTime, string Room, string StartDate, decimal MonthlyFee);
/// <summary>Guruh jurnalidagi o'quvchi qatori (faqat faol a'zolar). MemberStart — o'quvchi guruhda
/// boshlangan sana ("yyyy-MM-dd", aktivlashtirilgan bo'lsa ActivatedAt, aks holda JoinedAt); undan
/// oldingi darslarga davomat/baho KIRITILMAYDI (frontend shu sanadan oldingi kataklarni bloklaydi).</summary>
/// <summary><c>PresentDefaultFrom</c>: shu sanadan OLDINGI (MemberStart bilan bu orasidagi) o'tilgan darslarda
/// yozuv bo'lmasa ham avtomatik "keldi" deb ko'rsatilmaydi — bo'sh qoladi, o'qituvchi qo'lda belgilashi kerak
/// (bloklanmaydi). Bo'sh qiymat = cheklovsiz (eski xatti-harakat).</summary>
/// <summary><c>Balance</c>: SHU GURUH bo'yicha balans (manfiy = qarz) — o'quvchining umumiy balansi EMAS.
/// <see cref="IntellectCRM.Application.Services.GroupBalanceService"/> hisoblaydi: bir nechta guruhda o'qiydigan
/// o'quvchi to'lagan guruhida "qarzi yo'q", to'lamaganida qarzdor ko'rinadi (har o'qituvchi o'z guruhini ko'radi).</summary>
/// <summary><c>DebtMonths</c>: SHU GURUH bo'yicha to'liq yopilmagan OYLAR soni (0 = qarz yo'q).
/// 2 va undan ortiq bo'lsa jurnalda o'quvchi ALOHIDA rangda (binafsha-pushti) ko'rsatiladi — oddiy
/// qizil (1 oylik qarz) dan og'irroq holat. Hisob: <see cref="IntellectCRM.Application.Services.GroupBalanceService"/>.</summary>
/// <summary><c>PaymentHidden</c>: to'lov "darvozasi" (<see cref="JournalPolicyDto"/>) bo'yicha bu o'quvchi
/// O'QITUVCHI jurnalida KO'RINMASLIGI kerak (<c>PaymentHiddenReason</c>: "prevMonth" | "cutoff").
/// ADMIN jurnalida qator baribir qaytariladi — admin hammani ko'radi, bu faqat bayroq.</summary>
/// <summary><c>PhotoUrl</c>: o'quvchining profil surati ("/uploads/..."), bo'lmasa bo'sh satr.
/// Manba — <c>Student.BirthCertificateUrl</c> (nomi eski, qarang: Entities.cs). Jurnalda F.I.SH ustiga
/// bosilganda o'qituvchi o'quvchini yuzidan taniy olishi uchun kerak.</summary>
public record GroupJournalStudentDto(
    string StudentId, string FullName, string Status, string ActivatedAt, decimal Balance, string MemberStart,
    string PresentDefaultFrom, string FrozenAt, int DebtMonths = 0,
    bool PaymentHidden = false, string PaymentHiddenReason = "", string PhotoUrl = "");
/// <summary>Guruhning bitta oylik jurnali: ustunlar guruh dars kunlari bo'yicha avtomatik, qatorlar — faol o'quvchilar.
/// <see cref="ConductedDates"/> — "o'tildi" deb belgilangan dars sanalari (sababsiz o'quvchi shu kunda KELDI = yashil).</summary>
public record GroupJournalDto(
    GroupJournalInfoDto Group, List<string> Months, string Month,
    List<JournalColumnDto> Columns, List<GroupJournalStudentDto> Students, List<JournalEntryDto> Entries,
    List<string> ConductedDates, List<LessonRescheduleDto> Reschedules);
/// <summary>Bitta darsning BIR MARTALIK boshqa kunga ko'chirilishi (shu oyga tegishli). ToDate — jurnalda
/// ko'rinadigan yangi ustun; FromDate — asl (endi yo'q) kun; Time — yangi vaqt (ixtiyoriy, "HH:mm").</summary>
public record LessonRescheduleDto(string Id, string FromDate, string ToDate, string? Time);
/// <summary>Darsni boshqa kunga ko'chirish so'rovi (bir martalik).</summary>
public record RescheduleLessonRequest(string ClassId, string FromDate, string ToDate, string? Time);
/// <summary>Bitta dars (sana) uchun BARCHA o'quvchiga birdan davomat. <see cref="Absent"/>=false → hammasi KELDI
/// (sabablar tozalanadi). =true → hammasi KELMADI: <see cref="ReasonId"/> berilsa shu sabab, aks holda standart
/// "Sababsiz" (yo'q bo'lsa avtomatik yaratiladi). Ikkala holatda ham dars "o'tildi" (Conducted) bo'ladi.</summary>
public record BulkAttendanceRequest(
    string ClassId, string SubjectId, string Date, int Period, List<string> StudentIds,
    string? ReasonId, bool Absent = false);
public record SetLessonNoteRequest(
    string ClassId, string SubjectId, int Quarter, string Date, int Period, string Topic, string? Homework, bool Conducted);
/// <summary>Jurnal tahrirlash siyosati (admin "Guruhlar → Jurnal boshqaruvi"). EditMode:
/// "free" (istalgan o'tgan sana) | "today" (faqat bugun) | "window" (oxirgi RetroDays kun).
/// ConductedOnly — baho/davomat faqat "o'tildi" darsga. ApplyToAdmins — admin jurnaliga ham qo'llash.</summary>
/// <summary><c>HideUnpaidPrevMonth</c> — O'TGAN oydan qarzi bor o'quvchi o'qituvchi jurnalida ko'rinmaydi.
/// <c>HideUnpaidAfterDay</c> + <c>UnpaidCutoffDay</c> (1-28) — JORIY oy qarzi shu kundan boshlab yashiradi.
/// Ikkalasi ham MUZLATISH EMAS: hisob-kitob davom etadi, to'lov kelishi bilan qator o'zi qaytadi.</summary>
public record JournalPolicyDto(
    string EditMode, int RetroDays, bool ConductedOnly, bool ApplyToAdmins,
    bool SalaryRequireJournal = false, int SalaryGraceDays = 0,
    bool HideUnpaidPrevMonth = false, bool HideUnpaidAfterDay = false, int UnpaidCutoffDay = 10);

/* ---------- Settings ---------- */
public record LessonTimeDto(int Period, string StartTime, string EndTime);
public record AbsenceReasonDto(string Id, string Name, string Short, bool IsLate);
/// <summary>Jadval/hafta navigatsiyasi uchun davr oralig'i. Markazda chorak tizimi YO'Q —
/// bu o'quv yili oralig'idan sintez qilingan bitta davr (frontend hafta hisobi uchun).</summary>
public record QuarterPeriodDto(int Quarter, string StartDate, string EndDate, bool GradesOpen);
public record SchoolSettingsDto(
    List<LessonTimeDto> LessonTimes, List<AbsenceReasonDto> AbsenceReasons,
    List<QuarterPeriodDto> Quarters);
public record SaveAbsenceReasonsRequest(List<AbsenceReasonDto> AbsenceReasons);

/* ---------- Dashboard ---------- */
public record AdminStatsDto(int StudentsCount, int TeachersCount, double AverageGrade, double? AttendanceRate);
public record ClassPerformanceItemDto(string ClassId, string ClassName, double AverageGrade, double? AttendanceRate, string TeacherName = "");
public record TopClassDto(string Id, string Name, int StudentsCount, int ActiveCount, double AverageGrade);
public record StudentBreakdownDto(int Active, int Inactive, int Debtors, int Paid, int WithGroup, int WithoutGroup);
/// <summary>Bosh sahifa tepasidagi 5 ta asosiy ko'rsatkich.</summary>
public record DashboardHeaderStatsDto(int Leads, int TrialStudents, int Active, int Frozen, int Debtors);
public record AdminDashboardDto(
    AdminStatsDto Stats, List<ClassPerformanceItemDto> ClassPerformance, List<TopClassDto> TopClasses,
    StudentBreakdownDto StudentBreakdown, int TotalGradesCount, DashboardHeaderStatsDto Header);

/// <summary>Bosh sahifa "Bugungi darslar" monitoringi: bugun dars kuni bo'lgan har bir guruh uchun
/// o'qituvchi davomat qildimi (bugungi Conducted LessonNote) va baho qo'ydimi (bugungi jurnal
/// bahosi yoki mezon belgisi).</summary>
public record TodayLessonMonitorDto(
    string GroupId, string GroupName, string CourseName, string TeacherId, string TeacherName,
    string Room, string StartTime, string EndTime, int StudentsCount,
    bool AttendanceDone, bool GradesDone);
public record TodayLessonsDto(string Date, int DayIndex, List<TodayLessonMonitorDto> Lessons);

/* ---------- Class performance / rating ---------- */
public record SubjectDto(string Id, string Name, decimal Price = 0);
public record StudentDto(
    string Id, string FullName, string BirthDate, string Address, string Gender,
    string ParentFullName, string ParentPhone, string ClassName, string EnrollmentDate, decimal Balance,
    int DiscountPct = 0, decimal DiscountAmount = 0, string DiscountNote = "",
    string LastName = "", string FirstName = "", string MiddleName = "",
    string? BirthCertificateUrl = null,
    string ParentLastName = "", string ParentFirstName = "", string ParentMiddleName = "",
    string? ParentPassportUrl = null,
    bool IsArchived = false, string? ArchivedAt = null, string? ArchiveReason = null,
    bool LoginBlocked = false);

/// <summary>O'quvchini arxivlash so'rovi — sababini saqlaydi (ReasonId yo'ki Reason).</summary>
public record ArchiveStudentRequest(string? Reason = null, string? ReasonId = null);

/// <summary>O'quvchi rasmini (profil surati) o'rnatish/o'chirish. <c>PhotoUrl</c> — serverga
/// yuklangan fayl manzili ("/uploads/..."); bo'sh yoki null bo'lsa rasm O'CHIRILADI.
/// Ma'lumot <see cref="Student.BirthCertificateUrl"/> ustunida saqlanadi (nomi eski — butun
/// tizim uni RASM deb ishlatadi).</summary>
public record StudentPhotoRequest(string? PhotoUrl);

/// <summary>O'qituvchi rasmini (profil surati) o'rnatish/o'chirish — <see cref="StudentPhotoRequest"/>
/// bilan bir xil shakl. <c>PhotoUrl</c> — serverga oldin yuklangan <c>/uploads/...</c> manzili;
/// bo'sh/`null` = rasmni o'chirish.</summary>
public record TeacherPhotoRequest(string? PhotoUrl);

/// <summary>Admin o'quvchi login'ini vaqtincha cheklaydi/qayta ochadi.</summary>
public record StudentLoginBlockRequest(bool Blocked);

/// <summary>Admin bir nechta o'quvchi login'ini birdaniga cheklaydi/qayta ochadi.</summary>
public record StudentLoginBlockBulkRequest(List<string> StudentIds, bool Blocked);

/// <summary>Bitta davomat sababidan o'quvchida necha marta bo'lgani (jurnal belgilaridan).</summary>
public record AttendanceReasonCountDto(string ReasonId, string Name, string Short, bool IsLate, int Count);

/// <summary>O'quvchini arxivdan qaytarish — ixtiyoriy yangi parol (arxivlanganda parol bloklangan edi).</summary>
public record RestoreStudentRequest(string? NewPassword);

/// <summary>
/// VAQTINCHA BLOKLASH so'rovi — guruh uchun ham (o'qituvchiga ko'rinmasin), o'qituvchi uchun ham
/// (tizimga kira olmasin). <c>Note</c> ixtiyoriy izoh, faqat admin ko'radi.
/// </summary>
public record BlockRequest(string? Note = null);

/// <summary>O'qituvchini arxivlash so'rovi — sababini saqlaydi.</summary>
public record ArchiveTeacherRequest(string Reason);
/// <summary>O'qituvchini arxivdan qaytarish — ixtiyoriy yangi parol (arxivlanganda parol bloklangan edi).</summary>
public record RestoreTeacherRequest(string? NewPassword);

/// <summary>
/// O'qituvchi faollik hisoboti — bitta qator (umumiy ko'rinish). Expected = jadvaldan kelib
/// chiqib bugungacha bo'lishi kerak bo'lgan darslar; Conducted = jurnal "o'tildi" belgilari;
/// foizlar bajarilgan/mavzu yozilgan/uy vazifa berilgan ulushini bildiradi. Status: active|low|none.
/// Came/Active/Trial/Frozen/Left — TANLANGAN OYDAGI oqim (Umumiy = barcha oylar): kelgan (JoinedAt oyi),
/// aktivlashgan (ActivatedAt oyi), sinov, muzlatilgan (FrozenAt oyi), ketgan (LeftAt oyi).
/// Remaining (Qolgan) — HOZIRGI aktiv o'quvchilar soni (barcha guruhlarida, oyga bog'liq emas); o'qituvchi
/// profili/performance dagi "Faol" (Status=="active" && IsActive) bilan AYNAN bir xil.
/// ConversionPct = Active / Came * 100 (shu oyda kelganlardan aktivlashganlar foizi).
/// </summary>
public record TeacherReportRowDto(
    string TeacherId, string FullName, bool IsArchived,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    string? LastActivity, string Status,
    int Came, int Active, int Trial, int Frozen, int Left, int Remaining, int? ConversionPct);

/// <summary>
/// O'qituvchilar hisoboti — umumiy ko'rinish javobi: mavjud oylar ro'yxati + tanlangan oy + qatorlar.
/// Month = "" bo'lsa Umumiy (barcha oylar yig'indisi).
/// </summary>
public record TeacherReportOverviewDto(
    List<string> Months, string Month, List<TeacherReportRowDto> Rows);

/// <summary>O'qituvchi hisoboti — guruh/fan kesimida bitta qator (batafsil ko'rinish).</summary>
public record TeacherReportBreakdownDto(
    string ClassName, string SubjectName,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct);

/// <summary>Bitta o'qituvchining batafsil hisoboti: umumiy ko'rsatkichlar + guruh/fan yoyilmasi.</summary>
public record TeacherReportDetailDto(
    string TeacherId, string FullName, bool IsArchived,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    string? LastActivity, string Status,
    int Came, int Active, int Trial, int Frozen, int Left, int Remaining, int? ConversionPct,
    List<TeacherReportBreakdownDto> Rows);

/// <summary>O'quvchi (mobil ilova) o'z joylashuvini yangilash so'rovi — GPS dan keladi.</summary>
public record UpdateLocationRequest(double Latitude, double Longitude, string? Address);
/// <summary>Joriy saqlangan joylashuvni o'qish (ilova xaritada ko'rsatishi uchun). Hali yo'q bo'lsa null'lar.</summary>
public record StudentLocationDto(double? Latitude, double? Longitude, string? Address, string? UpdatedAt);
/// <summary>Admin xarita uchun — joylashuvi bor o'quvchi qatori.</summary>
public record StudentLocationRowDto(
    string StudentId, string FullName, string ClassName,
    double Latitude, double Longitude, string? Address, string? UpdatedAt);

/// <summary>Ota-ona bo'limidagi bitta farzand (qisqacha) + qurilma ma'lumoti.</summary>
public record ParentChildDto(
    string StudentId, string FullName, string ClassName,
    string? FirstLoginAt, string? LastLoginAt,
    string DeviceName = "", string Platform = "", string AppId = "");

/// <summary>
/// Admin "Ota-onalar" bo'limidagi bir ota-ona qatori — telefon bo'yicha guruhlangan.
/// IsActivated = farzandlardan kamida bittasi ilovaga kirgan (FirstLoginAt mavjud).
/// ActivatedAt = farzandlar orasida eng erta FirstLoginAt; LastSeenAt = eng kech LastLoginAt.
/// DeviceName/Platform = oxirgi faol qurilma (farzandlar bo'yicha).
/// </summary>
public record ParentRowDto(
    string FullName, string Phone, int ChildrenCount,
    bool IsActivated, string? ActivatedAt, string? LastSeenAt,
    List<ParentChildDto> Children, string DeviceName = "", string Platform = "");

/// <summary>Admin "Ilova → O'qituvchilar" bo'limidagi bir o'qituvchi qatori (ilova faolligi + qurilma).</summary>
public record TeacherAppRowDto(
    string TeacherId, string FullName, string Phone,
    bool IsActivated, string? ActivatedAt, string? LastSeenAt,
    string DeviceName, string Platform, string AppId);
/// <summary>Guruh hisobotidagi bitta o'quvchi qatori.</summary>
public record ClassStudentRowDto(StudentDto Student, Dictionary<string, double> Grades, double Average, double? Attendance);
public record ClassPerformanceDataDto(List<SubjectDto> Subjects, List<ClassStudentRowDto> Rows);
public record ClassStatsDto(int StudentsCount, double AverageGrade, double? Attendance);
/// <summary>Reyting qatori. <paramref name="Ball"/> — yig'ilgan ball (jurnal baholari + bajarilgan
/// baholash mezonlari); reyting SHU bo'yicha saralanadi. Average/Attendance — qo'shimcha ko'rsatkich.
/// <para><paramref name="GroupIds"/> — o'quvchining FAOL guruhlari (M2M a'zolik; a'zoligi bo'lmasa
/// eski <c>ClassName</c> yorlig'idan topilganlari). Guruh reytingi SHU bo'yicha ajratiladi —
/// <c>ClassName</c> matni ko'p o'quvchida bo'sh yoki eskirgan bo'lgani uchun unga tayanib bo'lmaydi.
/// Ixtiyoriy (default `null`) — eski chaqiruvchilar buzilmasin.</para>
/// <para><paramref name="AvgBall"/> — <b>MARKAZ REYTINGI SHU BO'YICHA</b> saralanadi:
/// jami ball ÷ ball tushgan guruhlar soni (<paramref name="GroupCount"/>). Ko'p guruhda o'qiydigan
/// o'quvchi shunchaki ko'p baho olgani uchun tepaga chiqib qolmasin.
/// <paramref name="GroupBalls"/> — guruh → SHU guruhdagi ball; guruh reytingi (portal) shundan
/// quriladi, ya'ni guruh ro'yxatida markaz JAMISI emas, o'sha guruh bali turadi.</para></summary>
public record StudentRatingRowDto(
    StudentDto Student, string ClassName, int Grade, double Average, double? Attendance, int Ball = 0,
    List<string>? GroupIds = null,
    double AvgBall = 0, int GroupCount = 0, Dictionary<string, int>? GroupBalls = null);

/* ---------- BALL (reyting bali) — jurnal baholari + bajarilgan baholash mezonlari ---------- */

/// <summary>Bitta o'quvchining markaz bo'yicha bali (admin "O'quvchilar" ro'yxatidagi "Ball" ustuni).
/// <para><paramref name="Ball"/> — barcha guruhlar YIG'INDISI (ma'nosi o'zgarmagan),
/// <paramref name="AvgBall"/> — guruhlar bo'yicha O'RTACHA (markaz reytingi shu bo'yicha),
/// <paramref name="GroupCount"/> — ball TUSHGAN guruhlar soni (o'rtachaning maxraji).</para></summary>
public record StudentBallDto(
    string StudentId, int JournalTotal, int CriteriaDone, int Ball, double Average,
    double AvgBall = 0, int GroupCount = 0);

/// <summary>O'qituvchi reytingidagi bitta qator: o'rin, o'quvchi, ball tarkibi va davomat.
/// <para><b>Qator = (o'quvchi, GURUH)</b>: bir o'quvchi o'qituvchining ikki guruhida bo'lsa
/// ikkita qator chiqadi va har birida FAQAT o'sha guruh bali turadi. <paramref name="Rank"/> —
/// GURUH ICHIDAGI o'rin (1,2,3...). <paramref name="Groups"/> ESKI nom sifatida qoladi va shu
/// qatorning guruh nomi bilan to'ldiriladi (ilovaning eski versiyalari uni matn bo'yicha
/// filtrlaydi) — yangi kod <paramref name="GroupId"/>/<paramref name="GroupName"/> ni o'qisin.</para></summary>
public record TeacherRatingRowDto(
    int Rank, string StudentId, string FullName, string Groups,
    int JournalTotal, int CriteriaDone, int Ball, double Average, double? Attendance,
    string GroupId = "", string GroupName = "");

/// <summary>O'qituvchi guruhlaridagi o'quvchilar reytingi (ball bo'yicha, o'rin bilan).
/// <para><paramref name="StudentsCount"/> — DISTINCT o'quvchi soni (qatorlar soni EMAS: qator =
/// o'quvchi×guruh). Qatorlar soni kerak bo'lsa — <paramref name="RowsCount"/>.</para>
/// <para><paramref name="Month"/> — qaysi oy kesimi qaytarildi ("yyyy-MM"); <b>"" = Umumiy</b>
/// (barcha vaqt) — standart. <paramref name="Months"/> — tanlash mumkin bo'lgan oylar (eng erta
/// ma'lumot oyidan JORIY oygacha): kelajakdagi oy ro'yxatda yo'q.</para></summary>
public record TeacherRatingDto(
    string TeacherId, string FullName, int GroupsCount, int StudentsCount, double AverageBall,
    List<TeacherRatingRowDto> Rows, int RowsCount = 0,
    string Month = "", List<string>? Months = null);

/* ---------- BALL TAFSILOTI, TARIXI va QO'LDA TUZATISH (admin/superadmin) ---------- */

/// <summary>O'quvchi balining BITTA GURUH kesimi.
/// <para><paramref name="Computed"/> — jurnal+mezon (xom hisob), <paramref name="Adjustment"/> —
/// qo'lda tuzatishlar yig'indisi, <paramref name="Ball"/> — amaldagi ball
/// (<c>Math.Max(0, Computed + Adjustment)</c>, manfiyga tushmaydi).</para></summary>
public record StudentBallGroupDto(
    string GroupId, string GroupName, int JournalTotal, int CriteriaDone,
    int Computed, int Adjustment, int Ball);

/// <summary>O'quvchi balining to'liq tafsiloti (profil → "Ball" paneli).</summary>
public record StudentBallDetailDto(
    string StudentId, int Total, double AvgBall, int GroupCount, List<StudentBallGroupDto> Groups);

/// <summary>Ball tarixidagi bitta hodisa.
/// <para><paramref name="Source"/>: <c>journal</c> (jurnal bahosi), <c>criterion</c> (bajarilgan
/// baholash mezoni, <paramref name="Points"/> = 1) yoki <c>manual</c> (qo'lda tuzatish,
/// <paramref name="Points"/> = Delta, manfiy bo'lishi mumkin).</para></summary>
public record StudentBallHistoryItemDto(
    string Date, string GroupId, string GroupName, string Source, string Label, int Points, string Actor);

/// <summary>Ball tarixi. <paramref name="Truncated"/> — chegara (<paramref name="Limit"/>) ga
/// yetgani uchun eski yozuvlar qirqilgan (jimgina emas, ochiq bildiriladi).</summary>
public record StudentBallHistoryDto(
    List<StudentBallHistoryItemDto> Items, bool Truncated, int Limit, int Total);

/// <summary>Ballni qo'lda tuzatish so'rovi. <paramref name="ResetToZero"/> berilsa
/// <paramref name="Delta"/> e'tiborga olinmaydi — server joriy balni O'ZI o'qib teskarisini yozadi.</summary>
public record StudentBallAdjustPayload(
    string GroupId, int? Delta, bool ResetToZero, string Reason);

/// <summary>Tuzatish natijasi: nima o'zgardi + yangilangan tafsilot (UI qayta so'ramasin).</summary>
public record StudentBallAdjustResultDto(
    string GroupId, string GroupName, int Before, int Delta, int After, StudentBallDetailDto Ball);
/// <summary>O'quvchi davomati — har metrika chorak (1-4) → son ko'rinishida.</summary>
public record StudentAttendanceDto(
    Dictionary<int, int> MissedDays, Dictionary<int, int> IllnessDays,
    Dictionary<int, int> MissedLessons, Dictionary<int, int> IllnessLessons,
    Dictionary<int, int> LateCount);
/// <summary>Bitta o'quvchining o'zlashtirish va qatnashish hisoboti.</summary>
public record StudentReportDto(
    string StudentId, string FullName, string ClassName, string HomeroomTeacher, string ParentFullName,
    List<SubjectDto> Subjects, Dictionary<string, Dictionary<int, double>> Grades,
    StudentAttendanceDto Attendance);

/// <summary>O'quvchining bitta OYDAGI ("yyyy-MM") uy vazifa/xulq jamlamasi (daftar — oyma-oy).</summary>
public record MonthMarksDto(string Month, int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad);
/// <summary>O'quvchi davomati — har metrika OY ("yyyy-MM") → son ko'rinishida (daftar — oyma-oy).</summary>
public record MonthlyAttendanceDto(
    Dictionary<string, int> MissedDays, Dictionary<string, int> IllnessDays,
    Dictionary<string, int> MissedLessons, Dictionary<string, int> IllnessLessons,
    Dictionary<string, int> LateCount);

/// <summary>
/// O'quvchi shaxsiy daftari — bitta o'quvchi haqida BARCHA ma'lumot (profil, o'zlashtirish,
/// davomat, uy vazifa va xulq).
/// </summary>
public record StudentNotebookDto(
    // Profil
    string Id, string FullName, string ClassName, string HomeroomTeacher,
    string ParentFullName, string ParentPhone, string Gender, string BirthDate,
    string EnrollmentDate, decimal Balance, string? PhotoUrl,
    // Shaxsiy ma'lumotlar
    string Address, int DiscountPct, decimal DiscountAmount, string DiscountNote,
    string? ParentPassportUrl,
    // O'zlashtirish — fan → OY ("yyyy-MM") → o'rtacha baho
    List<SubjectDto> Subjects, Dictionary<string, Dictionary<string, double>> Grades, double AvgGrade,
    // Davomat — oyma-oy
    MonthlyAttendanceDto Attendance, int Conducted, int Attended, int AttendancePct,
    List<AttendanceReasonCountDto> Reasons,
    // Uy vazifa + xulq (oyma-oy)
    int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad, List<MonthMarksDto> MarksTrend);

/// <summary>Portal reytingidagi bitta qator (o'quvchi/parent ko'rinishi — shaxsiy ma'lumotsiz: telefon/balans/manzil yo'q).
/// <paramref name="Ball"/> — YIG'ILGAN ball (reyting shu bo'yicha); Average — qo'shimcha ko'rsatkich.
/// <para><b>`Ball` — RO'YXAT NIMA BO'YICHA TUZILGAN BO'LSA, O'SHA SON:</b> guruh ro'yxatida
/// (<c>Groups[].Rows</c>, <c>ClassRows</c>) — SHU GURUHDAGI ball; markaz ro'yxatida
/// (<c>SchoolRows</c>) — guruhlar bo'yicha O'RTACHA (butunlashtirilgan). Shu sababdan klient
/// qo'shimcha mantiqsiz `Ball`ni ko'rsataveradi. Aniq o'rtacha — <paramref name="AvgBall"/>,
/// barcha guruhlar yig'indisi — <paramref name="TotalBall"/>, guruhlar soni —
/// <paramref name="GroupCount"/>.</para></summary>
public record PortalRatingRowDto(
    int Rank, string StudentId, string FullName, string ClassName, double Average, double? Attendance,
    int Ball = 0, int TotalBall = 0, int GroupCount = 0, double AvgBall = 0);

/// <summary>
/// Portal reytingidagi BITTA GURUH: o'quvchining har bir faol guruhi uchun alohida ro'yxat.
/// <para><paramref name="Rows"/> — guruh a'zolari, o'rin (<c>Rank</c>) GURUH ICHIDA qayta
/// raqamlangan (1,2,3...), markaz o'rni EMAS — aks holda podium (1/2/3) noto'g'ri chizilardi.
/// <paramref name="MeRank"/> — o'quvchining shu guruhdagi o'rni (0 — ro'yxatda yo'q, masalan
/// arxivlangan), <paramref name="Size"/> — guruhdagi jami o'quvchi.</para>
/// <para>Guruhsiz (faqat eski <c>ClassName</c> yorlig'i bo'lgan) o'quvchida bitta "soxta" guruh
/// qaytadi: <c>GroupId = ""</c>, nomi — o'sha yorliq.</para>
/// </summary>
public record PortalRatingGroupDto(
    string GroupId, string GroupName, List<PortalRatingRowDto> Rows, int MeRank, int Size);

/// <summary>
/// O'quvchi/parent reytingi (adminniki bilan bir xil hisob, YIG'ILGAN ball bo'yicha): o'z guruhlari TO'LIQ
/// ranglangan, markaz bo'yicha esa faqat TOP 15. `MeStudentId` — o'z qatorini ajratish uchun;
/// `MeSchoolRank` top 15 dan tashqarida bo'lsa ham o'quvchining markaz o'rnini beradi (`SchoolSize` — jami).
/// <para><b>`Groups`</b> — o'quvchining HAR BIR faol guruhi alohida (loyihada bir o'quvchi bir necha
/// kursda o'qishi odatiy hol). <b>`ClassRows`</b> ESKI nom sifatida QOLADI (birinchi guruh qatorlari
/// bilan to'ldiriladi) — web klient va ilovaning eski versiyalari uni o'qiydi, orqaga moslik shart.</para>
/// </summary>
public record PortalRatingDto(
    string MeStudentId,
    List<PortalRatingRowDto> ClassRows,
    List<PortalRatingRowDto> SchoolRows,
    int? MeSchoolRank, int SchoolSize,
    List<PortalRatingGroupDto> Groups);


/// <summary>Markaz ma'lumotlari (profil sozlamasi).</summary>
public record SchoolInfoDto(
    string Name, string Director, string Phone, string Email,
    string Address, string Region, string District, string LogoUrl = "");
/// <summary>Markaz nomi + logo (brending — barcha foydalanuvchilar uchun).</summary>
public record SchoolNameDto(string Name, string TelegramChannel = "", string LogoUrl = "");
/// <summary>Ommaviy brending (login/daraja testi kabi autentifikatsiyasiz sahifalar uchun).</summary>
/// <summary>Ommaviy brending (tokensiz) — login, daraja testi va MAXFIYLIK SIYOSATI sahifalari uchun.
/// <c>Email</c> maxfiylik sahifasida aloqa manzili sifatida ko'rsatiladi (Google Play talab qiladi).</summary>
public record PublicBrandDto(string Name, string LogoUrl, string Phone, string Email = "");
/// <summary>Telegram bot sozlamasi (admin). Configured = token bo'sh emasligini bildiradi.
/// PhoneMatchField: "parent" (default) | "student" — kontakt ulashilganda qaysi raqam bo'yicha qidirish.</summary>
/// <summary>Telegram bot sozlamalari. <c>ChannelStatus</c>/<c>ChannelMessage</c> — MAJBURIY OBUNA
/// tekshiruvi haqiqatan ishlayaptimi (ok | not-set | no-token | private | not-found | bot-not-admin):
/// Telegram getChatMember faqat bot kanalda ADMIN bo'lsagina ishlaydi, aks holda bot obunani
/// tekshira olmaydi va hammani o'tkazib yuboradi — admin buni ko'rib turishi kerak.</summary>
public record TelegramSettingsDto(
    string BotUsername, string BotName, bool Configured,
    string Channel = "", string PhoneMatchField = "parent",
    string ChannelStatus = "", string ChannelMessage = "",
    EnvSecretDto? Token = null);
/// <summary>Telegram bot sozlamasini saqlash so'rovi. <c>BotToken</c> ATAYIN qoldirilgan: eski
/// mijoz token yuborsa jimgina e'tiborsiz qolmasdan tushunarli xato qaytadi (token — .env da).</summary>
public record SaveTelegramSettingsRequest(
    string? BotToken, string? BotUsername, string? BotName, string? Channel, string? PhoneMatchField);

/// <summary>
/// MAXFIY QIYMAT HOLATI — qiymatning O'ZI hech qachon UI'ga qaytmaydi (u faqat serverda, .env da).
/// <c>EnvKey</c> — <c>.env</c> dagi o'zgaruvchi nomi (masalan "GEMINI_API_KEY"), <c>Configured</c> —
/// qiymat berilganmi. Sozlamalar sahifasi shu ikkisi bilan "sozlangan / qanday sozlash" ni ko'rsatadi.
/// </summary>
public record EnvSecretDto(string EnvKey, bool Configured);
/// <summary>Telegram backup konfiguratsiyasi — DB'dan o'qilgan holat.</summary>
public record TelegramBackupConfigDto(
    string? AdminChatId,
    int ScheduleHour,
    int ScheduleMinute,
    bool Enabled,
    DateTime? LastSentAt);
/// <summary>Telegram backup sozlamasini yangilash so'rovi.</summary>
public record SaveTelegramBackupConfigRequest(
    string? AdminChatId,
    int ScheduleHour,
    int ScheduleMinute,
    bool Enabled);
/// <summary>Ilova (APK) sozlamasi — Telegram bot ro'yxatdan o'tgan o'quvchi/o'qituvchiga yuboradigan fayl(lar).
/// Nom + hajm (bayt; 0 = yuklanmagan).</summary>
public record AppApkSettingsDto(string StudentApkName, long StudentApkSize, string TeacherApkName, long TeacherApkSize);
/// <summary>Firebase (FCM push) sozlamasi. <b>ServiceAccount</b> — server push yuborishi uchun
/// (MAXFIY: faqat .env, qiymati qaytmaydi). <b>WebConfigJson</b> + <b>VapidKey</b> — brauzer/PWA push
/// token olishi uchun (OMMAVIY, bazada saqlanadi va UI'dan kiritiladi).
/// Configured = native push tayyor; WebConfigured = web/PWA push tayyor.</summary>
public record FirebaseSettingsDto(
    bool Configured, string WebConfigJson, string VapidKey, bool WebConfigured,
    EnvSecretDto? ServiceAccount = null);
/// <summary><c>ServiceAccountJson</c> endi qabul qilinmaydi (bo'sh bo'lmasa — xato); u .env dan.</summary>
public record SaveFirebaseSettingsRequest(string? ServiceAccountJson, string? WebConfigJson, string? VapidKey);

/// <summary>Ommaviy (autentifikatsiyasiz) web/PWA push konfiguratsiyasi — brauzer Firebase JS SDK'ni
/// ishga tushirib FCM token olishi uchun. Maxfiy emas (apiKey/vapidKey ommaviy kalitlar).</summary>
public record PublicPushConfigDto(string WebConfigJson, string VapidKey, bool Configured);

/// <summary>Turniket/FaceID integratsiya sozlamasi (o'qituvchilar davomati avtomatik).
/// Parol javobda BO'SH qaytadi (xavfsizlik); HasPassword saqlanganini bildiradi.</summary>
public record TurnstileSettingsDto(
    bool Enabled, string Vendor, string Host, int Port, string Username, bool HasPassword,
    string WorkStartTime, int LateGraceMinutes, string LastSync,
    List<TeacherDeviceMapDto> Teachers,
    // Qurilma login/paroli endi .env dan (TURNSTILE_USERNAME / TURNSTILE_PASSWORD) — UI'dan kiritilmaydi.
    EnvSecretDto? Credentials = null);
/// <summary>O'qituvchi ↔ qurilma ID moslamasi.</summary>
public record TeacherDeviceMapDto(string TeacherId, string FullName, string DeviceUserId);
/// <summary>Turniket sozlamasini saqlash so'rovi. Password null/bo'sh = o'zgartirilmaydi (eski saqlanadi).</summary>
public record SaveTurnstileSettingsRequest(
    bool Enabled, string? Vendor, string? Host, int? Port, string? Username, string? Password,
    string? WorkStartTime, int? LateGraceMinutes, List<TeacherDeviceMapDto>? Teachers);

/* ---------- Finance (Moliya) ---------- */
public record FinanceTransactionDto(
    string Id, string Date, string Direction, string Category, decimal Amount,
    string? Note, string? StudentId, string? StudentName, string? TeacherId, string? TeacherName,
    string? Month, string? GroupId = null, string? Comment = null, string? Method = null,
    string? GroupName = null, string? CreatedAt = null,
    // Bu to'lovdan (income+tuition) jami qancha VOZVRAT qilingani (>0 bo'lsa qisman/to'liq qaytarilgan).
    decimal Refunded = 0m,
    // Bu yozuvning O'ZI vozvrat bo'lsa — qaysi asl to'lov uchun.
    string? RefundOfId = null,
    // Qog'oz kvitansiya raqami (naqd to'lov, "KV...") va karta to'lovining haqiqiy vaqti ("HH:mm").
    string? ReceiptNo = null,
    string? PaidTime = null,
    // Karta raqamining oxirgi 4 raqami (karta to'lovi) — jadvalda "•••• 1234" bo'lib ko'rinadi.
    string? CardLast4 = null,
    // KIM KIRITGAN (kassir/admin F.I.Sh) — "To'lovlar" va "Kunlik hisobot" jadvallarida ko'rinadi.
    string? CreatedBy = null,
    // Kiritgan xodimning AKKAUNT id'si (eski yozuvlarda null) — "Kiritgan" filtri ism emas,
    // aynan akkaunt bo'yicha ajratishi uchun (bir xil ismli ikki xodim aralashmasin).
    string? CreatedById = null);

/// <summary>
/// "Kiritgan" filtri uchun bitta variant — TO'LOV KIRITA OLADIGAN xodim/admin. Ro'yxatga
/// ruxsati borlarning HAMMASI kiradi (hali to'lov kiritmagan bo'lsa ham) + davr ichida
/// to'lov kiritgan eski/o'chirilgan akkauntlar. <paramref name="Key"/> — CashierReport bilan
/// bir xil kalit (akkaunt id'si yoki "name:F.I.Sh").
/// </summary>
public record PaymentAuthorDto(string Key, string? Id, string Name, string Position, bool CanEnter);

/// <summary>O'quvchi to'lovini (income+tuition) qisman/to'liq VOZVRAT qilish — FAQAT superadmin.
/// Muzlatishdan hosil bo'lgan avans shu orqali qaytariladi (balans 0 ga tushadi), o'qituvchi foizi net'dan.</summary>
public record RefundPayload(decimal Amount, string? Date = null, string? Reason = null);

/// <summary>Bitta vozvrat yozuvi (tarix uchun) — asl to'lov ma'lumoti bilan.</summary>
public record RefundDto(
    string Id, string Date, decimal Amount, string? StudentId, string? StudentName,
    string? GroupId, string? GroupName, string? Month, string? Reason,
    string? PaymentId, decimal? PaymentAmount, string? PaymentDate, string? CreatedBy, string? CreatedAt);
/* ---------- Support Telegram (bot foydalanuvchisi ↔ admin) ---------- */
public record BotThreadDto(long ChatId, string Name, string Username, string Phone, string Linked,
    string StartedAt, string? LastMessageAt, string LastText, int Unread);
public record BotSupportMessageDto(string Id, bool FromUser, string Text, string AdminName, string CreatedAt);
public record BotSupportReplyRequest(string? Text);

/* ---------- To'lov cheki (termal kvitansiya) ---------- */
public record CheckSettingsDto(string Json);
public record ReceiptDto(
    string ReceiptNo, string DateTime, string StudentName, string TeacherName,
    string ResponsibleName, string GroupName, string Method, string? Comment, decimal? Total,
    string CenterName, string CenterPhone, string CenterAddress, string LogoUrl, string SettingsJson,
    string? Subtitle = null,
    /// <summary>QOG'OZ kvitansiya raqami ("KV...") — naqd to'lovda kiritilgan bo'lsa chekda ham chiqadi.</summary>
    string? KvNo = null);
/// <summary>
/// O'quvchi to'lovini (income+tuition) tahrirlash — FAQAT superadmin. Sana/summa/oy/guruh/usul/izoh.
/// O'quvchi o'zgartirilmaydi (boshqa o'quvchiga o'tkazish uchun o'chirib, qaytadan kiritiladi).
/// </summary>
public record PaymentEditPayload(
    string Date, decimal Amount, string Month,
    string? GroupId = null, string? Method = null, string? Comment = null,
    string? ReceiptNo = null, string? PaidTime = null, bool ForceReceipt = false,
    string? CardLast4 = null);

public record FinanceTransactionPayload(
    string Date, string Direction, string Category, decimal Amount, string? Note,
    string? StudentId, string? TeacherId, string? Month = null, string? GroupId = null, string? Comment = null,
    string? Method = null, string? ReceiptNo = null, string? PaidTime = null, bool ForceReceipt = false,
    string? CardLast4 = null);
public record CategoryAmountDto(string Category, decimal Amount);
public record FinanceSummaryDto(
    decimal TotalIncome, decimal TotalExpense, decimal Net,
    decimal TuitionIncome, decimal OtherIncome,
    List<CategoryAmountDto> IncomeByCategory, List<CategoryAmountDto> ExpenseByCategory,
    decimal StudentDebt, decimal StudentAdvance, int TransactionsCount);
public record FinanceMonthlyDto(string Month, decimal Income, decimal Expense);
public record AccrueResultDto(List<string> Months, int Count, decimal Total);

/* ---------- Ilova bildirishnomalari (o'quvchi/o'qituvchi tarixi) ---------- */
public record UserNotificationDto(string Id, string Title, string Body, string Type, string CreatedAt, bool Read, bool Confirmed);
public record NotificationsResponseDto(int Unread, List<UserNotificationDto> Items);

/* ---------- Kurs/guruh kesimida moliyaviy hisobot ---------- */
/// <summary>Bitta kurs (Subject) bo'yicha davr hisobi: hisoblangan/yig'ilgan, yig'ilish foizi,
/// to'liq to'lagan o'quvchilar nisbati. Daromad bo'yicha saralanadi.</summary>
public record CourseFinanceRowDto(
    string CourseId, string CourseName, decimal Price,
    int GroupCount, int StudentCount,
    decimal Billed, decimal Collected, decimal CollectionPct,
    int FullyPaidStudents, int BillableStudents, decimal PaidPct);
/// <summary>Bitta guruh bo'yicha davr hisobi (qaysi o'qituvchi guruhi faolroq). TeacherId — frontend
/// o'qituvchi filtri uchun. CollectedCash — yig'ilganning NAQD (Method=="cash") ulushi
/// (frontend "Naqd jami" kartasi; o'qituvchi tanlanganda uning guruhlari bo'yicha yig'iladi).</summary>
public record GroupFinanceRowDto(
    string GroupId, string GroupName, string CourseName, string TeacherId, string TeacherName,
    int StudentCount, decimal Billed, decimal Collected, decimal CollectedCash, decimal CollectionPct,
    int FullyPaidStudents, int BillableStudents);
public record CourseFinanceReportDto(
    string From, string To,
    decimal TotalBilled, decimal TotalCollected, decimal TotalCollectedCash, decimal CollectionPct,
    List<CourseFinanceRowDto> Courses,
    List<GroupFinanceRowDto> Groups);

/* ---------- Bitta guruh ichidagi to'lov holati (kim to'ladi / kim to'lamadi) ---------- */
/// <summary>Guruhdagi bitta o'quvchining davr bo'yicha to'lov holati.
/// Status — a'zolik holati (trial/active/frozen). FullyPaid — hisoblangan to'liq qoplangan.</summary>
public record GroupPaymentRowDto(
    string StudentId, string FullName, string Status,
    decimal Billed, decimal Collected, decimal Debt, bool FullyPaid, bool HasPaid);
/// <summary>Bitta guruh ichidagi to'lov hisobi: kim to'ladi (FullyPaid), kim to'lamadi.</summary>
public record GroupPaymentsReportDto(
    string GroupId, string GroupName, string From, string To,
    decimal Billed, decimal Collected,
    int PaidCount, int UnpaidCount, int StudentCount,
    List<GroupPaymentRowDto> Rows);

/* ---------- O'quvchi to'lov tarixi (ledger) ---------- */
/// <summary>Bitta oyning hisobi.
/// Charged = to'liq oylik (guruh narxi); Discount = shu oy uchun berilgan chegirma;
/// Paid = haqiqiy naqd to'lov (tx); Remaining = Charged − Discount − Paid (manfiy bo'lsa 0).</summary>
/// <summary>Oydagi bitta kurs ulushi (qaysi kursga qancha) — to'lov tarixida breakdown uchun.
/// GroupId — shu ulush qaysi guruh hisobiga tegishli (null = guruhsiz/ClassName); super admin
/// shu guruhning oylik hisobini alohida tahrirlashi uchun (ko'p guruhli o'quvchi).</summary>
/// <summary>Oy hisobining bitta qatori: qaysi GURUH uchun (GroupName — asosiy) va uning kursi
/// (CourseName). O'quvchi profilida "Guruh — Kurs" ko'rinishida chiqadi.</summary>
public record MonthCourseDto(string CourseName, decimal Fee, string? GroupId = null, string? GroupName = null);
public record MonthLedgerDto(
    string Month, decimal Charged, decimal Discount, decimal Paid, decimal Remaining, string Status,
    List<MonthCourseDto> Courses, string? GroupId = null);
/// <summary>Bitta to'lov yozuvi (kassa). GroupName/CourseName — to'lov QAYSI guruh (va uning kursi)
/// uchun qilingani; TeacherName — o'sha guruh o'qituvchisi (to'lov guruhga teglanmagan bo'lsa null).
/// O'quvchi profilida "Guruh — Kurs" ko'rinishida chiqadi.</summary>
/// <summary>
/// Bitta to'lov yozuvi (o'quvchi to'lov tarixi va o'qituvchi maosh to'lovlari uchun umumiy).
/// <para><paramref name="ReceiptNo"/> — NAQD to'lovda kassir kiritgan QOG'OZ kvitansiya raqami
/// ("KV000123"); <paramref name="PaidTime"/> — KARTA to'lovida pul o'tkazilgan haqiqiy vaqt ("HH:mm");
/// <paramref name="CardLast4"/> — kartaning OXIRGI 4 raqami ("1234", to'liq raqam hech qachon
/// saqlanmaydi). Uchalasi ham to'lov oynasida kiritilmagan bo'lsa <c>null</c> (eski yozuvlar ham).</para>
/// </summary>
public record PaymentDto(string Date, decimal Amount, string? Note, string? Month, string? Comment, string? Method = null,
    string? GroupName = null, string? TeacherName = null, string? CourseName = null,
    string? ReceiptNo = null, string? PaidTime = null, string? CardLast4 = null);
/// <summary>To'lov oynasi uchun BITTA guruh bo'yicha oylik hisob: shu guruhning oylik to'lovi (chegirma
/// ayirilgan), shu guruhga teglangan to'langan summa va qoldiq. Aggregate emas — faqat shu guruh.</summary>
public record GroupMonthDto(string Month, decimal Fee, decimal Paid, decimal Remaining, string Status);
public record GroupLedgerDto(string GroupId, string GroupName, string CourseName, List<GroupMonthDto> Months);
public record StudentLedgerDto(
    StudentDto Student, decimal Balance, decimal MonthlyFee,
    decimal TotalCharged, decimal TotalDiscount, decimal TotalPaid,
    List<MonthLedgerDto> Months, List<PaymentDto> Payments);
/// <summary>Super admin: oylik HISOBLANGAN summani qo'lda tahrirlash.</summary>
public record EditChargeRequest(decimal Amount);

/* ---------- O'zgarishlar tarixi (audit) ---------- */
public record AuditLogDto(
    string Id, string EntityType, string EntityId, string Action, string Timestamp,
    string? ActorName, string Summary, string? Before, string? After,
    string? StudentId, string? TeacherId,
    /// <summary>Yozuv qaysi BO'LIMGA tegishli (<c>AuditSections.SectionOf</c>) — klient uni
    /// o'zi hisoblamasin: `EntityType` nomlari tarixiy sabablarga ko'ra aldamchi.</summary>
    string Section = "other");

/// <summary>Tarixdagi bitta bo'lim: kalit + nom + shu filtrlarda nechta yozuv borligi.</summary>
public record AuditSectionDto(string Key, string Label, int Count);

/// <summary>"O'zgarishlar tarixi" sahifasining boshlang'ich ma'lumoti (chiplar + xodim filtri).</summary>
/// <param name="Total">Barcha bo'limlar bo'yicha jami (chiplardagi "Hammasi").</param>
/// <param name="Actors">Tarixda uchragan xodim nomlari (filtr ro'yxati uchun).</param>
public record AuditSectionsDto(List<AuditSectionDto> Sections, int Total, List<string> Actors);

/* ---------- Teacher portal (ilova) ---------- */
/// <summary>O'qituvchining o'z profili (ilovada ko'rsatish uchun).</summary>
public record TeacherProfileDto(
    string Id, string FullName, string Email, string HomeroomClass, List<SubjectDto> Subjects,
    List<string> Permissions, string? PhotoUrl = null, bool IsSupport = false);

/* ---------- Support o'qituvchi (bo'sh vaqt slot + bron) ---------- */
/// <summary>Admin "Support" ro'yxati elementi — support o'qituvchi + slot statistikasi.</summary>
public record SupportTeacherDto(
    string Id, string FullName, string Phone, string? PhotoUrl,
    int OpenCount, int BookedCount, int DoneCount);
/// <summary>Bitta support slot/dars yozuvi (admin va o'qituvchi ko'rinishi uchun umumiy).</summary>
public record SupportSlotDto(
    string Id, string TeacherId, string Date, string StartTime, string EndTime, string Status,
    string? StudentId, string StudentName, string Topic, string Notes, string? BookedAt);
/// <summary>Admin: bitta support o'qituvchi tafsiloti + uning slot/darslari (eng yangi birinchi).</summary>
public record SupportTeacherDetailDto(
    string Id, string FullName, string Phone, string? PhotoUrl, List<SupportSlotDto> Slots);
/// <summary>Support slot yaratish: sana + bo'sh vaqt bloki (StartTime..EndTime). SlotMinutes>0 bo'lsa
/// blok HAR ODAMGA shuncha daqiqalik bron-slotlarga bo'linadi (masalan 1 soat + 30 → 2 slot).
/// SlotMinutes=0 → butun blok bitta slot. RepeatWeeks>0 — shu hafta kuni keyingi N haftaga ham.</summary>
public record CreateSupportSlotRequest(
    string Date, string StartTime, string EndTime, int SlotMinutes = 0, int RepeatWeeks = 0,
    string? RepeatMode = null, string? EndDate = null);
/// <summary>Support dars yopish: mavzu + izoh.</summary>
public record CompleteSupportRequest(string Topic, string Notes);
/// <summary>O'quvchi ko'rinishidagi support o'qituvchi — bo'sh slotlari bilan.</summary>
public record StudentSupportTeacherDto(
    string TeacherId, string FullName, string? PhotoUrl, string Subject, List<StudentSupportSlotDto> OpenSlots);
/// <summary>O'quvchi uchun bo'sh slot (bron qilish mumkin).</summary>
public record StudentSupportSlotDto(string Id, string Date, string StartTime, string EndTime);
/// <summary>O'quvchining support broni (o'z bronlari ro'yxati).</summary>
public record StudentSupportBookingDto(
    string Id, string TeacherId, string TeacherName, string Date, string StartTime, string EndTime,
    string Status, string Topic, string Notes);
/// <summary>O'quvchi support ekrani: bo'sh slotli supportlar + mening bronlarim.</summary>
public record StudentSupportDto(
    List<StudentSupportTeacherDto> Supports, List<StudentSupportBookingDto> MyBookings);
/// <summary>O'quvchi profilidagi support feedback — support o'qituvchining o'tilgan darsi (mavzu+izoh).</summary>
public record StudentSupportFeedbackDto(
    string Date, string StartTime, string EndTime, string TeacherName, string Topic, string Notes);
/// <summary>O'qituvchi dars beradigan bitta guruh (qaysi kurslarni o'qitishi).</summary>
public record TeacherClassDto(
    string ClassId, string ClassName, int Grade, List<SubjectDto> Subjects);
/* ---------- Student portal (ilova) ---------- */
/// <summary>O'quvchining o'z profili (ilovada ko'rsatish uchun).</summary>
public record StudentProfileDto(
    string Id, string FullName, string ClassName, string BirthDate, string Gender,
    string ParentFullName, string ParentPhone, string EnrollmentDate,
    string? PhotoUrl = null, string? ParentPhotoUrl = null);
/// <summary>O'quvchi jadvalidagi bitta dars (fan, o'qituvchi, kun, dars raqami, vaqt).</summary>
public record StudentLessonDto(
    int Day, int Period, string? StartTime, string? EndTime,
    string SubjectId, string SubjectName, string TeacherId, string TeacherName);
/// <summary>O'quvchi uchun dars mavzusi va uyga vazifa (sana + fan bo'yicha).
/// Shu o'quvchining o'sha (sana + dars raqami) jurnal yozuvi bo'lsa — Grade va Reason ham
/// bog'lab qaytariladi (bugungi/haftalik baholarni alohida endpoint'siz ko'rsatish uchun).</summary>
public record HomeworkItemDto(
    string Date, int Period, string SubjectId, string SubjectName,
    string Topic, string? Homework, bool Conducted,
    int? Grade, string? ReasonId, string? ReasonName, bool IsLate);

/* ---------------------------------------------------------------------------------------------
 *  O'QUVCHI ILOVASI — «Umumiy statistika» (sana oralig'i bo'yicha jurnal).
 *  GET /api/student/journal?from=&to=&groupId=  →  StudentPeriodJournalDto
 *  Mantiq: StudentJournalBuilder.PeriodAsync (admin jurnal modali bilan YAGONA yadro).
 * ------------------------------------------------------------------------------------------- */

/// <summary>
/// Oraliqdagi BITTA dars — o'quvchi shu darsda nima olgani (baho, davomat, uyga vazifa, xulq).
/// <para><c>Grade</c> — jurnal bahosi (yo'q bo'lsa null); <c>ReasonName</c>/<c>ReasonShort</c> —
/// davomat sababi (kelmadi/kechikdi), <c>IsLate</c> bo'lsa o'quvchi darsda QATNASHGAN hisoblanadi.
/// <c>HomeworkMark</c>: 0=belgilanmagan, 1=qildi, 2=qilmadi, 3=chala; <c>Behavior</c>: 0/1=yaxshi/2=yomon.
/// <c>HomeworkText</c> — o'qituvchi bergan uyga vazifa MATNI (<c>LessonNote.Homework</c>), bahosi emas.</para>
/// </summary>
public record StudentPeriodLessonDto(
    string Date, int Period,
    string GroupId, string GroupName, string SubjectId, string SubjectName,
    string Topic, string HomeworkText,
    bool Conducted, bool Present,
    int? Grade, string? ReasonName, string? ReasonShort, bool IsLate,
    int HomeworkMark, int Behavior, MasteryLevel? Mastery);

/// <summary>
/// Oraliq jamlanmasi. <c>Held</c> — hisobga kirgan (o'tilgan, o'quvchiga tegishli, holati MA'LUM)
/// darslar soni; <c>AttendancePct</c> = attended×100/held (held=0 bo'lsa 0). <c>AvgGrade</c> —
/// 1 xonagacha yaxlitlangan, baho bo'lmasa 0.
/// </summary>
public record StudentPeriodSummaryDto(
    int Held, int Attended, int Absent, int Late, int AttendancePct,
    int GradesCount, double AvgGrade,
    int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad);

/// <summary>Fan (kurs) kesimi — o'quvchi qaysi kursda qanday ko'rsatkichga ega.</summary>
public record StudentPeriodSubjectDto(
    string SubjectId, string SubjectName, int Held, int Attended, int GradesCount, double AvgGrade);

/// <summary>
/// O'quvchining SANA ORALIG'IDAGI jurnali (hafta/oy). <c>Groups</c> — o'quvchining barcha guruhlari
/// (ilovadagi tanlov ro'yxati; <c>GroupId</c> filtri qo'yilgan bo'lsa ham to'liq qaytadi).
/// <c>Lessons</c> — faqat O'TILGAN va o'quvchiga tegishli darslar, sana bo'yicha tartiblangan.
/// </summary>
public record StudentPeriodJournalDto(
    string From, string To, string GroupId,
    List<StudentJournalGroupDto> Groups,
    StudentPeriodSummaryDto Summary,
    List<StudentPeriodSubjectDto> Subjects,
    List<StudentPeriodLessonDto> Lessons);

/// <summary>O'quvchining bitta davomatsizlik (yoki kech qolish) yozuvi.</summary>
public record StudentAbsenceRowDto(
    string Date, int Period, int Quarter,
    string SubjectId, string SubjectName,
    string ReasonId, string ReasonName, bool IsLate, bool IsIll);

/// <summary>O'quvchi davomati — chorak bo'yicha umumiy + kunlik ro'yxat.</summary>
public record StudentAttendanceFullDto(
    StudentAttendanceDto Summary, List<StudentAbsenceRowDto> Rows);

/// <summary>Bosh sahifa uchun yagona payload — bir chaqiruvda hammasi.</summary>
public record StudentDashboardDto(
    StudentProfileDto Profile, PortalMetaDto Meta,
    List<StudentLessonDto> TodayLessons, List<HomeworkItemDto> TodayGrades,
    decimal Balance, decimal MonthlyFee);

/// <summary>
/// O'quvchining guruhi (ilova uchun) — FAOL ham, tugagan/chiqilgan ham qaytadi, hech biri
/// jimgina yo'qolmaydi (`GET /api/student/groups`).
///
/// <para><paramref name="State"/> — ilova ko'rsatadigan YAGONA holat, server hisoblaydi:
/// <c>active</c> (faol) · <c>trial</c> (sinov) · <c>frozen</c> (muzlatilgan) ·
/// <c>finished</c> (chiqilgan/yakunlangan yoki guruh yopilgan). Qoidani ikki tilda
/// takrorlamaslik uchun ATAYIN shu yerda hisoblanadi.</para>
///
/// <para><paramref name="Days"/>: 0=Dushanba … 6=Yakshanba.
/// <paramref name="Status"/> — a'zolikning xom holati (trial|active|frozen|completed),
/// <paramref name="GroupArchived"/> — guruhning o'zi yopilgan/arxivlangan.</para>
/// </summary>
public record StudentGroupItemDto(
    string GroupId, string Name, string CourseName, string TeacherName,
    List<int> Days, string StartTime, string EndTime, string Room,
    string State, string Status, bool IsActive, bool GroupArchived,
    string JoinedAt, string LeftAt);

/// <summary>O'quvchi/foydalanuvchi shaxsiy sozlamasi (til, tema, bildirishnoma).</summary>
public record UserSettingsDto(string Language, string Theme, bool NotificationsEnabled);

/// <summary>Sozlamani yangilash so'rovi. Berilgan maydonlar yangilanadi, qolganlari saqlanadi.</summary>
public record SaveUserSettingsRequest(string? Language, string? Theme, bool? NotificationsEnabled);

/// <summary>Push qurilma tokenini ro'yxatdan o'tkazish so'rovi.</summary>
public record RegisterDeviceRequest(string Token, string? Platform, string? DeviceName, string? AppId);

/// <summary>Portal umumiy konteksti: dars vaqtlari, davomat sabablari, davr(lar) + joriy chorak/hafta.</summary>
public record PortalMetaDto(
    List<LessonTimeDto> LessonTimes,
    List<AbsenceReasonDto> AbsenceReasons, List<QuarterPeriodDto> Quarters,
    int CurrentQuarter = 1, int CurrentWeek = 1);

/* ---------- Messaging (chat + e'lon + telegram) ---------- */

/// <summary>Guruh chatidagi bitta xabar. CreatedAt — ISO 8601 ("o" formati).</summary>
public record ChatMessageDto(
    string Id, string ClassName, string SenderUserId, string SenderName,
    string SenderRole, string Text, string CreatedAt);

/// <summary>Chatga xabar yuborish so'rovi (guruh URL'dan keladi).</summary>
public record SendChatRequest(string Text);

/// <summary>Admin uchun guruh chat/e'lon ro'yxati elementi.</summary>
public record ChatClassDto(
    string Name, int Grade, int StudentCount, int ParentCount, string? LastMessageAt);

/// <summary>Yuborilgan e'lon (Telegram). CreatedAt — ISO 8601.</summary>
public record BroadcastDto(
    string Id, string ClassName, string Text, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount);

/// <summary>
/// E'lon yuborish so'rovi. <c>Scope</c>: "class" (ClassName guruhi), "all" (barcha guruh),
/// "selected" (StudentIds/TeacherIds tanlangan o'quvchilar/o'qituvchilar). <c>OnlyDebtors</c> — faqat balansi manfiylar.
/// <c>Text</c> ichida o'rinbosarlar bo'lishi mumkin: {fish} {sinf} {qarzdorlik} {balans} {ota-ona} {telefon}.
/// </summary>
public record SendBroadcastRequest(
    string? Scope, string? ClassName, bool OnlyDebtors, List<string>? StudentIds, string Text,
    List<string>? TeacherIds = null);

/// <summary>Telegramda ro'yxatdan o'tgan ota-ona. ChatId string (JS aniqligi uchun). Balance — qarz aniqlash uchun.</summary>
public record TelegramParentDto(
    string StudentId, string StudentName, string ClassName, decimal Balance,
    string ParentName, string Phone, string ChatId, string CreatedAt);

/// <summary>Telegramda ro'yxatdan o'tgan o'qituvchi (xodim ro'yxati) — "Tanlab" e'lon uchun.</summary>
public record TelegramTeacherDto(
    string TeacherId, string TeacherName, string Phone, string ChatId, string CreatedAt);

/// <summary>
/// Ilovaga push yuborish so'rovi. Audience: "parents" (ClassName ixtiyoriy) | "teachers" |
/// "selected" (UserIds tanlangan foydalanuvchilar).
/// </summary>
public record SendPushRequest(string Audience, string? ClassName, List<string>? UserIds, string Title, string Body);
/// <summary>Push uchun tanlanadigan oluvchi. UserId — akkaunt id; HasDevice = qurilma ulangan (push yetadi).</summary>
public record PushRecipientDto(string UserId, string Name, string Group, string Detail, bool HasDevice);
/// <summary>Yuborilgan push (tarix). CreatedAt — ISO. ConfirmedCount/TargetCount — tasdiqlash holati.</summary>
public record PushMessageDto(
    string Id, string Audience, string Title, string Body, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount, int ConfirmedCount = 0, int TargetCount = 0);
/// <summary>Bitta e'lon (broadcast) bo'yicha oluvchining tasdiqlash holati — admin ko'rishi uchun.</summary>
public record PushConfirmationDto(string Name, string Group, bool Confirmed, string? ConfirmedAt);

/* ---------- SMS (Eskiz.uz) ---------- */
/// <summary>
/// SMS yuborish so'rovi. Audience: "parents" (o'quvchi ota-onasi raqami) | "students" (o'quvchi raqami) |
/// "teachers" (o'qituvchi raqami) | "selected" (StudentIds — ota-ona/o'quvchi raqami + TeacherIds — o'qituvchi
/// raqami). ClassName ixtiyoriy (parents/students uchun guruh filtri). OnlyDebtors — faqat qarzdorlar.
/// ToParent — "selected"da o'quvchilar uchun ota-ona (true) yoki o'quvchi (false) raqamiga (o'qituvchilarga
/// taalluqli emas — doim o'z raqamiga). Text ichida o'rinbosarlar bo'lishi mumkin ({fish} {sinf} {qarzdorlik} {balans} {telefon}).
/// </summary>
public record SendSmsRequest(string Audience, string? ClassName, bool OnlyDebtors, List<string>? StudentIds, string Text,
    bool ToParent = true, List<string>? TeacherIds = null, string? Provider = null, string? AgentId = null);
/// <summary>Yuborilgan SMS partiyasi (tarix). CreatedAt — ISO. Provider: eskiz|local.
/// <paramref name="Queued"/> — partiya FONDA yuborilmoqda (ko'p oluvchi): SentCount hali 0, holatni
/// <c>GET sms/{id}/progress</c> dan kuzatiladi. Qarang: <c>SmsQueueService</c>.</summary>
public record SmsBatchDto(
    string Id, string Audience, string Message, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount, string Provider, bool Queued = false);
/// <summary>Ommaviy SMS partiyasining jonli holati: jami / ishlangan / yuborilgan + tugaganmi.</summary>
public record SmsProgressDto(string Id, int Total, int Done, int Sent, bool Finished);
/// <summary>Bitta SMS jurnali (raqam bo'yicha) — partiya tafsilotida ko'rsatiladi. Provider: eskiz|local.</summary>
public record SmsLogDto(string Id, string PhoneNumber, string RecipientName, string Status, string CreatedAt, string Provider);
/// <summary>SMS holati — admin UI uchun: Eskiz sozlangan/sender/balans + Local SMS yoqilganmi/standart agent.</summary>
public record SmsStatusDto(bool Configured, string From, decimal? Balance, bool LocalEnabled, string? LocalDefaultAgentId);
/// <summary>SMS andozasi (shablon) — faqat QO'LDA yuborish uchun (avto-hodisalar AutoMessageRule'da).</summary>
public record SmsTemplateDto(string Id, string Name, string Text, int Order);
/// <summary>SMS andozasini saqlash (yaratish/tahrirlash).</summary>
public record SaveSmsTemplateRequest(string Name, string Text);
/// <summary>Lidga SMS yuborish so'rovi (lid telefon raqamiga).</summary>
public record SendLeadSmsRequest(string LeadId, string Text, string? Provider = null, string? AgentId = null);
/// <summary>Bir nechta lidga birdan SMS yuborish so'rovi.</summary>
public record SendLeadBulkSmsRequest(List<string> LeadIds, string Text, string? Provider = null, string? AgentId = null);
/// <summary>Ommaviy lid SMS natijasi: yuborildi / xato / raqamsiz lidlar soni.
/// <paramref name="Queued"/> — partiya FONDA yuborilmoqda (u holda Sent hali 0 va
/// <paramref name="QueuedCount"/> ta lid navbatda; holat <c>sms/{BatchId}/progress</c> dan o'qiladi).</summary>
public record LeadBulkSmsResultDto(
    int Sent, int Failed, int NoPhone, bool Queued = false, int QueuedCount = 0, string? BatchId = null);
/// <summary>Eskiz callback (yetkazib berish holati webhook'i) tanasi.</summary>
public record EskizCallbackDto(string? request_id, string? message_id, string? phone_number, string? status, string? status_date);
/// <summary>SMS (Eskiz) sozlamasi holati. Login/parol .env dan (qiymat qaytmaydi) — faqat
/// jo'natuvchi nomi (From) UI'dan o'zgartiriladi.</summary>
public record EskizSettingsDto(
    string Email, string From, bool Configured, decimal? Balance,
    EnvSecretDto? Login = null, EnvSecretDto? Password = null);
/// <summary>SMS (Eskiz) login/parol/sender saqlash so'rovi (bo'sh qoldirilsa eski saqlanadi).</summary>
public record SaveEskizRequest(string? Email, string? Password, string? From);
/// <summary>Local SMS (CTI agent telefonidan) sozlamasi holati. DelaySeconds — massaviy yuborishda
/// ikkita SMS orasidagi kutish (soniya).</summary>
public record LocalSmsSettingsDto(bool Enabled, string? DefaultAgentId, int DelaySeconds);
/// <summary>Local SMS sozlamasini saqlash — DefaultAgentId bo'sh/null bo'lsa standart agent tozalanadi.</summary>
public record SaveLocalSmsRequest(bool Enabled, string? DefaultAgentId, int DelaySeconds);
/// <summary>
/// "Tanlab" SMS uchun oluvchi (o'quvchi). ParentPhone = ParentPhone→FatherPhone→MotherPhone birinchi
/// bo'sh bo'lmagani; StudentPhone = s.Phone. Ikkalasi ham bo'sh bo'lishi mumkin (raqam yo'q — UIda kulrang).
/// </summary>
public record SmsRecipientDto(string StudentId, string FullName, string ClassName, string? ParentPhone, string? StudentPhone);
/// <summary>"Tanlab" SMS uchun o'qituvchi oluvchi. Phone bo'sh bo'lishi mumkin (raqam yo'q — UIda kulrang).</summary>
public record SmsTeacherRecipientDto(string TeacherId, string FullName, string? Phone);
/// <summary>Birlashgan tayyor matn. Source: "sms" (Eskiz andozasi) | "auto" (avto-xabar qoidasi shabloni).</summary>
public record UnifiedTemplateDto(string Source, string Name, string Text);

/* ---------- Fayl yuklash ---------- */

/// <summary>Yuklangan fayl haqida ma'lumot (upload javobida).</summary>
public record UploadedFileDto(string Name, string Url, long Size, string ContentType);

/* ---------- Shartnomalar ---------- */

/// <summary>Foydalanuvchi aniqlagan qo'shimcha @-o'rinbosar (doimiy qiymat bilan).</summary>
public record ContractFieldDto(string Key, string Value);
/// <summary>Shartnoma andozasi (yuklangan Word YOKI custom matnli). Body bo'sh bo'lmasa — matnli andoza.
/// Fields — foydalanuvchi qo'shgan qo'shimcha o'rinbosarlar (doimiy qiymatli).</summary>
public record ContractTemplateDto(string Id, string Target, string Name, string FileUrl, string FileName, string Body, List<ContractFieldDto> Fields, string UploadedAt);
/// <summary>Shartnoma andozasini yaratish/tahrirlash so'rovi. Word fayl (FileUrl, avval /api/admin/uploads orqali)
/// YOKI custom matn (Body) — kamida bittasi bo'lishi shart. Fields — qo'shimcha doimiy o'rinbosarlar.</summary>
public record CreateContractTemplateRequest(string Target, string Name, string? FileUrl, string? FileName, string? Body, List<ContractFieldDto>? Fields);
/// <summary>O'quvchi oluvchi qatori (shartnoma o'quvchi bo'yicha tuziladi; Telegram — ota-ona ro'yxati orqali).</summary>
public record StudentRecipientDto(
    string StudentId, string FullName, string ParentName, string Phone, string Groups,
    bool Registered, int? LastNumber);
/// <summary>Xodim oluvchi qatori.</summary>
public record StaffRecipientDto(
    string TeacherId, string FullName, string Phone, bool Registered, int? LastNumber);
/// <summary>Shartnoma yuborish so'rovi.</summary>
public record SendContractsRequest(string Target, string TemplateId, List<string> RecipientKeys);
/// <summary>Bitta oluvchi uchun yuborish natijasi.</summary>
public record SendResultDto(string RecipientKey, bool Ok, int? Number, string Message);
/// <summary>Bitta oluvchi uchun shartnomani to'ldirib .docx YUKLAB OLISH so'rovi (Telegram shart emas).</summary>
public record BuildContractRequest(string Target, string TemplateId, string RecipientKey);
/// <summary>Shartnoma hujjati — portal/ilova va admin tarixi uchun.</summary>
public record ContractDocDto(
    string Id,
    int Number,
    string Title,          // "Shartnoma № 12"
    string Target,         // parent | staff
    string RecipientKey,
    string RecipientName,
    string TemplateName,
    string Date,           // ISO ("o") — SentAt
    string PdfUrl,         // superadmin yuklagan PDF; "" — hali yuklanmagan
    string DocxUrl,        // tizim hosil qilgan Word nusxa
    bool Delivered,
    string Status,
    bool Visible);      // ilovada (o'qituvchi/o'quvchi) ko'rinadimi
/// <summary>Tayyor PDF nusxani biriktirish so'rovi — fayl avval /api/admin/uploads orqali yuklanadi.</summary>
public record ContractPdfRequest(string FileUrl, string? FileName);
/// <summary>Shartnomani ilovada ko'rsatish/yashirish so'rovi.</summary>
public record ContractVisibilityRequest(bool Visible);

/* ---------- O'quv xonalari ---------- */

/// <summary>O'quv xonasi (auditoriya).</summary>
public record RoomDto(
    string Id, string Name, int Capacity, string? Building, string? Location,
    bool IsActive, string CreatedAt);

/// <summary>
/// Xona samaradorlik metrikasi — bandlik, o'quvchi soni, haftalik bandlik foizi.
/// GET /api/admin/rooms/utilization-dashboard
/// </summary>
public record RoomUtilizationDto(
    string RoomId,
    string RoomName,
    int Capacity,
    int CurrentStudents,
    int TotalSlots,
    int Gap,
    int GroupCount,
    double OccupancyPercent,
    int ActiveGroupCount,
    double WeeklyActiveHours,
    double WeeklyUtilizationPercent,
    int EfficiencyScore,
    string EfficiencyStatus,
    string Building,
    string Location,
    List<string> GroupNames);

/// <summary>Xona yaratish so'rovi.</summary>
public record CreateRoomRequest(string Name, int Capacity, string? Building, string? Location);

/// <summary>Xona tahrirlash so'rovi (IsActive bilan — faollashtirish/o'chirish).</summary>
public record UpdateRoomRequest(string Name, int Capacity, string? Building, string? Location, bool IsActive);

/// <summary>
/// Xona sig'im samaradorligi — Total Slots = Capacity × GroupCount,
/// Utilization = ActualStudents / TotalSlots * 100.
/// GET /api/admin/rooms/{id}/capacity
/// </summary>
public record RoomCapacityMetric(
    string RoomId,
    string RoomName,
    int Capacity,
    int GroupCount,
    int TotalSlots,
    int ActualStudents,
    double UtilizationPercent,
    int Gap,
    string Status,
    List<RoomGroupSlotDto> Groups);

/// <summary>Har bir guruhning o'quvchi soni (capacity modal uchun).</summary>
public record RoomGroupSlotDto(string GroupId, string GroupName, int StudentCount, string? CourseName);

/// <summary>
/// Bitta xona uchun unified metrika — karta va modal ikkalasi uchun bitta manba.
/// GET /api/admin/rooms/{id}/detail
/// ActualStudents = barcha guruhlardagi o'quvchilar yig'indisi (unique emas — har guruh alohida slot).
/// OccupancyPercent = ActualStudents / TotalSlots (barcha guruhlar birlashtirilgan).
/// UtilizationPercent = ActualStudents / (Capacity × GroupCount) — OccupancyPercent bilan bir xil.
/// </summary>
public record RoomDetailMetricDto(
    string RoomId,
    string RoomName,
    string Building,
    string Location,
    int Capacity,
    int GroupCount,
    int TotalSlots,  // = Capacity × GroupCount
    int ActualStudents,  // = SUM of all groups' active students (NOT unique)
    double OccupancyPercent,  // = ActualStudents / TotalSlots * 100
    double UtilizationPercent,  // = ActualStudents / (Capacity × GroupCount) — alias of OccupancyPercent
    double WeeklyUtilizationPercent,
    double WeeklyActiveHours,
    int EfficiencyScore,
    string EfficiencyStatus,
    int Gap,
    List<RoomGroupDetailDto> Groups);

/// <summary>Xona ichidagi har bir guruh haqida batafsil ma'lumot (detail modal uchun).</summary>
public record RoomGroupDetailDto(
    string GroupId,
    string GroupName,
    string CourseName,
    string TeacherName,
    int StudentCount,
    int StudentCapacity,
    double UtilizationPercent,
    string Days,
    string TimeSlot);

/* ---------- Boshqaruv ---------- */

/// <summary>Filial (branch).</summary>
public record BranchDto(
    string Id, string Name, string Address, double Latitude, double Longitude,
    int RadiusMeters, string CreatedAt);
public record BranchPayload(
    string Name, string Address, double Latitude, double Longitude, int RadiusMeters);

/// <summary>Xodim (o'qituvchi bo'lmagan ishchi) — admin akkaunti bilan.
/// <para><paramref name="Role"/> — akkaunt roli (<c>staff</c> | <c>admin</c> | <c>superadmin</c>).
/// "Xodimlar va rollar" ro'yxatida faqat xodimlar emas, admin/superadmin akkauntlar ham ko'rinadi —
/// aks holda superadminlikka ko'tarilgan odam ro'yxatdan yo'qolib, orqaga qaytarib bo'lmasdi.</para></summary>
public record StaffDto(string Id, string FullName, string Position, string Login, List<string> Permissions,
    string Phone = "", string Role = "staff");

/// <summary>Panel akkauntining ROLINI o'zgartirish — "ikkinchi superadmin" tayinlash yoki qaytarish.
/// Ruxsat etilgan qiymatlar: <c>superadmin</c> | <c>admin</c> | <c>staff</c>.</summary>
public record SetStaffRoleRequest(string Role);

/* ---------- Adminga topshiriq (xodim checklist) ---------- */
/// <summary>Chap ro'yxatdagi xodim (topshiriq biriktirish uchun). HasTelegram — bot orqali ro'yxatdan
/// o'tganmi (kunlik checklist yuborilishi uchun kerak). TaskCount — biriktirilgan topshiriqlar soni.</summary>
public record StaffTaskTargetDto(
    string UserId, string FullName, string Role, string Position, string Phone, bool HasTelegram, int TaskCount);
/// <summary>Bitta topshiriq (checklist bandi).</summary>
public record StaffTaskDto(string Id, string StaffUserId, string Title, int Order);
/// <summary>Topshiriq yaratish/tahrirlash payload'i.</summary>
public record StaffTaskInput(string Title);
/// <summary>Kunlik jo'natish sozlamalari.</summary>
public record StaffTaskSettingsDto(bool Enabled, int Hour, int Minute);
/// <summary>Tarix jadvalidagi bitta band: nomi + bajarildimi + qachon.</summary>
public record StaffTaskHistoryItemDto(string Title, bool Done, string? DoneAt);
/// <summary>Tarix jadvalidagi bitta xodim qatori (bir kun): jami/bajarilgan + bandlar.</summary>
public record StaffTaskHistoryRowDto(
    string UserId, string FullName, int Total, int Done, List<StaffTaskHistoryItemDto> Items);
/// <summary>Xodim yaratish/tahrirlash so'rovi.
/// <paramref name="Phone"/> — xodim telefoni (ixtiyoriy, max 32 belgi);
/// PhoneUtil.Normalize() orqali standartlashtirilib saqlanadi (format: +998-XX-XXX-XX-XX).
/// Kiritilsa kamita 7 ta raqam bo'lishi kerak. 998 prefiksi avtomatik qo'shiladi.
/// </summary>
public record StaffPayload(string FullName, string Position, string? NewPassword = null, string? Phone = null);
/// <summary>Xodimning admin bo'lim ruxsatlari (faqat superadmin o'zgartiradi).</summary>
public record SetStaffPermissionsRequest(List<string> Permissions);

/// <summary>Xodim roli shabloni — yangi xodim qo'shishda template tanlab olsa, default ruxsatlari avtomatik belgilanadi.</summary>
public record StaffRoleTemplateDto(string Id, string Code, string Name, string Description, List<string> DefaultPermissions);

/// <summary>Xodim yaratishda rolle shablonini tanlab, qo'shimcha ruxsatlari bilan qo'shish so'rovi.</summary>
public record CreateStaffWithTemplateRequest(
    string FullName, string Position, string? Phone = null, string? NewPassword = null,
    string? TemplateCode = null, List<string>? ExtraPermissions = null);

/// <summary>Taklif/shikoyat — admin ko'rinishi uchun (yuboruvchi roli/ismi + ixtiyoriy rasm bilan).</summary>
public record FeedbackDto(
    string Id, string StudentName, string ParentName, string ClassName,
    string Type, string Text, string CreatedAt, string Status,
    string SenderRole, string SenderName, string? ImageUrl);

/* ---------- O'quvchining O'QITUVCHI haqidagi fikri (admin yozadi, AI tahlil manbai) ---------- */

/// <summary>Bitta yozib qo'yilgan fikr. <c>CreatedAt</c> — ISO; ro'yxat shu bo'yicha
/// kamayish tartibida (eng yangisi tepada).</summary>
public record TeacherReviewDto(
    string Id, string TeacherId, string GroupId, string Text,
    string CreatedAt, string CreatedBy);

/// <summary>
/// O'quvchi profilidagi bitta BLOK — o'quvchining shu guruhdagi o'qituvchisi va u haqida
/// yozilgan fikrlar. O'quvchi 2+ guruhda o'qisa, shunday blok 2+ ta bo'ladi.
/// </summary>
/// <param name="MembershipStatus">A'zolik holati: active | trial | frozen | completed — admin
/// fikr yozayotganda o'quvchi hozir shu guruhda faolmi yoki yo'qmi ko'rib tursin.</param>
public record StudentTeacherReviewGroupDto(
    string GroupId, string GroupName, string CourseName,
    string TeacherId, string TeacherName,
    bool IsActive, string MembershipStatus,
    List<TeacherReviewDto> Reviews);

/// <summary>Yangi fikr yozish (admin/superadmin). Matn bo'sh bo'lmasligi kerak.</summary>
public record CreateTeacherReviewRequest(string TeacherId, string GroupId, string Text);

/// <summary>
/// O'QITUVCHI PROFILIDAGI «Fikrlar» bo'limi uchun bitta qator — shu o'qituvchi haqida yozilgan
/// fikr, kim (o'quvchi) va qaysi guruh bo'yicha ekani bilan. Eng yangisi tepada.
/// <para>DIQQAT: bu ADMIN ko'rinishi (o'quvchi ismi bor). O'qituvchining O'ZIGA — na o'qituvchi
/// portalida, na Flutter ilovasida — bu ma'lumot berilmaydi.</para>
/// </summary>
public record TeacherReviewFeedItemDto(
    string Id, string StudentId, string StudentName,
    string GroupId, string GroupName,
    string Text, string CreatedAt, string CreatedBy);

/// <summary>O'qituvchi «Fikrlar» bo'limi: jami soni + qatorlar (eng yangisi tepada).</summary>
public record TeacherReviewFeedDto(int Total, List<TeacherReviewFeedItemDto> Items);
/* ---------- Fan progresi (dars o'tilishiga qarab — LMS'siz) ----------
   Reja (Planned) = chorakdagi guruh jadvalidagi shu fan dars kataklari soni.
   O'tilgan (Conducted) = o'qituvchi "dars o'tildi" deb belgilagan (LessonNote.Conducted) darslar.
   Progress = Conducted / Planned. */

/// <summary>O'quvchi/ota-ona uchun bitta fan progresi.</summary>
public record SubjectProgressDto(
    string SubjectId, string SubjectName,
    int Planned,          // chorakdagi jami reja darslar
    int Conducted,        // o'tilgan (belgilangan) darslar
    int Remaining,        // qolgan = Planned - Conducted
    int Percent,          // Conducted/Planned (0..100)
    int ExpectedByToday,  // shu kungacha bo'lishi kerak edi (orqada/oldinda aniqlash uchun)
    string? NextLessonDate,  // keyingi hali o'tilmagan reja dars sanasi (ISO) yoki null
    string? LastLessonDate); // chorakdagi oxirgi reja dars sanasi (ISO) yoki null

/// <summary>O'quvchining barcha fanlari bo'yicha umumiy + har bir fan progresi.</summary>
public record StudentSubjectsProgressDto(
    int Quarter,
    int TotalPlanned, int TotalConducted, int TotalPercent,
    List<SubjectProgressDto> Subjects);

/// <summary>Fan ichidagi bitta dars (yashil = o'tilgan, qizil = hali yo'q).</summary>
public record SubjectLessonDto(
    string Date, int Period, string? StartTime, string? EndTime,
    string Topic, string? Homework, bool Conducted, bool IsPast);

/// <summary>Fanga kirilganda — darslar ro'yxati va yig'ma sonlar.</summary>
public record SubjectProgressDetailDto(
    string SubjectId, string SubjectName, int Quarter,
    int Planned, int Conducted, int Remaining, int Percent,
    List<SubjectLessonDto> Lessons);

/// <summary>O'qituvchi progresi — bitta (guruh, fan) kesimi.</summary>
public record TeacherSubjectProgressDto(
    string ClassId, string ClassName, string SubjectId, string SubjectName,
    int Planned, int Conducted, int Remaining, int Percent, int ExpectedByToday);

/// <summary>O'qituvchining umumiy o'tilgan darslar progresi + kesimlar bo'yicha yoyilma.</summary>
public record TeacherProgressDto(
    int Quarter,
    int TotalPlanned, int TotalConducted, int TotalPercent,
    List<TeacherSubjectProgressDto> Items);

// ============================ AMAL SABABLARI (action reasons) ============================

/// <summary>Bitta amal sababi.</summary>
public record ActionReasonDto(string Id, string Category, string Label, int Order);

/// <summary>Sabab yaratish so'rovi.</summary>
public record ActionReasonCreate(string Category, string Label);

/// <summary>Sabab yangilash so'rovi.</summary>
public record ActionReasonUpdate(string Label);

// ============================ DARAJA TESTI (placement test) ============================

/// <summary>Admin ro'yxati uchun test qatori.</summary>
public record LevelTestListDto(
    string Id, string Title, string CourseId, string CourseName, string Slug,
    bool IsActive, string CreatedAt, int QuestionCount, int SubmissionCount);

/// <summary>Test elementi (admin) — Kind="question" (to'g'ri javobli) yoki "survey" (so'rovnoma, checkbox).</summary>
public record LevelTestQuestionDto(string Id, string Text, List<string> Options, int CorrectIndex, int Order,
    string Kind = "question", bool Multiple = false);

/// <summary>Daraja diapazoni.</summary>
public record LevelTestBandDto(string Id, string Label, int MinPercent, int Order);

/// <summary>Test to'liq tafsiloti (admin editor uchun).</summary>
public record LevelTestDetailDto(
    string Id, string Title, string CourseId, string CourseName, string Slug, string Intro,
    bool IsActive, string CreatedAt,
    List<LevelTestQuestionDto> Questions, List<LevelTestBandDto> Bands);

/// <summary>Element kiritish/yangilash payload'i (Id bo'sh bo'lsa yangi). Kind="question"|"survey".</summary>
public record LevelTestQuestionInput(string? Id, string Text, List<string> Options, int CorrectIndex,
    string Kind = "question", bool Multiple = false);

/// <summary>Daraja diapazoni payload'i.</summary>
public record LevelTestBandInput(string? Id, string Label, int MinPercent);

/// <summary>Test yaratish/yangilash payload'i.</summary>
public record LevelTestPayload(
    string Title, string CourseId, string Intro, bool IsActive,
    List<LevelTestQuestionInput> Questions, List<LevelTestBandInput> Bands);

/// <summary>So'rovnoma javobi (admin natijalarda) — savol matni + tanlangan variant(lar).</summary>
public record SurveyAnswerDto(string Question, List<string> Answers);

/// <summary>Test topshiruvi (admin — natijalar ro'yxati).</summary>
public record LevelTestSubmissionDto(
    string Id, string FullName, string Phone, int Age, int Score, int Total,
    int Percent, string Level, string CreatedAt, string LeadId, List<SurveyAnswerDto> Survey);

/// <summary>Daraja testi topshiruvchisi: aktiv bo'ldimi + qaysi guruh(lar)ga qo'shilgan va o'qituvchisi (FISH).
/// IsDeleted — lid o'chirilgan yoki o'quvchi o'chirilgan (lead→student→archived).
/// <para><c>StageTitle</c> — lidning kanban bosqichi, <c>Paid</c>/<c>PaidTotal</c> — SOTUV
/// natijasi (lid formalari bilan bir xil, yagona manba: <c>LeadOutcome</c>).</para></summary>
public record LevelTestStatRowDto(
    string SubmissionId, string FullName, string Phone, string Level, int Percent, string CreatedAt,
    string LeadId, string? StudentId, bool Active, string GroupName, string TeacherName, bool IsDeleted = false,
    string StageTitle = "", string StageColor = "", bool Paid = false, decimal PaidTotal = 0,
    string FirstPaidAt = "");
/// <summary>
/// BITTA daraja testining statistikasi: <paramref name="Total"/> — topshiriqlar soni,
/// qolgan sonlar esa TAKRORSIZ LIDLAR bo'yicha (<paramref name="Leads"/> — maxraj).
///
/// <para>⚠️ <paramref name="Active"/> ham takrorsiz lid bo'yicha sanaladi: bir odam testni ikki
/// marta topshirgan bo'lsa "aktiv" ikki marta sanalardi va o'sha test umumiy statistika sahifasida
/// (<see cref="LevelTestOverallStatsDto"/>, u har doim takrorsiz sanaydi) BOSHQACHA raqam
/// ko'rsatardi — foydalanuvchi qaysi biri to'g'ri ekanini bilmasdi.</para>
/// </summary>
public record LevelTestStatsDto(
    int Total, int Active, List<LevelTestStatRowDto> Rows, int Paid = 0, decimal Revenue = 0,
    int Leads = 0);

// ============================ LID FORMALARI (ariza formalari) ============================
// Har kanal (Instagram / Facebook / Telegram / ...) uchun alohida ommaviy forma → o'z manbasi bilan lid.

/// <summary>Forma qatori (admin ro'yxati) — qisqa sanoqlar bilan.</summary>
public record LeadFormListDto(
    string Id, string Title, string Slug, string Source, string CourseName,
    bool IsActive, int Views, int FieldCount, int SubmissionCount, string CreatedAt, string CreatedBy);

/// <summary>Qo'shimcha savol (admin).</summary>
public record LeadFormFieldDto(
    string Id, string Label, string Kind, List<string> Options, string Placeholder, bool Required, int Order);

/// <summary>Ijtimoiy tarmoq havolalari — formada saqlanadi, "rahmat" ekranida ikonka bo'ladi.</summary>
public record LeadFormSocialsDto(
    string Instagram, string Telegram, string Facebook, string Youtube, string Website);

/// <summary>Forma to'liq tafsiloti (admin muharriri uchun).</summary>
public record LeadFormDetailDto(
    string Id, string Title, string Slug, string Source, string CourseName, List<string> CourseOptions,
    string Intro, string SuccessText, string ButtonText,
    bool AskAge, bool AskCourse, bool AskParentPhone, bool IsActive,
    int Views, string CreatedAt, string CreatedBy, List<LeadFormFieldDto> Fields,
    LeadFormSocialsDto Socials);

/// <summary>Qo'shimcha savol payload'i (Id bo'sh — yangi).</summary>
public record LeadFormFieldInput(
    string? Id, string Label, string Kind, List<string>? Options, string? Placeholder, bool Required);

/// <summary>Forma yaratish/yangilash payload'i.</summary>
public record LeadFormPayload(
    string Title, string Source, string? CourseName, List<string>? CourseOptions,
    string? Intro, string? SuccessText, string? ButtonText,
    bool AskAge, bool AskCourse, bool AskParentPhone, bool IsActive,
    List<LeadFormFieldInput>? Fields, LeadFormSocialsDto? Socials);

/// <summary>
/// Formaga tushgan ariza (admin ro'yxati) + lidning HOZIRGI holati.
/// <para><paramref name="StageTitle"/> — kanban bosqichi ("Bog'lanildi", "Sinov darsi"...);
/// <paramref name="Paid"/>/<paramref name="PaidTotal"/> — SOTUV natijasi: odam pul to'ladimi
/// (to'lov − vozvrat); <paramref name="Active"/> — hozir faol guruhda o'qiyaptimi.</para>
/// </summary>
public record LeadFormSubmissionDto(
    string Id, string FormId, string FormTitle, string FullName, string Phone, string ParentPhone,
    int Age, string CourseName, string Ref, string CreatedAt, string LeadId, bool IsNewLead,
    string? StudentId, bool Active, bool LeadDeleted,
    string StageTitle, string StageColor, bool Paid, decimal PaidTotal, string FirstPaidAt,
    List<SurveyAnswerDto> Answers);

/// <summary>
/// Bitta forma bo'yicha voronka qatori: ochildi → ariza → lid → o'quvchi → TO'LADI → faol.
/// <para><paramref name="PayRate"/> — SOTUV konversiyasi: takrorsiz lidlarning necha foizi
/// haqiqatan pul to'ladi (eng halol o'lchov — "o'quvchi bo'ldi" hali pul degani emas).</para>
/// </summary>
public record LeadFormStatRowDto(
    string FormId, string Title, string Source, bool IsActive,
    int Views, int Submissions, int NewLeads, int Converted, int ActiveStudents,
    int Paid, decimal Revenue,
    double SubmitRate, double ConvertRate, double PayRate);

/// <summary>
/// Kunlik oqim (grafik uchun): "yyyy-MM-dd" + o'sha kundagi soni.
/// <para>YAGONA tur: lid formalari statistikasi ham, daraja testi statistikasi ham shundan
/// foydalanadi — ikkala sahifadagi grafik bir xil ma'lumot shaklida bo'lsin.</para>
/// </summary>
public record DayCountDto(string Date, int Count);

/// <summary>Sub-kanal (`?ref=`) kesimi — bir forma havolasi bir necha joyga qo'yilganda.</summary>
public record LeadFormRefDto(string Ref, int Submissions, int Converted, int Paid);

/// <summary>Manba (kanal) kesimi — bir manbaga bir nechta forma bog'langan bo'lishi mumkin.</summary>
public record LeadFormSourceDto(
    string Source, int Forms, int Submissions, int Converted, int ActiveStudents,
    int Paid, decimal Revenue);

/// <summary>
/// Bosqich kesimi — kelgan lidlar HOZIR kanbanning qaysi ustunida turibdi. "Voronka qayerda
/// tiqilib qolgan" degan savolga javob beradi.
/// <para>YAGONA tur: lid formalari ham, daraja testi statistikasi ham shundan foydalanadi.</para>
/// </summary>
public record LeadStageCountDto(string Stage, string Color, int Leads);

/// <summary>
/// Formalar UMUMIY statistikasi (barcha formalar bo'yicha) + <b>BUTUN CRM manzarasi</b>.
///
/// <para>⚠️ Ikki xil "jami" bor va ular ATAYIN ajratilgan:</para>
/// <list type="bullet">
///   <item><c>Submissions</c>/<c>Converted</c>/<c>Paid</c>/<c>Revenue</c> — FAQAT formalardan
///   kelganlar (sahifaning asosiy mavzusi);</item>
///   <item><c>Overview</c> — CRM'dagi BARCHA lidlar (qo'lda kiritilgan, daraja testi, Instagram
///   ham). Busiz sahifadagi sonlar "markazning hamma lidi" deb o'qilib, noto'g'ri xulosaga olib
///   kelardi. Daraja testi statistikasida ham AYNAN shu blok bor.</item>
/// </list>
/// </summary>
public record LeadFormStatsDto(
    int Forms, int ActiveForms, int Views, int Submissions, int NewLeads, int Converted, int ActiveStudents,
    int Paid, decimal Revenue,
    List<LeadFormStatRowDto> ByForm, List<LeadFormSourceDto> BySource,
    List<LeadFormRefDto> ByRef, List<LeadStageCountDto> ByStage, List<DayCountDto> Daily,
    CrmOverviewDto Overview);

// ---- Ommaviy (anonim) ----

/// <summary>Ommaviy formadagi qo'shimcha savol.</summary>
public record PublicLeadFormFieldDto(
    string Id, string Label, string Kind, List<string> Options, string Placeholder, bool Required);

/// <summary>Ommaviy ijtimoiy tarmoq havolasi — ariza yuborilgach ikonka bo'lib chiziladi.
/// <paramref name="Kind"/> ∈ instagram | telegram | facebook | youtube | website.</summary>
public record PublicSocialLinkDto(string Kind, string Url);

/// <summary>Ommaviy forma ko'rinishi (arizani to'ldiruvchi uchun). Manba/statistika KO'RSATILMAYDI.</summary>
public record PublicLeadFormDto(
    string Title, string Intro, string ButtonText, string CourseName,
    bool AskAge, bool AskCourse, bool AskParentPhone,
    List<string> Courses, List<PublicLeadFormFieldDto> Fields,
    List<PublicSocialLinkDto> Socials);

/// <summary>Ariza yuborish so'rovi. Answers — savol id → tanlangan/kiritilgan qiymat(lar).</summary>
public record LeadFormSubmitRequest(
    string FullName, string Phone, string? ParentPhone, int Age, string? Course,
    Dictionary<string, List<string>>? Answers, string? Ref);

/// <summary>Ariza yuborilgandan keyingi javob (rahmat matni).</summary>
public record LeadFormSubmitResultDto(string Message);

/* ---------- Baholash mezonlari (grading criteria) ---------- */
/// <summary>Baholash mezoni (kriteriya). TeacherId/TeacherName bo'sh — umumiy mezon.</summary>
public record GradingCriterionDto(
    string Id, string Name, string Description, int MaxScore, int Order,
    string? TeacherId, string? TeacherName);
/// <summary>Mezon yaratish/yangilash payload'i. TeacherId — mezon egasi (ixtiyoriy).</summary>
public record CriterionInput(string Name, string? Description, int MaxScore, string? TeacherId);
/// <summary>Guruhga biriktirilgan mezonlar ro'yxatini saqlash (to'liq almashtiriladi).</summary>
public record GroupCriteriaInput(List<string> CriterionIds);
/// <summary>Baholash grid'idagi ustun (mezon).</summary>
public record GradingBoardCriterionDto(string Id, string Name, int Order);
/// <summary>Baholash grid'idagi qator (o'quvchi). DoneKeys — "criterionId|date" bo'yicha "bajardi" belgilangan kataklar.</summary>
/// <summary>MemberStart — a'zolik boshlanishi (aktivlashtirilgan bo'lsa ActivatedAt, aks holda JoinedAt,
/// "yyyy-MM-dd"); undan oldingi sanaga mezon belgilab bo'lmaydi (frontend katakni bloklaydi).</summary>
public record GradingBoardStudentDto(string StudentId, string FullName, List<string> DoneKeys, string MemberStart);
/// <summary>Guruh baholash grid'i: oy(lar) + dars sanalari + mezonlar (ustun) + faol o'quvchilar + bajardi belgilar.</summary>
public record GradingBoardDto(
    string GroupId, string GroupName,
    List<string> Months, string Month, List<string> Dates,
    List<GradingBoardCriterionDto> Criteria, List<GradingBoardStudentDto> Students);
/// <summary>Bitta katakni belgilash: shu sanada shu mezon bo'yicha bajardi (Done) yoki yo'q.</summary>
public record SetCriterionGradeRequest(string GroupId, string StudentId, string CriterionId, string Date, bool Done);
/// <summary>Shu sanada bitta mezon bo'yicha BARCHA faol o'quvchini belgilash/belgilamaslik (ommaviy).</summary>
public record BulkCriterionGradeRequest(string GroupId, string CriterionId, string Date, bool Done);
/// <summary>Guruh baholash statistikasi: oylik mezon ballari + nechta ba'ho kiritilgan.</summary>
public record GradingGroupSummaryDto(string GroupId, string GroupName, int ActiveStudents, int TotalGrades, double AverageScore);

/* ---------- Speaking (Azure Pronunciation Assessment) ---------- */
/// <summary>Bitta so'z bo'yicha talaffuz natijasi: aniqlik bali + xato turi (None/Mispronunciation/Omission/Insertion).</summary>
public record SpeakingWordDto(string Word, double Accuracy, string ErrorType);
/// <summary>Speaking baholash natijasi (Azure): tanilgan matn + ballar (0..100) + per-word.</summary>
public record SpeakingResultDto(
    string RecognizedText, double PronScore, double Accuracy, double Fluency,
    double Completeness, double Prosody, List<SpeakingWordDto> Words, string? Error);
/// <summary>Azure Speech sozlamasi holati. Kalit ham, region ham .env dan (AZURE_SPEECH_KEY /
/// AZURE_SPEECH_REGION) — UI faqat holatni ko'rsatadi.</summary>
public record AzureSpeechSettingsDto(
    string Region, bool Configured, EnvSecretDto? Key = null, string RegionEnvKey = "AZURE_SPEECH_REGION");
/// <summary>ESKI so'rov — endi qabul qilinmaydi (kalit/region .env da); bo'sh bo'lmasa xato qaytadi.</summary>
public record SaveAzureSpeechRequest(string? Key, string? Region);

/// <summary>Gemini AI sozlamasi holati (kalit .env dan — GEMINI_API_KEY; model — GEMINI_MODEL).</summary>
public record GeminiSettingsDto(string Model, bool Configured, EnvSecretDto? Key = null);
/// <summary>ESKI so'rov — endi qabul qilinmaydi (kalit .env da); bo'sh bo'lmasa xato qaytadi.</summary>
public record SaveGeminiRequest(string? Key);
/// <summary>AI tahlilidagi sohaviy baholar (0-100) — radar/diagramma uchun.</summary>
public record AiRatingsDto(int Akademik, int Davomat, int Intizom, int UyVazifa, int Faollik, int Umumiy);
/// <summary>O'quvchi AI tahlilining strukturali natijasi (matn bo'limlari + diagramma sonlari).</summary>
public record StudentAiAnalysisResultDto(
    string Umumiy, List<string> Kuchli, List<string> Zaif, string Dinamika,
    string Ozgarishlar, List<string> Tavsiyalar, AiRatingsDto Baholar, string Trend);
/// <summary>Saqlangan bitta AI tahlil yozuvi (tarix elementi).</summary>
public record StudentAiAnalysisRecordDto(
    string Id, string Date, string CreatedAt, string Model, int OverallScore,
    StudentAiAnalysisResultDto Result);
/// <summary>AI tahlil yaratish javobi. AlreadyToday=true bo'lsa bugun allaqachon qilingan
/// (yangi Gemini chaqirig'i bo'lmadi, mavjud yozuv qaytdi).</summary>
public record StudentAiAnalysisResponseDto(
    bool Ok, bool AlreadyToday, StudentAiAnalysisRecordDto? Record, string? Error);

/* ---------- Markaz (butun o'quv markazi) kunlik AI tahlili ---------- */
/// <summary>Diagramma uchun umumiy nuqta (yorliq + qiymat).</summary>
public record CenterPointDto(string Label, double Value);
/// <summary>Markaz moliyaviy prognozi (deterministik hisoblangan — AI emas).</summary>
public record CenterRevenueDto(
    decimal ExpectedThisMonth,   // shu oy kutilayotgan hisob (MonthlyCharge effektiv yig'indisi)
    decimal CollectedThisMonth,  // shu oy yig'ilgan tushum (income)
    decimal OutstandingDebt,     // jami qarzdorlik (manfiy balanslar yig'indisi, musbat)
    decimal YesterdayIncome,     // kechagi tushum
    decimal PredictedMonthEnd);  // oy oxirigacha taxminiy yig'iladigan tushum (chiziqli prognoz)
/// <summary>Markaz ko'rsatkichlari (deterministik hisoblangan raqamlar — diagramma uchun).</summary>
public record CenterMetricsDto(
    int ActiveStudents,
    int NewLeadsThisMonth,
    int NewLeadsYesterday,
    int ConvertedThisMonth,
    int DepartedThisMonth,
    double AvgGradeThisMonth,
    double AvgGradePrevMonth,
    List<CenterPointDto> LeadsBySource,
    List<CenterPointDto> DepartureReasons,
    List<CenterPointDto> IncomeLast14Days);
/// <summary>AI tomonidan yozilgan narrativ (o'zbek tilida) — markaz tahlili matn qismlari.</summary>
public record CenterAiNarrativeDto(
    string Umumiy, string TushumTahlili, string BaholarTahlili,
    string Lidlar, string Ketganlar, List<string> Xavflar,
    List<string> Tavsiyalar, int Salomatlik, string Trend);
/// <summary>Saqlangan bitta markaz AI tahlil yozuvi (to'liq: AI narrativ + raqamlar).</summary>
public record CenterAiRecordDto(
    string Id, string Date, string CreatedAt, string Model, int Health,
    CenterAiNarrativeDto Ai, CenterRevenueDto Revenue, CenterMetricsDto Metrics);
/// <summary>Tarix ro'yxati elementi (qisqa).</summary>
public record CenterAiHistoryItemDto(string Id, string Date, string CreatedAt, int Health, string Summary);
/// <summary>Markaz AI tahlil yaratish javobi. AlreadyToday=true bo'lsa bugun allaqachon qilingan.</summary>
public record CenterAiResponseDto(
    bool Ok, bool AlreadyToday, CenterAiRecordDto? Record, string? Error);

/* ---------- O'QITUVCHI AI tahlili (profil → "AI tahlil" tabi) ---------- */
/// <summary>Bir oydagi o'quvchi OQIMI: kelgan / aktivlashgan / muzlatilgan / ketgan.</summary>
public record TeacherFlowPointDto(string Month, int Came, int Activated, int Frozen, int Left);
/// <summary>Bir oydagi JURNAL intizomi: reja/o'tilgan/o'tkazib yuborilgan + mavzu/uy vazifa/davomat foizi.</summary>
public record TeacherJournalMonthDto(
    string Month, int Planned, int Conducted, int Missed,
    int TopicPct, int HomeworkPct, int AttendanceTakenPct, int Grades, double AvgGrade);
/// <summary>Guruh kesimidagi qisqa ko'rsatkichlar (AI tahlil jadvali uchun).</summary>
public record TeacherGroupStatDto(
    string GroupId, string Name, string CourseName, bool IsArchived,
    int Active, int Trial, int Frozen, int Left,
    int Planned, int Conducted, int Missed, double AvgGrade);
/// <summary>DETERMINISTIK hisoblangan o'qituvchi ko'rsatkichlari (AI emas) — diagramma va jadvallar uchun.</summary>
public record TeacherAiMetricsDto(
    int GroupCount, int ActiveGroupCount,
    int CameTotal, int ActiveStudents, int TrialStudents, int FrozenStudents, int LeftStudents,
    double RetentionPct, double LossPct,
    int PlannedLessons, int ConductedLessons, int MissedLessons, int JournalDonePct,
    int TopicPct, int HomeworkPct, int AttendanceTakenPct, int GradesCount,
    double AvgGradeThisMonth, double AvgGradePrevMonth, double StudentAttendancePct, double AvgBall,
    int TestCount, double TestAvgPct,
    int TeacherPresentDays, int TeacherLateDays, int TeacherAbsentDays,
    int ComplaintCount, int SuggestionCount,
    List<TeacherFlowPointDto> FlowByMonth,
    List<TeacherJournalMonthDto> JournalByMonth,
    List<CenterPointDto> DepartureReasons,
    List<TeacherGroupStatDto> Groups,
    List<string> RecentMissedDates,
    /// <summary>O'quvchilarning shu o'qituvchi haqida yozilgan fikrlari SONI (oxirgi 12 oy).
    /// Matnlarning O'ZI bu yerda YO'Q — ular faqat AI promptiga boradi (maxfiylik).</summary>
    int StudentReviewCount = 0);
/// <summary>O'qituvchi AI baholari (0-100) — radar diagramma uchun.</summary>
public record TeacherAiScoresDto(
    int Jurnal, int Saqlash, int Baholash, int Rivojlanish, int Faollik, int Umumiy);
/// <summary>AI yozgan narrativ (o'zbekcha) — o'qituvchi tahlilining matn qismlari.</summary>
/// <param name="OquvchilarFikri">O'QUVCHILARNING FIKRI bo'yicha xulosa — admin yozib borgan
/// matnli mulohazalardan AI ajratgan takrorlanuvchi naqshlar. Xom matn (va o'quvchi ismi)
/// hech qachon ko'rsatilmaydi, faqat shu umumlashma.</param>
public record TeacherAiNarrativeDto(
    string Umumiy, string OquvchiOqimi, string KetishSabablari, string Jurnal,
    string Rivojlanish, string Ozgarishlar,
    List<string> Kuchli, List<string> Zaif, List<string> Xavflar, List<string> Tavsiyalar,
    TeacherAiScoresDto Baholar, string Trend,
    string OquvchilarFikri = "");
/// <summary>Saqlangan bitta o'qituvchi AI tahlili (AI narrativ + deterministik raqamlar).</summary>
public record TeacherAiRecordDto(
    string Id, string Date, string CreatedAt, string Model, int OverallScore,
    TeacherAiNarrativeDto Ai, TeacherAiMetricsDto Metrics);
/// <summary>O'qituvchi AI tahlil yaratish javobi. AlreadyToday=true — bugun allaqachon qilingan.</summary>
public record TeacherAiResponseDto(
    bool Ok, bool AlreadyToday, TeacherAiRecordDto? Record, string? Error);

/* ---------- GURUH AI tahlili (guruh sahifasi → "AI tahlil" tabi) ---------- */
/// <summary>Bir oydagi a'zolik oqimi: kelgan / aktivlashgan / muzlatilgan / ketgan.</summary>
public record GroupFlowPointDto(string Month, int Came, int Activated, int Frozen, int Left);
/// <summary>Bir oydagi guruh ko'rsatkichlari (jurnal + davomat + baho + moliya).</summary>
public record GroupMonthStatDto(
    string Month, int Planned, int Conducted, int Missed,
    int AttendancePct, int Grades, double AvgGrade, decimal Billed, decimal Collected);
/// <summary>Guruhda o'tkazilgan bitta test/imtihon natijasi.</summary>
public record GroupTestStatDto(
    string Id, string Name, string Date, string Mode, decimal MaxScore,
    int Scored, int StudentCount, double AvgPct);
/// <summary>Guruhdagi bitta o'quvchining kesimi (ball, o'zlashtirish, davomat, qarz).</summary>
public record GroupStudentStatDto(
    string StudentId, string FullName, string Status,
    int Ball, double AvgGrade, int? AttendancePct, int Absent, decimal Debt);
/// <summary>DETERMINISTIK hisoblangan guruh ko'rsatkichlari (AI emas) — diagramma va jadvallar uchun.</summary>
public record GroupAiMetricsDto(
    string GroupName, string CourseName, string TeacherName, string Days, string Time,
    string StartDate, string EndDate, bool IsArchived, int Capacity, decimal MonthlyFee,
    int CameTotal, int ActiveStudents, int TrialStudents, int FrozenStudents, int LeftStudents,
    double RetentionPct, double LossPct, int FillPct,
    int PlannedLessons, int ConductedLessons, int MissedLessons, int JournalDonePct,
    int TopicPct, int HomeworkPct, int AttendanceTakenPct,
    int AttendancePct, int AbsenceCount, int LateCount,
    int GradesCount, double AvgGradeThisMonth, double AvgGradePrevMonth, double AvgBall,
    int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad,
    int TestCount, double TestAvgPct,
    bool FinanceIncluded, decimal Billed, decimal Collected, int CollectionPct,
    decimal Debt, int PaidCount, int UnpaidCount,
    int CurriculumTotal, int CurriculumCovered, int CurriculumRemaining, string CurriculumFinishDate,
    List<GroupFlowPointDto> FlowByMonth,
    List<GroupMonthStatDto> MonthStats,
    List<CenterPointDto> DepartureReasons,
    List<CenterPointDto> AbsenceReasons,
    List<GroupTestStatDto> Tests,
    List<GroupStudentStatDto> Students,
    List<string> RecentMissedDates);
/// <summary>Guruh AI baholari (0-100) — radar diagramma uchun.</summary>
public record GroupAiScoresDto(
    int Davomat, int Barqarorlik, int Ozlashtirish, int Tolov, int Jurnal, int Umumiy);
/// <summary>AI yozgan narrativ (o'zbekcha, TANQIDIY) — guruh tahlilining matn qismlari.</summary>
public record GroupAiNarrativeDto(
    string Umumiy, string Davomat, string Oqim, string Ozlashtirish, string Imtihonlar,
    string Tolovlar, string Jurnal, string Ozgarishlar,
    List<string> Kuchli, List<string> Zaif, List<string> Xavflar, List<string> Tavsiyalar,
    GroupAiScoresDto Baholar, string Trend);
/// <summary>Saqlangan bitta guruh AI tahlili (AI narrativ + deterministik raqamlar).</summary>
public record GroupAiRecordDto(
    string Id, string Date, string CreatedAt, string Model, int OverallScore,
    GroupAiNarrativeDto Ai, GroupAiMetricsDto Metrics);
/// <summary>Guruh AI tahlil yaratish javobi. AlreadyToday=true — bugun allaqachon qilingan.</summary>
public record GroupAiResponseDto(
    bool Ok, bool AlreadyToday, GroupAiRecordDto? Record, string? Error);

/* ---------- VORONKA AI tahlili (lid formalari · daraja testlari) ---------- */

/// <summary>
/// Bitta KANAL kesimi: lid formalarida — forma, daraja testlarida — test.
/// <paramref name="Source"/> faqat formada bo'ladi (Instagram/Telegram...), testda bo'sh.
/// </summary>
public record FunnelAiChannelDto(
    string Name, string Source, int Submissions, int Leads, int Converted,
    int ActiveStudents, int Paid, decimal Revenue, double ConvertRate, double PayRate);

/// <summary>
/// AI tahlilga beriladigan DETERMINISTIK raqamlar — statistika sahifasidagi bilan AYNAN bir xil
/// manbadan (<c>LeadFormService.BuildStatsAsync</c> / <c>LevelTestService.BuildOverallStatsAsync</c>).
///
/// <para>⚠️ Bu yerda FAQAT jamlanma bor: ariza yuborgan odamlarning ismi, telefoni va javoblari
/// AI'ga HECH QACHON yuborilmaydi (tashqi xizmatga shaxsiy ma'lumot chiqmaydi).</para>
/// </summary>
public record FunnelAiMetricsDto(
    /// <summary><c>lead-forms</c> | <c>level-tests</c>.</summary>
    string Kind,
    /// <summary>Formalar (yoki testlar) soni va ulardan nechtasi faol.</summary>
    int Sources, int ActiveSources,
    /// <summary>Formada — ochilishlar; testda — yuborilgan bir martalik havolalar.</summary>
    int Views,
    int Submissions, int Leads, int Converted, int ActiveStudents, int Paid, decimal Revenue,
    /// <summary>Ariza/ochilish (formada) yoki topshiriq/havola (testda); manba bo'lmasa 0.</summary>
    double SubmitRate,
    double ConvertRate, double PayRate,
    List<FunnelAiChannelDto> Channels,
    List<LeadStageCountDto> Stages,
    List<DayCountDto> Daily);

/// <summary>Voronka tahlilining 0..100 baholari (radar/ring uchun).</summary>
public record FunnelAiScoresDto(
    /// <summary>Oqim hajmi — kelayotgan ariza/topshiriq yetarlimi.</summary>
    int Hajm,
    /// <summary>Lid → o'quvchi konversiyasi.</summary>
    int Konversiya,
    /// <summary>Sotuv — o'quvchilarning pul to'lashi.</summary>
    int Sotuv,
    /// <summary>Barqarorlik — oqim uzilib-uzilib emas, muntazam kelyaptimi.</summary>
    int Barqarorlik,
    int Umumiy);

/// <summary>AI yozgan narrativ (o'zbekcha, TANQIDIY) — voronka tahlilining matn qismlari.</summary>
public record FunnelAiNarrativeDto(
    string Umumiy, string Kanallar, string Voronka, string Sifat, string Pul, string Ozgarishlar,
    List<string> Kuchli, List<string> Zaif, List<string> Xavflar, List<string> Tavsiyalar,
    FunnelAiScoresDto Baholar, string Trend);

/// <summary>Saqlangan bitta voronka tahlili (AI narrativ + deterministik raqamlar).</summary>
public record FunnelAiRecordDto(
    string Id, string Kind, string Date, string CreatedAt, string Model, int OverallScore,
    FunnelAiNarrativeDto Ai, FunnelAiMetricsDto Metrics);

/// <summary>Yaratish javobi. AlreadyToday=true — bugun allaqachon yaratilgan (Gemini chaqirilmadi).</summary>
public record FunnelAiResponseDto(
    bool Ok, bool AlreadyToday, FunnelAiRecordDto? Record, string? Error);

/* ---------- O'quvchi baholash statistikasi (oylik + har darslik) ---------- */
/// <summary>Mezon bo'yicha OYLIK xulosa: shu oyda nechta darsda bajargan / jami dars.</summary>
public record StudentGradingCriterionDto(string Id, string Name, int Done, int Total);
/// <summary>Bitta dars (sana) — shu darsda bajarilgan mezon id'lari.</summary>
public record StudentGradingDateDto(string Date, List<string> DoneCriterionIds);
/// <summary>O'quvchining bitta guruhdagi baholash statistikasi (oylik xulosa + har darslik).</summary>
/// <summary>
/// O'quvchining bitta guruhdagi baholash xulosasi. <paramref name="MonthBall"/> — SHU OYDA yig'ilgan ball
/// (bajarilgan mezonlar soni), <paramref name="TotalBall"/> — shu guruhda BARCHA vaqt bo'yicha yig'ilgan ball.
/// </summary>
public record StudentGradingGroupDto(
    string GroupId, string GroupName, List<string> Months, string Month, List<string> Dates,
    List<StudentGradingCriterionDto> Criteria, List<StudentGradingDateDto> Lessons,
    int MonthBall = 0, int TotalBall = 0);

/* ---------- Baholash aggregatsiya (o'quvchi-level totals) ---------- */
/// <summary>Guruh baholash jadvali ichida o'quvchi qatori: nechta mezon biriktirilgan,
/// nechta mezon checklari bajarilgan, o'rtacha hajm (TotalScore/CriteriaCount).</summary>
public record StudentGradingTotalDto(
    string Id,
    string FullName,
    int CriteriaCount,
    int TotalScore,
    double AverageScore);

/* ---------- Baholash xulosa (oylik o'rtacha va jami) ---------- */
/// <summary>Bitta oyda baholash xulosa: o'rtacha ba'ho + jami ba'holi darslar / jami mezonlar.</summary>
public record MonthGradingSummaryDto(
    string Month, double AverageScore, int TotalScore, int CriteriaCount);

// ---- Ommaviy (anonim) ----

/// <summary>Ommaviy test elementi (to'g'ri javobSIZ). Kind="question" (radio) yoki "survey" (checkbox).</summary>
public record PublicTestQuestionDto(string Id, string Text, List<string> Options,
    string Kind = "question", bool Multiple = false);

/// <summary>Ommaviy test ko'rinishi (test ishlovchi uchun).</summary>
public record PublicTestDto(
    string Title, string Intro, string CourseName, List<PublicTestQuestionDto> Questions);

/// <summary>Test topshirish so'rovi: kontakt + savol javoblari (savol id → variant indeksi) +
/// so'rovnoma javoblari (element id → tanlangan variant indekslari ro'yxati).</summary>
public record TestSubmitRequest(
    string FullName, string Phone, int Age, Dictionary<string, int> Answers,
    Dictionary<string, List<int>>? SurveyAnswers = null);

/// <summary>Test natijasi (topshirgandan keyin ko'rsatiladi).</summary>
public record TestResultDto(int Score, int Total, int Percent, string Level, string Message);

/// <summary>Lidga daraja testi havolasini yuborish so'rovi.</summary>
public record SendLeadTestRequest(string TestId);
/// <summary>Lidga test havolasi yuborilgandan keyin javob — holat + havola.</summary>
public record SendLeadTestResultDto(bool Ok, string Status, string Link);

/// <summary>Bir martalik havola (invite) bo'yicha test ko'rinishi: test + lid nomi (oldindan to'ldirilgan).
/// Used=true bo'lsa havola allaqachon ishlatilgan (qayta kirib bo'lmaydi).</summary>
public record PublicInviteDto(PublicTestDto? Test, string FullName, string Phone, bool Used);

/// <summary>Daraja testi invite (lidga yuborilgan bir martalik havola) — admin statistikasi uchun.</summary>
public record LevelTestInviteDto(
    string Id, string TestId, string LeadId, string LeadName, string Phone,
    string SmsStatus, string CreatedAt, bool Used, string UsedAt, int Percent, string Level);

/// <summary>
/// Daraja testlari UMUMIY statistikasi — "Formalar → Test statistikasi" sahifasi (testga KIRMASDAN
/// ko'riladi). Voronka lid formalaridagi bilan BIR XIL o'qiladi: topshirdi → lid → o'quvchi →
/// TO'LADI, foizlar esa TAKRORSIZ lidlar bo'yicha (bir odam testni ikki marta topshirsa ham bitta
/// mijoz). Rows — har bir topshiruvchi (qaysi testga tegishli + natija + bosqich/to'lov/holat).
/// </summary>
public record LevelTestOverallStatsDto(
    int TestCount, int ActiveTests, int Submissions, int Invites, int InvitesUsed, double AvgPercent,
    int Leads, int Converted, int Active, int Paid, decimal Revenue,
    List<LevelCountDto> ByLevel, List<TestStatRowDto> ByTest,
    List<LeadStageCountDto> ByStage, List<DayCountDto> Daily,
    /// <summary>JAMI topshiruvchilar soni. <c>Rows</c> ko'pi bilan
    /// <c>LevelTestService.MaxRows</c> ta (eng yangilari) — sahifa cheklovni ochiq yozadi.</summary>
    int RowsTotal,
    List<LevelTestOverallRowDto> Rows,
    /// <summary>BUTUN CRM manzarasi — lid formalari statistikasidagi bilan AYNAN bir xil blok.</summary>
    CrmOverviewDto? Overview = null);
public record LevelCountDto(string Level, int Count);
/// <summary>Bitta test bo'yicha voronka qatori (lid formalaridagi <see cref="LeadFormStatRowDto"/> ning
/// daraja testi uchun ko'rinishi: "ochildi" o'rniga HAVOLA yuborilgani/ishlangani).</summary>
public record TestStatRowDto(
    string TestId, string Title, bool IsActive,
    int Submissions, int Invites, int InvitesUsed, double AvgPercent,
    int Leads, int Converted, int ActiveStudents, int Paid, decimal Revenue,
    double ConvertRate, double PayRate);
/// <summary>Umumiy statistikadagi bitta topshiruvchi qatori — qaysi testga tegishli (TestTitle) + natija +
/// hozirgi holati (bosqich, to'lov, aktivmi, guruh, o'qituvchi). Bitta test statistikasi bilan bir xil,
/// faqat test nomi qo'shilgan.</summary>
public record LevelTestOverallRowDto(
    string SubmissionId, string TestId, string TestTitle,
    string FullName, string Phone, string Level, int Percent, string CreatedAt,
    string LeadId, string? StudentId, bool Active, string GroupName, string TeacherName, bool IsDeleted,
    string StageTitle, string StageColor, bool Paid, decimal PaidTotal, string FirstPaidAt);

/// <summary>Arxiv yozuvi (o'chirilgan entity surati) — ko'rsatish uchun.</summary>
public record ArchivedRecordDto(
    string Id, string Type, string EntityId, string Title, string Subtitle,
    string? Reason, string DeletedAt, string ActorName);

/* ---------- O'quv dasturi (Modul → Mavzu → Dars → Topshiriq) — Kurs(Subject)dan MUSTAQIL,
   ko'p-ko'pga (SubjectCurriculum orqali) biriktiriladi ---------- */

/// <summary>Sillabus bandi / TOPSHIRIQ (4-bosqich) — daraxt/jadvalda ko'rsatish uchun (tur + meta +
/// tayyorlik + yaratilgan sana). <paramref name="Count"/> — topshiriq ICHIDAGI elementlar soni:
/// mashqda gap/savol/juftlik soni, testda savollar soni, lug'atda so'zlar soni; qolgan turlarda 0.</summary>
public record CurriculumItemDto(
    string Id, string Text, string Note, int Order, string Type, string Meta, bool Ready, string CreatedAt,
    int Count = 0);

/// <summary>Lug'at (vocab) yozuvi: so'z + tarjima.</summary>
public record VocabEntryDto(string Term, string Meaning);
/// <summary>Test savoli: matn + variantlar + to'g'ri javob indeksi.</summary>
public record CourseQuestionDto(string Id, string Text, List<string> Options, int CorrectIndex);
/// <summary>Bitta topshiriqning TO'LIQ kontenti (tahrirlovchi + ko'rish ekrani uchun).
/// <paramref name="ExerciseKind"/>/<paramref name="ExerciseJson"/> — interaktiv mashq ("exercise"
/// turi): konstruktorda tanlangan tur va uning JSON mazmuni.</summary>
public record CourseItemDetailDto(
    string Id, string LessonId, string Text, string Note, int Order,
    string Type, string VideoUrl, string AudioUrl, string TextContent,
    string PdfUrl, string PdfName, string Meta,
    List<VocabEntryDto> Vocab, List<CourseQuestionDto> Questions,
    string ExerciseKind = "", string ExerciseJson = "");
/// <summary>Topshiriq kontentini saqlash payload'i (nom + kontent + lug'at + test savollari +
/// interaktiv mashq). Type YO'Q — u topshiriq yaratilganda tanlanadi, bu yerda o'zgartirilmaydi.</summary>
public record SaveItemContentRequest(
    string Text, string? VideoUrl, string? AudioUrl, string? TextContent,
    string? PdfUrl, string? PdfName, string? Meta,
    List<VocabEntryDto>? Vocab, List<CourseQuestionDto>? Questions,
    string? ExerciseKind = null, string? ExerciseJson = null);

/// <summary>Sillabus darsi (3-bosqich) + uning topshiriqlari (har biri o'z turida).</summary>
public record CurriculumLessonDto(
    string Id, string Title, string Note, int Order, List<CurriculumItemDto> Items);

/// <summary>Sillabus mavzusi (2-bosqich) + uning darslari.</summary>
public record CurriculumTopicDto(string Id, string Title, string Note, int Order, List<CurriculumLessonDto> Lessons);

/// <summary>Sillabus moduli (1-bosqich) + uning mavzulari.</summary>
public record CurriculumModuleDto(string Id, string Name, string Note, int Order, List<CurriculumTopicDto> Topics);

/// <summary>Bitta o'quv dasturining to'liq sillabusi (Modul → Mavzu → Dars → Topshiriq).</summary>
public record CurriculumDto(string Id, string Name, List<CurriculumModuleDto> Modules);

// ---- Topshiriq URINISHLARI (o'quvchi ishlagan natijalar) ----

/// <summary>Bitta savol/element bo'yicha o'quvchi javobi — <c>CourseItemAttempt.AnswersJson</c>
/// ichida massiv bo'lib saqlanadi. <paramref name="Sec"/> — shu elementga sarflangan soniya.</summary>
public record AttemptAnswerDto(int Index, string Prompt, string Answer, string Expected, bool Ok, int Sec);

/// <summary>O'quvchi urinishini saqlash payload'i (o'quvchi portali → server).
/// <paramref name="Section"/>: exercise | test | view.</summary>
public record SaveAttemptRequest(
    string ItemId, string Section, string? ExerciseKind,
    int Correct, int Total, int DurationSec, List<AttemptAnswerDto>? Answers);

/// <summary>Urinish saqlangandan keyingi javob — nechanchi urinish bo'lgani.</summary>
public record SaveAttemptResponse(int AttemptNo);

/// <summary>O'quvchining O'Z urinishi (ilovadagi "oldingi natijalarim" ro'yxati).</summary>
public record MyAttemptDto(
    string Id, string Section, string ExerciseKind, int AttemptNo,
    int Correct, int Total, int ScorePct, int DurationSec, DateTime FinishedAt);

/// <summary>Admin: o'quvchi profilidagi urinish qatori — sillabusdagi joyi (dastur → modul →
/// mavzu → dars → topshiriq) va guruh nomi bilan boyitilgan.</summary>
public record StudentAttemptDto(
    string Id, string ItemId, string ItemText, string ItemType,
    string LessonTitle, string TopicTitle, string ModuleName, string CurriculumName, string GroupName,
    string Section, string ExerciseKind, int AttemptNo,
    int Correct, int Total, int ScorePct, int DurationSec, DateTime FinishedAt, int AnswerCount);

/// <summary>Admin: bitta urinishning to'liq javoblari (modal ichida ochiladi).</summary>
public record StudentAttemptDetailDto(StudentAttemptDto Attempt, List<AttemptAnswerDto> Answers);

/// <summary>Admin: o'quvchining topshiriq natijalari — jami ko'rsatkichlar + urinishlar ro'yxati.
/// <paramref name="ItemCount"/> — nechta HAR XIL topshiriq ishlangani (urinishlar emas).</summary>
public record StudentAttemptsDto(
    int ItemCount, int AttemptCount, int GradedCount, int AvgScorePct, int TotalMinutes,
    List<StudentAttemptDto> Attempts);

/// <summary>O'quv dasturlari ro'yxati uchun qisqacha kartochka (top-level "O'quv dasturi" sahifasi).</summary>
public record CurriculumSummaryDto(
    string Id, string Name, string Note, int Order, string CreatedAt,
    int ModuleCount, int TopicCount, int ItemCount, int ReadyItemCount, int SubjectCount);

/// <summary>O'quv dasturi yaratish/yangilash payload'i.</summary>
public record CurriculumInput(string Name, string? Note);

/// <summary>Bitta kursga biriktirilgan o'quv dasturi (tartib bilan — guruh ko'rinishida shu tartibda
/// ketma-ket birlashtiriladi).</summary>
public record SubjectCurriculumDto(string CurriculumId, string Name, int Order);

// ---- Guruh sillabus o'tilishi + tugash prognozi ----
/// <summary>Guruh sillabus bandi: o'tilgan (Covered) bayrog'i + o'tilgan sana (CoveredDate) bilan.</summary>
public record GroupCurriculumItemDto(string Id, string Text, string Note, int Order, bool Covered, string CoveredDate);
/// <summary>O'quvchining bir guruhda o'tilgan sillabus bandi (yoki takrorlash darsi) — vaqt jadvali yozuvi.</summary>
public record CoverageLogEntryDto(string Date, string CourseName, string GroupName, string ModuleName, string TopicTitle, string ItemText, bool IsRevision);
/// <summary>Guruh sillabus mavzusi.</summary>
public record GroupCurriculumTopicDto(string Id, string Title, string Note, int Order, List<GroupCurriculumItemDto> Items);
/// <summary>Guruh sillabus moduli.</summary>
public record GroupCurriculumModuleDto(string Id, string Name, string Note, int Order, List<GroupCurriculumTopicDto> Topics);
/// <summary>Guruhning sillabus o'tilishi + tugash prognozi.</summary>
public record GroupCurriculumDto(
    string GroupId, string CourseId, string CourseName,
    int TotalItems, int CoveredCount, int RevisionLessons, int TotalLessons,
    int RemainingItems, int EstLessonsLeft, int LessonsPerWeek, string EstFinishDate,
    List<GroupCurriculumModuleDto> Modules);
/// <summary>Bandni o'tilgan/o'tilmagan deb belgilash payload'i.</summary>
public record CoverRequest(string ItemId, bool Covered);
/// <summary>Takrorlash darsi qo'shish/olib tashlash payload'i (Delta &gt; 0 qo'shadi, &lt; 0 olib tashlaydi).</summary>
public record RevisionRequest(int Delta);

/// <summary>Modul yaratish/yangilash payload'i.</summary>
public record ModuleInput(string Name, string? Note);
/// <summary>Mavzu yaratish/yangilash payload'i.</summary>
public record TopicInput(string Title, string? Note);
/// <summary>Dars yaratish/yangilash payload'i (nom/izoh) — tur yo'q, u topshiriq darajasida tanlanadi.</summary>
public record LessonInput(string Title, string? Note);
/// <summary>Topshiriq (CourseItem) yaratish payload'i. <paramref name="Type"/> MAJBURIY (text|video|
/// audio|vocab|test|pdf|exercise) — shu yerda tanlanadi. Yangilashda (UpdateItem) faqat nom/izoh
/// o'zgaradi — Type qayta yuborilmaydi. <paramref name="ExerciseKind"/> — "exercise" turida
/// topshiriq yaratish ekranida tanlangan MASHQ TURI (masalan "sentence-order"), shu zahoti
/// yoziladi (konstruktor darhol o'sha tur tahrirlovchisini ochadi).</summary>
public record ItemInput(string Text, string? Note, string? Type = null, string? ExerciseKind = null);
/// <summary>Bir nechta topshiriqni bir zumda yaratish payload'i — barchasi BITTA turda
/// (<paramref name="Type"/> MAJBURIY), har bir nom alohida band bo'ladi.</summary>
public record BulkItemInput(List<string>? Texts, string? Type);

// ============================ SERTIFIKAT ============================

/// <summary>O'quvchi sertifikati (portal ko'rinishi).</summary>
public record StudentCertificateDto(
    string Id,
    string CourseName,
    string IssuedAt,
    string ExpiresAt,
    string Status,
    string FileName,
    string DownloadUrl,
    int DownloadCount,
    string Metadata);

/// <summary>Admin: o'quvchining tugatgan kursi + sertifikati (StudentDetailPage uchun).</summary>
public record StudentCompletedCourseDto(
    string CertificateId,
    string CourseId,
    string CourseName,
    string IssuedAt,
    string ExpiresAt,
    string Status,
    string FileName,
    string DownloadUrl,
    int DownloadCount,
    string GroupName);

/// <summary>Sertifikat andozasi yaratish so'rovi (admin: kurs + HTML shablon).</summary>
public record CreateCertificateTemplateRequest(
    string Name,
    string CourseId,
    string HtmlTemplate,
    int ValidityDays = 0);

/// <summary>Sertifikat andozasi (admin ko'rinishi).</summary>
public record CertificateTemplateDto(
    string Id,
    string Name,
    string CourseId,
    string CourseName,
    int ValidityDays,
    string CreatedAt);

/// <summary>Sertifikat andozasini yangilash so'rovi (admin: name, courseId, HTML shablon, muddati).</summary>
public record UpdateCertificateTemplateRequest(
    string Name,
    string CourseId,
    string HtmlTemplate,
    int ValidityDays = 0);

/// <summary>Ommaviy sertifikat tekshirish natijasi.</summary>
public record CertificateVerificationDto(
    bool IsValid,
    string StudentName,
    string CourseName,
    string IssuedAt,
    string ExpiresAt,
    string Status,
    bool HashMatched,
    string Metadata,
    string ErrorMessage);

/// <summary>
/// Guruhni yakunlash va YANGI guruh ochish so'rovi (Hybrid).
/// Eski guruh arxivlanadi, o'quvchilarga sertifikat beriladi, yangi kurs/guruh ochiladi.
/// </summary>
public record CompleteAndTransferRequest(
    /// <summary>Yangi guruhga avtomatik qo'shish. Qo'shiladiganlar — FAQAT eski guruhda
    /// Status=="active" bo'lgan a'zolar; sinovdagi (trial) va muzlatilgan (frozen) a'zolar
    /// ko'chirilmaydi (ular eski guruhda "completed" bo'lib qoladi).</summary>
    bool AutoEnrollNewGroup = true,
    string? NewGroupName = null,
    string? CompletionNotes = null,
    /// <summary>Yangi guruh qaysi kurs bilan ochiladi. Bo'sh bo'lsa — eski guruh kursi qayta ishlatiladi.</summary>
    string? TargetCourseId = null,
    /// <summary>ESKI guruh YOPILADIGAN sana (ISO "YYYY-MM-DD"). A'zoliklar AYNAN shu sanadan
    /// muzlatiladi: shu sanagacha qatnashgan darslar uchun eski guruhga qisman oylik yoziladi,
    /// keyingi oylar hisobi bekor qilinadi. Bo'sh — bugun.</summary>
    string? CloseDate = null,
    /// <summary>YANGI guruhda AKTIVLASHTIRISH sanasi (ISO). Bo'sh — <paramref name="CloseDate"/>.
    /// <paramref name="ActivateInNewGroup"/>=false bo'lsa ishlatilmaydi.</summary>
    string? ActivateDate = null,
    /// <summary>Ko'chirilgan (aktiv) a'zolarni yangi guruhda DARHOL aktivlashtirish — qisman oylik shu
    /// sanadan yangi guruhga yoziladi. false bo'lsa ular yangi guruhga "sinov" statusida qo'shiladi
    /// (to'lov hisoblanmaydi). Kimlar ko'chishiga TA'SIR QILMAYDI — buni <paramref
    /// name="AutoEnrollNewGroup"/> hal qiladi.</summary>
    bool ActivateInNewGroup = true);

/// <summary>Complete-and-Transfer (Hybrid) natijasi.</summary>
public record CompleteAndTransferResultDto(
    bool Ok,
    string ArchivedGroupId,
    string NewGroupId,
    int CertificatesGenerated,
    int EnrolledInNew,
    string? TargetCourseName = null,
    /// <summary>Eski guruh yopilgan (a'zoliklar muzlatilgan) sana.</summary>
    string CloseDate = "",
    /// <summary>Yangi guruhda aktivlashtirish sanasi ("" — aktivlashtirilmagan, sinovda qoldi).</summary>
    string ActivateDate = "",
    /// <summary>ESKI guruhga qisman oylik hisobi yozilgan a'zolar soni.</summary>
    int ChargedOldGroup = 0,
    /// <summary>Yopish oyidan KEYINGI oylar uchun bekor qilingan (balansga qaytarilgan) hisob summasi.</summary>
    decimal RestoredCharges = 0,
    /// <summary>Yangi guruhda darhol aktivlashtirilgan a'zolar soni.</summary>
    int ActivatedInNew = 0,
    /// <summary>Eski guruhda ortib qolgan (avans) va yangi guruhga ko'chirilgan to'lov summasi.</summary>
    decimal MovedAdvance = 0,
    /// <summary>Yangi guruhga KO'CHIRILMAGAN a'zolar soni — eski guruhda aktiv emas edi
    /// (sinovdagi yoki muzlatilgan). Ular eski guruhda "completed" bo'lib qoladi.</summary>
    int SkippedNotActive = 0);

/// <summary>
/// Guruhni YOPISH so'rovi: barcha a'zolar <paramref name="Date"/> sanasidan MUZLATILADI
/// (qarzdorlik shu sanagacha hisoblanadi) va guruh arxivga (NotActive) olinadi.
/// Sertifikat berilmaydi va yangi guruh ochilmaydi — bu "Tugatish (sertifikat bilan)"dan farqi.
/// </summary>
public record CloseGroupRequest(string? Date = null, string? ReasonId = null);

/// <summary>Guruhni yopish natijasi.</summary>
public record CloseGroupResultDto(
    bool Ok,
    string GroupId,
    string GroupName,
    /// <summary>Muzlatish sanasi (ISO) — qarzdorlik shu sanagacha hisoblangan.</summary>
    string FreezeDate,
    /// <summary>Muzlatilgan (aktiv) a'zolar soni.</summary>
    int FrozenCount,
    /// <summary>Allaqachon muzlatilgan bo'lgani uchun tegilmagan a'zolar soni.</summary>
    int AlreadyFrozen,
    /// <summary>Sinovdagi (hisob ochilmagan) a'zolar soni — ular guruhdan chiqarildi.</summary>
    int TrialClosed,
    /// <summary>Muzlatish sanasidan KEYINGI oylar uchun bekor qilingan (balansga qaytarilgan) hisob summasi.</summary>
    decimal RestoredCharges);

/// <summary>Admin tomonidan qo'lda sertifikat yaratish so'rovi.</summary>
public record GenerateCertificateRequest(string CourseId, string? Notes = null);

/// <summary>O'quvchining bir sillabus bandi bo'yicha holatini o'rnatish so'rovi.</summary>
public record SetProgressRequest(string StudentId, string ItemId, bool Done);

/* ---------- Teacher performance ---------- */
/// <summary>
/// Bitta o'qituvchining talaba saqlab qolish statistikasi (barcha guruhlari bo'yicha lifetime).
/// Per-group hisob: bir talaba 2 guruhda bo'lsa = 2 slot.
/// </summary>
public record TeacherPerformanceDto(
    string TeacherId,
    string TeacherName,
    string Phone,
    /// <summary>Umumiy slot soni (StudentGroup yozuvlari).</summary>
    int TotalStudents,
    /// <summary>Hozirda faol (Status=="active") slotlar.</summary>
    int ActiveStudents,
    /// <summary>Muzlatilgan (Status=="frozen") slotlar.</summary>
    int FrozenStudents,
    /// <summary>Guruhdan chiqib ketgan (IsActive==false) slotlar.</summary>
    int LeftStudents,
    /// <summary>Active / Total * 100 (0-100).</summary>
    double RetentionPercent,
    /// <summary>(Frozen + Left) / Total * 100 (0-100).</summary>
    double LossPercent,
    /// <summary>0-100 ball: round(RetentionPercent).</summary>
    int EffectivenessScore,
    /// <summary>O'qituvchi biriktirilgan guruhlar soni (arxivlanmagan).</summary>
    int GroupCount
);

/* ---------- Kassa (to'lov qabul qilish oynasi) ---------- */

/// <summary>
/// Kassa qidiruvida chiqadigan bitta o'quvchi qatori — F.I.Sh bo'yicha topilgan o'quvchini kassir
/// ADASHMASDAN tanishi uchun kerakli minimum: telefonlar, guruhlari va joriy balansi (manfiy = qarz).
/// To'liq profil YUBORILMAYDI (kassirga kerak emas) — to'lov oynasi ochilganda alohida olinadi.
/// </summary>
public record KassaStudentDto(
    string Id,
    string FullName,
    /// <summary>O'quvchining o'z telefoni (bo'sh bo'lishi mumkin).</summary>
    string Phone,
    /// <summary>Ota-ona telefoni (kassada ko'pincha shu bo'yicha topiladi).</summary>
    string ParentPhone,
    /// <summary>Faol a'zoliklaridagi guruh nomlari (sinov ham kiradi — kassir kontekstni ko'rsin).</summary>
    List<string> Groups,
    /// <summary>UMUMIY balans (so'm): manfiy = qarzdor, musbat = avans.</summary>
    decimal Balance,
    /// <summary>Arxivlangan o'quvchi (to'lov qabul qilinaveradi, lekin ro'yxatda belgilanadi).</summary>
    bool IsArchived
);

/// <summary>
/// Kassir kesimidagi jami (davr bo'yicha): u qancha to'lov qabul qilgan. Moliya → "Kassirlar"
/// jadvali qatori va kassaning o'zidagi "Mening to'lovlarim" sarlavhasi uchun.
/// </summary>
public record CashierSummaryDto(
    /// <summary>Guruhlash kaliti: akkaunt id'si yoki eski yozuvlar uchun "name:F.I.Sh".</summary>
    string Key,
    /// <summary>Akkaunt id'si (eski yozuvlarda null — faqat ism bor).</summary>
    string? CashierId,
    string CashierName,
    int Count,
    decimal Total,
    decimal Cash,
    decimal Card,
    decimal Bank,
    /// <summary>Usuli ko'rsatilmagan yoki boshqa usuldagi to'lovlar yig'indisi.</summary>
    decimal Other,
    /// <summary>Oxirgi to'lov kiritilgan vaqt (ISO) — kassir faolligini ko'rish uchun.</summary>
    string? LastAt
);

/// <summary>Kassir kiritgan bitta to'lov (o'z ro'yxati va superadmin drill-down'i uchun).</summary>
public record CashierPaymentDto(
    string Id,
    string Date,
    decimal Amount,
    string? Method,
    string StudentName,
    string GroupName,
    string CourseName,
    string TeacherName,
    string? Month,
    string? ReceiptNo,
    string? CardLast4,
    string? PaidTime,
    string CreatedAt
);

/// <summary>Bitta kassirning davr bo'yicha to'lovlari + jami.</summary>
public record CashierPaymentsDto(CashierSummaryDto Summary, List<CashierPaymentDto> Payments);

/* ---------- Kitoblar sotuvi (O'quv bo'limi → Kitoblar sotuvi) ---------- */

/// <summary>Kitob yaratish/tahrirlash payload'i. <c>InitialStock</c> FAQAT yaratishda ishlatiladi
/// (boshlang'ich qoldiq → "initial" kirim yozuvi); tahrirlashda qoldiq alohida "kirim" amali orqali
/// o'zgartiriladi, aks holda ombor tarixi buzilardi.</summary>
public record BookPayload(
    string Title,
    decimal Price,
    string? Author = null,
    string? Description = null,
    string? CoverUrl = null,
    bool IsActive = true,
    int InitialStock = 0);

/// <summary>Omborga kirim (yoki qo'lda korreksiya): <c>Qty</c> musbat = kirim, manfiy = ayirish.</summary>
public record BookStockPayload(int Qty, string? Note = null);

/// <summary>Kitob — ro'yxat/karta ko'rinishi (qoldiq + sotuv statistikasi bilan).</summary>
public record BookDto(
    string Id,
    string Title,
    string Author,
    string Description,
    string CoverUrl,
    decimal Price,
    int Stock,
    bool IsActive,
    // Tasdiqlangan buyurtmalarda sotilgan umumiy dona.
    int SoldQty,
    // Tasdiqlangan sotuvlardan tushum (so'm).
    decimal SoldTotal,
    // Shu kitob bo'yicha KUTILAYOTGAN buyurtmalardagi dona (rezerv emas — faqat ogohlantirish).
    int PendingQty,
    string CreatedAt,
    string CreatedBy);

/// <summary>Ombor harakati (kirim/sotuv/korreksiya) — "kitoblar qo'shilish tarixi" hisoboti.</summary>
public record BookStockMoveDto(
    string Id,
    string BookId,
    string BookTitle,
    int Qty,
    string Reason,
    string? OrderId,
    string Note,
    int StockAfter,
    string CreatedAt,
    string CreatedBy);

/// <summary>Botdan tushgan kitob buyurtmasi (admin ro'yxati uchun).</summary>
public record BookOrderDto(
    string Id,
    int Number,
    string CustomerName,
    string Phone,
    string? StudentId,
    string? StudentName,
    string BookId,
    string BookTitle,
    decimal UnitPrice,
    int Qty,
    decimal Total,
    string PaymentMethod,
    string ReceiptUrl,
    string Status,
    string RejectReason,
    string CreatedAt,
    string? DecidedAt,
    string DecidedBy,
    // Kitobning JORIY qoldig'i — admin tasdiqlashdan oldin yetarlimi ko'rish uchun.
    int BookStock,
    // Manba: "bot" (mijoz o'zi buyurtma bergan) | "manual" (markazda qo'lda sotilgan).
    string Source = "bot",
    // Karta to'lovida: karta oxirgi 4 raqami va to'lov qilingan vaqt ("HH:mm").
    string? CardLast4 = null,
    string? PaidTime = null,
    // ---- NASIYA (PaymentMethod = "credit") ----
    // Pul olinganmi (naqd/kartada tasdiqlangani = to'langani; nasiyada — PaidAt to'lganida).
    bool IsPaid = true,
    // Va'da qilingan to'lov sanasi ("yyyy-MM-dd") va u o'tib ketganmi.
    string? DueDate = null,
    bool IsOverdue = false,
    // Pul qachon va kim tomonidan olindi; nasiya qanday yopildi ("cash" | "card").
    string? PaidAt = null,
    string PaidBy = "",
    string? SettledMethod = null,
    // ---- QAYTARISH (vozvrat) ----
    // Shu sotuvdan jami qaytarilgan dona (0 = qaytarilmagan) va qaytarish izlari.
    int ReturnedQty = 0,
    string? ReturnedAt = null,
    string ReturnedBy = "",
    string ReturnReason = "",
    // Mijozga haqiqatan qaytarilgan pul (to'lanmagan nasiyada 0 — u yerda qarz kamaygan).
    decimal RefundedAmount = 0m,
    // SOF summa = Total − qaytarilganlarning qiymati. Hisobotlarda AYNAN shu ishlatiladi.
    decimal NetTotal = 0m);

/// <summary>Buyurtmani rad etish sababi.</summary>
public record BookRejectPayload(string Reason);

/// <summary>
/// SOTILGAN KITOBNI QAYTARISH (vozvrat): <paramref name="Qty"/> dona omborga qaytadi va sotuv
/// summasidan o'sha qismi ayiriladi. Qisman qaytarish mumkin (sotilganidan ko'p emas).
/// </summary>
public record BookReturnPayload(int Qty, string? Reason = null);

/// <summary>
/// MARKAZDA QO'LDA SOTUV ("Buyurtmalar → Kitob sotish"): admin kitobni o'quvchiga joyida sotadi.
/// Botdagi oqimdan farqi — chek rasmi YO'Q (pul kassirning oldida to'lanadi), buning o'rniga
/// karta to'lovida <paramref name="CardLast4"/> va <paramref name="PaidTime"/> qo'lda kiritiladi.
/// Buyurtma darhol TASDIQLANGAN holatda yaratiladi (qoldiq shu zahoti ayiriladi).
/// </summary>
public record BookManualSalePayload(
    string BookId,
    /// <summary>Markazdagi o'quvchi — <b>IXTIYORIY</b>. Bo'sh bo'lsa xaridor markazda o'qimaydi
    /// (chetdan kelgan odam). Bot oqimida ham mehmon kitob sotib ola oladi.</summary>
    string? StudentId,
    int Qty,
    string PaymentMethod,
    string? CardLast4 = null,
    string? PaidTime = null,
    /// <summary>O'quvchi tanlanmaganda xaridor ismi — bu ham IXTIYORIY. Bo'sh qolsa ro'yxatda
    /// "Noma'lum" bo'lib ko'rinadi (bot buyurtmalarida allaqachon shunday). NASIYADA esa
    /// o'quvchi yoki shu ism SHART (qarz kimdaligi yozilmasa nasiya ma'nosini yo'qotadi).</summary>
    string? CustomerName = null,
    /// <summary>O'quvchi tanlanmaganda xaridor telefoni (ixtiyoriy) — nasiyada qarzdorni topish
    /// uchun asqotadi. O'quvchi tanlansa raqam undan olinadi va bu maydon inobatga olinmaydi.</summary>
    string? CustomerPhone = null,
    /// <summary>NASIYA: pulni qaytarish uchun va'da qilingan sana ("yyyy-MM-dd", ixtiyoriy).</summary>
    string? DueDate = null);

/// <summary>
/// NASIYA TO'LOVINI QABUL QILISH ("pulini oldim → Tasdiqlash"). Ombor TEGILMAYDI — kitob sotuv
/// paytida berilgan; bu yerda faqat pul to'landi deb belgilanadi va summa tushumga qo'shiladi.
/// </summary>
public record BookCreditPayPayload(
    /// <summary>Pul qanday olindi: "cash" | "card".</summary>
    string Method,
    /// <summary>Karta bo'lsa — oxirgi 4 raqam (to'liq raqam saqlanmaydi).</summary>
    string? CardLast4 = null);

/// <summary>Qo'lda sotuvda o'quvchi qidirish natijasi (yengil — balans/hisob hisoblanmaydi).</summary>
public record BookStudentDto(
    string Id,
    string FullName,
    string Phone,
    string ParentPhone,
    string ClassName,
    bool IsArchived);

/// <summary>
/// KARTA TO'LOVLARI — kartaga o'tkazma bilan to'langan kitob buyurtmalari (chek rasmi bilan).
/// <paramref name="CardNumber"/>/<paramref name="CardHolder"/> — bo'lim bog'langan karta
/// (<c>CenterMeta.BookCardNumber/BookCardHolder</c>), ya'ni pul shu kartaga tushadi.
/// Jami summalar TO'LIQ topilma bo'yicha hisoblanadi (<paramref name="Orders"/> ro'yxati
/// ko'rsatish uchun cheklangan bo'lishi mumkin).
/// </summary>
public record BookCardPaymentsDto(
    string CardNumber,
    string CardHolder,
    // Tasdiqlangan — kartaga haqiqatda tushgan deb hisoblangan pul.
    int CountApproved, decimal TotalApproved,
    // Kutilmoqda — chek kelgan, lekin admin hali tasdiqlamagan.
    int CountPending, decimal TotalPending,
    int CountRejected,
    List<BookOrderDto> Orders);

/// <summary>Kunlik sotuv nuqtasi (grafik uchun). <c>Cash+Card+Credit = Total</c> — taqsimot
/// SOTUV paytidagi to'lov turi bo'yicha (nasiya keyin to'lansa ham o'sha kunda nasiya bo'lib
/// qoladi, ya'ni o'tgan kunlarning grafigi orqaga qarab o'zgarmaydi).
/// <para>⚠️ Barcha sonlar SOF — qaytarilgan kitoblar AYIRILGAN (qaytarish o'zi sotilgan KUNGA
/// yoziladi, aks holda "shu kuni qancha sotildi" savoliga noto'g'ri javob chiqardi).
/// <paramref name="ReturnedQty"/>/<paramref name="ReturnedTotal"/> — shu kunning sotuvlaridan
/// keyinchalik qaytarilgani (izoh uchun).</para></summary>
public record BookDaySalesDto(
    string Date, int Qty, decimal Cash, decimal Card, decimal Credit, decimal Total,
    int ReturnedQty = 0, decimal ReturnedTotal = 0m);

/// <summary>Kitob kesimidagi sotuv (top kitoblar jadvali).</summary>
public record BookSalesByBookDto(string BookId, string BookTitle, int Qty, decimal Total, int Stock);

/// <summary>HAR KUNI qaysi kitob nechta sotilgani ("kunlik sotuv tarixi" jadvali).
/// <paramref name="Qty"/>/<paramref name="Total"/> — SOF (qaytarilgani ayirilgan).</summary>
public record BookDayBookSalesDto(
    string Date, string BookId, string BookTitle, int Qty, decimal Total, int Orders,
    int ReturnedQty = 0);

/// <summary>
/// Bitta SOTUV yozuvi — "qaysi kitob QACHON (soati bilan) va kimga sotildi" lentasi.
/// <para>⚠️ <paramref name="Qty"/>/<paramref name="Total"/> — sotuv PAYTIDAGI (xom) qiymat:
/// lenta hodisalar tarixi, kunlik jamlanma esa sof. Farqni <paramref name="ReturnedQty"/>
/// tushuntiradi — qator ustida "N dona qaytarildi" belgisi chiqadi.</para>
/// </summary>
public record BookSaleRowDto(
    string Id,
    int Number,
    // Sotuv payti ("yyyy-MM-ddTHH:mm:ss") — tasdiqlangan vaqt, bo'lmasa yaratilgan vaqt.
    string SoldAt,
    string BookId,
    string BookTitle,
    int Qty,
    decimal Total,
    string CustomerName,
    string PaymentMethod,
    bool IsPaid,
    string Source,
    // Shu sotuvdan keyinchalik qaytarilgan dona (0 = qaytarilmagan).
    int ReturnedQty = 0);

/// <summary>Kitoblar sotuvi analitikasi — tanlangan davr bo'yicha.</summary>
public record BookAnalyticsDto(
    string From,
    string To,
    // Tasdiqlangan buyurtmalar soni.
    int OrdersApproved,
    int OrdersPending,
    int OrdersRejected,
    // Sotilgan umumiy dona (tasdiqlangan).
    int SoldQty,
    // Davr ichidagi SOTUV summasi, to'lov turi bo'yicha taqsimlangan (jami = uchalasining yig'indisi).
    decimal RevenueCash,
    decimal RevenueCard,
    decimal RevenueTotal,
    // Ombordagi umumiy qoldiq (barcha kitoblar, davrga bog'liq emas).
    int StockTotal,
    // Davr ichida omborga kirim qilingan dona.
    int StockInQty,
    List<BookDaySalesDto> ByDay,
    List<BookSalesByBookDto> ByBook,
    // Qoldig'i tugagan/kam qolgan kitoblar (qoldiq ≤ 3).
    List<BookSalesByBookDto> LowStock,
    // ---- HAR KUNI SOTILGAN KITOBLAR ----
    // Kun × kitob kesimi (TO'LIQ — chegarasiz) va sotuvlar lentasi (soati bilan, cheklangan).
    List<BookDayBookSalesDto> ByDayBook,
    List<BookSaleRowDto> Sales,
    // Lentaga hamma sotuv sig'madimi (rost bo'lsa UI "eng oxirgi N tasi" deb ogohlantiradi).
    bool SalesTruncated,
    // ---- NASIYA: DAVR ICHIDA SOTILGANI (sotuv sanasi bo'yicha) ----
    decimal CreditSold,
    int CreditSoldCount,
    // Shu davrda nasiyaga sotilganlarning ALLAQACHON to'langan qismi.
    decimal CreditSoldPaid,
    // ---- NASIYA: JORIY QARZ (davrga BOG'LIQ EMAS — xuddi ombor qoldig'i kabi) ----
    decimal CreditOutstanding,
    int CreditOutstandingCount,
    decimal CreditOverdue,
    int CreditOverdueCount,
    // ---- NASIYA: davr ichida YIG'ILGAN pul (to'lov sanasi bo'yicha) ----
    decimal CreditCollected,
    int CreditCollectedCount,
    // ---- QAYTARISH (vozvrat) ----
    // Davr SOTUVLARIDAN qaytarilgani (SOTUV sanasi bo'yicha) — yuqoridagi sof raqamlar shu
    // qadar kamaygan, ya'ni "xom sotuv − qaytarilgan = sof" tenglamasi ko'rinib turadi.
    int ReturnedQty = 0,
    decimal ReturnedTotal = 0m,
    // Davr ICHIDA qaytarib berilgan pul (QAYTARISH sanasi bo'yicha — kassadan haqiqatan
    // chiqqan summa; o'tgan oyda sotilgan kitob shu oyda qaytarilishi mumkin).
    decimal RefundedInPeriod = 0m,
    int RefundedCount = 0);

/// <summary>NASIYA bo'limidagi bitta QARZDOR (xaridor kesimida jamlangan).</summary>
public record BookDebtorDto(
    // Guruhlash kaliti: o'quvchi id'si bo'lsa "s:{id}", aks holda "n:{ism}|{telefon}".
    string Key,
    string? StudentId,
    string Name,
    string Phone,
    // To'lanmagan nasiyalar soni va summasi.
    int Orders,
    decimal Total,
    // Eng eski to'lanmagan nasiya sanasi va muddati o'tganlari bormi.
    string OldestDate,
    bool HasOverdue);

/// <summary>
/// NASIYA bo'limi: to'lanmagan qarzlar ro'yxati + qarzdorlar kesimi + jamlanma.
/// Jami summalar TO'LIQ topilma bo'yicha SQL tomonda hisoblanadi (<paramref name="Orders"/>
/// ko'rsatish uchun cheklangan bo'lishi mumkin).
/// </summary>
public record BookCreditsDto(
    // Joriy qarz (butun tarix bo'yicha, filtrdan qat'i nazar) — bo'limning asosiy raqami.
    decimal TotalUnpaid,
    int CountUnpaid,
    decimal TotalOverdue,
    int CountOverdue,
    // Tanlangan davrda nasiyadan yig'ilgan pul.
    decimal CollectedInPeriod,
    int CollectedCount,
    List<BookDebtorDto> Debtors,
    List<BookOrderDto> Orders);

/// <summary>Botdagi kitob sotuvi sozlamalari (to'lov rekvizitlari).</summary>
public record BookSettingsDto(
    bool BookSalesEnabled,
    string BookCardNumber,
    string BookCardHolder,
    string BookPaymentNote);

/* ---------- O'QUVCHINI USHLAB TURISH BONUSI (retention) ---------- */

/// <summary>
/// Bonus jadvalidagi BITTA OY katagi. Hech qayerda saqlanmaydi — har so'rovda qayta hisoblanadi
/// (to'lov keyinroq kiritilsa katak o'z-o'zidan ✅ ga aylanadi).
///
/// <para>Kataklar HAR FAN uchun ALOHIDA hisoblanadi: <c>Charged</c>/<c>Paid</c> — faqat SHU KURS
/// guruhlariga tegishli summalar (teglanmagan eski yozuvlar narx nisbatida taqsimlanadi).</para>
/// </summary>
/// <param name="State">
/// "paid" ✅ pullik a'zolik bor + qarz yo'q (sanoqqa +1) ·
/// "debt" ⏳ pullik a'zolik bor + qarz bor (+0, lekin sikl UZILMAYDI) ·
/// "nocharge" 📄 pullik a'zolik bor, LEKIN shu oy uchun hisob (MonthlyCharge) YOZILMAGAN
/// (+0, sikl UZILMAYDI va tanaffusga ham kirmaydi — hisob paydo bo'lgach o'z-o'zidan tuzaladi) ·
/// "frozen" ❄️ muzlatilgan (pauza) · "gone" 🚪 pullik a'zolik yo'q (pauza)
/// </param>
public record RetentionMonthCellDto(
    string Month, string State, decimal Charged, decimal Paid,
    string TeacherId, string TeacherName, bool Counted);

/// <summary>Bonusning bitta o'qituvchiga tegadigan ulushi (taxminiy yoki berilgan).
/// <para><c>AlreadyAwarded</c> — bu o'qituvchi SHU o'quvchi orqali ALLAQACHON bonus olgan
/// (bir juftlik = umr bo'yi bitta bonus). Bunda <c>Amount</c> 0 bo'ladi va uning vazni qolgan
/// o'qituvchilarga qayta taqsimlanadi; qator ro'yxatda nega tushib qolgani ko'rinsin uchun
/// <c>Months</c> HAQIQIY qiymat bilan qaytadi.</para></summary>
public record RetentionShareDto(
    string TeacherId, string TeacherName, decimal Months, decimal Amount, bool AlreadyAwarded = false);

/// <summary>Berilgan bonus (tarix). <c>CourseId</c>/<c>CourseName</c> — qaysi FAN bo'yicha
/// (nomi SNAPSHOT). Eski (fanlarga bo'linishdan oldingi) yozuvlarda bo'sh bo'lishi mumkin.</summary>
public record RetentionAwardDto(
    string Id, string StudentId, string StudentName,
    string CourseId, string CourseName,
    int CycleNo, string PeriodFrom, string PeriodTo, decimal TotalAmount,
    string Status, string CancelReason, DateTime CreatedAt, string GivenBy, string Note,
    List<RetentionShareDto> Shares);

/// <summary>
/// Bonus hisobotidagi bitta qator. Qator kaliti — <b>(StudentId, CourseId)</b>: o'quvchi 2 fanga
/// qatnasa hisobotda 2 ta mustaqil qator chiqadi (har fanning sanog'i, davri va bonusi alohida).
/// Bir fan ICHIDA guruh almashtirish (Ingliz A1 → Ingliz A2) siklni UZMAYDI — kalit guruh emas, KURS.
/// </summary>
/// <param name="CourseId">Kurs (Subject id); kursi biriktirilmagan eski guruhda — guruh id'si.</param>
/// <param name="Status">
/// "notstarted" — boshlanish oyi kiritilmagan · "progress" — yo'lda ·
/// "ready" — sanoq to'ldi, bonus berish mumkin · "broken" — sikl uzilgan ·
/// "blocked" — sanoq to'ldi, LEKIN barcha o'qituvchilar bu o'quvchi orqali allaqachon bonus olgan
/// </param>
/// <summary>Bosiladigan havola uchun (id + nom) — guruh yoki o'qituvchi. Jadvalda nomi ko'rinadi,
/// bosilganda id bo'yicha profilga o'tiladi.</summary>
public record RetentionRefDto(string Id, string Name);

public record RetentionRowDto(
    string StudentId, string FullName,
    string CourseId, string CourseName,
    /// <summary>Shu fandagi FAOL guruhlar (bosilsa guruh sahifasiga o'tiladi).</summary>
    List<RetentionRefDto> Groups,
    /// <summary>Shu siklda o'qitgan o'qituvchi(lar) (bosilsa profilga o'tiladi). Sikl boshlanmagan
    /// bo'lsa — faol guruhlarning hozirgi o'qituvchisi.</summary>
    List<RetentionRefDto> Teachers,
    string Days,
    string StartMonth, int CycleNo,
    List<RetentionMonthCellDto> Months,
    int Counted, int Required,
    string Status, string StatusNote,
    bool IsArchived,
    List<RetentionShareDto> Shares,
    List<RetentionAwardDto> Awards);

/// <summary>Bonus tizimi sozlamalari (<c>CenterMeta</c>).</summary>
public record RetentionSettingsDto(int MonthsRequired, int MaxGapMonths, decimal DefaultAmount);

/// <summary>Bonus hisoboti — jadval + sozlamalar + "tayyor" (faqat "ready") qatorlar soni.</summary>
public record RetentionReportDto(
    List<RetentionRowDto> Rows, RetentionSettingsDto Settings, int ReadyCount);

/// <summary>Bonus berish so'rovi — HAR FAN uchun alohida (<c>CourseId</c> majburiy).
/// <c>Shares</c> — admin tahrirlagan taqsimot (yig'indisi <c>TotalAmount</c> ga TENG bo'lishi shart).</summary>
public record GiveRetentionBonusRequest(
    string StudentId, string CourseId, decimal TotalAmount,
    List<RetentionShareInput> Shares, string? Note = null);
public record RetentionShareInput(string TeacherId, decimal Amount, decimal Months = 0);

/// <summary>Uzilgan siklni yangi oydan qayta boshlash — FAQAT ko'rsatilgan fan uchun.</summary>
public record RestartRetentionRequest(string CourseId, string StartMonth);

/// <summary>Bonusni bekor qilish (xato kiritilgan bo'lsa).</summary>
public record CancelRetentionBonusRequest(string? Reason = null);

/// <summary>O'qituvchi profilidagi "Bonus" tabi uchun — u olgan barcha ulushlar.</summary>
public record TeacherRetentionBonusDto(
    string AwardId, string StudentId, string StudentName, string CourseName,
    string PeriodFrom, string PeriodTo, decimal Months, decimal Amount,
    DateTime GivenAt, string GivenBy, string Status);

/// <summary>
/// O'qituvchi profilidagi "yo'ldagilar" qatori: shu o'qituvchiga oy(lar) to'planayotgan, lekin
/// hali bonus berilmagan (o'quvchi × fan) sikli. O'qituvchi "yangi o'quvchilarim qanday
/// hisoblanyapti" ni shu orqali ko'radi.
/// </summary>
/// <param name="MyMonths">Shu SIKLDA aynan SHU o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin:
/// o'quvchi bir fan bo'yicha parallel ikki guruhda o'qisa oy vazni narx nisbatida bo'linadi).</param>
/// <param name="Status">Qator holati: "progress" | "ready" | "broken" | "blocked".</param>
/// <param name="AlreadyAwarded">Shu o'qituvchi bu o'quvchi orqali allaqachon bonus olganmi — olgan
/// bo'lsa bu sikldan unga bonus tegmaydi (qoida: bir o'qituvchi — bir o'quvchi — bir bonus).</param>
public record TeacherRetentionProgressDto(
    string StudentId, string StudentName,
    string CourseId, string CourseName,
    string GroupNames,
    int Counted, int Required,
    decimal MyMonths,
    string Status,
    string StatusNote,
    bool AlreadyAwarded);

/// <summary>O'qituvchining bonus jamlanmasi: berilganlar (<paramref name="Items"/>) va hali
/// yo'ldagi sikllar (<paramref name="InProgress"/>). <paramref name="Total"/>/<paramref name="Count"/>
/// — faqat bekor qilinmagan bonuslar.</summary>
public record TeacherRetentionSummaryDto(
    decimal Total, int Count, List<TeacherRetentionBonusDto> Items,
    List<TeacherRetentionProgressDto>? InProgress = null);

/* =================================================================================================
 *  BOG'LANISH KERAK (follow-up navbati)
 * ============================================================================================== */

/// <summary>Yangi "bog'lanish kerak" talabi (o'quvchi profilidagi "⋮" menyusidan).</summary>
/// <param name="ReasonId">Sabab (<c>ActionReason</c>.Id, kategoriya "contact"). Bo'sh — sababsiz.</param>
/// <param name="Note">Qo'shimcha izoh (ixtiyoriy).</param>
/// <param name="DueDate">Darhol qayta qo'ng'iroqqa qo'yish sanasi ("yyyy-MM-dd"). Bo'sh — navbatga
/// "Bog'lanish kerak" holatida tushadi.</param>
public record CreateContactRequest(string StudentId, string? ReasonId = null, string? Note = null,
    string? DueDate = null);

/// <summary>
/// BOG'LANILDI — bitta urinish natijasi. Modulning asosiy amali.
/// </summary>
/// <param name="Result">Natija kaliti (<c>ContactService.Results</c>): answered | no_answer | ...</param>
/// <param name="Response">"Javobi nima dedi" — erkin matn.</param>
/// <param name="NextStatus">Keyingi bosqich: callback | done | failed
/// (<c>ContactService.CanTransitionTo</c>).</param>
/// <param name="DueDate">Qayta qo'ng'iroq sanasi — <paramref name="NextStatus"/>=="callback" da MAJBURIY.</param>
public record ContactAttemptRequest(string Result, string? Response, string NextStatus, string? DueDate = null);

/// <summary>Talabga oddiy izoh qo'shish (bosqich o'zgarmaydi).</summary>
public record ContactNoteRequest(string Text);

/// <summary>Yakunlangan talabni QAYTA ochish (yana navbatga qaytadi).</summary>
public record ContactReopenRequest(string? Note = null);

/// <summary>Navbat/tarix qatoridagi bitta hodisa.</summary>
public record ContactAttemptDto(
    string Id, string Type, string Result, string ResultLabel, string Response,
    string NextStatus, string NextStatusLabel, string DueDate,
    string ActorName, string CreatedAt);

/// <summary>Navbatdagi bitta talab.</summary>
/// <param name="Overdue">Qayta qo'ng'iroq muddati o'tganmi (bugundan oldin).</param>
/// <param name="Phones">Bog'lanish uchun raqamlar (o'quvchi + ota-ona), takrorsiz.</param>
public record ContactRequestDto(
    string Id, string StudentId, string StudentName,
    string ReasonId, string ReasonLabel, string Note,
    string Status, string StatusLabel, string DueDate, bool Overdue,
    int AttemptCount, string LastResponse, string LastActorName, string LastActionAt,
    string CreatedAt, string CreatedBy, string ClosedAt, string ClosedBy,
    List<string> Phones,
    List<ContactAttemptDto>? History = null);

/// <summary>Bosqich/natija KATALOGI + navbat sanoqlari (sahifa bir so'rovda to'liq ochilsin).</summary>
/// <param name="Due">MUDDAT bo'yicha kesim — "bugun nechta odamga bog'lanish kerak".</param>
/// <param name="Days">Yaqin kunlar rejasi: qaysi kuni nechta qayta qo'ng'iroq bor.</param>
public record ContactMetaDto(
    List<ContactStatusDto> Statuses, List<ContactResultDto> Results,
    List<ContactCountDto> Counts, int Overdue,
    ContactDueCountsDto? Due = null, List<ContactDayPlanDto>? Days = null);

/// <summary>
/// Navbatning MUDDAT bo'yicha kesimi (<c>ContactService.Due</c>).
/// </summary>
/// <param name="Todo">BUGUN qilinishi kerak = <paramref name="Overdue"/> + <paramref name="Today"/>
/// + <paramref name="NoDate"/>. Operatorning asosiy raqami.</param>
/// <param name="Week">Ertadan keyingi 6 kun (bugundan +2..+7).</param>
/// <param name="NoDate">Sana belgilanmagan ("Bog'lanish kerak" holatidagilar).</param>
public record ContactDueCountsDto(
    int Todo, int Overdue, int Today, int Tomorrow, int Week, int Later, int NoDate);

/// <summary>Bitta kunning rejasi — "qaysi kuni nechta qo'ng'iroq".</summary>
public record ContactDayPlanDto(string Date, int Count);

public record ContactStatusDto(string Key, string Label, bool IsOpen, string Color);
public record ContactResultDto(string Key, string Label, bool Reached);
public record ContactCountDto(string Key, int Count);

/* ---------- Hisobotlar ---------- */

/// <summary>KUNLIK qator: shu kunda nima bo'lgan.</summary>
/// <param name="Reached">Odam bilan HAQIQATAN gaplashilgan urinishlar ("nechta odam bilan bog'lanildi").</param>
/// <param name="Attempts">Jami urinishlar (ko'tarmagani ham).</param>
public record ContactDailyRowDto(
    string Date, int Created, int Attempts, int Reached, int Done, int Callback, int Failed);

/// <summary>XODIM kesimi — "kim qaysi bosqichga oldi, natijasi qanday bo'ldi".</summary>
public record ContactStaffRowDto(
    string ActorName, int Attempts, int Reached, int Done, int Callback, int Failed);

/// <summary>SABAB kesimi — qaysi sabab bilan nechta talab ochilgan va qanchasi hal bo'lgan.</summary>
public record ContactReasonRowDto(string ReasonLabel, int Created, int Done, int Failed, int Open);

/// <summary>NATIJA kesimi (ko'tarmadi/band/...) — qo'ng'iroq sifati ko'rsatkichi.</summary>
public record ContactResultRowDto(string Key, string Label, int Count);

/// <summary>"Bog'lanish kerak" bo'limi hisobotlari (davr bo'yicha).</summary>
public record ContactStatsDto(
    string From, string To,
    int Created, int Attempts, int Reached, int Done, int Callback, int Failed,
    int OpenNow, int OverdueNow,
    List<ContactDailyRowDto> Daily,
    List<ContactStaffRowDto> ByStaff,
    List<ContactReasonRowDto> ByReason,
    List<ContactResultRowDto> ByResult,
    /// <summary>Javoblarda eng ko'p uchragan so'zlar — "nima deb yozilyapti" ni bir qarashda ko'rsatadi.</summary>
    List<ContactWordDto>? TopWords = null,
    /// <summary>Javob YOZILGAN urinishlar soni (bo'sh javoblar hisobga olinmaydi).</summary>
    int WithResponse = 0);

/* =================================================================================================
 *  KURSLAR ANALITIKASI (O'quv bo'limi)
 * ============================================================================================== */

/// <summary>Kursning bir oydagi oqimi.</summary>
/// <param name="Joined">Kursga KELGAN (sinovdagilar ham) — oraliq boshlandi.</param>
/// <param name="Activated">Shu oyda BIRINCHI marta aktivlashgan (to'lov boshlangan).</param>
/// <param name="Left">KETGAN (haqiqiy churn — tugatgan emas).</param>
/// <param name="Completed">Kursni TUGATGAN (sertifikat bilan).</param>
/// <param name="ActiveEnd">Oy oxirida faol bo'lgan o'quvchilar.</param>
public record CourseMonthFlowDto(
    string Month, int Joined, int Activated, int Left, int Completed, int ActiveEnd);

/// <summary>Bitta kursning to'liq kesimi.</summary>
/// <param name="Students">HOZIR shu kursda o'qiyotgan takrorsiz o'quvchilar (faol+sinov+muzlatilgan).</param>
/// <param name="TotalEver">Shu kursda BIROR PAYT o'qigan takrorsiz o'quvchilar.</param>
/// <param name="MonthlyRevenue">Faol a'zoliklar bo'yicha kutilayotgan oylik tushum (guruh oyliklari yig'indisi).</param>
/// <param name="Monthly">Oylik oqim (eng eski oydan boshlab).</param>
public record CourseAnalyticsRowDto(
    string CourseId, string CourseName, decimal Price,
    int Groups, int Teachers,
    int Active, int Trial, int Frozen, int Students, int TotalEver,
    decimal MonthlyRevenue,
    List<CourseMonthFlowDto> Monthly);

/// <summary>Nechta kursga qatnashadigan o'quvchilar taqsimoti.</summary>
public record CourseOverlapBucketDto(int Courses, int Students);

/// <summary>Birga o'qiladigan kurs juftligi.</summary>
public record CoursePairDto(string AId, string AName, string BId, string BName, int Students);

/// <summary>Kurslar kesishuvi — FAOL a'zoliklar bo'yicha.</summary>
/// <param name="OneCourse">Faqat bitta kursga qatnaydigan o'quvchilar.</param>
/// <param name="MultiCourse">BIRDAN ORTIQ kursga qatnaydigan o'quvchilar.</param>
public record CourseOverlapDto(
    int TotalStudents, int OneCourse, int MultiCourse,
    List<CourseOverlapBucketDto> Buckets, List<CoursePairDto> Pairs);

/// <summary>"Kurslar analitikasi" sahifasining butun ma'lumoti (bitta so'rovda).</summary>
/// <param name="Months">Ko'rilayotgan oylar ("yyyy-MM"), eng eskisidan.</param>
/// <param name="ActiveStudents">Markazda faol a'zoligi bor takrorsiz o'quvchilar.</param>
public record CourseAnalyticsDto(
    List<string> Months,
    List<CourseAnalyticsRowDto> Courses,
    CourseOverlapDto Overlap,
    int ActiveStudents, int TotalGroups, decimal MonthlyRevenue);

/* ---------- Bog'lanish kerak: KO'PLAB qo'shish va JAVOBLAR tahlili ---------- */

/// <summary>Bir nechta o'quvchini birdan navbatga qo'shish ("O'quvchilar ro'yxati"dan).</summary>
public record CreateContactRequestsBulk(
    List<string> StudentIds, string? ReasonId = null, string? Note = null, string? DueDate = null);

/// <summary>Ko'plab qo'shish natijasi.</summary>
/// <param name="Skipped">Ochiq talabi borligi uchun CHETLAB O'TILGANLAR soni.</param>
/// <param name="SkippedNames">Ulardan bir nechtasining ismi (xabarda ko'rsatish uchun, chegaralangan).</param>
/// <param name="NotFound">Topilmagan o'quvchilar soni (ro'yxat eskirgan bo'lsa).</param>
public record ContactBulkResultDto(int Created, int Skipped, List<string> SkippedNames, int NotFound);

/// <summary>Yozilgan JAVOB ("javobi nima dedi") — hisobotdagi javoblar lentasi uchun.</summary>
public record ContactResponseRowDto(
    string Id, string RequestId, string StudentId, string StudentName,
    string ReasonLabel, string Result, string ResultLabel,
    string NextStatus, string NextStatusLabel,
    string Response, string ActorName, string CreatedAt);

/// <summary>Javoblarda eng ko'p uchragan so'z.</summary>
public record ContactWordDto(string Word, int Count);

/* ---------- Bog'lanish kerak: KUNLIK JURNAL ("bugun kimga qo'ng'iroq qilindi") ---------- */

/// <summary>
/// Kunlik jurnalning BITTA qatori — bitta hodisa: kimga, qachon, nima deyilgani.
/// </summary>
/// <param name="Type">created | contact | note | reopen (<c>ContactAttemptTypes</c>).</param>
/// <param name="Time">"HH:mm" — jurnalda soat ko'rinsin (to'liq ISO ham <paramref name="CreatedAt"/> da).</param>
/// <param name="Phones">O'quvchi + ota-ona raqamlari — operator qatordan darhol qayta qo'ng'iroq qila olsin.</param>
public record ContactJournalItemDto(
    string Id, string RequestId, string StudentId, string StudentName,
    string ReasonLabel, string Type, string TypeLabel,
    string Result, string ResultLabel,
    string NextStatus, string NextStatusLabel, string DueDate,
    string Response, string ActorName, string Time, string CreatedAt,
    List<string> Phones);

/// <summary>BITTA KUN — o'sha kunning jamlanmasi va hodisalari (ertalabdan kechgacha).</summary>
public record ContactJournalDayDto(
    string Date, int Created, int Attempts, int Reached, int Done, int Callback, int Failed,
    List<ContactJournalItemDto> Items);

/* ---------- Bog'lanish kerak: AI TAHLIL (davr bo'yicha) ---------- */

/// <summary>
/// AI'ga beriladigan DETERMINISTIK raqamlar — hisobot sahifasidagi sonlar bilan AYNAN bir xil
/// (<c>ContactAiAnalysisService.BuildMetricsAsync</c> ularni <c>ContactsController.Stats</c> bilan
/// bir xil qoidada yig'adi).
/// </summary>
/// <param name="Samples">Javob MATNLARIDAN namunalar — AI "nima deyilyapti" ni shundan o'qiydi.
/// ⚠️ O'QUVCHI ISMI va TELEFONI kirmaydi (qarang: <c>ContactAiSampleDto</c>).</param>
public record ContactAiMetricsDto(
    string From, string To,
    int Created, int Attempts, int Reached, int Done, int Callback, int Failed,
    int OpenNow, int OverdueNow, int WithResponse,
    List<ContactDailyRowDto> Daily,
    List<ContactStaffRowDto> ByStaff,
    List<ContactReasonRowDto> ByReason,
    List<ContactResultRowDto> ByResult,
    List<ContactWordDto> TopWords,
    List<ContactAiSampleDto> Samples);

/// <summary>
/// Promptga ketadigan BITTA javob namunasi.
///
/// <para>⚠️ MAXFIYLIK: o'quvchining ISMI ham, TELEFONI ham ATAYIN yo'q — tahlil savoli
/// "kim" emas, "NIMA deyilyapti va natija qanday". Xodim ismi qoladi: xodimlar kesimi
/// (kim qanday ishlayapti) tahlilning maqsadli qismi va bu ICHKI ma'lumot.</para>
/// </summary>
public record ContactAiSampleDto(
    string Date, string ReasonLabel, string ResultLabel, string NextStatusLabel,
    string Response, string ActorName);

/// <summary>Sohaviy baholar (0..100).</summary>
/// <param name="Qamrov">Navbat ishlanyaptimi (ochiq/muddati o'tganlarga nisbatan urinishlar).</param>
/// <param name="Aloqa">Odam bilan haqiqatan gaplashish ulushi (Reached / Attempts).</param>
/// <param name="Natija">Bog'lanishlar natija berdimi (hal bo'ldi / bo'lmadi).</param>
/// <param name="Sifat">Yozuvlar sifati — "javobi nima dedi" to'ldirilyaptimi, matn mazmunlimi.</param>
public record ContactAiScoresDto(int Qamrov, int Aloqa, int Natija, int Sifat, int Umumiy);

/// <summary>AI yozgan narrativ — bo'sh maydon ekranda umuman chizilmaydi.</summary>
/// <param name="Sabablar">Qaysi sabablar ustunlik qilmoqda va ular nimani ko'rsatadi.</param>
/// <param name="Javoblar">Javob matnlaridagi TAKRORLANUVCHI naqshlar ("nima deyilyapti").</param>
/// <param name="Sifat">Aloqa sifati: ko'tarmagan/band ulushi, yozuvlarning to'liqligi.</param>
/// <param name="Xodimlar">Xodimlar kesimi — kim qanday ishlayapti.</param>
public record ContactAiNarrativeDto(
    string Umumiy, string Sabablar, string Javoblar, string Sifat, string Xodimlar,
    string Ozgarishlar,
    List<string> Kuchli, List<string> Zaif, List<string> Xavflar, List<string> Tavsiyalar,
    ContactAiScoresDto Baholar, string Trend);

/// <summary>AI tahlil so'rovi — QAYSI DAVR tahlil qilinadi (bo'sh — oxirgi 30 kun).</summary>
public record ContactAiRequest(string? From = null, string? To = null);

/// <summary>Saqlangan bitta tahlil (raqamlari bilan — eski tahlil ochilganda ham to'liq ko'rinadi).</summary>
public record ContactAiRecordDto(
    string Id, string From, string To, string Date, string CreatedAt, string Model,
    int OverallScore, ContactAiNarrativeDto Ai, ContactAiMetricsDto Metrics);

/// <summary>
/// Tahlil yaratish javobi. <paramref name="AlreadyToday"/>=true — XATO EMAS: shu DAVR uchun
/// bugun tahlil qilingan, <paramref name="Record"/> da o'sha qaytadi (Gemini chaqirilmaydi).
/// </summary>
public record ContactAiResponseDto(
    bool Ok, bool AlreadyToday, ContactAiRecordDto? Record, string? Error);

/* =================================================================================================
 *  IZOHLARGA JAVOBLAR — o'quvchi profillariga yozilgan izohlar bir joyda (O'quvchilar bo'limi)
 * ============================================================================================== */

/// <summary>
/// Bitta o'quvchi — unga yozilgan izohlarning JAMLANMASI ("Izohlarga javoblar" ro'yxati).
/// </summary>
/// <param name="NoteCount">Shu o'quvchiga yozilgan izohlar soni (filtrga tushganlari).</param>
/// <param name="LastNoteText">Oxirgi izoh matni — ro'yxatda ko'rinadi (qisqartirilmaydi, UI kesadi).</param>
/// <param name="Authors">Izoh yozgan xodimlar (takrorsiz, F.I.Sh).</param>
/// <summary>
/// Oyning bir KUNIDA nechta izoh yozilgan — sana chizig'idagi kataklar uchun.
/// (Kalendar TANLANGAN davrga bog'liq emas: bitta kun tanlanganda ham butun oy ko'rinib tursin.)
/// </summary>
public record StudentNoteDayDto(string Date, int Count);

public record StudentNoteOverviewDto(
    string StudentId, string FullName, List<string> Groups, string Phone, string ParentPhone,
    bool IsArchived,
    int NoteCount, string FirstNoteAt, string LastNoteAt, string LastNoteText,
    string LastAuthorName, List<string> Authors);

/// <summary>
/// O'qituvchi jurnalidagi "Aloqa" tabidan navbatga yuborish. SANA YO'Q — talab darhol
/// navbatga tushadi (bugungi ish); rejalashtirish operatorning ishi.
/// </summary>
public record TeacherContactRequest(List<string> StudentIds, string? ReasonId = null, string? Note = null);

/* =================================================================================================
 *  O'RINBOSAR O'QITUVCHILAR (Substitute Teacher Assignments)
 * ============================================================================================== */

/// <summary>O'rinbosar o'qituvchi tayinlash so'rovi (admin).</summary>
public record CreateSubstituteAssignmentRequest(
    [Required] string GroupId,
    [Required] string SubstituteTeacherId,
    List<string>? Dates = null,
    string? Date = null,
    string? EndDate = null,
    string? Reason = null);

/// <summary>
/// O'rinbosar o'qituvchi tayinlovi ma'lumoti.
///
/// <para><b>PUL — NOL YIG'INDILI:</b> <paramref name="EstimatedSalary"/> (o'rinbosarga to'lanadi)
/// va <paramref name="EstimatedDeduction"/> (asosiy o'qituvchidan ushlanadi) AYNAN TENG —
/// markaz uchun amal neytral. Ikkalasi ham bitta hisoblagichdan
/// (<c>SubstituteTeacherService.PerLesson</c>). Ikki alohida maydon ATAYIN: UI ikki tomonni
/// ("kimga qo'shildi / kimdan ayrildi") ochiq ko'rsatadi va model kelajakda o'zgarsa
/// (masalan markaz ustidan qo'shsa) shartnoma buzilmaydi.</para>
///
/// <para>⚠️ Bu maydonlar — MAOSH ma'lumoti. Admin API'da o'qish <c>teachers</c> ruxsati bilan
/// darvozalangan, o'qituvchi ilovasida esa <c>TeacherPermissions.Salary</c> yo'q bo'lsa
/// TOZALANADI (nolga tushiriladi).</para>
/// </summary>
public record SubstituteTeacherAssignmentDto(
    string Id,
    string GroupId,
    string GroupName,
    string OriginalTeacherId,
    string OriginalTeacherName,
    string SubstituteTeacherId,
    string SubstituteTeacherName,
    string Date,
    string? EndDate,
    string Reason,
    string CreatedBy,
    DateTime CreatedAt,
    bool IsActive,
    int LessonCount = 1,
    decimal EstimatedSalary = 0m,
    List<string>? Dates = null,
    decimal PerLessonFee = 0m,
    decimal EstimatedDeduction = 0m,
    int StudentCount = 0);

/// <summary>
/// Admin modalidagi JONLI hisob (<c>GET /api/admin/substitute-teachers/preview</c>) — frontend
/// pulni O'ZI hisoblamaydi (ilgari hisoblardi va serverdagi raqamdan farq qilardi).
/// </summary>
/// <param name="LessonCount">Tanlangan dars sanalari soni.</param>
/// <param name="PerLessonFee">Bitta dars narxi (bir necha oyga tushsa — O'RTACHA).</param>
/// <param name="EstimatedSalary">O'rinbosarga to'lanadigan summa.</param>
/// <param name="EstimatedDeduction">Asosiy o'qituvchidan ushlanadigan summa (nol yig'indili — teng).</param>
/// <param name="StudentCount">Guruhdagi FAOL o'quvchilar soni (foizli hovuz shundan).</param>
/// <param name="MonthLessons">Oyning HAQIQIY dars soni — bitta dars narxining MAXRAJI.</param>
/// <param name="Warning">Foydalanuvchiga ko'rsatiladigan ogohlantirish yoki <c>null</c>
/// (masalan "guruhda faol o'quvchi yo'q", "bu oyda pul yig'ilmagan").</param>
public record SubstituteFeePreviewDto(
    int LessonCount,
    decimal PerLessonFee,
    decimal EstimatedSalary,
    decimal EstimatedDeduction,
    int StudentCount,
    int MonthLessons,
    string? Warning);

/// <summary>Guruhning oydagi rejalashtirilgan dars sanasi (modal uchun).</summary>
public record GroupLessonDateDto(string Date, string DayName, bool IsScheduled);

/* ---------- Global o'quvchi qidiruvi (topbar / Ctrl+K) ---------- */

/// <summary>Qidiruv natijasidagi bitta a'zolik: guruh nomi + holati (dropdownda chip bo'lib
/// ko'rinadi). MUZLATILGANLAR ham kiradi — maqsad "qayerda va qanday holatda"ni ko'rsatish.</summary>
public record StudentSearchGroupDto(string Name, string Status);

/// <summary>
/// <c>GET /api/admin/students/search</c> javobi — ATAYIN yengil: to'liq <c>Student</c> emas.
/// Hujjat manzillari (<c>BirthCertificateUrl</c>, <c>ParentPassportUrl</c>) bu DTO'ga UMUMAN
/// kirmaydi, ya'ni <c>RedactDocs</c> ga ehtiyoj yo'q (<c>uploads-security.md</c> qoidasi).
/// </summary>
/// <param name="Phone">O'quvchining o'z raqami (bo'sh bo'lishi mumkin).</param>
/// <param name="ParentPhone">Ota-onaning BIRINCHI mavjud raqami (asosiy → ota → ona) —
/// dropdownda o'z raqami bo'lmaganda ko'rsatiladi.</param>
/// <param name="MemberState">"active" | "trial" | "frozen" | "" — badge uchun
/// (<c>GetAll</c> dagi ustunlik bilan bir xil).</param>
public record StudentSearchResultDto(
    string Id,
    string FullName,
    string Phone,
    string ParentPhone,
    bool IsArchived,
    string MemberState,
    List<StudentSearchGroupDto> Groups);

