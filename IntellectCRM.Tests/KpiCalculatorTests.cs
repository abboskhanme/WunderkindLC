using IntellectCRM.Application.Services.Kpi;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// KPI oylik hisobi — Excel «Ssenariylar» varag'ining SO'MGACHA qulfi.
///
/// <para>Bu testlar shunchaki "kod ishlayaptimi" degan savolga emas, <b>"xodim bilan
/// kelishilgan raqam o'zgarib ketmadimi"</b> degan savolga javob beradi: koeffitsient
/// jadvalidagi bitta qator yoki yaxlitlash rejimi o'zgarsa, oylik summa jimgina siljib
/// ketardi va buni faqat xodim oyning oxirida sezardi.</para>
/// </summary>
public class KpiCalculatorTests
{
    private static readonly KpiRuleSetJson IntakeRules = KpiRuleSeed.Intake();
    private static readonly KpiRuleSetJson RetentionRules = KpiRuleSeed.Retention();

    private const decimal IntakeSalary = 2_500_000m;
    private const decimal RetentionSalary = 3_000_000m;

    /* ==========================  KIRUVCHI ADMIN  ========================== */

    /// <summary>
    /// Excel «Ssenariylar» varag'idagi 6 qator (oklad 2 500 000, kafolat YO'Q).
    /// </summary>
    [Theory]
    [InlineData("Yomon oy",                   240,  62,  50, 0.88, 2, 0.8, 0.9, 1_260_000, 3_660_000)]
    [InlineData("Plandi pasti",               300,  96,  78, 0.92, 1, 1.0, 1.0, 2_730_000, 5_180_000)]
    [InlineData("NORMA",                      300,  99,  80, 0.93, 0, 1.0, 1.0, 2_800_000, 5_300_000)]
    [InlineData("Plan to'liq",                350, 119,  96, 0.95, 0, 1.0, 1.1, 3_696_000, 6_196_000)]
    [InlineData("Kuchli oy",                  330, 125, 100, 0.96, 0, 1.2, 1.1, 4_620_000, 7_120_000)]
    [InlineData("Shartnoma ko'p, SIFAT past", 340, 145,  94, 0.90, 3, 0.8, 1.0, 2_632_000, 4_982_000)]
    public void Kiruvchi_ssenariylari_Excel_bilan_SOMGACHA_bir_xil(
        string nom, int leads, int came, int contracts, double eff, int tickets,
        double convCoef, double effCoef, double bonus, double jami)
    {
        var r = KpiCalculator.Intake(new KpiIntakeInputs(leads, came, contracts, eff, tickets),
                                     IntakeRules, IntakeSalary, guarantee: false);

        Assert.Equal(convCoef, r.ConvCoef, 6);
        Assert.Equal(effCoef, r.EffCoef, 6);
        Assert.Equal((decimal)bonus, r.Bonus);
        Assert.Equal((decimal)jami, r.Salary);
        Assert.False(r.CapExceeded, nom);          // kiruvchida chegara YO'Q
    }

    [Fact]
    public void MODULNING_MANOSI_kop_shartnoma_qilgan_lekin_SIFATSIZ_admin_KAM_oladi()
    {
        // 94 shartnoma — lekin kelgan→shartnoma 65% (norma 80%) va 3 tiket.
        var yomon = KpiCalculator.Intake(new KpiIntakeInputs(340, 145, 94, 0.90, 3), IntakeRules, IntakeSalary, false);
        // 80 shartnoma — lekin sifat normada va tiket yo'q.
        var yaxshi = KpiCalculator.Intake(new KpiIntakeInputs(300, 99, 80, 0.93, 0), IntakeRules, IntakeSalary, false);

        Assert.True(yomon.Salary < yaxshi.Salary);
        Assert.Equal(-0.40, yomon.QualityPenalty);          // 0.648 < 0.70 → QATTIQ jarima
        Assert.Equal(0.0, yaxshi.QualityPenalty);
    }

    [Fact]
    public void KIRUVCHI_KALKULYATOR_qatori_toliq_yol()
    {
        var r = KpiCalculator.Intake(new KpiIntakeInputs(280, 92, 74, 0.93, 1), IntakeRules, IntakeSalary, false);

        Assert.Equal(0.3286, r.ConvLeadToTrial, 4);
        Assert.Equal(0.8043, r.ConvTrialToDeal, 4);
        Assert.Equal(1.0, r.TierCoef);
        Assert.Equal(0.0, r.QualityPenalty);                // 80.43% ≥ 80% → jarima yo'q
        Assert.Equal(0.0, r.TicketPenalty);                 // 1 tiket < 5 → qadam yo'q
        Assert.Equal(2_590_000m, r.Bonus);
        Assert.Equal(50_000m, r.TicketFine);
        Assert.Equal(5_040_000m, r.Salary);

        // ⚠️ Lid oqimi kafolati ISHLAMAYDI (280 ≥ 250) → reja o'zgarmas 90 ta.
        Assert.False(r.LeadFloorApplied);
        Assert.Equal(90, r.PlanContracts);
        Assert.Equal(0.8222, r.PlanDone, 4);
    }

    [Fact]
    public void LID_OQIMI_kafolati_markazning_aybi_uchun_xodimni_jazolamaydi()
    {
        // 240 lid < 250 → reja haqiqiy lid oqimidan qayta hisoblanadi: 240 × 0.32 × 0.80 = 61.44 → 61.
        var r = KpiCalculator.Intake(new KpiIntakeInputs(240, 62, 50, 0.88, 2), IntakeRules, IntakeSalary, false);

        Assert.True(r.LeadFloorApplied);
        Assert.Equal(61, r.PlanContracts);
        Assert.Equal(50d / 61d, r.PlanDone, 6);
    }

    [Fact]
    public void TIKET_QADAMI_faqat_chegaradan_boshlab_qollanadi()
    {
        var tort = KpiCalculator.Intake(new KpiIntakeInputs(300, 99, 80, 0.93, 4), IntakeRules, IntakeSalary, false);
        var besh = KpiCalculator.Intake(new KpiIntakeInputs(300, 99, 80, 0.93, 5), IntakeRules, IntakeSalary, false);

        Assert.Equal(0.0, tort.TicketPenalty);
        Assert.Equal(2_800_000m, tort.Bonus);
        Assert.Equal(5_100_000m, tort.Salary);              // 2 500 000 + 2 800 000 − 200 000

        Assert.Equal(-0.20, besh.TicketPenalty);
        Assert.Equal(0.8, besh.ConvCoef, 6);
        Assert.Equal(2_240_000m, besh.Bonus);
        Assert.Equal(4_490_000m, besh.Salary);              // 2 500 000 + 2 240 000 − 250 000
    }

    [Fact]
    public void Koeffitsient_MANFIY_bolmaydi_bonus_okladdan_pul_yechmaydi()
    {
        // Eng past pog'ona 0.5, qattiq sifat jarimasi −0.4, tiket qadami −0.2 → −0.1 chiqardi.
        var r = KpiCalculator.Intake(new KpiIntakeInputs(1000, 100, 40, 0.5, 9), IntakeRules, IntakeSalary, false);

        Assert.Equal(0.0, r.ConvCoef);
        Assert.Equal(0m, r.Bonus);
        // Jarima ALOHIDA qator bo'lib qoladi — u yo'qolmaydi.
        Assert.Equal(450_000m, r.TicketFine);
        Assert.Equal(2_050_000m, r.Salary);
    }

    [Fact]
    public void Nol_lid_va_nol_sinovda_hisob_NaN_bermaydi()
    {
        var r = KpiCalculator.Intake(new KpiIntakeInputs(0, 0, 0, 0, 0), IntakeRules, IntakeSalary, false);

        Assert.Equal(0.0, r.ConvLeadToTrial);
        Assert.Equal(0.0, r.ConvTrialToDeal);
        Assert.Equal(0.0, r.ConvLeadToDeal);
        Assert.Equal(0m, r.Bonus);
        Assert.Equal(0, r.PlanContracts);
        Assert.Equal(0.0, r.PlanDone);                      // reja 0 → bo'lish YO'Q
        Assert.Equal(IntakeSalary, r.Salary);
    }

    /* ==========================  CHIQUVCHI ADMIN  ========================== */

    /// <summary>
    /// Excel «Ssenariylar» varag'idagi 6 qator (oklad 3 000 000, kafolat va jarimasiz).
    /// Har qatorda <c>debtStart == debtCollected</c>, ya'ni yig'ish 100% → ulush to'liq 1%.
    /// </summary>
    [Theory]
    [InlineData("Zaif oy",                   320, 42, 300, 38, 19,  9_000_000, 0.88, 0.40, 0.50, 0.90,   120_000,   332_500,  90_000, 3_488_250)]
    [InlineData("Normadan past",             320, 30, 315, 40, 25, 13_000_000, 0.91, 0.80, 0.80, 1.00,   252_000,   700_000, 130_000, 4_082_000)]
    [InlineData("NORMA",                     320, 22, 330, 40, 29, 15_500_000, 0.93, 1.00, 1.00, 1.00,   330_000, 1_015_000, 155_000, 4_500_000)]
    [InlineData("Yaxshi oy",                 325, 17, 340, 44, 35, 17_000_000, 0.95, 1.10, 1.15, 1.05,   374_000, 1_408_750, 170_000, 5_050_388)]
    [InlineData("Kuchli oy",                 500, 30, 440, 44, 38, 70_000_000, 0.96, 1.00, 1.25, 1.05,   440_000, 1_662_500, 700_000, 5_942_625)]
    [InlineData("Uzaytirdi, USHLAB QOLMADI", 330, 46, 320, 44, 38, 16_000_000, 0.92, 0.40, 1.25, 1.00,   128_000, 1_662_500, 160_000, 4_950_500)]
    public void Chiquvchi_ssenariylari_Excel_bilan_SOMGACHA_bir_xil(
        string nom, int start, int left, int end, int finished, int extended, double debt, double eff,
        double retCoef, double extCoef, double effCoef,
        double bonusA, double bonusB, double bonusC, double jami)
    {
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(start, left, end, finished, extended,
                                   DebtStart: (decimal)debt, DebtCollected: (decimal)debt,
                                   Efficiency: eff, UnknownReasonLeft: 0, Tickets: 0),
            RetentionRules, RetentionSalary, guarantee: false);

        Assert.Equal(retCoef, r.RetentionCoef, 6);
        Assert.Equal(extCoef, r.ExtensionCoef, 6);
        Assert.Equal(effCoef, r.EffCoef, 6);
        Assert.Equal((decimal)bonusA, r.BonusA);
        Assert.Equal((decimal)bonusB, r.BonusB);
        Assert.Equal((decimal)bonusC, r.BonusC);
        Assert.Equal((decimal)jami, r.Salary);
        Assert.False(r.CapExceeded, nom);
    }

    [Fact]
    public void NORMA_oyi_roppa_rosa_KAFOLATGA_teng_chiqadi()
    {
        // ⚠️ §9 dagi ziddiyatning hal qilinishi shu yerda ko'rinadi: A = 1 000, B = 35 000
        // bo'lgandagina "normal oy" kafolat bilan bir xil summa beradi.
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(320, 22, 330, 40, 29, 15_500_000m, 15_500_000m, 0.93, 0, 0),
            RetentionRules, RetentionSalary, guarantee: false);

        Assert.Equal(RetentionRules.Guarantee, r.Salary);
    }

    [Fact]
    public void CHIQUVCHI_KALKULYATOR_qatori_jarimalar_BILAN()
    {
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(ActiveStart: 500, LeftControlled: 60, ActiveEnd: 440,
                                   CourseFinished: 40, Extended: 29,
                                   DebtStart: 18_000_000m, DebtCollected: 79_000_000m,
                                   Efficiency: 0.93, UnknownReasonLeft: 0, Tickets: 1),
            RetentionRules, RetentionSalary, guarantee: true);

        Assert.Equal(0.12, r.ChurnRate, 6);
        Assert.Equal(0.40, r.RetentionCoef);
        Assert.Equal(0.725, r.ExtRate, 6);
        Assert.Equal(1.00, r.ExtensionCoef);
        Assert.Equal(0.01, r.DebtPercent);                  // yig'ish 438.9% ≥ 80% → to'liq ulush

        Assert.Equal(176_000m, r.BonusA);
        Assert.Equal(1_015_000m, r.BonusB);
        Assert.Equal(790_000m, r.BonusC);
        Assert.Equal(1_981_000m, r.BonusTotal);
        Assert.Equal(50_000m, r.FineTickets);
        Assert.Equal(4_931_000m, r.Computed);

        // Kafolat YOQILGAN, lekin hisob undan YUQORI — kafolat qo'llanmaydi.
        Assert.False(r.GuaranteeApplied);
        Assert.Equal(4_931_000m, r.Salary);
        Assert.False(r.CapExceeded);
    }

    [Fact]
    public void QARZ_yigish_normasi_bajarilmasa_ulush_YARMIGA_tushadi()
    {
        static KpiRetentionResult Run(decimal start, decimal collected) => KpiCalculator.Retention(
            new KpiRetentionInputs(320, 22, 330, 40, 29, start, collected, 0.93, 0, 0),
            RetentionRules, RetentionSalary, guarantee: false);

        var past = Run(10_000_000m, 5_000_000m);            // 50% → yarim ulush
        Assert.Equal(0.005, past.DebtPercent);
        Assert.Equal(25_000m, past.BonusC);

        var chegara = Run(10_000_000m, 8_000_000m);         // AYNAN 80% → to'liq ulush
        Assert.Equal(0.01, chegara.DebtPercent);
        Assert.Equal(80_000m, chegara.BonusC);
    }

    [Fact]
    public void SABABSIZ_ketish_va_tiket_jarimalari_ALOHIDA_qatorlar()
    {
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(320, 22, 330, 40, 29, 15_500_000m, 15_500_000m, 0.93,
                                   UnknownReasonLeft: 3, Tickets: 2),
            RetentionRules, RetentionSalary, guarantee: false);

        Assert.Equal(150_000m, r.FineUnknown);
        Assert.Equal(100_000m, r.FineTickets);
        Assert.Equal(4_250_000m, r.Salary);                 // 4 500 000 − 150 000 − 100 000
    }

    [Fact]
    public void KAFOLAT_faqat_muddati_ichida_ishlaydi()
    {
        var input = new KpiRetentionInputs(300, 60, 100, 10, 2, 0m, 0m, 0.5, 0, 0);

        var bilan = KpiCalculator.Retention(input, RetentionRules, RetentionSalary, guarantee: true);
        Assert.True(bilan.GuaranteeApplied);
        Assert.Equal(4_500_000m, bilan.Salary);
        Assert.Equal(3_056_250m, bilan.Computed);           // hisob O'ZI ko'rinib turadi

        var siz = KpiCalculator.Retention(input, RetentionRules, RetentionSalary, guarantee: false);
        Assert.False(siz.GuaranteeApplied);
        Assert.Equal(3_056_250m, siz.Salary);
    }

    [Fact]
    public void OYLIK_CHEGARA_summani_KESMAYDI_faqat_bayroq_qoyadi()
    {
        // ⚠️ Jimgina kesilsa, xodim "nega kam oldim" degan savolga javob topolmasdi va zo'r oy
        // jazoga aylanardi. Chegara — rahbarning ALOHIDA qarori uchun signal.
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(1000, 20, 3000, 100, 90, 100_000_000m, 100_000_000m, 0.96, 0, 0),
            RetentionRules, RetentionSalary, guarantee: false);

        Assert.Equal(3_450_000m, r.BonusA);
        Assert.Equal(3_937_500m, r.BonusB);
        Assert.Equal(1_000_000m, r.BonusC);
        Assert.Equal(8_806_875m, r.BonusTotal);
        Assert.Equal(11_806_875m, r.Salary);                // KESILMAGAN
        Assert.True(r.CapExceeded);
        Assert.True(r.Salary > RetentionRules.MonthlyCap);
    }

    [Fact]
    public void Nol_bazada_hisob_NaN_bermaydi()
    {
        var r = KpiCalculator.Retention(
            new KpiRetentionInputs(0, 0, 0, 0, 0, 0m, 0m, 0.93, 0, 0),
            RetentionRules, RetentionSalary, guarantee: false);

        Assert.Equal(0.0, r.ChurnRate);
        Assert.Equal(0.0, r.ExtRate);
        Assert.Equal(0.0, r.DebtRateFact);
        Assert.Equal(0m, r.BonusTotal);
        Assert.Equal(RetentionSalary, r.Salary);
    }

    [Fact]
    public void SOM_gacha_yaxlitlash_YARMIDAN_YUQORISI_yuqoriga_Excel_dagidek()
    {
        // "Yaxshi oy" qatorida jami bonus 1 952 750 × 1.05 = 2 050 387.5 chiqadi.
        // Bankir yaxlitlashi (.NET standarti) bu yerda boshqa natija berardi.
        Assert.Equal(2_050_388m, KpiCalculator.Som(2_050_387.5m));
        Assert.Equal(501m, KpiCalculator.Som(500.5m));
        Assert.Equal(502m, KpiCalculator.Som(501.5m));
    }
}
