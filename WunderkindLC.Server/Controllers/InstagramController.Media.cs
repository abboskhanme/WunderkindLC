using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → INSTAGRAM KONTENT: <b>OCHIQ</b> MEDIA (§5.6, Variant A) — yuklash va o'chirish.
///
/// <para>Bu <see cref="InstagramController"/> ning DAVOMI (<c>partial</c>): marshrut prefiksi
/// (<c>api/admin/instagram</c>), sinf darajasidagi <c>[AdminPerm("marketing",
/// ReadRequiresPerm = true)]</c> va <c>AuditEntity</c> asosiy fayldan MEROS bo'ladi.</para>
///
/// <para><b>🔴 NEGA FAYL OCHIQ PAPKAGA TUSHADI:</b> Instagram postni joylashda media faylni
/// <b>O'ZI yuklab oladi</b> — manzil ochiq HTTPS bo'lishi SHART, autentifikatsiya/IP cheklov/
/// redirect ishlamaydi. Loyihaning <c>/uploads</c> papkasi <c>UploadsGuard</c> ortida (login
/// talab qiladi), shuning uchun har post <c>2207052</c> («Media yuklab bo'lmadi») bilan
/// yiqilardi. Yechim — <b>alohida papka</b> va <b>alohida marshrut</b>:
/// <see cref="MarketingPublicMedia.RequestPath"/>. Qolgan <c>/uploads</c> (jumladan
/// <c>certificates</c> va <c>face</c>) avvalgidek YOPIQ (<c>uploads-security.md</c>).</para>
///
/// <para><b>Kim yuklaydi:</b> faqat <c>marketing.content</c> ruxsati bor xodim. Yuklangan fayl
/// baribir Instagram'da OMMAGA chiqadi, ya'ni bu yerda «maxfiy hujjat ochilib ketishi»
/// xavfi yo'q — xavf faqat <b>bu papkaga BEGONA fayl tushishi</b> yoki <b>bu marshrutdan
/// begona fayl chiqishi</b>. Ikkalasi ham quyidagi qatlamlar bilan yopilgan.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>
    /// Fayl boshidan/oxiridan shuncha bayt xotiraga o'qiladi (o'lcham va davomiylik uchun).
    /// <para>Video 300 MB gacha bo'lishi mumkin — uni butunlay xotiraga olib bo'lmaydi.
    /// JPEG o'lchami sarlavhada, MP4 <c>mvhd</c> esa yo boshida, yo oxirida turadi.</para>
    /// </summary>
    private const int MediaProbeBytes = 256 * 1024;

    /// <summary>Sehrli baytlar uchun yetarli bosh bo'lak (JPEG SOI va ISO BMFF <c>ftyp</c> shu yerda).</summary>
    private const int SniffBytes = 64;

    // =============================================================================================
    //  YUKLASH
    // =============================================================================================

    /// <summary>
    /// Rejalashtirilgan post uchun media yuklash → <c>{ url, kind, sizeBytes, width, height,
    /// durationSeconds }</c>. Javobdagi maydonlar to'g'ridan-to'g'ri <c>IgMediaItem</c> ga
    /// tushadi, ya'ni post yaratilishida <see cref="InstagramPublishContract.ValidateMedia"/>
    /// nisbat/hajm/davomiylikni ALDANMAGAN qiymatlar bilan tekshiradi.
    ///
    /// <para><b>Uchta mustaqil tekshiruv (hammasi o'tishi SHART):</b> kengaytma (JPEG yoki
    /// MP4/MOV) · <c>Content-Type</c> · fayl BOShidagi sehrli baytlar. Faqat kengaytmaga
    /// ishonish ochiq papkaga <c>.jpg</c> nomli HTML qo'yish yo'lini ochib berardi.</para>
    ///
    /// <para><b>Nom:</b> <c>{Guid:N}{kengaytma}</c> — 128 bit tasodifiylik, foydalanuvchining
    /// asl fayl nomi saqlanmaydi (<c>uploads-security.md</c>, <c>UploadGuard.SafeName</c> naqshi).</para>
    ///
    /// <para><b>Manzil ABSOLUT</b> quriladi (<c>Scheme://Host</c> — Cloudflare Tunnel ortida sxema
    /// <c>X-Forwarded-Proto</c> dan tiklanadi, <c>InstagramWebhookController.WebhookUrl</c> bilan
    /// bir xil usul): Meta nisbiy manzilni yuklab ololmaydi. Dev muhitida (http) manzil
    /// HTTPS bo'lmaydi va <see cref="InstagramPublishContract.ValidateMediaUrl"/> uni ATAYIN
    /// rad etadi — lokalda haqiqiy post joylab bo'lmaydi, buni jimgina yashirish kerak emas.</para>
    /// </summary>
    [HttpPost("content/media")]
    [AdminPerm(ContentPerm)]
    [RequestSizeLimit(IgPublishConst.MaxReelsBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IgPublishConst.MaxReelsBytes)]
    public async Task<ActionResult<IgUploadedMediaDto>> UploadContentMedia(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmadi." });

        // Sehrli baytlar — faylni DISKKA yozishdan OLDIN. Ochiq papkaga avval yozib, keyin
        // tekshirib o'chirish "yozildi–o'chirildi" oralig'ida faylni ochiq qoldirardi.
        var head = new byte[(int)Math.Min(file.Length, SniffBytes)];
        await using (var probe = file.OpenReadStream())
            // `ReadAtLeastAsync` — bitta `Read` qisqa qaytishi mumkin, u holda sehrli baytlar
            // "topilmadi" bo'lib, to'g'ri fayl ham rad etilardi.
            await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);

        var (error, kind) = MarketingPublicMedia.Validate(
            file.FileName, file.ContentType, file.Length, head);
        if (error is not null) return BadRequest(new { message = error });

        var dir = PublicMediaDir();
        var stored = MarketingPublicMedia.NewStoredName(file.FileName);
        var fullPath = Path.Combine(dir, stored);

        await using (var fs = System.IO.File.Create(fullPath))
        await using (var source = file.OpenReadStream())
            await source.CopyToAsync(fs, ct);

        var (width, height, duration) = await MeasureAsync(fullPath, kind, ct);

        // ⚠️ AUDITGA FAYL MANZILI YOZILMAYDI (`uploads-security.md` §1 va `audit.md` §1):
        // manzil ochiq, ya'ni tarixni ko'rgan HAR KIM faylni abadiy olib qolardi. `EntityId`
        // ham fayl nomi EMAS — u manzilning maxfiy qismi. Yozilayotgani faqat "nima bo'ldi".
        audit.Record(AuditEntity, MediaAuditId, "create",
            $"Instagram posti uchun media yuklandi ({KindLabel(kind)}{Dimensions(width, height, duration)})");
        await db.SaveChangesAsync(ct);

        return new IgUploadedMediaDto(
            Url: PublicMediaUrl(stored),
            Kind: kind,
            SizeBytes: file.Length,
            Width: width,
            Height: height,
            DurationSeconds: duration);
    }

    // =============================================================================================
    //  O'CHIRISH
    // =============================================================================================

    /// <summary>
    /// Yuklangan media faylni o'chirish (post bekor qilinganda yoki media almashtirilganda).
    ///
    /// <para><b>🔴 FAQAT shu papkadagi fayl.</b> Nom
    /// <see cref="MarketingPublicMedia.SafeStoredName"/> darvozasidan o'tadi — u "nima ruxsat
    /// etilgan" naqshi bilan ishlaydi (32 hex + ruxsat etilgan kengaytma), ya'ni <c>..</c>,
    /// absolut yo'l, papka ajratkichi va begona papka manzili o'z-o'zidan rad etiladi.
    /// Ustiga QO'SHIMCHA qatlam: to'liq yo'l normalizatsiya qilinib (<c>Path.GetFullPath</c>),
    /// papka ichida qolgani QAYTA tekshiriladi — sof funksiyada kutilmagan kamchilik chiqsa ham
    /// fayl tizimiga chiqib ketilmasin.</para>
    ///
    /// <para>Fayl topilmasa ham <b>muvaffaqiyat</b> qaytadi: o'chirish idempotent bo'lsin
    /// (UI post bekor qilinganda ikki marta chaqirishi mumkin) va javob orqali "bu nomli fayl
    /// bor/yo'q" ma'lumoti sizmasin.</para>
    /// </summary>
    /// <param name="url">To'liq manzil yoki <c>/uploads/marketing-public/...</c> yo'li.</param>
    /// <param name="name">Yoki yalang saqlangan nom.</param>
    [HttpDelete("content/media")]
    [AdminPerm(ContentPerm)]
    public async Task<IActionResult> DeleteContentMedia(
        [FromQuery] string? url, [FromQuery] string? name, CancellationToken ct)
    {
        var safe = MarketingPublicMedia.SafeStoredName(string.IsNullOrWhiteSpace(url) ? name : url);
        if (safe is null)
            return BadRequest(new { message = "Manzil noto'g'ri — bu fayl kontent moduliniki emas." });

        var dir = PublicMediaDir();
        var fullPath = Path.GetFullPath(Path.Combine(dir, safe));
        // Ikkinchi qatlam: normalizatsiyadan keyin ham papka ICHIDA qolganini talab qilamiz.
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            return BadRequest(new { message = "Manzil noto'g'ri — bu fayl kontent moduliniki emas." });

        var existed = System.IO.File.Exists(fullPath);
        if (existed) System.IO.File.Delete(fullPath);

        if (existed)
        {
            // Manzil/nom bu yerda ham YOZILMAYDI (yuqoridagi bir xil sabab).
            audit.Record(AuditEntity, MediaAuditId, "delete",
                "Instagram posti uchun yuklangan media o'chirildi");
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { deleted = existed });
    }

    // =============================================================================================
    //  Yordamchilar
    // =============================================================================================

    /// <summary>
    /// Media yozuvlarining yagona <c>EntityId</c> si.
    /// <para>⚠️ ATAYIN o'zgarmas satr: fayl nomi manzilning MAXFIY qismi, uni auditga yozish
    /// faylni tarixni ko'rgan har kimga abadiy ochib berardi.</para>
    /// </summary>
    private const string MediaAuditId = "content-media";

    /// <summary>Ochiq media papkasi (<c>ContentRoot/uploads/marketing-public</c>).
    /// <para>Papka <c>Program.cs</c> da ham yaratiladi (statik marshrut uchun) — bu yerdagi
    /// <c>CreateDirectory</c> faqat ehtiyot chorasi (papka qo'lda o'chirib yuborilgan bo'lsa).</para></summary>
    private string PublicMediaDir()
    {
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var dir = Path.Combine(env.ContentRootPath, "uploads", MarketingPublicMedia.FolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>To'liq (absolut) ochiq manzil — Meta faylni shu manzildan yuklab oladi.</summary>
    private string PublicMediaUrl(string storedName) =>
        $"{Request.Scheme}://{Request.Host}{MarketingPublicMedia.RequestPath}/{storedName}";

    /// <summary>Fayl bosh va oxirgi bo'lagidan o'lcham/davomiylikni o'lchaydi (topilmasa 0).</summary>
    private static async Task<(int Width, int Height, double Duration)> MeasureAsync(
        string path, string kind, CancellationToken ct)
    {
        await using var fs = System.IO.File.OpenRead(path);
        var headLen = (int)Math.Min(fs.Length, MediaProbeBytes);
        var head = new byte[headLen];
        await fs.ReadAtLeastAsync(head.AsMemory(0, headLen), headLen, throwOnEndOfStream: false, ct);

        if (kind == IgPublishConst.KindImage)
        {
            var (w, h) = MarketingPublicMedia.JpegSize(head);
            return (w, h, 0);
        }

        var duration = MarketingPublicMedia.Mp4DurationSeconds(head);
        if (duration == 0 && fs.Length > headLen)
        {
            // `moov` ko'p enkoderlarda faylning OXIRIDA — oxirgi bo'lakni ham ko'ramiz.
            var tailLen = (int)Math.Min(fs.Length - headLen, MediaProbeBytes);
            var tail = new byte[tailLen];
            fs.Seek(-tailLen, SeekOrigin.End);
            await fs.ReadAtLeastAsync(tail.AsMemory(0, tailLen), tailLen, throwOnEndOfStream: false, ct);
            duration = MarketingPublicMedia.Mp4DurationSeconds(tail);
        }
        return (0, 0, duration);
    }

    private static string KindLabel(string kind) =>
        kind == IgPublishConst.KindVideo ? "video" : "rasm";

    /// <summary>Audit matni uchun o'lcham/davomiylik (noma'lum bo'lsa umuman yozilmaydi).</summary>
    private static string Dimensions(int width, int height, double duration)
    {
        if (width > 0 && height > 0) return $", {width}×{height}";
        if (duration > 0) return $", {duration:0.#} s";
        return "";
    }
}

/// <summary>
/// Yuklangan media haqidagi javob — maydonlari <c>IgMediaItem</c> bilan bir xil nomda,
/// ya'ni frontend ularni to'g'ridan-to'g'ri post payload'iga qo'ya oladi.
/// <para><c>0</c> qiymat «noma'lum» degani (fayl sarlavhasidan o'qib bo'lmadi) va
/// tegishli tekshiruv o'tkazib yuboriladi — <c>IgMediaItem</c> dagi bir xil kelishuv.</para>
/// </summary>
public record IgUploadedMediaDto(
    string Url,
    string Kind,
    long SizeBytes,
    int Width,
    int Height,
    double DurationSeconds);
