using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// CHEGIRMA REGISTRI ustidagi SOF qoidalar (bazasiz, testlanadigan).
///
/// <para>⚠️ Bu yerda PUL hisoblanmaydi. Oylik hisob avvalgidek <c>TuitionService</c> da va
/// <see cref="Student"/> maydonlariga tayanadi; registr faqat "kim, qachon, qancha, nega"
/// savoliga javob beradi.</para>
/// </summary>
public static class DiscountRules
{
    /// <summary>Holatning o'zbekcha yorlig'i (UI faqat shuni ko'rsatadi).</summary>
    public static string StatusLabel(string status) => status switch
    {
        StudentDiscount.StatusActive => "Amalda",
        StudentDiscount.StatusReplaced => "Almashtirilgan",
        StudentDiscount.StatusCancelled => "Bekor qilingan",
        _ => status,
    };

    /// <summary>
    /// Chegirma berilgan OYDA ("yyyy-MM") HAQIQATAN amaldami: holati
    /// <see cref="StudentDiscount.StatusActive"/> VA oy
    /// <see cref="StudentDiscount.StartMonth"/>..<see cref="StudentDiscount.EndMonth"/>
    /// oralig'ida (INKLYUZIV; bo'sh chegara — cheklovsiz).
    ///
    /// <para>⚠️ Oy oralig'i tekshiruvi bu yerda QAYTA YOZILMAGAN —
    /// <see cref="TuitionService.DiscountActiveForMonth(string?, string?, string)"/> chaqiriladi.
    /// Nusxa ko'chirilsa, ikkisi vaqt o'tib ayrilib ketardi: registr "amalda" deb ko'rsatgan
    /// chegirma hisobda qo'llanmay qolardi.</para>
    /// </summary>
    public static bool InForce(StudentDiscount d, string month) =>
        d.Status == StudentDiscount.StatusActive
        && TuitionService.DiscountActiveForMonth(d.StartMonth, d.EndMonth, month);
}

/// <summary>
/// Chegirma so'rovi — "qanday chegirma qo'yilsin". Yaratish/tahrirlash formasidan ham,
/// <see cref="Student"/> maydonlaridan ham quriladi (ikki yo'l bir xil ma'lumot beradi).
/// </summary>
/// <param name="Pct">Foiz (0..100).</param>
/// <param name="Amount">Aniq summa (so'm), foizdan keyin ayriladi.</param>
/// <param name="StartMonth">"yyyy-MM" yoki "" (cheklovsiz).</param>
/// <param name="EndMonth">"yyyy-MM" yoki "" (cheklovsiz).</param>
/// <param name="Reason">Sabab/izoh (<see cref="Student.DiscountNote"/> bilan bir xil matn).</param>
/// <param name="GroupId">null/"" — BARCHA guruh hisoblariga; id — faqat o'sha guruhga.</param>
public record DiscountSpec(
    int Pct, decimal Amount, string StartMonth, string EndMonth, string Reason, string? GroupId)
{
    /// <summary>Chegirma NOLINCHI — bunday qator registrga YOZILMAYDI (chegirma yo'q degani).</summary>
    public bool IsEmpty => Pct <= 0 && Amount <= 0m;

    /// <summary>Chegaralarni normallashtiradi: foiz 0..100, summa &gt;= 0, matnlar trim,
    /// bo'sh guruh — null.</summary>
    public DiscountSpec Normalized() => new(
        Math.Clamp(Pct, 0, 100),
        Math.Max(0m, Amount),
        (StartMonth ?? "").Trim(),
        (EndMonth ?? "").Trim(),
        (Reason ?? "").Trim(),
        string.IsNullOrWhiteSpace(GroupId) ? null : GroupId.Trim());

    /// <summary>O'quvchining JORIY (Student.Discount*) chegirmasi.</summary>
    public static DiscountSpec From(Student s) => new(
        s.DiscountPct, s.DiscountAmount, s.DiscountStartMonth ?? "", s.DiscountEndMonth ?? "",
        s.DiscountNote ?? "", string.IsNullOrWhiteSpace(s.DiscountGroupId) ? null : s.DiscountGroupId);
}

/// <summary>
/// CHEGIRMA REGISTRIGA YOZUVCHI YAGONA JOY.
///
/// <para>⚠️ <b>INVARIANT:</b> har o'quvchida ko'pi bilan BITTA
/// <see cref="StudentDiscount.StatusActive"/> qator bo'ladi va u AYNAN
/// <c>Student.Discount*</c> maydonlariga mos keladi. Registr <c>Student</c> ni ALMASHTIRMAYDI —
/// AKS ETTIRADI: pul hisobi (<c>TuitionService.DiscountForMonth</c>) bu jadvalni umuman
/// o'qimaydi. Shuning uchun registrga boshqa joydan YOZMANG — aks holda ikki manba ayrilib
/// ketadi va hisobot yolg'on ko'rsatadi.</para>
///
/// <para>Hech bir metod <c>SaveChanges</c> QILMAYDI — chaqiruvchining tranzaksiyasiga qo'shiladi
/// (<c>AuditService.Record</c> bilan bir xil qoida).</para>
/// </summary>
public static class StudentDiscountService
{
    /// <summary>O'quvchining hozir amaldagi registr qatori (yo'q bo'lsa null).</summary>
    public static async Task<StudentDiscount?> CurrentAsync(IAppDbContext db, string studentId) =>
        await db.StudentDiscounts
            .Where(d => d.StudentId == studentId && d.Status == StudentDiscount.StatusActive)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Yangi chegirma qo'yadi (yoki chegirmani OLIB TASHLAYDI, agar <paramref name="spec"/>
    /// nolinchi bo'lsa):
    /// <list type="number">
    ///   <item>mavjud <c>active</c> qator <c>replaced</c> bo'lib yopiladi;</item>
    ///   <item>spec nolinchi BO'LMASA — yangi <c>active</c> qator ochiladi va
    ///         <c>Student.Discount*</c> spec'dan to'ldiriladi;</item>
    ///   <item>nolinchi bo'lsa — <c>Student.Discount*</c> TOZALANADI, yangi qator ochilmaydi
    ///         (chegirmasiz o'quvchi registrda "bo'sh chegirma" qatori bilan turmasin).</item>
    /// </list>
    /// Qaytaradi: yangi <c>active</c> qator (nolinchi spec'da null).
    /// </summary>
    public static async Task<StudentDiscount?> ApplyAsync(
        IAppDbContext db, Student student, DiscountSpec spec, string actor, string? actorId)
    {
        var norm = spec.Normalized();
        await CloseActiveAsync(db, student.Id, StudentDiscount.StatusReplaced, actor, "");

        if (norm.IsEmpty)
        {
            // Chegirma olib tashlandi — o'quvchi maydonlari ham tozalanadi (davr va guruh bilan
            // birga: aks holda "0%, lekin 2026-09 dan" degan ma'nosiz qoldiq qolardi).
            student.DiscountPct = 0;
            student.DiscountAmount = 0m;
            student.DiscountNote = "";
            student.DiscountStartMonth = "";
            student.DiscountEndMonth = "";
            student.DiscountGroupId = null;
            return null;
        }

        var row = await NewRowAsync(db, student, norm, actor, actorId);
        db.StudentDiscounts.Add(row);
        WriteToStudent(student, norm);
        return row;
    }

    /// <summary>
    /// AMALDAGI qatorni JOYIDA tahrirlaydi (yangi qator OCHILMAYDI) va
    /// <c>Student.Discount*</c> ni unga moslaydi.
    ///
    /// <para>Nega yangi qator emas: tahrirlash — xatoni to'g'rilash (foizni tuzatish, sababni
    /// aniqlashtirish), "yangi chegirma berildi" degani emas. Har tuzatish yangi qator ochsa
    /// tarix bir xil chegirmaning nusxalari bilan to'lib ketardi. HAQIQATAN yangi chegirma
    /// uchun <see cref="ApplyAsync"/> bor (u eskisini <c>replaced</c> qilib yopadi).</para>
    ///
    /// <para><c>false</c> qaytaradi, agar qator <c>active</c> BO'LMASA — tarixiy qator
    /// tahrirlanmaydi (chaqiruvchi 400 qaytaradi).</para>
    /// </summary>
    public static async Task<bool> UpdateAsync(
        IAppDbContext db, Student student, StudentDiscount row, DiscountSpec spec, string actor)
    {
        if (row.Status != StudentDiscount.StatusActive) return false;
        var norm = spec.Normalized();

        // Guruh o'zgargan bo'lsa SNAPSHOT ham yangilanadi (aks holda qatorda eski guruh/o'qituvchi
        // nomi qolib, hisobotda boshqa guruhga ishora qilardi).
        if (!string.Equals(row.GroupId ?? "", norm.GroupId ?? "", StringComparison.Ordinal))
        {
            var snap = await NewRowAsync(db, student, norm, actor, row.CreatedById);
            row.GroupId = snap.GroupId;
            row.GroupName = snap.GroupName;
            row.TeacherId = snap.TeacherId;
            row.TeacherName = snap.TeacherName;
        }

        row.Pct = norm.Pct;
        row.Amount = norm.Amount;
        row.StartMonth = norm.StartMonth;
        row.EndMonth = norm.EndMonth;
        row.Reason = norm.Reason;
        row.StudentName = student.FullName;
        WriteToStudent(student, norm);
        return true;
    }

    /// <summary>
    /// Chegirmani BEKOR qiladi: qator o'chmaydi — <c>cancelled</c> bo'ladi (sabab va kim/qachon
    /// bilan), o'quvchidan esa chegirma OLINADI.
    ///
    /// <para><c>false</c> qaytaradi, agar qator allaqachon yopilgan bo'lsa (tarixiy qatorni
    /// bekor qilib bo'lmaydi) — chaqiruvchi 400 qaytaradi.</para>
    /// </summary>
    public static async Task<bool> CancelAsync(
        IAppDbContext db, Student student, StudentDiscount row, string reason, string actor)
    {
        if (row.Status != StudentDiscount.StatusActive) return false;
        // Avval QOLGAN amaldagi qatorlar yopiladi (invariant: bitta o'quvchida bitta `active`).
        // Odatda bittasi bor — o'sha bekor qilinayotgani; eski/qo'lda tuzatilgan ma'lumotda
        // ortiqchasi qolsa, o'quvchidan chegirma olingani bilan registr "amalda" deb turardi.
        await CloseActiveAsync(db, student.Id, StudentDiscount.StatusReplaced, actor, "");
        row.Status = StudentDiscount.StatusCancelled;
        row.CancelReason = (reason ?? "").Trim();
        row.EndedAt = AppClock.Iso();
        row.EndedBy = actor;

        student.DiscountPct = 0;
        student.DiscountAmount = 0m;
        student.DiscountNote = "";
        student.DiscountStartMonth = "";
        student.DiscountEndMonth = "";
        student.DiscountGroupId = null;
        return true;
    }

    /// <summary>
    /// <c>Student.Discount*</c> BOSHQA yo'l bilan o'zgargan bo'lsa (eski o'quvchi formasi —
    /// <c>PUT /students/{id}</c>) registrni MOSLAYDI: joriy <c>active</c> qator maydonlari bilan
    /// solishtiradi va farq bo'lsa eskisini <c>replaced</c> qilib yangisini ochadi (chegirma
    /// nolga tushgan bo'lsa faqat yopadi).
    ///
    /// <para>⚠️ Bu metod <c>Student</c> maydonlariga TEGMAYDI — u yerda manba allaqachon
    /// yozilgan. Farq yo'q bo'lsa hech narsa qilmaydi (idempotent).</para>
    /// </summary>
    public static async Task<bool> SyncFromStudentAsync(
        IAppDbContext db, Student student, string actor, string? actorId)
    {
        var spec = DiscountSpec.From(student).Normalized();
        var current = await CurrentAsync(db, student.Id);

        if (current is not null && Matches(current, spec)) return false;   // registr allaqachon mos
        if (current is null && spec.IsEmpty) return false;                 // chegirma ham, qator ham yo'q

        await CloseActiveAsync(db, student.Id, StudentDiscount.StatusReplaced, actor, "");
        if (spec.IsEmpty) return true;

        db.StudentDiscounts.Add(await NewRowAsync(db, student, spec, actor, actorId));
        return true;
    }

    /// <summary>
    /// JORIY oy hisoblarida chegirmani QAYTA hisoblaydi va balansni to'g'rilaydi.
    ///
    /// <para>⚠️ Mantiq <c>StudentsController.Update</c> dagi blokdan AYNAN olingan (o'ylab
    /// topilmagan): <c>Locked</c> (qo'lda tahrirlangan) qatorlar TEGILMAYDI, <c>Amount</c>
    /// (narx/prorate) TEGILMAYDI — faqat <c>Discount</c> yangilanadi. Guruhga biriktirilgan
    /// chegirmada <c>DiscountForMonth</c> o'zi faqat mos guruhga beradi, qolganlarida 0 ga
    /// tushiradi; guruhsiz (<c>GroupId == null</c>) hisoblar ham shu so'rovga tushadi.</para>
    ///
    /// <para>Qaytaradi: birorta hisob o'zgardimi. <c>SaveChanges</c> — chaqiruvchida.</para>
    /// </summary>
    public static async Task<bool> ReapplyCurrentMonthAsync(IAppDbContext db, Student student)
    {
        var applied = false;
        var current = TuitionService.CurrentMonth();
        var monthCharges = await db.MonthlyCharges
            .Where(c => c.StudentId == student.Id && c.Month == current && !c.Locked)
            .ToListAsync();
        foreach (var charge in monthCharges)
        {
            var newDiscount = TuitionService.DiscountForMonth(student, charge.Amount, current, charge.GroupId);
            if (newDiscount == charge.Discount) continue;
            // Effektiv farq: (Amount − yangiD) − (Amount − eskiD) = eskiD − yangiD.
            student.Balance -= charge.Discount - newDiscount;
            charge.Discount = newDiscount;
            applied = true;
        }
        return applied;
    }

    /// <summary>
    /// Registr qatorini javob DTO'siga o'giradi. <paramref name="month"/> — "hozir" deb
    /// qaraladigan oy (odatda joriy oy): <c>InForce</c> aynan shu oy bo'yicha hisoblanadi.
    ///
    /// <para>⚠️ <c>InForce</c> KLIENTDA qayta hisoblanmaydi — server tayyor beradi (kamera
    /// modulidagi <c>ArchiveSource</c> bilan bir xil sabab: ikki joyda hisoblansak, biri
    /// o'zgarganda ikkinchisi jimgina eskirib qolardi).</para>
    /// </summary>
    public static StudentDiscountItemDto ToDto(StudentDiscount d, string month) => new(
        d.Id, d.StudentId, d.StudentName,
        d.GroupId, d.GroupName,
        d.TeacherId, d.TeacherName,
        d.Pct, d.Amount,
        d.StartMonth, d.EndMonth,
        d.Reason,
        d.Status, DiscountRules.StatusLabel(d.Status), DiscountRules.InForce(d, month),
        d.CreatedAt, d.CreatedBy,
        d.EndedAt, d.EndedBy, d.CancelReason);

    /// <summary>
    /// O'quvchi profilidagi «Chegirma» tabining TO'LIQ javobi: registr qatorlari + oylar
    /// bo'yicha HAQIQATAN qo'llangan chegirma.
    ///
    /// <para>⚠️ "Qo'llangan" qismi registrdan EMAS, <see cref="MonthlyCharge"/> dan olinadi —
    /// pul haqiqati o'sha yerda (registr yangi jadval, oylik hisoblar esa butun tarixni saqlaydi).</para>
    ///
    /// <para>Har amaldan keyin TO'LIQ javob qaytariladi — klient qayta so'rov yubormasin.</para>
    /// </summary>
    public static async Task<StudentDiscountsResponseDto> BuildAsync(IAppDbContext db, string studentId)
    {
        var month = TuitionService.CurrentMonth();

        var rows = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.StudentId == studentId)
            .ToListAsync();
        // Eng yangisi birinchi; amaldagi qator (EndedAt bo'sh) teng vaqtda ham tepada qolsin.
        var items = rows
            .OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal)
            .ThenByDescending(d => d.Status == StudentDiscount.StatusActive)
            .Select(d => ToDto(d, month))
            .ToList();

        // Faqat chegirma QO'LLANGAN hisoblar (Discount != 0) — qolgani lentani suyultirardi.
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => c.StudentId == studentId && c.Discount != 0m)
            .Select(c => new { c.Month, c.GroupId, c.Amount, c.Discount })
            .ToListAsync();

        var groupIds = charges.Where(c => c.GroupId != null).Select(c => c.GroupId!).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.TeacherId })
            .ToListAsync();
        var teacherIds = groups.Where(g => !string.IsNullOrEmpty(g.TeacherId)).Select(g => g.TeacherId).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName })
            .ToListAsync();
        var teacherById = teachers.ToDictionary(t => t.Id, t => t.FullName);
        var groupById = groups.ToDictionary(
            g => g.Id,
            g => (g.Name, Teacher: teacherById.TryGetValue(g.TeacherId, out var tn) ? tn : ""));

        var applied = charges
            .OrderByDescending(c => c.Month, StringComparer.Ordinal)
            .Select(c =>
            {
                var (gname, tname) = c.GroupId is not null && groupById.TryGetValue(c.GroupId, out var g)
                    ? (g.Name, g.Teacher)
                    : ("", "");
                return new StudentDiscountMonthDto(c.Month, c.GroupId, gname, tname, c.Amount, c.Discount);
            })
            .ToList();

        var current = items.FirstOrDefault(i => i.Status == StudentDiscount.StatusActive);
        return new StudentDiscountsResponseDto(
            items, applied,
            applied.Sum(a => a.Discount),
            current,
            applied.Where(a => a.Month == month).Sum(a => a.Discount));
    }

    // ==================== ichki yordamchilar ====================

    /// <summary>Mavjud <c>active</c> qator(lar)ni yopadi. Ko'plik ATAYIN: invariant buzilgan
    /// eski/qo'lda tuzatilgan ma'lumotda ham registr bitta amaldagi qatorga qaytadi.</summary>
    private static async Task CloseActiveAsync(
        IAppDbContext db, string studentId, string status, string actor, string cancelReason)
    {
        var rows = await db.StudentDiscounts
            .Where(d => d.StudentId == studentId && d.Status == StudentDiscount.StatusActive)
            .ToListAsync();
        var now = AppClock.Iso();
        foreach (var r in rows)
        {
            r.Status = status;
            r.EndedAt = now;
            r.EndedBy = actor;
            if (cancelReason.Length > 0) r.CancelReason = cancelReason;
        }
    }

    /// <summary>Yangi registr qatori — SNAPSHOT maydonlari (o'quvchi/guruh/o'qituvchi nomi) shu
    /// yerda bir marta to'ldiriladi.</summary>
    private static async Task<StudentDiscount> NewRowAsync(
        IAppDbContext db, Student student, DiscountSpec spec, string actor, string? actorId)
    {
        var groupName = "";
        string? teacherId = null;
        var teacherName = "";
        if (!string.IsNullOrEmpty(spec.GroupId))
        {
            var g = await db.Classes.AsNoTracking()
                .Where(c => c.Id == spec.GroupId)
                .Select(c => new { c.Name, c.TeacherId })
                .FirstOrDefaultAsync();
            if (g is not null)
            {
                groupName = g.Name;
                if (!string.IsNullOrWhiteSpace(g.TeacherId))
                {
                    teacherId = g.TeacherId;
                    teacherName = await db.Teachers.AsNoTracking()
                        .Where(t => t.Id == g.TeacherId)
                        .Select(t => t.FullName)
                        .FirstOrDefaultAsync() ?? "";
                }
            }
        }

        return new StudentDiscount
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            GroupId = spec.GroupId,
            GroupName = groupName,
            TeacherId = teacherId,
            TeacherName = teacherName,
            Pct = spec.Pct,
            Amount = spec.Amount,
            StartMonth = spec.StartMonth,
            EndMonth = spec.EndMonth,
            Reason = spec.Reason,
            Status = StudentDiscount.StatusActive,
            CreatedAt = AppClock.Iso(),
            CreatedBy = actor,
            CreatedById = actorId,
        };
    }

    /// <summary><c>Student.Discount*</c> ni spec'dan to'ldiradi (registr va o'quvchi bir xil
    /// bo'lishi SHART — invariant).</summary>
    private static void WriteToStudent(Student student, DiscountSpec spec)
    {
        student.DiscountPct = spec.Pct;
        student.DiscountAmount = spec.Amount;
        student.DiscountNote = spec.Reason;
        student.DiscountStartMonth = spec.StartMonth;
        student.DiscountEndMonth = spec.EndMonth;
        student.DiscountGroupId = spec.GroupId;
    }

    /// <summary>Registr qatori spec bilan AYNAN bir xilmi (sinxronlashda "o'zgardimi" savoli).</summary>
    private static bool Matches(StudentDiscount d, DiscountSpec spec) =>
        d.Pct == spec.Pct
        && d.Amount == spec.Amount
        && string.Equals(d.StartMonth ?? "", spec.StartMonth, StringComparison.Ordinal)
        && string.Equals(d.EndMonth ?? "", spec.EndMonth, StringComparison.Ordinal)
        && string.Equals(d.Reason ?? "", spec.Reason, StringComparison.Ordinal)
        && string.Equals(d.GroupId ?? "", spec.GroupId ?? "", StringComparison.Ordinal);
}
