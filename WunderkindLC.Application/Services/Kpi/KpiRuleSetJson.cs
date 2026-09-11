namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// Bitta POG'ONA: <c>[From, To]</c> oralig'iga tushgan qiymat uchun koeffitsient.
/// </summary>
/// <param name="From">Oraliqning boshi — KIRADI (<c>From &lt;= x</c>).</param>
/// <param name="To">Oraliqning oxiri — faqat KO'RSATISH uchun (Qoidalar sahifasidagi jadval).</param>
/// <param name="Coef">Bonusga ko'paytiriladigan koeffitsient.</param>
/// <param name="Note">Nima uchun shu koeffitsient — xodimga ko'rsatiladigan izoh
/// ("NORMA. Bonus to'liq.", "Baza oqib ketyapti."). Raqamning O'ZI hech narsani tushuntirmaydi.</param>
/// <remarks>
/// ⚠️ Tanlash faqat <see cref="From"/> bo'yicha (Excel <c>MATCH(...,1)</c> naqshi), <see cref="To"/>
/// TEKSHIRILMAYDI. Sabab: jadvalda oraliqlar tutashib ketadi (0.2499 → 0.25) va <c>To</c> ni ham
/// tekshirsak, oradagi 0.24995 kabi qiymat HECH QAYSI pog'onaga tushmasdan qolib ketardi.
/// Ya'ni <c>To</c> — hujjat, qoida emas.
/// </remarks>
public sealed record KpiTier(double From, double To, double Coef, string? Note = null);

/// <summary>
/// Bir ROL uchun KPI qoidalarining to'liq to'plami — <c>KpiRuleSet.Json</c> ichida saqlanadi.
///
/// <para>Nega JSON: konstantalar ham, pog'ona JADVALLARI ham vaqt o'tishi bilan o'zgaradi va
/// har o'zgarish YANGI VERSIYA bo'lib yoziladi (<c>EffectiveFrom</c>). Ustunlar bo'lsa, har yangi
/// koeffitsient uchun migratsiya kerak bo'lardi; yopilgan oyning raqamlari esa o'sha paytdagi
/// qoidalar bilan qayta hisoblanishi SHART (<see cref="KpiVersioning"/>).</para>
/// </summary>
public sealed record KpiRuleSetJson
{
    /* ---------- umumiy (barcha rollar) ---------- */

    /// <summary>Bitta tasdiqlangan tiket uchun ushlanma (so'm).</summary>
    public decimal TicketFine { get; init; }

    /// <summary>Kafolatlangan minimal oylik — sinov davrida "past oy" xodimni qo'rqitmasin.</summary>
    /// <remarks>⚠️ Kafolat HAR DOIM emas: u faqat <c>KpiProfile.GuaranteeUntilMonth</c> gacha
    /// ishlaydi. Aks holda pastroq natijaning "narxi" nolga tushib, butun modul ma'nosini
    /// yo'qotardi.</remarks>
    public decimal Guarantee { get; init; }

    /// <summary>Oylik "shift" — bundan oshgani rahbarning ALOHIDA qarorini talab qiladi.</summary>
    /// <remarks>⚠️ Bu chegara summani KESMAYDI (<see cref="KpiCalculator"/> faqat
    /// <c>CapExceeded</c> bayrog'ini qo'yadi). Jimgina kesilsa, xodim "nega kam oldim" degan
    /// savolga javob topolmasdi va zo'r oy jazoga aylanardi.</remarks>
    public decimal? MonthlyCap { get; init; }

    /// <summary>Samaradorlik (kunlik cheklist bajarilishi) → koeffitsient.</summary>
    public List<KpiTier> Efficiency { get; init; } = [];

    /* ---------- intake_admin / call_operator ---------- */

    /// <summary>Bitta shartnoma uchun asosiy bonus (operatorda — sinovga KELGAN har bir odam uchun).</summary>
    public decimal ContractBase { get; init; }

    /// <summary>Sinovga kelgan → shartnoma NORMASI. Bundan past bo'lsa bonus koeffitsienti pasayadi.</summary>
    /// <remarks>⚠️ Modulning MA'NOSI shu yerda: ko'p shartnoma qilgan, lekin kelganlarning
    /// yarmini qo'ldan chiqargan admin kamroq oladi — aks holda "ko'proq lid quving" mantiqi
    /// sifatni butunlay yo'q qilardi.</remarks>
    public double QualityNorm { get; init; }
    /// <summary>Normadan past sifat uchun koeffitsientga QO'SHILADIGAN manfiy qadam.</summary>
    public double QualityPenalty { get; init; }
    /// <summary>Shundan ham past bo'lsa — qattiq jarima chegarasi.</summary>
    public double QualitySevereBelow { get; init; }
    /// <summary>Qattiq jarima qadami.</summary>
    public double QualitySeverePenalty { get; init; }

    /// <summary>Lid oqimi KAFOLATI: markaz shuncha lid bermasa, oylik reja lidlar soniga moslashadi.</summary>
    /// <remarks>⚠️ Bu xodimni MARKETINGNING kam ishlagani uchun jazolamaslik uchun: lid kelmasa
    /// shartnoma ham bo'lmaydi, ammo bu adminning aybi emas.</remarks>
    public int LeadFloor { get; init; }
    /// <summary>Reja: lidning qanchasi sinovga KELADI.</summary>
    public double PlanLeadToTrial { get; init; }
    /// <summary>Reja: kelganning qanchasi SHARTNOMA bo'ladi.</summary>
    public double PlanTrialToContract { get; init; }
    /// <summary>Oydagi ish kunlari (kunlik norma shundan chiqadi).</summary>
    public int WorkDays { get; init; }
    /// <summary>Bitta lidga o'rtacha nechta "teginish" (qo'ng'iroq/xabar) rejalashtiriladi.</summary>
    public double TouchesPerLead { get; init; }
    /// <summary>Oylik shartnoma rejasi (lid oqimi yetarli bo'lganda).</summary>
    public int MonthlyContractPlan { get; init; }
    /// <summary>Shuncha tiketdan boshlab koeffitsientga qo'shimcha manfiy qadam qo'shiladi.</summary>
    public int TicketStepThreshold { get; init; }
    /// <summary>O'sha qadam.</summary>
    public double TicketStepPenalty { get; init; }
    /// <summary>Lid → sinovga KELISH konversiyasi → koeffitsient.</summary>
    public List<KpiTier> Conversion { get; init; } = [];

    /* ---------- retention_admin ---------- */

    /// <summary>Oy oxiridagi HAR FAOL o'quvchi uchun bonus (Bonus A birligi).</summary>
    /// <remarks>⚠️ Spetsifikatsiya §9: Excel "Boshlash" varag'i 6 000 deydi, BARCHA formulalar
    /// esa 1 000 bilan hisoblaydi. Seed formula qiymati bilan — u bilan NORMA oyi roppa-rosa
    /// kafolatga teng chiqadi. Qiymat "Qoidalar" sahifasidan tahrirlanadi.</remarks>
    public decimal ActiveStudentBonus { get; init; }
    /// <summary>Kursni tugatib KEYINGI bosqichga o'tgan har o'quvchi uchun bonus (Bonus B birligi).</summary>
    public decimal ExtensionBonus { get; init; }
    /// <summary>Yig'ilgan qarzdan ulush (Bonus C).</summary>
    public double DebtRate { get; init; }
    /// <summary>Yig'ish normasi — bundan past bo'lsa ulush YARMIGA tushadi.</summary>
    public double DebtRateNorm { get; init; }
    /// <summary>Ketish sababi yozilmagan HAR o'quvchi uchun jarima.</summary>
    /// <remarks>⚠️ Sabab — modulning butun ma'nosi: sababsiz ketish "nima tuzatish kerak"
    /// degan savolni javobsiz qoldiradi, ya'ni hisobot bor, xulosa yo'q.</remarks>
    public decimal UnknownReasonFine { get; init; }
    /// <summary>Oylik KETISH foizi → koeffitsient (kam ketgan — yuqori koeffitsient).</summary>
    public List<KpiTier> Retention { get; init; } = [];
    /// <summary>Kursi tugaganlarning qanchasi davom etdi → koeffitsient.</summary>
    public List<KpiTier> Extension { get; init; } = [];

    /* ---------- birlik iqtisodiyoti (Yopish sahifasidagi 3 sog'lomlik indikatori) ---------- */

    /// <summary>O'rtacha kurs narxi (oylik).</summary>
    public decimal UnitCoursePrice { get; init; }
    /// <summary>O'qituvchining ulushi.</summary>
    public double UnitTeacherShare { get; init; }
    /// <summary>Uzaytirish o'rtacha necha oy davom etadi (Bonus B ning "narxi" shundan).</summary>
    public int UnitExtensionMonths { get; init; }
    /// <summary>Bonus A marjadagi maksimal ulushi.</summary>
    public double UnitMaxBonusAShare { get; init; }
    /// <summary>Bonus B marjadagi maksimal ulushi.</summary>
    public double UnitMaxBonusBShare { get; init; }
    /// <summary>Barcha KPI oyliklarining markaz tushumidagi maksimal ulushi.</summary>
    public double UnitMaxSalaryShare { get; init; }
}
