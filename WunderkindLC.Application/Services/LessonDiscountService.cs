using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «CHEGIRMA» — kelmagan darslar uchun oylikdan ayirish (to'lov oynasida). WunderkindLMS dagi
/// «kasallik chegirmasi» naqshi, LC hisob modeliga moslab. Testlar: <c>LessonDiscountTests</c>.
///
/// <code>
/// bir dars narxi = kursning (yoki sinf oralig'ining) bir dars narxi; u yo'q bo'lsa — oylik ÷ oydagi darslar
/// ayriladi       = min(darslar × bir dars narxi, shu oyning TO'LANMAGAN qoldig'i)
/// </code>
///
/// <para>⚠️ To'liq to'langan oyga QO'LLANMAYDI — pul qaytarish alohida amal (vozvrat). Aks holda
/// chegirma jimgina "avans" bo'lib qolardi.</para>
///
/// <para>⚠️ Chegirma hisob qatorining <c>Discount</c> iga qo'shiladi va qator <c>Locked</c> bo'ladi:
/// narxni joriy oyga qo'llash, qayta aktivlashtirish, muzlatish va h.k. <c>Discount</c> ni QAYTA
/// hisoblaydi — qulfsiz qator chegirmani jimgina o'chirib yuborardi. Bu "qo'lda tahrirlangan hisob"
/// qoidasining o'zi (o'sha yo'llar <c>Locked</c> qatorga tegmaydi).</para>
///
/// <para>Maosh foizi bazasi (<c>MonthlyCharge.Amount</c>, chegirmasiz) O'ZGARMAYDI — chegirmani markaz
/// ko'taradi (`billing.md`, oddiy chegirma bilan bir xil qoida).</para>
///
/// <para>SaveChanges QILINMAYDI — chaqiruvchi (audit bilan birga) saqlaydi. Xato —
/// <see cref="InvalidOperationException"/> (o'zbekcha matn, controller 400 qiladi).</para>
/// </summary>
public static class LessonDiscountService
{
    public const int MaxLessons = 31;
    public const int MaxReasonLength = 500;

    public sealed record Quote(
        string GroupId, string Month, int Lessons, int LessonsInMonth, decimal LessonPrice, decimal Remaining, decimal Amount);

    // ============================ Sof qoidalar ============================

    /// <summary>Bir dars narxi: belgilangan bo'lsa o'sha, aks holda oylik ÷ oydagi darslar (so'mgacha yaxlit).</summary>
    public static decimal LessonPrice(decimal monthly, decimal lessonFee, int lessonsInMonth) =>
        lessonFee > 0 ? lessonFee
        : lessonsInMonth > 0 ? Math.Round(monthly / lessonsInMonth, 0, MidpointRounding.AwayFromZero)
        : 0m;

    /// <summary>Ayriladigan summa — to'lanmagan qoldiqdan oshmaydi, manfiy bo'lmaydi.</summary>
    public static decimal AmountFor(int lessons, decimal lessonPrice, decimal remaining) =>
        Math.Max(0m, Math.Min(lessons * lessonPrice, remaining));

    // ============================ Hisob ============================

    /// <summary>Nima ayrilishini HISOBLAYDI — hech narsa yozilmaydi.</summary>
    public static async Task<Quote> QuoteAsync(IAppDbContext db, Student s, Group g, string month, int lessons,
                                               CancellationToken ct = default)
    {
        var (start, end) = RequireMonth(month);
        if (lessons < 1 || lessons > MaxLessons)
            throw new InvalidOperationException($"Kelmagan darslar soni 1 dan {MaxLessons} gacha bo'lishi kerak");
        var inMonth = TuitionService.LessonsInRange(g.Days, start, end);
        if (inMonth <= 0)
            throw new InvalidOperationException($"«{g.Name}» guruhining {month} oyida darsi yo'q");
        if (lessons > inMonth)
            throw new InvalidOperationException($"{month} oyida {inMonth} ta dars bor — kelmagan darslar undan ko'p bo'lmaydi");

        // Narx — o'quvchining sinfiga qarab (`TuitionPricing`), qisman oy hisobi bilan bir xil.
        var (monthly, bandLesson) = (await PricingBook.LoadForCourseAsync(db, g.CourseId, ct)).Resolve(g, s, month);
        var courseLesson = string.IsNullOrEmpty(g.CourseId) ? 0m
            : await db.Subjects.AsNoTracking().Where(x => x.Id == g.CourseId).Select(x => x.LessonPrice).FirstOrDefaultAsync(ct);
        var lessonPrice = LessonPrice(monthly, bandLesson ?? courseLesson, inMonth);

        var charge = await db.MonthlyCharges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == g.Id && c.Month == month, ct);
        decimal effective;
        if (charge is not null)
            effective = Math.Max(0m, charge.Amount - charge.Discount);
        else
        {
            // Hisob hali yozilmagan oy — `EnsureChargeAsync` aynan shu summani yozadi.
            var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
            effective = Math.Max(0m, monthly - book.DiscountFor(s.Id, monthly, month, g.Id));
        }
        var remaining = Math.Max(0m, effective - await PaidAsync(db, s.Id, g.Id, month, ct));
        return new Quote(g.Id, month, lessons, inMonth, lessonPrice, remaining, AmountFor(lessons, lessonPrice, remaining));
    }

    /// <summary>Chegirmani QO'LLAYDI: hisob <c>Discount</c> i oshadi, qator qulflanadi, balans yengillashadi.</summary>
    public static async Task<LessonDiscount> ApplyAsync(
        IAppDbContext db, Student s, Group g, string month, int lessons, string? reason,
        string actorId, string actorName, CancellationToken ct = default)
    {
        var why = (reason ?? "").Trim();
        if (why.Length == 0) throw new InvalidOperationException("Sababini yozing (masalan: kasal edi)");
        if (why.Length > MaxReasonLength) throw new InvalidOperationException($"Sabab {MaxReasonLength} belgidan oshmasin");

        var q = await QuoteAsync(db, s, g, month, lessons, ct);
        if (q.Amount <= 0)
            throw new InvalidOperationException(
                $"{month} oyi uchun to'lanmagan summa yo'q — chegirma qo'llanmaydi (to'langan pulni qaytarish — «Vozvrat»)");

        // Hisob qatori kerak: yo'q bo'lsa (masalan kelgusi oy) avans to'lovdagi kabi yaratiladi.
        var charge = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == g.Id && c.Month == month, ct);
        if (charge is null)
        {
            await TuitionService.EnsureChargeAsync(db, s, g.Id, month);
            charge = db.MonthlyCharges.Local.FirstOrDefault(c => c.StudentId == s.Id && c.GroupId == g.Id && c.Month == month)
                     ?? throw new InvalidOperationException($"{month} oyi uchun hisob yaratib bo'lmadi");
        }

        var row = new LessonDiscount
        {
            StudentId = s.Id, GroupId = g.Id, Month = month, Lessons = lessons, LessonPrice = q.LessonPrice,
            Amount = q.Amount, Reason = why, WasLocked = charge.Locked,
            CreatedAt = AppClock.Iso(), CreatedById = actorId, CreatedByName = actorName,
        };
        charge.Discount += q.Amount;
        charge.Locked = true;
        s.Balance += q.Amount;
        db.LessonDiscounts.Add(row);
        return row;
    }

    /// <summary>Bekor qiladi: ayrilgan summa hisobga va qarzga QAYTADI, qator iz sifatida qoladi.</summary>
    public static async Task<LessonDiscount> CancelAsync(
        IAppDbContext db, Student s, string discountId, string actorName, CancellationToken ct = default)
    {
        var row = await db.LessonDiscounts.FirstOrDefaultAsync(x => x.Id == discountId && x.StudentId == s.Id, ct)
                  ?? throw new InvalidOperationException("Chegirma topilmadi");
        if (row.CancelledAt is not null) throw new InvalidOperationException("Bu chegirma allaqachon bekor qilingan");

        var charge = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == row.GroupId && c.Month == row.Month, ct)
            ?? throw new InvalidOperationException("Shu oyning hisobi o'chirilgan — chegirmani qaytarib bo'lmaydi");

        var back = Math.Min(row.Amount, charge.Discount);
        charge.Discount -= back;
        s.Balance -= back;

        // Qulf: shu oyda boshqa amaldagi chegirma bo'lsa — qoladi; aks holda BIRINCHI chegirmadan
        // oldingi holat (o'sha qatorning `WasLocked`i) tiklanadi.
        var sameMonth = await db.LessonDiscounts
            .Where(x => x.StudentId == s.Id && x.GroupId == row.GroupId && x.Month == row.Month && x.Id != row.Id)
            .ToListAsync(ct);
        charge.Locked = sameMonth.Any(x => x.CancelledAt is null)
                        || (sameMonth.Concat([row]).OrderBy(x => x.CreatedAt, StringComparer.Ordinal).First().WasLocked);

        row.CancelledAt = AppClock.Iso();
        row.CancelledByName = actorName;
        return row;
    }

    /// <summary>
    /// Shu oyga (guruh bo'yicha) AMALDAGI chegirmalar yig'indisi. Qo'lda oylik tahriri
    /// (`StudentsController.EditCharge`) chegirmani registrdan QAYTA hisoblaydi — bu summa unga
    /// QO'SHILMASA kelmagan darslar chegirmasi jimgina o'chib, o'quvchiga qarz yozilardi.
    /// </summary>
    public static async Task<decimal> ActiveAmountAsync(IAppDbContext db, string studentId, string? groupId, string month,
                                                        CancellationToken ct = default)
    {
        if (groupId is null) return 0m; // chegirma faqat guruh hisobiga qo'yiladi
        var amounts = await db.LessonDiscounts.AsNoTracking()
            .Where(x => x.StudentId == studentId && x.GroupId == groupId && x.Month == month && x.CancelledAt == null)
            .Select(x => x.Amount)
            .ToListAsync(ct);
        return amounts.Sum();
    }

    /// <summary>Shu oyga (guruh bo'yicha) to'langan pul: o'quv to'lovi − vozvrat (to'lov oynasi bilan bir xil).</summary>
    private static async Task<decimal> PaidAsync(IAppDbContext db, string studentId, string groupId, string month, CancellationToken ct)
    {
        var rows = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId == studentId && t.GroupId == groupId && t.Month == month
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund")))
            .Select(t => new { t.Direction, t.Amount })
            .ToListAsync(ct);
        return rows.Sum(t => t.Direction == "expense" ? -t.Amount : t.Amount);
    }

    private static (DateOnly Start, DateOnly End) RequireMonth(string? month)
    {
        if (month is { Length: 7 } && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var start))
            return (start, start.AddMonths(1).AddDays(-1));
        throw new InvalidOperationException("Oy noto'g'ri (kutilgan ko'rinish: yyyy-MM)");
    }
}
