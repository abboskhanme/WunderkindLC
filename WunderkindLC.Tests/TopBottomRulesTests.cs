using Regex = System.Text.RegularExpressions.Regex;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Rating;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM U4d — monthly TOP/BOTTOM (docs/SPEC-crm-upgrade.md P4.5/P4.7): k = min(N, ⌊n/2⌋), no overlap, ties, centre
/// scope; the notifier sends once on the rule day for the previous month and never repeats after a restart.
/// </summary>
public class TopBottomRulesTests
{
    private static TopBottomInput In(string id, double t, string group = "g1", string? name = null) =>
        new(id, name ?? id, group, group == "g1" ? "A1" : "B2", t);

    // ===================== pure rules =====================

    [Fact]
    public void K_min_N_va_yarim_n()
    {
        Assert.Equal(2, TopBottomRules.K(5, 5));
        Assert.Equal(5, TopBottomRules.K(5, 12));
        Assert.Equal(1, TopBottomRules.K(5, 2));
        Assert.Equal(1, TopBottomRules.K(1, 30));
        Assert.Equal(5, TopBottomRules.NormalizeCount(0));
        Assert.Equal(10, TopBottomRules.NormalizeCount(10));
        Assert.Equal("group", TopBottomRules.NormalizeScope(null));
        Assert.Equal("centre", TopBottomRules.NormalizeScope("Centre"));
    }

    [Fact]
    public void Guruh_boyicha_TOP_birinchi_k_BOTTOM_oxirgi_k_kesishmaydi_bitta_oquvchili_guruh_yoq()
    {
        var lists = TopBottomRules.Pick(
        [
            In("a", 95), In("b", 90), In("c", 70), In("d", 60), In("e", 40),
            In("x", 80, "g2"),   // a group with one scored student — no lists
        ], 5, "group");

        var g = Assert.Single(lists);
        Assert.Equal(5, g.Scored);
        Assert.Equal(new[] { "a", "b" }, g.Top.Select(e => e.StudentId));
        Assert.Equal(new[] { "e", "d" }, g.Bottom.Select(e => e.StudentId));   // lowest first
        Assert.Equal(new[] { 5, 4 }, g.Bottom.Select(e => e.Rank));
        Assert.Empty(g.Top.Select(e => e.StudentId).Intersect(g.Bottom.Select(e => e.StudentId)));
    }

    [Fact]
    public void Teng_natija_TOP_da_ism_boyicha_TOP_bilan_teng_BOTTOM_ga_tushmaydi()
    {
        var lists = TopBottomRules.Pick([In("d", 70, name: "Dilshod"), In("a", 80, name: "Bek"), In("b", 80, name: "Ali"), In("c", 90, name: "Zafar")], 5, "group");
        var g = Assert.Single(lists);
        // k = 2: Zafar (90) and Ali (80 — ties by name before Bek).
        Assert.Equal(new[] { "c", "b" }, g.Top.Select(e => e.StudentId));
        Assert.Equal(new[] { 1, 2 }, g.Top.Select(e => e.Rank));
        // Bek has the same 80 as Ali (praised) — never warned; only Dilshod.
        Assert.Equal(new[] { "d" }, g.Bottom.Select(e => e.StudentId));

        var same = Assert.Single(TopBottomRules.Pick([In("a", 100), In("b", 100), In("c", 100), In("d", 100)], 5, "group"));
        Assert.Equal(2, same.Top.Count);
        Assert.Empty(same.Bottom);
    }

    [Fact]
    public void Markaz_qamrovi_oquvchining_guruh_T_lari_ortachasi_bitta_royxat()
    {
        var lists = TopBottomRules.Pick(
        [
            In("a", 100), In("a", 60, "g2"),    // centre T = 80
            In("b", 90),
            In("c", 50), In("d", 30, "g2"),
        ], 5, "centre");
        var c = Assert.Single(lists);
        Assert.Equal("", c.GroupId);
        Assert.Equal(TopBottomRules.CentreName, c.GroupName);
        Assert.Equal(4, c.Scored);
        Assert.Equal(new[] { "b", "a" }, c.Top.Select(e => e.StudentId));
        Assert.Equal(80, c.Top[1].TestAvg);
        Assert.Equal(new[] { "d", "c" }, c.Bottom.Select(e => e.StudentId));
    }

    [Fact]
    public void Ikki_guruh_royxatidagi_oquvchiga_bitta_xabar()
    {
        var lists = TopBottomRules.Pick([In("a", 95), In("b", 50), In("a", 70, "g2"), In("c", 90, "g2")], 5, "group");
        var top = MonthlyRatingNotifier.Targets(lists, top: true);
        Assert.Equal(new[] { "a", "c" }, top.Select(t => t.Entry.StudentId).OrderBy(x => x));
        Assert.Equal("A1", top.Single(t => t.Entry.StudentId == "a").List.GroupName);   // the better place (1st in A1)
        var bottom = MonthlyRatingNotifier.Targets(lists, top: false);
        Assert.Equal(new[] { "a", "b" }, bottom.Select(t => t.Entry.StudentId).OrderBy(x => x));
    }

    // ===================== notifier =====================

    private static AutoMessageRule Rule(string trigger) => new()
    {
        Trigger = trigger, Name = trigger, Enabled = true, SendTelegram = true, Audience = "parents",
        Template = "{fish}|{oy}|{orin}|{foiz}|{guruh}", ScheduleType = "monthly", ScheduleDayOfMonth = 3, ScheduleTime = "10:00",
    };

    private static (TestDb Db, MonthlyRatingNotifier Notifier) Setup()
    {
        var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "Ingliz A1" };
        ctx.Classes.Add(g);
        var t = new TestResult
        {
            GroupId = g.Id, Name = "Sentabr testi", Date = "2026-09-15", Kind = "monthly",
            QuestionCount = 10, MaxScore = 10, CreatedAt = "2026-09-15T10:00:00",
        };
        ctx.TestResults.Add(t);
        var chat = 100L;
        foreach (var (name, score) in new[] { ("Ali", 10m), ("Bek", 8m), ("Vali", 6m), ("Zafar", 4m) })
        {
            var s = new Student { FullName = name, FirstName = name };
            ctx.Students.Add(s);
            ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active" });
            ctx.TestScores.Add(new TestScore { TestResultId = t.Id, StudentId = s.Id, Score = score });
            ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = ++chat, Relation = "father" });
        }
        ctx.AutoMessageRules.AddRange(Rule(AutoMessageTriggers.RatingTop), Rule(AutoMessageTriggers.RatingBottom));
        ctx.SaveChanges();
        return (db, New(db));
    }

    private static MonthlyRatingNotifier New(TestDb db) =>
        new(new SingleServiceProvider(db.Context), new MessagingStack().Auto, NullLogger<MonthlyRatingNotifier>.Instance);

    private static void Tick(MonthlyRatingNotifier n, DateTime at) =>
        Reflect.RunAsyncMethod(n, "TickAsync", at, CancellationToken.None);

    [Fact]
    public void Qoida_kuni_3_sana_10_00_da_bir_marta_otgan_oy_uchun_restartda_takrorlanmaydi()
    {
        var (db, notifier) = Setup();
        using var _ = db;
        var ctx = db.Context;

        Tick(notifier, new DateTime(2026, 10, 2, 12, 0, 0));
        Tick(notifier, new DateTime(2026, 10, 3, 9, 59, 0));
        Assert.Empty(ctx.MessageDeliveries);

        Tick(notifier, new DateTime(2026, 10, 3, 10, 5, 0));
        var rows = ctx.MessageDeliveries.AsNoTracking().ToList();
        Assert.Equal(4, rows.Count);   // k = min(5, ⌊4/2⌋) = 2 → 2 TOP + 2 BOTTOM
        Assert.Equal(2, rows.Count(r => r.Trigger == AutoMessageTriggers.RatingTop));
        Assert.Equal(2, rows.Count(r => r.Trigger == AutoMessageTriggers.RatingBottom));
        Assert.Contains(rows, r => r.Trigger == AutoMessageTriggers.RatingTop && r.Text.Contains("Ali|sentabr|1/4|100%|Ingliz A1"));
        Assert.Contains(rows, r => r.Trigger == AutoMessageTriggers.RatingBottom && r.Text.Contains("Zafar|sentabr|4/4|40%|Ingliz A1"));
        Assert.All(rows, r => Assert.EndsWith(":2026-09", r.DedupeKey));

        // Restart (a new instance) later the same day and the next days — nothing again.
        Tick(New(db), new DateTime(2026, 10, 3, 15, 0, 0));
        Tick(New(db), new DateTime(2026, 10, 4, 10, 30, 0));
        Assert.Equal(4, ctx.MessageDeliveries.Count());
        var topKey = MonthlyRatingNotifier.RuleKey(
            ctx.AutoMessageRules.First(x => x.Trigger == AutoMessageTriggers.RatingTop).Id, "2026-09");
        Assert.True(ctx.BackgroundJobRuns.Any(r => r.Key == topKey && r.LastRunDate == "2026-09-01"));
    }

    [Fact]
    public void Kun_otib_ketgan_bolsa_yuborilmaydi_catch_up_yoq()
    {
        var (db, notifier) = Setup();
        using var _ = db;
        Tick(notifier, new DateTime(2026, 10, 5, 11, 0, 0));
        Assert.Empty(db.Context.MessageDeliveries);
        Assert.True(db.Context.BackgroundJobRuns.Any(r => r.Key.StartsWith(MonthlyRatingNotifier.SkipPrefix + ":")));
    }

    [Fact]
    public async Task Korinish_endpointi_xizmati_otgan_oy_va_sozlamadagi_qamrov()
    {
        var (db, _) = Setup();
        using var __ = db;
        var (dto, _, err) = await TopBottomService.BuildAsync(db.Context, RatingEngine.Direct(db.Context), null, null,
            new DateOnly(2026, 10, 12));
        Assert.Null(err);
        Assert.Equal("2026-09", dto!.Month);
        Assert.Equal("group", dto.Scope);
        Assert.Equal(5, dto.Count);
        var g = Assert.Single(dto.Groups);
        Assert.Equal(new[] { "Ali", "Bek" }, g.Top.Select(r => r.FullName));
        Assert.Equal(new[] { "Zafar", "Vali" }, g.Bottom.Select(r => r.FullName));

        var (none, _, future) = await TopBottomService.BuildAsync(db.Context, RatingEngine.Direct(db.Context), "2027-01", null,
            new DateOnly(2026, 10, 12));
        Assert.Null(none);
        Assert.Equal(RatingPeriods.InvalidMessage, future);
    }

    [Fact]
    public void Korinish_endpointi_reyting_kaliti_bilan_darvozalangan()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "RatingController.TopBottom.cs"));
        Assert.Contains("[HttpGet(\"top-bottom\")]", src);
        Assert.Contains("[AdminPerm(\"classes.rating\", ReadRequiresPerm = true)]", src);
        Assert.Equal(Regex.Matches(src, @"\[Http(Get|Post|Put|Patch|Delete)").Count, Regex.Matches(src, @"\[AdminPerm\(").Count);
        Assert.DoesNotContain("AllowAnonymous", src);
        // 403 for staff without the key (reads are gated).
        Assert.False(PermissionRules.HasSection(["classes.testResults"], "classes.rating"));
        Assert.True(PermissionRules.HasSection(["classes.rating"], "classes.rating"));
    }
}
