using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;
using System.Security.Claims;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Sertifikat endpointlari:
/// <list type="bullet">
///   <item>Student portal: o'z sertifikatlari ro'yxati + yuklash</item>
///   <item>Admin: kurs uchun andoza yaratish/olish</item>
///   <item>Public (anonim): sertifikat id bo'yicha tekshirish</item>
/// </list>
/// </summary>
[ApiController]
public class CertificatesController(AppDbContext db, CertificateService certService) : ControllerBase
{
    // ─────────────────────────── STUDENT PORTAL ───────────────────────────

    /// <summary>O'quvchining sertifikatlari ro'yxati (o'z sertifikatlari).</summary>
    [HttpGet("api/student/certificates")]
    [Authorize(Roles = "student,parent")]
    public async Task<ActionResult<IEnumerable<StudentCertificateDto>>> GetStudentCertificates()
    {
        var student = await ResolveStudentAsync();
        if (student is null) return Unauthorized();

        var certs = await db.StudentCertificates
            .Where(c => c.StudentId == student.Id)
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync();

        var courseIds = certs.Select(c => c.CourseId).Distinct().ToList();
        var courseNames = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        return certs.Select(c => ToStudentDto(c, courseNames.GetValueOrDefault(c.CourseId, ""))).ToList();
    }

    /// <summary>Sertifikat faylini yuklab olish (egalik tekshiruvi bilan).</summary>
    [HttpGet("api/student/certificates/{id}/download")]
    [Authorize(Roles = "student,parent")]
    public async Task<IActionResult> DownloadCertificate(string id)
    {
        var student = await ResolveStudentAsync();
        if (student is null) return Unauthorized();

        try
        {
            var (bytes, fileName, contentType) = await certService.DownloadCertificateAsync(student.Id, id);
            return File(bytes, contentType, fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "Sertifikat topilmadi" });
        }
        catch (FileNotFoundException)
        {
            return StatusCode(500, new { message = "Fayl serverda topilmadi" });
        }
    }

    // ─────────────────────────── ADMIN: O'QUVCHI SERTIFIKATLARI ───────────────────────────

    /// <summary>
    /// Admin: o'quvchining tugatgan kurslari + sertifikatlari (StudentDetailPage uchun).
    /// Har sertifikat — kurs nomi, berilgan sana, holat, yuklash havolasi (admin).
    /// </summary>
    [HttpGet("api/admin/students/{studentId}/certificates")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<StudentCompletedCourseDto>>> GetStudentCertificatesAdmin(string studentId)
    {
        var certs = await db.StudentCertificates
            .Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync();

        var courseIds = certs.Select(c => c.CourseId).Distinct().ToList();
        var courseNames = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        // Kurs → guruh nomi (o'quvchi shu kursdagi guruh(lar)idan birortasi — ko'rsatish uchun).
        var groupNamesByCourse = await (
            from sg in db.StudentGroups
            join g in db.Classes on sg.GroupId equals g.Id
            where sg.StudentId == studentId && courseIds.Contains(g.CourseId)
            select new { g.CourseId, g.Name, sg.IsActive, sg.Status, sg.JoinedAt })
            .ToListAsync();
        // Bir kursda bir nechta guruh bo'lsa — TIRIK a'zolik, so'ng holat (faol → sinov →
        // muzlatilgan), so'ng eng yangi qo'shilgani. ⚠️ Ilgari yalang `grp.First()` edi: tartib
        // yo'q, tirik/chiqib ketgan ajratilmasdi — sertifikatda TASODIFIY (ko'pincha eski,
        // muzlatilgan) guruh nomi chiqardi.
        var groupByCourse = groupNamesByCourse
            .GroupBy(x => x.CourseId)
            .ToDictionary(grp => grp.Key, grp => grp
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => MembershipLifecycle.StateRank(x.Status))
                .ThenByDescending(x => x.JoinedAt ?? string.Empty, StringComparer.Ordinal)
                .First().Name);

        return certs.Select(c => new StudentCompletedCourseDto(
            CertificateId: c.Id,
            CourseId: c.CourseId,
            CourseName: courseNames.GetValueOrDefault(c.CourseId, ""),
            IssuedAt: c.IssuedAt.ToString("yyyy-MM-dd"),
            ExpiresAt: c.ExpiresAt?.ToString("yyyy-MM-dd") ?? "",
            Status: c.Status,
            FileName: c.FileName,
            DownloadUrl: $"/api/admin/students/{studentId}/certificates/{c.Id}/download",
            DownloadCount: c.DownloadCount,
            GroupName: groupByCourse.GetValueOrDefault(c.CourseId, ""))).ToList();
    }

    /// <summary>Admin: o'quvchi sertifikat faylini yuklab olish.</summary>
    [HttpGet("api/admin/students/{studentId}/certificates/{id}/download")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<IActionResult> DownloadCertificateAdmin(string studentId, string id)
    {
        try
        {
            var (bytes, fileName, contentType) = await certService.DownloadCertificateAsync(studentId, id);
            return File(bytes, contentType, fileName);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "Sertifikat topilmadi" });
        }
        catch (FileNotFoundException)
        {
            return StatusCode(500, new { message = "Fayl serverda topilmadi" });
        }
    }

    // ─────────────────────────── ADMIN ───────────────────────────

    /// <summary>Admin: sertifikat andozalari ro'yxati.</summary>
    [HttpGet("api/admin/certificate-templates")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<CertificateTemplateDto>>> GetTemplates()
    {
        var templates = await db.CertificateTemplates
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var courseIds = templates.Where(t => t.CourseId != null)
            .Select(t => t.CourseId!).Distinct().ToList();
        var courseNames = await db.Subjects
            .Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        return templates.Select(t => ToTemplateDto(t, courseNames.GetValueOrDefault(t.CourseId ?? "", ""))).ToList();
    }

    /// <summary>Admin: kurs uchun sertifikat andozasi yaratish.</summary>
    [HttpPost("api/admin/certificate-templates")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<CertificateTemplateDto>> CreateTemplate(CreateCertificateTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Andoza nomi bo'sh bo'lmasligi kerak" });
        if (string.IsNullOrWhiteSpace(req.HtmlTemplate))
            return BadRequest(new { message = "HTML andoza bo'sh bo'lmasligi kerak" });

        var tpl = new CertificateTemplate
        {
            Name = req.Name.Trim(),
            CourseId = req.CourseId ?? string.Empty,
            HtmlTemplate = req.HtmlTemplate,
            ValidityDays = 0,
        };
        db.CertificateTemplates.Add(tpl);
        await db.SaveChangesAsync();

        var courseName = tpl.CourseId is not null
            ? await db.Subjects.Where(s => s.Id == tpl.CourseId).Select(s => s.Name).FirstOrDefaultAsync() ?? ""
            : "";

        return StatusCode(201, ToTemplateDto(tpl, courseName));
    }

    /// <summary>Admin: kurs uchun mavjud sertifikat andozasini olish.</summary>
    [HttpGet("api/admin/certificate-templates/{id}")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<CertificateTemplateDto>> GetTemplate(string id)
    {
        var tpl = await db.CertificateTemplates.FindAsync(id);
        if (tpl is null) return NotFound(new { message = "Andoza topilmadi" });

        var courseName = tpl.CourseId is not null
            ? await db.Subjects.Where(s => s.Id == tpl.CourseId).Select(s => s.Name).FirstOrDefaultAsync() ?? ""
            : "";

        return ToTemplateDto(tpl, courseName);
    }

    /// <summary>Admin: o'quvchiga qo'lda sertifikat yaratish (kurs id bo'yicha).</summary>
    [HttpPost("api/admin/students/{studentId}/certificates/generate")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<ActionResult<StudentCompletedCourseDto>> GenerateCertificateAdmin(
        string studentId, [FromBody] GenerateCertificateRequest req)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var course = await db.Subjects.FindAsync(req.CourseId);
        if (course is null) return BadRequest(new { message = "Kurs topilmadi" });

        StudentCertificate cert;
        try
        {
            cert = await certService.GenerateCertificateAsync(studentId, req.CourseId, req.Notes);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }

        return StatusCode(201, new StudentCompletedCourseDto(
            CertificateId: cert.Id,
            CourseId: cert.CourseId,
            CourseName: course.Name,
            IssuedAt: cert.IssuedAt.ToString("yyyy-MM-dd"),
            ExpiresAt: cert.ExpiresAt?.ToString("yyyy-MM-dd") ?? "",
            Status: cert.Status,
            FileName: cert.FileName,
            DownloadUrl: $"/api/admin/students/{studentId}/certificates/{cert.Id}/download",
            DownloadCount: cert.DownloadCount,
            GroupName: ""));
    }

    /// <summary>Admin: andozani o'chirish.</summary>
    [HttpDelete("api/admin/certificate-templates/{id}")]
    [Authorize]
    [AdminPerm("students.list", ReadRequiresPerm = true)]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        var tpl = await db.CertificateTemplates.FindAsync(id);
        if (tpl is null) return NotFound();
        db.CertificateTemplates.Remove(tpl);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ─────────────────────────── PUBLIC ───────────────────────────

    /// <summary>Anonim: sertifikat id bo'yicha tekshirish (hash + status).</summary>
    [HttpGet("api/public/certificates/{id}/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<CertificateVerificationDto>> VerifyCertificate(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new CertificateVerificationDto(
                false, "", "", "", "", "", false, "", "Sertifikat ID kiritilmagan"));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var verification = await certService.VerifyCertificateAsync(id, ip);

        var cert = await db.StudentCertificates.FindAsync(id);
        var studentName = cert is not null
            ? await db.Students.Where(s => s.Id == cert.StudentId).Select(s => s.FullName).FirstOrDefaultAsync() ?? ""
            : "";
        var courseName = cert is not null
            ? await db.Subjects.Where(s => s.Id == cert.CourseId).Select(s => s.Name).FirstOrDefaultAsync() ?? ""
            : "";

        return Ok(new CertificateVerificationDto(
            IsValid: verification.IsValid,
            StudentName: studentName,
            CourseName: courseName,
            IssuedAt: cert?.IssuedAt.ToString("yyyy-MM-dd") ?? "",
            ExpiresAt: cert?.ExpiresAt?.ToString("yyyy-MM-dd") ?? "",
            Status: cert?.Status ?? "not_found",
            HashMatched: verification.HashMatched,
            // ⚠️ `Metadata` BERILMAYDI. Bu endpoint `[AllowAnonymous]` — sertifikatdagi QR kodni
            // skanerlagan HAR KIM ochadi. `Metadata` esa ADMIN yozgan erkin matn (sertifikat
            // yaratishdagi `Notes`, guruh yopishdagi `CompletionNotes` — u BUTUN bitiruvchi
            // guruhga birdek yoziladi), ya'ni "to'lovni to'lamadi, sertifikat ijtimoiy sabab
            // bilan berildi" kabi ichki izoh o'quvchining ismi yonida ommaga chiqib ketardi.
            // Tekshiruvga kerak bo'lgani — ISM, KURS, SANA va HOLAT; izoh emas.
            Metadata: "",
            ErrorMessage: verification.IsValid ? "" : "Sertifikat yaroqsiz yoki topilmadi"));
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private async Task<Student?> ResolveStudentAsync()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (uid is null) return null;

        if (User.IsInRole("parent"))
        {
            var user = await db.Users.FindAsync(uid);
            if (user is null) return null;

            // ⚠️ BO'SH RAQAM — BOSHQA ODAMNING FARZANDI. `ParentPhone`/`FatherPhone`/`MotherPhone`
            // ning uchalasi ham `""` bilan boshlanadi (`Entities.cs`), ya'ni raqamsiz login
            // (`Email` da bironta ham raqam yo'q) bo'sh satrni bo'sh satrga tenglashtirib,
            // ota-onasi telefoni to'ldirilmagan BIRINCHI o'quvchini qaytarardi — va u ota-ona
            // begona bolaning sertifikatini yuklab olardi. `StudentPortalController` da
            // bu qo'riqchi BOR edi, bu yerda esa tushib qolgan.
            var norm = PhoneUtil.Normalize(user.Email);
            if (string.IsNullOrEmpty(norm)) return null;

            // Telefonlar bazaga har doim `PhoneUtil.Normalize` bilan kanonik ko'rinishda
            // yoziladi, shuning uchun solishtirish SERVERDA (butun jadvalni xotiraga
            // tortmasdan) — `StudentPortalController.MyStudentAsync` bilan AYNAN bir xil.
            return await db.Students.AsNoTracking()
                .FirstOrDefaultAsync(s => !s.IsArchived
                    && (s.ParentPhone == norm || s.FatherPhone == norm || s.MotherPhone == norm));
        }

        return await db.Students.FirstOrDefaultAsync(s => s.UserId == uid);
    }

    private static StudentCertificateDto ToStudentDto(StudentCertificate c, string courseName) => new(
        Id: c.Id,
        CourseName: courseName,
        IssuedAt: c.IssuedAt.ToString("yyyy-MM-dd"),
        ExpiresAt: c.ExpiresAt?.ToString("yyyy-MM-dd") ?? "",
        Status: c.Status,
        FileName: c.FileName,
        DownloadUrl: $"/api/student/certificates/{c.Id}/download",
        DownloadCount: c.DownloadCount,
        Metadata: c.Metadata ?? "");

    private static CertificateTemplateDto ToTemplateDto(CertificateTemplate t, string courseName) => new(
        Id: t.Id,
        Name: t.Name,
        CourseId: t.CourseId ?? "",
        CourseName: courseName,
        ValidityDays: 0,
        CreatedAt: t.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss"));
}
