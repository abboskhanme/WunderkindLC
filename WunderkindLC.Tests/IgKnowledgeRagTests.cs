using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// E6.5 — BILIM BAZASI RAG'ining SOF qismi (<see cref="IgKnowledgeRag"/>).
///
/// <para>⚠️ <c>IgEmbeddingService</c> TESTLANMAYDI: u Gemini'ga HAQIQIY HTTP so'rov qiladi va
/// provayder interfeys ortiga olinmagan (loyihada mock kutubxonasi yo'q) —
/// <c>InstagramAgentServiceTests</c> dagi bilan bir xil chegara. Testlanadigan hamma narsa
/// ATAYIN sof funksiyalarga ajratilgan.</para>
///
/// <para>Eng muhim qulf: <b>RAG yiqilsa modul ISHLASHDA DAVOM ETADI</b> — bo'sh, buzuq yoki
/// turli o'lchamdagi vektor istisno OTMAYDI, shunchaki bo'lak tanlovga kirmaydi.</para>
/// </summary>
public class IgKnowledgeRagTests
{
    private static IgRagChunk Chunk(string id, int order, float[] v, string title = "", string content = "") =>
        new(id, title.Length > 0 ? title : "S" + id, content.Length > 0 ? content : "M" + id, order, v);

    // ===================== 1) Vektor ↔ JSON =====================

    [Fact]
    public void Vektor_JSONga_va_qaytib_oqiladi()
    {
        var json = IgKnowledgeRag.SerializeVector(new[] { 0.5f, -0.25f, 1f });
        var back = IgKnowledgeRag.ParseVector(json);

        Assert.Equal(3, back.Length);
        Assert.Equal(0.5f, back[0]);
        Assert.Equal(-0.25f, back[1]);
        Assert.Equal(1f, back[2]);
    }

    [Fact]
    public void Bosh_vektor_bosh_satr_boladi()
    {
        Assert.Equal("", IgKnowledgeRag.SerializeVector(null));
        Assert.Equal("", IgKnowledgeRag.SerializeVector(Array.Empty<float>()));
    }

    [Fact]
    public void Onlik_ajratgich_HAR_DOIM_nuqta()
    {
        // Server mintaqasi vergulli o'nlik ishlatsa JSON buzilardi ("0,5" — ikkita son).
        var json = IgKnowledgeRag.SerializeVector(new[] { 0.5f });
        Assert.Contains(".", json, StringComparison.Ordinal);
        Assert.DoesNotContain(",", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("buzuq")]
    [InlineData("{\"a\":1}")]
    [InlineData("[")]
    [InlineData("[1,\"salom\"]")]
    [InlineData("[]")]
    public void Buzuq_JSON_istisno_otmaydi(string raw)
    {
        Assert.Empty(IgKnowledgeRag.ParseVector(raw));
    }

    [Fact]
    public void Juda_katta_vektor_qabul_qilinmaydi()
    {
        var big = "[" + string.Join(",", Enumerable.Repeat("0.1", IgKnowledgeRag.MaxDims + 1)) + "]";
        Assert.Empty(IgKnowledgeRag.ParseVector(big));
    }

    // ===================== 2) Kosinus =====================

    [Fact]
    public void Ayni_vektor_bilan_oxshashlik_bir()
    {
        var v = new[] { 1f, 2f, 3f };
        Assert.Equal(1.0, IgKnowledgeRag.Cosine(v, v), 5);
    }

    [Fact]
    public void Perpendikulyar_vektorlar_nol()
    {
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(new[] { 1f, 0f }, new[] { 0f, 1f }), 5);
    }

    [Fact]
    public void Yonalishi_bir_xil_uzunligi_boshqa_vektor_ham_bir()
    {
        // Kosinus UZUNLIKKA bog'liq emas — faqat yo'nalishga.
        Assert.Equal(1.0, IgKnowledgeRag.Cosine(new[] { 1f, 1f }, new[] { 5f, 5f }), 5);
    }

    [Fact]
    public void Turli_olchamdagi_vektor_YIQILMAYDI()
    {
        // Model almashganda shunday bo'ladi — istisno emas, 0 (bo'lak jimgina tanlovga kirmaydi).
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(new[] { 1f, 2f }, new[] { 1f, 2f, 3f }));
    }

    [Fact]
    public void Bosh_va_null_vektor_YIQILMAYDI()
    {
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(null, new[] { 1f }));
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(new[] { 1f }, null));
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(Array.Empty<float>(), Array.Empty<float>()));
    }

    [Fact]
    public void Nol_vektor_bolinish_xatosi_bermaydi()
    {
        Assert.Equal(0.0, IgKnowledgeRag.Cosine(new[] { 0f, 0f }, new[] { 1f, 1f }));
    }

    // ===================== 3) Top-N tanlash =====================

    [Fact]
    public void Eng_yaqin_boklar_tartib_bilan_tanlanadi()
    {
        var chunks = new[]
        {
            Chunk("a", 0, new[] { 1f, 0f }),      // savolga AYNAN mos
            Chunk("b", 1, new[] { 0.9f, 0.1f }),  // yaqin
            Chunk("c", 2, new[] { 0f, 1f }),      // aloqasiz
        };

        var top = IgKnowledgeRag.TopMatches(chunks, new[] { 1f, 0f }, topN: 2);

        Assert.Equal(2, top.Count);
        Assert.Equal("a", top[0].Id);
        Assert.Equal("b", top[1].Id);
    }

    [Fact]
    public void Chegaradan_otmagan_bok_tanlanmaydi()
    {
        var chunks = new[] { Chunk("c", 0, new[] { 0f, 1f }) };
        Assert.Empty(IgKnowledgeRag.TopMatches(chunks, new[] { 1f, 0f }));
    }

    [Fact]
    public void Mos_bok_umuman_yoq_bolsa_bosh_royxat_qaytadi()
    {
        // Chaqiruvchi uchun bu "zaxira yo'lga o't" signali.
        var chunks = new[] { Chunk("a", 0, Array.Empty<float>()) };
        Assert.Empty(IgKnowledgeRag.TopMatches(chunks, new[] { 1f, 0f }));
        Assert.Empty(IgKnowledgeRag.TopMatches(chunks, Array.Empty<float>()));
        Assert.Empty(IgKnowledgeRag.TopMatches(null, new[] { 1f }));
    }

    [Fact]
    public void Teng_ballda_tartib_BARQAROR()
    {
        var chunks = new[]
        {
            Chunk("z", 5, new[] { 1f, 0f }),
            Chunk("a", 1, new[] { 1f, 0f }),
        };

        var top = IgKnowledgeRag.TopMatches(chunks, new[] { 1f, 0f }, topN: 2);

        Assert.Equal("a", top[0].Id);   // Order kichigi oldinda
        Assert.Equal("z", top[1].Id);
    }

    // ===================== 4) RAG'ni ishlatish sharti =====================

    [Fact]
    public void Kichik_bilim_bazasida_RAG_ishlatilmaydi()
    {
        // TopN tadan kam bo'lak bo'lsa hammasini yuborish ham arzon, ham xatosiz.
        var few = Enumerable.Range(0, IgKnowledgeRag.TopN)
            .Select(i => Chunk(i.ToString(), i, new[] { 1f, 0f })).ToList();

        Assert.False(IgKnowledgeRag.CanUseRag(few));
        Assert.False(IgKnowledgeRag.CanUseRag(null));
    }

    [Fact]
    public void Bitta_bok_embedding_qilinmagan_bolsa_RAG_ISHLATILMAYDI()
    {
        // ⚠️ Aynan o'sha yangi bo'lak savolga javob bo'lishi mumkin — yarim tayyor bazada
        // RAG uni JIMGINA tashlab ketardi.
        var chunks = Enumerable.Range(0, IgKnowledgeRag.TopN + 2)
            .Select(i => Chunk(i.ToString(), i, i == 3 ? Array.Empty<float>() : new[] { 1f, 0f })).ToList();

        Assert.False(IgKnowledgeRag.CanUseRag(chunks));
    }

    [Fact]
    public void Toliq_tayyor_va_katta_bazada_RAG_ishlaydi()
    {
        var chunks = Enumerable.Range(0, IgKnowledgeRag.TopN + 1)
            .Select(i => Chunk(i.ToString(), i, new[] { 1f, 0f })).ToList();

        Assert.True(IgKnowledgeRag.CanUseRag(chunks));
    }

    // ===================== 5) Promptga yig'ish =====================

    [Fact]
    public void Compose_eski_formatni_saqlaydi()
    {
        var text = IgKnowledgeRag.Compose(
            new[] { Chunk("a", 0, [], "Narxlar", "IELTS 500 000") }, IgConst.KnowledgeLimit);

        Assert.Contains("## Narxlar", text, StringComparison.Ordinal);
        Assert.Contains("IELTS 500 000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_Order_boyicha_tartiblaydi()
    {
        var text = IgKnowledgeRag.Compose(
            new[]
            {
                Chunk("b", 2, [], "Ikkinchi", "B matni"),
                Chunk("a", 1, [], "Birinchi", "A matni"),
            }, IgConst.KnowledgeLimit);

        Assert.True(text.IndexOf("A matni", StringComparison.Ordinal)
                    < text.IndexOf("B matni", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_chegaradan_oshmaydi()
    {
        var chunks = Enumerable.Range(0, 50)
            .Select(i => Chunk(i.ToString(), i, [], "S" + i, new string('x', 100))).ToList();

        var text = IgKnowledgeRag.Compose(chunks, 500);

        Assert.True(text.Length <= 500);
    }

    [Fact]
    public void Compose_bosh_royxatda_yiqilmaydi()
    {
        Assert.Equal("", IgKnowledgeRag.Compose(null, IgConst.KnowledgeLimit));
        Assert.Equal("", IgKnowledgeRag.Compose([], IgConst.KnowledgeLimit));
    }

    // ===================== 6) Qayta hisoblash qoidasi =====================

    [Fact]
    public void Vektor_yoq_bolsa_hisoblash_kerak()
    {
        Assert.True(IgKnowledgeRag.NeedsEmbedding("", "h", "m", "S", "M", "m"));
    }

    [Fact]
    public void Matn_ozgarsa_qayta_hisoblanadi()
    {
        var json = IgKnowledgeRag.SerializeVector(new[] { 1f });
        var hash = IgKnowledgeRag.ContentHash("S", "Eski matn");

        Assert.False(IgKnowledgeRag.NeedsEmbedding(json, hash, "m", "S", "Eski matn", "m"));
        Assert.True(IgKnowledgeRag.NeedsEmbedding(json, hash, "m", "S", "Yangi matn", "m"));
    }

    [Fact]
    public void Model_almashsa_qayta_hisoblanadi()
    {
        var json = IgKnowledgeRag.SerializeVector(new[] { 1f });
        var hash = IgKnowledgeRag.ContentHash("S", "M");

        Assert.True(IgKnowledgeRag.NeedsEmbedding(json, hash, "eski-model", "S", "M", "yangi-model"));
    }

    [Fact]
    public void Buzuq_vektor_qayta_hisoblanadi()
    {
        var hash = IgKnowledgeRag.ContentHash("S", "M");
        Assert.True(IgKnowledgeRag.NeedsEmbedding("[buzuq", hash, "m", "S", "M", "m"));
    }

    [Fact]
    public void Sarlavha_ham_hashga_kiradi()
    {
        Assert.NotEqual(IgKnowledgeRag.ContentHash("A", "M"), IgKnowledgeRag.ContentHash("B", "M"));
    }

    // ===================== 7) Qidiruv so'rovi =====================

    [Fact]
    public void Qidiruv_sorovida_XABAR_oldinda_turadi()
    {
        var q = IgKnowledgeRag.QueryText("Narxi qancha?", "Yangi guruhga qabul boshlandi");

        Assert.StartsWith("Narxi qancha?", q, StringComparison.Ordinal);
        Assert.Contains("qabul", q, StringComparison.Ordinal);
    }

    [Fact]
    public void Qidiruv_sorovi_bosh_bolsa_bosh_qaytadi()
    {
        Assert.Equal("", IgKnowledgeRag.QueryText(null, null));
        Assert.Equal("", IgKnowledgeRag.QueryText("   ", ""));
    }
}

/// <summary>
/// E6.6 — JAVOB SIFATI JURNALINING sof qismi (<see cref="IgQualityLog"/>): matnlarni
/// solishtirish va "bu AI taklifi edimi" qarori.
///
/// <para>Eng muhim qulf: <b>yolg'on "tahrirlandi" bo'lmasin</b> — bosh harf, ortiqcha bo'shliq
/// yoki boshqa klaviaturadagi apostrof tahrir hisoblanmaydi; operatorning ketma-ket ikkinchi
/// xabari esa umuman taklif ustiga yozilgan javob emas.</para>
/// </summary>
public class IgQualityLogTests
{
    private static IgMessage Out(bool isAi, string text, string createdAt, string dir = "out") =>
        new() { Direction = dir, IsAi = isAi, Text = text, CreatedAt = createdAt };

    // ===================== 1) Normallashtirish =====================

    [Fact]
    public void Bosh_harf_va_ortiqcha_boshliq_farq_emas()
    {
        Assert.Equal(IgQualityLog.Normalize("Salom   dunyo"), IgQualityLog.Normalize("salom dunyo"));
    }

    [Fact]
    public void Turli_apostroflar_bir_xil_hisoblanadi()
    {
        // Matn turli klaviaturalardan kiritiladi — "to'lov" va "toʻlov" bir xil so'z.
        Assert.Equal(IgQualityLog.Normalize("to'lov"), IgQualityLog.Normalize("toʻlov"));
        Assert.Equal(IgQualityLog.Normalize("to'lov"), IgQualityLog.Normalize("to’lov"));
    }

    // ===================== 2) Masofa va o'xshashlik =====================

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitob", "kitab", 1)]
    [InlineData("salom", "salomlar", 3)]
    public void Levenshtein_masofasi(string a, string b, int expected)
    {
        Assert.Equal(expected, IgQualityLog.EditDistance(a, b));
    }

    [Fact]
    public void Ayni_matn_toliq_oxshash()
    {
        Assert.Equal(1.0, IgQualityLog.Similarity("Salom!", "salom!"), 5);
        Assert.Equal(100, IgQualityLog.SimilarityPercent("Salom!", "Salom!"));
    }

    [Fact]
    public void Bitta_tomon_bosh_bolsa_oxshashlik_nol()
    {
        Assert.Equal(0.0, IgQualityLog.Similarity("Salom", ""));
        Assert.Equal(0.0, IgQualityLog.Similarity("", "Salom"));
    }

    [Fact]
    public void Ikkalasi_bosh_bolsa_bir()
    {
        Assert.Equal(1.0, IgQualityLog.Similarity(null, "   "));
    }

    [Fact]
    public void Butunlay_boshqa_matn_past_ball_beradi()
    {
        var p = IgQualityLog.SimilarityPercent(
            "IELTS kursimiz narxi 500 000 so'm",
            "Assalomu alaykum, qanday yordam bera olaman?");

        Assert.InRange(p, 0, 40);
    }

    [Fact]
    public void Kichik_tuzatish_yuqori_ball_beradi()
    {
        var p = IgQualityLog.SimilarityPercent(
            "IELTS kursimiz narxi 500 000 so'm",
            "IELTS kursimiz narxi 550 000 so'm");

        Assert.InRange(p, 80, 99);
    }

    // ===================== 3) "Tahrirlandimi" =====================

    [Fact]
    public void Faqat_registr_va_apostrof_farqi_tahrir_EMAS()
    {
        Assert.False(IgQualityLog.IsEdited("To'lov qiling", "to’lov   qiling"));
    }

    [Fact]
    public void Matn_ozgarsa_tahrir()
    {
        Assert.True(IgQualityLog.IsEdited("500 000 so'm", "550 000 so'm"));
    }

    // ===================== 4) Taklif nomzodi =====================

    private const string Now = "2026-08-21T12:00:00";
    private static DateTime NowDt => DateTime.Parse(Now, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void AI_ning_oxirgi_javobi_taklif_hisoblanadi()
    {
        Assert.True(IgQualityLog.IsSuggestionCandidate(Out(true, "AI matni", "2026-08-21T11:50:00"), NowDt));
    }

    [Fact]
    public void Operatorning_oz_xabari_taklif_EMAS()
    {
        // Aks holda operatorning ketma-ket ikki xabari "tahrirladi" bo'lib sanalardi.
        Assert.False(IgQualityLog.IsSuggestionCandidate(Out(false, "Odam yozgan", "2026-08-21T11:50:00"), NowDt));
    }

    [Fact]
    public void Kiruvchi_xabar_taklif_EMAS()
    {
        Assert.False(IgQualityLog.IsSuggestionCandidate(Out(true, "Mijoz", "2026-08-21T11:50:00", dir: "in"), NowDt));
    }

    [Fact]
    public void Eski_taklif_hisobga_olinmaydi()
    {
        // Ertasi kungi javob kechagi bot matnining tahriri emas — bu suhbatning yangi qadami.
        Assert.False(IgQualityLog.IsSuggestionCandidate(Out(true, "AI matni", "2026-08-20T12:00:00"), NowDt));
    }

    [Fact]
    public void Matnsiz_yoki_buzuq_sanali_taklif_olinmaydi()
    {
        Assert.False(IgQualityLog.IsSuggestionCandidate(Out(true, "   ", "2026-08-21T11:50:00"), NowDt));
        Assert.False(IgQualityLog.IsSuggestionCandidate(Out(true, "AI matni", "buzuq-sana"), NowDt));
        Assert.False(IgQualityLog.IsSuggestionCandidate(null, NowDt));
    }
}
