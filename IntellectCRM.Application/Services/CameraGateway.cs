using System.Text;
using System.Text.Json;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

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
    /// <param name="record">Shu kamera YOZIB borilsinmi — <see cref="CameraRules.ShouldRecord"/>
    /// natijasi. ⚠️ Chaqiruvchi bosh kalitni O'ZI hisoblab beradi (shlyuz bazani bilmaydi).
    /// <c>false</c> bo'lsa kamera baribir ro'yxatdan o'tadi va JONLI ko'rinadi — lekin diskka
    /// yozilmaydi va RTSP faqat kimdir qaraganda ochiladi.</param>
    public async Task EnsureAsync(Camera cam, bool record)
    {
        var name = PathName(cam.Id);
        var src = !string.IsNullOrWhiteSpace(cam.RtspUrl) ? cam.RtspUrl : cam.RtspSubUrl;
        if (string.IsNullOrWhiteSpace(src)) return;
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
        if (!add) await PostAsync($"{ApiBase}/v3/config/paths/patch/{name}", body);
    }

    /// <summary>Kamerani shlyuzdan olib tashlaydi.</summary>
    public async Task RemoveAsync(string cameraId)
    {
        try { await http.PostAsync($"{ApiBase}/v3/config/paths/delete/{PathName(cameraId)}", null); }
        catch { /* shlyuz mavjud bo'lmasligi mumkin — e'tibor bermaymiz */ }
    }

    private async Task<bool> PostAsync(string url, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(url, content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
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
