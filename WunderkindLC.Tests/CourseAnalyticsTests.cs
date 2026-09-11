using WunderkindLC.Application.Services;
using Xunit;
using Row = WunderkindLC.Application.Services.CourseAnalytics.MembershipRow;

namespace WunderkindLC.Tests;

/// <summary>
/// KURSLAR ANALITIKASI hisob-kitobi (<see cref="CourseAnalytics"/>).
///
/// <para>Eng muhim talab: GURUH ALMASHTIRISH "ketdi" bo'lib ko'rinmasligi kerak. O'quvchi bir
/// kursning ichida guruhdan guruhga o'tsa yoki darajani tugatib keyingisiga o'tsa — u kursdan
/// KETMAGAN. Aks holda hisobot yolg'on churn ko'rsatardi.</para>
/// </summary>
public class CourseAnalyticsTests
{
    private static Row M(
        string student, string joined, string activated = "", string? left = null,
        string frozen = "", string status = "active", bool isActive = true, decimal fee = 500_000m)
        => new(student, "c1", joined, activated, left, frozen, status, isActive, fee);

    // ==================== Oraliqlarni birlashtirish ====================

    [Fact]
    public void Guruh_almashtirish_KETDI_bolib_kormaydi()
    {
        // Eski guruhdan 10-mart kuni chiqdi, ertasiga yangi guruhda paydo bo'ldi — bu BITTA oraliq.
        var res = CourseAnalytics.MergeIntervals(new[]
        {
            M("s1", "2026-01-05", left: "2026-03-10", status: "active", isActive: false),
            M("s1", "2026-03-11"),
        });

        var i = Assert.Single(res);
        Assert.Equal("2026-01-05", i.Start);
        Assert.Null(i.End);   // hali kursda
    }

    [Fact]
    public void Uzoq_tanaffusdan_keyin_qaytish_YANGI_oraliq()
    {
        // 4 oy yo'q bo'lib ketib qaytgan — bu haqiqatan ketgan va QAYTA kelgan.
        var res = CourseAnalytics.MergeIntervals(new[]
        {
            M("s1", "2026-01-05", left: "2026-02-01", isActive: false),
            M("s1", "2026-06-01"),
        });

        Assert.Equal(2, res.Count);
        Assert.Equal("2026-02-01", res[0].End);
        Assert.Equal("2026-06-01", res[1].Start);
    }

    [Fact]
    public void Parallel_ikkita_guruh_bitta_oraliq()
    {
        // Bir kursning IKKI guruhida bir vaqtda — o'quvchi kursda BIR marta bor.
        var res = CourseAnalytics.MergeIntervals(new[]
        {
            M("s1", "2026-01-05"),
            M("s1", "2026-02-01"),
        });

        var i = Assert.Single(res);
        Assert.Equal("2026-01-05", i.Start);
        Assert.Null(i.End);
    }

    [Fact]
    public void Parallel_guruhda_TUGATGAN_belgisi_oraliqni_YOPGAN_azolikdan_olinadi()
    {
        // Ikkinchi (parallel) a'zolik ERTA "tugatgan" bo'lib yopilgan, lekin o'quvchi birinchi
        // guruhda iyungacha o'qigan va u yerdan KETGAN. Oraliqni yopgan — birinchi a'zolik,
        // demak bu "tugatdi" emas, "ketdi".
        var res = CourseAnalytics.MergeIntervals(new[]
        {
            M("s1", "2026-01-05", left: "2026-06-01", status: "active", isActive: false),
            M("s1", "2026-02-01", left: "2026-03-01", status: "completed", isActive: false),
        });

        var i = Assert.Single(res);
        Assert.Equal("2026-06-01", i.End);
        Assert.False(i.Completed);
    }

    [Fact]
    public void Tugatgan_azolik_alohida_belgilanadi()
    {
        var res = CourseAnalytics.MergeIntervals(new[]
        {
            M("s1", "2026-01-05", left: "2026-05-20", status: "completed", isActive: false),
        });

        var i = Assert.Single(res);
        Assert.True(i.Completed);
    }

    // ==================== Oy oxirida faol ====================

    [Theory]
    // Aktivlashtirilgan va hali ketmagan — faol.
    [InlineData("2026-01-10", null, "", "2026-03-31", true)]
    // Aktivlashtirish sanasidan OLDIN — hali faol emas.
    [InlineData("2026-04-10", null, "", "2026-03-31", false)]
    // Ketgan — faol emas.
    [InlineData("2026-01-10", "2026-02-15", "", "2026-03-31", false)]
    // Ketgan, lekin KEYIN — o'sha oyda hali faol edi.
    [InlineData("2026-01-10", "2026-05-15", "", "2026-03-31", true)]
    // Muzlatilgan — faol emas.
    [InlineData("2026-01-10", null, "2026-02-01", "2026-03-31", false)]
    // Muzlatish KEYINROQ — o'sha oyda hali faol edi (Status "frozen" bo'lsa ham!).
    [InlineData("2026-01-10", null, "2026-05-01", "2026-03-31", true)]
    // Hech qachon aktivlashmagan (sinov) — faol emas.
    [InlineData("", null, "", "2026-03-31", false)]
    public void Oy_oxiridagi_faollik_SANALAR_boyicha_tiklanadi(
        string activated, string? left, string frozen, string date, bool expected)
    {
        // ⚠️ `Status` JORIY holat — o'tmishni u bilan hisoblab bo'lmaydi. Shu sababdan
        // status ataylab "frozen" berilgan: natija faqat sanalarga bog'liq bo'lishi kerak.
        var row = M("s1", "2026-01-01", activated: activated, left: left, frozen: frozen, status: "frozen");
        Assert.Equal(expected, CourseAnalytics.WasActiveAt(row, date));
    }

    // ==================== Oylik oqim ====================

    [Fact]
    public void Oylik_oqim_keldi_ketdi_va_tugatdi_ni_ajratadi()
    {
        var byStudent = new Dictionary<string, List<Row>>
        {
            // Yanvarda keldi va aktivlashdi, martda KETDI.
            ["s1"] = new() { M("s1", "2026-01-10", activated: "2026-01-10", left: "2026-03-05", isActive: false) },
            // Yanvarda keldi, fevralda kursni TUGATDI (churn EMAS).
            ["s2"] = new() { M("s2", "2026-01-15", activated: "2026-01-15", left: "2026-02-20",
                                status: "completed", isActive: false) },
            // Fevralda keldi, hamon o'qiydi.
            ["s3"] = new() { M("s3", "2026-02-01", activated: "2026-02-01") },
        };

        var flows = CourseAnalytics.MonthlyFlow(byStudent, new[] { "2026-01", "2026-02", "2026-03" });

        Assert.Equal(2, flows[0].Joined);        // yanvar: s1, s2
        Assert.Equal(2, flows[0].Activated);
        Assert.Equal(0, flows[0].Left);
        Assert.Equal(2, flows[0].ActiveEnd);

        Assert.Equal(1, flows[1].Joined);        // fevral: s3
        Assert.Equal(0, flows[1].Left);
        Assert.Equal(1, flows[1].Completed);     // s2 tugatdi — "ketdi" emas
        Assert.Equal(2, flows[1].ActiveEnd);     // s1 + s3

        Assert.Equal(1, flows[2].Left);          // mart: s1 ketdi
        Assert.Equal(0, flows[2].Completed);
        Assert.Equal(1, flows[2].ActiveEnd);     // faqat s3
    }

    [Fact]
    public void Bir_oyda_ikki_guruhga_qoshilgan_oquvchi_BIR_marta_sanaladi()
    {
        var byStudent = new Dictionary<string, List<Row>>
        {
            ["s1"] = new() { M("s1", "2026-02-03", activated: "2026-02-03"), M("s1", "2026-02-20") },
        };

        var flows = CourseAnalytics.MonthlyFlow(byStudent, new[] { "2026-02" });
        Assert.Equal(1, flows[0].Joined);
        Assert.Equal(1, flows[0].Activated);
        Assert.Equal(1, flows[0].ActiveEnd);
    }

    // ==================== Kesishuv ====================

    [Fact]
    public void Kesishuv_taqsimot_va_juftliklarni_beradi()
    {
        var active = new Dictionary<string, HashSet<string>>
        {
            ["s1"] = new() { "ing" },
            ["s2"] = new() { "ing", "mat" },
            ["s3"] = new() { "ing", "mat" },
            ["s4"] = new() { "ing", "mat", "rus" },
        };

        var (buckets, pairs) = CourseAnalytics.Overlap(active);

        Assert.Equal(new[] { 1, 2, 3 }, buckets.Select(b => b.Courses).ToArray());
        Assert.Equal(1, buckets[0].Students);   // faqat "ing"
        Assert.Equal(2, buckets[1].Students);   // s2, s3
        Assert.Equal(1, buckets[2].Students);   // s4

        // Eng ko'p birga o'qiladigan juftlik — ing+mat (s2, s3, s4).
        Assert.Equal(3, pairs[0].Students);
        Assert.Equal("ing", pairs[0].AId);
        Assert.Equal("mat", pairs[0].BId);
    }

    [Fact]
    public void Juftlik_kaliti_TARTIBLANGAN_ikki_marta_sanalmaydi()
    {
        // (A,B) va (B,A) BITTA juftlik bo'lishi kerak — aks holda jadvalda takror qatorlar chiqardi.
        var active = new Dictionary<string, HashSet<string>>
        {
            ["s1"] = new() { "b", "a" },
            ["s2"] = new() { "a", "b" },
        };

        var (_, pairs) = CourseAnalytics.Overlap(active);
        var p = Assert.Single(pairs);
        Assert.Equal(2, p.Students);
        Assert.Equal("a", p.AId);
        Assert.Equal("b", p.BId);
    }

    // ==================== Oylar ro'yxati ====================

    [Fact]
    public void Oxirgi_oylar_eng_eskisidan_boshlanadi()
    {
        var months = CourseAnalytics.LastMonths(new DateOnly(2026, 3, 15), 3);
        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03" }, months);
    }

    [Fact]
    public void MonthEnd_oyning_oxirgi_kunini_beradi()
    {
        Assert.Equal("2026-02-28", CourseAnalytics.MonthEnd("2026-02"));
        Assert.Equal("2024-02-29", CourseAnalytics.MonthEnd("2024-02"));   // kabisa yil
        Assert.Equal("2026-12-31", CourseAnalytics.MonthEnd("2026-12"));
    }
}
