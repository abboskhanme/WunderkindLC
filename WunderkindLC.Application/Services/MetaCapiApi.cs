using System.Net;
using System.Text;
using System.Text.Json;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// META CONVERSIONS API (CAPI) mijozi — lid SIFATINI Meta'ga qaytarish.
///
/// <para><c>POST {FbGraphBase}/{DATASET_ID}/events?access_token={TOKEN}</c></para>
///
/// <para><b>Uslub — <see cref="MetaAdsApi"/> bilan bir xil: ISTISNO OTILMAYDI.</b> Har metod
/// <c>(Ok, …, Error)</c> qaytaradi, <c>Error</c> — o'zbekcha, "nima qilish kerak"i bilan.
/// Chaqiruvchi fon xizmati: bitta tarmoq uzilishi navbatni yiqitmasligi kerak.</para>
///
/// <para>⚠️ <b>Nima uchun alohida sinf</b> (<see cref="MetaAdsApi"/> ga qo'shilmadi): u yerda
/// <b>Page Access Token</b> ishlatiladi, bu yerda esa <b>Dataset (Events Manager) tokeni</b> —
/// ikkalasi har xil obyektga tegishli. Tokenlarni chalkashtirish "OAuthException 190" bo'lib
/// chiqadi va sababini topish qiyin.</para>
///
/// <para>DI: <c>builder.Services.AddHttpClient&lt;MetaCapiApi&gt;();</c></para>
///
/// <para>🔴 <b>Manzil hech qachon LOGGA yozilmaydi</b> — unda <c>access_token</c> bor.</para>
/// </summary>
public sealed class MetaCapiApi(HttpClient http, ILogger<MetaCapiApi> logger)
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// Hodisalar to'plamini yuboradi.
    ///
    /// <para>⚠️ <b>So'rov "hammasi yoki hech nima"</b>: bitta yaroqsiz hodisa (eski
    /// <c>event_time</c>, 1000 dan ortiq element) BUTUN so'rovni rad ettiradi. Shuning uchun
    /// tekshiruvlar tarmoqqa chiqishdan OLDIN, shu yerda qilinadi va xato matni qaysi
    /// hodisa aybdorligini (<c>event_id</c>) aytadi.</para>
    /// </summary>
    /// <param name="events">Ko'pi bilan <see cref="MetaCapiPayload.MaxEventsPerRequest"/> ta —
    /// ko'p bo'lsa chaqiruvchi <see cref="MetaCapiPayload.Chunk{T}"/> bilan bo'lakka bo'ladi.</param>
    /// <param name="testEventCode">⚠️ FAQAT sinov uchun (Events Manager → "Test Events" tabidagi
    /// kod). <b>Produksiyada berilmaydi</b>: kod bilan kelgan hodisalar faqat sinov oynasida
    /// ko'rinadi va reklama optimizatsiyasiga umuman qo'shilmaydi — ya'ni modul "ishlayotgandek"
    /// ko'rinib, aslida hech narsa qilmasdi.</param>
    /// <returns><c>Received</c> — Meta qabul qilgan hodisalar soni (<c>events_received</c>).</returns>
    public async Task<(bool Ok, int Received, string Error)> SendAsync(
        string datasetId,
        string token,
        IReadOnlyList<MetaCapiEventInput> events,
        CancellationToken ct,
        string testEventCode = "")
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            return (false, 0, "Dataset ID kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.");
        if (string.IsNullOrWhiteSpace(token))
            return (false, 0, "CAPI tokeni kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.");
        if (events.Count == 0)
            return (false, 0, "Yuboriladigan hodisa yo'q.");
        if (events.Count > MetaCapiPayload.MaxEventsPerRequest)
            return (false, 0, $"Bir so'rovda ko'pi bilan {MetaCapiPayload.MaxEventsPerRequest} ta hodisa "
                              + "yuboriladi — ro'yxatni bo'laklarga bo'ling.");

        // ⚠️ Vaqt tekshiruvi — yuborishdan OLDIN (bitta eski hodisa butun so'rovni yiqitadi).
        var now = AppClock.Now;
        foreach (var e in events)
        {
            var timeErr = MetaCapiPayload.EventTimeError(e.EventTimeUnix, now);
            if (timeErr.Length > 0)
                return (false, 0, $"{timeErr} (event_id: {MetaCapiPayload.EventId(e.User.LeadId, e.EventTimeUnix)})");

            if (!e.User.HasAnyIdentifier)
                return (false, 0, "Hodisada identifikator yo'q (lead_id ham, telefon ham) — Meta uni hech kimga bog'lay olmaydi.");
            if (string.IsNullOrWhiteSpace(e.EventName))
                return (false, 0, "Hodisa nomi (event_name) bo'sh — Events Manager'dagi bosqich nomini kiriting.");
        }

        var body = MetaCapiPayload.BuildBody(events, testEventCode);
        var url = $"{IgConst.FbGraphBase}/{Uri.EscapeDataString(datasetId.Trim())}/events"
                  + $"?access_token={Uri.EscapeDataString(token.Trim())}";

        var (ok, responseBody, err) = await SendRawAsync(url, body, ct);
        if (!ok) return (false, 0, err);

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var received = doc.RootElement.TryGetProperty("events_received", out var r)
                           && r.TryGetInt32(out var ri) ? ri : 0;
            var trace = Str(doc.RootElement, "fbtrace_id");

            // ⚠️ `fbtrace_id` MUVAFFAQIYATDA ham yoziladi: Meta qo'llab-quvvatlash xizmati
            //    usiz umuman gaplashmaydi ("hodisa yuborilgan, lekin ko'rinmayapti" holati).
            logger.LogInformation(
                "CAPI: {Received}/{Sent} hodisa qabul qilindi (fbtrace_id: {Trace})",
                received, events.Count, trace);

            // ⚠️ Meta 200 qaytarib, hodisalarning bir qismini jimgina tashlab yuborishi mumkin.
            if (received < events.Count)
                logger.LogWarning(
                    "CAPI: {Sent} ta yuborildi, {Received} tasi qabul qilindi (fbtrace_id: {Trace})",
                    events.Count, received, trace);

            return (true, received, "");
        }
        catch (JsonException)
        {
            // 2xx keldi — hodisalar ketgan deb hisoblaymiz, lekin sonini bilmaymiz.
            logger.LogWarning("CAPI javobini o'qib bo'lmadi (kutilmagan format).");
            return (true, 0, "");
        }
    }

    /* ═════════════════════════ Transport ═════════════════════════ */

    /// <summary>
    /// Bitta POST + qayta urinish (1s → 2s → 4s). Qayta urinish FAQAT vaqtinchalik xatolarda:
    /// token yaroqsiz (190) yoki payload noto'g'ri (100) bo'lsa urinish faqat vaqt yo'qotardi.
    ///
    /// <para>⚠️ Qayta urinish XAVFSIZ, chunki <c>event_id</c> deterministik: Meta 48 soatlik
    /// oynada takrorni o'zi tashlab yuboradi.</para>
    /// </summary>
    private async Task<(bool Ok, string Body, string Error)> SendRawAsync(
        string url, string json, CancellationToken ct)
    {
        var delayMs = 1000;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return (true, body, "");

                var (code, sub, msg, trace) = ParseError(body);
                // ⚠️ Manzil LOGGA yozilmaydi — unda `access_token` bor.
                logger.LogWarning(
                    "CAPI xato ({Status}/{Code}/{Sub}): {Msg} (fbtrace_id: {Trace})",
                    (int)resp.StatusCode, code, sub, msg, trace);

                if (IsRetryable(resp.StatusCode, code) && attempt < MaxAttempts)
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
                return (false, "", "Meta javob bermadi (vaqt tugadi) — keyinroq qayta urinamiz.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < MaxAttempts) { await Task.Delay(delayMs, ct); delayMs *= 2; continue; }
                return (false, "", $"Tarmoq xatosi: {ex.Message}");
            }
        }
    }

    /// <summary>Vaqtinchalik xato — qayta urinsa o'tib ketishi mumkin (rate limit, server xatosi).</summary>
    private static bool IsRetryable(HttpStatusCode status, int code)
    {
        if (status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout) return true;
        return code is 4 or 17 or 32 or 613 or 2;
    }

    /// <summary>Meta xato kodi → O'ZBEKCHA matn ("nima qilish kerak"i bilan).</summary>
    private static string MapError(int httpStatus, int code, int sub, string msg) => code switch
    {
        190 => "CAPI tokeni muddati tugagan yoki bekor qilingan — Sozlamalar bo'limida yangisini kiriting.",
        4 or 17 or 32 or 613 or 2 => "Meta so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.",
        10 or 200 or 299 =>
            "Ruxsat yetishmaydi — tokenda `ads_management` ruxsati va Dataset ustidan huquq borligini tekshiring.",
        // ⚠️ CAPI'da `100` odatda payload xatosi: noto'g'ri Dataset ID, eski `event_time`
        //    yoki hashlanmasligi kerak bo'lgan maydon hashlangan.
        100 => $"Noto'g'ri so'rov: {msg} — Dataset ID va hodisa maydonlarini tekshiring.",
        803 => "Dataset (Events Manager) obyekti topilmadi — Dataset ID ni tekshiring.",
        _ => msg.Length > 0
            ? $"Meta xato ({(code != 0 ? code : httpStatus)}): {msg}"
            : $"Meta xato ({httpStatus}).",
    };

    private static (int Code, int Sub, string Message, string Trace) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var e) || e.ValueKind != JsonValueKind.Object)
                return (0, 0, "", "");
            var code = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            var sub = e.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : 0;
            return (code, sub, Str(e, "message"), Str(e, "fbtrace_id"));
        }
        catch (JsonException) { return (0, 0, "", ""); }
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
