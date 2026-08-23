using IntellectCRM.Application.Services;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// WORD → BILIM BAZASI (<see cref="DocxKnowledge"/>).
///
/// <para>Ajratish qoidasi <see cref="DocxKnowledge.Split"/> da SOF funksiya sifatida turadi
/// (OpenXml ham, fayl ham yo'q) — aynan shu testlanadi. OpenXml o'qish qatlami (uslub nomi →
/// daraja, jadval → qator) bu yerda emas: u kutubxonaning o'z ishi va soxta <c>.docx</c>
/// baytlarini yasash testni faylni emas, kutubxonani sinashga aylantirardi.</para>
///
/// <para>Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §21.10.</para>
/// </summary>
public class DocxKnowledgeTests
{
    private static DocxKnowledge.DocxLine H(string text, int level = 1) => new(text, level);
    private static DocxKnowledge.DocxLine P(string text) => new(text, 0);

    // ===================== 1) SARLAVHALAR BO'YICHA AJRATISH =====================

    [Fact]
    public void Har_sarlavha_yangi_bolak_boshlaydi()
    {
        var chunks = DocxKnowledge.Split(
            [H("Kurslar"), P("Bizda IELTS va umumiy ingliz tili kurslari bor."),
             H("Narxlar"), P("IELTS — oyiga 500 000 so'm.")],
            "Hujjat");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Kurslar", chunks[0].Title);
        Assert.Equal("Narxlar", chunks[1].Title);
        Assert.Contains("IELTS va umumiy", chunks[0].Content);
        Assert.Contains("500 000", chunks[1].Content);
    }

    /// <summary>
    /// 🔴 Sarlavha IZI: «Narxlar» degan yolg'iz so'z qaysi kursnikini bildirmaydi — bitta
    /// hujjatda o'nlab «Narxlar» bo'limi bo'lishi mumkin. Sarlavha vektorga ham kiradi, ya'ni
    /// izsiz RAG to'g'ri bo'lakni tanlay olmasdi.
    /// </summary>
    [Fact]
    public void Ichma_ich_sarlavhalar_IZ_bolib_qoshiladi()
    {
        var chunks = DocxKnowledge.Split(
            [H("Kurslar", 1), H("IELTS", 2), P("Haftada 3 kun."),
             H("Narxlar", 3), P("500 000 so'm.")],
            "Hujjat");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Kurslar › IELTS", chunks[0].Title);
        Assert.Equal("Kurslar › IELTS › Narxlar", chunks[1].Title);
    }

    /// <summary>
    /// Bir darajadagi keyingi sarlavha izning O'SHA bo'g'inini ALMASHTIRADI. Aks holda
    /// «Kurslar › IELTS › Nemis tili» kabi mantiqan noto'g'ri iz chiqardi.
    /// </summary>
    [Fact]
    public void Bir_darajadagi_sarlavha_izni_ALMASHTIRADI()
    {
        var chunks = DocxKnowledge.Split(
            [H("Kurslar", 1), H("IELTS", 2), P("Ingliz tili."),
             H("Nemis tili", 2), P("A1 dan B2 gacha.")],
            "Hujjat");

        Assert.Equal("Kurslar › IELTS", chunks[0].Title);
        Assert.Equal("Kurslar › Nemis tili", chunks[1].Title);
    }

    /// <summary>Yuqoriroq darajaga qaytilganda chuqur bo'g'inlar tushib qoladi.</summary>
    [Fact]
    public void Yuqori_darajaga_qaytilganda_iz_qisqaradi()
    {
        var chunks = DocxKnowledge.Split(
            [H("Kurslar", 1), H("IELTS", 2), P("Matn."),
             H("Manzil", 1), P("Ko'kon shahri.")],
            "Hujjat");

        Assert.Equal("Kurslar › IELTS", chunks[0].Title);
        Assert.Equal("Manzil", chunks[1].Title);
    }

    /// <summary>
    /// Sarlavhadan OLDIN turgan matn (muqaddima) YO'QOLMAYDI — u fayl nomi bilan atalgan
    /// birinchi bo'lakka tushadi. Jimgina tashlab yuborilsa hujjatning kirish qismi
    /// (ko'pincha markaz haqidagi umumiy ma'lumot) bilim bazasiga umuman kirmasdi.
    /// </summary>
    [Fact]
    public void Sarlavhadan_oldingi_matn_yoqolmaydi()
    {
        var chunks = DocxKnowledge.Split(
            [P("Markazimiz 2015-yildan beri ishlaydi."), H("Kurslar"), P("IELTS.")],
            "Markaz malumotlari");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Markaz malumotlari", chunks[0].Title);
        Assert.Contains("2015-yildan", chunks[0].Content);
    }

    /// <summary>Matnsiz sarlavha (faqat bo'lim nomi) bo'lak yaratmaydi — bo'sh qator bilim
    /// bazasini axlat bilan to'ldirardi. Lekin u IZDA qoladi (yuqoridagi testlar).</summary>
    [Fact]
    public void Matnsiz_sarlavha_bolak_yaratmaydi()
    {
        var chunks = DocxKnowledge.Split([H("Kurslar"), H("IELTS", 2), P("Haftada 3 kun.")], "Hujjat");

        var only = Assert.Single(chunks);
        Assert.Equal("Kurslar › IELTS", only.Title);
    }

    // ===================== 2) SARLAVHASIZ HUJJAT =====================

    /// <summary>
    /// Sarlavhasiz hujjat ham yuklanishi SHART: markazlarning ma'lumotlari ko'pincha oddiy
    /// matn bo'lib yozilgan. Bo'lak nomi fayl nomidan quriladi.
    /// </summary>
    [Fact]
    public void Sarlavhasiz_hujjat_fayl_nomi_bilan_bolinadi()
    {
        var lines = Enumerable.Range(1, 40)
            .Select(i => P($"{i}-qator: markaz haqida ma'lumot va tafsilotlar yozilgan matn."))
            .ToList();

        var chunks = DocxKnowledge.Split(lines, "Narxlar");

        Assert.True(chunks.Count > 1, "Uzun matn bo'laklarga bo'linishi kerak edi.");
        Assert.Equal("Narxlar — 1", chunks[0].Title);
        Assert.Equal("Narxlar — 2", chunks[1].Title);
        Assert.All(chunks, c => Assert.True(
            c.Content.Length <= DocxKnowledge.PlainChunkChars + 200,
            $"Bo'lak juda katta: {c.Content.Length}"));
    }

    /// <summary>Qisqa sarlavhasiz hujjat bitta bo'lak bo'lib qoladi.</summary>
    [Fact]
    public void Qisqa_sarlavhasiz_hujjat_bitta_bolak()
    {
        var chunks = DocxKnowledge.Split([P("Manzil: Ko'kon shahri, Turkiston 12.")], "Kontakt");

        var only = Assert.Single(chunks);
        Assert.Equal("Kontakt — 1", only.Title);
    }

    // ===================== 3) CHEGARALAR =====================

    /// <summary>
    /// Uzun bo'lim PARAGRAF chegarasida bo'linadi va nomiga «(N-qism)» qo'shiladi — promptda
    /// ham, ekranda ham qaysi bo'lakning davomi ekani ko'rinib tursin.
    /// </summary>
    [Fact]
    public void Uzun_bolim_qismlarga_bolinadi_va_nomlanadi()
    {
        var big = new string('a', 1500);
        var lines = new List<DocxKnowledge.DocxLine> { H("Shartlar") };
        for (var i = 0; i < 5; i++) lines.Add(P(big));

        var chunks = DocxKnowledge.Split(lines, "Hujjat");

        Assert.True(chunks.Count > 1);
        Assert.Equal("Shartlar (1-qism)", chunks[0].Title);
        Assert.Equal("Shartlar (2-qism)", chunks[1].Title);
    }

    /// <summary>
    /// MA'NOSIZ qoldiq tashlanadi: sahifa uzilishi, «———», ro'yxat belgisi — harf ham,
    /// raqam ham yo'q.
    /// </summary>
    [Theory]
    [InlineData("———")]
    [InlineData("•")]
    [InlineData("   ")]
    public void Manosiz_qoldiq_tashlanadi(string junk) =>
        Assert.Empty(DocxKnowledge.Split([H("Bo'lim"), P(junk)], "Hujjat"));

    /// <summary>
    /// 🔴 QISQA, lekin MA'NOLI javob QOLADI. Chegara uzunlik bo'yicha bo'lganda «Sinov darsi
    /// → Bepul» kabi eng qimmat qatorlar jimgina yo'qolardi.
    /// </summary>
    [Fact]
    public void Qisqa_lekin_manoli_javob_qoladi()
    {
        var chunks = DocxKnowledge.Split([H("Sinov darsi"), P("Bepul")], "Hujjat");

        var only = Assert.Single(chunks);
        Assert.Equal("Sinov darsi", only.Title);
        Assert.Equal("Bepul", only.Content);
    }

    /// <summary>Bo'sh kirish — istisno emas, bo'sh ro'yxat.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bosh_kirishda_yiqilmaydi(bool nullList) =>
        Assert.Empty(DocxKnowledge.Split(nullList ? null : [], "Hujjat"));

    /// <summary>Uzun sarlavha izi qisqartiriladi, lekin OXIRGI (eng aniq) bo'g'in saqlanadi.</summary>
    [Fact]
    public void Uzun_iz_qisqartiriladi_va_oxirgi_bogin_qoladi()
    {
        var chunks = DocxKnowledge.Split(
            [H(new string('A', 150), 1), H(new string('B', 150), 2), P("Matn yetarlicha uzun.")],
            "Hujjat");

        var only = Assert.Single(chunks);
        Assert.True(only.Title.Length <= DocxKnowledge.MaxTitleChars);
        Assert.Contains("BBB", only.Title);
    }

    // ===================== 4) FAYL NOMI =====================

    /// <summary>
    /// ⚠️ Fayl nomini FOYDALANUVCHI beradi va u ekranga chiqadi — papka yo'li (Windows'niki
    /// ham), kengaytma va boshqaruv belgilari tozalanadi.
    /// </summary>
    [Theory]
    [InlineData("Narxlar.docx", "Narxlar")]
    [InlineData("C:\\Users\\ali\\Kurslar.docx", "Kurslar")]
    [InlineData("/home/ali/Ichki tartib.docx", "Ichki tartib")]
    [InlineData("bir\nikki.docx", "bir ikki")]
    [InlineData("   ", "Hujjat")]
    [InlineData(null, "Hujjat")]
    [InlineData(".docx", ".docx")]
    public void Fayl_nomidan_bolak_nomi(string? input, string kutilgan) =>
        Assert.Equal(kutilgan, DocxKnowledge.TitleFromFileName(input));

    // ===================== 5) PARSE — buzuq fayl =====================

    /// <summary>
    /// 🔴 Buzuq yoki noto'g'ri formatdagi fayl uchun istisno OTILMAYDI — sabab o'zbekcha
    /// matn bo'lib qaytadi. Yuklash ekranida foydalanuvchiga AYNAN nima qilish kerakligi
    /// ko'rinishi kerak, «500 server xatosi» emas.
    /// </summary>
    [Fact]
    public void Buzuq_fayl_istisno_otmaydi_sabab_qaytadi()
    {
        var result = DocxKnowledge.Parse([1, 2, 3, 4, 5], "buzuq.docx");

        Assert.Empty(result.Chunks);
        Assert.NotEmpty(result.Error);
        Assert.Contains(".docx", result.Error);
    }

    [Fact]
    public void Bosh_fayl_sabab_bilan_rad_etiladi()
    {
        Assert.NotEmpty(DocxKnowledge.Parse([], "bosh.docx").Error);
        Assert.NotEmpty(DocxKnowledge.Parse(null, "bosh.docx").Error);
    }
}
