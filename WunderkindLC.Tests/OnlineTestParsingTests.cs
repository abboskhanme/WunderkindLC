using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// ONLAYN TEST — javoblarni o'qish (parsing) va tekshirish mantig'i. Bu yerdagi hamma narsa SOF:
/// baza ham, tarmoq ham kerak emas.
///
/// <para>Ikkita PARSER bor va ular ATAYLAB boshqacha ishlaydi (`.claude/rules/tests.md` da
/// hujjatlashtirilgan "gotcha"):</para>
/// <list type="bullet">
///   <item><see cref="OnlineTestService.Normalize"/> — ILOVA (student app) yuboradigan POZITSIYALI
///   satr: har belgi = bitta savol, javobsizi <c>'-'</c>. O'rin SAQLANADI.</item>
///   <item><see cref="OnlineTestBotService.ParseAnswers"/> — Telegram botdagi ERKIN matn
///   ("abcda", "1a 2b 3c"): faqat harflar yig'iladi, <c>'-'</c> va raqamlar TASHLANADI.</item>
/// </list>
/// <para>Shuning uchun bir xil kirish ikki xil natija beradi — quyidagi testlar shu farqni
/// MUZLATIB qo'yadi (kimdir ikkalasini "birlashtirmoqchi" bo'lsa test yiqiladi).</para>
/// </summary>
public class OnlineTestParsingTests
{
    /* =========================================================================================
     *  1) OnlineTestService.Normalize — ILOVA (pozitsiyali)
     * ========================================================================================= */

    [Fact]
    public void Normalize_PozitsiyaSaqlanadi_TireJavobsizDeganBelgi()
    {
        // Ilova 5 savoldan 1-, 3-, 5- ga javob bergan: o'rtadagi bo'shliqlar SAQLANISHI shart,
        // aks holda javoblar siljib ketadi va ball noto'g'ri hisoblanadi.
        Assert.Equal("A-C-D", OnlineTestService.Normalize("A-C-D", 5, 4));
    }

    [Fact]
    public void Normalize_KaltaJavob_TireBilanToldiriladi()
    {
        // Faqat 2 ta javob kelgan, savol 5 ta → qolgani javobsiz ('-').
        Assert.Equal("AB---", OnlineTestService.Normalize("AB", 5, 4));
    }

    [Fact]
    public void Normalize_OrtiqchaBelgilar_Kesiladi()
    {
        // Savol 3 ta bo'lsa 6 ta belgidan faqat birinchi 3 tasi olinadi.
        Assert.Equal("ABC", OnlineTestService.Normalize("ABCDAB", 3, 4));
    }

    [Fact]
    public void Normalize_VariantdanTashqariHarf_JavobsizBoladi()
    {
        // OptionCount=4 → ruxsat A..D. 'E' variant yo'q — javobsiz deb olinadi (o'rni saqlanadi).
        Assert.Equal("A-C", OnlineTestService.Normalize("AEC", 3, 4));
    }

    [Fact]
    public void Normalize_BeshtaVariantda_EHamQabulQilinadi()
    {
        Assert.Equal("AEC", OnlineTestService.Normalize("AEC", 3, 5));
    }

    [Fact]
    public void Normalize_KirillHarflari_LotinGaOgiriladi()
    {
        // Telefon klaviaturasi kirillda bo'lsa ham javob yo'qolmasin: А В С Д Е → A B C D E.
        Assert.Equal("ABCDE", OnlineTestService.Normalize("АВСДЕ", 5, 5));
    }

    [Fact]
    public void Normalize_KichikHarf_KattaGaAylanadi()
    {
        Assert.Equal("ABCD", OnlineTestService.Normalize("abcd", 4, 4));
    }

    [Fact]
    public void Normalize_TinishBelgiVaBoshliq_JavobsizOrinEgallaydi()
    {
        // DIQQAT: bu yerda bo'shliq TASHLANMAYDI — u ham bitta savolning o'rnini egallaydi
        // (ilova pozitsiyali satr yuboradi, erkin matn emas).
        Assert.Equal("A-B-", OnlineTestService.Normalize("A B ", 4, 4));
    }

    [Fact]
    public void Normalize_SavolYoq_BoshSatr()
    {
        Assert.Equal("", OnlineTestService.Normalize("ABCD", 0, 4));
        Assert.Equal("", OnlineTestService.Normalize("ABCD", -3, 4));
    }

    [Fact]
    public void Normalize_BoshKirish_HammasiJavobsiz()
    {
        Assert.Equal("----", OnlineTestService.Normalize("", 4, 4));
    }

    [Theory]
    // optionCount 2..6 oralig'iga qisiladi: 0/1 → 2 (A..B), 10 → 6 (A..F).
    [InlineData(0, "ABC", "AB-")]
    [InlineData(1, "ABC", "AB-")]
    [InlineData(10, "AEF", "AEF")]
    public void Normalize_VariantlarSoni_IkkidanOltigachaQisiladi(int optionCount, string raw, string expected)
    {
        Assert.Equal(expected, OnlineTestService.Normalize(raw, 3, optionCount));
    }

    /* =========================================================================================
     *  2) OnlineTestBotService.ParseAnswers — BOT (erkin matn)
     * ========================================================================================= */

    [Fact]
    public void ParseAnswers_TireTashlanadi_NormalizeDanFARQLI()
    {
        // MUZLATILGAN FARQ: aynan shu sabab ilova uchun alohida Normalize yozilgan.
        // Bot: "A-C-D" → "ACD" (3 ta javob), ilova: "A-C-D" → "A-C-D" (1-, 3-, 5- savol).
        Assert.Equal("ACD", OnlineTestBotService.ParseAnswers("A-C-D", 4));
        Assert.Equal("A-C-D", OnlineTestService.Normalize("A-C-D", 5, 4));
    }

    [Fact]
    public void ParseAnswers_RaqamVaTinishBelgilariEtiborsiz()
    {
        Assert.Equal("AB", OnlineTestBotService.ParseAnswers("1) a, 2) b", 4));
    }

    [Fact]
    public void ParseAnswers_UzluksizMatn_TogridanToWriOqiladi()
    {
        Assert.Equal("ABCDA", OnlineTestBotService.ParseAnswers("abcda", 4));
    }

    [Fact]
    public void ParseAnswers_KirillHarflari_LotinGaOgiriladi()
    {
        Assert.Equal("ABCDE", OnlineTestBotService.ParseAnswers("АВСДЕ", 5));
    }

    [Fact]
    public void ParseAnswers_NotanishHarf_BoshSatrQaytadi()
    {
        // "Salom" kabi oddiy matn javob deb qabul qilinmasligi kerak (aks holda o'quvchining
        // har bir xabari javob sifatida tushunilardi).
        Assert.Equal("", OnlineTestBotService.ParseAnswers("Salom, testni qanday ishlayman?", 4));
    }

    [Fact]
    public void ParseAnswers_VariantdanTashqariHarf_BoshSatr()
    {
        // OptionCount=4 (A..D) da 'E' — harf, lekin variant emas → butun xabar rad etiladi
        // (Normalize'dan yana bir farq: u 'E' ni '-' qilib qo'ya qolardi).
        Assert.Equal("", OnlineTestBotService.ParseAnswers("ABE", 4));
        Assert.Equal("A-", OnlineTestService.Normalize("AE", 2, 4));
    }

    [Fact]
    public void ParseAnswers_BoshYokiFaqatBoshliq_BoshSatr()
    {
        Assert.Equal("", OnlineTestBotService.ParseAnswers("", 4));
        Assert.Equal("", OnlineTestBotService.ParseAnswers("   ", 4));
        Assert.Equal("", OnlineTestBotService.ParseAnswers(null!, 4));
    }

    [Fact]
    public void ParseAnswers_RaqamlanganJavoblar_TARTIBiSaqlanmaydi_HOZIRGIXULQ()
    {
        // HOZIRGI (xato) xulqni muzlatadi — pastdagi Skip'li test to'g'ri xulqni tasvirlaydi.
        // O'quvchi 5- javobni 4- dan oldin yozgan: raqamlar e'tiborsiz qolgani uchun harflar
        // YOZILISH tartibida yig'iladi.
        Assert.Equal("ABCED", OnlineTestBotService.ParseAnswers("1a 2b 3c 5e 4d", 5));
    }

    [Fact(Skip = "XATO (OnlineTestBotService.cs:414-430): raqamlar e'tiborsiz — javob RAQAMI o'qilmaydi")]
    public void ParseAnswers_RaqamlanganJavoblar_RAQAMBOYICHAJOYLASHISHIKERAK()
    {
        // XATO (OnlineTestBotService.cs:414-430): ParseAnswers savol raqamini umuman o'qimaydi,
        // faqat harflarni ketma-ket yig'adi. Natijada o'quvchi javoblarni tartibsiz yozsa
        // ("1a 2b 3c 5e 4d") javoblar SILJIYDI: kutilgan "ABCDE" o'rniga "ABCED" chiqadi va
        // 4-, 5- savollar noto'g'ri sanaladi. Xabar formati "1a 2b ..." ko'rinishida bo'lsa
        // raqam bo'yicha joylashtirish kerak edi.
        Assert.Equal("ABCDE", OnlineTestBotService.ParseAnswers("1a 2b 3c 5e 4d", 5));
    }

    /* =========================================================================================
     *  3) CountCorrect — avtomatik baholash
     * ========================================================================================= */

    [Fact]
    public void CountCorrect_HammaJavobTogri()
    {
        Assert.Equal(5, OnlineTestBotService.CountCorrect("ABCDA", "ABCDA"));
    }

    [Fact]
    public void CountCorrect_QismanTogri()
    {
        Assert.Equal(3, OnlineTestBotService.CountCorrect("ABCDA", "ABXDB"));
    }

    [Fact]
    public void CountCorrect_JavobsizSavol_XatoSanaladi()
    {
        // '-' (javobsiz) hech qachon ball bermaydi — kalitda ham '-' bo'lsa ham.
        Assert.Equal(2, OnlineTestBotService.CountCorrect("A-C--", "ABCDA"));
    }

    [Fact]
    public void CountCorrect_KalitdagiTire_HechKimgaBallBermaydi()
    {
        // Kalitning 2-savoli to'ldirilmagan ('-') — u savolga hech kim ball olmaydi.
        Assert.Equal(2, OnlineTestBotService.CountCorrect("ABC", "A-C"));
        Assert.Equal(2, OnlineTestBotService.CountCorrect("A-C", "A-C"));
    }

    [Fact]
    public void CountCorrect_KalitQisqaBolsa_MINuzunlikBoyichaSanaladi()
    {
        // Kalit 3 ta, javob 5 ta → faqat birinchi 3 tasi solishtiriladi (IndexOutOfRange bo'lmaydi).
        Assert.Equal(3, OnlineTestBotService.CountCorrect("ABCDA", "ABC"));
        // Teskarisi ham: javob kalitdan qisqa.
        Assert.Equal(2, OnlineTestBotService.CountCorrect("AB", "ABCDA"));
    }

    [Fact]
    public void CountCorrect_BoshSatrlar_Nol()
    {
        Assert.Equal(0, OnlineTestBotService.CountCorrect("", "ABCD"));
        Assert.Equal(0, OnlineTestBotService.CountCorrect("ABCD", ""));
    }

    [Fact]
    public void CountCorrect_KattaKichikHarf_FARQLI_ShuSababAvvalNormalizeShart()
    {
        // CountCorrect belgilarni AYNAN solishtiradi — kichik harfli javob 0 ball beradi.
        // Shuning uchun uni chaqirishdan oldin Normalize/ParseAnswers ISHLATILISHI shart.
        Assert.Equal(0, OnlineTestBotService.CountCorrect("abc", "ABC"));
        Assert.Equal(3, OnlineTestBotService.CountCorrect(
            OnlineTestService.Normalize("abc", 3, 4), "ABC"));
    }
}
