using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Fake Telegram: records sends, answers with queued outcomes (default Ok with growing message ids).</summary>
internal sealed class FakeTelegramSender : ITelegramSender
{
    public bool IsConfigured { get; set; } = true;
    public List<TgSend> Sent { get; } = new();
    public Queue<TgSendOutcome> Outcomes { get; } = new();
    public List<(long Chat, long Message)> KeyboardsRemoved { get; } = new();
    public List<(string Id, string? Text)> Answers { get; } = new();
    private long _nextId = 1000;

    public Task<TgSendOutcome> SendAsync(TgSend msg, CancellationToken ct)
    {
        Sent.Add(msg);
        if (Outcomes.Count > 0) return Task.FromResult(Outcomes.Dequeue());
        return Task.FromResult(new TgSendOutcome(TgSendResult.Ok, ++_nextId, msg.Media is null ? null : "file-" + _nextId));
    }

    public Task<bool> EditMessageReplyMarkupAsync(long chatId, long messageId, object? replyMarkup, CancellationToken ct)
    {
        KeyboardsRemoved.Add((chatId, messageId));
        return Task.FromResult(true);
    }

    public Task AnswerCallbackAsync(string callbackId, string? text, CancellationToken ct)
    {
        Answers.Add((callbackId, text));
        return Task.CompletedTask;
    }
}

/// <summary>
/// CRM upgrade P2.4 — the outbox worker (<see cref="MessageDeliveryWorker"/>) with a fake sender and a controllable
/// clock: ok / blocked / 429 / failures / restart / expiry / BadMarkup / per-chat spacing / multi-part / campaign done.
/// </summary>
public class MessageDeliveryWorkerTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 10, 0, 0);

    private sealed class Rig : IDisposable
    {
        public TestDb Db { get; } = TestDb.Sqlite();
        public FakeTelegramSender Sender { get; } = new();
        public DateTime Now { get; set; } = T0;
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "wlc-msg-" + Guid.NewGuid().ToString("N"));
        public MessageAttachmentStore Store { get; }
        public MessageDeliveryWorker Worker { get; }

        public Rig()
        {
            Store = new MessageAttachmentStore(Folder);
            Worker = new MessageDeliveryWorker(new SingleServiceProvider(Db.Context), Sender, Store,
                NullLogger<MessageDeliveryWorker>.Instance)
            {
                Now = () => Now,
                Delay = (_, _) => Task.CompletedTask,
            };
        }

        public IAppDbContext Ctx => Db.Context;

        public MessageDelivery Add(long chat = 501, string text = "Salom", bool ack = false, string? broadcastId = null,
            string status = "pending", string? attachmentId = null)
        {
            var row = TelegramOutbox.Build(new OutboxItem(TelegramOutbox.SourceAuto, "attendance_absent", chat, text,
                StudentId: "s1", AckRequired: ack, BroadcastId: broadcastId, AttachmentId: attachmentId), Now);
            row.Status = status;
            Ctx.MessageDeliveries.Add(row);
            Db.Context.SaveChanges();
            return row;
        }

        public MessageDelivery Reload(string id)
        {
            Db.Context.ChangeTracker.Clear();
            return Ctx.MessageDeliveries.AsNoTracking().Single(d => d.Id == id);
        }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(Folder, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public async Task Ok_sent_va_message_id()
    {
        using var r = new Rig();
        var row = r.Add(ack: true);
        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        var d = r.Reload(row.Id);
        Assert.Equal("sent", d.Status);
        Assert.NotNull(d.SentAt);
        Assert.NotNull(d.TelegramMessageId);
        var msg = Assert.Single(r.Sender.Sent);
        Assert.True(msg.ParseHtml);
        Assert.NotNull(msg.ReplyMarkup);   // «Qabul qildim» button
    }

    [Fact]
    public async Task Blocked_holat_va_chatning_HAMMA_ulanishlari_belgilanadi()
    {
        using var r = new Rig();
        r.Ctx.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = "s1", ChatId = 501 },
            new TelegramRegistration { StudentId = "s2", ChatId = 501 },
            new TelegramRegistration { StudentId = "s3", ChatId = 777 });
        r.Db.Context.SaveChanges();
        var row = r.Add();
        r.Sender.Outcomes.Enqueue(new TgSendOutcome(TgSendResult.Blocked, Description: "bot was blocked by the user"));

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal("blocked", r.Reload(row.Id).Status);
        var regs = r.Ctx.TelegramRegistrations.AsNoTracking().ToList();
        Assert.All(regs.Where(x => x.ChatId == 501), x => Assert.NotNull(x.BlockedAt));
        Assert.Null(regs.Single(x => x.ChatId == 777).BlockedAt);
    }

    [Fact]
    public async Task RateLimit_pendingga_qaytadi_retry_after_bilan()
    {
        using var r = new Rig();
        var row = r.Add();
        r.Sender.Outcomes.Enqueue(new TgSendOutcome(TgSendResult.RateLimited, RetryAfterSeconds: 7));

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        var d = r.Reload(row.Id);
        Assert.Equal("pending", d.Status);
        Assert.Equal(T0.AddSeconds(7), d.NextAttemptAt);
        Assert.Equal(0, d.Attempts);
    }

    [Fact]
    public async Task Uch_marta_xato_bolsa_failed()
    {
        using var r = new Rig();
        var row = r.Add();
        for (var i = 0; i < 3; i++) r.Sender.Outcomes.Enqueue(new TgSendOutcome(TgSendResult.Failed, Description: "timeout"));

        await r.Worker.ProcessBatchAsync(CancellationToken.None);
        var d = r.Reload(row.Id);
        Assert.Equal("pending", d.Status);
        Assert.Equal(1, d.Attempts);
        Assert.Equal(T0.AddMinutes(1), d.NextAttemptAt);

        r.Now = T0.AddMinutes(2);
        await r.Worker.ProcessBatchAsync(CancellationToken.None);
        d = r.Reload(row.Id);
        Assert.Equal(2, d.Attempts);
        Assert.Equal(r.Now.AddMinutes(5), d.NextAttemptAt);

        r.Now = T0.AddMinutes(10);
        await r.Worker.ProcessBatchAsync(CancellationToken.None);
        d = r.Reload(row.Id);
        Assert.Equal("failed", d.Status);
        Assert.Equal(3, d.Attempts);
        Assert.Equal("timeout", d.Error);
    }

    [Fact]
    public async Task Restartda_sending_unknown_bolib_QAYTA_yuborilmaydi_pending_davom_etadi()
    {
        using var r = new Rig();
        var stuck = r.Add(status: "sending");
        var waiting = r.Add(chat: 900);

        await r.Worker.RecoverAsync(CancellationToken.None);
        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal("unknown", r.Reload(stuck.Id).Status);
        Assert.Equal("sent", r.Reload(waiting.Id).Status);
        Assert.Single(r.Sender.Sent);
        Assert.Equal(900, r.Sender.Sent[0].ChatId);
    }

    [Fact]
    public async Task Muddati_otgan_yuborilmaydi()
    {
        using var r = new Rig();
        var row = r.Add();
        r.Now = T0.AddHours(7);   // auto TTL 6 h

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal("expired", r.Reload(row.Id).Status);
        Assert.Empty(r.Sender.Sent);
    }

    [Fact]
    public async Task BadMarkup_bir_marta_oddiy_matn_bilan_qayta_yuboriladi()
    {
        using var r = new Rig();
        var row = r.Add(text: "<b>Salom</b> &amp; xayr");
        r.Sender.Outcomes.Enqueue(new TgSendOutcome(TgSendResult.BadMarkup, Description: "can't parse entities"));

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal("sent", r.Reload(row.Id).Status);
        Assert.Equal(2, r.Sender.Sent.Count);
        Assert.False(r.Sender.Sent[1].ParseHtml);
        Assert.Equal("Salom & xayr", r.Sender.Sent[1].HtmlText);
    }

    [Fact]
    public async Task Bitta_chatga_1_1_soniya_oraliq()
    {
        using var r = new Rig();
        var first = r.Add(chat: 501);
        var second = r.Add(chat: 501);

        await r.Worker.ProcessBatchAsync(CancellationToken.None);
        Assert.Single(r.Sender.Sent);
        var d2 = r.Reload(second.Id);
        Assert.Equal("pending", d2.Status);
        Assert.Equal(T0 + MessageDeliveryWorker.ChatSpacing, d2.NextAttemptAt);

        r.Now = T0.AddSeconds(2);
        await r.Worker.ProcessBatchAsync(CancellationToken.None);
        Assert.Equal(2, r.Sender.Sent.Count);
        Assert.Equal("sent", r.Reload(first.Id).Status);
        Assert.Equal("sent", r.Reload(second.Id).Status);
    }

    [Fact]
    public async Task Kampaniya_tugagach_done_boladi()
    {
        using var r = new Rig();
        var bc = new Broadcast { ClassName = "Faol", Status = "queued", Kind = "broadcast" };
        r.Ctx.Broadcasts.Add(bc);
        r.Db.Context.SaveChanges();
        r.Add(chat: 1, broadcastId: bc.Id);
        r.Add(chat: 2, broadcastId: bc.Id);

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        r.Db.Context.ChangeTracker.Clear();
        var b = r.Ctx.Broadcasts.AsNoTracking().Single();
        Assert.Equal("done", b.Status);
        Assert.NotNull(b.FinishedAt);
    }

    [Fact]
    public async Task Uzun_matn_rasm_bilan_ikki_xabar_tugma_oxirgisida_file_id_saqlanadi()
    {
        using var r = new Rig();
        var (att, url) = await SavePhoto(r);
        var row = r.Add(text: new string('x', 1500), ack: true, attachmentId: att.Id);

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(2, r.Sender.Sent.Count);
        Assert.NotNull(r.Sender.Sent[0].Media);
        Assert.Equal("", r.Sender.Sent[0].HtmlText);   // media without caption
        Assert.Null(r.Sender.Sent[0].ReplyMarkup);
        Assert.Null(r.Sender.Sent[1].Media);
        Assert.NotNull(r.Sender.Sent[1].ReplyMarkup);    // button on the LAST message
        var d = r.Reload(row.Id);
        Assert.Equal("sent", d.Status);
        Assert.Equal(r.Sender.Sent[1].ChatId, r.Sender.Sent[0].ChatId);
        Assert.NotEqual("", r.Ctx.MessageAttachments.AsNoTracking().Single().TelegramFileId);
        Assert.True(r.Store.Exists(url));
    }

    [Fact]
    public async Task Qisqa_matn_rasm_izohi_bolib_ketadi_va_keyingisi_file_id_ishlatadi()
    {
        using var r = new Rig();
        var (att, _) = await SavePhoto(r);
        r.Add(chat: 1, text: "Izoh", attachmentId: att.Id);
        r.Add(chat: 2, text: "Izoh", attachmentId: att.Id);

        await r.Worker.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(2, r.Sender.Sent.Count);
        Assert.Equal("Izoh", r.Sender.Sent[0].HtmlText);
        Assert.Null(r.Sender.Sent[0].Media!.FileId);            // first upload streams the file
        Assert.NotNull(r.Sender.Sent[1].Media!.FileId);         // then Telegram's file_id is reused
    }

    [Fact]
    public async Task Eski_yuklangan_biriktirma_fayli_tozalanadi_navbatdagisi_va_yutuq_rasmi_qoladi()
    {
        using var r = new Rig();
        var (old, oldUrl) = await SavePhoto(r);
        var (kept, keptUrl) = await SavePhoto(r);
        var (achPhoto, achUrl) = await SavePhoto(r);
        foreach (var a in r.Ctx.MessageAttachments)
        {
            a.CreatedAt = T0.AddDays(-61);
            a.TelegramFileId = "file-x";
        }
        r.Ctx.StudentAchievements.Add(new StudentAchievement { StudentId = "s1", Title = "IELTS", Date = "2026-01-01", AttachmentId = achPhoto.Id });
        r.Db.Context.SaveChanges();
        r.Add(attachmentId: kept.Id);   // still queued

        await r.Worker.MaintainAsync(CancellationToken.None);

        Assert.False(r.Store.Exists(oldUrl));
        Assert.True(r.Store.Exists(keptUrl));
        Assert.True(r.Store.Exists(achUrl));
        r.Db.Context.ChangeTracker.Clear();
        Assert.NotNull(r.Ctx.MessageAttachments.AsNoTracking().Single(a => a.Id == old.Id).FileDeletedAt);
    }

    [Fact]
    public async Task Log_180_kundan_eskisi_ochiriladi()
    {
        using var r = new Rig();
        var old = r.Add(status: "sent");
        old.CreatedAt = T0.AddDays(-181);
        r.Db.Context.SaveChanges();
        var fresh = r.Add(status: "sent");

        await r.Worker.MaintainAsync(CancellationToken.None);

        r.Db.Context.ChangeTracker.Clear();
        Assert.False(r.Ctx.MessageDeliveries.Any(d => d.Id == old.Id));
        Assert.True(r.Ctx.MessageDeliveries.Any(d => d.Id == fresh.Id));
    }

    [Fact]
    public async Task Bot_sozlanmaganda_qolib_ketgan_pending_kunlik_xizmatda_expired()
    {
        using var r = new Rig();
        var bc = new Broadcast { ClassName = "Faol", Status = "queued", Kind = "broadcast" };
        r.Ctx.Broadcasts.Add(bc);
        r.Db.Context.SaveChanges();
        var row = r.Add(broadcastId: bc.Id);
        r.Now = T0.AddDays(1);

        await r.Worker.MaintainAsync(CancellationToken.None);

        Assert.Equal("expired", r.Reload(row.Id).Status);
        Assert.Equal("done", r.Ctx.Broadcasts.AsNoTracking().Single().Status);
        Assert.Empty(r.Sender.Sent);
    }

    private static async Task<(MessageAttachment Attachment, string Url)> SavePhoto(Rig r)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6 };
        using var ms = new MemoryStream(bytes);
        var (a, error) = await MessageAttachments.CreateAsync(r.Ctx, r.Store, ms, "rasm.jpg", "image/jpeg", bytes.Length,
            "u1", "Admin");
        Assert.Null(error);
        r.Db.Context.SaveChanges();
        return (a!, a!.FileUrl);
    }
}
