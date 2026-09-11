using System.Text;
using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// OCHIQ MEDIA MARSHRUTINING QULFI — <c>uploads/marketing-public/</c> (§5.6, Variant A).
///
/// <para><b>Nima qilindi va nega:</b> Instagram postni joylashda media faylni O'ZI yuklab oladi,
/// ya'ni manzil ochiq HTTPS bo'lishi SHART. Loyihaning <c>/uploads</c> papkasi esa
/// <c>UploadsGuard</c> ortida (login talab qiladi) — busiz har post <c>2207052</c> bilan
/// yiqilardi. Shuning uchun BITTA papka ochiq berildi.</para>
///
/// <para><b>🔴 XAVF:</b> ochiq marshrut «bir oz kengayib» qolsa — masalan kimdir
/// <c>FileProvider</c> ni <c>uploadsDir</c> ga o'zgartirsa yoki <c>ServeUnknownFileTypes</c> ni
/// yoqsa — passport skanlari, shartnomalar, sertifikatlar va biometrik selfilar login'siz
/// ochilib ketardi. Bunday drift KOD KO'RIB CHIQISHDA sezilmasligi mumkin (bir qatorlik
/// o'zgarish), shuning uchun u shu yerda MANBA MATNIDAN qulflanadi.</para>
///
/// <para><b>NEGA MANBA MATNI:</b> <c>WunderkindLC.Tests</c> loyihasi <c>WunderkindLC.Server</c>
/// ga referens QILMAYDI (faqat Domain/Application/Infrastructure) — <c>SensitiveReadPermTests</c>
/// va <c>FaceSecurityTests</c> dagi bilan bir xil usul. Sof QOIDALAR esa
/// <see cref="MarketingPublicMedia"/> da (Application) va ular haqiqiy test bilan qoplangan.</para>
/// </summary>
public class MarketingPublicMediaTests
{
    /* =============================================================================================
     *  Yordamchilar
     * ========================================================================================== */

    /// <summary>Repo ildizi — <c>WunderkindLC.slnx</c> yotgan papka.</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ServerSource(params string[] parts)
    {
        var yol = Path.Combine(new[] { RepoRoot, "WunderkindLC.Server" }.Concat(parts).ToArray());
        Assert.True(File.Exists(yol), $"Fayl topilmadi: {yol}");
        return File.ReadAllText(yol);
    }

    private static string ProgramSource() => ServerSource("Program.cs");

    private static string MediaControllerSource() =>
        ServerSource("Controllers", "InstagramController.Media.cs");

    /// <summary><c>app.UseStaticFiles(new StaticFileOptions { … });</c> bloklarini ajratadi.</summary>
    private static List<string> StaticFileBlocks(string src)
    {
        var blocks = new List<string>();
        const string marker = "app.UseStaticFiles(new StaticFileOptions";
        var i = src.IndexOf(marker, StringComparison.Ordinal);
        while (i >= 0)
        {
            var end = src.IndexOf("});", i, StringComparison.Ordinal);
            Assert.True(end > i, "UseStaticFiles bloki tugamagan — Program.cs buzuq");
            blocks.Add(src[i..(end + 3)]);
            i = src.IndexOf(marker, end, StringComparison.Ordinal);
        }
        return blocks;
    }

    /// <summary>Ochiq (darvozasiz) statik bloklar — <c>Guarded(</c> ishlatmaydiganlari.</summary>
    private static List<string> PublicStaticBlocks(string src) =>
        [.. StaticFileBlocks(src).Where(b => !b.Contains("Guarded(", StringComparison.Ordinal))];

    private const string Hex32 = "0123456789abcdef0123456789abcdef";

    private static byte[] Jpeg(int width, int height)
    {
        var d = new byte[20];
        d[0] = 0xFF; d[1] = 0xD8;                       // SOI
        d[2] = 0xFF; d[3] = 0xC0;                       // SOF0
        d[4] = 0x00; d[5] = 0x11;                       // segment uzunligi (17)
        d[6] = 0x08;                                    // aniqlik
        d[7] = (byte)(height >> 8); d[8] = (byte)height;
        d[9] = (byte)(width >> 8); d[10] = (byte)width;
        return d;
    }

    private static byte[] Mp4(int timescale, int duration)
    {
        var d = new List<byte> { 0x00, 0x00, 0x00, 0x18 };
        d.AddRange("ftypisom"u8.ToArray());
        d.AddRange(new byte[8]);
        d.AddRange("mvhd"u8.ToArray());
        d.AddRange(new byte[4]);                        // version 0 + flags
        d.AddRange(new byte[8]);                        // creation + modification
        d.AddRange([(byte)(timescale >> 24), (byte)(timescale >> 16), (byte)(timescale >> 8), (byte)timescale]);
        d.AddRange([(byte)(duration >> 24), (byte)(duration >> 16), (byte)(duration >> 8), (byte)duration]);
        return [.. d];
    }

    /* =============================================================================================
     *  1) OCHIQ MARSHRUT — FAQAT `marketing-public`
     * ========================================================================================== */

    /// <summary>
    /// Ochiq marshrut prefiksining O'ZI qulflanadi: u <c>/uploads</c> ostidagi ANIQ bitta
    /// papka bo'lishi kerak, ildiz (<c>/uploads</c>) emas.
    /// </summary>
    [Fact]
    public void Ochiq_marshrut_faqat_bitta_papka()
    {
        Assert.Equal("marketing-public", MarketingPublicMedia.FolderName);
        Assert.Equal("/uploads/marketing-public", MarketingPublicMedia.RequestPath);
        Assert.NotEqual("/uploads", MarketingPublicMedia.RequestPath);
    }

    /// <summary>
    /// <c>Program.cs</c> da darvozasiz (Guarded'siz) statik blok AYNAN BITTA bo'lishi va u
    /// FAQAT <c>marketing-public</c> papkasini berishi kerak.
    ///
    /// <para>⚠️ Ikkinchi ochiq blok paydo bo'lishi — bu testning ASOSIY maqsadi: kimdir
    /// "yana bitta ochiq papka kerak edi" deb qo'shsa, qaror ko'rib chiqilsin.</para>
    /// </summary>
    [Fact]
    public void Programda_ochiq_statik_blok_faqat_bitta_va_u_marketing_public()
    {
        var src = ProgramSource();
        var publicBlocks = PublicStaticBlocks(src);

        Assert.True(publicBlocks.Count == 1,
            $"Program.cs da darvozasiz UseStaticFiles bloklari soni {publicBlocks.Count} — "
            + "kutilgani 1 (faqat uploads/marketing-public). Yangi ochiq marshrut qo'shilgan bo'lsa "
            + "uploads-security.md dagi qoidani qayta ko'rib chiqing.");

        var block = publicBlocks[0];
        Assert.Contains("MarketingPublicMedia.RequestPath", block);
        Assert.Contains("PhysicalFileProvider(marketingPublicDir)", block);
    }

    /// <summary>
    /// Ochiq blok <c>uploads</c> ildizini, sertifikat/selfi papkalarini yoki <c>wwwroot</c> ni
    /// BERMASLIGI kerak.
    /// </summary>
    [Fact]
    public void Ochiq_blok_boshqa_papkani_bermaydi()
    {
        var block = PublicStaticBlocks(ProgramSource())[0];

        Assert.DoesNotContain("PhysicalFileProvider(uploadsDir)", block);
        Assert.DoesNotContain("WebRootFileProvider", block);
        Assert.DoesNotContain("certificates", block);
        Assert.DoesNotContain("faceFolders", block);
        Assert.DoesNotContain("FaceStorage", block);
        // Bo'sh `RequestPath` butun `wwwroot`ni ildizga chiqarardi.
        Assert.DoesNotContain("RequestPath = \"\"", block);
        Assert.DoesNotContain("RequestPath = \"/uploads\"", block);
    }

    /// <summary>
    /// Ochiq papkada NOMA'LUM tur berilmasligi kerak: bu papka BIZNING domenimizda, ya'ni
    /// u yerdan <c>text/html</c> yoki <c>image/svg+xml</c> chiqsa — saqlangan XSS.
    /// </summary>
    [Fact]
    public void Ochiq_blok_notanish_turlarni_bermaydi()
    {
        var block = PublicStaticBlocks(ProgramSource())[0];

        Assert.Contains("ServeUnknownFileTypes = false", block);
        Assert.Contains("ContentTypeProvider", block);
        Assert.Contains("nosniff", block);
    }

    /// <summary>MIME xaritasi YOPIQ: faqat JPEG/MP4/MOV, hech qanday matn/skript turi yo'q.</summary>
    [Fact]
    public void Mime_xaritasi_yopiq()
    {
        var types = MarketingPublicMedia.ContentTypes;

        Assert.Equal(4, types.Count);
        Assert.Equal("image/jpeg", types[".jpg"]);
        Assert.Equal("image/jpeg", types[".JPEG"]);   // katta harf ham (OrdinalIgnoreCase)
        Assert.Equal("video/mp4", types[".mp4"]);
        Assert.Equal("video/quicktime", types[".mov"]);

        foreach (var (ext, mime) in types)
        {
            Assert.DoesNotContain("html", mime);
            Assert.DoesNotContain("svg", mime);
            Assert.DoesNotContain("javascript", mime);
            Assert.True(ext.StartsWith('.'), $"Kengaytma nuqta bilan boshlanishi kerak: {ext}");
        }
    }

    /// <summary>
    /// Ochiq marshrut <c>UploadsGuard</c> dan OLDIN turishi kerak (aks holda darvoza uni
    /// 404 qilardi) — LEKIN darvozaning o'zi joyida qolishi ham shart.
    /// </summary>
    [Fact]
    public void Ochiq_marshrut_darvozadan_oldin_va_darvoza_joyida()
    {
        var src = ProgramSource();

        var publicAt = src.IndexOf("MarketingPublicMedia.RequestPath", StringComparison.Ordinal);
        var guardAt = src.IndexOf("uploadsGuard.IsAllowedAsync", StringComparison.Ordinal);

        Assert.True(publicAt > 0, "Program.cs da ochiq marshrut yo'q");
        Assert.True(guardAt > 0, "Program.cs da /uploads darvozasi (UploadsGuard) YO'Q — yopiq fayllar ochilib ketgan");
        Assert.True(publicAt < guardAt,
            "Ochiq marshrut UploadsGuard dan KEYIN turibdi — darvoza uni 404 qiladi va Meta media'ni yuklab ololmaydi");
    }

    /* =============================================================================================
     *  2) ESKI QULFLAR BUZILMAGANMI — certificates / face
     * ========================================================================================== */

    /// <summary>
    /// <c>uploads/certificates</c> va <c>uploads/face</c> hamon <c>PrivateFolderFileProvider</c>
    /// bilan yopiq va ochiq marshrutga TUSHMAGAN.
    /// </summary>
    [Fact]
    public void Sertifikat_va_selfi_papkalari_hamon_yopiq()
    {
        var src = ProgramSource();

        Assert.Contains("PrivateFolderFileProvider", src);
        Assert.Contains("certificateFolders", src);
        Assert.Contains("faceFolders", src);

        // Darvozasiz blok ularni ko'rsatmasligi yuqorida tekshirilgan; bu yerda —
        // ular hamon DARVOZALANGAN bloklardan berilishini qulflaymiz.
        var guardedBlocks = StaticFileBlocks(src)
            .Where(b => b.Contains("Guarded(", StringComparison.Ordinal)).ToList();
        Assert.True(guardedBlocks.Count >= 2,
            "Darvozalangan statik bloklar soni kamaygan — wwwroot yoki /uploads ochilib qolgan bo'lishi mumkin");
    }

    /// <summary>
    /// Ochiq papka <c>uploads/</c> ICHIDA — ya'ni tungi zaxira arxiviga o'z-o'zidan kiradi
    /// (<c>uploads/face</c> dan farqli: u ATAYIN <c>--exclude</c> qilingan).
    /// </summary>
    [Fact]
    public void Ochiq_papka_uploads_ichida_va_zaxiradan_chiqarilmagan()
    {
        Assert.StartsWith("/uploads/", MarketingPublicMedia.RequestPath);
        Assert.Matches(
            new Regex(@"marketingPublicDir\s*=\s*Path\.Combine\(\s*uploadsDir", RegexOptions.Singleline),
            ProgramSource());

        var compose = File.ReadAllText(Path.Combine(RepoRoot, "docker-compose.yml"));
        Assert.DoesNotContain(MarketingPublicMedia.FolderName, compose);
    }

    /* =============================================================================================
     *  3) ENDPOINT DARVOZALARI
     * ========================================================================================== */

    /// <summary>Yuklash va o'chirish — <c>marketing.content</c> ruxsati bilan darvozalangan.</summary>
    [Fact]
    public void Media_endpointlari_ruxsat_talab_qiladi()
    {
        var src = MediaControllerSource();

        Assert.Matches(new Regex(@"\[HttpPost\(""content/media""\)\]\s*\r?\n\s*\[AdminPerm\("), src);
        Assert.Matches(new Regex(@"\[HttpDelete\(""content/media""\)\]\s*\r?\n\s*\[AdminPerm\("), src);
    }

    /// <summary>
    /// <c>[AllowAnonymous]</c> sinf darajasidagi <c>[Authorize]</c> va <c>[AdminPerm]</c> ni
    /// ham bekor qiladi — <c>/api/admin/...</c> yo'lida u hech qachon turmasligi kerak
    /// (<c>uploads-security.md</c>).
    /// </summary>
    [Fact]
    public void Media_controllerida_AllowAnonymous_yoq()
    {
        Assert.DoesNotContain("AllowAnonymous", MediaControllerSource());
    }

    /// <summary>
    /// O'chirishda yo'ldan chiqib ketishga qarshi darvoza CHAQIRILGANMI (sof funksiya) va
    /// ustiga normalizatsiya tekshiruvi bormi.
    /// </summary>
    [Fact]
    public void Ochirishda_yoldan_chiqish_himoyasi_bor()
    {
        var src = MediaControllerSource();

        Assert.Contains("MarketingPublicMedia.SafeStoredName", src);
        Assert.Contains("Path.GetFullPath", src);
        Assert.Contains("StartsWith(root", src);
    }

    /// <summary>
    /// ⚠️ AUDITGA FAYL MANZILI YOZILMAYDI (<c>uploads-security.md</c> §1): manzil ochiq, ya'ni
    /// tarixni ko'rgan har kim faylni abadiy olib qolardi.
    /// </summary>
    [Fact]
    public void Auditga_fayl_manzili_yozilmaydi()
    {
        var src = MediaControllerSource();

        foreach (Match m in Regex.Matches(src, @"audit\.Record\((?:[^;])*?\);", RegexOptions.Singleline))
        {
            var call = m.Value;
            Assert.DoesNotContain("stored", call);
            Assert.DoesNotContain("PublicMediaUrl", call);
            Assert.DoesNotContain("fullPath", call);
            Assert.DoesNotContain("safe", call);
            Assert.DoesNotContain("/uploads", call);
        }
    }

    /* =============================================================================================
     *  4) SOF FUNKSIYALAR — nom xavfsizligi (yo'ldan chiqib ketish)
     * ========================================================================================== */

    [Theory]
    // yalang nom
    [InlineData(Hex32 + ".jpg")]
    [InlineData(Hex32 + ".jpeg")]
    [InlineData(Hex32 + ".mp4")]
    [InlineData(Hex32 + ".mov")]
    // to'liq yo'l va so'rov qatori bilan
    [InlineData("/uploads/marketing-public/" + Hex32 + ".jpg")]
    [InlineData("/uploads/marketing-public/" + Hex32 + ".jpg?v=2")]
    [InlineData("https://crm.example.com/uploads/marketing-public/" + Hex32 + ".jpg")]
    public void Xavfsiz_nomlar_qabul_qilinadi(string input)
    {
        var name = MarketingPublicMedia.SafeStoredName(input);
        Assert.NotNull(name);
        Assert.StartsWith(Hex32, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../../etc/passwd")]
    [InlineData("/uploads/marketing-public/../" + Hex32 + ".jpg")]           // papkadan chiqish
    [InlineData("/uploads/marketing-public/sub/" + Hex32 + ".jpg")]          // ichki papka
    [InlineData("/uploads/" + Hex32 + ".jpg")]                               // BEGONA papka (umumiy uploads)
    [InlineData("/uploads/certificates/" + Hex32 + ".jpg")]                  // sertifikatlar
    [InlineData("/uploads/face/" + Hex32 + ".jpg")]                          // biometrik selfi
    [InlineData("\\uploads\\marketing-public\\" + Hex32 + ".jpg")]           // teskari chiziq
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://boshqa.example.com/x/" + Hex32 + ".jpg")]           // begona yo'l
    [InlineData(Hex32 + ".png")]                                             // ruxsat etilmagan kengaytma
    [InlineData(Hex32 + ".html")]
    [InlineData(Hex32 + ".svg")]
    [InlineData(Hex32 + ".jpg.html")]                                        // qo'sh kengaytma
    [InlineData("0123456789abcdef.jpg")]                                     // qisqa (32 emas)
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.jpg")]                     // hex emas
    [InlineData(Hex32)]                                                      // kengaytmasiz
    public void Xavfli_nomlar_rad_etiladi(string? input)
    {
        Assert.Null(MarketingPublicMedia.SafeStoredName(input));
    }

    /// <summary>Katta harfli hex ham qabul qilinadi, lekin nom KICHIK harfda qaytadi
    /// (diskda fayl doim kichik harfda yozilgan — katta-kichik farqlaydigan FS'da topilsin).</summary>
    [Fact]
    public void Nom_kichik_harfga_keltiriladi()
    {
        var name = MarketingPublicMedia.SafeStoredName("0123456789ABCDEF0123456789ABCDEF.JPG");
        Assert.Equal("0123456789abcdef0123456789abcdef.jpg", name);
    }

    /// <summary>Yaratilgan nom O'Z darvozasidan o'tishi kerak (yaratish ↔ o'chirish mos).</summary>
    [Fact]
    public void Yaratilgan_nom_ozining_darvozasidan_otadi()
    {
        var name = MarketingPublicMedia.NewStoredName("reklama-oktabr.JPG");
        Assert.EndsWith(".jpg", name);
        Assert.DoesNotContain("reklama", name);           // asl nom SAQLANMAYDI
        Assert.Equal(36, name.Length);                    // 32 hex + ".jpg"
        Assert.Equal(name, MarketingPublicMedia.SafeStoredName(name));
        Assert.Equal(name, MarketingPublicMedia.SafeStoredName(
            MarketingPublicMedia.RequestPath + "/" + name));
    }

    /// <summary>Ikki yuklash bir xil nom bermasligi kerak (128 bit tasodifiylik).</summary>
    [Fact]
    public void Nomlar_takrorlanmaydi()
    {
        var names = Enumerable.Range(0, 200)
            .Select(_ => MarketingPublicMedia.NewStoredName("a.jpg")).ToHashSet();
        Assert.Equal(200, names.Count);
    }

    [Fact]
    public void Ruxsat_etilmagan_kengaytma_bilan_nom_yasalmaydi()
    {
        Assert.Throws<ArgumentException>(() => MarketingPublicMedia.NewStoredName("virus.html"));
        Assert.Throws<ArgumentException>(() => MarketingPublicMedia.NewStoredName("logo.png"));
        Assert.Throws<ArgumentException>(() => MarketingPublicMedia.NewStoredName("kengaytmasiz"));
    }

    /* =============================================================================================
     *  5) SOF FUNKSIYALAR — yuklash darvozasi (kengaytma · MIME · mazmun)
     * ========================================================================================== */

    [Theory]
    [InlineData("a.jpg", "image")]
    [InlineData("a.JPEG", "image")]
    [InlineData("a.mp4", "video")]
    [InlineData("a.MOV", "video")]
    public void Kengaytma_turni_aniqlaydi(string name, string kind) =>
        Assert.Equal(kind, MarketingPublicMedia.KindOfExtension(name));

    [Theory]
    [InlineData("a.png")]        // Instagram 2207005 bilan rad etadi
    [InlineData("a.webp")]
    [InlineData("a.heic")]
    [InlineData("a.gif")]
    [InlineData("a.svg")]
    [InlineData("a.html")]
    [InlineData("a.pdf")]
    [InlineData("a.webm")]
    [InlineData("kengaytmasiz")]
    [InlineData("")]
    [InlineData(null)]
    public void Notogri_kengaytma_rad_etiladi(string? name) =>
        Assert.Null(MarketingPublicMedia.KindOfExtension(name));

    [Fact]
    public void Togri_jpeg_qabul_qilinadi()
    {
        var (error, kind) = MarketingPublicMedia.Validate(
            "post.jpg", "image/jpeg", 200_000, Jpeg(1080, 1350));
        Assert.Null(error);
        Assert.Equal(IgPublishConst.KindImage, kind);
    }

    [Fact]
    public void Togri_mp4_qabul_qilinadi()
    {
        var (error, kind) = MarketingPublicMedia.Validate(
            "reels.mp4", "video/mp4", 5_000_000, Mp4(1000, 5000));
        Assert.Null(error);
        Assert.Equal(IgPublishConst.KindVideo, kind);
    }

    /// <summary>
    /// 🔴 ENG MUHIM YUKLASH TEKSHIRUVI: <c>.jpg</c> nomi va <c>image/jpeg</c> sarlavhasi bilan
    /// yuborilgan HTML fayl RAD etilishi kerak — aks holda ochiq papkadan bizning domenimizda
    /// HTML berilib, saqlangan XSS bo'lardi.
    /// </summary>
    [Fact]
    public void Jpg_niqobidagi_html_rad_etiladi()
    {
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var (error, kind) = MarketingPublicMedia.Validate("post.jpg", "image/jpeg", html.Length, html);
        Assert.NotNull(error);
        Assert.Equal("", kind);
    }

    /// <summary>JPEG mazmuni <c>.mp4</c> nomi bilan yuborilsa ham rad etiladi (tur mos kelmadi).</summary>
    [Fact]
    public void Mazmun_kengaytmaga_mos_kelmasa_rad_etiladi()
    {
        var (error, _) = MarketingPublicMedia.Validate(
            "post.mp4", "video/mp4", 1000, Jpeg(100, 100));
        Assert.NotNull(error);
    }

    [Fact]
    public void Content_type_mos_kelmasa_rad_etiladi()
    {
        var (error, _) = MarketingPublicMedia.Validate(
            "post.jpg", "text/html", 1000, Jpeg(100, 100));
        Assert.NotNull(error);
    }

    /// <summary><c>Content-Type</c> dagi parametr ("; charset=…") xalaqit bermasligi kerak.</summary>
    [Fact]
    public void Content_type_parametri_xalaqit_bermaydi()
    {
        Assert.True(MarketingPublicMedia.ContentTypeMatches(
            IgPublishConst.KindImage, "image/jpeg; charset=binary"));
        Assert.False(MarketingPublicMedia.ContentTypeMatches(IgPublishConst.KindImage, "image/png"));
        Assert.False(MarketingPublicMedia.ContentTypeMatches(IgPublishConst.KindImage, ""));
        Assert.False(MarketingPublicMedia.ContentTypeMatches(IgPublishConst.KindVideo, "image/jpeg"));
    }

    /// <summary>Hajm chegaralari Meta'niki bilan BITTA joydan olinadi.</summary>
    [Fact]
    public void Hajm_chegaralari_kontraktdan_olinadi()
    {
        Assert.Equal(IgPublishConst.MaxImageBytes, MarketingPublicMedia.MaxBytesFor(IgPublishConst.KindImage));
        Assert.Equal(IgPublishConst.MaxReelsBytes, MarketingPublicMedia.MaxBytesFor(IgPublishConst.KindVideo));

        var (error, _) = MarketingPublicMedia.Validate(
            "post.jpg", "image/jpeg", IgPublishConst.MaxImageBytes + 1, Jpeg(100, 100));
        Assert.NotNull(error);
        Assert.Contains("katta", error!);
    }

    [Fact]
    public void Bosh_fayl_rad_etiladi()
    {
        var (error, _) = MarketingPublicMedia.Validate("post.jpg", "image/jpeg", 0, []);
        Assert.NotNull(error);
    }

    /* =============================================================================================
     *  6) SOF FUNKSIYALAR — o'lcham va davomiylik
     * ========================================================================================== */

    [Fact]
    public void Jpeg_olchami_sarlavhadan_oqiladi()
    {
        var (w, h) = MarketingPublicMedia.JpegSize(Jpeg(1080, 1350));
        Assert.Equal(1080, w);
        Assert.Equal(1350, h);
    }

    /// <summary>Tanilmasa <c>(0,0)</c> — «noma'lum» degani va nisbat tekshiruvi o'tkazib
    /// yuboriladi (<c>IgMediaItem</c> dagi kelishuv). Xato QAYTARILMAYDI.</summary>
    [Fact]
    public void Notanish_rasm_nol_qaytaradi()
    {
        Assert.Equal((0, 0), MarketingPublicMedia.JpegSize([1, 2, 3, 4, 5]));
        Assert.Equal((0, 0), MarketingPublicMedia.JpegSize([]));
    }

    [Fact]
    public void Mp4_davomiyligi_mvhd_dan_oqiladi()
    {
        Assert.Equal(5.0, MarketingPublicMedia.Mp4DurationSeconds(Mp4(1000, 5000)), 3);
        Assert.Equal(30.0, MarketingPublicMedia.Mp4DurationSeconds(Mp4(600, 18000)), 3);
    }

    /// <summary>Buzuq/ishonchsiz qiymatlar «noma'lum» (0) bo'ladi — soxta davomiylik bilan
    /// post rad etilib qolmasin.</summary>
    [Fact]
    public void Buzuq_mp4_nol_qaytaradi()
    {
        Assert.Equal(0d, MarketingPublicMedia.Mp4DurationSeconds(Mp4(0, 5000)));          // timescale 0
        Assert.Equal(0d, MarketingPublicMedia.Mp4DurationSeconds(Mp4(1000, 0)));          // duration 0
        Assert.Equal(0d, MarketingPublicMedia.Mp4DurationSeconds(Mp4(1, 999_999)));       // ~11 kun — ishonchsiz
        Assert.Equal(0d, MarketingPublicMedia.Mp4DurationSeconds([1, 2, 3]));
        Assert.Equal(0d, MarketingPublicMedia.Mp4DurationSeconds(Jpeg(100, 100)));
    }

    /* =============================================================================================
     *  7) MANZIL — Meta uchun yaroqli bo'lishi
     * ========================================================================================== */

    /// <summary>
    /// Yuklash endpointi ABSOLUT manzil qurishi shart: Meta faylni O'ZI yuklab oladi va nisbiy
    /// manzilni ocha olmaydi. HTTPS bo'lsa <see cref="InstagramPublishContract.ValidateMediaUrl"/>
    /// dan o'tadi.
    /// </summary>
    [Fact]
    public void Qurilgan_manzil_kontrakt_darvozasidan_otadi()
    {
        var url = $"https://crm.example.com{MarketingPublicMedia.RequestPath}/{Hex32}.jpg";

        var (ok, error) = InstagramPublishContract.ValidateMediaUrl(url);
        Assert.True(ok, error);
        Assert.True(InstagramPublishContract.IsJpegUrl(url));

        // Manzil qurish usuli manba matnida ham qulflanadi (nisbiy manzilga qaytib ketmasin).
        Assert.Contains("Request.Scheme}://{Request.Host}", MediaControllerSource());
    }
}
