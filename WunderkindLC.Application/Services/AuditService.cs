using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Moliyaga oid o'zgarishlarni tarix (audit) sifatida yozib boradi.
/// Yozuv joriy tranzaksiyaga qo'shiladi — controller'dagi SaveChanges uni ham saqlaydi.
/// </summary>
public class AuditService(IAppDbContext db, IHttpContextAccessor http)
{
    /// <summary>Pulni "850 000" ko'rinishida formatlash.</summary>
    public static string Money(decimal v) =>
        v.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    public const string EntityFinanceTransaction = "FinanceTransaction";
    public const string EntityTeacherSalary = "TeacherSalary";
    public const string EntityClassFee = "ClassFee";
    public const string EntityStudentDiscount = "StudentDiscount";
    public const string EntityTemplate = "CertificateTemplate";
    /// <summary>O'quvchi yozuvining o'zi (yaratildi/tahrirlandi/o'chirildi) — "kim yaratdi/o'zgartirdi".</summary>
    public const string EntityStudent = "Student";
    /// <summary>Guruh yozuvining o'zi (yaratildi/tahrirlandi/o'chirildi) — "kim yaratdi/o'zgartirdi".</summary>
    public const string EntityGroup = "Group";
    /// <summary>Reyting balini QO'LDA tuzatish (qo'shish / 0 ga tushirish) — per-guruh.</summary>
    public const string EntityStudentBall = "StudentBall";

    /// <summary>Audit yozuvini joriy DbContext'ga qo'shadi (hali SaveChanges qilinmaydi).</summary>
    public void Record(
        string entityType, string entityId, string action, string summary,
        object? before = null, object? after = null,
        string? studentId = null, string? teacherId = null)
    {
        var user = http.HttpContext?.User;
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Timestamp = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ActorId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user?.FindFirst("sub")?.Value,
            ActorName = user?.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim",
            Summary = summary,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
            StudentId = studentId,
            TeacherId = teacherId,
        });
    }

    /// <summary>Moliyaviy amal snapshot'i (Before/After uchun).</summary>
    public static object Snapshot(FinanceTransaction t) => new
    {
        t.Date,
        t.Direction,
        t.Category,
        t.Amount,
        t.Note,
        t.Month,
        t.StudentId,
        t.TeacherId,
        t.GroupId,
        t.Method,
        t.Comment,
    };

    /// <summary>
    /// O'quvchi profili snapshot'i (audit Before/After uchun) — asosiy shaxsiy maydonlar.
    /// Chegirma/balans BU YERGA kirmaydi — ular alohida moliyaviy audit'da yuritiladi.
    /// </summary>
    public static object StudentProfileSnapshot(Student s) => new
    {
        s.FullName,
        s.Phone,
        s.BirthDate,
        s.Gender,
        s.Address,
        s.ParentFullName,
        s.ParentPhone,
        s.FatherFullName,
        s.FatherPhone,
        s.MotherFullName,
        s.MotherPhone,
        s.ClassName,
        s.EnrollmentDate,
    };

    /// <summary>
    /// Guruh snapshot'i (audit Before/After uchun). TeacherId/CourseId o'zgarish ANIQLASH uchun
    /// kiritilgan (GUID sifatida ko'rsatilmaydi — frontend faqat yorliqli maydonlarni chizadi).
    /// </summary>
    public static object GroupSnapshot(Group g) => new
    {
        g.Name,
        g.Grade,
        g.Language,
        g.MonthlyFee,
        g.Room,
        g.Status,
        g.StartTime,
        g.EndTime,
        g.Note,
        g.TeacherId,
        g.CourseId,
    };

    /// <summary>
    /// O'qituvchi snapshot'i (audit Before/After uchun) — <see cref="GroupSnapshot"/> bilan bir xil
    /// maqsadda: "kim nimani o'zgartirdi".
    ///
    /// <para>Ro'yxatlar (fanlar, ruxsatlar) o'zgarish ANIQLASH uchun kiritilgan — frontend faqat
    /// yorlig'i bor maydonlarni chizadi, ya'ni ular ekranda GUID bo'lib chiqmaydi.</para>
    ///
    /// <para>Parol/hash bu yerga HECH QACHON kirmaydi — u <c>AppUser</c> da va tarixga yozilmaydi.</para>
    /// </summary>
    public static object TeacherSnapshot(Teacher t) => new
    {
        t.FullName,
        t.Phone,
        t.BirthDate,
        t.Gender,
        t.Address,
        t.HomeroomClass,
        t.SalaryMode,
        t.Salary,
        t.SalaryPercent,
        t.BonusPct,
        t.Category,
        t.SalaryStartDate,
        t.IsSupport,
        t.SubjectIds,
        t.Permissions,
    };
}
