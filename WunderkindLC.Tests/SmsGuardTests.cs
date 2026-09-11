using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-09-07 dagi prod hodisalaridan kelib chiqqan qulflar (SMS oqimi).
/// </summary>
public class SmsGuardTests
{
    /* ---------- {link} — qo'lda yuborib bo'lmaydigan token ---------- */

    /// <summary>
    /// ⚠️ HAQIQIY HODISA: lid oynasining "tayyor matn" ro'yxatidan `test_link` andozasi
    /// tanlanib, oddiy "SMS yuborish" bosilgan — abonentga «...sizga {link} testi yuborildi»
    /// matni KETGAN. Hech qanday xato ko'rinmagan (SMS muvaffaqiyatli yuborilgan edi).
    /// </summary>
    [Fact]
    public void Link_tokeni_qolda_yuborishda_TUTILADI()
    {
        Assert.Equal("{link}", MessageTokenCatalog.ForbiddenInManual(
            "Assalomu alaykum sizga {link} testi yuborildi."));
    }

    [Fact]
    public void Link_tokeni_katta_harfda_ham_tutiladi() =>
        Assert.Equal("{link}", MessageTokenCatalog.ForbiddenInManual("Havola: {LINK}"));

    [Fact]
    public void Oddiy_matn_otadi()
    {
        Assert.Null(MessageTokenCatalog.ForbiddenInManual("Assalomu alaykum {fish}! Darsingiz {dars_vaqti} da."));
        Assert.Null(MessageTokenCatalog.ForbiddenInManual(""));
        Assert.Null(MessageTokenCatalog.ForbiddenInManual(null));
    }

    /// <summary>
    /// ⚠️ Qolgan "event" tokenlari BLOKLANMAYDI: masalan {dars_vaqti} ni qo'lda yuborishda
    /// MessageTokenizer guruh jadvalidan to'ldira oladi. Ro'yxat ATAYIN faqat {link}.
    /// </summary>
    [Fact]
    public void Boshqa_event_tokenlari_bloklanmaydi()
    {
        Assert.Null(MessageTokenCatalog.ForbiddenInManual("Summa: {summa}, ball: {ball}, natija: {natija}"));
        Assert.Single(MessageTokenCatalog.ManualForbidden);
    }

    /* ---------- Jo'natuvchi nomi (nickname) ---------- */

    /// <summary>
    /// ⚠️ HAQIQIY HODISA: prodda `CenterMeta.EskizFrom` da "tasdiqlangan_nikname" — ya'ni
    /// o'rniga ism yozilishi kerak bo'lgan NAMUNA matn turgan. SMS baribir ketardi (Eskiz
    /// 4546 dan yuboradi), lekin markaz nomi ko'rinmasdi va buni hech narsa ko'rsatmasdi.
    /// </summary>
    [Theory]
    [InlineData("tasdiqlangan_nikname")]
    [InlineData("Tasdiqlangan_Nikname")]
    [InlineData("  tasdiqlangan_nickname  ")]
    [InlineData("nickname")]
    public void Namuna_jonatuvchi_nomi_tanib_olinadi(string from) =>
        Assert.True(EskizService.IsPlaceholderSender(from));

    /// <summary>Ro'yxat ATAYIN qisqa — haqiqiy markaz nomini tasodifan rad etmasin.</summary>
    [Theory]
    [InlineData("Wunderkind")]
    [InlineData("4546")]
    [InlineData("WunderkindEdu")]
    [InlineData("")]
    [InlineData(null)]
    public void Haqiqiy_nom_namuna_deb_hisoblanmaydi(string? from) =>
        Assert.False(EskizService.IsPlaceholderSender(from));
}
