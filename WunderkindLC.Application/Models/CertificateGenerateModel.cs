namespace WunderkindLC.Application.Models;

/// <summary>
/// Sertifikat yaratish uchun kerakli ma'lumotlar modeli.
/// CertificateService.GenerateCertificateAsync() ga beriladi.
/// </summary>
public class CertificateGenerateModel
{
    // ---------- O'quvchi ----------
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;

    // ---------- Kurs ----------
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;

    // ---------- Natijalar ----------
    /// <summary>O'quv dasturini bajarish foizi (0..100). Masalan: 95.5</summary>
    public double ProgressPercent { get; set; }

    /// <summary>O'rtacha baho (1.0..5.0 yoki 0..100 — CRM uslubiga qarab). Masalan: 4.2</summary>
    public double AverageScore { get; set; }

    /// <summary>Sertifikat berilgan sanagacha o'tilgan darslar soni.</summary>
    public int DarsCount { get; set; }

    // ---------- Sertifikat ma'lumotlari ----------
    /// <summary>
    /// Tugatish sanasi (ISO string "yyyy-MM-dd"). Bo'sh bo'lsa bugungi sana ishlatiladi.
    /// </summary>
    public string CompletionDate { get; set; } = string.Empty;

    /// <summary>Sertifikatga yozilgan qo'shimcha izoh (ixtiyoriy).</summary>
    public string? CompletionNotes { get; set; }

    /// <summary>Sertifikat beruvchi tashkilot nomi. Masalan: "Wunderkind o'quv markazi"</summary>
    public string IssuedBy { get; set; } = "Wunderkind o'quv markazi";

    /// <summary>Tashkilot manzili (ixtiyoriy).</summary>
    public string? IssuedByAddress { get; set; }

    // ---------- Identifikatsiya ----------
    /// <summary>
    /// Sertifikat raqami. Bo'sh bo'lsa GenerateCertificateNumber() bilan avtomatik yaratiladi.
    /// Format: "CERT-yyyy-MM-dd-NNNN"
    /// </summary>
    public string? CertificateNumber { get; set; }

    /// <summary>
    /// Sertifikatning noyob ID'si (tekshirish uchun URL'da ishlatiladi).
    /// Bo'sh bo'lsa GUID generatsiya qilinadi.
    /// </summary>
    public string? CertificateId { get; set; }

    /// <summary>
    /// Sertifikat tekshirish URL'i (QR-kod ichiga yoziladi).
    /// Masalan: "https://lc.wunderkindedu.uz/certificate/verify/{CertificateId}"
    /// Bo'sh bo'lsa QR-kod sertifikat raqamini o'z ichiga oladi.
    /// </summary>
    public string? VerifyUrl { get; set; }

    /// <summary>Asosiy rangi (ixtiyoriy, CSS hex yoki named). Masalan: "#3b0764"</summary>
    public string? AccentColor { get; set; }
}
