using WunderkindLC.Application.Services.Kpi;

namespace WunderkindLC.Application.Dtos;

/* =====================================================================================
 *  KPI (xodimlar samaradorligi) — API KONTRAKTI
 * =====================================================================================
 *  ⚠️ Bu fayldagi recordlar `WunderkindLC.Client/src/api/services/kpi.ts` dagi interfeyslar
 *  bilan AYNAN mos bo'lishi SHART (ASP.NET default JSON siyosati — camelCase). Maydon
 *  qo'shilsa/nomi o'zgarsa ikkala fayl BIRGA o'zgaradi: aks holda klient `undefined` oladi
 *  va ekranda "0" chiqadi — bu XATO emas, "raqam yo'q" bo'lib ko'rinadi, ya'ni jimgina
 *  noto'g'ri oylik ko'rsatiladi.
 *
 *  ⚠️ `Dtos.cs` ga TEGILMAYDI — KPI o'z faylida (bo'lim mustaqil o'sadi).
 */

/// <summary>
/// Bitta KIRISH raqami va uning «QAYERDAN» izi.
///
/// <para>Modulning bosh xususiyati: xodim har raqamni BOSIB tekshira olishi kerak.
/// <paramref name="Source"/> — raqam qanday olingani ("Lidlar: yaratilgan sana oy ichida va
/// mas'ul — shu xodim"), <paramref name="Link"/> — o'sha raqamni ISBOTLAYDIGAN mavjud sahifa.
/// Izsiz raqam bahsni hal qilmaydi: "menda boshqacha chiqdi" deyilganda ko'rsatadigan joy
/// bo'lmasdi.</para>
/// </summary>
/// <param name="Key">Barqaror kalit (<c>leads</c>, <c>contracts</c> ...) — klient shu bo'yicha topadi.</param>
/// <param name="Label">O'zbekcha yorliq.</param>
/// <param name="Value">Raqamning O'ZI (pul ham shu yerda — <c>double</c>, chunki bu KO'RSATKICH,
/// hisob-kitob emas; pul hisobi <c>KpiMoneyLineDto</c> da <c>decimal</c> bilan boradi).</param>
/// <param name="Source">"Qayerdan olindi" matni.</param>
/// <param name="Link">Isbot sahifasi (mavjud marshrut) yoki <c>null</c>.</param>
/// <param name="Auto">Tizim O'ZI hisobladimi (qo'lda kiritilmagan).</param>
/// <param name="Estimated">⚠️ Oy boshi snapshoti yo'q — qiymat JONLI hisobdan tiklangan, ya'ni
/// TAXMINIY. UI ogohlantirish ko'rsatadi; jimgina "aniq raqam" bo'lib ko'rinmasin.</param>
public sealed record KpiInputDto(
    string Key, string Label, double Value, string Source, string? Link, bool Auto, bool Estimated = false);

/// <summary>Bitta koeffitsient: qaysi XOM qiymatdan, qaysi pog'ona tanlandi va NIMA UCHUN.</summary>
/// <param name="Value">Pog'ona tanlangan xom qiymat (masalan konversiya 0.3286).</param>
/// <param name="Coef">Tanlangan koeffitsient.</param>
/// <param name="Note">Pog'onaning izohi ("NORMA. Bonus to'liq.") — raqamning O'ZI hech narsani
/// tushuntirmaydi.</param>
public sealed record KpiCoefDto(string Key, string Label, double Value, double Coef, string Note);

/// <summary>Oylik hisobning bitta PUL qatori (oklad, bonus A/B/C, jarima, jami).</summary>
public sealed record KpiMoneyLineDto(string Key, string Label, decimal Amount, string? Note = null);

/// <summary>
/// Bitta xodimning bitta oydagi TO'LIQ hisobi — «Oy» va «Yopish» sahifalari uchun.
///
/// <para>⚠️ <see cref="Status"/> = <c>confirmed</c> bo'lsa raqamlar MUZLATILGAN
/// (<c>KpiMonthResult</c> dan o'qiladi), jonli qayta hisoblanmaydi — yopishning butun ma'nosi
/// shu. Qoralamada esa hammasi har so'rovda qayta hisoblanadi (kechikkan ma'lumot o'z-o'zidan
/// tuzalsin).</para>
/// </summary>
/// <param name="Computed">Kafolat/shift QO'LLANMASDAN oldingi summa.</param>
/// <param name="Salary">Yakuniy summa (kafolat qo'llangandan keyin), so'mgacha yaxlitlangan.</param>
/// <param name="CapExceeded">Shiftdan oshdi — summa KESILMAYDI, bu faqat rahbar uchun bayroq.</param>
/// <param name="RulesMissing">Shu oyga qoidalar versiyasi topilmadi — raqamlar NOL, hisob yo'q.</param>
/// <param name="SnapshotMissing">Oy boshi snapshoti yo'q — <c>activeStart</c>/<c>debtStart</c> taxminiy.</param>
/// <param name="Warning">Foydalanuvchiga ko'rsatiladigan ogohlantirish (o'lchanmagan samaradorlik va h.k.).</param>
/// <param name="Conversions">Excel «2-bo'lim» NISBATLARI — TO'LIQ ro'yxat: kiruvchida
/// <c>convLeadToTrial · convTrialToDeal · convLeadToDeal</c>, chiquvchida
/// <c>churnRate · extRate · debtRateFact</c>.
/// ⚠️ <see cref="Coefs"/> dan FARQI: bu yerda nisbatlarning HAMMASI turadi (ular xodimga
/// "voronka qayerda oqyapti" ni ko'rsatadi), <see cref="Coefs"/> da esa FAQAT pulni
/// ko'paytiradigan qatorlar. Ikkovini bitta ro'yxatga qo'shsak, hech narsani ko'paytirmaydigan
/// nisbat (masalan <c>convLeadToDeal</c>) "koeffitsient" bo'lib ko'rinardi va xodim uni
/// oyligidan izlab, topolmasdi. Koeffitsient bermaydigan nisbatda <c>Coef = 1</c> va sabab
/// <c>Note</c> da yoziladi.</param>
public sealed record KpiMonthDto(
    string UserId, string UserName, string RoleCode, string RoleLabel, string Month,
    List<KpiInputDto> Inputs,
    List<KpiCoefDto> Conversions,
    List<KpiCoefDto> Coefs,
    List<KpiMoneyLineDto> Lines,
    decimal BaseSalary, decimal BonusTotal, decimal FineTotal,
    decimal Computed, decimal Salary,
    bool GuaranteeApplied, bool CapExceeded, decimal? Cap,
    string Status,
    string? ConfirmedBy, string? ConfirmedAt,
    string? RuleSetId, bool RulesMissing, bool SnapshotMissing,
    KpiPlanDto? Plan,
    string? Warning);

/// <summary>
/// 5-QADAM nazorati: reja bilan solishtirish (faqat <c>intake_admin</c> / <c>call_operator</c>).
/// </summary>
/// <param name="LeadFloorApplied">Lid oqimi KAFOLATI ishladimi — lid <c>LeadFloor</c> dan kam
/// bo'lsa reja haqiqiy lid oqimidan qayta hisoblanadi (xodim marketingning kam ishlagani uchun
/// jazolanmasin).</param>
public sealed record KpiPlanDto(
    int PlanContracts, double PlanDone, bool LeadFloorApplied, int LeadFloor,
    double DailyLeads, double DailyTouches, double DailyTrials, double DailyContracts);

/// <summary>
/// KUNLIK NORMA: reja va fakt — IKKALASI HAM RAQAM.
///
/// <para>⚠️ Reja matn maydonida ("kuniga 13.5 ta lid") BERILMAYDI: klient progress chizig'ini
/// chizishi, foizni hisoblashi va normadan chetlashishni rang bilan ko'rsatishi kerak — matnni
/// esa u qayta parse qilishga majbur bo'lardi. Reja SERVERDA, o'sha oyda amal qiladigan
/// qoidalardan hisoblanadi (spetsifikatsiya §16), ya'ni koeffitsient o'zgarsa norma ham
/// o'z-o'zidan o'zgaradi.</para>
/// </summary>
/// <param name="Unit">Qisqa o'lchov qo'shimchasi: "ta", "soat", "so'm" yoki bo'sh.</param>
/// <param name="Inverse">⚠️ PAST bo'lgani YAXSHI ("ketma-ket 3 dars kelmaganlar — norma 0",
/// "kechikkan vazifa — 0 ta"). Busiz UI bunday kartani fakt 0 dan oshgan ZAHOTI "bajarildi"
/// deb (yoki teskarisi — doim "bajarilmadi" deb) ko'rsatardi.</param>
/// <param name="Note">Qo'shimcha izoh — norma qayerdan olingani yoki nima uchun o'lchanmagani.</param>
public sealed record KpiNormDto(
    string Key, string Label, double Fact, double Plan, string Unit, bool Inverse, string? Link, string? Note);

/// <summary>«Bugun» sahifasi: kunlik norma, cheklist, signallar va erta ogohlantirish ro'yxatlari.</summary>
public sealed record KpiTodayDto(
    string UserId, string UserName, string RoleCode, string Date,
    List<KpiNormDto> Norms,
    List<KpiChecklistBlockDto> Blocks,
    List<KpiSignalDto> Signals,
    List<KpiAlertListDto> Lists,
    KpiMonthForecastDto? Forecast);

/// <summary>Cheklistning bitta VAQT BLOKI ("09:45 – 11:30  FAOL OBZVON #1").</summary>
public sealed record KpiChecklistBlockDto(string TimeBlock, List<KpiChecklistItemDto> Items);

/// <summary>Cheklist bandi + shu KUNDAGI belgisi.</summary>
/// <param name="State">done | failed | na (<c>KpiConst</c>). Belgilanmagan band ham <c>na</c>
/// bilan qaytadi — klient uchta holatni bir xil chizadi.</param>
/// <param name="Source">manual | auto.</param>
public sealed record KpiChecklistItemDto(
    string ItemId, int No, string Text, string Norm, string? KpiTag, int? CriterionNo,
    string? AutoCheckKey, string State, string Source, string? Note);

/// <summary>Signal chipi: kechikkan topshiriq, tasdiqlangan tiket va h.k.</summary>
/// <param name="Tone">ok | warn | bad — rang shkalasi (klient rangni O'ZI tanlamaydi).</param>
public sealed record KpiSignalDto(string Key, string Label, int Count, string Tone, string? Link);

/// <summary>Erta ogohlantirish ro'yxati (chiquvchi admin uchun) — "bugun kimga qarash kerak".</summary>
public sealed record KpiAlertListDto(string Key, string Label, string? Link, List<KpiAlertRowDto> Rows);

/// <summary>Ogohlantirish ro'yxatining bitta qatori.</summary>
public sealed record KpiAlertRowDto(string Id, string Title, string Sub, string? Link);

/// <summary>"Shu tezlikda davom etsa oy oxirida…" — kunlik sahifadagi rag'bat.</summary>
public sealed record KpiMonthForecastDto(decimal Salary, double Progress, string Note);

/// <summary>Xodimning KPI profili: rol, kafolat va OKLAD TARIXI.</summary>
/// <param name="CurrentSalary">Joriy oyda amal qiladigan oklad (<c>KpiVersioning.SalaryFor</c>).</param>
public sealed record KpiProfileDto(
    string Id, string UserId, string UserName, string Position, string RoleCode, string RoleLabel,
    string StartMonth, string? GuaranteeUntilMonth, bool IsActive, string? Note,
    decimal CurrentSalary, List<KpiSalaryDto> Salaries);

/// <summary>Bitta oklad VERSIYASI (eski versiyalar hech qachon o'chirilmaydi — tarix).</summary>
public sealed record KpiSalaryDto(string Id, string EffectiveFrom, decimal BaseSalary, string? Note,
    string? CreatedBy, string CreatedAt);

/// <summary>Profilni yaratish/tahrirlash. <c>Id</c> bo'sh bo'lsa — yangi profil.</summary>
/// <param name="BaseSalary">Birinchi oklad — profil yaratilayotganda birga kiritiladi
/// (bo'lmasa xodimning oyligi 0 bo'lib chiqardi).</param>
public sealed record SaveKpiProfileRequest(
    string? Id, string UserId, string RoleCode, string StartMonth, string? GuaranteeUntilMonth,
    bool IsActive, string? Note, decimal? BaseSalary, string? SalaryEffectiveFrom);

/// <summary>Yangi oklad versiyasi.</summary>
public sealed record SaveKpiSalaryRequest(string EffectiveFrom, decimal BaseSalary, string? Note);

/// <summary>Qoidalar to'plami — JSON OCHILGAN holda («Qoidalar» sahifasi uni tahrirlaydi).</summary>
public sealed record KpiRuleSetDto(
    string Id, string RoleCode, string RoleLabel, string EffectiveFrom, string? Note,
    string? CreatedBy, string CreatedAt, KpiRuleSetJson Rules);

/// <summary>Qoidalarning YANGI versiyasi (eskisi tahrirlanmaydi — tasdiqlangan oylar buzilmasin).</summary>
public sealed record SaveKpiRuleSetRequest(string RoleCode, string EffectiveFrom, string? Note, KpiRuleSetJson Rules);

/// <summary>Sifat nazorati tiketi.</summary>
public sealed record KpiTicketDto(
    string Id, string UserId, string UserName, string Date, string ReasonCode, string ReasonLabel,
    int? CriterionNo, string? CriterionLabel, string? CallId, string? Note, string Status,
    string? IssuedBy, string IssuedAt, string? DisputeNote, string? ResolvedBy, string? ResolvedAt);

/// <summary>Tiketni qo'yish/tahrirlash.</summary>
public sealed record SaveKpiTicketRequest(
    string? Id, string UserId, string Date, string ReasonCode, int? CriterionNo,
    string? CallId, string? Note, string Status, string? DisputeNote);

/// <summary>Tiket formasi uchun ma'lumotnoma: sabablar, 13 audit mezoni va KPI xodimlari.</summary>
public sealed record KpiTicketMetaDto(
    List<KpiTicketReasonDto> Reasons, List<KpiCriterionDto> Criteria, List<KpiStaffDto> Staff);

/// <summary>Tiket sababi (<c>KpiTicketCatalog</c> — KODDA, <c>ActionReason</c> jadvalida EMAS).</summary>
public sealed record KpiTicketReasonDto(string Code, string Label, string RoleCode, int? CriterionNo);

/// <summary>Yashirin mijoz auditining bitta mezoni (1..13).</summary>
public sealed record KpiCriterionDto(int No, string Label);

/// <summary>
/// KPI xodimi — tiket formasidagi ro'yxat va «profil qo'shish» formasidagi NOMZODLAR uchun.
/// </summary>
/// <param name="RoleCode">Profil hali yo'q bo'lsa BO'SH (nomzodlar ro'yxatida odatdagi holat) —
/// klient bo'sh kodni "roli belgilanmagan" deb ko'rsatadi.</param>
public sealed record KpiStaffDto(string UserId, string UserName, string RoleCode, string RoleLabel);

/// <summary>
/// Haftalik sifat nazorati uchun TASODIFIY qo'ng'iroq namunasi.
/// </summary>
/// <remarks>⚠️ Yozuv FAYLI manzili qaytmaydi (<c>HasRecording</c> — faqat bayroq): fayl
/// avtorizatsiyalangan qo'ng'iroqlar bo'limi orqali tinglanadi
/// (<c>.claude/rules/uploads-security.md</c> printsipi).</remarks>
public sealed record KpiCallSampleDto(
    string CallId, string PhoneNumber, string Direction, string StartedAt, int DurationSeconds,
    bool HasRecording, string? Transcript, string? AiAnalysis);

/// <summary>«Yopish» sahifasi: oyning barcha qatorlari + birlik iqtisodiyoti.</summary>
public sealed record KpiCloseDto(string Month, List<KpiMonthDto> Rows, KpiUnitEconomicsDto? Unit);

/// <summary>
/// BIRLIK IQTISODIYOTI — "bonus markazni yeb qo'ymayaptimi" degan uchta sog'lomlik indikatori.
/// </summary>
/// <param name="MarginPerStudent">Bitta o'quvchining oylik marjasi = narx × (1 − o'qituvchi ulushi).</param>
/// <param name="BonusAShare">Bonus A birligining marjadagi ulushi (norma: <paramref name="BonusAMax"/>).</param>
/// <param name="ExtensionMargin">Uzaytirishning "narxi": marja × o'rtacha davom etish oylari.</param>
/// <param name="SalaryShare">Barcha KPI oyliklarining markaz marjasidagi ulushi.</param>
public sealed record KpiUnitEconomicsDto(
    decimal CoursePrice, double TeacherShare, decimal MarginPerStudent, int ActiveStudents,
    decimal CenterMargin,
    decimal BonusAUnit, double BonusAShare, double BonusAMax, bool BonusAOk,
    decimal BonusBUnit, decimal ExtensionMargin, double BonusBShare, double BonusBMax, bool BonusBOk,
    decimal TotalSalaries, double SalaryShare, double SalaryMax, bool SalaryOk);

/// <summary>Kunlik cheklist SHABLONI (rol bo'yicha) — «Qoidalar» sahifasi uchun.</summary>
public sealed record KpiChecklistTemplateDto(
    string Id, string RoleCode, string RoleLabel, string Name, bool IsActive,
    List<KpiChecklistItemDefDto> Items);

/// <summary>Shablonning bitta bandi (ta'rif — kunlik BELGI emas).</summary>
public sealed record KpiChecklistItemDefDto(
    string Id, int No, string TimeBlock, string Text, string Norm, string? KpiTag,
    int? CriterionNo, string? AutoCheckKey, int Order);

/// <summary>Cheklist bandini belgilash (done | failed | na).</summary>
public sealed record SaveChecklistCheckRequest(string UserId, string Date, string ItemId, string State, string? Note);

/// <summary>
/// Shablon bandini TAHRIRLASH — QISMAN body: berilmagan (null) maydon TEGILMAYDI.
/// </summary>
/// <remarks>⚠️ Barcha maydonlar ixtiyoriy ATAYIN: klient bitta katakni (masalan normani)
/// o'zgartirganda butun bandni qayta yuborishga majbur bo'lmasin — aks holda ekranda ko'rinmagan
/// maydon (masalan <c>AutoCheckKey</c>) jimgina bo'shatib yuborilardi.</remarks>
public sealed record UpdateChecklistItemRequest(
    int? No, string? TimeBlock, string? Text, string? Norm, string? KpiTag,
    int? CriterionNo, string? AutoCheckKey, int? Order);

/// <summary>
/// Oy BOSHIDAGI muzlatilgan raqamlar.
/// </summary>
/// <param name="UserId">Xodim id'si yoki <c>null</c> — MARKAZ darajasidagi surat.
/// ⚠️ Entity'da bu maydon <c>string</c> (bo'sh satr), chunki <c>(Month, RoleCode, UserId)</c>
/// unikal indeksi Postgres'da NULL bilan ishlamaydi (NULL ≠ NULL — takrorlar o'tib ketardi).
/// DTO'da esa "markaz darajasi" ni bo'sh satr emas, <c>null</c> ifodalaydi — klientda
/// <c>userId === null</c> tekshiruvi bo'sh satrdan ko'ra aniqroq. Konversiya
/// <c>KpiController</c> da: chiqishda <c>"" → null</c>, kirishda <c>null → ""</c>.</param>
public sealed record KpiSnapshotDto(string Id, string Month, string RoleCode, string? UserId,
    string TakenAt, Dictionary<string, double> Values);
