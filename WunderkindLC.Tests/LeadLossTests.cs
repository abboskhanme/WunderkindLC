using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «YO'QOTILGAN LIDLAR» — belgilash/tiklash qoidalari (<see cref="LeadLoss"/>), hisobot
/// (<see cref="LeadLossReport"/>), "ochiq lid" ta'rifidan chiqarilishi (bosh sahifa, birinchi dars,
/// sinov eslatmasi), Telegram kartasi va RUXSAT darvozasi. Rasmiy manba:
/// <c>.claude/rules/crm-leads.md</c> «YO'QOTILGAN LIDLAR».
/// </summary>
public class LeadLossTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static readonly LeadLoss.Actor Manager = new("u-1", "Menejer Ali");

    private static AuditService Audit(AppDbContext ctx) => new(ctx, new NoHttpContext());

    private static (Lead lead, ActionReason reason) Seed(AppDbContext ctx, string? assignee = null)
    {
        var stage = new LeadStage { Id = "st-1", Title = "O'ylanmoqda", Order = 0 };
        var other = new LeadStage { Id = "st-2", Title = "Sinov darsi", Order = 1 };
        var lead = new Lead
        {
            FullName = "Vali Valiyev", Phone = "901234567", Stage = stage.Id, Source = "Instagram",
            CreatedAt = AppClock.Iso(), AssigneeUserId = assignee,
        };
        var reason = new ActionReason { Category = LeadLoss.ReasonCategory, Label = "Narx qimmatlik qildi" };
        ctx.LeadStages.AddRange(stage, other);
        ctx.Leads.Add(lead);
        ctx.ActionReasons.Add(reason);
        ctx.SaveChanges();
        return (lead, reason);
    }

    // ============================== 1) Qoidalar (sof) ==============================

    [Fact]
    public void ValidateMark_sabab_MAJBURIY_va_faqat_lead_lost_kategoriyasidan()
    {
        var lead = new Lead();
        var ok = new ActionReason { Category = LeadLoss.ReasonCategory, Label = "Boshqa" };
        Assert.Null(LeadLoss.ValidateMark(lead, ok, null));
        Assert.NotNull(LeadLoss.ValidateMark(lead, null, null));
        Assert.NotNull(LeadLoss.ValidateMark(lead, new ActionReason { Category = "lead_delete", Label = "Spam" }, null));
    }

    [Fact]
    public void ValidateMark_aylantirilgan_va_allaqachon_yoqotilgan_lid_rad_etiladi()
    {
        var ok = new ActionReason { Category = LeadLoss.ReasonCategory, Label = "Boshqa" };
        Assert.NotNull(LeadLoss.ValidateMark(new Lead { ConvertedStudentId = "s-1" }, ok, null));
        Assert.NotNull(LeadLoss.ValidateMark(new Lead { LostAt = "2026-10-01T10:00:00" }, ok, null));
    }

    [Fact]
    public void ValidateMark_juda_uzun_izoh_JIMGINA_qirqilmaydi_rad_etiladi()
    {
        var ok = new ActionReason { Category = LeadLoss.ReasonCategory, Label = "Boshqa" };
        Assert.Null(LeadLoss.ValidateMark(new Lead(), ok, new string('a', LeadLoss.MaxCommentLength)));
        Assert.NotNull(LeadLoss.ValidateMark(new Lead(), ok, new string('a', LeadLoss.MaxCommentLength + 1)));
    }

    [Fact]
    public void Standart_sabablar_yigilishdagi_misollarni_qamraydi()
    {
        // Yig'ilish talabi: "vaqt to'g'ri kelmadi, narx qimmatlik qildi, boshqa kursga ketdi".
        Assert.Contains("Vaqt to'g'ri kelmadi", LeadLoss.DefaultReasons);
        Assert.Contains("Narx qimmatlik qildi", LeadLoss.DefaultReasons);
        Assert.Contains(LeadLoss.DefaultReasons, r => r.StartsWith("Boshqa kurs", StringComparison.Ordinal));
        Assert.Equal(LeadLoss.DefaultReasons.Count, LeadLoss.DefaultReasons.Distinct().Count());
    }

    // ============================== 2) Belgilash ==============================

    [Fact]
    public async Task MarkLost_sabab_SNAPSHOT_izoh_kim_qachon_hodisa_va_audit_yoziladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);

        var res = await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, "  Keyingi yil  ", Manager);

        Assert.Equal(200, res.Status);
        var saved = ctx.Leads.Single();
        Assert.True(LeadLoss.IsLost(saved));
        Assert.Equal(reason.Id, saved.LostReasonId);
        Assert.Equal("Narx qimmatlik qildi", saved.LostReason);
        Assert.Equal("Keyingi yil", saved.LostComment);
        Assert.Equal("u-1", saved.LostByUserId);
        Assert.Equal("Menejer Ali", saved.LostByName);
        // Bosqich TEGILMAYDI — qaysi bosqichda yo'qotilgani ko'rinadi, tiklanganda shu yerga qaytadi.
        Assert.Equal("st-1", saved.Stage);

        var ev = Assert.Single(ctx.LeadEvents);
        Assert.Equal(LeadLoss.EventLost, ev.Type);
        Assert.Equal("st-1", ev.FromStage);
        Assert.Contains("Narx qimmatlik qildi", ev.Text);
        Assert.Equal("u-1", ev.ActorUserId);

        var log = Assert.Single(ctx.AuditLogs);
        Assert.Equal("Lead", log.EntityType);
        Assert.Equal("leads", AuditSections.SectionOf(log.EntityType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yoq-id")]
    public async Task MarkLost_sababsiz_yoki_notogri_sabab_bilan_HECH_NARSA_ozgarmaydi(string? reasonId)
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, _) = Seed(ctx);

        var res = await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reasonId, null, Manager);

        Assert.Equal(400, res.Status);
        Assert.False(string.IsNullOrWhiteSpace(res.Error));
        Assert.False(LeadLoss.IsLost(ctx.Leads.Single()));
        Assert.Empty(ctx.LeadEvents);
        Assert.Empty(ctx.AuditLogs);
    }

    [Fact]
    public async Task MarkLost_boshqa_kategoriyadagi_sabab_rad_etiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, _) = Seed(ctx);
        var foreign = new ActionReason { Category = "lead_delete", Label = "Takror / spam" };
        ctx.ActionReasons.Add(foreign);
        ctx.SaveChanges();

        var res = await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, foreign.Id, null, Manager);

        Assert.Equal(400, res.Status);
        Assert.False(LeadLoss.IsLost(ctx.Leads.Single()));
    }

    [Fact]
    public async Task MarkLost_aylantirilgan_lidga_ruxsat_yoq_va_topilmagan_lid_404()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);
        lead.ConvertedStudentId = "s-1";
        ctx.SaveChanges();

        Assert.Equal(400, (await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager)).Status);
        Assert.Equal(404, (await LeadLoss.MarkLostAsync(ctx, Audit(ctx), "yoq", reason.Id, null, Manager)).Status);
    }

    [Fact]
    public async Task MarkLost_ikki_marta_belgilanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);

        Assert.Equal(200, (await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager)).Status);
        Assert.Equal(400, (await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager)).Status);
        Assert.Single(ctx.LeadEvents);
    }

    [Fact]
    public async Task MarkLost_masul_bosh_bolsa_belgilagan_xodim_biriktiriladi_bori_USTIDAN_yozilmaydi()
    {
        using (var db = TestDb.Sqlite())
        {
            var ctx = db.Context;
            var (lead, reason) = Seed(ctx);
            await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);
            Assert.Equal("u-1", ctx.Leads.Single().AssigneeUserId);
        }
        using (var db = TestDb.Sqlite())
        {
            var ctx = db.Context;
            var (lead, reason) = Seed(ctx, assignee: "u-boshqa");
            await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);
            Assert.Equal("u-boshqa", ctx.Leads.Single().AssigneeUserId);
        }
    }

    [Fact]
    public async Task Sabab_katalogda_qayta_nomlansa_lidning_sababi_OZGARMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);
        await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);

        ctx.ActionReasons.Single(r => r.Id == reason.Id).Label = "Qimmat";
        ctx.SaveChanges();

        Assert.Equal("Narx qimmatlik qildi", ctx.Leads.Single().LostReason);
    }

    // ============================== 3) Tiklash ==============================

    [Fact]
    public async Task Restore_maydonlar_tozalanadi_bosqich_joyida_qoladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);
        await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, "izoh", Manager);

        var res = await LeadLoss.RestoreAsync(ctx, Audit(ctx), lead.Id, null, Manager);

        Assert.Equal(200, res.Status);
        var saved = ctx.Leads.Single();
        Assert.False(LeadLoss.IsLost(saved));
        Assert.Null(saved.LostReason);
        Assert.Null(saved.LostReasonId);
        Assert.Null(saved.LostComment);
        Assert.Null(saved.LostByName);
        Assert.Equal("st-1", saved.Stage);
        Assert.Contains(ctx.LeadEvents, e => e.Type == LeadLoss.EventRestore);
        Assert.DoesNotContain(ctx.LeadEvents, e => e.Type == LeadAnalytics.TypeStage);
        Assert.Equal(2, ctx.AuditLogs.Count());
    }

    [Fact]
    public async Task Restore_boshqa_bosqichga_STAGE_hodisasi_bilan_voronka_buzilmasin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);
        await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);

        var res = await LeadLoss.RestoreAsync(ctx, Audit(ctx), lead.Id, "st-2", Manager);

        Assert.Equal(200, res.Status);
        Assert.Equal("st-2", ctx.Leads.Single().Stage);
        var stageEv = Assert.Single(ctx.LeadEvents, e => e.Type == LeadAnalytics.TypeStage);
        Assert.Equal("st-1", stageEv.FromStage);
        Assert.Equal("st-2", stageEv.ToStage);
    }

    [Fact]
    public async Task Restore_notogri_bosqich_yoki_yoqotilmagan_lid_400()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (lead, reason) = Seed(ctx);

        Assert.Equal(400, (await LeadLoss.RestoreAsync(ctx, Audit(ctx), lead.Id, null, Manager)).Status);

        await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);
        Assert.Equal(400, (await LeadLoss.RestoreAsync(ctx, Audit(ctx), lead.Id, "yoq-bosqich", Manager)).Status);
        Assert.True(LeadLoss.IsLost(ctx.Leads.Single()));
        Assert.Equal(404, (await LeadLoss.RestoreAsync(ctx, Audit(ctx), "yoq", null, Manager)).Status);
    }

    // ============================== 4) "Ochiq lid" ta'rifi ==============================

    [Fact]
    public void Bosh_sahifa_buyurtmalar_va_birinchi_dars_YOQOTILGANNI_sanamaydi()
    {
        var today = "2026-10-06";
        var leads = new[]
        {
            new DashboardSummary.LeadRow("a", false),
            new DashboardSummary.LeadRow("b", false, Lost: true),
            new DashboardSummary.LeadRow("c", true),
        };
        var trials = new[]
        {
            new DashboardSummary.TrialRow("a", "pending", "2026-10-07T15:00"),
            new DashboardSummary.TrialRow("b", "pending", "2026-10-07T15:00"),
        };

        Assert.Equal(1, DashboardSummary.OpenLeads(leads));
        Assert.Equal(1, DashboardSummary.FirstLessonLeads(leads, trials, today));

        // «Birinchi darsga yozilganlar» ro'yxati AYNAN shu ta'rif bilan.
        var picked = LeadFirstLesson.Select(leads, new[]
        {
            new LeadFirstLesson.TrialRow("t1", "a", "g", "pending", "2026-10-07T15:00"),
            new LeadFirstLesson.TrialRow("t2", "b", "g", "pending", "2026-10-07T15:00"),
        }, today);
        Assert.Equal(new[] { "a" }, picked.Keys.ToArray());
    }

    private static TrialReminderService Reminder(IAppDbContext ctx, MessagingStack stack) =>
        new(new SingleServiceProvider(ctx), stack.Auto, NullLogger<TrialReminderService>.Instance);

    [Fact]
    public void Sinov_eslatmasi_YOQOTILGAN_lidga_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var today = AppClock.Today;
        ctx.CenterMeta.Add(new CenterMeta { Name = "Wunderkind", LocalSmsEnabled = true });
        ctx.AutoMessageRules.Add(new AutoMessageRule
        {
            Trigger = AutoMessageTriggers.TrialReminder, Enabled = true, SendSms = true,
            SmsProvider = "local", Template = "Salom {fish}! Ertaga sinov darsingiz bor.",
        });
        var active = new Lead { FullName = "Faol Lid", Phone = "901111111", CreatedAt = AppClock.Iso() };
        var lost = new Lead
        {
            FullName = "Yoqotilgan Lid", Phone = "902222222", CreatedAt = AppClock.Iso(),
            LostAt = AppClock.Iso(), LostReason = "Narx qimmatlik qildi",
        };
        ctx.Leads.AddRange(active, lost);
        foreach (var l in new[] { active, lost })
            ctx.TrialLessons.Add(new TrialLesson
            {
                LeadId = l.Id, ScheduledAt = $"{today.AddDays(1):yyyy-MM-dd}T15:30", Result = "pending",
            });
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Reminder(ctx, new MessagingStack()), "RunAsync", today, CancellationToken.None);

        var log = Assert.Single(ctx.SmsLogs);
        Assert.Contains("Faol Lid", log.Message);
    }

    // ============================== 5) Telegram kartasi ==============================

    private static readonly Type CardPartsType =
        typeof(LeadNotifier).GetNestedType("CardParts", BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(LeadNotifier), "CardParts");

    private static string CardText(Lead lead, bool includeNotes)
    {
        var parts = Activator.CreateInstance(
            CardPartsType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
            args: new object?[] { lead, null, null, null, "O'ylanmoqda", null, null, new List<LeadEvent>(), 0 },
            culture: null)!;
        var card = Reflect.StaticCall<object>(typeof(LeadNotifier), "Render", parts, includeNotes);
        return (string)card.GetType().GetProperty("Text")!.GetValue(card)!;
    }

    [Fact]
    public void Kartada_yoqotilgan_qatori_IZOH_esa_faqat_shaxsiy_chatda()
    {
        var lead = new Lead
        {
            FullName = "Vali", Phone = "901234567", CreatedAt = AppClock.Iso(),
            LostAt = AppClock.Iso(), LostReason = "Vaqt to'g'ri kelmadi", LostComment = "Kechki guruh yo'q ekan",
        };

        var group = CardText(lead, includeNotes: false);
        var personal = CardText(lead, includeNotes: true);

        Assert.Contains("Yo'qotildi: Vaqt to'g'ri kelmadi", group);
        Assert.Contains("Yo'qotildi: Vaqt to'g'ri kelmadi", personal);
        Assert.DoesNotContain("Kechki guruh", group);
        Assert.Contains("Kechki guruh", personal);
    }

    [Fact]
    public void Yoqotilmagan_lid_kartasi_OZGARMAGAN()
    {
        var text = CardText(new Lead { FullName = "Vali", Phone = "901234567", CreatedAt = AppClock.Iso() }, true);
        Assert.DoesNotContain("Yo'qotildi", text);
    }

    // ============================== 6) Hisobot ==============================

    private static LeadLossReport.Row R(
        string id, string created, string? lostAt = null, string? reason = null, string source = "Instagram",
        string? assignee = "u-1", bool converted = false) =>
        new(id, $"Lid {id}", created, source, assignee, converted, lostAt, reason, null, "Ali");

    [Fact]
    public void Hisobot_sanoqlar_va_foiz_KELGAN_sana_boyicha()
    {
        var rows = new[]
        {
            R("1", "2026-09-05T10:00:00", "2026-09-20T10:00:00", "Narx qimmatlik qildi"),
            R("2", "2026-09-10T10:00:00"),
            R("3", "2026-09-11T10:00:00", converted: true),
            R("4", "2026-09-12T10:00:00", "2026-10-02T10:00:00", "Vaqt to'g'ri kelmadi"),
            // Davrdan tashqarida kelgan — sanoqqa KIRMAYDI (yo'qotilgan bo'lsa ham).
            R("5", "2026-08-01T10:00:00", "2026-09-15T10:00:00", "Narx qimmatlik qildi"),
        };

        var dto = LeadLossReport.Build(rows, "2026-09-01", "2026-09-30");

        Assert.Equal(4, dto.Total);
        Assert.Equal(2, dto.Lost);
        Assert.Equal(50.0, dto.LostRate);
        Assert.Equal(1, dto.Converted);
    }

    [Fact]
    public void Hisobot_sabablar_registr_va_bosliq_farqisiz_birlashadi_sababsiz_YOQOLMAYDI()
    {
        var rows = new[]
        {
            R("1", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "Narx qimmatlik qildi"),
            R("2", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "narx  qimmatlik qildi "),
            R("3", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "Narx qimmatlik qildi"),
            R("4", "2026-09-01T10:00:00", "2026-09-02T10:00:00", ""),
        };

        var dto = LeadLossReport.Build(rows, null, null);

        Assert.Equal(2, dto.ByReason.Count);
        Assert.Equal("Narx qimmatlik qildi", dto.ByReason[0].Label);
        Assert.Equal(3, dto.ByReason[0].Count);
        Assert.Equal(75.0, dto.ByReason[0].Share);
        Assert.Equal(LeadLossReport.NoReasonLabel, dto.ByReason[1].Label);
    }

    [Fact]
    public void Hisobot_manba_va_masul_kesimi_jami_va_yoqotilgan_bilan()
    {
        var rows = new[]
        {
            R("1", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "Narx", source: "src-1"),
            R("2", "2026-09-01T10:00:00", source: "src-1"),
            R("3", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "Narx", source: "", assignee: null),
            R("4", "2026-09-01T10:00:00", source: "Telegram", assignee: "u-ochirilgan"),
        };
        var users = new Dictionary<string, string> { ["u-1"] = "Ali Valiyev" };
        var sources = new Dictionary<string, string> { ["src-1"] = "Instagram" };

        var dto = LeadLossReport.Build(rows, null, null, users, sources);

        // Manba id'si NOMGA aylanadi, bo'sh manba — «Noma'lum».
        var insta = Assert.Single(dto.BySource, s => s.Label == "Instagram");
        Assert.Equal((2, 1, 50.0), (insta.Total, insta.Lost, insta.Rate));
        Assert.Contains(dto.BySource, s => s.Label == LeadAnalytics.UnknownSourceLabel && s.Lost == 1);

        var ali = Assert.Single(dto.ByManager, m => m.Label == "Ali Valiyev");
        Assert.Equal((2, 1), (ali.Total, ali.Lost));
        Assert.Contains(dto.ByManager, m => m.Label == LeadLossReport.UnassignedLabel && m.Key == "" && m.Lost == 1);
        Assert.Contains(dto.ByManager, m => m.Label == LeadLossReport.UnknownUserLabel);
        // Yo'qotilgani ko'p bo'lgan tepada.
        Assert.True(dto.ByManager[0].Lost >= dto.ByManager[^1].Lost);
    }

    [Fact]
    public void Hisobot_oylik_oqimda_bosh_oylar_NOL_bilan_va_davr_chegaralari_qamraladi()
    {
        var rows = new[]
        {
            R("1", "2026-07-03T10:00:00", "2026-07-10T10:00:00", "Narx"),
            R("2", "2026-07-04T10:00:00"),
            R("3", "2026-09-04T10:00:00"),
        };

        var dto = LeadLossReport.Build(rows, "2026-06-15", "2026-09-30");

        Assert.Equal(new[] { "2026-06", "2026-07", "2026-08", "2026-09" }, dto.Monthly.Select(m => m.Month).ToArray());
        var jul = dto.Monthly[1];
        Assert.Equal((2, 1, 50.0), (jul.Total, jul.Lost, jul.Rate));
        Assert.Equal(0, dto.Monthly[2].Total);
    }

    [Fact]
    public void Hisobot_tiklangan_lid_yoqotilgan_SANALMAYDI_royxat_eng_yangisi_tepada()
    {
        var rows = new[]
        {
            R("old", "2026-09-01T10:00:00", "2026-09-02T10:00:00", "Narx"),
            R("new", "2026-09-01T10:00:00", "2026-09-05T10:00:00", "Vaqt"),
            R("restored", "2026-09-01T10:00:00"),   // tiklangan — LostAt null
        };

        var dto = LeadLossReport.Build(rows, null, null);

        Assert.Equal(2, dto.Lost);
        Assert.Equal(new[] { "new", "old" }, dto.Recent.Select(r => r.Id).ToArray());
        Assert.Equal(0, dto.RecentTruncated);
    }

    [Fact]
    public void Hisobot_ro_yxat_chegarasi_qirqilgani_ochiq_aytiladi()
    {
        var rows = Enumerable.Range(0, LeadLossReport.RecentLimit + 7)
            .Select(i => R($"l{i}", "2026-09-01T10:00:00", $"2026-09-02T10:{i % 60:00}:00", "Narx"))
            .ToList();

        var dto = LeadLossReport.Build(rows, null, null);

        Assert.Equal(LeadLossReport.RecentLimit, dto.Recent.Count);
        Assert.Equal(7, dto.RecentTruncated);
    }

    [Fact]
    public async Task Hisobot_bazadan_yuklanadi_va_bosh_bazada_yiqilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Assert.Equal(0, (await LeadLossReport.BuildAsync(ctx, null, null)).Total);

        var (lead, reason) = Seed(ctx);
        await LeadLoss.MarkLostAsync(ctx, Audit(ctx), lead.Id, reason.Id, null, Manager);
        var dto = await LeadLossReport.BuildAsync(ctx, null, null);
        Assert.Equal((1, 1, 100.0), (dto.Total, dto.Lost, dto.LostRate));
        Assert.Equal("Narx qimmatlik qildi", Assert.Single(dto.ByReason).Label);
    }

    // ============================== 7) RUXSAT (RBAC) ==============================

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

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", file));

    /// <summary>Metod atributlari: <paramref name="route"/> dan metod imzosigacha bo'lgan matn.</summary>
    private static string AttrsOf(string src, string route)
    {
        var i = src.IndexOf(route, StringComparison.Ordinal);
        Assert.True(i > 0, $"{route} topilmadi");
        var start = src.LastIndexOf("</summary>", i, StringComparison.Ordinal);
        var sig = src.IndexOf("public async", i, StringComparison.Ordinal);
        return src[start..sig];
    }

    [Fact]
    public void Belgilash_va_tiklash_lidlar_royxati_ruxsati_ostida_EDIT_amali()
    {
        var src = Source("LeadLossController.cs");
        var header = src[..src.IndexOf("public class LeadLossController", StringComparison.Ordinal)];
        Assert.Contains("[Authorize]", header);
        Assert.Contains("[AdminPerm(\"leads.list\")]", header);
        Assert.DoesNotContain("AllowAnonymous", src);

        foreach (var route in new[] { "[HttpPatch(\"{id}/lost\")]", "[HttpPatch(\"{id}/restore\")]" })
        {
            var attrs = AttrsOf(src, route);
            // Metodda o'z [AdminPerm] i bo'lsa u sinfdagisini BEKOR qilardi (permissions.md §4).
            Assert.DoesNotContain("AdminPerm", attrs);
        }

        // PATCH = "edit": faqat ko'rish yoki faqat yaratish ruxsati bor xodim lidni yo'qota olmaydi.
        Assert.True(PermissionRules.CanWrite(new[] { "leads.list:edit" }, "leads.list", "PATCH"));
        Assert.True(PermissionRules.CanWrite(new[] { "leads" }, "leads.list", "PATCH"));
        Assert.False(PermissionRules.CanWrite(new[] { "leads.list:create" }, "leads.list", "PATCH"));
        Assert.False(PermissionRules.CanWrite(new[] { "leads.stats" }, "leads.list", "PATCH"));
    }

    [Fact]
    public void Hisobot_CRM_statistikasi_ruxsati_bilan_OQISH_ham_darvozalangan()
    {
        var src = Source("LeadLossController.cs");
        var attrs = AttrsOf(src, "[HttpGet(\"lost-report\")]");
        Assert.Matches(new Regex(@"\[AdminPerm\(""leads\.stats"",\s*ReadRequiresPerm\s*=\s*true\)\]"), attrs);

        // Faqat lidlar ro'yxati berilgan operator hisobotni KO'RMAYDI; bo'lim yoki sahifa egasi ko'radi.
        Assert.False(PermissionRules.HasSection(new[] { "leads.list" }, "leads.stats"));
        Assert.True(PermissionRules.HasSection(new[] { "leads.stats" }, "leads.stats"));
        Assert.True(PermissionRules.HasSection(new[] { "leads" }, "leads.stats"));
    }

    [Fact]
    public void Yoqotilgan_lidni_SURISH_va_AYLANTIRISH_serverda_toxtatiladi()
    {
        var src = Source("LeadsController.cs");

        var patch = src[src.IndexOf("[HttpPatch(\"{id}\")]", StringComparison.Ordinal)..];
        patch = patch[..patch.IndexOf("SaveChangesAsync", StringComparison.Ordinal)];
        Assert.Contains("LeadLoss.IsLost(lead)", patch);

        var convert = src[src.IndexOf("[HttpPost(\"{id}/convert\")]", StringComparison.Ordinal)..];
        // O'quvchi endi `LeadConversion` da yaratiladi (audit C5) — tekshiruv undan OLDIN turishi shart.
        convert = convert[..convert.IndexOf("LeadConversion.", StringComparison.Ordinal)];
        Assert.Contains("LeadLoss.IsLost(lead)", convert);

        // «Birinchi darsga yozilganlar» — ochiq lid ta'rifi yo'qotilganni chiqarib tashlaydi.
        var first = src[src.IndexOf("[HttpGet(\"first-lesson\")]", StringComparison.Ordinal)..];
        Assert.Contains("l.LostAt == null", first[..first.IndexOf("ToListAsync", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Sabab_kategoriyasi_serverda_ruxsat_etilgan()
    {
        var src = Source("ActionReasonsController.cs");
        Assert.Contains($"\"{LeadLoss.ReasonCategory}\"", src);
    }
}
