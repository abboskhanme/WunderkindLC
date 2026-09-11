using System.Globalization;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KONTENT MODULINING <b>OCHIQ</b> MEDIA PAPKASI — sof (I/O'siz) qoidalari.
///
/// <para><b>Nega bu papka umuman ochiq?</b> Instagram (Meta) postni joylashda faylni
/// <b>O'ZI yuklab oladi</b>: media manzili ochiq HTTPS bo'lishi SHART — autentifikatsiya,
/// IP cheklov va redirect ishlamaydi (<c>KENGAYTIRISH-PROMPT.md</c> §5.6, Variant A).
/// Loyihaning <c>/uploads</c> papkasi esa <c>UploadsGuard</c> ortida, ya'ni login talab qiladi
/// — natijada har post <c>2207052</c> («Media yuklab bo'lmadi») bilan yiqilardi.</para>
///
/// <para><b>🔴 ENG MUHIM QOIDA:</b> ochiq marshrutdan <b>FAQAT</b> shu papkadagi fayllar
/// chiqadi. <c>uploads/</c> ning qolgan qismi, <c>uploads/certificates</c> va
/// <c>uploads/face</c> avvalgidek YOPIQ. Buni uch qatlam ushlab turadi:</para>
/// <list type="number">
///   <item>ochiq statik marshrut <b>aynan shu jismoniy papkaga</b> ildizlangan
///     (<c>Program.cs</c>) — undan yuqoriga chiqib bo'lmaydi;</item>
///   <item>fayl nomi qat'iy naqsh bilan cheklangan (<see cref="SafeStoredName"/>):
///     32 ta hex belgi + ruxsat etilgan kengaytma. Ya'ni <c>..</c>, absolut yo'l,
///     papka ajratkichi va begona nom UMUMAN qabul qilinmaydi;</item>
///   <item>faqat 4 ta kengaytma va ularning MIME turi beriladi — boshqa fayl (masalan
///     <c>.html</c>) tasodifan tushib qolsa ham berilmaydi.</item>
/// </list>
///
/// <para><b>Nima TUSHADI:</b> faqat rejalashtirilgan post uchun yuklangan rasm/video.
/// <b>Nima TUSHMAYDI:</b> hujjat, shartnoma, sertifikat, selfi, o'quvchi surati — hech narsa.
/// Foydalanuvchi bu faylni Instagram'da baribir OMMAGA chiqarmoqchi, ya'ni maxfiylik
/// darajasi «ommaviy» — bu papka shu ma'noda landing rasmlariga o'xshaydi.</para>
///
/// <para>Sof funksiyalar ATAYIN Application qatlamida: <c>WunderkindLC.Tests</c> loyihasi
/// <c>WunderkindLC.Server</c> ga referens QILMAYDI, ya'ni qoidani faqat shu yerdan
/// haqiqiy test bilan qoplash mumkin (<c>MarketingPublicMediaTests</c>).</para>
/// </summary>
public static class MarketingPublicMedia
{
    /// <summary>Jismoniy papka nomi (<c>ContentRoot/uploads/</c> ostida).</summary>
    public const string FolderName = "marketing-public";

    /// <summary>Ochiq statik marshrut prefiksi.</summary>
    public const string RequestPath = "/uploads/" + FolderName;

    /// <summary>Rasm kengaytmalari — FAQAT JPEG.
    /// <para>Instagram PNG/WebP/HEIC ni <c>2207005</c> bilan rad etadi
    /// (<see cref="InstagramPublishContract.IsJpegUrl"/> ham aynan shunga qaraydi), ya'ni
    /// boshqa turni qabul qilish foydalanuvchini keyinchalik yiqiladigan postga olib borardi.</para></summary>
    public static readonly IReadOnlyList<string> ImageExtensions = [".jpg", ".jpeg"];

    /// <summary>Video kengaytmalari — MP4/MOV (<see cref="InstagramPublishContract.IsVideoUrl"/>).</summary>
    public static readonly IReadOnlyList<string> VideoExtensions = [".mp4", ".mov"];

    /// <summary>
    /// Ochiq marshrutda beriladigan yagona MIME xaritasi.
    ///
    /// <para>⚠️ Bu papka BIZNING domenimizdan ochiq beriladi, ya'ni u yerdan <c>text/html</c>
    /// yoki <c>image/svg+xml</c> chiqsa — bu saqlangan XSS bo'lardi (<c>UploadGuard</c> dagi
    /// bir xil mantiq). Shuning uchun ro'yxat yopiq va <c>ServeUnknownFileTypes=false</c>.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".mp4"] = "video/mp4",
            [".mov"] = "video/quicktime",
        };

    /// <summary>Yuklashda qabul qilinadigan <c>Content-Type</c> lar (mijoz turlicha yuboradi).</summary>
    private static readonly HashSet<string> AcceptedImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/jpg", "image/pjpeg" };

    private static readonly HashSet<string> AcceptedVideoTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "video/mp4", "video/quicktime", "video/x-quicktime", "video/mov", "application/mp4" };

    /// <summary>Saqlangan fayl nomining QAT'IY naqshi: <c>{guid:N}.{kengaytma}</c>.</summary>
    private const int NameHexLength = 32;

    /* ═════════════════════════ 1) Tur va kengaytma ═════════════════════════ */

    /// <summary>Kengaytmani kichik harfda ajratadi (nuqta bilan), topilmasa bo'sh satr.</summary>
    public static string ExtensionOf(string? fileName)
    {
        var s = (fileName ?? "").Trim();
        if (s.Length == 0) return "";
        var cut = s.IndexOfAny(['?', '#']);
        if (cut >= 0) s = s[..cut];
        var dot = s.LastIndexOf('.');
        if (dot < 0 || dot == s.Length - 1) return "";
        return s[dot..].ToLowerInvariant();
    }

    /// <summary>
    /// Kengaytma bo'yicha media turi: <c>image</c> | <c>video</c>, ruxsat etilmagan bo'lsa
    /// <c>null</c> (chaqiruvchi shu holda faylni RAD etadi).
    /// </summary>
    public static string? KindOfExtension(string? fileName)
    {
        var ext = ExtensionOf(fileName);
        if (ext.Length == 0) return null;
        if (ImageExtensions.Contains(ext)) return IgPublishConst.KindImage;
        if (VideoExtensions.Contains(ext)) return IgPublishConst.KindVideo;
        return null;
    }

    /// <summary>Mijoz yuborgan <c>Content-Type</c> kengaytmaga mos keladimi.</summary>
    public static bool ContentTypeMatches(string? kind, string? contentType)
    {
        // ";" dan keyingi parametr ("; charset=...") hisobga olinmaydi.
        var s = (contentType ?? "").Trim();
        var semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi].Trim();
        if (s.Length == 0) return false;
        return kind == IgPublishConst.KindVideo
            ? AcceptedVideoTypes.Contains(s)
            : AcceptedImageTypes.Contains(s);
    }

    /* ═════════════════════════ 2) Mazmun (sehrli baytlar) ═════════════════════════ */

    /// <summary>
    /// Fayl BOSHIDAN haqiqiy turini aniqlaydi: <c>image</c> | <c>video</c> | <c>null</c>.
    ///
    /// <para>⚠️ Kengaytma va <c>Content-Type</c> — mijoz aytgan gap, ularga ishonib bo'lmaydi.
    /// Papka OCHIQ va bizning domenimizda bo'lgani uchun u yerga <c>.jpg</c> nomi bilan HTML
    /// qo'yilsa, <c>nosniff</c> bo'lsa ham bu xavfli naqsh. Shuning uchun mazmun ham
    /// tekshiriladi — uchala manba (nom · sarlavha · baytlar) mos kelishi SHART.</para>
    /// </summary>
    public static string? SniffKind(ReadOnlySpan<byte> head)
    {
        // JPEG: SOI (FF D8) + keyingi marker (FF).
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return IgPublishConst.KindImage;

        // ISO BMFF (MP4/MOV): 4 bayt hajm + "ftyp". QuickTime'da "moov"/"mdat"/"wide"/"free"
        // ham birinchi box bo'lishi mumkin.
        if (head.Length >= 12)
        {
            var box = Ascii(head.Slice(4, 4));
            if (box is "ftyp" or "moov" or "mdat" or "wide" or "free" or "skip" or "pnot")
                return IgPublishConst.KindVideo;
        }
        return null;
    }

    private static string Ascii(ReadOnlySpan<byte> s)
    {
        Span<char> buf = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++) buf[i] = (char)s[i];
        return new string(buf);
    }

    /* ═════════════════════════ 3) Yuklash darvozasi ═════════════════════════ */

    /// <summary>Shu tur uchun ruxsat etilgan eng katta hajm (bayt).
    /// <para>Chegaralar <see cref="IgPublishConst"/> dan olinadi — Meta'niki bilan bitta joyda
    /// tursin, aks holda serverga sig'gan fayl Instagram'da rad etilardi.</para></summary>
    public static long MaxBytesFor(string? kind) =>
        kind == IgPublishConst.KindVideo ? IgPublishConst.MaxReelsBytes : IgPublishConst.MaxImageBytes;

    /// <summary>
    /// Yuklanayotgan faylni tekshiradi. Hammasi joyida bo'lsa <c>(null, kind)</c>,
    /// aks holda <c>(xato matni, "")</c>.
    /// </summary>
    /// <param name="fileName">Mijoz yuborgan asl nom (faqat kengaytmasi olinadi).</param>
    /// <param name="contentType">Mijoz yuborgan <c>Content-Type</c>.</param>
    /// <param name="length">Fayl hajmi (bayt).</param>
    /// <param name="head">Fayl boshidagi baytlar (kamida 12 ta) — mazmun tekshiruvi uchun.</param>
    public static (string? Error, string Kind) Validate(
        string? fileName, string? contentType, long length, ReadOnlySpan<byte> head)
    {
        if (length <= 0) return ("Fayl bo'sh.", "");

        var kind = KindOfExtension(fileName);
        if (kind is null)
            return ("Instagram uchun faqat JPEG rasm (.jpg/.jpeg) yoki MP4/MOV video qabul qilinadi.", "");

        var max = MaxBytesFor(kind);
        if (length > max)
            return ($"Fayl juda katta: {Mb(length)} MB (ruxsat {Mb(max)} MB).", "");

        if (!ContentTypeMatches(kind, contentType))
            return ("Fayl turi (Content-Type) kengaytmaga mos kelmadi.", "");

        var sniffed = SniffKind(head);
        if (sniffed is null || sniffed != kind)
            return ("Fayl mazmuni kengaytmaga mos kelmadi (buzuq yoki boshqa turdagi fayl).", "");

        return (null, kind);
    }

    private static string Mb(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// Saqlash uchun nom: <c>{Guid:N}{kengaytma}</c> — 128 bit tasodifiylik, asl nom
    /// (masalan <c>reklama-oktabr.jpg</c>) HECH QACHON saqlanmaydi
    /// (<c>uploads-security.md</c> dagi <c>UploadGuard.SafeName</c> naqshi).
    /// </summary>
    public static string NewStoredName(string? originalFileName)
    {
        var ext = ExtensionOf(originalFileName);
        if (!ContentTypes.ContainsKey(ext))
            throw new ArgumentException("Ruxsat etilmagan kengaytma", nameof(originalFileName));
        return $"{Guid.NewGuid():N}{ext}";
    }

    /* ═════════════════════════ 4) O'chirish darvozasi (yo'ldan chiqib ketish) ═════════════════════════ */

    /// <summary>
    /// Manzil yoki nomdan XAVFSIZ saqlangan fayl nomini ajratadi. Naqshga tushmasa
    /// <c>null</c> — chaqiruvchi hech narsa qilmaydi.
    ///
    /// <para><b>🔴 Bu funksiya — papkadan chiqib ketishga (path traversal) qarshi asosiy
    /// darvoza.</b> "Nimani rad etamiz" ro'yxati bilan emas, "nima ruxsat etilgan" naqshi
    /// bilan ishlaydi: <b>aynan 32 ta hex belgi + ruxsat etilgan kengaytma</b>. Shu sababdan
    /// quyidagilarning HAMMASI o'z-o'zidan rad etiladi:</para>
    /// <list type="bullet">
    ///   <item><c>..</c> va <c>../../</c> (nuqta hex emas);</item>
    ///   <item>absolut yo'l (<c>/etc/passwd</c>, <c>C:\...</c>) va UNC (<c>\\host\share</c>);</item>
    ///   <item>papka ajratkichi bo'lgan har qanday nom (<c>certificates/x.jpg</c>);</item>
    ///   <item>begona papkadagi fayl: manzil berilgan bo'lsa u <see cref="RequestPath"/> bilan
    ///     boshlanishi SHART, ya'ni <c>/uploads/&lt;guid&gt;.jpg</c> (umumiy papka) o'chirilmaydi;</item>
    ///   <item>bizniki bo'lmagan nom — ya'ni symlink qo'yilgan bo'lsa ham unga BOSHQA nom kerak,
    ///     bizning oqim esa faqat GUID nomli oddiy fayl yozadi.</item>
    /// </list>
    /// </summary>
    /// <param name="urlOrName">To'liq URL, <c>/uploads/marketing-public/...</c> yo'li yoki yalang nom.</param>
    public static string? SafeStoredName(string? urlOrName)
    {
        var s = (urlOrName ?? "").Trim();
        if (s.Length == 0) return null;

        // ⚠️ TESKARI CHIZIQ UMUMAN QABUL QILINMAYDI. Bizning manzillarimizda u hech qachon
        // uchramaydi, Windows'da esa u papka ajratkichi (`C:\...`, `\\host\share`) — ya'ni
        // uni "shunchaki `/` ga aylantirib" qo'yish yo'ldan chiqib ketish yo'lini ochib berardi.
        if (s.Contains('\\')) return null;

        // To'liq URL bo'lsa — faqat yo'l qismi olinadi (host/parametrlar tashlanadi).
        //
        // ⚠️ `"://"` sharti MAJBURIY. `Uri.TryCreate(s, UriKind.Absolute, ...)` UNIX'da
        // `/uploads/...` kabi ODDIY YO'LNI ham qabul qiladi va uni `file:` sxemasi deb biladi —
        // ya'ni shartsiz yozilsa, bizning O'Z manzilimiz (`/uploads/marketing-public/…`)
        // "begona sxema" deb RAD ETILARDI va o'chirish umuman ishlamasdi. Windows'da esa bu
        // hech qachon sezilmasdi (u yerda `/…` absolut URI emas), ya'ni xato faqat serverda
        // chiqadigan turdan bo'lardi.
        if (s.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(s, UriKind.Absolute, out var abs))
        {
            // ⚠️ `file:`/`ftp:` kabi sxemalar qabul qilinmaydi — bizning manzilimiz http(s).
            if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) return null;
            s = abs.AbsolutePath;
        }
        else
        {
            var cut = s.IndexOfAny(['?', '#']);
            if (cut >= 0) s = s[..cut];
        }

        // Yo'l ko'rinishida bo'lsa — AYNAN shu papkaniki bo'lishi shart.
        if (s.Contains('/'))
        {
            if (!s.StartsWith(RequestPath + "/", StringComparison.OrdinalIgnoreCase)) return null;
            s = s[(RequestPath.Length + 1)..];
            // Ichki papka bo'lmasin: papka TEKIS, ichida boshqa katalog yo'q.
            if (s.Contains('/')) return null;
        }

        var ext = ExtensionOf(s);
        if (!ContentTypes.ContainsKey(ext)) return null;

        var stem = s[..^ext.Length];
        if (stem.Length != NameHexLength) return null;
        foreach (var c in stem)
            if (!Uri.IsHexDigit(c)) return null;

        // Kengaytma har doim kichik harfda saqlanadi — nom ham shunday qaytariladi.
        return stem.ToLowerInvariant() + ext;
    }

    /* ═════════════════════════ 5) O'lcham va davomiylik (sof parserlar) ═════════════════════════ */

    /// <summary>
    /// JPEG o'lchamini FAYL SARLAVHASIDAN o'qiydi (piksel). Tanilmasa <c>(0, 0)</c> —
    /// bu <see cref="IgMediaItem"/> uchun «noma'lum» degani va nisbat tekshiruvi
    /// o'tkazib yuboriladi (qarorni Meta chiqaradi).
    /// </summary>
    public static (int Width, int Height) JpegSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return (0, 0);
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; }                                  // to'ldiruvchi
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            var segLen = (data[i + 2] << 8) | data[i + 3];
            if (segLen < 2) break;
            // SOFn — kadr sarlavhasi. DHT (C4), JPG (C8) va DAC (CC) SOF emas.
            var isSof = marker >= 0xC0 && marker <= 0xCF
                        && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof) return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
            i += 2 + segLen;
        }
        return (0, 0);
    }

    /// <summary>Video davomiyligi juda uzun bo'lsa natija ishonchsiz — «noma'lum» deymiz.</summary>
    private const double MaxPlausibleSeconds = 24 * 60 * 60;

    /// <summary>
    /// MP4/MOV davomiyligini <c>mvhd</c> box'idan o'qiydi (soniya). Topilmasa <c>0</c>
    /// («noma'lum» — tekshiruv o'tkazib yuboriladi).
    ///
    /// <para>Box daraxti bo'ylab yurish o'rniga <c>mvhd</c> imzosi QIDIRILADI: <c>moov</c>
    /// ko'p enkoderlarda faylning OXIRIDA turadi, biz esa 300 MB videoni butunlay xotiraga
    /// o'qiy olmaymiz — faqat bosh va oxirgi bo'lakni ko'ramiz. Shuning uchun parser
    /// "qayerdan topsa o'shandan" o'qiydi va natijani ishonchlilikka tekshiradi.</para>
    /// </summary>
    public static double Mp4DurationSeconds(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 4 <= data.Length; i++)
        {
            if (data[i] != (byte)'m' || data[i + 1] != (byte)'v'
                || data[i + 2] != (byte)'h' || data[i + 3] != (byte)'d') continue;

            var p = i + 4;
            if (p >= data.Length) break;
            var version = data[p];
            p += 4;                                   // version (1) + flags (3)

            long timescale, duration;
            if (version == 1)
            {
                if (p + 8 + 8 + 4 + 8 > data.Length) continue;
                p += 16;                              // creation + modification (8+8)
                timescale = Be32(data, p); p += 4;
                duration = Be64(data, p);
            }
            else
            {
                if (p + 4 + 4 + 4 + 4 > data.Length) continue;
                p += 8;                               // creation + modification (4+4)
                timescale = Be32(data, p); p += 4;
                duration = Be32(data, p);
            }

            if (timescale <= 0 || timescale > 1_000_000 || duration <= 0) continue;
            var seconds = duration / (double)timescale;
            if (seconds <= 0 || seconds > MaxPlausibleSeconds) continue;
            return seconds;
        }
        return 0;
    }

    private static long Be32(ReadOnlySpan<byte> d, int o) =>
        ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3];

    private static long Be64(ReadOnlySpan<byte> d, int o)
    {
        long v = 0;
        for (var i = 0; i < 8; i++) v = (v << 8) | d[o + i];
        return v;
    }
}
