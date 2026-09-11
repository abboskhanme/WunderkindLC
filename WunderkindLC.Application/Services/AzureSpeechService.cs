using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Azure Speech (Cognitive Services) — TALAFFUZNI BAHOLASH (Pronunciation Assessment).
/// REST orqali (SDK'siz, Docker'da yengil): qisqa audio (WAV PCM 16kHz mono) yuboriladi,
/// xizmat nutqni matnga o'giradi VA talaffuzni baholaydi (accuracy/fluency/completeness/prosody +
/// per-word). ReferenceText berilsa shu matnga taqqoslanadi (scripted), bo'sh bo'lsa erkin (unscripted).
/// </summary>
public static class AzureSpeechService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    public static bool IsConfigured(string? key, string? region) =>
        !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region);

    /// <summary>
    /// SOF nutq→matn (Speech-to-Text) — talaffuz baholashsiz. AI tekshiruv (Speaking) shuni ishlatadi:
    /// Azure audioni matnga o'giradi, keyin matn Gemini'ga tahlil uchun yuboriladi.
    /// REST (SDK'siz) — qisqa audio (WAV PCM 16kHz mono). Muvaffaqiyatsiz bo'lsa Ok=false, sabab Error'da.
    /// </summary>
    public static async Task<(bool Ok, string Text, string? Error)> RecognizeAsync(
        byte[] wav, string key, string region, string language = "en-US")
    {
        try
        {
            var url = $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/" +
                      $"cognitiveservices/v1?language={language}&format=detailed&profanity=raw";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Headers.Add("Accept", "application/json");
            var content = new ByteArrayContent(wav);
            content.Headers.TryAddWithoutValidation(
                "Content-Type", "audio/wav; codecs=audio/pcm; samplerate=16000");
            req.Content = content;

            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var hint = (int)resp.StatusCode switch
                {
                    401 or 403 => "Kalit yoki region noto'g'ri.",
                    400 => "Audio formati/so'rov noto'g'ri.",
                    429 => "Limit (kvota) tugagan.",
                    _ => "Kalit/region to'g'riligini tekshiring.",
                };
                var snippet = string.IsNullOrWhiteSpace(body) ? "" : " — " + body.Trim().Replace("\n", " ");
                if (snippet.Length > 200) snippet = snippet[..200] + "…";
                return (false, "", $"Azure xato ({(int)resp.StatusCode}). {hint}{snippet}");
            }
            if (string.IsNullOrWhiteSpace(body))
                return (false, "", "Azure bo'sh javob qaytardi.");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("RecognitionStatus", out var st) ? st.GetString() : "";
            if (status != "Success")
                return (false, "", status == "NoMatch"
                    ? "Nutq aniqlanmadi — balandroq va aniqroq gapiring."
                    : $"Holat: {status}");

            // DisplayText (yoki NBest[0].Display) — tanilgan matn.
            var text = root.TryGetProperty("DisplayText", out var dt) ? dt.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text) &&
                root.TryGetProperty("NBest", out var nb) && nb.ValueKind == JsonValueKind.Array && nb.GetArrayLength() > 0)
            {
                var n0 = nb[0];
                text = n0.TryGetProperty("Display", out var d) ? d.GetString() ?? ""
                     : n0.TryGetProperty("Lexical", out var lx) ? lx.GetString() ?? "" : "";
            }
            return string.IsNullOrWhiteSpace(text)
                ? (false, "", "Nutq aniqlanmadi — balandroq va aniqroq gapiring.")
                : (true, text.Trim(), null);
        }
        catch (TaskCanceledException)
        {
            return (false, "", "Vaqt tugadi (timeout) — qaytadan urinib ko'ring.");
        }
        catch (Exception ex)
        {
            return (false, "", $"Xatolik: {ex.Message}");
        }
    }

    /// <summary>Baytlar haqiqiy WAV (RIFF/WAVE) ekanini tekshiradi — ixtiyoriy/buzuq yuklamani
    /// pullik Azure chaqirig'iga jo'natmaslik uchun.</summary>
    public static bool LooksLikeWav(byte[] b) =>
        b.Length >= 12 &&
        b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' &&
        b[8] == (byte)'W' && b[9] == (byte)'A' && b[10] == (byte)'V' && b[11] == (byte)'E';

    public static async Task<SpeakingResultDto> AssessAsync(
        byte[] wav, string referenceText, string key, string region, string language = "en-US")
    {
        SpeakingResultDto Err(string m) => new("", 0, 0, 0, 0, 0, new(), m);
        try
        {
            // Pronunciation Assessment konfiguratsiyasi (base64 header sifatida uzatiladi).
            // MUHIM: ERKIN nutq (unscripted) uchun ReferenceText UMUMAN yuborilmasligi kerak — uni bo'sh
            // string qilib yuborsak Azure talaffuz bahosini qaytarmaydi ("talaffuz bahosi qaytmadi").
            // Reference matn berilsa — scripted (aniq, shu matnga), bo'lmasa — erkin nutq baholash.
            object config = string.IsNullOrWhiteSpace(referenceText)
                ? new
                {
                    GradingSystem = "HundredMark",
                    Granularity = "Word",
                    Dimension = "Comprehensive",
                    EnableProsodyAssessment = true,
                }
                : new
                {
                    ReferenceText = referenceText,
                    GradingSystem = "HundredMark",
                    Granularity = "Word",
                    Dimension = "Comprehensive",
                    EnableProsodyAssessment = true,
                };
            var configB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config)));
            var url = $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/" +
                      $"cognitiveservices/v1?language={language}&format=detailed&profanity=raw";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Headers.Add("Pronunciation-Assessment", configB64);
            req.Headers.Add("Accept", "application/json");
            var content = new ByteArrayContent(wav);
            // DIQQAT: MediaTypeHeaderValue.Parse "codecs=audio/pcm" dagi '/' ni token sifatida
            // rad etadi ("format ... is invalid"). Shuning uchun xom header sifatida qo'yamiz.
            content.Headers.TryAddWithoutValidation(
                "Content-Type", "audio/wav; codecs=audio/pcm; samplerate=16000");
            req.Content = content;

            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                // Azure javob matnidan haqiqiy sababni olamiz (401 kalit, 403 region, 400 format...).
                var hint = (int)resp.StatusCode switch
                {
                    401 or 403 => "Kalit yoki region noto'g'ri.",
                    400 => "Audio formati/so'rov noto'g'ri.",
                    429 => "Limit (kvota) tugagan.",
                    _ => "Kalit/region to'g'riligini tekshiring.",
                };
                var snippet = string.IsNullOrWhiteSpace(body) ? "" : " — " + body.Trim().Replace("\n", " ");
                if (snippet.Length > 200) snippet = snippet[..200] + "…";
                Console.Error.WriteLine($"[AzureSpeech] HTTP {(int)resp.StatusCode} body={body}");
                return Err($"Azure xato ({(int)resp.StatusCode}). {hint}{snippet}");
            }

            if (string.IsNullOrWhiteSpace(body))
                return Err("Azure bo'sh javob qaytardi.");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("RecognitionStatus", out var st) ? st.GetString() : "";
            if (status != "Success")
                return Err(status == "NoMatch" ? "Nutq aniqlanmadi — balandroq va aniqroq gapiring." : $"Holat: {status}");

            var display = root.TryGetProperty("DisplayText", out var dt) ? dt.GetString() ?? "" : "";
            if (!root.TryGetProperty("NBest", out var nbestArr) || nbestArr.GetArrayLength() == 0)
                return Err("Natija bo'sh.");
            var nbest = nbestArr[0];

            // Xom (lexical) shakl: ITN/bosh harf/tinish normalizatsiyasiz — o'quvchi AYTGAN so'z.
            var lexical = nbest.TryGetProperty("Lexical", out var lxEl) ? lxEl.GetString() ?? "" : "";
            var recognized = string.IsNullOrWhiteSpace(lexical) ? display : lexical;

            // PronunciationAssessment IXTIYORIY: erkin nutq (reference matnsiz) rejimda kelmasligi
            // mumkin. Bu holda hard-error qaytarmaymiz — faqat tanilgan matnni (ballarsiz) qaytaramiz,
            // chaqiruvchi (endpoint) buni aniqlab Gemini tahlilига o'tadi.
            nbest.TryGetProperty("PronunciationAssessment", out var pa);

            static double G(JsonElement e, string p) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetDouble() : 0;

            var words = new List<SpeakingWordDto>();
            if (nbest.TryGetProperty("Words", out var wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
                foreach (var w in wordsEl.EnumerateArray())
                {
                    var word = w.TryGetProperty("Word", out var wd) ? wd.GetString() ?? "" : "";
                    double acc = 0;
                    var errType = "None";
                    if (w.TryGetProperty("PronunciationAssessment", out var wpa))
                    {
                        acc = G(wpa, "AccuracyScore");
                        errType = wpa.TryGetProperty("ErrorType", out var et) ? et.GetString() ?? "None" : "None";
                    }
                    words.Add(new SpeakingWordDto(word, acc, errType));
                }

            // DIAGNOSTIKA (docker compose logs app): Azure aynan nima qaytardi — talaffuz keldimi, ballar, so'z soni.
            Console.Error.WriteLine(
                $"[AzureSpeech] scripted={!string.IsNullOrWhiteSpace(referenceText)} status={status} " +
                $"hasPA={pa.ValueKind == JsonValueKind.Object} pron={G(pa, "PronScore")} acc={G(pa, "AccuracyScore")} " +
                $"words={words.Count} text=\"{(recognized.Length > 60 ? recognized[..60] : recognized)}\"");

            return new SpeakingResultDto(
                recognized, G(pa, "PronScore"), G(pa, "AccuracyScore"), G(pa, "FluencyScore"),
                G(pa, "CompletenessScore"), G(pa, "ProsodyScore"), words, null);
        }
        catch (TaskCanceledException)
        {
            return Err("Vaqt tugadi (timeout) — qaytadan urinib ko'ring.");
        }
        catch (Exception ex)
        {
            return Err($"Xatolik: {ex.Message}");
        }
    }
}
