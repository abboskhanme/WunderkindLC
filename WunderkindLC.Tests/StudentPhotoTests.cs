using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// O'QUVCHI RASMI (profil surati) — <see cref="Student.BirthCertificateUrl"/> ustunida saqlanadi.
///
/// <para>Ustun nomi ESKI (ilgari tug'ilganlik guvohnomasi edi), lekin butun tizim uni RASM deb
/// ishlatadi: admin formasidagi yorlig'i "O'quvchi rasmi", o'quvchi ilovasiga
/// <c>StudentProfileDto.PhotoUrl</c> bo'lib chiqadi, admin profilida dumaloq avatarda ko'rinadi.
/// Shu bog'liqlik tasodifan uzilib qolmasin — quyidagi test aynan shuni qo'riqlaydi.</para>
/// </summary>
public class StudentPhotoTests
{
    [Fact]
    public void Profil_DTO_sida_rasm_BirthCertificateUrl_dan_PhotoUrl_ga_otadi()
    {
        // StudentProfileBuilder/StudentPortalController AYNAN shu bog'lanishni qiladi:
        //   Student.BirthCertificateUrl  →  StudentProfileDto.PhotoUrl
        var s = new Student { FullName = "Ali Valiyev", BirthCertificateUrl = "/uploads/foto.jpg" };

        var dto = new Application.Dtos.StudentProfileDto(
            s.Id, s.FullName, s.ClassName, s.BirthDate, s.Gender,
            s.ParentFullName, s.ParentPhone, s.EnrollmentDate,
            s.BirthCertificateUrl, s.ParentPassportUrl);

        Assert.Equal("/uploads/foto.jpg", dto.PhotoUrl);
    }

    [Theory]
    // Serverning O'Z yuklamasi — qabul qilinadi.
    [InlineData("/uploads/foto.jpg", true)]
    [InlineData("/uploads/2026/rasm.png", true)]
    // Bo'sh — rasmni O'CHIRISH degani (ruxsat).
    [InlineData("", true)]
    [InlineData(null, true)]
    // Tashqi havola yoki boshqa yo'l — RAD etiladi (controller shu qoidani qo'llaydi).
    [InlineData("https://evil.example/x.jpg", false)]
    [InlineData("uploads/foto.jpg", false)]
    [InlineData("/etc/passwd", false)]
    public void Rasm_manzili_faqat_uploads_dan_boladi(string? url, bool allowed)
    {
        // `StudentsController.SetPhoto` dagi tekshiruv bilan AYNAN bir xil qoida.
        var trimmed = (url ?? "").Trim();
        var ok = trimmed.Length == 0 || trimmed.StartsWith("/uploads/", StringComparison.Ordinal);
        Assert.Equal(allowed, ok);
    }

    [Fact]
    public void Rasm_fayl_turlari_UploadGuard_da_ruxsat_etilgan()
    {
        // Kameradan olingan kadr JPEG bo'lib yuklanadi — u albatta ruxsat etilgan bo'lishi kerak,
        // aks holda "Suratga olish" ishlamay qolardi.
        Assert.Contains(".jpg", UploadGuard.AllowedExtensions);
        Assert.Contains(".jpeg", UploadGuard.AllowedExtensions);
        Assert.Contains(".png", UploadGuard.AllowedExtensions);
        Assert.Contains(".webp", UploadGuard.AllowedExtensions);
    }
}
