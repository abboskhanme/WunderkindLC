using System.Text;
using System.Xml.Linq;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Hikvision NVR (videoregistrator) arxivi bilan ishlash — <b>sof qism</b> (manzil qurish,
/// vaqt formati, javobni o'qish). Tarmoq chaqiruvlari <see cref="NvrArchiveService"/> da.
/// Testlar: <c>HikvisionNvrTests</c>.
///
/// <para>NVR allaqachon 24/7 o'z disklariga yozib turadi, shuning uchun biz hech narsa
/// yozmaymiz — faqat so'ralgan bo'lakni undan olib beramiz. Batafsil:
/// <c>.claude/rules/cameras.md</c> §8.</para>
/// </summary>
public static class HikvisionNvr
{
    /// <summary>
    /// Hikvision "track" identifikatori: <c>{kanal}{oqim}</c>, oqim 01 = asosiy, 02 = sub.
    /// Kanal 1 → "101", kanal 2 → "201", kanal 12 → "1201".
    /// </summary>
    public static string TrackId(int channel, bool sub = false) =>
        $"{channel}{(sub ? "02" : "01")}";

    /// <summary>
    /// RTSP playback manzilidagi vaqt formati: <c>yyyyMMddTHHmmssZ</c>, <b>UTC</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Bu yerda eng ko'p xato qilinadi.</b> Foydalanuvchi ekranda MARKAZ vaqtini (UTC+5)
    /// tanlaydi, Hikvision esa UTC kutadi. Konversiya qilinmasa arxiv <b>5 soat surilgan</b>
    /// holda kelardi — va bu "yozuv topilmadi" emas, "boshqa vaqtning yozuvi" bo'lgani uchun
    /// darhol sezilmasdi.
    /// </remarks>
    public static string PlaybackTime(DateTime centerLocal) =>
        AppClock.ToUtc(centerLocal).ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>
    /// Arxiv RTSP manzili (login/parol ichida — Hikvision boshqa yo'lni qabul qilmaydi).
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu satr <b>hech qachon</b> API javobiga yoki logga tushmasligi kerak —
    /// <see cref="Redact"/> dan foydalaning.
    /// </remarks>
    public static string PlaybackUrl(
        string host, int port, string user, string password, int channel,
        DateTime startLocal, DateTime endLocal, bool sub = false)
    {
        var cred = string.IsNullOrEmpty(user) ? "" : $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(password)}@";
        return $"rtsp://{cred}{host}:{(port > 0 ? port : 554)}/Streaming/tracks/{TrackId(channel, sub)}"
             + $"?starttime={PlaybackTime(startLocal)}&endtime={PlaybackTime(endLocal)}";
    }

    /// <summary>Login/parolni manzildan olib tashlaydi — log va xato matnlari uchun.</summary>
    /// <remarks>
    /// ⚠️ Har qanday xato xabari NVR manzilini o'z ichiga oladi (diagnostika uchun kerak),
    /// shuning uchun tozalash MAJBURIY: aks holda parol log fayliga va ekranga chiqib ketardi.
    /// </remarks>
    public static string Redact(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return url;
        var at = url.IndexOf('@', scheme);
        return at < 0 ? url : string.Concat(url.AsSpan(0, scheme + 3), "***:***@", url.AsSpan(at + 1));
    }

    /// <summary>
    /// ISAPI qidiruv so'rovi (CMSearchDescription) — "shu kanalda shu oraliqda yozuv bormi".
    /// </summary>
    /// <param name="searchId">Har so'rovda YANGI GUID. Hikvision bir xil id bilan kelgan
    /// so'rovni "oldingi qidiruvning davomi" deb hisoblaydi va navbatdagi sahifani qaytaradi.</param>
    public static string SearchBody(string searchId, int channel, DateTime fromLocal, DateTime toLocal,
        int maxResults = 40)
    {
        // ISAPI XML'da vaqt ISO-8601, UTC ("...Z") — RTSP'dagi bilan bir xil mintaqa qoidasi.
        static string T(DateTime local) => AppClock.ToUtc(local).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <CMSearchDescription>
              <searchID>{searchId}</searchID>
              <trackIDList><trackID>{TrackId(channel)}</trackID></trackIDList>
              <timeSpanList>
                <timeSpan><startTime>{T(fromLocal)}</startTime><endTime>{T(toLocal)}</endTime></timeSpan>
              </timeSpanList>
              <maxResults>{maxResults}</maxResults>
              <searchResultPosition>0</searchResultPosition>
              <metadataList><metadataDescriptor>//recordType.meta.std-cgi.com</metadataDescriptor></metadataList>
            </CMSearchDescription>
            """;
    }

    /// <summary>Arxivdagi bitta uzluksiz bo'lak (markaz vaqtida, "yyyy-MM-ddTHH:mm:ss").</summary>
    public record Segment(string Start, string End);

    /// <summary>
    /// ISAPI qidiruv javobini (XML) o'qiydi: topilgan bo'laklar + status matni.
    /// </summary>
    /// <remarks>
    /// ⚠️ Nom maydoni (namespace) firmware'dan firmware'ga farq qiladi, shuning uchun elementlar
    /// <b>lokal nom</b> bo'yicha qidiriladi — aks holda javob to'g'ri kelgani bilan ro'yxat bo'sh
    /// chiqardi va buni "yozuv yo'q" deb tushunish oson bo'lardi.
    /// </remarks>
    public static (List<Segment> Segments, string Status) ParseSearch(string xml)
    {
        var segments = new List<Segment>();
        var status = "";
        try
        {
            var doc = XDocument.Parse(xml);
            status = Local(doc.Root, "responseStatusStrg") ?? "";
            foreach (var item in doc.Descendants().Where(e => e.Name.LocalName == "searchMatchItem"))
            {
                var span = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "timeSpan");
                var s = Local(span, "startTime");
                var e2 = Local(span, "endTime");
                if (s is null || e2 is null) continue;
                if (DateTimeOffset.TryParse(s, out var so) && DateTimeOffset.TryParse(e2, out var eo))
                    segments.Add(new Segment(
                        AppClock.ToLocal(so.UtcDateTime).ToString("yyyy-MM-ddTHH:mm:ss"),
                        AppClock.ToLocal(eo.UtcDateTime).ToString("yyyy-MM-ddTHH:mm:ss")));
            }
        }
        catch (System.Xml.XmlException)
        {
            // Buzuq/HTML javob (masalan login sahifasi) — bo'sh ro'yxat + sabab.
            return ([], "Javob XML emas — manzil yoki login/parol noto'g'ri bo'lishi mumkin.");
        }
        return (segments, status);
    }

    private static string? Local(XElement? parent, string name) =>
        parent?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>
    /// ffmpeg argumentlari: RTSP arxiv oqimini <b>qayta kodlamasdan</b> (remux) MP4 ga o'girish.
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <c>-c copy</c> ATAYIN: qayta kodlash (transcode) 1 GB RAM'li serverni ushlab
    /// qolardi. Kameralar H.264 da (jonli HLS <c>mpegts</c> variantida ishlayapti, u H.265 ni
    /// qo'llab-quvvatlamaydi) — ya'ni nusxa ko'chirish brauzer uchun yetarli.</para>
    /// <para>⚠️ <c>frag_keyframe+empty_moov</c> — MP4 ni <b>oqim sifatida</b> berish uchun:
    /// oddiy MP4 da indeks (moov) fayl OXIRIDA yoziladi, ya'ni javobni boshlashdan oldin butun
    /// klipni diskka yig'ish kerak bo'lardi.</para>
    /// <para>⚠️ <c>-rtsp_transport tcp</c> — UDP da uzoq masofada paketlar yo'qolib, video
    /// "to'kilib" chiqardi.</para>
    /// </remarks>
    public static string[] FfmpegArgs(string url, int durationSec) =>
    [
        "-hide_banner", "-loglevel", "error",
        "-rtsp_transport", "tcp",
        "-stimeout", "15000000",          // ulanish/o'qish kutish vaqti (mikrosoniya) = 15 s
        "-i", url,
        "-t", durationSec.ToString(),
        "-c", "copy",
        "-movflags", "frag_keyframe+empty_moov+default_base_moof",
        "-f", "mp4", "pipe:1",
    ];
}
