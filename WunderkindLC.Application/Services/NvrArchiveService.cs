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

        // stderr DARHOL o'qila boshlaydi: (1) bufer to'lib ffmpeg qotib qolmasin, (2) xato bo'lsa
        // SABAB shu yerdan olinadi. ⚠️ Matnda `login:parol@` bor — faqat RedactText bilan chiqadi.
        var stderr = proc.StandardError.ReadToEndAsync();
        var safeUrl = HikvisionNvr.Redact(url);

        // ⚠️ BIRINCHI BO'LAK kutiladi, keyin javob boshlanadi. Ilgari oqim darhol `File()` ga
        // berilardi: ffmpeg ulana olmasa (yoki argument xato bo'lsa) javob allaqachon "200 OK"
        // bo'lib ketgan, klient esa 0 baytlik MP4 olib, sababni hech qachon ko'rmasdi.
        var first = new byte[64 * 1024];
        // ⚠️ Kutish `Task.WhenAny` bilan: quvur (pipe) o'qishini bekor qilish tokeni har platformada
        // ham hurmat qilinmaydi — vaqt tugasa jarayon O'LDIRILADI, o'qish shundan keyin o'zi tugaydi.
        var read = proc.StandardOutput.BaseStream.ReadAsync(first, 0, first.Length);
        var winner = await Task.WhenAny(read, Task.Delay(FirstChunkTimeout, ct));
        if (winner != read)
        {
            Kill(proc);
            try { await read; } catch (Exception) { /* jarayon o'ldirildi — o'qish xatosi kutilgan */ }
            var reason = ct.IsCancellationRequested
                ? "So'rov bekor qilindi."
                : $"NVR {(int)FirstChunkTimeout.TotalSeconds} soniyada video bermadi "
                  + "(manzil/port serverdan ko'rinmayapti yoki shu vaqtda yozuv yo'q).";
            log.LogWarning("[nvr] klip: birinchi bo'lak kelmadi {Url}", safeUrl);
            proc.Dispose();
            return (null, reason);
        }
        int n;
        try { n = await read; }
        catch (IOException) { n = 0; }

        if (n == 0)
        {
            // Oqim BO'SH tugadi — ffmpeg xato bilan chiqib ketgan. Sabab stderr'dan (tozalangan).
            string err;
            try
            {
                await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                err = await stderr.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException) { Kill(proc); err = ""; }
            catch (IOException) { err = ""; }
            int? code = proc.HasExited ? proc.ExitCode : null;
            var safeErr = HikvisionNvr.RedactText(err);
            log.LogWarning("[nvr] ffmpeg video bermadi (kod {Code}) {Url}: {Err}",
                code, safeUrl, safeErr.Length > 500 ? safeErr[..500] : safeErr);
            proc.Dispose();
            return (null, HikvisionNvr.ClipFailureReason(err, code));
        }

        // ⚠️ Buferga YIG'MAYMIZ: 1 soatlik klip yuzlab MB bo'lishi mumkin va 1 GB RAM'li
        // serverni yiqitardi. Birinchi bo'lakdan keyin oqim to'g'ridan-to'g'ri javobga ulanadi.
        return (new FfmpegStream(proc, first.AsMemory(0, n), stderr, safeUrl, FfmpegTimeout(durationSec), log), null);
    }

    /// <summary>ffmpeg birinchi video bo'lagini shuncha vaqt ichida bermasa — xato (RTSP
    /// kutish vaqti 15 s + zaxira).</summary>
    private static readonly TimeSpan FirstChunkTimeout = TimeSpan.FromSeconds(25);

    private static void Kill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* jarayon allaqachon yo'q */ }
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
    /// (har biri RTSP oqimini tortib turadi). Avval oldindan o'qilgan BIRINCHI bo'lak beriladi.
    /// </remarks>
    private sealed class FfmpegStream(
        Process proc, ReadOnlyMemory<byte> prefix, Task<string> stderr, string safeUrl, TimeSpan timeout, ILogger log)
        : Stream
    {
        private readonly Stream inner = proc.StandardOutput.BaseStream;
        private readonly CancellationTokenSource life = new(timeout);
        private ReadOnlyMemory<byte> pending = prefix;
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (TakePending(buffer.AsSpan(offset, count)) is var k and > 0) return k;
            return inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (TakePending(buffer.Span) is var k and > 0) return k;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token);
            return await inner.ReadAsync(buffer, linked.Token);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private int TakePending(Span<byte> dest)
        {
            if (pending.IsEmpty || dest.IsEmpty) return 0;
            var k = Math.Min(dest.Length, pending.Length);
            pending.Span[..k].CopyTo(dest);
            pending = pending[k..];
            return k;
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
                        // ⚠️ stderr'da `login:parol@` bor — logga faqat tozalangan holda.
                        var err = "";
                        try { if (stderr.Wait(TimeSpan.FromSeconds(1))) err = HikvisionNvr.RedactText(stderr.Result); }
                        catch (AggregateException) { /* stderr o'qilmadi — sababsiz log */ }
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
