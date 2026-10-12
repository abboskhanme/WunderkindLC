using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CRM upgrade Phase 2 — flows through the outbox: auto-message dispatch (relations, ack, dedupe, header), «Qabul
/// qildim», multi-account registration + «01», per-rule debt schedule, achievements, Telegram link summary, startup
/// data fixes and attachment validation.
/// </summary>
public class MessagingFlowTests
{
    private static Student NewStudent(string name = "Aliyev Ali", string? userId = "u-1") => new()
    {
        FullName = name, FirstName = name.Split(' ').Last(), LastName = name.Split(' ')[0], UserId = userId,
        Phone = "+998-90-111-22-33", ParentPhone = "+998-90-444-55-66",
        FatherPhone = "+998-90-444-55-66", MotherPhone = "+998-91-777-88-99",
    };

    private static AutoMessageRule Rule(string trigger, string template, bool ack = false, string audience = "parents") => new()
    {
        Trigger = trigger, Name = "", Enabled = true, SendTelegram = true, Template = template, AckButton = ack, Audience = audience,
    };

    // ===================== Auto messages → outbox =====================

    [Fact]
    public async Task Avto_xabar_ota_ona_chatlariga_navbatga_tushadi_oquvchi_chati_va_bloklagan_chiqariladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent(name: "Aliyev Ali <b>");
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" },
            new TelegramRegistration { StudentId = s.Id, ChatId = 2, Relation = "student" },
            new TelegramRegistration { StudentId = s.Id, ChatId = 3, Relation = "mother", BlockedAt = AppClock.Now },
            new TelegramRegistration { StudentId = s.Id, ChatId = 4, Relation = "" });
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.PaymentReceived, "<b>To'lov</b> {fish}: {summa}", ack: true));
        ctx.SaveChanges();

        var n = await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.PaymentReceived, s,
            new Dictionary<string, string> { ["{summa}"] = "100 000" });

        Assert.Equal(2, n);
        var rows = ctx.MessageDeliveries.AsNoTracking().OrderBy(d => d.ChatId).ToList();
        Assert.Equal(new long[] { 1, 4 }, rows.Select(r => r.ChatId));
        Assert.All(rows, r =>
        {
            Assert.True(r.AckRequired);
            Assert.Equal("auto", r.Source);
            Assert.StartsWith("<b>🔔 ", r.Text);
            Assert.Contains("<b>To'lov</b> Aliyev Ali &lt;b&gt;: 100 000", r.Text);   // value escaped
        });
        Assert.Equal("parent", rows[1].Relation);   // "" (not backfilled) stored as parent
        // Cabinet bell: plain text, no markup.
        var bell = Assert.Single(ctx.UserNotifications);
        Assert.Equal("To'lov Aliyev Ali <b>: 100 000", bell.Body);
    }

    [Fact]
    public async Task Oquvchilar_auditoriyasi_faqat_oquvchining_oz_chatiga()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" },
            new TelegramRegistration { StudentId = s.Id, ChatId = 2, Relation = "student" });
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.GradeEntered, "{ism}: {baho}", audience: "students"));
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.GradeEntered, s,
            new Dictionary<string, string> { ["{baho}"] = "5" });

        Assert.Equal(2, Assert.Single(ctx.MessageDeliveries.AsNoTracking()).ChatId);
    }

    [Fact]
    public async Task EventKey_bir_hodisa_ikki_marta_navbatga_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.AttendanceAbsent, "{ism} kelmadi"));
        ctx.SaveChanges();
        var auto = new MessagingStack().Auto;

        Assert.Equal(1, await auto.DispatchAttendanceAbsentAsync(ctx, s, "A", "Sababsiz", "2026-10-12", eventKey: "item-1", sourceId: "item-1"));
        Assert.Equal(0, await auto.DispatchAttendanceAbsentAsync(ctx, s, "A", "Sababsiz", "2026-10-12", eventKey: "item-1", sourceId: "item-1"));
        var row = Assert.Single(ctx.MessageDeliveries.AsNoTracking());
        Assert.Equal("item-1", row.SourceId);
        var summary = await TelegramOutbox.SummaryBySourceAsync(ctx, new[] { "item-1" });
        Assert.Equal(1, summary["item-1"].Pending);
    }

    [Fact]
    public async Task Arxivdagi_oquvchiga_faqat_tolov_kvitansiyasi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.IsArchived = true;
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        ctx.AutoMessageRules.AddRange(Rule(AutoMessageTriggers.AttendanceAbsent, "x"), Rule(AutoMessageTriggers.PaymentReceived, "y"));
        ctx.SaveChanges();
        var auto = new MessagingStack().Auto;

        Assert.Equal(0, await auto.DispatchStudentAsync(ctx, AutoMessageTriggers.AttendanceAbsent, s));
        Assert.Equal(1, await auto.DispatchStudentAsync(ctx, AutoMessageTriggers.PaymentReceived, s));
    }

    [Fact]
    public async Task Eski_shablondagi_buzuq_teg_matn_qilib_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.PaymentReceived, "<b>Rahmat {ism}"));   // unclosed legacy markup
        ctx.SaveChanges();

        await new MessagingStack().Auto.DispatchStudentAsync(ctx, AutoMessageTriggers.PaymentReceived, s);

        var text = Assert.Single(ctx.MessageDeliveries.AsNoTracking()).Text;
        Assert.Contains("&lt;b&gt;Rahmat Ali", text);
        Assert.Empty(TelegramHtml.SanitizeTemplate(text).Errors);
    }

    // ===================== «Qabul qildim» =====================

    [Fact]
    public async Task Ack_togri_chat_bir_marta_notogri_chat_ozgarmaydi_notanish_id_xatosiz()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var row = TelegramOutbox.Build(new OutboxItem("auto", "payment_debt", 42, "x", AckRequired: true), AppClock.Now);
        row.Status = "sent";
        row.TelegramMessageId = 9000;
        ctx.MessageDeliveries.Add(row);
        ctx.SaveChanges();
        var sender = new FakeTelegramSender();

        Assert.Equal(AckOutcome.WrongChat, await MessageAckService.HandleAsync(ctx, sender, 43, "cb1", row.Id));
        Assert.Null(ctx.MessageDeliveries.AsNoTracking().Single().AckAt);
        Assert.Equal(MessageAckService.ToastWrongChat, sender.Answers[^1].Text);

        Assert.Equal(AckOutcome.Acknowledged, await MessageAckService.HandleAsync(ctx, sender, 42, "cb2", row.Id));
        var ackAt = ctx.MessageDeliveries.AsNoTracking().Single().AckAt;
        Assert.NotNull(ackAt);
        Assert.Equal((42L, 9000L), Assert.Single(sender.KeyboardsRemoved));
        Assert.Equal(MessageAckService.ToastOk, sender.Answers[^1].Text);

        Assert.Equal(AckOutcome.AlreadyAcknowledged, await MessageAckService.HandleAsync(ctx, sender, 42, "cb3", row.Id));
        Assert.Equal(ackAt, ctx.MessageDeliveries.AsNoTracking().Single().AckAt);
        Assert.Single(sender.KeyboardsRemoved);

        Assert.Equal(AckOutcome.NotFound, await MessageAckService.HandleAsync(ctx, sender, 42, "cb4", "yoq"));
        Assert.Equal(MessageAckService.ToastNotFound, sender.Answers[^1].Text);
    }

    [Fact]
    public void Ack_tugmasi_mack_prefiksi()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(MessageAckService.Keyboard("abc"));
        Assert.Contains("\"callback_data\":\"mack:abc\"", json);
        Assert.Contains("Qabul qildim", System.Text.RegularExpressions.Regex.Unescape(json));
    }

    // ===================== Registration (multi-account) + «01» =====================

    [Fact]
    public async Task Ota_ona_raqamlari_boyicha_taniladi_aka_ukaga_bittadan_ulanish()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var a = NewStudent("Aliyev Ali");
        var b = NewStudent("Aliyev Bobur");
        b.Phone = "+998-90-999-99-99";
        var other = NewStudent("Begona Bek");
        other.Phone = other.ParentPhone = other.FatherPhone = other.MotherPhone = "+998-93-000-00-00";
        ctx.Students.AddRange(a, b, other);
        ctx.SaveChanges();

        var byMother = await TelegramRegistrar.MatchStudentsAsync(ctx, "+998917778899");
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), byMother.Select(s => s.Id).OrderBy(x => x));

        var created = await TelegramRegistrar.UpsertStudentLinksAsync(ctx, 777, "998917778899", "Ona", byMother);
        ctx.SaveChanges();
        Assert.Equal(2, created.Count);
        Assert.All(ctx.TelegramRegistrations.AsNoTracking().ToList(), r => Assert.Equal("mother", r.Relation));

        // Same contact again: nothing new; an existing relation is NOT overwritten, the block mark is cleared.
        var reg = ctx.TelegramRegistrations.First(r => r.StudentId == a.Id);
        reg.Relation = "other";   // admin edit
        reg.BlockedAt = AppClock.Now;
        ctx.SaveChanges();
        var again = await TelegramRegistrar.UpsertStudentLinksAsync(ctx, 777, "998917778899", "Ona", byMother);
        ctx.SaveChanges();
        Assert.Empty(again);
        var after = ctx.TelegramRegistrations.AsNoTracking().First(r => r.StudentId == a.Id);
        Assert.Equal("other", after.Relation);
        Assert.Null(after.BlockedAt);
    }

    [Fact]
    public async Task Uch_kishi_uch_ulanish_har_biriga_01_xabar_bir_marta()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.BotConnected,
            AutoMessageTriggers.Get(AutoMessageTriggers.BotConnected)!.DefaultTemplate));
        ctx.SaveChanges();
        var auto = new MessagingStack().Auto;

        foreach (var (chat, phone) in new[] { (11L, "998904445566"), (12L, "998917778899"), (13L, "998901112233") })
        {
            var students = await TelegramRegistrar.MatchStudentsAsync(ctx, phone);
            var created = await TelegramRegistrar.UpsertStudentLinksAsync(ctx, chat, phone, "", students);
            ctx.SaveChanges();
            foreach (var reg in created) await auto.DispatchBotConnectedAsync(ctx, s, reg);
            foreach (var reg in ctx.TelegramRegistrations.Where(r => r.ChatId == chat).ToList())
                await auto.DispatchBotConnectedAsync(ctx, s, reg);   // repeated contact → deduped
        }

        var regs = ctx.TelegramRegistrations.AsNoTracking().OrderBy(r => r.ChatId).ToList();
        Assert.Equal(new[] { "father", "mother", "student" }, regs.Select(r => r.Relation));
        var rows = ctx.MessageDeliveries.AsNoTracking().OrderBy(d => d.ChatId).ToList();
        Assert.Equal(new long[] { 11, 12, 13 }, rows.Select(r => r.ChatId));
        Assert.Contains("Otasi", rows.Select(r => r.Relation).Select(TelegramRelations.Label));
        Assert.Contains("Ali", rows[0].Text);
    }

    [Fact]
    public async Task Blokdan_chiqarish_va_belgilash()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = "a", ChatId = 5, BlockedAt = AppClock.Now },
            new TelegramRegistration { StudentId = "b", ChatId = 5, BlockedAt = AppClock.Now });
        ctx.SaveChanges();
        Assert.True(await TelegramBlocks.UnblockAsync(ctx, 5));
        Assert.All(ctx.TelegramRegistrations.AsNoTracking().ToList(), r => Assert.Null(r.BlockedAt));
        Assert.False(await TelegramBlocks.UnblockAsync(ctx, 5));
        Assert.True(TelegramBlocks.BlockedByStatus("kicked"));
        Assert.False(TelegramBlocks.BlockedByStatus("member"));
        Assert.Null(TelegramBlocks.BlockedByStatus("left"));
    }

    // ===================== Debt reminder — per-rule schedule =====================

    private static PaymentReminderService Payment(Infrastructure.Data.AppDbContext ctx) =>
        new(new SingleServiceProvider(ctx), new MessagingStack().Telegram, NullLogger<PaymentReminderService>.Instance);

    private static AutoMessageRule DebtRule(int day, string time) => new()
    {
        Trigger = AutoMessageTriggers.PaymentDebt, Enabled = true, SendTelegram = true, Template = "",
        ScheduleType = "monthly", ScheduleDayOfMonth = day, ScheduleTime = time, AckButton = true,
    };

    [Fact]
    public void Qarzdorlik_10_kun_09_00_faqat_10_sanada_09_00_dan_keyin()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -200000m;
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        ctx.AutoMessageRules.Add(DebtRule(10, "09:00"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx), "TickAsync", new DateTime(2026, 10, 9, 12, 0, 0), CancellationToken.None);
        Reflect.RunAsyncMethod(Payment(ctx), "TickAsync", new DateTime(2026, 10, 10, 8, 59, 0), CancellationToken.None);
        Assert.Empty(ctx.MessageDeliveries);

        Reflect.RunAsyncMethod(Payment(ctx), "TickAsync", new DateTime(2026, 10, 10, 9, 1, 0), CancellationToken.None);
        var row = Assert.Single(ctx.MessageDeliveries.AsNoTracking());
        Assert.True(row.AckRequired);
        Assert.StartsWith(MessageRender.HeaderDebt, row.Text);
        Assert.Contains("200 000 so'm", row.Text);
        Assert.Equal(AutoMessageTriggers.PaymentDebt, row.Trigger);
        Assert.EndsWith(":2026-10-10", row.DedupeKey);

        // Restart (new instance) the same day — the rule is done; no duplicate.
        Reflect.RunAsyncMethod(Payment(ctx), "TickAsync", new DateTime(2026, 10, 10, 15, 0, 0), CancellationToken.None);
        Assert.Single(ctx.MessageDeliveries);
    }

    [Fact]
    public void Qarzdorlik_31_kun_fevralda_oxirgi_kun_ikki_qoida_bir_kunda_ikkalasi()
    {
        var r31 = DebtRule(31, "09:00");
        Assert.True(AutoMessageSchedule.IsDue(r31, new DateTime(2026, 2, 28, 9, 0, 0)));
        Assert.False(AutoMessageSchedule.IsDue(r31, new DateTime(2026, 2, 27, 23, 0, 0)));
        Assert.True(AutoMessageSchedule.IsDue(r31, new DateTime(2028, 2, 29, 10, 0, 0)));

        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        s.Balance = -100000m;
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        ctx.AutoMessageRules.AddRange(DebtRule(5, "09:00"), DebtRule(5, "10:30"));
        ctx.SaveChanges();

        Reflect.RunAsyncMethod(Payment(ctx), "TickAsync", new DateTime(2026, 10, 5, 11, 0, 0), CancellationToken.None);
        Assert.Equal(2, ctx.MessageDeliveries.Count());
        Assert.Equal(2, ctx.UserNotifications.Count());
    }

    // ===================== Achievements =====================

    [Fact]
    public async Task Yutuq_tabrigi_navbatga_qayta_yuborishda_yangi_kalit_arxivga_yoq()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = s.Id, ChatId = 1, Relation = "father" });
        var a = new StudentAchievement { StudentId = s.Id, Kind = "ielts", Title = "IELTS", Result = "7.5", Date = "2026-10-01" };
        ctx.StudentAchievements.Add(a);
        ctx.SaveChanges();
        var auto = new MessagingStack().Auto;

        Assert.Equal(AchievementService.ErrNoRule, (await AchievementService.QueueCongratulationAsync(ctx, auto, a, s)).Error);

        ctx.AutoMessageRules.Add(Rule(AutoMessageTriggers.Achievement,
            AutoMessageTriggers.Get(AutoMessageTriggers.Achievement)!.DefaultTemplate));
        ctx.SaveChanges();
        var (n1, e1) = await AchievementService.QueueCongratulationAsync(ctx, auto, a, s);
        var (n2, _) = await AchievementService.QueueCongratulationAsync(ctx, auto, a, s);
        Assert.Null(e1);
        Assert.Equal(1, n1);
        Assert.Equal(1, n2);   // explicit resend → new dedupe key
        var rows = ctx.MessageDeliveries.AsNoTracking().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(a.Id, r.SourceId));
        Assert.Contains("IELTS 7.5", rows[0].Text);
        Assert.NotNull(ctx.StudentAchievements.AsNoTracking().Single().CongratulatedAt);

        s.IsArchived = true;
        Assert.Equal(AchievementService.ErrArchived, (await AchievementService.QueueCongratulationAsync(ctx, auto, a, s)).Error);
    }

    [Fact]
    public async Task Yutuq_validatsiyasi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        Assert.NotNull(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "nope", "T", "", "2026-01-01", "", null)));
        Assert.NotNull(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "ielts", "", "", "2026-01-01", "", null)));
        Assert.NotNull(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "ielts", "T", "", "2999-01-01", "", null)));
        Assert.NotNull(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "ielts", "T", new string('x', 51), "2026-01-01", "", null)));
        Assert.NotNull(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "ielts", "T", "", "2026-01-01", "", "no-such")));
        Assert.Null(await AchievementService.ValidateAsync(ctx, new SaveAchievementRequest("s", "cefr", "CEFR", "B2", "2026-01-01", "", null)));
        Assert.Equal("Olimpiada", AchievementService.KindLabel("olympiad"));
    }

    // ===================== U6b — Telegram link summary =====================

    [Fact]
    public async Task Ulangan_soni_bloklaganni_hisoblamaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = "s1", ChatId = 1, Relation = "mother" },
            new TelegramRegistration { StudentId = "s1", ChatId = 2, Relation = "father" },
            new TelegramRegistration { StudentId = "s1", ChatId = 3, Relation = "student", BlockedAt = AppClock.Now },
            new TelegramRegistration { StudentId = "s2", ChatId = 4, Relation = "", BlockedAt = AppClock.Now });
        ctx.SaveChanges();

        var map = await TelegramLinkSummary.ForStudentsAsync(ctx, new[] { "s1", "s2", "s3" });
        Assert.Equal(2, map["s1"].Linked);
        Assert.Equal(new[] { "father", "mother" }, map["s1"].Relations);
        Assert.Equal(1, map["s1"].Blocked);
        Assert.Equal(0, map["s2"].Linked);
        Assert.Equal(1, map["s2"].Blocked);
        Assert.False(map.ContainsKey("s3"));
    }

    // ===================== Startup data fixes =====================

    [Fact]
    public async Task Startup_tuzatishlari_bir_martalik()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = NewStudent();
        ctx.Students.Add(s);
        ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = s.Id, ChatId = 1, Phone = "998917778899" },
            new TelegramRegistration { StudentId = s.Id, ChatId = 2, Phone = "998901112233", Relation = "other" },
            new TelegramRegistration { StudentId = "", TeacherId = "t1", ChatId = 3, Phone = "998901112233" });
#pragma warning disable CS0618
        ctx.AutoMessageRules.AddRange(
            new AutoMessageRule { Trigger = "birthday", Name = "Push", Enabled = true, SendPush = true },
            new AutoMessageRule { Trigger = "payment_debt", Name = "Sms", Enabled = true, SendSms = true },
            new AutoMessageRule { Trigger = "payment_received", Name = "Tg", Enabled = true, SendTelegram = true });
#pragma warning restore CS0618
        ctx.SaveChanges();

        Assert.Equal(1, await MessagingDataFixes.BackfillRelationsAsync(ctx));
        Assert.Null(await MessagingDataFixes.BackfillRelationsAsync(ctx));   // marker
        var regs = ctx.TelegramRegistrations.AsNoTracking().OrderBy(r => r.ChatId).ToList();
        Assert.Equal("mother", regs[0].Relation);
        Assert.Equal("other", regs[1].Relation);   // untouched
        Assert.Equal("", regs[2].Relation);        // teacher row

        var disabled = await MessagingDataFixes.TelegramOnlyRulesAsync(ctx);
        Assert.Equal(new[] { "Push" }, disabled);
        Assert.Null(await MessagingDataFixes.TelegramOnlyRulesAsync(ctx));
        var rules = ctx.AutoMessageRules.AsNoTracking().ToList();
        Assert.All(rules, r => Assert.True(r.SendTelegram));
        Assert.False(rules.Single(r => r.Name == "Push").Enabled);
        Assert.True(rules.Single(r => r.Name == "Sms").Enabled);   // Phase 1: legacy SMS rules go over Telegram
    }

    // ===================== Attachments =====================

    [Theory]
    [InlineData("a.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "photo")]
    [InlineData("a.PNG", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "photo")]
    [InlineData("v.mp4", "video/mp4", new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 }, "video")]
    [InlineData("d.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "document")]
    [InlineData("d.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "document")]
    [InlineData("d.xls", "application/vnd.ms-excel", new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, "document")]
    public void Biriktirma_turlari_qabul_qilinadi(string name, string type, byte[] head, string kind)
    {
        var (k, error) = MessageAttachmentRules.Validate(name, type, 1000, head);
        Assert.Null(error);
        Assert.Equal(kind, k);
    }

    [Fact]
    public void Biriktirma_rad_etish_sabablari()
    {
        byte[] jpg = { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.Equal(MessageAttachmentRules.ErrType, MessageAttachmentRules.Validate("a.gif", "image/gif", 10, jpg).Error);
        Assert.Equal(MessageAttachmentRules.ErrType, MessageAttachmentRules.Validate("a.html", "text/html", 10, jpg).Error);
        Assert.Equal(MessageAttachmentRules.ErrMagic, MessageAttachmentRules.Validate("a.jpg", "image/jpeg", 10, new byte[] { 0x3C, 0x68 }).Error);
        Assert.Equal(MessageAttachmentRules.ErrMagic, MessageAttachmentRules.Validate("a.jpg", "text/html", 10, jpg).Error);
        Assert.Equal(MessageAttachmentRules.ErrMagic, MessageAttachmentRules.Validate("a.png", "image/png", 10, jpg).Error);
        Assert.Equal(MessageAttachmentRules.ErrPhotoSize,
            MessageAttachmentRules.Validate("a.jpg", "image/jpeg", MessageAttachmentRules.MaxPhotoBytes + 1, jpg).Error);
        Assert.Equal(MessageAttachmentRules.ErrFileSize, MessageAttachmentRules.Validate("v.mp4", "video/mp4",
            MessageAttachmentRules.MaxFileBytes + 1, new byte[] { 0, 0, 0, 0, 0x66, 0x74, 0x79, 0x70 }).Error);
        Assert.Equal(MessageAttachmentRules.ErrEmpty, MessageAttachmentRules.Validate("a.jpg", "image/jpeg", 0, jpg).Error);
    }

    [Fact]
    public async Task Saqlangan_fayl_nomi_GUID_va_yolni_tekshiradi()
    {
        using var db = TestDb.Sqlite();
        var dir = Path.Combine(Path.GetTempPath(), "wlc-att-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MessageAttachmentStore(dir);
            var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 };
            using var ms = new MemoryStream(bytes);
            var (a, error) = await MessageAttachments.CreateAsync(db.Context, store, ms, "../../shartnoma ali.pdf",
                "application/pdf", bytes.Length, "u1", "Admin");
            Assert.Null(error);
            Assert.Matches("^/uploads/messaging/[0-9a-f]{32}\\.pdf$", a!.FileUrl);
            Assert.Equal("shartnoma ali.pdf", a.FileName);
            Assert.Equal(bytes.Length, a.SizeBytes);
            Assert.True(store.Exists(a.FileUrl));
            Assert.Null(store.PhysicalPath("/uploads/messaging/../secret.pdf"));
            Assert.Null(store.PhysicalPath("/uploads/contract.pdf"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
