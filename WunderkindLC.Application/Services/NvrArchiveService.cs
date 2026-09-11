using System.Diagnostics;
using System.Net;
using System.Text;
using WunderkindLC.Domain;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// NVR (videoregistrator) arxivini olib keladi: ISAPI orqali "qaysi vaqtlarda yozuv bor"
/// qidiruvi va RTSP playback oqimidan MP4 klip.
///
/// <para><b>Nega umuman kerak:</b> NVR allaqachon 24/7 o'z disklariga yozib turibdi. Bizning
/// serverda ikkinchi nusxa saqlash disk va markazning internet kanalini bekorga sarflardi
/// (1080p kamera ≈ 20–43 GB/kun). Endi biz hech narsa yozmaymiz — arxiv NVR'da qoladi va
/// faqat SO'RALGAN bo'lak olib kelinadi.</para>
///
/// <para>Sof qism (manzil, vaqt formati, javobni o'qish) — <see cref="HikvisionNvr"/>.</para>
/// </summary>
public class NvrArchiveService(ILogger<NvrArchiveService> log)
{
    /// <summary>Bitta klipning eng uzun davomiyligi (soniya) — 1 soat.</summary>
    public const int MaxClipSeconds = 3600;

    /// <summary>ffmpeg jarayonining eng uzun ishlash vaqti. Klip davomiyligi + zaxira.</summary>
    private static TimeSpan FfmpegTimeout(int durationSec) =>
        TimeSpan.FromSeconds(Math.Min(durationSec, MaxClipSeconds) + 60);

    /// <summary>NVR ishlatishga TAYYORmi (host + login/parol berilganmi).</summary>
    public static bool IsConfigured(CenterMeta? meta) =>
        meta is { NvrEnabled: true }
        && !string.IsNullOrWhiteSpace(meta.NvrHost)
        && AppSecrets.NvrCredentialsConfigured;

    /// <summary>
    /// Arxiv klipini MP4 oqimi sifatida beradi (ffmpeg orqali remux — qayta kodlash YO'Q).
    /// </summary>
    /// <returns>
    /// Muvaffaqiyatda ochiq oqim; xatoda <c>null</c> + sabab. ⚠️ Sabab foydalanuvchiga
    /// ko'rsatiladi, shuning uchun unda login/parol BO'LMASLIGI shart
    /// (<see cref="HikvisionNvr.Redact"/>).
    /// </returns>
    public async Task<(Stream? Stream, string? Error)> ClipAsync(
        CenterMeta meta, int channel, DateTime startLocal, int durationSec, CancellationToken ct = default)
    {
        if (!IsConfigured(meta))
            return (null, "NVR sozlanmagan (manzil yoki .env dagi NVR_USERNAME/NVR_PASSWORD yo'q).");
        if (channel <= 0)
            return (null, "Bu kameraning NVR kanali ko'rsatilmagan.");

        durationSec = Math.Clamp(durationSec, 1, MaxClipSeconds);
        var url = HikvisionNvr.PlaybackUrl(
            meta.NvrHost, meta.NvrRtspPort, AppSecrets.NvrUsername, AppSecrets.NvrPassword,
            channel, startLocal, startLocal.AddSeconds(durationSec));

        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in HikvisionNvr.FfmpegArgs(url, durationSec)) psi.ArgumentList.Add(a);

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex)
        {
            log.LogError(ex, "[nvr] ffmpeg ishga tushmadi");
            return (null, "ffmpeg topilmadi yoki ishga tushmadi — server obrazini tekshiring.");
        }
        if (proc is null) return (null, "ffmpeg ishga tushmadi.");

        // ⚠️ Buferga YIG'MAYMIZ: 1 soatlik klip yuzlab MB bo'lishi mumkin va 1 GB RAM'li
        // serverni yiqitardi. Oqim to'g'ridan-to'g'ri javobga ulanadi.
        return (new FfmpegStream(proc, HikvisionNvr.Redact(url), FfmpegTimeout(durationSec), log), null);
    }

    /// <summary>
    /// "Shu kanalda shu oraliqda yozuv bormi" — ISAPI qidiruvi. Sozlash/diagnostika uchun
    /// ham shu ishlatiladi ("NVR'ni sinash").
    /// </summary>
    public async Task<(List<HikvisionNvr.Segment> Segments, string? Error)> SearchAsync(
        CenterMeta meta, int channel, DateTime fromLocal, DateTime toLocal, CancellationToken ct = default)
    {
        if (!IsConfigured(meta))
            return ([], "NVR sozlanmagan (manzil yoki .env dagi NVR_USERNAME/NVR_PASSWORD yo'q).");
        if (channel <= 0) return ([], "Kanal ko'rsatilmagan.");

        var port = meta.NvrIsapiPort > 0 ? meta.NvrIsapiPort : 80;
        var baseUrl = $"http://{meta.NvrHost}:{port}";
        using var handler = new HttpClientHandler
        {
            // Hikvision odatda Digest — HttpClientHandler uni O'ZI hal qiladi (turniketdagi naqsh).
            Credentials = new NetworkCredential(AppSecrets.NvrUsername, AppSecrets.NvrPassword),
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        try
        {
            // ⚠️ searchID har so'rovda YANGI: bir xil id "oldingi qidiruvning keyingi sahifasi"
            // deb talqin qilinadi va ikkinchi so'rov bo'sh qaytarardi.
            var body = HikvisionNvr.SearchBody(Guid.NewGuid().ToString(), channel, fromLocal, toLocal);
            using var content = new StringContent(body, Encoding.UTF8, "application/xml");
            using var resp = await http.PostAsync($"{baseUrl}/ISAPI/ContentMgmt/search", content, ct);
            var xml = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return ([], $"NVR javobi: {(int)resp.StatusCode} {resp.ReasonPhrase}. "
                          + "Manzil, port va login/parolni tekshiring.");

            var (segments, status) = HikvisionNvr.ParseSearch(xml);
            if (segments.Count == 0 && status.Length > 0 && !status.Equals("OK", StringComparison.OrdinalIgnoreCase))
                return ([], $"NVR: {status}");
            return (segments, null);
        }
        catch (TaskCanceledException)
        {
            return ([], $"NVR javob bermadi (20 s). Manzil {meta.NvrHost}:{port} serverdan ko'rinadimi?");
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "[nvr] ISAPI qidiruv xatosi");
            return ([], $"NVR bilan bog'lanib bo'lmadi ({meta.NvrHost}:{port}): {ex.Message}");
        }
    }

    /// <summary>
    /// ffmpeg stdout'i — o'qish tugagach jarayonni yopadi.
    /// </summary>
    /// <remarks>
    /// ⚠️ Alohida sinf kerak, chunki oddiy <c>proc.StandardOutput.BaseStream</c> qaytarilsa
    /// klient ulanishni uzganda ffmpeg <b>yetim</b> qolib, serverda to'planib ketardi
    /// (har biri RTSP oqimini tortib turadi).
    /// </remarks>
    private sealed class FfmpegStream(Process proc, string safeUrl, TimeSpan timeout, ILogger log) : Stream
    {
        private readonly Stream inner = proc.StandardOutput.BaseStream;
        private readonly CancellationTokenSource life = new(timeout);
        private bool closed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token);
            return await inner.ReadAsync(buffer, linked.Token);
        }

        protected override void Dispose(bool disposing)
        {
            if (!closed)
            {
                closed = true;
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    else if (proc.ExitCode != 0)
                    {
                        var err = proc.StandardError.ReadToEnd();
                        log.LogWarning("[nvr] ffmpeg xato (kod {Code}) {Url}: {Err}",
                            proc.ExitCode, safeUrl, err.Length > 500 ? err[..500] : err);
                    }
                }
                catch (InvalidOperationException) { /* jarayon allaqachon yo'q */ }
                proc.Dispose();
                life.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
