using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'QUVCHINI USHLAB TURISH BONUSI — butun mantiqning YAGONA joyi (jadval, sikl holati,
/// taqsimot, bonus berish/bekor qilish). Controller ham, kelajakdagi har qanday chaqiruvchi ham
/// shu orqali o'tadi (<see cref="BookSalesService"/> uslubida static xizmat).
///
/// <para><b>Maqsad:</b> o'quvchini markazda uzoq muddat ushlab turgan o'qituvchi(lar)ni
/// rag'batlantirish. O'quvchi <c>CenterMeta.RetentionMonthsRequired</c> (default 6) oy uzluksiz
/// o'qib to'lasa — uni o'qitgan o'qituvchi(lar)ga bonus ajratiladi, o'qigan oylar NISBATIDA.</para>
///
/// <para><b>SIKL HAR FAN (kurs) UCHUN ALOHIDA.</b> Qator kaliti — <c>(StudentId, CourseId)</c>:
/// o'quvchi Ingliz va Matematikaga qatnasa, har fan uchun mustaqil sanoq, mustaqil davr va
/// mustaqil bonus yuritiladi. Kalit GURUH emas, KURS (<see cref="Group.CourseId"/>; kursi
/// biriktirilmagan eski guruhda — guruh id'si) — chunki o'quvchi bir fan ichida guruh almashtirsa
/// (Ingliz A1 → Ingliz A2) u markazda O'SHA FAN bo'yicha qolgan, sikl UZILMASLIGI kerak.</para>
///
/// <para><b>Bir o'qituvchi — bir o'quvchi — BIR bonus.</b> <c>(TeacherId, StudentId)</c> juftligi
/// umr bo'yi bitta bonus beradi; bekor qilingan bonus ham blokni SAQLAB QOLADI (aks holda bekor
/// qilib qayta berish yo'li ochiq qolardi).</para>
///
/// <para><b>Asosiy tamoyil:</b> bonus <i>ushlab turgani</i> uchun beriladi, <i>o'z vaqtida
/// to'laganligi</i> uchun emas. Shu sabab kechikkan to'lov siklni BUZMAYDI — u faqat tugma
/// chiqishini kechiktiradi.</para>
///
/// <para><b>Hech narsa saqlanmaydi</b> (faqat yakuniy <see cref="RetentionBonusAward"/> va
/// sanoq boshlanish oyi <see cref="RetentionBonusTrack"/>): oylik kataklar har so'rovda qayta
/// hisoblanadi. Superadmin <see cref="MonthlyCharge"/>ni tahrirlashi, to'lov tuzatilishi yoki
/// vozvrat qilinishi mumkin — saqlansa jadval haqiqatdan uzilib qolardi. Sentabr to'lovi yanvarda
/// kelsa, sentabr katagi O'Z-O'ZIDAN ✅ ga aylanadi (maosh — <c>SalaryLedger</c> — ham aynan
/// shunday ishlaydi).</para>
///
/// <para><b>Pul chiqimi EMAS:</b> «Bonus berish» — hisoblash/qayd. Haqiqiy pul odatdagi maosh
/// to'lovi (<c>FinanceTransaction</c> expense/salary) orqali beriladi va bonus <c>SalaryLedger</c>
/// ga ULANMAYDI — Moliya, Kassa va Chiqimlar raqamlari o'zgarmaydi.</para>
/// </summary>
public static class RetentionBonusService
{
    /* ---------- Oy katagi holatlari (DTO'dagi State) ---------- */
    /// <summary>✅ pullik a'zolik bor + hisob yozilgan + qarz yo'q → sanoqqa +1.</summary>
    public const string StatePaid = "paid";
    /// <summary>⏳ pullik a'zolik bor + qarz bor → +0, LEKIN sikl uzilmaydi.</summary>
    public const string StateDebt = "debt";
    /// <summary>
    /// 📄 pullik a'zolik bor, LEKIN shu oy uchun <see cref="MonthlyCharge"/> qatori UMUMAN yo'q.
    /// Sanoqqa KIRMAYDI (ilgari bunday oy "to'langan" bo'lib chiqardi — hisob 0, to'lov 0), lekin
    /// siklni ham UZMAYDI va tanaffus (gap) sanog'iga kirmaydi: o'quvchi shu oyda O'QIGAN, bu
    /// shunchaki hisob yozilmagani. Hisob paydo bo'lgach jadval o'z-o'zidan tuzaladi.
    /// <para>DIQQAT: hisob BOR-u summasi nol bo'lsa (100% chegirma) — bu <see cref="StatePaid"/>,
    /// sanoqqa KIRADI. Farq: qator YO'Qmi yoki qator bor-u summasi nolmi.</para>
    /// </summary>
    public const string StateNoCharge = "nocharge";
    /// <summary>❄️ muzlatilgan (ta'til yoki guruh almashtirish) → pauza.</summary>
    public const string StateFrozen = "frozen";
    /// <summary>🚪 shu fan bo'yicha pullik a'zolik umuman yo'q → pauza.</summary>
    public const string StateGone = "gone";

    /* ---------- Qator holatlari (DTO'dagi Status) ---------- */
    public const string RowNotStarted = "notstarted";
    public const string RowProgress = "progress";
    public const string RowReady = "ready";
    public const string RowBroken = "broken";
    /// <summary>Sanoq to'ldi, LEKIN barcha o'qituvchilar shu o'quvchi orqali allaqachon bonus olgan.</summary>
    public const string RowBlocked = "blocked";

    private static readonly string[] DayShort = ["Du", "Se", "Cho", "Pay", "Ju", "Sha", "Yak"];

    /// <summary>Hisobot uchun kerakli guruh maydonlari (butun <see cref="Group"/> yuklanmaydi).
    /// <paramref name="CourseKey"/> — sikl kaliti: <see cref="Group.CourseId"/>, bo'sh bo'lsa guruh id'si.</summary>
    private sealed record GroupInfo(
        string Id, string Name, decimal MonthlyFee, string TeacherId, List<int> Days,
        string CourseKey, string CourseName);

    /// <summary>Hisob qatori (<see cref="MonthlyCharge"/>) — kerakli maydonlar.</summary>
    private sealed record ChargeRow(string StudentId, string? GroupId, string Month, decimal Amount, decimal Discount);

    /// <summary>To'lov harakati — summa ISHORALI (vozvrat manfiy).</summary>
    private sealed record PayRow(string StudentId, string? GroupId, string Month, decimal Amount);

    /// <summary>Bitta qator (o'quvchi × fan) bo'yicha o'qituvchilar kesimi — qarang
    /// <see cref="TeachersOfRow"/>.</summary>
    private sealed record RowTeachers(Dictionary<string, decimal> Weights, HashSet<string> Participants);

    /// <summary>Bitta (o'quvchi, fan, oy) uchun pul holati: shu KURS guruhlariga tegishli hisob/to'lov
    /// va shu oyda hisob qatori umuman bormi (<see cref="StateNoCharge"/> ni ajratish uchun).</summary>
    private sealed class MoneyCell
    {
        public decimal Charged;
        public decimal Paid;
        public bool HasCharge;
    }

    /* ==================== SOZLAMALAR ==================== */

    /// <summary>
    /// Sozlamalar — himoyalangan o'qish. <c>MonthsRequired == 0</c> "sozlanmagan" degani
    /// (migratsiyagacha yaratilgan qator) va 6 ga tushiriladi: 0 qolsa har bir o'quvchi darhol
    /// "tayyor" bo'lib chiqardi.
    /// </summary>
    public static RetentionSettingsDto Settings(CenterMeta? meta) => new(
        MonthsRequired: (meta?.RetentionMonthsRequired ?? 0) <= 0
            ? 6 : Math.Min(meta!.RetentionMonthsRequired, 36),
        MaxGapMonths: Math.Clamp(meta?.RetentionMaxGapMonths ?? 2, 0, 12),
        DefaultAmount: Math.Max(0m, meta?.RetentionDefaultAmount ?? 0m));

    /* ==================== HISOBOT ==================== */

    /// <summary>
    /// Bonus hisoboti jadvali — ptichkasi yoqilgan BARCHA o'quvchilar uchun (yoki
    /// <paramref name="onlyStudentId"/> berilsa faqat bittasi — bonus berishda qayta tekshirish).
    /// Har o'quvchi uchun u qatnashgan HAR FAN bo'yicha alohida qator qaytadi.
    ///
    /// <para>Barcha ma'lumot OMMAVIY yuklanadi (o'quvchi boshiga alohida so'rov YO'Q — N+1
    /// bo'lmasin); ptichkali o'quvchilar soni kam bo'lgani uchun bu yetarli.</para>
    /// </summary>
    public static async Task<RetentionReportDto> BuildReportAsync(
        IAppDbContext db, string? onlyStudentId = null, CancellationToken ct = default) =>
        (await BuildReportCoreAsync(db, onlyStudentId, ct)).Report;

    /// <summary>
    /// <see cref="BuildReportAsync"/> ning ichki varianti: hisobot bilan birga HAR QATOR uchun
    /// o'qituvchi vaznlarini ham qaytaradi (<c>(StudentId, CourseId)</c> → o'qituvchi ulushlari).
    /// Vaznlar DTO'ga chiqmaydi (<c>Shares</c> faqat "ready" qatorlarda to'ladi), lekin
    /// <see cref="ForTeacherAsync"/> ga "yo'ldagi" sikllarni ko'rsatish uchun kerak — mantiq
    /// NUSXALANMASIN uchun shu yerdan uzatiladi.
    /// </summary>
    private static async Task<(RetentionReportDto Report,
                               Dictionary<(string Student, string Course), RowTeachers> Teachers)>
        BuildReportCoreAsync(IAppDbContext db, string? onlyStudentId, CancellationToken ct)
    {
        var teachersByRow = new Dictionary<(string, string), RowTeachers>();
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var settings = Settings(meta);

        // KIM hisobotga kiradi: ptichka endi a'zolikni AKTIVLASHTIRISH oynasida qo'yiladi va
        // (o'quvchi, fan) uchun `RetentionBonusTrack` yozadi. Shu sabab ikkita manba birlashtiriladi:
        //   1) kamida bitta YOQILGAN tracki bor o'quvchilar (yangi, asosiy yo'l);
        //   2) eski `Student.RetentionBonus` ptichkasi qo'yilganlar (orqaga moslik — o'quvchi
        //      formasidagi ptichka olib tashlanganidan keyin ham eski ma'lumot yo'qolmasin).
        // Fan darajasida qaysi biri ustun turishi pastda: track bo'lsa `Enabled`, bo'lmasa legacy.
        var trackStudentIds = await db.RetentionBonusTracks.AsNoTracking()
            .Where(t => t.Enabled).Select(t => t.StudentId).Distinct().ToListAsync(ct);

        var q = db.Students.AsNoTracking()
            .Where(s => s.RetentionBonus || trackStudentIds.Contains(s.Id));
        if (onlyStudentId is not null) q = q.Where(s => s.Id == onlyStudentId);
        var students = await q
            .Select(s => new { s.Id, s.FullName, s.IsArchived, s.ArchivedAt, s.RetentionBonusStartMonth, s.RetentionBonus })
            .ToListAsync(ct);
        if (students.Count == 0) return (new RetentionReportDto([], settings, 0), teachersByRow);

        var ids = students.Select(s => s.Id).ToList();

        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(m => ids.Contains(m.StudentId)).ToListAsync(ct);

        // Hisob qatorlari (chegirma ALOHIDA — 100% chegirma ham HISOB BOR degani).
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => ids.Contains(c.StudentId))
            .Select(c => new ChargeRow(c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount))
            .ToListAsync(ct);

        // To'lovlar — QAYSI OY UCHUN to'langani (t.Month) bo'yicha; vozvrat MANFIY qo'shiladi.
        // DIQQAT: SalaryLedger t.Date bo'yicha yig'adi — u "shu oyda qancha pul TUSHDI" ni
        // so'raydi; bu yerda esa "falon oy YOPILDIMI" — boshqa savol, shuning uchun Month.
        var payments = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && ids.Contains(t.StudentId) && t.Month != null
                            && ((t.Direction == "income" && t.Category == "tuition")
                                || (t.Direction == "expense" && t.Category == "refund")))
                .Select(t => new { StudentId = t.StudentId!, t.GroupId, Month = t.Month!, t.Amount, t.Direction })
                .ToListAsync(ct))
            .Select(t => new PayRow(t.StudentId, t.GroupId, t.Month,
                                    t.Direction == "expense" ? -t.Amount : t.Amount))
            .ToList();

        // Guruhlar: a'zoliklardan + hisob/to'lov teglaridan (o'quvchi guruhdan butunlay o'chirilgan
        // bo'lsa ham teglangan yozuv qaysi kursga tegishli ekani aniqlansin).
        var groupIds = memberships.Select(m => m.GroupId)
            .Concat(charges.Select(c => c.GroupId ?? ""))
            .Concat(payments.Select(p => p.GroupId ?? ""))
            .Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
        var groupRows = await db.Classes.AsNoTracking()
            .Where(c => groupIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.MonthlyFee, c.TeacherId, c.Days, c.CourseId })
            .ToListAsync(ct);
        var courseIds = groupRows.Select(g => g.CourseId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var courseNames = (await db.Subjects.AsNoTracking()
                .Where(s => courseIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.Name);
        // Kurs kaliti: CourseId; bo'sh bo'lsa (eski, kursi biriktirilmagan guruh) — guruh id'si.
        // Nomi: Subject.Name, fallback Group.Name.
        var groupById = groupRows.ToDictionary(g => g.Id, g => new GroupInfo(
            g.Id, g.Name, g.MonthlyFee, g.TeacherId, g.Days,
            string.IsNullOrEmpty(g.CourseId) ? g.Id : g.CourseId,
            string.IsNullOrEmpty(g.CourseId) ? g.Name : courseNames.GetValueOrDefault(g.CourseId, g.Name)));

        var history = await GroupTeacherHistory.LoadAsync(db, groupIds, ct);

        var teacherIds = groupById.Values.Select(g => g.TeacherId)
            .Concat(history.Values.SelectMany(v => v.Select(a => a.TeacherId)))
            .Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        var teacherNames = (await db.Teachers.AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FullName }).ToListAsync(ct))
            .ToDictionary(t => t.Id, t => t.FullName);

        var awards = await db.RetentionBonusAwards.AsNoTracking()
            .Where(a => ids.Contains(a.StudentId)).ToListAsync(ct);
        var awardIds = awards.Select(a => a.Id).ToList();
        var shares = await db.RetentionBonusShares.AsNoTracking()
            .Where(s => awardIds.Contains(s.AwardId)).ToListAsync(ct);
        var sharesByAward = shares.GroupBy(s => s.AwardId).ToDictionary(g => g.Key, g => g.ToList());
        var awardStudent = awards.ToDictionary(a => a.Id, a => a.StudentId);
        // Bloklangan juftliklar: (o'quvchi → shu o'quvchi orqali ALLAQACHON bonus olgan o'qituvchilar).
        // Bekor qilingan bonus ham bloklaydi — status bo'yicha filtrlanmaydi (ataylab).
        var blockedByStudent = shares
            .Where(s => awardStudent.ContainsKey(s.AwardId))
            .GroupBy(s => awardStudent[s.AwardId])
            .ToDictionary(g => g.Key, g => g.Select(s => s.TeacherId).ToHashSet());
        var awardsByStudent = awards.GroupBy(a => a.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tracks = (await db.RetentionBonusTracks.AsNoTracking()
                .Where(t => ids.Contains(t.StudentId)).ToListAsync(ct))
            .ToDictionary(t => (t.StudentId, t.CourseId), t => t);

        var membsByStudent = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var money = BuildMoney(charges, payments, membsByStudent, groupById);

        var currentMonth = TuitionService.CurrentMonth();
        var rows = new List<RetentionRowDto>();

        foreach (var s in students)
        {
            var membs = membsByStudent.GetValueOrDefault(s.Id, []);
            var studentAwards = awardsByStudent.GetValueOrDefault(s.Id, []);
            var blocked = blockedByStudent.GetValueOrDefault(s.Id, []);

            // Qatorlar HAR FAN uchun. Kurslar ro'yxati: a'zoliklardan + tarixdan (track/bonus) —
            // o'quvchi fandan butunlay chiqib ketgan bo'lsa ham tarixi ko'rinib tursin.
            var courseKeys = membs.Select(m => CourseKeyOf(m.GroupId, groupById))
                .Concat(studentAwards.Select(a => a.CourseId))
                .Concat(tracks.Keys.Where(k => k.StudentId == s.Id).Select(k => k.CourseId))
                .Distinct().ToList();
            // Hech bir guruhga biriktirilmagan o'quvchi ro'yxatdan tushib qolmasin.
            if (courseKeys.Count == 0) courseKeys.Add("");

            foreach (var course in courseKeys)
            {
                // Shu FAN bonus hisobiga kiradimi. Track bo'lsa — u ustun (aktivlashtirish
                // oynasidagi ptichka shuni yozadi); bo'lmasa eski o'quvchi darajasidagi ptichka.
                var track = tracks.GetValueOrDefault((s.Id, course));
                if (!(track?.Enabled ?? s.RetentionBonus)) continue;

                var courseMembs = membs.Where(m => CourseKeyOf(m.GroupId, groupById) == course).ToList();
                var courseName = CourseNameOf(course, courseMembs, groupById, studentAwards);
                var courseAwards = studentAwards.Where(a => a.CourseId == course)
                    .OrderBy(a => a.CycleNo).ToList();

                var awardDtos = courseAwards.Select(a => new RetentionAwardDto(
                    a.Id, a.StudentId, a.StudentName, a.CourseId, a.CourseName,
                    a.CycleNo, a.PeriodFrom, a.PeriodTo,
                    a.TotalAmount, a.Status, a.CancelReason, a.CreatedAt, a.GivenBy, a.Note,
                    sharesByAward.GetValueOrDefault(a.Id, [])
                        .Select(x => new RetentionShareDto(x.TeacherId, x.TeacherName, x.Months, x.Amount))
                        .ToList())).ToList();

                // Joriy sikl raqami — shu fan bo'yicha BARCHA bonuslardan keyingisi. Bekor qilingan
                // bonus ham raqamni band qiladi: uning sikli qaytarilmaydi (talab 3).
                var cycleNo = courseAwards.Count + 1;

                var activeGroups = courseMembs
                    .Where(m => m.IsActive && m.Status != "frozen" && groupById.ContainsKey(m.GroupId))
                    .Select(m => groupById[m.GroupId]).ToList();
                // Guruhlar — id bilan: hisobotda nomi bosilsa guruh sahifasiga o'tiladi.
                var groupRefs = activeGroups
                    .GroupBy(g => g.Id).Select(g => new RetentionRefDto(g.Key, g.First().Name)).ToList();
                var days = string.Join(" · ", activeGroups
                    .Select(g => FormatDays(g.Days)).Where(d => d != "").Distinct());

                // Sanoq boshlanish oyi: shu fan uchun track, bo'lmasa o'quvchining umumiy
                // boshlang'ich qiymati (orqaga moslik — track jadvali paydo bo'lgunga qadar).
                var startMonth = (track?.StartMonth
                                  ?? s.RetentionBonusStartMonth ?? "").Trim();
                if (startMonth.Length < 7)
                {
                    // Sikl boshlanmagan — o'qitgan oy yo'q, shuning uchun guruhlarning HOZIRGI
                    // o'qituvchisi ko'rsatiladi (jadval ustuni bo'sh qolmasin).
                    var currentTeachers = activeGroups
                        .Select(g => g.TeacherId).Where(t => !string.IsNullOrEmpty(t)).Distinct()
                        .Select(t => new RetentionRefDto(t, teacherNames.GetValueOrDefault(t, "—")))
                        .ToList();
                    rows.Add(new RetentionRowDto(
                        s.Id, s.FullName, course, courseName, groupRefs, currentTeachers, days,
                        "", cycleNo, [], 0, settings.MonthsRequired,
                        RowNotStarted, "Boshlanish oyi kiritilmagan", s.IsArchived, [], awardDtos));
                    continue;
                }
                startMonth = startMonth[..7];

                var walk = Walk(s.Id, course, startMonth, currentMonth, settings, courseMembs,
                                groupById, history, teacherNames, money, s.IsArchived, s.ArchivedAt);

                // O'qituvchi vaznlari HAR qator uchun hisoblanadi (arzon — kataklar ustidan bir yurish):
                // taqsimot uchun ham, o'qituvchi profilidagi "yo'ldagilar" ro'yxati uchun ham.
                var rowTeachers = TeachersOfRow(walk.Cells, courseMembs, groupById, history);
                teachersByRow[(s.Id, course)] = rowTeachers;

                // Tayyor bo'lsa — taxminiy taqsimot: standart summa bo'yicha oldindan hisoblanadi
                // (admin modalda summani ham, ulushlarni ham o'zgartira oladi; summa o'zgarsa klient
                // Months nisbatida qayta bo'ladi).
                var shareDtos = walk.Status == RowReady
                    ? Distribute(rowTeachers.Weights, teacherNames, blocked, settings.DefaultAmount)
                    : [];

                var status = walk.Status;
                var note = walk.Note;

                // Shu sikldan bonus OLISHI MUMKIN bo'lgan o'qituvchilar. Sikl hali boshlanmagan
                // bo'lsa (birorta oy o'tmagan — masalan bonus berilgach keyingi sikl kelasi oydan
                // boshlanadi) guruhlarning HOZIRGI o'qituvchisi olinadi.
                var candidates = rowTeachers.Participants.Count > 0
                    ? rowTeachers.Participants
                    : activeGroups.Select(g => g.TeacherId)
                        .Where(t => !string.IsNullOrEmpty(t)).ToHashSet();

                // Barcha nomzod o'qituvchilar shu o'quvchi orqali ALLAQACHON bonus olgan — bu sikl
                // hech kimga bonus keltirmaydi. Sanoq to'lishini KUTMASDAN shuni ko'rsatamiz:
                // aks holda bonus berilgandan keyin ochilgan yangi sikl jadvalda "Yo'lda 0/6" bo'lib
                // turaverardi va admin "bonus berilgan-ku, nega yo'lda?" deb chalkashardi.
                // O'qituvchi almashsa qator o'z-o'zidan yana "yo'lda" ga qaytadi (jonli hisob).
                if (status is RowProgress or RowReady
                    && candidates.Count > 0 && candidates.All(blocked.Contains))
                {
                    status = RowBlocked;
                    note = "Barcha o'qituvchilar bu o'quvchi orqali bonus olgan";
                }

                // Jadvaldagi «O'qituvchi» ustuni — eng ko'p oy olgani birinchi (asosiy o'qituvchi
                // oldinda tursin). `candidates` ishlatiladi: siklda o'qitganlar, ular hali yo'q
                // bo'lsa (sikl boshlanmagan — masalan bonusdan keyingi yangi sikl kelasi oydan
                // boshlanadi) guruhning HOZIRGI o'qituvchisi — ustun bo'sh qolmasin.
                var teacherRefs = candidates
                    .OrderByDescending(t => rowTeachers.Weights.GetValueOrDefault(t))
                    .ThenBy(t => teacherNames.GetValueOrDefault(t, ""), StringComparer.OrdinalIgnoreCase)
                    .Select(t => new RetentionRefDto(t, teacherNames.GetValueOrDefault(t, "—")))
                    .ToList();

                rows.Add(new RetentionRowDto(
                    s.Id, s.FullName, course, courseName, groupRefs, teacherRefs, days,
                    startMonth, cycleNo,
                    walk.Cells, walk.Counted, settings.MonthsRequired,
                    status, note, s.IsArchived, shareDtos, awardDtos));
            }
        }

        rows = [.. rows
            .OrderByDescending(r => r.Status == RowReady)
            .ThenByDescending(r => r.Counted)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CourseName, StringComparer.OrdinalIgnoreCase)];

        return (new RetentionReportDto(rows, settings, rows.Count(r => r.Status == RowReady)), teachersByRow);
    }

    /* ==================== PUL: HISOB/TO'LOVNI FANLAR KESIMIGA AJRATISH ==================== */

    /// <summary>
    /// (o'quvchi, fan, oy) → hisoblangan/to'langan + shu oyda hisob qatori bormi.
    ///
    /// <para>Teglangan yozuv (<c>GroupId</c> bor) 100% o'sha guruhning KURSIGA tushadi.
    /// TEGLANMAGAN (GroupId=null — per-guruh billingdan OLDINGI eski yozuvlar) hisob va to'lov
    /// o'quvchining SHU OYDAGI billable a'zoliklari <c>MonthlyFee</c> nisbatida taqsimlanadi —
    /// <c>SalaryLedger</c> va <c>GroupBalanceService</c> dagi AYNAN o'sha konvensiya
    /// (<c>.claude/rules/billing.md</c>). Ular boshqa shakl bilan ishlagani (guruh kesimi, o'z
    /// so'rovlari) uchun umumiy yordamchiga chiqarilmadi — QOIDA bir xil: narx nisbatida, narxlar
    /// noma'lum bo'lsa teng.</para>
    /// </summary>
    private static Dictionary<(string Student, string Course, string Month), MoneyCell> BuildMoney(
        List<ChargeRow> charges, List<PayRow> payments,
        Dictionary<string, List<StudentGroup>> membsByStudent,
        Dictionary<string, GroupInfo> groupById)
    {
        var money = new Dictionary<(string, string, string), MoneyCell>();
        MoneyCell Cell(string sid, string course, string month)
        {
            var key = (sid, course, month);
            if (!money.TryGetValue(key, out var c)) money[key] = c = new MoneyCell();
            return c;
        }

        // Teglanmaganlar: (o'quvchi, oy) → jami hisob / jami to'lov + hisob qatori bormi.
        var untaggedCharge = new Dictionary<(string, string), decimal>();
        var untaggedChargeRow = new HashSet<(string, string)>();
        var untaggedPaid = new Dictionary<(string, string), decimal>();

        foreach (var c in charges)
        {
            if (c.Month is not { Length: >= 7 }) continue;
            var month = c.Month[..7];
            var effective = Math.Max(0m, c.Amount - c.Discount);
            var gid = c.GroupId ?? "";
            if (gid.Length > 0 && groupById.TryGetValue(gid, out var g))
            {
                var cell = Cell(c.StudentId, g.CourseKey, month);
                cell.Charged += effective;
                cell.HasCharge = true;   // qator BOR — summasi 0 bo'lsa ham (100% chegirma)
            }
            else
            {
                untaggedCharge[(c.StudentId, month)] =
                    untaggedCharge.GetValueOrDefault((c.StudentId, month)) + effective;
                untaggedChargeRow.Add((c.StudentId, month));
            }
        }

        foreach (var p in payments)
        {
            if (p.Month is not { Length: >= 7 } || p.Amount == 0m) continue;
            var month = p.Month[..7];
            var gid = p.GroupId ?? "";
            if (gid.Length > 0 && groupById.TryGetValue(gid, out var g))
                Cell(p.StudentId, g.CourseKey, month).Paid += p.Amount;
            else
                untaggedPaid[(p.StudentId, month)] =
                    untaggedPaid.GetValueOrDefault((p.StudentId, month)) + p.Amount;
        }

        foreach (var key in untaggedChargeRow.Concat(untaggedPaid.Keys).Distinct())
        {
            var (sid, month) = key;
            var split = SplitByFee(membsByStudent.GetValueOrDefault(sid, []), month, groupById);
            if (split.Count == 0) continue;   // shu oyda billable a'zolik yo'q — taqsimlab bo'lmaydi
            var chargeSum = untaggedCharge.GetValueOrDefault(key, 0m);
            var paidSum = untaggedPaid.GetValueOrDefault(key, 0m);
            var hasRow = untaggedChargeRow.Contains(key);
            foreach (var (course, frac) in split)
            {
                var cell = Cell(sid, course, month);
                cell.Charged += chargeSum * frac;
                cell.Paid += paidSum * frac;
                if (hasRow) cell.HasCharge = true;
            }
        }

        return money;
    }

    /// <summary>Shu oydagi billable a'zoliklarni KURSLAR bo'yicha ulushga ajratadi (yig'indi = 1.0).
    /// Narxlar noma'lum (hammasi 0) bo'lsa teng bo'linadi.</summary>
    private static Dictionary<string, decimal> SplitByFee(
        List<StudentGroup> membs, string month, Dictionary<string, GroupInfo> groupById)
    {
        var billable = membs.Where(m => MembershipLifecycle.BillableInMonth(m, month)).ToList();
        var res = new Dictionary<string, decimal>();
        if (billable.Count == 0) return res;
        var denom = billable.Sum(m => FeeOf(m.GroupId, groupById));
        foreach (var m in billable)
        {
            var course = CourseKeyOf(m.GroupId, groupById);
            var frac = denom > 0 ? FeeOf(m.GroupId, groupById) / denom : 1m / billable.Count;
            res[course] = res.GetValueOrDefault(course) + frac;
        }
        return res;
    }

    /* ==================== SIKL MANTIG'I ==================== */

    private sealed record WalkResult(List<RetentionMonthCellDto> Cells, int Counted, string Status, string Note);

    /// <summary>
    /// Boshlanish oyidan joriy oygacha yurib har oyning holatini aniqlaydi va siklni baholaydi —
    /// BITTA FAN kesimida (<paramref name="courseMembs"/> — faqat shu kursga tegishli a'zoliklar).
    ///
    /// <para><b>Tekshiruv A'ZOLIK darajasida EMAS, KURS darajasida.</b> Guruh almashtirishda
    /// (<c>ClassesController.TransferMember</c>) eski a'zolik MUZLATILADI va yangisi ochiladi —
    /// ya'ni "muzlatilgan" belgisi "markazdan ketdi" degani emas. Shuning uchun savol
    /// <i>"shu oyda o'quvchining SHU KURS bo'yicha kamida bitta pullik a'zoligi bormi?"</i> bo'ladi
    /// va bir fan ichida guruh almashtirish siklni buzmaydi.</para>
    ///
    /// <para>Qoidalar: ✅ → +1 · ⏳ (qarz) va 📄 (hisob yozilmagan) → +0, lekin sikl uzilmaydi va
    /// pauza ham emas (o'quvchi shu oyda O'QIGAN) · ❄️/🚪 → pauza, ketma-ket <c>MaxGapMonths</c>
    /// dan oshsa UZILADI · 🔴 arxivlangan → DARHOL uziladi (aniq signal, 2 oy kutilmaydi).</para>
    /// </summary>
    private static WalkResult Walk(
        string studentId, string course, string startMonth, string currentMonth,
        RetentionSettingsDto settings,
        List<StudentGroup> courseMembs,
        Dictionary<string, GroupInfo> groupById,
        Dictionary<string, List<GroupTeacherAssignment>> history,
        Dictionary<string, string> teacherNames,
        Dictionary<(string, string, string), MoneyCell> money,
        bool isArchived, string? archivedAt)
    {
        var cells = new List<RetentionMonthCellDto>();
        var counted = 0;
        var gap = 0;
        var archiveMonth = (archivedAt ?? "").Length >= 7 ? archivedAt![..7] : null;

        if (string.CompareOrdinal(startMonth, currentMonth) > 0)
            return new WalkResult(cells, 0, RowProgress, "Boshlanish oyi hali kelmagan");

        foreach (var month in TuitionService.MonthRange(startMonth, currentMonth))
        {
            // Arxivlash — aniq signal: o'quvchi ketdi, 2 oy kutib o'tirilmaydi.
            if (isArchived && (archiveMonth is null || string.CompareOrdinal(month, archiveMonth) >= 0))
                return new WalkResult(cells, counted, RowBroken,
                    archiveMonth is null ? "O'quvchi arxivlangan" : $"O'quvchi arxivlangan ({archiveMonth})");

            var billable = courseMembs.Where(m => MembershipLifecycle.BillableInMonth(m, month)).ToList();
            var (tid, tname) = TeachersOfMonth(billable, groupById, history, teacherNames, month);

            if (billable.Count == 0)
            {
                // Muzlatilgan (ta'til) va butunlay ketgan — ikkalasi ham PAUZA; farqi ko'rinishda.
                // Muzlashning ham chegarasi bor: 8 oy muzlab yotgan o'quvchi bonus keltirsa
                // tizimning ma'nosi qolmaydi (qaror #2 — pauza, maks MaxGapMonths oy).
                var frozen = courseMembs.Any(m => m.Status == "frozen" && m.FrozenAt.Length >= 7
                                                  && string.CompareOrdinal(month, m.FrozenAt[..7]) >= 0);
                cells.Add(new RetentionMonthCellDto(month, frozen ? StateFrozen : StateGone, 0m, 0m, tid, tname, false));
                gap++;
                if (gap > settings.MaxGapMonths)
                    return new WalkResult(cells, counted, RowBroken,
                        $"{gap} oy uzluksiz {(frozen ? "muzlatilgan" : "a'zolik yo'q")} — ruxsat {settings.MaxGapMonths} oy");
                continue;
            }

            gap = 0;
            var cell = money.GetValueOrDefault((studentId, course, month));
            var charged = decimal.Round(cell?.Charged ?? 0m, 2);
            var paid = decimal.Round(cell?.Paid ?? 0m, 2);

            // Hisob qatori umuman yo'q → "to'langan" DEB KO'RSATILMAYDI (talab 4). Sanoqqa kirmaydi,
            // lekin siklni ham uzmaydi va tanaffusga kirmaydi (gap 0 ga tushirilgan) — hisob
            // paydo bo'lgach katak o'z-o'zidan ✅/⏳ ga aylanadi.
            if (cell is null || !cell.HasCharge)
            {
                cells.Add(new RetentionMonthCellDto(month, StateNoCharge, charged, paid, tid, tname, false));
                continue;
            }

            var closed = charged - paid <= 0m;
            cells.Add(new RetentionMonthCellDto(month, closed ? StatePaid : StateDebt, charged, paid, tid, tname, closed));

            if (!closed) continue;
            counted++;
            if (counted >= settings.MonthsRequired)
                return new WalkResult(cells, counted, RowReady, "Bonus berish mumkin");
        }

        return new WalkResult(cells, counted, RowProgress, $"{counted}/{settings.MonthsRequired}");
    }

    /* ==================== TAQSIMOT ==================== */

    /// <summary>
    /// Bitta qatordagi o'qituvchilar: <b>Weights</b> — HISOBGA KIRGAN oylar bo'yicha vazn (taqsimot
    /// maxraji), <b>Participants</b> — qatordagi ISTALGAN oyda (qarzli/hisobsiz oylar ham) o'qitgan
    /// barcha o'qituvchilar.
    ///
    /// <para>Har hisobga kirgan oy vazni 1.0; o'quvchi shu fan bo'yicha bir vaqtda bir nechta guruhda
    /// o'qisa — oy vazni a'zoliklar orasida <c>MonthlyFee</c> nisbatida bo'linadi (teglanmagan
    /// to'lovni taqsimlash bilan AYNAN BIR XIL konvensiya). Har a'zolik → guruh → O'SHA OYDAGI
    /// o'qituvchi (<see cref="GroupTeacherHistory"/>; tarix topilmasa <c>Group.TeacherId</c>).</para>
    ///
    /// <para>Nega <c>Participants</c> alohida: o'qituvchi profilidagi "yo'ldagilar" ro'yxatida hali
    /// birorta oyi hisobga kirmagan (hammasi qarz/hisobsiz) yangi o'quvchi ham ko'rinishi kerak —
    /// vazn 0 bo'lsa ham u SHU o'qituvchining o'quvchisi.</para>
    /// </summary>
    private static RowTeachers TeachersOfRow(
        List<RetentionMonthCellDto> cells, List<StudentGroup> courseMembs,
        Dictionary<string, GroupInfo> groupById,
        Dictionary<string, List<GroupTeacherAssignment>> history)
    {
        var weight = new Dictionary<string, decimal>();
        var participants = new HashSet<string>();
        foreach (var c in cells)
        {
            var billable = courseMembs.Where(m => MembershipLifecycle.BillableInMonth(m, c.Month)).ToList();
            if (billable.Count == 0) continue;
            var denom = billable.Sum(m => FeeOf(m.GroupId, groupById));
            foreach (var m in billable)
            {
                var tid = TeacherFor(m.GroupId, c.Month, groupById, history);
                if (string.IsNullOrEmpty(tid)) continue;
                participants.Add(tid);
                if (!c.Counted) continue;
                // Narxlar noma'lum (hammasi 0) bo'lsa — teng bo'linadi, oy vazni baribir 1.0 qoladi.
                var share = denom > 0 ? FeeOf(m.GroupId, groupById) / denom : 1m / billable.Count;
                weight[tid] = weight.GetValueOrDefault(tid) + share;
            }
        }
        return new RowTeachers(weight, participants);
    }

    /// <summary>
    /// Bonusni o'qituvchilar orasida O'QIGAN OYLAR nisbatida bo'ladi (bitta fan kesimida).
    ///
    /// <para>Vaznlar <see cref="TeachersOfRow"/> da hisoblanadi (o'sha yerda qoida izohlangan) —
    /// bu yerda faqat pulga aylantirish.</para>
    ///
    /// <para><b>Bloklangan o'qituvchi</b> (shu o'quvchi orqali allaqachon bonus olgan) ulushga
    /// KIRMAYDI: uning summasi 0, <c>AlreadyAwarded=true</c>, lekin <c>Months</c> haqiqiy qiymat
    /// bilan qaytadi — admin nega tushib qolganini ko'rsin. Uning vazni qolganlarga QAYTA
    /// taqsimlanadi, yig'indi baribir <paramref name="totalAmount"/> ga teng chiqadi.</para>
    ///
    /// <para>Yaxlitlash qoldig'i eng katta ulushli (bloklanmagan) o'qituvchiga qo'shiladi.</para>
    /// </summary>
    private static List<RetentionShareDto> Distribute(
        Dictionary<string, decimal> weight,
        Dictionary<string, string> teacherNames,
        HashSet<string> blocked,
        decimal totalAmount)
    {
        if (weight.Count == 0) return [];

        var ordered = weight.OrderByDescending(kv => kv.Value)
            .ThenBy(kv => teacherNames.GetValueOrDefault(kv.Key, ""), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Faqat BLOKLANMAGANLAR maxrajga kiradi — bloklanganning vazni qolganlarga qayta bo'linadi.
        var openWeight = ordered.Where(kv => !blocked.Contains(kv.Key)).Sum(kv => kv.Value);

        var list = ordered.Select(kv => new RetentionShareDto(
                kv.Key,
                teacherNames.GetValueOrDefault(kv.Key, "(o'chirilgan o'qituvchi)"),
                decimal.Round(kv.Value, 4),
                blocked.Contains(kv.Key) || openWeight <= 0
                    ? 0m
                    : decimal.Round(totalAmount * kv.Value / openWeight, 0, MidpointRounding.AwayFromZero),
                blocked.Contains(kv.Key)))
            .ToList();

        // Yaxlitlash qoldig'i — eng katta bloklanmagan ulushga.
        var first = list.FindIndex(x => !x.AlreadyAwarded);
        if (first >= 0 && openWeight > 0)
        {
            var diff = totalAmount - list.Sum(x => x.Amount);
            if (diff != 0m) list[first] = list[first] with { Amount = list[first].Amount + diff };
        }

        return list;
    }

    private static decimal FeeOf(string groupId, Dictionary<string, GroupInfo> groupById) =>
        groupById.TryGetValue(groupId, out var g) ? g.MonthlyFee : 0m;

    /// <summary>Guruhning sikl kaliti: kurs (Subject id); kursi yo'q eski guruhda — guruh id'si.</summary>
    private static string CourseKeyOf(string groupId, Dictionary<string, GroupInfo> groupById) =>
        groupById.TryGetValue(groupId, out var g) ? g.CourseKey : groupId;

    /// <summary>Fan nomi: guruhlardan (Subject.Name yoki Group.Name), topilmasa bonus tarixidagi
    /// SNAPSHOT nomi, u ham bo'lmasa "—".</summary>
    private static string CourseNameOf(
        string course, List<StudentGroup> courseMembs,
        Dictionary<string, GroupInfo> groupById, List<RetentionBonusAward> studentAwards)
    {
        foreach (var m in courseMembs)
            if (groupById.TryGetValue(m.GroupId, out var g)) return g.CourseName;
        var snap = studentAwards.FirstOrDefault(a => a.CourseId == course && a.CourseName.Length > 0);
        return snap?.CourseName ?? "—";
    }

    /// <summary>Guruhning shu oydagi o'qituvchisi — tarixdan; topilmasa hozirgi biriktirilgani.</summary>
    private static string TeacherFor(
        string groupId, string month,
        Dictionary<string, GroupInfo> groupById,
        Dictionary<string, List<GroupTeacherAssignment>> history)
    {
        var fromHistory = GroupTeacherHistory.TeacherAtMonth(history.GetValueOrDefault(groupId), month);
        if (!string.IsNullOrEmpty(fromHistory)) return fromHistory;
        return groupById.TryGetValue(groupId, out var g) ? g.TeacherId : "";
    }

    /// <summary>Oy katagi ostida ko'rsatiladigan o'qituvchi(lar): eng katta narxli guruhniki
    /// birinchi, bir nechta bo'lsa hammasi vergul bilan.</summary>
    private static (string Id, string Name) TeachersOfMonth(
        List<StudentGroup> billable,
        Dictionary<string, GroupInfo> groupById,
        Dictionary<string, List<GroupTeacherAssignment>> history,
        Dictionary<string, string> teacherNames,
        string month)
    {
        var byTeacher = new Dictionary<string, decimal>();
        foreach (var m in billable)
        {
            var tid = TeacherFor(m.GroupId, month, groupById, history);
            if (string.IsNullOrEmpty(tid)) continue;
            byTeacher[tid] = byTeacher.GetValueOrDefault(tid) + FeeOf(m.GroupId, groupById);
        }
        if (byTeacher.Count == 0) return ("", "");
        var ordered = byTeacher.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        return (ordered[0], string.Join(", ", ordered.Select(id => teacherNames.GetValueOrDefault(id, "—"))));
    }

    /* ==================== BONUS BERISH ==================== */

    /// <summary>
    /// Shu o'quvchi orqali ALLAQACHON bonus olgan o'qituvchilar (bekor qilinganlari ham —
    /// bekor qilish blokni OLIB TASHLAMAYDI, aks holda "bekor qil → qayta ber" yo'li ochiq qolardi).
    /// </summary>
    public static async Task<HashSet<string>> BlockedTeachersAsync(
        IAppDbContext db, string studentId, CancellationToken ct = default)
    {
        var awardIds = await db.RetentionBonusAwards.AsNoTracking()
            .Where(a => a.StudentId == studentId).Select(a => a.Id).ToListAsync(ct);
        if (awardIds.Count == 0) return [];
        return (await db.RetentionBonusShares.AsNoTracking()
                .Where(s => awardIds.Contains(s.AwardId))
                .Select(s => s.TeacherId).Distinct().ToListAsync(ct))
            .ToHashSet();
    }

    /// <summary>
    /// Bonusni yozadi: <see cref="RetentionBonusAward"/> + har o'qituvchi uchun
    /// <see cref="RetentionBonusShare"/>, va SHU FAN sanog'ini keyingi siklga suradi
    /// (<see cref="RetentionBonusTrack"/>.StartMonth = davr oxiridan keyingi oy) — aks holda o'sha
    /// sikl jadvalda «tayyor» bo'lib turaverardi. Boshqa fanlar TEGILMAYDI.
    /// <c>Student.RetentionBonusStartMonth</c> ham o'zgartirilmaydi — u faqat BOSHLANG'ICH qiymat.
    ///
    /// <para>Holat qayta tekshiriladi (<see cref="BuildReportAsync"/> orqali, AYNAN o'sha mantiq):
    /// ikki admin bir vaqtda bosgan yoki jadval eskirgan bo'lsa xato qaytadi.
    /// Qo'shimcha himoya — <c>(StudentId, CourseId, CycleNo)</c> unikal indeksi.</para>
    ///
    /// <para><c>SaveChangesAsync</c> chaqirilmaydi — chaqiruvchi saqlaydi.</para>
    /// </summary>
    /// <returns>Xato bo'lsa (Award=null, Error=sabab).</returns>
    public static async Task<(RetentionBonusAward? Award, string? Error)> GiveAsync(
        IAppDbContext db, GiveRetentionBonusRequest req, string actor, CancellationToken ct = default)
    {
        if (req.TotalAmount <= 0m) return (null, "Bonus summasi 0 dan katta bo'lishi kerak");

        var courseId = (req.CourseId ?? "").Trim();
        var report = await BuildReportAsync(db, req.StudentId, ct);
        var row = report.Rows.FirstOrDefault(r => r.CourseId == courseId);
        if (row is null) return (null, "O'quvchi/fan topilmadi yoki bonus ptichkasi yoqilmagan");
        if (row.Status == RowBlocked)
            return (null, $"«{row.CourseName}» bo'yicha barcha o'qituvchilar bu o'quvchi orqali allaqachon bonus olgan");
        if (row.Status != RowReady)
            return (null, $"Sikl hali tayyor emas ({row.Counted}/{row.Required}) — {row.StatusNote}");
        if (row.Months.Count == 0) return (null, "Davr aniqlanmadi");

        var shares = (req.Shares ?? []).Where(s => !string.IsNullOrWhiteSpace(s.TeacherId)).ToList();
        if (shares.Count == 0) return (null, "Taqsimot bo'sh — kamida bitta o'qituvchi bo'lishi kerak");
        if (shares.Any(s => s.Amount < 0m)) return (null, "Ulush manfiy bo'lishi mumkin emas");
        if (shares.Sum(s => s.Amount) != req.TotalAmount)
            return (null, $"Taqsimot yig'indisi ({shares.Sum(s => s.Amount):N0}) jami summaga ({req.TotalAmount:N0}) teng emas");

        var teacherIds = shares.Select(s => s.TeacherId).Distinct().ToList();
        if (teacherIds.Count != shares.Count) return (null, "Bir o'qituvchi ikki marta ko'rsatilgan");
        var names = (await db.Teachers.AsNoTracking()
                .Where(t => teacherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.FullName }).ToListAsync(ct))
            .ToDictionary(t => t.Id, t => t.FullName);
        if (names.Count != teacherIds.Count) return (null, "Ko'rsatilgan o'qituvchilardan biri topilmadi");

        // Bir o'qituvchi bitta o'quvchi orqali FAQAT BIR MARTA bonus oladi (bekor qilingani ham bloklaydi).
        var blocked = await BlockedTeachersAsync(db, req.StudentId, ct);
        var already = teacherIds.FirstOrDefault(blocked.Contains);
        if (already is not null)
            return (null, $"{names[already]} bu o'quvchi orqali allaqachon bonus olgan");

        var periodTo = row.Months[^1].Month;
        var award = new RetentionBonusAward
        {
            StudentId = row.StudentId,
            StudentName = row.FullName,
            CourseId = row.CourseId,
            CourseName = row.CourseName,
            CycleNo = row.CycleNo,
            PeriodFrom = row.StartMonth,
            PeriodTo = periodTo,
            TotalAmount = req.TotalAmount,
            Status = RetentionBonusAward.StatusGiven,
            GivenBy = actor,
            Note = (req.Note ?? "").Trim(),
        };
        db.RetentionBonusAwards.Add(award);

        foreach (var s in shares)
            db.RetentionBonusShares.Add(new RetentionBonusShare
            {
                AwardId = award.Id,
                TeacherId = s.TeacherId,
                TeacherName = names[s.TeacherId],
                Months = decimal.Round(s.Months, 4),
                Amount = s.Amount,
            });

        // SHU FAN sanog'i davr oxiridan KEYINGI oydan davom etadi — uzluksiz zanjir.
        await UpsertTrackAsync(db, row.StudentId, row.CourseId, TuitionService.NextMonth(periodTo), actor, ct);

        return (award, null);
    }

    /// <summary>
    /// Bonusni bekor qiladi (xato kiritilgan bo'lsa). Yozuv O'CHIRILMAYDI — "cancelled" bo'lib
    /// tarixda qoladi, ulushlari ham (o'qituvchi profilida "bekor qilingan" belgisi bilan ko'rinadi).
    ///
    /// <para><b>Sanoq QAYTARILMAYDI</b> (markaz egasining talabi: "bekor qilinganda bekor qilingan
    /// deb qo'yilishi kerak va qayta bonus berilmasligi kerak"): <see cref="RetentionBonusTrack"/>
    /// o'z joyida qoladi va bekor qilingan bonusning ulushlari o'qituvchi(lar)ni shu o'quvchi orqali
    /// yangi bonusdan BLOKLAB turadi. Bekor qilingan bonus o'qituvchi JAMI summasiga kirmaydi.</para>
    /// </summary>
    public static async Task<string?> CancelAsync(
        IAppDbContext db, string awardId, string? reason, CancellationToken ct = default)
    {
        var award = await db.RetentionBonusAwards.FirstOrDefaultAsync(a => a.Id == awardId, ct);
        if (award is null) return "Bonus topilmadi";
        if (award.Status == RetentionBonusAward.StatusCancelled) return "Bonus allaqachon bekor qilingan";

        award.Status = RetentionBonusAward.StatusCancelled;
        award.CancelReason = (reason ?? "").Trim();
        return null;
    }

    /// <summary>Uzilgan (yoki istalgan) siklni yangi oydan qayta boshlash — FAQAT ko'rsatilgan fan uchun
    /// (<see cref="RetentionBonusTrack"/> yoziladi; boshqa fanlar tegilmaydi).</summary>
    public static async Task<string?> RestartAsync(
        IAppDbContext db, string studentId, string courseId, string startMonth,
        string actor, CancellationToken ct = default)
    {
        var month = (startMonth ?? "").Trim();
        if (month.Length < 7 || !DateOnly.TryParse($"{month[..7]}-01", out _))
            return "Boshlanish oyi noto'g'ri (kutilgan: YYYY-MM)";

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return "O'quvchi topilmadi";

        student.RetentionBonus = true;
        await UpsertTrackAsync(db, studentId, (courseId ?? "").Trim(), month[..7], actor, ct);
        return null;
    }

    /// <summary>(o'quvchi, fan) sanoq boshlanish oyini yozadi/yangilaydi.</summary>
    private static async Task UpsertTrackAsync(
        IAppDbContext db, string studentId, string courseId, string startMonth,
        string actor, CancellationToken ct)
    {
        var track = await db.RetentionBonusTracks
            .FirstOrDefaultAsync(t => t.StudentId == studentId && t.CourseId == courseId, ct);
        if (track is null)
        {
            track = new RetentionBonusTrack { StudentId = studentId, CourseId = courseId };
            db.RetentionBonusTracks.Add(track);
        }
        track.StartMonth = startMonth;
        track.Enabled = true;
        track.UpdatedBy = actor;
        track.UpdatedAt = AppClock.Now;
    }

    /// <summary>
    /// A'ZOLIK AKTIVLASHTIRILGANDA chaqiriladi: shu guruh FANI bo'yicha bonus hisoblanishini
    /// yoqadi/o'chiradi va sanoqni AKTIVLASHTIRILGAN oydan boshlaydi.
    ///
    /// <para>Nega aynan aktivlashtirishda: o'quvchi guruhga bir oyda qo'shilib, keyingi oydan
    /// aktivlashtirilishi mumkin (sinov oyi to'lovsiz). Sanoq esa PULLIK oydan boshlanishi kerak,
    /// shuning uchun boshlanish oyi qo'shilgan sanadan emas, <paramref name="activatedAtIso"/>
    /// dan olinadi.</para>
    ///
    /// <para><b>Mavjud sanoqqa tegilmaydi:</b> agar shu fan uchun sanoq allaqachon ketayotgan
    /// bo'lsa (masalan bir fan ichida guruh almashtirilgan yoki ta'tildan keyin qayta
    /// aktivlashtirilgan), boshlanish oyi ORQAGA SURILMAYDI — aks holda yig'ilgan oylar
    /// yo'qolardi. Faqat yangi yoki o'chirilgan holatdagi sanoq shu oydan boshlanadi.</para>
    ///
    /// <para><c>enabled == null</c> — hech narsa qilinmaydi (eski chaqiruvlar buzilmasin).
    /// <c>SaveChangesAsync</c> chaqirilmaydi — chaqiruvchi saqlaydi.</para>
    /// </summary>
    public static async Task ApplyOnActivateAsync(
        IAppDbContext db, string studentId, Group group, string activatedAtIso, bool? enabled,
        string actor, CancellationToken ct = default)
    {
        if (enabled is null || activatedAtIso.Length < 7) return;

        var courseId = string.IsNullOrWhiteSpace(group.CourseId) ? group.Id : group.CourseId;
        var month = activatedAtIso[..7];

        var track = await db.RetentionBonusTracks
            .FirstOrDefaultAsync(t => t.StudentId == studentId && t.CourseId == courseId, ct);

        if (track is null)
        {
            if (enabled != true) return;   // o'chirilgan holat uchun bo'sh qator yaratish shart emas
            db.RetentionBonusTracks.Add(new RetentionBonusTrack
            {
                StudentId = studentId, CourseId = courseId, StartMonth = month,
                Enabled = true, UpdatedBy = actor, UpdatedAt = AppClock.Now,
            });
            return;
        }

        // Ketayotgan sanoq bor edi va yoqilgan holicha qolyapti — boshlanish oyi TEGILMAYDI.
        if (track.Enabled && enabled == true)
        {
            track.UpdatedBy = actor;
            track.UpdatedAt = AppClock.Now;
            return;
        }

        if (enabled == true) track.StartMonth = month;   // o'chirilgandan qayta yoqildi → shu oydan
        track.Enabled = enabled.Value;
        track.UpdatedBy = actor;
        track.UpdatedAt = AppClock.Now;
    }

    /* ==================== O'QITUVCHI KESIMI ==================== */

    /// <summary>
    /// Bitta o'qituvchining bonus kesimi (profil tabi va o'qituvchi ilovasi uchun):
    /// <c>Items</c> — BERILGAN bonuslar (bekor qilinganlar ham belgisi bilan qaytadi, lekin JAMI ga
    /// kirmaydi); <c>InProgress</c> — hali bonus berilmagan, oylari TO'PLANAYOTGAN (o'quvchi × fan)
    /// sikllari ("yangi o'quvchilarim qanday hisoblanyapti").
    ///
    /// <para>"Yo'ldagilar" <see cref="BuildReportAsync"/> ning ichki variantidan olinadi — sikl
    /// mantig'i va oy vaznlari YAGONA joyda qolsin (nusxa yozilmaydi). Bu butun hisobotni
    /// hisoblaydi: ptichkali o'quvchilar soni kam bo'lgani uchun maqbul (mavjud tamoyil —
    /// hech narsa saqlanmaydi, hammasi har so'rovda qayta hisoblanadi).</para>
    /// </summary>
    public static async Task<TeacherRetentionSummaryDto> ForTeacherAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var shares = await db.RetentionBonusShares.AsNoTracking()
            .Where(s => s.TeacherId == teacherId).ToListAsync(ct);

        var items = new List<TeacherRetentionBonusDto>();
        if (shares.Count > 0)
        {
            var awardIds = shares.Select(s => s.AwardId).Distinct().ToList();
            var awards = (await db.RetentionBonusAwards.AsNoTracking()
                    .Where(a => awardIds.Contains(a.Id)).ToListAsync(ct))
                .ToDictionary(a => a.Id);

            items = shares
                .Where(s => awards.ContainsKey(s.AwardId))
                .Select(s =>
                {
                    var a = awards[s.AwardId];
                    return new TeacherRetentionBonusDto(
                        a.Id, a.StudentId, a.StudentName, a.CourseName, a.PeriodFrom, a.PeriodTo,
                        s.Months, s.Amount, a.CreatedAt, a.GivenBy, a.Status);
                })
                .OrderByDescending(x => x.GivenAt)
                .ToList();
        }

        // Shu o'qituvchi ALLAQACHON bonus olgan o'quvchilar (bekor qilingani ham bloklaydi).
        var awardedStudents = items.Select(x => x.StudentId).ToHashSet();

        var (report, teachersByRow) = await BuildReportCoreAsync(db, null, ct);
        var inProgress = new List<TeacherRetentionProgressDto>();
        foreach (var r in report.Rows)
        {
            // Boshlanish oyi kiritilmagan qatorda ko'rsatadigan narsa yo'q.
            if (r.Status == RowNotStarted) continue;

            var t = teachersByRow.GetValueOrDefault((r.StudentId, r.CourseId));
            var mine = t?.Weights.GetValueOrDefault(teacherId) ?? 0m;
            // Qator kiradi: o'qituvchi qatordagi biror oyda o'qitgan bo'lsa (vazni 0 bo'lsa ham —
            // hammasi qarz/hisobsiz oy bo'lishi mumkin) yoki tayyor taqsimotda ko'rinsa.
            if (!(t?.Participants.Contains(teacherId) ?? false)
                && !r.Shares.Any(x => x.TeacherId == teacherId)) continue;

            inProgress.Add(new TeacherRetentionProgressDto(
                r.StudentId, r.FullName, r.CourseId, r.CourseName,
                string.Join(", ", r.Groups.Select(g => g.Name)),
                r.Counted, r.Required, decimal.Round(mine, 4),
                r.Status, r.StatusNote, awardedStudents.Contains(r.StudentId)));
        }

        // Tartib: avval "tayyor", keyin "yo'lda" (sanoq kamayishi bo'yicha), oxirida uzilgan/bloklangan.
        inProgress = [.. inProgress
            .OrderBy(x => x.Status == RowReady ? 0 : x.Status == RowProgress ? 1 : 2)
            .ThenByDescending(x => x.Counted)
            .ThenBy(x => x.StudentName, StringComparer.OrdinalIgnoreCase)];

        var active = items.Where(x => x.Status == RetentionBonusAward.StatusGiven).ToList();
        return new TeacherRetentionSummaryDto(
            active.Sum(x => x.Amount), active.Count, items, inProgress);
    }

    /* ==================== YORDAMCHI ==================== */

    private static string FormatDays(List<int> days) =>
        days is null || days.Count == 0 ? "" :
        string.Join(", ", days.Order().Select(d => d >= 0 && d < DayShort.Length ? DayShort[d] : "?"));
}
