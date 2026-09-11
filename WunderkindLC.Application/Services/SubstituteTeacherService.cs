using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'RINBOSAR O'QITUVCHILAR — tayinlov mantig'ining <b>yagona joyi</b>: kirish huquqi (qaysi kunda
/// begona guruh jurnaliga kira oladi), dars sonini oyga taqsimlash va bitta dars narxi.
///
/// <para><b>YAGONA HAQIQAT MANBAI — <see cref="SubstituteTeacherAssignment.SelectedDates"/>.</b>
/// Tayinlov qaysi yo'l bilan yaratilgan bo'lmasin (kalendardan sanalar tanlab yoki
/// <c>Date</c>..<c>EndDate</c> oralig'i bilan), yaratishda SelectedDates guruhning HAQIQIY dars
/// kunlari bilan to'ldiriladi. Sabab: dars soni, pul, kirish huquqi va kesishuv tekshiruvi —
/// to'rttasi ham AYNAN bir xil sanalar to'plamiga tayanishi kerak. Ilgari ular uch xil edi:
/// kirish huquqi ORALIQ bo'yicha berilardi (5 va 20-avgust tanlansa, oradagi 14 kun ham ochilardi),
/// dars soni esa oraliqdan yaratilganda har doim 2 chiqardi.</para>
///
/// <para><b>PUL — NOL YIG'INDILI.</b> O'rinbosarga to'lanadigan summa asosiy o'qituvchidan AYNAN
/// o'shancha ushlanadi; markaz uchun amal neytral. Ikkala tomon ham BITTA hisoblagichdan —
/// <see cref="PerLesson"/> (sof funksiya) va uning yuklovchisi <see cref="PerLessonBatchAsync"/> —
/// foydalanadi. Ilgari to'rtta ayri formula bor edi (o'rinbosarga "hisoblangan × foiz", asosiydan
/// "yig'ilgan × foiz", ro'yxatda uchinchisi, modalda to'rtinchisi) va ekranlar bir-biriga
/// mos kelmasdi. Batafsil: <c>.claude/rules/substitute-teachers.md</c>.</para>
/// </summary>
public static class SubstituteTeacherService
{
    /// <summary>Audit yozuvining `EntityType`i — <see cref="AuditSections"/> da "teachers" bo'limiga xaritalangan.</summary>
    public const string AuditEntityType = "substitute_teacher";

    /// <summary>O'zbekcha oy nomlari (audit jumlasi uchun; indeks 1..12).</summary>
    private static readonly string[] MonthNames =
    {
        "", "yanvar", "fevral", "mart", "aprel", "may", "iyun",
        "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr"
    };

    /// <summary>Audit jumlasida sanalar ro'yxati shu sondan oshsa "boshi — oxiri, N kun" deb qisqartiriladi.</summary>
    private const int MaxDatesInSummary = 5;

    /// <summary>Ko'chirish yo'q holat uchun bo'sh ro'yxat (har chaqiruvda yangi massiv yaratilmasin).</summary>
    private static readonly IReadOnlyList<JournalService.LessonMove> NoMoves = Array.Empty<JournalService.LessonMove>();

    // =============================================================================================
    //  CHEGARALAR VA OYNALAR — hammasi NOMLANGAN konstanta (sehrli son yozilmaydi)
    // =============================================================================================

    /// <summary>
    /// TUZATISH OYNASI (kun): o'rinbosar o'zi o'tgan darsning davomat/bahosini SHU KUNDAN keyin
    /// necha kun davomida tuzata oladi.
    /// <para>⚠️ NEGA 0 EMAS: o'rinbosar dars o'tib, kechqurun uyda jurnalni to'ldirishi odatiy hol;
    /// tayinlov tugagan zahoti yozuv yopilsa, u o'z xatosini tuzata olmay qolardi va tuzatish
    /// asosiy o'qituvchi (dars o'tmagan odam) zimmasiga tushardi.</para>
    /// <para>⚠️ NEGA CHEKSIZ EMAS: tayinlov tugagach o'rinbosar begona guruhning ISTALGAN o'tgan
    /// kunidagi baho va davomatini o'zgartira olardi — bu modulning eng katta xavfsizlik teshigi edi.</para>
    /// </summary>
    public const int EditWindowDays = 3;

    /// <summary>
    /// O'qituvchi ilovasida begona guruh RO'YXATDA ko'rinadigan oldindan kunlar soni: o'rinbosar
    /// ertaga o'tadigan darsiga tayyorlanishi uchun guruhni (o'quv dasturi, o'quvchilar) oldindan
    /// ko'ra oladi. YOZISH bunga kirmaydi — yozish faqat <see cref="EditWindowDays"/> oynasida.
    /// </summary>
    public const int UpcomingDays = 7;

    /// <summary>Bitta tayinlovdagi eng ko'p dars sanasi. Ilgari chegara umuman yo'q edi — API'ga
    /// to'g'ridan-to'g'ri murojaat qilib 1000 ta sana yuborish va shuncha darsga haq yozdirish mumkin edi.</summary>
    public const int MaxDates = 60;

    /// <summary>
    /// O'TMISHGA nechta kungacha tayinlash mumkin. ⚠️ Butunlay taqiqlanmaydi: "kecha kasal bo'lib
    /// qoldi, bugun rasmiylashtiramiz" — odatiy hol. Lekin oyning maoshi yopilgandan keyin orqaga
    /// qarab tayinlov qo'shish maosh varaqasini jimgina o'zgartirib yuborardi.
    /// </summary>
    public const int MaxBackdateDays = 14;

    /// <summary>Ro'yxat so'rovida qaytariladigan eng ko'p qator (audit <c>MaxLimit</c> bilan bir xil g'oya).
    /// Qirqilgani foydalanuvchidan YASHIRILMAYDI — jami son ham qaytadi.</summary>
    public const int MaxRows = 500;

    // =============================================================================================
    //  SANALAR — DARS KUNLARINING YAGONA MANBAI: JournalService.EffectiveLessonDatesInMonth
    // =============================================================================================

    /// <summary>Oyning oxirgi kuni ("yyyy-MM" → "yyyy-MM-31"). Buzuq oy uchun <c>null</c>.</summary>
    /// <remarks>
    /// ⚠️ Ilgari hamma joyda qattiq <c>"-28"</c> yozilgan edi: oyning 29/30/31-kunlaridagi darslar
    /// sanalmasdi, ya'ni maosh hovuzi kamroq darsga bo'linib, BITTA DARS NARXI sun'iy ravishda
    /// katta chiqardi va asosiy o'qituvchidan ortiqcha ayirilardi.
    /// </remarks>
    public static string? MonthEndDate(string? month)
    {
        if (month is null || month.Length < 7) return null;
        if (!int.TryParse(month[..4], out var y) || !int.TryParse(month[5..7], out var m)) return null;
        if (m is < 1 or > 12) return null;
        return $"{month[..7]}-{DateTime.DaysInMonth(y, m):D2}";
    }

    /// <summary>
    /// Guruhning SHU OYDAGI amaldagi dars sanalari.
    ///
    /// <para><b>YAGONA MANBA</b> — <see cref="JournalService.EffectiveLessonDatesInMonth"/>, ya'ni
    /// bir martalik KO'CHIRISHLAR (<c>LessonReschedules</c>) hisobga olinadi. ⚠️ Ilgari bu fayl
    /// hafta kuni mantig'ini qo'lda takrorlar (<c>((int)DayOfWeek + 6) % 7</c>) va ko'chirishlarni
    /// bilmasdi, <see cref="SalaryJournalStats"/> esa bilardi — natijada bitta oyning dars soni
    /// (maxraj) ikki xil chiqar va pul ikki xil hisoblanardi.</para>
    ///
    /// <para>Guruh CHEGARALARI ham qo'llanadi: <see cref="Group.StartDate"/> dan oldingi va guruh
    /// yopilgandan (<see cref="Group.EndDate"/> / <see cref="Group.ArchivedAt"/> dan ERTAROG'I)
    /// keyingi kunlar dars emas — arxivlangan guruhda "dars bor" deb pul to'lanmasin.</para>
    /// </summary>
    public static List<string> LessonDatesInMonth(
        Group? group, string? month, IReadOnlyList<JournalService.LessonMove>? moves = null)
    {
        if (month is null || month.Length < 7) return new();
        var m7 = month[..7];
        if (!int.TryParse(m7[..4], out var y) || !int.TryParse(m7[5..7], out var m) || m is < 1 or > 12)
            return new();

        List<string> dates;
        if (group?.Days is { Count: > 0 })
        {
            dates = JournalService.EffectiveLessonDatesInMonth(group.Days, m7, moves ?? NoMoves);
        }
        else
        {
            // Dars kunlari belgilanmagan guruh (yoki guruh umuman berilmagan) — oyning HAR kuni.
            // Tarixiy xulq: usiz bunday guruhda maxraj 0 bo'lib, pul umuman hisoblanmay qolardi.
            dates = new List<string>();
            for (var d = 1; d <= DateTime.DaysInMonth(y, m); d++)
                dates.Add($"{m7}-{d:D2}");
        }

        return ClipToGroupLifetime(group, dates);
    }

    /// <summary>Guruh boshlanishi/yopilishidan tashqaridagi sanalarni olib tashlaydi.</summary>
    private static List<string> ClipToGroupLifetime(Group? group, List<string> dates)
    {
        if (group is null) return dates;
        var start = group.StartDate is { Length: >= 10 } ? group.StartDate[..10] : null;
        var end = SalaryLedger.LessonEnd(group.EndDate, group.ArchivedAt);
        if (start is null && end is null) return dates;

        return dates.Where(d =>
            (start is null || string.CompareOrdinal(d, start) >= 0) &&
            (end is null || string.CompareOrdinal(d, end) <= 0)).ToList();
    }

    /// <summary>Oraliqdagi (ikki chegara ham kiradi) guruh dars sanalari.</summary>
    public static List<string> ScheduledDatesBetween(
        Group? group, DateOnly start, DateOnly end, IReadOnlyList<JournalService.LessonMove>? moves = null)
    {
        var result = new List<string>();
        if (end < start) return result;

        var startStr = start.ToString("yyyy-MM-dd");
        var endStr = end.ToString("yyyy-MM-dd");

        for (var cur = new DateOnly(start.Year, start.Month, 1); cur <= end; cur = cur.AddMonths(1))
            foreach (var d in LessonDatesInMonth(group, cur.ToString("yyyy-MM"), moves))
                if (string.CompareOrdinal(d, startStr) >= 0 && string.CompareOrdinal(d, endStr) <= 0)
                    result.Add(d);

        return result;
    }

    /// <summary>
    /// Sana oralig'ida guruhning dars kunlariga mos keladigan darslar soni.
    /// <para>⚠️ Natija KAMIDA 1 (tarixiy xulq: bitta kunlik tayinlov dars kunida bo'lmasa ham
    /// "bitta dars" deb ko'rsatiladi). Oyga taqsimlashda bu clamp ZARARLI bo'lgani uchun
    /// <see cref="LessonsInMonth"/> undan foydalanmaydi.</para>
    /// </summary>
    public static int CalculateScheduledLessons(
        Group? group, string startDateStr, string? endDateStr,
        IReadOnlyList<JournalService.LessonMove>? moves = null)
    {
        if (!DateOnly.TryParse(startDateStr, out var start))
            return 1;

        var end = start;
        if (!string.IsNullOrEmpty(endDateStr) && DateOnly.TryParse(endDateStr, out var parsedEnd) && parsedEnd >= start)
            end = parsedEnd;

        var count = ScheduledDatesBetween(group, start, end, moves).Count;
        return count > 0 ? count : 1;
    }

    /// <summary>
    /// Guruhning butun OYDAGI amaldagi darslari soni — <b>bitta dars narxining MAXRAJI</b>.
    /// Ko'chirilgan darslar bilan (<see cref="LessonDatesInMonth"/>).
    /// </summary>
    public static int ScheduledLessonsInMonth(
        Group? group, string? month, IReadOnlyList<JournalService.LessonMove>? moves = null) =>
        LessonDatesInMonth(group, month, moves).Count;

    /// <summary>
    /// Tayinlovning DARS SANALARI: <see cref="SubstituteTeacherAssignment.SelectedDates"/> bor bo'lsa
    /// AYNAN o'sha (yagona haqiqat manbai), aks holda <c>Date</c>..<c>EndDate</c> oralig'idagi
    /// guruh dars kunlari.
    /// </summary>
    public static List<string> EffectiveDates(
        SubstituteTeacherAssignment a, Group? group = null,
        IReadOnlyList<JournalService.LessonMove>? moves = null)
    {
        if (a.SelectedDates is { Count: > 0 })
            return a.SelectedDates.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
                    .Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();

        if (!DateOnly.TryParse(a.Date, out var start)) return new();
        var end = start;
        if (!string.IsNullOrEmpty(a.EndDate) && DateOnly.TryParse(a.EndDate, out var pe) && pe >= start)
            end = pe;
        return ScheduledDatesBetween(group, start, end, moves);
    }

    /// <summary>
    /// Tayinlov shu SANANI qamrab oladimi — <b>kirish huquqining yagona qoidasi</b>.
    /// <para>⚠️ SelectedDates to'ldirilgan bo'lsa FAQAT o'sha kunlar. Ilgari tekshiruv
    /// <c>Date</c>..<c>EndDate</c> ORALIG'I bo'yicha edi: admin 5 va 20-avgustni tanlasa,
    /// o'rinbosar oradagi 14 kun davomida ham begona guruhning jurnaliga (davomat, baholar,
    /// o'quvchilar ro'yxati) kira olardi.</para>
    /// </summary>
    public static bool CoversDate(SubstituteTeacherAssignment a, string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return false;
        date = date.Trim();

        if (a.SelectedDates is { Count: > 0 })
            return a.SelectedDates.Any(d => d is not null && d.Trim() == date);

        if (string.IsNullOrEmpty(a.EndDate))
            return a.Date == date;

        return string.CompareOrdinal(a.Date, date) <= 0 && string.CompareOrdinal(a.EndDate, date) >= 0;
    }

    /// <summary>
    /// Tayinlov shu sanadagi ishni O'ZGARTIRISHGA ruxsat beradimi (jurnal, davomat, baho).
    /// <para>Ikki shart: (1) tayinlov AYNAN shu kunni qamraydi; (2) bugun shu kundan
    /// <see cref="EditWindowDays"/> kun ichida (kelajakka yozib bo'lmaydi).</para>
    /// <para>⚠️ <paramref name="today"/> — PARAMETR: qoida sof funksiya bo'lib qolsin va testda
    /// vaqtni surish uchun <see cref="AppClock"/> ga tegish kerak bo'lmasin.</para>
    /// </summary>
    public static bool CanWriteOn(SubstituteTeacherAssignment a, string? date, DateOnly today)
    {
        if (!CoversDate(a, date)) return false;
        if (!DateOnly.TryParse(date, out var d)) return false;
        var diff = today.DayNumber - d.DayNumber;
        return diff >= 0 && diff <= EditWindowDays;
    }

    /// <summary>Sana ko'rish oynasida (o'tmishda <see cref="EditWindowDays"/>, kelajakda
    /// <see cref="UpcomingDays"/>) turibdimi.</summary>
    private static bool WithinViewWindow(string? date, DateOnly today)
    {
        if (!DateOnly.TryParse(date, out var d)) return false;
        var diff = d.DayNumber - today.DayNumber;
        return diff >= -EditWindowDays && diff <= UpcomingDays;
    }

    /// <summary>
    /// Tayinlovning AYNAN SHU OYGA tushadigan darslari soni.
    /// <para>⚠️ Ilgari <c>Date[..7] == month</c> edi — 25-avgustdan 5-sentabrgacha tayinlov
    /// BUTUNLAY avgustga yozilib, sentabr maoshida umuman ko'rinmasdi.</para>
    /// </summary>
    public static int LessonsInMonth(
        SubstituteTeacherAssignment a, Group? group, string? month,
        IReadOnlyList<JournalService.LessonMove>? moves = null)
    {
        if (month is null || month.Length < 7) return 0;
        var m7 = month[..7];

        if (a.SelectedDates is { Count: > 0 })
            return a.SelectedDates.Count(d => d is { Length: >= 7 } && d[..7] == m7);

        var end = MonthEndDate(m7);
        if (end is null) return 0;

        if (!DateOnly.TryParse(a.Date, out var start)) return 0;
        var last = start;
        if (!string.IsNullOrEmpty(a.EndDate) && DateOnly.TryParse(a.EndDate, out var pe) && pe >= start)
            last = pe;

        var mStart = DateOnly.Parse($"{m7}-01");
        var mEnd = DateOnly.Parse(end);
        if (last < mStart || start > mEnd) return 0;

        // Oraliqning shu oyga TUSHGAN qismi (clamp YO'Q: tushmasa 0 bo'lishi kerak).
        return ScheduledDatesBetween(group, start > mStart ? start : mStart, last < mEnd ? last : mEnd, moves).Count;
    }

    /// <summary>Tayinlov tegadigan oylar ("yyyy-MM"), takrorsiz va tartiblangan.</summary>
    public static List<string> MonthsOf(
        SubstituteTeacherAssignment a, Group? group = null,
        IReadOnlyList<JournalService.LessonMove>? moves = null) =>
        EffectiveDates(a, group, moves)
            .Where(d => d.Length >= 7).Select(d => d[..7])
            .Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();

    // =============================================================================================
    //  PUL — BITTA DARS NARXI (NOL YIG'INDILI MODELNING YURAGI)
    // =============================================================================================

    /// <summary>Maosh rejimlari (guruh yoki o'qituvchi darajasida).</summary>
    public const string ModeGroupPercent = "group-percent";
    /// <summary>Guruhning O'ZIGA qat'iy summa berilgan.</summary>
    public const string ModeGroupFixed = "group-fixed";
    /// <summary>Guruh sozlanmagan, asosiy o'qituvchi FOIZLI (eski sozlama).</summary>
    public const string ModeLegacyPercent = "legacy-percent";
    /// <summary>Guruh sozlanmagan, asosiy o'qituvchi QAT'IY oyliqda (eski sozlama).</summary>
    public const string ModeLegacyFixed = "legacy-fixed";

    /// <summary>
    /// Bitta (guruh, oy) uchun bitta dars narxini hisoblashga kerak bo'lgan HAMMA narsa.
    /// Bazadan bir marta yuklanadi (<see cref="PerLessonBatchAsync"/>), qoidaning O'ZI esa
    /// <see cref="PerLesson"/> sof funksiyasida — shuning uchun testlanadi.
    /// </summary>
    /// <param name="MonthLessons">Guruhning shu oydagi HAQIQIY dars soni (ko'chirishlar bilan) — MAXRAJ.</param>
    /// <param name="CollectedInMonth">Shu guruhga shu oy uchun YIG'ILGAN pul (foizli rejim bazasi).</param>
    /// <param name="LegacyTotalLessons">Legacy-QAT'IY rejim uchun: asosiy o'qituvchining BARCHA
    /// guruhlaridagi shu oydagi darslar yig'indisi (qat'iy oylik shularga bo'linadi).</param>
    public sealed record SalaryContext(
        Group? Group,
        Teacher? OriginalTeacher,
        int MonthLessons,
        decimal CollectedInMonth,
        int LegacyTotalLessons,
        int ActiveStudents);

    /// <summary>Bitta (guruh, oy) uchun natija: dars narxi va uni tushuntiradigan raqamlar.</summary>
    public sealed record PerLessonResult(
        decimal PerLessonFee, int MonthLessons, decimal GroupPool, int ActiveStudents,
        string Mode, string? Warning)
    {
        public static readonly PerLessonResult Empty =
            new(0m, 0, 0m, 0, ModeLegacyFixed, "Guruh topilmadi");
    }

    /// <summary>
    /// <b>YAGONA HISOBLAGICH.</b> Guruhning shu oydagi maosh ULUSHI ÷ oyning HAQIQIY dars soni.
    ///
    /// <para>O'rinbosarga to'lanadigan summa ham, asosiy o'qituvchidan ushlanadigan summa ham
    /// AYNAN shu qiymatdan chiqadi — shuning uchun model NOL YIG'INDILI: markaz na yutadi,
    /// na yo'qotadi.</para>
    ///
    /// <list type="bullet">
    ///   <item><b>guruh foizli</b> — hovuz = shu guruhga shu oyda YIG'ILGAN pul × guruh foizi;</item>
    ///   <item><b>guruh qat'iy</b> — hovuz = guruhning qat'iy summasi;</item>
    ///   <item><b>legacy foizli</b> — hovuz = yig'ilgan pul × o'qituvchi foizi;</item>
    ///   <item><b>legacy qat'iy</b> — hovuz = qat'iy oylik × (shu guruh darslari ÷ o'qituvchining
    ///     BARCHA guruhlaridagi darslar): qat'iy oylik bitta guruhga tegishli emas, u hamma
    ///     guruhlar uchun to'lanadi.</item>
    /// </list>
    ///
    /// <para>⚠️ Ilgari o'rinbosarning haqi <c>MonthlyFee × o'quvchi × foiz</c> ("HISOBLANGAN")
    /// dan, asosiydan ushlanma esa "YIG'ILGAN × foiz" dan hisoblanardi. Yig'ilmagan qarz bor
    /// oyda o'rinbosarga ko'proq to'lanib, asosiydan kamroq ushlanardi — farqni markaz to'lardi.</para>
    /// </summary>
    public static PerLessonResult PerLesson(SalaryContext ctx)
    {
        var g = ctx.Group;
        if (g is null) return PerLessonResult.Empty;

        var teacherMode = ctx.OriginalTeacher?.SalaryMode == "percent" ? "percent" : "fixed";
        var mode = g.TeacherSalaryMode == "percent" ? ModeGroupPercent
                 : g.TeacherSalaryMode == "fixed" ? ModeGroupFixed
                 : teacherMode == "percent" ? ModeLegacyPercent
                 : ModeLegacyFixed;

        if (ctx.MonthLessons <= 0)
            return new PerLessonResult(0m, 0, 0m, ctx.ActiveStudents, mode,
                "Guruhda bu oyda dars kuni yo'q — bitta dars narxini hisoblab bo'lmaydi");

        decimal pool;
        string? warning = null;

        switch (mode)
        {
            case ModeGroupFixed:
                pool = g.TeacherSalaryFixed;
                if (pool <= 0) warning = "Guruhga qat'iy maosh summasi kiritilmagan — haq 0 bo'ladi";
                break;

            case ModeLegacyFixed:
                // Qat'iy oylik BARCHA guruhlarga tegishli: bitta dars narxi = oylik ÷ hamma darslar.
                var total = ctx.LegacyTotalLessons > 0 ? ctx.LegacyTotalLessons : ctx.MonthLessons;
                var salary = ctx.OriginalTeacher?.Salary ?? 0m;
                pool = total > 0 ? salary * ctx.MonthLessons / total : 0m;
                if (salary <= 0) warning = "Asosiy o'qituvchining qat'iy oyligi kiritilmagan — haq 0 bo'ladi";
                break;

            default:   // ModeGroupPercent | ModeLegacyPercent
                var pct = mode == ModeGroupPercent
                    ? g.TeacherSalaryPercent
                    : (ctx.OriginalTeacher?.SalaryPercent ?? 0m);
                pool = decimal.Round(ctx.CollectedInMonth * pct / 100m, 2);
                if (ctx.ActiveStudents == 0)
                    warning = "Guruhda faol o'quvchi yo'q — bu oyda yig'ilgan puldan haq chiqmaydi";
                else if (ctx.CollectedInMonth <= 0)
                    warning = "Bu oyda guruhdan hali pul yig'ilmagan — haq 0 bo'ladi " +
                              "(pul kelgach maosh varaqasida o'zi paydo bo'ladi)";
                break;
        }

        if (pool < 0) pool = 0m;
        return new PerLessonResult(
            decimal.Round(pool / ctx.MonthLessons, 2), ctx.MonthLessons, decimal.Round(pool, 2),
            ctx.ActiveStudents, mode, warning);
    }

    /// <summary>
    /// (guruh, oy) → bitta dars narxi. <b>Bitta yuklovchi</b>: "O'rinbosarlar" ro'yxati, admin
    /// modalining jonli hisobi (<c>/preview</c>), o'rinbosarning maosh varaqasi va asosiy
    /// o'qituvchidan ushlanma — hammasi shuni chaqiradi.
    ///
    /// <para>⚠️ O'rinbosarning maoshini hisoblash uchun BEGONA guruhning (o'zi o'qitmaydigan
    /// guruhning) yig'ilgan puli va maosh rejimi kerak bo'ladi — shuning uchun bu funksiya
    /// guruh, uning asosiy o'qituvchisi va (legacy-qat'iy uchun) o'sha o'qituvchining boshqa
    /// guruhlarini ham yuklaydi.</para>
    /// </summary>
    public static async Task<Dictionary<(string GroupId, string Month), PerLessonResult>> PerLessonBatchAsync(
        IAppDbContext db, IReadOnlyCollection<(string GroupId, string Month)> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<(string, string), PerLessonResult>();
        if (keys.Count == 0) return result;

        var groupIds = keys.Select(k => k.GroupId).Distinct().ToList();
        var months = keys.Select(k => k.Month).Where(m => m is { Length: >= 7 })
            .Select(m => m[..7]).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();
        if (months.Count == 0) return result;

        var groups = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        var teacherIds = groups.Values.Select(g => g.TeacherId)
            .Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        // Legacy-QAT'IY rejim uchun asosiy o'qituvchining BARCHA guruhlari kerak (qat'iy oylik
        // shular orasida bo'linadi). Faqat kerak bo'lganda yuklanadi.
        var legacyFixedTeachers = teachers.Values
            .Where(t => t.SalaryMode != "percent")
            .Select(t => t.Id)
            .Where(id => groups.Values.Any(g => g.TeacherId == id && g.TeacherSalaryMode is not ("percent" or "fixed")))
            .ToList();
        var siblingGroups = legacyFixedTeachers.Count == 0
            ? new List<Group>()
            : await db.Classes.AsNoTracking().Where(g => legacyFixedTeachers.Contains(g.TeacherId)).ToListAsync(ct);

        var allGroupIds = groupIds.Concat(siblingGroups.Select(g => g.Id)).Distinct().ToList();
        var moves = await MovesByGroupAsync(db, allGroupIds, ct);

        // Foizli rejimlar uchun YIG'ILGAN pul — SalaryLedger bilan AYNAN bir xil taqsimot qoidasi
        // (teglangan to'lov 100% guruhga, teglanmagani MonthlyFee nisbatida). Faqat kerak bo'lsa.
        var needCollected = groups.Values.Any(g =>
            g.TeacherSalaryMode == "percent" ||
            (g.TeacherSalaryMode is not ("percent" or "fixed")
             && teachers.GetValueOrDefault(g.TeacherId)?.SalaryMode == "percent"));
        var collected = needCollected
            ? await SalaryLedger.CollectedForGroupsAsync(db, groupIds, months[0], months[^1])
            : new Dictionary<(string month, string groupId), decimal>();

        var studentCounts = await ActiveStudentCountsAsync(db, groupIds, ct);

        foreach (var (groupId, monthRaw) in keys.Distinct())
        {
            if (monthRaw is not { Length: >= 7 }) continue;
            var month = monthRaw[..7];
            var g = groups.GetValueOrDefault(groupId);

            var monthLessons = ScheduledLessonsInMonth(g, month, moves.GetValueOrDefault(groupId));
            var legacyTotal = 0;
            if (g is not null && !string.IsNullOrEmpty(g.TeacherId))
                foreach (var sib in siblingGroups.Where(s => s.TeacherId == g.TeacherId))
                    legacyTotal += ScheduledLessonsInMonth(sib, month, moves.GetValueOrDefault(sib.Id));

            result[(groupId, month)] = PerLesson(new SalaryContext(
                Group: g,
                OriginalTeacher: g is null ? null : teachers.GetValueOrDefault(g.TeacherId),
                MonthLessons: monthLessons,
                CollectedInMonth: collected.GetValueOrDefault((month, groupId), 0m),
                LegacyTotalLessons: legacyTotal,
                ActiveStudents: studentCounts.GetValueOrDefault(groupId, 0)));
        }

        return result;
    }

    /// <summary>Guruhlarning bir martalik dars ko'chirishlari (guruh → ko'chirishlar).</summary>
    public static async Task<Dictionary<string, IReadOnlyList<JournalService.LessonMove>>> MovesByGroupAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return new();
        var rows = await db.LessonReschedules.AsNoTracking()
            .Where(r => groupIds.Contains(r.ClassId))
            .Select(r => new { r.ClassId, r.FromDate, r.ToDate })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ClassId).ToDictionary(
            grp => grp.Key,
            grp => (IReadOnlyList<JournalService.LessonMove>)grp
                .Select(r => new JournalService.LessonMove(r.FromDate, r.ToDate)).ToList());
    }

    /// <summary>Guruhlarning FAOL o'quvchilari soni — bitta guruhlangan so'rov (N+1 emas).</summary>
    public static async Task<Dictionary<string, int>> ActiveStudentCountsAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, CancellationToken ct = default)
    {
        if (groupIds.Count == 0) return new();
        return await db.StudentGroups.AsNoTracking()
            .Where(sg => groupIds.Contains(sg.GroupId) && sg.IsActive)
            .GroupBy(sg => sg.GroupId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

    // =============================================================================================
    //  O'QISH
    // =============================================================================================

    /// <summary>
    /// O'rinbosar o'qituvchi tayinlovlari ro'yxati + JAMI son (chegara foydalanuvchidan yashirilmaydi).
    /// <para>⚠️ <paramref name="date"/> filtri IKKI QADAMDA: bazada qo'pol oraliq bo'yicha
    /// (indeksdan foydalanadi, provayderga bog'liq emas), keyin xotirada <see cref="CoversDate"/>
    /// bilan aniq. Sabab: <c>SelectedDates</c> — massiv ustun, uning ichidan qidirish PostgreSQL'da
    /// ishlaydi-yu SQLite testlarida ishlamaydi (audit qoidasidagi <c>ILike</c> bilan bir xil hol).</para>
    /// </summary>
    public static async Task<(List<SubstituteTeacherAssignmentDto> Items, int Total)> GetAssignmentsPageAsync(
        IAppDbContext db,
        string? groupId = null,
        string? teacherId = null,
        string? date = null,
        bool? isActive = null,
        int limit = MaxRows,
        CancellationToken ct = default)
    {
        var query = db.SubstituteTeacherAssignments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(groupId))
            query = query.Where(a => a.GroupId == groupId);

        if (!string.IsNullOrWhiteSpace(teacherId))
            query = query.Where(a => a.SubstituteTeacherId == teacherId || a.OriginalTeacherId == teacherId);

        if (!string.IsNullOrWhiteSpace(date))
        {
            // Qo'pol (superset) filtr: SelectedDates har doim Date..EndDate ichida yotadi.
            query = query.Where(a =>
                (a.EndDate == null && a.Date == date) ||
                (a.EndDate != null && string.Compare(a.Date, date) <= 0 && string.Compare(a.EndDate, date) >= 0));
        }

        if (isActive.HasValue)
            query = query.Where(a => a.IsActive == isActive.Value);

        // ⚠️ CHEGARA: ilgari `ToListAsync()` xom holda edi — bir necha yildan keyin butun jadval
        // xotiraga yig'ilardi (loyihada boshqa joylarda ochiq chegara bor: audit MaxLimit = 500).
        var cap = limit is <= 0 or > MaxRows ? MaxRows : limit;
        var total = await query.CountAsync(ct);
        var list = await query.OrderByDescending(a => a.CreatedAt).Take(cap).ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(date))
            list = list.Where(a => CoversDate(a, date)).ToList();   // aniq filtr

        if (list.Count == 0) return (new(), total);

        var groupIds = list.Select(a => a.GroupId).Distinct().ToList();
        var teacherIds = list.SelectMany(a => new[] { a.OriginalTeacherId, a.SubstituteTeacherId }).Distinct().ToList();

        var groups = await db.Classes.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g, ct);

        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t, ct);

        var moves = await MovesByGroupAsync(db, groupIds, ct);
        var studentCounts = await ActiveStudentCountsAsync(db, groupIds, ct);

        // (guruh, oy) juftliklari — bitta ommaviy hisob (assignment boshiga so'rov YO'Q).
        var keys = new HashSet<(string, string)>();
        foreach (var a in list)
            foreach (var m in MonthsOf(a, groups.GetValueOrDefault(a.GroupId), moves.GetValueOrDefault(a.GroupId)))
                keys.Add((a.GroupId, m));
        var rates = await PerLessonBatchAsync(db, keys, ct);

        var resultList = new List<SubstituteTeacherAssignmentDto>();
        foreach (var a in list)
        {
            var g = groups.GetValueOrDefault(a.GroupId);
            var mv = moves.GetValueOrDefault(a.GroupId);
            var subTeacher = teachers.GetValueOrDefault(a.SubstituteTeacherId);
            var origTeacher = teachers.GetValueOrDefault(a.OriginalTeacherId);

            int lessonCount = EffectiveDates(a, g, mv).Count;
            int studentCount = g != null ? studentCounts.GetValueOrDefault(g.Id, 0) : 0;

            decimal estimatedSalary = 0m;
            foreach (var m in MonthsOf(a, g, mv))
            {
                var rate = rates.GetValueOrDefault((a.GroupId, m));
                if (rate is null) continue;
                estimatedSalary += Math.Round(rate.PerLessonFee * LessonsInMonth(a, g, m, mv), 2);
            }
            estimatedSalary = Math.Round(estimatedSalary, 2);

            // Ko'rsatiladigan "bitta dars narxi" — jami ÷ darslar soni (tayinlov ikki oyga tushsa
            // oylarning stavkasi har xil bo'lishi mumkin, shuning uchun O'RTACHA ko'rsatiladi).
            decimal singleRate = lessonCount > 0 ? Math.Round(estimatedSalary / lessonCount, 2) : 0m;

            resultList.Add(new SubstituteTeacherAssignmentDto(
                Id: a.Id,
                GroupId: a.GroupId,
                GroupName: g?.Name ?? "Noma'lum guruh",
                OriginalTeacherId: a.OriginalTeacherId,
                OriginalTeacherName: origTeacher?.FullName ?? "Noma'lum o'qituvchi",
                SubstituteTeacherId: a.SubstituteTeacherId,
                SubstituteTeacherName: subTeacher?.FullName ?? "Noma'lum o'qituvchi",
                Date: a.Date,
                EndDate: a.EndDate,
                Reason: a.Reason,
                CreatedBy: a.CreatedBy,
                CreatedAt: a.CreatedAt,
                IsActive: a.IsActive,
                LessonCount: lessonCount,
                EstimatedSalary: estimatedSalary,
                Dates: a.SelectedDates,
                PerLessonFee: singleRate,
                // NOL YIG'INDILI: asosiy o'qituvchidan AYNAN o'shancha ushlanadi.
                EstimatedDeduction: estimatedSalary,
                StudentCount: studentCount));
        }

        return (resultList, total);
    }

    /// <summary>Ro'yxatning qisqa shakli (jami son kerak bo'lmagan joylar uchun).</summary>
    public static async Task<List<SubstituteTeacherAssignmentDto>> GetAssignmentsAsync(
        IAppDbContext db,
        string? groupId = null,
        string? teacherId = null,
        string? date = null,
        bool? isActive = null,
        CancellationToken ct = default) =>
        (await GetAssignmentsPageAsync(db, groupId, teacherId, date, isActive, MaxRows, ct)).Items;

    /// <summary>
    /// Guruhning ko'rsatilgan oydagi rejalashtirilgan dars sanalarini olish (modal uchun).
    /// </summary>
    public static async Task<List<GroupLessonDateDto>> GetGroupLessonDatesAsync(
        IAppDbContext db, string groupId, string month, CancellationToken ct = default)
    {
        var group = await db.Classes.FindAsync(new object[] { groupId }, ct);
        if (group is null || group.Days is null || group.Days.Count == 0) return new();

        if (string.IsNullOrWhiteSpace(month) || month.Length < 7)
            month = AppClock.Today.ToString("yyyy-MM");

        var moves = (await MovesByGroupAsync(db, new[] { groupId }, ct)).GetValueOrDefault(groupId);
        var effectiveDates = LessonDatesInMonth(group, month[..7], moves);

        if (!int.TryParse(month[..4], out var year) || !int.TryParse(month[5..7], out var mNum))
            return new();

        string[] weekDayNames = { "Yak", "Dush", "Sesh", "Chorsh", "Paysh", "Juma", "Shanba" };

        var result = new List<GroupLessonDateDto>();
        foreach (var dateStr in effectiveDates)
        {
            if (!DateOnly.TryParse(dateStr, out var dateOnly)) continue;
            var dayName = weekDayNames[(int)dateOnly.DayOfWeek];
            result.Add(new GroupLessonDateDto(dateStr, $"{dateOnly.Day}-{mNum:D2} ({dayName})", true));
        }
        return result;
    }

    /// <summary>
    /// ID bo'yicha tayinlovni olish.
    /// </summary>
    public static async Task<SubstituteTeacherAssignmentDto?> GetByIdAsync(
        IAppDbContext db, string id, CancellationToken ct = default)
    {
        var item = await db.SubstituteTeacherAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return null;

        var list = await GetAssignmentsAsync(db, groupId: item.GroupId, teacherId: null, date: null, isActive: null, ct: ct);
        return list.FirstOrDefault(a => a.Id == id);
    }

    // =============================================================================================
    //  KIRISH HUQUQI
    // =============================================================================================

    // ⚠️ DARVOZA IKKITA, KO'P EMAS: `CanSubstituteWriteAsync` (yozish, SANA + tuzatish oynasi) va
    // `CanSubstituteReadAsync` (o'qish, ko'rish oynasi). Ilgari bu yerda uchinchisi bor edi —
    // `IsSubstituteForGroupAsync`, u sanani tekshirar, lekin tuzatish oynasini BILMASDI. Uni
    // qaytarib qo'shmang: aynan shunday "yonma-yon ikkinchi qoida" modulning asosiy kasalligi edi.

    /// <summary>
    /// O'qituvchi shu guruhda shu SANADAGI ishni O'ZGARTIRA oladimi (davomat, baho, jurnal).
    /// <see cref="CanWriteOn"/> qoidasi: tayinlov shu kunni qamraydi VA bugun tuzatish oynasida.
    /// </summary>
    public static async Task<bool> CanSubstituteWriteAsync(
        IAppDbContext db, string teacherId, string groupId, string? date,
        DateOnly? today = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacherId) || string.IsNullOrWhiteSpace(groupId)
            || string.IsNullOrWhiteSpace(date)) return false;

        var d = date.Trim();
        var candidates = await ActiveOnDateQuery(db, d)
            .Where(a => a.GroupId == groupId && a.SubstituteTeacherId == teacherId)
            .ToListAsync(ct);

        var now = today ?? AppClock.Today;
        return candidates.Any(a => CanWriteOn(a, d, now));
    }

    /// <summary>
    /// O'qituvchi shu guruhni UMUMAN ko'ra oladimi (o'qish): tayinlovi ko'rish oynasidagi
    /// (o'tmishda <see cref="EditWindowDays"/>, kelajakda <see cref="UpcomingDays"/>) biror kunni
    /// qamrasa. Guruhlar RO'YXATI ham, guruh ichidagi GET'lar ham AYNAN shu qoidada.
    /// </summary>
    public static async Task<bool> CanSubstituteReadAsync(
        IAppDbContext db, string teacherId, string groupId,
        DateOnly? today = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacherId) || string.IsNullOrWhiteSpace(groupId)) return false;
        var ids = await SubstituteGroupIdsAsync(db, teacherId, today: today, ct: ct);
        return ids.Contains(groupId);
    }

    /// <summary>
    /// O'qituvchi o'rinbosar bo'lgan va HOZIR ko'rinishi kerak bo'lgan guruhlar ro'yxati
    /// ("Mening guruhlarim"). <see cref="CanSubstituteReadAsync"/> bilan AYNAN bir xil qoidada —
    /// ilgari ro'yxat va guruhga kirish darvozasi ikki xil ishlar edi.
    /// </summary>
    public static async Task<List<string>> SubstituteGroupIdsAsync(
        IAppDbContext db, string teacherId, string? date = null,
        DateOnly? today = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacherId)) return new();

        // Aniq SANA berilgan bo'lsa — faqat o'sha kun (eski, qat'iy semantika).
        if (!string.IsNullOrWhiteSpace(date))
        {
            var one = await ActiveOnDateQuery(db, date)
                .Where(a => a.SubstituteTeacherId == teacherId).ToListAsync(ct);
            return one.Where(a => CoversDate(a, date)).Select(a => a.GroupId).Distinct().ToList();
        }

        var now = today ?? AppClock.Today;
        var from = now.AddDays(-EditWindowDays).ToString("yyyy-MM-dd");
        var to = now.AddDays(UpcomingDays).ToString("yyyy-MM-dd");

        // Qo'pol (superset) filtr: oraliqlari umuman kesishmaydiganlar bazadayoq tashlanadi.
        var candidates = await db.SubstituteTeacherAssignments.AsNoTracking()
            .Where(a => a.IsActive
                        && string.Compare(a.Date, to) <= 0
                        && string.Compare(a.EndDate ?? a.Date, from) >= 0
                        && a.SubstituteTeacherId == teacherId)
            .ToListAsync(ct);

        return candidates
            .Where(a => EffectiveDates(a).Any(d => WithinViewWindow(d, now)))
            .Select(a => a.GroupId).Distinct().ToList();
    }

    /// <summary>Qo'pol (superset) so'rov: faol va sanasi <c>Date</c>..<c>EndDate</c> oralig'ida.</summary>
    private static IQueryable<SubstituteTeacherAssignment> ActiveOnDateQuery(IAppDbContext db, string date) =>
        db.SubstituteTeacherAssignments.AsNoTracking().Where(a =>
            a.IsActive &&
            ((a.EndDate == null && a.Date == date) ||
             (a.EndDate != null && string.Compare(a.Date, date) <= 0 && string.Compare(a.EndDate, date) >= 0)));

    // =============================================================================================
    //  TEKSHIRUV — server QABUL QILGAN hamma narsani YOZMAYDI
    // =============================================================================================

    /// <summary>Tekshiruv natijasi: xato bo'lsa <c>Error</c> (o'zbekcha, foydalanuvchiga ko'rsatiladi).</summary>
    public sealed record Validated(
        string? Error, List<string> Dates, Group? Group, Teacher? Substitute,
        IReadOnlyList<JournalService.LessonMove>? Moves);

    /// <summary>
    /// Tayinlov so'rovini TO'LIQ tekshiradi — yaratish ham, jonli hisob (<c>/preview</c>) ham
    /// AYNAN shuni chaqiradi, ya'ni modal ko'rsatgan raqam va saqlanadigan tayinlov bir xil qoidada.
    ///
    /// <para>⚠️ Ilgari server so'rovda kelgan HAMMA narsani yozardi: buzuq sana (<c>2026-13-99</c>),
    /// guruhning dars kuni bo'lmagan sana, bir yil oldingi sana, 1000 ta sana, arxivlangan guruh,
    /// ishdan ketgan o'qituvchi. Pul esa TO'G'RIDAN-TO'G'RI sanalar sonidan chiqardi. Tekshiruv
    /// faqat frontendda edi — bu naqsh `.claude/rules/books.md` §2.1 da xato deb yozilgan.</para>
    /// </summary>
    public static async Task<Validated> ValidateAsync(
        IAppDbContext db, CreateSubstituteAssignmentRequest req, DateOnly? today = null, CancellationToken ct = default)
    {
        static Validated Fail(string message) => new(message, new(), null, null, null);

        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == req.GroupId, ct);
        if (group is null) return Fail("Guruh topilmadi");

        // Arxivlangan/yopilgan yoki vaqtincha bloklangan guruhga o'rinbosar tayinlanmaydi: u yerda
        // dars ham, pul ham yo'q (qoida — TeacherGroupAccess, o'qituvchi ilovasi bilan bitta manba).
        if (TeacherGroupAccess.HiddenReason(group) is { } hidden)
            return Fail(hidden == TeacherGroupAccess.ReasonBlocked
                ? "Guruh vaqtincha bloklangan — o'rinbosar biriktirib bo'lmaydi"
                : "Guruh arxivlangan (yopilgan) — o'rinbosar biriktirib bo'lmaydi");

        if (string.IsNullOrWhiteSpace(group.TeacherId))
            return Fail("Guruhda biriktirilgan asosiy o'qituvchi yo'q");

        var subTeacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.SubstituteTeacherId, ct);
        if (subTeacher is null) return Fail("O'rinbosar o'qituvchi topilmadi");
        if (subTeacher.IsArchived) return Fail("O'rinbosar o'qituvchi arxivlangan (ishdan ketgan)");
        if (subTeacher.IsBlocked) return Fail("O'rinbosar o'qituvchi vaqtincha faol emas");

        if (group.TeacherId == req.SubstituteTeacherId)
            return Fail("Asosiy o'qituvchi o'ziga o'rinbosar qilib tayinlanishi mumkin emas");

        var moves = (await MovesByGroupAsync(db, new[] { group.Id }, ct)).GetValueOrDefault(group.Id);

        // ---------- SANALAR ----------
        var raw = (req.Dates ?? new List<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct().ToList();

        List<string> selectedDates;
        if (raw.Count > 0)
        {
            // FORMAT: "2026-13-99" ni `DateOnly.TryParse` madaniyatga qarab qabul qilib yuborishi
            // mumkin, shuning uchun ISO shakli AYNAN qaytib chiqishi tekshiriladi.
            foreach (var d in raw)
                if (!DateOnly.TryParse(d, out var parsed) || parsed.ToString("yyyy-MM-dd") != d)
                    return Fail($"Sana formati noto'g'ri: \"{d}\" (kutilgan shakl: 2026-08-05)");

            selectedDates = raw.OrderBy(d => d, StringComparer.Ordinal).ToList();
        }
        else
        {
            // Oraliq bilan yaratish (API yo'li): SelectedDates guruhning HAQIQIY dars kunlari bilan
            // to'ldiriladi. Ilgari faqat [boshi, oxiri] yozilardi — 10 kunlik tayinlov "2 dars"
            // deb hisoblanardi va kirish huquqi esa butun oraliqqa berilardi.
            if (string.IsNullOrWhiteSpace(req.Date) || !DateOnly.TryParse(req.Date.Trim(), out var s))
                return Fail("Kamida bitta dars sanasi tanlanishi kerak");

            var e = s;
            if (!string.IsNullOrWhiteSpace(req.EndDate) && DateOnly.TryParse(req.EndDate.Trim(), out var pe) && pe >= s)
                e = pe;

            selectedDates = ScheduledDatesBetween(group, s, e, moves);
            if (selectedDates.Count == 0)
                return Fail("Tanlangan oraliqda guruhning dars kuni yo'q");
        }

        if (selectedDates.Count == 0)
            return Fail("Kamida bitta dars sanasi tanlanishi kerak");

        if (selectedDates.Count > MaxDates)
            return Fail($"Bir tayinlovda ko'pi bilan {MaxDates} ta dars sanasi bo'lishi mumkin " +
                        $"(tanlangan: {selectedDates.Count}). Uzoq muddat uchun guruhning asosiy " +
                        $"o'qituvchisini almashtiring.");

        var now = today ?? AppClock.Today;

        // O'TMISH: butunlay taqiqlanmaydi (kecha kasal bo'lgan o'qituvchini bugun rasmiylashtirish
        // odatiy hol), lekin yopilgan oy maoshini orqaga qarab o'zgartirish mumkin bo'lmasin.
        var oldest = selectedDates[0];
        if (DateOnly.TryParse(oldest, out var oldestDate)
            && now.DayNumber - oldestDate.DayNumber > MaxBackdateDays)
            return Fail($"{UzDate(oldest)} juda eski sana: o'rinbosarlikni orqaga qarab ko'pi bilan " +
                        $"{MaxBackdateDays} kungacha rasmiylashtirish mumkin (maosh varaqasi allaqachon yopilgan).");

        // HAR SANA guruhning HAQIQIY dars kuni bo'lishi shart — aks holda "dars o'tildi" deb pul
        // yozilardi, jurnalda esa unday kun umuman yo'q edi.
        var lessonDays = new HashSet<string>();
        foreach (var m in selectedDates.Select(d => d[..7]).Distinct())
            foreach (var d in LessonDatesInMonth(group, m, moves))
                lessonDays.Add(d);

        var notLessons = selectedDates.Where(d => !lessonDays.Contains(d)).ToList();
        if (notLessons.Count > 0)
            return Fail($"Bu kunlarda \"{group.Name}\" guruhida dars yo'q: {FormatDates(notLessons)}");

        return new Validated(null, selectedDates, group, subTeacher, moves);
    }

    // =============================================================================================
    //  JONLI HISOB (admin modali) — frontend pulni O'ZI hisoblamaydi
    // =============================================================================================

    /// <summary>
    /// Tanlangan sanalar uchun jonli hisob: nechta dars, bitta dars narxi, o'rinbosarga to'lanadigan
    /// va asosiydan ushlanadigan summa (NOL YIG'INDILI — ikkisi TENG).
    /// </summary>
    public static async Task<(string? Error, SubstituteFeePreviewDto? Preview)> PreviewAsync(
        IAppDbContext db, CreateSubstituteAssignmentRequest req, DateOnly? today = null, CancellationToken ct = default)
    {
        var v = await ValidateAsync(db, req, today, ct);
        if (v.Error is not null) return (v.Error, null);

        var group = v.Group!;
        var byMonth = v.Dates.GroupBy(d => d[..7]).ToList();
        var rates = await PerLessonBatchAsync(db, byMonth.Select(m => (group.Id, m.Key)).ToList(), ct);

        decimal total = 0m;
        string? warning = null;
        int monthLessons = 0;
        foreach (var m in byMonth)
        {
            var rate = rates.GetValueOrDefault((group.Id, m.Key)) ?? PerLessonResult.Empty;
            total += Math.Round(rate.PerLessonFee * m.Count(), 2);
            if (monthLessons == 0) monthLessons = rate.MonthLessons;
            warning ??= rate.Warning;
        }
        total = Math.Round(total, 2);

        var lessonCount = v.Dates.Count;
        var counts = await ActiveStudentCountsAsync(db, new[] { group.Id }, ct);

        return (null, new SubstituteFeePreviewDto(
            LessonCount: lessonCount,
            PerLessonFee: lessonCount > 0 ? Math.Round(total / lessonCount, 2) : 0m,
            EstimatedSalary: total,
            EstimatedDeduction: total,   // NOL YIG'INDILI: to'lanadi = ushlanadi
            StudentCount: counts.GetValueOrDefault(group.Id, 0),
            MonthLessons: monthLessons,
            Warning: warning));
    }

    // =============================================================================================
    //  YOZISH
    // =============================================================================================

    /// <summary>
    /// Yangi o'rinbosar o'qituvchi tayinlovini yaratish.
    /// <para><b>Audit yozuvi SHU YERDA</b>, <c>SaveChangesAsync</c> dan OLDIN qo'shiladi — ya'ni
    /// tayinlov bilan BITTA tranzaksiyada saqlanadi (`.claude/rules/audit.md` §1). Ilgari yozuv
    /// controllerda, servis allaqachon saqlab bo'lgandan KEYIN qo'shilardi va bazaga umuman
    /// tushmasdi.</para>
    /// </summary>
    public static async Task<(bool Ok, string Message, SubstituteTeacherAssignment? Assignment)> CreateAssignmentAsync(
        IAppDbContext db,
        CreateSubstituteAssignmentRequest req,
        string actorName,
        string? actorUserId = null,
        AuditService? audit = null,
        // ⚠️ "Bugun" PARAMETR sifatida: o'tmish oynasi (MaxBackdateDays) testda vaqtni surmasdan
        // tekshirilsin. null = AppClock.Today (ishlab chiqarishdagi odatiy yo'l).
        DateOnly? today = null,
        CancellationToken ct = default)
    {
        var v = await ValidateAsync(db, req, today, ct);
        if (v.Error is not null) return (false, v.Error, null);

        var group = v.Group!;
        var selectedDates = v.Dates;

        var entity = new SubstituteTeacherAssignment
        {
            GroupId = req.GroupId,
            OriginalTeacherId = group.TeacherId,
            SubstituteTeacherId = req.SubstituteTeacherId,
            Date = selectedDates[0],
            EndDate = selectedDates[^1],
            Reason = (req.Reason ?? "").Trim(),
            CreatedBy = actorName,
            CreatedById = actorUserId,
            CreatedAt = AppClock.Now,
            IsActive = true,
            SelectedDates = selectedDates
        };

        // KESISHUV: shu guruhda shu kunlarga allaqachon FAOL o'rinbosar bo'lsa — ikki marta haq
        // to'lanardi (tugma ikki marta bosilsa yoki ikki admin bir vaqtda tayinlasa).
        var busy = await BusyDatesAsync(db, req.GroupId, selectedDates, ct);
        if (busy.Count > 0)
            return (false, $"Bu guruhda quyidagi kunlarga allaqachon o'rinbosar biriktirilgan: " +
                           $"{FormatDates(busy)}. Avval eski tayinlovni bekor qiling.", null);

        db.SubstituteTeacherAssignments.Add(entity);

        audit?.Record(AuditEntityType, AuditEntityId(entity), "create",
            CreateSummary(entity, group, v.Substitute),
            // teacherId — O'RINBOSAR: haq aynan uning maoshiga qo'shiladi, ya'ni yozuv uning
            // kartochkasidagi "Tarix" bo'limida ko'rinishi kerak. Asosiy o'qituvchi ismi jumlada bor.
            teacherId: req.SubstituteTeacherId);

        await db.SaveChangesAsync(ct);

        return (true, "O'rinbosar o'qituvchi muvaffaqiyatli biriktirildi", entity);
    }

    /// <summary>Shu guruhda FAOL tayinlov bilan allaqachon band bo'lgan sanalar.</summary>
    private static async Task<List<string>> BusyDatesAsync(
        IAppDbContext db, string groupId, List<string> wanted, CancellationToken ct)
    {
        var first = wanted[0];
        var last = wanted[^1];

        // Qo'pol filtr: oraliqlari umuman kesishmaydiganlar bazadayoq tashlanadi.
        var existing = await db.SubstituteTeacherAssignments.AsNoTracking()
            .Where(a => a.GroupId == groupId && a.IsActive
                        && string.Compare(a.Date, last) <= 0
                        && string.Compare(a.EndDate ?? a.Date, first) >= 0)
            .ToListAsync(ct);

        return wanted.Where(d => existing.Any(a => CoversDate(a, d))).ToList();
    }

    /// <summary>
    /// Tayinlovni bekor qilish (IsActive = false).
    /// <para>Audit `action` = <c>delete</c>: ruxsat etilgan to'rt qiymatdan ("cancel" YO'Q) shu
    /// mos keladi — foydalanuvchi uchun bu tayinlovni BEKOR QILISH, ya'ni o'chirish amali
    /// (yozuv bazada qoladi, lekin hech qanday kunda kuchga ega emas). Endpoint ham HTTP DELETE.</para>
    /// </summary>
    public static async Task<(bool Ok, string Message)> CancelAssignmentAsync(
        IAppDbContext db, string id, AuditService? audit = null, CancellationToken ct = default)
    {
        var item = await db.SubstituteTeacherAssignments.FindAsync(new object[] { id }, ct);
        if (item is null)
            return (false, "Tayinlov topilmadi");

        if (!item.IsActive)
            return (false, "Tayinlov allaqachon bekor qilingan");

        item.IsActive = false;

        var group = await db.Classes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == item.GroupId, ct);
        var subTeacher = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == item.SubstituteTeacherId, ct);

        audit?.Record(AuditEntityType, AuditEntityId(item), "delete", CancelSummary(item, group, subTeacher),
            teacherId: item.SubstituteTeacherId);

        await db.SaveChangesAsync(ct);

        return (true, "O'rinbosar o'qituvchi tayinlovi bekor qilindi");
    }

    // =============================================================================================
    //  AUDIT — `EntityId` va JUMLA (foydalanuvchi tarixda FAQAT shuni o'qiydi, GUID yozilmaydi!)
    // =============================================================================================

    /// <summary>
    /// Audit yozuvining <c>EntityId</c>i: <c>"{groupId}:{assignmentId}"</c>.
    /// <para>⚠️ AYNAN <c>Membership</c> naqshi: <c>AuditController</c> ning <c>groupId</c> filtri
    /// <c>EntityId == groupId || EntityId.StartsWith(groupId + ":")</c> deb qidiradi. Ilgari bu
    /// yerda faqat tayinlov id'si turardi va yozuv GURUH sahifasidagi "Tarix" tabida hech qachon
    /// ko'rinmasdi — ya'ni "guruhda kim dars o'tgani" tarixda topilmasdi.</para>
    /// </summary>
    public static string AuditEntityId(SubstituteTeacherAssignment a) => $"{a.GroupId}:{a.Id}";

    /// <summary>"2026-08-12" → "12-avgust". Buzuq sana bo'lsa o'zi qaytadi.</summary>
    public static string UzDate(string? iso)
    {
        if (iso is null || !DateOnly.TryParse(iso, out var d)) return iso ?? "";
        return $"{d.Day}-{MonthNames[d.Month]}";
    }

    /// <summary>Sanalar ro'yxatini o'qiladigan matnga: ko'p bo'lsa "boshi — oxiri (N kun)".</summary>
    public static string FormatDates(IReadOnlyList<string> dates)
    {
        if (dates.Count == 0) return "sana ko'rsatilmagan";
        if (dates.Count <= MaxDatesInSummary) return string.Join(", ", dates.Select(UzDate));
        return $"{UzDate(dates[0])} — {UzDate(dates[^1])} (jami {dates.Count} kun)";
    }

    /// <summary>Tayinlash jumlasi: guruh NOMI, o'qituvchilar F.I.SH, SANALAR va sabab (GUID emas).</summary>
    public static string CreateSummary(SubstituteTeacherAssignment a, Group? group, Teacher? subTeacher)
    {
        var dates = EffectiveDates(a, group);
        var reason = string.IsNullOrWhiteSpace(a.Reason) ? "sabab ko'rsatilmagan" : $"sabab: {a.Reason.Trim()}";
        return $"\"{group?.Name ?? "Noma'lum guruh"}\" guruhiga {FormatDates(dates)} kunlari uchun " +
               $"({dates.Count} dars) o'rinbosar o'qituvchi {subTeacher?.FullName ?? "Noma'lum o'qituvchi"} " +
               $"biriktirildi ({reason})";
    }

    /// <summary>Bekor qilish jumlasi.</summary>
    public static string CancelSummary(SubstituteTeacherAssignment a, Group? group, Teacher? subTeacher)
    {
        var dates = EffectiveDates(a, group);
        return $"\"{group?.Name ?? "Noma'lum guruh"}\" guruhidagi o'rinbosar o'qituvchi " +
               $"{subTeacher?.FullName ?? "Noma'lum o'qituvchi"} tayinlovi bekor qilindi " +
               $"({FormatDates(dates)} kunlari)";
    }
}
