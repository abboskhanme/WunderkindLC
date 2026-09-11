using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// LID FORMALARI testlari (<see cref="LeadFormService"/>). Rasmiy manba:
/// <c>.claude/rules/lead-forms.md</c>.
///
/// <para>Qamrab olingan QOIDALAR: manba formadan olinadi · bir telefon = bitta lid (dublikat
/// yaratilmaydi va MANBA o'zgarmaydi) · majburiy savol bo'sh bo'lsa ariza qabul qilinmaydi ·
/// variantli savolga BEGONA javob o'tmaydi · faol bo'lmagan forma ochilmaydi · konversiya foizi
/// takrorsiz lidlar bo'yicha hisoblanadi.</para>
/// </summary>
public class LeadFormsTests
{
    // ===================== Yordamchilar =====================

    private static LeadForm NewForm(string source = "Instagram", bool active = true) => new()
    {
        Title = "Instagram — bepul sinov darsi",
        Slug = "instagram-test",
        Source = source,
        IsActive = active,
        CreatedAt = "2026-08-06T10:00:00",
    };

    private static LeadFormSubmitRequest Req(
        string name = "Aliyev Ali", string phone = "+998901234567",
        Dictionary<string, List<string>>? answers = null, string? refTag = null,
        string? course = null) =>
        new(name, phone, null, 0, course, answers, refTag);

    // ===================== 1) Manba formadan olinadi =====================

    [Fact]
    public async Task Yangi_ariza_lid_yaratadi_va_manba_formadan_olinadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        db.LeadStages.Add(new LeadStage { Title = "Yangi", Order = 0 });
        await db.SaveChangesAsync();

        var (result, error) = await LeadFormService.SubmitAsync(db, "instagram-test", Req());

        Assert.Null(error);
        Assert.NotNull(result);

        var lead = await db.Leads.SingleAsync();
        Assert.Equal("Instagram", lead.Source);
        Assert.Equal("Aliyev Ali", lead.FullName);
        Assert.Contains("Forma: Instagram — bepul sinov darsi", lead.Note);

        var sub = await db.LeadFormSubmissions.SingleAsync();
        Assert.True(sub.IsNewLead);
        Assert.Equal(lead.Id, sub.LeadId);
    }

    [Fact]
    public async Task Faol_bolmagan_forma_ochilmaydi_va_ariza_qabul_qilinmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm(active: false));
        await db.SaveChangesAsync();

        Assert.Null(await LeadFormService.GetPublicAsync(db, "instagram-test"));

        var (result, error) = await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        // Ikkalasi ham null = "forma topilmadi" (controller 404 qaytaradi).
        Assert.Null(result);
        Assert.Null(error);
        Assert.Empty(db.Leads);
    }

    // ===================== 2) Bir telefon = bitta lid =====================

    [Fact]
    public async Task Ayni_telefon_bilan_takroriy_ariza_YANGI_lid_ochmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        // Xuddi shu raqam boshqa formatda — solishtirish oxirgi 9 raqam bo'yicha.
        await LeadFormService.SubmitAsync(db, "instagram-test", Req(phone: "901234567"));

        Assert.Single(db.Leads);
        Assert.Equal(2, await db.LeadFormSubmissions.CountAsync());
        var subs = await db.LeadFormSubmissions.OrderBy(s => s.CreatedAt).ToListAsync();
        Assert.Contains(subs, s => !s.IsNewLead); // ikkinchisi takroriy deb belgilangan
    }

    [Fact]
    public async Task Takroriy_ariza_MAVJUD_lidning_manbasini_ozgartirmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        db.LeadForms.Add(new LeadForm
        {
            Title = "Facebook — kurslar", Slug = "facebook-test", Source = "Facebook",
            IsActive = true, CreatedAt = "2026-08-06T10:00:00",
        });
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        await LeadFormService.SubmitAsync(db, "facebook-test", Req());

        var lead = await db.Leads.SingleAsync();
        // First-touch: odamni BIRINCHI qaysi kanal olib kelgani saqlanadi.
        Assert.Equal("Instagram", lead.Source);
    }

    // ===================== 3) Tekshiruvlar =====================

    [Fact]
    public async Task Majburiy_qoshimcha_savol_bosh_bolsa_ariza_qabul_qilinmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        db.LeadForms.Add(form);
        db.LeadFormFields.Add(new LeadFormField
        {
            FormId = form.Id, Label = "Qaysi vaqtda qulay?", Kind = LeadFormService.KindRadio,
            Options = new() { "Ertalab", "Kechqurun" }, Required = true, Order = 0,
        });
        await db.SaveChangesAsync();

        var (result, error) = await LeadFormService.SubmitAsync(db, "instagram-test", Req());

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("Qaysi vaqtda qulay?", error);
        // Xato bo'lsa hech narsa saqlanmaydi — yarim holatdagi lid qolib ketmasin.
        Assert.Empty(db.Leads);
        Assert.Empty(db.LeadFormSubmissions);
    }

    [Fact]
    public async Task Notogri_telefon_rad_etiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        await db.SaveChangesAsync();

        var (result, error) = await LeadFormService.SubmitAsync(db, "instagram-test", Req(phone: "123"));

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Empty(db.Leads);
    }

    [Fact]
    public async Task Variantli_savolga_BEGONA_javob_otmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        db.LeadForms.Add(form);
        var field = new LeadFormField
        {
            FormId = form.Id, Label = "Vaqt", Kind = LeadFormService.KindRadio,
            Options = new() { "Ertalab", "Kechqurun" }, Required = false, Order = 0,
        };
        db.LeadFormFields.Add(field);
        await db.SaveChangesAsync();

        var (result, error) = await LeadFormService.SubmitAsync(
            db, "instagram-test",
            Req(answers: new() { [field.Id] = new() { "<script>hack</script>" } }));

        Assert.Null(error);
        Assert.NotNull(result);
        var sub = await db.LeadFormSubmissions.SingleAsync();
        var answers = LeadFormService.ParseAnswers(sub.AnswersJson);
        Assert.Single(answers);
        Assert.Empty(answers[0].Answers); // begona qiymat tashlab yuborilgan
    }

    [Fact]
    public async Task Bitta_tanlovli_savolda_faqat_BIRINCHI_javob_qoladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        db.LeadForms.Add(form);
        var field = new LeadFormField
        {
            FormId = form.Id, Label = "Vaqt", Kind = LeadFormService.KindRadio,
            Options = new() { "Ertalab", "Kechqurun" }, Order = 0,
        };
        db.LeadFormFields.Add(field);
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(
            db, "instagram-test",
            Req(answers: new() { [field.Id] = new() { "Ertalab", "Kechqurun" } }));

        var answers = LeadFormService.ParseAnswers((await db.LeadFormSubmissions.SingleAsync()).AnswersJson);
        Assert.Equal(new[] { "Ertalab" }, answers[0].Answers);
    }

    // ===================== 3.5) KURS — formaning O'Z variantlaridan =====================

    /// <summary>
    /// Kurs markazdagi <c>Subject</c> katalogidan OLINMAYDI: variantlar formaning o'zida yoziladi
    /// (<see cref="LeadForm.CourseOptions"/>). Mijoz tanlagani lidning "qiziqqan kursi" bo'ladi.
    /// </summary>
    [Fact]
    public async Task Kurs_formaning_OZ_variantlaridan_olinadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        form.AskCourse = true;
        form.CourseName = "Bepul sinov darsi";
        form.CourseOptions = new() { "Ingliz tili", "Matematika" };
        db.LeadForms.Add(form);
        // Markazda BOSHQA nomli kurs bor — forma unga qaramaydi.
        db.Subjects.Add(new Subject { Name = "Rus tili" });
        await db.SaveChangesAsync();

        var pub = await LeadFormService.GetPublicAsync(db, "instagram-test");
        Assert.NotNull(pub);
        Assert.True(pub!.AskCourse);
        Assert.Equal(new[] { "Ingliz tili", "Matematika" }, pub.Courses);

        var (result, error) = await LeadFormService.SubmitAsync(
            db, "instagram-test", Req(course: "ingliz tili")); // registr farqi ahamiyatsiz
        Assert.Null(error);
        Assert.NotNull(result);

        var lead = await db.Leads.SingleAsync();
        Assert.Equal("Ingliz tili", lead.InterestSubject);
        Assert.Equal("Ingliz tili", (await db.LeadFormSubmissions.SingleAsync()).CourseName);
    }

    /// <summary>Ro'yxatda yo'q kurs (API'ga qo'lda yuborilgan) jim rad etiladi — formaniki qoladi.</summary>
    [Fact]
    public async Task Royxatda_yoq_kurs_qabul_qilinmaydi_va_formaning_kursiga_qaytiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        form.AskCourse = true;
        form.CourseName = "Bepul sinov darsi";
        form.CourseOptions = new() { "Ingliz tili" };
        db.LeadForms.Add(form);
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req(course: "Kosmonavtika"));

        Assert.Equal("Bepul sinov darsi", (await db.Leads.SingleAsync()).InterestSubject);
    }

    /// <summary>Variant yozilmasa savol UMUMAN ko'rsatilmaydi (bo'sh select ma'nosiz).</summary>
    [Fact]
    public async Task Variantsiz_kurs_savoli_ommaviy_formada_korsatilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        form.AskCourse = true;
        form.CourseOptions = new();
        db.LeadForms.Add(form);
        await db.SaveChangesAsync();

        var pub = await LeadFormService.GetPublicAsync(db, "instagram-test");
        Assert.NotNull(pub);
        Assert.False(pub!.AskCourse);
        Assert.Empty(pub.Courses);
    }

    /// <summary>Takror va bo'sh variantlar tozalanadi, ADMIN yozgan TARTIB saqlanadi.</summary>
    [Fact]
    public void Kurs_variantlari_tozalanadi_va_tartibi_saqlanadi()
    {
        var clean = LeadFormService.CleanCourseOptions(
            new[] { " Ingliz tili ", "", "ingliz TILI", "Matematika", null! });
        Assert.Equal(new[] { "Ingliz tili", "Matematika" }, clean);
    }

    // ===================== 4) Maydonlarni yozish qoidalari =====================

    [Fact]
    public async Task Variantsiz_qolgan_royxat_maydoni_oddiy_matnga_tushadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        db.LeadForms.Add(form);
        LeadFormService.WriteFields(db, form.Id, new()
        {
            new(null, "Izoh", LeadFormService.KindSelect, new(), null, false),
            new(null, "   ", LeadFormService.KindText, null, null, false), // yorliqsiz — tashlanadi
        });
        await db.SaveChangesAsync();

        var field = await db.LeadFormFields.SingleAsync();
        Assert.Equal(LeadFormService.KindText, field.Kind);
        Assert.Empty(field.Options);
    }

    // ===================== 5) Sub-kanal belgisi (?ref=) =====================

    [Theory]
    [InlineData("Story", "story")]
    [InlineData("bio link!", "biolink")]
    [InlineData(null, "")]
    public void Ref_belgisi_tozalanadi(string? raw, string expected) =>
        Assert.Equal(expected, LeadFormService.NormalizeRef(raw));

    // ===================== 6) Statistika =====================

    [Fact]
    public async Task Konversiya_foizi_TAKRORSIZ_lidlar_boyicha_hisoblanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = NewForm();
        form.Views = 10;
        db.LeadForms.Add(form);
        await db.SaveChangesAsync();

        // Bitta odam formani IKKI marta to'ldirdi → 2 ariza, 1 lid.
        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        await LeadFormService.SubmitAsync(db, "instagram-test", Req());

        // O'sha lid o'quvchiga aylandi va faol guruhda.
        var lead = await db.Leads.SingleAsync();
        var student = new Student { FullName = "Aliyev Ali" };
        db.Students.Add(student);
        lead.ConvertedStudentId = student.Id;
        var group = new Group { Name = "IELTS-1" };
        db.Classes.Add(group);
        db.StudentGroups.Add(new StudentGroup
        {
            StudentId = student.Id, GroupId = group.Id, IsActive = true, Status = "active",
        });
        await db.SaveChangesAsync();

        var stats = await LeadFormService.BuildStatsAsync(db);
        var row = Assert.Single(stats.ByForm);

        Assert.Equal(2, row.Submissions);
        Assert.Equal(1, row.NewLeads);
        Assert.Equal(1, row.Converted);
        Assert.Equal(1, row.ActiveStudents);
        // 1 lid / 1 lid = 100% (arizalar bo'yicha hisoblansa 50% chiqib, forma yomon ko'rinardi).
        Assert.Equal(100, row.ConvertRate);
        // 2 ariza / 10 ochilish
        Assert.Equal(20, row.SubmitRate);
    }

    // ===================== 6.5) BOSQICH va TO'LOV (sotuv konversiyasi) =====================

    /// <summary>
    /// Arizalar ro'yxatida lidning KANBAN BOSQICHI va TO'LOVI ko'rinadi — "o'quvchi bo'ldi" hali
    /// pul degani emas, sotuv konversiyasi aynan shundan aniqlanadi.
    /// </summary>
    [Fact]
    public async Task Ariza_royxatida_lid_bosqichi_va_tolovi_korinadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        var stage = new LeadStage { Title = "Sinov darsi", Color = "amber", Order = 1 };
        db.LeadStages.Add(stage);
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());

        var lead = await db.Leads.SingleAsync();
        lead.Stage = stage.Id;
        var student = new Student { FullName = "Aliyev Ali" };
        db.Students.Add(student);
        lead.ConvertedStudentId = student.Id;
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "income", Category = "tuition",
            Amount = 500_000m, Date = "2026-08-01",
        });
        await db.SaveChangesAsync();

        var subs = await db.LeadFormSubmissions.ToListAsync();
        var rows = await LeadFormService.BuildSubmissionsAsync(
            db, subs, new Dictionary<string, string>());
        var row = Assert.Single(rows);

        Assert.Equal("Sinov darsi", row.StageTitle);
        Assert.Equal("amber", row.StageColor);
        Assert.True(row.Paid);
        Assert.Equal(500_000m, row.PaidTotal);
        Assert.Equal("2026-08-01", row.FirstPaidAt);
    }

    /// <summary>
    /// VOZVRAT to'lovdan ayiriladi: puli to'liq qaytarilgan odam "to'ladi" deb sanalmaydi
    /// (aks holda sotuv hisoboti bo'lmagan daromadni ko'rsatardi).
    /// </summary>
    [Fact]
    public async Task Vozvrat_tolovdan_ayiriladi_va_toliq_qaytarilgan_lid_tolamagan_hisoblanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        await db.SaveChangesAsync();
        await LeadFormService.SubmitAsync(db, "instagram-test", Req());

        var lead = await db.Leads.SingleAsync();
        var student = new Student { FullName = "Aliyev Ali" };
        db.Students.Add(student);
        lead.ConvertedStudentId = student.Id;
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "income", Category = "tuition",
            Amount = 300_000m, Date = "2026-08-01",
        });
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "expense", Category = "refund",
            Amount = 300_000m, Date = "2026-08-05",
        });
        await db.SaveChangesAsync();

        var stats = await LeadFormService.BuildStatsAsync(db);
        var row = Assert.Single(stats.ByForm);

        Assert.Equal(1, row.Converted);  // o'quvchi bo'lgan
        Assert.Equal(0, row.Paid);       // lekin puli qaytarilgan
        Assert.Equal(0m, row.Revenue);
        Assert.Equal(0, row.PayRate);
    }

    /// <summary>Sotuv konversiyasi (`PayRate`) — TAKRORSIZ lidlar bo'yicha, arizalar bo'yicha emas.</summary>
    [Fact]
    public async Task Sotuv_konversiyasi_takrorsiz_lidlar_boyicha_hisoblanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        await db.SaveChangesAsync();

        // Bitta odam ikki marta to'ldirdi (1 lid), ikkinchi odam bir marta (1 lid).
        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        await LeadFormService.SubmitAsync(db, "instagram-test", Req(name: "Valiyev Vali", phone: "+998907654321"));

        // Faqat BIRINCHI odam to'ladi → 1/2 = 50%.
        var payer = await db.Leads.OrderBy(l => l.CreatedAt).FirstAsync();
        var student = new Student { FullName = payer.FullName };
        db.Students.Add(student);
        payer.ConvertedStudentId = student.Id;
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "income", Category = "tuition",
            Amount = 250_000m, Date = "2026-08-02",
        });
        await db.SaveChangesAsync();

        var stats = await LeadFormService.BuildStatsAsync(db);
        var row = Assert.Single(stats.ByForm);

        Assert.Equal(3, row.Submissions);
        Assert.Equal(1, row.Paid);
        Assert.Equal(250_000m, row.Revenue);  // 3 ariza bo'lsa ham summa BIR marta
        Assert.Equal(50, row.PayRate);
        Assert.Equal(1, stats.Paid);
    }

    /// <summary>
    /// Bosqichlar kesimi lidlarni HOZIRGI ustuni bo'yicha guruhlaydi.
    /// ⚠️ Yangi lid avtomatik BIRINCHI bosqichga tushadi (<c>LeadIntake.FirstStageIdAsync</c>) —
    /// ya'ni formadan kelgan lid voronkada darhol ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Bosqichlar_kesimi_lidlarni_hozirgi_ustuni_boyicha_guruhlaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        var yangi = new LeadStage { Title = "Yangi", Color = "slate", Order = 0 };
        var sinov = new LeadStage { Title = "Sinov darsi", Color = "amber", Order = 1 };
        db.LeadStages.AddRange(yangi, sinov);
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        await LeadFormService.SubmitAsync(db, "instagram-test", Req(name: "Valiyev Vali", phone: "+998907654321"));

        // Ikkalasi ham "Yangi" da; bittasi keyingi bosqichga ko'chirildi.
        Assert.Equal(2, await db.Leads.CountAsync(l => l.Stage == yangi.Id));
        var moved = await db.Leads.OrderBy(l => l.CreatedAt).FirstAsync();
        moved.Stage = sinov.Id;
        await db.SaveChangesAsync();

        var stats = await LeadFormService.BuildStatsAsync(db);
        Assert.Equal(2, stats.ByStage.Count);
        Assert.Equal(1, stats.ByStage.Single(x => x.Stage == "Yangi").Leads);
        var s2 = stats.ByStage.Single(x => x.Stage == "Sinov darsi");
        Assert.Equal("amber", s2.Color);
        Assert.Equal(1, s2.Leads);
    }

    /// <summary>
    /// Ustun O'CHIRILGAN bo'lsa lid bosqichsiz qoladi va kesimga KIRMAYDI — kanbanda ham
    /// ko'rinmaydi, ya'ni sun'iy "Noma'lum bosqich" qatori yasalmaydi.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_ustundagi_lid_bosqichlar_kesimiga_kirmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        var stage = new LeadStage { Title = "Yangi", Color = "slate", Order = 0 };
        db.LeadStages.Add(stage);
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        db.LeadStages.Remove(stage);           // ustun o'chirildi, lidda eski id qoldi
        await db.SaveChangesAsync();

        var stats = await LeadFormService.BuildStatsAsync(db);
        Assert.Empty(stats.ByStage);

        var rows = await LeadFormService.BuildSubmissionsAsync(
            db, await db.LeadFormSubmissions.ToListAsync(), new Dictionary<string, string>());
        Assert.Equal("", Assert.Single(rows).StageTitle);
    }

    // ===================== 7) Audit bo'limi =====================

    [Fact]
    public void LeadForm_yozuvlari_Lidlar_bolimida_korinadi() =>
        Assert.Equal("leads", AuditSections.SectionOf("LeadForm"));

    // ===================== 8) Takroriy murojaat belgisi =====================

    [Fact]
    public async Task Takroriy_ariza_lidga_TAKRORIY_belgisini_qoyadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(NewForm());
        await db.SaveChangesAsync();

        await LeadFormService.SubmitAsync(db, "instagram-test", Req());
        var first = await db.Leads.SingleAsync();
        // Birinchi murojaat TAKROR sanalmaydi.
        Assert.Equal(0, first.RepeatCount);
        Assert.Equal("", first.LastRepeatAt);

        await LeadFormService.SubmitAsync(db, "instagram-test", Req(phone: "901234567"));
        await LeadFormService.SubmitAsync(db, "instagram-test", Req(phone: "+998 90 123 45 67"));

        var lead = await db.Leads.SingleAsync();
        Assert.Equal(2, lead.RepeatCount);
        Assert.NotEqual("", lead.LastRepeatAt);
    }

    // ===================== 9) Telefon kaliti (qidiruv indeksi) =====================

    [Fact]
    public async Task Telefon_kaliti_AVTOMATIK_yoziladi_va_tahrirda_yangilanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        // Kalit qo'lda BERILMAYDI — SaveChanges o'zi hisoblaydi (`AppDbContext.SyncLeadPhoneKeys`).
        var lead = new Lead { FullName = "Aliyev Ali", Phone = "+998-90-123-45-67", CreatedAt = "2026-08-06T10:00:00" };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        Assert.Equal("901234567", lead.PhoneKey);

        // Telefon o'zgartirilsa kalit ham ko'chadi — aks holda lid qidiruvda ESKI raqamda qolardi.
        lead.Phone = "998911112233";
        await db.SaveChangesAsync();
        Assert.Equal("911112233", lead.PhoneKey);

        // Qidiruv boshqa formatdagi raqam bilan ham topadi.
        Assert.Equal(lead.Id, (await LeadIntake.FindByPhoneAsync(db, "+998 91 111 22 33"))?.Id);
        Assert.Null(await LeadIntake.FindByPhoneAsync(db, "901234567")); // eski raqam — endi yo'q
    }

    [Fact]
    public async Task Chala_telefon_bilan_BEGONA_lidga_biriktirilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Leads.Add(new Lead { FullName = "Aliyev Ali", Phone = "+998901234567", CreatedAt = "2026-08-06T10:00:00" });
        await db.SaveChangesAsync();

        // 7 dan qisqa kalit — umuman qidirilmaydi (tasodifiy moslik bo'lmasin).
        Assert.Null(await LeadIntake.FindByPhoneAsync(db, "4567"));
    }
}
