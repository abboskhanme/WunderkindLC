using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// OMMAVIY SMS NAVBATI (<see cref="SmsQueueService"/>) testlari.
///
/// <para>Nima uchun bu modul bor: ilgari partiya HTTP so'rovi ichida yuborilardi va barcha
/// <see cref="SmsLog"/> yozuvlari faqat siklning OXIRIDA saqlanardi. Ko'p oluvchida so'rov
/// proksi chegarasidan (Cloudflare — 100 s) oshib ketardi: ulanish uzilar, sikl to'xtar,
/// SMS'lar esa ketgan bo'lardi — natijada TARIXDA IZ QOLMASDI. Shu sabab quyidagi ikki xulq
/// qoplangan: (1) qaysi partiya fonga tushadi, (2) har SMS'dan keyin yozuv saqlanadi.</para>
/// </summary>
public class SmsQueueTests
{
    /// <summary>Tarmoqqa chiqmaydigan navbat xizmati (fon ishchisi ishga tushirilmaydi —
    /// testda faqat <c>RunInlineAsync</c> chaqiriladi).</summary>
    private static SmsQueueService Stack(IntellectCRM.Application.Abstractions.IAppDbContext db)
    {
        var http = new NoNetworkHttpClientFactory();
        var config = new ConfigurationBuilder().Build();
        var eskiz = new EskizService(config, http, NullLogger<EskizService>.Instance);
        var fcm = new FcmService(http, NullLogger<FcmService>.Instance);
        var cti = new CtiSmsService(new CtiConnectionManager(), fcm, NullLogger<CtiSmsService>.Instance);
        return new SmsQueueService(
            new SingleServiceProvider(db), eskiz, cti, NullLogger<SmsQueueService>.Instance);
    }

    [Fact]
    public void ShouldQueue_faqat_chegaradan_katta_partiyani_fonga_beradi()
    {
        // Kichik partiya (bitta o'quvchi/lidga yuborish) so'rov ichida ketadi — admin natijani
        // darhol ko'radi va bu yerda kutish xavfi yo'q.
        Assert.False(SmsQueueService.ShouldQueue(1));
        Assert.False(SmsQueueService.ShouldQueue(SmsQueueService.InlineLimit));
        // Undan kattasi — FONDA (aks holda javob proksi chegarasiga urilardi).
        Assert.True(SmsQueueService.ShouldQueue(SmsQueueService.InlineLimit + 1));
        Assert.True(SmsQueueService.ShouldQueue(300));
    }

    [Fact]
    public async Task RunInline_har_oluvchi_uchun_jurnal_yozadi_va_partiyani_yangilaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var batch = new SmsBatch { Audience = "Test", Message = "Salom", RecipientCount = 2, SentCount = 0 };
        ctx.SmsBatches.Add(batch);
        ctx.SaveChanges();

        var job = new SmsQueueService.Job(batch.Id, "eskiz", null, null, new[]
        {
            new SmsQueueService.Target("998901234567", "Ali", "Salom Ali"),
            new SmsQueueService.Target("998907654321", "Vali", "Salom Vali"),
        });

        await Stack(ctx).RunInlineAsync(ctx, job);

        // Eskiz sozlanmagan (testda tarmoq yopiq) — YUBORILMAYDI, lekin HAR oluvchi uchun
        // jurnal qatori qolishi SHART: "nima bo'lgani" tarixdan ko'rinsin.
        var logs = ctx.SmsLogs.Where(l => l.BatchId == batch.Id).ToList();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.NotEqual("", l.Status));
        Assert.Equal(0, batch.SentCount);
    }

    [Fact]
    public async Task RunInline_lid_uchun_timeline_yozuvini_qoshadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var batch = new SmsBatch { Audience = "Lid", Message = "Salom", RecipientCount = 1 };
        ctx.SmsBatches.Add(batch);
        ctx.SaveChanges();

        var job = new SmsQueueService.Job(
            batch.Id, "eskiz", null, null,
            [new SmsQueueService.Target("998901234567", "Lid", "Salom", LeadId: "lead-1")],
            LeadNote: "SMS yuborildi: Salom", ActorName: "Admin");

        await Stack(ctx).RunInlineAsync(ctx, job);

        // Lid tarixi (timeline) yuborish MUVAFFAQIYATSIZ bo'lganda ham yoziladi — operator
        // "SMS urinildi" faktini ko'rishi kerak.
        var ev = Assert.Single(ctx.LeadEvents.Where(e => e.LeadId == "lead-1"));
        Assert.Equal("Admin", ev.ActorName);
        Assert.Contains("SMS yuborildi", ev.Text);
    }
}
