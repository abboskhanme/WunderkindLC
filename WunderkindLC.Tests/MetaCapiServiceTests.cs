using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CAPI NAVBATI (<see cref="MetaCapiService"/>) — kunlik skandan Meta'ga yuborishgacha
/// UCHDAN-UCHGACHA testlar. Rasmiy manba: <c>KENGAYTIRISH-PROMPT.md</c> §7.5–§7.7.
///
/// <para>Meta javobi <see cref="RecordingHandler"/> bilan soxtalashtiriladi — test
/// "tashqariga so'rov KETDIMI?" degan savolga ham javob beradi (modul o'chiq bo'lsa
/// ketmasligi SHART: hashlangan bo'lsa ham bu mijoz ma'lumotini uzatish).</para>
///
/// <para><b>NIMA TEST QILINMAYDI:</b> hashlash va payload shakli — ular
/// <c>MetaCapiTests</c> da (sof funksiyalar) allaqachon qoplangan. Bu yerda faqat NAVBAT
/// mantig'i: qaysi hodisa qachon yaratiladi, dedup, eskirgan qator va maxfiylik.</para>
/// </summary>
public class MetaCapiServiceTests
{
    /// <summary>Meta'ning muvaffaqiyatli javobi.</summary>
    private const string CapiOk = """{"events_received":3,"fbtrace_id":"tr-1"}""";

    private const string QualifiedName = "Sifatli lid";
    private const string WonName = "To'lov qildi";

    private static CenterMeta Meta(bool enabled) => new()
    {
        // ⚠️ Dataset va token TO'LDIRILGAN holda qoldiriladi: modul o'chiqligi TESTI aynan
        //    bayroqni sinasin, "sozlanmagan" holatni emas.
        InstagramCapiEnabled = enabled,
        InstagramCapiDatasetId = "ds-1",
        InstagramCapiToken = "tok-1",
        InstagramCapiStageQualified = QualifiedName,
        InstagramCapiStageWon = WonName,
    };

    private static (MetaCapiService Svc, RecordingHandler Handler) Build(TestDb db)
    {
        var handler = new RecordingHandler(body: CapiOk);
        var api = new MetaCapiApi(new HttpClient(handler), NullLogger<MetaCapiApi>.Instance);
        var svc = new MetaCapiService(db.Context, api, NullLogger<MetaCapiService>.Instance);
        return (svc, handler);
    }

    /// <summary>Reklama formasidan kelgan lid + (ixtiyoriy) konvertatsiya va to'lov.</summary>
    private static void SeedLead(
        TestDb db, string leadId, string leadgenId, string phone,
        string stageId = "", string? studentId = null, decimal paid = 0m, string paidDate = "")
    {
        db.Context.Leads.Add(new Lead
        {
            Id = leadId,
            FullName = "Ali Valiyev",
            Phone = phone,
            Stage = stageId,
            ConvertedStudentId = studentId,
            CreatedAt = AppClock.Iso(),
        });

        db.Context.IgAdLeads.Add(new IgAdLead
        {
            LeadId = leadId,
            LeadgenId = leadgenId,
            PageId = "page-1",
            FormId = "form-1",
            FullName = "Ali Valiyev",
            Phone = phone,
            CreatedTime = AppClock.Iso(),
            ReceivedAt = AppClock.Iso(),
        });

        if (paid > 0m && studentId is not null)
            db.Context.FinanceTransactions.Add(new FinanceTransaction
            {
                Direction = "income",
                Category = "tuition",
                StudentId = studentId,
                Amount = paid,
                Date = paidDate.Length > 0 ? paidDate : AppClock.Today.ToString("yyyy-MM-dd"),
            });
    }

    private static void SeedStages(TestDb db)
    {
        db.Context.LeadStages.Add(new LeadStage { Id = "s-new", Title = "Yangi", Order = 0 });
        db.Context.LeadStages.Add(new LeadStage { Id = "s-trial", Title = "Sinov darsi", Order = 1 });
    }

    /* ═════════════════════════ DARVOZA ═════════════════════════ */

    /// <summary>
    /// 🔴 Modul o'chiq bo'lsa TASHQARIGA HECH NARSA CHIQMAYDI — na so'rov, na navbat yozuvi.
    /// Hodisalarni "zaxiraga" yig'ib qo'yish ham MUMKIN EMAS: modul yoqilmagan degani markaz
    /// hali Meta'ning Data Protection Assessment shartlarini qabul qilmagan degani.
    /// </summary>
    [Fact]
    public async Task Modul_ochiq_bolsa_tashqariga_sorov_ketmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: false));
        SeedStages(db);
        SeedLead(db, "lead-1", "1234567890123456", "+998901234567",
            stageId: "s-trial", studentId: "st-1", paid: 500000m);
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);
        var (ok, created, sent, error) = await svc.ScanAndSendAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(0, created);
        Assert.Equal(0, sent);
        Assert.Contains("o'chirilgan", error);
        Assert.Empty(handler.Requests);
        Assert.Empty(db.Context.IgCapiEvents);
    }

    /* ═════════════════════════ HODISA XARITASI (§7.6) ═════════════════════════ */

    /// <summary>
    /// To'lov qilgan lid uchun "To'lov qildi" hodisasi (summa bilan) yaratiladi; to'lamagan
    /// va bosqichi "Yangi" bo'lgan lid uchun HECH QANDAY hodisa yaratilmaydi.
    /// </summary>
    [Fact]
    public async Task Tolov_qilgan_lid_uchun_won_hodisasi_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-paid", "1111111111111111", "+998901234567",
            stageId: "s-trial", studentId: "st-1", paid: 750000m);
        SeedLead(db, "lead-cold", "2222222222222222", "+998907654321", stageId: "s-new");
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);
        var (ok, created, sent, error) = await svc.ScanAndSendAsync(CancellationToken.None);

        Assert.True(ok, error);
        Assert.Equal("", error);

        var rows = await db.Context.IgCapiEvents.AsNoTracking().ToListAsync();

        // To'lagan lid: "sifatli" + "to'lov qildi" (u ham sifatli — voronkaning ikkala qadami).
        var paid = rows.Where(r => r.LeadId == "lead-paid").ToList();
        Assert.Equal(2, paid.Count);
        Assert.Contains(paid, r => r.EventName == QualifiedName);
        var won = Assert.Single(paid, r => r.EventName == WonName);

        // Sovuq lid: hech narsa (LID YARATILGANI uchun hodisa YO'Q — Meta buni biladi).
        Assert.DoesNotContain(rows, r => r.LeadId == "lead-cold");

        Assert.Equal(2, created);
        Assert.Equal(2, sent);
        Assert.All(paid, r => Assert.Equal(MetaCapiService.StatusSent, r.Status));
        Assert.All(paid, r => Assert.NotEqual("", r.SentAt));

        // "To'lov qildi" hodisasida summa va valyuta bo'lishi SHART (Meta ROAS bo'yicha
        // optimallashtira olsin).
        Assert.Contains("750000", won.PayloadJson);
        Assert.Contains("UZS", won.PayloadJson);

        // Bitta paket — bitta so'rov.
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// LID YARATILGANI uchun hodisa YUBORILMAYDI (§7.6 birinchi qatori) — bosqichi ham,
    /// konvertatsiyasi ham, to'lovi ham yo'q lid navbatga umuman tushmaydi.
    /// </summary>
    [Fact]
    public async Task Lid_yaratilgani_uchun_hodisa_yaratilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-new", "3333333333333333", "+998901112233", stageId: "s-new");
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);
        var (ok, created, sent, _) = await svc.ScanAndSendAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(0, created);
        Assert.Equal(0, sent);
        Assert.Empty(db.Context.IgCapiEvents);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Bosqich "Sinov darsi" bo'lsa (konvertatsiyasiz ham) "sifatli lid" hodisasi ketadi —
    /// §7.6: <c>Lead.Stage</c> sifatli/sinov bosqichiga o'tishi HAM trigger.
    /// </summary>
    [Fact]
    public async Task Sinov_darsi_bosqichi_sifatli_lid_hodisasini_beradi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-trial", "4444444444444444", "+998901112244", stageId: "s-trial");
        await db.Context.SaveChangesAsync();

        var (svc, _) = Build(db);
        await svc.ScanAndSendAsync(CancellationToken.None);

        var row = Assert.Single(db.Context.IgCapiEvents);
        Assert.Equal(QualifiedName, row.EventName);
        Assert.Equal(MetaCapiService.StatusSent, row.Status);
    }

    /* ═════════════════════════ DEDUP ═════════════════════════ */

    /// <summary>
    /// 🔴 Bir xil holat IKKI MARTA yuborilmaydi. Skan HAR KUNI ishlaydi va lidning holati
    /// o'zgarmaydi — dedup bo'lmasa bitta konversiya har kuni qayta yuborilib, Meta
    /// optimizatsiyasini buzardi.
    ///
    /// <para>⚠️ Dedup kaliti — (lid, hodisa nomi), <c>EventId</c> EMAS: <c>EventId</c> da vaqt
    /// bor va u ertasi kuni boshqacha chiqadi (ya'ni unikal indeks bu holatda ushlamasdi).</para>
    /// </summary>
    [Fact]
    public async Task Bir_xil_holat_ikki_marta_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-paid", "5555555555555555", "+998901234567",
            stageId: "s-trial", studentId: "st-9", paid: 400000m);
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);

        var first = await svc.ScanAndSendAsync(CancellationToken.None);
        var countAfterFirst = await db.Context.IgCapiEvents.CountAsync();

        var second = await svc.ScanAndSendAsync(CancellationToken.None);
        var countAfterSecond = await db.Context.IgCapiEvents.CountAsync();

        Assert.Equal(2, first.Created);
        Assert.Equal(2, countAfterFirst);

        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Sent);
        Assert.Equal(countAfterFirst, countAfterSecond);

        // Ikkinchi skanda yuboriladigan qator qolmagani uchun so'rov ham ketmaydi.
        Assert.Single(handler.Requests);
    }

    /* ═════════════════════════ ESKI HODISA (§7.5) ═════════════════════════ */

    /// <summary>
    /// 🔴 7 kundan eski <c>event_time</c> BUTUN so'rovni rad ettiradi. Shuning uchun eskirgan
    /// qator <c>skipped</c> bo'ladi va <b>paketni yiqitmaydi</b>: qolganlar baribir ketadi.
    ///
    /// <para>⚠️ <c>skipped</c> — <c>failed</c> EMAS va sababi OCHIQ yoziladi: "jimgina
    /// tashlab yuborilgan" hodisa admin uchun ko'rinmas nosozlik bo'lardi.</para>
    /// </summary>
    [Fact]
    public async Task Eski_hodisa_skipped_boladi_va_paketni_yiqitmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);

        // Eski to'lov (30 kun oldin) — "To'lov qildi" hodisasi vaqti ham o'sha sana.
        SeedLead(db, "lead-old", "6666666666666666", "+998901110001",
            stageId: "s-trial", studentId: "st-old", paid: 300000m,
            paidDate: AppClock.Today.AddDays(-30).ToString("yyyy-MM-dd"));

        // Bugungi to'lov — normal ketishi kerak.
        SeedLead(db, "lead-fresh", "7777777777777777", "+998901110002",
            stageId: "s-trial", studentId: "st-new", paid: 600000m);
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);
        var (ok, created, sent, error) = await svc.ScanAndSendAsync(CancellationToken.None);

        Assert.True(ok, error);
        Assert.Equal(4, created);   // 2 ta "sifatli" + 2 ta "to'lov qildi"

        var oldWon = await db.Context.IgCapiEvents.AsNoTracking()
            .SingleAsync(e => e.LeadId == "lead-old" && e.EventName == WonName);
        Assert.Equal(MetaCapiService.StatusSkipped, oldWon.Status);
        Assert.Contains("kundan eski", oldWon.Error);
        Assert.Equal("", oldWon.SentAt);

        // Qolgan uchtasi ketdi — bitta eskisi butun paketni yiqitmadi.
        Assert.Equal(3, sent);
        Assert.Single(handler.Requests);
        Assert.Equal(3, await db.Context.IgCapiEvents
            .CountAsync(e => e.Status == MetaCapiService.StatusSent));
    }

    /* ═════════════════════════ MAXFIYLIK (§7.7) ═════════════════════════ */

    /// <summary>
    /// 🔴 <c>PayloadJson</c> da XOM TELEFON YO'Q — faqat SHA-256 hex. Data Protection
    /// Assessment aynan shuni tekshiradi; bundan tashqari bu ustunni ko'rgan har qanday xodim
    /// mijoz raqamini olib qolardi.
    /// </summary>
    [Fact]
    public async Task Payloadda_xom_telefon_yoq_hashlangani_bor()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-p", "8888888888888888", "+998 90 123-45-67",
            stageId: "s-trial", studentId: "st-p", paid: 100000m);
        await db.Context.SaveChangesAsync();

        var (svc, _) = Build(db);
        await svc.ScanAndSendAsync(CancellationToken.None);

        var expected = MetaCapiHash.Phone("+998901234567");
        Assert.Equal(64, expected.Length);

        foreach (var row in await db.Context.IgCapiEvents.AsNoTracking().ToListAsync())
        {
            Assert.DoesNotContain("998901234567", row.PayloadJson);
            Assert.DoesNotContain("901234567", row.PayloadJson);
            Assert.DoesNotContain("123-45-67", row.PayloadJson);
            Assert.Contains(expected, row.PayloadJson);
        }
    }

    /* ═════════════════════════ NAVBATNI YUBORISH ═════════════════════════ */

    /// <summary>
    /// <see cref="MetaCapiService.SendPendingAsync"/> YANGI qator yaratmaydi — faqat navbatda
    /// turganini yuboradi (worker "skan" va "yuborish"ni ayri chaqira olsin).
    /// </summary>
    [Fact]
    public async Task Send_pending_yangi_qator_yaratmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: true));
        SeedStages(db);
        SeedLead(db, "lead-q", "9999999999999999", "+998901110003",
            stageId: "s-trial", studentId: "st-q", paid: 200000m);
        await db.Context.SaveChangesAsync();

        var (svc, handler) = Build(db);

        var (ok, sent, error) = await svc.SendPendingAsync(CancellationToken.None);

        Assert.True(ok, error);
        Assert.Equal(0, sent);
        Assert.Empty(db.Context.IgCapiEvents);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Bosqich nomini tanish — SOF funksiya (standart voronka nomlari bilan).</summary>
    [Theory]
    [InlineData("Sinov darsi", true)]
    [InlineData("Aylantirildi", true)]
    [InlineData("Sifatli lid", true)]
    [InlineData("Marketing Qualified Lead", true)]
    [InlineData("Yangi", false)]
    [InlineData("Bog'lanildi", false)]
    [InlineData("O'ylanmoqda", false)]
    [InlineData("", false)]
    public void Sifatli_bosqich_nomi_taniladi(string title, bool expected) =>
        Assert.Equal(expected, MetaCapiService.IsQualifiedStage(title));
}
