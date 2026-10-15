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
///
/// <para>⚠️ 2026-10-15 dan to'lov oynasi KELGAN darslar sonini so'raydi (<see cref="QuoteAttendedAsync"/> /
/// <see cref="ApplyAttendedAsync"/>): o'quvchi faqat kelgan darslari uchun to'laydi, ayirma chegirma bo'ladi.
/// Kelmagan darslar yo'li (<see cref="QuoteAsync"/> / <see cref="ApplyAsync"/>) — hisob mexanizmi (qulf, bekor
/// qilish) va eski yozuvlar uchun saqlangan; HTTP endi uni chaqirmaydi (`discounts.md` §13.1).</para>
/// </summary>
public static class LessonDiscountService
{
    public const int MaxLessons = 31;
    public const int MaxReasonLength = 500;

    public sealed record Quote(
        string GroupId, string Month, int Lessons, int LessonsInMonth, decimal LessonPrice, decimal Remaining, decimal Amount);

    /// <summary>KELGAN darslar bo'yicha hisob. <paramref name="AttendedPrice"/> — o'quvchining o'z chegirmasi bilan
    /// bir dars narxi; <paramref name="MonthAmount"/> — shu oy hisobi (chegirmalar ayrilgan);
    /// <paramref name="Payable"/> = kelgan darslar × <paramref name="AttendedPrice"/>; <paramref name="Amount"/> — ayriladi.</summary>
    public sealed record AttendedQuote(
        string GroupId, string Month, int Attended, int LessonsInMonth, decimal LessonPrice, decimal AttendedPrice,
        decimal MonthAmount, decimal Payable, decimal Remaining, decimal Amount);

    /// <summary>Oyning hisob ma'lumoti: oydagi darslar, bir dars narxi, oy hisobi (chegirmalardan keyin),
    /// o'quvchining o'z chegirmasi ulushi (<c>Share</c> = to'lanadigan ÷ to'liq, 0..1) va to'lanmagan qoldiq.</summary>
    private sealed record MonthState(int InMonth, decimal LessonPrice, decimal Effective, decimal Share, decimal Remaining);

    // ============================ Sof qoidalar ============================

    /// <summary>Bir dars narxi: belgilangan bo'lsa o'sha, aks holda oylik ÷ oydagi darslar (so'mgacha yaxlit).</summary>
    public static decimal LessonPrice(decimal monthly, decimal lessonFee, int lessonsInMonth) =>
        lessonFee > 0 ? lessonFee
        : lessonsInMonth > 0 ? Math.Round(monthly / lessonsInMonth, 0, MidpointRounding.AwayFromZero)
        : 0m;

    /// <summary>Ayriladigan summa — to'lanmagan qoldiqdan oshmaydi, manfiy bo'lmaydi.</summary>
    public static decimal AmountFor(int lessons, decimal lessonPrice, decimal remaining) =>
        Math.Max(0m, Math.Min(lessons * lessonPrice, remaining));

    /// <summary>KELGAN darslar uchun bir dars narxi — o'quvchining o'z chegirmasi ulushi bilan
    /// (<paramref name="share"/> = to'lanadigan ÷ to'liq oylik): 50% chegirmali o'quvchi kelgan darsi uchun ham
    /// yarim narx to'laydi. So'mgacha yaxlit.</summary>
    public static decimal AttendedPrice(decimal lessonPrice, decimal share) =>
        Math.Round(lessonPrice * Math.Clamp(share, 0m, 1m), 0, MidpointRounding.AwayFromZero);

    /// <summary>KELGAN darslar bo'yicha ayriladigan summa: oy hisobidan kelgan darslar puli
    /// (<paramref name="attended"/> × <paramref name="attendedPrice"/>) chiqarilgani; manfiy bo'lmaydi va
    /// to'lanmagan qoldiqdan oshmaydi (to'langan pulni qaytarish — «Vozvrat»).</summary>
    public static decimal AttendedAmountFor(int attended, decimal attendedPrice, decimal monthAmount, decimal remaining) =>
        Math.Max(0m, Math.Min(monthAmount - attended * attendedPrice, remaining));

    // ============================ Hisob ============================

    /// <summary>Nima ayrilishini HISOBLAYDI — hech narsa yozilmaydi.</summary>
    public static async Task<Quote> QuoteAsync(IAppDbContext db, Student s, Group g, string month, int lessons,
                                               CancellationToken ct = default)
    {
        RequireMonth(month);
        if (lessons < 1 || lessons > MaxLessons)
            throw new InvalidOperationException($"Kelmagan darslar soni 1 dan {MaxLessons} gacha bo'lishi kerak");
        var m = await MonthStateAsync(db, s, g, month, ct);
        if (lessons > m.InMonth)
            throw new InvalidOperationException($"{month} oyida {m.InMonth} ta dars bor — kelmagan darslar undan ko'p bo'lmaydi");
        return new Quote(g.Id, month, lessons, m.InMonth, m.LessonPrice, m.Remaining, AmountFor(lessons, m.LessonPrice, m.Remaining));
    }

    /// <summary>KELGAN darslar bo'yicha nima ayrilishini HISOBLAYDI — hech narsa yozilmaydi.
    /// <paramref name="attended"/> — 0 dan oydagi darslar sonigacha.</summary>
    public static async Task<AttendedQuote> QuoteAttendedAsync(IAppDbContext db, Student s, Group g, string month, int attended,
                                                               CancellationToken ct = default)
    {
        RequireMonth(month);
        if (attended < 0 || attended > MaxLessons)
            throw new InvalidOperationException($"Kelgan darslar soni 0 dan {MaxLessons} gacha bo'lishi kerak");
        var m = await MonthStateAsync(db, s, g, month, ct);
        if (attended > m.InMonth)
            throw new InvalidOperationException($"{month} oyida {m.InMonth} ta dars bor — kelgan darslar undan ko'p bo'lmaydi");
        var price = AttendedPrice(m.LessonPrice, m.Share);
        return new AttendedQuote(g.Id, month, attended, m.InMonth, m.LessonPrice, price, m.Effective,
            attended * price, m.Remaining, AttendedAmountFor(attended, price, m.Effective, m.Remaining));
    }

    /// <summary>Oy hisobining holati — <see cref="QuoteAsync"/> va <see cref="QuoteAttendedAsync"/> uchun BITTA manba.</summary>
    private static async Task<MonthState> MonthStateAsync(IAppDbContext db, Student s, Group g, string month, CancellationToken ct)
    {
        var (start, end) = RequireMonth(month);
        var inMonth = TuitionService.LessonsInRange(g.Days, start, end);
        if (inMonth <= 0)
            throw new InvalidOperationException($"«{g.Name}» guruhining {month} oyida darsi yo'q");

        // Narx — o'quvchining sinfiga qarab (`TuitionPricing`), qisman oy hisobi bilan bir xil.
        var (monthly, bandLesson) = (await PricingBook.LoadForCourseAsync(db, g.CourseId, ct)).Resolve(g, s, month);
        var courseLesson = string.IsNullOrEmpty(g.CourseId) ? 0m
            : await db.Subjects.AsNoTracking().Where(x => x.Id == g.CourseId).Select(x => x.LessonPrice).FirstOrDefaultAsync(ct);
        var lessonPrice = LessonPrice(monthly, bandLesson ?? courseLesson, inMonth);

        var charge = await db.MonthlyCharges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == g.Id && c.Month == month, ct);
        decimal effective, share;
        if (charge is not null)
        {
            effective = Math.Max(0m, charge.Amount - charge.Discount);
            share = charge.Amount > 0 ? effective / charge.Amount : 1m;
        }
        else
        {
            // Hisob hali yozilmagan oy — `EnsureChargeAsync` aynan shu summani yozadi. ⚠️ AKTIVLASHTIRISH oyida u
            // QISMAN hisob ochadi (audit R1-5) — to'liq oylikdan hisoblansa chegirma qisman hisobdan oshib ketardi.
            var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
            var registry = book.DiscountFor(s.Id, monthly, month, g.Id);
            effective = await TuitionService.ActivationChargePreviewAsync(db, s, g, month)
                ?? Math.Max(0m, monthly - registry);
            share = monthly > 0 ? Math.Max(0m, monthly - registry) / monthly : 1m;
        }
        var remaining = Math.Max(0m, effective - await PaidAsync(db, s.Id, g.Id, month, ct));
        return new MonthState(inMonth, lessonPrice, effective, share, remaining);
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
        return await ApplyCoreAsync(db, s, g, month, lessons, null, q.LessonPrice, q.Amount, why, actorId, actorName, ct);
    }

    /// <summary>
    /// KELGAN darslar bo'yicha chegirmani QO'LLAYDI (to'lov oynasi, 2026-10-15). Hisob mexanizmi
    /// <see cref="ApplyAsync"/> bilan AYNAN bir xil (qulf, balans, bekor qilish).
    /// <para>⚠️ Shu oyda AMALDAGI chegirma bo'lsa — rad etiladi: "kelgan darslar soni" butun oy haqidagi gap, ikkinchi
    /// kiritma birinchisining ustiga qo'shilsa ikki marta ayrilardi. O'zgartirish — avval bekor qilish.</para>
    /// </summary>
    public static async Task<LessonDiscount> ApplyAttendedAsync(
        IAppDbContext db, Student s, Group g, string month, int attended, string? reason,
        string actorId, string actorName, CancellationToken ct = default)
    {
        var why = (reason ?? "").Trim();
        if (why.Length == 0) throw new InvalidOperationException("Sababini yozing (masalan: kasal edi)");
        if (why.Length > MaxReasonLength) throw new InvalidOperationException($"Sabab {MaxReasonLength} belgidan oshmasin");

        var standing = await db.LessonDiscounts.AsNoTracking()
            .CountAsync(x => x.StudentId == s.Id && x.GroupId == g.Id && x.Month == month && x.CancelledAt == null, ct);
        if (standing > 0)
            throw new InvalidOperationException(
                $"{month} oyiga chegirma allaqachon qo'llangan — o'zgartirish uchun avval uni bekor qiling");

        var q = await QuoteAttendedAsync(db, s, g, month, attended, ct);
        if (q.Amount <= 0)
            throw new InvalidOperationException(q.Remaining <= 0
                ? $"{month} oyi uchun to'lanmagan summa yo'q — chegirma qo'llanmaydi (to'langan pulni qaytarish — «Vozvrat»)"
                : $"Kelgan darslar puli ({AuditService.Money(q.Payable)} so'm) oy hisobidan kam emas — chegirma kerak emas");
        return await ApplyCoreAsync(db, s, g, month, 0, attended, q.AttendedPrice, q.Amount, why, actorId, actorName, ct);
    }

    /// <summary>Hisob qatoriga yozish — ikkala yo'l uchun BITTA joy.</summary>
    private static async Task<LessonDiscount> ApplyCoreAsync(
        IAppDbContext db, Student s, Group g, string month, int lessons, int? attended, decimal lessonPrice, decimal amount,
        string why, string actorId, string actorName, CancellationToken ct)
    {
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
            StudentId = s.Id, GroupId = g.Id, Month = month, Lessons = lessons, AttendedLessons = attended,
            LessonPrice = lessonPrice, Amount = amount, Reason = why, WasLocked = charge.Locked,
            CreatedAt = AppClock.Iso(), CreatedById = actorId, CreatedByName = actorName,
        };
        charge.Discount += amount;
        charge.Locked = true;
        s.Balance += amount;
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

    /// <summary>
    /// HISOB QATORI NIMA UCHUN QULFLI — sof qoida. <paramref name="sameMonth"/> — shu (o'quvchi, guruh, oy)
    /// ning BARCHA kelmagan darslar chegirmalari (bekor qilinganlari ham).
    /// <list type="bullet">
    ///   <item>amaldagi chegirma YO'Q yoki BIRINCHI chegirmadan oldin ham qulfli edi (<c>WasLocked</c>) —
    ///     qulf QO'LDA TAHRIRDAN: <c>null</c> (qatorga tegilmaydi);</item>
    ///   <item>aks holda qulf FAQAT shu chegirmalar tufayli: amaldagilar yig'indisi qaytadi — muzlatish/
    ///     chiqish/aktivlashtirish qatorni QAYTA hisoblab, shu summani chegirmaga QO'SHADI
    ///     (`StudentsController.EditCharge` dagi naqsh).</item>
    /// </list>
    /// <para>⚠️ NEGA KERAK: ilgari har qanday qulfli qator muzlatish/chiqish/aktivlashtirishda BUTUNLAY
    /// o'tkazib yuborilardi — chegirma qo'yilgan oyda guruh almashtirilsa eski guruh TO'LIQ oylikni,
    /// yangi guruh esa oyning qolgan qismini yozardi (IKKI MARTA hisob).</para>
    /// </summary>
    public static decimal? LockExtra(IReadOnlyCollection<LessonDiscount> sameMonth)
    {
        var active = sameMonth.Where(x => x.CancelledAt is null).ToList();
        if (active.Count == 0) return null;
        var first = sameMonth.OrderBy(x => x.CreatedAt, StringComparer.Ordinal).First();
        if (first.WasLocked) return null;
        return active.Sum(x => x.Amount);
    }

    /// <summary>
    /// Qatorni hisob-kitob QAYTA hisoblay oladimi: qulfsiz — <c>0</c>; qulf faqat kelmagan darslar
    /// chegirmasidan — amaldagi chegirmalar yig'indisi (qayta hisobda chegirmaga QO'SHILADI); qo'lda
    /// tahrir qulfi — <c>null</c> (tegilmaydi). Qoida — <see cref="LockExtra"/>.
    /// </summary>
    public static async Task<decimal?> ReworkableExtraAsync(IAppDbContext db, MonthlyCharge row, CancellationToken ct = default)
    {
        if (!row.Locked) return 0m;
        if (row.GroupId is null) return null; // chegirma faqat guruh hisobiga qo'yiladi — bu qo'lda tahrir
        var sameMonth = await db.LessonDiscounts.AsNoTracking()
            .Where(x => x.StudentId == row.StudentId && x.GroupId == row.GroupId && x.Month == row.Month)
            .ToListAsync(ct);
        return LockExtra(sameMonth);
    }

    /// <summary>Hisobi BEKOR qilinayotgan oyning amaldagi chegirmalarini yopadi (iz qoladi). Aks holda
    /// chegirma "amaldagi" bo'lib qolib, keyin yoziladigan yangi hisobga <see cref="ActiveAmountAsync"/>
    /// orqali yolg'on qo'shilardi, bekor qilish esa "hisob o'chirilgan" deb rad etilardi.</summary>
    public static void CloseForDroppedCharge(IEnumerable<LessonDiscount> sameMonth, string reason)
    {
        foreach (var d in sameMonth.Where(x => x.CancelledAt is null))
        {
            d.CancelledAt = AppClock.Iso();
            d.CancelledByName = reason;
        }
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
