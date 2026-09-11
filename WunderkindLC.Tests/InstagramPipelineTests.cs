using System.Net;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Instagram Graph API'ga ketgan so'rovlarni SANAYDIGAN va tayyor javob qaytaradigan handler.
/// <para>Testning asosiy savoli ko'pincha "tashqariga so'rov KETDIMI?" — shuning uchun har so'rov
/// yozib boriladi (<see cref="Requests"/>).</para>
/// </summary>
internal sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    : HttpMessageHandler
{
    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add($"{request.Method} {request.RequestUri}");
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary><see cref="InstagramPipeline"/> uchun eng kichik DI konteyner (scope = o'zi).</summary>
internal sealed class InstagramServiceProvider(IAppDbContext db, InstagramApi api, TelegramService telegram)
    : IServiceProvider, IServiceScopeFactory, IServiceScope
{
    private readonly IConfiguration _config = new ConfigurationBuilder().Build();

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory)) return this;
        if (serviceType == typeof(IAppDbContext)) return db;
        if (serviceType == typeof(InstagramApi)) return api;
        if (serviceType == typeof(TelegramService)) return telegram;
        if (serviceType == typeof(IConfiguration)) return _config;
        return null;
    }

    public IServiceScope CreateScope() => this;
    public IServiceProvider ServiceProvider => this;
    public void Dispose() { }
}

/// <summary>
/// INSTAGRAM OQIMI (<see cref="InstagramPipeline"/>) — soxta webhook JSON'idan boshlanadigan
/// UCHDAN-UCHGACHA testlar. Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §1, §3–§5.
///
/// <para><b>NIMA TEST QILINMAYDI VA NEGA:</b></para>
/// <list type="bullet">
///   <item><b>AI javobi bilan to'liq oqim.</b> <see cref="InstagramAgentService.AskAsync"/>
///     <see cref="GeminiService"/> ni STATIK chaqiradi (interfeys ortida emas, loyihada mock
///     kutubxonasi ham yo'q), ya'ni testda AI javobini "soxtalashtirib" bo'lmaydi. Shuning uchun
///     AI qismi <c>InstagramAgentServiceTests</c> da SOF funksiya (<c>ParseOutput</c>) darajasida
///     tekshiriladi, bu yerda esa faqat <b>AI ishlamagan holat</b> (kalit sozlanmagan) —
///     ya'ni "jonli javob yuborilmaydi" qoidasi.</item>
///   <item><b>"Qoida topildi ⇒ AI CHAQIRILMADI" ni bevosita tasdiqlash.</b> Gemini chaqiruvi
///     testdan kuzatiladigan iz qoldirmaydi (kalit yo'qligi sababli u baribir tarmoqqa
///     chiqmaydi). Kuzatiladigani tekshiriladi: javob matni AYNAN qoidaniki, muallif "Qoida: …",
///     <c>IsAi = false</c> va <c>MatchCount</c> oshgan.</item>
///   <item><b>Tabiiy kechikish</b> (<c>InstagramReplyDelaySeconds</c>) — testlarda 0 ga qo'yiladi,
///     aks holda har test 5 soniya kutardi. Kechikishning O'ZI mantiqiy qaror emas.</item>
/// </list>
/// </summary>
public class InstagramPipelineTests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";

    /// <summary>
    /// Fikstura vaqti — HOZIRGI paytdan hisoblanadi (qotib qolgan epoch EMAS).
    /// <para>⚠️ Ilgari bu yerda o'zgarmas <c>1786500000</c> turardi. Pipeline 24 soatlik DM
    /// oynasini Meta vaqtidan hisoblaydigan bo'lgach, o'sha fikstura "6 kun oldin" ga aylanib,
    /// testlar sababsiz qizara boshlagan edi. Nisbiy vaqt bilan test kelasi yili ham ishlaydi.</para>
    /// </summary>
    private static long NowSeconds => new DateTimeOffset(AppClock.Now.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();
    private static long NowMillis => NowSeconds * 1000;

    /* ───────────────────────── yordamchilar ───────────────────────── */

    private static string DmJson(string text = "Narxi qancha?", string mid = "mid-1", bool echo = false) => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}}, "messaging": [{
            "sender": { "id": "{{(echo ? OurId : ClientId)}}" },
            "recipient": { "id": "{{(echo ? ClientId : OurId)}}" },
            "timestamp": {{NowMillis}},
            "message": { "mid": "{{mid}}", "text": "{{text}}" } }]}]}
        """;

    private static string CommentJson(string fromId = ClientId, string text = "Narxi qancha?") => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}}, "changes": [{ "field": "comments",
            "value": { "id": "c-1", "text": "{{text}}",
                       "from": { "id": "{{fromId}}", "username": "ali" },
                       "media": { "id": "m-1" } } }]}]}
        """;

    private static CenterMeta Meta(bool enabled = true, bool dm = true, bool comments = true, int limit = 200) => new()
    {
        Name = "Wunderkind",
        InstagramEnabled = enabled,
        InstagramAutoReplyDm = dm,
        InstagramAutoReplyComments = comments,
        InstagramReplyDelaySeconds = 0,      // testda kutish yo'q
        InstagramDailyReplyLimit = limit,
        InstagramNotifyTelegram = false,
    };

    private static IgAccount Account() => new()
    {
        IgUserId = OurId,
        Username = "wunderkind",
        AccessToken = "test-token",
        IsActive = true,
        ConnectedAt = AppClock.Iso(),
    };

    private static IgWebhookEvent Event(string rawJson) => new()
    {
        EventKey = "test:" + Guid.NewGuid().ToString("N"),
        RawJson = rawJson,
        Status = IgConst.EvPending,
        ReceivedAt = AppClock.Iso(),
    };

    /// <summary>
    /// MIJOZGA YUBORISH so'rovlari (POST) — <b>profil so'rovi hisobga olinmaydi</b>.
    ///
    /// <para>⚠️ Ko'p testning savoli "javob KETDIMI". DM'da esa webhook username bermaydi va
    /// pipeline yangi suhbat uchun bir marta <c>GET /{igsid}?fields=name,username</c> qiladi
    /// (aks holda Inbox'da suhbat <c>@1784140…</c> degan RAQAM bo'lib turardi). U javob EMAS,
    /// shuning uchun "yuborilmadi" tekshiruvlari aynan POST'lar bo'yicha qilinadi.</para>
    ///
    /// <para>⚠️ "Modul BUTUNLAY o'chiq" testida esa baribir <c>handler.Requests</c> ning O'ZI
    /// bo'sh bo'lishi tekshiriladi (qoidalar §3: tashqariga hech narsa ketmaydi).</para>
    /// </summary>
    private static List<string> Sends(RecordingHandler h) =>
        h.Requests.Where(r => r.StartsWith("POST", StringComparison.Ordinal)).ToList();

    /// <summary>Pipeline'ni tarmoqsiz (yoki yozib boruvchi) HTTP bilan ishga tushiradi.</summary>
    private static async Task<IgWebhookEvent> RunAsync(TestDb db, IgWebhookEvent ev, RecordingHandler handler)
    {
        var api = new InstagramApi(new HttpClient(handler), NullLogger<InstagramApi>.Instance);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var pipeline = new InstagramPipeline(
            new InstagramServiceProvider(db.Context, api, telegram), NullLogger<InstagramPipeline>.Instance);

        await pipeline.ProcessAsync(ev.Id, CancellationToken.None);
        return db.Context.IgWebhookEvents.Single(e => e.Id == ev.Id);
    }

    // ===================== 1) MODUL O'CHIQ =====================

    [Fact]
    public async Task Modul_ochiq_bolsa_javob_yozilmaydi_va_tashqariga_sorov_ketmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", Channel = "any", ReplyText = "Narxlar: …", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        var done = await RunAsync(db, ev, handler);

        Assert.Empty(handler.Requests);                                    // ⚠️ asosiy tekshiruv
        Assert.Equal(IgConst.EvDone, done.Status);
        var msg = Assert.Single(db.Context.IgMessages);
        Assert.Equal(IgConst.DirIn, msg.Direction);                        // faqat kiruvchi xabar
    }

    [Fact]
    public async Task Modul_ochiq_bolsa_ham_kiruvchi_xabar_tarixga_yoziladi()
    {
        // Tarix yo'qolmasin: modul yoqilganda operator butun suhbatni ko'radi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: false));
        var ev = Event(DmJson(text: "Salom"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());

        var conv = Assert.Single(db.Context.IgConversations);
        Assert.Equal(ClientId, conv.IgUserId);
        Assert.Equal("Salom", conv.LastMessageText);
        Assert.Equal(1, conv.MessageCount);
    }

    [Fact]
    public async Task Kanal_boyicha_avto_javob_ochirilgan_bolsa_sorov_ketmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", ReplyText = "Narxlar: …", IsActive = true,
        });
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));
        Assert.DoesNotContain(db.Context.IgMessages, m => m.Direction == IgConst.DirOut);
    }

    // ===================== 2) KALIT SO'Z QOIDASI =====================

    [Fact]
    public async Task Qoida_mos_kelsa_javob_yoziladi_va_yuboriladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var rule = new IgAutoRule
        {
            Title = "Narx savoli", Keywords = "narx,qancha", Channel = "any",
            ReplyText = "Narxlar: IELTS — 700 000 so'm/oy.", StopAi = true, IsActive = true,
        };
        db.Context.IgAutoRules.Add(rule);
        var ev = Event(DmJson(text: "Narxi qancha?"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        var done = await RunAsync(db, ev, handler);

        Assert.Equal(IgConst.EvDone, done.Status);
        var outbound = Assert.Single(db.Context.IgMessages.Where(m => m.Direction == IgConst.DirOut).ToList());
        Assert.Equal("Narxlar: IELTS — 700 000 so'm/oy.", outbound.Text);
        Assert.Equal("Qoida: Narx savoli", outbound.ActorName);
        Assert.False(outbound.IsAi);
        Assert.Equal("", outbound.Error);
        Assert.Single(Sends(handler));                         // aynan bitta DM yuborildi
        Assert.Equal(1, db.Context.IgAutoRules.Single().MatchCount);
    }

    [Fact]
    public async Task Yuborishda_xato_bolsa_operator_chaqiriladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", ReplyText = "Narxlar: …", StopAi = true, IsActive = true,
        });
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        // Meta 190 = token bekor qilingan (qayta urinilmaydigan doimiy xato).
        var handler = new RecordingHandler(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":190,\"message\":\"Invalid OAuth access token\"}}");
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("Javob yuborilmadi", conv.NeedsOperatorReason);
        var outbound = db.Context.IgMessages.Single(m => m.Direction == IgConst.DirOut);
        Assert.NotEqual("", outbound.Error);       // xato lentada ko'rinadi
    }

    // ===================== 3) O'Z YOZUVIMIZ =====================

    [Fact]
    public async Task Oz_izohimiz_tashlanadi_va_hodisa_skipped_boladi()
    {
        // ⚠️ Cheksiz halqa himoyasi: bot o'z javobiga javob yozmaydi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var ev = Event(CommentJson(fromId: OurId));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        var done = await RunAsync(db, ev, handler);

        Assert.Equal(IgConst.EvSkipped, done.Status);
        Assert.NotEqual("", done.Error);              // jimgina yo'qolmaydi — sabab yozilgan
        Assert.Empty(db.Context.IgConversations);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Buzuq_payload_skipped_boladi_va_navbat_toxtamaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        var ev = Event("{buzuq");
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var done = await RunAsync(db, ev, new RecordingHandler());

        Assert.Equal(IgConst.EvSkipped, done.Status);
    }

    // ===================== 4) ECHO → OPERATOR PAUZASI =====================

    [Fact]
    public async Task Echo_kelsa_operator_pauzasi_yoqiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        db.Context.IgConversations.Add(new IgConversation
        {
            IgUserId = ClientId, Username = "ali", Status = IgConst.StatusBot, CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson(text: "Ertaga soat 15:00 da keling", mid: "echo-1", echo: true));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.NotEqual("", conv.OperatorPausedUntil);
        Assert.True(InstagramContract.OperatorPaused(conv, AppClock.Now));
        var msg = db.Context.IgMessages.Single();
        Assert.Equal(IgConst.DirOut, msg.Direction);
        Assert.Equal(IgConst.ActorOperatorIg, msg.ActorName);
        Assert.Empty(handler.Requests);              // echo'ga HECH QACHON javob yozilmaydi
    }

    [Fact]
    public async Task Botning_oz_javobi_echo_bolib_qaytsa_pauza_qoyilmaydi()
    {
        // Aks holda bot har javobidan keyin o'zini jim qilib qo'yardi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var conv = new IgConversation
        {
            IgUserId = ClientId, Username = "ali", Status = IgConst.StatusBot, CreatedAt = AppClock.Iso(),
        };
        db.Context.IgConversations.Add(conv);
        db.Context.IgMessages.Add(new IgMessage
        {
            ConversationId = conv.Id, Direction = IgConst.DirOut, Channel = IgConst.ChannelDm,
            Text = "Bizning javobimiz", ActorName = IgConst.ActorAi, IsAi = true, CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson(text: "Bizning javobimiz", mid: "echo-2", echo: true));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());

        Assert.Equal("", db.Context.IgConversations.Single().OperatorPausedUntil);
    }

    [Fact]
    public async Task Operator_pauzasi_kuchda_bolsa_bot_javob_bermaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", ReplyText = "Narxlar: …", StopAi = true, IsActive = true,
        });
        db.Context.IgConversations.Add(new IgConversation
        {
            IgUserId = ClientId, Username = "ali", Status = IgConst.StatusBot,
            OperatorPausedUntil = AppClock.Now.AddMinutes(20).ToString("yyyy-MM-ddTHH:mm:ss"),
            CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(db.Context.IgMessages, m => m.Direction == IgConst.DirOut);
    }

    // ===================== 5) HIMOYA DARVOZALARI =====================

    [Fact]
    public async Task Matnsiz_xabar_jimgina_yoqolmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: ""));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("Matnsiz", conv.NeedsOperatorReason);
        Assert.Equal("[matnsiz xabar]", conv.LastMessageText);
        Assert.Empty(Sends(handler));
    }

    [Fact]
    public async Task Akkaunt_ulanmagan_bolsa_javob_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());   // IgAccount YO'Q
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", ReplyText = "Narxlar: …", StopAi = true, IsActive = true,
        });
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("akkaunt ulanmagan", conv.NeedsOperatorReason.ToLowerInvariant());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Kunlik_chegara_tugasa_javob_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(limit: 1));
        db.Context.IgAccounts.Add(Account());
        db.Context.IgAutoRules.Add(new IgAutoRule
        {
            Title = "Narx", Keywords = "narx", ReplyText = "Narxlar: …", StopAi = true, IsActive = true,
        });
        // Bugun allaqachon bitta javob ketgan (chegara = 1).
        db.Context.IgMessages.Add(new IgMessage
        {
            ConversationId = "boshqa-suhbat", Direction = IgConst.DirOut, Channel = IgConst.ChannelDm,
            Text = "oldingi javob", CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("Kunlik javob chegarasi", conv.NeedsOperatorReason);
        Assert.Empty(Sends(handler));
    }

    [Fact]
    public async Task AI_ishlamasa_va_qoida_yoq_bolsa_jonli_javob_YUBORILMAYDI()
    {
        // ⚠️ "Bir narsa yozib qo'yamiz" varianti YO'Q — operatorga signal beriladi.
        // (Testda Gemini kaliti sozlanmagan, ya'ni AI aynan shu holatda.)
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "Sizda nemis tili bormi?"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("AI javob bera olmadi", conv.NeedsOperatorReason);
        Assert.Empty(Sends(handler));
        Assert.DoesNotContain(db.Context.IgMessages, m => m.Direction == IgConst.DirOut);
    }

    // ===================== 6) Navbat holati =====================

    [Fact]
    public async Task Qayta_ishlangan_hodisa_ikkinchi_marta_ishlanmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: false));
        var ev = Event(DmJson());
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());
        await RunAsync(db, ev, new RecordingHandler());   // Status endi `done` — ikkinchi marta o'tmaydi

        Assert.Equal(1, db.Context.IgMessages.Count());
        Assert.Equal(1, db.Context.IgConversations.Single().MessageCount);
    }

    // ===================== 7) DM'da username — PROFIL SO'ROVIDAN =====================

    [Fact]
    public async Task DM_da_username_profil_sorovidan_aniqlanadi()
    {
        // ⚠️ Webhook'ning `messaging[]` bo'limida username UMUMAN yo'q — faqat `sender.id`.
        // Usiz Inbox'da suhbat `@5550001112223` degan RAQAM bo'lib turardi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));     // avtojavob shart emas — faqat O'QISH
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "Salom"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler(body: """{"id":"5550001112223","username":"ali_vali","name":"Ali"}""");
        await RunAsync(db, ev, handler);

        var conv = db.Context.IgConversations.Single();
        Assert.Equal("ali_vali", conv.Username);
        Assert.Empty(Sends(handler));                                    // javob YUBORILMADI
        Assert.Contains(handler.Requests, r => r.Contains("fields=name,username"));
        Assert.Equal("@ali_vali", db.Context.IgMessages.Single().ActorName);
    }

    [Fact]
    public async Task Profil_sorovi_yiqilsa_xabar_baribir_yoziladi()
    {
        // Username — QULAYLIK. U olinmagani mijozning xabarini yo'qotishga sabab bo'lmaydi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "Salom"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler(HttpStatusCode.BadRequest,
            """{"error":{"message":"Unsupported get request","code":100}}""");
        var done = await RunAsync(db, ev, handler);

        Assert.Equal(IgConst.EvDone, done.Status);
        var conv = db.Context.IgConversations.Single();
        Assert.Equal("", conv.Username);
        Assert.Equal("Salom", conv.LastMessageText);
        Assert.Equal("Mijoz", db.Context.IgMessages.Single().ActorName);
    }

    [Fact]
    public async Task Username_allaqachon_bor_bolsa_profil_sorovi_TAKRORLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        db.Context.IgConversations.Add(new IgConversation
        {
            IgUserId = ClientId, Username = "ali_vali", Status = IgConst.StatusBot, CreatedAt = AppClock.Iso(),
        });
        var ev = Event(DmJson(text: "Yana savol"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(handler.Requests);                                  // bitta ham so'rov yo'q
        Assert.Equal("ali_vali", db.Context.IgConversations.Single().Username);
    }

    [Fact]
    public async Task Qollab_quvvatlanmaydigan_maydon_SABABI_bilan_skipped_boladi()
    {
        // "Hodisa kelyapti, lekin hech narsa bo'lmayapti" ning eng ko'p uchraydigan sababi —
        // Meta'da keraksiz maydonga obuna. Umumiy matn buni ko'rsatmasdi.
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var ev = Event($$"""
            { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}},
                "changes": [{ "field": "mentions", "value": { "media_id": "m-9", "comment_id": "c-9" } }]}]}
            """);
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var done = await RunAsync(db, ev, new RecordingHandler());

        Assert.Equal(IgConst.EvSkipped, done.Status);
        Assert.Contains("mentions", done.Error);
    }

    // ═══════════════ IZOH TARIXI — o'z posti bilan chegaralangan ═══════════════

    /// <summary>Tarix uchun bitta xabar qatori (vaqt tartibi CreatedAt bo'yicha).</summary>
    private static IgMessage Msg(string convId, string channel, string mediaId, string text, string at) =>
        new()
        {
            ConversationId = convId, Direction = IgConst.DirIn, Channel = channel,
            MediaId = mediaId, Text = text, CreatedAt = at,
        };

    /// <summary>
    /// 🔴 Ochiq izohga javob YOZILADI, ya'ni promptdagi har bir qator omma o'qiydigan matnga
    /// aylanishi mumkin. Shaxsiy yozishma (telefon, to'lov haqidagi gap) u yerga tushmasligi
    /// SHART — chegara tuzilma darajasida: DM qatorlari so'rovga UMUMAN olinmaydi.
    /// </summary>
    [Fact]
    public async Task Izoh_tarixiga_DM_qatorlari_TUSHMAYDI()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelDm, "", "Telefonim 901234567", "2026-08-23T10:00:00"),
            Msg("c1", IgConst.ChannelComment, "post-1", "Narxi qancha?", "2026-08-23T10:01:00"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelComment, "post-1", CancellationToken.None);

        var only = Assert.Single(history);
        Assert.Equal("Narxi qancha?", only.Text);
    }

    /// <summary>
    /// Izoh — YAKKA savol: konteksti post matni va O'SHA post ostidagi yozishma. Boshqa post
    /// ostidagi eski savol qo'shilsa model ostidagi izohga tegishsiz javob yozardi.
    /// </summary>
    [Fact]
    public async Task Izoh_tarixiga_BOSHQA_post_izohlari_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelComment, "post-1", "IELTS bormi?", "2026-08-23T10:00:00"),
            Msg("c1", IgConst.ChannelComment, "post-2", "Manzilingiz?", "2026-08-23T10:01:00"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelComment, "post-2", CancellationToken.None);

        var only = Assert.Single(history);
        Assert.Equal("Manzilingiz?", only.Text);
    }

    /// <summary>
    /// ⚠️ <c>MediaId</c> bo'sh bo'lsa (eski yozuv / Meta post id bermagan) post bo'yicha ajratib
    /// bo'lmaydi — u holda hech bo'lmaganda KANAL bo'yicha filtrlanadi, ya'ni DM baribir
    /// promptga tushmaydi. Zaxira yo'l ham OCHIQ kanal qoidasini buzmaydi.
    /// </summary>
    [Fact]
    public async Task MediaId_bosh_bolsa_ham_DM_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelDm, "", "Shaxsiy gap", "2026-08-23T10:00:00"),
            Msg("c1", IgConst.ChannelComment, "", "Ochiq savol", "2026-08-23T10:01:00"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelComment, "", CancellationToken.None);

        var only = Assert.Single(history);
        Assert.Equal("Ochiq savol", only.Text);
    }

    /// <summary>Yopiq javob (private reply) — o'sha IZOHNING davomi, shuning uchun QOLADI.</summary>
    [Fact]
    public async Task Izoh_tarixida_yopiq_javob_QOLADI()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelComment, "post-1", "Narxi?", "2026-08-23T10:00:00"),
            Msg("c1", IgConst.ChannelPrivateReply, "post-1", "DM'ga yozdik", "2026-08-23T10:00:30"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelComment, "post-1", CancellationToken.None);

        Assert.Equal(2, history.Count);
    }

    /// <summary>
    /// DM tomonida xulq O'ZGARMAYDI: shaxsiy yozishma bitta uzluksiz muloqot, izohlar ham
    /// o'sha odamning tarixi — hammasi qoladi.
    /// </summary>
    [Fact]
    public async Task DM_tarixi_avvalgidek_HAMMASINI_oladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelComment, "post-1", "Izoh", "2026-08-23T10:00:00"),
            Msg("c1", IgConst.ChannelDm, "", "DM", "2026-08-23T10:01:00"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelDm, "", CancellationToken.None);

        Assert.Equal(2, history.Count);
    }

    /// <summary>Tarix ESKIDAN YANGIGA tartiblanadi — prompt suhbatni shu ko'rinishda o'qiydi.</summary>
    [Fact]
    public async Task Tarix_eskisidan_yangisiga_tartiblanadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgMessages.AddRange(
            Msg("c1", IgConst.ChannelComment, "post-1", "Ikkinchi", "2026-08-23T10:05:00"),
            Msg("c1", IgConst.ChannelComment, "post-1", "Birinchi", "2026-08-23T10:00:00"));
        await db.Context.SaveChangesAsync();

        var history = await InstagramPipeline.LoadHistoryAsync(
            db.Context, "c1", IgConst.ChannelComment, "post-1", CancellationToken.None);

        Assert.Equal("Birinchi", history[0].Text);
        Assert.Equal("Ikkinchi", history[1].Text);
    }

    [Fact]
    public async Task Yoq_hodisa_uchun_yiqilmaydi()
    {
        using var db = TestDb.Sqlite();
        var api = new InstagramApi(new HttpClient(new RecordingHandler()), NullLogger<InstagramApi>.Instance);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var pipeline = new InstagramPipeline(
            new InstagramServiceProvider(db.Context, api, telegram), NullLogger<InstagramPipeline>.Instance);

        await pipeline.ProcessAsync("yoq-id", CancellationToken.None);   // istisno OTILMAYDI
    }

    // ===================== TELEFON RAQAMI → LID (AI'siz) =====================
    //
    // Qoida: mijoz raqamini bersa u LIDGA tushadi — javob berilish-berilmasligidan QAT'I NAZAR.
    // (`.claude/rules/marketing-instagram.md` §6.3)

    /// <summary>
    /// 🔴 ASOSIY HOLAT: DM avtojavobi O'CHIQ. Bot mijozga hech narsa yozmaydi, lekin raqam
    /// baribir lidga tushadi — ilgari bu yerda lid UMUMAN yozilmasdi.
    /// </summary>
    [Fact]
    public async Task Avtojavob_ochiq_bolsa_ham_telefon_lidga_tushadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "Ali Valiyev 90 123 45 67"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));                       // mijozga javob KETMAYDI
        var lead = Assert.Single(db.Context.Leads);
        Assert.Equal(PhoneUtil.Normalize("998901234567"), lead.Phone);
        Assert.Equal("Ali Valiyev", lead.FullName);         // ism matndan ajratildi
        var conv = Assert.Single(db.Context.IgConversations);
        Assert.Equal(lead.Id, conv.LeadId);                 // suhbat lidga BOG'LANDI
        Assert.True(conv.LeadScore >= IgConst.HotLeadScore);
    }

    /// <summary>Ism topilmasa lid baribir yoziladi — raqam eng qimmatli ma'lumot.</summary>
    [Fact]
    public async Task Ismsiz_raqam_ham_lid_ochadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "901234567"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());

        var lead = Assert.Single(db.Context.Leads);
        Assert.Equal(PhoneUtil.Normalize("998901234567"), lead.Phone);
        Assert.Contains("Instagram", lead.FullName);        // «username (Instagram)» zaxira nomi
    }

    /// <summary>Raqamsiz oddiy savol lid OCHMAYDI — CRM salom-alik bilan to'lib ketmasin.</summary>
    [Fact]
    public async Task Raqamsiz_xabar_lid_ochmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "Assalomu alaykum, narxi qancha?"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());

        Assert.Empty(db.Context.Leads);
    }

    /// <summary>⚠️ Modul BUTUNLAY o'chiq bo'lsa lid ham yozilmaydi: master darvoza hamma
    /// narsadan yuqori turadi (qoidalar §3).</summary>
    [Fact]
    public async Task Modul_ochiq_bolsa_telefon_ham_lid_ochmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(enabled: false));
        db.Context.IgAccounts.Add(Account());
        var ev = Event(DmJson(text: "901234567"));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, ev, new RecordingHandler());

        Assert.Empty(db.Context.Leads);
    }

    /// <summary>
    /// Bir odam raqamini IKKI marta yozsa IKKINCHI lid ochilmaydi — navbat bir xil odam bilan
    /// to'lib ketmasin (takror faqat `RepeatCount` da ko'rinadi).
    /// </summary>
    [Fact]
    public async Task Ikkinchi_raqamli_xabar_yangi_lid_ochmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var first = Event(DmJson(text: "901234567", mid: "mid-a"));
        var second = Event(DmJson(text: "Yana yozdim 901234567", mid: "mid-b"));
        db.Context.IgWebhookEvents.AddRange(first, second);
        await db.Context.SaveChangesAsync();

        await RunAsync(db, first, new RecordingHandler());
        await RunAsync(db, second, new RecordingHandler());

        var lead = Assert.Single(db.Context.Leads);
        Assert.Equal(1, lead.RepeatCount);
    }

    // ===================== FAQ TUGMALARI (ice breaker postback) =====================

    private static string PostbackJson(string payload, string title = "Narxlar qancha?", string mid = "pb-1") => $$"""
        { "entry": [{ "id": "{{OurId}}", "time": {{NowSeconds}}, "messaging": [{
            "sender": { "id": "{{ClientId}}" },
            "recipient": { "id": "{{OurId}}" },
            "timestamp": {{NowMillis}},
            "postback": { "mid": "{{mid}}", "title": "{{title}}", "payload": "{{payload}}" } }]}]}
        """;

    private static IgIceBreaker IceBreaker(bool active = true) => new()
    {
        Question = "Narxlar qancha?",
        Answer = "Narxlar: IELTS — 700 000 so'm/oy.",
        IsActive = active,
        Order = 0,
        CreatedAt = AppClock.Iso(),
        UpdatedAt = AppClock.Iso(),
    };

    /// <summary>Tugma bosildi → saqlangan javob yuboriladi, AI CHAQIRILMAYDI (Gemini kaliti
    /// yo'q muhitda AI yo'li «AI javob bera olmadi» eskalatsiyasi berardi — bermagani javob
    /// FAQ'dan ketganining isboti), TapCount oshadi.</summary>
    [Fact]
    public async Task Faq_tugmasi_bosilsa_saqlangan_javob_yuboriladi_va_tap_count_oshadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var faq = IceBreaker();
        db.Context.IgIceBreakers.Add(faq);
        var ev = Event(PostbackJson(InstagramContract.FaqPayload(faq.Id)));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        var done = await RunAsync(db, ev, handler);

        Assert.Equal(IgConst.EvDone, done.Status);
        var outbound = Assert.Single(db.Context.IgMessages.Where(m => m.Direction == IgConst.DirOut).ToList());
        Assert.Equal("Narxlar: IELTS — 700 000 so'm/oy.", outbound.Text);
        Assert.Equal(IgConst.ActorFaq, outbound.ActorName);
        Assert.False(outbound.IsAi);
        Assert.Equal("", outbound.Error);
        Assert.Single(Sends(handler));                                    // aynan bitta DM
        Assert.Equal(1, db.Context.IgIceBreakers.Single().TapCount);

        // Kiruvchi qator — tugma SARLAVHASI bilan (lentada mijozning "savoli" bo'lib turadi).
        var inbound = db.Context.IgMessages.Single(m => m.Direction == IgConst.DirIn);
        Assert.Equal("Narxlar qancha?", inbound.Text);
        var conv = db.Context.IgConversations.Single();
        Assert.False(conv.NeedsOperator);                                 // AI yo'liga tushmadi
    }

    /// <summary>O'chirilgan tugma (yoki eski suhbatda qolgan payload) → oddiy qoida→AI oqimi.
    /// AI kaliti yo'q muhitda bu «AI javob bera olmadi» eskalatsiyasi bilan ko'rinadi —
    /// ya'ni hodisa FAQ yo'lida emas, umumiy yo'lda ketgan.</summary>
    [Fact]
    public async Task Ochirilgan_faq_tugmasi_oddiy_ai_oqimiga_tushadi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta());
        db.Context.IgAccounts.Add(Account());
        var faq = IceBreaker(active: false);
        db.Context.IgIceBreakers.Add(faq);
        var ev = Event(PostbackJson(InstagramContract.FaqPayload(faq.Id)));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));                                     // javob KETMADI
        Assert.Equal(0, db.Context.IgIceBreakers.Single().TapCount);      // o'chiq tugma sanalmaydi
        var conv = db.Context.IgConversations.Single();
        Assert.True(conv.NeedsOperator);
        Assert.Contains("AI javob bera olmadi", conv.NeedsOperatorReason);
    }

    /// <summary>DM avto-javobi o'chirilgan bo'lsa FAQ javobi ham yuborilmaydi — darvozalar
    /// FAQ uchun ham hurmat qilinadi (tugma javobi ham AVTOMATIK javob).</summary>
    [Fact]
    public async Task Dm_avtojavobi_ochiq_bolsa_faq_javobi_ham_yuborilmaydi()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(Meta(dm: false));
        db.Context.IgAccounts.Add(Account());
        var faq = IceBreaker();
        db.Context.IgIceBreakers.Add(faq);
        var ev = Event(PostbackJson(InstagramContract.FaqPayload(faq.Id)));
        db.Context.IgWebhookEvents.Add(ev);
        await db.Context.SaveChangesAsync();

        var handler = new RecordingHandler();
        await RunAsync(db, ev, handler);

        Assert.Empty(Sends(handler));
        Assert.Equal(0, db.Context.IgIceBreakers.Single().TapCount);
        // Kiruvchi qator baribir yoziladi — tarix yo'qolmaydi.
        Assert.Single(db.Context.IgMessages.Where(m => m.Direction == IgConst.DirIn).ToList());
    }
}
