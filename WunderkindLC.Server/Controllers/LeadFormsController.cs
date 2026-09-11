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
/// LID FORMALARI — "O'quv bo'limi → Formalar" bo'limining API'si.
///
/// <para>Har bir reklama kanali uchun alohida ommaviy forma yaratiladi (Instagram uchun bittasi,
/// Facebook uchun boshqasi, Telegram uchun uchinchisi...) — har birining o'z havolasi
/// (<c>/forma/{slug}</c>) va o'z MANBASI bor, shu sabab tushgan lid qaysi kanaldan kelgani
/// aniq bo'ladi. Mantiq <see cref="LeadFormService"/> da (bot/ommaviy endpoint bilan yagona).</para>
///
/// <para>Ruxsat kaliti: <c>leads</c> — forma lid ishlab chiqaradi va javobida abituriyentlarning
/// TELEFON raqamlari qaytadi, ya'ni bu Lidlar bo'limining ma'lumoti (daraja testi tarixan
/// <c>schedule</c> ruxsatida qolgan — u kurs bilan bog'liq).</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.forms", ReadRequiresPerm = true)]
[Route("api/admin/lead-forms")]
public class LeadFormsController(
    AppDbContext db, AuditService audit, DataCache dataCache, IConfiguration config) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private static string Now() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>Bir so'rovda qaytadigan arizalar chegarasi (ro'yxat cheksiz o'smasin).</summary>
    private const int MaxSubmissions = 1000;

    // ==================== Formalar ====================

    /// <summary>Formalar ro'yxati + har birida maydonlar va arizalar soni.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeadFormListDto>>> GetAll()
    {
        var forms = await db.LeadForms.AsNoTracking().ToListAsync();
        // Sanoqlar DB tomonda agregatsiya qilinadi (jadvallar to'liq yuklanmaydi).
        var fieldCounts = (await db.LeadFormFields.GroupBy(f => f.FormId)
                .Select(g => new { g.Key, C = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.C);
        var subCounts = (await db.LeadFormSubmissions.GroupBy(s => s.FormId)
                .Select(g => new { g.Key, C = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.C);

        return forms
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new LeadFormListDto(
                f.Id, f.Title, f.Slug, f.Source, f.CourseName,
                f.IsActive, f.Views,
                fieldCounts.GetValueOrDefault(f.Id, 0), subCounts.GetValueOrDefault(f.Id, 0),
                f.CreatedAt, f.CreatedBy))
            .ToList();
    }

    /// <summary>Bitta formaning to'liq tafsiloti (maydonlari bilan).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<LeadFormDetailDto>> Get(string id)
    {
        var form = await db.LeadForms.FindAsync(id);
        if (form is null) return NotFound();
        return await LeadFormService.BuildDetailAsync(db, form);
    }

    [HttpPost]
    public async Task<ActionResult<LeadFormDetailDto>> Create(LeadFormPayload p)
    {
        var title = (p.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Forma nomini kiriting" });

        var form = new LeadForm
        {
            Title = title,
            Source = (p.Source ?? "").Trim(),
            CourseName = (p.CourseName ?? "").Trim(),
            CourseOptions = LeadFormService.CleanCourseOptions(p.CourseOptions),
            Intro = (p.Intro ?? "").Trim(),
            SuccessText = (p.SuccessText ?? "").Trim(),
            ButtonText = (p.ButtonText ?? "").Trim(),
            AskAge = p.AskAge,
            AskCourse = p.AskCourse,
            AskParentPhone = p.AskParentPhone,
            IsActive = p.IsActive,
            Slug = await LeadFormService.GenerateSlugAsync(db, title),
            CreatedAt = Now(),
            CreatedBy = Actor,
        };
        LeadFormService.WriteSocials(form, p.Socials);
        db.LeadForms.Add(form);
        LeadFormService.WriteFields(db, form.Id, p.Fields);
        audit.Record("LeadForm", form.Id, "create",
            $"«{form.Title}» lid formasi yaratildi (manba: {Label(form.Source)})");
        await db.SaveChangesAsync();
        return await LeadFormService.BuildDetailAsync(db, form);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LeadFormDetailDto>> Update(string id, LeadFormPayload p)
    {
        var form = await db.LeadForms.FindAsync(id);
        if (form is null) return NotFound();
        var title = (p.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest(new { message = "Forma nomini kiriting" });

        var before = Snapshot(form);
        form.Title = title;
        form.Source = (p.Source ?? "").Trim();
        form.CourseName = (p.CourseName ?? "").Trim();
        form.CourseOptions = LeadFormService.CleanCourseOptions(p.CourseOptions);
        form.Intro = (p.Intro ?? "").Trim();
        form.SuccessText = (p.SuccessText ?? "").Trim();
        form.ButtonText = (p.ButtonText ?? "").Trim();
        form.AskAge = p.AskAge;
        form.AskCourse = p.AskCourse;
        form.AskParentPhone = p.AskParentPhone;
        form.IsActive = p.IsActive;
        LeadFormService.WriteSocials(form, p.Socials);

        // Maydonlar TO'LIQ almashtiriladi (daraja testidagi savollar bilan bir xil, sodda usul).
        db.LeadFormFields.RemoveRange(db.LeadFormFields.Where(f => f.FormId == id));
        LeadFormService.WriteFields(db, id, p.Fields);
        audit.Record("LeadForm", form.Id, "update", $"«{form.Title}» lid formasi tahrirlandi",
            before: before, after: Snapshot(form));
        await db.SaveChangesAsync();
        return await LeadFormService.BuildDetailAsync(db, form);
    }

    /// <summary>
    /// NUSXALASH — "Instagram uchun bor formani Facebook uchun ham qilaman" holati. Savollar va
    /// sozlamalar ko'chadi, YANGI havola (slug) beriladi, MANBA esa bo'sh qoldiriladi: nusxa
    /// tasodifan boshqa kanalning manbasi bilan ishlab ketmasin (statistika buzilardi).
    /// Arizalar va ochilishlar soni KO'CHMAYDI.
    /// </summary>
    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<LeadFormDetailDto>> Duplicate(string id)
    {
        var src = await db.LeadForms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (src is null) return NotFound();
        var fields = await db.LeadFormFields.AsNoTracking()
            .Where(f => f.FormId == id).OrderBy(f => f.Order).ToListAsync();

        var title = $"{src.Title} (nusxa)";
        var copy = new LeadForm
        {
            Title = title,
            Source = "",
            CourseName = src.CourseName,
            // Kurs variantlari ham ko'chadi — ular formaning O'ZINIKI (markaz katalogi emas), ya'ni
            // nusxa olishdan maqsad aynan shu ro'yxatni qayta yozmaslik.
            CourseOptions = src.CourseOptions.ToList(),
            Intro = src.Intro,
            SuccessText = src.SuccessText,
            ButtonText = src.ButtonText,
            AskAge = src.AskAge,
            AskCourse = src.AskCourse,
            AskParentPhone = src.AskParentPhone,
            // Nusxa ATAYIN O'CHIQ holda keladi: manba tanlanmasdan lid yig'a boshlamasin.
            IsActive = false,
            Slug = await LeadFormService.GenerateSlugAsync(db, title),
            CreatedAt = Now(),
            CreatedBy = Actor,
            // Ijtimoiy tarmoq havolalari ham ko'chadi (ular "rahmat" ekranining matni — sozlama).
            InstagramUrl = src.InstagramUrl,
            TelegramUrl = src.TelegramUrl,
            FacebookUrl = src.FacebookUrl,
            YoutubeUrl = src.YoutubeUrl,
            WebsiteUrl = src.WebsiteUrl,
        };
        db.LeadForms.Add(copy);
        foreach (var f in fields)
            db.LeadFormFields.Add(new LeadFormField
            {
                FormId = copy.Id, Label = f.Label, Kind = f.Kind, Options = f.Options.ToList(),
                Placeholder = f.Placeholder, Required = f.Required, Order = f.Order,
            });
        audit.Record("LeadForm", copy.Id, "create", $"«{src.Title}» formasidan nusxa olindi");
        await db.SaveChangesAsync();
        return await LeadFormService.BuildDetailAsync(db, copy);
    }

    /// <summary>
    /// Formani o'chiradi. Arizalar tarixi ham o'chadi — LEKIN ular yaratgan LIDLAR CRM'da QOLADI
    /// (mijoz yo'qolmasin). Shuning uchun tasodifan bosilmasin: UI tasdiq so'raydi.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var form = await db.LeadForms.FindAsync(id);
        if (form is null) return NotFound();
        var subs = await db.LeadFormSubmissions.CountAsync(s => s.FormId == id);
        db.LeadFormFields.RemoveRange(db.LeadFormFields.Where(f => f.FormId == id));
        db.LeadFormSubmissions.RemoveRange(db.LeadFormSubmissions.Where(s => s.FormId == id));
        db.LeadForms.Remove(form);
        audit.Record("LeadForm", id, "delete",
            $"«{form.Title}» lid formasi o'chirildi ({subs} ta ariza tarixi bilan; lidlar saqlandi)");
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ==================== Arizalar va statistika ====================

    /// <summary>Arizalar — barcha formalar bo'yicha yoki <paramref name="formId"/> bo'yicha.</summary>
    [HttpGet("submissions")]
    public async Task<ActionResult<IEnumerable<LeadFormSubmissionDto>>> Submissions(
        [FromQuery] string? formId = null)
    {
        var q = db.LeadFormSubmissions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(formId)) q = q.Where(s => s.FormId == formId);
        var subs = await q.OrderByDescending(s => s.CreatedAt).Take(MaxSubmissions).ToListAsync();
        var titles = await db.LeadForms.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Title);
        return await LeadFormService.BuildSubmissionsAsync(db, subs, titles);
    }

    /// <summary>
    /// Voronka: ochildi → ariza → lid → o'quvchi (forma, manba va sub-kanal kesimida).
    ///
    /// <para>Hisob BUTUN arizalar to'plami ustida boradi (voronka qisman ma'lumotdan chiqmaydi),
    /// shuning uchun natija <see cref="DataCache"/> da: bog'liq jadvallardan biri o'zgarsa kesh
    /// AVTO-eskiradi, TTL esa faqat zaxira. Sanoqqa kiradigan hamma narsa bog'liqlikda —
    /// forma (ochilishlar), ariza, lid, bosqich, a'zolik (aktivmi) va to'lov.</para>
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<LeadFormStatsDto>> Stats() =>
        await dataCache.GetOrCreateAsync(
            "leadforms:stats",
            new[]
            {
                nameof(LeadForm), nameof(LeadFormSubmission), nameof(Lead), nameof(LeadStage),
                nameof(StudentGroup), nameof(FinanceTransaction),
            },
            TimeSpan.FromMinutes(10),
            LeadFormService.BuildStatsAsync);

    // ==================== AI tahlil (voronka) ====================

    /// <summary>Lid formalari voronkasining saqlangan AI tahlillari (eng yangisi birinchi).</summary>
    [HttpGet("ai-analyses")]
    public async Task<ActionResult<IEnumerable<FunnelAiRecordDto>>> AiAnalyses(CancellationToken ct) =>
        await FunnelAiAnalysisService.HistoryAsync(db, FunnelAiAnalysisService.KindLeadForms, ct);

    /// <summary>
    /// Lid formalari voronkasini Gemini orqali TANQIDIY tahlil qiladi (kuniga bir marta — bugungi
    /// yozuv bo'lsa Gemini chaqirilmaydi, mavjudi qaytadi).
    ///
    /// <para>⚠️ Auditga YOZILMAYDI: tahlil hech qanday ma'lumotni o'zgartirmaydi
    /// (`.claude/rules/audit.md` — AI tahlil qamrovda ATAYIN yo'q).</para>
    /// </summary>
    [HttpPost("ai-analysis")]
    public async Task<ActionResult<FunnelAiResponseDto>> AiAnalysis(CancellationToken ct) =>
        await FunnelAiAnalysisService.GenerateAsync(db, config, FunnelAiAnalysisService.KindLeadForms, ct);

    // ==================== Yordamchi ma'lumotnomalar ====================

    /// <summary>
    /// Manba ma'lumotnomasi (<see cref="LeadSource"/>) — forma qaysi kanalga tegishli ekanini
    /// tanlash uchun. <c>LeadSourcesController</c> <c>settings</c> ruxsatida bo'lgani uchun bu
    /// yerda alohida (lidlar bo'limi kalitiga ochilgan) endpoint bor.
    ///
    /// <para>⚠️ KURSLAR uchun bunday ma'lumotnoma YO'Q va ATAYIN qo'shilmagan: forma kursni
    /// markazdagi <c>Subject</c> katalogidan olmaydi, variantlar formaning O'ZIDA yoziladi
    /// (<see cref="LeadForm.CourseOptions"/>) — batafsil `.claude/rules/lead-forms.md` §2.5.</para>
    /// </summary>
    [HttpGet("sources")]
    public async Task<ActionResult<IEnumerable<string>>> Sources() =>
        await db.LeadSources.AsNoTracking()
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => s.Name).ToListAsync();

    /// <summary>Qo'llab-quvvatlanadigan maydon turlari — frontend ro'yxati SHUNDAN quriladi.</summary>
    [HttpGet("field-kinds")]
    public ActionResult<IEnumerable<object>> FieldKinds() =>
        LeadFormService.Kinds
            .Select(k => (object)new { key = k, needsOptions = LeadFormService.NeedsOptions(k) })
            .ToList();

    // ==================== Ichki ====================

    private static string Label(string? s) => string.IsNullOrWhiteSpace(s) ? "ko'rsatilmagan" : s.Trim();

    /// <summary>Audit uchun surat — maxfiy qiymat yo'q (forma sozlamalari ochiq ma'lumot).</summary>
    private static object Snapshot(LeadForm f) => new
    {
        f.Title, f.Source, f.CourseName,
        // Variantlar RO'YXAT — tarixda o'qiladigan bo'lishi uchun bitta matnga yig'iladi.
        CourseOptions = string.Join(", ", f.CourseOptions),
        f.Intro, f.SuccessText, f.ButtonText,
        f.AskAge, f.AskCourse, f.AskParentPhone, f.IsActive,
    };
}
