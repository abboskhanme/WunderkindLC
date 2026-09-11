using WunderkindLC.Application.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// MAXFIY PAPKA — statik fayl provayderi ustidagi darvoza (<see cref="PrivateFolderFileProvider"/>).
///
/// <para><c>/uploads</c> ochiq statik papka: manzilni bilgan har kim login'siz oladi. Sertifikatlar
/// esa shaxsiy ma'lumot, shuning uchun ular statik yo'l bilan BERILMASLIGI kerak — lekin qolgan
/// fayllar (o'quvchi surati, kitob muqovasi) <c>&lt;img&gt;</c> da kerak, ya'ni OCHIQ qolishi shart.
/// Shu ikki talab bir vaqtda bajarilayotganini tekshiramiz.</para>
/// </summary>
public class PrivateFolderFileProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wunderkind-privatefiles", Guid.NewGuid().ToString("N"));

    private readonly string _certificatesDir;

    public PrivateFolderFileProviderTests()
    {
        _certificatesDir = Path.Combine(_root, "certificates");
        Directory.CreateDirectory(_certificatesDir);
        // Sertifikat (maxfiy) va o'quvchi surati (ochiq bo'lishi kerak).
        File.WriteAllText(Path.Combine(_certificatesDir, "cert-abc.pdf"), "MAXFIY");
        File.WriteAllText(Path.Combine(_root, "photo-abc.jpg"), "SURAT");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* tozalash xatosi natijaga ta'sir qilmasin */ }
    }

    private PrivateFolderFileProvider Provider() =>
        new(new PhysicalFileProvider(_root), NullLogger.Instance, _certificatesDir);

    [Fact]
    public void SertifikatFayli_STATIKyolBilanBERILMAYDI()
    {
        var info = Provider().GetFileInfo("/certificates/cert-abc.pdf");

        // "Yo'q" deb ko'rsatiladi — statik middleware uni bermaydi va 404 ga o'tadi.
        Assert.False(info.Exists);
        // Fayl DISKDA turibdi: yuklab olish avtorizatsiyalangan endpoint orqali ishlashda davom etadi.
        Assert.True(File.Exists(Path.Combine(_certificatesDir, "cert-abc.pdf")));
    }

    [Fact]
    public void OQUVCHISURATI_OCHIQqoladi()
    {
        // Surat `<img src="/uploads/...">` da kerak — brauzer u yerga `Authorization` sarlavhasini
        // yubora olmaydi, shuning uchun u YOPILMAYDI. Sertifikatni yopish suratlarga tegmasligi shart.
        var info = Provider().GetFileInfo("/photo-abc.jpg");

        Assert.True(info.Exists);
        Assert.Equal("photo-abc.jpg", info.Name);
    }

    [Fact]
    public void MaxfiyPapka_ICHKIpapkadaHamYOPIQ()
    {
        var nested = Path.Combine(_certificatesDir, "2026");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "eski.html"), "MAXFIY");

        Assert.False(Provider().GetFileInfo("/certificates/2026/eski.html").Exists);
    }

    [Fact]
    public void Tekshiruv_MANZILGAemas_FIZIKYOLGAasoslanadi()
    {
        // Manzilni `..` bilan aylantirib yozish tekshiruvni chetlab o'tmasligi kerak: qaror
        // hal qilingan FIZIK yo'l bo'yicha qabul qilinadi, manzil qanday yozilganidan qat'i nazar.
        var info = Provider().GetFileInfo("/certificates/2026/../cert-abc.pdf");

        Assert.False(info.Exists);
    }

    [Fact]
    public void QOSHNIpapka_ADASHIBbloklanmaydi()
    {
        // Nomi yopiq papka nomi bilan boshlanadigan BOSHQA papka ("certificates-eski") ochiq qolishi
        // kerak — aks holda oddiy prefiks solishtiruvi begona fayllarni ham yopib qo'yardi.
        var neighbour = Path.Combine(_root, "certificates-eski");
        Directory.CreateDirectory(neighbour);
        File.WriteAllText(Path.Combine(neighbour, "ochiq.txt"), "OCHIQ");

        Assert.True(Provider().GetFileInfo("/certificates-eski/ochiq.txt").Exists);
    }

    [Fact]
    public void RoyxatlashdaHam_MaxfiyPapkaKORINMAYDI()
    {
        var names = Provider().GetDirectoryContents("/").Select(f => f.Name).ToList();

        Assert.Contains("photo-abc.jpg", names);
        Assert.DoesNotContain("certificates", names);
    }

    [Fact]
    public void MaxfiyPapkaBERILMAGANDA_HammaFaylOchiq()
    {
        // Darvoza ATAYLAB o'chirilgan holat (favqulodda qaytarish kaliti) — hech narsa bloklanmaydi.
        var open = new PrivateFolderFileProvider(
            new PhysicalFileProvider(_root), NullLogger.Instance);

        Assert.True(open.GetFileInfo("/certificates/cert-abc.pdf").Exists);
        Assert.True(open.GetFileInfo("/photo-abc.jpg").Exists);
    }
}
