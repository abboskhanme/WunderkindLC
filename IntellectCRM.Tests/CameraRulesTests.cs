using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// Kamera YOZUVI qoidalari — <c>.claude/rules/cameras.md</c> dagi kelishuvning qulfi.
///
/// <para>Eng muhimi: <b>jonli kuzatuv va yozuv — ikki boshqa narsa</b>, va yozuvning BOSH kaliti
/// o'chiq bo'lganda hech qaysi kamera yozilmasligi kerak (har birini qo'lda o'chirish shart emas).</para>
/// </summary>
public class CameraRulesTests
{
    private static Camera Cam(bool active = true, bool record = true, int retention = 7) =>
        new() { Name = "test", RtspUrl = "rtsp://x", IsActive = active, RecordEnabled = record, RetentionDays = retention };

    [Fact]
    public void Bosh_kalit_ochiq_bolsa_HECH_QAYSI_kamera_yozilmaydi()
    {
        // Kamera o'zi "yozuv yoqilgan" bo'lsa ham — bosh kalit hal qiladi.
        Assert.False(CameraRules.ShouldRecord(globalRecordEnabled: false, Cam(record: true)));
        Assert.False(CameraRules.ShouldRecord(globalRecordEnabled: false, Cam(record: false)));
    }

    [Fact]
    public void Bosh_kalit_yoqilganda_kameraning_OZ_bayrogi_hal_qiladi()
    {
        Assert.True(CameraRules.ShouldRecord(true, Cam(record: true)));
        Assert.False(CameraRules.ShouldRecord(true, Cam(record: false)));
    }

    [Fact]
    public void Faol_bolmagan_kamera_yozilmaydi()
    {
        Assert.False(CameraRules.ShouldRecord(true, Cam(active: false, record: true)));
    }

    [Fact]
    public void Yozuv_YOQ_bolsa_RTSP_faqat_kimdir_qaraganda_ochiladi()
    {
        // ⚠️ Bu shunchaki optimizatsiya emas: `sourceOnDemand = false` markazning internet
        // kanalini har kamera uchun 24/7 band qiladi. Yozuv bo'lmasa buning ma'nosi yo'q.
        Assert.True(CameraRules.SourceOnDemand(record: false));
        Assert.False(CameraRules.SourceOnDemand(record: true));
    }

    [Theory]
    [InlineData(7, "168h")]
    [InlineData(1, "24h")]
    [InlineData(365, "8760h")]
    [InlineData(0, "0s")]     // 0 kun = cheksiz (shlyuz o'chirmaydi)
    [InlineData(-5, "0s")]    // manfiy ham cheksiz sifatida — teskari muddat yasamaymiz
    public void Saqlash_muddati_shlyuz_formatiga_ogiriladi(int days, string expected)
    {
        Assert.Equal(expected, CameraRules.RecordDeleteAfter(days));
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(9999, 365)]
    public void Muddat_chegaralanadi(int input, int expected)
    {
        Assert.Equal(expected, CameraRules.ClampRetention(input));
    }
}
