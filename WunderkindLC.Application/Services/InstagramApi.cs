using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Instagram Graph API mijozi (OAuth + izohga javob + private reply + DM + media).
///
/// <para><b>Uslub — loyihadagi <see cref="GeminiService"/> bilan bir xil: ISTISNO OTILMAYDI.</b>
/// Har metod <c>(Ok, …, Error)</c> qaytaradi, <c>Error</c> — foydalanuvchiga ko'rsatiladigan
/// O'ZBEKCHA matn. Sabab: bu chaqiruvlar fon xizmatidan turadi va bitta tarmoq uzilishi butun
/// navbatni yiqitmasligi kerak.</para>
///
/// <para><b>Chiquvchi throttle (IG-SPEC §4.1):</b> ketma-ket ikki so'rov orasi kamida 1 soniya —
/// holat STATIK, ya'ni butun ilova bo'yicha (NUR'da har worker o'z hisobini yuritgani uchun
/// amalda 2 so'rov/soniya chiqib ketgan).</para>
///
/// <para><b>Retry:</b> faqat VAQTINCHALIK xatolarda (429/5xx/tarmoq, Meta rate-limit kodlari) —
/// 1s → 2s → 4s. Token yaroqsiz (190) yoki ruxsat yetishmasa (10/200) qayta urinilmaydi: bu
/// odam aralashuvini talab qiladigan doimiy xato.</para>
///
/// <para>DI: <c>builder.Services.AddHttpClient&lt;InstagramApi&gt;();</c></para>
/// </summary>
public sealed class InstagramApi(HttpClient http, ILogger<InstagramApi> logger)
{
    private const int MaxAttempts = 3;

    // Butun ilova bo'yicha yagona "oxirgi so'rov" hisobi (throttle).
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastSentUtc = DateTime.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /* ═════════════════════════ OAuth ═════════════════════════ */

    /// <summary>
    /// Authorize URL (SOF funksiya — tarmoq yo'q). <paramref name="redirectUri"/> Meta'da
    /// ro'yxatga olingani bilan AYNAN bir xil bo'lishi shart (oxirida <c>/</c> yo'q), aks holda
    /// Instagram <c>Invalid redirect_uri</c> beradi.
    /// </summary>
    /// <param name="scopes">So'raladigan ruxsatlar. Bo'sh bo'lsa <see cref="IgConst.Scopes"/>
    /// (kontent joylashsiz asosiy ro'yxat). ⚠️ Meta ilovada YOQILMAGAN scope so'ralsa butun
    /// authorize so'rovi rad etiladi — shuning uchun ro'yxat chaqiruvchida, modul bayrog'iga
    /// qarab quriladi (<see cref="IgConst.ScopesFor"/>).</param>
    public static string BuildAuthorizeUrl(string appId, string redirectUri, string state, string? scopes = null) =>
        $"{IgConst.AuthorizeUrl}?client_id={Uri.EscapeDataString(appId ?? "")}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri ?? "")}" +
        $"&response_type=code&scope={Uri.EscapeDataString(string.IsNullOrWhiteSpace(scopes) ? IgConst.Scopes : scopes)}" +
        $"&state={Uri.EscapeDataString(state ?? "")}";

    /// <summary>
    /// [4] <c>code</c> → QISQA muddatli token (1 soat) + akkaunt id.
    /// ⚠️ Javob <c>data[]</c> MASSIVI ichida keladi (to'g'ridan-to'g'ri obyekt emas) — eski
    /// formatdagi javob ham qabul qilinadi.
    /// </summary>
    public async Task<(bool Ok, string ShortToken, string IgUserId, string Error)> ExchangeCodeAsync(
        string appId, string appSecret, string redirectUri, string code, CancellationToken ct)
    {
        // Kod URL'da `#_` fragmenti bilan kelishi mumkin — kesib tashlanadi.
        var clean = (code ?? "").Split('#')[0];
        var form = new Dictionary<string, string>
        {
            ["client_id"] = appId ?? "",
            ["client_secret"] = appSecret ?? "",
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri ?? "",
            ["code"] = clean,
        };

        var (ok, body, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, IgConst.OauthTokenUrl) { Content = new FormUrlEncodedContent(form) },
            ct);
        if (!ok) return (false, "", "", err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                root = data[0];
            var token = Str(root, "access_token");
            var userId = Str(root, "user_id");
            if (token.Length == 0) return (false, "", "", "Instagram token qaytarmadi — App ID/Secret va redirect manzilni tekshiring.");
            return (true, token, userId, "");
        }
        catch (JsonException)
        {
            return (false, "", "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>[5] Qisqa token → UZOQ muddatli (≈60 kun).</summary>
    public async Task<(bool Ok, string LongToken, int ExpiresIn, string Error)> ExchangeLongLivedAsync(
        string appSecret, string shortToken, CancellationToken ct)
    {
        var url = $"{IgConst.GraphRoot}/access_token?grant_type=ig_exchange_token" +
                  $"&client_secret={Uri.EscapeDataString(appSecret ?? "")}" +
                  $"&access_token={Uri.EscapeDataString(shortToken ?? "")}";
        return await TokenCallAsync(url, ct);
    }

    /// <summary>[7] Tokenni yangilash (yana ≈60 kun). Token kamida bir marta ishlatilgan bo'lishi kerak.</summary>
    public async Task<(bool Ok, string Token, int ExpiresIn, string Error)> RefreshTokenAsync(
        string longToken, CancellationToken ct)
    {
        var url = $"{IgConst.GraphRoot}/refresh_access_token?grant_type=ig_refresh_token" +
                  $"&access_token={Uri.EscapeDataString(longToken ?? "")}";
        return await TokenCallAsync(url, ct);
    }

    private async Task<(bool Ok, string Token, int ExpiresIn, string Error)> TokenCallAsync(string url, CancellationToken ct)
    {
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, "", 0, err);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var token = Str(doc.RootElement, "access_token");
            var expires = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 0;
            if (token.Length == 0) return (false, "", 0, "Instagram yangi token qaytarmadi.");
            return (true, token, expires, "");
        }
        catch (JsonException)
        {
            return (false, "", 0, "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>
    /// [6] O'z akkauntimiz. ⚠️ <c>user_id</c> (IGSID) ustun olinadi — webhook'dagi
    /// <c>from.id</c> aynan shu formatda keladi va "o'zimizni tanish" tekshiruvi shunga tayanadi.
    /// U bo'lmasa <c>id</c> ishlatiladi.
    /// </summary>
    /// <remarks>⚠️ <c>id</c> (app-scoped) ham QAYTARILADI: webhook'da <c>from.id</c> ba'zan
    /// <c>user_id</c>, ba'zan <c>id</c> bo'lib keladi va faqat bittasini saqlash halqa
    /// himoyasini teshib qo'yadi (<c>marketing-instagram.md</c> §4).</remarks>
    public async Task<(bool Ok, string IgUserId, string AppScopedId, string Username, string Name, string PictureUrl, string Error)> MeAsync(
        string token, CancellationToken ct)
    {
        var url = $"{IgConst.GraphBase}/me?fields=id,user_id,username,name,account_type,profile_picture_url" +
                  $"&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, "", "", "", "", "", err);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var appScoped = Str(r, "id");
            var userId = Str(r, "user_id");
            if (userId.Length == 0) userId = appScoped;
            if (userId.Length == 0) return (false, "", "", "", "", "", "Instagram akkaunt id'sini aniqlab bo'lmadi.");
            // `id` va `user_id` bir xil bo'lsa app-scoped'ni takrorlashning ma'nosi yo'q.
            if (string.Equals(appScoped, userId, StringComparison.Ordinal)) appScoped = "";
            return (true, userId, appScoped, Str(r, "username"), Str(r, "name"), Str(r, "profile_picture_url"), "");
        }
        catch (JsonException)
        {
            return (false, "", "", "", "", "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>
    /// [8] Webhook obunasi — maydonlar <see cref="IgConst.WebhookFields"/> dan.
    ///
    /// <para><b>🔴 <c>message_echoes</c> OLIB TASHLANDI (2026-08-22).</b> Meta uni qabul
    /// qilmaydi va butun so'rovni rad etadi (<c>IGApiException 100</c>) — ya'ni <c>comments</c>
    /// ham obuna bo'lmasdi. Sabab va echo qanday kelishi: <see cref="IgConst.WebhookFields"/>.</para>
    /// </summary>
    public async Task<(bool Ok, string Error)> SubscribeWebhookAsync(string token, CancellationToken ct)
    {
        // 1) Odatdagi yo'l — hammasi bitta so'rovda (bitta Graph chaqiruvi).
        var (ok, _, err) = await SendAsync(() => SubscribeRequest(IgConst.WebhookFieldsCsv, token), ct);
        if (ok) return (true, "");

        // 2) ZAXIRA — MAYDONMA-MAYDON.
        //
        // 🔴 NEGA KERAK. Meta bitta noto'g'ri nom uchun BUTUN so'rovni rad etadi
        // (`code 100 — Param subscribed_fields[N] must be one of {…}`). 2026-08-22 da aynan
        // shu sodir bo'ldi: ro'yxatda `message_echoes` bor edi va u Meta tomonidan olib
        // tashlangani uchun `comments` HAM obuna bo'lmadi — izohlar oylab kelmadi, nosozlik
        // esa "ulash o'tdi" bo'lib ko'rinardi.
        //
        // Nom o'zgargani bilan TUZILMA o'zgarmasdi: Meta ertaga yana bir nomga tegsa hammasi
        // birdan yiqilardi. Endi bitta maydon rad etilsa QOLGANLARI baribir obuna bo'ladi va
        // xato matnida AYNAN qaysi biri o'tmagani yoziladi.
        var failed = new List<string>();
        var passed = new List<string>();
        foreach (var field in IgConst.WebhookFields)
        {
            var (fOk, _, fErr) = await SendAsync(() => SubscribeRequest(field, token), ct);
            if (fOk) passed.Add(field);
            else failed.Add($"{field} ({fErr})");
        }

        if (failed.Count == 0)
            return (true, "");

        var detail = passed.Count > 0
            ? $"Obuna qisman o'tdi. Muvaffaqiyatli: {string.Join(", ", passed)}. "
            : "Obunaning HECH BIR maydoni o'tmadi. ";
        return (false, detail + $"O'tmadi: {string.Join("; ", failed)}. Birinchi xato: {err}");
    }

    /// <summary>Obuna so'rovi (bitta maydon yoki vergul bilan ajratilgan ro'yxat uchun).</summary>
    private static HttpRequestMessage SubscribeRequest(string fields, string token) =>
        new(HttpMethod.Post,
            $"{IgConst.GraphBase}/me/subscribed_apps?subscribed_fields={Uri.EscapeDataString(fields)}" +
            $"&access_token={Uri.EscapeDataString(token ?? "")}");

    /// <summary>
    /// [8b] Akkaunt HOZIR qaysi maydonlarga obuna — <c>GET /me/subscribed_apps</c>.
    ///
    /// <para><b>Nega kerak:</b> <c>IgAccount.WebhookSubscribed</c> ulanish paytidagi suratcha
    /// va eskirishi mumkin. Diagnostika holatni Meta'dan JONLI o'qishi kerak, aks holda
    /// "hammasi yashil, lekin hodisa kelmayapti" holati sababsiz qolardi
    /// (<see cref="InstagramContract.MissingWebhookFields"/>).</para>
    ///
    /// <para>Javob: <c>{"data":[{"id":"…","subscribed_fields":["comments","messages"]}]}</c>.
    /// Obuna umuman yo'q bo'lsa <c>data</c> BO'SH massiv bo'ladi — bu xato emas, shuning uchun
    /// <c>Ok = true</c> va ro'yxat bo'sh qaytadi.</para>
    /// </summary>
    public async Task<(bool Ok, IReadOnlyList<string> Fields, string Error)> GetSubscribedFieldsAsync(
        string token, CancellationToken ct)
    {
        var url = $"{IgConst.GraphBase}/me/subscribed_apps?access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, Array.Empty<string>(), err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var app in data.EnumerateArray())
                {
                    if (app.ValueKind != JsonValueKind.Object) continue;
                    if (!app.TryGetProperty("subscribed_fields", out var fs) || fs.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var f in fs.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String)
                        {
                            var name = f.GetString() ?? "";
                            if (name.Length > 0 && !list.Contains(name)) list.Add(name);
                        }
                }
            }
            return (true, list, "");
        }
        catch (JsonException)
        {
            return (false, Array.Empty<string>(), "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /* ═════════════════════════ Amallar ═════════════════════════ */

    /// <summary>Izohga OCHIQ javob (post ostida hamma ko'radi).</summary>
    public async Task<(bool Ok, string Error)> ReplyToCommentAsync(
        string commentId, string message, string token, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["message"] = message ?? "",
            ["access_token"] = token ?? "",
        };
        var (ok, _, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"{IgConst.GraphBase}/{Uri.EscapeDataString(commentId ?? "")}/replies")
            {
                Content = new FormUrlEncodedContent(form)
            }, ct);
        return (ok, err);
    }

    /// <summary>
    /// Izoh yozgan odamga YOPIQ javob (private reply).
    /// ⚠️ Har izoh uchun FAQAT BIR MARTA va izohdan keyin 7 kun ichida — takroriy urinish xato
    /// beradi, shuning uchun chaqiruvchi yuborilganini yozib qo'yishi shart.
    /// Manzil <c>recipient.id</c> emas, <c>recipient.comment_id</c>.
    /// </summary>
    public async Task<(bool Ok, string Error)> SendPrivateReplyAsync(
        string commentId, string message, string token, CancellationToken ct)
    {
        var payload = new
        {
            recipient = new { comment_id = commentId ?? "" },
            // ⚠️ Bu yerda ham BAYT bo'yicha kesiladi (`SendDmAsync` bilan bir xil): ilgari
            // private reply matni UMUMAN kesilmasdi va API qatlami asimmetrik edi — yangi
            // chaqiruvchi qo'shilsa 1000 baytdan oshib, javob jimgina yiqilardi.
            message = new { text = InstagramContract.TrimBytes(message, IgConst.MaxReplyBytes) },
        };
        return await PostMessagesAsync("me", payload, token, ct);
    }

    /// <summary>
    /// Oddiy DM. ⚠️ Chaqirishdan OLDIN 24 soatlik oyna tekshirilgan bo'lishi kerak
    /// (<see cref="InstagramContract.DmWindowOpen"/>) — bu yerda faqat HTTP bajariladi.
    /// </summary>
    /// <param name="humanAgent">🔴 FAQAT operator qo'lda yozganda <c>true</c>. Meta'ga
    /// <c>messaging_type=MESSAGE_TAG</c> · <c>tag=HUMAN_AGENT</c> qo'shiladi va javob 24 soatlik
    /// oynadan TASHQARIDA ham (7 kungacha) yetib boradi. Avtomatik javobga qo'yish Meta
    /// siyosatini buzadi — <see cref="InstagramContract.HumanAgentWindowOpen"/> izohiga qarang.</param>
    public async Task<(bool Ok, string Error)> SendDmAsync(
        string igUserId, string recipientId, string message, string token, CancellationToken ct,
        bool humanAgent = false)
    {
        // ⚠️ Ikki xil payload: teg kerak bo'lmaganda maydonlar UMUMAN yuborilmaydi (ortiqcha
        // parametr Meta tomonidan rad etilishi mumkin va oddiy javob yo'lini buzardi).
        if (humanAgent)
        {
            var tagged = new
            {
                recipient = new { id = recipientId ?? "" },
                message = new { text = InstagramContract.TrimBytes(message, IgConst.MaxReplyBytes) },
                messaging_type = "MESSAGE_TAG",
                tag = "HUMAN_AGENT",
            };
            return await PostMessagesAsync(
                string.IsNullOrWhiteSpace(igUserId) ? "me" : igUserId, tagged, token, ct);
        }

        var payload = new
        {
            recipient = new { id = recipientId ?? "" },
            // ⚠️ BAYT bo'yicha (Meta: UTF-8 ≤1000 bayt). Belgi bo'yicha kesish kirill/emoji
            // javoblarda chegaradan oshib ketardi — `IgConst.MaxReplyBytes` izohiga qarang.
            message = new { text = InstagramContract.TrimBytes(message, IgConst.MaxReplyBytes) },
        };
        var path = string.IsNullOrWhiteSpace(igUserId) ? "me" : igUserId;
        return await PostMessagesAsync(path, payload, token, ct);
    }

    private async Task<(bool Ok, string Error)> PostMessagesAsync(
        string path, object payload, string token, CancellationToken ct)
    {
        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(path)}/messages?access_token={Uri.EscapeDataString(token ?? "")}";
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var (ok, _, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, ct);
        return (ok, err);
    }

    /// <summary>
    /// FAQ TUGMALARINI (ice breakers) Meta'ga yozadi — <c>POST /me/messenger_profile</c>.
    ///
    /// <para>Tugmalar Direct birinchi ochilganda mijozga savol ro'yxati bo'lib ko'rinadi;
    /// bosilganda webhook'ga <c>messaging_postbacks</c> hodisasi keladi (payload —
    /// <c>InstagramContract.FaqPayload</c>). So'rov messenger profile'ni BUTUNLIGICHA
    /// almashtiradi, ya'ni har chaqiruvda TO'LIQ ro'yxat yuboriladi (qisman qo'shish yo'q).</para>
    ///
    /// <para>⚠️ Ko'pi bilan <see cref="IgConst.MaxFaqItems"/> ta savol — Meta chegarasi;
    /// ortiqchasini yuborish butun so'rovni <c>code 100</c> bilan yiqitadi, shuning uchun
    /// ro'yxat shu yerda ham kesiladi (chaqiruvchi xatosi mijozga yetib bormasin).</para>
    /// </summary>
    /// <param name="items">Tartibda: savol matni + postback payload (<c>FAQ:&lt;id&gt;</c>).</param>
    public async Task<(bool Ok, string Error)> SetIceBreakersAsync(
        string token, IReadOnlyList<(string Question, string Payload)> items, CancellationToken ct)
    {
        var payload = new
        {
            platform = "instagram",
            ice_breakers = new[]
            {
                new
                {
                    call_to_actions = items
                        .Take(IgConst.MaxFaqItems)
                        .Select(i => new { question = i.Question ?? "", payload = i.Payload ?? "" })
                        .ToArray(),
                    locale = "default",
                },
            },
        };

        var url = $"{IgConst.GraphBase}/me/messenger_profile?access_token={Uri.EscapeDataString(token ?? "")}";
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var (ok, _, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, ct);
        return (ok, err);
    }

    /// <summary>
    /// FAQ tugmalarini Meta'dan OLIB TASHLAYDI — <c>DELETE /me/messenger_profile</c>
    /// (<c>fields=["ice_breakers"]</c>).
    ///
    /// <para>Bo'sh ro'yxat bilan <see cref="SetIceBreakersAsync"/> chaqirish O'RNIGA ishlatiladi:
    /// Meta bo'sh <c>call_to_actions</c> massivini rad etadi — profil maydonini tozalashning
    /// rasmiy yo'li aynan DELETE.</para>
    /// </summary>
    public async Task<(bool Ok, string Error)> DeleteIceBreakersAsync(string token, CancellationToken ct)
    {
        var payload = new { platform = "instagram", fields = new[] { "ice_breakers" } };
        var url = $"{IgConst.GraphBase}/me/messenger_profile?access_token={Uri.EscapeDataString(token ?? "")}";
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var (ok, _, err) = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, ct);
        return (ok, err);
    }

    /// <summary>Post matni (caption) — AI'ga "mijoz qaysi post ostida yozdi" konteksti uchun.</summary>
    public async Task<(bool Ok, string Caption, string Permalink, string Error)> GetMediaAsync(
        string mediaId, string token, CancellationToken ct)
    {
        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(mediaId ?? "")}?fields=id,caption,permalink" +
                  $"&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, "", "", err);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (true, Str(doc.RootElement, "caption"), Str(doc.RootElement, "permalink"), "");
        }
        catch (JsonException)
        {
            return (false, "", "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>
    /// SUHBATDOSHNING profili — <c>GET /{igsid}?fields=name,username</c>.
    ///
    /// <para><b>Nega kerak (TEXNIK.md §3.5, qoidalar §11 tuzoq 6):</b> DM webhook'ining
    /// <c>messaging[]</c> bo'limida <b>username UMUMAN kelmaydi</b> — faqat <c>sender.id</c>
    /// (IGSID). Izohda esa <c>from.username</c> bor. Ya'ni bu so'rovsiz Inbox'da DM suhbatlari
    /// <c>@17841400…</c> degan raqam bo'lib turadi: operator kim bilan yozishayotganini
    /// bilmaydi, profilga ham o'tolmaydi.</para>
    ///
    /// <para><b>⚠️ Faqat <c>name,username</c> so'raladi.</b> Meta bu yerda <c>profile_pic</c>,
    /// <c>follower_count</c> va boshqalarni ham beradi, lekin (a) ularni saqlaydigan ustun yo'q,
    /// (b) mijozning maxfiylik sozlamasiga qarab qaytmasligi mumkin — kerak bo'lmagan maydon
    /// faqat nosozlik yuzasini kengaytirardi.</para>
    ///
    /// <para><b>⚠️ Chaqiruvchi xatoni YUTISHI kerak:</b> username — qulaylik, xabarning o'zi
    /// emas. Profil so'rovi yiqilgani (mijoz akkauntini yopgan, token ruxsati yetmagan)
    /// mijozning xabarini yozib qo'yishga ham, javob berishga ham to'sqinlik qilmasligi
    /// SHART.</para>
    /// </summary>
    public async Task<(bool Ok, string Username, string Name, string Error)> GetUserProfileAsync(
        string igsid, string token, CancellationToken ct)
    {
        var url = $"{IgConst.GraphBase}/{Uri.EscapeDataString(igsid ?? "")}?fields=name,username" +
                  $"&access_token={Uri.EscapeDataString(token ?? "")}";
        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, "", "", err);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (true, Str(doc.RootElement, "username"), Str(doc.RootElement, "name"), "");
        }
        catch (JsonException)
        {
            return (false, "", "", "Instagram javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /* ═════════════════════════ Ichki qism ═════════════════════════ */

    /// <summary>So'rovni yuboradi: throttle + retry + xatoni o'zbekcha matnga aylantirish.
    /// <paramref name="factory"/> — HAR urinishda YANGI <c>HttpRequestMessage</c> (bittasini
    /// qayta yuborib bo'lmaydi).</summary>
    private async Task<(bool Ok, string Body, string Error)> SendAsync(
        Func<HttpRequestMessage> factory, CancellationToken ct)
    {
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
                var retryable = IsRetryable(resp.StatusCode, code);
                logger.LogWarning("Instagram API xato ({Status}/{Code}/{Sub}): {Msg}", (int)resp.StatusCode, code, sub, msg);
                if (retryable && attempt < MaxAttempts)
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
                if (attempt < MaxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                return (false, "", "Instagram javob bermadi (vaqt tugadi) — keyinroq qayta urinamiz.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < MaxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                return (false, "", $"Tarmoq xatosi: {ex.Message}");
            }
        }
    }

    /// <summary>Ketma-ket so'rovlar orasida kamida 1 soniya (butun ilova bo'yicha).</summary>
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

    /// <summary>Meta xato kodini foydalanuvchi o'qiydigan O'ZBEKCHA matnga aylantiradi
    /// (IG-SPEC §3.6 xaritasi). Tamoyil: <b>vaqtinchalik → qayta urinamiz, doimiy → signal</b>.</summary>
    private static string MapError(int httpStatus, int code, int sub, string msg) => code switch
    {
        190 => "Token muddati tugagan yoki bekor qilingan — akkauntni qayta ulang.",
        4 or 17 or 32 or 613 => "Instagram so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.",
        10 when sub == 2534022 => "24 soatlik javob oynasi yopilgan — Instagram DM'ni qabul qilmadi.",
        10 or 200 => "Ruxsat yetishmaydi — akkauntni qayta ulab, xabar va izoh ruxsatlarini bering.",
        100 => $"Noto'g'ri so'rov parametri: {msg}",
        551 => "Mijoz xabarni qabul qila olmadi (akkaunt cheklangan yoki bloklangan).",
        _ => msg.Length > 0
            ? $"Instagram xato ({(code != 0 ? code : httpStatus)}): {msg}"
            : $"Instagram xato ({httpStatus}).",
    };

    private static (int Code, int Sub, string Message) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
                return (0, 0, "");
            var code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            var sub = e.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : 0;
            return (code, sub, Str(e, "message"));
        }
        catch (JsonException) { return (0, 0, ""); }
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
}
