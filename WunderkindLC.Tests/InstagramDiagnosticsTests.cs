using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «META BILAN ALOQANI TEKSHIRISH» ning SOF QOIDALARI (<see cref="IgDiagnostics"/>).
///
/// <para><b>Nega aynan bu funksiyalar testlanadi:</b> tugmaning qiymati "yashil/qizil"da emas,
/// <b>MASLAHAT</b>da — admin uchta har xil token bilan ishlaydi (Instagram tokeni, Page tokeni,
/// reklama akkaunti tokeni) va noto'g'ri maslahat uni bir necha kunga noto'g'ri yo'lga
/// boshlardi. Tarmoq so'rovlari esa mavjud mijozlarniki, ular alohida testlangan.</para>
///
/// <para>⚠️ Matn bo'yicha tanish testlari ATAYIN mijozlarning HAQIQIY xato matnlari bilan
/// yozilgan (<c>MetaAdsApi</c> · <c>MetaInsightsApi</c> · <c>InstagramPublishApi</c> ·
/// <c>InstagramApi</c> dagi <c>MapError</c>). Matn — zaxira yo'l (Meta kodi faqat bitta
/// mijozda tashqariga chiqadi), shuning uchun u sinovsiz qolmasligi kerak.</para>
/// </summary>
public class InstagramDiagnosticsTests
{
    // ===================== 1) Modul kalitlari va yorliqlari =====================

    [Fact]
    public void All_besh_modulni_qamraydi_va_takrorlanmaydi()
    {
        Assert.Equal(5, IgDiagnostics.All.Length);
        Assert.Equal(IgDiagnostics.All.Length, IgDiagnostics.All.Distinct().Count());
        Assert.Contains(IgDiagnostics.KeyCapi, IgDiagnostics.All);
    }

    [Fact]
    public void Label_har_bir_kalit_uchun_ozbekcha_nom_beradi()
    {
        foreach (var key in IgDiagnostics.All)
        {
            var label = IgDiagnostics.Label(key);
            Assert.False(string.IsNullOrWhiteSpace(label));
            // Yorliq kalitning O'ZI bo'lib qolmasin (ya'ni xaritada haqiqatan bor).
            Assert.NotEqual(key, label);
        }
    }

    /// <summary>Noma'lum kalit JIMGINA yo'qolmaydi — kalitning o'zi qaytadi.</summary>
    [Fact]
    public void Label_notanish_kalitni_yashirmaydi()
        => Assert.Equal("yangiModul", IgDiagnostics.Label("yangiModul"));

    // ===================== 2) "Nima yetishmayapti" =====================

    [Fact]
    public void MissingText_hammasi_joyida_bolsa_bosh_qaytaradi()
        => Assert.Equal("", IgDiagnostics.MissingText(IgDiagnostics.KeyAdsStats, true, true));

    [Fact]
    public void MissingText_aynan_nima_tushib_qolganini_aytadi()
    {
        var noToken = IgDiagnostics.MissingText(IgDiagnostics.KeyAdLeads, hasId: true, hasToken: false);
        var noId = IgDiagnostics.MissingText(IgDiagnostics.KeyAdLeads, hasId: false, hasToken: true);
        var neither = IgDiagnostics.MissingText(IgDiagnostics.KeyAdLeads, hasId: false, hasToken: false);

        Assert.Contains("Page Access Token", noToken);
        Assert.DoesNotContain("Page ID", noToken);

        Assert.Contains("Page ID", noId);
        Assert.DoesNotContain("Page Access Token", noId);

        Assert.Contains("Page ID", neither);
        Assert.Contains("Page Access Token", neither);
    }

    /// <summary>Har modulda O'Z tokeni nomlanadi — "tokenni yangilang" degan umumiy matn
    /// adminni noto'g'ri joyga yuborardi.</summary>
    [Fact]
    public void MissingText_modullarda_har_xil_token_nomlanadi()
    {
        var ads = IgDiagnostics.MissingText(IgDiagnostics.KeyAdsStats, true, false);
        var leads = IgDiagnostics.MissingText(IgDiagnostics.KeyAdLeads, true, false);
        var capi = IgDiagnostics.MissingText(IgDiagnostics.KeyCapi, true, false);

        Assert.NotEqual(ads, leads);
        Assert.NotEqual(ads, capi);
        Assert.NotEqual(leads, capi);
    }

    [Fact]
    public void MissingHint_har_bir_modul_uchun_bor()
    {
        foreach (var key in IgDiagnostics.All)
            Assert.False(string.IsNullOrWhiteSpace(IgDiagnostics.MissingHint(key)));
    }

    // ===================== 3) Meta KODI bo'yicha tasnif =====================

    [Theory]
    [InlineData(190, IgDiagFault.Token)]
    [InlineData(10, IgDiagFault.Permission)]
    [InlineData(200, IgDiagFault.Permission)]
    [InlineData(299, IgDiagFault.Permission)]
    [InlineData(100, IgDiagFault.BadId)]
    [InlineData(4, IgDiagFault.RateLimit)]
    [InlineData(17, IgDiagFault.RateLimit)]
    [InlineData(32, IgDiagFault.RateLimit)]
    [InlineData(613, IgDiagFault.RateLimit)]
    [InlineData(80000, IgDiagFault.RateLimit)]
    [InlineData(80004, IgDiagFault.RateLimit)]
    public void Classify_meta_kodini_taniydi(int code, IgDiagFault expected)
        => Assert.Equal(expected, IgDiagnostics.Classify("istalgan matn", code));

    /// <summary>KOD — barqaror shartnoma, MATN esa yo'q: kod bo'lsa matn HISOBGA OLINMAYDI.</summary>
    [Fact]
    public void Classify_kod_matndan_ustun()
        => Assert.Equal(IgDiagFault.Token, IgDiagnostics.Classify("Tarmoq xatosi: uzildi", 190));

    // ===================== 4) MATN bo'yicha tasnif (zaxira yo'l) =====================

    [Theory]
    // MetaAdsApi
    [InlineData("Page Access Token muddati tugagan yoki bekor qilingan — Sozlamalar bo'limida yangisini kiriting.", IgDiagFault.Token)]
    [InlineData("Ruxsat yetishmaydi — ilovada `leads_retrieval` ruxsati va sahifa ustidan huquq borligini tekshiring.", IgDiagFault.Permission)]
    [InlineData("Meta so'rov chegarasi (rate limit) — keyinroq qayta urinamiz.", IgDiagFault.RateLimit)]
    [InlineData("Noto'g'ri so'rov parametri: Unsupported get request.", IgDiagFault.BadId)]
    [InlineData("Page ID bo'sh.", IgDiagFault.NotConfigured)]
    // MetaInsightsApi
    [InlineData("Meta tokeni yaroqsiz yoki muddati tugagan — Marketing → Sozlamalar bo'limida reklama akkaunti tokenini yangilang.", IgDiagFault.Token)]
    [InlineData("Reklama akkaunti ID noto'g'ri — u 'act_1234567890' ko'rinishida (yoki faqat raqamlar) bo'lishi kerak.", IgDiagFault.BadId)]
    [InlineData("Reklama akkaunti tokeni kiritilmagan — Marketing → Sozlamalar bo'limida saqlang.", IgDiagFault.NotConfigured)]
    [InlineData("Meta statistika so'rovlari chegarasiga yetildi — keyinroq avtomatik qayta urinamiz.", IgDiagFault.RateLimit)]
    // InstagramApi / InstagramPublishApi
    [InlineData("Token muddati tugagan yoki bekor qilingan — akkauntni qayta ulang.", IgDiagFault.Token)]
    [InlineData("Ruxsat yetishmaydi — akkauntni qayta ulab, kontent joylash ruxsatini bering (instagram_business_content_publish).", IgDiagFault.Permission)]
    // Transport (kod umuman yo'q)
    [InlineData("Tarmoq xatosi: No such host is known.", IgDiagFault.Network)]
    [InlineData("Meta javob bermadi (vaqt tugadi) — keyinroq qayta urinamiz.", IgDiagFault.Network)]
    [InlineData("So'rov bekor qilindi.", IgDiagFault.Network)]
    public void Classify_mijozlarning_haqiqiy_matnlarini_taniydi(string message, IgDiagFault expected)
        => Assert.Equal(expected, IgDiagnostics.Classify(message));

    /// <summary>
    /// ⚠️ "bekor qilingan" (token bekor qilingan) va "bekor qilindi" (so'rov uzildi) — deyarli
    /// bir xil so'zlar, lekin BUTUNLAY boshqa amal talab qiladi. Chalkashib ketmasin.
    /// </summary>
    [Fact]
    public void Classify_bekor_qilingan_va_bekor_qilindi_farqlanadi()
    {
        Assert.Equal(IgDiagFault.Token,
            IgDiagnostics.Classify("Token muddati tugagan yoki bekor qilingan — akkauntni qayta ulang."));
        Assert.Equal(IgDiagFault.Network, IgDiagnostics.Classify("So'rov bekor qilindi."));
    }

    /// <summary>Apostrof turi (klaviaturaga qarab `'` · `ʻ` · `’`) tasnifni buzmasligi kerak.</summary>
    [Fact]
    public void Classify_apostrof_turiga_bogliq_emas()
    {
        Assert.Equal(IgDiagFault.BadId, IgDiagnostics.Classify("Noto'g'ri so'rov parametri: x"));
        Assert.Equal(IgDiagFault.BadId, IgDiagnostics.Classify("Notoʻgʻri soʻrov parametri: x"));
        Assert.Equal(IgDiagFault.BadId, IgDiagnostics.Classify("Noto’g’ri so’rov parametri: x"));
    }

    [Fact]
    public void Classify_bosh_matn_Unknown()
    {
        Assert.Equal(IgDiagFault.Unknown, IgDiagnostics.Classify(""));
        Assert.Equal(IgDiagFault.Unknown, IgDiagnostics.Classify("   "));
        Assert.Equal(IgDiagFault.Unknown, IgDiagnostics.Classify(null!));
    }

    // ===================== 5) Maslahat ("nima qilish kerak") =====================

    /// <summary>Xato BOR — demak maslahat ham BO'LISHI SHART. Bo'sh maslahat modulning butun
    /// ma'nosini yo'q qiladi ("nosoz" deb yozib, nima qilishni aytmaslik).</summary>
    [Fact]
    public void Hint_har_bir_modul_va_har_bir_nosozlik_uchun_bor()
    {
        var faults = Enum.GetValues<IgDiagFault>().Where(f => f != IgDiagFault.None);
        foreach (var key in IgDiagnostics.All)
            foreach (var fault in faults)
                Assert.False(string.IsNullOrWhiteSpace(IgDiagnostics.Hint(key, fault)),
                    $"{key} / {fault} uchun maslahat yo'q");
    }

    [Fact]
    public void Hint_xato_bolmasa_bosh()
        => Assert.Equal("", IgDiagnostics.Hint(IgDiagnostics.KeyAccount, IgDiagFault.None));

    /// <summary>Token maslahati MODULGA xos: uchala token uch xil joydan kiritiladi.</summary>
    [Fact]
    public void Hint_token_maslahati_modulga_xos()
    {
        var account = IgDiagnostics.Hint(IgDiagnostics.KeyAccount, IgDiagFault.Token);
        var leads = IgDiagnostics.Hint(IgDiagnostics.KeyAdLeads, IgDiagFault.Token);
        var stats = IgDiagnostics.Hint(IgDiagnostics.KeyAdsStats, IgDiagFault.Token);

        Assert.NotEqual(account, leads);
        Assert.NotEqual(leads, stats);
        Assert.Contains("Page Access Token", leads);
        Assert.Contains("Reklama akkaunti tokeni", stats);
    }

    /// <summary>Ruxsat maslahatida AYNAN kerakli scope nomi turishi kerak.</summary>
    [Fact]
    public void Hint_ruxsat_maslahatida_kerakli_scope_nomi_bor()
    {
        Assert.Contains("leads_retrieval", IgDiagnostics.Hint(IgDiagnostics.KeyAdLeads, IgDiagFault.Permission));
        Assert.Contains("ads_read", IgDiagnostics.Hint(IgDiagnostics.KeyAdsStats, IgDiagFault.Permission));
        Assert.Contains("instagram_business_content_publish",
            IgDiagnostics.Hint(IgDiagnostics.KeyContent, IgDiagFault.Permission));
    }

    /// <summary>App Review — ruxsat xatosining eng ko'p uchraydigan "yashirin" sababi,
    /// shuning uchun matnda tilga olinadi.</summary>
    [Fact]
    public void Hint_ruxsat_maslahati_App_Review_ehtimolini_eslatadi()
    {
        Assert.Contains("App Review", IgDiagnostics.Hint(IgDiagnostics.KeyAdLeads, IgDiagFault.Permission));
        Assert.Contains("App Review", IgDiagnostics.Hint(IgDiagnostics.KeyContent, IgDiagFault.Permission));
    }

    /// <summary>Rate limit — VAQTINCHALIK: admin tokenni behuda qayta yozmasin.</summary>
    [Fact]
    public void Hint_rate_limit_vaqtinchalik_ekanini_aytadi()
        => Assert.Contains("VAQTINCHALIK",
            IgDiagnostics.Hint(IgDiagnostics.KeyAdsStats, IgDiagFault.RateLimit));

    [Fact]
    public void HintFor_matndan_togri_maslahatni_beradi()
    {
        var hint = IgDiagnostics.HintFor(IgDiagnostics.KeyAdsStats,
            "Meta tokeni yaroqsiz yoki muddati tugagan — Marketing → Sozlamalar bo'limida reklama akkaunti tokenini yangilang.");
        Assert.Equal(IgDiagnostics.Hint(IgDiagnostics.KeyAdsStats, IgDiagFault.Token), hint);
    }

    [Fact]
    public void HintFor_kod_berilsa_matndan_qatiy_nazar_kod_ishlaydi()
    {
        var hint = IgDiagnostics.HintFor(IgDiagnostics.KeyAdsStats, "tushunarsiz matn", 200);
        Assert.Equal(IgDiagnostics.Hint(IgDiagnostics.KeyAdsStats, IgDiagFault.Permission), hint);
    }

    // ===================== 6) CAPI ATAYIN SINALMAYDI =====================

    /// <summary>
    /// 🔴 CAPI matni "sinalmadi" ni OCHIQ aytishi SHART: sinalmagan modulni "ishlayapti" deb
    /// ko'rsatish eng yomon variant bo'lardi (admin ishonch bilan kutib qolardi).
    /// </summary>
    [Fact]
    public void Capi_matni_aloqa_sinalmaganini_ochiq_aytadi()
    {
        Assert.Contains("SINALMADI", IgDiagnostics.CapiNotProbedText.ToUpperInvariant());
        Assert.False(string.IsNullOrWhiteSpace(IgDiagnostics.CapiNotProbedHint));
    }

    [Fact]
    public void Ochirilgan_modul_matni_va_maslahati_bor()
    {
        Assert.False(string.IsNullOrWhiteSpace(IgDiagnostics.DisabledText));
        Assert.False(string.IsNullOrWhiteSpace(IgDiagnostics.DisabledHint));
    }
}
