using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// VORONKA AI tahlili (<see cref="FunnelAiAnalysisService"/>) — "Formalar → Lid statistikasi" va
/// "Formalar → Test statistikasi" sahifalaridagi AI tugmasining backend qismi.
///
/// <para>Bu yerda Gemini CHAQIRILMAYDIGAN yo'llar qoplanadi: turni tekshirish, "kuniga bir marta"
/// darvozasi va DETERMINISTIK raqamlarning to'g'ri yig'ilishi. Aynan shu raqamlar promptga
/// ketadi, ya'ni ular noto'g'ri bo'lsa AI ishonch bilan YOLG'ON xulosa yozadi — shuning uchun
/// tekshiruv sonlar ustida.</para>
///
/// <para>⚠️ Testlarda <c>AppSecrets.Init</c> hech qachon chaqirilmaydi, ya'ni
/// <c>AppSecrets.GeminiApiKey</c> BO'SH — tashqi tarmoq so'rovi hech qanday holatda ketmaydi.</para>
/// </summary>
public class FunnelAiTests
{
    // ===================== Yordamchilar =====================

    /// <summary>Saqlangan tahlilning haqiqiy shakli: <c>{ ai, metrics }</c>.</summary>
    private const string StoredJson = """
        {
          "ai": {
            "umumiy": "Instagram yagona pul keltirayotgan kanal.",
            "kanallar": "", "voronka": "", "sifat": "", "pul": "", "ozgarishlar": "",
            "kuchli": [], "zaif": [], "xavflar": [], "tavsiyalar": [],
            "baholar": { "hajm": 40, "konversiya": 30, "sotuv": 20, "barqarorlik": 50, "umumiy": 35 },
            "trend": "barqaror"
          },
          "metrics": { "kind": "lead-forms", "sources": 1, "activeSources": 1 }
        }
        """;

    /// <summary>Bugungi sana bilan saqlangan tahlil (kuniga bir marta darvozasini sinash uchun).</summary>
    private static FunnelAiAnalysis TodaysRecord(string kind) => new()
    {
        Kind = kind,
        Date = AppClock.Today.ToString("yyyy-MM-dd"),
        CreatedAt = AppClock.Iso(),
        Model = "gemini-test",
        Summary = "Instagram yagona pul keltirayotgan kanal.",
        OverallScore = 35,
        ResultJson = StoredJson,
    };

    /// <summary>Lid formasi + unga N ta ariza (hammasi BITTA lidga — takroriy murojaat).</summary>
    private static LeadForm AddForm(
        Microsoft.EntityFrameworkCore.DbContext db, string title, string slug,
        string source = "Instagram", int views = 0)
    {
        var form = new LeadForm
        {
            Title = title, Slug = slug, Source = source, IsActive = true,
            Views = views, CreatedAt = "2026-08-01T09:00:00",
        };
        db.Add(form);
        return form;
    }

    private static void AddSubmission(
        Microsoft.EntityFrameworkCore.DbContext db, LeadForm form, Lead lead,
        bool isNewLead = true, string date = "2026-08-05T10:00:00")
    {
        db.Add(new LeadFormSubmission
        {
            FormId = form.Id, LeadId = lead.Id, IsNewLead = isNewLead,
            FullName = lead.FullName, Phone = lead.Phone, CreatedAt = date,
        });
    }

    private static Lead AddLead(
        Microsoft.EntityFrameworkCore.DbContext db,
        string name = "Aliyev Ali", string phone = "+998901234567")
    {
        var lead = new Lead { FullName = name, Phone = phone, CreatedAt = "2026-08-05T10:00:00" };
        db.Add(lead);
        return lead;
    }

    // ===================== 1) KUNIGA BIR MARTA — kalitdan OLDIN =====================

    /// <summary>
    /// Bugungi tahlil bor bo'lsa saqlangani qaytadi — API kaliti YO'Q bo'lsa ham.
    ///
    /// <para>⚠️ Tekshiruvlar TARTIBI muhim: agar kalit tekshiruvi oldin turganda, kalit olib
    /// tashlangan (yoki eskirgan) markazda foydalanuvchi bugun YARATILGAN tahlilni ham ko'ra
    /// olmasdi — bo'lmagan xato ko'rinardi. Shu sabab test kalitsiz holatda yoziladi.</para>
    /// </summary>
    [Fact]
    public async Task Bugungi_tahlil_bor_bolsa_kalitsiz_ham_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.FunnelAiAnalyses.Add(TodaysRecord(FunnelAiAnalysisService.KindLeadForms));
        await db.SaveChangesAsync();

        var res = await FunnelAiAnalysisService.GenerateAsync(
            db, null, FunnelAiAnalysisService.KindLeadForms);

        Assert.True(res.Ok);
        Assert.True(res.AlreadyToday);
        Assert.Null(res.Error);
        Assert.NotNull(res.Record);
        Assert.Equal(35, res.Record!.OverallScore);
        Assert.Equal("barqaror", res.Record.Ai.Trend);
        // Yangi yozuv QO'SHILMAYDI (Gemini chaqirilmagani ham shundan bilinadi).
        Assert.Single(db.FunnelAiAnalyses);
    }

    /// <summary>Bir voronkaning bugungi tahlili IKKINCHISIGA tegishli emas — <c>Kind</c> bo'yicha
    /// ajratilgan (aks holda lid tahlili test tahlilini "bugun yaratilgan" deb bloklab qo'yardi).</summary>
    [Fact]
    public async Task Bugungi_tahlil_faqat_OZ_turiga_tegishli()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.FunnelAiAnalyses.Add(TodaysRecord(FunnelAiAnalysisService.KindLeadForms));
        await db.SaveChangesAsync();

        var res = await FunnelAiAnalysisService.GenerateAsync(
            db, null, FunnelAiAnalysisService.KindLevelTests);

        Assert.False(res.AlreadyToday);
        Assert.False(res.Ok);   // kalit yo'q — quyidagi testga qarang
    }

    // ===================== 2) Noto'g'ri tur va kalitsizlik =====================

    [Fact]
    public async Task Notogri_tur_xato_qaytaradi()
    {
        using var t = TestDb.Sqlite();

        var res = await FunnelAiAnalysisService.GenerateAsync(t.Context, null, "boshqa-voronka");

        Assert.False(res.Ok);
        Assert.False(res.AlreadyToday);
        Assert.Null(res.Record);
        Assert.Equal("Noma'lum tahlil turi", res.Error);
        Assert.False(FunnelAiAnalysisService.IsValidKind("boshqa-voronka"));
        Assert.True(FunnelAiAnalysisService.IsValidKind(FunnelAiAnalysisService.KindLeadForms));
        Assert.True(FunnelAiAnalysisService.IsValidKind(FunnelAiAnalysisService.KindLevelTests));
    }

    /// <summary>Kalit sozlanmagan bo'lsa — TUSHUNARLI xato (qayerdan sozlash kerakligi bilan),
    /// yozuv esa saqlanmaydi (yarim holatdagi tahlil tarixda qolib ketmasin).</summary>
    [Fact]
    public async Task Kalit_yoq_bolsa_tushunarli_xato_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;

        var res = await FunnelAiAnalysisService.GenerateAsync(
            db, null, FunnelAiAnalysisService.KindLeadForms);

        Assert.False(res.Ok);
        Assert.False(res.AlreadyToday);
        Assert.Null(res.Record);
        Assert.Contains("Gemini API kaliti sozlanmagan", res.Error);
        Assert.Empty(db.FunnelAiAnalyses);
    }

    // ===================== 3) Lid formalari — deterministik raqamlar =====================

    /// <summary>
    /// Bir odam formani IKKI marta to'ldirsa: ariza 2 ta, LID esa 1 ta.
    ///
    /// <para>⚠️ Bu AI uchun eng muhim raqam — foizlarning MAXRAJI. Arizalar bo'yicha sanalsa
    /// ko'p to'ldirilgan forma sun'iy ravishda yomon ko'rinar va AI "Instagram ishlamayapti" deb
    /// noto'g'ri xulosa yozardi.</para>
    /// </summary>
    [Fact]
    public async Task Lid_formalari_TAKRORSIZ_lidlar_boyicha_sanaladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = AddForm(db, "Instagram forma", "instagram", views: 100);
        var ali = AddLead(db);
        AddSubmission(db, form, ali);
        AddSubmission(db, form, ali, isNewLead: false);   // o'sha odam, ikkinchi marta
        await db.SaveChangesAsync();

        var m = await FunnelAiAnalysisService.BuildMetricsAsync(
            db, FunnelAiAnalysisService.KindLeadForms);

        Assert.Equal(FunnelAiAnalysisService.KindLeadForms, m.Kind);
        Assert.Equal(1, m.Sources);
        Assert.Equal(1, m.ActiveSources);
        Assert.Equal(100, m.Views);
        Assert.Equal(2, m.Submissions);
        Assert.Equal(1, m.Leads);              // TAKRORSIZ
        Assert.Equal(2.0, m.SubmitRate);       // 2 ariza / 100 ochilish
        Assert.Equal(0, m.ConvertRate);        // hech kim o'quvchi bo'lmagan
        Assert.Equal(0, m.PayRate);

        var ch = Assert.Single(m.Channels);
        Assert.Equal("Instagram forma", ch.Name);
        Assert.Equal("Instagram", ch.Source);
        Assert.Equal(2, ch.Submissions);
        Assert.Equal(1, ch.Leads);
    }

    /// <summary>
    /// Bir odam IKKI xil formani to'ldirsa: har forma o'z kesimida 1 ta lid ko'radi, JAMI esa
    /// baribir 1 — ya'ni umumiy son forma qatorlarining YIG'INDISI emas (yig'indi 2 chiqardi).
    /// </summary>
    [Fact]
    public async Task Ikki_formadagi_bir_odam_jamida_BIR_marta_sanaladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var insta = AddForm(db, "Instagram forma", "instagram");
        var tg = AddForm(db, "Telegram forma", "telegram", source: "Telegram");
        var ali = AddLead(db);
        AddSubmission(db, insta, ali);
        AddSubmission(db, tg, ali, isNewLead: false);
        await db.SaveChangesAsync();

        var m = await FunnelAiAnalysisService.BuildMetricsAsync(
            db, FunnelAiAnalysisService.KindLeadForms);

        Assert.Equal(2, m.Submissions);
        Assert.Equal(1, m.Leads);
        Assert.Equal(2, m.Channels.Count);
        Assert.All(m.Channels, c => Assert.Equal(1, c.Leads));
    }

    /// <summary>Ochilish (Views) 0 bo'lsa foiz 0 — nolga bo'linish yo'q.</summary>
    [Fact]
    public async Task Ochilish_yoq_bolsa_foiz_nol()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var form = AddForm(db, "QR forma", "qr", views: 0);
        AddSubmission(db, form, AddLead(db));
        await db.SaveChangesAsync();

        var m = await FunnelAiAnalysisService.BuildMetricsAsync(
            db, FunnelAiAnalysisService.KindLeadForms);

        Assert.Equal(0, m.SubmitRate);
    }

    // ===================== 4) Daraja testlari =====================

    /// <summary>
    /// Daraja testida "ochilish" sanog'i yo'q, shuning uchun <c>Views</c> = YUBORILGAN bir
    /// martalik havolalar (<see cref="LevelTestInvite"/>) soni — "havola yuborilgan N kishidan
    /// nechtasi topshirdi" degan savolga javob beradi.
    /// </summary>
    [Fact]
    public async Task Daraja_testida_Views_yuborilgan_havolalar_soni()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var test = new LevelTest { Title = "Ingliz — daraja testi", Slug = "ingliz", IsActive = true };
        db.LevelTests.Add(test);
        var ali = AddLead(db);
        db.LevelTestSubmissions.Add(new LevelTestSubmission
        {
            TestId = test.Id, FullName = ali.FullName, Phone = ali.Phone,
            Score = 8, Total = 10, Percent = 80, Level = "B1",
            CreatedAt = "2026-08-05T10:00:00", LeadId = ali.Id,
        });
        for (var i = 0; i < 4; i++)
            db.LevelTestInvites.Add(new LevelTestInvite
            {
                TestId = test.Id, LeadId = ali.Id, CreatedAt = "2026-08-04T10:00:00",
            });
        await db.SaveChangesAsync();

        var m = await FunnelAiAnalysisService.BuildMetricsAsync(
            db, FunnelAiAnalysisService.KindLevelTests);

        Assert.Equal(FunnelAiAnalysisService.KindLevelTests, m.Kind);
        Assert.Equal(1, m.Sources);
        Assert.Equal(1, m.ActiveSources);
        Assert.Equal(4, m.Views);              // ← yuborilgan havolalar
        Assert.Equal(1, m.Submissions);
        Assert.Equal(1, m.Leads);
        Assert.Equal(25.0, m.SubmitRate);      // 1 topshiriq / 4 havola

        var ch = Assert.Single(m.Channels);
        Assert.Equal("Ingliz — daraja testi", ch.Name);
        Assert.Equal("", ch.Source);           // testda "manba" tushunchasi yo'q
        Assert.Equal(1, ch.Leads);
    }

    // ===================== 5) Prompt shishmasin — kanal chegarasi =====================

    /// <summary>
    /// Kanallar kesimi <see cref="FunnelAiAnalysisService.MaxChannels"/> bilan cheklanadi:
    /// prompt shishib ketsa AI eng muhim raqamlarni "yo'qotadi". JAMLANMA sonlar esa cheklovdan
    /// MUSTAQIL — butun to'plam bo'yicha (bu yerda 20 ta forma, 20 ta ariza).
    /// </summary>
    [Fact]
    public async Task Kanallar_royxati_MaxChannels_bilan_cheklanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        for (var i = 0; i < 20; i++)
        {
            var form = AddForm(db, $"Forma {i}", $"forma-{i}");
            // Har formada har xil ariza soni — saralash (eng ko'p arizali 15 tasi) sinalsin.
            for (var j = 0; j <= i; j++)
                AddSubmission(db, form, AddLead(db, $"Mijoz {i}-{j}", $"+99890{i:00}{j:0000}"));
        }
        await db.SaveChangesAsync();

        var m = await FunnelAiAnalysisService.BuildMetricsAsync(
            db, FunnelAiAnalysisService.KindLeadForms);

        Assert.Equal(20, m.Sources);
        Assert.Equal(210, m.Submissions);                      // 1+2+...+20 — cheklovdan mustaqil
        Assert.Equal(210, m.Leads);
        Assert.Equal(FunnelAiAnalysisService.MaxChannels, m.Channels.Count);
        // Eng ko'p arizali forma birinchi (quyruqdagilar tushib qoladi).
        Assert.Equal("Forma 19", m.Channels[0].Name);
        Assert.Equal(20, m.Channels[0].Submissions);
    }

    // ===================== 6) Tarix =====================

    /// <summary>Tarix — eng yangisi birinchi va FAQAT o'z turi (ikkala sahifa aralashib ketmasin).</summary>
    [Fact]
    public async Task Tarix_eng_yangisi_birinchi_va_tur_boyicha_ajratilgan()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = TodaysRecord(FunnelAiAnalysisService.KindLeadForms);
        old.Date = "2026-08-01";
        old.CreatedAt = "2026-08-01T09:00:00";
        var newer = TodaysRecord(FunnelAiAnalysisService.KindLeadForms);
        newer.Date = "2026-08-06";
        newer.CreatedAt = "2026-08-06T09:00:00";
        var other = TodaysRecord(FunnelAiAnalysisService.KindLevelTests);
        other.Date = "2026-08-07";
        db.FunnelAiAnalyses.AddRange(old, newer, other);
        await db.SaveChangesAsync();

        var history = await FunnelAiAnalysisService.HistoryAsync(
            db, FunnelAiAnalysisService.KindLeadForms);

        Assert.Equal(2, history.Count);
        Assert.Equal("2026-08-06", history[0].Date);
        Assert.Equal("2026-08-01", history[1].Date);
        Assert.All(history, r => Assert.Equal(FunnelAiAnalysisService.KindLeadForms, r.Kind));
    }
}
