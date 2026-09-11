using System.Diagnostics;
using System.Net;
using System.Text;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Instagram Content Publishing API'sini soxtalashtiradigan handler: javob MANZILGA qarab
/// tanlanadi (limit → konteyner → holat → chop etish → havola).
/// <para>Umumiy <c>RecordingHandler</c> BITTA tanani qaytaradi, bu yerda esa bitta oqim ichida
/// TO'RT xil javob kerak — shuning uchun alohida.</para>
/// </summary>
internal sealed class IgPublishFakeHandler : HttpMessageHandler
{
    public List<string> Requests { get; } = new();

    public string LimitBody = """{"data":[{"quota_usage":0,"config":{"quota_total":50,"quota_duration":86400}}]}""";
    public string CreateBody = """{"id":"container-1"}""";
    public HttpStatusCode CreateStatus = HttpStatusCode.OK;
    public string StatusBody = """{"status_code":"FINISHED","status":"Finished"}""";
    public string PublishBody = """{"id":"media-1"}""";
    public string PermalinkBody = """{"permalink":"https://www.instagram.com/p/abc/"}""";

    /// <summary>Faqat konteyner YARATISH so'rovlari (bolalar + ota-ona).</summary>
    public int CreateCalls => Requests.Count(r =>
        r.Contains("/media") && !r.Contains("media_publish") && !r.Contains("fields="));

    public int PublishCalls => Requests.Count(r => r.Contains("media_publish"));
    public int StatusCalls => Requests.Count(r => r.Contains("fields=status_code"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri?.ToString() ?? "";
        Requests.Add($"{request.Method} {url}");

        var status = HttpStatusCode.OK;
        string body;
        if (url.Contains("content_publishing_limit")) body = LimitBody;
        else if (url.Contains("fields=permalink")) body = PermalinkBody;
        else if (url.Contains("fields=status_code")) body = StatusBody;
        else if (url.Contains("media_publish")) body = PublishBody;
        else { body = CreateBody; status = CreateStatus; }

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// KONTENT JOYLASH (<see cref="InstagramPublishService"/>) — navbatdan Instagram'gacha.
/// Rasmiy manba: <c>KENGAYTIRISH-PROMPT.md</c> §5.2–§5.9.
///
/// <para><b>Eng qimmat qoidalar shu yerda qulflanadi:</b> modul o'chiq bo'lsa tashqariga so'rov
/// KETMASLIGI, limit to'lganda post <b>yiqilmasligi</b>, <c>IN_PROGRESS</c> konteyner worker'ni
/// <b>bloklamasligi</b> va validatsiyadan o'tmagan post uchun tarmoqqa <b>umuman chiqilmasligi</b>.</para>
/// </summary>
public class InstagramPublishServiceTests
{
    private const string Jpeg = "https://cdn.example.com/rasm.jpg";

    private static string MediaJson(string url = Jpeg, string kind = "image") =>
        $$"""[{"url":"{{url}}","kind":"{{kind}}"}]""";

    /// <summary>Modul yoqilgan, akkaunt ulangan, tokeni tirik — standart holat.</summary>
    private static void Seed(TestDb db, bool enabled = true, bool tokenAlive = true)
    {
        db.Context.CenterMeta.Add(new CenterMeta
        {
            InstagramPublishEnabled = enabled,
            InstagramNotifyTelegram = false,   // Telegram testda tarmoqqa chiqmasin
        });
        db.Context.IgAccounts.Add(new IgAccount
        {
            IgUserId = "ig-1",
            AccessToken = "tok",
            IsActive = true,
            TokenExpiresAt = AppClock.Now.AddDays(tokenAlive ? 30 : -1).ToString("yyyy-MM-ddTHH:mm:ss"),
            ConnectedAt = AppClock.Iso(),
        });
        db.Context.SaveChanges();
    }

    /// <param name="minutes">Rejalashtirilgan vaqt — hozirga nisbatan (manfiy = vaqti kelgan).</param>
    private static IgScheduledPost AddPost(
        TestDb db, int minutes = -1, string? mediaJson = null, string type = IgPublishConst.TypeImage)
    {
        var post = new IgScheduledPost
        {
            PostType = type,
            Caption = "Yozgi intensiv boshlandi!",
            MediaJson = mediaJson ?? MediaJson(),
            OptionsJson = "",
            ScheduledAt = AppClock.Now.AddMinutes(minutes).ToString("yyyy-MM-ddTHH:mm:ss"),
            Status = IgPublishConst.StScheduled,
            CreatedBy = "Admin",
            CreatedAt = AppClock.Iso(),
        };
        db.Context.IgScheduledPosts.Add(post);
        db.Context.SaveChanges();
        return post;
    }

    private static (InstagramPublishService Svc, IgPublishFakeHandler Handler) Build(TestDb db)
    {
        // ⚠️ Xotiradagi poll jadvali STATIC — testlar orasida oqib ketmasin.
        InstagramPublishService.ResetPollState();

        var handler = new IgPublishFakeHandler();
        var api = new InstagramPublishApi(new HttpClient(handler), NullLogger<InstagramPublishApi>.Instance);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);
        var svc = new InstagramPublishService(
            db.Context, api, telegram, NullLogger<InstagramPublishService>.Instance);
        return (svc, handler);
    }

    private static async Task<IgScheduledPost> ReloadAsync(TestDb db, string id) =>
        await db.Context.IgScheduledPosts.AsNoTracking().FirstAsync(p => p.Id == id);

    // =============================================================================================

    /// <summary>⚠️ DARVOZA: modul o'chiq bo'lsa Meta'ga so'rov UMUMAN ketmaydi va post
    /// navbatda o'zgarishsiz qoladi.</summary>
    [Fact]
    public async Task Modul_ochiq_bolsa_tashqariga_sorov_ketmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db, enabled: false);
        var post = AddPost(db);
        var (svc, handler) = Build(db);

        var (ok, processed, error) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(0, processed);
        Assert.Equal("", error);
        Assert.Empty(handler.Requests);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StScheduled, saved.Status);
        Assert.Equal(0, saved.Attempts);
    }

    /// <summary>Vaqti kelmagan post navbatdan OLINMAYDI (<c>IsDue</c>) — konteyner 24 soatda
    /// o'ladi, ya'ni oldindan yaratish postni jimgina yo'qotardi (§5.2).</summary>
    [Fact]
    public async Task Vaqti_kelmagan_post_olinmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db, minutes: +120);
        var (svc, handler) = Build(db);

        var (ok, processed, _) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(0, processed);
        Assert.Empty(handler.Requests);
        Assert.Equal(IgPublishConst.StScheduled, (await ReloadAsync(db, post.Id)).Status);
    }

    /// <summary>⚠️ KUNLIK LIMIT to'lgan bo'lsa post <c>scheduled</c> bo'lib QOLADI va urinishlar
    /// hisobi ham OSHMAYDI — limit sutkalik va o'zi bo'shaydi. `failed` qilinsa limit tugagan
    /// kuni butun navbat "xato" bo'lib yonardi.</summary>
    [Fact]
    public async Task Limit_tolgan_bolsa_post_scheduled_boladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.LimitBody = """{"data":[{"quota_usage":50,"config":{"quota_total":50}}]}""";

        await svc.ProcessDueAsync(CancellationToken.None);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StScheduled, saved.Status);
        Assert.Equal(0, saved.Attempts);
        Assert.Contains("limit", saved.Error);
        // Konteyner YARATILMAGAN — limit to'lgani konteyner bosqichidayoq to'xtatadi.
        Assert.Equal(0, handler.CreateCalls);
    }

    /// <summary>⚠️ Limit NOMA'LUM bo'lsa (<c>quota_total</c> yo'q) ish TO'XTAMAYDI — taxminiy
    /// 50/100 kodga yozilmagan, haqiqiy limitni Meta o'zi qo'llaydi.</summary>
    [Fact]
    public async Task Nomalum_limit_ishni_toxtatmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.LimitBody = """{"data":[{"quota_usage":7}]}""";

        await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(IgPublishConst.StPublished, (await ReloadAsync(db, post.Id)).Status);
    }

    /// <summary>
    /// ⚠️ <c>IN_PROGRESS</c> — post <c>processing</c> da QOLADI va worker BLOKLANMAYDI:
    /// ichkarida <c>Task.Delay(300s)</c> yo'q, keyingi so'rov keyingi tsiklda qilinadi.
    /// <para>Ikkinchi chaqiruv darhol qilinsa konteyner holati QAYTA so'ralmaydi —
    /// 30 → 60 → 120 → 300 s jadvali aynan shu (Meta "daqiqada bir marta" deydi).</para>
    /// </summary>
    [Fact]
    public async Task In_progress_konteyner_workerni_bloklamaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.StatusBody = """{"status_code":"IN_PROGRESS","status":"Media is being processed"}""";

        var watch = Stopwatch.StartNew();
        await svc.ProcessDueAsync(CancellationToken.None);
        watch.Stop();

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StProcessing, saved.Status);
        Assert.Equal(IgPublishConst.CsInProgress, saved.ContainerStatus);
        Assert.Equal(0, handler.PublishCalls);
        Assert.Equal(1, handler.StatusCalls);
        // Poll jadvali 30 soniyadan boshlanadi — tsikl unga TENG kutmaydi.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"tsikl juda uzoq: {watch.Elapsed}");

        // Ikkinchi tsikl DARHOL: navbatdagi so'rov vaqti hali kelmagan.
        await svc.ProcessDueAsync(CancellationToken.None);
        Assert.Equal(1, handler.StatusCalls);
        Assert.Equal(IgPublishConst.StProcessing, (await ReloadAsync(db, post.Id)).Status);
    }

    /// <summary>Konteyner tayyor bo'lsa post chop etiladi: media id, havola va vaqt yoziladi.</summary>
    [Fact]
    public async Task Tayyor_konteyner_chop_etiladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);

        var (ok, processed, _) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(1, processed);
        Assert.Equal(1, handler.PublishCalls);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StPublished, saved.Status);
        Assert.Equal("media-1", saved.MediaId);
        Assert.Equal("https://www.instagram.com/p/abc/", saved.Permalink);
        Assert.NotEqual("", saved.PublishedAt);
        Assert.Equal("", saved.Error);
    }

    /// <summary>⚠️ Chop etilgan post ikkinchi tsiklda QAYTA olinmaydi — dublikat postni API
    /// orqali o'chirib bo'lmaydi (§5.9.1).</summary>
    [Fact]
    public async Task Chop_etilgan_post_qayta_joylanmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        AddPost(db);
        var (svc, handler) = Build(db);

        await svc.ProcessDueAsync(CancellationToken.None);
        await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, handler.PublishCalls);
    }

    /// <summary>⚠️ Uchinchi urinishdan keyin post <c>failed</c> bo'ladi — cheksiz urinish
    /// kunlik kvotani bo'sh so'rovlarga sarflardi.</summary>
    [Fact]
    public async Task Uch_urinishdan_keyin_failed()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.CreateStatus = HttpStatusCode.BadRequest;
        handler.CreateBody = """{"error":{"message":"Media download failed","code":100,"error_subcode":2207052}}""";

        for (var i = 0; i < IgPublishConst.MaxAttempts - 1; i++)
        {
            await svc.ProcessDueAsync(CancellationToken.None);
            var mid = await ReloadAsync(db, post.Id);
            // Chegaraga yetmaguncha post NAVBATDA qoladi (jimgina yo'qolmaydi).
            Assert.Equal(IgPublishConst.StScheduled, mid.Status);
            Assert.Equal(i + 1, mid.Attempts);
        }

        await svc.ProcessDueAsync(CancellationToken.None);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StFailed, saved.Status);
        Assert.Equal(IgPublishConst.MaxAttempts, saved.Attempts);
        // Xato kodi o'zbekcha matnga aylangan (2207052 — eng ko'p uchraydigan sabab).
        Assert.Contains("yuklab", saved.Error);
        Assert.Equal("", saved.ContainerId);
    }

    /// <summary>⚠️ BUZUQ <c>MediaJson</c> — istisno EMAS, tushunarli sabab bilan DARHOL
    /// <c>failed</c> (qayta urinish o'zi tuzatmaydi) va tarmoqqa umuman chiqilmaydi.</summary>
    [Fact]
    public async Task Buzuq_media_json_tushunarli_sabab_bilan_failed()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db, mediaJson: "{ buzuq json");
        var (svc, handler) = Build(db);

        var (ok, _, _) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.True(ok);   // navbat yiqilmadi
        Assert.Empty(handler.Requests);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StFailed, saved.Status);
        Assert.Contains("buzilgan", saved.Error);
    }

    /// <summary>⚠️ VALIDATSIYADAN o'tmagan post uchun tarmoqqa UMUMAN chiqilmaydi: PNG rasm
    /// Meta'da baribir <c>2207005</c> bilan qaytardi, lekin 10 daqiqalik poll'dan SO'NG.</summary>
    [Theory]
    [InlineData("https://cdn.example.com/rasm.png", "JPEG")]
    [InlineData("http://cdn.example.com/rasm.jpg", "HTTPS")]
    public async Task Validatsiyadan_otmagan_post_tarmoqqa_chiqmaydi(string url, string expect)
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db, mediaJson: MediaJson(url));
        var (svc, handler) = Build(db);

        await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Empty(handler.Requests);
        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StFailed, saved.Status);
        Assert.Contains(expect, saved.Error);
    }

    /// <summary>⚠️ TOKEN o'lik bo'lsa (§5.9.5 — kechasi ishga tushgan job'ning eng ko'p
    /// uchraydigan nosozligi) tashqariga so'rov ketmaydi, post `failed` HAM qilinmaydi:
    /// sabab ulanishda, postda emas.</summary>
    [Fact]
    public async Task Olik_token_bilan_navbat_toxtaydi_lekin_post_yiqilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db, tokenAlive: false);
        var post = AddPost(db);
        var (svc, handler) = Build(db);

        var (ok, processed, error) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(0, processed);
        Assert.Contains("token", error.ToLowerInvariant());
        Assert.Empty(handler.Requests);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StScheduled, saved.Status);
        Assert.Equal(0, saved.Attempts);
        Assert.Contains("token", saved.Error.ToLowerInvariant());
    }

    /// <summary>Akkaunt umuman ulanmagan bo'lsa — tushunarli o'zbekcha xato (500 EMAS).</summary>
    [Fact]
    public async Task Akkaunt_ulanmagan_bolsa_tushunarli_xato()
    {
        using var db = TestDb.Sqlite();
        db.Context.CenterMeta.Add(new CenterMeta { InstagramPublishEnabled = true });
        db.Context.SaveChanges();
        var post = AddPost(db);
        var (svc, handler) = Build(db);

        var (ok, _, error) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("ulanmagan", error);
        Assert.Empty(handler.Requests);

        var (nowOk, nowError) = await svc.PublishNowAsync(post.Id, CancellationToken.None);
        Assert.False(nowOk);
        Assert.Contains("ulanmagan", nowError);
    }

    /// <summary>KARUSEL: avval BOLALAR, keyin OTA-ONA konteyneri (jami 3 ta yaratish so'rovi),
    /// keyin bitta chop etish.</summary>
    [Fact]
    public async Task Karusel_bolalar_va_ota_ona_konteyneri_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var media = """
        [{"url":"https://cdn.example.com/1.jpg","kind":"image"},
         {"url":"https://cdn.example.com/2.jpg","kind":"image"}]
        """;
        var post = AddPost(db, mediaJson: media, type: IgPublishConst.TypeCarousel);
        var (svc, handler) = Build(db);

        await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(3, handler.CreateCalls);   // 2 bola + 1 ota-ona
        Assert.Equal(1, handler.PublishCalls);
        Assert.Equal(IgPublishConst.StPublished, (await ReloadAsync(db, post.Id)).Status);
    }

    /// <summary>⚠️ Konteyner <c>ERROR</c> bo'lsa sabab <c>status</c> MATNIDAN o'qiladi (xato
    /// kodi HTTP javobida emas, matn ichida keladi) va o'zbekchaga aylanadi.</summary>
    [Fact]
    public async Task Konteyner_xatosi_ozbekcha_sababga_aylanadi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.StatusBody = """{"status_code":"ERROR","status":"Error: 2207009 - Aspect ratio"}""";

        await svc.ProcessDueAsync(CancellationToken.None);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(0, handler.PublishCalls);
        Assert.Equal(IgPublishConst.StScheduled, saved.Status);   // 1-urinish, chegara 3
        Assert.Equal(1, saved.Attempts);
        Assert.Contains("nisbati", saved.Error);
    }

    /// <summary>⚠️ Konteyner allaqachon <c>PUBLISHED</c> bo'lsa IKKINCHI marta chop
    /// ETILMAYDI — dublikat postni o'chirib bo'lmaydi.</summary>
    [Fact]
    public async Task Allaqachon_published_konteyner_qayta_chop_etilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.StatusBody = """{"status_code":"PUBLISHED","status":"Published"}""";

        await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(0, handler.PublishCalls);
        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StPublished, saved.Status);
        Assert.Contains("tekshiring", saved.Error);
    }

    /// <summary>⚠️ <c>media_publish</c> XATOSIDA avtomatik qayta urinish YO'Q: post darhol
    /// <c>failed</c> bo'ladi va matnda "Instagram'da tekshiring" turadi (post joylangan
    /// bo'lishi ham mumkin, uni API orqali o'chirib bo'lmaydi).</summary>
    [Fact]
    public async Task Chop_etish_xatosida_qayta_urinilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, handler) = Build(db);
        handler.PublishBody = """{"error":{"message":"Unknown","code":1}}""";

        await svc.ProcessDueAsync(CancellationToken.None);

        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StFailed, saved.Status);
        Assert.Equal(IgPublishConst.MaxAttempts, saved.Attempts);
        Assert.Contains("tekshiring", saved.Error);

        // Keyingi tsiklda ham qayta urinilmaydi.
        await svc.ProcessDueAsync(CancellationToken.None);
        Assert.Equal(1, handler.PublishCalls);
    }

    /// <summary>«Hoziroq joylash»: vaqti kelmagan post ham darhol joylanadi va urinishlar
    /// hisobi NOLDAN boshlanadi.</summary>
    [Fact]
    public async Task Hoziroq_joylash_vaqtni_kutmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db, minutes: +600);
        db.Context.IgScheduledPosts.First(p => p.Id == post.Id).Attempts = 2;
        db.Context.SaveChanges();
        var (svc, _) = Build(db);

        var (ok, error) = await svc.PublishNowAsync(post.Id, CancellationToken.None);

        Assert.True(ok, error);
        var saved = await ReloadAsync(db, post.Id);
        Assert.Equal(IgPublishConst.StPublished, saved.Status);
        Assert.Equal(0, saved.Attempts);
    }

    /// <summary>Joylangan postni qayta joylab bo'lmaydi (dublikat himoyasi).</summary>
    [Fact]
    public async Task Joylangan_postni_qayta_joylab_bolmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var post = AddPost(db);
        var (svc, _) = Build(db);

        await svc.ProcessDueAsync(CancellationToken.None);
        var (ok, error) = await svc.PublishNowAsync(post.Id, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("allaqachon", error);
    }

    /// <summary>Bir tsiklda ko'pi bilan <see cref="IgPublishConst.QueueBatch"/> ta post olinadi —
    /// katta navbat kunlik kvotani bir zumda yeb qo'ymasin.</summary>
    [Fact]
    public async Task Bir_tsiklda_kopi_bilan_uchta_post()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        for (var i = 0; i < 5; i++) AddPost(db, minutes: -(i + 1));
        var (svc, _) = Build(db);

        var (_, processed, _) = await svc.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(IgPublishConst.QueueBatch, processed);
        Assert.Equal(2, await db.Context.IgScheduledPosts
            .CountAsync(p => p.Status == IgPublishConst.StScheduled));
    }
}

/// <summary>
/// <see cref="IgPublishPayload"/> — SOF funksiyalar: saqlangan JSON'ni o'qish.
///
/// <para>⚠️ Nega alohida test: .NET 8 dagi System.Text.Json <c>record</c> konstruktorining
/// STANDART qiymatini e'tiborsiz qoldiradi. Shu sabab oraliq o'zgaruvchan sinf ishlatiladi va
/// aynan shu yerda qulflanadi — aks holda <c>thumbOffsetMs</c> "berilmagan" (-1) o'rniga
/// 0 bo'lib, Meta'ga ortiqcha parametr ketardi.</para>
/// </summary>
public class IgPublishPayloadTests
{
    [Fact]
    public void Bosh_json_bosh_royxat() =>
        Assert.Empty(IgPublishPayload.ReadMedia("").Items);

    [Fact]
    public void Buzuq_json_istisno_otmaydi()
    {
        var (ok, items, error) = IgPublishPayload.ReadMedia("{ buzuq");

        Assert.False(ok);
        Assert.Empty(items);
        Assert.Contains("buzilgan", error);
    }

    /// <summary>Yo'q maydonlar STANDART qiymatini oladi: <c>kind=image</c>,
    /// <c>thumbOffsetMs=-1</c> ("berilmagan"), satrlar esa hech qachon <c>null</c> emas.</summary>
    [Fact]
    public void Yoq_maydonlar_standart_qiymatga_tushadi()
    {
        var (ok, items, _) = IgPublishPayload.ReadMedia("""[{"url":"https://a/b.jpg"}]""");

        Assert.True(ok);
        var one = Assert.Single(items);
        Assert.Equal(IgPublishConst.KindImage, one.Kind);
        Assert.Equal(-1, one.ThumbOffsetMs);
        Assert.Equal("", one.AltText);
        Assert.Equal("", one.CoverUrl);
        Assert.Equal("", one.Caption);
    }

    /// <summary>0 — HAQIQIY <c>thumb_offset</c> (videoning birinchi kadri), "berilmagan" emas.</summary>
    [Fact]
    public void Nol_thumb_offset_saqlanadi()
    {
        var (_, items, _) = IgPublishPayload.ReadMedia(
            """[{"url":"https://a/b.mp4","kind":"video","thumbOffsetMs":0}]""");

        Assert.Equal(0, items[0].ThumbOffsetMs);
    }

    /// <summary>Buzuq SOZLAMA postni yiqitmaydi — standart sozlama qaytadi (media'dan farqli,
    /// bu yerda yo'qoladigan ma'lumot yo'q).</summary>
    [Fact]
    public void Buzuq_sozlama_standartga_qaytadi()
    {
        var options = IgPublishPayload.ReadOptions("{ buzuq");

        Assert.True(options.ShareToFeed);
        Assert.Equal("", options.LocationId);
    }

    [Fact]
    public void Bosh_hammuallif_tashlanadi()
    {
        var options = IgPublishPayload.ReadOptions(
            """{"shareToFeed":false,"collaborators":["ali","  ",""],"audioName":" Yozgi "}""");

        Assert.False(options.ShareToFeed);
        Assert.Equal(new[] { "ali" }, options.Collaborators);
        Assert.Equal("Yozgi", options.AudioName);
    }

    /// <summary>Yozib-o'qish davri: saqlangan JSON qaytib o'qilganda AYNAN o'sha qiymatlar.</summary>
    [Fact]
    public void Yozib_oqish_davri_buzilmaydi()
    {
        var json = IgPublishPayload.WriteMedia(new List<IgMediaJson>
        {
            new() { Url = "https://a/b.mp4", Kind = "video", DurationSeconds = 12, Width = 1080, Height = 1920 },
        });

        var (ok, items, _) = IgPublishPayload.ReadMedia(json);

        Assert.True(ok);
        Assert.Equal(IgPublishConst.KindVideo, items[0].Kind);
        Assert.Equal(12, items[0].DurationSeconds);
        Assert.Equal(1920, items[0].Height);
    }
}
