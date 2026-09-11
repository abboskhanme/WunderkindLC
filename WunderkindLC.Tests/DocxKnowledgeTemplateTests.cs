using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// TAYYOR WORD NAMUNASI (<see cref="DocxKnowledgeTemplate"/>).
///
/// <para>🔴 <b>Bu testlarning asosiy vazifasi — NAMUNA va PARSERNI bir-biriga QULFLASH.</b>
/// Markazga to'ldirish uchun hujjat beramiz; agar ajratish qoidasi (sarlavha aniqlash,
/// yo'riqnoma belgisi, uslub nomlari) o'zgarsa va namuna eskirib qolsa, biz markazga
/// <b>o'zimiz qabul qilmaydigan</b> hujjatni berib turardik va buni hech kim sezmasdi —
/// nosozlik faqat «yukladim, lekin AI hech narsa bilmaydi» shikoyati orqali chiqardi.
/// Shuning uchun namuna shu yerda HAQIQIY parserdan o'tkaziladi.</para>
///
/// <para>Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §21.11.</para>
/// </summary>
public class DocxKnowledgeTemplateTests
{
    private static readonly byte[] Bytes = DocxKnowledgeTemplate.Build();

    // ===================== 1) HUJJAT HAQIQIY .docx =====================

    /// <summary>Namuna Word ocha oladigan haqiqiy paket bo'lishi kerak (ZIP imzosi bilan).</summary>
    [Fact]
    public void Namuna_haqiqiy_docx_paket()
    {
        Assert.True(Bytes.Length > 0);
        // `PK\x03\x04` — controller yuklashda AYNAN shu imzoni tekshiradi.
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, Bytes[..4]);
    }

    /// <summary>
    /// 🔴 Hujjat OOXML sxemasiga to'liq mos bo'lishi kerak. Parser buzuq hujjatni ham o'qib
    /// yuborishi mumkin, WORD esa «faylni ochib bo'lmadi» deb rad etardi — ya'ni nosozlik
    /// bizda emas, markazning stolida chiqardi va sababini topib bo'lmasdi.
    /// </summary>
    [Fact]
    public void Namuna_OOXML_sxemasiga_MOS()
    {
        using var ms = new MemoryStream(Bytes);
        using var doc = WordprocessingDocument.Open(ms, false);

        var problems = new OpenXmlValidator().Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToList();

        Assert.Empty(problems);
    }

    /// <summary>
    /// Sarlavhalar HAQIQIY uslub bilan qo'yilgan bo'lishi kerak — parser aynan shunga qaraydi.
    /// Matnni «qalin va katta» qilib qo'yish bo'lim OCHMAYDI.
    /// </summary>
    [Fact]
    public void Sarlavhalar_Heading_uslubi_bilan_qoyilgan()
    {
        using var ms = new MemoryStream(Bytes);
        using var doc = WordprocessingDocument.Open(ms, false);

        var styleIds = doc.MainDocumentPart!.Document.Body!
            .Elements<Paragraph>()
            .Select(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "")
            .Where(v => v.Length > 0)
            .Distinct()
            .ToList();

        Assert.Contains("Heading1", styleIds);
        Assert.Contains("Heading2", styleIds);
    }

    /// <summary>
    /// ⚠️ Uslub NOMI Word'ning ichki nomi bo'lishi shart («heading 1»): shundagina Word uni
    /// O'ZINING «Heading 1» uslubi deb taniydi va foydalanuvchi yangi bo'limni tanish joydan
    /// qo'sha oladi. Boshqa nom berilsa hujjatda ro'yxatdan topilmaydigan begona uslub
    /// paydo bo'lardi va namunaning butun ma'nosi yo'qolardi.
    /// </summary>
    [Fact]
    public void Uslublar_Word_ning_ICHKI_nomi_bilan_elon_qilingan()
    {
        using var ms = new MemoryStream(Bytes);
        using var doc = WordprocessingDocument.Open(ms, false);

        var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!
            .Elements<Style>()
            .ToDictionary(x => x.StyleId!.Value!, x => x.StyleName?.Val?.Value ?? "");

        Assert.Equal("heading 1", styles["Heading1"]);
        Assert.Equal("heading 2", styles["Heading2"]);
    }

    // ===================== 2) PARSERDAN O'TISH =====================

    /// <summary>
    /// 🔴 <b>ENG MUHIM TEST.</b> To'ldirilmagan namuna yuklansa bilim bazasiga <b>HECH NARSA</b>
    /// tushmasligi kerak: undagi har bir qator yo'riqnoma.
    ///
    /// <para>Aks holda «(bu yerga narxlarni yozing)» kabi qatorlar — yoki namunadagi o'ylab
    /// topilgan NARX — haqiqiy ma'lumot bo'lib qolar va AI ularni mijozga aytardi.</para>
    /// </summary>
    [Fact]
    public void Toldirilmagan_namuna_bilim_bazasiga_HECH_NARSA_qoshmaydi()
    {
        var result = DocxKnowledge.Parse(Bytes, DocxKnowledgeTemplate.FileName);

        Assert.Empty(result.Chunks);
    }

    /// <summary>
    /// Bo'sh namuna «xato» emas, TUSHUNARLI sabab qaytarishi kerak: foydalanuvchi nima
    /// qilishini bilsin («to'ldirib, qaytadan yuklang»), umumiy «matn topilmadi» matni
    /// uni boshi berk ko'chaga olib kirardi.
    /// </summary>
    [Fact]
    public void Toldirilmagan_namuna_TUSHUNARLI_sabab_qaytaradi()
    {
        var result = DocxKnowledge.Parse(Bytes, DocxKnowledgeTemplate.FileName);

        Assert.Contains(DocxKnowledge.NoteMarker, result.Error);
        Assert.Contains("to'ldirib", result.Error);
    }

    /// <summary>
    /// Namuna TO'LDIRILGANDA aynan o'sha bo'limlar chiqishi kerak — yo'riqnoma esa baribir
    /// tushmasligi. To'ldirish namunaning O'Z modeli ustida bajariladi (yo'riqnoma qatorlari
    /// o'rniga matn qo'yiladi), ya'ni test haqiqiy foydalanuvchi qadamini takrorlaydi.
    /// </summary>
    [Fact]
    public void Toldirilgan_namuna_TOGRI_bolaklarga_bolinadi()
    {
        // Foydalanuvchi qiladigan ish: yo'riqnoma qatorini o'z matni bilan almashtirish.
        var filled = DocxKnowledgeTemplate.Content()
            .Select(l => l.Level > 0 || !DocxKnowledge.IsNote(l.Text)
                ? l
                : new DocxKnowledge.DocxLine("Markaz ma'lumoti: shu yerda haqiqiy matn turadi.", 0))
            .ToList();

        var chunks = DocxKnowledge.Split(filled, "namuna");
        var titles = chunks.Select(c => c.Title).ToList();

        // Yo'riqnoma bo'limi sarlavha bo'lsa ham bo'lak OCHMAYDI.
        Assert.DoesNotContain(titles, t => DocxKnowledge.IsNote(t));
        Assert.DoesNotContain(chunks, c => c.Content.Contains(DocxKnowledge.NoteMarker));

        // Markaz uchun eng muhim bo'limlar joyida.
        Assert.Contains("Markaz haqida", titles);
        Assert.Contains("Narxlar va to'lov", titles);
        Assert.Contains("Manzil va ish vaqti", titles);
        Assert.Contains("Dars jadvali", titles);

        // «Heading 2» bo'limlari IZ bilan ataladi (RAG sarlavhani ham o'qiydi).
        Assert.Contains(titles, t => t.StartsWith("Kurslar › ", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.StartsWith("Ko'p so'raladigan savollar › ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Namunaning O'ZIDA yo'riqnoma sarlavhasi BOR va u eng boshida turadi — foydalanuvchi
    /// hujjatni ochganda birinchi bo'lib «qanday to'ldirish kerak» ni o'qiydi.
    /// </summary>
    [Fact]
    public void Yoriqnoma_hujjatning_BOSHIDA_turadi()
    {
        var first = DocxKnowledgeTemplate.Content()[0];

        Assert.Equal(1, first.Level);
        Assert.True(DocxKnowledge.IsNote(first.Text));
    }

    /// <summary>
    /// ⚠️ Namunada bo'sh qator BO'LMASLIGI kerak: bo'shliq uslublardagi <c>spacing</c> orqali
    /// beriladi. Bo'sh paragraflar model ichida yursa ular bo'lak matniga ham tushib,
    /// promptni kerakmas bo'sh qatorlar bilan to'ldirardi.
    /// </summary>
    [Fact]
    public void Namunada_bosh_qator_yoq()
    {
        Assert.DoesNotContain(DocxKnowledgeTemplate.Content(), l => l.Text.Trim().Length == 0);
    }

    /// <summary>
    /// Yuklash endpointi faylni kengaytma bo'yicha ham tekshiradi — namuna nomi o'sha
    /// tekshiruvdan o'tishi shart (o'zimiz bergan faylni o'zimiz rad etmaylik).
    /// </summary>
    [Fact]
    public void Namuna_nomi_docx_bilan_tugaydi() =>
        Assert.EndsWith(".docx", DocxKnowledgeTemplate.FileName, StringComparison.Ordinal);
}
