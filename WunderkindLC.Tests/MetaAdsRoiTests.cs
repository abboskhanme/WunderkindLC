using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// REKLAMA ROI HISOBOTI (<see cref="MetaAdsRoi"/>) — "xarajat → lid → o'quvchi → to'lov"
/// zanjiri. Rasmiy spetsifikatsiya: <c>KENGAYTIRISH-PROMPT.md</c> §4.8.
///
/// <para>Bu yerda tekshiriladigan asosiy va'dalar:</para>
/// <list type="number">
///   <item><b>Meta lidlari ≠ CRM lidlari</b> — ikkalasi ham alohida qaytadi, farq yashirilmaydi;</item>
///   <item><b>Pul</b> — faqat o'quv to'lovi (<c>tuition</c>); kitob savdosi va boshqa
///         kategoriyalar daromadga KIRMAYDI;</item>
///   <item><b>Vozvrat</b> — to'liq qaytarilgan lid "to'lamagan", daromadga MANFIY qo'shilmaydi;</item>
///   <item><b>Takrorsizlik</b> — bitta lid ikki qatordan kelsa bir marta sanaladi;</item>
///   <item><b>Nolga bo'lish</b> — CPL/CAC/ROI <c>null</c>, istisno EMAS;</item>
///   <item><b>Qamrov QO'SHILMAYDI</b> — pastki/yuqori chegara beriladi;</item>
///   <item><b>Darajalar aralashmaydi</b> — kampaniya va e'lon qatorlari birga qo'shilmaydi.</item>
/// </list>
/// </summary>
public class MetaAdsRoiTests
{
    private const string Act = "act_1";
    private const string From = "2026-08-01";
    private const string To = "2026-08-31";

    // ═══════════════════════ Yordamchilar ═══════════════════════

    private static IgAdAccount NewAccount(string currency = "UZS") => new()
    {
        AdAccountId = Act,
        Name = "Markaz reklamasi",
        Currency = currency,
        CurrencyOffset = MetaCurrency.OffsetOf(currency),
        TimezoneName = "Asia/Tashkent",
        AccessToken = "token",
        IsActive = true,
        ConnectedAt = "2026-07-01T09:00:00",
    };

    /// <summary>Iyerarxiya tuguni. <c>ExternalId</c> UNIKAL — testlarda takrorlanmasin.</summary>
    private static IgAdEntity Node(string level, string id, string parent, string name) => new()
    {
        AdAccountId = Act, Level = level, ExternalId = id, ParentId = parent, Name = name,
        Status = "ACTIVE", EffectiveStatus = "ACTIVE", SyncedAt = "2026-08-20T05:00:00",
    };

    private static IgAdInsight Insight(
        string level, string externalId, string date, long spendMinor,
        long impressions = 1000, long reach = 500, long clicks = 50,
        int leadsOnsite = 0, int leadsPixel = 0, string platform = MetaAdsRoi.PlatformInstagram,
        int msgStarted = 0, string attributionSetting = "") => new()
    {
        AdAccountId = Act, Level = level, ExternalId = externalId, StatDate = date,
        Platform = platform, SpendMinor = spendMinor, Impressions = impressions, Reach = reach,
        Clicks = clicks, LinkClicks = clicks / 2, LeadsOnsite = leadsOnsite, LeadsPixel = leadsPixel,
        MsgStarted = msgStarted, AttributionSetting = attributionSetting,
        FetchedAt = "2026-08-20T05:00:00",
    };

    /// <summary>Reklama lidi + (ixtiyoriy) unga bog'langan CRM lidi.</summary>
    private static IgAdLead AdLead(
        string leadgenId, string campaignId, string leadId,
        string adsetId = "as1", string adId = "ad1",
        string platform = MetaAdsRoi.LeadPlatformIg, string created = "2026-08-05T10:00:00") => new()
    {
        LeadgenId = leadgenId, PageId = "page1", FormId = "form1", FormName = "Yozgi intensiv",
        CampaignId = campaignId, CampaignName = "Yoz-2026", AdsetId = adsetId, AdId = adId,
        AdName = "Kreativ 1", Platform = platform, FullName = "Test", Phone = "+998901112233",
        LeadId = leadId, CreatedTime = created, ReceivedAt = created,
    };

    /// <summary>CRM lidi (bazaga qo'shiladi, saqlanmaydi).</summary>
    private static Lead NewLead(Microsoft.EntityFrameworkCore.DbContext db, string name, string phone)
    {
        var lead = new Lead
        {
            FullName = name, Phone = phone, Source = "Instagram reklama",
            CreatedAt = "2026-08-05T10:00:00",
        };
        db.Add(lead);
        return lead;
    }

    /// <summary>Lidni o'quvchiga aylantiradi va (ixtiyoriy) O'QUV to'lovini yozadi.</summary>
    private static Student Convert(
        Microsoft.EntityFrameworkCore.DbContext db, Lead lead, decimal tuition = 0m,
        string date = "2026-08-10")
    {
        var student = new Student { FullName = lead.FullName };
        db.Add(student);
        lead.ConvertedStudentId = student.Id;
        if (tuition > 0)
            db.Add(new FinanceTransaction
            {
                StudentId = student.Id, Direction = "income", Category = "tuition",
                Amount = tuition, Date = date,
            });
        return student;
    }

    private static Task<IgRoiReportDto> Build(
        Microsoft.EntityFrameworkCore.DbContext db, string platform = MetaAdsRoi.PlatformAll,
        string campaignId = "") =>
        MetaAdsRoi.BuildAsync((WunderkindLC.Application.Abstractions.IAppDbContext)db,
                              From, To, platform, campaignId);

    /// <summary>Bitta kampaniya (c1) → adset (as1) → e'lon (ad1) skeleti.</summary>
    private static void AddTree(Microsoft.EntityFrameworkCore.DbContext db)
    {
        db.Add(Node(MetaAdsRoi.LevelCampaign, "c1", "", "Yoz-2026"));
        db.Add(Node(MetaAdsRoi.LevelAdset, "as1", "c1", "Toshkent 18-30"));
        db.Add(Node(MetaAdsRoi.LevelAd, "ad1", "as1", "Kreativ 1"));
    }

    // ═══════════════════════ 1) Asosiy zanjir ═══════════════════════

    /// <summary>
    /// IKKI lid: biri to'lagan, ikkinchisining puli TO'LIQ qaytarilgan.
    /// Kutilgan: <c>Paid = 1</c>, daromad faqat to'lagan lidniki va vozvrat MANFIY qo'shilmaydi.
    /// </summary>
    [Fact]
    public async Task Tolagan_va_qaytargan_lid_Paid_1_va_daromad_manfiy_qoshilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 20_000_000, leadsOnsite: 2));

        var ali = NewLead(db, "Aliyev Ali", "+998901112233");
        var vali = NewLead(db, "Valiyev Vali", "+998907654321");
        db.Add(AdLead("lg1", "c1", ali.Id));
        db.Add(AdLead("lg2", "c1", vali.Id));

        Convert(db, ali, tuition: 400_000m);
        var valiStudent = Convert(db, vali, tuition: 300_000m);
        db.Add(new FinanceTransaction
        {
            StudentId = valiStudent.Id, Direction = "expense", Category = "refund",
            Amount = 300_000m, Date = "2026-08-15",
        });
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.True(report.Connected);
        Assert.Equal(2, report.Totals.CrmLeads);
        Assert.Equal(2, report.Totals.Converted);
        Assert.Equal(1, report.Totals.Paid);
        // 400 000 so'm = 40 000 000 tiyin (offset 2). Vozvrat qilgan lid 0 qo'shadi (manfiy EMAS).
        Assert.Equal(40_000_000, report.Totals.RevenueMinor);
    }

    /// <summary>
    /// KITOB SAVDOSI daromadga KIRMAYDI (<c>books.md</c> §7: kitob sotuvi
    /// <c>FinanceTransaction</c> ga umuman yozilmaydi). Qo'shimcha himoya sifatida
    /// o'quv to'lovi BO'LMAGAN kategoriya ham tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Kitob_savdosi_daromadga_qoshilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000, leadsOnsite: 1));

        var ali = NewLead(db, "Aliyev Ali", "+998901112233");
        db.Add(AdLead("lg1", "c1", ali.Id));
        var student = Convert(db, ali, tuition: 200_000m);

        // Kitob sotuvi — o'z bo'limida (moliya bilan bog'lanmagan).
        db.Add(new BookOrder
        {
            Number = 1, ChatId = 0, CustomerName = "Aliyev Ali", StudentId = student.Id,
            BookId = "b1", BookTitle = "Ingliz tili darsligi", UnitPrice = 150_000m, Qty = 1,
            Total = 150_000m, PaymentMethod = "cash", Status = "approved",
        });
        // O'quv to'lovi BO'LMAGAN boshqa kirim ham daromadga kirmasligi kerak.
        db.Add(new FinanceTransaction
        {
            StudentId = student.Id, Direction = "income", Category = "other",
            Amount = 500_000m, Date = "2026-08-12",
        });
        await db.SaveChangesAsync();

        var report = await Build(db);

        // Faqat 200 000 so'm o'quv to'lovi — kitob (150k) va "other" (500k) qo'shilmadi.
        Assert.Equal(20_000_000, report.Totals.RevenueMinor);
        Assert.Equal(1, report.Totals.Paid);
    }

    // ═══════════════════════ 2) Meta lidlari ≠ CRM lidlari ═══════════════════════

    /// <summary>Meta 5 ta lid ko'rsatsa, CRM'da esa 3 tasi bo'lsa — IKKALASI ham qaytadi
    /// va farq izohda ochiq aytiladi (telefon dublikati, 90 kunlik oyna va h.k.).</summary>
    [Fact]
    public async Task Meta_lidi_5_CRM_lidi_3_ikkalasi_ham_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05",
                       spendMinor: 50_000_000, leadsOnsite: 3, leadsPixel: 2));

        for (var i = 1; i <= 3; i++)
        {
            var lead = NewLead(db, $"Lid {i}", $"+99890111223{i}");
            db.Add(AdLead($"lg{i}", "c1", lead.Id));
        }
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(5, report.Totals.MetaLeads);     // onsite 3 + pixel 2
        Assert.Equal(3, report.Totals.CrmLeads);
        Assert.Contains(report.Notes, n => n.Contains("Meta lidlari 5"));
    }

    // ═══════════════════════ 3) Takrorsizlik ═══════════════════════

    /// <summary>Bitta CRM lidi IKKI reklama qatoridan kelsa (Meta ikki marta yuborgan yoki
    /// bir odam ikki formaga yozilgan) — u BIR marta sanaladi, puli ham bir marta.</summary>
    [Fact]
    public async Task Bir_lid_ikki_marta_boglangan_bolsa_bir_marta_sanaladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000, leadsOnsite: 2));

        var ali = NewLead(db, "Aliyev Ali", "+998901112233");
        db.Add(AdLead("lg1", "c1", ali.Id));
        db.Add(AdLead("lg2", "c1", ali.Id));      // AYNAN o'sha CRM lidi
        Convert(db, ali, tuition: 500_000m);
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(2, report.Totals.AdLeadRows);   // xom qatorlar — 2
        Assert.Equal(1, report.Totals.CrmLeads);     // takrorsiz — 1
        Assert.Equal(1, report.Totals.Paid);
        Assert.Equal(50_000_000, report.Totals.RevenueMinor);
    }

    /// <summary>IKKI xil lid BITTA o'quvchiga ulangan bo'lsa (dublikat lid qo'lda
    /// birlashtirilgan): konversiya lid bo'yicha 2, DAROMAD esa BIR marta — pul ikki
    /// barobar ko'rinmasligi kerak.</summary>
    [Fact]
    public async Task Ikki_lid_bitta_oquvchiga_ulangan_bolsa_pul_bir_marta_sanaladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000, leadsOnsite: 2));

        var a = NewLead(db, "Aliyev Ali", "+998901112233");
        var b = NewLead(db, "Aliyev Ali", "+998901112234");
        db.Add(AdLead("lg1", "c1", a.Id));
        db.Add(AdLead("lg2", "c1", b.Id));

        var student = Convert(db, a, tuition: 300_000m);
        b.ConvertedStudentId = student.Id;         // ikkinchi lid ham O'SHA o'quvchida
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(2, report.Totals.CrmLeads);
        Assert.Equal(2, report.Totals.Converted);
        Assert.Equal(2, report.Totals.Paid);            // ikkala lid ham "pul keltirgan"
        Assert.Equal(30_000_000, report.Totals.RevenueMinor);  // lekin PUL bir marta
    }

    // ═══════════════════════ 4) Nolga bo'lish ═══════════════════════

    /// <summary>Xarajat 0 bo'lsa CPL/CAC/ROI — <c>null</c> (0 EMAS) va istisno otilmaydi.</summary>
    [Fact]
    public async Task Xarajat_nol_bolsa_CPL_va_ROI_null()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 0, leadsOnsite: 1));

        var ali = NewLead(db, "Aliyev Ali", "+998901112233");
        db.Add(AdLead("lg1", "c1", ali.Id));
        Convert(db, ali, tuition: 400_000m);
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Null(report.Totals.CplMinor);
        Assert.Null(report.Totals.CacMinor);
        Assert.Null(report.Totals.Roi);
        Assert.Equal(40_000_000, report.Totals.RevenueMinor);
    }

    /// <summary>Lid kelmagan bo'lsa CPL <c>null</c>, lekin ROI HISOBLANADI: pul sarflandi,
    /// daromad 0 → ROI = −1 ("butun byudjet kuydi"). Bu HAQIQIY qiymat, null emas.</summary>
    [Fact]
    public async Task Lid_yoq_bolsa_CPL_null_ROI_esa_minus_bir()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(0, report.Totals.CrmLeads);
        Assert.Null(report.Totals.CplMinor);
        Assert.Equal(-1m, report.Totals.Roi);
    }

    // ═══════════════════════ 5) Daraxt va roll-up ═══════════════════════

    /// <summary>E'lon darajasidagi xarajat adset va kampaniyaga KO'TARILADI (roll-up),
    /// daraxt esa kampaniya → adset → e'lon bo'lib chiziladi.</summary>
    [Fact]
    public async Task Elon_xarajati_adset_va_kampaniyaga_kotariladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Node(MetaAdsRoi.LevelAd, "ad2", "as1", "Kreativ 2"));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad2", "2026-08-06", spendMinor: 5_000_000));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(MetaAdsRoi.LevelAd, report.InsightLevel);
        Assert.Equal(15_000_000, report.Totals.SpendMinor);

        var campaign = Assert.Single(report.Campaigns);
        Assert.Equal("c1", campaign.Id);
        Assert.Equal("Yoz-2026", campaign.Name);
        Assert.Equal(15_000_000, campaign.SpendMinor);

        var adset = Assert.Single(campaign.Children);
        Assert.Equal(15_000_000, adset.SpendMinor);
        Assert.Equal(2, adset.Children.Count);
        Assert.Equal(10_000_000, adset.Children.First(c => c.Id == "ad1").SpendMinor);
    }

    /// <summary>
    /// 🔴 DARAJALAR ARALASHMAYDI: bazada e'lon VA kampaniya qatorlari birga bo'lsa,
    /// hisobot faqat ENG MAYDA darajani oladi — aks holda sarf ikki barobar ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Kampaniya_va_elon_qatorlari_QOSHILMAYDI()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000));
        // Meta o'sha kunning kampaniya kesimini ham bergan — bu O'SHA pul, boshqasi emas.
        db.Add(Insight(MetaAdsRoi.LevelCampaign, "c1", "2026-08-05", spendMinor: 10_000_000));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(MetaAdsRoi.LevelAd, report.InsightLevel);
        Assert.Equal(10_000_000, report.Totals.SpendMinor);   // 20 000 000 EMAS
    }

    // ═══════════════════════ 6) Qamrov ═══════════════════════

    /// <summary>
    /// QAMROV QO'SHILMAYDI: uch kunlik qatorlar (500 + 700 + 300) bo'lsa asosiy raqam
    /// <b>700</b> (pastki chegara), yuqori chegara esa 1500 — va u TAXMINIY deb belgilanadi.
    /// </summary>
    [Fact]
    public async Task Qamrov_qoshilmaydi_pastki_va_yuqori_chegara_beriladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 1_000_000, reach: 500));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-06", spendMinor: 1_000_000, reach: 700));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-07", spendMinor: 1_000_000, reach: 300));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(700, report.Totals.Reach);
        Assert.Equal(1500, report.Totals.ReachUpper);
        Assert.True(report.Totals.ReachApprox);
        Assert.Contains(report.Notes, n => n.Contains("Qamrov TAXMINIY"));
    }

    // ═══════════════════════ 7) Filtrlar va bo'sh holat ═══════════════════════

    /// <summary>Platforma filtri xarajatni ham, lidni ham ajratadi
    /// (<c>ig</c>/<c>fb</c> qisqartmalari <c>instagram</c>/<c>facebook</c> ga moslanadi).</summary>
    [Fact]
    public async Task Platforma_filtri_xarajat_va_lidni_ajratadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000,
                       platform: MetaAdsRoi.PlatformInstagram));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 4_000_000,
                       platform: MetaAdsRoi.PlatformFacebook));

        var ig = NewLead(db, "Instagram lid", "+998901112233");
        var fb = NewLead(db, "Facebook lid", "+998901112244");
        db.Add(AdLead("lg1", "c1", ig.Id, platform: MetaAdsRoi.LeadPlatformIg));
        db.Add(AdLead("lg2", "c1", fb.Id, platform: MetaAdsRoi.LeadPlatformFb));
        await db.SaveChangesAsync();

        var all = await Build(db);
        Assert.Equal(14_000_000, all.Totals.SpendMinor);
        Assert.Equal(2, all.Totals.CrmLeads);

        var instagram = await Build(db, MetaAdsRoi.PlatformInstagram);
        Assert.Equal(10_000_000, instagram.Totals.SpendMinor);
        Assert.Equal(1, instagram.Totals.CrmLeads);

        var facebook = await Build(db, MetaAdsRoi.PlatformFacebook);
        Assert.Equal(4_000_000, facebook.Totals.SpendMinor);
        Assert.Equal(1, facebook.Totals.CrmLeads);
    }

    /// <summary>Kampaniya filtri: boshqa kampaniyaning xarajati ham, lidi ham kirmaydi.</summary>
    [Fact]
    public async Task Kampaniya_filtri_faqat_oz_qatorlarini_qoldiradi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Node(MetaAdsRoi.LevelCampaign, "c2", "", "Kuz-2026"));
        db.Add(Node(MetaAdsRoi.LevelAdset, "as2", "c2", "Viloyat"));
        db.Add(Node(MetaAdsRoi.LevelAd, "ad2", "as2", "Kreativ 2"));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad2", "2026-08-05", spendMinor: 7_000_000));

        var a = NewLead(db, "Birinchi", "+998901112233");
        var b = NewLead(db, "Ikkinchi", "+998901112244");
        db.Add(AdLead("lg1", "c1", a.Id));
        db.Add(AdLead("lg2", "c2", b.Id, adsetId: "as2", adId: "ad2"));
        await db.SaveChangesAsync();

        var report = await Build(db, campaignId: "c2");

        Assert.Equal(7_000_000, report.Totals.SpendMinor);
        Assert.Equal(1, report.Totals.CrmLeads);
        Assert.Equal("c2", Assert.Single(report.Campaigns).Id);
    }

    /// <summary>Reklama akkaunti ULANMAGAN bo'lsa: istisno YO'Q, bo'sh lekin tushunarli javob.</summary>
    [Fact]
    public async Task Akkaunt_ulanmagan_bolsa_bosh_lekin_tushunarli_javob()
    {
        using var t = TestDb.Sqlite();
        var report = await Build(t.Context);

        Assert.False(report.Connected);
        Assert.Empty(report.Campaigns);
        Assert.Empty(report.Daily);
        Assert.Equal(0, report.Totals.SpendMinor);
        Assert.Contains(report.Notes, n => n.Contains("ulanmagan"));
    }

    /// <summary>Oraliqdan TASHQARIDAGI xarajat va lid hisobga kirmaydi
    /// (<c>to</c> — kunning OXIRIGACHA, zona qo'shimchali ISO ham kiradi).</summary>
    [Fact]
    public async Task Oraliqdan_tashqaridagi_qatorlar_kirmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-07-31", spendMinor: 9_000_000));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-31", spendMinor: 1_000_000));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-09-01", spendMinor: 9_000_000));

        var inRange = NewLead(db, "Ichkarida", "+998901112233");
        var outRange = NewLead(db, "Tashqarida", "+998901112244");
        // Oxirgi kunning oxirgi soniyasi, Meta uslubidagi zona qo'shimchasi bilan.
        db.Add(AdLead("lg1", "c1", inRange.Id, created: "2026-08-31T23:59:59+0000"));
        db.Add(AdLead("lg2", "c1", outRange.Id, created: "2026-09-01T00:00:01+0000"));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(1_000_000, report.Totals.SpendMinor);
        Assert.Equal(1, report.Totals.CrmLeads);
    }

    // ═══════════════════════ 8) Kunlik qator va kesimlar ═══════════════════════

    /// <summary>Kunlik qator sanaga qarab o'sish tartibida va lidlar O'Z kunida sanaladi.</summary>
    [Fact]
    public async Task Kunlik_qator_tartibli_va_lidlar_oz_kunida()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-06", spendMinor: 2_000_000));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 1_000_000));

        var a = NewLead(db, "Birinchi", "+998901112233");
        var b = NewLead(db, "Ikkinchi", "+998901112244");
        db.Add(AdLead("lg1", "c1", a.Id, created: "2026-08-05T09:00:00"));
        db.Add(AdLead("lg2", "c1", b.Id, created: "2026-08-06T09:00:00"));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(2, report.Daily.Count);
        Assert.Equal("2026-08-05", report.Daily[0].Date);
        Assert.Equal(1_000_000, report.Daily[0].SpendMinor);
        Assert.Equal(1, report.Daily[0].CrmLeads);
        Assert.Equal("2026-08-06", report.Daily[1].Date);
        Assert.Equal(1, report.Daily[1].CrmLeads);
    }

    /// <summary>Platforma kesimi xarajat bo'yicha kamayish tartibida qaytadi.</summary>
    [Fact]
    public async Task Platforma_kesimi_xarajat_boyicha_tartiblanadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 3_000_000,
                       platform: MetaAdsRoi.PlatformFacebook));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 8_000_000,
                       platform: MetaAdsRoi.PlatformInstagram));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(2, report.Platforms.Count);
        Assert.Equal(MetaAdsRoi.PlatformInstagram, report.Platforms[0].Platform);
        Assert.Equal(8_000_000, report.Platforms[0].SpendMinor);
    }

    // ═══════════════════════ 9) Sof funksiyalar ═══════════════════════

    /// <summary>CPL/CAC — nolga bo'lish <c>null</c>, aks holda yaxlitlangan minor qiymat.</summary>
    [Theory]
    [InlineData(0L, 5, null)]
    [InlineData(10_000_000L, 0, null)]
    [InlineData(-5L, 5, null)]
    [InlineData(10_000_000L, 4, 2_500_000L)]
    [InlineData(10L, 4, 3L)]            // 2.5 → 3 (yarmi yuqoriga)
    public void CplMinor_qoidalari(long spend, int leads, long? expected)
        => Assert.Equal(expected, MetaAdsRoi.CplMinor(spend, leads));

    /// <summary>ROI = (daromad − xarajat) / xarajat; xarajat ≤ 0 → <c>null</c>.</summary>
    [Theory]
    [InlineData(0L, 0L, null)]
    [InlineData(30L, 0L, null)]
    [InlineData(0L, 100L, -1.0)]
    [InlineData(250L, 100L, 1.5)]
    [InlineData(100L, 100L, 0.0)]
    public void RoiRatio_qoidalari(long revenue, long spend, double? expected)
        => Assert.Equal(expected is null ? null : (decimal?)expected,
                        MetaAdsRoi.RoiRatio(revenue, spend));

    /// <summary>Daraja tanlash — eng maydasi ustun; hech biri bo'lmasa bo'sh satr.</summary>
    [Fact]
    public void PickLevel_eng_maydasini_tanlaydi()
    {
        Assert.Equal(MetaAdsRoi.LevelAd, MetaAdsRoi.PickLevel(["campaign", "ad", "adset"]));
        Assert.Equal(MetaAdsRoi.LevelAdset, MetaAdsRoi.PickLevel(["campaign", "adset"]));
        Assert.Equal(MetaAdsRoi.LevelCampaign, MetaAdsRoi.PickLevel(["campaign"]));
        Assert.Equal("", MetaAdsRoi.PickLevel([null, "", "  "]));
    }

    /// <summary>Platforma nomlari: <c>ig</c>/<c>fb</c> qisqartmalari to'liq nomga keladi,
    /// noma'lum qiymat esa <c>all</c> ga (bo'sh ekran ko'rsatilmasin).</summary>
    [Fact]
    public void Platforma_nomlari_normallashadi()
    {
        Assert.Equal(MetaAdsRoi.PlatformInstagram, MetaAdsRoi.NormalizePlatform("ig"));
        Assert.Equal(MetaAdsRoi.PlatformFacebook, MetaAdsRoi.NormalizePlatform("FACEBOOK"));
        Assert.Equal(MetaAdsRoi.PlatformAll, MetaAdsRoi.NormalizePlatform("tiktok"));
        Assert.Equal(MetaAdsRoi.PlatformAll, MetaAdsRoi.NormalizePlatform(null));

        Assert.True(MetaAdsRoi.LeadMatchesPlatform("ig", MetaAdsRoi.PlatformInstagram));
        Assert.False(MetaAdsRoi.LeadMatchesPlatform("fb", MetaAdsRoi.PlatformInstagram));
        Assert.True(MetaAdsRoi.LeadMatchesPlatform("", MetaAdsRoi.PlatformAll));

        // ⚠️ Bo'linmasiz ("all") yuklangan xarajat platforma tanlanganda KIRMAYDI.
        Assert.False(MetaAdsRoi.MatchesPlatform("all", MetaAdsRoi.PlatformInstagram));
        Assert.True(MetaAdsRoi.MatchesPlatform("all", MetaAdsRoi.PlatformAll));
    }

    /// <summary>Qamrov chegaralari — sof funksiya darajasida.</summary>
    [Fact]
    public void ReachOf_max_va_sum_qaytaradi()
    {
        var (lower, upper) = MetaAdsRoi.ReachOf([500, 700, 300]);
        Assert.Equal(700, lower);
        Assert.Equal(1500, upper);

        var (emptyLower, emptyUpper) = MetaAdsRoi.ReachOf([]);
        Assert.Equal(0, emptyLower);
        Assert.Equal(0, emptyUpper);
    }

    /// <summary>Kun oxiri chegarasi zona qo'shimchali ISO'dan ham KATTA bo'lishi shart.</summary>
    [Fact]
    public void DayEnd_zona_qoshimchali_ISO_dan_katta()
    {
        var bound = MetaAdsRoi.DayEnd("2026-08-31");
        Assert.True(string.CompareOrdinal("2026-08-31T23:59:59+0000", bound) <= 0);
        Assert.True(string.CompareOrdinal("2026-08-31T23:59:59", bound) <= 0);
        Assert.True(string.CompareOrdinal("2026-09-01T00:00:00", bound) > 0);
    }

    // ═══════════════════════ 8) MsgStarted va atributsiya oynasi ═══════════════════════

    /// <summary>
    /// «Yozishma boshlandi» (Click-to-Direct) — daraxtning HAR bir darajasiga ko'tariladi va
    /// lidlarga QO'SHILMAYDI.
    ///
    /// <para>⚠️ Aynan shu holat modulning bo'shlig'i edi: forma YO'Q kampaniyada
    /// <c>MetaLeads</c> nol turadi va reklama "hech narsa keltirmagan" bo'lib ko'rinardi.</para>
    /// </summary>
    [Fact]
    public async Task Yozishma_boshlandi_daraxt_boylab_kotariladi_va_lidga_qoshilmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        // Forma lidi UMUMAN yo'q — faqat DM orqali kelgan yozishmalar.
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 10_000_000, msgStarted: 3));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-06", spendMinor: 5_000_000, msgStarted: 4));
        await db.SaveChangesAsync();

        var report = await Build(db);

        Assert.Equal(7, report.Totals.MsgStarted);
        Assert.Equal(0, report.Totals.MetaLeads);   // lidlar bilan aralashmaydi
        Assert.Equal(0, report.Totals.CrmLeads);

        var campaign = Assert.Single(report.Campaigns);
        Assert.Equal(7, campaign.MsgStarted);
        var adset = Assert.Single(campaign.Children);
        Assert.Equal(7, adset.MsgStarted);
        var ad = Assert.Single(adset.Children);
        Assert.Equal(7, ad.MsgStarted);
    }

    /// <summary>Atributsiya oynasi Meta bergan HOLICHA qaytadi (tarjima qilinmaydi).</summary>
    [Fact]
    public async Task Atributsiya_oynasi_Meta_bergan_holicha_qaytadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 1_000_000,
                       attributionSetting: "7d_click,1d_view"));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-06", spendMinor: 1_000_000,
                       attributionSetting: "7d_click,1d_view"));
        await db.SaveChangesAsync();

        var report = await Build(db);
        Assert.Equal("7d_click,1d_view", report.AttributionSetting);
    }

    /// <summary>
    /// Davr ichida sozlama O'ZGARGAN bo'lsa IKKALASI ham ko'rsatiladi — jimgina bittasi
    /// tanlansa hisobot "bitta oyna bo'yicha" bo'lib ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Bir_nechta_atributsiya_oynasi_hammasi_sanab_korsatiladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 1_000_000,
                       attributionSetting: "1d_click"));
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-06", spendMinor: 1_000_000,
                       attributionSetting: "7d_click,1d_view"));
        await db.SaveChangesAsync();

        var report = await Build(db);
        Assert.Contains("1d_click", report.AttributionSetting);
        Assert.Contains("7d_click,1d_view", report.AttributionSetting);
    }

    /// <summary>Meta qiymat bermagan bo'lsa — BO'SH satr (sun'iy matn o'ylab topilmaydi).</summary>
    [Fact]
    public async Task Atributsiya_oynasi_berilmagan_bolsa_bosh_qoladi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.Add(NewAccount());
        AddTree(db);
        db.Add(Insight(MetaAdsRoi.LevelAd, "ad1", "2026-08-05", spendMinor: 1_000_000));
        await db.SaveChangesAsync();

        var report = await Build(db);
        Assert.Equal("", report.AttributionSetting);
    }

    /// <summary>Akkaunt ulanmagan bo'sh hisobotda ham maydonlar mavjud va NOL/BO'SH.</summary>
    [Fact]
    public async Task Akkauntsiz_bosh_hisobotda_yangi_maydonlar_nol()
    {
        using var t = TestDb.Sqlite();
        var report = await Build(t.Context);

        Assert.False(report.Connected);
        Assert.Equal(0, report.Totals.MsgStarted);
        Assert.Equal("", report.AttributionSetting);
    }
}
