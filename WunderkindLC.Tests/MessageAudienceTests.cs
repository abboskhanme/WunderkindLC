using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM upgrade P2.3/P2.7 — audiences (<see cref="MessageAudienceResolver"/>) and campaigns (<see cref="CampaignService"/>):
/// active vs archive strictly separate, «Barcha ota-onalar», blocked chats, per-chat dedupe, count == rows created.
/// </summary>
public class MessageAudienceTests
{
    private sealed class World : IDisposable
    {
        public TestDb Db { get; } = TestDb.Sqlite();
        public AppDbContext Ctx => Db.Context;
        public Group Live { get; } = new() { Name = "Ingliz A" };
        public Group Other { get; } = new() { Name = "Matematika B" };
        public Group Closed { get; } = new() { Name = "Yopiq", IsArchived = true };
        private long _chat = 100;

        public World()
        {
            Ctx.Classes.AddRange(Live, Other, Closed);
            Ctx.SaveChanges();
        }

        public Student Student(string name, Group? group = null, string status = "active", bool archived = false,
            decimal balance = 0, params string[] relations)
        {
            var s = new Student { FullName = name, FirstName = name.Split(' ').Last(), IsArchived = archived, Balance = balance,
                ArchivedAt = archived ? "2026-09-15" : null, ArchiveReason = archived ? "Boshqa markazga o'tdi" : null };
            Ctx.Students.Add(s);
            if (group is not null)
                Ctx.StudentGroups.Add(new StudentGroup { StudentId = s.Id, GroupId = group.Id, IsActive = true, Status = status });
            foreach (var rel in relations)
                Ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = ++_chat, Relation = rel });
            Ctx.SaveChanges();
            return s;
        }

        public void Link(Student s, long chat, string relation, bool blocked = false)
        {
            Ctx.TelegramRegistrations.Add(new TelegramRegistration
            {
                StudentId = s.Id, ChatId = chat, Relation = relation, BlockedAt = blocked ? AppClock.Now : null,
            });
            Ctx.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    private static AudienceSpec Active(params string[] relations) =>
        new() { Kind = "active", Relations = relations.Length == 0 ? null : relations.ToList() };

    [Fact]
    public async Task Barcha_ota_onalar_arxiv_yopiq_guruh_bloklagan_va_oquvchi_chatisiz()
    {
        using var w = new World();
        var ok = w.Student("Aliyev Ali", w.Live, relations: new[] { "father", "student" });
        var trial = w.Student("Bekov Bek", w.Live, status: "trial", relations: new[] { "mother" });
        var frozen = w.Student("Frozen Fozil", w.Live, status: "frozen", relations: new[] { "mother" });
        w.Student("Arxiv Anvar", w.Live, archived: true, relations: new[] { "father" });
        w.Student("Yopiq Yusuf", w.Closed, relations: new[] { "father" });
        var blocked = w.Student("Blok Botir", w.Live);
        w.Link(blocked, 9001, "father", blocked: true);

        var r = await MessageAudienceResolver.ResolveAsync(w.Ctx, Active());

        Assert.Equal(new[] { ok.Id, trial.Id, blocked.Id }.OrderBy(x => x), r.Students.Select(s => s.Id).OrderBy(x => x));
        Assert.DoesNotContain(r.Students, s => s.Id == frozen.Id);           // frozen is opt-in
        Assert.All(r.Targets, t => Assert.NotEqual("student", t.Reg.Relation));
        Assert.Equal(2, r.Targets.Count);                                     // ok/father + trial/mother
        Assert.Equal(1, r.Blocked);
        Assert.Equal(1, r.NotConnected);                                       // blocked student has no usable chat
    }

    [Fact]
    public async Task Holat_va_qarindosh_filtrlari()
    {
        using var w = new World();
        var a = w.Student("Aliyev Ali", w.Live, relations: new[] { "father", "student" });
        var f = w.Student("Frozen Fozil", w.Other, status: "frozen", relations: new[] { "mother" });

        var students = await MessageAudienceResolver.ResolveAsync(w.Ctx, new AudienceSpec
        {
            Kind = "active", MemberStatuses = new() { "frozen" }, Relations = new() { "student", "mother" },
        });
        Assert.Equal(f.Id, Assert.Single(students.Students).Id);
        Assert.Equal("mother", Assert.Single(students.Targets).Reg.Relation);

        var group = await MessageAudienceResolver.ResolveAsync(w.Ctx, new AudienceSpec
        {
            Kind = "active", GroupIds = new() { w.Live.Id }, Relations = new() { "student" },
        });
        Assert.Equal(a.Id, Assert.Single(group.Students).Id);
        Assert.Equal("student", Assert.Single(group.Targets).Reg.Relation);
    }

    [Fact]
    public async Task Arxiv_faqat_arxivdagilar_sana_va_sabab_bilan()
    {
        using var w = new World();
        w.Student("Faol Ali", w.Live, relations: new[] { "father" });
        var arch = w.Student("Arxiv Anvar", w.Live, archived: true, relations: new[] { "father" });

        var r = await MessageAudienceResolver.ResolveAsync(w.Ctx, new AudienceSpec
        {
            Kind = "archive", ArchivedFrom = "2026-09-01", ArchivedTo = "2026-09-30", ArchiveReason = "boshqa markaz",
        });
        Assert.Equal(arch.Id, Assert.Single(r.Students).Id);

        var none = await MessageAudienceResolver.ResolveAsync(w.Ctx, new AudienceSpec { Kind = "archive", ArchivedFrom = "2026-10-01" });
        Assert.Empty(none.Students);
    }

    [Fact]
    public async Task Tanlangan_aynan_shular()
    {
        using var w = new World();
        var a = w.Student("Aliyev Ali", w.Live, relations: new[] { "father" });
        w.Student("Boshqa Bek", w.Live, relations: new[] { "father" });
        var r = await MessageAudienceResolver.ResolveAsync(w.Ctx, new AudienceSpec { Kind = "selected", StudentIds = new() { a.Id } });
        Assert.Equal(a.Id, Assert.Single(r.Students).Id);
        Assert.NotNull(MessageAudienceResolver.Validate(new AudienceSpec { Kind = "selected", StudentIds = new() }));
        Assert.NotNull(MessageAudienceResolver.Validate(new AudienceSpec { Kind = "active", Relations = new() { "cousin" } }));
        Assert.NotNull(MessageAudienceResolver.Validate(new AudienceSpec { Kind = "nobody" }));
    }

    [Fact]
    public async Task Talaba_tokenisiz_shablon_BITTA_chatga_BITTA_xabar()
    {
        using var w = new World();
        var a = w.Student("Aliyev Ali", w.Live);
        var b = w.Student("Aliyev Bobur", w.Live);
        w.Link(a, 555, "father");
        w.Link(b, 555, "father");   // siblings share the father's chat

        var (perChat, _) = await CampaignService.PlanAsync(w.Ctx, "broadcast", Active(), "Ertaga dars yo'q. {markaz}",
            null, false, render: true);
        var item = Assert.Single(perChat!.Items);
        Assert.Equal(a.Id, item.StudentId);   // first student by name
        Assert.True(perChat.PerChat);

        var (perStudent, _) = await CampaignService.PlanAsync(w.Ctx, "broadcast", Active(), "{ism}, ertaga dars yo'q",
            null, false, render: true);
        Assert.Equal(2, perStudent!.Items.Count);
        Assert.False(perStudent.PerChat);
        Assert.Contains(perStudent.Items, i => i.Text.Contains("Bobur"));
    }

    [Fact]
    public async Task Sanoq_yaratilgan_qatorlar_soniga_TENG()
    {
        using var w = new World();
        w.Student("Aliyev Ali", w.Live, relations: new[] { "father", "mother" });
        w.Student("Bekov Bek", w.Other, status: "trial", relations: new[] { "parent" });
        var blocked = w.Student("Blok Botir", w.Live, relations: new[] { "father" });
        w.Link(blocked, 9999, "mother", blocked: true);
        var spec = Active();

        var (plan, error) = await CampaignService.PlanAsync(w.Ctx, "broadcast", spec, "{ism}!", null, false, render: false);
        Assert.Null(error);
        var count = plan!.ToCount();

        var (created, createError) = await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", spec, "{ism}!", null, true), "u1", "Admin", botConfigured: true);
        Assert.Null(createError);
        Assert.Equal(count.Messages, created!.Messages);
        Assert.Equal(count.Messages, w.Ctx.MessageDeliveries.Count());
        Assert.Equal(4, count.Messages);
        Assert.Equal(1, count.Blocked);
        Assert.All(w.Ctx.MessageDeliveries.ToList(), d =>
        {
            Assert.True(d.AckRequired);
            Assert.StartsWith(MessageRender.HeaderCampaign, d.Text);
            Assert.Equal("campaign", d.Source);
        });
        var bc = w.Ctx.Broadcasts.Single();
        Assert.Equal("queued", bc.Status);
        Assert.Equal(4, bc.RecipientCount);
        Assert.NotEmpty(bc.AudienceJson);
    }

    [Fact]
    public async Task Arxiv_va_faol_aralashmaydi()
    {
        using var w = new World();
        var live = w.Student("Faol Ali", w.Live, relations: new[] { "father" });
        var arch = w.Student("Arxiv Anvar", w.Live, archived: true, relations: new[] { "father" });

        var (_, e1) = await CampaignService.CreateAsync(w.Ctx, null, new CreateCampaignRequest("broadcast",
            new AudienceSpec { Kind = "archive" }, "Salom", null, false), "u1", "Admin", true);
        Assert.Equal(CampaignService.ErrUseArchive, e1);

        var (_, e2) = await CampaignService.CreateAsync(w.Ctx, null, new CreateCampaignRequest("archive",
            Active(), "Salom", null, false), "u1", "Admin", true);
        Assert.Equal(CampaignService.ErrArchiveOnly, e2);

        var (_, e3) = await CampaignService.CreateAsync(w.Ctx, null, new CreateCampaignRequest("archive",
            new AudienceSpec { Kind = "selected", StudentIds = new() { live.Id, arch.Id } }, "Salom", null, false), "u1", "Admin", true);
        Assert.Equal(CampaignService.ErrArchiveOnly, e3);

        var (ok, e4) = await CampaignService.CreateAsync(w.Ctx, null, new CreateCampaignRequest("archive",
            new AudienceSpec { Kind = "archive" }, "Salom", null, false), "u1", "Admin", true);
        Assert.Null(e4);
        Assert.Equal(1, ok!.Messages);
        Assert.Equal("archive", w.Ctx.Broadcasts.Single().Kind);
    }

    [Fact]
    public async Task Kampaniya_validatsiyasi()
    {
        using var w = new World();
        w.Student("Faol Ali", w.Live, relations: new[] { "father" });

        Assert.Equal(CampaignService.ErrBotOff, (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), "Salom", null, false), "u1", "Admin", false)).Error);
        Assert.Equal(CampaignService.ErrEmpty, (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), "  ", null, false), "u1", "Admin", true)).Error);
        Assert.Equal("Noma'lum o'zgaruvchi: {ismi}", (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), "{ismi}", null, false), "u1", "Admin", true)).Error);
        Assert.Equal("Formatlash yopilmagan: <b>", (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), "<b>Salom", null, false), "u1", "Admin", true)).Error);
        Assert.Equal(CampaignService.ErrNoRecipients, (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active("student"), "Salom", null, false), "u1", "Admin", true)).Error);
        var tooLong = (await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), new string('x', 5000), null, false), "u1", "Admin", true)).Error;
        Assert.StartsWith("Xabar juda uzun", tooLong);
        Assert.Empty(w.Ctx.MessageDeliveries);
    }

    [Fact]
    public async Task Bekor_qilish_navbatdagilarni_bekor_qiladi()
    {
        using var w = new World();
        w.Student("Faol Ali", w.Live, relations: new[] { "father", "mother" });
        var (created, _) = await CampaignService.CreateAsync(w.Ctx, null,
            new CreateCampaignRequest("broadcast", Active(), "Salom", null, false), "u1", "Admin", true);
        var n = await CampaignService.CancelAsync(w.Ctx, null, created!.Id);
        Assert.Equal(2, n);
        Assert.All(w.Ctx.MessageDeliveries.ToList(), d => Assert.Equal("cancelled", d.Status));
        Assert.Equal("cancelled", w.Ctx.Broadcasts.Single().Status);
        Assert.Null(await CampaignService.CancelAsync(w.Ctx, null, "yoq"));
    }

    [Fact]
    public async Task Oqituvchilar_auditoriyasi_chat_boyicha()
    {
        using var w = new World();
        var t = new Teacher { FullName = "Karimov Karim" };
        var archived = new Teacher { FullName = "Eski Ustoz", IsArchived = true };
        w.Ctx.Teachers.AddRange(t, archived);
        w.Ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { TeacherId = t.Id, StudentId = "", ChatId = 7001 },
            new TelegramRegistration { TeacherId = archived.Id, StudentId = "", ChatId = 7002 });
        w.Ctx.SaveChanges();
        var (plan, error) = await CampaignService.PlanAsync(w.Ctx, "broadcast", new AudienceSpec { Kind = "teachers" },
            "Hurmatli {fish}!", null, false, render: true);
        Assert.Null(error);
        var item = Assert.Single(plan!.Items);
        Assert.Equal(7001, item.ChatId);
        Assert.Contains("Karimov Karim", item.Text);
    }

    [Fact]
    public async Task Qarzdorlar_kampaniyasi_HAR_FAN_uchun_alohida_va_tizim_matni()
    {
        using var w = new World();
        var s = w.Student("Qarzdor Qodir", null, balance: -300000m, relations: new[] { "father" });
        var (plan, error) = await CampaignService.PlanAsync(w.Ctx, "debt",
            new AudienceSpec { Kind = "selected", StudentIds = new() { s.Id } }, "", null, true, render: true);
        Assert.Null(error);
        var item = Assert.Single(plan!.Items);   // legacy (no membership) → one message from the balance
        Assert.Contains("300 000 so'm", item.Text);
        Assert.DoesNotContain("Markaz e'loni", item.Text);   // debt campaigns have no header
        Assert.Equal("debt", item.Trigger);
    }
}
