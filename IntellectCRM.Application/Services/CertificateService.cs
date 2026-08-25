using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Models;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// Sertifikat tizimi:
/// HTML sertifikat (A4 landscape, navy + gold dizayn, o'zbek matni).
/// certificate-template.html andozasidan foydalaniladi.
/// </summary>
public class CertificateService(IAppDbContext db, IHostEnvironment env)
{
    // ─────────────────────────────────────────────────────────────
    // Asosiy API: sertifikat yaratish
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// O'quvchi uchun HTML sertifikat yaratadi.
    /// Bir (studentId, courseId) uchun bir xil kunda idempotent.
    /// </summary>
    public async Task<StudentCertificate> GenerateCertificateAsync(
        string studentId,
        string courseId,
        string? metadataJson = null,
        DateTime? expiresAt = null,
        string? teacherName = null)
    {
        var today = AppClock.Now.Date;

        // Idempotency: bugun allaqachon berilgan bo'lsa qaytaramiz
        var existing = await db.StudentCertificates
            .Where(c => c.StudentId == studentId
                     && c.CourseId == courseId
                     && c.IssuedAt.Date == today
                     && c.Status == "active")
            .FirstOrDefaultAsync();
        if (existing is not null) return existing;

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId)
            ?? throw new InvalidOperationException($"O'quvchi topilmadi: {studentId}");
        var course = await db.Subjects.FirstOrDefaultAsync(s => s.Id == courseId)
            ?? throw new InvalidOperationException($"Kurs topilmadi: {courseId}");

        // O'qituvchi ismini DB dan olish (berilmagan bo'lsa)
        if (string.IsNullOrWhiteSpace(teacherName))
        {
            var group = await db.Classes
                .Where(g => g.CourseId == courseId && !g.IsArchived)
                .FirstOrDefaultAsync();
            if (group?.TeacherId is not null)
            {
                teacherName = await db.Teachers
                    .Where(t => t.Id == group.TeacherId)
                    .Select(t => t.FullName)
                    .FirstOrDefaultAsync();
            }
        }

        var certId = Guid.NewGuid().ToString("N");

        // Noyob raqam (u ayni paytda FAYL NOMI ham): bazada yoki diskda band bo'lsa qayta uriniladi.
        // Aks holda ikkinchi sertifikat birinchisining faylini qayta yozib, uning hash'ini buzardi.
        var (certNumber, fileName) = await ReserveCertificateNumberAsync();

        // Tekshirish URL (QR uchun)
        var verifyUrl = $"https://crm.intellectschool.uz/verify-certificate/{certId}";

        // HTML generatsiya
        var htmlContent = GenerateHtmlCertificate(
            studentName: student.FullName,
            courseName: course.Name,
            teacherName: teacherName ?? "O'qituvchi",
            certNumber: certNumber,
            issueDate: today,
            verifyUrl: verifyUrl
        );

        // MUHIM: fayl AYNAN shu baytlar bilan yoziladi (BOM'siz), hash va FileSize ham
        // AYNAN shu baytlardan olinadi. Ilgari fayl `Encoding.UTF8` bilan (BOM — EF BB BF)
        // yozilib, hash esa BOM'siz baytlardan hisoblanardi → hash hech qachon mos kelmasdi.
        var htmlBytes = Utf8NoBom.GetBytes(htmlContent);

        // Saqlash yo'li
        var certsDir = GetCertificatesDirectory();
        var absolutePath = System.IO.Path.Combine(certsDir, fileName);
        await System.IO.File.WriteAllBytesAsync(absolutePath, htmlBytes);

        var hash = ComputeSHA256(htmlBytes);

        var cert = new StudentCertificate
        {
            Id = certId,
            StudentId = studentId,
            CourseId = courseId,
            FileName = fileName,
            FilePath = $"/uploads/certificates/{fileName}",
            FileHash = hash,
            FileSize = htmlBytes.LongLength,
            IssuedAt = today,
            ExpiresAt = expiresAt,
            Status = "active",
            Metadata = metadataJson,
            CreatedAt = AppClock.Now,
        };
        db.StudentCertificates.Add(cert);
        await db.SaveChangesAsync();
        return cert;
    }

    /// <summary>O'quvchining barcha sertifikatlarini qaytaradi.</summary>
    public async Task<List<StudentCertificate>> GetStudentCertificatesAsync(string studentId)
    {
        return await db.StudentCertificates
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync();
    }

    /// <summary>Sertifikat faylini (HTML) qaytaradi.</summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)> DownloadCertificateAsync(
        string studentId,
        string certificateId)
    {
        var cert = await db.StudentCertificates
            .Where(c => c.Id == certificateId && c.StudentId == studentId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Sertifikat topilmadi yoki ruxsat yo'q.");

        var absolutePath = ResolveAbsolutePath(cert.FilePath);
        if (!System.IO.File.Exists(absolutePath))
            throw new System.IO.FileNotFoundException($"Sertifikat fayli topilmadi: {cert.FilePath}");

        var bytes = await System.IO.File.ReadAllBytesAsync(absolutePath);

        cert.DownloadCount++;
        if (cert.DownloadedAt is null) cert.DownloadedAt = AppClock.Now;
        await db.SaveChangesAsync();

        return (bytes, cert.FileName, "text/html");
    }

    /// <summary>Sertifikatni tekshiradi. Har tekshiruvda CertificateVerification yozuvi qoladi.</summary>
    public async Task<CertificateVerification> VerifyCertificateAsync(
        string certificateId,
        string verifiedFromIp = "")
    {
        var cert = await db.StudentCertificates
            .Where(c => c.Id == certificateId)
            .FirstOrDefaultAsync();

        // Sertifikat topilmadi (soxta yoki xato id) — bu ANONIM ommaviy endpoint bo'lgani uchun
        // yiqilmasligi shart. Tekshiruv jurnaliga yozmaymiz: StudentCertificateId MAJBURIY FK,
        // mavjud bo'lmagan id bilan saqlash DbUpdateException (500) beradi.
        if (cert is null)
        {
            return new CertificateVerification
            {
                StudentCertificateId = certificateId,
                VerifiedAt = AppClock.Now,
                VerifiedFrom = verifiedFromIp,
                IsValid = false,
                HashMatched = false,
            };
        }

        bool hashMatched = false;

        var absolutePath = ResolveAbsolutePath(cert.FilePath);
        if (System.IO.File.Exists(absolutePath))
        {
            var fileBytes = StripBom(await System.IO.File.ReadAllBytesAsync(absolutePath));
            var currentHash = ComputeSHA256(fileBytes);
            hashMatched = string.Equals(currentHash, cert.FileHash, StringComparison.OrdinalIgnoreCase);
        }

        var now = AppClock.Now;
        var isValid = hashMatched
                   && cert.Status == "active"
                   && (cert.ExpiresAt is null || cert.ExpiresAt.Value > now);

        var verification = new CertificateVerification
        {
            StudentCertificateId = certificateId,
            VerifiedAt = AppClock.Now,
            VerifiedFrom = verifiedFromIp,
            IsValid = isValid,
            HashMatched = hashMatched,
        };
        db.CertificateVerifications.Add(verification);
        await db.SaveChangesAsync();
        return verification;
    }

    // ─────────────────────────────────────────────────────────────
    // HTML GENERATSIYA — andozadan
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// certificate-template.html andozasini o'qib tokenlarni almashtiradi.
    /// Andoza topilmasa — ichki HTML fallback ishlatiladi.
    /// </summary>
    private string GenerateHtmlCertificate(
        string studentName,
        string courseName,
        string teacherName,
        string certNumber,
        DateTime issueDate,
        string verifyUrl)
    {
        var templatePath = GetTemplatePath();
        string template;

        if (System.IO.File.Exists(templatePath))
        {
            template = System.IO.File.ReadAllText(templatePath, Encoding.UTF8);
        }
        else
        {
            // Fallback: ichki minimal andoza
            template = GetFallbackTemplate();
        }

        var issueDateStr = FormatDateUz(issueDate);

        return template
            .Replace("{{student_name}}", HtmlEncode(studentName))
            .Replace("{{course_name}}", HtmlEncode(courseName))
            .Replace("{{teacher_name}}", HtmlEncode(teacherName))
            .Replace("{{issue_date}}", issueDateStr)
            .Replace("{{certificate_number}}", HtmlEncode(certNumber))
            .Replace("{{verify_url}}", HtmlEncode(verifyUrl))
            .Replace("[QR_CODE_IMAGE]", $"<a href=\"{HtmlEncode(verifyUrl)}\" style=\"font-size:7px;color:#8A94A0;word-break:break-all;\">QR: {HtmlEncode(verifyUrl)}</a>");
    }

    // ─────────────────────────────────────────────────────────────
    // Yordamchilar (ommaviy — mavjud API bilan muvofiq)
    // ─────────────────────────────────────────────────────────────

    /// <summary>SHA-256 hash (lowercase hex).</summary>
    public static string ComputeSHA256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Sertifikat raqami: CERT-yyyy-MM-dd-NNNN (odam o'qiy oladigan format saqlanadi).
    /// <para>Qo'shimcha (NNNN) ilgari vaqtdan olinardi ((ms + sek*1000) % 10000) va har 10
    /// soniyada takrorlanardi — bir xil raqam = bir xil fayl nomi = eski faylning ustiga
    /// yozilishi. Endi tasodifiy nuqtadan boshlanuvchi atomik hisoblagich ishlatiladi, ya'ni
    /// jarayon ichidagi ketma-ket chaqiruvlar HECH QACHON takrorlanmaydi.</para>
    /// <para>Jarayonlar/qayta ishga tushirish orasidagi to'qnashuvni esa
    /// <see cref="ReserveCertificateNumberAsync"/> baza va diskdan tekshirib to'sadi.</para>
    /// </summary>
    public static string GenerateCertificateNumber()
    {
        var datePart = AppClock.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var suffix = (uint)Interlocked.Increment(ref _numberSequence) % 10000;
        return $"CERT-{datePart}-{suffix:D4}";
    }

    /// <summary>Hisoblagichning boshlang'ich nuqtasi tasodifiy — qayta ishga tushirishda
    /// har doim bir xil raqamdan boshlanmasligi uchun.</summary>
    private static int _numberSequence = Random.Shared.Next(10000);

    /// <summary>Sertifikat andozasi uchun token almashtirish (HTML andoza uchun).</summary>
    public static string RenderTemplate(
        string htmlTemplate,
        string studentName,
        string courseName,
        DateTime issueDate,
        DateTime? expiresDate,
        string certNumber)
    {
        var expiresStr = expiresDate.HasValue
            ? expiresDate.Value.ToString("dd.MM.yyyy")
            : "Muddatsiz";

        return htmlTemplate
            .Replace("{{student_name}}", HtmlEncode(studentName))
            .Replace("{{course_name}}", HtmlEncode(courseName))
            .Replace("{{issue_date}}", issueDate.ToString("dd.MM.yyyy"))
            .Replace("{{certificate_number}}", HtmlEncode(certNumber))
            .Replace("{{expires_date}}", expiresStr);
    }

    /// <summary>
    /// HTML maxsus belgilarni encode qiladi.
    /// Qat'iy, kontekstga chidamli encoder ishlatiladi (apostrof va non-ASCII ham escape qilinadi) —
    /// token bir-qo'shtirnoqli atribut yoki skript/uslub kontekstiga tushsa ham teshik ochilmaydi.
    /// </summary>
    public static string HtmlEncode(string text) => HtmlEncoder.Default.Encode(text ?? "");

    // ─────────────────────────────────────────────────────────────
    // Xususiy yordamchilar
    // ─────────────────────────────────────────────────────────────

    /// <summary>BOM'siz UTF-8. `Encoding.UTF8` statik xossasi BOM (EF BB BF) YOZADI.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Baytlar boshidagi UTF-8 BOM'ni (EF BB BF) olib tashlaydi.
    /// <para>MIGRATSIYA KERAK EMAS: ESKI sertifikat fayllari BOM bilan yozilgan, lekin ularning
    /// saqlangan FileHash'i BOM'SIZ baytlardan hisoblangan edi. Tekshirishda BOM'ni olib
    /// tashlaganimiz uchun eski fayl → eski hash bilan MOS KELADI. YANGI fayllar esa allaqachon
    /// BOM'siz yoziladi → o'zgarishsiz qoladi va yangi hash bilan MOS KELADI.</para>
    /// </summary>
    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    /// <summary>
    /// Bo'sh (band bo'lmagan) sertifikat raqami va unga mos fayl nomini tanlaydi.
    /// Raqam fayl nomi bo'lgani uchun bazadagi FileName va diskdagi fayl bo'yicha tekshiriladi.
    /// </summary>
    private async Task<(string CertNumber, string FileName)> ReserveCertificateNumberAsync()
    {
        var certsDir = GetCertificatesDirectory();

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var certNumber = GenerateCertificateNumber();
            var safeName = certNumber.Replace("/", "-").Replace("\\", "-");
            var fileName = $"{safeName}.html";

            if (System.IO.File.Exists(System.IO.Path.Combine(certsDir, fileName))) continue;
            if (await db.StudentCertificates.AnyAsync(c => c.FileName == fileName)) continue;

            return (certNumber, fileName);
        }

        // Deyarli imkonsiz holat (bir kunda 50 ta ketma-ket band raqam) — Guid bilan kafolatlaymiz.
        var fallbackNumber = $"CERT-{AppClock.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                           + $"-{Guid.NewGuid().ToString("N")[..8]}";
        return (fallbackNumber, $"{fallbackNumber}.html");
    }

    private static readonly string[] UzMonths =
        ["yanvar", "fevral", "mart", "aprel", "may", "iyun",
         "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr"];

    private static string FormatDateUz(DateTime d) =>
        $"{d.Day} {UzMonths[d.Month - 1]} {d.Year}-yil";

    private string GetCertificatesDirectory()
    {
        var webRoot = env is IWebHostEnvironment webEnv
            ? webEnv.WebRootPath
            : env.ContentRootPath;
        var dir = System.IO.Path.Combine(webRoot ?? env.ContentRootPath, "uploads", "certificates");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private string GetTemplatePath()
    {
        var webRoot = env is IWebHostEnvironment webEnv
            ? webEnv.WebRootPath
            : env.ContentRootPath;
        return System.IO.Path.Combine(webRoot ?? env.ContentRootPath, "templates", "certificate-template.html");
    }

    private string ResolveAbsolutePath(string filePath)
    {
        var webRoot = env is IWebHostEnvironment webEnv
            ? webEnv.WebRootPath
            : env.ContentRootPath;
        return System.IO.Path.Combine(webRoot ?? env.ContentRootPath, filePath.TrimStart('/'));
    }

    private static string GetFallbackTemplate() => """
        <!DOCTYPE html>
        <html lang="uz">
        <head>
            <meta charset="UTF-8">
            <title>Sertifikat</title>
            <style>
                body { font-family: 'Times New Roman', Times, serif; margin: 0; padding: 0; background: #fff; }
                .certificate {
                    width: 297mm; height: 210mm;
                    background: linear-gradient(135deg, #1a3a5e 0%, #1f4a7a 50%, #0d2240 100%);
                    color: #fff; display: flex; flex-direction: column;
                    align-items: center; justify-content: center;
                    padding: 40px; box-sizing: border-box; position: relative;
                }
                .title { font-size: 58px; font-weight: 700; letter-spacing: 8px; margin-bottom: 10px; }
                .awarded { font-size: 13px; color: #B0B8C1; font-style: italic; margin-bottom: 12px; }
                .student-name { font-size: 38px; color: #FDB913; font-weight: 700; font-style: italic; margin-bottom: 10px; }
                .course { font-size: 15px; font-style: italic; margin-bottom: 20px; }
                .cert-num { font-size: 9px; color: #8A94A0; font-family: monospace; position: absolute; bottom: 20px; left: 40px; }
                @page { size: A4 landscape; margin: 0; }
            </style>
        </head>
        <body>
            <div class="certificate">
                <div class="title">SERTIFIKAT</div>
                <div class="awarded">Faxriyat bilan taqdim etilmoqda:</div>
                <div class="student-name">{{student_name}}</div>
                <div class="course">{{course_name}} kursini muvaffaqiyatli yakunladi.</div>
                <div class="cert-num">Sertifikat: {{certificate_number}} &bull; {{issue_date}}</div>
            </div>
        </body>
        </html>
        """;
}
