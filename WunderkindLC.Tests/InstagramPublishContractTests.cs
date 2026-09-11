using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// KONTENT REJALASHTIRISHNING SOF QOIDALARI (<see cref="InstagramPublishContract"/>) testlari.
/// Rasmiy manba: <c>KENGAYTIRISH-PROMPT.md</c> §5.2–§5.8, <c>META-API-MALUMOTNOMA.md</c> §13.
///
/// <para>Bu funksiyalarda tarmoq ham, baza ham yo'q — ular ATAYIN ajratilgan, chunki modulning
/// eng qimmat qarorlari shu yerda: <b>postni Meta'ga umuman yubormaslik</b> (validatsiya),
/// <b>qachon qayta so'rash</b> (poll) va <b>xatoni odam o'qiydigan matnga aylantirish</b>.
/// Chop etilgan IG postni API orqali o'chirib bo'lmaydi — ya'ni bu yerdagi har bir "yo'q"
/// tuzatib bo'lmaydigan xatoning oldini oladi.</para>
/// </summary>
public class InstagramPublishContractTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0);
    private static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");

    private static IgMediaItem Jpeg(int w = 1080, int h = 1080, long size = 1_000_000, string caption = "") =>
        new("https://cdn.test/uploads/a.jpg", IgPublishConst.KindImage,
            SizeBytes: size, Width: w, Height: h, Caption: caption);

    private static IgMediaItem Mp4(int w = 1080, int h = 1920, double dur = 30, long size = 10_000_000) =>
        new("https://cdn.test/uploads/a.mp4", IgPublishConst.KindVideo,
            SizeBytes: size, DurationSeconds: dur, Width: w, Height: h);

    // ===================== 1) Normalizatsiya =====================

    [Theory]
    [InlineData("image", "image")]
    [InlineData("REELS", "reels")]
    [InlineData("  story  ", "story")]
    [InlineData("carousel", "carousel")]
    [InlineData("igtv", "image")]     // noma'lum tur — yozuv yo'qolmaydi, "image"ga tushadi
    [InlineData("", "image")]
    [InlineData(null, "image")]
    public void NormalizePostType_nomalum_turni_image_ga_otkazadi(string? given, string expected)
    {
        Assert.Equal(expected, InstagramPublishContract.NormalizePostType(given));
    }

    [Theory]
    [InlineData("published", "published")]
    [InlineData("CANCELLED", "cancelled")]
    [InlineData("nimadir", "scheduled")]
    [InlineData(null, "scheduled")]
    public void NormalizeStatus_nomalum_holatni_scheduled_ga_otkazadi(string? given, string expected)
    {
        Assert.Equal(expected, InstagramPublishContract.NormalizeStatus(given));
    }

    [Theory]
    [InlineData("FINISHED", "FINISHED")]
    [InlineData("finished", "FINISHED")]
    [InlineData("ERROR", "ERROR")]
    [InlineData("EXPIRED", "EXPIRED")]
    [InlineData("PUBLISHED", "PUBLISHED")]
    [InlineData("IN_PROGRESS", "IN_PROGRESS")]
    [InlineData("SOMETHING_NEW", "IN_PROGRESS")]   // noma'lum → ERROR emas, IN_PROGRESS
    [InlineData("", "IN_PROGRESS")]
    [InlineData(null, "IN_PROGRESS")]
    public void NormalizeContainerStatus_nomalum_kodni_ERROR_qilib_yubormaydi(string? given, string expected)
    {
        // ⚠️ Yangi/kutilmagan qiymat tufayli tayyor bo'layotgan post o'chirilib ketmasin —
        // poll baribir 10 daqiqada to'xtaydi.
        Assert.Equal(expected, InstagramPublishContract.NormalizeContainerStatus(given));
    }

    [Fact]
    public void IsReadyToPublish_va_IsTerminal_holatlarni_togri_ajratadi()
    {
        Assert.True(InstagramPublishContract.IsReadyToPublish("FINISHED"));
        Assert.False(InstagramPublishContract.IsReadyToPublish("IN_PROGRESS"));
        Assert.False(InstagramPublishContract.IsReadyToPublish("ERROR"));

        Assert.True(InstagramPublishContract.IsTerminal("ERROR"));
        Assert.True(InstagramPublishContract.IsTerminal("EXPIRED"));
        Assert.True(InstagramPublishContract.IsTerminal("PUBLISHED"));
        Assert.False(InstagramPublishContract.IsTerminal("IN_PROGRESS"));
    }

    [Theory]
    [InlineData("image", "")]              // IMAGE — standart, parametr yuborilmaydi
    [InlineData("reels", "REELS")]
    [InlineData("video", "REELS")]         // feed videosi ham REELS bo'lib joylanadi
    [InlineData("story", "STORIES")]
    [InlineData("carousel", "CAROUSEL")]
    public void MediaTypeOf_post_turini_Meta_qiymatiga_aylantiradi(string type, string expected)
    {
        Assert.Equal(expected, InstagramPublishContract.MediaTypeOf(type));
    }

    // ===================== 2) Caption =====================

    [Theory]
    [InlineData("#dars #ingliz #kurs", 3)]
    [InlineData("salom#tag", 0)]          // so'z ichidagi `#` hashtag emas
    [InlineData("## tag", 0)]             // `#` dan keyin harf yo'q
    [InlineData("##tag", 1)]              // ikkinchi `#` haqiqiy hashtag boshlaydi
    [InlineData("#1kurs", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void CountHashtags_faqat_haqiqiy_hashtaglarni_sanaydi(string? text, int expected)
    {
        Assert.Equal(expected, InstagramPublishContract.CountHashtags(text));
    }

    [Theory]
    [InlineData("@wunderkind @markaz", 2)]
    [InlineData("ali@mail.uz", 0)]        // e-mail mention EMAS
    [InlineData("yozing: @admin", 1)]
    [InlineData("@", 0)]
    [InlineData(null, 0)]
    public void CountMentions_email_ni_mention_deb_sanamaydi(string? text, int expected)
    {
        Assert.Equal(expected, InstagramPublishContract.CountMentions(text));
    }

    [Fact]
    public void ValidateCaption_2200_belgi_otadi_2201_otmaydi()
    {
        Assert.True(InstagramPublishContract.ValidateCaption(new string('a', 2200)).Ok);

        var (ok, err) = InstagramPublishContract.ValidateCaption(new string('a', 2201));
        Assert.False(ok);
        Assert.Contains("2201", err);
    }

    [Fact]
    public void ValidateCaption_30_hashtag_otadi_31_otmaydi()
    {
        var ok30 = string.Join(" ", Enumerable.Range(0, 30).Select(i => "#t" + i));
        Assert.True(InstagramPublishContract.ValidateCaption(ok30).Ok);

        var bad31 = string.Join(" ", Enumerable.Range(0, 31).Select(i => "#t" + i));
        var (ok, err) = InstagramPublishContract.ValidateCaption(bad31);
        Assert.False(ok);
        Assert.Contains("Hashtag", err);
    }

    [Fact]
    public void ValidateCaption_20_mention_otadi_21_otmaydi()
    {
        var ok20 = string.Join(" ", Enumerable.Range(0, 20).Select(i => "@u" + i));
        Assert.True(InstagramPublishContract.ValidateCaption(ok20).Ok);

        var bad21 = string.Join(" ", Enumerable.Range(0, 21).Select(i => "@u" + i));
        Assert.False(InstagramPublishContract.ValidateCaption(bad21).Ok);
    }

    [Fact]
    public void ValidateCaption_bosh_matn_xato_emas()
    {
        Assert.True(InstagramPublishContract.ValidateCaption("").Ok);
        Assert.True(InstagramPublishContract.ValidateCaption(null).Ok);
    }

    // ===================== 3) Media URL =====================

    [Theory]
    [InlineData("https://cdn.test/a.jpg", true)]
    [InlineData("http://cdn.test/a.jpg", false)]    // Meta faylni o'zi yuklab oladi — HTTPS shart
    [InlineData("/uploads/a.jpg", false)]           // nisbiy manzil ishlamaydi
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateMediaUrl_faqat_ochiq_HTTPS_ni_qabul_qiladi(string? url, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMediaUrl(url).Ok);
    }

    [Theory]
    [InlineData("https://cdn.test/a.jpg", true)]
    [InlineData("https://cdn.test/a.JPEG", true)]
    [InlineData("https://cdn.test/a.jpg?v=12", true)]   // query kengaytmani buzmaydi
    [InlineData("https://cdn.test/a.png", false)]
    [InlineData("https://cdn.test/a.webp", false)]
    public void IsJpegUrl_faqat_jpg_jpeg(string url, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.IsJpegUrl(url));
    }

    // ===================== 4) Rasm validatsiyasi =====================

    [Fact]
    public void ValidateMedia_feed_rasmi_JPEG_bolishi_shart()
    {
        var png = new IgMediaItem("https://cdn.test/a.png", IgPublishConst.KindImage, Width: 1080, Height: 1080);
        var (ok, err) = InstagramPublishContract.ValidateMedia("image", png);
        Assert.False(ok);
        Assert.Contains("JPEG", err);
    }

    [Fact]
    public void ValidateMedia_rasm_hajmi_8MB_dan_oshmaydi()
    {
        Assert.True(InstagramPublishContract.ValidateMedia("image", Jpeg(size: 8 * 1024 * 1024)).Ok);
        Assert.False(InstagramPublishContract.ValidateMedia("image", Jpeg(size: 9 * 1024 * 1024)).Ok);
    }

    [Theory]
    [InlineData(1080, 1080, true)]   // 1:1
    [InlineData(1080, 1350, true)]   // 4:5 — quyi chegara
    [InlineData(1440, 754, true)]    // ~1.91:1 — yuqori chegara
    [InlineData(1080, 1920, false)]  // 9:16 — feed uchun juda tik
    [InlineData(1440, 600, false)]   // 2.4:1 — juda keng
    public void ValidateMedia_feed_nisbati_4_5_dan_1_91_gacha(int w, int h, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("image", Jpeg(w, h)).Ok);
    }

    [Theory]
    [InlineData(320, 320, true)]
    [InlineData(1440, 1440, true)]
    [InlineData(300, 300, false)]    // juda kichik
    [InlineData(2000, 2000, false)]  // juda katta
    public void ValidateMedia_feed_rasmi_kengligi_320_1440(int w, int h, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("image", Jpeg(w, h)).Ok);
    }

    [Fact]
    public void ValidateMedia_olcham_nomalum_bolsa_nisbat_tekshirilmaydi()
    {
        // ⚠️ 0 = "o'lchanmagan". "Bilmasak — rad etamiz" qoidasi ishlaydigan postlarni to'sardi;
        // qarorni Meta chiqaradi va kod (2207009) o'zbekcha matnga aylanadi.
        var unknown = new IgMediaItem("https://cdn.test/a.jpg", IgPublishConst.KindImage);
        Assert.True(InstagramPublishContract.ValidateMedia("image", unknown).Ok);
    }

    [Theory]
    [InlineData(1080, 1920, true)]
    [InlineData(720, 1280, true)]
    [InlineData(1080, 1080, false)]   // story kvadrat bo'lmaydi
    public void ValidateMedia_story_rasmi_9_16(int w, int h, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("story", Jpeg(w, h)).Ok);
    }

    // ===================== 5) Video validatsiyasi =====================

    [Fact]
    public void ValidateMedia_reels_uchun_rasm_qabul_qilinmaydi()
    {
        var (ok, err) = InstagramPublishContract.ValidateMedia("reels", Jpeg(1080, 1920));
        Assert.False(ok);
        Assert.Contains("video", err, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(60, true)]
    [InlineData(900, true)]
    [InlineData(2, false)]     // 3 soniyadan qisqa
    [InlineData(901, false)]   // 15 daqiqadan uzun
    public void ValidateMedia_reels_davomiyligi_3_900_soniya(double dur, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("reels", Mp4(dur: dur)).Ok);
    }

    [Fact]
    public void ValidateMedia_reels_hajmi_1GB_gacha_qabul_qilinadi()
    {
        // ⚠️ Ilgari chegara 300 MB edi — bu BIZNING cheklovimiz, Meta'niki emas: telefonda
        // olingan bir daqiqalik 4K video ham undan oshadi va post umuman yuborilmasdi.
        Assert.True(InstagramPublishContract.ValidateMedia("reels", Mp4(size: 500L * 1024 * 1024)).Ok);
        Assert.True(InstagramPublishContract.ValidateMedia("reels", Mp4(size: 1024L * 1024 * 1024)).Ok);

        // 1 GB dan oshgani — Meta'ning HAQIQIY chegarasi, ya'ni bu "yo'q" o'rinli.
        var (ok, err) = InstagramPublishContract.ValidateMedia("reels", Mp4(size: 1536L * 1024 * 1024));
        Assert.False(ok);
        Assert.Contains("hajmi", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateMedia_story_videosi_hajm_chegarasi_ALOHIDA_qoladi()
    {
        // Reels chegarasi ko'tarilgani story'ga TEGMAYDI — Meta'da ular ayri (100 MB).
        Assert.False(InstagramPublishContract.ValidateMedia("story", Mp4(dur: 30, size: 500L * 1024 * 1024)).Ok);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]    // story videosi 60 soniyadan uzun bo'lmaydi
    public void ValidateMedia_story_videosi_3_60_soniya(double dur, bool expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("story", Mp4(dur: dur)).Ok);
    }

    [Fact]
    public void ValidateMedia_story_videosi_100MB_dan_oshmaydi()
    {
        Assert.True(InstagramPublishContract.ValidateMedia("story", Mp4(dur: 30, size: 100L * 1024 * 1024)).Ok);
        Assert.False(InstagramPublishContract.ValidateMedia("story", Mp4(dur: 30, size: 120L * 1024 * 1024)).Ok);
    }

    [Theory]
    [InlineData(1080, 1920, true)]    // 9:16 — tavsiya etilgan, lekin YAGONA variant emas
    [InlineData(1080, 1080, true)]    // 1:1 — Instagram qabul qiladi (ilgari BLOKLANARDI)
    [InlineData(1080, 1350, true)]    // 4:5 — vertikal lenta videosi
    [InlineData(1920, 1080, true)]    // 16:9 — gorizontal
    [InlineData(2000, 100, false)]    // 20:1 — Meta chegarasidan (10:1) tashqarida
    [InlineData(100, 2000, true)]     // 0.05:1 — juda tik, lekin 0.01:1 dan keng
    [InlineData(10, 2000, false)]     // 0.005:1 — quyi chegaradan ham past
    public void ValidateMedia_reels_nisbati_Meta_diapazoni_0_01_dan_10_gacha(int w, int h, bool expected)
    {
        // ⚠️ ENG MUHIM QATOR — 1:1. Ilgari "Video 9:16 nisbatda bo'lishi kerak" degan BIZNING
        // xatomiz kvadrat va 4:5 videoni Instagram'ga umuman yubormasdi. Meta esa 0.01:1 dan
        // 10:1 gacha qabul qiladi va kerak bo'lsa o'zi moslaydi.
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("reels", Mp4(w, h)).Ok);
    }

    [Fact]
    public void ValidateMedia_feed_videosi_ham_reels_bilan_bir_xil_diapazonda()
    {
        // `video` turi Meta'ga REELS bo'lib ketadi — demak nisbat qoidasi ham AYNAN bir xil.
        Assert.True(InstagramPublishContract.ValidateMedia("video", Mp4(1080, 1080)).Ok);
        Assert.False(InstagramPublishContract.ValidateMedia("video", Mp4(2000, 100)).Ok);
    }

    [Fact]
    public void ValidateMedia_absurd_nisbat_sababini_OCHIQ_yozadi()
    {
        var (ok, err) = InstagramPublishContract.ValidateMedia("reels", Mp4(2000, 100));
        Assert.False(ok);
        Assert.Contains("20:1", err);      // hozirgi nisbat
        Assert.Contains("10:1", err);      // ruxsat etilgan chegara
    }

    [Theory]
    [InlineData(1080, 1920, true)]
    [InlineData(720, 1280, true)]
    [InlineData(1080, 1080, false)]   // ⚠️ STORY'da 9:16 QATTIQ qoladi — u to'liq ekran
    [InlineData(1080, 1350, false)]
    public void ValidateMedia_story_videosi_9_16_TALABI_saqlanadi(int w, int h, bool expected)
    {
        // Reels yumshatilgani story'ga tegmaydi: story butun ekranni egallaydi va boshqa
        // nisbat katta bo'sh chekka bo'lib chiqadi — foydalanuvchi buni KUTMAYDI.
        Assert.Equal(expected, InstagramPublishContract.ValidateMedia("story", Mp4(w, h, dur: 30)).Ok);
    }

    [Fact]
    public void ValidateMedia_video_olchami_nomalum_bolsa_nisbat_tekshirilmaydi()
    {
        // 0 = "o'lchanmagan" (server video kengligini o'qimaydi — §18.9).
        var unknown = new IgMediaItem("https://cdn.test/a.mp4", IgPublishConst.KindVideo, DurationSeconds: 30);
        Assert.True(InstagramPublishContract.ValidateMedia("reels", unknown).Ok);
    }

    [Fact]
    public void ValidateMedia_video_faqat_mp4_yoki_mov()
    {
        var avi = new IgMediaItem("https://cdn.test/a.avi", IgPublishConst.KindVideo,
            DurationSeconds: 30, Width: 1080, Height: 1920);
        Assert.False(InstagramPublishContract.ValidateMedia("reels", avi).Ok);
    }

    [Fact]
    public void ValidateMedia_alt_matn_1000_belgidan_oshmaydi()
    {
        var item = Jpeg() with { AltText = new string('a', 1001) };
        Assert.False(InstagramPublishContract.ValidateMedia("image", item).Ok);
    }

    [Fact]
    public void ValidateMedia_null_element_yiqilmaydi()
    {
        var (ok, err) = InstagramPublishContract.ValidateMedia("image", null);
        Assert.False(ok);
        Assert.NotEmpty(err);
    }

    // ===================== 6) Butun post =====================

    [Fact]
    public void ValidatePost_media_siz_post_qabul_qilinmaydi()
    {
        Assert.False(InstagramPublishContract.ValidatePost("image", "salom", Array.Empty<IgMediaItem>()).Ok);
        Assert.False(InstagramPublishContract.ValidatePost("image", "salom", null).Ok);
    }

    [Theory]
    [InlineData(1, false)]    // karusel kamida 2 ta
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]   // ko'pi bilan 10 ta
    public void ValidatePost_karuselda_2_10_element(int count, bool expected)
    {
        var items = Enumerable.Range(0, count).Select(_ => Jpeg()).ToList();
        Assert.Equal(expected, InstagramPublishContract.ValidatePost("carousel", "matn", items).Ok);
    }

    [Fact]
    public void ValidatePost_karusel_bolasidagi_caption_XATO()
    {
        // ⚠️ Meta karusel bolasidagi caption'ni JIMGINA e'tiborsiz qoldiradi — foydalanuvchi
        // yozgan matn hech qayerda ko'rinmasdi. Shuning uchun bu yerda ochiq xato.
        var items = new List<IgMediaItem> { Jpeg(), Jpeg(caption: "ikkinchi rasm matni") };
        var (ok, err) = InstagramPublishContract.ValidatePost("carousel", "umumiy matn", items);
        Assert.False(ok);
        Assert.Contains("karusel", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePost_karuselda_nisbat_faqat_BIRINCHI_element_boyicha()
    {
        // Ikkinchi element 9:16 — feed nisbatiga tushmaydi, lekin Instagram uni birinchisining
        // nisbatiga QIRQADI, ya'ni rad etish foydalanuvchini bekorga to'sardi.
        var items = new List<IgMediaItem> { Jpeg(1080, 1080), Jpeg(1080, 1920) };
        Assert.True(InstagramPublishContract.ValidatePost("carousel", "matn", items).Ok);

        // Birinchisi noto'g'ri bo'lsa — post o'tmaydi.
        var bad = new List<IgMediaItem> { Jpeg(1080, 1920), Jpeg(1080, 1080) };
        Assert.False(InstagramPublishContract.ValidatePost("carousel", "matn", bad).Ok);
    }

    [Fact]
    public void ValidatePost_yakka_turda_bir_nechta_media_qabul_qilinmaydi()
    {
        var items = new List<IgMediaItem> { Jpeg(), Jpeg() };
        var (ok, err) = InstagramPublishContract.ValidatePost("image", "matn", items);
        Assert.False(ok);
        Assert.Contains("Karusel", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatePost_uzun_caption_media_dan_OLDIN_ushlanadi()
    {
        var items = new List<IgMediaItem> { Jpeg() };
        var (ok, err) = InstagramPublishContract.ValidatePost("image", new string('a', 3000), items);
        Assert.False(ok);
        Assert.Contains("3000", err);
    }

    [Fact]
    public void ValidateCollaborators_3_tagacha()
    {
        Assert.True(InstagramPublishContract.ValidateCollaborators(null).Ok);
        Assert.True(InstagramPublishContract.ValidateCollaborators(new[] { "a", "b", "c" }).Ok);
        Assert.False(InstagramPublishContract.ValidateCollaborators(new[] { "a", "b", "c", "d" }).Ok);
    }

    // ===================== 7) Konteyner so'rovini qurish =====================

    [Fact]
    public void BuildContainerRequest_image_uchun_media_type_YUBORILMAYDI()
    {
        var r = InstagramPublishContract.BuildContainerRequest("image", Jpeg(), "matn");
        Assert.Equal("", r.MediaType);           // IMAGE — standart qiymat
        Assert.Equal("https://cdn.test/uploads/a.jpg", r.ImageUrl);
        Assert.Equal("", r.VideoUrl);
        Assert.Equal("matn", r.Caption);
        Assert.False(r.IsCarouselItem);
    }

    [Fact]
    public void BuildContainerRequest_reels_uchun_REELS_va_share_to_feed()
    {
        var item = Mp4() with { CoverUrl = "https://cdn.test/c.jpg", ThumbOffsetMs = 0 };
        var r = InstagramPublishContract.BuildContainerRequest(
            "reels", item, "matn", new IgPublishOptions(ShareToFeed: false, AudioName: "trek"));

        Assert.Equal("REELS", r.MediaType);
        Assert.Equal("https://cdn.test/uploads/a.mp4", r.VideoUrl);
        Assert.Equal("https://cdn.test/c.jpg", r.CoverUrl);
        Assert.Equal(0, r.ThumbOffsetMs);        // 0 — haqiqiy qiymat, "berilmagan" emas
        Assert.False(r.ShareToFeed);
        Assert.Equal("trek", r.AudioName);
    }

    [Fact]
    public void BuildContainerRequest_story_da_caption_yoq()
    {
        var r = InstagramPublishContract.BuildContainerRequest("story", Jpeg(1080, 1920), "matn");
        Assert.Equal("STORIES", r.MediaType);
        Assert.Equal("", r.Caption);
    }

    [Fact]
    public void BuildContainerRequest_karusel_bolasida_caption_yoq_va_is_carousel_item_bor()
    {
        var r = InstagramPublishContract.BuildContainerRequest(
            "carousel", Jpeg(), "matn", asCarouselChild: true);
        Assert.True(r.IsCarouselItem);
        Assert.Equal("", r.Caption);
        Assert.Equal("", r.MediaType);           // rasm bolasi — media_type kerak emas
    }

    [Fact]
    public void BuildCarouselParent_children_va_caption_ota_onada()
    {
        var r = InstagramPublishContract.BuildCarouselParent(new[] { "1", "2" }, "matn");
        Assert.Equal("CAROUSEL", r.MediaType);
        Assert.Equal("matn", r.Caption);
        Assert.Equal(2, r.Children!.Count);
    }

    [Fact]
    public void BuildCarouselParent_location_id_YUBORILMAYDI()
    {
        // 🔴 Meta hujjatlari ZID: qo'llanmada `location_id` umumiy parametr, endpoint
        // reference jadvalida esa IMAGE ✓ / REELS ✓ / CAROUSEL ✗. Graph qo'llab-quvvatlamagan
        // parametrni jimgina tashlamaydi — BUTUN so'rovni `code 100` bilan rad etadi.
        // Ya'ni joylashuv tanlangan HAR BIR karusel yiqilardi. Xavfsiz tomon tanlandi.
        // Tekshirish yo'li — `BuildCarouselParent` XML izohida yozilgan.
        var opt = new IgPublishOptions(LocationId: "12345", Collaborators: new[] { "hamkor" });
        var r = InstagramPublishContract.BuildCarouselParent(new[] { "1", "2" }, "matn", opt);

        Assert.Equal("", r.LocationId);
        // ⚠️ `collaborators` esa QOLADI — u reference'da karusel uchun ham belgilangan.
        Assert.Equal(new[] { "hamkor" }, r.Collaborators);
    }

    [Fact]
    public void BuildContainerRequest_yakka_postda_location_id_QOLADI()
    {
        // Karuseldagi cheklov yakka rasm/reels'ga TEGMAYDI — u yerda maydon hujjatda aniq.
        var opt = new IgPublishOptions(LocationId: "12345");
        Assert.Equal("12345", InstagramPublishContract.BuildContainerRequest("image", Jpeg(), "m", opt).LocationId);
        Assert.Equal("12345", InstagramPublishContract.BuildContainerRequest("reels", Mp4(), "m", opt).LocationId);
        // Story'da esa avvalgidek bo'sh.
        Assert.Equal("", InstagramPublishContract.BuildContainerRequest("story", Jpeg(1080, 1920), "m", opt).LocationId);
    }

    // ===================== 7.5) Ogohlantirishlar (xato EMAS) =====================

    [Fact]
    public void MediaWarning_9_16_dan_uzoq_reels_uchun_OGOHLANTIRADI_lekin_bloklamaydi()
    {
        var square = Mp4(1080, 1080);

        // Post baribir o'tadi — bu "xato" emas...
        Assert.True(InstagramPublishContract.ValidateMedia("reels", square).Ok);

        // ...lekin foydalanuvchi natijani bilib turishi kerak.
        var w = InstagramPublishContract.MediaWarning("reels", square);
        Assert.NotEmpty(w);
        Assert.Contains("9:16", w);
    }

    [Fact]
    public void MediaWarning_9_16_video_uchun_JIM()
    {
        Assert.Empty(InstagramPublishContract.MediaWarning("reels", Mp4(1080, 1920)));
        Assert.Empty(InstagramPublishContract.MediaWarning("video", Mp4(720, 1280)));
    }

    [Fact]
    public void MediaWarning_story_va_rasm_uchun_TAKRORLANMAYDI()
    {
        // Story'da nisbat QATTIQ tekshiriladi (ValidateMedia xato beradi) — bir xil gapni
        // ikki marta aytish foydalanuvchini chalg'itardi.
        Assert.Empty(InstagramPublishContract.MediaWarning("story", Mp4(1080, 1080)));
        // Rasm — bu ogohlantirish videoga tegishli.
        Assert.Empty(InstagramPublishContract.MediaWarning("image", Jpeg(1080, 1080)));
    }

    [Fact]
    public void MediaWarning_olcham_nomalum_bolsa_JIM_qoladi()
    {
        // "Bilmasak — qo'rqitmaymiz" (IgMediaItem kelishuvi bilan bir xil).
        var unknown = new IgMediaItem("https://cdn.test/a.mp4", IgPublishConst.KindVideo, DurationSeconds: 30);
        Assert.Empty(InstagramPublishContract.MediaWarning("reels", unknown));
        Assert.Empty(InstagramPublishContract.MediaWarning("reels", null));
    }

    [Fact]
    public void PostWarnings_karuselda_faqat_BIRINCHI_element_boyicha()
    {
        // Nisbat qoidasi bilan AYNAN bir xil: qolganlari birinchisiga qirqiladi.
        var first = new List<IgMediaItem> { Mp4(1080, 1080), Mp4(1080, 1920) };
        Assert.Single(InstagramPublishContract.PostWarnings("carousel", first));

        var second = new List<IgMediaItem> { Mp4(1080, 1920), Mp4(1080, 1080) };
        Assert.Empty(InstagramPublishContract.PostWarnings("carousel", second));
    }

    [Fact]
    public void PostWarnings_ogohlantirish_ValidatePost_ni_TOXTATMAYDI()
    {
        var items = new List<IgMediaItem> { Mp4(1080, 1080) };
        Assert.True(InstagramPublishContract.ValidatePost("reels", "matn", items).Ok);
        Assert.NotEmpty(InstagramPublishContract.PostWarnings("reels", items));

        Assert.Empty(InstagramPublishContract.PostWarnings("reels", null));
        Assert.Empty(InstagramPublishContract.PostWarnings("reels", Array.Empty<IgMediaItem>()));
    }

    // ===================== 8) Poll jadvali =====================

    [Theory]
    [InlineData(0, 30)]     // hisob xatosi ham birinchi qadamni beradi
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 300)]
    [InlineData(5, 300)]    // 5 daqiqada TO'XTAYDI (Meta tavsiyasi), cheksiz o'smaydi
    [InlineData(100, 300)]
    [InlineData(-3, 30)]
    public void NextPollDelaySeconds_30_60_120_300_ketma_ketligi(int attempt, int expected)
    {
        Assert.Equal(expected, InstagramPublishContract.NextPollDelaySeconds(attempt));
    }

    [Fact]
    public void IsPollExpired_10_daqiqadan_keyin_toxtaydi()
    {
        Assert.False(InstagramPublishContract.IsPollExpired(Iso(Now.AddMinutes(-5)), Now));
        Assert.False(InstagramPublishContract.IsPollExpired(Iso(Now.AddMinutes(-9)), Now));
        Assert.True(InstagramPublishContract.IsPollExpired(Iso(Now.AddMinutes(-11)), Now));
    }

    [Fact]
    public void IsPollExpired_buzuq_sana_da_toxtaydi()
    {
        // "Bilmasak — cheksiz kutmaymiz": post xatoga chiqadi va operator ko'radi.
        Assert.True(InstagramPublishContract.IsPollExpired("", Now));
        Assert.True(InstagramPublishContract.IsPollExpired(null, Now));
        Assert.True(InstagramPublishContract.IsPollExpired("buzuq-sana", Now));
    }

    // ===================== 9) Konteyner muddati (24 soat) =====================

    [Fact]
    public void IsContainerExpired_23_soat_TIRIK_25_soat_OLGAN()
    {
        Assert.False(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(-23)), Now));
        Assert.True(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(-25)), Now));
        Assert.True(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(-24)), Now));
    }

    [Fact]
    public void IsContainerExpired_ISO_satrli_variant_ham_ishlaydi()
    {
        Assert.False(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(-23)), Iso(Now)));
        Assert.True(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(-25)), Iso(Now)));
    }

    [Fact]
    public void IsContainerExpired_buzuq_sana_OLGAN_deb_qaraladi()
    {
        // Konteyner yaratish arzon (kvota faqat media_publish'da sanaladi), shuning uchun
        // "bilmasak — qaytadan yaratamiz" xavfsizroq.
        Assert.True(InstagramPublishContract.IsContainerExpired("", Now));
        Assert.True(InstagramPublishContract.IsContainerExpired(null, Now));
        Assert.True(InstagramPublishContract.IsContainerExpired("2026-13-99", Now));
    }

    [Fact]
    public void IsContainerExpired_kelajakdagi_sana_tirik()
    {
        Assert.False(InstagramPublishContract.IsContainerExpired(Iso(Now.AddHours(1)), Now));
    }

    [Fact]
    public void IsDue_vaqti_kelgan_postni_ajratadi()
    {
        Assert.True(InstagramPublishContract.IsDue(Iso(Now.AddMinutes(-1)), Now));
        Assert.True(InstagramPublishContract.IsDue(Iso(Now), Now));
        Assert.False(InstagramPublishContract.IsDue(Iso(Now.AddMinutes(1)), Now));
        Assert.False(InstagramPublishContract.IsDue("buzuq", Now));   // navbatni band qilmaydi
        Assert.False(InstagramPublishContract.IsDue(null, Now));
    }

    // ===================== 10) Kvota =====================

    [Theory]
    [InlineData(2, 50, false)]
    [InlineData(50, 50, true)]
    [InlineData(51, 50, true)]
    [InlineData(99, 0, false)]     // quota_total noma'lum — TAXMIN qilib to'xtatilmaydi
    [InlineData(99, -1, false)]
    public void QuotaExceeded_nomalum_limitda_postni_toxtatmaydi(int usage, int total, bool expected)
    {
        // ⚠️ Meta hujjatlari zid (100 vs 50) — kodda hech qanday standart qiymat YO'Q.
        Assert.Equal(expected, InstagramPublishContract.QuotaExceeded(usage, total));
    }

    [Fact]
    public void QuotaText_nomalum_limitni_ochiq_yozadi()
    {
        Assert.Equal("2 / 50", InstagramPublishContract.QuotaText(2, 50));
        Assert.Contains("noma'lum", InstagramPublishContract.QuotaText(2, 0));
    }

    // ===================== 11) Xato kodlari (§5.8) =====================

    [Theory]
    [InlineData(2207052)]
    [InlineData(2207020)]
    [InlineData(2207003)]
    [InlineData(2207005)]
    [InlineData(2207009)]
    [InlineData(2207010)]
    [InlineData(2207026)]
    [InlineData(2207042)]
    [InlineData(2207001)]
    public void ErrorText_har_kod_uchun_ozbekcha_matn_beradi(int code)
    {
        var text = InstagramPublishContract.ErrorText(code);
        Assert.NotEmpty(text);
        // Matn KOD RAQAMINI takrorlamaydi — bu tanilgan kod, odam o'qiydigan sabab yoziladi.
        Assert.DoesNotContain(code.ToString(), text);
    }

    [Fact]
    public void ErrorText_kodlar_bir_biridan_farq_qiladi()
    {
        var codes = new[] { 2207052, 2207020, 2207003, 2207005, 2207009, 2207010, 2207026, 2207042, 2207001 };
        var texts = codes.Select(c => InstagramPublishContract.ErrorText(c)).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void ErrorText_NOMALUM_kod_jimgina_yutilmaydi()
    {
        // ⚠️ Rasmiy kodlar sahifasi yo'q — Meta yangi kod qo'shishi mumkin. Noma'lum kod
        // raqami bilan qaytadi, ya'ni operator qidiruvga soladigan narsa qoladi.
        var text = InstagramPublishContract.ErrorText(9999999);
        Assert.NotEmpty(text);
        Assert.Contains("9999999", text);

        var withMsg = InstagramPublishContract.ErrorText(9999999, "Something went wrong");
        Assert.Contains("Something went wrong", withMsg);

        Assert.NotEmpty(InstagramPublishContract.ErrorText(0));
        Assert.NotEmpty(InstagramPublishContract.ErrorText(0, ""));
    }

    [Theory]
    [InlineData("Error: 2207020 - The media container has expired", 2207020)]
    [InlineData("media download failed 2207052", 2207052)]
    [InlineData("2207005", 2207005)]
    [InlineData("hech qanday kod yo'q", 0)]
    [InlineData("12207020", 0)]        // uzunroq sonning bo'lagi — kod EMAS
    [InlineData("1234567", 0)]         // 2207 bilan boshlanmaydi
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ExtractErrorCode_status_matnidan_kodni_ajratadi(string? text, int expected)
    {
        Assert.Equal(expected, InstagramPublishContract.ExtractErrorCode(text));
    }

    [Fact]
    public void ContainerErrorText_status_matnidagi_kodni_ozbekchaga_aylantiradi()
    {
        var t = InstagramPublishContract.ContainerErrorText("ERROR", "Error: 2207052 - Media download failed");
        Assert.Equal(InstagramPublishContract.ErrorText(2207052), t);
    }

    [Fact]
    public void ContainerErrorText_EXPIRED_da_kod_bolmasa_ham_togri_sabab()
    {
        var t = InstagramPublishContract.ContainerErrorText("EXPIRED", "");
        Assert.Equal(InstagramPublishContract.ErrorText(2207020), t);
    }

    [Fact]
    public void ContainerErrorText_buzuq_kirishda_yiqilmaydi()
    {
        Assert.NotEmpty(InstagramPublishContract.ContainerErrorText(null, null));
        Assert.NotEmpty(InstagramPublishContract.ContainerErrorText("", ""));
    }
}
