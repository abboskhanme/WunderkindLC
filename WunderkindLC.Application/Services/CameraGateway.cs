using System.Text;
using System.Text.Json;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Media-shlyuz (MediaMTX) bilan ishlaydi: kamera RTSP oqimini shlyuzga ro'yxatdan o'tkazadi
/// (jonli HLS + 24/7 yozib borish), HLS va playback (yozuvdan MP4) oqimlarini brauzerga
/// proksilaydi. Shlyuz docker ichki tarmog'ida — tashqariga ochilmaydi.
/// </summary>
public class CameraGateway(HttpClient http)
{
    private static string Env(string key, string def) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v.TrimEnd('/') : def;

    private static string ApiBase => Env("MEDIAMTX_API", "http://mediamtx:9997");
    private static string HlsBase => Env("MEDIAMTX_HLS", "http://mediamtx:8888");
    private static string PlaybackBase => Env("MEDIAMTX_PLAYBACK", "http://mediamtx:9996");

    /// <summary>MediaMTX yo'l (path) nomi — faqat harf/raqam (GUID'dagi chiziqchalar olib tashlanadi).</summary>
    public static string PathName(string cameraId) => "cam" + cameraId.Replace("-", "");

    /// <summary>
    /// Kamerani shlyuzga ro'yxatdan o'tkazadi (yoki yangilaydi): RTSP manba + yozib borish.
    /// </summary>
    /// <param name="cam">Kamera.</param>
    /// <param name="record">Shu kamera YOZIB borilsinmi — <see cref="CameraRules.ShouldRecordWithNvr"/>
    /// natijasi. ⚠️ Chaqiruvchi bosh kalitni O'ZI hisoblab beradi (shlyuz bazani bilmaydi).
    /// <c>false</c> bo'lsa kamera baribir ro'yxatdan o'tadi va JONLI ko'rinadi — lekin diskka
    /// yozilmaydi va RTSP faqat kimdir qaraganda ochiladi.</param>
    /// <returns><c>null</c> — shlyuz qabul qildi; aks holda foydalanuvchiga ko'rsatiladigan SABAB.</returns>
    /// <remarks>
    /// ⚠️ Ilgari xato JIM yutilardi: shlyuz o'chiq/qayta ishga tushayotgan paytda saqlangan kamera
    /// "saqlandi" deb ko'rinardi, aslida esa yozuv YOQILMAGAN bo'lardi. Endi sabab qaytadi va
    /// chaqiruvchi uni ko'rsatadi; <see cref="CameraResyncService"/> esa davriy ravishda qayta yuboradi.
    /// ⚠️ Sabab matnida RTSP manzili (ichida login/parol) BO'LMAYDI — faqat HTTP holati.
    /// </remarks>
    public async Task<string?> EnsureAsync(Camera cam, bool record)
    {
        var name = PathName(cam.Id);
        var src = !string.IsNullOrWhiteSpace(cam.RtspUrl) ? cam.RtspUrl : cam.RtspSubUrl;
        if (string.IsNullOrWhiteSpace(src)) return null;
        var body = JsonSerializer.Serialize(new
        {
            source = src,
            // Yozuv bor -> 24/7 ulanib turadi (uzluksiz yozuv uchun).
            // Yozuv yo'q -> faqat kimdir qarab turganda ulanadi: disk ham, markazning
            // 24/7 internet trafigi ham sarflanmaydi.
            sourceOnDemand = CameraRules.SourceOnDemand(record),
            record,
            // Saqlash muddati: shu vaqtdan eski yozuv segmentlari shlyuz tomonidan AVTOMATIK o'chiriladi.
            // 0 kun = cheksiz ("0s" = o'chirish o'chirilgan).
            recordDeleteAfter = CameraRules.RecordDeleteAfter(cam.RetentionDays),
        });

        // Avval qo'shamiz; mavjud bo'lsa (400) — yangilaymiz (patch).
        var add = await PostAsync($"{ApiBase}/v3/config/paths/add/{name}", body);
        if (add.Ok) return null;
        var patch = await PostAsync($"{ApiBase}/v3/config/paths/patch/{name}", body);
        if (patch.Ok) return null;
        return $"Media-shlyuz kamerani qabul qilmadi ({patch.Error ?? add.Error}). "
               + "Shlyuz ishlayotganini tekshiring — tizim uni har 10 daqiqada avtomatik qayta yuboradi.";
    }

    /// <summary>
    /// Kamerani shlyuzdan olib tashlaydi VA uning diskdagi yozuvlarini o'chiradi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Yozuvlar ham o'chiriladi: kamera o'chgach ularga hech qanday yo'l qolmaydi (klip endpointi
    /// kamerani talab qiladi), lekin `cam-recordings` volume'ida ABADIY joy egallab turardi
    /// (zaxiraga ham kirmaydi). Fayllar shlyuz konteynerida — o'chirish MediaMTX API'si orqali.
    /// </remarks>
    /// <returns>O'chirilgan segmentlar soni (shlyuz yo'q bo'lsa 0).</returns>
    public async Task<int> RemoveAsync(string cameraId)
    {
        try { await http.PostAsync($"{ApiBase}/v3/config/paths/delete/{PathName(cameraId)}", null); }
        catch { /* shlyuz mavjud bo'lmasligi mumkin — e'tibor bermaymiz */ }
        return await DeleteSegmentsAsync(cameraId, olderThanUtc: null);
    }

    /// <summary>
    /// YOZUVI O'CHIRILGAN kameraning eski segmentlarini saqlash muddati bo'yicha tozalaydi.
    /// </summary>
    /// <remarks>
    /// ⚠️ MediaMTX eski segmentlarni faqat YOZILAYOTGAN yo'lda o'zi o'chiradi (<c>recordDeleteAfter</c>).
    /// Yozuv o'chirilgach tozalovchi to'xtaydi va ilgari yozilgan arxiv ABADIY qolib ketardi.
    /// Shuning uchun <see cref="CameraResyncService"/> yozilmayotgan kameralar uchun shu metodni
    /// chaqiradi: arxiv odatdagi muddati tugaguncha ko'rinadi, keyin o'chadi. 0 kun = cheksiz (tegilmaydi).
    /// </remarks>
    public Task<int> PruneAsync(string cameraId, int retentionDays) =>
        retentionDays <= 0
            ? Task.FromResult(0)
            : DeleteSegmentsAsync(cameraId, DateTime.UtcNow.AddDays(-retentionDays));

    /// <summary>Kameraning yozuv segmentlarini o'chiradi (<paramref name="olderThanUtc"/> berilsa —
    /// faqat undan eskilarini). Shlyuz javob bermasa jim 0 qaytaradi (keyingi siklda qayta urinadi).</summary>
    private async Task<int> DeleteSegmentsAsync(string cameraId, DateTime? olderThanUtc)
    {
        var name = PathName(cameraId);
        try
        {
            using var resp = await http.GetAsync($"{ApiBase}/v3/recordings/get/{name}");
            if (!resp.IsSuccessStatusCode) return 0;      // yozuv yo'q (404) yoki shlyuz eski versiya
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("segments", out var segs) || segs.ValueKind != JsonValueKind.Array)
                return 0;

            var deleted = 0;
            foreach (var seg in segs.EnumerateArray())
            {
                var start = seg.TryGetProperty("start", out var st) ? st.GetString() ?? "" : "";
                if (start.Length == 0) continue;
                if (olderThanUtc is { } cutoff
                    && (!DateTimeOffset.TryParse(start, out var at) || at.UtcDateTime >= cutoff))
                    continue;
                using var del = await http.DeleteAsync(
                    $"{ApiBase}/v3/recordings/deletesegment?path={name}&start={Uri.EscapeDataString(start)}");
                if (del.IsSuccessStatusCode) deleted++;
            }
            return deleted;
        }
        catch { return 0; }
    }

    private async Task<(bool Ok, string? Error)> PostAsync(string url, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content);
            return resp.IsSuccessStatusCode
                ? (true, null)
                : (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "shlyuz javob bermadi");
        }
    }

    /// <summary>Jonli HLS faylini (index.m3u8 yoki segment) shlyuzdan oladi (proksi uchun).</summary>
    public Task<HttpResponseMessage> HlsAsync(string cameraId, string file) =>
        http.GetAsync($"{HlsBase}/{PathName(cameraId)}/{file}", HttpCompletionOption.ResponseHeadersRead);

    /// <summary>Yozuvdan MP4 olib beradi (playback / qirqib yuklab olish). start ISO, duration soniyada.</summary>
    public Task<HttpResponseMessage> PlaybackAsync(string cameraId, string startIso, int durationSec) =>
        http.GetAsync(
            $"{PlaybackBase}/get?path={PathName(cameraId)}&start={Uri.EscapeDataString(startIso)}&duration={durationSec}&format=mp4",
            HttpCompletionOption.ResponseHeadersRead);
}
