using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KARYERA (ishga qabul) — "Boshqaruv → Vakansiyalar" bo'limining API'si:
/// <list type="bullet">
///   <item><b>Biz haqimizda</b> — Mini App'ning birinchi ekrani (matn, manzil, ijtimoiy tarmoqlar).</item>
///   <item><b>Vakansiyalar</b> — CRUD + <b>arxivlash</b>/tiklash. Arxivlangan vakansiya ilovada
///     ko'rinmaydi, lekin unga tushgan arizalar saqlanadi.</item>
///   <item><b>Arizalar</b> — nomzodlar ro'yxati, bosqichni o'zgartirish (nomzodga karyera boti
///     orqali avtomatik xabar ketadi) va ichki izoh.</item>
/// </list>
/// Ruxsat kaliti: <c>vacancies</c> (xodim uchun; admin/superadmin — cheklovsiz).
/// </summary>
[ApiController]
[Authorize]
// O'QISH ham darvozalanadi (ReadRequiresPerm): `GET applications` va `applications/{id}` javobida
// nomzodning `CvUrl` — `/uploads/*.pdf` REZYUME manzili qaytadi (ustiga telefon, ichki `AdminNote`).
// `/uploads` autentifikatsiyasiz berilgani uchun manzilni olgan xodim CV'ni abadiy ko'chirib ola
// oladi. Shu sabab GET uchun ham `vacancies` bo'limi ruxsati (biror amali) talab qilinadi.
// Nomzod tomoni (Mini App) bunga bog'liq EMAS — u alohida `api/career` ([AllowAnonymous]) da.
[AdminPerm("vacancies", ReadRequiresPerm = true)]
[Route("api/admin/career")]
public class CareerController(AppDbContext db, CareerService career, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";

    // =============================================================================================
    //  BOSQICHLAR KATALOGI
    // =============================================================================================

    /// <summary>Ariza bosqichlari (admin paneli va Mini App bir xil ro'yxatni ishlatadi).</summary>
    [HttpGet("stages")]
    public ActionResult<List<CareerStageDto>> Stages() => StageDtos();

    internal static List<CareerStageDto> StageDtos() =>
        CareerService.Stages
            .Select(s => new CareerStageDto(s.Key, s.Label, s.CandidateText, s.Icon, s.Order, s.IsFinal))
            .ToList();

    // =============================================================================================
    //  BIZ HAQIMIZDA
    // =============================================================================================

    /// <summary>"Biz haqimizda" bloki (hech qachon 404 emas — bo'sh andoza qaytadi).</summary>
    [HttpGet("about")]
    public async Task<ActionResult<CareerAboutDto>> GetAbout()
    {
        var a = await db.CareerAbout.AsNoTracking().FirstOrDefaultAsync();
        return ToDto(a);
    }

    /// <summary>"Biz haqimizda"ni saqlaydi (qator bo'lmasa yaratiladi).</summary>
    [HttpPut("about")]
    public async Task<ActionResult<CareerAboutDto>> SaveAbout(CareerAboutPayload p)
    {
        var a = await db.CareerAbout.FirstOrDefaultAsync();
        if (a is null)
        {
            a = new CareerAbout();
            db.CareerAbout.Add(a);
        }

        a.Title = Trim(p.Title, 200);
        a.Tagline = Trim(p.Tagline, 300);
        a.About = Trim(p.About, 8000);
        a.Benefits = Trim(p.Benefits, 4000);
        a.LogoUrl = Trim(p.LogoUrl, 500);
        a.Address = Trim(p.Address, 500);
        a.Landmark = Trim(p.Landmark, 300);
        a.MapUrl = Trim(p.MapUrl, 500);
        a.WorkTime = Trim(p.WorkTime, 200);
        a.Phone = Trim(p.Phone, 50);
        a.Phone2 = Trim(p.Phone2, 50);
        a.Email = Trim(p.Email, 200);
        a.Telegram = Trim(p.Telegram, 300);
        a.Instagram = Trim(p.Instagram, 300);
        a.Facebook = Trim(p.Facebook, 300);
        a.Youtube = Trim(p.Youtube, 300);
        a.Tiktok = Trim(p.Tiktok, 300);
        a.Website = Trim(p.Website, 300);
        a.UpdatedAt = AppClock.Iso();
        a.UpdatedBy = Actor;

        audit.Record("Vacancy", "about", "update", "Karyera Mini App \"Biz haqimizda\" matni o'zgartirildi");
        await db.SaveChangesAsync();
        return ToDto(a);
    }

    internal static CareerAboutDto ToDto(CareerAbout? a) => new(
        a?.Title ?? "", a?.Tagline ?? "", a?.About ?? "", a?.Benefits ?? "", a?.LogoUrl ?? "",
        a?.Address ?? "", a?.Landmark ?? "", a?.MapUrl ?? "", a?.WorkTime ?? "",
        a?.Phone ?? "", a?.Phone2 ?? "", a?.Email ?? "",
        a?.Telegram ?? "", a?.Instagram ?? "", a?.Facebook ?? "", a?.Youtube ?? "",
        a?.Tiktok ?? "", a?.Website ?? "",
        a?.UpdatedAt ?? "", a?.UpdatedBy ?? "");

    // =============================================================================================
    //  VAKANSIYALAR
    // =============================================================================================

    /// <summary>Vakansiyalar. <paramref name="status"/> — "active" | "archived" (bo'sh = hammasi).</summary>
    [HttpGet("vacancies")]
    public async Task<ActionResult<List<VacancyDto>>> Vacancies([FromQuery] string? status)
    {
        var query = db.Vacancies.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(v => v.Status == status);
        var list = await query.OrderBy(v => v.Order).ThenByDescending(v => v.CreatedAt).ToListAsync();

        // Arizalar soni BIR so'rovda (N+1 bo'lmasin).
        var ids = list.Select(v => v.Id).ToList();
        var counts = await db.JobApplications.AsNoTracking()
            .Where(a => ids.Contains(a.VacancyId))
            .GroupBy(a => new { a.VacancyId, a.Status })
            .Select(g => new { g.Key.VacancyId, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        return list.Select(v => ToDto(
            v,
            counts.Where(c => c.VacancyId == v.Id).Sum(c => c.Count),
            counts.Where(c => c.VacancyId == v.Id && c.Status == CareerService.StatusNew).Sum(c => c.Count)))
            .ToList();
    }

    /// <summary>Yangi vakansiya (darhol FAOL holatda — ilovada ko'rinadi).</summary>
    [HttpPost("vacancies")]
    public async Task<ActionResult<VacancyDto>> CreateVacancy(VacancyPayload p)
    {
        if (Validate(p) is { } err) return BadRequest(new { message = err });

        var v = new Vacancy
        {
            Status = "active",
            CreatedAt = AppClock.Iso(),
            CreatedBy = Actor,
        };
        Apply(v, p);
        db.Vacancies.Add(v);
        audit.Record("Vacancy", v.Id, "create", $"Vakansiya qo'shildi: {v.Title}");
        await db.SaveChangesAsync();
        return ToDto(v, 0, 0);
    }

    /// <summary>Vakansiyani tahrirlash (holat bu yerda o'zgarmaydi — arxivlash alohida amal).</summary>
    [HttpPut("vacancies/{id}")]
    public async Task<ActionResult<VacancyDto>> UpdateVacancy(string id, VacancyPayload p)
    {
        if (Validate(p) is { } err) return BadRequest(new { message = err });

        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound(new { message = "Vakansiya topilmadi" });

        Apply(v, p);
        audit.Record("Vacancy", v.Id, "update", $"Vakansiya tahrirlandi: {v.Title}");
        await db.SaveChangesAsync();
        return await WithCountsAsync(v);
    }

    /// <summary>Arxivlash — ilovada ko'rinmaydi, arizalar saqlanadi.</summary>
    [HttpPost("vacancies/{id}/archive")]
    public async Task<ActionResult<VacancyDto>> Archive(string id)
    {
        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound(new { message = "Vakansiya topilmadi" });

        v.Status = "archived";
        v.ArchivedAt = AppClock.Iso();
        v.ArchivedBy = Actor;
        audit.Record("Vacancy", v.Id, "update", $"Vakansiya arxivlandi: {v.Title}");
        await db.SaveChangesAsync();
        return await WithCountsAsync(v);
    }

    /// <summary>Arxivdan qaytarish (yana faol bo'ladi).</summary>
    [HttpPost("vacancies/{id}/restore")]
    public async Task<ActionResult<VacancyDto>> Restore(string id)
    {
        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound(new { message = "Vakansiya topilmadi" });

        v.Status = "active";
        v.ArchivedAt = "";
        v.ArchivedBy = "";
        audit.Record("Vacancy", v.Id, "update", $"Vakansiya arxivdan qaytarildi: {v.Title}");
        await db.SaveChangesAsync();
        return await WithCountsAsync(v);
    }

    /// <summary>Butunlay o'chirish — FAQAT unga ariza tushmagan bo'lsa (aks holda arxivlanadi).</summary>
    [HttpDelete("vacancies/{id}")]
    public async Task<IActionResult> DeleteVacancy(string id)
    {
        var v = await db.Vacancies.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return NotFound(new { message = "Vakansiya topilmadi" });

        if (await db.JobApplications.AnyAsync(a => a.VacancyId == id))
            return BadRequest(new { message = "Bu vakansiyaga arizalar tushgan — uni o'chirib bo'lmaydi, arxivlang." });

        audit.Record("Vacancy", v.Id, "delete", $"Vakansiya o'chirildi: {v.Title}");
        db.Vacancies.Remove(v);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<VacancyDto> WithCountsAsync(Vacancy v)
    {
        var total = await db.JobApplications.CountAsync(a => a.VacancyId == v.Id);
        var fresh = await db.JobApplications.CountAsync(a => a.VacancyId == v.Id && a.Status == CareerService.StatusNew);
        return ToDto(v, total, fresh);
    }

    private static string? Validate(VacancyPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.Title)) return "Lavozim nomi kerak";
        if (p.Title.Trim().Length > 200) return "Lavozim nomi juda uzun";
        if (p.SalaryFrom < 0 || p.SalaryTo < 0) return "Maosh manfiy bo'lmaydi";
        if (p.SalaryTo > 0 && p.SalaryFrom > p.SalaryTo) return "Maoshning quyi chegarasi yuqoridan katta";
        return null;
    }

    private static void Apply(Vacancy v, VacancyPayload p)
    {
        v.Title = Trim(p.Title, 200);
        v.Department = Trim(p.Department, 200);
        v.EmploymentType = p.EmploymentType is "full" or "part" or "shift" or "remote" ? p.EmploymentType : "full";
        v.Location = Trim(p.Location, 300);
        v.SalaryFrom = p.SalaryFrom;
        v.SalaryTo = p.SalaryTo;
        v.SalaryNote = Trim(p.SalaryNote, 200);
        v.Description = Trim(p.Description, 4000);
        v.Requirements = Trim(p.Requirements, 4000);
        v.Responsibilities = Trim(p.Responsibilities, 4000);
        v.Conditions = Trim(p.Conditions, 4000);
        v.Deadline = Trim(p.Deadline, 10);
        v.Order = p.Order;
    }

    internal static VacancyDto ToDto(Vacancy v, int appCount, int newCount) => new(
        v.Id, v.Title, v.Department, v.EmploymentType, v.Location,
        v.SalaryFrom, v.SalaryTo, v.SalaryNote,
        v.Description, v.Requirements, v.Responsibilities, v.Conditions,
        v.Status, v.Deadline, v.Order,
        v.CreatedAt, v.CreatedBy, v.ArchivedAt, v.ArchivedBy,
        appCount, newCount);

    // =============================================================================================
    //  ARIZALAR
    // =============================================================================================

    /// <summary>Arizalar ro'yxati — bosqich / vakansiya / matn bo'yicha filtr bilan.</summary>
    [HttpGet("applications")]
    public async Task<ActionResult<List<JobApplicationDto>>> Applications(
        [FromQuery] string? status, [FromQuery] string? vacancyId, [FromQuery] string? q)
    {
        var query = db.JobApplications.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(vacancyId)) query = query.Where(a => a.VacancyId == vacancyId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(a =>
                a.FullName.ToLower().Contains(term) ||
                a.Phone.Contains(term) ||
                a.VacancyTitle.ToLower().Contains(term));
        }

        var list = await query.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync();
        return list.Select(a => ToDto(a, null)).ToList();
    }

    /// <summary>Bitta ariza — bosqichlar TARIXI bilan.</summary>
    [HttpGet("applications/{id}")]
    public async Task<ActionResult<JobApplicationDto>> Application(string id)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Ariza topilmadi" });

        var history = await db.JobApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new JobApplicationEventDto(e.Status, e.Note, e.CreatedAt, e.CreatedBy))
            .ToListAsync();

        return ToDto(a, history);
    }

    /// <summary>Bosqichni o'zgartiradi — tarixga yoziladi va nomzodga botda xabar ketadi.</summary>
    [HttpPost("applications/{id}/status")]
    public async Task<ActionResult<JobApplicationDto>> SetStatus(string id, JobApplicationStatusPayload p)
    {
        if (!CareerService.IsValidStatus(p.Status))
            return BadRequest(new { message = "Noma'lum bosqich" });

        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Ariza topilmadi" });

        var note = (p.Note ?? "").Trim();
        if (note.Length > 2000) note = note[..2000];

        var oldStatus = a.Status;
        await career.SetStatusAsync(db, a, p.Status, note, Actor);
        audit.Record("JobApplication", a.Id, "update",
            $"Ariza #{a.Number} bosqichi: {CareerService.StageOf(oldStatus).Label} → " +
            $"{CareerService.StageOf(a.Status).Label} ({a.FullName})" +
            (note.Length > 0 ? $" — izoh: {note}" : ""));
        await db.SaveChangesAsync();
        return ToDto(a, null);
    }

    /// <summary>Ichki izoh (nomzodga KO'RINMAYDI).</summary>
    [HttpPut("applications/{id}/note")]
    public async Task<ActionResult<JobApplicationDto>> SetNote(string id, JobApplicationNotePayload p)
    {
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Ariza topilmadi" });

        a.AdminNote = Trim(p.AdminNote, 4000);
        audit.Record("JobApplication", a.Id, "update", $"Ariza #{a.Number} ichki izohi o'zgartirildi ({a.FullName})");
        await db.SaveChangesAsync();
        return ToDto(a, null);
    }

    /// <summary>Arizani o'chirish (tarixi bilan).</summary>
    [HttpDelete("applications/{id}")]
    public async Task<IActionResult> DeleteApplication(string id)
    {
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound(new { message = "Ariza topilmadi" });

        var events = await db.JobApplicationEvents.Where(e => e.ApplicationId == id).ToListAsync();
        db.JobApplicationEvents.RemoveRange(events);
        audit.Record("JobApplication", a.Id, "delete", $"Ariza o'chirildi: #{a.Number} ({a.FullName})");
        db.JobApplications.Remove(a);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Bo'lim tepasidagi jamlanma — bosqich bo'yicha arizalar soni.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CareerStatsDto>> Stats()
    {
        var rows = await db.JobApplications.AsNoTracking()
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var byStatus = CareerService.Stages.ToDictionary(
            s => s.Key, s => rows.FirstOrDefault(r => r.Status == s.Key)?.Count ?? 0);

        var total = rows.Sum(r => r.Count);
        var hired = byStatus.GetValueOrDefault(CareerService.StatusHired);
        var rejected = byStatus.GetValueOrDefault(CareerService.StatusRejected);
        return new CareerStatsDto(total, total - hired - rejected, hired, rejected, byStatus);
    }

    internal static JobApplicationDto ToDto(JobApplication a, List<JobApplicationEventDto>? history) => new(
        a.Id, a.Number, a.VacancyId, a.VacancyTitle,
        a.ChatId, a.TgUsername,
        a.FullName, a.Phone, a.Experience, a.Motivation,
        a.CvUrl, a.CvName,
        a.Status, a.StatusNote, a.StatusChangedAt, a.StatusChangedBy,
        a.AdminNote, a.CreatedAt, history);

    private static string Trim(string? s, int max)
    {
        var v = (s ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }
}
