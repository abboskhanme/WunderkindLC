using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Instagram <b>Content Publishing</b> API mijozi (konteyner yaratish → holat → chop etish).
///
/// <para><b>Uslub — <see cref="InstagramApi"/> bilan AYNAN bir xil: ISTISNO OTILMAYDI.</b>
/// Har metod <c>(Ok, …, Error)</c> qaytaradi, <c>Error</c> — foydalanuvchiga ko'rsatiladigan
/// O'ZBEKCHA matn. Chaqiruvchi fon xizmati (<c>InstagramWorkerService</c>) bo'lgani uchun
/// bitta tarmoq uzilishi butun navbatni yiqitmasligi kerak.</para>
///
/// <para><b>🔴 Rejalashtirish bu yerda YO'Q.</b> <c>scheduled_publish_time</c> parametri
/// Instagram'da mavjud emas — vaqt bizning navbatimizda (<c>IgScheduledPost.ScheduledAt</c>),
/// konteyner esa faqat chop etish payti yaratiladi (<see cref="InstagramPublishContract"/>
/// sinf izohi).</para>
///
/// <para><b>⚠️ <c>/me</c> ga TAYANMAYMIZ.</b> Overview'da <c>/me</c> aliasi tasdiqlangan, lekin
/// endpoint reference'da <c>/{ig-user-id}/media</c> yozilgan va ikkisi zid. Shuning uchun har
/// chaqiruvda <c>IgAccount.IgUserId</c> dan kelgan ANIQ yo'l ishlatiladi; u bo'sh bo'lsa metod
/// tarmoqqa umuman chiqmaydi va tushunarli xato beradi.</para>
///
/// <para><b>Log:</b> so'rov MANZILI hech qachon logga yozilmaydi — <c>access_token</c> GET
/// so'rovlarida query'da ketadi (Graph boshqa yo'lni qo'llab-quvvatlamaydi). POST'larda esa
/// token ATAYIN forma tanasiga qo'yilgan.</para>
///
/// <para>DI: <c>builder.Services.AddHttpClient&lt;InstagramPublishApi&gt;();</c></para>
/// </summary>
public sealed class InstagramPublishApi(HttpClient http, ILogger<InstagramPublishApi> logger)
{
    private const int MaxAttempts = 3;

    /* Chiquvchi throttle — ketma-ket ikki so'rov orasi kamida 1 soniya.
       ⚠️ Bu hisob InstagramApi'nikidan ALOHIDA (u yerdagi gate `private`, mavjud faylga esa
       tegilmaydi). Ya'ni ikkala mijoz bir vaqtda ishlasa umumiy tezlik 2 so'rov/soniyagacha
       chiqishi mumkin. Amalda xavfsiz: chop etish siyrak (bir postga 3-5 so'rov), izoh/DM esa
       doimiy. Agar kelajakda muammo bo'lsa — gate umumiy yordamchiga chiqariladi. */
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastSentUtc = DateTime.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    /* ═════════════════════════ 1) Konteyner yaratish ═════════════════════════ */

    /// <summary>
    /// <c>POST /{ig-user-id}/media</c> — media konteynerini yaratadi (hali chop etilmaydi).
    /// <para>Parametrlar to'plami SOF funksiyada quriladi
    /// (<see cref="InstagramPublishContract.BuildContainerRequest"/>) — "qaysi parametr qaysi
    /// turga tegishli" qoidasi HTTP kodida takrorlanmaydi.</para>
    /// <para>Karusel: avval har bola <c>is_carousel_item=true</c> bilan yaratiladi, keyin
    /// <see cref="InstagramPublishContract.BuildCarouselParent"/> bilan ota-ona.</para>
    /// </summary>
    public async Task<(bool Ok, string ContainerId, string Error)> CreateContainerAsync(
        string igUserId, string token, IgContainerRequest payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(igUserId))
            return (false, "", "Instagram akkaunt id'si noma'lum — akkauntni qayta ulang.");
        if (payload is null)
            return (false, "", "Post ma'lumotlari bo'sh.");

        var form = new Dictionary<string, string> { ["access_token"] = token ?? "" };

        void Put(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) form[key] = value!;
        }

        Put("media_type", payload.MediaType);
        Put("image_url", payload.ImageUrl);
        Put("video_url", payload.VideoUrl);
        Put("caption", payload.Caption);
        Put("cover_url", payload.CoverUrl);
        Put("alt_text", payload.AltText);
        Put("location_id", payload.LocationId);
        Put("audio_name", payload.AudioName);

        // ⚠️ `thumb_offset` 0 ham HAQIQIY qiymat (videoning birinchi kadri), shuning uchun
        // "berilmagan" belgisi -1: `Put` bo'sh satrni tashlab yuboradi, 0 esa tushib qolardi.
        if (payload.ThumbOffsetMs >= 0)
            form["thumb_offset"] = payload.ThumbOffsetMs.ToString();

        // `share_to_feed` FAQAT reels uchun ma'noli — boshqa turda yuborilsa Graph `code 100` berishi mumkin.
        if (payload.MediaType == IgPublishConst.MtReels)
            form["share_to_feed"] = payload.ShareToFeed ? "true" : "false";

        if (payload.IsCarouselItem)
            form["is_carousel_item"] = "true";

        if (payload.Children is { Count: > 0 })
            form["children"] = string.Join(",", payload.Children);

        if (payload.Collaborators is { Count: > 0 })
        {
            var (colOk, colErr) = InstagramPublishContract.ValidateCollaborators(payload.Collaborators);
            if (!colOk) return (false, "", colErr);
            // Meta JSON massiv kutadi: ["user1","user2"].
            form["collaborators"] = JsonSerializer.Serialize(payload.Collaborators);
        }

        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(igUserId)}/media";
        var (ok, body, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            retry: true, ct);
        if (!ok) return (false, "", err);

        var id = ReadId(body);
        return id.Length > 0
            ? (true, id, "")
            : (false, "", "Instagram post konteynerini qaytarmadi (kutilmagan javob).");
    }

    /* ═════════════════════════ 2) Konteyner holati ═════════════════════════ */

    /// <summary>
    /// <c>GET /{container-id}?fields=status_code,status</c>.
    /// <para><c>status_code</c>: <c>IN_PROGRESS | FINISHED | ERROR | EXPIRED | PUBLISHED</c>,
    /// <c>status</c> — erkin matn, xato bo'lsa ichida kod turadi
    /// (<c>"Error: 2207020 - …"</c>) va u
    /// <see cref="InstagramPublishContract.ContainerErrorText"/> bilan o'qiladi.</para>
    /// <para>Keyingi so'rovgacha kutish — <see cref="InstagramPublishContract.NextPollDelaySeconds"/>
    /// (30 → 60 → 120 → 300 s), 10 daqiqadan keyin post <c>failed</c>.</para>
    /// </summary>
    public async Task<(bool Ok, string StatusCode, string Status, string Error)> GetContainerStatusAsync(
        string containerId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            return (false, "", "", "Post konteyneri id'si bo'sh.");

        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(containerId)}?fields=status_code,status" +
                  $"&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), retry: true, ct);
        if (!ok) return (false, "", "", err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var code = InstagramPublishContract.NormalizeContainerStatus(Str(doc.RootElement, "status_code"));
            return (true, code, Str(doc.RootElement, "status"), "");
        }
        catch (JsonException)
        {
            return (false, "", "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /* ═════════════════════════ 3) Chop etish ═════════════════════════ */

    /// <summary>
    /// <c>POST /{ig-user-id}/media_publish?creation_id=…</c> — postni Instagram'ga joylaydi.
    ///
    /// <para>⚠️ <b>QAYTA URINISH YO'Q</b> (<c>retry: false</c>) — bu metod boshqalardan shu bilan
    /// farq qiladi. Sabab: Meta postni joylab bo'lib javobni yetkaza olmagan bo'lsa (5xx/timeout),
    /// avtomatik takror <b>IKKINCHI POST</b> yaratardi, chop etilgan IG media'ni esa API orqali
    /// <b>o'chirib ham, tahrirlab ham bo'lmaydi</b> (§5.9). Ya'ni xatoning narxi — profilda
    /// abadiy qoladigan dublikat. Shuning uchun noaniq holatda qayta urinishni ODAM hal qiladi
    /// («Qayta urinish» tugmasi).</para>
    ///
    /// <para>Kunlik limit ham AYNAN shu bosqichda tekshiriladi (konteyner yaratishda emas) —
    /// <see cref="GetPublishingLimitAsync"/>.</para>
    /// </summary>
    public async Task<(bool Ok, string MediaId, string Error)> PublishAsync(
        string igUserId, string token, string containerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(igUserId))
            return (false, "", "Instagram akkaunt id'si noma'lum — akkauntni qayta ulang.");
        if (string.IsNullOrWhiteSpace(containerId))
            return (false, "", "Post konteyneri id'si bo'sh.");

        var form = new Dictionary<string, string>
        {
            ["creation_id"] = containerId,
            ["access_token"] = token ?? "",
        };

        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(igUserId)}/media_publish";
        var (ok, body, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            retry: false, ct);
        if (!ok) return (false, "", err);

        var id = ReadId(body);
        return id.Length > 0
            ? (true, id, "")
            : (false, "", "Instagram post id'sini qaytarmadi (kutilmagan javob).");
    }

    /* ═════════════════════════ 4) Kunlik limit ═════════════════════════ */

    /// <summary>
    /// <c>GET /{ig-user-id}/content_publishing_limit?fields=config,quota_usage</c>.
    ///
    /// <para>⚠️ <b><c>quota_total</c> KODGA YOZILMAYDI.</b> Meta hujjatlari zid: qo'llanmada
    /// 24 soatda 100 post, reference namunasida 50. Shuning uchun qiymat FAQAT shu javobdagi
    /// <c>config.quota_total</c> dan olinadi. Maydon bo'lmasa 0 ("noma'lum") qaytadi va
    /// <see cref="InstagramPublishContract.QuotaExceeded"/> postni to'xtatmaydi — taxminiy
    /// limit tufayli ishlaydigan postni bloklagandan ko'ra, Meta'ning <c>2207042</c> xatosini
    /// olib, uni tushunarli matnga aylantirgan to'g'riroq.</para>
    ///
    /// <para>Karusel — 1 post deb sanaladi.</para>
    /// </summary>
    public async Task<(bool Ok, int QuotaUsage, int QuotaTotal, string Error)> GetPublishingLimitAsync(
        string igUserId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(igUserId))
            return (false, 0, 0, "Instagram akkaunt id'si noma'lum — akkauntni qayta ulang.");

        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(igUserId)}/content_publishing_limit" +
                  $"?fields=config,quota_usage&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), retry: true, ct);
        if (!ok) return (false, 0, 0, err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // Javob `data[]` massivi ichida keladi.
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                if (data.GetArrayLength() == 0)
                    return (true, 0, IgPublishConst.UnknownQuota, "");
                root = data[0];
            }

            var usage = Int(root, "quota_usage");
            var total = IgPublishConst.UnknownQuota;
            if (root.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
                total = Int(cfg, "quota_total");

            return (true, usage, total, "");
        }
        catch (JsonException)
        {
            return (false, 0, 0, "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /* ═════════════════════════ 5) Permalink (ixtiyoriy) ═════════════════════════ */

    /// <summary>
    /// <c>GET /{media-id}?fields=permalink</c> — chop etilgan postga havola.
    /// <para>Ixtiyoriy: havola olinmasa post baribir <c>published</c> bo'ladi (media id bor),
    /// UI'da faqat "Instagram'da ochish" tugmasi ko'rinmaydi. Shuning uchun chaqiruvchi bu
    /// metodning xatosini post holatiga TA'SIR QILDIRMASLIGI kerak.</para>
    /// </summary>
    public async Task<(bool Ok, string Permalink, string Error)> GetMediaPermalinkAsync(
        string mediaId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return (false, "", "Post id'si bo'sh.");

        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(mediaId)}?fields=permalink" +
                  $"&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), retry: true, ct);
        if (!ok) return (false, "", err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return (true, Str(doc.RootElement, "permalink"), "");
        }
        catch (JsonException)
        {
            return (false, "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /* ═════════════════════════ Ichki qism ═════════════════════════ */

    /// <summary>So'rovni yuboradi: throttle + (shartli) retry + xatoni o'zbekcha matnga aylantirish.
    /// <paramref name="factory"/> — HAR urinishda YANGI <c>HttpRequestMessage</c>.</summary>
    private async Task<(bool Ok, string Body, string Error)> SendAsync(
        Func<HttpRequestMessage> factory, bool retry, CancellationToken ct)
    {
        var maxAttempts = retry ? MaxAttempts : 1;
        var delayMs = 1000;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ThrottleAsync(ct);
                using var req = factory();
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return (true, body, "");

                var (code, sub, msg) = ParseError(body);
                // ⚠️ Manzil LOGGA YOZILMAYDI — GET so'rovlarida access_token query'da ketadi.
                logger.LogWarning("Instagram publish API xato ({Status}/{Code}/{Sub}): {Msg}",
                    (int)resp.StatusCode, code, sub, msg);

                if (retry && IsRetryable(resp.StatusCode, code) && attempt < maxAttempts)
                {
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                    continue;
                }
                return (false, body, MapError((int)resp.StatusCode, code, sub, msg));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (false, "", "So'rov bekor qilindi.");
            }
            catch (TaskCanceledException)
            {
                if (attempt < maxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                return (false, "", "Instagram javob bermadi (vaqt tugadi) — keyinroq qayta urinamiz.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                return (false, "", $"Tarmoq xatosi: {ex.Message}");
            }
        }
    }

    /// <summary>Ketma-ket so'rovlar orasida kamida 1 soniya.</summary>
    private static async Task ThrottleAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wait = MinInterval - (DateTime.UtcNow - _lastSentUtc);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastSentUtc = DateTime.UtcNow;
        }
        finally { Gate.Release(); }
    }

    private static bool IsRetryable(HttpStatusCode status, int code)
    {
        if (status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout) return true;
        // Meta rate-limit kodlari (app/user/custom) — vaqtinchalik.
        return code is 4 or 17 or 32 or 613 or 2;
    }

    /// <summary>
    /// Meta xatosini O'ZBEKCHA matnga aylantiradi.
    ///
    /// <para>Tartib ATAYIN shunday: avval <b>publishing</b> kodi (<c>2207xxx</c>) qidiriladi —
    /// u <c>error_subcode</c> da, ba'zan esa faqat <c>message</c> MATNI ichida keladi. Topilsa
    /// <see cref="InstagramPublishContract.ErrorText"/> ishlatiladi. Topilmasa umumiy Graph
    /// kodlari (token, ruxsat, rate limit) xaritasiga tushadi.</para>
    /// </summary>
    private static string MapError(int httpStatus, int code, int sub, string msg)
    {
        var pub = sub;
        if (pub is < 2207000 or > 2207999) pub = InstagramPublishContract.ExtractErrorCode(msg);
        if (pub != 0) return InstagramPublishContract.ErrorText(pub, msg);

        return code switch
        {
            190 => "Token muddati tugagan yoki bekor qilingan — akkauntni qayta ulang.",
            4 or 17 or 32 or 613 => "Instagram so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.",
            10 or 200 => "Ruxsat yetishmaydi — akkauntni qayta ulab, kontent joylash ruxsatini bering "
                         + "(instagram_business_content_publish).",
            100 => $"Noto'g'ri so'rov parametri: {msg}",
            _ => InstagramPublishContract.ErrorText(0, msg.Length > 0 ? msg : $"HTTP {httpStatus}"),
        };
    }

    private static (int Code, int Sub, string Message) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
                return (0, 0, "");
            var code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            var sub = e.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : 0;
            var msg = Str(e, "message");
            // `error_user_msg` odatda aniqroq sabab yozadi (masalan "Media download failed").
            var userMsg = Str(e, "error_user_msg");
            if (userMsg.Length > 0) msg = msg.Length > 0 ? $"{msg} ({userMsg})" : userMsg;
            return (code, sub, msg);
        }
        catch (JsonException) { return (0, 0, ""); }
    }

    /// <summary>Javobdagi <c>id</c> (konteyner yoki media).</summary>
    private static string ReadId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Str(doc.RootElement, "id");
        }
        catch (JsonException) { return ""; }
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => "",
        };
    }

    private static int Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
            JsonValueKind.String => int.TryParse(v.GetString(), out var s) ? s : 0,
            _ => 0,
        };
    }
}
