using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// AI BILAN CAPTION YOZISH (<see cref="InstagramCaptionService"/>) — SOF qismning testlari:
/// prompt qurish, model javobini o'qish va <b>chegaralarni qo'llash</b>.
///
/// <para>⚠️ <c>GenerateAsync</c> TESTLANMAYDI (u <see cref="GeminiService"/> orqali HAQIQIY
/// HTTP so'rov qiladi, provayder esa interfeys ortiga olinmagan — <c>InstagramAgentService</c>
/// testlaridagi bir xil chegara). Yagona tekshiriladigan xulq: mavzu bo'sh bo'lsa tarmoqqa
/// UMUMAN chiqilmaydi.</para>
///
/// <para>Eng muhim guruh — <c>Finalize</c>: aynan u foydalanuvchini «Matn juda uzun»
/// (Meta <c>2207010</c>) xatosidan saqlaydi.</para>
/// </summary>
public class InstagramCaptionTests
{
    // ===================== 1) Model javobini o'qish =====================

    [Fact]
    public void Toza_JSON_oqiladi()
    {
        var d = InstagramCaptionService.ParseDraft("""
            { "caption": "Yangi guruh ochildi", "hashtags": ["#ingliz", "#kurs"] }
            """);

        Assert.NotNull(d);
        Assert.Equal("Yangi guruh ochildi", d!.Caption);
        Assert.Equal(2, d.Hashtags.Count);
    }

    [Fact]
    public void Fence_va_ortiqcha_matn_tozalanadi()
    {
        var d = InstagramCaptionService.ParseDraft("""
            Mana natija:
            ```json
            { "caption": "Salom", "hashtags": [] }
            ```
            Umid qilamanki yoqadi.
            """);

        Assert.NotNull(d);
        Assert.Equal("Salom", d!.Caption);
        Assert.Empty(d.Hashtags);
    }

    [Fact]
    public void Buzuq_javob_null_qaytaradi()
    {
        Assert.Null(InstagramCaptionService.ParseDraft("hech qanday JSON yo'q"));
        Assert.Null(InstagramCaptionService.ParseDraft(""));
        Assert.Null(InstagramCaptionService.ParseDraft("{ \"caption\": "));
    }

    [Fact]
    public void Hashtags_maydonisiz_javob_ham_oqiladi()
    {
        var d = InstagramCaptionService.ParseDraft("{ \"caption\": \"Matn\" }");
        Assert.NotNull(d);
        Assert.Empty(d!.Hashtags);
    }

    // ===================== 2) Hashtagni tozalash =====================

    [Theory]
    [InlineData("#kurs", "#kurs")]
    [InlineData("kurs", "#kurs")]
    [InlineData("  #Ingliz_tili  ", "#Ingliz_tili")]
    [InlineData("ingliz tili", "#ingliztili")]   // bo'sh joy tashlanadi — teg ichida bo'lmaydi
    [InlineData("##kurs", "#kurs")]
    [InlineData("#kurs2026", "#kurs2026")]
    public void Hashtag_normallashadi(string raw, string expected)
        => Assert.Equal(expected, InstagramCaptionService.NormalizeHashtag(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("###")]
    [InlineData("!!!")]
    [InlineData("___")]   // harf/raqam yo'q — `CountHashtags` uni teg deb sanamaydi
    public void Yaroqsiz_hashtag_tashlanadi(string raw)
        => Assert.Equal("", InstagramCaptionService.NormalizeHashtag(raw));

    [Fact]
    public void Juda_uzun_hashtag_qisqartiriladi()
    {
        var tag = InstagramCaptionService.NormalizeHashtag(new string('a', 500));
        Assert.True(tag.Length <= InstagramCaptionService.MaxHashtagLength);
        Assert.StartsWith("#", tag);
    }

    // ===================== 3) CHEGARALAR (eng muhim guruh) =====================

    [Fact]
    public void Hashtaglar_matn_oxiriga_qoshiladi()
    {
        var (ok, caption, tags, error) = InstagramCaptionService.Finalize(
            "Yangi guruh ochildi", new[] { "#ingliz", "#kurs" });

        Assert.True(ok, error);
        Assert.StartsWith("Yangi guruh ochildi", caption);
        Assert.EndsWith("#ingliz #kurs", caption);
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public void Takror_va_matnda_bor_teglar_qoshilmaydi()
    {
        var (ok, caption, tags, _) = InstagramCaptionService.Finalize(
            "Yangi #ingliz guruhi", new[] { "#ingliz", "#INGLIZ", "#kurs" });

        Assert.True(ok);
        Assert.Single(tags);
        Assert.Equal("#kurs", tags[0]);
        // Matndagi teg qayta yozilmagan: jami sanoq 2 (biri matnda, biri oxirida).
        Assert.Equal(2, InstagramPublishContract.CountHashtags(caption));
    }

    [Fact]
    public void Uzunroq_teg_borligi_qisqasini_tosib_qolmaydi()
    {
        // "#inglizcha" matnda bor, lekin "#ingliz" — BOSHQA teg va qo'shilishi kerak.
        var (ok, _, tags, _) = InstagramCaptionService.Finalize(
            "Bizda #inglizcha guruh bor", new[] { "#ingliz" });

        Assert.True(ok);
        Assert.Contains("#ingliz", tags);
    }

    [Fact]
    public void Hashtag_chegarasidan_oshgani_qirqiladi()
    {
        var many = Enumerable.Range(1, 50).Select(i => $"#teg{i}").ToArray();

        var (ok, caption, tags, error) = InstagramCaptionService.Finalize("Matn", many);

        Assert.True(ok, error);
        Assert.Equal(IgPublishConst.MaxHashtags, tags.Count);
        Assert.True(InstagramPublishContract.CountHashtags(caption) <= IgPublishConst.MaxHashtags);
    }

    [Fact]
    public void Matndagi_teglar_ham_chegaraga_kiradi()
    {
        // Matnning O'ZIDA 28 ta teg bor → oxiriga faqat 2 tasi qo'shiladi.
        var body = string.Join(' ', Enumerable.Range(1, 28).Select(i => $"#ichki{i}"));
        var extra = Enumerable.Range(1, 10).Select(i => $"#tashqi{i}").ToArray();

        var (ok, caption, tags, error) = InstagramCaptionService.Finalize(body, extra);

        Assert.True(ok, error);
        Assert.Equal(2, tags.Count);
        Assert.Equal(IgPublishConst.MaxHashtags, InstagramPublishContract.CountHashtags(caption));
    }

    [Fact]
    public void Uzun_matn_chegaraga_sigadi()
    {
        var body = string.Join(' ', Enumerable.Repeat("juda uzun matn", 400));   // ≈5600 belgi

        var (ok, caption, _, error) = InstagramCaptionService.Finalize(body, new[] { "#kurs" });

        Assert.True(ok, error);
        Assert.True(caption.Length <= IgPublishConst.MaxCaptionLength, $"uzunlik {caption.Length}");
        // Qirqilgani KO'RINIB tursin.
        Assert.Contains("…", caption);
        // Natija saqlashdagi AYNAN o'sha tekshiruvdan o'tadi.
        Assert.True(InstagramPublishContract.ValidateCaption(caption).Ok);
    }

    [Fact]
    public void Uzunlik_oshsa_avval_hashtaglar_qirqiladi()
    {
        // Matn chegaraga deyarli to'la → hashtaglarga joy qolmaydi, lekin MATN saqlanadi.
        var body = new string('a', IgPublishConst.MaxCaptionLength - 5);
        var many = Enumerable.Range(1, 20).Select(i => $"#teg{i}").ToArray();

        var (ok, caption, tags, error) = InstagramCaptionService.Finalize(body, many);

        Assert.True(ok, error);
        Assert.Empty(tags);
        Assert.Equal(body, caption);   // matn TEGILMAGAN
    }

    [Fact]
    public void Mention_chegarasidan_oshgan_matn_rad_etiladi()
    {
        var body = string.Join(' ', Enumerable.Range(1, 25).Select(i => $"@user{i}"));

        var (ok, caption, _, error) = InstagramCaptionService.Finalize(body, null);

        Assert.False(ok);
        Assert.Equal("", caption);
        Assert.Contains("mention", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bosh_natija_xato_beradi()
    {
        var (ok, _, _, error) = InstagramCaptionService.Finalize("   ", Array.Empty<string>());
        Assert.False(ok);
        Assert.NotEqual("", error);
    }

    [Fact]
    public void Natija_har_doim_ValidateCaption_dan_otadi()
    {
        var (ok, caption, _, _) = InstagramCaptionService.Finalize(
            "Matn " + new string('b', 3000),
            Enumerable.Range(1, 40).Select(i => $"#teg{i}").ToArray());

        Assert.True(ok);
        var (valid, err) = InstagramPublishContract.ValidateCaption(caption);
        Assert.True(valid, err);
    }

    // ===================== 4) So'z chegarasida qisqartirish =====================

    [Fact]
    public void Qisqartirish_soz_chegarasida_boladi()
    {
        var s = InstagramCaptionService.TrimToWord("bir ikki uch tort besh", 12);
        Assert.True(s.Length <= 12);
        Assert.EndsWith("…", s);
        // O'rtadan kesilmagan: oxirgi to'liq so'zdan keyin tugaydi.
        Assert.DoesNotContain("tor…", s);
    }

    [Fact]
    public void Qisqa_matn_tegilmaydi()
        => Assert.Equal("qisqa", InstagramCaptionService.TrimToWord("qisqa", 50));

    // ===================== 5) Uslub =====================

    [Theory]
    [InlineData("expert", InstagramCaptionService.ToneExpert)]
    [InlineData("SALES", InstagramCaptionService.ToneSales)]
    [InlineData("  energetic  ", InstagramCaptionService.ToneEnergetic)]
    [InlineData("yoq-bunday-uslub", InstagramCaptionService.DefaultTone)]
    [InlineData("", InstagramCaptionService.DefaultTone)]
    [InlineData(null, InstagramCaptionService.DefaultTone)]
    public void Uslub_normallashadi(string? raw, string expected)
        => Assert.Equal(expected, InstagramCaptionService.NormalizeTone(raw));

    // ===================== 6) Prompt =====================

    [Fact]
    public void Prompt_bilim_bazasi_mavzu_va_chegaralarni_oz_ichiga_oladi()
    {
        var p = InstagramCaptionService.BuildPrompt(
            "## Narxlar\nIELTS — 500 000 so'm", "Wunderkind", IgPublishConst.TypeReels,
            "yozgi chegirma", "uz-Latn", InstagramCaptionService.ToneSales);

        Assert.Contains("Wunderkind", p);
        Assert.Contains("IELTS — 500 000 so'm", p);
        Assert.Contains("yozgi chegirma", p);
        Assert.Contains(InstagramCaptionService.WantedHashtags.ToString(), p);
        Assert.Contains(InstagramCaptionService.TargetCaptionLength.ToString(), p);
        // Mention TAQIQ — chegara oshib ketmasin va begona akkaunt teglanmasin.
        Assert.Contains("@mention", p);
        // Post turiga xos ko'rsatma.
        Assert.Contains("Reels", p);
    }

    [Fact]
    public void Bosh_bilim_bazasi_promptda_ochiq_aytiladi()
    {
        var p = InstagramCaptionService.BuildPrompt("", "", IgPublishConst.TypeImage, "mavzu", "uz-Latn", "friendly");
        Assert.Contains("to'ldirilmagan", p);
        Assert.Contains("o'quv markazi", p);
    }

    [Fact]
    public void Til_kalitiga_qarab_korsatma_ozgaradi()
    {
        Assert.Contains("КИРИЛЛ", InstagramCaptionService.LanguageName("uz-Cyrl"));
        Assert.Contains("rus", InstagramCaptionService.LanguageName("ru"));
        Assert.Contains("LOTIN", InstagramCaptionService.LanguageName("noma'lum"));
    }

    // ===================== 7) Bo'sh mavzuda tarmoqqa chiqilmaydi =====================

    [Fact]
    public async Task Bosh_mavzu_bilan_AI_umuman_chaqirilmaydi()
    {
        using var db = TestDb.Sqlite();

        var (ok, caption, tags, error) = await InstagramCaptionService.GenerateAsync(
            db.Context, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            IgPublishConst.TypeImage, "   ", null, null);

        Assert.False(ok);
        Assert.Equal("", caption);
        Assert.Empty(tags);
        Assert.Contains("Mavzu", error);
    }
}
