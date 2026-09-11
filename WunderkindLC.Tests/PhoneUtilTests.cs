using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="PhoneUtil"/> — telefon raqamlarini solishtirish/normallashtirish. Bu sof
/// yordamchi bo'lsa-da, tizimda MUHIM rol o'ynaydi: Telegram bot yuborilgan raqamni
/// <see cref="PhoneUtil.Key"/> bo'yicha profilga bog'laydi, lid dublikatlari shu kalit
/// bo'yicha topiladi va SMS shu normal ko'rinishga tayanadi.
/// </summary>
public class PhoneUtilTests
{
    /* =========================================================================================
     *  DigitsOnly
     * ========================================================================================= */

    [Theory]
    [InlineData("+998 90 123 45 67", "998901234567")]
    [InlineData("(90) 123-45-67", "901234567")]
    [InlineData("Tel: 90 123 45 67 (uy)", "901234567")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("abc", "")]
    public void DigitsOnly_FaqatRaqamlarQoladi(string? input, string kutilgan)
    {
        Assert.Equal(kutilgan, PhoneUtil.DigitsOnly(input));
    }

    /* =========================================================================================
     *  Key — solishtirish kaliti (oxirgi 9 raqam)
     * ========================================================================================= */

    [Theory]
    [InlineData("901234567")]
    [InlineData("+998 90 123 45 67")]
    [InlineData("998901234567")]
    [InlineData("+998901234567")]
    [InlineData("8-998-90-123-45-67")] // oldida ortiqcha raqamlar bo'lsa ham — oxirgi 9 tasi
    public void Key_TurliFormatlar_BirXilKalit(string phone)
    {
        Assert.Equal("901234567", PhoneUtil.Key(phone));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("1234567", "1234567")] // 9 tadan kam — borini qaytaradi
    public void Key_QisqaYokiBosh(string? phone, string kutilgan)
    {
        Assert.Equal(kutilgan, PhoneUtil.Key(phone));
    }

    /* =========================================================================================
     *  Normalize
     * ========================================================================================= */

    [Theory]
    [InlineData("901234567")]
    [InlineData("+998 90 123 45 67")]
    [InlineData("998901234567")]
    [InlineData("+998901234567")]
    [InlineData("  (90) 123-45-67  ")]
    public void Normalize_TurliFormatlar_BirXilNatija(string phone)
    {
        Assert.Equal("+998-90-123-45-67", PhoneUtil.Normalize(phone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BoshQiymat_BoshSatr(string? phone)
    {
        Assert.Equal("", PhoneUtil.Normalize(phone));
    }

    [Fact]
    public void Normalize_NotogriUzunlik_AslQiymatQaytadi()
    {
        // 12 ta raqamga tushmagan qiymat o'zgarishsiz (trim qilingan holda) qaytadi —
        // ma'lumot yo'qolmasin, admin xatoni ko'rsin.
        Assert.Equal("90123", PhoneUtil.Normalize("  90123  "));
        Assert.Equal("79161234567", PhoneUtil.Normalize("79161234567")); // O'zbekiston raqami emas
    }

    /* =========================================================================================
     *  Validate
     * ========================================================================================= */

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Bosh_Xato(string? phone)
    {
        var (valid, normalized, error) = PhoneUtil.Validate(phone);

        Assert.False(valid);
        Assert.Equal("", normalized);
        Assert.Equal("Telefon raqami bo'sh", error);
    }

    [Theory]
    [InlineData("901234567")]
    [InlineData("+998 90 123 45 67")]
    [InlineData("998901234567")]
    public void Validate_TogriRaqam_Valid(string phone)
    {
        var (valid, normalized, error) = PhoneUtil.Validate(phone);

        Assert.True(valid);
        Assert.Equal("+998-90-123-45-67", normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("90-123")]
    [InlineData("+998 90")]
    public void Validate_JudaQisqa_Xato(string phone)
    {
        var (valid, _, error) = PhoneUtil.Validate(phone);

        Assert.False(valid);
        Assert.Equal("Telefon raqami juda qisqa (kamida 7 ta raqam)", error);
    }

    [Theory]
    [InlineData("telefon yo'q")]
    [InlineData("---")]
    public void Validate_RaqamsizMatn_Xato(string phone)
    {
        // Raqam umuman bo'lmasa kalit bo'sh chiqadi — "juda qisqa" xatosiga tushadi.
        var (valid, _, error) = PhoneUtil.Validate(phone);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_NotogriFormat_Xato()
    {
        // 7-11 ta raqamli, 998 ga aloqasi yo'q qiymat: normallasha olmaydi → format xatosi.
        var (valid, normalized, error) = PhoneUtil.Validate("12345678");

        Assert.False(valid);
        Assert.Equal("12345678", normalized);
        Assert.Equal("Noto'g'ri telefon raqami formati", error);
    }

    /* =========================================================================================
     *  MA'LUM XATOLAR — Skip bilan hujjatlashtirilgan (kutilgan TO'G'RI xulq yozilgan)
     * ========================================================================================= */

    [Theory(Skip = "XATO (PhoneUtil.cs:69-74): matnda ISTALGAN joyda \"998\" uchrasa format tekshiruvi "
                   + "butunlay o'tkazib yuboriladi — chala raqam Valid=true bo'lib o'tib ketadi. "
                   + "Tuzatish: `!phone.Contains(\"998\")` o'rniga normalize natijasi \"+998-\" bilan "
                   + "boshlanishini tekshirish kerak")]
    [InlineData("12345998")]
    [InlineData("998998998")]
    [InlineData("00998000")]
    public void Validate_MatndaSoxta998_XatoBerishiKerak(string phone)
    {
        // KUTILGAN: bu qiymatlar haqiqiy raqam emas → Valid=false.
        // HOZIRGI: Normalize uni normallashtira olmaydi (asl qiymatni qaytaradi), lekin
        // `!phone.Contains("998")` sharti FALSE bo'lgani uchun xato bloki o'tkazib yuboriladi
        // va funksiya (true, "<xom qiymat>", null) qaytaradi.
        var (valid, _, _) = PhoneUtil.Validate(phone);

        Assert.False(valid);
    }

    [Fact(Skip = "XATO (PhoneUtil.cs:41-46): UZ \"99\" operatori mamlakat kodisiz kiritilsa "
                 + "(998123456 = 99-812-34-56) prefiks qo'shilmaydi — \"998\" bilan boshlangani "
                 + "uchun kod uni allaqachon mamlakat kodli deb hisoblaydi va xom qaytaradi. "
                 + "Tuzatish: 998 prefiksini faqat digits.Length==12 bo'lganda mamlakat kodi deb bilish")]
    public void Normalize_Operator99_MamlakatKodisiz_NormallashishiKerak()
    {
        // KUTILGAN: "998123456" — bu 99 operatorining 8-12-34-56 raqami → "+998-99-812-34-56".
        // HOZIRGI: digits "998" bilan boshlanadi deb qaraladi, uzunligi 9 (12 emas) → xom qaytadi.
        Assert.Equal("+998-99-812-34-56", PhoneUtil.Normalize("998123456"));
    }

    [Fact]
    public void Normalize_Operator99_HozirgiXulq_XomQaytadi()
    {
        // Yuqoridagi Skip test tuzatilgunga qadar HOZIRGI xulqni qayd etib qo'yamiz —
        // regressiya bo'lsa (masalan kutilmaganda boshqacha satr chiqsa) darrov ko'rinadi.
        Assert.Equal("998123456", PhoneUtil.Normalize("998123456"));
    }
}
