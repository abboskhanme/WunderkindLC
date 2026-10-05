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
    /// <summary>Kurslar + har birining sinf oraliqlari (`TuitionPricing`). Maydonlar avvalgidek, ustiga `priceBands`.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CourseWithBandsDto>>> GetAll()
    {
        var subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        var bands = (await db.SubjectPriceBands.AsNoTracking().ToListAsync())
            .ToLookup(b => b.SubjectId, StringComparer.Ordinal);
        return subjects.Select(x => ToDto(x, bands[x.Id])).ToList();
    }

    private static CourseWithBandsDto ToDto(Subject x, IEnumerable<SubjectPriceBand> bands) => new(
        x.Id, x.Name, x.Price, x.LessonPrice,
        bands.OrderBy(b => b.GradeFrom)
            .Select(b => new SubjectPriceBandDto(b.GradeFrom, b.GradeTo, b.Price, b.LessonPrice)).ToList());

    /// <summary>Oraliqlar tekshiruvi + BEPUL kursga oraliq taqiqlanadi (`TuitionPricing.Resolve` izohi:
    /// bepul guruhga oraliq qo'llansa fon hisobi o'tgan barcha oylarga orqaga qarz yozardi).</summary>
    private static string? BandsError(IReadOnlyList<TuitionPricing.Band> bands, decimal coursePrice) =>
        TuitionPricing.Validate(bands)
        ?? (bands.Count > 0 && coursePrice <= 0
            ? "Sinf narxlari uchun kursning asosiy oylik narxini kiriting (bepul kursga oraliq qo'shilmaydi)"
            : null);

    private static List<TuitionPricing.Band> ToBands(IEnumerable<SubjectPriceBandDto> dtos) =>
        dtos.Select(b => new TuitionPricing.Band(b.GradeFrom, b.GradeTo, b.Price, b.LessonPrice))
            .OrderBy(b => b.GradeFrom).ToList();

    private static List<SubjectPriceBand> ToRows(string subjectId, IEnumerable<TuitionPricing.Band> bands) =>
        bands.Select(b => new SubjectPriceBand
        {
            SubjectId = subjectId, GradeFrom = b.GradeFrom, GradeTo = b.GradeTo, Price = b.Price, LessonPrice = b.LessonPrice,
        }).ToList();

    /// <summary>"3–7-sinf 225 000 · 8–9-sinf 275 000" — audit matni; bo'sh — "yo'q".</summary>
    private static string BandsText(IEnumerable<TuitionPricing.Band> bands)
    {
        var list = bands.Select(b => $"{TuitionPricing.Label(b)} {AuditService.Money(b.Price)}").ToList();
        return list.Count == 0 ? "yo'q" : string.Join(" · ", list);
    }

    /// <summary>Tarixdagi tur nomi — "Kurslar" bo'limiga tushadi (<c>AuditSections</c>).
    /// Ilgari kurs narxi <c>ClassFee</c> deb yozilar va tarixda "Guruhlar"ga tushib ketardi;
    /// ESKI yozuvlar o'sha joyda qoladi (bazadagi qatorlar qayta yozilmaydi).</summary>
    private const string AuditCourse = "Course";

    [HttpPost]
    public async Task<ActionResult<CourseWithBandsDto>> Create(SubjectPayload payload)
    {
        var bands = ToBands(payload.PriceBands ?? []);
        if (BandsError(bands, payload.Price) is { } bandError) return BadRequest(new { message = bandError });

        var subject = new Subject { Name = payload.Name, Price = payload.Price, LessonPrice = payload.LessonPrice };
        db.Subjects.Add(subject);
        var rows = ToRows(subject.Id, bands);
        db.SubjectPriceBands.AddRange(rows);
        audit.Record(AuditCourse, subject.Id, "create",
            $"Kurs qo'shildi: {subject.Name} — {AuditService.Money(subject.Price)} so'm/oy"
            + (bands.Count > 0 ? $"; sinf narxlari: {BandsText(bands)}" : ""));
        await db.SaveChangesAsync();
        return ToDto(subject, rows);
    }

    /// <summary>
    /// Kursni tahrirlash. Narx o'zgarsa — shu kursga bog'langan BARCHA guruhlarning oylik to'lovi
    /// (<c>MonthlyFee</c>) yangi narxga yangilanadi. <paramref name="applyFee"/> = true ("Ha — joriy
    /// oydan") bo'lsa, yangi ASOSIY narx shu guruhlardagi o'quvchilarning JORIY oy hisobiga ham qo'llanadi
    /// (balans farqqa moslanadi, qo'lda tahrirlangan oyliklar tegilmaydi). false ("Yo'q") bo'lsa —
    /// joriy oy eski asosiy narxda qoladi, yangi narx keyingi oy hisoblashidan amal qiladi.
    ///
    /// <para>⚠️ SINF NARXLARI esa joriy oyga HAR DOIM, so'ralmasdan qo'llanadi (foydalanuvchi talabi,
    /// 2026-10-05: "shu zahoti avtomatik"). Asosiy narx bilan bir so'rovda o'zgarib «Yo'q» tanlansa —
    /// joriy oyda sinfi oraliqqa tushmaganlar eski asosiy narxda qoladi, oraliqqa tushganlar yangi
    /// sinf narxiga o'tadi.</para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<CourseWithBandsDto>> Update(string id, SubjectPayload payload, [FromQuery] bool applyFee = false)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null) return NotFound();

        // SINF ORALIQLARI (`TuitionPricing`): null — tegilmaydi (eski mijoz), aks holda to'liq almashtiriladi.
        var oldRows = await db.SubjectPriceBands.Where(b => b.SubjectId == id).ToListAsync();
        var oldBands = oldRows.Select(b => new TuitionPricing.Band(b.GradeFrom, b.GradeTo, b.Price, b.LessonPrice))
            .OrderBy(b => b.GradeFrom).ToList();
        var newBands = payload.PriceBands is null ? oldBands : ToBands(payload.PriceBands);
        if (BandsError(newBands, payload.Price) is { } bandError) return BadRequest(new { message = bandError });
        var bandsChanged = !oldBands.SequenceEqual(newBands);
        var rows = oldRows;
        if (bandsChanged)
        {
            db.SubjectPriceBands.RemoveRange(oldRows);
            rows = ToRows(id, newBands);
            db.SubjectPriceBands.AddRange(rows);
        }

        var oldPrice = subject.Price;
        var oldName = subject.Name;
        subject.Name = payload.Name;
        subject.Price = payload.Price;
        subject.LessonPrice = payload.LessonPrice;
        var priceChanged = oldPrice != payload.Price;

        if (priceChanged || bandsChanged)
        {
            // Shu kursga bog'langan guruhlar oyligini yangi narxga yangilaymiz. «Joriy oydan» tanlansa —
            // joriy oy hisoblari har o'quvchi uchun YANGI oraliqlar bilan qayta moslanadi (hali saqlanmagan
            // oraliqlar shu yerda xotiradan beriladi — bazadan o'qilsa eskisi chiqardi).
            var pricing = PricingBook.From(rows);
            var oldPricing = PricingBook.From(oldRows);
            var groups = await db.Classes.Where(c => c.CourseId == id).ToListAsync();
            var appliedTotal = 0;
            foreach (var g in groups)
            {
                var gOld = g.MonthlyFee;
                if (priceChanged) g.MonthlyFee = payload.Price;
                // Joriy oy: asosiy narx — faqat «Ha» bo'lsa; sinf narxlari — har doim.
                var currentFee = applyFee ? g.MonthlyFee : gOld;
                if ((applyFee && gOld != g.MonthlyFee) || bandsChanged)
                    appliedTotal += await TuitionService.ApplyGroupFeeToCurrentMonthAsync(
                        db, g.Id, g.Name, currentFee, pricing, courseId: id, oldFee: gOld, oldPricing: oldPricing);
            }

            var tail = $" ({subject.Name}) — {groups.Count} ta guruhga"
                + (applyFee || !priceChanged
                    ? $", joriy oydan {appliedTotal} o'quvchiga qo'llandi"
                    : bandsChanged
                        ? $", sinf narxlari joriy oydan {appliedTotal} o'quvchiga qo'llandi, asosiy narx keyingi oydan"
                        : ", keyingi oydan amal qiladi");
            if (priceChanged)
                audit.Record(AuditCourse, subject.Id, "update",
                    $"Kurs narxi o'zgartirildi: {AuditService.Money(oldPrice)} → {AuditService.Money(payload.Price)} so'm" + tail,
                    before: new { Price = oldPrice, subject.Name }, after: new { subject.Price, subject.Name });
            if (bandsChanged)
                audit.Record(AuditCourse, subject.Id, "update",
                    $"Kurs sinf narxlari o'zgartirildi: {BandsText(oldBands)} → {BandsText(newBands)}" + tail,
                    before: new { PriceBands = BandsText(oldBands) }, after: new { PriceBands = BandsText(newBands) });
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

        return ToDto(subject, rows);
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
