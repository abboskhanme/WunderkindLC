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
    public void Ulanmagan_chat_klaviaturasi_FAQAT_Panel_kitob_support_telefon_YOQ()
    {
        var rows = JsonDocument.Parse(JsonSerializer.Serialize(TelegramBotService.StartKeyboard()))
            .RootElement.GetProperty("keyboard").EnumerateArray().ToList();
        var button = Assert.Single(Assert.Single(rows).EnumerateArray());
        Assert.Equal(TelegramBotService.PanelButtonText, button.GetProperty("text").GetString());
        Assert.Equal(TelegramBotService.PanelUrl, button.GetProperty("web_app").GetProperty("url").GetString());
        Assert.False(button.TryGetProperty("request_contact", out _));
        Assert.DoesNotContain("telefon", TelegramBotService.StartWelcomeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login/parol", TelegramBotService.GateText);
    }

    // ===== Keyboard-button Mini App: sendData token (2026-10-13) =====

    [Fact]
    public void SendData_tokeni_bir_martalik_hisobga_boglangan_va_10_daqiqada_eskiradi()
    {
        var now = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
        var tokens = new TelegramLinkTokens(() => now);

        var data = tokens.Issue("u-1", Roles.Student);
        Assert.StartsWith(TelegramLinkTokens.DataPrefix, data);
        Assert.True(data.Length >= TelegramLinkTokens.DataPrefix.Length + 32);   // ≥ 192 random bits, base64url
        Assert.NotEqual(data, tokens.Issue("u-1", Roles.Student));

        Assert.Equal(("u-1", Roles.Student), tokens.Redeem(data));
        Assert.Null(tokens.Redeem(data));                                        // one-time
        Assert.Null(tokens.Redeem(data[TelegramLinkTokens.DataPrefix.Length..])); // prefix required
        Assert.Null(tokens.Redeem(null));
        Assert.Null(tokens.Redeem(TelegramLinkTokens.DataPrefix + "guess"));

        var late = tokens.Issue("u-2", Roles.Teacher);
        now = now.AddMinutes(11);
        Assert.Null(tokens.Redeem(late));                                        // expired
    }

    [Fact]
    public async Task IsLinked_hisobning_bloklanmagan_ulanishi_bormi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent("Aliyev Ali", "u-stu");
        var t = new Teacher { FullName = "Ustoz", UserId = "u-t" };
        ctx.Students.Add(s);
        ctx.Teachers.Add(t);
        ctx.SaveChanges();

        Assert.False(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-stu", Roles.Student));
        Assert.False(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-t", Roles.Teacher));
        Assert.True(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-x", Roles.PlatformOwner));   // nothing to link — no banner

        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = Chat, BlockedAt = AppClock.Now });
        ctx.SaveChanges();
        Assert.False(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-stu", Roles.Student));      // blocked = not receiving

        await TelegramMiniAppLink.LinkAsync(ctx, "u-stu", Roles.Student, Chat, "");
        await TelegramMiniAppLink.LinkAsync(ctx, "u-t", Roles.Teacher, Chat + 1, "");
        ctx.SaveChanges();
        Assert.True(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-stu", Roles.Student));
        Assert.True(await TelegramMiniAppLink.IsLinkedAsync(ctx, "u-t", Roles.Teacher));
    }

    [Fact]
    public void Bot_web_app_data_ni_qabul_qiladi_kodni_LOGGA_yozmaydi_va_starti_inline_Panel_beradi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "WunderkindLC.Application", "Services", "TelegramBotService.cs"));
        var branch = src[src.IndexOf("msg.TryGetProperty(\"web_app_data\"", StringComparison.Ordinal)..];
        branch = branch[..branch.IndexOf("return;", StringComparison.Ordinal)];
        Assert.Contains("LogInAsync(chatId, \"[Panel: botga ulash so'rovi]\"", branch);
        Assert.DoesNotContain("LogInAsync(chatId, data", branch);
        Assert.Contains("SendAsync(chatId, StartWelcomeText, PanelInline, ct)", src);   // inline launch → initData
        var ctrl = File.ReadAllText(Path.Combine(dir.FullName, "WunderkindLC.Server", "Controllers", "TelegramLinkController.cs"));
        Assert.Contains("tokens.Issue(uid,", ctrl);   // token bound to the session's account
    }
}
