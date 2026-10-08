using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH ALMASHGANDA CHEGIRMA HAM KO'CHADI — <c>ClassesController.TransferMember</c> va
/// «Tugatish (sertifikat bilan)» (<c>CompleteAndTransfer</c>) chaqiradi
/// (<c>.claude/rules/discounts.md</c> §5.5).
///
/// <para><b>MUAMMO:</b> chegirma qamrovi (<see cref="StudentDiscount.GroupId"/>) aniq GURUHGA
/// bog'langan, <see cref="DiscountRules.Resolve"/> esa hisob qatorining guruhi bilan solishtiradi.
/// O'quvchi boshqa guruhga o'tkazilgach yangi guruh to'liq narxda hisoblanar, profil esa
/// chegirmani hamon "amalda" deb ko'rsatardi.</para>
///
/// <para><b>QOIDA:</b> eski guruhga qaratilgan, o'tkazish oyidan boshlab hali kuchda bo'lgan
/// (<c>EndMonth</c> bo'sh yoki ≥ oy) AMALDAGI qator <b>IKKIGA BO'LINADI</b> (audit R1-3):
/// eski qamrovdagi qator <c>EndMonth = o'tkazish oyi</c> bilan yopiladi, yangi guruhga qaratilgan
/// yangi qator o'tkazish oyidan ochiladi — o'tkazish oyida IKKALASI ham amalda, har biri O'Z
/// guruhining hisobiga. ⚠️ Ilgari qator JOYIDA ko'chirilardi: o'sha oyda keyin chegirma tahrirlansa
/// yoki sinf o'zgarsa (<see cref="StudentDiscountService.ReapplyCurrentMonthAsync"/>,
/// <c>GradeRepricing</c>) eski guruhning qisman oy hisobi chegirmasiz qayta hisoblanib, o'quvchiga
/// YOLG'ON QARZ yozilardi. Chegirma o'tkazish oyida hali BOSHLANMAGAN bo'lsa (<c>StartMonth</c>
/// keyin) — bo'lish shart emas, qator joyida qayta qaratiladi (eski guruhga u hech qachon tushmagan).
/// <list type="bullet">
///   <item>⚠️ FAQAT BIR XIL FAN (kurs) bo'lsa — §5: boshqa fan = boshqa qoida. Matematikaga
///     berilgan chegirma ingliz tiliga jimgina o'tib ketmasin (admin buni ko'rmaydi, pul yo'qoladi).
///     Ikkala guruh ham kurssiz bo'lsa — boshqa fan ekaniga dalil yo'q, ko'chadi.</item>
///   <item>Yangi guruhda o'tkazish oyidan KEYIN ham kuchda bo'ladigan chegirma bo'lsa — invariant
///     (bitta qamrov — bitta qator) buzilmasin deb ko'chirilmaydi (<see cref="BlocksTarget"/>). O'sha
///     oyda yoki oldin tugaydigani to'siq emas (A→B→A qaytishi); joriy oydan oldin tugagani yopiladi.</item>
///   <item>«Barcha guruhlar» (<c>GroupId = null</c>) chegirmasi tegilmaydi — u baribir yangi guruhga ham tushadi.</item>
/// </list>
/// Eski guruh oylarining hisobi allaqachon <c>MonthlyCharge.Discount</c> da yozilgan — tarix buzilmaydi.</para>
///
/// <para>⚠️ TARTIB: eski guruhning chiqish (qisman) hisobi yozilgandan KEYIN, yangi guruhning
/// aktivlashtirish hisobidan OLDIN chaqirilsin — ikkalasi ham chegirmani shu paytdagi
/// qamrovdan oladi (<see cref="DiscountBook"/> kuzatilayotgan o'zgarishni ko'radi).</para>
/// <para><c>SaveChanges</c> QILINMAYDI. Audit — <c>StudentDiscount</c> turi (§11).</para>
/// </summary>
public static class DiscountTransfer
{
    /// <summary>Natija: ko'chirilgan, boshqa fan sababli qoldirilgan va to'qnashuv sababli qoldirilgan qatorlar.</summary>
    public readonly record struct Result(int Moved, int SkippedOtherSubject, int SkippedConflict);

    /// <summary>Ikki guruh BIR FANmi (kurs id'lari teng; ikkalasi ham kurssiz — teng).</summary>
    public static bool SameSubject(Group from, Group to) =>
        string.Equals(from.CourseId ?? "", to.CourseId ?? "", StringComparison.Ordinal);

    /// <summary>Qator <paramref name="month"/> dan boshlab hali kuchdami (tugash oyi bo'sh yoki keyin).</summary>
    public static bool StillRelevant(StudentDiscount d, string month) =>
        d.Status == StudentDiscount.StatusActive
        && (string.IsNullOrEmpty(d.EndMonth) || month.Length < 7
            || string.CompareOrdinal(d.EndMonth, month[..7]) >= 0);

    public static async Task<Result> MoveGroupScopedAsync(
        IAppDbContext db, AuditService? audit, Student student, Group from, Group to, string month,
        CancellationToken ct = default)
    {
        if (from.Id == to.Id) return default;
        var active = await StudentDiscountService.ActiveAsync(db, student.Id);
        var rows = active
            .Where(d => string.Equals(d.GroupId, from.Id, StringComparison.Ordinal) && StillRelevant(d, month))
            .ToList();
        if (rows.Count == 0) return default;

        if (!SameSubject(from, to))
        {
            audit?.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                $"Chegirma ko'chirilmadi: {string.Join(", ", rows.Select(Describe))} — «{to.Name}» boshqa fan " +
                $"(kerak bo'lsa yangi guruhga alohida bering) ({student.FullName})",
                studentId: student.Id);
            return new Result(0, rows.Count, 0);
        }

        // To'qnashuv — yangi guruhda o'tkazish oyidan KEYIN ham kuchda bo'ladigan qator. U oyda yoki
        // undan oldin TUGAYDIGAN qator (masalan A→B→A: avvalgi bo'linishdan qolgan) to'qnashuv EMAS —
        // oylar ustma-ust tushmaydi; aks holda guruhga qaytgan o'quvchi chegirmasini yo'qotardi.
        var targetRows = active.Where(d => string.Equals(d.GroupId, to.Id, StringComparison.Ordinal)).ToList();
        if (targetRows.Any(d => BlocksTarget(d, month)))
        {
            audit?.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                $"Chegirma ko'chirilmadi: {string.Join(", ", rows.Select(Describe))} — «{to.Name}» guruhida " +
                $"allaqachon amaldagi chegirma bor ({student.FullName})",
                studentId: student.Id);
            return new Result(0, 0, rows.Count);
        }

        string teacherName = "";
        if (!string.IsNullOrWhiteSpace(to.TeacherId))
            teacherName = await db.Teachers.AsNoTracking().Where(t => t.Id == to.TeacherId)
                .Select(t => t.FullName).FirstOrDefaultAsync(ct) ?? "";

        // Bir qamrovda bir nechta qator qolgan buzuq holatda ham faqat ENG YANGISI ko'chadi
        // (Resolve ham shuni oladi), qolganlari eski qamrovda tarix bo'lib turadi.
        var row = rows.OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal).First();
        var fromName = row.GroupName.Length > 0 ? row.GroupName : from.Name;
        var m7 = month.Length >= 7 ? month[..7] : month;

        // Yangi guruh qamrovidagi TARIXIY (joriy oydan oldin tugagan) qator yopiladi — qamrovda bitta
        // amaldagi qator qolsin. Joriy yoki keyingi oyda tugaydigani TEGILMAYDI: u o'z oyining hisobiga
        // hali kerak (qayta hisoblashda chegirma yo'qolmasin), oylar esa yangi qator bilan ustma-ust emas.
        var current = TuitionService.CurrentMonth();
        var retired = targetRows.Where(d => d.EndMonth.Length >= 7 && string.CompareOrdinal(d.EndMonth, current) < 0).ToList();
        foreach (var old in retired)
        {
            old.Status = StudentDiscount.StatusReplaced;
            old.EndedAt = AppClock.Iso();
            old.EndedBy = "Tizim: guruh almashtirildi";
        }
        var retiredNote = retired.Count > 0 ? $"; «{to.Name}» qamrovidagi muddati tugagan eski qator yopildi" : "";

        if (!DiscountRules.InForce(row, m7))
        {
            // O'tkazish oyida hali boshlanmagan (StartMonth keyin) — eski guruhga hech qachon
            // tushmagan, bo'lish shart emas: joyida qayta qaratiladi.
            row.GroupId = to.Id;
            row.GroupName = to.Name;
            row.TeacherId = string.IsNullOrWhiteSpace(to.TeacherId) ? null : to.TeacherId;
            row.TeacherName = teacherName;
            await StudentDiscountService.RefreshMirrorAsync(db, student);
            audit?.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                $"Chegirma yangi guruhga ko'chirildi: {Describe(row)} — «{fromName}» → «{to.Name}» " +
                $"(guruh almashtirildi{retiredNote}) ({student.FullName})",
                studentId: student.Id);
            return new Result(1, 0, rows.Count - 1);
        }

        // BO'LISH: eski qamrov o'tkazish oyi bilan yopiladi (o'sha oyning eski guruh hisobi chegirmasini
        // saqlab qoladi), yangi guruhga — o'sha oydan yangi qator (qiymat, sabab, muddat oxiri o'sha-o'sha).
        var next = new StudentDiscount
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            GroupId = to.Id,
            GroupName = to.Name,
            CourseId = row.CourseId,
            CourseName = row.CourseName,
            TeacherId = string.IsNullOrWhiteSpace(to.TeacherId) ? null : to.TeacherId,
            TeacherName = teacherName,
            Pct = row.Pct,
            Amount = row.Amount,
            StartMonth = m7,
            EndMonth = row.EndMonth,
            Reason = row.Reason,
            Status = StudentDiscount.StatusActive,
            CreatedAt = AppClock.Iso(),
            // Chegirmani BERGAN odam o'zgarmaydi — o'tkazishni kim qilgani auditda.
            CreatedBy = row.CreatedBy,
            CreatedById = row.CreatedById,
        };
        if (string.IsNullOrEmpty(next.CourseId) && !string.IsNullOrWhiteSpace(to.CourseId))
        {
            next.CourseId = to.CourseId;
            next.CourseName = await db.Subjects.AsNoTracking().Where(x => x.Id == to.CourseId)
                .Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        }
        var oldEnd = row.EndMonth;
        row.EndMonth = m7;
        db.StudentDiscounts.Add(next);
        await StudentDiscountService.RefreshMirrorAsync(db, student);

        audit?.Record(AuditService.EntityStudentDiscount, student.Id, "update",
            $"Chegirma «{fromName}» guruhida {m7} oyi bilan yakunlandi: {Describe(row)} " +
            $"(muddat oxiri {(oldEnd.Length > 0 ? oldEnd : "cheklanmagan")} → {m7}; guruh almashtirildi — " +
            $"o'tkazish oyining eski guruh hisobi chegirmasini saqlaydi) ({student.FullName})",
            studentId: student.Id);
        audit?.Record(AuditService.EntityStudentDiscount, student.Id, "create",
            $"Chegirma yangi guruhga ko'chirildi: {Describe(next)} — «{fromName}» → «{to.Name}», {m7} oyidan " +
            $"(guruh almashtirildi{retiredNote}) ({student.FullName})",
            studentId: student.Id);
        return new Result(1, 0, rows.Count - 1);
    }

    /// <summary>Yangi guruh qamrovidagi qator ko'chirishga TO'SIQmi: o'tkazish oyidan KEYIN ham kuchda
    /// (tugash oyi bo'sh yoki keyin). O'sha oyda/oldin tugaydigani to'siq emas (sof funksiya).</summary>
    public static bool BlocksTarget(StudentDiscount d, string month) =>
        d.Status == StudentDiscount.StatusActive
        && (string.IsNullOrEmpty(d.EndMonth) || month.Length < 7
            || string.CompareOrdinal(d.EndMonth, month[..7]) > 0);

    private static string Describe(StudentDiscount d) =>
        $"{DiscountRules.ScopeLabel(d)}: {DiscountRules.ValueLabel(d.Pct, d.Amount)}";
}
