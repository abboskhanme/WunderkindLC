using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WunderkindLC.Application.Services;

/// <summary>
/// WORD HUJJATINI bilim bazasi BO'LAKLARIGA aylantiradi (Marketing → Bilim bazasi → «Word
/// yuklash»).
///
/// <para><b>Nega bo'laklarga bo'linadi, butun matn bitta qator qilib yozilmaydi:</b> AI promptiga
/// bilim bazasi <see cref="IgConst.KnowledgeLimit"/> (12000 belgi) gacha tushadi va undan
/// oshgani KESILADI, RAG esa (<see cref="IgKnowledgeRag"/>) savolga eng yaqin
/// <see cref="IgKnowledgeRag.TopN"/> ta BO'LAKNI tanlaydi. Ya'ni 40 betlik hujjat bitta qator
/// bo'lib yozilsa u yo butunlay promptni to'ldirib qolgan ma'lumotni siqib chiqarardi, yo
/// o'rtasidan kesilardi — ikkala holatda ham AI «bunday ma'lumot yo'q» deb javob berardi.
/// Bo'laklarga bo'linganda esa har savolga AYNAN kerakli bo'lim tanlanadi.</para>
///
/// <para><b>SARLAVHALAR IZI SAQLANADI.</b> Bo'lak nomi «Kurslar › IELTS › Narxlar» ko'rinishida
/// quriladi. Sabab: sarlavha ham vektorga kiradi (<see cref="IgKnowledgeRag.ContentHash"/>) va
/// «Narxlar» degan yolg'iz so'z qaysi kursnikini bildirmasdi — bir hujjatda o'nlab «Narxlar»
/// bo'limi bo'lishi mumkin.</para>
///
/// <para><b>JADVALLAR ham olinadi</b> — o'quv markazining narx ro'yxati va dars jadvali
/// deyarli har doim jadvalda bo'ladi. Ular tashlab ketilsa yuklash «ishladi» ko'rinar,
/// lekin eng kerakli ma'lumot yo'q bo'lardi.</para>
///
/// <para>⚠️ Faqat <b>.docx</b> (Office Open XML). Eski <b>.doc</b> — butunlay boshqa, ikkilik
/// format; u qo'llab-quvvatlanmaydi va sabab foydalanuvchiga OCHIQ aytiladi
/// (<see cref="Parse"/> xato matni), jimgina bo'sh natija qaytarilmaydi.</para>
///
/// <para>Tarmoq ham, baza ham yo'q — deterministik funksiyalar, <c>DocxKnowledgeTests</c> bilan
/// qoplangan.</para>
/// </summary>
public static class DocxKnowledge
{
    /// <summary>Bitta bo'lakning eng katta hajmi. Undan uzuni MA'NOLI joyda (paragraf
    /// chegarasida) bo'linadi va nomiga «(2-qism)» qo'shiladi.
    /// <para>4000 tanlangan: RAG 6 ta bo'lak tanlaydi, ya'ni eng yomon holatda promptga
    /// ~24000 belgi tushadi — bu Gemini uchun bemalol, lekin bitta bo'lak butun javobni
    /// egallab olmaydi.</para></summary>
    public const int MaxChunkChars = 4000;

    /// <summary>Sarlavhasiz hujjat shu hajmdagi bo'laklarga bo'linadi (sarlavhali hujjatdagidan
    /// KICHIK: bu yerda tabiiy chegara yo'q, ya'ni bo'lak qanchalik kichik bo'lsa RAG shuncha
    /// aniq tanlaydi).</summary>
    public const int PlainChunkChars = 2000;


    /// <summary>Bitta yuklashdan ko'pi bilan shuncha bo'lak. Chegaradan oshgani
    /// <b>JIM TASHLANMAYDI</b> — <see cref="DocxParseResult.Skipped"/> da qaytadi va ekranda
    /// ochiq yoziladi (yuklandi deb o'ylab, yarmi yo'q qolmasin).</summary>
    public const int MaxChunks = 200;

    /// <summary>Bo'lak nomining chegarasi (uzun sarlavha izi qisqartiriladi).</summary>
    public const int MaxTitleChars = 200;

    /// <summary>
    /// YO'RIQNOMA QATORI belgisi. Shu bilan boshlangan qator bilim bazasiga <b>TUSHMAYDI</b>.
    ///
    /// <para><b>Nega kerak (namuna hujjati bilan birga tug'ilgan qoida):</b> markazga
    /// to'ldirish uchun tayyor Word namunasi beriladi (<see cref="DocxKnowledgeTemplate"/>) va
    /// unda «qanday to'ldiriladi» yo'riqnomasi hamda har bo'lim uchun maslahat bo'ladi. Belgisiz
    /// ular ham bilim bazasiga tushardi — ya'ni AI mijozga «Har bo'limni mustaqil yozing» deb
    /// javob berib qo'yishi mumkin edi, RAG esa savolga eng yaqin 6 ta bo'lakni tanlaganda
    /// o'rinlarni yo'riqnoma egallab olardi.</para>
    ///
    /// <para>🔴 Ikkinchi, undan ham muhimroq foydasi: <b>namuna XAVFSIZ bo'ladi</b>. Har bir
    /// maslahat va misol shu belgi bilan yozilgani uchun to'ldirilmagan namuna yuklansa
    /// bilim bazasiga <b>hech narsa</b> tushmaydi. Aks holda «(bu yerga narxlarni yozing)»
    /// yoki namunadagi <b>o'ylab topilgan narx</b> haqiqiy ma'lumot bo'lib qolardi va AI uni
    /// mijozga aytardi.</para>
    ///
    /// <para>Uchinchi foydasi: markaz o'z hujjatida ichki eslatma qoldira oladi («bu narxni
    /// sentabrda ko'rib chiqamiz») va u mijozga hech qachon chiqmaydi.</para>
    ///
    /// <para>⚠️ SARLAVHAGA qo'yilsa BUTUN bo'lim (va uning ichki bo'limlari) tashlanadi —
    /// yo'riqnoma sahifasi aynan shunday chiqarib tashlanadi.</para>
    /// </summary>
    public const string NoteMarker = "//";

    /// <summary>Sarlavha izidagi bo'g'inlar ajratgichi.</summary>
    private const string TrailSeparator = " › ";

    /// <summary>Jadval katakchalari ajratgichi (matnda o'qiladigan ko'rinish).</summary>
    private const string CellSeparator = " | ";

    /// <summary>Hujjatdan olingan BITTA bo'lak — to'g'ridan-to'g'ri <c>IgKnowledge</c> qatoriga.</summary>
    public sealed record DocxChunk(string Title, string Content);

    /// <summary>
    /// O'qish natijasi. <paramref name="Error"/> bo'sh bo'lmasa — hech narsa yuklanmaydi.
    /// <paramref name="Skipped"/> — <see cref="MaxChunks"/> chegarasidan oshib, OLINMAGAN
    /// bo'laklar soni.
    /// </summary>
    public sealed record DocxParseResult(IReadOnlyList<DocxChunk> Chunks, int Skipped, string Error);

    /// <summary>Hujjatning bitta qatori: matn va u SARLAVHAMI (daraja bilan).</summary>
    /// <param name="Level">0 — oddiy matn; 1..9 — sarlavha darajasi (Heading1 → 1).</param>
    public sealed record DocxLine(string Text, int Level);

    // =============================================================================================
    //  KIRISH NUQTASI
    // =============================================================================================

    /// <summary>
    /// Word baytlarini bo'laklarga aylantiradi.
    ///
    /// <para>⚠️ Buzuq/qo'llab-quvvatlanmaydigan fayl uchun <b>istisno OTILMAYDI</b> — sabab
    /// <see cref="DocxParseResult.Error"/> da o'zbekcha matn bo'lib qaytadi. Yuklash ekranida
    /// foydalanuvchiga AYNAN nima qilish kerakligi ko'rinishi kerak, «500 server xatosi» emas.</para>
    /// </summary>
    /// <param name="fileName">Asl fayl nomi — sarlavhasiz hujjatda bo'lak nomi shundan quriladi.</param>
    public static DocxParseResult Parse(byte[]? bytes, string? fileName)
    {
        if (bytes is null || bytes.Length == 0)
            return new DocxParseResult([], 0, "Fayl bo'sh.");

        List<DocxLine> lines;
        try
        {
            lines = ReadLines(bytes);
        }
        catch (Exception)
        {
            // OpenXml buzuq arxivda ham, `.doc` ni `.docx` deb nomlaganda ham shu yerga tushadi.
            return new DocxParseResult([], 0,
                "Faylni o'qib bo'lmadi. Word hujjati .docx formatida bo'lishi kerak "
                + "(eski .doc qo'llab-quvvatlanmaydi — uni Word'da «Save As → .docx» bilan saqlang).");
        }

        if (lines.Count == 0)
            return new DocxParseResult([], 0,
                "Hujjatda matn topilmadi. Ma'lumot rasm ko'rinishida bo'lsa u o'qilmaydi — "
                + "matn sifatida yozilgan hujjat kerak.");

        var all = Split(lines, TitleFromFileName(fileName));
        if (all.Count == 0)
            // ⚠️ Ikki sabab BUTUNLAY boshqa ishni talab qiladi: to'ldirilmagan NAMUNA («o'z
            // matningizni yozing») va mazmunsiz hujjat («bu hujjatda umuman ma'lumot yo'q»).
            // Umumiy matn birinchi holatda foydalanuvchini boshi berk ko'chaga olib kirardi.
            return new DocxParseResult([], 0, lines.Any(l => IsNote(l.Text))
                ? $"Hujjatda faqat yo'riqnoma qatorlari («{NoteMarker}» bilan boshlanadi) qolibdi — "
                  + "ular ATAYIN yuklanmaydi. Namunani o'z ma'lumotlaringiz bilan to'ldirib, "
                  + "qaytadan yuklang."
                : "Hujjatda bilim bazasiga yozadigan matn topilmadi.");

        return all.Count <= MaxChunks
            ? new DocxParseResult(all, 0, "")
            : new DocxParseResult(all.Take(MaxChunks).ToList(), all.Count - MaxChunks, "");
    }

    // =============================================================================================
    //  1) HUJJATNI QATORLARGA
    // =============================================================================================

    /// <summary>
    /// Hujjat tanasini tartib bo'yicha o'qiydi: paragraflar va JADVALLAR.
    ///
    /// <para>⚠️ <c>Body.Elements()</c> ATAYIN <c>Descendants&lt;Paragraph&gt;()</c> o'rniga:
    /// ikkinchisi jadval ichidagi paragraflarni ham qaytaradi va katakchalar bir-biridan
    /// ajralmagan holda, hujjatdagi tartibdan chiqib aralashib ketardi — narx jadvali
    /// o'qib bo'lmaydigan matnga aylanardi.</para>
    /// </summary>
    private static List<DocxLine> ReadLines(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);

        var body = doc.MainDocumentPart?.Document?.Body;
        var lines = new List<DocxLine>();
        if (body is null) return lines;

        foreach (var element in body.Elements())
        {
            switch (element)
            {
                case Paragraph p:
                    var text = ParagraphText(p);
                    if (text.Length > 0) lines.Add(new DocxLine(text, HeadingLevel(p)));
                    break;

                case Table t:
                    foreach (var row in TableLines(t)) lines.Add(new DocxLine(row, 0));
                    break;
            }
        }

        return lines;
    }

    /// <summary>
    /// Paragraf matni. <c>Tab</c> bo'shliqqa, <c>Break</c> ham bo'shliqqa aylanadi — bo'lak
    /// matni bitta qator bo'lishi kerak (qator ichidagi uzilish ma'no bermaydi, lekin so'zlarni
    /// bir-biriga yopishtirib qo'yardi: «IELTS kursi»→«IELTSkursi»).
    /// </summary>
    private static string ParagraphText(Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var run in p.Descendants<Run>())
            foreach (var child in run.ChildElements)
                switch (child)
                {
                    case Text t: sb.Append(t.Text); break;
                    case TabChar: sb.Append(' '); break;
                    case Break: sb.Append(' '); break;
                }

        return Collapse(sb.ToString());
    }

    /// <summary>
    /// Jadval qatorlari: <c>katak | katak | katak</c>. Bo'sh qatorlar tashlanadi.
    ///
    /// <para>Sarlavha qatori alohida belgilanmaydi — u baribir birinchi qator bo'lib chiqadi va
    /// AI uni kontekstdan tushunadi. Belgilash uchun ustun nomlarini har qatorga takrorlash
    /// kerak bo'lardi va bu matnni bir necha barobar shishirardi.</para>
    /// </summary>
    private static List<string> TableLines(Table table)
    {
        var rows = new List<string>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>()
                .Select(c => Collapse(string.Join(' ', c.Descendants<Paragraph>().Select(ParagraphText))))
                .Where(s => s.Length > 0)
                .ToList();

            if (cells.Count > 0) rows.Add(string.Join(CellSeparator, cells));
        }
        return rows;
    }

    /// <summary>
    /// Paragrafning sarlavha darajasi: <c>Heading1</c> → 1, … <c>Heading9</c> → 9,
    /// <c>Title</c> → 1, qolgani → 0.
    ///
    /// <para>⚠️ Uslub nomi Word'ning TIL versiyasiga qarab o'zgaradi (<c>Heading1</c>,
    /// <c>berschrift1</c>, <c>1</c>) — shuning uchun raqamni ham qaraymiz: nomining oxiri
    /// 1..9 raqami bo'lgan uslub sarlavha deb hisoblanadi FAQAT nomida «head»/«title»/
    /// «заголов»/«sarlavha» bo'lsa. Har qanday raqamli uslubni sarlavha deb olish ro'yxat
    /// («ListParagraph3») uslublarini ham tortib olardi.</para>
    ///
    /// <para>⚠️ QALIN (bold) matn sarlavha DEB HISOBLANMAYDI: hujjatlarda qalin matn eng ko'p
    /// urg'u uchun ishlatiladi va u bilan ajratish hujjatni o'nlab mayda bo'lakka
    /// parchalab tashlardi.</para>
    /// </summary>
    private static int HeadingLevel(Paragraph p)
    {
        var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        if (style.Length == 0) return 0;

        var s = style.ToLowerInvariant();
        var isHeading = s.Contains("head") || s.Contains("title")
                        || s.Contains("заголов") || s.Contains("sarlavha");
        if (!isHeading) return 0;

        // Oxiridagi raqam — daraja. Raqamsiz sarlavha (`Title`) eng yuqori daraja.
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(s[i])) return s[i] - '0';
            if (char.IsLetter(s[i])) break;
        }
        return 1;
    }

    // =============================================================================================
    //  2) QATORLARDAN BO'LAKLAR
    // =============================================================================================

    /// <summary>
    /// Qatorlarni bo'laklarga ajratadi — <b>sof funksiya</b> (OpenXml ham, fayl ham yo'q),
    /// ya'ni ajratish qoidasi to'g'ridan-to'g'ri testlanadi.
    ///
    /// <para><b>Sarlavhali hujjat:</b> har sarlavha yangi bo'lak boshlaydi, nomi esa
    /// SARLAVHALAR IZI («Kurslar › IELTS»). Bir darajadagi keyingi sarlavha izning o'sha
    /// bo'g'inini ALMASHTIRADI, chuqurroq sarlavha esa QO'SHILADI.</para>
    ///
    /// <para><b>Sarlavhasiz hujjat:</b> matn <see cref="PlainChunkChars"/> hajmidagi bo'laklarga
    /// bo'linadi va nomi fayl nomidan quriladi — bo'lak nomi baribir bo'lishi kerak
    /// (u ekranda ham ko'rinadi, vektorga ham kiradi).</para>
    ///
    /// <para>⚠️ Sarlavhadan OLDIN turgan matn (muqaddima) yo'qolmaydi — u fayl nomi bilan
    /// atalgan birinchi bo'lakka tushadi.</para>
    /// </summary>
    public static IReadOnlyList<DocxChunk> Split(IReadOnlyList<DocxLine>? lines, string fallbackTitle)
    {
        if (lines is null || lines.Count == 0) return [];

        var fallback = string.IsNullOrWhiteSpace(fallbackTitle) ? "Hujjat" : fallbackTitle.Trim();
        var hasHeadings = lines.Any(l => l.Level > 0);
        if (!hasHeadings) return SplitPlain(lines, fallback);

        var result = new List<DocxChunk>();
        var trail = new List<(int Level, string Text)>();
        var buffer = new List<string>();
        var currentTitle = fallback;

        // Yo'riqnoma SARLAVHASI ochgan bo'lim: shu darajadan pastdagi hamma narsa tashlanadi.
        // 0 — hozir tashlanmayapti.
        var skipBelow = 0;

        void Flush()
        {
            foreach (var chunk in Emit(currentTitle, buffer)) result.Add(chunk);
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.Level == 0)
            {
                // Tashlanayotgan bo'lim ichidagi matn ham, yakka yo'riqnoma qatori ham olinmaydi.
                if (skipBelow > 0 || IsNote(line.Text)) continue;
                buffer.Add(line.Text);
                continue;
            }

            // Shu daraja yoki undan YUQORI sarlavha — tashlanayotgan bo'lim TUGADI.
            // (Ichki, chuqurroq sarlavhalar esa o'sha bo'limning davomi va ular ham tashlanadi.)
            if (skipBelow > 0 && line.Level <= skipBelow) skipBelow = 0;
            if (skipBelow > 0) continue;

            Flush();

            if (IsNote(line.Text))
            {
                // ⚠️ Bunday sarlavha IZGA ham kirmaydi: aks holda undan keyingi haqiqiy
                // bo'lim nomiga yo'riqnoma matni yopishib qolardi.
                skipBelow = line.Level;
                continue;
            }

            // Iz: shu darajadan PAST bo'lgan bo'g'inlar olib tashlanadi, so'ng yangisi qo'yiladi.
            // Aks holda «Kurslar › IELTS › Ingliz tili» kabi noto'g'ri ketma-ketlik chiqardi
            // (IELTS va «Ingliz tili» bir darajada bo'lsa ham).
            while (trail.Count > 0 && trail[^1].Level >= line.Level) trail.RemoveAt(trail.Count - 1);
            trail.Add((line.Level, line.Text));
            currentTitle = BuildTitle(trail);
        }

        Flush();
        return result;
    }

    /// <summary>Sarlavhasiz hujjat — hajm bo'yicha, PARAGRAF chegarasida bo'linadi.</summary>
    private static List<DocxChunk> SplitPlain(IReadOnlyList<DocxLine> lines, string fallback)
    {
        var result = new List<DocxChunk>();
        var buffer = new List<string>();
        var length = 0;

        void Flush()
        {
            if (buffer.Count == 0) return;
            var content = string.Join("\n", buffer).Trim();
            if (HasMeaning(content))
                result.Add(new DocxChunk(NumberedTitle(fallback, result.Count + 1), content));
            buffer.Clear();
            length = 0;
        }

        foreach (var line in lines)
        {
            if (IsNote(line.Text)) continue;

            // Paragrafning O'ZI chegaradan uzun bo'lsa ham bo'linmaydi: uni o'rtasidan kesish
            // gapni buzardi, bitta uzun paragraf esa `MaxChunkChars` dan sezilarli oshmaydi.
            if (length > 0 && length + line.Text.Length > PlainChunkChars) Flush();
            buffer.Add(line.Text);
            length += line.Text.Length + 1;
        }

        Flush();
        return result;
    }

    /// <summary>
    /// Bitta sarlavha ostidagi matndan bo'lak(lar). Chegaradan uzun bo'lsa PARAGRAF
    /// chegarasida bo'linadi va nomiga «(N-qism)» qo'shiladi — qaysi bo'lakning davomi
    /// ekani ekranda ham, promptda ham ko'rinib tursin.
    /// </summary>
    private static List<DocxChunk> Emit(string title, List<string> buffer)
    {
        var result = new List<DocxChunk>();
        if (buffer.Count == 0) return result;

        var parts = new List<string>();
        var current = new List<string>();
        var length = 0;

        foreach (var line in buffer)
        {
            if (length > 0 && length + line.Length > MaxChunkChars)
            {
                parts.Add(string.Join("\n", current));
                current.Clear();
                length = 0;
            }
            current.Add(line);
            length += line.Length + 1;
        }
        if (current.Count > 0) parts.Add(string.Join("\n", current));

        for (var i = 0; i < parts.Count; i++)
        {
            var content = parts[i].Trim();
            if (!HasMeaning(content)) continue;
            var name = parts.Count == 1 ? title : $"{title} ({i + 1}-qism)";
            result.Add(new DocxChunk(InstagramContract.Trim(name, MaxTitleChars), content));
        }

        return result;
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    /// <summary>Sarlavhalar izidan bo'lak nomi. Uzun iz OXIRIDAN saqlanadi — eng aniq
    /// (chuqur) bo'g'in eng qimmatlisi.</summary>
    private static string BuildTitle(List<(int Level, string Text)> trail)
    {
        var name = string.Join(TrailSeparator, trail.Select(t => t.Text));
        if (name.Length <= MaxTitleChars) return name;

        // Boshidagi bo'g'inlarni tashlab, oxirgilarini saqlaymiz.
        for (var skip = 1; skip < trail.Count; skip++)
        {
            var shorter = string.Join(TrailSeparator, trail.Skip(skip).Select(t => t.Text));
            if (shorter.Length <= MaxTitleChars) return shorter;
        }
        return InstagramContract.Trim(trail[^1].Text, MaxTitleChars);
    }

    /// <summary>«Hujjat» → «Hujjat — 3». Sarlavhasiz hujjatda bo'laklar shunday ataladi.</summary>
    private static string NumberedTitle(string fallback, int index) =>
        InstagramContract.Trim($"{fallback} — {index}", MaxTitleChars);

    /// <summary>
    /// Fayl nomidan bo'lak nomi: papka yo'li va kengaytma olib tashlanadi.
    ///
    /// <para>⚠️ Nomni FOYDALANUVCHI beradi, ya'ni u ekranga chiqadigan ishonchsiz matn —
    /// yangi qator va boshqaruv belgilari tozalanadi (ro'yxatni buzib ko'rsatmasin).</para>
    /// </summary>
    public static string TitleFromFileName(string? fileName)
    {
        var raw = (fileName ?? "").Trim();
        if (raw.Length == 0) return "Hujjat";

        // `Path.GetFileName` faqat joriy OS ajratgichini biladi — ikkalasini ham kesamiz
        // (fayl nomi brauzerdan keladi, ya'ni Windows yo'li ham bo'lishi mumkin).
        var cut = raw.LastIndexOfAny(['/', '\\']);
        if (cut >= 0) raw = raw[(cut + 1)..];

        var dot = raw.LastIndexOf('.');
        if (dot > 0) raw = raw[..dot];

        var clean = Collapse(new string(raw.Select(c => char.IsControl(c) ? ' ' : c).ToArray()));
        return clean.Length == 0 ? "Hujjat" : InstagramContract.Trim(clean, MaxTitleChars);
    }

    /// <summary>
    /// Bo'lakda YOZADIGAN narsa bormi — kamida bitta harf yoki raqam.
    ///
    /// <para>⚠️ Chegara UZUNLIK bo'yicha ATAYIN emas. Dastlab «10 belgidan qisqa bo'lak
    /// tashlansin» qoidasi qo'yilgan edi va u haqiqiy ma'lumotni yeb qo'yardi: «Sinov darsi»
    /// sarlavhasi ostidagi «Bepul» (5 belgi) yoki «Kurs» ostidagi «IELTS» (5 belgi) —
    /// bilim bazasidagi eng qimmat qatorlardan. Tashlanishi kerak bo'lgan narsa qisqa matn
    /// EMAS, MA'NOSIZ matn: sahifa uzilishi qoldig'i, «———», ro'yxat belgisi.</para>
    /// </summary>
    private static bool HasMeaning(string s)
    {
        foreach (var c in s) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    /// <summary>Qator yo'riqnomami (<see cref="NoteMarker"/> bilan boshlanadimi).</summary>
    public static bool IsNote(string? text) =>
        (text ?? "").TrimStart().StartsWith(NoteMarker, StringComparison.Ordinal);

    /// <summary>Ketma-ket bo'shliqlarni bittaga keltiradi va chetlarini tozalaydi.</summary>
    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
