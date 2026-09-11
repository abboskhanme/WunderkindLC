namespace WunderkindLC.Application.Services.Kpi;

/* =====================  KIRISH va NATIJA recordlari  ===================== */

/// <summary>Kiruvchi adminning bir oydagi KIRISH raqamlari (barchasi <c>KpiMetricsService</c> dan).</summary>
/// <param name="Leads">Oy ichida shu adminga biriktirilgan lidlar.</param>
/// <param name="TrialCame">Sinov darsiga HAQIQATAN kelganlar (yozilganlar emas).</param>
/// <param name="Contracts">Shu admin yopgan shartnomalar.</param>
/// <param name="Efficiency">Kunlik cheklist bajarilishi, 0..1.</param>
/// <param name="Tickets">TASDIQLANGAN tiketlar soni.</param>
public sealed record KpiIntakeInputs(int Leads, int TrialCame, int Contracts, double Efficiency, int Tickets);

/// <summary>Kiruvchi adminning oylik hisobi — har oraliq qiymat ham qaytadi (UI "nega shuncha" ni ko'rsatadi).</summary>
public sealed record KpiIntakeResult(
    double ConvLeadToTrial, double ConvTrialToDeal, double ConvLeadToDeal,
    double TierCoef, double QualityPenalty, double TicketPenalty, double ConvCoef, double EffCoef,
    decimal BaseSalary, decimal Bonus, decimal TicketFine, decimal Computed, decimal Salary,
    bool GuaranteeApplied, bool CapExceeded,
    int PlanContracts, double PlanDone, bool LeadFloorApplied,
    KpiTier? ConvTier, KpiTier? EffTier);

/// <summary>Chiquvchi adminning bir oydagi KIRISH raqamlari.</summary>
/// <param name="ActiveStart">Oy BOSHIDAGI faol o'quvchilar (snapshot — orqaga tiklab bo'lmaydi).</param>
/// <param name="LeftControlled">Oy ichida ketganlar, sababi NAZORAT OSTIDA bo'lganlari.</param>
/// <param name="ActiveEnd">Oy OXIRIDAGI faol o'quvchilar (<see cref="KpiActiveStudentRule"/>).</param>
/// <param name="CourseFinished">Kursi shu oyda tugaganlar.</param>
/// <param name="Extended">Ulardan keyingi bosqichga o'tganlar.</param>
/// <param name="DebtStart">Oy boshidagi umumiy qarz.</param>
/// <param name="DebtCollected">Oy ichida yig'ilgan qarz.</param>
/// <param name="Efficiency">Kunlik cheklist bajarilishi, 0..1.</param>
/// <param name="UnknownReasonLeft">Ketish SABABI yozilmaganlar soni.</param>
/// <param name="Tickets">TASDIQLANGAN tiketlar soni.</param>
public sealed record KpiRetentionInputs(
    int ActiveStart, int LeftControlled, int ActiveEnd, int CourseFinished, int Extended,
    decimal DebtStart, decimal DebtCollected, double Efficiency, int UnknownReasonLeft, int Tickets);

/// <summary>Chiquvchi adminning oylik hisobi.</summary>
public sealed record KpiRetentionResult(
    double ChurnRate, double ExtRate, double DebtRateFact,
    double RetentionCoef, double ExtensionCoef, double EffCoef, double DebtPercent,
    decimal BaseSalary, decimal BonusA, decimal BonusB, decimal BonusC, decimal BonusTotal,
    decimal FineUnknown, decimal FineTickets, decimal Computed, decimal Salary,
    bool GuaranteeApplied, bool CapExceeded,
    KpiTier? RetentionTier, KpiTier? ExtensionTier, KpiTier? EffTier);

/* =============================  HISOB  ============================= */

/// <summary>
/// KPI oylik hisobi — Excel bilan SO'MGACHA bir xil (sof funksiyalar, testlangan:
/// <c>KpiCalculatorTests</c>).
///
/// <para>Bu sinf hech narsa O'QIMAYDI va hech narsa SAQLAMAYDI: kirish raqamlari
/// (<c>KpiMetricsService</c>), qoidalar (<c>KpiRuleSet.Json</c>) va oklad
/// (<c>KpiProfileSalary</c>) tashqaridan beriladi. Shu sababdan "oy jonli hisoblanmoqda",
/// "oy yopilmoqda" va TEST — uchalasi AYNAN bir xil funksiyani chaqiradi, ya'ni yopishdan
/// keyin summa "birdan boshqacha" bo'lib qolmaydi.</para>
/// </summary>
public static class KpiCalculator
{
    /// <summary>Pul qatori so'mgacha yaxlitlanadi: so'mning kasri ma'nosiz (kassada ham yo'q).</summary>
    /// <remarks>
    /// ⚠️ <c>AwayFromZero</c> ATAYIN — .NET ning standarti <c>ToEven</c> (bankir yaxlitlashi),
    /// ya'ni <c>0.5</c> JUFT tomonga qarab yaxlitlanadi: 500.5 → 500, lekin 501.5 → 502.
    /// Excel esa har doim "yarmi va undan yuqorisi — yuqoriga" qoidasi bilan ishlaydi. Farq
    /// bir so'm bo'lsa ham, oylik hisobot Excel bilan solishtiriladi va bir so'mlik farq
    /// "hisob noto'g'ri" degan savolni tug'dirardi.
    /// </remarks>
    public static decimal Som(decimal x) => Math.Round(x, 0, MidpointRounding.AwayFromZero);

    /// <summary>Xavfsiz bo'lish: maxraj 0 bo'lsa 0 (NaN/Infinity JSON'ga chiqib, klientni buzardi).</summary>
    private static double Div(double a, double b) => b == 0 ? 0 : a / b;

    /// <summary>
    /// KIRUVCHI admin (va call operator) uchun oylik hisob.
    /// </summary>
    /// <param name="i">Oylik kirish raqamlari.</param>
    /// <param name="r">Shu oyda AMAL QILADIGAN qoidalar (<see cref="KpiVersioning.RuleFor"/>).</param>
    /// <param name="baseSalary">Shu oyda amal qiladigan shaxsiy oklad.</param>
    /// <param name="guarantee">Kafolat muddati hali tugamaganmi (<c>KpiProfile.GuaranteeUntilMonth</c>).</param>
    public static KpiIntakeResult Intake(KpiIntakeInputs i, KpiRuleSetJson r, decimal baseSalary, bool guarantee)
    {
        var convLeadToTrial = Div(i.TrialCame, i.Leads);
        var convTrialToDeal = Div(i.Contracts, i.TrialCame);
        var convLeadToDeal = Div(i.Contracts, i.Leads);

        var convTier = KpiRules.TierOf(r.Conversion, convLeadToTrial);
        var tierCoef = convTier?.Coef ?? 1.0;

        // ⚠️ SIFAT jarimasi — modulning MA'NOSI. "Kelgan → shartnoma" normadan past bo'lsa,
        // shartnomalar soni ko'p bo'lsa ham koeffitsient pasayadi: ya'ni ko'p mehmonni qo'ldan
        // chiqargan admin kamroq oladi. Busiz KPI "lidni ko'proq quv" ga aylanib, sifatni
        // butunlay yo'q qilardi.
        var qualityPenalty =
            convTrialToDeal < r.QualitySevereBelow ? r.QualitySeverePenalty :
            convTrialToDeal < r.QualityNorm ? r.QualityPenalty : 0;

        // ⚠️ Chegara 0 bo'lsa tekshiruv QO'LLANMAYDI: aks holda "tiket 0 >= chegara 0" rost
        // bo'lib, hech qanday tiketi yo'q xodim ham jarimaga tushardi (qoidalar sahifasida
        // maydon bo'sh qoldirilsa aynan shunday bo'lardi).
        var ticketPenalty = r.TicketStepThreshold > 0 && i.Tickets >= r.TicketStepThreshold
            ? r.TicketStepPenalty : 0;

        // ⚠️ Manfiy koeffitsient bo'lmaydi: bonus MANFIY bo'lib, oklad hisobidan pul yechib
        // olardi. Jarima — alohida qator (tiket ushlanmasi), koeffitsient esa faqat bonusni
        // kamaytiradi (nolgacha).
        var convCoef = Math.Max(0, tierCoef + qualityPenalty + ticketPenalty);

        var effTier = KpiRules.TierOf(r.Efficiency, i.Efficiency);
        var effCoef = effTier?.Coef ?? 1.0;

        var bonus = Som(i.Contracts * r.ContractBase * (decimal)convCoef * (decimal)effCoef);
        var ticketFine = Som(i.Tickets * r.TicketFine);
        var computed = Som(baseSalary + bonus - ticketFine);

        var guaranteeApplied = guarantee && computed < r.Guarantee;
        var salary = guaranteeApplied ? r.Guarantee : computed;
        var capExceeded = r.MonthlyCap is decimal cap && salary > cap;

        // 5-QADAM nazorati: reja bilan solishtirish.
        // ⚠️ Lid oqimi kafolati — xodimni MARKETINGNING kam ishlagani uchun jazolamaslik uchun:
        // lid kelmasa shartnoma ham bo'lmaydi, lekin bu adminning aybi emas. Shu holatda reja
        // haqiqiy lid oqimidan qayta hisoblanadi.
        var leadFloorApplied = i.Leads < r.LeadFloor;
        var planContracts = leadFloorApplied
            ? (int)Math.Round(i.Leads * r.PlanLeadToTrial * r.PlanTrialToContract, MidpointRounding.AwayFromZero)
            : r.MonthlyContractPlan;
        var planDone = Div(i.Contracts, planContracts);

        return new KpiIntakeResult(
            convLeadToTrial, convTrialToDeal, convLeadToDeal,
            tierCoef, qualityPenalty, ticketPenalty, convCoef, effCoef,
            baseSalary, bonus, ticketFine, computed, salary,
            guaranteeApplied, capExceeded,
            planContracts, planDone, leadFloorApplied,
            convTier, effTier);
    }

    /// <summary>
    /// CHIQUVCHI admin uchun oylik hisob: A (ushlab qolish) + B (uzaytirish) + C (qarz yig'ish).
    /// </summary>
    public static KpiRetentionResult Retention(KpiRetentionInputs i, KpiRuleSetJson r, decimal baseSalary, bool guarantee)
    {
        var churnRate = Div(i.LeftControlled, i.ActiveStart);
        var extRate = Div(i.Extended, i.CourseFinished);
        var debtRateFact = i.DebtStart == 0 ? 0 : (double)(i.DebtCollected / i.DebtStart);

        var retentionTier = KpiRules.TierOf(r.Retention, churnRate);
        var extensionTier = KpiRules.TierOf(r.Extension, extRate);
        var effTier = KpiRules.TierOf(r.Efficiency, i.Efficiency);

        var retentionCoef = retentionTier?.Coef ?? 1.0;
        var extensionCoef = extensionTier?.Coef ?? 1.0;
        var effCoef = effTier?.Coef ?? 1.0;

        // ⚠️ Yig'ish normasi bajarilmasa ulush YARMIGA tushadi — "eng oson qarzni yig'ib,
        // qiyinini qoldirish" foydali bo'lib qolmasin.
        // ⚠️ Oy boshida qarz UMUMAN bo'lmasa (DebtStart == 0) fakt 0 bo'ladi va ulush yarmiga
        // tushadi. Bu ATAYIN spetsifikatsiyadagidek: "yig'ish normasi" o'lchanmagan oyda to'liq
        // ulush berilsa, oy boshida qarzni JIMGINA nolga tushirish (masalan hisobni keyinroq
        // yozish) foydali bo'lib qolardi.
        var debtPercent = debtRateFact >= r.DebtRateNorm ? r.DebtRate : r.DebtRate / 2;

        var bonusA = Som(i.ActiveEnd * r.ActiveStudentBonus * (decimal)retentionCoef);
        var bonusB = Som(i.Extended * r.ExtensionBonus * (decimal)extensionCoef);
        var bonusC = Som(i.DebtCollected * (decimal)debtPercent);

        // ⚠️ Samaradorlik koeffitsienti UCHALA bonusga BIRDAN qo'llanadi (alohida-alohida emas):
        // Excel'da ham shunday va faqat shu holda "intizom" butun bonusning narxi bo'lib qoladi.
        var bonusTotal = Som((bonusA + bonusB + bonusC) * (decimal)effCoef);

        var fineUnknown = Som(i.UnknownReasonLeft * r.UnknownReasonFine);
        var fineTickets = Som(i.Tickets * r.TicketFine);
        var computed = Som(baseSalary + bonusTotal - fineUnknown - fineTickets);

        var guaranteeApplied = guarantee && computed < r.Guarantee;
        var salary = guaranteeApplied ? r.Guarantee : computed;

        // ⚠️ Chegara summani KESMAYDI — faqat bayroq. Kesilsa, xodim "nega kam oldim" degan
        // savolga javob topolmasdi; endi esa rahbar Yopish sahifasida ALOHIDA qaror qabul qiladi.
        var capExceeded = r.MonthlyCap is decimal cap && salary > cap;

        return new KpiRetentionResult(
            churnRate, extRate, debtRateFact,
            retentionCoef, extensionCoef, effCoef, debtPercent,
            baseSalary, bonusA, bonusB, bonusC, bonusTotal,
            fineUnknown, fineTickets, computed, salary,
            guaranteeApplied, capExceeded,
            retentionTier, extensionTier, effTier);
    }
}
