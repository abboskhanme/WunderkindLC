using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// Qarzdorlik → «Bog'lanish kerak» (`contacts.md` §9.5). Egasining talabi (2026-10-27): oyning 20-sanasigacha to'lamagan
/// qarzdor avtomatik navbatga tushadi, to'lov qilinib qarz yopilganda talab o'zi yopiladi.
/// </summary>
public class DebtContactTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static readonly DateOnly Day19 = new(2031, 3, 19);
    private static readonly DateOnly Day20 = new(2031, 3, 20);

    [Fact]
    public void Muddat_20_sanadan_boshlanadi()
    {
        Assert.False(DebtContactRules.IsOpenSeason(Day19, 20));
        Assert.True(DebtContactRules.IsOpenSeason(Day20, 20));
        Assert.True(DebtContactRules.IsOpenSeason(new DateOnly(2031, 3, 31), 20));
        Assert.Equal("2031-03", DebtContactRules.MonthKey(Day20));
    }

    [Fact]
    public void Qarz_tolandimi()
    {
        Assert.True(DebtContactRules.Settled(false, false, 0));
        Assert.True(DebtContactRules.Settled(false, false, 5000));
        Assert.False(DebtContactRules.Settled(false, false, -1));   // qisman to'lov — hali qarzdor
        Assert.True(DebtContactRules.Settled(false, true, -100));    // arxivlandi — kuzatish tugaydi
        Assert.True(DebtContactRules.Settled(true, false, -100));    // o'quvchi o'chirildi
    }

    [Fact]
    public void Izoh_matni_kurslar_boyicha()
    {
        Assert.Equal("To'lov qarzdorligi: 450 000 so'm — Ingliz tili: 300 000 so'm; Matematika: 150 000 so'm",
            DebtContactRules.NoteText([("Ingliz tili", 300_000m), ("Matematika", 150_000m)]));
        Assert.Equal("To'lov qarzdorligi: 1 000 so'm — 1 000 so'm", DebtContactRules.NoteText([("", 1_000m)]));
    }

    // ---------------------------------------------------------------- servis

    private static Student Debtor(TestDb db, string name = "Ali Valiyev", decimal balance = -300_000m, bool archived = false)
    {
        var s = new Student { FullName = name, Balance = balance, ClassName = "IELTS-1", IsArchived = archived };
        db.Context.Students.Add(s);
        db.Context.SaveChanges();
        return s;
    }

    private static Task<DebtContactService.RunResult> RunAsync(TestDb db, DateOnly day) =>
        DebtContactService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()), day);

    private static List<ContactRequest> Requests(TestDb db) => db.Context.ContactRequests.AsNoTracking().ToList();

    [Fact]
    public async Task Yigirmanchi_sanadan_qarzdorga_talab_ochiladi_qayta_yugurishda_takror_yoq()
    {
        using var db = TestDb.Sqlite();
        await DebtContactService.SeedReasonAsync(db.Context);
        var debtor = Debtor(db);
        Debtor(db, "Arxivdagi", archived: true);
        Debtor(db, "Qarzi yo'q", balance: 0);

        Assert.Equal(new DebtContactService.RunResult(0, 0, 0), await RunAsync(db, Day19));
        Assert.Empty(Requests(db));

        Assert.Equal(new DebtContactService.RunResult(1, 0, 0), await RunAsync(db, Day20));
        var req = Assert.Single(Requests(db));
        Assert.Equal(debtor.Id, req.StudentId);
        Assert.Equal(DebtContactService.ReasonLabel, req.ReasonLabel);
        Assert.NotEqual("", req.ReasonId);
        Assert.Equal(ContactStatuses.New, req.Status);
        Assert.Equal("To'lov qarzdorligi: 300 000 so'm — IELTS-1: 300 000 so'm", req.Note);
        Assert.Equal(DebtContactService.ActorName, req.CreatedBy);

        // har 30 daqiqada yuguradi — shu oy ikkinchi talab yo'q (operator yopgan bo'lsa ham)
        Assert.Equal(new DebtContactService.RunResult(0, 0, 0), await RunAsync(db, Day20.AddDays(3)));
        Assert.Single(Requests(db));
    }

    [Fact]
    public async Task Tolov_qilinsa_talab_ozi_yopiladi_qisman_tolovda_yoq()
    {
        using var db = TestDb.Sqlite();
        var debtor = Debtor(db);
        await RunAsync(db, Day20);

        // qisman to'lov — hali qarzdor, talab ochiq
        db.Context.Students.Single(s => s.Id == debtor.Id).Balance = -100_000m;
        db.Context.SaveChanges();
        Assert.Equal(0, (await RunAsync(db, Day20.AddDays(1))).Closed);
        Assert.Equal(ContactStatuses.New, Assert.Single(Requests(db)).Status);

        // to'liq to'landi — avtomatik «Hal bo'ldi»
        db.Context.Students.Single(s => s.Id == debtor.Id).Balance = 0;
        db.Context.SaveChanges();
        Assert.Equal(1, (await RunAsync(db, Day20.AddDays(2))).Closed);
        var req = Assert.Single(Requests(db));
        Assert.Equal(ContactStatuses.Done, req.Status);
        Assert.Equal(DebtContactService.ActorName, req.ClosedBy);
        Assert.NotEqual("", req.ClosedAt);
        var ev = db.Context.ContactAttempts.AsNoTracking().Single(a => a.RequestId == req.Id && a.Type == ContactAttemptTypes.Note);
        Assert.Equal(ContactStatuses.Done, ev.NextStatus);
        Assert.Equal(DebtContactRules.PaidText, ev.Response);
        Assert.NotNull(db.Context.DebtContactAlerts.AsNoTracking().Single().ResolvedAt);

        // keyingi sikllar unga tegmaydi
        Assert.Equal(new DebtContactService.RunResult(0, 0, 0), await RunAsync(db, Day20.AddDays(3)));
    }

    [Fact]
    public async Task Ochiq_talab_bolsa_izoh_qoshiladi_va_tolovda_u_YOPILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var debtor = Debtor(db);
        var audit = new AuditService(db.Context, new NoHttpContext());
        new ContactQueueService(db.Context, audit).Add(debtor, "", "Ketma-ket darsga kelmadi", "A1: 2 ta dars", "", "", "Tizim");
        db.Context.SaveChanges();

        Assert.Equal(new DebtContactService.RunResult(0, 1, 0), await RunAsync(db, Day20));
        var req = Assert.Single(Requests(db));
        Assert.Contains(db.Context.ContactAttempts.AsNoTracking().Where(a => a.RequestId == req.Id).ToList(),
            a => a.Type == ContactAttemptTypes.Note && a.Response.StartsWith("To'lov qarzdorligi"));

        // qarz to'landi, lekin talab BOSHQA sabab bilan ochilgan — kelmaslik bo'yicha qo'ng'iroq hamon kerak
        db.Context.Students.Single(s => s.Id == debtor.Id).Balance = 0;
        db.Context.SaveChanges();
        await RunAsync(db, Day20.AddDays(1));
        Assert.Equal(ContactStatuses.New, Assert.Single(Requests(db)).Status);
    }

    [Fact]
    public async Task Operator_ozi_yopgan_talabga_tegilmaydi()
    {
        using var db = TestDb.Sqlite();
        var debtor = Debtor(db);
        await RunAsync(db, Day20);
        var req = db.Context.ContactRequests.Single();
        req.Status = ContactStatuses.Failed;
        db.Context.SaveChanges();

        db.Context.Students.Single(s => s.Id == debtor.Id).Balance = 0;
        db.Context.SaveChanges();
        Assert.Equal(0, (await RunAsync(db, Day20.AddDays(1))).Closed);
        Assert.Equal(ContactStatuses.Failed, Assert.Single(Requests(db)).Status);
        Assert.NotNull(db.Context.DebtContactAlerts.AsNoTracking().Single().ResolvedAt);
    }

    private static void ServiceSettings(TestDb db, bool enabled, int day)
    {
        var meta = db.Context.CenterMeta.FirstOrDefault();
        if (meta is null)
        {
            meta = new CenterMeta();
            db.Context.CenterMeta.Add(meta);
        }
        meta.ContactDebtAutoEnabled = enabled;
        meta.ContactDebtDay = day;
        db.Context.SaveChanges();
    }

    [Fact]
    public async Task Sozlamadagi_sana_ishlatiladi()
    {
        using var db = TestDb.Sqlite();
        Debtor(db);
        ServiceSettings(db, enabled: true, day: 10);
        Assert.Equal(0, (await RunAsync(db, new DateOnly(2031, 3, 9))).Requests);
        Assert.Equal(1, (await RunAsync(db, new DateOnly(2031, 3, 10))).Requests);
    }

    [Fact]
    public async Task Ochirilgan_sozlama_yangi_talab_ochmaydi_lekin_tolanganini_yopaveradi()
    {
        using var db = TestDb.Sqlite();
        var debtor = Debtor(db);
        await RunAsync(db, Day20);
        ServiceSettings(db, enabled: false, day: 20);
        Debtor(db, "Yangi qarzdor");
        Assert.Equal(0, (await RunAsync(db, Day20.AddDays(1))).Requests);

        db.Context.Students.Single(s => s.Id == debtor.Id).Balance = 0;
        db.Context.SaveChanges();
        Assert.Equal(1, (await RunAsync(db, Day20.AddDays(2))).Closed);
    }

    [Fact]
    public async Task Arxivlanganda_ochiq_talab_yopiladi()
    {
        using var db = TestDb.Sqlite();
        var debtor = Debtor(db);
        await RunAsync(db, Day20);
        var queue = new ContactQueueService(db.Context, new AuditService(db.Context, new NoHttpContext()));

        Assert.Equal(1, await queue.CloseForArchivedAsync(debtor.Id, "Tizim"));
        db.Context.SaveChanges();
        var req = Assert.Single(Requests(db));
        Assert.Equal(ContactStatuses.Done, req.Status);
        Assert.Equal(ContactQueueService.ArchivedText, req.LastResponse);
        Assert.Equal(0, await queue.CloseForArchivedAsync(debtor.Id, "Tizim"));   // yopilganga qayta tegilmaydi
    }

    // ---------------------------------------------------------------- taxta ustunlari (2026-10-27)

    [Fact]
    public void Karta_ustuni_yashirilsa_shu_bosqichning_birinchi_ustuniga_tushadi()
    {
        (string, string)[] all = [("new", "new"), ("sms", "callback"), ("callback", "callback"), ("done", "done")];
        Assert.Equal("sms", ContactService.StageHomeOf("sms", "callback", all));
        Assert.Equal("callback", ContactService.StageHomeOf("", "callback", all));
        // tizim «Qayta qo'ng'iroq» ustuni o'chirildi — shu bosqichning birinchi ko'rinadigan ustuni
        (string, string)[] noCallback = [("new", "new"), ("sms", "callback")];
        Assert.Equal("sms", ContactService.StageHomeOf("", "callback", noCallback));
        Assert.Equal("sms", ContactService.StageHomeOf("o'chirilgan-ustun", "callback", noCallback));
        // yakuniy holat ustuni yo'q — taxtada yo'q (taxtada faqat ochiqlar)
        Assert.Null(ContactService.StageHomeOf("", "failed", noCallback));
    }

    [Fact]
    public void Ustunni_ochirish_faqat_ochiq_bosqichning_oxirgisida_toxtaydi()
    {
        Assert.NotNull(ContactService.StageDeleteError(ContactStatuses.New, 0));
        Assert.NotNull(ContactService.StageDeleteError(ContactStatuses.Callback, 0));
        Assert.Null(ContactService.StageDeleteError(ContactStatuses.Callback, 1));
        Assert.Null(ContactService.StageDeleteError(ContactStatuses.Done, 0));
        Assert.Null(ContactService.StageDeleteError(ContactStatuses.Failed, 0));
    }

    [Fact]
    public async Task Sabab_bir_marta_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        Assert.True(await DebtContactService.SeedReasonAsync(db.Context));
        Assert.False(await DebtContactService.SeedReasonAsync(db.Context));
        var reason = Assert.Single(db.Context.ActionReasons.AsNoTracking().Where(r => r.Category == "contact").ToList());
        Assert.Equal(DebtContactService.ReasonLabel, reason.Label);
        Assert.Equal(reason.Id, db.Context.CenterMeta.AsNoTracking().Single().DebtContactReasonId);
    }
}
