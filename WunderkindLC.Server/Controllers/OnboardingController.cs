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
/// MOSLASHUV (onboarding — 30 kunlik adaptatsiya) — "Boshqaruv → Moslashuv" sahifasining API'si:
/// <list type="bullet">
///   <item><b>Moslashuvlar</b> — yangi xodimlar ro'yxati (foiz, kechikkan qadamlar), tafsilot
///     (cheklist), qo'lda boshlash, qadamni belgilash, to'xtatish/qayta ochish.</item>
///   <item><b>Qadamlar katalogi</b> — standart 11 qadam (tahrirlanadi; yangi moslashuvlarga NUSXA
///     qilinadi, davom etayotganlariga tegmaydi).</item>
/// </list>
/// Avtomatik boshlash bu yerda EMAS — <c>CareerController.SetStatus</c> da (ariza "hired" bo'lganda).
///
/// <para>Ruxsat: <c>vacancies.onboarding</c> (bo'lim kaliti <c>vacancies</c> merosi bilan ochadi).
/// O'QISH ham darvozalangan (<c>ReadRequiresPerm</c>): javobda yangi xodimlarning telefoni va
/// ichki izohlar bor — bo'limlararo o'qish uchun kerak emas.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("vacancies.onboarding", ReadRequiresPerm = true)]
[Route("api/admin/onboarding")]
public class OnboardingController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "Admin";
    private const string Entity = OnboardingService.AuditEntity;
    private const string StepEntity = OnboardingService.AuditStepEntity;

    // =============================================================================================
    //  MOSLASHUVLAR
    // =============================================================================================

    /// <summary>
    /// Moslashuvlar ro'yxati (qadamlarsiz, jamlanma bilan). <paramref name="status"/>:
    /// "active" | "completed" | "cancelled" | "overdue" (faol + kechikkan qadami bor) | bo'sh = hammasi.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<EmployeeOnboardingDto>>> List(
        [FromQuery] string? status, [FromQuery] string? q)
    {
        var items = await LoadAllAsync();
        IEnumerable<EmployeeOnboardingDto> list = items;

        list = status switch
        {
            OnboardingService.StatusActive or OnboardingService.StatusCompleted or OnboardingService.StatusCancelled
                => list.Where(i => i.Status == status),
            "overdue" => list.Where(i => i.Status == OnboardingService.StatusActive && i.OverdueCount > 0),
            _ => list,
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            list = list.Where(i =>
                i.FullName.ToLowerInvariant().Contains(term) ||
                i.Position.ToLowerInvariant().Contains(term) ||
                i.MentorName.ToLowerInvariant().Contains(term) ||
                i.Phone.Contains(term));
        }

        // Faollar tepada (eng ko'p kechikkani birinchi), keyin yangi boshlanganlari.
        return list
            .OrderBy(i => i.Status == OnboardingService.StatusActive ? 0 : 1)
            .ThenByDescending(i => i.OverdueCount)
            .ThenByDescending(i => i.StartDate)
            .ToList();
    }

    /// <summary>Monitoring tepasidagi jamlanma (holatlar soni, kechikkanlar).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<OnboardingSummaryDto>> Summary() =>
        OnboardingService.Summarize(await LoadAllAsync());

    /// <summary>Bitta moslashuv — cheklist (qadamlar) bilan.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<EmployeeOnboardingDto>> Get(string id)
    {
        var o = await db.EmployeeOnboardings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });
        return await WithTasksAsync(o);
    }

    /// <summary>Qo'lda boshlash ("Yangi xodim moslashuvi") — katalogning faol qadamlari nusxa qilinadi.</summary>
    [HttpPost]
    public async Task<ActionResult<EmployeeOnboardingDto>> Create(EmployeeOnboardingPayload p)
    {
        var o = new EmployeeOnboarding
        {
            Source = OnboardingService.SourceManual,
            Status = OnboardingService.StatusActive,
            CreatedAt = AppClock.Iso(),
            CreatedBy = Actor,
        };
        if (await ApplyAsync(o, p) is { } err) return BadRequest(new { message = err });

        await OnboardingService.AddWithTasksAsync(db, o);
        audit.Record(Entity, o.Id, "create",
            $"Moslashuv (30 kun) qo'lda boshlandi: {o.FullName}" +
            (o.Position.Length > 0 ? $" — {o.Position}" : "") + $", boshlanish {o.StartDate}");
        await db.SaveChangesAsync();
        return await WithTasksAsync(o);
    }

    /// <summary>Sarlavhani tahrirlash (ism, telefon, lavozim, boshlanish sanasi, mentor, bog'lanish, izoh).
    /// Boshlanish sanasi o'zgarsa barcha qadam muddatlari shunga qarab siljiydi.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<EmployeeOnboardingDto>> Update(string id, EmployeeOnboardingPayload p)
    {
        var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });

        var oldStart = o.StartDate;
        if (await ApplyAsync(o, p) is { } err) return BadRequest(new { message = err });

        audit.Record(Entity, o.Id, "update",
            $"Moslashuv ma'lumotlari tahrirlandi: {o.FullName}" +
            (oldStart != o.StartDate ? $" (boshlanish {oldStart} → {o.StartDate})" : ""));
        await db.SaveChangesAsync();
        return await WithTasksAsync(o);
    }

    /// <summary>
    /// Qadamni BELGILASH yoki belgisini olish (+ izoh). Oxirgi qadam belgilansa moslashuv
    /// avtomatik "yakunlandi" bo'ladi; yakunlangandan keyin belgi olinsa — yana faol.
    /// To'xtatilgan moslashuvda qadam o'zgartirilmaydi (avval qayta oching).
    /// </summary>
    [HttpPut("{id}/tasks/{taskId}")]
    public async Task<ActionResult<EmployeeOnboardingDto>> SetTask(string id, string taskId, OnboardingTaskPayload p)
    {
        var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });
        if (o.Status == OnboardingService.StatusCancelled)
            return BadRequest(new { message = "Moslashuv to'xtatilgan — qadamni belgilash uchun avval uni qayta oching." });

        var tasks = await db.EmployeeOnboardingTasks.Where(t => t.OnboardingId == id).ToListAsync();
        var t = tasks.FirstOrDefault(x => x.Id == taskId);
        if (t is null) return NotFound(new { message = "Qadam topilmadi" });

        var wasDone = t.DoneAt.Length > 0;
        if (p.Done && !wasDone)
        {
            t.DoneAt = AppClock.Iso();
            t.DoneBy = Actor;
        }
        else if (!p.Done && wasDone)
        {
            t.DoneAt = "";
            t.DoneBy = "";
        }
        var note = Trim(p.Note, 1000);
        var noteChanged = note != t.Note;
        t.Note = note;

        var oldStatus = o.Status;
        o.Status = OnboardingService.StatusAfterTasks(o.Status, tasks.Count(x => x.DoneAt.Length > 0), tasks.Count);
        if (o.Status != oldStatus)
            o.CompletedAt = o.Status == OnboardingService.StatusCompleted ? AppClock.Iso() : "";

        if (wasDone != p.Done || noteChanged)
        {
            var what = wasDone != p.Done
                ? (p.Done ? "bajarildi" : "belgisi olib tashlandi")
                : "izohi o'zgartirildi";
            audit.Record(Entity, o.Id, "update",
                $"Moslashuv qadami {what}: «{t.Title}» — {o.FullName}" +
                (note.Length > 0 && noteChanged ? $" (izoh: {note})" : "") +
                (o.Status == OnboardingService.StatusCompleted && oldStatus != o.Status
                    ? ". Barcha qadamlar bajarildi — moslashuv YAKUNLANDI" : "") +
                (oldStatus == OnboardingService.StatusCompleted && o.Status == OnboardingService.StatusActive
                    ? ". Moslashuv qayta faol" : ""));
        }
        await db.SaveChangesAsync();
        return OnboardingService.ToDto(o, tasks, AppClock.Today, withTasks: true);
    }

    /// <summary>To'xtatish (xodim ketdi / xato boshlangan) — monitoringdan chiqadi, tarix saqlanadi.</summary>
    [HttpPut("{id}/cancel")]
    public async Task<ActionResult<EmployeeOnboardingDto>> Cancel(string id)
    {
        var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });
        if (o.Status != OnboardingService.StatusCancelled)
        {
            o.Status = OnboardingService.StatusCancelled;
            o.CancelledAt = AppClock.Iso();
            audit.Record(Entity, o.Id, "update", $"Moslashuv to'xtatildi: {o.FullName}");
            await db.SaveChangesAsync();
        }
        return await WithTasksAsync(o);
    }

    /// <summary>Qayta ochish — holat qadamlarga qarab tiklanadi (hammasi bajarilgan bo'lsa "yakunlandi").</summary>
    [HttpPut("{id}/reopen")]
    public async Task<ActionResult<EmployeeOnboardingDto>> Reopen(string id)
    {
        var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });
        if (o.Status == OnboardingService.StatusCancelled)
        {
            var tasks = await db.EmployeeOnboardingTasks.AsNoTracking().Where(t => t.OnboardingId == id).ToListAsync();
            o.Status = OnboardingService.StatusAfterTasks(
                OnboardingService.StatusActive, tasks.Count(t => t.DoneAt.Length > 0), tasks.Count);
            o.CancelledAt = "";
            o.CompletedAt = o.Status == OnboardingService.StatusCompleted ? AppClock.Iso() : "";
            audit.Record(Entity, o.Id, "update", $"Moslashuv qayta ochildi: {o.FullName}");
            await db.SaveChangesAsync();
        }
        return await WithTasksAsync(o);
    }

    /// <summary>Butunlay o'chirish (qadamlari bilan) — xato yaratilgan yozuv uchun; odatda "to'xtatish" afzal.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var o = await db.EmployeeOnboardings.FirstOrDefaultAsync(x => x.Id == id);
        if (o is null) return NotFound(new { message = "Moslashuv topilmadi" });

        var tasks = await db.EmployeeOnboardingTasks.Where(t => t.OnboardingId == id).ToListAsync();
        db.EmployeeOnboardingTasks.RemoveRange(tasks);
        audit.Record(Entity, o.Id, "delete", $"Moslashuv o'chirildi: {o.FullName}");
        db.EmployeeOnboardings.Remove(o);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Mentor va akkaunt tanlash uchun odamlar: faol o'qituvchilar + panel xodimlari (faqat ism).</summary>
    [HttpGet("people")]
    public async Task<ActionResult<List<OnboardingPersonDto>>> People()
    {
        var teachers = await db.Teachers.AsNoTracking().Where(t => !t.IsArchived)
            .OrderBy(t => t.FullName)
            .Select(t => new OnboardingPersonDto(t.Id, "teacher", t.FullName)).ToListAsync();
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin)
            .OrderBy(u => u.FullName)
            .Select(u => new OnboardingPersonDto(u.Id, "staff", u.FullName)).ToListAsync();
        return teachers.Concat(staff).ToList();
    }

    // =============================================================================================
    //  QADAMLAR KATALOGI
    // =============================================================================================

    /// <summary>Katalog (tartib bilan, o'chirilganlari ham — UI'da kulrang).</summary>
    [HttpGet("steps")]
    public async Task<ActionResult<List<OnboardingStepDto>>> Steps() =>
        await db.OnboardingSteps.AsNoTracking()
            .OrderBy(s => s.Order).ThenBy(s => s.DayOffset)
            .Select(s => new OnboardingStepDto(s.Id, s.Title, s.Description, s.DayOffset, s.Order, s.IsActive))
            .ToListAsync();

    [HttpPost("steps")]
    public async Task<ActionResult<OnboardingStepDto>> CreateStep(OnboardingStepPayload p)
    {
        if (ValidateStep(p) is { } err) return BadRequest(new { message = err });
        var maxOrder = await db.OnboardingSteps.Select(s => (int?)s.Order).MaxAsync() ?? -1;
        var now = AppClock.Iso();
        var s = new OnboardingStep
        {
            Title = Trim(p.Title, 200),
            Description = Trim(p.Description, 1000),
            DayOffset = p.DayOffset,
            Order = maxOrder + 1,
            IsActive = p.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.OnboardingSteps.Add(s);
        audit.Record(StepEntity, s.Id, "create", $"Moslashuv katalogiga qadam qo'shildi: «{s.Title}» ({s.DayOffset}-kun)");
        await db.SaveChangesAsync();
        return StepDto(s);
    }

    /// <summary>Katalog qadamini tahrirlash — FAQAT yangi moslashuvlarga ta'sir qiladi (eskilari nusxa).</summary>
    [HttpPut("steps/{stepId}")]
    public async Task<ActionResult<OnboardingStepDto>> UpdateStep(string stepId, OnboardingStepPayload p)
    {
        if (ValidateStep(p) is { } err) return BadRequest(new { message = err });
        var s = await db.OnboardingSteps.FirstOrDefaultAsync(x => x.Id == stepId);
        if (s is null) return NotFound(new { message = "Qadam topilmadi" });

        s.Title = Trim(p.Title, 200);
        s.Description = Trim(p.Description, 1000);
        s.DayOffset = p.DayOffset;
        s.IsActive = p.IsActive;
        s.UpdatedAt = AppClock.Iso();
        audit.Record(StepEntity, s.Id, "update",
            $"Moslashuv katalogi qadami tahrirlandi: «{s.Title}» ({s.DayOffset}-kun{(s.IsActive ? "" : ", o'chirilgan")})");
        await db.SaveChangesAsync();
        return StepDto(s);
    }

    /// <summary>Katalogdan o'chirish — davom etayotgan moslashuvlardagi NUSXA qadamlar tegilmaydi.</summary>
    [HttpDelete("steps/{stepId}")]
    public async Task<IActionResult> DeleteStep(string stepId)
    {
        var s = await db.OnboardingSteps.FirstOrDefaultAsync(x => x.Id == stepId);
        if (s is null) return NotFound(new { message = "Qadam topilmadi" });
        audit.Record(StepEntity, s.Id, "delete", $"Moslashuv katalogidan qadam o'chirildi: «{s.Title}»");
        db.OnboardingSteps.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Katalog tartibi — id'lar yangi tartibda (ro'yxatda yo'qlari oxiriga, eski tartibida).</summary>
    [HttpPut("steps/reorder")]
    public async Task<ActionResult<List<OnboardingStepDto>>> ReorderSteps(OnboardingReorderPayload p)
    {
        var steps = await db.OnboardingSteps.ToListAsync();
        var pos = (p.Ids ?? []).Select((id, i) => (id, i)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First().i);
        var ordered = steps
            .OrderBy(s => pos.TryGetValue(s.Id, out var i) ? i : int.MaxValue)
            .ThenBy(s => s.Order)
            .ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        audit.Record(StepEntity, "order", "update", "Moslashuv katalogi qadamlari tartibi o'zgartirildi");
        await db.SaveChangesAsync();
        return ordered.Select(StepDto).ToList();
    }

    // =============================================================================================
    //  YORDAMCHILAR
    // =============================================================================================

    /// <summary>Barcha moslashuvlar DTO'si (qadamlari bilan hisoblangan jamlanma) — ikki so'rovda.</summary>
    private async Task<List<EmployeeOnboardingDto>> LoadAllAsync()
    {
        var list = await db.EmployeeOnboardings.AsNoTracking().ToListAsync();
        var ids = list.Select(o => o.Id).ToList();
        var tasks = (await db.EmployeeOnboardingTasks.AsNoTracking()
                .Where(t => ids.Contains(t.OnboardingId)).ToListAsync())
            .ToLookup(t => t.OnboardingId);
        var today = AppClock.Today;
        return list.Select(o => OnboardingService.ToDto(o, tasks[o.Id], today, withTasks: false)).ToList();
    }

    private async Task<EmployeeOnboardingDto> WithTasksAsync(EmployeeOnboarding o)
    {
        var tasks = db.EmployeeOnboardingTasks.Local.Where(t => t.OnboardingId == o.Id).ToList();
        if (tasks.Count == 0)
            tasks = await db.EmployeeOnboardingTasks.AsNoTracking().Where(t => t.OnboardingId == o.Id).ToListAsync();
        return OnboardingService.ToDto(o, tasks, AppClock.Today, withTasks: true);
    }

    /// <summary>Forma maydonlarini tekshiradi va yozadi (xato — matn bilan, aks holda null).</summary>
    private async Task<string?> ApplyAsync(EmployeeOnboarding o, EmployeeOnboardingPayload p)
    {
        var name = Trim(p.FullName, 150);
        if (name.Length < 3) return "Xodimning F.I.Sh. ni kiriting";
        var start = (p.StartDate ?? "").Trim();
        if (start.Length == 0) start = AppClock.Today.ToString("yyyy-MM-dd");
        if (OnboardingService.ParseDate(start) is null) return "Boshlanish sanasi noto'g'ri (yyyy-MM-dd)";

        var mentorId = (p.MentorId ?? "").Trim();
        var mentorName = "";
        if (mentorId.Length > 0)
        {
            mentorName = await db.Teachers.Where(t => t.Id == mentorId).Select(t => t.FullName).FirstOrDefaultAsync()
                ?? await db.Users.Where(u => u.Id == mentorId).Select(u => u.FullName).FirstOrDefaultAsync()
                ?? "";
            if (mentorName.Length == 0) return "Mentor topilmadi";
        }

        var teacherId = (p.TeacherId ?? "").Trim();
        if (teacherId.Length > 0 && !await db.Teachers.AnyAsync(t => t.Id == teacherId))
            return "O'qituvchi yozuvi topilmadi";
        var staffUserId = (p.StaffUserId ?? "").Trim();
        if (staffUserId.Length > 0 && !await db.Users.AnyAsync(u => u.Id == staffUserId))
            return "Xodim akkaunti topilmadi";
        if (teacherId.Length > 0 && staffUserId.Length > 0)
            return "Xodim yo o'qituvchi, yo panel akkaunti bilan bog'lanadi — bittasini tanlang";

        o.FullName = name;
        o.Phone = Trim(p.Phone, 50);
        o.Position = Trim(p.Position, 200);
        o.StartDate = start;
        o.MentorId = mentorId;
        o.MentorName = mentorName;
        o.TeacherId = teacherId;
        o.StaffUserId = staffUserId;
        o.Note = Trim(p.Note, 2000);
        return null;
    }

    private static string? ValidateStep(OnboardingStepPayload p)
    {
        var title = Trim(p.Title, 200);
        if (title.Length < 2) return "Qadam nomini kiriting";
        if (!OnboardingService.IsValidDayOffset(p.DayOffset))
            return $"Muddat 0 dan {OnboardingService.PeriodDays} gacha kun bo'lishi kerak";
        return null;
    }

    private static OnboardingStepDto StepDto(OnboardingStep s) =>
        new(s.Id, s.Title, s.Description, s.DayOffset, s.Order, s.IsActive);

    private static string Trim(string? s, int max)
    {
        var v = (s ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }
}
