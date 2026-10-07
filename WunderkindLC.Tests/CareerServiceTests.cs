using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="CareerService"/> ning SOF (bog'liqliksiz) qismlari: bosqichlar katalogi,
/// HTML ekranlash va ariza yaratish. Bosqichlar katalogi backend, Mini App va admin paneli
/// uchun YAGONA haqiqat manbai — kalitlari o'zgarsa bazadagi eski arizalar "noma'lum bosqich"
/// bo'lib qoladi, shuning uchun ular alohida qulflab qo'yilgan.
/// </summary>
public class CareerServiceTests
{
    /* =========================================================================================
     *  BOSQICHLAR KATALOGI
     * ========================================================================================= */

    [Fact]
    public void Stages_KalitlarNoyob()
    {
        var keys = CareerService.Stages.Select(s => s.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Stages_KutilganKalitlar_TartibBilan()
    {
        // Bu kalitlar bazada saqlanadi va Mini App/frontend ham AYNAN shularni kutadi —
        // o'zgartirish = buzilish. Test ataylab qattiq yozilgan.
        // 2026-10: voronka yig'ilish talabiga moslandi — ESKI kalitlarning HAMMASI saqlangan
        // (new, review, interview, trial, hired, rejected), yangilari: test, director.
        Assert.Equal(
            new[] { "new", "review", "test", "trial", "interview", "director", "hired", "rejected" },
            CareerService.Stages.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void Stages_EskiKalitlarningHammasi_HamonHaqiqiy()
    {
        // Bazadagi mavjud arizalar "noma'lum bosqich" bo'lib qolmasin — eski katalogdagi har kalit bor.
        foreach (var eski in new[] { "new", "review", "interview", "trial", "hired", "rejected" })
            Assert.True(CareerService.IsValidStatus(eski), $"Eski kalit yo'qolgan: {eski}");
    }

    [Fact]
    public void FunnelStages_YigilishdagiVoronka_TartibBilan()
    {
        // CV qabul qilish -> Test -> Demo dars -> HR suhbati -> Rahbar (direktor) suhbati -> Ishga qabul.
        Assert.Equal(
            new[] { "new", "test", "trial", "interview", "director", "hired" },
            CareerService.FunnelStages.Select(s => s.Key).ToArray());
        Assert.Equal(
            new[] { "CV qabul qilindi", "Test", "Demo dars", "HR suhbati", "Rahbar (direktor) suhbati", "Ishga qabul qilindi" },
            CareerService.FunnelStages.Select(s => s.Label).ToArray());
    }

    [Fact]
    public void FunnelStages_TartibQatiyOsuvchi()
    {
        var orders = CareerService.FunnelStages.Select(s => s.Order).ToList();
        Assert.Equal(orders.OrderBy(o => o).ToList(), orders);
        Assert.Equal(orders.Count, orders.Distinct().Count());
    }

    [Fact]
    public void Review_EskiBosqich_VoronkadaYoq_CVBilanBirOrinda()
    {
        var review = CareerService.StageOf(CareerService.StatusReview);

        Assert.True(review.IsLegacy);
        Assert.DoesNotContain(CareerService.FunnelStages, s => s.Key == CareerService.StatusReview);
        // Ma'nosi "CV ko'rib chiqilmoqda" — yo'l-xaritada CV bosqichi bilan BIR o'rinda.
        Assert.Equal(CareerService.StageOf(CareerService.StatusNew).Order, review.Order);
        // Faqat bitta eski bosqich bor.
        Assert.Single(CareerService.Stages, s => s.IsLegacy);
    }

    [Theory]
    [InlineData("new", "test", true)]
    [InlineData("new", "director", true)]
    [InlineData("hired", "new", true)]           // orqaga qaytarish taqiqlanmagan (admin xatosini tuzatish)
    [InlineData("new", "review", false)]         // eski bosqichga YANGI o'tish yopiq
    [InlineData("interview", "review", false)]
    [InlineData("review", "review", true)]       // o'sha bosqichdagi arizaning izohini yangilash
    [InlineData("review", "test", true)]         // eski bosqichdan chiqib ketish mumkin
    [InlineData("new", "nomalum", false)]
    [InlineData("new", null, false)]
    public void CanMoveTo_EskiBosqichgaYangiOtishYopiq(string? current, string? target, bool kutilgan)
    {
        Assert.Equal(kutilgan, CareerService.CanMoveTo(current, target));
    }

    [Theory]
    [InlineData("new", 1)]
    [InlineData("review", 1)]
    [InlineData("test", 2)]
    [InlineData("trial", 3)]
    [InlineData("interview", 4)]
    [InlineData("director", 5)]
    [InlineData("hired", 6)]
    [InlineData("rejected", 0)]
    [InlineData("nomalum", 0)]
    [InlineData(null, 0)]
    public void FunnelOrderOf_KalitlarniVoronkaOrniga(string? key, int kutilgan)
    {
        Assert.Equal(kutilgan, CareerService.FunnelOrderOf(key));
    }

    [Fact]
    public void Stages_MatnlarBoshEmas()
    {
        foreach (var s in CareerService.Stages)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Label), $"Label bo'sh: {s.Key}");
            Assert.False(string.IsNullOrWhiteSpace(s.CandidateText), $"CandidateText bo'sh: {s.Key}");
            Assert.False(string.IsNullOrWhiteSpace(s.Icon), $"Icon bo'sh: {s.Key}");
        }
    }

    [Fact]
    public void Stages_YakuniyBosqichlar_HiredVaRejected()
    {
        var final = CareerService.Stages.Where(s => s.IsFinal).Select(s => s.Key).ToArray();

        Assert.Equal(new[] { "hired", "rejected" }, final);
    }

    [Fact]
    public void Stages_RejectedYolXaritasidanTashqarida()
    {
        // "rejected" — yakuniy natija, yo'l-xaritaga kirmaydi: tartibi eng oxirida (99).
        var rejected = CareerService.StageOf(CareerService.StatusRejected);
        var boshqalar = CareerService.Stages.Where(s => s.Key != CareerService.StatusRejected);

        Assert.All(boshqalar, s => Assert.True(s.Order < rejected.Order));
    }

    [Theory]
    [InlineData("new", "CV qabul qilindi")]
    [InlineData("review", "Ko'rib chiqilmoqda (eski)")]
    [InlineData("test", "Test")]
    [InlineData("trial", "Demo dars")]
    [InlineData("interview", "HR suhbati")]
    [InlineData("director", "Rahbar (direktor) suhbati")]
    [InlineData("hired", "Ishga qabul qilindi")]
    [InlineData("rejected", "Rad etildi")]
    public void StageOf_MavjudKalit_TogriBosqich(string key, string label)
    {
        var stage = CareerService.StageOf(key);

        Assert.Equal(key, stage.Key);
        Assert.Equal(label, stage.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nomalum")]
    [InlineData("NEW")] // registr muhim — katta harfli kalit topilmaydi
    public void StageOf_NomalumKalit_BirinchiBosqich(string? key)
    {
        // Eski/buzuq yozuv sabab UI qulab qolmasligi uchun "new" ga tushiladi.
        Assert.Equal(CareerService.StatusNew, CareerService.StageOf(key).Key);
    }

    [Theory]
    [InlineData("new", true)]
    [InlineData("review", true)]
    [InlineData("interview", true)]
    [InlineData("trial", true)]
    [InlineData("test", true)]
    [InlineData("director", true)]
    [InlineData("hired", true)]
    [InlineData("rejected", true)]
    [InlineData("deleted", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("New", false)]
    public void IsValidStatus_FaqatKatalogKalitlari(string? key, bool kutilgan)
    {
        Assert.Equal(kutilgan, CareerService.IsValidStatus(key));
    }

    /* =========================================================================================
     *  VORONKA KONVERSIYASI — ComputeFunnel
     * ========================================================================================= */

    private static CareerService.FunnelInput A(string status, params string[] history) => new(status, history);

    private static CareerService.FunnelStep Qadam(List<CareerService.FunnelStep> f, string key) =>
        f.Single(s => s.Key == key);

    [Fact]
    public void ComputeFunnel_BoshRoyxat_HammasiNol()
    {
        var f = CareerService.ComputeFunnel([]);

        Assert.Equal(CareerService.FunnelStages.Count, f.Count);
        Assert.All(f, s => Assert.Equal(0, s.Reached));
        Assert.All(f, s => Assert.Equal(0, s.FromStartPct));
        Assert.Equal(0, f[0].FromPrevPct);
    }

    [Fact]
    public void ComputeFunnel_YetibBorganlar_VaKonversiya()
    {
        var f = CareerService.ComputeFunnel(
        [
            A("new", "new"),
            A("new", "new"),
            A("test", "new", "test"),
            A("trial", "new", "test", "trial"),
            A("hired", "new", "test", "trial", "interview", "director", "hired"),
        ]);

        Assert.Equal(5, Qadam(f, "new").Reached);
        Assert.Equal(3, Qadam(f, "test").Reached);
        Assert.Equal(2, Qadam(f, "trial").Reached);
        Assert.Equal(1, Qadam(f, "interview").Reached);
        Assert.Equal(1, Qadam(f, "hired").Reached);

        Assert.Equal(100, Qadam(f, "new").FromPrevPct);
        Assert.Equal(60, Qadam(f, "test").FromPrevPct);   // 3/5
        Assert.Equal(66, Qadam(f, "trial").FromPrevPct);  // 2/3 — pastga yaxlitlanadi
        Assert.Equal(50, Qadam(f, "interview").FromPrevPct);
        Assert.Equal(20, Qadam(f, "hired").FromStartPct);  // 1/5

        Assert.Equal(2, Qadam(f, "new").Current);
        Assert.Equal(1, Qadam(f, "hired").Current);
    }

    [Fact]
    public void ComputeFunnel_RadEtilgan_QayerdaToxtaganiBilanSanaladi()
    {
        // Demo darsdan keyin rad etilgan nomzod: CV, Test, Demo dars — "o'tilgan", HR — yo'q.
        var f = CareerService.ComputeFunnel([A("rejected", "new", "test", "trial", "rejected")]);

        Assert.Equal(1, Qadam(f, "trial").Reached);
        Assert.Equal(0, Qadam(f, "interview").Reached);
        Assert.Equal(1, Qadam(f, "trial").RejectedHere);
        Assert.Equal(0, Qadam(f, "new").RejectedHere);
        Assert.All(f, s => Assert.Equal(0, s.Current)); // rad etilgan hech bir bosqichda "turmaydi"
    }

    [Fact]
    public void ComputeFunnel_BosqichOtkazibYuborilsa_OraliqlarOtilganSanaladi()
    {
        // CV → to'g'ridan-to'g'ri HR suhbati: test va demo dars ham "yetib borilgan" hisoblanadi.
        var f = CareerService.ComputeFunnel([A("interview", "new", "interview")]);

        Assert.Equal(1, Qadam(f, "test").Reached);
        Assert.Equal(1, Qadam(f, "trial").Reached);
        Assert.Equal(1, Qadam(f, "interview").Reached);
        Assert.Equal(0, Qadam(f, "director").Reached);
    }

    [Fact]
    public void ComputeFunnel_EskiReview_CVBosqichigaQoshiladi()
    {
        var f = CareerService.ComputeFunnel([A("review", "new", "review")]);

        Assert.Equal(1, Qadam(f, "new").Reached);
        Assert.Equal(1, Qadam(f, "new").Current);
        Assert.Equal(0, Qadam(f, "test").Reached);
        Assert.DoesNotContain(f, s => s.Key == "review");
    }

    [Fact]
    public void ComputeFunnel_TarixsizVaNomalumBosqich_KamidaCVgaYetgan()
    {
        // Buzuq/eski yozuv (tarixsiz, noma'lum bosqich) ham jami sonda — birinchi bosqichda.
        var f = CareerService.ComputeFunnel([A("nomalum"), A("rejected")]);

        Assert.Equal(2, Qadam(f, "new").Reached);
        Assert.Equal(1, Qadam(f, "new").RejectedHere);
        Assert.Equal(0, Qadam(f, "test").Reached);
    }

    /* =========================================================================================
     *  VAKANSIYA HOLATI — ON/OFF
     * ========================================================================================= */

    [Theory]
    [InlineData("active", true)]
    [InlineData("paused", true)]
    [InlineData("archived", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Active", false)]
    [InlineData("deleted", false)]
    public void IsValidVacancyStatus_FaqatUchHolat(string? status, bool kutilgan)
    {
        Assert.Equal(kutilgan, CareerService.IsValidVacancyStatus(status));
    }

    [Fact]
    public void VacancyHolatlari_EskiQiymatlarOzgarmagan()
    {
        // Bazadagi vakansiyalar "active"/"archived" bilan yozilgan — qiymatlar o'zgarmasin.
        Assert.Equal("active", CareerService.VacancyActive);
        Assert.Equal("archived", CareerService.VacancyArchived);
        Assert.Equal("paused", CareerService.VacancyPaused);
    }

    /* =========================================================================================
     *  Esc — Telegram HTML parse_mode uchun ekranlash
     * ========================================================================================= */

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Oddiy matn", "Oddiy matn")]
    [InlineData("<b>qalin</b>", "&lt;b&gt;qalin&lt;/b&gt;")]
    [InlineData("Ali & Vali", "Ali &amp; Vali")]
    [InlineData("1 < 2 > 0", "1 &lt; 2 &gt; 0")]
    public void Esc_HtmlBelgilariEkranlanadi(string? input, string kutilgan)
    {
        Assert.Equal(kutilgan, CareerService.Esc(input));
    }

    [Fact]
    public void Esc_AmpersandBirinchiEkranlanadi()
    {
        // Tartib muhim: avval "&", keyin "<"/">" — aks holda "&lt;" ikki marta ekranlanardi
        // ("&amp;amp;lt;"). Nomzod o'z ismiga "<script>" yozsa ham xabar buzilmasin.
        Assert.Equal("&amp;lt;", CareerService.Esc("&lt;"));
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;", CareerService.Esc("<script>alert(1)</script>"));
    }

    /* =========================================================================================
     *  BuildApplication
     * ========================================================================================= */

    private static Vacancy Vakansiya() => new()
    {
        Id = "vac-1",
        Title = "Ingliz tili o'qituvchisi",
        Department = "O'quv bo'limi",
    };

    [Fact]
    public void BuildApplication_MaydonlarTogriKochadi()
    {
        var app = CareerService.BuildApplication(
            Vakansiya(), chatId: 55501, tgUsername: "ali_v", fullName: "Valiyev Ali",
            phone: "+998-90-123-45-67", experience: "3 yil", motivation: "Bolalar bilan ishlashni yoqtiraman",
            cvUrl: "/uploads/abc.pdf", cvName: "cv.pdf", number: 7);

        Assert.Equal(7, app.Number);
        Assert.Equal("vac-1", app.VacancyId);
        Assert.Equal("Ingliz tili o'qituvchisi", app.VacancyTitle);
        Assert.Equal(55501, app.ChatId);
        Assert.Equal("ali_v", app.TgUsername);
        Assert.Equal("Valiyev Ali", app.FullName);
        Assert.Equal("+998-90-123-45-67", app.Phone);
        Assert.Equal("3 yil", app.Experience);
        Assert.Equal("Bolalar bilan ishlashni yoqtiraman", app.Motivation);
        Assert.Equal("/uploads/abc.pdf", app.CvUrl);
        Assert.Equal("cv.pdf", app.CvName);
    }

    [Fact]
    public void BuildApplication_BoshlangichBosqich_New()
    {
        var app = CareerService.BuildApplication(
            Vakansiya(), 1, "u", "F.I.Sh", "+998901234567", "", "", "", "", 1);

        Assert.Equal(CareerService.StatusNew, app.Status);
        Assert.Equal("Nomzod", app.StatusChangedBy);
        Assert.True(CareerService.IsValidStatus(app.Status));
    }

    [Fact]
    public void BuildApplication_VaqtMarkazMintaqasida_VaBirXil()
    {
        // AppClock statik — kutilgan qiymatni NISBIY tekshiramiz (sana qattiq yozilmaydi).
        var oldin = AppClock.Now.AddSeconds(-2);

        var app = CareerService.BuildApplication(
            Vakansiya(), 1, "u", "F.I.Sh", "+998901234567", "", "", "", "", 1);

        var keyin = AppClock.Now.AddSeconds(2);

        Assert.Equal(app.StatusChangedAt, app.CreatedAt);
        var parsed = DateTime.Parse(app.CreatedAt, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(parsed, oldin, keyin);
    }

    [Fact]
    public void BuildApplication_AdminIzohiBosh()
    {
        // AdminNote — faqat ichki eslatma; ariza yaratilganda hech qachon to'lmaydi.
        var app = CareerService.BuildApplication(
            Vakansiya(), 1, "u", "F.I.Sh", "+998901234567", "", "", "", "", 1);

        Assert.Equal("", app.AdminNote);
        Assert.Equal("", app.StatusNote);
    }

    /* =========================================================================================
     *  NextNumberAsync — ketma-ket ariza raqami (haqiqiy baza ustida)
     * ========================================================================================= */

    [Fact]
    public async Task NextNumberAsync_BoshBaza_BirQaytaradi()
    {
        using var db = TestDb.Sqlite();

        Assert.Equal(1, await CareerService.NextNumberAsync(db.Context));
    }

    [Fact]
    public async Task NextNumberAsync_UchtaAriza_TortQaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        foreach (var n in new[] { 1, 2, 3 })
            ctx.JobApplications.Add(CareerService.BuildApplication(
                Vakansiya(), 100 + n, "u", $"Nomzod {n}", "+998901234567", "", "", "", "", n));
        await ctx.SaveChangesAsync();

        Assert.Equal(4, await CareerService.NextNumberAsync(ctx));
    }

    [Fact]
    public async Task NextNumberAsync_OraliqOchirilgan_EngKattadanKeyingisi()
    {
        // #3 o'chirilgan bo'lsa ham raqam QAYTA ishlatilmaydi (maksimumdan keyingisi).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        foreach (var n in new[] { 1, 2, 5 })
            ctx.JobApplications.Add(CareerService.BuildApplication(
                Vakansiya(), 100 + n, "u", $"Nomzod {n}", "+998901234567", "", "", "", "", n));
        await ctx.SaveChangesAsync();

        Assert.Equal(6, await CareerService.NextNumberAsync(ctx));
    }
}
