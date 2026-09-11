using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace WunderkindLC.Application.Services;

/// <summary>
/// WORD (.docx) ANDOZASIGA QIYMAT QO'YISH — <c>@</c> bilan boshlanuvchi o'rinbosarlarni
/// (masalan <c>@fish</c>) berilgan qiymatlar bilan almashtiradi.
///
/// <para><b>Nega paragraf darajasida?</b> Word bitta so'zni ham bir nechta "run"ga bo'lib yozishi
/// mumkin (imlo tekshiruvi, formatlash, til belgisi) — u holda XML'da <c>@fi</c> va <c>sh</c>
/// alohida turadi va oddiy <c>Replace</c> topa olmaydi. Shuning uchun paragrafdagi barcha
/// <see cref="Text"/> larning matni BIRLASHTIRILADI, almashtiriladi va birinchi runga yoziladi,
/// qolganlari bo'shatiladi. Formatlash birinchi runniki bo'yicha qoladi.</para>
///
/// <para>Bu mantiq ilgari <c>ContractService</c> ichida edi; sertifikat andozalari ham AYNAN shu
/// sintaksisdan foydalanadi, shuning uchun bitta joyga ajratildi — ikki xil "token qoidasi"
/// paydo bo'lmasin.</para>
/// </summary>
public static class DocxTemplate
{
    /// <summary>Faqat harf va pastki chiziq — <c>@fish</c>, <c>@max_ball</c>. Raqam bilan
    /// tugaydigan token YO'Q (matndagi "@2024" kabi yozuvlar token deb o'qilmasin).</summary>
    private static readonly Regex TokenRx = new(@"@[A-Za-z_]+", RegexOptions.Compiled);

    /// <summary>
    /// Andoza baytlarini nusxalab tokenlarni almashtiradi va yangi .docx baytlarini qaytaradi.
    /// Asosiy hujjat, kolontitullar (header/footer) va jadval ichidagi paragraflar ham qamraladi.
    /// Noma'lum tokenlar O'Z HOLICHA qoladi (andoza muallifi xatosini ko'rsin).
    /// </summary>
    public static byte[] Fill(byte[] docxBytes, IDictionary<string, string> tokens)
    {
        using var ms = new MemoryStream();
        ms.Write(docxBytes, 0, docxBytes.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var main = doc.MainDocumentPart;
            if (main?.Document is not null)
            {
                ReplaceIn(main.Document, tokens);
                foreach (var h in main.HeaderParts) ReplaceIn(h.Header, tokens);
                foreach (var f in main.FooterParts) ReplaceIn(f.Footer, tokens);
                main.Document.Save();
            }
        }
        return ms.ToArray();
    }

    /// <summary>Matndagi <c>@</c>-tokenlarni almashtiradi (matnli andozalar uchun).</summary>
    public static string Apply(string? input, IDictionary<string, string> tokens) =>
        TokenRx.Replace(input ?? string.Empty, m => tokens.TryGetValue(m.Value, out var v) ? v : m.Value);

    /// <summary>Matnda ishlatilgan barcha <c>@</c>-tokenlar (andozani tekshirish uchun).</summary>
    public static IReadOnlyCollection<string> FindTokens(string? input) =>
        TokenRx.Matches(input ?? string.Empty).Select(m => m.Value).Distinct().ToList();

    // =============================================================================================
    //  RASM O'RNINI ALMASHTIRISH (o'quvchi surati)
    // =============================================================================================

    /// <summary>Rasm o'rni deb tanib olinadigan nomlar (Word'dagi "Alt Text" / rasm nomi).</summary>
    private static readonly string[] PhotoMarkers = ["rasm", "surat", "foto", "photo"];

    /// <summary>Andozadagi matn belgisi — shu yerga o'quvchining surati qo'yiladi.</summary>
    public const string PhotoToken = "@rasm";

    /// <summary>Standart surat o'lchami (PIKSEL) — hujjat surati (3×4) nisbatiga yaqin.
    /// <c>@rasm</c> belgisi ishlatilganda AYNAN shu o'lchamda qo'yiladi.</summary>
    public const int PhotoWidthPx = 185;
    public const int PhotoHeightPx = 260;

    /// <summary>1 piksel = 9525 EMU (Word'ning ichki o'lchov birligi, 96 DPI).</summary>
    private const long EmuPerPixel = 9525;

    /// <summary>
    /// Andozaga O'QUVCHINING SURATINI qo'yadi. <b>Ikki usul qo'llab-quvvatlanadi:</b>
    ///
    /// <list type="number">
    ///   <item><b><c>@rasm</c> matn belgisi</b> — eng oddiysi: shablonda shunchaki
    ///     <c>@rasm</c> deb yoziladi va o'sha joyga surat <b>185×260 px</b> o'lchamda qo'yiladi.</item>
    ///   <item><b>Word'dagi rasm o'rni</b> — muallif hujjatga rasm qo'yib, o'lchami/ramkasi/joyini
    ///     xohlagancha sozlaydi (alt-matni <c>rasm/surat/foto/photo</c>, yoki hujjatda yagona rasm
    ///     bo'lsa belgisiz ham bo'ladi). Bunda muallif bergan O'LCHAM saqlanadi.</item>
    /// </list>
    ///
    /// <para><b>Nisbat hech qachon buzilmaydi:</b> surat katakni to'ldiradi va ortiqchasi
    /// MARKAZDAN qirqiladi (<c>a:srcRect</c> — qirqishni Word/LibreOffice o'zi bajaradi, ya'ni
    /// rasmni qayta chizadigan kutubxona kerak emas). Yuz o'rtada bo'lgani uchun (surat kvadrat
    /// qilib olinadi) bu xavfsiz.</para>
    ///
    /// <para><paramref name="imageBytes"/> bo'sh yoki format qo'llanmasa (webp/heic) —
    /// <c>@rasm</c> belgisi MATNDAN OLIB TASHLANADI (sertifikatda "@rasm" yozuvi qolib ketmasin),
    /// Word'dagi rasm o'rni esa o'z holicha qoladi.</para>
    /// </summary>
    /// <returns>Yangi .docx baytlari; hech narsa o'zgarmasa — kirish massivining o'zi.</returns>
    public static byte[] ApplyPhoto(byte[] docxBytes, byte[]? imageBytes, string? extension)
    {
        var contentType = ContentTypeOf(extension);
        var hasPhoto = contentType is not null && imageBytes is { Length: > 0 };

        using var ms = new MemoryStream();
        ms.Write(docxBytes, 0, docxBytes.Length);
        ms.Position = 0;
        var changed = false;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var main = doc.MainDocumentPart;
            if (main is not null)
            {
                var parts = new List<(OpenXmlPart Part, OpenXmlElement Root)>();
                if (main.Document is not null) parts.Add((main, main.Document));
                foreach (var h in main.HeaderParts) parts.Add((h, h.Header));
                foreach (var f in main.FooterParts) parts.Add((f, f.Footer));

                // 1) `@rasm` belgilari — hujjatning HAMMA joyida (bir nechta bo'lishi mumkin).
                var tokenUsed = false;
                foreach (var (part, root) in parts)
                    if (InsertTokenImages(part, root, hasPhoto ? imageBytes : null, contentType))
                        tokenUsed = true;

                // 2) Belgi ishlatilmagan bo'lsa — Word'dagi rasm o'rnini almashtiramiz.
                if (!tokenUsed && hasPhoto)
                {
                    foreach (var (part, root) in parts)
                    {
                        var drawing = FindPhotoDrawing(root);
                        if (drawing is null) continue;
                        SwapImage(part, drawing, imageBytes!, contentType!);
                        tokenUsed = true;
                        break;
                    }
                }
                changed = tokenUsed;
                if (changed)
                {
                    main.Document?.Save();
                    foreach (var h in main.HeaderParts) h.Header?.Save();
                    foreach (var f in main.FooterParts) f.Footer?.Save();
                }
            }
        }
        return changed ? ms.ToArray() : docxBytes;
    }

    /// <summary>
    /// Paragraflardagi <c>@rasm</c> belgisini rasmga (yoki surat yo'q bo'lsa — bo'shliqqa)
    /// almashtiradi. Belgi Word tomonidan bir necha "run"ga bo'lingan bo'lishi mumkin, shuning
    /// uchun paragraf matni birlashtirilib, belgidan OLDINGI va KEYINGI qismlarga bo'linadi.
    /// </summary>
    /// <returns>Hech bo'lmasa bitta belgi topildimi.</returns>
    private static bool InsertTokenImages(
        OpenXmlPart part, OpenXmlElement root, byte[]? imageBytes, string? contentType)
    {
        var found = false;
        // Eng katta mavjud id — yangi rasmlarga takrorlanmaydigan id berish uchun.
        var nextId = root.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>()
            .Select(p => p.Id?.Value ?? 0U).DefaultIfEmpty(0U).Max() + 1U;

        string? relId = null;
        (int W, int H) size = (0, 0);

        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            var texts = para.Descendants<Text>().ToList();
            if (texts.Count == 0) continue;
            var combined = string.Concat(texts.Select(t => t.Text));
            // Belgi paragrafda BIR NECHTA bo'lishi mumkin (masalan bir qatorda ikkita surat o'rni).
            // Ilgari faqat BIRINCHISI almashtirilib, qolgani sertifikatda "@rasm" yozuvi bo'lib
            // chop etilardi — shuning uchun paragraf belgilar bo'yicha to'liq bo'linadi.
            var segments = SplitOnToken(combined, PhotoToken);
            if (segments.Count < 2) continue;   // belgi umuman yo'q

            found = true;

            // Matnni birinchi belgigacha bo'lgan qismga qisqartiramiz, qolgan run'lar bo'shatiladi.
            texts[0].Text = segments[0];
            texts[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < texts.Count; i++) texts[i].Text = "";

            var anchorRun = texts[0].Ancestors<Run>().FirstOrDefault();
            if (anchorRun?.Parent is null) continue;

            if (imageBytes is null || contentType is null)
            {
                // Surat yo'q — belgi(lar) olib tashlandi, qolgan matn joyida qoladi.
                texts[0].Text = string.Concat(segments);
                continue;
            }

            // Rasm qismi bir marta qo'shiladi va barcha belgilar uchun qayta ishlatiladi.
            if (relId is null)
            {
                relId = "rIdPhoto" + Guid.NewGuid().ToString("N")[..8];
                var imagePart = part.AddNewPart<ImagePart>(contentType, relId);
                using var src = new MemoryStream(imageBytes);
                imagePart.FeedData(src);
                size = ImageSize(imageBytes);
            }

            var cx = PhotoWidthPx * EmuPerPixel;
            var cy = PhotoHeightPx * EmuPerPixel;
            // Yangi elementlar ketma-ket, oldingisidan KEYIN qo'yiladi — tartib saqlansin.
            OpenXmlElement cursor = anchorRun;
            for (var k = 1; k < segments.Count; k++)
            {
                var imageRun = new Run(BuildInlineImage(
                    relId, cx, cy, nextId++, CoverCrop(size.W, size.H, cx, cy)));
                anchorRun.Parent.InsertAfter(imageRun, cursor);
                cursor = imageRun;

                if (segments[k].Length == 0) continue;
                var textRun = new Run(new Text(segments[k]) { Space = SpaceProcessingModeValues.Preserve });
                // FORMATLASH SAQLANADI: asl run'ning xossalari (shrift, o'lcham, qalinlik, rang)
                // nusxalanadi. Aks holda `@rasm` dan keyingi matn — masalan `@rasm @fish` da
                // o'quvchining ismi — hujjatning standart shriftiga tushib qolardi.
                if (anchorRun.RunProperties is { } rp)
                    textRun.RunProperties = (RunProperties)rp.CloneNode(true);
                anchorRun.Parent.InsertAfter(textRun, cursor);
                cursor = textRun;
            }
        }
        return found;
    }

    /// <summary>Matnni belgi bo'yicha bo'laklarga ajratadi (registrga qaramaydi).
    /// N ta belgi bo'lsa N+1 bo'lak qaytadi; belgi bo'lmasa bitta bo'lak (butun matn).</summary>
    private static List<string> SplitOnToken(string text, string token)
    {
        var parts = new List<string>();
        var from = 0;
        while (true)
        {
            var at = text.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { parts.Add(text[from..]); return parts; }
            parts.Add(text[from..at]);
            from = at + token.Length;
        }
    }

    /// <summary>Word uchun "matn ichidagi" (inline) rasm elementini yasaydi.</summary>
    private static Drawing BuildInlineImage(
        string relId, long cx, long cy, uint docPrId, (int L, int T, int R, int B) crop)
    {
        var blipFill = new PIC.BlipFill(new A.Blip { Embed = relId });
        if (crop != default)
            blipFill.Append(new A.SourceRectangle
            {
                Left = crop.L, Top = crop.T, Right = crop.R, Bottom = crop.B,
            });
        blipFill.Append(new A.Stretch(new A.FillRectangle()));

        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = docPrId, Name = "rasm" },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "rasm" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            blipFill,
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U, DistanceFromBottom = 0U,
                DistanceFromLeft = 0U, DistanceFromRight = 0U,
            });
    }

    /// <summary>
    /// Suratni KATAKNI TO'LDIRADIGAN qilib markazdan qirqish (<c>a:srcRect</c>) qiymatlari.
    /// Qiymatlar foizning mingdan bir ulushida (100000 = 100%). Qirqishni Word/LibreOffice
    /// o'zi bajaradi — rasmni qayta chizadigan kutubxona KERAK EMAS.
    /// </summary>
    internal static (int L, int T, int R, int B) CoverCrop(int srcW, int srcH, long boxCx, long boxCy)
    {
        if (srcW <= 0 || srcH <= 0 || boxCx <= 0 || boxCy <= 0) return default;
        var target = boxCx / (double)boxCy;
        var source = srcW / (double)srcH;
        if (Math.Abs(target - source) < 0.0005) return default;   // deyarli bir xil — qirqish shart emas

        if (source > target)
        {
            // Manba KENGROQ — chap va o'ngdan teng qirqamiz (yuz o'rtada qoladi).
            var cut = (int)Math.Round((1 - target / source) / 2 * 100000);
            return (cut, 0, cut, 0);
        }
        // Manba BALANDROQ — tepa va pastdan teng qirqamiz.
        var cutV = (int)Math.Round((1 - source / target) / 2 * 100000);
        return (0, cutV, 0, cutV);
    }

    /// <summary>Hujjatda rasm o'rni bormi (andoza yuklanganda adminni ogohlantirish uchun).</summary>
    public static bool HasPhotoPlaceholder(byte[] docxBytes)
    {
        try
        {
            using var ms = new MemoryStream(docxBytes);
            using var doc = WordprocessingDocument.Open(ms, false);
            var main = doc.MainDocumentPart;
            if (main?.Document is null) return false;
            if (FindPhotoDrawing(main.Document) is not null) return true;
            return main.HeaderParts.Any(h => FindPhotoDrawing(h.Header) is not null)
                || main.FooterParts.Any(f => FindPhotoDrawing(f.Footer) is not null);
        }
        catch { return false; }   // buzuq fayl — "yo'q" deb hisoblaymiz, yuklashni bloklamaymiz
    }

    private static Drawing? FindPhotoDrawing(OpenXmlElement root)
    {
        var drawings = root.Descendants<Drawing>().ToList();
        if (drawings.Count == 0) return null;

        // 1) Nomi/alt-matni belgilangani.
        foreach (var d in drawings)
        {
            var props = d.Descendants<DW.DocProperties>();
            var label = string.Join(" ", props.Select(p => $"{p.Name?.Value} {p.Description?.Value}"))
                .ToLowerInvariant();
            if (PhotoMarkers.Any(m => label.Contains(m))) return d;
        }
        // 2) Belgi yo'q, lekin hujjatda BITTA rasm — o'sha.
        return drawings.Count == 1 ? drawings[0] : null;
    }

    private static void SwapImage(
        OpenXmlPart part, Drawing drawing, byte[] imageBytes, string contentType)
    {
        // YANGI rasm qismi qo'shiladi (mavjudining turini o'zgartirib bo'lmaydi: andozadagi
        // o'rin PNG bo'lib, surat JPEG bo'lsa baytlarni to'g'ridan-to'g'ri yozish hujjatni buzardi).
        // `AddNewPart<ImagePart>(contentType, id)` — content-type SATRI bilan ishlaydi, ya'ni
        // kutubxonaning ichki `ImagePartType` turiga bog'lanib qolmaymiz.
        var relId = "rIdPhoto" + Guid.NewGuid().ToString("N")[..8];
        var imagePart = part.AddNewPart<ImagePart>(contentType, relId);

        using (var src = new MemoryStream(imageBytes)) imagePart.FeedData(src);
        foreach (var blip in drawing.Descendants<A.Blip>()) blip.Embed = relId;

        // MUALLIF BERGAN O'LCHAM TEGILMAYDI — surat katakni to'ldiradi, ortiqchasi markazdan
        // qirqiladi. Shu sabab ramka/joylashuv aynan muallif chizganidek qoladi va rasm cho'zilmaydi.
        var (w, h) = ImageSize(imageBytes);
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        if (extent?.Cx is null || extent.Cy is null) return;
        ApplyCrop(drawing, CoverCrop(w, h, extent.Cx.Value, extent.Cy.Value));
    }

    /// <summary>Rasmning <c>a:srcRect</c> qirqimini o'rnatadi (eskisi almashtiriladi).</summary>
    private static void ApplyCrop(OpenXmlElement drawing, (int L, int T, int R, int B) crop)
    {
        foreach (var blip in drawing.Descendants<A.Blip>().ToList())
        {
            var fill = blip.Parent;
            if (fill is null) continue;
            foreach (var old in fill.Elements<A.SourceRectangle>().ToList()) old.Remove();
            if (crop == default) continue;
            fill.InsertAfter(new A.SourceRectangle
            {
                Left = crop.L, Top = crop.T, Right = crop.R, Bottom = crop.B,
            }, blip);
        }
    }

    private static string? ContentTypeOf(string? extension) =>
        (extension ?? "").ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            // webp/heic — Word ularni ishonchli ko'rsatmaydi, andozaga tegmaymiz.
            // (O'quvchi surati kameradan JPEG bo'lib keladi — StudentPhotoDialog.)
            _ => null,
        };

    /// <summary>
    /// Rasm o'lchamini FAYL SARLAVHASIDAN o'qiydi (piksel). Tashqi kutubxona shart emas —
    /// bizga faqat NISBAT kerak, rasmni qayta chizish emas. Tanilmasa (0,0).
    /// </summary>
    internal static (int Width, int Height) ImageSize(byte[] data)
    {
        // PNG: 8 baytlik imzo + IHDR (kenglik 16..19, balandlik 20..23 — big-endian).
        if (data.Length > 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return (Be32(data, 16), Be32(data, 20));

        // JPEG: segmentlarni kezib SOFn (0xC0..0xCF, restart/DHT/DAC mustasno) markerini topamiz.
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                // To'ldiruvchi 0xFF, SOI va RSTn — uzunliksiz markerlar.
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
                var segLen = (data[i + 2] << 8) | data[i + 3];
                if (segLen < 2) break;
                var isSof = (marker >= 0xC0 && marker <= 0xCF)
                            && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof) return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6]);
                i += 2 + segLen;
            }
        }
        return (0, 0);
    }

    private static int Be32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    private static void ReplaceIn(OpenXmlElement root, IDictionary<string, string> tokens)
    {
        foreach (var para in root.Descendants<Paragraph>())
        {
            var texts = para.Descendants<Text>().ToList();
            if (texts.Count == 0) continue;
            var combined = string.Concat(texts.Select(t => t.Text));
            if (!combined.Contains('@')) continue;
            var replaced = Apply(combined, tokens);
            if (replaced == combined) continue;
            texts[0].Text = replaced;
            texts[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < texts.Count; i++) texts[i].Text = "";
        }
    }
}
