using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

// "classes" (Guruhlar) — admin jurnali faqat Guruh detali sahifasidan ochiladi, alohida "Jurnal"
// bo'limi/ruxsat katalogida yo'q; "journal" kaliti bo'lsa xodimga hech qachon berib bo'lmas edi.
[ApiController]
[Authorize]
[AdminPerm("classes.list")]
[Route("api/admin/journal")]
public class JournalController(
    AppDbContext db, AutoMessageService autoMsg, AuditService audit,
    AttendanceNotifyWorker notifyWorker) : ControllerBase
{
    /// <summary>Bu controller — ADMIN jurnali (xodim ham shu yerdan yozadi): jurnal siyosati faqat
    /// "Adminlarga ham qo'llash" yoqilganda cheklaydi (<see cref="JournalPolicy.CheckAsync"/>).</summary>
    private const bool IsAdminPath = true;

    /// <summary>Fanning chorakdagi darslari (sana + dars raqami). Bir kunda bir fan bir necha marta bo'lishi mumkin.</summary>
    [HttpGet("columns")]
    public async Task<ActionResult<IEnumerable<JournalColumnDto>>> GetColumns(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.ComputeColumnsAsync(db, classId, subjectId, quarter);

    /// <summary>Berilgan sanada o'tilgan darslar (guruh+fan+dars raqami): ptichka yoki baho/davomat bo'lganlar.</summary>
    [HttpGet("conducted")]
    public async Task<ActionResult<IEnumerable<ConductedLessonDto>>> Conducted([FromQuery] string date)
    {
        var fromNotes = await db.LessonNotes
            .Where(n => n.Date == date && n.Conducted)
            .Select(n => new ConductedLessonDto(n.ClassId, n.SubjectId, n.Period))
            .ToListAsync();
        var fromEntries = await db.JournalEntries
            .Where(e => e.Date == date && (e.Grade != null || e.ReasonId != null))
            .Select(e => new ConductedLessonDto(e.ClassId, e.SubjectId, e.Period))
            .ToListAsync();
        return fromNotes.Concat(fromEntries).Distinct().ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JournalEntryDto>>> GetEntries(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetEntriesAsync(db, classId, subjectId, quarter);

    /// <summary>Guruhning bitta OYLIK jurnali (guruh sahifasi uchun): ustunlar guruh dars kunlari bo'yicha
    /// avtomatik, qatorlar faqat faol o'quvchilar, oyma-oy navigatsiya. <paramref name="month"/> berilmasa — oxirgi oy.</summary>
    [HttpGet("group")]
    public async Task<ActionResult<GroupJournalDto>> GroupMonth(
        [FromQuery] string classId, [FromQuery] string? month)
    {
        var result = await JournalService.GroupMonthAsync(db, classId, month);
        return result is null ? NotFound(new { message = "Guruh topilmadi" }) : result;
    }

    /// <summary>Bitta katakni belgilash — baho yoki davomat sababi (mavjud bo'lsa ustiga yoziladi).</summary>
    [HttpPut]
    public async Task<IActionResult> SetEntry(SetJournalEntryRequest req)
    {
        // Jurnal siyosati (sana oynasi / faqat o'tilgan dars) — "Adminlarga ham qo'llash" yoqiq bo'lsa cheklaydi.
        // skipConducted: bitta katakka baho/davomat kiritish DARSNI O'ZI "o'tildi" qiladi (SetEntryAsync
        // ichida) — bulk-attendance bilan bir xil, shuning uchun "hali o'tilmagan" taqig'i bunga qo'llanmaydi
        // (aks holda birinchi bahoni qo'yish uchun avval "hammaga davomat" qilish SHART bo'lib qolardi).
        var deny = await JournalPolicy.CheckAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period,
            isAdmin: true, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        // ⚠️ SetEntryAsync SABABNI matn bilan tashlaydi ("Sana guruh yaratilishidan oldin" va h.k.).
        // Ushlanmasa 500 bo'lib chiqar va foydalanuvchi "server xatosi" ko'rardi — nima
        // noto'g'ri ekani esa yo'qolardi. `Reschedule` da bu allaqachon shunday qilingan.
        // «Keldi» xabari faqat YANGI kelganga — saqlashdan OLDINGI holat.
        var one = new[] { req.StudentId };
        var wasPresent = req.Present
            ? await JournalService.PresentStudentIdsAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period, one)
            : new HashSet<string>();
        bool newAbsence;
        try
        {
            newAbsence = await JournalService.SetEntryAsync(db, req, autoMsg,
                actorId: User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        // Bitta o'quvchi — so'rov ichida (kutish xavfi yo'q). Faqat YANGI "kelmadi" ("kech" emas).
        if (newAbsence)
            await AttendanceNotify.DispatchAbsencesAsync(db, autoMsg, req.ClassId, req.Date, req.ReasonId, one);
        // Arxivlangan o'quvchiga (eski yozuvlarda a'zoligi tirik bo'lishi mumkin) "keldi" ham ketmaydi.
        if (req.Present)
            await autoMsg.DispatchNewlyPresentAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period,
                await AttendanceNotify.NotArchivedAsync(db, one), wasPresent);
        return NoContent();
    }

    /// <summary>Bitta dars (sana) uchun BARCHA o'quvchiga birdan davomat: reasonId null = hammasi keldi, aks holda hammasi shu sabab bilan kelmadi.</summary>
    [HttpPost("bulk-attendance")]
    public async Task<IActionResult> BulkAttendance(BulkAttendanceRequest req)
    {
        // skipConducted: ommaviy davomat darsni o'zi "o'tildi" qiladi — conducted talabi unga qo'llanmaydi.
        var deny = await JournalPolicy.CheckAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period,
            isAdmin: true, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        // SABAB matn bilan qaytsin (yuqoridagi `SetEntry` bilan bir xil sabab).
        var wasPresent = !req.Absent
            ? await JournalService.PresentStudentIdsAsync(db, req.ClassId, req.SubjectId, req.Date, req.Period, req.StudentIds)
            : new HashSet<string>();
        // Ommaviy davomat xabarlari — FONDA, BAZADAGI NAVBAT orqali (AttendanceNotify.EnqueueBulkAsync →
        // AttendanceNotifyWorker): "kelmadi" faqat shu so'rovda YANGI kelmaganlarga, "keldi" faqat yangi
        // kelganlarga (wasPresent — saqlashdan oldingi holat). Navbat jurnal yozuvi bilan BITTA SaveChanges
        // da yoziladi (beforeSave). O'qituvchi ilovasi ham aynan shunday.
        // ⚠️ So'rov ichida yuborilmaydi: 25 ta SMS bittalab ketsa so'rov Cloudflare'ning 100 soniyasidan
        // oshar, foydalanuvchi qayta bosar va ota-onalar xabarni ikki marta olardi. ⚠️ Xotiradagi fon
        // vazifasi (Task.Run) ham EMAS: yuborish paytida deploy bo'lsa qolganlar jimgina yo'qolardi.
        try
        {
            await JournalService.BulkAttendanceAsync(db, req,
                beforeSave: r => AttendanceNotify.EnqueueBulkAsync(db, req, r, wasPresent),
                actorId: User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        notifyWorker.Wake();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearEntry(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        [FromQuery] string studentId, [FromQuery] string date, [FromQuery] int period)
    {
        // Tozalash ham sana oynasiga bo'ysunadi (yopiq davr yozuvini o'chirib ham bo'lmaydi).
        var deny = await JournalPolicy.CheckAsync(db, classId, subjectId, date, period,
            isAdmin: true, skipConducted: true);
        if (deny is not null) return BadRequest(new { message = deny });
        await JournalService.ClearEntryAsync(db, classId, subjectId, quarter, studentId, date, period);
        return NoContent();
    }

    /* ---------- Darsni boshqa kunga ko'chirish (bir martalik) ---------- */

    /// <summary>Bitta darsni bir martalik boshqa kunga ko'chiradi (sana + ixtiyoriy vaqt). Asl kundagi
    /// baho/davomat/mavzu ham yangi kunga ko'chadi. Xato bo'lsa 400 + xabar.</summary>
    [HttpPost("reschedule")]
    public async Task<ActionResult<LessonRescheduleDto>> Reschedule(RescheduleLessonRequest req)
    {
        // Band xona/o'qituvchiga ko'chirish — QAT'IY taqiq (409); faqat SUPERADMIN sabab bilan "baribir"
        // ko'chira oladi (CRM Phase 5, ScheduleConflicts.Decide — guruh formasi bilan bir xil qoida).
        var isSuper = User.IsInRole(WunderkindLC.Domain.Roles.SuperAdmin);
        var decision = ScheduleConflicts.Decide(1, req.Force, req.ForceReason, isSuper);
        try
        {
            var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            var rec = await JournalService.RescheduleLessonAsync(
                db, req.ClassId, req.FromDate, req.ToDate, req.Time, actor, isAdmin: IsAdminPath, audit,
                forceConflict: decision.Verdict == ScheduleConflicts.Verdict.Forced, forceReason: decision.Reason);
            return new LessonRescheduleDto(rec.Id, rec.FromDate, rec.ToDate, rec.Time);
        }
        catch (ScheduleConflictException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                roomConflict = true,
                message = decision.Message == ScheduleConflicts.ReasonRequiredMessage ? decision.Message : ex.Message,
                canForce = isSuper,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Ko'chirishni bekor qiladi — dars asl kuniga qaytadi (ma'lumotlar ham).</summary>
    [HttpDelete("reschedule/{id}")]
    public async Task<IActionResult> CancelReschedule(string id)
    {
        try
        {
            await JournalService.CancelRescheduleAsync(db, id, isAdmin: IsAdminPath, audit);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        return NoContent();
    }

    /* ---------- Jurnal boshqaruvi (tahrirlash siyosati) — "Guruhlar → Jurnal boshqaruvi" oynasi ---------- */

    /// <summary>Joriy jurnal siyosati (sana oynasi, faqat o'tilgan dars, adminlarga qo'llash).</summary>
    [HttpGet("policy")]
    public async Task<ActionResult<JournalPolicyDto>> GetPolicy() => await JournalPolicy.GetAsync(db);

    /// <summary>Jurnal siyosatini saqlaydi (noto'g'ri qiymatlar xavfsiz defaultga tushiriladi).</summary>
    /// <para>⚠️ Bu sozlamalar PULGA ta'sir qiladi (maoshni jurnalga bog'lash, muhlat, to'lamaganlarni
    /// yashirish) — shuning uchun har saqlash "O'zgarishlar tarixi"ga yoziladi (Sozlamalar bo'limi,
    /// <c>SettingsController.LogSettings</c> bilan bir xil shakl).</para>
    [HttpPut("policy")]
    public async Task<ActionResult<JournalPolicyDto>> SavePolicy(JournalPolicyDto req)
    {
        var before = await JournalPolicy.GetAsync(db);
        var meta = await JournalPolicy.ApplyAsync(db, req);
        var changed = db.ChangeTracker.Entries<WunderkindLC.Domain.CenterMeta>()
            .SelectMany(e => e.Properties.Where(p => e.State == EntityState.Added || p.IsModified))
            .Select(p => p.Metadata.Name)
            .Distinct().Order().ToList();
        if (changed.Count > 0)
            audit.Record("CenterMeta", "center", "update",
                $"Sozlama o'zgartirildi — Jurnal boshqaruvi ({string.Join(", ", changed)})",
                before: before, after: JournalPolicy.FromMeta(meta));
        await db.SaveChangesAsync();
        return await JournalPolicy.GetAsync(db);
    }

    /* ---------- Mavzu va uyga vazifa ---------- */

    [HttpGet("notes")]
    public async Task<ActionResult<IEnumerable<JournalTopicDto>>> GetNotes(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetNotesAsync(db, classId, subjectId, quarter);

    [HttpPut("notes")]
    public async Task<IActionResult> SetNote(SetLessonNoteRequest req)
    {
        await JournalService.SetNoteAsync(db, req);
        return NoContent();
    }

    /* ---------- Mavzularni Excel'dan ommaviy yuklash (mavzu + uy vazifa) ---------- */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Tanlangan guruh+fan+chorak uchun mavzular shabloni (.xlsx) — jadval kunlari oldindan to'ldirilgan.</summary>
    [HttpGet("topics-template")]
    public async Task<IActionResult> TopicsTemplate(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        var bytes = await JournalService.TopicTemplateXlsxAsync(db, classId, subjectId, quarter);
        return File(bytes, XlsxMime, "mavzular_shablon.xlsx");
    }

    /// <summary>To'ldirilgan Excel'dan mavzu+uy vazifani import qiladi (darsni "o'tilgan" qilmaydi).</summary>
    [HttpPost("topics-import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<TopicImportResultDto>> TopicsImport(
        [FromForm] string classId, [FromForm] string subjectId, [FromForm] int quarter, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(subjectId))
            return BadRequest(new { message = "Guruh va fan ko'rsatilishi shart" });
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, JournalService.TopicHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        return await JournalService.ImportTopicsAsync(db, classId, subjectId, quarter, rows);
    }
}
