using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// AUDIT T9 — Instagram (DM agenti, inbox, kontent joylash), Meta Lead Ads / ROI va kunlik AI
/// rejalashtiruvchisi bo'yicha tuzatishlarning REGRESSIYA testlari.
///
/// <para>Har test bitta audit bandini qulflaydi (IG-1 … IG-10, CRM-8, past darajali bandlar).
/// Sof funksiyalar alohida, oqim (pipeline, navbat tozalanishi, publish xizmati) esa SQLite
/// bazada uchdan-uchgacha tekshiriladi.</para>
/// </summary>
public class AuditFixT9Tests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";

    private static DateTime Now => AppClock.Now;

    /// <summary>Webhook vaqti (unix soniya) — <c>InstagramPipelineTests</c> dagi bilan bir xil hisob.</summary>
    private static long UnixAt(DateTime local) =>
        new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string DmJson(string text, string mid, DateTime at) => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{UnixAt(at)}}, "messaging": [{
            "sender": { "id": "{{ClientId}}" },
            "recipient": { "id": "{{OurId}}" },
            "timestamp": {{UnixAt(at) * 1000}},
            "message": { "mid": "{{mid}}", "text": "{{text}}" } }]}]}
        """;

    private static string PostbackJson(string payload, string mid) => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{UnixAt(Now)}}, "messaging": [{
            "sender": { "id": "{{ClientId}}" },
            "recipient": { "id": "{{OurId}}" },
            "timestamp": {{UnixAt(Now) * 1000}},
            "postback": { "mid": "{{mid}}", "title": "Narxlar qancha?", "payload": "{{payload}}" } }]}]}
        """;

    private static CenterMeta Meta(int limit = 200, bool leadAds = false) => new()
    {
        Name = "Wunderkind",
        InstagramEnabled = true,
        InstagramAutoReplyDm = true,
        InstagramAutoReplyComments = true,
        InstagramReplyDelaySeconds = 0,
        InstagramDailyReplyLimit = limit,
        InstagramNotifyTelegram = false,
        InstagramLeadAdsEnabled = leadAds,
    };

    private static IgAccount Account() => new()
    {
        IgUserId = OurId, Username = "wunderkind", AccessToken = "test-token",
        IsActive = true, ConnectedAt = AppClock.Iso(),
    };

    private static IgAutoRule PriceRule() => new()
    {
        Title = "Narx", Keywords = "narx,qancha", Channel = "any",
        ReplyText = "Narxlar: IELTS — 700 000 so'm/oy.", StopAi = true, IsActive = true,
    };

    private static IgWebhookEvent Event(string raw, DateTime? receivedAt = null) => new()
    {
        EventKey = "t9:" + Guid.NewGuid().ToString("N"),
        RawJson = raw,
        Status = IgConst.EvPending,
        ReceivedAt = (receivedAt ?? Now).ToString("yyyy-MM-ddTHH:mm:ss"),
    };

    private static List<string> Sends(RecordingHandler h) =>
        h.Requests.Where(r => r.StartsWith("POST", StringComparison.Ordinal)).ToList();

    private static async Task<IgWebhookEvent> RunAsync(TestDb db, IgWebhookEvent ev, RecordingHandler handler)
    {
        var api = new InstagramApi(new HttpClient(handler), NullLogger<InstagramApi>.Instance);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var pipeline = new InstagramPipeline(
            new InstagramServiceProvider(db.Context, api, telegram), NullLogger<InstagramPipeline>.Instance);
        await pipeline.ProcessAsync(ev.Id, CancellationToken.None);
        return db.Context.IgWebhookEvents.AsNoTracking().Single(e => e.Id == ev.Id);
    }

    // =============================================================================================
    //  IG-1 — ESKI HODISA: saqlanadi, lekin AVTOJAVOB YO'Q
    // =============================================================================================

    [Fact]
    public void Eski_hodisa_qoidasi_DM_24_soat_izoh_3_kun()
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0);
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");

        Assert.Equal("", InstagramContract.StaleReplyReason(IgConst.ChannelDm, Iso(now.AddHours(-2)), "", now));
        Assert.NotEqual("", InstagramContract.StaleReplyReason(IgConst.ChannelDm, Iso(now.AddHours(-25)), "", now));
        // Izohda oyna kengroq — 30 soatlik izohga hali javob beriladi, 4 kunlikka emas.
        Assert.Equal("", InstagramContract.StaleReplyReason(IgConst.ChannelComment, Iso(now.AddHours(-30)), "", now));
        Assert.NotEqual("", InstagramContract.StaleReplyReason(IgConst.ChannelComment, Iso(now.AddDays(-4)), "", now));
        // Meta 36 soat QAYTA yuborgan eski xabar: qabul vaqti yangi, xabarning o'zi eski → eski.
        Assert.NotEqual("", InstagramContract.StaleReplyReason(IgConst.ChannelDm, Iso(now), Iso(now.AddHours(-30)), now));
        // Buzuq Meta vaqti (kelajak / 30 kundan eski) HISOBGA OLINMAYDI — bot sababsiz jim qolmasin.
        Assert.Equal("", InstagramContract.StaleReplyReason(IgConst.ChannelDm, Iso(now), Iso(now.AddDays(5)), now));
        Assert.Equal("", InstagramContract.StaleReplyReason(IgConst.ChannelDm, Iso(now), Iso(now.AddDays(-60)), now));
    }

    /// <summary>🔴 Modul o'chiq paytda to'plangan DM — modul yoqilgach qayta ishlanadi, lekin
    /// bot unga JAVOB YOZMAYDI: xabar tarixga tushadi, navbat yozuvi sababi bilan `skipped`.</summary>
    [Fact]
    public async Task Eski_DM_avtojavobsiz_saqlanadi_va_skipped_boladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(PriceRule());
        var old = Now.AddDays(-3);
        var ev = Event(DmJson("Narxi qancha?", "old-mid-1", old), receivedAt: old);
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        var done = await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));                                         // javob KETMADI
        Assert.Equal(IgConst.EvSkipped, done.Status);
        Assert.Contains("soat oldin", done.Error);
        Assert.Single(db.Context.IgMessages.Where(m => m.Direction == IgConst.DirIn).ToList());   // tarix bor
        Assert.DoesNotContain(db.Context.IgMessages, m => m.Direction == IgConst.DirOut);
        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);                                      // Inbox'da ko'rinadi
        Assert.Equal(0, db.Context.IgAutoRules.Single().MatchCount);
        // 24 soatlik oyna — eski xabar vaqtidan (yangi deb ko'rinmasin).
        Assert.False(InstagramContract.DmWindowOpen(conv.LastInboundAt, Now));
    }

    /// <summary>Eski xabarda telefon bo'lsa LID baribir yoziladi — javob emas, lid muhim.</summary>
    [Fact]
    public async Task Eski_DM_dagi_telefon_baribir_lidga_tushadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var old = Now.AddDays(-2);
        var ev = Event(DmJson("Ali Valiyev 90 123 45 67", "old-mid-2", old), receivedAt: old);
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));
        var lead = Assert.Single(db.Context.Leads);
        Assert.Equal(lead.Id, db.Context.IgConversations.Single().LeadId);
    }

    /// <summary>Navbat tozalanishi 30 kundan eski <c>pending</c> ni ham o'chiradi; reklama lidi esa
    /// avval <c>IgAdLead</c> ga xato bilan ko'chiriladi (pullik lid yo'qolmasin).</summary>
    [Fact]
    public async Task Tozalash_eski_pending_ni_ochiradi_reklama_lidini_saqlab_qoladi()
    {
        using var db = TestDb.Sqlite();
        var old = Now.AddDays(-31);
        var oldDm = Event(DmJson("Salom", "very-old", old), receivedAt: old);
        var oldLeadgen = Event("""
            {"object":"page","entry":[{"id":"page-1","time":1755600000,"changes":[
              {"field":"leadgen","value":{"leadgen_id":"lg-old","page_id":"page-1",
               "form_id":"form-1","ad_id":"ad-1","created_time":1755600000}}]}]}
            """, receivedAt: old);
        var fresh = Event(DmJson("Salom", "fresh", Now));
        db.Context.IgWebhookEvents.AddRange(oldDm, oldLeadgen, fresh);
        await db.Context.SaveChangesAsync();

        var removed = await InstagramWorkerService.CleanupAsync(db.Context, CancellationToken.None);

        Assert.Equal(2, removed);
        var left = Assert.Single(db.Context.IgWebhookEvents.AsNoTracking().ToList());
        Assert.Equal(fresh.Id, left.Id);
        var adLead = Assert.Single(db.Context.IgAdLeads);
        Assert.Equal("lg-old", adLead.LeadgenId);
        Assert.Equal("", adLead.LeadId);
        Assert.Contains("Qayta olish", adLead.Error);
    }

    [Fact]
    public void Signal_oynada_bir_marta()
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0);
        var window = TimeSpan.FromMinutes(10);
        Assert.True(InstagramContract.AlertDue(null, now, window));
        Assert.False(InstagramContract.AlertDue(now.AddMinutes(-3), now, window));
        Assert.True(InstagramContract.AlertDue(now.AddMinutes(-11), now, window));
    }

    // =============================================================================================
    //  IG-2 — reklama lidi modul o'chiq paytda ham TIKLANADIGAN holda saqlanadi
    // =============================================================================================

    /// <summary>🔴 AI agenti yoqilgan, «Reklama lidlari» o'chiq: lid qatori xato bilan SAQLANADI
    /// (tashqariga so'rov yo'q), takroriy hodisa ikkinchi qator ochmaydi.</summary>
    [Fact]
    public async Task Modul_ochiq_paytdagi_reklama_lidi_qayta_olish_uchun_saqlanadi()
    {
        using var db = TestDb.Sqlite();
        var handler = new RecordingHandler();
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var svc = new MetaLeadgenService(
            db.Context, new MetaAdsApi(new HttpClient(handler), NullLogger<MetaAdsApi>.Instance),
            telegram, NullLogger<MetaLeadgenService>.Instance);
        var meta = Meta(leadAds: false);
        var events = new List<IgLeadgenEvent>
        {
            new("lg-1", "page-1", "form-1", "ad-1", "as-1", "2026-10-06T10:00:00", "leadgen:lg-1"),
        };

        var problems = await svc.HandleAsync(events, meta, CancellationToken.None);
        await svc.HandleAsync(events, meta, CancellationToken.None);   // takror

        Assert.Empty(handler.Requests);
        Assert.Contains(problems, p => p.Contains("o'chirilgan"));
        var row = Assert.Single(db.Context.IgAdLeads);
        Assert.Equal("lg-1", row.LeadgenId);
        Assert.Equal("form-1", row.FormId);
        Assert.Equal("ad-1", row.AdId);
        Assert.Equal("", row.LeadId);                                  // «Qayta olish» ko'rsatiladi
        Assert.Equal(MetaLeadgenService.ModuleOffError, row.Error);
    }

    // =============================================================================================
    //  IG-3 — yopilgan suhbat yangi xabarda QAYTA OCHILADI
    // =============================================================================================

    [Fact]
    public void Yopilgan_suhbat_qayta_ochiladi_operatorniki_tegilmaydi()
    {
        var closed = new IgConversation { Status = IgConst.StatusClosed };
        Assert.True(InstagramContract.ReopenOnInbound(closed));
        Assert.Equal(IgConst.StatusBot, closed.Status);

        var op = new IgConversation { Status = IgConst.StatusOperator };
        Assert.False(InstagramContract.ReopenOnInbound(op));
        Assert.Equal(IgConst.StatusOperator, op.Status);
    }

    /// <summary>🔴 Qaytib kelgan mijoz yopilgan suhbatga yozdi — bot javob beradi va suhbat
    /// ro'yxat/sanoqlarda yana ko'rinadi (ilgari abadiy jim edi).</summary>
    [Fact]
    public async Task Yopilgan_suhbatga_yangi_xabar_javob_oladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(PriceRule());
        db.Context.IgConversations.Add(new IgConversation
        {
            IgUserId = ClientId, Username = "ali", Status = IgConst.StatusClosed,
            Unread = false, CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson("Narxi qancha?", "mid-closed", Now));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Single(Sends(handler));
        var conv = db.Context.IgConversations.AsNoTracking().Single();
        Assert.Equal(IgConst.StatusBot, conv.Status);
        Assert.True(conv.Unread);
    }

    // =============================================================================================
    //  IG-5 — bekor qilingan token: post NAVBATDA kutadi
    // =============================================================================================

    [Fact]
    public void Ulanish_xatosi_kodlari()
    {
        Assert.True(InstagramPublishContract.IsConnectionError(190));
        Assert.True(InstagramPublishContract.IsConnectionError(10));
        Assert.True(InstagramPublishContract.IsConnectionError(200));
        Assert.True(InstagramPublishContract.IsConnectionError(299));
        Assert.False(InstagramPublishContract.IsConnectionError(100));
        Assert.False(InstagramPublishContract.IsConnectionError(4));
        Assert.False(InstagramPublishContract.IsConnectionError(0));
    }

    /// <summary>🔴 OAuthException 190 (token bekor qilingan, muddati o'tmagan): post `failed`
    /// bo'lmaydi, urinish oshmaydi, sabab ko'rinadi va keyingi tsikllarda Meta'ga bekorga
    /// so'rov ketmaydi (blok).</summary>
    [Fact]
    public async Task Token_190_post_scheduled_qoladi_urinish_oshmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { InstagramPublishEnabled = true, InstagramNotifyTelegram = false });
        db.Context.IgAccounts.Add(new IgAccount
        {
            IgUserId = "ig-1", AccessToken = "tok", IsActive = true,
            TokenExpiresAt = Now.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ss"),   // muddati O'TMAGAN
            ConnectedAt = AppClock.Iso(),
        });
        var post = new IgScheduledPost
        {
            PostType = IgPublishConst.TypeImage, Caption = "Yangi guruh!",
            MediaJson = """[{"url":"https://cdn.example.com/rasm.jpg","kind":"image"}]""",
            OptionsJson = "", Status = IgPublishConst.StScheduled, CreatedBy = "Admin",
            ScheduledAt = Now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss"), CreatedAt = AppClock.Iso(),
        };
        db.Context.IgScheduledPosts.Add(post);
        await db.Context.SaveChangesAsync();

        InstagramPublishService.ResetPollState();
        var handler = new IgPublishFakeHandler
        {
            CreateStatus = HttpStatusCode.BadRequest,
            CreateBody = """{"error":{"message":"Error validating access token: The user has not authorized application.","type":"OAuthException","code":190,"error_subcode":460}}""",
        };
        var api = new InstagramPublishApi(new HttpClient(handler), NullLogger<InstagramPublishApi>.Instance);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var svc = new InstagramPublishService(db.Context, api, telegram, NullLogger<InstagramPublishService>.Instance);

        try
        {
            for (var i = 0; i < IgPublishConst.MaxAttempts + 1; i++)
                await svc.ProcessDueAsync(CancellationToken.None);

            var saved = await db.Context.IgScheduledPosts.AsNoTracking().FirstAsync(p => p.Id == post.Id);
            Assert.Equal(IgPublishConst.StScheduled, saved.Status);
            Assert.Equal(0, saved.Attempts);
            Assert.StartsWith(InstagramPublishService.ConnectionPrefix, saved.Error);
            Assert.Equal("", saved.ContainerId);
            // Blok: birinchi tsikldan keyin Meta'ga qayta-qayta so'rov KETMAYDI.
            Assert.Equal(1, handler.CreateCalls);
        }
        finally { InstagramPublishService.ResetPollState(); }
    }

    // =============================================================================================
    //  IG-7 — bot oshkorligi HAR yo'lda (FAQ · qoida · AI)
    // =============================================================================================

    [Fact]
    public void Oshkorlik_faqat_birinchi_javobda_va_takrorlanmaydi()
    {
        const string reply = "Narxlar: 700 000 so'm.";
        Assert.Equal(IgConst.DefaultGreeting + "\n\n" + reply, InstagramContract.WithDisclosure(reply, "", true));
        Assert.Equal("Men botman\n\n" + reply, InstagramContract.WithDisclosure(reply, " Men botman ", true));
        Assert.Equal(reply, InstagramContract.WithDisclosure(reply, "", false));
        // Model allaqachon qo'shgan bo'lsa — ikki marta emas.
        var withHello = IgConst.DefaultGreeting + " " + reply;
        Assert.Equal(withHello, InstagramContract.WithDisclosure(withHello, "", true));
    }

    /// <summary>🔴 FAQ tugmasi javobi AI'siz ketadi — ilgari bot oshkorligisiz yuborilardi.
    /// Birinchi javob oshkorlik bilan, keyingisi esa usiz.</summary>
    [Fact]
    public async Task FAQ_birinchi_javobida_oshkorlik_bor_keyingisida_yoq()
    {
        using var db = TestDb.Sqlite();
        var meta = Meta();
        meta.InstagramGreeting = "Salom! Men Wunderkind AI yordamchisiman.";
        db.Context.CenterMeta.Add(meta);
        db.Context.IgAccounts.Add(Account());
        var faq = new IgIceBreaker
        {
            Question = "Narxlar qancha?", Answer = "Narxlar: IELTS — 700 000 so'm/oy.",
            IsActive = true, CreatedAt = AppClock.Iso(), UpdatedAt = AppClock.Iso(),
        };
        db.Context.IgIceBreakers.Add(faq);
        var ev1 = Event(PostbackJson(InstagramContract.FaqPayload(faq.Id), "pb-a"));
        db.Context.IgWebhookEvents.Add(ev1);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev1, handler);

        var first = db.Context.IgMessages.AsNoTracking().Single(m => m.Direction == IgConst.DirOut);
        Assert.StartsWith("Salom! Men Wunderkind AI yordamchisiman.", first.Text);
        Assert.EndsWith(faq.Answer, first.Text);

        var ev2 = Event(PostbackJson(InstagramContract.FaqPayload(faq.Id), "pb-b"));
        db.Context.IgWebhookEvents.Add(ev2);
        await db.Context.SaveChangesAsync();
        await RunAsync(db, ev2, handler);

        var outs = db.Context.IgMessages.AsNoTracking()
            .Where(m => m.Direction == IgConst.DirOut).OrderBy(m => m.CreatedAt).ToList();
        Assert.Equal(2, outs.Count);
        Assert.Single(outs, m => m.Text == faq.Answer);                     // ikkinchisi oshkorliksiz
    }

    // =============================================================================================
    //  IG-8 — kunlik chegara va halqa o'chirgichi FAQAT bot javoblarini sanaydi
    // =============================================================================================

    [Fact]
    public void Bot_qatori_operator_qatoridan_ajratiladi()
    {
        Assert.True(InstagramContract.IsBotOutbound(true, "AI agent"));
        Assert.True(InstagramContract.IsBotOutbound(false, IgConst.RuleActorPrefix + "Narx"));
        Assert.True(InstagramContract.IsBotOutbound(false, IgConst.ActorRule));
        Assert.True(InstagramContract.IsBotOutbound(false, IgConst.ActorFaq));
        Assert.False(InstagramContract.IsBotOutbound(false, "admin"));
        Assert.False(InstagramContract.IsBotOutbound(false, IgConst.ActorOperatorIg));
        Assert.False(InstagramContract.IsBotOutbound(false, ""));
    }

    /// <summary>🔴 Operator bugun ko'p yozgan (Inbox va Instagram ilovasidan) — bu botning
    /// kunlik chegarasini yemaydi: bot javobi baribir yuboriladi.</summary>
    [Fact]
    public async Task Operator_javoblari_bot_chegarasiga_kirmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(limit: 1));
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(PriceRule());
        db.Context.IgMessages.AddRange(
            new IgMessage
            {
                ConversationId = "boshqa", Direction = IgConst.DirOut, Channel = IgConst.ChannelDm,
                Text = "Operator javobi", ActorName = "admin", IsAi = false, CreatedAt = AppClock.Iso(),
            },
            new IgMessage
            {
                ConversationId = "boshqa", Direction = IgConst.DirOut, Channel = IgConst.ChannelDm,
                Text = "Ilovadan", ActorName = IgConst.ActorOperatorIg, IsAi = false, CreatedAt = AppClock.Iso(),
            });
        var ev = Event(DmJson("Narxi qancha?", "mid-limit", Now));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Single(Sends(handler));
        Assert.False(db.Context.IgConversations.Single().NeedsOperator);
    }

    // =============================================================================================
    //  IG-10 — «Click-to-Direct» reklamasi ROI va kanal tahliliga yetib boradi
    // =============================================================================================

    private static (Lead Lead, IgConversation Conv) SeedDmAdLead(TestDb db, string adId, string created)
    {
        var lead = new Lead { FullName = "Ali", Phone = "+998901112233", Source = "Instagram", CreatedAt = created };
        db.Context.Leads.Add(lead);
        var conv = new IgConversation { IgUserId = "u-ad", Username = "ali", LeadId = lead.Id, CreatedAt = created };
        db.Context.IgConversations.Add(conv);
        db.Context.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id, Direction = IgConst.DirIn, Channel = IgConst.ChannelDm,
            Text = "Salom", AdId = adId, CreatedAt = created,
        });
        return (lead, conv);
    }

    [Fact]
    public async Task Click_to_DM_lidi_kanal_tahlilida_reklama_bolib_sanaladi()
    {
        using var db = TestDb.Sqlite();
        SeedDmAdLead(db, "ad-1", "2026-08-05T10:00:00");
        // Izohdagi TAXMINIY atributsiya — aniq kanalga KIRMAYDI.
        var organic = new Lead { FullName = "Vali", Phone = "+998907654321", Source = "Instagram", CreatedAt = "2026-08-05T11:00:00" };
        db.Context.Leads.Add(organic);
        var c2 = new IgConversation { IgUserId = "u-c", Username = "vali", LeadId = organic.Id, CreatedAt = "2026-08-05T11:00:00" };
        db.Context.IgConversations.Add(c2);
        db.Context.IgMessages.Add(new IgMessage
        {
            ConversationId = c2.Id, Direction = IgConst.DirIn, Channel = IgConst.ChannelComment,
            Text = "Narxi?", AdId = "ad-9", CreatedAt = "2026-08-05T11:00:00",
        });
        await db.Context.SaveChangesAsync();

        var overview = await LeadCrmOverview.BuildAsync(db.Context);

        Assert.Equal(1, overview.Origins.Single(o => o.Key == LeadOrigins.Ads).Leads);
        Assert.Equal(1, overview.Origins.Single(o => o.Key == LeadOrigins.Instagram).Leads);
    }

    [Fact]
    public async Task Click_to_DM_lidi_ROI_hisobotida_kampaniyaga_yoziladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgAdAccounts.Add(new IgAdAccount
        {
            AdAccountId = "act_1", Name = "Markaz", Currency = "UZS", CurrencyOffset = 2,
            TimezoneName = "Asia/Tashkent", AccessToken = "t", IsActive = true, ConnectedAt = "2026-07-01T09:00:00",
        });
        db.Context.IgAdEntities.AddRange(
            new IgAdEntity { AdAccountId = "act_1", Level = MetaAdsRoi.LevelCampaign, ExternalId = "c1", Name = "DM kampaniya" },
            new IgAdEntity { AdAccountId = "act_1", Level = MetaAdsRoi.LevelAdset, ExternalId = "as1", ParentId = "c1", Name = "To'plam" },
            new IgAdEntity { AdAccountId = "act_1", Level = MetaAdsRoi.LevelAd, ExternalId = "ad-1", ParentId = "as1", Name = "Kreativ" });
        db.Context.IgAdInsights.Add(new IgAdInsight
        {
            AdAccountId = "act_1", Level = MetaAdsRoi.LevelAd, ExternalId = "ad-1", StatDate = "2026-08-05",
            Platform = MetaAdsRoi.PlatformInstagram, SpendMinor = 10_000_000, Impressions = 1000, MsgStarted = 3,
            FetchedAt = "2026-08-20T05:00:00",
        });
        SeedDmAdLead(db, "ad-1", "2026-08-05T10:00:00");
        await db.Context.SaveChangesAsync();

        var report = await MetaAdsRoi.BuildAsync(db.Context, "2026-08-01", "2026-08-31", null, null);

        Assert.Equal(1, report.Totals.CrmLeads);
        var campaign = Assert.Single(report.Campaigns);
        Assert.Equal("c1", campaign.Id);
        Assert.Equal(1, campaign.CrmLeads);
        Assert.NotNull(campaign.CplMinor);
        Assert.Contains(report.Notes, n => n.Contains("Click-to-Direct"));
    }

    // =============================================================================================
    //  Past darajali bandlar
    // =============================================================================================

    /// <summary>(a) Xato bilan qolgan / bekor qilingan post TAHRIRLANADI (navbatga qaytadi);
    /// joylangan va joylanayotgan — yo'q.</summary>
    [Fact]
    public void Tahrirlanadigan_post_holatlari()
    {
        Assert.True(InstagramPublishContract.IsEditableStatus(IgPublishConst.StScheduled));
        Assert.True(InstagramPublishContract.IsEditableStatus(IgPublishConst.StFailed));
        Assert.True(InstagramPublishContract.IsEditableStatus(IgPublishConst.StCancelled));
        Assert.False(InstagramPublishContract.IsEditableStatus(IgPublishConst.StProcessing));
        Assert.False(InstagramPublishContract.IsEditableStatus(IgPublishConst.StPublished));
        Assert.False(InstagramPublishContract.IsEditableStatus("buzuq"));
    }

    /// <summary>(f) Kunlik markaz AI tahlili belgilangan soatdan KEYIN kun oxirigacha qayta
    /// urinadi (ilgari faqat 10 daqiqalik oynada, bitta urinish).</summary>
    [Fact]
    public void Kunlik_AI_tahlil_muvaffaqiyatgacha_qayta_urinadi()
    {
        var at = new DateTime(2026, 10, 7, 8, 0, 0);
        Assert.False(CenterAiSchedulerService.ShouldAttempt(at.AddMinutes(-1), 8, 0, DateTime.MinValue));
        Assert.True(CenterAiSchedulerService.ShouldAttempt(at, 8, 0, DateTime.MinValue));
        // Server 8:00 da qayta ishga tushayotgan edi — 10:30 da ham yaratiladi.
        Assert.True(CenterAiSchedulerService.ShouldAttempt(at.AddHours(2.5), 8, 0, DateTime.MinValue));
        // Muvaffaqiyatsiz urinishdan keyin — keyingi urinish vaqti kelguncha kutadi.
        Assert.False(CenterAiSchedulerService.ShouldAttempt(at.AddMinutes(10), 8, 1, at.AddMinutes(30)));
        Assert.True(CenterAiSchedulerService.ShouldAttempt(at.AddMinutes(31), 8, 1, at.AddMinutes(30)));
        // Kunlik chegara — kalit noto'g'ri bo'lsa kun bo'yi bekorga so'rov ketmasin.
        Assert.False(CenterAiSchedulerService.ShouldAttempt(at.AddHours(10), 8,
            CenterAiSchedulerService.MaxAttemptsPerDay, DateTime.MinValue));
    }
}
