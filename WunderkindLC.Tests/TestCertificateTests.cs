using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// TEST SERTIFIKATI — Word andozasidan sertifikat yaratish
/// (<see cref="TestCertificateService"/> + <see cref="DocxTemplate"/>).
///
/// <para>PDF konvertatsiyasi (LibreOffice) test muhitida BO'LMASLIGI mumkin — shuning uchun
/// testlar "PDF yo'q" holatini ham kutadi: sertifikat baribir yaratiladi, faqat
/// <c>Status="docx"</c> bo'ladi. Bu ataylab: server LibreOfficesiz ham ishlashi kerak.</para>
/// </summary>
public class TestCertificateTests : IDisposable
{
    private sealed class FakeEnv(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "WunderkindLC.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wunderkind-testcert", Guid.NewGuid().ToString("N"));

    public TestCertificateTests() => Directory.CreateDirectory(Path.Combine(_root, "uploads"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* tozalash xatosi natijaga ta'sir qilmasin */ }
    }

    private TestCertificateService Service() =>
        new(new FakeEnv(_root), new DocxToPdfConverter(NullLogger<DocxToPdfConverter>.Instance));

    // =============================================================================================
    //  Yordamchilar — soxta .docx andoza
    // =============================================================================================

    /// <summary>Berilgan paragraflardan .docx yasaydi (har paragraf — bitta run).</summary>
    private static byte[] MakeDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var p in paragraphs)
                body.Append(new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Bitta paragrafni bir NECHTA runga bo'lib yozadi — Word real hayotda shunday qiladi
    /// (imlo/formatlash sabab), va aynan shu holat oddiy Replace'ni ishlatmay qo'yadi.</summary>
    private static byte[] MakeDocxSplitRuns(params string[] pieces)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var para = new Paragraph();
            foreach (var piece in pieces)
                para.Append(new Run(new Text(piece) { Space = SpaceProcessingModeValues.Preserve }));
            main.Document = new Document(new Body(para));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Bitta paragraf, matni QALIN qilib belgilangan run bilan (formatlash sinovi uchun).</summary>
    private static byte[] MakeDocxBold(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })
            {
                RunProperties = new RunProperties(new Bold()),
            };
            main.Document = new Document(new Body(new Paragraph(run)));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static string ReadText(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        return string.Concat(doc.MainDocumentPart!.Document.Descendants<Text>().Select(t => t.Text));
    }

    /// <summary>Diskka andoza yozadi va "/uploads/..." manzilini qaytaradi.</summary>
    private string WriteTemplateFile(byte[] bytes)
    {
        var name = $"tpl-{Guid.NewGuid():N}.docx";
        File.WriteAllBytes(Path.Combine(_root, "uploads", name), bytes);
        return "/uploads/" + name;
    }

    // =============================================================================================
    //  1) SOF MANTIQ — token almashtirish
    // =============================================================================================

    [Fact]
    public void Apply_TokenlarniAlmashtiradi_NomalumlariniQoldiradi()
    {
        var tokens = new Dictionary<string, string> { ["@fish"] = "Ali Valiyev", ["@ball"] = "85" };

        Assert.Equal("Ali Valiyev — 85 ball", DocxTemplate.Apply("@fish — @ball ball", tokens));
        // Noma'lum token O'Z HOLICHA qoladi — andoza muallifi xatoni ko'rsin (jimgina o'chmasin).
        Assert.Equal("@yoq", DocxTemplate.Apply("@yoq", tokens));
    }

    [Fact]
    public void Apply_RaqamliMatnTokenDebOqilmaydi()
    {
        // "@2026" token emas (regex faqat harf/pastki chiziq) — sanalar buzilmasin.
        Assert.Equal("@2026", DocxTemplate.Apply("@2026", new Dictionary<string, string>()));
    }

    [Fact]
    public void Fill_BolinganRUNlardagiTokenniHamAlmashtiradi()
    {
        // Word "@fish" ni "@fi" + "sh" qilib bo'lib yozgan — paragraf darajasidagi almashtirish shart.
        var docx = MakeDocxSplitRuns("Hurmatli ", "@fi", "sh", ", tabriklaymiz!");
        var filled = DocxTemplate.Fill(docx, new Dictionary<string, string> { ["@fish"] = "Ali Valiyev" });

        Assert.Equal("Hurmatli Ali Valiyev, tabriklaymiz!", ReadText(filled));
    }

    [Fact]
    public void FindTokens_TakrorlanmaydiganRoyxat()
    {
        var found = DocxTemplate.FindTokens("@fish @ball @fish");
        Assert.Equal(2, found.Count);
        Assert.Contains("@fish", found);
        Assert.Contains("@ball", found);
    }

    [Fact]
    public void Tokenlar_KatalogiBoshEmas_VaHammasiBirXilShaklda()
    {
        Assert.NotEmpty(TestCertificateService.Tokens);
        Assert.All(TestCertificateService.Tokens, t =>
        {
            Assert.StartsWith("@", t.Token);
            Assert.NotEmpty(t.Label);
        });
        // Takroriy token bo'lmasin (ikkita bir xil qator UI'da chalkashtirardi).
        Assert.Equal(TestCertificateService.Tokens.Count,
            TestCertificateService.Tokens.Select(t => t.Token).Distinct().Count());
    }

    // =============================================================================================
    //  1b) RASM O'RNI — o'quvchi surati
    // =============================================================================================

    /// <summary>Minimal, HAQIQIY PNG (1×2 piksel) — nisbat 1:2, o'lcham hisobini tekshirish uchun.</summary>
    private static byte[] Png1x2() =>
    [
        0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
        0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        0, 0, 0, 1,     // kenglik = 1
        0, 0, 0, 2,     // balandlik = 2
        8, 6, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>Rasm o'rni (100×100 EMU katak) bo'lgan .docx — muallif Word'da qo'ygan rasm kabi.</summary>
    private static byte[] MakeDocxWithImage(string? altText, long cx = 1000, long cy = 1000)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var img = main.AddNewPart<ImagePart>("image/png", "rIdSeed");
            using (var s = new MemoryStream(Png1x2())) img.FeedData(s);

            var drawing = new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cx, Cy = cy },
                    new DW.DocProperties { Id = 1U, Name = altText ?? "Picture 1", Description = altText ?? "" },
                    new A.Graphic(new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = altText ?? "p.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = "rIdSeed" },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));

            main.Document = new Document(new Body(new Paragraph(new Run(drawing))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static (int W, int H) ImageBytesIn(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var main = doc.MainDocumentPart!;
        // Blip qaysi qismga ishora qilyapti — o'shani o'qiymiz.
        var relId = main.Document.Descendants<A.Blip>().First().Embed!.Value!;
        var part = (ImagePart)main.GetPartById(relId);
        using var s = part.GetStream();
        using var mem = new MemoryStream();
        s.CopyTo(mem);
        return DocxTemplate.ImageSize(mem.ToArray());
    }

    [Fact]
    public void ImageSize_PNGVaJPEGolchaminiOqiydi()
    {
        Assert.Equal((1, 2), DocxTemplate.ImageSize(Png1x2()));
        Assert.Equal((0, 0), DocxTemplate.ImageSize([1, 2, 3]));   // tanilmasa — yiqilmaydi
    }

    [Fact]
    public void ApplyPhoto_BITTArasmBolsa_AltMatnSHARTEMAS()
    {
        var docx = MakeDocxWithImage(altText: null);

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(4, 4), ".jpg");

        Assert.Equal((4, 4), ImageBytesIn(result));
    }

    [Fact]
    public void ApplyPhoto_RasmORNI_MUALLIFOLCHAMISAQLANADI_ortiqchasiQIRQILADI()
    {
        // Katak 1000×1000 (kvadrat), surat 1:2 (bo'yiga cho'zilgan) → tepa/pastdan qirqiladi.
        var docx = MakeDocxWithImage(altText: "rasm", cx: 1000, cy: 1000);

        var result = DocxTemplate.ApplyPhoto(docx, Png1x2(), ".png");

        using var ms = new MemoryStream(result);
        using var doc = WordprocessingDocument.Open(ms, false);
        // O'lcham TEGILMAYDI — muallif chizgan ramka o'z joyida qoladi.
        var extent = doc.MainDocumentPart!.Document.Descendants<DW.Extent>().First();
        Assert.Equal(1000L, extent.Cx!.Value);
        Assert.Equal(1000L, extent.Cy!.Value);
        // Ortiqchasi markazdan qirqiladi: 1:2 rasmning yarmi ko'rinadi → har tomondan 25%.
        var crop = doc.MainDocumentPart.Document.Descendants<A.SourceRectangle>().Single();
        Assert.Equal(25000, crop.Top!.Value);
        Assert.Equal(25000, crop.Bottom!.Value);
        Assert.Equal(0, crop.Left!.Value);      // yon tomonlar qirqilmaydi
        Assert.Equal(0, crop.Right!.Value);
    }

    [Fact]
    public void ApplyPhoto_QollanmaydiganFormat_ANDOZAGATEGILMAYDI()
    {
        var docx = MakeDocxWithImage(altText: "rasm");

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(4, 4), ".webp");

        Assert.Equal(docx, result);                 // bayt-bayt o'zgarmagan
        Assert.Equal((1, 2), ImageBytesIn(result)); // eski (andozadagi) rasm joyida
    }

    // ---- `@rasm` MATN BELGISI ----

    [Fact]
    public void CoverCrop_KengManbaCHAPONGDAN_BalandManbaTEPAPASTDAN()
    {
        // Kvadrat surat (1:1) 185×260 katakda → chap/o'ngdan qirqiladi.
        var wide = DocxTemplate.CoverCrop(100, 100, 185, 260);
        Assert.True(wide.L > 0 && wide.R > 0 && wide.T == 0 && wide.B == 0);
        Assert.Equal(wide.L, wide.R);   // markazdan — teng

        // Bo'yiga cho'zilgan surat kvadrat katakda → tepa/pastdan.
        var tall = DocxTemplate.CoverCrop(100, 200, 100, 100);
        Assert.True(tall.T > 0 && tall.B > 0 && tall.L == 0 && tall.R == 0);

        // Nisbat bir xil — qirqish shart emas.
        Assert.Equal(default, DocxTemplate.CoverCrop(185, 260, 185, 260));
    }

    [Fact]
    public void ApplyPhoto_RASMbelgisi_185x260olchamdaQoyiladi()
    {
        var docx = MakeDocx("Hurmatli @fish", "@rasm");

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(300, 300), ".jpg");

        using var ms = new MemoryStream(result);
        using var doc = WordprocessingDocument.Open(ms, false);
        var extent = doc.MainDocumentPart!.Document.Descendants<DW.Extent>().Single();
        Assert.Equal(185L * 9525, extent.Cx!.Value);    // 96 DPI: 1 px = 9525 EMU
        Assert.Equal(260L * 9525, extent.Cy!.Value);
        // Belgining o'zi matnda qolmaydi.
        Assert.DoesNotContain("@rasm", ReadText(result));
        Assert.Equal((300, 300), ImageBytesIn(result));
    }

    [Fact]
    public void ApplyPhoto_RASMbelgisi_BOLINGANRUNlardaHamIshlaydi()
    {
        // Word "@rasm" ni "@ra" + "sm" qilib bo'lib yozgan bo'lishi mumkin.
        var docx = MakeDocxSplitRuns("Surat: ", "@ra", "sm", " (o'quvchi)");

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(4, 4), ".jpg");

        var text = ReadText(result);
        Assert.DoesNotContain("@rasm", text);
        Assert.Contains("Surat: ", text);
        Assert.Contains("(o'quvchi)", text);    // belgidan keyingi matn yo'qolmaydi
        Assert.Equal((4, 4), ImageBytesIn(result));
    }

    [Fact]
    public void ApplyPhoto_BittaParagrafdaIKKITAbelgi_IKKALASIHAMRasmBoladi()
    {
        // Bir qatorda ikkita surat o'rni. Ilgari faqat BIRINCHISI almashtirilib, ikkinchisi
        // sertifikatda "@rasm" YOZUVI bo'lib chop etilardi.
        var docx = MakeDocx("@rasm va @rasm");

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(4, 4), ".jpg");

        Assert.DoesNotContain("@rasm", ReadText(result));
        Assert.Contains(" va ", ReadText(result));
        using var ms = new MemoryStream(result);
        using var doc = WordprocessingDocument.Open(ms, false);
        Assert.Equal(2, doc.MainDocumentPart!.Document.Descendants<DW.Extent>().Count());
    }

    [Fact]
    public void ApplyPhoto_BelgidanKEYINGIMATN_FORMATLASHNIYoqotmaydi()
    {
        // "@rasm @fish" — surat qo'yilgach ism shablondagi QALIN shriftda qolishi kerak edi,
        // lekin keyingi matn yangi run'da qayta yaratilgani uchun standart shriftga tushib qolardi.
        var docx = MakeDocxBold("@rasm Valiyev Ali");

        var result = DocxTemplate.ApplyPhoto(docx, MakeJpeg(4, 4), ".jpg");

        using var ms = new MemoryStream(result);
        using var doc = WordprocessingDocument.Open(ms, false);
        var textRun = doc.MainDocumentPart!.Document.Descendants<Run>()
            .Single(r => r.Descendants<Text>().Any(t => t.Text.Contains("Valiyev Ali")));
        Assert.NotNull(textRun.RunProperties?.Bold);
    }

    [Fact]
    public void ApplyPhoto_SURATYOQ_belgiOLIBTASHLANADI()
    {
        // Sertifikatda "@rasm" yozuvi qolib ketmasligi kerak.
        var docx = MakeDocx("Surat: @rasm shu yerda");

        var result = DocxTemplate.ApplyPhoto(docx, null, null);

        var text = ReadText(result);
        Assert.DoesNotContain("@rasm", text);
        Assert.Contains("Surat: ", text);
        Assert.Contains("shu yerda", text);
    }

    [Fact]
    public void HasPhotoPlaceholder_RasmsizAndozadaFALSE()
    {
        Assert.False(DocxTemplate.HasPhotoPlaceholder(MakeDocx("@fish")));
        Assert.True(DocxTemplate.HasPhotoPlaceholder(MakeDocxWithImage("rasm")));
    }

    /// <summary>Haqiqiy (minimal) JPEG — SOF0 sarlavhasi bilan, o'lchami berilganidek.</summary>
    private static byte[] MakeJpeg(int w, int h)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08 };
        b.Add((byte)(h >> 8)); b.Add((byte)(h & 0xFF));
        b.Add((byte)(w >> 8)); b.Add((byte)(w & 0xFF));
        b.AddRange(new byte[] { 0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 });
        b.AddRange(new byte[] { 0xFF, 0xD9 });
        return [.. b];
    }

    // =============================================================================================
    //  2) BAZA — andozalar
    // =============================================================================================

    [Fact]
    public async Task Andoza_BirinchisiAvtomatikSTANDARTBoladi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var url = WriteTemplateFile(MakeDocx("@fish"));

        var (dto, err) = await svc.CreateTemplateAsync(
            db.Context, new TestCertificateTemplatePayload("Birinchi", url, "a.docx"), "Admin");

        Assert.Null(err);
        Assert.True(dto!.IsDefault);   // birinchi shablon — hech kim tanlamasa ham ishlasin
    }

    [Fact]
    public async Task Andoza_YangiStandartQoyilsa_EskisidanBelgiOlinadi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (a, _) = await svc.CreateTemplateAsync(db.Context,
            new TestCertificateTemplatePayload("A", WriteTemplateFile(MakeDocx("@fish")), "a.docx"), "Admin");
        var (b, _) = await svc.CreateTemplateAsync(db.Context,
            new TestCertificateTemplatePayload("B", WriteTemplateFile(MakeDocx("@fish")), "b.docx", IsDefault: true), "Admin");

        var list = await svc.ListTemplatesAsync(db.Context);
        Assert.False(list.Single(t => t.Id == a!.Id).IsDefault);
        Assert.True(list.Single(t => t.Id == b!.Id).IsDefault);
    }

    [Fact]
    public async Task Andoza_FaqatDocxQabulQilinadi()
    {
        using var db = TestDb.Sqlite();
        var (_, err) = await Service().CreateTemplateAsync(db.Context,
            new TestCertificateTemplatePayload("PDF andoza", "/uploads/x.pdf", "x.pdf"), "Admin");

        Assert.NotNull(err);
        Assert.Contains(".docx", err);
    }

    [Fact]
    public async Task Andoza_SertifikatBerilganBolsa_OCHIRILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var svc = Service();
        var (tpl, _) = await svc.CreateTemplateAsync(ctx,
            new TestCertificateTemplatePayload("A", WriteTemplateFile(MakeDocx("@fish")), "a.docx"), "Admin");
        // FK bor — sertifikat REAL testga bog'lanishi shart.
        var test = new TestResult { GroupId = "g1", Name = "T", Date = "2026-08-04", MaxScore = 10 };
        ctx.TestResults.Add(test);
        ctx.TestCertificates.Add(new TestCertificate { TestResultId = test.Id, StudentId = "s1", TemplateId = tpl!.Id });
        await ctx.SaveChangesAsync();

        var err = await svc.DeleteTemplateAsync(ctx, tpl.Id);

        Assert.NotNull(err);
        Assert.Contains("nofaol", err);
    }

    // =============================================================================================
    //  3) BAZA — sertifikat yaratish
    // =============================================================================================

    private async Task<(TestResult Test, Student A, Student B)> SeedTestAsync(
        TestDb db, TestCertificateService svc, bool certificateEnabled = true)
    {
        var ctx = db.Context;
        var course = new Subject { Name = "Ingliz tili" };
        var teacher = new Teacher { FullName = "Nodira Karimova" };
        var group = new Group { Name = "A1-2", CourseId = course.Id, TeacherId = teacher.Id };
        var a = new Student { FullName = "Ali Valiyev" };
        var b = new Student { FullName = "Vali Aliyev" };
        var test = new TestResult
        {
            GroupId = group.Id, Name = "Unit 3", Date = "2026-08-04", MaxScore = 100,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            CertificateEnabled = certificateEnabled,
        };
        ctx.Subjects.Add(course);
        ctx.Teachers.Add(teacher);
        ctx.Classes.Add(group);
        ctx.Students.AddRange(a, b);
        ctx.TestResults.Add(test);
        ctx.TestScores.AddRange(
            new TestScore { TestResultId = test.Id, StudentId = a.Id, Score = 90 },
            new TestScore { TestResultId = test.Id, StudentId = b.Id, Score = 70 });
        await ctx.SaveChangesAsync();

        await svc.CreateTemplateAsync(ctx, new TestCertificateTemplatePayload(
            "Standart", WriteTemplateFile(MakeDocx("@fish", "@ball / @maksball", "@foiz", "@orin", "@raqam", "@kurs")),
            "cert.docx"), "Admin");
        return (test, a, b);
    }

    [Fact]
    public async Task Yaratish_HarBallliOquvchigaBittadan_VaTokenlarQoyiladi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, a, _) = await SeedTestAsync(db, svc);

        var (items, err) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        Assert.Null(err);
        Assert.Equal(2, items.Count);

        var top = items.Single(c => c.StudentId == a.Id);
        Assert.Equal("Ali Valiyev", top.StudentName);
        Assert.Equal(90, top.Score);
        Assert.Equal(90, top.Percent);
        Assert.StartsWith("SRT-", top.Number);
        Assert.NotEmpty(top.DocxUrl);

        // Word fayl haqiqatan yozilgan va tokenlar almashtirilgan.
        var path = Path.Combine(_root, "uploads", "certificates", Path.GetFileName(top.DocxUrl));
        Assert.True(File.Exists(path));
        var text = ReadText(await File.ReadAllBytesAsync(path));
        Assert.Contains("Ali Valiyev", text);
        Assert.Contains("90 / 100", text);      // @ball / @maksball — ortiqcha nol yo'q
        Assert.Contains("Ingliz tili", text);   // @kurs guruh kursidan olindi
        Assert.DoesNotContain("@fish", text);

        // LibreOffice bo'lmasa PDF bo'lmaydi, LEKIN sertifikat baribir yaratilgan.
        Assert.Equal(DocxToPdfConverter.IsAvailable
            ? TestCertificateService.StatusReady
            : TestCertificateService.StatusDocxOnly, top.Status);
    }

    [Fact]
    public async Task Yaratish_HarKIMOZFayliniOladi_INDEKSSILJIMAYDI()
    {
        // Fayllar bitta LibreOffice chaqiruvida TO'PLAM bo'lib konvertatsiya qilinadi
        // (ConvertManyAsync) — natijalar kirish TARTIBIGA qat'iy mos kelishi shart, aks holda
        // Ali'ning sertifikati Vali'ga tegib ketardi. Shu yerda aynan shuni tekshiramiz.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, a, b) = await SeedTestAsync(db, svc);

        var (items, _) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        string TextOf(string studentId)
        {
            var c = items.Single(x => x.StudentId == studentId);
            var path = Path.Combine(_root, "uploads", "certificates", Path.GetFileName(c.DocxUrl));
            return ReadText(File.ReadAllBytes(path));
        }

        var aText = TextOf(a.Id);
        var bText = TextOf(b.Id);
        Assert.Contains("Ali Valiyev", aText);
        Assert.DoesNotContain("Vali Aliyev", aText);
        Assert.Contains("90 / 100", aText);
        Assert.Contains("Vali Aliyev", bText);
        Assert.Contains("70 / 100", bText);
        // Fayllar ham har xil bo'lishi kerak (bitta fayl ikki marta yozilib qolmasin).
        Assert.NotEqual(items.Single(x => x.StudentId == a.Id).DocxUrl,
                        items.Single(x => x.StudentId == b.Id).DocxUrl);
    }

    [Fact]
    public async Task Yaratish_IKKINCHIMartaNusxaYARATMAYDI()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, _, _) = await SeedTestAsync(db, svc);

        var (first, _) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");
        var (second, _) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, await db.Context.TestCertificates.CountAsync());
        // Raqam SAQLANADI — qayta yaratish sertifikat raqamini o'zgartirmasin.
        Assert.Equal(
            first.OrderBy(c => c.StudentId).Select(c => c.Number),
            second.OrderBy(c => c.StudentId).Select(c => c.Number));
    }

    [Fact]
    public async Task Yaratish_BallOZGARSA_SertifikatYANGILANADI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var svc = Service();
        var (test, a, _) = await SeedTestAsync(db, svc);
        await svc.GenerateForTestAsync(ctx, test.Id, "Admin");

        var score = await ctx.TestScores.FirstAsync(s => s.StudentId == a.Id);
        score.Score = 50;
        await ctx.SaveChangesAsync();
        var (again, _) = await svc.GenerateForTestAsync(ctx, test.Id, "Admin");

        var cert = again.Single(c => c.StudentId == a.Id);
        Assert.Equal(50, cert.Score);
        Assert.Equal(50, cert.Percent);
    }

    [Fact]
    public async Task Yaratish_PtichkaBELGILANMAGAN_XatoQaytadi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, _, _) = await SeedTestAsync(db, svc, certificateEnabled: false);

        var (items, err) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        Assert.Empty(items);
        Assert.NotNull(err);
        Assert.Contains("yoqilmagan", err);
    }

    [Fact]
    public async Task Yaratish_ANDOZAYOQ_TushunarliXato()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var group = new Group { Name = "A1" };
        var test = new TestResult
        {
            GroupId = group.Id, Name = "T", Date = "2026-08-04", MaxScore = 10,
            CertificateEnabled = true,
        };
        var st = new Student { FullName = "Ali" };
        ctx.Classes.Add(group);
        ctx.Students.Add(st);
        ctx.TestResults.Add(test);
        ctx.TestScores.Add(new TestScore { TestResultId = test.Id, StudentId = st.Id, Score = 8 });
        await ctx.SaveChangesAsync();

        var (_, err) = await Service().GenerateForTestAsync(ctx, test.Id, "Admin");

        Assert.NotNull(err);
        Assert.Contains("shablon", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Yaratish_BallYOQ_XatoQaytadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var svc = Service();
        var (test, _, _) = await SeedTestAsync(db, svc);
        ctx.TestScores.RemoveRange(await ctx.TestScores.ToListAsync());
        await ctx.SaveChangesAsync();

        var (_, err) = await svc.GenerateForTestAsync(ctx, test.Id, "Admin");

        Assert.NotNull(err);
        Assert.Contains("ball", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task YuklabOlish_FaylNomiOquvchiIsmiBilan()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, a, _) = await SeedTestAsync(db, svc);
        var (items, _) = await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");
        var cert = items.Single(c => c.StudentId == a.Id);

        var file = await svc.ReadFileAsync(db.Context, cert.Id);

        Assert.NotNull(file);
        Assert.Contains("Ali Valiyev", file!.Value.FileName);
        Assert.NotEmpty(file.Value.Bytes);
    }

    [Fact]
    public async Task ZIP_BarchaSertifikatlarniQadoqlaydi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var (test, _, _) = await SeedTestAsync(db, svc);
        await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        var zip = await svc.ZipForTestAsync(db.Context, test.Id);

        Assert.NotNull(zip);
        using var ms = new MemoryStream(zip!.Value.Bytes);
        using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
    }

    // =============================================================================================
    //  4) BO'LAKLAB YARATISH va FON ISHI
    // =============================================================================================

    /// <summary>Berilgan sonda o'quvchi + ball bilan test tayyorlaydi (bo'lak chegarasini sinash uchun).</summary>
    private async Task<TestResult> SeedManyAsync(TestDb db, TestCertificateService svc, int studentCount)
    {
        var ctx = db.Context;
        var group = new Group { Name = "Katta guruh" };
        var test = new TestResult
        {
            GroupId = group.Id, Name = "Yakuniy", Date = "2026-08-04", MaxScore = 100,
            CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            CertificateEnabled = true,
        };
        ctx.Classes.Add(group);
        ctx.TestResults.Add(test);
        for (var i = 0; i < studentCount; i++)
        {
            var s = new Student { FullName = $"O'quvchi {i:D2}" };
            ctx.Students.Add(s);
            // Ball har xil — o'rin hisoblanishi ham tabiiy bo'lsin.
            ctx.TestScores.Add(new TestScore { TestResultId = test.Id, StudentId = s.Id, Score = 100 - i });
        }
        await ctx.SaveChangesAsync();

        await svc.CreateTemplateAsync(ctx, new TestCertificateTemplatePayload(
            "Standart", WriteTemplateFile(MakeDocx("@fish", "@ball")), "cert.docx"), "Admin");
        return test;
    }

    [Fact]
    public async Task Yaratish_BOLAKLABBajariladi_ProgressHarBolakdanKeyinKeladi()
    {
        // 12 o'quvchi, bo'lak 5 ta → 5 + 5 + 2. LibreOffice'ni har fayl uchun qayta ochish qimmat,
        // hammasini birdan chizish esa 1 GB serverda xotirani to'ldiradi — shu sabab bo'laklab.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var test = await SeedManyAsync(db, svc, 12);

        var progress = new List<int>();
        var (items, err) = await svc.GenerateForTestAsync(
            db.Context, test.Id, "Admin", onProgress: done => progress.Add(done));

        Assert.Null(err);
        Assert.Equal(12, items.Count);
        Assert.Equal(TestCertificateService.ChunkSize, 5);   // hujjatlashtirilgan qiymat o'zgarmasin
        Assert.Equal(new[] { 5, 10, 12 }, progress);
    }

    [Fact]
    public async Task Yaratish_HarBolakdanKeyin_BAZAGAYoziladi()
    {
        // Eng muhim xossa: ish tugashini kutmasdan tayyor sertifikatlar ro'yxatda ko'rinishi kerak.
        // Buning uchun har bo'lak o'z SaveChanges'i bilan yakunlanadi.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var test = await SeedManyAsync(db, svc, 12);

        var seenInDb = new List<int>();
        await svc.GenerateForTestAsync(db.Context, test.Id, "Admin",
            onProgress: _ => seenInDb.Add(db.Context.TestCertificates.Count(c => c.TestResultId == test.Id)));

        // Ya'ni birinchi bo'lak tugagach bazada ALLAQACHON 5 ta yozuv bor edi.
        Assert.Equal(new[] { 5, 10, 12 }, seenInDb);
    }

    [Fact]
    public async Task KutilganSoni_HechNarsaYARATMASDANAytadi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var test = await SeedManyAsync(db, svc, 7);

        var (total, err) = await svc.ExpectedCountAsync(db.Context, test.Id);

        Assert.Null(err);
        Assert.Equal(7, total);
        // "Oldindan aytish" — yaratish EMAS: bazada hech narsa paydo bo'lmasligi kerak.
        Assert.Equal(0, await db.Context.TestCertificates.CountAsync());
    }

    [Fact]
    public async Task KutilganSoni_ShablonYOQBolsa_XatoQaytaradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var test = new TestResult
        {
            GroupId = "g1", Name = "T", Date = "2026-08-04", MaxScore = 10, CertificateEnabled = true,
        };
        ctx.TestResults.Add(test);
        ctx.SaveChanges();

        var (total, err) = await Service().ExpectedCountAsync(ctx, test.Id);

        Assert.Equal(0, total);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task Shablon_OZNUSXASINIOladi_BEGONAFAYLOCHIRILMAYDI()
    {
        // `/uploads` — yagona tekis papka (shartnomalar, skanlar ham shu yerda). Ilgari shablon
        // istalgan mavjud .docx manzilini ko'rsata olardi va o'chirilganda o'sha BEGONA fayl
        // diskdan o'chib ketardi. Endi shablon o'z nusxasiga egalik qiladi.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var foreignUrl = WriteTemplateFile(MakeDocx("Shartnoma andozasi"));
        var foreignPath = Path.Combine(_root, "uploads", Path.GetFileName(foreignUrl));

        var (tpl, err) = await svc.CreateTemplateAsync(db.Context,
            new TestCertificateTemplatePayload("A", foreignUrl, "a.docx"), "Admin");

        Assert.Null(err);
        Assert.NotEqual(foreignUrl, tpl!.FileUrl);      // o'z nusxasi olindi
        Assert.True(File.Exists(foreignPath));

        Assert.Null(await svc.DeleteTemplateAsync(db.Context, tpl.Id));

        // Eng muhimi: begona fayl JOYIDA turibdi, shablonning o'z nusxasi esa o'chdi.
        Assert.True(File.Exists(foreignPath));
        Assert.False(File.Exists(Path.Combine(_root, "uploads", Path.GetFileName(tpl.FileUrl))));
    }

    [Fact]
    public async Task Yaratish_BALLOCHIRILGANoquvchiningSertifikati_OLIBTASHLANADI()
    {
        // Ssenariy: noto'g'ri o'quvchiga ball qo'yildi → sertifikat chiqdi → ball tozalandi →
        // qayta yaratildi. Ilgari noto'g'ri sertifikat ro'yxatda ham, ZIP da ham qolib ketardi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var svc = Service();
        var (test, _, b) = await SeedTestAsync(db, svc);
        var (first, _) = await svc.GenerateForTestAsync(ctx, test.Id, "Admin");
        Assert.Equal(2, first.Count);
        var removed = first.Single(c => c.StudentId == b.Id);

        // "b" o'quvchining bali tozalandi.
        ctx.TestScores.RemoveRange(ctx.TestScores.Where(s => s.TestResultId == test.Id && s.StudentId == b.Id));
        await ctx.SaveChangesAsync();

        var (again, err) = await svc.GenerateForTestAsync(ctx, test.Id, "Admin");

        Assert.Null(err);
        Assert.Single(again);
        Assert.Equal(1, await ctx.TestCertificates.CountAsync(c => c.TestResultId == test.Id));
        // Fayli ham qolmaydi (ombor shishmasin).
        Assert.False(File.Exists(Path.Combine(_root, "uploads", "certificates", Path.GetFileName(removed.DocxUrl))));
    }

    /// <summary>Fon ishi uchun eng kichik DI konteyneri (u scope'ni shu yerdan ochadi).</summary>
    private static TestCertificateJobs Jobs(TestDb db, TestCertificateService svc)
    {
        var sp = new ServiceCollection()
            .AddSingleton<IAppDbContext>(db.Context)
            .AddSingleton(svc)
            .BuildServiceProvider();
        return new TestCertificateJobs(sp, NullLogger<TestCertificateJobs>.Instance);
    }

    [Fact]
    public async Task FonIshi_DARHOLQaytadi_SoOngraTugaydi()
    {
        using var db = TestDb.Sqlite();
        var svc = Service();
        var jobs = Jobs(db, svc);
        var (test, _, _) = await SeedTestAsync(db, svc);

        var (job, err) = await jobs.StartAsync(db.Context, svc, test.Id, "Admin");

        // Boshlash so'rovi kutmaydi: hali yaratilmagan bo'lsa ham javob qaytdi.
        Assert.Null(err);
        Assert.True(job.Running);
        Assert.Equal(2, job.Total);

        // Fon ishi tugashini kutamiz (cheklangan urinish — test osilib qolmasin).
        for (var i = 0; i < 200 && jobs.Status(test.Id)?.Running == true; i++)
            await Task.Delay(50);

        var final = await jobs.StatusWithItemsAsync(db.Context, test.Id);
        Assert.False(final.Running);
        Assert.Null(final.Error);
        Assert.Equal(2, final.Done);
        Assert.Equal(2, final.Items.Count);
    }

    [Fact]
    public async Task FonIshi_TekshiruvXATOSISorovIchidaQaytadi()
    {
        // Shablon yo'q / ball kiritilmagan kabi xatolar fonga O'TKAZILMAYDI — foydalanuvchi ularni
        // "boshlandi" degan javob o'rniga darhol ko'rishi kerak.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var jobs = Jobs(db, svc);
        var (test, _, _) = await SeedTestAsync(db, svc, certificateEnabled: false);

        var (job, err) = await jobs.StartAsync(db.Context, svc, test.Id, "Admin");

        Assert.NotNull(err);
        Assert.False(job.Running);
        Assert.Null(jobs.Status(test.Id));      // ish umuman ro'yxatga olinmadi
    }

    [Fact]
    public async Task FonIshi_ISHYOQBolsa_BazadagiRoyxatQaytadi()
    {
        // Server qayta ishga tushsa xotiradagi holat yo'qoladi — UI shunda ham to'g'ri ko'rinishi
        // kerak: "yaratilmayapti" + bazadagi mavjud sertifikatlar "hammasi tayyor" deb.
        using var db = TestDb.Sqlite();
        var svc = Service();
        var jobs = Jobs(db, svc);
        var (test, _, _) = await SeedTestAsync(db, svc);
        await svc.GenerateForTestAsync(db.Context, test.Id, "Admin");

        var status = await jobs.StatusWithItemsAsync(db.Context, test.Id);

        Assert.False(status.Running);
        Assert.Equal(2, status.Total);
        Assert.Equal(2, status.Done);
        Assert.Equal(2, status.Items.Count);
    }
}
