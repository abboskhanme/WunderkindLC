using System.Text;
using System.Text.RegularExpressions;
using WunderkindLC.Application.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="UploadGuard"/> — fayl yuklashning xavfsizlik darvozasi. Ikkita hujumni to'sadi:
/// <list type="bullet">
///   <item>SAQLANGAN XSS — <c>.svg</c>/<c>.html</c> kabi brauzerda skript ishga tushira oladigan
///         fayl yuklab, keyin uning <c>/uploads/...</c> havolasini yuborish;</item>
///   <item>YO'L BUZISH (path traversal) — <c>../../etc/passwd</c> kabi nom bilan serverdagi
///         boshqa faylni ustiga yozish. <see cref="UploadGuard.SafeName"/> foydalanuvchi
///         nomidan FAQAT kengaytmani oladi.</item>
/// </list>
/// </summary>
public class UploadGuardTests
{
    /// <summary>
    /// Eng sodda <see cref="IFormFile"/> soxtasi — testga faqat <c>FileName</c> va <c>Length</c>
    /// kerak, shuning uchun tashqi mock kutubxonasi ishlatilmaydi.
    /// </summary>
    private sealed class FakeFormFile : IFormFile
    {
        public FakeFormFile(string fileName, long length = 1024)
        {
            FileName = fileName;
            Length = length;
        }

        public string FileName { get; }
        public long Length { get; }
        public string ContentType { get; set; } = "application/octet-stream";
        public string ContentDisposition { get; set; } = "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string Name { get; set; } = "file";

        public Stream OpenReadStream() => new MemoryStream(Encoding.UTF8.GetBytes("test"));
        public void CopyTo(Stream target) => OpenReadStream().CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken ct = default) =>
            OpenReadStream().CopyToAsync(target, ct);
    }

    /* =========================================================================================
     *  Validate — bo'sh / hajm
     * ========================================================================================= */

    [Fact]
    public void Validate_FaylNull_Xato()
    {
        Assert.Equal("Fayl bo'sh", UploadGuard.Validate(null));
    }

    [Fact]
    public void Validate_HajmNol_Xato()
    {
        Assert.Equal("Fayl bo'sh", UploadGuard.Validate(new FakeFormFile("rasm.jpg", 0)));
    }

    [Fact]
    public void Validate_20MbDanKatta_Xato()
    {
        var file = new FakeFormFile("video.mp4", UploadGuard.MaxBytes + 1);

        Assert.Equal("Fayl 20 MB dan katta", UploadGuard.Validate(file));
    }

    [Fact]
    public void Validate_AynanChegara_Qabul()
    {
        // Chegara "dan katta" — aynan 20 000 000 bayt o'tishi kerak (off-by-one tekshiruvi).
        var file = new FakeFormFile("video.mp4", UploadGuard.MaxBytes);

        Assert.Null(UploadGuard.Validate(file));
    }

    /* =========================================================================================
     *  Validate — kengaytma allowlist
     * ========================================================================================= */

    [Theory]
    [InlineData("cv.pdf.exe")]   // ikki kengaytmali hiyla — oxirgisi hisobga olinadi
    [InlineData("a.php")]
    [InlineData("x.svg")]        // SVG ichida <script> bo'lishi mumkin
    [InlineData("y.html")]
    [InlineData("z.htm")]
    [InlineData("shell.sh")]
    [InlineData("app.js")]
    [InlineData("t.env")]
    public void Validate_XavfliKengaytma_Rad(string fileName)
    {
        Assert.Equal("Ruxsat etilmagan fayl turi", UploadGuard.Validate(new FakeFormFile(fileName)));
    }

    [Fact]
    public void Validate_KengaytmasizNom_Rad()
    {
        Assert.Equal("Ruxsat etilmagan fayl turi", UploadGuard.Validate(new FakeFormFile("readme")));
    }

    [Fact]
    public void Validate_BoshFaylNomi_Rad()
    {
        Assert.Equal("Ruxsat etilmagan fayl turi", UploadGuard.Validate(new FakeFormFile("")));
    }

    [Theory]
    [InlineData("SCAN.PDF")]     // katta harfli kengaytma ham qabul (registrga bog'liq emas)
    [InlineData("Hujjat.PdF")]
    [InlineData("rasm.jpg")]
    [InlineData("rasm.jpeg")]
    [InlineData("rasm.png")]
    [InlineData("rasm.webp")]
    [InlineData("rasm.heic")]
    [InlineData("jadval.xlsx")]
    [InlineData("shartnoma.docx")]
    [InlineData("izoh.txt")]
    [InlineData("dars.mp4")]
    [InlineData("ovoz.ogg")]
    public void Validate_RuxsatEtilganTur_Qabul(string fileName)
    {
        Assert.Null(UploadGuard.Validate(new FakeFormFile(fileName)));
    }

    [Fact]
    public void Validate_YolliNom_KengaytmaBoyichaQabul()
    {
        // DIQQAT: Validate yo'lni tekshirmaydi — bu SafeName'ning vazifasi (quyidagi testlar).
        Assert.Null(UploadGuard.Validate(new FakeFormFile("../../etc/passwd.pdf")));
    }

    /* =========================================================================================
     *  SafeName — foydalanuvchi nomidan faqat kengaytma olinadi
     * ========================================================================================= */

    [Fact]
    public void SafeName_YolBuzish_FaqatGuidVaKengaytma()
    {
        var name = UploadGuard.SafeName(new FakeFormFile("../../etc/passwd.PDF"));

        Assert.Matches(new Regex("^[0-9a-f]{32}\\.pdf$"), name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("passwd", name);
    }

    [Theory]
    [InlineData("Mening hujjatim (1).DOCX", ".docx")]
    [InlineData("C:\\Users\\ali\\rasm.JPG", ".jpg")]
    [InlineData("отчёт.xlsx", ".xlsx")]
    [InlineData("cv.pdf", ".pdf")]
    public void SafeName_KengaytmaSaqlanadi_KichikHarfda(string fileName, string kutilganExt)
    {
        var name = UploadGuard.SafeName(new FakeFormFile(fileName));

        Assert.EndsWith(kutilganExt, name, StringComparison.Ordinal);
        Assert.Equal(32, name.Length - kutilganExt.Length);
    }

    [Fact]
    public void SafeName_HarChaqiriqdaTakrorlanmas()
    {
        var file = new FakeFormFile("cv.pdf");

        var names = Enumerable.Range(0, 50).Select(_ => UploadGuard.SafeName(file)).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void AllowedExtensions_XavfliTurlarniOzIchigaOlmaydi()
    {
        // Allowlist kelajakda kengaytirilsa, quyidagilar TASODIFAN kirib qolmasin.
        foreach (var xavfli in new[] { ".svg", ".html", ".htm", ".js", ".php", ".exe", ".sh", ".xml" })
            Assert.DoesNotContain(xavfli, UploadGuard.AllowedExtensions);
    }

    [Fact]
    public void IsPhotoUrl_FaqatJPGvaPNGniQabulQiladi()
    {
        Assert.True(UploadGuard.IsPhotoUrl("/uploads/abc.jpg"));
        Assert.True(UploadGuard.IsPhotoUrl("/uploads/abc.JPEG"));
        Assert.True(UploadGuard.IsPhotoUrl("/uploads/abc.png"));

        // Sertifikatga (Word) qo'yib bo'lmaydigan turlar — ular jimgina yo'qolmasin,
        // foydalanuvchi rasmni bog'lash paytidayoq xato ko'rsin.
        Assert.False(UploadGuard.IsPhotoUrl("/uploads/abc.heic"));
        Assert.False(UploadGuard.IsPhotoUrl("/uploads/abc.webp"));
        // Nisbatini o'qiy olmaydiganlarimiz — cho'zilib ketardi.
        Assert.False(UploadGuard.IsPhotoUrl("/uploads/abc.gif"));
        // Rasm umuman emas (ilgari `/uploads/` prefiksi yetarli edi — passport skani ham o'tardi).
        Assert.False(UploadGuard.IsPhotoUrl("/uploads/abc.pdf"));
        Assert.False(UploadGuard.IsPhotoUrl("/uploads/abc"));
    }
}
