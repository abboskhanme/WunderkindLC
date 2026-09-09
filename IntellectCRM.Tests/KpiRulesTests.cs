using IntellectCRM.Application.Services.Kpi;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// POG'ONA tanlash (Excel <c>MATCH(...;1)</c>), seed qiymatlari va VERSIYALASH qulflari.
///
/// <para>Eng nozik joyi — CHEGARALAR: jadvaldagi oraliqlar "0.2499 → 0.25" bo'lib tutashadi,
/// ya'ni bitta <c>&lt;</c> / <c>&lt;=</c> xatosi xodimning bonusini butun bir pog'onaga
/// surib yuborardi.</para>
/// </summary>
public class KpiRulesTests
{
    private static IReadOnlyList<KpiTier> Conv => KpiRuleSeed.Intake().Conversion;
    private static IReadOnlyList<KpiTier> Eff => KpiRuleSeed.Intake().Efficiency;
    private static IReadOnlyList<KpiTier> Churn => KpiRuleSeed.Retention().Retention;
    private static IReadOnlyList<KpiTier> Ext => KpiRuleSeed.Retention().Extension;

    /* ----------------------- CHEGARALAR ----------------------- */

    [Fact]
    public void Konversiya_chegarasi_0_2499_pastda_qoladi_0_25_esa_keyingi_pogonaga_otadi()
    {
        Assert.Equal(0.5, KpiRules.Coef(Conv, 0.2499));
        Assert.Equal(0.8, KpiRules.Coef(Conv, 0.25));
        // Chegaraning O'ZI kiradi: 0.25 dan bir tuki past qiymat hali pastki pog'onada.
        Assert.Equal(0.5, KpiRules.Coef(Conv, 0.249999));
    }

    [Fact]
    public void Ketish_foizi_chegarasi_0_0399_va_0_04()
    {
        Assert.Equal(1.15, KpiRules.Coef(Churn, 0.0399));
        Assert.Equal(1.10, KpiRules.Coef(Churn, 0.04));
    }

    [Fact]
    public void Samaradorlik_chegarasi_0_7999_va_0_80()
    {
        Assert.Equal(0.75, KpiRules.Coef(Eff, 0.7999));
        Assert.Equal(0.90, KpiRules.Coef(Eff, 0.80));
    }

    [Fact]
    public void Samaradorlik_chegarasi_0_9499_va_0_95()
    {
        Assert.Equal(1.00, KpiRules.Coef(Eff, 0.9499));
        // ⚠️ Kiruvchida eng yuqori qadam 1.1, chiquvchida 1.05 — ATAYIN farqli.
        Assert.Equal(1.10, KpiRules.Coef(Eff, 0.95));
        Assert.Equal(1.05, KpiRules.Coef(KpiRuleSeed.Retention().Efficiency, 0.95));
    }

    [Fact]
    public void Uzaytirish_chegaralari_har_pogonada_tekshiriladi()
    {
        Assert.Equal(0.50, KpiRules.Coef(Ext, 0.5499));
        Assert.Equal(0.80, KpiRules.Coef(Ext, 0.55));
        Assert.Equal(0.80, KpiRules.Coef(Ext, 0.6499));
        Assert.Equal(1.00, KpiRules.Coef(Ext, 0.65));
        Assert.Equal(1.00, KpiRules.Coef(Ext, 0.7599));
        Assert.Equal(1.15, KpiRules.Coef(Ext, 0.76));
        Assert.Equal(1.15, KpiRules.Coef(Ext, 0.8599));
        Assert.Equal(1.25, KpiRules.Coef(Ext, 0.86));
    }

    /* ----------------------- ORALIQDAN TASHQARI ----------------------- */

    [Fact]
    public void Jadvaldan_PAST_qiymat_Excel_dagi_NA_emas_ENG_PAST_pogonani_oladi()
    {
        // Admin birinchi qatorni 0.05 dan boshlab qo'ysa, 0.01 hech qaysi oraliqqa tushmaydi.
        // Excel #N/A berardi — biz eng past pog'onani beramiz, aks holda butun hisob yiqilardi.
        var tiers = new List<KpiTier> { new(0.05, 0.10, 0.7), new(0.10, 1.0, 1.0) };
        Assert.Equal(0.7, KpiRules.Coef(tiers, 0.01));
        Assert.Equal(0.05, KpiRules.TierOf(tiers, 0.0)!.From);
    }

    [Fact]
    public void Jadvaldan_YUQORI_qiymat_eng_songgi_pogonada_qoladi()
    {
        // Ketish foizi 100% dan oshmaydi, lekin qarz yig'ilishi 438% bo'lishi mumkin.
        Assert.Equal(1.4, KpiRules.Coef(Conv, 5.0));
        Assert.Equal(0.40, KpiRules.Coef(Churn, 1.0));
    }

    [Fact]
    public void Bosh_royxat_koeffitsientni_QOLLAMAYDI_yani_1_0()
    {
        // ⚠️ 0 EMAS: qoidasi hali seed qilinmagan rolda butun bonus jimgina yo'qolardi.
        Assert.Equal(1.0, KpiRules.Coef(new List<KpiTier>(), 0.5));
        Assert.Equal(1.0, KpiRules.Coef(null, 0.5));
        Assert.Null(KpiRules.TierOf(new List<KpiTier>(), 0.5));
    }

    [Fact]
    public void Tartibsiz_royxatda_ham_ENG_KATTA_mos_pogona_tanlanadi()
    {
        // Jadval "Qoidalar" sahifasidan qo'lda tahrirlanadi — tartibi buzilishi mumkin.
        var tiers = new List<KpiTier> { new(0.50, 1.0, 1.2), new(0.00, 0.49, 0.8), new(0.30, 0.49, 1.0) };
        Assert.Equal(0.8, KpiRules.Coef(tiers, 0.10));
        Assert.Equal(1.0, KpiRules.Coef(tiers, 0.35));
        Assert.Equal(1.2, KpiRules.Coef(tiers, 0.90));
    }

    [Fact]
    public void Pogona_IZOHI_ham_qaytadi_raqamning_ozi_hech_narsani_tushuntirmaydi()
    {
        Assert.Equal("NORMA. Bonus to'liq.", KpiRules.TierOf(Conv, 0.32)!.Note);
        Assert.Equal("Baza oqib ketyapti.", KpiRules.TierOf(Churn, 0.13)!.Note);
    }

    /* ----------------------- SEED ----------------------- */

    [Fact]
    public void Seed_qiymatlari_Excel_konstantalari_bilan_bir_xil()
    {
        var i = KpiRuleSeed.Intake();
        Assert.Equal(35_000m, i.ContractBase);
        Assert.Equal(50_000m, i.TicketFine);
        Assert.Equal(4_500_000m, i.Guarantee);
        Assert.Null(i.MonthlyCap);            // ⚠️ kiruvchida chegara YO'Q
        Assert.Equal(250, i.LeadFloor);
        Assert.Equal(90, i.MonthlyContractPlan);
        Assert.Equal(26, i.WorkDays);
        Assert.Equal(5, i.TicketStepThreshold);

        var r = KpiRuleSeed.Retention();
        // ⚠️ §9 dagi ziddiyat: "Boshlash" varag'i 6 000 / 45 000 deydi, FORMULALAR esa
        // 1 000 / 35 000 — seed formula qiymati bilan (NORMA oyi roppa-rosa kafolatga teng).
        Assert.Equal(1_000m, r.ActiveStudentBonus);
        Assert.Equal(35_000m, r.ExtensionBonus);
        Assert.Equal(0.01, r.DebtRate);
        Assert.Equal(0.80, r.DebtRateNorm);
        Assert.Equal(7_500_000m, r.MonthlyCap);
        Assert.Equal(50_000m, r.UnknownReasonFine);
    }

    [Fact]
    public void Call_operator_kiruvchining_NUSXASI_farqi_faqat_birlik_narxi()
    {
        var i = KpiRuleSeed.Intake();
        var o = KpiRuleSeed.CallOperator();

        Assert.Equal(25_000m, o.ContractBase);          // sinovga KELGAN har bir odam uchun

        // Qolgani AYNAN bir xil (jadvallar ikki joyda ayri tahrirlanib ketmasin).
        // ⚠️ Recordning o'z tengligi bu yerda YARAMAYDI: List<KpiTier> maydonlari havola
        // bo'yicha solishtiriladi, ya'ni mazmuni bir xil ikki ro'yxat "teng emas" chiqadi.
        Assert.Equal(i.TicketFine, o.TicketFine);
        Assert.Equal(i.Guarantee, o.Guarantee);
        Assert.Equal(i.MonthlyCap, o.MonthlyCap);
        Assert.Equal(i.LeadFloor, o.LeadFloor);
        Assert.Equal(i.MonthlyContractPlan, o.MonthlyContractPlan);
        Assert.Equal(i.QualityNorm, o.QualityNorm);
        Assert.Equal(i.Conversion, o.Conversion);
        Assert.Equal(i.Efficiency, o.Efficiency);
    }

    [Fact]
    public void Birlik_iqtisodiyoti_konstantalari_HAR_UCHALA_rolda_bir_xil()
    {
        // Bo'sh (0) qolgan rolda "Yopish" sahifasidagi indikator nolga bo'linardi.
        foreach (var role in KpiRuleSeed.Roles)
        {
            var r = KpiRuleSeed.For(role);
            Assert.Equal(500_000m, r.UnitCoursePrice);
            Assert.Equal(0.55, r.UnitTeacherShare);
            Assert.Equal(4, r.UnitExtensionMonths);
            Assert.Equal(0.10, r.UnitMaxSalaryShare);
        }
    }

    [Fact]
    public void Rol_yorliqlari_va_nomalum_kod_KODNING_OZI()
    {
        Assert.Equal("Kiruvchi admin", KpiRuleSeed.RoleLabel(KpiConst.Intake));
        Assert.Equal("Chiquvchi admin", KpiRuleSeed.RoleLabel(KpiConst.Retention));
        Assert.Equal("Call operator", KpiRuleSeed.RoleLabel(KpiConst.Operator));
        // Bo'sh matn EMAS: jadvaldagi bo'sh katak "roli yo'q" bo'lib ko'rinardi.
        Assert.Equal("boshqa_rol", KpiRuleSeed.RoleLabel("boshqa_rol"));
    }

    /* ----------------------- TIKET KATALOGI (§14) ----------------------- */

    [Fact]
    public void Audit_mezonlari_13_ta_va_raqamlari_1_dan_13_gacha_uzluksiz()
    {
        Assert.Equal(13, KpiTicketCatalog.Criteria.Count);
        Assert.Equal(Enumerable.Range(1, 13), KpiTicketCatalog.Criteria.Select(x => x.No));
        Assert.Equal("Natijani RAQAM bilan aytdi", KpiTicketCatalog.CriterionLabel(5));
        Assert.Null(KpiTicketCatalog.CriterionLabel(99));
    }

    [Fact]
    public void Tiket_sabablari_takrorlanmaydi_va_mezon_raqami_HAQIQIY()
    {
        var codes = KpiTicketCatalog.Reasons.Select(x => x.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());

        // ⚠️ Mavjud bo'lmagan mezonga bog'langan sabab UI'da "mezon: —" bo'lib ko'rinardi,
        // ya'ni tiketning SABABI yo'qolardi.
        foreach (var r in KpiTicketCatalog.Reasons.Where(x => x.CriterionNo is not null))
            Assert.Contains(KpiTicketCatalog.Criteria, c => c.No == r.CriterionNo);
    }

    [Fact]
    public void Rol_boyicha_sabablar_ozinikini_va_UMUMIYNI_beradi()
    {
        var intake = KpiTicketCatalog.ForRole(KpiConst.Intake);
        Assert.Contains(intake, x => x.Code == "result_not_numeric");
        Assert.Contains(intake, x => x.Code == "other");                 // umumiy
        Assert.DoesNotContain(intake, x => x.Code == "room_not_checked");

        // ⚠️ Operator kiruvchining sabablarini ko'radi — ish mazmuni bir xil.
        Assert.Equal(intake.Select(x => x.Code), KpiTicketCatalog.ForRole(KpiConst.Operator).Select(x => x.Code));

        var ret = KpiTicketCatalog.ForRole(KpiConst.Retention);
        Assert.Contains(ret, x => x.Code == "debtor_not_called");
        Assert.Contains(ret, x => x.Code == "other");
    }

    [Fact]
    public void Nomalum_sabab_kaliti_YOQOLMAYDI_kalitning_ozi_qaytadi()
    {
        Assert.Equal("CRM ga yozmadi", KpiTicketCatalog.ReasonLabel("not_in_crm"));
        Assert.Equal("eski_kalit", KpiTicketCatalog.ReasonLabel("eski_kalit"));
    }

    /* ----------------------- VERSIYALASH ----------------------- */

    private static KpiRuleSet Rs(string role, string from, string id) =>
        new() { Id = id, RoleCode = role, EffectiveFrom = from, Json = "{}", CreatedAt = "2026-01-01T00:00:00" };

    [Fact]
    public void Qoida_versiyasi_shu_oyga_TEGISHLI_eng_songgisi()
    {
        var all = new[]
        {
            Rs(KpiConst.Intake, "2026-01", "v1"),
            Rs(KpiConst.Intake, "2026-05", "v2"),
            Rs(KpiConst.Retention, "2026-03", "r1"),
        };

        Assert.Equal("v1", KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-04")!.Id);
        Assert.Equal("v2", KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-05")!.Id);  // o'sha oyning O'ZI kiradi
        Assert.Equal("v2", KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-12")!.Id);
        Assert.Equal("r1", KpiVersioning.RuleFor(all, KpiConst.Retention, "2026-09")!.Id);
    }

    [Fact]
    public void Versiya_topilmasa_null_JIMGINA_standart_qiymat_ISHLATILMAYDI()
    {
        // ⚠️ Hech kim kiritmagan koeffitsientlar bilan hisoblangan oylik HAQIQIY bo'lib
        // ko'rinardi. Klientga RulesMissing bayrog'i qaytishi uchun bu yerda null.
        var all = new[] { Rs(KpiConst.Intake, "2026-05", "v2") };
        Assert.Null(KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-04"));
        Assert.Null(KpiVersioning.RuleFor(all, KpiConst.Retention, "2026-09"));
    }

    [Fact]
    public void TOLIQ_SANA_bilan_kiritilgan_versiya_OSHA_oyda_ishlaydi()
    {
        // "2026-03-15" > "2026-03" bo'lgani uchun qisqartirmasdan solishtirsak, mart oyida
        // kiritilgan versiya aynan MARTDA ishlamasdi — o'zgarish bir oyga kechikardi.
        var all = new[] { Rs(KpiConst.Intake, "2026-03-15", "v1") };
        Assert.Equal("v1", KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-03")!.Id);
    }

    [Fact]
    public void Oklad_versiyasi_XODIM_va_oy_boyicha_tanlanadi()
    {
        var all = new[]
        {
            new KpiProfileSalary { Id = "s1", UserId = "u1", EffectiveFrom = "2026-01", BaseSalary = 2_500_000m, CreatedAt = "2026-01-01T00:00:00" },
            new KpiProfileSalary { Id = "s2", UserId = "u1", EffectiveFrom = "2026-06", BaseSalary = 3_000_000m, CreatedAt = "2026-06-01T00:00:00" },
            new KpiProfileSalary { Id = "s3", UserId = "u2", EffectiveFrom = "2026-01", BaseSalary = 9_000_000m, CreatedAt = "2026-01-01T00:00:00" },
        };

        Assert.Equal(2_500_000m, KpiVersioning.SalaryFor(all, "u1", "2026-05")!.BaseSalary);
        Assert.Equal(3_000_000m, KpiVersioning.SalaryFor(all, "u1", "2026-06")!.BaseSalary);
        Assert.Equal(9_000_000m, KpiVersioning.SalaryFor(all, "u2", "2026-12")!.BaseSalary);
        Assert.Null(KpiVersioning.SalaryFor(all, "u3", "2026-12"));
    }

    [Fact]
    public void Bir_xil_oyda_IKKI_versiya_bolsa_KEYIN_kiritilgani_golib()
    {
        var all = new[]
        {
            Rs(KpiConst.Intake, "2026-05", "eski"),
            new KpiRuleSet { Id = "yangi", RoleCode = KpiConst.Intake, EffectiveFrom = "2026-05", Json = "{}", CreatedAt = "2026-05-20T10:00:00" },
        };
        Assert.Equal("yangi", KpiVersioning.RuleFor(all, KpiConst.Intake, "2026-07")!.Id);
    }
}
