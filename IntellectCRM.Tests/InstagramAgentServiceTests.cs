using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// INSTAGRAM AI QATLAMINING SOF QISMI (<see cref="InstagramAgentService"/>) testlari:
/// <c>ParseOutput</c> (model javobini o'qish) va <c>BuildSystemPrompt</c>/<c>BuildContext</c>
/// (prompt qurish). Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §10.
///
/// <para>⚠️ <c>AskAsync</c> TESTLANMAYDI: u <see cref="GeminiService"/> orqali HAQIQIY HTTP
/// so'rov qiladi va provayder interfeys ortiga olinmagan (loyihada mock kutubxonasi ham yo'q).
/// Testlanadigan qismlar ATAYIN ajratilgan — model javobini o'qish va prompt qurish shu yerda.
/// Yagona tekshiriladigan xulq: kalit sozlanmagan bo'lsa <c>AskAsync</c> tarmoqqa CHIQMAYDI
/// (quyidagi oxirgi test).</para>
/// </summary>
public class InstagramAgentServiceTests
{
    private const string Full = """
        {
          "reply": "Salom! IELTS kursimiz bor.",
          "language": "uz-Latn",
          "intent": "price_question",
          "lead_score": 75,
          "is_hot_lead": true,
          "move_to_dm": true,
          "escalate_to_human": false,
          "lead_name": "Ali",
          "lead_contact": "901234567",
          "lead_product_interest": "IELTS",
          "lead_summary": "Narx so'radi"
        }
        """;

    // ===================== 1) ParseOutput — to'g'ri formatlar =====================

    [Fact]
    public void Toza_JSON_toliq_oqiladi()
    {
        var o = InstagramAgentService.ParseOutput(Full);

        Assert.NotNull(o);
        Assert.Equal("Salom! IELTS kursimiz bor.", o!.Reply);
        Assert.Equal("uz-Latn", o.Language);
        Assert.Equal("price_question", o.Intent);
        Assert.Equal(75, o.LeadScore);
        Assert.True(o.IsHotLead);
        Assert.True(o.MoveToDm);
        Assert.False(o.EscalateToHuman);
        Assert.Equal("Ali", o.LeadName);
        Assert.Equal("901234567", o.LeadContact);
        Assert.Equal("IELTS", o.LeadProductInterest);
        Assert.Equal("Narx so'radi", o.LeadSummary);
    }

    [Fact]
    public void Markdown_fence_ichidagi_JSON_oqiladi()
    {
        // Gemini javobni ko'pincha ```json bloki bilan o'raydi.
        var o = InstagramAgentService.ParseOutput("```json\n" + Full + "\n```");
        Assert.Equal("price_question", o?.Intent);
    }

    [Fact]
    public void Tilsiz_fence_ham_oqiladi()
    {
        var o = InstagramAgentService.ParseOutput("```\n" + Full + "\n```");
        Assert.Equal(75, o?.LeadScore);
    }

    [Fact]
    public void JSON_atrofidagi_ortiqcha_matn_tashlanadi()
    {
        var o = InstagramAgentService.ParseOutput("Mana javobim:\n" + Full + "\nUmid qilamanki foydali bo'ldi.");
        Assert.Equal("Salom! IELTS kursimiz bor.", o?.Reply);
    }

    // ===================== 2) ParseOutput — buzuq javob =====================

    [Theory]
    [InlineData("{buzuq")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Kechirasiz, javob bera olmayman.")]   // JSON umuman yo'q
    [InlineData("}{")]                                  // qavslar teskari
    [InlineData("{\"reply\": }")]                       // qiymatsiz kalit
    public void Buzuq_javob_null_qaytaradi(string raw)
    {
        // ⚠️ `null` = pipeline JONLI JAVOB YUBORMAYDI. "Bir narsa yozib qo'yamiz" varianti YO'Q.
        Assert.Null(InstagramAgentService.ParseOutput(raw));
    }

    [Fact]
    public void Null_kirish_uchun_null()
    {
        Assert.Null(InstagramAgentService.ParseOutput(null!));
    }

    [Fact]
    public void Notogri_turdagi_maydon_butun_javobni_null_qiladi()
    {
        // `lead_score` satr sifatida kelsa deserializatsiya yiqiladi — istisno otilmaydi, null qaytadi.
        Assert.Null(InstagramAgentService.ParseOutput("{\"reply\":\"a\",\"lead_score\":\"ko'p\"}"));
    }

    // ===================== 3) ParseOutput — normalizatsiya va clamp =====================

    [Fact]
    public void Nomalum_intent_va_language_normalizatsiya_qilinadi()
    {
        var o = InstagramAgentService.ParseOutput(
            "{\"reply\":\"a\",\"intent\":\"price_inquiry\",\"language\":\"tr\"}");

        Assert.Equal("other", o?.Intent);
        Assert.Equal("uz-Latn", o?.Language);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(60, 60)]
    public void Diapazondan_tashqaridagi_ball_clamp_qilinadi(int given, int expected)
    {
        var o = InstagramAgentService.ParseOutput($"{{\"reply\":\"a\",\"lead_score\":{given}}}");
        Assert.Equal(expected, o?.LeadScore);
    }

    [Fact]
    public void Yetishmagan_maydonlar_bosh_qiymat_bilan_toldiriladi()
    {
        var o = InstagramAgentService.ParseOutput("{\"reply\":\"Salom\"}");

        Assert.NotNull(o);
        Assert.Equal("", o!.LeadContact);
        Assert.Equal("", o.LeadName);
        Assert.Equal("uz-Latn", o.Language);
        Assert.Equal("other", o.Intent);
        Assert.Equal(0, o.LeadScore);
    }

    [Fact]
    public void Juda_uzun_javob_Instagram_chegarasiga_qisqartiriladi()
    {
        // Meta: DM matni ≤1000 bayt. Qisqartirilmasa Instagram butun xabarni rad etardi.
        var uzun = new string('x', 3000);
        var o = InstagramAgentService.ParseOutput("{\"reply\":\"" + uzun + "\"}");

        Assert.NotNull(o);
        Assert.Equal(IgConst.MaxReplyLength, o!.Reply.Length);
    }

    [Fact]
    public void Bosh_reply_li_javob_ham_obyekt_qaytaradi()
    {
        // Bo'sh `reply` ni "yuborib bo'lmaydi" deb qaror qilish — AskAsync ning ishi.
        var o = InstagramAgentService.ParseOutput("{}");
        Assert.NotNull(o);
        Assert.Equal("", o!.Reply);
    }

    // ===================== 4) BuildSystemPrompt =====================

    [Fact]
    public void Prompt_bilim_bazasi_matnini_ichiga_oladi()
    {
        // ⚠️ Eng muhimi: AI narxni FAQAT shu matndan oladi.
        var prompt = InstagramAgentService.BuildSystemPrompt(
            "## Narxlar\nIELTS — 700 000 so'm/oy", "Intellect", "");

        Assert.Contains("IELTS — 700 000 so'm/oy", prompt);
        Assert.Contains("BILIM BAZASI", prompt);
    }

    [Fact]
    public void Prompt_markaz_nomini_ichiga_oladi()
    {
        Assert.Contains("«Intellect Group»", InstagramAgentService.BuildSystemPrompt("", "Intellect Group", ""));
    }

    [Fact]
    public void Markaz_nomi_bosh_bolsa_umumiy_nom_ishlatiladi()
    {
        Assert.Contains("«o'quv markazi»", InstagramAgentService.BuildSystemPrompt("", "   ", ""));
    }

    [Fact]
    public void Salomlashuv_bosh_bolsa_standart_bot_oshkorligi_qoyiladi()
    {
        // Meta talabi: bot ekanini oshkor qilish — sozlanmagan bo'lsa ham matn qoladi.
        Assert.Contains(IgConst.DefaultGreeting, InstagramAgentService.BuildSystemPrompt("", "Intellect", ""));
    }

    [Fact]
    public void Sozlangan_salomlashuv_promptga_tushadi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "Men botman 🤖");
        Assert.Contains("Men botman 🤖", prompt);
        Assert.DoesNotContain(IgConst.DefaultGreeting, prompt);
    }

    [Fact]
    public void Bosh_bilim_bazasida_taxmin_qilmaslik_ogohlantirishi_boladi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("   ", "Intellect", "");
        Assert.Contains("hech narsa o'ylab topma", prompt);
    }

    [Fact]
    public void Prompt_JSON_sxemasining_barcha_kalitlarini_sanaydi()
    {
        // Kalitlar `IgAgentOutput` bilan bir xil bo'lishi shart — aks holda ParseOutput bo'sh
        // qiymat oladi (NUR'dagi `price_inquiry`/`price_question` nomuvofiqligi kabi).
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        foreach (var key in new[]
                 {
                     "reply", "language", "intent", "lead_score", "is_hot_lead", "move_to_dm",
                     "escalate_to_human", "lead_name", "lead_contact", "lead_product_interest", "lead_summary",
                 })
            Assert.Contains($"\"{key}\"", prompt);
    }

    [Fact]
    public void Prompt_katalogdagi_barcha_niyat_va_tillarni_sanaydi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        foreach (var i in IgConst.Intents) Assert.Contains(i, prompt);
        foreach (var l in IgConst.Languages) Assert.Contains(l, prompt);
    }

    // ===================== 5) BuildContext =====================

    [Fact]
    public void Kontekst_kanalni_ajratadi()
    {
        Assert.Contains("ochiq IZOH", InstagramAgentService.BuildContext(
            IgConst.ChannelComment, "ali", "", "Narxi?", null));
        Assert.Contains("shaxsiy xabar (DM)", InstagramAgentService.BuildContext(
            IgConst.ChannelDm, "ali", "", "Narxi?", null));
    }

    [Fact]
    public void Kontekst_suhbat_tarixini_kim_yozganini_belgilab_qoyadi()
    {
        var history = new List<IgMessage>
        {
            new() { Direction = IgConst.DirIn, Text = "Salom" },
            new() { Direction = IgConst.DirOut, Text = "Assalomu alaykum!" },
        };

        var ctx = InstagramAgentService.BuildContext(IgConst.ChannelDm, "ali", "", "Narxi?", history);

        Assert.Contains("Mijoz: Salom", ctx);
        Assert.Contains("Biz: Assalomu alaykum!", ctx);
        Assert.EndsWith("Narxi?", ctx);
    }

    [Fact]
    public void Kontekst_post_matnini_qosha_oladi()
    {
        var ctx = InstagramAgentService.BuildContext(
            IgConst.ChannelComment, "ali", "Yangi IELTS guruhi ochildi", "Narxi?", null);
        Assert.Contains("Post matni: Yangi IELTS guruhi ochildi", ctx);
    }

    [Fact]
    public void Kontekst_tarixsiz_ham_ishlaydi()
    {
        var ctx = InstagramAgentService.BuildContext(IgConst.ChannelDm, "", "", "Salom", new List<IgMessage>());
        Assert.DoesNotContain("Suhbat tarixi", ctx);
    }

    // ===================== 6) AskAsync — sozlanmagan holatda tarmoqqa chiqmaydi =====================

    [Fact]
    public async Task Bosh_xabar_bilan_AI_umuman_chaqirilmaydi()
    {
        using var db = TestDb.Sqlite();
        var (ok, output, error) = await InstagramAgentService.AskAsync(
            db.Context, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            IgConst.ChannelDm, "ali", "", "   ", new List<IgMessage>());

        Assert.False(ok);
        Assert.Null(output);
        Assert.Contains("bo'sh", error);
    }

    [Fact]
    public async Task Bilim_bazasi_faol_boklardan_tartib_boyicha_yigiladi()
    {
        using var db = TestDb.Sqlite();
        db.Context.IgKnowledges.AddRange(
            new IgKnowledge { Title = "Ikkinchi", Content = "B matni", Order = 2, IsActive = true },
            new IgKnowledge { Title = "Birinchi", Content = "A matni", Order = 1, IsActive = true },
            new IgKnowledge { Title = "O'chirilgan", Content = "YO'Q BO'LSIN", Order = 0, IsActive = false });
        await db.Context.SaveChangesAsync();

        var kb = await InstagramAgentService.LoadKnowledgeAsync(db.Context);

        Assert.DoesNotContain("YO'Q BO'LSIN", kb);
        Assert.True(kb.IndexOf("A matni", StringComparison.Ordinal) <
                    kb.IndexOf("B matni", StringComparison.Ordinal));
    }

    // ===================== ISM VA RAQAM SO'RASH (7-qoida) =====================
    //
    // 🔴 Bu bo'lim ATAYIN qo'shildi: modulning ASOSIY maqsadi — qiziqqan odamni ismi va
    // telefoni bilan CRM'ga olib kelish, lekin prompt matnining shu qismi hech qanday test
    // bilan qulflanmagan edi. Ya'ni uni olib tashlasa bironta test qizarmasdi va agent
    // jimgina "shunchaki savolga javob beradigan" botga aylanib qolardi.

    /// <summary>Prompt mijozdan ISM va RAQAMni so'rashni ochiq talab qiladi.</summary>
    [Fact]
    public void Prompt_ism_va_raqam_sorashni_talab_qiladi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");

        Assert.Contains("ISM VA RAQAM SO'RA", prompt);
        // Tayyor jumla — model o'zi o'ylab topmasin, so'rash bir xil va tushunarli bo'lsin.
        Assert.Contains("Ismingiz va telefon raqamingizni qoldiring", prompt);
    }

    /// <summary>⚠️ IKKALASI BIRGA: faqat raqam bo'lsa operator kimga qo'ng'iroq qilayotganini
    /// bilmaydi, faqat ism bo'lsa umuman bog'lana olmaydi.</summary>
    [Fact]
    public void Prompt_ism_va_raqamni_BIRGA_sorashni_talab_qiladi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        Assert.Contains("BIRGA so'ra", prompt);
    }

    /// <summary>Allaqachon bergan mijozdan QAYTA so'ralmaydi — takror so'rash bezor qiladi
    /// va suhbatni tugatib yuboradi.</summary>
    [Fact]
    public void Prompt_kontakt_bergan_mijozdan_qayta_soramaslikni_aytadi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        Assert.Contains("QAYTA SO'RAMA", prompt);
    }

    /// <summary>
    /// 🔴 OCHIQ IZOHda raqam SO'RALMAYDI: u post ostida hamma ko'radigan joyga tushardi
    /// (mijoz uchun xavf), Instagram esa bunday izohlarni spam deb belgilaydi.
    /// Izohda vazifa — DM'ga taklif qilish.
    /// </summary>
    [Fact]
    public void Prompt_ochiq_izohda_raqam_soramaslikni_talab_qiladi()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        Assert.Contains("OCHIQ IZOHda raqam SO'RAMA", prompt);
    }

    /// <summary>Qoidalar raqamlanishi UZLUKSIZ (1..12) — tushib qolgan yoki takrorlangan raqam
    /// modelni chalg'itadi va yangi qoida qo'shilganda jimgina buziladi.</summary>
    [Fact]
    public void Prompt_qoidalari_uzluksiz_raqamlangan()
    {
        var prompt = InstagramAgentService.BuildSystemPrompt("", "Intellect", "");
        var rules = prompt[prompt.IndexOf("QOIDALAR:", StringComparison.Ordinal)..];

        for (var i = 1; i <= 12; i++)
            Assert.Contains($"\n{i}. ", rules);
        Assert.DoesNotContain("\n13. ", rules);
    }
}
