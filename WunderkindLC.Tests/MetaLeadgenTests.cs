using System.Text.Json;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// REKLAMA LIDLARI (Meta Lead Ads) — webhook payloadini o'qish testlari.
/// Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §16.
///
/// <para>Eng qimmat qoida — <b>DEDUP KALITI DETERMINISTIK</b>: <c>leadgen:{id}</c>. Meta
/// yetkazishni "at-least-once" kafolatlaydi va muvaffaqiyatsiz yetkazishni 36 soat qayta
/// yuboradi; kalit har safar bir xil chiqmasa bitta mijoz uchun bir necha lid ochilardi.</para>
/// </summary>
public class MetaLeadgenParserTests
{
    /// <summary>Meta yuboradigan haqiqiy payload shakli (Page obyekti, `leadgen` maydoni).</summary>
    private const string Payload = """
    {
      "object": "page",
      "entry": [{
        "id": "1122334455",
        "time": 1755600000,
        "changes": [{
          "field": "leadgen",
          "value": {
            "created_time": 1755600000,
            "leadgen_id": "9988776655",
            "page_id": "1122334455",
            "form_id": "5544332211",
            "ad_id": "7777",
            "adgroup_id": "8888"
          }
        }]
      }]
    }
    """;

    [Fact]
    public void Leadgen_hodisasini_oqiydi()
    {
        var items = MetaLeadgenParser.Parse(Payload);

        var one = Assert.Single(items);
        Assert.Equal("9988776655", one.LeadgenId);
        Assert.Equal("1122334455", one.PageId);
        Assert.Equal("5544332211", one.FormId);
        Assert.Equal("7777", one.AdId);
        Assert.Equal("8888", one.AdgroupId);
        Assert.NotEqual("", one.CreatedTimeIso);
    }

    /// <summary>⚠️ Kalit BARQAROR bo'lishi shart — restartdan keyin ham, qayta parse qilinganda
    /// ham bir xil. `GetHashCode`/`Guid` ishlatilsa dedup umuman ishlamasdi.</summary>
    [Fact]
    public void Dedup_kaliti_deterministik()
    {
        var a = MetaLeadgenParser.Parse(Payload).Single().EventKey;
        var b = MetaLeadgenParser.Parse(Payload).Single().EventKey;

        Assert.Equal(a, b);
        Assert.Equal("leadgen:9988776655", a);
    }

    /// <summary>⚠️ Meta id'larni ba'zan SATR, ba'zan RAQAM qilib yuboradi — faqat satr o'qilsa
    /// lidlar jimgina tushib qolardi.</summary>
    [Fact]
    public void Raqam_korinishidagi_id_ham_oqiladi()
    {
        const string json = """
        {"object":"page","entry":[{"id":"1","changes":[
          {"field":"leadgen","value":{"leadgen_id":"123","form_id":456}}]}]}
        """;

        var one = Assert.Single(MetaLeadgenParser.Parse(json));
        Assert.Equal("123", one.LeadgenId);
        Assert.Equal("456", one.FormId);
    }

    /// <summary>`page_id` kelmasa `entry.id` ishlatiladi — token qaysi sahifaniki ekanini
    /// bilish uchun bu qiymat kerak.</summary>
    [Fact]
    public void Page_id_yoq_bolsa_entry_id_olinadi()
    {
        const string json = """
        {"object":"page","entry":[{"id":"777","changes":[
          {"field":"leadgen","value":{"leadgen_id":"1"}}]}]}
        """;

        Assert.Equal("777", MetaLeadgenParser.Parse(json).Single().PageId);
    }

    /// <summary>Izoh/DM payloadi (boshqa maydon) — reklama parseri uni OLMAYDI.</summary>
    [Fact]
    public void Izoh_hodisasi_olinmaydi()
    {
        const string json = """
        {"object":"instagram","entry":[{"id":"1","changes":[
          {"field":"comments","value":{"id":"c1","text":"salom"}}]}]}
        """;

        Assert.Empty(MetaLeadgenParser.Parse(json));
    }

    /// <summary>Lid id'siz hodisadan foyda yo'q (ism ham, telefon ham AYNAN shu id bilan
    /// olinadi) — jimgina tashlanadi, istisno OTILMAYDI.</summary>
    [Fact]
    public void Leadgen_id_siz_hodisa_tashlanadi()
    {
        const string json = """
        {"object":"page","entry":[{"id":"1","changes":[
          {"field":"leadgen","value":{"form_id":"5"}}]}]}
        """;

        Assert.Empty(MetaLeadgenParser.Parse(json));
    }

    /// <summary>Buzuq/bo'sh JSON → BO'SH ro'yxat: bitta noto'g'ri payload navbatni
    /// to'xtatib qo'ymasin.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ buzuq")]
    [InlineData("[]")]
    [InlineData("{\"object\":\"page\"}")]
    public void Buzuq_json_bosh_royxat(string json) => Assert.Empty(MetaLeadgenParser.Parse(json));
}

/// <summary>
/// Graph javobidan lid ma'lumotini o'qish (<c>MetaAdsApi.ReadLead</c>).
/// ⚠️ Formada odatda FAQAT F.I.Sh. va telefon bo'ladi — asosiy holat aynan shu.
/// </summary>
public class MetaAdLeadReadTests
{
    private static MetaAdLeadData Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MetaAdsApi.ReadLead(doc.RootElement, "lead-1");
    }

    [Fact]
    public void Fish_va_telefonni_oqiydi()
    {
        var lead = Read("""
        {
          "id": "lead-1",
          "created_time": "2026-08-20T10:15:00+0000",
          "campaign_name": "Yozgi intensiv",
          "ad_name": "Story #2",
          "platform": "ig",
          "field_data": [
            {"name": "full_name", "values": ["Ali Valiyev"]},
            {"name": "phone_number", "values": ["+998901234567"]}
          ]
        }
        """);

        Assert.Equal("Ali Valiyev", lead.FullName);
        Assert.Equal("+998901234567", lead.Phone);
        Assert.Equal("Yozgi intensiv", lead.CampaignName);
        Assert.Equal("ig", lead.Platform);
        Assert.NotEqual("", lead.FieldsJson);
    }

    /// <summary>Ba'zi formalarda ism IKKI maydonda keladi — birlashtiriladi.</summary>
    [Fact]
    public void Ism_ikki_maydonda_bolsa_birlashtiriladi()
    {
        var lead = Read("""
        {"id":"lead-1","field_data":[
          {"name":"first_name","values":["Ali"]},
          {"name":"last_name","values":["Valiyev"]}]}
        """);

        Assert.Equal("Ali Valiyev", lead.FullName);
    }

    /// <summary>Maydon nomlari formadan formaga farq qiladi — tanish nomlar topilmasa xom JSON
    /// BARIBIR saqlanadi (ma'lumot jimgina yo'qolmasin).</summary>
    [Fact]
    public void Notanish_maydon_xom_json_da_qoladi()
    {
        var lead = Read("""
        {"id":"lead-1","field_data":[{"name":"qaysi_kurs","values":["IELTS"]}]}
        """);

        Assert.Equal("", lead.FullName);
        Assert.Contains("qaysi_kurs", lead.FieldsJson);
    }

    /// <summary>`field_data` umuman bo'lmasa ham yiqilmaydi.</summary>
    [Fact]
    public void Maydonsiz_javob_yiqilmaydi()
    {
        var lead = Read("""{"id":"lead-1"}""");

        Assert.Equal("lead-1", lead.LeadgenId);
        Assert.Equal("", lead.Phone);
        Assert.Equal("", lead.FieldsJson);
    }
}

/// <summary>
/// REKLAMA LIDINI CRM LIDIGA ULASH (<see cref="MetaLeadBridge"/>).
/// Qoidalar <c>InstagramLeadBridge</c> bilan AYNAN bir xil — bir odam avval reklama formasini
/// to'ldirib, keyin DM yozishi odatiy hol va ikki joyda ikki xil qoida bo'lsa CRM'da bitta odam
/// ikkita kartochka bo'lib qolardi.
/// </summary>
public class MetaLeadBridgeTests
{
    private const string FirstStage = "stage-yangi";

    private static void Seed(TestDb db)
    {
        db.Context.LeadStages.AddRange(
            new LeadStage { Id = FirstStage, Title = "Yangi", Order = 0 },
            new LeadStage { Id = "stage-ikkinchi", Title = "Aloqada", Order = 1 });
        db.Context.SaveChanges();
    }

    private static IgAdLead AdLead(string phone = "+998901234567", string name = "Ali Valiyev") => new()
    {
        LeadgenId = "lg-1",
        FullName = name,
        Phone = phone,
        FormName = "Yozgi IELTS intensiv",
        CampaignName = "Avgust kampaniyasi",
        CreatedTime = AppClock.Iso(),
        ReceivedAt = AppClock.Iso(),
    };

    [Fact]
    public async Task Yangi_lid_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);

        var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(db.Context, AdLead(), "Instagram reklama");
        await db.Context.SaveChangesAsync();

        Assert.True(isNew);
        var lead = await db.Context.Leads.FirstAsync(l => l.Id == leadId);
        Assert.Equal("Ali Valiyev", lead.FullName);
        Assert.Equal("Instagram reklama", lead.Source);
        Assert.Equal(FirstStage, lead.Stage);
        // Forma nomi — lidning "qiziqqan yo'nalishi".
        Assert.Equal("Yozgi IELTS intensiv", lead.InterestSubject);
        Assert.Contains("Avgust kampaniyasi", lead.Note);

        var ev = await db.Context.LeadEvents.FirstAsync(e => e.LeadId == leadId);
        Assert.Equal("created", ev.Type);
        Assert.Equal(MetaLeadBridge.ActorName, ev.ActorName);
    }

    /// <summary>⚠️ FIRST-TOUCH: mavjud lidda `Source` ham, `Stage` ham O'ZGARMAYDI — aks holda
    /// "Sayt" lidi "Instagram reklama"ga aylanib, menejer kanbanda qo'lda qo'ygan bosqich ham
    /// tashlanib ketardi.</summary>
    [Fact]
    public async Task Mavjud_lidda_manba_va_bosqich_ozgarmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            Id = "lead-eski", FullName = "Ali V.", Phone = "+998 90 123 45 67",
            Source = "Sayt", Stage = "stage-ikkinchi", CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(db.Context, AdLead(), "Instagram reklama");
        await db.Context.SaveChangesAsync();

        Assert.False(isNew);
        Assert.Equal("lead-eski", leadId);

        var lead = await db.Context.Leads.FirstAsync(l => l.Id == "lead-eski");
        Assert.Equal("Sayt", lead.Source);
        Assert.Equal("stage-ikkinchi", lead.Stage);
        Assert.Equal(1, lead.RepeatCount);
        // Menejer kiritgan ism ustiga yozilmaydi.
        Assert.Equal("Ali V.", lead.FullName);

        var ev = await db.Context.LeadEvents.FirstAsync(e => e.LeadId == "lead-eski");
        Assert.Equal("note", ev.Type);
    }

    /// <summary>Telefon bazada boshqa FORMATDA saqlangan bo'lsa ham topiladi (oxirgi 9 raqam —
    /// `PhoneUtil.Key`, `LeadIntake` bilan yagona qoida).</summary>
    [Fact]
    public async Task Boshqa_formatdagi_telefon_bilan_ham_dublikat_ochilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            Id = "lead-eski", FullName = "Ali", Phone = "998901234567",
            Source = "Telegram", Stage = FirstStage, CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(
            db.Context, AdLead(phone: "+998-90-123-45-67"), "Instagram reklama");
        await db.Context.SaveChangesAsync();

        Assert.False(isNew);
        Assert.Equal("lead-eski", leadId);
        Assert.Equal(1, await db.Context.Leads.CountAsync());
    }

    /// <summary>⚠️ TELEFONSIZ lid ham YOZILADI: Meta formasida telefon majburiy bo'lmasligi
    /// mumkin. Jimgina tashlab yuborilsa markaz PUL TO'LAGAN murojaatdan xabar topmasdi.</summary>
    [Fact]
    public async Task Telefonsiz_lid_ham_yoziladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);

        var (leadId, isNew) = await MetaLeadBridge.UpsertAsync(
            db.Context, AdLead(phone: "", name: ""), "Instagram reklama");
        await db.Context.SaveChangesAsync();

        Assert.True(isNew);
        var lead = await db.Context.Leads.FirstAsync(l => l.Id == leadId);
        Assert.Equal("", lead.Phone);
        Assert.Equal("Reklama lidi (ismsiz)", lead.FullName);
    }

    /// <summary>Manba nomi bo'sh bo'lsa (eski bazada ustun bo'sh qolgan bo'lishi mumkin) —
    /// lid "manbasiz" qolmaydi.</summary>
    [Fact]
    public async Task Bosh_manba_nomi_default_ga_qaytadi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);

        var (leadId, _) = await MetaLeadBridge.UpsertAsync(db.Context, AdLead(), "   ");
        await db.Context.SaveChangesAsync();

        var lead = await db.Context.Leads.FirstAsync(l => l.Id == leadId);
        Assert.Equal(MetaLeadBridge.DefaultSource, lead.Source);
    }
}

/// <summary>
/// KANAL TASNIFI — reklama lidi DM/izoh lididan AJRALADI (`LeadOrigins`).
/// Sabab: "Instagram" degan bitta qator marketologga pul to'langan reklama qancha lid berganini
/// KO'RSATMASDI.
/// </summary>
public class LeadOriginsAdsTests
{
    private static readonly HashSet<string> Ads = ["a1"];
    private static readonly HashSet<string> Ig = ["a1", "i1"];

    /// <summary>⚠️ Reklama Instagram'dan OLDIN tekshiriladi: reklama formasini to'ldirgan odam
    /// keyin DM ham yozsa lid IKKALA ro'yxatda bo'ladi, kanal esa — BIRINCHI teginish.</summary>
    [Fact]
    public void Reklama_dm_dan_ustun()
    {
        Assert.Equal(LeadOrigins.Ads,
            LeadOrigins.Classify("a1", instagramLeadIds: Ig, adsLeadIds: Ads));
        Assert.Equal(LeadOrigins.Instagram,
            LeadOrigins.Classify("i1", instagramLeadIds: Ig, adsLeadIds: Ads));
    }

    /// <summary>Qo'lda kiritilgan lid baribir eng ustun (birinchi teginish).</summary>
    [Fact]
    public void Qolda_kiritilgan_reklamadan_ustun()
    {
        Assert.Equal(LeadOrigins.Manual,
            LeadOrigins.Classify("a1", manualLeadIds: new HashSet<string> { "a1" }, adsLeadIds: Ads));
    }

    [Fact]
    public void Yorligi_va_tartibi_bor()
    {
        Assert.Contains(LeadOrigins.Ads, LeadOrigins.Order);
        Assert.False(string.IsNullOrWhiteSpace(LeadOrigins.LabelOf(LeadOrigins.Ads)));
    }
}

/// <summary>
/// REKLAMA LIDI OQIMI — soxta webhook JSON'idan CRM lidigacha (UCHDAN-UCHGACHA).
/// Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §16.2.
///
/// <para>Graph API javobi <see cref="RecordingHandler"/> bilan soxtalashtiriladi — test
/// "tashqariga so'rov KETDIMI?" degan savolga ham javob beradi (modul o'chiq bo'lsa
/// ketmasligi kerak).</para>
/// </summary>
public class MetaLeadgenPipelineTests
{
    /// <summary>Meta yuboradigan `page`/`leadgen` payloadi.</summary>
    private static string LeadgenJson(string leadgenId = "lg-777") =>
        """
        {"object":"page","entry":[{"id":"page-1","time":1755600000,"changes":[
          {"field":"leadgen","value":{"leadgen_id":"__ID__","page_id":"page-1",
           "form_id":"form-1","ad_id":"ad-1","created_time":1755600000}}]}]}
        """.Replace("__ID__", leadgenId);

    /// <summary>Graph `GET /{leadgen_id}` javobi — formada FAQAT F.I.Sh. va telefon.</summary>
    private const string GraphLead = """
    {"id":"lg-777","created_time":"2026-08-20T10:15:00+0000","campaign_name":"Avgust",
     "ad_name":"Story","platform":"ig","form_id":"form-1",
     "field_data":[{"name":"full_name","values":["Ali Valiyev"]},
                   {"name":"phone_number","values":["901234567"]}]}
    """;

    private static CenterMeta Meta(bool adsEnabled) => new()
    {
        InstagramEnabled = false,           // ⚠️ AVTOJAVOB O'CHIQ — reklama lidi undan MUSTAQIL
        InstagramLeadAdsEnabled = adsEnabled,
        InstagramAdsLeadSource = "Instagram reklama",
        InstagramNotifyTelegram = false,
    };

    /// <param name="keySuffix">Navbat kaliti UNIKAL bo'lgani uchun takroriy yozuvni
    /// modellashtirishda boshqa kalit beriladi — bu AYNAN eski hodisa tozalangandan keyin
    /// Meta uni qayta yuborgan holat (dedupning ikkinchi qavati shunda sinaladi).</param>
    private static async Task<(IgWebhookEvent Ev, RecordingHandler Handler)> RunAsync(
        TestDb db, string rawJson, string keySuffix = "")
    {
        var ev = new IgWebhookEvent
        {
            EventKey = (MetaLeadgenParser.Parse(rawJson).FirstOrDefault()?.EventKey ?? "x") + keySuffix,
            RawJson = rawJson,
            Status = IgConst.EvPending,
            ReceivedAt = AppClock.Iso(),
        };
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler(body: GraphLead);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var adsApi = new MetaAdsApi(new HttpClient(handler), NullLogger<MetaAdsApi>.Instance);
        var leadgen = new MetaLeadgenService(db.Context, adsApi, telegram, NullLogger<MetaLeadgenService>.Instance);

        var pipeline = new InstagramPipeline(
            new LeadgenServiceProvider(db.Context, telegram, leadgen), NullLogger<InstagramPipeline>.Instance);

        await pipeline.ProcessAsync(ev.Id, CancellationToken.None);
        return (db.Context.IgWebhookEvents.Single(e => e.Id == ev.Id), handler);
    }

    [Fact]
    public async Task Webhookdan_crm_lidigacha()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(adsEnabled: true));
        db.Context.LeadStages.Add(new LeadStage { Id = "s1", Title = "Yangi", Order = 0 });
        db.Context.IgAdPages.Add(new IgAdPage
        {
            PageId = "page-1", PageName = "Wunderkind", AccessToken = "tok",
            LeadgenSubscribed = true, IsActive = true, ConnectedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var (ev, handler) = await RunAsync(db, LeadgenJson());

        Assert.Equal(IgConst.EvDone, ev.Status);
        Assert.Equal("", ev.Error);

        var adLead = await db.Context.IgAdLeads.SingleAsync();
        Assert.Equal("lg-777", adLead.LeadgenId);
        Assert.Equal("Ali Valiyev", adLead.FullName);
        // Telefon normalizatsiya qilinadi — dedup shu bilan ishlaydi.
        Assert.Equal("+998-90-123-45-67", adLead.Phone);
        Assert.Equal("Avgust", adLead.CampaignName);
        Assert.NotEqual("", adLead.LeadId);
        Assert.True(adLead.IsNewLead);
        Assert.Equal("", adLead.Error);

        var lead = await db.Context.Leads.SingleAsync();
        Assert.Equal("Ali Valiyev", lead.FullName);
        Assert.Equal("Instagram reklama", lead.Source);
        Assert.Equal("s1", lead.Stage);

        // Sahifada "oxirgi lid" belgilanadi — "ulangan, lekin lid kelmayapti" holati ko'rinsin.
        Assert.NotEqual("", (await db.Context.IgAdPages.SingleAsync()).LastLeadAt);
        Assert.Contains(handler.Requests, r => r.Contains("graph.facebook.com"));
    }

    /// <summary>⚠️ MODUL O'CHIQ bo'lsa Meta'ga so'rov UMUMAN ketmaydi (darvoza qoidasi §3).
    /// Sabab navbat yozuvida ochiq qoladi — jimgina yo'qolmaydi.</summary>
    [Fact]
    public async Task Modul_ochiq_bolsa_tashqariga_sorov_ketmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(adsEnabled: false));
        await db.Context.SaveChangesAsync();

        var (ev, handler) = await RunAsync(db, LeadgenJson());

        Assert.Empty(handler.Requests);
        Assert.Empty(db.Context.IgAdLeads);
        Assert.Empty(db.Context.Leads);
        Assert.Contains("o'chirilgan", ev.Error);
    }

    /// <summary>⚠️ TOKEN YO'Q bo'lsa yozuv BARIBIR saqlanadi (xato bilan) — keyin admin
    /// «Qayta olish» bilan to'ldiradi. Jimgina yo'qolgan lid — eng yomon holat.</summary>
    [Fact]
    public async Task Tokensiz_yozuv_xato_bilan_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(adsEnabled: true));
        await db.Context.SaveChangesAsync();

        var (_, handler) = await RunAsync(db, LeadgenJson());

        Assert.Empty(handler.Requests);
        var adLead = await db.Context.IgAdLeads.SingleAsync();
        Assert.Equal("lg-777", adLead.LeadgenId);
        Assert.Equal("", adLead.LeadId);
        Assert.Contains("Token", adLead.Error);
    }

    /// <summary>⚠️ DEDUPNING UZOQ MUDDATLI QAVATI: navbat yozuvlari 30 kunda tozalanadi, ya'ni
    /// eski hodisa qayta kelsa `IgAdLead.LeadgenId` tekshiruvi ikkinchi lidni to'xtatadi.</summary>
    [Fact]
    public async Task Ayni_lid_ikkinchi_marta_kelsa_dublikat_ochilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(adsEnabled: true));
        db.Context.LeadStages.Add(new LeadStage { Id = "s1", Title = "Yangi", Order = 0 });
        db.Context.IgAdPages.Add(new IgAdPage
        {
            PageId = "page-1", AccessToken = "tok", IsActive = true, ConnectedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        await RunAsync(db, LeadgenJson());
        await RunAsync(db, LeadgenJson(), keySuffix: "-qayta");   // AYNAN o'sha lid, boshqa navbat yozuvi

        Assert.Equal(1, await db.Context.IgAdLeads.CountAsync());
        Assert.Equal(1, await db.Context.Leads.CountAsync());
    }
}

/// <summary><see cref="InstagramPipeline"/> ning reklama lidlari shoxobchasi uchun eng kichik
/// DI konteyner (scope = o'zi).</summary>
internal sealed class LeadgenServiceProvider(
    WunderkindLC.Application.Abstractions.IAppDbContext db,
    TelegramService telegram,
    MetaLeadgenService leadgen)
    : IServiceProvider, Microsoft.Extensions.DependencyInjection.IServiceScopeFactory,
      Microsoft.Extensions.DependencyInjection.IServiceScope
{
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config =
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

    private readonly InstagramApi _api =
        new(new HttpClient(new RecordingHandler()), NullLogger<InstagramApi>.Instance);

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) return this;
        if (serviceType == typeof(WunderkindLC.Application.Abstractions.IAppDbContext)) return db;
        if (serviceType == typeof(InstagramApi)) return _api;
        if (serviceType == typeof(TelegramService)) return telegram;
        if (serviceType == typeof(MetaLeadgenService)) return leadgen;
        if (serviceType == typeof(Microsoft.Extensions.Configuration.IConfiguration)) return _config;
        return null;
    }

    public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public void Dispose() { }
}
