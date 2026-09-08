using Microsoft.EntityFrameworkCore;
using IntellectCRM.Application.Abstractions;
using IntellectCRM.Application.Dtos;
using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// CHEGIRMA REGISTRI ustidagi SOF qoidalar (bazasiz, testlanadigan).
///
/// <para>⚠️ Bu yerda "qaysi chegirma qo'llanadi" savoliga javob beriladi va u <b>PUL
/// QOIDASI</b>: <see cref="Resolve"/> ni <c>TuitionService.DiscountForMonth</c> chaqiradi.
/// Ilgari chegirma <c>Student.Discount*</c> maydonlaridan olinardi va o'quvchida bir vaqtda
/// BITTA chegirma bo'lardi; endi registr manba va har FAN (guruh) uchun alohida qator bo'ladi.
/// Batafsil: <c>.claude/rules/discounts.md</c>.</para>
/// </summary>
public static class DiscountRules
{
    /// <summary>«Barcha guruhlar» qamrovining o'zbekcha yorlig'i (UI va hisobot matnlari).</summary>
    public const string AllGroupsLabel = "Barcha guruhlar";

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

    /// <summary>
    /// <b>PUL QOIDASI — bitta hisob qatoriga QAYSI chegirma qo'llanadi.</b>
    ///
    /// <list type="number">
    ///   <item><c>active</c> + shu oyda amalda + <c>GroupId == groupId</c> (AYNAN shu fan) — g'olib;</item>
    ///   <item>aks holda <c>active</c> + amalda + <c>GroupId == null</c> («barcha guruhlar»);</item>
    ///   <item>aks holda <c>null</c> (chegirma yo'q).</item>
    /// </list>
    ///
    /// <para>⚠️ <b>HECH QACHON QO'SHILMAYDI (summa emas).</b> Ikkita chegirma bir-birining ustiga
    /// qo'yilsa oylik kutilmaganda 0 ga tushib ketardi. <b>ENG ANIQ moslik g'olib</b> — bu
    /// loyihadagi <c>navigation.ts → activeNavTo</c> bilan AYNAN bir xil printsip.</para>
    ///
    /// <para>⚠️ Bir QAMROVDA bir nechta <c>active</c> qator bo'lib qolsa (buzuq/qo'lda tuzatilgan
    /// ma'lumot) — <b>eng YANGISI</b> olinadi (<c>CreatedAt</c> bo'yicha), jimgina birinchisi emas.
    /// Aks holda natija qatorlar tartibiga bog'liq bo'lib qolardi.</para>
    ///
    /// <para>⚠️ ORQAGA MOSLIK: eski ma'lumotda har o'quvchida bitta qator bor
    /// (<c>GroupId</c> = eski <c>Student.DiscountGroupId</c>), ya'ni bu qoida AYNAN eski natijani
    /// beradi — guruhga biriktirilgan chegirma faqat o'sha guruhga, biriktirilmagani hammasiga.</para>
    /// </summary>
    /// <param name="rows">O'quvchining registr qatorlari (holatiga qaramay — filtr shu yerda).</param>
    /// <param name="month">Hisob qatorining oyi ("yyyy-MM").</param>
    /// <param name="groupId">Hisob qatorining guruhi (<c>MonthlyCharge.GroupId</c>; guruhsiz hisobda null).</param>
    public static StudentDiscount? Resolve(IReadOnlyList<StudentDiscount>? rows, string month, string? groupId)
    {
        if (rows is null || rows.Count == 0) return null;

        StudentDiscount? exact = null;
        StudentDiscount? all = null;
        foreach (var d in rows)
        {
            if (!InForce(d, month)) continue;
            if (d.GroupId is null)
            {
                if (all is null || Newer(d, all)) all = d;
            }
            else if (groupId is not null && string.Equals(d.GroupId, groupId, StringComparison.Ordinal))
            {
                if (exact is null || Newer(d, exact)) exact = d;
            }
        }
        return exact ?? all;
    }

    /// <summary><c>a</c> <c>b</c> dan KEYIN yaratilganmi (bir qamrovda ortiqcha qator qolgan holat).</summary>
    private static bool Newer(StudentDiscount a, StudentDiscount b) =>
        string.CompareOrdinal(a.CreatedAt ?? "", b.CreatedAt ?? "") > 0;

    /// <summary>Qamrov (scope) nomi: FAN nomi, bo'lmasa guruh nomi, bo'lmasa «Barcha guruhlar».
    /// Foydalanuvchi chegirmani "guruh" emas, FAN deb o'ylaydi — shuning uchun fan birinchi.</summary>
    public static string ScopeLabel(StudentDiscount d) =>
        !string.IsNullOrWhiteSpace(d.CourseName) ? d.CourseName
        : !string.IsNullOrWhiteSpace(d.GroupName) ? d.GroupName
        : AllGroupsLabel;

    /// <summary>Chegirma miqdorining qisqa matni: "20%", "50 000 so'm" yoki "20% + 50 000 so'm".</summary>
    public static string ValueLabel(int pct, decimal amount) => (pct > 0, amount > 0m) switch
    {
        (true, true) => $"{pct}% + {AuditService.Money(amount)} so'm",
        (true, false) => $"{pct}%",
        (false, true) => $"{AuditService.Money(amount)} so'm",
        _ => "0",
    };

    /// <summary>
    /// Amaldagi chegirmalarning QISQA yorlig'i — «Matematika 20%, Ingliz tili 50 000 so'm».
    /// Hisobotdagi <c>activeLabel</c> AYNAN shu yerda quriladi (klientda qayta yig'ilmaydi:
    /// ikki joyda tuzilsa biri o'zgarganda ikkinchisi jimgina eskirardi).
    /// </summary>
    public static string ActiveLabel(IEnumerable<StudentDiscount> rows) =>
        string.Join(", ", rows
            .OrderBy(d => d.GroupId is null ? 0 : 1)
            .ThenBy(d => ScopeLabel(d), StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{ScopeLabel(d)} {ValueLabel(d.Pct, d.Amount)}"));
}

/// <summary>
/// Chegirma so'rovi — "qanday chegirma qo'yilsin".
/// </summary>
/// <param name="Pct">Foiz (0..100).</param>
/// <param name="Amount">Aniq summa (so'm), foizdan keyin ayriladi.</param>
/// <param name="StartMonth">"yyyy-MM" yoki "" (cheklovsiz).</param>
/// <param name="EndMonth">"yyyy-MM" yoki "" (cheklovsiz).</param>
/// <param name="Reason">Sabab/izoh.</param>
/// <param name="GroupId">null/"" — «BARCHA guruhlar» qamrovi; id — faqat o'sha FAN (guruh).</param>
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
}

/// <summary>
/// CHEGIRMA QATORLARINI TASHIYDIGAN IDISH — pul hisobi chegirmani AYNAN shundan oladi.
///
/// <para>⚠️ <b>NEGA KERAK:</b> <c>TuitionService.DiscountForMonth</c> endi <see cref="Student"/>
/// emas, REGISTR qatorlarini oladi. "Yuklashni unutish" xavfini KOMPILYATOR ushlashi uchun
/// eski imzo umuman QOLDIRILMAGAN — aks holda eski chaqiruv jimgina 0 chegirma berib,
/// o'quvchilarga ortiqcha qarz yozilardi.</para>
///
/// <para>⚠️ <see cref="Empty"/> ni FAQAT haqiqatan chegirmasiz yo'lda (test/preview) ishlating.
/// Ommaviy hisob yo'llarida (accrual, guruh narxini qayta qo'llash) kitob BIR MARTA yuklanadi —
/// N+1 qilmang.</para>
///
/// <para>Faqat <c>active</c> qatorlar yuklanadi (jadval kichik, davr filtri esa oy bo'yicha
/// <see cref="DiscountRules.Resolve"/> da qo'llanadi).</para>
/// </summary>
public sealed class DiscountBook
{
    private static readonly IReadOnlyList<StudentDiscount> None = Array.Empty<StudentDiscount>();
    private readonly Dictionary<string, List<StudentDiscount>> _byStudent;

    private DiscountBook(Dictionary<string, List<StudentDiscount>> byStudent) => _byStudent = byStudent;

    /// <summary>BO'SH kitob — chegirma yo'q. Testlar va chegirmasiz preview yo'llari uchun.</summary>
    public static DiscountBook Empty { get; } = new(new Dictionary<string, List<StudentDiscount>>());

    /// <summary>Bir nechta o'quvchi uchun (ommaviy amallar). Id'lar bo'laklarga bo'linadi —
    /// bitta so'rovga 500 dan ortiq parametr tushmasin.</summary>
    public static async Task<DiscountBook> LoadAsync(IAppDbContext db, IEnumerable<string> studentIds)
    {
        var ids = studentIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (ids.Count == 0) return Empty;

        var rows = new List<StudentDiscount>();
        const int chunk = 500;
        for (var i = 0; i < ids.Count; i += chunk)
        {
            var part = ids.GetRange(i, Math.Min(chunk, ids.Count - i));
            rows.AddRange(await db.StudentDiscounts.AsNoTracking()
                .Where(d => d.Status == StudentDiscount.StatusActive && part.Contains(d.StudentId))
                .ToListAsync());
        }
        return Build(db, rows, ids.ToHashSet());
    }

    /// <summary>Bitta o'quvchi uchun (profil, ledger, aktivlashtirish/muzlatish hisobi).</summary>
    public static async Task<DiscountBook> LoadForStudentAsync(IAppDbContext db, string studentId)
    {
        if (string.IsNullOrEmpty(studentId)) return Empty;
        var rows = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.Status == StudentDiscount.StatusActive && d.StudentId == studentId)
            .ToListAsync();
        return Build(db, rows, new HashSet<string> { studentId });
    }

    /// <summary>BARCHA o'quvchilar (oylik accrual — <c>TuitionService.AccrueMonth</c>).
    /// ⚠️ Accrual butun tarixni qayta skanerlaydi: kitob BO'SH bo'lsa chegirma 0 bo'lib,
    /// ommaviy YOLG'ON QARZ yozilardi (<c>.claude/rules/membership-periods.md</c> §6).</summary>
    public static async Task<DiscountBook> LoadAllAsync(IAppDbContext db)
    {
        var rows = await db.StudentDiscounts.AsNoTracking()
            .Where(d => d.Status == StudentDiscount.StatusActive)
            .ToListAsync();
        return Build(db, rows, null);
    }

    /// <summary>
    /// Bazadan o'qilgan qatorlarni EF'ning HALI SAQLANMAGAN (Local) qatorlari bilan birlashtiradi.
    ///
    /// <para>⚠️ Bu SHART: chegirma berilgandan keyin, <c>SaveChanges</c> dan OLDIN joriy oy hisobi
    /// qayta hisoblanadi (<c>ReapplyCurrentMonthAsync</c>) — yangi qator <c>AsNoTracking</c>
    /// so'rovida qaytmaydi, ya'ni "chegirma berdim, joriy oy o'zgarmadi" holati chiqardi.
    /// Local qatori DB nusxasidan USTUN (u eng yangi holat: masalan endigina <c>replaced</c>
    /// qilingan qator bu yerda chiqarib tashlanadi).</para>
    /// </summary>
    private static DiscountBook Build(IAppDbContext db, List<StudentDiscount> fromDb, HashSet<string>? onlyStudents)
    {
        var local = db.StudentDiscounts.Local
            .Where(d => onlyStudents is null || onlyStudents.Contains(d.StudentId))
            .ToList();

        var merged = new Dictionary<string, StudentDiscount>(StringComparer.Ordinal);
        foreach (var d in fromDb) merged[d.Id] = d;
        foreach (var d in local) merged[d.Id] = d;   // Local USTUN

        var byStudent = merged.Values
            .Where(d => d.Status == StudentDiscount.StatusActive)
            .GroupBy(d => d.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        return new DiscountBook(byStudent);
    }

    /// <summary>O'quvchining amaldagi chegirma qatorlari (yo'q bo'lsa bo'sh ro'yxat).</summary>
    public IReadOnlyList<StudentDiscount> For(string studentId) =>
        _byStudent.TryGetValue(studentId, out var rows) ? rows : None;

    /// <summary>Bitta hisob qatoriga qo'yiladigan chegirma summasi
    /// (<see cref="TuitionService.DiscountForMonth(IReadOnlyList{StudentDiscount}, decimal, string, string?)"/>).</summary>
    public decimal DiscountFor(string studentId, decimal fee, string month, string? groupId) =>
        TuitionService.DiscountForMonth(For(studentId), fee, month, groupId);
}

/// <summary>
/// CHEGIRMA REGISTRIGA YOZUVCHI YAGONA JOY.
///
/// <para>⚠️ <b>INVARIANT:</b> har <c>(StudentId, GroupId)</c> QAMROVIDA ko'pi bilan BITTA
/// <see cref="StudentDiscount.StatusActive"/> qator (<c>GroupId == null</c> — «barcha guruhlar»
/// alohida qamrov). O'quvchida esa nechta fan bo'lsa shuncha chegirma bo'lishi mumkin.</para>
///
/// <para>⚠️ <c>Student.Discount*</c> maydonlari endi PUL MANBASI EMAS — ular registrning ASOSIY
/// qatorini aks ettiradi va faqat shu servisda (<see cref="RefreshMirrorAsync"/>) yangilanadi.</para>
///
/// <para>Hech bir metod <c>SaveChanges</c> QILMAYDI — chaqiruvchining tranzaksiyasiga qo'shiladi
/// (<c>AuditService.Record</c> bilan bir xil qoida).</para>
/// </summary>
public static class StudentDiscountService
{
    /// <summary>Berilgan QAMROVDAGI (guruh yoki «barcha guruhlar») amaldagi qator — yo'q bo'lsa null.
    /// Yangi chegirma berishdan oldin 409 tekshiruvi AYNAN shu bilan qilinadi.</summary>
    public static async Task<StudentDiscount?> ActiveInScopeAsync(
        IAppDbContext db, string studentId, string? groupId)
    {
        var scope = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
        var rows = await ActiveRowsAsync(db, studentId);
        return rows
            .Where(d => string.Equals(d.GroupId ?? "", scope ?? "", StringComparison.Ordinal))
            .OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>O'quvchining HAMMA amaldagi qatorlari (har qamrovdan bittadan) — eng yangisi birinchi.</summary>
    public static async Task<List<StudentDiscount>> ActiveAsync(IAppDbContext db, string studentId) =>
        (await ActiveRowsAsync(db, studentId))
        .OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Yangi chegirma qo'yadi — FAQAT o'z QAMROVIDA (boshqa fanlarning chegirmasiga TEGMAYDI):
    /// <list type="number">
    ///   <item>shu qamrovdagi mavjud <c>active</c> qator <c>replaced</c> bo'lib yopiladi;</item>
    ///   <item>spec nolinchi BO'LMASA — yangi <c>active</c> qator ochiladi;</item>
    ///   <item>nolinchi bo'lsa — yangi qator ochilmaydi (chegirmasiz o'quvchi registrda
    ///         "bo'sh chegirma" qatori bilan turmasin).</item>
    /// </list>
    /// Oxirida <c>Student.Discount*</c> ko'zgusi yangilanadi. Qaytaradi: yangi qator (yoki null).
    /// </summary>
    public static async Task<StudentDiscount?> ApplyAsync(
        IAppDbContext db, Student student, DiscountSpec spec, string actor, string? actorId)
    {
        var norm = spec.Normalized();
        await CloseScopeAsync(db, student.Id, norm.GroupId, StudentDiscount.StatusReplaced, actor);

        StudentDiscount? row = null;
        if (!norm.IsEmpty)
        {
            row = await NewRowAsync(db, student, norm, actor, actorId);
            db.StudentDiscounts.Add(row);
        }
        await RefreshMirrorAsync(db, student);
        return row;
    }

    /// <summary>
    /// AMALDAGI qatorni JOYIDA tahrirlaydi (yangi qator OCHILMAYDI).
    ///
    /// <para>Nega yangi qator emas: tahrirlash — xatoni to'g'rilash (foizni tuzatish, sababni
    /// aniqlashtirish), "yangi chegirma berildi" degani emas. Har tuzatish yangi qator ochsa
    /// tarix bir xil chegirmaning nusxalari bilan to'lib ketardi.</para>
    ///
    /// <para>⚠️ QAMROV o'zgartirilsa (boshqa fanga ko'chirilsa) — MAQSAD qamrovda allaqachon
    /// amaldagi chegirma bo'lmasligi kerak; tekshiruv chaqiruvchida (409).</para>
    ///
    /// <para><c>false</c> qaytaradi, agar qator <c>active</c> BO'LMASA — tarixiy qator
    /// tahrirlanmaydi (chaqiruvchi 400 qaytaradi).</para>
    /// </summary>
    public static async Task<bool> UpdateAsync(
        IAppDbContext db, Student student, StudentDiscount row, DiscountSpec spec, string actor)
    {
        if (row.Status != StudentDiscount.StatusActive) return false;
        var norm = spec.Normalized();

        // Qamrov o'zgargan bo'lsa SNAPSHOT ham yangilanadi (aks holda qatorda eski guruh/fan/
        // o'qituvchi nomi qolib, hisobotda boshqa fanga ishora qilardi).
        if (!string.Equals(row.GroupId ?? "", norm.GroupId ?? "", StringComparison.Ordinal))
        {
            var snap = await NewRowAsync(db, student, norm, actor, row.CreatedById);
            row.GroupId = snap.GroupId;
            row.GroupName = snap.GroupName;
            row.CourseId = snap.CourseId;
            row.CourseName = snap.CourseName;
            row.TeacherId = snap.TeacherId;
            row.TeacherName = snap.TeacherName;
        }

        row.Pct = norm.Pct;
        row.Amount = norm.Amount;
        row.StartMonth = norm.StartMonth;
        row.EndMonth = norm.EndMonth;
        row.Reason = norm.Reason;
        row.StudentName = student.FullName;
        await RefreshMirrorAsync(db, student);
        return true;
    }

    /// <summary>
    /// Chegirmani BEKOR qiladi: qator o'chmaydi — <c>cancelled</c> bo'ladi (sabab va kim/qachon
    /// bilan). ⚠️ FAQAT SHU qator yopiladi — o'quvchining BOSHQA fanlaridagi chegirmalari
    /// joyida qoladi.
    ///
    /// <para><c>false</c> qaytaradi, agar qator allaqachon yopilgan bo'lsa (tarixiy qatorni
    /// bekor qilib bo'lmaydi) — chaqiruvchi 400 qaytaradi.</para>
    /// </summary>
    public static async Task<bool> CancelAsync(
        IAppDbContext db, Student student, StudentDiscount row, string reason, string actor)
    {
        if (row.Status != StudentDiscount.StatusActive) return false;
        row.Status = StudentDiscount.StatusCancelled;
        row.CancelReason = (reason ?? "").Trim();
        row.EndedAt = AppClock.Iso();
        row.EndedBy = actor;
        await RefreshMirrorAsync(db, student);
        return true;
    }

    /// <summary>
    /// <c>Student.Discount*</c> KO'ZGUSINI registrdan qayta quradi — YAGONA joy.
    ///
    /// <para>ASOSIY qator: <c>GroupId == null</c> («barcha guruhlar») bo'lgan <c>active</c> qator,
    /// bo'lmasa — eng YANGI <c>active</c> qator; qator umuman bo'lmasa hammasi 0/"".</para>
    ///
    /// <para>⚠️ Eski ma'lumotda har o'quvchida BITTA qator bor, ya'ni bu qoida mavjud qiymatlarni
    /// AYNAN qaytaradi — migratsiyada hech narsa o'zgarmaydi.</para>
    ///
    /// <para>⚠️ Ko'zgu — faqat KO'RSATISH uchun (mobil ilovalar va eski API mijozlari). Pul uni
    /// o'qimaydi; chegirma summasi <see cref="DiscountBook"/> dan olinadi.</para>
    /// </summary>
    public static async Task RefreshMirrorAsync(IAppDbContext db, Student student)
    {
        var rows = await ActiveRowsAsync(db, student.Id);
        var primary = rows.FirstOrDefault(d => d.GroupId is null)
                      ?? rows.OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal).FirstOrDefault();

        student.DiscountPct = primary?.Pct ?? 0;
        student.DiscountAmount = primary?.Amount ?? 0m;
        student.DiscountNote = primary?.Reason ?? "";
        student.DiscountStartMonth = primary?.StartMonth ?? "";
        student.DiscountEndMonth = primary?.EndMonth ?? "";
        student.DiscountGroupId = primary?.GroupId;
    }

    /// <summary>
    /// JORIY oy hisoblarida chegirmani QAYTA hisoblaydi va balansni to'g'rilaydi.
    ///
    /// <para>⚠️ Mantiq <c>StudentsController.Update</c> dagi eski blokdan olingan: <c>Locked</c>
    /// (qo'lda tahrirlangan) qatorlar TEGILMAYDI, <c>Amount</c> (narx/prorate) TEGILMAYDI — faqat
    /// <c>Discount</c> yangilanadi. Chegirma HAR HISOB QATORI uchun alohida hal qilinadi
    /// (<c>DiscountRules.Resolve</c>): matematika guruhining chegirmasi ingliz tili hisobiga
    /// tushmaydi.</para>
    ///
    /// <para>⚠️ Kitob EF'ning hali saqlanmagan qatorlarini ham ko'radi (<see cref="DiscountBook"/>) —
    /// aks holda endigina berilgan chegirma joriy oyga qo'llanmasdi.</para>
    ///
    /// <para>Qaytaradi: birorta hisob o'zgardimi. <c>SaveChanges</c> — chaqiruvchida.</para>
    /// </summary>
    public static async Task<bool> ReapplyCurrentMonthAsync(IAppDbContext db, Student student)
    {
        var applied = false;
        var current = TuitionService.CurrentMonth();
        var book = await DiscountBook.LoadForStudentAsync(db, student.Id);
        var monthCharges = await db.MonthlyCharges
            .Where(c => c.StudentId == student.Id && c.Month == current && !c.Locked)
            .ToListAsync();
        foreach (var charge in monthCharges)
        {
            var newDiscount = book.DiscountFor(student.Id, charge.Amount, current, charge.GroupId);
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
        d.CourseName,
        d.Pct, d.Amount,
        d.StartMonth, d.EndMonth,
        d.Reason,
        d.Status, DiscountRules.StatusLabel(d.Status), DiscountRules.InForce(d, month),
        d.CreatedAt, d.CreatedBy,
        d.EndedAt, d.EndedBy, d.CancelReason);

    /// <summary>
    /// O'quvchi profilidagi «Chegirma» tabining TO'LIQ javobi: registr qatorlari, HOZIR amaldagi
    /// chegirmalar (har fanga bittadan), chegirma BERISH mumkin bo'lgan QAMROVLAR va oylar
    /// bo'yicha HAQIQATAN qo'llangan chegirma.
    ///
    /// <para>⚠️ "Qo'llangan" qismi registrdan EMAS, <see cref="MonthlyCharge"/> dan olinadi —
    /// pul o'sha yerda YOZILGAN (registr esa "hozir nima amalda" ni aytadi).</para>
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
        var ordered = rows
            .OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal)
            .ThenByDescending(d => d.Status == StudentDiscount.StatusActive)
            .ToList();
        var items = ordered.Select(d => ToDto(d, month)).ToList();
        var activeRows = ordered.Where(d => d.Status == StudentDiscount.StatusActive).ToList();

        // ---------- QAMROVLAR: o'quvchining FAOL a'zoliklari + «Barcha guruhlar» ----------
        // Modal shu ro'yxatdan quriladi: qaysi fanga chegirma berish mumkin va qaysisida
        // allaqachon bor (`hasActive` — server 409 qaytaradigan holat).
        var memberships = await (from sg in db.StudentGroups.AsNoTracking()
                                 join g in db.Classes.AsNoTracking() on sg.GroupId equals g.Id
                                 where sg.StudentId == studentId && sg.IsActive
                                 select new { g.Id, g.Name, g.CourseId, g.TeacherId, g.MonthlyFee })
            .ToListAsync();
        var scopeCourseIds = memberships.Select(m => m.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var scopeTeacherIds = memberships.Select(m => m.TeacherId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var scopeCourses = (await db.Subjects.AsNoTracking()
                .Where(s => scopeCourseIds.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.Name);
        var scopeTeachers = (await db.Teachers.AsNoTracking()
                .Where(t => scopeTeacherIds.Contains(t.Id)).Select(t => new { t.Id, t.FullName }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.FullName);
        var activeScopes = activeRows.Select(d => d.GroupId ?? "").ToHashSet(StringComparer.Ordinal);

        var scopes = new List<DiscountScopeOptionDto>
        {
            // «Barcha guruhlar» — RO'YXAT BOSHIDA: eng keng qamrov, standart tanlov.
            new(null, DiscountRules.AllGroupsLabel, "", "", 0m, activeScopes.Contains("")),
        };
        scopes.AddRange(memberships
            .DistinctBy(m => m.Id)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new DiscountScopeOptionDto(
                m.Id, m.Name,
                string.IsNullOrEmpty(m.CourseId) ? "" : scopeCourses.GetValueOrDefault(m.CourseId, ""),
                string.IsNullOrEmpty(m.TeacherId) ? "" : scopeTeachers.GetValueOrDefault(m.TeacherId, ""),
                m.MonthlyFee,
                activeScopes.Contains(m.Id))));

        // ---------- QO'LLANGAN: pul haqiqati (MonthlyCharge) ----------
        // Faqat chegirma QO'LLANGAN hisoblar (Discount != 0) — qolgani lentani suyultirardi.
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => c.StudentId == studentId && c.Discount != 0m)
            .Select(c => new { c.Month, c.GroupId, c.Amount, c.Discount })
            .ToListAsync();

        var groupIds = charges.Where(c => c.GroupId != null).Select(c => c.GroupId!).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.TeacherId, g.CourseId })
            .ToListAsync();
        var teacherIds = groups.Where(g => !string.IsNullOrEmpty(g.TeacherId)).Select(g => g.TeacherId).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName })
            .ToListAsync();
        var teacherById = teachers.ToDictionary(t => t.Id, t => t.FullName);
        // FAN nomi — ⚠️ ARXIVLANGAN guruh ham kerak (o'quvchi chiqib ketgan guruhning eski oylari
        // aynan shu jadvalda ko'rinadi), shuning uchun filtr faqat id bo'yicha.
        var courseIds = groups.Where(g => !string.IsNullOrEmpty(g.CourseId)).Select(g => g.CourseId).Distinct().ToList();
        var courseById = (await db.Subjects.AsNoTracking()
                .Where(x => courseIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync())
            .ToDictionary(x => x.Id, x => x.Name);
        var groupById = groups.ToDictionary(
            g => g.Id,
            g => (g.Name,
                  Course: courseById.TryGetValue(g.CourseId ?? "", out var cn) ? cn : "",
                  Teacher: teacherById.TryGetValue(g.TeacherId, out var tn) ? tn : ""));

        var applied = charges
            .OrderByDescending(c => c.Month, StringComparer.Ordinal)
            .Select(c =>
            {
                var (gname, cname, tname) = c.GroupId is not null && groupById.TryGetValue(c.GroupId, out var g)
                    ? (g.Name, g.Course, g.Teacher)
                    : ("", "", "");
                return new StudentDiscountMonthDto(c.Month, c.GroupId, gname, cname, tname, c.Amount, c.Discount);
            })
            .ToList();

        return new StudentDiscountsResponseDto(
            items,
            activeRows.Select(d => ToDto(d, month)).ToList(),
            scopes,
            applied,
            applied.Sum(a => a.Discount),
            // JORIY oyning chegirmasi — BARCHA fanlar bo'yicha JAMI.
            applied.Where(a => a.Month == month).Sum(a => a.Discount));
    }

    // ==================== ichki yordamchilar ====================

    /// <summary>
    /// O'quvchining HOZIR amaldagi qatorlari — bazadagi va EF'ning HALI SAQLANMAGAN (Local)
    /// qatorlari BIRLASHTIRILGAN holda.
    ///
    /// <para>⚠️ Filtr XOTIRADA qo'llanadi: SQL <c>Status</c> ning BAZADAGI qiymatiga qaraydi,
    /// biz esa shu tranzaksiyada <c>replaced</c> qilib qo'ygan qatorni chiqarib tashlashimiz
    /// kerak. Aks holda ko'zgu (mirror) endigina yopilgan chegirmani "asosiy" deb olardi.</para>
    /// </summary>
    private static async Task<List<StudentDiscount>> ActiveRowsAsync(IAppDbContext db, string studentId)
    {
        var fromDb = await db.StudentDiscounts
            .Where(d => d.StudentId == studentId)
            .ToListAsync();
        var merged = new Dictionary<string, StudentDiscount>(StringComparer.Ordinal);
        foreach (var d in fromDb) merged[d.Id] = d;
        foreach (var d in db.StudentDiscounts.Local.Where(d => d.StudentId == studentId)) merged[d.Id] = d;
        return merged.Values.Where(d => d.Status == StudentDiscount.StatusActive).ToList();
    }

    /// <summary>BERILGAN QAMROVDAGI <c>active</c> qator(lar)ni yopadi. Boshqa fanlarnikiga
    /// TEGMAYDI. Ko'plik ATAYIN: invariant buzilgan eski/qo'lda tuzatilgan ma'lumotda ham
    /// qamrov bitta amaldagi qatorga qaytadi.</summary>
    private static async Task CloseScopeAsync(
        IAppDbContext db, string studentId, string? groupId, string status, string actor)
    {
        var rows = (await ActiveRowsAsync(db, studentId))
            .Where(d => string.Equals(d.GroupId ?? "", groupId ?? "", StringComparison.Ordinal))
            .ToList();
        var now = AppClock.Iso();
        foreach (var r in rows)
        {
            r.Status = status;
            r.EndedAt = now;
            r.EndedBy = actor;
        }
    }

    /// <summary>Yangi registr qatori — SNAPSHOT maydonlari (o'quvchi/guruh/FAN/o'qituvchi nomi) shu
    /// yerda bir marta to'ldiriladi.</summary>
    private static async Task<StudentDiscount> NewRowAsync(
        IAppDbContext db, Student student, DiscountSpec spec, string actor, string? actorId)
    {
        var groupName = "";
        string? courseId = null;
        var courseName = "";
        string? teacherId = null;
        var teacherName = "";
        if (!string.IsNullOrEmpty(spec.GroupId))
        {
            var g = await db.Classes.AsNoTracking()
                .Where(c => c.Id == spec.GroupId)
                .Select(c => new { c.Name, c.TeacherId, c.CourseId })
                .FirstOrDefaultAsync();
            if (g is not null)
            {
                groupName = g.Name;
                if (!string.IsNullOrWhiteSpace(g.CourseId))
                {
                    courseId = g.CourseId;
                    courseName = await db.Subjects.AsNoTracking()
                        .Where(s => s.Id == g.CourseId)
                        .Select(s => s.Name)
                        .FirstOrDefaultAsync() ?? "";
                }
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
            CourseId = courseId,
            CourseName = courseName,
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
}
