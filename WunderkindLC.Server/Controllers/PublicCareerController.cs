using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// KARYERA MINI APP (<c>/vakansiya</c> — statik HTML/CSS/Bootstrap sahifa) uchun OCHIQ API.
///
/// <para><b>Autentifikatsiya — Telegram imzosi.</b> Sahifada login yo'q: Telegram Mini App
/// <c>initData</c> satrini beradi, u <c>X-Telegram-Init-Data</c> sarlavhasida keladi va
/// <see cref="TelegramInitData"/> uni KARYERA BOTI tokeni bilan tekshiradi. Shundan
/// <c>ChatId</c> olinadi — "Arizalarim" va ariza yuborish faqat shu asosda ishlaydi
/// (ya'ni foydalanuvchi boshqa birovning arizasini ko'ra olmaydi).</para>
///
/// <para>Sahifa oddiy brauzerda ochilsa imzo bo'lmaydi — u holda <b>faqat ko'rish</b> rejimi:
/// "Biz haqimizda" va vakansiyalar ko'rinadi, ariza yuborish esa Telegram talab qiladi.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/career")]
public class PublicCareerController(
    AppDbContext db, CareerService career, CareerTelegramService careerBot,
    TelegramService telegram, IWebHostEnvironment env) : ControllerBase
{
    private const string InitDataHeader = "X-Telegram-Init-Data";
    /// <summary>Bitta nomzod bir vakansiyaga necha marta ariza bera olishi (takroriy spam oldini olish).</summary>
    private const int MaxApplicationsPerVacancy = 1;

    /// <summary>So'rov sarlavhasidagi Telegram imzosini tekshiradi. Yaroqsiz/yo'q bo'lsa null.</summary>
    private TelegramInitData.User? CurrentUser()
    {
        var raw = Request.Headers[InitDataHeader].ToString();
        return TelegramInitData.Validate(raw, careerBot.BotToken);
    }

    // =============================================================================================
    //  BOOTSTRAP — ilova ochilganda BIR so'rovda hamma narsa
    // =============================================================================================

    /// <summary>Mini App'ning boshlang'ich holati: biz haqimizda + faol vakansiyalar +
    /// (imzo bo'lsa) nomzodning o'z arizalari va bosqichlar katalogi.</summary>
    [HttpGet("bootstrap")]
    public async Task<ActionResult<CareerBootstrapDto>> Bootstrap()
    {
        var me = CurrentUser();

        var about = CareerController.ToDto(await db.CareerAbout.AsNoTracking().FirstOrDefaultAsync());

        var vacancies = await db.Vacancies.AsNoTracking()
            .Where(v => v.Status == "active")
            .OrderBy(v => v.Order).ThenByDescending(v => v.CreatedAt)
            .ToListAsync();

        var apps = new List<JobApplication>();
        var name = "";
        var phone = "";
        if (me is not null)
        {
            apps = await db.JobApplications.AsNoTracking()
                .Where(a => a.ChatId == me.ChatId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // Formani oldindan to'ldirish uchun: avvalgi arizadan yoki botga ulashilgan raqamdan.
            var last = apps.FirstOrDefault();
            name = last?.FullName ?? $"{me.FirstName} {me.LastName}".Trim();
            phone = last?.Phone ?? "";
            if (phone.Length == 0)
            {
                var botUser = await db.CareerBotUsers.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ChatId == me.ChatId);
                phone = botUser?.Phone ?? "";
            }
        }

        var appliedVacancyIds = apps.Select(a => a.VacancyId).ToHashSet();
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        var vacancyDtos = vacancies.Select(v => new PublicVacancyDto(
            v.Id, v.Title, v.Department, v.EmploymentType, v.Location,
            SalaryText(v),
            v.Description, v.Requirements, v.Responsibilities, v.Conditions,
            v.Deadline,
            v.Deadline.Length == 10 && string.CompareOrdinal(v.Deadline, today) < 0,
            appliedVacancyIds.Contains(v.Id),
            v.CreatedAt)).ToList();

        // Arizalar tarixi — bitta so'rovda (N+1 bo'lmasin).
        var appIds = apps.Select(a => a.Id).ToList();
        var events = appIds.Count == 0
            ? []
            : await db.JobApplicationEvents.AsNoTracking()
                .Where(e => appIds.Contains(e.ApplicationId))
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

        var appDtos = apps.Select(a => ToPublicDto(a, events)).ToList();

        return new CareerBootstrapDto(
            me is not null, name, phone, about, vacancyDtos, appDtos, CareerController.StageDtos());
    }

    // =============================================================================================
    //  CV YUKLASH (faqat PDF)
    // =============================================================================================

    /// <summary>Nomzodning CV faylini qabul qiladi — FAQAT PDF, 10 MB gacha. URL qaytaradi,
    /// u keyin <see cref="Apply"/> so'rovida yuboriladi.</summary>
    [HttpPost("cv")]
    [EnableRateLimiting("public-lead")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<UploadedFileDto>> UploadCv(IFormFile file)
    {
        if (CurrentUser() is null)
            return Unauthorized(new { message = "Bu amal faqat Telegram ilovasi orqali bajariladi." });

        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > 10_000_000) return BadRequest(new { message = "Fayl 10 MB dan katta" });

        // ATAYIN faqat PDF: nomzod fayli ochiq endpointdan keladi, kengaytmalar doirasi tor bo'lsin.
        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "CV faqat PDF ko'rinishida bo'lishi kerak" });

        var dir = Path.Combine(env.ContentRootPath, "uploads");
        Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}.pdf";
        await using (var fs = System.IO.File.Create(Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        var original = Path.GetFileName(file.FileName);
        if (original.Length > 150) original = original[..150];
        return new UploadedFileDto(original, $"/uploads/{stored}", file.Length, "application/pdf");
    }

    // =============================================================================================
    //  ARIZA YUBORISH
    // =============================================================================================

    /// <summary>Vakansiyaga ariza yuboradi. Adminlarga (asosiy bot orqali) xabarnoma ketadi.</summary>
    [HttpPost("apply")]
    [EnableRateLimiting("public-lead")]
    public async Task<ActionResult<PublicApplicationDto>> Apply(PublicApplyPayload p)
    {
        var me = CurrentUser();
        if (me is null)
            return Unauthorized(new { message = "Ariza yuborish faqat Telegram ilovasi orqali mumkin." });

        var vacancy = await db.Vacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Id == p.VacancyId);
        if (vacancy is null || vacancy.Status != "active")
            return NotFound(new { message = "Vakansiya topilmadi yoki yopilgan" });

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        if (vacancy.Deadline.Length == 10 && string.CompareOrdinal(vacancy.Deadline, today) < 0)
            return BadRequest(new { message = "Bu vakansiyaga ariza qabul qilish muddati tugagan" });

        var already = await db.JobApplications
            .CountAsync(a => a.ChatId == me.ChatId && a.VacancyId == vacancy.Id);
        if (already >= MaxApplicationsPerVacancy)
            return BadRequest(new { message = "Siz bu vakansiyaga allaqachon ariza yuborgansiz" });

        var fullName = Trim(p.FullName, 150);
        if (fullName.Length < 3) return BadRequest(new { message = "F.I.Sh. to'liq kiritilishi kerak" });

        var (phoneOk, phone, phoneError) = PhoneUtil.Validate(p.Phone);
        if (!phoneOk) return BadRequest(new { message = phoneError ?? "Telefon raqami noto'g'ri" });

        var motivation = Trim(p.Motivation, 4000);
        if (motivation.Length < 10)
            return BadRequest(new { message = "Motivatsion xatni to'liqroq yozing (kamida 10 belgi)" });

        // CV — ixtiyoriy, lekin berilsa faqat o'zimiz yuklagan `/uploads/...pdf` bo'lishi shart
        // (tashqi/soxta URL adminga yuborilmasin).
        var cvUrl = Trim(p.CvUrl, 300);
        if (cvUrl.Length > 0 &&
            !(cvUrl.StartsWith("/uploads/", StringComparison.Ordinal)
              && cvUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
              && !cvUrl.Contains("..", StringComparison.Ordinal)))
            return BadRequest(new { message = "CV fayli noto'g'ri" });

        var app = CareerService.BuildApplication(
            vacancy, me.ChatId, me.Username, fullName, phone,
            Trim(p.Experience, 4000), motivation, cvUrl, Trim(p.CvName, 150),
            await CareerService.NextNumberAsync(db));

        db.JobApplications.Add(app);
        db.JobApplicationEvents.Add(new JobApplicationEvent
        {
            ApplicationId = app.Id,
            Status = CareerService.StatusNew,
            Note = "Ariza yuborildi",
            CreatedAt = app.CreatedAt,
            CreatedBy = "Nomzod",
        });
        await db.SaveChangesAsync();

        // Xabarnomalar — arizani hech qachon buzmaydi (ichida try/catch).
        await CareerService.NotifyAdminsAsync(db, telegram, app);
        await career.NotifyCandidateAsync(app);

        var events = await db.JobApplicationEvents.AsNoTracking()
            .Where(e => e.ApplicationId == app.Id).OrderBy(e => e.CreatedAt).ToListAsync();
        return ToPublicDto(app, events);
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    private static PublicApplicationDto ToPublicDto(JobApplication a, List<JobApplicationEvent> allEvents)
    {
        var stage = CareerService.StageOf(a.Status);
        var history = allEvents
            .Where(e => e.ApplicationId == a.Id)
            .Select(e => new JobApplicationEventDto(e.Status, e.Note, e.CreatedAt, e.CreatedBy))
            .ToList();

        return new PublicApplicationDto(
            a.Id, a.Number, a.VacancyTitle,
            a.Status, stage.Label, stage.Icon, stage.CandidateText, a.StatusNote,
            a.CreatedAt, a.StatusChangedAt, history);
    }

    /// <summary>Maoshni ilovada ko'rsatiladigan matnga aylantiradi (raqam berilmasa — izoh).</summary>
    private static string SalaryText(Vacancy v)
    {
        static string M(decimal x) => AuditService.Money(x);
        if (v.SalaryFrom > 0 && v.SalaryTo > 0 && v.SalaryTo > v.SalaryFrom)
            return $"{M(v.SalaryFrom)} – {M(v.SalaryTo)} so'm";
        if (v.SalaryFrom > 0) return $"{M(v.SalaryFrom)} so'mdan";
        if (v.SalaryTo > 0) return $"{M(v.SalaryTo)} so'mgacha";
        return v.SalaryNote.Length > 0 ? v.SalaryNote : "Kelishilgan holda";
    }

    private static string Trim(string? s, int max)
    {
        var v = (s ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }
}
