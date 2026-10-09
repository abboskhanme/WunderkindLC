using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Oylik to'lovlarni hisoblash (accrual). Har oy har bir o'quvchiga guruh oylik
/// to'lovi miqdorida qarz yoziladi: balans kamayadi va MonthlyCharge yozuvi yaratiladi.
/// To'lanmagan oylar balansda jamlanib boradi.
/// </summary>
public static class TuitionService
{
    public static string CurrentMonth() => AppClock.Now.ToString("yyyy-MM");

    /// <summary>
    /// Guruh oylik to'lovidan o'quvchining chegirmasini ayirib, hisoblanishi kerak bo'lgan
    /// summa. Avval foiz olib tashlanadi (<paramref name="discountPct"/>, 0..100), keyin aniq
    /// summa (<paramref name="discountAmount"/>) ayriladi. Manfiy chiqsa — 0 qaytadi.
    /// </summary>
    public static decimal ChargeFor(decimal fee, int discountPct, decimal discountAmount)
    {
        if (fee <= 0) return 0m;
        var pct = Math.Clamp(discountPct, 0, 100);
        var amount = Math.Max(0m, discountAmount);
        var afterPct = fee * (100 - pct) / 100m;
        var charge = afterPct - amount;
        if (charge < 0m) charge = 0m;
        return decimal.Round(charge, 2);
    }

    /// <summary>Berilgan oylik to'lovga qo'yiladigan chegirma summasi (fee − effective).
    /// Chegirma fee dan oshmaydi.</summary>
    public static decimal DiscountFor(decimal fee, int discountPct, decimal discountAmount)
    {
        if (fee <= 0) return 0m;
        var effective = ChargeFor(fee, discountPct, discountAmount);
        return decimal.Round(fee - effective, 2);
    }

    /// <summary>
    /// Chegirma berilgan OY ("yyyy-MM") uchun amal qiladimi — davr chegaralari
    /// (<see cref="StudentDiscount.StartMonth"/>..<see cref="StudentDiscount.EndMonth"/>, INKLYUZIV).
    /// Ikkala chegara bo'sh bo'lsa — har doim; bittasi bo'sh — bir tomonlama ochiq.
    ///
    /// <para>⚠️ Bu SOF (entity'siz) funksiya: chegirma REGISTRI ham (<c>DiscountRules.InForce</c>),
    /// pul hisobi ham AYNAN shuni chaqiradi — nusxa ko'chirilsa ikkisi vaqt o'tib ayrilib ketardi
    /// va registr "amalda" deb ko'rsatgan chegirma hisobda qo'llanmay qolardi (yoki teskarisi).</para>
    ///
    /// <para>⚠️ Ilgari bu yerda <c>Student.Discount*</c> ni o'qiydigan overload ham bor edi — u
    /// ATAYIN olib tashlangan: chegirma manbai endi registr.</para>
    /// </summary>
    public static bool DiscountActiveForMonth(string? startMonth, string? endMonth, string month)
    {
        var start = startMonth ?? "";
        var end = endMonth ?? "";
        if (start.Length == 0 && end.Length == 0) return true;
        if (start.Length > 0 && string.CompareOrdinal(month, start) < 0) return false;
        if (end.Length > 0 && string.CompareOrdinal(month, end) > 0) return false;
        return true;
    }

    /// <summary>
    /// Berilgan OY va GURUH hisobi uchun chegirma summasi — <b>chegirma REGISTRIDAN</b>.
    ///
    /// <para>Qaysi qator qo'llanishini <see cref="DiscountRules.Resolve"/> hal qiladi:
    /// avval AYNAN shu guruhning chegirmasi, bo'lmasa «barcha guruhlar» chegirmasi, bo'lmasa 0.
    /// Chegirmalar HECH QACHON QO'SHILMAYDI.</para>
    ///
    /// <para>⚠️ <b>IMZO ATAYIN O'ZGARGAN</b> (ilgari <c>Student</c> olardi): "qatorlarni
    /// yuklashni unutish" xatosini KOMPILYATOR ushlashi kerak. Eski imzo qoldirilganda u
    /// jimgina 0 chegirma berib, o'quvchilarga ortiqcha qarz yozilardi. Qatorlarni
    /// <see cref="DiscountBook"/> orqali oling.</para>
    /// </summary>
    /// <param name="rows">O'quvchining chegirma qatorlari (<c>DiscountBook.For(studentId)</c>).</param>
    /// <param name="groupId">Hisob qatorining guruhi (<c>MonthlyCharge.GroupId</c>; guruhsiz hisobda null).</param>
    public static decimal DiscountForMonth(
        IReadOnlyList<StudentDiscount> rows, decimal fee, string month, string? groupId)
    {
        var win = DiscountRules.Resolve(rows, month, groupId);
        return win is null ? 0m : DiscountFor(fee, win.Pct, win.Amount);
    }

    /// <summary>
    /// Guruh oylik to'lovi o'zgarganda JORIY oy hisobini qayta hisoblaydi: shu guruhga
    /// (asosiy <c>ClassName</c> bo'yicha) biriktirilgan o'quvchilarning shu oygi
    /// <see cref="MonthlyCharge"/> yozuvini yangi narxga moslaydi, balansni farqqa to'g'rilaydi,
    /// <c>Locked</c> (qo'lda tahrirlangan) yozuvlarni o'tkazib yuboradi. Faqat shu oyda hisob
    /// yozuvi BOR o'quvchilar tegiladi (hali hisoblanmagan o'quvchi keyingi AccrueMonth orqali yangi
    /// narxda hisoblanadi). Qaytaradi: nechta o'quvchiga qo'llandi. SaveChanges — chaqiruvchida.
    /// </summary>
    /// <param name="pricing">Kursning sinf oraliqlari — berilmasa bazadan olinadi. Kurs oraliqlari
    /// TAHRIRLANAYOTGAN paytda (hali saqlanmagan) chaqiruvchi YANGI oraliqlarni shu yerga beradi.</param>
    /// <param name="courseId">Guruhning (YANGI) kursi. ⚠️ Kurs shu so'rovda almashtirilayotgan bo'lsa
    /// bazadan o'qish ESKISINI berardi — chaqiruvchi o'zi uzatadi. <c>null</c> — bazadan.</param>
    /// <param name="oldFee">O'zgarishdan OLDINGI guruh narxi. Berilsa: (1) narxi o'zgarmagan o'quvchi
    /// TEGILMAYDI; (2) joriy oy hisobi ESKI to'liq narxga teng bo'lmasa (oy o'rtasidagi qisman hisob —
    /// aktivlashtirish/muzlatish/chiqish) u ham TEGILMAYDI. Aks holda qisman hisob to'liq oylik bilan
    /// ustidan yozilib, o'quvchiga o'qimagan darslari uchun qarz tushardi. <c>null</c> — eski xatti-harakat.</param>
    /// <param name="oldPricing">ESKI oraliqlar (kurs oraliqlari tahrirlanayotganda). <c>null</c> — <paramref name="pricing"/>.</param>
    /// <param name="oldCourseId">ESKI kurs (guruh kursi almashtirilayotganda). <c>null</c> — <paramref name="courseId"/>.</param>
    /// <param name="audit">Berilsa — balansi o'zgargan HAR o'quvchi uchun tarixga BITTA qator yoziladi
    /// (<see cref="GradeRepricing"/> naqshi, `StudentDiscount` turi — o'quvchining to'lov tarixi panelida
    /// ko'rinadi). Ilgari kurs/guruh narxi o'zgarganda balanslar JIMGINA o'zgarardi: o'quvchi tarixida
    /// "qarzim nega oshdi" savoliga javob yo'q edi. SaveChanges — chaqiruvchida.</param>
    /// <remarks>
    /// ⚠️ <b>JORIY va KEYINGI (oldindan to'langan) oylar</b> qayta hisoblanadi (2026-10-07): ilgari faqat
    /// joriy oy tegilardi va avans bilan oldindan ochilgan kelgusi oy hisobi ESKI narxda qolib ketardi
    /// (<see cref="ApplyGradeChangeAsync"/> esa kelgusi oylarni allaqachon qayta hisoblardi). O'tgan oylar
    /// tegilmaydi. Qaytaradi: hisobi o'zgargan o'quvchilar SONI (qatorlar emas).
    /// </remarks>
    public static async Task<int> ApplyGroupFeeToCurrentMonthAsync(IAppDbContext db, string groupId, string groupName, decimal newFee,
                                                                   PricingBook? pricing = null, string? courseId = null,
                                                                   decimal? oldFee = null, PricingBook? oldPricing = null,
                                                                   string? oldCourseId = null, AuditService? audit = null)
    {
        var month = CurrentMonth();
        // O'quvchi → (oy, eski effektiv, yangi effektiv) — audit uchun.
        var changed = new Dictionary<string, (Student S, List<(string Month, decimal Old, decimal New)> Rows)>(StringComparer.Ordinal);
        void Track(Student s, string m, decimal oldEff, decimal newEff)
        {
            if (!changed.TryGetValue(s.Id, out var e)) changed[s.Id] = e = (s, new());
            e.Rows.Add((m, oldEff, newEff));
        }

        // (1) Shu GURUHGA a'zo o'quvchilarning per-guruh hisobi (GroupId == groupId) — joriy va kelgusi oylar.
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            var groupCharges = (await db.MonthlyCharges
                    .Where(c => c.GroupId == groupId && string.Compare(c.Month, month) >= 0).ToListAsync())
                .OrderBy(c => c.Month, StringComparer.Ordinal).ToList();
            if (groupCharges.Count > 0)
            {
                var sids = groupCharges.Select(c => c.StudentId).Distinct().ToList();
                var byId = (await db.Students.Where(s => sids.Contains(s.Id)).ToListAsync())
                    .ToDictionary(s => s.Id);
                // ⚠️ Chegirma kitobi BIR MARTA — o'quvchi boshiga so'rov qilinsa N+1 bo'lardi
                // (guruhda 500 o'quvchi = 500 so'rov).
                var book = await DiscountBook.LoadAsync(db, sids);
                // SINFGA QARAB NARX: `newFee` — guruhning ASOSIY narxi; o'quvchining sinfi kurs
                // oralig'iga tushsa uning narxi oraliqdan olinadi (`TuitionPricing`).
                courseId ??= await db.Classes.Where(c => c.Id == groupId).Select(c => c.CourseId).FirstOrDefaultAsync();
                pricing ??= await PricingBook.LoadForCourseAsync(db, courseId);
                oldPricing ??= pricing;
                oldCourseId ??= courseId;
                foreach (var charge in groupCharges)
                {
                    var st = byId.GetValueOrDefault(charge.StudentId);
                    if (st is null) continue;
                    var fee = pricing.Resolve(courseId, newFee, st, charge.Month).Monthly;
                    if (oldFee is { } of)
                    {
                        var oldResolved = oldPricing.Resolve(oldCourseId, of, st, charge.Month).Monthly;
                        if (oldResolved == fee) continue;            // shu o'quvchining narxi o'zgarmadi
                        if (charge.Amount != oldResolved) continue;  // qisman / qo'lda o'zgartirilgan oy
                    }
                    var oldEff = Math.Max(0m, charge.Amount - charge.Discount);
                    if (ApplyFeeToCharge(charge, st, fee, book))
                        Track(st, charge.Month, oldEff, Math.Max(0m, charge.Amount - charge.Discount));
                }
            }
        }

        // (2) Guruhsiz o'quvchilar (a'zoligi yo'q, ClassName == groupName) — GroupId=null hisobi.
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var nameStudents = await db.Students.Where(s => s.ClassName == groupName).ToListAsync();
            if (nameStudents.Count > 0)
            {
                var ids = nameStudents.Select(s => s.Id).ToList();
                var nameCharges = (await db.MonthlyCharges
                        .Where(c => c.GroupId == null && ids.Contains(c.StudentId) && string.Compare(c.Month, month) >= 0)
                        .ToListAsync())
                    .OrderBy(c => c.Month, StringComparer.Ordinal).ToList();
                var byId = nameStudents.ToDictionary(s => s.Id);
                var book = await DiscountBook.LoadAsync(db, ids);
                foreach (var charge in nameCharges)
                {
                    // Qisman oy hisobi to'liq oylik bilan ustidan yozilmasin (yuqoridagi bilan bir xil qoida).
                    if (oldFee is { } of && (of == newFee || charge.Amount != of)) continue;
                    var st = byId.GetValueOrDefault(charge.StudentId);
                    var oldEff = Math.Max(0m, charge.Amount - charge.Discount);
                    if (ApplyFeeToCharge(charge, st, newFee, book))
                        Track(st!, charge.Month, oldEff, Math.Max(0m, charge.Amount - charge.Discount));
                }
            }
        }

        if (audit is not null)
            foreach (var (s, rows) in changed.Values)
                audit.Record(AuditService.EntityStudentDiscount, s.Id, "update",
                    $"Oylik guruh/kurs narxi o'zgargani uchun qayta hisoblandi: {groupName}, "
                    + string.Join("; ", rows.Select(r => $"{r.Month} — {AuditService.Money(r.Old)} → {AuditService.Money(r.New)} so'm"))
                    + $" — {s.FullName}",
                    studentId: s.Id);
        return changed.Count;
    }

    /// <summary>Sinf o'zgarishi tufayli qayta hisoblangan bitta oy (audit uchun).</summary>
    public sealed record GradeRepriceChange(string GroupName, string Month, decimal OldAmount, decimal NewAmount);

    /// <summary>
    /// O'QUVCHINING MAKTAB SINFI o'zgarganda — uning JORIY va undan KEYINGI (oldindan to'langan) oylari
    /// hisobini kurs SINF NARXIGA darhol moslaydi (`.claude/rules/billing.md` «SINFGA QARAB OYLIK»).
    /// Sinf narxi yo'q kurs (yoki sinfi narxga ta'sir qilmaydigan guruh) — TEGILMAYDI: u yerda
    /// o'quvchi guruhning oddiy narxida qoladi. Balans farqqa to'g'rilanadi. SaveChanges — chaqiruvchida.
    ///
    /// <para>Tegilmaydi: o'tgan oylar (tarix), <c>Locked</c> (qo'lda tahrirlangan, «Chegirma» qo'yilgan)
    /// qatorlar va ESKI to'liq narxga teng bo'lmagan qator — oy o'rtasidagi QISMAN hisob
    /// (aktivlashtirish/muzlatish/chiqish); u to'liq oylik bilan ustidan yozilsa o'quvchiga o'qimagan
    /// darslari uchun qarz tushardi (<see cref="ApplyGroupFeeToCurrentMonthAsync"/> bilan bir xil qoida).</para>
    /// </summary>
    /// <param name="oldGrade">O'zgarishdan OLDINGI <see cref="Student.SchoolGrade"/>.</param>
    /// <param name="oldGradeYear">O'zgarishdan OLDINGI <see cref="Student.SchoolGradeYear"/>.</param>
    public static async Task<List<GradeRepriceChange>> ApplyGradeChangeAsync(IAppDbContext db, Student s,
                                                                            int? oldGrade, int? oldGradeYear)
    {
        if (oldGrade == s.SchoolGrade && oldGradeYear == s.SchoolGradeYear) return [];
        var month = CurrentMonth();
        var charges = (await db.MonthlyCharges.Where(c => c.StudentId == s.Id && c.GroupId != null).ToListAsync())
            .Where(c => !c.Locked && string.CompareOrdinal(c.Month, month) >= 0)
            .OrderBy(c => c.Month, StringComparer.Ordinal)
            .ToList();
        if (charges.Count == 0) return [];

        var groupIds = charges.Select(c => c.GroupId!).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id);
        var courseIds = groups.Values.Select(g => g.CourseId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (courseIds.Count == 0) return [];
        var pricing = PricingBook.From(await db.SubjectPriceBands.AsNoTracking()
            .Where(b => courseIds.Contains(b.SubjectId)).ToListAsync());
        var before = new Student { Id = s.Id, SchoolGrade = oldGrade, SchoolGradeYear = oldGradeYear };
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);

        var changes = new List<GradeRepriceChange>();
        foreach (var charge in charges)
        {
            if (!groups.TryGetValue(charge.GroupId!, out var g)) continue;
            var newFee = pricing.MonthlyFee(g, s, charge.Month);
            var oldFee = pricing.MonthlyFee(g, before, charge.Month);
            if (newFee == oldFee) continue;          // bu guruhda sinf narxga ta'sir qilmaydi
            if (charge.Amount != oldFee) continue;   // qisman oy — tegilmaydi
            var oldAmount = charge.Amount;
            if (ApplyFeeToCharge(charge, s, newFee, book))
                changes.Add(new GradeRepriceChange(g.Name, charge.Month, oldAmount, newFee));
        }
        return changes;
    }

    /// <summary>Bitta hisob qatorini yangi narxga moslaydi (balans farqqa to'g'rilanadi). <c>Locked</c>
    /// (qo'lda tahrirlangan) yoki o'zgarishsiz bo'lsa tegmaydi. Qaytaradi: qo'llandimi.</summary>
    private static bool ApplyFeeToCharge(MonthlyCharge charge, Student? s, decimal newFee, DiscountBook book)
    {
        if (s is null || charge.Locked) return false;
        var newDiscount = book.DiscountFor(s.Id, newFee, charge.Month, charge.GroupId);
        var newEffective = newFee - newDiscount;
        var oldEffective = charge.Amount - charge.Discount;
        var delta = newEffective - oldEffective;
        if (delta == 0 && charge.Amount == newFee && charge.Discount == newDiscount) return false;
        charge.Amount = newFee;
        charge.Discount = newDiscount;
        s.Balance -= delta;
        return true;
    }

    /// <summary>"yyyy-MM" -> keyingi oy "yyyy-MM".</summary>
    public static string NextMonth(string month)
    {
        var year = int.Parse(month[..4]);
        var m = int.Parse(month[5..]);
        if (m == 12) { year++; m = 1; } else { m++; }
        return $"{year:D4}-{m:D2}";
    }

    /// <summary>Bitta <see cref="MonthRange"/> chaqirig'i qaytaradigan oylarning MAKSIMUMI (buzuq
    /// sanaga qarshi xavfsizlik chegarasi).</summary>
    private const int MaxRangeMonths = 1200;

    /// <summary>fromMonth..toMonth (inklyuziv) oralig'idagi oylar ("yyyy-MM"). from > to bo'lsa — bo'sh.</summary>
    public static IEnumerable<string> MonthRange(string fromMonth, string toMonth)
    {
        if (string.IsNullOrEmpty(fromMonth) || string.IsNullOrEmpty(toMonth)) yield break;
        // Buzuq oy ("2026", "abcd-ef", "2026-13") — bo'sh oraliq: ilgari `NextMonth` istisno berib butun
        // so'rov 500 bilan yiqilardi (yoki cheksiz siklga tushardi). Oy AYNAN "yyyy-MM" (`PaymentFields.IsMonth`).
        if (!PaymentFields.IsMonth(fromMonth) || !PaymentFields.IsMonth(toMonth)) yield break;
        var m = fromMonth;
        // XAVFSIZLIK CHEGARASI: NextMonth oyni normallashtirmaydi ("2026-13" → "2026-14" → ...),
        // shuning uchun BUZUQ oy bilan shart hech qachon buzilmas va sikl abadiy davom etardi
        // (so'rov osilib qolardi). 1200 oy = 100 yil — haqiqiy ma'lumotda erishib bo'lmaydi.
        for (var guard = 0; guard < MaxRangeMonths && string.CompareOrdinal(m, toMonth) <= 0; guard++)
        {
            yield return m;
            m = NextMonth(m);
        }
    }

    /// <summary>
    /// O'quv yili boshlanish oyi ("yyyy-MM") — faol (arxivlanmagan) o'quvchilarning ENG ERTA
    /// qabul (EnrollmentDate) oyi. Faol o'quvchi yoki sana bo'lmasa — joriy oy.
    /// Maosh/hisob shu oydan boshlanadi.
    /// </summary>
    public static async Task<string> AcademicYearStartMonthAsync(IAppDbContext db)
    {
        var dates = await db.Students
            .Where(s => !s.IsArchived && s.EnrollmentDate.Length >= 7)
            .Select(s => s.EnrollmentDate).ToListAsync();
        return dates.Count == 0 ? CurrentMonth() : dates.Min()![..7];
    }

    /// <summary>
    /// Frontend jadval/hafta navigatsiyasi uchun BITTA sintetik davr (markazda chorak tizimi yo'q).
    /// O'quv yili boshlanish oyidan ~10 oy oraliq. Frontend shu davrni haftalarga bo'lib jadvalni
    /// ko'rsatadi (eski chorak-asosli mantiq buzilmasin).
    /// </summary>
    public static async Task<List<QuarterPeriodDto>> SyntheticPeriodsAsync(IAppDbContext db)
    {
        var start = await AcademicYearStartMonthAsync(db);
        var end = start;
        for (var i = 0; i < 10; i++) end = NextMonth(end);
        return new List<QuarterPeriodDto> { new(1, $"{start}-01", $"{end}-28", true) };
    }

    /// <summary>
    /// O'quvchining shu paytdagi to'liq oylik to'lovi (chegirmasiz). Ko'p-guruh: barcha FAOL
    /// guruhlari oylik narxining yig'indisi (aggregate). A'zoligi bo'lmasa — eski ClassName
    /// bo'yicha guruh narxi (orqaga moslik). <paramref name="feesById"/>/<paramref name="feesByName"/>
    /// — oldindan yuklangan narx jadvallari; <paramref name="activeGroupIds"/> — o'quvchining
    /// faol guruh id'lari.
    /// </summary>
    public static decimal GrossFee(
        Student s,
        IDictionary<string, decimal> feesById,
        IDictionary<string, decimal> feesByName,
        IReadOnlyCollection<string>? activeGroupIds)
    {
        if (activeGroupIds is { Count: > 0 })
            return activeGroupIds.Sum(gid => feesById.TryGetValue(gid, out var f) ? f : 0m);
        return feesByName.TryGetValue(s.ClassName, out var fee) ? fee : 0m;
    }

    /// <summary>
    /// Qisman-oy to'lovini hisoblaydi (aktivlashtirish/muzlatish uchun yagona formula):
    ///   - <paramref name="lessons"/> = shu segmentdagi billable dars soni (qolgan yoki qatnashilgan);
    ///   - dars soni <paramref name="totalInMonth"/> ga TENG (oyning birinchi darsidan boshlagan /
    ///     butun oy o'qigan) → TO'LIQ oylik narx;
    ///   - aks holda → dars soni × <paramref name="lessonFee"/> (kursning bir dars yaxlit narxi);
    ///   - <paramref name="lessonFee"/> 0 (kursda kiritilmagan) bo'lsa → eski pro-rata (oylik × dars ÷ jami);
    ///   - har holatda to'liq oylik narxdan OSHMAYDI (qisman oy to'liqdan qimmat bo'lib qolmasin).
    ///
    /// <para>⚠️ **«12 dars» CHEGARASI OLIB TASHLANDI (2026-09-20, foydalanuvchi qarori).** Ilgari
    /// 12 va undan ko'p dars qolgan bo'lsa to'liq oylik olinardi. Endi oy o'rtasida qo'shilgan
    /// o'quvchiga HAR DOIM "qolgan dars × dars narxi" yoziladi: markazning modeli — oylik abonement,
    /// dars soni esa FAQAT haqiqatan kam dars o'tgan holatda ahamiyatli. Yuqoridagi CHEGARA
    /// (to'liq oylikdan oshmaslik) bu yerda muhim: dars narxi to'g'ri qo'yilgan bo'lsa to'liq oy
    /// baribir to'liq oylikka teng chiqadi.</para>
    /// </summary>
    public static decimal ProratedLessonCharge(decimal monthlyFee, decimal lessonFee, int lessons, int totalInMonth)
    {
        if (monthlyFee <= 0 || lessons <= 0 || totalInMonth <= 0) return 0m;
        // To'liq oy: birinchi darsdan boshlagan (lessons == totalInMonth).
        if (lessons >= totalInMonth) return decimal.Round(monthlyFee, 2);
        // Qisman: har bir dars uchun yaxlit summa; kursda narx yo'q bo'lsa eski pro-rata.
        var partial = lessonFee > 0
            ? lessonFee * lessons
            : monthlyFee * lessons / totalInMonth;
        return decimal.Round(Math.Min(partial, monthlyFee), 2);
    }

    /// <summary>Kursning (Subject) bir dars yaxlit narxi (LessonPrice). CourseId bo'sh/topilmasa 0.
    /// <para>⚠️ Mantiq OMMAVIY variantda (<see cref="LessonFeesForCoursesAsync"/>) — ikki joyda ayri
    /// qoida QOLMASIN: bo'sh CourseId, topilmagan kurs va kiritilmagan narx uchun ikkalasi ham
    /// AYNAN bir xil (0) qaytaradi.</para></summary>
    private static async Task<decimal> LessonFeeForCourseAsync(IAppDbContext db, string? courseId)
    {
        if (string.IsNullOrEmpty(courseId)) return 0m;
        var fees = await LessonFeesForCoursesAsync(db, [courseId]);
        return fees.TryGetValue(courseId, out var fee) ? fee : 0m;
    }

    /// <summary>
    /// Bir nechta kursning bir dars yaxlit narxi (LessonPrice) — BITTA so'rovda.
    ///
    /// <para><b>Nega kerak:</b> ommaviy muzlatishda guruh (demak KURS ham) odatda bitta va bir xil,
    /// lekin narx har a'zolik uchun qaytadan so'ralardi — 500 ta o'quvchida <c>Subjects</c> jadvaliga
    /// 500 ta bir xil so'rov. Bu yerda distinct kurslar bir marta yuklanadi va natija halqada
    /// <c>lessonFee</c> sifatida pastga uzatiladi.</para>
    ///
    /// <para>Bo'sh/null <c>courseId</c> lug'atga umuman KIRMAYDI, topilmagan kurs ham yo'q — ikkala
    /// holatda chaqiruvchi 0 oladi, ya'ni yakka variant bilan bir xil semantika.</para>
    /// </summary>
    public static async Task<Dictionary<string, decimal>> LessonFeesForCoursesAsync(
        IAppDbContext db, IEnumerable<string?> courseIds)
    {
        var ids = courseIds.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (ids.Count == 0) return result;
        var rows = await db.Subjects.Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.LessonPrice }).ToListAsync();
        foreach (var r in rows) result[r.Id] = r.LessonPrice;
        return result;
    }

    /// <summary>Hafta kunlari (0=Du..6=Yak) bo'yicha [from..to] (inklyuziv) oralig'idagi darslar soni.</summary>
    public static int LessonsInRange(IReadOnlyCollection<int> days, DateOnly from, DateOnly to)
    {
        if (days.Count == 0 || from > to) return 0;
        var set = days.ToHashSet();
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var wd = ((int)d.DayOfWeek + 6) % 7; // Dushanba=0..Yakshanba=6
            if (set.Contains(wd)) count++;
        }
        return count;
    }

    /// <summary>Aktivlashtirilgan oyning QISMAN to'lovi (chegirmasiz) — <see cref="ChargeActivationProrateAsync"/>
    /// yozadigan summaning O'ZI, hech narsa yozmasdan. <c>Monthly</c> — shu oy uchun o'quvchining oyligi
    /// (sinf oralig'i bo'yicha), <c>Gross</c> — qolgan darslar bo'yicha qisman summa (0 — narx yo'q, sana
    /// buzuq, shu oyda dars yo'q yoki sana oyning oxirgi darsidan keyin).</summary>
    private static async Task<(decimal Monthly, decimal Gross)> ActivationGrossAsync(
        IAppDbContext db, Student s, Group cls, string dateIso, decimal? lessonFee, PricingBook? pricing)
    {
        if (dateIso.Length < 10 || !DateOnly.TryParse(dateIso, out var d)) return (0m, 0m);
        // SINFGA QARAB NARX (`TuitionPricing`): oraliq topilsa uning oyligi va bir dars narxi.
        var (monthly, bandLesson) = (pricing ?? await PricingBook.LoadForCourseAsync(db, cls.CourseId))
            .Resolve(cls, s, dateIso[..7]);
        if (monthly <= 0) return (monthly, 0m);
        var monthStart = new DateOnly(d.Year, d.Month, 1);
        var monthEnd = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        var totalInMonth = LessonsInRange(cls.Days, monthStart, monthEnd);
        var remaining = LessonsInRange(cls.Days, d, monthEnd);
        if (totalInMonth <= 0 || remaining <= 0) return (monthly, 0m); // shu oyda dars yo'q — qisman to'lov yo'q

        // Formula: birinchi darsdan (remaining == jami) → to'liq oylik; aks holda qolgan dars ×
        // kursning bir dars yaxlit narxi (LessonPrice; kiritilmagan bo'lsa eski pro-rata).
        var fee = bandLesson ?? lessonFee ?? await LessonFeeForCourseAsync(db, cls.CourseId);
        return (monthly, ProratedLessonCharge(monthly, fee, remaining, totalInMonth));
    }

    /// <summary>Aktivlashtirilgan oyning QISMAN to'lovini hisoblab o'quvchiga yozadi (balans kamayadi,
    /// shu oy MonthlyCharge'iga qo'shiladi yoki yaratiladi). Formula (<see cref="ProratedLessonCharge"/>):
    /// oyning BIRINCHI darsidan aktivlashtirilgan (qolgan == jami) → TO'LIQ oylik narx; aks holda
    /// qolgan dars × kursning bir dars yaxlit narxi (LessonPrice; kiritilmagan bo'lsa eski
    /// pro-rata). To'liq oylikdan oshmaydi. Chegirma qo'llanadi. SaveChanges — chaqiruvchida.</summary>
    /// <param name="addSegment">true bo'lsa (shu OYDA muzlatilgandan keyin QAYTA aktivlashtirish) — yangi
    /// studied segment mavjud (muzlatishgacha studied) hisobga QO'SHILADI, almashtirilmaydi. Aks holda
    /// (birinchi aktivlashtirish / ikki marta bosish) idempotent ALMASHTIRADI.</param>
    /// <param name="lessonFee">Kursning bir dars yaxlit narxi, OLDINDAN hisoblangan bo'lsa (ommaviy
    /// amalda — <see cref="LessonFeesForCoursesAsync"/>). ⚠️ FAQAT tezlik uchun: <c>null</c> bo'lsa
    /// AYNAN o'sha qiymat shu yerda yakka so'rov bilan olinadi, ya'ni natija bir xil. Ommaviy
    /// aktivlashtirishda guruh (demak kurs) bitta bo'lgani uchun bu bir xil so'rovni 500 martadan
    /// 1 martaga tushiradi. Qolgan chaqiruvchilar (yakka aktivlashtirish, guruh almashtirish,
    /// sertifikat bilan tugatish) parametrni bermaydi va avvalgidek ishlaydi —
    /// <see cref="ChargeFreezeProrateAsync"/> dagi bilan AYNAN bir xil naqsh.</param>
    /// <param name="pricing">Kurslarning sinf oraliqlari, OLDINDAN yuklangan bo'lsa (ommaviy amal).
    /// <c>null</c> — shu kursniki yakka so'rov bilan olinadi; natija bir xil.</param>
    public static async Task ChargeActivationProrateAsync(IAppDbContext db, Student s, Group cls, string dateIso, bool addSegment = false, decimal? lessonFee = null,
                                                          PricingBook? pricing = null)
    {
        try
        {
            // Summa hisobi — `ActivationGrossAsync` (YAGONA joy: `EnsureChargeAsync` ning aktivlashtirish
            // oyi shoxobchasi va kelmagan darslar chegirmasi previewi ham AYNAN shuni ishlatadi).
            var (monthly, gross) = await ActivationGrossAsync(db, s, cls, dateIso, lessonFee, pricing);
            if (gross <= 0) return; // narx yo'q / shu oyda dars yo'q / oyning oxirgi darsidan keyin

        var month = dateIso[..7];
        // Chegirma REGISTRDAN — shu o'quvchining qatorlari (aktivlashtirish bitta o'quvchi uchun,
        // ya'ni bitta yengil so'rov; ommaviy amalda ham a'zolik boshiga bittadan).
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
        var discount = book.DiscountFor(s.Id, gross, month, cls.Id);
        var effective = gross - discount;

        // Shu (o'quvchi, oy) uchun kerak bo'ladigan IKKALA qator ham BITTA so'rovda: aggregate
        // (GroupId=null) va shu guruhniki. Ilgari bular ketma-ket ikki so'rov edi
        // (`PurgeAggregateRowAsync` + `existing`) — ommaviy amalda a'zolik boshiga ikkita ortiqcha
        // aylanish. Filtr `MonthlyCharges(StudentId, GroupId, Month)` unikal indeksining prefiksiga
        // tushadi. Naqsh `ChargeFreezeProrateAsync` dagi bilan AYNAN bir xil.
        var monthRows = await db.MonthlyCharges
            .Where(c => c.StudentId == s.Id && c.Month == month && (c.GroupId == null || c.GroupId == cls.Id))
            .ToListAsync();

        // ⚠️ TARTIB O'ZGARMAYDI: per-guruh billingga o'tdik — shu oyning eski aggregate (GroupId=null)
        // qatorini `existing` bilan ishlashdan OLDIN tozalaymiz (dublikat hisob bo'lmasin). Balans
        // hisobi shunga tayanadi: aggregate qatorning effektivi avval balansga QAYTARILADI, keyin
        // quyida per-guruh qisman hisob yechiladi.
        // ⚠️ O'chirishga belgilangan (hali saqlanmagan) qator so'rovda baribir qaytadi — u ikkinchi marta
        // balansga QAYTARILMASIN (bir so'rovda bir o'quvchi ikki guruhda aktivlashtirilganda).
        var aggregateRow = monthRows.FirstOrDefault(c => c.GroupId == null
                                                          && db.MonthlyCharges.Entry(c).State != EntityState.Deleted);
        if (aggregateRow is not null)
        {
            s.Balance += Math.Max(0m, aggregateRow.Amount - aggregateRow.Discount);
            db.MonthlyCharges.Remove(aggregateRow);
        }
        // Per-guruh: hisob shu GURUH (cls.Id) uchun yoziladi.
        var existing = monthRows.FirstOrDefault(c => c.GroupId == cls.Id);
        if (existing is null)
        {
            db.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = cls.Id, Month = month, Amount = gross, Discount = discount, Date = dateIso,
            });
            s.Balance -= effective;
        }
        else
        {
            // Qo'lda tahrirlangan (qulfli) qatorga tegmaymiz. ⚠️ Qulf FAQAT kelmagan darslar
            // chegirmasidan bo'lsa — qator qayta hisoblanadi va o'sha chegirmalar QO'SHILADI
            // (`LessonDiscountService.LockExtra`), aks holda yangi segment hech qachon yozilmasdi.
            var lessonExtra = await LessonDiscountService.ReworkableExtraAsync(db, existing);
            if (lessonExtra is not { } extra) return;
            var oldEffective = Math.Max(0m, existing.Amount - existing.Discount);
            if (addSegment)
            {
                // SHU OYDA muzlatilgandan keyin QAYTA aktivlashtirish: mavjud hisob = muzlatishgacha studied
                // segment. Yangi segment (shu sanadan oy oxirigacha) USTIGA QO'SHILADI — gap (muzlatish↔qayta
                // aktiv) hisoblanmaydi, studied portion yo'qolmaydi. Yig'indi to'liq oylikdan oshmaydi.
                var newAmount = Math.Min(existing.Amount + gross, monthly);
                var newDiscount = Math.Min(newAmount, book.DiscountFor(s.Id, newAmount, month, cls.Id) + extra);
                existing.Amount = newAmount;
                existing.Discount = newDiscount;
                existing.Date = dateIso;
                s.Balance += oldEffective - (newAmount - newDiscount);
            }
            else
            {
                // IDEMPOTENT: birinchi aktivlashtirish (eski to'liq AccrueMonth qatori ustidan) yoki ikki marta
                // bosish — ALMASHTIRAMIZ (ikki marta yozilmasin).
                var newDiscount = Math.Min(gross, discount + extra);
                existing.Amount = gross;
                existing.Discount = newDiscount;
                existing.Date = dateIso;
                s.Balance += oldEffective - (gross - newDiscount);
            }
        }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ChargeActivationProrateAsync error: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// AKTIVLASHTIRISH SANASI O'ZGARGANDA (<see cref="ActivationDateChange"/>) shu sananing oyini QAYTA hisoblaydi:
    /// qolgan darslar bo'lsa — <see cref="ChargeActivationProrateAsync"/> (idempotent almashtirish); shu sanadan keyin
    /// oyda dars qolmasa — oyning (qulfsiz) hisobi olib tashlanadi va effektiv summa balansga qaytadi.
    /// Qaytaradi: <c>false</c> — oy QO'LDA tahrirlangan (qulf), tegilmadi. SaveChanges — chaqiruvchida.
    /// </summary>
    public static async Task<bool> RechargeActivationMonthAsync(IAppDbContext db, Student s, Group cls, string dateIso)
    {
        var month = dateIso[..7];
        var row = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == cls.Id && c.Month == month);
        // Qo'lda tahrir qulfi — tegilmaydi (kelmagan darslar chegirmasi qulfi esa qayta hisoblanadi).
        if (row is not null && await LessonDiscountService.ReworkableExtraAsync(db, row) is null) return false;
        var (_, gross) = await ActivationGrossAsync(db, s, cls, dateIso, null, null);
        if (gross > 0)
        {
            await ChargeActivationProrateAsync(db, s, cls, dateIso);
            return true;
        }
        if (row is null) return true;
        // Kelmagan darslar chegirmasi qulflagan qator — chegirmalari bilan qo'lda hal qilinadi (jimgina qolmasin).
        if (row.Locked) return false;
        s.Balance += Math.Max(0m, row.Amount - row.Discount);
        db.MonthlyCharges.Remove(row);
        return true;
    }

    /// <summary>
    /// Oyni TO'LIQ oylik qilib qayta hisoblaydi (aktivlashtirish sanasi oldingi oyga surilganda ESKI aktivlashtirish
    /// oyi qisman emas, to'liq o'qilgan oy bo'lib qoladi). Narx — <see cref="PricingBook"/> (sinf oralig'i), chegirma —
    /// registrdan (+ amaldagi kelmagan darslar chegirmasi). Hisob qatori YO'Q bo'lsa — hech narsa (uni
    /// <see cref="AccrueCatchUpAsync"/> yozadi). Qaytaradi: <c>false</c> — qo'lda tahrirlangan, tegilmadi.
    /// SaveChanges — chaqiruvchida.
    /// </summary>
    public static async Task<bool> RechargeFullMonthAsync(IAppDbContext db, Student s, Group cls, string month)
    {
        var row = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == cls.Id && c.Month == month);
        if (row is null) return true;
        if (await LessonDiscountService.ReworkableExtraAsync(db, row) is not { } extra) return false;
        var fee = (await PricingBook.LoadForCourseAsync(db, cls.CourseId)).MonthlyFee(cls, s, month);
        if (fee <= 0) return true;
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
        var discount = Math.Min(fee, book.DiscountFor(s.Id, fee, month, cls.Id) + extra);
        var oldEffective = Math.Max(0m, row.Amount - row.Discount);
        row.Amount = fee;
        row.Discount = discount;
        row.Date = $"{month}-01";
        s.Balance += oldEffective - (fee - discount);
        return true;
    }

    /// <summary>MUZLATILGAN oyning QISMAN to'lovini hisoblaydi: o'quvchi shu oyda muzlatilgan SANAGACHA (shu sana
    /// ham kiradi) qatnashgan darslar uchun to'lov. Bir dars = oylik narx ÷ shu oydagi jami dars; to'lov = bir dars ×
    /// (faol boshlanishidan muzlatish sanasigacha, MUZLATISH SANASI HAM hisobga olinadi — o'sha kuni dars bo'lsa
    /// qo'shiladi). Faol boshlanishi = shu oyda aktivlashtirilgan bo'lsa o'sha sana, aks holda oy boshi. Mavjud
    /// yozuvni IDEMPOTENT almashtiradi; <c>Locked</c> bo'lsa tegmaydi.
    /// <para>CARRY-FORWARD: SHU OYDA muzlatish→qayta aktivlashtirish→yana muzlatish tsikli bo'lsa (masalan
    /// 1-yanv aktiv → 10-yanv muzlatish → 15-yanv qayta aktiv → 20-yanv yana muzlatish), oldingi allaqachon
    /// YAKUNLANGAN segmentlarning to'lovi (1–9 yanv) YO'QOLMASLIGI kerak. Buning uchun <c>existing.Date</c> aynan
    /// <paramref name="activatedAtIso"/> bo'lsa (ya'ni joriy faol segment aktivlashtirishda yozilgan), o'sha
    /// aktivlashtirishda qo'shilgan PROYEKSIYON gross'ni qayta hisoblab, uni <c>existing.Amount</c>dan ayiramiz —
    /// natija "carry" (oldingi segmentlar summasi). Yangi jami = min(carry + joriy segment gross, oylik narx).
    /// Oy davomida BIRINCHI muzlatishda (existing shu aktivlashtirishga tegishli emas yoki umuman yo'q) carry=0 —
    /// eski almashtirish xatti-harakati o'zgarmaydi.</para></summary>
    /// <param name="lessonFee">Kursning bir dars yaxlit narxi, OLDINDAN hisoblangan bo'lsa (ommaviy
    /// amalda — <see cref="LessonFeesForCoursesAsync"/>). ⚠️ FAQAT tezlik uchun: <c>null</c> bo'lsa
    /// aynan o'sha qiymat shu yerda yakka so'rov bilan olinadi, ya'ni natija bir xil. Ommaviy
    /// muzlatishda guruh bitta bo'lgani uchun bu bir xil so'rovni 500 martadan 1 martaga tushiradi.</param>
    /// <param name="pricing">Kurslarning sinf oraliqlari, OLDINDAN yuklangan bo'lsa (ommaviy amal).</param>
    public static async Task ChargeFreezeProrateAsync(IAppDbContext db, Student s, Group cls, string activatedAtIso, string freezeDateIso, decimal? lessonFee = null,
                                                      PricingBook? pricing = null)
    {
        if (freezeDateIso.Length < 10 || !DateOnly.TryParse(freezeDateIso, out var fz)) return;
        // SINFGA QARAB NARX (`TuitionPricing`) — muzlatish oyi bo'yicha.
        var (monthly, bandLesson) = (pricing ?? await PricingBook.LoadForCourseAsync(db, cls.CourseId))
            .Resolve(cls, s, freezeDateIso[..7]);
        if (monthly <= 0) return;
        var monthStart = new DateOnly(fz.Year, fz.Month, 1);
        var monthEnd = new DateOnly(fz.Year, fz.Month, DateTime.DaysInMonth(fz.Year, fz.Month));
        var totalInMonth = LessonsInRange(cls.Days, monthStart, monthEnd);
        var month = freezeDateIso[..7];

        // Faol boshlanishi: shu oyda aktivlashtirilgan bo'lsa o'sha sanadan, aks holda oy boshidan.
        var activeFrom = monthStart;
        var activatedThisMonth = false;
        DateOnly act = default;
        if (!string.IsNullOrEmpty(activatedAtIso) && activatedAtIso.Length >= 10 && activatedAtIso[..7] == month
            && DateOnly.TryParse(activatedAtIso, out act) && act > monthStart)
        {
            activeFrom = act;
            activatedThisMonth = true;
        }
        // Muzlatish SANASIGACHA qatnashilgan darslar — shu sananing O'ZI HAM kiradi (o'sha kuni dars bo'lsa
        // hisoblanadi): bugungi sanadan muzlatilsa bugungi dars ham to'lovga qo'shiladi.
        var studied = fz >= activeFrom ? LessonsInRange(cls.Days, activeFrom, fz) : 0;
        // Qatnashilgan darslar uchun (aktivlashtirish bilan bir xil formula): jami/12+ → to'liq, aks holda
        // qatnashilgan dars × kursning bir dars yaxlit narxi (LessonPrice; yo'q bo'lsa eski pro-rata).
        var fee = bandLesson ?? lessonFee ?? await LessonFeeForCourseAsync(db, cls.CourseId);
        var gross = ProratedLessonCharge(monthly, fee, studied, totalInMonth);

        // Shu (o'quvchi, oy) uchun kerak bo'ladigan IKKALA qator ham BITTA so'rovda: aggregate
        // (GroupId=null) va shu guruhniki. Ilgari bular ketma-ket ikki so'rov edi
        // (`PurgeAggregateRowAsync` + `existing`) — ommaviy amalda a'zolik boshiga ikkita ortiqcha
        // aylanish. Filtr `MonthlyCharges(StudentId, GroupId, Month)` indeksining prefiksiga tushadi.
        var monthRows = await db.MonthlyCharges
            .Where(c => c.StudentId == s.Id && c.Month == month && (c.GroupId == null || c.GroupId == cls.Id))
            .ToListAsync();

        // ⚠️ TARTIB O'ZGARMAYDI: per-guruh billingga o'tdik — shu oyning eski aggregate (GroupId=null)
        // qatorini `existing` bilan ishlashdan OLDIN tozalaymiz (aks holda muzlatish faqat per-guruh
        // qatorni kamaytirib, aggregate qator to'liq oy bo'lib qolardi). Balans hisobi shunga tayanadi.
        var aggregateRow = monthRows.FirstOrDefault(c => c.GroupId == null);
        if (aggregateRow is not null)
        {
            s.Balance += Math.Max(0m, aggregateRow.Amount - aggregateRow.Discount);
            db.MonthlyCharges.Remove(aggregateRow);
        }
        var existing = monthRows.FirstOrDefault(c => c.GroupId == cls.Id);

        // SHU OYDA muzlatib-qayta aktivlashtirish tsikli bo'lgan bo'lsa (existing aynan shu aktivlashtirish
        // paytida yozilgan — Date == activatedAtIso), oldingi (allaqachon yakunlangan) segmentlar summasini
        // "carry" sifatida tiklaymiz — ular ustiga faqat YANGI segment qo'shiladi, umuman ALMASHTIRILMAYDI.
        var carry = 0m;
        if (activatedThisMonth && existing is not null && existing.Date == activatedAtIso)
        {
            var remainingAtActivation = LessonsInRange(cls.Days, act, monthEnd);
            var projectedAtActivation = ProratedLessonCharge(monthly, fee, remainingAtActivation, totalInMonth);
            carry = Math.Max(0m, existing.Amount - projectedAtActivation);
        }
        var totalGross = Math.Min(carry + gross, monthly);
        // Chegirma REGISTRDAN (qarang: ChargeActivationProrateAsync).
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
        var discount = totalGross > 0 ? book.DiscountFor(s.Id, totalGross, month, cls.Id) : 0m;

        if (existing is null)
        {
            if (totalGross <= 0) return;
            db.MonthlyCharges.Add(new MonthlyCharge
            {
                StudentId = s.Id, GroupId = cls.Id, Month = month, Amount = totalGross, Discount = discount, Date = freezeDateIso,
            });
            s.Balance -= totalGross - discount;
        }
        else
        {
            // Qo'lda tahrirlangan (qulfli) qatorga tegmaymiz. ⚠️ Qulf FAQAT kelmagan darslar chegirmasidan
            // bo'lsa — oy BARIBIR qisqartiriladi va amaldagi chegirmalar yangi summaga QO'SHILADI (qirqib):
            // ilgari bunday qator butunlay o'tkazib yuborilardi va oy o'rtasida guruh almashtirilsa eski
            // guruh TO'LIQ oylikni, yangi guruh oyning qolgan qismini yozardi (IKKI MARTA hisob).
            var lessonExtra = await LessonDiscountService.ReworkableExtraAsync(db, existing);
            if (lessonExtra is not { } extra) return;
            discount = Math.Min(totalGross, discount + extra);
            var oldEffective = Math.Max(0m, existing.Amount - existing.Discount);
            existing.Amount = totalGross;
            existing.Discount = discount;
            existing.Date = freezeDateIso;
            s.Balance += oldEffective - (totalGross - discount);
        }
    }

    /// <summary>Bitta oy uchun hisoblash (PER-GURUH). Har FAOL a'zolik uchun alohida hisob qatori
    /// (StudentId, GroupId, Month); guruhsiz (eski ClassName) o'quvchiga GroupId=null. Allaqachon hisoblangan
    /// (o'quvchi, guruh) juftliklari o'tkazib yuboriladi — idempotent.
    /// <para>⚠️ BITTA O'QUVCHINING XATOSI BUTUN YUGURISHNI TO'XTATMAYDI (audit R1-6): saqlash
    /// (<see cref="DbUpdateException"/> — masalan parallel yo'l AYNI (o'quvchi, guruh, oy) hisobini
    /// yaratib unikal indeks rad etdi) shu o'quvchining yozuvlari kontekstdan ajratiladi, logga yoziladi va
    /// keyingi o'quvchiga o'tiladi. Ilgari istisno butun yugurishni uzar, ALLAQACHON saqlangan hisoblar
    /// uchun "oylik hisoblandi" xabari ketmas (<c>Created</c> yo'qolardi), qolganlari 12 soat kutardi.
    /// <c>Count</c>/<c>Total</c>/<c>Created</c> — faqat HAQIQATAN saqlanganlar.</para></summary>
    /// <param name="log">Ixtiyoriy — o'tkazib yuborilgan o'quvchi sababini yozish uchun (fon xizmati beradi).</param>
    public static async Task<(int Count, decimal Total, List<(string StudentId, decimal Amount)> Created)> AccrueMonth(
        IAppDbContext db, string month, ILogger? log = null)
    {
        var created = new List<(string StudentId, decimal Amount)>();
        // Faqat O'QILADI (AsNoTracking) — har o'quvchi alohida saqlangani uchun ChangeTracker kichik qolsin.
        var classList = await db.Classes.AsNoTracking().ToListAsync();
        var groupsById = classList.ToDictionary(c => c.Id);
        // SINFGA QARAB NARX — barcha kurslar oraliqlari BIR MARTA (`TuitionPricing`).
        var pricing = await PricingBook.LoadAsync(db);
        var feesByName = classList.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().MonthlyFee);
        // Barcha a'zoliklar (holat bilan). Oylik to'lov faqat FAOL (active) a'zolikka — aktivlashtirilgan
        // oydan KEYINGI oylar uchun (aktiv oyi qisman to'lov bilan alohida yozilgan) va muzlatish oyidan OLDIN.
        var membershipsByStudent = (await db.StudentGroups.AsNoTracking().ToListAsync())
            .GroupBy(sg => sg.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Idempotentlik PER-GURUH: (o'quvchi, guruh) juftligi shu oyda allaqachon hisoblangan bo'lsa o'tkazamiz.
        var already = (await db.MonthlyCharges.Where(c => c.Month == month)
                .Select(c => new { c.StudentId, c.GroupId }).ToListAsync())
            .Select(x => (x.StudentId, x.GroupId)).ToHashSet();
        // Arxivlangan o'quvchilarga oylik hisoblanmaydi. ⚠️ Ro'yxat faqat REJA uchun (AsNoTracking): balans
        // quyida har o'quvchi QULFI ostida bazadan QAYTA o'qiladi — fon xizmati ro'yxatni bir marta yuklab,
        // oylar bo'yicha aylanib, oxirida ESKI balans ustidan yozardi va shu orada kelgan to'lov ta'siri
        // jimgina yo'qolardi (`PaymentIntake` dagi "balans qulf ichida qayta o'qiladi" naqshi).
        var students = await db.Students.AsNoTracking().Where(s => !s.IsArchived).ToListAsync();
        // ⚠️ CHEGIRMA KITOBI — BIR MARTA, BARCHA o'quvchilar uchun. Ikki sabab:
        //  (1) N+1 bo'lmasin (bu yerda minglab o'quvchi × a'zolik aylanadi);
        //  (2) `AccrueDue` har startupda va har 12 soatda BUTUN TARIXNI qayta skanerlaydi —
        //      kitob bo'sh bo'lsa chegirma 0 bo'lib, ommaviy YOLG'ON QARZ yozilardi va
        //      ota-onalarga avto-SMS to'lqini ketardi (`.claude/rules/membership-periods.md` §6).
        var book = await DiscountBook.LoadAllAsync(db);

        var count = 0;
        decimal total = 0;
        foreach (var s in students)
        {
            // 1) REJA (sof): shu o'quvchiga shu oy qaysi (guruh, narx) hisoblari yoziladi.
            var plan = new List<(string? GroupId, decimal Fee)>();
            if (membershipsByStudent.TryGetValue(s.Id, out var mships) && mships.Count > 0)
            {
                // DIQQAT: bu yerda EnrollmentDate bo'yicha filtr QO'YILMAYDI. A'zoligi bor o'quvchida haqiqat
                // manbai — m.ActivatedAt (quyida tekshiriladi), EnrollmentDate emas. Aks holda ORQAGA SANALGAN
                // aktivlashtirishda (o'quvchi bugun qo'shilib, guruhda fevraldan aktivlashtirilsa) EnrollmentDate
                // = "2026-07" bo'lgani uchun mart..iyun oylari "kelmagan" deb tashlab yuborilardi va u oylar
                // HECH QACHON hisoblanmasdi (fon xizmati keyin ishlaganda ham).
                // Har FAOL a'zolik uchun alohida hisob qatori.
                foreach (var m in mships)
                {
                    // Guruhdan chiqarilgan (IsActive=false) a'zolik Status="active" bo'lib qolishi mumkin —
                    // shuning uchun IsActive ham talab qilinadi (aks holda chiqib ketgan o'quvchi har oy hisoblanardi).
                    if (m.Status != "active" || !m.IsActive) continue;
                    if (!AccruableMonth(m, month)) continue;
                    if (already.Contains((s.Id, (string?)m.GroupId))) continue;
                    var gfee = groupsById.TryGetValue(m.GroupId, out var grp) ? pricing.MonthlyFee(grp, s, month) : 0m;
                    if (gfee <= 0) continue;
                    plan.Add((m.GroupId, gfee));
                }
            }
            else
            {
                // Guruhsiz (eski ClassName) — GroupId=null, narx ClassName→guruh nomi orqali.
                // Bu shoxobchada a'zolik (ActivatedAt) yo'q, shuning uchun boshlanish nuqtasi — EnrollmentDate:
                // o'quvchi shu oydan KEYIN kelgan bo'lsa, hisoblamaymiz. (.Length >= 7 — noto'g'ri/qisqa
                // EnrollmentDate qatorida [..7] crash bo'lmasligi uchun; boshqa call-site'lar bilan bir xil himoya.)
                if (s.EnrollmentDate.Length >= 7 && string.CompareOrdinal(s.EnrollmentDate[..7], month) > 0) continue;
                if (already.Contains((s.Id, (string?)null))) continue;
                var nfee = feesByName.TryGetValue(s.ClassName, out var nf) ? nf : 0m;
                if (nfee <= 0) continue;
                plan.Add((null, nfee));
            }
            if (plan.Count == 0) continue;

            // 2) YOZISH — o'quvchi QULFI ostida (to'lov/vozvrat/bekor qilish bilan BIR navbatda), balans
            // bazadan QAYTA o'qiladi va har o'quvchi ALOHIDA saqlanadi: oylar bo'yicha uzoq aylanish
            // davomida kelgan to'lov ta'siri eski balans bilan ustidan yozilmasin.
            using (await PaymentIdempotency.LockAsync("student:" + s.Id))
            {
                // ⚠️ QULF ICHIDA QAYTA TEKSHIRUV: `already` butun oy uchun QULFDAN TASHQARIDA bir marta
                // o'qilgan. Shu orada to'lov (`EnsureChargeAsync`), aktivlashtirish (`AccrueCatchUpAsync`) va
                // h.k. AYNI (o'quvchi, guruh, oy) hisobini yozgan bo'lishi mumkin — usiz ikkinchi qator unikal
                // indeksga urilardi.
                var nowCharged = (await db.MonthlyCharges.AsNoTracking()
                        .Where(c => c.StudentId == s.Id && c.Month == month)
                        .Select(c => c.GroupId).ToListAsync())
                    .ToHashSet();
                var todo = plan.Where(p => !nowCharged.Contains(p.GroupId)).ToList();
                if (todo.Count == 0) continue;

                var tracked = await db.Students.FirstOrDefaultAsync(x => x.Id == s.Id);
                if (tracked is null) continue;
                await StudentBalanceSync.ReloadAsync(db, tracked);
                var rows = todo.Select(p => AccrueOne(db, tracked, book.For(s.Id), p.GroupId, month, p.Fee)).ToList();
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    // Shu o'quvchining saqlanmagan yozuvlari kontekstdan AJRATILADI — aks holda keyingi
                    // o'quvchining SaveChanges'i ularni qayta yubormoqchi bo'lib yana yiqilardi.
                    foreach (var r in rows) db.MonthlyCharges.Entry(r).State = EntityState.Detached;
                    db.Students.Entry(tracked).State = EntityState.Detached;
                    log?.LogWarning(ex, "Oylik hisob {Month}: o'quvchi {StudentId} o'tkazib yuborildi (keyingi yugurishda qayta urinadi)",
                        month, s.Id);
                    continue;
                }
                foreach (var r in rows)
                {
                    var eff = r.Amount - r.Discount;
                    total += eff;
                    count++;
                    created.Add((s.Id, eff));
                }
            }
        }

        return (count, total, created);
    }

    /// <summary>Bitta (o'quvchi, guruh, oy) hisob qatorini yozadi va balansni effektiv miqdorda kamaytiradi.
    /// Amount = to'liq narx; Discount = chegirma; effektiv = Amount − Discount.
    /// Effektiv 0 (100% chegirma) bo'lsa ham qator qoldiriladi — hisobotda ko'rinsin. SaveChanges — chaqiruvchida.
    /// Qaytaradi: qo'shilgan qator (effektiv = Amount − Discount).</summary>
    private static MonthlyCharge AccrueOne(
        IAppDbContext db, Student s, IReadOnlyList<StudentDiscount> discounts,
        string? groupId, string month, decimal fee)
    {
        var discount = DiscountForMonth(discounts, fee, month, groupId);
        var effective = fee - discount;
        var row = new MonthlyCharge
        {
            StudentId = s.Id, GroupId = groupId, Month = month, Amount = fee, Discount = discount, Date = $"{month}-01",
        };
        db.MonthlyCharges.Add(row);
        s.Balance -= effective;
        return row;
    }

    /// <summary>Shu a'zolik uchun <paramref name="month"/> oyiga TO'LIQ oylik hisob yozilishi kerakmi.
    ///
    /// <para>JORIY davr: aktivlashtirilgan oydan KEYINGI oydan muzlatish oyidan OLDINGI oygacha.
    /// Chegaralar ATAYIN kirmaydi — aktivlashtirish va muzlatish oylari QISMAN hisob bilan o'sha
    /// paytning o'zida yoziladi (<see cref="ChargeActivationProrateAsync"/> /
    /// <see cref="ChargeFreezeProrateAsync"/>); ularni bu yerda yozsak IKKI marta hisoblanardi.</para>
    ///
    /// <para>YOPILGAN DAVRLAR (<see cref="Domain.StudentGroup.PastPeriods"/>) — xuddi shu qoida bilan.
    /// Ular bo'lmasa muzlatib qayta aktivlashtirilgan a'zolikda <c>ActivatedAt</c> yangi sanaga
    /// o'tgani uchun eski davrdagi TUSHIB QOLGAN oy hech qachon yozilmasdi.</para>
    ///
    /// <para>⚠️ Bu ORTGA QARAB HISOB YOZISHNI xavfsiz qiladigan uchta narsa:
    /// (1) mavjud qatorlarda <c>PastPeriods</c> BO'SH — backfill yo'q, ya'ni deploy paytida bironta ham
    /// yangi hisob yozilmaydi; (2) davr MUZLATISH sanasida yopilgani uchun orqaga sanalgan muzlatishda
    /// <see cref="PurgeChargesAfterMonthAsync"/> o'chirgan oylar davrdan TASHQARIDA qoladi va qayta
    /// tirilmaydi; (3) chaqiruvchi <c>Status=="active" &amp;&amp; IsActive</c> ni talab qiladi — guruhdan
    /// chiqib ketgan o'quvchiga retroaktiv qarz yozilmaydi.</para></summary>
    public static bool AccruableMonth(Domain.StudentGroup m, string month)
    {
        var actOk = m.ActivatedAt.Length >= 7 && string.CompareOrdinal(month, m.ActivatedAt[..7]) > 0;
        var frzOk = m.FrozenAt.Length < 7 || string.CompareOrdinal(month, m.FrozenAt[..7]) < 0;
        if (actOk && frzOk) return true;
        foreach (var p in m.PastPeriods)
            if (MembershipLifecycle.AccruableInPeriod(p, month)) return true;
        return false;
    }

    /// <summary>
    /// ORQAGA SANALGAN aktivlashtirishdan keyin ORALIQ oylarni darhol hisoblaydi: aktivlashtirilgan
    /// oydan KEYINGI oydan joriy oygacha har oy uchun TO'LIQ oylik. Aktivlashtirilgan oyning O'ZI bu
    /// yerda tegilmaydi — u qisman (dars soni bo'yicha) hisob bilan
    /// <see cref="ChargeActivationProrateAsync"/> da yoziladi.
    /// <para>NEGA KERAK: oraliq oylarni odatda faqat fon xizmati (<c>TuitionAccrualService</c> →
    /// <see cref="AccrueDue"/> → <see cref="AccrueMonth"/>) yozadi, u esa startupda va har 12 SOATDA
    /// ishlaydi. Usiz admin fevraldan aktivlashtirgach mart..joriy oy hisoblarini 12 soatgacha
    /// ko'rmaydi — o'quvchi qarzi kam bo'lib turadi.</para>
    /// <para>IDEMPOTENT: mavjud (o'quvchi, guruh, oy) hisobiga TEGILMAYDI — shu bilan qo'lda
    /// tahrirlangan (<c>Locked</c>) qatorlar ham himoyalanadi. KELAJAK oy yozilmaydi. Muzlatilgandan
    /// keyin qayta aktivlashtirishda ham xavfsiz: yopilgan davrlardagi bo'shliqlar ham faqat
    /// hisobi YO'Q oylarga yoziladi (<paramref name="membership"/>).</para>
    /// SaveChanges QILINMAYDI — chaqiruvchi saqlaydi.
    /// </summary>
    /// <returns>Nechta oy uchun yangi hisob yaratildi.</returns>
    /// <param name="membership">Aktivlashtirilayotgan a'zolik — berilsa uning YOPILGAN faol davrlaridagi
    /// (<see cref="Domain.StudentGroup.PastPeriods"/>) tushib qolgan oylar ham SHU YERDA to'ldiriladi.
    /// <para>Usiz ham ular yo'qolmaydi — <see cref="AccrueMonth"/> endi davrlarni biladi va fon xizmati
    /// 12 soat ichida yozadi; parametr faqat KECHIKISHNI yo'q qiladi (aynan shu sabab bilan bu metod
    /// umuman paydo bo'lgan). <c>null</c> bo'lsa eski xatti-harakat — hech narsa o'zgarmaydi.</para></param>
    public static async Task<int> AccrueCatchUpAsync(IAppDbContext db, Student s, Group cls, string activatedAtIso,
                                                     Domain.StudentGroup? membership = null)
    {
        // ⚠️ Narx endi OY BO'YICHA tanlanadi (sinf 1-sentabrda oshadi) — "narx 0" tekshiruvi halqada.
        if (string.IsNullOrEmpty(activatedAtIso) || activatedAtIso.Length < 7) return 0;
        var from = NextMonth(activatedAtIso[..7]);
        var cur = CurrentMonth();
        // ODATDAGI HOLAT (joriy oydan aktivlashtirish, tarix yo'q) — qiladigan ish yo'q, pastdagi
        // MonthlyCharges so'rovi ham qilinmaydi. Yopilgan davrlar BOR bo'lsa esa "from > cur" da ham
        // davom etamiz: ulardagi bo'shliqlar joriy oydan OLDIN va ular to'ldirilishi kerak.

        if (string.CompareOrdinal(from, cur) > 0 && (membership?.PastPeriods.Count ?? 0) == 0) return 0;

        // Shu (o'quvchi, guruh) uchun allaqachon mavjud oylar — ular ustidan yozilmaydi.
        var existing = (await db.MonthlyCharges
                .Where(c => c.StudentId == s.Id && c.GroupId == cls.Id)
                .Select(c => c.Month).ToListAsync())
            .Where(m => m.Length >= 7).Select(m => m[..7]).ToHashSet();

        // Yoziladigan oylar: (1) ORQAGA SANALGAN aktivlashtirish oralig'i — aktivlashtirilgan oydan
        // KEYINGI oydan joriy oygacha; (2) YOPILGAN faol davrlardagi tushib qolgan oylar (muzlatib,
        // keyin qayta aktivlashtirilgan a'zolik). Ikkalasi bir ro'yxatga birlashtirilib TARTIBLANADI.
        var months = new SortedSet<string>(StringComparer.Ordinal);
        if (string.CompareOrdinal(from, cur) <= 0)
            foreach (var m in MonthRange(from, cur)) months.Add(m);
        if (membership is not null)
            foreach (var period in membership.PastPeriods)
            {
                if (!MembershipLifecycle.TryParsePeriod(period, out var pFrom, out var pTo)) continue;
                if (pTo.Length < 7) continue;
                foreach (var m in MonthRange(pFrom[..7], pTo[..7]))
                    if (MembershipLifecycle.AccruableInPeriod(period, m)) months.Add(m);
            }

        // Chegirma qatorlari — BIR MARTA (halqa ichida emas): oralig'i bir necha oy bo'lishi mumkin.
        var discounts = (await DiscountBook.LoadForStudentAsync(db, s.Id)).For(s.Id);
        var pricing = await PricingBook.LoadForCourseAsync(db, cls.CourseId);

        var created = 0;
        foreach (var month in months)
        {
            if (string.CompareOrdinal(month, cur) > 0) continue; // KELAJAK oy hech qachon yozilmaydi
            if (existing.Contains(month)) continue;
            var monthFee = pricing.MonthlyFee(cls, s, month);
            if (monthFee <= 0) continue;
            // Eski aggregate (GroupId=null) qator bo'lsa — dublikat bo'lmasin.
            await PurgeAggregateRowAsync(db, s, month);
            AccrueOne(db, s, discounts, cls.Id, month, monthFee); // to'liq oylik (sinf oralig'i bo'yicha) + chegirma
            created++;
        }
        return created;
    }

    /// <summary>AVANS uchun: berilgan (o'quvchi, guruh, oy) hisobi mavjudligini ta'minlaydi — yo'q bo'lsa
    /// to'liq oylik narxda yaratadi (balans effektiv miqdorda kamayadi). Kassir kelajak oyga to'lasa, o'sha
    /// oy hisobi shu zahoti ochiladi. Mavjud bo'lsa tegmaydi (idempotent). null narx/guruh bo'lsa — hech narsa.
    /// SaveChanges — chaqiruvchida. Qaytaradi: yangi hisob yaratildimi.
    /// <para>⚠️ <b>FAQAT accrual YOZADIGAN oyga</b> (2026-10-07): hisob <see cref="AccrueMonth"/> bilan AYNAN
    /// bir xil shartda ochiladi — a'zolik <c>active</c> + <c>IsActive</c> va oy <see cref="AccruableMonth"/>
    /// (aktivlashtirish oyidan KEYIN, muzlatish oyidan OLDIN). Ilgari a'zolik holatiga qaralmasdi: SINOVDAGI
    /// o'quvchiga to'lov guruhsiz TO'LIQ hisob ochardi (aktivlashtirish uni hech qachon o'chirmasdi), kech
    /// aktivlashtirilgan oyga (qisman hisobi yo'q) esa to'liq oylik yozilardi. Shart bajarilmasa pul shunchaki
    /// AVANS bo'lib qoladi (balansda) — aktivlashtirilganda/accrual'da odatdagidek hisobga o'tadi.</para>
    /// <para>Guruhsiz (<paramref name="groupId"/> = null) hisob faqat a'zoligi UMUMAN yo'q eski (ClassName)
    /// o'quvchiga va qabul oyidan boshlab — <see cref="AccrueMonth"/> ning guruhsiz shoxobchasi bilan bir xil.</para>
    /// <para>⚠️ <b>AKTIVLASHTIRISH OYI, hisobi YO'Q</b> (2026-10-08, audit R1-5): o'quvchi yaratish/importda
    /// "active" tanlangan a'zolik (<c>StudentsController.AddMembershipRow</c>) aktivlashtirish oyiga ATAYIN
    /// hisob yozmaydi (`membership-periods.md` §9), accrual esa bu oyni olmaydi (<see cref="AccruableMonth"/>).
    /// Shu oyga to'lov kelsa hisob ochilmay, pul AVANS bo'lib keyingi oy hisobini yopardi — ya'ni bitta oy
    /// TEKIN qolardi. Endi to'lov kelganda o'sha oyga <see cref="ChargeActivationProrateAsync"/> yozadigan
    /// QISMAN hisob (ActivatedAt sanasidan, chegirma bilan) ochiladi. Qisman summa 0 bo'lsa (oyning oxirgi
    /// darsidan keyin aktivlashtirilgan) — hisob ochilmaydi, pul avans bo'lib qoladi.</para></summary>
    public static async Task<bool> EnsureChargeAsync(IAppDbContext db, Student s, string? groupId, string month)
    {
        if (!PaymentFields.IsMonth(month)) return false;
        if (s.IsArchived) return false; // accrual arxivdagilarga hisob yozmaydi
        var existing = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == month);
        if (existing is not null) return false;

        decimal fee;
        if (groupId is null)
        {
            if (await db.StudentGroups.AnyAsync(sg => sg.StudentId == s.Id)) return false;
            if (s.EnrollmentDate.Length >= 7 && string.CompareOrdinal(s.EnrollmentDate[..7], month) > 0) return false;
            fee = (await db.Classes.FirstOrDefaultAsync(c => c.Name == s.ClassName))?.MonthlyFee ?? 0m;
        }
        else
        {
            var memberships = await db.StudentGroups
                .Where(sg => sg.StudentId == s.Id && sg.GroupId == groupId).ToListAsync();
            if (!memberships.Any(m => m.Status == "active" && m.IsActive && AccruableMonth(m, month)))
            {
                // Aktivlashtirish oyi (hisobi yo'q) — QISMAN hisob (yuqoridagi izoh). Boshqa holat — avans.
                var act = ActivationMonthMembership(memberships, month);
                if (act is null) return false;
                var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == groupId);
                if (cls is null) return false;
                await ChargeActivationProrateAsync(db, s, cls, act.ActivatedAt[..10]);
                // Bazada shu kalitli qator yo'q edi (yuqorida tekshirildi) — lokal qator bo'lsa, u hozir yozildi.
                return db.MonthlyCharges.Local.Any(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == month);
            }
            // Aktivlashtirish oyi hisobsiz qolgan bo'lsa (yaratish/importda `active` — qisman oylik ATAYIN
            // yozilmaydi, `membership-periods.md` §9) va oila KEYINGI oyni to'layotgan bo'lsa, aktivlashtirish
            // oyi ham qisman hisoblanadi — aks holda u hech qachon hisoblanmay, birinchi oy tekin qolardi.
            await EnsureRecentActivationChargeAsync(db, s, groupId, memberships, month);
            var g = await db.Classes.Where(c => c.Id == groupId)
                .Select(c => new { c.CourseId, c.MonthlyFee }).FirstOrDefaultAsync();
            fee = g is null ? 0m
                : (await PricingBook.LoadForCourseAsync(db, g.CourseId)).Resolve(g.CourseId, g.MonthlyFee, s, month).Monthly;
        }
        if (fee <= 0) return false;

        var discounts = (await DiscountBook.LoadForStudentAsync(db, s.Id)).For(s.Id);
        AccrueOne(db, s, discounts, groupId, month, fee);
        return true;
    }

    /// <summary>
    /// To'lov KEYINGI oy uchun kelganda, hisobsiz qolgan AKTIVLASHTIRISH oyini qisman hisob bilan ochadi
    /// (<see cref="ChargeActivationProrateAsync"/> — qolgan darslar bo'yicha). Faqat YAQIN oy (joriy yoki
    /// o'tgan): eski tarixga (admin o'chirgan yoki ataylab yozilmagan oylarga) orqaga hisob yozilmasin.
    /// Shu oyga qator bo'lsa (0 so'mlik tahrir ham) — tegilmaydi.
    /// </summary>
    private static async Task EnsureRecentActivationChargeAsync(IAppDbContext db, Student s, string groupId,
        List<Domain.StudentGroup> memberships, string paidMonth)
    {
        var floor = AppClock.Today.AddMonths(-1).ToString("yyyy-MM");
        foreach (var m in memberships)
        {
            if (m.ActivatedAt.Length < 10) continue;
            var actMonth = m.ActivatedAt[..7];
            if (string.CompareOrdinal(actMonth, paidMonth) >= 0 || string.CompareOrdinal(actMonth, floor) < 0) continue;
            if (ActivationMonthMembership(new[] { m }, actMonth) is null) continue;
            var has = db.MonthlyCharges.Local.Any(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == actMonth)
                      || await db.MonthlyCharges.AnyAsync(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == actMonth);
            if (has) continue;
            var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == groupId);
            if (cls is null) return;
            await ChargeActivationProrateAsync(db, s, cls, m.ActivatedAt[..10]);
        }
    }

    /// <summary>Shu oy aynan JORIY faol davrning AKTIVLASHTIRISH oyi bo'lgan a'zolik (faol, tirik, sana haqiqiy,
    /// shu oyda muzlatilmagan) — <see cref="EnsureChargeAsync"/> ning qisman hisob shoxobchasi uchun. Sof.</summary>
    public static Domain.StudentGroup? ActivationMonthMembership(IEnumerable<Domain.StudentGroup> memberships, string month) =>
        memberships.FirstOrDefault(m => m.Status == "active" && m.IsActive
            && m.ActivatedAt.Length >= 10 && m.ActivatedAt[..7] == month
            && DateOnly.TryParse(m.ActivatedAt[..10], out _)
            && (m.FrozenAt.Length < 7 || string.CompareOrdinal(month, m.FrozenAt[..7]) < 0));

    /// <summary>
    /// <see cref="EnsureChargeAsync"/> AKTIVLASHTIRISH OYIDA (hisobi yo'q) qancha EFFEKTIV hisob yozishini
    /// oldindan aytadi — hech narsa yozmasdan. <c>null</c> — bu shoxobcha qo'llanmaydi (to'liq oylik yoki
    /// umuman hisob yo'q holati — chaqiruvchi o'z hisobini qiladi); <c>0</c> — hisob ochilmaydi (avans).
    /// <para>Nega kerak: kelmagan darslar chegirmasi (`LessonDiscountService.QuoteAsync`) hisobi hali yo'q
    /// oyda "qancha to'lanmagan" ni shu summadan oladi — to'liq oylikdan olsa, qisman oyga undan KATTA
    /// chegirma yozilib, balansga hech qachon yechilmagan pul qo'shilardi.</para>
    /// </summary>
    public static async Task<decimal?> ActivationChargePreviewAsync(IAppDbContext db, Student s, Group cls, string month)
    {
        if (!PaymentFields.IsMonth(month) || s.IsArchived) return null;
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.StudentId == s.Id && sg.GroupId == cls.Id).ToListAsync();
        if (memberships.Any(m => m.Status == "active" && m.IsActive && AccruableMonth(m, month))) return null;
        var act = ActivationMonthMembership(memberships, month);
        if (act is null) return null;
        var (_, gross) = await ActivationGrossAsync(db, s, cls, act.ActivatedAt[..10], null, null);
        if (gross <= 0) return 0m;
        var book = await DiscountBook.LoadForStudentAsync(db, s.Id);
        return Math.Max(0m, gross - book.DiscountFor(s.Id, gross, month, cls.Id));
    }

    /// <summary>
    /// AVANS HISOBINI QAYTARISH — <see cref="EnsureChargeAsync"/> ning teskarisi. KELAJAK oy (joriy oydan
    /// keyingi) hisobini faqat avans to'lov ochadi (<see cref="AccrueMonth"/> kelajakni yozmaydi), ya'ni
    /// o'sha oyga TIRIK to'lov qolmasa (bekor qilindi, to'liq qaytarildi yoki boshqa oy/guruhga ko'chirildi)
    /// hisob ham o'chiriladi va effektiv summa balansga QAYTADI. Usiz o'quvchi hali boshlanmagan oy uchun
    /// "qarzdor" bo'lib, ota-onaga to'lov eslatmasi SMS'i ketardi. Oy kelganda (a'zolik hamon faol bo'lsa)
    /// accrual uni odatdagidek yozadi — balans izchil qoladi.
    /// <para>Tegilmaydi: joriy/o'tgan oylar (ular haqiqiy o'quv oyi), <c>Locked</c> qator (qo'lda tahrir yoki
    /// kelmagan darslar chegirmasi — ataylab qo'yilgan), shu (guruh, oy) ga boshqa to'lov qolgan hisob.</para>
    /// SaveChanges — chaqiruvchida. Qaytaradi: hisob o'chirildimi.
    /// </summary>
    /// <param name="ignoreTxIds">Hali saqlanmagan o'zgarish: bekor qilinayotgan / boshqa oyga ko'chirilayotgan
    /// tranzaksiyalar — bazada ular hamon shu oyda turibdi, shuning uchun sanalmaydi.</param>
    /// <param name="pendingNet">Hali saqlanmagan qo'shimcha harakat (masalan shu so'rovdagi vozvrat: −summa).</param>
    public static async Task<bool> DropOrphanAdvanceChargeAsync(IAppDbContext db, Student s, string? groupId, string? month,
                                                               IReadOnlyCollection<string>? ignoreTxIds = null, decimal pendingNet = 0m)
    {
        if (!PaymentFields.IsMonth(month) || string.CompareOrdinal(month, CurrentMonth()) <= 0) return false;
        var charge = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == groupId && c.Month == month);
        if (charge is null || charge.Locked) return false;

        var rows = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.StudentId == s.Id && t.GroupId == groupId && t.Month == month
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund")))
            .Select(t => new { t.Id, t.Direction, t.Amount })
            .ToListAsync();
        var net = pendingNet + rows
            .Where(t => ignoreTxIds is null || !ignoreTxIds.Contains(t.Id))
            .Sum(t => t.Direction == "expense" ? -t.Amount : t.Amount);
        if (net > 0m) return false;

        s.Balance += Math.Max(0m, charge.Amount - charge.Discount);
        db.MonthlyCharges.Remove(charge);
        return true;
    }

    /// <summary>
    /// MUZLATISHDAN KEYINGI oylarning hisobini BEKOR qiladi (guruh yopilganda / orqaga sanalgan muzlatishda).
    /// Muzlatish sanasi o'tgan oyga qo'yilsa, oradagi oylar uchun <see cref="AccrueMonth"/> allaqachon to'liq
    /// oylik yozib qo'ygan bo'lishi mumkin — qarz muzlatish sanasidan keyin ham o'sib ketardi. Bu metod shu
    /// (o'quvchi, guruh) juftligining <paramref name="month"/> dan KEYINGI oy hisoblarini o'chiradi va
    /// effektiv summani balansga QAYTARADI. <c>Locked</c> (qo'lda tahrirlangan) qatorlar tegilmaydi.
    /// <paramref name="inclusive"/>=true bo'lsa <paramref name="month"/> ning O'ZI ham o'chiriladi (a'zolik
    /// muzlatish sanasidan KEYIN aktivlashtirilgan bo'lsa — o'sha oyda umuman hisob bo'lmasligi kerak).
    /// SaveChanges — chaqiruvchida. Qaytaradi: balansga qaytarilgan summa va O'CHIRILGAN OYLAR.
    /// <para><paramref name="Months"/> nega kerak: <see cref="CarryGroupAdvanceAsync"/> shu chaqiruvdan
    /// KEYIN, lekin SaveChanges'dan OLDIN ishlaydi. U hisoblarni bazadan qayta o'qiydi va o'chirilgan
    /// (hali flush qilinmagan) qatorlarni EF baribir qaytaradi — natijada o'sha oylar "hisoblangan"
    /// bo'lib ko'rinib, avans ko'chmay qolardi. Shuning uchun o'chirilgan oylar ro'yxati unga
    /// <c>zeroOwedMonths</c> sifatida uzatiladi.</para>
    /// </summary>
    public static async Task<(decimal Restored, List<string> Months)> PurgeChargesAfterMonthAsync(
        IAppDbContext db, Student s, string groupId, string month, bool inclusive = false)
    {
        if (month.Length < 7) return (0m, []);
        var m0 = month[..7];
        var candidates = (await db.MonthlyCharges
                .Where(c => c.StudentId == s.Id && c.GroupId == groupId)
                .ToListAsync())
            .Where(c => c.Month.Length >= 7
                        && (inclusive
                            ? string.CompareOrdinal(c.Month[..7], m0) >= 0
                            : string.CompareOrdinal(c.Month[..7], m0) > 0))
            .ToList();
        if (candidates.Count == 0) return (0m, []);

        // ⚠️ QULFLI qator: qo'lda tahrirlangani tegilmaydi; qulfi FAQAT kelmagan darslar chegirmasidan bo'lsa
        // (`LessonDiscountService.LockExtra`) — u ham BEKOR qilinadi va shu oyning chegirmalari yopiladi.
        // Ilgari bunday kelgusi oy hisobi muzlatish/chiqishdan keyin ham qarz bo'lib qolardi.
        var lessonDiscounts = candidates.Any(c => c.Locked)
            ? await db.LessonDiscounts.Where(x => x.StudentId == s.Id && x.GroupId == groupId).ToListAsync()
            : [];
        var rows = candidates
            .Where(c => !c.Locked || LessonDiscountService.LockExtra(
                lessonDiscounts.Where(x => x.Month == c.Month).ToList()) is not null)
            .ToList();

        var restored = 0m;
        var months = new List<string>();
        foreach (var row in rows)
        {
            var effective = Math.Max(0m, row.Amount - row.Discount);
            s.Balance += effective;   // yaratilganda yechilgan effektivni qaytaramiz
            restored += effective;
            months.Add(row.Month[..7]);
            if (row.Locked)
                LessonDiscountService.CloseForDroppedCharge(
                    lessonDiscounts.Where(x => x.Month == row.Month), "Tizim: oy hisobi bekor qilindi (muzlatish/chiqish)");
            db.MonthlyCharges.Remove(row);
        }
        return (restored, months);
    }

    /// <summary>
    /// GURUH ALMASHTIRISHDA AVANSNI KO'CHIRISH. Muammo: o'quvchi oy boshida ESKI guruhga to'lab, keyin
    /// yangi guruhga o'tkazilsa — muzlatish qisman hisobi eski guruh hisobini (masalan oy boshidan
    /// muzlatilsa) deyarli nolga tushiradi, lekin PUL o'sha eski guruhga TEGLANGAN bo'lib qoladi.
    /// Natijada per-guruh balansda: eski guruh AVANS (yashil), yangi guruh esa to'liq QARZ (qizil) —
    /// o'quvchi to'lagan bo'lsa ham yangi o'qituvchida "qarzdor" ko'rinadi.
    /// <para>Bu metod eski guruhda ORTIB QOLGAN summani (to'langan − hisoblangan, oy bo'yicha) yangi
    /// guruhga qayta teglaydi: to'lov to'liq ortiqcha bo'lsa <c>GroupId</c> almashtiriladi, qisman
    /// bo'lsa tranzaksiya ikkiga bo'linadi (asl yozuv kamayadi + yangi guruhga yangi yozuv). Umumiy
    /// pul miqdori, o'quvchi balansi va kassa hisobotlari O'ZGARMAYDI — faqat guruh tegi o'zgaradi
    /// (shu sabab o'qituvchining foizli maoshi ham to'g'ri guruhga o'tadi).</para>
    /// <para>Faqat <paramref name="fromMonth"/> (muzlatish oyi) va undan KEYINGI oylar ko'chiriladi —
    /// o'tgan oylardagi avans eski guruh tarixida qoladi. Vozvrat qilingan summa (expense+refund)
    /// ortiqchadan ayriladi. SaveChanges — chaqiruvchida. Qaytaradi: ko'chirilgan jami summa.</para>
    /// </summary>
    /// <param name="zeroOwedMonths">
    /// Hisobi SHU chaqiruvdan oldin bekor qilingan, lekin hali bazaga yozilmagan oylar
    /// (<see cref="PurgeChargesAfterMonthAsync"/> qaytaradi). EF o'chirishga belgilangan qatorni
    /// so'rovda baribir qaytargani uchun ular "hisoblangan" bo'lib ko'rinadi va avans ko'chmay
    /// qolardi — shuning uchun bu oylar majburan 0 deb olinadi.
    /// </param>
    public static async Task<decimal> CarryGroupAdvanceAsync(
        IAppDbContext db, Student s, Group fromGroup, Group toGroup, string fromMonth,
        IReadOnlyCollection<string>? zeroOwedMonths = null)
    {
        if (fromMonth.Length < 7 || fromGroup.Id == toGroup.Id) return 0m;
        var month0 = fromMonth[..7];

        // Eski guruhda shu oydan boshlab HISOBLANGAN (chegirmadan keyin) summa — oy bo'yicha.
        var owedByMonth = (await db.MonthlyCharges
                .Where(c => c.StudentId == s.Id && c.GroupId == fromGroup.Id)
                .ToListAsync())
            .Where(c => c.Month.Length >= 7 && string.CompareOrdinal(c.Month[..7], month0) >= 0)
            .GroupBy(c => c.Month[..7])
            .ToDictionary(g => g.Key, g => g.Sum(c => Math.Max(0m, c.Amount - c.Discount)));

        // Bekor qilingan (hali flush qilinmagan) oylar — hisobi yo'q deb olinadi.
        if (zeroOwedMonths is not null)
            foreach (var m in zeroOwedMonths)
                owedByMonth[m] = 0m;

        // Eski guruhga teglangan to'lovlar va vozvratlar (shu oydan boshlab).
        var movements = (await db.FinanceTransactions
                .Where(t => t.StudentId == s.Id && t.GroupId == fromGroup.Id && t.Month != null
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .ToListAsync())
            .Where(t => t.Month!.Length >= 7 && string.CompareOrdinal(t.Month![..7], month0) >= 0)
            .ToList();
        if (movements.Count == 0) return 0m;

        var moved = 0m;
        foreach (var byMonth in movements.GroupBy(t => t.Month![..7]))
        {
            var paid = byMonth.Where(t => t.Direction == "income").Sum(t => t.Amount);
            var refunded = byMonth.Where(t => t.Direction == "expense").Sum(t => t.Amount);
            var surplus = paid - refunded - owedByMonth.GetValueOrDefault(byMonth.Key, 0m);
            if (surplus <= 0m) continue;

            // Ortiqchani eng OXIRGI to'lovdan boshlab ko'chiramiz (oxirgi to'lov yangi davr uchun
            // qilingan bo'lish ehtimoli yuqori).
            var mark = $"[guruh almashtirildi: {fromGroup.Name} → {toGroup.Name}]";
            var remaining = surplus;
            foreach (var tx in byMonth.Where(t => t.Direction == "income")
                         .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt))
            {
                if (remaining <= 0m) break;
                var take = Math.Min(remaining, tx.Amount);
                if (take >= tx.Amount)
                {
                    // To'lov to'liq ortiqcha — butunicha yangi guruhga teglanadi.
                    tx.GroupId = toGroup.Id;
                    tx.Note = $"{tx.Note} {mark}".Trim();
                }
                else
                {
                    // Qisman ortiqcha — to'lov ikkiga bo'linadi (asl yozuv eski guruhda kamayadi).
                    // ⚠️ Yangi qator ASL to'lovga BOG'LANADI (`SplitFromId` — oila ildizi) va uning kassir/
                    // kvitansiya/karta/usul/kassa maydonlarini oladi: ilgari bog'lanmagan "yetim" qator bo'lib,
                    // asl to'lov bekor qilinganda faqat bir qismi bekor bo'lar, kassir hisobotida esa bo'lingan
                    // qism "kim kiritgani noma'lum" bo'lib qolardi (`PaymentSplits`).
                    tx.Amount -= take;
                    tx.Note = $"{tx.Note} [{AuditService.Money(take)} so'm {toGroup.Name} guruhiga ko'chirildi]".Trim();
                    db.FinanceTransactions.Add(new FinanceTransaction
                    {
                        Date = tx.Date,
                        Direction = tx.Direction,
                        Category = tx.Category,
                        TypeId = tx.TypeId,
                        Amount = take,
                        StudentId = tx.StudentId,
                        GroupId = toGroup.Id,
                        Month = tx.Month,
                        Method = tx.Method,
                        Comment = tx.Comment,
                        ReceiptNo = tx.ReceiptNo,
                        PaidTime = tx.PaidTime,
                        CardLast4 = tx.CardLast4,
                        CreatedBy = tx.CreatedBy,
                        CreatedById = tx.CreatedById,
                        CashboxId = tx.CashboxId, // pul o'sha kassada turibdi — faqat guruh tegi o'zgaradi
                        SplitFromId = tx.SplitFromId ?? tx.Id,
                        Note = $"O'quvchi to'lovi ({tx.Month}) [{toGroup.Name}] {mark}",
                    });
                }
                remaining -= take;
                moved += take;
            }
        }
        return moved;
    }

    /// <summary>
    /// Hisoblanishi kerak bo'lgan BARCHA oylarni (eng erta o'quvchi kelgan oydan / o'quv yili
    /// boshidan — qaysi biri ertaroq — joriy oygacha) to'ldiradi. Har oy uchun
    /// <see cref="AccrueMonth"/> chaqiriladi: u idempotent (allaqachon hisoblangan o'quvchini
    /// o'tkazib yuboradi) va har o'quvchini faqat o'z EnrollmentDate'idan boshlab hisoblaydi.
    /// Shu sabab: import/seed orqali qo'shilgan, hali hisoblanmagan o'quvchilar ham tutiladi,
    /// va oraliqdagi "tushib qolgan" oylar to'ldiriladi (avvalgi xulq faqat oxirgi oydan
    /// keyingi oylarni qo'shardi — yangi/eski o'quvchilar 0 bo'lib qolardi).
    /// </summary>
    /// <param name="log">Ixtiyoriy — o'tkazib yuborilgan o'quvchilar sababi uchun (<see cref="AccrueMonth"/>).</param>
    public static async Task<(List<string> Months, List<(string StudentId, string Month, decimal Amount)> Created)> AccrueDue(
        IAppDbContext db, ILogger? log = null)
    {
        // Avval eski aggregate (GroupId=null) + per-guruh dublikat hisoblarni tozalaymiz (o'z-o'zini tuzatish).
        await PurgeDuplicateAggregateChargesAsync(db, log);

        var cur = CurrentMonth();
        var start = await AcademicYearStartMonthAsync(db);

        // Faol o'quvchilarning eng erta kelgan oyi (o'quv yili boshidan oldin kelgan bo'lsa,
        // o'sha oydan boshlab). AccrueMonth har o'quvchini o'z enrollment'idan tekshiradi.
        var enrolls = await db.Students
            .Where(s => !s.IsArchived && s.EnrollmentDate != null && s.EnrollmentDate.Length >= 7)
            .Select(s => s.EnrollmentDate).ToListAsync();
        if (enrolls.Count > 0)
        {
            var minEnroll = enrolls.Min()![..7];
            if (string.CompareOrdinal(minEnroll, start) < 0) start = minEnroll;
        }

        if (string.CompareOrdinal(start, cur) > 0) start = cur; // o'quv yili hali boshlanmagan bo'lsa

        var accrued = new List<string>();
        var created = new List<(string StudentId, string Month, decimal Amount)>();
        foreach (var month in MonthRange(start, cur))
        {
            var (count, _, monthCreated) = await AccrueMonth(db, month, log);
            if (count > 0) accrued.Add(month);
            foreach (var (sid, amount) in monthCreated)
                created.Add((sid, month, amount));
        }
        return (accrued, created);
    }

    /// <summary>DUBLIKAT TUZATISH: o'quvchi guruhga qo'shilib per-guruh billingiga o'tganda, u guruhsiz paytda
    /// yozilgan eski ClassName-asosli aggregate (GroupId=null) hisob qatori SHU OY uchun per-guruh qator bilan
    /// BIRGA qolib ketishi mumkin (per-guruh qator yaratilganda null qator o'chirilmaydi). Bu ikki muammo beradi:
    ///  (1) <see cref="StudentLedger"/> oy summasini ikkala qatorni qo'shib IKKI BARAVAR ko'rsatadi;
    ///  (2) <see cref="ChargeFreezeProrateAsync"/> faqat per-guruh qatorni kamaytirgani uchun aggregate qator
    ///      to'liq oy bo'lib qolib, MUZLATILGANDAN keyin ham "o'qilmagan keyingi kunlar"ni hisoblab turadi.
    /// Bu yerda: bir (o'quvchi, oy) uchun HAM null, HAM kamida bitta per-guruh qator bo'lsa — null (aggregate)
    /// qatorni o'chiramiz va uning effektiv summasini balansga QAYTARAMIZ (yaratilganda balans kamaygan edi).
    /// Idempotent + o'z-o'zini tuzatuvchi (har AccrueDue siklida ishlaydi, mavjud prod ma'lumotini ham tozalaydi).</summary>
    /// <summary>Bitta (o'quvchi, oy) uchun aggregate (GroupId=null) hisob qatorini o'chiradi va effektivni
    /// balansga qaytaradi. Per-guruh qator yozishdan oldin chaqiriladi (dublikat oldini olish). SaveChanges — chaqiruvchida.</summary>
    // ==================== QO'LDA TAHRIRLASH (super-admin) ====================

    /// <summary>Oylik hisobni QO'LDA tahrirlashning REJASI — sof (bazasiz) hisob.
    /// <see cref="PlanChargeEdit"/> qaytaradi, <c>StudentsController.EditCharge</c> qo'llaydi.</summary>
    /// <param name="OldAmount">Tahrirdan OLDINGI to'liq narx.</param>
    /// <param name="OldDiscount">Tahrirdan OLDINGI chegirma (balans aynan shunga qarab yechilgan edi).</param>
    /// <param name="NewAmount">Yangi to'liq narx (manfiy bo'lmaydi).</param>
    /// <param name="NewDiscount">Yangi chegirma — yangi summadan oshmasligi uchun qirqilgan bo'lishi mumkin.</param>
    /// <param name="DiscountRecomputed">Chegirma registr spetsifikatsiyasidan (foiz/summa) YANGI
    /// summa bo'yicha QAYTA HISOBLANDIMI (auditda aytiladi).</param>
    /// <param name="DiscountClamped">Chegirma yangi summaga sig'magani uchun KAMAYTIRILDIMI.</param>
    public readonly record struct ChargeEditPlan(
        decimal OldAmount, decimal OldDiscount, decimal NewAmount, decimal NewDiscount,
        bool DiscountRecomputed = false, bool DiscountClamped = false)
    {
        /// <summary>Tahrirdan OLDIN balansdan yechilgan effektiv summa (ESKI chegirma bilan).</summary>
        public decimal OldEffective => Math.Max(0m, OldAmount - OldDiscount);
        /// <summary>Tahrirdan KEYIN yechilishi kerak bo'lgan effektiv summa.</summary>
        public decimal NewEffective => Math.Max(0m, NewAmount - NewDiscount);
        /// <summary>Balansga qo'shiladigan farq (<c>Balance += ...</c>).</summary>
        public decimal BalanceDelta => OldEffective - NewEffective;
        /// <summary>Chegirma umuman o'zgardimi (auditda izoh yozish uchun).</summary>
        public bool DiscountChanged => NewDiscount != OldDiscount;
    }

    /// <summary>
    /// Hisob qatorini qo'lda tahrirlash rejasini tuzadi — <b>chegirmani qirqishdan OLDIN</b> eski
    /// effektiv summani "muzlatib" qo'yish uchun.
    ///
    /// <para>⚠️ <b>NEGA SOF FUNKSIYA:</b> ilgari controller chegirmani AVVAL qirqar
    /// (<c>charge.Discount = newAmount</c>), keyin "eski effektiv"ni ALLAQACHON O'ZGARGAN chegirma
    /// bilan hisoblardi. Misol: Amount=500 000, Discount=200 000 (balansdan 300 000 yechilgan);
    /// admin summani 150 000 ga tushiradi → chegirma 150 000 ga qirqiladi → "eski effektiv"
    /// 500 000 − 150 000 = 350 000 bo'lib chiqadi va balansga 350 000 qaytariladi. Ya'ni o'quvchiga
    /// hech qachon to'lanmagan 50 000 SOVG'A qilinardi. Eski chegirma SNAPSHOT qilinishi shart.</para>
    /// </summary>
    /// <param name="specDiscount">Chegirma registridan (<c>DiscountBook</c>) <b>YANGI summa
    /// bo'yicha</b> qayta hisoblangan chegirma, yoki <c>null</c> — bu oyda/qamrovda amaldagi
    /// chegirma qatori yo'q (tarixiy yoki bekor qilingan).
    ///
    /// <para>⚠️ <b>NEGA KERAK:</b> <c>MonthlyCharge.Discount</c> — birinchi hisoblashda ESKI
    /// summadan chiqarilgan MUTLAQ so'm. Uni ko'chirib qo'ysak, foizli chegirma summa
    /// tahrirlanganda jimgina boshqa foizga aylanardi: 1 000 000 (30% = 300 000) → 600 000 da
    /// chegirma 300 000 bo'lib qolib, aslida <b>50%</b> bo'lardi. Foiz YANGI summadan
    /// hisoblanishi SHART (<c>.claude/rules/discounts.md</c> §2).</para></param>
    public static ChargeEditPlan PlanChargeEdit(
        decimal oldAmount, decimal oldDiscount, decimal requestedAmount, decimal? specDiscount = null)
    {
        var newAmount = Math.Max(0m, requestedAmount);
        // Registr qatori bo'lsa — chegirma YANGI summadan qayta hisoblanadi; bo'lmasa (tarixiy
        // yoki bekor qilingan chegirma) eskisi saqlanadi — jimgina 0 ga tushirib yubormaymiz.
        var recomputed = specDiscount.HasValue;
        var baseDiscount = Math.Max(0m, specDiscount ?? oldDiscount);
        // Chegirma yangi summadan oshib ketmasin (effektiv manfiy bo'lmasin).
        var newDiscount = Math.Min(baseDiscount, newAmount);
        return new ChargeEditPlan(
            oldAmount, oldDiscount, newAmount, newDiscount,
            DiscountRecomputed: recomputed, DiscountClamped: newDiscount < baseDiscount);
    }

    private static async Task PurgeAggregateRowAsync(IAppDbContext db, Student s, string month)
    {
        var nullRow = await db.MonthlyCharges
            .FirstOrDefaultAsync(c => c.StudentId == s.Id && c.GroupId == null && c.Month == month);
        if (nullRow is null) return;
        s.Balance += Math.Max(0m, nullRow.Amount - nullRow.Discount);
        db.MonthlyCharges.Remove(nullRow);
    }

    public static async Task<int> PurgeDuplicateAggregateChargesAsync(IAppDbContext db, ILogger? log = null)
    {
        // AsNoTracking: butun jadval ChangeTracker'ga yuklanmasin — keyingi `AccrueMonth` har o'quvchini
        // alohida saqlaydi va har saqlashda kuzatilayotgan BARCHA qatorlar tekshirilardi. O'chiriladiganlari
        // `Remove` bilan biriktiriladi (EF ajratilgan qatorni Deleted holatida qo'shadi).
        var all = await db.MonthlyCharges.AsNoTracking().ToListAsync();
        var dupNullRows = all
            .GroupBy(c => (c.StudentId, c.Month))
            .Where(g => g.Any(c => c.GroupId == null) && g.Any(c => c.GroupId != null))
            .SelectMany(g => g.Where(c => c.GroupId == null))
            .ToList();
        if (dupNullRows.Count == 0) return 0;

        // Har o'quvchi o'z QULFI ostida, balans bazadan QAYTA o'qilib, alohida saqlanadi (`AccrueMonth` bilan
        // bir xil sabab: fon xizmati eski balans ustidan yozib, parallel to'lov ta'sirini yo'qotmasin).
        var purged = 0;
        foreach (var byStudent in dupNullRows.GroupBy(c => c.StudentId))
        {
            using (await PaymentIdempotency.LockAsync("student:" + byStudent.Key))
            {
                var s = await db.Students.FirstOrDefaultAsync(x => x.Id == byStudent.Key);
                if (s is not null) await StudentBalanceSync.ReloadAsync(db, s);
                var removed = new List<MonthlyCharge>();
                foreach (var row in byStudent)
                {
                    if (s is not null)
                        s.Balance += Math.Max(0m, row.Amount - row.Discount); // yaratilganda yechilgan effektivni qaytaramiz
                    // Shu qator kontekstda allaqachon kuzatilayotgan bo'lsa — o'sha nusxa o'chiriladi.
                    var target = db.MonthlyCharges.Local.FirstOrDefault(c => c.Id == row.Id) ?? row;
                    db.MonthlyCharges.Remove(target);
                    removed.Add(target);
                }
                try
                {
                    await db.SaveChangesAsync();
                    purged += removed.Count;
                }
                catch (DbUpdateException ex)
                {
                    // Qator shu orada boshqa yo'l bilan o'chirilgan bo'lishi mumkin — bitta o'quvchi tufayli
                    // butun oylik hisob (`AccrueDue`) to'xtamasin (`AccrueMonth` dagi bilan bir xil naqsh).
                    foreach (var r in removed) db.MonthlyCharges.Entry(r).State = EntityState.Detached;
                    if (s is not null) db.Students.Entry(s).State = EntityState.Detached;
                    log?.LogWarning(ex, "Dublikat aggregate hisob: o'quvchi {StudentId} o'tkazib yuborildi", byStudent.Key);
                }
            }
        }
        return purged;
    }
}
