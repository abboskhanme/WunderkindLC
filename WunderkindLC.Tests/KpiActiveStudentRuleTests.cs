using WunderkindLC.Application.Services.Kpi;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «FAOL O'QUVCHI» ta'rifi (spetsifikatsiya §6) — chiquvchi adminning butun bonusi shu songa
/// ko'paytiriladi, ketish foizi esa shu songa bo'linadi.
///
/// <para>Shuning uchun har SHART alohida qulflanadi: ta'rif bir tomonga bir kunga surilsa,
/// oy boshi va oy oxiri sanoqlari mos kelmay qolar va "ketish foizi" xayoliy bo'lardi.</para>
/// </summary>
public class KpiActiveStudentRuleTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 30);

    private static string Iso(int daysAgo) => AsOf.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    /// <summary>Barcha shartlari joyida bo'lgan o'quvchi — boshqa testlar shundan chetlashadi.</summary>
    private static bool Run(bool member = true, string? present = null, string? due = null,
                            bool archived = false, int absence = 30, int debt = 45) =>
        KpiActiveStudentRule.IsActive(member, present ?? Iso(1), due, archived, AsOf, absence, debt);

    [Fact]
    public void Hamma_sharti_joyida_bolgan_oquvchi_FAOL()
    {
        Assert.True(Run());
    }

    /* ---------- 1-shart: FAOL a'zolik ---------- */

    [Fact]
    public void FAOL_azoligi_yoq_oquvchi_faol_EMAS()
    {
        // ⚠️ trial va frozen ham "faol a'zolik" emas: sinovdagi hali pul to'lamaydi,
        // muzlatilgani esa to'xtatgan. Ikkalasini ham sanasak, bonus A ro'yxat uzunligi
        // uchun to'langan bo'lardi.
        Assert.False(Run(member: false));
        // Qolgan hamma sharti joyida bo'lsa ham — bu shart YETARLI.
        Assert.False(KpiActiveStudentRule.IsActive(false, Iso(0), null, false, AsOf));
    }

    /* ---------- 2-shart: 30 KUN kelmaslik ---------- */

    [Fact]
    public void Kelmaslik_chegarasi_29_kun_FAOL_30_kun_esa_EMAS()
    {
        Assert.True(Run(present: Iso(29)));
        // ⚠️ Chegara QAT'IY: "30 kun kelmagan" — allaqachon kelmagan.
        Assert.False(Run(present: Iso(30)));
        Assert.False(Run(present: Iso(31)));
    }

    [Fact]
    public void Hali_BIRORTA_dars_bolmagan_oquvchi_baribir_FAOL()
    {
        // ⚠️ Yangi qo'shilgan o'quvchining jurnalda yozuvi yo'q. Bo'sh sanani "juda eski"
        // deb hisoblasak, yangi kelganlar oy boshidayoq bazadan tushib qolar va ketish
        // foizi soxta ravishda ko'tarilardi.
        Assert.True(Run(present: ""));
        Assert.True(KpiActiveStudentRule.IsActive(true, null, null, false, AsOf));
    }

    [Fact]
    public void Kelmaslik_chegarasi_PARAMETR_bilan_ozgaradi()
    {
        Assert.True(Run(present: Iso(40), absence: 60));
        Assert.False(Run(present: Iso(40), absence: 30));
    }

    /* ---------- 3-shart: 45 KUN qarz ---------- */

    [Fact]
    public void Qarz_chegarasi_44_kun_FAOL_45_kun_esa_EMAS()
    {
        Assert.True(Run(due: Iso(44)));
        Assert.False(Run(due: Iso(45)));
        Assert.False(Run(due: Iso(46)));
    }

    [Fact]
    public void Qarzi_YOQ_oquvchi_FAOL()
    {
        Assert.True(Run(due: null));
        Assert.True(Run(due: ""));
    }

    [Fact]
    public void Muddati_hali_KELMAGAN_hisob_faollikka_tegmaydi()
    {
        Assert.True(Run(due: AsOf.AddDays(10).ToString("yyyy-MM-dd")));
    }

    /* ---------- 4-shart: arxiv ---------- */

    [Fact]
    public void ARXIVLANGAN_oquvchi_faol_EMAS()
    {
        Assert.False(Run(archived: true));
        // Boshqa hamma sharti mukammal bo'lsa ham.
        Assert.False(KpiActiveStudentRule.IsActive(true, Iso(0), null, true, AsOf));
    }

    /* ---------- Buzuq ma'lumot ---------- */

    [Fact]
    public void BUZUQ_sana_oquvchini_bazadan_CHIQARIB_yubormaydi()
    {
        // ⚠️ Qo'lda kiritilgan "2026-13-99" tufayli tirik o'quvchini yo'qotish, uni
        // qoldirishdan ko'ra battar: pul hisobi aynan shu songa bog'liq.
        Assert.True(Run(present: "2026-13-99"));
        Assert.True(Run(due: "kecha"));
    }

    [Fact]
    public void TOLIQ_vaqtli_sana_ham_oqiladi()
    {
        Assert.False(Run(present: Iso(31) + "T09:00:00"));
        Assert.True(Run(present: Iso(2) + "T09:00:00"));
    }

    /* ---------- Standart chegaralar ---------- */

    [Fact]
    public void Standart_chegaralar_30_va_45_kun()
    {
        Assert.Equal(30, KpiActiveStudentRule.DefaultAbsenceDays);
        Assert.Equal(45, KpiActiveStudentRule.DefaultDebtDays);
    }
}
