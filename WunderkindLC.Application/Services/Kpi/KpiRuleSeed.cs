namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// Excel'dagi KPI konstantalari va pog'ona jadvallarining KODDAGI nusxasi — <b>faqat SEED</b>.
///
/// <para>⚠️ Bu qiymatlar bir marta bazaga <c>KpiRuleSet</c> versiyasi bo'lib yoziladi, keyin
/// «Qoidalar» sahifasidan tahrirlanadi. Ya'ni kod bu yerda YAGONA MANBA EMAS: hisob HAR DOIM
/// bazadagi versiyadan o'qiydi (<see cref="KpiVersioning.RuleFor"/>). Aks holda rahbar
/// koeffitsientni o'zgartirsa-yu, hisob koddagi eski qiymat bilan ishlab tursa — hech kim
/// buni sezmasdi.</para>
/// </summary>
public static class KpiRuleSeed
{
    /// <summary>KPI rollari — UI'da SHU tartibda.</summary>
    public static IReadOnlyList<string> Roles { get; } =
        new[] { KpiConst.Intake, KpiConst.Retention, KpiConst.Operator };

    /// <summary>Rol kodining o'zbekcha yorlig'i. Noma'lum kod — KODNING O'ZI qaytadi.</summary>
    /// <remarks>⚠️ Noma'lum rol uchun bo'sh matn EMAS: jadvalda bo'sh katak "xodimning roli
    /// yo'q" bo'lib ko'rinardi, holbuki muammo — yorliq xaritasi eskirgani.</remarks>
    public static string RoleLabel(string roleCode) => roleCode switch
    {
        KpiConst.Intake => "Kiruvchi admin",
        KpiConst.Retention => "Chiquvchi admin",
        KpiConst.Operator => "Call operator",
        _ => roleCode,
    };

    /// <summary>Samaradorlik pog'onalari — KIRUVCHI (eng yuqori qadam 1.1).</summary>
    private static List<KpiTier> IntakeEfficiency() =>
    [
        new(0.00, 0.7999, 0.75, "Intizom yo'q."),
        new(0.80, 0.8999, 0.90, "Yetarli emas."),
        new(0.90, 0.9499, 1.00, "NORMA."),
        new(0.95, 1.00,   1.10, "Namunali xodim."),
    ];

    /// <summary>Samaradorlik pog'onalari — CHIQUVCHI.</summary>
    /// <remarks>⚠️ Eng yuqori qadam 1.05 (kiruvchida 1.1) — ATAYIN farqli. Chiquvchining
    /// bonusi butun bazaga ko'paytiriladi, ya'ni bir xil qadam u yerda ancha katta summa
    /// beradi; Excel'da ham shunday.</remarks>
    private static List<KpiTier> RetentionEfficiency() =>
    [
        new(0.00, 0.7999, 0.75, "Intizom yo'q."),
        new(0.80, 0.8999, 0.90, "Yetarli emas."),
        new(0.90, 0.9499, 1.00, "NORMA."),
        new(0.95, 1.00,   1.05, "Namunali xodim."),
    ];

    /// <summary>Lid → sinovga KELISH konversiyasi (kiruvchi va operator uchun bir xil).</summary>
    private static List<KpiTier> Conversion() =>
    [
        new(0.00, 0.2499, 0.5, "Normadan keskin past. Bonus yarmiga kesiladi."),
        new(0.25, 0.2999, 0.8, "Minimal chegara. Bonus to'liq emas."),
        new(0.30, 0.3599, 1.0, "NORMA. Bonus to'liq."),
        new(0.36, 0.4299, 1.2, "Normadan yuqori. Bonus 20% ko'p."),
        new(0.43, 1.00,   1.4, "Zo'r natija. Bonus 40% ko'p."),
    ];

    /// <summary>SEED — <c>intake_admin</c> (Excel «KPI Kiruvchi Admin» → «KPI qoidalari»).</summary>
    public static KpiRuleSetJson Intake() => new()
    {
        ContractBase = 35_000m,
        TicketFine = 50_000m,
        Guarantee = 4_500_000m,
        // ⚠️ Kiruvchida chegara YO'Q: shartnoma soni markazga to'g'ridan-to'g'ri tushum
        // keltiradi, ya'ni "juda ko'p ishlab qo'yish" degan muammo bo'lmaydi.
        MonthlyCap = null,

        QualityNorm = 0.80,
        QualityPenalty = -0.20,
        QualitySevereBelow = 0.70,
        QualitySeverePenalty = -0.40,

        LeadFloor = 250,
        PlanLeadToTrial = 0.32,
        PlanTrialToContract = 0.80,
        WorkDays = 26,
        TouchesPerLead = 3,
        MonthlyContractPlan = 90,
        TicketStepThreshold = 5,
        TicketStepPenalty = -0.20,

        Conversion = Conversion(),
        Efficiency = IntakeEfficiency(),

        // ⚠️ Birlik iqtisodiyoti — MARKAZ darajasidagi konstantalar, ya'ni har uchala rol
        // seed'ida bir xil. Bitta rolda bo'sh (0) qoldirilsa, "Yopish" sahifasidagi
        // sog'lomlik indikatori nolga bo'lishga yiqilardi.
        UnitCoursePrice = 500_000m,
        UnitTeacherShare = 0.55,
        UnitExtensionMonths = 4,
        UnitMaxBonusAShare = 0.03,
        UnitMaxBonusBShare = 0.05,
        UnitMaxSalaryShare = 0.10,
    };

    /// <summary>SEED — <c>retention_admin</c> (Excel «KPI Chiquvchi Admin» → «KPI qoidalari»).</summary>
    public static KpiRuleSetJson Retention() => new()
    {
        ActiveStudentBonus = 1_000m,
        ExtensionBonus = 35_000m,
        DebtRate = 0.01,
        DebtRateNorm = 0.80,
        UnknownReasonFine = 50_000m,
        TicketFine = 50_000m,
        Guarantee = 4_500_000m,
        // ⚠️ Chiquvchida chegara BOR: bonus butun bazaga ko'paytirilgani uchun baza o'sishi
        // bilan summa cheksiz o'sib ketardi. Chegara summani KESMAYDI — rahbar qarorini
        // so'raydi (KpiCalculator.CapExceeded).
        MonthlyCap = 7_500_000m,

        Retention =
        [
            new(0.00, 0.0399, 1.15, "Ajoyib. Baza deyarli to'liq saqlangan."),
            new(0.04, 0.0599, 1.10, "Normadan yaxshi."),
            new(0.06, 0.0799, 1.00, "NORMA. Bonus to'liq."),
            new(0.08, 0.0999, 0.80, "Normadan yomon."),
            new(0.10, 0.1199, 0.60, "Jiddiy muammo."),
            new(0.12, 1.00,   0.40, "Baza oqib ketyapti."),
        ],
        Extension =
        [
            new(0.00, 0.5499, 0.50, "Normadan keskin past."),
            new(0.55, 0.6499, 0.80, "Minimal chegara."),
            new(0.65, 0.7599, 1.00, "NORMA. Bonus to'liq."),
            new(0.76, 0.8599, 1.15, "Normadan yuqori."),
            new(0.86, 1.00,   1.25, "Zo'r natija."),
        ],
        Efficiency = RetentionEfficiency(),

        UnitCoursePrice = 500_000m,
        UnitTeacherShare = 0.55,
        UnitExtensionMonths = 4,
        UnitMaxBonusAShare = 0.03,
        UnitMaxBonusBShare = 0.05,
        UnitMaxSalaryShare = 0.10,
    };

    /// <summary>
    /// SEED — <c>call_operator</c>: kiruvchining NUSXASI, farqi faqat
    /// <see cref="KpiRuleSetJson.ContractBase"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Operator uchun birlik — SHARTNOMA emas, sinovga KELGAN odam (u shartnomani
    /// yopmaydi). Shuning uchun bir birlikning narxi pastroq: 25 000. Qolgan qoidalar bir xil
    /// bo'lgani uchun nusxa <c>with</c> bilan olinadi — jadvallar ikki joyda ayri
    /// tahrirlanib ketmasin.
    /// </remarks>
    public static KpiRuleSetJson CallOperator() => Intake() with { ContractBase = 25_000m };

    /// <summary>Rol kodi bo'yicha seed. Noma'lum kod — kiruvchi (eng keng tarqalgan rol).</summary>
    public static KpiRuleSetJson For(string roleCode) => roleCode switch
    {
        KpiConst.Retention => Retention(),
        KpiConst.Operator => CallOperator(),
        _ => Intake(),
    };
}
