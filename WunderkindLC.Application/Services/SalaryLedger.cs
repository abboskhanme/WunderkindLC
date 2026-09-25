using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): jami belgilangan, berilgan, qoldiq
/// va har oyda qancha maosh berilgani. Admin moliya bo'limi ham, o'qituvchi ilovasi ham shu yagona
/// mantiqdan foydalanadi (ikki joyda farq qilib ketmasligi uchun).
///
/// MAOSH HISOBI:
///   • PER-GURUH (yangi, ustuvor) — o'qituvchining HAR guruhi alohida sozlanadi (<see cref="Group.TeacherSalaryMode"/>):
///       "percent" → shu guruhdan yig'ilgan to'lovning <see cref="Group.TeacherSalaryPercent"/> foizi;
///       "fixed"   → shu guruh uchun qat'iy summa <see cref="Group.TeacherSalaryFixed"/>.
///       Oylik maosh = barcha guruhlar ulushi YIG'INDISI (bir guruhi 40%, keyingisi 60% bo'lishi mumkin).
///   • LEGACY (hech bir guruh sozlanmagan bo'lsa) — o'qituvchi darajasidagi eski sozlama:
///       "fixed" qat'iy <see cref="Teacher.Salary"/> | "percent" barcha guruhlardan yig'ilganning
///       <see cref="Teacher.SalaryPercent"/> foizi.
///
/// JURNALGA BOG'LASH (<see cref="CenterMeta.SalaryRequireJournal"/>): yoqilgan bo'lsa har oyda jurnalda
/// "o'tildi" belgilanmagan dars o'tilmagan hisoblanadi va uning haqi maoshdan ushlanadi
/// (<see cref="SalaryJournalStats"/>). Ushlanma sababi <see cref="MonthSalaryDto.Lessons"/>da
/// guruh + o'tkazib yuborilgan sanalar bilan qaytadi — moliya bo'limi shuni ko'rsatadi.
/// </summary>
public static class SalaryLedger
{
    /// <summary>
    /// Shu oy uchun FOIZ BAZASI hisoblangan oylikmi (aks holda yig'ilgan pul).
    /// <paramref name="chargedFrom"/> — <see cref="CenterMeta.SalaryChargedBaseFrom"/> ("yyyy-MM"; bo'sh = hech qachon).
    ///
    /// <para>Foydalanuvchi qarori (2026-09-25): o'qituvchi o'quvchi to'lashini kutmasdan, hisoblangan
    /// TO'LIQ oylikdan (chegirmasiz) foiz oladi; chegirmani markaz ko'taradi. ⚠️ Kuchga kirishdan oldingi
    /// oylar ATAYIN yig'ilgan pulda qoladi: ular uchun maosh allaqachon berilgan — qayta hisoblansa
    /// "qoldiq" jimgina o'zgarib ketardi.</para>
    /// </summary>
    public static bool UsesChargedBase(string month, string? chargedFrom) =>
        chargedFrom is { Length: >= 7 }
        && string.CompareOrdinal(month.Length >= 7 ? month[..7] : month, chargedFrom[..7]) >= 0;

    /// <summary>Guruhning amaldagi maosh rejimi: sozlangan bo'lsa o'zi, aks holda o'qituvchi darajasidagi.</summary>
    private static string EffMode(string groupMode, string teacherMode) =>
        groupMode is "percent" or "fixed" ? groupMode : (teacherMode == "percent" ? "percent" : "fixed");

    /// <summary>Standart davrni orqaga cho'zishning eng uzoq chegarasi (oy) — jadval cheksiz
    /// uzayib ketmasin (masalan xato sana bilan kiritilgan eski maosh to'lovi tufayli).</summary>
    private const int MaxLookbackMonths = 24;

    /// <summary>Standart davr HAR DOIM qamrab oladigan "yaqin tarix" (oy) — o'tgan oy ham shunga
    /// kiradi. Guruh yopilib o'quvchilar arxivlangach o'quv yili boshi joriy oyga sakrab ketardi.</summary>
    private const int RecentMonths = 6;

    /// <summary>Ikki "yyyy-MM" dan ERTAROG'I; bo'sh qiymat "yo'q" degani (ikkinchisi qaytadi).</summary>
    private static string EarlierMonth(string a, string b) =>
        a.Length < 7 ? b : b.Length < 7 ? a : (string.CompareOrdinal(a, b) <= 0 ? a : b);

    /// <summary>Guruhda DARS bo'lishi mumkin bo'lgan oxirgi sana: rejalashtirilgan tugash sanasi
    /// (<see cref="Group.EndDate"/>) va arxivga olingan sana (<see cref="Group.ArchivedAt"/>) dan
    /// ERTAROG'I. Ikkalasi ham bo'sh bo'lsa <c>null</c> (chegara yo'q).</summary>
    public static string? LessonEnd(string? endDate, string? archivedAt)
    {
        var a = endDate is { Length: >= 10 } ? endDate[..10] : null;
        var b = archivedAt is { Length: >= 10 } ? archivedAt[..10] : null;
        if (a is null) return b;
        if (b is null) return a;
        return string.CompareOrdinal(a, b) <= 0 ? a : b;
    }

    /// <param name="withRevenue">
    /// <b>TUSHUM raqamlari</b> (<see cref="MonthSalaryDto.TuitionCharged"/> /
    /// <see cref="MonthSalaryDto.TuitionCollected"/>) HAR DOIM hisoblansinmi — maosh rejimidan
    /// qat'i nazar. Odatda tuition bazasi faqat FOIZLI maosh uchun o'qiladi (qat'iy maoshda u
    /// hisobga kerak emas), lekin admin o'qituvchi kartochkasida "shu oyda guruhlardan qancha
    /// tushum bo'lishi kerak edi va qanchasi tushdi" ko'rsatiladi — qat'iy maoshli o'qituvchida ham.
    /// <para>Standart <c>false</c> ATAYIN: Moliya → "O'qituvchilar" hisoboti bu metodni HAR BIR
    /// o'qituvchi uchun chaqiradi, ya'ni yoqib qo'yilsa qat'iy maoshlilarga ham beshta ortiqcha
    /// so'rov qo'shilardi. O'qituvchi ILOVASI ham <c>false</c> — qat'iy maoshli o'qituvchi ilgari
    /// ko'rmagan markaz tushumini ko'rib qolmasin.</para>
    /// </param>
    public static async Task<SalaryLedgerDto> BuildAsync(
        IAppDbContext db, Teacher teacher, string? from, string? to, bool withRevenue = false)
    {
        // MAOSH QAYSI OYGA TEGISHLI — `Month` maydoni hal qiladi, to'lov SANASI emas:
        // iyul maoshi 5-avgustda berilishi mumkin. `Month` bo'sh bo'lsa (eski yozuvlar,
        // Moliya formasi ilgari uni saqlamasdi) — orqaga moslik uchun sanadan olinadi.
        // Shu sabab so'rovda SANA oralig'i bo'yicha filtrlab bo'lmaydi: kech berilgan
        // to'lov oraliqdan tashqarida qolib ketardi. O'qituvchining maosh to'lovlari kam,
        // shu bois hammasi olinadi va oy bo'yicha keyin filtrlanadi.
        var allPayments = await db.FinanceTransactions
            .Where(t => t.TeacherId == teacher.Id && t.Direction == "expense" && t.Category == "salary")
            .OrderByDescending(t => t.Date).ToListAsync();

        // "yyyy-MM" qaytaradi. Month to'liq bo'lmasa (bo'sh yoki buzuq eski yozuv) — sanadan;
        // ikkalasi ham yaroqsiz bo'lsa "" (bunday to'lov oy hisobiga umuman kirmaydi).
        static string PayMonth(FinanceTransaction t) =>
            t.Month is { Length: >= 7 } m ? m[..7]
            : t.Date is { Length: >= 7 } d ? d[..7]
            : "";

        // O'qituvchi guruhlari + per-guruh maosh sozlamasi.
        // ARXIVLANGAN (yopilgan) guruhlar ham OLINADI — o'tgan oylarda ular uchun maosh hisoblangan
        // va u tarixdan yo'qolmasligi kerak.
        var groups = await db.Classes
            .Where(c => c.TeacherId == teacher.Id)
            .Select(c => new
            {
                c.Id, c.Name, c.CourseId, c.MonthlyFee,
                c.TeacherSalaryMode, c.TeacherSalaryPercent, c.TeacherSalaryFixed,
                c.Days, c.StartDate, c.EndDate, c.ArchivedAt,
            })
            .ToListAsync();

        // Maosh o'quv yili boshidan hisoblanadi (yanvardan emas) — choraklardagi eng erta oydan.
        var fromMonth = string.IsNullOrEmpty(from)
            ? await TuitionService.AcademicYearStartMonthAsync(db) : from[..7];
        var toMonth = string.IsNullOrEmpty(to) ? TuitionService.CurrentMonth() : to[..7];

        if (string.IsNullOrEmpty(from))
        {
            // ⚠️ `AcademicYearStartMonthAsync` = ARXIVLANMAGAN o'quvchilarning eng erta kelgan oyi.
            // Guruh yopilib, o'quvchilari arxivlangach u JORIY oyga sakrab ketardi va o'qituvchining
            // O'TGAN OY qatori jadvaldan umuman tushib qolardi ("o'tgan oy uchun qancha hisoblangani
            // ko'rinmayapti"). Shuning uchun standart davr quyidagilarni HAR DOIM qamrab oladi:
            //   • oxirgi RecentMonths oy (yaqin tarix);
            //   • maosh BERILGAN eng erta oy — aks holda o'sha to'lov "Jami berildi" dan jimgina
            //     tushib qolardi (bu `toMonth` ni oldinga cho'zish bilan bir xil mantiq).
            // Chegara — MaxLookbackMonths (jadval cheksiz uzayib ketmasin).
            // DIQQAT: markaz odatdagidek ishlayotganda `AcademicYearStartMonthAsync` baribir
            // ERTAROQ bo'ladi va bu blok hech narsani o'zgartirmaydi — u faqat "sakrab ketgan"
            // holatni tuzatadi.
            var widen = AppClock.Today.AddMonths(-RecentMonths).ToString("yyyy-MM");
            foreach (var p in allPayments)
                widen = EarlierMonth(widen, PayMonth(p));

            var floor = AppClock.Today.AddMonths(-MaxLookbackMonths).ToString("yyyy-MM");
            if (string.CompareOrdinal(widen, floor) < 0) widen = floor;
            fromMonth = EarlierMonth(fromMonth, widen);
        }

        // O'qituvchi ishga kirgan KUN (yangi maydon yoki eski oy-01). Birinchi oy shu kundan qisman.
        var startDate = TeacherSalaryCalc.StartDateOf(teacher);
        var teacherStartMonth = startDate is { Length: >= 7 } ? startDate[..7] : fromMonth;
        // Oylik o'qituvchi boshlagan oydan hisoblanadi — undan oldingi oylar uchun qarz yozilmaydi.
        var startMonth = string.CompareOrdinal(teacherStartMonth, fromMonth) > 0 ? teacherStartMonth : fromMonth;

        // OLDINDAN berilgan maosh (masalan sentyabr maoshi avgustda) davr oxiridan KEYINGI oyga
        // tegishli bo'lsa — u ko'rinmay qolsa admin ikkinchi marta to'lab yuborishi mumkin edi.
        // Shu sabab oxirgi chegara to'lov oylarini QAMRAB olguncha cho'ziladi (xato kiritilgan
        // oy jadvalni cheksiz uzaytirmasligi uchun 12 oy bilan cheklangan).
        var maxPayMonth = allPayments.Select(PayMonth)
            .Where(m => m.Length == 7 && string.CompareOrdinal(m, toMonth) > 0)
            .DefaultIfEmpty("").Max();
        if (maxPayMonth.Length == 7)
        {
            var cap = TuitionService.MonthRange(toMonth, maxPayMonth).Take(13).Last();
            toMonth = cap;
        }

        var payments = allPayments
            .Where(t =>
            {
                var m = PayMonth(t);
                return m.Length == 7
                       && string.CompareOrdinal(m, startMonth) >= 0
                       && string.CompareOrdinal(m, toMonth) <= 0;
            })
            .ToList();

        var paidByMonth = payments
            .GroupBy(PayMonth)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        // Maosh jurnalga bog'langanmi? (Guruhlar → "Jurnal boshqaruvi"). Yoqilgan bo'lsa — belgilanmagan
        // darslar o'tilmagan hisoblanadi va shu oy maoshidan ushlanadi.
        var policy = await JournalPolicy.GetAsync(db);
        var journalLinked = policy.SalaryRequireJournal;
        // ⚠️ GURUH YOPILGANDAN KEYIN DARS REJALASHTIRILMAYDI. Rejadagi darslar guruh hafta
        // kunlaridan chiqariladi va "belgilanmagan dars = o'tilmagan" deb maoshdan ushlanadi.
        // Yopilgan/arxivlangan guruhda esa dars umuman bo'lmaydi — o'qituvchi jurnalda hech narsa
        // belgilay olmaydi (guruh unga ko'rinmaydi ham, `TeacherGroupAccess`). Chegara faqat
        // `EndDate` bo'yicha olinardi va u "Arxivlash" yo'lida QO'YILMAYDI (faqat "Yopish" qo'yadi)
        // — natijada arxivdan keyingi barcha "rejadagi" darslar o'tkazib yuborilgan hisoblanib,
        // ushlanma butun oylikni yeb qo'yardi (oy qatori 0 bo'lib qolardi).
        // Endi chegara = EndDate va ArchivedAt dan ERTAROG'I.

        // =========================================================================================
        //  O'RINBOSARLIK — nol yig'indili model uchun kerakli hamma narsa BIR JOYDA yuklanadi
        // =========================================================================================
        // ⚠️ J14: Moliya → "O'qituvchilar" hisoboti bu metodni HAR BIR o'qituvchi uchun sikl ichida
        // chaqiradi. Ilgari bu yerda 4 ta SHARTSIZ so'rov turardi (N×4 aylanish, jadvalda indeks
        // ham yo'q). Endi avval BITTA yengil `AnyAsync`: markazda o'rinbosarlik umuman ishlatilmasa
        // (odatiy hol) qolgan so'rovlarning hech biri ketmaydi.
        var groupIdsList = groups.Select(g => g.Id).ToList();
        var hasSubstitutions = await db.SubstituteTeacherAssignments.AsNoTracking()
            .AnyAsync(a => a.IsActive
                && (a.SubstituteTeacherId == teacher.Id
                    || (a.OriginalTeacherId == teacher.Id && groupIdsList.Contains(a.GroupId))));

        // teacher = O'RINBOSAR (begona guruhda dars o'tgan) → unga HAQ qo'shiladi.
        var subAssignments = new List<SubstituteTeacherAssignment>();
        // teacher = ASOSIY o'qituvchi (uning guruhida boshqa odam dars o'tgan) → undan USHLANADI.
        var origSubstitutions = new List<SubstituteTeacherAssignment>();
        // ⚠️ O17: guruhlar TO'LIQ entity sifatida yuklanadi. Ilgari `new Group { … }` bilan qisman
        // to'ldirilardi va `ArchivedAt`/`EndDate`/`StartDate` tashlanib ketardi — arxivlangan
        // guruhda ham "dars bor" deb sanalib, pul to'lanaverardi.
        var subGroups = new Dictionary<string, Group>();
        var subMoves = new Dictionary<string, IReadOnlyList<JournalService.LessonMove>>();
        // (guruh, oy) → bitta dars narxi. IKKALA tomon ham AYNAN shu jadvaldan o'qiydi —
        // nol yig'indililik shu bilan STRUKTURAVIY kafolatlanadi.
        var subRates = new Dictionary<(string GroupId, string Month), SubstituteTeacherService.PerLessonResult>();
        // Guruh → o'rinbosar QAMRAGAN sanalar (asosiy o'qituvchining jurnal rejasidan chiqariladi).
        var substitutedDates = new Dictionary<string, HashSet<string>>();

        if (hasSubstitutions)
        {
            subAssignments = await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => a.SubstituteTeacherId == teacher.Id && a.IsActive)
                .ToListAsync();

            origSubstitutions = await db.SubstituteTeacherAssignments.AsNoTracking()
                .Where(a => groupIdsList.Contains(a.GroupId) && a.OriginalTeacherId == teacher.Id && a.IsActive)
                .ToListAsync();

            var involvedGroupIds = subAssignments.Select(a => a.GroupId)
                .Concat(origSubstitutions.Select(a => a.GroupId)).Distinct().ToList();

            subGroups = await db.Classes.AsNoTracking()
                .Where(c => involvedGroupIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            subMoves = await SubstituteTeacherService.MovesByGroupAsync(db, involvedGroupIds);

            var keys = new HashSet<(string, string)>();
            foreach (var a in subAssignments.Concat(origSubstitutions))
            {
                var g = subGroups.GetValueOrDefault(a.GroupId);
                var mv = subMoves.GetValueOrDefault(a.GroupId);
                foreach (var m in SubstituteTeacherService.MonthsOf(a, g, mv))
                    keys.Add((a.GroupId, m));
            }
            subRates = await SubstituteTeacherService.PerLessonBatchAsync(db, keys);

            foreach (var a in origSubstitutions)
            {
                var g = subGroups.GetValueOrDefault(a.GroupId);
                var mv = subMoves.GetValueOrDefault(a.GroupId);
                if (!substitutedDates.TryGetValue(a.GroupId, out var set))
                    substitutedDates[a.GroupId] = set = new HashSet<string>();
                foreach (var d in SubstituteTeacherService.EffectiveDates(a, g, mv)) set.Add(d);
            }
        }

        var lessonStats = journalLinked && groups.Count > 0
            ? await SalaryJournalStats.BuildAsync(db,
                groups.Select(g => new SalaryJournalStats.GroupInfo(
                    g.Id, g.Name, g.Days, g.StartDate, LessonEnd(g.EndDate, g.ArchivedAt))).ToList(),
                startMonth, toMonth, policy.SalaryGraceDays, startDate,
                // J10: o'rinbosar o'tgan dars asosiy o'qituvchining rejasidan CHIQARILADI —
                // aks holda u bitta dars uchun ikki marta jarimalanardi.
                substitutedDates.Count > 0 ? substitutedDates : null)
            : new Dictionary<(string Month, string GroupId), SalaryJournalStats.Stat>();

        // Kamida bitta guruh per-guruh sozlangan bo'lsa — YIG'INDI (per-guruh) hisob; aks holda LEGACY.
        var anyConfigured = groups.Any(g => g.TeacherSalaryMode is "percent" or "fixed");
        // Foizli ulush bo'lsa (legacy yoki per-guruh) — yig'ilgan to'lov bazasi kerak.
        var anyPercent = teacher.SalaryMode == "percent" || groups.Any(g => g.TeacherSalaryMode == "percent");
        // `withRevenue` bo'lsa QAT'IY maoshda ham o'qiladi — tushum raqamlari maosh rejimiga
        // bog'liq emas (hisobga ta'sir qilmaydi, faqat ko'rsatiladi).
        var bases = (groups.Count > 0 && (anyPercent || withRevenue))
            ? await PercentBasesAsync(db, groupIdsList, startMonth, toMonth)
            : new PercentBases(new(), new(), new(), null);
        var collectedPerGroup = bases.Collected;
        var chargedPerGroup = bases.Charged;
        var salaryBasePerGroup = bases.SalaryBase;

        decimal TotalCollected(string month) =>
            collectedPerGroup.Where(kv => kv.Key.month == month).Sum(kv => kv.Value);
        decimal TotalCharged(string month) =>
            chargedPerGroup.Where(kv => kv.Key.month == month).Sum(kv => kv.Value);
        decimal TotalSalaryBase(string month) =>
            salaryBasePerGroup.Where(kv => kv.Key.month == month).Sum(kv => kv.Value);

        // Kurs nomlari (breakdown uchun).
        var courseIds = groups.Where(g => !string.IsNullOrEmpty(g.CourseId)).Select(g => g.CourseId).Distinct().ToList();
        var courseNames = (await db.Subjects.Where(s => courseIds.Contains(s.Id)).ToListAsync())
            .ToDictionary(s => s.Id, s => s.Name);

        // Legacy plan oyligi (eski UI uchun ishora).
        var plannedMonthly = teacher.SalaryMode == "percent" ? 0m : teacher.Salary;

        var groupPeriodExpected = groups.ToDictionary(g => g.Id, _ => 0m);
        var groupPeriodCollected = groups.ToDictionary(g => g.Id, _ => 0m);
        var groupPeriodSubDeduction = groups.ToDictionary(g => g.Id, _ => 0m);
        var groupPeriodSubLessons = groups.ToDictionary(g => g.Id, _ => 0);

        // (guruh, oy) uchun bitta dars narxi — YAGONA hisoblagichdan.
        decimal PerLesson(string gid, string month) =>
            subRates.GetValueOrDefault((gid, month))?.PerLessonFee ?? 0m;

        var months = new List<MonthSalaryDto>();
        foreach (var month in TuitionService.MonthRange(startMonth, toMonth))
        {
            // Birinchi (ishga kirgan) oy QISMAN — qat'iy summalarga shu nisbat qo'llanadi.
            decimal factor = 1m;
            if (startDate is { Length: >= 10 } && startDate[..7] == month
                && DateOnly.TryParse(startDate, out var sd))
            {
                var dim = DateTime.DaysInMonth(sd.Year, sd.Month);
                factor = (decimal)(dim - sd.Day + 1) / dim;
            }

            // ---------- O'RINBOSARLIK HAQI (teacher = O'RINBOSAR) ----------
            // ⚠️ Tayinlov FAQAT boshlangan oyiga emas, TEGADIGAN HAR OYGA taqsimlanadi
            // (25-avgust — 5-sentabr tayinlovi ilgari butunlay avgustga tushib, sentabr maoshida
            // umuman ko'rinmasdi). Qoida yagona: SubstituteTeacherService.LessonsInMonth.
            // ⚠️ NARX ham yagona (`subRates`) — asosiy o'qituvchidan AYNAN shu narx bilan
            // ushlanadi, ya'ni model NOL YIG'INDILI.
            decimal monthSubstituteFee = 0m;
            foreach (var sub in subAssignments)
            {
                var g = subGroups.GetValueOrDefault(sub.GroupId);
                var mv = subMoves.GetValueOrDefault(sub.GroupId);
                var lessons = SubstituteTeacherService.LessonsInMonth(sub, g, month, mv);
                if (lessons <= 0) continue;
                monthSubstituteFee += Math.Round(PerLesson(sub.GroupId, month) * lessons, 2);
            }
            monthSubstituteFee = Math.Round(monthSubstituteFee, 2);

            decimal monthSubstituteDeduction = 0m;

            // Har guruhning shu oydagi ulushi (breakdown + per-guruh yig'indisi uchun doim hisoblanadi).
            decimal grossSum = 0m, groupDeduction = 0m;
            decimal monthCollected = 0m;
            decimal monthCharged = 0m, grossPotential = 0m;
            decimal monthSalaryBase = 0m;
            var lessonLines = new List<SalaryLessonStatDto>();
            int plannedTotal = 0, conductedTotal = 0;

            foreach (var g in groups)
            {
                var mode = EffMode(g.TeacherSalaryMode, teacher.SalaryMode);
                decimal contribution, potential;
                if (mode == "percent")
                {
                    var pct = g.TeacherSalaryMode == "percent" ? g.TeacherSalaryPercent : teacher.SalaryPercent;
                    var col = collectedPerGroup.GetValueOrDefault((month, g.Id), 0m);
                    var chg = chargedPerGroup.GetValueOrDefault((month, g.Id), 0m);
                    // Baza oyga qarab: hisoblangan to'liq oylik (CenterMeta.SalaryChargedBaseFrom dan) yoki yig'ilgan.
                    var salaryBase = salaryBasePerGroup.GetValueOrDefault((month, g.Id), 0m);
                    contribution = decimal.Round(salaryBase * pct / 100m, 2);
                    // "Hammasi to'lansa" — hisoblangan bazada maoshning O'ZI (pul kelishiga bog'liq emas).
                    potential = UsesChargedBase(month, bases.ChargedFrom) ? contribution : decimal.Round(chg * pct / 100m, 2);
                    groupPeriodCollected[g.Id] += col;
                    monthCollected += col;
                    monthSalaryBase += salaryBase;
                    monthCharged += chg;
                }
                else
                {
                    var amt = g.TeacherSalaryMode == "fixed" ? g.TeacherSalaryFixed : 0m;
                    contribution = decimal.Round(amt * factor, 2);
                    potential = contribution;
                }

                // ---------- ASOSIY O'QITUVCHIDAN USHLANMA (teacher = ASOSIY) ----------
                // Shu OYGA tushgan o'rinbosarlik darslari (tayinlov boshlangan oy MUHIM EMAS).
                // ⚠️ Narx — AYNAN o'rinbosarga to'lanadigan narx (`subRates`), ya'ni ushlangan
                // summa to'langan summaga TENG (nol yig'indili). Ilgari bu yerda ayrim formula
                // turardi: o'rinbosarga "hisoblangan × foiz" dan to'lanib, asosiydan "yig'ilgan ×
                // foiz" dan ushlanardi va farqni markaz to'lardi.
                if (subGroups.ContainsKey(g.Id))
                {
                    var fullGroup = subGroups[g.Id];
                    var mv = subMoves.GetValueOrDefault(g.Id);
                    int subLessons = origSubstitutions
                        .Where(a => a.GroupId == g.Id)
                        .Sum(a => SubstituteTeacherService.LessonsInMonth(a, fullGroup, month, mv));
                    if (subLessons > 0)
                    {
                        decimal subDeduction = Math.Round(PerLesson(g.Id, month) * subLessons, 2);
                        if (subDeduction > 0)
                        {
                            monthSubstituteDeduction += subDeduction;
                            groupPeriodSubDeduction[g.Id] += subDeduction;
                            groupPeriodSubLessons[g.Id] += subLessons;

                            // Guruh ulushi bor rejimlarda (per-guruh va legacy-foiz) ushlanma AYNAN
                            // shu guruh ulushidan ayriladi. Legacy-QAT'IYda guruh ulushi umuman
                            // yo'q (contribution == 0) — u yerda ushlanma OY BAZASIDAN ayriladi
                            // (pastda). ⚠️ Ilgari `if (contribution > 0)` sharti tufayli
                            // legacy-qat'iyda ushlanma HECH QACHON qo'llanmas, lekin ekranga
                            // "ushlandi" deb yuborilardi.
                            contribution = Math.Max(0m, contribution - subDeduction);
                            potential = Math.Max(0m, potential - subDeduction);
                        }
                    }
                }

                grossPotential += potential;

                var stat = lessonStats.GetValueOrDefault((month, g.Id));
                decimal ded = 0m;
                if (journalLinked && stat is not null)
                {
                    plannedTotal += stat.Planned;
                    conductedTotal += stat.Conducted;
                    if (contribution > 0 && stat.Missed > 0)
                        ded = decimal.Round(contribution * stat.Missed / stat.Planned, 2);
                }

                grossSum += contribution;
                groupDeduction += ded;
                groupPeriodExpected[g.Id] += contribution - ded;
                if (journalLinked && stat is not null)
                    lessonLines.Add(new SalaryLessonStatDto(
                        g.Id, g.Name, stat.Planned, stat.Conducted, stat.Missed, ded, stat.MissedDates));
            }

            decimal baseExpected, basePotential;
            if (anyConfigured)
            {
                // Per-guruh: ushlanma allaqachon HAR GURUH ulushidan ayrilgan (yuqorida).
                baseExpected = decimal.Round(grossSum, 2);
                basePotential = decimal.Round(grossPotential, 2);
            }
            else if (teacher.SalaryMode == "percent")
            {
                baseExpected = decimal.Round(TotalSalaryBase(month) * teacher.SalaryPercent / 100m, 2);
                basePotential = UsesChargedBase(month, bases.ChargedFrom)
                    ? baseExpected
                    : decimal.Round(TotalCharged(month) * teacher.SalaryPercent / 100m, 2);
                // ⚠️ LEGACY-FOIZ baza guruhlar ulushidan EMAS, oyning JAMI yig'ilganidan chiqadi
                // — ya'ni sikl ichida ayrilgan ushlanma bu raqamga UMUMAN ta'sir qilmagan. Ilgari
                // shu sabab ushlanma hisoblanib, ekranga yuborilib, lekin maoshdan AYRILMASDI.
                baseExpected = Math.Max(0m, baseExpected - monthSubstituteDeduction);
                basePotential = Math.Max(0m, basePotential - monthSubstituteDeduction);
            }
            else
            {
                // ⚠️ LEGACY-QAT'IYda guruh ulushi umuman yo'q (contribution == 0), shuning uchun
                // ushlanma AYNAN shu yerda — yakuniy bazadan — ayriladi.
                baseExpected = decimal.Round(teacher.Salary * factor, 2);
                basePotential = baseExpected;
                baseExpected = Math.Max(0m, baseExpected - monthSubstituteDeduction);
                basePotential = Math.Max(0m, basePotential - monthSubstituteDeduction);
            }

            // ⚠️ J9: JURNAL JARIMASINING MAXRAJI — o'rinbosarlik haqi QO'SHILMAGAN baza.
            // O'rinbosarlik haqi BOSHQA guruhlarda o'tilgan darslar uchun; uni o'qituvchining
            // O'Z guruhlaridagi "belgilanmagan dars" jarimasi maxrajiga qo'shish jarimani
            // sun'iy ravishda kattalashtirardi (`.claude/rules/billing.md`).
            var ownWorkBase = baseExpected;

            // O'rinbosarlik haqi maosh bazasiga qo'shiladi (guruhlar ulushidan KEYIN).
            baseExpected += monthSubstituteFee;
            basePotential += monthSubstituteFee;

            // Ushlanma: per-guruh ulushi bor bo'lsa (per-guruh yoki legacy foiz) — guruhlar bo'yicha yig'indi;
            // legacy QAT'IY oylikda guruh ulushi yo'q, shuning uchun bitta dars narxi = oylik ÷ rejadagi darslar.
            decimal deduction = 0m;
            if (journalLinked && plannedTotal > 0)
            {
                if (anyConfigured || teacher.SalaryMode == "percent")
                {
                    deduction = decimal.Round(groupDeduction, 2);
                }
                else if (ownWorkBase > 0)
                {
                    var perLesson = ownWorkBase / plannedTotal;
                    for (var i = 0; i < lessonLines.Count; i++)
                    {
                        var line = lessonLines[i];
                        var ded = decimal.Round(perLesson * line.Missed, 2);
                        lessonLines[i] = line with { Deduction = ded };
                        deduction += ded;
                    }
                    if (deduction > ownWorkBase) deduction = ownWorkBase;  // yaxlitlash himoyasi
                }
            }

            var expected = baseExpected - deduction;
            var paid = paidByMonth.GetValueOrDefault(month, 0m);
            var remaining = expected - paid;
            var status = remaining <= 0 ? (expected <= 0 ? "unpaid" : "paid") : paid > 0 ? "partial" : "unpaid";
            // Potentsial maoshdan ham o'sha ushlanma ayriladi — aks holda "hammasi to'lansa" raqami
            // jurnal ushlanmasini hisobga olmay, haqiqiydan katta ko'rinardi.
            var potentialExpected = basePotential - deduction;
            if (potentialExpected < expected) potentialExpected = expected;   // yig'ilgan hisoblangandan oshib ketgan holat

            months.Add(new MonthSalaryDto(
                month, expected, paid, remaining, status,
                baseExpected, deduction,
                plannedTotal, conductedTotal, plannedTotal - conductedTotal,
                journalLinked ? lessonLines : null,
                decimal.Round(monthCollected, 2),
                decimal.Round(monthCharged, 2), decimal.Round(potentialExpected, 2),
                decimal.Round(TotalCharged(month), 2),
                decimal.Round(TotalCollected(month), 2),
                monthSubstituteFee,
                decimal.Round(monthSubstituteDeduction, 2),
                // Legacy-foizda baza oyning JAMI bazasidan (guruh ulushlaridan emas).
                decimal.Round(!anyConfigured && teacher.SalaryMode == "percent" ? TotalSalaryBase(month) : monthSalaryBase, 2),
                UsesChargedBase(month, bases.ChargedFrom)));
        }

        var totalExpected = months.Sum(m => m.Expected);
        var totalPaid = payments.Sum(p => p.Amount);
        var paymentDtos = payments.Select(t => new PaymentDto(t.Date, t.Amount, t.Note, t.Month, t.Comment)).ToList();

        var groupLines = groups.Select(g => new GroupSalaryLineDto(
            g.Id, g.Name,
            string.IsNullOrEmpty(g.CourseId) ? "" : courseNames.GetValueOrDefault(g.CourseId, ""),
            g.MonthlyFee,
            EffMode(g.TeacherSalaryMode, teacher.SalaryMode),
            g.TeacherSalaryMode == "percent" ? g.TeacherSalaryPercent
                : g.TeacherSalaryMode == "fixed" ? 0m : teacher.SalaryPercent,
            g.TeacherSalaryMode == "fixed" ? g.TeacherSalaryFixed : 0m,
            decimal.Round(groupPeriodCollected.GetValueOrDefault(g.Id, 0m), 2),
            decimal.Round(groupPeriodExpected.GetValueOrDefault(g.Id, 0m), 2),
            decimal.Round(groupPeriodSubDeduction.GetValueOrDefault(g.Id, 0m), 2),
            groupPeriodSubLessons.GetValueOrDefault(g.Id, 0)
        )).ToList();

        return new SalaryLedgerDto(
            teacher.Id, teacher.FullName, plannedMonthly,
            totalExpected, totalPaid, totalExpected - totalPaid,
            months, paymentDtos, teacher.SalaryMode, teacher.SalaryPercent, groupLines,
            months.Sum(m => m.Deduction), journalLinked);
    }

    /// <summary>
    /// Foizli maosh bazalari PER-GURUH — IKKITA xarita: (oy, guruh) → <b>yig'ilgan</b> pul va
    /// (oy, guruh) → <b>hisoblangan</b> (qarz bilan) summa. Maosh yig'ilgandan hisoblanadi;
    /// hisoblangan esa "hammasi to'lansa qancha bo'lardi" raqami uchun (guruh yopilib pul hali
    /// kelmagan oyda maosh 0 bo'lib ko'rinmasin).
    /// O'quvchi bir nechta guruhda bo'lsa, TEGLANMAGAN to'lovi (va hisobi) guruhlar
    /// oylik narxi (MonthlyFee) nisbatida taqsimlanadi — har guruhga o'z ulushi. TEGLANGAN to'lov
    /// 100% o'sha guruhga. Trial/muzlatilgan a'zoliklar shu oyda hisobga olinmaydi.
    ///
    /// <para><b>TO'LOV QAYSI OYGA TEGISHLI — <see cref="FinanceTransaction.Month"/>, to'lov SANASI EMAS.</b>
    /// O'quvchi 3-avgustda IYUL uchun to'lasa, o'sha pul o'qituvchining IYUL maoshiga kiradi — chunki
    /// o'qituvchi iyulda dars bergan. Bu markazdagi boshqa hamma joy bilan bir xil konvensiya
    /// (<see cref="StudentGroupLedger"/>, <see cref="GroupBalanceService"/>,
    /// <see cref="CourseFinanceReport"/> va shu faylning O'ZIDAGI maosh to'lovlari — <c>PayMonth</c>).
    /// ⚠️ Ilgari BU YERDA to'lov SANASI ishlatilardi va o'qituvchi profilida bitta qatorning ikki
    /// yarmi turlicha hisoblanardi: "iyul uchun berilgan maosh" — <c>Month</c> bo'yicha, "iyulda
    /// yig'ilgan" esa SANA bo'yicha. Shu sabab raqamlar tushunarsiz chiqardi.</para>
    ///
    /// <para><c>Month</c> bo'sh bo'lgan ESKI yozuvlarda (Moliya formasi uni ilgari saqlamasdi) orqaga
    /// moslik uchun sana ishlatiladi. So'rov shu sabab IKKI shart bilan: oyi mos yoki (oyi yo'q va)
    /// sanasi oraliqda — aks holda kech to'langan pul umuman tushib qolardi.</para>
    /// </summary>
    /// <param name="Collected">(oy, guruh) → SHU OY UCHUN haqiqatan YIG'ILGAN pul (vozvrat ayrilgan).
    /// Foizli maosh AYNAN shundan hisoblanadi.</param>
    /// <param name="Charged">(oy, guruh) → SHU OY UCHUN o'quvchilarga HISOBLANGAN summa (chegirma
    /// ayrilgan; pul kelmagan bo'lsa ham). "Hammasi to'lansa maosh qancha bo'lardi" raqami shundan.</param>
    /// <param name="SalaryBase">(oy, guruh) → FOIZ QO'LLANADIGAN baza: <see cref="CenterMeta.SalaryChargedBaseFrom"/>
    /// dan oldin — <paramref name="Collected"/>, undan boshlab — hisoblangan TO'LIQ oylik (chegirmasiz).</param>
    private readonly record struct PercentBases(
        Dictionary<(string month, string groupId), decimal> Collected,
        Dictionary<(string month, string groupId), decimal> Charged,
        Dictionary<(string month, string groupId), decimal> SalaryBase,
        string? ChargedFrom);

    /// <summary>
    /// (oy, guruh) → SHU OY UCHUN yig'ilgan pul — <b>o'rinbosarlik hovuzining bazasi</b>.
    ///
    /// <para>⚠️ NEGA OMMAVIY: o'rinbosarning maosh varaqasini hisoblashda u O'ZI O'QITMAYDIGAN
    /// (begona) guruhning yig'ilgan puli kerak bo'ladi — nol yig'indili modelda ikkala tomon ham
    /// AYNAN shu bazadan chiqadi. Taqsimot qoidasi (teglangan to'lov 100% guruhga, teglanmagani
    /// <c>MonthlyFee</c> nisbatida) shu sabab nusxalanmaydi: manba bitta.</para>
    /// </summary>
    /// <remarks>⚠️ Nomiga qaramay endi FOIZ BAZASINI qaytaradi (<see cref="CenterMeta.SalaryChargedBaseFrom"/> dan
    /// boshlab hisoblangan oylik) — o'rinbosar va asosiy o'qituvchi AYNAN bitta bazadan hisoblansin.</remarks>
    public static async Task<Dictionary<(string month, string groupId), decimal>> CollectedForGroupsAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, string startMonth, string toMonth) =>
        (await PercentBasesAsync(db, groupIds, startMonth, toMonth)).SalaryBase;

    private static async Task<PercentBases> PercentBasesAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, string startMonth, string toMonth)
    {
        var result = new Dictionary<(string month, string groupId), decimal>();
        var charged = new Dictionary<(string month, string groupId), decimal>();
        var gross = new Dictionary<(string month, string groupId), decimal>();
        if (groupIds.Count == 0) return new PercentBases(result, charged, new(), null);
        var chargedFrom = await db.CenterMeta.AsNoTracking()
            .Select(m => m.SalaryChargedBaseFrom).FirstOrDefaultAsync();

        void Add(string month, string groupId, decimal amount) =>
            result[(month, groupId)] = result.GetValueOrDefault((month, groupId), 0m) + amount;
        void AddCharge(string month, string groupId, decimal amount) =>
            charged[(month, groupId)] = charged.GetValueOrDefault((month, groupId), 0m) + amount;
        void AddGross(string month, string groupId, decimal amount) =>
            gross[(month, groupId)] = gross.GetValueOrDefault((month, groupId), 0m) + amount;

        // So'ralgan guruhlar (id → oylik narx). ARXIVLANGANLARI HAM — yopilgan guruhning
        // o'tgan oylardagi hisobi tarixdan yo'qolmasin.
        var teacherGroups = await db.Classes
            .Where(c => groupIds.Contains(c.Id))
            .Select(c => new { c.Id, c.MonthlyFee }).ToListAsync();
        if (teacherGroups.Count == 0) return new PercentBases(result, charged, new(), chargedFrom);
        var tgIds = teacherGroups.Select(g => g.Id).ToHashSet();

        // Shu guruhlardagi o'quvchilar.
        var studentIds = await db.StudentGroups
            .Where(sg => tgIds.Contains(sg.GroupId))
            .Select(sg => sg.StudentId).Distinct().ToListAsync();
        if (studentIds.Count == 0) return new PercentBases(result, charged, new(), chargedFrom);

        // Bu o'quvchilarning BARCHA a'zoliklari (taqsimlash maxraji uchun boshqa guruhlari ham kerak).
        var memberships = await db.StudentGroups
            .Where(sg => studentIds.Contains(sg.StudentId)).ToListAsync();
        var allGroupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var feeByGroup = (await db.Classes.Where(c => allGroupIds.Contains(c.Id)).ToListAsync())
            .ToDictionary(c => c.Id, c => c.MonthlyFee);

        // Tuition to'lovlari (kirim, o'quvchi) — GroupId tegi bilan. VOZVRAT (expense+refund) MANFIY qo'shiladi:
        // YIG'ILGAN baza net (to'langan − vozvrat). ⚠️ Faqat `SalaryChargedBaseFrom` dan OLDINGI oylar maoshi
        // shundan; keyingi oylarda baza hisoblangan oylik — vozvrat unga ta'sir qilmaydi (foydalanuvchi qarori).
        var fromDate = $"{startMonth}-01";
        var toDate = $"{toMonth}-31";
        // Davr oylari — to'lovning `Month` tegi shu ro'yxatda bo'lsa hisobga olinadi (CourseFinanceReport
        // bilan AYNAN bir xil filtr). `Month`i yo'q eski yozuvlar sana bo'yicha tutiladi.
        var monthList = TuitionService.MonthRange(startMonth, toMonth).ToList();
        var monthSet = monthList.ToHashSet();
        var movements = await db.FinanceTransactions
            .Where(t => t.StudentId != null && studentIds.Contains(t.StudentId)
                        && ((t.Direction == "income" && t.Category == "tuition")
                            || (t.Direction == "expense" && t.Category == "refund"))
                        && ((t.Month != null && t.Month != "" && monthList.Contains(t.Month))
                            || ((t.Month == null || t.Month == "")
                                && string.Compare(t.Date, fromDate) >= 0
                                && string.Compare(t.Date, toDate) <= 0)))
            .Select(t => new { StudentId = t.StudentId!, t.GroupId, t.Date, t.Month, t.Amount, t.Direction }).ToListAsync();
        // Vozvrat manfiy belgi bilan: yig'ilgan bazani kamaytiradi.
        // To'lov QAYSI OYGA tegishli: `Month` tegi (yo'q bo'lsa — sana oyi, eski yozuvlar uchun).
        var payments = movements
            .Select(t => new
            {
                t.StudentId, t.GroupId,
                Month = string.IsNullOrEmpty(t.Month)
                    ? (t.Date.Length >= 7 ? t.Date[..7] : "")
                    : (t.Month.Length >= 7 ? t.Month[..7] : t.Month),
                Amount = t.Direction == "expense" ? -t.Amount : t.Amount,
            })
            .Where(t => monthSet.Contains(t.Month))
            .ToList();

        // TEGLANGAN to'lovlar (GroupId bor) — 100% o'sha guruhga; faqat o'qituvchi guruhi hisobga olinadi.
        // O'quvchi → oy → TEGLANMAGAN net summasi (narx nisbatida taqsimlanadi; vozvratdan keyin manfiy ham bo'lishi mumkin).
        var untaggedByStudentMonth = new Dictionary<(string, string), decimal>();

        var membsByStudent = memberships.GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Ta'rif MembershipLifecycle da — ushlab turish bonusi ham AYNAN shuni ishlatadi
        // (nusxa qilinsa, vaqt o'tib maosh va bonus bir oyni turlicha hisoblab qolardi).
        static bool BillableInMonth(StudentGroup m, string month) =>
            MembershipLifecycle.BillableInMonth(m, month);

        foreach (var p in payments)
        {
            if (p.Amount == 0m) continue;   // vozvrat manfiy — o'tkazib yubormaymiz
            var month = p.Month;
            if (!string.IsNullOrEmpty(p.GroupId))
            {
                // Teglangan: faqat o'qituvchi guruhiga tegishli bo'lsa, 100% o'sha guruhga.
                if (tgIds.Contains(p.GroupId))
                    Add(month, p.GroupId, p.Amount);
            }
            else
            {
                var key = (p.StudentId, month);
                untaggedByStudentMonth[key] = untaggedByStudentMonth.GetValueOrDefault(key, 0m) + p.Amount;
            }
        }

        // Teglanmagan to'lovlarni narx (MonthlyFee) nisbatida o'qituvchi guruh(lar)iga taqsimlaymiz.
        foreach (var ((sid, month), collected) in untaggedByStudentMonth)
        {
            if (collected == 0m || !membsByStudent.TryGetValue(sid, out var membs)) continue;
            var active = membs.Where(m => BillableInMonth(m, month)).ToList();
            var denom = active.Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
            if (denom <= 0) continue; // shu oyda billable guruh yo'q — taqsimlab bo'lmaydi.
            foreach (var m in active.Where(m => tgIds.Contains(m.GroupId)))
            {
                var fee = feeByGroup.GetValueOrDefault(m.GroupId, 0m);
                if (fee <= 0) continue;
                Add(month, m.GroupId, collected * fee / denom);
            }
        }

        // ---------- HISOBLANGAN (qarz bilan) — "hammasi to'lansa" bazasi ----------
        // Manba: `MonthlyCharge` (o'quvchiga oy uchun yozilgan hisob; chegirma alohida ustunda).
        // Teglangan qator (GroupId bor) 100% o'sha guruhga, teglanmagan (eski ClassName billing)
        // esa to'lovlar bilan AYNAN bir xil qoida bo'yicha narx nisbatida taqsimlanadi.
        var chargeRows = await db.MonthlyCharges
            .Where(c => studentIds.Contains(c.StudentId) && monthList.Contains(c.Month))
            .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount })
            .ToListAsync();

        var untaggedChargeByStudentMonth = new Dictionary<(string, string), (decimal Eff, decimal Gross)>();
        foreach (var c in chargeRows)
        {
            var eff = c.Amount - c.Discount;
            // TO'LIQ (chegirmasiz) summa — CenterMeta.SalaryChargedBaseFrom dan boshlab foiz AYNAN shundan:
            // chegirmani markaz ko'taradi, o'qituvchi ulushi kamaymaydi.
            var full = c.Amount;
            if (!string.IsNullOrEmpty(c.GroupId))
            {
                if (!tgIds.Contains(c.GroupId)) continue;
                if (eff > 0) AddCharge(c.Month, c.GroupId, eff);   // 100% chegirma — "potensial"ga kirmaydi
                if (full > 0) AddGross(c.Month, c.GroupId, full);
            }
            else
            {
                var key = (c.StudentId, c.Month);
                var cur = untaggedChargeByStudentMonth.GetValueOrDefault(key);
                untaggedChargeByStudentMonth[key] = (cur.Eff + Math.Max(0m, eff), cur.Gross + Math.Max(0m, full));
            }
        }

        foreach (var ((sid, month), amount) in untaggedChargeByStudentMonth)
        {
            if ((amount.Eff == 0m && amount.Gross == 0m) || !membsByStudent.TryGetValue(sid, out var membs)) continue;
            var active = membs.Where(m => BillableInMonth(m, month)).ToList();
            var denom = active.Sum(m => feeByGroup.GetValueOrDefault(m.GroupId, 0m));
            if (denom <= 0) continue;
            foreach (var m in active.Where(m => tgIds.Contains(m.GroupId)))
            {
                var fee = feeByGroup.GetValueOrDefault(m.GroupId, 0m);
                if (fee <= 0) continue;
                if (amount.Eff != 0m) AddCharge(month, m.GroupId, amount.Eff * fee / denom);
                if (amount.Gross != 0m) AddGross(month, m.GroupId, amount.Gross * fee / denom);
            }
        }

        // Yaxlitlash.
        foreach (var key in result.Keys.ToList())
            result[key] = decimal.Round(result[key], 2);
        foreach (var key in charged.Keys.ToList())
            charged[key] = decimal.Round(charged[key], 2);

        // Foiz bazasi: oyga qarab yig'ilgan yoki hisoblangan to'liq oylik.
        var salaryBase = new Dictionary<(string month, string groupId), decimal>();
        foreach (var kv in result.Where(kv => !UsesChargedBase(kv.Key.month, chargedFrom)))
            salaryBase[kv.Key] = kv.Value;
        foreach (var kv in gross.Where(kv => UsesChargedBase(kv.Key.month, chargedFrom)))
            salaryBase[kv.Key] = decimal.Round(kv.Value, 2);
        return new PercentBases(result, charged, salaryBase, chargedFrom);
    }
}
