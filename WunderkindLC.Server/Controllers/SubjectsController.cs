using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("schedule.courses")]
[Route("api/admin/subjects")]
public class SubjectsController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetAll() =>
        await db.Subjects.OrderBy(s => s.Name).ToListAsync();

    /// <summary>Tarixdagi tur nomi — "Kurslar" bo'limiga tushadi (<c>AuditSections</c>).
    /// Ilgari kurs narxi <c>ClassFee</c> deb yozilar va tarixda "Guruhlar"ga tushib ketardi;
    /// ESKI yozuvlar o'sha joyda qoladi (bazadagi qatorlar qayta yozilmaydi).</summary>
    private const string AuditCourse = "Course";

    [HttpPost]
    public async Task<ActionResult<Subject>> Create(SubjectPayload payload)
    {
        var subject = new Subject { Name = payload.Name, Price = payload.Price, LessonPrice = payload.LessonPrice };
        db.Subjects.Add(subject);
        audit.Record(AuditCourse, subject.Id, "create",
            $"Kurs qo'shildi: {subject.Name} — {AuditService.Money(subject.Price)} so'm/oy");
        await db.SaveChangesAsync();
        return subject;
    }

    /// <summary>
    /// Kursni tahrirlash. Narx o'zgarsa — shu kursga bog'langan BARCHA guruhlarning oylik to'lovi
    /// (<c>MonthlyFee</c>) yangi narxga yangilanadi. <paramref name="applyFee"/> = true ("Ha — joriy
    /// oydan") bo'lsa, yangi narx shu guruhlardagi o'quvchilarning JORIY oy hisobiga ham qo'llanadi
    /// (balans farqqa moslanadi, qo'lda tahrirlangan oyliklar tegilmaydi). false ("Yo'q") bo'lsa —
    /// joriy oy eski narxda qoladi, yangi narx keyingi oy hisoblashidan amal qiladi.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Subject>> Update(string id, SubjectPayload payload, [FromQuery] bool applyFee = false)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        var oldPrice = subject.Price;
        var oldName = subject.Name;
        subject.Name = payload.Name;
        subject.Price = payload.Price;
        subject.LessonPrice = payload.LessonPrice;

        if (oldPrice != payload.Price)
        {
            // Shu kursga bog'langan guruhlar oyligini yangi narxga yangilaymiz.
            var groups = await db.Classes.Where(c => c.CourseId == id).ToListAsync();
            var appliedTotal = 0;
            foreach (var g in groups)
            {
                var gOld = g.MonthlyFee;
                g.MonthlyFee = payload.Price;
                if (applyFee && gOld != payload.Price)
                    appliedTotal += await TuitionService.ApplyGroupFeeToCurrentMonthAsync(db, g.Id, g.Name, payload.Price);
            }

            var summary = $"Kurs narxi o'zgartirildi: {AuditService.Money(oldPrice)} → {AuditService.Money(payload.Price)} so'm ({subject.Name}) — {groups.Count} ta guruhga";
            summary += applyFee
                ? $", joriy oydan {appliedTotal} o'quvchiga qo'llandi"
                : ", keyingi oydan amal qiladi";
            audit.Record(AuditCourse, subject.Id, "update", summary,
                before: new { Price = oldPrice, subject.Name }, after: new { subject.Price, subject.Name });
        }

        if (oldName != payload.Name)
            audit.Record(AuditCourse, subject.Id, "update",
                $"Kurs nomi o'zgartirildi: {oldName} → {payload.Name}",
                before: new { Name = oldName }, after: new { subject.Name });

        await db.SaveChangesAsync();

        // Kurs NOMI o'zgarsa — lidlarning "Qiziqqan fani" (Lead.InterestSubject kurs NOMINI saqlaydi)
        // ham ko'chiriladi, aks holda CRM statistikasida eski nom alohida qator bo'lib qolardi
        // (lid manbasi — LeadSource — bilan bir xil konvensiya).
        if (!string.IsNullOrWhiteSpace(oldName) && oldName != payload.Name)
            await db.Leads.Where(l => l.InterestSubject == oldName)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.InterestSubject, payload.Name));

        return subject;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();
        // O'quv dasturlari mustaqil (Curriculum) — o'chirilmaydi, faqat biriktirilgan holat tozalanadi.
        await db.SubjectCurricula.Where(sc => sc.SubjectId == id).ExecuteDeleteAsync();
        audit.Record(AuditCourse, subject.Id, "delete", $"Kurs o'chirildi: {subject.Name}");
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Kursga biriktirilgan o'quv dasturlari (ko'p-ko'pga) ----

    /// <summary>Shu kursga biriktirilgan o'quv dasturlari ro'yxati (biriktirish tartibi bilan).</summary>
    [HttpGet("{id}/curricula")]
    public async Task<ActionResult<List<SubjectCurriculumDto>>> GetCurricula(string id)
    {
        var links = await db.SubjectCurricula
            .Where(sc => sc.SubjectId == id).OrderBy(sc => sc.Order).ToListAsync();
        var curriculumIds = links.Select(l => l.CurriculumId).ToList();
        var names = await db.Curricula.Where(c => curriculumIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        return links.Select(l => new SubjectCurriculumDto(
            l.CurriculumId, names.GetValueOrDefault(l.CurriculumId, "?"), l.Order)).ToList();
    }

    /// <summary>Bitta o'quv dasturini shu kursga biriktiradi (bitta dastur bir nechta kursga,
    /// bitta kurs bir nechta dasturga biriktirilishi mumkin — ko'p-ko'pga). Allaqachon biriktirilgan
    /// bo'lsa — o'zgarishsiz muvaffaqiyatli qaytadi.</summary>
    [HttpPost("{id}/curricula/{curriculumId}")]
    public async Task<ActionResult> AttachCurriculum(string id, string curriculumId)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound(new { message = "Kurs topilmadi" });
        var curriculum = await db.Curricula.FindAsync(curriculumId);
        if (curriculum is null) return NotFound(new { message = "Dastur topilmadi" });

        var exists = await db.SubjectCurricula.AnyAsync(sc => sc.SubjectId == id && sc.CurriculumId == curriculumId);
        if (!exists)
        {
            var maxOrder = await db.SubjectCurricula
                .Where(sc => sc.SubjectId == id).Select(sc => (int?)sc.Order).MaxAsync() ?? -1;
            db.SubjectCurricula.Add(new SubjectCurriculum { SubjectId = id, CurriculumId = curriculumId, Order = maxOrder + 1 });
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    /// <summary>Dasturni shu kursdan uzadi (progress/kontentga tegilmaydi — faqat bog'lanish o'chadi).</summary>
    [HttpDelete("{id}/curricula/{curriculumId}")]
    public async Task<ActionResult> DetachCurriculum(string id, string curriculumId)
    {
        await db.SubjectCurricula
            .Where(sc => sc.SubjectId == id && sc.CurriculumId == curriculumId).ExecuteDeleteAsync();
        return NoContent();
    }
}
