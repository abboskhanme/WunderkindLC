using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// ILOVA HAQIQIYLIGI (attestation) — "bu so'rov HAQIQATAN bizning, o'zgartirilmagan ilovadan,
/// buzilmagan qurilmadan keldimi".
///
/// <para><b>Nega kerak?</b> Yuz modeli TELEFONDA ishlaydi va serverga tayyor VEKTOR keladi
/// (server 1 GB RAM — <c>FACE-DETEKT-PLAN.md</c> §2). Demak o'zgartirilgan APK istalgan vektorni
/// (masalan boshqa o'quvchining suratidan olinganini) yubora oladi va server buni farqlay
/// olmaydi. Attestation shu bo'shliqni yopishga urinadi: token ilovaning O'ZIDAN emas,
/// Google'ning ishonch zanjiridan keladi.</para>
///
/// <para><b>Android — Google Play Integrity API.</b> Ilova <c>requestIntegrityToken</c> bilan
/// token oladi (ichiga bizning bir martalik <c>nonce</c> ni qo'yadi) va uni <c>verify</c> so'rovi
/// bilan yuboradi. Server tokenni Google'da ochadi
/// (<c>playintegrity.googleapis.com/v1/{package}:decodeIntegrityToken</c>) va uchta narsani
/// tekshiradi: ilova Play tomonidan tanilganmi, paket nomi biznikimi, qurilma butunmi.
/// Qo'shimcha: token ichidagi <c>nonce</c> bizning challenge nonce'imiz bilan bir xilmi
/// (busiz eski token qayta ishlatilardi).</para>
///
/// <para><b>iOS — App Attest: HALI SOZLANMAGAN.</b> Verdict har doim
/// <see cref="Verdict.NotConfigured"/>. TODO: DeviceCheck/App Attest (attestation obyektini
/// tekshirish + assertion hisoblagichi) — Android birinchi, chunki markazda Android ustun va
/// iOS uchun Apple Developer hisobidan alohida kalit (Key ID + Team ID + P8) kerak.</para>
///
/// <para><b>Kalitlar — FAQAT <c>.env</c></b> (<see cref="AppSecrets"/>):
/// <c>PLAY_INTEGRITY_SA_JSON</c> va <c>PLAY_INTEGRITY_PACKAGE</c>. Alohida service account
/// qo'yilmasa <c>FCM_SERVICE_ACCOUNT_JSON</c> ZAXIRA sifatida ishlatiladi — Firebase loyihasi
/// ayni paytda Google Cloud loyihasidir, ya'ni o'sha service account YARAYDI, lekin ikki shart
/// bilan: (1) o'sha GCP loyihasida <b>Play Integrity API yoqilgan</b>, (2) Play Console'dagi
/// ilova o'sha loyihaga <b>bog'langan</b>. Shartlar bajarilmasa Google 403 qaytaradi va biz uni
/// <see cref="Verdict.Unavailable"/> deb belgilaymiz (fail-closed/fail-open sozlamaga qarab).</para>
/// </summary>
public sealed class AppAttestation(IHttpClientFactory httpFactory, ILogger<AppAttestation> logger)
{
    /* =============================================================================================
     *  Natija
     * ========================================================================================== */

    /// <summary>Tekshiruv xulosasi.</summary>
    public enum Verdict
    {
        /// <summary>Kalit/paket sozlanmagan yoki platforma qo'llab-quvvatlanmaydi (iOS) —
        /// biz umuman tekshira olmadik. Bu XATO EMAS.</summary>
        NotConfigured,
        /// <summary>Google tasdiqladi: ilova Play'dan, paket bizniki, qurilma butun.</summary>
        Ok,
        /// <summary>Google JAVOB BERDI va RAD ETDI (o'zgartirilgan APK, root/emulyator, boshqa paket,
        /// nonce mos emas). Bu — HUJUM belgisi.</summary>
        Failed,
        /// <summary>Tashqi xizmatga YETIB BORILMADI (timeout, tarmoq, 5xx, ruxsat yo'q).
        /// ⚠️ Bu <see cref="Failed"/> DAN FARQ QILADI: aybdor foydalanuvchi emas, bizning
        /// infratuzilma. Shuning uchun sozlama <c>true</c> bo'lsa FAIL-CLOSED (rad etamiz —
        /// "tekshira olmadik" degani "o'tkazamiz" degani emas), <c>false</c> bo'lsa o'tkazamiz.</summary>
        Unavailable,
    }

    /// <summary>Natija + qisqa sabab (bazaga <c>LoginFaceCheck.AttestReason</c> ga yoziladi).</summary>
    public readonly record struct Result(Verdict Verdict, string Reason)
    {
        public static readonly Result NotConfigured = new(Verdict.NotConfigured, "sozlanmagan");
    }

    /// <summary>Bazaga yoziladigan qisqa kalitlar (<c>LoginFaceCheck.Attested</c>).</summary>
    public static string Code(Verdict v) => v switch
    {
        Verdict.Ok => "ok",
        Verdict.Failed => "failed",
        Verdict.Unavailable => "unavailable",
        _ => "notConfigured",
    };

    /* =============================================================================================
     *  DARVOZA — sof funksiya (testlangan)
     * ========================================================================================== */

    /// <summary>
    /// Sozlamaga qarab kirishga ruxsat berilsinmi. <c>null</c> — o'tadi, aks holda
    /// foydalanuvchiga ko'rsatiladigan sabab.
    ///
    /// <para><paramref name="required"/> = <c>false</c> (STANDART) bo'lsa attestation HECH QACHON
    /// kirishni to'smaydi — natija faqat jurnalga yoziladi. Sabab: kalit sozlanmaguncha yoki
    /// ilovaning yangi versiyasi tarqalmaguncha hech kim qulflanib qolmasin.</para>
    ///
    /// <para>⚠️ <c>true</c> QILISHDAN OLDIN: <see cref="Verdict.NotConfigured"/> ham RAD etiladi,
    /// iOS esa <see cref="VerifyAsync"/> da HAR DOIM shu xulosani oladi (App Attest yozilmagan) —
    /// ya'ni yoqilishi bilan hamma iOS foydalanuvchisi bloklanadi. Kalitlar umuman qo'yilmagan
    /// bo'lsa (<see cref="Configured"/> = <c>false</c>) esa Android ham bloklanadi; shu sabab
    /// <c>PUT /api/admin/face/settings</c> bunday holatda sozlamani YOQTIRMAYDI.</para>
    /// </summary>
    public static string? Gate(Verdict verdict, bool required)
    {
        if (!required) return null;
        return verdict switch
        {
            Verdict.Ok => null,
            Verdict.Failed => FaceMatch.ReasonAttestation,
            // Fail-closed: tekshira olmadik, lekin sozlama "majburiy" deydi.
            Verdict.Unavailable => FaceMatch.ReasonAttestationUnavailable,
            _ => FaceMatch.ReasonAttestationMissing,
        };
    }

    /* =============================================================================================
     *  TEKSHIRISH
     * ========================================================================================== */

    /// <summary>Tashqi so'rov uchun eng ko'p kutish vaqti. Qisqa ATAYIN: bu KIRISH yo'lida turadi —
    /// Google sekinlashsa o'quvchi ilovada osilib qolmasin (natija `Unavailable` bo'ladi).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Play Integrity uchun service account: alohida kalit bo'lmasa FCM'niki (izohga qarang).</summary>
    public static string ServiceAccountJson =>
        AppSecrets.PlayIntegrityServiceAccountJson.Length > 0
            ? AppSecrets.PlayIntegrityServiceAccountJson
            : AppSecrets.FcmServiceAccountJson;

    public static string PackageName => AppSecrets.PlayIntegrityPackage;

    /// <summary>Android tomoni sozlanganmi (paket nomi + service account).</summary>
    public static bool Configured =>
        PackageName.Length > 0 && FcmService.IsConfigured(ServiceAccountJson);

    /// <summary>
    /// Integrity tokenini tekshiradi.
    /// </summary>
    /// <param name="integrityToken">Ilova yuborgan token (bo'sh bo'lsa — <c>NotConfigured</c>).</param>
    /// <param name="platform">Klient platformasi (<c>android</c> | <c>ios</c> | ...).</param>
    /// <param name="expectedNonce">Bizning bir martalik nonce — token ichidagisi bilan solishtiriladi.</param>
    public async Task<Result> VerifyAsync(
        string? integrityToken, string? platform, string? expectedNonce, CancellationToken ct = default)
    {
        var token = (integrityToken ?? "").Trim();
        var os = (platform ?? "").Trim().ToLowerInvariant();

        // iOS — App Attest hali yozilmagan (yuqoridagi TODO). Token kelsa ham tekshirmaymiz:
        // "tekshirdim" deb yolg'on `ok` qaytarish eng yomon variant bo'lardi.
        if (os is "ios" or "iphone" or "ipados") return new Result(Verdict.NotConfigured, "iOS: App Attest hali sozlanmagan");

        if (token.Length == 0) return new Result(Verdict.NotConfigured, "token yuborilmagan");
        if (!Configured) return new Result(Verdict.NotConfigured, "PLAY_INTEGRITY_* sozlanmagan");

        var pkg = PackageName;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            string accessToken;
            try { accessToken = await GetAccessTokenAsync(cts.Token); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Play Integrity: OAuth token olinmadi");
                return new Result(Verdict.Unavailable, "OAuth token olinmadi");
            }

            var client = httpFactory.CreateClient();
            var url = $"https://playintegrity.googleapis.com/v1/{Uri.EscapeDataString(pkg)}:decodeIntegrityToken";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { integrity_token = token }), Encoding.UTF8, "application/json");

            var resp = await client.SendAsync(req, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                // 400 = token BUZUQ/eskirgan (mijoz aybi) → Failed.
                // 401/403/404/5xx = biz yeta olmadik yoki sozlanmagan (bizning aybimiz) → Unavailable.
                var code = (int)resp.StatusCode;
                logger.LogWarning("Play Integrity {Status}: {Body}", code, Trim(body));
                return code == 400
                    ? new Result(Verdict.Failed, "token yaroqsiz")
                    : new Result(Verdict.Unavailable, $"Google javobi {code}");
            }

            return Judge(body, pkg, expectedNonce);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Bizning 5 soniyalik timeout (foydalanuvchi so'rovi bekor qilingani EMAS).
            logger.LogWarning("Play Integrity: timeout ({Sec} s)", Timeout.TotalSeconds);
            return new Result(Verdict.Unavailable, "timeout");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Play Integrity: tekshirib bo'lmadi");
            return new Result(Verdict.Unavailable, "tarmoq xatosi");
        }
    }

    /* =============================================================================================
     *  JAVOBNI BAHOLASH — sof funksiya (testlangan, tarmoqsiz)
     * ========================================================================================== */

    /// <summary>
    /// Google javobidan (JSON) xulosa chiqaradi. Tarmoqqa tegmaydi — shu sababdan testlanadi.
    ///
    /// <para>Kutilgan tuzilma:
    /// <c>tokenPayloadExternal.appIntegrity.appRecognitionVerdict = "PLAY_RECOGNIZED"</c>,
    /// <c>...appIntegrity.packageName = bizniki</c>,
    /// <c>...deviceIntegrity.deviceRecognitionVerdict</c> ichida <c>"MEETS_DEVICE_INTEGRITY"</c>,
    /// <c>...requestDetails.nonce = bizning nonce</c>.</para>
    /// </summary>
    public static Result Judge(string? json, string expectedPackage, string? expectedNonce)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Result(Verdict.Unavailable, "bo'sh javob");
        JsonElement payload;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return new Result(Verdict.Unavailable, "javob JSON emas"); }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("tokenPayloadExternal", out payload))
                return new Result(Verdict.Unavailable, "javobda tokenPayloadExternal yo'q");

            var app = payload.TryGetProperty("appIntegrity", out var a) ? a : default;
            var device = payload.TryGetProperty("deviceIntegrity", out var d) ? d : default;
            var request = payload.TryGetProperty("requestDetails", out var r) ? r : default;

            var recognition = Str(app, "appRecognitionVerdict");
            if (!string.Equals(recognition, "PLAY_RECOGNIZED", StringComparison.Ordinal))
                return new Result(Verdict.Failed,
                    recognition.Length == 0 ? "ilova tanilmadi" : $"ilova: {recognition}");

            var pkg = Str(app, "packageName");
            if (pkg.Length > 0 && !string.Equals(pkg, expectedPackage, StringComparison.Ordinal))
                return new Result(Verdict.Failed, "paket nomi mos emas");

            var deviceOk = false;
            if (device.ValueKind == JsonValueKind.Object
                && device.TryGetProperty("deviceRecognitionVerdict", out var verdicts)
                && verdicts.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in verdicts.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.String
                        && string.Equals(v.GetString(), "MEETS_DEVICE_INTEGRITY", StringComparison.Ordinal))
                    { deviceOk = true; break; }
            }
            if (!deviceOk) return new Result(Verdict.Failed, "qurilma butunligi tasdiqlanmadi");

            // NONCE: token bizning SHU urinishimiz uchun olinganini isbotlaydi. Nonce bo'lmasa
            // eski token qayta ishlatilib ketardi (replay).
            var want = (expectedNonce ?? "").Trim();
            if (want.Length > 0)
            {
                var got = Str(request, "nonce");
                if (!string.Equals(got, want, StringComparison.Ordinal))
                    return new Result(Verdict.Failed, "nonce mos emas");
            }

            return new Result(Verdict.Ok, "");
        }
    }

    private static string Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "") : "";

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];

    /* =============================================================================================
     *  Google OAuth (service account → access token)
     * ========================================================================================== */

    private readonly object _lock = new();
    private string _cachedToken = "";
    private DateTime _cachedExpiry = DateTime.MinValue;

    /// <summary>
    /// Service account JSON'dan OAuth access token (scope: <c>playintegrity</c>).
    /// ⚠️ <see cref="FcmService"/> da shunga o'xshash kod bor — ATAYIN takrorlangan: u yerda
    /// scope boshqa (<c>firebase.messaging</c>) va o'z keshi bor; ikkalasini bitta yordamchiga
    /// yig'ish push oqimiga tegishni talab qilardi (bu yerda tegilmaydigan qism).
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_cachedToken.Length > 0 && DateTime.UtcNow < _cachedExpiry) return _cachedToken;
        }

        if (!FcmService.TryParse(ServiceAccountJson, out var c))
            throw new InvalidOperationException("Play Integrity service account JSON yaroqsiz");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = c.ClientEmail,
            scope = "https://www.googleapis.com/auth/playintegrity",
            aud = "https://oauth2.googleapis.com/token",
            iat = now,
            exp = now + 3600,
        }));
        var unsigned = $"{header}.{claims}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(c.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var jwt = $"{unsigned}.{B64Url(signature)}";

        var client = httpFactory.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt,
        });
        var resp = await client.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString() ?? "";
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

        lock (_lock)
        {
            _cachedToken = token;
            _cachedExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }
        return token;
    }

    private static string B64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
