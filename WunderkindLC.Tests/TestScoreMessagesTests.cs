using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM U4a — «Ota-onalarga yuborish» (docs/SPEC-crm-upgrade.md P4.2/P4.7): `test_score` through the outbox, tokens
/// (ball, foiz, orin, guruh_orin, reyting_orin), unchanged scores are not re-sent, explicit resend, no rule → error.
/// </summary>
public class TestScoreMessagesTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private sealed record Fixture(TestDb Db, TestResult Test, Student Ali, Student Bek, Student Vali) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private const string Template =
        "{test}|{turi}|{sana}|{ball}|{foiz}|{orin}|{guruh_orin}|{reyting_orin}|{guruh}";

    private static Fixture Setup(bool rule = true)
    {
        var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "Ingliz A1" };
        ctx.Classes.Add(g);
        Student Add(string name)
        {
            var s = new Student { FullName = name, FirstName = name };
            ctx.Students.Add(s);
            ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active" });
            return s;
        }
        var ali = Add("Ali");
        var bek = Add("Bek");
        var vali = Add("Vali");   // no Telegram
        var archived = Add("Arxiv");
        archived.IsArchived = true;
        var t = new TestResult
        {
            GroupId = g.Id, Name = "Unit 5", Date = AppClock.Today.ToString("yyyy-MM-dd"), Kind = "interim",
            QuestionCount = 30, MaxScore = 30, CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        };
        ctx.TestResults.Add(t);
        ctx.TestScores.AddRange(
            new TestScore { TestResultId = t.Id, StudentId = ali.Id, Score = 30 },
            new TestScore { TestResultId = t.Id, StudentId = bek.Id, Score = 25 },
            new TestScore { TestResultId = t.Id, StudentId = vali.Id, Score = 20 },
            new TestScore { TestResultId = t.Id, StudentId = archived.Id, Score = 10 });
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 11, Relation = "father" },
            new TelegramRegistration { StudentId = bek.Id, ChatId = 21, Relation = "mother" },
            new TelegramRegistration { StudentId = bek.Id, ChatId = 22, Relation = "student" },   // not a parent chat
            new TelegramRegistration { StudentId = vali.Id, ChatId = 31, Relation = "father", BlockedAt = AppClock.Now },
            new TelegramRegistration { StudentId = archived.Id, ChatId = 41, Relation = "father" });
        if (rule)
            ctx.AutoMessageRules.Add(new AutoMessageRule
            {
                Trigger = AutoMessageTriggers.TestScore, Name = "Test", Enabled = true, SendTelegram = true,
                Template = Template, Audience = "parents",
            });
        ctx.SaveChanges();
        return new Fixture(db, t, ali, bek, vali);
    }

    private static Task<(Application.Dtos.SendTestResultsResultDto? Result, string? Error)> Send(
        Fixture f, bool resend = false) =>
        TestScoreMessages.SendAsync(f.Db.Context, new MessagingStack().Auto, RatingEngine.Direct(f.Db.Context),
            f.Test.Id, resend, "u-admin", new AuditService(f.Db.Context, new NoHttpContext()), AppClock.Today);

    [Fact]
    public void Tokenlar_ball_foiz_orin_va_reyting()
    {
        var t = new TestResult { Name = "Unit 5", Kind = "monthly", Date = "2026-10-12", QuestionCount = 30, MaxScore = 30 };
        var tokens = TestScoreMessages.Tokens(t, "A1", 25, 3, 18, "2/12", TestScoreMessages.NoRank);
        Assert.Equal("Unit 5", tokens["{test}"]);
        Assert.Equal("Oylik", tokens["{turi}"]);
        Assert.Equal("12.10.2026", tokens["{sana}"]);
        Assert.Equal("25/30", tokens["{ball}"]);
        Assert.Equal("83%", tokens["{foiz}"]);
        Assert.Equal("3/18", tokens["{orin}"]);
        Assert.Equal("2/12", tokens["{guruh_orin}"]);
        Assert.Equal("—", tokens["{reyting_orin}"]);
        Assert.Equal("A1", tokens["{guruh}"]);
        Assert.Equal("—", TestScoreMessages.Place(null, 10));
        Assert.Equal("t1:25", TestScoreMessages.EventKey("t1", 25m));
        Assert.Equal("t1:25:r7", TestScoreMessages.EventKey("t1", 25m, 7));
    }

    [Fact]
    public async Task Yuborish_tokenlar_bilan_ota_ona_chatiga_telegramsiz_va_arxiv_sanaladi()
    {
        using var f = Setup();
        var (res, err) = await Send(f);

        Assert.Null(err);
        Assert.Equal(2, res!.Queued);        // Ali's father, Bek's mother (Bek's own chat is not a parent chat)
        Assert.Equal(1, res.NoTelegram);     // Vali — only a blocked chat
        Assert.Equal(0, res.SkippedUnchanged);

        var rows = f.Db.Context.MessageDeliveries.AsNoTracking().OrderBy(d => d.ChatId).ToList();
        Assert.Equal(new long[] { 11, 21 }, rows.Select(r => r.ChatId));
        Assert.All(rows, r => Assert.Equal(f.Test.Id, r.SourceId));
        // Ali 30/30 → 1st of 3 scored non-archived… the place in the TEST counts every scored row (4).
        Assert.Contains("Unit 5|Oraliq|", rows[0].Text);
        Assert.Contains("|30/30|100%|1/4|1/3|1/3|Ingliz A1", rows[0].Text);
        Assert.Contains("|25/30|83%|2/4|2/3|2/3|Ingliz A1", rows[1].Text);
        Assert.EndsWith($":{f.Test.Id}:30", rows[0].DedupeKey);

        var test = f.Db.Context.TestResults.AsNoTracking().Single();
        Assert.NotNull(test.ResultsSentAt);
        Assert.Equal("u-admin", test.ResultsSentById);
        var log = Assert.Single(f.Db.Context.AuditLogs.AsNoTracking());
        Assert.Equal("Test natijalari ota-onalarga yuborildi: Unit 5, 2 ta", log.Summary);
        Assert.Equal($"{test.GroupId}:{test.Id}", log.EntityId);
    }

    [Fact]
    public async Task Qayta_bosilsa_faqat_ozgargan_natija_ketadi_resend_hammasiga()
    {
        using var f = Setup();
        await Send(f);
        var bells = f.Db.Context.UserNotifications.Count();

        var (again, _) = await Send(f);
        Assert.Equal((0, 2), (again!.Queued, again.SkippedUnchanged));
        Assert.Equal(2, f.Db.Context.MessageDeliveries.Count());
        Assert.Equal(bells, f.Db.Context.UserNotifications.Count());   // no repeated cabinet bell

        var bek = f.Db.Context.TestScores.Single(s => s.StudentId == f.Bek.Id);
        bek.Score = 26;
        f.Db.Context.SaveChanges();
        var (changed, _) = await Send(f);
        Assert.Equal((1, 1), (changed!.Queued, changed.SkippedUnchanged));
        Assert.Contains("26/30", f.Db.Context.MessageDeliveries.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DedupeKey).First(d => d.ChatId == 21).Text);

        var (resent, _) = await Send(f, resend: true);
        Assert.Equal((2, 0), (resent!.Queued, resent.SkippedUnchanged));
        Assert.Equal(5, f.Db.Context.MessageDeliveries.Count());
    }

    [Fact]
    public async Task Qoida_yoqilmagan_bolsa_xato_va_hech_narsa_navbatga_tushmaydi()
    {
        using var f = Setup(rule: false);
        var (res, err) = await Send(f);
        Assert.Null(res);
        Assert.Equal(TestScoreMessages.RuleMissing, err);
        Assert.Empty(f.Db.Context.MessageDeliveries);
        Assert.Null(f.Db.Context.TestResults.AsNoTracking().Single().ResultsSentAt);
    }
}
