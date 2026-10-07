using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// MOSLASHUV (onboarding — 30 kunlik adaptatsiya) — <b>yagona mantiq</b>. Admin API
/// (<c>OnboardingController</c>) va ariza "Ishga qabul qilindi" bo'lganda avtomatik boshlash
/// (<c>CareerController.SetStatus</c>) shu yerdan foydalanadi. Sof qismlari (muddat, kechikish,
/// foiz, holat) bog'liqliksiz — <c>OnboardingServiceTests</c> bilan qoplangan.
///
/// <para>⚠️ SaveChanges CHAQIRILMAYDI (katalog seed'idan tashqari) — chaqiruvchi audit yozuvi
/// bilan BITTA tranzaksiyada saqlaydi (<c>.claude/rules/audit.md</c> §1).</para>
/// </summary>
public static class OnboardingService
{
    /// <summary>Audit tur nomlari — <see cref="AuditSections"/> da "Vakansiyalar" bo'limiga tushadi.</summary>
    public const string AuditEntity = "Onboarding";
    public const string AuditStepEntity = "OnboardingStep";

    public const string StatusActive = "active";
    /// <summary>Barcha qadamlar bajarildi — AVTOMATIK (oxirgi belgi qo'yilganda).</summary>
    public const string StatusCompleted = "completed";
    /// <summary>To'xtatildi (xodim ketdi / xato boshlangan) — qo'lda, qayta ochish mumkin.</summary>
    public const string StatusCancelled = "cancelled";

    public const string SourceHired = "hired";
    public const string SourceManual = "manual";

    /// <summary>Moslashuv davri (kun). Qadam muddati shu oraliqda bo'lishi shart.</summary>
    public const int PeriodDays = 30;

    /// <summary>Standart qadam (katalog bo'sh bo'lganda shu ro'yxat yoziladi).</summary>
    public record DefaultStep(string Title, string Description, int DayOffset);

    /// <summary>
    /// 11 ta STANDART qadam (2026-10 yig'ilishi). Muddat — boshlanishdan necha kun ichida.
    /// Faqat katalog BO'SH bo'lganda yoziladi; keyin admin nomini/muddatini/tartibini o'zgartiradi.
    /// </summary>
    public static readonly IReadOnlyList<DefaultStep> DefaultSteps =
    [
        new("Hujjatlarni topshirish", "Pasport nusxasi, diplom/sertifikatlar, rasm, ma'lumotnoma.", 1),
        new("Mehnat shartnomasini imzolash", "Shartnoma va lavozim yo'riqnomasi bilan tanishib imzolash.", 2),
        new("Ichki tartib-qoidalar bilan tanishish", "Ish vaqti, kiyinish, intizom va xavfsizlik qoidalari.", 2),
        new("Telegram kanal va guruhlarga ulanish", "Jamoa kanali, ish guruhlari va markaz botiga qo'shilish.", 1),
        new("CRM akkaunti ochish", "Tizimga login berish, kerakli bo'limlarga ruxsat.", 2),
        new("Ish joyi va jihozlar bilan tanishish", "Xona, jihozlar, kalitlar, turniket/kirish kartasi.", 3),
        new("Mentor biriktirish", "Birinchi oy davomida yordam beradigan tajribali hamkasb.", 3),
        new("Metodika va o'quv dasturi bilan tanishish", "Kurs dasturi, dars rejasi andozasi, baholash mezonlari.", 7),
        new("Darslarni kuzatish", "Tajribali o'qituvchilarning darslarida qatnashib, kuzatuv qaydi yozish.", 10),
        new("Birinchi mustaqil dars", "Mentor kuzatuvi ostida birinchi mustaqil dars va fikr-mulohaza.", 14),
        new("30-kunlik yakuniy suhbat", "Rahbar/HR bilan natijalar, fikr-mulohaza va keyingi rejalar.", 30),
    ];

    /* =============================================================================================
     *  KATALOG
     * ============================================================================================= */

    /// <summary>
    /// Katalog BO'SH bo'lsa standart 11 qadamni yozadi va SAQLAYDI (startup seed'i). Idempotent:
    /// biror qadam bo'lsa HECH NARSA qilmaydi (admin o'chirgan/tahrirlagan qadam qayta tug'ilmaydi).
    /// </summary>
    /// <returns>Qo'shilgan qadamlar soni.</returns>
    public static async Task<int> EnsureCatalogAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var added = await AddDefaultStepsIfEmptyAsync(db, ct);
        if (added.Count > 0) await db.SaveChangesAsync(ct);
        return added.Count;
    }

    /// <summary>
    /// Katalog bo'sh bo'lsa standart qadamlarni kontekstga QO'SHADI (saqlamaydi) va ularni qaytaradi;
    /// bo'sh bo'lmasa — bo'sh ro'yxat. SaveChanges chaqiruvchida (masalan arizaning bosqich
    /// o'zgarishi va audit yozuvi bilan BITTA tranzaksiyada).
    /// </summary>
    public static async Task<List<OnboardingStep>> AddDefaultStepsIfEmptyAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        if (db.OnboardingSteps.Local.Count > 0 || await db.OnboardingSteps.AnyAsync(ct)) return [];
        var now = AppClock.Iso();
        var rows = DefaultSteps.Select((d, i) => new OnboardingStep
        {
            Title = d.Title, Description = d.Description, DayOffset = d.DayOffset,
            Order = i, IsActive = true, CreatedAt = now, UpdatedAt = now,
        }).ToList();
        db.OnboardingSteps.AddRange(rows);
        return rows;
    }

    /// <summary>Qadam muddati to'g'rimi (0..<see cref="PeriodDays"/>).</summary>
    public static bool IsValidDayOffset(int dayOffset) => dayOffset is >= 0 and <= PeriodDays;

    /* =============================================================================================
     *  BOSHLASH
     * ============================================================================================= */

    /// <summary>
    /// Ariza "Ishga qabul qilindi" bo'lganda moslashuvni AVTOMATIK boshlaydi. <b>Idempotent</b>:
    /// shu arizaga moslashuv allaqachon bo'lsa (har qanday holatda — hatto to'xtatilgan bo'lsa ham)
    /// yangisi YARATILMAYDI, mavjudi qaytadi. Boshlanish sanasi — <paramref name="today"/>.
    /// SaveChanges chaqiruvchida.
    /// </summary>
    public static async Task<(EmployeeOnboarding Onboarding, bool Created)> StartForApplicationAsync(
        IAppDbContext db, JobApplication app, string actor, DateOnly today, CancellationToken ct = default)
    {
        var existing = db.EmployeeOnboardings.Local.FirstOrDefault(o => o.JobApplicationId == app.Id)
            ?? await db.EmployeeOnboardings.FirstOrDefaultAsync(o => o.JobApplicationId == app.Id, ct);
        if (existing is not null) return (existing, false);

        var o = new EmployeeOnboarding
        {
            FullName = app.FullName,
            Phone = app.Phone,
            Position = app.VacancyTitle,
            JobApplicationId = app.Id,
            ApplicationNumber = app.Number,
            StartDate = today.ToString("yyyy-MM-dd"),
            Source = SourceHired,
            Status = StatusActive,
            CreatedAt = AppClock.Iso(),
            CreatedBy = actor,
        };
        await AddWithTasksAsync(db, o, ct);
        return (o, true);
    }

    /// <summary>
    /// Moslashuvni yozadi va katalogning FAOL qadamlarini unga NUSXA qiladi. Katalog umuman bo'sh
    /// bo'lsa standart qadamlar ham (shu tranzaksiyada) yoziladi — aks holda cheklistsiz
    /// "moslashuv" yaratilib qolardi. SaveChanges chaqiruvchida.
    /// </summary>
    public static async Task<List<EmployeeOnboardingTask>> AddWithTasksAsync(
        IAppDbContext db, EmployeeOnboarding o, CancellationToken ct = default)
    {
        var steps = await AddDefaultStepsIfEmptyAsync(db, ct);
        if (steps.Count == 0)
            steps = await db.OnboardingSteps.AsNoTracking()
                .Where(s => s.IsActive)
                .ToListAsync(ct);

        db.EmployeeOnboardings.Add(o);
        var tasks = steps
            .Where(s => s.IsActive)
            .OrderBy(s => s.Order).ThenBy(s => s.DayOffset)
            .Select((s, i) => new EmployeeOnboardingTask
            {
                OnboardingId = o.Id,
                StepId = s.Id,
                Title = s.Title,
                Description = s.Description,
                DayOffset = s.DayOffset,
                Order = i,
            }).ToList();
        db.EmployeeOnboardingTasks.AddRange(tasks);
        return tasks;
    }

    /* =============================================================================================
     *  SOF QOIDALAR — muddat, kechikish, foiz, holat
     * ============================================================================================= */

    /// <summary>"yyyy-MM-dd" sanani o'qiydi (buzuq/bo'sh — null).</summary>
    public static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Qadam muddati = boshlanish + <paramref name="dayOffset"/> kun ("" — sana buzuq).</summary>
    public static string DueDate(string startDate, int dayOffset) =>
        ParseDate(startDate) is { } d ? d.AddDays(Math.Max(0, dayOffset)).ToString("yyyy-MM-dd") : "";

    /// <summary>
    /// KECHIKKAN qadam: bajarilmagan VA muddati BUGUNDAN OLDIN (bugungi muddat hali kechikmagan —
    /// kun oxirigacha vaqt bor). Sana buzuq bo'lsa — kechikmagan (yolg'on qizil bo'lmasin).
    /// </summary>
    public static bool IsOverdue(string startDate, int dayOffset, bool done, DateOnly today) =>
        !done && ParseDate(startDate) is { } d && d.AddDays(Math.Max(0, dayOffset)) < today;

    /// <summary>Bajarilganlar ulushi — PASTGA yaxlitlanadi: 100% faqat hammasi bajarilganda.</summary>
    public static int ProgressPct(int done, int total) =>
        total <= 0 ? 0 : (int)Math.Floor(Math.Clamp(done, 0, total) * 100.0 / total);

    /// <summary>Moslashuvning nechanchi kuni (boshlanish kuni = 1). Hali boshlanmagan bo'lsa — 0.</summary>
    public static int DayNumber(string startDate, DateOnly today) =>
        ParseDate(startDate) is { } d && d <= today ? today.DayNumber - d.DayNumber + 1 : 0;

    /// <summary>
    /// Qadam belgilangandan keyingi HOLAT: faol + hammasi bajarildi → "completed";
    /// "completed" + biror belgi olindi → yana "active". To'xtatilgan holat o'zgarmaydi.
    /// </summary>
    public static string StatusAfterTasks(string status, int done, int total)
    {
        if (status == StatusCancelled) return status;
        return total > 0 && done >= total ? StatusCompleted : StatusActive;
    }

    /* =============================================================================================
     *  DTO
     * ============================================================================================= */

    public static OnboardingTaskDto ToTaskDto(EmployeeOnboardingTask t, EmployeeOnboarding o, DateOnly today)
    {
        var done = t.DoneAt.Length > 0;
        return new OnboardingTaskDto(
            t.Id, t.StepId, t.Title, t.Description, t.DayOffset, t.Order,
            DueDate(o.StartDate, t.DayOffset), done, t.DoneAt, t.DoneBy, t.Note,
            o.Status == StatusActive && IsOverdue(o.StartDate, t.DayOffset, done, today));
    }

    /// <summary>Moslashuv DTO'si: jamlanma (foiz, kechikkanlar, navbatdagi qadam) qadamlardan hisoblanadi.</summary>
    public static EmployeeOnboardingDto ToDto(
        EmployeeOnboarding o, IEnumerable<EmployeeOnboardingTask> tasks, DateOnly today, bool withTasks)
    {
        var ordered = tasks.OrderBy(t => t.Order).ThenBy(t => t.DayOffset).ToList();
        var dtos = ordered.Select(t => ToTaskDto(t, o, today)).ToList();
        var done = dtos.Count(t => t.Done);
        var next = dtos.Where(t => !t.Done)
            .OrderBy(t => t.DayOffset).ThenBy(t => t.Order).FirstOrDefault();

        return new EmployeeOnboardingDto(
            o.Id, o.FullName, o.Phone, o.Position,
            o.JobApplicationId, o.ApplicationNumber, o.TeacherId, o.StaffUserId,
            o.StartDate, o.MentorId, o.MentorName,
            o.Status, o.Source, o.Note,
            o.CompletedAt, o.CancelledAt, o.CreatedAt, o.CreatedBy,
            dtos.Count, done, dtos.Count(t => t.Overdue), ProgressPct(done, dtos.Count),
            DayNumber(o.StartDate, today),
            next?.Title ?? "", next?.DueDate ?? "",
            withTasks ? dtos : null);
    }

    /// <summary>Monitoring jamlanmasi (ro'yxat DTO'laridan — bitta hisob, ikki joyda ayri ketmasin).</summary>
    public static OnboardingSummaryDto Summarize(IReadOnlyCollection<EmployeeOnboardingDto> items) => new(
        items.Count(i => i.Status == StatusActive),
        items.Count(i => i.Status == StatusCompleted),
        items.Count(i => i.Status == StatusCancelled),
        items.Count(i => i.Status == StatusActive && i.OverdueCount > 0),
        items.Where(i => i.Status == StatusActive).Sum(i => i.OverdueCount));
}
