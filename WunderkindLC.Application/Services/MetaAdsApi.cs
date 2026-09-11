using System.Net;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>Reklama formasidan olingan lidning TO'LIQ ma'lumoti (Graph javobidan).</summary>
/// <param name="FieldsJson">Formaning barcha javoblari (xom <c>field_data</c>) — kelajakda
/// forma maydoni qo'shilsa ma'lumot yo'qolmasin.</param>
public record MetaAdLeadData(
    string LeadgenId,
    string FullName,
    string Phone,
    string Email,
    string FormId,
    string FormName,
    string AdId,
    string AdName,
    string AdsetId,
    string CampaignId,
    string CampaignName,
    string Platform,
    string CreatedTimeIso,
    string FieldsJson);

/// <summary>
/// META REKLAMA LIDLARI (Lead Ads) uchun Graph API mijozi.
///
/// <para><b>⚠️ Bu YAGONA joy, qayerda <c>graph.facebook.com</c> ishlatiladi</b>
/// (<see cref="IgConst.FbGraphBase"/>). Izoh/DM moduli <c>graph.instagram.com</c> da qoladi —
/// ikkisi Meta'da ayri mahsulot va tokenlari ham har xil: bu yerda <b>Page Access Token</b>
/// (<c>leads_retrieval</c> ruxsati bilan), u yerda Instagram Login tokeni. Tokenni almashtirib
/// yuborish "OAuthException 190" bo'lib chiqadi va sababini topish qiyin, shuning uchun
/// mijozlar ATAYIN alohida sinflarda.</para>
///
/// <para><b>Uslub — <see cref="InstagramApi"/> bilan bir xil: ISTISNO OTILMAYDI.</b> Har metod
/// <c>(Ok, …, Error)</c> qaytaradi, <c>Error</c> — foydalanuvchiga ko'rsatiladigan O'ZBEKCHA
/// matn. Chaqiruvchi fon xizmati bo'lgani uchun bitta tarmoq uzilishi navbatni yiqitmasligi
/// kerak.</para>
///
/// <para>DI: <c>builder.Services.AddHttpClient&lt;MetaAdsApi&gt;();</c></para>
/// </summary>
public sealed class MetaAdsApi(HttpClient http, ILogger<MetaAdsApi> logger)
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// [1] Lid ma'lumotini oladi (<c>GET /{leadgen_id}</c>).
    ///
    /// <para>⚠️ Webhook payloadida ism ham, telefon ham YO'Q — Meta shaxsiy ma'lumotni faqat
    /// shu so'rov orqali beradi. Ya'ni token yaroqsiz bo'lsa lid MAZMUNSIZ qoladi va buni
    /// jimgina o'tkazib yuborib bo'lmaydi (xato yozib qo'yiladi).</para>
    ///
    /// <para>⚠️ Meta lidni ~90 kun saqlaydi; bundan eski hodisani qayta o'ynatib bo'lmaydi.</para>
    /// </summary>
    public async Task<(bool Ok, MetaAdLeadData? Lead, string Error)> FetchLeadAsync(
        string leadgenId, string pageToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(leadgenId)) return (false, null, "Lid id bo'sh.");
        if (string.IsNullOrWhiteSpace(pageToken))
            return (false, null, "Page Access Token kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.");

        var url = $"{IgConst.FbGraphBase}/{Uri.EscapeDataString(leadgenId.Trim())}"
                  + $"?fields={IgConst.LeadgenFields}&access_token={Uri.EscapeDataString(pageToken)}";

        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, null, err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return (true, ReadLead(doc.RootElement, leadgenId), "");
        }
        catch (JsonException)
        {
            return (false, null, "Meta javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>
    /// [2] Sahifani ilovaga <c>leadgen</c> maydoni bo'yicha OBUNA qiladi
    /// (<c>POST /{page-id}/subscribed_apps</c>).
    ///
    /// <para>⚠️ Bu qadamsiz Meta hodisani UMUMAN yubormaydi — Meta konsolida webhook manzili
    /// to'g'ri turgan bo'lsa ham. Aynan shu sabab admin uchun "obuna faol/yo'q" holati alohida
    /// ko'rsatiladi: aks holda nosozlik "reklama ishlayapti, lid kelmayapti" bo'lib ko'rinadi.</para>
    /// </summary>
    public async Task<(bool Ok, string Error)> SubscribeLeadgenAsync(
        string pageId, string pageToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pageId)) return (false, "Page ID bo'sh.");

        var url = $"{IgConst.FbGraphBase}/{Uri.EscapeDataString(pageId.Trim())}/subscribed_apps"
                  + $"?subscribed_fields={IgConst.LeadgenSubscribeFields}"
                  + $"&access_token={Uri.EscapeDataString(pageToken ?? "")}";

        var (ok, _, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url), ct);
        return ok ? (true, "") : (false, err);
    }

    /// <summary>
    /// [3] Sahifa nomini oladi — token TO'G'RI SAHIFANIKI ekanini tekshirish uchun.
    ///
    /// <para>Sozlamalar saqlanayotganda chaqiriladi: admin token bilan Page ID'ni chalkashtirib
    /// yuborsa xato DARHOL ko'rinadi, oradan bir hafta o'tib "nega lid kelmadi" degan savol
    /// bo'lib emas.</para>
    /// </summary>
    public async Task<(bool Ok, string PageName, string Error)> FetchPageAsync(
        string pageId, string pageToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pageId)) return (false, "", "Page ID bo'sh.");

        var url = $"{IgConst.FbGraphBase}/{Uri.EscapeDataString(pageId.Trim())}"
                  + $"?fields=id,name&access_token={Uri.EscapeDataString(pageToken ?? "")}";

        var (ok, body, err) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return (false, "", err);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return (true, Str(doc.RootElement, "name"), "");
        }
        catch (JsonException)
        {
            return (false, "", "Meta javobini o'qib bo'lmadi (kutilmagan format).");
        }
    }

    /// <summary>
    /// [4] Instant Form NOMI (<c>GET /{form_id}?fields=name</c>).
    ///
    /// <para>⚠️ Forma nomi lid tugunida YO'Q, shuning uchun alohida so'rov. U lidning "qiziqqan
    /// yo'nalishi" bo'ladi ("Yozgi IELTS intensiv") — formada kurs maydoni bo'lmasa ham menejer
    /// nimaga qiziqqanini ko'radi. Chaqiruvchi natijani KESHLAYDI (o'sha formaning oldingi lidida
    /// saqlangan nom ishlatiladi), ya'ni har lid uchun qo'shimcha so'rov ketmaydi.</para>
    ///
    /// <para>Xato bo'lsa BO'SH nom qaytadi — lid baribir yaratiladi (nom — qulaylik, majburiy
    /// ma'lumot emas).</para>
    /// </summary>
    public async Task<string> FetchFormNameAsync(string formId, string pageToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(pageToken)) return "";

        var url = $"{IgConst.FbGraphBase}/{Uri.EscapeDataString(formId.Trim())}"
                  + $"?fields=id,name&access_token={Uri.EscapeDataString(pageToken)}";

        var (ok, body, _) = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!ok) return "";

        try
        {
            using var doc = JsonDocument.Parse(body);
            return Str(doc.RootElement, "name");
        }
        catch (JsonException) { return ""; }
    }

    /* ═════════════════════════ Javobni o'qish ═════════════════════════ */

    /// <summary>
    /// Graph javobidan lidni yig'adi. <c>field_data</c> — <c>[{name, values:[…]}]</c> massivi.
    ///
    /// <para>⚠️ Maydon nomlari formadan formaga FARQ QILADI (<c>full_name</c>, <c>phone_number</c>,
    /// lekin ba'zan <c>first_name</c>+<c>last_name</c> yoki markaz o'zi qo'ygan nom). Shu sabab
    /// tanish nomlar bo'yicha izlanadi va topilmasa xom JSON baribir saqlanadi — ma'lumot
    /// jimgina yo'qolmasin.</para>
    /// </summary>
    internal static MetaAdLeadData ReadLead(JsonElement root, string leadgenId)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fieldsJson = "";

        if (root.TryGetProperty("field_data", out var fd) && fd.ValueKind == JsonValueKind.Array)
        {
            fieldsJson = fd.GetRawText();
            foreach (var f in fd.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                var name = Str(f, "name");
                if (name.Length == 0) continue;

                var value = "";
                if (f.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                    foreach (var v in vals.EnumerateArray())
                    {
                        var one = v.ValueKind switch
                        {
                            JsonValueKind.String => v.GetString() ?? "",
                            JsonValueKind.Number => v.ToString(),
                            _ => "",
                        };
                        if (one.Length == 0) continue;
                        value = value.Length == 0 ? one : value + ", " + one;
                    }

                if (value.Length > 0) fields[name] = value;
            }
        }

        var first = Pick(fields, "first_name");
        var last = Pick(fields, "last_name");
        var full = Pick(fields, "full_name", "name", "fish", "ism");
        if (full.Length == 0) full = string.Join(" ", new[] { first, last }.Where(x => x.Length > 0));

        return new MetaAdLeadData(
            LeadgenId: leadgenId,
            FullName: full,
            Phone: Pick(fields, "phone_number", "phone", "telefon"),
            Email: Pick(fields, "email"),
            FormId: Str(root, "form_id"),
            FormName: Str(root, "form_name"),
            AdId: Str(root, "ad_id"),
            AdName: Str(root, "ad_name"),
            AdsetId: Str(root, "adset_id"),
            CampaignId: Str(root, "campaign_id"),
            CampaignName: Str(root, "campaign_name"),
            Platform: Str(root, "platform"),
            CreatedTimeIso: InstagramEventParser.ToIso(Str(root, "created_time")),
            FieldsJson: fieldsJson);
    }

    private static string Pick(Dictionary<string, string> fields, params string[] names)
    {
        foreach (var n in names)
            if (fields.TryGetValue(n, out var v) && v.Length > 0) return v;
        return "";
    }

    /* ═════════════════════════ Transport ═════════════════════════ */

    /// <summary>
    /// Bitta so'rov + qayta urinish (1s → 2s → 4s). Qayta urinish FAQAT vaqtinchalik xatolarda:
    /// token yaroqsiz (190) yoki ruxsat yetishmasa (10/200) urinish faqat vaqt yo'qotardi va
    /// navbatdagi qolgan lidlarni ham kechiktirardi.
    /// </summary>
    private async Task<(bool Ok, string Body, string Error)> SendAsync(
        Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        var delayMs = 1000;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = factory();
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return (true, body, "");

                var (code, sub, msg) = ParseError(body);
                // ⚠️ Manzil LOGGA yozilmaydi — unda `access_token` bor (marketing-instagram.md §7).
                logger.LogWarning(
                    "Meta reklama lidlari API xato ({Status}/{Code}/{Sub}): {Msg}",
                    (int)resp.StatusCode, code, sub, msg);

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

    private static bool IsRetryable(HttpStatusCode status, int code)
    {
        if (status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout) return true;
        return code is 4 or 17 or 32 or 613 or 2;
    }

    /// <summary>Meta xato kodi → O'ZBEKCHA matn. Matnlar ATAYIN "nima qilish kerak" bilan:
    /// admin xatoni Sozlamalar sahifasida o'qiydi.</summary>
    private static string MapError(int httpStatus, int code, int sub, string msg) => code switch
    {
        190 => "Page Access Token muddati tugagan yoki bekor qilingan — Sozlamalar bo'limida yangisini kiriting.",
        4 or 17 or 32 or 613 => "Meta so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.",
        10 or 200 or 299 =>
            "Ruxsat yetishmaydi — ilovada `leads_retrieval` ruxsati va sahifa ustidan huquq borligini tekshiring.",
        100 => $"Noto'g'ri so'rov parametri: {msg}",
        _ => msg.Length > 0
            ? $"Meta xato ({(code != 0 ? code : httpStatus)}): {msg}"
            : $"Meta xato ({httpStatus}).",
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
