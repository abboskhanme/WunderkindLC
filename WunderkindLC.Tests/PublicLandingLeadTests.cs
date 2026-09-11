using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// LANDING SAHIFASINING OMMAVIY LID QABULI (<c>POST /api/public/landing-lead</c>,
/// <c>PublicLandingController</c>) va landing CMS endpointlari
/// (<c>LandingCmsController</c>) qoidalarini qo'riqlaydi.
///
/// <para>Rasmiy manba: <c>.claude/rules/lead-forms.md</c> §4 ("BIR TELEFON = BITTA LID") va §6.5
/// (bosqich), <c>.claude/rules/crm-leads.md</c>, <c>.claude/rules/uploads-security.md</c>.</para>
///
/// <para><b>NEGA IKKI XIL TEST:</b> <c>WunderkindLC.Tests</c> loyihasi <c>WunderkindLC.Server</c>
/// ga referens QILMAYDI (faqat Domain/Application/Infrastructure — qarang
/// <see cref="SensitiveReadPermTests"/>), ya'ni controllerni bu yerdan chaqirib bo'lmaydi va
/// faqat shu test uchun sun'iy referens qo'shilmaydi. Shuning uchun:</para>
/// <list type="bullet">
///   <item><b>XATTI-HARAKAT</b> testlari controller tayanadigan UMUMIY mantiqni haqiqiy baza
///     ustida tekshiradi (<see cref="LeadIntake"/>, <see cref="PhoneUtil"/>, <c>PhoneKey</c>
///     sinxronizatsiyasi) — "dublikat lid ochilmaydi", "bosqich yo'q bo'lsa lid bosqichsiz
///     qoladi" qoidalari AYNAN shu yerda hal bo'ladi;</item>
///   <item><b>DARVOZA</b> testlari controller manba matnini o'qiydi — kimdir avto-xabar
///     darvozasini yoki <c>AdminPerm</c> atributini olib tashlasa test darrov qizaradi.</item>
/// </list>
/// </summary>
public class PublicLandingLeadTests
{
    // ===================== Yordamchilar =====================

    /// <summary>Repo ildizi — <c>WunderkindLC.slnx</c> yotgan papka.</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ControllerSource(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", fileName));

    private static string LandingLead => ControllerSource("PublicLandingController.cs");
    private static string LandingCms => ControllerSource("LandingCmsController.cs");

    /// <summary>Landing arizasi tushadigan lid (controller AYNAN shunday yozadi).</summary>
    private static Lead NewLandingLead(string name = "Aliyev Ali", string phone = "+998901234567",
        string stage = "", string subject = "General English") => new()
    {
        FullName = name,
        Phone = phone,
        Stage = stage,
        Source = "Sayt",
        InterestSubject = subject,
        CreatedAt = "2026-08-18T10:00:00",
    };

    // ===================== 1) YANGI LID: birinchi bosqich + hodisa =====================

    [Fact]
    public async Task Yangi_lid_birinchi_bosqichga_tushadi_va_hodisa_yoziladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Tartib ATAYIN aralash — birinchi bosqich Order bo'yicha tanlanishi kerak.
        db.LeadStages.Add(new LeadStage { Title = "Yo'qotilgan", Color = "red", Order = 5 });
        var first = new LeadStage { Title = "Yangi", Color = "blue", Order = 0 };
        db.LeadStages.Add(first);
        await db.SaveChangesAsync();

        var stageId = await LeadIntake.FirstStageIdAsync(db);
        Assert.Equal(first.Id, stageId);

        var lead = NewLandingLead(stage: stageId);
        db.Leads.Add(lead);
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = "created", ActorName = "Sayt",
            CreatedAt = "2026-08-18T10:00:00", ToStage = stageId,
            Text = "Lid yaratildi (Aliyev Ali)",
        });
        await db.SaveChangesAsync();

        var saved = await db.Leads.SingleAsync();
        Assert.Equal(first.Id, saved.Stage);
        Assert.Equal("Sayt", saved.Source);
        Assert.Equal(0, saved.RepeatCount);

        var ev = await db.LeadEvents.SingleAsync();
        Assert.Equal("created", ev.Type);
        Assert.Equal(stageId, ev.ToStage);
    }

    // ===================== 2) TAKRORIY ARIZA: yangi lid OCHILMAYDI =====================

    [Fact]
    public async Task Takroriy_ariza_yangi_lid_ochmaydi_bosqich_va_manba_ozgarmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var lost = new LeadStage { Title = "Yo'qotilgan", Order = 5 };
        db.LeadStages.AddRange(new LeadStage { Title = "Yangi", Order = 0 }, lost);
        // Odam ilgari Instagram formasidan kelgan va menejer uni "Yo'qotilgan" ga surgan.
        db.Leads.Add(new Lead
        {
            FullName = "Aliyev Ali", Phone = "+998901234567", Stage = lost.Id,
            Source = "Instagram", InterestSubject = "IELTS", CreatedAt = "2026-08-01T09:00:00",
        });
        await db.SaveChangesAsync();

        // Saytda raqam BOSHQA formatda kiritilgan — moslik PhoneKey (oxirgi 9 raqam) bo'yicha.
        var existing = await LeadIntake.FindByPhoneAsync(db, "90 123 45 67");
        Assert.NotNull(existing);

        existing!.RepeatCount++;
        existing.LastRepeatAt = "2026-08-18T10:00:00";
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = existing.Id, Type = "repeat_intake", ActorName = "Sayt",
            CreatedAt = "2026-08-18T10:00:00", ToStage = existing.Stage,
            Text = "Saytdan qayta ariza keldi (IELTS)",
        });
        await db.SaveChangesAsync();

        // Lid BITTA qoladi, bosqichi va manbasi o'zgarmaydi (first-touch).
        var lead = await db.Leads.SingleAsync();
        Assert.Equal(1, lead.RepeatCount);
        Assert.Equal(lost.Id, lead.Stage);
        Assert.Equal("Instagram", lead.Source);
        Assert.Equal("IELTS", lead.InterestSubject);
    }

    /// <summary>Controller takroriy arizada AYNAN shu shart bilan ishlashi kerak: fan FAQAT
    /// lidda umuman yo'q bo'lsa VA mijoz haqiqatan tanlagan bo'lsa to'ldiriladi.</summary>
    [Fact]
    public void Takroriy_arizada_fan_shartsiz_ustidan_yozilmaydi()
    {
        var src = LandingLead;
        // "General English" sukut qiymati FAQAT yangi lid uchun ishlatiladi.
        Assert.DoesNotContain("lead.InterestSubject = subject;", src);
        Assert.Matches(
            new Regex(@"string\.IsNullOrWhiteSpace\(lead\.InterestSubject\)\s*&&\s*chosenSubject\.Length\s*>\s*0\s*\)\s*\r?\n\s*lead\.InterestSubject",
                RegexOptions.Multiline),
            src);
    }

    // ===================== 3) AVTO-XABAR faqat YANGI lidga =====================

    [Fact]
    public void Takroriy_lidga_lead_new_avto_xabari_qayta_yuborilmaydi()
    {
        var src = LandingLead;

        // Avto-xabar `isNewLead` darvozasi ostida bo'lishi SHART (lead-forms.md §4).
        Assert.Matches(
            new Regex(@"if\s*\(isNewLead\)\s*\r?\n\s*await\s+autoMsg\.DispatchLeadAsync\(", RegexOptions.Multiline),
            src);

        // Telegram xabarnomasi esa IKKALA holatda ham ketadi — u darvozalanmaydi, lekin
        // "yangimi" bayrog'i bilan chaqiriladi (sarlavha to'g'ri chiqsin).
        Assert.Contains("isNewLead: isNewLead", src);
        Assert.Single(Regex.Matches(src, @"DispatchLeadAsync\("));
    }

    // ===================== 4) BOSQICH YO'Q — ustun YARATILMAYDI =====================

    [Fact]
    public async Task Bosqich_umuman_bolmasa_lid_bosqichsiz_qoladi_va_ustun_yaratilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        var stageId = await LeadIntake.FirstStageIdAsync(db);
        Assert.Equal("", stageId);

        db.Leads.Add(NewLandingLead(stage: stageId));
        await db.SaveChangesAsync();

        // ⚠️ Sun'iy "Yangi" ustun YASALMAYDI — ommaviy endpoint kanbanni o'zgartirmaydi.
        Assert.Empty(db.LeadStages);
        Assert.Equal("", (await db.Leads.SingleAsync()).Stage);
    }

    [Fact]
    public void Controller_LeadStage_yaratmaydi_va_LeadIntake_dan_foydalanadi()
    {
        var src = LandingLead;
        Assert.DoesNotContain("new LeadStage", src);
        Assert.Contains("LeadIntake.FirstStageIdAsync(db)", src);
        Assert.Contains("LeadIntake.FindByPhoneAsync(db", src);
    }

    // ===================== 5) TEKSHIRUVLAR: telefon va ism =====================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    public void Notogri_telefon_qabul_qilinmaydi(string phone)
    {
        var (valid, _, error) = PhoneUtil.Validate(phone);
        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Bosh_ism_va_notogri_telefon_400_qaytaradi()
    {
        var src = LandingLead;
        // Ism bo'sh / juda uzun va telefon xato bo'lganda 400 (BadRequest) — uchala darvoza ham joyida.
        Assert.Contains("if (fullName.Length == 0)", src);
        Assert.Contains("BadRequest(new { message = \"Ism-familiya kiritilishi shart\" })", src);
        Assert.Contains("if (fullName.Length > 100)", src);
        Assert.Contains("PhoneUtil.Validate(p.Phone)", src);
        Assert.Contains("if (!valid)", src);
        Assert.Equal(3, Regex.Matches(src, @"return BadRequest\(").Count);
    }

    // ===================== 6) PhoneKey — dublikat qidiruvi ishlashi uchun =====================

    [Fact]
    public async Task Saytdan_kelgan_lidning_PhoneKey_i_avtomatik_yoziladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Leads.Add(NewLandingLead(phone: "+998 (90) 123-45-67"));
        await db.SaveChangesAsync();

        // PhoneKey qo'lda yozilmaydi — AppDbContext.SaveChanges o'zi hisoblaydi.
        Assert.Equal("901234567", (await db.Leads.SingleAsync()).PhoneKey);
        Assert.NotNull(await LeadIntake.FindByPhoneAsync(db, "998901234567"));
    }

    // ===================== 7) MANBA — LeadSource katalogidagi nom =====================

    [Fact]
    public void Manba_LeadSource_katalogidan_olinadi()
    {
        var src = LandingLead;
        // Ilgari qattiq "sayt" (kichik harf) yozilardi — katalogda esa "Sayt" bor va "Manba"
        // filtri bitta kanalni ikki qator qilib ko'rsatardi.
        Assert.DoesNotContain("Source = \"sayt\"", src);
        Assert.Contains("db.LeadSources", src);
    }

    // ===================== 8) LANDING CMS — ruxsat darvozasi =====================

    [Fact]
    public void Landing_CMS_admin_marshrutlari_AdminPerm_bilan_darvozalangan()
    {
        var src = LandingCms;

        // Yalang [Authorize] QOLMASLIGI kerak: u bilan ISTALGAN tizimga kirgan foydalanuvchi
        // (o'quvchi, ota-ona, o'qituvchi) landing kontentini o'zgartira olardi.
        Assert.DoesNotContain("\n    [Authorize]\n", src);

        // Har bir api/admin/landing/... marshrutidan keyin AdminPerm turishi shart.
        var routes = Regex.Matches(src, @"\[Http(Get|Post|Put|Delete)\(""api/admin/landing/[^""]*""\)\]\s*\r?\n\s*(\[[^\]]+\])");
        Assert.True(routes.Count >= 14, $"api/admin/landing marshrutlari kam topildi: {routes.Count}");
        foreach (Match m in routes)
            Assert.StartsWith("[AdminPerm(", m.Groups[2].Value);

        // Ruxsat kaliti nav (`/admin/landing`) va marshrut darvozasi bilan bir xil.
        Assert.Contains("private const string SectionPerm = \"settings.landing\";", src);

        // Ommaviy endpoint ANONIM bo'lib qoladi.
        Assert.Matches(
            new Regex(@"\[HttpGet\(""api/public/landing-data""\)\]\s*\r?\n\s*\[AllowAnonymous\]", RegexOptions.Multiline),
            src);
    }

    // ===================== 9) LANDING CMS — ommaviy endpointda DDL YO'Q =====================

    [Fact]
    public void Ommaviy_landing_data_da_DDL_bajarilmaydi_va_xato_jim_yutilmaydi()
    {
        var src = LandingCms;

        var start = src.IndexOf("public async Task<IActionResult> GetPublicLandingData()", StringComparison.Ordinal);
        Assert.True(start > 0, "GetPublicLandingData topilmadi");
        var end = src.IndexOf("// ==================== ADMIN MAP & SOCIALS", start, StringComparison.Ordinal);
        Assert.True(end > start, "GetPublicLandingData tanasi topilmadi");

        // Ommaviy (auth'siz) endpoint har so'rovda ALTER TABLE bajarmasin.
        Assert.DoesNotContain("EnsureColumnsAsync", src[start..end]);

        // Jarayon davomida BIR MARTA + xato logga yoziladi (ilgari `catch { }` edi).
        Assert.Contains("private static bool _columnsEnsured;", src);
        Assert.Contains("if (_columnsEnsured) return;", src);
        Assert.Contains("logger.LogWarning(", src);
        Assert.DoesNotContain("catch { }", src);
    }

    // ===================== 10) LANDING CMS — bo'sh qiymat BO'SH saqlanadi =====================

    [Fact]
    public void Socials_saqlashda_qattiq_kodlangan_default_qoyilmaydi()
    {
        var src = LandingCms;

        var start = src.IndexOf("public async Task<IActionResult> UpdateSocials(", StringComparison.Ordinal);
        Assert.True(start > 0, "UpdateSocials topilmadi");
        var end = src.IndexOf("await db.SaveChangesAsync();", start, StringComparison.Ordinal);
        var body = src[start..end];

        // Admin YouTube havolasini o'chirsa u "https://youtube.com" bo'lib qaytmasligi kerak.
        foreach (var def in new[] { "https://youtube.com", "https://facebook.com" })
            Assert.DoesNotContain(def, body);

        // ⚠️ BOSHQA markazning kontaktlari kodda QOLMASIN. Ular ilgari `Default*` konstantalari
        // sifatida turardi va CMS to'ldirilmagan yangi o'rnatishda ommaviy sahifada CHIQARDI.
        foreach (var stale in new[] { "info@intellect.uz", "+998 (90) 344-44-34",
                                       "intellect_kokand", "Asqarali charxiy" })
            Assert.DoesNotContain(stale, src);

        // Sukut qiymatlar FAQAT o'qishda (ommaviy javobda) qo'llanadi.
        Assert.Contains("DefaultYoutubeUrl", src);
        Assert.Contains("youtubeUrl = NormalizeUrl(meta?.YoutubeUrl, DefaultYoutubeUrl)", src);
    }

    // ===================== 11) LANDING CMS — o'qituvchi/sertifikat ZAXIRASI YO'Q =====================
    //
    // Ommaviy javobda (`GET /api/public/landing-data`) o'qituvchilar va sertifikatlar uchun
    // SOXTA namuna qaytarilmaydi: bazada yo'q bo'lsa BO'SH massiv. Sabab: markaz nomidan
    // to'qib chiqarilgan o'qituvchi ismi yoki begona odamning "IELTS 8.5" sertifikatini
    // ommaviy saytda ko'rsatish — yolg'on da'vo. Bo'lim ko'rinishini klient hal qiladi.

    /// <summary>Controller AYNAN shu so'rovni bajaradi (faol + Order bo'yicha).</summary>
    private static async Task<List<LandingTeacher>> PublicTeachersAsync(WunderkindLC.Infrastructure.Data.AppDbContext db) =>
        await db.LandingTeachers.Where(t => t.IsActive).OrderBy(t => t.Order).ToListAsync();

    private static async Task<List<LandingCertificate>> PublicCertificatesAsync(WunderkindLC.Infrastructure.Data.AppDbContext db) =>
        await db.LandingCertificates.Where(c => c.IsActive).OrderBy(c => c.Order).ToListAsync();

    [Fact]
    public async Task Bazada_oqituvchi_yoq_bolsa_royxat_BOSH_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        Assert.Empty(await PublicTeachersAsync(db));
    }

    [Fact]
    public async Task Bazada_sertifikat_yoq_bolsa_royxat_BOSH_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        Assert.Empty(await PublicCertificatesAsync(db));
    }

    [Fact]
    public async Task Faol_oqituvchilar_Order_boyicha_qaytadi_va_ochirilgani_qaytmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Tartib ATAYIN teskari kiritiladi — javob Order bo'yicha kelishi kerak.
        db.LandingTeachers.Add(new LandingTeacher { FullName = "Ikkinchi", Order = 2, IsActive = true });
        db.LandingTeachers.Add(new LandingTeacher { FullName = "Birinchi", Order = 1, IsActive = true });
        db.LandingTeachers.Add(new LandingTeacher { FullName = "Yashirilgan", Order = 0, IsActive = false });
        await db.SaveChangesAsync();

        var list = await PublicTeachersAsync(db);

        Assert.Equal(new[] { "Birinchi", "Ikkinchi" }, list.Select(x => x.FullName).ToArray());
        // IsActive=false bo'lgani javobga TUSHMAYDI (u Order bo'yicha birinchi bo'lsa ham).
        Assert.DoesNotContain(list, x => x.FullName == "Yashirilgan");
    }

    [Fact]
    public async Task Faol_sertifikatlar_Order_boyicha_qaytadi_va_ochirilgani_qaytmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LandingCertificates.Add(new LandingCertificate { Title = "Ikkinchi", Order = 2, IsActive = true });
        db.LandingCertificates.Add(new LandingCertificate { Title = "Birinchi", Order = 1, IsActive = true });
        db.LandingCertificates.Add(new LandingCertificate { Title = "Yashirilgan", Order = 0, IsActive = false });
        await db.SaveChangesAsync();

        var list = await PublicCertificatesAsync(db);

        Assert.Equal(new[] { "Birinchi", "Ikkinchi" }, list.Select(x => x.Title).ToArray());
        Assert.DoesNotContain(list, x => x.Title == "Yashirilgan");
    }

    // ===================== 12) DARVOZA: zaxira kodi kodga QAYTMASIN =====================

    [Fact]
    public void Ommaviy_javobda_oqituvchi_va_sertifikat_zaxirasi_YOQ()
    {
        var src = LandingCms;

        // Metodlarning O'ZI ham, chaqiruvi ham qolmasligi kerak.
        Assert.DoesNotContain("GetDefaultTeachers", src);
        Assert.DoesNotContain("GetDefaultCertificates", src);

        // Soxta yozuvlarning izlari (hech qachon bazaga yozilmagan "t1"/"c1" IDlar va
        // `/img/...` rasm manzillari) ham qolmasin.
        Assert.DoesNotContain("/img/teachers/", src);
        Assert.DoesNotContain("/img/certificates/", src);
        Assert.DoesNotContain("Id = \"t1\"", src);
        Assert.DoesNotContain("Id = \"c1\"", src);

        // Ommaviy endpoint tanasida `teachers`/`certificates` uchun "bo'sh bo'lsa to'ldir"
        // sharti qolmagan.
        var start = src.IndexOf("public async Task<IActionResult> GetPublicLandingData()", StringComparison.Ordinal);
        Assert.True(start > 0, "GetPublicLandingData topilmadi");
        var end = src.IndexOf("// ==================== ADMIN MAP & SOCIALS", start, StringComparison.Ordinal);
        var body = src[start..end];
        Assert.DoesNotContain("teachers.Count == 0", body);
        Assert.DoesNotContain("certificates.Count == 0", body);
    }

    [Fact]
    public void FAQ_zaxirasi_ATAYIN_qoladi()
    {
        var src = LandingCms;

        // landing.html da FAQ uchun statik markup zaxira sifatida turibdi — bu zaxira
        // ATAYIN saqlanadi (u markaz haqidagi umumiy javoblar, shaxsiy da'vo emas).
        Assert.Contains("private static List<LandingFaq> GetDefaultFaqs()", src);
        Assert.Contains("faqs = GetDefaultFaqs();", src);
    }
}
