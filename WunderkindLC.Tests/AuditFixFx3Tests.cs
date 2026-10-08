using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// POST-MERGE REVYU (FX3) — Instagram ↔ CRM bog'lanishi va kontent joylash qulfi bo'yicha
/// tuzatishlarning REGRESSIYA testlari.
///
/// <para>Qamrov: R2-1/R3-2 (qaytgan o'quvchining yangi lidi Telegram guruhiga tushadi, Inbox'da
/// «Lidga aylantirish» qaytadi) · R2-2 (suhbat yangi lidga ko'chganda Instagram/reklama
/// atributsiyasi pul to'lagan eski lidda QOLADI — <see cref="IgLeadLinks"/>) · R3-8 (navbat qulfi
/// kutishi cheklangan, 409) · R3-9 (bot oshkorligi ochiq izohga qo'shilmaydi) · R3-10 (agent
/// o'chiq bo'lsa qayta ulash webhook obunasini tiklamaydi).</para>
/// </summary>
public class AuditFixFx3Tests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";
    private const long GroupChat = -100777;

    private static long NowSeconds => new DateTimeOffset(AppClock.Now.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string DmJson(string text, string mid = "mid-fx3") => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}}, "messaging": [{
            "sender": { "id": "{{ClientId}}" },
            "recipient": { "id": "{{OurId}}" },
            "timestamp": {{NowSeconds * 1000}},
            "message": { "mid": "{{mid}}", "text": "{{text}}" } }]}]}
        """;

    private static string CommentJson(string text, string commentId = "c-fx3") => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}}, "changes": [{ "field": "comments",
            "value": { "id": "{{commentId}}", "text": "{{text}}",
                       "from": { "id": "{{ClientId}}", "username": "ali" },
                       "media": { "id": "m-fx3" } } }]}]}
        """;

    private static IgWebhookEvent Event(string raw) => new()
    {
        EventKey = "fx3:" + Guid.NewGuid().ToString("N"),
        RawJson = raw,
        Status = IgConst.EvPending,
        ReceivedAt = AppClock.Iso(),
    };

    private static IgAccount Account() => new()
    {
        IgUserId = OurId, Username = "wunderkind", AccessToken = "test-token",
        IsActive = true, ConnectedAt = AppClock.Iso(),
    };

    private static async Task RunAsync(TestDb db, IgWebhookEvent ev, RecordingHandler ig, TelegramService telegram)
    {
        var api = new InstagramApi(new HttpClient(ig), NullLogger<InstagramApi>.Instance);
        var pipeline = new InstagramPipeline(
            new InstagramServiceProvider(db.Context, api, telegram), NullLogger<InstagramPipeline>.Instance);
        await pipeline.ProcessAsync(ev.Id, CancellationToken.None);
    }

    /// <summary>O'quvchiga aylantirilgan lid + uning <c>convert</c> hodisasi.</summary>
    private static async Task<Lead> ConvertedLeadAsync(AppDbContext db, string convertedAt = "2026-03-01T12:00:00")
    {
        var student = new Student { FullName = "Aliyev Ali (o'quvchi)" };
        db.Students.Add(student);
        var lead = new Lead
        {
            FullName = "Aliyev Ali", Phone = "+998901234567", Source = "Instagram",
            ConvertedStudentId = student.Id, CreatedAt = "2026-02-01T10:00:00", ClosedAt = convertedAt[..10],
        };
        db.Leads.Add(lead);
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = lead.Id, Type = LeadAnalytics.TypeConvert, ActorName = "Menejer", CreatedAt = convertedAt,
            Text = "O'quvchiga aylantirildi",
        });
        await db.SaveChangesAsync();
        return lead;
    }

    private static IgAgentOutput Output(string contact = "", string name = "") =>
        new("", "uz-Latn", "buying_intent", 80, true, false, false, name, contact, "IELTS", "Yana yozdi");

    // =============================================================================================
    //  IgLeadLinks — sof qoidalar
    // =============================================================================================

    [Fact]
    public void Xabar_egasi_tarix_tugash_vaqti_boyicha_aniqlanadi()
    {
        var past = new List<string> { IgLeadLinks.Entry("L1", "2026-03-01T12:00:00") };

        // Tarix bo'sh — har doim joriy lid (eski xatti-harakat AYNAN).
        Assert.Equal("L2", IgLeadLinks.OwnerAt("L2", null, "2026-01-01T00:00:00"));
        Assert.Equal("L2", IgLeadLinks.OwnerAt("L2", [], "2026-09-01T00:00:00"));
        // Aylantirishdan OLDIN — eski lid; KEYIN (va aynan o'sha soniyada) — joriy lid.
        Assert.Equal("L1", IgLeadLinks.OwnerAt("L2", past, "2026-02-10T09:00:00"));
        Assert.Equal("L2", IgLeadLinks.OwnerAt("L2", past, "2026-03-01T12:00:00"));
        Assert.Equal("L2", IgLeadLinks.OwnerAt("L2", past, "2026-05-01T09:00:00"));
        // Joriy lid yo'q (o'chirilgan) — keyingi xabarning egasi yo'q.
        Assert.Equal("", IgLeadLinks.OwnerAt(null, past, "2026-05-01T09:00:00"));
        // Ikki qavat tarix — tugash vaqti tartibida.
        var two = new List<string> { IgLeadLinks.Entry("L2", "2026-06-01T00:00:00"), IgLeadLinks.Entry("L1", "2026-03-01T12:00:00") };
        Assert.Equal("L2", IgLeadLinks.OwnerAt("L3", two, "2026-04-01T00:00:00"));
        Assert.Equal("L3", IgLeadLinks.OwnerAt("L3", two, "2026-07-01T00:00:00"));
    }

    [Fact]
    public void Tarix_qatori_takrorlanmaydi_buzugi_o_qilmaydi()
    {
        var conv = new IgConversation();
        IgLeadLinks.Remember(conv, "L1", "2026-03-01T12:00:00");
        IgLeadLinks.Remember(conv, "L1", "2026-04-01T12:00:00");   // shu lid — ikkinchi qator YO'Q
        Assert.Equal(new[] { "L1|2026-03-01T12:00:00" }, conv.PastLeadIds);

        Assert.False(IgLeadLinks.TryParse("", out _, out _));
        Assert.False(IgLeadLinks.TryParse("|2026-01-01", out _, out _));
        Assert.False(IgLeadLinks.TryParse("L1|", out _, out _));
        Assert.Equal(new[] { "L2", "L1" }, IgLeadLinks.AllLeadIds("L2", ["L1|2026-03-01T12:00:00", "buzuq", "L2|2026-01-01T00:00:00"]).ToList());

        for (var i = 0; i < IgLeadLinks.MaxPastLeads + 5; i++) IgLeadLinks.Remember(conv, "X" + i, "2026-05-01T00:00:00");
        Assert.Equal(IgLeadLinks.MaxPastLeads, conv.PastLeadIds.Count);
    }

    [Fact]
    public void Reklama_xabari_xabar_KELGAN_paytdagi_lidga_yoziladi()
    {
        var past = new List<string> { "L1|2026-03-01T12:00:00" };
        var rows = IgDmAdLeads.Attribute(
        [
            new IgDmAdLeads.AdMessage("c1", "L2", past, "ad-1", "camp-1", "2026-02-05T10:00:00"),
            new IgDmAdLeads.AdMessage("c1", "L2", past, "ad-1", "camp-1", "2026-02-06T10:00:00"),
            new IgDmAdLeads.AdMessage("c1", "L2", past, "ad-7", "camp-7", "2026-06-01T10:00:00"),
        ]);

        Assert.Equal(2, rows.Count);
        // Pul to'lagan eski lid o'z reklamasini SAQLAYDI (birinchi teginish).
        var old = Assert.Single(rows, r => r.LeadId == "L1");
        Assert.Equal("ad-1", old.AdId);
        Assert.Equal("2026-02-05T10:00:00", old.CreatedAt);
        // Qaytgan o'quvchini yangi reklama keltirdi — u yangi lidniki.
        Assert.Equal("ad-7", Assert.Single(rows, r => r.LeadId == "L2").AdId);

        // Tarixsiz — bitta suhbat, bitta qator, joriy lid (eski natija).
        var legacy = IgDmAdLeads.Attribute(
        [
            new IgDmAdLeads.AdMessage("c2", "L9", null, "ad-1", "", "2026-02-05T10:00:00"),
            new IgDmAdLeads.AdMessage("c2", "L9", null, "ad-2", "", "2026-02-07T10:00:00"),
        ]);
        Assert.Equal("ad-1", Assert.Single(legacy).AdId);
    }

    // =============================================================================================
    //  R2-1 — qaytgan o'quvchi: yangi lid, Telegram kartasi, "Avval o'quvchi bo'lgan"
    // =============================================================================================

    [Fact]
    public async Task Jonli_lid_aylantirilmagan_va_mavjud_lid()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var converted = await ConvertedLeadAsync(db);
        var live = new Lead { FullName = "Vali", CreatedAt = AppClock.Iso() };
        db.Leads.Add(live);
        await db.SaveChangesAsync();

        Assert.True(await IgLeadLinks.HasLiveLeadAsync(db, live.Id));
        Assert.False(await IgLeadLinks.HasLiveLeadAsync(db, converted.Id));   // aylantirilgan — "bog'lanmagan"
        Assert.False(await IgLeadLinks.HasLiveLeadAsync(db, "o-chirilgan"));
        Assert.False(await IgLeadLinks.HasLiveLeadAsync(db, null));
    }

    /// <summary>🔴 Ilgari: suhbat aylantirilgan lidga bog'langani uchun "birinchi bog'lanish" yolg'on
    /// bo'lib, yangi lidga faqat <c>SyncCardAsync</c> ketardi — u esa kartasi yo'q lidga hech narsa
    /// yubormaydi. Qaytgan o'quvchi Telegram guruhiga UMUMAN tushmasdi.</summary>
    [Fact]
    public async Task Qaytgan_oquvchining_yangi_lidi_Telegram_guruhiga_tushadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = await ConvertedLeadAsync(db);
        db.CenterMeta.Add(new CenterMeta
        {
            Name = "Wunderkind", InstagramEnabled = true, InstagramAutoReplyDm = false,
            InstagramReplyDelaySeconds = 0, InstagramDailyReplyLimit = 200, InstagramNotifyTelegram = true,
        });
        db.IgAccounts.Add(Account());
        db.TelegramGroups.Add(new TelegramGroup { ChatId = GroupChat, IsActive = true });
        var conv = new IgConversation { IgUserId = ClientId, Username = "ali_v", Status = IgConst.StatusBot, LeadId = old.Id };
        db.IgConversations.Add(conv);
        var ev = Event(DmJson("Assalomu alaykum, raqamim 90 123 45 67"));
        db.IgWebhookEvents.Add(ev);
        await db.SaveChangesAsync();

        using var token = TelegramTokenScope.Use();
        var tg = new FakeTelegramHandler();
        var telegram = new TelegramService(new SingleHandlerHttpClientFactory(tg), NullLogger<TelegramService>.Instance);
        await RunAsync(t, ev, new RecordingHandler(), telegram);

        var saved = await db.IgConversations.AsNoTracking().SingleAsync();
        var fresh = await db.Leads.AsNoTracking().SingleAsync(l => l.Id != old.Id);
        Assert.Equal(fresh.Id, saved.LeadId);
        Assert.Contains("Avval o'quvchi bo'lgan", fresh.Note);
        // Karta guruhga YUBORILDI (karta yozuvi bor, sendMessage ketdi).
        Assert.Contains(await db.LeadTelegramMessages.AsNoTracking().ToListAsync(),
            m => m.LeadId == fresh.Id && m.ChatId == GroupChat);
        Assert.Contains(tg.Calls, c => c.Method == "sendMessage" && c.ChatId == GroupChat);
        // Eski lid tarixda — aylantirilgan paytgacha.
        Assert.Equal(new[] { IgLeadLinks.Entry(old.Id, "2026-03-01T12:00:00") }, saved.PastLeadIds);
    }

    /// <summary>Telefonsiz qaytgan o'quvchi ham "Avval o'quvchi bo'lgan" izohini oladi — suhbat
    /// bog'langan aylantirilgan lid bo'yicha.</summary>
    [Fact]
    public async Task Telefonsiz_qaytgan_oquvchi_izohi_suhbat_lidi_boyicha()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = await ConvertedLeadAsync(db, "2026-03-01T12:00:00");
        var conv = new IgConversation { IgUserId = "u1", Username = "ali_v", LeadId = old.Id };
        db.IgConversations.Add(conv);
        await db.SaveChangesAsync();

        var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(db, conv, Output(), "Instagram");
        await db.SaveChangesAsync();

        Assert.True(isNew);
        var fresh = await db.Leads.SingleAsync(l => l.Id == leadId);
        Assert.Contains("Avval o'quvchi bo'lgan: Aliyev Ali (o'quvchi)", fresh.Note);
        Assert.Equal(new[] { IgLeadLinks.Entry(old.Id, "2026-03-01T12:00:00") }, conv.PastLeadIds);

        // Keyingi xabar — o'sha (jonli) lid, tarix O'ZGARMAYDI.
        var (again, againNew) = await InstagramLeadBridge.UpsertAsync(db, conv, Output(), "Instagram");
        Assert.False(againNew);
        Assert.Equal(leadId, again);
        Assert.Single(conv.PastLeadIds);
    }

    [Fact]
    public void Inbox_va_qolda_lid_yaratish_aylantirilgan_lidni_boglanmagan_deb_biladi()
    {
        var ctrl = Source("WunderkindLC.Server", "Controllers", "InstagramController.cs");
        var create = Between(ctrl, "public async Task<IActionResult> CreateLead(", "private async Task<string> FindPhoneAsync(");
        Assert.Contains("IgLeadLinks.HasLiveLeadAsync(db, c.LeadId, ct)", create);
        Assert.Contains("(firstLink || isNew)", create);
        Assert.Contains("Converted: l.ConvertedStudentId != null", ctrl);

        var inbox = Source("WunderkindLC.Client", "src", "pages", "admin", "marketing", "InstagramInbox.tsx");
        Assert.Contains("(!detail?.lead || detail.lead.converted)", inbox);
    }

    // =============================================================================================
    //  R2-2 — atributsiya pul to'lagan ESKI lidda qoladi
    // =============================================================================================

    [Fact]
    public async Task Yangi_lidga_kochganda_eski_lid_Instagram_va_reklama_atributsiyasini_saqlaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var old = await ConvertedLeadAsync(db, "2026-03-01T12:00:00");
        var conv = new IgConversation { IgUserId = "u-ad", Username = "ali", LeadId = old.Id, CreatedAt = "2026-02-01T09:00:00" };
        db.IgConversations.Add(conv);
        // «Click-to-Direct»: reklamadan kelgan birinchi DM — eski lidning tug'ilishi.
        db.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id, Direction = IgConst.DirIn, Channel = IgConst.ChannelDm,
            Text = "Salom", AdId = "ad-1", AdCampaignId = "camp-1", CreatedAt = "2026-02-01T09:00:00",
        });
        await db.SaveChangesAsync();

        // Qaytgan o'quvchi yana yozdi → YANGI lid, suhbat unga ko'chadi.
        var (freshId, isNew) = await InstagramLeadBridge.UpsertAsync(db, conv, Output(), "Instagram");
        await db.SaveChangesAsync();
        Assert.True(isNew);

        // ROI manbasi: reklama xabari ESKI (to'lagan) lidniki, yangisiga ko'chmagan.
        var row = Assert.Single(await IgDmAdLeads.LoadAsync(db));
        Assert.Equal(old.Id, row.LeadId);
        Assert.Equal("ad-1", row.AdId);

        // Kanal tahlili: eski lid — reklama, yangisi — Instagram (ikkalasi ham "Boshqa" emas).
        var linked = await IgLeadLinks.LinkedLeadIdsAsync(db);
        Assert.Contains(old.Id, linked);
        Assert.Contains(freshId, linked);
        var overview = await LeadCrmOverview.BuildAsync(db);
        Assert.Equal(1, overview.Origins.Single(o => o.Key == LeadOrigins.Ads).Leads);
        Assert.Equal(1, overview.Origins.Single(o => o.Key == LeadOrigins.Instagram).Leads);
        Assert.DoesNotContain(overview.Origins, o => o.Key == LeadOrigins.Other && o.Leads > 0);

        // Aylantirishdan KEYIN kelgan yangi reklama xabari — yangi lidniki.
        db.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id, Direction = IgConst.DirIn, Channel = IgConst.ChannelDm,
            Text = "Yana", AdId = "ad-7", AdCampaignId = "camp-7", CreatedAt = "2026-06-01T09:00:00",
        });
        await db.SaveChangesAsync();
        var rows = await IgDmAdLeads.LoadAsync(db);
        Assert.Equal(2, rows.Count);
        Assert.Equal("ad-7", rows.Single(r => r.LeadId == freshId).AdId);
    }

    /// <summary>O'chirilgan lidga (bazada yo'q) reklama qatori chiqmaydi — ROI'dagi "CRM lidlari"
    /// faqat mavjud lidlarni sanaydi.</summary>
    [Fact]
    public async Task Bazada_yoq_lidga_reklama_qatori_chiqmaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var conv = new IgConversation { IgUserId = "u-x", LeadId = "o-chirilgan", PastLeadIds = ["ham-yoq|2026-01-01T00:00:00"] };
        db.IgConversations.Add(conv);
        db.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id, Direction = IgConst.DirIn, Channel = IgConst.ChannelDm,
            Text = "Salom", AdId = "ad-1", CreatedAt = "2026-02-01T09:00:00",
        });
        await db.SaveChangesAsync();

        Assert.Empty(await IgDmAdLeads.LoadAsync(db));
    }

    [Fact]
    public void Ikkala_kanal_tahlili_bir_manbadan_tarix_bilan()
    {
        var leads = Source("WunderkindLC.Server", "Controllers", "LeadsController.cs");
        var analytics = Between(leads, "[HttpGet(\"analytics\")]", "LeadAnalytics.Build(");
        Assert.Contains("IgLeadLinks.LinkedLeadIdsAsync(db)", analytics);
        var overview = Source("WunderkindLC.Application", "Services", "LeadCrmOverview.cs");
        Assert.Contains("IgLeadLinks.LinkedLeadIdsAsync(db, ct)", overview);
        // `conv.LeadId` ni QO'LDA almashtiruvchi yo'l qolmagan (faqat `RelinkAsync`, o'chirishda tozalash).
        var bridge = Source("WunderkindLC.Application", "Services", "InstagramLeadBridge.cs");
        Assert.DoesNotContain("conv.LeadId =", bridge);
    }

    // =============================================================================================
    //  R3-8 — navbat qulfi kutishi CHEKLANGAN
    // =============================================================================================

    [Fact]
    public async Task Qulf_band_bolsa_kutish_cheklangan_va_ish_bajarilmaydi()
    {
        var hold = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var holder = InstagramPublishService.RunExclusiveAsync(async () =>
        {
            entered.SetResult();
            await hold.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;

        var ran = false;
        var (acquired, _) = await InstagramPublishService.RunExclusiveAsync(() =>
        {
            ran = true;
            return Task.FromResult(2);
        }, CancellationToken.None, TimeSpan.FromMilliseconds(50));
        Assert.False(acquired);
        Assert.False(ran);

        hold.SetResult();
        Assert.Equal((true, 1), await holder);
        Assert.Equal((true, 3), await InstagramPublishService.RunExclusiveAsync(
            () => Task.FromResult(3), CancellationToken.None));
        Assert.True(InstagramPublishService.ExclusiveWait <= TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Tahrir_bekor_va_joylash_band_qulfda_409_qaytaradi()
    {
        var src = Source("WunderkindLC.Server", "Controllers", "InstagramController.Content.cs");
        Assert.Equal(3, Regex.Matches(src, @"Conflict\(new \{ message = (InstagramPublishService\.GateBusyMessage|error) \}\)").Count);
        var svc = Source("WunderkindLC.Application", "Services", "InstagramPublishService.cs");
        var now = Between(svc, "public async Task<(bool Ok, string Error)> PublishNowAsync(", "//  3) KUNLIK LIMIT");
        Assert.Contains("Gate.WaitAsync(ExclusiveWait, ct)", now);
    }

    // =============================================================================================
    //  R3-9 — bot oshkorligi FAQAT shaxsiy yozishmada
    // =============================================================================================

    [Fact]
    public void Oshkorlik_kanallari()
    {
        Assert.False(InstagramPipeline.DisclosureApplies(IgConst.ChannelComment));
        Assert.True(InstagramPipeline.DisclosureApplies(IgConst.ChannelDm));
        Assert.True(InstagramPipeline.DisclosureApplies(IgConst.ChannelPrivateReply));
    }

    /// <summary>🔴 Ilgari har yangi izohchining birinchi OCHIQ javobiga uzun oshkorlik matni
    /// qo'shilardi — post ostida bir xil matn qatorlashib, spamga o'xshardi. Endi ochiq javob toza,
    /// yopiq javob (shaxsiy yozishma) esa oshkorlik bilan.</summary>
    [Fact]
    public async Task Ochiq_izoh_javobida_oshkorlik_yoq_yopiq_javobda_bor()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.CenterMeta.Add(new CenterMeta
        {
            Name = "Wunderkind", InstagramEnabled = true, InstagramAutoReplyComments = true,
            InstagramPrivateReplyEnabled = true, InstagramReplyDelaySeconds = 0,
            InstagramDailyReplyLimit = 200, InstagramNotifyTelegram = false,
        });
        db.IgAccounts.Add(Account());
        const string answer = "Narxlar: IELTS — 700 000 so'm/oy.";
        db.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx,qancha", Channel = "any", ReplyText = answer, StopAi = true, IsActive = true,
        });
        var ev = Event(CommentJson("Narxi qancha?"));
        db.IgWebhookEvents.Add(ev);
        await db.SaveChangesAsync();

        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        await RunAsync(t, ev, new RecordingHandler(), telegram);

        var outs = await db.IgMessages.AsNoTracking().Where(m => m.Direction == IgConst.DirOut).ToListAsync();
        var publicReply = Assert.Single(outs, m => m.Channel == IgConst.ChannelComment);
        Assert.Equal(answer, publicReply.Text);
        var privateReply = Assert.Single(outs, m => m.Channel == IgConst.ChannelPrivateReply);
        Assert.StartsWith(IgConst.DefaultGreeting, privateReply.Text);
        Assert.EndsWith(answer, privateReply.Text);
    }

    // =============================================================================================
    //  R3-10 — agent o'chiq bo'lsa qayta ulash webhook obunasini TIKLAMAYDI
    // =============================================================================================

    [Fact]
    public void Qayta_ulash_obunani_faqat_agent_yoqilganda_qiladi()
    {
        var webhook = Source("WunderkindLC.Server", "Controllers", "InstagramWebhookController.cs");
        var callback = Between(webhook, "public async Task<IActionResult> Callback(", "private RedirectResult Back(");
        Assert.Contains("var agentOn = meta?.InstagramEnabled == true;", callback);
        Assert.Matches(@"agentOn\s*\?\s*await api\.SubscribeWebhookAsync", callback);
        Assert.Single(Regex.Matches(callback, @"SubscribeWebhookAsync"));

        var ctrl = Source("WunderkindLC.Server", "Controllers", "InstagramController.cs");
        var manual = Between(ctrl, "public async Task<IActionResult> ConnectWithToken(", "//  SUHBATLAR (INBOX)");
        Assert.Matches(@"agentOn\s*\?\s*await api\.SubscribeWebhookAsync", manual);
        Assert.Single(Regex.Matches(manual, @"SubscribeWebhookAsync"));
    }

    // ============================ yordamchilar ============================

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

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    private static string Between(string src, string start, string end)
    {
        var i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, "Boshlanish topilmadi: " + start);
        var j = src.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, "Tugash topilmadi: " + end);
        return src[i..j];
    }
}
