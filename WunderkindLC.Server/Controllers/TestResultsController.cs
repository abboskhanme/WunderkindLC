using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// Test natijalari — "O'quv bo'limi" → Testlar natijalari. Admin/superadmin barcha guruhlarni ko'radi;
/// xodim (staff) "classes" ruxsatiga qarab yozadi (o'qish har doim ochiq). Mantiq
/// <see cref="TestResultService"/>da (o'qituvchi ilovasi bilan umumiy).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes.testResults")]
[Route("api/admin/test-results")]
public class TestResultsController(
    AppDbContext db, TestCertificateService certs, TestCertificateJobs certJobs) : ControllerBase
{
    private const string ZipMime = "application/zip";

    private string Actor() =>
        User.Identity?.Name
        ?? User.FindFirst("name")?.Value
        ?? User.FindFirst(ClaimTypes.Name)?.Value
        ?? "Admin";

    /// <summary>Bosh sahifa — barcha guruhlar + har biriga yaratilgan testlar soni.</summary>
    [HttpGet("groups")]
    public async Task<List<TestGroupOverviewDto>> Groups() =>
        await TestResultService.GroupsOverviewAsync(db);

    /// <summary>Bitta guruhning testlar ro'yxati (?groupId=).</summary>
    [HttpGet]
    public async Task<List<GroupTestDto>> List([FromQuery] string groupId) =>
        await TestResultService.ListForGroupAsync(db, groupId);

    /// <summary>Test tafsiloti — o'quvchilar + ballari (ball desc).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TestResultDetailDto>> Detail(string id)
    {
        var d = await TestResultService.DetailAsync(db, id);
        return d is null ? NotFound() : d;
    }

    /// <summary>Yangi test yaratish.</summary>
    [HttpPost]
    public async Task<ActionResult<GroupTestDto>> Create(CreateTestResultRequest req)
    {
        var (dto, err) = await TestResultService.CreateAsync(
            db, req.GroupId, req.Name, req.Date, req.MaxScore, Actor(), req.Online,
            req.CertificateEnabled, req.CertificateTemplateId);
        return err != null ? BadRequest(new { message = err }) : dto!;
    }

    /// <summary>Testni tahrirlash.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateTestResultRequest req)
    {
        var (ok, err) = await TestResultService.UpdateAsync(
            db, id, req.Name, req.Date, req.MaxScore, req.Online,
            req.CertificateEnabled, req.CertificateTemplateId);
        return ok ? NoContent() : BadRequest(new { message = err });
    }

    /// <summary>Testni o'chirish (ballari bilan).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id) =>
        await TestResultService.DeleteAsync(db, id) ? NoContent() : NotFound();

    /// <summary>Bitta o'quvchiga ball qo'yish/tozalash. Qaytadi: qayta saralangan tafsilot.</summary>
    [HttpPut("{id}/scores")]
    public async Task<ActionResult<TestResultDetailDto>> SetScore(string id, SetTestScoreRequest req)
    {
        var (detail, err) = await TestResultService.SetScoreAsync(db, id, req.StudentId, req.Score);
        return err != null ? BadRequest(new { message = err }) : detail!;
    }

    /// <summary>Bitta o'quvchining barcha test natijalari (profil sahifasi uchun).</summary>
    [HttpGet("student/{studentId}")]
    public async Task<List<StudentGroupTestDto>> ForStudent(string studentId) =>
        await TestResultService.StudentResultsAsync(db, studentId);

    // =============================================================================================
    //  SERTIFIKAT — Word andozalari va berilgan sertifikatlar
    // =============================================================================================

    /// <summary>Andozada ishlatiladigan <c>@</c>-o'zgaruvchilar ro'yxati + PDF konvertori bormi.
    /// Admin paneli shu ro'yxatni ko'rsatadi — shablon muallifi nimani yozishini bilsin.</summary>
    [HttpGet("certificate-tokens")]
    public ActionResult<object> CertificateTokens() =>
        Ok(new
        {
            tokens = TestCertificateService.Tokens,
            photoHelp = TestCertificateService.PhotoHelp,
            pdfAvailable = DocxToPdfConverter.IsAvailable,
        });

    /// <summary>Sertifikat Word andozalari ro'yxati.</summary>
    [HttpGet("certificate-templates")]
    public async Task<List<TestCertificateTemplateDto>> CertificateTemplates(
        [FromQuery] bool activeOnly = false, CancellationToken ct = default) =>
        await certs.ListTemplatesAsync(db, includeInactive: !activeOnly, ct);

    /// <summary>Yangi andoza (avval <c>POST /api/admin/uploads</c> ga .docx yuklanadi).</summary>
    [HttpPost("certificate-templates")]
    public async Task<ActionResult<TestCertificateTemplateDto>> CreateCertificateTemplate(
        TestCertificateTemplatePayload payload, CancellationToken ct)
    {
        var (dto, err) = await certs.CreateTemplateAsync(db, payload, Actor(), ct);
        return err != null ? BadRequest(new { message = err }) : dto!;
    }

    /// <summary>Andozani tahrirlash (nomi / faylni almashtirish / standart / faol).</summary>
    [HttpPut("certificate-templates/{id}")]
    public async Task<ActionResult<TestCertificateTemplateDto>> UpdateCertificateTemplate(
        string id, TestCertificateTemplatePayload payload, CancellationToken ct)
    {
        var (dto, err) = await certs.UpdateTemplateAsync(db, id, payload, ct);
        return err != null ? BadRequest(new { message = err }) : dto!;
    }

    /// <summary>Andozani o'chirish (sertifikat berilgan bo'lsa taqiqlanadi — nofaol qiling).</summary>
    [HttpDelete("certificate-templates/{id}")]
    public async Task<IActionResult> DeleteCertificateTemplate(string id, CancellationToken ct)
    {
        var err = await certs.DeleteTemplateAsync(db, id, ct);
        return err is null ? NoContent() : BadRequest(new { message = err });
    }

    /// <summary>
    /// Test bo'yicha SERTIFIKATLARNI YARATISHNI BOSHLASH — ball kiritilgan har bir o'quvchiga bittadan.
    /// Qayta chaqirilsa mavjudlari yangilanadi (nusxa yaratilmaydi).
    ///
    /// <para>Ish FONDA bajariladi va bu so'rov DARHOL qaytadi: 30 kishilik guruhda generatsiya
    /// ~40 soniya olardi va Cloudflare uni uzib yuborishi mumkin edi. Holatni bilish uchun UI
    /// <c>{id}/certificates/status</c> ni so'rab turadi.</para>
    /// </summary>
    [HttpPost("{id}/certificates")]
    public async Task<ActionResult<TestCertificateJobDto>> GenerateCertificates(
        string id, CancellationToken ct)
    {
        var (_, err) = await certJobs.StartAsync(db, certs, id, Actor(), ct);
        if (err != null) return BadRequest(new { message = err });
        // Javobda MAVJUD sertifikatlar ham qaytariladi: UI ro'yxatni shu javobdan oladi va agar
        // bo'sh kelsa, allaqachon berilgan sertifikatlar birinchi holat javobigacha g'oyib bo'lardi.
        return await certJobs.StatusWithItemsAsync(db, id, ct);
    }

    /// <summary>Test bo'yicha berilgan sertifikatlar ro'yxati.</summary>
    [HttpGet("{id}/certificates")]
    public async Task<List<TestCertificateDto>> Certificates(string id, CancellationToken ct) =>
        await TestCertificateService.ListForTestAsync(db, id, ct);

    /// <summary>Generatsiya holati + SHU DAQIQADA tayyor sertifikatlar (UI shuni so'rab turadi).</summary>
    [HttpGet("{id}/certificates/status")]
    public async Task<TestCertificateJobDto> CertificatesStatus(string id, CancellationToken ct) =>
        await certJobs.StatusWithItemsAsync(db, id, ct);

    /// <summary>Bitta sertifikatni yuklab olish (PDF bo'lsa PDF, aks holda .docx).</summary>
    [HttpGet("certificates/{certificateId}/download")]
    public async Task<IActionResult> DownloadCertificate(
        string certificateId, [FromQuery] string? format = null, CancellationToken ct = default)
    {
        var file = await certs.ReadFileAsync(db, certificateId, preferPdf: format != "docx", ct);
        return file is null ? NotFound() : File(file.Value.Bytes, file.Value.ContentType, file.Value.FileName);
    }

    /// <summary>Test bo'yicha BARCHA sertifikatlar — bitta ZIP.</summary>
    [HttpGet("{id}/certificates/download")]
    public async Task<IActionResult> DownloadAllCertificates(string id, CancellationToken ct)
    {
        var zip = await certs.ZipForTestAsync(db, id, ct);
        return zip is null ? NotFound(new { message = "Sertifikat yo'q" }) : File(zip.Value.Bytes, ZipMime, zip.Value.FileName);
    }
}
