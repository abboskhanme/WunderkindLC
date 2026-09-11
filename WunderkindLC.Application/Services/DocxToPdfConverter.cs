using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// WORD (.docx) → PDF konvertori — <b>LibreOffice headless</b> orqali.
///
/// <para><b>Nega LibreOffice?</b> Sertifikat "hech narsa o'zgarmasdan", ya'ni Word'da qanday
/// ko'rinsa PDF'da ham aynan shunday (chop etilgan holatda) chiqishi kerak. .NET uchun bepul
/// kutubxonalar (OpenXml) faqat MAZMUNNI o'qiy oladi — sahifani qayta chizmaydi, shrift/joylashuv
/// buziladi. To'liq ishonchli bepul yagona yo'l — LibreOffice'ning o'z render dvigateli.</para>
///
/// <para><b>Mavjud bo'lmasa nima bo'ladi?</b> Hech narsa buzilmaydi: <see cref="ConvertAsync"/>
/// <c>null</c> qaytaradi, chaqiruvchi esa faqat .docx ni saqlaydi va foydalanuvchiga
/// "PDF konvertori o'rnatilmagan" deb ko'rsatadi. Ya'ni server LibreOfficesiz ham ishlaydi.</para>
///
/// <para><b>1GB RAM eslatmasi:</b> LibreOffice bitta konvertatsiyada ~150-200MB oladi, shuning uchun
/// chaqiruvlar <see cref="Gate"/> bilan NAVBAT bilan bajariladi (bir vaqtda bittasi). 30 ta
/// sertifikat ketma-ket ~30-60 soniyada chiqadi — bu fon jarayoni emas, so'rov ichida bo'lgani
/// uchun controller'da katta timeout kerak.</para>
/// </summary>
public class DocxToPdfConverter(ILogger<DocxToPdfConverter> logger)
{
    /// <summary>Bir vaqtda FAQAT bitta konvertatsiya (past xotirali serverda OOM bo'lmasin).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Kutish vaqti: sovuq ishga tushish + har faylga qo'shimcha (10 daqiqadan oshmaydi).</summary>
    private static TimeSpan TimeoutFor(int fileCount) =>
        TimeSpan.FromSeconds(Math.Min(600, 60 + 10 * Math.Max(1, fileCount)));

    private static string? _resolved;
    private static bool _searched;
    private static readonly object SearchLock = new();

    /// <summary>
    /// LibreOffice bajaruvchi fayli topilgan yo'l (yoki <c>null</c>). Bir marta izlanadi va keshlanadi.
    /// Qidirish tartibi: <c>LIBREOFFICE_PATH</c> muhit o'zgaruvchisi → PATH → odatiy joylar.
    /// </summary>
    public static string? ExecutablePath
    {
        get
        {
            if (_searched) return _resolved;
            lock (SearchLock)
            {
                if (_searched) return _resolved;
                _resolved = Locate();
                _searched = true;
                return _resolved;
            }
        }
    }

    /// <summary>PDF ga o'girish mumkinmi (UI shu bo'yicha ogohlantirish ko'rsatadi).</summary>
    public static bool IsAvailable => ExecutablePath is not null;

    /// <summary>Kesh — Docker image yangilangach qayta izlash uchun (testlarda ham ishlatiladi).</summary>
    public static void ResetProbe()
    {
        lock (SearchLock) { _searched = false; _resolved = null; }
    }

    private static string? Locate()
    {
        var fromEnv = Environment.GetEnvironmentVariable("LIBREOFFICE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        var names = OperatingSystem.IsWindows()
            ? new[] { "soffice.exe" }
            : new[] { "soffice", "libreoffice" };

        // PATH bo'yicha
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var n in names)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), n);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* PATH ichidagi noto'g'ri belgili yo'l — o'tkazib yuboramiz */ }
            }
        }

        // Odatiy joylar (Docker: /usr/bin/soffice; Windows dev mashinasi)
        string[] common = OperatingSystem.IsWindows()
            ? [
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
              ]
            : [
                "/usr/bin/soffice",
                "/usr/bin/libreoffice",
                "/usr/lib/libreoffice/program/soffice",
                "/opt/libreoffice/program/soffice",
              ];
        return common.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// .docx baytlarini PDF baytlariga o'giradi. Konvertor topilmasa yoki xato bo'lsa —
    /// <c>null</c> (istisno TASHLANMAYDI: sertifikat yaratish to'xtab qolmasin, Word fayl baribir
    /// saqlanadi).
    /// </summary>
    public async Task<byte[]?> ConvertAsync(byte[] docxBytes, CancellationToken ct = default) =>
        (await ConvertManyAsync([docxBytes], ct))[0];

    /// <summary>
    /// BIR NECHTA .docx ni <b>BITTA</b> LibreOffice chaqiruvida PDF ga o'giradi.
    ///
    /// <para><b>Nega bitta chaqiruv?</b> LibreOffice'ning sovuq ishga tushishi ~2-4 soniya va
    /// ~150-200 MB xotira oladi. Har sertifikat uchun alohida jarayon ochilsa, 30 o'quvchilik
    /// guruhda 30 marta shu narx to'lanadi: ~2 daqiqa kutish (Cloudflare Tunnel javobni uzib
    /// yuborishi mumkin) va 30 marta xotira ko'tarilishi — 1 GB RAM li serverda bu xavfli.
    /// Bitta chaqiruvda esa narx BIR MARTA to'lanadi, xotira cho'qqisi ham bitta bo'ladi.</para>
    /// </summary>
    /// <returns>Kirish bilan INDEKS bo'yicha mos massiv; konvertatsiya qilinmagan fayl uchun
    /// <c>null</c> (chaqiruvchi u sertifikatni faqat .docx sifatida saqlaydi).</returns>
    public async Task<byte[]?[]> ConvertManyAsync(
        IReadOnlyList<byte[]> docs, CancellationToken ct = default)
    {
        var result = new byte[]?[docs.Count];
        if (docs.Count == 0) return result;

        var exe = ExecutablePath;
        if (exe is null)
        {
            logger.LogWarning("LibreOffice topilmadi — sertifikatlar faqat .docx sifatida saqlanadi. "
                              + "Docker image'ga libreoffice-writer qo'shing yoki LIBREOFFICE_PATH bering.");
            return result;
        }

        // Konvertatsiya O'Z vaqtinchalik papkasida — LibreOffice profil qulfi (lock) chiqmasin.
        var work = Path.Combine(Path.GetTempPath(), "icrm-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        await Gate.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = work,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // ArgumentList — bo'shliqli yo'llar qo'lda qo'shtirnoqqa olinmaydi (Windows'da muhim).
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--norestore");
            psi.ArgumentList.Add("--nolockcheck");
            psi.ArgumentList.Add("--nodefault");
            psi.ArgumentList.Add("--nologo");
            // Alohida profil: allaqachon ishlab turgan LibreOffice nusxasiga "ulanib" darhol
            // chiqib ketmasin (aks holda fayl konvertatsiya qilinmay qoladi).
            psi.ArgumentList.Add($"-env:UserInstallation=file:///{work.Replace('\\', '/').TrimStart('/')}/profile");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(work);

            // Nomlar indeks bo'yicha: "0.docx" → "0.pdf" — javobni kirish tartibiga qaytarish uchun.
            for (var i = 0; i < docs.Count; i++)
            {
                var input = Path.Combine(work, $"{i}.docx");
                await File.WriteAllBytesAsync(input, docs[i], ct);
                psi.ArgumentList.Add(input);
            }

            using var proc = Process.Start(psi);
            if (proc is null) return result;

            // OQIMLARNI DARHOL DRENAJLAYMIZ. Yo'naltirilgan (redirect) stdout/stderr buferi Linux'da
            // ~64 KB: LibreOffice shrift/konfiguratsiya ogohlantirishlarini shuncha yozib bufer
            // to'lsa, u YOZISHDA bloklanadi va hech qachon tugamaydi — biz esa `WaitForExitAsync`
            // da timeout'gacha kutib, butun bo'lakni PDF'siz qoldirardik. Shuning uchun o'qishni
            // KUTISHDAN OLDIN boshlaymiz.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            var timeout = TimeoutFor(docs.Count);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* allaqachon tugagan */ }
                logger.LogWarning("LibreOffice {Timeout}s ichida tugamadi ({Count} fayl) — PDF yaratilmadi.",
                    timeout.TotalSeconds, docs.Count);
                return result;
            }

            if (proc.ExitCode != 0)
            {
                var err = await stderrTask;
                logger.LogWarning("LibreOffice xato bilan tugadi (exit {Code}): {Error}", proc.ExitCode, err);
                // ExitCode != 0 bo'lsa ham ba'zi fayllar chiqqan bo'lishi mumkin — pastda tekshiramiz.
            }
            else
            {
                // Kutilmasa `ReadToEndAsync` vazifasi kuzatilmagan holda qolib ketardi.
                await stdoutTask;
            }

            // Chiqmagan fayl null bo'lib qoladi: o'sha sertifikat .docx sifatida saqlanadi,
            // qolganlari PDF bo'ladi (bitta buzuq hujjat butun guruhni yiqitmasin).
            var missing = 0;
            for (var i = 0; i < docs.Count; i++)
            {
                var output = Path.Combine(work, $"{i}.pdf");
                if (File.Exists(output)) result[i] = await File.ReadAllBytesAsync(output, ct);
                else missing++;
            }
            if (missing > 0)
                logger.LogWarning("{Missing}/{Total} fayl PDF ga o'girilmadi — ular .docx bo'lib qoladi.",
                    missing, docs.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF konvertatsiyasi xato bilan tugadi — .docx saqlanadi.");
            return result;
        }
        finally
        {
            Gate.Release();
            try { Directory.Delete(work, recursive: true); } catch { /* vaqtinchalik papka — muhim emas */ }
        }
    }
}
