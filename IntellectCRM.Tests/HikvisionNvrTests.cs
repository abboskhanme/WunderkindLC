using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// NVR arxivi — manzil, vaqt mintaqasi va javobni o'qish qulflari.
/// Qoida: <c>.claude/rules/cameras.md</c> §8.
/// </summary>
public class HikvisionNvrTests
{
    [Theory]
    [InlineData(1, "101")]
    [InlineData(2, "201")]
    [InlineData(12, "1201")]
    public void Kanal_track_id_ga_ogiriladi(int channel, string expected) =>
        Assert.Equal(expected, HikvisionNvr.TrackId(channel));

    [Fact]
    public void Sub_oqim_uchun_02_bilan_tugaydi() =>
        Assert.Equal("102", HikvisionNvr.TrackId(1, sub: true));

    /// <summary>
    /// ⚠️ ENG MUHIM TEST. Foydalanuvchi ekranda MARKAZ vaqtini (UTC+5) tanlaydi, Hikvision esa
    /// UTC kutadi. Konversiya qilinmasa arxiv 5 soat SURILGAN holda kelardi — va bu "topilmadi"
    /// emas, "boshqa vaqtning yozuvi" bo'lgani uchun darhol sezilmasdi.
    /// </summary>
    [Fact]
    public void Vaqt_UTC_ga_ogiriladi_markaz_vaqtidan_5_soat_ayriladi()
    {
        // 2026-09-07 14:30 (Toshkent) = 09:30 UTC
        Assert.Equal("20260907T093000Z", HikvisionNvr.PlaybackTime(new DateTime(2026, 9, 7, 14, 30, 0)));
    }

    [Fact]
    public void Vaqt_konversiyasi_SUTKA_chegarasidan_otadi()
    {
        // 2026-09-07 02:00 (Toshkent) = OLDINGI kun 21:00 UTC
        Assert.Equal("20260906T210000Z", HikvisionNvr.PlaybackTime(new DateTime(2026, 9, 7, 2, 0, 0)));
    }

    [Fact]
    public void Playback_manzili_toliq_quriladi()
    {
        var url = HikvisionNvr.PlaybackUrl(
            "192.168.1.50", 554, "admin", "p@ss", channel: 3,
            new DateTime(2026, 9, 7, 14, 30, 0), new DateTime(2026, 9, 7, 14, 35, 0));

        Assert.StartsWith("rtsp://admin:p%40ss@192.168.1.50:554/Streaming/tracks/301?", url);
        Assert.Contains("starttime=20260907T093000Z", url);
        Assert.Contains("endtime=20260907T093500Z", url);
    }

    [Fact]
    public void Port_0_bolsa_554_ishlatiladi()
    {
        var url = HikvisionNvr.PlaybackUrl("nvr", 0, "u", "p", 1, DateTime.Now, DateTime.Now.AddMinutes(1));
        Assert.Contains("@nvr:554/", url);
    }

    /// <summary>
    /// ⚠️ Parol xato matnida va logda CHIQMASLIGI shart — xato xabari NVR manzilini o'z ichiga
    /// oladi (diagnostika uchun kerak), ya'ni tozalash MAJBURIY.
    /// </summary>
    [Fact]
    public void Redact_login_parolni_yashiradi()
    {
        var url = HikvisionNvr.PlaybackUrl("10.0.0.5", 554, "admin", "MaxfiyParol1", 1,
            new DateTime(2026, 9, 7, 10, 0, 0), new DateTime(2026, 9, 7, 10, 1, 0));
        var safe = HikvisionNvr.Redact(url);

        Assert.DoesNotContain("MaxfiyParol1", safe);
        Assert.DoesNotContain("admin", safe);
        Assert.Contains("rtsp://***:***@10.0.0.5:554/", safe);
    }

    [Fact]
    public void Redact_login_yoq_manzilni_ozgartirmaydi()
    {
        const string url = "rtsp://10.0.0.5:554/Streaming/tracks/101";
        Assert.Equal(url, HikvisionNvr.Redact(url));
    }

    [Fact]
    public void Qidiruv_sorovida_track_va_UTC_vaqt_boladi()
    {
        var body = HikvisionNvr.SearchBody("abc", channel: 2,
            new DateTime(2026, 9, 7, 14, 0, 0), new DateTime(2026, 9, 7, 15, 0, 0));

        Assert.Contains("<searchID>abc</searchID>", body);
        Assert.Contains("<trackID>201</trackID>", body);
        Assert.Contains("<startTime>2026-09-07T09:00:00Z</startTime>", body);
        Assert.Contains("<endTime>2026-09-07T10:00:00Z</endTime>", body);
    }

    [Fact]
    public void Qidiruv_javobi_oqiladi_va_vaqt_MARKAZ_vaqtiga_qaytariladi()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <CMSearchResult version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
              <responseStatusStrg>OK</responseStatusStrg>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2026-09-07T09:00:00Z</startTime>
                    <endTime>2026-09-07T10:30:00Z</endTime>
                  </timeSpan>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;
        var (segments, status) = HikvisionNvr.ParseSearch(xml);

        Assert.Equal("OK", status);
        var seg = Assert.Single(segments);
        // UTC 09:00 -> markaz 14:00
        Assert.Equal("2026-09-07T14:00:00", seg.Start);
        Assert.Equal("2026-09-07T15:30:00", seg.End);
    }

    /// <summary>
    /// ⚠️ Nom maydoni (namespace) firmware'dan firmware'ga farq qiladi — elementlar LOKAL nom
    /// bo'yicha qidiriladi, aks holda javob to'g'ri kelgani bilan ro'yxat bo'sh chiqardi.
    /// </summary>
    [Fact]
    public void Namespace_boshqacha_bolsa_ham_oqiladi()
    {
        const string xml = """
            <CMSearchResult>
              <matchList><searchMatchItem><timeSpan>
                <startTime>2026-01-01T00:00:00Z</startTime><endTime>2026-01-01T00:10:00Z</endTime>
              </timeSpan></searchMatchItem></matchList>
            </CMSearchResult>
            """;
        Assert.Single(HikvisionNvr.ParseSearch(xml).Segments);
    }

    [Fact]
    public void Buzuq_javob_yiqilmaydi_sabab_qaytadi()
    {
        var (segments, status) = HikvisionNvr.ParseSearch("<html>401 Unauthorized");
        Assert.Empty(segments);
        Assert.Contains("XML emas", status);
    }

    [Fact]
    public void Ffmpeg_qayta_kodlamaydi_va_oqim_sifatida_beradi()
    {
        var args = HikvisionNvr.FfmpegArgs("rtsp://x/y", 300);
        var joined = string.Join(" ", args);

        Assert.Contains("-c copy", joined);                    // transcode YO'Q (1 GB RAM server)
        Assert.Contains("frag_keyframe+empty_moov", joined);   // moov oxirida bo'lsa oqim bo'lmasdi
        Assert.Contains("-rtsp_transport tcp", joined);        // UDP'da uzoq masofada video to'kilardi
        Assert.Contains("-t 300", joined);
        Assert.EndsWith("pipe:1", joined);
    }
}

/// <summary>Arxiv QAYERDAN olinadi — NVR lokal yozuvdan ustun.</summary>
public class CameraArchiveSourceTests
{
    private static Camera Cam(bool active = true, bool record = true, int nvrChannel = 0) =>
        new() { IsActive = active, RecordEnabled = record, NvrChannel = nvrChannel };

    [Fact]
    public void NVR_kanali_bolsa_arxiv_NVR_dan()
    {
        Assert.Equal(CameraRules.ArchiveNvr,
            CameraRules.ArchiveSource(nvrEnabled: true, globalRecordEnabled: false, Cam(nvrChannel: 1)));
    }

    /// <summary>
    /// ⚠️ NVR allaqachon yozib turibdi — bizda ikkinchi nusxa saqlashning ma'nosi yo'q.
    /// Aks holda admin NVR'ni ulab, keyin "nega disk to'lyapti" degan savolga tushardi.
    /// </summary>
    [Fact]
    public void NVR_bolsa_lokal_yozuv_yoqilgan_bolsa_ham_BIZ_yozmaymiz()
    {
        var cam = Cam(record: true, nvrChannel: 1);
        Assert.Equal(CameraRules.ArchiveNvr, CameraRules.ArchiveSource(true, true, cam));
        Assert.False(CameraRules.ShouldRecordWithNvr(nvrEnabled: true, globalRecordEnabled: true, cam));
    }

    [Fact]
    public void NVR_yoq_bolsa_lokal_yozuvga_tushadi()
    {
        var cam = Cam(record: true);
        Assert.Equal(CameraRules.ArchiveLocal, CameraRules.ArchiveSource(false, true, cam));
        Assert.True(CameraRules.ShouldRecordWithNvr(false, true, cam));
    }

    [Fact]
    public void NVR_yoqilgan_lekin_kanal_berilmagan_kamera_lokal_yozuvda_qoladi()
    {
        // NVR umumiy yoqilgan, lekin bu kamera unda yo'q (kanal 0) — eski yo'l bilan ishlaydi.
        Assert.Equal(CameraRules.ArchiveLocal, CameraRules.ArchiveSource(true, true, Cam(nvrChannel: 0)));
    }

    [Fact]
    public void Ikkalasi_ham_yoq_bolsa_arxiv_YOQ()
    {
        Assert.Equal(CameraRules.ArchiveNone, CameraRules.ArchiveSource(false, false, Cam()));
    }

    [Fact]
    public void Faol_bolmagan_kamera_NVR_dan_baribir_oqiladi()
    {
        // Kamera "faol emas" = biz jonli ko'rsatmaymiz. Lekin NVR'dagi ESKI arxiv o'z joyida
        // turibdi va uni ochish mumkin bo'lishi kerak — aks holda kamera o'chirilishi bilan
        // butun tarix yo'qolgandek bo'lardi.
        Assert.Equal(CameraRules.ArchiveNvr,
            CameraRules.ArchiveSource(true, false, Cam(active: false, nvrChannel: 4)));
    }
}
