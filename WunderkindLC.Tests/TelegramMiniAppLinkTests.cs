using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-12 — one bot for everyone, no phone sharing: the chat is linked when its owner logs in inside the
/// «📱 Panel» Mini App (<see cref="TelegramMiniAppLink"/>, <c>POST api/telegram/link</c>). Only the CALLER's own
/// account(s) are ever linked.
/// </summary>
public class TelegramMiniAppLinkTests
{
    private const long Chat = 555_000_111;

    private static Student NewStudent(string name, string? userId, bool archived = false) => new()
    {
        FullName = name, UserId = userId, IsArchived = archived,
        Phone = "+998-90-111-22-33", ParentPhone = "+998-90-444-55-66",
        FatherPhone = "+998-90-444-55-66", MotherPhone = "+998-91-777-88-99",
    };

    [Fact]
    public async Task Oquvchi_akkaunti_FAQAT_oz_oquvchisini_ulaydi_va_takror_ulanmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var mine = NewStudent("Aliyev Ali", "u-stu");
        var other = NewStudent("Begona Bola", "u-other");   // same parent phones — must NOT be linked
        ctx.Students.AddRange(mine, other);
        ctx.SaveChanges();

        var o = await TelegramMiniAppLink.LinkAsync(ctx, "u-stu", Roles.Student, Chat, "Ali");
        ctx.SaveChanges();

        var reg = Assert.Single(ctx.TelegramRegistrations.AsNoTracking());
        Assert.Equal(mine.Id, reg.StudentId);
        Assert.Equal(Chat, reg.ChatId);
        Assert.Equal(TelegramRelations.Other, reg.Relation);   // no phone → «Boshqa», admin-editable
        Assert.True(o.AnyNew);
        Assert.Contains("Aliyev Ali (o'quvchi)", TelegramMiniAppLink.ConfirmationText(o));

        var again = await TelegramMiniAppLink.LinkAsync(ctx, "u-stu", Roles.Student, Chat, "Ali");
        ctx.SaveChanges();
        Assert.False(again.AnyNew);
        Assert.Single(ctx.TelegramRegistrations.AsNoTracking());
    }

    [Fact]
    public async Task Ota_ona_akkaunti_farzandlarini_qarindoshlik_bilan_ulaydi_arxivdagini_emas()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.Add(new AppUser { Id = "u-par", Email = "+998-91-777-88-99", Role = TelegramMiniAppLink.ParentRole, FullName = "Ona" });
        var a = NewStudent("Aka", null);
        var b = NewStudent("Uka", null);
        var gone = NewStudent("Arxivdagi", null, archived: true);
        ctx.Students.AddRange(a, b, gone);
        ctx.SaveChanges();

        var o = await TelegramMiniAppLink.LinkAsync(ctx, "u-par", TelegramMiniAppLink.ParentRole, Chat, "Ona");
        ctx.SaveChanges();

        var regs = ctx.TelegramRegistrations.AsNoTracking().ToList();
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), regs.Select(r => r.StudentId).OrderBy(x => x));
        Assert.All(regs, r => Assert.Equal(TelegramRelations.Mother, r.Relation));
        Assert.Equal(2, o.NewStudentLinks.Count);
    }

    [Fact]
    public async Task Oqituvchi_va_xodim_oz_akkauntiga_ulanadi_blok_belgisi_tozalanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var t = new Teacher { FullName = "Ustoz", UserId = "u-t" };
        ctx.Teachers.AddRange(t, new Teacher { FullName = "Arxiv ustoz", UserId = "u-t", IsArchived = true });
        ctx.Users.Add(new AppUser { Id = "u-adm", Email = "admin", Role = Roles.Admin, FullName = "Admin" });
        ctx.TelegramRegistrations.Add(new TelegramRegistration
        {
            UserId = "u-adm", StudentId = "", ChatId = Chat, BlockedAt = AppClock.Now,
        });
        ctx.SaveChanges();

        var ot = await TelegramMiniAppLink.LinkAsync(ctx, "u-t", Roles.Teacher, Chat, "Ustoz");
        var oa = await TelegramMiniAppLink.LinkAsync(ctx, "u-adm", Roles.Admin, Chat, "Admin");
        ctx.SaveChanges();

        Assert.Equal(t.Id, Assert.Single(ot.NewTeachers).Id);
        Assert.False(oa.NewStaffLink);   // already linked — refreshed only
        // ONE non-student row per chat (unique StudentId+ChatId) — the teacher id joins the staff row.
        var row = Assert.Single(ctx.TelegramRegistrations.AsNoTracking());
        Assert.Equal(t.Id, row.TeacherId);
        Assert.Equal("u-adm", row.UserId);
        Assert.Null(row.BlockedAt);

        // Another teacher account on the same chat: not linked, and no unique-index failure.
        ctx.Teachers.Add(new Teacher { FullName = "Boshqa ustoz", UserId = "u-t2" });
        ctx.SaveChanges();
        var o2 = await TelegramMiniAppLink.LinkAsync(ctx, "u-t2", Roles.Teacher, Chat, "");
        ctx.SaveChanges();
        Assert.False(o2.AnyNew);
        Assert.Single(ctx.TelegramRegistrations.AsNoTracking());
    }

    [Fact]
    public async Task Nomalum_rol_hech_narsa_ulamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Students.Add(NewStudent("Aliyev Ali", "u-x"));
        ctx.SaveChanges();

        var o = await TelegramMiniAppLink.LinkAsync(ctx, "u-x", Roles.PlatformOwner, Chat, "");
        ctx.SaveChanges();

        Assert.False(o.AnyNew);
        Assert.Empty(ctx.TelegramRegistrations.AsNoTracking());
    }

    [Fact]
    public void Endpoint_faqat_kirgan_foydalanuvchiga_va_FAQAT_imzolangan_initData_bilan()
    {
        // The test project does not reference the Server — checked from the controller source (RBAC pattern).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers", "TelegramLinkController.cs"));
        Assert.Contains("[Authorize]", src);
        Assert.DoesNotContain("AllowAnonymous", src);
        Assert.Contains("TelegramInitData.Validate(req.InitData, telegram.BotToken)", src);
        // The account comes from the session, never from the request body.
        Assert.Contains("ClaimTypes.NameIdentifier", src);
        Assert.DoesNotContain("StudentId", src.Split("record LinkRequest")[1].Split(';')[0]);
    }

    [Fact]
    public void Ulanmagan_chat_klaviaturasi_PANEL_tepada_telefon_SOROLMAYDI()
    {
        foreach (var books in new[] { true, false })
        {
            var rows = JsonDocument.Parse(JsonSerializer.Serialize(TelegramBotService.StartKeyboard(books)))
                .RootElement.GetProperty("keyboard").EnumerateArray().ToList();
            var first = rows[0].EnumerateArray().First();
            Assert.Equal(TelegramBotService.PanelButtonText, first.GetProperty("text").GetString());
            Assert.Equal(TelegramBotService.PanelUrl, first.GetProperty("web_app").GetProperty("url").GetString());
            var all = rows.SelectMany(r => r.EnumerateArray()).ToList();
            Assert.DoesNotContain(all, b => b.TryGetProperty("request_contact", out _));
            Assert.Equal(books, all.Any(b => b.GetProperty("text").GetString() == BookShopBotService.BooksButtonText));
        }
        Assert.DoesNotContain("telefon", TelegramBotService.StartWelcomeText, StringComparison.OrdinalIgnoreCase);
    }
}
