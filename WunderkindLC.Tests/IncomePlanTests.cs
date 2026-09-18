using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// "TUSHUM REJASI" (Moliya → Tushum rejasi) — `IncomePlan.Compute` sof hisobi.
/// Qulflanadi: reja faqat hisobi BOR o'quvchilardan, oy boshidagi balans joriy balansdan
/// TIKLANISHI (to'lov ayriladi, YOZILGAN hisob qo'shiladi) va "qolgan kutilayotgan" formulasi.
/// </summary>
public class IncomePlanTests
{
    private static SubscriptionRisk.MonthCharge Written(string id, decimal v) => new(id, v, Written: true);
    private static SubscriptionRisk.MonthCharge Pending(string id, decimal v) => new(id, v, Written: false);

    private static decimal Amount(IncomePlan.Result r, string label) =>
        r.Rows.Single(x => x.Label == label).Amount;

    private static int? Students(IncomePlan.Result r, string label) =>
        r.Rows.Single(x => x.Label == label).Students;

    [Fact]
    public void Reja_faqat_HISOBI_BOR_oquvchilardan_sanaladi()
    {
        var r = IncomePlan.Compute(
            [("a", 0m), ("b", 0m), ("c", 0m)],
            [Written("a", 100), Pending("b", 200)],
            [],
            "2026-09", "17/09/2026");

        Assert.Equal(300, Amount(r, "17/09/2026"));
        Assert.Equal(2, Students(r, "17/09/2026")); // "c" ning hisobi yo'q
    }

    [Fact]
    public void Oy_boshidagi_balans_TIKLANADI_tolov_ayriladi_YOZILGAN_hisob_qoshiladi()
    {
        // Qarzdor: balans −50, shu oyda 100 to'lagan, yozilgan hisob 100 → oy boshida −50 − 100 + 100 = −50
        // Avansdagi: balans +30, to'lov/hisob yo'q → oy boshida +30.
        var r = IncomePlan.Compute(
            [("a", -50m), ("b", 30m)],
            [Written("a", 100)],
            [new IncomePlan.Payment("a", 100)],
            "2026-09", "reja");

        Assert.Equal(-50, Amount(r, IncomePlan.LabelDebt));
        Assert.Equal(1, Students(r, IncomePlan.LabelDebt));
        Assert.Equal(30, Amount(r, IncomePlan.LabelCredit));
        Assert.Equal(100, Amount(r, IncomePlan.LabelPaid));
    }

    [Fact]
    public void Qolgan_kutilayotgan_tushum_formulasi()
    {
        // a: balans −50, hisobi 150 (yozilmagan) → oy boshida −50 (qarz)
        // b: balans +30, hisobi 150 (yozilmagan) → oy boshida +30 (avans)
        // c: balans 0, shu oyda 100 to'lagan, hisobi yo'q → oy boshida −100 (ESKI qarzini yopgan)
        // reja 300 + qarz (50+100) − avans 30 − to'langan 100 = 320
        var r = IncomePlan.Compute(
            [("a", -50m), ("b", 30m), ("c", 0m)],
            [Pending("a", 150), Pending("b", 150)],
            [new IncomePlan.Payment("c", 100)],
            "2026-09", "reja");

        Assert.Equal(-150, Amount(r, IncomePlan.LabelDebt));
        Assert.Equal(320, Amount(r, IncomePlan.LabelRemaining));
        Assert.Null(Students(r, IncomePlan.LabelRemaining));
    }

    [Fact]
    public void Vozvrat_tolangan_summadan_AYIRILADI()
    {
        var r = IncomePlan.Compute(
            [("a", 0m)],
            [],
            [new IncomePlan.Payment("a", 100), new IncomePlan.Payment("a", -40)],
            "2026-09", "reja");

        Assert.Equal(60, Amount(r, IncomePlan.LabelPaid));
        Assert.Equal(1, Students(r, IncomePlan.LabelPaid)); // bitta o'quvchi, ikki yozuv
    }
}
