namespace WunderkindLC.Domain;

/* =====================================================================================
 *  SINOV DARSI TOPSHIRIQLARI (LC qo'shimchasi — LMS'da YO'Q). Qoidalar: `.claude/rules/hr-tasks.md` §8.
 *
 *  Lid mas'uliga (bo'lmasa sinovga yozgan xodimga) HR topshirig'i AVTOMATIK beriladi:
 *    before — sinov darsidan BIR KUN OLDIN (09:00): «Ertaga sinov darsiga keladiganlar» — bitta ro'yxat;
 *    after  — sinov darsidan KEYINGI KUNI (09:00): «Kecha sinov darsi bo'lganlar — bog'laning» — bitta ro'yxat.
 *  Topshiriqning o'zi oddiy `HrTask` (panel, «Mening topshiriqlarim», eslatmalar, KPI — hammasi o'sha). Bu qator
 *  faqat uni SINOV DARSLARIGA bog'laydi: kim uchun, qaysi kun, qaysi sinovlar — idempotentlik va avtomatik yopish uchun.
 * ===================================================================================== */

public static class HrTrialTaskKinds
{
    /// <summary>Sinovdan bir kun oldin — «ertaga keladiganlar».</summary>
    public const string Before = "before";
    /// <summary>Sinovdan keyingi kuni — «kecha bo'lganlar, bog'laning».</summary>
    public const string After = "after";
    public static readonly string[] All = [Before, After];
}

/// <summary>
/// Avtomatik sinov topshirig'i ↔ sinov darslari. Unikal: (xodim, tur, dars kuni) — bir kunga BITTA ro'yxat;
/// kech qo'shilgan sinov shu ro'yxatga QO'SHILADI (yangi topshiriq ochilmaydi).
/// </summary>
public class HrTrialTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>`hr_tasks.id` — ro'yxat topshirig'i.</summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>Kimga (`hr_employees.id`).</summary>
    public string EmployeeId { get; set; } = string.Empty;
    /// <summary><see cref="HrTrialTaskKinds"/>.</summary>
    public string Kind { get; set; } = HrTrialTaskKinds.Before;
    /// <summary>Sinov darsi KUNI (ertangi yoki kechagi) — markaz vaqti.</summary>
    public DateOnly LessonDate { get; set; }
    /// <summary>Ro'yxatdagi sinovlar (`TrialLesson.Id`), vergul bilan. Faqat qo'shiladi.</summary>
    public string TrialIds { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> TrialIdList =>
        TrialIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
