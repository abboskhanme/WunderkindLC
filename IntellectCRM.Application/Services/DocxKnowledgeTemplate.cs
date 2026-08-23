using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IntellectCRM.Application.Services;

/// <summary>
/// BILIM BAZASI UCHUN TAYYOR WORD NAMUNASI (Marketing → Bilim bazasi → «Namuna yuklab olish»).
///
/// <para><b>Muammo.</b> Word yuklash imkoniyati paydo bo'lgach keyingi savol darhol chiqdi:
/// markaz hujjatni <b>QANDAY</b> yozsa, u to'g'ri bo'laklarga bo'linadi? Ajratish Word'ning
/// SARLAVHA uslublariga tayanadi (<see cref="DocxKnowledge"/>), oddiy foydalanuvchi esa
/// sarlavhani ko'pincha «qalin qilib kattalashtirib» yozadi — bunday hujjat bitta ulkan
/// bo'lak bo'lib tushardi va RAG undan kerakli qismni ajrata olmasdi. Ya'ni yuklash
/// «ishlagandek» ko'rinib, javob sifati past qolardi.</para>
///
/// <para><b>Yechim.</b> Markazga tayyor namuna beriladi: sarlavhalari to'g'ri uslub bilan
/// qo'yilgan, bo'limlari o'quv markazi uchun oldindan tuzilgan hujjat. Foydalanuvchi faqat
/// matn yozadi.</para>
///
/// <para>🔴 <b>NAMUNA KOD BILAN QURILADI, repoda tayyor fayl SAQLANMAYDI.</b> Sabab: ikkalasi
/// bir-biriga bog'liq — ajratish qoidasi o'zgarsa (masalan sarlavha aniqlash yoki yo'riqnoma
/// belgisi), tayyor ikkilik fayl JIMGINA eskirardi va biz markazga o'zimiz qabul qilmaydigan
/// hujjatni berib turardik. Bu yerda esa namuna AYNAN <see cref="DocxKnowledge"/> tanigan
/// uslublar bilan quriladi va <c>DocxKnowledgeTemplateTests</c> namunani parserdan O'TKAZIB
/// tekshiradi — «bergan namunamiz haqiqatan bo'linadimi» degan savol testda qulflangan.</para>
///
/// <para>⚠️ Namunadagi HAR BIR maslahat <see cref="DocxKnowledge.NoteMarker"/> bilan yozilgan,
/// ya'ni <b>to'ldirilmagan namuna yuklansa bilim bazasiga hech narsa tushmaydi</b>. Aks holda
/// «(bu yerga narxlarni yozing)» kabi qatorlar — yoki namunadagi o'ylab topilgan NARX —
/// haqiqiy ma'lumot bo'lib qolar va AI ularni mijozga aytardi.</para>
/// </summary>
public static class DocxKnowledgeTemplate
{
    /// <summary>Yuklab olinadigan fayl nomi.</summary>
    public const string FileName = "bilim-bazasi-namuna.docx";

    /// <summary>Word hujjatining MIME turi (javob sarlavhasi uchun).</summary>
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Sarlavha rangi — o'qishga qulay to'q ko'k (Word'ning standart Heading rangi).</summary>
    private const string HeadingColor = "1F3864";

    /// <summary>Yo'riqnoma qatorlari rangi — kulrang, ya'ni ular hujjatda DARHOL ajralib turadi
    /// («bu men yozadigan matn emas»).</summary>
    private const string NoteColor = "7F7F7F";

    // =============================================================================================
    //  NAMUNA MATNI — sof ma'lumot (Word'siz), shuning uchun to'g'ridan-to'g'ri testlanadi
    // =============================================================================================

    /// <summary>
    /// Namunaning to'liq mazmuni: sarlavhalar (daraja bilan) va yo'riqnoma qatorlari.
    ///
    /// <para>Bo'limlar tanlovi — Instagram'da eng ko'p so'raladigan narsalar: narx, jadval,
    /// manzil, sinov darsi, chegirma. Bilim bazasida bo'lmagan savolga AI javob bermaydi
    /// («operatorimiz bog'lanadi»), ya'ni bu ro'yxat amalda «AI nimaga javob bera oladi»
    /// degan ro'yxat.</para>
    ///
    /// <para>⚠️ Bo'sh qator (ajratkich) ATAYIN yo'q: bo'shliq uslublardagi
    /// <c>spacing</c> orqali beriladi. Bo'sh paragraflar model ichida yursa ular
    /// <see cref="DocxKnowledge.Split"/> ga ham tushib, bo'lak matnini kerakmas bo'sh
    /// qatorlar bilan to'ldirardi.</para>
    /// </summary>
    public static IReadOnlyList<DocxKnowledge.DocxLine> Content()
    {
        var lines = new List<DocxKnowledge.DocxLine>();

        void Head(string text, int level) => lines.Add(new DocxKnowledge.DocxLine(text, level));
        void Note(string text) => lines.Add(new DocxKnowledge.DocxLine(N(text), 0));

        // ── YO'RIQNOMA ──
        // ⚠️ Sarlavhaning O'ZI ham yo'riqnoma belgisi bilan: shunda BUTUN bo'lim (undagi
        // barcha qatorlar) bilim bazasiga umuman tushmaydi.
        Head(N("BILIM BAZASI — WORD NAMUNASI"), 1);
        Note("Bu hujjatni to'ldiring va CRM'ga yuklang: Marketing → Bilim bazasi → «Word yuklash».");
        Note("Instagram'dagi AI mijozlarga FAQAT shu yerdagi ma'lumot asosida javob beradi.");
        Note("");
        Note($"1) «{DocxKnowledge.NoteMarker}» bilan boshlangan qatorlar — YO'RIQNOMA. Ular bilim bazasiga TUSHMAYDI.");
        Note("   O'z matningizni ularning o'rniga yozing yoki ostiga qo'shing.");
        Note("2) Har SARLAVHA alohida bo'lak bo'ladi. Sarlavha — Word'dagi «Heading 1» / «Heading 2» uslubi.");
        Note("   Yangi bo'lim qo'shish: matnni yozing → Word'da «Uslublar» ro'yxatidan «Heading 1» ni tanlang.");
        Note("   Shunchaki qalin va katta harf YETMAYDI — u oddiy matn hisoblanadi va bo'lim ochmaydi.");
        Note("3) Har bo'lim MUSTAQIL o'qilsin. AI savolga eng mos bir nechta bo'limni ko'radi, xolos —");
        Note("   «yuqorida yozilgan», «o'sha narx» kabi havolalar ISHLAMAYDI. Narxni har joyda to'liq yozing.");
        Note("4) ANIQ fakt yozing: raqam, sana, manzil, shart. AI hech narsa o'ylab topmaydi —");
        Note("   bu yerda yo'q savolga «operatorimiz tez orada bog'lanadi» deb javob beradi.");
        Note("5) Mijoz KO'RMASLIGI kerak bo'lgan narsani yozmang (ichki eslatma, xodim maoshi, parol):");
        Note("   bu yerdagi matnni AI mijozga aytib qo'yishi mumkin.");
        Note("6) Kerakmas bo'limni butunlay o'chiring — to'ldirilmagan bo'lim baribir yuklanmaydi.");
        Note("7) Ma'lumot o'zgarsa: shu hujjatni tuzating, CRM'da ESKI faylni o'chiring, so'ng yangisini");
        Note("   yuklang. Ikkalasi qolsa bilim bazasida ikki xil narx turadi va AI eskisini aytishi mumkin.");
        Note("8) Jadval ham o'qiladi — narx ro'yxatini Word jadvali bilan yozsangiz ham bo'ladi.");

        // ── TO'LDIRILADIGAN BO'LIMLAR ──
        Head("Markaz haqida", 1);
        Note("Nomi, nechanchi yildan beri ishlaydi, qaysi yo'nalishlar bo'yicha, nechta filial,");
        Note("nima bilan ajralib turadi.");

        Head("Manzil va ish vaqti", 1);
        Note("To'liq manzil va mo'ljal, ish kunlari va soatlari, dam olish kunlari.");

        Head("Aloqa", 1);
        Note("Telefon raqamlari, Telegram, Instagram, sayt. Qaysi raqamga qachon qo'ng'iroq qilish mumkin.");

        Head("Kurslar", 1);
        Note("Har kurs uchun pastdagi kabi ALOHIDA «Heading 2» bo'lim oching.");

        Head("Kurs nomi (masalan: Ingliz tili)", 2);
        Note("Kimga mo'ljallangan, boshlang'ich daraja talab qilinadimi, davomiyligi, haftada necha kun,");
        Note("bitta dars necha daqiqa, guruhda nechta o'quvchi, nima beriladi (kitob, material),");
        Note("kurs oxirida o'quvchi nimaga erishadi.");

        Head("Narxlar va to'lov", 1);
        Note("Har kurs uchun narxni TO'LIQ yozing, masalan: «Ingliz tili (boshlang'ich) — oyiga 500 000 so'm».");
        Note("To'lov usullari (naqd, karta, Payme, Click), to'lov muddati, kechikkanda nima bo'ladi.");

        Head("Chegirmalar", 1);
        Note("Kimga va qancha: aka-uka, bir nechta kurs, oldindan to'lov, do'st olib kelish — shartlari bilan.");

        Head("Sinov darsi va ro'yxatdan o'tish", 1);
        Note("Sinov darsi bormi, pullikmi, qanday yoziladi, qanday hujjat kerak, guruh qachon ochiladi.");

        Head("Dars jadvali", 1);
        Note("Guruhlar qaysi kunlari va soatlarda: ertalabki, kunduzgi, kechki oqim; dam olish kunlaridagi guruhlar.");

        Head("O'qituvchilar", 1);
        Note("Tajriba, sertifikatlar, ona tilida so'zlashuvchi o'qituvchi bormi, kim qaysi kursni olib boradi.");

        Head("Sertifikat va natijalar", 1);
        Note("Kurs oxirida nima beriladi, imtihon bormi, o'quvchilarning erishgan natijalari.");

        Head("Ko'p so'raladigan savollar", 1);
        Note("Har savol uchun alohida «Heading 2» oching va SAVOLNI sarlavha qilib yozing —");
        Note("AI mos bo'lakni aynan sarlavha bo'yicha ham topadi.");

        Head("Savol (masalan: Noldan boshlaganlar uchun guruh bormi?)", 2);
        Note("Javobni to'liq yozing.");

        return lines;
    }

    /// <summary>Matnni yo'riqnoma qatoriga aylantiradi (belgi bitta joyda turadi).</summary>
    private static string N(string text) =>
        text.Length == 0 ? DocxKnowledge.NoteMarker : $"{DocxKnowledge.NoteMarker} {text}";

    // =============================================================================================
    //  WORD HUJJATINI QURISH
    // =============================================================================================

    /// <summary>
    /// Namunani <c>.docx</c> baytlari sifatida quradi.
    ///
    /// <para>⚠️ Sarlavhalar uchun <b>haqiqiy uslublar</b> (<c>Heading1</c>/<c>Heading2</c>)
    /// e'lon qilinadi, faqat matn kattalashtirilmaydi. Ikki sabab: (1) parser AYNAN uslubga
    /// qaraydi; (2) foydalanuvchi Word'ning «Uslublar» ro'yxatidan o'sha uslubni tanlab
    /// YANGI bo'lim qo'sha oladi va hujjatning tuzilishi Navigatsiya panelida ko'rinadi.</para>
    ///
    /// <para>⚠️ <c>MemoryStream.ToArray()</c> hujjat YOPILGANDAN keyin chaqiriladi: OpenXml
    /// paketni <c>Dispose</c> paytida yakunlaydi va oqim erta o'qilsa fayl buzuq chiqardi.</para>
    /// </summary>
    public static byte[] Build()
    {
        using var ms = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();

            var stylePart = main.AddNewPart<StyleDefinitionsPart>();
            stylePart.Styles = BuildStyles();
            stylePart.Styles.Save();

            var body = main.Document.AppendChild(new Body());
            foreach (var line in Content()) body.AppendChild(BuildParagraph(line));

            main.Document.Save();
        }

        return ms.ToArray();
    }

    /// <summary>Bitta qator → Word paragrafi.</summary>
    private static Paragraph BuildParagraph(DocxKnowledge.DocxLine line)
    {
        var p = new Paragraph();
        if (line.Level > 0)
            p.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = "Heading" + Math.Clamp(line.Level, 1, 9) });

        var run = new Run();
        if (DocxKnowledge.IsNote(line.Text))
            run.RunProperties = new RunProperties(new Italic(), new Color { Val = NoteColor });

        // ⚠️ `Preserve`: qator boshidagi bo'shliqlar (yo'riqnoma ro'yxatining tashlanishi)
        // saqlanadi — aks holda Word ularni yeb, ro'yxat o'qilishi buzilardi.
        run.AppendChild(new Text(line.Text) { Space = SpaceProcessingModeValues.Preserve });
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Uslublar: <c>Normal</c> + <c>Heading1</c> + <c>Heading2</c>.
    ///
    /// <para>⚠️ <c>StyleName</c> AYNAN Word'ning ichki nomi bo'lishi kerak («heading 1»,
    /// kichik harflar bilan): shundagina Word uni O'ZINING «Heading 1» uslubi deb taniydi va
    /// foydalanuvchi uni tanish joyidan qo'llay oladi. Boshqa nom berilsa hujjatda begona,
    /// ro'yxatda topilmaydigan uslub paydo bo'lardi.</para>
    ///
    /// <para><c>OutlineLevel</c> — Word'ning Navigatsiya paneli va mundarija shu qiymatga
    /// qaraydi, ya'ni foydalanuvchi hujjat tuzilishini ko'rib turadi.</para>
    /// </summary>
    private static Styles BuildStyles() => new(
        new DocDefaults(
            new RunPropertiesDefault(
                new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                    new FontSize { Val = "22" })),      // 11 pt (yarim punktlarda)
            new ParagraphPropertiesDefault(
                new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto }))),

        new Style(
            new StyleName { Val = "Normal" },
            new PrimaryStyle())
        { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },

        HeadingStyle(1, sizeHalfPoints: "32", spacingBefore: "360"),
        HeadingStyle(2, sizeHalfPoints: "26", spacingBefore: "240"));

    /// <summary>Bitta sarlavha uslubi (daraja bo'yicha o'lcham va bo'shliq farq qiladi).</summary>
    private static Style HeadingStyle(int level, string sizeHalfPoints, string spacingBefore) => new(
        new StyleName { Val = $"heading {level}" },
        new BasedOn { Val = "Normal" },
        new NextParagraphStyle { Val = "Normal" },
        new UIPriority { Val = 9 },
        new PrimaryStyle(),
        // ⚠️ ELEMENTLAR TARTIBI OOXML sxemasi bilan belgilangan va uni buzsa Word faylni
        // «buzuq» deb rad etadi (bizda esa hammasi ishlagandek ko'rinardi — parser tartibga
        // qaramaydi). `w:pPr` da: keepNext → spacing → outlineLvl; `w:rPr` da: b → color → sz.
        // Buni `DocxKnowledgeTemplateTests.Namuna_OOXML_sxemasiga_MOS` qulflaydi.
        new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = spacingBefore, After = "120" },
            new OutlineLevel { Val = level - 1 }),
        new StyleRunProperties(
            new Bold(),
            new Color { Val = HeadingColor },
            new FontSize { Val = sizeHalfPoints }))
    { Type = StyleValues.Paragraph, StyleId = "Heading" + level };
}
